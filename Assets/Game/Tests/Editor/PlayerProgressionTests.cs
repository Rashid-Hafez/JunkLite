using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using Object = UnityEngine.Object;

namespace junklite.Tests
{
    public sealed class PlayerProgressionTests
    {
        private readonly List<Object> owned = new();
        private PlayerProgressionConfig config;
        private PlayerProgression progression;

        [SetUp]
        public void SetUp()
        {
            config = Own(ScriptableObject.CreateInstance<PlayerProgressionConfig>());
            config.Generate(20, 100, 50, healthPerLevel: 10f, damagePercentPerLevel: 3f);
            // An inactive service never runs Awake or reads/writes the actual player's save.
            var root = Own(new GameObject("Progression Test Service"));
            root.SetActive(false);
            progression = root.AddComponent<PlayerProgression>();
            var serialized = new SerializedObject(progression);
            serialized.FindProperty("config").objectReferenceValue = config;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }

        private T Own<T>(T value) where T : Object { owned.Add(value); return value; }

        private static void SetField(object target, string name, object value) => target.GetType()
            .GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(target, value);

        private static object Call(object target, string name, params object[] arguments) => target.GetType()
            .GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.DeclaredOnly).Invoke(target, arguments);

        private static string PlainText(TMP_Text text) => System.Text.RegularExpressions.Regex.Replace(text.text, "<[^>]+>", "");

        private PlayerCharacter MakePresentationPlayer(out WeaponManager weapons)
        {
            var runtime = MakeSource(out var attributes);
            runtime.gameObject.AddComponent<CapsuleCollider>();
            var player = runtime.gameObject.AddComponent<PlayerCharacter>();
            player.attributes = attributes;
            typeof(PlayerCharacter).GetProperty(nameof(PlayerCharacter.RuntimeStats)).SetValue(player, runtime);
            weapons = runtime.gameObject.AddComponent<WeaponManager>();
            var fists = Own(ScriptableObject.CreateInstance<MeleeWeaponData>());
            fists.baseDamage = 10f;
            SetField(weapons, "fistWeaponData", fists);
            var armament = runtime.gameObject.AddComponent<PlayerArmament>();
            armament.ApplyDefaultsIfMissing(fists, null);
            SetField(weapons, "armament", armament);
            return player;
        }

        private PlayerLevelUI MakeLevelUI(out RectTransform hud, out RectTransform portrait)
        {
            var canvasObject = Own(new GameObject("Canvas", typeof(RectTransform), typeof(Canvas)));
            hud = (RectTransform)Own(new GameObject("Small HUD", typeof(RectTransform))).transform;
            hud.SetParent(canvasObject.transform, false);
            hud.sizeDelta = new Vector2(340, 112);
            portrait = (RectTransform)new GameObject("Portrait", typeof(RectTransform)).transform;
            portrait.SetParent(hud, false);
            portrait.sizeDelta = new Vector2(66, 66);
            return PlayerLevelUI.Create(portrait, hud);
        }

        [Test]
        public void PortraitProgressStartsAtBottomLeftAndCompletesThePerimeter()
        {
            var ui = MakeLevelUI(out _, out var portrait);
            Call(ui, "SetProgress", 0.125f);
            var first = (RectTransform)portrait.Find("XP Edge 0");
            var top = (RectTransform)portrait.Find("XP Edge 1");
            Assert.That(first.sizeDelta.y, Is.EqualTo(33f));
            Assert.That(first.anchoredPosition.y, Is.EqualTo(-33f));
            Assert.That(top.sizeDelta.x, Is.Zero);
            Call(ui, "SetProgress", 0.5f);
            Assert.That(first.sizeDelta.y, Is.EqualTo(66f));
            Assert.That(first.anchoredPosition.y, Is.Zero);
            Assert.That(top.sizeDelta.x, Is.EqualTo(66f));
            Assert.That(((RectTransform)portrait.Find("XP Edge 2")).sizeDelta.y, Is.Zero);
            Call(ui, "SetProgress", 1f);
            Assert.That(((RectTransform)portrait.Find("XP Edge 3")).sizeDelta.x, Is.EqualTo(66f));
            Assert.That(((RectTransform)portrait.Find("XP Edge 2")).sizeDelta.y, Is.EqualTo(66f));
            Assert.That(((RectTransform)portrait.Find("XP Edge 3")).anchoredPosition.x, Is.Zero);
            var badge = (RectTransform)portrait.Find("Level Badge");
            Assert.That(-badge.anchoredPosition.y, Is.GreaterThan(portrait.rect.height), "Badge must not obscure the border.");
        }

