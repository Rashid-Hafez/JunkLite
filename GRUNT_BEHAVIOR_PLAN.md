# Grunt Behavior Plan — making the grunts feel alive

Status: Phase 5 (presence, solid bodies, launch) implemented 2026-10-23 (packages A to E compile; Play Mode verification pending), see section 8 and the handoff top section. Phases 1–3 implemented on 2026-10-23 (code compiles; prefab migration via `Tools > JunkLite > Enemies > Migrate Grunt Prefab To GruntBrain` and Play Mode verification pending). After the first Sandbox playtest the same day, leader-aware lane spacing with speed matching, Pursue/Engage modes, a re-aiming vault with abort, and the ally-hit flinch ripple (from Phase 4) were added; see the handoff's "Playtest fixes". The rest of Phase 4 is not started. Written against commit `7ab29fb7`. Implementation notes: `ARCHITECTURE_HANDOFF.md`, top section; tuning table: `PROGRAMMER_AND_DESIGNER_GUIDE.md`, "Grunt group tactics".
Scope: Grunt only (`Assets/Game/ENEMIES/Grunt/Grunt Enemy.prefab`, 25 in Rooftop, 24 in Service Floor). Hyena and Brute are untouched unless noted.

---

## 1. What the grunt does today (and why it feels dead)

The grunt has no brain of its own. The prefab runs the shared `MeleeChaserBrain` (`Scripts/New Enemies/MeleeChaserBrain.cs`) with these values: chase speed 7, stop distance 2, pursuit radius 12 (30 in Service Floor), wind-up 0.3 s, swing 0.5 s, cooldown 0.5 s, stagger 1 s.

The decision loop is:

```
Idle ──player seen──▶ Chase ──in range──▶ ask EnemyEngagementDirector
                                            ├─ I am front and lease is free ▶ MeleeAttack ▶ back to evaluate
                                            ├─ I am front, lease taken     ▶ WaitForOpening (stand still, face player)
                                            └─ I am queued (index ≥ 1)     ▶ Chase to my slot ▶ WaitForOpening
```

`EnemyEngagementDirector` (`Scripts/New Enemies/EnemyEngagementDirector.cs`) is the group coordinator. Per player it:

- Groups enemies by **side** of the player and sorts each side by distance. Index 0 is "front"; everyone else gets a slot further back (attack distance + half-widths + 0.2 gap, so slot 2 is about 0.8 units behind the front grunt).
- Hands out **one attack lease for the whole group** (not per side), with a 0.25 s hand-off delay, and refuses the last attacker if another ready grunt exists (forced alternation).
- Recomputes the side from the enemy's current position every query (0.1 dead zone). Nothing in it knows about the far side's occupancy.

Where the dead feeling comes from:

| Symptom | Cause in code |
| --- | --- |
| Queued grunt stands frozen behind the front one | `WaitForOpeningState` only calls `Movement.Stop()` and `FaceTarget()`. It never moves. |
| Nobody ever flanks | The director only ever assigns a slot on the side the enemy is already on. There is no state that crosses the player. |
| Front grunt eats a full combo and never backs off | Nothing reads `Damageable.OnDamageResolved` for decisions. Hits only cause `StunnedState` (1 s), then it re-evaluates and walks straight back in. |
| All grunts move in lockstep | Same chase speed, same evaluation on the same frame, no decision jitter. |
| The line of grunts is a static picket fence | Slots are exact points; grunts snap to them (0.12 arrival tolerance) and stop. |
| Front grunt waits politely while another attacks | Correct per the lease system, but it waits motionless at exactly attack range, which reads as "AFK". |

Physics facts that constrain the design (verified in `ProjectSettings` and `EnemyCharacter.TryIgnorePlayerBodyCollision`):

- Enemy bodies **collide with each other** (Enemies ↔ Enemies is on in the layer matrix). A grunt cannot walk through the grunt in front of it. This is why swapping places on a 1D lane needs a leap, not a walk.
- Enemy bodies **do not collide with the player** (pairwise `Physics.IgnoreCollision` on target acquire). A grunt can pass through the player.
- `DodgeState` already does a kinematic arc (`rb.isKinematic = true`, lerp with `4·h·t·(1−t)` height, wall raycast clamp). That is 80 % of a vault.

Animation facts (from `Assets/Game/ENEMIES/Grunt/Culler.json`): the grunt skeleton has `Attack, Death, Float, Hurt, Idle, Jump_End, Jump_Start, OnParried, Run, Stunt`. **There is no Roll, no Walk, no Backstep.** `Jump_Start → Float → Jump_End` is a usable leap. Pacing will have to use `Run` with a reduced Spine time scale until a Walk clip exists.

---

## 2. Design goals

1. A blocked grunt **does something** about being blocked: vault to the open side if it can, otherwise shuffle, feint, and stay readable.
2. Grunts **react to how the fight is going**: a grunt that is being hit a lot gives ground; grunts whose pack is winning press in.
3. Grunts stay **fair and readable**: every new move has a telegraph (Jump_Start crouch, pacing rhythm) and a punish window (vault has no i-frames).
4. **No new global systems.** Everything lives in a `GruntBrain`, a couple of new states, and small additive changes to the director. ScriptableObjects stay read-only; runtime state stays on the brain and the director's runtime entries.
5. **Cheap.** 25 grunts in a scene. No per-frame allocations, no raycasts outside action boundaries, decisions on a ~0.1–0.2 s cadence, not every frame.

---

## 3. Architecture

### 3.1 New `GruntBrain : MeleeChaserBrain`

Mirror of how `HyenaBrain` layers dodge/counter-dash over the base loop. The base loop (passive → chase → one melee → evaluate) is untouched; the grunt overrides `EvaluateNextAction`, `TickBrain`, `IsDecisionLocked`, registers extra states, and owns the new tunables.

File: `Assets/Game/Scripts/New Enemies/Grunt Enemy/GruntBrain.cs`.

Why a subclass and not more fields on `MeleeChaserBrain`: the base is the reusable "dumb melee chaser" that Hyena and future enemies build on. Group tactics are a grunt identity. If Hyena later wants the vault, the state and the director query are reusable; only the policy is grunt-owned.

