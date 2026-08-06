using System.Diagnostics;

namespace ServerBackup.Engine.Notifications;

/// <summary>
/// Writes to the Windows Application event log — see plan Faz 11. Email
/// notification is intentionally not implemented: no SMTP infrastructure
/// could be verified in this environment, and shipping unverified network
/// code that sends credentials/messages would be worse than not shipping
/// it. <see cref="INotifier"/> is the extension point for adding it later.
/// </summary>
public sealed class WindowsEventLogNotifier : INotifier
{
    public const string SourceName = "ServerBackup";

    public static bool IsSourceRegistered()
    {
        try
        {
            return EventLog.SourceExists(SourceName);
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>Requires Administrator — call once at install time (see scripts/install-service.ps1).</summary>
    public static void RegisterSource()
    {
        if (!EventLog.SourceExists(SourceName))
        {
            EventLog.CreateEventSource(SourceName, "Application");
        }
    }

    public void Notify(string title, string message, NotificationSeverity severity)
    {
        var entryType = severity switch
        {
            NotificationSeverity.Error => EventLogEntryType.Error,
            NotificationSeverity.Warning => EventLogEntryType.Warning,
            _ => EventLogEntryType.Information,
        };

        try
        {
            EventLog.WriteEntry(SourceName, $"{title}\n{message}", entryType);
        }
        catch (Exception)
        {
            // A failed notification must never fail the backup/prune job itself.
        }
    }
}
