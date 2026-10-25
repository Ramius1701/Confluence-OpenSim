/*
 * [Confluence] SimProtection suspends a region's scripts during a low-FPS dip and resumes them; it no longer removes
 * and recreates them, which restarted every script from state_entry and lost its globals (2026-10-25).
 */

using System;
using System.Linq;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Region.CoreModules.World.Region;
using OpenSim.Tests.Common;
using Xunit;

namespace InWorldz.Phlox.Tests;

/// <summary>
/// A protection cycle (FPS below the threshold, then SecondsBeforeReenable later) pauses each script and resumes it
/// with its globals and state: state_entry does not run again, and an event sent while it was paused runs after.
/// In "phlox-yengine" (YEngine's statics).
/// </summary>
[Collection("phlox-yengine")]
public class SimProtectionSuspendTests
{
    private const string Phlox = "InWorldz.Phlox", YEngine = "YEngine";
    private static readonly TimeSpan Cap = TimeSpan.FromSeconds(60);

    /// <summary>A global and a state of its own, so a fresh start would show in both.</summary>
    private const string Body =
        "integer n;\n" +
        "default {\n" +
        "    state_entry() { llSay(0, \"fresh\"); }\n" +
        "    touch_start(integer t) { ++n; llSay(0, \"count \" + (string)n); if (n == 2) state two; }\n" +
        "}\n" +
        "state two {\n" +
        "    state_entry() { llSay(0, \"in two\"); }\n" +
        "}\n";

    private static SimProtectionModule Protection(SchedulerHarness h)
    {
        var config = new IniConfigSource();
        IConfig sp = config.AddConfig("SimProtection");
        sp.Set("Enabled", "true");
        sp.Set("BaseRateFramesPerSecond", "45");
        sp.Set("PercentToBeginShutDownOfServices", "50");
        sp.Set("SecondsBeforeReenable", "20");
        var module = new SimProtectionModule();
        module.Initialise(config);
        module.AddRegion(h.Scene);   // no RegionLoaded: no timer, the test drives the checks
        return module;
    }

    private static UUID RegionStart(SchedulerHarness h, string defaultEngine)
    {
        UUID itemId = UUID.Random();
        TaskInventoryHelpers.AddScript(h.Scene.AssetService, h.Prim, itemId, UUID.Random(), "protected", Body);
        Assert.Equal(1, h.Prim.ParentGroup.CreateScriptInstances(0, false, defaultEngine, 0));
        h.Prim.ParentGroup.ResumeScripts();
        Assert.True(h.PumpUntil(() => h.Said.Contains("fresh"), Cap), h.Diagnose(itemId));
        return itemId;
    }

    private static void Cycle(SchedulerHarness h, Action touch, Func<bool> hasScript)
    {
        touch();
        Assert.True(h.PumpUntil(() => h.Said.Contains("count 1"), Cap));

        SimProtectionModule module = Protection(h);
        DateTime t0 = DateTime.Now;
        module.CheckScripts(1f, t0);            // FPS 1 of a 22.5 threshold: suspend
        Assert.True(hasScript());               // paused, not removed

        touch();                                // queued while paused
        h.PumpFor(TimeSpan.FromSeconds(2));
        Assert.DoesNotContain("count 2", h.Said);

        module.CheckScripts(1f, t0.AddSeconds(10));   // still inside SecondsBeforeReenable: stays paused
        h.PumpFor(TimeSpan.FromSeconds(1));
        Assert.DoesNotContain("count 2", h.Said);

        module.CheckScripts(45f, t0.AddSeconds(30));  // resume
        Assert.True(h.PumpUntil(() => h.Said.Contains("in two"), Cap), string.Join(" | ", h.Said));
        Assert.Contains("count 2", h.Said);           // the global survived and the queued touch ran
        Assert.Equal(1, h.Said.Count(s => s == "fresh"));
        Assert.True(hasScript());
    }

    [Fact]
    public void AYEngineScriptKeepsItsStateThroughAProtectionCycle()
    {
        using var h = new SchedulerHarness(withYEngine: true);
        UUID item = RegionStart(h, YEngine);
        Assert.True(h.YEngine.HasScript(item, out _));
        Cycle(h,
            () => Assert.True(h.YEngine.PostScriptEvent(item, "touch_start", new object[] { 1 })),
            () => h.YEngine.HasScript(item, out _));
    }

    [Fact]
    public void APhloxScriptKeepsItsStateThroughAProtectionCycle()
    {
        using var h = new SchedulerHarness(withYEngine: true);
        UUID item = RegionStart(h, Phlox);
        Assert.False(h.YEngine.HasScript(item, out _));
        Cycle(h, () => h.PostTouch(item), () => h.InterpreterFor(item) != null);
    }

    /// <summary>Scripts an admin switched off are not the module's to resume.</summary>
    [Fact]
    public void ScriptsAnAdminDisabledAreLeftAlone()
    {
        using var h = new SchedulerHarness(withYEngine: true);
        UUID item = RegionStart(h, YEngine);
        h.Scene.RegionInfo.RegionSettings.DisableScripts = true;
        SimProtectionModule module = Protection(h);
        DateTime t0 = DateTime.Now;
        module.CheckScripts(1f, t0);
        module.CheckScripts(45f, t0.AddSeconds(30));

        Assert.True(h.YEngine.PostScriptEvent(item, "touch_start", new object[] { 1 }));
        Assert.True(h.PumpUntil(() => h.Said.Contains("count 1"), Cap));   // never suspended
    }
}
