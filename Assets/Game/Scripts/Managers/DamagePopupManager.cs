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

        [Header("Damage Colors")]
        [SerializeField] private Color lowDamageColor = Color.white;
        [SerializeField] private Color mediumDamageColor = Color.yellow;
        [SerializeField] private Color highDamageColor = Color.red;
        [SerializeField] private float mediumDamageThreshold = 20f;
        [SerializeField] private float highDamageThreshold = 50f;

        private readonly Queue<DamagePopup> pool = new();
        private Transform poolRoot;

        // Public accessors for DamagePopup
        public float MoveSpeed => moveSpeed;
        public float Lifetime => lifetime;
        public Color LowDamageColor => lowDamageColor;
        public Color MediumDamageColor => mediumDamageColor;
        public Color HighDamageColor => highDamageColor;
        public float MediumDamageThreshold => mediumDamageThreshold;
        public float HighDamageThreshold => highDamageThreshold;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
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
        public void SpawnPopup(Vector3 position, float damage)
        {
            DamagePopup popup = PreparePopup(position, new Vector2(spawnOffset.x, spawnOffset.y), randomOffsetRange);
            if (popup != null) popup.Setup(damage);
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
            popup.gameObject.SetActive(false);
            popup.transform.SetParent(poolRoot, false);
            pool.Enqueue(popup);
        }
    }
}
