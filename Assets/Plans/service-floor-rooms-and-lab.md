# Service Floor — Room Grouping, Room 0 Lab, Monitor Swap & Pink Material Fix

## Project Overview
- **Game Title:** JunkLite
- **High-Level Concept:** 2.5D side-scrolling action brawler set in a high-tech cyberpunk building; this task dresses the "Service Floor" level.
- **Players:** Single player
- **Inspiration / Reference Games:** Cyberpunk industrial interiors; the attached reference image (a clean high-tech white/neon science-lab interior) and the red/chrome Cyber Chair image.
- **Tone / Art Direction:** Cyberpunk, metallic, cold blue-grey for most bays with a warm hazard bay; Room 0 is a clean, high-tech science-lab.
- **Target Platform:** StandaloneOSX (current), PC.
- **Screen Orientation / Resolution:** Landscape.
- **Render Pipeline:** URP — active asset "HIGH ASSET RENDER FEATURES". **Do not modify pipeline/quality/global settings** (recently stabilized).

## Scope (Confirmed Decisions)
1. **Room grouping:** 8 corridor bays → `Room 1` … `Room 8`, plus a new `Room 0`. 1:1 with existing Frame dividers.
2. **Monitor swap:** Replace ALL monitor/computer cubes in the scene with a **varied mix of all 4 Cosmic Retro Computer prefabs** (3/7/14/15).
3. **Pink fix:** **Run URP Built-in→URP converter** for Standard-shader materials, then **manually reassign the custom-shader materials** the converter can't handle.
4. **Room 0 ↔ Room 1 gateway:** The relocated **`Door_Entry`** (now at the start of Room 1) is the gateway. Room 0 is built west of it.

---

## Game Mechanics
### Core Gameplay Loop
No gameplay-logic changes. Room 0 is an **explorable** space (wider + deeper than a bay) added before the current spawn/entry so the player can walk into the lab. Managers, spawn point, encounter/level logic remain untouched.

### Controls and Input Methods
Unchanged (existing New Input System + player controller). Room 0 must be traversable: floor collider, walls with colliders, and an open doorway aligned to the player's 2.5D movement plane (X horizontal, gravity on Y, Z locked).

---

## UI
No UI changes.

---

## Key Asset & Context

### Scene
- `Assets/Game/Scenes/Level 2/Service Floor.unity` (currently loaded).
- Root `[Environment]` (0,0,0) holds sub-groups: `[Structure]`, `[Foreground]`, `[Lighting]`, `[Props]`, `[BayWallVariety]`, `[Dressing]`, `[CameraSwitchTrigger_Corner]`.
- Corridor runs along **X** from ~-5 to ~140; stairs at X=70.5–75.5 rise Y 0→1.25; L-turn at X≈124–146 (Z 16–48).
- `EndWall_Start` at (-5, 6, -7); `SpawnPoint` at (4, 0.2, 0); relocated `Door_Entry` at start of Room 1.

### Bay → Room boundaries (by X, split at Frame dividers)
| Group | X Range | Y Level | Notes |
|---|---|---|---|
| Room 0 (NEW lab) | -25 → -5 | 0.00 | Wider/deeper explorable lab; gateway = Door_Entry at X≈-5 |
| Room 1 | -5 → 16 | 0.00 | Entry bay, SpawnPoint, Frame_016 |
| Room 2 | 16 → 32 | 0.00 | DeepServerHall, Frame_032 |
| Room 3 | 32 → 48 | 0.00 | Hazard chamber (warm), Frame_048 |
| Room 4 | 48 → 64 | 0.00 | Monitors_Bay4, Frame_064 |
| Room 5 | 64 → 80 | 0→1.25 | Stairwell transition, Frame_080 |
| Room 6 | 80 → 96 | 1.25 | Upper server + Monitors_Bay6, Frame_096 |
| Room 7 | 96 → 112 | 1.25 | Upper storage, Frame_112 |
| Room 8 | 112 → 140+ | 1.25 | Monitors_Bay8, L-turn terminal |

### Monitors to replace (all primitive cubes)
- `[Props]/[Monitors_Bay4]` — Desk (56,1.10,13.90); 8 screen cubes `Mon_*` X 52.4–59.6, Y 3.20 & 5.30, Z 14.30; 8 `MonFrame_*` Z 14.36.
- `[Props]/[Monitors_Bay6]` — Desk (103.70,2.60,14.64); 8 `Mon_*` X 100.1–107.3, Y 4.70 & 6.80, Z 15.04; 8 `MonFrame_*`.
- `[Props]/[Monitors_Bay8]` — Desk (118,2.60,13.90); 8 `Mon_*` X 114.4–121.6, Y 4.70 & 6.80, Z 14.30; 8 `MonFrame_*`.

