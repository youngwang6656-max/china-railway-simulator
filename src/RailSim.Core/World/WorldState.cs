using RailSim.Core.Rail;

namespace RailSim.Core.World;

public sealed record WorldState
{
    public WorldState(
        long revision,
        GeneratedWorld baseWorld,
        RailTopology railTopology)
    {
        if (revision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(revision));
        }

        BaseWorld = baseWorld ??
            throw new ArgumentNullException(nameof(baseWorld));
        RailTopology = railTopology ??
            throw new ArgumentNullException(nameof(railTopology));
        Revision = revision;
    }

    public long Revision { get; }

    public GeneratedWorld BaseWorld { get; }

    public RailTopology RailTopology { get; }
}
