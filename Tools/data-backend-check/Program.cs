using System;
using System.Collections.Generic;
using OpenMetaverse;
using OpenSim.Data;
using OpenSim.Framework;

static class Program
{
    static int checks = 0, failures = 0;

    static void Eq(string name, object expected, object actual)
    {
        checks++;
        if (!Equals(expected, actual))
        {
            failures++;
            Console.WriteLine("FAIL " + name + "\n   expected: " + expected + "\n   actual:   " + actual);
        }
    }

    static MarketplaceListing Make(UUID seller, string title, int? stock, bool listed, int price)
    {
        return new MarketplaceListing
        {
            SellerID = seller,
            Title = title,
            Description = "Description of " + title + " with unicode é中 and 'quotes'",
            Price = price,
            CountOnHand = stock,
            IsListed = listed,
            SnapshotFolderID = UUID.Random(),
            ListingFolderID = UUID.Random(),
            VersionFolderID = UUID.Random(),
            SnapshotFingerprint = "fp-" + title,
            Created = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc),
            Updated = new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc)
        };
    }

    static void Market(string label, IMarketplaceListingsData d)
    {
        UUID seller = UUID.Random(), other = UUID.Random();

        Eq(label + " get missing listing is null", null, d.GetListing(999999));

        MarketplaceListing a = Make(seller, "A finite", 2, true, 50);
        int idA = d.InsertListing(a);
        Eq(label + " insert returns positive id", true, idA > 0);
        MarketplaceListing got = d.GetListing(idA);
        Eq(label + " round trip title", a.Title, got.Title);
        Eq(label + " round trip description (unicode, quotes)", a.Description, got.Description);
        Eq(label + " round trip price", 50, got.Price);
        Eq(label + " round trip stock", (int?)2, got.CountOnHand);
        Eq(label + " round trip listed", true, got.IsListed);
        Eq(label + " round trip seller", seller, got.SellerID);
        Eq(label + " round trip folders", a.SnapshotFolderID, got.SnapshotFolderID);
        Eq(label + " round trip fingerprint", "fp-A finite", got.SnapshotFingerprint);
        Eq(label + " round trip created", a.Created, got.Created);
        Eq(label + " round trip updated", a.Updated, got.Updated);

        MarketplaceListing u = Make(seller, "B unlimited", null, false, 10);
        int idU = d.InsertListing(u);
        Eq(label + " unlimited stock stored as null", (int?)null, d.GetListing(idU).CountOnHand);
        Eq(label + " unlisted stored as false", false, d.GetListing(idU).IsListed);

        int idO = d.InsertListing(Make(other, "C other seller", 5, true, 1));
        Eq(label + " by-seller returns only that seller's", 2, d.GetListingsBySeller(seller).Count);
        Eq(label + " by-seller other", 1, d.GetListingsBySeller(other).Count);

        List<MarketplaceListing> listed = d.GetListedListings(0, 10);
        Eq(label + " listed excludes unlisted", 2, listed.Count);
        Eq(label + " listed paging count", 1, d.GetListedListings(0, 1).Count);
        Eq(label + " listed paging start", 1, d.GetListedListings(1, 10).Count);

        // update
        got.Title = "A renamed";
        got.Price = 75;
        got.CountOnHand = 3;
        got.SnapshotFingerprint = "fp-new";
        got.Updated = new DateTime(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc);
        Eq(label + " update reports success", true, d.UpdateListing(got));
        MarketplaceListing again = d.GetListing(idA);
        Eq(label + " update title", "A renamed", again.Title);
        Eq(label + " update price", 75, again.Price);
        Eq(label + " update stock", (int?)3, again.CountOnHand);
        Eq(label + " update keeps Created", a.Created, again.Created);
        got.ID = 424242;
        Eq(label + " update missing reports false", false, d.UpdateListing(got));

        // listed state
        Eq(label + " set listed false", true, d.SetListedState(idA, false));
        Eq(label + " set listed false stored", false, d.GetListing(idA).IsListed);
        Eq(label + " set listed missing", false, d.SetListedState(777777, true));
        Eq(label + " set listed true", true, d.SetListedState(idA, true));

        // stock: finite (3 units)
        Eq(label + " reserve 1", true, d.TryReserveStock(idA));
        Eq(label + " reserve 2", true, d.TryReserveStock(idA));
        Eq(label + " reserve 3", true, d.TryReserveStock(idA));
        Eq(label + " reserve at zero refused", false, d.TryReserveStock(idA));
        Eq(label + " stock is zero", (int?)0, d.GetListing(idA).CountOnHand);
        d.ReleaseStock(idA);
        Eq(label + " release gives one back", (int?)1, d.GetListing(idA).CountOnHand);
        Eq(label + " reserve after release", true, d.TryReserveStock(idA));
        Eq(label + " stock back to zero", (int?)0, d.GetListing(idA).CountOnHand);

        // stock: unlimited but unlisted, then listed
        Eq(label + " unlimited unlisted refused", false, d.TryReserveStock(idU));
        d.SetListedState(idU, true);
        Eq(label + " unlimited listed always ok", true, d.TryReserveStock(idU));
        Eq(label + " unlimited listed always ok again", true, d.TryReserveStock(idU));
        Eq(label + " unlimited stays null", (int?)null, d.GetListing(idU).CountOnHand);
        d.ReleaseStock(idU);
        Eq(label + " release on unlimited is a no-op", (int?)null, d.GetListing(idU).CountOnHand);
        Eq(label + " reserve missing refused", false, d.TryReserveStock(31337));

        // unlisted finite refused
        d.SetListedState(idO, false);
        Eq(label + " unlisted finite refused", false, d.TryReserveStock(idO));
        Eq(label + " unlisted finite unchanged", (int?)5, d.GetListing(idO).CountOnHand);

        // deliveries
        DeliveryReceipt r;
        Eq(label + " get missing delivery", false, d.TryGetDelivery("nope", out r));
        DeliveryReceipt rec = new DeliveryReceipt
        {
            DeliveryId = "delivery-" + Guid.NewGuid().ToString("N"),
            SellerId = seller.ToString(),
            SnapshotFolderId = UUID.Random().ToString(),
            RecipientId = other.ToString(),
            SnapshotFingerprint = "fingerprint",
            DestinationFolderId = UUID.Random().ToString(),
            ItemCount = 7,
            FolderCount = 2
        };
        Eq(label + " insert delivery", true, d.TryInsertDelivery(rec));
        Eq(label + " duplicate delivery id refused", false, d.TryInsertDelivery(rec));
        Eq(label + " get delivery", true, d.TryGetDelivery(rec.DeliveryId, out r));
        Eq(label + " delivery seller", rec.SellerId, r.SellerId);
        Eq(label + " delivery recipient", rec.RecipientId, r.RecipientId);
        Eq(label + " delivery fingerprint", "fingerprint", r.SnapshotFingerprint);
        Eq(label + " delivery destination", rec.DestinationFolderId, r.DestinationFolderId);
        Eq(label + " delivery item count", 7, r.ItemCount);
        Eq(label + " delivery folder count", 2, r.FolderCount);
        Eq(label + " delivery timestamp parses", true, DateTime.TryParse(r.TimestampUtc, out _));

        // a second instance over the same database finds the tables already migrated
    }

    // Many buyers racing for a few units: exactly that many must win, never more (no overselling).
    static void Race(string label, IMarketplaceListingsData d)
    {
        int id = d.InsertListing(Make(UUID.Random(), "Race", 5, true, 1));
        int wins = 0;
        System.Threading.Tasks.Parallel.For(0, 60, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 16 }, i =>
        {
            if (d.TryReserveStock(id))
                System.Threading.Interlocked.Increment(ref wins);
        });
        Eq(label + " race: exactly 5 of 60 buyers win", 5, wins);
        Eq(label + " race: stock ends at zero, never negative", (int?)0, d.GetListing(id).CountOnHand);
    }

    static OfflineIMData Im(UUID to, UUID from, string text)
    {
        OfflineIMData m = new OfflineIMData();
        m.PrincipalID = to;
        m.FromID = from;
        m.Data = new Dictionary<string, string>();
        m.Data["Message"] = text;
        return m;
    }

    // ageSql runs a raw statement to make one message look 20 days old (per database).
    static void OfflineIM(string label, IOfflineIMData d, Action<string> ageMessage)
    {
        UUID me = UUID.Random(), other = UUID.Random(), from1 = UUID.Random(), from2 = UUID.Random();
        string text = "<GridInstantMessage><message>Hello é中 'quoted' &amp; \"double\" \U0001F600</message></GridInstantMessage>";

        Eq(label + " im: unknown principal has none", 0, d.Get("PrincipalID", me.ToString()).Length);
        Eq(label + " im: unknown principal count", 0L, d.GetCount("PrincipalID", me.ToString()));

        Eq(label + " im: store 1", true, d.Store(Im(me, from1, text)));
        Eq(label + " im: store 2", true, d.Store(Im(me, from2, "second")));
        Eq(label + " im: store 3", true, d.Store(Im(me, from1, "third")));
        Eq(label + " im: store other principal", true, d.Store(Im(other, from1, "for someone else")));

        OfflineIMData[] mine = d.Get("PrincipalID", me.ToString());
        Eq(label + " im: get returns own 3", 3, mine.Length);
        Eq(label + " im: count own", 3L, d.GetCount("PrincipalID", me.ToString()));
        Eq(label + " im: count other", 1L, d.GetCount("PrincipalID", other.ToString()));
        Eq(label + " im: count by sender", 2L, d.GetCount("FromID", from1.ToString()) - 1L); // 3 stored from1 in total: 2 to me + 1 to other
        long bad;
        try { bad = d.GetCount("PrincipalID; drop table im_offline", "x"); } catch (Exception) { bad = 0; }
        Eq(label + " im: bad column name is refused safely (none, or an error)", 0L, bad);
        Eq(label + " im: table still there after that", 3L, d.GetCount("PrincipalID", me.ToString()));

        bool sawText = false, allHaveId = true, principalOk = true;
        foreach (OfflineIMData m in mine)
        {
            if (m.Data.ContainsKey("Message") && m.Data["Message"] == text)
                sawText = true;
            if (!m.Data.ContainsKey("ID") || string.IsNullOrEmpty(m.Data["ID"]))
                allHaveId = false;
            if (m.PrincipalID != me)
                principalOk = false;
        }
        Eq(label + " im: unicode/quotes/emoji message round trips", true, sawText);
        Eq(label + " im: every message has its own ID", true, allHaveId);
        Eq(label + " im: principal id read back", true, principalOk);

        // delete exactly one message by ID, scoped to the principal
        string id0 = mine[0].Data["ID"];
        Eq(label + " im: delete with someone else's principal removes nothing", false,
           d.Delete(new string[] { "ID", "PrincipalID" }, new string[] { id0, other.ToString() }));
        Eq(label + " im: still 3 after wrong-principal delete", 3, d.Get("PrincipalID", me.ToString()).Length);
        Eq(label + " im: delete one by id+principal", true,
           d.Delete(new string[] { "ID", "PrincipalID" }, new string[] { id0, me.ToString() }));
        Eq(label + " im: 2 left after deleting one", 2, d.Get("PrincipalID", me.ToString()).Length);
        Eq(label + " im: other principal untouched", 1, d.Get("PrincipalID", other.ToString()).Length);

        // DeleteOld drops messages older than two weeks, keeps recent ones
        string oldId = d.Get("PrincipalID", me.ToString())[0].Data["ID"];
        ageMessage(oldId);
        d.DeleteOld();
        OfflineIMData[] afterOld = d.Get("PrincipalID", me.ToString());
        Eq(label + " im: DeleteOld removed the 20-day-old message", 1, afterOld.Length);
        Eq(label + " im: DeleteOld kept the recent one", true, afterOld[0].Data["ID"] != oldId);
        Eq(label + " im: DeleteOld left the other principal's recent message", 1, d.Get("PrincipalID", other.ToString()).Length);

        // delete all for a principal
        Eq(label + " im: delete all for principal", true, d.Delete("PrincipalID", me.ToString()));
        Eq(label + " im: none left", 0, d.Get("PrincipalID", me.ToString()).Length);
        Eq(label + " im: delete when none left reports false", false, d.Delete("PrincipalID", me.ToString()));
    }

    static Dictionary<string, string> Dict(params string[] kv)
    {
        Dictionary<string, string> m = new Dictionary<string, string>();
        for (int i = 0; i + 1 < kv.Length; i += 2)
            m[kv[i]] = kv[i + 1];
        return m;
    }

    // ageInvite makes one invitation look 20 days old (raw SQL, per database).
    static void Groups(string label, IGroupsData d, Action<string> ageInvite)
    {
        string tag = Guid.NewGuid().ToString("N").Substring(0, 8);
        UUID gid = UUID.Random(), gid2 = UUID.Random(), ownerRole = UUID.Random(), memberRole = UUID.Random();
        UUID founder = UUID.Random();
        string alice = UUID.Random().ToString(), bob = UUID.Random().ToString();
        string tokenBob = UUID.Random().ToString(); // the access token is a UUID string (a uuid column on PostgreSQL)
        string name = "Explorers " + tag;
        string charter = "We explore é中 'quotes' \"double\" \U0001F600 and more";

        Eq(label + " grp: unknown group is null", null, d.RetrieveGroup(UUID.Random()));

        Eq(label + " grp: store group", true, d.StoreGroup(new GroupData
        {
            GroupID = gid,
            Data = Dict("Location", "", "Name", name, "Charter", charter, "InsigniaID", UUID.Random().ToString(),
                        "FounderID", founder.ToString(), "MembershipFee", "25", "OpenEnrollment", "1", "ShowInList", "1",
                        "AllowPublish", "1", "MaturePublish", "0", "OwnerRoleID", ownerRole.ToString())
        }));
        Eq(label + " grp: store hidden group", true, d.StoreGroup(new GroupData
        {
            GroupID = gid2,
            Data = Dict("Location", "", "Name", "Hidden " + tag, "Charter", "secret", "InsigniaID", UUID.Zero.ToString(),
                        "FounderID", founder.ToString(), "MembershipFee", "0", "OpenEnrollment", "0", "ShowInList", "0",
                        "AllowPublish", "0", "MaturePublish", "0", "OwnerRoleID", UUID.Random().ToString())
        }));

        GroupData g = d.RetrieveGroup(gid);
        Eq(label + " grp: by id name", name, g.Data["Name"]);
        Eq(label + " grp: charter (unicode, quotes, emoji)", charter, g.Data["Charter"]);
        Eq(label + " grp: fee", "25", g.Data["MembershipFee"]);
        Eq(label + " grp: founder", founder.ToString(), g.Data["FounderID"]);
        Eq(label + " grp: owner role", ownerRole.ToString(), g.Data["OwnerRoleID"]);
        Eq(label + " grp: by name", gid, d.RetrieveGroup(name).GroupID);
        Eq(label + " grp: unknown name is null", null, d.RetrieveGroup("No such group " + tag));

        Eq(label + " grp: search by substring finds it", true, Contains(d.RetrieveGroups("plorers " + tag), gid));
        Eq(label + " grp: search hides ShowInList=0", false, Contains(d.RetrieveGroups("Hidden " + tag), gid2));
        Eq(label + " grp: search with an apostrophe does not break", 0, d.RetrieveGroups("O'Brien " + tag + "'; drop table x; --").Length);
        Eq(label + " grp: injection attempt matches nothing extra", 0, d.RetrieveGroups("zzz%' OR '1'='1").Length);
        Eq(label + " grp: table survived the injection attempts", true, Contains(d.RetrieveGroups("plorers " + tag), gid));
        Eq(label + " grp: empty search returns listed groups", true, Contains(d.RetrieveGroups(""), gid));
        Eq(label + " grp: count includes both", true, d.GroupsCount() >= 2);

        // update group (replace)
        g.Data["Charter"] = "New charter";
        Eq(label + " grp: update", true, d.StoreGroup(g));
        Eq(label + " grp: update stored", "New charter", d.RetrieveGroup(gid).Data["Charter"]);

        // roles (powers as a big flag set)
        string powers = "9223372036854775807";
        Eq(label + " role: store owner", true, d.StoreRole(new RoleData { GroupID = gid, RoleID = ownerRole,
            Data = Dict("Name", "Owners", "Description", "The owners é", "Title", "Owner", "Powers", powers) }));
        Eq(label + " role: store member", true, d.StoreRole(new RoleData { GroupID = gid, RoleID = memberRole,
            Data = Dict("Name", "Members", "Description", "", "Title", "Member", "Powers", "1234") }));
        Eq(label + " role: retrieve keeps exact powers", powers, d.RetrieveRole(gid, ownerRole).Data["Powers"]);
        Eq(label + " role: retrieve description unicode", "The owners é", d.RetrieveRole(gid, ownerRole).Data["Description"]);
        Eq(label + " role: unknown role null", null, d.RetrieveRole(gid, UUID.Random()));
        Eq(label + " role: list", 2, d.RetrieveRoles(gid).Length);
        Eq(label + " role: count", 2, d.RoleCount(gid));

        // membership
        Eq(label + " mem: store alice", true, d.StoreMember(new MembershipData { GroupID = gid, PrincipalID = alice,
            Data = Dict("SelectedRoleID", ownerRole.ToString(), "Contribution", "5", "ListInProfile", "1", "AcceptNotices", "1", "AccessToken", "") }));
        Eq(label + " mem: store bob", true, d.StoreMember(new MembershipData { GroupID = gid, PrincipalID = bob,
            Data = Dict("SelectedRoleID", memberRole.ToString(), "Contribution", "0", "ListInProfile", "0", "AcceptNotices", "0", "AccessToken", tokenBob) }));
        Eq(label + " mem: members", 2, d.RetrieveMembers(gid).Length);
        Eq(label + " mem: count", 2, d.MemberCount(gid));
        MembershipData ma = d.RetrieveMember(gid, alice);
        Eq(label + " mem: alice contribution", "5", ma.Data["Contribution"]);
        Eq(label + " mem: alice selected role", ownerRole.ToString(), ma.Data["SelectedRoleID"]);
        Eq(label + " mem: bob accept notices off", "0", d.RetrieveMember(gid, bob).Data["AcceptNotices"]);
        Eq(label + " mem: bob token", tokenBob, d.RetrieveMember(gid, bob).Data["AccessToken"]);
        Eq(label + " mem: memberships of alice", 1, d.RetrieveMemberships(alice).Length);
        Eq(label + " mem: unknown member null", null, d.RetrieveMember(gid, UUID.Random().ToString()));

        // role membership
        Eq(label + " rm: store alice owner", true, d.StoreRoleMember(new RoleMembershipData { GroupID = gid, RoleID = ownerRole, PrincipalID = alice }));
        Eq(label + " rm: store alice member", true, d.StoreRoleMember(new RoleMembershipData { GroupID = gid, RoleID = memberRole, PrincipalID = alice }));
        Eq(label + " rm: store bob member", true, d.StoreRoleMember(new RoleMembershipData { GroupID = gid, RoleID = memberRole, PrincipalID = bob }));
        Eq(label + " rm: all in group", 3, d.RetrieveRolesMembers(gid).Length);
        Eq(label + " rm: members of member role", 2, d.RetrieveRoleMembers(gid, memberRole).Length);
        Eq(label + " rm: count of member role", 2, d.RoleMemberCount(gid, memberRole));
        Eq(label + " rm: roles of alice", 2, d.RetrieveMemberRoles(gid, alice).Length);
        Eq(label + " rm: one specific", true, d.RetrieveRoleMember(gid, ownerRole, alice) != null);
        Eq(label + " rm: specific missing is null", null, d.RetrieveRoleMember(gid, ownerRole, bob));
        Eq(label + " rm: delete one", true, d.DeleteRoleMember(new RoleMembershipData { GroupID = gid, RoleID = memberRole, PrincipalID = bob }));
        Eq(label + " rm: delete all roles of alice", true, d.DeleteMemberAllRoles(gid, alice));
        Eq(label + " rm: none left", 0, d.RetrieveRolesMembers(gid).Length);

        // principals (active group)
        Eq(label + " pr: store", true, d.StorePrincipal(new PrincipalData { PrincipalID = alice, ActiveGroupID = gid }));
        Eq(label + " pr: retrieve", gid, d.RetrievePrincipal(alice).ActiveGroupID);
        Eq(label + " pr: unknown null", null, d.RetrievePrincipal(UUID.Random().ToString()));
        d.StorePrincipal(new PrincipalData { PrincipalID = bob, ActiveGroupID = gid2 });

        // invites
        UUID inv1 = UUID.Random(), inv2 = UUID.Random();
        string carol = UUID.Random().ToString(), dave = UUID.Random().ToString();
        Eq(label + " inv: store", true, d.StoreInvitation(new InvitationData { InviteID = inv1, GroupID = gid, RoleID = memberRole, PrincipalID = carol, Data = new Dictionary<string, string>() }));
        Eq(label + " inv: store second", true, d.StoreInvitation(new InvitationData { InviteID = inv2, GroupID = gid, RoleID = memberRole, PrincipalID = dave, Data = new Dictionary<string, string>() }));
        Eq(label + " inv: by id", carol, d.RetrieveInvitation(inv1).PrincipalID);
        Eq(label + " inv: by group and principal", inv2, d.RetrieveInvitation(gid, dave).InviteID);
        Eq(label + " inv: unknown null", null, d.RetrieveInvitation(UUID.Random()));
        ageInvite(inv1.ToString());
        d.DeleteOldInvites();
        Eq(label + " inv: DeleteOld removed the 20-day-old invite", null, d.RetrieveInvitation(inv1));
        Eq(label + " inv: DeleteOld kept the fresh one", true, d.RetrieveInvitation(inv2) != null);
        Eq(label + " inv: delete", true, d.DeleteInvite(inv2));
        Eq(label + " inv: gone", null, d.RetrieveInvitation(inv2));

        // notices
        UUID n1 = UUID.Random(), n2 = UUID.Random();
        int now = (int)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
        Eq(label + " ntc: store recent", true, d.StoreNotice(new NoticeData { GroupID = gid, NoticeID = n1,
            Data = Dict("TMStamp", now.ToString(), "FromName", "Alice é", "Subject", "Meeting 中", "Message", "See you 'there' \U0001F600",
                        "HasAttachment", "0", "AttachmentType", "0", "AttachmentName", "", "AttachmentItemID", UUID.Zero.ToString(), "AttachmentOwnerID", "") }));
        Eq(label + " ntc: store 20-day-old", true, d.StoreNotice(new NoticeData { GroupID = gid, NoticeID = n2,
            Data = Dict("TMStamp", (now - 20 * 86400).ToString(), "FromName", "Old", "Subject", "Old news", "Message", "old",
                        "HasAttachment", "1", "AttachmentType", "6", "AttachmentName", "thing", "AttachmentItemID", UUID.Random().ToString(), "AttachmentOwnerID", alice) }));
        Eq(label + " ntc: two notices", 2, d.RetrieveNotices(gid).Length);
        NoticeData nn = d.RetrieveNotice(n1);
        Eq(label + " ntc: subject unicode", "Meeting 中", nn.Data["Subject"]);
        Eq(label + " ntc: message quotes and emoji", "See you 'there' \U0001F600", nn.Data["Message"]);
        Eq(label + " ntc: timestamp kept", now.ToString(), nn.Data["TMStamp"]);
        Eq(label + " ntc: attachment fields", "6", d.RetrieveNotice(n2).Data["AttachmentType"]);
        d.DeleteOldNotices();
        Eq(label + " ntc: DeleteOld dropped the old one", null, d.RetrieveNotice(n2));
        Eq(label + " ntc: DeleteOld kept the recent one", true, d.RetrieveNotice(n1) != null);

        // bans
        Eq(label + " ban: store", true, d.StoreBan(new BanData { GroupID = gid, BannedID = carol, BanDate = 1790000000 }));
        Eq(label + " ban: retrieve", 1790000000, d.RetrieveBan(gid, carol).BanDate);
        Eq(label + " ban: list", 1, d.RetrieveBans(gid).Length);
        Eq(label + " ban: unknown null", null, d.RetrieveBan(gid, dave));
        Eq(label + " ban: delete", true, d.DeleteBan(gid, carol));
        Eq(label + " ban: none left", 0, d.RetrieveBans(gid).Length);

        // delete role and member, then cascade-delete the group
        Eq(label + " role: delete one", true, d.DeleteRole(gid, memberRole));
        Eq(label + " role: one left", 1, d.RoleCount(gid));
        Eq(label + " mem: delete bob", true, d.DeleteMember(gid, bob));
        Eq(label + " mem: one left", 1, d.MemberCount(gid));

        Eq(label + " grp: delete group", true, d.DeleteGroup(gid));
        Eq(label + " grp: group gone", null, d.RetrieveGroup(gid));
        Eq(label + " cascade: members gone", 0, d.RetrieveMembers(gid).Length);
        Eq(label + " cascade: roles gone", 0, d.RetrieveRoles(gid).Length);
        Eq(label + " cascade: notices gone", 0, d.RetrieveNotices(gid).Length);
        Eq(label + " cascade: alice's active group cleared", null, d.RetrievePrincipal(alice));
        Eq(label + " cascade: bob (other group) untouched", gid2, d.RetrievePrincipal(bob).ActiveGroupID);
        Eq(label + " grp: other group still there", true, d.RetrieveGroup(gid2) != null);
    }

    static bool Contains(GroupData[] list, UUID id)
    {
        foreach (GroupData g in list)
            if (g.GroupID == id)
                return true;
        return false;
    }

    static void RegionHG(string label, IRegionHGData d)
    {
        UUID a = UUID.Random(), b = UUID.Random();
        Eq(label + " unknown region -> null", null, d.GetIsOpen(a));
        d.SetIsOpen(a, false);
        Eq(label + " set closed", (bool?)false, d.GetIsOpen(a));
        d.SetIsOpen(a, true);
        Eq(label + " upsert to open", (bool?)true, d.GetIsOpen(a));
        d.SetIsOpen(a, false);
        Eq(label + " upsert to closed again", (bool?)false, d.GetIsOpen(a));
        d.SetIsOpen(b, true);
        Eq(label + " other region independent", (bool?)true, d.GetIsOpen(b));
        Eq(label + " first region unchanged", (bool?)false, d.GetIsOpen(a));
    }

    static void FSAssets(string label, IFSAssetDataPlugin d)
    {
        string idA = UUID.Random().ToString();
        string idB = UUID.Random().ToString();
        const string hashA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string hashB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

        Eq(label + " get missing asset is null", null, d.Get(idA, out string missingHash));

        AssetMetadata a = new AssetMetadata
        {
            ID = idA,
            FullID = new UUID(idA),
            Type = (sbyte)AssetType.Texture,
            Flags = AssetFlags.Normal
        };
        Eq(label + " store new returns true", true, d.Store(a, hashA));

        AssetMetadata got = d.Get(idA, out string gotHash);
        Eq(label + " round trip hash", hashA, gotHash);
        Eq(label + " round trip id", idA, got.ID);
        Eq(label + " round trip type", (sbyte)AssetType.Texture, got.Type);
        Eq(label + " round trip flags", AssetFlags.Normal, got.Flags);

        // A row with no data at all (the empty-texture/empty-IAR-file case) is never correct:
        // a later store of real data for the same ID must heal it in place, not be ignored as
        // "already exists".
        AssetMetadata b = new AssetMetadata { ID = idB, FullID = new UUID(idB), Type = (sbyte)AssetType.Texture, Flags = AssetFlags.Normal };
        d.Store(b, FSAssetHashes.Empty);
        Eq(label + " empty-hash row stored", FSAssetHashes.Empty, d.Get(idB, out string emptyHash) != null ? emptyHash : null);
        d.Store(b, hashB);
        d.Get(idB, out string healedHash);
        Eq(label + " empty-hash row healed by a later real store", hashB, healedHash);

        bool[] exist = d.AssetsExist(new[] { new UUID(idA), new UUID(idB), UUID.Random() });
        Eq(label + " AssetsExist a", true, exist[0]);
        Eq(label + " AssetsExist b", true, exist[1]);
        Eq(label + " AssetsExist missing", false, exist[2]);

        Eq(label + " AssetsExist empty array", 0, d.AssetsExist(Array.Empty<UUID>()).Length);

        Eq(label + " count at least 2", true, d.Count() >= 2);

        Eq(label + " delete returns true", true, d.Delete(idB));
        Eq(label + " deleted asset is gone", null, d.Get(idB, out string afterDeleteHash));
    }

    // Runs a check body, counting an exception as a failure (several of the bugs
    // these cover threw rather than returning a wrong value).
    static void NoThrow(string name, Action body)
    {
        try { body(); }
        catch (Exception e)
        {
            checks++;
            failures++;
            Console.WriteLine("FAIL " + name + " threw " + e.GetType().Name + ": " + e.Message);
        }
    }

    // Region extra settings (Scene.StoreExtraSetting). SQLite used to drop them:
    // SaveExtra/RemoveExtra did nothing and GetExtra returned null.
    static void RegionExtra(string label, OpenSim.Data.SQLite.SQLiteSimulationData d)
    {
        UUID a = UUID.Random(), b = UUID.Random();
        Dictionary<string, string> none = d.GetExtra(a);
        Eq(label + " extra: unknown region gives an empty dictionary, not null", 0, none is null ? -1 : none.Count);
        d.SaveExtra(a, "flag", "on");
        d.SaveExtra(a, "note", "it's \"quoted\" ünïcode");
        Eq(label + " extra: round trip", "on", d.GetExtra(a).TryGetValue("flag", out string v1) ? v1 : null);
        Eq(label + " extra: quotes and unicode", "it's \"quoted\" ünïcode", d.GetExtra(a).TryGetValue("note", out string v2) ? v2 : null);
        d.SaveExtra(a, "flag", "off");
        Eq(label + " extra: save again replaces", "off", d.GetExtra(a)["flag"]);
        Eq(label + " extra: two settings", 2, d.GetExtra(a).Count);
        d.SaveExtra(b, "flag", "other");
        Eq(label + " extra: other region independent", "off", d.GetExtra(a)["flag"]);
        d.RemoveExtra(a, "flag");
        Eq(label + " extra: remove", false, d.GetExtra(a).ContainsKey("flag"));
        Eq(label + " extra: remove leaves the rest", true, d.GetExtra(a).ContainsKey("note"));
        Eq(label + " extra: remove leaves other region", "other", d.GetExtra(b)["flag"]);
    }

    // A region still loads when some of its rows cannot be read (Tranquillity #508, #539, #540, #541): an item row
    // that cannot be built is left out (the prim keeps its other items), unreadable dynamic attributes leave the prim
    // without them, and a NULL prim name is read with a default (MySQL) or leaves out that child (PostgreSQL).
    // sql(table, column, literal, idColumn, id) sets one column of one row to an SQL literal.
    static void RegionLoad(string label, OpenSim.Region.Framework.Interfaces.ISimulationDataStore d,
        Action<string, string, string, string, UUID> sql, bool nullName, bool badDynAttrs, int childrenAfterNullName,
        string itemColumn = "flags", Func<OpenSim.Region.Framework.Interfaces.ISimulationDataStore> reopen = null)
    {
        UUID region = UUID.Random(), owner = UUID.Random();
        var a = new OpenSim.Region.Framework.Scenes.SceneObjectGroup(owner, new Vector3(10, 10, 25), PrimitiveBaseShape.CreateBox());
        a.RootPart.Name = "object a";
        var b = new OpenSim.Region.Framework.Scenes.SceneObjectGroup(owner, new Vector3(20, 20, 25), PrimitiveBaseShape.CreateBox());
        b.RootPart.Name = "object b";
        var child = new OpenSim.Region.Framework.Scenes.SceneObjectPart(owner, PrimitiveBaseShape.CreateBox(), new Vector3(20, 20, 26), Quaternion.Identity, Vector3.Zero);
        child.Name = "child of b";
        b.AddPart(child);
        d.StoreObject(a, region);
        d.StoreObject(b, region);

        TaskInventoryItem Item(string name) => new TaskInventoryItem
        {
            ItemID = UUID.Random(), AssetID = UUID.Random(), Name = name, Description = name, InvType = 7, Type = 7,
            OwnerID = owner, CreatorID = owner, ParentID = a.RootPart.UUID, ParentPartID = a.RootPart.UUID,
            BasePermissions = 0x7fffffff, CurrentPermissions = 0x7fffffff, NextPermissions = 0x7fffffff,
            EveryonePermissions = 0, GroupPermissions = 0
        };
        TaskInventoryItem good = Item("good note"), bad = Item("bad note");
        d.StorePrimInventory(a.RootPart.UUID, new List<TaskInventoryItem> { good, bad });

        Eq(label + " load: both objects before damage", 2, d.LoadObjects(region).Count);

        sql("primitems", itemColumn, "-1", "itemID", bad.ItemID);
        if (nullName)
            sql("prims", "Name", "NULL", "UUID", child.UUID);
        if (badDynAttrs)
            sql("prims", "DynAttrs", "'<llsd><integer>5</integer></llsd>'", "UUID", a.RootPart.UUID);

        List<OpenSim.Region.Framework.Scenes.SceneObjectGroup> loaded = null;
        // a store that keeps its tables in memory (SQLite) sees the damage only when opened again, as at a restart
        var after = reopen is null ? d : reopen();
        NoThrow(label + " load: a region with damaged rows loads", () => loaded = after.LoadObjects(region));
        if (loaded is null)
            return;
        Eq(label + " load: both objects after damage", 2, loaded.Count);
        var la = loaded.Find(g => g.UUID == a.UUID);
        var lb = loaded.Find(g => g.UUID == b.UUID);
        Eq(label + " load: the unreadable item is left out, the other kept", "good note",
            la is null ? "(object missing)" : string.Join(",", la.RootPart.Inventory.GetInventoryItems().ConvertAll(i => i.Name)));
        if (nullName)
            Eq(label + " load: linkset parts after a NULL child name", childrenAfterNullName, lb is null ? -1 : lb.PrimCount);
    }

    // An estate's linked regions. SQLite's GetRegions always returned an empty list.
    static void EstateRegions(string label, OpenSim.Data.SQLite.SQLiteEstateStore d)
    {
        int e1 = 7001, e2 = 7002;
        UUID r1 = UUID.Random(), r2 = UUID.Random(), r3 = UUID.Random();
        Eq(label + " estate: no regions yet", 0, d.GetRegions(e1).Count);
        d.LinkRegion(r1, e1);
        d.LinkRegion(r2, e1);
        d.LinkRegion(r3, e2);
        List<UUID> got = d.GetRegions(e1);
        Eq(label + " estate: two linked regions", 2, got.Count);
        Eq(label + " estate: has first", true, got.Contains(r1));
        Eq(label + " estate: has second", true, got.Contains(r2));
        Eq(label + " estate: other estate's region excluded", false, got.Contains(r3));
        Eq(label + " estate: other estate", 1, d.GetRegions(e2).Count);
    }

    // SetDataItem threw (the :UUID parameter was never added) and named the
    // column unquoted although the auth columns are quoted mixed-case.
    static void AuthSetDataItem(string label, OpenSim.Data.PGSQL.PGSQLAuthenticationData d)
    {
        UUID u1 = UUID.Random(), u2 = UUID.Random();
        foreach (UUID u in new[] { u1, u2 })
        {
            AuthenticationData ad = new AuthenticationData { PrincipalID = u, Data = new Dictionary<string, object>() };
            ad.Data["passwordHash"] = "h";
            ad.Data["passwordSalt"] = "s";
            ad.Data["webLoginKey"] = UUID.Zero.ToString();
            ad.Data["accountType"] = "UserAccount";
            d.Store(ad);
        }
        string key = UUID.Random().ToString();
        bool ok = false;
        NoThrow(label + " auth: SetDataItem", () => ok = d.SetDataItem(u1, "webLoginKey", key));
        Eq(label + " auth: SetDataItem returns true", true, ok);
        Eq(label + " auth: value written", key, d.Get(u1)?.Data["webLoginKey"]?.ToString());
        Eq(label + " auth: other user unchanged", UUID.Zero.ToString(), d.Get(u2)?.Data["webLoginKey"]?.ToString());
    }

    // MoveItem and MoveFolder named the quoted mixed-case columns unquoted, so
    // PostgreSQL folded them and the update failed (Tranquillity fixed only items).
    static void InventoryMoves(string label, OpenSim.Data.PGSQL.PGSQLXInventoryData d)
    {
        UUID owner = UUID.Random();
        UUID root = UUID.Random(), fa = UUID.Random(), fb = UUID.Random(), sub = UUID.Random(), item = UUID.Random();
        XInventoryFolder Folder(UUID id, UUID parent, string name) =>
            new XInventoryFolder { folderID = id, parentFolderID = parent, agentID = owner, folderName = name, type = -1, version = 1 };
        d.StoreFolder(Folder(root, UUID.Zero, "My Inventory"));
        d.StoreFolder(Folder(fa, root, "A"));
        d.StoreFolder(Folder(fb, root, "B"));
        d.StoreFolder(Folder(sub, fa, "Sub"));
        d.StoreItem(new XInventoryItem
        {
            inventoryID = item, parentFolderID = fa, avatarID = owner, assetID = UUID.Random(),
            assetType = 7, inventoryName = "note", inventoryDescription = "", creatorID = owner.ToString()
        });

        bool movedItem = false, movedFolder = false;
        NoThrow(label + " inventory: MoveItem", () => movedItem = d.MoveItem(item.ToString(), fb.ToString()));
        Eq(label + " inventory: MoveItem returns true", true, movedItem);
        XInventoryItem[] items = d.GetItems(new[] { "inventoryID" }, new[] { item.ToString() });
        Eq(label + " inventory: item now in B", fb, items.Length == 1 ? items[0].parentFolderID : UUID.Zero);

        NoThrow(label + " inventory: MoveFolder", () => movedFolder = d.MoveFolder(sub.ToString(), fb.ToString()));
        Eq(label + " inventory: MoveFolder returns true", true, movedFolder);
        XInventoryFolder[] folders = d.GetFolders(new[] { "folderID" }, new[] { sub.ToString() });
        Eq(label + " inventory: folder now in B", fb, folders.Length == 1 ? folders[0].parentFolderID : UUID.Zero);
    }

    // Lookups that take text from a request into a LIKE match (Tranquillity #519, #522, #525): the text must be
    // bound, never written into the statement, and its % and _ must match only themselves.
    static void LikeLookups(string label, IGridUserData gu, IFriendsData fr, IRegionData rg)
    {
        string tag = UUID.Random().ToString();
        string hg = tag + ";http://grid.example:8002/;Visitor Name";
        string other = UUID.Random().ToString();

        Eq(label + " griduser: store", true, gu.Store(new GridUserData { UserID = tag }));
        Eq(label + " griduser: store HG id", true, gu.Store(new GridUserData { UserID = hg }));
        Eq(label + " griduser: store other", true, gu.Store(new GridUserData { UserID = other }));
        Eq(label + " griduser: prefix finds both rows", 2, gu.GetAll(tag).Length);
        Eq(label + " griduser: quote in the id does not break", 0, gu.GetAll("x' OR '1'='1").Length);
        Eq(label + " griduser: statement in the id does not run", 0, gu.GetAll("';delete from GridUser;--").Length);
        Eq(label + " griduser: rows survived", 2, gu.GetAll(tag).Length);
        Eq(label + " griduser: % matches only itself", 0, gu.GetAll("%").Length);
        Eq(label + " griduser: _ matches only itself", 0, gu.GetAll(new string('_', 36)).Length);
        Eq(label + " griduser: empty id lists everyone", true, gu.GetAll("").Length >= 3);

        FriendsData f = new FriendsData { PrincipalID = hg, Friend = other, Data = new Dictionary<string, string> { ["Flags"] = "1" } };
        Eq(label + " friends: store HG friendship", true, fr.Store(f));
        // Only the MySQL store matches the text id as a prefix; SQLite and PostgreSQL match it exactly (as upstream).
        // PostgreSQL takes only a bare uuid here and finds no Hypergrid row at all.
        if (label != "pgsql")
            Eq(label + " friends: the text id finds the HG row", 1, fr.GetFriends(label == "mysql" ? tag : hg).Length);
        Eq(label + " friends: % matches only itself", 0, fr.GetFriends("%").Length);
        Eq(label + " friends: quote does not break", 0, fr.GetFriends("x' OR '1'='1").Length);

        RegionData MakeRegion(string name, int x)
        {
            RegionData r = MakeRegionBase(name, x);
            // MySQL in strict mode needs regionHandle (a real registration always sends it). The PostgreSQL store binds
            // it as text against a bigint column, fails, and still answers true, so it is left out there.
            if (label != "pgsql")
                r.Data["regionHandle"] = Util.RegionWorldLocToHandle((uint)x, 256000).ToString();
            return r;
        }
        RegionData MakeRegionBase(string name, int x) => new RegionData
        {
            RegionID = UUID.Random(), ScopeID = UUID.Zero, RegionName = name, posX = x, posY = 256000, sizeX = 256, sizeY = 256,
            Data = new Dictionary<string, object>
            {
                ["regionName"] = name, ["serverIP"] = "127.0.0.1", ["serverHttpPort"] = "9000", ["serverURI"] = "http://127.0.0.1:9000/",
                ["serverPort"] = "9000", ["regionMapTexture"] = UUID.Zero.ToString(), ["parcelMapTexture"] = UUID.Zero.ToString(),
                ["access"] = "13", ["regionSecret"] = UUID.Random().ToString(), ["owner_uuid"] = UUID.Zero.ToString(),
                ["flags"] = "0", ["last_seen"] = "0", ["Token"] = "", ["sizeX"] = "256", ["sizeY"] = "256",
                ["locX"] = x.ToString(), ["locY"] = "256000"
            }
        };
        string short8 = tag.Substring(0, 8);
        string plain = "Plain " + short8, under = "Under_" + short8, slash = "Back\\" + short8;
        NoThrow(label + " region: store three", () =>
        {
            Eq(label + " region: store plain", true, rg.Store(MakeRegion(plain, 256000)));
            Eq(label + " region: store with _", true, rg.Store(MakeRegion(under, 256256)));
            Eq(label + " region: store with backslash", true, rg.Store(MakeRegion(slash, 256512)));
        });
        int Count(List<RegionData> l) => l is null ? 0 : l.Count;
        Eq(label + " region: exact name", 1, Count(rg.Get(Util.EscapeForLike(plain), UUID.Zero)));
        Eq(label + " region: a name with _", 1, Count(rg.Get(Util.EscapeForLike(under), UUID.Zero)));
        Eq(label + " region: _ is not a wildcard", 0, Count(rg.Get(Util.EscapeForLike("UnderX" + short8), UUID.Zero)));
        Eq(label + " region: a name with a backslash", 1, Count(rg.Get(Util.EscapeForLike(slash), UUID.Zero)));
        Eq(label + " region: map search by part", 3, Count(rg.Get("%" + Util.EscapeForLike(short8) + "%", UUID.Zero)));
    }

    static int Main(string[] args)
    {
        string sqliteFile = args.Length > 0 ? args[0] : "datatest.db";
        string pg = args.Length > 1 && args[1].Length > 0 ? args[1] : null;
        string my = args.Length > 2 ? args[2] : null;

        string sq = "URI=file:" + sqliteFile + ",version=3";
        Market("sqlite", new OpenSim.Data.SQLite.SQLiteMarketplaceListingsData(sq));
        Race("sqlite", new OpenSim.Data.SQLite.SQLiteMarketplaceListingsData(sq));
        RegionHG("sqlite", new OpenSim.Data.SQLite.SQLiteRegionHGData(sq));
        Groups("sqlite", new OpenSim.Data.SQLite.SQLiteGroupsData(sq, "os_groups"), id =>
        {
            using (var c = new System.Data.SQLite.SQLiteConnection(sq))
            {
                c.Open();
                using (var cmd = new System.Data.SQLite.SQLiteCommand("update os_groups_invites set TMStamp = datetime('now','-20 days') where InviteID = '" + id + "'", c))
                    cmd.ExecuteNonQuery();
            }
        });
        OfflineIM("sqlite", new OpenSim.Data.SQLite.SQLiteOfflineIMData(sq, "im_offline"), id =>
        {
            using (var c = new System.Data.SQLite.SQLiteConnection(sq))
            {
                c.Open();
                using (var cmd = new System.Data.SQLite.SQLiteCommand("update im_offline set TMStamp = datetime('now','-20 days') where ID = " + long.Parse(id), c))
                    cmd.ExecuteNonQuery();
            }
        });
        var sqliteFSAssets = new OpenSim.Data.SQLite.SQLiteFSAssetData();
        sqliteFSAssets.Initialise(sq, "fsassets", 0);
        FSAssets("sqlite", sqliteFSAssets);
        var sqliteSim = new OpenSim.Data.SQLite.SQLiteSimulationData();
        sqliteSim.Initialise(sq);
        NoThrow("sqlite extra", () => RegionExtra("sqlite", sqliteSim));
        var sqliteEstate = new OpenSim.Data.SQLite.SQLiteEstateStore();
        sqliteEstate.Initialise(sq);
        NoThrow("sqlite estate", () => EstateRegions("sqlite", sqliteEstate));

        NoThrow("sqlite region load", () => RegionLoad("sqlite", sqliteSim, (t, c, v, idc, id) =>
        {
            using (var cn = new System.Data.SQLite.SQLiteConnection(sq))
            {
                cn.Open();
                using (var cmd = new System.Data.SQLite.SQLiteCommand($"update {t} set {c} = {v} where {idc} = '{id}'", cn))
                    Eq("sqlite load: damage " + t + "." + c, 1, cmd.ExecuteNonQuery());
            }
        }, nullName: false, badDynAttrs: false, childrenAfterNullName: 0, itemColumn: "creationDate",
            reopen: () => { var r = new OpenSim.Data.SQLite.SQLiteSimulationData(); r.Initialise(sq); return r; }));

        NoThrow("sqlite like", () => LikeLookups("sqlite", new OpenSim.Data.SQLite.SQLiteGridUserData(sq, "GridUser"),
            new OpenSim.Data.SQLite.SQLiteFriendsData(sq, "Friends"), new OpenSim.Data.SQLite.SQLiteRegionData(sq, "regions")));

        // constructing again over the same file must not fail (migration is idempotent)
        Eq("sqlite reopen ok", true, new OpenSim.Data.SQLite.SQLiteMarketplaceListingsData(sq) != null);
        var sqliteFSAssetsReopen = new OpenSim.Data.SQLite.SQLiteFSAssetData();
        sqliteFSAssetsReopen.Initialise(sq, "fsassets", 0);
        Eq("sqlite FSAssets reopen ok", true, sqliteFSAssetsReopen != null);

        if (pg != null)
        {
            Market("pgsql", new OpenSim.Data.PGSQL.PGSQLMarketplaceListingsData(pg));
            Race("pgsql", new OpenSim.Data.PGSQL.PGSQLMarketplaceListingsData(pg));
            RegionHG("pgsql", new OpenSim.Data.PGSQL.PGSQLRegionHGData(pg));
            Groups("pgsql", new OpenSim.Data.PGSQL.PGSQLGroupsData(pg, "os_groups"), id =>
            {
                using (var c = new Npgsql.NpgsqlConnection(pg))
                {
                    c.Open();
                    using (var cmd = new Npgsql.NpgsqlCommand("update os_groups_invites set \"TMStamp\" = now() - interval '20 days' where \"InviteID\"::text = '" + id + "'", c))
                        cmd.ExecuteNonQuery();
                }
            });
            OfflineIM("pgsql", new OpenSim.Data.PGSQL.PGSQLOfflineIMData(pg, "im_offline"), id =>
            {
                using (var c = new Npgsql.NpgsqlConnection(pg))
                {
                    c.Open();
                    using (var cmd = new Npgsql.NpgsqlCommand("update im_offline set \"TMStamp\" = now() - interval '20 days' where \"ID\" = " + long.Parse(id), c))
                        cmd.ExecuteNonQuery();
                }
            });
            var pgsqlFSAssets = new OpenSim.Data.PGSQL.PGSQLFSAssetData();
            pgsqlFSAssets.Initialise(pg, "fsassets", 0);
            FSAssets("pgsql", pgsqlFSAssets);
            NoThrow("pgsql auth", () => AuthSetDataItem("pgsql", new OpenSim.Data.PGSQL.PGSQLAuthenticationData(pg, "auth")));
            NoThrow("pgsql inventory", () => InventoryMoves("pgsql", new OpenSim.Data.PGSQL.PGSQLXInventoryData(pg, string.Empty)));

            var pgSim = new OpenSim.Data.PGSQL.PGSQLSimulationData();
            pgSim.Initialise(pg);
            NoThrow("pgsql region load", () => RegionLoad("pgsql", pgSim, (t, c, v, idc, id) =>
            {
                using (var cn = new Npgsql.NpgsqlConnection(pg))
                {
                    cn.Open();
                    using (var cmd = new Npgsql.NpgsqlCommand($"update {t} set \"{c}\" = {v} where \"{idc}\"::text = '{id}'", cn))
                        Eq("pgsql load: damage " + t + "." + c, 1, cmd.ExecuteNonQuery());
                }
            }, nullName: true, badDynAttrs: true, childrenAfterNullName: 1));

            NoThrow("pgsql like", () => LikeLookups("pgsql", new OpenSim.Data.PGSQL.PGSQLGridUserData(pg, "GridUser"),
                new OpenSim.Data.PGSQL.PGSQLFriendsData(pg, "Friends"), new OpenSim.Data.PGSQL.PGSQLRegionData(pg, "regions")));

            Eq("pgsql reopen ok", true, new OpenSim.Data.PGSQL.PGSQLMarketplaceListingsData(pg) != null);
        }

        // MySQL (third argument, an empty database): so far only the LIKE lookups and the group search.
        if (my != null)
        {
            var mySim = new OpenSim.Data.MySQL.MySQLSimulationData();
            mySim.Initialise(my);
            NoThrow("mysql region load", () => RegionLoad("mysql", mySim, (t, c, v, idc, id) =>
            {
                using (var cn = new MySql.Data.MySqlClient.MySqlConnection(my))
                {
                    cn.Open();
                    using (var cmd = new MySql.Data.MySqlClient.MySqlCommand($"update {t} set `{c}` = {v} where `{idc}` = '{id}'", cn))
                        Eq("mysql load: damage " + t + "." + c, 1, cmd.ExecuteNonQuery());
                }
            }, nullName: true, badDynAttrs: true, childrenAfterNullName: 2));

            NoThrow("mysql like", () => LikeLookups("mysql", new OpenSim.Data.MySQL.MySQLGridUserData(my, "GridUser"),
                new OpenSim.Data.MySQL.MySqlFriendsData(my, "Friends"), new OpenSim.Data.MySQL.MySqlRegionData(my, "regions")));
            NoThrow("mysql groups search", () =>
            {
                var g = new OpenSim.Data.MySQL.MySQLGroupsData(my, "os_groups");
                string tag = UUID.Random().ToString().Substring(0, 8);
                UUID gid = UUID.Random();
                GroupData gd = new GroupData { GroupID = gid, Data = new Dictionary<string, string>
                {
                    ["Name"] = "Explorers_" + tag, ["Charter"] = "", ["InsigniaID"] = UUID.Zero.ToString(), ["FounderID"] = UUID.Zero.ToString(),
                    ["MembershipFee"] = "0", ["OpenEnrollment"] = "1", ["ShowInList"] = "1", ["AllowPublish"] = "1", ["MaturePublish"] = "0",
                    ["OwnerRoleID"] = UUID.Random().ToString(), ["Location"] = ""
                } };
                Eq("mysql grp: store", true, g.StoreGroup(gd));
                Eq("mysql grp: search by part", true, Contains(g.RetrieveGroups("plorers_" + tag), gid));
                Eq("mysql grp: _ matches only itself", false, Contains(g.RetrieveGroups("plorersX" + tag), gid));
                Eq("mysql grp: injection attempt matches nothing extra", 0, g.RetrieveGroups("zzz%' OR '1'='1").Length);
            });
        }

        Console.WriteLine(checks + " checks, " + failures + " failures");
        return failures == 0 ? 0 : 1;
    }
}
