/*
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

// [Confluence] Tranquillity #351's test of the new ChatterBoxSessionUpdate event, from its EventQueueTests (xUnit there;
// Confluence's copy of that class is upstream's NUnit file, which is not built), with the fixture it needs.

using System;
using System.Collections;
using System.Net;
using System.Text;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using OpenSim.Framework.Servers;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Region.ClientStack.Linden;
using OpenSim.Region.CoreModules.Framework;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Tests.Common;
using Xunit;

namespace InWorldz.Phlox.Tests;

// Runs with the viewer-address wiring tests, never beside them: both need MainServer, which is process-wide.
[Collection("viewer-address-wiring")]
public class EventQueueSessionUpdateTests
{
    private readonly TestScene m_scene;
    private readonly EventQueueGetModule m_eqgMod;

    public EventQueueSessionUpdateTests()
    {
        if (MainServer.Instance is null)
        {
            var httpServer = new BaseHttpServer(99999);
            MainServer.AddHttpServer(httpServer);
            MainServer.Instance = httpServer;
        }

        IConfigSource config = new IniConfigSource();
        config.AddConfig("Startup");

        CapabilitiesModule capsModule = new CapabilitiesModule();
        m_eqgMod = new EventQueueGetModule();

        m_scene = new SceneHelpers().SetupScene();
        SceneHelpers.SetupSceneModules(m_scene, config, capsModule, m_eqgMod);
    }

    /// <summary>
    /// A moderated-mode change reaches the agent as ChatterBoxSessionUpdate, with the mode under
    /// info.moderated_mode.voice and the session id beside it, which is where the viewer reads them.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TestChatterBoxSessionUpdateCarriesTheModeratedMode(bool moderated)
    {
        ScenePresence sp = SceneHelpers.AddScenePresence(m_scene, TestHelpers.ParseTail(0x1));
        UUID sessionId = TestHelpers.ParseTail(0x77);

        m_eqgMod.ChatterBoxSessionUpdate(sessionId, sp.UUID, moderated);

        OSDMap update = null;
        for (int attempt = 0; attempt < 3 && update is null; attempt++)
        {
            Hashtable eventsResponse = m_eqgMod.GetEvents(UUID.Zero, sp.UUID);
            if ((int)eventsResponse["int_response_code"] != (int)HttpStatusCode.OK
                || eventsResponse["bin_response_data"] is null)
                continue;

            string data = Encoding.UTF8.GetString((byte[])eventsResponse["bin_response_data"]);
            OSDArray eventsOsd = (OSDArray)((OSDMap)OSDParser.DeserializeLLSDXml(data))["events"];
            foreach (OSD osd in eventsOsd)
            {
                OSDMap eventOsd = (OSDMap)osd;
                if (eventOsd["message"] == "ChatterBoxSessionUpdate")
                    update = (OSDMap)eventOsd["body"];
            }
        }

        Assert.NotNull(update);
        Assert.Equal(sessionId, update["session_id"].AsUUID());
        Assert.Equal(moderated, ((OSDMap)((OSDMap)update["info"])["moderated_mode"])["voice"].AsBoolean());
    }
}
