using UnityEngine;

namespace junklite
{
    /// <summary>
    /// Marks a Wall-layer collider as one the player can wall slide on and wall jump off
    /// (Character2D5Controller.requireWallJumpSurface). Goes on the collider's own object. No logic.
    /// </summary>
    [DisallowMultipleComponent]
    public class WallJumpSurface : MonoBehaviour
    {
    }
}
