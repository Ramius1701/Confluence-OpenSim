using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.ScriptEngine.Shared;
using OpenSim.Region.ScriptEngine.Shared.Api;
using OpenSim.Region.ScriptEngine.Shared.ScriptBase;
using OpenSim.Tests.Common;
using Xunit;
using Xunit.Abstractions;
using LSL_Float = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLFloat;
using LSL_Integer = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLInteger;
using LSL_List = OpenSim.Region.ScriptEngine.Shared.LSL_Types.list;

namespace InWorldz.Phlox.Tests;

/// <summary>
/// [Confluence] YEngine's SL combat parameters (2026-10-13): PRIM_SIT_FLAGS / PRIM_DAMAGE / PRIM_HEALTH in
/// llSetPrimitiveParams and llGetPrimitiveParams, llGetObjectDetails' OBJECT_HEALTH / OBJECT_DAMAGE /
/// OBJECT_DAMAGE_TYPE, and llRezObjectWithParams' REZ_DAMAGE / REZ_DAMAGE_TYPE, against the values the scene keeps
/// (SceneObjectGroup.Damage, the root part's LSL health and damage type). Not a Tranquillity test: YEngine here is
/// Confluence's. Uses the Phlox harness only for its scene with YEngine registered.
/// </summary>
[Collection("phlox-yengine")]
public class YEngineCombatParamsTests
{
    private readonly ITestOutputHelper _out;
    public YEngineCombatParamsTests(ITestOutputHelper o) => _out = o;

    private static LSL_Api YEngineApi(SchedulerHarness h)
    {
        var item = TaskInventoryHelpers.AddScript(h.Scene.AssetService, h.Prim, UUID.Random(), UUID.Random(), "yengine-api", "default { }");
        var api = new LSL_Api();
        api.Initialize(h.YEngine, h.Prim, item);
        return api;
    }

    private static LSL_List L(params object[] items) => new LSL_List(items);
    private static LSL_Integer I(int v) => new LSL_Integer(v);
    private static LSL_Float F(double v) => new LSL_Float(v);

    [Fact]
    public void SitFlagsSetByTheRuleReadBackAndMatchLlGetLinkSitFlags()
    {
        using var h = new SchedulerHarness(withYEngine: true);
        var api = YEngineApi(h);
        int flags = ScriptBaseClass.SIT_FLAG_ALLOW_UNSIT | ScriptBaseClass.SIT_FLAG_SCRIPTED_ONLY | ScriptBaseClass.SIT_FLAG_NO_DAMAGE;

        api.llSetPrimitiveParams(L(I(ScriptBaseClass.PRIM_SIT_FLAGS), I(flags)));
        LSL_List got = api.llGetPrimitiveParams(L(I(ScriptBaseClass.PRIM_SIT_FLAGS)));
        _out.WriteLine("PRIM_SIT_FLAGS -> " + got.ToCSV());

        Assert.Equal(1, got.Length);
        Assert.Equal((int)api.llGetLinkSitFlags(I(ScriptBaseClass.LINK_THIS)), got.GetLSLIntegerItem(0).value);
        Assert.Equal(flags, got.GetLSLIntegerItem(0).value & flags);
    }

    [Fact]
    public void DamageAndHealthReadZeroOnAFreshObject()
    {
        using var h = new SchedulerHarness(withYEngine: true);
        var api = YEngineApi(h);

        LSL_List got = api.llGetPrimitiveParams(L(I(ScriptBaseClass.PRIM_DAMAGE), I(ScriptBaseClass.PRIM_HEALTH)));
        _out.WriteLine("fresh -> " + got.ToCSV());

        Assert.Equal(3, got.Length);
        Assert.Equal(0.0, got.GetLSLFloatItem(0).value);              // SL's 0.0, not the scene's -1 "none"
        Assert.Equal(ScriptBaseClass.DAMAGE_TYPE_GENERIC, got.GetLSLIntegerItem(1).value);
        Assert.Equal(0.0, got.GetLSLFloatItem(2).value);              // SL: objects start with 0 health
    }

