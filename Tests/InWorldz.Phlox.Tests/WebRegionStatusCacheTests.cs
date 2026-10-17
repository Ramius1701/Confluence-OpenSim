using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using OpenMetaverse;
using OpenSim.Server.Handlers.WebInterface;
using Xunit;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;

namespace InWorldz.Phlox.Tests;

/// <summary>
/// [Confluence] The web portal's region status cache (2026-10-17): pages read which regions are up from a background
/// check instead of probing every region on every load (a down region made the welcome page wait 3 s or more). A
/// region not checked yet counts as up; after a check, a closed port is down and an open one up; reading never waits.
/// </summary>
public class WebRegionStatusCacheTests
{
    private static readonly Type T = typeof(WebInterfaceServiceConnector);

    private static bool Cached(GridRegion r)
        => (bool)T.GetMethod("IsRegionOnlineCached", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { r })!;

    private static void Refresh()
        => T.GetMethod("RefreshRegionStatus", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static GridRegion Region(int port) => new GridRegion
    {
        RegionID = UUID.Random(),
        RegionName = "status-" + port,
        ServerURI = "http://127.0.0.1:" + port + "/",
        InternalEndPoint = new IPEndPoint(IPAddress.Loopback, port),
    };

    [Fact]
    public void AnUncheckedRegionIsUpThenTheCheckTellsADownOneFromAnUpOne()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            GridRegion up = Region(((IPEndPoint)listener.LocalEndpoint).Port);
            GridRegion down = Region(FreePort());

            // The web interface class's one-time static setup is large; pay it before timing (Robust pays it at start).
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(T.TypeHandle);

            var sw = Stopwatch.StartNew();
            Assert.True(Cached(up));
            Assert.True(Cached(down));          // not checked yet: counted as up, no waiting
            Assert.True(sw.ElapsedMilliseconds < 500, "reading the cache took " + sw.ElapsedMilliseconds + " ms");

            Refresh();

            sw.Restart();
            Assert.True(Cached(up));
            Assert.False(Cached(down));
            Assert.True(sw.ElapsedMilliseconds < 500, "reading the cache took " + sw.ElapsedMilliseconds + " ms");
        }
        finally
        {
            listener.Stop();
        }
    }
}
