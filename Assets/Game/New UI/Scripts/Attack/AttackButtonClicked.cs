using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace junklite
{
    public class AttackButtonClicked : MonoBehaviour
    {
        private enum AttackType
        {
            Weapon1,
            Weapon2
        }

        [SerializeField] private AttackType attackType;
        [SerializeField] private Image buttonImage;
        [SerializeField] private Sprite defaultSprite;
        [SerializeField] private Sprite attackSprite;
        [SerializeField] private float swapDuration = 0.15f;

        private Coroutine resetRoutine;

        private void OnEnable()
        {
            if (buttonImage != null)
                buttonImage.sprite = defaultSprite;

            if (GameInputManager.Instance == null) return;

            if (attackType == AttackType.Weapon1)
                GameInputManager.Instance.OnWeapon1Attack += HandleAttack;
            else
                GameInputManager.Instance.OnWeapon2Attack += HandleAttack;
        }

        private void OnDisable()
        {
            if (resetRoutine != null)
            {
                StopCoroutine(resetRoutine);
                resetRoutine = null;
            }

            if (buttonImage != null)
                buttonImage.sprite = defaultSprite;

            if (GameInputManager.Instance == null) return;

            if (attackType == AttackType.Weapon1)
                GameInputManager.Instance.OnWeapon1Attack -= HandleAttack;
            else
                GameInputManager.Instance.OnWeapon2Attack -= HandleAttack;
        }

        private void HandleAttack()
        {
            if (buttonImage == null) return;

            buttonImage.sprite = attackSprite;

            if (resetRoutine != null) StopCoroutine(resetRoutine);
            resetRoutine = StartCoroutine(ResetAfterDelay());
        }

        private IEnumerator ResetAfterDelay()
        {
            yield return new WaitForSeconds(swapDuration);
            buttonImage.sprite = defaultSprite;
            resetRoutine = null;
        }
    }
}