    [Fact]
    public void PrimDamageSetsTheObjectsDamageAndTypeAndObjectDetailsAgree()
    {
        using var h = new SchedulerHarness(withYEngine: true);
        var api = YEngineApi(h);

        api.llSetPrimitiveParams(L(I(ScriptBaseClass.PRIM_DAMAGE), F(12.5), I(ScriptBaseClass.DAMAGE_TYPE_FIRE)));

        Assert.Equal(12.5f, h.Prim.ParentGroup.Damage);
        Assert.Equal(ScriptBaseClass.DAMAGE_TYPE_FIRE, h.Prim.ParentGroup.RootPart.GetLslDamageType());

        LSL_List got = api.llGetPrimitiveParams(L(I(ScriptBaseClass.PRIM_DAMAGE)));
        Assert.Equal(12.5, got.GetLSLFloatItem(0).value, 3);
        Assert.Equal(ScriptBaseClass.DAMAGE_TYPE_FIRE, got.GetLSLIntegerItem(1).value);

        LSL_List det = api.llGetObjectDetails(h.Prim.UUID.ToString(),
            L(I(ScriptBaseClass.OBJECT_DAMAGE), I(ScriptBaseClass.OBJECT_DAMAGE_TYPE)));
        _out.WriteLine("details -> " + det.ToCSV());
        Assert.Equal(12.5, det.GetLSLFloatItem(0).value, 3);
        Assert.Equal(ScriptBaseClass.DAMAGE_TYPE_FIRE, det.GetLSLIntegerItem(1).value);
    }

    [Fact]
    public void PrimHealthSetsTheObjectsHealthNeverBelowZeroAndObjectDetailsAgree()
    {
        using var h = new SchedulerHarness(withYEngine: true);
        var api = YEngineApi(h);

        api.llSetPrimitiveParams(L(I(ScriptBaseClass.PRIM_HEALTH), F(75.0)));
        Assert.Equal(75f, h.Prim.ParentGroup.RootPart.GetLslHealth());
        Assert.Equal(75.0, api.llGetPrimitiveParams(L(I(ScriptBaseClass.PRIM_HEALTH))).GetLSLFloatItem(0).value, 3);
        Assert.Equal(75.0, api.llGetObjectDetails(h.Prim.UUID.ToString(), L(I(ScriptBaseClass.OBJECT_HEALTH))).GetLSLFloatItem(0).value, 3);
        Assert.Equal(75.0, (double)api.llGetHealth(h.Prim.UUID.ToString()), 3);   // the same value llGetHealth reads

        api.llSetPrimitiveParams(L(I(ScriptBaseClass.PRIM_HEALTH), F(-5.0)));
        Assert.Equal(0f, h.Prim.ParentGroup.RootPart.GetLslHealth());
    }

    [Fact]
    public void ObjectDetailsOfAnAvatarGiveItsHealthAndNoDamage()
    {
        using var h = new SchedulerHarness(withYEngine: true);
        var api = YEngineApi(h);
        ScenePresence sp = SceneHelpers.AddScenePresence(h.Scene, UUID.Random());
        sp.Health = 60f;

        LSL_List det = api.llGetObjectDetails(sp.UUID.ToString(),
            L(I(ScriptBaseClass.OBJECT_HEALTH), I(ScriptBaseClass.OBJECT_DAMAGE), I(ScriptBaseClass.OBJECT_DAMAGE_TYPE)));
        _out.WriteLine("avatar -> " + det.ToCSV());

        Assert.Equal(3, det.Length);
        Assert.Equal(60.0, det.GetLSLFloatItem(0).value, 3);
        Assert.Equal(0.0, det.GetLSLFloatItem(1).value);
        Assert.Equal(ScriptBaseClass.DAMAGE_TYPE_GENERIC, det.GetLSLIntegerItem(2).value);
    }

    [Fact]
    public void RezDamageAndRezDamageTypeReachTheRezzedObject()
    {
        using var h = new SchedulerHarness(withYEngine: true);
        var api = YEngineApi(h);
        TaskInventoryHelpers.AddSceneObject(h.Scene.AssetService, h.Prim, "bullet", UUID.Random(), h.Prim.OwnerID);

        string id = api.llRezObjectWithParams("bullet", L(
            I(ScriptBaseClass.REZ_POS), new LSL_Types.Vector3(0, 0, 1), I(1), I(0),   // [ vector pos, integer rel, integer at_root ]
            I(ScriptBaseClass.REZ_DAMAGE), F(33.0),
            I(ScriptBaseClass.REZ_DAMAGE_TYPE), I(ScriptBaseClass.DAMAGE_TYPE_PIERCING)));
        _out.WriteLine("rezzed " + id + " said=[" + string.Join(" | ", h.SaidOn.Select(m => m.Channel + ":" + m.Message)) + "]");
        Assert.True(UUID.TryParse(id, out UUID rezzedId) && rezzedId.IsNotZero(), "no object key returned: " + id);

        SceneObjectGroup rezzed = null;
        Assert.True(h.PumpUntil(() => (rezzed = h.Scene.GetSceneObjectGroup(rezzedId)) != null, TimeSpan.FromSeconds(10)),
            "the object was not rezzed");
        Assert.Equal(33f, rezzed.Damage);
        Assert.Equal(ScriptBaseClass.DAMAGE_TYPE_PIERCING, rezzed.RootPart.GetLslDamageType());
    }
}
