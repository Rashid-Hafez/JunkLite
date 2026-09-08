#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Editor-only command source for the in-game pause console. Keeping all asset-database
    /// lookups in this compilation block guarantees that none of the development tooling or
    /// prefab paths are included in a player build.
    /// </summary>
    internal sealed class PauseMenuDeveloperTools
    {
        internal readonly struct ToolAction
        {
            public ToolAction(string label, Func<string> execute)
            {
                Label = label;
                Execute = execute;
            }

            public string Label { get; }
            public Func<string> Execute { get; }
        }

        private readonly List<ToolAction> enemyActions = new();
        private readonly List<ToolAction> weaponActions = new();
        private readonly List<ToolAction> modActions = new();
        private readonly List<ToolAction> playerActions = new();
        private readonly List<ToolAction> runActions = new();

        private int spawnSequence;
        private bool enemyAiPaused;

        public IReadOnlyList<ToolAction> EnemyActions => enemyActions;
        public IReadOnlyList<ToolAction> WeaponActions => weaponActions;
        public IReadOnlyList<ToolAction> ModActions => modActions;
        public IReadOnlyList<ToolAction> PlayerActions => playerActions;
        public IReadOnlyList<ToolAction> RunActions => runActions;

        public PauseMenuDeveloperTools()
        {
            BuildEnemyActions();
            BuildWeaponActions();
            BuildModActions();
            BuildPlayerActions();
            BuildRunActions();
        }

        private void BuildEnemyActions()
        {
            AddEnemy("GRUNT", "Assets/Game/Prefabs/Enemies/Grunt Enemy.prefab");
            AddEnemy("HYENA", "Assets/Game/Prefabs/Enemies/Hyena.prefab");
            AddEnemy("HYENA / EASY", "Assets/Game/Prefabs/Enemies/Hyena EASY.prefab");
            AddEnemy("HYENA / BLUE", "Assets/Game/Prefabs/Enemies/Hyena Blue.prefab");
            AddEnemy("HYENA / GREEN", "Assets/Game/Prefabs/Enemies/Hyena Green.prefab");
            AddEnemy("ROBOT", "Assets/Game/Prefabs/Enemies/Robot Enemy.prefab");
            AddEnemy("FLYING DUMMY", "Assets/Game/Prefabs/Enemies/Flying Dummy.prefab");
            AddEnemy("TRAINING DUMMY", "Assets/Game/Prefabs/Enemies/Dummy.prefab");
        }

        private void AddEnemy(string label, string assetPath)
        {
            enemyActions.Add(new ToolAction(label, () => SpawnEnemy(label, assetPath)));
        }

        private void BuildWeaponActions()
        {
            AddWeapon("SWORD", "Assets/Game/Prefabs/New Weapons/sword prefab (2).prefab");
            AddWeapon("GUN", "Assets/Game/Prefabs/New Weapons/Gun Prefab 1.prefab");
        }

        private void AddWeapon(string label, string assetPath)
        {
            weaponActions.Add(new ToolAction(label, () => SpawnWeapon(label, assetPath)));
        }

        private void BuildModActions()
        {
            string[] guids = AssetDatabase.FindAssets(
                "t:ModData",
                new[] { "Assets/Game/New Prefabs/Mods" });

            var mods = new List<ModData>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in guids
                         .Select(AssetDatabase.GUIDToAssetPath)
                         .Where(path => !path.Contains("tutorial", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                ModData mod = AssetDatabase.LoadAssetAtPath<ModData>(path);
                if (mod == null)
                    continue;

                string displayName = GetModName(mod);
                if (seenNames.Add(displayName))
                    mods.Add(mod);
            }

            foreach (ModData mod in mods
                         .OrderBy(data => data is ActiveModData ? 0 : 1)
                         .ThenBy(GetModName, StringComparer.OrdinalIgnoreCase))
            {
                ModData captured = mod;
                string kind = captured is ActiveModData ? "A" : "P";
                modActions.Add(new ToolAction(
                    $"[{kind}] {GetModName(captured)}",
                    () => SpawnMod(captured)));
            }
        }

        private void BuildPlayerActions()
        {
            playerActions.Add(new ToolAction("HEAL TO FULL", HealPlayer));
            playerActions.Add(new ToolAction("TOGGLE INVINCIBLE", ToggleInvincibility));
            playerActions.Add(new ToolAction("UNLOCK MOD SLOTS", UnlockModSlots));
            playerActions.Add(new ToolAction("CLEAR INVENTORY", ClearInventory));
            playerActions.Add(new ToolAction("DEFEAT PLAYER", DefeatPlayer));
            playerActions.Add(new ToolAction("RESPAWN AT ENTRY", RespawnPlayer));
        }

        private void BuildRunActions()
        {
            runActions.Add(new ToolAction("TOGGLE ENEMY AI", ToggleEnemyAi));
            runActions.Add(new ToolAction("DEFEAT ALL ENEMIES", DefeatAllEnemies));
            runActions.Add(new ToolAction("CLEAR DEV SPAWNS", ClearDeveloperSpawns));
            runActions.Add(new ToolAction("RESUME / 0.25X", () => ResumeAtSpeed(0.25f)));
            runActions.Add(new ToolAction("RESUME / 1.00X", () => ResumeAtSpeed(1f)));
            runActions.Add(new ToolAction("RESTART SECTOR", RestartSector));
            runActions.Add(new ToolAction("RESET GAME", ResetGame));
        }

        private string SpawnEnemy(string label, string assetPath)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
                return $"Missing prefab: {label}";

            GameObject spawned = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (spawned == null)
                return $"Could not spawn {label}";

            spawned.name = $"[DEV] {prefab.name}";
            spawned.transform.SetPositionAndRotation(GetSpawnPosition(), prefab.transform.rotation);
            if (enemyAiPaused)
                spawned.GetComponent<EnemyCharacter>()?.StateMachine?.Pause();
            return $"Spawned {label}";
        }

        private string SpawnMod(ModData mod)
        {
            if (mod == null)
                return "Mod asset is missing";

            const string pickupPath = "Assets/Game/Prefabs/Mods/Mod pick up prefab.prefab";
            GameObject pickupPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(pickupPath);
            if (pickupPrefab != null)
            {
                GameObject spawned = PrefabUtility.InstantiatePrefab(pickupPrefab) as GameObject;
                if (spawned != null)
                {
                    spawned.name = $"[DEV] MOD / {GetModName(mod)}";
                    spawned.transform.SetPositionAndRotation(GetSpawnPosition(), pickupPrefab.transform.rotation);
                    WorldModPickup pickup = spawned.GetComponent<WorldModPickup>();
                    if (pickup != null)
                        pickup.modData = mod;
                    return $"Spawned {GetModName(mod)} pickup";
                }
            }

            PlayerCharacter player = GetPlayer();
            InventoryComponent inventory = player != null
                ? player.GetComponent<InventoryComponent>()
                : null;
            if (inventory != null && inventory.AddMod(mod))
                return $"Granted {GetModName(mod)} (no drop manager)";

            return "No drop manager or inventory available";
        }

        private string SpawnWeapon(string label, string assetPath)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
                return $"Missing weapon prefab: {label}";

            GameObject spawned = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (spawned == null)
                return $"Could not spawn {label}";

            spawned.name = $"[DEV] WEAPON / {label}";
            spawned.transform.SetPositionAndRotation(GetSpawnPosition(), prefab.transform.rotation);
            return $"Spawned {label} pickup";
        }

        private Vector3 GetSpawnPosition()
        {
            PlayerCharacter player = GetPlayer();
            Vector3 playerPosition = player != null ? player.transform.position : Vector3.zero;

            int lane = spawnSequence % 3;
            float side = (spawnSequence / 3) % 2 == 0 ? 1f : -1f;
            spawnSequence++;

            Vector3 candidate = playerPosition + Vector3.right * side * (4f + lane * 1.75f);
            Vector3 rayOrigin = candidate + Vector3.up * 8f;
            if (Physics.Raycast(
                    rayOrigin,
                    Vector3.down,
                    out RaycastHit hit,
                    30f,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore))
            {
                candidate.y = hit.point.y + 0.05f;
            }

            return candidate;
        }

        private static string HealPlayer()
        {
            PlayerCharacter player = GetPlayer();
            if (player?.Health == null)
                return "Player health is unavailable";

            player.Health.SetToMax();
            return "Player health restored";
        }

        private static string ToggleInvincibility()
        {
            PlayerState state = GetPlayer()?.PlayerState;
            if (state == null)
                return "Player state is unavailable";

            state.SetInvincible(!state.IsInvincible);
            return state.IsInvincible ? "Invincibility enabled" : "Invincibility disabled";
        }

        private static string UnlockModSlots()
        {
            ModManager manager = GetPlayer()?.GetComponent<ModManager>();
            if (manager == null)
                return "Mod manager is unavailable";

            while (manager.UnlockedActiveSlots < manager.MaxActiveSlots)
                manager.UnlockActiveSlot();
            while (manager.UnlockedPassiveSlots < manager.MaxPassiveSlots)
                manager.UnlockPassiveSlot();

            return "All mod slots unlocked";
        }

        private static string ClearInventory()
        {
            InventoryComponent inventory = GetPlayer()?.GetComponent<InventoryComponent>();
            if (inventory == null)
                return "Inventory is unavailable";

            int removed = 0;
            for (int i = inventory.SlotCount - 1; i >= 0; i--)
            {
                if (inventory.RemoveModAt(i) != null)
                    removed++;
            }

            return $"Removed {removed} inventory mods";
        }

        private static string DefeatPlayer()
        {
            PlayerCharacter player = GetPlayer();
            if (player == null)
                return "Player is unavailable";

            player.Kill();
            return "Player defeated";
        }

        private static string RespawnPlayer()
        {
            if (GameManager.Instance == null)
                return "Game manager is unavailable";

            GameManager.Instance.RestartLevel();
            return "Player reset to entry";
        }

        private string ToggleEnemyAi()
        {
            enemyAiPaused = !enemyAiPaused;
            EnemyCharacter[] enemies = UnityEngine.Object.FindObjectsByType<EnemyCharacter>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            int changed = 0;
            foreach (EnemyCharacter enemy in enemies)
            {
                if (enemy == null || enemy.StateMachine == null)
                    continue;

                if (enemyAiPaused)
                    enemy.StateMachine.Pause();
                else
                    enemy.StateMachine.Resume();
                changed++;
            }

            return enemyAiPaused
                ? $"Paused AI on {changed} enemies"
                : $"Resumed AI on {changed} enemies";
        }

        private static string DefeatAllEnemies()
        {
            EnemyCharacter[] enemies = UnityEngine.Object.FindObjectsByType<EnemyCharacter>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            int defeated = 0;
            foreach (EnemyCharacter enemy in enemies)
            {
                if (enemy == null || !enemy.IsAlive || enemy.Health == null)
                    continue;

                enemy.ReceiveDamage(DamageRequest.Forced(enemy.Health.Current));
                defeated++;
            }

            return $"Defeated {defeated} enemies";
        }

        private static string ClearDeveloperSpawns()
        {
            GameObject[] sceneObjects = UnityEngine.Object.FindObjectsByType<GameObject>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            int removed = 0;
            foreach (GameObject sceneObject in sceneObjects)
            {
                if (sceneObject == null || !sceneObject.name.StartsWith("[DEV] ", StringComparison.Ordinal))
                    continue;

                UnityEngine.Object.Destroy(sceneObject);
                removed++;
            }

            return $"Cleared {removed} developer spawns";
        }

        private static string ResumeAtSpeed(float speed)
        {
            if (GameManager.Instance == null)
                return "Game manager is unavailable";

            GameManager.Instance.ResumeGame();
            Time.timeScale = Mathf.Clamp(speed, 0.01f, 4f);
            return $"Simulation resumed at {speed:0.00}x";
        }

        private static string RestartSector()
        {
            if (GameManager.Instance == null)
                return "Game manager is unavailable";

            GameManager.Instance.RestartCurrentScene();
            return "Restarting sector";
        }

        private static string ResetGame()
        {
            if (GameManager.Instance == null)
                return "Game manager is unavailable";

            GameManager.Instance.RestartGame();
            return "Resetting game";
        }

        private static PlayerCharacter GetPlayer()
        {
            if (PlayerLifecycle.Instance != null && PlayerLifecycle.Instance.Player != null)
                return PlayerLifecycle.Instance.Player;

            return UnityEngine.Object.FindFirstObjectByType<PlayerCharacter>();
        }

        private static string GetModName(ModData mod)
        {
            if (mod == null)
                return "UNKNOWN MOD";
            return string.IsNullOrWhiteSpace(mod.modName) ? mod.name : mod.modName;
        }
    }
}
#endif
