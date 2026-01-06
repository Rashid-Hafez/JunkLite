using UnityEngine;

public class DamageFlashUniversal : MonoBehaviour
{
    [SerializeField] private Material damageFlashMaterial;
    [SerializeField] private float flashDuration = 0.1f;
    [SerializeField] private float flashIntensity = 1.0f;
    [SerializeField] private Color flashColor = Color.white;
    private float flashAmount = 0.0f;

    private SpriteRenderer[] _spriteRendererArray;
    private Material[] _materialArray;


    private void Awake()
    {
        spriteRendererArray = GetComponentsInChildren<SpriteRenderer>();
        InitializeMaterials();
    }

    private void Start()
    {
        _
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
        // TODO: Implement flash effect
        flashAmount = 1.0f;
        damageFlashMaterial.SetFloat("_FlashAmount", flashAmount);
        Invoke(nameof(ResetFlash), flashDuration);
    }

    private void ResetFlash()
    {
        flashAmount = 0.0f;
        damageFlashMaterial.SetFloat("_FlashAmount", flashAmount);
    }
}
