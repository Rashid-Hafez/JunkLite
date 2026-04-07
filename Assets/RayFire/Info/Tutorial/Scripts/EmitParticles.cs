using UnityEngine;

public class EmitParticles : MonoBehaviour
{
    ParticleSystem ps;
    
    // Start is called before the first frame update
    void Start()
    {
        ps = gameObject.GetComponent<ParticleSystem>();
        ps.Emit (30);
    }
    
}
