# Project Overview

- **Game Title:** JunkLite
- **High-Level Concept:** A 2.5D action game where a 2D pixel-art (Spine) heroine fights through fully-3D cyberpunk environments. This plan adds a new interior level: a militant service floor of a corporate building, under attack, alarms blaring.
- **Players:** Single player.
- **Inspiration / Reference Games:** *Deus Ex* (brutalist, militant, restrained sci-fi) — explicitly **not** the neon-saturated look of *Cyberpunk 2077*, and **not** cozy/industrial.
- **Tone / Art Direction:** Dark, metallic, brutalist sci-fi interior. Warm **orange/amber** key light punctuated by **red alarm flashes**. Volumetric fog, reflective metal, sparse emissive signage.
- **Target Platform:** StandaloneOSX (per current project settings).
- **Screen Orientation / Resolution:** Landscape (matches existing levels).
- **Render Pipeline:** URP — active asset `Assets/Settings/HIGH ASSET RENDER FEATURES.asset` (VolumetricFog, AnalogGlitch, DigitalGlitch renderer features).

# Game Mechanics

## Core Gameplay Loop
This first pass is **environment, layout, lighting, and mood only** — no combat encounters or hazards. The player spawns at the entry and traverses a linear multi-room service floor (Entry Corridor → Machine Room → Security Checkpoint → Control Room) with 90° corner turns handled by the existing camera-switch system. Combat/encounters are deliberately deferred to a later pass so the atmosphere can be validated first.

## Controls and Input Methods
No new input work. The level reuses the existing `junklite.Character2D5Controller` movement (locked to XY plane with ZY corner-turn support) and the New Input System already wired to `Player_2.2`. All player, camera, and lifecycle behavior is inherited unchanged from the reference scene.

# UI
No new UI is authored. The existing `Canvas` + `EventSystem` and HUD are carried over verbatim from the reference scene via duplication (see approach below), so the HUD, dialogue panel, and health/mod displays continue to function.

# Key Asset & Context

## Reference scene (the template)
`Assets/Game/Scenes/Level 1/V2/Rooftop Scene v2.unity` — verified root hierarchy:
- **Persistent-manager roots** (created here but also `DontDestroyOnLoad` from bootstrap): `[Audio Manger]`, `[Combat Effects Manager]`, `[Damage Popup Manager ]`, `[Game Manager]`, `[FeedbackManager]`, `[Game Input Manager ]`, `[DropManager]`.
- **Scene-local manager roots (must be present & wired per level):** `[Camera Manager]`, `[DialogueManager]`, `[Level Manager]`.
- **Camera rig** `[Cameras ]` → `CinemachineBrain` (Camera + UniversalAdditionalCameraData + AudioListener + Animator), plus four directional vcams `PosZCam`, `NegZCam`, `PosXCam`, `NegXCam` (CinemachineCamera + CinemachineFollow + AutoFocus + VolumeSettings + Perlin + ImpulseListener).
- **UI:** `[Canvas]`, `[EventSystem]`.
- **Spawn/flow:** `[SpawnPoint]`, `[SceneSettings]`, `[CameraSwitchTriggers]` (13 `CameraSwitchTrigger`, three also carry `EncounterController`), `[Corners Turning Points]`.
- **Lighting:** `[ThreePointLighting]` → `Fill` / `Key` / `Overhead` directional lights, plus extra `[Light]` / `[Light (1)]`.
- **Environment/gameplay (to be REMOVED and rebuilt):** `[Environmnent Assets]`, `[Level Boundaries]`, `[HAZARDS]`, `[Enemies]`, `Death Trigger`(s), `Mod pick up prefab`(s), cinematic prefabs, `[TestPlayerState]`, `[---Level Things---]`.

## Player benchmark
`Assets/Game/Prefabs/PLAYER/Player_2.2.prefab` — world height ~2.8u, width ~0.77u. Spawned at runtime via `PlayerLifecycle`; `CameraManager.ConnectToPlayer()` rebinds vcam tracking targets. **Do not place a player in the scene manually** — the spawn system instantiates it. Scale all geometry to the 2.8m benchmark.

