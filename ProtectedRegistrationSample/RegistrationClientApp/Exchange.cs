using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RegistrationClientApp;

/// <summary>
/// Sends one request of the scenario, prints it with the response, and counts every response whose status is not
/// the one the scenario expects.
/// </summary>
public sealed class Exchange(HttpClient http)
{
    /// <summary>
    /// Members whose values are credentials. They are printed shortened: the full value belongs in the client's
    /// secret store, not in a console log.
    /// </summary>
    private static readonly string[] CredentialMembers = ["client_secret", "registration_access_token", "access_token"];

    private const string JsonMediaType = "application/json";

    private const int ShownCredentialLength = 6;

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private int _step;

    /// <summary>The number of responses whose status was not the expected one.</summary>
    public int Unexpected { get; private set; }

    /// <summary>
    /// Sends <paramref name="request"/>, prints what was sent and received, and returns the JSON body when the status
    /// is the expected one, otherwise null.
    /// </summary>
    public async Task<JsonObject?> SendAsync(string title, HttpRequestMessage request, HttpStatusCode expected)
    {
        Console.WriteLine($"=== {++_step}. {title}");
        Console.WriteLine($"> {request.Method} {request.RequestUri}");
        if (request.Headers.Authorization is { } authorization)
            Console.WriteLine($"> Authorization: {authorization.Scheme} {Shorten(authorization.Parameter)}");
        if (request.Content is { } content)
            Console.WriteLine(Print(await content.ReadAsStringAsync(), content.Headers.ContentType?.MediaType));

        using var response = await http.SendAsync(request);
        Console.WriteLine($"< {(int)response.StatusCode} {response.ReasonPhrase}");
        foreach (var challenge in response.Headers.WwwAuthenticate)
            Console.WriteLine($"< WWW-Authenticate: {challenge}");

        var body = await response.Content.ReadAsStringAsync();
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (body.Length > 0)
            Console.WriteLine(Print(body, mediaType));

        if (response.StatusCode == expected)
        {
            Console.WriteLine($"  as expected: {(int)expected}");
        }
        else
        {
            Unexpected++;
            Console.WriteLine($"  UNEXPECTED: {(int)expected} was expected");
        }

        Console.WriteLine();
        return response.StatusCode == expected && body.Length > 0 && mediaType == JsonMediaType
            ? JsonNode.Parse(body) as JsonObject
            : null;
    }

    private static string Print(string text, string? mediaType)
    {
        if (mediaType != JsonMediaType || JsonNode.Parse(text) is not JsonObject document)
            return text;

        foreach (var member in CredentialMembers)
        {
            if (document[member] is JsonValue value && value.TryGetValue<string>(out var credential))
                document[member] = Shorten(credential);
        }

        return document.ToJsonString(Indented);
    }

    /// <summary>
    /// The first and the last characters of <paramref name="credential"/>. Every JWT this provider issues starts
    /// with the same header, so it is the end that tells one token from another.
    /// </summary>
    private static string? Shorten(string? credential)
        => credential is { Length: > 2 * ShownCredentialLength }
            ? $"{credential[..ShownCredentialLength]}...{credential[^ShownCredentialLength..]}"
            : credential;
}
