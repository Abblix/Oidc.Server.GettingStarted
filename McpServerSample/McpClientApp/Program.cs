using System.Text.Json.Nodes;
using McpClientApp;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

var mcpServer = new Uri(configuration["Mcp:Server"] ?? throw new InvalidOperationException("Mcp:Server is not configured"));
var documentFile = configuration["Client:MetadataDocument"]
                   ?? throw new InvalidOperationException("Client:MetadataDocument is not configured");

// The document is the one description of this client: its URL is the client_id, and the name and the redirect URI
// a dynamic registration sends are read from it as well
var documentPath = Path.Combine(AppContext.BaseDirectory, documentFile);
var documentText = await File.ReadAllTextAsync(documentPath);
var document = JsonNode.Parse(documentText) as JsonObject
               ?? throw new InvalidOperationException($"{documentPath} is not a JSON object");
var clientId = new Uri(RequiredString(document, "client_id"), UriKind.Absolute);
var clientName = RequiredString(document, "client_name");
var redirectUri = document["redirect_uris"] is JsonArray { Count: > 0 } redirectUris
    ? new Uri(redirectUris[0]!.GetValue<string>(), UriKind.Absolute)
    : throw new InvalidOperationException($"{documentPath} lists no redirect_uris");

var useMetadataDocument = configuration["Registration"] switch
{
    "metadata-document" => true,
    "dynamic" => false,
    var other => throw new InvalidOperationException(
        $"Registration is '{other}'; use metadata-document or dynamic"),
};

// This app is the client's web site and its loopback redirect listener at once: the document is served at the
// origin of its client_id, and the provider's redirect arrives at the origin of the redirect URI
builder.WebHost.UseUrls(clientId.GetLeftPart(UriPartial.Authority), redirectUri.GetLeftPart(UriPartial.Authority));

var signIn = configuration["SignIn"] switch
{
    "browser" => (ISignIn)new BrowserSignIn(redirectUri),
    "headless" => new HeadlessSignIn(
        redirectUri,
        configuration["HeadlessSignIn:Email"]
        ?? throw new InvalidOperationException("HeadlessSignIn:Email is not configured"),
        configuration["HeadlessSignIn:Password"]
        ?? throw new InvalidOperationException("HeadlessSignIn:Password is not configured")),
    var other => throw new InvalidOperationException($"SignIn is '{other}'; use browser or headless"),
};

var app = builder.Build();

// Served as it is on disk, with the media type draft-ietf-oauth-client-id-metadata-document section 4 asks for. The
// line it prints shows the provider fetching it.
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

    // The SDK sends this URL as the client_id when the provider's metadata says
    // client_id_metadata_document_supported, and registers the client otherwise (MCP authorization, client
    // registration). Without it the client always registers.
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

    // The handler returns the code together with state and iss, so the SDK checks both: section 7 of the draft makes
    // iss mandatory in the authorization response
    AuthorizationCallbackHandler = signIn.AuthorizeAsync,
};

if (useMetadataDocument)
    Console.WriteLine($"Identifying as {clientId}, served by this app");

await using (var transport = new HttpClientTransport(
                 new HttpClientTransportOptions { Endpoint = mcpServer, OAuth = oauth },
                 loggerFactory))
await using (var client = await McpClient.CreateAsync(transport, loggerFactory: loggerFactory))
{
    var result = await client.CallToolAsync("who_am_i");
    foreach (var block in result.Content.OfType<TextContentBlock>())
        Console.WriteLine($"who_am_i: {block.Text}");

    if (result.IsError is true)
        Environment.ExitCode = 1;
}

await app.StopAsync();
return;

static string RequiredString(JsonObject document, string member)
    => document[member]?.GetValue<string>()
       ?? throw new InvalidOperationException($"The client metadata document has no {member}");
