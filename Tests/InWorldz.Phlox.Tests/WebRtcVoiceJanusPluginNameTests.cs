// [Confluence] From OpenSim-Tranquillity PR #347's JanusPluginNameTests, converted to xunit. Confluence reads the
// setting inline in WebRtcJanusService, so the default is checked on JanusAudioBridge.DefaultPluginName and the
// unset-key path through the bridge's constructor.

using Xunit;

namespace osWebRtcVoice.Tests;

/// <summary>
/// [JanusWebRtcVoice] PluginName picks the Janus plugin (mixer) voice handles attach to; unset, it is the stock
/// AudioBridge plugin (it used to be fixed to it).
/// </summary>
public class WebRtcVoiceJanusPluginNameTests
{
    // JanusSession's constructor only stores its fields (no network).
    private static JanusSession NewSession() =>
        new JanusSession("http://127.0.0.1:24223/voice", "tok", "http://127.0.0.1:24225/voiceAdmin", "tok");

    [Fact]
    public void TheBridgeAttachesToTheConfiguredPlugin()
    {
        var ab = new JanusAudioBridge(NewSession(), "janus.plugin.slvoice");
        Assert.Equal("janus.plugin.slvoice", ab.PluginName);
    }

    [Fact]
    public void TheAudioBridgeNameFlowsThrough()
    {
        var ab = new JanusAudioBridge(NewSession(), "janus.plugin.audiobridge");
        Assert.Equal("janus.plugin.audiobridge", ab.PluginName);
    }

    [Fact]
    public void TheDefaultIsTheStockAudioBridge()
    {
        Assert.Equal("janus.plugin.audiobridge", JanusAudioBridge.DefaultPluginName);
        Assert.Equal("janus.plugin.audiobridge", new JanusAudioBridge(NewSession()).PluginName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankNameFallsBackToTheStockAudioBridge(string name)
    {
        // Confluence addition: "PluginName =" left empty in an ini must not attach to a plugin with no name.
        Assert.Equal("janus.plugin.audiobridge", new JanusAudioBridge(NewSession(), name).PluginName);
    }
}
