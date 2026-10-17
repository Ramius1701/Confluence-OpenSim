using OpenMetaverse;
using OpenSim.Framework;
using Xunit;

namespace InWorldz.Phlox.Tests;

/// <summary>[Confluence] From OpenSim-Tranquillity #287: Util.IsNanOrInfinity tested Z for NaN twice, never for infinity.</summary>
public class UtilNanOrInfinityTests
{
    [Theory]
    [InlineData(float.NaN, 0f, 0f)]
    [InlineData(0f, float.NaN, 0f)]
    [InlineData(0f, 0f, float.NaN)]
    [InlineData(float.PositiveInfinity, 0f, 0f)]
    [InlineData(0f, float.PositiveInfinity, 0f)]
    [InlineData(0f, 0f, float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity, 0f, 0f)]
    [InlineData(0f, float.NegativeInfinity, 0f)]
    [InlineData(0f, 0f, float.NegativeInfinity)]
    public void IsNanOrInfinityFlagsAnyBadComponent(float x, float y, float z)
    {
        Assert.True(OpenSim.Framework.Util.IsNanOrInfinity(new Vector3(x, y, z)));
    }

    [Fact]
    public void IsNanOrInfinityAcceptsFiniteVectors()
    {
        Assert.False(OpenSim.Framework.Util.IsNanOrInfinity(Vector3.Zero));
        Assert.False(OpenSim.Framework.Util.IsNanOrInfinity(new Vector3(1f, -2f, 3f)));
        Assert.False(OpenSim.Framework.Util.IsNanOrInfinity(new Vector3(float.MaxValue, float.MinValue, float.Epsilon)));
    }
}
