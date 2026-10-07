# UI INSTRUCTIONS FOR AI AGENTS

Read this file before creating, editing, or styling any UI or in-world text in JunkLite.

Canonical copy: `Assets/Game/New UI/UI INSTRUCTIONS FOR AI AGENTS.pdf`

## Stack (this is the standard)

Inventory and all other HUD menus use **TextMesh Pro UGUI** (`TextMeshProUGUI` / `TMP_Text`) on Unity UI canvases. That is the default for every new screen, card, button, tooltip, and overlay.

Use **3D TextMesh Pro** (`TextMeshPro`, not UGUI) only when there is no canvas: floating world numbers, damage popups, labels parented to a mesh. World-space **canvases** still use `TextMeshProUGUI`.

Do not use Unity `UI.Text`, TextMesh (legacy), or UI Toolkit for game UI.

## Fonts (only these)

Source files live in `Assets/Game/New UI/Fonts/`.

| Use | Font | TMP asset |
| --- | --- | --- |
| World interactables and any in-world text (not HUD) | **Play** | `Play-Regular SDF.asset` |
| Main HUD: titles, cards, submenus | **ZuumeEdge** | `ZuumeEdge-Regular SDF.asset` |
| All other HUD text (body, labels, hints, stats) | **Satoshi** | `Satoshi-Variable SDF TMP.asset` |

Do not use Lekton, Clash Display, Bebas Neue, LiberationSans, or TMP example fonts for new work.

`Satoshi-Variable SDF.asset` is a Unity TextCore font, not TextMesh Pro. Ignore it for TMP.

## Material (mandatory)

Use **only** `Assets/Game/New UI/Default Font Material.mat` as the font/text material style from now on.

It is a bland overlay TMP material: no glow, no outline, no underlay, no foggy/pattern shaders. World-space prompts were picking up volumetric fog and the corrupted LiberationSans SDF material; this overlay shader avoids that.

Do **not** assign `Default Font Material` as the only material on a TMP component. Its `_MainTex` is empty. Assign the correct TMP font asset, then copy this material’s shader and bland settings onto that font’s atlas material (keep `_MainTex`).

Runtime helper that does this correctly:

- `UIFonts.ApplyWorld(tmp)`
- `UIFonts.ApplyHudTitle(tmp)`
- `UIFonts.ApplyHudBody(tmp)`
- `UIFonts.ApplyWorldTree(transform)`

Catalog: `Assets/Game/New UI/Resources/UIFontCatalog.asset`

Never use `TMP_Settings.defaultFontAsset` (LiberationSans with glow/fog keywords).

## Paths

- UI root: `Assets/Game/New UI/`
- Fonts: `Assets/Game/New UI/Fonts/`
- Default material: `Assets/Game/New UI/Default Font Material.mat`
- Helper: `Assets/Game/Scripts/UI/UIFonts.cs`
- If glyphs are missing in the Editor: menu **JunkLite > UI > Rebuild New UI Font Assets**
