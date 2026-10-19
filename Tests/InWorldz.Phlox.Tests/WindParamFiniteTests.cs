using System.Reflection;
using Nini.Config;
using OpenSim.Region.CoreModules.World.Wind.Plugins;
using OpenSim.Region.CoreModules;
using OpenSim.Region.Framework.Interfaces;
using Xunit;

namespace InWorldz.Phlox.Tests;

/// <summary>
/// [Confluence] Tranquillity #321's core part: WindModule.WindParamSet, the one path for osSetWindParam (YEngine and
/// Phlox) and the "wind" console command, refuses NaN and infinity and leaves the plugin's value as it was.
/// </summary>
public class WindParamFiniteTests
{
    public class RecordingPlugin : DispatchProxy
    {
        public readonly List<(string Param, float Value)> Sets = new();
        protected override object Invoke(MethodInfo m, object[] args)
        {
            if (m.Name == "WindParamSet") Sets.Add(((string)args[0], (float)args[1]));
            if (m.Name == "get_Name") return "recording";
            return m.ReturnType.IsValueType && m.ReturnType != typeof(void) ? Activator.CreateInstance(m.ReturnType) : null;
        }
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void ANonFiniteValueIsRefusedAndAFiniteOneGoesThrough(float bad)
    {
        var module = new WindModule();
        var proxy = DispatchProxy.Create<IWindModelPlugin, RecordingPlugin>();
        var plugins = (Dictionary<string, IWindModelPlugin>)typeof(WindModule)
            .GetField("m_availableWindPlugins", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(module)!;
        plugins["recording"] = proxy;
        var sets = ((RecordingPlugin)(object)proxy).Sets;

        module.WindParamSet("recording", "strength", bad);
        Assert.Empty(sets);

        module.WindParamSet("recording", "strength", 3.5f);
        Assert.Equal(new[] { ("strength", 3.5f) }, sets);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e40")]
    public void ANonFiniteWindSettingFallsBackToItsDefaultInEveryPlugin(string bad)
    {
        // [Confluence] Tranquillity #321's config half: [Wind] values that are not finite numbers give the default
        var config = new IniConfigSource();
        IConfig wind = config.AddConfig("Wind");
        foreach (string key in new[] { "strength", "avg_strength", "avg_direction", "var_strength", "var_direction",
                                       "rate_change", "rate_change_aloft", "rate_change_flutter" })
            wind.Set(key, bad);

        var simple = (IWindModelPlugin)Activator.CreateInstance(typeof(ZephyrWind).Assembly
            .GetType("OpenSim.Region.CoreModules.World.Wind.Plugins.SimpleRandomWind", true), true)!;
        simple.WindConfig(new OpenSim.Tests.Common.SceneHelpers().SetupScene(), wind);
        Assert.Equal(1f, simple.WindParamGet("strength"));

        var configurable = (IWindModelPlugin)Activator.CreateInstance(typeof(ZephyrWind).Assembly
            .GetType("OpenSim.Region.CoreModules.World.Wind.Plugins.ConfigurableWind", true), true)!;
        configurable.WindConfig(new OpenSim.Tests.Common.SceneHelpers().SetupScene(), wind);
        Assert.Equal(5f, configurable.WindParamGet("avgStrength"));
        Assert.Equal(30f, configurable.WindParamGet("varDirection"));

        var zephyr = new ZephyrWind();
        zephyr.WindConfig(new OpenSim.Tests.Common.SceneHelpers().SetupScene(), wind);
        foreach (string param in zephyr.WindParams().Keys)
            Assert.True(float.IsFinite(zephyr.WindParamGet(param)), "ZephyrWind " + param + " is " + zephyr.WindParamGet(param));
    }
}
