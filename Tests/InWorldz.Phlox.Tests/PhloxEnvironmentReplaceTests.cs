using System.Reflection;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Tests.Common;
using Xunit;
using Xunit.Abstractions;

namespace InWorldz.Phlox.Tests;

/// <summary>
/// [Confluence] Phlox's llReplaceEnvironment and llReplaceAgentEnvironment are YEngine's since 2026-10-14 (before,
/// Tranquillity's logged the asset and loaded nothing, and the agent form reported success doing nothing). The same
/// script runs under both engines against a recording environment module (the real one needs an estate module and an
/// event queue) and must give the same codes and store the same region environment.
/// </summary>
[Collection("phlox-yengine")]
public class PhloxEnvironmentReplaceTests
{
    private const int DebugChannel = 0x7FFFFFFF;
    private readonly ITestOutputHelper _out;
    public PhloxEnvironmentReplaceTests(ITestOutputHelper o) => _out = o;

    /// <summary>An IEnvironmentModule that hands out a default region environment and records what is stored.</summary>
    public class RecordingEnv : DispatchProxy
    {
        public ViewerEnvironment Region = new();
        public readonly List<ViewerEnvironment> Stored = new();
        public int Resets;

        protected override object Invoke(MethodInfo m, object[] args)
        {
            switch (m.Name)
            {
                case "GetRegionEnvironment": return Region;
                case "StoreOnRegion":
                    lock (Stored)
                    {
                        if (args[0] is ViewerEnvironment v) Stored.Add(v); else Resets++;
                    }
                    return null;
                default:
                    return m.ReturnType.IsValueType && m.ReturnType != typeof(void) ? Activator.CreateInstance(m.ReturnType) : null;
            }
        }
    }

    private static string Errors(SchedulerHarness h)
        => string.Join(" | ", h.SaidOn.Where(s => s.Channel == DebugChannel).Select(s => s.Message));

    private (List<string> Said, RecordingEnv Env) Run(bool yengine)
    {
        using var h = new SchedulerHarness(withYEngine: yengine);
        var envModule = DispatchProxy.Create<IEnvironmentModule, RecordingEnv>();
        var env = (RecordingEnv)(object)envModule;
        h.Scene.RegisterModuleInterface<IEnvironmentModule>(envModule);
        var sp = h.Scene.GetScenePresence(h.AddClient().AgentId);

        string source = "default { state_entry() { vector R = <-1, -1, 0>; " +
            "llSay(0, \"set \" + (string)llReplaceEnvironment(R, \"\", -1, 28800, 3600)); " +
            "llSay(0, \"track \" + (string)llReplaceEnvironment(R, \"\", 7, -1, -1)); " +
            "llSay(0, \"length \" + (string)llReplaceEnvironment(R, \"\", -1, 100, -1)); " +
            "llSay(0, \"remove0 \" + (string)llReplaceEnvironment(R, \"\", 0, -1, -1)); " +
            "llSay(0, \"remove3 \" + (string)llReplaceEnvironment(R, \"\", 3, -1, -1)); " +
            "llSay(0, \"missing \" + (string)llReplaceEnvironment(R, \"no such settings\", -1, -1, -1)); " +
            "llSay(0, \"reset \" + (string)llReplaceEnvironment(R, \"\", -1, -1, -1)); " +
            "llSay(0, \"agent \" + (string)llReplaceAgentEnvironment(\"" + sp.UUID + "\", 0.0, \"\")); " +
            "llSay(0, \"done\"); } }";

        if (yengine)
        {
            var item = TaskInventoryHelpers.AddScript(h.Scene.AssetService, h.Prim, UUID.Random(), UUID.Random(), "yscript", source);
            Assert.True(h.Prim.Inventory.CreateScriptInstance(item.ItemID, 0, false, h.YEngine.ScriptEngineName, 1));
            Assert.True(h.YEngine.GetScriptErrors(item.ItemID).Count == 0, "YEngine did not compile it");
            h.Prim.ParentGroup.ResumeScripts();
        }
        else h.RezScript(source);

        Assert.True(h.PumpUntil(() => h.Said.Contains("done") || Errors(h).Length > 0, TimeSpan.FromSeconds(30)),
            "said: " + string.Join(" | ", h.Said) + " errors: " + Errors(h));
        _out.WriteLine((yengine ? "YEngine: " : "Phlox:   ") + string.Join(" | ", h.Said) + " stored " + env.Stored.Count + " resets " + env.Resets);
        Assert.True(Errors(h).Length == 0, Errors(h));
        return (h.Said.ToList(), env);
    }

    [Fact]
    public void LlReplaceEnvironmentGivesYEnginesCodesAndStoresTheSameRegionEnvironment()
    {
        var (phlox, penv) = Run(yengine: false);
        var (yengine, yenv) = Run(yengine: true);

        Assert.Equal(new[] { "set 1", "track -5", "length -6", "remove0 -9", "remove3 -5", "missing -3", "reset 1", "agent -1", "done" }, phlox);
        Assert.Equal(yengine, phlox);

        Assert.Single(penv.Stored);
        Assert.Equal(28800, penv.Stored[0].DayLength);
        Assert.Equal(3600, penv.Stored[0].DayOffset);
        Assert.Equal(1, penv.Resets);
        Assert.Equal(yenv.Stored.Select(e => (e.DayLength, e.DayOffset)), penv.Stored.Select(e => (e.DayLength, e.DayOffset)));
        Assert.Equal(yenv.Resets, penv.Resets);
    }
}
