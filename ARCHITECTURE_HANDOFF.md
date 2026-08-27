# JunkLite Architecture Refactor Handoff

Last reviewed: 2026-08-27

Reviewed branch: `4.7`

Baseline code commit: `46f3c34e` (`changes to player, enemies and base services`)

The current working tree contains the player/enemy separation, result-based damage pipeline, `WeaponManager` damage migration, focused mod-runtime cleanup, the explicit `V2.5` infrastructure migration, the focused player/UI lifecycle extractions, the player weapon-loadout extraction, and the composed enemy-AI migration described below.

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
| Game root / level context | Migration implemented and validator passing; Play Mode verification pending | `V2.5` contains the reusable root and standalone level context without removing authored environment content. |
| Scene-local camera binding | Implemented, Play Mode verification pending | Core and trigger cameras use one cached registry and rebind to every spawned/respawned player. |
| Restart/loading transition | Timing fix implemented, Play Mode verification pending | Loading video and async scene loading now overlap; the obsolete serialized six-second activation delay is removed. |
| Player lifecycle extraction | Implemented, Play Mode verification pending | `PlayerLifecycle` owns player creation, identity, spawn selection, death, and soft respawn. Direct consumers no longer use player APIs on `GameManager`. |
| Game UI lifecycle extraction | Implemented, Play Mode verification pending | `GameUIManager` owns HUD, pause, game-over, loading UI, prefab configuration, instances, and state-driven visibility. |
| Player weapon loadout | Implemented, Play Mode verification pending | `PlayerWeaponLoadout` owns both equipped slots, paired world pickups, attachment, visibility, swap/drop/replace, and break removal. |
| Composed enemy AI foundation | Implemented for all current first-party enemy prefabs, latest migrations need Play Mode verification | Grunt/Hyena are user-verified. Robot, Flying Dummy, Patrol Dummy, and Dummy now use the same composition boundary without forcing every archetype through the same brain. |
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
- Animation-presentation cleanup and encounter/wave architecture were not migrated yet.

### 14. Planned encounter and wave architecture — approved, not implemented

The next implementation should extract one concrete responsibility from level-specific code:

> The sequence manager decides when a fight happens. The encounter decides which enemies belong to that fight and when the fight is finished.

This plan is intentionally not a universal mission, quest, or sequencing framework. The current code already demonstrates the need for the smaller boundary:

- `Level0SequenceManager` owns enemy configuration, instantiation, living counts, death subscriptions, tutorial reactions, and progression waits.
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

#### Implementation phase 1 — encounter foundation

Add focused first-party scripts under `Assets/Game/Scripts/Encounters/`:

1. `EncounterTypes.cs` containing the state/source enums and serializable `EncounterWave` / `EncounterEnemyEntry` data.
2. `EncounterController.cs` containing the runtime lifecycle, sequential-wave coroutine, set-based participant tracking, exact-once completion, cancellation, and validation.
3. `EncounterArchitectureTests.cs` and an editor validation entry for configuration and lifecycle contracts.

Do not add a singleton, factory abstraction, pooling interface, wave asset, mission graph, or generic action framework in this phase. Continue using `Instantiate`; pooling would require a separate enemy reset/activation contract and should be driven by profiling.

#### Implementation phase 2 — migrate the tutorial combat boundary

Give `Level0SequenceManager` one explicit `EncounterController` reference. Its combat portion becomes conceptually:

```text
subscribe to encounter participant/death events
StartEncounter()
wait until the encounter reaches a terminal state
continue only when it completed successfully
unsubscribe
```

The tutorial subscribes to `EnemyRegistered` so it can listen for the first attack notification, and to `EnemyDied` so it can detect the tutorial Hyena and create/activate the mod reward. Those rules remain tutorial-owned and must never move into `EncounterController`.

For the two existing tutorial scenes, retain `enemyPrefabs` and `enemySpawnPoints` only as hidden migration fields. If no native encounter configuration is assigned, the sequence manager obtains/adds a local controller and converts the legacy arrays into one runtime wave. Emit an editor/development warning whenever this bridge is used. Remove the bridge once both scenes have an explicitly authored encounter.

