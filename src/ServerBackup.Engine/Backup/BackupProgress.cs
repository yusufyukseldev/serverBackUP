namespace ServerBackup.Engine.Backup;

public sealed record BackupProgress(
    long FilesScanned,
    long FilesUnchanged,
    long FilesChanged,
    long NewBlobsWritten,
    long NewBytesWritten,
    long EntriesSkipped = 0);
