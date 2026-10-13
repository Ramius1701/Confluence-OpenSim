using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.CoreModules.Framework.InventoryAccess;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.ScriptEngine.Shared.Api;
using OpenSim.Region.ScriptEngine.Shared.ScriptBase;
using OpenSim.Tests.Common;
using Xunit;
using Xunit.Abstractions;
using LSL_Integer = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLInteger;
using LSL_List = OpenSim.Region.ScriptEngine.Shared.LSL_Types.list;
using LSL_String = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;

namespace InWorldz.Phlox.Tests;

/// <summary>
/// [Confluence] YEngine's llReturnObjectsByOwner / llReturnObjectsByID (2026-10-13), the same rules Phlox follows
/// (ReturnObjectsTests): who may grant PERMISSION_RETURN_OBJECTS, the three scopes, who is exempt, and that objects are
/// RETURNED to their owners' Lost and Found, never deleted. Calls YEngine's LSL_Api directly on a scene with YEngine
/// registered; the grant is set on the script item as a viewer's yes would. Land: three strips, west (x &lt; 86) and
/// middle (x &lt; 172) owned by the script owner, east by someone else.
/// </summary>
[Collection("phlox-yengine")]
public class YEngineObjectReturnTests
{
    private static readonly Vector3 West = new(40, 128, 25), Middle = new(128, 128, 25), East = new(210, 128, 25);

    private readonly ITestOutputHelper _out;
    public YEngineObjectReturnTests(ITestOutputHelper o) => _out = o;

    private sealed class Rig : IDisposable
    {
        public SchedulerHarness H;
        public UUID ScriptOwner, Target, Other;
        public TaskInventoryItem Item;
        public LSL_Api Api;
        public void Dispose() => H.Dispose();
    }

    private static Rig NewRig(UUID? granter = null)
    {
        var h = new SchedulerHarness(cfg => cfg.AddConfig("Modules").Set("InventoryAccessModule", "BasicInventoryAccessModule"),
            withYEngine: true);
        SceneHelpers.SetupSceneModules(h.Scene, h.Config, new BasicInventoryAccessModule());
        var r = new Rig { H = h, ScriptOwner = h.Prim.OwnerID, Target = UUID.Random(), Other = UUID.Random() };
        UserAccountHelpers.CreateUserWithInventory(h.Scene, "Script", "Owner", r.ScriptOwner, "pw");
        UserAccountHelpers.CreateUserWithInventory(h.Scene, "Target", "Resident", r.Target, "pw");
        UserAccountHelpers.CreateUserWithInventory(h.Scene, "Other", "Resident", r.Other, "pw");
        h.Scene.LandChannel = new StripLand(h.Scene, (86, r.ScriptOwner), (172, r.ScriptOwner), (256, r.Other));
        h.Prim.Name = "the scripted object";
        h.Prim.ParentGroup.UpdateGroupPosition(West);

        r.Item = TaskInventoryHelpers.AddScript(h.Scene.AssetService, h.Prim, UUID.Random(), UUID.Random(), "yengine-return", "default { }");
        if (granter is UUID g)
        {
            r.Item.PermsGranter = g;
            r.Item.PermsMask = ScriptBaseClass.PERMISSION_RETURN_OBJECTS;
        }
        r.Api = new LSL_Api();
        r.Api.Initialize(h.YEngine, h.Prim, r.Item);
        return r;
    }

    private static SceneObjectGroup Obj(Rig r, UUID owner, Vector3 at, string name)
    {
        var sog = SceneHelpers.AddSceneObject(r.H.Scene, name, owner);
        sog.RootPart.Name = name;
        sog.UpdateGroupPosition(at);
        return sog;
    }

    private static List<string> LostAndFound(Rig r, UUID owner)
    {
        var folder = r.H.Scene.InventoryService.GetFolderForType(owner, FolderType.LostAndFound);
        Assert.NotNull(folder);
        return r.H.Scene.InventoryService.GetFolderItems(owner, folder.ID).Select(i => i.Name).ToList();
    }

    private static bool InScene(Rig r, SceneObjectGroup g) => !g.IsDeleted && r.H.Scene.GetSceneObjectGroup(g.UUID) != null;

    private static void AssertReturned(Rig r, SceneObjectGroup g, UUID owner)
    {
        Assert.True(r.H.PumpUntil(() => LostAndFound(r, owner).Contains(g.Name), TimeSpan.FromSeconds(30)),
            $"'{g.Name}' never reached its owner's Lost and Found (has: {string.Join(", ", LostAndFound(r, owner))})");
        Assert.False(InScene(r, g), $"'{g.Name}' is in Lost and Found but still in the scene");
    }

    private static void AssertUntouched(Rig r, params SceneObjectGroup[] groups)
    {
        r.H.PumpFor(TimeSpan.FromMilliseconds(300));
        foreach (var g in groups)
        {
            Assert.True(InScene(r, g), $"'{g.Name}' left the scene");
            Assert.DoesNotContain(g.Name, LostAndFound(r, g.OwnerID));
        }
    }

    private static LSL_Integer Scope(int s) => new LSL_Integer(s);
    private static LSL_List Keys(params SceneObjectGroup[] g) => new LSL_List(g.Select(x => (object)new LSL_String(x.UUID.ToString())).ToArray());

