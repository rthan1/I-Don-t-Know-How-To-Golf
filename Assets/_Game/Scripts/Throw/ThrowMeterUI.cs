using UnityEngine;
using UnityEngine.UI;

namespace Golf.Throwing
{
    /// <summary>
    /// Tall meter bar, locked to the headset so it stays in view as you look around.
    /// Power/accuracy sit slightly left of center so the right-hand hold sprite does not cover them.
    /// Power: top green (strong) → yellow → orange → red bottom (weak).
    /// Accuracy: center green (best) → yellow → orange → red edges (worst).
    /// </summary>
    public sealed class ThrowMeterUI : MonoBehaviour
    {
        [SerializeField] Vector3 headLocalOffset = new Vector3(-0.28f, -0.02f, 0.85f);
        [SerializeField] Vector2 canvasSize = new Vector2(360f, 1040f);
        [SerializeField] float canvasScale = 0.00175f;

        // Accuracy: mirrored around center. Green middle is smaller.
        static readonly float[] AccuracyEdges = { 0f, 0.16f, 0.32f, 0.44f, 0.56f, 0.68f, 0.84f, 1f };
        static readonly Color[] AccuracyColors =
        {
            new Color(0.95f, 0.15f, 0.12f, 1f), // red
            new Color(1f, 0.5f, 0.08f, 1f),     // orange
            new Color(1f, 0.86f, 0.1f, 1f),     // yellow
            new Color(0.15f, 0.9f, 0.28f, 1f),  // green
            new Color(1f, 0.86f, 0.1f, 1f),     // yellow
            new Color(1f, 0.5f, 0.08f, 1f),     // orange
            new Color(0.95f, 0.15f, 0.12f, 1f), // red
        };

        // Power: top → bottom. Green top is strongest.
        static readonly float[] PowerEdges = { 0f, 0.25f, 0.5f, 0.75f, 1f };
        static readonly Color[] PowerColors =
        {
            new Color(0.95f, 0.15f, 0.12f, 1f), // red bottom
            new Color(1f, 0.5f, 0.08f, 1f),     // orange
            new Color(1f, 0.86f, 0.1f, 1f),     // yellow
            new Color(0.15f, 0.9f, 0.28f, 1f),  // green top
        };

        public enum MeterDisplayMode
        {
            Hidden,
            PowerFill,
            AccuracyNeedle,
        }

        static Sprite s_WhiteSprite;

        Transform _follow;
        Text _title;
        Text _prompt;
        Image _marker;
        RectTransform _markerRect;
        RectTransform _barRect;
        GameObject _root;
        GameObject _bar;
        MeterDisplayMode _mode;

        public void BindFollowTarget(Transform follow) => _follow = follow;

        // Kept so older callers still compile; meter is head-locked now.
        public void BindLeftHandFollow(Transform leftHand) { }

        public void Show(string title, string prompt, MeterDisplayMode mode)
        {
            Rebuild(mode);
            _root.SetActive(true);
            _title.text = title;
            _prompt.text = prompt;
            _mode = mode;

            bool showBar = mode != MeterDisplayMode.Hidden;
            _bar.SetActive(showBar);
            _marker.gameObject.SetActive(showBar);
            // Bar modes: left of gaze. Aim prompt: centered a bit lower.
            headLocalOffset = showBar
                ? new Vector3(-0.28f, -0.02f, 0.85f)
                : new Vector3(0f, -0.12f, 0.75f);

            if (showBar)
                SetValue(mode == MeterDisplayMode.PowerFill ? 0f : 0.5f);
        }

        public void Hide()
        {
            if (_root != null)
                _root.SetActive(false);
        }

        public void SetValue(float value01)
        {
            if (_markerRect == null || _barRect == null)
                return;

            Canvas.ForceUpdateCanvases();
            float height = Mathf.Max(1f, _barRect.rect.height);
            float v = Mathf.Clamp01(value01);
            _markerRect.anchoredPosition = new Vector2(0f, (v - 0.5f) * height);
        }

        void LateUpdate()
        {
            if (_root == null || !_root.activeSelf || _follow == null)
                return;

            Transform t = _root.transform;
            t.position = _follow.TransformPoint(headLocalOffset);
            t.rotation = Quaternion.LookRotation(t.position - _follow.position, Vector3.up);
        }

        void Rebuild(MeterDisplayMode mode)
        {
            if (_root != null)
                Destroy(_root);

            _root = null;
            _bar = null;
            _marker = null;
            _markerRect = null;
            _barRect = null;
            Build(mode);
        }