        [Test]
        public void LevelUpClonesParryPulseAndCleansUpWithoutAffectingParryOrPreviews()
        {
            var player = MakePresentationPlayer(out _);
            var parry = player.gameObject.AddComponent<ParryHandler>();
            var template = Own(new GameObject("Authored Parry Pulse", typeof(MeshRenderer)));
            template.SetActive(false);
            template.transform.localScale = new Vector3(2, 3, 4);
            template.GetComponent<MeshRenderer>().enabled = false;
            SetField(parry, "VisualEffectParry", template);
            config.rippleScale = 1.5f;
            var ui = MakeLevelUI(out var hud, out var portrait);
            ui.Bind(player);
            var feedback = hud.parent.Find("Level Up Feedback");
            Assert.That(feedback, Is.Not.Null, "Screen feedback must escape the small HUD bounds.");
            Assert.That(feedback.GetComponent<Canvas>().overrideSorting, Is.True);
            Assert.That(feedback.Find("Level Up Screen Pulse"), Is.Null);
            Assert.That(feedback.Find("Expanding Pulse"), Is.Null);
            progression.PreviewLevel(2);
            Assert.That(GameObject.Find("Level Up Parry Pulse"), Is.Null, "Loading/previews must not celebrate.");
            Assert.That(portrait.Find("Level Badge/Level").GetComponent<TMP_Text>().text, Is.EqualTo("2"));
            Call(ui, "Celebrate", new ProgressionChange(1, 2, 100, true));
            var pulse = Own(GameObject.Find("Level Up Parry Pulse"));
            Assert.That(pulse, Is.Not.Null);
            Assert.That(pulse, Is.Not.SameAs(template));
            Assert.That(pulse.transform.localScale, Is.EqualTo(template.transform.lossyScale * config.rippleScale));
            Assert.That(pulse.transform.position, Is.EqualTo(player.VFXCenter));
            Assert.That(pulse.GetComponent<MeshRenderer>().enabled, Is.True);
            Assert.That(template.activeSelf, Is.False);
            Assert.That(template.GetComponent<MeshRenderer>().enabled, Is.False);
            Assert.That(parry.IsParrying, Is.False);
            SetField(ui, "rippleRemaining", 0f);
            Call(ui, "Update");
            Assert.That(pulse == null, Is.True, "Transient distortion must be cleaned up after its lifetime.");
            Call(ui, "Celebrate", new ProgressionChange(1, 2, 100, true));
            pulse = Own(GameObject.Find("Level Up Parry Pulse"));
            ui.Unbind();
            Assert.That(pulse == null, Is.True, "Hiding/unbinding must immediately release the effect.");
            config.showLevelUpRipple = false;
            ui.Bind(player);
            Call(ui, "Celebrate", new ProgressionChange(1, 2, 100, true));
            Assert.That(GameObject.Find("Level Up Parry Pulse"), Is.Null);
        }

