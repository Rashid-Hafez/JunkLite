using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class DissolveController : MonoBehaviour
{
    [SerializeField] private Renderer rend;
    [SerializeField] private string propertyName = "_CutoffHeight";
    [SerializeField] private float duration = 1f;

    private MaterialPropertyBlock propertyBlock;
    private int propertyId;
    private float currentValue;
    private Coroutine routine;

    void Awake()
    {
        propertyBlock = new MaterialPropertyBlock();
        propertyId = Shader.PropertyToID(propertyName);
        Material sharedMaterial = rend != null ? rend.sharedMaterial : null;
        currentValue = sharedMaterial != null && sharedMaterial.HasProperty(propertyId)
            ? sharedMaterial.GetFloat(propertyId)
            : 0f;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
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
#endif

    public void AnimateDissolve(float target, float duration)
    {
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
            SetDissolveValue(currentValue);

            yield return null;
        }

        currentValue = target;
        SetDissolveValue(currentValue);
        routine = null;
    }

    private void SetDissolveValue(float value)
    {
        if (rend == null) return;
        propertyBlock.Clear();
        rend.GetPropertyBlock(propertyBlock);
        propertyBlock.SetFloat(propertyId, value);
        rend.SetPropertyBlock(propertyBlock);
    }

    // Convenience wrappers
    public void Dissolve() => AnimateDissolve(2f, duration);
    public void Undissolve() => AnimateDissolve(-1f, duration);
}
