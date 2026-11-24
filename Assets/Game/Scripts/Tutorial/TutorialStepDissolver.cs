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
            // A destroyed or inactive prop would throw and abort the rest of the sequence.
            if (dissolve == null || !dissolve.gameObject.activeInHierarchy) continue;
            dissolve.Dissolve();
            yield return new WaitForSeconds(interval);
        }
        
    }

    public IEnumerator UndissolveAll(float interval)
    {
        foreach (var dissolve in dissolves)
        {
            // A destroyed or inactive prop would throw and abort the rest of the sequence.
            if (dissolve == null || !dissolve.gameObject.activeInHierarchy) continue;
            dissolve.Undissolve();
            yield return new WaitForSeconds(interval);
        }

    }

}