        void Build(MeterDisplayMode mode)
        {
            Sprite white = GetWhiteSprite();
            bool isPower = mode == MeterDisplayMode.PowerFill;
            float[] edges = isPower ? PowerEdges : AccuracyEdges;
            Color[] colors = isPower ? PowerColors : AccuracyColors;

            _root = new GameObject("ThrowMeterUI", typeof(RectTransform));
            _root.transform.SetParent(transform, false);

            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            _root.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;
            _root.AddComponent<GraphicRaycaster>();

            var rootRect = _root.GetComponent<RectTransform>();
            rootRect.sizeDelta = canvasSize;
            _root.transform.localScale = Vector3.one * canvasScale;

            var backdrop = CreateUi("Backdrop", _root.transform);
            SetImage(backdrop, white, new Color(0f, 0f, 0f, 0.55f));
            Stretch(backdrop.GetComponent<RectTransform>());

            _title = CreateText("Title", _root.transform, 34, FontStyle.Bold, TextAnchor.UpperCenter);
            StretchAnchors(_title.rectTransform, 0.05f, 0.91f, 0.95f, 0.99f);

            _prompt = CreateText("Prompt", _root.transform, 22, FontStyle.Normal, TextAnchor.LowerCenter);
            StretchAnchors(_prompt.rectTransform, 0.05f, 0.005f, 0.95f, 0.07f);

            _bar = CreateUi("MeterBar", _root.transform);
            _barRect = _bar.GetComponent<RectTransform>();
            StretchAnchors(_barRect, 0.22f, 0.09f, 0.78f, 0.9f);
            SetImage(_bar, white, new Color(0.05f, 0.05f, 0.05f, 1f));

            for (int i = 0; i < colors.Length; i++)
            {
                var band = CreateUi($"Band_{i}", _bar.transform);
                SetImage(band, white, colors[i]);
                StretchAnchors(band.GetComponent<RectTransform>(), 0f, edges[i], 1f, edges[i + 1]);
            }

            for (int i = 1; i < edges.Length - 1; i++)
            {
                var line = CreateUi($"Cut_{i}", _bar.transform);
                SetImage(line, white, new Color(0f, 0f, 0f, 0.9f));
                var lineRect = line.GetComponent<RectTransform>();
                lineRect.anchorMin = new Vector2(0f, edges[i]);
                lineRect.anchorMax = new Vector2(1f, edges[i]);
                lineRect.pivot = new Vector2(0.5f, 0.5f);
                lineRect.sizeDelta = new Vector2(0f, 6f);
                lineRect.anchoredPosition = Vector2.zero;
                lineRect.offsetMin = new Vector2(0f, lineRect.offsetMin.y);
                lineRect.offsetMax = new Vector2(0f, lineRect.offsetMax.y);
            }

            var markerGo = CreateUi("Marker", _bar.transform);
            _marker = SetImage(markerGo, white, Color.white);
            _markerRect = markerGo.GetComponent<RectTransform>();
            _markerRect.anchorMin = new Vector2(0f, 0.5f);
            _markerRect.anchorMax = new Vector2(1f, 0.5f);
            _markerRect.pivot = new Vector2(0.5f, 0.5f);
            _markerRect.sizeDelta = new Vector2(8f, 30f);
            _markerRect.anchoredPosition = Vector2.zero;

            CreateBarOutline(_bar.transform, white);
            _root.SetActive(false);
        }

        void CreateBarOutline(Transform bar, Sprite white)
        {
            CreateEdge(bar, white, "EdgeL", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(8f, 0f));
            CreateEdge(bar, white, "EdgeR", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(8f, 0f));
            CreateEdge(bar, white, "EdgeB", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 8f));
            CreateEdge(bar, white, "EdgeT", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 8f));
        }

        static void CreateEdge(Transform parent, Sprite white, string name, Vector2 min, Vector2 max, Vector2 size)
        {
            var edge = CreateUi(name, parent);
            SetImage(edge, white, Color.black);
            var rect = edge.GetComponent<RectTransform>();
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
        }

        static GameObject CreateUi(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        static Image SetImage(GameObject go, Sprite sprite, Color color)
        {
            var image = go.GetComponent<Image>();
            if (image == null)
                image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static Text CreateText(string name, Transform parent, int size, FontStyle style, TextAnchor anchor)
        {
            var go = CreateUi(name, parent);
            var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (text.font == null)
                text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = anchor;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        static void StretchAnchors(RectTransform rect, float xMin, float yMin, float xMax, float yMax)
        {
            rect.anchorMin = new Vector2(xMin, yMin);
            rect.anchorMax = new Vector2(xMax, yMax);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        static Sprite GetWhiteSprite()
        {
            if (s_WhiteSprite != null)
                return s_WhiteSprite;

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
            tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Point;

            s_WhiteSprite = Sprite.Create(tex, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f), 100f);
            s_WhiteSprite.name = "ThrowMeterWhite";
            return s_WhiteSprite;
        }
    }
}
