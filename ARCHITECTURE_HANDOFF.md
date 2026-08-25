# JunkLite Architecture Refactor Handoff

Last updated: 2026-08-25

## Purpose

This document preserves the architectural analysis and decisions made before the refactor begins. It is intended to let development continue from another computer or a new Codex task without repeating the discovery and planning work.

The long-term objective is to make the project easier to extend, test, and maintain while improving runtime efficiency where it matters. The architecture should remain practical for this game's current size: use clear boundaries and composition, but avoid speculative abstractions, large frameworks, and unnecessary layers.

## Project Context

- Unity 6000 with URP.
- Mirror is used for networking.
- First-party gameplay scripts currently live primarily under `Assets/Game/Scripts/`.
- Refactoring should stay inside first-party scripts and small prefabs or ScriptableObject assets.
- Third-party packages, regular art assets, and broad scene edits are outside the coding scope unless explicitly requested.
- Unity should create `.meta` files when possible.

## Current Status

- Architectural analysis and planning have started.
- No implementation from this refactor plan has been applied yet.
- No scenes, prefabs, ScriptableObjects, or regular assets have been changed.
- No build was run for this planning work.
- The first agreed implementation slice is **Player Separation + Damage Result Foundation**.

## Refactor Principles

1. **Prefer composition for shared capabilities.** Health, damage reception, teams, targeting, status effects, and similar capabilities should not require player and enemies to share one large inheritance hierarchy.
2. **Keep actor-specific orchestration separate.** Player input and player state belong to the player. Enemy AI and enemy state belong to enemies.
3. **Use inheritance only for genuine specialization.** A small `EnemyBase` may provide lifecycle behavior common to all enemies, while concrete enemy types inherit from it only when they truly share that contract.
4. **Create one authoritative damage entry point.** Attackers submit a damage request and receivers return a result. Attackers should not directly modify health or guess whether a hit succeeded.
5. **Preserve working behavior during migration.** Refactor through small vertical slices with temporary adapters when required; do not rewrite every actor, ability, and manager at once.
6. **Keep data separate from runtime behavior.** ScriptableObjects should contain configuration. Runtime components should own mutable state and behavior.
7. **Avoid global-manager growth.** Persistent application flow, per-level configuration, player spawning, UI creation, audio, and encounter rules should not accumulate indefinitely in one `GameManager`.
8. **Optimize measured hotspots.** Architectural clarity comes first. Avoid per-frame reflection, repeated scene searches, unnecessary allocations, and uncached component lookups, but do not introduce pooling or complex infrastructure without an actual need.

## Main Architectural Finding

The player and enemies can share capabilities without sharing a character base class.

The current hierarchy makes `PlayerCharacter` inherit from `CharacterBase`, while `CharacterBase` also coordinates attributes, damage, character state, animation, death, activation, and deactivation. That inheritance implies that player and enemy actors share one behavioral model, but they have different sources of intent and different state machines:

- The player is driven by input, player movement, equipment, abilities, camera feedback, and respawn flow.
- Enemies are driven by AI, perception, navigation, encounter ownership, and enemy-specific decisions.

Separate player and enemy finite-state machines are therefore appropriate. The problem is not that there are two FSMs; the problem is forcing both actor categories through a base class that also owns unrelated capabilities.

The agreed direction is:

- `PlayerCharacter` becomes an independent player-only orchestrator.
- `PlayerState` remains player-specific.
- Enemies receive a small enemy-specific base class only when implementation reaches the enemy slice.
- Enemy FSMs remain separate from the player FSM.
- Player and enemies share contracts and components such as damage receiving, attributes, teams, and status handling.
- `CharacterBase` is retired gradually after remaining consumers are migrated; it is not deleted during the first slice.

## Relevant Current Scripts

- `Assets/Game/Scripts/Base Classes/CharacterBase.cs`
  - Currently requires and binds `AttributeManager` and `Damageable`.
  - Implements `IDamageable` and owns shared death/activation helpers.
- `Assets/Game/Scripts/Player/PlayerCharacter.cs`
  - Currently inherits `CharacterBase`.
  - Coordinates input, movement, player state, damage responses, feedback, grabbing, death, activation, and respawn behavior.
- `Assets/Game/Scripts/Character/Damageable.cs`
  - Currently defines `IDamageable`, `DamageInfo`, `DamageType`, `IGrabbable`, and `GrabInfo` as well as the `Damageable` component.
  - Returns only `bool`, which loses the reason a hit was accepted or rejected.
  - Currently combines validation, hostility checks, mitigation, health mutation, event emission, and hit-stun.
- `Assets/Game/Scripts/Character/AttributesManager.cs`
  - Owns runtime attributes and health mutation.
  - Must remain the authoritative health owner during the first migration.
- `Assets/Game/Scripts/Managers/GameManager.cs`
  - Currently contains persistent game state, scene loading, player lifecycle, spawn discovery, respawning, several UI lifecycles, pause handling, music changes, and scene-specific decisions.
  - This is a later refactor area, not part of the first implementation slice.

