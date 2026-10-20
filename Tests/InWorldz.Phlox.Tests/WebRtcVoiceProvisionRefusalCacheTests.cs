/* Copyright (c) 2026 Legion Builds
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

// [Confluence] From OpenSim-Tranquillity PR #348, converted from NUnit to xunit for the Phlox test project.

using Xunit;
/*
 * Tests for the per-(agent, region) negative cache for refused provisions - the pure cache, and its use in
 * WebRtcVoiceServiceModule (a repeat refusal inside the window never reaches the voice service; the
 * counting FakeVoiceService from ProvisionFailedSessionCleanupTests proves it).
 */

using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.StructuredData;

namespace osWebRtcVoice.Tests
{
    public class ProvisionRefusalCacheTests
    {
        private static readonly UUID Agent = new UUID("aaaaaaaa-1111-1111-1111-0000000a0072");
        private static readonly UUID Other = new UUID("aaaaaaaa-2222-2222-2222-0000000a0072");
        private static readonly UUID RegionA = new UUID("bbbbbbbb-0000-0000-0000-00000a0a0072");
        private static readonly UUID RegionB = new UUID("bbbbbbbb-0000-0000-0000-00000b0b0072");

        private long _now = 1_000_000;

        private ProvisionRefusalCache New(int pSeconds = 5) => new ProvisionRefusalCache(TimeSpan.FromSeconds(pSeconds), () => _now);

        [Fact]
        public void RepeatInsideTheWindow_HitsTheCache_AndIsCounted()
        {
            var cache = New();
            var answer = new object();
            Assert.Equal(0, cache.RecordRefusal(Agent, RegionA, answer));

            _now += 1000;
            Assert.True(cache.TryGetRefusal(Agent, RegionA, out object a1));
            _now += 1000;
            Assert.True(cache.TryGetRefusal(Agent, RegionA, out object a2));
            Assert.Same(answer, a1);
            Assert.Same(answer, a2);

            _now += 3500;   // past the 5 s window
            Assert.False(cache.TryGetRefusal(Agent, RegionA, out _), "re-evaluated after the window");
            Assert.Equal(2, cache.RecordRefusal(Agent, RegionA, answer));
        }

        [Fact]
        public void Clear_ForgetsTheRefusal()
        {
            var cache = New();
            cache.RecordRefusal(Agent, RegionA, "refused");
            cache.TryGetRefusal(Agent, RegionA, out _);

            Assert.Equal(1, cache.Clear(Agent, RegionA));
            Assert.False(cache.TryGetRefusal(Agent, RegionA, out _));
            Assert.Equal(0, cache.Count);
        }

        [Fact]
        public void TheKeyIsAgentAndRegion()
        {
            var cache = New();
            cache.RecordRefusal(Agent, RegionA, "refused");

            Assert.False(cache.TryGetRefusal(Agent, RegionB, out _), "same agent, other region");
            Assert.False(cache.TryGetRefusal(Other, RegionA, out _), "other agent, same region");
        }

        [Fact]
        public void ZeroSeconds_DisablesTheCache()
        {
            var cache = New(0);
            cache.RecordRefusal(Agent, RegionA, "refused");
            Assert.False(cache.TryGetRefusal(Agent, RegionA, out _));
        }
    }
}