Prefab change: replace the `MeleeChaserBrain` component on `Grunt Enemy.prefab` with `GruntBrain`. All serialized fields are inherited, so **Copy Component → Add GruntBrain → Paste Component Values** keeps every value and the hitbox reference. The `GruntEnemy.cs` `RequireComponent` changes to `GruntBrain`. The EditMode test `GruntPrefabUsesReusableMeleeChaserBrain` is updated to assert `GruntBrain` (which still *is* a `MeleeChaserBrain`).

### 3.2 New states (`Scripts/New Enemies/States/`)

| State | Purpose | Interrupts | Presenter |
| --- | --- | --- | --- |
| `HoldPositionState` (replaces `WaitForOpeningState` for the grunt) | Lives at an assigned slot but *breathes*: shuffles within ±`paceAmplitude` of the slot, pauses, feints a half-step toward the player then back. Follows the slot as the player moves. | Hit-stun, parry, death (as today). | `Idle` when still, `Run` at `timeScale = speed / chaseSpeed` while shuffling. |
| `LeapState` | Kinematic arc from current position to a **brain-supplied destination**. Used for the vault to the far side and for the pressure fall-back behind the line. Telegraph: holds `Jump_Start` for `leapTelegraph` seconds before leaving the ground. No i-frames by default (`CanTakeDamage = true`), `CanBeInterrupted = false` while airborne (it is kinematic; a knockback mid-arc would fight the lerp). | Death only. A hit during the telegraph cancels the leap (punish window). | `Jump_Start` (telegraph) → `Float` (air) → `Jump_End` (land). |
| `BackOffState` | Short grounded retreat along the axis, facing the player, to a hold distance, then a pause. For the *mild* pressure response of the front grunt. | As `HoldPositionState`. | `Run` reversed? No — the skeleton flips by scale; use `Run` at low time scale and keep facing the player (looks like a wary backpedal). Ask the animator for a `Backstep` clip; see §7. |

`WaitForOpeningState` stays as-is for Hyena.

`LeapState` is deliberately *not* `DodgeState` with options. `DodgeState` picks its own direction from facing and config; `LeapState` takes an explicit world destination and a duration, and reports completion through a new `ILeaper` capability. Same arc maths, shared via a small static helper if it keeps both files short.

### 3.3 Additive director queries (`EnemyEngagementDirector`)

Read-only queries; no change to how slots or the lease work:

```csharp
public readonly struct EnemyLaneSnapshot
{
    public int Side;            // +1 / -1 along the lane axis
    public int QueueIndex;      // 0 = front
    public int CountThisSide;
    public int CountFarSide;
    public bool FarFrontOccupied;
    public float GroupPressure; // 0..1, see 3.4
}
public static bool TryGetLaneSnapshot(EnemyCharacter enemy, out EnemyLaneSnapshot snapshot);
public static Vector3 GetFrontSlot(EnemyCharacter enemy, int side);      // where the front slot on that side is right now
public static Vector3 GetBackSlot(EnemyCharacter enemy, int side);       // one slot behind the last grunt on that side
```

Plus one small runtime flag per entry, set by the brain: `SetYielding(enemy, bool)`. A yielding entry sorts to the back of its side so a grunt that has decided to fall back is not still counted as front while it is mid-leap (otherwise the second grunt would not step up until the leap lands).

One behavioral change inside the director, behind a per-group toggle: **when the group has grunts on both sides, the lease prefers alternating sides** (current rule only alternates attackers). This is what makes the pincer feel like a pincer: left hit, right hit, left hit.

### 3.4 Pressure (how "being hit a lot" is measured)

Two counters, both sliding windows, both allocation-free (ring buffer of timestamps, size 8):

- **Per-grunt pressure** — on the brain. Subscribes to its own `Damageable.OnDamageResolved`; counts hits with `result.WasApplied` in the last `pressureWindow` (default 2.5 s). `Pressure01 = hits / pressureHitsToMax` (default 3). Decays naturally as timestamps age out.
- **Group pressure** — on the director's `Group`. Incremented by any grunt in the group reporting an applied hit (`ReportHitTaken(enemy)`), same window. Exposed in the lane snapshot. This is the "pack confidence" signal: high group pressure makes queued grunts hang back and widen their pacing; zero group pressure for a few seconds (the player is passive or whiffing) makes them press in.

Only successful hits count, as the damage contract requires. Blocked, invulnerable, and dodged requests do nothing.

### 3.5 Presenter additions

`EnemyAnimationPresenter` gains three semantic calls, implemented in `EnemySpineAnimationController` and no-ops in the Mecanim presenter:

```csharp
void PlayLeapTelegraph(float duration);   // Jump_Start, fitted
void PlayLeapAir();                      // Float, looping
void PlayLeapLand(float duration);       // Jump_End, then back to Idle
void SetLocomotionSpeed01(float t);      // picks Idle / Run and scales Run's time scale for shuffling
```

`HandleStateChanged` maps `HoldPositionState` → idle (the state then drives `SetLocomotionSpeed01` while moving), `BackOffState` → run at reduced scale, `LeapState` → nothing (the state drives its three phases). New serialized clip names: `leapStart = "Jump_Start"`, `leapAir = "Float"`, `leapLand = "Jump_End"`, `walk` already exists (currently `"Run"`).

---

## 4. Behavior spec

### 4.1 Vault to the open side (your headline ask)

Trigger, evaluated by the queued grunt on its decision tick (every `decisionInterval` ≈ 0.15 s, jittered per instance):

```
I am QueueIndex == 1 on my side                      (strictly "the second grunt")
AND FarFrontOccupied == false                        (there is an open slot over there)
AND axisDistance(me, player) <= vaultMaxStartDistance (default 4.5: "close enough")
AND player is grounded and not mid-roll              (don't vault into a roll; cheap check via PlayerState)
AND far landing point is valid:
      no wall between my position and landing point (raycast, wallLayer, same as DodgeState)
      ground under landing point                     (raycast down ≤ 1.5)
      landing point not inside another enemy's body  (overlap check against Enemies layer, small sphere)
AND Time.time >= nextVaultAllowed                    (per-grunt cooldown, default 3 s)
AND group vault cooldown elapsed                     (director, default 1 s: never two grunts airborne at once)
AND Random.value <= vaultChance                      (default 0.85; a little hesitation reads as human)
```

