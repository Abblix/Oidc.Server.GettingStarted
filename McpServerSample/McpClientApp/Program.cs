using System.Text.Json.Nodes;
using McpClientApp;
using ModelContextProtocol;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// The access token travels to the MCP server in every request, so plain http is accepted only to this computer
var mcpServer = AbsoluteUri(configuration["Mcp:Server"], "Mcp:Server", Uri.UriSchemeHttps, Uri.UriSchemeHttp);
if (mcpServer.Scheme == Uri.UriSchemeHttp && !mcpServer.IsLoopback)
{
    throw new InvalidOperationException(
        $"Mcp:Server is '{mcpServer}'; plain http is accepted only on this computer (localhost, 127.0.0.1, ::1), use https");
}
var documentFile = configuration["Client:MetadataDocument"]
                   ?? throw new InvalidOperationException("Client:MetadataDocument is not configured");
var newTaskTitle = configuration["Demo:NewTask"] ?? throw new InvalidOperationException("Demo:NewTask is not configured");
var signInTimeout = TimeSpan.TryParse(configuration["Client:SignInTimeout"], out var timeout) && timeout > TimeSpan.Zero
    ? timeout
    : throw new InvalidOperationException(
        $"Client:SignInTimeout is '{configuration["Client:SignInTimeout"]}'; it has to be a positive time span such as 00:05:00");

// The scopes the assistant is limited to, or none configured to take whatever the tracker asks for
var requestedScopes = configuration.GetSection("Scopes").Get<string[]>();

// A client that registers itself names its scopes in the registration, and the provider holds it to them, so a scope
// asked for later could not be added by step-up; such a client registers, and asks, for every scope it will use
var registrationScopes = configuration.GetSection("RegistrationScopes").Get<string[]>()
                         ?? throw new InvalidOperationException("RegistrationScopes is not configured");

// The document is the one description of the assistant: its URL is the client_id, and the name and the redirect URI
// a dynamic registration sends are read from it as well
var documentPath = Path.Combine(AppContext.BaseDirectory, documentFile);
var documentText = await File.ReadAllTextAsync(documentPath);
var document = JsonNode.Parse(documentText) as JsonObject
               ?? throw new InvalidOperationException($"{documentPath} is not a JSON object");
var clientId = AbsoluteUri(DocumentString(document, "client_id"), $"client_id in {documentPath}", Uri.UriSchemeHttps);
var clientName = DocumentString(document, "client_name")
                 ?? throw new InvalidOperationException($"{documentPath} has no client_name");
var redirectUri = AbsoluteUri(
    document["redirect_uris"] is JsonArray { Count: > 0 } redirectUris ? DocumentString(redirectUris, 0) : null,
    $"the first of redirect_uris in {documentPath}",
    Uri.UriSchemeHttp);

var useMetadataDocument = configuration["Registration"] switch
{
    "metadata-document" => true,
    "dynamic" => false,
    var other => throw new InvalidOperationException($"Registration is '{other}'; use metadata-document or dynamic"),
};

// This app stands for the vendor's web site, which publishes the document at the origin of its client_id, and for
// the assistant on the employee's computer, which receives the provider's redirect at the origin of the redirect URI
builder.WebHost.UseUrls(clientId.GetLeftPart(UriPartial.Authority), redirectUri.GetLeftPart(UriPartial.Authority));

// The browser run shows its steps on the assistant's page as well as in the terminal
var steps = configuration["SignIn"] == "browser" ? new StepLog() : null;
Action<string> say = steps is null ? Console.WriteLine : steps.Add;

