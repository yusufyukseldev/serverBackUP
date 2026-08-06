using System.Text;
using FluentAssertions;
using ServerBackup.Core.Chunking;
using ServerBackup.Core.Crypto;
using ServerBackup.Core.Trees;
using ServerBackup.Engine.Scanning;
using Xunit;

namespace ServerBackup.Engine.Tests.Scanning;

public sealed class TreeBuilderTests
{
    private static readonly byte[] IdKey = SubKeys.Derive(new byte[32], SubKeys.ChunkIdInfo);
    private static readonly FastCdcChunker Chunker = new(GearTableFactory.Derive(new byte[32]));

    private static TreeBuilder NewBuilder(FakeSourceProvider provider, ScanFilter? filter = null) =>
        new(provider, Chunker, IdKey, filter);

    [Fact]
    public void BuildTree_captures_a_synthetic_tree_with_deep_paths_unicode_names_and_empty_entries()
    {
        var provider = new FakeSourceProvider();
        provider.AddDirectory("C:/root");
        provider.AddDirectory("C:/root/a");
        provider.AddDirectory("C:/root/a/b");
        provider.AddDirectory("C:/root/a/b/c"); // deep path, empty leaf directory
        provider.AddFile("C:/root/a/b/rapor-ü-ş-ç.xlsx", "içerik"u8.ToArray()); // unicode name
        provider.AddFile("C:/root/a/empty.dat", []); // 0-byte file

        var tree = NewBuilder(provider).BuildTree("C:/root");

        tree.Nodes.Should().HaveCount(1);
        var a = tree.Nodes.Single();
        a.Name.Should().Be("a");
        a.Kind.Should().Be(TreeNodeKind.Directory);
        a.SubTreeBlobIdHex.Should().NotBeNullOrEmpty();

        var aTree = FindSubTree(provider, "C:/root/a");
        var emptyFile = aTree.Nodes.Single(n => n.Name == "empty.dat");
        emptyFile.Kind.Should().Be(TreeNodeKind.File);
        emptyFile.Size.Should().Be(0);
        emptyFile.ChunkBlobIdsHex.Should().NotBeNull().And.BeEmpty();

        var bNode = aTree.Nodes.Single(n => n.Name == "b");
        var bTree = FindSubTree(provider, "C:/root/a/b");
        var unicodeFile = bTree.Nodes.Single(n => n.Name == "rapor-ü-ş-ç.xlsx");
        unicodeFile.ChunkBlobIdsHex.Should().ContainSingle();

        var cNode = bTree.Nodes.Single(n => n.Name == "c");
        var cTree = FindSubTree(provider, "C:/root/a/b/c");
        cTree.Nodes.Should().BeEmpty();
        cNode.Kind.Should().Be(TreeNodeKind.Directory);
        cNode.SubTreeBlobIdHex.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Identical_file_contents_produce_identical_chunk_id_lists()
    {
        var provider = new FakeSourceProvider();
        provider.AddDirectory("C:/root");
        var content = Encoding.UTF8.GetBytes("the exact same bytes, twice");
        provider.AddFile("C:/root/one.txt", content);
        provider.AddFile("C:/root/two.txt", content);

        var tree = NewBuilder(provider).BuildTree("C:/root");

        var one = tree.Nodes.Single(n => n.Name == "one.txt");
        var two = tree.Nodes.Single(n => n.Name == "two.txt");
        one.ChunkBlobIdsHex.Should().BeEquivalentTo(two.ChunkBlobIdsHex);
    }

    [Fact]
    public void Two_identical_empty_directories_get_the_same_subtree_blob_id()
    {
        var provider = new FakeSourceProvider();
        provider.AddDirectory("C:/root");
        provider.AddDirectory("C:/root/empty1");
        provider.AddDirectory("C:/root/empty2");

        var tree = NewBuilder(provider).BuildTree("C:/root");

        var id1 = tree.Nodes.Single(n => n.Name == "empty1").SubTreeBlobIdHex;
        var id2 = tree.Nodes.Single(n => n.Name == "empty2").SubTreeBlobIdHex;
        id1.Should().Be(id2);
    }

    [Fact]
    public void Reparse_point_directories_are_recorded_but_not_descended_into()
    {
        var provider = new FakeSourceProvider();
        provider.AddDirectory("C:/root");
        provider.AddDirectory("C:/root/junction", isReparsePoint: true);
        // If the builder ever descended into it, this file would show up somewhere.
        provider.AddFile("C:/root/junction/should-not-be-visited.txt", "x"u8.ToArray());

        var tree = NewBuilder(provider).BuildTree("C:/root");

        var junctionNode = tree.Nodes.Single(n => n.Name == "junction");
        junctionNode.Kind.Should().Be(TreeNodeKind.Directory);
        junctionNode.SubTreeBlobIdHex.Should().BeNull("a reparse point is stored as a link placeholder, not followed");
    }

    [Fact]
    public void ScanFilter_excludes_matching_files_from_the_tree()
    {
        var provider = new FakeSourceProvider();
        provider.AddDirectory("C:/root");
        provider.AddFile("C:/root/keep.txt", "keep"u8.ToArray());
        provider.AddFile("C:/root/skip.tmp", "skip"u8.ToArray());

        var filter = new ScanFilter("C:/root", excludeGlobs: ["**/*.tmp"]);
        var tree = NewBuilder(provider, filter).BuildTree("C:/root");

        tree.Nodes.Select(n => n.Name).Should().BeEquivalentTo(["keep.txt"]);
    }

    [Fact]
    public void FileSystemScanner_yields_a_preorder_flat_listing_and_does_not_descend_reparse_points()
    {
        var provider = new FakeSourceProvider();
        provider.AddDirectory("C:/root");
        provider.AddDirectory("C:/root/sub");
        provider.AddFile("C:/root/sub/file.txt", "x"u8.ToArray());
        provider.AddDirectory("C:/root/link", isReparsePoint: true);
        provider.AddFile("C:/root/link/hidden.txt", "y"u8.ToArray());

        var scanner = new FileSystemScanner(provider);
        var entries = scanner.Scan("C:/root").ToList();

        entries.Select(e => e.FullPath).Should().BeEquivalentTo(
            ["C:/root", "C:/root/sub", "C:/root/sub/file.txt", "C:/root/link"]);
    }

    [Fact]
    public void SnapshotWriter_combines_multiple_source_paths_into_one_root_tree()
    {
        var provider = new FakeSourceProvider();
        provider.AddDirectory("C:/data");
        provider.AddFile("C:/data/a.txt", "a"u8.ToArray());
        provider.AddDirectory("D:/backup-me");
        provider.AddFile("D:/backup-me/b.txt", "b"u8.ToArray());

        var writer = new SnapshotWriter(NewBuilder(provider), IdKey);
        var draft = writer.BuildSnapshot(["C:/data", "D:/backup-me"], DateTimeOffset.UtcNow);

        draft.RootTree.Nodes.Select(n => n.Name).Should().BeEquivalentTo(["data", "backup-me"]);
        draft.RootTreeBlobId.Should().NotBeEmpty();
    }

    private static Tree FindSubTree(FakeSourceProvider provider, string path) =>
        NewBuilder(provider).BuildTree(path);
}