Landing point = `GetFrontSlot(me, -mySide)` pushed out by `vaultOvershoot` (default 0.3) so it lands just outside attack range and takes one step in, instead of landing on the player's toes. Arc height `vaultHeight` (default 1.6, must clear the front grunt's capsule, height 2.29 scaled) and duration from `vaultSpeed` (default 9 u/s, so a 4.8 u vault takes ~0.55 s). Telegraph 0.2 s of `Jump_Start` before leaving the ground.

On landing the director re-sorts by position; the grunt is now front on the far side with no extra code. Its `EvaluateNextAction(true)` then does what the base loop already does: ask for the lease, attack or hold.

The "third and fourth grunt stay idle" rule falls out of the trigger: once the second grunt lands, `FarFrontOccupied` is true, so the new index-1 grunt will not vault. Expose `vaultMaxFarSideCount` (default 0) in case you later want 4 grunts to split 2/2.

Failure handling: if the telegraph is interrupted by a hit, the grunt drops back to `HoldPositionState` and pays the cooldown. If the player moves such that the landing point ends up inside attack range of the player, fine; if it ends up on top of another enemy (player side-swapped during the arc), the landing overlap pushes it by physics once the rigidbody is restored. Acceptable for v1.

### 4.2 Living queue: hold, pace, feint

`HoldPositionState` replaces the frozen wait for every grunt that has a slot but not the lease (front without lease, and all queued grunts).

- **Follow the slot.** Every decision tick, re-read `TryGetChaseDestination`; if more than `holdTolerance` (0.35) from the slot, `MoveTo(slot, holdSpeed)` where `holdSpeed` = 60 % of chase speed. This already fixes the grunts being left behind when the player walks away (today they only re-chase after a full re-evaluate).
- **Pace.** While within tolerance, run a small per-grunt rhythm: pick an offset in `[-paceAmplitude, +paceAmplitude]` along the axis (default 0.4), move there at `paceSpeed` (2.0), pause `Random(0.4, 1.2)` s, repeat. Offsets are biased *away* from the player when group pressure is high and *toward* when it is zero. Each grunt seeds its own random from `GetInstanceID()` so neighbours are out of phase.
- **Feint.** Front grunt only, while it has no lease: with `feintChance` (0.25 per pause) step `feintDistance` (0.6) toward the player over 0.15 s with `Jump_Start`'s first frames as the crouch (or `Run` at high time scale), then back. The player sees "he's coming" and may waste a parry. Never feint while the active attacker is in its hit window (director exposes `IsAttackActive`), so feints never visually coincide with a real hit.
- **Stay out of the way.** Queued grunts never pace closer than their slot minus 0.1 (so they never bump the grunt in front) and the pace offset is clamped by the wall raycast cached from the last chase.

Slot overlap check: the base director slot spacing is `halfWidth + halfWidth + 0.2 ≈ 1.0` for grunts (capsule radius 0.40). With amplitude 0.4 two neighbours pacing in opposite phase can touch. Set the director `QueueGap` to 0.5 for groups that contain pacing grunts (make it a per-brain `queueGap` fed to the director via a new entry field, default stays 0.2 for Hyena).

### 4.3 Pressure response

Per-grunt `Pressure01` from §3.4 drives two tiers for the **front** grunt:

| Pressure | Response |
| --- | --- |
| ≥ `backOffPressure` (0.66, i.e. 2 hits in 2.5 s) and not stunned | `BackOffState`: retreat to `attackDistance + backOffDistance` (1.2) at `backOffSpeed` (3.5), hold `backOffHold` (0.6–1.0 s), release the lease if held, then re-evaluate. During the hold the grunt is still counted as front (it is still closest), so nobody rushes into the gap; it just *breathes*. |
| = 1.0 (3+ hits) **and** a queued grunt exists on my side | `LeapState` backward to `GetBackSlot(me, mySide)`: the grunt hops out of the line, `SetYielding(true)` so the next grunt is promoted immediately, lands at the back, `SetYielding(false)`. Cooldown `fallBackCooldown` (6 s) so one grunt cannot yo-yo forever. If no wall/ground-valid back slot exists it uses the back-off tier instead. |

Hitstun interplay: `StunnedState` still fires per hit (1 s stagger). The pressure check runs in `HandleStunCompleted` (already routes to `EvaluateNextAction(true)`), so the back-off begins the moment the stagger ends, which reads as "he got rocked, then scrambled back".

Group pressure effects (queued grunts):

- `GroupPressure ≥ 0.5`: pace bias away, hold slot +0.3 further, feints off. The pack is wary.
- `GroupPressure == 0` for `boldAfterQuietSeconds` (3 s) while the player is in range: hold slot −0.2 (crowd in), feints on, and the director's `AttackHandoffDelay` for this group drops from 0.25 to 0.1 so attacks come faster. The pack smells blood.

### 4.4 Small things that sell "alive" (cheap, high impact)

1. **Per-instance variance.** On `Awake`, multiply chase speed by `Random(0.9, 1.1)`, pace rhythm by `Random(0.8, 1.25)`, and offset the decision tick phase. Grunts stop moving as one body. (Runtime copies on the brain; the `ChaseBehavior` asset values stay untouched.)
2. **Decision jitter.** `decisionInterval = 0.15 s ± 0.05`. Also amortises the director sort across frames for 25 grunts.
3. **Ally-down flinch.** Subscribe to `EnemyCharacter.Died` of group mates through the director (`GroupMemberDied` event). Grunts within 4 units pause pacing for `Random(0.3, 0.6)` s and play `Hurt` at 0.5 time scale, or just freeze. One of them, chosen by lowest instance id, instead gets a 1.5 s `enraged` flag: chase speed ×1.25, wind-up ×0.8. Variety without a new state.
4. **Parry respect.** On `ParryHandler.ParrySucceeded(attacker)`, the director marks the group `recentlyParried` for 1.2 s. Queued grunts' feints stop and the next lease is delayed by 0.4 s. The player feels the parry land on the *group*.
5. **Combo escape.** If a grunt is hit 4 times within one hit-stun chain (`StunnedState.ResetTimer` is being spammed), on stun completion it gets priority for the backward `LeapState` even if it is not front. Stops infinite corner juggling of a single grunt.
6. **Attack commit check.** At the end of wind-up, if the player is now farther than `attackRange + 0.5`, the grunt has a `whiffAnywayChance` (0.3) to swing at air (punishable) and otherwise cancels into `HoldPositionState`. Today it always swings.
7. **Barks.** Add `vault` and `backOff` entries to `EnemySoundProfile`, played from the states through `EnemyAudioHandler`. Even a reused `attackNotify` grunt on vault telegraph makes it readable.
8. **Idle head-turn while holding.** Not an AI change. If the animator can give an `Idle_Alert` clip, use it for `HoldPositionState` instead of `Idle`.

