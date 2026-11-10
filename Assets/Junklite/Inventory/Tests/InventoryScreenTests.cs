using System;
using System.Collections;
using System.Linq;
using Junklite.Shared;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UnityEngine.UIElements.TestFramework;

namespace Junklite.Inventory.Tests
{
    public class InventoryScreenTests : RuntimeUITestFixture
    {
        const string ScenePath = "Assets/Junklite/Inventory/Scenes/Inventory.unity";

        InventoryScreen _screen;
        ModInventory _inventory;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync("Inventory");
#endif
            yield return null;

            foreach (var link in UnityEngine.Object.FindObjectsByType<SceneLink>(FindObjectsSortMode.None)) link.enabled = false;
            _screen = UnityEngine.Object.FindAnyObjectByType<InventoryScreen>();
            _inventory = _screen.Inventory;
            SetUIContent(_screen.GetComponent<UIDocument>());

            // The simulator can't step frames during set-up, so wait for the fade-in in real time.
            float end = Time.unscaledTime + 2f;
            while (Q("inventory").ClassListContains("inventory--hidden") && Time.unscaledTime < end) yield return null;
            yield return new WaitForSecondsRealtime(0.4f);
        }

        [UnityTest]
        public IEnumerator Grid_ShowsEveryStoredMod()
        {
            yield return null;
            Assert.That(Q<Label>("storage-count").text, Is.EqualTo($"{_inventory.StoredCount} / {_inventory.Capacity}"));
            Assert.That(Tiles().Count(t => !t.ClassListContains("tile--empty")), Is.EqualTo(_inventory.StoredCount));
            Assert.That(Tiles().Count(), Is.EqualTo(_inventory.Capacity));
        }

        [UnityTest]
        public IEnumerator ClickModThenSlot_EquipsIt()
        {
            var mod = StoredMod(ModType.Active);
            simulate.Click(Tile(mod));
            Assert.That(_screen.PickedMod, Is.EqualTo(mod));
            Assert.That(Q("slot-z").ClassListContains("socket--target"), Is.True);

            simulate.Click(Q("slot-z"));
            yield return null;
            Assert.That(_inventory.GetEquipped(ModSlot.Z), Is.EqualTo(mod));
            Assert.That(_screen.PickedMod, Is.Null);
        }

        [UnityTest]
        public IEnumerator DoubleClick_EquipsIntoFirstFreeSlot()
        {
            var mod = StoredMod(ModType.Active);
            simulate.DoubleClick(Tile(mod));
            yield return null;
            Assert.That(_inventory.GetEquipped(ModSlot.Z), Is.EqualTo(mod), "Z is the free active slot in the starting build.");
        }

        [UnityTest]
        public IEnumerator DragModOntoSlot_EquipsIt()
        {
            var mod = StoredMod(ModType.Passive);
            simulate.DragAndDrop(Tile(mod).worldBound.center, Q("slot-passive2").worldBound.center);
            yield return null;
            Assert.That(_inventory.GetEquipped(ModSlot.Passive2), Is.EqualTo(mod));
        }

        [UnityTest]
        public IEnumerator DragSlotOntoStorage_UnequipsIt()
        {
            var mod = _inventory.GetEquipped(ModSlot.X);
            simulate.DragAndDrop(Q("slot-x").worldBound.center, Q("grid").worldBound.center);
            yield return null;
            Assert.That(_inventory.GetEquipped(ModSlot.X), Is.Null);
            CollectionAssert.Contains(_inventory.Stored, mod);
        }

        [UnityTest]
        public IEnumerator WrongKindOfSlot_IsRefusedWithAWarning()
        {
            var mod = StoredMod(ModType.Passive);
            simulate.Click(Tile(mod));
            simulate.Click(Q("slot-z"));
            yield return null;
            Assert.That(_inventory.GetEquipped(ModSlot.Z), Is.Null);
            Assert.That(Q("callout").ClassListContains("callout--warn"), Is.True);
            StringAssert.Contains("NEEDS A PASSIVE SLOT", _screen.CalloutText);
        }

        [UnityTest]
        public IEnumerator SlotKey_PutsHighlightedModInThatSlot()
        {
            var mod = StoredMod(ModType.Active);
            simulate.MouseMove(Tile(mod));
            yield return null;
            Assert.That(_screen.HighlightedMod, Is.EqualTo(mod));

            simulate.KeyPress(KeyCode.Z);
            yield return null;
            Assert.That(_inventory.GetEquipped(ModSlot.Z), Is.EqualTo(mod));
        }

        [UnityTest]
        public IEnumerator DeleteKey_SendsSlotModBackToStorage()
        {
            var mod = _inventory.GetEquipped(ModSlot.C);
            simulate.MouseMove(Q("slot-c"));
            yield return null;
            simulate.KeyPress(KeyCode.Delete);
            yield return null;
            Assert.That(_inventory.GetEquipped(ModSlot.C), Is.Null);
            CollectionAssert.Contains(_inventory.Stored, mod);
        }

        [UnityTest]
        public IEnumerator Submit_EquipsTheFocusedMod()
        {
            var mod = StoredMod(ModType.Active);
            Tile(mod).Focus();
            yield return null;
            using (var evt = NavigationSubmitEvent.GetPooled()) Tile(mod).SendEvent(evt);
            yield return null;
            Assert.That(_inventory.IsEquipped(mod), Is.True);
        }

