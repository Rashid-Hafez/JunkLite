using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Junklite.Inventory;
using Junklite.Shared;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UnityEngine.UIElements.TestFramework;

namespace Junklite.Pause.Tests
{
    public class PauseMenuTests : RuntimeUITestFixture
    {
        const string ScenePath = "Assets/Junklite/Inventory/Scenes/Inventory.unity";

        PauseMenu _pause;
        InventoryScreen _screen;
        readonly List<PauseCommand> _executed = new List<PauseCommand>();

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
            _pause = UnityEngine.Object.FindAnyObjectByType<PauseMenu>();
            _screen = UnityEngine.Object.FindAnyObjectByType<InventoryScreen>();
            SetUIContent(_pause.GetComponent<UIDocument>());
            _executed.Clear();
            _pause.CommandExecuted += _executed.Add;

            yield return new WaitForSecondsRealtime(0.4f);
        }

        [UnityTearDown]
        public IEnumerator RestoreTime()
        {
            Time.timeScale = 1f;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Back_OpensMenu_DimsInventory_AndStopsTime()
        {
            _pause.HandleBack();
            yield return Frames();
            Assert.That(_pause.IsOpen, Is.True);
            Assert.That(_screen.Dimmed, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(0f));
            Assert.That(Q("pause").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(_pause.Selected, Is.EqualTo(PauseCommand.Resume));
        }

        [UnityTest]
        public IEnumerator Back_WithAPickedUpMod_DropsItInstead()
        {
            // The inventory and the menu share a panel, so the simulator can click the inventory too.
            var mod = _screen.Inventory.Stored.First();
            var grid = _screen.GetComponent<UIDocument>().rootVisualElement.Q("grid");
            simulate.Click(grid[0]);
            yield return Frames();
            Assert.That(_screen.PickedMod, Is.EqualTo(mod));

            _pause.HandleBack();
            yield return Frames();
            Assert.That(_pause.IsOpen, Is.False);
            Assert.That(_screen.PickedMod, Is.Null);
        }

        [UnityTest]
        public IEnumerator Resume_ClosesMenu_AndRestoresTime()
        {
            yield return OpenMenu();
            simulate.Click(Q("pause-resume"));
            yield return WaitFor(() => !_pause.IsOpen, 1f);
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(_screen.Dimmed, Is.False);
            CollectionAssert.AreEqual(new[] { PauseCommand.Resume }, _executed);
        }

        [UnityTest]
        public IEnumerator BackWhileOpen_ClosesMenu()
        {
            yield return OpenMenu();
            _pause.HandleBack();
            yield return WaitFor(() => !_pause.IsOpen, 1f);
        }

        [UnityTest]
        public IEnumerator LockedItem_ShowsToast_AndStaysOpen()
        {
            yield return OpenMenu();
            simulate.Click(Q("pause-map"));
            yield return Frames();
            Assert.That(_pause.IsOpen, Is.True);
            Assert.That(_pause.ToastText, Is.EqualTo("MAP ISN'T AVAILABLE YET"));
            CollectionAssert.AreEqual(new[] { PauseCommand.Map }, _executed);
        }

        [UnityTest]
        public IEnumerator QuitToTitle_ClosesAndFires()
        {
            yield return OpenMenu();
            simulate.Click(Q("pause-quit"));
            yield return Frames();
            Assert.That(_pause.IsOpen, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            CollectionAssert.AreEqual(new[] { PauseCommand.QuitToTitle }, _executed);
        }

        [UnityTest]
        public IEnumerator ArrowKeys_MoveSelectionAndWrap()
        {
            yield return OpenMenu();
            Move(NavigationMoveEvent.Direction.Up);
            Assert.That(_pause.Selected, Is.EqualTo(PauseCommand.QuitToTitle));
            Move(NavigationMoveEvent.Direction.Down);
            Assert.That(_pause.Selected, Is.EqualTo(PauseCommand.Resume));
            Move(NavigationMoveEvent.Direction.Down);
            Assert.That(_pause.Selected, Is.EqualTo(PauseCommand.Map));
        }

        [UnityTest]
        public IEnumerator CurrentBuild_MatchesTheInventory()
        {
            yield return OpenMenu();
            var inventory = _screen.Inventory;
            Assert.That(Q<Label>("count-linked").text, Is.EqualTo($"{inventory.EquippedCount}/6"));
            Assert.That(Q<Label>("count-storage").text, Is.EqualTo($"{inventory.StoredCount}/{inventory.Capacity}"));
            Assert.That(Q<Label>("count-elite").text, Is.EqualTo(inventory.All.Count(m => m.Rarity == ModRarity.Elite).ToString()));
            Assert.That(Q("build-tiles")[0].ClassListContains("build-tile--empty"), Is.EqualTo(inventory.GetEquipped(ModSlot.Z) == null));
        }

        IEnumerator OpenMenu()
        {
            _pause.Open();
            yield return Frames();
            Assert.That(_pause.IsOpen, Is.True);
        }

        IEnumerator Frames()
        {
            for (int i = 0; i < 3; i++)
            {
                simulate.FrameUpdate();
                yield return null;
            }
        }

        void Move(NavigationMoveEvent.Direction direction)
        {
            using (var evt = NavigationMoveEvent.GetPooled(direction)) rootVisualElement.panel.visualTree.SendEvent(evt);
        }

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
        }
    }
}
