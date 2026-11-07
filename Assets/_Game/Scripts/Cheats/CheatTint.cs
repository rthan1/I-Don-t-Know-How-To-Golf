using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Tints the player's view red and loops a heartbeat while any cheat is happening (cheat throw, pool table up, ...).
/// It creates itself when a scene with a GolfBall loads, so it doesn't need to be added to any scene.
/// Works like ComfortFade: a see-through panel parented just in front of the headset camera.
/// </summary>
public class CheatTint : MonoBehaviour
{
    [Tooltip("Tint color. Alpha is how strong the tint gets while cheating.")]
    public Color tint = new Color(1f, 0f, 0f, 0.15f);

    [Tooltip("Seconds to fade the tint in or out.")]
    [Min(0.01f)] public float fadeSeconds = 0.25f;

    [Tooltip("Heartbeat volume at full strength, 0 to 1. It fades in and out with the tint.")]
    [Range(0f, 1f)] public float heartbeatVolume = 0.8f;

    AudioSource heartbeat;
    CanvasGroup group;
    Image image;
    float alpha;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        CreateIfNeeded();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => CreateIfNeeded();

    static void CreateIfNeeded()
    {
        if (FindAnyObjectByType<CheatTint>() != null) return;
        if (FindAnyObjectByType<GolfBall>() == null) return;

        new GameObject("CheatTint").AddComponent<CheatTint>();
    }

    void Awake()
    {
        Build();
        SetAlpha(0f);
    }

    void LateUpdate()
    {
        AttachToCamera();

        float target = SecurityCameraSystem.AnyCheating ? 1f : 0f;
        if (Mathf.Approximately(alpha, target)) return;

        alpha = Mathf.MoveTowards(alpha, target, Time.unscaledDeltaTime / fadeSeconds);
        SetAlpha(alpha);
    }

    void SetAlpha(float value)
    {
        alpha = value;
        image.color = tint;
        group.alpha = value;
        // Off entirely when clear: a full-view see-through panel isn't free on Quest.
        bool visible = value > 0.001f;
        group.gameObject.SetActive(visible);

        if (heartbeat != null && heartbeat.clip != null)
        {
            heartbeat.volume = value * heartbeatVolume;
            // Restart from the top each time, so the first "dun dun" lands right as cheating starts.
            if (visible && !heartbeat.isPlaying) heartbeat.Play();
            else if (!visible && heartbeat.isPlaying) heartbeat.Stop();
        }
    }

    void Build()
    {
        var canvasGo = new GameObject("TintCanvas", typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 31000; // under ComfortFade (32000), so teleport fades still go fully black

        // 2 m square, 0.35 m in front of the eyes: wider than the headset's view, so no edges show.
        var rect = canvasGo.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(4f, 4f);
        canvasGo.transform.localPosition = new Vector3(0f, 0f, 0.35f);
        canvasGo.transform.localScale = Vector3.one * 0.5f;

        group = canvasGo.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        var imageGo = new GameObject("Tint", typeof(RectTransform));
        imageGo.transform.SetParent(canvasGo.transform, false);
        var imageRect = imageGo.GetComponent<RectTransform>();
        imageRect.anchorMin = Vector2.zero;
        imageRect.anchorMax = Vector2.one;
        imageRect.offsetMin = imageRect.offsetMax = Vector2.zero;

        image = imageGo.AddComponent<Image>();
        image.raycastTarget = false;

        // Heartbeat: 2D, looping, silent until cheating starts.
        heartbeat = gameObject.AddComponent<AudioSource>();
        heartbeat.clip = SoundLibrary.Instance != null ? SoundLibrary.Instance.heartbeat : null;
        heartbeat.loop = true;
        heartbeat.playOnAwake = false;
        heartbeat.spatialBlend = 0f;
        heartbeat.volume = 0f;
    }

    // Keeps the panel on the current headset camera (it can change across scene loads).
    void AttachToCamera()
    {
        Camera cam = Camera.main;
        if (cam == null || transform.parent == cam.transform) return;

        transform.SetParent(cam.transform, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one;
    }
}
