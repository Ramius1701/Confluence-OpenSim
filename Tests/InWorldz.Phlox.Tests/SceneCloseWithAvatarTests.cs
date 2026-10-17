// [Confluence] From OpenSim-Tranquillity #287, adapted: the scene logs through log4net here, so the error line is
// caught with a log4net memory appender, and the checks are xunit asserts.

using log4net.Appender;
using log4net.Core;
using log4net.Repository.Hierarchy;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Tests.Common;
using Xunit;

namespace InWorldz.Phlox.Tests;

/// <summary>
/// Closing a region kicks each root avatar, and the kick waits long enough for the viewer to log out and the presence
/// to be removed. The close must not then use the client of a presence that is gone (it threw inside the scene graph's
/// loop, which logs "[SCENEGRAPH]: Error in ...").
/// </summary>
[Collection("process-logger")]
public class SceneCloseWithAvatarTests
{
    /// <summary>A client whose viewer logs out while the kick is waiting.</summary>
    private sealed class LogsOutDuringKick : TestClient
    {
        private readonly Scene m_scene;
        public bool Kicked;

        public LogsOutDuringKick(AgentCircuitData agentData, Scene scene) : base(agentData, scene)
        {
            m_scene = scene;
        }

        public override void Kick(string message)
        {
            Kicked = true;
            m_scene.RemoveClient(AgentId, false);
        }
    }

    [Fact]
    public void ClosingARegionWhileTheAvatarLogsOutDuringTheKickLogsNoSceneGraphError()
    {
        var hierarchy = (Hierarchy)log4net.LogManager.GetRepository(typeof(Scene).Assembly);
        var memory = new MemoryAppender { Threshold = Level.All };
        memory.ActivateOptions();
        Level previousLevel = hierarchy.Root.Level;
        hierarchy.Root.AddAppender(memory);
        hierarchy.Root.Level = Level.All;
        hierarchy.Configured = true;
        try
        {
            SceneHelpers sh = new SceneHelpers();
            Scene scene = sh.SetupScene();
            AgentCircuitData agent = SceneHelpers.GenerateAgentData(UUID.Random());
            var client = new LogsOutDuringKick(agent, scene);
            SceneHelpers.AddScenePresence(scene, client, agent);

            sh.SceneManager.Close();

            Assert.True(client.Kicked);
            Assert.True(scene.ShuttingDown);
            Assert.DoesNotContain(memory.GetEvents(), e => (e.RenderedMessage ?? "").Contains("[SCENEGRAPH]: Error in"));
        }
        finally
        {
            hierarchy.Root.RemoveAppender(memory);
            hierarchy.Root.Level = previousLevel;
        }
    }
}
