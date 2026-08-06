using FluentAssertions;
using ServerBackup.Engine.Vss;
using Xunit;

namespace ServerBackup.Integration.Tests;

public sealed class VssSnapshotSessionTests
{
    [Fact]
    public void IsElevated_reflects_the_current_process_token()
    {
        // This documents the check BackupCommand relies on rather than
        // asserting a fixed value — dev sessions are typically not elevated,
        // CI/service accounts might be either way.
        var act = VssSnapshotSession.IsElevated;

        act.Should().NotThrow();
    }

    [Fact]
    public void Create_without_elevation_throws_a_clear_error_instead_of_a_native_crash()
    {
        if (VssSnapshotSession.IsElevated())
        {
            return; // this test only makes sense in a non-elevated session
        }

        var act = () => VssSnapshotSession.Create([@"C:\"]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*elevated*");
    }

    /// <summary>
    /// Requires an elevated (Administrator) process to actually exercise VSS.
    /// Excluded from normal runs — see CLAUDE.md ("dotnet test --filter
    /// Category!=RequiresAdmin"). Run explicitly from an elevated shell with
    /// `dotnet test --filter Category=RequiresAdmin` to verify on a real box.
    /// </summary>
    [Trait("Category", "RequiresAdmin")]
    [Fact]
    public void Create_snapshots_the_system_volume_and_maps_a_path_into_it()
    {
        using var session = VssSnapshotSession.Create([@"C:\Windows"]);

        var mapped = session.MapPath(@"C:\Windows\System32");

        mapped.Should().NotBe(@"C:\Windows\System32");
        mapped.Should().Contain("HarddiskVolumeShadowCopy");
        Directory.Exists(mapped).Should().BeTrue("the shadow copy path should be a real, readable directory");
    }
}
