# JunkLite main mechanics

How the game plays, and the rules any level or scene edit must respect. Read this before touching a level, collider, camera, lane, or rotation setup. For code architecture (damage, managers, mods), see `ARCHITECTURE_HANDOFF.md`.

## The game in one paragraph

JunkLite is a single-player **2.5D side-scroller**. The player and enemies are **2D** Spine characters. The world is **full 3D** (URP), viewed side-on by a Cinemachine camera. The player runs left and right along a lane, fighting, jumping, dashing, and parrying. The signature mechanic is **world rotation**: at the end of a lane the camera swings 90°, the player is put onto a new lane, and the level carries on in a new direction through the same 3D space.

## Lanes: the movement model

A lane is a straight line along **world X or world Z**. Characters move left and right along it, plus up and down (jumping and falling), and never toward or away from the camera.

**Player** (`Scripts/Character/Character2D5Controller.cs`):
- Moves only along its own `transform.right` (`MovementAxis`). The player's **Y rotation decides the lane direction**.
- `FreezePerpendicularAxis()` locks the Rigidbody position on whichever of world X or Z is perpendicular to the lane. This is why lanes **must** be axis-aligned: a 45° lane would lock the wrong axis.
- `snapToZPosition` / `fixedZPosition` pin depth. `allowZMovement` (off by default) lets vertical input move in depth. It is the hook for free 3D roaming (see Room 0 below).
- On respawn, `ResetToSpawnOrientation(spawnYRotation)` restores the spawn yaw and the axis lock, so a death after a rotation doesn't carry the wrong lane into the next run.

**Enemies** (`Scripts/New Enemies/Robot Enemy/EnemyMovement.cs`, `EnemyBehaviors.cs`):
- Take `horizontalAxis` from `transform.right`, snapped to the nearest world axis, and freeze the matching position axis.
- An enemy's **Y rotation in the scene decides its lane**. Place enemies on the lane line, rotated to the lane's yaw.

**Yaw ↔ lane direction** (Unity yaw, `transform.right` = "screen right"):

| Player yaw | Screen-right moves toward | Locked axis |
| --- | --- | --- |
| 0 | +X | Z |
| −90 (= 270) | +Z | X |
| −180 (= 180) | −X | Z |
| −270 (= 90) | −Z | X |

## World rotation: the USP

At a corner, a **Camera Switch Trigger** rotates the world. Code: `Scripts/LEVEL/CameraSwitchTrigger.cs`. Prefab: `Prefabs/CameraSwitchTrigger.prefab`.

When the player (tag `Player`) enters its box trigger, it:
1. **Switches camera** between `cameraA` and `cameraB` (`Prioritize()` plus `CameraManager.SetActiveCamera`), blending over `cameraBlendDuration`.
2. **Teleports** the player onto the new lane, using the X/Z of the trigger's child point `A` or `B` and keeping the player's height.
3. **Rotates** the player to `rotationA` or `rotationB` and calls `FreezePerpendicularAxis()` so the new lane's axis is locked.
4. **Billboards** the Spine body (`BODY SPINE`) across the blend, so the 2D sprite doesn't visibly snap. `ccwAB` sets the turn direction.

The trigger flips state every time it's crossed, so the same corner works in both directions: A→B going forward, B→A going back. Other options:
- `oneWaySwitch` locks the trigger after its first use.
- `lockPolicy` blocks the corner while fighting. `GlobalCombat` blocks it while the player is in combat, `Encounter` blocks it until a given `EncounterController` completes, and `None` never blocks. The arrow renderers show red when locked and green when unlocked.
- `objectsToHide` toggles geometry that would block the new view.

**Reference level:** `Scenes/Level 1/V2/Rooftop Scene v2.unity` is a square loop. It uses four vcams and a trigger at every corner, with rotation pairs 0↔−90, −90↔−180, −180↔−270 and −270↔0.

## Cameras

- Each scene has one `CinemachineBrain` and one vcam per lane direction, named by the direction the camera **looks**: `PosZCam` (yaw 0), `PosXCam` (90), `NegZCam` (180) and `NegXCam` (270). The scene-local `CameraManager` binds them to the player on `PlayerSpawned`.
- **The active vcam's yaw equals the player's lane yaw**, because the camera faces the same way as the player's forward. For example, player yaw −90 (moving toward +Z) uses `NegXCam` (270). When wiring a corner, `cameraA` must match `rotationA` and `cameraB` must match `rotationB`.
- Each vcam has `CinemachineVolumeSettings`. Cinemachine turns these into a hidden global volume at about priority 998, which overrides other volumes, so any runtime post effect must outrank it. `CombatModeScreenGlitch` uses 10000.

