/* Copyright (c) 2026 Legion Builds
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

// [Confluence] From OpenSim-Tranquillity PR #348, converted from NUnit to xunit for the Phlox test project.

using Xunit;
/*
 * Rooms the service created are removed once they have stood empty for the grace period, on the stock
 * AudioBridge plugin, which never removes an emptied room itself.
 *
 * The R tests are the sweep rules, run against a clock the test controls. The C tests are the settings. The S tests run the real
 * service against the fake gateway with its room lifecycle model switched on: a room made by a viewer's join is
 * removed after the last leave; a room the service did not create, an occupied room, and a plugin that removes
 * empty rooms itself are left alone.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.StructuredData;

namespace osWebRtcVoice.Tests
{
    public class EmptyRoomReaperTests
    {
        // ---- the sweep rules ---------------------------------------------------------------------------

        private sealed class Rig
        {
            public long Now = 1_000;
            public readonly Dictionary<int, int> Counts = new();
            public bool ListFails;
            public bool DestroyFails;
            public readonly List<int> Destroyed = new();
            public EmptyRoomReaper Reaper;

            public Rig(int graceSeconds = 60)
            {
                Reaper = new EmptyRoomReaper(TimeSpan.FromSeconds(graceSeconds), () => Now);
            }

            public Task<int> Sweep() => Reaper.SweepAsync(
                () => Task.FromResult<IReadOnlyDictionary<int, int>>(ListFails ? null : new Dictionary<int, int>(Counts)),
                room =>
                {
                    if (DestroyFails)
                        return Task.FromResult(false);
                    Destroyed.Add(room);
                    Counts.Remove(room);
                    return Task.FromResult(true);
                });
        }

        [Fact]
        public async Task R1_ARoomEmptyForTheGracePeriod_IsRemoved_NotBefore()
        {
            var rig = new Rig(60);
            rig.Counts[7] = 0;
            rig.Reaper.NoteCreated(7);

            await rig.Sweep();                       // first sight of the empty room: the grace starts here
            rig.Now += 59_000;
            await rig.Sweep();
            Assert.Empty(rig.Destroyed);

            rig.Now += 1_000;
            int removed = await rig.Sweep();

            Assert.Equal(new[] { 7 }, rig.Destroyed);
            Assert.Equal(1, removed);
            Assert.False(rig.Reaper.IsTracked(7), "and it is forgotten");
        }

        [Fact]
        public async Task R2_ARoomWithSomeoneInIt_IsNeverRemoved_AndItsGraceRestartsWhenItEmpties()
        {
            var rig = new Rig(60);
            rig.Counts[7] = 0;
            rig.Reaper.NoteCreated(7);
            await rig.Sweep();
            rig.Now += 50_000;
            rig.Counts[7] = 1;                       // someone joins
            await rig.Sweep();
            rig.Now += 600_000;                      // and stays a long time
            await rig.Sweep();
            Assert.Empty(rig.Destroyed);

            rig.Counts[7] = 0;                       // they leave
            await rig.Sweep();
            rig.Now += 59_000;
            await rig.Sweep();
            Assert.Empty(rig.Destroyed);
            rig.Now += 1_000;
            await rig.Sweep();
            Assert.Equal(new[] { 7 }, rig.Destroyed);
        }

        [Fact]
        public async Task R3_ARoomTheServiceDidNotCreate_IsNeverRemoved()
        {
            var rig = new Rig(0);
            rig.Counts[1234] = 0;                    // an operator's room: empty on the gateway, never reported as created
            rig.Counts[8] = 0;
            rig.Reaper.NoteCreated(8);

            await rig.Sweep();
            rig.Now += 10_000;
            await rig.Sweep();

            Assert.Equal(new[] { 8 }, rig.Destroyed);
            Assert.True(rig.Counts.ContainsKey(1234), "the room nobody reported creating is still there");
        }

        [Fact]
        public async Task R4_AListThatCannotBeRead_ChangesNothing()
        {
            var rig = new Rig(0);
            rig.Counts[7] = 0;
            rig.Reaper.NoteCreated(7);
            await rig.Sweep();                       // the room is seen empty
            rig.ListFails = true;
            rig.Now += 10_000;

            int removed = await rig.Sweep();

            Assert.Equal(0, removed);
            Assert.Empty(rig.Destroyed);
            Assert.True(rig.Reaper.IsTracked(7), "still watched");
        }

        [Fact]
        public async Task R5_AFailedDestroy_IsTriedAgainOnTheNextSweep()
        {
            var rig = new Rig(0);
            rig.Counts[7] = 0;
            rig.Reaper.NoteCreated(7);
            await rig.Sweep();
            rig.Now += 1_000;
            rig.DestroyFails = true;
            Assert.Equal(0, await rig.Sweep());
            Assert.True(rig.Reaper.IsTracked(7));

            rig.DestroyFails = false;
            Assert.Equal(1, await rig.Sweep());
            Assert.Equal(new[] { 7 }, rig.Destroyed);
        }

        [Fact]
        public async Task R6_ARoomTheGatewayNoLongerHas_IsForgotten()
        {
            var rig = new Rig(60);
            rig.Reaper.NoteCreated(7);               // the gateway restarted: it lists nothing

            await rig.Sweep();

            Assert.False(rig.Reaper.IsTracked(7));
            Assert.Empty(rig.Destroyed);
        }

        // ---- the settings ------------------------------------------------------------------------------

        private static IConfig Janus(params (string Key, string Value)[] pairs)
        {
            var src = new IniConfigSource();
            IConfig cfg = src.AddConfig("JanusWebRtcVoice");
            foreach ((string k, string v) in pairs)
                cfg.Set(k, v);
            return cfg;
        }

        [Fact]
        public void C1_TheDefault_OnTheStockPlugin_IsOn()
        {
            Assert.True(EmptyRoomReaper.DefaultEnabled);
            Assert.Equal(60, EmptyRoomReaper.DefaultGraceSeconds);
            Assert.NotNull(WebRtcJanusService.CreateRoomReaper(Janus(), JanusAudioBridge.DefaultPluginName));
        }

        [Fact]
        public void C2_TheKeyTurnsItOff()
        {
            Assert.Null(WebRtcJanusService.CreateRoomReaper(Janus(("RemoveEmptyRooms", "false")),
                JanusAudioBridge.DefaultPluginName));
        }

        [Fact]
        public void C3_TheSlvoiceMixerRemovesItsOwnRooms_SoNoReaperIsMade()
        {
            Assert.Null(WebRtcJanusService.CreateRoomReaper(Janus(), "janus.plugin.slvoice"));
            Assert.Null(WebRtcJanusService.CreateRoomReaper(Janus(("RemoveEmptyRooms", "true")), "janus.plugin.slvoice"));
        }

        [Fact]
        public async Task C4_TheGraceSecondsKeyIsRead()
        {
            long now = 0;
            EmptyRoomReaper reaper = WebRtcJanusService.CreateRoomReaper(Janus(("EmptyRoomGraceSeconds", "5")),
                JanusAudioBridge.DefaultPluginName, () => now);
            var destroyed = new List<int>();
            Func<Task<IReadOnlyDictionary<int, int>>> list = () => Task.FromResult<IReadOnlyDictionary<int, int>>(new Dictionary<int, int> { [3] = 0 });
            Func<int, Task<bool>> destroy = r => { destroyed.Add(r); return Task.FromResult(true); };
            reaper.NoteCreated(3);
            await reaper.SweepAsync(list, destroy);
            now = 4_999;
            await reaper.SweepAsync(list, destroy);
            Assert.Empty(destroyed);
            now = 5_000;
            await reaper.SweepAsync(list, destroy);
            Assert.Equal(new[] { 3 }, destroyed);
        }

        [Fact]
        public async Task C5_ANegativeGraceFallsBackToTheDefault()
        {
            long now = 0;
            EmptyRoomReaper reaper = WebRtcJanusService.CreateRoomReaper(Janus(("EmptyRoomGraceSeconds", "-5")),
                JanusAudioBridge.DefaultPluginName, () => now);
            var destroyed = new List<int>();
            Func<Task<IReadOnlyDictionary<int, int>>> list = () => Task.FromResult<IReadOnlyDictionary<int, int>>(new Dictionary<int, int> { [3] = 0 });
            Func<int, Task<bool>> destroy = r => { destroyed.Add(r); return Task.FromResult(true); };
            reaper.NoteCreated(3);
            await reaper.SweepAsync(list, destroy);
            now = 59_999;
            await reaper.SweepAsync(list, destroy);
            Assert.Empty(destroyed);
            now = 60_000;
            await reaper.SweepAsync(list, destroy);
            Assert.Equal(new[] { 3 }, destroyed);
        }

        // The service-level tests (S1-S5, against Tranquillity's fake Janus gateway) are not taken: Confluence's
        // service has no HTTP test seam.
    }
}
