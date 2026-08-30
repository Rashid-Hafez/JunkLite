using System.Collections.Generic;
using System.Collections;
using UnityEngine;

public class TutorialStepDissolver : MonoBehaviour
{
    public List<DissolveController> dissolves = new();
    public float dissolveInterval = 0.5f;
    public void DissolveAll()
    {
        StartCoroutine(DissolveAll(dissolveInterval));
    }

    public void UndissolveAll()
    {
        StartCoroutine(UndissolveAll(dissolveInterval));
    }

    public IEnumerator DissolveAll(float interval)
    {
        foreach (var dissolve in dissolves)
        {
            dissolve.Dissolve();
            yield return new WaitForSeconds(interval);
        }
        
    }

    public IEnumerator UndissolveAll(float interval)
    {
        foreach (var dissolve in dissolves)
        {
            dissolve.Undissolve();
            yield return new WaitForSeconds(interval);
        }

    }

}
