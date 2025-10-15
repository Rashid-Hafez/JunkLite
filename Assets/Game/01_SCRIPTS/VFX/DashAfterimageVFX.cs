using System.Collections.Generic;
using UnityEngine;

namespace junklite
{
    public class DashAfterimageVFX : MonoBehaviour
    {
        public enum RenderMode { SpriteRenderer2D, SkinnedMesh3D }

        [Header("Hookups")]
        [SerializeField] private Character2D5Controller controller; // subscribe to OnDashStarted/Ended
        [SerializeField] private Transform sampleFrom;               // typically the player root
        [SerializeField] private RenderMode renderMode = RenderMode.SpriteRenderer2D;

        [Tooltip("Only for Sprite mode")]
        [SerializeField] private SpriteRenderer sourceSprite;

        [Tooltip("Only for SkinnedMesh mode")]
        [SerializeField] private SkinnedMeshRenderer sourceSkinnedMesh;

        [Header("Spawn")]
        [SerializeField, Min(0.01f)] private float spawnInterval = 0.035f;
        [SerializeField] private int poolSize = 24;
        [SerializeField] private int maxPoolSize = 64; // hard cap

        [Header("Visuals")]
        [SerializeField] private Color tint = new Color(1f, 1f, 1f, 0.6f);
        [SerializeField] private float lifetime = 0.25f;
        [SerializeField] private AnimationCurve alphaCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);
        [SerializeField] private Vector3 scaleMultiplier = Vector3.one;

        [Header("Render Options")]
        [SerializeField] private bool inheritSortingLayer = true; // sprite
        [SerializeField] private string sortingLayerName = "Default";
        [SerializeField] private int sortingOrderOffset = -2;
        [SerializeField] private Material overrideMaterial; // optional Unlit/Transparent for both modes

        [Header("Perf")]
        [SerializeField] private bool unscaledTime = false; // true = ignore timescale
        [SerializeField] private bool bakeNormalsAndTangents = false; // 3D

        float spawnTimer;
        readonly Queue<DashAfterimageGhost> pool = new Queue<DashAfterimageGhost>();
        readonly List<DashAfterimageGhost> active = new List<DashAfterimageGhost>();

        Mesh bakedMesh;
        MaterialPropertyBlock mpb;

        void Awake()
        {
            if (!controller) controller = GetComponentInParent<Character2D5Controller>();
            if (!sampleFrom) sampleFrom = controller ? controller.transform : transform.parent;

            if (renderMode == RenderMode.SpriteRenderer2D && !sourceSprite)
                sourceSprite = sampleFrom.GetComponentInChildren<SpriteRenderer>();

            if (renderMode == RenderMode.SkinnedMesh3D && !sourceSkinnedMesh)
                sourceSkinnedMesh = sampleFrom.GetComponentInChildren<SkinnedMeshRenderer>();

            mpb = new MaterialPropertyBlock();
            if (renderMode == RenderMode.SkinnedMesh3D)
                bakedMesh = new Mesh { name = "DashAfterimage_BakedMesh", indexFormat = UnityEngine.Rendering.IndexFormat.UInt16 };

            PrewarmPool(poolSize);
        }

        void OnEnable()
        {
            if (controller != null)
            {
                controller.OnDashStarted += HandleDashStart;
                controller.OnDashEnded += HandleDashEnd;
            }
        }

