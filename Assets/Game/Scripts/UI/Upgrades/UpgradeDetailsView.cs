using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace junklite
{
    /// <summary>
    /// The details card of the Upgrades page: name, branch/tier, description, effects, cost, status and
    /// the BUY button. Binds text and toggles authored objects only.
    /// </summary>
    public sealed class UpgradeDetailsView : MonoBehaviour
    {
        [SerializeField] private TMP_Text title;
        [SerializeField] private TMP_Text meta;
        [SerializeField] private TMP_Text description;
        [SerializeField] private TMP_Text effects;
        [SerializeField] private TMP_Text status;
        [SerializeField] private Image icon;
        [SerializeField] private Button buyButton;
        [SerializeField] private TMP_Text buyLabel;
        [Tooltip("Shown while no node is selected.")]
        [SerializeField] private GameObject emptyState;
        [Tooltip("Everything that belongs to a selected node; hidden with the empty state.")]
        [SerializeField] private GameObject content;

        public UpgradeNode Node { get; private set; }
        /// <summary>Raised when the player presses BUY on the shown node.</summary>
        public event Action<UpgradeNode> BuyRequested;

        private void Awake()
        {
            if (buyButton != null) buyButton.onClick.AddListener(() => { if (Node != null) BuyRequested?.Invoke(Node); });
        }

        public void Show(UpgradeNode node, UpgradeNodeState state, UpgradeLockReason reason, int availablePoints)
        {
            Node = node;
            if (node == null) { Clear(); return; }
            if (emptyState != null) emptyState.SetActive(false);
            if (content != null) content.SetActive(true);
            if (title != null) title.text = node.Title.ToUpperInvariant();
            if (meta != null) meta.text = $"{BranchName(node.branch)}  •  TIER {node.tier}";
            if (description != null) description.text = node.description ?? "";
            if (icon != null)
            {
                icon.sprite = node.icon;
                icon.enabled = node.icon != null;
            }
            if (effects != null) effects.text = DescribeEffects(node);
            if (status != null) status.text = StatusText(node, state, reason, availablePoints);
            bool canBuy = state == UpgradeNodeState.Available;
            if (buyButton != null) buyButton.interactable = canBuy;
            if (buyLabel != null)
                buyLabel.text = state == UpgradeNodeState.Owned ? "OWNED"
                    : state == UpgradeNodeState.Unavailable ? "UNAVAILABLE"
                    : $"BUY  —  {node.cost} POINT{(node.cost == 1 ? "" : "S")}";
        }

        public void Clear()
        {
            Node = null;
            if (emptyState != null) emptyState.SetActive(true);
            if (content != null) content.SetActive(false);
            if (buyButton != null) buyButton.interactable = false;
        }

        public static string BranchName(UpgradeBranch branch) => branch switch
        {
            UpgradeBranch.Blades => "BLADES",
            UpgradeBranch.Vitality => "VITALITY",
            UpgradeBranch.Jump => "JUMP",
            UpgradeBranch.Dash => "DASH",
            UpgradeBranch.Mods => "MODS",
            _ => branch.ToString().ToUpperInvariant(),
        };

        private static string DescribeEffects(UpgradeNode node)
        {
            if (node.effects == null || node.effects.Length == 0) return "";
            var builder = new StringBuilder();
            foreach (UpgradeEffect effect in node.effects)
            {
                if (builder.Length > 0) builder.Append('\n');
                builder.Append(Describe(effect));
            }
            return builder.ToString();
        }

        private static string Describe(UpgradeEffect effect)
        {
            switch (effect.kind)
            {
                case UpgradeEffectKind.UnlockAbility:
                    return effect.ability switch
                    {
                        PlayerAbility.Dash => "UNLOCKS  DASH",
                        PlayerAbility.DoubleJump => "UNLOCKS  DOUBLE JUMP",
                        PlayerAbility.DashStrike => "UNLOCKS  DASH STRIKE",
                        _ => $"UNLOCKS  {effect.ability}".ToUpperInvariant(),
                    };
                case UpgradeEffectKind.ModSlot:
                    return effect.slotKind == ModSlotKind.Active ? "+1  ACTIVE MOD SLOT" : "+1  PASSIVE MOD SLOT";
                default:
                    string stat = effect.stat switch
                    {
                        PlayerStat.MaxHealth => "MAX HEALTH",
                        PlayerStat.OutgoingDamage => "DAMAGE",
                        PlayerStat.Armor => "ARMOR",
                        PlayerStat.MoveSpeed => "MOVE SPEED",
                        PlayerStat.JumpForce => "JUMP HEIGHT",
                        PlayerStat.DoubleJumpForce => "DOUBLE JUMP",
                        PlayerStat.DashCooldown => "DASH COOLDOWN",
                        PlayerStat.AttackSpeed => "ATTACK SPEED",
                        PlayerStat.FinisherForce => "FINISHER FORCE",
                        _ => effect.stat.ToString().ToUpperInvariant(),
                    };
                    var parts = new StringBuilder();
                    if (effect.flat != 0f) parts.Append(effect.flat.ToString("+0.##;-0.##"));
                    if (effect.percent != 0f)
                    {
                        if (parts.Length > 0) parts.Append(", ");
                        parts.Append(effect.percent.ToString("+0.##;-0.##")).Append('%');
                    }
                    return $"{parts}  {stat}";
            }
        }

        private static string StatusText(UpgradeNode node, UpgradeNodeState state, UpgradeLockReason reason, int availablePoints)
        {
            switch (state)
            {
                case UpgradeNodeState.Owned: return "OWNED";
                case UpgradeNodeState.Unavailable: return "CURRENTLY UNAVAILABLE";
                case UpgradeNodeState.Available: return $"{availablePoints} POINT{(availablePoints == 1 ? "" : "S")} AVAILABLE";
                case UpgradeNodeState.Unaffordable:
                    return $"NEEDS {node.cost} POINT{(node.cost == 1 ? "" : "S")}  —  {availablePoints} AVAILABLE";
                default:
                    if (reason == UpgradeLockReason.MissingWeapon && node.requiredWeapon != null)
                        return $"FIND THE {node.requiredWeapon.displayName.ToUpperInvariant()} FIRST";
                    return "REQUIRES THE PREVIOUS UPGRADE";
            }
        }
    }
}
