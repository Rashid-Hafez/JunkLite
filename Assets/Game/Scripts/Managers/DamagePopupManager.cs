using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Centralized manager for spawning damage popups with object pooling.
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
            if (popupPrefab == null)
                return;

            DamagePopup popup = GetPopup();

            // Position with offset and randomness

            popup.transform.rotation = CinemachineCore.GetVirtualCamera(0).transform.rotation; // Face camera
            Vector3 spawnPos = position + popup.transform.right * spawnOffset.x + popup.transform.up * spawnOffset.y; // Offset relative to local right and up
            spawnPos += popup.transform.right * Random.Range(-randomOffsetRange, randomOffsetRange); // Random horizontal offset
            popup.transform.position = spawnPos; 
            popup.gameObject.SetActive(true);
            popup.Setup(damage);
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