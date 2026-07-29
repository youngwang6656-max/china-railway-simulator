using System.Collections.Immutable;
using RailSim.Core.Editing;
using RailSim.Core.Rail;
using RailSim.Core.World;
using Xunit;

namespace RailSim.Core.Tests.Editing;

public sealed class RailTopologyEditStrategyTests
{
    [Fact]
    public void SplitIsReversibleAndPreservesTheExistingGroupIdentity()
    {
        var world = CreateWorld();
        var topology = world.RailTopology;
        var group = Assert.Single(topology.TrackGroups.Values);
        var target = topology.Tracks[group.TrackIds[0]];
        var alignment = topology.Alignments[target.AlignmentId];
        var splitPoint =
            alignment.ControlPoints[alignment.ControlPoints.Length / 2];
        var strategy = new RailTopologyEditStrategy(
            new DeterministicStableIdSource("split"));

        var patch = strategy.CreateSplitPatch(
            topology,
            target.Id,
            splitPoint,
            chunkSizeMeters: 64_000);
        var candidate = patch.ApplyTo(topology);
        var restored = patch.Invert().ApplyTo(candidate);

        Assert.Equal(topology.Tracks.Count + 2, candidate.Tracks.Count);
        Assert.Equal(
            topology.Alignments.Count + 1,
            candidate.Alignments.Count);
        Assert.Contains(group.Id, candidate.TrackGroups.Keys);
        Assert.DoesNotContain(target.Id, candidate.Tracks.Keys);
        Assert.Equal(
            WorldContentHasher.HashState(world),
            WorldContentHasher.HashState(
                new WorldState(0, world.BaseWorld, restored)));
    }

    [Fact]
    public void RealignIsAtomicReversibleAndPreservesStableRailIdentities()
    {
        var world = CreateWorld();
        var topology = world.RailTopology;
        var group = Assert.Single(topology.TrackGroups.Values);
        var target = topology.Tracks[group.TrackIds[0]];
        var original = topology.Alignments[target.AlignmentId];
        var midpoint = original.ControlPoints[
            original.ControlPoints.Length / 2];
        var shifted = midpoint.OffsetMillimeters(0, 8_000_000);
        var draft = new ControlPointSplineStrategy().Build(
            ImmutableArray.Create(
                new DraftWaypoint(original.ControlPoints[0], null),
                new DraftWaypoint(shifted, null),
                new DraftWaypoint(original.ControlPoints[^1], null)));
        var strategy = new RailTopologyEditStrategy(
            new DeterministicStableIdSource("realign-unused"));

        var patch = strategy.CreateRealignPatch(
            topology,
            target.Id,
            draft,
            chunkSizeMeters: 64_000);
        var candidate = patch.ApplyTo(topology);
        var restored = patch.Invert().ApplyTo(candidate);

        Assert.Equal(topology.Nodes.Keys, candidate.Nodes.Keys);
        Assert.Equal(topology.Ports.Keys, candidate.Ports.Keys);
        Assert.Equal(topology.Tracks.Keys, candidate.Tracks.Keys);
        Assert.Equal(topology.TrackGroups.Keys, candidate.TrackGroups.Keys);
        Assert.Equal(topology.Connections.Keys, candidate.Connections.Keys);
        Assert.Equal(topology.Alignments.Keys, candidate.Alignments.Keys);
        Assert.Equal(
            draft.SampledPoints,
            candidate.Alignments[original.Id].ControlPoints);
        Assert.NotEqual(
            original.ControlPoints,
            candidate.Alignments[original.Id].ControlPoints);
        Assert.Contains(
            WorldChunkId.FromPoint(
                shifted,
                chunkSizeMeters: 64_000),
            patch.AffectedChunks);
        Assert.Equal(
            WorldContentHasher.HashState(world),
            WorldContentHasher.HashState(
                new WorldState(0, world.BaseWorld, restored)));
    }

