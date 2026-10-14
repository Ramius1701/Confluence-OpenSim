using System.Text;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Tests.Common;
using Xunit;
using Xunit.Abstractions;

namespace InWorldz.Phlox.Tests;

/// <summary>
/// [Confluence] The 40 functions YEngine has in Confluence and Phlox lacked, added to Phlox 2026-10-14
/// (Phlox.ScriptEngine/LSLSystemAPI.Confluence.cs, table indexes 969-1008). The parity case runs the same script under
/// both engines in identical scenes and compares what each said, line by line; the others check what lands in the
/// scene. Not Tranquillity tests.
/// </summary>
[Collection("phlox-yengine")]
public class PhloxConfluenceFunctionsTests
{
    private const int DebugChannel = 0x7FFFFFFF;
    private readonly ITestOutputHelper _out;
    public PhloxConfluenceFunctionsTests(ITestOutputHelper o) => _out = o;

    private static SchedulerHarness Scene(bool withYEngine = false)
        => new SchedulerHarness(cfg =>
        {
            var ossl = cfg.AddConfig("OSSL");
            ossl.Set("OSFunctionThreatLevel", "Severe");
            ossl.Set("Allow_osAvatarFreeze", "true");      // osPerm2Use answers 1 only for an explicit Allow_ entry
        }, withYEngine);

    private static string Errors(SchedulerHarness h)
        => string.Join(" | ", h.SaidOn.Where(s => s.Channel == DebugChannel).Select(s => s.Message));

    private static void RezYEngine(SchedulerHarness h, SceneObjectPart part, string source)
    {
        var item = TaskInventoryHelpers.AddScript(h.Scene.AssetService, part, UUID.Random(), UUID.Random(), "yscript", source);
        Assert.True(part.Inventory.CreateScriptInstance(item.ItemID, 0, false, h.YEngine.ScriptEngineName, 1));
        var errors = h.YEngine.GetScriptErrors(item.ItemID);
        Assert.True(errors.Count == 0, "YEngine did not compile it: " + string.Join(" | ", errors.Cast<object>()));
        part.ParentGroup.ResumeScripts();
    }

    private static ScenePresence Avatar(SchedulerHarness h)
        => h.Scene.GetScenePresence(h.AddClient().AgentId);

    // Only ungated and threat-None functions run under YEngine here: YEngine reads [OSSL] once per process (the first
    // OSSL_Api to initialise, OSSL_Api.cs m_doneSharedInit), so in a full run its threat level is whichever test came
    // first. The gated functions are checked on Phlox alone below, against what YEngine's code answers.
    private const string ParityBody =
        "llSay(0, \"flags \" + (string)osGetAvatarFlyDisabled(a) + (string)osGetAvatarWalkDisabled(a) + (string)osGetAvatarForceFly(a)); " +
        "llSay(0, \"xp \" + (string)llIsExperienceTrusted() + \" \" + (string)llGetExperiencePermissions() + \" \" + (string)llExperienceCanAutoGrant(PERMISSION_ATTACH)); " +
        "llSay(0, \"kv \" + llList2CSV(llGetExperienceKeyValueStoreStats())); " +
        "llSay(0, \"look \" + (string)osNpcLookAt(a, 0, NULL_KEY, ZERO_VECTOR) + \" \" + (string)osNpcLookAt(a, 99, NULL_KEY, ZERO_VECTOR) + \" \" + (string)osNpcLookAt(\"x\", 0, NULL_KEY, ZERO_VECTOR)); " +
        "llSay(0, \"partner \" + (string)osGetPartnerId(a)); " +
        "llSay(0, \"done\");";

    private List<string> RunParity(bool yengine)
    {
        using var h = Scene(withYEngine: yengine);
        var sp = Avatar(h);
        sp.FlyDisabled = true;
        sp.ForceFly = true;
        string source = "default { state_entry() { key a = \"" + sp.UUID + "\"; " + ParityBody + " } }";
        if (yengine) RezYEngine(h, h.Prim, source);
        else h.RezScript(source);
        Assert.True(h.PumpUntil(() => h.Said.Contains("done") || Errors(h).Length > 0, TimeSpan.FromSeconds(30)),
            (yengine ? "YEngine" : "Phlox") + " never finished: " + string.Join(" | ", h.Said) + " errors: " + Errors(h));
        Assert.True(Errors(h).Length == 0, (yengine ? "YEngine" : "Phlox") + " errors: " + Errors(h));
        _out.WriteLine((yengine ? "YEngine: " : "Phlox:   ") + string.Join(" | ", h.Said));
        return h.Said.ToList();
    }

