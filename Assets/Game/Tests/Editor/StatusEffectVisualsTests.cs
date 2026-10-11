using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace junklite.Tests
{
    public sealed class StatusEffectVisualsTests
    {
        private GameObject root;
        private StatusEffectHandler handler;
        private StatusEffectVisuals visuals;
        private SpriteRenderer body;
        private Texture2D texture;
        private Sprite sprite;
        private Material sourceMaterial;
        private BurnVisualProfile profile;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Burn Test");
            body = new GameObject("Body").AddComponent<SpriteRenderer>();
            body.transform.SetParent(root.transform, false);
            texture = new Texture2D(8, 16);
            sprite = Sprite.Create(texture, new Rect(0, 0, 8, 16), new Vector2(0.5f, 0), 8);
            body.sprite = sprite;
            sourceMaterial = new Material(Shader.Find("Game/URP_Unlit_DamageFlash"));
            body.sharedMaterial = sourceMaterial;
            handler = root.AddComponent<StatusEffectHandler>();
            visuals = root.AddComponent<StatusEffectVisuals>();
            profile = ScriptableObject.CreateInstance<BurnVisualProfile>();
            profile.heatMaterial = new Material(Shader.Find("JunkLite/VFX/Burn Heat"));
            profile.flameMaterial = new Material(Shader.Find("JunkLite/VFX/Burn Flames"));
            Set(visuals, "burnProfile", profile);
            visuals.Initialize(handler, body.transform);
        }

        [TearDown]
        public void TearDown()
        {
            // EditMode does not dispatch MonoBehaviour destruction callbacks for these unstarted actors.
            if (visuals != null) { Call(visuals, "OnDisable"); Call(visuals, "OnDestroy"); }
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(sourceMaterial);
            Object.DestroyImmediate(sprite);
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(profile.heatMaterial);
            Object.DestroyImmediate(profile.flameMaterial);
            Object.DestroyImmediate(profile);
        }

        private void Burn() => handler.Apply(StatusEffectApplication.DamageOverTime(
            StatusEffectType.Burn, 5f, 0.5f, 3f, DamageType.Fire));

        private Renderer Heat => body.transform.Find("Burn Heat (Runtime)").GetComponent<Renderer>();
        private Renderer Flames => body.transform.Find("Burn Flames (Runtime)").GetComponent<Renderer>();
        private static void Set(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
        private static void Call(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args);

        [Test]
        public void ReapplyingAndReignitingReuseVisualsAndRemovalHidesThem()
        {
            Assert.That(body.transform.childCount, Is.Zero);
            Burn(); Call(visuals, "LateUpdate");
            Renderer heat = Heat, flames = Flames;
            Assert.That(visuals.IsBurning && heat.enabled && flames.enabled, Is.True);
            Burn();
            Assert.That(body.transform.childCount, Is.EqualTo(2));
            handler.Remove(StatusEffectType.Burn);
            Assert.That(visuals.IsBurning || heat.enabled || flames.enabled, Is.False);
            Burn(); Call(visuals, "LateUpdate");
            Assert.That(Heat, Is.SameAs(heat));
            Assert.That(Flames, Is.SameAs(flames));
        }

        [Test]
        public void RemovingOneBurnDefinitionKeepsTheOtherVisible()
        {
            var first = ScriptableObject.CreateInstance<StatusEffectDefinition>();
            var second = ScriptableObject.CreateInstance<StatusEffectDefinition>();
            try
            {
                Set(first, "type", StatusEffectType.Burn);
                Set(second, "type", StatusEffectType.Burn);
                handler.Apply(first.CreateApplication());
                handler.Apply(second.CreateApplication());
                handler.Remove(first);
                Assert.That(handler.HasEffect(StatusEffectType.Burn), Is.True);
                Assert.That(visuals.IsBurning, Is.True);
                handler.Remove(second);
                Assert.That(visuals.IsBurning, Is.False);
            }
            finally { Object.DestroyImmediate(first); Object.DestroyImmediate(second); }
        }

        [Test]
        public void NewBurnReplacesLegacyFireButLeavesOtherEffectsAlone()
        {
            var legacy = new GameObject("Legacy Burn"); legacy.transform.SetParent(root.transform);
            var poison = new GameObject("Poison"); poison.transform.SetParent(root.transform);
            poison.SetActive(false);
            Set(handler, "burnVFX", legacy);
            Set(handler, "poisonVFX", poison);
            visuals.Initialize(handler, body.transform);
            handler.Apply(StatusEffectApplication.DamageOverTime(StatusEffectType.Poison, 1, 1, 3, DamageType.Physical));
            Burn();
            Assert.That(legacy.activeSelf, Is.False);
            Assert.That(poison.activeSelf, Is.True);
            Call(visuals, "OnDisable");
            Assert.That(legacy.activeSelf, Is.False, "Retired particles must not return when the presenter is disabled.");
            Call(visuals, "OnEnable");
            Assert.That(legacy.activeSelf, Is.False);
            handler.ClearAllEffects();
            Assert.That(legacy.activeSelf || poison.activeSelf || visuals.IsBurning, Is.False);
        }

        [Test]
        public void LegacyParticlesAreClearedBeforeFirstBurnAndNeverRestartOnReignition()
        {
            var legacy = new GameObject("Legacy Burn"); legacy.transform.SetParent(root.transform);
            var child = new GameObject("Legacy Embers"); child.transform.SetParent(legacy.transform);
            var particles = child.AddComponent<ParticleSystem>();
            particles.Emit(8);
            Assert.That(particles.particleCount, Is.GreaterThan(0));
            Set(handler, "burnVFX", legacy);
            // Subscribe before rebinding the presenter: catch activation before its applied-event callback.
            handler.OnEffectApplied += type =>
            {
                if (type == StatusEffectType.Burn)
                    Assert.That(legacy.activeSelf, Is.False, "Legacy play-on-awake must never start on Apply.");
            };
            visuals.Initialize(handler, body.transform);
            Assert.That(particles.particleCount, Is.Zero);
            Assert.That(particles.isPlaying || legacy.activeSelf, Is.False);
            Burn(); handler.Remove(StatusEffectType.Burn); Burn();
            Call(visuals, "OnDisable"); Call(visuals, "OnDestroy");
            handler.Remove(StatusEffectType.Burn); Burn();
            Assert.That(particles.particleCount, Is.Zero);
            Assert.That(legacy.activeSelf, Is.False);
        }

        [Test]
        public void EmbersCanBeShownWithoutFlamesAndFollowBodyOpacity()
        {
            profile.showFlames = false;
            body.color = new Color(1, 1, 1, 0.4f);
            Burn(); Call(visuals, "LateUpdate");
            Assert.That(Flames.enabled, Is.True);
            var block = new MaterialPropertyBlock(); Flames.GetPropertyBlock(block);
            Assert.That(block.GetFloat("_Opacity"), Is.Zero);
            Assert.That(block.GetFloat("_EmberOpacity"), Is.EqualTo(profile.emberOpacity * body.color.a).Within(0.001f));
            profile.showEmbers = false; Call(visuals, "LateUpdate");
            Assert.That(Flames.enabled, Is.False);
            profile.showFlames = true; Call(visuals, "LateUpdate");
            Flames.GetPropertyBlock(block);
            Assert.That(Flames.enabled, Is.True);
            Assert.That(block.GetFloat("_EmberOpacity"), Is.Zero);
        }

        [Test]
        public void AttackWarningIsNeverBurnedOrUsedAsTheEmberSource()
        {
            var warning = new GameObject("Attack Warning");
            warning.transform.SetParent(root.transform, false);
            var warningSprite = Sprite.Create(texture, new Rect(0, 0, 8, 16), Vector2.zero, 1);
            warning.AddComponent<SpriteRenderer>().sprite = warningSprite;
            warning.transform.localScale = Vector3.one * 0.2f;
            warning.SetActive(false);
            Set(visuals, "visualRoot", root.transform);
            visuals.Initialize(handler, root.transform, warning.transform);
            try
            {
                Burn(); Call(visuals, "LateUpdate");
                Assert.That(Flames.enabled, Is.True, "Hidden warnings must not hide the body's embers.");
                Assert.That(warning.transform.childCount, Is.Zero);
                warning.SetActive(true); Call(visuals, "LateUpdate");
                Assert.That(warning.transform.childCount, Is.Zero, "Showing the warning must not burn it.");
                Assert.That(Flames.enabled, Is.True);
            }
            finally { Object.DestroyImmediate(warningSprite); }
        }

        [Test]
        public void EmberSourceUsesScaledBodySizeInsteadOfTextureUnits()
        {
            var smallPart = new GameObject("Small Part");
            smallPart.transform.SetParent(root.transform, false);
            var largeSprite = Sprite.Create(texture, new Rect(0, 0, 8, 16), Vector2.zero, 1);
            smallPart.AddComponent<SpriteRenderer>().sprite = largeSprite;
            smallPart.transform.localScale = Vector3.one * 0.01f;
            Set(visuals, "visualRoot", root.transform);
            try
            {
                Burn(); Call(visuals, "LateUpdate");
                Assert.That(Flames.enabled, Is.True);
                Assert.That(smallPart.transform.Find("Burn Flames (Runtime)"), Is.Null);
            }
            finally { Object.DestroyImmediate(largeSprite); }
        }

        [Test]
        public void HeatDoesNotChangeSourceMaterialOrDamageFlashProperties()
        {
            var block = new MaterialPropertyBlock();
            block.SetFloat("_AmountToFlash", 0.3f);
            body.SetPropertyBlock(block);
            Burn(); Call(visuals, "LateUpdate");
            handler.ClearAllEffects();
            body.GetPropertyBlock(block);
            Assert.That(body.sharedMaterial, Is.SameAs(sourceMaterial));
            Assert.That(block.GetFloat("_AmountToFlash"), Is.EqualTo(0.3f));
        }

        [Test]
        public void SpriteChangesFlipsAndVisibilityFollowTheSource()
        {
            Burn();
            body.flipX = true; body.flipY = true; body.sortingOrder = 6;
            body.color = new Color(1, 1, 1, 0.4f);
            Call(visuals, "LateUpdate");
            var overlay = (SpriteRenderer)Heat;
            Assert.That(overlay.sprite, Is.SameAs(body.sprite));
            Assert.That(overlay.flipX && overlay.flipY, Is.True);
            Assert.That(overlay.sortingOrder, Is.EqualTo(7));
            body.enabled = false; Call(visuals, "LateUpdate");
            Assert.That(Heat.enabled || Flames.enabled, Is.False);
            body.enabled = true; Call(visuals, "LateUpdate");
            Assert.That(Heat.enabled && Flames.enabled, Is.True);
        }

        [Test]
        public void ClearAndDisableResetPulseAndReleasePresentation()
        {
            Burn();
            Call(visuals, "OnStatusTick", StatusEffectType.Burn, 5f);
            Call(visuals, "LateUpdate");
            var block = new MaterialPropertyBlock(); Heat.GetPropertyBlock(block);
            Assert.That(block.GetFloat("_BurnTickTime"), Is.EqualTo(Time.time));
            Call(handler, "OnDisable");
            Assert.That(visuals.IsBurning, Is.False);
            Assert.That(Heat.enabled || Flames.enabled, Is.False);
            Burn(); Call(visuals, "LateUpdate"); Heat.GetPropertyBlock(block);
            Assert.That(block.GetFloat("_BurnTickTime"), Is.LessThan(-100f));
        }

        [Test]
        public void OnlyAppliedTickDamagePulsesAndDeathClearsBurn()
        {
            var receiver = root.AddComponent<BurnVisualTestReceiver>();
            Burn();
            receiver.Accept = false;
            Call(handler, "TickEffects", Time.time + 0.6f); Call(visuals, "LateUpdate");
            var block = new MaterialPropertyBlock(); Heat.GetPropertyBlock(block);
            Assert.That(block.GetFloat("_BurnTickTime"), Is.LessThan(-100f));
            receiver.Accept = true;
            Call(handler, "TickEffects", Time.time + 1.1f); Call(visuals, "LateUpdate");
            Heat.GetPropertyBlock(block);
            Assert.That(block.GetFloat("_BurnTickTime"), Is.EqualTo(Time.time));
            receiver.Alive = false; Call(handler, "Update");
            Assert.That(visuals.IsBurning || Heat.enabled || Flames.enabled, Is.False);
        }

        [Test]
        public void DestroyingPresenterRemovesItsRenderers()
        {
            Burn(); Call(visuals, "LateUpdate");
            Call(visuals, "OnDisable"); Call(visuals, "OnDestroy");
            Object.DestroyImmediate(visuals);
            Assert.That(body.transform.childCount, Is.Zero);
            Assert.That(body.sharedMaterial, Is.SameAs(sourceMaterial));
        }

        [Test]
        public void MeshBufferAndMaterialCountChangesAreTrackedWithoutReplacingSourceMaterials()
        {
            Object.DestroyImmediate(visuals);
            var meshBody = new GameObject("Mesh Body"); meshBody.transform.SetParent(root.transform, false);
            var filter = meshBody.AddComponent<MeshFilter>();
            var renderer = meshBody.AddComponent<MeshRenderer>(); renderer.sharedMaterial = sourceMaterial;
            var first = new Mesh(); first.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            first.triangles = new[] { 0, 1, 2 }; first.RecalculateBounds();
            var second = Object.Instantiate(first); second.subMeshCount = 2;
            second.SetTriangles(new[] { 0, 1, 2 }, 0); second.SetTriangles(new[] { 0, 1, 2 }, 1);
            filter.sharedMesh = first;
            visuals = root.AddComponent<StatusEffectVisuals>();
            Set(visuals, "burnProfile", profile); Set(visuals, "bodyRenderers", new Renderer[] { renderer });
            visuals.Initialize(handler, meshBody.transform);
            try
            {
                Burn(); Call(visuals, "LateUpdate");
                var overlay = meshBody.transform.Find("Burn Heat (Runtime)").GetComponent<MeshFilter>();
                Assert.That(overlay.sharedMesh, Is.SameAs(first));
                filter.sharedMesh = second; renderer.sharedMaterials = new[] { sourceMaterial, sourceMaterial };
                Call(visuals, "LateUpdate");
                Assert.That(overlay.sharedMesh, Is.SameAs(second));
                Assert.That(overlay.GetComponent<Renderer>().sharedMaterials.Length, Is.EqualTo(2));
                Assert.That(renderer.sharedMaterial, Is.SameAs(sourceMaterial));
            }
            finally { Object.DestroyImmediate(first); Object.DestroyImmediate(second); }
        }

        [Test]
        public void FlameSpreadIsLimitedForWidePaddedSprites()
        {
            var wideTexture = new Texture2D(128, 16);
            var wideSprite = Sprite.Create(wideTexture, new Rect(0, 0, 128, 16), new Vector2(0.5f, 0), 8);
            try
            {
                body.sprite = wideSprite;
                Burn(); Call(visuals, "LateUpdate");
                Vector3 scale = Flames.transform.localScale;
                Assert.That(scale.x, Is.LessThanOrEqualTo(scale.y * profile.flameMaxWidthToHeight + 0.001f));
            }
            finally { body.sprite = sprite; Object.DestroyImmediate(wideSprite); Object.DestroyImmediate(wideTexture); }
        }

        [Test]
        public void SteadyBurnDoesNotAllocateManagedMemory()
        {
            Burn();
            var method = typeof(StatusEffectVisuals).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
            var update = (System.Action)System.Delegate.CreateDelegate(typeof(System.Action), visuals, method);
            for (int i = 0; i < 32; i++) update();
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 256; i++) update();
            long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void DefaultAssetsReferenceBothShadersAndAreBuildReachable()
        {
            var asset = Resources.Load<BurnVisualProfile>(BurnVisualProfile.ResourceName);
            Assert.That(asset, Is.Not.Null);
            Assert.That(asset.IsConfigured, Is.True);
            Assert.That(asset.heatMaterial.GetTexture("_NoiseTex"), Is.Not.Null);
            Assert.That(ShaderUtil.ShaderHasError(asset.heatMaterial.shader), Is.False);
            Assert.That(ShaderUtil.ShaderHasError(asset.flameMaterial.shader), Is.False);
        }
    }

    public sealed class BurnVisualTestReceiver : MonoBehaviour, IDamageReceiver
    {
        public bool Accept = true;
        public bool Alive = true;
        public bool IsAlive => Alive;
        public DamageResult ReceiveDamage(DamageRequest request) => Accept
            ? DamageResult.Applied(request.Amount, request.Amount)
            : DamageResult.Rejected(DamageOutcome.Invulnerable, request.Amount);
    }
}
