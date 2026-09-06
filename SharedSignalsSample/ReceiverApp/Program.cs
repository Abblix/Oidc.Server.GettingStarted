using Abblix.SecurityEvents.CAEP;
using Abblix.SecurityEvents.Delivery;
using Abblix.SecurityEvents.Infrastructure;
using Abblix.SecurityEvents.MinimalApi;
using Abblix.SharedSignals.Infrastructure;
using Abblix.SharedSignals.Receiver.SecurityEvent;
using ReceiverApp;

var builder = WebApplication.CreateBuilder(args);

// Read as required rather than defaulted: a receiver that silently falls back to a hardcoded transmitter
// is a receiver trusting an issuer nobody configured.
var transmitter = builder.Configuration["Transmitter"]
    ?? throw new InvalidOperationException("Configuration key 'Transmitter' is missing.");

var self = builder.Configuration["Audience"]
    ?? throw new InvalidOperationException("Configuration key 'Audience' is missing.");

builder.Services.AddSecurityEvents(options => options.Events.RegisterCaepEvents());

// The receiver's actual trust root. Keys come from the transmitter's published JWK Set and are cached; a
// token naming a kid the cache lacks forces one refetch, which is how a key rotation is noticed before the
// cache expires.
builder.Services.AddJwksKeyResolution(options =>
{
    options.JwksUris[transmitter] = new Uri($"{transmitter}/.well-known/jwks.json");

    // DO NOT COPY THIS LINE INTO A DEPLOYMENT. The default floor of 30 seconds is a rate limit, and the
    // thing it limits is not this sample: the push endpoint accepts a token before any signature is
    // verified, so anyone who can reach it can send a stream of tokens naming key ids nobody ever
    // published, and without the floor each one becomes a fetch against the transmitter. The receiver
    // then works as an amplifier aimed at the party it trusts most.
    //
    // Zero here because the sample restarts its transmitter on purpose to show a rollover, and the point
    // of that exercise is drowned by waiting out a rate limit written for hostile traffic that a
    // one-reader sample does not have. The cost is real and paid in a deployment rather than here: within
    // the floor a token signed by the new key is judged against the old key set and refused, and a
    // refused push is acknowledged out of the transmitter's queue rather than retried, so events
    // dispatched in that window are lost.
    options.RolloverRefetchCooldown = TimeSpan.Zero;
});

// Duplicate suppression is a second line, not the first: RFC 8935 lets a transmitter redeliver whatever
// the earlier response was, so the sink must be idempotent regardless of this cache.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddDistributedReplayCache();

builder.Services.AddSharedSignalsReceiver(new SharedSignalsValidationOptions
{
    ExpectedIssuers = [transmitter],
    ExpectedAudience = self,

    // SSF 1.0 Section 4.1.6 wants "iss" to match the issuer of the stream this event arrived on, not just
    // an issuer the receiver happens to trust. Here the sample has one stream from one transmitter, so the
    // two values coincide - a receiver holding several streams carries one profile per stream.
    StreamIssuer = transmitter,
});

builder.Services.AddSingleton<SessionStore>();
builder.Services.AddSingleton<ISecurityEventSink>(sp => sp.GetRequiredService<SessionStore>());

var app = builder.Build();

// Where the transmitter pushes. The address is this receiver's to choose, and it is what was declared as
// PushEndpointUrl on the other side.
app.MapPushDeliveryEndpoint("/ssf/push");

// So the sample can be checked without reading logs.
app.MapGet("/revoked-sessions", (SessionStore sessions) => sessions.Revoked);

app.Run();
