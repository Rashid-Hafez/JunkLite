using System;
using junklite;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;

public class FeedbackManager : MonoBehaviour
{
    private static FeedbackManager instance;

    [SerializeField] float rumbleDuration = 0.2f;
    [SerializeField] float rumbleIntensity = 0.5f;

    Coroutine HitStopCoroutine;

#region Singleton Construction
    public static FeedbackManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindObjectOfType<FeedbackManager>();
                if (instance == null)
                {
                    GameObject obj = new GameObject("FeedbackManager");
                    instance = obj.AddComponent<FeedbackManager>();
                }
            }
            return instance;
        }
    }
    void Awake()
    {
        if (instance == null)
        {
            instance = this;
            //DontDestroyOnLoad(gameObject);
        }
        
    }
#endregion Singleton Construction

#region rumble
    public void CinemachineShake(CinemachineImpulseSource impulseSource)
    {
        if (impulseSource != null)
        {
            impulseSource.GenerateImpulse();
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