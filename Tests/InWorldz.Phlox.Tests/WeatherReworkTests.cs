using System.Reflection;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.OptionalModules.World.Weather;
using OpenSim.Server.Handlers.Concierge;
using OpenSim.Services.Interfaces;
using OpenSim.Tests.Common;
using Xunit;

namespace InWorldz.Phlox.Tests;

/// <summary>
/// [Confluence] The weather rework (2026-10-16). The weather sky is each avatar's own environment override, never the
/// region's stored environment (it was stored, so a restart left a storm sky that only "weather clear" removed); a
/// weather without a sky of its own gives the normal sky back (sunny after a storm kept the storm); a storm sky the old
/// module left saved is put back at startup; and a region has weather only when its owner turned it on in the web
/// portal, which Robust serves at /weather/&lt;region&gt;. Not a Tranquillity test.
/// </summary>
public class WeatherReworkTests
{
    /// <summary>An environment module that serves one region environment and records what is done to it.</summary>
    public class RecordingEnvironment : DispatchProxy
    {
        public ViewerEnvironment Region = NormalSky();

        /// <summary>A region sky like the default one: clouds at 0.27 (an empty environment has no skies at all).</summary>
        public static ViewerEnvironment NormalSky()
        {
            var env = new ViewerEnvironment();
            env.FromLightShare(new RegionLightShareData());
            return env;
        }
        public readonly List<string> Writes = new();
        public int Refreshes;

        protected override object Invoke(MethodInfo m, object[] args)
        {
            switch (m.Name)
            {
                case "GetRegionEnvironment": return Region;
                case "StoreOnRegion":
                case "FromLightShare":
                case "ResetEnvironmentSettings":
                    lock (Writes) Writes.Add(m.Name);
                    return null;
                case "WindlightRefreshForced":
                    Interlocked.Increment(ref Refreshes);
                    return null;
                default:
                    return m.ReturnType.IsValueType && m.ReturnType != typeof(void) ? Activator.CreateInstance(m.ReturnType) : null;
            }
        }
    }

    private sealed class Rig : IDisposable
    {
        public Scene Scene;
        public WeatherModule Module;
        public RecordingEnvironment Env;
        public ScenePresence Avatar;
        public void Dispose() => Module?.RemoveRegion(Scene);
    }

    private static Rig NewRig(bool webPortal = false, ViewerEnvironment region = null, string settingsUri = null)
    {
        var scene = new SceneHelpers().SetupScene();
        var envProxy = DispatchProxy.Create<IEnvironmentModule, RecordingEnvironment>();
        var env = (RecordingEnvironment)(object)envProxy;
        if (region != null) env.Region = region;
        scene.RegisterModuleInterface<IEnvironmentModule>(envProxy);

        var config = new IniConfigSource();
        var w = config.AddConfig("Weather");
        w.Set("Enabled", "true");
        w.Set("AdjustEnvironment", "true");
        w.Set("AdjustWind", "false");
        w.Set("AutoCycleEnabled", "false");
        w.Set("CleanupGeneratedObjectsOnStartup", "false");
        w.Set("SurfaceEffectsEnabled", "false");
        w.Set("LightningEnabled", "false");
        w.Set("ThunderEnabled", "false");
        w.Set("AuroraEnabled", "false");
        w.Set("AvoidCoveredAreas", "false");
        w.Set("UseExclusionVolumes", "false");
        w.Set("SendWeatherIMOnEntry", "false");
        w.Set("AnnounceWeatherChangesInChat", "false");
        w.Set("EmitterGrid", "2");
        w.Set("UseWebPortalSettings", webPortal ? "true" : "false");
        if (webPortal) w.Set("SettingsServerURI", settingsUri ?? "http://127.0.0.1:9");   // by default nothing listens; most tests set the switches directly

        var module = new WeatherModule();
        SceneHelpers.SetupSceneModules(scene, config, module);
        var sp = SceneHelpers.AddScenePresence(scene, UUID.Random());
        return new Rig { Scene = scene, Module = module, Env = env, Avatar = sp };
    }

