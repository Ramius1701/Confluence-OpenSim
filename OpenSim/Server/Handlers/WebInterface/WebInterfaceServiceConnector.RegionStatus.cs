using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenMetaverse;
using GridRegion = OpenSim.Services.Interfaces.GridRegion;

namespace OpenSim.Server.Handlers.WebInterface
{
    // [Confluence] Which registered regions are really up, for the pages that show it (welcome, home, the region list
    // and region pages, My Regions). A crashed or killed region stays registered, so the pages check each one; they
    // used to do it live, on every page load, and a region that was down or still starting cost up to 3.5 s (2 s for
    // Windows to refuse a closed loopback port, then the public address), so the welcome page Firestorm loads at
    // start-up took 3 s or more whenever a region was down. Now a background job checks every region the pages have
    // asked about, every 30 s, and the pages read its last answer. A region it has not checked yet (just after Robust
    // starts, or newly registered) counts as up until its first check, moments later. Admin actions (start, stop,
    // restart) still check live: they need the truth now, not a minute ago.
    public partial class WebInterfaceServiceConnector
    {
        private sealed class RegionStatusEntry
        {
            public GridRegion Region;
            public volatile bool Alive = true;
            public long CheckedMs;
            public long LastAskedMs;
        }

        private const int RegionStatusRefreshMs = 30_000;
        private const int RegionStatusForgetMs = 600_000;
        private const int RegionStatusProbeTimeoutMs = 1500;

        private static readonly ConcurrentDictionary<UUID, RegionStatusEntry> s_regionStatus = new ConcurrentDictionary<UUID, RegionStatusEntry>();
        private static Timer s_regionStatusTimer;
        private static int s_regionStatusRefreshing;
        private static readonly object s_regionStatusTimerLock = new object();

        /// <summary>Whether the region is up, from the last background check; true for one not checked yet.</summary>
        private static bool IsRegionOnlineCached(GridRegion region)
        {
            if (region == null)
                return false;

            long now = Environment.TickCount64;
            RegionStatusEntry entry = s_regionStatus.GetOrAdd(region.RegionID, _ => new RegionStatusEntry());
            entry.Region = region;                 // the latest registration (its port or address may have changed)
            entry.LastAskedMs = now;

            EnsureRegionStatusTimer();
            if (entry.CheckedMs == 0)
                s_regionStatusTimer?.Change(250, RegionStatusRefreshMs);   // first sight: check soon (one pass for a page's worth)
            return entry.Alive;
        }

        private static List<GridRegion> FilterOnlineRegions(List<GridRegion> regions)
        {
            if (regions == null || regions.Count == 0)
                return new List<GridRegion>();
            return regions.Where(IsRegionOnlineCached).ToList();
        }

        private static void EnsureRegionStatusTimer()
        {
            if (s_regionStatusTimer != null)
                return;
            lock (s_regionStatusTimerLock)
                s_regionStatusTimer ??= new Timer(_ => RefreshRegionStatus(), null, RegionStatusRefreshMs, RegionStatusRefreshMs);
        }

        /// <summary>Checks every region a page asked about in the last 10 minutes, in parallel; forgets the rest.</summary>
        private static void RefreshRegionStatus()
        {
            if (Interlocked.Exchange(ref s_regionStatusRefreshing, 1) == 1)
                return;
            try
            {
                long now = Environment.TickCount64;
                foreach (KeyValuePair<UUID, RegionStatusEntry> kv in s_regionStatus)
                    if (now - kv.Value.LastAskedMs > RegionStatusForgetMs)
                        s_regionStatus.TryRemove(kv.Key, out _);

                RegionStatusEntry[] entries = s_regionStatus.Values.Where(e => e.Region != null).ToArray();
                if (entries.Length == 0)
                    return;

                var probes = new System.Threading.Tasks.Task<bool>[entries.Length];
                for (int i = 0; i < entries.Length; i++)
                {
                    GridRegion region = entries[i].Region;
                    probes[i] = System.Threading.Tasks.Task.Run(() => IsRegionAlive(region, RegionStatusProbeTimeoutMs));
                }
                // IsRegionAlive may try loopback then the public address, each with the full timeout.
                System.Threading.Tasks.Task.WaitAll(probes, RegionStatusProbeTimeoutMs * 2 + 1000);

                long checkedAt = Environment.TickCount64;
                for (int i = 0; i < entries.Length; i++)
                {
                    entries[i].Alive = probes[i].IsCompletedSuccessfully && probes[i].Result;
                    entries[i].CheckedMs = checkedAt;
                }
            }
            catch (Exception e)
            {
                m_log.WarnFormat("[WEB INTERFACE]: region status refresh failed: {0}", e.Message);
            }
            finally
            {
                Interlocked.Exchange(ref s_regionStatusRefreshing, 0);
            }
        }
    }
}