    [Fact]
    public void WithoutTheGrantBothAreRuntimePermissionErrorsAndNothingMoves()
    {
        using var r = NewRig(granter: null);
        var a = Obj(r, r.Target, West + new Vector3(2, 0, 0), "target's box");

        Assert.Equal(ScriptBaseClass.ERR_RUNTIME_PERMISSIONS, (int)r.Api.llReturnObjectsByOwner(r.Target.ToString(), Scope(ScriptBaseClass.OBJECT_RETURN_PARCEL)));
        Assert.Equal(ScriptBaseClass.ERR_RUNTIME_PERMISSIONS, (int)r.Api.llReturnObjectsByID(Keys(a)));
        AssertUntouched(r, a);
    }

    [Fact]
    public void AGrantFromSomeoneOtherThanTheScriptOwnerIsRefused()
    {
        using var r = NewRig(granter: UUID.Random());
        var a = Obj(r, r.Target, West + new Vector3(2, 0, 0), "target's box");

        Assert.Equal(ScriptBaseClass.ERR_RUNTIME_PERMISSIONS, (int)r.Api.llReturnObjectsByOwner(r.Target.ToString(), Scope(ScriptBaseClass.OBJECT_RETURN_PARCEL)));
        AssertUntouched(r, a);
    }

    [Fact]
    public void ByOwnerOnTheParcelReturnsOnlyWhatIsOnThisParcelToTheOwnersLostAndFound()
    {
        using var r = NewRig(granter: null);
        r.Item.PermsGranter = r.ScriptOwner;
        r.Item.PermsMask = ScriptBaseClass.PERMISSION_RETURN_OBJECTS;
        var here = Obj(r, r.Target, West + new Vector3(2, 0, 0), "target's box here");
        var middle = Obj(r, r.Target, Middle, "target's box in the middle strip");
        var east = Obj(r, r.Target, East, "target's box on someone else's land");

        int rc = r.Api.llReturnObjectsByOwner(r.Target.ToString(), Scope(ScriptBaseClass.OBJECT_RETURN_PARCEL));
        _out.WriteLine("rc=" + rc);

        Assert.Equal(1, rc);
        AssertReturned(r, here, r.Target);
        AssertUntouched(r, middle, east);
        Assert.True(InScene(r, r.H.Prim.ParentGroup));
    }

    [Fact]
    public void ByOwnerOverTheOwnersParcelsReachesEveryParcelItOwnsButNotOthers()
    {
        using var r = NewRig(granter: null);
        r.Item.PermsGranter = r.ScriptOwner;
        r.Item.PermsMask = ScriptBaseClass.PERMISSION_RETURN_OBJECTS;
        var here = Obj(r, r.Target, West + new Vector3(2, 0, 0), "west box");
        var middle = Obj(r, r.Target, Middle, "middle box");
        var east = Obj(r, r.Target, East, "east box");

        int rc = r.Api.llReturnObjectsByOwner(r.Target.ToString(), Scope(ScriptBaseClass.OBJECT_RETURN_PARCEL_OWNER));

        Assert.Equal(2, rc);
        AssertReturned(r, here, r.Target);
        AssertReturned(r, middle, r.Target);
        AssertUntouched(r, east);
    }

    [Fact]
    public void TheParcelOwnersOwnObjectsAndABadScopeAreNotReturned()
    {
        using var r = NewRig(granter: null);
        r.Item.PermsGranter = r.ScriptOwner;
        r.Item.PermsMask = ScriptBaseClass.PERMISSION_RETURN_OBJECTS;
        var mine = Obj(r, r.ScriptOwner, West + new Vector3(2, 0, 0), "the parcel owner's box");

        // SL: "Parcel owner, estate owner and estate managers can not have their objects returned by this method."
        Assert.Equal(0, (int)r.Api.llReturnObjectsByOwner(r.ScriptOwner.ToString(), Scope(ScriptBaseClass.OBJECT_RETURN_PARCEL)));
        Assert.Equal(ScriptBaseClass.ERR_MALFORMED_PARAMS, (int)r.Api.llReturnObjectsByOwner(r.Target.ToString(), Scope(3)));
        Assert.Equal(ScriptBaseClass.ERR_MALFORMED_PARAMS, (int)r.Api.llReturnObjectsByOwner("not a key", Scope(ScriptBaseClass.OBJECT_RETURN_PARCEL)));
        // the region scope needs an estate owner or manager
        Assert.Equal(ScriptBaseClass.ERR_PARCEL_PERMISSIONS, (int)r.Api.llReturnObjectsByOwner(r.Target.ToString(), Scope(ScriptBaseClass.OBJECT_RETURN_REGION)));
        AssertUntouched(r, mine);
    }

    [Fact]
    public void ByIDReturnsObjectsOnTheOwnersLandAndRefusesOthersLand()
    {
        using var r = NewRig(granter: null);
        r.Item.PermsGranter = r.ScriptOwner;
        r.Item.PermsMask = ScriptBaseClass.PERMISSION_RETURN_OBJECTS;
        var here = Obj(r, r.Target, West + new Vector3(2, 0, 0), "target's box here");
        var east = Obj(r, r.Target, East, "target's box on someone else's land");

        Assert.Equal(ScriptBaseClass.ERR_PARCEL_PERMISSIONS, (int)r.Api.llReturnObjectsByID(Keys(east)));
        Assert.Equal(ScriptBaseClass.ERR_MALFORMED_PARAMS, (int)r.Api.llReturnObjectsByID(new LSL_List(new object[] { new LSL_String("not a key") })));
        Assert.Equal(1, (int)r.Api.llReturnObjectsByID(Keys(here, east)));
        AssertReturned(r, here, r.Target);
        AssertUntouched(r, east);
    }
}
