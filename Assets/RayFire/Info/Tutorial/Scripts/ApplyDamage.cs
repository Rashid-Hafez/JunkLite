using RayFire;
using UnityEngine;

public class ApplyDamage : MonoBehaviour
{

    public RayfireRigid rigid;
    public float        damage = 60f;
    public float        radius = 1f;
    public string       tip    = "Push SPACE to Applly damage";

    void Update()
    {
        if (Input.GetKeyDown (KeyCode.Space))
        {
            if (rigid == null)
                return;
        
            if (rigid.objTp == ObjectType.Mesh)
                rigid.ApplyDamage (damage, rigid.transform.position);
            else if (rigid.objTp == ObjectType.ConnectedCluster)
            {
                rigid.ApplyDamage (damage, rigid.transform.position, radius);
            }
        }
    }
}
