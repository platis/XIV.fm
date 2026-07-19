using System.Security.Cryptography;

namespace XIV.fm.Server.Infrastructure.Authentication;

public static class InstallationCredentialGenerator
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
