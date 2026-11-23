using System.Net;
using Nini.Config;
using OpenSim.Data;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Server.Base;
using OpenSim.Services.LLLoginService;
using Xunit;

namespace InWorldz.Phlox.Tests;

/// <summary>
/// [Confluence] Checks for the Tranquillity fixes ported 2026-11-24 that sit outside the scene: the control plane's
/// trusted callers and trusted-host entries, the people search's words, and the stale online check at login.
/// (Tranquillity's own tests for these live in test projects this tree does not have.)
/// </summary>
// No process-wide state: each test builds its own objects, so the class runs in parallel.
public class TranquillityFixesBatchTests
{
    private static ControlPlaneAccess Access(string trustedHosts)
    {
        var config = new IniConfigSource();
        config.AddConfig("Network").Set("ControlPlaneTrustedHosts", trustedHosts);
        return new ControlPlaneAccess(config);
    }

    // 5e1e9979c0: a script's llHTTPRequest to its own host arrives from a trusted address; the XML-RPC server marks it,
    // and a marked caller is never trusted, so a script cannot send the grid kick (250) or a god teleport.
    [Fact]
    public void AScriptsXmlRpcRequestIsNeverATrustedCaller()
    {
        var access = Access("");
        var loopback = new IPEndPoint(IPAddress.Loopback, 9000);

        Assert.True(access.IsTrustedCaller(loopback));
        Assert.False(access.IsTrustedCaller(new ScriptRequestEndPoint(loopback)));
        Assert.False(access.IsTrustedCaller(null));
        Assert.False(access.AuthorizePrivilegedInstantMessage(250, new ScriptRequestEndPoint(loopback)));
        Assert.True(access.AuthorizePrivilegedInstantMessage(250, loopback));
        // ordinary IMs are not gated
        Assert.True(access.AuthorizePrivilegedInstantMessage(0, new ScriptRequestEndPoint(loopback)));
    }

    // 9e464d5395: "host:port", "[IPv6]:port" and a bare IPv6 address each trust the host they name (.NET read the
    // first and third as URIs with an empty host, and the empty name was looked up instead).
    [Theory]
    [InlineData("203.0.113.7:8003", "203.0.113.7")]
    [InlineData("203.0.113.8", "203.0.113.8")]
    [InlineData("[2001:db8::9]:8003", "2001:db8::9")]
    [InlineData("2001:db8::10", "2001:db8::10")]
    [InlineData("http://203.0.113.11:8003/", "203.0.113.11")]
    public void ATrustedHostEntryTrustsTheHostItNames(string entry, string address)
    {
        Assert.True(Access(entry).IsTrustedAddress(IPAddress.Parse(address)));
    }

    [Fact]
    public void AnEntryWithNoHostTrustsNothingExtra()
    {
        Assert.False(Access(":8003").IsTrustedAddress(IPAddress.Parse("203.0.113.12")));
    }

    // #466: the loop that dropped short words skipped the word after each one it dropped.
    [Theory]
    [InlineData("a bb ccc", new[] { "ccc" })]
    [InlineData("ab cd efg hi jkl", new[] { "efg", "jkl" })]
    [InlineData("  two   spaces  ", new[] { "two", "spaces" })]
    [InlineData("x y", new string[0])]
    public void APeopleSearchDropsEveryShortWord(string query, string[] expected)
    {
        Assert.Equal(expected, UserSearchWords.From(query));
    }

    // 8628c4762d: without a presence service nothing is taken as stale, so the login keeps its old duplicate check.
    [Fact]
    public void WithoutAPresenceServiceAnOnlineFlagIsNotStale()
    {
        Assert.False(LLLoginService.OnlineFlagIsStale(null, "00000000-0000-0000-0000-000000000001"));
    }
}
