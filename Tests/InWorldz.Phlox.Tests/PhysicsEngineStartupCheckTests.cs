// [Confluence] A region whose physics engine never loaded used to reach "Startup complete" and throw on its first
// heartbeat; the region server now refuses to start it with a message naming what was asked for.

using Nini.Config;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Tests.Common;
using Xunit;

namespace InWorldz.Phlox.Tests;

public class PhysicsEngineStartupCheckTests
{
    private static IConfigSource Physics(string name)
    {
        var config = new IniConfigSource();
        IConfig startup = config.AddConfig("Startup");
        if (name is not null)
            startup.Set("physics", name);
        return config;
    }

    [Fact]
    public void ALoadedEngineIsNoProblem()
    {
        Scene scene = new SceneHelpers().SetupScene();   // SceneHelpers loads basicphysics

        Assert.NotNull(scene.PhysicsScene);
        Assert.Null(PhysicsEngineStartupCheck.Problem(Physics("basicphysics"), scene.PhysicsScene, scene.Name));
    }

    [Fact]
    public void AMisspeltNameIsCalledOutWithTheRealOne()
    {
        string problem = PhysicsEngineStartupCheck.Problem(Physics("ubode"), null, "Test Region");

        Assert.NotNull(problem);
        Assert.Contains("Test Region", problem);
        Assert.Contains("[Startup] physics = \"ubode\"", problem);
        Assert.Contains("\"ubODE\"", problem);
        Assert.Contains("Refusing to start", problem);
    }

    [Fact]
    public void AnUnknownNameListsTheEngines()
    {
        string problem = PhysicsEngineStartupCheck.Problem(Physics("Havok"), null, "Test Region");

        Assert.Contains("not one of ubODE, BulletSim, basicphysics, POS, Jolt", problem);
    }

    [Fact]
    public void AKnownEngineThatDidNotLoadPointsAtItsOwnError()
    {
        string problem = PhysicsEngineStartupCheck.Problem(Physics("ubODE"), null, "Test Region");

        Assert.Contains("the engine did not load", problem);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoNameAtAllIsSaidSo(string name)
    {
        string problem = PhysicsEngineStartupCheck.Problem(Physics(name), null, "Test Region");

        Assert.Contains("[Startup] physics is not set", problem);
    }

    [Fact]
    public void NoConfigAtAllStillGivesAMessage()
    {
        Assert.Contains("[Startup] physics is not set", PhysicsEngineStartupCheck.Problem(null, null, "Test Region"));
    }
}
