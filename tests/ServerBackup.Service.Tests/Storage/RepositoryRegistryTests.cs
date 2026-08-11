using FluentAssertions;
using ServerBackup.Service.Scheduling;
using ServerBackup.Service.Storage;
using Xunit;

namespace ServerBackup.Service.Tests.Storage;

public sealed class RepositoryRegistryTests : IDisposable
{
    private readonly string _stateDirectory =
        Path.Combine(Path.GetTempPath(), "sb-reg-" + Guid.NewGuid().ToString("n"));

    [Fact]
    public void The_configured_list_seeds_the_registry_on_first_run()
    {
        var registry = Create([@"C:\Depo\Bir", @"C:\Depo\Iki"]);

        registry.Paths.Should().HaveCount(2);
        registry.Contains(@"C:\Depo\Bir").Should().BeTrue();
    }

    [Fact]
    public void A_change_survives_a_restart_and_the_configured_list_no_longer_overrides_it()
    {
        var first = Create([@"C:\Depo\Bir"]);
        first.Add(@"C:\Depo\Iki");
        first.Remove(@"C:\Depo\Bir");

        // Same state directory, same appsettings: a fresh process must see what
        // the panel did, not what the config file still says.
        var second = Create([@"C:\Depo\Bir"]);

        second.Paths.Should().ContainSingle().Which.Should().Be(@"C:\Depo\Iki");
    }

    [Theory]
    [InlineData(@"C:\Depo\Bir\")]
    [InlineData(@"C:\depo\bir")]
    [InlineData(@"C:\Depo\Iki\..\Bir")]
    public void The_same_folder_under_a_different_spelling_is_not_added_twice(string spelling)
    {
        var registry = Create([@"C:\Depo\Bir"]);

        registry.Add(spelling).Should().BeFalse();
        registry.Paths.Should().ContainSingle();
    }

    [Fact]
    public void Removing_something_that_is_not_registered_reports_it_rather_than_throwing()
    {
        var registry = Create([@"C:\Depo\Bir"]);

        registry.Remove(@"C:\Depo\Yok").Should().BeFalse();
        registry.Paths.Should().ContainSingle();
    }

    [Fact]
    public void A_drive_root_keeps_its_separator_so_it_stays_an_absolute_path()
    {
        var registry = Create([]);

        registry.Add(@"C:\").Should().BeTrue();

        registry.Paths.Should().ContainSingle().Which.Should().Be(@"C:\",
            "trimming it to 'C:' would mean 'the current directory on C:' to every path API afterwards");
    }

    private RepositoryRegistry Create(string[] configured) => new(new ServerBackupOptions
    {
        Repositories = [.. configured],
        DataDirectory = _stateDirectory,
    });

    public void Dispose()
    {
        if (Directory.Exists(_stateDirectory))
        {
            Directory.Delete(_stateDirectory, recursive: true);
        }
    }
}
