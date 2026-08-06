using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ServerBackup.Core.Crypto;

namespace ServerBackup.Engine.Repository;

/// <summary>Tries every key file in keys/ until one unwraps with the given password.</summary>
public static class RepositoryKeyStore
{
    public static async Task<byte[]> UnlockAsync(string repoPath, string password, CancellationToken ct = default)
    {
        var keysDir = Path.Combine(repoPath, "keys");
        if (!Directory.Exists(keysDir))
        {
            throw new InvalidOperationException($"No 'keys' directory found under '{repoPath}'. Is this a repository?");
        }

        var keyFiles = Directory.EnumerateFiles(keysDir, "*.json").ToArray();
        if (keyFiles.Length == 0)
        {
            throw new InvalidOperationException($"No key files found under '{keysDir}'.");
        }

        var passwordBytes = Encoding.UTF8.GetBytes(password);
        Exception? lastError = null;

        foreach (var path in keyFiles)
        {
            var json = await File.ReadAllTextAsync(path, ct);
            var keyFile = JsonSerializer.Deserialize<KeyFileV1>(json)
                ?? throw new InvalidDataException($"Could not parse key file '{path}'.");

            try
            {
                return MasterKeyFile.Unwrap(keyFile, passwordBytes);
            }
            catch (CryptographicException ex)
            {
                lastError = ex;
            }
        }

        throw new UnauthorizedAccessException("The password did not unlock any key file in this repository.", lastError);
    }
}