What I looked at in other games (from memory, no fetched sources): Streets of Rage 4 and the Arkham games use the same one-attacker-at-a-time token the director already implements, and sell the crowd with constant small repositioning plus side-alternation; Halo's "flinch and retreat when hurt" is the pressure model; DOOM 2016 scales attack cadence with how passive the player has been (the bold tier); Dead Cells makes every enemy move readable with a crouch telegraph, which is why the vault has one.

---

## 5. Implementation phases

Each phase compiles and plays on its own. Stop after any phase and the game is still better than today.

### Phase 1 — Scaffolding and the living queue (no new moves yet)
- `GruntBrain` (empty override set), prefab component swap, `GruntEnemy.RequireComponent`, test update.
- `HoldPositionState` with slot-follow and pacing (no feints).
- Presenter `SetLocomotionSpeed01`.
- Director: `TryGetLaneSnapshot`, per-brain `queueGap`.
- Per-instance variance and decision jitter.
- Verify: Rooftop, 3 grunts on one side. They follow the player, shuffle out of phase, never bump.

### Phase 2 — Vault to the open side
- `LeapState` + `ILeaper` capability on `GruntBrain`.
- Director: `GetFrontSlot`, `FarFrontOccupied`, group vault cooldown, side-alternating lease.
- Presenter leap phases.
- Verify: 2 grunts same side → second vaults, lands front on the far side, attacks alternate L/R. 3 grunts → third stays. Near a wall → no vault. Player rolling → no vault. Hit during telegraph → cancelled.

### Phase 3 — Pressure
- Hit windows on brain and director, `BackOffState`, backward leap with `SetYielding`, group bold/wary tiers, handoff delay per group.
- Verify: combo the front grunt → it backs off after 2 hits, hops out after 3 with a queued ally promoted; stand still for 3 s → attacks speed up and feints start.

### Phase 4 — Polish list from §4.4 (pick what you like after playing Phases 1–3)
- Feints, ally-down flinch, parry respect, combo escape, commit check, barks.

---

## 6. Files touched

| File | Change |
| --- | --- |
| `Scripts/New Enemies/Grunt Enemy/GruntBrain.cs` | New. Policy, tunables, pressure tracker, leap/back-off callbacks. |
| `Scripts/New Enemies/Grunt Enemy/GruntEnemy.cs` | `RequireComponent(typeof(GruntBrain))`. |
| `Scripts/New Enemies/States/HoldPositionState.cs` | New. |
| `Scripts/New Enemies/States/LeapState.cs` | New. |
| `Scripts/New Enemies/States/BackOffState.cs` | New. |
| `Scripts/New Enemies/EnemyCapabilities.cs` | `ILeaper` (destination, duration, height, telegraph, `OnLeapComplete`, `OnLeapCancelled`), `IHoldPositioner` (pace params). |
| `Scripts/New Enemies/EnemyEngagementDirector.cs` | Lane snapshot, slot queries, yielding flag, group pressure, group vault cooldown, side-alternating lease, per-entry queue gap, `GroupMemberDied`. All additive. |
| `Scripts/New Enemies/EnemyAnimationPresenter.cs` + `EnemySpineAnimationController.cs` + `MecanimEnemyAnimationPresenter.cs` | Leap phases and locomotion speed. |
| `Scripts/New Sound System/EnemySoundProfile.cs`, `EnemyAudioHandler.cs` | `vault`, `backOff` entries (Phase 4). |
| `Assets/Game/ENEMIES/Grunt/Grunt Enemy.prefab` | Component swap (Editor), new clip names on the Spine presenter. |
| `Assets/Game/Tests/Editor/EnemyArchitectureTests.cs` | Update the grunt brain assertion. |
| New `Assets/Game/Tests/Editor/GruntTacticsTests.cs` | Director snapshot and vault-eligibility logic tested with fake positions (pure functions extracted so no Play Mode needed). |
| `ARCHITECTURE_HANDOFF.md` | New section 15: grunt group tactics, director additions, what the states own. |
| `PROGRAMMER_AND_DESIGNER_GUIDE.md` | Tuning table for the grunt tunables. |

Not touched: `MeleeChaserBrain` (except making `IsAtAssignedDestination`, `IsTargetInMeleeRange`, `TryAcquireAttackPermission` and the behaviour fields `protected`, most already are), `Damageable`, `EnemyCharacter`, `EnemyMovement`, `WaitForOpeningState`, `DodgeState`, Hyena, Brute, scenes.

---

## 7. Open questions and asks

