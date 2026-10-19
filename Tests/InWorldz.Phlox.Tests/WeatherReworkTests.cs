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

    private static Rig NewRig(bool webPortal = false, ViewerEnvironment region = null, string settingsUri = null, float fadeSeconds = 0f)
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
        w.Set("SkyFadeSeconds", fadeSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
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
        Assert.Equal(0.95f, CloudCoverage(r.Avatar.Environment), 3);            // the storm's cloud cover
        Assert.NotEqual(0.95f, CloudCoverage(r.Env.Region), 3);                  // the region's own sky is unchanged
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

    // ── The sky, rethought (2026-10-18): the weather changes each sky of the day cycle relative to itself ──

    /// <summary>A midnight sky of a day cycle: dark colours, little ambient light, stars out.</summary>
    private static SkyData Midnight() => new SkyData
    {
        blue_horizon = new Vector3(0.02f, 0.03f, 0.08f),
        blue_density = new Vector3(0.01f, 0.02f, 0.06f),
        cloud_color = new Vector3(0.05f, 0.05f, 0.07f),
        ambient = new Vector3(0.05f, 0.05f, 0.08f),
        sunlight_color = new Vector4(0.1f, 0.1f, 0.2f, 0.3f),
        star_brightness = 1.5f,
        moon_brightness = 0.6f,
        cloud_shadow = 0.2f,
    };

    private static float Lum(Vector3 c) => 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;

    [Theory]
    [InlineData("rain")]
    [InlineData("storm")]
    [InlineData("snow")]
    [InlineData("blizzard")]
    public void AWeatherNeverBrightensASkyAndMidnightStaysDark(string weather)
    {
        foreach (SkyData own in new[] { Midnight(), new SkyData() })            // midnight, and the default noon sky
        {
            SkyData sky = Clone(own);
            SkyEffect.For(weather).ApplyTo(sky);

            Assert.True(Lum(sky.blue_horizon) <= Lum(own.blue_horizon) + 1e-6f, weather + " brightened the horizon");
            Assert.True(Lum(sky.blue_density) <= Lum(own.blue_density) + 1e-6f, weather + " brightened the blue density");
            Assert.True(Lum(sky.cloud_color) <= Lum(own.cloud_color) + 1e-6f, weather + " brightened the clouds");
            Assert.True(Lum(sky.ambient) <= Lum(own.ambient) + 1e-6f, weather + " brightened the ambient light");
            Assert.True(sky.star_brightness <= own.star_brightness, weather + " brightened the stars");
            Assert.True(sky.cloud_shadow >= own.cloud_shadow, weather + " took clouds away");
        }

        SkyData night = Midnight();
        SkyEffect.For(weather).ApplyTo(night);
        Assert.True(Lum(night.blue_horizon) < 0.05f, weather + " turned midnight grey: horizon " + night.blue_horizon);
        Assert.True(Lum(night.ambient) < 0.06f, weather + " lit the midnight scene: ambient " + night.ambient);
    }

    [Fact]
    public void ClearAndSunnyHaveNoSkyOfTheirOwnAndNeutralChangesNothing()
    {
        Assert.Null(SkyEffect.For("clear"));
        Assert.Null(SkyEffect.For("sunny"));

        SkyData own = Midnight(), sky = Midnight();
        SkyEffect.Neutral.ApplyTo(sky);
        Assert.Equal(own.blue_horizon, sky.blue_horizon);
        Assert.Equal(own.cloud_color, sky.cloud_color);
        Assert.Equal(own.star_brightness, sky.star_brightness);
        Assert.Equal(own.cloud_scale, sky.cloud_scale);
        Assert.True(SkyEffect.Neutral.IsNeutral);
    }

    [Fact]
    public void AFadeGoesFromNothingThroughPartWayToTheWholeSkyAndAChangeMidFadeStartsFromWhatShows()
    {
        SkyEffect storm = SkyEffect.For("storm"), snow = SkyEffect.For("snow");

        Assert.True(SkyEffect.Lerp(null, storm, 0f).IsNeutral);
        SkyEffect half = SkyEffect.Lerp(null, storm, 0.5f);
        Assert.Equal(storm.CloudCover / 2f, half.CloudCover, 4);
        Assert.Equal((1f + storm.SkyDarken) / 2f, half.SkyDarken, 4);
        Assert.Equal(storm.CloudScale, half.CloudScale, 4);                     // shape from the storm, its weight halved
        Assert.Equal(0.5f, half.CloudShapeWeight, 4);
        Assert.Equal(storm.SkyDarken, SkyEffect.Lerp(null, storm, 1f).SkyDarken, 4);

        // storm to snow, interrupted at 30%: the new fade starts from exactly what was showing
        SkyEffect showing = SkyEffect.Lerp(storm, snow, 0.3f);
        SkyEffect restart = SkyEffect.Lerp(showing, null, 0f);
        Assert.Equal(showing.CloudCover, restart.CloudCover, 5);
        Assert.Equal(showing.SkyDarken, restart.SkyDarken, 5);
        Assert.Equal(showing.HazeDensityAdd, restart.HazeDensityAdd, 5);
    }

    [Fact]
    public void TheRegionFadesIntoAStormInStepsAndBackOutAgain()
    {
        using var r = NewRig(fadeSeconds: 60f);
        var module = r.Module;
        FieldInfo start = typeof(WeatherModule).GetField("m_skyFadeStartUtc", BindingFlags.Instance | BindingFlags.NonPublic)!;
        MethodInfo step = typeof(WeatherModule).GetMethod("SkyFadeStep", BindingFlags.Instance | BindingFlags.NonPublic)!;
        float own = CloudCoverage(r.Env.Region);

        Assert.True(Apply(r, "Storm"));
        Assert.Null(r.Avatar.Environment);                                       // the fade starts from the region's sky

        start.SetValue(module, DateTime.UtcNow.AddSeconds(-30));
        step.Invoke(module, null);
        Assert.NotNull(r.Avatar.Environment);
        float mid = CloudCoverage(r.Avatar.Environment);
        Assert.True(mid > own && mid < 0.95f, "half way the clouds are " + mid);

        start.SetValue(module, DateTime.UtcNow.AddSeconds(-61));
        step.Invoke(module, null);
        Assert.Equal(0.95f, CloudCoverage(r.Avatar.Environment), 3);

        Assert.True(Apply(r, "Sunny"));                                          // and back out, over the same time
        Assert.NotNull(r.Avatar.Environment);
        start.SetValue(module, DateTime.UtcNow.AddSeconds(-61));
        step.Invoke(module, null);
        Assert.Null(r.Avatar.Environment);
        Assert.Empty(r.Env.Writes);
    }

    // ── Precipitation around the avatars (0.6.0): it reaches the ground, stays visible, stays within the budget ──

    [Theory]
    [InlineData("rain")]
    [InlineData("storm")]
    [InlineData("snow")]
    [InlineData("blizzard")]
    public void EveryFlakeOrDropReachesTheGroundAndStaysVisibleOnTheWay(string weather)
    {
        var fall = PrecipitationParticles.For(weather);
        foreach (float drop in new[] { 6f, 22f, 40f, 60f })
        {
            var ps = PrecipitationParticles.Build(fall, drop, 200f, Vector2.Zero, UUID.Random());

            // the slowest particle (the edge of the cone, the slowest speed) must have fallen the whole drop
            float v = ps.BurstSpeedMin * MathF.Cos(ps.OuterAngle);              // the edge of the cone
            float a = -ps.PartAcceleration.Z;
            float t = ps.PartMaxAge;
            Assert.True(v * t + 0.5f * a * t * t >= drop, $"{weather} from {drop} m stops at {v * t + 0.5f * a * t * t:0.0} m");
            Assert.True(t <= 30f, $"{weather} lives {t} s, more than a viewer keeps a particle");

            Assert.Equal(ps.PartStartColor.A, ps.PartEndColor.A);               // no fading on the way down
            var flags = (uint)ps.PartDataFlags;
            Assert.True((flags & 256u) != 0, "not emissive: lit by a dark weather sky the particles turn grey and vanish");
            Assert.True((flags & 16u) == 0, "follows its source: the particles would move with the emitter");
            Assert.True(ps.PartStartScaleX >= (fall.StreakLength > 0f ? 0.05f : 0.3f), "too small to see from 20 m");
            Assert.True(ps.PartStartColor.A >= 0.5f);
            // a narrow cone round the emitter's +Z (no angle near pi, which a viewer turned into a fountain going up)
            Assert.Equal(0f, ps.InnerAngle);
            Assert.InRange(ps.OuterAngle, 0.05f, 0.8f);

            float alive = ps.BurstPartCount * ps.PartMaxAge / ps.BurstRate;   // particles in the air at once
            Assert.InRange(alive, 100f, 400f);
        }
        Assert.Null(PrecipitationParticles.For("clear"));
        Assert.Null(PrecipitationParticles.For("sunny"));
    }

    [Fact]
    public void ASnowfallPutsItsEmittersRoundTheAvatarAShortDropAboveIt()
    {
        using var r = NewRig();
        r.Avatar.AbsolutePosition = new Vector3(100f, 120f, 30f);

        Assert.True(Apply(r, "Snow"));

        var emitters = r.Scene.GetSceneObjectGroups().Where(g => g.RootPart.Name.Contains("emitter")).ToList();
        Assert.NotEmpty(emitters);
        Assert.True(emitters.Count <= 40, emitters.Count + " emitters for one avatar");
        foreach (var e in emitters)
        {
            Vector3 p = e.AbsolutePosition;
            float dx = p.X - 100f, dy = p.Y - 120f;
            Assert.True(MathF.Sqrt(dx * dx + dy * dy) <= 32f + 24f, "an emitter " + MathF.Sqrt(dx * dx + dy * dy) + " m away");
            Assert.InRange(p.Z, 29f + 22f - 0.5f, 29f + 22f + 0.5f);          // the avatar's floor plus the drop
            Assert.True(e.RootPart.ParticleSystem.Length > 0);
            Vector3 axis = Vector3.UnitZ * e.RootPart.RotationOffset;           // the cone's axis in the region
            Assert.True(axis.Z < -0.99f, "the emitter's +Z points " + axis + ", not at the ground");
        }
    }

    private static SkyData Clone(SkyData s)
    {
        var c = new SkyData();
        foreach (FieldInfo f in typeof(SkyData).GetFields(BindingFlags.Instance | BindingFlags.Public))
            f.SetValue(c, f.GetValue(s));
        return c;
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
