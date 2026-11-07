using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Golf.Throwing
{
    /// <summary>
    /// Full-view black fade parented to the main camera. Used for comfort teleports.
    /// </summary>
    public sealed class ComfortFade : MonoBehaviour
    {
        const float DefaultOutSeconds = 0.2f;
        const float DefaultInSeconds = 0.25f;

        static ComfortFade s_Instance;

        CanvasGroup _group;
        Coroutine _routine;

        public static ComfortFade Ensure()
        {
            if (s_Instance != null)
                return s_Instance;

            var existing = Object.FindAnyObjectByType<ComfortFade>();
            if (existing != null)
            {
                s_Instance = existing;
                return s_Instance;
            }

            var go = new GameObject("ComfortFade");
            s_Instance = go.AddComponent<ComfortFade>();
            return s_Instance;
        }

        void Awake()
        {
            s_Instance = this;
            BuildIfNeeded();
            AttachToCamera();
            SetAlpha(0f);
        }

        void OnDestroy()
        {
            if (s_Instance == this)
                s_Instance = null;
        }

        void LateUpdate()
        {
            // Keep the fade overlay on the active headset/camera across scene loads.
            if (_group == null || _group.transform.parent == null)
                AttachToCamera();
        }

        public IEnumerator FadeOut(float seconds = DefaultOutSeconds)
        {
            yield return FadeTo(1f, Mathf.Max(0.01f, seconds));
        }

        public IEnumerator FadeIn(float seconds = DefaultInSeconds)
        {
            yield return FadeTo(0f, Mathf.Max(0.01f, seconds));
        }

        public void SnapClear()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }

            SetAlpha(0f);
        }

        IEnumerator FadeTo(float target, float seconds)
        {
            BuildIfNeeded();
            AttachToCamera();

            float start = _group.alpha;
            float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                SetAlpha(Mathf.Lerp(start, target, Mathf.Clamp01(t / seconds)));
                yield return null;
            }

            SetAlpha(target);
        }

        void SetAlpha(float alpha)
        {
            BuildIfNeeded();
            _group.alpha = Mathf.Clamp01(alpha);
            _group.blocksRaycasts = alpha > 0.01f;
            _group.gameObject.SetActive(alpha > 0.001f);
        }

        void BuildIfNeeded()
        {
            if (_group != null)
                return;

            var canvasGo = new GameObject("FadeCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 32000;

            var rect = canvasGo.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(2f, 2f);
            canvasGo.transform.localPosition = new Vector3(0f, 0f, 0.35f);
            canvasGo.transform.localRotation = Quaternion.identity;
            canvasGo.transform.localScale = Vector3.one * 0.5f;

            _group = canvasGo.AddComponent<CanvasGroup>();

            var imageGo = new GameObject("Black", typeof(RectTransform));
            imageGo.transform.SetParent(canvasGo.transform, false);
            var imageRect = imageGo.GetComponent<RectTransform>();
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.offsetMin = imageRect.offsetMax = Vector2.zero;

            var image = imageGo.AddComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = false;
        }

        void AttachToCamera()
        {
            Camera cam = Camera.main;
            if (cam == null)
                return;

            if (transform.parent == cam.transform)
                return;

            transform.SetParent(cam.transform, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
        }
    }
}
