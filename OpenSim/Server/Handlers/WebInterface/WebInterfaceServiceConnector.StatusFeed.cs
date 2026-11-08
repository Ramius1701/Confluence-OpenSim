using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Framework.Servers.HttpServer;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;

namespace OpenSim.Server.Handlers.WebInterface
{
    // [Confluence] (2026-11-08) The grid status feed viewers show (GridStatusRSS in [GridInfoService], passed to them
    // in SimulatorFeatures OpenSimExtras). Firestorm reads only Atom (each <entry>'s <id> and <updated>); Aurora reads
    // Atom or RSS and flashes its toolbar button on a new or changed entry. So: an Atom feed at /gridstatus/feed of
    // what changed - Robust starting, a public region going offline or coming back, grid services failing or
    // recovering - plus the grid's news posts. A minute-by-minute monitor watches for the changes, using the region
    // checks the pages already run (RegionStatus.cs); a change counts once it is seen on two checks in a row, so one
    // slow answer is not reported as an outage. Hidden regions never appear. The history is kept in memory for seven
    // days and starts again when Robust restarts (which is itself the first entry).
    public partial class WebInterfaceServiceConnector
    {
        private const int StatusFeedMonitorMs = 60_000;
        private const int StatusFeedServicesEvery = 5;          // service checks every 5th tick: they query the database
        private const int StatusFeedMaxEntries = 30;
        private const int StatusFeedNewsCount = 5;

        private readonly GridStatusFeedTracker m_statusFeed = new GridStatusFeedTracker();
        private System.Threading.Timer m_statusFeedTimer;
        private int m_statusFeedTicks;
        private int m_statusFeedRunning;

        private void StartGridStatusFeed()
        {
            m_statusFeed.Started(GetSetting("GridName", m_gridName), DateTime.UtcNow);
            if (m_GridService != null)
                m_statusFeedTimer = new System.Threading.Timer(_ => GridStatusFeedTick(), null, 15_000, StatusFeedMonitorMs);
        }

        private void GridStatusFeedTick()
        {
            if (System.Threading.Interlocked.Exchange(ref m_statusFeedRunning, 1) == 1)
                return;
            try
            {
                DateTime now = DateTime.UtcNow;
                List<GridRegion> listed = FilterListedRegions(m_GridService.GetRegionRange(UUID.Zero, 0, 2000000, 0, 2000000));
                var online = new Dictionary<UUID, string>();
                bool allChecked = true;
                foreach (GridRegion region in listed)
                {
                    IsRegionOnlineCached(region);                // keeps the background check watching it
                    if (!TryGetCheckedRegionStatus(region.RegionID, out bool alive))
                        allChecked = false;
                    else if (alive)
                        online[region.RegionID] = region.RegionName;
                }
                if (allChecked)                                  // a region not checked yet is neither up nor down
                    m_statusFeed.ObserveRegions(online, now);

                if (m_statusFeedTicks++ % StatusFeedServicesEvery == 0)
                {
                    GridStatusStats stats = ComputeGridStatusStats();
                    m_statusFeed.ObserveServices(stats.ServicesOk, stats.ServiceErrors.Keys, now);
                }
            }
            catch (Exception e)
            {
                m_log.WarnFormat("[WEB INTERFACE]: grid status feed check failed: {0}", e.Message);
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref m_statusFeedRunning, 0);
            }
        }

        private static bool TryGetCheckedRegionStatus(UUID regionID, out bool alive)
        {
            alive = false;
            if (!s_regionStatus.TryGetValue(regionID, out RegionStatusEntry entry) || entry.CheckedMs == 0)
                return false;
            alive = entry.Alive;
            return true;
        }

