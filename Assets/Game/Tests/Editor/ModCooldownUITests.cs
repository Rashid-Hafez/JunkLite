using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace junklite.Tests
{
    public sealed class ModCooldownUITests
    {
        private readonly List<Object> cleanup = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = cleanup.Count - 1; i >= 0; i--)
                if (cleanup[i] != null) Object.DestroyImmediate(cleanup[i]);
            cleanup.Clear();
        }

        [Test]
        public void SharedStyleIsAvailableAndWiredToCombatPrefab()
        {
            Assert.That(ModCooldownStyle.Default, Is.Not.Null);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/UI/Active Mod.prefab");
            Assert.That(prefab.GetComponent<CombatModSlotUI>().CooldownStyle,
                Is.SameAs(ModCooldownStyle.Default));
        }

        [Test]
        public void MovingCoolingModBetweenSlotsKeepsTimerAndClearsOldOverlay()
        {
            ModInstance mod = CreateMod();
            mod.StartCooldown(5f);
            Image first = CreateIcon();
            Image second = CreateIcon();
            var hud = new ModCooldownUI(first, ModCooldownStyle.Default);
            var inventory = new ModCooldownUI(second, ModCooldownStyle.Default);
            hud.Refresh(mod);
            hud.Refresh(null);
            inventory.Refresh(mod);

            Assert.That(first.GetComponentInChildren<ModCooldownGraphic>(true).gameObject.activeSelf, Is.False);
            Assert.That(second.GetComponentInChildren<ModCooldownGraphic>().gameObject.activeSelf, Is.True);
            Assert.That(mod.CooldownRemaining, Is.EqualTo(5f).Within(0.05f));
            foreach (Graphic graphic in second.GetComponentsInChildren<Graphic>())
                Assert.That(graphic.raycastTarget, Is.False);

            inventory.Refresh(CreateMod());
            Assert.That(second.GetComponentInChildren<ModCooldownGraphic>(true).gameObject.activeSelf, Is.False);
        }

        [TestCase(2.24f, "2.3s")]
        [TestCase(0.04f, "0.1s")]
        [TestCase(5.25f, "6s")]
        public void CountdownRoundsUpWhileActivationRemainsBlocked(float remaining, string expected)
        {
            ModInstance mod = CreateMod();
            mod.StartCooldown(remaining);
            Image icon = CreateIcon();
            new ModCooldownUI(icon, ModCooldownStyle.Default).Refresh(mod);
            Assert.That(icon.GetComponentInChildren<TMP_Text>().text, Is.EqualTo(expected));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CompletionFlashRequiresActivationReadiness(bool ready)
        {
            ModInstance mod = CreateMod();
            mod.StartCooldown(5f);
            Image icon = CreateIcon();
            var view = new ModCooldownUI(icon, ModCooldownStyle.Default);
            view.Refresh(mod);
            mod.ResetCooldown();
            view.Refresh(mod, ready);
            Assert.That(icon.GetComponentInChildren<ModCooldownGraphic>(true).gameObject.activeSelf, Is.EqualTo(ready));
            if (ready)
                Assert.That(icon.GetComponentInChildren<TMP_Text>().enabled, Is.False);

            view.Reset(); // Reopening the HUD must not replay a stale ready flash.
            view.Refresh(mod, ready);
            Assert.That(icon.GetComponentInChildren<ModCooldownGraphic>(true).gameObject.activeSelf, Is.False);
        }

        [Test]
        public void CombatSlotKeepsChargeGatingAfterCooldownExpires()
        {
            ModInstance mod = CreateMod();
            var data = (ActiveModData)mod.Data;
            data.chargesRequired = 1;
            data.icon = Track(Sprite.Create(Texture2D.whiteTexture,
                new Rect(0f, 0f, 1f, 1f), Vector2.one * 0.5f));
            Image icon = CreateIcon();
            var slot = icon.gameObject.AddComponent<CombatModSlotUI>();
            slot.Configure(icon, null, null, null, Color.white, Color.gray);
            mod.StartCooldown(5f);
            slot.Bind(mod, null);
            Assert.That(icon.color, Is.EqualTo(Color.white)); // Timer overlay supplies the dimming.
            mod.ResetCooldown();
            slot.Refresh();
            Assert.That(icon.color, Is.EqualTo(Color.gray));
            Assert.That(icon.GetComponentInChildren<ModCooldownGraphic>(true).gameObject.activeSelf, Is.False);
            mod.AddCharge(1);
            slot.Refresh();
            Assert.That(icon.color, Is.EqualTo(Color.white));
            slot.Clear();
            Assert.That(icon.enabled, Is.False);
        }

        [Test]
        public void ActiveTimerTransitionsToFullCooldownWithoutReadyFlash()
        {
            ModInstance mod = CreateMod();
            InvokeInstance(mod, "BeginActiveDuration", 15f);
            mod.StartCooldown(5f);
            Image icon = CreateIcon();
            var view = new ModCooldownUI(icon, ModCooldownStyle.Default);
            view.Refresh(mod);
            Assert.That(mod.IsOnCooldown, Is.False);
            Assert.That(Phase(icon).text, Is.EqualTo("ACTIVE"));
            Assert.That(Countdown(icon).text, Is.EqualTo("15s"));

            // Simulate early termination, such as shield HP being depleted.
            InvokeInstance(mod, "EndActiveDuration");
            view.Refresh(mod);
            Assert.That(mod.ActiveDurationRemaining, Is.Zero);
            Assert.That(mod.CooldownRemaining, Is.EqualTo(5f).Within(0.05f));
            Assert.That(Phase(icon).text, Is.EqualTo("COOLDOWN"));
            Assert.That(Countdown(icon).text, Is.EqualTo("5s"));
            Assert.That(Countdown(icon).enabled, Is.True);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void CooldownWaitsForBothExecutionAndIndependentEffect(bool executionEndsFirst)
        {
            ModInstance mod = CreateMod();
            InvokeInstance(mod, "TryBeginExecution");
            InvokeInstance(mod, "BeginActiveDuration", 15f);
            mod.StartCooldown(5f);
            InvokeInstance(mod, executionEndsFirst ? "EndExecution" : "EndActiveDuration");
            Assert.That(mod.IsOnCooldown, Is.False);
            Assert.That(((ActiveModData)mod.Data).CanActivate(mod, null), Is.False);
            InvokeInstance(mod, executionEndsFirst ? "EndActiveDuration" : "EndExecution");
            Assert.That(mod.CooldownRemaining, Is.EqualTo(5f).Within(0.05f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExecutionCleanupEndsActiveTimerOnCompletionOrCancellation(bool cancelled)
        {
            ModInstance mod = CreateMod();
            InvokeInstance(mod, "TryBeginExecution");
            var context = (ModExecutionContext)System.Activator.CreateInstance(typeof(ModExecutionContext),
                BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { null, mod, null }, null);
            context.ShowActiveDuration(15f);
            mod.StartCooldown(5f);
            typeof(ModExecutionContext).GetMethod("Finish", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(context, new object[] { cancelled });
            InvokeInstance(mod, "EndExecution");
            Assert.That(mod.HasActiveEffect, Is.False);
            Assert.That(mod.CooldownRemaining, Is.EqualTo(5f).Within(0.05f));
        }

        [TestCase("break")]
        [TestCase("expire")]
        [TestCase("cancel")]
        public void PulseBarrierEndsItsTimerWithTheActualShield(string ending)
        {
            var go = Track(new GameObject("Barrier Test Player"));
            go.SetActive(false); // Keep unrelated player lifecycle out of this focused test.
            var player = go.AddComponent<PlayerCharacter>();
            var shield = go.AddComponent<DamageShield>();
            var data = Track(ScriptableObject.CreateInstance<PulseBarrierMod>());
            data.shieldDuration = 15f;
            var mod = new ModInstance(data);
            InvokeInstance(mod, "TryBeginExecution");
            var context = (ModExecutionContext)System.Activator.CreateInstance(typeof(ModExecutionContext),
                BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { null, mod, player }, null);
            var routine = (System.Collections.IEnumerator)typeof(PulseBarrierMod)
                .GetMethod("MaintainShield", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(data, new object[] { context, player, shield });
            Assert.That(routine.MoveNext(), Is.True);
            mod.StartCooldown(5f);
            Assert.That(mod.ActiveDurationRemaining, Is.EqualTo(shield.TimeRemaining).Within(0.05f));
            Assert.That(mod.IsOnCooldown, Is.False);
            if (ending == "break") shield.Absorb(data.shieldHP);
            if (ending == "expire")
                typeof(DamageShield).GetField("expireTime", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(shield, Time.time - 1f);
            if (ending != "cancel") Assert.That(routine.MoveNext(), Is.False);
            typeof(ModExecutionContext).GetMethod("Finish", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(context, new object[] { ending == "cancel" });
            InvokeInstance(mod, "EndExecution");
            Assert.That(shield.IsActive, Is.False);
            Assert.That(mod.HasActiveEffect, Is.False);
            Assert.That(mod.CooldownRemaining, Is.EqualTo(5f).Within(0.05f));
        }

        [Test]
        public void ZeroCooldownEffectReturnsDirectlyToReadyAndDoesNotAffectOtherInstances()
        {
            ModInstance first = CreateMod();
            ModInstance second = new(first.Data);
            InvokeInstance(first, "BeginActiveDuration", 15f);
            first.StartCooldown(0f);
            Assert.That(second.HasActiveEffect, Is.False);
            Assert.That(((ActiveModData)second.Data).CanActivate(second, null), Is.True);
            InvokeInstance(first, "EndActiveDuration");
            Assert.That(first.IsOnCooldown, Is.False);
            Assert.That(((ActiveModData)first.Data).CanActivate(first, null), Is.True);
        }

        [Test]
        public void ExpiredEstimateStaysActiveUntilEffectActuallyEnds()
        {
            ModInstance mod = CreateMod();
            InvokeInstance(mod, "BeginActiveDuration", 0f);
            mod.StartCooldown(5f);
            Image icon = CreateIcon();
            new ModCooldownUI(icon, ModCooldownStyle.Default).Refresh(mod);
            Assert.That(Countdown(icon).enabled, Is.False);
            Assert.That(Phase(icon).enabled, Is.True);
            Assert.That(Phase(icon).text, Is.EqualTo("ACTIVE"));
            Assert.That(mod.IsOnCooldown, Is.False);
            Assert.That(((ActiveModData)mod.Data).CanActivate(mod, null), Is.False);
        }

        [Test]
        public void BreakingModClearsCooldownImmediately()
        {
            ModInstance mod = CreateMod();
            mod.Data.durabilityPerUse = mod.Data.maxDurability;
            mod.StartCooldown(5f);
            Image icon = CreateIcon();
            var view = new ModCooldownUI(icon, ModCooldownStyle.Default);
            view.Refresh(mod);
            mod.ConsumeDurability();
            view.Refresh(mod);
            Assert.That(icon.GetComponentInChildren<ModCooldownGraphic>(true).gameObject.activeSelf, Is.False);
        }

        [TestCase(0f)]
        [TestCase(0.25f)]
        [TestCase(0.5f)]
        [TestCase(0.75f)]
        [TestCase(1f)]
        public void SquareOverlayFillsIconCornersAndTracksRemainingQuarterTurns(float fraction)
        {
            var style = Track(ScriptableObject.CreateInstance<ModCooldownStyle>());
            style.timerBackgroundColor = Color.clear; // Measure only the moving layer.
            var go = Track(new GameObject("Overlay", typeof(RectTransform), typeof(ModCooldownGraphic)));
            var overlay = go.GetComponent<ModCooldownGraphic>();
            overlay.rectTransform.sizeDelta = new Vector2(48f, 43f);
            overlay.SetAppearance(1f, style);
            float fullArea = MeshArea(overlay);
            Assert.That(fullArea, Is.EqualTo(43f * 43f).Within(0.1f));
            overlay.SetAppearance(fraction, style);
            Assert.That(MeshArea(overlay), Is.EqualTo(fullArea * fraction).Within(0.1f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LastQuarterRemainsOnTheSideOppositeTheWipe(bool clockwise)
        {
            var style = Track(ScriptableObject.CreateInstance<ModCooldownStyle>());
            style.timerBackgroundColor = Color.clear;
            style.wipeClockwise = clockwise;
            var go = Track(new GameObject("Overlay", typeof(RectTransform), typeof(ModCooldownGraphic)));
            var overlay = go.GetComponent<ModCooldownGraphic>();
            overlay.rectTransform.sizeDelta = new Vector2(48f, 43f);
            overlay.SetAppearance(0.25f, style);
            var triangles = MeshTriangles(overlay);
            Assert.That(triangles, Is.Not.Empty);
            foreach (UIVertex vertex in triangles)
            {
                Assert.That(vertex.position.x * (clockwise ? -1f : 1f), Is.GreaterThanOrEqualTo(-0.001f));
                Assert.That(vertex.position.y, Is.GreaterThanOrEqualTo(-0.001f));
            }
        }

        private static List<UIVertex> MeshTriangles(ModCooldownGraphic overlay)
        {
            using var vh = new VertexHelper();
            typeof(ModCooldownGraphic).GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic,
                null, new[] { typeof(VertexHelper) }, null)
                .Invoke(overlay, new object[] { vh });
            var triangles = new List<UIVertex>();
            vh.GetUIVertexStream(triangles);
            return triangles;
        }

        private static float MeshArea(ModCooldownGraphic overlay)
        {
            var triangles = MeshTriangles(overlay);
            float area = 0f;
            for (int i = 0; i < triangles.Count; i += 3)
                area += Vector3.Cross(triangles[i + 1].position - triangles[i].position,
                    triangles[i + 2].position - triangles[i].position).magnitude * 0.5f;
            return area;
        }

        private ModInstance CreateMod() => new(Track(ScriptableObject.CreateInstance<EnergyWaveMod>()));

        private static TMP_Text Countdown(Image icon) => icon.transform.Find("Mod Timer/Seconds Remaining").GetComponent<TMP_Text>();
        private static TMP_Text Phase(Image icon) => icon.transform.Find("Mod Timer/Timer Phase").GetComponent<TMP_Text>();

        private static void InvokeInstance(ModInstance mod, string method, params object[] args) =>
            typeof(ModInstance).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(mod, args);

        private Image CreateIcon()
        {
            var go = Track(new GameObject("Icon", typeof(RectTransform), typeof(Image)));
            var icon = go.GetComponent<Image>();
            icon.rectTransform.sizeDelta = new Vector2(48f, 43f);
            icon.raycastTarget = false;
            return icon;
        }

        private T Track<T>(T value) where T : Object
        {
            cleanup.Add(value);
            return value;
        }
    }
}
