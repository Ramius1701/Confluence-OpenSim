# Data backend check

Exercises Confluence's data-layer backends directly against **real databases**, one method at a time, so a
backend that merely loads is also proven to behave the same as the MySQL one. Currently covers, on SQLite
and PostgreSQL:

- **Marketplace listings** - round trips (including unicode and quotes), paging, update, listed state,
  finite vs unlimited stock, delivery receipts and duplicate delivery ids, and a race of 60 concurrent
  buyers for 5 units (exactly 5 must win, stock never negative).
- **RegionHG** - the per-region Hypergrid open/closed flag and its upsert.
- **Offline IM** - store several messages from the same sender, get / count, delete exactly one message
  by id scoped to its owner, the two-week clean-up, and delete all for a principal.
- **Groups** - groups, roles (exact 64-bit powers), membership, role membership, active group, invitations
  and notices with their two-week clean-ups, bans, search (including hostile search text), counts, and the
  cascade when a group is deleted.
- **FSAssets** - round trips (id, type, flags, hash), the empty-hash healing rule (a row storing no data at
  all is replaced in place by a later real store, never left empty forever), `AssetsExist` (including an
  empty request), count, and delete.
- **Region extra settings** (SQLite) - `SaveExtra` / `GetExtra` / `RemoveExtra` round trips, replace, per-region
  isolation, and an empty dictionary (never null) for a region with none.
- **Estate regions** (SQLite) - `GetRegions` lists exactly the regions linked to that estate.
- **Authentication `SetDataItem`** (PostgreSQL) - updates the one user's column and leaves others alone.
- **Inventory moves** (PostgreSQL) - `MoveItem` and `MoveFolder` against the quoted mixed-case columns.
- **LIKE lookups** (SQLite, PostgreSQL, MySQL) - grid users by id prefix, friends by text id and regions by
  name, with hostile text (quotes, a statement, `%`, `_`, a backslash): the text is bound and matched literally.
  Plus the MySQL group search. Against the 2026-11-24 build they report 10 failures, including the grid user
  rows deleted on PostgreSQL by an id of `';delete from GridUser;--`; after Tranquillity #519/#522/#525, none.
- **Region load with damaged rows** (SQLite, PostgreSQL, MySQL) - stores two objects (one a linkset) and a prim's
  two items, then damages rows as other tools can leave them: an item row that cannot be built, unreadable dynamic
  attributes, a NULL child name (not SQLite). The region must still load both objects, keeping the readable item;
  the SQLite store is opened again first, since it keeps its tables in memory. Against the 2026-11-24 build the
  PostgreSQL and MySQL loads throw (the region would start empty) and SQLite cannot store an object at all (its
  LinkNumber step had a duplicate version number); after Tranquillity #508/#539/#540/#541, none.

Each of these four groups catches its own exceptions and counts them as failures, so one broken backend does
not stop the rest of the run. Against the code before the 2026-10-11 fixes they report 15 failures; after, none.

When a backend is added or changed (the remaining gaps are listed in
`Tools/fresh-clone-matrix-expected.json`), add its checks here first.

## Running it

Build the solution first (`dotnet build OpenSim.sln -c Release` from the repository root), then copy the
whole `bin` folder somewhere disposable (the check needs the native SQLite library beside it), build this
project into that copy and run it there:

```bat
xcopy /E /I bin C:\temp\databackend
dotnet build Tools\data-backend-check -c Release -o C:\temp\databackend
cd C:\temp\databackend
dotnet DataBackendCheck.dll test.db "Server=localhost;Port=5432;Database=cfx_datacheck;User Id=postgres;Password=..."
```

The first argument is a **new** SQLite file (delete it between runs; leftover rows change the counts; the
provider keeps `,version=3` in the name, so the file on disk is `test.db,version=3`: delete `test.db*`). The
second is optional: an **empty** PostgreSQL database you created for the check (drop it afterwards); pass ""
to skip it. The third is optional: an **empty** MySQL/MariaDB database, for example
`"Data Source=127.0.0.1;Port=3317;Database=cfx_check;User ID=root;Password=;Old Guids=true;SslMode=None;"`.
Never point either at a server that holds a live grid's data: start a throwaway server with its own data folder
and port instead (`mariadb-install-db --datadir=... --port=3317`, `initdb -D ...`, then `pg_ctl -o "-p 5442"`).
Without them only SQLite is tested. The last line reports `N checks, M failures`; the exit code is 0 only
when there are no failures.
