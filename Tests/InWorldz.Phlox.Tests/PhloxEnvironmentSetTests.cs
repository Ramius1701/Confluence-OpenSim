// [Confluence] Phlox's llSetEnvironment and llSetAgentEnvironment run the rule parsers YEngine uses (moved to
// EnvironmentRules 2026-10-21). Before, Phlox's llSetEnvironment handled only ENV_DAY_LENGTH / ENV_DAY_OFFSET and its
// llSetAgentEnvironment was a stub that reported success. The same script runs under both engines and must give the
// same codes and store the same region environment.

using System.Reflection;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Tests.Common;
using Xunit;
using Xunit.Abstractions;

namespace InWorldz.Phlox.Tests;

[Collection("phlox-yengine")]
public class PhloxEnvironmentSetTests
{
    private const int DebugChannel = 0x7FFFFFFF;
    private readonly ITestOutputHelper _out;
    public PhloxEnvironmentSetTests(ITestOutputHelper o) => _out = o;

    private static string Errors(SchedulerHarness h)
        => string.Join(" | ", h.SaidOn.Where(s => s.Channel == DebugChannel).Select(s => s.Message));

    private (List<string> Said, PhloxEnvironmentReplaceTests.RecordingEnv Env) Run(bool yengine)
    {
        using var h = new SchedulerHarness(withYEngine: yengine);
        var envModule = DispatchProxy.Create<IEnvironmentModule, PhloxEnvironmentReplaceTests.RecordingEnv>();
        var env = (PhloxEnvironmentReplaceTests.RecordingEnv)(object)envModule;
        h.Scene.RegisterModuleInterface<IEnvironmentModule>(envModule);
        var sp = h.Scene.GetScenePresence(h.AddClient().AgentId);

        string source = "default { state_entry() { vector R = <-1, -1, 0>; " +
            "llSay(0, \"day \" + (string)llSetEnvironment(R, [ENVIRONMENT_DAYINFO, 28800, 3600])); " +
            "llSay(0, \"gamma \" + (string)llSetEnvironment(<-1, -1, -1>, [SKY_GAMMA, 2.5])); " +
            "llSay(0, \"blur \" + (string)llSetEnvironment(R, [WATER_BLUR_MULTIPLIER, 5.0])); " +
            "llSay(0, \"unknown \" + (string)llSetEnvironment(R, [9999, 1])); " +
            "llSay(0, \"short \" + (string)llSetEnvironment(R, [SKY_GAMMA])); " +
            "llSay(0, \"reset \" + (string)llSetEnvironment(R, [])); " +
            "llSay(0, \"agent \" + (string)llSetAgentEnvironment(\"" + sp.UUID + "\", 0.0, [SKY_GAMMA, 1.0])); " +
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
    public void LlSetEnvironmentGivesYEnginesCodesAndStoresTheSameRegionEnvironment()
    {
        var (phlox, penv) = Run(yengine: false);
        var (yengine, yenv) = Run(yengine: true);

        // 1 applied; -6 out of range; -5 an unknown or incomplete rule; -1 not an Experience script.
        Assert.Equal(new[] { "day 1", "gamma 1", "blur -6", "unknown -5", "short -5", "reset 1", "agent -1", "done" }, phlox);
        Assert.Equal(yengine, phlox);

        Assert.Equal(2, penv.Stored.Count);
        Assert.Equal(28800, penv.Stored[0].DayLength);
        Assert.Equal(3600, penv.Stored[0].DayOffset);
        Assert.Equal(1, penv.Resets);
        Assert.Equal(yenv.Stored.Count, penv.Stored.Count);
        Assert.Equal(yenv.Resets, penv.Resets);
        Assert.Equal(yenv.Stored[1].ToOSD().ToString(), penv.Stored[1].ToOSD().ToString());
    }
}
