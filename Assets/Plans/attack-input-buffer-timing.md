# Project Overview
- Game Title: JunkLite
- High-Level Concept: 2.5D action combat platformer featuring fast-paced melee/ranged weapon combinations, combo chaining, and mod synergies.
- Players: Single player
- Inspiration / Reference Games: Dead Cells, Hollow Knight, Mega Man Zero
- Tone / Art Direction: 2.5D stylized post-apocalyptic action
- Target Platform: PC (StandaloneOSX / Windows)
- Screen Orientation / Resolution: Landscape 1920x1080
- Render Pipeline: Universal Render Pipeline (URP)

# Game Mechanics
## Core Gameplay Loop
Engage in fluid 2.5D combat by weaving melee and ranged attacks into multi-hit combo strings. Players press attack inputs both during current animations (early buffer) and shortly after swings conclude (post-attack combo window) to cleanly progress their combo strings without dropped inputs or sluggish recovery.

## Controls and Input Methods
- **Light/Fist Attack / Weapon 1 / Weapon 2 Attack**: Pressed to execute the respective attack step.
- **Directional Modifiers**: Up and Down inputs alter attack angle and aerial dive/launch properties.
- **Bi-directional Input Buffering**: 
  - *Early Input Buffer*: Inputs pressed while currently attacking are stored in `WeaponManager` and execute immediately once recovery/cooldown permits.
  - *Post-Attack Combo Window*: Inputs pressed after an attack ends continue the combo string within the centralized combo window.

# UI
No UI layout changes required. Existing `WeaponUI`, `ModCombatUI`, and HUD will continue to function seamlessly without modification.

# Key Asset & Context
- `Assets/Game/Scripts/Player/PlayerState.cs`: Player capability checks (`CanAttack`, `IsInputLocked`).
- `Assets/Game/Scripts/Player/PlayerCharacter.cs`: Attack input routing to `WeaponManager`.
- `Assets/Game/Scripts/Weapons/WeaponData.cs`: ScriptableObject defining weapon archetype properties (removing per-asset `attackCooldown` and `comboWindow`).
- `Assets/Game/Scripts/Weapons/ComboState.cs`: State machine managing combo progression, cooldown timers, and combo reset timers.
- `Assets/Game/Scripts/Weapons/WeaponManager.cs`: Central player combat controller managing active attacks, input buffering, and timing settings.
- `Assets/Game/Scripts/Weapons/WeaponInstance.cs`: Runtime component on weapon GameObjects.

# Implementation Steps

### Step 1: Unblock Attack Input Buffering in `PlayerState`
- **Description**: In `Assets/Game/Scripts/Player/PlayerState.cs`, adjust `CanAttack` so that `IsInputLocked` does not reject attack presses while `IsAttacking` is true. This allows early attack inputs during swings to reach `PlayerCharacter` and be buffered by `WeaponManager`.
- **Assigned role**: developer
- **Dependencies**: None
- **Parallelizable**: Yes

### Step 2: Remove Timing Fields from `WeaponData`
- **Description**: In `Assets/Game/Scripts/Weapons/WeaponData.cs`, remove `attackCooldown`, `comboWindow`, `ComboInputWindow`, and `OnValidate()` checks so timing is no longer decentralized across individual ScriptableObjects.
- **Assigned role**: developer
- **Dependencies**: None
- **Parallelizable**: Yes

### Step 3: Centralize Attack Timing & Buffer Configuration in `WeaponManager`
- **Description**: In `Assets/Game/Scripts/Weapons/WeaponManager.cs`:
  1. Add serialized fields for `attackCooldown`, `comboWindow`, and `bufferDuration` (renamed from `BUFFER_DURATION`) with sensible default values (`attackCooldown = 0.15f`, `comboWindow = 0.6f`, `bufferDuration = 0.35f`).
  2. Pass/sync these timing values to `fistCombat` and weapon slot `CombatState` instances.
  3. Ensure `BufferAttack()` and `Update()` process buffered inputs cleanly as soon as an active attack concludes and the cooldown completes.
- **Assigned role**: developer
- **Dependencies**: Step 2
- **Parallelizable**: No

### Step 4: Refactor `ComboState` and `WeaponInstance`
- **Description**: In `Assets/Game/Scripts/Weapons/ComboState.cs`:
  1. Update `CombatState` to use configurable/hardcoded timing properties (`AttackCooldown`, `ComboWindow`) rather than fetching from `WeaponData`.
  2. In `Assets/Game/Scripts/Weapons/WeaponInstance.cs`, remove legacy warnings and validation comparing `weaponData.comboWindow` to `weaponData.attackCooldown`.
- **Assigned role**: developer
- **Dependencies**: Step 2, Step 3
- **Parallelizable**: No

### Step 5: Test and Validate
- **Description**: Run existing Editor tests (`PlayerWeaponLoadoutTests`, `DamagePipelineTests`) to ensure zero regression, and verify that inputs pressed early during attack animations queue and fire smoothly into the next combo attack.
- **Assigned role**: developer
- **Dependencies**: Steps 1-4
- **Parallelizable**: No

# Verification & Testing
- **Compilation Check**: Verify all scripts compile without warnings or errors regarding missing `WeaponData.attackCooldown` or `WeaponData.comboWindow`.
- **Unit Test Execution**: Run `junklite.Tests.PlayerWeaponLoadoutTests` to confirm weapon equip, drop, and swap behaviors remain intact.
- **Early Input Buffer Test**: Press the attack button during the active animation frames of an attack; verify that the subsequent combo swing executes immediately upon completion without needing to re-press.
- **Post-Attack String Test**: Wait for the swing to complete and press attack within `comboWindow`; verify the combo counter advances and plays the next attack step.
- **Combo Expiration Test**: Wait past `comboWindow` without input; verify the combo resets to step 0.