## First Implementation Slice

### Name

**Player Separation + Damage Result Foundation**

### Goal

Detach the player from the general character inheritance hierarchy and establish a result-based damage boundary, while preserving the current player prefab and gameplay behavior.

This is deliberately a narrow vertical slice. It proves the new boundary on the player before migrating all enemies or redesigning every combat feature.

### Step 1: Introduce damage contracts

Create small, neutral combat types, preferably in a focused combat folder/namespace within the existing first-party script root:

- `DamageRequest`
  - Requested/base amount.
  - Source or instigator.
  - Damage type.
  - Knockback data.
  - Tick/damage-over-time flag.
  - Only add hit point, direction, ability identifier, or network metadata when a current consumer actually needs it.
- `DamageOutcome`
  - A small enum representing meaningful outcomes such as `Applied`, `Blocked`, `Parried`, `Invulnerable`, `FriendlyFire`, `Dead`, and `Invalid`.
  - Keep the set aligned with current behavior; do not add speculative states.
- `DamageResult`
  - Outcome.
  - Requested damage.
  - Final applied damage.
  - Convenience property such as `WasApplied`.
- `IDamageReceiver`
  - Exposes whether the receiver is alive.
  - Accepts a `DamageRequest` and returns a `DamageResult`.

The names may be adjusted to match established conventions, but the request/result separation is the required architectural boundary.

### Step 2: Evolve the existing `Damageable` component

Keep `Damageable` as a neutral receiver/calculator during the first slice rather than replacing it with many services.

Responsibilities for this stage:

- Reject invalid, self, dead, or friendly hits.
- Obtain the receiver's current defensive state through explicit dependencies.
- Apply current mitigation rules.
- Ask `AttributeManager` to mutate health.
- Return the actual outcome and applied amount.
- Raise one post-application event carrying the result/context.

Avoid turning it into a universal combat engine. Player presentation, audio, animation, camera shake, AI reactions, loot, score, and encounter progression should react to results through their actor-specific components.

Preserve serialized fields and component references wherever possible so the current prefab does not require a destructive rebuild.

### Step 3: Detach `PlayerCharacter` from `CharacterBase`

Change `PlayerCharacter` into a player-only `MonoBehaviour` orchestrator that implements only the interfaces it actually needs, including the new damage receiver contract and the existing grab contract if still appropriate.

Move or reproduce only the small amount of functionality the player genuinely needs:

- Cache and initialize `AttributeManager`.
- Bind/configure `Damageable`.
- Expose player health/alive state through player-facing accessors.
- Subscribe and unsubscribe from death once.
- Preserve healing, forced-death, activate/deactivate, respawn, and current public API used by managers or UI.

Do not create a new `PlayerBase` solely to replace `CharacterBase`. There is currently only one player actor category, so another inheritance layer would add structure without value.

### Step 4: Preserve the current defensive resolution order

Before modifying damage behavior, trace the existing player hit flow and keep its observable order. The intended ordering is:

1. Validate request, source, target, and alive state.
2. Resolve team/friendly-fire rules.
3. Resolve parry or explicit block behavior.
4. Resolve invulnerability/state immunity.
5. Resolve shields or temporary defensive resources, if currently present.
6. Apply armor/resistance mitigation.
7. Apply health damage.
8. Emit the result.
9. Trigger actor-specific reaction, knockback, hit-stun, VFX, audio, camera feedback, and death reactions as appropriate.

The exact shield/parry ordering must follow current intended gameplay. If current code is inconsistent, document the conflict before changing player-facing behavior.

### Step 5: Add a compatibility bridge

Do not migrate every enemy in the same change.

- Leave enemy classes using `CharacterBase` temporarily.
- Keep the old `IDamageable`/`DamageInfo` entry point as a short-lived adapter if too many call sites depend on it.
- Route the adapter into the new request/result pipeline so there is still one authoritative resolution path.
- Mark the adapter for removal only after all first-party damage producers and receivers have migrated.

Avoid maintaining two independent damage implementations.

### Step 6: Migrate active player damage call sites

Update first-party systems that hit the player so they consume `DamageResult` rather than treating a collision or a `bool` as proof of damage.

Important rule:

- Hit confirmation, on-hit effects, life steal, status application, combat tracking, hit-stop, and similar consequences should occur only for the appropriate returned outcome.
- A blocked, parried, invulnerable, friendly, or dead-target hit must not accidentally trigger applied-hit effects.

Migrate only the call sites necessary to complete and verify the player slice. Inventory all remaining legacy call sites for a later migration.

### Step 7: Stabilize damage-critical attribute behavior

Inspect and correct only issues in `AttributeManager` and related attribute code that directly affect damage correctness, including:

- Initialization being safe and idempotent.
- Health not being initialized twice through competing lifecycle paths.
- Death firing once per life.
- Revive resetting the death guard and restoring valid health.
- Damage events reporting actual applied values.
- No negative health or invalid maximum/current values unless explicitly intended.

