using UnityEngine;

namespace junklite
{
    public abstract class DashAfterimageGhost : MonoBehaviour
    {
        DashAfterimageVFX owner;
        float life, lifeMax;
        AnimationCurve curve;
        bool useUnscaled;

        protected MaterialPropertyBlock mpb;

        public void ConfigureReturn(DashAfterimageVFX vfx) => owner = vfx;

        public void Spawn(Vector3 pos, Quaternion rot, Vector3 scale, float lifetime, AnimationCurve alphaCurve, bool unscaled)
        {
            transform.SetPositionAndRotation(pos, rot);
            transform.localScale = scale;

            life = 0f; lifeMax = Mathf.Max(0.01f, lifetime);
            curve = alphaCurve ?? AnimationCurve.Linear(0, 1, 1, 0);
            useUnscaled = unscaled;

            if (mpb == null) mpb = new MaterialPropertyBlock();
            OnSpawn();
            gameObject.SetActive(true);
        }

        void Update()
        {
            float dt = useUnscaled ? Time.unscaledDeltaTime : Time.deltaTime;
            life += dt;

            float t = Mathf.Clamp01(life / lifeMax);
            float a = Mathf.Clamp01(curve.Evaluate(t));

            ApplyAlpha(a);

            if (life >= lifeMax)
                ForceDespawnToPool();
        }

        public void ForceDespawnToPool()
        {
            OnDespawn();
            owner?.ReturnToPool(this);
        }

        protected abstract void ApplyAlpha(float a);
        protected abstract void OnSpawn();
        protected abstract void OnDespawn();
    }

    // ---- Sprite variant ----
    public class DashAfterimageGhost_Sprite : DashAfterimageGhost
    {
        public SpriteRenderer Renderer { get; private set; }

        public void Init(SpriteRenderer sr) { Renderer = sr; }

        protected override void OnSpawn()
        {
            // nothing extra; color is set in manager via MPB
        }

        protected override void ApplyAlpha(float a)
        {
            if (!Renderer) return;
            Renderer.GetPropertyBlock(mpb);
            // Multiplicative alpha over tint set by manager
            if (mpb.HasColor("_BaseColor"))
            {
                var c = mpb.GetColor("_BaseColor");
                c.a = a;
                mpb.SetColor("_BaseColor", c);
            }
            if (mpb.HasColor("_Color"))
            {
                var c2 = mpb.GetColor("_Color");
                c2.a = a;
                mpb.SetColor("_Color", c2);
            }
            Renderer.SetPropertyBlock(mpb);
        }

        protected override void OnDespawn()
        {
            // no-op
        }
    }

    // ---- Skinned mesh (baked) variant ----
    public class DashAfterimageGhost_Mesh : DashAfterimageGhost
    {
        public MeshFilter MeshFilter { get; private set; }
        public MeshRenderer MeshRenderer { get; private set; }

        public void Init(MeshFilter mf, MeshRenderer mr) { MeshFilter = mf; MeshRenderer = mr; }

        protected override void OnSpawn()
        {
            // nothing extra
        }

        protected override void ApplyAlpha(float a)
        {
            if (!MeshRenderer) return;
            MeshRenderer.GetPropertyBlock(mpb);

            if (mpb.HasColor("_BaseColor"))
            {
                var c = mpb.GetColor("_BaseColor");
                c.a = a;
                mpb.SetColor("_BaseColor", c);
            }
            if (mpb.HasColor("_Color"))
            {
                var c2 = mpb.GetColor("_Color");
                c2.a = a;
                mpb.SetColor("_Color", c2);
            }
            MeshRenderer.SetPropertyBlock(mpb);
        }

        protected override void OnDespawn()
        {
            if (MeshFilter) MeshFilter.sharedMesh = null; // release baked ref
        }
    }
}
