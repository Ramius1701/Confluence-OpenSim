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

using log4net;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.Assets;
using OpenMetaverse.Packets;
using OpenMetaverse.Rendering;
using OpenSim.Framework;
using OpenSim.Region.CoreModules.World.Land;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.Framework.Scenes.Animation;
using OpenSim.Region.Framework.Scenes.Scripting;
using OpenSim.Region.Framework.Scenes.Serialization;
using OpenSim.Region.PhysicsModules.SharedBase;
using OpenSim.Region.ScriptEngine.Interfaces;
using OpenSim.Region.ScriptEngine.Shared.Api.Interfaces;
using OpenSim.Region.ScriptEngine.Shared.ScriptBase;
using OpenSim.Services.Connectors.Hypergrid;
using OpenSim.Services.Interfaces;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using AssetLandmark = OpenSim.Framework.AssetLandmark;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;
using LSL_Float = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLFloat;
using LSL_Integer = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLInteger;
using LSL_Key = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_List = OpenSim.Region.ScriptEngine.Shared.LSL_Types.list;
using LSL_Rotation = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Quaternion;
using LSL_String = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_Vector = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Vector3;
using MappingType = OpenMetaverse.MappingType;
using PermissionMask = OpenSim.Framework.PermissionMask;
using PresenceInfo = OpenSim.Services.Interfaces.PresenceInfo;
using PrimType = OpenSim.Region.Framework.Scenes.PrimType;
using RegionFlags = OpenSim.Framework.RegionFlags;
using RegionInfo = OpenSim.Framework.RegionInfo;
using OpenMetaverse.StructuredData;

namespace OpenSim.Region.ScriptEngine.Shared.Api
{
    /// <summary>
    /// [Confluence] The environment rule parsers both script engines share: the llSetEnvironment rule list applied to
    /// a ViewerEnvironment (Confluence's, 2026-08-06) and the llSetAgentEnvironment rule list turned into an
    /// EnvironmentUpdate for one avatar (from Tranquillity's experience work). Moved out of LSL_Api unchanged, so YEngine
    /// behaves exactly as before and Phlox runs the same rules (2026-10-21). The script-specific parts come in as
    /// arguments: looking a texture up by name in the script's prim (AssetType.Unknown for any type) and reporting an
    /// error the way the engine reports script errors.
    /// </summary>
    public static class EnvironmentRules
    {
        // Helpers copied from LSL_Api, where its other functions still use them.

        private static Vector2 ToVector2(LSL_Vector v)
        {
            return new Vector2((float)v.x, (float)v.y);
        }

        private static Vector3 ToVector3(LSL_Vector v)
        {
            return new Vector3((float)v.x, (float)v.y, (float)v.z);
        }

        private static Quaternion ToQuaternion(LSL_Rotation v)
        {
            Quaternion q = new((float)v.x, (float)v.y, (float)v.z, (float)v.s);
            q.Normalize();
            return q;
        }

        private static bool InRange(float value, float min, float max)
        {
            return value >= min && value <= max;
        }

        private static bool InRange(LSL_Vector value, float min, float max)
        {
            return InRange((float)value.x, min, max)
                && InRange((float)value.y, min, max)
                && InRange((float)value.z, min, max);
        }

        private static bool InRangeXY(LSL_Vector value, float min, float max)
        {
            return InRange((float)value.x, min, max)
                && InRange((float)value.y, min, max);
        }

        private static bool LooksLikeVectorItem(LSL_List list, int index)
        {
            if (list is null || index < 0 || index >= list.Length)
                return false;

            object value = list.Data[index];
            return value is LSL_Vector || value is Vector3;
        }

