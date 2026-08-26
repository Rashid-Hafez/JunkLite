# JunkLite Architecture Refactor Handoff

Last reviewed: 2026-08-26

Reviewed branch: `4.7`

Baseline code commit: `46f3c34e` (`changes to player, enemies and base services`)

The current working tree contains the player/enemy separation, result-based damage pipeline, `WeaponManager` migration, focused mod-runtime cleanup, and the explicit `V2.5` infrastructure migration described below.

## Purpose

This document is the source of truth for the architecture refactor. It records what is implemented, what still needs gameplay verification, and the next safe work order so development can continue from another computer or Codex task.

The goal is better modularity and easier feature development without building a framework larger than this game needs.

## Project Reality and Constraints

- JunkLite is a **single-player game**. Do not design combat, abilities, state, or managers around multiplayer authority or replication.
- The project uses Unity 6000 with URP.
- First-party gameplay code currently lives primarily under `Assets/Game/Scripts/`.
- Refactoring should remain inside first-party scripts, small prefabs, and ScriptableObject assets.
- Avoid broad scene changes and third-party modifications. Report required Inspector work to the user.
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
| Weapon damage migration | Implemented, gameplay verification pending | All `WeaponManager` damage paths publish actual applied damage. |
| Ability/mod runtime architecture | Implemented, gameplay verification pending | Per-slot executions, cancellation cleanup, lifecycle separation, and composable player ability locks are in place. |
| Damage/lock EditMode tests | Passing | Unity 6000.3.22f1 passes all 7 focused tests. |
| Game root / level context | Runtime fix and explicit migration implemented; scene run pending | The duplicate-singleton spawn failure is fixed in code. The Unity menu command must still be run to rewrite `V2.5` and rebuild the prefab. |
| Scene-local camera binding | Implemented, Play Mode verification pending | Core and trigger cameras use one cached registry and rebind to every spawned/respawned player. |
| Restart/loading transition | Timing fix implemented, Play Mode verification pending | Loading video and async scene loading now overlap; the obsolete serialized six-second activation delay is removed. |
| Full game/level manager cleanup | Not started | `GameManager` still owns several unrelated responsibilities. |

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

`CharacterBase` has no known first-party inheritors or serialized references. It now uses `IDamageReceiver` so it does not preserve the removed damage API; the file can be deleted in a later cleanup after one final Unity reference check.

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

- `Game Root` is the duplicate-safe persistent root. It owns `GameManager`, `GameInputManager`, `PlayerCombatTracker`, the runtime gameplay canvas, and the event system.
- `Level Context` is a standalone scene-local root. It owns the level identity, whether a player should spawn, and explicit typed player spawn points.

`LevelContext` must not be nested under the persistent prefab. `GameRoot` temporarily detaches contexts found in an older prefab so unmigrated scenes remain usable, while the rebuild command removes that legacy nesting from the prefab entirely.

The `V2.5` spawn failure was traced to a duplicate-singleton race. The scene had a standalone `GameInputManager` while the composed `Game Root` also contained one. The duplicate path called `Destroy(gameObject)`, so depending on `Awake` order it could destroy the complete `Game Root`, including `GameManager`, before player spawning. Duplicate `GameInputManager`, `GameManager`, and `PlayerCombatTracker` instances now disable and remove only their own duplicate component, never the shared host object.

`Assets/Game/Editor/GameRootTrainingLevelMigration.cs` is now manual-only. It no longer uses `[InitializeOnLoad]` or silently edits a scene. The command:

`Tools > JunkLite > V2.5 > Strip Legacy Systems and Rebuild`

does the following in one reviewed operation:

1. Rebuilds `Assets/Game/Prefabs/Manager/Game Root.prefab` without scene-local `LevelContext` data.
2. Removes obsolete player/bootstrap/input/UI infrastructure from `Assets/Game/Scenes/V2.5.unity`.
3. Installs exactly one persistent `Game Root` at the world origin.
4. Creates one standalone `Level Context` and preserves the authored spawn transform.
5. Preserves level geometry, cameras, enemies, pickups, and required supporting services such as audio, combat effects, projectiles, drops, and feedback.
6. Saves and validates the resulting scene.

The scene and prefab have deliberately not been rewritten from outside Unity. Run the command in the open Editor, inspect the hierarchy, then use `Tools > JunkLite > V2.5 > Validate Redesigned Setup`. This is a foundation, not a completed manager refactor: `GameManager` still owns scene loading, player lifecycle, respawn, UI lifecycles, pause state, spawn resolution, music selection, and game-state transitions.

### 9. Scene-local camera ownership and player binding

`CameraManager` remains scene-local because Cinemachine rigs, blends, and trigger cameras belong to a level. It is not part of the persistent `Game Root`.

The original V2.5 camera failure was caused by a split configuration: the scene assigned `mainCamera`, but `ConnectToPlayer` only targeted the optional `cameraList`, which was empty. The main camera was prioritized without ever receiving the spawned player as its `TrackingTarget`.

The corrected flow is:

