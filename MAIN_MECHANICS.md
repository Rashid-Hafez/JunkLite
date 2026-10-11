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

- **Free-move rooms** (safe rooms, explore rooms such as Service Floor's Room 0) are where the player can roam. Full 3D roaming is not built yet: the controller does not apply depth input (`zMoveSpeed` is unused), so for now the player still moves along X at the spawn depth.
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
  - `Locked` keeps the door shut, which is how `LaneEntryTrigger` gates its exit door.
- The prefab pivot is the frame's right edge (same as the pack). The door is 3.76 × 3 m at scale 1. Lane doors use Y rotation 270 (decorated face toward −X) and scale 2.87 to fill a 10.8 m bay.

## Combat / mod mode

- Pressing Q, or clicking the left stick, fires the `CombatMode` input, which calls `WeaponManager.TryToggleCombatMode()`. That needs a weapon equipped, and fails if the player is mid-attack or input-locked. On success it raises `OnCombatModeChanged`.
- In mod mode, weapons are visible and mods are active (`ModData.OnCombatModeEntered/Exited`).
- `CombatModeScreenGlitch` (on the player prefab) plays a short screen glitch on each switch.

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
  - `Window_Outside/OutsideBackdrop_Placeholder` stands in until the low-poly skyline is built.
  - Rooms 1–8 are lit in sodium orange. The pack materials there are swapped to `SF_Metal_ServiceOrange`; Room 0 keeps the blue `Metal` look.

Next:
1. **World rotation at the end of Room 8.**
   - `[Gameplay]/CameraSwitchTrigger_Corner` (now at x 158) should turn the +X lane (yaw 0, `PosZCam`) onto a +Z lane through `L_Turn` (yaw −90, `NegXCam`). It is **not set up correctly yet**, so copy the Rooftop Scene v2 corner setup.
   - Extend `LanePath` with the +Z segment.
2. **Room 0.** Build 3D roaming in the controller. The left wall art and its collider (`Wall_Room0_West`) are in place.
3. **Content.** Place enemies, then run *Snap All Enemies To Lane Path*.
4. **Room 0 props** have no collision now. Add simple boxes under `Colliders` when 3D roaming is built.
