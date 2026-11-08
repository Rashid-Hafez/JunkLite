# Enemies: making and placing them

How to add a new enemy and put it in a level so it works on the first try. Written so a person or an agent can follow it step by step. For the code architecture (brains, capabilities, damage path), see `ARCHITECTURE_HANDOFF.md`. For Spine hit-flash wiring, see `EnemySpineSetup.md`. For lanes and world rotation, see `MAIN_MECHANICS.md`.

## 1. Start from the closest existing prefab

Duplicate a working prefab instead of building one from scratch. Then swap the art and tune the values.

| New enemy is like... | Duplicate | Enemy class / brain |
| --- | --- | --- |
| Basic melee fighter (Spine) | `Assets/Game/ENEMIES/Grunt/Grunt Enemy.prefab` | `GruntEnemy` / `GruntBrain` |
| Fast, dodging melee (Spine) | `Assets/Game/Prefabs/Enemies/Hyena.prefab` | `HyenaEnemy` / `HyenaBrain` |
| Ranged / dasher | `Assets/Game/Prefabs/Enemies/Robot Enemy.prefab` | `RobotBrain` |
| Boss / heavy | `Assets/Game/ENEMIES/Brute/Brute Boss.prefab` | `BruteEnemy` / `BruteBrain` |
| Training target | `Assets/Game/Prefabs/Enemies/Dummy.prefab` (or Patrol / Flying Dummy) | `DummyEnemy` / `PassiveEnemyBrain` |

Each Hyena colour (EASY, Blue, Green) is its own prefab, not a variant. Changing `Hyena.prefab` does **not** change the others. If you make colour or difficulty versions of a new enemy, make them **prefab variants**, so one change reaches all of them.

