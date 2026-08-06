namespace ServerBackup.Engine.Retention;

/// <summary>
/// Grandfather-Father-Son style retention policy — see plan Faz 8 and
/// restic's `forget` semantics, which this mirrors. Each Keep* rule keeps
/// at most N snapshots for that bucket (the most recent one per bucket);
/// a snapshot can be kept by more than one rule at once.
/// </summary>
public sealed record RetentionPolicy(
    int? KeepLast = null,
    int? KeepHourly = null,
    int? KeepDaily = null,
    int? KeepWeekly = null,
    int? KeepMonthly = null,
    int? KeepYearly = null,
    TimeSpan? KeepWithin = null,
    IReadOnlyList<string>? KeepTags = null);
