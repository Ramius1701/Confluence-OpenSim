// [Confluence] Tests for OpenSim-Tranquillity PR #348's bounded Janus requests, written for Confluence's JanusSession
// (it posts through WebUtil's shared client, so the gateway here is a tiny local HTTP listener that answers every POST
// with an "ack" and never sends the completing event). Tranquillity's JanusSessionTimeoutTests use a message-handler
// seam Confluence's session does not have.

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenSim.Framework;
using Xunit;

namespace osWebRtcVoice.Tests;

/// <summary>
/// A Janus request answered with an ack waits for its event for at most RequestTimeoutMs, then fails with the reason
/// "timeout" (it used to wait without limit), and leaves no entry behind; destroying the session fails whatever is
/// still waiting with "session destroyed".
/// </summary>
public class WebRtcVoiceJanusRequestTimeoutTests
{
    /// <summary>Answers every request with {"janus":"ack"} and never sends an event.</summary>
    private sealed class AckOnlyGateway : IDisposable
    {
        private readonly TcpListener m_listener = new TcpListener(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource m_stop = new();
        public int Requests;

        public AckOnlyGateway()
        {
            m_listener.Start();
            _ = Task.Run(Serve);
        }

        public string Uri => $"http://127.0.0.1:{((IPEndPoint)m_listener.LocalEndpoint).Port}/janus";

        private async Task Serve()
        {
            while (!m_stop.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await m_listener.AcceptTcpClientAsync(m_stop.Token); }
                catch { return; }
                _ = Task.Run(() => Answer(client));
            }
        }

        private async Task Answer(TcpClient client)
        {
            using (client)
            {
                NetworkStream stream = client.GetStream();
                byte[] buf = new byte[16384];
                var text = new StringBuilder();
                int contentLength = -1, headerEnd = -1;
                while (true)
                {
                    int n = await stream.ReadAsync(buf, 0, buf.Length);
                    if (n <= 0) return;
                    text.Append(Encoding.ASCII.GetString(buf, 0, n));
                    string s = text.ToString();
                    if (headerEnd < 0 && (headerEnd = s.IndexOf("\r\n\r\n", StringComparison.Ordinal)) >= 0)
                    {
                        foreach (string line in s.Substring(0, headerEnd).Split("\r\n"))
                            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                                contentLength = int.Parse(line.Substring(15).Trim());
                    }
                    if (headerEnd >= 0 && s.Length - (headerEnd + 4) >= Math.Max(0, contentLength))
                        break;
                }
                string req = text.ToString();
                string body = req.Substring(headerEnd + 4);
                string tx = "";
                int i = body.IndexOf("\"transaction\"", StringComparison.Ordinal);
                if (i >= 0)
                {
                    int q1 = body.IndexOf('"', body.IndexOf(':', i) + 1);
                    tx = body.Substring(q1 + 1, body.IndexOf('"', q1 + 1) - q1 - 1);
                }
                Interlocked.Increment(ref Requests);
                string json = "{\"janus\":\"ack\",\"transaction\":\"" + tx + "\"}";
                string resp = "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: " + json.Length +
                              "\r\nConnection: close\r\n\r\n" + json;
                byte[] bytes = Encoding.ASCII.GetBytes(resp);
                await stream.WriteAsync(bytes, 0, bytes.Length);
            }
        }

        public void Dispose()
        {
            m_stop.Cancel();
            m_listener.Stop();
        }
    }

    public WebRtcVoiceJanusRequestTimeoutTests()
    {
        if (WebUtil.SharedSocketsHttpHandler is null)
            WebUtil.SetupHTTPClients(false, false, null, 8);
    }

    private static string Reason(JanusMessageResp resp) => new ErrorResp(resp).errorReason;

    [Fact]
    public void TheDefaultIsFiveSeconds()
    {
        Assert.Equal(5000, JanusSession.DefaultRequestTimeoutMs);
        var session = new JanusSession("http://127.0.0.1:1/janus", "tok", "http://127.0.0.1:1/admin", "tok");
        Assert.Equal(TimeSpan.FromSeconds(5), session.JanusRequestTimeout);
    }

    [Fact]
    public async Task AnAckWithNoEventFailsWithTimeoutAndLeavesNothingParked()
    {
        using var gw = new AckOnlyGateway();
        var session = new JanusSession(gw.Uri, "tok", gw.Uri, "tok") { JanusRequestTimeout = TimeSpan.FromMilliseconds(300) };

        var started = DateTime.UtcNow;
        JanusMessageResp resp = await session.SendToJanus(new CreateSessionReq(), gw.Uri);
        TimeSpan took = DateTime.UtcNow - started;

        Assert.Equal(1, gw.Requests);
        Assert.NotNull(resp);
        Assert.True(resp.isError, "a timed-out request reads as an ordinary Janus error");
        Assert.Equal(JanusSession.RequestTimeoutReason, Reason(resp));
        Assert.True(took >= TimeSpan.FromMilliseconds(250), $"waited {took.TotalMilliseconds} ms");
        Assert.True(took < TimeSpan.FromSeconds(5), $"waited {took.TotalMilliseconds} ms - not bounded");
        Assert.Equal(0, session.OutstandingRequestCount);
    }

    [Fact]
    public async Task DestroyingTheSessionFailsAWaitingRequestAtOnce()
    {
        using var gw = new AckOnlyGateway();
        var session = new JanusSession(gw.Uri, "tok", gw.Uri, "tok") { JanusRequestTimeout = TimeSpan.FromSeconds(30) };

        Task<JanusMessageResp> waiting = session.SendToJanus(new CreateSessionReq(), gw.Uri);
        for (int i = 0; i < 100 && gw.Requests == 0; i++)
            await Task.Delay(20);
        await Task.Delay(100);   // let the ack arrive, so the request is waiting for its event

        _ = session.DestroySession();
        Task first = await Task.WhenAny(waiting, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.Same(waiting, first);
        JanusMessageResp resp = await waiting;
        Assert.True(resp.isError);
        Assert.Contains(Reason(resp), new[] { JanusSession.SessionDestroyedReason, JanusSession.RequestTimeoutReason });
        Assert.Equal(0, session.OutstandingRequestCount);
    }
}
