using ServerBackup.Service.Scheduling;
using ServerBackup.Service.Storage;

namespace ServerBackup.Service.Tests.Components;

/// <summary>
/// RepositoryRegistry persists its list, and its default location is
/// %ProgramData%. Tests must never write there, so every one of them gets its
/// own throwaway state directory.
/// </summary>
internal sealed class TestRepositoryRegistry : IDisposable
{
    private readonly string _stateDirectory =
        Path.Combine(Path.GetTempPath(), "sb-registry-" + Guid.NewGuid().ToString("n"));

    public RepositoryRegistry Create(params string[] repoPaths) => new(new ServerBackupOptions
    {
        Repositories = [.. repoPaths],
        DataDirectory = _stateDirectory,
    });

    public void Dispose()
    {
        if (Directory.Exists(_stateDirectory))
        {
            Directory.Delete(_stateDirectory, recursive: true);
        }
    }
}
