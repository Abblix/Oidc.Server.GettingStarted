# Shared Signals: one app tells another that a session is gone

`SharedSignalsSample` is two applications. `TransmitterApp` revokes a user's session and announces it; `ReceiverApp` hears the announcement and closes its own. Between them runs a real push delivery: a signed Security Event Token, posted over HTTPS, verified against a JWK Set the receiver fetches for itself.

The question a reader arrives with is "how do I know the event is genuine", and the answer is a signature, a published key set, an expected issuer and an expected audience. Hosted in a single process all four collapse into the application checking its own word, which is why the sample pays for a second host.

## What it demonstrates

- The transmitter side: a signing key published at `/.well-known/jwks.json`, `AddSecurityEvents` with the CAEP event registry, `AddSharedSignalsTransmitter`, streams declared in `appsettings.json` through `AddSharedSignalsConfiguredStreams`, and one `DispatchAsync` call at the place where the revocation actually happens.
- The receiver side: `AddJwksKeyResolution` pointed at the transmitter as the trust root, `AddSharedSignalsReceiver` carrying the expected issuer and audience, `AddDistributedReplayCache` as a second line against redelivery, `MapPushDeliveryEndpoint` for the incoming POST, and a `SessionStore` implementing `ISecurityEventSink` - the only class in the sample the application itself had to write.
- Why a transmitter refuses to deliver into its own network. A receiver names its own delivery endpoint, so a transmitter that POSTs wherever it is told is a server-side request forgery engine with the deployment's network position. Private hosts are refused by default; both halves of this sample run on `localhost`, which is precisely that case, so the operator names the destination explicitly through `AllowedReceiverAddresses`.
- What a key rollover looks like from the receiving side. The transmitter mints a key per run, and mints its `kid` with it.

## Run it

Delivery here is a real HTTPS request from one process to another, so the pair needs slightly more than `dotnet run` twice. Four steps, from the `Oidc.Server.GettingStarted` root.

**One. Trust the development certificate.** The transmitter POSTs to `https://localhost:5102`, and it validates that certificate like any other client would:

```shell
dotnet dev-certs https --trust
```

**Two. Start the receiver.** Starting it first is the easier order to read, though not a requirement: an undelivered event stays queued and goes out on a later sweep, so a receiver that comes up second still gets it.

```shell
dotnet run --project SharedSignalsSample/ReceiverApp
```

Wait for `Now listening on: https://localhost:5102`.

**Three. Start the transmitter in a second terminal:**

```shell
dotnet run --project SharedSignalsSample/TransmitterApp
```

Two lines say it is ready: `Sweeping push streams every 00:00:30` and `Now listening on: https://localhost:5101`.

**Four. Revoke a session and wait for the sweep.**

```shell
curl -k -X POST "https://localhost:5101/sessions/s-42/revoke?user=alice@example.com"
```

`s-42` and `alice@example.com` are yours to choose. This transmitter keeps no sessions of its own: it takes the identifier from the path, puts it in the event as the subject's session, and announces that. In a real deployment the identifier would come from the session the provider is actually ending, and the receiver would be storing the same value against a login of its own, which is what lets it match one to the other. Here only the receiver stores anything, and `GET /revoked-sessions` is that store.

The `202` is the transmitter accepting the event, not the receiver getting it. Delivery happens on the next sweep, so give it up to 30 seconds, then:

```shell
curl -k https://localhost:5102/revoked-sessions
# ["s-42"]
```

### Reading the transmitter's log while you wait

The transmitter narrates the delivery, and its last line is the receiver's verdict:

- `Received HTTP response headers ... - 202` - the receiver validated the token and accepted it. This is the run working.
- `... - 400` - the receiver rejected the token. The signature did not verify, or the issuer or audience did not match. The usual cause is a restarted transmitter whose key the receiver has not refetched; see "A rollover is noticed" below.
- `Push delivery failed for stream sample-stream` followed by `Refusing to deliver` - the transmitter never sent anything. The address in `appsettings.json` is one it will not POST to, and the message says why.

Silence in both logs after 30 seconds means the sweep found nothing to deliver: the stream in `appsettings.json` does not cover the event, or the revoke call never reached the transmitter.

### Running them both from one terminal

Nothing in the sample needs two windows; it only needs the receiver started first.

```shell
dotnet run --project SharedSignalsSample/ReceiverApp > receiver.log 2>&1 &
dotnet run --project SharedSignalsSample/TransmitterApp > transmitter.log 2>&1 &
```

On Windows PowerShell, `Start-Process dotnet -ArgumentList 'run','--project','SharedSignalsSample/ReceiverApp' -RedirectStandardOutput receiver.log` does the same. Either way, read the logs for the two `Now listening` lines rather than assuming the ports came up - a process that failed to start leaves the port quiet in exactly the same way a slow one does.

## Verify it yourself

### The event travels

Read the receiver before revoking anything, not only after:

```shell
curl -k https://localhost:5102/revoked-sessions
# []
```

The empty answer matters as much as the populated one. Without it, `["s-42"]` at the end is equally consistent with a store that always said `s-42`, and the run proves nothing about delivery.

### The key set is public and partial

`curl -k https://localhost:5101/.well-known/jwks.json` returns the modulus and exponent and nothing else. The private half never leaves the transmitter, which is what lets the receiver verify without being able to forge.

### A rollover is noticed

Read the `kid` from the JWK Set, restart `TransmitterApp`, read it again - it changed, because the key changed. Revoke another session and it still arrives: the receiver met a `kid` it did not hold, refetched the key set, and verified against the new key.

Fix that `kid` to a constant and the same restart breaks delivery instead, with the receiver answering `400` to every push. It is caching by key id, so a new key wearing the old name is a key it believes it already has. This is worth doing once, because in a deployment where the key genuinely rotates the symptom arrives without an obvious cause.

### The trust boundary is real

Point the receiver's `Transmitter` setting at some other address and restart it: the key set no longer matches the signature, and deliveries are refused. The receiver believes the events because of the key it fetched, not because of where the POST came from.

## Layout

- `TransmitterApp/Program.cs`: the signing key and its `kid`, the JWK Set endpoint, `AddSecurityEvents`, `AddSharedSignalsTransmitter` with the allowed receiver address, and the revoke endpoint with its single `DispatchAsync`.
- `TransmitterApp/appsettings.json`: the declared stream - which receiver, which events, which push endpoint.
- `ReceiverApp/Program.cs`: key resolution, the validation options, the replay cache, and the push endpoint.
- `ReceiverApp/SessionStore.cs`: what a revoked session means to this application.

## Two things the sample deliberately leaves out

The Stream Management API is not mapped. This transmitter's streams come from its configuration file, so nothing needs to create one over HTTP - and that API is the surface that has to be guarded by scope, since whoever can create a stream can ask to be told about your users. `MapSharedSignalsTransmitterEndpoints()` maps it together with the configuration document; the sample maps only the document.

Poll delivery is not shown. Push is the harder half to get right, because it is the one where the transmitter makes an outbound request to an address someone else chose.

## The article behind this sample

- Shared Signals: how identity systems deliver bad news: https://docs.abblix.com/docs/shared-signals-framework
