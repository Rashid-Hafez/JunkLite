using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Junklite.Shared;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UnityEngine.UIElements.TestFramework;

namespace Junklite.TitleScreen.Tests
{
    public class TitleScreenTests : RuntimeUITestFixture
    {
        const string ScenePath = "Assets/Junklite/TitleScreen/Scenes/TitleScreen.unity";
        const float Margin = 0.25f;

        TitleScreenController _controller;
        readonly List<TitleCommand> _executed = new List<TitleCommand>();

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync("TitleScreen");
#endif
            yield return null;

            // Keep the commands from loading the inventory scene in the middle of a test.
            foreach (var link in UnityEngine.Object.FindObjectsByType<SceneLink>(FindObjectsSortMode.None)) link.enabled = false;

            _controller = UnityEngine.Object.FindAnyObjectByType<TitleScreenController>();
            SetUIContent(_controller.GetComponent<UIDocument>());

            _executed.Clear();
            _controller.CommandExecuted += _executed.Add;

            float end = Time.unscaledTime + 3f;
            while (_controller.Selected == null && Time.unscaledTime < end) yield return null;
            Assert.That(_controller.Selected, Is.Not.Null, "The menu never selected its first command.");
        }

        [UnityTest]
        public IEnumerator Resume_Click_FiresResume()
        {
            yield return CheckCommand("menu-resume", TitleCommand.Resume);
        }

        [UnityTest]
        public IEnumerator Begin_Click_FiresBegin()
        {
            yield return CheckCommand("menu-begin", TitleCommand.Begin);
        }

        [UnityTest]
        public IEnumerator Rooftop_Click_FiresRooftop()
        {
            yield return CheckCommand("menu-rooftop", TitleCommand.Rooftop);
        }

        [UnityTest]
        public IEnumerator Calibrate_Click_FiresCalibrate()
        {
            yield return CheckCommand("menu-calibrate", TitleCommand.Calibrate);
        }

        [UnityTest]
        public IEnumerator Commands_CanRunBackToBack()
        {
            simulate.Click(Q("menu-resume"));
            simulate.Click(Q("menu-begin"));
            yield return null;
            CollectionAssert.AreEqual(new[] { TitleCommand.Resume, TitleCommand.Begin }, _executed);
        }

