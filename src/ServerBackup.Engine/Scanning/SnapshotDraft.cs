using ServerBackup.Core.Trees;

namespace ServerBackup.Engine.Scanning;

/// <summary>
/// In-memory result of building a snapshot's tree structure — not yet
/// persisted. Turning this into a catalog row and writing any missing blobs
/// to packs is the backup engine's job (plan Faz 5).
/// </summary>
public sealed record SnapshotDraft(
    IReadOnlyList<string> SourcePaths,
    Tree RootTree,
    byte[] RootTreeBlobId,
    DateTimeOffset StartedAtUtc);
