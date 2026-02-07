# Spine Animation Flow (Weapons → Player → Spine)

This is the short, plain‑English path for how a weapon attack ends up playing a Spine animation.

## Where the animation names live

- Weapon data assets (example: `Assets/Game/01_SCRIPTS/New Crafting System/sword.asset`)
  - These store combo steps and their `animationName` fields.
- The schema is defined in:
  - `Assets/Game/01_SCRIPTS/Weapons/WeaponComboData.cs`
  - `Assets/Game/01_SCRIPTS/Crafting/WeaponData.cs`

Each combo step has an `animationName` string. That is the exact Spine animation name to play.

## Simple flow (weapon → spine)

1. **Weapon data provides animationName**
   - The current weapon’s `WeaponData` returns a `ComboStep` with its `animationName`.

2. **WeaponManager starts the attack**
   - `Assets/Game/01_SCRIPTS/Weapons/WeaponManager.cs`
   - It resolves direction (side / up / down), picks the combo step, and reads the `animationName`.

3. **WeaponManager updates PlayerState**
   - It sets `IsAttacking` and sets attack context (e.g., “down attack requested” when needed).
   - It forwards the animation name to PlayerState.

4. **PlayerState broadcasts the request**
   - `PlayerState` fires `OnAttackAnimationRequested(animationName)`.

5. **SpineAnimationController plays it**
   - `Assets/Game/01_SCRIPTS/Player/SpineAnimationController.cs`
   - It listens to the PlayerState event and calls `PlayAttackAnimation(animationName)`.
   - If the player is in air and not doing a down‑attack, it can swap to `Air_Attack`.

6. **SpineAnimationController finishes the attack**
   - On animation complete/interrupt, it notifies PlayerState.
   - PlayerState then notifies WeaponManager so cooldown/combos continue.

## One‑line summary

**WeaponData gives the animation name → WeaponManager starts attack → PlayerState forwards the name → SpineAnimationController plays it.**
