namespace ServerBackup.Service.Components.Ui;

public sealed record LogLine(DateTimeOffset At, string Level, string Message);
