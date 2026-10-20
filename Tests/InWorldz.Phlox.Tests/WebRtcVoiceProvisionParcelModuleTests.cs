// [Confluence] From OpenSim-Tranquillity PR #350's ProvisionParcelModuleTests, converted to xunit and adapted to
// Confluence's region module: it hands the request to the voice service through CreateViewerSession and the session's
// ProvisionVoiceAccountRequest (Tranquillity's goes straight to the service), and reads the request body through
// IOSHttpRequest, so the service and the request are stand-ins set up here.

using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Region.CoreModules.World.Land;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Tests.Common;
using Xunit;

namespace osWebRtcVoice.Tests;

/// <summary>
/// A local voice request is checked against the parcel the avatar stands on (a root agent) on every estate: the
/// parcel's voice-off setting and ban list apply on a parcel that uses the estate channel, where stock viewers send no
/// parcel id, and on TaxFree estates. A child agent (a neighbour region's request) keeps the estate room unless it
/// names a parcel near where it is known to be.
/// </summary>
public class WebRtcVoiceProvisionParcelModuleTests
{
    private static readonly UUID Owner = new UUID("a11ce000-0000-4000-8000-000000000001");
    private static readonly UUID Visitor = new UUID("b0b00000-0000-4000-8000-000000000002");

    private static readonly Vector3 InWest = new Vector3(64, 128, 25);
    private static readonly Vector3 InEast = new Vector3(192, 128, 25);

    /// <summary>A viewer session that only carries its fields.</summary>
    private sealed class FakeSession : IVoiceViewerSession
    {
        public string ViewerSessionID { get; set; }
        public IWebRtcVoiceService VoiceService { get; set; }
        public string VoiceServiceSessionId { get; set; }
        public UUID RegionId { get; set; }
        public UUID AgentId { get; set; }
        public IVoiceViewerSession.VFlags Flags { get; set; }
        public Task Shutdown() => Task.CompletedTask;
    }

    /// <summary>Records the requests that reach it and answers with a success map.</summary>
    private sealed class RecordingVoiceService : IWebRtcVoiceService
    {
        public readonly List<OSDMap> Requests = new();

        public IVoiceViewerSession CreateViewerSession(OSDMap r, UUID u, UUID sc)
            => new FakeSession { ViewerSessionID = UUID.Random().ToString(), VoiceService = this, AgentId = u, RegionId = sc };
        public OSDMap ProvisionVoiceAccountRequest(IVoiceViewerSession s, OSDMap r, UUID u, UUID sc)
        {
            Requests.Add((OSDMap)OSDParser.DeserializeLLSDXml(OSDParser.SerializeLLSDXmlString(r)));
            return new OSDMap { { "jsep", new OSDMap() }, { "viewer_session", s.ViewerSessionID } };
        }
        public OSDMap ProvisionVoiceAccountRequest(OSDMap pRequest, UUID pUserID, UUID pScene) => new OSDMap();
        public OSDMap VoiceSignalingRequest(OSDMap pRequest, UUID pUserID, UUID pScene) => new OSDMap();
        public OSDMap VoiceSignalingRequest(IVoiceViewerSession s, OSDMap r, UUID u, UUID sc) => new OSDMap();
    }

    /// <summary>A POST carrying an LLSD body; every other member is unused here.</summary>
    public class PostRequest : DispatchProxy
    {
        public Stream Body;

        protected override object Invoke(MethodInfo method, object[] args)
        {
            switch (method.Name)
            {
                case "get_HttpMethod": return "POST";
                case "get_InputStream": return Body;
                default:
                    Type rt = method.ReturnType;
                    return rt == typeof(void) || !rt.IsValueType ? null : Activator.CreateInstance(rt);
            }
        }
    }

    private sealed class Rig
    {
        public Scene Scene;
        public WebRtcVoiceRegionModule Module;
        public RecordingVoiceService Service = new();
        public ILandObject West;
        public ILandObject East;
    }

