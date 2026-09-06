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
//
// That refetch has a floor under it - JwksKeyResolutionOptions.RolloverRefetchCooldown, 30 seconds by
// default - so a bogus kid cannot be used to hammer the issuer. A token arriving inside that window is
// refused against the stale key set, and a refused push is dropped rather than retried, so an event
// delivered in the first seconds after a rotation is lost.
builder.Services.AddJwksKeyResolution(options =>
    options.JwksUris[transmitter] = new Uri($"{transmitter}/.well-known/jwks.json"));

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