    [Fact]
    public void TheNewFunctionsAnswerTheSameInPhloxAndYEngine()
    {
        var phlox = RunParity(yengine: false);
        var yengine = RunParity(yengine: true);

        Assert.Contains("flags 101", phlox);
        Assert.Contains("look -3 -1 -2", phlox);
        Assert.Equal(yengine, phlox);
    }

    /// <summary>The gated functions on Phlox, with the answers YEngine's code gives in the same scene.</summary>
    [Fact]
    public void TheGatedFunctionsAnswerAsYEnginesCodeDoes()
    {
        using var h = Scene();
        var sp = Avatar(h);
        string a = sp.UUID.ToString();
        h.RezScript("default { state_entry() { key a = \"" + a + "\"; " +
            "osSetAvatarFlyDisabled(a, 1); osSetAvatarWalkDisabled(a, 1); osSetAvatarForceFly(a, 1); " +
            "llSay(0, \"flags \" + (string)osGetAvatarFlyDisabled(a) + (string)osGetAvatarWalkDisabled(a) + (string)osGetAvatarForceFly(a)); " +
            "osSetAvatarFlyDisabled(a, 0); osSetAvatarWalkDisabled(a, 0); osSetAvatarForceFly(a, 0); " +
            "llSay(0, \"flags2 \" + (string)osGetAvatarFlyDisabled(a) + (string)osGetAvatarWalkDisabled(a) + (string)osGetAvatarForceFly(a)); " +
            "llSay(0, \"perm \" + (string)osPerm2Use(\"osAvatarFreeze\") + (string)osPerm2Use(\"osNoSuchFunction\")); " +
            "llSay(0, \"propose \" + osProposePartnership(a, a)); " +
            "llSay(0, \"end \" + osEndPartnership(\"not a key\")); " +
            "llSay(0, \"tags \" + (string)llGetListLength(osNpcGetNPCsWithTag(\"nobody\")) + \" \" + (string)osNpcDeleteNPCsWithTag(\"nobody\")); " +
            "llSay(0, \"done\"); } }");
        Assert.True(h.PumpUntil(() => h.Said.Contains("done") || Errors(h).Length > 0, TimeSpan.FromSeconds(30)), Errors(h));
        _out.WriteLine(string.Join(" | ", h.Said));
        Assert.True(Errors(h).Length == 0, Errors(h));

        Assert.Contains("flags 111", h.Said);
        Assert.Contains("flags2 000", h.Said);
        Assert.Contains("perm 10", h.Said);                                            // only an Allow_ entry admits
        Assert.Contains("propose Partnering is not configured on this grid.", h.Said);  // no [PartnerService] secret
        Assert.Contains("end Caller is not a real, present avatar.", h.Said);
        Assert.Contains("tags 0 0", h.Said);
    }

    [Fact]
    public void FreezeAndThawSetTheAvatarsMovement()
    {
        using var h = Scene();
        var sp = Avatar(h);
        h.RezScript("default { state_entry() { osAvatarFreeze(\"" + sp.UUID + "\"); llSay(0, \"frozen\"); } " +
                    "touch_start(integer n) { osAvatarThaw(\"" + sp.UUID + "\"); llSay(0, \"thawed\"); } }");
        Assert.True(h.PumpUntil(() => h.Said.Contains("frozen"), TimeSpan.FromSeconds(30)), Errors(h));
        Assert.False(sp.AllowMovement);

        h.TouchViaScene();
        Assert.True(h.PumpUntil(() => h.Said.Contains("thawed"), TimeSpan.FromSeconds(30)), Errors(h));
        Assert.True(sp.AllowMovement);
    }

    [Fact]
    public void OsMakeScriptWritesTheLinesAsAScriptInThisPrim()
    {
        using var h = Scene();
        h.RezScript("default { state_entry() { osMakeScript(\"made\", [\"default {\", \"state_entry() { }\", \"}\"]); llSay(0, \"done\"); } }");
        Assert.True(h.PumpUntil(() => h.Said.Contains("done") || Errors(h).Length > 0, TimeSpan.FromSeconds(30)), Errors(h));

        TaskInventoryItem made = h.Prim.Inventory.GetInventoryItems().SingleOrDefault(i => i.Name == "made");
        Assert.NotNull(made);
        Assert.Equal((int)AssetType.LSLText, made.Type);
        Assert.Equal((int)InventoryType.LSL, made.InvType);
        Assert.Equal("default {\nstate_entry() { }\n}\n", Encoding.UTF8.GetString(h.Scene.AssetService.Get(made.AssetID.ToString()).Data));
    }

