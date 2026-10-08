# JunkLite agent notes

**Every AI agent working in this repo (Claude Code, Codex, Cursor, Copilot, Gemini, etc.) must read this file before making changes.** It applies to every contributor's agent, not just one machine. `CLAUDE.md`, `GEMINI.md`, and `.github/copilot-instructions.md` are one-line pointers to this file, so keep the rules in this file only.

## Unity CLI setup (do this first)

Agents edit scenes, prefabs, and assets through the **Unity CLI** connected to the running Editor, not by hand-editing `.unity`/`.prefab`/`.asset` YAML.

1. Install the CLI (macOS): `curl -fsSL https://public-cdn.cloud.unity3d.com/hub/prod/cli/install.sh | UNITY_CLI_CHANNEL=beta bash`
2. Put it on PATH for **all** shells (the installer only edits `~/.zshrc`, which agent shells don't load). Add this to `~/.zshenv`:
   `[ -f "$HOME/.unity/env" ] && . "$HOME/.unity/env"`
   Then restart your terminal or agent session.
3. With the JunkLite Editor open, check the connection: `unity status` should show state `ready`. If it won't connect, run `unity pipeline list`. Safe Mode means there are compile errors to fix first.

Agent rules: run `unity status` before editing any scene or asset. Use `unity command` to list what the Editor exposes. Ask the user before saving scenes or making large changes. Only edit scene files directly if no Editor is reachable, and say so.

JunkLite is a **single-player** Unity 6 (6000.3.x) URP game. First-party code lives in `Assets/Game/Scripts/`.

## Read first

| Task | Read |
| --- | --- |
| Any architecture, combat, mod, manager, camera, enemy, or encounter work | `ARCHITECTURE_HANDOFF.md` (source of truth: status, known gaps, safe next steps) |
| UI, fonts, HUD, in-world prompts | The **UI rules** section below (full copy: `Assets/Game/New UI/UI INSTRUCTIONS FOR AI AGENTS.pdf`) |

If you change an architectural boundary, update `ARCHITECTURE_HANDOFF.md` in the same change.

## Key architecture

**Damage: one path only.**
Producers (weapons, hazards, mods, status effects) build a `DamageRequest` and send it to an `IDamageReceiver`. `Damageable` validates, applies armor, changes health through `AttributesManager`, and returns a `DamageResult` with an outcome and the amount actually applied. See `Character/DamageContracts.cs`.
- Only react to a successful result. Hit VFX, hit-stop, recoil, durability, "hit" events, and lifesteal all depend on the applied result, never on the requested amount.
- Deduplicate receivers in AOE and piercing attacks, since enemies can have several colliders.
- Player and enemy code own their own reactions (stun, knockback, VFX). `Damageable` does not.

**Player vs enemy: no shared character base.**
`PlayerCharacter` derives directly from `MonoBehaviour` and owns input, parry, shields, immunity, death, and respawn. Enemies derive from the small `EnemyBase` (via `EnemyCharacter`), and AI is composed per archetype. Do not add a universal character base class. `CharacterBase` is legacy and is being removed.

**Weapons.**
`PlayerWeaponLoadout` owns the two equipped slots, pickups, attach/swap/drop/break, and the `WeaponChanged` event. `WeaponManager` owns combat rules, attack input, combos, hit detection, and damage requests. UI should subscribe to `PlayerWeaponLoadout`, not to `WeaponManager` slots.

**Mods and abilities.**
- Mod ScriptableObjects are **read-only config**. Cooldowns, charges, durability, VFX refs, and flags belong on `ModInstance` (one per slot), not on the asset.
- `ModExecutionRunner` (player-owned) starts each activation with a `ModExecutionContext`. That context owns cancellation callbacks and lock scopes.
- Lifecycle: `OnInstalled` and `OnRemoved` mean slot ownership. `OnCombatModeEntered` and `OnCombatModeExited` mean temporary availability. Do not mix them up.
- Input, movement, and immunity locks are disposable leases that compose. Never set or clear them directly. Each long-running ability needs exactly one owner and one cleanup path covering completion, cancel, removal, mode exit, disable, and death.

**Runtime roots and managers.**
- **Game Root** (`Prefabs/Manager/Game Root.prefab`) is persistent and duplicate-safe. It holds `GameManager`, `PlayerLifecycle`, `GameUIManager`, `GameInputManager`, `PlayerCombatTracker`, the gameplay canvas, and the EventSystem.
- **Level Context** is scene-local and standalone (never nested in the Game Root). It holds level identity, the spawn-player flag, and typed spawn points.
- `PlayerLifecycle` owns spawning, the current player, death, and soft respawn. Listen to `PlayerSpawned` and do not reach through `GameManager` for the player.
- `GameUIManager` owns the HUD, pause, game-over, and loading screens.
- `GameManager` is now only global state, pause, scene transitions, and music. Do not add responsibilities back into it.
- `CameraManager` is scene-local and rebinds every registered camera on `PlayerSpawned`.
- Duplicate singletons must remove **only their own component**, never `Destroy(gameObject)`. Doing that once destroyed the whole Game Root.

## UI rules

**Stack.** Every HUD screen, menu, card, button, tooltip, and overlay uses **TextMesh Pro UGUI** (`TextMeshProUGUI` / `TMP_Text`) on Unity UI canvases. World-space canvases also use `TextMeshProUGUI`. Use 3D `TextMeshPro` only when there is no canvas, for example damage popups or labels parented to a mesh. Never use legacy `UI.Text`, legacy TextMesh, or UI Toolkit for game UI.

**Fonts.** Only these three, all in `Assets/Game/New UI/Fonts/`:

| Use | Font | TMP asset |
| --- | --- | --- |
| World interactables and any in-world text | Play | `Play-Regular SDF.asset` |
| Main HUD: titles, cards, submenus | ZuumeEdge | `ZuumeEdge-Regular SDF.asset` |
| All other HUD text: body, labels, hints, stats | Satoshi | `Satoshi-Variable SDF TMP.asset` |

Don't use Lekton, Clash Display, Bebas Neue, LiberationSans, or the TMP example fonts. `Satoshi-Variable SDF.asset` (without "TMP") is a TextCore font, so ignore it for TMP.

**Material.** The only text style is `Assets/Game/New UI/Default Font Material.mat`: a plain overlay with no glow, outline, underlay, or fog. Don't assign it directly, because its `_MainTex` is empty. Use the helpers in `Assets/Game/Scripts/UI/UIFonts.cs`, which put the right font and material settings on the text for you: `UIFonts.ApplyWorld`, `ApplyHudTitle`, `ApplyHudBody`, and `ApplyWorldTree`. The font list they use is `Assets/Game/New UI/Resources/UIFontCatalog.asset`. Never use `TMP_Settings.defaultFontAsset`, which is LiberationSans with glow and fog. If glyphs are missing in the Editor, run **JunkLite > UI > Rebuild New UI Font Assets**.

## Do

- Prefer composition and small, concrete boundaries. Split a manager only at a real lifecycle or scene boundary.
- Cache references and reuse buffers in per-frame code. Unsubscribe events in `OnDisable`.
- Run the EditMode tests in `Assets/Game/Tests/Editor/` (`DamagePipelineTests`, `PlayerLifecycleConfigurationTests`) after touching damage, locks, or lifecycle code.
- Use the editor tools under **Tools > JunkLite > Systems** (`Validate`, `Validate Enemies`, `Validate Encounters`) to check scene setup.
- Tell the user which Inspector or prefab wiring they need to do, rather than guessing at scene edits.

## Don't

- Don't reintroduce `IDamageable`, `DamageInfo`, bool `TakeDamage`, or any other path that changes health outside `Damageable`.
- Don't store runtime state on ScriptableObjects.
- Don't add service locators, global event buses, empty interfaces, ECS, or multiplayer/replication patterns.
- Don't make broad scene edits or modify third-party/plugin code. `Tools > JunkLite > Systems > Rebuild` rewrites V2.5, so run it only when the user explicitly asks.
- Don't hand-write `.meta` files. Let Unity generate them.
- Don't use legacy `UI.Text`, UI Toolkit, or unapproved fonts for game UI (see **UI rules** above).
