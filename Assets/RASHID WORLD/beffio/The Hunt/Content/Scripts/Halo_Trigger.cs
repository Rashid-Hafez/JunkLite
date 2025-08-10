using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Halo_Trigger : MonoBehaviour {
    Animator anim;
    public Animation animat;
    // Use this for initialization
    void Start () {
        anim = gameObject.GetComponent<Animator>();
        animat = gameObject.GetComponent<Animation>();
    }
	
	// Update is called once per frame
	void Update () {
        
        if (Input.GetMouseButtonDown(0))
        {
            //print("test");
            try
            {
                foreach (Renderer r in gameObject.GetComponentsInChildren<Renderer>())
                {
                    r.enabled = true;
                }
            }
            catch
            { }
            anim.SetTrigger("Active");
            
        }
        if (Input.GetMouseButtonDown(1))
        {
            anim.SetTrigger("Active_Inversed");

            StartCoroutine(Example());
            //print("test2");
        }
        
    }

    IEnumerator Example()
    {

        
        yield return new WaitForSeconds(0.5F);
        try
        {
            foreach (Renderer r in gameObject.GetComponentsInChildren<Renderer>())
            {
                r.enabled = false;
            }
        }
        catch { }
        
        yield break;

    

    }

    void Waiter()
    {
        gameObject.SetActive(false);
    }


}
