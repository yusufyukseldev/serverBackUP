using System.Text;

namespace ServerBackup.Engine.Backup;

/// <summary>
/// Single-writer repository lock: an exclusively-opened file at
/// locks/repo.lock. FileShare.None gives real OS-enforced mutual exclusion —
/// a second process's attempt to open the same file fails immediately rather
/// than relying on advisory metadata. The file's contents (PID/host/time)
/// are for diagnostics only.
/// </summary>
public sealed class RepositoryLock : IDisposable
{
    private readonly FileStream _stream;
    private readonly string _path;

    private RepositoryLock(FileStream stream, string path)
    {
        _stream = stream;
        _path = path;
    }

    public static RepositoryLock Acquire(string repoPath)
    {
        var locksDir = Path.Combine(repoPath, "locks");
        Directory.CreateDirectory(locksDir);
        var path = Path.Combine(locksDir, "repo.lock");

        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                $"Repository is already locked by another process (could not exclusively open '{path}').", ex);
        }

        var info = $$"""{"pid":{{Environment.ProcessId}},"host":"{{Environment.MachineName}}","startedAtUtc":"{{DateTimeOffset.UtcNow:o}}"}""";
        var bytes = Encoding.UTF8.GetBytes(info);
        stream.Write(bytes);
        stream.Flush();

        return new RepositoryLock(stream, path);
    }

    public void Dispose()
    {
        _stream.Dispose();
        try
        {
            File.Delete(_path);
        }
        catch (IOException)
        {
            // Best effort — the exclusive handle is already released, which is
            // what actually matters for the next writer.
        }
    }
}
