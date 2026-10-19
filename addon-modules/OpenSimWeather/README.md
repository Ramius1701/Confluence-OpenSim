# OpenSimWeather 0.6.2

OpenSimWeather is an experimental but production-conscious OpenSimulator region
module that provides Clear, Sunny, Rain, Storm, Snow, and Blizzard conditions.
It uses
viewer-visible particle systems and can optionally coordinate wind,
weather skies made from the region's own EEP day cycle, lightning, thunder and
forecast messages.

This release replaces the initial OpenSim Enhanced Weather implementation.
It retains the useful multiple-emitter architecture while correcting unsafe,
non-portable, incomplete, and hard-coded behavior in the earlier module.

## What "real-world style" means here

The module models weather as related systems instead of one visual effect:

- precipitation cells with wind drift;
- different sky/cloud profiles for sunny, rain, storm, and snow;
- lightning and spatial thunder during storms;
- roof/interior suppression at the weather-cell level;
- a sky that each weather changes relative to the region's own skies, fading in
  and out, so night stays night.

It does **not** retrieve live meteorological data. Weather is selected manually
or by the configured automatic cycle. No external web service is required.

## Safe defaults

The complete reference is intentionally conservative:

- a missing `[Weather]` section leaves the module disabled;
- `Enabled = false` always disables it;
- the private command channel defaults to 89 and channel 0 is rejected;
- rain and snow fall around the avatars present, so they cost the same on any region size;
- automatic cycling, entry IMs, thunder, wind changes and environment changes
  are opt-in;
- the weather sky is each avatar's own override and is never stored on the region;
- generated emitters and flashes are inserted as
  non-backup objects and marked temporary;
- non-persistent weather objects left by an interrupted module instance can be
  removed automatically during region load.

## Installation

Copy the package directory to:

```text
addon-modules/OpenSimWeather
```

From the OpenSim source root:

```text
runprebuild.bat
compile.bat
```

A Visual Studio build can be used after `runprebuild.bat`. The included project
and prebuild files use repository-relative paths and contain no local drive
references.

Place `[Weather]` and any `[Weather.Sky.*]` sections in `OpenSim.ini` or
an INI file included by `OpenSim.ini`. Do not place module configuration in
`Regions.ini`.

For a production replacement, follow `UPGRADE.md` rather than copying a new DLL
into a running simulator process.

## Configuration examples

- `config/OpenSimWeather.ini.example` — common disabled installation baseline.
- `config/OpenSimWeather.reference.ini.example` — complete disabled reference.
- `config/OpenSimWeather.manual.ini.example` — controlled first test.
- `config/OpenSimWeather.autocycle.ini.example` — automatic particle weather.
- `config/OpenSimWeather.environment.ini.example` — weather sky and wind
  integration example.
- `assets/README.md` — texture and sound asset requirements.

## Commands

See `COMMANDS.md` for the full end-user reference (aliases, permissions,
report format, troubleshooting). Summary, with `CommandChannel = 89`:

```text
/89 weather status
/89 weather sunny
/89 weather rain
/89 weather storm
/89 weather snow
/89 weather blizzard
/89 weather clear
```

`meteo` is retained as an alias. The command and weather state are parsed as
exact tokens, so unrelated text containing words such as `rain` is ignored.
Replies are sent by the object-style chat source named `Weather`, so reply text
does not repeat a second `Weather:` prefix.
When `EstateManagerOnly = true`, only estate managers/owners can change the
weather.

`weather status` reports a narrative weather report: current conditions,
temperature (with a plain-language descriptor), emitter coverage, active
exclusion volumes, and time until the next
auto-cycle change. The same report is broadcast publicly to everyone in the
region whenever the weather actually changes (`AnnounceWeatherChangesInChat`).

`weather clear` clears the active condition but does not disable an enabled
auto-cycle timer.

## Rain and snow around the avatars (Confluence, 0.6.0)

Rain and snow fall around each avatar in the region instead of from a grid of emitters across the whole region.
For each avatar the module keeps a few emitters on a grid of cells over a circle `PrecipitationRadiusMeters`
(32 m) round it, each `PrecipitationHeightMeters` (22 m) above the floor the avatar stands on, and moves them as the
avatar moves (every `ActiveAreaRefreshSeconds`). The cells are spaced so the ground circles of their particles
overlap: about 9-10 m apart for rain, 18-24 m for snow.

Each emitter sends its particles down a cone at a steady speed (snow about 1.3 m/s, a blizzard 2.6, rain 8.5, a storm
11) with little acceleration and a gentle drift, and their lifetime is worked out from the drop so every one ends at
the ground. They do not fade on the way. The number in the air around one avatar stays near `ParticleBudget` (2,500,
half as many again for storms and blizzards), within what a viewer draws; `PrecipitationDensity` (0.25-2) scales it.

