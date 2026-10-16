using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using OpenMetaverse;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Server.Handlers.Concierge;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;

namespace OpenSim.Server.Handlers.WebInterface
{
    // [Confluence] Region weather in the web portal: a region's owner (or a grid administrator) decides whether the
    // region has weather, whether it changes by itself, and how long each weather lasts. Stored in the grid settings
    // table (RegionWeatherKeys) and read by the region's weather module through RegionWeatherHandler, so a change
    // reaches the region within about a minute with no restart. A region whose owner has not turned weather on has
    // none (the module's grid-wide [Weather] Enabled only makes the module available).
    public partial class WebInterfaceServiceConnector
    {
        private string WeatherSetting(string kind, UUID regionID)
            => m_GridSettingsService?.Get(RegionWeatherKeys.Key(kind, regionID)) ?? string.Empty;

        /// <summary>The My Regions table's one-word state for a region: On, Off or (not set) Off.</summary>
        private string WeatherSummary(UUID regionID)
        {
            if (m_GridSettingsService == null)
                return "Edit";
            string enabled = ConciergeSettingKeys.NormalizeFlag(RegionWeatherKeys.Resolve(m_GridSettingsService, RegionWeatherKeys.KindEnabled, regionID));
            return enabled == "true" ? "On" : "Off";
        }

        private void HandleMyRegionsWeather(IOSHttpRequest request, IOSHttpResponse response)
        {
            WebSession session = GetSession(request);
            if (session == null)
            {
                response.Redirect(BasePath + "/login", HttpStatusCode.Redirect);
                return;
            }

            if (m_GridSettingsService == null)
            {
                response.Redirect(BasePath + "/myregions?message=" + Uri.EscapeDataString("Grid settings are not available, so weather can't be changed here."), HttpStatusCode.Redirect);
                return;
            }

            GridRegion region = null;
            if (UUID.TryParse(request.QueryString.Get("region") ?? string.Empty, out UUID regionID))
                region = GetConciergeRegion(session, regionID);   // the owner, or a grid administrator
            if (region == null)
            {
                response.Redirect(BasePath + "/myregions?message=" + Uri.EscapeDataString("Region not found or not owned by you."), HttpStatusCode.Redirect);
                return;
            }

            bool enabled = ConciergeSettingKeys.NormalizeFlag(RegionWeatherKeys.Resolve(m_GridSettingsService, RegionWeatherKeys.KindEnabled, regionID)) == "true";
            bool autoCycle = ConciergeSettingKeys.NormalizeFlag(RegionWeatherKeys.Resolve(m_GridSettingsService, RegionWeatherKeys.KindAutoCycle, regionID)) != "false";
            string hours = RegionWeatherKeys.NormalizeHours(RegionWeatherKeys.Resolve(m_GridSettingsService, RegionWeatherKeys.KindCycleHours, regionID));
            if (hours.Length == 0)
                hours = "1";

            string message = request.QueryString.Get("message");
            var sb = new StringBuilder();
            sb.Append("<h1><i class=\"bi bi-cloud-rain\"></i> Weather: ").Append(Html(region.RegionName)).Append("</h1>");
            sb.Append("<p><a href=\"").Append(BasePath).Append("/myregions\">Back to My Regions</a></p>");
            if (!string.IsNullOrEmpty(message))
                sb.Append("<p>").Append(Html(message)).Append("</p>");

            sb.Append("<p>Rain, snow, storms with lightning and thunder, and the sky that goes with them. Off until you turn it on. ")
              .Append("Changes reach the region within about a minute - no restart needed. The sky changes only for the people in the ")
              .Append("region while the weather lasts; the region's own environment settings are never changed.</p>");

            sb.Append("<form method=\"post\" action=\"").Append(BasePath).Append("/myregions/weather/save\">");
            sb.Append("<input type=\"hidden\" name=\"region_id\" value=\"").Append(region.RegionID).Append("\">");
            sb.Append("<table>");
            sb.Append("<tr><td>Weather in this region</td><td><select name=\"enabled\">")
              .Append("<option value=\"true\"").Append(enabled ? " selected" : "").Append(">On</option>")
              .Append("<option value=\"false\"").Append(!enabled ? " selected" : "").Append(">Off</option>")
              .Append("</select></td></tr>");
            sb.Append("<tr><td>Change the weather by itself</td><td><select name=\"autocycle\">")
              .Append("<option value=\"true\"").Append(autoCycle ? " selected" : "").Append(">Yes</option>")
              .Append("<option value=\"false\"").Append(!autoCycle ? " selected" : "").Append(">No - only when an estate manager asks</option>")
              .Append("</select></td></tr>");
            sb.Append("<tr><td>Hours each weather lasts</td><td><input type=\"number\" name=\"cyclehours\" min=\"")
              .Append(RegionWeatherKeys.MinCycleHours.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("\" max=\"")
              .Append(RegionWeatherKeys.MaxCycleHours.ToString(System.Globalization.CultureInfo.InvariantCulture))
              .Append("\" step=\"0.25\" value=\"").Append(Html(hours)).Append("\"></td></tr>");
            sb.Append("</table><p><button type=\"submit\">Save</button></p></form>");

            sb.Append("<h2>In-world commands</h2><p class=\"news-meta\">Estate managers can type on channel 89 (unless your grid changed it): ")
              .Append("<code>/89 weather rain</code>, <code>storm</code>, <code>snow</code>, <code>blizzard</code>, <code>sunny</code>, ")
              .Append("<code>clear</code> and <code>status</code>. <code>clear</code> pauses the automatic changes until another weather is asked for.</p>");

            WritePage(request, response, PageTitle("Weather: " + region.RegionName), sb.ToString());
        }

        private void HandleMyRegionsWeatherSave(IOSHttpRequest request, IOSHttpResponse response)
        {
            WebSession session = GetSession(request);
            if (session == null)
            {
                response.StatusCode = (int)HttpStatusCode.Forbidden;
                return;
            }

            string message = "Region not found or not owned by you.";
            string redirect = BasePath + "/myregions";

            if (request.HttpMethod == "POST" && m_GridSettingsService != null)
            {
                Dictionary<string, string> form = ReadForm(request);
                if (UUID.TryParse(FormValue(form, "region_id"), out UUID regionID) && GetConciergeRegion(session, regionID) != null)
                {
                    string scope = regionID.ToString();
                    m_GridSettingsService.Set(RegionWeatherKeys.Key(RegionWeatherKeys.KindEnabled, scope),
                        ConciergeSettingKeys.NormalizeFlag(FormValue(form, "enabled")));
                    m_GridSettingsService.Set(RegionWeatherKeys.Key(RegionWeatherKeys.KindAutoCycle, scope),
                        ConciergeSettingKeys.NormalizeFlag(FormValue(form, "autocycle")));
                    m_GridSettingsService.Set(RegionWeatherKeys.Key(RegionWeatherKeys.KindCycleHours, scope),
                        RegionWeatherKeys.NormalizeHours(FormValue(form, "cyclehours")));

                    message = "Weather settings saved. The region picks them up within about a minute.";
                    redirect = BasePath + "/myregions/weather?region=" + regionID;
                }
            }

            response.Redirect(redirect + (redirect.Contains('?') ? "&" : "?") + "message=" + Uri.EscapeDataString(message), HttpStatusCode.Redirect);
        }
    }
}
