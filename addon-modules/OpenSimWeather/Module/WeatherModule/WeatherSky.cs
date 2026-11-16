/*
 * OpenSimWeather - weather for OpenSimulator regions.
 * [Confluence] The weather sky, rethought (2026-10-18).
 */

using System;
using Nini.Config;
using OpenMetaverse;
using OpenSim.Framework;

namespace OpenSim.Region.OptionalModules.World.Weather
{
    /// <summary>
    /// What a weather does to a sky, as changes to the sky's own values rather than values of its own.
    /// </summary>
    /// <remarks>
    /// A region's day cycle has a sky for each part of the day (morning, noon, evening, midnight). A weather sky used to
    /// put the same storm colours, haze and density into every one of them, so midnight became the same mid-grey as
    /// noon. Here each sky keeps what it is and the weather is applied to it: clouds are raised to the weather's cover
    /// (never lowered), the horizon, blue density and cloud colours are desaturated and darkened by a factor, haze and
    /// density are added, and the stars and moon are dimmed by the clouds. A dark sky stays dark, only darker.
    ///
    /// An effect can be part-way (<see cref="Lerp"/>): <see cref="Neutral"/> changes nothing, so a fade from one
    /// weather to another is <c>Lerp(from, to, t)</c> applied at each step, and a new change that starts mid-fade
    /// starts from exactly what is showing.
    /// </remarks>
    public sealed class SkyEffect
    {
        /// <summary>The cloud cover the weather brings, 0..1; a sky with more keeps its own.</summary>
        public float CloudCover;
        /// <summary>How far the cloud shape (scale, densities, scroll) goes to the weather's, 0..1.</summary>
        public float CloudShapeWeight;
        public float CloudScale = 0.42f;
        public Vector3 CloudDensity1 = new(1f, 0.526f, 1f);
        public Vector3 CloudDensity2 = new(1f, 0.526f, 0.12f);
        public Vector2 CloudScroll = new(0.2f, 0.011f);

        /// <summary>Multiplier on the cloud colour (storm clouds are dark, snow clouds pale).</summary>
        public float CloudDarken = 1f;
        /// <summary>Multiplier on the horizon and blue density colours: how much the weather darkens the sky.</summary>
        public float SkyDarken = 1f;
        /// <summary>How far the sky and cloud colours go to grey, 0..1.</summary>
        public float Desaturate;
        /// <summary>Multipliers on the ambient light and on the sun and moon light.</summary>
        public float AmbientScale = 1f;
        public float SunScale = 1f;
        public float MoonScale = 1f;
        /// <summary>Added to the sky's haze density and haze horizon.</summary>
        public float HazeDensityAdd;
        public float HazeHorizonAdd;
        /// <summary>Multipliers on the atmosphere density and the distance multiplier (visibility).</summary>
        public float DensityScale = 1f;
        public float DistanceScale = 1f;
        /// <summary>How much of the stars the clouds hide, 0..1.</summary>
        public float StarDim;
        /// <summary>[Confluence] What Aurora Viewer draws for this weather (<see cref="AuroraWeather"/>); null: nothing.</summary>
        public AuroraWeather? Aurora;

        /// <summary>An effect that changes nothing.</summary>
        public static SkyEffect Neutral => new SkyEffect();

        public SkyEffect Clone() => (SkyEffect)MemberwiseClone();

        public bool IsNeutral =>
            CloudCover <= 0f && CloudShapeWeight <= 0f && CloudDarken == 1f && SkyDarken == 1f && Desaturate <= 0f
            && AmbientScale == 1f && SunScale == 1f && MoonScale == 1f && HazeDensityAdd == 0f && HazeHorizonAdd == 0f
            && DensityScale == 1f && DistanceScale == 1f && StarDim <= 0f;

