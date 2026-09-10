using UnityEngine;
using System.Collections;

namespace junklite
{
    public class DamageFlashUniversal : MonoBehaviour
    {
        [SerializeField] private float flashDuration = 0.1f;
        [SerializeField] private float flashAmount = 0.4f;
        [SerializeField] private float normalAmount = 1f;
        [SerializeField] private bool isSpine = false;
        [SerializeField] private Color flashColor = Color.white;
        private Renderer[] _renderers;
        private bool[] _supportsFlashAmount;
        private bool[] _supportsFlashColor;
        private Coroutine _flashCoroutine;
        private MaterialPropertyBlock _propertyBlock;
        private static readonly int AmountToFlashId = Shader.PropertyToID("_AmountToFlash");
        private static readonly int FlashColorId = Shader.PropertyToID("_FlashColor");

        private void Awake()
        {
            _renderers = isSpine
                ? GetComponentsInChildren<Renderer>(true)
                : GetComponentsInChildren<SpriteRenderer>(true);
            _propertyBlock = new MaterialPropertyBlock();
            CacheSupportedProperties();
        }

        private void CacheSupportedProperties()
        {
            _supportsFlashAmount = new bool[_renderers.Length];
            _supportsFlashColor = new bool[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer renderer = _renderers[i];
                if (renderer == null) continue;

                Material[] materials = renderer.sharedMaterials;
                for (int j = 0; j < materials.Length; j++)
                {
                    Material material = materials[j];
                    if (material == null) continue;
                    _supportsFlashAmount[i] |= material.HasProperty(AmountToFlashId);
                    _supportsFlashColor[i] |= material.HasProperty(FlashColorId);
                }
            }
        }

        public void Flash()
        {
            if (_flashCoroutine != null)
                StopCoroutine(_flashCoroutine);
            _flashCoroutine = StartCoroutine(FlashCoroutine());
        }

        private IEnumerator FlashCoroutine()
        {
            if (_renderers == null || _renderers.Length == 0) yield break;
            SetFlashProperties(flashAmount, flashColor);

            yield return new WaitForSeconds(flashDuration);
            ResetFlash();
            _flashCoroutine = null;
        }

        private void ResetFlash()
        {
            if (_renderers == null) return;
            SetFlashProperties(normalAmount, flashColor);
        }

        private void SetFlashProperties(float amountToFlash, Color color)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer renderer = _renderers[i];
                if (renderer == null || (!_supportsFlashAmount[i] && !_supportsFlashColor[i]))
                    continue;

                _propertyBlock.Clear();
                renderer.GetPropertyBlock(_propertyBlock);
                if (_supportsFlashAmount[i])
                    _propertyBlock.SetFloat(AmountToFlashId, amountToFlash);
                if (_supportsFlashColor[i])
                    _propertyBlock.SetColor(FlashColorId, color);
                renderer.SetPropertyBlock(_propertyBlock);
            }
        }

        private void OnDisable()
        {
            if (_flashCoroutine != null)
            {
                StopCoroutine(_flashCoroutine);
                _flashCoroutine = null;
            }
            ResetFlash();
        }
    }
}
