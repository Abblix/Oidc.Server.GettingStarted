# Shared Signals: one app tells another that a session is gone

`SharedSignalsSample` is two applications. `TransmitterApp` revokes a user's session and announces it; `ReceiverApp` hears the announcement and closes its own. Between them runs a real push delivery: a signed Security Event Token, posted over HTTPS, verified against a JWK Set the receiver fetches for itself.

The question a reader arrives with is "how do I know the event is genuine", and the answer is a signature, a published key set, an expected issuer and an expected audience. Hosted in a single process all four collapse into the application checking its own word, which is why the sample pays for a second host.

## What it demonstrates

- The transmitter side: a signing key published at `/.well-known/jwks.json`, `AddSecurityEvents` with the CAEP event registry, `AddSharedSignalsTransmitter`, streams declared in `appsettings.json` through `AddSharedSignalsConfiguredStreams`, and one `DispatchAsync` call at the place where the revocation actually happens.
- The receiver side: `AddJwksKeyResolution` pointed at the transmitter as the trust root, `AddSharedSignalsReceiver` carrying the expected issuer and audience, `AddDistributedReplayCache` as a second line against redelivery, `MapPushDeliveryEndpoint` for the incoming POST, and a `SessionStore` implementing `ISecurityEventSink`, which is the only class in the sample the application itself had to write.
- Why a transmitter refuses to deliver into its own network. A receiver names its own delivery endpoint, so a transmitter that POSTs wherever it is told is a server-side request forgery engine with the deployment's network position. Private hosts are refused by default; both halves of this sample run on `localhost`, which is precisely that case, so the operator permits those destinations explicitly through `AllowedReceiverAddresses`.
- What a key rollover looks like from the receiving side, including the window in which it goes wrong.

## Run it

Delivery here is a real HTTPS request from one process to another, so the pair needs slightly more than `dotnet run` twice. From the `Oidc.Server.GettingStarted` root:

**Trust the development certificate.** Both directions are HTTPS between two local hosts: the transmitter POSTs to the receiver, and the receiver fetches the transmitter's key set. Each validates the other's certificate the way any client would.

```shell
dotnet dev-certs https --trust
```

**Start the receiver.** Starting it first is the easier order to read, though not a requirement: a delivery that fails because nothing is listening leaves the event queued, and a later sweep takes it out again.

```shell
dotnet run --project SharedSignalsSample/ReceiverApp
```

Wait for `Now listening on: https://localhost:5102`.

**Start the transmitter in a second terminal.**

```shell
dotnet run --project SharedSignalsSample/TransmitterApp
```

It is ready when the log carries `Sweeping push streams every 00:00:30, ...` and `Now listening on: https://localhost:5101`.

**Revoke a session and wait for the sweep.**

```shell
curl -k -X POST "https://localhost:5101/sessions/alice-laptop-session/revoke?user=alice@example.com"
```

`alice-laptop-session` and `alice@example.com` are yours to choose. This transmitter keeps no sessions of its own: it takes the identifier from the path, puts it in the event as the subject's session, and announces that. In a real deployment the identifier would come from the session the provider is actually ending, and the receiver would be holding the same value against a login of its own, which is what lets it match one to the other. Here only the receiver stores anything, and `GET /revoked-sessions` is that store.

The `202` is the transmitter accepting the event, not the receiver getting it. Delivery happens on the next sweep, so give it up to 30 seconds, then:

```shell
curl -k https://localhost:5102/revoked-sessions
# ["alice-laptop-session"]
```

### Reading the transmitter's log while you wait

The transmitter narrates the delivery, and its last line is the receiver's verdict:

- `Received HTTP response headers ... - 202` - the receiver validated the token and accepted it. This is the run working.
- `... - 400` - the receiver rejected the token. The signature did not verify, or the issuer or audience did not match.
- A warning, `Push delivery failed for stream sample-stream; the sweep continued with the rest`, carrying an exception whose message begins `Refusing to deliver` - the transmitter never sent anything, because the address in `appsettings.json` is one it will not POST to. The rest of that message says why.

Silence in both logs after 30 seconds means the sweep found nothing to deliver: the stream in `appsettings.json` does not cover the event, or the revoke call never reached the transmitter.

