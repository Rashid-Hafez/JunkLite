# Inventory UI Setup Guide

## Canvas Structure

Your Canvas should have this hierarchy:

```
Canvas
├─ WeaponModsToolbar (panel at bottom)
│  ├─ ModSlotsContainer (horizontal layout group)
│  │  └─ [ModSlot prefabs spawn here]
│  │
├─ InventoryPanel (main inventory window)
│  ├─ Title: "Mods"
│  └─ ModListScroll (scroll rect)
│     └─ ModListContainer (vertical layout group)
│        └─ [InventoryModItem prefabs spawn here]
```

---

## Step 1: Create ModSlot Prefab

This is what displays **equipped mods** at the bottom toolbar.

1. **Create a Button UI element** in your scene
   - Name: `ModSlot`
   - Add child Image (for mod icon)
   - Add child Slider (for durability bar)
   - Add child Text (optional, for mod name)

2. **Attach `ModSlotUI` script** to the button

3. **Assign UI references in inspector:**
   - Mod Icon → Image component
   - Durability Bar → Slider component
   - Mod Name Text → Text component (optional)
   - Unequip Button → Button component (the parent button itself)

4. **Drag into `Assets/Prefabs/UI/` as prefab**

---

## Step 2: Create InventoryModItem Prefab

This is what displays **reserve mods** in the inventory list.

1. **Create a Button UI element** in your scene
   - Name: `InventoryModItem`
   - Add child Image (for mod icon)
   - Add child Text (for mod name)
   - Add child Text (for damage bonus, e.g., "+5 DMG")

2. **Attach `InventoryModItemUI` script** to the button

3. **Assign UI references in inspector:**
   - Mod Icon → Image component
   - Mod Name Text → Text component
   - Mod Bonus Text → Text component
   - Equip Button → Button component (the parent button)

4. **Drag into `Assets/Prefabs/UI/` as prefab**

---

## Step 3: Setup Weapon Mod Display (Toolbar)

1. **Create a Panel** at the bottom of your Canvas
   - Name: `WeaponModsToolbar`
   - Use `Image` with transparent white background

2. **Add child: `ModSlotsContainer`** (empty GameObject)
   - Add `HorizontalLayoutGroup` component
   - Set spacing: 10
   - Set child alignment: Middle Center

3. **Attach `WeaponModDisplayUI` script** to the Panel

4. **Assign in inspector:**
   - Mod Slots Container → the ModSlotsContainer transform
   - Mod Slot Prefab → your ModSlot prefab
   - Layout Group → the HorizontalLayoutGroup component

---

## Step 4: Setup Inventory List (Main Panel)

1. **Create a Panel** in your Canvas (or reuse existing)
   - Name: `InventoryPanel`
   - Position: top-right or wherever you want

2. **Add child: `ModListScroll`** (Scroll Rect)
   - Set layout to "Vertical"

3. **Inside Scroll Rect, add:**
   - `ModListContainer` (empty GameObject)
   - Add `VerticalLayoutGroup` component
   - Set child force expand height: ON
   - Set spacing: 5

4. **Attach `InventoryModListUI` script** to the Panel (or Scroll Rect)

5. **Assign in inspector:**
   - Mod List Container → the ModListContainer transform
   - Mod Item Prefab → your InventoryModItem prefab
   - Layout Group → the VerticalLayoutGroup component

---

## Step 5: Setup InventoryComponent

1. **Create an empty GameObject** in your scene
   - Name: `GameManager` or `InventoryManager`

2. **Attach `InventoryComponent` script** to it

3. **Assign in inspector:**
   - Equipped Weapon → your WeaponInstance in the scene

---

## Testing the Flow

### Manual Test:

1. **Play the game**

2. **In Console, run:**
   ```csharp
   InventoryComponent inv = FindFirstObjectByType<InventoryComponent>();
   Mod_Data mod = Resources.Load<Mod_Data>("Mods/PogoMod_Common");
   
   // Simulate picking up a mod
   inv.PickupMod(mod);
   ```

3. **You should see:**
   - Mod appears in inventory list
   - Click "Equip" button
   - Mod moves to weapon toolbar at bottom
   - Clicking "Unequip" moves it back

### Scene Test:

1. **Add a ModDrop_Instance in the world**
   - Create GameObject
   - Attach `ModDrop_Instance` component
   - Assign Mod_Data

2. **Wire it to inventory:**
   ```csharp
   // In ModDrop_Instance or pickup trigger:
   InventoryComponent inventory = FindFirstObjectByType<InventoryComponent>();
   inventory.PickupMod(modData);
   Destroy(gameObject);
   ```

---

## Data Flow Summary

```
World Pickup
  ↓
inventory.PickupMod(modData)
  ↓
modsInReserve.Add(modData)
  ↓
OnInventoryChanged.Invoke()
  ↓
InventoryModListUI.RefreshDisplay()
  ↓
Spawns InventoryModItem buttons
  ↓
Player clicks "Equip" button
  ↓
inventory.EquipModToWeapon(modData)
  ↓
modsInReserve.Remove(modData)
  ↓
weapon.AddMod(modData)
  ↓
OnInventoryChanged.Invoke()
  ↓
WeaponModDisplayUI.RefreshDisplay()
  ↓
Spawns/updates ModSlot in toolbar
  ↓
Mod now equipped!
```

---

## Troubleshooting

**"No mods in inventory"**
- Make sure you called `inventory.PickupMod(mod)`
- Check that `InventoryModListUI.Initialize()` was called

**Slots not appearing**
- Check that `modSlotsContainer` is assigned in inspector
- Make sure `modSlotPrefab` has `ModSlotUI` component
- Verify `HorizontalLayoutGroup` is enabled

**Equip button not working**
- Check that `equipButton` is assigned in `InventoryModItemUI`
- Verify `OnClick` listener was added in `Start()`

**Durability bar not updating**
- Subscribe to `weapon.OnHit` in `WeaponModDisplayUI`
- Check that `UpdateDurabilityBar()` is called

