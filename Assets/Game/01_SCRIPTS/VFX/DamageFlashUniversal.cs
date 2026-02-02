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
        private Renderer[] _spineRenderers;
        private MaterialPropertyBlock _spinePropertyBlock;
        private static readonly int FillPhaseId = Shader.PropertyToID("_FillPhase");

        private void Awake()
        {
            if (isSpine)
            {
                _spineRenderers = GetComponentsInChildren<Renderer>();
                _spinePropertyBlock = new MaterialPropertyBlock();
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
            if (isSpine)
            {
                if (_spineRenderers == null || _spineRenderers.Length == 0) yield break;
                foreach (var r in _spineRenderers)
                {
                    if (r == null) continue;
                    r.GetPropertyBlock(_spinePropertyBlock);
                    _spinePropertyBlock.SetFloat(FillPhaseId, flashAmount);
                    r.SetPropertyBlock(_spinePropertyBlock);
                }
            }
            else
            {
                if (_materialArray == null || _materialArray.Length == 0) yield break;
                foreach (var mat in _materialArray)
                    mat.SetFloat("_FlashAmount", flashamounttemp);
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
                    _spinePropertyBlock.SetFloat(FillPhaseId, 0f);
                    r.SetPropertyBlock(_spinePropertyBlock);
                }
            }
            else
            {
                if (_materialArray == null) return;
                foreach (var mat in _materialArray)
                    mat.SetFloat("_FlashAmount", 1f);
            }
        }
    }
}