**Hyena behaviour (`HyenaBrain`):** it dodges a swing only when the swing can reach it (player weapon reach + lunge margin, player facing it), each dodge tires it (fatigue lowers the next dodge's chance and recovers over a few seconds), and it picks the open side (away, or through the player when a wall or lane-mate is behind it; `dodgeObstacleLayers`). Every hop ends in the dash: the moment a dodge lands with you inside `maxCounterDashRange` she winds up (the charge pose and warning for `charge.chargeTime`, 0.5 s: your window to parry or step aside) and dashes (`counterChance`, 1 = always), with nothing else gating it; the dodge has i-frames and both the counter's wind-up and the dash cannot be interrupted, so hitting her during that beat deals damage but never staggers her. The same charge is also the opener from range while chasing, on `dashCooldown`, and that one can be interrupted. Both dashes show the attack warning, and a whiff stuns her for 1.5 s with no dodge out of it: that is the punish window. When combo'd she does not stay stun-locked: after `breakStaggerHits` (2) hits in the pressure window she breaks the stagger with a hop (i-frames, `breakStaggerCooldown` 1 s), and a stagger that ends under pressure also ends in a hop. Both hops dash back as above. Parry and whiff stuns are never broken. Squared up to a planted player who faces her (1.5–5 u), she sometimes vaults clean over them (`vaultChance`, `vaultCooldown`; same `LeapState` as the Grunt, i-frames in the air) and lands 1.4 u behind, biting before they turn; rolling or jumping during her crouch calls it off. Tick `logDecisions` on the brain to see each decision in the console. All tuning is on the brain; the dodge `wallLayer` on the prefab should include walls, ground and enemies so the hop gets clamped.

## 2. What the prefab must have

`Tools > JunkLite > Systems > Validate Enemies` checks most of this (see step 4).

**Root object**
- Layer **Enemies** (14) on the root and body colliders. Weapons hit the `Damageable` + `Enemies` layers; mods and AI only query `Enemies`.
- An `EnemyCharacter` subclass (`GruntEnemy`, `HyenaEnemy`, ...). Never add a shared character base (see AGENTS.md).
- `Damageable` + `AttributesManager`. Health changes only through `Damageable` (one damage path).
- `StateMachine`, `EnemyMovement` and an `EnemyBrain` subclass.
- `Rigidbody`: dynamic (not kinematic), Use Gravity on, constraints left to code. `EnemyMovement` locks the axis across the lane from the enemy's Y rotation, the same way the player controller does.
- One solid body collider (capsule). The root sits at the **middle of the body**, not at the feet (the grunt root is ~1.3 u above its feet).
- Ledges are handled for you: `EnemyMovement` never lets a commanded step leave the floor ("Ledges" header: probe distance, drop tolerance, max gap width as clear air from lip to far floor, default 9 u, landing rise/drop, jump speed/height; long jumps arc higher). While chasing (the player, or the spot where it last saw them), a gap the enemy can clear is jumped (`GapJumpState`, Spine `Float` / `Jump_End`); one it cannot clear makes it stand at the edge for `Ledge Give Up Delay` (on the brain), forget the player for `Ledge Ignore Target Seconds` and return to patrol or idle. Patrols turn at edges. Knockback and launches still carry a body off, and anything that falls `Fall Kill Depth` below its last floor (or under `Fall Kill World Y`) dies at once with no drops, so nothing falls through the map for ever.
- Optional `EnemyContactDamage`: touching its body hurts the player and shoves her back to the side she came from. It only blocks a player facing it; one backed into it (e.g. shoved by another enemy) passes through. Tune damage/knockback per prefab (light for grunts, heavy for big enemies); leave it off for dummies and bosses with their own contact rules.
- `DamageFlashUniversal`, plus the Spine material `Game_URP_Unlit_DamageFlash.mat` (see `EnemySpineSetup.md`).
  - Leave **Emission Boost** (`_EmissionBoost`) at 0. Enemies should only light up through the hit flash. At 1.5 (tried 2026-10-31) the enemies looked too bright at rest.

**Children**
- `EnemyPerception`, referenced from the enemy component's **Perception** field.
- Attack hitboxes referenced by the brain (`melee.hitbox`, and `dash.dashHitbox` / `grab.*` where the brain uses them).
- Spine: `SkeletonAnimation` with `EnemySpineAnimationController` (or `BruteAnimationPresenter` / the Mecanim presenter). Its renderer sorting must stay **below** the hit VFX (`Enemy Hit X` uses sorting layer **Player**, order **100**).

**Inspector values to set on the enemy component**

| Field | What it does | Typical |
| --- | --- | --- |
| Base Stats | Stats asset (health, damage). Give each enemy its own asset. | `<Name> Stats.asset` |
| Experience Reward / Upgrade Point Reward | Reward on kill. | 25 / 0 |
| Ground Layers (on movement) | What counts as floor. Must include **Default + Ground + Wall** (mask **6145**), or launched enemies never land on Wall-layer floors. | 6145 |
| Can Be Knocked Back | Normal hits push it. | on (off for bosses) |
| **Can Be Launched** | The Up attack throws it into the air, and normal hits may lift it a little. **Off = it never leaves the ground**, including the vertical part of normal knockback. | on for grunts / hyenas, off for bosses, robots, dummies |
| Launch (block) | Launch height (`apexHeight`), gravity, bounce, juggle, spike and down/get-up timings. Copy the Grunt's block as a start. | Grunt values |
| Pass Through Enemies | Its body ignores other enemies' bodies (no blocking or stacking). | on for grunts and dummies |

## 3. Register it

These lists are code, so a new enemy needs one line in each:

1. **Dev console spawn:** `Assets/Game/Scripts/UI/PauseMenuDeveloperTools.cs`, add `AddEnemy("NAME", "<prefab path>");` next to the others.
2. **Validator:** `Assets/Game/Editor/EnemyArchitectureValidation.cs`. Add melee enemies to `MeleeEnemyPrefabs`; other types need their own `Validate...` method, like `ValidateRobot`.
3. **Launch test:** if the new enemy has **Can Be Launched** on, update `LaunchOnlyGruntsAndDevSpawnTests.OnlyGruntsAndHyenasCanBeLaunched` (it lists the launchable classes).

## 4. Check the prefab

1. `Tools > JunkLite > Systems > Validate Enemies`. It must log "Passed". It also catches a launchable enemy without a dynamic, gravity Rigidbody.
2. Run the EditMode tests (`Assets/Game/Tests/Editor/`), at least `LaunchOnlyGruntsAndDevSpawnTests` and `EnemyArchitectureTests`.
3. Play any level, open the dev console (F10), spawn it. Check it stands on the floor, chases, attacks, flashes on hit, gets knocked back and dies. If it can be launched, check that the sword **Up attack** launches it and that the 3rd air hit spikes it down.

## 5. Place it in a level

Read `MAIN_MECHANICS.md` first: characters live on straight lanes (`LanePath`) and the world turns 90° at Camera Switch Triggers.

**Placed enemies (in the scene from the start)**
1. Drag the prefab under the scene's enemy group inside **`[Gameplay]`** (SANDBOX uses `[Gameplay]/[Enemies]`). Never put enemies in the art hierarchy.
2. Put it roughly where it should stand. Set its height so the **capsule bottom touches the floor**. The root is the body centre, so it sits above the floor.
3. `Tools > JunkLite > Level > Snap Selected To Lane Path`. This moves it onto the closest point of the closest lane, keeps its height, and sets the lane's Y rotation (which also sets its movement axis). `Snap All Enemies To Lane Path` does every `EnemyBase` in the open scene at once. Both are one Undo step.
4. Keep the prefab connection (don't unpack). Unpacked copies keep old values when the prefab is retuned; Rooftop Scene v2 has three unpacked grunts like that.

**Encounter enemies (spawned by waves)**
1. On the `EncounterController`, add an entry to a wave. **Source Mode** is either *Spawn Prefab*, using **Enemy Prefab** + **Spawn Transform**, or *Existing Enemy*, using **Existing Enemy** + **Activate Existing Enemy**.
2. A Spawn Transform is an empty object under `[Gameplay]`. The enemy spawns with its position **and rotation**, so snap the spawn point too: select it and run **Snap Selected To Lane Path**. Set its height to the body centre (feet on the floor), the same as a placed enemy.
3. `Tools > JunkLite > Systems > Validate Encounters`.

## 6. Automating it (agents / editor scripts)

With the Editor open (`unity status` shows `ready`), the safe order is:

1. Instantiate with `PrefabUtility.InstantiatePrefab(prefab, scene)` (keeps the prefab link), parent it under `[Gameplay]`'s enemy group, and set a rough position.
2. Select it (`Selection.objects = ...`) and run `unity command menu -- --path "Tools/JunkLite/Level/Snap Selected To Lane Path"`. Snapping keeps the height, so fix Y first or after: feet on the floor.
3. Run `Tools/JunkLite/Systems/Validate Enemies` (and `Validate Encounters` if you touched waves) and read the console.
4. **Ask the user before saving the scene.** If another scene with unsaved changes is open, open the target scene additively and save only it.

Prefab tuning (launch, stats, toggles) goes through `PrefabUtility.LoadPrefabContents` + `SerializedObject` + `SaveAsPrefabAsset`, so Unity writes the YAML and keeps references.

## Hitboxes

Melee hitboxes (`Hitbox` child, trigger box along local +X, flipped with the root's X scale) must start **behind the body centre**, so a player standing inside the enemy is hit when the box turns on.

`Hitbox` does not rely on `OnTriggerEnter`. While active it overlap-queries its own collider shape (Box, Sphere or Capsule) on `Activate()` and every physics step, and raises `OnHit` once per damage receiver per activation. Trigger callbacks miss a player who is already inside the box when it turns on and both bodies are asleep, which is what made point-blank swings whiff. Grunt `Hitbox`: x −0.5 to 1.15 (local, before root scale). Hyena `hitbox` (all four colour prefabs): the child sits at identity and the box is authored in root-local space, centre (0.9, 0.2), size (2.55, 2.05), so it covers x −0.53 to 3.09 u and y 0.3 to 3.2 u above her feet in world units: the solid part of the `Attack_1_2` slash arc. Its hit window is 0.36–0.7 of the 0.5 s attack (the slash is visible from 0.18 s to 0.35 s). Hyena `Dashhitbox`: x −0.35 to 1.95. Match a hitbox to the clip, not to the attack range: a player who sees the slash pass through them expects the hit.

## Common mistakes

| Symptom | Cause |
| --- | --- |
| Up attack doesn't launch it | **Can Be Launched** off (per prefab; each Hyena colour separately). |
| Launched enemy hovers on landing, then snaps up after ~4 s | **Ground Layers** missing Wall/Default (use 6145). |
| Weapon hits miss | Body not on the **Enemies** layer, or the hit only reaches a child collider on another layer. |
| Moves off the lane or faces the wrong way | Not snapped to the lane, or the spawn point's rotation isn't the lane rotation. |
| Hit VFX draw behind it | Its sprite sorting layer/order is above `Player` / 100. |
| No hit flash | Wrong Spine material, or `isSpine` off (see `EnemySpineSetup.md`). |
| A boss gets lifted | **Can Be Launched** on. Bosses keep it off (Brute also ignores knockback in code). |