        /// <summary>The effect a fraction <paramref name="t"/> of the way from <paramref name="a"/> to <paramref name="b"/> (null: neutral).</summary>
        public static SkyEffect Lerp(SkyEffect a, SkyEffect b, float t)
        {
            a ??= Neutral;
            b ??= Neutral;
            t = Math.Clamp(t, 0f, 1f);
            var e = new SkyEffect
            {
                CloudCover = L(a.CloudCover, b.CloudCover, t),
                CloudShapeWeight = L(a.CloudShapeWeight, b.CloudShapeWeight, t),
                CloudDarken = L(a.CloudDarken, b.CloudDarken, t),
                SkyDarken = L(a.SkyDarken, b.SkyDarken, t),
                Desaturate = L(a.Desaturate, b.Desaturate, t),
                AmbientScale = L(a.AmbientScale, b.AmbientScale, t),
                SunScale = L(a.SunScale, b.SunScale, t),
                MoonScale = L(a.MoonScale, b.MoonScale, t),
                HazeDensityAdd = L(a.HazeDensityAdd, b.HazeDensityAdd, t),
                HazeHorizonAdd = L(a.HazeHorizonAdd, b.HazeHorizonAdd, t),
                DensityScale = L(a.DensityScale, b.DensityScale, t),
                DistanceScale = L(a.DistanceScale, b.DistanceScale, t),
                StarDim = L(a.StarDim, b.StarDim, t),
                Aurora = AuroraWeather.Lerp(a.Aurora, b.Aurora, t),
            };

            // The cloud shape of a neutral effect is no shape at all: take the other's, not a blend toward a default.
            if (a.CloudShapeWeight <= 0f || b.CloudShapeWeight <= 0f)
            {
                SkyEffect shaped = a.CloudShapeWeight > 0f ? a : b;
                e.CloudScale = shaped.CloudScale;
                e.CloudDensity1 = shaped.CloudDensity1;
                e.CloudDensity2 = shaped.CloudDensity2;
                e.CloudScroll = shaped.CloudScroll;
            }
            else
            {
                e.CloudScale = L(a.CloudScale, b.CloudScale, t);
                e.CloudDensity1 = Vector3.Lerp(a.CloudDensity1, b.CloudDensity1, t);
                e.CloudDensity2 = Vector3.Lerp(a.CloudDensity2, b.CloudDensity2, t);
                e.CloudScroll = Vector2.Lerp(a.CloudScroll, b.CloudScroll, t);
            }
            return e;
        }

        private static float L(float a, float b, float t) => a + (b - a) * t;

        /// <summary>Apply this effect to one sky of a day cycle, in place.</summary>
        public void ApplyTo(SkyData sky)
        {
            if (sky == null)
                return;

            if (CloudCover > sky.cloud_shadow)
                sky.cloud_shadow = CloudCover;

            if (CloudShapeWeight > 0f)
            {
                float w = Math.Clamp(CloudShapeWeight, 0f, 1f);
                sky.cloud_scale = L(sky.cloud_scale, CloudScale, w);
                sky.cloud_pos_density1 = Vector3.Lerp(sky.cloud_pos_density1, CloudDensity1, w);
                sky.cloud_pos_density2 = Vector3.Lerp(sky.cloud_pos_density2, CloudDensity2, w);
                sky.cloud_scroll_rate = Vector2.Lerp(sky.cloud_scroll_rate, CloudScroll, w);
            }

            sky.cloud_color = Grey(sky.cloud_color, Desaturate) * CloudDarken;
            sky.blue_horizon = Grey(sky.blue_horizon, Desaturate) * SkyDarken;
            sky.blue_density = Grey(sky.blue_density, Desaturate) * SkyDarken;

            sky.ambient *= AmbientScale;
            sky.sunlight_color = new Vector4(sky.sunlight_color.X * SunScale, sky.sunlight_color.Y * SunScale,
                sky.sunlight_color.Z * SunScale, sky.sunlight_color.W);
            sky.moon_brightness *= MoonScale;

            sky.haze_density = Math.Clamp(sky.haze_density + HazeDensityAdd, 0f, 10f);
            sky.haze_horizon = Math.Clamp(sky.haze_horizon + HazeHorizonAdd, 0f, 5f);
            sky.density_multiplier *= DensityScale;
            sky.distance_multiplier *= DistanceScale;

            sky.star_brightness *= 1f - Math.Clamp(StarDim, 0f, 1f);

            // last, so that nothing changes the marked numbers after it
            Aurora?.WriteTo(sky);
        }

        /// <summary>A colour <paramref name="amount"/> of the way to the grey of the same brightness.</summary>
        private static Vector3 Grey(Vector3 c, float amount)
        {
            if (amount <= 0f)
                return c;
            float y = 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;
            return Vector3.Lerp(c, new Vector3(y, y, y), Math.Clamp(amount, 0f, 1f));
        }

