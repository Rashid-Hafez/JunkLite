# Combat feel plan: remaining work

Written 2026-11-15. Source: the 18-item combat-feel checklist (anticipation, hit stop, shake, haptics and so on) audited against the code.

Phase 1 is in the working tree but **not committed and not tested in Play Mode**. Its details are in the top entry of `ARCHITECTURE_HANDOFF.md` ("Hit feel tiers, directional shake, gamepad rumble").

## Before anything else: verify Phase 1

1. Open the Editor and confirm it compiles. A `dotnet build` outside Unity showed no errors in the changed files, but it also reported errors in `EnemyLaunchSettings.cs`, `LaunchedState.cs`, `MainMenuController.cs` and `PlayerUI.cs` (missing `LaunchArc` and Junklite UI types). These looked like an out-of-date `.csproj`; this was not confirmed.
2. Open `Assets/Game/Scripts/Weapons/Fist (Cass).asset` and `Sword (Cass).asset`. Each combo step should show a **Hit Feel** block with values. They were edited as YAML, not through the Inspector.
3. Play with a gamepad on Service Floor or Sandbox and check:
   - Hits 1–2 feel quick (hit-stop 0.06 s, was 0.16 s) and hit 3 feels heavy (0.16 s).
   - The camera kicks sideways along the swing. If it reads worse than the old shake, lower `shakeForce` on the steps.
   - Rumble fires on landed hits, on damage taken and on a successful parry, and stops when paused or when the window loses focus.
   - No rumble at all when playing on keyboard and mouse.
   - The up launch still lets her jump straight after (its 0.05 s hit-stop was left alone).
4. Run the EditMode tests in `Assets/Game/Tests/Editor/` (`DamagePipelineTests` at least).
5. Tune, then commit.

## Phase 1 leftovers

- **Rumble on landing a launch.** Not done. The hook belongs where a launched enemy hits the ground or bounces (`LaunchedState` / `EnemyCharacter`), calling `FeedbackManager.Instance.DoRumble`.
- **Settings sliders for shake and rumble.** `FeedbackManager.ShakeIntensity` and `RumbleIntensity` (0–1) exist, but nothing drives them. The sliders must be authored in the options UI first (no UI built in code), then bound and saved with the other settings.
- **Non-Cass weapons.** `Fist.asset` and `Sword.asset` have no tiers and use the `WeaponManager` defaults. Author them only if those assets are still in use.
- **Other shake callers.** Mods, the Brute and parry still use the old non-directional shake. Move them to `DoCameraShake(source, direction, force)` only where a direction makes sense.

## Phase 2: Camera emphasis

- **FOV kick on heavy hits.** Add an `fovKick` field to `HitFeel` (degrees, 0 = none) and a short real-time pulse in `CameraManager`, reusing the parry zoom path. Use 1–2° on finishers, spikes and launches only. It must not fight the parry zoom or the Phantom Strike zoom-out: one zoom owner at a time.
- **Kill emphasis.** A short `FeedbackManager.DoSlowMotion` (about 0.3x for 0.25 s real) on the last enemy of an encounter. Needs a "last enemy died" signal from the encounter code.
- **Skip motion blur and focus kick.** On a side-on 2.5D lane with 2D Spine characters they smear the sprites and cost URP performance for little gain.

## Phase 3: Target reactions

- **Jitter during hit-stop.** Shake the struck enemy's visual a few pixels in unscaled time for the length of the hit-stop. `BruteEnemy` already does this for armor blocks; move it to `EnemyCharacter` so every enemy gets it. Offset the visual child only, never the Rigidbody.
- **Squash and stretch.** A 3–4 frame scale pulse on the Spine skeleton root: on an applied hit, on launch bounce, and on player landing. Code only, no new art. Keep it on the visual transform so colliders don't change.
- **Light and heavy Hurt clips.** Pick the Hurt clip by hit weight. Needs one extra clip per enemy from the animator, and a weight value carried on `HitReactionRequest`.
- **Player hit flash.** Not audited. Check whether `PlayerCharacter` flashes on damage the way enemies do with `DamageFlashUniversal`.

## Phase 4: Art and audio (mostly not code)

- **Weapon trails.** An authored `TrailRenderer` on the hand or blade bone, with code toggling `emitting` during the release window only. Same pattern as `PlayerDashStrike.strikeTrail`.
- **Motion smears.** Spine work: stretched frames on the fastest part of each swing. The per-step `slashPrefab` partly covers this already.
- **Follow-through and secondary motion.** Not audited. Check that each attack clip settles instead of snapping back, and consider Spine physics constraints for hair, cloth and loose parts.
- **Sound layering.** Not audited. Target three layers per hit (sharp transient, body, short tail) with slight pitch variation, and a heavier set for the heavy tier. See `PlayerSoundProfile`, `EnemySoundProfile`, `PlayerAudioHandler`.

## Phase 5: World response

- **Dust.** Pooled dust puffs on launch landing, bounce and spike impact, spawned through `CombatEffectsManager`.
- **Breakables.** A few props implementing `IDamageReceiver`, so they break through the normal damage path. Keep them out of the enemy layer logic and deduplicate like any other receiver.
- **Surface-matched impacts.** Choose the environment hit particle by surface type (metal sparks, concrete dust) in `WeaponAttackExecutor.TryResolveEnvironmentImpact`. Needs a small surface tag component on level colliders.

## Rules to keep

- `FeedbackManager` is the only owner of `Time.timeScale` effects and of gamepad motors. Never write either anywhere else.
- Feedback fires only on an applied `DamageResult`, never on the requested amount.
- `HitFeel` on the weapon asset is read-only config. No runtime state on ScriptableObjects.
- No UI built in code. Sliders and any new HUD element are authored as prefabs first.
- Update `ARCHITECTURE_HANDOFF.md` in the same change as each phase.

## Suggested order

1. Verify and commit Phase 1.
2. Phase 3 jitter and squash (code only, visible at once).
3. Phase 2 FOV kick and kill slow-mo.
4. Phase 5 dust.
5. Phase 4 with the animator and sound person, and the Phase 1 settings sliders once the options UI has room.
