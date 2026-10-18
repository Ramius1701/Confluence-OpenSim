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
            if (e == null || overrides == null)
                return e;

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
}
