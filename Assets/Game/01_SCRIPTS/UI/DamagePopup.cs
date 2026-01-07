using UnityEngine;
using TMPro;

namespace junklite
{
    public class DamagePopup : MonoBehaviour
    {
        private TextMeshPro textMesh;
        private Color textColor;
        private float elapsed;
        private float moveSpeed;
        private float lifetime;

        private void Awake()
        {
            textMesh = GetComponent<TextMeshPro>();
        }

        public void Setup(float damageAmount)
        {
            var manager = DamagePopupManager.Instance;

            // Get settings from manager
            moveSpeed = manager != null ? manager.MoveSpeed : 1f;
            lifetime = manager != null ? manager.Lifetime : 1f;

            // Set text
            textMesh.text = Mathf.RoundToInt(damageAmount).ToString();

            // Set color based on damage amount
            if (manager != null)
            {
                if (damageAmount >= manager.HighDamageThreshold)
                    textColor = manager.HighDamageColor;
                else if (damageAmount >= manager.MediumDamageThreshold)
                    textColor = manager.MediumDamageColor;
                else
                    textColor = manager.LowDamageColor;
            }
            else
            {
                textColor = Color.white;
            }

            textMesh.color = textColor;
            elapsed = 0f;
        }

        private void Update()
        {
            // Move up
            transform.position += Vector3.up * moveSpeed * Time.deltaTime;

            // Fade out
            elapsed += Time.deltaTime;
            float t = elapsed / lifetime;
            textColor.a = Mathf.Lerp(1f, 0f, t);
            textMesh.color = textColor;

            // Return to pool when fully faded
            if (t >= 1f)
                DamagePopupManager.Instance?.ReturnPopup(this);
        }
    }
}