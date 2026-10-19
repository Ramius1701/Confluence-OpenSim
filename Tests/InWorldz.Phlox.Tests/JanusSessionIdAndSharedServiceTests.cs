// [Confluence] Tests for OpenSim-Tranquillity PR #345, written for xunit from its JanusMessageParseTests and
// SharedServiceTests (OSDToLong is protected here, so the 64-bit read is checked through a probe subclass and the
// create-session reply).

using System.Reflection;
using Nini.Config;
using OpenMetaverse.StructuredData;
using osWebRtcVoice;
using Xunit;

namespace InWorldz.Phlox.Tests;

/// <summary>
/// Janus names sessions and plugin handles with 64-bit numbers, which the JSON parser returns as OSDType.Long;
/// they used to read as 0, so the voice service disabled itself on its first request. And when both voice roles name
/// the same DLL, the one service now serves both (the non-spatial one was left null).
/// </summary>
public class JanusSessionIdAndSharedServiceTests
{
    private sealed class Probe : JanusMessage
    {
        public static long Read(OSD o) => OSDToLong(o);
    }

    private static OSD ParseId(long id) =>
        JanusMessageResp.FromJson("{\"janus\":\"success\",\"data\":{\"id\":" + id + "}}").dataSection["id"];

    [Fact]
    public void ACreateSessionReplyReadsA64BitSessionId()
    {
        var resp = new CreateSessionResp(JanusMessageResp.FromJson(
            "{\"janus\":\"success\",\"transaction\":\"test123\",\"data\":{\"id\":923631757106466}}"));
        Assert.Equal("923631757106466", resp.returnedId);
    }

    [Theory]
    [InlineData(923631757106466L)]
    [InlineData(4294967296L)]        // 2^32
    [InlineData(2147483648L)]        // Int32.MaxValue + 1
    [InlineData(9007199254740991L)]  // 2^53 - 1
    public void ValuesAboveInt32AreRead(long id)
    {
        OSD osd = ParseId(id);
        Assert.Equal(OSDType.Long, osd.Type);
        Assert.Equal(id, Probe.Read(osd));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(12345L)]
    [InlineData(2147483647L)]        // Int32.MaxValue
    public void Int32ValuesAreStillRead(long id)
    {
        OSD osd = ParseId(id);
        Assert.Equal(OSDType.Integer, osd.Type);
        Assert.Equal(id, Probe.Read(osd));
    }

    private const string ServiceName = "WebRtcJanusService.dll:WebRtcJanusService";

    // No [JanusWebRtcVoice] section: the loaded service makes no connection to Janus.
    private static WebRtcVoiceServiceModule InitialiseModule(string spatial, string nonSpatial)
    {
        var config = new IniConfigSource();
        IConfig voice = config.AddConfig("WebRtcVoice");
        voice.Set("Enabled", "true");
        voice.Set("SpatialVoiceService", spatial);
        if (nonSpatial is not null)
            voice.Set("NonSpatialVoiceService", nonSpatial);
        // Here the module loads its services in its constructor (Tranquillity's does it in Initialise).
        return new WebRtcVoiceServiceModule(config);
    }

    private static object GetService(WebRtcVoiceServiceModule module, string field) =>
        typeof(WebRtcVoiceServiceModule)
            .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(module);

    [Fact]
    public void WhenBothRolesNameTheSameDllOneServiceServesBoth()
    {
        var module = InitialiseModule(ServiceName, ServiceName);
        object spatial = GetService(module, "m_spatialVoiceService");
        Assert.NotNull(spatial);
        Assert.Same(spatial, GetService(module, "m_nonSpatialVoiceService"));
    }

    [Fact]
    public void WhenTheNonSpatialRoleIsLeftOutTheSpatialServiceServesBoth()
    {
        var module = InitialiseModule(ServiceName, null);
        object spatial = GetService(module, "m_spatialVoiceService");
        Assert.NotNull(spatial);
        Assert.Same(spatial, GetService(module, "m_nonSpatialVoiceService"));
    }
}
