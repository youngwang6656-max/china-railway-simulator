using System.Collections.Immutable;
using System.Text;
using RailSim.Core.Persistence;
using RailSim.Core.World;
using Xunit;

namespace RailSim.Core.Tests.Persistence;

public sealed class FileWorldChunkStoreTests : IDisposable
{
    private readonly string _temporaryDirectory =
        Path.Combine(
            Path.GetTempPath(),
            "railsim-chunk-tests",
            Guid.NewGuid().ToString("N"));

    public FileWorldChunkStoreTests()
    {
        Directory.CreateDirectory(_temporaryDirectory);
    }

    [Fact]
    public async Task FarApartChunksRoundTripIndependently()
    {
        var store = new FileWorldChunkStore(_temporaryDirectory);
        var first = Snapshot(
            new WorldChunkId(0, 0),
            "river:regional-0001");
        var second = Snapshot(
            new WorldChunkId(93, 93),
            "track:continental-9001");

        await store.WriteAsync(first);
        await store.WriteAsync(second);
        var reloadedFirst = await store.ReadAsync(first.Id);
        var reloadedSecond = await store.ReadAsync(second.Id);
        var missing = await store.ReadAsync(new WorldChunkId(47, 12));

        Assert.Equal(first.Id, reloadedFirst!.Id);
        Assert.Equal(first.SchemaVersion, reloadedFirst.SchemaVersion);
        Assert.Equal(first.ContentHash, reloadedFirst.ContentHash);
        Assert.Equal(first.Payload.ToArray(), reloadedFirst.Payload.ToArray());
        Assert.Equal(second.Id, reloadedSecond!.Id);
        Assert.Equal(second.SchemaVersion, reloadedSecond.SchemaVersion);
        Assert.Equal(second.ContentHash, reloadedSecond.ContentHash);
        Assert.Equal(second.Payload.ToArray(), reloadedSecond.Payload.ToArray());
        Assert.Null(missing);
        Assert.Equal(
            "river:regional-0001",
            Encoding.UTF8.GetString(reloadedFirst.Payload.AsSpan()));
        Assert.Equal(
            "track:continental-9001",
            Encoding.UTF8.GetString(reloadedSecond.Payload.AsSpan()));
    }

    [Fact]
    public async Task ReplacingChunkLeavesNoTemporaryFile()
    {
        var store = new FileWorldChunkStore(_temporaryDirectory);
        var id = new WorldChunkId(-4, 7);

        await store.WriteAsync(Snapshot(id, "first"));
        await store.WriteAsync(Snapshot(id, "second"));

        var actual = await store.ReadAsync(id);
        Assert.Equal(
            "second",
            Encoding.UTF8.GetString(actual!.Payload.AsSpan()));
        Assert.Empty(
            Directory.GetFiles(
                _temporaryDirectory,
                "*.tmp",
                SearchOption.AllDirectories));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }
    }

    private static WorldChunkSnapshot Snapshot(
        WorldChunkId id,
        string payload)
    {
        return new WorldChunkSnapshot(
            id,
            schemaVersion: 1,
            $"hash:{payload}",
            Encoding.UTF8.GetBytes(payload).ToImmutableArray());
    }
}