        void OnDisable()
        {
            if (controller != null)
            {
                controller.OnDashStarted -= HandleDashStart;
                controller.OnDashEnded -= HandleDashEnd;
            }
            // Ensure all ghosts return to pool
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (active[i]) active[i].ForceDespawnToPool();
            }
            active.Clear();
        }

        void Update()
        {
            // While dashing, spawn with fixed cadence
            if (controller != null && controller.IsDashing)
            {
                float dt = unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
                spawnTimer += dt;
                if (spawnTimer >= spawnInterval)
                {
                    spawnTimer = 0f;
                    SpawnGhost();
                }
            }
        }

        void HandleDashStart()
        {
            spawnTimer = 0f; // immediate spawn on dash start feels snappy
            SpawnGhost();
        }

        void HandleDashEnd()
        {
            // no-op; ghosts fade out by themselves
        }

        void PrewarmPool(int count)
        {
            count = Mathf.Min(Mathf.Max(1, count), maxPoolSize);
            for (int i = 0; i < count; i++)
                pool.Enqueue(CreateGhostGO());
        }

        DashAfterimageGhost CreateGhostGO()
        {
            var go = new GameObject("AfterimageGhost");
            go.transform.SetParent(null, false);
            go.SetActive(false);

            DashAfterimageGhost ghost = null;

            switch (renderMode)
            {
                case RenderMode.SpriteRenderer2D:
                    {
                        var sr = go.AddComponent<SpriteRenderer>();
                        sr.enabled = true;
                        ghost = go.AddComponent<DashAfterimageGhost_Sprite>();
                        ((DashAfterimageGhost_Sprite)ghost).Init(sr);
                    }
                    break;

                case RenderMode.SkinnedMesh3D:
                    {
                        var mf = go.AddComponent<MeshFilter>();
                        var mr = go.AddComponent<MeshRenderer>();
                        ghost = go.AddComponent<DashAfterimageGhost_Mesh>();
                        ((DashAfterimageGhost_Mesh)ghost).Init(mf, mr);
                    }
                    break;
            }

            ghost.gameObject.layer = gameObject.layer;
            ghost.ConfigureReturn(this);
            return ghost;
        }

        void SpawnGhost()
        {
            if (!sampleFrom) return;

            // pull from pool
            var ghost = (pool.Count > 0) ? pool.Dequeue() : (active.Count + pool.Count < maxPoolSize ? CreateGhostGO() : null);
            if (ghost == null) return;

            // common transform snapshot
            var pos = sampleFrom.position;
            var rot = sampleFrom.rotation;
            var scl = Vector3.Scale(sampleFrom.lossyScale, scaleMultiplier);

            // per-mode payload
            switch (renderMode)
            {
                case RenderMode.SpriteRenderer2D:
                    {
                        if (!sourceSprite || sourceSprite.sprite == null) { ReturnToPool(ghost); return; }

                        var g = ghost as DashAfterimageGhost_Sprite;
                        var sr = g.Renderer;

                        // sorting layer/order
                        if (inheritSortingLayer)
                        {
                            sr.sortingLayerID = sourceSprite.sortingLayerID;
                            sr.sortingOrder = sourceSprite.sortingOrder + sortingOrderOffset;
                        }
                        else
                        {
                            sr.sortingLayerName = sortingLayerName;
                            sr.sortingOrder = sortingOrderOffset;
                        }

                        // copy sprite & flip
                        sr.sprite = sourceSprite.sprite;
                        sr.flipX = sourceSprite.flipX;

                        // material
                        if (overrideMaterial) sr.sharedMaterial = overrideMaterial;

                        // color via MPB for alpha fades
                        sr.GetPropertyBlock(mpb);
                        mpb.SetColor("_BaseColor", tint); // URP Sprite/Lit or Unlit uses _BaseColor/_Color (both common)
                        mpb.SetColor("_Color", tint);
                        sr.SetPropertyBlock(mpb);

                        g.Spawn(pos, rot, scl, lifetime, alphaCurve, unscaledTime);
                    }
                    break;

                case RenderMode.SkinnedMesh3D:
                    {
                        if (!sourceSkinnedMesh) { ReturnToPool(ghost); return; }

                        var g = ghost as DashAfterimageGhost_Mesh;
                        var mf = g.MeshFilter;
                        var mr = g.MeshRenderer;

                        // bake mesh snapshot
                        if (bakedMesh == null) bakedMesh = new Mesh();
                        sourceSkinnedMesh.BakeMesh(bakedMesh, bakeNormalsAndTangents);

                        mf.sharedMesh = bakedMesh;

                        // materials (share original to avoid instancing) – color via MPB
                        if (overrideMaterial)
                            mr.sharedMaterial = overrideMaterial;
                        else
                            mr.sharedMaterials = sourceSkinnedMesh.sharedMaterials;

                        // color on all submaterials
                        mr.GetPropertyBlock(mpb);
                        mpb.SetColor("_BaseColor", tint);
                        mpb.SetColor("_Color", tint);
                        mr.SetPropertyBlock(mpb);

                        g.Spawn(pos, rot, scl, lifetime, alphaCurve, unscaledTime);
                    }
                    break;
            }

            active.Add(ghost);
        }

        // called by ghosts when they finish fading
        internal void ReturnToPool(DashAfterimageGhost ghost)
        {
            if (!ghost) return;
            ghost.gameObject.SetActive(false);
            if (!pool.Contains(ghost))
                pool.Enqueue(ghost);
            active.Remove(ghost);
        }
    }
}
