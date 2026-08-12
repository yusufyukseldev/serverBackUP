using System.Diagnostics;

namespace ServerBackup.Engine.Throttling;

/// <summary>
/// Continuous-refill token bucket used to cap the bytes-per-second a backup
/// run pushes through the disk. Callers declare how many bytes they are about
/// to move and are held only long enough to keep the long-run average at the
/// configured rate.
///
/// <para><b>One shared budget for reads AND writes.</b> A single instance is
/// shared by the source-read (chunking) side and the pack-write side of a run,
/// so both draw from the same bucket. That is deliberate: in the common
/// deployment the source volume and the repository live on the same machine —
/// often the same physical disk — and the operator who types "10 MB/s ile
/// sınırla" means "this plan must not push more than 10 MB/s through the
/// disk", not "10 MB/s of reads plus another 10 MB/s of writes". Independent
/// budgets would make the real load depend on the compression ratio (an
/// incompressible source would move ~2x the configured rate, a highly
/// compressible one ~1.1x), which is exactly the unpredictability the limit
/// exists to remove. The cost of the choice is that effective read throughput
/// is rate/(1 + compressionRatio), which is the honest interpretation of a
/// device-level cap.</para>
///
/// <para>Refill is computed from elapsed <see cref="Stopwatch"/> ticks at each
/// call rather than from a timer callback, so the bucket tracks real elapsed
/// time even when nothing asks for budget for a while.</para>
///
/// <para>Thread-safe: safe to call concurrently from the walker/chunker, the
/// compression workers and the single writer loop. The lock is held only for
/// the arithmetic that decides how long to wait; the wait itself happens
/// outside it, so throttled callers do not serialize behind each other.</para>
/// </summary>
public sealed class TokenBucketThrottle
{
    private readonly double _bytesPerSecond;
    private readonly double _burstBytes;
    private readonly Lock _gate = new();

    private double _availableBytes;
    private long _lastRefillTimestamp;

    /// <param name="bytesPerSecond">Sustained rate cap. Must be positive.</param>
    /// <param name="burstSeconds">
    /// How much unused budget the bucket may bank, expressed in seconds of the
    /// rate. Small on purpose: a large burst would let a run saturate the disk
    /// for a moment before the average catches up, which is the very spike an
    /// operator is trying to avoid. Non-zero so that a stream of small blobs
    /// does not pay a <see cref="Task.Delay(TimeSpan)"/> per blob.
    /// </param>
    public TokenBucketThrottle(long bytesPerSecond, double burstSeconds = 0.1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(bytesPerSecond, 0);
        ArgumentOutOfRangeException.ThrowIfNegative(burstSeconds);

        _bytesPerSecond = bytesPerSecond;
        _burstBytes = bytesPerSecond * burstSeconds;
        _availableBytes = 0; // start empty: no free head-start on the first second
        _lastRefillTimestamp = Stopwatch.GetTimestamp();
    }

    /// <summary>Configured sustained rate, in bytes per second.</summary>
    public long BytesPerSecond => (long)_bytesPerSecond;

    /// <summary>
    /// Blocks the caller until <paramref name="byteCount"/> bytes fit inside
    /// the configured rate, then returns. A request larger than the bucket is
    /// never rejected — it simply borrows against future budget and waits the
    /// corresponding time, so a single 4 MiB chunk at 1 MB/s costs ~4 s rather
    /// than deadlocking.
    /// <para>Returns a completed task (no state machine, no allocation) when
    /// budget is already available, which is the common case for small
    /// blobs.</para>
    /// </summary>
    public Task WaitForBudgetAsync(int byteCount, CancellationToken ct = default)
    {
        if (byteCount <= 0)
        {
            return Task.CompletedTask;
        }

        if (ct.IsCancellationRequested)
        {
            return Task.FromCanceled(ct);
        }

        double waitSeconds;
        lock (_gate)
        {
            var now = Stopwatch.GetTimestamp();
            var elapsedSeconds = (now - _lastRefillTimestamp) / (double)Stopwatch.Frequency;
            _lastRefillTimestamp = now;

            _availableBytes = Math.Min(_burstBytes, _availableBytes + (elapsedSeconds * _bytesPerSecond));

            // Debt is allowed to go negative: every caller subtracts its own
            // cost immediately and then waits out exactly the deficit it
            // created. That keeps concurrent callers from all being told to
            // wait for the same tokens (which would overshoot the rate) while
            // still letting them wait in parallel.
            _availableBytes -= byteCount;
            waitSeconds = _availableBytes >= 0 ? 0 : -_availableBytes / _bytesPerSecond;
        }

        return waitSeconds <= 0
            ? Task.CompletedTask
            : Task.Delay(TimeSpan.FromSeconds(waitSeconds), ct);
    }
}
