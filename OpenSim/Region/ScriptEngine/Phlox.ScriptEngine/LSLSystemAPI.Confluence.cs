/*
 * [Confluence] Functions YEngine has in Confluence and Phlox lacked (2026-10-14): Confluence's own OSSL additions
 * (partnership, avatar freeze and fly/walk flags, viewer name, link inventory giving, NPC tags/follow/say-to/look-at)
 * and the upstream ones Tranquillity's Phlox did not carry, plus eight ll* functions. Each is a port of the YEngine
 * implementation named in its comment (Shared/Api/Implementation/OSSL_Api.cs or LSL_Api.cs), with the same OSSL key
 * and threat level through OsslGate, so a script behaves the same on either engine. Kept in this file so Tranquillity's
 * LSLSystemAPI.cs changes only by the "partial" keyword.
 *
 * NPCs: Phlox's osNpc* functions drive NPCs through the bot manager (IBotManager), so the tag and follow functions
 * use its tags and follow, with the caller's owner as the permission, as the bot* functions do.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.Framework.Scenes.Scripting;
using OpenSim.Services.Interfaces;
using InWorldz.Phlox.Types;
using Microsoft.Extensions.Logging;
using static Phlox.ScriptEngine.SlConst;
using ThreatLevel = OpenSim.Region.ScriptEngine.Shared.Api.Interfaces.ThreatLevel;

namespace Phlox.ScriptEngine
{
    public partial class LSLSystemAPI
    {
        // The script constants these use that SlConst does not carry (the values of Compiler/DefaultConstants.cs).
        private const int OS_ATTACH_MSG_ALL = -65535, OS_ATTACH_MSG_INVERT_POINTS = 1,
            OS_ATTACH_MSG_OBJECT_CREATOR = 2, OS_ATTACH_MSG_SCRIPT_CREATOR = 4;
        private const int BOT_START_FOLLOWING_DISTANCE = 6, BOT_STOP_FOLLOWING_DISTANCE = 7;
        private const int NPCLOOKAT_CLEAR = 10;
        private const string NULL_KEY = "00000000-0000-0000-0000-000000000000";

        // ── Avatar movement flags (OSSL_Api.cs osAvatarFreeze .. osSetAvatarWalkDisabled) ─────────────────────────

        private ScenePresence OsslPresence(string avatar)
            => UUID.TryParse(avatar, out UUID id) ? World?.GetScenePresence(id) : null;

        public void osAvatarFreeze(string avatar)
        {
            OsslCheck(ThreatLevel.VeryHigh, "osAvatarFreeze");
            if (OsslPresence(avatar) is ScenePresence sp) sp.AllowMovement = false;
        }

        public void osAvatarThaw(string avatar)
        {
            OsslCheck(ThreatLevel.VeryHigh, "osAvatarThaw");
            if (OsslPresence(avatar) is ScenePresence sp) sp.AllowMovement = true;
        }

        public int osGetAvatarFlyDisabled(string avatar)
        {
            OsslCheck(ThreatLevel.None, "osGetAvatarFlyDisabled");
            return OsslPresence(avatar)?.FlyDisabled == true ? 1 : 0;
        }

        public int osGetAvatarForceFly(string avatar)
        {
            OsslCheck(ThreatLevel.None, "osGetAvatarForceFly");
            return OsslPresence(avatar)?.ForceFly == true ? 1 : 0;
        }

        public int osGetAvatarWalkDisabled(string avatar)
        {
            OsslCheck(ThreatLevel.None, "osGetAvatarWalkDisabled");
            return OsslPresence(avatar)?.WalkDisabled == true ? 1 : 0;
        }

        public void osSetAvatarFlyDisabled(string avatar, int disabled)
        {
            OsslCheck(ThreatLevel.VeryHigh, "osSetAvatarFlyDisabled");
            if (OsslPresence(avatar) is ScenePresence sp) sp.FlyDisabled = disabled != 0;
        }

        public void osSetAvatarForceFly(string avatar, int force)
        {
            OsslCheck(ThreatLevel.VeryHigh, "osSetAvatarForceFly");
            if (OsslPresence(avatar) is ScenePresence sp) sp.ForceFly = force != 0;
        }

        public void osSetAvatarWalkDisabled(string avatar, int disabled)
        {
            OsslCheck(ThreatLevel.VeryHigh, "osSetAvatarWalkDisabled");
            if (OsslPresence(avatar) is ScenePresence sp) sp.WalkDisabled = disabled != 0;
        }

        /// <summary>OSSL_Api.cs osGetAgentViewer - Moderate: the viewer name and version the agent logged in with.</summary>
        public string osGetAgentViewer(string agent)
        {
            OsslCheck(ThreatLevel.Moderate, "osGetAgentViewer");
            if (World == null || !UUID.TryParse(agent, out UUID avatarID)) return string.Empty;
            AgentCircuitData circuit = World.AuthenticateHandler.GetAgentCircuitData(avatarID);
            return circuit != null ? Util.GetViewerName(circuit) : string.Empty;
        }

        /// <summary>
        /// OSSL_Api.cs osPerm2Use: 1 when this script may call the named OSSL function, without calling it. Upstream
        /// tests one level above Severe, so only an explicit Allow_ list (or "true") admits a function.
        /// </summary>
        public int osPerm2Use(string function)
        {
            if (!m_ScriptEngine.Ossl.Enabled) return 0;
            return string.IsNullOrEmpty(m_ScriptEngine.Ossl.Test(ThreatLevel.Severe + 1, function, World, m_host, OsslItem)) ? 1 : 0;
        }

        // ── Partnership (OSSL_Api.cs PostPartnerAction and the five functions over it) ──────────────────────────────

        /// <summary>
        /// OSSL_Api.cs PostPartnerAction: the WebUI's partner-action endpoint behind [PartnerService] SharedSecret. The
        /// keys come from real touches; each must name an avatar present here. Nothing is final until the target
        /// accepts. Runs on the service lane (it posts to the grid's web service).
        /// </summary>
        private string PostPartnerAction(string action, UUID callerId, UUID targetId)
        {
            var config = m_ScriptEngine.ConfigSource;
            string secret = config?.Configs["PartnerService"]?.GetString("SharedSecret", string.Empty);
            if (string.IsNullOrEmpty(secret))
                return "Partnering is not configured on this grid.";

            string gridInfoUri = config.Configs["GridInfo"]?.GetString("GridInfoURI", string.Empty);
            if (string.IsNullOrEmpty(gridInfoUri))
                return "Grid info service is not configured.";

            OSDMap data = new OSDMap
            {
                ["secret"] = secret,
                ["action"] = action,
                ["callerId"] = callerId.ToString()
            };
            if (targetId != UUID.Zero)
                data["targetId"] = targetId.ToString();

            try
            {
                OSDMap result = WebUtil.PostToService(gridInfoUri.TrimEnd('/') + "/internal/partner-action", data, 8000, false);
                string raw = result["_RawResult"].AsString();
                return string.IsNullOrEmpty(raw) ? "No response from the partnering service." : raw;
            }
            catch (Exception e)
            {
                m_log.LogWarning(e, "[Phlox] Could not reach the partnering service");
                return "Could not reach the partnering service.";
            }
        }

        private bool TryResolvePresentAvatar(string key, out UUID avatarId)
        {
            if (!UUID.TryParse(key, out avatarId) || World == null) return false;
            ScenePresence sp = World.GetScenePresence(avatarId);
            return sp != null && !sp.IsChildAgent && !sp.IsDeleted && !sp.IsNPC;
        }

        public string osProposePartnership(string proposer, string target)
        {
            OsslCheck(ThreatLevel.Moderate, "osProposePartnership");
            if (!TryResolvePresentAvatar(proposer, out UUID proposerId)) return "Proposer is not a real, present avatar.";
            if (!TryResolvePresentAvatar(target, out UUID targetId)) return "Target is not a real, present avatar.";
            return PostPartnerAction("propose", proposerId, targetId);
        }

        public string osRespondToPartnershipProposal(string responder, int accept)
        {
            OsslCheck(ThreatLevel.Moderate, "osRespondToPartnershipProposal");
            if (!TryResolvePresentAvatar(responder, out UUID responderId)) return "Responder is not a real, present avatar.";
            return PostPartnerAction(accept != 0 ? "accept" : "decline", responderId, UUID.Zero);
        }

        public string osCancelPartnershipProposal(string caller)
        {
            OsslCheck(ThreatLevel.Low, "osCancelPartnershipProposal");
            if (!TryResolvePresentAvatar(caller, out UUID callerId)) return "Caller is not a real, present avatar.";
            return PostPartnerAction("cancel", callerId, UUID.Zero);
        }

        public string osEndPartnership(string caller)
        {
            OsslCheck(ThreatLevel.Low, "osEndPartnership");
            if (!TryResolvePresentAvatar(caller, out UUID callerId)) return "Caller is not a real, present avatar.";
            return PostPartnerAction("breakup", callerId, UUID.Zero);
        }

        public string osGetPartnerId(string avatar)
        {
            OsslCheck(ThreatLevel.None, "osGetPartnerId");
            if (!UUID.TryParse(avatar, out UUID avatarId)) return UUID.Zero.ToString();
            string result = PostPartnerAction("get", avatarId, UUID.Zero);
            return UUID.TryParse(result, out UUID partnerId) ? partnerId.ToString() : UUID.Zero.ToString();
        }

        // ── Inventory (OSSL_Api.cs osGiveLinkInventory, osGiveLinkInventoryList, osMakeScript) ──────────────────────

        /// <summary>
        /// OSSL_Api.cs osGiveLinkInventory - ungated upstream: llGiveInventory from one prim of the link set (LINK_SET
        /// and the other multi-prim selectors give nothing). An avatar here, elsewhere or offline; 3 s to an avatar.
        /// </summary>
        public void osGiveLinkInventory(int linkNumber, string destination, string inventory)
        {
            if (World == null || !UUID.TryParse(destination, out UUID destId) || destId == UUID.Zero) return;
            SceneObjectPart part = OsslSingleLinkPart(linkNumber);
            TaskInventoryItem item = part?.Inventory.GetInventoryItem(inventory);
            if (item == null) return;
            if (GiveRefusedByMute(destId, "inventory")) return;

            bool toPrim = World.GetSceneObjectPart(destId) != null;
            if (GiveTaskItem(part, item, destId, out string failure) != IW_DELIVER_OK)
            {
                llSay(0, failure);
                return;
            }
            if (!toPrim) ScriptSleep(3000);
        }

        /// <summary>
        /// OSSL_Api.cs osGiveLinkInventoryList - ungated upstream: the copyable items of one prim (transferable too when
        /// the avatar is not the owner; for a prim, those the permissions module lets move) into a folder. The
        /// destination must be a prim or an avatar in this region. 100 ms on a refusal or to a prim, 3 s to an avatar.
        /// </summary>
        public void osGiveLinkInventoryList(int linkNumber, string destination, string folderName, LSLList inventory)
        {
            if (inventory.Length == 0 || World == null) return;
            SceneObjectPart part = OsslSingleLinkPart(linkNumber);
            if (part == null) return;
            if (!UUID.TryParse(destination, out UUID destID) || destID == UUID.Zero) return;

            bool isNotOwner = true;
            SceneObjectPart destSop = World.GetSceneObjectPart(destID);
            if (destSop == null)
            {
                ScenePresence sp = World.GetScenePresence(destID);
                if (sp == null)
                {
                    ShoutError("osGiveLinkInventoryList: Unable to give list, destination not found");
                    ScriptSleep(100);
                    return;
                }
                isNotOwner = sp.UUID != m_host.OwnerID;
            }
            if (GiveRefusedByMute(destID, "inventory list")) return;

            var itemList = new List<UUID>(inventory.Length);
            foreach (object entry in inventory.Data)
            {
                string raw = entry?.ToString() ?? string.Empty;
                TaskInventoryItem taskItem = UUID.TryParse(raw, out UUID itemID)
                    ? part.Inventory.GetInventoryItem(itemID) : part.Inventory.GetInventoryItem(raw);
                if (taskItem == null) continue;
                if ((taskItem.CurrentPermissions & (uint)OpenSim.Framework.PermissionMask.Copy) == 0) continue;
                if (destSop != null)
                {
                    if (!World.Permissions.CanDoObjectInvToObjectInv(taskItem, m_host, destSop)) continue;
                }
                else if (isNotOwner && (taskItem.CurrentPermissions & (uint)OpenSim.Framework.PermissionMask.Transfer) == 0)
                    continue;
                itemList.Add(taskItem.ItemID);
            }

            if (itemList.Count == 0)
            {
                ShoutError("osGiveLinkInventoryList: Unable to give list, no items found");
                ScriptSleep(100);
                return;
            }

            if (GiveTaskItems(part, destID, folderName, itemList, out _) != IW_DELIVER_OK)
            {
                ShoutError("osGiveLinkInventoryList: Unable to give list");
                ScriptSleep(100);
                return;
            }
            ScriptSleep(destSop != null ? 100 : 3000);
        }

        /// <summary>OSSL_Api.cs osMakeScript - Severe: a script from the list's lines, one per line, into this prim.</summary>
        public void osMakeScript(string scriptName, LSLList contents)
        {
            OsslCheck(ThreatLevel.Severe, "osMakeScript");
            if (m_host == null || World == null) return;

            var scriptData = new StringBuilder();
            for (int i = 0; i < contents.Length; i++)
                scriptData.Append(contents.Data[i]?.ToString() ?? string.Empty).Append('\n');

            AssetBase asset = new AssetBase(UUID.Random(), scriptName, (sbyte)AssetType.LSLText, m_host.OwnerID.ToString())
            {
                Description = "Script generated script",
                Data = Util.UTF8.GetBytes(scriptData.ToString())
            };
            World.AssetService.Store(asset);

            TaskInventoryItem taskItem = new TaskInventoryItem
            {
                ParentID = m_host.UUID,
                CreationDate = (uint)Util.UnixTimeSinceEpoch(),
                Name = scriptName,
                Description = "Script generated script",
                Type = (int)AssetType.LSLText,
                InvType = (int)InventoryType.LSL,
                OwnerID = m_host.OwnerID,
                CreatorID = m_host.OwnerID,
                BasePermissions = (uint)OpenSim.Framework.PermissionMask.All | (uint)OpenSim.Framework.PermissionMask.Export,
                CurrentPermissions = (uint)OpenSim.Framework.PermissionMask.All | (uint)OpenSim.Framework.PermissionMask.Export,
                EveryonePermissions = 0,
                NextPermissions = (uint)OpenSim.Framework.PermissionMask.All,
                GroupID = m_host.GroupID,
                GroupPermissions = 0,
                Flags = 0,
                PermsGranter = UUID.Zero,
                PermsMask = 0,
                AssetID = asset.FullID
            };
            taskItem.ResetIDs(m_host.UUID);

            m_host.Inventory.AddInventoryItem(taskItem, false);
            m_host.ParentGroup.InvalidateDeepEffectivePerms();
        }

        // ── Messages and returns (OSSL_Api.cs osMessageAttachments, osReturnObject, osReturnObjects) ──────────────

        /// <summary>
        /// OSSL_Api.cs osMessageAttachments - Moderate: a dataserver event (this prim's key, the message) in every prim
        /// with a dataserver handler in the avatar's attachments at the listed points (OS_ATTACH_MSG_ALL for all), or
        /// at every other point with OS_ATTACH_MSG_INVERT_POINTS; OS_ATTACH_MSG_OBJECT_CREATOR / _SCRIPT_CREATOR keep
        /// only attachments made by this object's / this script's creator.
        /// </summary>
        public void osMessageAttachments(string avatar, string message, LSLList attachmentPoints, int options)
        {
            OsslCheck(ThreatLevel.Moderate, "osMessageAttachments");
            if (World == null || m_host == null) return;
            if (!UUID.TryParse(avatar, out UUID targetUUID) || targetUUID == UUID.Zero) return;
            ScenePresence target = World.GetScenePresence(targetUUID);
            if (target == null || target.IsDeleted || target.IsInTransit || target.GetAttachmentsCount() == 0) return;

            bool invertPoints = (options & OS_ATTACH_MSG_INVERT_POINTS) != 0;
            bool msgAll = false;
            var aps = new List<int>(attachmentPoints.Length);
            foreach (object point in attachmentPoints.Data)
            {
                int ipoint;
                if (point is int i) ipoint = i;
                else if (point is float f) ipoint = (int)f;
                else if (!int.TryParse(point?.ToString(), out ipoint)) return;
                if (ipoint == OS_ATTACH_MSG_ALL)
                {
                    if (invertPoints) return;
                    msgAll = true;
                    break;
                }
                if (ipoint > 0) aps.Add(ipoint);
            }
            if (aps.Count == 0)
            {
                if (!invertPoints && !msgAll) return;
                msgAll = true;
                invertPoints = false;
            }

            List<SceneObjectGroup> attachments;
            if (msgAll || invertPoints)
                attachments = target.GetAttachments();
            else
            {
                attachments = new List<SceneObjectGroup>();
                foreach (int point in aps)
                    attachments.AddRange(target.GetAttachments((uint)point));
            }
            if (attachments.Count == 0) return;

            bool optionObjCreator = (options & OS_ATTACH_MSG_OBJECT_CREATOR) != 0;
            bool optionScriptCreator = (options & OS_ATTACH_MSG_SCRIPT_CREATOR) != 0;
            UUID hostCreatorID = m_host.CreatorID;
            UUID itemCreatorID = OsslItem?.CreatorID ?? UUID.Zero;

            foreach (SceneObjectGroup sog in attachments)
            {
                if (sog.IsDeleted || sog.inTransit || (sog.ScriptEvents & scriptEvents.dataserver) == 0) continue;
                if (invertPoints && aps.Contains((int)sog.AttachmentPoint)) continue;
                UUID creatorID = sog.RootPart.CreatorID;
                if (optionObjCreator && creatorID != hostCreatorID) continue;
                if (optionScriptCreator && creatorID != itemCreatorID) continue;
                foreach (SceneObjectPart p in sog.Parts)
                    if ((p.ScriptEvents & scriptEvents.dataserver) != 0)
                        OsslMessagePart(p, message);
            }
        }

        /// <summary>osMessageObject's delivery without its gate: every script in the prim, whatever engine runs it.</summary>
        private void OsslMessagePart(SceneObjectPart part, string message)
        {
            string sender = m_host.UUID.ToString(), text = message ?? string.Empty;
            m_ScriptEngine.PostObjectEvent(part.LocalId, new OpenSim.Region.ScriptEngine.Shared.EventParams("dataserver",
                new object[] { sender, text }, new OpenSim.Region.ScriptEngine.Shared.DetectParams[0]));
            OfferToOtherEngines("osMessageAttachments", () => ScriptItemsIn(new[] { part }), "dataserver", () => new object[] { sender, text });
        }

        /// <summary>OSSL_Api.cs osReturnObject - VeryHigh: that owner's objects on this parcel, if the script's owner may.</summary>
        public void osReturnObject(string userID)
        {
            OsslCheck(ThreatLevel.VeryHigh, "osReturnObject");
            if (!UUID.TryParse(userID, out UUID ownerID) || World == null) return;
            ILandObject land = World.LandChannel.GetLandObject(m_host.AbsolutePosition);
            if (land == null) return;
            if (!World.Permissions.CanEditParcelProperties(m_host.OwnerID, land, GroupPowers.LandOptions, false))
            {
                ShoutError("script owner does not have permission to return objects on this parcel");
                return;
            }
            land.ReturnLandObjects((uint)ObjectReturnType.List, new UUID[] { ownerID }, null, null);
        }

        /// <summary>OSSL_Api.cs osReturnObjects - VeryHigh: 0 the parcel owner's, 1 others', 2 the parcel group's.</summary>
        public void osReturnObjects(int type)
        {
            OsslCheck(ThreatLevel.VeryHigh, "osReturnObjects");
            if (World == null) return;
            ILandObject land = World.LandChannel.GetLandObject(m_host.AbsolutePosition);
            if (land == null) return;
            if (!World.Permissions.CanEditParcelProperties(m_host.OwnerID, land, GroupPowers.LandOptions, false))
            {
                ShoutError("script owner does not have permission to return objects on this parcel");
                return;
            }
            ObjectReturnType returnType = type switch
            {
                0 => ObjectReturnType.Owner,
                1 => ObjectReturnType.Other,
                2 => ObjectReturnType.Group,
                _ => ObjectReturnType.None,
            };
            if (returnType == ObjectReturnType.None) return;
            land.ReturnLandObjects((uint)returnType, null, null, null);
        }

        // ── NPCs, through the bot manager ───────────────────────────────────────────────────────────────────────────

        /// <summary>OSSL_Api.cs osNpcAddTag - High.</summary>
        public void osNpcAddTag(string npc, string tag)
        {
            OsslCheck(ThreatLevel.High, "osNpcAddTag");
            var mgr = NpcMgr(); if (mgr == null || !NpcKey(npc, out UUID id)) return;
            if (!mgr.CheckPermission(id, m_host.OwnerID)) return;
            mgr.AddTagToBot(id, tag, m_host.OwnerID);
        }

        /// <summary>OSSL_Api.cs osNpcGetNPCsWithTag - High: those the caller's owner may control.</summary>
        public LSLList osNpcGetNPCsWithTag(string tag)
        {
            OsslCheck(ThreatLevel.High, "osNpcGetNPCsWithTag");
            var result = new List<object>();
            var mgr = NpcMgr();
            if (mgr != null)
                foreach (UUID id in mgr.GetBotsWithTag(tag))
                    if (mgr.CheckPermission(id, m_host.OwnerID)) result.Add(id.ToString());
            return new LSLList(result);
        }

        /// <summary>OSSL_Api.cs osNpcDeleteNPCsWithTag - High: removes those the caller's owner may control; how many.</summary>
        public int osNpcDeleteNPCsWithTag(string tag)
        {
            OsslCheck(ThreatLevel.High, "osNpcDeleteNPCsWithTag");
            var mgr = NpcMgr(); if (mgr == null) return 0;
            int deleted = 0;
            foreach (UUID id in mgr.GetBotsWithTag(tag))
            {
                if (!mgr.CheckPermission(id, m_host.OwnerID)) continue;
                mgr.RemoveBot(id, m_host.OwnerID);
                if (!mgr.IsBot(id)) deleted++;
            }
            return deleted;
        }

        /// <summary>
        /// OSSL_Api.cs osNpcFollow - High: follow the avatar, starting when it is further than startDistance and
        /// stopping within stopDistance (the bot manager's BOT_START_ / BOT_STOP_FOLLOWING_DISTANCE).
        /// </summary>
        public void osNpcFollow(string npc, string target, float startDistance, float stopDistance)
        {
            OsslCheck(ThreatLevel.High, "osNpcFollow");
            var mgr = NpcMgr(); if (mgr == null || !NpcKey(npc, out UUID id)) return;
            if (!mgr.CheckPermission(id, m_host.OwnerID) || !UUID.TryParse(target, out UUID targetId)) return;
            var options = new Dictionary<int, object>
            {
                [BOT_START_FOLLOWING_DISTANCE] = startDistance,
                [BOT_STOP_FOLLOWING_DISTANCE] = stopDistance,
            };
            mgr.StartFollowingAvatar(id, targetId, options, m_host.OwnerID);
        }

        /// <summary>OSSL_Api.cs osNpcStopFollow - High.</summary>
        public void osNpcStopFollow(string npc)
        {
            OsslCheck(ThreatLevel.High, "osNpcStopFollow");
            var mgr = NpcMgr(); if (mgr == null || !NpcKey(npc, out UUID id)) return;
            if (!mgr.CheckPermission(id, m_host.OwnerID)) return;
            mgr.StopMovement(id, m_host.OwnerID);
        }

        /// <summary>
        /// OSSL_Api.cs osNpcSayTo - High: llRegionSayTo spoken by the NPC, from its position and name, to one avatar or
        /// object (Phlox's own listens and the core's, as llRegionSayTo here).
        /// </summary>
        public void osNpcSayTo(string npc, string target, int channel, string msg)
        {
            OsslCheck(ThreatLevel.High, "osNpcSayTo");
            var mgr = NpcMgr(); if (mgr == null || World == null || !NpcKey(npc, out UUID npcId)) return;
            if (!UUID.TryParse(target, out UUID targetId) || !mgr.CheckPermission(npcId, m_host.OwnerID)) return;
            ScenePresence npcSp = World.GetScenePresence(npcId);
            if (npcSp == null || npcSp.IsDeleted || !npcSp.IsNPC) return;

            Vector3 pos = npcSp.AbsolutePosition;
            string name = npcSp.Name;
            msg = CapChatBytes(msg);
            ScenePresence sp = World.GetScenePresence(targetId);
            if (channel == 0 && sp != null && !sp.IsChildAgent)
                sp.ControllingClient?.SendChatMessage(msg, (byte)ChatTypeEnum.Direct, pos, name, npcId, npcId,
                    (byte)ChatSourceType.Agent, (byte)ChatAudibleLevel.Fully);
            m_ScriptEngine.ListenManager?.DeliverChat(ChatTypeEnum.Direct, channel, name, npcId, msg, pos, targetId);
            if (!(channel == 0 && sp != null))
                m_ScriptEngine.SendToWorldComm(w => w.DeliverMessageTo(targetId, channel, pos, name, npcId, msg));
        }

        /// <summary>
        /// OSSL_Api.cs osNpcLookAt - ungated upstream: point the NPC's look-at at an object or avatar (or clear it) for
        /// every viewer in the region. 0 done (or nobody to see it), -1 bad type, -2/-3 not an NPC, -4/-5 bad target.
        /// </summary>
        public int osNpcLookAt(string npckey, int ltype, string objkey, Vector3 offset)
        {
            if (World == null || World.GetNumberOfClients() == 0) return 0;
            if (ltype < 0 || ltype > NPCLOOKAT_CLEAR) return -1;
            if (!UUID.TryParse(npckey, out UUID npc)) return -2;
            ScenePresence npcSP = World.GetScenePresence(npc);
            if (npcSP == null || !npcSP.IsNPC) return -3;
            if (!UUID.TryParse(objkey, out UUID obj)) return -4;
            if (obj != UUID.Zero && World.GetScenePresence(obj) == null && World.GetSceneObjectPart(obj) == null) return -5;

            byte[] data = new byte[57];
            npc.ToBytes(data, 0);
            obj.ToBytes(data, 16);
            new Vector3d(offset.X, offset.Y, offset.Z).ToBytes(data, 32);
            data[56] = (byte)ltype;

            var effect = new OpenMetaverse.Packets.ViewerEffectPacket.EffectBlock
            {
                AgentID = npc,
                Color = new byte[4],
                Duration = 0,
                ID = UUID.Random(),
                Type = 14,
                TypeData = data
            };
            var effectblock = new[] { effect };
            World.ForEachScenePresence(sp =>
            {
                if (!sp.IsNPC && !sp.IsDeleted) sp.ControllingClient.SendViewerEffect(effectblock);
            });
            return 0;
        }

        // ── Environment (OSSL_Api.cs osReplaceAgentEnvironment .. osResetEnvironment) ──────────────────────────────

        private IEnvironmentModule EnvModule => World?.RequestModuleInterface<IEnvironmentModule>();

        /// <summary>A settings asset by key or this prim's item name, read into a copy of <paramref name="env"/>; or why not.</summary>
        private int OsslLoadDayCycle(string daycycle, ViewerEnvironment env, int notFound, int bad)
        {
            UUID envID = ScriptUtils.GetAssetIdFromKeyOrItemName(m_host, daycycle);
            if (envID == UUID.Zero) return notFound;
            AssetBase asset = World.AssetService.Get(envID.ToString());
            if (asset == null || asset.Type != (byte)AssetType.Settings) return notFound;
            try
            {
                OSD oenv = OSDParser.Deserialize(asset.Data);
                return env.CycleFromOSD(oenv) ? 1 : bad;
            }
            catch
            {
                return bad;
            }
        }

        /// <summary>OSSL_Api.cs osReplaceAgentEnvironment - Moderate (-2 refused): one avatar's own environment.</summary>
        public int osReplaceAgentEnvironment(string agentkey, int transition, string daycycle)
        {
            if (!string.IsNullOrEmpty(m_ScriptEngine.Ossl.Test(ThreatLevel.Moderate, "osReplaceAgentEnvironment", World, m_host, OsslItem)))
                return -2;
            IEnvironmentModule env = EnvModule;
            if (env == null || !UUID.TryParse(agentkey, out UUID agentid)) return -4;
            ScenePresence sp = World.GetScenePresence(agentid);
            if (sp == null || sp.IsChildAgent || sp.IsNPC || sp.IsInTransit) return -4;

            if (string.IsNullOrEmpty(daycycle) || daycycle == NULL_KEY)
            {
                sp.Environment = null;
                env.WindlightRefreshForced(sp, transition);
                return 1;
            }

            ViewerEnvironment venv = env.GetRegionEnvironment().Clone();
            int rc = OsslLoadDayCycle(daycycle, venv, -3, -5);
            if (rc == -3) return -3;
            sp.Environment = rc == 1 ? venv : null;
            env.WindlightRefreshForced(sp, transition);
            return rc;
        }

        /// <summary>OSSL_Api.cs osReplaceParcelEnvironment - parcel environment rights (AllowEnvironment).</summary>
        public int osReplaceParcelEnvironment(int transition, string daycycle)
        {
            IEnvironmentModule env = EnvModule;
            if (env == null || !World.RegionInfo.EstateSettings.AllowEnvironmentOverride) return -1;
            Vector3 pos = m_host.GetWorldPosition();
            ILandObject parcel = World.LandChannel.GetLandObject(pos.X, pos.Y);
            if (parcel == null) return -2;
            if (!World.Permissions.CanEditParcelProperties(m_host.OwnerID, parcel, GroupPowers.AllowEnvironment, true)) return -3;

            ViewerEnvironment venv = parcel.LandData.Environment ?? env.GetRegionEnvironment().Clone();
            if (!string.IsNullOrEmpty(daycycle) && daycycle != NULL_KEY)
            {
                int rc = OsslLoadDayCycle(daycycle, venv, -4, -5);
                if (rc != 1) return rc;
                parcel.StoreEnvironment(venv);
                env.WindlightRefresh(transition, false);
            }
            return 1;
        }

        /// <summary>
        /// OSSL_Api.cs osReplaceRegionEnvironment - estate rights: the region's day cycle, day length (4-168 h), offset
        /// (-11.5..11.5 h) and sky altitudes. [Confluence: upstream compared the new offset with the day length and
        /// checked altitude2's range for altitude3; both fixed in YEngine too.]
        /// </summary>
        public int osReplaceRegionEnvironment(int transition, string daycycle, float daylen, float dayoffset,
            float altitude1, float altitude2, float altitude3)
        {
            IEnvironmentModule env = EnvModule;
            if (env == null || !World.Permissions.CanIssueEstateCommand(m_host.OwnerID, true)) return -3;
            ViewerEnvironment venv = env.GetRegionEnvironment().Clone();

            bool changed = false;
            if (!string.IsNullOrEmpty(daycycle) && daycycle != NULL_KEY)
            {
                int rc = OsslLoadDayCycle(daycycle, venv, -4, -5);
                if (rc != 1) return rc;
                changed = true;
            }
            if (daylen >= 4 && daylen <= 24 * 7)
            {
                int ll = venv.DayLength;
                venv.DayLength = (int)(daylen * 3600f);
                changed |= ll != venv.DayLength;
            }
            if (dayoffset >= -11.5 && dayoffset <= 11.5)
            {
                int lo = venv.DayOffset;
                if (dayoffset <= 0) dayoffset += 24;
                venv.DayOffset = (int)(dayoffset * 3600f);
                changed |= lo != venv.DayOffset;
            }
            bool needSort = false;
            if (altitude1 > 0 && altitude1 < 4000 && venv.Altitudes[0] != altitude1) { venv.Altitudes[0] = altitude1; needSort = true; }
            if (altitude2 > 0 && altitude2 < 4000 && venv.Altitudes[1] != altitude2) { venv.Altitudes[1] = altitude2; needSort = true; }
            if (altitude3 > 0 && altitude3 < 4000 && venv.Altitudes[2] != altitude3) { venv.Altitudes[2] = altitude3; needSort = true; }
            if (needSort)
            {
                venv.SortAltitudes();
                changed = true;
            }
            if (changed)
            {
                env.StoreOnRegion(venv);
                env.WindlightRefresh(transition);
            }
            return 1;
        }

        /// <summary>OSSL_Api.cs osResetEnvironment - parcelOrRegion &gt; 0 the parcel's (parcel rights), else the region's (estate rights).</summary>
        public int osResetEnvironment(int parcelOrRegion, int transition)
        {
            IEnvironmentModule env = EnvModule;
            if (env == null) return -3;
            if (parcelOrRegion > 0)
            {
                if (!World.RegionInfo.EstateSettings.AllowEnvironmentOverride) return -1;
                Vector3 pos = m_host.GetWorldPosition();
                ILandObject parcel = World.LandChannel.GetLandObject(pos.X, pos.Y);
                if (parcel == null) return -2;
                if (!World.Permissions.CanEditParcelProperties(m_host.OwnerID, parcel, GroupPowers.AllowEnvironment, true)) return -3;
                if (parcel.LandData.Environment == null) return 1;
                parcel.StoreEnvironment(null);
                env.WindlightRefresh(transition, false);
                return 1;
            }
            if (!World.Permissions.CanIssueEstateCommand(m_host.OwnerID, true)) return -3;
            env.StoreOnRegion(null);
            env.WindlightRefresh(transition);
            return 1;
        }

        // ── llReplaceEnvironment / llReplaceAgentEnvironment (LSL_Api.cs, YEngine's) ───────────────────────────────

        private const int ENV_NOT_EXPERIENCE = -1, ENV_NO_EXPERIENCE_PERMISSION = -2, ENV_NO_ENVIRONMENT = -3,
            ENV_INVALID_AGENT = -4, ENV_INVALID_RULE = -5, ENV_VALIDATION_FAIL = -6, ENV_NO_PERMISSIONS = -9;

        /// <summary>LSL_Api.cs TryLoadEnvironmentAsset: a settings asset by key or this prim's item name.</summary>
        private bool TryLoadEnvironmentAsset(string environment, out OSD envOSD)
        {
            envOSD = null;
            UUID envID = ScriptUtils.GetAssetIdFromKeyOrItemName(m_host, environment);
            if (envID == UUID.Zero) return false;
            AssetBase asset = World.AssetService.Get(envID.ToString());
            if (asset == null || asset.Type != (byte)AssetType.Settings) return false;
            try
            {
                envOSD = OSDParser.Deserialize(asset.Data);
                return envOSD != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>SL: a day length of 4 h to 7 days, an offset within a day; -1 leaves either alone.</summary>
        private static bool ValidateEnvironmentTime(int dayLength, int dayOffset)
        {
            if (dayLength >= 0 && (dayLength < 14400 || dayLength > 604800)) return false;
            return dayOffset < 0 || dayOffset <= 86400;
        }

        private static void ApplyEnvironmentTime(ViewerEnvironment env, int dayLength, int dayOffset)
        {
            if (dayLength >= 0) env.DayLength = dayLength;
            if (dayOffset >= 0) env.DayOffset = dayOffset;
        }

        /// <summary>track -1 the whole environment; 0 water, 1 ground sky, 2-4 the altitude skies (LSL_Api.cs).</summary>
        private int ApplyReplaceEnvironmentAsset(ViewerEnvironment env, int trackNo, string environment)
        {
            if (!TryLoadEnvironmentAsset(environment, out OSD envOSD)) return ENV_NO_ENVIRONMENT;
            bool ok = trackNo == -1 ? env.CycleFromOSD(envOSD) : env.ReplaceTrackFromAsset(trackNo, null, envOSD);
            return ok ? 1 : ENV_NO_ENVIRONMENT;
        }

        /// <summary>
        /// LSL_Api.cs llReplaceEnvironment: a negative x or y is the whole region (estate rights), else the parcel there
        /// (parcel environment rights, if the estate allows parcel environments). An empty environment with no track and
        /// no times resets it; tracks 0 and 1 cannot be removed, removing one sky track is not supported.
        /// </summary>
        private int ConfluenceReplaceEnvironment(Vector3 position, string environment, int trackNo, int dayLength, int dayOffset)
        {
            IEnvironmentModule env = EnvModule;
            if (env == null || m_host == null) return ENV_NO_ENVIRONMENT;
            if (trackNo < -1 || trackNo > 4) return ENV_INVALID_RULE;
            if (!ValidateEnvironmentTime(dayLength, dayOffset)) return ENV_VALIDATION_FAIL;

            bool isEmpty = string.IsNullOrEmpty(environment) || environment == NULL_KEY;
            if (isEmpty && trackNo != -1)
                return trackNo <= 1 ? ENV_NO_PERMISSIONS : ENV_INVALID_RULE;
            bool reset = isEmpty && trackNo == -1 && dayLength < 0 && dayOffset < 0;

            if (position.X < 0 || position.Y < 0)
            {
                if (!World.Permissions.CanIssueEstateCommand(m_host.OwnerID, true)) return ENV_NO_PERMISSIONS;
                if (reset)
                {
                    env.StoreOnRegion(null);
                    env.WindlightRefresh(0);
                    return 1;
                }
                ViewerEnvironment regionEnv = env.GetRegionEnvironment().Clone();
                if (!isEmpty)
                {
                    int rc = ApplyReplaceEnvironmentAsset(regionEnv, trackNo, environment);
                    if (rc != 1) return rc;
                }
                ApplyEnvironmentTime(regionEnv, dayLength, dayOffset);
                env.StoreOnRegion(regionEnv);
                env.WindlightRefresh(0);
                return 1;
            }

            if (!World.RegionInfo.EstateSettings.AllowEnvironmentOverride) return ENV_NO_PERMISSIONS;
            ILandObject parcel = World.LandChannel?.GetLandObject(position.X, position.Y);
            if (parcel == null) return ENV_NO_ENVIRONMENT;
            if (!World.Permissions.CanEditParcelProperties(m_host.OwnerID, parcel, GroupPowers.AllowEnvironment, true))
                return ENV_NO_PERMISSIONS;
            if (reset)
            {
                parcel.StoreEnvironment(null);
                env.WindlightRefresh(0, false);
                return 1;
            }
            ViewerEnvironment parcelEnv = (parcel.LandData.Environment ?? env.GetRegionEnvironment()).Clone();
            if (!isEmpty)
            {
                int rc = ApplyReplaceEnvironmentAsset(parcelEnv, trackNo, environment);
                if (rc != 1) return rc;
            }
            ApplyEnvironmentTime(parcelEnv, dayLength, dayOffset);
            parcel.StoreEnvironment(parcelEnv);
            env.WindlightRefresh(0, false);
            return 1;
        }

        /// <summary>
        /// LSL_Api.cs llReplaceAgentEnvironment: an Experience script, with the avatar's permission for that Experience,
        /// sends the avatar a whole environment (or clears its override) through the event queue.
        /// </summary>
        private int ConfluenceReplaceAgentEnvironment(string agentID, float transition, string environment)
        {
            if (m_host == null || World == null) return ENV_INVALID_AGENT;
            UUID experience = GetScriptExperienceId();
            if (experience == UUID.Zero) return ENV_NOT_EXPERIENCE;
            if (!UUID.TryParse(agentID, out UUID avatar) || avatar == UUID.Zero) return ENV_INVALID_AGENT;
            ScenePresence presence = World.GetScenePresence(avatar);
            if (presence == null || presence.IsChildAgent || presence.Animator == null) return ENV_INVALID_AGENT;
            if (World.ExperienceModule?.GetExperiencePermission(avatar, experience) != ExperiencePermission.Allowed)
                return ENV_NO_EXPERIENCE_PERMISSION;

            IEventQueue eq = World.RequestModuleInterface<IEventQueue>();
            if (eq == null) return ENV_NO_ENVIRONMENT;

            if (string.IsNullOrEmpty(environment) || environment == NULL_KEY)
            {
                eq.SendEnvironmentUpdate(experience, avatar, new ClearEnvironmentUpdate { TransitionTime = transition });
                return 1;
            }

            UUID envID = ScriptUtils.GetAssetIdFromKeyOrItemName(m_host, environment);
            if (envID == UUID.Zero) return ENV_NO_ENVIRONMENT;
            AssetBase asset = World.AssetService.Get(envID.ToString());
            if (asset == null || asset.Type != (byte)AssetType.Settings) return ENV_NO_ENVIRONMENT;
            eq.SendEnvironmentUpdate(experience, avatar, new FullEnvironmentUpdate { AssetID = asset.FullID, TransitionTime = transition });
            return 1;
        }

        // ── ll* functions YEngine has ──────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// OpenSim's alternative llCastRay (LSL_Api.cs llCastRayV3, a mesh-based ray cast). Phlox's llCastRay already
        /// answers in SL's form; this name runs it, so a script written for OpenSim's V3 works the same here.
        /// </summary>
        public LSLList llCastRayV3(Vector3 start, Vector3 end, LSLList options) => llCastRay(start, end, options);

        /// <summary>LSL_Api.cs: the permissions an Experience may grant without a dialog.</summary>
        private const int ExperienceAutoGrantPermissions =
            PERMISSION_TRIGGER_ANIMATION | PERMISSION_TAKE_CONTROLS | PERMISSION_CONTROL_CAMERA | PERMISSION_ATTACH |
            PERMISSION_TRACK_CAMERA | PERMISSION_TELEPORT;

        /// <summary>LSL_Api.cs IsScriptExperienceTrusted: the script's Experience is enabled and its owner is an admin of it, or it is an estate key Experience.</summary>
        private bool IsScriptExperienceTrusted()
        {
            IExperienceModule module = World?.ExperienceModule;
            UUID experience = GetScriptExperienceId();
            if (module == null || experience == UUID.Zero || !module.IsExperienceEnabled(experience)) return false;
            if (module.IsExperienceAdmin(m_host.OwnerID, experience)) return true;
            UUID[] keyExperiences = module.GetEstateKeyExperiences();
            return keyExperiences != null && Array.IndexOf(keyExperiences, experience) >= 0;
        }

        public int llIsExperienceTrusted() => IsScriptExperienceTrusted() ? 1 : 0;

        public int llGetExperiencePermissions() => IsScriptExperienceTrusted() ? ExperienceAutoGrantPermissions : 0;

        public int llExperienceCanAutoGrant(int permissions)
            => IsScriptExperienceTrusted() && (permissions & ~ExperienceAutoGrantPermissions) == 0 ? 1 : 0;

        /// <summary>LSL_Api.cs llGetExperienceKeyValueStoreStats: the store has no configured limits here, so those are -1.</summary>
        public LSLList llGetExperienceKeyValueStoreStats()
        {
            IExperienceModule module = World?.ExperienceModule;
            UUID experience = GetScriptExperienceId();
            bool enabled = module != null && experience != UUID.Zero;
            int keyCount = enabled ? module.GetKeyCount(experience) : 0;
            int usedBytes = enabled ? module.GetSize(experience) : 0;
            return new LSLList(new List<object>
            {
                "enabled", enabled ? 1 : 0,
                "trusted", IsScriptExperienceTrusted() ? 1 : 0,
                "max_keys", -1,
                "max_key_bytes", -1,
                "max_value_bytes", -1,
                "max_store_bytes", -1,
                "key_count", keyCount,
                "used_bytes", usedBytes,
                "free_bytes", -1,
            });
        }

        /// <summary>SL: deprecated, does nothing. LSL_Api.cs says so on DEBUG_CHANNEL.</summary>
        public void llRemoteDataSetRegion() => Deprecated("llRemoteDataSetRegion. Use llOpenRemoteDataChannel instead");

        /// <summary>SL: deprecated (use llRemoteLoadScriptPin), does nothing, and the script sleeps 3 s.</summary>
        public void llRemoteLoadScript(string target, string name, int running, int start_param)
        {
            Deprecated("llRemoteLoadScript. Use llRemoteLoadScriptPin instead");
            ScriptSleep(3000);
        }

        /// <summary>
        /// LSL_Api.cs llSetSculptAnim: the request is kept on the prim (DynAttrs OpenSim/LSLSculptAnim) and shown through the
        /// texture animation block, as OpenSim has no separate sculpt-map animation field.
        /// </summary>
        public void llSetSculptAnim(int mode, int sizex, int sizey, int start_frame, int end_frame, float rate, int texture_sync)
        {
            if (m_host?.ParentGroup == null || m_host.ParentGroup.IsDeleted) return;

            m_host.DynAttrs ??= new DAMap();
            lock (m_host.DynAttrs)
            {
                if (!m_host.DynAttrs.TryGetStore("OpenSim", "LSLSculptAnim", out OSDMap store) || store == null)
                    store = new OSDMap();
                store["mode"] = OSD.FromInteger(mode);
                store["size_x"] = OSD.FromInteger(sizex);
                store["size_y"] = OSD.FromInteger(sizey);
                store["start_frame"] = OSD.FromInteger(start_frame);
                store["end_frame"] = OSD.FromInteger(end_frame);
                store["rate"] = OSD.FromReal(rate);
                store["texture_sync"] = OSD.FromBoolean(texture_sync != 0);
                m_host.DynAttrs.SetStore("OpenSim", "LSLSculptAnim", store);
            }

            float length = Math.Max(0f, end_frame - start_frame + 1f);
            llSetTextureAnim(mode, ALL_SIDES, Math.Max(1, sizex), Math.Max(1, sizey), start_frame, length, rate);
            m_host.ParentGroup.HasGroupChanged = true;
        }
    }
}
