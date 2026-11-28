using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;
using Microsoft.AspNetCore.Antiforgery;
using ModelContextProtocol;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpClientApp;

/// <summary>
/// The assistant of the browser run: a page at the origin of the redirect URI with a button to sign in and one per
/// tool. Each button starts the MCP call here, with the token the SDK holds here, and the page shows its result and the
/// steps it took. The token never reaches the browser.
/// </summary>
/// <remarks>
/// A call runs in the background, one at a time. The request that started it waits for it to finish or for the SDK to
/// ask for a sign-in, whichever comes first: the sign-in needs the same browser, so the request answers with the way to
/// the provider while the call waits in <see cref="BrowserSignIn"/>. The redirect back hands over the code and waits
/// for the call in turn, then answers with the page.
/// </remarks>
public sealed class AssistantPage(
    string clientName,
    string trackerName,
    Uri tracker,
    string newTaskTitle,
    TimeSpan signInTimeout,
    StepLog log,
    BrowserSignIn signIn,
    TrackerTraffic traffic,
    Func<ITokenCache, HttpClientTransport> createTransport,
    ILoggerFactory loggerFactory)
{
    private const string Home = "/";
    private static readonly JsonSerializerOptions IndentedJson = new()
    {
        WriteIndented = true,

        // The page HTML-encodes the answer, so a title keeps its own characters instead of escapes
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly Lock _lock = new();
    private Task _operation = Task.CompletedTask;
    private CancellationTokenSource? _operationTimeout;
    private bool _cancelled;

    private HttpClientTransport? _transport;
    private McpClient? _client;
    private LoggedTokenCache? _tokens;

    // who_am_i's answer: what the tracker sees of the token
    private string? _access;
    private (string Call, string Result, bool Failed)? _last;

    public void Map(IEndpointRouteBuilder app, Uri redirectUri)
    {
        // Only on the assistant's own origin, not on the vendor's site that serves the metadata document
        var page = app.MapGroup("").RequireHost(redirectUri.Authority);
        page.MapGet(Home, Render);
        page.MapPost("/sign-in", SignInAsync);
        page.MapPost("/call", CallAsync);
        page.MapPost("/cancel", CancelAsync);
        page.MapGet(redirectUri.AbsolutePath, ReceiveAsync);
    }

    private async Task<IResult> SignInAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (!await IsFromThisPageAsync(context, antiforgery))
            return Results.BadRequest();

        return await StartAsync(context, "Sign in", ConnectAsync, toProvider: true);
    }

    private async Task<IResult> CallAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (!await IsFromThisPageAsync(context, antiforgery))
            return Results.BadRequest();

        var form = await context.Request.ReadFormAsync();
        var tool = form["tool"].ToString();
        Dictionary<string, object?>? arguments = tool switch
        {
            "list_my_tasks" or "who_am_i" => null,
            "create_task" when form["title"].ToString() is { Length: > 0 } title => new() { ["title"] = title },
            "close_task" when int.TryParse(form["id"], out var id) => new() { ["id"] = id },
            "create_task" or "close_task" => throw new BadHttpRequestException($"{tool} needs its {nameof(arguments)}"),
            _ => throw new BadHttpRequestException($"No tool {tool} on this page"),
        };

        var call = arguments is null
            ? tool
            : $"{tool}({string.Join(", ", arguments.Select(argument => $"{argument.Key}: {argument.Value}"))})";
        return await StartAsync(context, call, token => CallToolAsync(tool, call, arguments, token), toProvider: false);
    }

    private async Task<IResult> CancelAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (!await IsFromThisPageAsync(context, antiforgery))
            return Results.BadRequest();

        lock (_lock)
        {
            _cancelled = true;
            _operationTimeout?.Cancel();
        }

        return SeeOther(context, Home);
    }

    private async Task<IResult> ReceiveAsync(HttpRequest request)
    {
        var context = request.HttpContext;
        Task operation;
        Task requested;
        lock (_lock)
        {
            operation = _operation;
            requested = signIn.Requested;
        }

        if (!signIn.Receive(context.Request.Query))
        {
            return Results.Content(
                Document("<p>No sign-in waits for this redirect: it ended, or this address was opened again.</p>" +
                         $"<p><a href=\"{Home}\">Back to the assistant</a></p>"),
                "text/html",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // The call goes on with the code: it redeems it and repeats the request the provider's sign-in was for
        await Task.WhenAny(operation, requested);
        return SeeOther(context, Home);
    }

    /// <summary>
    /// Starts <paramref name="work"/> unless a call is still running, and answers once it finishes or asks for a
    /// sign-in: with the provider's address when <paramref name="toProvider"/>, otherwise with the page, which then
    /// says why the sign-in is needed and links to the provider.
    /// </summary>
    private async Task<IResult> StartAsync(
        HttpContext context,
        string description,
        Func<CancellationToken, Task> work,
        bool toProvider)
    {
        Task operation;
        Task requested;
        lock (_lock)
        {
            if (!_operation.IsCompleted)
            {
                log.Add($"{description} not started: the previous step has not finished");
                return SeeOther(context, Home);
            }

            requested = signIn.Requested;
            var timeout = new CancellationTokenSource(signInTimeout);
            _operationTimeout = timeout;
            _cancelled = false;
            _last = (description, "Waiting for the answer", false);
            _operation = operation = Task.Run(() => RunAsync(description, work, timeout, requested));
        }

        await Task.WhenAny(operation, requested);
        return toProvider && !operation.IsCompleted && signIn.PendingUri is { } provider
            ? SeeOther(context, provider.ToString())
            : SeeOther(context, Home);
    }

    private async Task RunAsync(
        string description,
        Func<CancellationToken, Task> work,
        CancellationTokenSource timeout,
        Task signInRequested)
    {
        traffic.ForgetRefusal();
        try
        {
            await work(timeout.Token);
        }
        catch (Exception exception)
        {
            var reason = Reason(exception, timeout.IsCancellationRequested, signInRequested.IsCompleted);
            log.Add($"{description} failed: {reason}");
            SetLast(description, reason, failed: true);
        }
        finally
        {
            lock (_lock)
            {
                _operationTimeout = null;
                timeout.Dispose();
            }
        }
    }

    private string Reason(Exception exception, bool stopped, bool signedInAgain) => exception switch
    {
        OperationCanceledException or TimeoutException when stopped && _cancelled => "cancelled on this page",
        OperationCanceledException or TimeoutException when stopped || exception is TimeoutException
            => $"the sign-in did not finish within {signInTimeout} (Client:SignInTimeout)",
        HttpRequestException { StatusCode: HttpStatusCode.Unauthorized }
            => "the tracker refused the provider's token (401). If the provider was restarted, restart the tracker " +
               "too, so it fetches the provider's new signing keys.",

        // The SDK tries one step-up per connection: once it has asked the provider for a scope, a later refusal for
        // that scope fails at once, even when that sign-in was cancelled or denied, and only a new connection asks again
        HttpRequestException or McpException
            when traffic.RefusedScope is { } scope && !signedInAgain && signIn.HasAsked(scope)
            => $"{trackerName} refused: the token lacks {scope}. The assistant has already asked for it once since " +
               "signing in; Sign in again to ask anew.",

        // The tracker's 403 for a scope the employee did not grant, or the assistant does not ask for (Scopes)
        HttpRequestException or McpException when traffic.RefusedScope is { } scope
            => $"{trackerName} refused: the token lacks {scope}",
        _ => exception.Message,
    };

    /// <summary>
    /// Signs in afresh: a new transport, so the SDK forgets the earlier token and the scopes it asked for, and a new
    /// connection, whose first request the tracker answers with 401.
    /// </summary>
    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (_client is { } previous)
            await previous.DisposeAsync();
        if (_transport is { } previousTransport)
            await previousTransport.DisposeAsync();
        (_client, _transport, _access) = (null, null, null);

        log.Add($"Connecting to {trackerName} at {tracker}");
        _tokens = new LoggedTokenCache(log);
        signIn.ForgetAskedScopes();
        _transport = createTransport(_tokens);

        // The connection is initialized only after the employee has signed in, so the SDK's wait for it, a minute by
        // default, has to cover a person reading the sign-in page and typing a password
        _client = await McpClient.CreateAsync(
            _transport,
            new McpClientOptions { InitializationTimeout = signInTimeout },
            loggerFactory,
            cancellationToken);

        var tools = await _client.ListToolsAsync(cancellationToken: cancellationToken);
        log.Add($"tools/list: {string.Join(", ", tools.Select(listed => listed.Name))}");
        await ReadAccessAsync(cancellationToken);
        SetLast("Sign in", $"Signed in; {trackerName} lists {string.Join(", ", tools.Select(listed => listed.Name))}", failed: false);
    }

    private async Task CallToolAsync(
        string tool,
        string call,
        IReadOnlyDictionary<string, object?>? arguments,
        CancellationToken cancellationToken)
    {
        var client = _client ?? throw new InvalidOperationException("Sign in first");
        var issued = _tokens!.Issued;
        log.Add($"Calling {call}");
        var (text, failed) = await InvokeAsync(client, tool, arguments, cancellationToken);
        SetLast(call, text, failed);

        if (tool == "who_am_i")
            _access = text;
        else if (_tokens.Issued != issued)
            await ReadAccessAsync(cancellationToken);
    }

    // A call that brought a new token shows the access it grants, so the page asks the tracker what it sees
    private async Task ReadAccessAsync(CancellationToken cancellationToken)
        => (_access, _) = await InvokeAsync(_client!, "who_am_i", null, cancellationToken);

    private async Task<(string Text, bool Failed)> InvokeAsync(
        McpClient client,
        string tool,
        IReadOnlyDictionary<string, object?>? arguments,
        CancellationToken cancellationToken)
    {
        var result = await client.CallToolAsync(tool, arguments, cancellationToken: cancellationToken);
        var text = string.Join('\n', result.Content.OfType<TextContentBlock>().Select(block => block.Text));
        log.Add($"{tool}: {text}");
        return (text, result.IsError is true);
    }

    private void SetLast(string call, string result, bool failed)
    {
        lock (_lock)
            _last = (call, result, failed);
    }

    // The framework's own antiforgery log is off (appsettings.json), so a refused form is written here
    private async Task<bool> IsFromThisPageAsync(HttpContext context, IAntiforgery antiforgery)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            log.Add($"Refused a form posted to {context.Request.Path} without a valid antiforgery token of this page");
            return false;
        }
    }

    // See Other: the browser follows a form's answer with a GET, so reloading the page sends no form again
    private static IResult SeeOther(HttpContext context, string location)
    {
        context.Response.Headers.Location = location;
        return Results.StatusCode(StatusCodes.Status303SeeOther);
    }

    private IResult Render(HttpContext context, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        var formToken = $"<input type=\"hidden\" name=\"{tokens.FormFieldName}\" value=\"{tokens.RequestToken}\">";
        var pending = signIn.PendingUri;
        bool busy;
        (string Call, string Result, bool Failed)? last;
        lock (_lock)
            (busy, last) = (!_operation.IsCompleted, _last);
        var disabled = busy ? " disabled" : "";

        var body = new StringBuilder();
        body.Append($"<p>Works with {Encode(trackerName)} at <code>{Encode(tracker.ToString())}</code>.</p>");

        if (pending is not null)
        {
            var refused = traffic.RefusedScope;

            // What the SDK asks the provider for: the refused scope, or only the scopes the assistant is kept to
            var asked = HttpUtility.ParseQueryString(pending.Query)["scope"] ?? "";
            body.Append("<section class=\"notice\">");
            body.Append(refused is null
                ? "<p>The tracker asks for a sign-in.</p>"
                : $"<p>{Encode(trackerName)} refused: the token lacks {Encode(refused)}.</p>");
            body.Append($"<p>The assistant asks the provider for: <code>{Encode(asked)}</code></p>");
            body.Append($"<p><a class=\"button\" href=\"{Encode(pending.ToString())}\">" +
                        (refused is not null && signIn.HasAsked(refused)
                            ? $"Allow {Encode(refused)}"
                            : "Continue to the provider") +
                        "</a></p>");
            body.Append($"<form method=\"post\" action=\"/cancel\">{formToken}<button class=\"secondary\">Cancel</button></form>");
            body.Append("</section>");
        }
        else if (busy)
        {
            body.Append($"<p class=\"notice\">Working on the last step. <a href=\"{Home}\">Refresh</a></p>");
        }

        body.Append("<h2>Access</h2>");
        if (_client is null)
        {
            body.Append("<p>Not signed in.</p>");
        }
        else if (_access is null)
        {
            body.Append("<p>Signed in, but who_am_i did not answer; the log says why.</p>");
        }
        else
        {
            body.Append("<p>What the tracker sees of the token, as who_am_i answers:</p><dl>");
            foreach (var pair in _access.Split("; ").Select(pair => pair.Split('=', 2)).Where(pair => pair.Length == 2))
                body.Append($"<dt>{Encode(pair[0])}</dt><dd><code>{Encode(pair[1])}</code></dd>");
            body.Append("</dl>");
        }

        body.Append($"<form method=\"post\" action=\"/sign-in\">{formToken}" +
                    $"<button{disabled}>{(_client is null ? "Sign in" : "Sign in again")}</button></form>");

        if (_client is not null)
        {
            body.Append("<h2>Tools</h2><div class=\"tools\">");
            body.Append(ToolForm(formToken, disabled, "list_my_tasks", "List my tasks", ""));
            body.Append(ToolForm(formToken, disabled, "who_am_i", "Who am I", ""));
            body.Append(ToolForm(formToken, disabled, "create_task", "Create task",
                $"<input name=\"title\" required value=\"{Encode(newTaskTitle)}\" aria-label=\"Title\">"));
            body.Append(ToolForm(formToken, disabled, "close_task", "Close task",
                "<input name=\"id\" type=\"number\" min=\"1\" required placeholder=\"Task id\" aria-label=\"Task id\">"));
            body.Append("</div>");
        }

        if (last is { } shown)
        {
            body.Append($"<h2>Last call</h2><p><code>{Encode(shown.Call)}</code></p>");
            body.Append($"<pre class=\"{(shown.Failed ? "failed" : "result")}\">{Encode(Indented(shown.Result))}</pre>");
        }

        body.Append("<h2>Log, latest first</h2><ol class=\"log\">");
        foreach (var (at, text) in log.LatestFirst())
            body.Append($"<li><time>{at:HH:mm:ss}</time> {Encode(text)}</li>");
        body.Append("</ol>");

        context.Response.Headers.CacheControl = "no-store";
        return Results.Content(Document(body.ToString()), "text/html");
    }

    private static string ToolForm(string formToken, string disabled, string tool, string label, string fields)
        => $"<form method=\"post\" action=\"/call\">{formToken}<input type=\"hidden\" name=\"tool\" value=\"{tool}\">" +
           $"{fields}<button{disabled}>{label}</button></form>";

    private string Document(string body)
        => $"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{Encode(clientName)}</title>
            <style>
            body {"{"} font-family: system-ui, sans-serif; max-width: 60rem; margin: 2rem auto; padding: 0 1rem; color: #222; background: #fff; {"}"}
            code, pre {"{"} font-family: ui-monospace, monospace; {"}"}
            pre {"{"} white-space: pre-wrap; padding: .75rem; border-radius: .25rem; background: #f3f5f7; {"}"}
            pre.failed, .notice {"{"} background: #fff4e5; border-left: .25rem solid #e08a00; padding: .75rem; {"}"}
            .tools {"{"} display: flex; flex-wrap: wrap; gap: .75rem; {"}"}
            form {"{"} margin: .5rem 0; display: flex; gap: .5rem; {"}"}
            a.button, button {"{"} padding: .4rem .9rem; border: 1px solid #0b5cad; border-radius: .25rem; background: #0b5cad; color: #fff; text-decoration: none; font: inherit; cursor: pointer; {"}"}
            button.secondary {"{"} background: #fff; color: #0b5cad; {"}"}
            input[name=title] {"{"} width: 16rem; max-width: 100%; {"}"}
            button[disabled] {"{"} opacity: .5; cursor: default; {"}"}
            dl {"{"} display: grid; grid-template-columns: max-content 1fr; gap: .25rem 1rem; {"}"}
            dd {"{"} margin: 0; {"}"}
            .log {"{"} list-style: none; padding: 0; font-size: .9rem; {"}"}
            .log li {"{"} padding: .2rem 0; border-bottom: 1px solid #eee; overflow-wrap: anywhere; {"}"}
            .log time {"{"} color: #666; margin-right: .5rem; {"}"}
            </style>
            </head>
            <body>
            <h1>{Encode(clientName)}</h1>
            {body}
            </body>
            </html>
            """;

    // A tool's JSON answer, indented for reading; any other answer as it came
    private static string Indented(string text)
    {
        try
        {
            return JsonNode.Parse(text)?.ToJsonString(IndentedJson) ?? text;
        }
        catch (JsonException)
        {
            return text;
        }
    }

    private static string Encode(string text) => WebUtility.HtmlEncode(text);
}
