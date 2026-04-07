using RayFire;
using UnityEngine;

public class Rigid_Initialization : MonoBehaviour
{
    public bool       existingRigid;
    public bool       newRigid;
    public GameObject prefab;
    
    // Start is called before the first frame update
    void Start()
    {
        if (existingRigid == true)
        {
            RayfireRigid rigid = GetComponent<RayfireRigid>();
            if (rigid != null)
            {
                rigid.dmlTp = DemolitionType.Runtime;
                rigid.Initialize();
            }
        }

        if (newRigid == true)
        {
            RayfireRigid rigid = gameObject.AddComponent<RayfireRigid>();
            rigid.dmlTp = DemolitionType.Runtime;
            rigid.Initialize();
        }

        if (prefab != null)
        {
            GameObject   instance = Instantiate(prefab);
            instance.transform.position = new Vector3 (0, 5, 0);
            RayfireRigid rigid    = instance.AddComponent<RayfireRigid>();
            rigid.dmlTp = DemolitionType.Runtime;
            rigid.Initialize();
        }
    }
}
