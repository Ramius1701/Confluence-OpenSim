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

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using OpenMetaverse.StructuredData;

using log4net;

namespace osWebRtcVoice
{
    // Encapsulization of a Session to the Janus server
    public class JanusAudioBridge : JanusPlugin
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly string LogHeader = "[JANUS AUDIO BRIDGE]";

        // [Confluence] Tranquillity #347: the stock AudioBridge plugin, used when [JanusWebRtcVoice] PluginName is unset.
        public const string DefaultPluginName = "janus.plugin.audiobridge";

        // Wrapper around the session connection to Janus-gateway. The Janus plugin name comes from the caller
        // ([JanusWebRtcVoice] PluginName) rather than being fixed, so a mixer with the same messages (for one,
        // janus.plugin.slvoice) can be chosen without a code change.
        public JanusAudioBridge(JanusSession pSession, string pPluginName = DefaultPluginName)
            : base(pSession, string.IsNullOrWhiteSpace(pPluginName) ? DefaultPluginName : pPluginName)
        {
            // m_log.DebugFormat("{0} JanusAudioBridge constructor (plugin={1})", LogHeader, pPluginName);
        }

        public override void Dispose()
        {
            if (IsConnected)
            {
                // Close the handle

            }
            base.Dispose();
        }

        public async Task<AudioBridgeResp> SendAudioBridgeMsg(PluginMsgReq pMsg)
        {
            try
            {
                JanusMessageResp ret = await SendPluginMsg(pMsg).ConfigureAwait(false);
                if(ret is not null)
                    return new AudioBridgeResp(ret);
                else
                    m_log.Error($"{LogHeader} AudioBridge SendPluginMsg returned null");

            }
            catch (Exception e)
            {
                m_log.Error($"{LogHeader} SendPluginMsg. Exception", e);
            }
            return null;
        }

        /// <summary>
        /// Create a room with the given criteria. This talks to Janus to create the room.
        /// If the room with this RoomId already exists, just return it.
        /// Janus could create and return the RoomId but this presumes that the Janus server
        /// is only being used for our voice service.
        /// </summary>
        /// <param name="pRoomId">integer room ID to create</param>
        /// <param name="pSpatial">boolean on whether room will be spatial or non-spatial</param>
        /// <param name="pRoomDesc">added as "description" to the created room</param>
        /// <returns></returns>
        public async Task<JanusRoom> CreateRoom(int pRoomId, bool pSpatial, string pRoomDesc, string credentials)
        {
            JanusRoom ret = null;
            try
            {
                JanusMessageResp resp = await SendPluginMsg(new AudioBridgeCreateRoomReq(pRoomId, pSpatial, pRoomDesc, credentials)).ConfigureAwait(false);
                AudioBridgeResp abResp = new(resp);

                m_log.Debug($"{LogHeader} CreateRoom. ReturnCode: '{abResp.AudioBridgeReturnCode}'");
                switch (abResp.AudioBridgeReturnCode)
                {
                    case "created":
                        ret = new JanusRoom(this, pRoomId);
                        RoomCreated?.Invoke(pRoomId);
                        break;
                    case "event":
                        if (abResp.AudioBridgeErrorCode == 486)
                        {
                            // Every select sends a create first ([Confluence] Tranquillity #348), so "already exists"
                            // is the normal answer on almost every provision - success, logged at debug.
                            m_log.Debug($"{LogHeader} CreateRoom. Room {pRoomId} already exists. Reusing.");
                            if (m_log.IsDebugEnabled)
                            {
                                m_log.Debug($"{LogHeader} CreateRoom. Reconnect details: {abResp}");
                            }
                            // if room already exists, just use it
                            ret = new JanusRoom(this, pRoomId);
                        }
                        else
                        {
                            m_log.Error($"{LogHeader} CreateRoom. XX Room creation failed.");
                            if (m_log.IsDebugEnabled)
                            {
                                m_log.Debug($"{LogHeader} CreateRoom. XX failure detail: {abResp}");
                            }
                        }
                        break;
                    default:
                        m_log.Error($"{LogHeader} CreateRoom. YY Room creation failed.");
                        if (m_log.IsDebugEnabled)
                        {
                            m_log.Debug($"{LogHeader} CreateRoom. YY failure detail: {abResp}");
                        }
                        break;
                }   
            }
            catch (Exception e)
            {
                m_log.Error($"{LogHeader} CreateRoom. Exception '{e.Message}'");
            }
            return ret;
        }

        // [Confluence] Tranquillity #348: called with the room number when a create is answered "created" (not for
        // "already exists"), so the empty-room reaper watches only rooms this service made.
        public Action<int> RoomCreated { get; set; }