        private static bool TryReadIntegerItem(LSL_List list, int index, out int value)
        {
            value = 0;
            try
            {
                value = list.GetLSLIntegerItem(index);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool ShouldConsumeOptionalSkyReadback(LSL_List parameters, int index)
        {
            return index + 4 <= parameters.Length
                && TryReadIntegerItem(parameters, index, out int defaultFlag)
                && (defaultFlag == 0 || defaultFlag == 1)
                && LooksLikeVectorItem(parameters, index + 1)
                && LooksLikeVectorItem(parameters, index + 2)
                && LooksLikeVectorItem(parameters, index + 3);
        }

        // LSL_Api.llLinear2sRGB, which the agent rules apply to colours.
        private static LSL_Vector llLinear2sRGB(LSL_Vector src)
        {
            return new LSL_Vector(Util.LinearTosRGB((float)src.x), Util.LinearTosRGB((float)src.y), Util.LinearTosRGB((float)src.z));
        }

        /// <summary>llSetEnvironment's rules applied to <paramref name="env"/>; 1, or an ENV_* error code.</summary>
        public static LSL_Integer Apply(ViewerEnvironment env, LSL_List parameters, float altitude, bool allSkyTracks,
            Func<string, AssetType, UUID> resolveAsset)
        {
            try
            {
                WaterData water = null;
                WaterData GetWaterTarget()
                {
                    water ??= env.EnsureWater();
                    return water;
                }

                List<SkyData> skyTargets = null;
                List<SkyData> GetSkyTargets()
                {
                    skyTargets ??= env.EnsureSkyTargets(altitude, allSkyTracks);
                    return skyTargets;
                }

                int i = 0;

                while (i < parameters.Length)
                {
                    int rule = parameters.GetLSLIntegerItem(i++);
                    switch (rule)
                    {
                        case ScriptBaseClass.SKY_AMBIENT:
                            if (i + 1 > parameters.Length)
                                return ScriptBaseClass.ENV_INVALID_RULE;
                            LSL_Vector ambient = parameters.GetVector3Item(i++);
                            if (!InRange(ambient, 0.0f, 3.0f))
                                return ScriptBaseClass.ENV_VALIDATION_FAIL;
                            foreach (SkyData sky in GetSkyTargets())
                                sky.ambient = ToVector3(ambient);
                            break;

                        case ScriptBaseClass.SKY_BLUE:
                            if (i + 2 > parameters.Length)
                                return ScriptBaseClass.ENV_INVALID_RULE;
                            LSL_Vector blueDensity = parameters.GetVector3Item(i++);
                            LSL_Vector blueHorizon = parameters.GetVector3Item(i++);
                            if (!InRange(blueDensity, 0.0f, 3.0f) || !InRange(blueHorizon, 0.0f, 3.0f))
                                return ScriptBaseClass.ENV_VALIDATION_FAIL;
                            foreach (SkyData sky in GetSkyTargets())
                            {
                                sky.blue_density = ToVector3(blueDensity);
                                sky.blue_horizon = ToVector3(blueHorizon);
                            }
                            break;

                        case ScriptBaseClass.SKY_CLOUDS:
                            if (i + 7 > parameters.Length)
                                return ScriptBaseClass.ENV_INVALID_RULE;
                            LSL_Vector cloudColor = parameters.GetVector3Item(i++);
                            float cloudShadow = (float)parameters.GetLSLFloatItem(i++);
                            float cloudScale = (float)parameters.GetLSLFloatItem(i++);
                            float cloudVariance = (float)parameters.GetLSLFloatItem(i++);
                            LSL_Vector cloudScroll = parameters.GetVector3Item(i++);
                            LSL_Vector cloudDensity1 = parameters.GetVector3Item(i++);
                            LSL_Vector cloudDensity2 = parameters.GetVector3Item(i++);
                            if (i < parameters.Length
                                && TryReadIntegerItem(parameters, i, out int drawClassicClouds)
                                && (drawClassicClouds == 0 || drawClassicClouds == 1)
                                && (i == parameters.Length - 1 || !LooksLikeVectorItem(parameters, i + 1)))
                                i++;
                            if (!InRange(cloudColor, 0.0f, 1.0f) || !InRange(cloudShadow, 0.0f, 1.0f)
                                || !InRange(cloudScale, 0.0f, 1.0f) || !InRange(cloudVariance, 0.0f, 1.0f)
                                || !InRangeXY(cloudScroll, -20.0f, 20.0f)
                                || !InRange(cloudDensity1, 0.0f, 1.0f) || !InRange(cloudDensity2, 0.0f, 1.0f))
                                return ScriptBaseClass.ENV_VALIDATION_FAIL;
                            foreach (SkyData sky in GetSkyTargets())
                            {
                                sky.cloud_color = ToVector3(cloudColor);
                                sky.cloud_shadow = cloudShadow;
                                sky.cloud_scale = cloudScale;
                                sky.cloud_variance = cloudVariance;
                                sky.cloud_scroll_rate = ToVector2(cloudScroll);
                                sky.cloud_pos_density1 = ToVector3(cloudDensity1);
                                sky.cloud_pos_density2 = ToVector3(cloudDensity2);
                            }
                            break;

                        case ScriptBaseClass.SKY_DOME:
                            if (i + 3 > parameters.Length)
                                return ScriptBaseClass.ENV_INVALID_RULE;
                            float domeOffset = (float)parameters.GetLSLFloatItem(i++);
                            float domeRadius = (float)parameters.GetLSLFloatItem(i++);
                            float maxY = (float)parameters.GetLSLFloatItem(i++);
                            if (!InRange(domeOffset, 0.0f, 1.0f) || !InRange(domeRadius, 0.0f, 100000.0f)
                                || !InRange(maxY, 0.0f, 100000.0f))
                                return ScriptBaseClass.ENV_VALIDATION_FAIL;
                            foreach (SkyData sky in GetSkyTargets())
                            {
                                sky.dome_offset = domeOffset;
                                sky.dome_radius = domeRadius;
                                sky.max_y = maxY;
                            }
                            break;

                        case ScriptBaseClass.SKY_GAMMA:
                            if (i + 1 > parameters.Length)
                                return ScriptBaseClass.ENV_INVALID_RULE;
                            float gamma = (float)parameters.GetLSLFloatItem(i++);
                            if (!InRange(gamma, 0.0f, 20.0f))
                                return ScriptBaseClass.ENV_VALIDATION_FAIL;
                            foreach (SkyData sky in GetSkyTargets())
                                sky.gamma = gamma;
                            break;

                        case ScriptBaseClass.SKY_GLOW:
                            if (i + 2 > parameters.Length)
                                return ScriptBaseClass.ENV_INVALID_RULE;
                            float glowSize = (float)parameters.GetLSLFloatItem(i++);
                            float glowFocus = (float)parameters.GetLSLFloatItem(i++);
                            if (!InRange(glowSize, -20.0f, 40.0f) || !InRange(glowFocus, -20.0f, 20.0f))
                                return ScriptBaseClass.ENV_VALIDATION_FAIL;
                            foreach (SkyData sky in GetSkyTargets())
                                sky.glow = new Vector3(glowSize, sky.glow.Y, glowFocus);
                            break;

                        case ScriptBaseClass.SKY_MOON:
                            if (i + 3 > parameters.Length)
                                return ScriptBaseClass.ENV_INVALID_RULE;
                            Quaternion moonRotation = ToQuaternion(parameters.GetQuaternionItem(i++));
                            float moonScale = (float)parameters.GetLSLFloatItem(i++);
                            float moonBrightness = (float)parameters.GetLSLFloatItem(i++);
                            if (!InRange(moonScale, 0.0f, 20.0f) || !InRange(moonBrightness, 0.0f, 1.0f))
                                return ScriptBaseClass.ENV_VALIDATION_FAIL;
                            if (ShouldConsumeOptionalSkyReadback(parameters, i))
                                i += 4;
                            foreach (SkyData sky in GetSkyTargets())
                            {
                                sky.moon_rotation = moonRotation;
                                sky.moon_scale = moonScale;
                                sky.moon_brightness = moonBrightness;
                            }
                            break;

                        case ScriptBaseClass.SKY_PLANET:
                            if (i + 3 > parameters.Length)
                                return ScriptBaseClass.ENV_INVALID_RULE;
                            float planetRadius = (float)parameters.GetLSLFloatItem(i++);
                            float skyBottomRadius = (float)parameters.GetLSLFloatItem(i++);
                            float skyTopRadius = (float)parameters.GetLSLFloatItem(i++);
                            if (!InRange(planetRadius, 0.0f, 100000.0f) || !InRange(skyBottomRadius, 0.0f, 100000.0f)
                                || !InRange(skyTopRadius, 0.0f, 100000.0f) || skyTopRadius < skyBottomRadius)
                                return ScriptBaseClass.ENV_VALIDATION_FAIL;
                            foreach (SkyData sky in GetSkyTargets())
                            {
                                sky.planet_radius = planetRadius;
                                sky.sky_bottom_radius = skyBottomRadius;
                                sky.sky_top_radius = skyTopRadius;
                            }
                            break;

                        case ScriptBaseClass.SKY_REFRACTION:
                            if (i + 3 > parameters.Length)
                                return ScriptBaseClass.ENV_INVALID_RULE;
                            float moistureLevel = (float)parameters.GetLSLFloatItem(i++);
                            float dropletRadius = (float)parameters.GetLSLFloatItem(i++);
                            float iceLevel = (float)parameters.GetLSLFloatItem(i++);
                            if (!InRange(moistureLevel, 0.0f, 1.0f) || !InRange(dropletRadius, 0.0f, 2000.0f)
                                || !InRange(iceLevel, 0.0f, 1.0f))
                                return ScriptBaseClass.ENV_VALIDATION_FAIL;
                            foreach (SkyData sky in GetSkyTargets())
                            {
                                sky.moisture_level = moistureLevel;
                                sky.droplet_radius = dropletRadius;
                                sky.ice_level = iceLevel;
                            }
                            break;

                        case ScriptBaseClass.SKY_STAR_BRIGHTNESS:
                            if (i + 1 > parameters.Length)
                                return ScriptBaseClass.ENV_INVALID_RULE;
                            float starBrightness = (float)parameters.GetLSLFloatItem(i++);
                            if (!InRange(starBrightness, 0.0f, 500.0f))
                                return ScriptBaseClass.ENV_VALIDATION_FAIL;
                            foreach (SkyData sky in GetSkyTargets())
                                sky.star_brightness = starBrightness;
                            break;

                        case ScriptBaseClass.SKY_SUN:
                            if (i + 3 > parameters.Length)
                                return ScriptBaseClass.ENV_INVALID_RULE;
                            Quaternion sunRotation = ToQuaternion(parameters.GetQuaternionItem(i++));
                            float sunScale = (float)parameters.GetLSLFloatItem(i++);
                            LSL_Vector sunlightColor = parameters.GetVector3Item(i++);
                            if (!InRange(sunScale, 0.0f, 20.0f) || !InRange(sunlightColor, 0.0f, 3.0f))
                                return ScriptBaseClass.ENV_VALIDATION_FAIL;
                            if (ShouldConsumeOptionalSkyReadback(parameters, i))
                                i += 4;
                            foreach (SkyData sky in GetSkyTargets())
                            {
                                sky.sun_rotation = sunRotation;
                                sky.sun_scale = sunScale;
                                sky.sunlight_color = new Vector4((float)sunlightColor.x, (float)sunlightColor.y, (float)sunlightColor.z, sky.sunlight_color.W);
                            }
                            break;

                        case ScriptBaseClass.SKY_CLOUD_TEXTURE:
                            if (i + 1 > parameters.Length)
                                return ScriptBaseClass.ENV_INVALID_RULE;
                            UUID cloudTexture = resolveAsset(parameters.GetLSLStringItem(i++), AssetType.Texture);
                            if (cloudTexture.IsZero())
                                return ScriptBaseClass.ENV_NO_ENVIRONMENT;
                            foreach (SkyData sky in GetSkyTargets())
                                sky.CloudTexture = cloudTexture;
                            break;

                        case ScriptBaseClass.SKY_MOON_TEXTURE:
                            if (i + 1 > parameters.Length)
                                return ScriptBaseClass.ENV_INVALID_RULE;
                            UUID moonTexture = resolveAsset(parameters.GetLSLStringItem(i++), AssetType.Texture);
                            if (moonTexture.IsZero())
                                return ScriptBaseClass.ENV_NO_ENVIRONMENT;
                            foreach (SkyData sky in GetSkyTargets())
                                sky.MoonTexture = moonTexture;
                            break;

                        case ScriptBaseClass.SKY_SUN_TEXTURE:
                            if (i + 1 > parameters.Length)
                                return ScriptBaseClass.ENV_INVALID_RULE;
                            UUID sunTexture = resolveAsset(parameters.GetLSLStringItem(i++), AssetType.Texture);
                            if (sunTexture.IsZero())
                                return ScriptBaseClass.ENV_NO_ENVIRONMENT;
                            foreach (SkyData sky in GetSkyTargets())
                                sky.SunTexture = sunTexture;
                            break;

                        case ScriptBaseClass.SKY_HAZE:
                            if (i + 4 > parameters.Length)
                                return ScriptBaseClass.ENV_INVALID_RULE;
                            float hazeDensity = (float)parameters.GetLSLFloatItem(i++);
                            float hazeHorizon = (float)parameters.GetLSLFloatItem(i++);
                            float densityMultiplier = (float)parameters.GetLSLFloatItem(i++);
                            float distanceMultiplier = (float)parameters.GetLSLFloatItem(i++);
                            if (!InRange(hazeDensity, 0.0f, 4.0f) || !InRange(hazeHorizon, 0.0f, 4.0f)
                                || !InRange(densityMultiplier, 0.0f, 1.0f) || !InRange(distanceMultiplier, 0.0f, 1000.0f))
                                return ScriptBaseClass.ENV_VALIDATION_FAIL;
                            foreach (SkyData sky in GetSkyTargets())
                            {
                                sky.haze_density = hazeDensity;
                                sky.haze_horizon = hazeHorizon;
                                sky.density_multiplier = densityMultiplier;
                                sky.distance_multiplier = distanceMultiplier;
                            }
                            break;

                        case ScriptBaseClass.SKY_REFLECTION_PROBE_AMBIANCE:
                            if (i + 1 > parameters.Length)
                                return ScriptBaseClass.ENV_INVALID_RULE;
                            float reflectionProbeAmbiance = (float)parameters.GetLSLFloatItem(i++);
                            if (!InRange(reflectionProbeAmbiance, 0.0f, 10.0f))
                                return ScriptBaseClass.ENV_VALIDATION_FAIL;
                            foreach (SkyData sky in GetSkyTargets())
                            {
                                sky.HasRefProbe = true;
                                sky.reflectionProbeAmbiance = reflectionProbeAmbiance;
                            }
                            break;

                        case ScriptBaseClass.WATER_BLUR_MULTIPLIER:
                            if (i + 1 > parameters.Length)
                                return ScriptBaseClass.ENV_INVALID_RULE;
                            float blur = (float)parameters.GetLSLFloatItem(i++);
                            if (!InRange(blur, -0.5f, 0.5f))
                                return ScriptBaseClass.ENV_VALIDATION_FAIL;
                            GetWaterTarget().blurMultiplier = blur;
                            break;

                        case ScriptBaseClass.WATER_FOG:
                            if (i + 3 > parameters.Length)
                                return ScriptBaseClass.ENV_INVALID_RULE;
                            LSL_Vector fogColor = parameters.GetVector3Item(i++);
                            float fogDensity = (float)parameters.GetLSLFloatItem(i++);
                            float underwaterMod = (float)parameters.GetLSLFloatItem(i++);
                            if (!InRange(fogColor, 0.0f, 1.0f) || !InRange(fogDensity, -10.0f, 10.0f)
                                || !InRange(underwaterMod, 0.0f, 20.0f))
                                return ScriptBaseClass.ENV_VALIDATION_FAIL;
                            WaterData fogWater = GetWaterTarget();
                            fogWater.waterFogColor = new Vector3((float)fogColor.x, (float)fogColor.y, (float)fogColor.z);
                            fogWater.waterFogDensity = fogDensity;
                            fogWater.underWaterFogMod = underwaterMod;
                            break;

                        case ScriptBaseClass.WATER_FRESNEL:
                            if (i + 2 > parameters.Length)
                                return ScriptBaseClass.ENV_INVALID_RULE;
                            float fresnelOffset = (float)parameters.GetLSLFloatItem(i++);
                            float fresnelScale = (float)parameters.GetLSLFloatItem(i++);
                            if (!InRange(fresnelOffset, 0.0f, 1.0f) || !InRange(fresnelScale, 0.0f, 1.0f))
                                return ScriptBaseClass.ENV_VALIDATION_FAIL;
                            WaterData fresnelWater = GetWaterTarget();
                            fresnelWater.fresnelOffset = fresnelOffset;
                            fresnelWater.fresnelScale = fresnelScale;
                            break;

                        case ScriptBaseClass.WATER_NORMAL_SCALE:
                            if (i + 1 > parameters.Length)
                                return ScriptBaseClass.ENV_INVALID_RULE;
                            LSL_Vector normalScale = parameters.GetVector3Item(i++);
                            if (!InRange(normalScale, 0.0f, 10.0f))
                                return ScriptBaseClass.ENV_VALIDATION_FAIL;
                            GetWaterTarget().normScale = new Vector3((float)normalScale.x, (float)normalScale.y, (float)normalScale.z);
                            break;

                        case ScriptBaseClass.WATER_REFRACTION:
                            if (i + 2 > parameters.Length)
                                return ScriptBaseClass.ENV_INVALID_RULE;
                            float scaleAbove = (float)parameters.GetLSLFloatItem(i++);
                            float scaleBelow = (float)parameters.GetLSLFloatItem(i++);
                            if (!InRange(scaleAbove, 0.0f, 3.0f) || !InRange(scaleBelow, 0.0f, 3.0f))
                                return ScriptBaseClass.ENV_VALIDATION_FAIL;
                            WaterData refractionWater = GetWaterTarget();
                            refractionWater.scaleAbove = scaleAbove;
                            refractionWater.scaleBelow = scaleBelow;
                            break;

                        case ScriptBaseClass.WATER_WAVE_DIRECTION:
                            if (i + 2 > parameters.Length)
                                return ScriptBaseClass.ENV_INVALID_RULE;
                            LSL_Vector bigWave = parameters.GetVector3Item(i++);
                            LSL_Vector littleWave = parameters.GetVector3Item(i++);
                            if (!InRangeXY(bigWave, -20.0f, 20.0f) || !InRangeXY(littleWave, -20.0f, 20.0f))
                                return ScriptBaseClass.ENV_VALIDATION_FAIL;
                            WaterData waveWater = GetWaterTarget();
                            waveWater.wave2Dir = ToVector2(bigWave);
                            waveWater.wave1Dir = ToVector2(littleWave);
                            break;

                        case ScriptBaseClass.WATER_NORMAL_TEXTURE:
                            if (i + 1 > parameters.Length)
                                return ScriptBaseClass.ENV_INVALID_RULE;
                            UUID normalMap = resolveAsset(parameters.GetLSLStringItem(i++), AssetType.Texture);
                            if (normalMap.IsZero())
                                return ScriptBaseClass.ENV_NO_ENVIRONMENT;
                            GetWaterTarget().normalMap = normalMap;
                            break;

                        case ScriptBaseClass.ENVIRONMENT_DAYINFO:
                            if (i + 2 > parameters.Length)
                                return ScriptBaseClass.ENV_INVALID_RULE;
                            int dayLength = parameters.GetLSLIntegerItem(i++);
                            int dayOffset = parameters.GetLSLIntegerItem(i++);
                            if (dayLength <= 0)
                                return ScriptBaseClass.ENV_VALIDATION_FAIL;
                            env.DayLength = dayLength;
                            env.DayOffset = dayOffset;
                            break;

                        case ScriptBaseClass.SKY_TRACKS:
                            if (i + 3 > parameters.Length)
                                return ScriptBaseClass.ENV_INVALID_RULE;
                            float altitude0 = (float)parameters.GetLSLFloatItem(i++);
                            float altitude1 = (float)parameters.GetLSLFloatItem(i++);
                            float altitude2 = (float)parameters.GetLSLFloatItem(i++);
                            if (!InRange(altitude0, -10000.0f, 10000.0f)
                                || !InRange(altitude1, -10000.0f, 10000.0f)
                                || !InRange(altitude2, -10000.0f, 10000.0f))
                                return ScriptBaseClass.ENV_VALIDATION_FAIL;
                            env.Altitudes[0] = altitude0;
                            env.Altitudes[1] = altitude1;
                            env.Altitudes[2] = altitude2;
                            env.SortAltitudes();
                            break;

                        case ScriptBaseClass.SKY_TEXTURE_DEFAULTS:
                        case ScriptBaseClass.SKY_LIGHT:
                        case ScriptBaseClass.WATER_TEXTURE_DEFAULTS:
                            return ScriptBaseClass.ENV_INVALID_RULE;

                        default:
                            return ScriptBaseClass.ENV_INVALID_RULE;
                    }
                }

                env.InvalidateCaches();
                return 1;
            }
            catch
            {
                return ScriptBaseClass.ENV_INVALID_RULE;
            }
        }

        /// <summary>llSetAgentEnvironment's rules as an update for one avatar; null when a rule cannot be read.</summary>
        public static EnvironmentUpdate ToAgentUpdate(LSL_Float time, LSL_List param_list, UUID ownerID, string objectName,
            string parcelName, Func<string, AssetType, UUID> resolveAsset, Action<string, string> reportError)
        {
            string parcel = parcelName;

            if(param_list.Length == 0)
            {
                return new ClearEnvironmentUpdate
                {
                    OwnerID = ownerID,
                    TransitionTime = (float)time.value,
                    ObjectName = objectName,
                    Permission = 17,
                    ParcelName = parcel
                };
            }

            var update = new PartialEnvironmentUpdate
            {
                OwnerID = ownerID,
                TransitionTime = (float)time.value,
                ObjectName = objectName,
                Permission = 17,
                ParcelName = parcel
            };

            int n = 0;
            int rulesParsed = 0;
            int index;

            try
            {
                while (n < param_list.Length)
                {
                    ++rulesParsed;

                    int code = param_list.GetLSLIntegerItem(n++);

                    int remain = param_list.Length - n;
                    index = n;

                    bool error = false;

                    // todo: clamp vectors

                    switch (code)
                    {
                        case ScriptBaseClass.SKY_CLOUDS:
                            if (remain < 7)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_CLOUDS) Expecting vector parameter..", rulesParsed));
                                return null;
                            }

                            try
                            {
                                update.sky["cloud_color"] = (Vector3)param_list.GetVector3Item(n++);
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_CLOUDS) Expecting vector parameter..", rulesParsed));
                                return null;
                            }

                            try
                            {
                                update.sky["cloud_shadow"] = (float)param_list.GetLSLFloatItem(n++);
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_CLOUDS) Expecting float parameter..", rulesParsed));
                                return null;
                            }

                            try
                            {
                                update.sky["cloud_scale"] = (float)param_list.GetLSLFloatItem(n++);
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_CLOUDS) Expecting float parameter..", rulesParsed));
                                return null;
                            }

                            try
                            {
                                update.sky["cloud_variance"] = (float)param_list.GetLSLFloatItem(n++);
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_CLOUDS) Expecting float parameter..", rulesParsed));
                                return null;
                            }

