using System.Security.Cryptography;

namespace ServerBackup.Core.Tests.Crypto;

internal static class RandomNumberGeneratorFixture
{
    public static byte[] Bytes(int length) => RandomNumberGenerator.GetBytes(length);
}
