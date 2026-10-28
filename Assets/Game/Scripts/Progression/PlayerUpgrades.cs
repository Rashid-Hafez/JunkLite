using System;
using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    public enum UpgradeLockReason
    {
        None,
        Owned,
        NoPoints,
        MissingPrerequisite,
        MissingWeapon,
        /// <summary>The node's tier is above this build's Max Available Tier.</summary>
        NotInBuild,
        Unknown,
    }

    /// <summary>
    /// Persistent record of the player's permanent upgrades: acquired weapons, bought tree nodes,
    /// bonus upgrade points and one-shot point sources already collected. Lives beside
    /// <see cref="PlayerProgression"/> on the Game Root and is saved into the same profile file.
    /// Game code reads it; only this class writes it. Points: one per earned level
    /// (<see cref="UpgradeTreeConfig.pointsPerLevel"/>) plus bonus points from bosses and pickups.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerUpgrades : MonoBehaviour
    {
        public static PlayerUpgrades Instance { get; private set; }

        [Tooltip("Leave empty to load Resources/UpgradeTreeConfig. A missing asset falls back to the built-in default tree.")]
        [SerializeField] private UpgradeTreeConfig config;

        private readonly HashSet<string> ownedWeapons = new();
        private readonly HashSet<string> purchased = new();
        private readonly HashSet<string> collectedSources = new();
        private readonly List<UpgradeNode> scratch = new();
        private int bonusPoints;
        private PlayerProgression progression;
        private bool ownsConfig;

        /// <summary>Raised after any owned weapon, purchase or point change.</summary>
        public event Action Changed;

        public UpgradeTreeConfig Config => config;
        public IReadOnlyCollection<string> OwnedWeaponIds => ownedWeapons;
        public IReadOnlyCollection<string> PurchasedIds => purchased;
        public IReadOnlyCollection<string> CollectedSourceIds => collectedSources;
        public int BonusPoints => bonusPoints;
        public int PointsPerLevel => config != null ? config.pointsPerLevel : 1;
        public int LevelPoints => Mathf.Max(0, (progression != null ? progression.Level : 1) - 1) * PointsPerLevel;
        /// <summary>The tree's starting grant (every profile has it).</summary>
        public int StartingPoints => config != null ? config.startingPoints : 0;
        public int EarnedPoints => StartingPoints + LevelPoints + bonusPoints;
        public int SpentPoints
        {
            get
            {
                int spent = 0;
                foreach (string id in purchased)
                {
                    UpgradeNode node = config != null ? config.Find(id) : null;
                    spent += node != null ? node.cost : 1;
                }
                return spent;
            }
        }
        /// <summary>Points left to spend. Clamped at zero so a developer level preview never goes negative.</summary>
        public int AvailablePoints => Mathf.Max(0, EarnedPoints - SpentPoints);

        public static PlayerUpgrades Ensure(Transform parent = null)
        {
            if (Instance != null) return Instance;
            if (parent != null) return parent.GetComponent<PlayerUpgrades>() ?? parent.gameObject.AddComponent<PlayerUpgrades>();
            var go = new GameObject("Player Upgrades");
            DontDestroyOnLoad(go);
            return go.AddComponent<PlayerUpgrades>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            EnsureConfig();
            progression = PlayerProgression.Instance ?? GetComponent<PlayerProgression>();
            if (progression != null) Attach(progression);
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            if (progression != null)
            {
                progression.Changed -= OnProgressionChanged;
                progression.DetachUpgrades(this);
            }
            Instance = null;
            if (ownsConfig) Destroy(config);
        }

        private void EnsureConfig()
        {
            if (config == null) config = Resources.Load<UpgradeTreeConfig>(UpgradeTreeConfig.ResourceName);
            if (config != null && config.Validate(out _)) return;
            if (config != null)
                Debug.LogWarning("[Upgrades] UpgradeTreeConfig is invalid; using the built-in default tree.", this);
            else
                Debug.LogWarning("[Upgrades] No Resources/UpgradeTreeConfig; using the built-in default tree. Run Tools > JunkLite > Progression > Install Upgrade Tree.", this);
            config = ScriptableObject.CreateInstance<UpgradeTreeConfig>();
            UpgradeTreeConfig.PopulateDefaults(config);
            ownsConfig = true;
        }

        /// <summary>Links to the progression service that owns the save file. Idempotent.</summary>
        internal void Attach(PlayerProgression service)
        {
            if (service == null) return;
            if (progression != null && progression != service) progression.Changed -= OnProgressionChanged;
            progression = service;
            progression.Changed -= OnProgressionChanged;
            progression.Changed += OnProgressionChanged;
            progression.AttachUpgrades(this);
        }

        private void OnProgressionChanged(ProgressionChange change) => Changed?.Invoke();

        // ---------------------------------------------------------------- weapons

        public bool OwnsWeapon(WeaponData weapon) => weapon != null && ownedWeapons.Contains(weapon.ProfileId);

        public void GrantWeapon(WeaponData weapon)
        {
            if (weapon == null || !ownedWeapons.Add(weapon.ProfileId)) return;
            MarkChanged();
        }

        /// <summary>Developer tooling: forget every acquired weapon.</summary>
        public void RevokeWeapons()
        {
            if (ownedWeapons.Count == 0) return;
            ownedWeapons.Clear();
            MarkChanged();
        }

        /// <summary>Profile reset: no purchases, bonus points, weapons or collected point sources. Saved by the progression.</summary>
        internal void ClearProfile()
        {
            purchased.Clear();
            ownedWeapons.Clear();
            collectedSources.Clear();
            bonusPoints = 0;
            MarkChanged();
        }

        // ---------------------------------------------------------------- nodes

        public bool IsOwned(UpgradeNode node) => node != null && purchased.Contains(node.id);
        public bool IsOwned(string nodeId) => !string.IsNullOrEmpty(nodeId) && purchased.Contains(nodeId);

        public UpgradeLockReason GetLockReason(UpgradeNode node)
        {
            if (node == null || config == null || config.Find(node.id) != node) return UpgradeLockReason.Unknown;
            if (purchased.Contains(node.id)) return UpgradeLockReason.Owned;
            if (!config.IsTierAvailable(node.tier)) return UpgradeLockReason.NotInBuild;
            if (node.requiredWeapon != null && !OwnsWeapon(node.requiredWeapon)) return UpgradeLockReason.MissingWeapon;
            if (node.prerequisiteIds != null)
                foreach (string id in node.prerequisiteIds)
                    if (!string.IsNullOrEmpty(id) && !purchased.Contains(id)) return UpgradeLockReason.MissingPrerequisite;
            if (AvailablePoints < node.cost) return UpgradeLockReason.NoPoints;
            return UpgradeLockReason.None;
        }

        public bool CanPurchase(UpgradeNode node, out UpgradeLockReason reason)
        {
            reason = GetLockReason(node);
            return reason == UpgradeLockReason.None;
        }

        public bool TryPurchase(UpgradeNode node)
        {
            if (!CanPurchase(node, out _)) return false;
            purchased.Add(node.id);
            MarkChanged();
            return true;
        }

        /// <summary>Developer tooling: refund every purchase. Weapons and points are kept.</summary>
        public void ResetUpgrades()
        {
            if (purchased.Count == 0) return;
            purchased.Clear();
            MarkChanged();
        }

        // ---------------------------------------------------------------- points

        public void AddBonusPoints(int amount)
        {
            if (amount <= 0) return;
            bonusPoints += amount;
            MarkChanged();
        }

        /// <summary>
        /// Awards points from a one-shot source (boss, pickup, scripted event) once per profile.
        /// Returns false when that source was already collected.
        /// </summary>
        public bool TryCollectPoints(string sourceId, int amount)
        {
            if (amount <= 0) return false;
            if (!string.IsNullOrEmpty(sourceId))
            {
                if (collectedSources.Contains(sourceId)) return false;
                collectedSources.Add(sourceId);
            }
            bonusPoints += amount;
            MarkChanged();
            return true;
        }

        public bool HasCollected(string sourceId) => !string.IsNullOrEmpty(sourceId) && collectedSources.Contains(sourceId);

        // ---------------------------------------------------------------- effect queries (consumers read these)

        /// <summary>Sum of owned stat bonuses: flat added before the additive percentage.</summary>
        public void GetStatBonus(PlayerStat stat, out float flat, out float percent)
        {
            flat = 0f;
            percent = 0f;
            if (config == null) return;
            foreach (string id in purchased)
            {
                UpgradeNode node = config.Find(id);
                if (node?.effects == null) continue;
                foreach (UpgradeEffect effect in node.effects)
                {
                    if (effect.kind != UpgradeEffectKind.StatBonus || effect.stat != stat) continue;
                    flat += effect.flat;
                    percent += effect.percent;
                }
            }
        }

        public bool HasAbility(PlayerAbility ability)
        {
            if (config == null) return false;
            foreach (string id in purchased)
            {
                UpgradeNode node = config.Find(id);
                if (node?.effects == null) continue;
                foreach (UpgradeEffect effect in node.effects)
                    if (effect.kind == UpgradeEffectKind.UnlockAbility && effect.ability == ability) return true;
            }
            return false;
        }

        /// <summary>How many extra mod slots of a kind the owned nodes grant, on top of the player prefab's base slots.</summary>
        public int ExtraModSlots(ModSlotKind kind)
        {
            int count = 0;
            if (config == null) return 0;
            foreach (string id in purchased)
            {
                UpgradeNode node = config.Find(id);
                if (node?.effects == null) continue;
                foreach (UpgradeEffect effect in node.effects)
                    if (effect.kind == UpgradeEffectKind.ModSlot && effect.slotKind == kind) count++;
            }
            return count;
        }

        /// <summary>Owned nodes of a branch, for UI.</summary>
        public void GetBranch(UpgradeBranch branch, List<UpgradeNode> results)
        {
            if (config != null) config.GetBranch(branch, results);
            else results.Clear();
        }

        // ---------------------------------------------------------------- persistence (PlayerProgression owns the file)

        internal void LoadFrom(ProgressionSaveData data)
        {
            ownedWeapons.Clear();
            purchased.Clear();
            collectedSources.Clear();
            bonusPoints = 0;
            if (data != null)
            {
                bonusPoints = Mathf.Max(0, data.bonusUpgradePoints);
                AddAll(ownedWeapons, data.ownedWeapons);
                AddAll(purchased, data.purchasedUpgrades);
                AddAll(collectedSources, data.collectedPointSources);
            }
            Changed?.Invoke();
        }

        internal void WriteTo(ProgressionSaveData data)
        {
            if (data == null) return;
            data.bonusUpgradePoints = bonusPoints;
            data.ownedWeapons = ToArray(ownedWeapons);
            data.purchasedUpgrades = ToArray(purchased);
            data.collectedPointSources = ToArray(collectedSources);
        }

        private static void AddAll(HashSet<string> set, string[] values)
        {
            if (values == null) return;
            foreach (string value in values)
                if (!string.IsNullOrEmpty(value)) set.Add(value);
        }

        private static string[] ToArray(HashSet<string> set)
        {
            var array = new string[set.Count];
            set.CopyTo(array);
            Array.Sort(array, StringComparer.Ordinal);
            return array;
        }

        private void MarkChanged()
        {
            progression?.MarkDirty();
            Changed?.Invoke();
        }
    }
}
