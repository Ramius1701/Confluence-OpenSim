// [Confluence] Tests for OpenSim-Tranquillity #301/#319's ExternalIPResolver, written against the log4net port
// (Tranquillity's own tests capture Microsoft.Extensions.Logging lines).

using System.Net;
using Nini.Config;
using OpenSim.Services.HypergridService;
using Xunit;

namespace InWorldz.Phlox.Tests;

/// <summary>
/// The grid's public address for the user agent service's NAT fallback: looked up through the operating system
/// resolver from the gatekeeper's host or [UserAgentService] PublicAddress, looked up again on Refresh (and on a timer),
/// keeping the last good answer when a lookup fails, and never throwing when the first lookup fails (it used to stop
/// the service starting).
/// </summary>
public class ExternalIPResolverTests
{
    private sealed class FakeDns
    {
        public readonly Dictionary<string, IPAddress> Answers = new();
        public readonly List<string> Asked = new();
        public bool Fail;
        public IPAddress Resolve(string host)
        {
            Asked.Add(host);
            if (Fail) throw new System.Net.Sockets.SocketException(11001);
            return Answers.TryGetValue(host, out IPAddress ip) ? ip : null;
        }
    }

    private static IConfig Section(string publicAddress = null, int? refreshMinutes = null)
    {
        var config = new IniConfigSource();
        IConfig uas = config.AddConfig("UserAgentService");
        if (publicAddress != null) uas.Set("PublicAddress", publicAddress);
        if (refreshMinutes != null) uas.Set("ExternalIPRefreshMinutes", refreshMinutes.Value.ToString());
        return uas;
    }

    [Fact]
    public void WithNothingSetTheGatekeepersHostIsLookedUpAndRefreshedEveryThirtyMinutes()
    {
        var dns = new FakeDns();
        dns.Answers["grid.example.org"] = IPAddress.Parse("203.0.113.20");

        using var r = ExternalIPResolver.FromConfig(Section(), "grid.example.org", null, dns.Resolve);

        Assert.Equal("grid.example.org", r.Host);
        Assert.Equal("203.0.113.20", r.CurrentIP);
        Assert.Equal(TimeSpan.FromMinutes(30), r.RefreshInterval);
    }

    [Fact]
    public void PublicAddressIsLookedUpInPlaceOfTheGatekeepersHost()
    {
        var dns = new FakeDns();
        dns.Answers["grid.example.org"] = IPAddress.Parse("192.168.1.10");   // this machine's hosts file
        dns.Answers["public.example.org"] = IPAddress.Parse("203.0.113.20");

        using var r = ExternalIPResolver.FromConfig(Section("public.example.org", 0), "grid.example.org", null, dns.Resolve);

        Assert.Equal("203.0.113.20", r.CurrentIP);
        Assert.DoesNotContain("grid.example.org", dns.Asked);
    }

    [Fact]
    public void ADynamicDnsChangeIsPickedUpAndAFailedLookupKeepsTheLastGoodAddress()
    {
        var dns = new FakeDns();
        dns.Answers["grid.example.org"] = IPAddress.Parse("203.0.113.20");
        using var r = new ExternalIPResolver("grid.example.org", TimeSpan.Zero, null, dns.Resolve);
        Assert.Equal("203.0.113.20", r.CurrentIP);

        dns.Answers["grid.example.org"] = IPAddress.Parse("203.0.113.99");
        r.Refresh();
        Assert.Equal("203.0.113.99", r.CurrentIP);

        dns.Fail = true;
        r.Refresh();
        Assert.Equal("203.0.113.99", r.CurrentIP);

        dns.Fail = false;
        dns.Answers.Remove("grid.example.org");                              // no answer at all
        r.Refresh();
        Assert.Equal("203.0.113.99", r.CurrentIP);
    }

    [Fact]
    public void AFirstLookupThatFailsDoesNotThrowAndALaterRefreshSucceeds()
    {
        var dns = new FakeDns { Fail = true };
        using var r = new ExternalIPResolver("grid.example.org", TimeSpan.Zero, null, dns.Resolve);
        Assert.Equal(string.Empty, r.CurrentIP);

        dns.Fail = false;
        dns.Answers["grid.example.org"] = IPAddress.Parse("203.0.113.20");
        r.Refresh();
        Assert.Equal("203.0.113.20", r.CurrentIP);
    }
}
