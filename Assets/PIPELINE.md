# Junklite UI: web prototypes to Unity

Three UI Toolkit screens for Unity 6000.6.8f1, in one package:

- **Title screen**, from the Dossier prototype (prototype 1).
- **Inventory**, from P04 Afterglow Prism, with a simple Missions tab. There's no Codex.
- **Pause menu**, also from P04. It opens over the inventory, or over anything else.

The flow matches P04: RESUME SUBJECT or BEGIN PROCEDURE on the title opens the inventory, Esc pauses, and Quit to Title goes back. All three share one CRT layer (scanlines, vignette, moving grain, a rolling band and glitch tears) in place of P04's glass, scratches and lens fringe.

It's UI only. Equipping a mod or picking a menu item fires an event; nothing in the game reacts yet.

## Setup

Requires Unity 6000.6.8f1 and `JunkliteUI.unitypackage` from this folder. It replaces `JunkliteTitleScreen.unitypackage`.

1. Create the project somewhere outside OneDrive, e.g. `C:\UnityProjects`. OneDrive syncing doesn't get along with Unity's `Library` folder.
   - In Unity Hub: Projects > New project.
   - Pick editor 6000.6.8f1 and the Universal 2D template.
   - The first open takes 10 to 20 minutes.
2. Stop Play mode before importing. The Play button shouldn't be blue, and paused still counts as playing.
3. If there's an older version in the project, delete `Assets/Junklite`.
4. Choose Assets > Import Package > Custom Package..., pick the `.unitypackage` and import everything. Dragging the file into the Project panel also works.
5. Add the three scenes to File > Build Profiles > Scene List, so the screens can load each other in builds:
   - `Assets/Junklite/TitleScreen/Scenes/TitleScreen`
   - `Assets/Junklite/Inventory/Scenes/Inventory`
   - `Assets/Junklite/Pause/Scenes/Pause`

   In the Editor they load even without this, with a warning in the Console.
6. Open the TitleScreen scene. If Unity asks about saving an untitled scene, choose Don't Save.
7. In the Game tab, set the resolution to Full HD (1920x1080).
8. Press Play, then click in the Game view once so it gets keyboard input.

Changes made in Play mode are thrown away when you stop.

| Scene | What's in it |
| --- | --- |
| `TitleScreen` | The title. Resume and Begin load `Inventory`. |
| `Inventory` | Cas's mods, the inventory screen and the pause menu (Esc). Quit to Title loads `TitleScreen`. |
| `Pause` | The pause menu on its own, open from the start, for checking it in isolation. |

To use a screen in your own scene, drag in its prefab:
- `TitleScreen/Prefabs/TitleScreen`
- `Inventory/Prefabs/Inventory` together with `Inventory/Prefabs/Mods`
- `Pause/Prefabs/PauseMenu`

The scene needs a camera.

## Mods

Every mod in the GDD mod table is an asset in `Inventory/Mods`, holding:
- name, rarity, active or passive
- cooldown, damage change and description
- icon

To add one, choose Assets > Create > Junklite > Mod and fill it in. To change one, select it and edit it in the Inspector.

What Cas owns sits on the **Mods** object (the `ModInventory` component): storage capacity, what's in storage, and what's in each of the six slots (Z, X, C, V and two passive). The lists set there are the starting contents. Drag mod assets into them to change the starting build.

From code, find the `ModInventory` and call it. The screens redraw on every change.

```csharp
using Junklite.Inventory;

var mods = FindAnyObjectByType<ModInventory>();

mods.Add(pulseBarrier);                    // into storage; false if storage is full
mods.Remove(pulseBarrier);                 // from storage, or from its slot
mods.Remove("pulse-barrier");              // by id, for save files
mods.Equip(pulseBarrier, ModSlot.Z);       // from storage or another slot; swaps whatever was there
mods.Equip(pulseBarrier);                  // first empty slot of the right kind
mods.Unequip(ModSlot.Z);                   // back to storage
mods.Changed += () => Debug.Log("build changed");

// a mod that isn't an asset, from a server or a save file
var mod = ModDefinition.Create("new-mod", "New Mod", ModRarity.Classified, ModType.Active, cooldown: 12f);
mods.Add(mod);
```

`Equip` returns `WrongSlotType` for a passive mod in an active slot or the other way round, and `NotInInventory` for a mod Cas doesn't own.

