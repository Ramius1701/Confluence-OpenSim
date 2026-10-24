// [Confluence] Written with nullable annotations (from Tranquillity); this project has nullable off.
#nullable enable annotations

using OpenSim.Framework.Tests;
using log4net;
/*
 * Copyright (c) Legion Builds
 * Copyright (c) 2026 Legion Builds
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

// [Confluence] From OpenSim-Tranquillity #302/#327 (Tests/OpenSim.Server.Handlers.Tests/LoginViewerAddressTests.cs), moved into the Phlox test project.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Collections;
using System.Net;
using Nwc.XmlRpc;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using OpenSim.Server.Handlers.Login;
using OpenSim.Services.Interfaces;
using OpenSim.Services.LLLoginService;
using Assert = Xunit.Assert;
using FriendInfo = OpenSim.Services.Interfaces.FriendInfo;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;
using Xunit;

namespace OpenSim.Server.Handlers.Tests;

/// <summary>
/// The region address a viewer is given in the login response, and the address the login handler classifies
/// the viewer by. Only documentation (192.0.2.0/24, 198.51.100.0/24, 203.0.113.0/24) and generic private
/// ranges are used.
/// </summary>
public class LoginViewerAddressTests
{
    private const int RegionPort = 9000;
    private const string LanViewer = "192.168.1.50";
    private const string RemoteViewer = "203.0.113.25";

    /// <summary>The viewers on <paramref name="networks"/> keep the region's own address; every other viewer is given <paramref name="publicAddress"/>.</summary>
    private static ViewerAddressSelector Selector(string publicAddress = "198.51.100.7", string networks = "192.168.0.0/16")
        => ViewerAddressSelector.Create(
            ViewerAddressConfig.Create(
                networks + (publicAddress.Length > 0 ? ", 0.0.0.0/0=" + publicAddress : string.Empty), 30, new CapturingLogger()),
            new CapturingLogger());

    private static GridRegion Region() => new()
    {
        RegionName = "Example",
        ExternalHostName = "192.168.1.10",
        InternalEndPoint = new IPEndPoint(IPAddress.Any, RegionPort),
        ServerURI = "http://grid.example.org:9000/",
    };

    private static LLLoginResponse Response(string clientIp, ViewerAddressSelector? selector, IPAddress? viewerSource = null,
        bool destinationIsLocal = true, GridRegion? destination = null)
    {
        UserAccount account = new() { FirstName = "Test", LastName = "User", PrincipalID = UUID.Random() };
        AgentCircuitData circuit = new() { circuitcode = 1234, SessionID = UUID.Random(), SecureSessionID = UUID.Random(), CapsPath = "caps" };
        GridUserInfo grid = new() { HomePosition = new Vector3(128, 128, 30), HomeLookAt = Vector3.UnitX };

        LLLoginResponse response = new LLLoginResponse(account, circuit, grid, destination ?? Region(), new List<InventoryFolderBase>(),
            Array.Empty<FriendInfo>(), null!, "last", "last", new Vector3(128, 128, 30), Vector3.UnitX,
            new List<InventoryItemBase>(), "welcome", null!, new IPEndPoint(IPAddress.Parse(clientIp), 0),
            "", "", "", "", "", "none", "", "", UUID.Zero, "", 0,
            selector!, viewerSource!, destinationIsLocal);
        // the library lists are only filled when a library service is given; the LLSD form needs them to exist
        foreach (System.Reflection.FieldInfo field in typeof(LLLoginResponse).GetFields(
                     System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic))
        {
            if (field.FieldType == typeof(ArrayList) && field.GetValue(response) is null)
                field.SetValue(response, new ArrayList());
        }
        return response;
    }

    private static string Xml(LLLoginResponse r) => (string)r.ToHashtable()["sim_ip"]!;
    private static int XmlPort(LLLoginResponse r) => (int)r.ToHashtable()["sim_port"]!;
    private static string Llsd(LLLoginResponse r) => ((OSDMap)r.ToOSDMap())["sim_ip"].AsString();

    // ---- no keys: exactly today ----

    [Fact]
    public void NoSelector_GivesTheRegionsOwnAddressInBothForms()
    {
        LLLoginResponse r = Response(RemoteViewer, null);

        Assert.Equal("192.168.1.10", Xml(r));
        Assert.Equal("192.168.1.10", Llsd(r));
        Assert.Equal(RegionPort, XmlPort(r));
    }

    [Fact]
    public void DisabledSelector_GivesTheRegionsOwnAddress_ForEverySource()
    {
        foreach (string source in new[] { LanViewer, RemoteViewer, "127.0.0.1" })
        {
            LLLoginResponse r = Response(source, ViewerAddressSelector.Disabled);
            Assert.Equal("192.168.1.10", Xml(r));
            Assert.Equal("192.168.1.10", Llsd(r));
        }
    }

    [Fact]
    public void BareNetworksAlone_ChangeNothing()
    {
        LLLoginResponse r = Response(RemoteViewer, Selector(publicAddress: string.Empty));

        Assert.Equal("192.168.1.10", Xml(r));
    }

    // ---- keys set ----

    [Fact]
    public void LanViewer_KeepsTheRegionsOwnAddress_InBothForms()
    {
        LLLoginResponse r = Response(LanViewer, Selector());

        Assert.Equal("192.168.1.10", Xml(r));
        Assert.Equal("192.168.1.10", Llsd(r));
    }

    [Fact]
    public void RemoteViewer_GetsThePublicAddress_InBothForms_WithTheSamePort()
    {
        LLLoginResponse r = Response(RemoteViewer, Selector());

        Assert.Equal("198.51.100.7", Xml(r));
        Assert.Equal("198.51.100.7", Llsd(r));
        Assert.Equal(RegionPort, XmlPort(r));
    }

    [Fact]
    public void TheViewerSourceDecidesWhenItIsGiven_NotTheRecordedClientAddress()
    {
        // recorded address says local (e.g. a forwarded-for value), the connection itself came from outside
        LLLoginResponse spoofed = Response(LanViewer, Selector(), viewerSource: IPAddress.Parse(RemoteViewer));
        // recorded address says remote (a proxy's address), the connection is from the local network
        LLLoginResponse proxied = Response(RemoteViewer, Selector(), viewerSource: IPAddress.Parse(LanViewer));

        Assert.Equal("198.51.100.7", Xml(spoofed));
        Assert.Equal("192.168.1.10", Xml(proxied));
    }

    [Fact]
    public void ADestinationOnAnotherGrid_IsNeverGivenThisGridsPublicAddress()
    {
        GridRegion foreign = Region();
        foreign.ExternalHostName = "192.0.2.80";

        LLLoginResponse r = Response(RemoteViewer, Selector(), destinationIsLocal: false, destination: foreign);

        Assert.Equal("192.0.2.80", Xml(r));
        Assert.Equal("192.0.2.80", Llsd(r));
    }

    // ---- a region that registered its own local address (a region on another machine) ----

    private static GridRegion RegionRegistered()
    {
        GridRegion region = Region();
        region.ViewerLocalAddress = "192.0.2.77";
        return region;
    }

    [Fact]
    public void LanViewer_IsGivenTheAddressTheRegionRegistered_InBothForms()
    {
        LLLoginResponse r = Response(LanViewer, Selector(), destination: RegionRegistered());

        Assert.Equal("192.0.2.77", Xml(r));
        Assert.Equal("192.0.2.77", Llsd(r));
        Assert.Equal(RegionPort, XmlPort(r));
    }

    [Fact]
    public void RemoteViewer_StillGetsThePublicAddress_WhenTheRegionRegisteredALocalOne()
    {
        LLLoginResponse r = Response(RemoteViewer, Selector(), destination: RegionRegistered());

        Assert.Equal("198.51.100.7", Xml(r));
    }

    [Fact]
    public void WithNoKeys_ARegisteredLocalAddressChangesNothing()
    {
        ViewerAddressSelector emptyList = ViewerAddressSelector.Create(ViewerAddressConfig.Create(string.Empty, 30, new CapturingLogger()), new CapturingLogger());
        foreach (ViewerAddressSelector? selector in new[] { null, ViewerAddressSelector.Disabled, emptyList })
        {
            Assert.Equal("192.168.1.10", Xml(Response(LanViewer, selector, destination: RegionRegistered())));
            Assert.Equal("192.168.1.10", Llsd(Response(RemoteViewer, selector, destination: RegionRegistered())));
        }
    }

    [Fact]
    public void ABareNetworkAlone_GivesItsViewersTheRegisteredAddress_AndOthersTheRegionsOwn()
    {
        ViewerAddressSelector bareOnly = Selector(publicAddress: string.Empty);

        Assert.Equal("192.0.2.77", Xml(Response(LanViewer, bareOnly, destination: RegionRegistered())));
        Assert.Equal("192.168.1.10", Xml(Response(RemoteViewer, bareOnly, destination: RegionRegistered())));
    }

    // ---- the handler: what is recorded, and what the viewer is classified by ----
    // [Confluence] Confluence's handler has no [LoginService] HasProxy: the address it is handed has already been
    // resolved by the HTTP layer through [Network] XFFTrustedProxies, so the viewer is always classified by it. The
    // forwarded-for value in the XML-RPC request is still what is recorded for the login, as before.

    private sealed class CapturingLoginService : ILoginService
    {
        public IPEndPoint? Recorded;
        public IPAddress? ViewerSource;
        public int Calls;

        public LoginResponse Login(string firstName, string lastName, string passwd, string startLocation, UUID scopeID,
            string clientVersion, string channel, string mac, string id0, IPEndPoint clientIP, bool agree_to_tos = false)
            => Login(firstName, lastName, passwd, startLocation, scopeID, clientVersion, channel, mac, id0, clientIP, agree_to_tos, clientIP.Address);

        public LoginResponse Login(string firstName, string lastName, string passwd, string startLocation, UUID scopeID,
            string clientVersion, string channel, string mac, string id0, IPEndPoint clientIP, bool agree_to_tos, IPAddress viewerSource)
        {
            Calls++;
            Recorded = clientIP;
            ViewerSource = viewerSource;
            return LLFailedLoginResponse.GridProblem ?? new LLFailedLoginResponse("key", "stub", "false");
        }

        public Hashtable SetLevel(string firstName, string lastName, string passwd, int level, IPEndPoint clientIP)
            => new();
    }

    private static XmlRpcRequest LoginRequest(string? forwardedFor)
    {
        XmlRpcRequest request = new();
        Hashtable data = new() { ["first"] = "Test", ["last"] = "User", ["passwd"] = "x" };
        request.Params.Add(data);
        request.Params.Add(null);
        request.Params.Add("/");
        request.Params.Add(forwardedFor);
        return request;
    }

    private static CapturingLoginService Run(string? forwardedFor)
    {
        CapturingLoginService service = new();
        LLLoginHandlers handlers = new(service);
        handlers.HandleXMLRPCLogin(LoginRequest(forwardedFor), new IPEndPoint(IPAddress.Parse(RemoteViewer), 40000));
        Assert.Equal(1, service.Calls);
        return service;
    }

    [Fact]
    public void WithoutAForwardedForHeader_BothAddressesAreTheConnection()
    {
        CapturingLoginService s = Run(null);

        Assert.Equal(IPAddress.Parse(RemoteViewer), s.Recorded!.Address);
        Assert.Equal(IPAddress.Parse(RemoteViewer), s.ViewerSource);
    }

    [Fact]
    public void ForwardedFor_IsStillRecorded_ButTheViewerIsClassifiedByTheConnection()
    {
        CapturingLoginService s = Run("192.168.1.5");

        Assert.Equal(IPAddress.Parse("192.168.1.5"), s.Recorded!.Address);   // recorded as before
        Assert.Equal(IPAddress.Parse(RemoteViewer), s.ViewerSource);         // never the header
    }

    [Fact]
    public void AnUnparsableForwardedFor_IsIgnored_AsBefore()
    {
        CapturingLoginService s = Run("not-an-address");

        Assert.Equal(IPAddress.Parse(RemoteViewer), s.Recorded!.Address);
        Assert.Equal(IPAddress.Parse(RemoteViewer), s.ViewerSource);
    }
}
