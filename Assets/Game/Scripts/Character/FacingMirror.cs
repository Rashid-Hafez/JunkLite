using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Mirrors a child of a SkeletonSwap player, whose root never flips.
    /// Author the child for facing right. Facing left negates its local X and,
    /// when Turn On Y is set, turns it 180° so child offsets and particles mirror too.
    /// Do not use on a ScaleFlip player (it would mirror twice) or on BoneFollowers.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FacingMirror : MonoBehaviour
    {
        [SerializeField] private Character2D5Controller controller;
        [Tooltip("Negate local position X when facing left.")]
        [SerializeField] private bool mirrorPosition = true;
        [Tooltip("Turn 180° on Y when facing left. Use on VFX groups so their children and particles mirror.")]
        [SerializeField] private bool turnOnY;

        private Vector3 rightPosition;
        private Quaternion rightRotation;

        private void Awake()
        {
            if (controller == null)
                controller = GetComponentInParent<Character2D5Controller>();

            rightPosition = transform.localPosition;
            rightRotation = transform.localRotation;
        }

        private void OnEnable()
        {
            if (controller == null) return;
            controller.FacingChanged += Apply;
            Apply(controller.IsFacingRight);
        }

        private void OnDisable()
        {
            if (controller != null)
                controller.FacingChanged -= Apply;
        }

        private void Apply(bool facingRight)
        {
            if (mirrorPosition)
            {
                Vector3 position = rightPosition;
                if (!facingRight) position.x = -position.x;
                transform.localPosition = position;
            }

            if (turnOnY)
            {
                transform.localRotation = facingRight
                    ? rightRotation
                    : Quaternion.Euler(0f, 180f, 0f) * rightRotation;
            }
        }
    }
}
