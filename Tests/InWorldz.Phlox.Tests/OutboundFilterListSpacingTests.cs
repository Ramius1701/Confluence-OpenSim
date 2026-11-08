using System;
using Nini.Config;
using OpenSim.Framework;
using Xunit;

namespace InWorldz.Phlox.Tests;

/// <summary>
/// [Confluence] (2026-11-08) The outbound filter lists read entries with spaces around the '|' separators. Upstream
/// 7a5950df3e dropped the per-entry Trim(), so " 192.168.0.0/16" no longer parsed and that range was let through.
/// </summary>
public class OutboundFilterListSpacingTests
{
    [Fact]
    public void EntriesWithSpacesAroundTheSeparatorStillBlock()
    {
        IConfigSource config = new IniConfigSource();
        IConfig network = config.AddConfig("Network");
        network.Set("OutboundDisallowForUserScripts", " 10.0.0.0/8 | 192.168.0.0/16 | 127.0.0.1:9000 ");
        network.Set("OutboundDisallowForUserScriptsExcept", "");

        var filter = new OutboundUrlFilter("Test", config);

        Assert.False(filter.CheckAllowed(new Uri("http://10.1.2.3/")));
        Assert.False(filter.CheckAllowed(new Uri("http://192.168.1.5/")));
        Assert.False(filter.CheckAllowed(new Uri("http://127.0.0.1:9000/")));
        Assert.True(filter.CheckAllowed(new Uri("http://8.8.8.8/")));
    }
}
