using UnityEngine;
using System.Collections;
public class DamageFlashUniversal : MonoBehaviour
{
    [SerializeField] private float flashDuration = 0.1f;
    [SerializeField] private float flashIntensity = 1.0f;
    [SerializeField] private Color flashColor = Color.white;
    private float flashAmount = 0.0f;
    private SpriteRenderer[] _spriteRendererArray;
    private Material[] _materialArray;
    private Coroutine _flashCoroutine;

    // methods

    private void Awake()
    {
        _spriteRendererArray = GetComponentsInChildren<SpriteRenderer>();
        InitializeMaterials();
    }

    private void InitializeMaterials()
    {
        _materialArray = new Material[_spriteRendererArray.Length];
        for (int i = 0; i < _spriteRendererArray.Length; i++)
        {
            _materialArray[i] = _spriteRendererArray[i].material;
        }
    }

    public void DamageFlashUniversal()
    {
        _flashCoroutine = StartCoroutine(FlashCoroutine());
    }

    private IEnumerator FlashCoroutine()
    {
        flashAmount = 0.5f;
        foreach (var material in _materialArray)
        {
            material.SetFloat("_FlashAmount", flashAmount);
        }
        yield return new WaitForSeconds(flashDuration);
        ResetFlash();
    }

    private void ResetFlash()
    {
        flashAmount = 1f;
        damageFlashMaterial.SetFloat("_FlashAmount", flashAmount);
    }
}