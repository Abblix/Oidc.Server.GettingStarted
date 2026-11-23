// Registers with an initial access token, then manages the registration with the registration access token the
// provider returns (RFC 7591, RFC 7592), and shows what the provider refuses on the way.
//
//   dotnet run -- <initial access token>
//
// The exit code is the number of responses whose status was not the one the scenario expects.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using RegistrationClientApp;

if (args is not [var initialAccessToken])
{
    Console.Error.WriteLine("Usage: dotnet run -- <initial access token>");
    return -1;
}

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json")
    .Build();

var issuer = Required("Provider:Issuer");
var clientName = Required("Registration:ClientName");
var updatedClientName = Required("Registration:UpdatedClientName");
var otherClientName = Required("Registration:OtherClientName");

using var http = new HttpClient();
var exchange = new Exchange(http);

var metadata = await http.GetFromJsonAsync<JsonObject>(issuer.TrimEnd('/') + "/.well-known/openid-configuration")
               ?? throw new InvalidOperationException($"{issuer} published no metadata");
var registrationEndpoint = (string?)metadata["registration_endpoint"]
                           ?? throw new InvalidOperationException($"{issuer} publishes no registration_endpoint");
var tokenEndpoint = (string?)metadata["token_endpoint"]
                    ?? throw new InvalidOperationException($"{issuer} publishes no token_endpoint");

// A service acting for itself: client_credentials, and no response type, since it never visits the authorization
// endpoint (RFC 7591 section 2.1)
JsonObject Registration(string name, string authMethod = "client_secret_basic") => new()
{
    ["client_name"] = name,
    ["grant_types"] = new JsonArray("client_credentials"),
    ["response_types"] = new JsonArray(),
    ["token_endpoint_auth_method"] = authMethod,
};

await exchange.SendAsync(
    "Register without an initial access token: refused",
    Post(registrationEndpoint, Registration(clientName), bearer: null),
    HttpStatusCode.Unauthorized);

var registered = await exchange.SendAsync(
    "Register with the initial access token",
    Post(registrationEndpoint, Registration(clientName), initialAccessToken),
    HttpStatusCode.Created);
if (registered == null)
    return Stopped("the registration was refused");

var clientId = Member(registered, "client_id");
var clientSecret = Member(registered, "client_secret");
// RFC 7592 Appendix B: the client uses this address as given and never builds it from pieces
var clientUri = Member(registered, "registration_client_uri");
var registrationAccessToken = Member(registered, "registration_access_token");

// client_credentials lets a client act for itself, so the provider issues it only to a client that proves who it
// is: a public client, one with no credential at the token endpoint, is refused at registration
await exchange.SendAsync(
    "Register a client that sends no credential (token_endpoint_auth_method none): refused",
    Post(registrationEndpoint, Registration(clientName, "none"), initialAccessToken),
    HttpStatusCode.BadRequest);

await exchange.SendAsync(
    "Get a token as the registered client",
    TokenRequest(clientId, clientSecret),
    HttpStatusCode.OK);

var current = await exchange.SendAsync(
    "Read the registration",
    new HttpRequestMessage(HttpMethod.Get, clientUri) { Headers = { Authorization = Bearer(registrationAccessToken) } },
    HttpStatusCode.OK);
if (current == null)
    return Stopped("the registration could not be read");

// RFC 7592 Appendix A.1: the provider may issue a new client_secret in any read or update response, so the client
// keeps the one each response carries
clientSecret = Member(current, "client_secret");

// RFC 7592 section 2.2: the update carries every field the read returned, client_id and client_secret included,
// except the four the provider owns; a field left out would be deleted from the registration
var update = (JsonObject)current.DeepClone();
foreach (var providerOwned in new[]
         {
             "registration_access_token", "registration_client_uri", "client_secret_expires_at", "client_id_issued_at",
         })
{
    update.Remove(providerOwned);
}

// RFC 7592 section 2.2: a client_secret in the update must be the one currently issued, and a client never chooses
// its own
var chosenSecret = (JsonObject)update.DeepClone();
chosenSecret["client_secret"] = "a-secret-the-client-chose";
await exchange.SendAsync(
    "Update the registration with a client_secret of the client's own choosing: refused",
    Put(clientUri, chosenSecret, registrationAccessToken),
    HttpStatusCode.BadRequest);

