using junklite;
using UnityEngine;

public class LedgeDetection : MonoBehaviour
{
    [SerializeField] private float radius = 0.5f;
    [SerializeField] private LayerMask WhatIsGround;
    private Character2D5Controller playerController;
    private BoxCollider playerBox;               // must be a trigger box on the same object

    // true while the player box is intersecting ground *below* the center
    [SerializeField] private bool insideGround;
    // tracks any overlap with the box collider (wall or floor)
    private bool triggerActive;

    void Start()
    {
        playerController = GetComponentInParent<Character2D5Controller>();
        playerBox = GetComponent<BoxCollider>();
    }

    void FixedUpdate()
    {
        if (playerController == null) return;

        bool hit = Physics.CheckSphere(transform.position, radius, WhatIsGround);

        if (triggerActive)
        {
            hit = false;
        }
        else if (insideGround)
        {
            hit = false;
        }

        playerController.LedgeDetected = hit;
    }

    private void OnDrawGizmos()
    {
        Gizmos.DrawWireSphere(transform.position, radius);
        if (playerBox != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(playerBox.bounds.center, playerBox.bounds.size);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (((1 << other.gameObject.layer) & WhatIsGround) == 0) return;

        triggerActive = true;
        Vector3 closest = other.ClosestPoint(playerBox.bounds.center);
        bool below = closest.y < playerBox.bounds.center.y - 0.01f;

        if (below)
            insideGround = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (((1 << other.gameObject.layer) & WhatIsGround) == 0) return;

        triggerActive = false;
        insideGround = false;
    }
}
