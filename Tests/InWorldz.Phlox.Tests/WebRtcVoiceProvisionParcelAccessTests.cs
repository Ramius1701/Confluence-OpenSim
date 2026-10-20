/* Copyright (c) 2026 Legion Builds
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

// [Confluence] From OpenSim-Tranquillity PR #350, converted from NUnit to xunit for the Phlox test project.

using Xunit;
/*
 * Unit tests for ProvisionParcelAccess: a parcel with voice off never gets voice, and a parcel ban is honoured,
 * whether or not the estate is TaxFree. coreBars is the simulator's own restriction-or-ban answer; the tests
 * pass false to show that voice does not rely on it.
 */

using System;
using OpenMetaverse;
using OpenSim.Framework;
using osWebRtcVoice;

namespace osWebRtcVoice.Tests
{
    public class ProvisionParcelAccessTests
    {
        private static readonly UUID Owner = UUID.Random();
        private static readonly UUID Avatar = UUID.Random();
        private const int Now = 1_800_000_000;
        private static readonly Func<UUID, bool> Nobody = _ => false;

        private static LandData Parcel(bool voiceOn, bool bannedAvatar)
        {
            uint flags = voiceOn ? (uint)ParcelFlags.AllowVoiceChat : 0u;
            var ld = new LandData { OwnerID = Owner, Flags = flags };
            if (bannedAvatar)
            {
                ld.Flags |= (uint)ParcelFlags.UseBanList;
                ld.ParcelAccessList.Add(new LandAccessEntry { AgentID = Avatar, Flags = AccessList.Ban, Expires = 0 });
            }
            return ld;
        }

        private static ProvisionAccess Check(LandData ld, bool coreBars)
            => ProvisionParcelAccess.Check(ld, coreBars, Nobody, Nobody, Avatar, Now);

        [Fact]
        public void VoiceOffParcel_IsRefused_EvenWhenTheSimBarsNobody()
        {
            // The simulator's answer bars nobody, so only the voice-off flag can stop this.
            Assert.Equal(ProvisionAccess.VoiceOff, Check(Parcel(voiceOn: false, bannedAvatar: false), coreBars: false));
        }

        [Fact]
        public void VoiceOnParcel_WithNoBan_IsAllowed()
        {
            Assert.Equal(ProvisionAccess.Allowed, Check(Parcel(voiceOn: true, bannedAvatar: false), coreBars: false));
        }

        [Fact]
        public void BannedAvatar_IsRefused_EvenWhenTheSimBarsNobody()
        {
            Assert.Equal(ProvisionAccess.Barred, Check(Parcel(voiceOn: true, bannedAvatar: true), coreBars: false));
        }

        [Fact]
        public void AvatarTheSimBars_IsRefused()
        {
            // The simulator's restrict or ban answer is honoured.
            Assert.Equal(ProvisionAccess.Barred, Check(Parcel(voiceOn: true, bannedAvatar: false), coreBars: true));
        }

        [Fact]
        public void BannedParcelOwner_IsNotBarred()
        {
            LandData ld = Parcel(voiceOn: true, bannedAvatar: true);
            ld.OwnerID = Avatar;
            Assert.Equal(ProvisionAccess.Allowed, Check(ld, coreBars: false));
        }

        [Fact]
        public void VoiceOffTakesPrecedence_OverBar()
        {
            Assert.Equal(ProvisionAccess.VoiceOff, Check(Parcel(voiceOn: false, bannedAvatar: true), coreBars: true));
        }
    }
}