### Running them both from one terminal

Nothing here needs two windows.

```shell
dotnet run --project SharedSignalsSample/ReceiverApp > receiver.log 2>&1 &
dotnet run --project SharedSignalsSample/TransmitterApp > transmitter.log 2>&1 &
```

On Windows PowerShell, `Start-Process dotnet -ArgumentList 'run','--project','SharedSignalsSample/ReceiverApp' -RedirectStandardOutput receiver.log` does the same. Either way, read the logs for the two `Now listening` lines rather than assuming the ports came up. A process that failed to start leaves the port quiet in exactly the same way a slow one does.

## Verify it yourself

### The event travels

Read the receiver before revoking anything, not only after:

```shell
curl -k https://localhost:5102/revoked-sessions
# []
```

The empty answer matters as much as the populated one. Without it, `["alice-laptop-session"]` at the end is equally consistent with a store that always said `alice-laptop-session`, and the run proves nothing about delivery.

### The key set publishes the public half only

```shell
curl -k https://localhost:5101/.well-known/jwks.json
```

What comes back is the key's public description: its type, id, algorithm and intended use, plus the modulus and exponent. The private members are stripped by `Sanitize(includePrivateKeys: false)`, which is what lets the receiver verify without being able to forge.

### A rollover is noticed, after a delay

Read the `kid` from the JWK Set, restart `TransmitterApp`, and read it again. It changed, because the sample mints a key per run and mints the id with it. Revoke another session and it arrives: the receiver met a `kid` it did not hold, refetched the key set, and verified against the new key.

Wait about half a minute after the restart before revoking, or the exercise shows the opposite result for a reason worth knowing. The forced refetch has a floor under it, `JwksKeyResolutionOptions.RolloverRefetchCooldown`, 30 seconds by default, so that a flood of tokens naming bogus key ids cannot be used to hammer the issuer. A token arriving inside that window is judged against the stale key set and refused, and a refused push is acknowledged out of the transmitter's queue rather than retried. The event is gone.

That window is the same order as the delivery sweep, which is what makes it easy to hit here and worth knowing about in a deployment: a rotation is not free, and events dispatched in the seconds around one can be lost.

Now fix `KeyId` to a constant and restart again. Every delivery gets a `400` from then on, and no waiting helps. The receiver is caching by key id, and a new key wearing the old name is a key it is confident it already has, so it never refetches at all.

The lesson generalizes past this sample. A key id is not a label for the slot a key sits in; it is the name of that key, and it changes when the key does.

### The trust boundary is real

Point the receiver's `Transmitter` setting at some other address and restart it. Deliveries stop being accepted, and the reason is worth being precise about: the token's `iss` is no longer an issuer this receiver expects, and the profile checks that before it does any signature work. The receiver is not refusing because the POST came from an unexpected place; it never looks at that. It refuses because the event claims an issuer it was not configured to believe.

## Layout

- `TransmitterApp/Program.cs`: the signing key and its `kid`, the JWK Set endpoint, `AddSecurityEvents`, `AddSharedSignalsTransmitter` with the allowed receiver addresses, and the revoke endpoint with its single `DispatchAsync`.
- `TransmitterApp/appsettings.json`: the declared stream: which receiver, which events, which push endpoint.
- `ReceiverApp/Program.cs`: key resolution, the validation options, the replay cache, and the push endpoint.
- `ReceiverApp/SessionStore.cs`: what a revoked session means to this application.

## What the sample deliberately leaves out

The Stream Management API is not mapped. This transmitter's streams come from its configuration file, so nothing needs to create one over HTTP. That API is also the surface that has to be guarded by scope, since whoever can create a stream can ask to be told about your users. `MapSharedSignalsTransmitterEndpoints()` maps it together with the configuration document; the sample maps only the document.

Poll delivery is not shown. Push is the harder half to get right, because it is the one where the transmitter makes an outbound request to an address someone else chose.

An event that names only the user, rather than one of that user's sessions, is accepted and does nothing here. A real receiver answers it by closing every session it holds for that user; `SessionStore` says so where it makes the choice.

## The article behind this sample

- Shared Signals: how identity systems deliver bad news: https://docs.abblix.com/docs/shared-signals-framework
