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
        private SpriteRenderer[] _spriteRendererArray;
        private Material[] _materialArray;
        private Coroutine _flashCoroutine;
        private Renderer[] _spineRenderers;
        private MaterialPropertyBlock _spinePropertyBlock;
        private static readonly int AmountToFlashId = Shader.PropertyToID("_AmountToFlash");
        private static readonly int FlashColorId = Shader.PropertyToID("_FlashColor");

        private void Awake()
        {
            if (isSpine)
            {
                _spineRenderers = GetComponentsInChildren<Renderer>(true);
                _spinePropertyBlock = new MaterialPropertyBlock();
            }
            else
            {
                _spriteRendererArray = GetComponentsInChildren<SpriteRenderer>(true);
                InitializeMaterials();
            }
        }

        private void InitializeMaterials()
        {
            _materialArray = new Material[_spriteRendererArray.Length];
            for (int i = 0; i < _spriteRendererArray.Length; i++)
                _materialArray[i] = _spriteRendererArray[i].material;
        }

        public void Flash()
        {
            if (_flashCoroutine != null)
                StopCoroutine(_flashCoroutine);
            _flashCoroutine = StartCoroutine(FlashCoroutine());
        }

        private IEnumerator FlashCoroutine()
        {
            if (isSpine)
            {
                if (_spineRenderers == null || _spineRenderers.Length == 0) yield break;
                foreach (var r in _spineRenderers)
                {
                    if (r == null) continue;
                    r.GetPropertyBlock(_spinePropertyBlock);
                    if (RendererHasProperty(r, AmountToFlashId))
                        _spinePropertyBlock.SetFloat(AmountToFlashId, flashAmount);
                    if (RendererHasProperty(r, FlashColorId))
                        _spinePropertyBlock.SetColor(FlashColorId, flashColor);
                    r.SetPropertyBlock(_spinePropertyBlock);
                }
            }
            else
            {
                if (_materialArray == null || _materialArray.Length == 0) yield break;
                foreach (var mat in _materialArray)
                    SetFlashProperties(mat, flashAmount, flashColor);
            }

            yield return new WaitForSeconds(flashDuration);
            ResetFlash();
            _flashCoroutine = null;
        }

        private void ResetFlash()
        {
            if (isSpine)
            {
                if (_spineRenderers == null) return;
                foreach (var r in _spineRenderers)
                {
                    if (r == null) continue;
                    r.GetPropertyBlock(_spinePropertyBlock);
                    if (RendererHasProperty(r, AmountToFlashId))
                        _spinePropertyBlock.SetFloat(AmountToFlashId, normalAmount);
                    if (RendererHasProperty(r, FlashColorId))
                        _spinePropertyBlock.SetColor(FlashColorId, flashColor);
                    r.SetPropertyBlock(_spinePropertyBlock);
                }
            }
            else
            {
                if (_materialArray == null) return;
                foreach (var mat in _materialArray)
                    SetFlashProperties(mat, normalAmount, flashColor);
            }
        }

        private static void SetFlashProperties(Material mat, float amountToFlash, Color color)
        {
            if (mat == null) return;
            if (mat.HasProperty(AmountToFlashId))
                mat.SetFloat(AmountToFlashId, amountToFlash);
            if (mat.HasProperty(FlashColorId))
                mat.SetColor(FlashColorId, color);
        }

        private static bool RendererHasProperty(Renderer r, int propertyId)
        {
            var mats = r.sharedMaterials;
            if (mats == null) return false;
            foreach (var mat in mats)
            {
                if (mat != null && mat.HasProperty(propertyId))
                    return true;
            }
            return false;
        }
    }
}