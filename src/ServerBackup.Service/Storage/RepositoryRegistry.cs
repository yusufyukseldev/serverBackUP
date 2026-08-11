using System.Text.Json;
using ServerBackup.Service.Scheduling;

namespace ServerBackup.Service.Storage;

/// <summary>
/// The set of repositories this service manages. It started life as a
/// read-only appsettings.json list, which meant attaching a new backup target
/// required editing a file on the server and restarting the service. The list
/// now lives in its own file that the panel can write, and every reader goes
/// through here so a change takes effect without a restart.
/// </summary>
public sealed class RepositoryRegistry
{
    private const string FileName = "repositories.json";

    private readonly string _filePath;
    private readonly Lock _gate = new();
    private List<string> _paths;

    public RepositoryRegistry(ServerBackupOptions options)
    {
        _filePath = Path.Combine(options.ResolveDataDirectory(), FileName);
        _paths = Load() ?? Seed(options.Repositories);
    }

    /// <summary>A snapshot; callers must not assume it stays current across an await.</summary>
    public IReadOnlyList<string> Paths
    {
        get
        {
            lock (_gate)
            {
                return [.. _paths];
            }
        }
    }

    public bool Contains(string repoPath)
    {
        var full = Normalize(repoPath);
        lock (_gate)
        {
            return _paths.Any(p => string.Equals(Normalize(p), full, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Returns false when the path is already registered.</summary>
    public bool Add(string repoPath)
    {
        var full = Normalize(repoPath);

        lock (_gate)
        {
            if (_paths.Any(p => string.Equals(Normalize(p), full, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            _paths = [.. _paths, full];
            Save(_paths);
            return true;
        }
    }

    /// <summary>Unregisters the repository. Nothing on disk is touched — the backups stay where they are.</summary>
    public bool Remove(string repoPath)
    {
        var full = Normalize(repoPath);

        lock (_gate)
        {
            var remaining = _paths.Where(p => !string.Equals(Normalize(p), full, StringComparison.OrdinalIgnoreCase)).ToList();
            if (remaining.Count == _paths.Count)
            {
                return false;
            }

            _paths = remaining;
            Save(_paths);
            return true;
        }
    }

    private List<string>? Load()
    {
        try
        {
            return File.Exists(_filePath)
                ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_filePath))
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A registry we cannot read must not take the service down: the
            // configured list still gets the operator to a working panel.
            return null;
        }
    }

    /// <summary>First run on an existing install: whatever appsettings.json listed becomes the starting set.</summary>
    private List<string> Seed(IEnumerable<string> configured)
    {
        var seeded = configured.Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Save(seeded);
        return seeded;
    }

    private void Save(List<string> paths)
    {
        var json = JsonSerializer.Serialize(paths, new JsonSerializerOptions { WriteIndented = true });

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

            // Write-then-replace: a crash mid-write must not leave the service
            // with a truncated list and no idea where its repositories are.
            var temporaryPath = _filePath + ".tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Depo listesi '{_filePath}' konumuna yazılamadı. Servis hesabının bu klasöre yazma izni olmalı.", ex);
        }
    }

    private static string Normalize(string repoPath)
    {
        var full = Path.GetFullPath(repoPath);
        var trimmed = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // "C:\" would trim down to "C:", which means "the current directory on
        // C:" to every path API that sees it afterwards.
        return trimmed.Length == 0 || trimmed.EndsWith(':') ? full : trimmed;
    }
}