The inventory screen also has `SetHealth(current, max)`, `SetArmor(value)` and `ClearVitals()` for the header and Cas's stats card. Until they're called, those show a dash, as in P04.

## Testing

### By hand

**Title**

| Control | Action | Expected |
| --- | --- | --- |
| RESUME SUBJECT, BEGIN PROCEDURE | Click | Loads the Inventory scene |
| CALIBRATE | Click | Button dips, On Calibrate fires |
| TERMINATE SESSION | Click | CRT power-off, then `SESSION TERMINATED · RECONNECT` on black |
| RECONNECT | Click, Enter or Space | Screen fades back in and the menu works |
| Hover, Up / Down | | Highlight follows, and wraps at both ends |

**Inventory**

| Control | Action | Expected |
| --- | --- | --- |
| A stored mod | Hover, or arrow keys | Orange frame on it, details on the right |
| A stored mod, then a slot | Click, click | Mod goes in the slot; a passive mod in an active slot shakes and warns |
| A stored mod | Drag onto a slot | Same |
| A slot's mod | Drag onto storage | Back to storage |
| A stored mod | Double-click, or Enter | Into the first free slot of its kind |
| Z, X, C, V | With a mod highlighted | Into that slot |
| Delete or Backspace | With a slot highlighted | Its mod back to storage |
| ALL / ACTIVE / PASSIVE | Click | Storage shows that kind only |
| MISSIONS | Click | The mods give way to mission cards; INVENTORY or Esc goes back |
| Cas | Hover | Orange stats card |
| Esc | With a mod picked up | Drops it |

**Pause**

| Control | Action | Expected |
| --- | --- | --- |
| Esc, or a gamepad's Start | In the inventory | Menu slides in, inventory dims, game time stops |
| RESUME, or Esc again | | Menu slides out, time runs again |
| MAP, SETTINGS | Click | Shakes, toast "isn't available yet" |
| QUIT TO TITLE | Click | Loads the TitleScreen scene |
| Up / Down | | Highlight moves and wraps |

Each screen should fit the Game view, with nothing cut off at the top.

### Automated

There are 54 tests, in each screen's `Tests/` folder, using Unity's UI Test Framework:

| Area | Tests |
| --- | --- |
| Title | 17 |
| `ModInventory` | 10 |
| Inventory screen | 19 |
| Pause | 8 |

The test assemblies only compile once that package is installed, so the screens work without it.

1. Window > Package Manager > + > Install package by name: `com.unity.ui.test-framework`.
   - Templates without Test Framework (3D URP, for example) also need `com.unity.test-framework`.
2. Window > General > Test Runner > PlayMode > Run All.
   - Takes about two minutes.

The tests send UI events directly, so they don't cover a physical mouse and keyboard. That's what the tables above are for.

## Events

