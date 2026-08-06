using System.Threading.Channels;

namespace ServerBackup.Engine.Scheduling;

/// <summary>Unbounded queue of due jobs waiting for a worker — the poller only ever adds a plan once per due occurrence, so unbounded is safe.</summary>
public sealed class JobQueue
{
    private readonly Channel<ScheduledJob> _channel = Channel.CreateUnbounded<ScheduledJob>();

    public ChannelWriter<ScheduledJob> Writer => _channel.Writer;

    public ChannelReader<ScheduledJob> Reader => _channel.Reader;
}
