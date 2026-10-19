/*
 * [Confluence] The shims for the functions YEngine has in Confluence and Phlox lacked (2026-10-14), table indexes
 * 969-1008 (Types/Defaults.cs), in the pattern of the shims beside them: arguments popped last first; a call that may
 * leave the process (an asset, inventory, Experience or web service) on the service lane through Defer, a give
 * that sleeps through RunAsync, the rest in place.
 */

using System;
using System.Collections.Generic;
using OpenMetaverse;
using InWorldz.Phlox.Types;

namespace InWorldz.Phlox.Glue
{
    public partial class SyscallShim
    {
        static private void Shim_llCastRayV3(SyscallShim self)
        {
            LSLList p2 = ConvToLSLList(self._interpreter.ScriptState.Operands.Pop());
            Vector3 p1 = ConvToVector(self._interpreter.ScriptState.Operands.Pop());
            Vector3 p0 = ConvToVector(self._interpreter.ScriptState.Operands.Pop());
            LSLList ret = self._systemAPI.llCastRayV3(p0, p1, p2);
            self._interpreter.SafeOperandsPush(ConvToLSLType(ret));
        }

        static private void Shim_llExperienceCanAutoGrant(SyscallShim self)
        {
            int p0 = ConvToInt(self._interpreter.ScriptState.Operands.Pop());
            Defer(self, "llExperienceCanAutoGrant", new object[] { p0 }, () => ConvToLSLType(self._systemAPI.llExperienceCanAutoGrant(p0)), 0, true);
        }

        static private void Shim_llGetExperienceKeyValueStoreStats(SyscallShim self)
        {
            Defer(self, "llGetExperienceKeyValueStoreStats", new object[0], () => ConvToLSLType(self._systemAPI.llGetExperienceKeyValueStoreStats()), new LSLList(new List<object>()), true);
        }

        static private void Shim_llGetExperiencePermissions(SyscallShim self)
        {
            Defer(self, "llGetExperiencePermissions", new object[0], () => ConvToLSLType(self._systemAPI.llGetExperiencePermissions()), 0, true);
        }

        static private void Shim_llIsExperienceTrusted(SyscallShim self)
        {
            Defer(self, "llIsExperienceTrusted", new object[0], () => ConvToLSLType(self._systemAPI.llIsExperienceTrusted()), 0, true);
        }

        static private void Shim_llRemoteDataSetRegion(SyscallShim self)
        {
            self._systemAPI.llRemoteDataSetRegion();
        }

        static private void Shim_llRemoteLoadScript(SyscallShim self)
        {
            int p3 = ConvToInt(self._interpreter.ScriptState.Operands.Pop());
            int p2 = ConvToInt(self._interpreter.ScriptState.Operands.Pop());
            string p1 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            string p0 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            RunAsync(self, () => self._systemAPI.llRemoteLoadScript(p0, p1, p2, p3));
        }

        static private void Shim_llSetSculptAnim(SyscallShim self)
        {
            int p6 = ConvToInt(self._interpreter.ScriptState.Operands.Pop());
            float p5 = ConvToFloat(self._interpreter.ScriptState.Operands.Pop());
            int p4 = ConvToInt(self._interpreter.ScriptState.Operands.Pop());
            int p3 = ConvToInt(self._interpreter.ScriptState.Operands.Pop());
            int p2 = ConvToInt(self._interpreter.ScriptState.Operands.Pop());
            int p1 = ConvToInt(self._interpreter.ScriptState.Operands.Pop());
            int p0 = ConvToInt(self._interpreter.ScriptState.Operands.Pop());
            self._systemAPI.llSetSculptAnim(p0, p1, p2, p3, p4, p5, p6);
        }

        static private void Shim_osAvatarFreeze(SyscallShim self)
        {
            string p0 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            self._systemAPI.osAvatarFreeze(p0);
        }

        static private void Shim_osAvatarThaw(SyscallShim self)
        {
            string p0 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            self._systemAPI.osAvatarThaw(p0);
        }

        static private void Shim_osCancelPartnershipProposal(SyscallShim self)
        {
            string p0 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            Defer(self, "osCancelPartnershipProposal", new object[] { p0 }, () => ConvToLSLType(self._systemAPI.osCancelPartnershipProposal(p0)), "", true);
        }

        static private void Shim_osEndPartnership(SyscallShim self)
        {
            string p0 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            Defer(self, "osEndPartnership", new object[] { p0 }, () => ConvToLSLType(self._systemAPI.osEndPartnership(p0)), "", true);
        }

        static private void Shim_osGetAgentViewer(SyscallShim self)
        {
            string p0 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            string ret = self._systemAPI.osGetAgentViewer(p0);
            self._interpreter.SafeOperandsPush(ConvToLSLType(ret));
        }

        static private void Shim_osGetAvatarFlyDisabled(SyscallShim self)
        {
            string p0 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            int ret = self._systemAPI.osGetAvatarFlyDisabled(p0);
            self._interpreter.SafeOperandsPush(ConvToLSLType(ret));
        }