## Free-move rooms and lane entry

- **Free-move rooms** (safe rooms, explore rooms such as Service Floor's Room 0, 3 and 10) are where the player can roam in depth. `Character2D5Controller.SetFreeRoam(true)` lets vertical input walk along `transform.forward` at `zMoveSpeed` (world Z on an X lane, world X on a Z lane) with no ground axis locked; room colliders bound the player.
- **`ZMovementZone`** (`Scripts/LEVEL/ZMovementZone.cs`) is the trigger box for this: Z movement is on while the player is inside. Several BoxColliders on one zone form one area (L-shaped rooms). Leaving without an E-press snaps the player back to its optional `lanePath`, or to the depth they entered at. It re-enables free roam when the player respawns inside it.
- **Lane-only rooms** use a `LanePath` (`Scripts/LEVEL/LanePath.cs`). Its child transforms are waypoints, in hierarchy order. Every segment must run along world X or Z; a segment that doesn't is drawn red and logs a warning. `TryGetClosestPoint` returns the nearest lane point and the lane's Y rotation.
- **Leaving a free-move room** goes through a `LaneEntryTrigger` (`Scripts/LEVEL/LaneEntryTrigger.cs`). It's a trigger box placed in front of the exit door:
  - While the player stands in it, a small on-screen prompt reads "E to proceed" (the Interact binding, so it's `LB` on a gamepad). The prompt is its own screen-space canvas using TMP and the Satoshi HUD font.
  - Pressing Interact calls `Character2D5Controller.SnapToLane(point, yRotation)`. This puts the player on the closest lane point with the lane's rotation, zero velocity, and the perpendicular axis locked.
  - Its `gateDoor` stays **locked** until the player snaps, and locks again on every `PlayerSpawned` (respawn). So the player can't skip the snap.
- **Enemies** take their lane from their Y rotation. Use **Tools > JunkLite > Level > Snap All Enemies To Lane Path**, or **Snap Selected To Lane Path**, after placing them.

## Doors

- Doors between rooms use `Prefabs/Environment/Doors/Door4_Sliding.prefab`. It's a variant of the Modular SciFi Pack `Metal/Door/Door4`.
  - **Door4.1** is the frame. Its mesh is replaced by `Door4.1_Cut.asset`, which has a walk-through hole cut where the leaves are (the original FBX has a solid back plate there). The frame has box colliders on the jambs and header only.
  - **Door4.2** is the left leaf and slides local −X. **Door4.3** is the right leaf and slides local +X.
- `SlidingDoor` (`Scripts/LEVEL/SlidingDoor.cs`) on the prefab root opens while the player is inside the root's trigger box.
  - The `Blocker` child is a solid box across the opening. It turns off once the door is fully open and back on as soon as the door starts closing.
  - It also opens for a living enemy's solid body (not its detection trigger), so chasing enemies follow the player through. Dead or disabled enemies are pruned each frame.
  - `Locked` keeps the door shut. Locks have owners (`AddLock(owner)` / `RemoveLock(owner)`): the door stays shut until every owner releases. `LaneEntryTrigger` and `DoorAccessRule` each hold their own lock on the same door.
- **`DoorAccessRule`** (`Scripts/Objectives/`) on a door keeps it locked until an objective is complete and/or the player carries a key (`requiredKeyTag`, a Unity tag held in the player's `PlayerKeyring`), or forever (`lockedForever`). While unmet, the door shows a "Locked" prompt and Interact plays `lockedDialogue`; once met it is silent. A `LaneEntryTrigger` gating the same door hides its own prompt until the rule is met.
- The prefab pivot is the frame's right edge (same as the pack). The door is 3.76 × 3 m at scale 1. Lane doors use Y rotation 270 (decorated face toward −X) and scale 2.87 to fill a 10.8 m bay.

## Combat and the arm weapon

- There is no combat mode and no weapon slots. `PlayerArmament` (on the player) holds exactly one `WeaponData`: fists by default, blades once acquired. `WeaponManager` always attacks with `PlayerArmament.Current`.
- **Blades pickup:** `Prefabs/New Weapons/sword prefab (2)` carries `WeaponUpgradePickup`. Inside its radius the shared prompt shows "Take Blades"; **E** calls `PlayerArmament.TryAcquire`, which is permanent (saved with the profile), swaps the held visual and plays the acquisition pose, VFX and `ScreenGlitchEffect`. A pickup whose weapon the profile already owns disables itself.
- Mods are always live (`ModManager` enables installed mods while the component is enabled). `ModData.OnCombatModeEntered/Exited` now mean enabled/disabled.
- Weapons have no durability and never break.
- **Upgrades.** Each level-up gives one upgrade point (bosses and pickups give more). Spend them in the **UPGRADES** tab (I menu): Blades, Vitality, Jump, Dash and Mods branches. Dash and double jump are always on (demo). A Mod Slot node unlocks one more slot. See `PROGRAMMER_AND_DESIGNER_GUIDE.md` §8.

## Level layout rules

- **Rooms** are named `Room 0 … Room N` under `[Environment]`, laid out along the lane in order.
- **Room 0** is the planned **3D exploration** hub. It has one box collider covering the whole floor, so the player can later walk into the foreground and background (via `allowZMovement`). Its left (west) wall is closed off by `Walls/WestWall` (art) and `Colliders/Walls/Wall_Room0_West`.
- **Rooms 1+** are **lane-only**: left and right movement on one lane.
- **Level collision lives under the `Colliders` root, not on art.**
  - Meshes in `[Environment]` have **no colliders**. The only exceptions are door prefabs, which need their trigger, blocker and frame colliders.
  - `Colliders/Floors` holds a few large `BoxCollider`s: one long strip per lane section, following the path (about 6 m deep around the lane), and one flat square per free-move room. Ramps go here too.
  - `Colliders/Walls` holds only what stops the player: free-move room perimeters and the ends of lanes.
  - Never use mesh colliders. Avoid rotated or negative-scale boxes unless it's a ramp.
  - When a lane is added or extended, add or stretch a floor strip to cover it.
- Lanes stay on world X or Z; use a corner trigger for every turn.
- **Hierarchy:**
  - `[Gameplay]` holds the spawn point, lane paths, lane entry triggers, and camera switch triggers.
  - `[Environment]/Shared` holds the shell pieces that span several rooms.
  - Everything else lives in `[Environment]/Room N/<Category>`. The categories are Ceiling, Doors, Dressing, Floors, Foreground, Gameplay, Lighting, Props, Structure, and Walls. A piece belongs to the room containing the centre of its bounds, and a door belongs to the room it opens into.
- **Collider tools** are under **Tools > JunkLite > Level**: *Replace Mesh Colliders With Boxes (Selection)* and *Remove Mesh Colliders (Selection)*. Each is one Undo step. Use them to strip imported art; level collision itself is authored by hand under `Colliders`.

### Agent checklist before saving a level edit
1. The player spawn point's yaw matches the first lane.
2. Every enemy sits on its lane line, rotated to that lane's yaw.
3. Every turn has a `CameraSwitchTrigger`. Its `A` and `B` points sit on the two lanes, its rotations match the yaw table above, and its cameras match those rotations.
4. Walkable surfaces along the lane have box colliders, there are no mesh colliders on gameplay geometry, and there's no negative scale.
5. Enter Play mode and walk the lane. Use `unity command capture_game_view` to check the camera, the turn, and that nothing blocks the view. Ask the user before saving the scene.

## Service Floor (Level 2) status and roadmap

`Scenes/Level 2/Service Floor.unity`. Rooms 0 to 8 run along +X:
- Room 0 (free-move) spans x −30 to −5.5.
- The lane bays are 20 m wide, with bay doors and piers at x 20, 40, 60, 80, 100, 120, 140 and 156:
  - Room 1 runs from x −5.5 to 20.
  - Rooms 2 to 7 each fill one bay.
  - Room 8 runs from 140 to 172 and has a second bay door at 156.
- The lane ends at x 172. The `L_Turn` geometry continues north (+Z) from x ≈ 154–178.

Done:
- **Spawn.** The spawn is in the middle of Room 0 at (−17.5, 0.2, 3), yaw 0.
- **Lane entry.** `[Gameplay]/LanePath_Room1-8` runs at z = 0 from x = −10 to x = 170. `[Gameplay]/LaneEntryTrigger_Room0` sits in front of `Room 0/Doors/Door_Entry` and gates that door.
- **Colliders.** There are 11 level colliders under `Colliders`:
  - Floors: Room 0's square, the lower lane strip (to x 88.2), the stairs ramp (x 88–94), the upper lane strip (x 94–172), and the Room 8 `L_Turn` floor.
  - Walls: Room 0's perimeter, with the east wall split around the door, and the east lane end at x 172.
  - Art has no colliders except the doors and the 3 `Window_VolumetricLight` trigger boxes.
- **Doors.** All doors are `Door4_Sliding`. `Door_Hazard` is centred on Room 3's opening (x 50), at the back of the hazard hall.
- **Stairs (Room 5).** The player climbs on `Colliders/Floors/Ramp_Room5_Stairs` (+14°). The steps are visual only.
- **Verified in Play mode.** The player stays grounded on the floor, ramp and upper floor through to x 164.
- **Art kit.** Only low-poly assets are used: Modular SciFi, CyberBox, the Cosmic retro computers, the Cyber chair and simple Neon City pieces. No Leartes or HDRP assets. Everything is visual only, with colliders stripped.
  - Floors:
    - Lane floors are a 4 m grid aligned to the piers, under `Floors/Floor_Lane` (Floor4_4 variants). The rows are mirrored about the lane, with a Floor2_2 strip along the back.
    - Halls use Floor4_4.1.
    - Tops sit at y 0.012 on the lower level and y 1.515 on the upper level.
  - Walls:
    - Long walls (back walls, hall walls, Room 0's back, east and west walls, the lane end and the L_Turn) use the same Wall2_3 tier columns, under `Walls/*_Panels`. Each wall is sized to its span and patterned symmetrically about its centre.
    - Piers, lintels, foreground posts, the valance and the fascia use Floor2_2.1 panels.
  - Ceilings: lane rooms copy Room 0's lattice ceiling (`Ceiling/Room0Style`). Halls use Ceiling4_4 tiles with light spines.
  - Back openings:
    - Room 2 shows `DeepServerHall` (x 20–40) and Room 6 shows the D2 hall (x 100–120). Each has 3×2 KitBash `SM_Objects_1` server rows and a back window.
    - Room 3 shows `HazardHall` (x 40–72, z 18–60, 22 m tall), with two mirrored reactors at x 45 and 55 flanking `Door_Hazard`.
  - Dressing (`Dressing/Symmetric`): each lane room has a matched pair of CyberBox stacks at the back and one `AccessTerminal` on its right-hand pier. Monitor banks stay centred on the back walls.
  - Window light in Rooms 1, 2 and 6 uses the Rooftop v2 volumetric setup (`Room N/Lighting/Window_VolumetricLight`):
    - A shadowed spot with `VolumetricAdditionalLight`, picked up by the volumetric fog in the camera's "Sky and Fog Volume Profile".
    - A `VolumetricLOD` trigger box that swaps it for a fake `GodRay_LightCone` cone while the player is outside the room.
  - **Skyline** (`[Environment]/Shared/Skyline`): the one exception to the no-Leartes rule. The `Prefabs/Environment/Skyline/SF_SkylineTower_*` variants copy Rooftop v2's `Fixed_Masked_Building*` (Leartes `SM_Test_LP`, `M_High_Rise`).
    - Towers are mega-city size, scale about 0.5–0.8, 33–63 m wide. Their bases always sit below the window view, so the level reads as high up.
    - `Skyline_West` (Room 1 window) and `Skyline_East` (Room 6) are sampled inside each window's view cone, a few towers each with gaps between them. Do not fill the cone: the warm window glow in Rooms 1 and 6 comes through those gaps. `Skyline_Gap` holds 3 narrow towers in the strip seen from Room 2 (x 13–28), between the West cluster and the Wing.
    - Towers stay out of the Wing (x 28–86, z 55+), the hazard hall and Rooms 10–12. The scene fog (exp², 0.014) hides anything past about 120 m from the camera.
    - The towers are Batching Static only: they cast no shadows, use no probes and are not in the bake. `OutsideBackdrop_Placeholder` stays as the sky behind them.
  - **Monitor desks** (`Monitors_*`, 17 of them):
    - Each `Desk` uses `SF_Durasteel`. Its `Desk_Dressing` child adds a top, a cyan neon edge, an amber kick strip, panel seams and keyboards.
    - The lower monitor row sits on the desk top. The upper row stands on a `Monitor_Shelf` with brackets.
    - All dressing is primitive cubes with no colliders, probe-lit.
  - **Room 8 back wall:** the 6 columns behind the monitors had only their top tier, and 5 grey `Clad` squares were patching the gap. The columns now have the full tier set (probe-lit), and the grey squares are gone.
  - **Front rail after the stairs:** the upper-floor rail (`Rail_*_East`, posts in Rooms 5–9, the `Room8 Collapse/Intact` rail) and its neon strips were buried at lower-floor height. They now sit 1.5 m higher on the upper floor. `NeonLine_094` fills x 94–100.
  - **Neon:** wall-line emission is ×4 on `SF_Metal_ServiceOrange` (Rooms 1+) and on the pack `Metal` (Room 0). The neon strips are ×2.5. The emission map's white lines now pass the bloom threshold (3) and glow; the green bands stay under it. The post profiles are unchanged.
  - **Room 0 exterior** (`Room 0/Structure/Exterior_Below`): the game camera sits in front of the room (z ≈ −26) and used to see open air past the west wall and under the floor.
    - A 60 m block under the floor adds storey bands every 4.5 m, glass strips and a few lit windows, so the tower continues downward.
    - `WestWall_Extension` carries the west wall to z −40.
    - Everything is probe-lit with no colliders.
  - **Piers (side walls):** all 44 `Pier_*` cubes are 10% thicker (0.9 → 0.99 m), and their clad panels moved out 4.5 cm to stay flush.
  - **Pit z-fighting fix (Room 8 collapse):** `Crater/Rim_West` and `Room 8 Basement/Chimney_RimWall` moved 4 cm off the upper-slab end faces at x 156 and x 168. `Crater/Rim_North` (on after the collapse) moved 4 cm forward off the z 6 faces of `Floor_Main_North` and `Floor_Upper_Back`; that was the dashed flicker on the pit's back wall. `Floor_Upper_West/East/Back` and `Intact/Floor_Upper_Hole` dropped 4 cm, so their tops no longer sit 2 cm under the floor tiles.
  - Rooms 1–8 are lit in sodium orange. The pack materials there are swapped to `SF_Metal_ServiceOrange`; Room 0 keeps the blue `Metal` look.
- **Lighting bake** (`Scenes/Level 2/Service Floor Lighting.lighting`, the scene's own settings file; it used to share Level 1's): ProgressiveGPU only, resolution 5, max lightmap 1024, samples 32/256/128, 2 bounces, directional lightmaps.
  - Static fixture lights (downlights, bay lights, teal fills, hall fills, reactor and sign glows) are **Baked**, with soft baked shadows. The exception is the `Down_*` ceiling lights: they sit above the lattice ceiling (y 10.8–12.3), so they cast no shadow, or the grid would block them.
  - These stay **Realtime**: `AlarmFlash`, `MonitorFlicker`, door glows, the hologram, and the volumetric window spots.
  - Sliding-door renderers are not Contribute GI. Props under 1.5 m, and meshes with no lightmap UVs, take their light from `[Lighting]/Adaptive Probe Volume` (global), which also lights the characters.
  - The modular kit FBXs (Modular SciFi `AssetsVer2`, Cyber_Box, Cosmic computers, Chair) now have **Generate Lightmap UVs** on.

Objectives and route (2026-10-13). Primary objective: GET TO THE ELEVATOR (`[Gameplay]/ObjectiveTracker`, assets in `Scripts/Objectives/ObjectiveSOs`).
1. **Room 0.** `The CyberBox` reveals `Contents` (the gun and `Item_Health`, a green cube with `HealPickUp` in interact mode + `ObjectiveLink` GET_HEALTH). `Door_Entry` has a `DoorAccessRule` needing GET_HEALTH; trying it early plays `ServiceFloor_DoorNeedsHealth`.
2. **Room 3** (safe zone): `ZMovementZone_Room3` (two boxes: bay + hazard hall), square floors and perimeter walls under `Colliders`, reactor boxes under `Colliders/Props`. `LaneEntryTrigger_Room3` (E) rejoins the main lane at `Door_Bay_48`.
3. **Room 3 `Z Door`**: `DoorAccessRule` needs the `Z Key` tag; locked → Crow `ServiceFloor_ZDoorLocked`. With the key, `LaneEntryTrigger_ZDoor` (E) snaps onto `LanePath_Wing` and switches to `NegXCam`.
4. **Room 9** stays on the lane. **Room 10** is free-move (`ZMovementZone_Room10`, `Floor_Room10`, walls). `[Gameplay]/Z Key CyberBox` replaces one prop box and holds `Item_ZKey` (`KeyPickup`, tag `Z Key`, `ObjectiveLink` GET_Z_KEY). `LaneEntryTrigger_Room10` (E) at `Door_Bay_112 (1)` rejoins the lane.
5. **Room 12 Door**: `DoorAccessRule` `lockedForever` (`ServiceFloor_Room12Locked`); Room 12 is visible but closed.
6. **Wing** (`[Environment]/Wing`, lane x = 50 running +Z, yaw −90, `NegXCam`): `Corridor_Z` (z 60–80), Rooms 1A–5A (two Room 11 bays each, 40 m, z 80–280), `Shaft` (z 280–293). Warm Room 11 lights plus red/orange `AlarmFlash` bulbs on the back walls. Floor strips `Floor_Wing_South/North`, `Wall_Wing_Start/End`.
   - **2A pit**: floor art removed at z 148–156, `BrokenPlate` juts to z 151, so the collider gap is 5 m (single jump ≈ 3.1 m, double jump ≈ 5.6 m with the Player_2.2 values). `FallRecovery_Room2A` returns the player to z 145 with 10 damage.
   - **5A**: `Brute Boss` at z 268 (`ObjectiveOnDeath` → DEFEAT_BRUTE); `Door_Shaft` needs DEFEAT_BRUTE.
   - **Service elevator**: `[Gameplay]/ServiceElevator` completes GET_TO_ELEVATOR and loads the next build scene (EndCard), or `nextSceneName` if set.

7. **Wayfinding.** `Room 3/Signage/Sign_Room3` ("SERVER ROOM" / "ROOM 3 // REAR ACCESS: Z DOOR") hangs above the opening into the hall, and `Hologram_ZDoor` (red `Hologram_Red` panel, Play-font TMP, pulsing red light) marks the Z Door. Taking the Z Key plays Crow's `ServiceFloor_ZKeyFound` ("head back to the Server Room, Room 3"), via `ObjectiveLink.completedDialogue`.
8. **Enemies.** Grunts use scene overrides (`Grunt Stats Service Floor`, 68 health = −15%; `pursuitRadius` 30 so they chase through doors). Groups under `[Enemies]`: `Main Hall` (Rooms 1, 2, 4–9, z = 0, yaw 0), `North Hall` (Room 11, x = 188, yaw 270), `Wing` (Corridor, 1A–4A, x = 50, yaw 270) and the Brute. None in the safe rooms (0, 3, 10).
   - **After the Room 8 collapse (harder second half):** `Hyena` prefab instances (pursuitRadius 30) and more grunts. `Basement` (Hyena + 2 Grunts at the right end of the service hall, x 194–202, facing left) and `Room 9` (Hyena + Grunt on the upper floor, x 177–183) start **inactive**; the last action of `Room8_Collapse` turns them on, so they can't start a fight that delays the collapse (it waits until out of combat). North Hall adds 2 hyenas (z 5, 60) and a grunt (z 67); the Wing adds hyenas in 1A–4A and grunts at z 116 and 194.
9. **Level Manager.** `LevelStatsTracker` (timer/kills), `LevelGoal` (Manual; counts scene enemies for the grade), `EncounterController` (one wave: the existing Brute), started by `[Gameplay]/EncounterStart_Brute` at the 5A doorway. The elevator completes the level through `LevelGoal`, so the Level Results Screen shows and its Continue loads EndCard.
   - **Mod drops (2026-10-31):** every Service Floor enemy has Drop Chance 0 (scene override), except `Hyena_Basement` (`Drops/Service Floor Dont Blink`, 100%) and the 4A grunts `Grunt_4A_B` (100%) and `Grunt_4A_C` (50%) with `Drops/Service Floor Passives` (Fire, Life Steal, Pogo; no Electric). An enemy with no custom table and chance > 0 falls back to the DropManager's Global Drop Table.
   - **Basement camera:** `BasementCam` Follow Offset y 6.65 (was 9.5, 30% lower).
   - **Music:** `SceneSettings` → Level Music = `MUSIC/Service Floor/ServiceFloor_Music_Loop` (vol 0.3) with intro `ServiceFloor_Music_Intro`, played through combat. Cut from the composer's `demo.wav`: the intro is the whole song (88.7 s) whose last 2 s crossfade into 0:50.93; the loop is 0:52.93–1:26 whose last 2 s crossfade back into 0:50.93 (loop start nudged −72 ms to line up with 1:26).
   - **Demo upgrades:** `[Gameplay]/Room1_DemoUpgrades` (Enemy Died: `Grunt_Room1`): Crow `ServiceFloor_DemoUpgrades` (needs continue) → +5 upgrade points once per save (`service-floor/demo-upgrades`) → opens the UPGRADES tab.
10. **Player and blades.** `SceneSettings` → Player Choice = New Player, so Service Floor always spawns `Player_Cass`. The `Sword CyberBox` pickup gives `Sword (Cass)`, shown as **Mantis Blades**: Cass's punch animations at base damage 18 (fists 10).
11. **Basement service hall (Room 8 collapse).** The basement runs from x 146 to 211 (east wall art inner face x 210.9).
    - `Chimney_RimWall` is now a header (y −5.5 to 1.3) over a doorway. `ServiceHall_Ceiling` fills the space under the upper floor. Colliders: `Floor_Basement` (x 146–211), `Wall_Basement_East` (x 210.9–212.4) and `Ceiling_ServiceHall` (x 169–211) under `Colliders/Basement`. Probe-lit, plus two realtime no-shadow lamps (`ServiceHall_Light_A/B`); nothing was rebaked.
    - Dressing (2026-10-31): `Room 8 Basement/Dressing` holds kit panels, ribs and pipes on the back wall (z 8), floor plates, hall ceiling beams, an amber kick strip and a cyan ceiling strip, cooling boxes, terminals, cables and lamp fixtures. All art only: colliders removed, Batching Static only, no lights, pack `Metal` swapped to `SF_Metal_ServiceOrange`. Everything sits behind the walk line (z > 6) or overhead. The greybox shell uses the untextured `SF_Cold_Floor/Wall/Ceiling` (textured trims stretch on the big cubes).
    - `Crater/Broken_Slab` (art + `Broken_Slab_Collider`, Wall layer) hangs from the upper floor down to y −5.5, so the hall is walkable under it; above that it is still the chimney's west wall. `ServiceHall_Ceiling` art stops at z −3.2 (cutaway, like the pit) so the basement camera sees the fight.
    - `Room8 Collapse/Intact` must be saved **on** and `Crater` **off** (the collapse timeline flips them). A save during a collapse preview once stored them flipped: Room 8's floor was missing on play and the full-height slab blocked the hall.
    - Exits: the pit has two climb-outs, west (`Wall_Pit_West`) and east (the chimney). `Room8_BasementExit` (x 169–173) and its child `Room8_BasementExit_West` (x 149–153) run the same actions (switch to `PosZCam`, complete `ESCAPE_BASEMENT`, tick the hint); both turn on with the parent.
    - Flow: the collapse no longer grants the hook. Crow sends the player down the hall, and the `GET_GRAPPLE_HOOK` objective starts. `[Gameplay]/Grapple Hook CyberBox` at x 205 holds `Item_GrappleHook` (`KeyPickup`, tag `Grapple Hook`, `ObjectiveLink` GET_GRAPPLE_HOOK). Taking it fires `Room8 Collapse/Basement_GrappleTaken` (Objective Completed), which starts `ESCAPE_BASEMENT` and shows the Aim Grapple hint. The player walks back to the pit and climbs out as before.

Next:
1. **Checkpoints.** There is one spawn (Room 0). Death anywhere returns there; wing checkpoints need a per-spawn camera and free-roam state first.
2. **Encounter lock.** Optionally lock the 5A entry door while the Brute encounter runs.
3. **Room 0 props** have no collision. Add simple boxes under `Colliders` if they should block.