An avatar whose sky is covered (a roof above it) gets none, and a cell with a roof between its floor and its emitter
is skipped. Before 0.6.0 the emitters sat 80 m above the terrain, burst particles over a sphere up to 50 m across,
followed the region's wind and faded to almost nothing, so on a large region most of a blizzard never reached the
ground; `CoverageMode`, `EmitterGrid`, `EmitterSpacingMeters`, `ActiveAreaRadiusMeters`, `EmitterRadiusScale`,
`Intensity` and the `Wandering*` keys are no longer read.

## Particle textures

Every visual asset is configurable:

```ini
RainTexture = <asset UUID>
StormTexture = <asset UUID>
SnowTexture = <asset UUID>
LightningTexture = <asset UUID>
```

These values are OpenSim asset UUIDs, not filenames or inventory item names.
Blank rain or snow textures fall back to OpenSim's blank texture and therefore
can look like rectangular or square billboards. Upload an alpha rain streak and
alpha snowflake texture, then paste the resulting asset UUIDs into the
configuration. No made-up universal texture UUID is bundled.

## Buildings and indoor weather

OpenSim particles do not physically collide with roofs, walls, or terrain. The
module can suppress weather **cells**, but it cannot make each particle detect a
building.

The repaired system uses:

1. a configurable odd grid of sky probes across every proposed cell;
2. `MinimumOpenSkyFraction` to scale or reject partially covered cells;
3. optional suppression of the active weather bubble around covered avatars;
4. explicit named exclusion volumes for complex interiors.

A practical production baseline is:

```ini
AvoidCoveredAreas = true
CoverProbeGrid = 3
MinimumOpenSkyFraction = 0.67
SuppressAroundCoveredAvatars = true
CoverIncludesPhantomPrims = false
UseExclusionVolumes = true
ExclusionObjectName = Weather Exclusion
```

For a building, create a transparent phantom box or linkset covering its indoor
X/Y footprint and name the root object `Weather Exclusion` or begin its name
with `Weather Exclusion `. The module treats the object's axis-aligned X/Y
bounds as a no-weather column. This is more reliable for caves, hangars,
underground rooms, irregular mesh structures, and phantom roofs than physics
ray tests alone.

The exclusion object itself is ordinary region content and should be made
invisible/phantom by the builder. The module does not create or delete it.

Even with these controls, particles near a cell boundary can occasionally be
visible through an adjacent wall or overhang. That is a viewer particle-system
limitation, not real collision simulation.

## The weather sky (Confluence, 0.5.0)

`AdjustEnvironment = true` gives each weather a sky. A region's day cycle has a sky for each part of the day; a
weather changes every one of them **relative to that sky's own values** instead of putting the same storm colours
into all of them (before 0.5.0 midnight turned the same mid-grey as noon):

- clouds are raised to the weather's cover (a cloudier sky keeps its own) and take its scale, density and scroll;
- the horizon, blue density and cloud colours go part of the way to grey and are darkened by a factor;
- ambient, sun and moon light are scaled down; haze and atmosphere density are added; the clouds hide the stars.

A dark sky stays dark, only darker; no weather brightens any part of a sky. Clear and sunny show the region's own
sky. A change of weather fades over `SkyFadeSeconds` (default 120, 0 for at once), one step every
`SkyFadeStepSeconds` (default 10); a change that starts mid-fade continues from what is showing. The built-in
numbers can be changed per weather in `[Weather.Sky.Rain]`, `[Weather.Sky.Storm]`, `[Weather.Sky.Snow]` and
`[Weather.Sky.Blizzard]` (keys in `config/OpenSimWeather.reference.ini.example`); the `[Weather.Environment.*]`
profiles of earlier versions are no longer read.

### The weather sky is per avatar (Confluence, 0.4.0)

`AdjustEnvironment = true` darkens the sky and changes the clouds for each avatar in the region while a weather
lasts, as that avatar's own environment override (the way `osReplaceAgentEnvironment` works). The region's stored
environment is never changed, so a crash, a forced stop or a restart can never leave a storm sky behind. The
override is a copy of the region's day cycle with the weather applied to every sky, so the sun and moon keep moving.
Avatars on a parcel with its own environment, or given one by a script, are left alone. Sunny and clear show the
region's normal sky. `AllowPersistentEnvironmentChanges` is no longer used.

At startup the module puts back a storm, rain or snow sky that an earlier version left saved on the region (a
LightShare environment whose clouds are exactly one of the weather profiles'); an owner's own environment is never
touched.

### On or off per region (Confluence, 0.4.0)

On a grid with Confluence's web portal, a region has weather only when its owner turns it on under My Regions >
Weather, where they also choose whether it changes by itself and how many hours each weather lasts (default 1).
The region reads that from Robust (`/weather/<region>`) about once a minute; no restart. `[Weather] Enabled = true`
only makes the module available. Without the portal (no `SettingsServerURI` and no `[GridService] GridServerURI`,
`UseWebPortalSettings = false`, or a Robust without the endpoint) the ini decides, as before.

