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

        private static readonly float[] SpawnSearchOffsets = { 0f, -1.5f, 1.5f, -3f, 3f };

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

            if (!TryGetSpawnPlacement(out SpawnPlacement placement))
                return "No ground found near the player";

            GameObject spawned = InstantiateForCurrentPlane(prefab, placement);
            spawned.name = $"[DEV] {prefab.name}";
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
                if (!TryGetSpawnPlacement(out SpawnPlacement placement))
                    return "No ground found near the player";

                GameObject spawned = InstantiateForCurrentPlane(pickupPrefab, placement);
                spawned.name = $"[DEV] MOD / {GetModName(mod)}";
                WorldModPickup pickup = spawned.GetComponent<WorldModPickup>();
                if (pickup != null)
                    pickup.modData = mod;
                return $"Spawned {GetModName(mod)} pickup";
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

            if (!TryGetSpawnPlacement(out SpawnPlacement placement))
                return "No ground found near the player";

            GameObject spawned = InstantiateForCurrentPlane(prefab, placement);
            spawned.name = $"[DEV] WEAPON / {label}";
            return $"Spawned {label} pickup";
        }

        private bool TryGetSpawnPlacement(out SpawnPlacement placement)
        {
            PlayerCharacter player = GetPlayer();
            if (player == null)
            {
                placement = default;
                return false;
            }

            Character2D5Controller controller = player.Controller;
            Vector3 movementAxis = controller != null
                ? controller.MovementAxis
                : player.transform.right;
            movementAxis.y = 0f;
            if (movementAxis.sqrMagnitude < 0.01f)
                movementAxis = Vector3.right;
            movementAxis = SnapToHorizontalAxis(movementAxis);

            int groundMask = controller != null && controller.GroundLayerMask.value != 0
                ? controller.GroundLayerMask.value
                : LayerMask.GetMask("Default", "Ground", "Wall");

            int lane = spawnSequence % 3;
            float side = (spawnSequence / 3) % 2 == 0 ? 1f : -1f;
            spawnSequence++;

            float preferredDistance = 4f + lane * 1.75f;
            for (int sidePass = 0; sidePass < 2; sidePass++)
            {
                float searchSide = sidePass == 0 ? side : -side;
                foreach (float offset in SpawnSearchOffsets)
                {
                    float distance = Mathf.Max(1.5f, preferredDistance + offset);
                    Vector3 candidate = player.transform.position +
                                        movementAxis * (searchSide * distance);
                    if (!TryFindGround(candidate, player.transform.position.y, groundMask,
                            out Vector3 groundPoint))
                    {
                        continue;
                    }

                    Quaternion planeRotation = Quaternion.LookRotation(
                        Vector3.Cross(movementAxis, Vector3.up),
                        Vector3.up);
                    placement = new SpawnPlacement(groundPoint, planeRotation);
                    return true;
                }
            }

            placement = default;
            return false;
        }

        private static bool TryFindGround(
            Vector3 candidate,
            float playerHeight,
            int groundMask,
            out Vector3 groundPoint)
        {
            Vector3 rayOrigin = candidate + Vector3.up * 10f;
            RaycastHit[] hits = Physics.RaycastAll(
                rayOrigin,
                Vector3.down,
                40f,
                groundMask,
                QueryTriggerInteraction.Ignore);

            bool found = false;
            float closestHeight = float.PositiveInfinity;
            groundPoint = default;
            foreach (RaycastHit hit in hits)
            {
                if (hit.normal.y < 0.55f)
                    continue;

                float heightDifference = Mathf.Abs(hit.point.y - playerHeight);
                if (heightDifference >= closestHeight)
                    continue;

                closestHeight = heightDifference;
                groundPoint = hit.point;
                found = true;
            }

            return found;
        }

        private static GameObject InstantiateForCurrentPlane(
            GameObject prefab,
            SpawnPlacement placement)
        {
            Vector3 prefabEuler = prefab.transform.eulerAngles;
            Quaternion rotation = Quaternion.Euler(
                prefabEuler.x,
                placement.PlaneRotation.eulerAngles.y,
                prefabEuler.z);

            // Position and rotation are supplied to Instantiate so axis-sensitive Awake
            // methods cache the correct XY or ZY movement plane on their first call.
            GameObject spawned = UnityEngine.Object.Instantiate(
                prefab,
                placement.GroundPoint + Vector3.up * 4f,
                rotation);
            PlaceOnGround(spawned, placement.GroundPoint.y);
            return spawned;
        }

        private static void PlaceOnGround(GameObject spawned, float groundHeight)
        {
            float lowestSolidPoint = float.PositiveInfinity;
            foreach (Collider collider in spawned.GetComponentsInChildren<Collider>(true))
            {
                if (!collider.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy)
                    continue;

                lowestSolidPoint = Mathf.Min(lowestSolidPoint, collider.bounds.min.y);
            }

            const float surfaceClearance = 0.08f;
            if (float.IsPositiveInfinity(lowestSolidPoint))
            {
                Vector3 position = spawned.transform.position;
                position.y = groundHeight + surfaceClearance;
                spawned.transform.position = position;
                return;
            }

            float verticalAdjustment = groundHeight + surfaceClearance - lowestSolidPoint;
            spawned.transform.position += Vector3.up * verticalAdjustment;
        }

        private static Vector3 SnapToHorizontalAxis(Vector3 direction)
        {
            return Mathf.Abs(direction.x) >= Mathf.Abs(direction.z)
                ? new Vector3(Mathf.Sign(direction.x), 0f, 0f)
                : new Vector3(0f, 0f, Mathf.Sign(direction.z));
        }

        private readonly struct SpawnPlacement
        {
            public SpawnPlacement(Vector3 groundPoint, Quaternion planeRotation)
            {
                GroundPoint = groundPoint;
                PlaneRotation = planeRotation;
            }

            public Vector3 GroundPoint { get; }
            public Quaternion PlaneRotation { get; }
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
