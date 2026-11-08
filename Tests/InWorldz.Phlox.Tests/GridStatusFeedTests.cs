using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using OpenMetaverse;
using OpenSim.Server.Handlers.WebInterface;
using Xunit;

namespace InWorldz.Phlox.Tests;

/// <summary>
/// [Confluence] The grid status feed (2026-11-08): what viewers list in their grid status window. A change counts once
/// two checks in a row agree, dated from the first. An outage is one entry, Investigating then Resolved (same id, later
/// updated), in Second Life's status-page HTML; the document is Atom, the format Firestorm reads.
/// </summary>
public class GridStatusFeedTests
{
    private const string Link = "http://grid.example/gridstatus";
    private static readonly UUID A = UUID.Random(), B = UUID.Random();
    private static readonly DateTime T0 = new DateTime(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    private static Dictionary<UUID, string> Online(params UUID[] ids)
        => ids.ToDictionary(id => id, id => id == A ? "Alpha" : "Beta");

    private static List<GridStatusFeedEntry> Entries(GridStatusFeedTracker t, DateTime now) => t.Entries(now, Link);

    [Fact]
    public void TheFirstCheckOnlyRecordsWhatIsUp()
    {
        var t = new GridStatusFeedTracker();
        t.ObserveRegions(Online(A, B), T0);
        Assert.Empty(Entries(t, T0));
        Assert.Equal("2 regions online.", t.Summary());
    }

    [Fact]
    public void ARegionGoingDownOpensAnOutageOnTheSecondCheckDatedFromTheFirst()
    {
        var t = new GridStatusFeedTracker();
        t.ObserveRegions(Online(A, B), T0);
        t.ObserveRegions(Online(A), T0.AddMinutes(1));
        Assert.Empty(Entries(t, T0.AddMinutes(1)));

        t.ObserveRegions(Online(A), T0.AddMinutes(2));
        GridStatusFeedEntry e = Assert.Single(Entries(t, T0.AddMinutes(2)));
        Assert.Equal("Region Beta outage", e.Title);
        Assert.Equal(T0.AddMinutes(1), e.Updated);
        GridStatusFeedUpdate u = Assert.Single(e.Updates);
        Assert.Equal(GridStatusFeedTracker.Investigating, u.Status);
        Assert.Equal("1 region online.", t.Summary());
    }

    [Fact]
    public void OneMissedCheckIsNotAnOutage()
    {
        var t = new GridStatusFeedTracker();
        t.ObserveRegions(Online(A, B), T0);
        t.ObserveRegions(Online(A), T0.AddMinutes(1));
        t.ObserveRegions(Online(A, B), T0.AddMinutes(2));
        t.ObserveRegions(Online(A), T0.AddMinutes(3));      // the earlier miss does not carry over
        Assert.Empty(Entries(t, T0.AddMinutes(3)));
    }

    [Fact]
    public void TheRegionComingBackResolvesTheSameEntryWithALaterUpdated()
    {
        var t = new GridStatusFeedTracker();
        t.ObserveRegions(Online(A, B), T0);
        t.ObserveRegions(Online(A), T0.AddMinutes(1));
        t.ObserveRegions(Online(A), T0.AddMinutes(2));
        string id = Assert.Single(Entries(t, T0.AddMinutes(2))).Id;

        t.ObserveRegions(Online(A, B), T0.AddMinutes(10));
        t.ObserveRegions(Online(A, B), T0.AddMinutes(11));
        GridStatusFeedEntry e = Assert.Single(Entries(t, T0.AddMinutes(11)));
        Assert.Equal(id, e.Id);
        Assert.Equal(T0.AddMinutes(10), e.Updated);
        Assert.Equal(new[] { GridStatusFeedTracker.Resolved, GridStatusFeedTracker.Investigating },
                e.Updates.Select(u => u.Status));

        // a second outage of the same region is a new entry
        t.ObserveRegions(Online(A), T0.AddMinutes(20));
        t.ObserveRegions(Online(A), T0.AddMinutes(21));
        Assert.Equal(2, Entries(t, T0.AddMinutes(21)).Count);
    }

    [Fact]
    public void ARegionThatWasNotInAnOutageComingOnlineIsANotice()
    {
        var t = new GridStatusFeedTracker();
        t.ObserveRegions(Online(A), T0);
        t.ObserveRegions(Online(A, B), T0.AddMinutes(1));
        t.ObserveRegions(Online(A, B), T0.AddMinutes(2));
        GridStatusFeedEntry e = Assert.Single(Entries(t, T0.AddMinutes(2)));
        Assert.Equal("Region Beta is online", e.Title);
        Assert.Equal(string.Empty, Assert.Single(e.Updates).Status);
    }

    [Fact]
    public void ServicesFailingAndRecoveringAreOneOutageNamingTheFailedOnes()
    {
        var t = new GridStatusFeedTracker();
        t.ObserveServices(true, Array.Empty<string>(), T0);
        t.ObserveServices(false, new[] { "Search", "Events" }, T0.AddMinutes(5));
        Assert.Empty(Entries(t, T0.AddMinutes(5)));
        t.ObserveServices(false, new[] { "Search", "Events" }, T0.AddMinutes(10));
        GridStatusFeedEntry down = Assert.Single(Entries(t, T0.AddMinutes(10)));
        Assert.Equal("Grid services outage", down.Title);
        Assert.Equal("Not answering: Events, Search.", Assert.Single(down.Updates).Text);

        t.ObserveServices(true, Array.Empty<string>(), T0.AddMinutes(15));
        t.ObserveServices(true, Array.Empty<string>(), T0.AddMinutes(20));
        GridStatusFeedEntry over = Assert.Single(Entries(t, T0.AddMinutes(20)));
        Assert.Equal(down.Id, over.Id);
        Assert.Equal(GridStatusFeedTracker.Resolved, over.Updates[0].Status);
    }

    [Fact]
    public void StartingIsANoticeAndOldEntriesExpireButNotAnOngoingOutage()
    {
        var t = new GridStatusFeedTracker();
        t.Started("Test Grid", T0);
        t.ObserveRegions(Online(A, B), T0);
        t.ObserveRegions(Online(A), T0.AddMinutes(1));
        t.ObserveRegions(Online(A), T0.AddMinutes(2));
        Assert.Equal(new[] { "Region Beta outage", "Test Grid grid services started" },
                Entries(t, T0.AddMinutes(2)).Select(e => e.Title));
        Assert.Equal(new[] { "Region Beta outage" }, Entries(t, T0.AddDays(8)).Select(e => e.Title));
    }

    [Fact]
    public void TheContentIsTheStatusPageFormNewestFirst()
    {
        string html = GridStatusFeedTracker.UpdatesHtml(new[]
        {
            new GridStatusFeedUpdate(T0, GridStatusFeedTracker.Investigating, "Region <Beta> is offline."),
            new GridStatusFeedUpdate(T0.AddMinutes(9), GridStatusFeedTracker.Resolved, "Back online."),
        });
        Assert.Equal("<p><small>Nov 8, 2026 12:09 UTC</small><br><strong>Resolved</strong> - Back online.</p>" +
                     "<p><small>Nov 8, 2026 12:00 UTC</small><br><strong>Investigating</strong> - Region &lt;Beta&gt; is offline.</p>",
                html);
        Assert.Equal("<p><small>Nov 8, 2026 12:00 UTC</small><br>A notice.</p>",
                GridStatusFeedTracker.UpdatesHtml(new[] { new GridStatusFeedUpdate(T0, "", "A notice.") }));
    }

    [Fact]
    public void TheDocumentIsAtomNewestFirstWithAnIdAndUpdatedOnEveryEntry()
    {
        var entries = new List<GridStatusFeedEntry>();
        for (int i = 0; i < 40; i++)
            entries.Add(new GridStatusFeedEntry("urn:uuid:" + Guid.NewGuid(), "Entry " + i, Link,
                    new[] { new GridStatusFeedUpdate(T0.AddMinutes(i), "", "Text\u0001 " + i) }));

        string xml = GridStatusFeedTracker.BuildAtom("Test Grid grid status", Link + "/feed", Link, "2 regions online.",
                entries, 30, T0);

        XNamespace atom = "http://www.w3.org/2005/Atom";
        XDocument doc = XDocument.Parse(xml);
        Assert.Equal(atom + "feed", doc.Root!.Name);
        Assert.Equal("2026-11-08T12:39:00Z", doc.Root.Element(atom + "updated")!.Value);

        List<XElement> items = doc.Root.Elements(atom + "entry").ToList();
        Assert.Equal(30, items.Count);
        Assert.Equal("Entry 39", items[0].Element(atom + "title")!.Value);
        Assert.All(items, e =>
        {
            Assert.StartsWith("urn:uuid:", e.Element(atom + "id")!.Value);
            Assert.Matches(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$", e.Element(atom + "updated")!.Value);
            Assert.DoesNotContain('\u0001', e.Element(atom + "summary")!.Value);
            XElement content = e.Element(atom + "content")!;
            Assert.Equal("html", content.Attribute("type")!.Value);
            Assert.StartsWith("<p><small>", content.Value);
        });
    }
}
