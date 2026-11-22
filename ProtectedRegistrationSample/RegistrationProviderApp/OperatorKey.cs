using System.Security.Cryptography;
using System.Text;

namespace RegistrationProviderApp;

/// <summary>
/// The key the operator presents to the operator endpoints, known to this provider only by its hash.
/// </summary>
/// <remarks>
/// A plain SHA-256 is enough here, unlike for a password: the key is 32 random bytes rather than something a person
/// chose, so there is no dictionary to try, and a slow hash would only slow every operator call.
/// </remarks>
public sealed class OperatorKey
{
    private readonly byte[] _hash;

    private OperatorKey(byte[] hash) => _hash = hash;

    /// <summary>
    /// The key whose hash <paramref name="configuration"/> holds under <paramref name="settingName"/>.
    /// </summary>
    public static OperatorKey FromConfiguration(IConfiguration configuration, string settingName)
    {
        var value = configuration[settingName];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"{settingName} is not configured: set it to the base64 SHA-256 hash of the operator key");
        }

        byte[] hash;
        try
        {
            hash = Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                $"{settingName} is not base64: set it to the base64 SHA-256 hash of the operator key");
        }

        if (hash.Length != SHA256.HashSizeInBytes)
        {
            throw new InvalidOperationException(
                $"{settingName} holds {hash.Length} bytes, while a SHA-256 hash has {SHA256.HashSizeInBytes}");
        }

        return new OperatorKey(hash);
    }

    /// <summary>
    /// Whether <paramref name="presented"/> is the operator key, compared in time that does not depend on where the
    /// first difference is, so the response time tells a caller nothing about how close a guess came.
    /// </summary>
    public bool Matches(string presented)
        => CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(presented)), _hash);
}
