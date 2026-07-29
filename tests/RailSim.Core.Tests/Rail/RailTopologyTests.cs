using System.Collections.Immutable;
using RailSim.Core.Rail;
using RailSim.Core.World;
using Xunit;

namespace RailSim.Core.Tests.Rail;

public sealed class RailTopologyTests
{
    [Fact]
    public void CrossingAlignmentsDoNotCreateConnections()
    {
        var horizontal = new Alignment(
            Id("alignment:horizontal"),
            [Point(0, 50), Point(100, 50)],
            100_000);
        var vertical = new Alignment(
            Id("alignment:vertical"),
            [Point(50, 0), Point(50, 100)],
            100_000);

        var topology = RailTopology.Empty
            .AddAlignment(horizontal)
            .AddAlignment(vertical);

        Assert.Empty(topology.Connections);
    }

    [Fact]
    public void ConnectionsMustReferenceACommonNode()
    {
        var topology = CreateTwoDisconnectedTracks();
        var invalid = new TrackConnection(
            Id("connection:invalid"),
            Id("node:a"),
            Id("track:first"),
            Id("track:second"));

        var exception = Assert.Throws<RailTopologyException>(
            () => topology.AddConnection(invalid));

        Assert.Equal("rail.connection.node_mismatch", exception.Code);
    }

    [Fact]
    public void ValidConnectionJoinsIncomingTrackToOutgoingTrack()
    {
        var topology = CreateConnectedTrackFixture(out var incoming, out var outgoing);
        var connection = new TrackConnection(
            Id("connection:valid"),
            Id("node:middle"),
            incoming.Id,
            outgoing.Id);

        var updated = topology.AddConnection(connection);

        Assert.Equal(connection, updated.Connections[connection.Id]);
    }

    [Fact]
    public void TrackRequiresExistingPortsAndAlignment()
    {
        var missing = new TrackEdge(
            Id("track:missing"),
            Id("port:from"),
            Id("port:to"),
            Id("alignment:missing"),
            2_500,
            350);

        var exception = Assert.Throws<RailTopologyException>(
            () => RailTopology.Empty.AddTrack(missing));

        Assert.Equal("rail.track.port_missing", exception.Code);
    }

    [Fact]
    public void WorldStateKeepsGeneratedWorldSeparateFromTopology()
    {
        var generated = new ProceduralWorldGenerator().Generate(
            WorldGenerationParameters.CreateRegional(1));
        var state = new WorldState(0, generated, RailTopology.Empty);

        Assert.Equal(generated.ContentHash, state.BaseWorld.ContentHash);
        Assert.Empty(state.RailTopology.Tracks);
    }

    private static RailTopology CreateTwoDisconnectedTracks()
    {
        var topology = RailTopology.Empty;
        topology = AddSimpleTrack(
            topology,
            "first",
            "a",
            "b",
            Point(0, 0),
            Point(100, 0),
            out _);
        topology = AddSimpleTrack(
            topology,
            "second",
            "c",
            "d",
            Point(0, 100),
            Point(100, 100),
            out _);
        return topology;
    }

    private static RailTopology CreateConnectedTrackFixture(
        out TrackEdge incoming,
        out TrackEdge outgoing)
    {
        var start = new RailNode(
            Id("node:start"),
            RailNodeKind.Endpoint,
            Point(0, 0),
            null);
        var middle = new RailNode(
            Id("node:middle"),
            RailNodeKind.Junction,
            Point(100, 0),
            null);
        var end = new RailNode(
            Id("node:end"),
            RailNodeKind.Endpoint,
            Point(200, 0),
            null);
        var startOut = new RailPort(
            Id("port:start-out"),
            start.Id,
            RailPortDirection.Outbound);
        var middleIn = new RailPort(
            Id("port:middle-in"),
            middle.Id,
            RailPortDirection.Inbound);
        var middleOut = new RailPort(
            Id("port:middle-out"),
            middle.Id,
            RailPortDirection.Outbound);
        var endIn = new RailPort(
            Id("port:end-in"),
            end.Id,
            RailPortDirection.Inbound);
        var firstAlignment = new Alignment(
            Id("alignment:first"),
            [start.Position, middle.Position],
            100_000);
        var secondAlignment = new Alignment(
            Id("alignment:second"),
            [middle.Position, end.Position],
            100_000);
        incoming = new TrackEdge(
            Id("track:incoming"),
            startOut.Id,
            middleIn.Id,
            firstAlignment.Id,
            -2_500,
            200);
        outgoing = new TrackEdge(
            Id("track:outgoing"),
            middleOut.Id,
            endIn.Id,
            secondAlignment.Id,
            -2_500,
            200);

        return RailTopology.Empty
            .AddNode(start)
            .AddNode(middle)
            .AddNode(end)
            .AddPort(startOut)
            .AddPort(middleIn)
            .AddPort(middleOut)
            .AddPort(endIn)
            .AddAlignment(firstAlignment)
            .AddAlignment(secondAlignment)
            .AddTrack(incoming)
            .AddTrack(outgoing);
    }

    private static RailTopology AddSimpleTrack(
        RailTopology topology,
        string name,
        string fromName,
        string toName,
        WorldPoint from,
        WorldPoint to,
        out TrackEdge track)
    {
        var fromNode = new RailNode(
            Id($"node:{fromName}"),
            RailNodeKind.Endpoint,
            from,
            null);
        var toNode = new RailNode(
            Id($"node:{toName}"),
            RailNodeKind.Endpoint,
            to,
            null);
        var fromPort = new RailPort(
            Id($"port:{name}:from"),
            fromNode.Id,
            RailPortDirection.Outbound);
        var toPort = new RailPort(
            Id($"port:{name}:to"),
            toNode.Id,
            RailPortDirection.Inbound);
        var alignment = new Alignment(
            Id($"alignment:{name}"),
            ImmutableArray.Create(from, to),
            100_000);
        track = new TrackEdge(
            Id($"track:{name}"),
            fromPort.Id,
            toPort.Id,
            alignment.Id,
            0,
            160);
        return topology
            .AddNode(fromNode)
            .AddNode(toNode)
            .AddPort(fromPort)
            .AddPort(toPort)
            .AddAlignment(alignment)
            .AddTrack(track);
    }

    private static StableId Id(string value)
    {
        return new StableId(value);
    }

    private static WorldPoint Point(long xMeters, long yMeters)
    {
        return new WorldPoint(xMeters * 1000, yMeters * 1000);
    }
}