        [UnityTest]
        public IEnumerator Back_DropsAPickedUpMod()
        {
            simulate.Click(Tile(StoredMod(ModType.Active)));
            Assert.That(_screen.PickedMod, Is.Not.Null);
            yield return null;

            Assert.That(_screen.HandleBack(), Is.True);
            Assert.That(_screen.PickedMod, Is.Null);
        }

        [UnityTest]
        public IEnumerator Filter_ShowsOneKindOfMod()
        {
            simulate.Click(Q("filter-passive"));
            yield return null;
            int passive = _inventory.Stored.Count(m => m.Type == ModType.Passive);
            Assert.That(_screen.Filter, Is.EqualTo(ModFilter.Passive));
            Assert.That(Tiles().Count(t => !t.ClassListContains("tile--empty")), Is.EqualTo(passive));
        }

        [UnityTest]
        public IEnumerator Hover_ShowsTheModInTheDetailPanel()
        {
            var mod = StoredMod(ModType.Active);
            simulate.MouseMove(Tile(mod));
            yield return null;
            Assert.That(Q<Label>("detail-name").text, Is.EqualTo(mod.DisplayName.ToUpperInvariant()));
            Assert.That(Q<Label>("detail-status-label").text, Is.EqualTo("IN STORAGE"));
        }

        [UnityTest]
        public IEnumerator AddingAndRemovingFromCode_RedrawsTheScreen()
        {
            var mod = ModDefinition.Create("test-mod", "Test Mod", ModRarity.Junk, ModType.Passive);
            int before = _inventory.StoredCount;

            Assert.That(_inventory.Add(mod), Is.True);
            yield return null;
            Assert.That(Tile(mod), Is.Not.Null);
            Assert.That(Q<Label>("storage-count").text, Is.EqualTo($"{before + 1} / {_inventory.Capacity}"));

            _inventory.Remove(mod);
            yield return null;
            Assert.That(Tile(mod), Is.Null);
            UnityEngine.Object.Destroy(mod);
        }

        [UnityTest]
        public IEnumerator MissionsTab_ShowsMissionsInPlaceOfMods()
        {
            simulate.Click(Q("tab-missions"));
            yield return null;
            Assert.That(_screen.Tab, Is.EqualTo(InventoryTab.Missions));
            Assert.That(Q("tab-missions").ClassListContains("tab--active"), Is.True);
            Assert.That(Q("grid").ClassListContains("hidden"), Is.True);
            Assert.That(Q("page").ClassListContains("hidden"), Is.False);
            Assert.That(Q("page").childCount, Is.GreaterThan(0));

            simulate.Click(Q("tab-inventory"));
            yield return null;
            Assert.That(Q("grid").ClassListContains("hidden"), Is.False);
            Assert.That(Q("page").ClassListContains("hidden"), Is.True);
        }

        [UnityTest]
        public IEnumerator MissionEntries_CanBeSetFromCode()
        {
            simulate.Click(Q("tab-missions"));
            yield return null;
            _screen.SetMissions(new[] { new InfoEntry("Test", "Only entry", "Body text.") });
            yield return null;
            Assert.That(Q("page").childCount, Is.EqualTo(1));
            Assert.That(Q("page").Q<Label>(className: "page-card__title").text, Is.EqualTo("ONLY ENTRY"));
        }

        [UnityTest]
        public IEnumerator Back_OnAPage_ReturnsToTheMods()
        {
            simulate.Click(Q("tab-missions"));
            yield return null;
            Assert.That(_screen.HandleBack(), Is.True);
            Assert.That(_screen.Tab, Is.EqualTo(InventoryTab.Inventory));
        }

        [UnityTest]
        public IEnumerator ThereIsNoCodexTab()
        {
            yield return null;
            Assert.That(Q("tab-codex"), Is.Null);
        }

        [UnityTest]
        public IEnumerator HoveringCas_OpensHerStats()
        {
            simulate.MouseMove(Q("cas-hit"));
            yield return null;
            Assert.That(_screen.IsCasStatsOpen, Is.True);
        }

        [UnityTest]
        public IEnumerator Screen_IsFullyOnPanel()
        {
            yield return null;
            var panel = rootVisualElement.panel.visualTree.layout;
            var screen = Q("inventory").worldBound;
            Assert.That(screen.yMin, Is.GreaterThanOrEqualTo(-0.5f));
            Assert.That(screen.yMax, Is.LessThanOrEqualTo(panel.height + 0.5f));
            Assert.That(screen.height, Is.GreaterThan(panel.height * 0.5f));
        }

        ModDefinition StoredMod(ModType type) => _inventory.Stored.First(m => m.Type == type);

        // The grid draws storage in order, so a mod's place in the filtered list is its tile.
        VisualElement Tile(ModDefinition mod)
        {
            var visible = _inventory.Stored.Where(m => _screen.Filter == ModFilter.All || (_screen.Filter == ModFilter.Active) == (m.Type == ModType.Active)).ToList();
            int index = visible.IndexOf(mod);
            return index < 0 ? null : Tiles().ElementAt(index);
        }

        System.Collections.Generic.IEnumerable<VisualElement> Tiles() => Q("grid").Children();

        VisualElement Q(string name) => rootVisualElement.Q(name);
        T Q<T>(string name) where T : VisualElement => rootVisualElement.Q<T>(name);

        IEnumerator WaitFor(Func<bool> condition, float seconds)
        {
            float end = Time.unscaledTime + seconds;
            while (!condition())
            {
                if (Time.unscaledTime > end) Assert.Fail($"Condition not met within {seconds} seconds.");
                simulate.FrameUpdate();
                yield return null;
            }
            simulate.FrameUpdate();
        }
    }
}