### Cosmic Retro Computer prefabs (URP-safe)
- Computer 3 `b9dd581be99d7b84ab71e4e9b6acda89` (0.51×1.10×0.49) — upright monitor.
- Computer 7 `522f2ed413277974f912900bd445fa79` (1.16×0.91×0.63) — wide console.
- Computer 14 `ee8f9569bc459a14aa267f7f23d0df4c` (0.40×0.42×0.43) — small unit.
- Computer 15 `f736c66fc1ed5e24087a827825e4c6ec` (0.93×0.50×0.25) — flat panel.

### Room 0 dressing prefabs
- Cyber Chair `a7ef9df2cef9cf843a1f8fe1fb91eb83` (needs scale ~100x; materials pink → fix first).
- Cyber_Box `fc740d3d9f59541cd9a4adbea33c40bd` (URP OK, 4.24³ — scale down for props).
- Cyberpunk Cube 1 `2c656892c716c6f4bbbc93c3f5cc6a12` (huge 396³ — scale WAY down, materials pink → fix first).
- NEON CITY (URP OK): `Cable00–05`, `Pipe_Mounted00–10`, `Coolbox00–01c`, `Neon00–017`, `Lighting_Deco00*`.
- Papers: LeartesStudios `SM_Paper_01/02.prefab` (verify existence at build time).
- Doorway: reuse relocated `Door_Entry` as the Room0↔Room1 gateway.

### Pink materials to fix
- Auto-convertible (Standard): `Cyber Chair/metal_05.mat`, `Cyberpunk Cube 1/Material #293.mat`.
- Manual reassign (custom built-in shaders):
  - `Cyber Chair/leather.mat` — `Ciconia Studio/Double Sided/Standard/Diffuse Bump` → URP/Lit (map leather albedo/normal/spec).
  - `Cyberpunk Cube 1/CT_ fukong01.mat` — `Xuqi/FlowEffect/Flow_OnlyEmission_Transparent` → URP/Lit or URP/Unlit transparent + emission (map Map_12_D + lut mask).

---

## Implementation Steps

### Step 1 — Backup & pre-flight
- **Description:** Confirm scene saved / under version control before edits. Snapshot the `[Environment]` hierarchy names+positions via read-only command for rollback reference. No changes yet.
- **Assigned role:** developer
- **Dependencies:** None
- **Parallelizable:** No (gate)

### Step 2 — Fix pink materials (converter + manual)
- **Description:** (a) Run URP **Built-in → URP** material converter (Render Pipeline Converter) scoped so it upgrades the Standard-shader materials `metal_05.mat` and `Material #293.mat` to `Universal Render Pipeline/Lit`. (b) Manually reassign `leather.mat` and `CT_ fukong01.mat` to URP shaders and re-map their textures (albedo/normal/spec for leather; emission map + transparency for the Flow material). Verify no other Cyberpunk-asset materials remain pink. Do NOT touch pipeline/quality/global settings.
- **Assigned role:** developer
- **Dependencies:** Step 1
- **Parallelizable:** No (must precede placing Chair/Cube in Room 0)