        [Test]
        public void ClickingCassidyShowsEssentialStatsXpAndRestoresAfterItemHover()
        {
            var player = MakePresentationPlayer(out var weapons);
            var root = Own(new GameObject("Inventory", typeof(RectTransform)));
            var art = new GameObject("CHARACTER", typeof(RectTransform), typeof(Image));
            art.transform.SetParent(root.transform, false);
            var panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(root.transform, false);
            var ui = root.AddComponent<InventoryUI>();
            SetField(ui, "characterVisual", (RectTransform)art.transform);
            SetField(ui, "panel", panel);
            Call(ui, "Awake");
            ui.Bind(null, weapons);
            var eventSystem = Own(new GameObject("Events", typeof(EventSystem))).GetComponent<EventSystem>();
            Button character = Array.Find(root.GetComponentsInChildren<Button>(true), b => b.name == "Player Stats Button");
            Assert.That(character, Is.Not.Null);
            ExecuteEvents.Execute(character.gameObject, new PointerEventData(eventSystem) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
            TMP_Text FindText(string name) => Array.Find(root.GetComponentsInChildren<TMP_Text>(true), t => t.name == name);
            Assert.That(FindText("Details Label").text, Is.EqualTo("STATS"));
            Assert.That(FindText("Item Name").text, Is.EqualTo("Cassidy"));
            Assert.That(PlainText(FindText("Stats")), Is.EqualTo("HEALTH  100 / 100\nARMOR  5\nDAMAGE  +0"));
            Assert.That(FindText("Stats").text, Does.Contain("<color=#3DE8FA>HEALTH</color>")
                .And.Contain("<color=#E6F0FF>100 / 100</color>"));
            Assert.That(FindText("Description").gameObject.activeSelf, Is.False);
            Assert.That(FindText("Stats").rectTransform.anchoredPosition.y, Is.EqualTo(-228f));
            progression.AddExperience(175);
            player.Health.Remove(20);
            Call(ui, "LateUpdate");
            Assert.That(FindText("Experience").text, Is.EqualTo("75 / 150 XP"));
            Assert.That(FindText("Next Level").text, Is.Empty);
            Assert.That(PlainText(FindText("Stats")), Is.EqualTo("HEALTH  90 / 110\nARMOR  5\nDAMAGE  +3%"));
            var fill = Array.Find(root.GetComponentsInChildren<Image>(true), i => i.name == "XP Fill");
            Assert.That(fill.rectTransform.anchorMax.x, Is.EqualTo(0.5f));
            var hoveredMod = Own(ScriptableObject.CreateInstance<FireModData>());
            hoveredMod.baseDamage = 20f;
            Call(ui, "HandleModHovered", new ModInstance(hoveredMod));
            Assert.That(PlainText(FindText("Stats")), Does.Contain("BASE DAMAGE  20"));
            Assert.That(FindText("Description").gameObject.activeSelf, Is.True);
            Assert.That(FindText("Stats").rectTransform.anchoredPosition.y, Is.EqualTo(-344f));
            Assert.That(FindText("Stats").fontSize, Is.EqualTo(13f));
            Call(ui, "HandleHoverExit");
            Assert.That(FindText("Details Label").text, Is.EqualTo("STATS"));
            Assert.That(FindText("Stats").rectTransform.anchoredPosition.y, Is.EqualTo(-228f));
            progression.PreviewLevel(config.MaxLevel);
            Call(ui, "LateUpdate");
            Assert.That(FindText("Next Level").text, Is.EqualTo("MAX LEVEL"));
            Assert.That(fill.rectTransform.anchorMax.x, Is.EqualTo(1f));
            // EditMode does not run EventSystem's enable lifecycle automatically.
            Call(eventSystem, "OnEnable");
            try
            {
                eventSystem.SetSelectedGameObject(null);
                Assert.That(Call(ui, "TryMoveSelection", Vector2.left), Is.True);
                Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(character.gameObject));
                Assert.That(FindText("Details Label").text, Is.EqualTo("STATS"));
            }
            finally { Call(eventSystem, "OnDisable"); }
            ui.Unbind();
        }

