// [Confluence] Tranquillity #302 adds regions.viewerLocalAddress for MySQL and PostgreSQL; Confluence also supports
// SQLite, so its GridStore gets migration 2. This runs the SQLite grid store's migrations on a fresh database and
// round-trips the column through the store the grid service uses.

using System.Collections.Generic;
using System.IO;
using OpenMetaverse;
using OpenSim.Data;
using OpenSim.Data.SQLite;
using Xunit;

namespace InWorldz.Phlox.Tests;

public class ViewerLocalAddressSqliteStoreTests
{
    private static RegionData Region(UUID id, string localAddress)
    {
        var data = new RegionData
        {
            RegionID = id,
            ScopeID = UUID.Zero,
            RegionName = "Sqlite Test",
            posX = 256000,
            posY = 256000,
            sizeX = 256,
            sizeY = 256,
            Data = new Dictionary<string, object>
            {
                ["serverIP"] = "127.0.0.1",
                ["serverPort"] = "9000",
                ["serverURI"] = "http://127.0.0.1:9000/",
                ["Token"] = "",
            },
        };
        if (localAddress is not null)
            data.Data["viewerLocalAddress"] = localAddress;
        return data;
    }

    [Fact]
    public void AFreshSqliteGridStoreHasTheColumnAndKeepsTheAddress()
    {
        string file = Path.Combine(Path.GetTempPath(), "confluence-gridstore-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var store = new SQLiteRegionData("URI=file:" + file, "regions");
            UUID withAddress = UUID.Random(), without = UUID.Random();

            Assert.True(store.Store(Region(withAddress, "192.0.2.20")));
            Assert.True(store.Store(Region(without, null)));

            RegionData got = store.Get(withAddress, UUID.Zero);
            Assert.NotNull(got);
            Assert.Equal("192.0.2.20", got.Data["viewerLocalAddress"]?.ToString());

            // A region that registers none reads back empty, which GridRegion treats as "none".
            RegionData none = store.Get(without, UUID.Zero);
            Assert.True(string.IsNullOrEmpty(none.Data.TryGetValue("viewerLocalAddress", out object v) ? v?.ToString() : null));

            // Clearing it (what the grid service writes when a region stops registering one) sticks.
            Assert.True(store.Store(Region(withAddress, string.Empty)));
            Assert.True(string.IsNullOrEmpty(store.Get(withAddress, UUID.Zero).Data["viewerLocalAddress"]?.ToString()));
        }
        finally
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { File.Delete(file); } catch { }
        }
    }
}
