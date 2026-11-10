using System;
using System.Collections.Generic;
using Junklite.Inventory;
using UnityEngine;
using UnityEngine.UIElements;

namespace junklite
{
    /// <summary>
    /// Upgrades tab of the UI Toolkit inventory (Assets/Junklite/Inventory). Draws the upgrade tree from
    /// <see cref="PlayerUpgrades"/> into the screen's "upgrades" element and buys through it.
    /// The structure is built here as elements with USS classes; every colour, font and size lives in
    /// Inventory.uss ("Upgrades" section), as with the package's own Missions cards.
    /// Hover or focus selects a node; click, Enter or the gamepad's submit buys it when it is available.
    /// Lives on the inventory screen prefab next to <see cref="InventoryScreen"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UpgradesScreenView : MonoBehaviour
    {
        private const string NodeClass = "upgrade-node";
        private const string SelectedClass = "upgrade-node--selected";

        [SerializeField] private InventoryScreen screen;

        private static readonly string[] StateClasses =
        {
            "upgrade-node--locked", "upgrade-node--unaffordable", "upgrade-node--available",
            "upgrade-node--owned", "upgrade-node--unavailable",
        };

        private sealed class NodeView
        {
            public UpgradeNode Node;
            public VisualElement Root;
            public Label Meta;
            public UpgradeNodeState State;
        }

        private readonly List<NodeView> nodes = new();
        private readonly List<UpgradeNode> scratch = new();
        private PlayerUpgrades upgrades;
        private UpgradeTreeConfig builtTree;
        private VisualElement builtRoot;
        private NodeView selected;
        private bool shown;

        private Label points;
        private Label detailKicker, detailTitle, detailDescription, detailEffects, detailStatus, buyLabel;
        private VisualElement detail, buy;

        public bool IsShown => shown;
        public UpgradeNode SelectedNode => selected?.Node;

        private void Awake()
        {
            if (screen == null) screen = GetComponent<InventoryScreen>();
        }

        private void OnEnable()
        {
            if (screen != null) screen.TabShown += HandleTabShown;
        }

        private void OnDisable()
        {
            if (screen != null) screen.TabShown -= HandleTabShown;
            SetShown(false);
        }

        private void HandleTabShown(InventoryTab tab) => SetShown(tab == InventoryTab.Upgrades);

        private void SetShown(bool value)
        {
            shown = value;
            PlayerUpgrades service = value ? PlayerUpgrades.Instance : null;
            if (service != upgrades)
            {
                if (upgrades != null) upgrades.Changed -= Refresh;
                upgrades = service;
                if (upgrades != null) upgrades.Changed += Refresh;
            }
            if (!value) return;

            VisualElement root = screen != null ? screen.UpgradesRoot : null;
            UpgradeTreeConfig tree = upgrades != null ? upgrades.Config : null;
            // The screen rebuilds its tree when the UXML reimports; the page is rebuilt with it.
            if (root != builtRoot || tree != builtTree || nodes.Count == 0) Build(root, tree);
            Refresh();
        }

        private void Build(VisualElement root, UpgradeTreeConfig tree)
        {
            nodes.Clear();
            selected = null;
            builtRoot = root;
            builtTree = tree;
            if (root == null) return;
            root.Clear();
            // Registered once per element tree: the screen recreates the root when the UXML reimports.
            root.RegisterCallback<NavigationMoveEvent>(OnNavigate);

            var head = Element("upgrades__head", root);
            head.Add(Text("upgrades__head-label", "UPGRADE POINTS"));
            points = Text("upgrades__points", "0");
            head.Add(points);

            if (tree == null || tree.nodes == null || tree.nodes.Count == 0)
            {
                root.Add(Text("upgrades__empty", "NO UPGRADES IN THIS BUILD"));
                return;
            }

            var columns = Element("upgrades__tree", root);
            foreach (UpgradeBranch branch in Enum.GetValues(typeof(UpgradeBranch)))
            {
                tree.GetBranch(branch, scratch);
                if (scratch.Count == 0) continue;
                var column = Element("upgrades__column", columns);
                column.Add(Text("upgrades__column-title", UpgradeDetailsView.BranchName(branch)));
                foreach (UpgradeNode node in scratch) column.Add(MakeNode(node).Root);
            }

            detail = Element("upgrades__detail", root);
            detailKicker = Text("upgrades__detail-kicker", "");
            detailTitle = Text("upgrades__detail-title", "");
            detailDescription = Text("upgrades__detail-description", "");
            detailEffects = Text("upgrades__detail-effects", "");
            detailStatus = Text("upgrades__detail-status", "");
            detail.Add(detailKicker);
            detail.Add(detailTitle);
            detail.Add(detailDescription);
            detail.Add(detailEffects);
            detail.Add(detailStatus);

            buy = Element("upgrade-buy", detail);
            buy.focusable = true;
            buyLabel = Text("upgrade-buy__label", "BUY");
            buy.Add(buyLabel);
            buy.AddManipulator(new Clickable(() => TryBuy(selected)));
            buy.RegisterCallback<NavigationSubmitEvent>(evt => { TryBuy(selected); evt.StopPropagation(); });
        }

        private NodeView MakeNode(UpgradeNode node)
        {
            var view = new NodeView { Node = node, Root = new VisualElement { focusable = true } };
            view.Root.AddToClassList(NodeClass);
            view.Root.Add(Text("upgrade-node__title", node.Title.ToUpperInvariant()));
            view.Meta = Text("upgrade-node__meta", "");
            view.Root.Add(view.Meta);

            view.Root.RegisterCallback<PointerEnterEvent>(_ => Select(view));
            view.Root.RegisterCallback<FocusInEvent>(_ => Select(view));
            view.Root.AddManipulator(new Clickable(() => { Select(view); TryBuy(view); }));
            view.Root.RegisterCallback<NavigationSubmitEvent>(evt => { Select(view); TryBuy(view); evt.StopPropagation(); });
            nodes.Add(view);
            return view;
        }

        /// <summary>Re-reads every node's state and the points. Runs on PlayerUpgrades.Changed while shown.</summary>
        public void Refresh()
        {
            if (!shown || builtRoot == null) return;
            int available = upgrades != null ? upgrades.AvailablePoints : 0;
            if (points != null)
            {
                points.text = available.ToString();
                points.EnableInClassList("upgrades__points--zero", available == 0);
            }

            foreach (NodeView view in nodes)
            {
                view.State = ResolveState(view.Node);
                for (int i = 0; i < StateClasses.Length; i++)
                    view.Root.EnableInClassList(StateClasses[i], i == (int)view.State);
                view.Meta.text = view.State switch
                {
                    UpgradeNodeState.Owned => "OWNED",
                    UpgradeNodeState.Unavailable => "UNAVAILABLE",
                    UpgradeNodeState.Locked => $"TIER {view.Node.tier}  ·  LOCKED",
                    _ => $"TIER {view.Node.tier}  ·  {view.Node.cost} PT",
                };
            }

            if (selected == null && nodes.Count > 0)
                selected = nodes.Find(n => n.State == UpgradeNodeState.Available) ?? nodes[0];
            ShowDetails();
        }

        private void Select(NodeView view)
        {
            if (view == null || view == selected) return;
            selected = view;
            ShowDetails();
        }

        private void ShowDetails()
        {
            foreach (NodeView view in nodes) view.Root.EnableInClassList(SelectedClass, view == selected);
            if (detail == null) return;
            detail.EnableInClassList("hidden", selected == null);
            if (selected == null) return;

            UpgradeNode node = selected.Node;
            int available = upgrades != null ? upgrades.AvailablePoints : 0;
            UpgradeLockReason reason = upgrades != null ? upgrades.GetLockReason(node) : UpgradeLockReason.Unknown;
            detailKicker.text = $"{UpgradeDetailsView.BranchName(node.branch)}  ·  TIER {node.tier}";
            detailTitle.text = node.Title.ToUpperInvariant();
            detailDescription.text = node.description ?? "";
            detailEffects.text = UpgradeDetailsView.DescribeEffects(node);
            detailStatus.text = UpgradeDetailsView.StatusText(node, selected.State, reason, available);

            bool canBuy = selected.State == UpgradeNodeState.Available;
            buy.EnableInClassList("upgrade-buy--disabled", !canBuy);
            buyLabel.text = selected.State == UpgradeNodeState.Owned ? "OWNED"
                : selected.State == UpgradeNodeState.Unavailable ? "UNAVAILABLE"
                : $"BUY  ·  {node.cost} POINT{(node.cost == 1 ? "" : "S")}";
        }

        // D-pad / stick / arrow keys: move to the nearest card in that direction; Down from the bottom
        // of a column goes to BUY, Up from BUY returns to the selected card.
        private void OnNavigate(NavigationMoveEvent evt)
        {
            var from = evt.target as VisualElement;
            VisualElement next = null;
            if (from == buy)
            {
                if (evt.direction == NavigationMoveEvent.Direction.Up && selected != null) next = selected.Root;
            }
            else
            {
                NodeView view = nodes.Find(n => n.Root == from);
                if (view == null) return;
                next = Nearest(view, evt.direction);
                if (next == null && evt.direction == NavigationMoveEvent.Direction.Down) next = buy;
            }

            evt.StopImmediatePropagation();
            from?.focusController?.IgnoreEvent(evt);
            next?.Focus();
        }

        private VisualElement Nearest(NodeView from, NavigationMoveEvent.Direction direction)
        {
            Vector2 dir = direction switch
            {
                NavigationMoveEvent.Direction.Left => Vector2.left,
                NavigationMoveEvent.Direction.Right => Vector2.right,
                NavigationMoveEvent.Direction.Up => Vector2.down,   // panel y grows downward
                NavigationMoveEvent.Direction.Down => Vector2.up,
                _ => Vector2.zero,
            };
            if (dir == Vector2.zero) return null;

            Vector2 origin = from.Root.worldBound.center;
            VisualElement best = null;
            float bestScore = float.MaxValue;
            foreach (NodeView candidate in nodes)
            {
                if (candidate == from) continue;
                Vector2 delta = candidate.Root.worldBound.center - origin;
                float along = Vector2.Dot(delta, dir);
                if (along <= 2f) continue;
                float across = Mathf.Abs(dir.x != 0f ? delta.y : delta.x);
                // Up/Down stay in the branch column; past its end, Down goes to BUY.
                if (dir.x == 0f && across > from.Root.worldBound.width * 0.5f) continue;
                float score = along + across * 2.2f;
                if (score < bestScore) { bestScore = score; best = candidate.Root; }
            }
            return best;
        }

        private void TryBuy(NodeView view)
        {
            if (view == null || upgrades == null || view.State != UpgradeNodeState.Available) return;
            upgrades.TryPurchase(view.Node); // Changed redraws every node and the details.
        }

        private UpgradeNodeState ResolveState(UpgradeNode node)
        {
            if (upgrades == null) return UpgradeNodeState.Locked;
            return upgrades.GetLockReason(node) switch
            {
                UpgradeLockReason.None => UpgradeNodeState.Available,
                UpgradeLockReason.Owned => UpgradeNodeState.Owned,
                UpgradeLockReason.NoPoints => UpgradeNodeState.Unaffordable,
                UpgradeLockReason.NotInBuild => UpgradeNodeState.Unavailable,
                _ => UpgradeNodeState.Locked,
            };
        }

        private static VisualElement Element(string className, VisualElement parent)
        {
            var element = new VisualElement();
            element.AddToClassList(className);
            parent.Add(element);
            return element;
        }

        private static Label Text(string className, string text)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(className);
            return label;
        }
    }
}
