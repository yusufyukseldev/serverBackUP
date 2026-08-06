namespace ServerBackup.Engine.Prune;

public sealed record PruneResult(
    bool DryRun,
    IReadOnlyList<string> SnapshotsToDelete,
    IReadOnlyList<string> PacksToDelete,
    IReadOnlyList<string> PacksToRepack,
    long BytesFreed);
