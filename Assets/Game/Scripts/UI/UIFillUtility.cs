using UnityEngine;
using UnityEngine.UI;

namespace junklite
{
    public static class UIFillUtility
    {
        /// <summary>Updates a left-to-right bar, including solid images with no sprite.</summary>
        public static void SetHorizontalFill(Image image, float amount)
        {
            if (image == null) return;

            amount = Mathf.Clamp01(amount);
            if (!Mathf.Approximately(image.fillAmount, amount))
                image.fillAmount = amount;

            // uGUI ignores fillAmount when there is no active sprite. Runtime-built
            // solid bars need their rectangle resized, as with StatBarUI.
            if (image.overrideSprite != null && image.type == Image.Type.Filled)
                return;

            RectTransform rect = image.rectTransform;
            Vector2 anchorMin = rect.anchorMin;
            Vector2 anchorMax = rect.anchorMax;
            anchorMin.x = 0f;
            anchorMax.x = amount;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;

            Vector2 offsetMin = rect.offsetMin;
            Vector2 offsetMax = rect.offsetMax;
            offsetMin.x = 0f;
            offsetMax.x = 0f;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }
    }
}