    [Fact]
    public void DeletingSelectedCorridorLeavesNoDanglingRailReferences()
    {
        var world = CreateWorld();
        var topology = world.RailTopology;
        var group = Assert.Single(topology.TrackGroups.Values);
        var strategy = new RailTopologyEditStrategy(
            new DeterministicStableIdSource("delete"));

        var patch = strategy.CreateDeletePatch(
            topology,
            ImmutableHashSet.Create(group.Id),
            chunkSizeMeters: 64_000);
        var candidate = patch.ApplyTo(topology);
        var restored = patch.Invert().ApplyTo(candidate);

        Assert.Empty(candidate.Tracks);
        Assert.Empty(candidate.Alignments);
        Assert.Empty(candidate.Ports);
        Assert.Empty(candidate.TrackGroups);
        Assert.Empty(candidate.Connections);
        Assert.Equal(
            WorldContentHasher.HashState(world),
            WorldContentHasher.HashState(
                new WorldState(0, world.BaseWorld, restored)));
    }

    [Fact]
    public void DisconnectAtSelectedNodeRemovesOnlyExplicitMovements()
    {
        var world = CreateWorld();
        var topology = world.RailTopology;
        var node = topology.Nodes.Values
            .First(
                candidate =>
                    topology.Connections.Values.Count(
                        connection =>
                            connection.NodeId == candidate.Id) == 2);
        var strategy = new RailTopologyEditStrategy(
            new DeterministicStableIdSource("disconnect"));

        var patch = strategy.CreateDisconnectPatch(
            topology,
            ImmutableHashSet.Create(node.Id),
            chunkSizeMeters: 64_000);
        var candidate = patch.ApplyTo(topology);

        Assert.Equal(
            topology.Connections.Count - 2,
            candidate.Connections.Count);
        Assert.Equal(topology.Tracks.Count, candidate.Tracks.Count);
        Assert.Equal(topology.TrackGroups.Count, candidate.TrackGroups.Count);
        Assert.Equal(
            WorldContentHasher.HashState(world),
            WorldContentHasher.HashState(
                new WorldState(
                    0,
                    world.BaseWorld,
                    patch.Invert().ApplyTo(candidate))));
    }

    [Fact]
    public void DeletingOnlyAConnectionInvalidatesItsNodeChunk()
    {
        var world = CreateWorld();
        var connection = world.RailTopology.Connections.Values
            .OrderBy(item => item.Id)
            .First();
        var patch = new RailTopologyEditStrategy(
            new DeterministicStableIdSource("delete-connection"))
            .CreateDeletePatch(
                world.RailTopology,
                ImmutableHashSet.Create(connection.Id),
                chunkSizeMeters: 64_000);
        var expected = WorldChunkId.FromPoint(
            world.RailTopology.Nodes[connection.NodeId].Position,
            chunkSizeMeters: 64_000);

        Assert.Contains(expected, patch.AffectedChunks);
        Assert.Single(patch.RemovedConnections);
        Assert.Empty(patch.RemovedTracks);
    }

    private static WorldState CreateWorld()
    {
        var generated = new ProceduralWorldGenerator().Generate(
            WorldGenerationParameters.CreateRegional(20260729));
        var stations = generated.Stations.Values
            .OrderBy(station => station.Position.XMillimeters)
            .ThenBy(station => station.Id)
            .Take(4)
            .ToArray();
        var draft = new ControlPointSplineStrategy().Build(
            stations
                .Select(
                    station => new DraftWaypoint(
                        station.Position,
                        station.Id))
                .ToImmutableArray());
        var patch = new DoubleTrackCorridorStrategy(
            new DeterministicStableIdSource("editable"))
            .CreatePatch(
                RailTopology.Empty,
                draft,
                TrackPlacementOptions.HighSpeedDoubleTrack);
        return new WorldState(
            0,
            generated,
            patch.ApplyTo(RailTopology.Empty));
    }
}
