// [Confluence] From opensim/opensim PR #63 (typhartez), as an xunit test in the Phlox suite.

using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Tests.Common;
using Xunit;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;

namespace InWorldz.Phlox.Tests;

/// <summary>
/// A Hypergrid teleport to a remote large region whose map Y is not 0 was taken for a local teleport (the code assumed
/// Hypergrid links sit at Y = 0), so the viewer could arrive to a grey region and child agents were left behind. The
/// child agents now close by the transit kind the entity transfer module set, not by the destination's Y.
/// </summary>
public class HypergridChildAgentCloseTests
{
    [Fact]
    public void AHypergridTeleportClosesTheChildAgentsWhateverTheDestinationsY()
    {
        Scene scene = new SceneHelpers().SetupScene("source", TestHelpers.ParseTail(0x100), 1006, 1004, 512, 512, new IniConfigSource());
        ScenePresence sp = SceneHelpers.AddScenePresence(scene, TestHelpers.ParseTail(0x1));

        var child = new GridRegion(1006, 1003);
        sp.AddNeighbourRegion(child, "caps");

        ulong destination = OpenSim.Framework.Util.RegionWorldLocToHandle(OpenSim.Framework.Util.RegionToWorldLoc(1004), OpenSim.Framework.Util.RegionToWorldLoc(1000));

        sp.IsInLocalTransit = false;                                    // Hypergrid
        Assert.Contains(child.RegionHandle, sp.GetChildAgentsToClose(destination, 1024, 1024));

        sp.IsInLocalTransit = true;                                     // a local teleport to the same place keeps it
        Assert.DoesNotContain(child.RegionHandle, sp.GetChildAgentsToClose(destination, 1024, 1024));
    }
}
