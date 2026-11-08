using System.Linq;
using junklite.Editor;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace junklite.Tests
{
    /// <summary>The authored Active Mods Bar: wiring, slot template contents, approved fonts, nothing drawn in code.</summary>
    public sealed class ActiveModsBarTests
    {
        private GameObject bar;

        [TearDown]
        public void TearDown()
        {
            if (bar != null) Object.DestroyImmediate(bar);
            bar = null;
        }

        private ModCombatUI Build()
        {
            bar = ActiveModsBarUISetup.BuildBarObject();
            return bar.GetComponent<ModCombatUI>();
        }

        [Test]
        public void BuiltBarIsWiredToItsOwnChildren()
        {
            ModCombatUI ui = Build();
            var so = new SerializedObject(ui);

            var panel = so.FindProperty("panel").objectReferenceValue as GameObject;
            var parent = so.FindProperty("activeModParent").objectReferenceValue as Transform;
            var template = so.FindProperty("slotTemplate").objectReferenceValue as CombatModSlotUI;

            Assert.That(panel, Is.Not.Null, "panel");
            Assert.That(parent, Is.Not.Null, "slot parent");
            Assert.That(template, Is.Not.Null, "slot template");
            Assert.That(panel.transform.IsChildOf(bar.transform), "panel is inside the bar");
            Assert.That(parent.IsChildOf(panel.transform), "slot parent is inside the panel");
            Assert.That(template.transform.parent, Is.EqualTo(parent), "template sits in the slot parent");
            Assert.That(template.gameObject.activeSelf, Is.False, "template is inactive until copied");
            Assert.That(parent.GetComponent<HorizontalLayoutGroup>(), Is.Not.Null, "slots are laid out by a layout group");
        }

        [Test]
        public void SlotTemplateHasIconDurabilityKeyChipAndFlash()
        {
            ModCombatUI ui = Build();
            var template = new SerializedObject(ui).FindProperty("slotTemplate").objectReferenceValue as CombatModSlotUI;
            var so = new SerializedObject(template);

            var icon = so.FindProperty("iconImage").objectReferenceValue as Image;
            var fill = so.FindProperty("durabilityFill").objectReferenceValue as Image;
            var key = so.FindProperty("inputHintText").objectReferenceValue as TMP_Text;
            var flash = so.FindProperty("unlockFlash").objectReferenceValue as Image;

            Assert.That(icon, Is.Not.Null, "icon");
            Assert.That(icon.preserveAspect, Is.True, "icon keeps its aspect");
            Assert.That(fill, Is.Not.Null, "durability fill");
            Assert.That(fill.type, Is.EqualTo(Image.Type.Filled));
            Assert.That(key, Is.Not.Null, "key chip text");
            Assert.That(flash, Is.Not.Null, "unlock flash");
            Assert.That(flash.gameObject.activeSelf, Is.False, "flash starts hidden");

            var rect = (RectTransform)template.transform;
            Assert.That(rect.sizeDelta.x, Is.EqualTo(ActiveModsBarUISetup.SlotSize).Within(0.01f));
            // The icon fills most of the slot: that was the complaint about the old bar.
            Assert.That(icon.rectTransform.sizeDelta.x / rect.sizeDelta.x, Is.GreaterThan(0.6f));
        }

        [Test]
        public void BarUsesOnlyApprovedHudFonts()
        {
            Build();
            TMP_FontAsset body = UIFonts.HudBody;
            TMP_FontAsset title = UIFonts.HudTitle;
            foreach (TMP_Text text in bar.GetComponentsInChildren<TMP_Text>(true))
            {
                Assert.That(text.font, Is.Not.Null, text.name);
                Assert.That(text.font == body || text.font == title, Is.True, $"{text.name} uses {text.font.name}");
            }
        }

        [Test]
        public void BarIsASingleStripNotACardInACard()
        {
            Build();
            // One backdrop image and one accent; no nested frame/card the slots sit inside.
            string[] names = bar.GetComponentsInChildren<Image>(true).Select(i => i.name).ToArray();
            Assert.That(names.Count(n => n == "Backdrop"), Is.EqualTo(1));
            Assert.That(names.Any(n => n.Contains("Card")), Is.False, "no card inside the bar");
        }
    }
}
