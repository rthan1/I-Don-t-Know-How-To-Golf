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

    // Scorecard grid: one column per hole in HoleProgression, plus a total column.
    Text[] _parCells;
    Text[] _scoreCells;
    Image[] _columnHighlights;  // pale yellow behind the hole just played
    Image[] _markOuter;         // circle (birdie) / square (bogey) drawn around the score
    Image[] _markInner;         // second circle (eagle) / second square (double bogey or worse)

    static readonly Color Yellow = new Color(0.973f, 0.722f, 0f);
    static readonly Color White = new Color(0.988f, 0.988f, 0.988f);

    // Printed-card palette
    static readonly Color CardPaper = new Color(0.97f, 0.95f, 0.88f);
    static readonly Color CardGreen = new Color(0.08f, 0.33f, 0.18f);
    static readonly Color CardLine = new Color(0.08f, 0.33f, 0.18f, 0.55f);
    static readonly Color CardInk = new Color(0.1f, 0.12f, 0.1f);
    static readonly Color CardRedInk = new Color(0.78f, 0.1f, 0.1f);
    static readonly Color CardTotalTint = new Color(0.9f, 0.87f, 0.77f);
    static readonly Color CardHighlight = new Color(1f, 0.93f, 0.55f);

    static Sprite s_RingSprite;
    static Sprite s_BoxSprite;

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
            _title.text = ScoreNames.Headline(strokes, par); // BIRDIE!, PAR, NONUPLE BOGEY...

        if (_detail != null)
        {
            string toParText = scores.FormatToPar(toPar);
            string scoreLine = $"{display}: {strokes} STROKES   PAR {par}   ({toParText})";
            if (strokes == 1) scoreLine += $"   {ScoreNames.ForToPar(toPar)}"; // a hole in one still shows its name
            if (manager != null && manager.GaveUp) scoreLine += "   (GAVE UP)";

            _detail.text = scoreLine;
        }

        FillScorecard(scores, holeName);

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
        rect.sizeDelta = new Vector2(1600f, 1150f);
        canvasGo.transform.localScale = Vector3.one * 0.0025f;

        _title = AddText(canvasGo.transform, "Title", "HOLE\nCOMPLETE", 130, Yellow, 0.72f, 1f);
        _title.lineSpacing = 0.9f;

        _detail = AddText(canvasGo.transform, "Detail", "", 52, White, 0.62f, 0.72f);

        BuildScorecard(canvasGo.transform, 0.19f, 0.6f);

        _prompt = AddText(canvasGo.transform, "Prompt", "PRESS A TO CONTINUE", 64, White, 0.04f, 0.17f);
    }

    // A printed golf scorecard: cream paper in a green frame, a green title strip and HOLE row,
    // ruled grid lines, dark ink numbers. Birdies get a circle, bogeys a square, like players mark them.
    void BuildScorecard(Transform parent, float bottom, float top)
    {
        int holes = HoleProgression.HoleScenes.Length;
        int dataColumns = holes + 1;          // holes + TOT
        const float labelWidth = 0.2f;        // the HOLE / PAR / SCORE column
        float colWidth = (1f - labelWidth) / dataColumns;

        // Row bands, top to bottom (fractions of the card's inner area).
        const float titleBottom = 0.8f, holeBottom = 0.56f, parBottom = 0.3f;

        // Frame and paper
        Image frame = AddBox(parent, "Scorecard", CardGreen, 0.04f, 0.96f, bottom, top);
        Image paper = AddBox(frame.transform, "Paper", CardPaper, 0f, 1f, 0f, 1f);
        paper.rectTransform.offsetMin = new Vector2(10f, 10f);
        paper.rectTransform.offsetMax = new Vector2(-10f, -10f);
        Transform card = paper.transform;

        // Title strip and the green HOLE row
        AddBox(card, "TitleBar", CardGreen, 0f, 1f, titleBottom, 1f);
        AddInk(card, "SCORECARD", 44, CardPaper, FontStyle.Bold, 0f, 1f, titleBottom, 1f);
        AddBox(card, "HoleRow", new Color(0.13f, 0.45f, 0.26f), 0f, 1f, holeBottom, titleBottom);

        // TOT column tint (PAR and SCORE rows)
        float totLeft = labelWidth + holes * colWidth;
        AddBox(card, "TotTint", CardTotalTint, totLeft, 1f, 0f, holeBottom);

        // Highlight for the hole just played (moved into place each time the card is shown)
        _columnHighlights = new Image[holes];
        for (int i = 0; i < holes; i++)
        {
            float left = labelWidth + i * colWidth;
            _columnHighlights[i] = AddBox(card, "Highlight", CardHighlight, left, left + colWidth, 0f, holeBottom);
            _columnHighlights[i].enabled = false;
        }

        // Row labels
        AddInk(card, "  HOLE", 50, CardPaper, FontStyle.Bold, 0f, labelWidth, holeBottom, titleBottom, TextAnchor.MiddleLeft);
        AddInk(card, "  PAR", 50, CardInk, FontStyle.Bold, 0f, labelWidth, parBottom, holeBottom, TextAnchor.MiddleLeft);
        AddInk(card, "  SCORE", 50, CardInk, FontStyle.Bold, 0f, labelWidth, 0f, parBottom, TextAnchor.MiddleLeft);

        _parCells = new Text[dataColumns];
        _scoreCells = new Text[dataColumns];
        _markOuter = new Image[holes];
        _markInner = new Image[holes];
        if (s_RingSprite == null) s_RingSprite = MakeOutlineSprite(circle: true);
        if (s_BoxSprite == null) s_BoxSprite = MakeOutlineSprite(circle: false);

        for (int col = 0; col < dataColumns; col++)
        {
            float left = labelWidth + col * colWidth;
            float right = left + colWidth;
            bool isTotal = col == holes;

            AddInk(card, isTotal ? "TOT" : (col + 1).ToString(), 54, CardPaper, FontStyle.Bold, left, right, holeBottom, titleBottom);
            _parCells[col] = AddInk(card, "", 54, CardInk, FontStyle.Normal, left, right, parBottom, holeBottom);

            if (!isTotal)
            {
                // Score marks sit behind the number, centered in the cell.
                _markOuter[col] = AddMark(card, left, right, parBottom, 104f);
                _markInner[col] = AddMark(card, left, right, parBottom, 80f);
            }

            _scoreCells[col] = AddInk(card, "", isTotal ? 50 : 60, CardInk, FontStyle.Bold, left, right, 0f, parBottom);
        }

        // Ruled lines: between rows, and between columns (not through the title strip).
        AddLine(card, 0f, 1f, holeBottom, holeBottom, horizontal: true);
        AddLine(card, 0f, 1f, parBottom, parBottom, horizontal: true);
        for (int col = 0; col <= dataColumns - 1; col++)
        {
            float x = labelWidth + col * colWidth;
            AddLine(card, x, x, 0f, titleBottom, horizontal: false, thick: col == 0 || col == holes);
        }
    }

    void FillScorecard(RoundScoreTracker scores, string currentHole)
    {
        if (_scoreCells == null)
            return;

        string[] scenes = HoleProgression.HoleScenes;
        int parTotal = 0;

        for (int i = 0; i < scenes.Length; i++)
        {
            bool played = TryGetScore(scores, scenes[i], out RoundScoreTracker.HoleScore score);
            int par = played ? score.par : StrokeManager.ParForScene(scenes[i]);
            parTotal += par;

            _columnHighlights[i].enabled = scenes[i] == currentHole;
            _parCells[i].text = par.ToString();
            _scoreCells[i].text = played ? score.strokes.ToString() : ""; // blank, like an unfilled card

            // Circle = birdie, double circle = eagle or better; square = bogey, double square = worse.
            int toPar = played ? score.ToPar : 0;
            bool under = toPar < 0, over = toPar > 0;
            SetMark(_markOuter[i], under || over, under ? s_RingSprite : s_BoxSprite);
            SetMark(_markInner[i], toPar <= -2 || toPar >= 2, under ? s_RingSprite : s_BoxSprite);
        }

        // Totals: par for the whole course, strokes for the holes played so far.
        int t = scenes.Length;
        _parCells[t].text = parTotal.ToString();
        bool any = scores.Holes.Count > 0;
        _scoreCells[t].text = any ? $"{scores.TotalStrokes}  {scores.FormatToPar(scores.TotalToPar)}" : "";
        _scoreCells[t].color = any && scores.TotalToPar < 0 ? CardRedInk : CardInk; // under par in red, like a leaderboard
    }

    static void SetMark(Image mark, bool show, Sprite sprite)
    {
        mark.enabled = show;
        if (show) mark.sprite = sprite;
    }

    static Image AddBox(Transform parent, string name, Color color, float left, float right, float bottom, float top)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(left, bottom);
        rect.anchorMax = new Vector2(right, top);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var image = go.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    static void AddLine(Transform parent, float left, float right, float bottom, float top, bool horizontal, bool thick = false)
    {
        Image line = AddBox(parent, "Line", CardLine, left, right, bottom, top);
        float half = thick ? 2.5f : 1.5f;
        line.rectTransform.offsetMin = horizontal ? new Vector2(0f, -half) : new Vector2(-half, 0f);
        line.rectTransform.offsetMax = horizontal ? new Vector2(0f, half) : new Vector2(half, 0f);
    }

    static Image AddMark(Transform parent, float left, float right, float top, float size)
    {
        Image mark = AddBox(parent, "Mark", CardInk, left, right, 0f, top);
        var rect = mark.rectTransform;
        Vector2 center = new Vector2((left + right) * 0.5f, top * 0.5f);
        rect.anchorMin = rect.anchorMax = center;
        rect.sizeDelta = new Vector2(size, size);
        mark.enabled = false;
        return mark;
    }

    // Plain card text: dark ink, no outline (the outline on the big headline text would look wrong on paper).
    static Text AddInk(Transform parent, string content, int size, Color color, FontStyle style,
        float left, float right, float bottom, float top, TextAnchor align = TextAnchor.MiddleCenter)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(left, bottom);
        rect.anchorMax = new Vector2(right, top);
        rect.offsetMin = rect.offsetMax = Vector2.zero;

        var text = go.AddComponent<Text>();
        text.text = content;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = align;
        text.color = color;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    // A hand-drawn-looking pen ring or square, generated once (no art asset needed).
    static Sprite MakeOutlineSprite(bool circle)
    {
        const int size = 128;
        const float stroke = 6f;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        float c = (size - 1) * 0.5f;
        float radius = c - 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distToStroke;
                if (circle)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    distToStroke = Mathf.Abs(d - (radius - stroke * 0.5f));
                }
                else
                {
                    float edge = Mathf.Min(Mathf.Min(x, y), Mathf.Min(size - 1 - x, size - 1 - y)) - 2f;
                    distToStroke = Mathf.Abs(edge - stroke * 0.5f);
                }

                float a = Mathf.Clamp01(stroke * 0.5f - distToStroke + 0.5f); // 1px anti-aliased edge
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    static bool TryGetScore(RoundScoreTracker scores, string scene, out RoundScoreTracker.HoleScore result)
    {
        for (int i = 0; i < scores.Holes.Count; i++)
        {
            if (scores.Holes[i].sceneName == scene)
            {
                result = scores.Holes[i];
                return true;
            }
        }

        result = default;
        return false;
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