        private void HandleGridStatusFeed(IOSHttpRequest request, IOSHttpResponse response)
        {
            string site = m_publicBaseUrl + BasePath;
            DateTime now = DateTime.UtcNow;
            var entries = new List<GridStatusFeedEntry>();
            foreach (GridStatusFeedEvent ev in m_statusFeed.Events(now))
                entries.Add(new GridStatusFeedEntry("urn:uuid:" + ev.Id, ev.Title, ev.Text, ev.When, site + "/gridstatus"));

            if (m_NewsService != null)
            {
                try
                {
                    foreach (NewsItem item in m_NewsService.GetNews(0, StatusFeedNewsCount))
                        entries.Add(new GridStatusFeedEntry("urn:uuid:" + item.ID, item.Title, item.Body,
                                DateTime.SpecifyKind(item.Date, DateTimeKind.Utc), site + "/"));
                }
                catch (Exception e)
                {
                    m_log.WarnFormat("[WEB INTERFACE]: grid status feed could not read the news: {0}", e.Message);
                }
            }

            string gridName = GetSetting("GridName", m_gridName);
            string xml = GridStatusFeedTracker.BuildAtom(gridName + " grid status", site + "/gridstatus/feed",
                    site + "/gridstatus", m_statusFeed.Summary(), entries, StatusFeedMaxEntries, now);

            response.ContentType = "application/atom+xml; charset=utf-8";
            response.AddHeader("Cache-Control", "no-cache");
            response.RawBuffer = Encoding.UTF8.GetBytes(xml);
        }
    }

    public sealed class GridStatusFeedEvent
    {
        public Guid Id = Guid.NewGuid();
        public DateTime When;
        public string Title;
        public string Text;
    }

    public sealed class GridStatusFeedEntry
    {
        public readonly string Id, Title, Text, Link;
        public readonly DateTime Updated;

        public GridStatusFeedEntry(string id, string title, string text, DateTime updated, string link)
        {
            Id = id;
            Title = title ?? string.Empty;
            Text = text ?? string.Empty;
            Updated = updated;
            Link = link;
        }
    }

    /// <summary>
    /// What the grid status feed reports: changes seen on two checks in a row, newest first, for seven days.
    /// </summary>
    public sealed class GridStatusFeedTracker
    {
        public const int MaxEvents = 50;
        public static readonly TimeSpan Keep = TimeSpan.FromDays(7);

        private readonly object m_lock = new object();
        private readonly List<GridStatusFeedEvent> m_events = new List<GridStatusFeedEvent>();
        private Dictionary<UUID, string> m_online;                          // null until the first full check
        private readonly Dictionary<UUID, (string Name, DateTime Since)> m_comingUp = new Dictionary<UUID, (string, DateTime)>();
        private readonly Dictionary<UUID, DateTime> m_goingDown = new Dictionary<UUID, DateTime>();
        private bool? m_servicesOk;
        private DateTime? m_servicesChangingSince;

        public void Started(string gridName, DateTime now)
        {
            Add(now, gridName + " grid services started",
                    "The grid's central services (logins, inventory, assets, search) have started. Regions come online over the next few minutes.");
        }

        /// <summary>The public regions answering right now. The first call only records them.</summary>
        public void ObserveRegions(IDictionary<UUID, string> online, DateTime now)
        {
            lock (m_lock)
            {
                if (m_online == null)
                {
                    m_online = new Dictionary<UUID, string>(online);
                    return;
                }

                foreach (KeyValuePair<UUID, string> kv in online)
                {
                    m_goingDown.Remove(kv.Key);
                    if (m_online.ContainsKey(kv.Key))
                    {
                        m_online[kv.Key] = kv.Value;                 // renamed regions keep their latest name
                        continue;
                    }
                    if (m_comingUp.TryGetValue(kv.Key, out var pending))
                    {
                        m_comingUp.Remove(kv.Key);
                        m_online[kv.Key] = kv.Value;
                        Add(pending.Since, "Region " + kv.Value + " is online", "The region " + kv.Value + " is up and accepting visitors.");
                    }
                    else
                    {
                        m_comingUp[kv.Key] = (kv.Value, now);
                    }
                }
                foreach (UUID id in m_comingUp.Keys.Where(id => !online.ContainsKey(id)).ToList())
                    m_comingUp.Remove(id);

                foreach (UUID id in m_online.Keys.Where(id => !online.ContainsKey(id)).ToList())
                {
                    if (m_goingDown.TryGetValue(id, out DateTime since))
                    {
                        string name = m_online[id];
                        m_goingDown.Remove(id);
                        m_online.Remove(id);
                        Add(since, "Region " + name + " is offline", "The region " + name + " is not answering. Teleports to it will fail until it is back.");
                    }
                    else
                    {
                        m_goingDown[id] = now;
                    }
                }
            }
        }

        /// <summary>Whether every grid service answered; <paramref name="failing"/> names the ones that did not.</summary>
        public void ObserveServices(bool ok, IEnumerable<string> failing, DateTime now)
        {
            lock (m_lock)
            {
                if (m_servicesOk == null)
                {
                    m_servicesOk = ok;
                    if (!ok)
                        Add(now, "Some grid services are not answering", ServicesText(failing));
                    return;
                }
                if (ok == m_servicesOk.Value)
                {
                    m_servicesChangingSince = null;
                    return;
                }
                if (m_servicesChangingSince == null)
                {
                    m_servicesChangingSince = now;
                    return;
                }
                DateTime since = m_servicesChangingSince.Value;
                m_servicesChangingSince = null;
                m_servicesOk = ok;
                if (ok)
                    Add(since, "All grid services are answering again", "Every grid service is answering normally again.");
                else
                    Add(since, "Some grid services are not answering", ServicesText(failing));
            }
        }

        private static string ServicesText(IEnumerable<string> failing)
        {
            string names = string.Join(", ", (failing ?? Enumerable.Empty<string>()).OrderBy(n => n, StringComparer.Ordinal));
            return names.Length == 0 ? "One or more grid services are not answering." : "Not answering: " + names + ".";
        }

        /// <summary>One line on the grid now, for the feed's subtitle.</summary>
        public string Summary()
        {
            lock (m_lock)
            {
                if (m_online == null)
                    return "Checking regions.";
                string regions = m_online.Count == 1 ? "1 region online" : m_online.Count + " regions online";
                return m_servicesOk == false ? regions + "; some grid services are not answering." : regions + ".";
            }
        }

        /// <summary>The changes of the last seven days, newest first.</summary>
        public List<GridStatusFeedEvent> Events(DateTime now)
        {
            lock (m_lock)
            {
                m_events.RemoveAll(e => now - e.When > Keep);
                return m_events.OrderByDescending(e => e.When).ToList();
            }
        }

        private void Add(DateTime when, string title, string text)
        {
            lock (m_lock)
            {
                m_events.Add(new GridStatusFeedEvent { When = when, Title = title, Text = text });
                if (m_events.Count > MaxEvents)
                    m_events.RemoveRange(0, m_events.Count - MaxEvents);   // oldest first: Add is called in time order
            }
        }

        /// <summary>An Atom 1.0 document of the newest <paramref name="max"/> entries.</summary>
        public static string BuildAtom(string title, string selfUrl, string pageUrl, string subtitle,
                IEnumerable<GridStatusFeedEntry> entries, int max, DateTime now)
        {
            List<GridStatusFeedEntry> list = entries.OrderByDescending(e => e.Updated).Take(max).ToList();
            DateTime updated = list.Count > 0 ? list[0].Updated : now;

            var sb = new StringBuilder();
            var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false), OmitXmlDeclaration = false };
            using (var sw = new Utf8StringWriter(sb))
            using (XmlWriter w = XmlWriter.Create(sw, settings))
            {
                const string ns = "http://www.w3.org/2005/Atom";
                w.WriteStartDocument();
                w.WriteStartElement("feed", ns);
                w.WriteElementString("id", ns, selfUrl);
                w.WriteElementString("title", ns, title);
                w.WriteElementString("subtitle", ns, subtitle);
                w.WriteElementString("updated", ns, AtomDate(updated));
                Link(w, ns, "self", selfUrl);
                Link(w, ns, "alternate", pageUrl);
                foreach (GridStatusFeedEntry e in list)
                {
                    w.WriteStartElement("entry", ns);
                    w.WriteElementString("id", ns, e.Id);
                    w.WriteElementString("title", ns, Clean(e.Title));
                    w.WriteElementString("updated", ns, AtomDate(e.Updated));
                    if (!string.IsNullOrEmpty(e.Link))
                        Link(w, ns, "alternate", e.Link);
                    w.WriteStartElement("summary", ns);
                    w.WriteAttributeString("type", "text");
                    w.WriteString(Clean(e.Text));
                    w.WriteEndElement();
                    w.WriteStartElement("author", ns);
                    w.WriteElementString("name", ns, title);
                    w.WriteEndElement();
                    w.WriteEndElement();
                }
                w.WriteEndElement();
                w.WriteEndDocument();
            }
            return sb.ToString();
        }

        private static void Link(XmlWriter w, string ns, string rel, string href)
        {
            w.WriteStartElement("link", ns);
            w.WriteAttributeString("rel", rel);
            w.WriteAttributeString("href", href);
            w.WriteEndElement();
        }

        private static string AtomDate(DateTime when)
            => when.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        // Characters XML 1.0 cannot carry at all (control codes from pasted news text) would make the writer throw.
        private static string Clean(string s)
            => new string((s ?? string.Empty).Where(c => c == '\t' || c == '\n' || c == '\r' || c >= ' ').ToArray());

        private sealed class Utf8StringWriter : StringWriter
        {
            public Utf8StringWriter(StringBuilder sb) : base(sb, CultureInfo.InvariantCulture) { }
            public override Encoding Encoding => Encoding.UTF8;
        }
    }
}
