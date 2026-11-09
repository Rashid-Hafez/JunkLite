# SpineAnimationController

Plays the player's Spine animations. It only plays animations: it reads movement and state, and never changes gameplay. Script: `SpineAnimationController.cs`, on the player prefab root (`Player_Cass`, `Player_2.2`).

## Inputs

| Source | What it drives |
| --- | --- |
| `PlayerState` events | Grounded, jump, double jump, dash, wall attach/slide, ledge, parry, stun, death, attack requests |
| `Character2D5Controller` | Speed and input (Idle/Run/Run Stop, run speed), facing (which skeleton is shown, turns), double-jump callback |
| `PlayerCharacter.OnHurt` | Hurt on every applied hit |
| `PlayerCharacter.IsHeldByGrabber` | Grabbed loop (polled) |
| `GameInputManager.OnInteractionPerformed` | Interact clip |
| `PlayerArmament.WeaponAcquired` | Weapon acquired pose (`modCombatEntryAnim`) and VFX |
| `PlayerCharacter.OnRevived` | Back to Idle after respawn |
| Spine events | Footstep audio (`footstep`, `footstep_left`, `footstep_right`, or `footstepEvent`), attack `Hold` and `Release` |

## Tracks

- **Locomotion track (0):** Idle, Run, run stop, jumps, landing, dash, wall, stun/Hurt, grabbed, interact, death.
- **Overlay track (1):** attacks (unless `attackOverwrite` is on, which plays them on track 0). The parry hit clip (`ParryHandler.parryHitClip`) always plays on the overlay with no blend. Hurt also uses it while something else owns track 0.

## Every frame (`Update`)

1. `UpdateAttackHold`: count down a paused attack (see Attacks).
2. `UpdateGrabbedPose`: the grabbed loop owns the pose while she is held.
3. `SyncSkeletonToFacing`: on a left/right player, swap skeletons when facing changes.
4. Wall attach/slide owns the pose while attached.
5. `ApplyAnyStateFallbacks`: end a one-shot Hurt, fix a stuck air pose after landing, and force Jump Air while falling.
6. `UpdateLocomotionFromSpeed`: pick Idle, Run or Run Stop from input and speed (see below).
7. `UpdateRunSpeed`: Run time scale = `clamp(speed / MoveSpeed, minRunTimeScale, maxRunTimeScale) * runTimeScale`.

## Idle, Run and stopping

```
Run --(input released)------------------> Run Stop --(crossfade)--> Idle
Run --(speed under threshold, idleDelay)--^   |
 ^                                            |
 +----------(direction pressed, moving)-------+

Run --(flip, guard passes)--> part of Run Stop --(blend)--> Run
```

- **`speedThreshold`:** speed above this counts as moving.
- **`idleDelay`:** how long speed must stay under the threshold before Run gives way while input is still held. Short stops (door blockers, bumps) keep Run playing instead of restarting it.
- **`runStop`:** the stop clip (`RunToIdle` on `Player_Cass`). **Empty, or missing on the shown rig = skip it** and go straight to Idle after `idleDelay`.
- **Stopping:** letting go starts the clip at once, while `Character2D5Controller` is still easing speed down (`groundStopSlideDuration`). It plays at `runStopTimeScale` and crossfades into Idle over `runStopToIdleBlend`. Run must have played `turnStopMinRunTime` first, so a quick tap just goes back to Idle.
- **Turning:** flipping direction mid-run plays the first `turnStopPortion` of the clip, then blends into Run over `turnStopBlend`.
- **Spam guard:** `ShouldPlayTurnClip` needs `turnStopMinRunTime` of running and `turnStopCooldown` since the last turn clip. Otherwise she keeps Run, and the skeleton swap carries it over at the same time without a restart. Flipping again during the clip, or pressing a direction during a stop, blends straight to Run.
- **Reversal slide:** after a run of 0.5 s or more, reversing plays Run Stop on the old facing while `Character2D5Controller` slides her on (`IsTurnSliding`), then blends into Run at the flip.
- **`locomotionBlend`:** crossfade for the other Idle/Run/Landing changes, and into Run Stop.

## Hurt, Grabbed, Interact, Dash

