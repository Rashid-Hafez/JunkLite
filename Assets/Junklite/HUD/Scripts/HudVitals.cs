using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;
using Random = UnityEngine.Random;

namespace Junklite.HUD
{
    public enum CrowState
    {
        Ready,
        Deployed,
        Recharging,
    }

    public enum CasFace
    {
        Calm,
        Shout,
        Angry,
    }

    /// <summary>
    /// Cas's vitals (HUD/health): her feed (a placeholder box until her headshots are added), health as big digits over a
    /// segmented bar, armor plates and the CROW drone's status. A hit flashes the bar, the fill snaps down
    /// and a red trail drains after it; a heal sends a cyan lead ahead. Under 40% the bar turns yellow (LOW),
    /// under 20% red with a blinking CRITICAL chip. At zero, CROW deploys and revives Cas to 40% (GDD).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class HudVitals : MonoBehaviour
    {
        const int Segments = 10;
        const string ScanTag = "KRF-0636";

        [Min(1)] [SerializeField] int maxHealth = 150;
        [Min(0)] [SerializeField] int health = 132;
        [Range(1, 8)] [SerializeField] int plates = 4;
        [Min(0)] [SerializeField] int armor = 4;
        [Min(0)] [SerializeField] int lives = 3;
        [SerializeField] string feedId = ScanTag;

        [Header("Cas's feed")]
        [Tooltip("Empty for now: the feed is a placeholder box. Her headshots go in these three slots; with none, hits still glitch the box.")]
        [SerializeField] Texture2D calmFace;
        [SerializeField] Texture2D shoutFace;
        [SerializeField] Texture2D angryFace;

        [Tooltip("The feed flickers every 5 to 9 seconds.")]
        [SerializeField] bool idleFlicker = true;

        [Header("What the game has")]
        [Tooltip("Show CROW's status (ready, deployed, recharging). Off in a game without CROW.")]
        [SerializeField] bool showCrow = true;

        [Tooltip("Show the lives count on the feed. Off in a game without lives.")]
        [SerializeField] bool showLives = true;

        [Tooltip("Show the armor row. ShowArmorStat hides it while the armor is 0.")]
        [SerializeField] bool showArmor = true;

        [Header("CROW")]
        [Tooltip("At zero health CROW deploys and revives Cas, as in the GDD. Turn off if the game handles going down itself.")]
        [SerializeField] bool crowRevives = true;

        [Range(0.05f, 1f)] [SerializeField] float reviveFraction = 0.4f;

        [SerializeField] UnityEvent onDown = new UnityEvent();
        [SerializeField] UnityEvent onRevived = new UnityEvent();

        VisualElement _root;
        VisualElement _bound;
        VisualElement _feed;
        VisualElement _main;
        VisualElement _readout;
        VisualElement _armorRow;
        VisualElement _fill;
        VisualElement _trail;
        VisualElement _heal;
        VisualElement _crow;
        Label _value;
        Label _max;
        Label _state;
        Label _armorValue;
        Label _crowLabel;
        Label _lives;
        Label _feedIdLabel;
        VisualElement _shieldBar;
        Label _shieldLabel;
        readonly List<VisualElement> _plates = new List<VisualElement>();
        SlicedPortrait _portrait;

        CrowState _crowState = CrowState.Ready;
        CasFace _face = CasFace.Calm;
        HudFx.Timer _faceTimer;
        HudFx.Timer _blink;
        HudFx.Timer _hit;
        HudFx.Timer _healing;
        HudFx.Timer _idle;
        HudFx.Timer _shieldHit;
        HudFx.Timer _shieldIn;
        int _shield;
        int _shieldMax;
        bool _busy;

        public event Action Down;
        public event Action Revived;

        public int Health => health;
        public int MaxHealth => maxHealth;
        public int Armor => armor;
        public int Plates => plates;
        public CrowState Crow => _crowState;
        public CasFace Face => _face;

        /// <summary>The shield's points left (Pulse Barrier), 0 when there's none.</summary>
        public int Shield => _shield;
        // whole-number checks, so exactly 40% (60 / 150, the CROW revive) reads STABLE as in the prototype
        public bool ShowCrow { get => showCrow; set { showCrow = value; ApplyParts(); } }
        public bool ShowLives { get => showLives; set { showLives = value; ApplyParts(); } }
        public bool ShowArmor { get => showArmor; set { showArmor = value; ApplyParts(); } }

        public bool IsLow => !IsCritical && health * 5 < maxHealth * 2;
        public bool IsCritical => health * 5 < maxHealth;
        public VisualElement Root => _root;

        void OnEnable()
        {
            var doc = GetComponent<UIDocument>().rootVisualElement;
            _root = doc?.Q("hp");
            if (_root == null)
            {
                Debug.LogError("Vitals UI is missing. Check that the UI Document's Source Asset is Hud.uxml.", this);
                return;
            }

            if (_root != _bound)
            {
                _bound = _root;
                Bind();
            }

            health = Mathf.Clamp(health, 0, maxHealth);
            armor = Mathf.Clamp(armor, 0, plates);
            _busy = false;
            _shield = 0;
            _crowState = CrowState.Ready;
            _face = IsCritical ? CasFace.Angry : CasFace.Calm;
            _portrait.Set(FaceTexture(_face), glitch: false, force: true);

            HudFx.SetText(_value, health.ToString());
            HudFx.SetText(_max, $"/ {maxHealth}");
            HudFx.SetText(_lives, $"×{HudFx.Pad(lives)}");
            HudFx.SetText(_feedIdLabel, feedId);
            SetBars();
            PaintPlates();
            SetCrowClass(CrowState.Ready);
            HudFx.SetText(_crowLabel, CrowText(CrowState.Ready));
            ApplyParts();
            UpdateState(false);
            ScheduleIdle();
        }

        void OnDisable()
        {
            _faceTimer?.Cancel();
            _blink?.Cancel();
            _idle?.Cancel();
            _hit?.Cancel();
            _healing?.Cancel();
            _shieldHit?.Cancel();
            _shieldIn?.Cancel();
            StopAllCoroutines();
        }

        void Bind()
        {
            _feed = _root.Q("hp-feed");
            _main = _root.Q("hp-main");
            _readout = _root.Q("hp-readout");
            _armorRow = _root.Q("hp-armor");
            _fill = _root.Q("hp-fill");
            _trail = _root.Q("hp-trail");
            _heal = _root.Q("hp-heal");
            _crow = _root.Q("hp-crow");
            _value = _root.Q<Label>("hp-value");
            _max = _root.Q<Label>("hp-max");
            _state = _root.Q<Label>("hp-state");
            _armorValue = _root.Q<Label>("hp-armor-value");
            _crowLabel = _root.Q<Label>("hp-crow-label");
            _lives = _root.Q<Label>("hp-lives");
            _feedIdLabel = _root.Q<Label>("hp-feed-id");
            _shieldBar = _root.Q("hp-shield-bar");
            _shieldLabel = _root.Q<Label>("hp-shield");
            _root.Query<Label>().ForEach(l => l.enableRichText = false);

            var gaps = _root.Q("hp-gaps");
            gaps.Clear();
            for (int i = 1; i < Segments; i++)
            {
                var gap = new VisualElement { pickingMode = PickingMode.Ignore };
                gap.AddToClassList("hp-bar__gap");
                gap.style.left = Length.Percent(i * 100f / Segments);
                gaps.Add(gap);
            }

            var plateRow = _root.Q("hp-plates");
            plateRow.Clear();
            _plates.Clear();
            for (int i = 0; i < plates; i++)
            {
                var plate = new VisualElement { pickingMode = PickingMode.Ignore };
                plate.AddToClassList("hp-plate");
                plateRow.Add(plate);
                _plates.Add(plate);
            }

            _portrait = new SlicedPortrait(_root.Q("hp-feed-art"), 8, calmFace);
        }

        // ---------- public calls ----------

        /// <summary>A hit: a flash, the fill snaps down, the red trail drains after it, the number counts down and Cas shouts.</summary>
        public void Damage(int amount = 22)
        {
            if (_root == null || _busy || health <= 0 || amount <= 0) return;
            int before = health;
            health = Mathf.Max(0, health - amount);

            float p = Percent();
            _fill.style.width = Length.Percent(p);
            _heal.style.width = Length.Percent(p);
            _trail.style.width = Length.Percent(p);   // same target; the trail's transition delay makes it lag

            _hit?.Cancel();
            _hit = HudFx.Pulse(_root, "hp--hit", 0.16f);
            HudFx.Count(_value, before, health, 320);
            HudFx.Tear(new[] { _feed, _readout }, new TearOptions { frames = 4, amp = 14, hot = 0.2f, cold = 0f });
            HudFx.Shake(_main, 4f);
            UpdateState(true);

            bool critical = IsCritical;
            SetFace(critical ? CasFace.Angry : CasFace.Shout, critical ? 0f : 0.9f);
            if (health <= 0)
            {
                Down?.Invoke();
                onDown?.Invoke();
                if (crowRevives) StartCoroutine(DownRoutine());
            }
        }

        /// <summary>A health pack: a cyan lead jumps ahead, the fill follows it up and the number counts up.</summary>
        public void Heal(int amount = 40)
        {
            if (_root == null || _busy || health >= maxHealth || amount <= 0) return;
            int before = health;
            health = Mathf.Min(maxHealth, health + amount);

            _root.AddToClassList("hp--healing");
            HudFx.NextFrames(SetBars);
            _healing?.Cancel();
            _healing = HudFx.After(0.9f, () => _root.RemoveFromClassList("hp--healing"));

            HudFx.Count(_value, before, health, 700);
            HudFx.Tear(new[] { _readout }, new TearOptions { frames = 3, amp = 8, hot = 0f, cold = 0.6f });
            _faceTimer?.Cancel();
            _faceTimer = null;
            UpdateState(true);
            if (!IsCritical) SetFace(CasFace.Calm);
        }

        /// <summary>
        /// The shield (the Pulse Barrier mod): a cyan band along the top of the bar shows its share left, its points
        /// show beside her health (+100), and the bar's frame lights cyan while it's up. Call it whenever the shield
        /// changes: it comes up glitching in, flashes when a hit lands on it, and drops at 0 (a sharper glitch when
        /// <paramref name="broken"/>, i.e. a hit broke it rather than it running out).
        /// </summary>
        public void SetShield(int points, int max, bool broken = false)
        {
            if (_root == null || _shieldBar == null) return;
            points = Mathf.Max(0, points);
            int before = _shield;
            _shield = points;
            _shieldMax = Mathf.Max(1, Mathf.Max(points, max));
            var readout = new[] { _readout };

            if (points <= 0)
            {
                if (before <= 0) return;
                _shieldIn?.Cancel();
                _root.RemoveFromClassList("hp--shielded");
                _shieldBar.style.width = Length.Percent(0f);
                HudFx.Tear(readout, new TearOptions { frames = broken ? 5 : 3, amp = broken ? 16 : 8, hot = broken ? 0.3f : 0f, cold = 0.6f });
                return;
            }

            float share = 100f * _shield / _shieldMax;
            HudFx.SetText(_shieldLabel, $"+{_shield}");
            if (before <= 0)
            {
                // comes up from empty: the band fills once it's on screen
                _root.AddToClassList("hp--shielded");
                _shieldIn?.Cancel();
                _shieldIn = HudFx.NextFrames(() => _shieldBar.style.width = Length.Percent(share));
                HudFx.Tear(new[] { _readout, _root.Q("hp-bar") }, new TearOptions { frames = 4, amp = 12, hot = 0f, cold = 0.8f });
                return;
            }

            _shieldBar.style.width = Length.Percent(share);
            if (points < before)
            {
                _shieldHit?.Cancel();
                _shieldHit = HudFx.Pulse(_root, "hp--shield-hit", 0.16f);
                HudFx.Tear(readout, new TearOptions { frames = 3, amp = 10, hot = 0f, cold = 0.6f });
            }
        }

        /// <summary>Sets health without animation, for loading a save or starting a level.</summary>
        public void SetHealth(int current, int max)
        {
            maxHealth = Mathf.Max(1, max);
            health = Mathf.Clamp(current, 0, maxHealth);
            if (_root == null) return;
            HudFx.SetText(_value, health.ToString());
            HudFx.SetText(_max, $"/ {maxHealth}");
            SetBars();
            UpdateState(false);
        }

        /// <summary>An armor plate flashes and breaks. With no plates left the row rattles.</summary>
        public void HitArmor()
        {
            if (_root == null) return;
            if (armor <= 0)
            {
                HudFx.Shake(_armorRow, 4f);
                return;
            }

            var plate = _plates[armor - 1];
            armor--;
            plate.AddToClassList("hp-plate--flash");
            HudFx.After(0.09f, () =>
            {
                plate.RemoveFromClassList("hp-plate--flash");
                plate.AddToClassList("hp-plate--broken");
            });
            HudFx.SetText(_armorValue, $"{armor} / {plates}");
            HudFx.Tear(new[] { _armorRow }, new TearOptions { frames = 4, amp = 12, hot = 0f, cold = 0.5f });
            SetFace(IsCritical ? CasFace.Angry : CasFace.Shout, 0.7f);
        }

        /// <summary>All plates come back, one after another.</summary>
        public void RepairArmor()
        {
            armor = plates;
            if (_root == null) return;
            for (int i = 0; i < _plates.Count; i++)
            {
                var plate = _plates[i];
                HudFx.After(i * 0.07f, () => plate.RemoveFromClassList("hp-plate--broken"));
            }
            HudFx.SetText(_armorValue, $"{armor} / {plates}");
        }

        /// <summary>Sets the armor without animation.</summary>
        public void SetArmor(int value)
        {
            armor = Mathf.Clamp(value, 0, plates);
            PaintPlates();
        }

        /// <summary>
        /// Armor as a flat stat (every hit is reduced by it), not plates that break: all the plates full and the
        /// number shown. 0 hides the row.
        /// </summary>
        public void ShowArmorStat(float value)
        {
            int amount = Mathf.Max(0, Mathf.RoundToInt(value));
            armor = plates;
            ShowArmor = amount > 0;
            if (_root == null) return;
            PaintPlates();
            HudFx.SetText(_armorValue, amount.ToString());
        }

        public void SetLives(int value)
        {
            lives = Mathf.Max(0, value);
            HudFx.SetText(_lives, $"×{HudFx.Pad(lives)}");
        }

        public void SetCrow(CrowState state)
        {
            _crowState = state;
            if (_root == null) return;
            SetCrowClass(state);
            HudFx.Scramble(_crowLabel, 220, CrowText(state));
        }

        /// <summary>READY, DEPLOYED, RECHARGING, back to READY.</summary>
        public void CycleCrow() => SetCrow((CrowState)(((int)_crowState + 1) % 3));

        /// <summary>The feed's idle flicker, once.</summary>
        public void Flicker() => _portrait?.Flicker();

        /// <summary>Back to full health and armor, CROW ready.</summary>
        public void Full()
        {
            _faceTimer?.Cancel();
            _faceTimer = null;
            Heal(maxHealth);
            RepairArmor();
            SetCrow(CrowState.Ready);
        }

        /// <summary>Takes Cas to zero health, so CROW revives her.</summary>
        public void GoDown() => Damage(health);

        // ---------- internals ----------

        IEnumerator DownRoutine()
        {
            _busy = true;
            yield return new WaitForSecondsRealtime(0.5f);
            SetCrow(CrowState.Deployed);
            HudFx.Tear(_root.Query(className: "tear").ToList(), new TearOptions { frames = 6, amp = 24, hot = 0.1f, cold = 0.5f });
            yield return new WaitForSecondsRealtime(1.5f);
            _busy = false;
            Heal(Mathf.RoundToInt(maxHealth * reviveFraction));
            Revived?.Invoke();
            onRevived?.Invoke();
            yield return new WaitForSecondsRealtime(0.9f);
            SetCrow(CrowState.Recharging);
        }

        float Percent() => health * 100f / maxHealth;

        void SetBars()
        {
            var w = Length.Percent(Percent());
            _fill.style.width = w;
            _heal.style.width = w;
            _trail.style.width = w;
        }

        void ApplyParts()
        {
            if (_root == null) return;
            _root.EnableInClassList("hp--no-crow", !showCrow);
            _root.EnableInClassList("hp--no-lives", !showLives);
            _root.EnableInClassList("hp--no-armor", !showArmor);
        }

        void PaintPlates()
        {
            if (_root == null) return;
            for (int i = 0; i < _plates.Count; i++) _plates[i].EnableInClassList("hp-plate--broken", i >= armor);
            HudFx.SetText(_armorValue, $"{armor} / {plates}");
        }

        void UpdateState(bool animate)
        {
            bool critical = IsCritical;
            bool low = IsLow;
            _root.EnableInClassList("hp--critical", critical);
            _root.EnableInClassList("hp--low", low);

            string text;
            string modifier;
            if (health <= 0) { text = "DOWN"; modifier = "chip--alert"; }
            else if (critical) { text = "CRITICAL"; modifier = "chip--alert"; }
            else if (low) { text = "LOW"; modifier = "chip--hot"; }
            else { text = "STABLE"; modifier = "chip--ghost"; }

            _state.RemoveFromClassList("chip--alert");
            _state.RemoveFromClassList("chip--hot");
            _state.RemoveFromClassList("chip--ghost");
            _state.AddToClassList(modifier);
            if (HudFx.FinalText(_state) != text)
            {
                if (animate) HudFx.Scramble(_state, 200, text);
                else HudFx.SetText(_state, text);
            }

            _blink?.Cancel();
            _blink = null;
            _state.RemoveFromClassList("hp-state--off");
            if (critical)
            {
                _blink = HudFx.Every(0.42f, 0.42f, () =>
                {
                    _state.ToggleInClassList("hp-state--off");
                    return true;
                });
                if (_face != CasFace.Angry && _faceTimer == null) SetFace(CasFace.Angry);
            }
        }

        void SetFace(CasFace face, float holdSeconds = 0f)
        {
            _faceTimer?.Cancel();
            _faceTimer = null;
            if (face != _face)
            {
                _face = face;
                // forced, so the strips still glitch on a hit while the feed is an empty placeholder box
                _portrait.Set(FaceTexture(face), 6, 26f, force: true);
            }
            if (holdSeconds > 0f)
            {
                _faceTimer = HudFx.After(holdSeconds, () =>
                {
                    _faceTimer = null;
                    SetFace(IsCritical ? CasFace.Angry : CasFace.Calm);
                });
            }
        }

        Texture2D FaceTexture(CasFace face)
        {
            switch (face)
            {
                case CasFace.Shout: return shoutFace != null ? shoutFace : calmFace;
                case CasFace.Angry: return angryFace != null ? angryFace : calmFace;
                default: return calmFace;
            }
        }

        void SetCrowClass(CrowState state)
        {
            _crow.EnableInClassList("hp-crow--ready", state == CrowState.Ready);
            _crow.EnableInClassList("hp-crow--deployed", state == CrowState.Deployed);
            _crow.EnableInClassList("hp-crow--recharging", state == CrowState.Recharging);
        }

        static string CrowText(CrowState state)
        {
            switch (state)
            {
                case CrowState.Deployed: return "CROW DEPLOYED";
                case CrowState.Recharging: return "CROW RECHARGING";
                default: return "CROW READY";
            }
        }

        void ScheduleIdle()
        {
            _idle?.Cancel();
            if (!idleFlicker) return;
            _idle = HudFx.After(Random.Range(5f, 9f), () =>
            {
                if (!isActiveAndEnabled) return;
                _portrait.Flicker();
                ScheduleIdle();
            });
        }
    }
}
