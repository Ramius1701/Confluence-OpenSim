// [Confluence] Tests for OpenSim-Tranquillity #412 (f588fcdf3e), adapted: Confluence keeps upstream's setting names
// ([Network] http_listener_address on a region, [Network] address on Robust) and makes every listener follow them,
// where Tranquillity added a new bind_ip_address. Each test checks the operating system's list of listening sockets,
// so it tests where a server really listens, not what it was told.

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Nini.Config;
using OpenSim.Framework;
using OpenSim.Framework.Console;
using OpenSim.Framework.Servers;
using OpenSim.Framework.Servers.HttpServer;
using Xunit;

namespace InWorldz.Phlox.Tests;

/// <summary>
/// Every HTTP and HTTPS listener binds where the configuration says: the plain and the HTTPS servers, and the servers
/// modules open later through MainServer.GetHttpServer. The HTTPS listener used to bind to every interface whatever
/// it was told, and a server a module opened always did.
/// </summary>
[Collection("MainServer")]
public class HttpListenAddressTests
{
    [Theory]
    [InlineData("127.0.0.1", "127.0.0.1")]
    [InlineData(" 127.0.0.1 ", "127.0.0.1")]
    [InlineData("::1", "::1")]
    [InlineData("0.0.0.0", "0.0.0.0")]
    [InlineData("grid.example.org", "0.0.0.0")]   // not an address: every interface, as before, and reported
    public void TheRegionsListenerAddressIsReadFromTheNetworkSection(string value, string expected)
    {
        var source = new IniConfigSource();
        source.AddConfig("Network").Set("http_listener_address", value);
        var info = new NetworkServersInfo();
        info.loadFromConfiguration(source);

        Assert.Equal(IPAddress.Parse(expected), info.HttpListenerAddress);
    }

    [Fact]
    public void HttpServerListensOnlyOnItsAddress()
    {
        uint port = FreePort();
        var server = new BaseHttpServer(port) { ListenIPAddress = IPAddress.Loopback };
        try
        {
            server.Start(false, false);
            Assert.Equal(new[] { IPAddress.Loopback }, ListeningAddresses(port));
        }
        finally
        {
            server.Stop();
        }
    }

    [Fact]
    public void HttpsServerListensOnlyOnItsAddress()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        string path = Path.GetTempFileName();
        uint port = FreePort();
        BaseHttpServer server = null;
        try
        {
            File.WriteAllBytes(path, certificate.Export(X509ContentType.Pkcs12, "listen-test"));
            server = new BaseHttpServer(port, true, "localhost", path, "listen-test") { ListenIPAddress = IPAddress.Loopback };
            server.Start(false, false);
            Assert.Equal(new[] { IPAddress.Loopback }, ListeningAddresses(port));
        }
        finally
        {
            server?.Stop();
            File.Delete(path);
        }
    }

    [Fact]
    public void AServerAModuleOpensFollowsTheDefaultAddress()
    {
        uint port = FreePort();
        IPAddress before = MainServer.DefaultListenIPAddress;
        ICommandConsole savedConsole = InstallConsole();
        MainServer.DefaultListenIPAddress = IPAddress.Loopback;
        IHttpServer server = null;
        try
        {
            server = MainServer.GetHttpServer(port, null);
            Assert.Equal(new[] { IPAddress.Loopback }, ListeningAddresses(port));
        }
        finally
        {
            MainServer.DefaultListenIPAddress = before;
            MainConsole.Instance = savedConsole;
            MainServer.RemoveHttpServer(port);
            (server as BaseHttpServer)?.Stop();
        }
    }

    [Fact]
    public void AnAddressTheModuleNamesWinsOverTheDefault()
    {
        uint port = FreePort();
        IPAddress before = MainServer.DefaultListenIPAddress;
        ICommandConsole savedConsole = InstallConsole();
        MainServer.DefaultListenIPAddress = IPAddress.Any;
        IHttpServer server = null;
        try
        {
            server = MainServer.GetHttpServer(port, IPAddress.Loopback);
            Assert.Equal(new[] { IPAddress.Loopback }, ListeningAddresses(port));
        }
        finally
        {
            MainServer.DefaultListenIPAddress = before;
            MainConsole.Instance = savedConsole;
            MainServer.RemoveHttpServer(port);
            (server as BaseHttpServer)?.Stop();
        }
    }

    [Fact]
    public void WithNothingSetAServerAModuleOpensListensOnEveryInterface()
    {
        Assert.Equal(IPAddress.Any, MainServer.DefaultListenIPAddress);
    }

    // MainServer.GetHttpServer starts the server with its poll pool, whose WorkManager registers console commands when
    // first used, as a running simulator always has a console. Returns the console it replaced.
    private static ICommandConsole InstallConsole()
    {
        ICommandConsole saved = MainConsole.Instance;
        MainConsole.Instance ??= new MockConsole();
        return saved;
    }

    private static uint FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return (uint)port;
    }

    private static IPAddress[] ListeningAddresses(uint port) =>
        IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
            .Where(e => e.Port == port)
            .Select(e => e.Address)
            .Distinct()
            .ToArray();
}
