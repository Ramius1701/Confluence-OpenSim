using System.Reflection;
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
}
