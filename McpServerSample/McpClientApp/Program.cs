using System.Text.Json.Nodes;
using McpClientApp;
using ModelContextProtocol;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

var mcpServer = AbsoluteUri(configuration["Mcp:Server"], "Mcp:Server", Uri.UriSchemeHttps, Uri.UriSchemeHttp);
var documentFile = configuration["Client:MetadataDocument"]
                   ?? throw new InvalidOperationException("Client:MetadataDocument is not configured");
var newTaskTitle = configuration["Demo:NewTask"] ?? throw new InvalidOperationException("Demo:NewTask is not configured");
var signInTimeout = TimeSpan.TryParse(configuration["Client:SignInTimeout"], out var timeout) && timeout > TimeSpan.Zero
    ? timeout
    : throw new InvalidOperationException(
        $"Client:SignInTimeout is '{configuration["Client:SignInTimeout"]}'; it has to be a positive time span such as 00:05:00");

// The scopes the assistant asks for, or none configured to ask for every scope the tracker names
var requestedScopes = configuration.GetSection("Scopes").Get<string[]>();

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

ISignIn signIn = configuration["SignIn"] switch
{
    "browser" => new BrowserSignIn(clientName, redirectUri),
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
        }),
    var other => throw new InvalidOperationException($"SignIn is '{other}'; use browser or headless"),
};

var app = builder.Build();

// Served as application/json, the type the provider names in its Accept header (draft section 6). The line it prints
// shows the provider fetching it.
app.MapGet(clientId.AbsolutePath, (HttpRequest request) =>
{
    Console.WriteLine($"Served the metadata document (Accept: {request.Headers.Accept})");
    return Results.Text(documentText, "application/json");
});

if (signIn is BrowserSignIn browser)
    app.MapGet(redirectUri.AbsolutePath, browser.Receive);

await app.StartAsync();

var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
var oauth = new ClientOAuthOptions
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
            Console.WriteLine($"Registered dynamically, client_id {response.ClientId}");
            return Task.CompletedTask;
        },
    },

    // The SDK takes the scopes the tracker names. Kept to the configured ones, a refusal for a missing scope is final:
    // the SDK asks again only when that adds a scope (step-up authorization), and here it cannot
    ScopeSelector = requestedScopes is null
        ? null
        : candidates => candidates?.Where(scope => requestedScopes.Contains(scope) || scope == "offline_access"),

    // The handler returns the code together with state and iss, so the SDK checks both: section 7 of the draft makes iss
    // mandatory in the authorization response
    AuthorizationCallbackHandler = signIn.AuthorizeAsync,
};

Console.WriteLine(useMetadataDocument
    ? $"{clientName} identifies itself as {clientId}"
    : $"{clientName} registers itself at the provider");

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
    async Task<CallToolResult> CallAsync(string tool, IReadOnlyDictionary<string, object?>? arguments = null)
    {
        CallToolResult result;
        try
        {
            result = await client.CallToolAsync(tool, arguments);
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
