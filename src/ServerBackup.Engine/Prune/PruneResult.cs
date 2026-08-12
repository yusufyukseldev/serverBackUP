namespace ServerBackup.Engine.Prune;

public sealed record PruneResult(
    bool DryRun,
    IReadOnlyList<string> SnapshotsToDelete,
    IReadOnlyList<string> PacksToDelete,
    IReadOnlyList<string> PacksToRepack,
    long BytesFreed);

/// <summary>Why a snapshot the operator explicitly asked to delete was kept anyway.</summary>
public enum ManualPruneRefusalReason
{
    /// <summary>The repository is append-only: nothing can ever be deleted.</summary>
    AppendOnly,

    /// <summary>The snapshot is younger than the repository's immutability window.</summary>
    ImmutabilityWindow,

    /// <summary>No snapshot with that id exists in this repository.</summary>
    NotFound,
}

/// <summary>One refused id plus the reason, so the caller can explain it to the operator.</summary>
public sealed record ManualPruneRefusal(string SnapshotId, ManualPruneRefusalReason Reason);

/// <summary>
/// Outcome of <see cref="PruneEngine.RunManualAsync"/>. Field names mirror
/// <see cref="PruneResult"/> so a panel can bind both the same way; the one
/// addition is <see cref="Refused"/>.
/// <list type="bullet">
/// <item><description><c>DryRun</c> — true when nothing was touched and the lists are a preview.</description></item>
/// <item><description><c>SnapshotsToDelete</c> — ids actually deleted (or, in a dry run, that would be).
/// Always a subset of the requested ids.</description></item>
/// <item><description><c>Refused</c> — requested ids that were NOT deleted, each with its reason.
/// Populated in dry runs too. Requested ids appear in exactly one of these two lists.</description></item>
/// <item><description><c>PacksToDelete</c> / <c>PacksToRepack</c> — packs swept as a consequence.</description></item>
/// <item><description><c>BytesFreed</c> — 0 in a dry run.</description></item>
/// </list>
/// </summary>
public sealed record ManualPruneResult(
    bool DryRun,
    IReadOnlyList<string> SnapshotsToDelete,
    IReadOnlyList<ManualPruneRefusal> Refused,
    IReadOnlyList<string> PacksToDelete,
    IReadOnlyList<string> PacksToRepack,
    long BytesFreed);
