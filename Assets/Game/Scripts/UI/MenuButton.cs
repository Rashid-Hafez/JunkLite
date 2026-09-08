using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System;

namespace junklite
{
    [RequireComponent(typeof(RectTransform))]
    public class MenuButton : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler,
        IPointerClickHandler
    {
        #region Fields

        [Header("References")]
        [SerializeField] private TMP_Text label;
        [SerializeField] private Image background;

        [Header("Idle")]
        [SerializeField] private Color idleBgColor = Color.clear;
        [SerializeField] private Color idleTextColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        [SerializeField] private float idleFontSize = 14f;

        [Header("Selected")]
        [SerializeField] private Color selectedBgColor = new Color(0.18f, 1f, 0.28f, 1f);
        [SerializeField] private Color selectedTextColor = new Color(0.05f, 0.05f, 0.05f, 1f);
        [SerializeField] private float selectedFontSize = 17f;

        [Header("Hover / Press")]
        [SerializeField] private Color hoverBgColor = new Color(1f, 1f, 1f, 0.08f);
        [SerializeField] private Color pressBgColor = new Color(1f, 1f, 1f, 0.15f);

        [Header("Interactable")]
        [SerializeField] private bool interactable = true;

        public event Action OnClick;

        private bool isSelected;
        private bool isHovered;
        private bool isPressed;

        #endregion

        #region Unity

        private void OnEnable() => ApplyState();
        private void OnValidate() => ApplyState();

        #endregion

        #region Selection

        public bool IsSelected => isSelected;

        /// <summary>
        /// Configures a button created at runtime. The pause menu uses this to keep
        /// its generated navigation controls on the same interaction path as the
        /// rest of the game's controller-aware menu buttons.
        /// </summary>
        public void Configure(
            TMP_Text buttonLabel,
            Image buttonBackground,
            Color idleBackground,
            Color idleForeground,
            Color selectedBackground,
            Color selectedForeground,
            Color hoverBackground,
            Color pressedBackground,
            float fontSize,
            float focusedFontSize = -1f)
        {
            label = buttonLabel;
            background = buttonBackground;
            idleBgColor = idleBackground;
            idleTextColor = idleForeground;
            selectedBgColor = selectedBackground;
            selectedTextColor = selectedForeground;
            hoverBgColor = hoverBackground;
            pressBgColor = pressedBackground;
            idleFontSize = fontSize;
            selectedFontSize = focusedFontSize > 0f ? focusedFontSize : fontSize;
            ApplyState();
        }

        public void SetText(string text)
        {
            if (label != null)
                label.text = text;
        }

        public void SetSelected(bool selected)
        {
            isSelected = selected;
            isHovered = false;
            isPressed = false;
            ApplyState();
        }

        #endregion

        #region State

        private void ApplyState()
        {
            if (isSelected)
            {
                SetBackground(selectedBgColor);
                SetLabel(selectedTextColor, selectedFontSize, FontStyles.Bold);
                return;
            }

            if (!interactable)
            {
                SetBackground(idleBgColor);
                SetLabel(new Color(idleTextColor.r, idleTextColor.g, idleTextColor.b, 0.35f), idleFontSize, FontStyles.Normal);
                return;
            }

            if (isPressed)
            {
                SetBackground(pressBgColor);
                SetLabel(idleTextColor, idleFontSize, FontStyles.Normal);
            }
            else if (isHovered)
            {
                SetBackground(hoverBgColor);
                SetLabel(idleTextColor, idleFontSize, FontStyles.Normal);
            }
            else
            {
                SetBackground(idleBgColor);
                SetLabel(idleTextColor, idleFontSize, FontStyles.Normal);
            }
        }

        private void SetBackground(Color color)
        {
            if (background != null)
                background.color = color;
        }

        private void SetLabel(Color color, float size, FontStyles style)
        {
            if (label == null) return;
            label.color = color;
            label.fontStyle = style;
            label.enableAutoSizing = false;
            label.fontSize = size;
        }

        #endregion

        #region Interaction

        public bool Interactable
        {
            get => interactable;
            set { interactable = value; ApplyState(); }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!interactable || isSelected) return;
            isHovered = true;
            ApplyState();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            isHovered = false;
            isPressed = false;
            ApplyState();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!interactable || isSelected) return;
            isPressed = true;
            ApplyState();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            isPressed = false;
            ApplyState();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!interactable) return;
            OnClick?.Invoke();
        }

        #endregion

        public void Click()
        {
            if (!interactable) return;
            OnClick?.Invoke();
        }
    }
}
