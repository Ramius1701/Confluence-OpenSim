// [Confluence] Tranquillity #387's tests for [UserAgentService] DnsServers and the private-address warning,
// written against the log4net port (CapturingLogger from ViewerAddressTestLog.cs). The older resolver tests
// stay in ExternalIPResolverTests.cs.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Nini.Config;
using OpenSim.Framework.Tests;
using OpenSim.Services.HypergridService;
using Xunit;

namespace InWorldz.Phlox.Tests;

public class ExternalIPResolverDnsServerTests
{
    private static IPAddress Ip(string s) => IPAddress.Parse(s);

    private static IConfigSource Config(params (string Key, string Value)[] keys)
    {
        IniConfigSource source = new();
        IConfig section = source.AddConfig("UserAgentService");
        foreach ((string key, string value) in keys)
            section.Set(key, value);
        return source;
    }

    // ---- [UserAgentService] DnsServers: ask named DNS servers first ------------------------
    //
    // The DNS servers are replaced by a delegate (dnsLookup), so these tests send nothing on the network.

    private const string TwoServers = "192.0.2.53, 198.51.100.53";

    /// <summary>A stand-in for the DNS servers: records each name asked and gives the next scripted answer.</summary>
    private sealed class FakeDnsServers
    {
        private readonly Queue<Func<IPAddress?>> m_answers = new();
        public readonly List<string> Asked = new();

        public FakeDnsServers Then(string? address)
        {
            m_answers.Enqueue(() => address is null ? null : Ip(address));
            return this;
        }

        public FakeDnsServers ThenThrow()
        {
            m_answers.Enqueue(() => throw new System.Net.Sockets.SocketException(10060));
            return this;
        }

        public IPAddress? Lookup(string host)
        {
            Asked.Add(host);
            return m_answers.Count > 0 ? m_answers.Dequeue()() : null;
        }
    }

    [Fact]
    public void DnsServers_IsReadFromTheSection_AsACommaOrSemicolonSeparatedListOfAddresses()
    {
        CapturingLogger log = new();

        using ExternalIPResolver r = ExternalIPResolver.FromConfig(
            Config(("PublicAddress", "public.example.org"), ("DnsServers", " 192.0.2.53 ; 198.51.100.53, ")),
            "gate.example.org", log, osResolver: _ => Ip("203.0.113.10"),
            dnsLookup: new FakeDnsServers().Then("203.0.113.10").Lookup);

        Assert.Equal(new[] { Ip("192.0.2.53"), Ip("198.51.100.53") }, r.DnsServers);
        Assert.Equal(0, log.Warnings);
    }

    [Fact]
    public void DnsServers_EntryThatIsNotAnAddress_IsIgnoredWithAWarning()
    {
        CapturingLogger log = new();

        using ExternalIPResolver r = ExternalIPResolver.FromConfig(
            Config(("DnsServers", "192.0.2.53, dns.example.org, 0.0.0.0")),
            "gate.example.org", log, osResolver: _ => Ip("203.0.113.10"),
            dnsLookup: new FakeDnsServers().Then("203.0.113.10").Lookup);

        Assert.Equal(new[] { Ip("192.0.2.53") }, r.DnsServers);
        Assert.Contains(log.Entries, e => e.Level == CapturedLevel.Warning && e.Message.Contains("dns.example.org")
            && e.Message.Contains("DnsServers"));
        Assert.Contains(log.Entries, e => e.Level == CapturedLevel.Warning && e.Message.Contains("\"0.0.0.0\"")
            && e.Message.Contains("DnsServers"));
    }

    [Fact]
    public void DnsServersSet_TheNameIsAskedOfTheServersFirst_AndTheOperatingSystemIsNotAsked()
    {
        CapturingLogger log = new();
        FakeDnsServers dns = new FakeDnsServers().Then("203.0.113.20");
        int osCalls = 0;

        // The operating system would give the hosts-file answer (a LAN address).
        using ExternalIPResolver r = ExternalIPResolver.FromConfig(
            Config(("PublicAddress", "public.example.org"), ("DnsServers", TwoServers)),
            "gate.example.org", log, osResolver: _ => { osCalls++; return Ip("192.168.0.25"); },
            dnsLookup: dns.Lookup);

        Assert.Equal("203.0.113.20", r.CurrentIP);
        Assert.Equal(new[] { "public.example.org" }, dns.Asked);
        Assert.Equal(0, osCalls);
        Assert.Equal(0, log.Warnings);
        // The startup line names the servers the answer came from.
        Assert.Contains(log.Entries, e => e.Level == CapturedLevel.Information
            && e.Message == "[EXTERNAL IP RESOLVER]: public.example.org resolved to 203.0.113.20 via DNS servers 192.0.2.53, 198.51.100.53");
    }