                            try
                            {
                                Vector3 scroll = (Vector3)param_list.GetVector3Item(n++);
                                update.sky["cloud_scroll_rate"] = new Vector2(scroll.X, scroll.Y);
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_CLOUDS) Expecting vector parameter..", rulesParsed));
                                return null;
                            }

                            try
                            {
                                update.sky["cloud_pos_density1"] = (Vector3)param_list.GetVector3Item(n++);
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_CLOUDS) Expecting vector parameter..", rulesParsed));
                                return null;
                            }

                            try
                            {
                                update.sky["cloud_pos_density2"] = (Vector3)param_list.GetVector3Item(n++);
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_CLOUDS) Expecting vector parameter..", rulesParsed));
                                return null;
                            }

                            break;
                        case ScriptBaseClass.SKY_CLOUD_TEXTURE:
                            if (remain < 1)
                                error = true;

                            if (!error)
                            {
                                string texture = param_list.Data[n++].ToString();

                                UUID texture_id = resolveAsset(texture, AssetType.Unknown);
                                if (texture_id == UUID.Zero)
                                    update.sky["cloud_id"] = texture_id;
                                else
                                    error = true;
                            }

                            if (error)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} -> SKY_CLOUD_TEXTURE: arg #{1} - parameter 1 must be a texture name/key", rulesParsed, n - index - 1));
                            }

                            break;
                        case ScriptBaseClass.SKY_GAMMA:
                            if (remain < 1)
                                error = true;

                            if (!error)
                            {
                                LSL_Float f = param_list.GetLSLFloatItem(n++);
                                float fl = (float)f.value;
                                update.sky["gamma"] = Math.Clamp(fl, 0.0f, 20.0f);
                            }

                            if (error)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} -> SKY_GAMMA: arg #{1} - parameter 1 must be a float", rulesParsed, n - index - 1));
                            }

                            break;
                        case ScriptBaseClass.SKY_DOME:
                            if (remain < 2)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_DOME) Expecting float parameter..", rulesParsed));
                                return null;
                            }

                            try
                            {
                                update.sky["cloud_color"] = (Vector3)param_list.GetVector3Item(n++);
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_DOME) Expecting vector parameter..", rulesParsed));
                                return null;
                            }

                            try
                            {
                                update.sky["cloud_shadow"] = (float)param_list.GetLSLFloatItem(n++);
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_DOME) Expecting float parameter..", rulesParsed));
                                return null;
                            }

                            break;
                        case ScriptBaseClass.SKY_GLOW:
                            if (remain < 2)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_GLOW) Expecting float parameter..", rulesParsed));
                                return null;
                            }

                            Vector3 glow = new Vector3();

                            try
                            {
                                float x = (float)param_list.GetLSLFloatItem(n++);
                                glow.X = x >= 0.0f ? x <= 1.0f ? 40.0f * x : 1.0f : 0.0f;
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_GLOW) Expecting float parameter..", rulesParsed));
                                return null;
                            }

                            try
                            {
                                float z = (float)param_list.GetLSLFloatItem(n++);
                                glow.Z = z >= -2.0f ? z <= 2.0f ? 10.0f * -z : 1.0f : 0.0f;
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_GLOW) Expecting float parameter..", rulesParsed));
                                return null;
                            }

                            update.sky["glow"] = glow;

                            break;
                        case ScriptBaseClass.SKY_MOON:
                            if (remain < 3)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_MOON) Expecting rotation parameter..", rulesParsed));
                                return null;
                            }

                            try
                            {
                                update.sky["moon_rotation"] = (Quaternion)param_list.GetQuaternionItem(n++);
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_MOON) Expecting rotation parameter..", rulesParsed));
                                return null;
                            }

                            try
                            {
                                float moon_scale = (float)param_list.GetLSLFloatItem(n++);
                                update.sky["moon_scale"] = Math.Clamp(moon_scale, 0.25f, 20.0f);
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_MOON) Expecting float parameter..", rulesParsed));
                                return null;
                            }

                            try
                            {
                                float moon_brightness = (float)param_list.GetLSLFloatItem(n++);
                                update.sky["moon_brightness"] = Math.Clamp(moon_brightness, 0.0f, 0.0f);
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_MOON) Expecting float parameter..", rulesParsed));
                                return null;
                            }

                            break;
                        case ScriptBaseClass.SKY_MOON_TEXTURE:
                            if (remain < 1)
                                error = true;

                            if (!error)
                            {
                                string texture = param_list.Data[n++].ToString();

                                UUID texture_id = resolveAsset(texture, AssetType.Unknown);
                                if (texture_id == UUID.Zero)
                                    update.sky["moon_id"] = texture_id;
                                else
                                    error = true;
                            }

                            if (error)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} -> SKY_MOON_TEXTURE: arg #{1} - parameter 1 must be a texture name/key", rulesParsed, n - index - 1));
                            }

                            break;
                        case ScriptBaseClass.SKY_STAR_BRIGHTNESS:
                            if (remain < 1)
                                error = true;

                            if (!error)
                            {
                                LSL_Float f = param_list.GetLSLFloatItem(n++);
                                float fl = (float)f.value;
                                update.sky["star_brightness"] = Math.Clamp(fl, 0.0f, 500.0f);
                            }

                            if (error)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} -> SKY_STAR_BRIGHTNESS: arg #{1} - parameter 1 must be a float", rulesParsed, n - index - 1));
                            }

                            break;
                        case ScriptBaseClass.SKY_SUN:
                            if (remain < 3)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_SUN) Expecting rotation parameter..", rulesParsed));
                                return null;
                            }

                            try
                            {
                                update.sky["sun_rotation"] = (Quaternion)param_list.GetQuaternionItem(n++);
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_SUN) Expecting rotation parameter..", rulesParsed));
                                return null;
                            }

                            try
                            {
                                float sun_scale = (float)param_list.GetLSLFloatItem(n++);
                                update.sky["sun_scale"] = Math.Clamp(sun_scale, 0.25f, 20.0f);
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_SUN) Expecting float parameter..", rulesParsed));
                                return null;
                            }

                            try
                            {
                                Vector3 sun_color = (Vector3)param_list.GetVector3Item(n++);

                                // todo: check this is correct

                                var srgb = llLinear2sRGB(sun_color);

                                update.sky["sunlight_color"] = (Vector3)srgb;
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_SUN) Expecting vector parameter..", rulesParsed));
                                return null;
                            }

                            break;
                        case ScriptBaseClass.SKY_SUN_TEXTURE:
                            if (remain < 1)
                                error = true;

                            if (!error)
                            {
                                string texture = param_list.Data[n++].ToString();

                                UUID texture_id = resolveAsset(texture, AssetType.Unknown);
                                if (texture_id == UUID.Zero)
                                    update.sky["sun_id"] = texture_id;
                                else
                                    error = true;
                            }

                            if (error)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} -> SKY_SUN_TEXTURE: arg #{1} - parameter 1 must be a texture name/key", rulesParsed, n - index - 1));
                            }

                            break;
                        case ScriptBaseClass.SKY_PLANET:
                            if (remain < 3)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_PLANET) Expecting rotation parameter..", rulesParsed));
                                return null;
                            }

                            try
                            {
                                float planet_radius = (float)param_list.GetLSLFloatItem(n++);
                                update.sky["planet_radius"] = Math.Clamp(planet_radius, 1000.0f, 32768.0f);
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_PLANET) Expecting rotation parameter..", rulesParsed));
                                return null;
                            }

                            try
                            {
                                float sky_bottom_radius = (float)param_list.GetLSLFloatItem(n++);
                                update.sky["sky_bottom_radius"] = Math.Clamp(sky_bottom_radius, 1000.0f, 32768.0f);
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_PLANET) Expecting float parameter..", rulesParsed));
                                return null;
                            }

                            try
                            {
                                float sky_top_radius = (float)param_list.GetLSLFloatItem(n++);
                                update.sky["sky_top_radius"] = Math.Clamp(sky_top_radius, 1000.0f, 32768.0f);
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_PLANET) Expecting float parameter..", rulesParsed));
                                return null;
                            }

                            break;
                        case ScriptBaseClass.SKY_REFRACTION:
                            if (remain < 3)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_REFRACTION) Expecting rotation parameter..", rulesParsed));
                                return null;
                            }

                            try
                            {
                                float moisture_level = (float)param_list.GetLSLFloatItem(n++);
                                update.sky["moisture_level"] = Math.Clamp(moisture_level, 0.0f, 1.0f);
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_REFRACTION) Expecting rotation parameter..", rulesParsed));
                                return null;
                            }

                            try
                            {
                                float droplet_radius = (float)param_list.GetLSLFloatItem(n++);
                                update.sky["droplet_radius"] = Math.Clamp(droplet_radius, 5.0f, 1000.0f);
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_REFRACTION) Expecting float parameter..", rulesParsed));
                                return null;
                            }

                            try
                            {
                                float ice_level = (float)param_list.GetLSLFloatItem(n++);
                                update.sky["ice_level"] = Math.Clamp(ice_level, 0.0f, 1.0f);
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_REFRACTION) Expecting float parameter..", rulesParsed));
                                return null;
                            }

                            break;
                        case ScriptBaseClass.WATER_BLUR_MULTIPLIER:
                            if (remain < 1)
                                error = true;

                            if (!error)
                            {
                                LSL_Float f = param_list.GetLSLFloatItem(n++);
                                float fl = (float)f.value;
                                update.water["blur_multiplier"] = Math.Clamp(fl, -0.5f, 0.5f);
                            }

                            if (error)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} -> WATER_BLUR_MULTIPLIER: arg #{1} - parameter 1 must be a float", rulesParsed, n - index - 1));
                            }

                            break;
                        case ScriptBaseClass.WATER_FOG:
                            if (remain < 3)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (WATER_FOG) Expecting rotation parameter..", rulesParsed));
                                return null;
                            }

                            try
                            {
                                update.water["water_fog_color"] = (Vector3)param_list.GetVector3Item(n++);
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (WATER_FOG) Expecting rotation parameter..", rulesParsed));
                                return null;
                            }

                            try
                            {
                                float water_fog_density = (float)param_list.GetLSLFloatItem(n++);
                                update.water["water_fog_density"] = Math.Clamp(water_fog_density, -10.0f, 10.0f);
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (WATER_FOG) Expecting float parameter..", rulesParsed));
                                return null;
                            }

                            try
                            {
                                float underwater_fog_mod = (float)param_list.GetLSLFloatItem(n++);
                                update.water["underwater_fog_mod"] = Math.Clamp(underwater_fog_mod, 0.0f, 20.0f);
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (WATER_FOG) Expecting float parameter..", rulesParsed));
                                return null;
                            }

                            break;
                        case ScriptBaseClass.WATER_FRESNEL:
                            if (remain < 2)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (WATER_FRESNEL) Expecting float parameter..", rulesParsed));
                                return null;
                            }
                            

                            try
                            {
                                float fresnel_offset = (float)param_list.GetLSLFloatItem(n++);
                                update.water["fresnel_offset"] = Math.Clamp(fresnel_offset, 0.0f, 1.0f);
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (WATER_FRESNEL) Expecting float parameter..", rulesParsed));
                                return null;
                            }

                            try
                            {
                                float fresnel_scale = (float)param_list.GetLSLFloatItem(n++);
                                update.water["fresnel_scale"] = Math.Clamp(fresnel_scale, 0.0f, 1.0f);
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (WATER_FRESNEL) Expecting float parameter..", rulesParsed));
                                return null;
                            }


                            break;
                        case ScriptBaseClass.WATER_NORMAL_TEXTURE:
                            if (remain < 1)
                                error = true;

                            if (!error)
                            {
                                string texture = param_list.Data[n++].ToString();

                                UUID texture_id = resolveAsset(texture, AssetType.Unknown);
                                if (texture_id == UUID.Zero)
                                    update.water["normal_map"] = texture_id;
                                else
                                    error = true;
                            }

                            if (error)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} -> WATER_NORMAL_TEXTURE: arg #{1} - parameter 1 must be a texture name/key", rulesParsed, n - index - 1));
                            }

                            break;
                        case ScriptBaseClass.WATER_NORMAL_SCALE:
                            if (remain < 1)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (WATER_NORMAL_SCALE) Expecting float parameter..", rulesParsed));
                                return null;
                            }

                            try
                            {
                                update.water["normal_scale"] = (Vector3)param_list.GetVector3Item(n++);
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (WATER_NORMAL_SCALE) Expecting vector parameter..", rulesParsed));
                                return null;
                            }

                            break;
                        case ScriptBaseClass.WATER_REFRACTION:
                            if (remain < 2)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (WATER_REFRACTION) Expecting float parameter..", rulesParsed));
                                return null;
                            }


                            try
                            {
                                float scale_above = (float)param_list.GetLSLFloatItem(n++);
                                update.water["scale_above"] = Math.Clamp(scale_above, 0.0f, 3.0f);
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (WATER_REFRACTION) Expecting float parameter..", rulesParsed));
                                return null;
                            }

                            try
                            {
                                float scale_below = (float)param_list.GetLSLFloatItem(n++);
                                update.water["scale_below"] = Math.Clamp(scale_below, 0.0f, 3.0f);
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (WATER_REFRACTION) Expecting float parameter..", rulesParsed));
                                return null;
                            }


                            break;
                        case ScriptBaseClass.WATER_WAVE_DIRECTION:
                            if (remain < 2)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (WATER_WAVE_DIRECTION) Expecting float parameter..", rulesParsed));
                                return null;
                            }


                            try
                            {
                                Vector3 vec1 = param_list.GetVector3Item(n++);
                                Vector2 wave1_direction = new Vector2(vec1.X, vec1.Y);
                                update.water["wave1_direction"] = wave1_direction;
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (WATER_WAVE_DIRECTION) Expecting float parameter..", rulesParsed));
                                return null;
                            }

                            try
                            {
                                Vector3 vec2 = param_list.GetVector3Item(n++);
                                Vector2 wave2_direction = new Vector2(vec2.X, vec2.Y);
                                update.water["wave1_direction"] = wave2_direction;
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (WATER_WAVE_DIRECTION) Expecting float parameter..", rulesParsed));
                                return null;
                            }


                            break;
                        case ScriptBaseClass.SKY_AMBIENT:
                            if (remain < 1)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_AMBIENT) Expecting float parameter..", rulesParsed));
                                return null;
                            }

                            try
                            {
                                OSDMap legacy_haze_ambient = new OSDMap();

                                var vec = param_list.GetVector3Item(n++);
                                var srgb = llLinear2sRGB(vec);

                                legacy_haze_ambient["ambient"] = (Vector3)srgb;
                                update.sky["legacy_haze"] = legacy_haze_ambient;
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_AMBIENT) Expecting vector parameter..", rulesParsed));
                                return null;
                            }

                            break;
                        case ScriptBaseClass.SKY_BLUE:
                            if (remain < 2)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_BLUE) Expecting float parameter..", rulesParsed));
                                return null;
                            }

                            OSDMap legacy_haze_blue = new OSDMap();

                            try
                            {
                                var vec = param_list.GetVector3Item(n++);
                                var srgb = llLinear2sRGB(vec);
                                legacy_haze_blue["blue_density"] = (Vector3)srgb;
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_BLUE) Expecting vector parameter..", rulesParsed));
                                return null;
                            }

                            try
                            {
                                var vec = param_list.GetVector3Item(n++);
                                var srgb = llLinear2sRGB(vec);
                                legacy_haze_blue["blue_horizon"] = (Vector3)srgb;
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_BLUE) Expecting vector parameter..", rulesParsed));
                                return null;
                            }

                            update.sky["legacy_haze"] = legacy_haze_blue;

                            break;
                        case ScriptBaseClass.SKY_HAZE:
                            if (remain < 4)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_HAZE) Expecting float parameter..", rulesParsed));
                                return null;
                            }

                            OSDMap legacy_haze_haze = new OSDMap();

                            try
                            {
                                var val = param_list.GetFloatItem(n++);
                                legacy_haze_haze["haze_density"] = val * 10.0f;
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_HAZE) Expecting vector parameter..", rulesParsed));
                                return null;
                            }

                            try
                            {
                                var val = param_list.GetFloatItem(n++);
                                legacy_haze_haze["haze_horizon"] = val;
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_HAZE) Expecting vector parameter..", rulesParsed));
                                return null;
                            }

                            try
                            {
                                var val = param_list.GetFloatItem(n++);
                                legacy_haze_haze["density_multiplier"] = val / 1000.0f;
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_HAZE) Expecting vector parameter..", rulesParsed));
                                return null;
                            }

                            try
                            {
                                var val = param_list.GetFloatItem(n++);
                                legacy_haze_haze["distance_multiplier"] = val;
                            }
                            catch (InvalidCastException)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} (SKY_HAZE) Expecting vector parameter..", rulesParsed));
                                return null;
                            }

                            update.sky["legacy_haze"] = legacy_haze_haze;

                            break;
                        case ScriptBaseClass.SKY_REFLECTION_PROBE_AMBIANCE:
                            if (remain < 1)
                                error = true;

                            if (!error)
                            {
                                float fl = (float)param_list.GetLSLFloatItem(n++);
                                update.sky["reflection_probe_ambiance"] = Math.Clamp(fl, 0.0f, 10.0f);
                            }

                            if (error)
                            {
                                reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} -> SKY_REFLECTION_PROBE_AMBIANCE: arg #{1} - parameter 1 must be a float", rulesParsed, n - index - 1));
                            }

                            break;

                        default:
                            reportError("llSetAgentEnvironment", string.Format("Error running rule #{0} unknown rule id {1}.", rulesParsed, code));
                            break;
                    }
                }
            }
            catch
            {

            }

            return update;
        }
    }
}
