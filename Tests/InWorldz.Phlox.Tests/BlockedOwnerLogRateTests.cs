// [Confluence] From Tranquillity #370 (Tests/OpenSim.Region.CoreModules.Tests/World/BlockedOwners/Tests/BlockedOwnerLogRateTests.cs),
// run in this xUnit project, with the lines caught by a log4net memory appender (BlockedOwnerModule logs through log4net here).

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
using Xunit;

using OpenSim.Framework;
using OpenSim.Region.CoreModules.World.Objects.BlockedOwners;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Tests.Common;

namespace OpenSim.Region.CoreModules.World.BlockedOwners.Tests;

/// <summary>
/// A blocked owner can try as often as they like, so each kind of refusal writes one line in a 60 second window
/// and the rest are counted; the first refusal after the window writes the count. The refusals themselves are
/// unchanged. The gate's clock is set by hand and the logger factory swapped for a capture; both are
/// process-wide; this assembly runs its test classes one at a time (AssemblyInfo.cs).
/// </summary>
// Sets RateLimitedLog's process-wide clock and adds a log4net appender, so it runs alone (as ScriptLogRateTests).
[Collection("phlox-state")]
public class BlockedOwnerLogRateTests : IDisposable
{
    private static readonly UUID BlockedId = new("0c3e8d57-1a94-4b62-8f0d-e5a7c92b4d16");
    private static readonly Vector3 RezPos = new(10, 10, 25);
    private const int Attempts = 200;

    private long m_ms = 3_000_000;
    private readonly MemoryAppender m_capture = new MemoryAppender { Threshold = Level.All };
    private readonly Hierarchy m_hierarchy = (Hierarchy)log4net.LogManager.GetRepository(typeof(BlockedOwnerModule).Assembly);
    private readonly Level m_previousLevel;
    private readonly TestScene m_scene;
    private readonly BlockedOwnerModule m_module;

    public BlockedOwnerLogRateTests()
    {
        m_capture.ActivateOptions();
        m_previousLevel = m_hierarchy.Root.Level;
        m_hierarchy.Root.AddAppender(m_capture);
        m_hierarchy.Root.Level = Level.All;
        m_hierarchy.Configured = true;
        RateLimitedLog.UseClockForTesting(() => m_ms);

        m_module = new BlockedOwnerModule();
        m_scene = new SceneHelpers().SetupScene();
        SceneHelpers.SetupSceneModules(m_scene, new IniConfigSource(), m_module);
        m_module.Block(BlockedId);
    }

    public void Dispose()
    {
        RateLimitedLog.UseClockForTesting(null);
        m_hierarchy.Root.RemoveAppender(m_capture);
        m_hierarchy.Root.Level = m_previousLevel;
    }

    // As Tranquillity's capture wrote them: "<level>|<message>", log4net's Info as "Information".
    private List<string> Lines(string contains)
        => m_capture.GetEvents()
            .Select(e => (e.Level == Level.Info ? "Information" : e.Level.Name) + "|" + e.RenderedMessage)
            .Where(l => l.Contains(contains)).ToList();

    // Drives the refusal Attempts times inside one window, then once after it.
    private void AssertOneLineThenOneSummary(string contains, Func<bool> refusedAttempt)
    {
        for (int i = 0; i < Attempts; i++)
        {
            Assert.False(refusedAttempt());                 // still refused every time
            m_ms += 100;
        }

        var inWindow = Lines(contains);
        Assert.Single(inWindow);
        Assert.StartsWith("Information|", inWindow[0]);
        Assert.DoesNotContain("more like this", inWindow[0]);

        m_ms += 60_000;
        Assert.False(refusedAttempt());

        var all = Lines(contains);
        Assert.Equal(2, all.Count);
        Assert.StartsWith("Information|", all[1]);
        Assert.EndsWith($"({Attempts - 1} more like this in the last 80 s)", all[1]);
    }

    [Fact]
    public void RezzingInALoopWritesOneRefusalLineAMinute()
        => AssertOneLineThenOneSummary("Refused a rez of", () => m_scene.Permissions.CanRezObject(1, BlockedId, RezPos));

    [Fact]
    public void DuplicatingInALoopWritesOneRefusalLineAMinute()
    {
        SceneHelpers.AddScenePresence(m_scene, BlockedId);
        SceneObjectGroup original = SceneHelpers.CreateSceneObject(1, BlockedId, "original", 0x300);
        m_scene.AddNewSceneObject(original, false);

        AssertOneLineThenOneSummary("Refused a duplicate of", () =>
            m_scene.SceneGraph.DuplicateObject(original.LocalId, new Vector3(1, 0, 0), BlockedId, UUID.Zero,
                Quaternion.Identity, false) != null);
    }

    [Fact]
    public void EnteringInALoopWritesOneRefusalLineAMinute()
    {
        SceneObjectGroup arriving = SceneHelpers.CreateSceneObject(1, BlockedId, "arriving", 0x310);

        AssertOneLineThenOneSummary("Refused entry of", () => m_scene.Permissions.CanObjectEntry(arriving, true, RezPos));
    }

    [Fact]
    public void ThreeKindsOfRefusalAreGatedApart()
    {
        SceneHelpers.AddScenePresence(m_scene, BlockedId);
        SceneObjectGroup original = SceneHelpers.CreateSceneObject(1, BlockedId, "original", 0x320);
        m_scene.AddNewSceneObject(original, false);

        for (int i = 0; i < 20; i++)
        {
            m_scene.Permissions.CanRezObject(1, BlockedId, RezPos);
            m_scene.SceneGraph.DuplicateObject(original.LocalId, new Vector3(1, 0, 0), BlockedId, UUID.Zero,
                Quaternion.Identity, false);
            m_scene.Permissions.CanObjectEntry(original, true, RezPos);
        }

        Assert.Single(Lines("Refused a rez of"));
        Assert.Single(Lines("Refused a duplicate of"));
        Assert.Single(Lines("Refused entry of"));
    }
}