    [Fact]
    public void DnsServersSet_PublicAddressUnset_TheGatekeeperHostIsAskedOfTheServers()
    {
        FakeDnsServers dns = new FakeDnsServers().Then("203.0.113.20");

        using ExternalIPResolver r = ExternalIPResolver.FromConfig(Config(("DnsServers", TwoServers)),
            "gate.example.org", new CapturingLogger(), osResolver: _ => Ip("192.168.0.25"), dnsLookup: dns.Lookup);

        Assert.Equal(new[] { "gate.example.org" }, dns.Asked);
        Assert.Equal("203.0.113.20", r.CurrentIP);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DnsServersSet_NoServerAnswers_AndNoPreviousValue_TheOperatingSystemAnswers_WithAWarning(bool serversThrow)
    {
        CapturingLogger log = new();
        FakeDnsServers dns = serversThrow ? new FakeDnsServers().ThenThrow() : new FakeDnsServers().Then(null);
        int osCalls = 0;

        using ExternalIPResolver r = ExternalIPResolver.FromConfig(
            Config(("PublicAddress", "public.example.org"), ("DnsServers", TwoServers)),
            "gate.example.org", log, osResolver: _ => { osCalls++; return Ip("203.0.113.10"); }, dnsLookup: dns.Lookup);

        Assert.Equal(new[] { "public.example.org" }, dns.Asked);
        Assert.Equal(1, osCalls);
        Assert.Equal("203.0.113.10", r.CurrentIP);
        Assert.Contains(log.Entries, e => e.Level == CapturedLevel.Warning
            && e.Message.Contains("no DNS server answered") && e.Message.Contains("operating system"));
        // The operating system's answer is not reported as coming from the servers.
        Assert.DoesNotContain(log.Entries, e => e.Message.Contains("via DNS servers"));
    }

    [Fact]
    public void DnsServersSet_KeepsTheLastGoodAddress_WhenNoServerAnswersLater_AndDoesNotAskTheOperatingSystem()
    {
        CapturingLogger log = new();
        FakeDnsServers dns = new FakeDnsServers().Then("203.0.113.20").Then(null).ThenThrow().Then("203.0.113.21");
        int osCalls = 0;

        using ExternalIPResolver r = ExternalIPResolver.FromConfig(
            Config(("PublicAddress", "public.example.org"), ("DnsServers", TwoServers)),
            "gate.example.org", log, osResolver: _ => { osCalls++; return Ip("192.168.0.25"); }, dnsLookup: dns.Lookup);
        Assert.Equal("203.0.113.20", r.CurrentIP);

        Assert.False(r.Refresh());                       // no answer
        Assert.Equal("203.0.113.20", r.CurrentIP);
        Assert.False(r.Refresh());                       // servers fail
        Assert.Equal("203.0.113.20", r.CurrentIP);
        Assert.Equal(0, osCalls);
        Assert.Equal(2, log.Entries.Count(e => e.Level == CapturedLevel.Warning
            && e.Message.Contains("no DNS server answered") && e.Message.Contains("203.0.113.20")));

        Assert.True(r.Refresh());                        // dynamic DNS moved
        Assert.Equal("203.0.113.21", r.CurrentIP);
        Assert.Equal(4, dns.Asked.Count);
        Assert.Contains(log.Entries, e => e.Level == CapturedLevel.Information
            && e.Message.Contains("changed from 203.0.113.20 to 203.0.113.21") && e.Message.Contains("via DNS servers"));
    }

    [Fact]
    public void DnsServersUnset_OnlyTheOperatingSystemIsAsked_AsBefore()
    {
        CapturingLogger log = new();
        FakeDnsServers dns = new FakeDnsServers().Then("203.0.113.20");
        int osCalls = 0;

        using ExternalIPResolver r = ExternalIPResolver.FromConfig(Config(("PublicAddress", "public.example.org")),
            "gate.example.org", log, osResolver: _ => { osCalls++; return Ip("203.0.113.10"); }, dnsLookup: dns.Lookup);
        Assert.True(r.Refresh());

        Assert.Empty(r.DnsServers);
        Assert.Empty(dns.Asked);
        Assert.Equal(2, osCalls);
        Assert.Equal("203.0.113.10", r.CurrentIP);
        Assert.Contains(log.Entries, e => e.Level == CapturedLevel.Information
            && e.Message == "[EXTERNAL IP RESOLVER]: public.example.org resolved to 203.0.113.10");
        Assert.Equal(0, log.Warnings);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void DnsServersBlank_IsTheSameAsUnset(string value)
    {
        FakeDnsServers dns = new FakeDnsServers().Then("203.0.113.20");

        using ExternalIPResolver r = ExternalIPResolver.FromConfig(Config(("DnsServers", value)),
            "gate.example.org", new CapturingLogger(), osResolver: _ => Ip("203.0.113.10"), dnsLookup: dns.Lookup);

        Assert.Empty(r.DnsServers);
        Assert.Empty(dns.Asked);
        Assert.Equal("203.0.113.10", r.CurrentIP);
    }

    [Theory]
    [InlineData("198.51.100.9")]
    [InlineData("2001:db8::9")]
    public void PublicAddressLiteral_IsUsedAsIs_WithNoLookupOfAnyKind(string literal)
    {
        FakeDnsServers dns = new FakeDnsServers().Then("203.0.113.20");
        int osCalls = 0;

        using ExternalIPResolver r = ExternalIPResolver.FromConfig(
            Config(("PublicAddress", literal), ("DnsServers", TwoServers)),
            "gate.example.org", new CapturingLogger(), osResolver: _ => { osCalls++; return Ip("192.168.0.25"); },
            dnsLookup: dns.Lookup);
        Assert.True(r.Refresh());

        Assert.Equal(Ip(literal).ToString(), r.CurrentIP);
        Assert.Empty(dns.Asked);
        Assert.Equal(0, osCalls);
    }

    // ---- startup warning: the address settled on is not a public one ----------------------

    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.4.2")]
    [InlineData("192.168.0.25")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.10.1")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fd00::5")]
    public void FromConfig_APrivateLoopbackOrLinkLocalResult_LogsAWarningNamingTheFix(string address)
    {
        CapturingLogger log = new();

        using ExternalIPResolver r = ExternalIPResolver.FromConfig(Config(), "gate.example.org", log,
            osResolver: _ => Ip(address));

        Assert.Equal(Ip(address).ToString(), r.CurrentIP);
        string warning = Assert.Single(log.Entries, e => e.Level == CapturedLevel.Warning).Message;
        Assert.Contains(Ip(address).ToString(), warning);
        Assert.Contains("NAT fallback", warning);
        // Visitors to grids on the same network still pass on the login address comparison, so the warning is
        // only about grids outside that network.
        Assert.Contains("users logged in from that network will be refused when they go to a grid outside it", warning);
        Assert.DoesNotContain("another grid", warning);
        Assert.Contains("PublicAddress", warning);
        Assert.Contains("DnsServers", warning);
    }

    [Theory]
    [InlineData("203.0.113.10")]
    [InlineData("172.32.0.1")]
    [InlineData("2001:db8::9")]
    public void FromConfig_APublicResult_LogsNoWarning(string address)
    {
        CapturingLogger log = new();

        using ExternalIPResolver r = ExternalIPResolver.FromConfig(Config(), "gate.example.org", log,
            osResolver: _ => Ip(address));

        Assert.Equal(Ip(address).ToString(), r.CurrentIP);
        Assert.Equal(0, log.Warnings);
    }

    [Fact]
    public void FromConfig_APrivateResultFromTheDnsServers_AlsoWarns()
    {
        CapturingLogger log = new();

        using ExternalIPResolver r = ExternalIPResolver.FromConfig(Config(("DnsServers", TwoServers)),
            "gate.example.org", log, osResolver: _ => Ip("203.0.113.10"),
            dnsLookup: new FakeDnsServers().Then("10.0.0.5").Lookup);

        Assert.Equal("10.0.0.5", r.CurrentIP);
        Assert.Contains(log.Entries, e => e.Level == CapturedLevel.Warning && e.Message.Contains("10.0.0.5")
            && e.Message.Contains("NAT fallback") && e.Message.Contains("when they go to a grid outside it"));
    }

    [Theory]
    [InlineData("10.0.0.5", true)]
    [InlineData("172.31.255.255", true)]
    [InlineData("192.168.0.25", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("169.254.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("fc00::1", true)]
    [InlineData("::ffff:192.168.0.25", true)]
    [InlineData("203.0.113.10", false)]
    [InlineData("172.15.0.1", false)]
    [InlineData("11.0.0.1", false)]
    [InlineData("2001:db8::9", false)]
    public void IsPrivateLoopbackOrLinkLocal_ClassifiesAddresses(string address, bool expected)
    {
        Assert.Equal(expected, ExternalIPResolver.IsPrivateLoopbackOrLinkLocal(Ip(address)));
    }
}
