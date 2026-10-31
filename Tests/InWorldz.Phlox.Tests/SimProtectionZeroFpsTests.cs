/*
 * [Confluence] SimProtection's stuck-region check judges only a region that has finished starting. It used to count
 * from module load and shut down regions still loading when the whole grid started at once (2026-10-29).
 */

using System;
using Nini.Config;
using OpenSim.Region.CoreModules.World.Region;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Tests.Common;
using Xunit;

namespace InWorldz.Phlox.Tests;

public class SimProtectionZeroFpsTests
{
    private static (Scene Scene, SimProtectionModule Module, Func<int> Shutdowns) Rig()
    {
        Scene scene = new SceneHelpers().SetupScene();
        var config = new IniConfigSource();
        IConfig sp = config.AddConfig("SimProtection");
        sp.Set("Enabled", "true");
        sp.Set("RestartSimIfZeroFPS", "true");
        sp.Set("MinutesBeforeZeroFPSRestart", "1");
        var module = new SimProtectionModule();
        module.Initialise(config);
        module.AddRegion(scene);   // no RegionLoaded: no timer, the test drives the checks
        int shutdowns = 0;
        module.ShutdownSimulator = () => shutdowns++;
        return (scene, module, () => shutdowns);
    }

    /// <summary>A region still loading reads 0 FPS for as long as it loads; that is not a stuck region.</summary>
    [Fact]
    public void ARegionStillStartingIsNeverShutDown()
    {
        var (scene, module, shutdowns) = Rig();
        scene.Ready = false;
        DateTime t0 = DateTime.Now;
        for (int minute = 0; minute <= 10; minute++)
            module.CheckZeroFPS(0f, t0.AddMinutes(minute));
        Assert.Equal(0, shutdowns());
    }

    /// <summary>The minute counts from when the started region is first seen at ~0 FPS, not from module load.</summary>
    [Fact]
    public void TheMinuteStartsWhenTheStartedRegionIsFirstSeenStuck()
    {
        var (scene, module, shutdowns) = Rig();
        DateTime t0 = DateTime.Now;
        scene.Ready = false;
        module.CheckZeroFPS(0f, t0);
        module.CheckZeroFPS(0f, t0.AddMinutes(4));

        scene.Ready = true;                                  // finished starting at t0+4min
        module.CheckZeroFPS(0f, t0.AddMinutes(5));           // first seen stuck
        module.CheckZeroFPS(0f, t0.AddMinutes(5.5));
        Assert.Equal(0, shutdowns());

        module.CheckZeroFPS(0f, t0.AddMinutes(6.1));         // stuck for over a minute
        Assert.Equal(1, shutdowns());
    }

    /// <summary>A region that recovers in between starts the minute again.</summary>
    [Fact]
    public void RecoveringResetsTheMinute()
    {
        var (scene, module, shutdowns) = Rig();
        scene.Ready = true;
        DateTime t0 = DateTime.Now;
        module.CheckZeroFPS(0f, t0);
        module.CheckZeroFPS(40f, t0.AddSeconds(30));
        module.CheckZeroFPS(0f, t0.AddSeconds(50));
        module.CheckZeroFPS(0f, t0.AddSeconds(100));         // 50 s since it was stuck again
        Assert.Equal(0, shutdowns());

        module.CheckZeroFPS(0f, t0.AddSeconds(111));
        Assert.Equal(1, shutdowns());
    }

    [Fact]
    public void SwitchedOffItNeverShutsDown()
    {
        var (scene, _, _) = Rig();
        var config = new IniConfigSource();
        IConfig sp = config.AddConfig("SimProtection");
        sp.Set("Enabled", "true");
        sp.Set("RestartSimIfZeroFPS", "false");
        var module = new SimProtectionModule();
        module.Initialise(config);
        module.AddRegion(scene);
        int shutdowns = 0;
        module.ShutdownSimulator = () => shutdowns++;

        scene.Ready = true;
        DateTime t0 = DateTime.Now;
        for (int minute = 0; minute <= 5; minute++)
            module.CheckZeroFPS(0f, t0.AddMinutes(minute));
        Assert.Equal(0, shutdowns);
    }
}