        [Test]
        public void LockedSlotClickAndControllerConfirmExplainTheUpgrade()
        {
            var player = MakePresentationPlayer(out var weapons);
            var mods = player.gameObject.AddComponent<ModManager>();
            Call(mods, "Awake");
            var root = Own(new GameObject("Inventory", typeof(RectTransform)));
            var panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(root.transform, false);
            var ui = root.AddComponent<InventoryUI>();
            SetField(ui, "panel", panel);
            Call(ui, "Awake");
            ui.Bind(null, weapons);
            var events = Own(new GameObject("Events", typeof(EventSystem))).GetComponent<EventSystem>();
            var slot = Array.Find(root.GetComponentsInChildren<ModSlotUI>(), s => s.Type == ModSlotUI.SlotType.ActiveMod && s.SlotIndex == 1);
            Assert.That(slot.IsLocked, Is.True);
            Assert.That(slot.GetComponent<Selectable>().IsInteractable(), Is.True);
            ExecuteEvents.Execute(slot.gameObject, new PointerEventData(events), ExecuteEvents.pointerClickHandler);
            TMP_Text description = Array.Find(root.GetComponentsInChildren<TMP_Text>(true), t => t.name == "Description");
            Assert.That(description.text, Does.Contain("Mod Slot upgrade"));
            Assert.That(description.gameObject.activeSelf, Is.True);
            ExecuteEvents.Execute(slot.gameObject, new PointerEventData(events), ExecuteEvents.pointerExitHandler);
            ExecuteEvents.Execute(slot.gameObject, new BaseEventData(events), ExecuteEvents.submitHandler);
            Assert.That(description.text, Does.Contain("Mod Slot upgrade"));
            Assert.That(mods.UnlockedActiveSlots, Is.EqualTo(1));
            Assert.That(mods.GetActiveMod(1), Is.Null, "Inspecting a lock must not equip anything.");
            ui.Unbind();
        }

        [TestCase(1, 0)]
        [TestCase(5, 700)]
        [TestCase(10, 2700)]
        [TestCase(15, 5950)]
        public void DefaultThresholds(int level, long experience)
        {
            Assert.That(config.ExperienceToReach(level), Is.EqualTo(experience));
            Assert.That(config.LevelForExperience(experience), Is.EqualTo(level));
            if (level > 1) Assert.That(config.LevelForExperience(experience - 1), Is.EqualTo(level - 1));
        }

        [Test]
        public void IndividualLevelRewardsAreEditableAndShipEmpty()
        {
            config.levels[1].bonuses.Add(new ProgressionStatBonus { stat = PlayerStat.Armor, flat = 4 });
            Assert.That(config.Evaluate(PlayerStat.Armor, 5, 3), Is.EqualTo(9));
            Assert.That(config.Validate(out _), Is.True);
            var bare = Own(ScriptableObject.CreateInstance<PlayerProgressionConfig>());
            bare.Generate();
            Assert.That(bare.Evaluate(PlayerStat.MaxHealth, 100, bare.MaxLevel), Is.EqualTo(100), "default growth comes from the upgrade tree, not the level table");
            Assert.That(bare.Validate(out _), Is.True);
        }

        [Test]
        public void LargeRewardCrossesEveryThresholdAndRetainsOverflow()
        {
            int notifications = 0;
            ProgressionChange change = default;
            progression.Changed += value => { notifications++; change = value; };
            progression.AddExperience(725);
            Assert.That(progression.Level, Is.EqualTo(5));
            Assert.That(progression.ExperienceInLevel, Is.EqualTo(25));
            Assert.That(change.PreviousLevel, Is.EqualTo(1));
            Assert.That(change.LeveledUp, Is.True);
            Assert.That(notifications, Is.EqualTo(1));
            progression.AddExperience(-10);
            Assert.That(progression.TotalExperience, Is.EqualTo(725));
        }

        [Test]
        public void MaximumLevelClampsAndIgnoresFurtherRewards()
        {
            progression.AddExperience(int.MaxValue);
            Assert.That(progression.AtMaximum, Is.True);
            Assert.That(progression.Progress, Is.EqualTo(1));
            Assert.That(progression.ExperienceForNextLevel, Is.Zero);
            long total = progression.TotalExperience;
            progression.AddExperience(1000);
            Assert.That(progression.TotalExperience, Is.EqualTo(total));
        }

