using System.Security.Cryptography;

namespace XIV.fm.Server.Application.AccountLinks;

internal static class SecretGenerator
{
    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
