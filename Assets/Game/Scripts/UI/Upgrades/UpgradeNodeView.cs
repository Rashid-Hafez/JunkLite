using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace junklite
{
    /// <summary>Unavailable = not buyable in this build (above the tree's Max Available Tier); it uses the locked look.</summary>
    public enum UpgradeNodeState { Locked, Unaffordable, Available, Owned, Unavailable }

    /// <summary>
    /// One authored node card in the Upgrades page. Binds text and toggles the authored state objects;
    /// every colour, size and font lives on the prefab. Selecting it (click, hover or gamepad focus)
    /// asks the page to show its details.
    /// </summary>
    public sealed class UpgradeNodeView : MonoBehaviour, ISelectHandler, IPointerEnterHandler
    {
        [SerializeField] private TMP_Text title;
        [SerializeField] private TMP_Text cost;
        [SerializeField] private Image icon;
        [SerializeField] private Button button;
        [Header("State objects (authored children, toggled by state)")]
        [SerializeField] private GameObject lockedState;
        [SerializeField] private GameObject unaffordableState;
        [SerializeField] private GameObject availableState;
        [SerializeField] private GameObject ownedState;
        [Tooltip("Line drawn up to the previous tier. Hidden on the first node of a column.")]
        [SerializeField] private GameObject link;
        [Tooltip("Shown while this node is the one in the details card.")]
        [SerializeField] private GameObject selection;

        public UpgradeNode Node { get; private set; }
        public UpgradeNodeState State { get; private set; }
        public Button Button => button;

        /// <summary>Raised when the player focuses or hovers the node (show details).</summary>
        public event Action<UpgradeNodeView> Focused;
        /// <summary>Raised when the player confirms the node (click / submit): buy it or show why not.</summary>
        public event Action<UpgradeNodeView> Confirmed;

        private void Awake()
        {
            if (button == null) button = GetComponent<Button>();
            if (button != null) button.onClick.AddListener(() => Confirmed?.Invoke(this));
        }

        public void Bind(UpgradeNode node, UpgradeNodeState state, bool showLink)
        {
            Node = node;
            State = state;
            if (title != null) title.text = node != null ? node.Title.ToUpperInvariant() : "";
            if (cost != null) cost.text = node == null ? "" : state == UpgradeNodeState.Owned ? "OWNED"
                : state == UpgradeNodeState.Unavailable ? "UNAVAILABLE"
                : $"{node.cost} PT{(node.cost == 1 ? "" : "S")}";
            bool hasIcon = false;
            if (icon != null)
            {
                icon.sprite = node != null ? node.icon : null;
                hasIcon = icon.sprite != null;
                icon.enabled = hasIcon;
            }
            // The card shows the icon; the name lives in the details card. Without an icon the name stands in.
            if (title != null) title.gameObject.SetActive(!hasIcon);
            if (lockedState != null) lockedState.SetActive(state == UpgradeNodeState.Locked || state == UpgradeNodeState.Unavailable);
            if (unaffordableState != null) unaffordableState.SetActive(state == UpgradeNodeState.Unaffordable);
            if (availableState != null) availableState.SetActive(state == UpgradeNodeState.Available);
            if (ownedState != null) ownedState.SetActive(state == UpgradeNodeState.Owned);
            if (link != null) link.SetActive(showLink);
            SetSelected(false);
        }

        public void SetSelected(bool selected)
        {
            if (selection != null) selection.SetActive(selected);
        }

        // Submit and click both arrive through the Button (it implements ISubmitHandler), so Confirmed fires once.
        public void OnSelect(BaseEventData eventData) => Focused?.Invoke(this);
        public void OnPointerEnter(PointerEventData eventData) => Focused?.Invoke(this);
    }
}