    private static object Kind(string name)
        => Enum.Parse(typeof(WeatherModule).GetNestedType("WeatherKind", BindingFlags.NonPublic)!, name);

    private static bool Apply(Rig r, string weather)
        => (bool)typeof(WeatherModule).GetMethod("ApplyWeather", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(r.Module, new[] { Kind(weather), (object)UUID.Zero })!;

    private static bool Active(Rig r)
        => (bool)typeof(WeatherModule).GetField("m_regionActive", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(r.Module)!;

    private static void WebSettings(Rig r, OSDMap map)
        => typeof(WeatherModule).GetMethod("ApplyWebSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(r.Module, new object[] { map });

    private static float CloudCoverage(ViewerEnvironment env) => env.SkyFrames.First().cloud_shadow;

    [Fact]
    public void AStormSkyIsTheAvatarsOwnAndTheRegionEnvironmentIsNeverWritten()
    {
        using var r = NewRig();
        Assert.True(Active(r));

        Assert.True(Apply(r, "Storm"));

        Assert.NotNull(r.Avatar.Environment);
        Assert.Equal(0.94f, CloudCoverage(r.Avatar.Environment), 3);            // the storm profile's clouds
        Assert.NotEqual(0.94f, CloudCoverage(r.Env.Region), 3);                  // the region's own sky is unchanged
        Assert.Empty(r.Env.Writes);                                              // nothing stored on the region
        Assert.True(r.Env.Refreshes > 0);
    }

    [Fact]
    public void SunnyAfterAStormGivesTheNormalSkyBack()
    {
        using var r = NewRig();
        Assert.True(Apply(r, "Storm"));
        Assert.NotNull(r.Avatar.Environment);

        Assert.True(Apply(r, "Sunny"));

        Assert.Null(r.Avatar.Environment);
        Assert.Empty(r.Env.Writes);
    }

    [Fact]
    public void AnAvatarWithAScriptsEnvironmentIsLeftAlone()
    {
        using var r = NewRig();
        var scripted = new ViewerEnvironment();
        r.Avatar.Environment = scripted;

        Assert.True(Apply(r, "Storm"));
        Assert.Same(scripted, r.Avatar.Environment);

        Assert.True(Apply(r, "Sunny"));
        Assert.Same(scripted, r.Avatar.Environment);
    }

    [Fact]
    public void AStormSkyTheOldModuleSavedIsPutBackAtStartup()
    {
        var saved = new ViewerEnvironment();
        saved.FromLightShare(new RegionLightShareData
        {
            cloudCoverage = 0.94f, cloudScale = 0.72f, cloudColor = new Vector4(0.06f, 0.07f, 0.085f, 1f)
        });
        using var r = NewRig(region: saved);

        Assert.Contains("ResetEnvironmentSettings", r.Env.Writes);
    }

    [Fact]
    public void AnOwnersOwnLightShareSkyIsNotTouched()
    {
        var own = new ViewerEnvironment();
        own.FromLightShare(new RegionLightShareData { cloudCoverage = 0.5f, cloudScale = 0.3f });
        using var r = NewRig(region: own);

        Assert.Empty(r.Env.Writes);
    }

    [Fact]
    public void WithThePortalARegionHasNoWeatherUntilItsOwnerTurnsItOn()
    {
        using var r = NewRig(webPortal: true);
        Assert.False(Active(r));

        WebSettings(r, new OSDMap { ["enabled"] = "true", ["autocycle"] = "false" });
        Assert.True(Active(r));
        Assert.True(Apply(r, "Storm"));
        Assert.NotNull(r.Avatar.Environment);

        WebSettings(r, new OSDMap());                                            // the owner turned it off
        Assert.False(Active(r));
        Assert.Null(r.Avatar.Environment);                                       // and the sky is given back
    }

    /// <summary>A Robust stand-in: answers 404 (as one that is still starting does) until told to answer.</summary>
    private sealed class SettingsServer : IDisposable
    {
        private readonly System.Net.HttpListener m_listener = new();
        public volatile string Answer;          // null: 404
        public readonly string Uri;

        public SettingsServer()
        {
            var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            probe.Start();
            int port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            Uri = "http://127.0.0.1:" + port;
            m_listener.Prefixes.Add(Uri + "/");
            m_listener.Start();
            _ = Task.Run(Serve);
        }

        private async Task Serve()
        {
            while (m_listener.IsListening)
            {
                System.Net.HttpListenerContext ctx;
                try { ctx = await m_listener.GetContextAsync(); } catch { return; }
                string answer = Answer;
                ctx.Response.StatusCode = answer == null ? 404 : 200;
                if (answer != null)
                {
                    byte[] body = System.Text.Encoding.UTF8.GetBytes(answer);
                    ctx.Response.OutputStream.Write(body, 0, body.Length);
                }
                ctx.Response.Close();
            }
        }

        public void Dispose() { m_listener.Stop(); m_listener.Close(); }
    }

    private static void Poll(Rig r)
        => typeof(WeatherModule).GetMethod("PollWebSettings", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(r.Module, null);

    [Fact]
    public void ARobustStillStartingThat404sDoesNotTurnTheWeatherOn()
    {
        using var server = new SettingsServer();
        using var r = NewRig(webPortal: true, settingsUri: server.Uri);
        Assert.False(Active(r));

        Poll(r);                                             // 404 while Robust registers its handlers
        Poll(r);
        Assert.False(Active(r));                             // no ini fallback: the weather stays off

        server.Answer = "{\"enabled\":\"true\",\"autocycle\":\"false\"}";
        Poll(r);
        Assert.True(Active(r));                              // the owner's choice once Robust answers
    }

    [Fact]
    public void A404ThatLastsFallsBackToTheIniAndAnAnswerLaterHandsItBack()
    {
        using var server = new SettingsServer();
        using var r = NewRig(webPortal: true, settingsUri: server.Uri);
        typeof(WeatherModule).GetField("m_webSettings404FallbackMs", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(r.Module, 0L);

        Poll(r);
        Assert.True(Active(r));                              // an older Robust without the endpoint: the ini says on

        server.Answer = "{}";                                // the owner has not turned it on
        Poll(r);
        Assert.False(Active(r));
    }

    /// <summary>A grid settings table in memory.</summary>
    public class MemorySettings : DispatchProxy
    {
        public readonly Dictionary<string, string> Values = new();
        protected override object Invoke(MethodInfo m, object[] args)
        {
            if (m.Name == "Get") return Values.TryGetValue((string)args[0], out var v) ? v : null;
            if (m.Name == "Set") { Values[(string)args[0]] = (string)args[1]; return m.ReturnType == typeof(bool) ? true : null; }
            return m.ReturnType.IsValueType && m.ReturnType != typeof(void) ? Activator.CreateInstance(m.ReturnType) : null;
        }
    }

    [Fact]
    public void RobustServesARegionsOwnSwitchesThenTheGridDefaultAndLeavesUnsetOnesOut()
    {
        var proxy = DispatchProxy.Create<IGridSettingsService, MemorySettings>();
        var table = ((MemorySettings)(object)proxy).Values;
        UUID region = UUID.Random(), other = UUID.Random();
        table[RegionWeatherKeys.Key("enabled", region)] = "true";
        table[RegionWeatherKeys.Key("cyclehours", region)] = "100";                 // clamped to 24
        table[RegionWeatherKeys.Key("autocycle", "default")] = "false";

        var handler = new RegionWeatherHandler(proxy);
        OSDMap own = handler.Build(region);
        OSDMap unset = handler.Build(other);

        Assert.Equal("true", own["enabled"].AsString());
        Assert.Equal("24", own["cyclehours"].AsString());
        Assert.Equal("false", own["autocycle"].AsString());                       // from the grid default
        Assert.False(unset.ContainsKey("enabled"));                               // not set: the region's default (off)
        Assert.False(unset.ContainsKey("cyclehours"));
        Assert.Equal("", RegionWeatherKeys.NormalizeHours("soon"));
        Assert.Equal("0.25", RegionWeatherKeys.NormalizeHours("0.1"));
    }
}
