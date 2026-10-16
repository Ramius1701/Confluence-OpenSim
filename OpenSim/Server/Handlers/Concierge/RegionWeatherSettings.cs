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

using System;
using System.Globalization;
using System.Net;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Services.Interfaces;

namespace OpenSim.Server.Handlers.Concierge
{
    /// <summary>
    /// [Confluence] Where a region's weather switches live in the grid settings table: whether the region has
    /// weather at all, whether it changes by itself, and how many hours each weather lasts. A region owner sets them
    /// on My Regions; the weather module reads them from <see cref="RegionWeatherHandler"/>. A key is a kind plus a
    /// scope: a region's UUID for that region alone, or "default" for every region that has not set its own.
    /// </summary>
    public static class RegionWeatherKeys
    {
        public const string KindEnabled = "enabled";
        public const string KindAutoCycle = "autocycle";
        public const string KindCycleHours = "cyclehours";

        /// <summary>The shortest and longest time one weather may last, in hours.</summary>
        public const float MinCycleHours = 0.25f, MaxCycleHours = 24f;

        public static string Key(string kind, string scope) => "weather." + kind + "." + scope;

        public static string Key(string kind, UUID regionID) => Key(kind, regionID.ToString());

        /// <summary>The region's own value, else the grid-wide default, else empty (not set).</summary>
        public static string Resolve(IGridSettingsService settings, string kind, UUID regionID)
        {
            string own = settings.Get(Key(kind, regionID));
            if (!string.IsNullOrEmpty(own))
                return own;
            return settings.Get(Key(kind, ConciergeSettingKeys.Default)) ?? string.Empty;
        }

        /// <summary>Hours as stored ("1", "0.5"), clamped to the allowed range; empty when not a number.</summary>
        public static string NormalizeHours(string value)
        {
            if (string.IsNullOrWhiteSpace(value)
                || !float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float hours)
                || float.IsNaN(hours) || float.IsInfinity(hours))
                return string.Empty;
            hours = Math.Clamp(hours, MinCycleHours, MaxCycleHours);
            return hours.ToString("0.##", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// [Confluence] <c>GET /weather/&lt;region-uuid&gt;</c>: the region's weather switches as a small JSON object
    /// ({"enabled":"true", "autocycle":"false", "cyclehours":"1"}). A switch nobody has set is left out, so the region
    /// falls back to its own default. Only weather keys are read. Served by the concierge connector, which already
    /// holds the grid settings service, so Robust needs no new ini entry.
    /// </summary>
    public class RegionWeatherHandler : SimpleStreamHandler
    {
        private readonly IGridSettingsService m_settings;

        public RegionWeatherHandler(IGridSettingsService settings) : base("/weather")
        {
            m_settings = settings;
        }

        protected override void ProcessRequest(IOSHttpRequest httpRequest, IOSHttpResponse httpResponse)
        {
            if (httpRequest.HttpMethod != "GET")
            {
                httpResponse.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                return;
            }

            string path = httpRequest.UriPath ?? string.Empty;
            int slash = path.LastIndexOf('/');
            if (slash < 0 || !UUID.TryParse(path.Substring(slash + 1), out UUID regionID) || regionID.IsZero())
            {
                httpResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                return;
            }

            httpResponse.StatusCode = (int)HttpStatusCode.OK;
            httpResponse.ContentType = "application/json";
            httpResponse.RawBuffer = Util.UTF8.GetBytes(OSDParser.SerializeJsonString(Build(regionID)));
        }

        public OSDMap Build(UUID regionID)
        {
            var map = new OSDMap();
            foreach (string kind in new[] { RegionWeatherKeys.KindEnabled, RegionWeatherKeys.KindAutoCycle })
            {
                string flag = ConciergeSettingKeys.NormalizeFlag(RegionWeatherKeys.Resolve(m_settings, kind, regionID));
                if (flag.Length > 0)
                    map[kind] = OSD.FromString(flag);
            }
            string hours = RegionWeatherKeys.NormalizeHours(RegionWeatherKeys.Resolve(m_settings, RegionWeatherKeys.KindCycleHours, regionID));
            if (hours.Length > 0)
                map[RegionWeatherKeys.KindCycleHours] = OSD.FromString(hours);
            return map;
        }
    }
}