ISignIn signIn = configuration["SignIn"] switch
{
    "browser" => new BrowserSignIn(steps!),
    "headless" => new HeadlessSignIn(
        redirectUri,
        configuration["HeadlessSignIn:Email"]
        ?? throw new InvalidOperationException("HeadlessSignIn:Email is not configured"),
        configuration["HeadlessSignIn:Password"]
        ?? throw new InvalidOperationException("HeadlessSignIn:Password is not configured"),
        configuration["HeadlessSignIn:Consent"] switch
        {
            "allow" => "allow",
            "deny" => "deny",
            var other => throw new InvalidOperationException($"HeadlessSignIn:Consent is '{other}'; use allow or deny"),
        },
        configuration.GetSection("HeadlessSignIn:GrantedScopes").Get<string[]>()),
    var other => throw new InvalidOperationException($"SignIn is '{other}'; use browser or headless"),
};

// The assistant's page posts its forms with an antiforgery token
builder.Services.AddAntiforgery();

var app = builder.Build();

// Served as application/json, the type the provider names in its Accept header (draft section 6). The line it prints
// shows the provider fetching it.
app.MapGet(clientId.AbsolutePath, (HttpRequest request) =>
{
    say($"Served the metadata document (Accept: {request.Headers.Accept})");
    return Results.Text(documentText, "application/json");
});

var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
say(useMetadataDocument
    ? $"{clientName} identifies itself as {clientId}"
    : $"{clientName} registers itself at the provider");

if (signIn is BrowserSignIn browser)
{
    // The SDK's requests to the tracker and to the provider go through one handler, which shows them on the page
    var traffic = new TrackerTraffic(steps!);
    var page = new AssistantPage(
        clientName,
        configuration["Mcp:Name"] ?? throw new InvalidOperationException("Mcp:Name is not configured"),
        mcpServer,
        newTaskTitle,
        signInTimeout,
        steps!,
        browser,
        traffic,
        tokens => new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = mcpServer, OAuth = OAuth(tokens) },
            new HttpClient(traffic, disposeHandler: false),
            loggerFactory,
            ownsHttpClient: true),
        loggerFactory);
    page.Map(app, redirectUri);

    await app.StartAsync();
    Console.WriteLine($"Open {redirectUri.GetLeftPart(UriPartial.Authority)}/ in a browser; Ctrl+C stops the assistant.");
    await app.WaitForShutdownAsync();
    return;
}

await app.StartAsync();

var oauth = OAuth(null);

await using var transport = new HttpClientTransport(
    new HttpClientTransportOptions { Endpoint = mcpServer, OAuth = oauth },
    loggerFactory);

// What the assistant does for the employee: sign in through the provider, then look at the list, add a task, close
// it and look again
try
{
    // The connection is initialized only after the employee has signed in, so the SDK's wait for it, a minute by
    // default, has to cover a person reading the sign-in page and typing a password
    await using var client = await McpClient.CreateAsync(
        transport,
        new McpClientOptions { InitializationTimeout = signInTimeout },
        loggerFactory);

    // An assistant offers its model the tools the server lists, so the tracker lists every tool whatever the token's
    // scope, and a call the scope does not cover is what asks for more
    var tools = await client.ListToolsAsync();
    Console.WriteLine($"tools/list: {string.Join(", ", tools.Select(listed => listed.Name))}");

    async Task<CallToolResult> CallAsync(string tool, IReadOnlyDictionary<string, object?>? arguments = null)
    {
        // A call refused for a scope the token lacks has the employee sign in again, and that wait is bounded as the
        // first sign-in's is
        using var signInWait = new CancellationTokenSource(signInTimeout);
        CallToolResult result;
        try
        {
            result = await client.CallToolAsync(tool, arguments, cancellationToken: signInWait.Token);
        }
        catch (OperationCanceledException) when (signInWait.IsCancellationRequested)
        {
            throw new TimeoutException();
        }
        catch (HttpRequestException exception) when (exception.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            // The tracker's 403 for a scope the employee did not grant, once the SDK found no scope to step up to
            throw new McpException($"{tool} was refused by the tracker (403): the token lacks the scope it needs");
        }

        var text = string.Join('\n', result.Content.OfType<TextContentBlock>().Select(block => block.Text));
        Console.WriteLine($"{tool}: {text}");
        return result.IsError is true ? throw new McpException($"{tool} failed: {text}") : result;
    }

    await CallAsync("who_am_i");
    await CallAsync("list_my_tasks");
    var created = await CallAsync("create_task", new Dictionary<string, object?> { ["title"] = newTaskTitle });
    await CallAsync("close_task", new Dictionary<string, object?> { ["id"] = TaskId(created) });
    await CallAsync("list_my_tasks");
}
catch (TimeoutException)
{
    Console.WriteLine($"Stopped: the sign-in did not finish within {signInTimeout} (Client:SignInTimeout)");
    Environment.ExitCode = 1;
}
catch (HttpRequestException exception) when (exception.StatusCode == System.Net.HttpStatusCode.Unauthorized)
{
    // A token the provider has just issued and the tracker still refuses: the provider generates its signing keys at
    // startup, and the tracker holds the key set it fetched before
    Console.WriteLine("Stopped: the tracker refused the provider's token (401). If the provider was restarted, " +
                      "restart the tracker too, so it fetches the provider's new signing keys.");
    Environment.ExitCode = 1;
}
catch (Exception exception) when (exception is McpException or HttpRequestException or InvalidOperationException)
{
    Console.WriteLine($"Stopped: {exception.Message}");
    Environment.ExitCode = 1;
}

