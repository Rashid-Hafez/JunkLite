# JunkLite Architecture Refactor Handoff

### New Cass player: left/right skeletons, no scale flip (2026-10-24)

- **Why:** the new Cass has one orange and one black arm, so mirroring one skeleton by scale would swap the arms. `Prefabs/PLAYER/Player_Cass.prefab` (a full copy of `Player_2.2`) shows `Cass_R` while facing right and `Cass_L` while facing left (`02_CHARACTERS/Cassidy1/`). Neither is ever mirrored.
- **Facing is a value:** `Character2D5Controller.FacingMode.SkeletonSwap` never scales or turns the root for facing. It stores a bool, exposes `FacingSign` (+1/-1) and raises `FacingChanged` only on a real change. `SetFacingDirection` is the single writer for every mode. `ScaleFlip` remains only for the old `Player_2.2` prefab; delete it when that prefab retires.
- **Readers:** never read `localScale.x` for facing. `WeaponManager.Facing`, `EnergyWaveMod`, `DontBlinkMod` (which now turns via `SetFacingDirection` instead of writing scale), `HyenaBrain.IsPlayerFacingMe` and `PetDrone` use the controller (scale fallback kept for non-controller targets).
- **Mirrored children:** `FacingMirror` (Character) negates an authored "facing right" local X and, with Turn On Y, turns a VFX group 180° so child offsets and particles mirror. On the new prefab: attack transforms, LedgeCheck, wallCheck, groundCheck, Feet Ground, Effect Transform, Mod Trackers, BODY SPINE, Bullet Spawn (1), and Character Effects (turned). The capsule is centered (x 0) and every child was shifted by the old -0.175 offset, so facing mirrors about the body. The LedgeCheck box center was baked into its transform. Never put `FacingMirror` on a ScaleFlip player (double mirror) or on a BoneFollower.
- **Skeleton swap:** `SpineAnimationController` (`rightSkeleton`/`leftSkeleton`/`boneFollowers`) polls `IsFacingRight` in `Update`. A swap copies every track (current + queued: animation, loop, time, time scale, alpha, blend), clears the hidden state (End events only, so no Complete/Interrupt runs twice), re-hooks the callbacks this controller relies on (attack finish/interrupt via `HookAttackEntry`, jump-air hold, double-jump flag), re-points the weapon/muzzle `BoneFollower`s (`RightHand` on both rigs) and forces one skeleton update so no frame shows a stale body. Swaps wait while a force override (or knockdown) runs, because its restore callback lives on its entry. Mixes are configured and the Spine event is subscribed on both skeletons; only the shown one updates. Empty fields = old single-skeleton behavior.
- **Other:** `PlayerCharacter` drives every `SkeletonGhost` (one per side; the hidden one is inactive). `PulseBarrierVisual` follows the first visible body mesh under its orientation transform. `PlayerWeaponLoadout.weaponSortingOrderFacingLeft` (-1 on the new prefab; body is 0) draws the weapon behind the body when the right hand is the far arm.
- **Switching players:** `PlayerLifecycle` has `newPlayerPrefab` + `useNewPlayer`; `PlayerPrefab` returns the one the next spawn uses. Both `Game Root.prefab` and the legacy `Game Manager.prefab` (used by the build scenes) point at `Player_Cass` with the toggle on. `rashid LVL1.unity` has its own scene `GameManager` without these values.
- **Art import:** `Cas_R/L_Material` now copy the old `Cas_Material` (URP Complex Lit, cutoff 0.39) with the new textures; texture import settings and skeleton scale (0.02) match the old Cas. The new atlases are exported premultiplied (`pma:true`) while the lit material expects straight alpha; re-export with premultiply off if edges look dark.
- **Validation:** compile clean; `DamagePipelineTests` and `PlayerLifecycleConfigurationTests` pass. 16 encounter/enemy EditMode tests fail independently of this change (missing `ownsSerializedConfiguration` on `MeleeChaserBrain`/`HyenaBrain`, encounter edit-mode `Destroy`); `Tools > JunkLite > Systems > Validate` targets the removed `V2.5.unity`. Service Floor Play Mode: natural spawn of `Player_Cass`, facing swaps (idle, jump start with queued air, jump air, attack whose finish callback still fires after the swap), mirrors, follower retargeting, dash ghosts, zero console errors; the old prefab still flips and attacks. Not yet hands-on: weapon pickup draw order, grapple, mods, Pulse Barrier, Hyena, drone, lane corners, death/respawn.
- **Merge with the grunt redesign:** `HookAttackEntry` now carries its guard (only the current attack entry may finish or interrupt the attack), so both normal attacks and swap re-hooks use it.

### Per-enemy hit reactions and pass-through bodies: Phase 6 (2026-10-24)

Plan: `GRUNT_BEHAVIOR_PLAN.md` section 9. Compiles in both assemblies; not verified in Play Mode.

- **Launch is a per-enemy toggle.** `EnemyCharacter.canBeLaunched` now defaults to **false** (opt-in) and sits under "Launch (heavy hits)" with `EnemyLaunchSettings`. `EnemyBrain.Start` registers `LaunchedState` for every brain (`RegisterSharedReactionStates`), so any enemy with a dynamic Rigidbody under gravity launches once the box is ticked; `GruntBrain` and `HyenaBrain` no longer register it themselves. Robot and Brute ticks already stop on `IsForcedState`, which includes `LaunchedState`. The Grunt prefab has it on; every other prefab is off (the Hyena was launchable by accident before). `Validate Enemies` reports a launchable enemy without a dynamic, gravity-driven Rigidbody.
- **Pass-through bodies.** New `EnemyCharacter.passThroughEnemies` ("Body" header) adds the `Enemies` layer to `excludeLayers` on the enemy's solid colliders at `Awake`; either side of a pair having it on removes the contact. `excludeLayers` affects contacts only, so weapon, mod and sensor queries are unchanged. Launch pass-through (`SetEnemyBodyPassThrough`) now combines with the toggle instead of undoing it on landing. On for Grunt, Dummy, Patrol Dummy and Flying Dummy (so nothing is ever blocked by a dummy); Hyena, Robot and Brute still collide with each other and with grunts only where neither has it on.
- **No stacking among fighting grunts, without physics.** `EnemyEngagementDirector.IsLaneMember` now also requires `IsHoldingPlace`: an enemy that is stunned, parried, launched, idle (get-up recovery), alerting, searching, patrolling or dead is no slot, leader or follower for anyone else, so lane-mates walk through it instead of queueing behind it forever. The asking enemy is always in its own lane (unchanged `BuildLane` rule), so its own queries and the ally-hit ripple still work. The single attack lease still means only one grunt attacks at a time; the spacing solver and `HoldPositionState` separation keep active lane-mates apart. The grunt body-ahead probe now sees walls and door blockers only.
- **Grunt patrol** `wallLayer` is Default + Wall (Enemies removed): patrols pass through other enemies and turn at walls and ledges.
- **Draw order.** `EnemySpineAnimationController.overlapDepthStep` (0.01) offsets each enemy's Spine object along local Z by instance (8 steps) so overlapping bodies keep a stable draw order instead of flickering (all grunts share sorting order 42).
- **Launch hurtbox (playtest after Phase 6).** Air hits that visibly overlapped a launched or downed grunt missed: the drawn body is tilted and offset ~1.4 u from the upright capsule (and the death-fall pose stretches it sideways). `LaunchedState` now turns on `EnemyAnimationPresenter.SetVisualHurtboxActive`; the Spine presenter keeps a child "Launch Hurtbox" `BoxCollider` on the Spine object fitted to the skeleton mesh bounds every `LateUpdate` (`launchHurtbox`, `hurtboxDepth`). It is on the `Damageable` layer (in the player's weapon mask, not in the mods' Enemies masks or any AI query, so area mods that do not deduplicate receivers cannot hit twice) and has `excludeLayers = ~0`, so it has no physical or trigger contact. Weapon hits resolve it to the enemy through `GetComponentInParent`. The sword-grapple mask also includes Damageable.
- **Push follows the swing.** Knockback, launch drift, juggles and the spike pushed away from the attacker's position, which flips once the player passes the enemy's centre while the bodies overlap (common in the air, and the drawn launched body sits ~1.4 u from the capsule). `HitReactionRequest.PushDirection` (zero by default) now carries the melee swing direction (`WeaponAttackExecutor.SwingDirection` = facing axis times facing, passed through `WeaponDamageResolver`); `EnemyCharacter.PushSign` and `ApplyKnockback` use it when present and fall back to away-from-source otherwise, so blasts, ranged hits and enemy attacks are unchanged.
- **Verify in Play Mode:** grunt chases straight through a dummy; a stunned or knocked-down front grunt is passed and the passer attacks; three grunts settle apart after knockback and never attack together; hyenas and robots behave as before; no flicker where bodies overlap; Rooftop and Service Floor encounters play end to end.

### Grunt presence, solid bodies and the launch: Phase 5 (2026-10-23)

Design: `GRUNT_BEHAVIOR_PLAN.md` section 8. Packages A–E are implemented and compile in both assemblies via `dotnet build`. **Not yet run in Play Mode.** The prefab, weapon asset and ProjectSettings edits were made as text with the Editor closed, so the Editor must re-import (six new scripts need `.meta` files) before anything is verified.

