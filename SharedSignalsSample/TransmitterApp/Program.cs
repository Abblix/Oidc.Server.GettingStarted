using Abblix.SecurityEvents.Subjects;
using System.Security.Cryptography;
using Abblix.Jwt;
using Abblix.SecurityEvents.CAEP;
using Abblix.SecurityEvents.Infrastructure;
using Abblix.SharedSignals.Infrastructure;
using Abblix.SharedSignals.MinimalApi;
using Abblix.SharedSignals.Transmitter;

var builder = WebApplication.CreateBuilder(args);

const string IssuerKey = "Issuer";
const string StreamsSection = "SharedSignals:Streams";

// The issuer is the identity this transmitter claims in every token it signs, and the receiver matches it
// against its own expected issuers. A settings file that lost the key would announce an identity nobody
// configured, and the receiver would stop believing a transmitter that is otherwise working.
var issuer = builder.Configuration[IssuerKey]
    ?? throw new InvalidOperationException($"Configuration key '{IssuerKey}' is missing.");

// A transmitter whose stream declarations went missing starts cleanly, delivers nothing and logs nothing,
// which is the emptiest kind of failure to diagnose.
var streams = builder.Configuration.GetSection(StreamsSection).Get<IReadOnlyList<ConfiguredStream>>()
    ?? throw new InvalidOperationException($"Configuration section '{StreamsSection}' is missing.");

// A real transmitter takes its signing key from the same place the rest of the deployment does - a key
// vault, a certificate store. This sample mints one per run, so restarting it is a key rollover as the
// receiving side sees one.
//
// The key id has to be minted with the key, not fixed in the source. A receiver caches keys by "kid" and
// refetches the JWK Set only when a token names one it does not hold, so a new key wearing the previous
// name is never fetched: the receiver keeps verifying against the key it already has and answers every
// delivery "400". Restart this app with the id hardcoded and that is exactly what happens.
//
// 2048 bits is the floor RS256 is deployed at; a host with its own key size policy sets that instead of
// inheriting this number from a sample.
using var rsa = RSA.Create(2048);
var signingKey = new RsaJsonWebKey
    {
        KeyId = $"ssf-sign-{Guid.NewGuid():N}",
        Algorithm = SigningAlgorithms.RS256,
        Usage = PublicKeyUsages.Signature,
    }
    .Apply(rsa.ExportParameters(true));

builder.Services.AddSecurityEvents(options =>
{
    options.Events.RegisterCaepEvents();
    options.SigningKeySource = _ => Task.FromResult<JsonWebKey>(signingKey);
});

builder.Services.AddSharedSignalsTransmitter(new SharedSignalsTransmitterOptions
{
    Issuer = issuer,
    JwksUri = new Uri($"{issuer}/.well-known/jwks.json"),
    EventsSupported = [CaepEventTypes.SessionRevoked],

    // A receiver names its own delivery endpoint, so by default the transmitter refuses to POST to an
    // address inside its own network: otherwise a stream pointed at a metadata service turns the
    // transmitter into the attacker's HTTP client. Both sides of this sample run on one machine, which is
    // exactly the case that refusal covers, so the operator permits those destinations explicitly.
    //
    // Derived from the declared streams rather than configured separately, so the address this transmitter
    // permits and the address it pushes to cannot be edited apart.
    AllowedReceiverAddresses = [.. streams.Select(stream => stream.PushEndpointUrl).OfType<Uri>()],
});

// The receivers of this deployment are known in advance, so they are declared rather than created through
// the management API.
builder.Services.AddSharedSignalsConfiguredStreams(streams);

var app = builder.Build();

// Only the public halves travel: the receiver verifies signatures with them and can hold nothing else.
app.MapGet("/.well-known/jwks.json",
    () => new JsonWebKeySet([signingKey.Sanitize(includePrivateKeys: false)]));

// Only the configuration document, not the Stream Management API. This transmitter's streams come from
// appsettings.json, so nothing needs to create one over HTTP - and the management API is the surface that
// has to be guarded by scope, because whoever can create a stream can ask to be told about your users.
// MapSharedSignalsTransmitterEndpoints() maps that API and this document together.
app.MapSharedSignalsConfigurationDocument();

// The one call a host makes when the thing actually happens. Everything else in this file is setup.
app.MapPost("/sessions/{sessionId}/revoke", async (
    string sessionId,
    string user,
    EventDispatcher dispatcher,
    CancellationToken cancellationToken) =>
{
    await dispatcher.DispatchAsync(new SecurityEventDescriptor
    {
        EventType = CaepEventTypes.SessionRevoked,
        Subject = new ComplexSubject
        {
            Session = new OpaqueSubject(sessionId),
            User = new EmailSubject(user),
        },
        Payload = new SessionRevokedPayload
        {
            InitiatingEntity = CaepEventPayload.InitiatingEntities.Policy,
        },
    }, cancellationToken);

    return Results.Accepted();
});

app.Run();
