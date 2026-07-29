using RailSim.Core.World;

namespace RailSim.Core.Persistence;

public interface IWorldRepository
{
    void Save(string path, WorldState world);

    WorldState Load(string path);
}