        /// <summary>The weather's sky, with any keys an ini section sets over it.</summary>
        public static SkyEffect For(string weather, IConfig overrides = null)
        {
            SkyEffect e = DefaultFor(weather);
            if (e == null)
                return e;
            e.Aurora = AuroraWeather.DefaultFor(weather);
            if (overrides == null)
                return e;
            if (e.Aurora.HasValue)
                e.Aurora = AuroraWeather.Override(e.Aurora.Value, overrides);

            e.CloudCover = Math.Clamp(overrides.GetFloat("CloudCover", e.CloudCover), 0f, 1f);
            e.CloudScale = Math.Max(0f, overrides.GetFloat("CloudScale", e.CloudScale));
            e.CloudDarken = Math.Max(0f, overrides.GetFloat("CloudDarken", e.CloudDarken));
            e.SkyDarken = Math.Max(0f, overrides.GetFloat("SkyDarken", e.SkyDarken));
            e.Desaturate = Math.Clamp(overrides.GetFloat("Desaturate", e.Desaturate), 0f, 1f);
            e.AmbientScale = Math.Max(0f, overrides.GetFloat("AmbientScale", e.AmbientScale));
            e.SunScale = Math.Max(0f, overrides.GetFloat("SunScale", e.SunScale));
            e.MoonScale = Math.Max(0f, overrides.GetFloat("MoonScale", e.MoonScale));
            e.HazeDensityAdd = overrides.GetFloat("HazeDensityAdd", e.HazeDensityAdd);
            e.HazeHorizonAdd = overrides.GetFloat("HazeHorizonAdd", e.HazeHorizonAdd);
            e.DensityScale = Math.Max(0f, overrides.GetFloat("DensityScale", e.DensityScale));
            e.DistanceScale = Math.Max(0f, overrides.GetFloat("DistanceScale", e.DistanceScale));
            e.StarDim = Math.Clamp(overrides.GetFloat("StarDim", e.StarDim), 0f, 1f);
            return e;
        }

        /// <summary>
        /// The built-in skies. Clear and sunny have none: they show the region's own sky.
        /// </summary>
        public static SkyEffect DefaultFor(string weather)
        {
            switch ((weather ?? string.Empty).ToLowerInvariant())
            {
                case "rain":
                    return new SkyEffect
                    {
                        CloudCover = 0.8f, CloudShapeWeight = 1f, CloudScale = 0.55f,
                        CloudDensity1 = new Vector3(1.1f, 0.58f, 0.9f), CloudDensity2 = new Vector3(1.2f, 0.6f, 0.2f),
                        CloudScroll = new Vector2(0.3f, 0.05f),
                        CloudDarken = 0.7f, SkyDarken = 0.75f, Desaturate = 0.5f,
                        AmbientScale = 0.8f, SunScale = 0.65f, MoonScale = 0.6f,
                        HazeDensityAdd = 0.3f, HazeHorizonAdd = 0.15f, DensityScale = 1.4f, DistanceScale = 1f,
                        StarDim = 0.9f,
                    };
                case "storm":
                    return new SkyEffect
                    {
                        CloudCover = 0.95f, CloudShapeWeight = 1f, CloudScale = 0.72f,
                        CloudDensity1 = new Vector3(1.35f, 0.64f, 1f), CloudDensity2 = new Vector3(1.55f, 0.72f, 0.28f),
                        CloudScroll = new Vector2(0.48f, 0.1f),
                        CloudDarken = 0.35f, SkyDarken = 0.5f, Desaturate = 0.75f,
                        AmbientScale = 0.6f, SunScale = 0.4f, MoonScale = 0.35f,
                        HazeDensityAdd = 0.5f, HazeHorizonAdd = 0.25f, DensityScale = 1.8f, DistanceScale = 0.9f,
                        StarDim = 1f,
                    };
                case "snow":
                    return new SkyEffect
                    {
                        CloudCover = 0.85f, CloudShapeWeight = 1f, CloudScale = 0.7f,
                        CloudDensity1 = new Vector3(1.18f, 0.6f, 0.96f), CloudDensity2 = new Vector3(1.28f, 0.62f, 0.22f),
                        CloudScroll = new Vector2(0.14f, 0.035f),
                        CloudDarken = 0.95f, SkyDarken = 0.8f, Desaturate = 0.7f,
                        AmbientScale = 0.85f, SunScale = 0.75f, MoonScale = 0.7f,
                        HazeDensityAdd = 0.45f, HazeHorizonAdd = 0.3f, DensityScale = 1.5f, DistanceScale = 0.95f,
                        StarDim = 0.95f,
                    };
                case "blizzard":
                    return new SkyEffect
                    {
                        CloudCover = 0.97f, CloudShapeWeight = 1f, CloudScale = 0.78f,
                        CloudDensity1 = new Vector3(1.42f, 0.68f, 1.02f), CloudDensity2 = new Vector3(1.6f, 0.74f, 0.3f),
                        CloudScroll = new Vector2(0.52f, 0.12f),
                        CloudDarken = 0.85f, SkyDarken = 0.6f, Desaturate = 0.85f,
                        AmbientScale = 0.7f, SunScale = 0.5f, MoonScale = 0.45f,
                        HazeDensityAdd = 0.9f, HazeHorizonAdd = 0.6f, DensityScale = 2.2f, DistanceScale = 0.7f,
                        StarDim = 1f,
                    };
                default:
                    return null;
            }
        }
    }

