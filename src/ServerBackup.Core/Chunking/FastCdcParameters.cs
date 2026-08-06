namespace ServerBackup.Core.Chunking;

/// <summary>
/// Fixed v1 FastCDC normalized-chunking parameters — see docs/format-spec.md
/// "FastCDC Chunker Parametreleri". Changing these changes dedup boundaries
/// for every existing repository; do not tune casually.
/// </summary>
public static class FastCdcParameters
{
    public const int MinSize = 256 * 1024;
    public const int NormalSize = 1024 * 1024;
    public const int MaxSize = 4 * 1024 * 1024;

    public const int MaskSBits = 22;
    public const int MaskLBits = 18;

    /// <summary>
    /// Mask used in the [MinSize, NormalSize) region. More set bits than
    /// <see cref="MaskL"/> means the cut condition is harder to satisfy there,
    /// which discourages chunks from ending before NormalSize.
    /// </summary>
    public const ulong MaskS = (1UL << MaskSBits) - 1;

    /// <summary>
    /// Mask used in the [NormalSize, MaxSize) region. Fewer set bits than
    /// <see cref="MaskS"/> means the cut condition is easier to satisfy there,
    /// which pulls chunks back toward NormalSize instead of drifting to MaxSize.
    /// </summary>
    public const ulong MaskL = (1UL << MaskLBits) - 1;
}
