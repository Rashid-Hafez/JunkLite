using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class DissolveController : MonoBehaviour
{
    [SerializeField] private Renderer rend;
    [SerializeField] private string propertyName = "_CutoffHeight";
    [SerializeField] private float duration = 1f;

    private Material materialInstance;
    private float currentValue;
    private Coroutine routine;

    void Awake()
    {
        materialInstance = rend.material; // instance, not shared
        currentValue = materialInstance.GetFloat(propertyName);
    }

    public void Update()
    {
        if (Keyboard.current[Key.P].wasPressedThisFrame)
        {
            Dissolve();
        }

        if (Keyboard.current[Key.O].wasPressedThisFrame)
        {
            Undissolve();
        }
    }

    public void AnimateDissolve(float target, float duration)
    {
        Debug.Log("Animating dissolve!");
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }
        

        routine = StartCoroutine(LerpDissolve(target, duration));
        
    }

    private IEnumerator LerpDissolve(float target, float duration)
    {
        float start = currentValue;
        float time = 0f;

        while (time < duration)
        {
            time += Time.deltaTime;
            float t = time / duration;

            currentValue = Mathf.Lerp(start, target, t);
            materialInstance.SetFloat(propertyName, currentValue);

            yield return null;
        }

        currentValue = target;
        materialInstance.SetFloat(propertyName, currentValue);
    }

    // Convenience wrappers
    public void Dissolve() => AnimateDissolve(2f, duration);
    public void Undissolve() => AnimateDissolve(-1f, duration);
}