### Step 3 — Group existing bays into Room 1–8
- **Description:** Under `[Environment]`, create empty group GameObjects `Room 1`…`Room 8` (each at its bay's X-center, Y per level, Z=0, identity rotation/scale). Reparent existing environment/prop/dressing children into the correct room by their X coordinate ranges (table above), preserving world transforms. Managers, Cameras, Canvas, EventSystem, SpawnPoint, Level Manager, lighting rigs, PostProcess volume **stay at root** (not grouped). Shared spanning structures (Floors/Ceilings) may stay in a `[Structure]` shared group if they cross bays — only discrete per-bay props/dressing get reparented.
- **Assigned role:** developer
- **Dependencies:** Step 1
- **Parallelizable:** Yes (independent of Step 2)

### Step 4 — Swap monitor cubes for Cosmic Retro Computers
- **Description:** For each of `[Monitors_Bay4/6/8]`, replace the 8 `Mon_*` (and their `MonFrame_*`) cubes with instances of the 4 Cosmic Retro Computer prefabs in a varied mix, matching each original cube's world position/rotation and facing the camera (screens toward -Z front, consistent with existing desks). Keep the `Desk`. Scale prefabs to read correctly at the bay scale. Parent each swapped monitor bank under its owning Room group (Bay4→Room4, Bay6→Room6, Bay8→Room8). Remove leftover cube meshes.
- **Assigned role:** developer
- **Dependencies:** Step 3 (rooms exist for parenting)
- **Parallelizable:** No

### Step 5 — Build Room 0 shell (explorable lab)
- **Description:** Create `Room 0` group at X≈-15. Build a **wider + deeper** room than a standard bay: floor, ceiling (raised, matching corridor height ~12u feel), back wall (west), side walls (front/back Z depth greater than corridor so it's explorable), all with colliders. Open the east wall at the gateway where `Door_Entry` sits (X≈-5) so the player can pass Room 0 ↔ Room 1. Ensure floor is at Y=0 continuous with Room 1 and player movement plane (Z locked) works. Use URP-safe structural materials consistent with cold metallic bays (reuse `SF_Durasteel`/`SF_Cold_Panel`).
- **Assigned role:** developer
- **Dependencies:** Step 3
- **Parallelizable:** Yes (independent of Step 4)

### Step 6 — Dress Room 0 as a high-tech science lab
- **Description:** Populate Room 0 to match the reference image: (a) a **lay-down Cyber Chair** (scaled correctly, materials now fixed) reclined; (b) **wires/cables** (NEON CITY `Cable*`) running from the chair to nearby **computers** (Cosmic Retro Computer prefabs) so it reads as connected; (c) a bank of **monitors/computers** along a wall; (d) **scattered sheets of paper** on the floor (Leartes paper prefabs or thin quads if unavailable); (e) supporting props — Cyber_Box (scaled), Coolboxes, mounted pipes, Neon strips/Lighting_Deco for the clean hi-tech glow; (f) optionally a scaled Cyberpunk Cube 1 as a hero sci-fi structure. Parent everything under `Room 0`. Keep the palette clean/high-tech (whites + cool neon), distinct from the industrial bays.
- **Assigned role:** developer
- **Dependencies:** Step 2 (materials fixed), Step 5 (shell)
- **Parallelizable:** No

### Step 7 — Room 0 lighting
- **Description:** Add lighting for Room 0 consistent with URP setup and the cold/clean lab look (avoid over-bright bloom). Use downlights/fills matching existing `[Lighting]` intensities; add subtle neon accents. Do not alter global volume/pipeline.
- **Assigned role:** developer
- **Dependencies:** Step 5
- **Parallelizable:** No

### Step 8 — Verification pass
- **Description:** See Verification & Testing below.
- **Assigned role:** developer
- **Dependencies:** Steps 2–7
- **Parallelizable:** No

---

## Verification & Testing
- **Console:** 0 new errors/warnings after each step (esp. no missing-material/shader or missing-prefab errors).
- **Pink check:** Select Cyber Chair, Cyberpunk Cube, and every swapped monitor in the Scene view — confirm no magenta. Confirm `leather`, `metal_05`, `CT_ fukong01`, `Material #293` use URP shaders.
- **Hierarchy:** `[Environment]` contains `Room 0`…`Room 8`; each room's children fall within its X range; managers/cameras/spawn/level logic remain at root and unmodified.
- **Monitors:** All `Mon_*` cubes replaced; banks show a varied mix of Computer 3/7/14/15; screens face the camera; desks intact.
- **Room 0 traversal (Play Mode, Bootstrap pattern):** Player can walk west from spawn through the `Door_Entry` gateway into Room 0, move around (floor/walls collide, Z locked), and return — no falling through floor, no invisible barriers except intended walls.
- **Visual (multi-angle):** Capture Room 0 and each monitor bay from the gameplay camera(s) to confirm the lab reads as a clean science-lab with wired chair, monitors, and scattered papers, and that bays still look correct.
- **Render integrity:** Confirm post-processing, VFX (opaque/depth-dependent), and lighting still behave (no regression vs current stabilized state) since pipeline settings were untouched.

## Risks & Notes
- Running the URP converter can touch more materials than intended — scope it and review the converted set; back up first. Custom shaders (`Flow_OnlyEmission_Transparent`, Ciconia "Double Sided") will NOT convert and must be reassigned manually.
- Cyber Chair and Cyberpunk Cube meshes import at extreme scales (chair tiny ~0.02m, cube huge ~396m) — scale carefully when placing.
- Verify Leartes paper prefabs exist; if not, substitute thin textured quads for scattered papers.
- Preserve world transforms when reparenting so nothing shifts.
