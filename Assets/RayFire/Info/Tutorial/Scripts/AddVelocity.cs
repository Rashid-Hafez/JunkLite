using UnityEngine;

public class AddVelocity : MonoBehaviour
{
    
    public float velocity = 5f;
    
    
    // Start is called before the first frame update
    void Start()
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        rb.linearVelocity = new Vector3 (0, 0, 1f * velocity);

    }
    
}
