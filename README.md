# MvcBB Clans

A clans mod for MvcBB, built against MvcBB's mod framework (`MvcBB.ModKit`).
Members pick a clan from a list the administrators manage, and their clan's
icon and a line such as "I am an Elf." appear under their name on every post
and on their profile. Lives in its own solution, separate from MvcBB's core
codebase.

Requires MvcBB.ModKit **1.1.0** or later (core's view slots).

## Architecture

- **MvcBB.Clans.Api** - the API-side half: data access, business rules, and
  the HTTP endpoints MvcBB.App's side calls. Implements `IApiMod`, so
  MvcBB.API discovers it, registers its endpoints, and installs its database
  schema automatically once the built assembly is placed in MvcBB.API's
  `Mods/` folder.
- **MvcBB.Clans.App** - the pages, admin UI and view slot components, as a
  Razor Class Library. Implements `IAppMod`, so MvcBB.App discovers it,
  registers its controller and precompiled views, adds its nav link, and
  renders its view components into core pages once its built assembly is
  placed in MvcBB.App's `Mods/` folder.

Both projects depend only on `MvcBB.ModKit` and `MvcBB.Shared` (plus, for the
API, MvcBB's own connection-factory projects for direct database access) -
never on MvcBB.App's or MvcBB.API's internal types.

## Functionality

### Members
- The **Clans** page (nav link) lists the active clans with their icons and
  member counts, and shows the member's own clan.
- **Choose a clan**: a member picks one active clan, or none. Linked from the
  Clans page and from the member's own profile (under their avatar). Each
  member can change clan up to 10 times per 10 minutes.

### On core pages (view slots)
- **Posts** (`post.author`): under the author's name and rank, the clan icon
  (its tooltip is the clan name) next to "I am a/an {Name}."
- **Profiles** (`profile.sidebar`): the same, under the avatar. On the
  member's own profile there's also a "Choose a clan" / "Change clan" link.
- **Home page** (`home.sidebar`): a "Clans" card under Quick Links listing the
  active clans with icons and member counts.
- **Stylesheet** (`layout.head`): the mod's `clans.css`, embedded in the DLL
  and served by MvcBB.App at `/_mods/clans/css/clans.css`.

Only active clans are shown. A member whose clan has been deactivated shows
no clan until it's reactivated or they choose another. Anonymized accounts
(role Deleted) never show a clan and aren't counted as members.

### The article ("a" or "an")
Worked out from the name by default: "an" when it starts with a vowel letter
(a, e, i, o, u), otherwise "a". English doesn't always follow the spelling, so
each clan has an override: always "a" (a Unicorn, a One-Eyed Jack), always
"an" (an Honour Guard), or none ("I am Legion.").

### Administration
Administrators get a **Manage clans** page (from the Clans page) to add, edit
and delete clans: name (unique, ignoring case), article, sort order, active
flag, and icon.
- **Deactivating** a clan (untick "Active") hides it everywhere and stops new
  members choosing it, but keeps its memberships.
- **Deleting** a clan that still has members requires ticking a confirmation
  that removes their memberships too. The API refuses otherwise.

## Performance

A thread page can show hundreds of posts, so the post slot never makes one
API call per post:
- Core passes every author id on the page to the slot. The first post's
  lookup asks the API about all of them in one call (core's `SlotBatch`), and
  every later post on that page is served from memory.
- Each member's clan is also cached in MvcBB.App's memory for 60 seconds,
  and the clan list (with member counts) for 60 seconds. A member's own
  change, and any admin change, clears the relevant entries on the App
  instance where it was made straight away.
- The API's bulk lookup is capped at 200 member ids per call; the App splits
  larger sets into chunks.
- The mod's HttpClient times out after 5 seconds. If the API can't be
  reached, posts simply show no clan (logged once per page), and the page
  still renders.

## Data providers

Supports all three of MvcBB's data providers - SQL Server, PostgreSQL, and
the in-memory provider used for local development - following whichever
`DataProvider` MvcBB.API itself is configured with. Schema for SQL Server and
PostgreSQL is created automatically (idempotently, safe to redeploy) the first
time the mod runs against a database that doesn't have it yet, and upgraded
whenever the mod's `Version` changes.

| SQL Server | PostgreSQL | Holds |
| --- | --- | --- |
| `dbo.Mod_Clans_Clans` | `public.mod_clans_clans` | Id, Name (unique), Article (null = automatic, `a`, `an`, or empty for none), IconFileName, SortOrder, IsActive, CreatedAt |
| `dbo.Mod_Clans_UserClans` | `public.mod_clans_user_clans` | UserId (primary key, so one clan per member), ClanId, JoinedAt |

Membership is its own table rather than a column on core's `Users` table, so
core's schema is untouched and future features can hang off clan and
membership rows. Removing a member from core removes their membership
(cascade); a clan can't be removed while memberships point at it except
through the confirmed delete above.

## Extending: competitions and points

Not built yet. The intended shape, so the current tables don't need to
change:
- **`Mod_Clans_Competitions`** (Id, Name, StartsAt, EndsAt, Rules) and
  **`Mod_Clans_Scores`** (CompetitionId, ClanId, UserId, Points, AwardedAt),
  keyed by `ClanId` and `UserId` from the existing tables.
- **`Mod_Clans_MembershipHistory`** (UserId, ClanId, JoinedAt, LeftAt),
  written whenever `SetMembership` changes or removes a row, so points can be
  credited to the clan a member was in at the time.
- **Activity events**: points need to know when a member posts, replies or
  gets a like. Core has no mod event hook yet; the natural addition is an
  `IModEventHandler` in ModKit (for example `OnPostCreated(userId, postId)`)
  that MvcBB.API calls after its own write, with this mod's handler adding
  rows to `Mod_Clans_Scores`.
- A `post.author` badge for the current competition leader, and a
  leaderboard card in `home.sidebar`, would reuse the existing slot
  components and batching.

## Security

- **Login by default.** Every App page requires a signed-in member, and so do
  the clan icons and the clan display in slots, unless `Clans:ShowToGuests`
  is turned on. The App calls the API with the member's own token, never a
  shared or service token.
- **Public reads on the API.** The API's two lookups the slots use (active
  clans, and member-to-clan for a list of ids) are anonymous, like core's
  read-only browsing endpoints, so guest pages can show clans when the App
  allows it. They return clan names, icons and counts and which clan a
  member chose, which is shown on their posts anyway. Everything else
  requires a signed-in member.
- **Admin only.** Creating, editing and deleting clans, and listing inactive
  ones, is Administrator-only, checked in the API as well as the App.
- **Uploads.** Icons are checked by their actual bytes (PNG, JPEG, GIF or
  WebP signature), not just the name and content type, limited to 256 KB,
  and saved under a random file name with the extension of what they really
  are. If the API rejects the change, the saved file is deleted again. Each
  administrator can upload up to 30 icons per 10 minutes.
- **One clan per file.** The API only accepts icon names in the exact shape
  the App generates and refuses one another clan already uses (backed by a
  unique index). Icon deletion never leaves the icon folder.
- **Private files.** Icons are stored outside `wwwroot` and served by the
  mod's controller with the right content type, `X-Content-Type-Options:
  nosniff` and private caching.
- **CSRF and XSS.** Every form post carries an antiforgery token. Clan names
  are plain text, encoded by Razor everywhere they're shown.
- **Rate limits.** Clan changes (10 per 10 minutes per member) and icon
  uploads (30 per 10 minutes) are counted in memory, so they reset when
  MvcBB.App restarts.

## Configuration

Optional, in MvcBB.App's configuration:

| Setting | Default | Meaning |
| --- | --- | --- |
| `Clans:ShowToGuests` | `false` | Show clans on posts, profiles and the home page to visitors who aren't signed in, and serve them the icons. |
| `Clans:StoragePath` | `App_Data/clans` under MvcBB.App's content root | Where uploaded icons are kept. Relative paths are resolved from the content root. Must not be inside `wwwroot`, and MvcBB.App's process needs write access. |

## Installing

1. Build both projects (`dotnet build MvcBB.Clans.slnx`).
2. Copy `MvcBB.Clans.Api.dll` into MvcBB.API's own `Mods/` folder (next to
   its other build output), and `MvcBB.Clans.App.dll` into MvcBB.App's
   `Mods/` folder.
3. Restart both processes, so each discovers the new mod and (for the API)
   installs its schema.
4. Sign in as an Administrator, open the Mods page, and enable "Clans".
   It can be disabled again the same way at any time, with no restart
   required either way. While disabled, its pages, icons, stylesheet and slot
   output all disappear (within about 10 seconds); its data is kept.
5. Open **Clans > Manage clans** and add the clans.

If a web server or CDN in front of MvcBB.App serves `/images/` directly from
disk, exclude `/images/clans/` so requests reach MvcBB.App.
