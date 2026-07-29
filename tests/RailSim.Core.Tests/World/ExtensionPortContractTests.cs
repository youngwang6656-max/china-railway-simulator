using RailSim.Core.World;
using Xunit;

namespace RailSim.Core.Tests.World;

public sealed class ExtensionPortContractTests
{
    public static TheoryData<string> ReservedPortNames =>
        new()
        {
            "RailSim.Core.World.ITerrainSource",
            "RailSim.Core.World.ITerrainGenerator",
            "RailSim.Core.World.ITerrainEditService",
            "RailSim.Core.World.ICityPlacementStrategy",
            "RailSim.Core.World.INameGenerator",
            "RailSim.Core.Projection.IMacroTopologyProjection",
            "RailSim.Core.World.IWorldChunkStore",
            "RailSim.Core.World.IWorldRegionProvider",
            "RailSim.Core.World.IWorldStreamingPolicy",
            "RailSim.Core.Editing.IEditHistoryStore"
        };

    [Theory]
    [MemberData(nameof(ReservedPortNames))]
    public void UnselectedProductOptionsKeepMeaningfulInterfacePorts(
        string fullName)
    {
        var port = typeof(WorldPoint).Assembly.GetType(fullName);

        Assert.NotNull(port);
        Assert.True(port.IsInterface);
        Assert.NotEmpty(port.GetMethods());
    }
}