        static private void Shim_osGetAvatarForceFly(SyscallShim self)
        {
            string p0 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            int ret = self._systemAPI.osGetAvatarForceFly(p0);
            self._interpreter.SafeOperandsPush(ConvToLSLType(ret));
        }

        static private void Shim_osGetAvatarWalkDisabled(SyscallShim self)
        {
            string p0 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            int ret = self._systemAPI.osGetAvatarWalkDisabled(p0);
            self._interpreter.SafeOperandsPush(ConvToLSLType(ret));
        }

        static private void Shim_osGetPartnerId(SyscallShim self)
        {
            string p0 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            Defer(self, "osGetPartnerId", new object[] { p0 }, () => ConvToLSLType(self._systemAPI.osGetPartnerId(p0)), "00000000-0000-0000-0000-000000000000", true);
        }

        static private void Shim_osGiveLinkInventory(SyscallShim self)
        {
            string p2 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            string p1 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            int p0 = ConvToInt(self._interpreter.ScriptState.Operands.Pop());
            RunAsync(self, () => self._systemAPI.osGiveLinkInventory(p0, p1, p2));
        }

        static private void Shim_osGiveLinkInventoryList(SyscallShim self)
        {
            LSLList p3 = ConvToLSLList(self._interpreter.ScriptState.Operands.Pop());
            string p2 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            string p1 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            int p0 = ConvToInt(self._interpreter.ScriptState.Operands.Pop());
            RunAsync(self, () => self._systemAPI.osGiveLinkInventoryList(p0, p1, p2, p3));
        }

        static private void Shim_osMakeScript(SyscallShim self)
        {
            LSLList p1 = ConvToLSLList(self._interpreter.ScriptState.Operands.Pop());
            string p0 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            Defer(self, "osMakeScript", new object[] { p0, p1 }, () => { self._systemAPI.osMakeScript(p0, p1); return null; }, null, false);
        }

        static private void Shim_osMessageAttachments(SyscallShim self)
        {
            int p3 = ConvToInt(self._interpreter.ScriptState.Operands.Pop());
            LSLList p2 = ConvToLSLList(self._interpreter.ScriptState.Operands.Pop());
            string p1 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            string p0 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            self._systemAPI.osMessageAttachments(p0, p1, p2, p3);
        }

        static private void Shim_osNpcAddTag(SyscallShim self)
        {
            string p1 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            string p0 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            self._systemAPI.osNpcAddTag(p0, p1);
        }

        static private void Shim_osNpcDeleteNPCsWithTag(SyscallShim self)
        {
            string p0 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            int ret = self._systemAPI.osNpcDeleteNPCsWithTag(p0);
            self._interpreter.SafeOperandsPush(ConvToLSLType(ret));
        }

        static private void Shim_osNpcFollow(SyscallShim self)
        {
            float p3 = ConvToFloat(self._interpreter.ScriptState.Operands.Pop());
            float p2 = ConvToFloat(self._interpreter.ScriptState.Operands.Pop());
            string p1 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            string p0 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            self._systemAPI.osNpcFollow(p0, p1, p2, p3);
        }

        static private void Shim_osNpcGetNPCsWithTag(SyscallShim self)
        {
            string p0 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            LSLList ret = self._systemAPI.osNpcGetNPCsWithTag(p0);
            self._interpreter.SafeOperandsPush(ConvToLSLType(ret));
        }

        static private void Shim_osNpcLookAt(SyscallShim self)
        {
            Vector3 p3 = ConvToVector(self._interpreter.ScriptState.Operands.Pop());
            string p2 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            int p1 = ConvToInt(self._interpreter.ScriptState.Operands.Pop());
            string p0 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            int ret = self._systemAPI.osNpcLookAt(p0, p1, p2, p3);
            self._interpreter.SafeOperandsPush(ConvToLSLType(ret));
        }

        static private void Shim_osNpcSayTo(SyscallShim self)
        {
            string p3 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            int p2 = ConvToInt(self._interpreter.ScriptState.Operands.Pop());
            string p1 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            string p0 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            self._systemAPI.osNpcSayTo(p0, p1, p2, p3);
        }

        static private void Shim_osNpcStopFollow(SyscallShim self)
        {
            string p0 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            self._systemAPI.osNpcStopFollow(p0);
        }

        static private void Shim_osPerm2Use(SyscallShim self)
        {
            string p0 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            int ret = self._systemAPI.osPerm2Use(p0);
            self._interpreter.SafeOperandsPush(ConvToLSLType(ret));
        }

        static private void Shim_osProposePartnership(SyscallShim self)
        {
            string p1 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            string p0 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            Defer(self, "osProposePartnership", new object[] { p0, p1 }, () => ConvToLSLType(self._systemAPI.osProposePartnership(p0, p1)), "", true);
        }