    /// <summary>
    /// [Confluence] Aurora Viewer's weather (Aurora Weather), as a region sets it: rain or snow, how hard, wind, thunder.
    /// </summary>
    /// <remarks>
    /// Aurora Viewer draws its own rain, snow, puddles, lying snow, lightning and northern lights. With its "Let regions
    /// set the weather" ticked, it takes them from the region's EEP skies: a code carried in the lowest 12 bits of five
    /// of each sky's numbers (a change of about a thousandth of the value; no viewer shows a difference). Each sky the
    /// weather module sends is marked here, so an Aurora user sees the module's weather drawn by the viewer itself, and
    /// every other viewer sees what it saw before.
    ///
    /// The format is Aurora's (crates/aurora-viewer/src/weather/region.rs, SkyWeather::bits, and sky_mark.rs, at
    /// github.com/Aurora-Viewer/Aurora-Viewer). This is an implementation of that format written from its description,
    /// not a copy of its code; WeatherReworkTests holds codes Aurora's own encoder produced, so a change on either side
    /// shows as a failed test. Only the sky's numbers are marked: the server sends a day's skies without their names,
    /// so a code on a name would not reach the viewer.
    /// </remarks>
    public struct AuroraWeather
    {
        /// <summary>Snow; rain otherwise.</summary>
        public bool Snow;
        /// <summary>The chance it falls in one of Aurora's periods, 0..1. The module sets 1: it decides when itself.</summary>
        public float Chance;
        /// <summary>How hard it falls, 0..1: Aurora picks between these each period (equal: exactly this).</summary>
        public float AmountLow, AmountHigh;
        /// <summary>The wind's lulls and gusts, 0..1.</summary>
        public float WindLow, WindHigh;
        /// <summary>Where the wind comes from (degrees); null: the region's own wind.</summary>
        public float? WindFrom;
        /// <summary>Thunder and lightning (Aurora needs rain of 0.4 or more).</summary>
        public bool Thunder;
        /// <summary>How often lightning strikes, 0 seldom .. 1 often (in thirds).</summary>
        public float Strikes;

        private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"; // Crockford's base 32
        private const int SkyDataBits = 32;  // a sky's code without northern lights
        private const ulong SkyVersion = 2;
        private const int MarkData = 45;     // what a mark holds before its check
        private const int MarkPart = 12;     // bits in each of five numbers
        private const int MarkFields = 5;
        private const int MarkCheck = MarkFields * MarkPart - MarkData;
        private const ulong MarkSalt = 0x5EED_A0B0_7A11_0C0D;
        private const ulong Golden = 0x9E37_79B9_7F4A_7C15;
        private const uint PartMask = (1u << MarkPart) - 1;
        // a number this small (zero, subnormals) carries no part of a mark
        private const uint Smallest = 0x0080_0000 + (2u << MarkPart);

        /// <summary>The code's 32 bits, low bits first, as Aurora lays them out.</summary>
        public ulong Bits()
        {
            ulong v = 0;
            int at = 0;
            void Put(ulong value, int width) { v |= (value & ((1UL << width) - 1)) << at; at += width; }

            Put(SkyVersion, 2);
            Put(Snow ? 1UL : 0UL, 1);
            Put(Level(Chance, 31), 5);
            Put(Level(Math.Min(AmountLow, AmountHigh), 15), 4);
            Put(Level(Math.Max(AmountLow, AmountHigh), 15), 4);
            Put(Level(Math.Min(WindLow, WindHigh), 15), 4);
            Put(Level(Math.Max(WindLow, WindHigh), 15), 4);
            ulong from = WindFrom.HasValue
                ? 1 + ((ulong)MathF.Round(Euclid(WindFrom.Value, 360f) / 22.5f, MidpointRounding.AwayFromZero) % 16)
                : 0;
            Put(from, 5);
            Put(Thunder ? 1UL : 0UL, 1);
            Put(Level(Strikes, 3), 2);
            return v;
        }

