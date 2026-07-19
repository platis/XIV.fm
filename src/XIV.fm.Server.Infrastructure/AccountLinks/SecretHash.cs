using System.Security.Cryptography;
using System.Text;

namespace XIV.fm.Server.Infrastructure.AccountLinks;

internal static class SecretHash
{
    public static string Compute(string secret) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}
