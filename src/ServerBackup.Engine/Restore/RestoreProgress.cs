namespace ServerBackup.Engine.Restore;

public sealed record RestoreProgress(
    long FilesPlanned,
    long FilesCompleted,
    long BytesPlanned,
    long BytesCompleted);