        // Participant count of every room on the gateway, or null when the list could not be read.
        public async Task<IReadOnlyDictionary<int, int>> ListRoomParticipantCounts()
        {
            JanusMessageResp raw = await SendPluginMsg(new AudioBridgeListRoomsReq()).ConfigureAwait(false);
            AudioBridgeResp resp = raw is null ? null : new AudioBridgeResp(raw);
            if (resp?.PluginRespData is null || !resp.PluginRespData.TryGetValue("list", out OSD list) || list is not OSDArray rooms)
                return null;
            Dictionary<int, int> counts = new();
            foreach (OSD item in rooms)
            {
                if (item is OSDMap room)
                    counts[room["room"].AsInteger()] = room["num_participants"].AsInteger();
            }
            return counts;
        }

        // Destroys a room by number. True when it is gone afterwards: destroyed, or already absent (485).
        public async Task<bool> DestroyRoomById(int pRoomId)
        {
            JanusMessageResp raw = await SendPluginMsg(new AudioBridgeDestroyRoomReq(pRoomId, false)).ConfigureAwait(false);
            if (raw is null)
                return false;
            AudioBridgeResp resp = new(raw);
            if (resp.AudioBridgeReturnCode == "destroyed" || resp.AudioBridgeErrorCode == 485)
            {
                m_log.DebugFormat("{0} DestroyRoom. Room {1} removed", LogHeader, pRoomId);
                lock (_rooms)
                    _rooms.Remove(pRoomId);
                return true;
            }
            m_log.WarnFormat("{0} DestroyRoom. Room {1} was not removed: {2}", LogHeader, pRoomId, resp.ToString());
            return false;
        }

        public async Task<bool> DestroyRoom(JanusRoom janusRoom)
        {
            try
            {
                JanusMessageResp resp = await SendPluginMsg(new AudioBridgeDestroyRoomReq(janusRoom.RoomId)).ConfigureAwait(false);
                return true;
            }
            catch (Exception e)
            {
                m_log.Error($"{LogHeader} DestroyRoom. Exception ", e);
            }
            return false;
        }

        // Constant used to denote that this is a spatial audio room for the region (as opposed to parcels)
        public const int REGION_ROOM_ID = -999;
        private Dictionary<int, JanusRoom> _rooms = [];

        // Calculate a room number for the given parameters. The room number is a hash of the parameters.
        // The attempt is to deterministicly create a room number so all regions will generate the
        //     same room number across sessions and across the grid.
        // getHashCode() is not deterministic across sessions.
        public static int CalcRoomNumber(string regionID, string gridhash, string pChannelType, int pParcelLocalID, string pChannelID)
        {
            BHasherMdjb2 hasher = new();
            // If there is a channel specified it must be group 
            switch (pChannelType)
            {
                case "local":
                    // A "local" channel is unique to the region and parcel
                    hasher.Add(regionID);
                    hasher.Add(pChannelType);
                    hasher.Add(pParcelLocalID);
                    break;
                case "multiagent":
                    hasher.Add(gridhash);
                    hasher.Add(pChannelID);
                    hasher.Add(pChannelType);
                    break;
                default:
                    throw new Exception("Unknown channel type: " + pChannelType);
            }   
            BHash hashed = hasher.Finish();
            // The "Abs()" is because Janus room number must be a positive integer
            // And note that this is the BHash.GetHashCode() and not Object.getHashCode().
            int roomNumber = FoldHashToRoom(hashed.GetHashCode());
            return roomNumber;
        }

        // [Confluence] Tranquillity #348. Folds a 32-bit hash to a POSITIVE Janus room number. Math.Abs(int.MinValue)
        // throws OverflowException, and because the hash inputs are stable per region+parcel (or channel), one unlucky
        // combination would fail provisioning there for good. Only int.MinValue is redirected (to int.MaxValue); every
        // other value maps as before, so no existing room renumbers.
        public static int FoldHashToRoom(int hashCode)
            => hashCode == int.MinValue ? int.MaxValue : Math.Abs(hashCode);

        // [Confluence] Tranquillity #348. Runs a create attempt; if it comes back null (inconclusive - the room may
        // have been created by another process meanwhile), runs it once more, where "already exists" means reuse.
        public static async Task<JanusRoom> CreateWithRecheck(Func<Task<JanusRoom>> pAttempt)
        {
            JanusRoom ret = await pAttempt().ConfigureAwait(false);
            if (ret is null)
                ret = await pAttempt().ConfigureAwait(false);
            return ret;
        }