- **Hurt (`stun` clip):** plays on every applied non-periodic hit (`PlayerCharacter.OnHurt`), never for a blocked, parried or invulnerable one. A stun holds it until the stun ends; otherwise it is a one-shot and locomotion returns when it completes. The stun that follows a hit finds it already current, so it doesn't restart. If an attack, dash, wall slide or override owns track 0, Hurt shows on the overlay and clears itself, so the swing's gameplay continues.
- **Grabbed (`grabbed`):** loops while `PlayerCharacter.IsHeldByGrabber`. The check is polled, so a throw, an escape, a cancelled grab or death all end it. A throw's flight goes back to the stun pose. A rig without the clip keeps the stun pose.
- **Interact (`interact`):** plays once on `GameInputManager.OnInteractionPerformed` (the press had a prompt to act on) when she is grounded, standing still and not attacking, then Idle. Moving cancels it into Run.
- **Dash (`dash`):** the clip (0.47 s) is longer than the dash (`dashDuration`), so only its held pose shows. `dashExitBlend` eases out of it. Touching the ground mid-dash doesn't replace it.
- The side rigs have clips the front/back rigs lack. `ShowSideRigIfMissing` moves her to the side view before such a clip plays.

## Left / right skeletons (`Player_Cass`)

The new Cass has two rigs, `Cass_R` (shown facing right) and `Cass_L` (shown facing left). One arm is orange and one is black, so the rig is never mirrored by scale (`Character2D5Controller` facing mode `SkeletonSwap`).

- Fill **Right Skeleton** and **Left Skeleton**. Leave both empty on the old single-skeleton `Player_2.2`, which uses **Skeleton Animation**.
- On a swap, every track (current and queued) is copied to the other skeleton at the same time, callbacks are re-hooked, and the old skeleton is cleared and hidden.
- **Bone Followers** (weapon socket, muzzle) are moved to the shown skeleton. Each follower's bone must exist on both rigs (`RightHand`).
- `wallPoseForwardOffset` is flipped by facing on a left/right player.

## Attacks

- `PlayAttackAnimation(name)` is called through `PlayerState.OnAttackAnimationRequested`. Missing animations report complete at once, so combat never waits on art.
- Only the current attack entry may finish or interrupt the attack. A late callback from an older clip is ignored.
- Time scale: `fistAttackTimeScale` (no weapons), `weaponAttackTimeScale` (upgraded arm weapon), `downAttackTimeScale` (down attack).
- **Blending:** `attackMix` from idle/run, `attackToAttackBlend` from one swing into the next (also while the last swing is still mixing out), `attackMixOut` back to idle/run.
- **`Hold` and `Release` Spine events:** on the current attack entry only. `Hold` pauses the clip for `attackHoldDuration` (divided by the attack speed upgrade); `Release` runs it at base speed times `attackReleaseSpeed` until it ends. A skeleton swap keeps the pause and does not fire events again.
- **Hit timing:** `MeleeComboStep.hitDelay` stays measured on the plain clip. `WarpAttackDelay` (`AttackHoldTiming.WarpDelay`) moves the hit window with the Hold and Release, and the swing stays uncancellable until the hit window has passed.
- `ForcePlayOverride(name, loop, onComplete)` takes over the skeleton for things like Ground Pound; `forceOverrideGroundedHoldDuration` waits before returning to Idle/Run.
- `PlayKnockdown` / `EndKnockdown` for knockdowns. After death only the death animation can play.

## Inspector checklist

| Field | Notes |
| --- | --- |
| Animation names | Must match the Spine export exactly (case-sensitive). Missing names are skipped safely. |
| Looping | Usually only Idle and Run loop. |
| Time scales | Per animation. Run also scales with speed. |
| Parry clips | `ParryHandler` **Parry Start Clip** / **Parry Hit Clip**, not on this component. |
| Locomotion Settings | `speedThreshold`, `idleDelay`, `locomotionBlend`, run time scale clamps |
| Footsteps | `footstepEvent` + `footstepSource`, or Spine events named `footstep*` |

## Rendering notes (Cass)

- Cass's renderers use **Motion Vectors: Force No Motion**, and `Cas_R_Material`/`Cas_L_Material` have their motion vector pass on (`_AddPrecomputedVelocity`). Together they keep the player out of camera motion blur. A new Cass material needs both.
- The Spine data asset scale is `0.02`. The prefab root scale sets the on-screen size.