await app.StopAsync();
return;

// The SDK keeps its tokens in tokens when given, in a cache of its own otherwise
ClientOAuthOptions OAuth(ITokenCache? tokens) => new()
{
    RedirectUri = redirectUri,

    // The SDK sends this URL as the client_id when the provider's metadata says client_id_metadata_document_supported,
    // and registers the client otherwise (MCP authorization, client registration). Without it the client always
    // registers.
    ClientMetadataDocumentUri = useMetadataDocument ? clientId : null,

    DynamicClientRegistration = new DynamicClientRegistrationOptions
    {
        ClientName = clientName,
        ResponseDelegate = (response, _) =>
        {
            say($"Registered dynamically, client_id {response.ClientId}");
            return Task.CompletedTask;
        },
    },

    // Known by its document, the assistant takes the scope the tracker names, tasks.read, and asks for tasks.write only
    // when a call needs it (step-up authorization). Kept to the configured scopes, a refusal for a missing scope is
    // final after one more sign-in with the same scopes: the SDK always tries one step-up, and stops when the next refusal
    // names no scope it has not asked for. Registering itself, it asks up front for the scopes it registers with.
    ScopeSelector = candidates => (requestedScopes, useMetadataDocument) switch
    {
        ({ } limit, _) => candidates?.Where(scope => limit.Contains(scope) || scope == "offline_access"),
        (null, true) => candidates,
        (null, false) => (candidates ?? []).Union(registrationScopes),
    },

    // The handler returns the code together with state and iss, so the SDK checks both: section 7 of the draft makes iss
    // mandatory in the authorization response. It runs once at the start and again whenever the tracker asks for a
    // scope the token lacks, so the line it prints shows what each sign-in asks for.
    AuthorizationCallbackHandler = (context, cancellationToken) =>
    {
        var scope = System.Web.HttpUtility.ParseQueryString(context.AuthorizationUri.Query)["scope"];
        say($"Asking the employee to sign in and allow: {scope}");
        return signIn.AuthorizeAsync(context, cancellationToken);
    },

    TokenCache = tokens,
};

// create_task answers with the task as structured content
static int TaskId(CallToolResult created)
    => created.StructuredContent is { } task && task.TryGetProperty("id", out var id)
        ? id.GetInt32()
        : throw new InvalidOperationException("create_task answered no task id");

static string? DocumentString(JsonNode? parent, object member)
{
    var node = member is int index ? parent?.AsArray()[index] : parent?[(string)member];
    return node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}

static Uri AbsoluteUri(string? value, string name, params string[] schemes)
    => Uri.TryCreate(value, UriKind.Absolute, out var uri) && schemes.Contains(uri.Scheme)
        ? uri
        : throw new InvalidOperationException(
            $"{name} is '{value}'; it has to be an absolute {string.Join(" or ", schemes)} URL");