        // [Confluence] Tranquillity #348. Room existence is grid-wide and Janus is the only source of truth for it: the
        // mixer destroys empty rooms without telling the simulator, so a cached "it exists" goes stale and the JSEP join
        // then fails (error 485, which cannot be re-sent on the same handle). So every select asks the mixer to create
        // the room ("already exists" = success) before the join. Creates of one room number from this process are
        // serialized by a per-room gate, so concurrent provisions send one create at a time; the cross-process race is
        // absorbed by the 486. Idle gates are swept (at most once a minute) so the table does not grow forever.
        private static readonly ConcurrentDictionary<int, SemaphoreSlim> _roomCreateLocks = new();
        public static readonly TimeSpan RoomCreateLockIdle = TimeSpan.FromMinutes(10);
        private static readonly ConcurrentDictionary<int, long> _roomLastUseMs = new();
        private static long _lastRoomLockSweepMs = Environment.TickCount64;

        public static async Task<JanusRoom> SelectRoomCoalesced(int pRoomNumber, Func<Task<JanusRoom>> pCreate)
        {
            long now = Environment.TickCount64;
            MaybeSweepRoomCreateLocks(now);
            _roomLastUseMs[pRoomNumber] = now;
            SemaphoreSlim gate = _roomCreateLocks.GetOrAdd(pRoomNumber, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                return await pCreate().ConfigureAwait(false);
            }
            finally
            {
                _roomLastUseMs[pRoomNumber] = Environment.TickCount64;
                gate.Release();
            }
        }

        private static void MaybeSweepRoomCreateLocks(long pNowMs)
        {
            long last = Interlocked.Read(ref _lastRoomLockSweepMs);
            if (pNowMs - last < 60_000 || Interlocked.CompareExchange(ref _lastRoomLockSweepMs, pNowMs, last) != last)
                return;
            SweepRoomCreateLocks(pNowMs, RoomCreateLockIdle);
        }

        // Evicts every room number whose last select is older than pIdle and whose gate is not held right now.
        public static int SweepRoomCreateLocks(long pNowMs, TimeSpan pIdle)
        {
            int evicted = 0;
            foreach (KeyValuePair<int, long> kvp in _roomLastUseMs)
            {
                if (pNowMs - kvp.Value < (long)pIdle.TotalMilliseconds)
                    continue;
                if (_roomCreateLocks.TryGetValue(kvp.Key, out SemaphoreSlim held) && held.CurrentCount == 0)
                    continue;   // a select for this room is in progress
                _roomCreateLocks.TryRemove(kvp.Key, out _);
                _roomLastUseMs.TryRemove(kvp.Key, out _);
                evicted++;
            }
            return evicted;
        }

        // Room numbers currently holding a create gate (diagnostics and tests).
        public static int RoomCreateLockCount => _roomCreateLocks.Count;

        public async Task<JanusRoom> SelectRoom(string pRegionId, string pgridhash, string pChannelType, bool pSpatial, int pParcelLocalID, string pChannelID, string credentials)
        {
            int roomNumber = CalcRoomNumber(pRegionId, pgridhash, pChannelType, pParcelLocalID, pChannelID);

            // Should be unique for the given use and channel type
            m_log.Debug($"{LogHeader} SelectRoom: roomNumber={roomNumber}");

            string roomDesc;
            if(pChannelType == "local")
                roomDesc = $"{pRegionId}/{pChannelType}/{pParcelLocalID}";
            else
                roomDesc = $"{pgridhash}/{pChannelType}/{pChannelID}";

            // [Confluence] Tranquillity #348: always ask the mixer (see SelectRoomCoalesced). This used to return a
            // cached room without asking, and on a race destroyed the room it had just created - a room another
            // simulator could already be using.
            JanusRoom ret = await SelectRoomCoalesced(roomNumber,
                () => CreateWithRecheck(() => CreateRoom(roomNumber, pSpatial, roomDesc, credentials))).ConfigureAwait(false);

            if (ret is not null)
            {
                lock (_rooms)
                    _rooms[roomNumber] = ret;
            }
            return ret;
        }

        // Return the room with the given room ID or 'null' if no such room
        public bool GetRoom(int pRoomId, out JanusRoom room)
        {
            lock (_rooms)
            {
                return _rooms.TryGetValue(pRoomId, out room);
            }
        }

        public override void Handle_Event(JanusMessageResp pResp)
        {
            base.Handle_Event(pResp);
            AudioBridgeResp abResp = new AudioBridgeResp(pResp);
            if (abResp is not null && abResp.AudioBridgeReturnCode == "event")
            {
                // An audio bridge event!
                m_log.DebugFormat("{0} Handle_Event. {1}", LogHeader, abResp.ToString());
            }
        }

        public override void Handle_Message(JanusMessageResp pResp)
        {
            base.Handle_Message(pResp);
            AudioBridgeResp abResp = new AudioBridgeResp(pResp);
            if (abResp is not null && abResp.AudioBridgeReturnCode == "event")
            {
                // An audio bridge event!
                m_log.DebugFormat("{0} Handle_Event. {1}", LogHeader, abResp.ToString());
            }
        }
    }
}