        /// <summary>The nine letters Aurora shows after a sky name's "~" (for the log and the tests).</summary>
        public string Code()
        {
            const int len = 9;
            ulong data = Bits();
            ulong all = data | (Check(data, len * 5 - SkyDataBits) << SkyDataBits);
            var c = new char[len];
            for (int i = 0; i < len; i++)
                c[i] = Alphabet[(int)((all >> ((len - 1 - i) * 5)) & 31)];
            return new string(c);
        }

        /// <summary>
        /// Mark <paramref name="sky"/>'s numbers with this weather. False when the sky has too few numbers to carry it
        /// (Aurora then sees no weather in it).
        /// </summary>
        public bool WriteTo(SkyData sky)
        {
            if (sky == null)
                return false;
            ulong data = Bits();
            ulong all = data | (MarkCheckOf(data) << MarkData);
            return Carry(sky, all);
        }

        /// <summary>The weather marked in <paramref name="sky"/>, if any (for the tests and the log).</summary>
        public static AuroraWeather? ReadFrom(SkyData sky)
        {
            if (sky == null)
                return null;
            float[] values = CarrierValues(sky, out int[] which);
            if (which == null)
                return null;
            ulong all = 0;
            for (int i = 0; i < MarkFields; i++)
                all |= (ulong)(BitConverter.SingleToUInt32Bits(values[which[i]]) & PartMask) << (i * MarkPart);
            ulong data = all & ((1UL << MarkData) - 1);
            if (all >> MarkData != MarkCheckOf(data))
                return null;
            if ((data & 3) != SkyVersion || data >> SkyDataBits != 0)
                return null;
            int at = 2;
            ulong Take(int width) { ulong o = (data >> at) & ((1UL << width) - 1); at += width; return o; }
            var w = new AuroraWeather { Snow = Take(1) == 1, Chance = Take(5) / 31f };
            w.AmountLow = Take(4) / 15f;
            w.AmountHigh = Take(4) / 15f;
            w.WindLow = Take(4) / 15f;
            w.WindHigh = Take(4) / 15f;
            ulong from = Take(5);
            w.WindFrom = from >= 1 && from <= 16 ? (from - 1) * 22.5f : null;
            w.Thunder = Take(1) == 1;
            w.Strikes = Take(2) / 3f;
            return w;
        }

        // The numbers that may carry a mark, in Aurora's order (sky_mark.rs FIELDS): the first five that are not
        // tiny take a part each. The last is sent only when the sky has a reflection probe ambiance.
        private static float[] CarrierValues(SkyData sky, out int[] which)
        {
            float[] v =
            {
                sky.cloud_scale, sky.cloud_shadow, sky.dome_offset, sky.gamma, sky.max_y, sky.moon_scale,
                sky.sun_arc_radians, sky.sun_scale, sky.reflectionProbeAmbiance,
            };
            var picked = new int[MarkFields];
            int n = 0;
            for (int i = 0; i < v.Length && n < MarkFields; i++)
            {
                if (i == 8 && !sky.HasRefProbe)
                    continue;
                if (float.IsFinite(v[i]) && (BitConverter.SingleToUInt32Bits(v[i]) & 0x7FFF_FFFF) >= Smallest)
                    picked[n++] = i;
            }
            which = n == MarkFields ? picked : null;
            return v;
        }

        private static bool Carry(SkyData sky, ulong all)
        {
            float[] v = CarrierValues(sky, out int[] which);
            if (which == null)
                return false;
            for (int i = 0; i < MarkFields; i++)
            {
                float m = Marked(v[which[i]], (uint)(all >> (i * MarkPart)) & PartMask);
                switch (which[i])
                {
                    case 0: sky.cloud_scale = m; break;
                    case 1: sky.cloud_shadow = m; break;
                    case 2: sky.dome_offset = m; break;
                    case 3: sky.gamma = m; break;
                    case 4: sky.max_y = m; break;
                    case 5: sky.moon_scale = m; break;
                    case 6: sky.sun_arc_radians = m; break;
                    case 7: sky.sun_scale = m; break;
                    case 8: sky.reflectionProbeAmbiance = m; break;
                }
            }
            return true;
        }