After migration, remove the sequence manager's manual `spawnedEnemies`, `enemiesAlive`, spawn/count/wait methods, and direct death-subscription ownership. The currently unused post-pickup enemy spawning method/fields should be removed only after a serialized-reference audit confirms no authored content expects them. No scene should be bulk-edited as part of the code extraction.

#### Implementation phase 3 — level goal and statistics

`LevelGoal` should gain an explicit required-encounters mode and complete when every assigned required encounter publishes `EncounterCompleted`. It must not reconstruct completion from expected kills or participant health. Keep its old scene-scan Kill-All mode as a temporary compatibility path for levels not yet migrated.

Do not change statistics in the first encounter slice. `EnemyCharacter.HandleDeath` currently reports kills directly to `LevelStatsTracker`; also subscribing the tracker to `EncounterController.EnemyDied` would double-count. Once representative levels use encounters, choose one kill source deliberately. The preferred eventual direction is for a scene-configured stats consumer to observe `EnemyRegistered` and `EnemyDied`, after which the direct enemy-to-stats call can be removed. Until that migration is complete, preserve the existing universal kill path and keep encounter events out of statistics.

Level-goal completion and statistical enemy totals remain separate concepts. A required encounter can complete through an explicit scripted removal without that removal being counted as a kill.

#### Implementation phase 4 — event-driven gates and cameras

Replace `CameraSwitchTrigger`'s manual enemy-health polling only after the encounter foundation is verified. A trigger/gate may reference a specific encounter and lock on `EncounterStarted`, then unlock on `EncounterCompleted`.

Cancellation must not be interpreted as successful completion. V1 gates do not automatically unlock on cancellation; the cancelling level flow decides the appropriate presentation. `PlayerCombatTracker` remains available for gates that intentionally represent global current-combat engagement instead of a bounded encounter, but one gate must have one explicit lock policy rather than combining both concepts implicitly.

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

### Gate 6: Reassess encounter and wave ownership

After the new Robot/Flying/passive checks pass, implement the approved phased plan in section 14. Begin only with the scene-local `EncounterController`, explicit `EncounterEnemyEntry` modes, sequential waves, set-based lifecycle tracking, tests, and the Level 0 combat-boundary migration. Keep enemy lifecycle behind `EnemyCharacter.Died`; do not introduce reset/replay, reusable wave assets, pooling, a global AI event bus, or a universal action graph during the first slice.

### Deferred weapon reassessment

`WeaponManager` is still large because it contains melee, directional blast, hitscan, timing, movement, and feedback behavior. Do not split it based on line count alone. After loadout verification, use the next planned weapon type to identify whether a focused attack-execution strategy or executor would make that addition materially easier. If existing data-driven melee/ranged paths already support it cleanly, leave the manager as-is and move to the next gameplay system.

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

> Read `ARCHITECTURE_HANDOFF.md` completely and inspect the current code before changing anything. JunkLite is single-player. The player/combat/lifecycle/loadout work described here is implemented. The composed enemy architecture is implemented across Grunt, all Hyena variants, Robot, Flying Dummy, Patrol Dummy, and Dummy: `EnemyPerception` reports facts, archetype brains own voluntary transitions, states execute one action, behaviors own reusable mechanics, focused controllers own independent physical presentation, and `EnemyCharacter.Died` isolates Level 0 from enemy FSM details. Grunt/Hyena are user-verified; run `Tools > JunkLite > Systems > Validate Enemies`, run `EnemyArchitectureTests`, and complete the Robot/Flying/passive Play Mode checklist. Section 14 contains the approved but unimplemented encounter/wave plan. Implement it in phases, starting only with the scene-local `EncounterController`, explicit participant source modes, sequential waves, set-based tracking, tests, and the Level 0 combat-boundary migration. Do not add reset/replay, reusable wave assets, pooling, networking architecture, a universal AI/ability graph, a service locator, or a broad manager rewrite in the first slice.

## Synchronization Checklist

Before changing computers:

- Commit and push this handoff and its associated script changes.
- Note the active branch and Unity editor version.

On the other computer:

- Clone or pull branch `4.7` (or the branch containing this document).
- Open the project with Unity 6000.3.22f1.
- Open a new Codex task against the repository.
- Use the continuation prompt above.
