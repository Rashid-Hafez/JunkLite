using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Compass : MonoBehaviour {

    public GameObject camera;

	// Use this for initialization
	void Start () {


       // float x = 90;
        


        /*
        Vector3 newRotation = new Vector3(gameObject.transform.eulerAngles.x, gameObject.transform.eulerAngles.y+90, gameObject.transform.eulerAngles.z);
        gameObject.transform.eulerAngles = newRotation;
        */

    }
	
	// Update is called once per frame
	void Update () {
        if (camera!=null) gameObject.transform.localRotation = Quaternion.Euler(0, -camera.transform.eulerAngles.y, 0);

    }
}
