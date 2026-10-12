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

using OpenMetaverse;

namespace OpenSim.Region.Framework.Scenes
{
    /// <summary>
    /// One pending damage against a presence (Tranquillity #212): who did it, how much they asked for, what it was
    /// adjusted to, and what kind it was. A batch of these is what ScenePresence.ApplyDamage applies in one go - the
    /// physics frame's collisions, or a single scripted call - and what on_damage / final_damage see through
    /// llDetectedDamage(n). <see cref="Amount"/> is the mutable one: llAdjustDamage inside on_damage rewrites it;
    /// <see cref="OriginalDamage"/> keeps what was asked.
    /// </summary>
    public sealed class DamageEntry
    {
        /// <summary>DAMAGE_TYPE_IMPACT: collisions, ground falls, llSetDamage prims.</summary>
        public const int TYPE_IMPACT = -1;
        /// <summary>DAMAGE_TYPE_GENERIC: the default for scripted damage.</summary>
        public const int TYPE_GENERIC = 0;

        /// <summary>The object that dealt it; UUID.Zero for the ground.</summary>
        public UUID SourceObject;
        /// <summary>Its owner; UUID.Zero for the ground.</summary>
        public UUID SourceOwner;
        /// <summary>Local id of the source, for TriggerAvatarKill's killer argument; 0 for the ground.</summary>
        public uint SourceLocalId;
        /// <summary>What was asked for. Never changes.</summary>
        public float OriginalDamage;
        /// <summary>What will be (on_damage) or was (final_damage) applied. llAdjustDamage writes it.</summary>
        public float Amount;
        /// <summary>A DAMAGE_TYPE_* value.</summary>
        public int DamageType;

        public DamageEntry(UUID sourceObject, UUID sourceOwner, uint sourceLocalId, float amount, int damageType)
        {
            SourceObject = sourceObject;
            SourceOwner = sourceOwner;
            SourceLocalId = sourceLocalId;
            OriginalDamage = amount;
            Amount = amount;
            DamageType = damageType;
        }

        public override string ToString() => $"{Amount}/{OriginalDamage} type {DamageType} from {SourceObject}";
    }
}
