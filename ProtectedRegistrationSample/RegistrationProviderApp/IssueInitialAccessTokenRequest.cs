using System.Text.Json.Serialization;

namespace RegistrationProviderApp;

/// <summary>
/// What the operator asks for: who the initial access token is for, and for how many seconds it is valid.
/// </summary>
public sealed record IssueInitialAccessTokenRequest(
    [property: JsonPropertyName("subject")] string? Subject,
    [property: JsonPropertyName("expires_in")] int? ExpiresIn);
