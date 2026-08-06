namespace ServerBackup.Engine.Notifications;

public enum NotificationSeverity
{
    Information,
    Warning,
    Error,
}

public interface INotifier
{
    void Notify(string title, string message, NotificationSeverity severity);
}
