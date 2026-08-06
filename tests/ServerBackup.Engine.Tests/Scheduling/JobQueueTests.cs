using FluentAssertions;
using ServerBackup.Engine.Scheduling;
using Xunit;

namespace ServerBackup.Engine.Tests.Scheduling;

public sealed class JobQueueTests
{
    [Fact]
    public async Task Items_are_read_back_in_the_order_they_were_written()
    {
        var queue = new JobQueue();
        await queue.Writer.WriteAsync(new ScheduledJob("repo", "job1", "plan1"));
        await queue.Writer.WriteAsync(new ScheduledJob("repo", "job2", "plan1"));
        queue.Writer.Complete();

        var items = new List<ScheduledJob>();
        await foreach (var item in queue.Reader.ReadAllAsync())
        {
            items.Add(item);
        }

        items.Select(i => i.JobId).Should().Equal("job1", "job2");
    }

    [Fact]
    public async Task Completing_the_writer_ends_enumeration_once_drained()
    {
        var queue = new JobQueue();
        await queue.Writer.WriteAsync(new ScheduledJob("repo", "job1", "plan1"));
        queue.Writer.Complete();

        var readTask = Task.Run(async () =>
        {
            var count = 0;
            await foreach (var _ in queue.Reader.ReadAllAsync())
            {
                count++;
            }

            return count;
        });

        var completed = await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(5)));
        completed.Should().Be(readTask, "the reader must terminate once the writer completes and the queue drains");
        (await readTask).Should().Be(1);
    }
}
