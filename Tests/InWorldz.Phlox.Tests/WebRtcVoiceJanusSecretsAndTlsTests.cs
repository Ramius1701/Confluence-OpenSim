// [Confluence] Tests for OpenSim-Tranquillity PR #349, written for Confluence's log4net Janus add-on (Tranquillity's
// capture Microsoft.Extensions.Logging lines and start the whole service against its fake gateway). The TLS cases are
// theirs, run against JanusTls's handler directly with their TlsTestServer.

using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using log4net.Appender;
using log4net.Core;
using log4net.Repository.Hierarchy;
using Nini.Config;
using OpenMetaverse.StructuredData;
using Xunit;

namespace osWebRtcVoice.Tests;

/// <summary>
/// The Janus API and admin secrets never reach a log line (request bodies and URLs are redacted), a short secret draws
/// a warning that names the key only, and [JanusWebRtcVoice] JanusGatewayCACertificateFile trusts a private CA for the
/// gateway without excusing a host name that does not match.
/// </summary>
public class WebRtcVoiceJanusSecretsAndTlsTests
{
    private const string Secret = "s3cr3t-value-never-in-a-log-0123456789";

    private sealed class SecretReq : JanusMessageReq
    {
        public SecretReq() : base("create")
        {
            m_message["apisecret"] = Secret;
            m_message["admin_secret"] = Secret + "-admin";
            m_message["transaction"] = "tx-42";
        }
    }

    [Fact]
    public void ALoggedRequestHidesBothSecretsAndKeepsTheTransaction()
    {
        var req = new SecretReq();

        string logged = req.ToJsonForLog();

        Assert.DoesNotContain(Secret, logged);
        Assert.Contains("<redacted apisecret>", logged);
        Assert.Contains("<redacted admin_secret>", logged);
        Assert.Contains("tx-42", logged);
        // The wire form still carries them.
        Assert.Contains(Secret, req.ToJson());
        Assert.Contains(Secret + "-admin", req.ToJson());
    }

    [Theory]
    [InlineData("http://janus.example.org:8088/janus/123?apisecret=abc", "http://janus.example.org:8088/janus/123?<redacted>")]
    [InlineData("http://janus.example.org:8088/janus/123#x", "http://janus.example.org:8088/janus/123?<redacted>")]
    [InlineData("http://janus.example.org:8088/janus/123", "http://janus.example.org:8088/janus/123")]
    [InlineData("", "")]
    [InlineData(null, null)]
    public void ALoggedUrlLosesItsQuery(string uri, string expected)
    {
        Assert.Equal(expected, JanusSession.RedactUriForLog(uri));
    }

    [Theory]
    [InlineData("short", true)]
    [InlineData("0123456789012345678901234567890", true)]     // 31
    [InlineData("01234567890123456789012345678901", false)]   // 32
    public void AShortSecretWarnsByKeyNameOnly(string token, bool warns)
    {
        var hierarchy = (Hierarchy)log4net.LogManager.GetRepository(typeof(WebRtcJanusService).Assembly);
        var memory = new MemoryAppender { Threshold = Level.All };
        bool wasConfigured = hierarchy.Configured;
        hierarchy.Root.AddAppender(memory);
        if (!wasConfigured) hierarchy.Configured = true;
        try
        {
            var config = new IniConfigSource();
            config.AddConfig("WebRtcVoice").Set("Enabled", "true");
            IConfig janus = config.AddConfig("JanusWebRtcVoice");
            janus.Set("JanusGatewayURI", "http://127.0.0.1:1/voice");      // nothing listens: the service disables itself
            janus.Set("APIToken", token);
            janus.Set("JanusGatewayAdminURI", "http://127.0.0.1:1/admin");
            janus.Set("AdminAPIToken", new string('a', 40));
            janus.Set("RequestTimeoutMs", "500");
            _ = new WebRtcJanusService(config);

            string[] lines = memory.GetEvents().Select(e => e.RenderedMessage ?? "").ToArray();
            Assert.Equal(warns, lines.Any(l => l.Contains("APIToken is shorter than 32 characters")));
            Assert.DoesNotContain(lines, l => l.Contains("AdminAPIToken is shorter"));
            // The value never appears (checked for the long tokens: "short" is part of the word "shorter").
            if (token.Length > 10)
                Assert.DoesNotContain(lines, l => l.Contains(token));
        }
        finally
        {
            hierarchy.Root.RemoveAppender(memory);
        }
    }

    // ---- JanusGatewayCACertificateFile (Tranquillity's JanusTlsTests, against the handler directly) ------------------

    private static readonly string Dir = Path.Combine(Path.GetTempPath(), "confluence-janus-tls-" + Guid.NewGuid().ToString("N"));

    private static async Task<bool> Reaches(TlsTestServer server, string caFile)
    {
        using var client = new HttpClient(JanusTls.CreateHandler(caFile)) { Timeout = TimeSpan.FromSeconds(10) };
        try
        {
            var body = new StringContent("{\"janus\":\"create\",\"transaction\":\"t1\"}");
            using HttpResponseMessage resp = await client.PostAsync(server.VoiceUri, body);
            return server.Requests > 0;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    [Fact]
    public async Task APrivateCaSignedCertificateIsAcceptedWhenTheFileNamesTheCa()
    {
        using X509Certificate2 ca = TlsTestCerts.NewCa("Example Private CA");
        string caFile = TlsTestCerts.WritePem(ca, Dir, "ca.pem");
        using var server = new TlsTestServer(TlsTestCerts.NewServer("127.0.0.1", ca));

        Assert.True(await Reaches(server, caFile));
    }

    [Fact]
    public async Task TheSameCertificateIsRefusedWithoutTheFile()
    {
        using X509Certificate2 ca = TlsTestCerts.NewCa("Example Private CA");
        using var server = new TlsTestServer(TlsTestCerts.NewServer("127.0.0.1", ca));

        Assert.False(await Reaches(server, null));
        Assert.Equal(0, server.Requests);
    }

    [Fact]
    public async Task NamingOneCertificateDoesNotTrustAnother()
    {
        using X509Certificate2 named = TlsTestCerts.NewServer("127.0.0.1", null);
        string file = TlsTestCerts.WritePem(named, Dir, "named.pem");
        using var server = new TlsTestServer(TlsTestCerts.NewServer("127.0.0.1", null));

        Assert.False(await Reaches(server, file));
    }

    [Fact]
    public async Task TheCaDoesNotExcuseAHostNameThatDoesNotMatch()
    {
        using X509Certificate2 ca = TlsTestCerts.NewCa("Example Private CA");
        string caFile = TlsTestCerts.WritePem(ca, Dir, "ca.pem");
        using var server = new TlsTestServer(TlsTestCerts.NewServer("mixer.example.org", ca));

        Assert.False(await Reaches(server, caFile));
    }

    [Fact]
    public async Task AFileThatCannotBeUsedFallsBackToTheSystemRoots()
    {
        Directory.CreateDirectory(Dir);
        string junk = Path.Combine(Dir, "junk.pem");
        File.WriteAllText(junk, "not a certificate");
        using X509Certificate2 ca = TlsTestCerts.NewCa("Example Private CA");
        using var server = new TlsTestServer(TlsTestCerts.NewServer("127.0.0.1", ca));

        Assert.False(await Reaches(server, junk));
    }

    [Fact]
    public async Task PlainHttpIsUnchanged()
    {
        using var server = new TlsTestServer(null);
        Assert.True(await Reaches(server, null));
    }
}