update["client_name"] = updatedClientName;

var updated = await exchange.SendAsync(
    "Update the registration: a new client_name",
    Put(clientUri, update, registrationAccessToken),
    HttpStatusCode.OK);
if (updated == null)
    return Stopped("the update was refused");

// The provider rotates the registration access token on update, which RFC 7592 section 5 permits; the client must
// discard the previous one (section 2.2), and this provider refuses it from now on
var previousRegistrationAccessToken = registrationAccessToken;
registrationAccessToken = Member(updated, "registration_access_token");
clientSecret = Member(updated, "client_secret");

await exchange.SendAsync(
    "Read with the registration access token the update replaced: refused",
    new HttpRequestMessage(HttpMethod.Get, clientUri)
        { Headers = { Authorization = Bearer(previousRegistrationAccessToken) } },
    HttpStatusCode.Unauthorized);

var other = await exchange.SendAsync(
    "Register a second client with the same initial access token",
    Post(registrationEndpoint, Registration(otherClientName), initialAccessToken),
    HttpStatusCode.Created);
if (other == null)
    return Stopped("the second registration was refused");

var otherClientUri = Member(other, "registration_client_uri");
var otherRegistrationAccessToken = Member(other, "registration_access_token");

await exchange.SendAsync(
    "Read the second registration with its own registration access token",
    new HttpRequestMessage(HttpMethod.Get, otherClientUri)
        { Headers = { Authorization = Bearer(otherRegistrationAccessToken) } },
    HttpStatusCode.OK);

await exchange.SendAsync(
    "Delete the first registration with the second client's registration access token: refused",
    new HttpRequestMessage(HttpMethod.Delete, clientUri)
        { Headers = { Authorization = Bearer(otherRegistrationAccessToken) } },
    HttpStatusCode.Unauthorized);

await exchange.SendAsync(
    "Delete the first registration with its own registration access token",
    new HttpRequestMessage(HttpMethod.Delete, clientUri) { Headers = { Authorization = Bearer(registrationAccessToken) } },
    HttpStatusCode.NoContent);

await exchange.SendAsync(
    "Read the deleted registration: refused",
    new HttpRequestMessage(HttpMethod.Get, clientUri) { Headers = { Authorization = Bearer(registrationAccessToken) } },
    HttpStatusCode.Unauthorized);

// RFC 7592 section 2.3: deleting the registration invalidates its client_id and client_secret
await exchange.SendAsync(
    "Get a token as the deleted client: refused",
    TokenRequest(clientId, clientSecret),
    HttpStatusCode.Unauthorized);

await exchange.SendAsync(
    "Delete the second registration",
    new HttpRequestMessage(HttpMethod.Delete, otherClientUri)
        { Headers = { Authorization = Bearer(otherRegistrationAccessToken) } },
    HttpStatusCode.NoContent);

Console.WriteLine(exchange.Unexpected == 0
    ? "Every response was the expected one."
    : $"{exchange.Unexpected} response(s) were not the expected ones.");
return exchange.Unexpected;

string Required(string name)
    => configuration[name] ?? throw new InvalidOperationException($"{name} is not configured");

static string Member(JsonObject response, string name)
    => (string?)response[name] ?? throw new InvalidOperationException($"The response has no {name}");

int Stopped(string reason)
{
    Console.WriteLine($"The scenario cannot go on: {reason}.");
    return exchange.Unexpected;
}

static AuthenticationHeaderValue Bearer(string token) => new("Bearer", token);

static HttpRequestMessage Post(string uri, JsonObject body, string? bearer)
{
    var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(body) };
    if (bearer != null)
        request.Headers.Authorization = Bearer(bearer);
    return request;
}

static HttpRequestMessage Put(string uri, JsonObject body, string bearer)
    => new(HttpMethod.Put, uri) { Content = JsonContent.Create(body), Headers = { Authorization = Bearer(bearer) } };

HttpRequestMessage TokenRequest(string id, string secret) => new(HttpMethod.Post, tokenEndpoint)
{
    Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "client_credentials" }),
    Headers =
    {
        // RFC 6749 section 2.3.1: client_secret_basic form-encodes the id and the secret before joining them
        Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(
                $"{Uri.EscapeDataString(id)}:{Uri.EscapeDataString(secret)}"))),
    },
};
