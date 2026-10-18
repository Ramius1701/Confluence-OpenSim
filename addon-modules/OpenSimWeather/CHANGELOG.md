# Changelog

## 0.6.1 - Confluence: snow you can see (2026-10-19)

- Seen in-world on 0.6.0: the flakes were dark grey specks, visible only against the sky, so over buildings, water and
  the ground they seemed never to come down. They were lit by the scene (the emissive flag of earlier versions had been
  dropped) and 0.18 m across. They are emissive again and about twice the size (snow 0.35 m, blizzard 0.32 m); rain
  streaks are wider and longer.
- Rain streaks follow their velocity (`PSYS_PART_FOLLOW_VELOCITY_MASK`, 32); 0.6.0 set 16, which is follow-source.

## 0.6.0 - Confluence: rain and snow fall around the avatars and reach the ground (2026-10-19)

- Rain and snow fall from emitters a short drop (`PrecipitationHeightMeters`, 22 m) above each avatar, on a grid of
  cells round it (`PrecipitationRadiusMeters`, 32 m) that follows it, instead of a grid of up to 384 emitters 80 m
  above the whole region. On a 1024 m region a blizzard reached the ground almost nowhere: particles started 47-113 m
  up, lived too short to fall that far, faded to 8-14% on the way, followed the region's wind, and 253 emitters
  overran what a viewer draws.
- Particles go down a cone at a steady speed (snow 1.3 m/s, rain 8.5) with a gentle drift, live exactly long enough
  to reach the ground, and do not fade. Rain streaks follow their fall. The particles in the air round one avatar stay
  near `ParticleBudget` (2,500); `PrecipitationDensity` scales it.
- An avatar under cover gets none; the cover is probed from the avatar (an avatar on a platform is not "covered" by
  it), and a cell with a roof between its floor and its emitter is skipped.
- Removed: the region grid and wandering layouts and their keys (`CoverageMode`, `EmitterGrid`,
  `EmitterSpacingMeters`, `ActiveAreaRadiusMeters`, `EmitterRadiusScale`, `Intensity`, `Wandering*`).

## 0.5.0 - Confluence: the weather sky rethought, ground effects removed (2026-10-18)

- The weather sky changes each sky of the region's day cycle relative to its own values, instead of putting the
  same absolute horizon, blue density, haze and cloud colours into every one of them: a storm at midnight is a
  darker midnight, not the mid-grey of noon. Clouds are raised (never lowered), colours desaturated and darkened by
  a factor, light scaled down, haze and density added, stars and moon dimmed. No weather brightens any part of a sky.
- A change of weather fades over `SkyFadeSeconds` (default 120) in `SkyFadeStepSeconds` steps (default 10); a change
  that starts mid-fade continues from what is showing. `weather clear` and turning weather off still restore at once.
- `[Weather.Sky.<Weather>]` replaces `[Weather.Environment.<Weather>]` (no longer read; logged once if present).
- Removed: the ground effects (puddle and snow patches, flat prims on the terrain), with their wetness, drying, melt
  and patch settings, textures and assets. The weather report no longer has a ground line.

## 0.4.1 - Confluence: a Robust that is still starting does not switch the weather on (2026-10-18)

- A Robust that is starting answers 404 for a moment before its handlers are registered. The module took any 404
  for a Robust without the region weather endpoint, stopped asking and switched the weather on from the ini, so every
  region that asked in that moment had weather (and auto-cycle) whatever its owner had chosen. A 404 now counts as no
  answer yet; the ini decides only after 404s for 10 minutes, and the module keeps asking, so a Robust that answers
  later hands the choice back to the owner.

## 0.4.0 - Confluence: the sky per avatar, on or off per region (2026-10-16)

- The weather sky is each avatar's own environment override built from the region's day cycle, never the region's
  stored environment: a restart or crash no longer leaves a storm sky that only `weather clear` removes. Avatars on a
  parcel with its own environment, or with a script's, are left alone. `AllowPersistentEnvironmentChanges` removed.
- Sunny (and any weather without a sky profile) gives the normal sky back; it used to keep the previous storm sky.
- At startup a weather sky an earlier version left saved on the region is put back to the region default.
- Per-region on/off, automatic changes and hours per weather from the web portal (My Regions > Weather), read from
  Robust's `/weather/<region>`; off until the owner turns it on. Without the portal the ini decides.
- `AutoCycleHours` defaults to 1 (was 6); turning weather on starts the first change after
  `AutoCycleStartupDelaySeconds`.

## 0.3.4 - chat response and complete environment profiles

- Removed the duplicate `Weather:` prefix from object-style chat replies.
- Kept `weather` as the canonical private-channel command namespace and retained
  `meteo` as an alias.
- Added configurable `SunMoonPosition`, `EastAngle`, and `MaxAltitude` to Sunny,
  Rain, Storm, and Snow environment profiles.