        // v with part in its low bits, its size never above v's (a value at a maximum stays within it).
        private static float Marked(float v, uint part)
        {
            uint bits = BitConverter.SingleToUInt32Bits(v);
            uint sign = bits & 0x8000_0000;
            uint mag = bits & 0x7FFF_FFFF;
            uint baseBits = mag & ~PartMask;
            if ((baseBits | part) > mag)
                baseBits -= 1u << MarkPart;
            return BitConverter.UInt32BitsToSingle(sign | baseBits | part);
        }

        private static ulong MarkCheckOf(ulong data) => unchecked((data ^ MarkSalt) * Golden) >> (64 - MarkCheck);

        private static ulong Check(ulong data, int width) => unchecked(data * Golden) >> (64 - width);

        private static ulong Level(float v, ulong steps)
            => (ulong)MathF.Round(Math.Clamp(v, 0f, 1f) * steps, MidpointRounding.AwayFromZero);

        private static float Euclid(float a, float m)
        {
            float r = a % m;
            return r < 0 ? r + m : r;
        }

        /// <summary>The weather <paramref name="t"/> of the way from <paramref name="a"/> to <paramref name="b"/> (null: nothing falling).</summary>
        public static AuroraWeather? Lerp(AuroraWeather? a, AuroraWeather? b, float t)
        {
            if (a == null && b == null)
                return null;
            t = Math.Clamp(t, 0f, 1f);
            // a side with no weather is the other's kind, with nothing falling and no wind
            AuroraWeather x = a ?? Calm(b.Value), y = b ?? Calm(a.Value);
            AuroraWeather near = t < 0.5f ? x : y;
            return new AuroraWeather
            {
                Snow = near.Snow,
                Chance = Math.Max(x.Chance, y.Chance),
                AmountLow = x.AmountLow + (y.AmountLow - x.AmountLow) * t,
                AmountHigh = x.AmountHigh + (y.AmountHigh - x.AmountHigh) * t,
                WindLow = x.WindLow + (y.WindLow - x.WindLow) * t,
                WindHigh = x.WindHigh + (y.WindHigh - x.WindHigh) * t,
                WindFrom = near.WindFrom,
                Thunder = near.Thunder,
                Strikes = near.Strikes,
            };
        }

        private static AuroraWeather Calm(AuroraWeather w)
            => new AuroraWeather { Snow = w.Snow, Chance = w.Chance, WindFrom = w.WindFrom, Strikes = w.Strikes };

        /// <summary>The built-in Aurora weather of one of the module's weathers; null for one with nothing falling.</summary>
        public static AuroraWeather? DefaultFor(string weather)
        {
            switch ((weather ?? string.Empty).ToLowerInvariant())
            {
                case "rain": return Fixed(false, 0.55f, 0.15f, 0.4f, false);
                case "storm": return Fixed(false, 0.9f, 0.45f, 0.9f, true);
                case "snow": return Fixed(true, 0.5f, 0.1f, 0.3f, false);
                case "blizzard": return Fixed(true, 0.95f, 0.7f, 1f, false);
                default: return null;
            }
        }

        /// <summary><paramref name="w"/> with any Aurora keys an ini section sets over it.</summary>
        public static AuroraWeather Override(AuroraWeather w, IConfig c)
        {
            if (c == null)
                return w;
            float amount = Math.Clamp(c.GetFloat("AuroraAmount", w.AmountHigh), 0f, 1f);
            w.AmountLow = w.AmountHigh = amount;
            w.WindLow = Math.Clamp(c.GetFloat("AuroraWindLow", w.WindLow), 0f, 1f);
            w.WindHigh = Math.Clamp(c.GetFloat("AuroraWindHigh", w.WindHigh), 0f, 1f);
            w.Thunder = c.GetBoolean("AuroraThunder", w.Thunder);
            w.Strikes = Math.Clamp(c.GetFloat("AuroraStrikes", w.Strikes), 0f, 1f);
            return w;
        }

        private static AuroraWeather Fixed(bool snow, float amount, float windLow, float windHigh, bool thunder) => new AuroraWeather
        {
            Snow = snow, Chance = 1f, AmountLow = amount, AmountHigh = amount,
            WindLow = windLow, WindHigh = windHigh, WindFrom = null, Thunder = thunder, Strikes = 2f / 3f,
        };
    }
}