    [Fact]
    public void OsGiveLinkInventoryGivesFromThatLinkToAnotherPrim()
    {
        using var h = Scene();
        var target = SceneHelpers.AddSceneObject(h.Scene, "target", h.Prim.OwnerID);
        target.AbsolutePosition = new Vector3(20, 20, 25);
        TaskInventoryHelpers.AddNotecard(h.Scene.AssetService, h.Prim, "card", UUID.Random(), UUID.Random(), "hello");

        h.RezScript("default { state_entry() { osGiveLinkInventory(LINK_THIS, \"" + target.RootPart.UUID + "\", \"card\"); llSay(0, \"done\"); } }");
        Assert.True(h.PumpUntil(() => h.Said.Contains("done") || Errors(h).Length > 0, TimeSpan.FromSeconds(30)), Errors(h));

        Assert.Contains(target.RootPart.Inventory.GetInventoryItems(), i => i.Name == "card");
    }

    [Fact]
    public void OsMessageAttachmentsReachesTheWornScriptsDataserver()
    {
        using var h = Scene();
        var sp = Avatar(h);
        var worn = SceneHelpers.AddSceneObject(h.Scene, "worn", sp.UUID);
        h.RezScriptInto(worn.RootPart, "default { state_entry() { llSay(0, \"wup\"); } dataserver(key k, string d) { llSay(0, \"got \" + d); } }");
        Assert.True(h.PumpUntil(() => h.Said.Contains("wup"), TimeSpan.FromSeconds(30)));
        worn.AttachedAvatar = sp.UUID;
        worn.IsAttachment = true;
        worn.AttachmentPoint = (uint)AttachmentPoint.Chest;
        sp.AddAttachment(worn);

        h.RezScript("default { state_entry() { osMessageAttachments(\"" + sp.UUID + "\", \"hi there\", [ATTACH_CHEST], 0); llSay(0, \"sent\"); } }");
        Assert.True(h.PumpUntil(() => h.Said.Contains("got hi there") || Errors(h).Length > 0, TimeSpan.FromSeconds(30)),
            "said: " + string.Join(" | ", h.Said) + " errors: " + Errors(h));
    }

    /// <summary>Both engines: the request is stored on the prim and shown as texture animation. YEngine's threw on every
    /// call before (a DAMap namespace under 4 characters), found by this test.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LlSetSculptAnimKeepsTheRequestOnThePrim(bool yengine)
    {
        using var h = Scene(withYEngine: yengine);
        const string source = "default { state_entry() { llSetSculptAnim(ANIM_ON | LOOP, 4, 2, 0, 7, 10.0, TRUE); llSay(0, \"done\"); } }";
        if (yengine) RezYEngine(h, h.Prim, source);
        else h.RezScript(source);
        Assert.True(h.PumpUntil(() => h.Said.Contains("done") || Errors(h).Length > 0, TimeSpan.FromSeconds(30)), Errors(h));
        Assert.True(Errors(h).Length == 0, Errors(h));

        Assert.True(h.Prim.DynAttrs.TryGetStore("OpenSim", "LSLSculptAnim", out OSDMap store));
        Assert.Equal(4, store["size_x"].AsInteger());
        Assert.Equal(2, store["size_y"].AsInteger());
        Assert.Equal(7, store["end_frame"].AsInteger());
        Assert.True(store["texture_sync"].AsBoolean());
        Assert.NotEmpty(h.Prim.TextureAnimation ?? Array.Empty<byte>());
    }

    [Fact]
    public void LlRemoteLoadScriptAndLlRemoteDataSetRegionSayTheyAreDeprecated()
    {
        using var h = Scene();
        h.RezScript("default { state_entry() { llRemoteDataSetRegion(); llRemoteLoadScript(NULL_KEY, \"x\", 0, 0); llSay(0, \"done\"); } }");
        Assert.True(h.PumpUntil(() => h.Said.Contains("done"), TimeSpan.FromSeconds(30)), Errors(h));
        _out.WriteLine(Errors(h));

        Assert.Contains("llRemoteDataSetRegion", Errors(h));
        Assert.Contains("llRemoteLoadScriptPin", Errors(h));
    }

    [Fact]
    public void OsReturnObjectsWithoutParcelRightsSaysSoAndReturnsNothing()
    {
        using var h = Scene();
        var other = SceneHelpers.AddSceneObject(h.Scene, "someone's box", UUID.Random());
        other.AbsolutePosition = h.Prim.AbsolutePosition + new Vector3(2, 0, 0);
        h.Scene.Permissions.OnEditParcelProperties += (user, parcel, p, allowManager) => false;

        h.RezScript("default { state_entry() { osReturnObjects(1); llSay(0, \"done\"); } }");
        Assert.True(h.PumpUntil(() => h.Said.Contains("done") || Errors(h).Length > 0, TimeSpan.FromSeconds(30)), Errors(h));
        h.PumpFor(TimeSpan.FromMilliseconds(300));

        Assert.Contains("does not have permission to return objects", Errors(h));
        Assert.NotNull(h.Scene.GetSceneObjectGroup(other.UUID));
    }
}
