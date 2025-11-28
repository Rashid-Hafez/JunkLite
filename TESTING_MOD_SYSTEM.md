# How to Test the Mod System

## Step 1: Create Assets in Unity Editor

### 1a. Create a PogoMod Scriptable Object
- Right-click in `Assets/Game/Prefabs/Mods/` (create folder if needed)
- Create → Junklite/Mod
- Name it: `PogoMod_Common`
- Set values:
  - Display Name: "Pogo Mod"
  - Damage Bonus: 5
  - Durability Cost Per Hit: 2
  - Max Mod Durability: 20
  - Element: Dull
  - Effect Specific Strength: 15 (for the upward force)

### 1b. Create PogoModEffect Prefab
- Create empty GameObject in your test scene
- Add component: `PogoModEffect`
- Drag into `Assets/Game/Prefabs/Mods/` as prefab: `PogoModEffect.prefab`
- Assign this prefab to your `PogoMod_Common` → Mod Effect Prefab field

### 1c. Create a Weapon with Mod Slots
- Create empty GameObject: "TestWeapon"
- Add component: `WeaponInstance`
- Create WeaponData ScriptableObject:
  - Name: `TestWeapon_Data`
  - Base Damage: 10
  - Mod Slots: 2
  - Max Weapon Durability: 100
- Drag into WeaponInstance → Weapon Data field

---

## Step 2: Test in Code (Console)

### Test 2a: Add a Mod and Check Damage Calculation
```csharp
// In any script or play mode console:
WeaponInstance weapon = GetComponent<WeaponInstance>();
Mod_Data pogoMod = Resources.Load<Mod_Data>("Mods/PogoMod_Common");

// Add the mod
weapon.AddMod(pogoMod);

// Check damage
float totalDamage = weapon.CalculateTotalDamage();
Debug.Log($"Total Damage: {totalDamage}"); // Should be 10 + 5 = 15
```

### Test 2b: Verify Durability Consumption
```csharp
// After adding mod and hitting an enemy:
foreach (ModEffectBase effect in weapon.GetActiveEffects())
{
    Debug.Log($"Mod: {effect.modData.displayName}, Durability: {effect.CurrentDurability}");
    // Should decrease by 2 each hit
}
```

### Test 2c: Remove a Broken Mod
```csharp
// Hit until durability <= 0 (10 hits = 10 * 2 cost)
// Mod should auto-remove when broken

Debug.Log($"Active Mods: {weapon.GetActiveEffects().Count}"); // Should be 0 after break
```

---

## Step 3: Visual Test in Scene

1. Create a test scene with:
   - Player with WeaponInstance
   - Enemy with IDamageable component
   - Canvas with debugging UI (optional)

2. Equip the weapon with a mod

3. Attack the enemy

4. Check in Inspector:
   - Enemy health decreases by 15 (10 base + 5 mod bonus)
   - Weapon mod durability bar decreases by 2
   - After 10 hits, mod disappears

---

## Expected Behavior Flow

```
1. Player attacks
   ↓
2. OnTriggerEnter fires
   ↓
3. CalculateTotalDamage() = 10 + 5 = 15
   ↓
4. enemy.TakeDamage(DamageInfo { Amount: 15, Type: Physical })
   ↓
5. weapon.Hit() event fires
   ↓
6. PogoModEffect.OnHit() subscribes and runs
   ├─> base.OnHit() ← calls Consume(2)
   │   └─> CurrentDurability -= 2 (now 18)
   └─> AddForce upward (visual effect)
   
7. After 10 hits: CurrentDurability = 0 → IsBroken = true → weapon.RemoveMod()
   ↓
8. Mod is destroyed, next hit does only base damage (10)
```

---

## Debug Checks

Add this to a UI canvas or Console to monitor live:

```csharp
public void DebugWeapon(WeaponInstance weapon)
{
    float totalDamage = weapon.CalculateTotalDamage();
    int modCount = weapon.GetActiveEffects().Count;
    
    Debug.Log($"[Weapon Debug]");
    Debug.Log($"  Total Damage: {totalDamage}");
    Debug.Log($"  Active Mods: {modCount}");
    
    foreach (ModEffectBase effect in weapon.GetActiveEffects())
    {
        Debug.Log($"  - {effect.modData.displayName}: {effect.CurrentDurability}/{effect.modData.maxModDurability}");
    }
}
```

Call this after each hit to see real-time updates.
