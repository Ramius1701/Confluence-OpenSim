/*
 * OpenSimWeather - weather for OpenSimulator regions.
 * [Confluence] Precipitation around the avatars, rethought (2026-10-19).
 */

using System;
using OpenMetaverse;

namespace OpenSim.Region.OptionalModules.World.Weather
{
    /// <summary>
    /// The particles one weather emitter sends: rain or snow that falls from a set height to the ground.
    /// </summary>
    /// <remarks>
    /// The emitters used to sit 80 m above the terrain and burst their particles in every direction from a sphere up
    /// to 50 m across, with an acceleration that never let the slowest of them reach the ground and a colour that faded
    /// to almost nothing on the way down; they also followed the region's wind, which can carry a slow snowflake a long
    /// way off. An emitter here sits a short drop (about 22 m) above the avatars it is for and sends its particles down
    /// a cone at a steady speed (snow about 1.3 m/s, rain about 9 m/s) with only a little acceleration and a set drift,
    /// and their lifetime is worked out from the drop so each one ends at the ground. They do not fade on the way: they
    /// vanish where they land. How many it sends is set from a particle budget, so the particles alive around one
    /// avatar stay within what a viewer draws.
    /// </remarks>
    public static class PrecipitationParticles
    {
        // PSYS_PART_* flags: interpolate colour and scale, follow velocity (rain streaks point along their fall), and
        // emissive: lit by the scene, flakes under a dark weather sky turn grey and vanish against buildings, water and
        // the ground, so they looked as if they never came down (seen in-world 2026-10-19).
        private const uint InterpColor = 1u, InterpScale = 2u, FollowVelocity = 32u, Emissive = 256u;
        private const byte PatternAngleCone = 8;    // PSYS_SRC_PATTERN_ANGLE_CONE
        private const float MaxLifetime = 30f;      // the most a viewer keeps a particle

        /// <summary>The emitter prim's rotation: upside down, so its +Z, the cone's axis, points at the ground.</summary>
        public static readonly Quaternion EmitterRotation = new Quaternion(1f, 0f, 0f, 0f);

        public sealed class Fall
        {
            /// <summary>Speed the particles leave the emitter with (m/s), down the cone.</summary>
            public float Speed;
            /// <summary>Downward acceleration (m/s², positive down).</summary>
            public float Accel;
            /// <summary>Half-angle of the downward cone (radians from straight down).</summary>
            public float ConeHalfAngle;
            public float StartScale, EndScale;      // width (snow: square flakes; rain: a streak of this width)
            public float StreakLength;              // rain: the streak's length; snow: 0
            public Color4 Color;
        }

        /// <summary>How a weather's particles fall; null for a weather without precipitation.</summary>
        public static Fall For(string weather)
        {
            switch ((weather ?? string.Empty).ToLowerInvariant())
            {
                case "rain":
                    return new Fall { Speed = 8.5f, Accel = 1.5f, ConeHalfAngle = 0.3f, StartScale = 0.05f, EndScale = 0.05f,
                        StreakLength = 0.8f, Color = new Color4(0.78f, 0.82f, 0.88f, 0.6f) };
                case "storm":
                    return new Fall { Speed = 11f, Accel = 2f, ConeHalfAngle = 0.35f, StartScale = 0.06f, EndScale = 0.06f,
                        StreakLength = 1.0f, Color = new Color4(0.74f, 0.78f, 0.84f, 0.65f) };
                case "snow":
                    return new Fall { Speed = 1.3f, Accel = 0.04f, ConeHalfAngle = 0.55f, StartScale = 0.35f, EndScale = 0.35f,
                        Color = new Color4(1f, 1f, 1f, 0.85f) };
                case "blizzard":
                    return new Fall { Speed = 2.6f, Accel = 0.15f, ConeHalfAngle = 0.75f, StartScale = 0.32f, EndScale = 0.32f,
                        Color = new Color4(0.97f, 0.98f, 1f, 0.9f) };
                default:
                    return null;
            }
        }

