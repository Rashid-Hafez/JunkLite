using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace junklite.Tests
{
    public sealed class PulseBarrierVisualTests
    {
        private GameObject root;
        private DamageShield shield;
        private PulseBarrierVisual visual;
        private PulseBarrierVisualProfile profile;
        private Transform facing;
        private Renderer Arcs => root.transform.Find("Pulse Arcs (Runtime)").GetComponent<Renderer>();

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Pulse Barrier Test") { hideFlags = HideFlags.DontSave };
            root.AddComponent<CapsuleCollider>().height = 2f;
            shield = root.AddComponent<DamageShield>();
            visual = root.AddComponent<PulseBarrierVisual>();
            facing = new GameObject("Body").transform; facing.SetParent(root.transform, false);
            profile = ScriptableObject.CreateInstance<PulseBarrierVisualProfile>();
            profile.arcMaterial = new Material(Shader.Find("JunkLite/VFX/Pulse Barrier Arcs"));
            Start();
        }

        private void Start()
        {
            shield.Activate(100f, 10f);
            Assert.That(visual.Show(shield, profile, facing, Vector3.up), Is.True);
        }

        [TearDown]
        public void TearDown()
        {
            // Unstarted EditMode actors do not reliably dispatch their destruction callbacks.
            if (visual != null) { Call(visual, "OnDisable"); Call(visual, "OnDestroy"); }
            Object.DestroyImmediate(root);
            if (profile != null)
            {
                Object.DestroyImmediate(profile.arcMaterial);
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void AbsorbedDamageUpdatesPulseAndHealthWithoutChangingDamageRules()
        {
            Assert.That(shield.Absorb(35f), Is.Zero);
            Call(visual, "LateUpdate");
            var block = new MaterialPropertyBlock(); Arcs.GetPropertyBlock(block);
            Assert.That(block.GetVector("_ArcState").x, Is.EqualTo(0.65f).Within(0.001f));
            Assert.That(block.GetVector("_ArcTiming").y, Is.EqualTo(0f).Within(0.01f));
            Assert.That(shield.CurrentHP, Is.EqualTo(65f));
        }

        [Test]
        public void OverflowShattersThenHidesAndReactivationReusesGeometry()
        {
            Renderer renderer = Arcs;
            Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            Assert.That(shield.Absorb(140f), Is.EqualTo(40f));
            Call(visual, "LateUpdate");
            var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
            Assert.That(block.GetVector("_ArcState").z, Is.EqualTo(1f));
            Set(visual, "endedAt", Time.time - 2f); Call(visual, "LateUpdate");
            Assert.That(renderer.enabled, Is.False);
            Start();
            Assert.That(Arcs, Is.SameAs(renderer));
            Assert.That(Arcs.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(mesh));
        }

        [Test]
        public void ExpiryCollapsesWhileCancellationHidesImmediatelyAndUnsubscribes()
        {
            Set(shield, "expireTime", Time.time - 1f);
            Set(visual, "expiresAt", Time.time - 1f);
            Call(visual, "LateUpdate");
            var block = new MaterialPropertyBlock(); Arcs.GetPropertyBlock(block);
            Assert.That(block.GetVector("_ArcState").z, Is.Zero);
            visual.Stop(true);
            Assert.That(Arcs.enabled || visual.enabled, Is.False);
            float previousHit = Get<float>(visual, "lastHitAt");
            shield.Activate(100f, 10f); shield.Absorb(10f);
            Assert.That(Get<float>(visual, "lastHitAt"), Is.EqualTo(previousHit));
        }

        [Test]
        public void FollowsBodyDuringLaneRotationWithoutCopyingBodyFlip()
        {
            root.transform.position = new Vector3(20, 4, -10);
            root.transform.rotation = Quaternion.Euler(0, 90, 0);
            facing.localRotation = Quaternion.Euler(0, -35, 0);
            facing.localScale = new Vector3(-1, 1, 1);
            Call(visual, "LateUpdate");
            Assert.That(Vector3.Distance(Arcs.transform.position, root.transform.position + Vector3.up), Is.LessThan(0.001f));
            Assert.That(Quaternion.Angle(Arcs.transform.rotation, facing.rotation), Is.LessThan(0.001f));
            Assert.That(Arcs.transform.lossyScale, Is.EqualTo(Vector3.one));
            Call(visual, "OnDisable");
            Assert.That(Arcs.enabled, Is.False);
        }

        [Test]
        public void SteadyArcsAllocateNoManagedMemory()
        {
            var update = (Action)Delegate.CreateDelegate(typeof(Action), visual,
                typeof(PulseBarrierVisual).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance));
            for (int i = 0; i < 32; i++) update();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 256; i++) update();
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.Zero);
        }

        [Test]
        public void CurrentModReferencesCustomAssetsAndNoLegacySphere()
        {
            var mod = AssetDatabase.LoadAssetAtPath<PulseBarrierMod>("Assets/Game/New Prefabs/Mods/PulseBarrierMod.asset");
            Assert.That(mod.pulseArcVisuals, Is.Not.Null);
            Assert.That(mod.pulseArcVisuals.arcMaterial, Is.Not.Null);
            Assert.That(ShaderUtil.ShaderHasError(mod.pulseArcVisuals.arcMaterial.shader), Is.False);
            Assert.That(mod.shieldActivateVFX == null && mod.shieldLoopVFXPrefab == null && mod.shieldAbsorbVFX == null, Is.True);
        }

        private static void Call(object target, string method) =>
            target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, null);
        private static void Set(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        private static T Get<T>(object target, string field) =>
            (T)target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);
    }
}
