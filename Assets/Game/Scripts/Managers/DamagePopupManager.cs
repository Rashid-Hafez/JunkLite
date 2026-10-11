using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Centralized pool for damage numbers and player XP rewards.
    /// </summary>
    public class DamagePopupManager : MonoBehaviour
    {
        public static DamagePopupManager Instance { get; private set; }

        [Header("Prefab & Pool")]
        [SerializeField] private GameObject popupPrefab;
        [SerializeField] private int poolSize = 10;

        [Header("Spawn Settings")]
        [SerializeField] private Vector3 spawnOffset = new Vector3(0f, 1f, 0f);
        [SerializeField] private float randomOffsetRange = 0.3f;

        [Header("Popup Behavior")]
        [SerializeField] private float moveSpeed = 1f;
        [SerializeField] private float lifetime = 1f;

        [Header("Damage Type Colors")]
        [SerializeField] private Color physicalDamageColor = new Color32(235, 244, 250, 255);
        [SerializeField] private Color fireDamageColor = new Color32(255, 137, 57, 255);
        [SerializeField] private Color electricDamageColor = new Color32(76, 172, 255, 255);
        [SerializeField] private Color magicDamageColor = new Color32(194, 137, 255, 255);

        [Header("Damage Typography & Motion")]
        [Tooltip("Visible digit height at 1080p. Scales with the camera viewport, including camera blends.")]
        [SerializeField, Range(16f, 48f)] private float damageTextHeight = 28f;
        [SerializeField, Range(0.5f, 1f)] private float tickScale = 0.78f;
        [Tooltip("Heavy hits grow instead of changing their damage-type color.")]
        [SerializeField] private float mediumDamageThreshold = 20f;
        [SerializeField] private float highDamageThreshold = 50f;
        [SerializeField, Min(0f)] private float horizontalTravel = 0.3f;
        [Tooltip("Separates successive hits even when Random Offset Range is zero.")]
        [SerializeField, Min(0f)] private float hitSeparation = 0.22f;

        private readonly Queue<DamagePopup> pool = new();
        private Transform poolRoot;
        private int nextHitLane;

        // Public accessors for DamagePopup
        public float MoveSpeed => moveSpeed;
        public float Lifetime => lifetime;
        public float DamageTextHeight => damageTextHeight;

        public Color GetDamageColor(DamageType type) => type switch
        {
            DamageType.Fire => fireDamageColor,
            DamageType.Electric => electricDamageColor,
            DamageType.Magic => magicDamageColor,
            _ => physicalDamageColor
        };

        public float GetDamageScale(float damage, bool isTickDamage) => isTickDamage ? tickScale
            : damage >= highDamageThreshold ? 1.28f
            : damage >= mediumDamageThreshold ? 1.12f : 1f;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            InitializePool();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void InitializePool()
        {
            if (popupPrefab == null)
                return;

            var poolObj = new GameObject("DamagePopupPool");
            poolObj.transform.SetParent(transform);
            poolRoot = poolObj.transform;

            for (int i = 0; i < poolSize; i++)
            {
                GameObject go = Instantiate(popupPrefab, poolRoot);
                go.SetActive(false);

                var popup = go.GetComponent<DamagePopup>();
                if (popup != null)
                    pool.Enqueue(popup);
            }
        }

        /// <summary>
        /// Spawn a damage popup at position.
        /// </summary>
        public void SpawnPopup(Vector3 position, float damage, DamageType type = DamageType.Physical,
            bool isTickDamage = false)
        {
            if (damage <= 0f || float.IsNaN(damage) || float.IsInfinity(damage)) return;
            // Center, left, right: a bounded fan keeps repeated hits from occupying the same position.
            int lane = nextHitLane == 0 ? 0 : nextHitLane == 1 ? -1 : 1;
            nextHitLane = (nextHitLane + 1) % 3;
            Vector2 offset = new Vector2(spawnOffset.x + lane * hitSeparation, spawnOffset.y);
            DamagePopup popup = PreparePopup(position, offset, randomOffsetRange);
            if (popup != null) popup.Setup(damage, type, isTickDamage, lane * horizontalTravel);
        }

        public void SpawnExperiencePopup(Vector3 position, long amount, PlayerProgressionConfig config)
        {
            if (amount <= 0 || config == null || !config.showXpPopups) return;
            DamagePopup popup = PreparePopup(position, config.xpPopupOffset, config.xpPopupHorizontalSpread);
            if (popup != null) popup.SetupExperience(amount, config);
        }

        private DamagePopup PreparePopup(Vector3 position, Vector2 offset, float spread)
        {
            if (popupPrefab == null) return null;
            DamagePopup popup = GetPopup();
            if (popup == null) return null;

            // The rendering camera includes Cinemachine blends; some test scenes have no virtual camera.
            Camera camera = Camera.main;
            var virtualCamera = camera == null && CinemachineCore.VirtualCameraCount > 0
                ? CinemachineCore.GetVirtualCamera(0) : null;
            popup.transform.rotation = camera != null ? camera.transform.rotation
                : virtualCamera != null ? virtualCamera.transform.rotation : Quaternion.identity;
            popup.transform.position = position + popup.transform.right * (offset.x + Random.Range(-spread, spread))
                + popup.transform.up * offset.y;
            popup.Bind(this, camera);
            popup.gameObject.SetActive(true);
            return popup;
        }

        private DamagePopup GetPopup()
        {
            if (pool.Count > 0)
                return pool.Dequeue();

            // Pool exhausted, create new
            GameObject go = Instantiate(popupPrefab, poolRoot);
            return go.GetComponent<DamagePopup>();
        }

        public void ReturnPopup(DamagePopup popup)
        {
            if (popup == null || !popup.gameObject.activeSelf) return;
            popup.gameObject.SetActive(false);
            popup.transform.SetParent(poolRoot, false);
            pool.Enqueue(popup);
        }
    }
}
