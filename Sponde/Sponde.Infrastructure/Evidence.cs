using System.Security.Cryptography;
using System.Text;

namespace Sponde.Infrastructure;

public static class Evidence
{
    public static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static Guid Id(string key) => new(SHA256.HashData(Encoding.UTF8.GetBytes(key)).AsSpan(0, 16));
}
