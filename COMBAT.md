# Combat

How player combat works in JunkLite: every move, which script owns it, and where to tune it.
For deep rules and history see `ARCHITECTURE_HANDOFF.md`; for feel ideas see `COMBAT_FEEL_PLAN.md`; for enemies see `ENEMIES.md`.

Scripts are under `Assets/Game/Scripts/`. Tuning lives on the **`Player_Cass` prefab** (`Prefabs/PLAYER/Player_Cass.prefab`) and the **weapon assets** (`Scripts/Weapons/Fist (Cass).asset`, `Scripts/Weapons/Sword (Cass).asset`).

## Map

```
Input (GameInputManager)
  ├─ Attack ──► PlayerCharacter.HandleAttackInput ──► WeaponManager.Attack
  │                 WeaponManager ─► ComboState (which step)
  │                               ─► SpineAnimationController (clip, wind-up hold, squash & stretch)
  │                               ─► WeaponAttackMotion (lunge, float, recoil) ◄─ PlayerCombatFocus / LungeWarp
  │                               ─► WeaponHitResolver ─► WeaponDamageResolver ─► DamageRequest
  │                                                                             └► enemy Damageable ─► DamageResult
  │                               ─► FeedbackManager (hitstop, shake, rumble)  ◄─ only on an applied result
  │                               ─► ModManager (charges, on-hit passives)
  ├─ Dash ────► PlayerCharacter.PerformDash ──► Character2D5Controller.Dash ─► PlayerState.SetDashing (i-frames)
  │                                                                          ─► SkeletonGhost trail, PlayerDashStrike
  ├─ Parry ───► ParryHandler.BeginParry
  ├─ Special ─► PlayerPush.TryPush
  └─ Mod 1–4 ─► ModManager ─► ModExecutionRunner ─► ActiveModData.ExecuteAbility

Incoming hit ─► PlayerCharacter.ReceiveDamage: parry ► i-frames/dash ► shield ► Damageable
```

## Moves

| Move | Input | Script | Links to | What it does |
|---|---|---|---|---|
| **Ground combo** | Attack (LMB / X) | `Weapons/WeaponManager.cs` | `ComboState.cs`, `MeleeWeaponData.cs` (`sideCombo`), `Player/SpineAnimationController.cs` | 3-hit chain. Each step = one `MeleeComboStep`: clip, damage, reach, lunge, wind-up, hit delay, knockback, feel. Fists: `Attack_Punch_Combo_1–3`. Mantis Blades: `Attack_Punch_Mantis_1–3`. |
| **Air combo** | Attack in air | `WeaponManager.cs` | `airSideCombo`, `WeaponAttackMotion.cs` (air float) | 3-hit air chain (`Attack_Air_Combo_1–3`). She floats briefly while swinging (Air Attack Float, Air Hit Hang). |
| **Up attack** | Up + Attack | `WeaponManager.cs` | `upAttack` step, `Up Attack` header | Launcher. Own hitstop (`upLaunchHitstop`). |
| **Down attack / pogo** | Down + Attack in air | `WeaponManager.cs` | `downAttack` step, `Mods/PogoModData.cs`, `PlayerState.MarkAirAttackUsed` | Downward strike; a hit bounces her up. Air attacks are counted, and a pogo refund comes back after a double jump. |
| **Wind-up hold** | (part of a swing) | `Player/SpineAnimationController.cs` | `MeleeComboStep.windupHold` / `windupAt` / `windupIgnoreClipEvents` / `windupReleaseSpeed`, `WeaponAttackExecutor.cs` (`AttackHoldTiming`) | Pauses the clip mid-swing, then releases. The hit window moves with the pause. See **Wind-up** below. |
| **Squash & stretch** | (auto on wind-up) | `Player/SpineAnimationController.cs` (region *Wind-up Squash & Stretch*) | DOTween | Squishes the rig on the pause, stretches it on release, then springs back. See **Squash & stretch** below. |
| **Combat focus (soft lock)** | (auto on side swings) | `Player/PlayerCombatFocus.cs` | `CombatFocusSelector.cs`, `FocusMarker.cs` (+ `Focus Marker.prefab`), `Weapons/LungeWarp.cs` | Picks the enemy on the side she swings toward and warps the lunge to reach it. Never turns her around. |
| **Dash** | Dash | `Character/Character2D5Controller.cs` (`Dash`) | `Player/PlayerCharacter.cs` (`PerformDash`, `HandleDashStarted/Ended`), `PlayerState.SetDashing`, Spine `SkeletonGhost` | Fast burst along the lane (0.2 s, cooldown 1 s, air dash on). **I-frames for the whole dash:** `SetDashing(true)` → `SetVulnerable(false)`, so every normal and unparryable hit is rejected as `Invulnerable`. It also dodges the Brute grab. Only `DamageRequest.Forced` (kill zones, fall recovery, grab throw damage, dev tools) goes through. `SkeletonGhost` draws the after-image trail; a dash burst particle and trail play too. Can cancel an attack's recovery (buffered). |
| **Dash Strike** (upgrade) | Dash | `Player/PlayerDashStrike.cs` | `WeaponDamageResolver.cs`, `WeaponManager` (hit report), `PlayerCharacter.ApplyUpgrades` | While dashing, enemies she passes through take one hit each. Hits charge mods like weapon hits. Enabled when the upgrade is owned. |
| **Parry** | Parry | `Player/ParryHandler.cs` | `PlayerCharacter.ReceiveDamage`, `SpineAnimationController` (`Perry_1` / `Perry_2`), `New Enemies/AttackTellView.cs` | Opens a 0.25 s window. A hit inside it is rejected as `Parried`. Then: hit clip, hitstop, sparks, shake, rumble, slow motion, nearby enemies pushed and stunned (attacker full, others half). Red, shaking tells are **unparryable** (`DamageRequest.AsUnparryable`, e.g. the Brute grab): dash those instead. |
| **Push** | Special | `Player/PlayerPush.cs` | Enemy knockback API (same as parry push) | Shoves enemies in front away. No damage. |
| **Hurt i-frames** | (on taking a hit) | `Player/PlayerCharacter.cs` (`ReceiveDamage`) | `PlayerState.ApplyInvulnerability` | 0.5 s invulnerable after a real hit (not damage over time). |
| **Shield** | (mod/upgrade) | `PlayerCharacter.ReceiveDamage` | `damageShield` component | Absorbs damage before health. |

