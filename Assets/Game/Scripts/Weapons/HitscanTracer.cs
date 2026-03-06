using UnityEngine;
using System;

namespace junklite
{

    [RequireComponent(typeof(LineRenderer))]
    public class HitscanTracer : MonoBehaviour
    {
        private LineRenderer line;
        private float duration;
        private float elapsed;
        private Color startColor;
        private Color endColor;
        private bool isActive;

        private Action onReturn;

        private void Awake()
        {
            line = GetComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
        }

        // =================================================================
        // INITIALIZATION — called by WeaponManager after pulling from pool
        // =================================================================

        public void Initialize(Vector3 from, Vector3 to, float fadeDuration, Action returnCallback)
        {
            onReturn = returnCallback;
            duration = Mathf.Max(fadeDuration, 0.01f);
            elapsed = 0f;
            isActive = true;

            line.SetPosition(0, from);
            line.SetPosition(1, to);

            // Cache the starting colors so we can fade alpha
            startColor = line.startColor;
            endColor = line.endColor;

            // Reset alpha to full
            startColor.a = 1f;
            endColor.a = 1f;
            line.startColor = startColor;
            line.endColor = endColor;
            line.enabled = true;
        }

        // =================================================================
        // UPDATE — fade alpha, then return to pool
        // =================================================================

        private void Update()
        {
            if (!isActive) return;

            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // Fade alpha from 1 → 0
            float alpha = 1f - t;
            Color sc = startColor;
            Color ec = endColor;
            sc.a = alpha;
            ec.a = alpha;
            line.startColor = sc;
            line.endColor = ec;

            if (t >= 1f)
                ReturnToPool();
        }

        // =================================================================
        // POOL RETURN
        // =================================================================

        private void ReturnToPool()
        {
            isActive = false;
            line.enabled = false;
            onReturn?.Invoke();
        }
    }
}