## Environment kit (existing assets — primary source)
`Assets/RASHID WORLD/NEON CITY/Neon City/[Prefabs]/City Builder/` modular blocks:
- Structure: `0_Basic/0_Basics/` horizontal & vertical walls, corner walls (e.g. `CityTerrain_Base_cornerwall5x2.5`, `arcwall3x5`).
- Dressing: `2_Deco/Street Deco/Pipes/Pipe_Base00–05`, `Beams/MetalBeam00–002`.
- Optional accents: `Street Ads/Neon00–004` (use sparingly / recolor amber to avoid the "2077" look).

## Key materials (existing)
- Amber/gold emissive: `Assets/RASHID WORLD/NEON CITY/Neon City/Source/Materials/Generic/Light/Light03.mat`
- Orange alarm emissive: `.../Generic/Light/Light05.mat`
- Metal: `.../Generic/Light/MetalBare01_lgt*.mat`
- `AlarmFlash` script (from reference scene) for pulsing red alarm lights.

## Relevant scripts (read-only context — no code changes planned)
`CameraManager.cs`, `CameraSwitchTrigger.cs`, `LevelContext.cs`, `SceneSettings.cs`, `LevelGoal.cs`, `Fade.cs`, `SpineAnimationController`, `junklite.Character2D5Controller`.

# Approach Decision — Scaffolding Strategy

**Chosen: Approach A — Duplicate the reference scene, then gut & rebuild the environment.**

