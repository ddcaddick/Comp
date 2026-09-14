using System.Security.Cryptography;
using System.Text;

namespace Comp.Infrastructure.Identity;

/// <summary>
/// Refresh tokens are opaque random values, not JWTs — there is nothing a client needs to
/// read out of one. Only the SHA-256 hash is ever persisted; the raw value is returned to
/// the client exactly once, at issuance.
/// </summary>
public static class RefreshTokenGenerator
{
    public static (string RawToken, string Hash) Generate()
    {
        var rawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        return (rawToken, ComputeHash(rawToken));
    }

    public static string ComputeHash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
