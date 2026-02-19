using junklite;
using UnityEngine;

public class LedgeDetection : MonoBehaviour
{
   [SerializeField] private float radius = 0.5f;
   [SerializeField] private LayerMask WhatIsGround;
   private Character2D5Controller playerController;
   private BoxCollider playerBox;               // must be a trigger box on the same object


   // true while the player box is intersecting ground
  [SerializeField] private bool insideGround;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        playerController = GetComponentInParent<Character2D5Controller>();
        playerBox = GetComponent<BoxCollider>();
    }

    // FixedUpdate is used since detection is physics‑based
    void FixedUpdate()
    {
        if (playerController == null) return;

        // basic sphere check for a potential ledge
        bool hit = Physics.CheckSphere(transform.position, radius, WhatIsGround);

        // if our trigger box is currently touching ground, treat it as not a ledge
        if (insideGround)
            hit = false;

        playerController.LedgeDetected = hit;
        Debug.Log($"Ledge Detected: {hit}");
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
        if (((1 << other.gameObject.layer) & WhatIsGround) != 0)
            insideGround = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (((1 << other.gameObject.layer) & WhatIsGround) != 0)
            insideGround = false;
    }
}
