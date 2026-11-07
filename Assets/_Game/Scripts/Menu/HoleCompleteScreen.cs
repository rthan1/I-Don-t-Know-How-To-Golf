using Golf.Throwing;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Between-hole gate (same feel as the main menu): after the ball is holed,
/// show the hole result and wait for A / trigger / Enter before loading the next scene.
/// Stroke counts reset with each new hole scene; RoundScoreTracker keeps the round totals.
/// </summary>
public sealed class HoleCompleteScreen : MonoBehaviour
{
    [Tooltip("Ignore presses briefly so a held A from the putt doesn't skip the screen.")]
    public float inputDelay = 0.6f;

    [Tooltip("Blinks per second for the continue prompt.")]
    public float blinkRate = 1.5f;

    InputAction _continueButton;
    Text _title;
    Text _detail;
    Text _prompt;
    bool _visible;
    bool _loading;
    float _shownAt;
    string _nextScene = HoleProgression.MenuScene;
    bool _wired;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        EnsureInHoleScenes();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => EnsureInHoleScenes();

    static void EnsureInHoleScenes()
    {
        if (FindAnyObjectByType<GolfBall>() == null)
            return;
        if (FindAnyObjectByType<HoleCompleteScreen>() != null)
            return;

        var go = new GameObject("HoleCompleteScreen");
        go.AddComponent<HoleCompleteScreen>();
    }

    void Awake()
    {
        _continueButton = new InputAction("ContinueHole", InputActionType.Button);
        _continueButton.AddBinding("<XRController>{RightHand}/primaryButton");
        _continueButton.AddBinding("<XRController>{LeftHand}/primaryButton");
        _continueButton.AddBinding("<XRController>{RightHand}/triggerPressed");
        _continueButton.AddBinding("<XRController>{LeftHand}/triggerPressed");
        _continueButton.AddBinding("<Keyboard>/enter");
        _continueButton.AddBinding("<Keyboard>/space");
        _continueButton.AddBinding("<Gamepad>/start");

        BuildUi();
        SetVisible(false);
    }

    void OnEnable()
    {
        _continueButton.performed += OnContinuePressed;
        _continueButton.Enable();
        TryWireStrokeManager();
    }

    void OnDisable()
    {
        _continueButton.performed -= OnContinuePressed;
        _continueButton.Disable();
        UnwireStrokeManager();
    }

    void Start() => TryWireStrokeManager();

    void Update()
    {
        if (!_wired)
            TryWireStrokeManager();

        if (!_visible || _prompt == null)
            return;

        _prompt.enabled = _loading || Mathf.Repeat((Time.unscaledTime - _shownAt) * blinkRate, 1f) < 0.65f;
    }

    void TryWireStrokeManager()
    {
        if (_wired || StrokeManager.Instance == null)
            return;

        StrokeManager.Instance.onHoleFinished.AddListener(Show);
        _wired = true;
    }

    void UnwireStrokeManager()
    {
        if (!_wired || StrokeManager.Instance == null)
            return;

        StrokeManager.Instance.onHoleFinished.RemoveListener(Show);
        _wired = false;
    }

    void OnContinuePressed(InputAction.CallbackContext context)
    {
        if (!_visible || _loading)
            return;
        if (Time.unscaledTime - _shownAt < inputDelay)
            return;

        Continue();
    }

    public void Show()
    {
        if (_visible || _loading)
            return;

        StrokeManager manager = StrokeManager.Instance;
        RoundScoreTracker scores = RoundScoreTracker.Ensure();

        string holeName = manager != null ? manager.HoleName : SceneManager.GetActiveScene().name;
        bool hasNext = HoleProgression.TryGetNextHole(holeName, out string next);
        _nextScene = hasNext ? next : HoleProgression.MenuScene;

        string display = HoleProgression.DisplayName(holeName);
        int strokes = manager != null ? manager.StrokeCount : 0;
        int par = manager != null ? manager.Par : 0;
        int toPar = strokes - par;

        if (_title != null)
            _title.text = hasNext ? $"{display}\nCOMPLETE" : "ROUND\nCOMPLETE";

        if (_detail != null)
        {
            string toParText = scores.FormatToPar(toPar);
            _detail.text = hasNext
                ? $"{strokes} STROKES   PAR {par}   ({toParText})"
                : $"TOTAL {scores.TotalStrokes}   ({scores.FormatToPar(scores.TotalToPar)})\nPRESS A FOR MENU";
        }

        if (_prompt != null)
            _prompt.text = hasNext ? "PRESS A TO CONTINUE" : "PRESS A TO FINISH";

        _shownAt = Time.unscaledTime;
        _loading = false;
        SetVisible(true);
        PlayerLocomotionLock.SetLocked(true);
    }

    void Continue()
    {
        if (_loading)
            return;

        _loading = true;
        if (_prompt != null)
            _prompt.text = "LOADING...";

        PlayerLocomotionLock.ForceUnlock();
        SceneManager.LoadScene(_nextScene);
    }

    void SetVisible(bool visible)
    {
        _visible = visible;
        for (int i = 0; i < transform.childCount; i++)
            transform.GetChild(i).gameObject.SetActive(visible);
    }

    void BuildUi()
    {
        var canvasGo = new GameObject("Canvas", typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvasGo.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;

        var rect = canvasGo.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(1600f, 900f);
        canvasGo.transform.localScale = Vector3.one * 0.0025f;

        _title = AddText(canvasGo.transform, "Title", "HOLE\nCOMPLETE", 150,
            new Color(0.973f, 0.722f, 0f), 0.48f, 1f);
        _title.lineSpacing = 0.9f;

        _detail = AddText(canvasGo.transform, "Detail", "", 64,
            new Color(0.988f, 0.988f, 0.988f), 0.28f, 0.48f);

        _prompt = AddText(canvasGo.transform, "Prompt", "PRESS A TO CONTINUE", 72,
            new Color(0.988f, 0.988f, 0.988f), 0.05f, 0.26f);
    }

    void LateUpdate()
    {
        if (!_visible)
            return;

        Camera cam = Camera.main;
        if (cam == null)
            return;

        Transform canvas = transform.childCount > 0 ? transform.GetChild(0) : null;
        if (canvas == null)
            return;

        Vector3 flatFwd = cam.transform.forward;
        flatFwd.y = 0f;
        if (flatFwd.sqrMagnitude < 0.0001f)
            flatFwd = Vector3.forward;
        flatFwd.Normalize();

        canvas.position = cam.transform.position + flatFwd * 3.2f + Vector3.up * 0.35f;
        canvas.rotation = Quaternion.LookRotation(flatFwd, Vector3.up);
    }

    static Text AddText(Transform parent, string name, string content, int size, Color color, float bottom, float top)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, bottom);
        rect.anchorMax = new Vector2(1f, top);
        rect.offsetMin = rect.offsetMax = Vector2.zero;

        var text = go.AddComponent<Text>();
        text.text = content;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        var outline = go.AddComponent<Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(5f, -5f);
        return text;
    }
}