        /// <summary>
        /// How far from below its emitter a particle lands, at most: the drop times the tangent of the cone, whatever
        /// the speed. Emitters are set about one and a half of these apart so their circles overlap.
        /// </summary>
        public static float Spread(Fall fall, float drop) => Math.Max(0.5f, drop) * MathF.Tan(fall.ConeHalfAngle);

        /// <summary>
        /// The time a particle takes to fall <paramref name="drop"/> metres: the slowest one, leaving at the edge of the
        /// cone, plus a little so it ends just below the ground rather than above it.
        /// </summary>
        public static float Lifetime(Fall fall, float drop)
        {
            float v = fall.Speed * MathF.Cos(fall.ConeHalfAngle);
            float a = fall.Accel;
            float d = Math.Max(0.5f, drop) * 1.08f + 0.5f;
            float t = a > 1e-4f ? (-v + MathF.Sqrt(v * v + 2f * a * d)) / a : d / Math.Max(0.05f, v);
            return Math.Clamp(t, 0.5f, MaxLifetime);
        }

        /// <summary>
        /// The particle system of one emitter <paramref name="drop"/> metres above the ground it covers.
        /// <paramref name="particlesAlive"/> is this emitter's share of the budget: about that many of its particles are
        /// in the air at once. <paramref name="drift"/> is the horizontal acceleration the wind gives them.
        /// </summary>
        public static Primitive.ParticleSystem Build(Fall fall, float drop, float particlesAlive, Vector2 drift, UUID texture)
        {
            float lifetime = Lifetime(fall, drop);
            // A drop longer than a particle can live (a very slow fall from very high) is sped up to fit.
            float speed = fall.Speed;
            if (lifetime >= MaxLifetime)
                speed = Math.Max(speed, (Math.Max(0.5f, drop) * 1.08f) / (MaxLifetime * MathF.Cos(fall.ConeHalfAngle)) - 0.5f * fall.Accel * MaxLifetime);

            // particles alive = count per burst * lifetime / burst interval
            float perSecond = Math.Max(1f, particlesAlive) / lifetime;
            int count = Math.Clamp((int)MathF.Ceiling(perSecond * 0.1f), 1, 50);
            float burstRate = Math.Clamp(count / perSecond, 0.02f, 5f);

            bool streak = fall.StreakLength > 0f;
            return new Primitive.ParticleSystem
            {
                CRC = 1,
                PartDataFlags = (Primitive.ParticleSystem.ParticleDataFlags)(InterpColor | InterpScale | Emissive | (streak ? FollowVelocity : 0u)),
                Pattern = (Primitive.ParticleSystem.SourcePattern)PatternAngleCone,
                Texture = texture,
                BurstRadius = 0.5f,
                // A cone of this half-angle round the emitter's own +Z, which points at the ground: the emitter prim
                // is upside down (EmitterRotation). Angles near pi, meant as "down" round an upright +Z, came out of
                // Firestorm as a fountain shooting up (seen in-world 2026-10-19); a small angle round an axis that
                // points down reads the same in every viewer.
                InnerAngle = 0f,
                OuterAngle = fall.ConeHalfAngle,
                MaxAge = 0f,
                PartMaxAge = lifetime,
                BurstRate = burstRate,
                BurstPartCount = (byte)count,
                BurstSpeedMin = speed * 0.9f,
                BurstSpeedMax = speed * 1.1f,
                PartAcceleration = new Vector3(drift.X, drift.Y, -fall.Accel),
                PartStartColor = fall.Color,
                PartEndColor = fall.Color,          // no fading on the way down
                PartStartScaleX = fall.StartScale,
                PartStartScaleY = streak ? fall.StreakLength : fall.StartScale,
                PartEndScaleX = fall.EndScale,
                PartEndScaleY = streak ? fall.StreakLength : fall.EndScale,
                BlendFuncSource = 7,                // PSYS_PART_BF_SOURCE_ALPHA
                BlendFuncDest = 9,                  // PSYS_PART_BF_ONE_MINUS_SOURCE_ALPHA
            };
        }
    }
}
