namespace ServerBackup.Engine.Scanning;

/// <summary>
/// Abstracts where backup source files come from. Only <see cref="LocalSourceProvider"/>
/// exists today; the abstraction exists so a future remote-agent source
/// doesn't require rewriting the scanner or backup engine — see plan Faz 12.
/// </summary>
public interface ISourceProvider
{
    SourceEntry GetEntry(string path);

    IEnumerable<SourceEntry> EnumerateChildren(string directoryPath);

    Stream OpenRead(string filePath);

    /// <summary>SDDL form of the entry's security descriptor, or null if it couldn't be read.</summary>
    string? TryGetSddl(string path);
}
