using FluentAssertions;
using ServerBackup.Engine.Backup;
using Xunit;

namespace ServerBackup.Engine.Tests.Backup;

public sealed class AnomalyDetectorTests
{
    [Fact]
    public void No_anomaly_when_changes_are_within_normal_range()
    {
        var policy = new AnomalyPolicy();

        var report = AnomalyDetector.Evaluate(policy, parentFileCount: 100, changedCount: 5, deletedCount: 2, newOrChangedFileNames: ["report.docx"]);

        report.Detected.Should().BeFalse();
    }

    [Fact]
    public void Detects_a_sudden_bulk_change_ratio_above_threshold()
    {
        var policy = new AnomalyPolicy(ChangedOrDeletedRatioThreshold: 0.5, MinimumParentFileCount: 20);

        // 60 of 100 files changed/deleted — 60% > 50% threshold.
        var report = AnomalyDetector.Evaluate(policy, parentFileCount: 100, changedCount: 40, deletedCount: 20, newOrChangedFileNames: []);

        report.Detected.Should().BeTrue();
        report.Reasons.Should().ContainMatch("*%60*");
    }

    [Fact]
    public void Does_not_trigger_the_bulk_ratio_check_below_the_minimum_parent_file_count()
    {
        var policy = new AnomalyPolicy(ChangedOrDeletedRatioThreshold: 0.5, MinimumParentFileCount: 20);

        // 100% changed, but only 5 files total — too small a sample to judge.
        var report = AnomalyDetector.Evaluate(policy, parentFileCount: 5, changedCount: 5, deletedCount: 0, newOrChangedFileNames: []);

        report.Detected.Should().BeFalse();
    }

    [Fact]
    public void Detects_known_ransomware_extensions_even_with_low_volume()
    {
        var policy = new AnomalyPolicy();

        var report = AnomalyDetector.Evaluate(
            policy, parentFileCount: 1000, changedCount: 2, deletedCount: 0,
            newOrChangedFileNames: ["invoice.docx.locked", "photo.jpg"]);

        report.Detected.Should().BeTrue();
        report.Reasons.Should().ContainMatch("*.locked*");
    }

    [Fact]
    public void A_custom_suspicious_extension_list_is_honored()
    {
        var policy = new AnomalyPolicy(SuspiciousExtensions: [".mysterious"]);

        var report = AnomalyDetector.Evaluate(
            policy, parentFileCount: 1000, changedCount: 1, deletedCount: 0, newOrChangedFileNames: ["a.mysterious"]);

        report.Detected.Should().BeTrue();
    }

    [Fact]
    public void Ordinary_extensions_never_trigger_the_extension_check()
    {
        var policy = new AnomalyPolicy();

        var report = AnomalyDetector.Evaluate(
            policy, parentFileCount: 1000, changedCount: 3, deletedCount: 0,
            newOrChangedFileNames: ["a.docx", "b.xlsx", "c.pdf"]);

        report.Detected.Should().BeFalse();
    }

    [Fact]
    public void Both_signals_can_fire_together_and_both_reasons_are_reported()
    {
        var policy = new AnomalyPolicy(ChangedOrDeletedRatioThreshold: 0.3, MinimumParentFileCount: 10);

        var report = AnomalyDetector.Evaluate(
            policy, parentFileCount: 10, changedCount: 5, deletedCount: 0,
            newOrChangedFileNames: ["a.encrypted"]);

        report.Detected.Should().BeTrue();
        report.Reasons.Should().HaveCount(2);
    }
}
