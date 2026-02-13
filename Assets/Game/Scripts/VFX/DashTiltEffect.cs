using UnityEngine;

public class DashTiltEffect : MonoBehaviour
{
    [Header("Tilt Settings")]
    public float tiltAngle = 90f;
    public float tiltInSpeed = 20f;     // speed when dashing
    public float tiltOutSpeed = 12f;    // speed when returning to normal

    private Quaternion normalRotation;
    private Quaternion targetRotation;

    private Transform tiltTarget;

    private junklite.PlayerCharacter player;
    private junklite.Character2D5Controller controller;

    private bool subscribed = false;
    private bool isTilting = false;

    void Awake()
    {
        // Rotate the parent object
        tiltTarget = transform.parent;
        normalRotation = tiltTarget.localRotation;

        player = GetComponentInParent<junklite.PlayerCharacter>();
        if (player == null)
            Debug.LogWarning("DashTiltEffect: No PlayerCharacter found in parents!");
    }

    void Update()
    {
        TrySubscribe();

        // Gradual tilt
        tiltTarget.localRotation = Quaternion.Lerp(
            tiltTarget.localRotation,
            targetRotation,
            Time.deltaTime * (isTilting ? tiltInSpeed : tiltOutSpeed)
        );
    }

    private void TrySubscribe()
    {
        if (subscribed || player == null) return;

        controller = player.Controller;
        if (controller == null) return; // Wait until PlayerCharacter sets this

        controller.OnDashStarted += StartTilt;
        controller.OnDashEnded += StopTilt;

        subscribed = true;
    }

    void OnDisable()
    {
        if (subscribed && controller != null)
        {
            controller.OnDashStarted -= StartTilt;
            controller.OnDashEnded -= StopTilt;
        }
        subscribed = false;
    }

    // GRADUAL TILT INTO DASH
    void StartTilt()
    {
        if (tiltTarget == null) return;

        isTilting = true;

        float direction = controller.IsFacingRight ? 1f : -1f;

        targetRotation = Quaternion.Euler(0f, 0f, tiltAngle * -direction);
    }

    // GRADUAL RETURN TO NORMAL
    void StopTilt()
    {
        if (tiltTarget == null) return;

        isTilting = false;

        targetRotation = normalRotation;
    }
}
