using System.Security.Cryptography;

namespace ServerBackup.Engine.Repository;

/// <summary>
/// Password-less key access for scheduled/service runs — the master key is
/// wrapped with Windows DPAPI (LocalMachine scope) instead of a password, so
/// the Windows Service can unlock a repository without a human present. See
/// docs/format-spec.md "Servis İçin Parolasız Açılış": the documented
/// trade-off is that SYSTEM-level compromise of this machine can open the
/// repository, which is why this is opt-in per repository, not the default.
/// </summary>
public static class UnattendedKeyStore
{
    private const string FileName = "unattended.dat";

    public static bool IsEnabled(string repoPath) => File.Exists(FilePath(repoPath));

    public static void Enable(string repoPath, ReadOnlySpan<byte> masterKey)
    {
        var protectedBytes = ProtectedData.Protect(masterKey.ToArray(), optionalEntropy: null, DataProtectionScope.LocalMachine);
        File.WriteAllBytes(FilePath(repoPath), protectedBytes);
    }

    public static byte[] Unlock(string repoPath)
    {
        var protectedBytes = File.ReadAllBytes(FilePath(repoPath));
        return ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.LocalMachine);
    }

    public static void Disable(string repoPath)
    {
        var path = FilePath(repoPath);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string FilePath(string repoPath) => Path.Combine(repoPath, "keys", FileName);
}