| Component | Event | Fires when |
| --- | --- | --- |
| Title Screen Controller | On Resume, On Begin | Clicked or submitted. Wired to the Scene Link on the same object, which loads `Inventory`. |
| | On Calibrate | Clicked or submitted (settings isn't built yet) |
| | On Terminate | After the power-off animation |
| Inventory Screen | On Tab Changed | The player switched tabs. Passes the tab's name. |
| Pause Menu | On Opened, On Closed | The menu opens or closes |
| | On Quit To Title | Wired to the Scene Link on the same object, which loads `TitleScreen` |
| | On Locked Item | Map or Settings chosen. Passes the item's name. |

From code: `TitleScreenController.CommandExecuted`, `PauseMenu.CommandExecuted`, `ModInventory.Changed`.

Pause Menu settings:
- Open With Escape: off if the game opens it itself, with `Open()`, `Close()` or `Toggle()`.
- Pause Time: sets `Time.timeScale` to 0 while it's open.
- Open On Start, Show Backdrop: what the Pause scene uses.

To change where a menu item goes, change the Scene Name on its Scene Link, or point the event at something else.

## Missions

The Missions tab is plain text cards, in the inventory's colours and fonts: an orange kicker (Azeret Mono), a heading (Anton), the text (Azeret Mono) and optional lines such as objectives (JetBrains Mono).

It holds two placeholder cards for now. Change them on the Inventory prefab, under Inventory Screen > Missions: each entry has a kicker, title, body and points. Add, remove or reorder them in the Inspector, or from code with `SetMissions(entries)` and a list of `InfoEntry`. `ShowTab(InventoryTab.Missions)` opens the tab.

Two columns fit about eight cards before running off the bottom; past that the page would need scrolling.

## What's baked and what's live

| Layer | Contents | To change it |
| --- | --- | --- |
| `title-background.png` | Title, tree, portrait, grid, labels, profile | Edit the Dossier prototype, run `tools/bake-art.ps1` |
| `inventory-background.png` | Light field, Cas, headings, corner marks | Edit P04, run `tools/bake-p04.ps1` |
| `pause-showcase.png`, `light-field.png` | Cas from behind, the ghost "PAUSED", corner marks; the light field | Same |
| UXML / USS | Menus, slots, storage, details, vitals, callouts, legend, Missions | Each screen's `UI` folder |
| `CrtOverlay` | Grain, rolling band, glitch tears | Glitch Area sets the part of the screen that tears |
| Overlay textures | Scanlines, vignette, grain, band | `tools/bake-art.ps1` |

Rule of thumb: if it never changes and doesn't animate on its own, bake it. Anything that changes, reacts to input or moves stays live.

The tears reuse each screen's background, so only the title's bright "ghost" strip has its own image.

To re-bake, run these from the `Title screen` folder. They need Python and Edge, and `bake-p04.ps1` needs the `UI testing` folder next to `Title screen`.

```bash
powershell -ExecutionPolicy Bypass -File unity-port/tools/bake-art.ps1
```

```bash
powershell -ExecutionPolicy Bypass -File unity-port/tools/bake-p04.ps1
```

They write straight into `unity-port/Junklite`, and stop if their local server's port is already taken, since a server left over from an interrupted run would serve stale files. Copy the PNGs into the same folders in the Unity project; the import settings are kept. `bake-p04.ps1` also copies the 512 px mod icons, since Unity can't import the site's `.webp` copies.

### Different from the prototypes

- P04's glass, scratches, warm bleed and blue/orange lens fringe are replaced by the title's CRT layer, as asked.
- The P04 light field is a still frame. It doesn't drift or follow the mouse.
- Behind the pause menu the inventory is darkened, not blurred. UI Toolkit can't blur.
- P04's springs (the menu highlight, filter pill, tab line and focus corners) are eased transitions here. They feel close but don't overshoot.
- Entrances are one fade instead of staggered.
- No glow on rarity bars or the light ring, and the mod description has slightly tighter lines. USS has no `box-shadow` or `line-height`.
- P04 asked for Bender on numbers, but the browser never loaded it, so the screens everyone saw used JetBrains Mono. Unity matches those. It's one variable (`--font-digits`) if Bender is wanted.
- The Dossier title screen loses its grid draw-in, its tree breathing and the portrait's small shake.

### Size

Windows builds of the title screen before and after baking:

| | Before | After |
| --- | --- | --- |
| Title screen textures | 48.4 MB | 13.2 MB |
| Whole build | 157.6 MB | 120.0 MB |
| UI elements | 216 | 49 |

The inventory and pause backgrounds are 1920x1080. The P04 art behind them is that size, so 4K wouldn't add detail.

## Porting another screen

Specimen and Session can go the same way.

1. List every control, state and timing from the prototype's JS. That list becomes the tests.
2. Decide what to bake (see above).
3. Add a bake. Hide the live parts with `visibility: hidden`, not `display: none`, so the layout stays put. `bake-p04.ps1` shows how to bake a page that loads files from root paths.
4. Measure the live parts with `getBoundingClientRect()` on the prototype at 1920x1080.
5. Write UXML and USS for the live parts only (see the table below). Import `Shared/UI/Common.uss` and add the `Shared/UI/Crt.uxml` template last.
6. Write the C#. Keep logic and cosmetic effects in separate components. Use `WaitForSecondsRealtime` so the UI still runs while the game is paused.
7. Make a GameObject with a UI Document (using `Shared/Settings/JunklitePanelSettings`), your scripts and a `CrtOverlay`, and save it as a prefab in a scene with a camera.
8. Add tests and compare against the browser.
9. Check a Windows build and the Editor's Game view. Both have caught bugs the tests missed.

Things that bit us:
- Script start order differs between the Editor and a build. It left the title portrait blank in builds.
- In the Game view, the UI Document root can be full width but zero height. That cut off the top half of the screen. Call `rootVisualElement.StretchToParentSize()`.
- Reimporting UXML in Play mode rebuilds the tree and re-enables the components. Look up elements in `OnEnable`, not `Awake`, and null-check them.
- A stylesheet added in a template only reaches that template. The glitch layer sits outside the CRT template, so the CRT rules are imported through `Common.uss`. Before that, an unstyled glitch layer pushed the inventory's slots 729 px down.
- An element with `overflow: hidden` clips children that hang over its edge, so the dashed outlines on empty cells sit just inside.

Texture import settings:

| | Backgrounds | Mod icons | Everything else |
| --- | --- | --- | --- |
| Compression | High Quality | High Quality | High Quality, or None for tiny UI textures |
| Generate Mipmaps | On | On | Off |
| Alpha Is Transparency | Off | Off | On |
| Non-Power of 2 | None | None | None |
| Wrap Mode | Clamp | Clamp | Clamp |

Keep texture sizes multiples of 4 so they can be block compressed.

Panel Settings: Scale With Screen Size, reference resolution 1920x1080, Screen Match Mode Expand. Other aspect ratios get letterboxed. All three screens share one Panel Settings asset, so the pause menu (sort order 10) draws over the inventory (sort order 0).

## CSS to USS

| Web | Unity | Notes |
| --- | --- | --- |
| `calc(96 * var(--u))` | `96px` | Panel Settings does the scaling |
| `<div>`, `<span>` | `VisualElement`, `Label` | |
| `<button>` | `Button` with `plain-button` | Clears the theme's background, border, margin and padding in every state |
| `[data-sel]`, `.is-current` | `.menu-item--selected`, `.tile--current` | BEM classes, toggled from C# |
| `::before`, `::after` | Child element | No pseudo-elements in USS |
| `text-transform: uppercase` | Uppercase text | USS can't change case. Mod names are uppercased in code |
| `letter-spacing: 0.22em` | `letter-spacing: 22px` | On 6000.6.8f1 the value acts as 1/100 of the font size. Recheck after upgrading |
| `font-weight` | One font file per weight | Check `fonts/README.md` for licences |
| `cubic-bezier()` | `ease-out-cubic` etc. | Closest built-in |
| `@keyframes` | Transition or coroutine | |
| Gradients, `box-shadow` | Bake them, or a small gradient texture tinted in USS | |
| `border-style: dashed` | A tiled 9-slice texture | USS borders are solid only |
| `clip-path` | `overflow: hidden` on a wrapper | |
| `text-overflow: ellipsis` | Avoid with letter-spacing | It cut off short names like "EMPTY" |
| Decorative layers | `picking-mode="Ignore"` | Otherwise they block clicks |

Translucent colours on live elements need new alphas. Browsers blend in gamma space, while Unity's Linear colour space blends in linear space, so faint light overlays come out too bright. Baked art is unaffected. With `L()` as sRGB-to-linear and `R` as the colour the browser shows:

```
new alpha = (L(R) - L(background)) / (L(colour) - L(background))
```

For black overlays: `1 - (1 - alpha)^2.2`. Dark overlays look weaker than they measure, so check by eye. Where a translucent colour sits on a solid one, like text on the orange card, use the blended solid colour instead.

## Known issues

- Specimen and Session aren't ported. The prototypes' variant picker is review-only and isn't included.
- The inventory holds mods only, as in P04. Other kinds of items would need their own section.
- Map and Settings are locked placeholders with events. Missions holds placeholder text.
- Gamepad works for pause (Start) and through UI navigation, but hasn't been tried on a real pad. Hover is mouse-only. Dragging needs a mouse; pad players use the slot keys or Enter.
- Fonts in the package are all OFL. PP Fraktion Mono isn't licensed yet, so the HUD uses Azeret Mono.
- If the main project uses uGUI too, it needs an EventSystem with an Input System UI Input Module.
- Frame rate cap (VSync) is a project setting, so it's left to the main project.
- Builds log a URP Bloom shader warning. These screens don't use post-processing, so it's harmless.

## Tested

Run on 10 Nov 2026 with Unity 6000.6.8f1 on Windows 11:
- **Work project:** all 54 package tests pass. So do 22 more kept out of the package:
  - virtual-mouse and keyboard tests through the Input System, including real dragging and Esc;
  - screen captures, compared against the P04 renders.
- **Fresh Universal 2D project:** the package imports with no compile errors before the test package is installed, and all 54 package tests pass after.

Not yet done:
- A run-through with a real mouse, keyboard and gamepad in the Editor.
- A Windows build of this version.
- A 3D URP project.

`reference-title.png`, `reference-inventory.png`, `reference-missions.png` and `reference-pause.png` show how the screens should look.