        [UnityTest]
        public IEnumerator Terminate_ShowsReconnectScreen()
        {
            yield return GoOffline();
            Assert.That(Q("reconnect").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            CollectionAssert.AreEqual(new[] { TitleCommand.Terminate }, _executed);
        }

        [UnityTest]
        public IEnumerator Terminate_ClickedTwice_FiresOnce()
        {
            simulate.Click(Q("menu-terminate"));
            simulate.Click(Q("menu-terminate"));
            yield return WaitFor(() => _controller.IsOffline, 0.44f + Margin);
            yield return new WaitForSecondsRealtime(0.5f);
            CollectionAssert.AreEqual(new[] { TitleCommand.Terminate }, _executed);
        }

        [UnityTest]
        public IEnumerator Reconnect_Click_RestoresWorkingMenu()
        {
            yield return GoOffline();
            simulate.Click(Q("reconnect"));
            yield return CheckMenuRestored();

            _executed.Clear();
            simulate.Click(Q("menu-resume"));
            CollectionAssert.AreEqual(new[] { TitleCommand.Resume }, _executed);
        }

        [UnityTest]
        public IEnumerator Reconnect_Submit_RestoresMenu()
        {
            yield return GoOffline();
            Send(NavigationSubmitEvent.GetPooled());
            yield return CheckMenuRestored();
        }

        [UnityTest]
        public IEnumerator Reconnect_Space_RestoresMenu()
        {
            yield return GoOffline();
            Send(KeyDownEvent.GetPooled(' ', KeyCode.Space, EventModifiers.None));
            yield return CheckMenuRestored();
        }

        [UnityTest]
        public IEnumerator Hover_MovesSelection()
        {
            foreach (var command in new[] { TitleCommand.Calibrate, TitleCommand.Rooftop, TitleCommand.Terminate, TitleCommand.Begin, TitleCommand.Resume })
            {
                string name = "menu-" + command.ToString().ToLowerInvariant();
                simulate.MouseMove(Q(name));
                yield return null;
                Assert.That(_controller.Selected, Is.EqualTo(command));
                Assert.That(Q(name).ClassListContains("menu-item--selected"), Is.True);
            }
        }

        [UnityTest]
        public IEnumerator ArrowKeys_MoveSelectionAndWrap()
        {
            simulate.MouseMove(Q("menu-resume"));
            yield return null;

            Move(NavigationMoveEvent.Direction.Down);
            Assert.That(_controller.Selected, Is.EqualTo(TitleCommand.Begin));
            Move(NavigationMoveEvent.Direction.Up);
            Move(NavigationMoveEvent.Direction.Up);
            Assert.That(_controller.Selected, Is.EqualTo(TitleCommand.Terminate));
            Move(NavigationMoveEvent.Direction.Down);
            Assert.That(_controller.Selected, Is.EqualTo(TitleCommand.Resume));
        }

        [UnityTest]
        public IEnumerator Submit_RunsSelectedCommand()
        {
            simulate.MouseMove(Q("menu-calibrate"));
            yield return null;
            Q("menu-calibrate").Focus();

            Send(NavigationSubmitEvent.GetPooled());
            yield return WaitFor(() => _executed.Count > 0, 0.5f);
            CollectionAssert.AreEqual(new[] { TitleCommand.Calibrate }, _executed);
        }

        [UnityTest]
        public IEnumerator ButtonsWork_AfterUiIsRebuilt()
        {
            // What Unity does when the UXML is reimported during Play mode.
            var document = _controller.GetComponent<UIDocument>();
            _controller.enabled = false;
            document.enabled = false;
            document.enabled = true;
            _controller.enabled = true;
            SetUIContent(document);

            yield return WaitFor(() => _controller.Selected != null, 2f);
            simulate.Click(Q("menu-resume"));
            CollectionAssert.AreEqual(new[] { TitleCommand.Resume }, _executed);
            yield return new WaitForSecondsRealtime(1.5f);
            simulate.FrameUpdate();
            Assert.That(Q("portrait-cover").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
        }

        [UnityTest]
        public IEnumerator MissingLayout_LogsErrorInsteadOfThrowing()
        {
            var document = _controller.GetComponent<UIDocument>();
            var layout = document.visualTreeAsset;
            _controller.enabled = false;
            document.visualTreeAsset = null;

            LogAssert.Expect(LogType.Error, new Regex("Title screen UI is missing"));
            _controller.enabled = true;
            yield return null;
            _controller.enabled = false;

            document.visualTreeAsset = layout;
            _controller.enabled = true;
            SetUIContent(document);
            yield return WaitFor(() => _controller.Selected != null, 2f);
        }

        [UnityTest]
        public IEnumerator Screen_IsFullyOnPanel()
        {
            yield return null;
            simulate.FrameUpdate();
            CheckScreenFitsPanel();
        }

        [UnityTest]
        public IEnumerator Screen_IsFullyOnPanel_WhenRootIsNotStretched()
        {
            // Root full width but only as tall as its content, as seen in the Editor's Game view.
            var document = _controller.GetComponent<UIDocument>();
            _controller.enabled = false;
            var root = document.rootVisualElement;
            root.style.position = Position.Relative;
            root.style.left = StyleKeyword.Null;
            root.style.top = StyleKeyword.Null;
            root.style.right = StyleKeyword.Null;
            root.style.bottom = StyleKeyword.Null;
            root.style.flexGrow = 0;
            _controller.enabled = true;

            yield return null;
            simulate.FrameUpdate();
            CheckScreenFitsPanel();
        }

        [UnityTest]
        public IEnumerator Portrait_IsRevealedAfterStart()
        {
            yield return new WaitForSecondsRealtime(1.5f);
            simulate.FrameUpdate();
            Assert.That(Q("portrait-cover").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
        }

        IEnumerator CheckCommand(string button, TitleCommand command)
        {
            simulate.Click(Q(button));
            CollectionAssert.AreEqual(new[] { command }, _executed);
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.That(_controller.IsOffline, Is.False);
            CollectionAssert.AreEqual(new[] { command }, _executed, "The command fired more than once.");
        }

        IEnumerator GoOffline()
        {
            simulate.Click(Q("menu-terminate"));
            yield return WaitFor(() => _controller.IsOffline, 0.44f + Margin);
        }

        IEnumerator CheckMenuRestored()
        {
            yield return WaitFor(() => !_controller.IsOffline, 0.5f);
            yield return WaitFor(() => _controller.Selected != null, 2f);
            Assert.That(Q("reconnect").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
        }

        void CheckScreenFitsPanel()
        {
            var panel = rootVisualElement.panel.visualTree.layout;
            var screen = Q("screen").worldBound;
            Assert.That(panel.height, Is.GreaterThan(0));
            Assert.That(screen.height, Is.GreaterThan(panel.height * 0.5f), "The screen is squashed.");
            Assert.That(screen.yMin, Is.GreaterThanOrEqualTo(-0.5f), $"The top of the screen is cut off: {screen} in {panel}.");
            Assert.That(screen.yMax, Is.LessThanOrEqualTo(panel.height + 0.5f), $"The bottom of the screen is cut off: {screen} in {panel}.");
            Assert.That(screen.xMin, Is.GreaterThanOrEqualTo(-0.5f));
            Assert.That(screen.xMax, Is.LessThanOrEqualTo(panel.width + 0.5f));
        }

        VisualElement Q(string name) => rootVisualElement.Q(name);

        void Move(NavigationMoveEvent.Direction direction) => Send(NavigationMoveEvent.GetPooled(direction));

        void Send(EventBase evt)
        {
            using (evt) rootVisualElement.panel.visualTree.SendEvent(evt);
        }

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
