using System.Security.AccessControl;
using FluentAssertions;
using ServerBackup.Engine.Scanning;
using Xunit;

namespace ServerBackup.Engine.Tests.Scanning;

public sealed class LocalSourceProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sb-lsp-test-" + Guid.NewGuid().ToString("n"));

    public LocalSourceProviderTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void GetEntry_reports_correct_metadata_for_a_real_file()
    {
        var filePath = Path.Combine(_root, "file.txt");
        File.WriteAllText(filePath, "hello");

        var provider = new LocalSourceProvider();
        var entry = provider.GetEntry(filePath);

        entry.IsDirectory.Should().BeFalse();
        entry.Size.Should().Be(5);
        entry.Name.Should().Be("file.txt");
        entry.IsReparsePoint.Should().BeFalse();
    }

    [Fact]
    public void EnumerateChildren_lists_real_files_and_directories()
    {
        Directory.CreateDirectory(Path.Combine(_root, "subdir"));
        File.WriteAllText(Path.Combine(_root, "a.txt"), "a");

        var provider = new LocalSourceProvider();
        var children = provider.EnumerateChildren(_root).ToList();

        children.Select(c => c.Name).Should().BeEquivalentTo(["subdir", "a.txt"]);
        children.Single(c => c.Name == "subdir").IsDirectory.Should().BeTrue();
        children.Single(c => c.Name == "a.txt").IsDirectory.Should().BeFalse();
    }

    [Fact]
    public void OpenRead_returns_the_real_file_contents()
    {
        var filePath = Path.Combine(_root, "content.txt");
        File.WriteAllText(filePath, "the actual bytes");

        var provider = new LocalSourceProvider();
        using var stream = provider.OpenRead(filePath);
        using var reader = new StreamReader(stream);

        reader.ReadToEnd().Should().Be("the actual bytes");
    }

    [Fact]
    public void TryGetSddl_returns_a_valid_parseable_sddl_string_for_a_real_file()
    {
        var filePath = Path.Combine(_root, "acl-test.txt");
        File.WriteAllText(filePath, "x");

        var provider = new LocalSourceProvider();
        var sddl = provider.TryGetSddl(filePath);

        sddl.Should().NotBeNullOrEmpty();

        // Round-trip: a real SDDL string must be parseable back into a
        // security descriptor and re-serialize without throwing.
        var descriptor = new RawSecurityDescriptor(sddl!);
        var reformatted = descriptor.GetSddlForm(AccessControlSections.All);
        reformatted.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void TryGetSddl_returns_a_valid_sddl_string_for_a_real_directory()
    {
        var dirPath = Path.Combine(_root, "acl-dir");
        Directory.CreateDirectory(dirPath);

        var provider = new LocalSourceProvider();
        var sddl = provider.TryGetSddl(dirPath);

        sddl.Should().NotBeNullOrEmpty();
        var act = () => new RawSecurityDescriptor(sddl!);
        act.Should().NotThrow();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
