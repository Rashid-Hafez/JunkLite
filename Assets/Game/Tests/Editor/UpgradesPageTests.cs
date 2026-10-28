using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using junklite.Editor;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace junklite.Tests
{
    /// <summary>The authored Upgrades page: wiring, node cards per branch, buying, lock reasons, approved fonts.</summary>
    public sealed class UpgradesPageTests
    {
        private readonly List<Object> owned = new();
        private PlayerProgressionConfig levels;
        private UpgradeTreeConfig tree;
        private PlayerProgression progression;
        private PlayerUpgrades upgrades;
        private MeleeWeaponData blades;

        [SetUp]
        public void SetUp()
        {
            levels = Own(ScriptableObject.CreateInstance<PlayerProgressionConfig>());
            levels.Generate();
            blades = Own(ScriptableObject.CreateInstance<MeleeWeaponData>());
            blades.weaponId = "blades";
            blades.displayName = "Blades";
            tree = Own(ScriptableObject.CreateInstance<UpgradeTreeConfig>());
            UpgradeTreeConfig.PopulateDefaults(tree, blades);
            var root = Own(new GameObject("Upgrade Page Services"));
            root.SetActive(false);
            progression = root.AddComponent<PlayerProgression>();
            SetField(progression, "config", levels);
            upgrades = root.AddComponent<PlayerUpgrades>();
            SetField(upgrades, "config", tree);
            Call(upgrades, "Attach", progression);
            typeof(PlayerUpgrades).GetProperty(nameof(PlayerUpgrades.Instance)).SetValue(null, upgrades);
        }

        [TearDown]
        public void TearDown()
        {
            typeof(PlayerUpgrades).GetProperty(nameof(PlayerUpgrades.Instance)).SetValue(null, null);
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }

        private T Own<T>(T value) where T : Object { owned.Add(value); return value; }
        private static void SetField(object target, string name, object value) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static object Call(object target, string name, params object[] args) => target.GetType()
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(target, args);

        private UpgradesPageView MakePage()
        {
            var canvas = Own(new GameObject("Canvas", typeof(RectTransform), typeof(Canvas)));
            GameObject page = UpgradesUISetup.BuildPageObject();
            page.transform.SetParent(canvas.transform, false);
            var view = page.GetComponent<UpgradesPageView>();
            foreach (UpgradeDetailsView details in page.GetComponentsInChildren<UpgradeDetailsView>(true)) Call(details, "Awake");
            Call(view, "Awake");
            Call(view, "Rebind");
            view.Rebuild();
            return view;
        }

        private static TMP_Text FindText(Component root, string name) =>
            root.GetComponentsInChildren<TMP_Text>(true).First(t => t.name == name);

        [Test]
        public void BuiltPageIsWiredUsesApprovedFontsAndHasFiveColumns()
        {
            var view = MakePage();
            var so = new UnityEditor.SerializedObject(view);
            Assert.That(so.FindProperty("nodeTemplate").objectReferenceValue, Is.Not.Null);
            Assert.That(so.FindProperty("branchColumns").arraySize, Is.EqualTo(Enum.GetValues(typeof(UpgradeBranch)).Length));
            Assert.That(so.FindProperty("pointsText").objectReferenceValue, Is.Not.Null);
            Assert.That(so.FindProperty("details").objectReferenceValue, Is.Not.Null);
            var template = (UpgradeNodeView)so.FindProperty("nodeTemplate").objectReferenceValue;
            Assert.That(template.gameObject.activeSelf, Is.False, "the template is authored inactive");

            foreach (TMP_Text text in view.GetComponentsInChildren<TMP_Text>(true))
                Assert.That(text.font == UIFonts.HudTitle || text.font == UIFonts.HudBody, Is.True, $"{text.name} must use ZuumeEdge or Satoshi");
            Assert.That(FindText(view, "Points Value").font, Is.SameAs(UIFonts.HudTitle));
            Assert.That(view.GetComponentsInChildren<TMP_Text>(true).Any(t => t.text == "UPGRADES"), Is.False, "the menu frame title names the tab; the page has no title of its own");
        }

        [Test]
        public void EveryNodeGetsACardInItsBranchColumnAndPointsAreShown()
        {
            var view = MakePage();
            Assert.That(view.Views.Count, Is.EqualTo(tree.nodes.Count));
            var columns = (RectTransform[])typeof(UpgradesPageView).GetField("branchColumns", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
            var scratch = new List<UpgradeNode>();
            foreach (UpgradeBranch branch in Enum.GetValues(typeof(UpgradeBranch)))
            {
                tree.GetBranch(branch, scratch);
                int cards = columns[(int)branch].GetComponentsInChildren<UpgradeNodeView>(true).Count(v => v.gameObject.activeSelf);
                Assert.That(cards, Is.EqualTo(scratch.Count), branch.ToString());
            }
            Assert.That(FindText(view, "Points Value").text, Is.EqualTo("0"));
            progression.AddExperience((int)levels.ExperienceToReach(3));
            Assert.That(FindText(view, "Points Value").text, Is.EqualTo("2"));

            UpgradeNodeView first = view.Views.First(v => v.Node.id == "vitality.frame1");
            UpgradeNodeView second = view.Views.First(v => v.Node.id == "vitality.plating1");
            UpgradeNodeView bladesNode = view.Views.First(v => v.Node.id == "blades.honed");
            Assert.That(first.State, Is.EqualTo(UpgradeNodeState.Available));
            Assert.That(second.State, Is.EqualTo(UpgradeNodeState.Locked));
            Assert.That(bladesNode.State, Is.EqualTo(UpgradeNodeState.Locked), "no blades yet");
        }

        [Test]
        public void FocusingShowsDetailsAndConfirmingBuysOrExplains()
        {
            var view = MakePage();
            progression.AddExperience((int)levels.ExperienceToReach(2));
            var details = view.GetComponentInChildren<UpgradeDetailsView>(true);
            UpgradeNodeView bladesNode = view.Views.First(v => v.Node.id == "blades.honed");
            Call(view, "HandleFocused", bladesNode);
            Assert.That(details.Node, Is.SameAs(bladesNode.Node));
            Assert.That(FindText(details, "Status").text, Does.Contain("FIND THE BLADES FIRST"));
            Assert.That(FindText(details, "Effects").text, Does.Contain("+20%").And.Contain("DAMAGE"));
            Assert.That(details.GetComponentInChildren<Button>(true).interactable, Is.False);

            UpgradeNodeView frame = view.Views.First(v => v.Node.id == "vitality.frame1");
            Call(view, "HandleConfirmed", frame);
            Assert.That(upgrades.IsOwned("vitality.frame1"), Is.True, "confirming an available node buys it");
            Assert.That(frame.State, Is.EqualTo(UpgradeNodeState.Owned));
            Assert.That(FindText(details, "Status").text, Is.EqualTo("OWNED"));
            Assert.That(FindText(view, "Points Value").text, Is.EqualTo("0"));

            UpgradeNodeView plating = view.Views.First(v => v.Node.id == "vitality.plating1");
            Call(view, "HandleConfirmed", plating);
            Assert.That(upgrades.IsOwned("vitality.plating1"), Is.False, "no points: confirming only shows why");
            Assert.That(plating.State, Is.EqualTo(UpgradeNodeState.Unaffordable));
            Assert.That(FindText(details, "Status").text, Does.Contain("NEEDS 1 POINT"));
        }

        [Test]
        public void HigherTiersShowAsCurrentlyUnavailable()
        {
            tree.maxAvailableTier = 1;
            tree.startingPoints = 5;
            var view = MakePage();
            var details = view.GetComponentInChildren<UpgradeDetailsView>(true);
            UpgradeNodeView plating = view.Views.First(v => v.Node.id == "vitality.plating1");
            Assert.That(plating.State, Is.EqualTo(UpgradeNodeState.Unavailable));
            Assert.That(FindText(plating, "Cost").text, Is.EqualTo("UNAVAILABLE"));

            Call(view, "HandleConfirmed", plating);
            Assert.That(upgrades.IsOwned("vitality.plating1"), Is.False, "confirming an unavailable node never buys it");
            Assert.That(FindText(details, "Status").text, Is.EqualTo("CURRENTLY UNAVAILABLE"));
            Assert.That(details.GetComponentsInChildren<Button>(true).First(b => b.name == "Buy Button").interactable, Is.False);

            UpgradeNodeView frame = view.Views.First(v => v.Node.id == "vitality.frame1");
            Assert.That(frame.State, Is.EqualTo(UpgradeNodeState.Available), "tier 1 is buyable with the starting points");
            Assert.That(FindText(view, "Points Value").text, Is.EqualTo("5"));
        }

        [Test]
        public void BuyButtonSpendsAPointAndGamepadDefaultSelectsAnAffordableNode()
        {
            var view = MakePage();
            upgrades.AddBonusPoints(1);
            var details = view.GetComponentInChildren<UpgradeDetailsView>(true);
            UpgradeNodeView dash = view.Views.First(v => v.Node.id == "dash.unlock");
            Call(view, "HandleFocused", dash);
            Button buy = details.GetComponentsInChildren<Button>(true).First(b => b.name == "Buy Button");
            Assert.That(buy.interactable, Is.True);
            buy.onClick.Invoke();
            Assert.That(upgrades.HasAbility(PlayerAbility.Dash), Is.True);
            Assert.That(buy.interactable, Is.False);

            upgrades.AddBonusPoints(1);
            var events = Own(new GameObject("Events", typeof(EventSystem))).GetComponent<EventSystem>();
            Call(events, "OnEnable");
            try
            {
                view.SelectDefault();
                var selected = events.currentSelectedGameObject.GetComponent<UpgradeNodeView>();
                Assert.That(selected.State, Is.EqualTo(UpgradeNodeState.Available));
                Assert.That(details.Node, Is.SameAs(selected.Node));
            }
            finally { Call(events, "OnDisable"); }
        }

        [Test]
        public void InventoryMenuGetsAnUpgradesTabThatShowsTheAuthoredPage()
        {
            var root = Own(new GameObject("Inventory", typeof(RectTransform)));
            var panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(root.transform, false);
            GameObject page = UpgradesUISetup.BuildPageObject();
            page.transform.SetParent(root.transform, false);
            var ui = root.AddComponent<InventoryUI>();
            SetField(ui, "panel", panel);
            SetField(ui, "upgradesScreen", page);
            Call(ui, "Awake");
            var tabs = root.GetComponentsInChildren<MenuButton>(true);
            Assert.That(tabs.Length, Is.EqualTo(4));
            Assert.That(page.transform.parent, Is.Not.SameAs(root.transform), "the page is moved into the menu frame");
            Assert.That(page.activeSelf, Is.False, "pages start hidden until their tab is picked");
            Call(ui, "ShowUpgradesTab");
            Assert.That(page.activeSelf, Is.True);
            Call(ui, "ShowInventoryTab");
            Assert.That(page.activeSelf, Is.False);
        }
    }
}