        [Test]
        public void PreviewRestoresOriginalExperienceIncludingPartialLevel()
        {
            progression.AddExperience(175);
            progression.PreviewLevel(15);
            progression.AddExperience(1000);
            progression.EndPreview();
            Assert.That(progression.TotalExperience, Is.EqualTo(175));
            Assert.That(progression.Level, Is.EqualTo(2));
            Assert.That(progression.IsPreviewing, Is.False);
        }

        [Test]
        public void RevisionMigrationPreservesLevelAndFractionByDefault()
        {
            var save = new ProgressionSaveData { configRevision = 1, totalExperience = 175, level = 2, levelProgress = 0.5 };
            config.revision = 2;
            config.Generate(20, 200, 100);
            Assert.That(PlayerProgression.RestoreExperience(config, save), Is.EqualTo(350));
            config.migrationPolicy = ProgressionMigrationPolicy.PreserveTotalExperience;
            Assert.That(PlayerProgression.RestoreExperience(config, save), Is.EqualTo(175));
        }

        [Test]
        public void ValidationRejectsInvalidBonuses()
        {
            config.levels[0].bonuses.Add(new ProgressionStatBonus { stat = PlayerStat.MaxHealth, flat = float.NaN });
            Assert.That(config.Validate(out _), Is.False);
        }

        [TestCase(LevelUpHealthPolicy.KeepCurrent, 60f)]
        [TestCase(LevelUpHealthPolicy.KeepPercentage, 66f)]
        [TestCase(LevelUpHealthPolicy.AddCapacity, 70f)]
        [TestCase(LevelUpHealthPolicy.FullHeal, 110f)]
        public void HealthPolicyAppliesOnlyOnEarnedLevels(LevelUpHealthPolicy policy, float expected)
        {
            var health = new Attribute { type = AttributeType.Health, maxValue = 100, startingValue = 60 };
            health.Initialize();
            PlayerRuntimeStats.ApplyHealthMaximum(health, 110, true, policy);
            Assert.That(health.Current, Is.EqualTo(expected).Within(0.001f));
            PlayerRuntimeStats.ApplyHealthMaximum(health, 110, false, policy);
            Assert.That(health.Current, Is.EqualTo(expected).Within(0.001f));
            health.SetToZero();
            PlayerRuntimeStats.ApplyHealthMaximum(health, 120, true, policy);
            Assert.That(health.IsAlive, Is.False);
        }

        private PlayerRuntimeStats MakeSource(out AttributeManager attributes)
        {
            var source = Own(new GameObject("Player Stats Source"));
            source.SetActive(false);
            var stats = Own(ScriptableObject.CreateInstance<CharacterStats>());
            stats.armor = 5;
            stats.attributes.Add(new Attribute { type = AttributeType.Health, name = "Health", maxValue = 100, startingValue = 100 });
            attributes = source.AddComponent<AttributeManager>();
            attributes.Initialize(stats);
            var runtime = source.AddComponent<PlayerRuntimeStats>();
            runtime.Initialize(stats, attributes, progression);
            return runtime;
        }

