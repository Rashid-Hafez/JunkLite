# Spine Enemy Damage Flash Setup

Use this as the source of truth when setting up new Spine-based enemies.

## Required Material

- Use `Assets/Game/Shaders/Game_URP_Unlit_DamageFlash.mat` on the enemy Spine renderer material slot.
- Do **not** use old/default Spine materials if you expect hit flash to work.

## Required Components

On the enemy root:

- `GruntEnemy` / `HyenaEnemy` / other `EnemyCharacter` subclass
- `Damageable`
- `DamageFlashUniversal`

On the Spine visual object:

- `SkeletonAnimation` (or Spine renderer used by that enemy)

## DamageFlashUniversal Settings (Spine Enemies)

- `isSpine = true`
- `flashAmount` and `flashDuration` tuned per enemy
- `normalAmount` should match the material's normal (non-hit) state value

## Wiring Notes

- In the enemy script component (`EnemyCharacter` subclass), `damageFlashUniversal` should reference the Spine `DamageFlashUniversal` component.
- If the field is left empty, `EnemyCharacter.Awake()` will auto-find the first `DamageFlashUniversal` in children.
- Avoid adding multiple `DamageFlashUniversal` components unless you are sure which one is referenced.

## Quick Verification Checklist

1. Enemy takes damage and does not disappear.
2. Hit flash appears for `flashDuration`.
3. Material returns to normal value after flash.
4. `DamagePopupManager` still shows damage numbers.

## Common Failure Causes

- Wrong material assigned (most common): not using `Game_URP_Unlit_DamageFlash.mat`
- `isSpine` left disabled on a Spine enemy
- Serialized reference points to the wrong `DamageFlashUniversal` when duplicates exist
