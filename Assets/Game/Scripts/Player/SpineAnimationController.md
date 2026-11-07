# SpineAnimationController

Plays the player's Spine animations. It only plays animations: it reads movement and state, and never changes gameplay. Script: `SpineAnimationController.cs`, on the player prefab root (`Player_Cass`, `Player_2.2`).

## Inputs

| Source | What it drives |
| --- | --- |
| `PlayerState` events | Grounded, jump, double jump, dash, wall attach/slide, ledge, parry, stun, death, attack requests |
| `Character2D5Controller` | Speed (Idle/Run and run speed), facing (which skeleton is shown), double-jump callback |
| `WeaponManager.OnCombatModeChanged` | Mod combat entry pose (`modCombatEntryAnim`) and VFX |
| `PlayerCharacter.OnRevived` | Back to Idle after respawn |
| Spine events | Footstep audio (`footstep`, `footstep_left`, `footstep_right`, or `footstepEvent`) |

## Tracks

- **Locomotion track (0):** Idle, Run, run stop, jumps, landing, dash, wall, stun, death.
- **Overlay track (1):** attacks (unless `attackOverwrite` is on, which plays them on track 0). Parry hit (`Perry_2`) always plays on the overlay with no blend.

## Every frame (`Update`)

1. `SyncSkeletonToFacing`: on a left/right player, swap skeletons when facing changes.
2. Wall attach/slide owns the pose while attached.
3. `ApplyAnyStateFallbacks`: fix a stuck air pose after landing, and force Jump Air while falling.
4. `UpdateLocomotionFromSpeed`: pick Idle or Run from speed (see below).
5. `UpdateRunSpeed`: Run time scale = `clamp(speed / MoveSpeed, minRunTimeScale, maxRunTimeScale) * runTimeScale`.

## Idle, Run and stopping

```
Run --(speed under speedThreshold for idleDelay)--> Run Stop (one-shot, optional) --> Idle
 ^                                                       |
 +----------------- (speed over threshold) --------------+
```

- **`speedThreshold`:** speed above this counts as moving.
- **`idleDelay`:** how long speed must stay under the threshold before Run gives way. Short stops (door blockers, bumps) keep Run playing instead of restarting it.
- **`runStop` (template):** name of a short stop animation. **Empty = skip it** and go straight to Idle, which is the current behaviour. To use it:
  1. Add the animation in Spine and export (both `Cas_R` and `Cas_L` need the same name).
  2. Type its name into **Run Stop** on the controller, and tune **Run Stop Time Scale**.
  3. It plays once, then the queued Idle takes over. Moving again during it cancels it back into Run.
- **`locomotionBlend`:** crossfade for Idle/Run/Run Stop/Landing changes.

## Left / right skeletons (`Player_Cass`)

The new Cass has two rigs, `Cass_R` (shown facing right) and `Cass_L` (shown facing left). One arm is orange and one is black, so the rig is never mirrored by scale (`Character2D5Controller` facing mode `SkeletonSwap`).

- Fill **Right Skeleton** and **Left Skeleton**. Leave both empty on the old single-skeleton `Player_2.2`, which uses **Skeleton Animation**.
- On a swap, every track (current and queued) is copied to the other skeleton at the same time, callbacks are re-hooked, and the old skeleton is cleared and hidden.
- **Bone Followers** (weapon socket, muzzle) are moved to the shown skeleton. Each follower's bone must exist on both rigs (`RightHand`).
- `wallPoseForwardOffset` is flipped by facing on a left/right player.

## Attacks

- `PlayAttackAnimation(name)` is called through `PlayerState.OnAttackAnimationRequested`. Missing animations report complete at once, so combat never waits on art.
- Only the current attack entry may finish or interrupt the attack. A late callback from an older clip is ignored.
- Time scale: `fistAttackTimeScale` (no weapons), `weaponAttackTimeScale` (mod combat), `downAttackTimeScale` (down attack).
- `ForcePlayOverride(name, loop, onComplete)` takes over the skeleton for things like Ground Pound; `forceOverrideGroundedHoldDuration` waits before returning to Idle/Run.
- `PlayKnockdown` / `EndKnockdown` for knockdowns. After death only the death animation can play.

## Inspector checklist

| Field | Notes |
| --- | --- |
| Animation names | Must match the Spine export exactly (case-sensitive). Missing names are skipped safely. |
| Looping | Usually only Idle and Run loop. |
| Time scales | Per animation. Run also scales with speed. |
| Locomotion Settings | `speedThreshold`, `idleDelay`, `locomotionBlend`, run time scale clamps |
| Footsteps | `footstepEvent` + `footstepSource`, or Spine events named `footstep*` |

## Rendering notes (Cass)

- Cass's renderers use **Motion Vectors: Force No Motion**, and `Cas_R_Material`/`Cas_L_Material` have their motion vector pass on (`_AddPrecomputedVelocity`). Together they keep the player out of camera motion blur. A new Cass material needs both.
- The Spine data asset scale is `0.02`. The prefab root scale sets the on-screen size.
