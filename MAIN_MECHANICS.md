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
- **Room 0** is the planned **3D exploration** hub. It has one box collider covering the whole floor, so the player can later walk into the foreground and background (via `allowZMovement`). It still needs a wall on its left side, so the player can't walk off the start of the level.
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
- Room 0 (free-move) spans x −30 to −5.
- Room 1 runs from x −5.5 to 16, then each room after that is 16 m wide, up to Room 8 at x 112 to 140.
- The `L_Turn` geometry continues north (+Z) from x ≈ 122–146.

Done:
- **Spawn.** The spawn is in the middle of Room 0 at (−17.5, 0.2, 3), yaw 0.
- **Lane entry.** `[Gameplay]/LaneEntryTrigger_Room0` sits in front of `Room 0/Doors/Door_Entry` and gates that door.
- **Lane.** `[Gameplay]/LanePath_Room1-8` runs at z = 0 from x = −10 to x = 138.
- **Colliders.** There are 11 level colliders under `Colliders` (down from about 945):
  - Floors: Room 0's square, the lower lane strip, the stairs ramp, the upper lane strip, and the Room 8 `L_Turn` floor.
  - Walls: Room 0's perimeter, with the east wall split around the door, and the east lane end.
  - Art has no colliders except the 11 doors.
- **Doors.** All doors are `Door4_Sliding`: 8 bay doors, `Door_Entry`, `Door_Hazard` and `Door_Turn`.
- **Stairs (Room 5).** The player climbs on `Colliders/Floors/Ramp_Room5_Stairs`, whose slope sign was fixed to +14°. The steps are visual only.
- **Verified in Play mode.** Spawn → prompt → E → snap → walk through every door, up the ramp, to x = 139.5.

Next:
1. **World rotation at the end of Room 8.**
   - `[Gameplay]/CameraSwitchTrigger_Corner` should turn the +X lane (yaw 0, `PosZCam`) onto a +Z lane through `L_Turn` (yaw −90, `NegXCam`). It is **not set up correctly yet**, so copy the Rooftop Scene v2 corner setup.
   - Extend `LanePath` with the +Z segment.
2. **Room 0.** Add the left wall, then build 3D roaming in the controller.
3. **Content.** Place enemies, then run *Snap All Enemies To Lane Path*.
4. **Room 0 props** have no collision now. Add simple boxes under `Colliders` when 3D roaming is built.
