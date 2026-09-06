using Abblix.SecurityEvents.CAEP;
using Abblix.SecurityEvents.Delivery;
using Abblix.SecurityEvents.Subjects;
using Abblix.SecurityEvents.Validation;

namespace ReceiverApp;

/// <summary>
/// The part only the application can write: what a revoked session means here. Everything before this
/// point - signature, issuer, audience, freshness, the REQUIRED jti - was decided by the validation
/// profile, so a token reaching this method is one this receiver already agreed to believe.
/// </summary>
public sealed class SessionStore(ILogger<SessionStore> logger) : ISecurityEventSink
{
    private readonly HashSet<string> _revoked = [];

    public IReadOnlyCollection<string> Revoked
    {
        get { lock (_revoked) return _revoked.ToArray(); }
    }

    public Task<DeliveryError?> ConsumeAsync(
        ValidatedSecurityEventToken token,
        CancellationToken cancellationToken = default)
    {
        // A transmitter may name a session, or only the user - and then every session that user has here is
        // what is being talked about. This sample handles only the first case: an event naming the user
        // alone falls through below and nothing is closed. A real receiver answers it by revoking every
        // session it holds for that user, which is why the two are worth telling apart at all.
        var session = (token.Token.GetSubjectId() as ComplexSubject)?.Session as OpaqueSubject;

        if (token.EventPayloads?.ContainsKey(CaepEventTypes.SessionRevoked) == true && session is not null)
        {
            lock (_revoked) _revoked.Add(session.Id);
            logger.LogInformation("Session {SessionId} revoked by the transmitter", session.Id);
        }

        // Returning null accepts the delivery. A DeliveryError here is how a receiver tells the
        // transmitter that the event was understood but could not be acted on.
        return Task.FromResult<DeliveryError?>(null);
    }
}
