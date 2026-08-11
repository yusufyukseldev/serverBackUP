using FluentAssertions;
using ServerBackup.Engine.Restore;
using Xunit;

namespace ServerBackup.Integration.Tests;

/// <summary>
/// Tree node names are data read back out of the repository. A rooted or
/// traversing name must never steer a restore outside the chosen target —
/// on a whole-volume snapshot that would mean writing over the live system.
/// </summary>
public sealed class RestorePathContainmentTests
{
    private static readonly string Target = Path.Combine(Path.GetTempPath(), "sb-restore-target");

    [Theory]
    [InlineData("Veri/rapor.txt")]
    [InlineData("C/Kullanicilar/belge.docx")]
    public void Ordinary_relative_paths_resolve_inside_the_target(string relativePath)
    {
        var resolved = RestoreEngine.ResolveUnderRoot(Target, relativePath);

        resolved.Should().StartWith(Path.GetFullPath(Target));
    }

    [Theory]
    [InlineData("C:\\Windows\\System32\\drivers\\etc\\hosts")]
    [InlineData("..\\..\\Windows\\System32\\config")]
    [InlineData("../../escaped.txt")]
    [InlineData("Veri/../../escaped.txt")]
    public void Rooted_or_traversing_paths_are_refused(string relativePath)
    {
        var act = () => RestoreEngine.ResolveUnderRoot(Target, relativePath);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*outside the restore target*");
    }
}