- **Approach A (RECOMMENDED): Duplicate `Rooftop Scene v2.unity` → new file, delete rooftop environment/enemies/hazards, keep the fully-wired manager stack + camera rig + UI + spawn + triggers, then build the interior.**
  - Pros: Preserves every fragile serialized reference (CameraManager's `mainCamera`/`deathCamera`/`spawnCamera`/`cameraBrain`/`cameraList`, LevelContext spawns, vcam tracking, Cinemachine blends) with zero manual re-wiring risk. Fastest path to a *working* level.
  - Cons: Starts with rooftop cruft that must be deleted carefully; scene inherits any rooftop-specific leftovers if cleanup is incomplete.
- **Approach B: Build a fresh empty scene and copy in only the manager/camera/UI objects.**
  - Pros: Cleanest possible hierarchy, no leftover cruft.
  - Cons: High risk of broken serialized refs across scenes (Cinemachine + CameraManager wiring is notoriously fragile when copied between scenes); much slower; more error-prone.

Approach A is recommended because the camera/manager wiring is the highest-risk part of the level and duplication guarantees it stays intact; environment cleanup is low-risk and mechanical.

# Implementation Steps

### Step 1 — Create & register the new scene (Approach A)
- **Description:** Duplicate `Assets/Game/Scenes/Level 1/V2/Rooftop Scene v2.unity` to `Assets/Game/Scenes/Level 2/Service Floor.unity` (create the `Level 2` folder). Open it. Add it to Build Settings after the rooftop level.
- **Assigned role:** developer
- **Dependencies:** None
- **Parallelizable:** No (all later steps depend on it)

### Step 2 — Strip rooftop-specific content
- **Description:** Delete these roots/contents while KEEPING the manager stack, `[Cameras ]`, `[Canvas]`, `[EventSystem]`, `[SpawnPoint]`, `[SceneSettings]`, `[ThreePointLighting]`, `[CameraSwitchTriggers]` (will be repositioned), `[Corners Turning Points]`: remove `[Environmnent Assets]`, `[Level Boundaries]`, `[HAZARDS]`, `[Enemies]`, all `Death Trigger*`, all `Mod pick up prefab*`, `CinematicPrefab`, `EndgameCinematicPrefab`, `[TestPlayerState]`, extra `[Light]`/`[Light (1)]` if rooftop-specific, and any leftover rooftop geometry under `[---Level Things---]`.
- **Assigned role:** developer
- **Dependencies:** Step 1
- **Parallelizable:** No

### Step 3 — Blockout the interior geometry (grey-box, correct scale)
- **Description:** Build a linear floor along the XY plane (Z=0) with ZY-plane corner turns, using NEON CITY modular walls/floors (or primitive grey-box where a kit piece is missing). Sections in order: **Entry Corridor → 90° turn → Machine Room → Security Checkpoint → 90° turn → Control Room**. Constraints: corridor interior height **4.0–4.5u**, doorways **≥3.2u**, walkway width comfortable for a 2.8u character, everything axis-aligned to XY/ZY. Add a `Ground` collider strip and back `Walls`. Group under a new `[Environment]` root.
- **Assigned role:** developer
- **Dependencies:** Step 2
- **Parallelizable:** No

### Step 4 — Wire traversal: spawn, camera triggers, corner turns
- **Description:** Reposition `[SpawnPoint]` to the Entry Corridor start; confirm `[SceneSettings]`/`LevelContext` `primaryPlayerSpawn` points to it. Reposition/duplicate the existing `CameraSwitchTrigger` objects at each corner and room boundary, setting their A/B rotation states and vcam priority targets to the correct directional cameras (`PosXCam`/`NegXCam`/`PosZCam`/`NegZCam`) for each track segment. Remove `EncounterController` components (no combat this pass) or leave them disabled. Update `[Corners Turning Points]` markers to match new geometry.
- **Assigned role:** developer
- **Dependencies:** Step 3
- **Parallelizable:** No

### Step 5 — Fader walls for room reveals
- **Description:** On near-side walls that would occlude the player, add the `Fade` component (matching reference-scene setup: URP `_BaseColor` drive, ZWrite/depth-pass toggling, renderQueue 2450↔3000) so rooms reveal as the player enters. Assign the correct materials/renderers.
- **Assigned role:** developer
- **Dependencies:** Step 3
- **Parallelizable:** Yes (can run alongside Step 4)

### Step 6 — Prop dressing (existing assets)
- **Description:** Dress each room with NEON CITY props: `Pipe_Base*`, `MetalBeam*`, server/console blocks, and sparse recolored signage. Apply `MetalBare01_lgt*` to structural surfaces. Keep it militant/brutalist — restrained, no dense neon clutter.
- **Assigned role:** developer
- **Dependencies:** Step 3
- **Parallelizable:** Yes (alongside Steps 4–5)

### Step 7 — Lighting & mood
- **Description:** Retune `[ThreePointLighting]` for a dark interior with warm amber `Key`, low `Fill`, and orange `Overhead` accents (`Light03.mat`/`Light05.mat` emissive fixtures). Add red **alarm** lights using the `AlarmFlash` script at ceiling points. Enable/tune URP **Volumetric Fog** via a Volume, add **Reflection Probes** per room for metallic response. Set lighting mode to **Mixed** (baked GI + realtime alarms); do an initial bake.
- **Assigned role:** developer
- **Dependencies:** Steps 3, 6
- **Parallelizable:** No

### Step 8 — Optional new accent assets
- **Description:** *(Deferred pending user choice — default: skip in this pass.)* If requested, generate militant insignia, hazard stripes, service signage, and console-screen graphics as emissive/decal materials to reinforce the Deus Ex tone.
- **Assigned role:** developer
- **Dependencies:** Step 6
- **Parallelizable:** Yes

### Step 9 — Save, verify, and register
- **Description:** Save the scene. Confirm it is in Build Settings. Run the verification checklist below.
- **Assigned role:** developer
- **Dependencies:** All prior steps
- **Parallelizable:** No

# Verification & Testing

- **Console clean:** No errors/warnings on scene open or Play (check Unity Console).
- **Player spawn:** Enter Play — `Player_2.2` instantiates at `[SpawnPoint]` via `PlayerLifecycle`; no manually-placed player exists in the hierarchy.
- **Camera binding:** On spawn, `CameraManager.ConnectToPlayer()` binds the active vcam to the player; camera frames the ~2.8u character correctly.
- **Traversal:** Walk the full path; each `CameraSwitchTrigger` performs the correct 90° player rotation and camera priority swap at every corner without snapping/jitter.
- **Fade reveals:** Near walls fade correctly as the player enters each room with no depth-sorting artifacts.
- **Scale check:** Character reads at correct proportion in corridors (height 4.0–4.5u) and doorways (≥3.2u).
- **Mood check:** Dark scene, warm amber key light, pulsing red alarms, visible volumetric fog, metallic reflections — reads as "Deus Ex militant service floor," not neon 2077.
- **Build registration:** `Assets/Game/Scenes/Level 2/Service Floor.unity` present and enabled in Build Settings.
- **Manager integrity:** `[Camera Manager]` serialized refs (`mainCamera`, `deathCamera`, `spawnCamera`, `cameraBrain`, `cameraList`) all non-null after duplication.