1. `GameManager` spawns or revives the player and publishes `OnPlayerSpawned` once.
2. The scene-local `CameraManager` subscribes to that event and owns all camera response.
3. Main, spawn, death, and explicitly configured level cameras enter one cached, deduplicated registry.
4. Every registered camera is rebound to the current player on spawn and respawn.
5. A camera selected later by `CameraSwitchTrigger` registers and binds on demand.
6. Respawn prioritizes the configured spawn camera, falling back to main and then the first registered camera.

This uses explicit serialized references and small cached collections rather than repeated scene-wide camera searches. Follow-freeze state is retained when switching cameras, singleton duplicates remove only the duplicate component, and the manager cleans up both player and `GameManager` event subscriptions when disabled.

The V2.5 validation command now requires exactly one scene-local `CameraManager`, exactly one `CinemachineBrain`, and a main camera reference belonging to the scene camera rig.

### 10. Restart and loading-transition timing

The original scene-restart coroutine played the entire loading video before it even called `LoadSceneAsync`, then held the loaded scene behind a serialized `debugLoadDelay` of six seconds. Restart duration was therefore video duration plus scene-loading duration plus an artificial delay, producing a visibly frozen loading frame before the player returned.

`GameManager.LoadLevelWithScreen` now starts the video and asynchronous scene load together. Scene activation waits until the scene is ready and the video is finished, so the two real operations overlap instead of accumulating. The debug delay field and its stale values were removed from both manager prefabs. Input remains disabled until `InitializeForNewScene` has refreshed level references and spawned the player.

## Verification Recorded

On 2026-08-26 with Unity 6000.3.22f1:

- Runtime and editor assemblies compiled successfully after the mod cleanup.
- Runtime and editor assemblies compile with 0 errors after the `V2.5` spawn, camera-binding, and migration-tool changes. The wider project still reports pre-existing analyzer, obsolete-API, and unused-field warnings.
- `DamagePipelineTests` passed 7/7 in EditMode.
- The tests cover requested versus applied damage, rejection outcomes, defensive immunity, death/revive, idempotent attribute initialization, composable input locks, and composable damage-immunity locks.
- A source search found no `IDamageable`, `DamageInfo`, `TakeDamage`, `FromLegacy`, `ToLegacy`, or `OnDamaged` references in first-party gameplay scripts.

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

The broader foundation still needs representative player/enemy/weapon gameplay verification. The explicit `V2.5` migration must be run in Unity and then visually and functionally approved.

## What Should Be Done Next

### Gate 1: Play-test the completed combat/mod slice

Fix only regressions inside the implemented boundaries. Do not add another abstraction layer during verification.

### Gate 2: Harden the game-root migration workflow

1. In Unity, run `Tools > JunkLite > V2.5 > Strip Legacy Systems and Rebuild` and approve the confirmation dialog.
2. Inspect the preserved authored content, enter Play Mode, and verify the player spawns at `Level Context/Player Spawn Point` and the main Cinemachine camera immediately follows it.
3. Run `Tools > JunkLite > V2.5 > Validate Redesigned Setup`.
4. Migrate one additional gameplay scene and one menu/non-gameplay scene with user review.
5. Retire legacy spawn/UI fallbacks only after all scenes use the new workflow.

### Gate 3: Extract one concrete GameManager responsibility

After the scene foundation is verified:

1. First candidate: player spawn/death/respawn lifecycle.
2. Second candidate: gameplay HUD/pause/game-over UI lifecycle.
3. Keep global game state and scene transitions in `GameManager` until those extractions prove stable.

### Gate 4: Review WeaponManager responsibilities

Do not rewrite combat. Extract only a proven boundary, with equipment/loadout/pickup ownership as the first likely candidate. Attack execution/detection can be considered afterward if continued weapon additions remain difficult.

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
- Delete the unused `CharacterBase.cs` after one final serialized-reference check.

## Continuation Prompt for a New Codex Task

> Read `ARCHITECTURE_HANDOFF.md` completely and inspect the current code before changing anything. JunkLite is single-player. Player/enemy separation, the result-based damage pipeline, all first-party damage-producer migrations, the `WeaponManager` result migration, composable player ability locks, explicit mod lifecycle, per-instance mod execution/cancellation, scene-local camera rebinding, concurrent restart loading, and the manual-only `V2.5` infrastructure migration are implemented. Unity 6000.3.22f1 compiles and all 7 focused EditMode tests pass. If `V2.5` has not yet been rewritten, run `Tools > JunkLite > V2.5 > Strip Legacy Systems and Rebuild`, verify player spawning, camera follow, and restart timing, then run the validation command. Do not add networking architecture, a universal ability graph, a service locator, or a broad manager rewrite. After the listed Play Mode checks, extract only one concrete `GameManager` responsibility at a time.

## Synchronization Checklist

Before changing computers:

- Commit and push this handoff and its associated script changes.
- Note the active branch and Unity editor version.

On the other computer:

- Clone or pull branch `4.7` (or the branch containing this document).
- Open the project with Unity 6000.3.22f1.
- Open a new Codex task against the repository.
- Use the continuation prompt above.
