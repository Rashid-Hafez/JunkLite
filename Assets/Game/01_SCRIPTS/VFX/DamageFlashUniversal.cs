using UnityEngine;
using System.Collections;
namespace junklite
{
    public class DamageFlashUniversal : MonoBehaviour
{
    [SerializeField] private float flashDuration = 0.1f;
    [SerializeField] private float flashAmount = 0.8f;
    [SerializeField] private Color flashColor = Color.white;
    private SpriteRenderer[] _spriteRendererArray;
    private Material[] _materialArray;
    private Coroutine _flashCoroutine;
    private float flashamounttemp;

    // methods

    private void Awake()
    {
        _spriteRendererArray = GetComponentsInChildren<SpriteRenderer>();
        InitializeMaterials();
        flashamounttemp = flashAmount;
    }

    private void InitializeMaterials()
    {
        _materialArray = new Material[_spriteRendererArray.Length];
        for (int i = 0; i < _spriteRendererArray.Length; i++)
        {
            _materialArray[i] = _spriteRendererArray[i].material;
        }
    }

    public void Flash()
    {
        _flashCoroutine = StartCoroutine(FlashCoroutine());
    }

    private IEnumerator FlashCoroutine()
    {
        foreach (var material in _materialArray)
        {
            material.SetFloat("_FlashAmount", flashamounttemp);
        }
        yield return new WaitForSeconds(flashDuration);
        ResetFlash();
    }

    private void ResetFlash()
    {
        flashAmount = 1f;
        foreach (var material in _materialArray)
        {
            material.SetFloat("_FlashAmount", flashAmount);
        }
    }
}
}