- Missing environment sections and missing individual keys use built-in profile
  values derived from OpenSim LightShare defaults.
- Sunny no longer inherits a dark pre-weather sun position from the region.

## 0.3.3 — coverage compatibility and configuration reconciliation

- Restored `CoverageMode = Region` as the safe default.
- Restored the dimension-scaled 8x8 emitter layout that covers standard regions
  and varregions with a stable 64-object upper baseline before cover suppression.
- Restored the original `EmitterRadiusScale = 0.62` coverage behavior.
- Kept `ActiveArea`, fixed-metre spacing, and `MaxEmitters` as explicit advanced
  options rather than silently changing normal region coverage.
- Invalid `CoverageMode` values now warn and fall back to `Region`.
- Simplified `OpenSimWeather.ini.example` to common operational settings.
- Added `OpenSimWeather.reference.ini.example` for the complete advanced
  configuration and all environment profiles.
- Updated focused examples to use full-region coverage unless the operator
  deliberately selects ActiveArea.

## 0.3.2 — OpenSim Enhanced replacement packaging

- Replaces the initial `addon-modules/Weather` implementation rather than
  installing beside it.
- Documents removal of the stale `OpenSim.Addons.Weather.dll` output.
- Uses capability-focused OpenSim Enhanced wording in public documentation.
- Removes an unused `OpenMetaverse.StructuredData` project reference.
- Omits the generated `.csproj`; `runprebuild.bat` creates it for the target
  checkout.
- Weather runtime behavior is unchanged from 0.3.1.

## 0.3.2 — add-on packaging correction

- Removed the XML declaration from the root `prebuild.xml`. OpenSim inserts add-on
  `prebuild.xml` files into its main prebuild document, so an add-on fragment must
  contain only the `<Project>` tree.
- Revalidated the archive layout for direct extraction under `addon-modules`.
- No weather runtime behavior changed from 0.3.0.

## 0.3.2 — realistic/configurable production candidate

### Coverage and var-regions

- Added `CoverageMode` with production-oriented `ActiveArea` and full `Region`
  modes.
- Added var-region-aware `EmitterSpacingMeters` and global `MaxEmitters`.
- Retained `EmitterGrid` as a warned legacy setting only.
- Proportionally reduces over-limit full-region layouts instead of imposing a
  fixed per-axis cap.
- Added active-area radius and refresh controls.

### Particle and storm assets

- Added configurable rain, storm, snow, and lightning texture UUIDs.
- Added clear warnings when blank particle textures will appear square.
- Lightning now retries outdoor/non-excluded candidate positions.
- Thunder remains spatial at the strike location.

### Environment

- Added configurable Sunny, Rain, Storm, and Snow environment sections.
- Exposed every sky/cloud field changed by the module.
- Preserves unmanaged current LightShare fields.
- Renamed the canonical switch to `AdjustEnvironment`; `AdjustClouds` remains a
  compatibility alias.
- Retained explicit acknowledgement for persistent environment changes.

### Indoor suppression

- Added multi-point cover probing over each weather cell.
- Added minimum open-sky fraction and partially open cell scaling.
- Added covered-avatar suppression for active-area weather.
- Added optional phantom cover-ray inclusion and bounded hit count.
- Added named `Weather Exclusion` footprint volumes with periodic refresh.

### Puddles and snow accumulation

- Added optional session-local wetness and snow levels.
- Added configurable accumulation, drying, melting, and temperature behavior.
- Added capped, non-persistent terrain puddle and snow overlays near avatars.
- Added water, slope, low-point, cover, and exclusion checks.
- Reconciles patches incrementally instead of rebuilding every object each tick.
- Automatically disables an effect when its required overlay texture is blank.

### Runtime safety

- Fixed active-area rollback so **all** tracked emitters are removed when any
  later startup step fails.
- Added marker names/descriptions to all generated objects.
- Added `TemporaryOnRez`, temporary-instance marking, and non-backup insertion.
- Added optional startup cleanup for matching non-persistent leftovers.
- Extended status output with exclusion and surface counts.

### Packaging

- Added complete v0.3 configuration reference and focused manual, auto-cycle,
  environment, and surface examples.
- Added asset guidance and a production replacement/rollback guide.
- Updated audit and validation documentation.

## 0.2.0 — initial production audit repair

- Made `Enabled = false` authoritative and disabled the module when its section
  is absent.
- Removed the undocumented disable override behavior.
- Replaced developer-specific build paths with repository-relative references.
- Serialized weather transitions.
- Added initial rollback, shutdown restoration, command parsing, lightning, and
  thunder fixes.
- Added explicit persistent-environment acknowledgement.
- Removed generated/private IDE artifacts.

## 0.1 — supplied baseline

- Original supplied module package.
