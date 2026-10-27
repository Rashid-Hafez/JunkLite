using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace junklite
{
    /// <summary>
    /// Upgrades tab page. One authored column per <see cref="UpgradeBranch"/>; node cards are copies of
    /// an authored, inactive template placed by the columns' layout groups, so code never positions anything.
    /// Reads <see cref="PlayerUpgrades"/> and refreshes on its Changed event.
    /// </summary>
    public sealed class UpgradesPageView : MonoBehaviour
    {
        [Tooltip("Inactive authored node card; copies are made from it.")]
        [SerializeField] private UpgradeNodeView nodeTemplate;
        [Tooltip("Node parents in UpgradeBranch order: Blades, Vitality, Jump, Dash, Mods.")]
        [SerializeField] private RectTransform[] branchColumns = Array.Empty<RectTransform>();
        [SerializeField] private TMP_Text pointsText;
        [SerializeField] private UpgradeDetailsView details;
        [Tooltip("Optional. Shown when no tree is configured.")]
        [SerializeField] private TMP_Text emptyLabel;

        private readonly List<UpgradeNodeView> views = new();
        private readonly List<UpgradeNode> scratch = new();
        private PlayerUpgrades upgrades;
        private UpgradeNodeView selected;
        private UpgradeTreeConfig builtTree;

        public IReadOnlyList<UpgradeNodeView> Views => views;
        public UpgradeNode SelectedNode => selected != null ? selected.Node : null;

        private void Awake()
        {
            if (details != null) details.BuyRequested += HandleBuy;
        }

        private void OnEnable()
        {
            Rebind();
            // Cards are rebuilt only when the tree changes; reopening the tab just refreshes states.
            if (builtTree != (upgrades != null ? upgrades.Config : null) || views.Count == 0) Rebuild();
            else Refresh();
            if (Gamepad.current != null) SelectDefault();
        }

        private void OnDisable()
        {
            if (upgrades != null) upgrades.Changed -= Refresh;
            upgrades = null;
        }

        private void OnDestroy()
        {
            if (details != null) details.BuyRequested -= HandleBuy;
        }

        private void Rebind()
        {
            PlayerUpgrades service = PlayerUpgrades.Instance;
            if (service == upgrades) return;
            if (upgrades != null) upgrades.Changed -= Refresh;
            upgrades = service;
            if (upgrades != null) upgrades.Changed += Refresh;
        }

        /// <summary>Creates one card per tree node inside its branch column, then refreshes states.</summary>
        public void Rebuild()
        {
            foreach (UpgradeNodeView view in views)
                if (view != null) Destroy(view.gameObject);
            views.Clear();
            selected = null;

            UpgradeTreeConfig tree = upgrades != null ? upgrades.Config : null;
            builtTree = tree;
            bool hasTree = tree != null && tree.nodes != null && tree.nodes.Count > 0;
            if (emptyLabel != null) emptyLabel.gameObject.SetActive(!hasTree);
            if (hasTree && nodeTemplate != null)
            {
                if (nodeTemplate.gameObject.activeSelf) nodeTemplate.gameObject.SetActive(false);
                foreach (UpgradeBranch branch in Enum.GetValues(typeof(UpgradeBranch)))
                {
                    int column = (int)branch;
                    if (column < 0 || column >= branchColumns.Length || branchColumns[column] == null) continue;
                    tree.GetBranch(branch, scratch);
                    for (int i = 0; i < scratch.Count; i++)
                    {
                        UpgradeNodeView view = Instantiate(nodeTemplate, branchColumns[column], false);
                        view.name = $"Node {scratch[i].id}";
                        view.gameObject.SetActive(true);
                        view.Focused += HandleFocused;
                        view.Confirmed += HandleConfirmed;
                        view.Bind(scratch[i], ResolveState(scratch[i]), showLink: i > 0);
                        views.Add(view);
                    }
                }
            }
            details?.Clear();
            Refresh();
        }

        public void Refresh()
        {
            if (pointsText != null)
                pointsText.text = upgrades != null ? upgrades.AvailablePoints.ToString() : "0";
            foreach (UpgradeNodeView view in views)
            {
                if (view == null || view.Node == null) continue;
                int indexInColumn = view.transform.GetSiblingIndex() - (nodeTemplate != null && nodeTemplate.transform.parent == view.transform.parent ? 1 : 0);
                view.Bind(view.Node, ResolveState(view.Node), showLink: indexInColumn > 0);
                view.SetSelected(view == selected);
            }
            if (selected != null) ShowDetails(selected);
        }

        /// <summary>Focuses the first affordable node (or the first node) for gamepad users.</summary>
        public void SelectDefault()
        {
            if (views.Count == 0 || EventSystem.current == null) return;
            UpgradeNodeView target = views.Find(v => v.State == UpgradeNodeState.Available) ?? views[0];
            EventSystem.current.SetSelectedGameObject(target.gameObject);
            HandleFocused(target);
        }

        private UpgradeNodeState ResolveState(UpgradeNode node)
        {
            if (upgrades == null) return UpgradeNodeState.Locked;
            UpgradeLockReason reason = upgrades.GetLockReason(node);
            return reason switch
            {
                UpgradeLockReason.None => UpgradeNodeState.Available,
                UpgradeLockReason.Owned => UpgradeNodeState.Owned,
                UpgradeLockReason.NoPoints => UpgradeNodeState.Unaffordable,
                UpgradeLockReason.NotInBuild => UpgradeNodeState.Unavailable,
                _ => UpgradeNodeState.Locked,
            };
        }

        private void HandleFocused(UpgradeNodeView view)
        {
            if (view == null || view.Node == null) return;
            if (selected != null && selected != view) selected.SetSelected(false);
            selected = view;
            view.SetSelected(true);
            ShowDetails(view);
        }

        private void HandleConfirmed(UpgradeNodeView view)
        {
            HandleFocused(view);
            if (view != null && view.State == UpgradeNodeState.Available) HandleBuy(view.Node);
        }

        private void ShowDetails(UpgradeNodeView view)
        {
            if (details == null || view == null) return;
            UpgradeLockReason reason = upgrades != null ? upgrades.GetLockReason(view.Node) : UpgradeLockReason.Unknown;
            details.Show(view.Node, view.State, reason, upgrades != null ? upgrades.AvailablePoints : 0);
        }

        private void HandleBuy(UpgradeNode node)
        {
            if (upgrades == null || node == null) return;
            upgrades.TryPurchase(node); // Changed refreshes every card and the details.
        }
    }
}
