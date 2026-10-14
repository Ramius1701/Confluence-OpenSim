using System.Net.Http;
using System.Reflection;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Services.Interfaces;
using OpenSim.Tests.Common;
using Xunit;
using Xunit.Abstractions;

namespace InWorldz.Phlox.Tests;

/// <summary>
/// [Confluence] Two YEngine changes of 2026-10-14. (1) A grid service that cannot be reached during an API call drops
/// that event and the script keeps running, instead of "Script must be Reset to re-enable". (2) osCauseDamage and
/// osCauseHealing go through ScenePresence.ApplyDamage, so OnAvatarDamage / OnAvatarDamageApplied (on_damage /
/// final_damage) fire for them as for llDamage. Not Tranquillity tests: YEngine here is Confluence's.
/// </summary>
[Collection("phlox-yengine")]
public class YEngineServiceOutageAndOsDamageTests
{
    private const int DEBUG_CHANNEL = 0x7FFFFFFF;
    private readonly ITestOutputHelper _out;
    public YEngineServiceOutageAndOsDamageTests(ITestOutputHelper o) => _out = o;

    /// <summary>An IGridService whose every call fails as a connector does when Robust is down.</summary>
    public class UnreachableGrid : DispatchProxy
    {
        protected override object Invoke(MethodInfo targetMethod, object[] args)
            => throw new HttpRequestException("Connection refused (grid service down)");
    }

    private static UUID RezYEngine(SchedulerHarness h, SceneObjectPart part, string source)
    {
        var item = TaskInventoryHelpers.AddScript(h.Scene.AssetService, part, UUID.Random(), UUID.Random(), "yscript", source);
        Assert.True(part.Inventory.CreateScriptInstance(item.ItemID, 0, false, h.YEngine.ScriptEngineName, 1));
        var errors = h.YEngine.GetScriptErrors(item.ItemID);
        Assert.True(errors.Count == 0, "YEngine did not compile it: " + string.Join(" | ", errors.Cast<object>()));
        part.ParentGroup.ResumeScripts();
        return item.ItemID;
    }

    private void Dump(SchedulerHarness h)
    {
        foreach (var (c, m) in h.SaidOn)
            _out.WriteLine($"[{c}] {m}");
    }

    [Fact]
    public void AnUnreachableGridServiceDropsTheEventAndTheScriptKeepsRunning()
    {
        using var h = new SchedulerHarness(withYEngine: true);
        typeof(Scene).GetField("m_GridService", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(h.Scene, DispatchProxy.Create<IGridService, UnreachableGrid>());

        // llEdgeOfWorld asks the grid service whether a neighbour is there; the timer is the next event.
        RezYEngine(h, h.Prim, "default { state_entry() { llSetTimerEvent(0.3); llSay(99, \"asking\"); " +
            "llEdgeOfWorld(<128, 128, 25>, <1, 0, 0>); llSay(99, \"unreached\"); } " +
            "timer() { llSetTimerEvent(0); llSay(99, \"still running\"); } }");

        Assert.True(h.PumpUntil(() => h.Said.Contains("still running"), TimeSpan.FromSeconds(30)),
            "the script stopped after the service failure");
        Dump(h);

        Assert.Contains("asking", h.Said);
        Assert.DoesNotContain("unreached", h.Said);       // the event that hit the failure ended there
        var debug = h.SaidOn.Where(m => m.Channel == DEBUG_CHANNEL).Select(m => m.Message).ToList();
        Assert.Contains(debug, m => m.Contains("keeps running") && m.Contains("HttpRequestException"));
        Assert.DoesNotContain(debug, m => m.Contains("must be Reset"));
    }

    [Fact]
    public void AScriptErrorStillDisablesTheScript()
    {
        using var h = new SchedulerHarness(withYEngine: true);

        // A division by zero is the script's own error, not a service's: it keeps upstream's handling (disabled until
        // reset; the message goes to the owner, who is not in this scene).
        RezYEngine(h, h.Prim, "default { state_entry() { llSetTimerEvent(0.3); integer z = 0; llSay(99, \"dividing\"); " +
            "integer i = 1 / z; llSay(99, \"unreached\"); } " +
            "timer() { llSetTimerEvent(0); llSay(99, \"still running\"); } }");

        Assert.True(h.PumpUntil(() => h.Said.Contains("dividing"), TimeSpan.FromSeconds(30)), "the script never started");
        h.PumpFor(TimeSpan.FromSeconds(2));
        Dump(h);

        Assert.DoesNotContain("unreached", h.Said);
        Assert.DoesNotContain("still running", h.Said);
    }

    private static ScenePresence Vulnerable(SchedulerHarness h)
    {
        var sp = h.Scene.GetScenePresence(h.AddClient().AgentId);
        sp.Invulnerable = false;
        h.Scene.RegionInfo.RegionSettings.AllowDamage = true;
        ILandObject land = h.Scene.LandChannel.GetLandObject(h.Prim.AbsolutePosition.X, h.Prim.AbsolutePosition.Y);
        land.LandData.Flags |= (uint)ParcelFlags.AllowDamage;
        Assert.Equal(100f, sp.Health);
        return sp;
    }

    [Fact]
    public void OsCauseDamageAndHealingGoThroughTheDamageDoor()
    {
        using var h = new SchedulerHarness(cfg => cfg.AddConfig("OSSL").Set("OSFunctionThreatLevel", "High"), withYEngine: true);
        var sp = Vulnerable(h);
        var offered = new List<float>();
        var applied = new List<float>();
        h.Scene.EventManager.OnAvatarDamage += (who, batch) => { lock (offered) offered.AddRange(batch.Select(d => d.Amount)); };
        h.Scene.EventManager.OnAvatarDamageApplied += (who, batch) => { lock (applied) applied.AddRange(batch.Select(d => d.Amount)); };

        RezYEngine(h, h.Prim, $"default {{ state_entry() {{ osCauseDamage(\"{sp.UUID}\", 30); llSetTimerEvent(0.5); }} " +
            $"timer() {{ llSetTimerEvent(0); osCauseHealing(\"{sp.UUID}\", 50); }} }}");

        Assert.True(h.PumpUntil(() => { lock (applied) return applied.Count >= 2; }, TimeSpan.FromSeconds(30)),
            $"the damage door saw {applied.Count} batches; health {sp.Health}");
        Dump(h);

        lock (offered) Assert.Equal(new[] { 30f, -50f }, offered);
        lock (applied) Assert.Equal(new[] { 30f, -50f }, applied);
        Assert.Equal(100f, sp.Health);                  // 100 - 30 + 50, clamped at 100
    }

    [Fact]
    public void OsCauseDamageToZeroKillsOnceThroughTheCombatModule()
    {
        using var h = new SchedulerHarness(cfg => cfg.AddConfig("OSSL").Set("OSFunctionThreatLevel", "High"), withYEngine: true);
        var sp = Vulnerable(h);
        int kills = 0;
        h.Scene.EventManager.OnAvatarKilled += (killer, who) => Interlocked.Increment(ref kills);

        RezYEngine(h, h.Prim, $"default {{ state_entry() {{ osCauseDamage(\"{sp.UUID}\", 150); }} }}");

        Assert.True(h.PumpUntil(() => Volatile.Read(ref kills) > 0, TimeSpan.FromSeconds(30)), "OnAvatarKilled never fired");
        h.PumpFor(TimeSpan.FromMilliseconds(500));
        Assert.Equal(1, Volatile.Read(ref kills));
    }
}
