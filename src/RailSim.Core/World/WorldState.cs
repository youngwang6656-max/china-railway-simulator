using RailSim.Core.Rail;

namespace RailSim.Core.World;

public sealed record WorldState
{
    public WorldState(
        long revision,
        GeneratedWorld baseWorld,
        RailTopology railTopology,
        RailTopologyChangeSet? railChange = null)
    {
        if (revision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision));
        }

        BaseWorld = baseWorld ??
            throw new ArgumentNullException(nameof(baseWorld));
        RailTopology = railTopology ??
            throw new ArgumentNullException(nameof(railTopology));
        if (railChange is not null &&
            railChange.ResultRevision != revision)
        {
            throw new ArgumentException(
                "Rail change result revision must match the world revision.",
                nameof(railChange));
        }

        RailChange = railChange;
        Revision = revision;
    }

    public long Revision { get; }

    public GeneratedWorld BaseWorld { get; }

    public RailTopology RailTopology { get; }

    public RailTopologyChangeSet? RailChange { get; }
}
