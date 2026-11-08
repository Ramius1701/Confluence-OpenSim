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
/// two checks in a row agree, dated from the first; the document is Atom, the format Firestorm reads.
/// </summary>
public class GridStatusFeedTests
{
    private static readonly UUID A = UUID.Random(), B = UUID.Random();
    private static readonly DateTime T0 = new DateTime(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    private static Dictionary<UUID, string> Online(params UUID[] ids)
        => ids.ToDictionary(id => id, id => id == A ? "Alpha" : "Beta");

    private static List<string> Titles(GridStatusFeedTracker t, DateTime now) => t.Events(now).Select(e => e.Title).ToList();

    [Fact]
    public void TheFirstCheckOnlyRecordsWhatIsUp()
    {
        var t = new GridStatusFeedTracker();
        t.ObserveRegions(Online(A, B), T0);
        Assert.Empty(t.Events(T0));
        Assert.Equal("2 regions online.", t.Summary());
    }

    [Fact]
    public void ARegionGoingDownIsReportedOnTheSecondCheckDatedFromTheFirst()
    {
        var t = new GridStatusFeedTracker();
        t.ObserveRegions(Online(A, B), T0);
        t.ObserveRegions(Online(A), T0.AddMinutes(1));
        Assert.Empty(t.Events(T0.AddMinutes(1)));

        t.ObserveRegions(Online(A), T0.AddMinutes(2));
        GridStatusFeedEvent e = Assert.Single(t.Events(T0.AddMinutes(2)));
        Assert.Equal("Region Beta is offline", e.Title);
        Assert.Equal(T0.AddMinutes(1), e.When);
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
        Assert.Empty(t.Events(T0.AddMinutes(3)));
    }

    [Fact]
    public void ARegionComingBackIsReportedTheSameWay()
    {
        var t = new GridStatusFeedTracker();
        t.ObserveRegions(Online(A), T0);
        t.ObserveRegions(Online(A, B), T0.AddMinutes(1));
        Assert.Empty(t.Events(T0.AddMinutes(1)));
        t.ObserveRegions(Online(A, B), T0.AddMinutes(2));
        GridStatusFeedEvent e = Assert.Single(t.Events(T0.AddMinutes(2)));
        Assert.Equal("Region Beta is online", e.Title);
        Assert.Equal(T0.AddMinutes(1), e.When);
    }

    [Fact]
    public void ServicesFailingAndRecoveringAreReportedNamingTheFailedOnes()
    {
        var t = new GridStatusFeedTracker();
        t.ObserveServices(true, Array.Empty<string>(), T0);
        t.ObserveServices(false, new[] { "Search", "Events" }, T0.AddMinutes(5));
        Assert.Empty(t.Events(T0.AddMinutes(5)));
        t.ObserveServices(false, new[] { "Search", "Events" }, T0.AddMinutes(10));
        GridStatusFeedEvent down = Assert.Single(t.Events(T0.AddMinutes(10)));
        Assert.Equal("Some grid services are not answering", down.Title);
        Assert.Equal("Not answering: Events, Search.", down.Text);

        t.ObserveServices(true, Array.Empty<string>(), T0.AddMinutes(15));
        t.ObserveServices(true, Array.Empty<string>(), T0.AddMinutes(20));
        Assert.Equal(new[] { "All grid services are answering again", "Some grid services are not answering" },
                Titles(t, T0.AddMinutes(20)));
    }

    [Fact]
    public void StartingIsAnEntryAndOldEntriesExpireAfterSevenDays()
    {
        var t = new GridStatusFeedTracker();
        t.Started("Test Grid", T0);
        Assert.Equal(new[] { "Test Grid grid services started" }, Titles(t, T0));
        Assert.Empty(t.Events(T0.AddDays(8)));
    }

    [Fact]
    public void TheDocumentIsAtomNewestFirstWithAnIdAndUpdatedOnEveryEntry()
    {
        var entries = new List<GridStatusFeedEntry>();
        for (int i = 0; i < 40; i++)
            entries.Add(new GridStatusFeedEntry("urn:uuid:" + Guid.NewGuid(), "Entry " + i, "Text\u0001 " + i, T0.AddMinutes(i), "http://grid.example/gridstatus"));

        string xml = GridStatusFeedTracker.BuildAtom("Test Grid grid status", "http://grid.example/gridstatus/feed",
                "http://grid.example/gridstatus", "2 regions online.", entries, 30, T0);

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
        });
    }
}