## Weapons

| Piece | Script / asset | Notes |
|---|---|---|
| Current weapon | `Weapons/PlayerArmament.cs` | One weapon at a time: fists by default, Mantis Blades after `WeaponUpgradePickup`. Permanent, saved in `PlayerUpgrades`. UI listens to `WeaponChanged`. |
| Weapon config | `Weapons/WeaponData.cs`, `Weapons/MeleeWeaponData.cs` | Read-only assets. `sideCombo`, `airSideCombo`, `upAttack`, `downAttack`, plus reach and lunge defaults. |
| Fists | `Weapons/Fist (Cass).asset` | Clip speed `fistAttackTimeScale` 1.62 (on the Spine controller). |
| Mantis Blades | `Weapons/Sword (Cass).asset` | Clip speed `weaponAttackTimeScale` 1.22. Steps 1–3: see the Mantis table under **Wind-up**. Normal-speed release, clip Hold events ignored. |
| Combat rules | `Weapons/WeaponManager.cs` | Input buffering, combo windows, cancel windows, hit window, hitstop, air hang, focus lunge, damage requests. |
| Combo counter | `Weapons/ComboState.cs` | Plain C#: step index, combo reset, cooldowns. |
| Movement in attacks | `Weapons/WeaponAttackMotion.cs` | Lunge, vertical impulse, air float, recoil, hover, all through the controller (never the Rigidbody). |
| Hit detection | `Weapons/WeaponHitResolver.cs` | Overlap queries, classifies what was hit. |
| Damage build | `Weapons/WeaponDamageResolver.cs` | One `DamageRequest` per receiver; dedupes enemies with several colliders. |

## Wind-up

Owner: `SpineAnimationController.cs` (`SetAttackWindup`, `UpdateAttackHold`, `BeginAttackHold`, `ReleaseAttackHold`). `WeaponManager.Attack` passes each step's values in.

Per step in the weapon asset (Inspector: **Side Combo → Element N**):

| Field | Meaning |
|---|---|
| `Windup Hold` | Seconds the clip pauses. 0 = no step wind-up (clip Hold events use `attackHoldDuration`). |
| `Windup At` | Clip seconds where it pauses (used when the clip has no Hold event, or always with the next field). |
| `Windup Ignore Clip Events` | Pause at `Windup At` even if the clip has Spine `Hold`/`Release` events. |
| `Windup Release Speed` | Speed after the pause. 0 = `attackReleaseSpeed` (1.6), 1 = normal. |

Mantis Blades now (`Sword (Cass).asset`, clip speed 1.22, so clip seconds = real seconds × 1.22):

| Step | Pause | Pause at (clip s) | Hit delay (plain, real s) | Hit lands (real s from swing start) |
|---|---|---|---|---|
| Attack 1 | 0.18 s | 0.10 | 0.12 (clip 0.146) | 0.30, just after the pause |
| Attack 2 | 0.10 s | 0.10 | 0.19 (clip 0.232) | 0.29, after the pause |
| Attack 3 | 0.20 s | 0.15 | 0.13 (clip 0.159) | 0.33, just after the pause |

Rule: keep `hitDelay × clip speed` a little past `Windup At`, or the hit lands before the pause and the freeze reads as hitstop.
Fists keep their own wind-ups (0.09 / 0.09 / 0.14 s at their clip Hold events or `Windup At`).

On the `Player_Cass` Spine Animation Controller: `attackHoldDuration` (0.12, default pause at clip Hold events) and `attackReleaseSpeed` (1.6).
Attack speed upgrades shorten the pause. `hitDelay` stays measured on the plain clip; `AttackHoldTiming.WarpDelay` moves it with the pause, so a hit set after the pause lands after it.

## Squash & stretch

