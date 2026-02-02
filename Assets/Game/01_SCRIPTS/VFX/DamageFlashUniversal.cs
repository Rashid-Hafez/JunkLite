using UnityEngine;
using System.Collections;

namespace junklite
{
    public class DamageFlashUniversal : MonoBehaviour
    {
        [SerializeField] private float flashDuration = 0.1f;
        [SerializeField] private float flashAmount = 0.8f;
        [SerializeField] private bool isSpine = false;
        [SerializeField] private Color flashColor = Color.white;
        private SpriteRenderer[] _spriteRendererArray;
        private Material[] _materialArray;
        private Coroutine _flashCoroutine;
        private float flashamounttemp;
        private Renderer _spineRenderer;

        private void Awake()
        {
            if (isSpine)
            {
                _spineRenderer = GetComponentInChildren<Renderer>();
                if (_spineRenderer != null)
                    _materialArray = _spineRenderer.materials;
            }
            else
            {
                _spriteRendererArray = GetComponentsInChildren<SpriteRenderer>();
                InitializeMaterials();
            }
            flashamounttemp = flashAmount;
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
            if (_materialArray == null || _materialArray.Length == 0) yield break;

            if (isSpine)
            {
                foreach (var mat in _materialArray)
                {
                    if (mat.HasProperty("_FillPhase"))
                        mat.SetFloat("_FillPhase", flashAmount);
                    else if (mat.HasProperty("FillPhase"))
                        mat.SetFloat("FillPhase", flashAmount);
                }
            }
            else
            {
                foreach (var mat in _materialArray)
                    mat.SetFloat("_FlashAmount", flashamounttemp);
            }

            yield return new WaitForSeconds(flashDuration);
            ResetFlash();
            _flashCoroutine = null;
        }

        private void ResetFlash()
        {
            if (_materialArray == null) return;

            if (isSpine)
            {
                foreach (var mat in _materialArray)
                {
                    if (mat.HasProperty("_FillPhase"))
                        mat.SetFloat("_FillPhase", 1f);
                    else if (mat.HasProperty("FillPhase"))
                        mat.SetFloat("FillPhase", 1f);
                }
            }
            else
            {
                foreach (var mat in _materialArray)
                    mat.SetFloat("_FlashAmount", 1f);
            }
        }
    }
}