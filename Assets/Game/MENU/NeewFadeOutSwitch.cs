using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;

public class NeewFadeOutSwitch : MonoBehaviour
{
 [Header("Scene Settings")]
    [SerializeField] private string sceneName = "LevelScene";
    [SerializeField] private float fadeSpeed = 1.5f;
    [SerializeField] private float waitBeforeLoad = 1f;

    [Header("References")]
    [SerializeField] private CanvasGroup fadePanel; // full-screen black image

    private bool isTransitioning = false;

    void Start()
    {
        // Initialize fade panel to transparent
        fadePanel.alpha = 0f;
        Debug.Log("CanvasGroup reference: " + fadePanel);

    }

    void Update()
    {
        if (!isTransitioning && (Input.anyKeyDown || Input.GetMouseButtonDown(0)))
        {
            StartCoroutine(FadeOutAndLoad());
        }
    }

    public void LoadScene()
    {
        if (!isTransitioning)
            StartCoroutine(FadeOutAndLoad());
    }

    private IEnumerator FadeOutAndLoad()
    {
        isTransitioning = true;

        // Fade to black
        while (fadePanel.alpha < 1f)
        {
            fadePanel.alpha += Time.deltaTime * fadeSpeed;
            yield return null;
        }

        // Wait before switching scene
        yield return new WaitForSeconds(waitBeforeLoad);

        SceneManager.LoadScene(sceneName);
    }
}