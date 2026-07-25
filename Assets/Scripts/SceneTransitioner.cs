using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SceneTransitioner : MonoBehaviour
{
    public static SceneTransitioner Instance { get; private set; }

    public float fadeDuration = 0.1f;

    // 前シーンのカメラ状態を記憶
    [HideInInspector] public float savedDistance = -1f;
    [HideInInspector] public float savedAzimuth = 0f;
    [HideInInspector] public float savedElevation = 0f;

    private CanvasGroup canvasGroup;
    private bool busy = false;

    public static SceneTransitioner Get()
    {
        if (Instance != null) return Instance;
        var go = new GameObject("SceneTransitioner");
        return go.AddComponent<SceneTransitioner>();
    }

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildFadeCanvas();
        StartCoroutine(FadeIn());
    }

    void BuildFadeCanvas()
    {
        var go = new GameObject("FadeCanvas");
        go.transform.SetParent(transform);

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999;
        go.AddComponent<CanvasScaler>();
        go.AddComponent<GraphicRaycaster>();

        var panel = new GameObject("FadePanel");
        panel.transform.SetParent(go.transform, false);

        var img = panel.AddComponent<Image>();
        img.color = Color.black;

        var rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        canvasGroup = go.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
    }

    public void TransitionTo(string sceneName)
    {
        if (busy) return;
        StartCoroutine(DoTransition(sceneName));
    }

    IEnumerator DoTransition(string sceneName)
    {
        busy = true;
        yield return StartCoroutine(Fade(0f, 1f));
        yield return SceneManager.LoadSceneAsync(sceneName);
        yield return StartCoroutine(FadeIn());
        busy = false;
    }

    IEnumerator FadeIn()
    {
        yield return StartCoroutine(Fade(1f, 0f));
    }

    IEnumerator Fade(float from, float to)
    {
        float t = 0f;
        canvasGroup.alpha = from;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(from, to, t / fadeDuration);
            yield return null;
        }
        canvasGroup.alpha = to;
    }
}
