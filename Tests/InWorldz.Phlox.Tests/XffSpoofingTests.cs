using System.Net;
using Nini.Config;
using OpenSim.Framework;
using Xunit;

namespace InWorldz.Phlox.Tests;

/// <summary>
/// [Confluence] 2026-10-19: the client address of a request that reaches Robust or a region through a reverse proxy
/// on the same machine (Apache in front of Robust's public port). A scan sent forged X-Forwarded-For and Forwarded
/// headers naming 8.8.8.8, 127.0.0.1, 10.0.0.1, 192.168.1.1 and others; the proxy appends the real address to the right
/// of X-Forwarded-For, but Robust kept only the left-most (the client's own) entry, read "Forwarded" as given, and fell
/// back to the proxy's own loopback address when an entry could not be read - so a client could choose the address it
/// was taken to be from, including a trusted one. Nothing the client writes may decide the address any more.
/// </summary>
public class XffSpoofingTests
{
    private static readonly IPEndPoint ApacheOnThisMachine = new(IPAddress.Loopback, 54321);
    private const string ScannerReal = "217.60.241.20";

    static XffSpoofingTests()
    {
        // the Confluence default: unset trusts this machine (loopback) only
        var config = new IniConfigSource();
        NetworkXffUtil.Initialize(config.AddConfig("Network"));
    }

    private static IPAddress ClientOf(string chain, IPEndPoint peer = null)
        => NetworkXffUtil.CheckXFF(peer ?? ApacheOnThisMachine, chain, out IPEndPoint remote, out _)
            ? remote.Address
            : (peer ?? ApacheOnThisMachine).Address;

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("172.16.0.1")]
    [InlineData("1.1.1.1")]
    [InlineData("=8.8.8.8")]
    [InlineData("127.0.0.1, 127.0.0.1")]
    [InlineData("not-an-address")]
    public void AForgedEntryIsNeverTakenTheProxysAppendedAddressIs(string forged)
    {
        // what Apache sends on: the client's header, then the address Apache saw
        string chain = NetworkXffUtil.AppendChain(forged, ScannerReal);
        Assert.Equal(IPAddress.Parse(ScannerReal), ClientOf(chain));
    }

    [Fact]
    public void TwoForwardingHeadersAreOneChainInOrder()
    {
        string chain = NetworkXffUtil.AppendChain(NetworkXffUtil.AppendChain(null, "127.0.0.1"), ScannerReal);
        Assert.Equal("127.0.0.1, " + ScannerReal, chain);
        Assert.Equal(IPAddress.Parse(ScannerReal), ClientOf(chain));
    }

    [Theory]
    [InlineData("=8.8.8.8")]
    [InlineData("8.8.8.8, garbage")]
    [InlineData("")]
    public void AnUnreadableEntryWhereTheClientShouldBeIsAnUnknownClientNotTheTrustedProxy(string chain)
    {
        IPAddress client = ClientOf(chain);
        if (chain.Length == 0)
            Assert.Equal(IPAddress.Loopback, client);                     // no header: a request from this machine
        else
        {
            Assert.Equal(NetworkXffUtil.UnknownClient, client);
            Assert.False(IPAddress.IsLoopback(client));
            Assert.False(NetworkXffUtil.Contains(client));
        }
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.1, 127.0.0.1")]
    public void ARequestFromThisMachineThroughTheProxyIsThisMachineAndDoesNotThrow(string chain)
    {
        // what the proxy sends for a request that starts on this machine; the old code indexed addrs[-1] and threw,
        // so the proxy got no answer (502) for every request from the grid's own host
        Assert.Equal(IPAddress.Loopback, ClientOf(chain));
    }

    [Fact]
    public void AnUntrustedPeerCannotForwardAtAll()
    {
        var outsider = new IPEndPoint(IPAddress.Parse(ScannerReal), 40000);
        Assert.Equal(IPAddress.Parse(ScannerReal), ClientOf("127.0.0.1", outsider));
    }

    [Fact]
    public void ARealVisitorBehindTheProxyIsSeenAsThemselvesWithAPortOrIPv6()
    {
        Assert.Equal(IPAddress.Parse("203.0.113.9"), ClientOf("203.0.113.9"));
        Assert.Equal(IPAddress.Parse("203.0.113.9"), ClientOf("203.0.113.9:51234"));
        Assert.Equal(IPAddress.Parse("2001:db8::7"), ClientOf("[2001:db8::7]:443"));
        Assert.Equal(IPAddress.Parse("2001:db8::7"), ClientOf("2001:db8::7"));
        Assert.Null(NetworkXffUtil.ParseEntry("example.com"));            // no DNS on what a client wrote
    }

    [Fact]
    public void TheRequestParserKeepsTheWholeChainAndIgnoresForwarded()
    {
        var request = new OSHttpServer.HttpRequest(null);
        request.AddHeader("X-Forwarded-For", "192.168.1.1");               // the client's forged one
        request.AddHeader("X-Forwarded-For", ScannerReal);                 // the proxy's
        request.AddHeader("Forwarded", "for=127.0.0.1");
        Assert.Equal("192.168.1.1, " + ScannerReal, request.Headers["x-forwarded-for"]);
        Assert.Equal(IPAddress.Parse(ScannerReal), ClientOf(request.Headers["x-forwarded-for"]));
    }
}
