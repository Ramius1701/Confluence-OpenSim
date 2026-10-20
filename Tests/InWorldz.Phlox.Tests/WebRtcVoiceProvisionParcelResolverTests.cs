/* Copyright (c) 2026 Legion Builds
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

// [Confluence] From OpenSim-Tranquillity PR #350, converted from NUnit to xunit for the Phlox test project.

using Xunit;
/*
 * Unit tests for ProvisionParcelResolver. For a ROOT agent the
 * server-derived parcel always wins and the viewer's parcel_local_id only decides what gets logged.
 * A CHILD agent's position is not in this region, so it never derives from position: its client
 * hint selects the parcel, or with no hint there is no parcel (estate room).
 */

using osWebRtcVoice;

namespace osWebRtcVoice.Tests
{
    public class ProvisionParcelResolverTests
    {
        [Fact]
        public void Honest_SameId_NoMismatch()
        {
            ParcelResolution res = ProvisionParcelResolver.Resolve(clientLocalId: 7, serverLocalId: 7);

            Assert.Equal(ParcelSource.ServerPosition, res.Source);
            Assert.Equal(7, res.LocalId);
            Assert.Equal(7, res.ServerLocalId);
            Assert.False(res.ClientMismatch);
            Assert.False(res.ClientAbsent);
        }

        [Fact]
        public void Spoof_DifferentId_MismatchAndServerIdWins()
        {
            ParcelResolution res = ProvisionParcelResolver.Resolve(clientLocalId: 12, serverLocalId: 7);

            Assert.Equal(ParcelSource.ServerPosition, res.Source);
            Assert.Equal(7, res.LocalId);
            Assert.Equal(7, res.ServerLocalId);
            Assert.True(res.ClientMismatch);
            Assert.False(res.ClientAbsent);
        }

        [Fact]
        public void Absent_NullId_ClientAbsentAndServerIdWins()
        {
            ParcelResolution res = ProvisionParcelResolver.Resolve(clientLocalId: null, serverLocalId: 7);

            Assert.Equal(ParcelSource.ServerPosition, res.Source);
            Assert.Equal(7, res.LocalId);
            Assert.Equal(7, res.ServerLocalId);
            Assert.False(res.ClientMismatch);
            Assert.True(res.ClientAbsent);
        }

        [Theory]
        [InlineData(7)]
        [InlineData(12)]
        [InlineData(null)]
        public void Root_AlwaysServerPosition_RegardlessOfHint(int? hint)
        {
            ParcelResolution res = ProvisionParcelResolver.Resolve(new ParcelResolveInput(ClientLocalId: hint, IsChildAgent: false), serverLocalId: 7);

            Assert.Equal(ParcelSource.ServerPosition, res.Source);
            Assert.Equal(7, res.LocalId);
        }

        [Fact]
        public void Root_NoParcelAtPosition_ServerPositionWithNoId()
        {
            // The genuine failure: the handler refuses (WARN) on a ServerPosition result with no id.
            ParcelResolution res = ProvisionParcelResolver.Resolve(new ParcelResolveInput(ClientLocalId: 7, IsChildAgent: false), serverLocalId: null);

            Assert.Equal(ParcelSource.ServerPosition, res.Source);
            Assert.Null(res.LocalId);
            Assert.False(res.ClientMismatch);
        }

        [Fact]
        public void Child_WithHint_ClientHintWithHintId()
        {
            ParcelResolution res = ProvisionParcelResolver.Resolve(new ParcelResolveInput(ClientLocalId: 12, IsChildAgent: true), serverLocalId: null);

            Assert.Equal(ParcelSource.ClientHint, res.Source);
            Assert.Equal(12, res.LocalId);
            Assert.False(res.ClientMismatch);
            Assert.False(res.ClientAbsent);
        }

        [Fact]
        public void Child_WithHint_IgnoresAnyServerId()
        {
            // A child's position is outside this region; even if a caller passed a position-derived id it must not win.
            ParcelResolution res = ProvisionParcelResolver.Resolve(new ParcelResolveInput(ClientLocalId: 12, IsChildAgent: true), serverLocalId: 7);

            Assert.Equal(ParcelSource.ClientHint, res.Source);
            Assert.Equal(12, res.LocalId);
        }

        [Fact]
        public void Child_NoHint_NoneEstateRoom()
        {
            ParcelResolution res = ProvisionParcelResolver.Resolve(new ParcelResolveInput(ClientLocalId: null, IsChildAgent: true), serverLocalId: null);

            Assert.Equal(ParcelSource.None, res.Source);
            Assert.Null(res.LocalId);
            Assert.True(res.ClientAbsent);
        }

        [Fact]
        public void RequiresPosition_RootOnly()
        {
            Assert.True(ProvisionParcelResolver.DerivesFromPosition(new ParcelResolveInput(null, IsChildAgent: false)));
            Assert.False(ProvisionParcelResolver.DerivesFromPosition(new ParcelResolveInput(12, IsChildAgent: true)));
            Assert.False(ProvisionParcelResolver.DerivesFromPosition(new ParcelResolveInput(null, IsChildAgent: true)));
        }
    }
}
