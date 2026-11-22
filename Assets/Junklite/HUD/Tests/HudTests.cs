using System.Collections;
using System.Linq;
using Junklite.Inventory;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Junklite.HUD.Tests
{
    /// <summary>
    /// Plays the HUD scene and drives every element through its public calls, checking the classes and text the
    /// USS reacts to. Needs only Unity's Test Framework (in the 2D and 3D templates).
    /// </summary>
    public class HudTests
    {
        const string ScenePath = "Assets/Junklite/HUD/Scenes/Hud.unity";

        HudLayout _layout;
        HudVitals _vitals;
        HudObjective _objective;
        HudModBar _mods;
        HudDialogue _dialogue;
        ModInventory _inventory;
        VisualElement _hud;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return SceneManager.LoadSceneAsync("Hud");
#endif
            yield return null;

            HudInput.SetDevice(HudDevice.Keyboard);
            HudInput.ResetBindings();
            _layout = Object.FindAnyObjectByType<HudLayout>();
            _vitals = Object.FindAnyObjectByType<HudVitals>();
            _objective = Object.FindAnyObjectByType<HudObjective>();
            _mods = Object.FindAnyObjectByType<HudModBar>();
            _dialogue = Object.FindAnyObjectByType<HudDialogue>();
            _inventory = Object.FindAnyObjectByType<ModInventory>();
            _hud = _layout.Hud;

            // the objective arrives 0.3 s in and its glitch takes about half a second
            yield return new WaitForSecondsRealtime(1.2f);
        }

        [UnityTearDown]
        public IEnumerator Reset()
        {
            HudFx.ReducedMotion = false;
            HudInput.SetDevice(HudDevice.Keyboard);
            Time.timeScale = 1f;
            yield return null;
        }

        static IEnumerator Seconds(float s) => WaitRealtime(s);

        static IEnumerator WaitRealtime(float s)
        {
            yield return new WaitForSecondsRealtime(s);
        }

        VisualElement Q(string name) => _hud.Q(name);

        static void AssertColour(Color actual, string hex, string what)
        {
            ColorUtility.TryParseHtmlString(hex, out var want);
            Assert.That(actual.r, Is.EqualTo(want.r).Within(0.02f), what + " (red)");
            Assert.That(actual.g, Is.EqualTo(want.g).Within(0.02f), what + " (green)");
            Assert.That(actual.b, Is.EqualTo(want.b).Within(0.02f), what + " (blue)");
        }

        // ---------- layout ----------

        [UnityTest]
        public IEnumerator Layout_StartsOnTheChosenLayout()
        {
            Assert.That(Q("hp").parent.name, Is.EqualTo("zone-tl"));
            Assert.That(Q("ob").parent.name, Is.EqualTo("zone-tr"));
            Assert.That(Q("mods-wrap").parent.name, Is.EqualTo("zone-bc"));
            Assert.That(Q("dlg").parent.name, Is.EqualTo("zone-br"));
            Assert.That(_hud.ClassListContains("size-70"), Is.True);
            Assert.That(_layout.Size, Is.EqualTo(HudScale.Percent70));
            Assert.That(Q("mods").ClassListContains("mods--bare"), Is.True);
            Assert.That(Q("mods-head").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            Assert.That(Q("zone-tl").resolvedStyle.scale.value.x, Is.EqualTo(0.7f).Within(0.01f));
            // the Linear-colour-space sheet only applies in a Linear project
            Assert.That(_hud.ClassListContains("hud--linear"), Is.EqualTo(QualitySettings.activeColorSpace == ColorSpace.Linear));
            yield return null;
        }

        [UnityTest]
        public IEnumerator Layout_SizesScaleFromTheCorners()
        {
            (HudScale size, float scale)[] sizes =
            {
                (HudScale.Percent80, 0.8f), (HudScale.Percent90, 0.9f), (HudScale.Percent100, 1f), (HudScale.Percent70, 0.7f),
            };
            foreach (var (size, scale) in sizes)
            {
                _layout.Size = size;
                yield return Seconds(0.4f);
                foreach (var zone in new[] { "zone-tl", "zone-tr", "zone-bc", "zone-br" })
                    Assert.That(Q(zone).resolvedStyle.scale.value.x, Is.EqualTo(scale).Within(0.01f), zone + " at " + size);
            }

            _layout.Size = HudScale.Percent100;
            _layout.CycleSize();
            Assert.That(_layout.Size, Is.EqualTo(HudScale.Percent70));
        }

        // ---------- the look: no boxes but the objective's card, in the colour prototype's colours ----------

        [UnityTest]
        public IEnumerator Look_NoBoxesButTheObjectiveCard()
        {
            Assert.That(Q("hp-main").resolvedStyle.backgroundColor.a, Is.EqualTo(0f), "vitals panel");
            Assert.That(Q("hp-main").resolvedStyle.borderTopWidth, Is.EqualTo(0f), "vitals top line");
            Assert.That(Q("hp-feed").resolvedStyle.backgroundColor.a, Is.EqualTo(0f), "feed fill");
            Assert.That(Q("dlg-box").resolvedStyle.backgroundColor.a, Is.EqualTo(0f), "dialogue box");
            Assert.That(Q("mods").resolvedStyle.backgroundColor.a, Is.EqualTo(0f), "mod bar panel");
            foreach (var tick in Q("hp").Query(className: "tick").ToList().Concat(Q("dlg").Query(className: "tick").ToList()))
                Assert.That(tick.resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "corner ticks");

            var card = Q("ob").Q(className: "ob-panel");
            Assert.That(card.resolvedStyle.backgroundColor.a, Is.GreaterThan(0.9f), "objective card");
            AssertColour(card.resolvedStyle.backgroundColor, "#21266E", "objective card");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Look_YellowAndBlueLeadInTurn()
        {
            // objective: yellow writing on the dark blue card
            AssertColour(Q("ob-title").Q(className: "chroma__text").resolvedStyle.color, "#EBF83F", "objective title");
            AssertColour(L("ob-count").resolvedStyle.color, "#EBF83F", "objective count");
            // vitals: sky blue digits, a yellow bar, a sky blue STABLE chip with dark blue text, a red frame round the bar
            AssertColour(L("hp-value").resolvedStyle.color, "#6FD0FF", "health digits");
            AssertColour(Q("hp-fill").resolvedStyle.backgroundColor, "#EBF83F", "health bar (yellow since the fifth take)");
            AssertColour(Q("hp-state").resolvedStyle.backgroundColor, "#6FD0FF", "STABLE chip");
            AssertColour(L("hp-state").resolvedStyle.color, "#21266E", "STABLE text");
            AssertColour(Q("hp-bar").resolvedStyle.borderTopColor, "#E44018", "health bar frame");
            // mod bar: yellow caps
            var cap = _mods.KeyFor(2).Q(className: "kp__cap");
            AssertColour(cap.resolvedStyle.backgroundColor, "#EBF83F", "mod key cap");
            // dialogue: a sky blue name chip
            AssertColour(Q("dlg-name").resolvedStyle.backgroundColor, "#6FD0FF", "name chip");
            // no text outline: Unity draws it half inside the letters, so the writing keeps only its soft shadow
            Assert.That(L("hp-value").resolvedStyle.unityTextOutlineWidth, Is.EqualTo(0f), "no text outline");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Layout_FadesWhenQuiet_AndWakesOnAHit()
        {
            _layout.FadeWhenQuiet = true;
            yield return Seconds(4.6f);
            Assert.That(_layout.IsIdle, Is.True);

            _vitals.Damage(10);
            yield return null;
            Assert.That(_layout.IsIdle, Is.False);
            _layout.FadeWhenQuiet = false;
        }

        // ---------- vitals ----------

        [UnityTest]
        public IEnumerator Vitals_LowThenCritical()
        {
            Assert.That(_vitals.Health, Is.EqualTo(132));
            _vitals.Damage(84);
            yield return Seconds(0.5f);
            Assert.That(_vitals.Health, Is.EqualTo(48));
            Assert.That(Q("hp").ClassListContains("hp--low"), Is.True);
            Assert.That(L("hp-value").text, Is.EqualTo("48"));
            Assert.That(L("hp-state").text, Is.EqualTo("LOW"));
            Assert.That(Q("hp-fill").style.width.value.value, Is.EqualTo(32f).Within(0.1f));

            _vitals.Damage(28);
            yield return Seconds(1.2f);
            Assert.That(_vitals.Health, Is.EqualTo(20));
            Assert.That(Q("hp").ClassListContains("hp--critical"), Is.True);
            Assert.That(L("hp-state").text, Is.EqualTo("CRITICAL"));
            Assert.That(_vitals.Face, Is.EqualTo(CasFace.Angry));
        }

        Label L(string name) => _hud.Q<Label>(name);

        [UnityTest]
        public IEnumerator Vitals_HealthPackCountsUp()
        {
            _vitals.Damage(84);
            yield return Seconds(0.5f);
            _vitals.Heal(40);
            yield return Seconds(1f);
            Assert.That(_vitals.Health, Is.EqualTo(88));
            Assert.That(L("hp-value").text, Is.EqualTo("88"));
            Assert.That(Q("hp").ClassListContains("hp--low"), Is.False);
            Assert.That(L("hp-state").text, Is.EqualTo("STABLE"));
        }

        [UnityTest]
        public IEnumerator Vitals_GoingDown_CrowRevivesToFortyPercent()
        {
            _vitals.GoDown();
            yield return null;
            Assert.That(_vitals.Health, Is.EqualTo(0));
            Assert.That(L("hp-state").text, Is.Not.EqualTo("STABLE"));

            yield return Seconds(0.8f);
            Assert.That(_vitals.Crow, Is.EqualTo(CrowState.Deployed));
            yield return Seconds(1.6f);
            Assert.That(_vitals.Health, Is.EqualTo(60));
            yield return Seconds(1f);
            Assert.That(_vitals.Crow, Is.EqualTo(CrowState.Recharging));
            // exactly 40% is not low
            Assert.That(_vitals.IsLow, Is.False);
            Assert.That(L("hp-state").text, Is.EqualTo("STABLE"));
        }

        [UnityTest]
        public IEnumerator Vitals_ArmorBreaksAndRepairs()
        {
            for (int i = 0; i < 4; i++) _vitals.HitArmor();
            yield return Seconds(0.2f);
            Assert.That(_vitals.Armor, Is.EqualTo(0));
            Assert.That(Q("hp-plates").Children().Count(p => p.ClassListContains("hp-plate--broken")), Is.EqualTo(4));
            Assert.That(L("hp-armor-value").text, Is.EqualTo("0 / 4"));

            _vitals.RepairArmor();
            yield return Seconds(0.4f);
            Assert.That(Q("hp-plates").Children().Count(p => p.ClassListContains("hp-plate--broken")), Is.EqualTo(0));
        }

        // ---------- mods ----------

        [UnityTest]
        public IEnumerator Mods_LoadoutComesFromTheInventory()
        {
            Assert.That(_mods.ModIn(0), Is.Null);
            Assert.That(_mods.ModIn(1), Is.EqualTo(_inventory.GetEquipped(ModSlot.X)));
            Assert.That(_mods.ModIn(1).Id, Is.EqualTo("dont-blink"));
            Assert.That(_mods.ModIn(4).Id, Is.EqualTo("heavy-metal"));

            var phantom = _inventory.Stored.First(m => m.Id == "phantom-strike");
            _inventory.Equip(phantom, ModSlot.Z);
            yield return Seconds(0.3f);
            Assert.That(_mods.ModIn(0), Is.EqualTo(phantom));
            var slot = Q("mods-row").Children().First();
            Assert.That(slot.ClassListContains("slot--empty"), Is.False);
            Assert.That(slot.Q(className: "slot__rarity").ClassListContains("slot__rarity--elite"), Is.True);
        }

        [UnityTest]
        public IEnumerator Mods_UseCoolsDown_ThenComesBackReady()
        {
            _mods.CooldownSpeed = 20f;
            Assert.That(_mods.Press(1), Is.EqualTo(ModPressResult.Used));
            var slot = Q("mods-row").Children().ElementAt(1);
            yield return null;
            Assert.That(slot.ClassListContains("slot--cooling"), Is.True);
            Assert.That(_mods.KeyFor(1).ClassListContains("kp--down"), Is.True);

            yield return Seconds(0.65f);
            Assert.That(_mods.IsCooling(1), Is.False);
            Assert.That(slot.ClassListContains("slot--ready"), Is.True);
            Assert.That(_mods.KeyFor(1).Hot, Is.True);

            yield return Seconds(1f);
            Assert.That(slot.ClassListContains("slot--ready"), Is.False);
            _mods.CooldownSpeed = 1f;
        }

        [UnityTest]
        public IEnumerator Mods_RefusedPresses_FlashTheButtonRed()
        {
            Assert.That(_mods.Press(0), Is.EqualTo(ModPressResult.Empty));
            Assert.That(_mods.KeyFor(0).ClassListContains("kp--refuse"), Is.True);

            Assert.That(_mods.Press(2), Is.EqualTo(ModPressResult.Used));
            Assert.That(_mods.Press(2), Is.EqualTo(ModPressResult.Cooling));
            Assert.That(Q("mods-row").Children().ElementAt(2).ClassListContains("slot--refuse"), Is.True);

            _mods.SetModState(false);
            Assert.That(_mods.Press(3), Is.EqualTo(ModPressResult.Offline));
            yield return Seconds(0.45f);
            Assert.That(_mods.KeyFor(3).ClassListContains("kp--refuse"), Is.False);
            _mods.SetModState(true);
        }

        [UnityTest]
        public IEnumerator Mods_ModStateOff_DimsTheSlots()
        {
            HudInput.Press(HudAction.ModState);
            yield return Seconds(0.8f);
            Assert.That(_mods.ModStateOn, Is.False);
            var slot = Q("mods-row").Children().ElementAt(1);
            Assert.That(slot.resolvedStyle.opacity, Is.LessThan(0.6f).And.GreaterThan(0.1f));

            HudInput.Press(HudAction.ModState);
            yield return Seconds(0.8f);
            Assert.That(slot.resolvedStyle.opacity, Is.EqualTo(1f).Within(0.02f));
        }

        [UnityTest]
        public IEnumerator Mods_SlotKeysComeFromTheInputEvents()
        {
            HudInput.Press(HudAction.Slot1);
            yield return null;
            Assert.That(_mods.IsCooling(1), Is.True);
            Assert.That(_mods.CooldownRemaining(1), Is.GreaterThan(9f));
        }

        // ---------- prompts ----------

        [UnityTest]
        public IEnumerator Prompts_SwitchToGamepadButtons()
        {
            Assert.That(_mods.KeyFor(0).Shown, Is.EqualTo("Z"));
            Assert.That(_dialogue.Prompt.Shown, Is.EqualTo("E"));

            HudInput.SetDevice(HudDevice.Gamepad);
            yield return null;
            Assert.That(_mods.KeyFor(0).Shown, Is.EqualTo("Square"));
            Assert.That(_mods.KeyFor(1).Shown, Is.EqualTo("Triangle"));
            Assert.That(_mods.KeyFor(0).ClassListContains("kp--round"), Is.True);
            Assert.That(_dialogue.Prompt.Shown, Is.EqualTo("Cross"));
            Assert.That(Q("mods").ClassListContains("mods--pad"), Is.True);
            var hold = Q("mods-head").Q<KeyPrompt>();
            Assert.That(hold.IsUnbound, Is.False);
            Assert.That(hold.Shown, Is.EqualTo("L2"));

            HudInput.SetDevice(HudDevice.Keyboard);
            yield return null;
            Assert.That(_mods.KeyFor(0).Shown, Is.EqualTo("Z"));
            Assert.That(hold.IsUnbound, Is.True);
        }

        [UnityTest]
        public IEnumerator Prompts_FollowRebinding()
        {
            HudInput.SetKey(HudAction.Next, "f");
            yield return null;
            Assert.That(_dialogue.Prompt.Shown, Is.EqualTo("F"));
            HudInput.ResetBindings();
            yield return null;
            Assert.That(_dialogue.Prompt.Shown, Is.EqualTo("E"));
        }

        // ---------- objective ----------

        // Level 1's objectives as the GDD words them, with a button prompt on one step for the test.
        static Directive RaidOnKraisner() => new Directive("LVL 1", "THE RAID ON KRAISNER", "",
            new DirectiveStep("DEFEAT CAROGEN UNITS"),
            new DirectiveStep("ESCAPE THE KRAISNER BUILDING", 1, HudAction.ModState));

        static Directive PatchUp() => new Directive("LVL 0 / C.M.A. TRAINING", "PATCH UP", "Take the health pack from The KRF Porta Box.");

        [UnityTest]
        public IEnumerator Objective_StartsOnTheBuildsDirective()
        {
            Assert.That(_objective.IsShown, Is.True);
            Assert.That(_objective.Title, Is.EqualTo("PATCH UP"));
            Assert.That(L("ob-brief").text, Is.EqualTo("Take the health pack from The KRF Porta Box."));
            Assert.That(L("ob-chip").text, Is.EqualTo("NEW DIRECTIVE"));
            Assert.That(Q("ob-steps").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            yield return null;
        }

        [UnityTest]
        public IEnumerator Objective_StepsTick_ThenTheNextDirectiveArrives()
        {
            _objective.SetDirectives(new[] { RaidOnKraisner(), PatchUp() });
            yield return Seconds(1f);
            Assert.That(_objective.Title, Is.EqualTo("THE RAID ON KRAISNER"));
            Assert.That(Q("ob-steps").childCount, Is.EqualTo(2));
            Assert.That(Q("ob-brief").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            Assert.That(Q("ob-steps")[0].Q<KeyPrompt>(), Is.Null);
            Assert.That(Q("ob-steps")[1].Q<KeyPrompt>().Shown, Is.EqualTo("Q"));
            Assert.That(Q("ob-steps")[1].Q<KeyPrompt>().Hot, Is.True);

            _objective.Progress();
            yield return null;
            Assert.That(Q("ob-steps")[0].ClassListContains("ob-step--done"), Is.True);
            Assert.That(Q("ob-fill").style.width.value.value, Is.EqualTo(50f).Within(0.1f));

            _objective.Progress();
            yield return Seconds(1f);
            Assert.That(Q("ob").ClassListContains("ob--done"), Is.True);
            Assert.That(L("ob-chip").text, Is.EqualTo("COMPLETE"));
            Assert.That(Q("ob-steps")[1].Q<KeyPrompt>().Hot, Is.False);

            yield return Seconds(2.2f);
            Assert.That(_objective.Title, Is.EqualTo("PATCH UP"));
            Assert.That(Q("ob").ClassListContains("ob--shown"), Is.True);
        }

        [UnityTest]
        public IEnumerator Objective_PopsUp_ThenFoldsToItsTitle()
        {
            Assert.That(_objective.IsCompact, Is.False);
            yield return Seconds(5f);
            Assert.That(_objective.IsCompact, Is.True);
            Assert.That(Q("ob-brief").resolvedStyle.display, Is.EqualTo(DisplayStyle.None));

            _objective.Progress();
            yield return null;
            Assert.That(_objective.IsCompact, Is.False);
        }

        [UnityTest]
        public IEnumerator Objective_ChipSettlesToTheDirectiveNumber()
        {
            yield return Seconds(3f);
            Assert.That(L("ob-chip").text, Is.EqualTo("DIRECTIVE 01"));
            Assert.That(Q("ob-chip").ClassListContains("chip--hot"), Is.False);
        }

        // ---------- dialogue ----------

        [UnityTest]
        public IEnumerator Dialogue_TypesSkipsChangesSpeakerAndCloses()
        {
            // a second speaker (CROW, the GDD's drone) so the name chip has to change
            _dialogue.SetSpeakers(new[]
            {
                new DialogueSpeaker { id = "cassidy", displayName = "CASSIDY", unit = "K.R.F. / DIREHOUND" },
                new DialogueSpeaker { id = "crow", displayName = "CROW", unit = "R-STRKR1.3", accentName = true },
            });
            const string line = "What was that?!";
            _dialogue.SetLines(new[]
            {
                new DialogueLine("cassidy", line),
                new DialogueLine("crow", line),
                new DialogueLine("cassidy", line),
            });

            Assert.That(_dialogue.IsOpen, Is.False);
            HudInput.Press(HudAction.Next);
            yield return Seconds(0.2f);
            Assert.That(_dialogue.IsOpen, Is.True);
            Assert.That(_dialogue.IsTyping, Is.True);
            Assert.That(L("dlg-label").text, Is.EqualTo("SKIP"));
            Assert.That(_dialogue.Prompt.Hot, Is.False);

            _dialogue.Advance();
            yield return null;
            Assert.That(_dialogue.IsTyping, Is.False);
            Assert.That(_dialogue.VisibleText, Is.EqualTo(line));
            Assert.That(L("dlg-label").text, Is.EqualTo("NEXT"));
            Assert.That(_dialogue.Prompt.Hot, Is.True);
            Assert.That(_dialogue.Prompt.IsAttention, Is.True);

            _dialogue.Advance();
            yield return Seconds(0.4f);
            Assert.That(_dialogue.Index, Is.EqualTo(1));
            Assert.That(L("dlg-name").text, Is.EqualTo("CROW"));
            Assert.That(Q("dlg-name").ClassListContains("dlg-name--accent"), Is.True);
            AssertColour(Q("dlg-name").resolvedStyle.backgroundColor, "#EBF83F", "second speaker's chip");

            for (int i = 0; i < 4 && _dialogue.Index < 2; i++)
            {
                _dialogue.Advance();
                yield return null;
            }
            yield return Seconds(0.4f);
            Assert.That(_dialogue.Index, Is.EqualTo(2));
            Assert.That(Q("dlg-name").ClassListContains("dlg-name--accent"), Is.False);
            if (_dialogue.IsTyping) _dialogue.Advance();
            yield return null;
            Assert.That(L("dlg-label").text, Is.EqualTo("CLOSE"));

            _dialogue.Advance();
            yield return Seconds(0.6f);
            Assert.That(_dialogue.IsOpen, Is.False);
            Assert.That(Q("dlg").ClassListContains("dlg--open"), Is.False);
        }

        [UnityTest]
        public IEnumerator Dialogue_HasNoPicture_WithTheBuildsLine()
        {
            Assert.That(Q("dlg").Query(className: "pt__img").ToList(), Is.Empty);
            Assert.That(Q("dlg").resolvedStyle.height, Is.EqualTo(192f).Within(0.5f));
            HudInput.Press(HudAction.Next);
            yield return null;
            _dialogue.Advance();
            yield return null;
            Assert.That(_dialogue.VisibleText, Is.EqualTo("What was that?!"));
            Assert.That(L("dlg-label").text, Is.EqualTo("CLOSE"));
        }

        // ---------- every animation, at every size ----------

        static bool Moved(VisualElement e) => e.style.translate.keyword == StyleKeyword.Undefined
            && (e.style.translate.value.x.value != 0f || e.style.translate.value.y.value != 0f);

        [UnityTest]
        public IEnumerator Animations_PlayAtEverySize()
        {
            foreach (var size in new[] { HudScale.Percent70, HudScale.Percent80, HudScale.Percent90, HudScale.Percent100 })
            {
                string at = size + ": ";
                _layout.Size = size;
                _vitals.Full();
                _mods.SetModState(true, true);
                _mods.FinishCooldowns();
                yield return Seconds(1.2f);

                // a hit: a flash, shake, the feed's strips glitch, the number counts down
                int before = _vitals.Health;
                _vitals.Damage(10);
                Assert.That(Q("hp").ClassListContains("hp--hit"), Is.True, at + "hit flash");
                bool shook = false, glitched = false;
                for (float t = 0; t < 0.3f; t += Time.unscaledDeltaTime)
                {
                    shook |= Moved(Q("hp-main"));
                    glitched |= Q("hp-feed-art").Query(className: "pt__img").ToList().Any(Moved);
                    yield return null;
                }
                Assert.That(shook, Is.True, at + "panel shake");
                Assert.That(glitched, Is.True, at + "feed glitch");
                yield return Seconds(0.3f);
                Assert.That(L("hp-value").text, Is.EqualTo((before - 10).ToString()), at + "count");

                // a heal, and an armor plate breaking
                _vitals.Heal(10);
                Assert.That(Q("hp").ClassListContains("hp--healing"), Is.True, at + "heal lead");
                _vitals.HitArmor();
                Assert.That(Q("hp-plates").Children().Any(p => p.ClassListContains("hp-plate--flash")), Is.True, at + "armor flash");

                // a mod: fire, cool down, come back ready, then a refused press on the empty slot
                Assert.That(_mods.Press(1), Is.EqualTo(ModPressResult.Used), at + "mod used");
                var slot = Q("mods-row").Children().ElementAt(1);
                Assert.That(slot.ClassListContains("slot--fire"), Is.True, at + "slot dip");
                Assert.That(_mods.KeyFor(1).ClassListContains("kp--down"), Is.True, at + "key down");
                Assert.That(slot.ClassListContains("slot--cooling"), Is.True, at + "cooling");
                _mods.FinishCooldowns();
                Assert.That(slot.ClassListContains("slot--ready"), Is.True, at + "ready flash");
                Assert.That(_mods.KeyFor(1).Hot, Is.True, at + "ready key");
                Assert.That(_mods.Press(0), Is.EqualTo(ModPressResult.Empty), at + "refused");
                Assert.That(_mods.KeyFor(0).ClassListContains("kp--refuse"), Is.True, at + "red key");

                // mod state off: slots dim and the toggle pings; then back on
                _mods.ToggleModState();
                Assert.That(Q("mods").ClassListContains("mods--online"), Is.False, at + "mod state off");
                Assert.That(Q("mods-toggle-key").ClassListContains("kp--attention"), Is.True, at + "toggle pings");
                _mods.ToggleModState();

                // the objective glitches in again, then completes
                _objective.Refresh();
                yield return Seconds(0.15f);
                Assert.That(Q("ob-title").ClassListContains("chroma--on"), Is.True, at + "title chroma");
                Assert.That(L("ob-chip").text, Is.EqualTo("NEW DIRECTIVE"), at + "new chip");
                yield return Seconds(0.6f);
                _objective.Complete();
                yield return null;
                Assert.That(Q("ob").ClassListContains("ob--done"), Is.True, at + "complete");

                // the dialogue opens and types
                _dialogue.Open();
                yield return Seconds(0.2f);
                Assert.That(Q("dlg").ClassListContains("dlg--open"), Is.True, at + "dialogue open");
                Assert.That(_dialogue.IsTyping, Is.True, at + "typing");
                _dialogue.Close();

                // controller buttons
                HudInput.SetDevice(HudDevice.Gamepad);
                yield return null;
                Assert.That(_mods.KeyFor(0).ClassListContains("kp--round"), Is.True, at + "pad glyphs");
                HudInput.SetDevice(HudDevice.Keyboard);
                yield return Seconds(2.6f);
            }
        }

        // ---------- game-driven: the game owns the data and a bridge tells the HUD ----------

        [UnityTest]
        public IEnumerator GameDriven_ModBarShowsWhatTheGameSays()
        {
            var mod = _mods.ModIn(1);
            Assert.That(mod, Is.Not.Null);
            _mods.GameDriven = true;
            yield return null;
            Assert.That(_mods.ModIn(1), Is.Null, "empty until the game fills it");
            _mods.ShowSlot(1, mod);
            yield return Seconds(0.3f);
            Assert.That(_mods.ModIn(1), Is.EqualTo(mod));

            // 4 active slots, the divider, 2 passive slots
            var row = Q("mods-row").Children().ToList();
            _mods.ShowUnlocked(2, 1);
            yield return null;
            Assert.That(row[2].resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "locked active slot hidden");
            Assert.That(row[1].resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "unlocked slot shown");
            Assert.That(row[6].resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "locked passive slot hidden");

            _mods.ShowUsed(1);
            Assert.That(row[1].ClassListContains("slot--fire"), Is.True, "used");
            _mods.ShowCooldown(1, 3f, 6f);
            Assert.That(_mods.IsCooling(1), Is.True);
            yield return Seconds(0.5f);
            Assert.That(_mods.CooldownRemaining(1), Is.EqualTo(3f), "the game counts down, not the bar");

            _mods.ShowReady(1);
            Assert.That(row[1].ClassListContains("slot--ready"), Is.True);
            Assert.That(_mods.IsCooling(1), Is.False);

            _mods.ShowRefused(0);
            Assert.That(_mods.KeyFor(0).ClassListContains("kp--refuse"), Is.True);

            _mods.ShowBroken(1, true);
            Assert.That(row[1].ClassListContains("slot--broken"), Is.True);

            HudInput.Press(HudAction.Slot1);
            yield return null;
            Assert.That(_mods.IsCooling(1), Is.False, "keys don't fire mods when the game drives the bar");
        }

        [UnityTest]
        public IEnumerator GameDriven_VitalsHideWhatTheGameLacks()
        {
            _vitals.ShowCrow = false;
            _vitals.ShowLives = false;
            _vitals.ShowArmorStat(0f);
            yield return null;
            Assert.That(Q("hp-crow").resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "CROW");
            Assert.That(Q("hp-lives").resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "lives");
            Assert.That(Q("hp-armor").resolvedStyle.display, Is.EqualTo(DisplayStyle.None), "no armor yet");

            _vitals.ShowArmorStat(4f);
            yield return null;
            Assert.That(Q("hp-armor").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex), "armor");
            Assert.That(L("hp-armor-value").text, Is.EqualTo("4"));
            Assert.That(Q("hp-plates").Children().Any(p => p.ClassListContains("hp-plate--broken")), Is.False, "plates full");
        }

        [UnityTest]
        public IEnumerator GameDriven_ObjectiveWaitsForTheGameAfterCompleting()
        {
            _objective.AdvanceAfterComplete = false;
            _objective.LoadDirectives(new[]
            {
                new Directive("LVL 0 / C.M.A. TRAINING", "PATCH UP", "Take the health pack from The KRF Porta Box."),
                new Directive("LVL 1", "THE RAID ON KRAISNER", ""),
            });
            yield return Seconds(0.4f);
            Assert.That(_objective.IsShown, Is.False, "loading the list shows nothing");

            _objective.Show(1);
            yield return Seconds(0.8f);
            Assert.That(_objective.IsShown, Is.True);
            Assert.That(L("ob-count").text, Is.EqualTo("02 / 02"));

            _objective.Complete();
            yield return Seconds(2.2f);
            Assert.That(_objective.IsShown, Is.False, "slides out and waits");
            Assert.That(_objective.Index, Is.EqualTo(1));
        }

        // the game's own objectives (Service Floor), longer than the card's width
        [UnityTest]
        public IEnumerator Objective_LongTitlesFitTheCard()
        {
            const string brief = "The floor gave way. Wall jump off the broken slab to climb back up to Room 8.";
            _objective.LoadDirectives(new[]
            {
                new Directive("", "CLIMB OUT OF THE CRATER", brief),
                new Directive("", brief.ToUpperInvariant(), ""),
            });
            var title = Q("ob-title");
            var text = title.Q<Label>(className: "chroma__text");

            _objective.Show(0);
            yield return Seconds(0.8f);
            float width = text.MeasureTextSize(text.text, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined).x;
            Assert.That(width, Is.LessThanOrEqualTo(title.contentRect.width + 0.5f), "shrinks onto one line");
            Assert.That(text.resolvedStyle.fontSize, Is.LessThan(54f).And.GreaterThanOrEqualTo(37f));

            _objective.Show(1);
            yield return Seconds(0.8f);
            Assert.That(text.resolvedStyle.whiteSpace, Is.EqualTo(WhiteSpace.Normal), "too long for one line: wraps");
            Assert.That(text.layout.width, Is.LessThanOrEqualTo(title.contentRect.width + 0.5f));
            Assert.That(title.layout.height, Is.GreaterThan(60f), "the card grows for the second line");

            _objective.SetCompact(true);
            yield return Seconds(0.3f);
            Assert.That(text.layout.width, Is.LessThanOrEqualTo(title.contentRect.width + 0.5f), "folded");
        }

        [UnityTest]
        public IEnumerator Prompts_DrawTheDpad()
        {
            HudInput.SetPadButton(HudAction.Slot0, PadButton.DpadUp);
            HudInput.SetDevice(HudDevice.Gamepad);
            yield return null;
            var key = _mods.KeyFor(0);
            Assert.That(key.ClassListContains("kp--round"), Is.True);
            Assert.That(key.Q<PadGlyph>().Glyph, Is.EqualTo(PadButton.DpadUp));
        }

        // ---------- motion ----------

        [UnityTest]
        public IEnumerator ReducedMotion_ScrambleSetsTextAtOnce()
        {
            HudFx.ReducedMotion = true;
            var label = L("hp-crow-label");
            _vitals.SetCrow(CrowState.Deployed);
            Assert.That(label.text, Is.EqualTo("CROW DEPLOYED"));
            yield return null;
        }
    }
}
