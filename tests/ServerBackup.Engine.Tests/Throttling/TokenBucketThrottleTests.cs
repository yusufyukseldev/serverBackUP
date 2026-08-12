using System.Diagnostics;
using FluentAssertions;
using ServerBackup.Engine.Throttling;
using Xunit;

namespace ServerBackup.Engine.Tests.Throttling;

public sealed class TokenBucketThrottleTests
{
    /// <summary>
    /// The point of these assertions is the ABSOLUTE rate, not "slower than
    /// nothing": moving N bytes through a bucket configured for R bytes/s must
    /// take about N/R seconds, so a bug that throttles at the wrong rate (or
    /// throttles a different quantity than the one asked for) fails here.
    /// </summary>
    [Fact]
    public async Task Sequential_requests_are_paced_at_the_configured_rate()
    {
        const int rate = 4_000_000;
        const int pieceSize = 64 * 1024;
        const int pieces = 32; // 2 MiB total => ~0.5 s expected
        var totalBytes = (long)pieceSize * pieces;

        var throttle = new TokenBucketThrottle(rate);

        var sw = Stopwatch.StartNew();
        for (var i = 0; i < pieces; i++)
        {
            await throttle.WaitForBudgetAsync(pieceSize);
        }

        sw.Stop();

        AssertPacedAt(sw.Elapsed, totalBytes, rate);
    }

    [Fact]
    public async Task Concurrent_callers_share_one_budget_rather_than_each_getting_the_full_rate()
    {
        const int rate = 4_000_000;
        const int pieceSize = 64 * 1024;
        const int piecesPerCaller = 8;
        const int callers = 4; // 2 MiB total across all callers => ~0.5 s expected
        var totalBytes = (long)pieceSize * piecesPerCaller * callers;

        var throttle = new TokenBucketThrottle(rate);

        var sw = Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, callers).Select(_ => Task.Run(async () =>
        {
            for (var i = 0; i < piecesPerCaller; i++)
            {
                await throttle.WaitForBudgetAsync(pieceSize);
            }
        })));

        sw.Stop();

        // If each caller got its own budget this would finish ~4x too fast.
        AssertPacedAt(sw.Elapsed, totalBytes, rate);
    }

    /// <summary>
    /// A request bigger than the bucket must borrow against future budget
    /// instead of deadlocking waiting for tokens that can never accumulate.
    /// </summary>
    [Fact]
    public async Task A_request_larger_than_the_burst_capacity_still_completes()
    {
        const int rate = 8_000_000;
        const int oversized = 4_000_000; // 50x the 0.1 s burst allowance

        var throttle = new TokenBucketThrottle(rate);

        var sw = Stopwatch.StartNew();
        await throttle.WaitForBudgetAsync(oversized);
        sw.Stop();

        AssertPacedAt(sw.Elapsed, oversized, rate);
    }

    [Fact]
    public async Task Requests_that_fit_the_available_budget_do_not_wait()
    {
        var throttle = new TokenBucketThrottle(100_000_000);

        var sw = Stopwatch.StartNew();
        await throttle.WaitForBudgetAsync(1024);
        sw.Stop();

        sw.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public async Task A_cancelled_token_aborts_the_wait()
    {
        var throttle = new TokenBucketThrottle(1000); // 1 KB/s: the request below would take ~10 s
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var act = async () => await throttle.WaitForBudgetAsync(10_000, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_rate_is_rejected(long rate)
    {
        var act = () => new TokenBucketThrottle(rate);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// Wall-clock assertions are inherently a little noisy, so the band is
    /// generous — but bounded on BOTH sides, because a one-sided "it was
    /// slower" assertion would pass even if the rate were wildly wrong.
    /// </summary>
    private static void AssertPacedAt(TimeSpan elapsed, long totalBytes, long bytesPerSecond)
    {
        var expected = totalBytes / (double)bytesPerSecond;

        elapsed.TotalSeconds.Should().BeGreaterThan(expected * 0.5,
            $"moving {totalBytes} bytes at {bytesPerSecond} B/s cannot take much less than {expected:F2} s");
        elapsed.TotalSeconds.Should().BeLessThan((expected * 1.7) + 0.3,
            $"the throttle must not hold the caller far longer than {expected:F2} s");
    }
}
