# JunkLite Architecture Refactor Handoff

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
