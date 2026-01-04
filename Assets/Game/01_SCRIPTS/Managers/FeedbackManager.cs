using System;
using System.Collections;
using junklite;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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
        if (FindAnyObjectByType<FeedbackManager>() == null)
        {
            var go = new GameObject("FeedbackManager");
            go.AddComponent<FeedbackManager>();
            DontDestroyOnLoad(go);
        }

        // Keep static refs fresh across domain reload / scene loads
        instance = FindAnyObjectByType<FeedbackManager>();
        Instance = instance;
    }

    void Awake()
    {
        if (instance == null)
        {
            instance = this;
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (instance != this)
        {
            Destroy(gameObject);
            return;
        }
    }

    private void Start()
    {
        if (instance == null)
        {
            instance = this;
            Instance = this;
        }

        if (MAINCAMERA != null)
        {
            MAINCAMERA.Priority = 1;
            MAINCAMERA.gameObject.SetActive(true);
        }
        else
        {
            Debug.LogWarning("FeedbackManager: MAINCAMERA is not assigned.");
        }
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        if (Instance == this) Instance = null;
    }

    void OnApplicationQuit()
    {
        if (instance == this) instance = null;
        if (Instance == this) Instance = null;
    }

    void OnDisable()
    {
        // Keep legacy behavior: scripts may expect instance to be cleared when disabled.
        if (instance == this) instance = null;
        if (Instance == this) Instance = null;
    }

    void OnEnable()
    {
        instance = this;
        Instance = this;
    }

    void OnApplicationFocus(bool hasFocus)
    {
        instance = this;
        Instance = this;
    }

    void OnApplicationPause(bool isPaused)
    {
        instance = this;
        Instance = this;
    }

    #endregion Singleton Construction

    #region rumble

    public void ParryFeedback()
    {
        throw new NotImplementedException();
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

    #region Camera / PostFX (Cinemachine + URP VolumeProfile)

    [Header("Cameras (Cinemachine)")]
    [SerializeField] private CinemachineCamera MAINCAMERA;
    [SerializeField] private CinemachineCamera deathCamera;
    [SerializeField] private CinemachineCamera pauseCamera;
    [SerializeField] private CinemachineCamera gameOverCamera;
    [SerializeField] private CinemachineCamera gameWinCamera;
    [SerializeField] private CinemachineCamera gameLoseCamera;
    [SerializeField] private CinemachineCamera gamePauseCamera;
    [SerializeField] private CinemachineCamera gameResumeCamera;
    [SerializeField] private CinemachineCamera gameRestartCamera;

    /// <summary>
    /// Set URP Color Adjustments -> Post Exposure on a CinemachineCamera's Volume Settings profile.
    /// If <paramref name="vcam"/> is null, uses <see cref="MAINCAMERA"/>.
    /// </summary>
    public void SetExposure(float postExposure, CinemachineCamera vcam = null)
    {
        vcam ??= MAINCAMERA;
        if (vcam == null)
        {
            Debug.LogWarning("FeedbackManager.SetExposure: No CinemachineCamera provided and MAINCAMERA is not assigned.");
            return;
        }

        if (!TryGetCinemachineVolumeProfile(vcam, out var profile))
            return;

        var colorAdj = GetOrAddOverride<ColorAdjustments>(profile);
        colorAdj.active = true;
        colorAdj.postExposure.overrideState = true;
        colorAdj.postExposure.value = postExposure;
    }

    private static bool TryGetCinemachineVolumeProfile(CinemachineCamera vcam, out VolumeProfile profile)
    {
        profile = null;
        if (vcam == null)
            return false;

        var cmVolume = vcam.GetComponent<CinemachineVolumeSettings>();
        if (cmVolume == null)
        {
            Debug.LogWarning(
                $"FeedbackManager: {vcam.gameObject.name} has no CinemachineVolumeSettings. " +
                "Add component: Cinemachine/Procedural/Extensions/Cinemachine Volume Settings.");
            return false;
        }

        profile = cmVolume.Profile;
        if (profile == null)
        {
            Debug.LogWarning(
                $"FeedbackManager: {vcam.gameObject.name} CinemachineVolumeSettings.Profile is null. " +
                "Assign a URP VolumeProfile asset there.");
            return false;
        }

        return true;
    }

    private static T GetOrAddOverride<T>(VolumeProfile profile)
        where T : VolumeComponent
    {
        if (profile.TryGet(out T component))
            return component;
        return profile.Add<T>(overrides: false);
    }

    #endregion Camera / PostFX (Cinemachine + URP VolumeProfile)
}