using UnityEngine;
using UnityEngine.UI;

namespace Golf.Throwing
{
    /// <summary>
    /// Head-locked top-left picture-in-picture that follows a ball in flight.
    /// </summary>
    public sealed class BallFlightPip : MonoBehaviour
    {
        const int RtWidth = 512;
        const int RtHeight = 288;
        const float BackDistance = 2.2f;
        const float HeightOffset = 1.1f;
        const float MinFlightSpeed = 0.4f;
        const float PipLocalX = -0.30f;
        const float PipLocalY = 0.20f;
        const float PipLocalZ = 0.55f;
        const float PipWidth = 0.28f;
        const float PipHeight = 0.1575f;

        GolfBall _ball;
        Transform _holeTarget;
        Camera _pipCamera;
        RenderTexture _rt;
        GameObject _uiRoot;
        Vector3 _lastTravelDir = Vector3.forward;
        bool _cameraEnabled = true;

        public static BallFlightPip Create(GolfBall ball, Transform holeTarget, Vector3 initialTravelDir)
        {
            var go = new GameObject("BallFlightPip");
            var pip = go.AddComponent<BallFlightPip>();
            pip.Initialize(ball, holeTarget, initialTravelDir);
            return pip;
        }

        void Initialize(GolfBall ball, Transform holeTarget, Vector3 initialTravelDir)
        {
            _ball = ball;
            _holeTarget = holeTarget;
            if (initialTravelDir.sqrMagnitude > 0.0001f)
                _lastTravelDir = Flatten(initialTravelDir);

            BuildCamera();
            BuildUi();
            UpdateCameraPose(instant: true);
        }

        public void SetCameraEnabled(bool enabled)
        {
            _cameraEnabled = enabled;
            if (_pipCamera != null)
                _pipCamera.enabled = enabled;
        }

        public void Dispose()
        {
            if (_pipCamera != null)
            {
                _pipCamera.targetTexture = null;
                Destroy(_pipCamera.gameObject);
                _pipCamera = null;
            }

            if (_uiRoot != null)
            {
                Destroy(_uiRoot);
                _uiRoot = null;
            }

            if (_rt != null)
            {
                _rt.Release();
                Destroy(_rt);
                _rt = null;
            }

            Destroy(gameObject);
        }

        void LateUpdate()
        {
            if (_ball == null)
                return;

            AttachUiToHeadset();
            UpdateTravelDir();
            if (_cameraEnabled)
                UpdateCameraPose(instant: false);
        }

        void BuildCamera()
        {
            _rt = new RenderTexture(RtWidth, RtHeight, 16, RenderTextureFormat.ARGB32)
            {
                name = "BallFlightPipRT",
                antiAliasing = 1,
            };
            _rt.Create();

            var camGo = new GameObject("BallFlightPipCamera");
            camGo.transform.SetParent(transform, false);
            _pipCamera = camGo.AddComponent<Camera>();
            _pipCamera.targetTexture = _rt;
            _pipCamera.nearClipPlane = 0.05f;
            _pipCamera.farClipPlane = 250f;
            _pipCamera.fieldOfView = 55f;
            _pipCamera.depth = -20f;
            _pipCamera.clearFlags = CameraClearFlags.Skybox;
            _pipCamera.allowHDR = false;
            _pipCamera.allowMSAA = false;

            var listener = camGo.GetComponent<AudioListener>();
            if (listener != null)
                Destroy(listener);
        }

        void BuildUi()
        {
            Camera headset = Camera.main;
            Transform parent = headset != null ? headset.transform : transform;

            _uiRoot = new GameObject("BallFlightPipUI", typeof(RectTransform));
            _uiRoot.transform.SetParent(parent, false);

            var canvas = _uiRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 250;

            var rect = _uiRoot.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(PipWidth * 1000f, PipHeight * 1000f);
            _uiRoot.transform.localScale = Vector3.one * 0.001f;
            PlaceUiLocal();

            var frame = CreateUiChild("Frame", _uiRoot.transform);
            var frameImage = frame.AddComponent<Image>();
            frameImage.color = new Color(0f, 0f, 0f, 0.75f);
            frameImage.raycastTarget = false;
            Stretch(frame.GetComponent<RectTransform>(), -8f);

            var view = CreateUiChild("View", _uiRoot.transform);
            var viewImage = view.AddComponent<RawImage>();
            viewImage.texture = _rt;
            viewImage.color = Color.white;
            viewImage.raycastTarget = false;
            Stretch(view.GetComponent<RectTransform>(), 0f);

            var labelGo = CreateUiChild("Label", _uiRoot.transform);
            var labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.sizeDelta = new Vector2(0f, 70f);
            // Centered on the top edge — clear of the ball, not too high.
            labelRect.anchoredPosition = new Vector2(0f, 6f);

            var label = labelGo.AddComponent<Text>();
            label.text = "BALL CAM";
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 48;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            var outline = labelGo.AddComponent<Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(3f, -3f);
        }

        void AttachUiToHeadset()
        {
            if (_uiRoot == null)
                return;

            Camera headset = Camera.main;
            if (headset == null)
                return;

            if (_uiRoot.transform.parent != headset.transform)
                _uiRoot.transform.SetParent(headset.transform, false);

            PlaceUiLocal();
        }

        void PlaceUiLocal()
        {
            if (_uiRoot == null)
                return;

            _uiRoot.transform.localPosition = new Vector3(PipLocalX, PipLocalY, PipLocalZ);
            _uiRoot.transform.localRotation = Quaternion.identity;
        }

        void UpdateTravelDir()
        {
            if (_ball == null)
                return;

            Rigidbody body = _ball.GetComponent<Rigidbody>();
            if (body != null && body.linearVelocity.sqrMagnitude >= MinFlightSpeed * MinFlightSpeed)
            {
                _lastTravelDir = Flatten(body.linearVelocity);
                return;
            }

            if (_holeTarget != null)
            {
                Vector3 toward = Flatten(_holeTarget.position - _ball.transform.position);
                if (toward.sqrMagnitude > 0.0001f)
                    _lastTravelDir = toward;
            }
        }

        void UpdateCameraPose(bool instant)
        {
            if (_pipCamera == null || _ball == null)
                return;

            Vector3 ballPos = _ball.transform.position;
            Vector3 toward = _lastTravelDir;
            if (_holeTarget != null)
            {
                Vector3 toHole = Flatten(_holeTarget.position - ballPos);
                if (toHole.sqrMagnitude > 0.0001f)
                    toward = toHole;
            }

            Vector3 desired = ballPos - toward * BackDistance + Vector3.up * HeightOffset;
            Transform camT = _pipCamera.transform;
            if (instant)
                camT.position = desired;
            else
                camT.position = Vector3.Lerp(camT.position, desired, 1f - Mathf.Exp(-10f * Time.deltaTime));

            Vector3 lookAt = ballPos + Vector3.up * 0.15f;
            Vector3 look = lookAt - camT.position;
            if (look.sqrMagnitude > 0.0001f)
                camT.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
        }

        static GameObject CreateUiChild(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        static Vector3 Flatten(Vector3 v)
        {
            v.y = 0f;
            if (v.sqrMagnitude < 0.0001f)
                return Vector3.forward;
            return v.normalized;
        }
    }
}
