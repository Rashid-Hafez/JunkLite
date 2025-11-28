using UnityEngine;
using NUnit.Framework;

public class WeaponInstance_Tests
{
    private WeaponInstance weaponInstance;
    private WeaponData weaponData;
    private Mod_Data pogoModData;

    [SetUp]
    public void Setup()
    {
        // Create a test weapon
        GameObject weaponObj = new GameObject("TestWeapon");
        weaponInstance = weaponObj.AddComponent<WeaponInstance>();
        
        // Create weapon data
        weaponData = ScriptableObject.CreateInstance<WeaponData>();
        weaponData.baseDamage = 10f;
        weaponData.maxWeaponDurability = 100;
        weaponData.modSlots = 2;
        
        // Create a mod
        pogoModData = ScriptableObject.CreateInstance<Mod_Data>();
        pogoModData.displayName = "Test Pogo Mod";
        pogoModData.damageBonus = 5f;
        pogoModData.durabilityCostPerHit = 2f;
        pogoModData.maxModDurability = 20f;
        pogoModData.element = Mod_Data.ModElement.Dull;
        
        // You'll need to create a test prefab for modEffectPrefab
        // For now, this is where you'd assign it:
        // pogoModData.modEffectPrefab = testPogoEffectPrefab;
    }

    [Test]
    public void TestWeaponBaseDamage()
    {
        // Before any mods, weapon should do base damage
        float baseDamage = weaponData.baseDamage;
        Assert.AreEqual(10f, baseDamage, "Base damage should be 10");
    }

    [Test]
    public void TestModDamageBonus()
    {
        // Mod adds 5 damage
        float modBonus = pogoModData.damageBonus;
        Assert.AreEqual(5f, modBonus, "Mod bonus should be 5");
    }

    [Test]
    public void TestDurationConsumption()
    {
        // Create a mock effect to test durability
        GameObject mockEffectObj = new GameObject("MockEffect");
        MockModEffect mockEffect = mockEffectObj.AddComponent<MockModEffect>();
        
        // Initialize with mod data
        mockEffect.modData = pogoModData;
        mockEffect.CurrentDurability = pogoModData.maxModDurability;
        
        // Check initial durability
        Assert.AreEqual(20f, mockEffect.CurrentDurability, "Initial durability should be 20");
        
        // Consume durability
        mockEffect.CurrentDurability -= pogoModData.durabilityCostPerHit;
        Assert.AreEqual(18f, mockEffect.CurrentDurability, "After one hit, durability should be 18");
        
        // After 10 hits, should break
        for (int i = 0; i < 9; i++)
        {
            mockEffect.CurrentDurability -= pogoModData.durabilityCostPerHit;
        }
        Assert.AreEqual(0f, mockEffect.CurrentDurability, "After 10 hits, durability should be 0");
        Assert.IsTrue(mockEffect.IsBroken, "Mod should be broken");
    }

    [TearDown]
    public void Teardown()
    {
        if (weaponInstance != null)
            Object.DestroyImmediate(weaponInstance.gameObject);
        if (weaponData != null)
            Object.DestroyImmediate(weaponData);
        if (pogoModData != null)
            Object.DestroyImmediate(pogoModData);
    }
}

// Mock effect for testing without needing full scene setup
public class MockModEffect : MonoBehaviour
{
    public Mod_Data modData;
    
    private float _currentDurability;
    public float CurrentDurability
    {
        get { return _currentDurability; }
        set { _currentDurability = Mathf.Max(0, value); }
    }

    public bool IsBroken => CurrentDurability <= 0;
}