        static private void Shim_osReplaceAgentEnvironment(SyscallShim self)
        {
            string p2 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            int p1 = ConvToInt(self._interpreter.ScriptState.Operands.Pop());
            string p0 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            Defer(self, "osReplaceAgentEnvironment", new object[] { p0, p1, p2 }, () => ConvToLSLType(self._systemAPI.osReplaceAgentEnvironment(p0, p1, p2)), -5, true);
        }

        static private void Shim_osReplaceParcelEnvironment(SyscallShim self)
        {
            string p1 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            int p0 = ConvToInt(self._interpreter.ScriptState.Operands.Pop());
            Defer(self, "osReplaceParcelEnvironment", new object[] { p0, p1 }, () => ConvToLSLType(self._systemAPI.osReplaceParcelEnvironment(p0, p1)), -5, true);
        }

        static private void Shim_osReplaceRegionEnvironment(SyscallShim self)
        {
            float p6 = ConvToFloat(self._interpreter.ScriptState.Operands.Pop());
            float p5 = ConvToFloat(self._interpreter.ScriptState.Operands.Pop());
            float p4 = ConvToFloat(self._interpreter.ScriptState.Operands.Pop());
            float p3 = ConvToFloat(self._interpreter.ScriptState.Operands.Pop());
            float p2 = ConvToFloat(self._interpreter.ScriptState.Operands.Pop());
            string p1 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            int p0 = ConvToInt(self._interpreter.ScriptState.Operands.Pop());
            Defer(self, "osReplaceRegionEnvironment", new object[] { p0, p1, p2, p3, p4, p5, p6 }, () => ConvToLSLType(self._systemAPI.osReplaceRegionEnvironment(p0, p1, p2, p3, p4, p5, p6)), -5, true);
        }

        static private void Shim_osResetEnvironment(SyscallShim self)
        {
            int p1 = ConvToInt(self._interpreter.ScriptState.Operands.Pop());
            int p0 = ConvToInt(self._interpreter.ScriptState.Operands.Pop());
            int ret = self._systemAPI.osResetEnvironment(p0, p1);
            self._interpreter.SafeOperandsPush(ConvToLSLType(ret));
        }

        static private void Shim_osRespondToPartnershipProposal(SyscallShim self)
        {
            int p1 = ConvToInt(self._interpreter.ScriptState.Operands.Pop());
            string p0 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            Defer(self, "osRespondToPartnershipProposal", new object[] { p0, p1 }, () => ConvToLSLType(self._systemAPI.osRespondToPartnershipProposal(p0, p1)), "", true);
        }

        static private void Shim_osReturnObject(SyscallShim self)
        {
            string p0 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            Defer(self, "osReturnObject", new object[] { p0 }, () => { self._systemAPI.osReturnObject(p0); return null; }, null, false);
        }

        static private void Shim_osReturnObjects(SyscallShim self)
        {
            int p0 = ConvToInt(self._interpreter.ScriptState.Operands.Pop());
            Defer(self, "osReturnObjects", new object[] { p0 }, () => { self._systemAPI.osReturnObjects(p0); return null; }, null, false);
        }

        static private void Shim_osSetAvatarFlyDisabled(SyscallShim self)
        {
            int p1 = ConvToInt(self._interpreter.ScriptState.Operands.Pop());
            string p0 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            self._systemAPI.osSetAvatarFlyDisabled(p0, p1);
        }

        static private void Shim_osSetAvatarForceFly(SyscallShim self)
        {
            int p1 = ConvToInt(self._interpreter.ScriptState.Operands.Pop());
            string p0 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            self._systemAPI.osSetAvatarForceFly(p0, p1);
        }

        static private void Shim_osSetAvatarWalkDisabled(SyscallShim self)
        {
            int p1 = ConvToInt(self._interpreter.ScriptState.Operands.Pop());
            string p0 = ConvToString(self._interpreter.ScriptState.Operands.Pop());
            self._systemAPI.osSetAvatarWalkDisabled(p0, p1);
        }

        // [Confluence] Tranquillity #320: the wind volumes (slots 1009-1011)
        static private void Shim_iwWindVolumeCreate(SyscallShim self)
        {
            LSLList p0 = ConvToLSLList(self._interpreter.ScriptState.Operands.Pop());
            int ret = self._systemAPI.iwWindVolumeCreate(p0);
            self._interpreter.SafeOperandsPush(ConvToLSLType(ret));
        }

        static private void Shim_iwWindVolumeRemove(SyscallShim self)
        {
            int p0 = ConvToInt(self._interpreter.ScriptState.Operands.Pop());
            int ret = self._systemAPI.iwWindVolumeRemove(p0);
            self._interpreter.SafeOperandsPush(ConvToLSLType(ret));
        }

        static private void Shim_iwWindVolumes(SyscallShim self)
        {
            LSLList ret = self._systemAPI.iwWindVolumes();
            self._interpreter.SafeOperandsPush(ConvToLSLType(ret));
        }
    }
}
