// [Confluence] From Tranquillity #371 (Tests/OpenSim.Region.CoreModules.Tests/World/Wind/WindUpdateRateTests.cs), run in
// this xUnit project, with the warning caught by a log4net memory appender (Confluence's WindModule logs through log4net).

/*
 * Copyright (c) 2026 Legion Builds
 *
 * This Source Code Form is subject to the terms of the
 * Mozilla Public License, v. 2.0. If a copy of the MPL was not distributed
 * with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
 */

using log4net.Appender;
using log4net.Core;
using log4net.Repository.Hierarchy;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.CoreModules;
using OpenSim.Region.CoreModules.World.Wind;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Tests.Common;
using Xunit;

namespace OpenSim.Region.CoreModules.Tests.World.Wind;

/// <summary>
/// wind_update_rate is a frame count that WindUpdate divides by, so a value below 1 would throw a divide-by-zero
/// (or, for a negative value, run on a nonsense schedule) every frame. The [Wind] section falls back to the default
/// with one warning, and the console refuses the value and keeps the current one.
/// </summary>
// The log capture adds an appender to log4net's root, which is process-wide, so the class runs in the process-logger
// collection.
[Collection("process-logger")]
public class WindUpdateRateTests : IDisposable
{
    private const int DefaultRate = 150;

    private readonly MemoryAppender m_capture = new MemoryAppender { Threshold = Level.All };
    private readonly Hierarchy m_hierarchy = (Hierarchy)log4net.LogManager.GetRepository(typeof(WindModule).Assembly);
    private readonly Level m_previousLevel;

    public WindUpdateRateTests()
    {
        m_capture.ActivateOptions();
        m_previousLevel = m_hierarchy.Root.Level;
        m_hierarchy.Root.AddAppender(m_capture);
        m_hierarchy.Root.Level = Level.All;
        m_hierarchy.Configured = true;
    }

    public void Dispose()
    {
        m_hierarchy.Root.RemoveAppender(m_capture);
        m_hierarchy.Root.Level = m_previousLevel;
    }

    private static TestScene NewScene()
    {
        TestScene scene = new SceneHelpers().SetupScene("Wind rate test region", UUID.Random(), 1000, 1000, 256, 256, new IniConfigSource());
        scene.RegionInfo.RegionSettings.WaterHeight = 0f;
        return scene;
    }

    private static WindModule ModuleOn(TestScene scene, string rate)
    {
        IniConfigSource config = new IniConfigSource();
        IConfig windConfig = config.AddConfig("Wind");
        windConfig.Set("wind_plugin", "ZephyrWind");
        if (rate != null)
            windConfig.Set("wind_update_rate", rate);
        var module = new WindModule();
        module.Initialise(config);
        module.AddRegion(scene);
        return module;
    }

    private List<string> RateWarnings()
        => m_capture.GetEvents()
            .Where(e => e.Level == Level.Warn && (e.RenderedMessage ?? "").Contains("wind_update_rate"))
            .Select(e => e.RenderedMessage).ToList();

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public void AnIniRateBelowOneUsesTheDefaultWithOneWarning(string configured)
    {
        TestScene scene = NewScene();
        WindModule module = ModuleOn(scene, configured);

        Assert.Equal(DefaultRate, module.WindUpdateRate);
        Assert.Single(RateWarnings());
    }

    [Fact]
    public void AValidIniRateIsUsedWithoutAWarning()
    {
        TestScene scene = NewScene();
        WindModule module = ModuleOn(scene, "5");

        Assert.Equal(5, module.WindUpdateRate);
        Assert.Empty(RateWarnings());
    }

    [Fact]
    public void AnAbsentIniRateIsTheDefaultWithoutAWarning()
    {
        TestScene scene = NewScene();
        WindModule module = ModuleOn(scene, null);

        Assert.Equal(DefaultRate, module.WindUpdateRate);
        Assert.Empty(RateWarnings());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public void WindUpdateDoesNotThrowWhenTheIniRateWasBelowOne(string configured)
    {
        TestScene scene = NewScene();
        WindModule module = ModuleOn(scene, configured);

        // Before the fix a rate of 0 made the very first call divide by zero.
        for (int i = 0; i < 400; i++)
            module.WindUpdate();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void TheConsoleRefusesARateBelowOneAndKeepsTheCurrentValue(string typed)
    {
        TestScene scene = NewScene();
        WindModule module = ModuleOn(scene, "7");

        string message = module.SetUpdateRate(typed);

        Assert.Equal(7, module.WindUpdateRate);
        Assert.Contains("at least 1", message);
        for (int i = 0; i < 50; i++)
            module.WindUpdate();
    }

    [Fact]
    public void TheConsoleAcceptsAValidRate()
    {
        TestScene scene = NewScene();
        WindModule module = ModuleOn(scene, "7");

        string message = module.SetUpdateRate("20");

        Assert.Equal(20, module.WindUpdateRate);
        Assert.Contains("20", message);
    }

    [Fact]
    public void TheConsoleRefusesATextThatIsNotAWholeNumber()
    {
        TestScene scene = NewScene();
        WindModule module = ModuleOn(scene, "7");

        string message = module.SetUpdateRate("fast");

        Assert.Equal(7, module.WindUpdateRate);
        Assert.Contains("fast", message);
    }
}
