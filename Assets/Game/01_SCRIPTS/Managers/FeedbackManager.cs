using System;
using System.Collections;
using System.Collections.Generic;
using junklite;
using Unity.Cinemachine;
using UnityEngine;

public class FeedbackManager : MonoBehaviour
{
    public static FeedbackManager instance;

    [SerializeField] float rumbleDuration = 0.2f;
    [SerializeField] float rumbleIntensity = 0.5f;

    Coroutine HitStopCoroutine;

    public static FeedbackManager Instance { get; internal set; }

    #region Singleton Construction

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void InitOnLoad()
    {
        if (FindObjectOfType<FeedbackManager>() == null)
        {
            var go = new GameObject("FeedbackManager");
            go.AddComponent<FeedbackManager>();
            UnityEngine.Object.DontDestroyOnLoad(go);
        }
    }

    void Awake()
    {
        if (instance == null)
        {
            instance = this;
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
        }
        else if (instance != this)
        {
            Destroy(gameObject);
            return;
        }
    }

    #endregion Singleton Construction

    #region rumble

    void Start()
    {

    }
    public void CinemachineShake(Unity.Cinemachine.CinemachineImpulseSource impulseSource, float force = 1f)
    {
        if (impulseSource != null)
        {
            Debug.Log($"FeedbackManager: Generating impulse (force={force}) from {impulseSource.gameObject.name}");
            impulseSource.GenerateImpulse(force);
        }
        else
        {
            Debug.LogWarning("FeedbackManager.CinemachineShake called with null impulseSource");
        }
    }
    #endregion rumble

    #region hitstop

    public void HitStop(float duration)
    {
        if (HitStopCoroutine != null)
            StopCoroutine(HitStopCoroutine);
        HitStopCoroutine = StartCoroutine(HitStopCoroutineRoutine(duration));
    }

    private IEnumerator HitStopCoroutineRoutine(float duration)
    {
        float originalTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        yield return new WaitForSecondsRealtime(duration);
        Time.timeScale = originalTimeScale;
    }

    #endregion hitstop

}