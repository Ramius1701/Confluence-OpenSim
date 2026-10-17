/* Copyright (c) 2026 Legion Builds
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

// [Confluence] From OpenSim-Tranquillity #323, adapted to TimerResolution's log4net form.

using OpenSim.Framework;
using Xunit;

namespace InWorldz.Phlox.Tests;

/// <summary>
/// [Startup] TimerResolutionMs: 0 (the default) changes nothing; a value from 1 to 15 asks Windows for that timer
/// period once, logs one line, and gives it back once; off Windows, or out of range, nothing is asked.
/// </summary>
public class TimerResolutionTests
{
    private sealed class Calls
    {
        public readonly List<uint> Begin = new();
        public readonly List<uint> End = new();
        public readonly List<(bool Warning, string Message)> Log = new();
        public void Info(string m) => Log.Add((false, m));
        public void Warn(string m) => Log.Add((true, m));
    }

    private static TimerResolution Apply(Calls c, int ms, bool windows = true)
        => TimerResolution.Apply(ms, c.Begin.Add, c.End.Add, windows, () => 1.07, c.Info, c.Warn);

    private static TimerResolution Apply(Calls c, string setting, double measured = 1.0)
        => TimerResolution.Apply(setting, c.Begin.Add, c.End.Add, true, () => measured, c.Info, c.Warn);

    [Fact]
    public void OffByDefaultAsksForNothingAndLogsNothing()
    {
        var c = new Calls();
        using TimerResolution t = Apply(c, 0);

        Assert.Null(t);
        Assert.Empty(c.Begin);
        Assert.Empty(c.End);
        Assert.Empty(c.Log);
    }

    [Fact]
    public void OnAsksOnceLogsOneLineAndGivesItBackOnceAtShutdown()
    {
        var c = new Calls();
        TimerResolution t = Apply(c, 1);

        Assert.NotNull(t);
        Assert.Equal(1, t.Milliseconds);
        Assert.Equal(new uint[] { 1 }, c.Begin);
        Assert.Empty(c.End);
        var line = Assert.Single(c.Log);
        Assert.False(line.Warning);
        Assert.Contains("1 ms", line.Message);
        Assert.Contains("1.07", line.Message);

        t.Dispose();
        t.Dispose();   // a second shutdown path must not end the request twice

        Assert.Equal(new uint[] { 1 }, c.Begin);
        Assert.Equal(new uint[] { 1 }, c.End);
    }

    [Fact]
    public void TheEndCallCarriesTheValueTheBeginCallHad()
    {
        var c = new Calls();
        Apply(c, 5).Dispose();

        Assert.Equal(new uint[] { 5 }, c.Begin);
        Assert.Equal(new uint[] { 5 }, c.End);
    }

    [Fact]
    public void OffWindowsNothingIsAsked()
    {
        var c = new Calls();
        Assert.Null(Apply(c, 1, windows: false));

        Assert.Empty(c.Begin);
        var line = Assert.Single(c.Log);
        Assert.False(line.Warning);
        Assert.Contains("Windows only", line.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("0")]
    public void AnUnsetOrZeroTextSettingIsOff(string setting)
    {
        var c = new Calls();
        Assert.Null(Apply(c, setting));
        Assert.Empty(c.Begin);
        Assert.Empty(c.Log);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("1 ms")]
    public void ATextSettingThatIsNotAWholeNumberIsRefusedWithAWarning(string setting)
    {
        var c = new Calls();
        Assert.Null(Apply(c, setting));
        Assert.Empty(c.Begin);
        var line = Assert.Single(c.Log);
        Assert.True(line.Warning);
        Assert.Contains("TimerResolutionMs", line.Message);
    }

    [Fact]
    public void ATextSettingOfAValidNumberAsksForItAndGivesItBack()
    {
        var c = new Calls();
        TimerResolution t = Apply(c, " 2 ", 2.0);
        Assert.Equal(new uint[] { 2 }, c.Begin);
        t.Dispose();
        Assert.Equal(new uint[] { 2 }, c.End);
    }

    [Fact]
    public void AFailureWhileMeasuringGivesTheTimerBackBeforeItPropagates()
    {
        var c = new Calls();
        Assert.Throws<InvalidOperationException>(() =>
            TimerResolution.Apply(1, c.Begin.Add, c.End.Add, true, () => throw new InvalidOperationException(), c.Info, c.Warn));

        Assert.Equal(new uint[] { 1 }, c.Begin);
        Assert.Equal(new uint[] { 1 }, c.End);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(16)]
    [InlineData(1000)]
    public void AValueOutOfRangeIsRefusedWithAWarningAndAsksForNothing(int ms)
    {
        var c = new Calls();
        Assert.Null(Apply(c, ms));
        Assert.Empty(c.Begin);
        var line = Assert.Single(c.Log);
        Assert.True(line.Warning);
        Assert.Contains("TimerResolutionMs", line.Message);
    }

    [Fact]
    public void TheShippedDefaultsDoNotTurnItOn()
    {
        // The region reads GetString("TimerResolutionMs", "0"): the defaults file must keep the key commented out.
        string dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "bin", "OpenSimDefaults.ini")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);

        foreach (string raw in File.ReadAllLines(Path.Combine(dir, "bin", "OpenSimDefaults.ini")))
            Assert.False(raw.Trim().StartsWith("TimerResolutionMs", StringComparison.OrdinalIgnoreCase),
                "OpenSimDefaults.ini sets TimerResolutionMs: the key must stay commented out (off)");
    }
}