1. **Animation clips.** The skeleton has no Roll, Walk, or Backstep. v1 uses `Jump_Start/Float/Jump_End` for the vault and `Run` at reduced time scale for pacing. If you want the literal roll, the animator needs a `Roll` (≈0.5 s, travels ~4 u) and ideally `Walk` and `Backstep`. The presenter fields are strings, so swapping clips later needs no code.
2. **Vault over the player or only over the ally?** Spec above vaults over both (ally then player) and lands beyond the player. Alternative: vault over the ally only and then *run through* the player (bodies already don't collide). Cheaper arc, less spectacular, and the player can hit it as it runs past. I recommend the full vault; it is the move you described and it is the one that reads as intent.
3. **Service Floor doors.** Pursuit radius is 30 there and grunts chase through doors. The vault's wall raycast uses `wallLayer`; door `Blocker` colliders are on whatever layer the door prefab uses. Need to confirm they are included or the vault can land in a closed door.
4. **Strict "only the second grunt" vs. balanced split.** Spec defaults to strict (your words). `vaultMaxFarSideCount = 1` gives 2/2 splits with four grunts, which is more dangerous. Your call after playing.
5. **Should the vault have i-frames?** Default no: you can hit it mid-air and it falls back to a stun on landing. That is the counterplay. If it gets abused, `leapHasIFrames` is one bool on `ILeaper`.
6. **Tests to run after each phase:** `EnemyArchitectureTests`, `DamagePipelineTests`, the new `GruntTacticsTests`, plus `Tools > JunkLite > Systems > Validate Enemies` on Rooftop and Service Floor.

---

## 8. Phase 5 — presence, solid bodies, and the launch (implemented 2026-10-23, Play Mode verification pending)

Implementation notes and the verification list are in `ARCHITECTURE_HANDOFF.md` (top section). Package A was built with a sensor layer instead of a body layer, see 8.2; everything else landed as specified, with these deltas: the alert uses the Idle clip (no Hurt), the pack alert goes through `Perception.Alert`, there are no barks yet, and the player grab controller needed no change (it is the player being grabbed, not grabbing).

Written after the second playtest of Phases 1–3. The in-combat queue is in; what is left is everything *around* the fight: grunts stand dead before contact, detection is a light switch, the player can shove a grunt, grunts run in place against bodies they cannot pass, and a finisher only pushes instead of launching. Five packages. A, B and E are the "solid and fun" fixes; C and D are the "alive" features. E is independent of the others.

### 8.1 Why it feels like this (verified in code)

| Symptom | Cause |
| --- | --- |
| Grunts stand still until contact | `patrolWhenPassive` off and `patrol.patrolDistance` 0 on the prefab. `PatrolState` works but is bare: no pauses, no turn beat, `wallLayer` 0 (never reverses at walls), plays `Run` (no Walk clip). |
| Detection is instant, from any angle | `EnemyPerception` is a sphere trigger (4.12 × 1.26 scale ≈ 5.2 u). No facing, no reaction time, no alert phase. Chase starts at full speed on the same frame. |
| Player shoves a grunt, with a short delay | Player ↔ Enemies collide in the layer matrix. `EnemyCharacter.TryIgnorePlayerBodyCollision` is the only thing preventing contact; it runs at enemy `Start` (player usually not spawned yet, `GameManager` spawns it after scene load) and on target acquire. Until detection the grunt is a 1 kg dynamic body against a 1 kg dynamic body; contact resolution nudges it each physics step and `EnemyMovement` zeroes the velocity the next step. Unity also drops `IgnoreCollision` pairs when either collider is disabled or deactivated. |
| Blocked grunt keeps running in place | Enemies ↔ Enemies collide (by design, safety net). `ILaneSpacing` only sees *registered* lane-mates (in combat, same side, same height), so an idle, undetected, stunned or downed grunt, and every wall, is invisible to it. The Spine presenter plays `Run` on `ChaseState` entry and `SetLocomotionSpeed01` is fed the *commanded* speed, never the measured velocity. |
| Finisher pushes instead of launching | `HitReactionRequest` only knows knockback + hitstun. Sword step 3 is `knockback (18, 1)`: a hard shove. `EnemyMovement.ApplyKnockback` decays horizontal over `knockbackDuration` 0.2 s and `StunnedState` ends when knockback ends, so even a big Y only hops the enemy. No airborne or downed state exists. |

### 8.2 Package A — solid bodies by layer, not by runtime calls

Built the other way round from the first draft: the enemy **sensors** moved layer, not the bodies. Moving bodies would have meant touching every mask that targets enemy bodies (player weapon mask, five mod assets, three scene masks, two code sites); moving the triggers touches nothing that queries by mask, because triggers are never the target of a query.

- New layer 18 `EnemySensor`. Enemy trigger volumes (`EnemyPerception` spheres, enemy-owned `Hitbox` objects) live on it. `EnemySensorLayer.Apply` enforces the layer from their `Awake`, so a prefab or scene override cannot blind an enemy; the prefab layers only mirror it.
- Matrix: `EnemySensor` collides with Player and Damageable only. Enemies to Player is off. Enemy bodies stay on `Enemies`, so every existing mask keeps working. **Enemy bodies stay solid against each other**: two grunts can never overlap; only the player passes through them, which was already the case after detection. Package B is what stops them shoving each other; this collision is the safety net under it.
- `TryIgnorePlayerBodyCollision` and its two call sites are gone, with the per-spawn collider scan.
- Grunt, Hyena and Brute prefabs: sensor objects on layer 18 where the text edit could match them; the rest is covered by the runtime enforcement until the prefabs are saved from the Editor.
- Verify: walk into an idle grunt before it detects you → no push. Roll through a grunt mid-fight → no push. Hits, parry, doors opening for enemies, the burn overlay and corpse landing are unchanged.

### 8.3 Package B — stop running into things

1. **Measured locomotion.** `EnemySpineAnimationController` keeps a tiny velocity sampler: `axisSpeed = |dot(Movement.Velocity, MovementAxis)|`, smoothed over ~0.1 s. While the state is `ChaseState`, `PatrolState`, `HoldPositionState` or `BackOffState`, the clip is `Idle` when `axisSpeed < 0.3` and `walk`/`run` otherwise, with time scale `axisSpeed / ChaseSpeed` (clamped by `minLocomotionTimeScale`). `SetLocomotionSpeed01` calls from states become hints that the measured value overrides when the two disagree by more than 0.3. The Mecanim presenter already drives `Speed` from `CurrentSpeed`; switch it to the measured value the same way.
2. **Body-ahead probe in spacing.** `GruntBrain.ApplySpacing` adds a probe when the director reports no leader or the leader is further than `probeDistance` (1.5 u): one `Physics.SphereCast` along the lane from chest height, radius = capsule radius, length = `probeDistance`, mask = `EnemyBody | Wall | Default`, run at the decision cadence (0.15 s) and cached between ticks. A hit becomes a virtual leader at the hit distance with zero velocity, so the existing `LaneSpacingSolver` brakes the grunt behind an idle, stunned or downed ally, or a wall, instead of shoving. No per-frame raycasts.
3. **Blocked signal.** If commanded speed > 1 but measured speed < 0.3 for `blockedAfter` (0.25 s), the brain sets `IsBlocked`. A blocked `ChaseState` calls `EvaluateNextAction` early; a blocked `HoldPositionState` switches to a pause. A blocked grunt also pokes the idle grunt in front with `EnemyPerception.RefreshOverlap()`, which wakes it if the player is already inside its sphere (the usual case).
- Verify: three grunts in a corridor, only the front one awake → the two behind brake, show Idle, and the middle one wakes when poked. A grunt against the lane-end wall shows Idle, not Run.

### 8.4 Package C — patrol and presence (pre-combat)

- Turn `patrolWhenPassive` on in the prefab with `patrolDistance` 0 (no patrol by default); scene instances set a distance per placed grunt, so guards stay put.
- `PatrolState` gets a rhythm from a new `IPatrolRhythm` on the brain (grunt only; the Robot keeps the plain loop): walk at `patrolSpeedFraction` of chase speed (0.35), pause `patrolEndPause` (random 0.8–2.0 s) at each end, and with `patrolLookBackChance` (0.3 per leg) stop mid-leg, turn around for 0.6 s, turn back and continue. The per-instance `rhythmScale` from Phase 1 desyncs neighbours.
- `wallLayer` on the prefab = Default | Wall | EnemyBody, so a patrol reverses at walls and at another grunt instead of pushing it.
- Idle guards get `idleFidget`: every 3–6 s a 50 % chance to flip facing for 1–2 s, then flip back. No new clip needed.
- Presentation: `PatrolState` uses the measured locomotion from B at a `walk` time scale of about 0.35. A real `Walk` clip from the animator replaces that with one string change.
- Verify: Rooftop with two patrol routes set up; they walk, pause, look back, reverse at walls and at each other, and never leave the lane.

### 8.5 Package D — noticing instead of detecting

- `EnemyPerception` gains an optional facing cone: `frontRadius` (the sphere radius, as today) and `rearRadius` (default 2.0). A player behind the grunt (sign of the axis distance against `Movement.FacingDirection`) is only a candidate inside `rearRadius`. Being hit always acquires the target. The sphere stays the broad phase and the cone is a check inside `IsValidTarget`, so it costs nothing new.
- `AlertState` (new, grunt only): on first sight the grunt stops, faces the player, plays `Hurt` at 0.6 time scale (or an `Alert` clip when one exists) for `alertDuration` (0.3–0.5 s) with an `alert` bark, then the normal `OnTargetChanged` path runs (combat, director, chase). No i-frames; a hit during the alert skips straight to combat. If the player was already attacking it, skip the alert.
- Pack alert: entering `AlertState` calls `EnemyEngagementDirector.AlertNearby(this, alertRadius 6)`; each idle grunt in range gets `RefreshOverlap()` after `placesAway × 0.12 s`, so a line wakes as a ripple. Grunts outside the player's sphere stay idle but turn to face the alerting grunt.
- Losing the player: on `ClearTarget` the brain enters `SearchState` (new): walk to the last known position at hold speed, look both ways (two 0.5 s facing flips), then `ReturnToPassive`. Patrols resume from where they are (`PatrolBehavior.Initialize` re-anchors spawn to the current position).
- Verify: approach a grunt from behind → nothing until 2 u, then a visible beat, then chase. Approach a line from the front → the first reacts, the others wake one by one. Run past 12 u → the grunt walks to where it lost you, looks around, goes back to patrol.

### 8.6 Package E — launch, juggle, knockdown (the third combo hit)

The 2D-brawler finisher: the third hit lifts the grunt off the ground, it goes horizontal in the air, comes down, bounces once, lies there for a beat, gets up. Hits while it is in the air keep it up (juggle, capped). Still one damage path: the producer only *asks* for a reaction kind, `Damageable` is untouched, and `EnemyCharacter` reacts to the applied result.

**Contract (producer side).**
- `HitReactionRequest.Kind` : `HitReactionKind { Flinch, Launch }` (default Flinch, so every existing call site is unchanged). For `Launch`, `KnockbackForce` is the launch velocity (x along the lane away from the source, y up).
- `MeleeComboStep.reactionKind` (default Flinch). Sword `Attack_3_Dash` → Launch, `knockback (5, 9)`. Fist step 3 → Launch, `(4, 8)`. `Attack_Air` keeps Flinch and gets a `juggleLift` of 5 (see juggle below). `WeaponDamageResolver` passes the kind through; `WeaponAttackExecutor` uses `LaunchHitstopDuration` (0.12 s, a new setting next to `EnemyHitHitstopDuration`) on a launch.

**Receiver side (`EnemyCharacter`, `EnemyMovement`).**
- `canBeLaunched` per enemy (grunt and hyena true, brute false). A Launch request on an enemy that cannot be launched degrades to the normal flinch with the same knockback.
- `EnemyMovement.Launch(Vector3 velocity, float gravityScale)`: sets velocity directly, marks `IsAirborne` until the grounded check flips back on, applies a launch-only `gravityScale` (2.0, so a (5, 9) launch peaks about 2 u and lands in about 0.9 s instead of floating for 1.8 s), no horizontal decay in the air. `ApplyKnockback` while airborne is ignored; juggles go through `Launch`.
- `EnemyCharacter.ReceiveDamage` after an applied result: `Kind == Launch && canBeLaunched` → `ApplyLaunch` → `LaunchedState`. A hit while `LaunchedState` is airborne and `juggleCount < maxJuggleHits` (3): `Launch((hit.x × 0.6, max(hit.y, juggleLift)))`, `juggleCount++`. Beyond the cap, hits only add the horizontal push and the enemy falls. `ApplyHitstun` returns early while airborne or downed so `StunnedState` can never replace the launch; status crowd control (`ApplyStatusEffectSnapshot`) is deferred until `GetUp`.

**`LaunchedState` (new, `States/LaunchedState.cs`, reusable; registered by `GruntBrain`, later Hyena).**

| Phase | Gameplay | Presentation |
| --- | --- | --- |
| Air | `CanTakeDamage` true, `CanBeInterrupted` false (hitstun must not take over). Release the attack lease, `SetYielding(true)`, director excludes airborne bodies like leaping ones (`IsLeaping` → `IsAirborne`). Face away from the source. | `PlayLaunched()`: `Hurt` frozen on its last frame (or `Float`), plus `SetBodyTilt(-75°)`: the presenter rotates the Spine GameObject's local Z toward horizontal over 0.12 s, feet trailing. |
| Bounce | First ground contact with downward speed ≥ `bounceMinSpeed` (3): velocity = (x × 0.5, −y × `bounceRestitution` 0.35), `bounce` sound and a small dust VFX, 0.03 s hit-stop through the existing enemy hit-stop path. One bounce only. | Keep the tilt. |
| Downed | Second contact (or the first if too slow): stop and lie for `downedDuration` (random 0.5–0.9 s). Hits still land (flinch only, no re-launch, `downedHitExtend` +0.2 s each, capped at 1.5 s total so there is no infinite ground loop). Still excluded from lanes. | `PlayDowned()`: `Hurt` last frame, `SetBodyTilt(-90°)`. |
| GetUp | `getUpDuration` 0.3 s, `CanBeInterrupted` true (a hit here is a normal flinch and cancels the get-up: punish window, same as the vault telegraph). Then `SetYielding(false)` and `EvaluateNextAction(true)`; the Phase 3 pressure response usually makes it back off, which reads as "he got rocked". | `PlayGetUp()`: `Jump_End` fitted to the duration, tilt back to 0. |

- Death during a launch: `HandleDeath` already keeps physics on and `DeathSequence` waits for the ground, so a killing finisher sends the corpse flying and it lands naturally. Keep the tilt on the corpse.
- Walls: a launch into a lane-end wall stops horizontally by physics and falls straight; acceptable. `IsLeapLandingValid` must ignore airborne bodies.
- Grabs: `PlayerGrabController` skips enemies in `LaunchedState`.
- Presenter API additions (`EnemyAnimationPresenter`, virtual no-ops; Spine implements, Mecanim ignores or maps to triggers): `PlayLaunched()`, `PlayDowned()`, `PlayGetUp(float duration)`, `SetBodyTilt(float degrees, float blend)`.
- Verify: sword combo 1-2-3 on a lone grunt → it flies about 2 u up and 3 u back, flips horizontal, bounces once with dust, lies down, gets up, then backs off. Jump and hit it in the air → it stays up, three times at most. Hit it on the ground → flinch, no re-launch. Finisher on the front grunt of a line → the second grunt steps up while it is down. Finisher kill → corpse flies. Brute → normal shove.

### 8.7 Order, files, asks

Order: A → B → C → D, with E independent and runnable in parallel with any of them. Each package compiles and plays on its own.

| File | A | B | C | D | E |
| --- | --- | --- | --- | --- | --- |
| `ProjectSettings/TagManager.asset`, `DynamicsManager.asset` | layer + matrix | | | | |
| `ENEMIES/Grunt/Grunt Enemy.prefab` (+ Hyena, Brute) | body layer | | patrol on, wallLayer | perception radii | `canBeLaunched` |
| `New Enemies/EnemyCharacter.cs` | remove IgnoreCollision | | | | `ApplyLaunch`, juggle, hitstun guard |
| `New Enemies/Robot Enemy/EnemyMovement.cs` | | | | | `Launch`, `IsAirborne`, launch gravity |
| `New Enemies/EnemyPerception.cs` | | | | cone, rear radius | |
| `New Enemies/EnemySpineAnimationController.cs` + `EnemyAnimationPresenter.cs` + Mecanim | | measured locomotion | | | launched / downed / get-up, tilt |
| `New Enemies/Grunt Enemy/GruntBrain.cs` | | probe, blocked flag | patrol rhythm, fidget | alert, search, pack alert | register state, decision lock |
| `New Enemies/States/PatrolState.cs` | | | rhythm | | |
| `New Enemies/States/AlertState.cs`, `SearchState.cs` | | | | new | |
| `New Enemies/States/LaunchedState.cs` | | | | | new |
| `New Enemies/EnemyEngagementDirector.cs` | | | | `AlertNearby` | airborne exclusion |
| `Character/DamageContracts.cs` | | | | | `HitReactionKind` |
| `Weapons/MeleeWeaponData.cs`, `WeaponDamageResolver.cs`, `WeaponAttackExecutor.cs`, `Sword.asset`, `Fist.asset` | | | | | reaction kind, launch hit-stop |
| `Player/PlayerGrabController.cs` | | | | | skip airborne |
| `Tests/Editor/` | | spacing probe case | | | `LaunchReactionTests` (kind plumbing, degrade when `canBeLaunched` is false, no stun while airborne) |

Asks for the animator, none blocking: `Walk` (C), `Alert` (D), `Knockdown` / `GetUp` (E). Until then: `Run` at low time scale, `Hurt` at 0.6, and `Hurt` last frame + body tilt / `Jump_End`.

Open decisions:
1. **A** touches `ProjectSettings` and three enemy prefabs. Do it with the Editor open and confirm the free layer slot first.
2. **E** launch numbers above are starting points; tune `gravityScale`, restitution and `downedDuration` in Play Mode. Decide whether the hyena launches (it already dodges and counter-dashes; a launch may be too much on top).
3. **E** juggle cap 3 and no re-launch from the ground are the anti-infinite rules. For longer juggles raise the cap, not the rules.

---

## 9. Phase 6 plan: per-type hit reactions and pass-through bodies (implemented 2026-10-24, Play Mode verification pending)

Answers: grunts pass through each other but must never settle on the same spot or attack together; only grunts launch for now, behind a per-enemy toggle; the Brute was left as it is. Two deliberate deviations from the plan below: the settings stay on each prefab's `EnemyCharacter` (no new ScriptableObject, so no hand-made `.meta`; every grunt shares one prefab anyway), and enemy-to-enemy collision is switched off per enemy through `Collider.excludeLayers` (a `Pass Through Enemies` toggle) rather than in the layer matrix, so types without spacing logic keep colliding until someone turns it on. Details in `ARCHITECTURE_HANDOFF.md`, top section.

Two asks from playtesting: (1) launching, spiking and knockback must be tunable per enemy type, and some types must never fly; (2) an enemy must never be stuck behind a dummy or any other enemy that is not moving: it walks through it.

### 9.1 What is true today (verified in code)

| Area | Today | Problem |
| --- | --- | --- |
| Launch config | `canBeLaunched` + `EnemyLaunchSettings` are inline fields on each prefab's `EnemyCharacter`. `LaunchedState` is registered only by `GruntBrain` and `HyenaBrain`. | Tuning is per prefab, not per type; the Hyena is launchable with the grunt's tuning by accident; nothing reports a "launchable" enemy whose brain cannot launch. |
| Dummy | `Dummy.prefab` is a real enemy: `DummyEnemy` + `PassiveEnemyBrain`, root on `Enemies`, dynamic Rigidbody + capsule. It never joins the engagement director. | The grunt's body-ahead probe (mask includes `Enemies`) treats it as a lane leader and brakes behind it forever, and the Enemies-to-Enemies physics collision blocks the body anyway. |
| Enemy-to-enemy collision | On in the layer matrix. Grunts treat it as a safety net under the spacing solver. Hyena, Robot and Brute have no spacing solver and rely on it to not stack. | Anything not moving (dummy, idle or stunned enemy, a type without spacing) is a wall. |
| Grunt patrol | `wallLayer` includes `Enemies`. | A patrol turns around at a dummy. |
| Drawing | Every grunt's Spine body uses sorting order 42. | Once bodies can overlap, two grunts in the same spot can flicker. |

### 9.2 Part 1: `EnemyHitReactionProfile` (one asset per enemy type)

- New read-only ScriptableObject in `Scripts/New Enemies/`: `canBeLaunched`, the full `EnemyLaunchSettings` (apex, gravity, hang, bounce, spike, juggle, downed), `canBeSpiked`, `knockbackMultiplier` (0 = cannot be pushed), `hitstunMultiplier`. No runtime state on it.
- `EnemyCharacter` gains `reactionProfile`. When it is empty, the current inline fields are used exactly as today, so no prefab or scene changes behaviour until a profile is assigned.
- `LaunchedState` registration moves into one helper on `EnemyBrain` (`RegisterReactionStates`), called by every brain, so "launchable" depends only on the profile, not on which brain happened to register the state.
- Downgrades stay as they are: a Launch on a non-launchable enemy is a flinch, and a Spike on a grounded or non-spikeable enemy is a sideways flinch.
- Assets: Grunt (today's tuned values), Hyena (not launchable by default), Robot, Brute, Dummy (not launchable).
- Editor: `Tools > JunkLite > Enemies > Create Reaction Profiles From Prefabs` copies the current inline values into assets and assigns them, with Undo. `Validate Enemies` reports a launchable profile on an enemy without a ground body (flying, kinematic).
- Tests: empty profile matches the inline behaviour, a non-launchable profile turns Launch into Flinch, Spike on the ground pushes sideways only, `knockbackMultiplier` 0 means no push.

### 9.3 Part 2: pass-through bodies, with spacing kept by behaviour

Decision: enemy bodies stop colliding with each other completely, and "do not overlap" becomes purely a behaviour rule among enemies that are actually fighting together. A selective rule (pass through dummies only) would still leave grunts stuck behind an idle, stunned or downed enemy, and it would need per-pair collision bookkeeping.

1. **Layer matrix:** turn `Enemies` with `Enemies` off. Walls, floors, doors and the player are unchanged; sensors and hitboxes are on `EnemySensor` and are unaffected.
2. **Grunt probe:** walls and door blockers only. Enemy bodies count as leaders only through the director, which already limits them to members of the same fight, on the same side and height, and not airborne. Dummies, idle or unaware enemies, other types, stunned, downed and launched bodies are walked through.
3. **Grunt patrol:** drop `Enemies` from `wallLayer`; patrols turn at walls and ledges only (question 1 below).
4. **No stacking among fighting grunts:** unchanged in principle. Director slots plus the spacing solver already never command a move into a lane-mate, and `HoldPositionState` steps out of an overlap after knockback. Add a check that two lane-mates overlapping for more than 0.5 s triggers the follower's step back, since physics no longer pushes them apart.
5. **Other types (Hyena, Robot, Brute):** they lose the physical push that kept them apart. Move the spacing capability (`ILaneSpacing` with the director neighbours) from `GruntBrain` into `MeleeChaserBrain` so the Hyena gets it too. Robot and Brute queue on director slots and rarely crowd; accept and watch in playtest.
6. **Drawing:** give each enemy's Spine renderer a stable tiny depth offset by instance (a few thousandths of a unit along the camera axis), so overlapping bodies always draw in the same order and never flicker.
7. **Clean-up after verification:** `SetEnemyBodyPassThrough` (launch) and `IgnoreEnemyBodyCollisionsOnDeath` become redundant; keep them until Play Mode confirms, then remove.
8. **Stays the same:** the leap landing check (a query, not a collision), `SlidingDoor` detection, weapon hit detection, the knockback rules.

### 9.4 Risks and how to check them

| Risk | Check |
| --- | --- |
| Grunts stack after knockback or a vault | Hit the front grunt of a 3-grunt line repeatedly; they must settle apart within half a second. |
| Hyena or Robot stack when crowding | Spawn three of a kind on one side; slots keep them apart; note any overlap. |
| Bodies flicker when overlapping | Walk a grunt through a dummy at the default camera; no flicker. |
| Something relied on enemies blocking each other (encounter funnels, doors) | Play Service Floor and Rooftop encounters end to end. |
| Wrong profile on a prefab | `Validate Enemies` plus the new tests. |

### 9.5 Order

Part 1 first (data only, no behaviour change until profiles are assigned), then Part 2 steps 1 to 3 (fixes the dummy block), then 4 to 6, then the playtest list, then step 7.

### 9.6 Questions before implementing

1. Patrols: pass through other grunts (proposed), or keep turning around when they meet one?
2. Which types may fly: Grunt yes; Hyena, Robot, Brute, Dummy proposed no. The dummy could fly for practice if you want.
3. Should the Brute be pushable at all (`knockbackMultiplier`), or immovable?