Owner: `SpineAnimationController.cs`, region **Wind-up Squash & Stretch** (`PlaySquash`, `ApplySquash`, `SettleSquashIfIdle`, `ResetSquash`). Uses DOTween.

- **Pause starts** (`BeginAttackHold`): rig eases to `holdSquashScale` (1.08 wide, 0.92 tall) over `holdSquashTime` (0.06 s).
- **Release** (`UpdateAttackHold` timer end or the clip's `Release` event): snaps to `releaseStretchScale` (0.93, 1.08) over `releaseStretchTime` (0.05 s), then springs back to 1 over `stretchSettleTime` (0.12 s, OutBack).
- **Interrupted mid-pause** (hurt, dash, death): eases back to 1. Disabling the controller resets it.
- Scales the shown skeleton's transform as a delta (keeps facing flips and the left/right rig swap). Scales from the rig pivot (feet). A rig on the root object is skipped so the collider never scales.
- Tune on `Player_Cass` → Spine Animation Controller → **Wind-up Squash & Stretch**. Untick `Windup Squash` to turn it off. Applies to every paused attack (fists and blades).
- Shape changes inside a swing (limb squash) belong in Spine; this tween is whole-body game feel.

## Damage

One path only (`Character/DamageContracts.cs`, `Character/Damageable.cs`):

1. A producer (weapon, dash strike, mod, hazard) builds a `DamageRequest`.
2. The receiver's `Damageable` validates it, applies armor, changes health through `AttributesManager`, and returns a `DamageResult`.
3. Only an **applied** result triggers hit VFX, hitstop, recoil, mod charges and lifesteal.

Player defenses, in order (`PlayerCharacter.ReceiveDamage`):

1. Parry: rejected as `Parried`, unless the request is unparryable.
2. Can't take damage (dash, hurt i-frames, immunity lease) or invincible: rejected as `Invulnerable`.
3. Shield absorbs.
4. Simulation levels: she never drops below 1 HP.
5. `Damageable` applies the hit, then the hurt reaction plays and 0.5 s of i-frames start.

`DamageRequest.Forced` skips steps 1–3.

## Feedback

| What | Where |
|---|---|
| Hitstop, camera shake, rumble, FOV kick | `Managers/FeedbackManager.cs`, called by `WeaponManager` per hit. Per-step weight: `MeleeComboStep.feel` (0 = WeaponManager default). |
| Attack clips, blends, hurt and parry clips | `Player/SpineAnimationController.cs` (see `SpineAnimationController.md`) |
| Dash trail | Spine `SkeletonGhost` on each skeleton (`PlayerCharacter.SkeletonGhostActivation`) |
| Enemy attack tells (parryable / unparryable) | `New Enemies/AttackTellView.cs` |

## Mods

| Piece | Script |
|---|---|
| Slots and activation | `Mods/ModManager.cs` (Mod 1–4 inputs) |
| Runtime state (charges, cooldown, durability) | `Mods/ModInstance.cs`, one per slot. Mod assets are read-only. |
| Long abilities (owner, cancel, cleanup) | `Mods/ModExecutionRunner.cs` |
| Base types | `Mods/ModData.cs`, `PassiveModData.cs` (always on, on-hit hooks), `ActiveModData.cs` (charges from hits, then activate) |
| Passives | `FireModData.cs`, `ElectricModData.cs` (chain zap), `LifestealMod.cs` (heal from applied damage), `PogoModData.cs` |
| Actives | `Active Mods/EnergyWaveMod.cs` (+ `EnergyWavePulse.cs`), `PhantomStrikeMod.cs` (aerial slam, charges reset when hurt), `DontBlinkMod.cs` (blink behind the enemy), `SocialDistanceMod.cs` (radial push) |
| Pickups | `Mods/WorldModPickup.cs` |

## State and locks

- `Player/PlayerState.cs`: grounded, jumping, dashing, attacking, parrying, stunned, wall sliding, and the input and damage-immunity **leases**. Never set locks directly: acquire a lease and dispose it.
- `PlayerCharacter.CanInteract`: no interacting while airborne, attacking, dashing, parrying or stunned.
- `Managers/PlayerCombatTracker.cs`: counts enemies engaged with her. "In combat" blocks pickups and boxes, but not room-exit doors.
- Status effects (stun, slow…): `New Status Effect/StatusEffectHandler.cs`, `StatusEffectDefinition.cs`, `StatusEffectInstance.cs`.
- Grab / throw by enemies: `Player/PlayerGrabController.cs`. Mash jump to break free. A dash or roll dodges it.
- Upgrades reach combat in one place: `PlayerCharacter.ApplyUpgrades` (stats from `PlayerRuntimeStats.Evaluate` go to the controller, `WeaponManager` and `PlayerDashStrike`).

## Tests and tools

- EditMode: `Assets/Game/Tests/Editor/` (`DamagePipelineTests`, `PlayerAnimationRulesTests`, …).
- **Tools > JunkLite > Systems > Validate / Validate Enemies / Validate Encounters**.
- Dev keys (Editor only): H heal, T hurt, Y instant death. Pause menu developer tools can kill enemies.