    private static Rig NewRig(bool taxFree)
    {
        var rig = new Rig();
        rig.Scene = new SceneHelpers().SetupScene();
        var land = new LandManagementModule();
        SceneHelpers.SetupSceneModules(rig.Scene, land);
        rig.Scene.Permissions.OnIsAdministrator += _ => false;
        rig.Scene.RegionInfo.EstateSettings.AllowVoice = true;
        rig.Scene.RegionInfo.EstateSettings.TaxFree = taxFree;
        rig.West = AddParcel(rig.Scene, land, 0, 0, 128, 256, estateChannel: false);
        rig.East = AddParcel(rig.Scene, land, 128, 0, 256, 256, estateChannel: true);

        // No [WebRtcVoice] section: the module loads no service, and the recording one is put in its place.
        rig.Module = new WebRtcVoiceRegionModule();
        rig.Module.Initialise(new IniConfigSource());
        typeof(WebRtcVoiceRegionModule)
            .GetField("m_spatialVoiceService", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(rig.Module, rig.Service);
        return rig;
    }

    private static ILandObject AddParcel(Scene scene, LandManagementModule land, int x0, int y0, int x1, int y1, bool estateChannel)
    {
        ILandObject lo = new LandObject(Owner, false, scene);
        lo.SetLandBitmap(lo.GetSquareLandBitmap(x0, y0, x1, y1));
        lo = land.AddLandObject(lo);
        lo.LandData.Flags |= (uint)ParcelFlags.AllowVoiceChat;
        if (estateChannel)
            lo.LandData.Flags |= (uint)ParcelFlags.UseEstateVoiceChan;
        return lo;
    }

    private static void Ban(ILandObject parcel, UUID agent)
    {
        parcel.LandData.Flags |= (uint)ParcelFlags.UseBanList;
        parcel.LandData.ParcelAccessList.Add(new LandAccessEntry { AgentID = agent, Flags = AccessList.Ban, Expires = 0 });
    }

    private static (int Status, OSDMap Sent) Provision(Rig rig, UUID agent, int? parcelLocalId)
    {
        var body = new OSDMap
        {
            ["channel_type"] = OSD.FromString("local"),
            ["voice_server_type"] = OSD.FromString("webrtc"),
            ["jsep"] = new OSDMap { ["type"] = OSD.FromString("offer"), ["sdp"] = OSD.FromString("v=0") },
        };
        if (parcelLocalId.HasValue)
            body["parcel_local_id"] = OSD.FromInteger(parcelLocalId.Value);

        IOSHttpRequest request = DispatchProxy.Create<IOSHttpRequest, PostRequest>();
        ((PostRequest)(object)request).Body = new MemoryStream(Encoding.UTF8.GetBytes(OSDParser.SerializeLLSDXmlString(body)));

        int before = rig.Service.Requests.Count;
        var resp = new TestOSHttpResponse();
        rig.Module.ProvisionVoiceAccountRequest(request, resp, agent, rig.Scene);
        OSDMap sent = rig.Service.Requests.Count > before ? rig.Service.Requests.Last() : null;
        return (resp.StatusCode, sent);
    }

    private static ScenePresence Root(Rig rig, UUID id, Vector3 at)
    {
        ScenePresence sp = SceneHelpers.AddScenePresence(rig.Scene, id);
        sp.AbsolutePosition = at;
        return sp;
    }

    [Fact]
    public void RootAgent_TheParcelWhereItStandsWins_OverTheOneTheViewerNames()
    {
        Rig rig = NewRig(taxFree: false);
        Root(rig, Visitor, InWest);

        // The viewer names the estate-channel parcel while standing on the west parcel.
        var (status, sent) = Provision(rig, Visitor, rig.East.LandData.LocalID);

        Assert.Equal((int)HttpStatusCode.OK, status);
        Assert.NotNull(sent);
        Assert.Equal(rig.West.LandData.LocalID, sent["parcel_local_id"].AsInteger());
    }

    [Fact]
    public void RootAgent_WithNoParcelNamed_GetsTheParcelWhereItStands()
    {
        Rig rig = NewRig(taxFree: false);
        Root(rig, Visitor, InWest);

        var (status, sent) = Provision(rig, Visitor, null);

        Assert.Equal((int)HttpStatusCode.OK, status);
        Assert.Equal(rig.West.LandData.LocalID, sent["parcel_local_id"].AsInteger());
    }

    [Fact]
    public void RootAgent_OnAnEstateChannelParcel_GetsNoParcelId()
    {
        Rig rig = NewRig(taxFree: false);
        Root(rig, Visitor, InEast);

        var (status, sent) = Provision(rig, Visitor, rig.West.LandData.LocalID);

        Assert.Equal((int)HttpStatusCode.OK, status);
        Assert.False(sent.ContainsKey("parcel_local_id"), "the estate channel is the region-wide room");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ParcelWithVoiceOff_IsRefused_OnAnyEstate(bool taxFree)
    {
        Rig rig = NewRig(taxFree);
        rig.West.LandData.Flags &= ~(uint)ParcelFlags.AllowVoiceChat;
        Root(rig, Visitor, InWest);

        var (status, sent) = Provision(rig, Visitor, rig.West.LandData.LocalID);

        Assert.Equal((int)HttpStatusCode.Forbidden, status);
        Assert.Null(sent);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ParcelWithVoiceOff_IsRefused_WhenTheViewerNamesNoParcel(bool taxFree)
    {
        // Confluence addition: the stock-viewer case on an estate-channel parcel, which used to skip every check.
        Rig rig = NewRig(taxFree);
        rig.East.LandData.Flags &= ~(uint)ParcelFlags.AllowVoiceChat;
        Root(rig, Visitor, InEast);

        var (status, sent) = Provision(rig, Visitor, null);

        Assert.Equal((int)HttpStatusCode.Forbidden, status);
        Assert.Null(sent);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BannedAgent_IsRefused_OnAnyEstate(bool taxFree)
    {
        Rig rig = NewRig(taxFree);
        Ban(rig.West, Visitor);
        Root(rig, Visitor, InWest);

        var (status, sent) = Provision(rig, Visitor, null);

        Assert.Equal((int)HttpStatusCode.Forbidden, status);
        Assert.Null(sent);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BannedAgent_IsRefused_OnAParcelUsingTheEstateChannel(bool taxFree)
    {
        Rig rig = NewRig(taxFree);
        Ban(rig.East, Visitor);
        Root(rig, Visitor, InEast);

        var (status, sent) = Provision(rig, Visitor, null);

        Assert.Equal((int)HttpStatusCode.Forbidden, status);
        Assert.Null(sent);
    }

    [Fact]
    public void BanOnAnotherParcel_DoesNotStopAgentStandingElsewhere()
    {
        Rig rig = NewRig(taxFree: true);
        Ban(rig.East, Visitor);
        Root(rig, Visitor, InWest);

        var (status, sent) = Provision(rig, Visitor, rig.East.LandData.LocalID);

        Assert.Equal((int)HttpStatusCode.OK, status);
        Assert.NotNull(sent);
    }

    [Fact]
    public void ParcelOwner_IsNotRefusedByItsOwnBanList()
    {
        Rig rig = NewRig(taxFree: true);
        Ban(rig.West, Owner);
        Root(rig, Owner, InWest);

        var (status, sent) = Provision(rig, Owner, null);

        Assert.Equal((int)HttpStatusCode.OK, status);
        Assert.NotNull(sent);
    }

    [Fact]
    public void ChildAgent_WithNoParcelNamed_GetsNoParcelChecksAndTheRegionRoom()
    {
        Rig rig = NewRig(taxFree: false);
        Ban(rig.West, Visitor);
        ScenePresence child = SceneHelpers.AddChildScenePresence(rig.Scene, Visitor);
        child.AbsolutePosition = new Vector3(300, 128, 25);   // outside this region

        var (status, sent) = Provision(rig, Visitor, null);

        Assert.Equal((int)HttpStatusCode.OK, status);
        Assert.False(sent.ContainsKey("parcel_local_id"));
    }

    [Fact]
    public void ChildAgent_NamingAParcelFarFromItsKnownPosition_IsAnsweredAsIfItNamedNone()
    {
        Rig rig = NewRig(taxFree: false);
        ScenePresence child = SceneHelpers.AddChildScenePresence(rig.Scene, Visitor);
        child.AbsolutePosition = new Vector3(-200, 128, 25);   // well clear of the east parcel

        var (status, sent) = Provision(rig, Visitor, rig.East.LandData.LocalID);

        Assert.Equal((int)HttpStatusCode.OK, status);
        Assert.False(sent.ContainsKey("parcel_local_id"), "the dropped id is not forwarded");
    }
}