- **Sensor layer (A).** New layer 18 `EnemySensor`. Enemy trigger volumes (`EnemyPerception` spheres, enemy-owned `Hitbox` objects) live on it; `EnemySensorLayer.Apply` enforces that from their `Awake`, so prefab layers are a mirror, not the source of truth. Matrix: `EnemySensor` collides with Player and Damageable only; Enemies ↔ Player is off. Enemy bodies stay on `Enemies`, so every existing enemy mask (weapon hit, mods, leap landing, death exclusion) is unchanged, and enemy–enemy collision is unchanged. `EnemyCharacter.TryIgnorePlayerBodyCollision` is gone. Grunt, Hyena and Brute prefabs had their sensor objects moved to layer 18 in the text edit where the object name matched; the Hyena `Detection` and Brute `Hitbox` objects rely on the runtime enforcement until the prefabs are saved from the Editor.
- **Measured locomotion and the body probe (B).** `EnemySpineAnimationController`: in Chase, Patrol, Hold and BackOff the clip follows the measured lane speed (`EnemyMovement.Velocity` along `MovementAxis`, smoothed, `moveStartSpeed` / `moveStopSpeed` hysteresis); `SetLocomotionSpeed01` hints from states are ignored there. `locomotionState` is cleared on every state change before the attack/leap/launch early returns. The Mecanim presenter drives `Speed` from the measured value too. `GruntBrain.UpdateProbe`: one `OverlapBoxNonAlloc` ahead toward the player at `probeInterval` (Enemies plus obstacle layers, airborne and dead bodies skipped) becomes a virtual leader in `ApplySpacing` when nearer than the director's leader (`WithProbeLeader`). `IsBlocked` after `blockedAfter` seconds of commanded-but-not-moving forces an immediate re-evaluation and pokes the idle grunt ahead with `Perception.RefreshOverlap()` (rate-limited).
- **Patrol and idle (C).** `IPatrolRhythm` (grunt): end-of-leg pauses, occasional mid-leg look back. `PatrolState` falls back to the plain loop without it. Idle fidget (`UpdateIdleFidget`) runs from `GruntBrain.TickBrain` outside combat. Grunt prefab: `patrolWhenPassive` on with `patrolDistance` 3 (every grunt patrols unless a scene instance sets 0), `wallLayer` Default | Wall | Enemies, `wallCheckDistance` 1 so the ray reaches past the capsule. `PatrolBehavior.IsLedgeAhead` (new on `IPatroller`) reverses when there is no floor one step ahead (`groundLayer`, empty = Default + Ground + Wall; `ledgeCheckDepth` 1.5), and `Initialize` now snaps the wall/ledge ray axis to the lane instead of world X (pre-existing bug for Z lanes).
- **Noticing (D).** `EnemyPerception` facing cone (`useFacingCone`, `rearRadius`; grunt prefab on, 2 u): a player behind the facing is only a valid target inside the rear radius. `Perception.Alert(seconds)` bypasses the cone, re-checks tracked colliders and re-scans; `EnemyCharacter.ReceiveDamage` calls it on every applied non-tick hit. `AlertState` (`IAlerter`): `GruntBrain.OnTargetChanged` enters it instead of combat on first sight; `OnAlertComplete` re-runs the normal target path (`alertResolving` guards re-entry); a hit during the beat completes it first from `HandleDamageResolved`. `PackAlert` ripples `Perception.Alert` to idle grunts within `packAlertRadius`, nearest first, `packAlertStep` apart, using the new static `EnemyCharacter.ActiveEnemies` registry (enable/disable). `SearchState` (`ISearcher`): after the chase to the last known position, look both ways, then `patrol.Initialize` re-anchors and `ReturnToPassive`; `searchPending` is set when the target is lost in combat and the no-target branches of `TickBrain` / `EvaluateNextAction` route through it.
- **Launch (E).** `HitReactionKind { Flinch, Launch }` on `HitReactionRequest` (default Flinch, every existing call site unchanged); `MeleeComboStep.reactionKind`; Sword `Attack_3_Dash` and Fist step 3 are Launch with knockback (4, 9) and (3.5, 8). `WeaponDamageResolver` / `WeaponAttackExecutor` pass the kind; `WeaponManager.launchHitstopDuration` (0.12) feeds `WeaponAttackExecutionSettings.LaunchHitstopDuration`. Receiver: `EnemyCharacter.canBeLaunched` (Brute prefab 0) and `EnemyLaunchSettings` (gravity 2, one bounce at 0.35, downed 0.5–0.9 s capped at 1.5 s, get-up 0.3 s, 3 juggles, tilt 75/90). `ReceiveDamage` → `TryBeginLaunch` → `LaunchedState` (Air → Bounce → Downed → GetUp). While not interruptible the state owns hits through `OnHitWhileLaunched` (juggle with lift, ground hits extend the lie-down); the get-up is interruptible (normal flinch). `ApplyStatusEffectSnapshot` defers crowd control until `OnLaunchFinished`. A launching kill throws the corpse (`EnemyMovement.Launch(..., endOnLanding: true)`); `DeathSequence` already waits for the ground. `EnemyMovement`: `Launch`, `SetLaunchVelocity`, `TryConsumeLanding` (impact speed from the step before contact), `EndLaunch`, `ReleaseLaunchToPhysics`, `CanBeLaunched`, launch-only gravity scale; knockback and voluntary movement are suspended while launched. Presenter: `PlayLaunched`, `PlayLaunchBounce`, `PlayDowned`, `PlayGetUp`, `SetBodyTilt` (Spine rotates the skeleton object around `tiltPivotHeight`, dropped to the floor when downed; Mecanim no-ops). The director treats launched bodies like leaping ones (`IsAirborne`). `LaunchedState` is registered by `GruntBrain` and `HyenaBrain`; `MeleeChaserBrain.IsDecisionLocked` and `EnemyBrain.IsForcedState` include it.
- **Ground check from the feet (first playtest fix).** Enemy roots sit at the body centre (the grunt root is ~1.3 u above the floor), and `EnemyMovement.CheckGrounded` cast 0.15 u down from the transform, so grunts were never grounded. The launch therefore never saw a landing (it timed out after `maxAirTime` with the tilted body hovering at capsule height) and `DeathSequence` always waited out its 3 s ground timeout. `CheckGrounded` now casts from `GetFeetPosition()` (bottom of the body collider bounds), ignoring triggers. `PatrolBehavior.IsLedgeAhead` and the grunt probe box use the feet / the centre accordingly. The Spine downed pose rests the body centre `tiltGroundClearance` (0.35) above the floor instead of half-sunk.
- **Get-up pacing (second playtest fix).** The lie-down was 0.5 to 0.9 s and the get-up snapped the tilt from 90 to 0 in 0.12 s while turning to face the player, which read as a flip straight to upright. Now `downedDurationRange` is 0.8 to 1.2 s (cap 2 s), `getUpDuration` is 0.45 s and the body rises over the whole get-up (`SetBodyTilt` takes a blend time; the Spine presenter derives a degrees-per-second rate from it), and the enemy turns to the player only once it is standing (`Finish`). `EnemyCharacter.ReceiveDamage` now calls `Perception.Alert()` before the damage resolves, so an unaware grunt acquires its target and `GruntBrain.HandleDamageResolved` can skip the alert beat before the launch starts; previously the alert began after the launch and the grunt never entered combat.
- **Stay horizontal while being hit (third playtest fix).** The user preferred the earlier, accidental behaviour: with landings undetected the grunt stayed in the air phase, every hit reset it as a juggle, and it stayed horizontal for as long as it was hit. With landings working, a hit during the get-up was a normal flinch (`CanBeInterrupted` was true there) and snapped the body upright into `StunnedState`. Now `LaunchedState.CanBeInterrupted` is always false: a hit while rising knocks the grunt back down for `knockDownAgainTime` (0.4 s), sharing the `downedMaxTotal` budget with ground hits; once the budget is spent the get-up has armor (damage, no reaction). Separately, a grunt killed while tilted had the Death clip (pelvis rotates about 95 degrees, it lays the body down by itself) added on top of the 75 to 90 degree tilt, standing the corpse on its head; `EnemySpineAnimationController.PlayDeath` now blends the tilt to 0 over the death clip. `EnemySensorLayer` resets its cached layer index on `SubsystemRegistration` (Unity analyzer UDR0001).
- **Launch tuning (fourth playtest).** The launch was slow and mostly vertical: (4, 9) at gravity 2 peaked ~2 u over ~0.9 s, and the grunt behind stopped it horizontally (enemy bodies collide). Now Sword step 3 is (9, 10) and Fist step 3 is (8, 9) at launch gravity 3.5 (peak ~1.5 u, ~5 u back, ~0.6 s). `EnemyLaunchSettings.passThroughEnemies` (on): `LaunchedState` calls `EnemyCharacter.SetEnemyBodyPassThrough`, which adds the Enemies layer to `excludeLayers` on the body colliders it did not already exclude and removes exactly those on exit (a corpse keeps its death exclusion). The bounce sends 0.55 of the impact speed back up under its own `bounceGravityScale` 2 (`EnemyMovement.SetLaunchGravityScale`), ~0.75 u and ~0.55 s instead of a 0.2 s twitch; a juggle restores the flight gravity, and `juggleLift` is 8. **Testing value:** `Grunt Stats.asset` health is 1000 (was 80); Service Floor grunts use `Grunt Stats Service Floor.asset`, unchanged. Put it back before a build.
- **Vertical launch (fifth playtest).** The user wants the finisher to go up, not sideways: Sword step 3 is now (2.5, 13) and Fist step 3 (2, 12) at launch gravity 3.5 (~2.5 u up, ~2 u back, ~0.75 s).
- **Combo stuck on the first hit (player, pre-existing).** The player attack clips play on the base Spine track (`attackOverwrite` is on in `Player_2.2`). When an attack ended and the locomotion name was unchanged (idle before and after), `PlayLocomotion` skipped, so the finished clip stayed current on the track. The next attack replacing it made Spine fire `Interrupt` for the OLD entry after `attackActive` was already true for the new one; `OnAttackInterrupted` then reported the new swing as interrupted, `WeaponManager.OnAttackInterrupted` cancelled it and `CombatState.OnAttackInterrupted` reset the combo to step 0. Spamming while standing still looped attack 1; moving between hits hid it. `SpineAnimationController` now only honours `Complete` / `Interrupt` from the entry that is `currentAttackEntry` (also stops a clip that is mixing out from ending the next swing early). Also fixed in `CombatState.TryBeginAttack`: `comboActive` was cleared before the air-continuation check read it, so a jump mid-combo always restarted the chain.
- **Fast launch, bounce on contact, patrol pacing (sixth playtest).** Sword step 3 is (2.5, 18), Fist step 3 (2, 17), launch gravity 6 (~2.7 u up, ~0.6 s). The bounce fired before the sprite reached the floor because the flying tilt rotated the sprite around its middle (`tiltPivotHeight`) above the upright capsule; the air tilt now uses the on-ground placement too, so the sprite's lowest point follows the capsule's feet. The bounce is small and quick again (restitution 0.3, `bounceGravityScale` 6, ~0.25 u, ~0.2 s). Patrol: `PatrolState.EndLeg` no longer turns to the next direction before the pause (it read as turn-then-stall); the grunt stops facing forward and turns when it walks off. `patrolEndPauseRange` is 0.4 to 1 s and `patrolLookBackChance` defaults to 0. Scene: the `Gun Prefab 1` instance was removed from `SANDBOX.unity` by a text edit (prefab-instance block and its `SceneRoots` entry; nothing else referenced it); SANDBOX also logs a pre-existing missing prefab, `Grunt Enemy` guid `e277ab05b058b0e4aac949aa5d972516`, not touched.
- **Launch tuning, seventh playtest.** Higher, faster, almost straight up: Sword step 3 (0.5, 25), Fist step 3 (0.4, 24), launch gravity 8 (~4 u up in ~0.32 s, down in ~0.64 s, ~0.3 u sideways). Bounce barely visible: restitution 0.1, horizontal keep 0.2, bounce gravity 8 (~0.04 u). `juggleLift` 12 to suit the heavier gravity. Tall launches can now reach ceilings in indoor rooms; physics stops them there.
- **Landing without a physics rebound (eighth playtest).** At 50 Hz a 25 u/s fall moves 0.5 u per step, so the body ended the step inside the floor and the solver pushed it out at up to `m_DefaultMaxDepenetrationVelocity` (10 u/s), a ~0.6 u rebound before the authored bounce. `EnemyMovement.TryLandThisStep` (launch only) raycasts the floor within this step's travel and sets the vertical velocity so the feet arrive on it, then reports the landing with the real impact speed. Because the authored bounce (~0.04 u) is now lower than the ground check's reach, `LaunchedState` goes Downed once a bounced body is grounded and no longer rising.
- **Ninth playtest: follow-up hits, combo window, flag mismatch.** The "very high bounce" was the player's follow-up hits: in the Air phase after the landing every hit was a juggle (lift was the larger of the hit's own knockback y and `juggleLift` 12, ~0.9 u). Now a hit after the first landing goes straight to Downed, a juggle before it uses a fixed `juggleLift` 6 (~0.25 u) and `juggleHorizontalScale` 0.15. `Player_2.2` `comboWindow` 0.2 to 0.45 s and `bufferDuration` 0.2 to 0.3 s. `SpineAnimationController.PlayAttackAnimation` no longer calls `ForceFinishAttack` when its `attackActive` disagrees with the weapon manager (that reported the NEW attack complete); it drops the stale clip silently. `FeedbackManager` keeps the pre-hit-stop time scale across overlapping hit-stops (a second one used to save 0 and restore 0, freezing the game). The ground/air sample, the empty fist air chain, the up-input threshold and the event-driven design below are unchanged.
- **Tenth playtest: still a big bounce, alert pause.** Not caused by follow-up hits, physics materials (none are bouncy), saved values (none), or other weapon assets (player fists use `Weapons/Fist.asset`, SANDBOX swords use `Weapons/Sword.asset`). `TryLandThisStep` used to leave the body arriving at the floor still moving at up to ~20 u/s downward; it now teleports the feet onto the floor (`rb.position`) with zero vertical speed. `EnemyLaunchSettings` gained `bounceEnabled` (off = straight to Downed), restitution 0.08, and `debugLog`, which logs launch velocity, apex, impact speed, expected and real bounce height per grunt. **If the bounce is still high in Play Mode, turn on Debug Log on the grunt and read the numbers before changing anything else.** Alert: a grunt that spots the player while in `PatrolState` skips `AlertState` and chases at once; idle guards keep a 0.15 to 0.25 s beat; `PackAlert` now runs on every first sighting.
- **Eleventh playtest: floor clipping, launch height.** The tilted sprite was placed with a fixed thickness guess (`tiltGroundClearance`), and the hurt pose is thicker, so it clipped into the floor on landing and while lying. `EnemySpineAnimationController.fitTiltedBodyToFloor` (on) samples the sprite's lowest rendered point relative to the enemy root while standing and grounded, and while tilted on the ground line moves the Spine object so `MeshRenderer.bounds.min.y` sits on that line (feedback form, so it converges whether bounds update immediately or a frame late; fades out below ~30 degrees). The jump had looked lower since the air tilt moved to the feet line (the sprite drew ~1 u lower on the same arc); Sword step 3 is now (0.5, 27) and Fist step 3 (0.4, 26), ~4.6 u.
- **Twelfth playtest: gap under the lying body.** The floor fit used the standing sprite's lowest point as the floor (the grunt's sprite origin sits ~0.05 local above the capsule bottom) and the renderer's box (over-reaches when rotated). Now the floor is the capsule bottom (`EnemyMovement.GetFeetPosition`, sampled relative to the root while standing), the lowest point is the lowest skeleton-mesh vertex in world space (`MeshFilter.sharedMesh.GetVertices` into a reused list, only while tilted), and `lyingFloorInset` (0.05 local, tunable live) lets rectangular image edges dip below the floor so the visible art touches. The lying pose is identical every launch, so one inset value holds.
- **Thirteenth playtest: knockdown on the authored fall.** The gap under the lying body persisted with the measured fit, while the corpse (Death clip, no tilt) lay flat. The collider is not involved: the capsule stays upright under the sprite in both cases and never moves the art. Now `EnemySpineAnimationController.PlayDowned` returns true and plays the Death clip, holding it at `downedHoldTime` (0.4 s: the grunt's bones finish at 0.38 s, the dead-face swap is at 0.48 s); `LaunchedState.EnterLyingPose` then blends the flight tilt to 0 over that fall (the same handover the corpse uses), so the lying pose is the animator's and rests on the floor. `PlayGetUp` drives the same entry's `TrackTime` backward over the get-up, then Idle. Knocked down again while rising falls forward from the current time; dying while down lets the clip run on to the dead face. `downedUsesDeathFall` off restores the tilted Hurt pose and the floor fit (`fitTiltedBodyToFloor`, `lyingFloorInset`), which still covers the flight. `EnemyAnimationPresenter.PlayDowned` now returns bool and gained `DownedFallSeconds`.
- **Fourteenth playtest: step clips, run flash after get-up.** Backward steps (back-off, flinch, shuffling away) played the forward walk because the measured locomotion only looked at speed. `EnemySpineAnimationController` now smooths the signed lane speed, compares it with `EnemyMovement.FacingDirection` (with a `directionSwitchSpeed` dead band), and in Hold / BackOff plays `walkForward` or `walkBackward` (both empty by default: fall back to the old clip; set the names once the clips exist). Dedicated step clips play at 1x at `stepWalkReferenceSpeed` (2.5 u/s, up to 1.5x); measured locomotion ignores knockback slides. After the get-up, `GruntBrain.EvaluateNextAction` (called from inside `LaunchedState`) now puts the grunt in `IdleState` for `getUpRecoverTime` (0.3 s) facing the player before deciding; previously it entered Chase at once and the next hit cut a run that had just started.
- **Fifteenth playtest: room for air attacks.** Higher and floatier: Sword step 3 (0.5, 20), Fist step 3 (0.4, 19.5), launch gravity 4 (~5.1 u peak in ~0.5 s, ~1 s in the air; the player's double jump reaches ~5 u). `juggleLift` 8 (~0.8 u, ~0.4 s more air per pre-landing hit). The bounce keeps its own gravity (8) and stays tiny.
- **Sixteenth playtest: launch cut short.** Every applied damage event while airborne went to `OnHitWhileLaunched`, including damage-over-time ticks (burn, shock from passive mods) and reaction-less hits, and a juggle *set* the vertical speed to `juggleLift` (and over the juggle cap set it to at most 0). A tick during the climb replaced the launch speed (20) with 8 and three of them stopped the climb; lowering `juggleLift` across earlier playtests made the launch look lower each time. Now `EnemyCharacter.ReceiveDamage` routes only non-tick hits with a reaction to the launch, a juggle lifts to `max(current rise, juggleLift)`, and over the cap the arc is left untouched (sideways shove only). Debug Log reports each in-air hit and its source. Launch raised: Sword step 3 (0.5, 22), Fist step 3 (0.4, 21.5), ~6.2 u at gravity 4.
- **Seventeenth playtest: height in units.** `EnemyLaunchSettings.apexHeight` (default 8 u above take-off) now sets the launch's upward speed as `sqrt(2 * g * gravityScale * apexHeight)` for launches and launched corpses; 0 falls back to the hit's upward knockback. The weapons' step-3 knockback y is therefore ignored while Apex Height is set (their x still sets the sideways drift). `debugLog` is on by default for this tuning pass (one line per phase); turn it off before shipping.
- **Eighteenth playtest: air kick spike, attack cancels.** New `HitReactionKind.Spike`: on a launched enemy in the air, `LaunchedState` sets the velocity to the hit's sideways speed (unscaled) plus a downward speed of `|knockback.y|` (never slower than it is already falling) and ends the juggle; on a grounded receiver `EnemyCharacter.ReceiveDamage` zeroes the y and treats it as a normal flinch. Sword `Attack_Air` (the air kick) now deals damage (multiplier 0 to 1: a zero-damage hit is never "applied" and so never reacts), knockback (4, 14), Spike. The fists have no air chain (`airSideCombo` is empty), so they have no air kick yet. Spike and Launch both use `launchHitstopDuration`. Attack cancels: `WeaponManager.TryCancelAttackForMovement` / `IsAttackCancelable`: an attack is committed until it connects (`PlayHitFeedback`) or until lead + `delayBeforeAttack` + `attackOpenWindow` passes, then a jump or dash cancels it (through `OnAttackInterrupted`, which resets the ground combo and clears a queued attack). `PlayerCharacter` buffers a jump or dash pressed earlier for 0.25 s and fires it when the swing becomes cancellable (waiting out hit-stop; a tap released before it fires is a short hop). Before this the attack's input lock dropped every jump press until the swing ended.
- **Nineteenth playtest: air attack never connected.** Sword `Attack_Air` had `hitRadius` 0 and `Player_2.2` `WeaponManager.sideRadius` is 0, so the hit check was a zero-size sphere; its `hitDelay` 0.2 opened the window at ~0.25 s while the 0.417 s clip at `weaponAttackTimeScale` 1.5 completed (and cancelled the execution) at ~0.33 s. Now hitRadius 1.2 and hitDelay 0.1, like the ground steps. Fists still have no air chain. Note the general rule this exposed: `hitDelay` is in gameplay seconds and is not scaled by the clip's time scale, and `OnAttackAnimationComplete` cancels a hit window that is still open.
- **Twentieth playtest: sword air attack reach.** Two more blockers besides the zero radius: the melee hit origin was computed once when the window opened (the air attack lunges at 8 u/s and the player falls, so the check was left behind; `RunMelee` now re-resolves it every iteration, which also helps lunging ground steps), and the 8 u apex sat at the edge of the player's reach (~8.3 u for the sword at the top of a double jump). `EnemyLaunchSettings` gained an apex hang (`apexHangSpeed` 4, `apexHangGravityScale` 1.5: ~0.55 s floating near the top; `EnemyMovement.SetLaunchHang`, off after a bounce or spike, back on after a juggle) and `apexHeight` defaults to 6.5 u; `LaunchUpSpeed` accounts for the hang so the apex stays exact (`LaunchReactionTests.LaunchReachesApexHeightIncludingTheHang`).
- **Stronger air kick.** Sword `Attack_Air` knockback (18, 22) (was 9 in the first pass; still not visible to the user), `spikeSkidKeep` 1.5 (range up to 2): ~4 u sideways in the air plus ~2.4 u of skid. After a spike the body falls under `spikeGravityScale` 7 (~0.22 s from a 6.5 u apex, ~2 u sideways), does not bounce, and on landing skids on with `spikeSkidKeep` (1) of its sideways speed through `EnemyMovement.ApplyKnockback` while the death-fall pose plays (~0.8 u more). `EnemyCharacter.HandleKnockbackEnd` now ignores a knockback that ends inside `LaunchedState`, otherwise the end of the skid ran the stun-complete path and pulled the grunt out of its downed pose.
- **Player jump.** `Player_2.2` `jumpForce` and `doubleJumpForce` 12 to 14 (about 36% higher per jump: roughly 5 u single, 10 u with the double jump, rough figures from the force and gravity multiplier, not measured), so the 6.5 u launch apex is in comfortable reach for air attacks.
- **Combo assessment.** `Player_2.2` had `comboWindow` 0.2 s and `bufferDuration` 0.2 s; the window only opens when the attack clip completes, so a press more than 0.2 s after a swing reset the chain. Structural issues: combo progress is driven by Spine `Complete` / `Interrupt` events and two flags (`WeaponManager.isAttacking`, `SpineAnimationController.attackActive`) kept in sync only by those events (`PlayAttackAnimation` calls `ForceFinishAttack` when they disagree, which completes the NEW attack); the ground/air chain is picked from one grounded sample at attack start (fists have an empty air chain, so an airborne press is dropped); an up input above 0.5 turns a side press into an Up attack, which resets the side chain; any player stun or dash resets the chain.
- **Verify in Play Mode (first pass done by the user: patrol and the launch flight work; the landing, get-up, keep-down, tuning, combo and later playtest changes above not yet seen):** player walks through idle and fighting grunts with no push; enemy hitboxes and perception still fire (sensor layer); grunts behind a blocked ally show Idle and brake; patrol with a scene-set distance pauses and looks back; approach from behind is unnoticed until 2 u, then the alert beat and the pack ripple; losing the player ends in a look-around; the sword finisher launches, bounces once, lies down, gets up, backs off; air hits juggle three times; a finisher kill sends the corpse flying; Brute only shoves. Also run `Validate Enemies`, `LaunchReactionTests` (new, 5 contract checks), `GruntTacticsTests`, `EnemyArchitectureTests`, `DamagePipelineTests`. Tune in the Inspector: tilt direction and `tiltPivotHeight` on the grunt skeleton, `EnemyLaunchSettings`, `rearRadius`, `patrolDistance` per placed grunt. Animator asks (none blocking): Walk, Alert, Knockdown / GetUp clips.

### Grunt group tactics: hold, vault, pressure (2026-10-23)

Design and phase plan: `GRUNT_BEHAVIOR_PLAN.md`. Phases 1–3 are implemented (code compiles in both assemblies via `dotnet build`); the prefab swap and Play Mode verification are pending.

- **`GruntBrain : MeleeChaserBrain`** (`Scripts/New Enemies/Grunt Enemy/GruntBrain.cs`) owns grunt policy the way `HyenaBrain` owns dodge/counter-dash. The base loop, Hyena, Brute, and `WaitForOpeningState` (still used by Robot) are unchanged. `GruntEnemy` now requires `GruntBrain`. `MeleeChaserBrain.IsAtAssignedDestination` became `protected`; nothing else in the base changed.
- **Prefab swap is an Editor step:** `Tools > JunkLite > Enemies > Migrate Grunt Prefab To GruntBrain` (`Editor/GruntBrainMigration.cs`) changes the component's `m_Script` reference in place, so the file id, every serialized value, the hitbox reference, and scene-instance overrides (Service Floor `pursuitRadius` 30) survive. `Migrate Selected To GruntBrain` does the same for selected scene objects with Undo. `EnemyArchitectureTests.GruntPrefabUsesGruntBrainOverTheReusableMeleeChaserLoop` and `Validate Enemies` fail until the migration has run.
- **States** (`Scripts/New Enemies/States/`): `HoldPositionState` (follows the director slot, shuffles within `paceAmplitude`, pauses, feints; replaces the frozen wait for grunts), `LeapState` (telegraph → kinematic arc → land; `CanBeInterrupted` only during the telegraph, a hit there cancels through `ILeaper.OnLeapCancelled`; no i-frames by default), `BackOffState` (backpedal to a brain-chosen distance facing the player, hold, report). New capabilities in `EnemyCapabilities.cs`: `IHoldPositioner`, `ILeaper`, `IBackOff`, all implemented by `GruntBrain` directly.
- **Director additions** (`EnemyEngagementDirector`, all additive): `TryGetLaneSnapshot` (side, queue index, counts on both sides, group pressure, quiet seconds, active attacker), `TryGetLaneNeighbors` (real bodies directly ahead/behind with comfort and hard spacing and their velocities; leaping enemies excluded; O(n), no sort), `NotifyAllyHit` (calls `IAllyHitListener.OnAllyHit(ally, placesBehind)` on every lane-mate behind a hit enemy), `TryGetBackSlot`, `GetBackOffLimit` (clamps a retreat against the next body in line), `SetYielding` (sorts a leaving enemy to the back of its side so the next one is promoted mid-leap), `SetQueueGap` (per-entry spacing; grunts ask for 0.5 so shuffling neighbours never touch; default stays 0.2 for Hyena), `TryReserveLeap` (one airborne grunt per group per `groupLeapCooldown`), `ReportHitTaken` / group `HitPressureWindow`, `SetAttackHandoffDelay`. The lease now also alternates **sides** when a ready attacker waits on the other side. `CanRequestAttack` accepts `HoldPositionState`.
- **Policy:** queue index 1 with an empty far side (`vaultMaxFarSideCount`, default 0 = strictly the second grunt), within `vaultMaxStartDistance`, player grounded and not rolling, clear path and ground and no enemy body at the landing point, per-grunt and group cooldowns, `vaultChance` → vault over the line to the far front slot plus `vaultOvershoot`. Front grunt at ≥ `backOffPressure` (2 hits in 2.5 s) → back off; at full pressure with an ally queued → backward leap with yielding, `fallBackCooldown`. Group pressure ≥ `groupWaryPressure` → shuffle away, hold further, no feints; no hits on the group for `boldAfterQuietSeconds` → shuffle in, hold closer, handoff delay 0.1 s.
- **Presentation:** `EnemyAnimationPresenter` gained virtual `SetLocomotionSpeed01`, `PlayLeapTelegraph`, `PlayLeapAir`, `PlayLeapLand` (no-ops in Mecanim, which already drives speed from `EnemyMovement`). The Spine presenter maps Hold/BackOff to idle and lets the states scale the walk clip (`minLocomotionTimeScale`), and plays `Jump_Start` / `Float` / `Jump_End` for leaps (new serialized names). The grunt skeleton has no Roll, Walk, or Backstep clip; swapping clip names needs no code.
- **Pressure** only counts `DamageResult.WasApplied` hits that are not tick damage (`HitPressureWindow`, ring buffer, no allocations). Per-instance variance (`chaseSpeedVariance`, `rhythmVariance`) and decision jitter live on the brain; `ChaseBehavior.SetSpeedMultiplier` is runtime-only and never touches the authored speed.
- **Playtest fixes (same day, after Sandbox feedback):**
  - *Grunts shoved each other forward.* Queued grunts walked to slots computed from the player, at randomized speeds, into the real body ahead; equal-mass rigidbodies then pushed it. Now every voluntary move in `ChaseState`, `HoldPositionState` and `BackOffState` passes through `ILaneSpacing` (implemented by `GruntBrain`, absent on other enemies so their chase is unchanged). It clamps the destination between "comfort gap behind the leader" and "contact gap in front of the follower" and caps speed at the neighbour's velocity plus `spacingGain` × remaining slack, so followers brake and match speed. The pure math is `LaneSpacingSolver` (EditMode-tested). Enemy–enemy collision stays on as a safety net; `HoldPositionState` also steps out of any overlap (knockback). `EnemyMovement.Velocity` was added for this.
  - *Vault aimed at where the player used to be.* `GruntBrain` now has **Pursue** (player moving along the lane faster than `plantedSpeed`) and **Engage** (planted, attacking or parrying), with `engageDelay` / `pursueDelay` hysteresis. Vaults, shuffling and feints only happen in Engage; in Pursue grunts chase in single file. During the crouch `LeapState` asks `ILeaper.ShouldAbortLeap` (player left Engage, moved `vaultAbortDistance`, rolled or jumped) and re-aims via `RefreshLeapDestination`; airborne, the landing slides toward the re-aimed point at `LeapMaxCorrectionSpeed`. Landing is computed from the player's current position (no slot query), at take-off ground height.
  - *Grunts behind did not react to hits.* A real hit calls `NotifyAllyHit`; each grunt behind schedules a flinch `placesBehind × rippleDelay` later (wave down the line), steps back `recoilDistance`, holds until `RecoilHoldUntil` (extended by every further hit in the combo, no re-flinch), then creeps back at `creepSpeed`. This is a phase of `HoldPositionState`, driven through `IHoldPositioner.TryConsumeRecoil`; a grunt still walking up switches to Hold to flinch. No per-frame cost when nobody is hit.
- **Not done / verify in Play Mode:** vault landing when the player side-swaps mid-arc (physics resolves the overlap), Service Floor doors (blockers are on `Default`, which the default `obstacleLayers` mask includes), the feel of `Run` at low time scale for shuffling, and whether 25 Rooftop grunts keep the director sort cheap at the 0.15 s decision cadence. Phase 4 items (ally-down flinch, parry respect, combo escape, commit check, barks) are not implemented.

### Playtest fixes: kill Y, death animation, renames (2026-10-20)

- **Kill Y:** Service Floor had no `DeathTrigger`. `[Gameplay]/Kill Y` is a 700×20×800 trigger slab (top at y = -30; basement floor is -10.5) that kills anything that clips through the floor. Other levels: add the same slab if they lack one.
- **Death animation:** once `PlayerState.IsAlive` is false, `SpineAnimationController` refuses every non-death play path (attack, forced override, locomotion, jump, landing, wall, double jump), so queued combo hits or late Spine events can no longer replace the death pose. `PlayerLifecycle.CompleteDeathPresentation` waits the death length in scaled time (matching the animation under hit-stop/slow-mo), capped at length + 5 s unscaled.
- **Service Floor basement:** `Wall_Basement_West/East` now reach the upper lane floor (y = 0.5) and are 1.5 thick; the old 1.14-unit gap above them led out of the level. All `Colliders` boxes use `Non Slip`.
- **Names:** player-facing text says "Server Room Key" / "Server Room door" and "KRF Porta Box". The Unity tag `Z Key` (`DoorAccessRule.requiredKeyTag`, `KeyPickup`) and scene object names are unchanged.

### Title menu and playtest build order (2026-10-14)

- Build order: `Menu` (0) → `SimulationCinematicDemo` → `Service Floor` → `EndCard` → `Rooftop Scene v2`. Index 0 is the title, so `GameManager.RestartGame` (pause menu "Return to title") lands on the menu. Index-based fallbacks (`ServiceElevator`, `Level0SequenceManager`) still reach EndCard from Service Floor; Sim → Service Floor and Rooftop/Service Floor → EndCard are set by scene name.
- `Menu.unity` → `Canvas/Main Menu` is an authored layout (CMA "Main menu" board: orange title band, Play + level select, Cassidy art from `New UI/Character/Cassidy_Full.png`). `MainMenuController` only binds the four buttons to scene names, selects Play for gamepad, and uses `GameManager.LoadLevel` when the persistent manager exists (returning from a run), else `SceneManager.LoadScene`. `Scene Settings` keeps `spawnPlayer` off. The old intro (VideoPlayer with a missing clip, `FadeoutSwirtch` any-key loader, `BLACK` fade panel) is disabled, not deleted.
- Terrain Holes is off on every URP asset (only `HIGH ASSET RENDER FEATURES` is used; quality levels have no override). No Terrain objects in build scenes.

### Grapple hook item, gamepad grapple, no interact in combat (2026-10-13)

- The grapple (and its wall jump) needs the `Grapple Hook` keyring item (`PlayerKeyring.GrappleHook`), not a drawn sword or Mod Combat. `PlayerSwordGrapple.HasGrappleHook` replaces `HasUsableSword`; the sword is only used for the thrown visual when one is drawn.
- Gamepad: hold LT (`GrappleAim` binding) to aim, right stick picks the direction (neutral = facing), X (`Attack`) throws, A jumps/kicks off the wall.
- Service Floor: the Brute's `ObjectiveOnDeath.grantKey` gives the hook; its `ObjectiveLink` plays Crow `ServiceFloor_UseGrapple`. `[Gameplay]/Grapple CyberBox` (5A, `requiredKeyTag` = Grapple Hook) holds a heal pickup.
- `PlayerCharacter.IsInCombat` (from `PlayerCombatTracker`) sets `GameInputManager.InteractBlocker`: Interact is ignored and the shared interact prompt hides while in combat.

## Objectives, door locks and keys (2026-10-13)

- **Objectives** are scene-local. `ObjectiveDefinition` (ScriptableObject) is read-only config; per-run completion lives on `ObjectiveTracker` (`[Gameplay]`, static `Instance`, duplicates remove only their own component). `Complete` is idempotent and raises `Changed` once. `ObjectiveLink` is the per-object "OBJECTIVE BASED" switch; producers (`HealPickUp` interact mode, `KeyPickup`, `ServiceElevator`, `ObjectiveOnDeath`) call `TryComplete` and stay silent once complete.
- **Door locks have owners.** `SlidingDoor.AddLock(owner)` / `RemoveLock(owner)`; `Locked` is true while any owner holds a lock. The serialized `locked` flag means "starts locked". `LaneEntryTrigger` and `DoorAccessRule` own separate locks on the same door. `DoorAccessRule` raises `SatisfiedChanged`; a `LaneEntryTrigger` gating that door suppresses its prompt and Interact until the rule is met.
- **Keys** are Unity tags held by `PlayerKeyring` on the player prefab (`Player_2.2`); kept across respawns within the run.
- **Free-move zones:** `ZMovementZone` toggles `Character2D5Controller.SetFreeRoam`. `LaneEntryTrigger.laneCamera` activates a vcam on snap (lane on a different axis). `FallRecoveryZone` returns the player to a lane point and deals damage through `DamageReceiverUtility`.
- **Missions tab:** `MissionsPageView` / `MissionRowView` bind to the tracker; `InventoryUI` uses an authored page when `missionsScreen` has a `MissionsPageView`, else the legacy placeholder. The authored prefab is still to be built.
- `SlidingDoor` opens for the player and for living enemies' non-trigger colliders (tracked per collider, pruned when dead/disabled); locks still win.
- `ObjectiveLink.completedDialogue` plays once on completion; if a dialogue is running it waits on a coroutine hosted by `DialogueManager` (the pickup is usually hidden right away).
- `ServiceElevator` ends a tracked level through `LevelGoal.Trigger()` (results screen → Continue), and loads directly only when no running `LevelStatsTracker`/`LevelGoal` exists. `LevelGoal.countSceneEnemiesForStats` gives non-KillAll levels an enemy total for grading. `EncounterStartTrigger` starts one referenced encounter on player entry. `LevelLaneTools.SnapToLane` snaps to the closest of all scene lane paths.
- `EncounterArchitectureTests`: 9 of 19 fail on 2026-10-13 (camera lock policy, edit-mode `Destroy`, existing-participant completion). These are in code this change did not touch; not yet checked against the previous commit.
- Tests: `ObjectiveSystemTests` (7), plus Damage (7), Lifecycle (4), Progression (28) pass. Play Mode check on Service Floor: health/Door_Entry, Z key, Room 10/12 doors, Z door → wing camera, 2A pit recovery, Brute objective → shaft door, elevator → EndCard.

## Editor spawning and first-contact stutters (2026-10-11)

- Reproduced through the actual F10 buttons and D input in Rooftop Play Mode with Unity's main-thread profiler. The baseline recorded an 81 ms frame around pickup entry (`ProximityRelay.OnTriggerEnter` 72 ms, including `DOTweenComponent.Awake` 67 ms) and a 101 ms frame around enemy detection (`EnemyPerception.OnTriggerEnter` 96 ms, including `SoundManager.LoadFMODSound` 91 ms). The audio source was **combat music**, started by `PlayerCombatTracker` -> `GameManager.OnCombatStarted`, not the attack-warning effect. An isolated experiment loading `nowhere_to_run_battle_theme_v1_KLICKAUD` took 103 ms and removed that detection stall.
- `GameRoot` initializes DOTween before scene load, retaining project settings. `GameManager.PlayLevelMusic` prepares the selected combat/fallback track as level music starts, instead of first decompressing the song from a perception callback. This moves work into startup and retains the existing music crossfade/resume behavior; audio import settings and scene assets are unchanged. A new music profile introduced later still needs preparation before its first combat transition.
- `PauseMenuDeveloperTools` resolves its bounded enemy/weapon/pickup prefab references when the console is built; spawn clicks reuse them. `EditorRuntimeDevToolsPanel` discovers world weapons at 4 Hz and immediately on commands/toggles, while continuing equipped-weapon/mod overrides each frame. No pooling or enemy reset lifecycle was introduced.
- `PerformanceOverlay` records F10 command duration/age, GC collection, audio loading, asset reads, corrected physics/animation/URP markers, and measured log-write time. Unsupported counters are -1. It labels delayed FrameTimingManager CPU/GPU values as context and excludes them from current-hitch attribution; allocation volume and draw-call count alone no longer assert a GC/GPU bottleneck. It no longer blindly skips frames after disk flushes, and consecutive 100 ms+ stalls bypass cooldown. Marker durations can overlap; these are candidates for timeline inspection, not an exhaustive profiler. See [Unity's timing-delay documentation](https://docs.unity3d.com/6000.3/Documentation/Manual/frame-timing-manager.html).
- Final production-code replay: combat track and tween runner were ready before movement; spawns took 0.9–4.1 ms, with training dummy 2.1 ms (baseline 29.7 ms). Editor asset caching also contributes to repeated-run spawn timings, so these are observations, not cold-machine guarantees. Neither original pickup nor music stall reappeared. A separate Timeline graph initialization remains (~19 ms inside a ~32 ms frame). The 0.5–2 second historical CSV outliers were not reproduced or conclusively attributed; Editor work and missing/misaligned old counters prevent treating all those rows as gameplay/GC stalls.
- Captures: `Logs/spawn-probe-before.txt`, `Logs/spawn-probe-after2.txt` (audio still unprepared), `Logs/spawn-probe-music-diagnosis.txt` (isolated music experiment), and `Logs/spawn-probe-final.txt` (production fix, no test preloading). The first `after` experiment over-sampled raw profiler names and caused measurement overhead; do not use its overall frame statistics. Temporary profiling helpers are removed after validation. No full build, scene save, asset-import change, or Inspector wiring is required.
- Regression checks: Unity compilation and all eleven damage/lifecycle EditMode checks pass (`Logs/performance-required-tests.json`). Live F10 checks verified immediate override application and cleanup across five world/equipped weapons; spawned weapons remain covered by the throttled discovery path.
- Logger validation: one explicitly labeled synthetic 80 ms wait inside a spawned enemy's perception callback produced an 89.4 ms frame. The CSV identified `Physics / trigger callbacks candidate`, reported 85.265 ms for physics (matching the raw Unity timeline), and associated the test command with the exact gameplay frame, age zero. This synthetic capture is `Logs/spawn-probe-validation.txt`; it is a diagnostic check, not a naturally occurring stall.

## Damage popup presentation (2026-10-11)

- `EnemyCharacter` forwards successful applied damage, `DamageRequest.Type` and `IsTickDamage` to the existing popup pool. Colors now communicate damage type: physical pale white, fire orange, electric blue, magic violet. Heavy hits retain their element color and grow; tick numbers are smaller and upright. Damage/status rules and health mutation are unchanged.
- `DamagePopup` uses the approved Play font/plain overlay, bold italic direct-hit digits, a subtle vertex-color highlight, a 0.16-second pop/settle, camera-relative rise and lateral separation, an opaque hold through 55% of its lifetime, and a smooth fade. Digit height follows the rendering camera's frustum with bounded world size; active labels continue facing the camera during turns. Fractional applied hits cannot round down to a misleading zero.
- `DamagePopupManager` exposes Damage Type Colors and Damage Typography & Motion settings. Existing serialized lifetime, speed, offsets and heavy-hit thresholds are retained. Runtime styling needs no asset or scene migration. XP labels reset gradient, font style, spacing and scale when reusing the same pool; each label returns to its bound pool owner. Duplicate managers remove only their own component.
- Validation: Unity runtime compilation succeeds; all eleven existing damage/lifecycle EditMode checks passed. Focused checks using the actual popup prefab passed for approved font/material, type colors, heavy-hit color, zoom sizing, facing, fractional ticks and damage/XP/pool reuse. Unity-rendered comparison visually reviewed: `Logs/damage-popup-preview.png`. No full build or scene save. The current Editor bridge forces code execution into Edit Mode, so real enemy hit propagation and animation timing still need an in-game visual check.

## Pulse Barrier arcs (2026-10-11)

- The current `PulseBarrierMod` asset uses `PulseBarrierVisualProfile` instead of its old sphere and explosion prefabs. `PulseBarrierMod` owns activation and cleanup through the existing execution context; shield HP, absorption, duration and cooldown rules remain on the existing systems.
- `PulseBarrierVisual` is a reusable player component. It fits six outer arc segments and two tilted segmented orbits to the player's collider, centers them on the current `BODY SPINE` renderer bounds (not the offset collision capsule), follows body rotation through camera turns, pulses on absorbed hits, and flickers at low shield health. Without a valid body renderer, its fallback center follows the player's local space. Early depletion scatters arcs and sparks; timed expiry contracts them. Cancellation, removal, mode exit, death and disable hide immediately. The brief natural end animation is cosmetic and does not extend the active timer or delay cooldown.
- The amplified presentation uses 0.18-wide ribbons with a broad luminous core, longer arc coverage, brighter cyan, stronger/longer hit pulses and larger sparks. Projected ellipse normals keep the orbit bands consistently thick. Sparks start at the outer ribbon edge, follow activation/hit/break expansion, and travel outward; mesh bounds include their full visible travel. This changes shader coverage and tuning, without adding meshes or render passes.
- One mesh/material draw contains 960 ribbon vertices plus 112 spark vertices. GPU animation uses per-renderer properties; there are no particle systems, extra cameras, screen copies, lights or per-frame geometry uploads. The same mesh and renderer are reused across activations. Materials are referenced by the profile on the mod asset for build inclusion.
- `Tools > JunkLite > VFX > Install Pulse Barrier Arcs` creates missing assets under `Assets/Game/VFX/Pulse Barrier/` and wires the existing mod, preserving authored profile settings. It saves only its material, profile and mod asset; no scene or player prefab wiring is needed.
- Validation: six focused `PulseBarrierVisualTests` and all eleven damage/lifecycle checks passed in the main Unity Editor. Rooftop Play Mode verified real mod activation, absorbed-hit health feedback, cancellation/active-timer cleanup/cooldown, mesh reuse and overflow break presentation. The final cyan ribbons were rendered on Cassidy through the project's URP renderer (`Logs/pulse-arcs-preview.png`); width was tuned for readability. No full build or target-hardware frame-time measurement was performed, and no scene was saved.

## Enemy burn presentation (2026-10-11)

- `EnemyCharacter.Awake` installs/binds `StatusEffectVisuals` on the enemy, using its authored `enemyVisual`. Existing enemy prefabs and scenes need no migration. An explicitly added presenter can override its profile, body renderers, flame anchor and scale.
- Gameplay remains on `StatusEffectHandler`. The presenter observes apply/refresh/remove and successful tick events; it queries `HasEffect(Burn)` on removal so independent burns keep their visuals until the last one ends. No damage timing, stacking or health mutation moved into presentation.
- Binding/enabling the presenter permanently retires the handler's old `burnVFX` for that runtime enemy: it stops and clears its particle hierarchy before the first burn, and prevents later activation. Disabling/removing the presenter does not restore that effect. Missing configuration logs a warning. Poison, electric and bleed visuals remain unchanged.
- A second overlapping effect was traced live in Rooftop to `StunnedState` enabling Grunt's `Debuff (1)` smoke/circles/sparks on hit-stun. The Grunt prefab's `MeleeChaserBrain.stun.stunVFXObject` reference is now empty; the old child remains inactive. Stun mechanics and other archetypes are unchanged. Verified all 14 Rooftop Grunts have no stun-particle reference; in the actual Editor's Play Mode, all 11 active Grunts retained burn presentation and entered StunnedState with zero active/playing particle systems, including after simulation. No scene was saved.
- `BurnRendererOverlay` adds a heat pass over the source SpriteRenderer or Spine MeshRenderer, shares the current animated mesh/atlas texture and never replaces source materials or modifies source property blocks. It follows sprite flips, sorting, visibility, Spine mesh-buffer swaps and submesh/material changes. The shader uses local body coordinates for noise, avoiding separate patterns on Spine atlas regions.
- Automatic burn renderer discovery prefers Spine body meshes and falls back to sprite bodies. `EnemyCharacter` explicitly excludes its attack-warning hierarchy. Emitter selection compares scaled body size, so large texture-space warning icons cannot acquire heat overlays or own/hide the embers. The heat and ember palette has been restored to the earlier orange values after the increased HDR brightness appeared yellow in gameplay; larger ember specks remain for gameplay-camera visibility.
- Follow-up validation in the main Editor: nine focused burn checks (including warning exclusion, scaled emitter choice, zero steady-state allocation and shader imports) and all eleven damage/lifecycle checks passed. Rooftop Play Mode checks confirmed Grunt and Hyena embers remain attached to their active Spine bodies with warnings hidden, and warning hierarchies contain no burn renderers when shown. Inventory player-portrait pointer enter/exit and controller selection also passed. The final live preview capture failed; the latest brighter appearance has not been visually reviewed at the gameplay camera.
- `BurnFlameVisual` reuses one 100-vertex mesh (five optional flame cards, twenty ember cards) per ignited enemy. Embers start throughout the fitted body bounds, drift outward/upward with varied lifetimes, shrink and fade. Motion uses shader time in body space. The default is bright orange body heat plus amber embers; larger flame tongues are disabled. There are no particles to simulate, lights, distortion passes or scene-wide searches. Assets/renderers are created on first ignition and reused until the presenter is destroyed; removal/death/disable hides them immediately. Active presentation uses cached lists and property blocks. This costs an extra heat draw per body submesh plus one shared flame/ember draw. Property blocks belong only to the overlay, preserving the source renderer's existing batching/flash behavior.
- `Assets/Game/Resources/Burn Visual Profile.asset` owns read-only strength, HDR heat color, tick pulse, flame settings and ember color/opacity/size/visibility. Flames and embers can be toggled separately. Its material references keep the shaders reachable in builds. `Tools > JunkLite > VFX > Install Burn Visual Assets` creates missing materials, profile and a 64x64 seamless noise texture via Unity, without editing scenes or enemy prefabs; rerunning retains authored settings.
- The initial scope is burn. Other statuses can add their own presentation to the same event boundary without changing their damage producers.
- Validation: first-party runtime and Editor code compile. All 25 focused cases pass (`StatusEffectVisualsTests`, `DamagePipelineTests`, `PlayerLifecycleConfigurationTests`) in an isolated Unity 6000.3.22f1 project, including refresh/reuse, independent burns, successful-tick-only pulses, legacy particle clearing before first application and suppression through reignition/presenter removal, independent flame/ember visibility, death/disable cleanup, sprite flips, Spine-style mesh/submesh swaps, wide-sprite flame fitting and zero managed allocations across 256 warmed-up presentation updates. The actual Grunt Spine data and dummy sprite were rendered with URP 17.3.0 and both new shaders; the revised orange heat/ember-only preview is `Logs/burn-preview.png`, results: `Logs/burn-tests.xml`. New asset/meta files were originally generated by Unity in that verification project and copied back (the profile's script assembly identifier was adjusted to the main project's `Assembly-CSharp`). No full build, authored scene save or target-level performance measurement was performed. In-level overlap, camera turns and encounter frame time still need Play Mode review.

Last reviewed: 2026-08-29

Reviewed branch: `4.7`

Baseline code commit: `46f3c34e` (`changes to player, enemies and base services`)

The current working tree contains the player/enemy separation, result-based damage pipeline, `WeaponManager` execution/motion extraction, focused mod-runtime cleanup, the explicit `V2.5` infrastructure migration, the focused player/UI lifecycle extractions, the player weapon-loadout extraction, the composed enemy-AI migration, encounter phases 1–4, and the audited unused-script cleanup described below.

## Purpose

This document is the source of truth for the architecture refactor. It records what is implemented, what still needs gameplay verification, and the next safe work order so development can continue from another computer or Codex task.

The goal is better modularity and easier feature development without building a framework larger than this game needs.

## Project Reality and Constraints

- JunkLite is a **single-player game**. Do not design combat, abilities, state, or managers around multiplayer authority or replication.
- The project uses Unity 6000 with URP.
- First-party gameplay code currently lives primarily under `Assets/Game/Scripts/`.
- Refactoring should focus on first-party scripts, prefabs, and ScriptableObject assets; targeted scene edits are allowed when requested by the user.
- Preserve scene layout, visuals, gameplay references, and unrelated overrides. Prefer scene-instance overrides over third-party asset modifications, and report any remaining Inspector work to the user.
- Unity should create `.meta` files where possible.

## Architectural Principles

1. Prefer composition for shared capabilities such as health, damage reception, teams, status effects, and targeting.
2. Keep player input/state and enemy AI/state separate. Their FSMs solve different problems and should remain separate.
3. Use inheritance only for genuine specialization. A small enemy-specific base is acceptable; a universal player/enemy character base is not useful here.
4. Use one authoritative damage entry point: producers submit `DamageRequest`, receivers return `DamageResult`, and producers react to the result.
5. Keep ScriptableObjects as immutable runtime configuration. Mutable cooldowns, charges, durability, VFX references, subscriptions, and execution flags belong to runtime instances/components.
6. Every long-running ability must have one owner and one cleanup path for completion, cancellation, player disable, mode exit, removal, and interruption.
7. Split managers only at concrete responsibility boundaries. Do not replace one large manager with many empty interfaces, service locators, or global event buses.
8. Optimize measured or obvious hotspots with caching, reusable buffers, and receiver deduplication. ECS and a universal ability graph are not currently justified.

## Executive Status

| Area | Status | Meaning |
|---|---|---|
| Player separation | Implemented, gameplay verification pending | `PlayerCharacter` no longer inherits the shared character base. |
| Enemy foundation | Implemented, gameplay verification pending | `EnemyCharacter` inherits the small enemy-only `EnemyBase`. |
| Damage request/result pipeline | Implemented | All first-party producers use `DamageRequest`/`DamageResult`; the legacy API is removed. |
| Weapon execution and damage migration | Implemented, gameplay verification pending | `WeaponManager` owns attack decisions/lifecycle while plain C# helpers own sequencing, attack motion, hit resolution, and damage delivery. |
| Ability/mod runtime architecture | Implemented, gameplay verification pending | Per-slot executions, cancellation cleanup, lifecycle separation, and composable player ability locks are in place. |
| Damage/lock EditMode tests | Passing | Unity 6000.3.22f1 passes all 7 focused tests. |
| Game root / level context | Migration implemented and validator passing; Play Mode verification pending | `V2.5` contains the reusable root and standalone level context without removing authored environment content. |
| Scene-local camera binding | Implemented, Play Mode verification pending | Core and trigger cameras use one cached registry and rebind to every spawned/respawned player. |
| Restart/loading transition | Timing fix implemented, Play Mode verification pending | Loading video and async scene loading now overlap; the obsolete serialized six-second activation delay is removed. |
| Player lifecycle extraction | Implemented, Play Mode verification pending | `PlayerLifecycle` owns player creation, identity, spawn selection, death, and soft respawn. Direct consumers no longer use player APIs on `GameManager`. |
| Game UI lifecycle extraction | Implemented, Play Mode verification pending | `GameUIManager` owns HUD, pause, game-over, loading UI, prefab configuration, instances, and state-driven visibility. |
| Player weapon loadout | Implemented, Play Mode verification pending | `PlayerWeaponLoadout` owns both equipped slots, paired world pickups, attachment, visibility, swap/drop/replace, and break removal. |
| Composed enemy AI foundation | Implemented for all current first-party enemy prefabs, latest migrations need Play Mode verification | Grunt/Hyena are user-verified. Robot, Flying Dummy, Patrol Dummy, and Dummy now use the same composition boundary without forcing every archetype through the same brain. |
| Grunt group tactics | Code implemented and compiling; prefab migration and Play Mode verification pending | `GruntBrain` adds hold/pace, vault to the open side, hit-pressure retreat, and group mood on top of the engagement director. See the dated section at the top. |
| Encounter/wave progression | Phases 1–4 implemented; scene authoring, EditMode, and Play Mode verification pending | Scene-local waves, exact-once participant tracking, required-encounter level goals, explicit event-driven camera locks, cancellation semantics, validation, and the Level 0 legacy bridge are in place. |
| Full game/level manager cleanup | In progress | Player and UI lifecycle are extracted; `GameManager` retains global state, pause coordination, scene transitions, and music. |

## Implemented Architecture

### 1. Damage contracts and authoritative health mutation

`Assets/Game/Scripts/Character/DamageContracts.cs` defines:

- `DamageRequest`: requested amount, source, type, knockback, tick flag, and explicit defense/mitigation bypasses.
- `DamageOutcome`: `Applied`, `Blocked`, `Parried`, `Invulnerable`, `FriendlyFire`, `Dead`, and `Invalid`.
- `DamageResult`: requested damage, actual applied damage, outcome, and `WasApplied`.
- `IDamageReceiver`: the only first-party damage-receiver contract.
- `DamageReceiverUtility`: hierarchy-aware receiver resolution with no legacy fallback.

`Damageable` validates requests, applies armor mitigation, mutates health through `AttributeManager.ApplyDamage`, returns clamped applied damage, and emits `OnDamageResolved` only for applied damage.

The old `IDamageable`, `DamageInfo`, boolean `TakeDamage`, conversion helpers, fallback resolution, and legacy damage event have been removed. A source search on 2026-08-26 found no remaining first-party references.

Damage flow:

```text
weapon / hazard / ability / status / enemy attack
                         |
                         v
          IDamageReceiver.ReceiveDamage(request)
                         |
                         v
                    Damageable
          validation + mitigation + health mutation
                         |
                         v
                    DamageResult
                         |
            actor/producer-owned reactions
```

`Damageable` no longer applies generic hit-stun. Player and enemy actor code own stun, knockback, animation, VFX, audio, and other presentation reactions.

### 2. Player and enemy separation

`PlayerCharacter` inherits directly from `MonoBehaviour` and implements `IDamageReceiver` and `IGrabbable`. It owns player-specific input orchestration, defenses, damage reactions, death/respawn, grabbing, and presentation.

Player defense order is:

1. Request/source/team/alive validation.
2. Parry.
3. Invulnerability/capability state.
4. Shield absorption.
5. Armor mitigation and health mutation.
6. Player feedback, knockback, and non-tick hit-stun after applied non-lethal damage.

`EnemyBase` owns only enemy stats/attributes, damage binding, health/death lifecycle, healing, activation, and forced death. Enemy targeting, movement, FSM decisions, interruption, knockback, presentation, drops, and encounters remain enemy-specific.

The unused universal `CharacterBase` was deleted after confirming it had no inheritors, code consumers, or serialized references. Player and enemy lifecycle now use only their focused boundaries.

### 3. WeaponManager result-based damage migration

`WeaponManager` uses `DamageRequest`/`DamageResult` for melee, directional blast, piercing/non-piercing hitscan, single-target, and multi-target damage.

- `OnEnemyHit` publishes `DamageResult.AppliedDamage`.
- Hit VFX, hit-stop, recoil, melee durability, and hit events require `WasApplied`.
- Ranged durability remains consumed when a shot is fired.
- Piercing and area attacks deduplicate by resolved receiver instance.
- Target resolution and result consequences use a small internal helper without redesigning weapon definitions or attack detection.

### 4. Mod definitions, instances, and execution ownership

The mod system keeps the existing useful structure:

- `ModData`, `ActiveModData`, and `PassiveModData` are ScriptableObject definitions/configuration.
- `ModInstance` owns per-slot durability, capped charges, cooldown, and execution state.
- `ModManager` owns installed slots, activation, durability consumption, lifecycle dispatch, and combat-mode availability.
- `ModExecutionRunner` is a player-owned runtime component added automatically by `ModManager`; no scene or prefab edit is required.
- `ModExecutionContext` owns one activation's cleanup callbacks and player-control scope.

The runtime flow is:

```text
player input
    -> ModManager selects ModInstance
    -> ActiveModData validates configuration/state
    -> ModExecutionRunner starts per-instance execution
    -> concrete ability performs its unique behavior
    -> DamageRequest / DamageResult handles damage
    -> runner restores all owned state on completion or cancellation
```

`PulseBarrierMod`, `DontBlinkMod`, and `SocialDistanceMod` no longer store execution flags, player references, active VFX, or shield state in shared ScriptableObject assets. `EnergyWaveMod` uses the same execution path and no longer starts cooldown twice. `PhantomStrikeTracker` remains a per-player runtime component and now delegates execution/cancellation to the runner.

### 5. Explicit mod lifecycle

As of 2026-10-10, mod slot timers follow **active effect -> cooldown -> ready**. `ModInstance` holds active time and queues the full configured cooldown until both execution and any independent effect have ended. `ModExecutionContext.ShowActiveDuration` owns the display lifetime of coroutine abilities and clears it on completion/cancellation. Pulse Barrier uses shield duration (ending early on break), Social Distance uses pulse duration, and blink/slam sequences report their current remaining execution time. `EnergyWavePulse` owns its lifetime separately from the short casting lock and releases it on expiry, disable, or destruction. Instant abilities start cooldown immediately; zero-cooldown abilities return directly to their existing readiness/charge checks. HUD and inventory share a dark full-square timer background with a lighter overlay that clears radially clockwise and cyan ACTIVE / magenta COOLDOWN accents. The dark background keeps the numbers readable through the final fraction of either phase and disappears when timing ends. Both layers use the shared `ModCooldownStyle` asset. Overlay size, opacity, tint, wipe direction, labels, and ready feedback are Inspector-adjustable. No scene wiring is required.

The ambiguous `OnEquip`/`OnUnequip` callbacks were replaced with:

- `OnInstalled`: a runtime instance entered a slot.
- `OnRemoved`: a runtime instance left a slot.
- `OnCombatModeEntered`: an installed mod became enabled.
- `OnCombatModeExited`: an installed mod became disabled.

Entering or leaving Mod Combat no longer pretends to install or remove an item. Leaving combat mode, disabling the player, or explicitly removing a mod cancels its active execution through `ModExecutionRunner` before lifecycle teardown.

If an activation consumes the final durability point, the broken mod leaves its slot but that final successfully-started ability is allowed to finish. The runner still owns and cleans up that execution.

### 6. Composable player ability locks

`PlayerState` now provides disposable, owner-independent input-lock and damage-immunity leases. Multiple abilities can hold locks concurrently; releasing one lease cannot unlock the player while another lease is still active.

`ModExecutionContext.LockPlayerControl` composes these leases with reference-counted movement, physics-override, and rigidbody-kinematic ownership. It captures the previous controller/rigidbody state and restores it when the final ability scope releases.

This removes direct `SetInputLocked(false)`, `SetVulnerable(true)`, and hard-coded physics restoration from active mods. Existing non-mod systems can continue using the original state setters until they are reviewed separately.

### 7. Remaining damage producers migrated

These producers now use `DamageRequest`/`DamageResult`:

- `StatusEffectHandler` damage-over-time ticks.
- `EnergyWavePulse` initial and repeated damage.
- `DontBlinkMod` strike damage.
- `SocialDistanceMod` pulse damage.
- `PhantomStrikeTracker` slam damage.

Important behavior corrections included in the migration:

- Status tick events publish actual applied damage and reuse an expiry buffer instead of allocating a new list every frame.
- Social Distance and Phantom Strike deduplicate multi-collider targets and only spawn hit feedback for applied results.
- Energy Wave captures an enemy only after applied damage and restores the rigidbody/NavMeshAgent states that enemy had before capture.
- Fire and Electric status effects retain the player as their damage source; chained Electric application deduplicates multi-collider enemies.
- Phantom Strike resets charges from `OnDamageResolved` instead of the removed legacy event.
- `PlayerCharacter` caches `DamageShield` after it is first found instead of resolving it on every shielded hit.

### 8. Explicit game-root and level-context foundation

The redesigned scene boundary is two roots with different lifetimes:

- `Game Root` is the duplicate-safe persistent root. It owns `GameManager`, `PlayerLifecycle`, `GameUIManager`, `GameInputManager`, `PlayerCombatTracker`, the runtime gameplay canvas, and the event system.
- `Level Context` is a standalone scene-local root. It owns the level identity, whether a player should spawn, and explicit typed player spawn points.

`LevelContext` must not be nested under the persistent prefab. `GameRoot` temporarily detaches contexts found in an older prefab so unmigrated scenes remain usable, while the rebuild command removes that legacy nesting from the prefab entirely.

The `V2.5` spawn failure was traced to a duplicate-singleton race. The scene had a standalone `GameInputManager` while the composed `Game Root` also contained one. The duplicate path called `Destroy(gameObject)`, so depending on `Awake` order it could destroy the complete `Game Root`, including `GameManager`, before player spawning. Duplicate `GameInputManager`, `GameManager`, and `PlayerCombatTracker` instances now disable and remove only their own duplicate component, never the shared host object.

`Assets/Game/Editor/GameRootTrainingLevelMigration.cs` is now manual-only. It no longer uses `[InitializeOnLoad]` or silently edits a scene. The command:

`Tools > JunkLite > Systems > Rebuild`

does the following in one reviewed operation:

1. Rebuilds `Assets/Game/Prefabs/Manager/Game Root.prefab` without scene-local `LevelContext` data.
2. Removes obsolete player/bootstrap/input/UI infrastructure from `Assets/Game/Scenes/V2.5.unity`.
3. Installs exactly one persistent `Game Root` at the world origin.
4. Creates one standalone `Level Context` and preserves the authored spawn transform.
5. Preserves level geometry, cameras, enemies, pickups, and required supporting services such as audio, combat effects, projectiles, drops, and feedback.
6. Saves and validates the resulting scene.

`PlayerLifecycle` is now serialized on the reusable `Game Root` and owns the player prefab, current-player reference, typed spawn selection, death observation, lifecycle events, and delayed soft respawn. `GameUIManager` is also serialized on the root and owns all runtime UI prefab references, creation, instances, canvas discovery, loading presentation, and state-driven visibility. `GameManager` coordinates scene initialization but retains only global game state, pause coordination, scene transitions, and music. Hidden player and UI fields remain solely as compatibility bridges for old scenes that have not yet been rebuilt with the reusable prefab.

### 9. Scene-local camera ownership and player binding

`CameraManager` remains scene-local because Cinemachine rigs, blends, and trigger cameras belong to a level. It is not part of the persistent `Game Root`.

The original V2.5 camera failure was caused by a split configuration: the scene assigned `mainCamera`, but `ConnectToPlayer` only targeted the optional `cameraList`, which was empty. The main camera was prioritized without ever receiving the spawned player as its `TrackingTarget`.

The corrected flow is:

1. `PlayerLifecycle` spawns or revives the player and publishes `PlayerSpawned` once.
2. The scene-local `CameraManager` subscribes directly to that event and owns all camera response.
3. Main, spawn, death, and explicitly configured level cameras enter one cached, deduplicated registry.
4. Every registered camera is rebound to the current player on spawn and respawn.
5. A camera selected later by `CameraSwitchTrigger` registers and binds on demand.
6. Respawn prioritizes the configured spawn camera, falling back to main and then the first registered camera.

This uses explicit serialized references and small cached collections rather than repeated scene-wide camera searches. Follow-freeze state is retained when switching cameras, singleton duplicates remove only the duplicate component, and the manager cleans up both player and `PlayerLifecycle` event subscriptions when disabled.

The V2.5 validation command now requires exactly one scene-local `CameraManager`, exactly one `CinemachineBrain`, and a main camera reference belonging to the scene camera rig.

### 10. Restart and loading-transition timing

The original scene-restart coroutine played the entire loading video before it even called `LoadSceneAsync`, then held the loaded scene behind a serialized `debugLoadDelay` of six seconds. Restart duration was therefore video duration plus scene-loading duration plus an artificial delay, producing a visibly frozen loading frame before the player returned.

`GameManager.LoadLevelWithScreen` now starts the video and asynchronous scene load together. Scene activation waits until the scene is ready and the video is finished, so the two real operations overlap instead of accumulating. The debug delay field and its stale values were removed from both manager prefabs. Input remains disabled until `InitializeForNewScene` has refreshed level references and spawned the player.

### 11. Focused game UI lifecycle

`GameUIManager` owns the runtime HUD, pause menu, game-over screen, and loading screen without introducing a service locator or event bus. It subscribes directly to `GameManager.OnGameStateChanged` and `PlayerLifecycle.PlayerSpawned`, activates the HUD only for a live player in a player-enabled level, presents game over from global state, and restores HUD binding after soft respawn.

The reusable `Game Root` and legacy `Game Manager` prefabs both serialize the four UI prefabs on `GameUIManager`. The V2.5 rebuild command adds and migrates this component when necessary, and validation rejects a root with missing or incompatible UI prefabs. `GameManager` delegates scene UI initialization, loading begin/cancel, loading-video completion, and inventory-first pause handling through the focused UI owner.

The load-failure path now preserves the current player until Unity confirms the scene request is valid and restores the pause subscription if loading cannot start.

### 12. Player weapon-loadout ownership

`PlayerWeaponLoadout` is a player-owned runtime component that contains the two equipped weapon slots and their paired `WorldWeaponPickup` objects. It owns equip/replace, drop, swap, weapon-holder attachment and socket transforms, weapon visibility, and event-driven removal when durability reaches zero. Replacing a weapon is published as one atomic `WeaponChanged` notification.

`WeaponManager` now consumes the loadout and retains combat-mode rules, attack input/buffering, combo timing, melee/ranged execution, hit detection, damage requests/results, recoil, hit-stop, and attack feedback. Fists remain on `WeaponManager` because they are the default attack rather than an equipped item. The manager keeps command methods for attack-sensitive swap/drop/pickup operations, but no longer exposes or stores weapon slots.

Inventory, weapon-pickup UI, both weapon HUD implementations, the combined mod-combat HUD, and the Level 0 tutorial subscribe to `PlayerWeaponLoadout.WeaponChanged` and query the loadout directly. The active `Player_2.2` prefab serializes the new component and holder reference. `WeaponManager` retains a hidden holder fallback only for older player prefabs and adds/configures the loadout at runtime when necessary.

### 13. Composed enemy-AI foundation

The first enemy architecture vertical slice now follows this rule:

> Sensors report facts. Brains make voluntary decisions. States execute actions. Character/combat systems may force interrupts.

The current ownership boundaries are:

| Part | Owns | Must not own |
|---|---|---|
| `EnemyCharacter` | Enemy identity, damage reactions, death, lifecycle, combat participation | Normal chase/attack/dodge decisions |
| `EnemyPerception` | Current single-player target, distance, collider tracking, LOS/reachability checks, target changes | FSM transitions or combat policy |
| `EnemyBrain` | Voluntary behavior selection and normal FSM transitions | Hitbox damage, physical movement implementation, animation playback |
| Enemy states | One action's enter/update/exit and completion | The next long-term behavior decision |
| `EnemyMovement` | Movement commands, facing, physics, knockback | Knowledge of concrete FSM state types |
| Serializable behaviors | Capability tuning and reusable mechanics such as melee/dash hitbox damage | Archetype-level decision policy |

Implemented details:

- `EnemyCharacter.Died` is exact-once and `Level 0 Sequence Manager` now tracks wave deaths through that lifecycle event instead of inspecting `DeadState`.
- On death, enemy body colliders immediately exclude the `Enemies` layer while retaining their existing world collision settings. Corpses can land and play their death animation without being pushed by other enemies; the existing delayed teardown still disables all colliders afterward.
- `EnemyPerception` replaces detection logic while `DetectionZone` remains a thin compatibility subclass, preserving existing prefab script references and serialized sensor fields.
- The sensor safely tracks multiple colliders for the single player, rejects dead targets, retains the existing optional LOS/reachability rules, and resets expanded pursuit radius when disabled.
- `EnemyMovement` no longer references `StateMachine` or `StunnedState`; states stop or command movement explicitly.
- `EnemyBrain` is the decision boundary. `MeleeChaserBrain` implements the reusable passive/patrol, chase, single-melee-action, and re-evaluation loop.
- `HyenaBrain` adds only Hyena policy: reactive dodge, optional counter-charge/dash, and whiff stun.
- `PatrolBehavior`, `ChaseBehavior`, `MeleeAttackBehavior`, `DodgeBehavior`, `ChargeBehavior`, `DashBehavior`, and `StunBehavior` provide capabilities to states through one composed provider. Melee and dash damage remain in the behavior that owns the hitbox rather than in the brain.
- `GruntEnemy`, `HyenaEnemy`, `RobotEnemy`, `FlyingDummy`, and `PatrolEnemy` are now thin identity/migration components. `DummyEnemy` additionally retains only its unique invincibility/health-reset damage options.
- `RobotBrain` owns the charge/dash/optional-grab/recovery policy. Its focused runtime capability owns Robot-specific dash-contact damage and grab selection while reusing patrol, charge, dash, grab, recovery, and universal action states.
- `FlyingFollowerBrain` owns only patrol/follow decisions. `FlyingHoverController` independently owns gravity, passive hover/height return, and death falling; it does not inspect the FSM.
- `PassiveEnemyBrain` supplies either permanent patrol or idle behavior for non-combat test enemies and intentionally ignores perception.
- Older scene-embedded enemies retain hidden legacy fields and receive a runtime brain/controller bridge, avoiding broad scene edits during migration.
- Grunt, all four Hyena variants, Robot Enemy, Flying Dummy, Patrol Dummy, and Dummy prefabs were migrated. No scene was edited.
- `Tools > JunkLite > Systems > Validate Enemies` validates all nine prefabs, including brain/controller type, perception where required, serialized ownership, attack hitboxes, passive settings, and reusable capabilities.

Deliberately deferred until this vertical slice is gameplay-proven:

- No generalized target/threat framework, service locator, AI event bus, or universal action graph was added.
- `EnemyType`, `EnemyConfig`, and a possible identity-only `EnemyDefinition` were not redesigned.
- Animation-presentation cleanup remains deferred. Encounter phases 1–4 are implemented; native scene authoring and gameplay verification remain.

### 14. Encounter and wave architecture — phases 1–4 implemented

The first implementation slice extracts one concrete responsibility from level-specific code:

> The sequence manager decides when a fight happens. The encounter decides which enemies belong to that fight and when the fight is finished.

This design is intentionally not a universal mission, quest, or sequencing framework. Before phases 1–2, the code demonstrated the need for the smaller boundary:

- `Level0SequenceManager` owned enemy configuration, instantiation, living counts, death subscriptions, tutorial reactions, and progression waits.
- `LevelGoal` separately finds enemies and subscribes to attribute death events, which duplicates ownership and does not naturally cover later runtime spawns.
- `CameraSwitchTrigger` can maintain a manual enemy list and poll health every frame while also reacting to `PlayerCombatTracker`.
- `PlayerCombatTracker` represents current aggression against the player. Losing aggro is not the same as defeating an encounter.
- `LevelStatsTracker` derives its expected total from the initially discovered enemies, while spawned enemies may appear later.

#### Final ownership decision

| Part | Owns | Must not own |
|---|---|---|
| `EncounterController` | Encounter state, sequential wave progression, participant registration, living-enemy tracking, completion, cancellation cleanup | Tutorial logic, dialogue, cameras, gates, music, loot, stats, enemy AI, or level victory |
| `EncounterWave` | Ordered scene-local participant entries and optional wave delay | Runtime tracking or reactions to completion |
| `EncounterEnemyEntry` | One explicit prefab spawn or one existing scene enemy | Both source modes at once |
| `Level0SequenceManager` | Tutorial order, parry prompt, Hyena reward, pickups, cinematics, and deciding when to start combat | Instantiating enemies, maintaining alive counts, or deciding wave completion |
| `LevelGoal` | Level-completion policy based on required encounters, zones, or manual objectives | Reconstructing encounter completion from enemy health/counts |
| `LevelStatsTracker` | Time and statistical totals | Encounter progression or level-completion policy |
| `PlayerCombatTracker` | Whether living enemies are currently engaging the player and the resulting combat presentation/music | Encounter or wave completion |
| Gates and camera triggers | Local presentation/gameplay response to an explicitly referenced encounter | Polling enemy health or inspecting enemy FSM states |
| `GameManager` / `LevelContext` | Their existing global-state and scene/player-configuration responsibilities | Individual encounter execution or wave state |

The encounter remains a normal scene-local component. It is not a singleton and does not publish through a global event bus.

#### Initial data model

Use scene-local serialized data on `EncounterController`; do not introduce `ScriptableObject` wave assets yet. Scene transforms make local configuration clearer, and no proven repeated wave template currently justifies another asset layer.

`EncounterState` begins with only externally meaningful states:

- `Idle`
- `Running`
- `Completed`
- `Cancelled`

Internal spawn and between-wave delays remain coroutine details rather than additional public states.

Each `EncounterWave` contains an ordered list of `EncounterEnemyEntry` objects and an optional non-negative delay before it begins. Waves run sequentially in v1; overlapping/reinforcement waves are deferred until a real encounter requires them.

Each `EncounterEnemyEntry` has an explicit source mode:

- `SpawnPrefab`: requires an `EnemyCharacter` prefab and a scene spawn transform.
- `ExistingEnemy`: requires one scene enemy reference and no prefab/spawn transform.

Use an enum plus validation rather than polymorphic serialization or an inheritance hierarchy. `OnValidate` and the encounter validator must reject or loudly report the following:

- Both prefab and existing-enemy fields assigned.
- The field required by the selected source mode missing.
- A prefab entry without a spawn transform.
- Duplicate existing enemies within or across waves.
- Negative delays.
- Null/invalid entries and encounters with no waves.

Existing scene enemies intended for a later wave should normally begin inactive. The controller registers and subscribes them before activation. Spawned prefabs are registered immediately after `Instantiate` and before control returns to gameplay; enemy prefabs must not kill themselves during `Awake` as part of the v1 lifecycle contract.

#### Runtime contract

`EncounterController` exposes:

- `State`
- `CurrentWaveIndex`
- `AliveEnemyCount`: the number of living enemies currently registered with the encounter, not a historical or configured total.
- `StartEncounter()`
- `CancelEncounter()`
- `UnregisterEnemy(EnemyCharacter enemy)` for an intentional scripted despawn that should stop blocking progression without being reported as a kill.
- A small coroutine convenience such as `WaitUntilFinished()` for sequence code. Events remain the authoritative notification mechanism for other consumers.

The public event surface in v1 is deliberately limited to events with known consumers:

- `EncounterStarted`
- `EnemyRegistered` — covers both newly spawned and existing scene participants.
- `EnemyDied`
- `EncounterCompleted`

Do not add public wave-started/completed, count-changed, preparing, paused, resumed, or cancellation events until a concrete UI/presentation consumer needs them. Callers already know when they invoke cancellation, and cancellation is not equivalent to successful completion.

Living participants are stored in a `HashSet<EnemyCharacter>`, never represented only by an integer. Registration subscribes to `EnemyCharacter.Died`. Death handling must first remove from the set; if removal fails, the duplicate callback is ignored. It then unsubscribes, publishes `EnemyDied`, and evaluates wave completion. `EncounterCompleted` is guarded and fires exactly once.

Runtime edge-case semantics are fixed as follows:

- `StartEncounter()` is valid only from `Idle`. Calling it while running, completed, or cancelled is a no-op with a development warning; it never silently restarts.
- Empty waves complete immediately and progression continues.
- Invalid entries log a clear warning, are skipped, and cannot deadlock the wave.
- An encounter containing no valid participants still publishes `EncounterStarted` followed by one `EncounterCompleted`.
- A partially invalid wave tracks its valid participants normally.
- A disabled enemy remains registered because temporary disabling is not death or removal.
- An enemy intentionally removed by script must use `UnregisterEnemy`; this does not publish `EnemyDied` or award a kill.
- The running progression loop defensively prunes actually destroyed Unity-null participants, logs a warning, and treats them as removed rather than killed so external `Destroy` cannot deadlock the encounter or inflate statistics.
- `CancelEncounter()` stops pending spawn/delay work, unsubscribes every participant, clears runtime tracking, enters `Cancelled`, and leaves participating enemies alive and independent. It does not kill, destroy, despawn, unlock gates, or publish `EncounterCompleted`.
- True reset/restart semantics are not part of v1. Respawning dead scene enemies, restoring health/AI, replaying callbacks, and resetting statistics require explicit product behavior and will only be added when encounter replay is needed.

#### Implementation phase 1 — encounter foundation (implemented, verification pending)

Focused first-party scripts now live under `Assets/Game/Scripts/Encounters/`:

1. `EncounterTypes.cs` containing the state/source enums and serializable `EncounterWave` / `EncounterEnemyEntry` data.
2. `EncounterController.cs` containing the runtime lifecycle, sequential-wave coroutine, set-based participant tracking, exact-once completion, cancellation, and validation.
3. `EncounterArchitectureTests.cs` and an editor validation entry for configuration and lifecycle contracts.

Do not add a singleton, factory abstraction, pooling interface, wave asset, mission graph, or generic action framework in this phase. Continue using `Instantiate`; pooling would require a separate enemy reset/activation contract and should be driven by profiling.

#### Implementation phase 2 — migrate the tutorial combat boundary (implemented, scene authoring pending)

`Level0SequenceManager` now has one explicit `EncounterController` reference. Its combat portion is:

```text
subscribe to encounter participant/death events
StartEncounter()
wait until the encounter reaches a terminal state
continue only when it completed successfully
unsubscribe
```

The tutorial subscribes to `EnemyRegistered` so it can listen for the first attack notification, and to `EnemyDied` so it can detect the tutorial Hyena and create/activate the mod reward. Those rules remain tutorial-owned and must never move into `EncounterController`.

**Tutorial teaching (Oct 2026).** The tutorial never pauses or freezes anything. It teaches through `TutorialPromptHUD` (Cassidy's optic HUD: key chip + label, labels follow the active device via `GameInputManager.GetBindingHint`/`OnInputDeviceChanged`). Each prompt stays until its condition is met and the coroutine waits on it.
- Parry gate: on `EnemyRegistered` for the Hyena the tutorial calls `EnemyCharacter.SetTutorialInvulnerable(true)`. `EnemyCharacter.ReceiveDamage` rejects non-bypass hits as `Invulnerable` before `Damageable`, so the single damage path is unchanged. The `F Parry` prompt appears on the first `OnAttackNotifyShown`. The gate clears on `ParryHandler.ParrySucceeded` (a real stage-1 parry against that Hyena), not on the button press. `SetTutorialFrozen` and `SetParryOnlyInputEnabled` remain available but are unused by Level 0.
- Inventory gates read `GameUIManager.PlayerHUD.IsInventoryOpen` (state, not toggle counts). The Hyena mod pickup has `WorldModPickup.autoEquip = false`, so it goes to the inventory and the player slots it (`ModManager.OnModSlotsChanged` + `FindEquippedMod`). The order is: weapon → combat mode → open inventory → slot mod → close → activate mod.
- Dialogue presentation is `DialogueBubbleView` (on the `DialogueManager` object). It gets speaker, side, accent and mood portrait from `DialogueSpeaker` SOs referenced by `DialogueLine.speaker`/`mood`. `DialogueManager` keeps the flow and only swaps its text target per line. The legacy panel is the fallback when no view is present. Parry no longer advances dialogue.
- Simulation levels set `PlayerCharacter.CannotDie`: hits still apply but are clamped so health stays at 1 or above (inside `PlayerCharacter.ReceiveDamage`, before `Damageable`). The end-glitch values on the shared Volume Profile are zeroed in `Level0SequenceManager.Awake`/`OnDestroy`, because a stopped coroutine never runs its `finally`.
- The intro Timeline must not start dialogue itself. The scene's `DialogueManager` SignalReceiver, which restarted the intro at the end of the reveal, was removed. The sequence manager owns every dialogue start.

For the two existing tutorial scenes, `enemyPrefabs` and `enemySpawnPoints` remain only as hidden migration fields. If no native encounter configuration is assigned, the sequence manager obtains/adds a local controller and converts paired legacy entries into one runtime wave. It emits a development warning whenever this bridge is used. Remove the bridge once both scenes have an explicitly authored encounter.

The sequence manager's manual `spawnedEnemies`, `enemiesAlive`, spawn/count/wait methods, and direct `EnemyCharacter.Died` subscription ownership are removed. The unused post-pickup enemy spawning method/fields were also removed after a source and serialized-scene audit found only null authored values. No scene was bulk-edited as part of the code extraction.

#### Implementation phase 3 — level goal and statistics (implemented, scene wiring pending)

`LevelGoal` now has an explicit `RequiredEncounters` mode. It subscribes to each unique assigned encounter, recognizes encounters that had already completed before binding, and completes only when every valid required encounter publishes or already holds successful completion. Empty configuration does not auto-complete, cancellation is not success, and null/duplicate assignments are reported by validation. The old scene-scan `KillAll`, `ReachZone`, and `Manual` modes remain compatible for levels not yet migrated.

Do not change statistics in the first encounter slice. `EnemyCharacter.HandleDeath` currently reports kills directly to `LevelStatsTracker`; also subscribing the tracker to `EncounterController.EnemyDied` would double-count. Once representative levels use encounters, choose one kill source deliberately. The preferred eventual direction is for a scene-configured stats consumer to observe `EnemyRegistered` and `EnemyDied`, after which the direct enemy-to-stats call can be removed. Until that migration is complete, preserve the existing universal kill path and keep encounter events out of statistics.

Level-goal completion and statistical enemy totals remain separate concepts. A required encounter can complete through an explicit scripted removal without that removal being counted as a kill.

#### Implementation phase 4 — event-driven gates and cameras (implemented, scene wiring pending)

`CameraSwitchTrigger` now exposes one explicit lock policy: `Encounter`, `GlobalCombat`, `None`, or the temporary legacy enemy-list path. Encounter policy references one `EncounterController`, locks on `EncounterStarted`, and unlocks only on `EncounterCompleted`. Old scenes deserialize to `LegacyAutomatic`, which resolves to their prior enemy-list intent when `enableEnemyLock` is set and otherwise to global combat, so scene migration can be incremental.

Cancellation is not interpreted as successful completion and does not unlock an encounter-bound trigger. Policy locking and the trigger's one-way lock are composed independently, preventing encounter completion or combat end from accidentally clearing a one-way lock. `PlayerCombatTracker` remains available for triggers intentionally representing global current-combat engagement, but a trigger no longer combines global combat and an enemy list implicitly.

If two or more unrelated gate types repeat identical encounter subscription logic, extract a small `EncounterGate` adapter then. Do not create it pre-emptively.

#### Validation and focused tests

The implementation is not complete until tests cover:

1. Multiple sequential waves advance and complete correctly.
2. Empty waves skip safely.
3. Null and invalid entries warn and do not block progression.
4. Spawned and existing scene enemies register through the same runtime path.
5. Duplicate registration is ignored.
6. Each `EnemyCharacter.Died` affects progression once.
7. `EncounterCompleted` fires exactly once.
8. An enemy dying immediately after registration is handled.
9. A destroyed-without-`Died` participant is pruned without awarding a kill or deadlocking.
10. A disabled participant remains tracked.
11. `UnregisterEnemy` removes a scripted participant without publishing a death.
12. Cancellation stops pending work, unsubscribes safely, leaves enemies alive, and never publishes completion.
13. Repeated `StartEncounter()` calls cannot duplicate waves or enemies.
14. `Level0SequenceManager` source no longer instantiates or manually counts enemies.
15. Tutorial parry and Hyena-reward reactions remain outside encounter code.
16. Gates and level goals no longer inspect enemy health or concrete FSM states after their migration phases.
17. A required-encounter goal completes only after every assigned encounter completes.
18. Encounter cancellation never completes a level goal or unlocks an encounter-bound camera trigger.
19. Camera triggers lock/unlock from encounter start/completion events.
20. Encounter completion cannot clear an independent one-way trigger lock.

#### Definition of complete

The encounter/wave architecture is structurally complete when:

- `EncounterController` alone knows which enemies participate in its fight and when its sequential waves finish.
- Existing and spawned participants follow one exact-once registration/death path.
- Empty, invalid, cancelled, disabled, and externally destroyed cases have the defined behavior above and cannot deadlock progression.
- `Level0SequenceManager` starts/waits for combat but neither instantiates nor counts enemies.
- Tutorial-specific parry, reward, pickup, dialogue, and cinematic logic remains tutorial-owned.
- `LevelGoal` consumes encounter completion rather than reconstructing it from kills.
- Gates react through encounter events rather than per-frame health polling.
- Statistics have one—and only one—kill-reporting source during every migration phase.
- The editor validator, focused EditMode tests, and tutorial/representative-level Play Mode checks pass.

Only after this definition is met should the project consider reusable wave assets, overlapping waves, encounter replay/reset, enemy pooling, or a more general objective system.

### 15. Audited unused-script cleanup

On 2026-08-28, a code-symbol and serialized-GUID audit removed ten first-party scripts that had no consumers anywhere under `Assets`:

- The superseded `CharacterBase`.
- Empty prototype `Item` and `Weapon` components.
- Unused `DropRateTable`, `ModEffectBase`, and `Items` crafting prototypes.
- Unused `ChargeStrikeSplitBehaviour` and `SpineEnemyMovement` enemy prototypes.
- The fully commented-out `TutorialManager` prototype.
- The unused `DashTiltEffect` prototype.

Their matching `.meta` files were removed with them. Runtime and editor assemblies compile with 0 errors after the deletion.

Legacy-looking scripts with active serialized consumers were deliberately retained. In particular, `DetectionZone`, `ActivateTestManager`, `TestManager`, `UIManager`, older level UI components, and scene-specific trigger scripts must not be deleted until their referenced scenes/prefabs are explicitly migrated or retired.

### 16. Brute boss (first boss) and grabber-owned grabs

- `Scripts/New Enemies/Brute/`: `BruteEnemy` (identity, charge armor, long stun when the dash is parried), `BruteBrain` (weighted Taunt+Attack / Charge+Dash with cooldowns, a close-range grab, and phase two at 50% HP via phase-aware capability wrappers; the serialized tuning is never mutated), `BruteGrabState` (warning, reach, hold, throw), and `BruteAnimationPresenter` (reuses the Enemy_3 clips by holding single frames; no Spine edits).
- **Armor:** while in `ChargeState`, `BruteEnemy.ReceiveDamage` returns `DamageOutcome.Blocked` and plays its own feedback (spark, `EnemySoundProfile.armorClank`, camera shake, flash, jitter).
- **`DamageRequest.Unparryable`** (set with `AsUnparryable()`): `PlayerCharacter` skips parry for these hits, while i-frames and shields still apply. Used by the Brute grab.
- **Grabs:** `GrabInfo.Anchor` (follow a bone) and `GrabInfo.HoldUntilReleased` let the grabber own the timing through `IGrabbable.ReleaseGrab` / `CancelGrab`. The Robot keeps the timed path. `BruteGrabState.Exit` always cancels a held grab, so stunning or killing the boss frees the player.
- **Optional `IMeleeLunge`:** `MeleeAttackState` glides toward the target during the swing. Enemies without it are unchanged.
- **Prefab:** `ENEMIES/Brute/Brute Boss.prefab` (with `Brute Stats.asset` and `Brute Sound.asset`). It is checked by Validate Enemies and `BruteBossTests`.
- **Verified in Play Mode (Service Floor):** armor blocks, charge → dash, a parried dash gives a 2.5 s stun, grab → hold → throw, stun mid-hold frees the player, phase two multipliers apply, 0 console errors. **Pending:** feel tuning, the clank clip, and a real player parry and dash-through.

## Verification Recorded

On 2026-08-26 with Unity 6000.3.22f1:

- Runtime and editor assemblies compiled successfully after the mod cleanup.
- Runtime and editor assemblies compile with 0 errors after the `V2.5` spawn, camera-binding, lifecycle, UI-lifecycle, and migration-tool changes. The wider project still reports pre-existing analyzer, obsolete-API, and unused-field warnings.
- `DamagePipelineTests` passed 7/7 in EditMode.
- `PlayerLifecycleConfigurationTests` passed 3/3 in EditMode before the UI extraction; one additional reusable-root UI configuration test is implemented and awaits an in-Editor rerun because Unity was open during the change.
- `Tools > JunkLite > Systems > Validate` passes with one configured `PlayerLifecycle` hosted by `Game Root`.
- The tests cover requested versus applied damage, rejection outcomes, defensive immunity, death/revive, idempotent attribute initialization, composable input locks, and composable damage-immunity locks.
- A source search found no `IDamageable`, `DamageInfo`, `TakeDamage`, `FromLegacy`, `ToLegacy`, or `OnDamaged` references in first-party gameplay scripts.

On 2026-08-27, a direct build of `Assembly-CSharp.csproj` completed with 0 errors after the lifecycle/UI extraction and editor-menu rename. Existing third-party, obsolete-API, analyzer, and unused-field warnings remain.

After the weapon-loadout extraction, direct runtime and editor assembly builds completed with 0 errors. Five focused `PlayerWeaponLoadoutTests` compile and cover equip, atomic replacement, swap/pickup pairing, break removal, and active-player-prefab configuration; they still require an in-Editor EditMode run.

The user subsequently confirmed the current tests pass and weapon pickup/break behavior works in Play Mode.

On 2026-08-27, after the enemy-AI vertical slice and interruption audit:

- `Assembly-CSharp-Editor.csproj` builds with 0 errors. Existing unrelated/third-party warnings remain.
- Five focused enemy architecture tests (eight generated cases) compile. They cover Grunt/Hyena prefab composition, composed capability resolution, exact-once death notification, and source guards preventing movement/Level 0 from depending on concrete enemy states.
- The enemy tests could not be executed through a second Unity batch process because the project is already open in Unity; run them from the open Editor before gameplay approval.

On 2026-08-27, after migrating the remaining active enemy prefabs:

- Direct runtime and editor assembly builds complete with 0 warnings and 0 errors when warning output is suppressed for the focused compile check.
- The architecture suite now contains nine focused tests producing fifteen cases. Added coverage checks Robot composition/runtime capabilities, Flying Dummy brain/hover separation, passive-dummy settings, and source guards that keep migrated identity classes free of decision FSMs.
- Robot, Flying Dummy, Patrol Dummy, and Dummy still require the in-Editor validator, EditMode tests, and focused Play Mode checks below. Grunt and Hyena behavior was already confirmed working by the user.

On 2026-08-28, after implementing encounter phases 1–2:

- `EncounterTypes` and the scene-local `EncounterController` implement explicit prefab/existing participant modes, sequential waves, set-based living tracking, exact-once death/completion, destroyed-participant pruning, scripted unregister, cancellation, and runtime/configuration warnings.
- `Level0SequenceManager` delegates spawning and completion to the encounter while retaining tutorial-owned parry and Hyena-reward reactions. The two existing tutorial scenes remain compatible through hidden legacy arrays and a warning-emitting runtime conversion bridge.
- The direct runtime and editor assembly builds complete with 0 errors. Existing unrelated/third-party warnings remain.
- `EncounterArchitectureTests` and `Tools > JunkLite > Systems > Validate Encounters` are implemented. The focused tests compile but still require an in-Editor EditMode run.
- No scene or prefab was changed. Unity generated the runtime encounter script `.meta` files; the Editor will generate metadata for the new validator/test files when it imports them.

On 2026-08-29, after implementing encounter phases 3–4:

- `LevelGoal.RequiredEncounters` consumes authoritative encounter completion and retains the old Kill-All path for unmigrated levels.
- `CameraSwitchTrigger` supports one explicit encounter/global/none/legacy lock policy. Encounter locks are event-driven, cancellation stays locked, and the independent one-way lock composes safely with policy state.
- The encounter validator now checks encounters, required-encounter goals, and camera lock configuration. Five focused consumer tests were added for aggregate completion, cancellation, start/completion locking, and one-way lock composition.
- Direct runtime and editor assembly builds complete with 0 errors. The new focused tests compile but still require an in-Editor EditMode run because the project was already open in Unity.
- No scene or prefab was changed. Native encounter, goal, and camera-trigger references must be authored with user review.

After the unused-script audit on the same date, the direct runtime and editor assembly builds completed with 0 errors. The audit checked both code symbols and script GUIDs across all assets before deletion; no scene, prefab, or ScriptableObject referenced a removed script.

No scene or prefab was changed for the mod-runtime or camera-binding cleanup. Unity generated only the `.meta` for the new mod runtime script.

## Known Gaps and Required Gameplay Verification

Automated tests cover contracts and lock composition, not animation/physics/VFX timing. Before adding more abilities, verify these paths in Play Mode:

1. Enter and leave Mod Combat repeatedly with active and passive mods installed.
2. Explicitly unequip an idle mod and a currently executing mod.
3. Consume the last durability point and confirm the final activation completes while the slot becomes empty.
4. Disable/kill the player during each active ability and confirm input, visibility, camera, physics, and VFX are restored.
5. Pulse Barrier: activation, absorbed hit, partial overflow, depletion, expiry, mode exit, and removal.
6. Don't Blink: no target, successful target, rejected damage, and target death during vanish.
7. Social Distance: multi-collider enemy, invulnerable enemy, pulse cancellation, and VFX cleanup.
8. Energy Wave: capture, repeated ticks, enemy death during drag, early pulse destruction, and restoration of previously disabled NavMesh/kinematic state.
9. Phantom Strike: charge gain/reset, successful slam, multi-collider AOE, camera reset, cancellation, and UI rebinding.
10. Fire, Electric, Lifesteal, and Pogo behavior after actual-applied-damage migration.

For the scene-local camera slice, verify initial spawn follow, death-camera switching, respawn snap, camera-switch triggers, follow freeze/unfreeze, zoom effects, and loading V2.5 repeatedly from another scene.

For the restart transition, verify that restart begins loading immediately, the video does not sit frozen for the former six-second delay, the scene activates cleanly, and player input/camera/UI are restored once.

For the focused UI lifecycle, verify that exactly one HUD, pause menu, game-over screen, and loading screen are created; pause/resume visibility is correct; an open inventory closes before the game pauses; death hides the HUD and shows game over; the game-over restart button revives at the primary spawn; HUD data rebinds; and a failed/invalid scene request restores UI and pause input.

For the player weapon loadout, verify pickup into either slot, replacing an occupied slot, dropping, drag/click swapping, melee and ranged holder visibility, entering/leaving Mod Combat, durability UI updates, last-hit break removal, automatic combat-mode exit when the final weapon breaks, and the Level 0 pickup tutorial.

For the enemy architecture, verify in Play Mode:

1. Run `Tools > JunkLite > Systems > Validate Enemies`, then run `EnemyArchitectureTests` in EditMode.
2. Grunt: detect the player, chase, stop at configured distance, perform one complete wind-up/swing/cooldown, and choose attack/chase again correctly.
3. Grunt: lose the player beyond pursuit range, move to the last known position, return to idle, and reacquire correctly.
4. Grunt: normal hitstun, knockback, parry stun, attack interruption, death VFX/drop, and exactly one death/wave decrement.
5. Hyena: patrol, detect/chase/melee, reactive dodge, successful counter-dash, missed-dash stun, target loss during actions, parry, and death.
6. Repeat the Hyena check on EASY, Blue, and Green so prefab-specific tuning and hitbox references are confirmed.
7. Confirm combat music/tracking and the Level 0 attack-warning freeze/death-wave flow still work without level code reading the enemy FSM.
8. Robot: patrol, detect, charge, dash damage, successful and failed grab rolls, throw, recovery, target loss during each action, knockback, parry recovery, death/drop, and reacquisition.
9. Flying Dummy: patrol at its authored height, detect/follow, stop near the player without repeatedly firing completion decisions, return to patrol height after target loss, accept knockback, and fall on death.
10. Patrol Dummy: patrol continuously and ignore player detection. Dummy: remain idle, preserve invincibility/health-reset settings, and complete normal death when configured as mortal.

The broader foundation still needs representative player/enemy/weapon gameplay verification. The migrated `V2.5` scene still needs visual and functional Play Mode approval.

For the encounter system, run `EncounterArchitectureTests`, validate a natively authored encounter, and verify both tutorial scenes first through the legacy bridge and then with explicit scene-local encounter configuration. Also wire one representative `LevelGoal` to `RequiredEncounters` and one `CameraSwitchTrigger` to the `Encounter` lock policy. Confirm sequential wave activation, tutorial attack/parry handling, Hyena reward creation, cancellation staying incomplete/locked, and successful continuation/unlock only after `EncounterCompleted`.

## What Should Be Done Next

### Gate 1: Play-test the completed combat/mod slice

Fix only regressions inside the implemented boundaries. Do not add another abstraction layer during verification.

### Gate 2: Harden the game-root migration workflow

1. Re-run `Tools > JunkLite > Systems > Rebuild` only when the training scene infrastructure needs to be regenerated.
2. Inspect the preserved authored content, enter Play Mode, and verify the player spawns at `Level Context/Player Spawn Point` and the main Cinemachine camera immediately follows it.
3. Run `Tools > JunkLite > Systems > Validate` after infrastructure changes.
4. Migrate one additional gameplay scene and one menu/non-gameplay scene with user review.
5. Retire legacy spawn/UI fallbacks only after all scenes use the new workflow.

### Gate 3: Verify the extracted player and UI lifecycles

The player lifecycle boundary has been extracted. In Play Mode, verify:

1. Player spawn at the `LevelContext` primary typed spawn and immediate camera/HUD binding.
2. Death presentation, `PlayerDied`, game-over state, and delayed soft respawn from the game-over button.
3. Camera, HUD, combat tracking, trigger state, enemies, and debug UI all rebind to the revived player.
4. Confirm the pause menu, game-over screen, and loading presentation are created once by `GameUIManager` and remain correct across restart and scene transitions.
5. Confirm a gameplay scene with `LevelContext.SpawnPlayer` disabled creates no player HUD while retaining pause/loading UI.

### Gate 4: Verify player weapon loadout

Completed according to the user's current test and pickup/break report. Keep the broader replacement/drop/swap checklist above for regression passes.

### Gate 5: Validate the composed enemy migration

Run the validator, focused tests, and Play Mode checklist above. Fix regressions inside the existing character/perception/brain/state/behavior/controller boundaries. Do not add `EnemyDefinition` until a concrete content requirement proves it is useful.

### Gate 6: Verify and author encounter consumers

Encounter phases 1–4 are implemented. Run `EncounterArchitectureTests` and `Tools > JunkLite > Systems > Validate Encounters`, then Play Mode-check the Level 0 combat boundary and the required-goal/camera-lock cases above. With user review, author one explicit scene-local encounter in each tutorial scene and migrate representative goals/triggers from compatibility modes. Remove the Level 0 legacy bridge and per-frame legacy enemy-list usage only after their serialized consumers are migrated. Keep enemy lifecycle behind `EnemyCharacter.Died`; do not introduce reset/replay, reusable wave assets, pooling, a global AI event bus, or a universal action graph.

### Weapon execution extraction boundary

`WeaponManager` remains the authority for attack eligibility, selection, combo state, cancellation, durability, and public combat events. Plain C# `WeaponAttackExecutor`, `WeaponAttackMotion`, `WeaponHitResolver`, and `WeaponDamageResolver` helpers own their focused operational work without adding prefab components or competing attack state. Stop this refactor here; only add another weapon abstraction when an authored weapon behavior proves the current definition/executor boundary insufficient.

## Performance Priorities

The current architecture does not need ECS or a broad pooling rewrite. Useful near-term work is:

- Keep AOE receiver deduplication and reusable physics buffers in frequently executed paths.
- Profile fixed-size overlap buffers for truncation before increasing them blindly.
- Keep scene-wide searches in initialization paths and cache the resulting references.
- Profile VFX/projectile pooling before expanding pooling infrastructure.

## Definition of Closed for the Current Combat/Mod Foundation

Structurally complete now:

- All first-party damage producers use `DamageRequest`/`DamageResult`.
- Downstream weapon/mod hit consumers receive actual applied damage.
- No damage producer directly edits health.
- Legacy damage contracts and events are removed.
- Damage resolution owns validation/math/state mutation; actors own reactions.
- Mod ScriptableObjects contain configuration, not per-player execution state.
- Executions have deterministic completion/cancellation cleanup.
- Mod installation and combat-mode lifecycles are distinct.
- Player ability locks compose correctly.
- Unity compiles and all 7 focused EditMode tests pass.

Still required before calling the slice gameplay-closed:

- Complete the Play Mode checklist above.
- Verify representative player, enemy, weapon, status, and mod prefabs in the Inspector.

## Continuation Prompt for a New Codex Task

> Read `ARCHITECTURE_HANDOFF.md` completely and inspect the current code before changing anything. JunkLite is single-player. The player/combat/lifecycle/loadout and focused weapon-execution work described here is implemented. The composed enemy architecture is implemented across Grunt, all Hyena variants, Robot, Flying Dummy, Patrol Dummy, and Dummy. Encounter phases 1–4 are implemented: `EncounterController` owns scene-local waves and participant lifecycle; `LevelGoal` consumes required encounter completion; `CameraSwitchTrigger` supports an explicit event-driven encounter lock; and `Level0SequenceManager` owns tutorial reactions/sequencing with a temporary legacy-array bridge. Run the enemy and encounter validators/tests, complete the Robot/Flying/passive and Level 0 Play Mode checklists, then author explicit encounter/goal/trigger references with user review and retire compatibility paths only after their consumers are migrated. Do not add reset/replay, reusable wave assets, pooling, networking architecture, a universal AI/ability graph, a service locator, or a broad manager rewrite.

## Synchronization Checklist

Before changing computers:

- Commit and push this handoff and its associated script changes.
- Note the active branch and Unity editor version.

On the other computer:

- Clone or pull branch `4.7` (or the branch containing this document).
- Open the project with Unity 6000.3.22f1.
- Open a new Codex task against the repository.
- Use the continuation prompt above.


## Sword grapple prototype (2026-10-09)

Implemented for the rooftop's existing `Player_2.2` prefab. Gameplay verification is pending.

### Controls and behavior

- An unbroken sword in either equipped loadout slot enables grappling only while drawn in Mod Combat mode (Q). Holstered weapons, inventory/world pickups, disabled/detached weapon objects and empty slots do not enable it. `WeaponData.enablesSwordGrapple` is enabled on the current Sword asset; other weapons default to false.
- Hold left or right Ctrl for mouse aiming in the current movement plane. Releasing Ctrl immediately hides the preview; a sword already fired continues travelling and pulling. Normal time and gravity continue while aiming in free movement.
- Cyan marks a valid wall anchor; red marks an invalid target or the range limit. The ray stops at the first solid blocker. The player's capsule must also have clearance along the route and at arrival.
- LMB throws the visual sword and rope, then pulls the player after an adjustable impact delay (default 0.1 seconds). The embedded sword and rope remain visible during the delay. On arrival the sword becomes available again and `Jump_Wall` plays once, retaining its final pose for the wall hold.
- Hold Ctrl and press LMB to chain to another point on the same wall or an opposite wall. The old hold stays active while aiming/throwing and during the impact delay as long as movement input points toward that wall. Releasing Ctrl cancels aiming and retains a directionally held wall. Releasing the wall direction during an already-fired chain drops the old hold but lets the new sword finish its flight/pull.
- Hold movement toward the wall (A for left, D for right) to remain attached at a fixed height. Neutral or away input drops the hold; pressing toward the wall again permits ordinary downward sliding, not automatic reattachment. Airborne contact while pressing toward a wall also permits sliding without needing a sword. Space from a grapple hold, or from nearby wall contact with a drawn sword, launches up and away even with a spent air jump. A short push-off window preserves outward speed before ordinary air steering resumes. During aiming/flight without a hold, Space cancels the grapple and attempts the usual ground/air jump.
- No grapple durability cost, time limit on holding, or damage immunity in this prototype. Combat, dash, parry, active mod activation, and loadout changes are blocked during the grapple transaction. Stun, death, disable, lost weapon/anchor, camera-axis reposition, or another movement owner cancels it.
- Pausing freezes the current transaction. Switching to another input map during unpaused gameplay cancels it.

### Ownership and tuning

- `PlayerSwordGrapple` owns Idle/Aiming/Flying/ImpactDelay/Pulling/Holding, the reserved weapon, and its composable movement/physics leases. It writes motion through `Character2D5Controller`, whose external reposition event cancels traversal before teleport/camera-plane changes.
- `SwordGrappleTargeting` shares the preview/throw validation; `SwordGrappleVisuals` is a prefab component instantiated once per player and reused for subsequent throws. No runtime weapon/pickup instance is thrown or recreated, preserving durability and mods.
- `PlayerState.IsWallAttached` drives animation separately from `IsGrappleActive`, so the pose remains while aiming from a hold. Existing serialized `wallSlide` animation fields are retained for the `Jump_Wall` clip, but the old wall-slide behavior/state is removed.
- `Assets/Game/Scripts/Player/Sword Grapple Settings.asset` is already referenced by the player prefab. Current values: range 10, throw speed 48, impact-to-pull delay 0.1 seconds, constant pull speed 22, clearance 0.025, minimum distance between chained anchors 1. Acceleration and distance-based easing were removed. Wall Contact Distance is 0.08, Wall Slide Speed 2, Wall Jump Away Speed 6, Wall Jump Up Speed 12 and Wall Jump Push Off Time 0.15 seconds. Speeds and distances use Unity world units. The visual prefab's `Sword Visual Length` is 1.25 (previously 1).
- The visual prefab uses `Sword Grapple Lines.mat` for aiming and a separate textured `Sword Grapple Rope.mat` for the deployed rope, both URP Particles/Unlit with depth testing. The flight defaults to the existing weapon icon; a dedicated character throw animation remains future presentation work.
- Current anchors are static, near-vertical colliders on the `Wall` layer (12), including triggers when `allowTriggerAnchors` is enabled (the default). Non-attachable triggers are ignored. Solid `Default`, `Ground`, `Wall`, `Damageable`, and `Enemies` colliders block targeting/travel. Floors, ceilings, dynamic/kinematic Rigidbody anchors, swinging ropes, and moving platforms are outside this prototype.
- The rooftop already has `Wall Collider` and `Wall Colliders` objects. To author additional anchors, assign suitable collision geometry to `Wall` and ensure the full player capsule can fit; render meshes alone cannot receive throws. No rooftop geometry/layout was changed for this feature.
- Unity was not available for asset import during this change. Only the new GUID metadata required to wire the component, settings, and material was authored; Unity generates metadata for the remaining new scripts/tests on import.

### Verification

- Runtime and focused test sources compile using the installed Unity 6000.3.22f1 Roslyn compiler and project references.
- Static checks confirm the generated input wrapper matches the input-actions JSON, the prefab/component/settings/material references are connected, and existing Sword data changes only by its grapple eligibility flag.
- `SwordGrappleTests` covers range/occlusion, capsule route/arrival clearance, both movement planes, trigger and Rigidbody exclusions, both weapon slots/broken weapons, chained travel, combat gating, durability preservation, independent lock cleanup, and prefab wiring.
- Test execution could not start because the open Unity editor owns the project lock. Run `SwordGrappleTests` in Window > General > Test Runner > EditMode after import. Also rerun `MovementAndCameraRegressionTests` and `PlayerWeaponLoadoutTests` for surrounding-system regressions.
- In Rooftop Scene v2 Play Mode, check: no-sword rejection; sword in either slot; cyan/red preview; same/opposite wall chains; wall pose lasting throughout hold/re-aim; Space release; obstacles entering during a pull; stun/death/respawn; pause/resume; and camera-turn triggers. Check both normal and Mod Combat mode, and confirm sword durability is unchanged by traversal but still consumed by combat hits.

### Grapple input/targeting correction (2026-10-09)

- Ctrl aiming now uses press/release events, with a held-state check to recover from a missed release. Release removes only the preview; it preserves an established wall hold or a throw already launched.
- LMB resolves the current pointer immediately and starts the flight transaction before the next physics step. It no longer relies on a previous frame's `HasValidAim` flag or a queued click that could be discarded when Ctrl is released.
- Gameplay targeting uses the Rigidbody position, while rope drawing uses the interpolated Transform. An isolated Unity regression exposed same-wall chaining failures when the rendered Transform lagged the physics body.
- Rejected throws now report a concrete reason and the first collider/layer hit in the Editor Console, prefixed `[Sword Grapple] Cannot throw`. Valid wall layers can still be rejected when a different solid collider is in front, the target is out of range, or the player's capsule cannot fit/reach it. Collision protection remains enabled.
- Validation: 15 focused cases executed successfully in an isolated Unity 6000.3.22f1 project using the actual compiled runtime, including real keyboard/mouse action callbacks, Ctrl release, flight/pull/hold, chaining, range/obstruction, weapon eligibility/durability, and lock cleanup. The asset-configuration test is excluded from that isolated project; prefab wiring is checked separately in the main project. This is not a full rooftop Play Mode test.
- A read-only sample of the rooftop's 79 native box colliders reproduced both valid anchors and red targets caused by clearance/other blocking colliders. Prefab/mesh geometry is outside that reduced scene sample; a live rejection log is needed to identify a particular red target in the complete scene.

### Rooftop red-target diagnosis (2026-10-09)

- The reported `Ceiling_Floor (53)` is nested in `Cyber Punk Room 1.prefab`. Its source has an enabled, non-trigger BoxCollider (local size `8.1, 6.2999988, 0.1000016`, center approximately zero), but both room instances in `Rooftop Scene v2` explicitly remove that component through prefab overrides. Changing the visible object's layer to Wall does not restore its removed collider.
- Recent Editor throw logs report no collision hit within the 10-unit range, or a hit on the Default-layer floor boxes owned by `Track 01 All Assets`. They do not report hitting the selected wall.
- Required scene authoring: outside Play Mode, restore only the removed BoxCollider on the intended `Ceiling_Floor (53)` instance, keep that collider's GameObject on Wall, and save the scene. Do not revert all room overrides or change the whole track to Wall. Aim at the wall's vertical face within 10 units with room for the player's capsule. Scene files were left unchanged in this diagnostic pass, following the user's scene-edit boundary. The user subsequently confirmed the collider works when non-trigger and needs to stay a trigger for other systems; the trigger-support update below supersedes the earlier non-trigger requirement.
- Fixed the separate `MissingReferenceException` on leaving Play Mode: grapple visual cleanup now tolerates the independent visual root being destroyed before the player. All 16 focused cases pass in the isolated Unity physics/input harness, including that destruction order. This does not replace a live rooftop check after restoring the wall collider.

### Trigger walls, one-shot attachment and editable rope (2026-10-09)

- Aiming and flight share a nearest-hit query combining solid blockers with attachable-layer trigger anchors. Other trigger volumes remain ignored. Capsule clearance and movement sweeps still test solid obstacles; this feature never changes a collider's trigger flag or global physics settings.
- `SpineAnimationController` now honors `Wall Attach Loop` (serialized `wallSlideLoop` for compatibility). It is off on `Player_2.2`. Each new attachment plays `Jump_Wall` once and retains its final pose; aiming while held reuses the same track entry. A new attachment after pulling plays it again. Animation name and playback speed remain editable on the player prefab.
- `Assets/Game/Prefabs/PLAYER/Grapple/Sword Grapple Visuals.prefab` owns the Aim, Target, Rope and Thrown Sword renderers. `Player_2.2` directly references it; the grapple settings asset also retains a fallback reference. It is instantiated lazily, unparented to avoid the player's facing/scale changes, reused and cleaned up with the player.
- Edit the Rope child's Line Renderer for width, color gradient, material and Texture Scale. Default width is 0.1, Texture Mode is Tile and X Texture Scale is 3 repeats per world unit. Runtime updates positions/visibility without overwriting these choices. Edit marker colors/radius on the root component; edit marker thickness on the Aim/Target renderers. Turn off `Use Weapon Icon` to supply your own thrown-sword sprite in the prefab.
- Rope assets are `Assets/Game/Prefabs/PLAYER/Grapple/Sword Grapple Rope.mat` and `Sword Grapple Rope.png`. The rope has its own material so changing its texture does not affect the cyan/red aim marker. The texture repeats horizontally, clamps vertically, uses alpha transparency and mipmaps, and has a 1024 import cap. Prefab/material/texture metadata was generated by Unity in the isolated authoring project; the prefab's script reference points to the first-party source component in this project.
- No scene changes or changes to the user's room-prefab edits were made in this update. Keep the intended wall collider enabled, on Wall, with Is Trigger on as required by the other systems.
- Validation: runtime and first-party Editor code compile with Unity's Roslyn compiler. All 21 focused tests pass in the isolated Unity physics/input/Spine harness: trigger anchors and chaining, ignored trigger volumes, solid occlusion, flight interception, one completion and the actual final keyed bone pose, restarting on a new attachment, prefab customization, and previous grapple regressions. Main-project asset references were checked separately; final rope appearance and gameplay feel still require Rooftop Play Mode review.

Rope texture generated with the built-in image generation tool. Final prompt:

> Create a production game texture: a seamless repeating horizontal strip of a single taut twisted three-strand rope for a Unity LineRenderer. The rope runs exactly left to right, fills the entire canvas width, and continues beyond both left and right edges with no end caps or knots. Neutral light warm gray/tan fiber, three clearly defined spiral strands and subtle thread detail, soft cylindrical shading across the vertical width, crisp readable silhouette. Transparent background above and below only, rope body occupies almost all image height (90%). Wide rectangular canvas aspect ratio about 4:1. Orthographic front view, no perspective, no drop shadow, no text, no labels, no scene, no extra objects. Left and right boundaries should tile continuously with matching rope strand phase. This will be a small tile repeatedly sampled along a moving gameplay rope, so favor regular even braid spacing and clean edge-to-edge tiling.

### Equipped sword and visual integration correction (2026-10-09)

- Grappling now requires `WeaponManager.IsModCombat` and an active, unbroken grapple-enabled melee weapon in the player's own equipped hierarchy and loadout. Keeping a sword in a holstered slot is insufficient. Combat-mode changes are observed alongside equipment changes; a forced holster or lost/broken sword cancels traversal and releases its locks. Temporarily hiding the held sword renderer during flight does not count as unequipping it.
- `Player_2.2` now directly serializes the visual prefab on `PlayerSwordGrapple`. The settings asset reference is a fallback for other player prefabs. `EnsureVisuals` validates renderer bindings, creates/activates the instance, and restores a destroyed/disabled instance during an active grapple. Missing visual configuration reports an error and blocks traversal instead of silently allowing an invisible grapple.
- Visuals use the Player object layer and Player sorting layer (order 12), and dynamic occlusion culling is disabled on their renderers. They retain ordinary material depth testing and physical target clipping. Position counts/world-space geometry are initialized explicitly; user-authored width, material, tiling and color choices are retained.
- Validation: 24 focused Unity/Spine/input cases pass, including missing/holstered/detached sword rejection, forced holster cancellation and recovery of the aim, rope and sword presentation. Runtime and first-party Editor code compile. The exact visual prefab, materials, rope texture and actual sword icon also render successfully in an isolated Unity 6000.3.22f1 / URP 17.3.0 GPU check, with a camera restricted to the Player layer; aim and flight screenshots are in `Temp/GrappleVisualCheck/`.
- The reported missing visuals could not be reproduced by importing/rendering the assets in isolation. The integration changes cover missing/stale instances and rendering membership, but a live Rooftop check is still required after restarting Play Mode so the existing player is respawned with its new prefab reference. No scene or room-prefab edits were made.

### Wall alignment and grapple timing refinement (2026-10-09)

- Wall clearance was 0.1 world units, and arrival could permanently stop up to another 0.06 units short. Clearance is now 0.025. The hold keeps the calculated destination instead of the early arrival position and settles there through collision-swept movement. Arrival Distance still controls when the attach pose starts. No teleport or collision bypass is used.
- The hand-height offset now follows the wall tangent on supported sloped walls, so it does not add/remove capsule clearance depending on slope direction. The remaining clearance is measured from the player capsule to the wall collider, not between visible meshes. If the rendered pose still has a larger gap, inspect the wall collider's face against its mesh and the player capsule against the final `Jump_Wall` pose; these authored shapes were not changed. The production player capsule has radius 0.35, root scale 1.1 and an offset center.
- Throw Speed increased from 40 to 48, Pull Speed from 16 to 19 and Pull Acceleration from 65 to 78. Sword Visual Length increased from 1 to 1.25 on the visual prefab. These remain Inspector-editable; no new wiring is required.
- `Impact To Pull Delay` on the settings asset defaults to 0.1 seconds; zero starts pulling immediately. The fixed-time countdown pauses with gameplay. Free movement/gravity continues until the pull starts; a chained grapple retains the existing wall hold. The route is revalidated after the delay, and a missing/moved anchor or new obstacle cannot pull the player through geometry. Sword/rope rendering includes the delay phase, while releasing Ctrl still hides only the aim preview.
- Validation: all 34 focused cases pass in the isolated Unity physics/input/Spine harness using the updated runtime. New cases cover both slope directions, production-size/offset capsules facing either wall with solid and trigger anchors, delayed/zero-delay pulls, pause, impact visuals, new blockers, and retaining the old hold when a chained anchor disappears. This checks physics queries and manually stepped grapple motion; the final pose/mesh alignment and gameplay feel still need Rooftop Play Mode review. No full build or scene changes were made.

### Directional wall holding, instant pull and wall-pose alignment (2026-10-09)

- `PlayerSwordGrapple` now applies the configured Pull Speed immediately, with no acceleration ramp or proximity slowdown. Only the final step is shortened to avoid overshooting, and solid collision sweeps can shorten/block travel. Removed the unused Pull Acceleration and Slowdown Distance Inspector fields; user tuning now has one speed authority.
- Holding requires raw movement input toward the held wall. Intent is read independently of the grapple's movement lock, which otherwise clears controller movement input. Neutral/away input releases the hold and its leases. Arrival without the correct directional input does not retain a hold. A pending chained sword continues if the old wall is released.
- `Character2D5Controller` owns ordinary wall-contact sliding and jump velocity, configured by the existing grapple component/settings. Static Wall trigger anchors remain supported. Nearby contact plus directional input stops horizontal penetration and allows rising motion or downward sliding; grounded, stunned, locked, dashed, rolling and grapple-owned movement do not slide. Releasing direction returns to normal gravity. `PlayerState.IsWallSliding` is separate from `IsWallAttached`; Spine/Animator/audio consumers handle both without turning a slide into a grapple hold.
- Space from a hold launches up and away, emits the usual jump event, and restores an air jump. The configurable push-off interval prevents held input from immediately cancelling outward speed. Wall jumps from ordinary contact still require a drawn, eligible sword; sliding itself does not. Jump input is gated by pause and player jump capability. Existing control interruption clears the push-off state.
- Measured the actual `Cas.json` final `Jump_Wall` pose using Spine in isolated Unity (scale 0.02). Its foremost hand mesh vertex is approximately x=0.0991 in skeleton space; the prefab's BODY SPINE offset is -0.247. At root scale 1.1, compared with capsule center x=-0.17522341, radius 0.35 and clearance 0.025, the hand is about 0.38 world units behind the target wall. This explains a visible gap even when the wall collider matches the mesh.
- Added `Wall Pose Forward Offset` on `SpineAnimationController`, authored to 0.345 local units on `Player_2.2`. It moves only the child Spine artwork during hold/slide, mirrors with facing, and restores on release/disable; the physics capsule stays outside the wall. This is independently adjustable for pose/skin art changes. The cyan wire box drawn by `LedgeDetection` is its trigger bounds, not a wall-hook distance gizmo. Wall Clearance remains the physical capsule-to-wall gap; Wall Contact Distance is only the ordinary-contact probe range.
- Validation: 46 focused grapple/wall cases and all 13 existing movement/camera regression cases pass in the isolated Unity harness against the compiled first-party runtime. This includes real input callbacks, both movement planes, solid/trigger walls, unchanged holding height, release/repress sliding, no-sword sliding, constant speed at pull start and near arrival, spent-air-jump push-off, chain release, and artwork-only offset cleanup. No full build or scene edits. Final visual alignment and movement feel still need a Rooftop Play Mode check after reimport/respawn.

### Held-Ctrl chaining and pull-speed adjustment (2026-10-09)

- On reaching/returning to a valid directional wall hold, `PlayerSwordGrapple` now reads the current Grapple Aim action and resumes aiming while Ctrl remains held. Releasing and pressing Ctrl again is no longer required. The same check runs before consuming LMB so a follow-up click arriving before Update can still start the next throw.
- Resuming requires enabled gameplay input, an unpaused game, a usable equipped sword, a valid wall anchor and directional input toward that wall. A Ctrl release during flight/pulling keeps the preview hidden at arrival. The change does not restart aiming from Idle after cancellation.
- Pull Speed increased from 19 to 22 in both the settings asset and class default. It remains constant-speed movement without an acceleration ramp.
- Validation: all 50 focused grapple/wall tests pass in isolated Unity, including actual keyboard/mouse callbacks for both Ctrl keys, preview restoration, chaining without a new Ctrl press, clicking before Update, and releasing Ctrl before arrival. Test setup now preserves/restores temporary default Input System settings safely across multiple cases. Runtime compilation passed; no full build or scene edits.

### Grapple aiming presentation refresh (2026-10-09)

- Replaced the plain circle/solid-line presentation with cyan corner brackets, a bright central diamond for valid targets, a coral X for invalid targets, a faint inner ring and a lighter guide line with animated directional dashes. Shape and color both communicate validity. A short contracting pulse plays when aiming begins or a target becomes valid; animation uses scaled gameplay time.
- The marker faces the camera and uses a 22-pixel radius by default, constrained to 0.1–0.65 world units to keep it readable across camera zooms. `Scale Marker With Camera`, `Reticle Pixel Radius` and `World Radius Limits` control this; `Reticle Radius` is the fallback when camera scaling is disabled. The guide stops before the marker so it does not cross through the status glyph. Physics still determines the actual endpoint/range/blocker.
- All appearance is authored on `Assets/Game/Prefabs/PLAYER/Grapple/Sword Grapple Visuals.prefab`: root colors, marker size, bracket thickness, focus pulse, guide opacity, dash length/spacing/travel speed and maximum dash count. The new `Aim Details` child supplies its mesh renderer material and sorting, using the existing URP line material. Aim and Target line widths are 0.011 and 0.012; rope/sword styling is preserved.
- `SwordGrappleVisuals` reuses one dynamic mesh and preallocated geometry buffers for the decorative strokes, bounded to 48 dashes by default. Dark backing strokes improve contrast. The details hide with Ctrl release, firing, pause and cancellation, and are disposed with the visual owner, including explicit EditMode disposal. Prefabs without the optional detail mesh retain a circle fallback.
- No text/font assets were added or changed; the UI guide in `Assets/Game/New UI/` was read. Gameplay input, targeting, grapple speed and scene content were not changed. Unity serialized the prefab in an isolated authoring project; existing prefab GUID/component references remain intact.
- Validation: runtime compiles and all 53 focused grapple/wall/presentation cases pass in isolated Unity. New checks cover bounded/reused mesh geometry, release/flight visibility, disposal and screen-size consistency for orthographic/perspective camera zoom. The exact updated prefab was rendered and visually reviewed with Unity 6000.3.22f1/URP on the GPU for valid, blocked and flight states. Preview: `Temp/GrappleVisualCheck/aim-comparison.png` (valid above, blocked below). The complete rooftop backdrop still needs an in-game appearance check; no full build or scene edits.

### Keyboard diagonal movement speed fix (2026-10-09)

- The Player Move keyboard composite already uses unnormalized digital axes (`Dpad(mode=1)`), but `GameInputManager` passed keyboard values through the radial stick-deadzone helper. This normalized W+D from (1,1) to roughly (0.707,0.707). Since platformer locomotion consumes only X, holding W or S while running reduced speed by about 29.3%.
- Keyboard movement now preserves the raw digital axes. W/S attack direction remains available without reducing A/D running speed; arrow keys receive the same fix. Analog device deadzone/actuation and input bindings are unchanged. No Inspector wiring or scene changes are required.
- Validation: first-party runtime compilation passed. All 19 movement/camera regression cases and 53 grapple/wall/presentation cases pass in isolated Unity. Six new keyboard cases exercise actual input callbacks and physics-step running velocity, vertical-only intent, opposing-key cancellation and release. No full build was run.

### Grapple axis-switch verification (2026-10-09)

- Confirmed the existing grapple derives its horizontal axis from `Character2D5Controller.MovementAxis` (player-local right) and constructs the aiming/travel plane from that axis plus world up. Pointer projection, anchor validation, pulling, directional wall holding and wall jumps support both XY and ZY. Marker/sword presentation follows the viewing camera.
- `CameraSwitchTrigger` repositions the player, calls `RotatePLayer` and then `FreezePerpendicularAxis`. The rotation event cancels any active grapple and releases its movement/physics leases, hides the old visuals and restores the equipped sword. New aiming uses the new plane. An axis transition intentionally requires aiming again; held-Ctrl auto-resume currently applies to wall-to-wall chaining, not a cancelled axis transition.
- Added nine regression cases: real pointer/keyboard input through flight, pull, hold and jump at both +90/-90 degrees with either wall side and solid/trigger anchors; and the actual camera-trigger handler switching XY to ZY and back during each active grapple phase, with successful grapples after each change. All 62 focused grapple cases pass in isolated Unity. No gameplay/prefab/scene changes or full build were needed. The authored camera blend in Rooftop still merits an in-game check.

### Permanent player progression (2026-10-10)

- `PlayerProgression` is serialized on both manager prefabs and ensured by `GameRoot`/the player fallback for older scenes. It owns permanent total XP and emits one change per award, including multi-level gains. `PlayerRuntimeStats` is initialized by `PlayerCharacter` after attributes exist, and reapplies bonuses before respawn healing. Passive slot rules remain unchanged.
- Authoring asset: `Assets/Game/Resources/PlayerProgressionConfig.asset`. Menu: `Tools > JunkLite > Progression > Select Profile`. Defaults: levels 1–20, active slots at 1/5/10/15, XP costs starting at 100 and increasing by 50, +10 maximum HP and +3 additive percentage points of outgoing damage per earned level. Each level entry has editable maximum-health, outgoing-damage and armor rewards. Formula: `(base + sum(flat)) * (1 + sum(percent) / 100)`. Adding a new stat requires a typed enum value and gameplay integration; adjusting supported stats needs only asset edits.
- The Inspector provides an undoable level-table generator, validation, cumulative XP/stat/slot previews and temporary Play Mode previews. Increment `revision` when rebalancing a published profile. Migration defaults to preserving earned level and progress within it; preserving total XP is also selectable. Reducing the maximum level clamps progression. Health policy supports keeping current HP, preserving percentage, adding the new capacity (default), or healing to full. Rebinding/recalculation never heals or revives a dead player.
- `EnemyBase.experienceReward` is the editable per-enemy XP reward (default 25); `Dummy.prefab` uses zero. XP is awarded only after a player-attributed applied lethal hit, including inactive sources for lingering DoT. Rejected hits, corpse hits, source-less forced deaths, despawns and encounter cancellation award nothing. Fresh enemy lives may award XP again. Environmental deaths without player attribution do not currently award XP. Encounter/statistics events are not additional reward sources.
- `Damageable` applies outgoing player scaling once before armor across weapons, abilities and DoT. Caller-owned damage amounts remain base amounts. Use `DamageScaling.Fixed` for derived procs whose damage is already scaled; `DamageRequest.Forced` opts out automatically. Armor mitigation and its HUD read the same runtime value. Shared character/weapon/mod assets are immutable; other health-capacity changes survive progression recalculation.
- `ModManager` allocates four supported active input slots. The player prefab now serializes maximum 4 / starting 1. Progression synchronizes availability; lowered previews suspend locked-slot executions while retaining installed items. The developer unlock button is a temporary override. End Level Preview restores saved XP and clears the active-slot override. Preview XP, including combat kills while previewing, is never saved.
- `PlayerLevelUI` is built beside the portrait: clockwise rectangular XP perimeter, level badge, XP-gain text, combined level/reward notification and screen pulse. Newly unlocked combat slots flash; locked inventory slots display required levels. Level-up reuses Cassidy's authored parry distortion (see presentation notes below). Timing, colors, ripple visibility/scale and optional audio are configured in the profile. No sound is assigned by default. New text uses `UIFonts`; decorations ignore raycasts. Hiding the HUD cancels feedback and subscriptions.
- The portrait badge displays the plain level number (`1`, without `LV` or zero padding). Each earned positive XP change also spawns a cyan `+amount XP` above the bound player through the existing `DamagePopupManager` pool. XP labels pop in, rise along camera up, hold briefly and fade; reused damage labels reset their original scale/style. The profile's **XP popups** section controls visibility, camera-relative offset, horizontal spread, lifetime, rise speed and relative size; it shares **XP Color**. Existing level scenes already contain the popup manager, so no additional wiring is required. Loading/binding and level-preview changes do not spawn rewards. Runtime/editor compilation and all 30 existing progression/damage tests pass after this change; the popup appearance still needs an authored-scene Play Mode check.
- Save: `Application.persistentDataPath/player-progression.json`, schema version 1, atomic temporary-file replacement and `.bak` recovery. XP saves are debounced for one unscaled second and flushed on level-up, death, scene transitions, pause/quit and service destruction. Invalid saves or missing/invalid configuration disable writes to protect existing data.
- `Tools > JunkLite > Progression > Install Defaults` explicitly repairs configuration/player/manager prefabs and preserves an existing profile. Training reward initialization only runs when creating a new profile. Root rebuild/validation includes the progression service. No scene edits. Unity generated the new metadata in the isolated verification project.
- Validation: runtime and editor/test sources compile without errors with Unity 6000.3.22f1's compiler. All 30 focused EditMode tests pass in isolated Unity (23 progression cases plus 7 existing damage regressions), including imported profile/prefab wiring. Results are in ignored `Logs/progression-tests.xml`. The main checkout's batch editor exited before compilation; no full build or authored-scene Play Mode verification was performed. Still check portrait readability, pulse/ripple coverage, inventory/gamepad flow, four mod inputs, respawn and restart persistence in the authored level.

### Progression presentation and inventory character stats (2026-10-11)

- The portrait perimeter starts at the bottom-left corner, sweeps clockwise up the left edge and across the top/right/bottom, then closes at the bottom-left. Four segments preserve distance-based progress for rectangular portraits. The plain numeric badge sits below the frame, leaving the entire perimeter visible.
- Level-up feedback keeps its notification above the root Canvas and clones `ParryHandler.ScreenPulseTemplate` for the actual screen distortion. Cassidy's prefab overrides ShockWave2 to the current ShockWave graph; the original source prefab uses OLD ShockWave, which caused the earlier mismatch. The cloned effect preserves the parry instance's graph/property overrides and world scale, activates renderers, and restarts VFX once. The prior UI ring/tint has been removed. The profile's Show Level Up Ripple toggles it; Level Up Ripple Prefab is an optional override (default unassigned). Ripple Lifetime bounds cleanup in unscaled time; Ripple Scale multiplies the authored scale. Portrait Highlight Duration only controls the portrait color flash. Parry gameplay, slow motion and input locks are not invoked. Unbinding/hiding releases the clone; loading and direct level previews do not celebrate, while earned level-ups do.
- Clicking Cassidy's inventory artwork, or focusing it with gamepad navigation, opens her STATS in the existing details card. The player stats asset names her Cassidy. The compact card shows level, current/required XP, a horizontal progress bar and three rows: HEALTH (current/max), ARMOR (effective) and DAMAGE (cumulative outgoing bonus). Stat labels are cyan and values are pale white; item/mod stats use the same distinction. Health and damage labels are written out, including level-up rewards. Slot counts, weapon breakdowns, duplicate XP/unlock summaries and explanatory paragraphs are omitted. Maximum level uses a full bar and an explicit MAX LEVEL label. Health, armor, XP and equipment events refresh the selected card; item hover temporarily restores the normal item layout and returns to Cassidy on exit. Subscriptions are released on unbind.
- Locked active mod slots accept clicks and controller focus/confirm to show `Reach X level to unlock` in the details card. X comes from that player's progression profile, including customized unlock schedules. Inspecting a locked slot cannot equip, swap or drag a mod into it. Passive slots keep their existing non-level rules and use a generic locked message.
- Damage progression remains a player outgoing-damage bonus across fists, weapons and abilities; shared weapon base values are unchanged. Cassidy's card reads cumulative flat/percentage bonuses from the same progression profile used by the damage pipeline. Item/mod descriptions explicitly label their authored values BASE DAMAGE. No new scene or Inspector wiring is needed.
- Validation: runtime/editor compilation succeeds, and all 35 focused progression/UI/damage cases pass (`Logs/progression-ui-tests.xml`). Checks cover perimeter geometry, numeric badges, parry-template cloning/scale, lifecycle cleanup, preview suppression, live stats, label/value colors, hover restoration, max-level fill, gamepad selection and locked-slot click/confirm at both default and customized unlock levels. Actual UGUI/TMP assets were rendered and visually inspected in an isolated Unity 6000.3.22f1 project: `Logs/progression-inventory-preview.png`, `Logs/progression-locked-slot-preview.png`, and `Logs/progression-notification-preview.png`. No full game build or authored-level Play Mode check was performed; the reused parry distortion still needs a visual check with the level's VFX/URP setup.

### Render performance pass (2026-10-14)

- Room 1 was GPU-bound (16.5–19.6 ms) and VSync flipped frames between 60 and 30 fps (jitter). Volumetric fog was ~11 ms of that.
- `Assets/DefaultVolumeProfile.asset` fog: `maxSteps` 128 → 40, `blurIterations` 2 → 1. `HIGH ASSET RENDER FEATURES` MSAA 4x → 2x (SMAA stays on the camera).
- `VFX/GlitchFeatureGate` (static, self-starting): the URPGlitch passes blit every frame even at zero intensity, so their renderer features are only active while the glitch volume values are non-zero. Features are set back on at quit so the asset never saves disabled. Don't edit the URPGlitch package for this.
- `Managers/FrameRateLock` (static, BeforeSceneLoad): 60 fps lock. VSync divider on 60/120/240 Hz, plain cap otherwise.
- Result in Editor: Room 1 GPU ~7.6 ms uncapped. On Mac, capped GPU timings read higher because the GPU idles; compare uncapped.

### Level events, objective lifecycle and HUD tracker (2026-10-14)

- `ObjectiveTracker` now has states (`ObjectiveState` Inactive/Active/Complete): `Begin`, `Complete`, `GetState`, `IsActive`, `Current` (newest active, else unfinished primary), `StateChanged(def, state)` plus the old no-arg `Changed`. `startActive` list begins objectives on `Start`. Completing an inactive objective is allowed (it goes straight to Complete).
- `Scripts/LevelEvents/`: `LevelEvent` (scene component) runs an ordered `[SerializeReference] List<LevelEventAction>` when its source fires (trigger enter, objective started/completed, `SlidingDoor.PlayerOpened`, level start, manual `Play()`). It is a flat, scene-local step list, not a global bus or graph. It owns every player lock its steps take (`LockPlayer`/`AddLease`) and releases them on finish, `Abort`, disable and `PlayerLifecycle.PlayerDied`. New steps = subclass `LevelEventAction` (+ `[LevelEventActionMenu]`); `Editor/LevelEventEditor.cs` finds them via `TypeCache`.
- `SlidingDoor.PlayerOpened` event (fires when fully open with the player inside).
- HUD: `UI/ObjectiveTrackerView` on `New UI/Prefabs/New/Objective Tracker HUD.prefab`, nested in `New Player UI 2`; it rebinds to each scene's tracker. Missions: `Missions Page.prefab` (`MissionsPageView` + inactive `MissionRowView` template) is assigned to `InventoryUI.missionsScreen`, replacing the code placeholder. Both prefabs were made by *Tools > JunkLite > UI > Build Objective HUD + Missions Prefabs* (creates missing ones only); edit their look in the Inspector.
- Service Floor: the Room 8/9 wall (`PierPanels` at x≈156) and `Door_Bay_124` are removed. `[Gameplay]/Room8 Collapse` holds the cinematic: `Room8_Collapse` LevelEvent (trigger x159–165) → `Room8_Collapse.playable` (shake signals, explosion, debris anim, floor `Intact` off / `Crater` on at 1.9 s; wrap None so post-playback states keep the crater) → player falls into `[Environment]/Room 8 Basement` (floor y −10) → Crow `ServiceFloor_Room8_Collapse` → Grapple Hook granted → `ESCAPE_BASEMENT` + grapple hint. Climb out: Wall-layer chimney between `Broken_Slab` (x164.5 face) and `Wall_Chimney_Rim` (x168 face); `Room8_BasementExit` (activated by the beat) completes the objective. `Floor_Main` and `Floor_Lane_Upper` are split around the hole (x156–168).
- The Brute no longer grants the hook (`ObjectiveOnDeath.grantKey` cleared); `ServiceFloor_UseGrapple` now points at the 5A grapple box. `ZDoor_StartBruteObjective` begins `DEFEAT_BRUTE` when the Z door first opens.
- Tests: `ObjectiveSystemTests` (lifecycle), `LevelEventTests` (order, leases, sources).

### Grapple-only walls + crater view (2026-10-14)

- `Character2D5Controller`: plain wall contact no longer slides or sticks (input into a Wall-layer wall is cancelled and the player falls at normal speed), `Jump()` no longer wall-kicks from contact, and `JumpFromWall` (grapple hold kick-off) no longer refills the air jump. Walls are climbed only by aiming and firing the grapple, holding, and kicking off. Supersedes the 2026-10-09 notes about contact sliding/jumping. Tests: `SwordGrappleTests` (`OrdinaryWallContactNeverSlidesOrGrabs`, `DirectionalHoldReleaseThenRepressNeverSlides`, `JumpTouchingAWallWithTheHookIsNotAWallKick`).
- New LevelEvent step `World/Switch Camera` (`SwitchCameraAction`): activates a Cinemachine camera, binds it via `CameraManager.SetActiveCamera`, prioritizes it.
- Room 8 crater is a front cutaway: in x156–168 the front floor tiles, `Floor_Main_South`, `Floor_Upper`/`Floor_Upper_Fascia` hole pieces, front fascia/foreground panels, pillar and rail middles sit under `Room8 Collapse/Intact` and vanish at impact (`Floor_Upper`, fascia and rails are split into West/East/Back pieces). `BasementCam` (offset 0, 9.5, −44) is switched on after landing so the shaft, exit lip and Room 8 ceiling are in view; the exit event switches back to `PosZCam`. Rubble and falling chunks sit behind the walk line (z > 1.8) so nothing clips the player.

### Grapple controls: RT fires, automatic wall stick (2026-10-14)

- Gamepad: hold LT (`GrappleAim`) to aim with the right stick, **RT fires**. `GameInputManager.ConsumeGamepadGrappleFire` routes gamepad presses of `SpecialAttack`/`Parry` (both on RT) to the grapple's `PrimaryAttackConsumer` first, so RT never parries/specials while the grapple is aiming or holding. X (`Attack`) still fires too. Keyboard is unchanged (Ctrl aim, LMB fire).
- Holding is automatic: arrival sticks without pushing into the wall, and the hold stays with no input (no slide). Only **down** (`MoveDirection.y < -0.6`, S / stick down) lets go; jump kicks off. Chained throws keep the old hold until the new wall is reached. `PlayerSwordGrapple.PressingIntoWall` was replaced by `WantsToDrop`.
- Tests: `GamepadLeftTriggerAimsAndRightTriggerFires`, `HoldIsAutomaticAndOnlyDownLetsGo`, `ChainedFlightKeepsOldHoldUntilTheNewWall`.