        [Test]
        public void RuntimeBonusesDoNotStackAndPreserveOtherHealthCapacity()
        {
            var runtime = MakeSource(out var attributes);
            progression.AddExperience(700);
            Assert.That(attributes.Health.Max, Is.EqualTo(140));
            Assert.That(runtime.ScaleDamage(100), Is.EqualTo(112).Within(0.001f));
            attributes.Health.SetMax(160, false); // Independent +20 capacity.
            attributes.Health.Remove(30);
            float healthBefore = attributes.Health.Current;
            runtime.Refresh(false);
            runtime.Refresh(false);
            Assert.That(attributes.Health.Max, Is.EqualTo(160));
            Assert.That(attributes.Health.Current, Is.EqualTo(healthBefore));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PlayerDamageScalesOnceBeforeArmorIncludingTicks(bool tick)
        {
            var source = MakeSource(out _);
            progression.AddExperience(700);
            var target = Own(new GameObject("Enemy Target"));
            var stats = Own(ScriptableObject.CreateInstance<CharacterStats>());
            stats.armor = 5;
            stats.attributes.Add(new Attribute { type = AttributeType.Health, maxValue = 1000, startingValue = 1000 });
            var attributes = target.AddComponent<AttributeManager>();
            attributes.Initialize(stats);
            var damageable = target.AddComponent<Damageable>();
            damageable.Bind(stats, attributes, null);
            var request = new DamageRequest(100, source.gameObject, isTickDamage: tick);
            Assert.That(damageable.ReceiveDamage(request).AppliedDamage, Is.EqualTo(107).Within(0.001f));
            // Reusing a request for an AOE target must not compound the bonus.
            Assert.That(damageable.ReceiveDamage(request).AppliedDamage, Is.EqualTo(107).Within(0.001f));
            var fixedRequest = new DamageRequest(100, source.gameObject, scaling: DamageScaling.Fixed);
            Assert.That(damageable.ReceiveDamage(fixedRequest).AppliedDamage, Is.EqualTo(95).Within(0.001f));
        }

        [Test]
        public void EnemyRewardsOnlyThePlayerAttributedLethalHit()
        {
            var source = MakeSource(out _);
            var target = Own(new GameObject("Reward Target"));
            target.SetActive(false);
            var enemy = target.AddComponent<DummyEnemy>();
            var stats = Own(ScriptableObject.CreateInstance<CharacterStats>());
            stats.attributes.Add(new Attribute { type = AttributeType.Health, maxValue = 100, startingValue = 100 });
            enemy.attributes = target.GetComponent<AttributeManager>();
            enemy.attributes.Initialize(stats);
            var receiver = target.GetComponent<Damageable>();
            receiver.Bind(stats, enemy.attributes, null);
            typeof(EnemyBase).GetField("damageable", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(enemy, receiver);
            enemy.ReceiveDamage(new DamageRequest(40, source.gameObject, isTickDamage: true));
            Assert.That(progression.TotalExperience, Is.Zero);
            enemy.ReceiveDamage(new DamageRequest(100, source.gameObject, isTickDamage: true));
            Assert.That(progression.TotalExperience, Is.EqualTo(25));
            enemy.ReceiveDamage(new DamageRequest(100, source.gameObject, isTickDamage: true));
            Assert.That(progression.TotalExperience, Is.EqualTo(25));
            enemy.attributes.RestoreHealthToMax();
            enemy.ReceiveDamage(DamageRequest.Forced(100));
            Assert.That(progression.TotalExperience, Is.EqualTo(25));
        }

        [Test]
        public void SaveRoundTripAndBackupRecovery()
        {
            string directory = Path.Combine(Path.GetTempPath(), "JunkLite-Progression-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "save.json");
            try
            {
                var store = new ProgressionSaveStore(path);
                Assert.That(store.TrySave(new ProgressionSaveData { totalExperience = 175, level = 2 }, out _), Is.True);
                Assert.That(store.TryLoad(out var first, out _), Is.True);
                Assert.That(first.totalExperience, Is.EqualTo(175));
                Assert.That(store.TrySave(new ProgressionSaveData { totalExperience = 700, level = 5 }, out _), Is.True);
                File.WriteAllText(path, "broken");
                Assert.That(store.TryLoad(out var recovered, out string warning), Is.True);
                Assert.That(recovered.totalExperience, Is.EqualTo(175));
                Assert.That(warning, Is.Not.Null);
                Assert.That(store.TrySave(recovered, out _), Is.True);
                File.WriteAllText(path, "{}");
                Assert.That(store.TryLoad(out recovered, out _), Is.True);
                Assert.That(recovered.totalExperience, Is.EqualTo(175));
            }
            finally
            {
                // Delete only this test's unique files; no recursive deletion.
                foreach (string suffix in new[] { "", ".bak", ".tmp" })
                    if (File.Exists(path + suffix)) File.Delete(path + suffix);
                if (Directory.Exists(directory)) Directory.Delete(directory);
            }
        }

        [Test]
        public void ArmorRewardUpdatesMitigationAndDisplayedValue()
        {
            config.levels[0].bonuses.Add(new ProgressionStatBonus { stat = PlayerStat.Armor, flat = 4 });
            var runtime = MakeSource(out var attributes);
            var receiver = runtime.gameObject.AddComponent<Damageable>();
            receiver.Bind(null, attributes, null);
            progression.AddExperience(100);
            Assert.That(runtime.Armor, Is.EqualTo(9));
            Assert.That(runtime.ArmorDisplay.Current, Is.EqualTo(9));
            Assert.That(receiver.ReceiveDamage(new DamageRequest(20)).AppliedDamage, Is.EqualTo(11));
        }

        [Test]
        public void SlotSyncEnforcesLocksAndPreservesItemsDuringPreview()
        {
            var root = Own(new GameObject("Mod Slot Test"));
            root.SetActive(false);
            var manager = root.AddComponent<ModManager>();
            typeof(ModManager).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(manager, null);
            manager.ApplyUnlockedSlots(0, 0);
            var mod = Own(ScriptableObject.CreateInstance<EnergyWaveMod>());
            var instance = new ModInstance(mod);
            Assert.That(manager.MaxActiveSlots, Is.EqualTo(4));
            Assert.That(manager.UnlockedActiveSlots, Is.EqualTo(1), "the prefab's starting slot is the floor");
            Assert.That(manager.EquipModAt(instance, true, 1), Is.False);
            manager.ApplyUnlockedSlots(1, 0);
            Assert.That(manager.EquipModAt(instance, true, 1), Is.True);
            manager.ApplyUnlockedSlots(0, 0);
            Assert.That(manager.GetActiveMod(1), Is.SameAs(instance));
            Assert.That(manager.TryActivateMod(1), Is.False);
            manager.ApplyUnlockedSlots(1, 0);
            Assert.That(manager.GetActiveMod(1), Is.SameAs(instance));
        }

        [Test]
        public void LevelsAloneNeverUnlockModSlots()
        {
            var runtime = MakeSource(out var attributes);
            var manager = runtime.gameObject.AddComponent<ModManager>();
            typeof(ModManager).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(manager, null);
            runtime.Initialize(null, attributes, progression);
            Assert.That(manager.UnlockedActiveSlots, Is.EqualTo(1));
            progression.AddExperience((int)config.ExperienceToReach(15));
            Assert.That(manager.UnlockedActiveSlots, Is.EqualTo(1), "slots are bought in the upgrade tree");
        }

        [Test]
        public void ShippedConfigurationAndPrefabsAreWired()
        {
            var shipped = AssetDatabase.LoadAssetAtPath<PlayerProgressionConfig>("Assets/Game/Resources/PlayerProgressionConfig.asset");
            Assert.That(shipped, Is.Not.Null);
            Assert.That(shipped.Validate(out string error), Is.True, error);
            foreach (string filename in new[] { "Game Root", "Game Manager" })
            {
                var root = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Game/Prefabs/Manager/{filename}.prefab");
                Assert.That(root.GetComponent<PlayerProgression>(), Is.Not.Null);
                Assert.That(root.GetComponent<PlayerProgression>().Config, Is.SameAs(shipped));
            }
            var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/PLAYER/Player_2.2.prefab");
            Assert.That(player.GetComponent<ModManager>().MaxActiveSlots, Is.EqualTo(4));
            Assert.That(player.GetComponent<ModManager>().UnlockedActiveSlots, Is.EqualTo(1));
            Assert.That(shipped.showLevelUpRipple, Is.True);
            Assert.That(shipped.levelUpRipplePrefab, Is.Null, "Default feedback should use Cassidy's current parry effect.");
            Assert.That(player.GetComponent<ParryHandler>().ScreenPulseTemplate, Is.Not.Null);
            var dummy = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Enemies/Dummy.prefab");
            Assert.That(dummy.GetComponent<DummyEnemy>().ExperienceReward, Is.Zero);
        }
    }

}