Do not redesign the entire statistics system in this slice.

## Target Dependency Shape

```text
Damage producer (weapon / hazard / ability / enemy attack)
                  |
                  v
          IDamageReceiver.Receive(request)
                  |
                  v
              Damageable
        validation + mitigation
                  |
                  v
           AttributeManager
          authoritative health
                  |
                  v
             DamageResult
                  |
       +----------+-----------+
       |          |           |
       v          v           v
 player feedback  FSM      combat tracking
 / animation      reaction  / ability effects
```

Neither the damage producer nor `GameManager` should directly subtract health.

## Explicit Non-Goals for the First Slice

Do not include these in the first implementation unless required to keep the project compiling:

- Full enemy hierarchy migration.
- A universal actor/character framework.
- Replacing both FSMs with a shared FSM framework.
- Ability-system redesign.
- Status-effect framework redesign.
- Complete stats/equipment/mod redesign.
- `GameManager` or level-flow decomposition.
- Scene rewrites.
- Network authority redesign.
- Object pooling or ECS conversion.
- Broad namespace/folder renaming.
- Third-party code changes.

## First-Slice Acceptance Criteria

The change is complete only when:

- `PlayerCharacter` no longer inherits from `CharacterBase`.
- Player-specific state remains in `PlayerState` and no enemy AI/FSM concern is introduced into it.
- The player accepts damage through the new request/result contract.
- The damage caller can distinguish applied damage from parry, block, immunity, friendly fire, dead target, and invalid input where those outcomes currently exist.
- Health is changed through one authoritative pipeline.
- Existing armor, parry, invulnerability, shield, tick-damage, hit-stun, death, revive, knockback, and feedback behavior is preserved where currently implemented.
- Player activation, deactivation, spawning, respawning, UI binding, and manager-facing APIs continue to work.
- Existing enemy prefabs can remain on the compatibility path without requiring a simultaneous migration.
- No first-party damage producer directly edits target health.
- Event subscriptions do not duplicate across revive/reactivation.
- The project compiles after the major slice is complete.

## Verification Plan

After implementation, perform focused verification:

1. Search all first-party direct health mutations and classify legitimate internal calls versus bypasses.
2. Search all uses of `DamageInfo`, `IDamageable`, and `TakeDamage` and list remaining compatibility consumers.
3. Compile the Unity project because this slice changes core type relationships and interfaces.
4. In Unity, the user should verify the relevant player prefab references and run these gameplay checks:
   - Normal enemy hit.
   - Armor mitigation.
   - Invulnerable hit.
   - Parried/blocked hit.
   - Friendly/self hit rejection.
   - Tick damage without unintended hit-stun.
   - Lethal hit and exactly one death event.
   - Revive followed by receiving damage and dying again.
   - Knockback and feedback only for valid outcomes.
5. Do not edit scenes automatically; report any Inspector or scene wiring the user must perform.

## Planned Order After the First Slice

Reassess after the player slice rather than committing to a large rewrite. The likely order is:

1. **Enemy foundation**
   - Introduce a minimal enemy lifecycle/base boundary.
   - Keep enemy decision logic in enemy FSM/AI components.
   - Migrate one representative enemy to the new damage receiver pipeline before migrating all types.
2. **Damage producers and abilities**
   - Standardize how weapons, hazards, projectiles, and abilities create requests and react to results.
   - Separate ability definition data from runtime execution and cooldown state.
3. **Attributes and status effects**
   - Clarify base stats, runtime stats, modifiers, resources, and temporary effects only after combat boundaries are stable.
4. **Game and level flow**
   - Reduce `GameManager` to persistent application/session concerns.
   - Move per-scene spawn/configuration into a level context or level controller.
   - Give encounter, UI, audio, and respawn flows focused owners communicating through events or explicit references.
5. **Networking pass**
   - Confirm server authority and replication rules across damage, abilities, spawning, death, and respawn once local ownership boundaries are clear.

Each stage should migrate one real vertical slice, verify it, and only then expand to other content.

## Guidance for the Next Codex Task

Open the synchronized repository on the other computer and use this prompt:

> Read `ARCHITECTURE_HANDOFF.md` completely. Inspect the current first-party scripts under `Assets/Game/Scripts/` and compare them with the document because the code may have changed since it was written. Then implement only the first slice, **Player Separation + Damage Result Foundation**, in small compatibility-preserving steps. Do not redesign enemies, abilities, managers, scenes, or networking in the same change. Preserve serialized prefab compatibility, report any Unity Inspector work I must do, and compile after the major type migration.

## Synchronization Checklist

Before changing computers:

- Add this document to version control.
- Commit and push the current branch.
- Ensure any other local project changes needed for the refactor are also intentionally committed or otherwise transferred.

On the other computer:

- Clone or pull the same branch.
- Open the project with the matching Unity version.
- Open a new Codex task against the repository.
- Use the continuation prompt above.

