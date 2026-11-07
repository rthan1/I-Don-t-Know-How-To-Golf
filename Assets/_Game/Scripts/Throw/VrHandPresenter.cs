using UnityEngine;
using UnityEngine.SceneManagement;

namespace Golf.Throwing
{
    /// <summary>
    /// Hides controller models and attaches Quest-oriented hand sprites to the controllers.
    /// Survives scene loads and rebinds to the new XR Origin controllers.
    /// </summary>
    public sealed class VrHandPresenter : MonoBehaviour
    {
        public static VrHandPresenter Instance { get; private set; }

        [Header("Hand sprites")]
        [SerializeField] Sprite openHandSprite;
        [SerializeField] Sprite grabHandSprite;

        SimpleHandVisual _left;
        SimpleHandVisual _right;
        bool _ready;

        public SimpleHandVisual RightHand => _right;
        public SimpleHandVisual LeftHand => _left;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            EnsurePresenterExists();
            if (Instance != null)
                Instance.RebindToCurrentControllers();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsurePresenterExists();
            if (Instance != null)
                Instance.RebindToCurrentControllers();
        }

        static void EnsurePresenterExists()
        {
            if (Instance != null)
                return;

            var existing = Object.FindFirstObjectByType<VrHandPresenter>(FindObjectsInactive.Include);
            if (existing != null)
            {
                Instance = existing;
                Object.DontDestroyOnLoad(existing.gameObject);
                return;
            }

            // Create even if controllers aren't ready yet; Update/rebind will attach later.
            var go = new GameObject("VrHandPresenter");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<VrHandPresenter>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            ForceResolveSprites();
        }

        void Start() => TrySetupHands();

        void Update()
        {
            if (!_ready)
                TrySetupHands();
            else
                KeepControllerVisualsHidden();
        }

        void RebindToCurrentControllers()
        {
            // Old hand sprites were parented under the previous scene's XR Origin.
            _left = null;
            _right = null;
            _ready = false;
            ForceResolveSprites();
            TrySetupHands();
        }

        void ForceResolveSprites()
        {
            if (openHandSprite == null)
                openHandSprite = LoadSprite("Hands/Hand_Open");
            if (grabHandSprite == null)
                grabHandSprite = LoadSprite("Hands/Hand_HoldBall") ?? LoadSprite("Hands/Hand_Grab");

#if UNITY_EDITOR
            if (openHandSprite == null)
            {
                openHandSprite = LoadFirstSprite(
                    "Assets/_Game/Resources/Hands/Hand_Open.png")
                    ?? LoadFirstSprite(
                        "Assets/Samples/XR Interaction Toolkit/3.6.1/XR Device Simulator/UI/Hands/Hand_Open.png");
            }

            if (grabHandSprite == null)
            {
                grabHandSprite = LoadFirstSprite("Assets/_Game/Resources/Hands/Hand_HoldBall.png")
                                 ?? LoadFirstSprite("Assets/_Game/Textures/Hands/Hand_HoldBall.png")
                                 ?? LoadFirstSprite(
                                     "Assets/Samples/XR Interaction Toolkit/3.6.1/XR Device Simulator/UI/Hands/Hand_Grab.png");
            }
#endif
        }

        static Sprite LoadSprite(string resourcesPath)
        {
            var sprite = Resources.Load<Sprite>(resourcesPath);
            if (sprite != null)
                return sprite;

            var all = Resources.LoadAll<Sprite>(resourcesPath);
            return all != null && all.Length > 0 ? all[0] : null;
        }

#if UNITY_EDITOR
        static Sprite LoadFirstSprite(string assetPath)
        {
            var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(assetPath);
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is Sprite sprite)
                    return sprite;
            }

            return null;
        }
#endif

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            var oldUi = GameObject.Find("HandUI");
            if (oldUi != null)
                Destroy(oldUi);
        }

        public void SetRightHandGrabbing(bool grabbing)
        {
            if (_right != null)
                _right.SetGrabbing(grabbing);
        }

        public void SetLeftHandVisible(bool visible)
        {
            if (!_ready)
                TrySetupHands();

            if (_left == null)
                return;

            _left.gameObject.SetActive(visible);
            if (visible)
                _left.SetGrabbing(false);
        }

        public Transform LeftControllerTransform
        {
            get
            {
                if (!_ready)
                    TrySetupHands();
                return _left != null ? _left.transform.parent : null;
            }
        }

        public void SetHandsVisible(bool visible)
        {
            if (!_ready)
                TrySetupHands();

            if (_left != null)
            {
                _left.gameObject.SetActive(visible);
                if (visible)
                    _left.SetGrabbing(false);
            }

            if (_right != null)
            {
                _right.gameObject.SetActive(visible);
                if (visible)
                    _right.SetGrabbing(false);
            }
        }

        void TrySetupHands()
        {
            if (openHandSprite == null || grabHandSprite == null)
            {
                ForceResolveSprites();
                if (openHandSprite == null || grabHandSprite == null)
                    return;
            }

            var leftController = FindTransform("Left Controller");
            var rightController = FindTransform("Right Controller");
            if (leftController == null || rightController == null)
                return;

            var oldUi = GameObject.Find("HandUI");
            if (oldUi != null)
                Destroy(oldUi);

            HideControllerVisualsUnder(leftController);
            HideControllerVisualsUnder(rightController);

            _left = EnsureHand(leftController, rightHand: false);
            _right = EnsureHand(rightController, rightHand: true);
            _ready = _left != null && _right != null;

            if (_ready)
                SetHandsVisible(true);
        }

        void KeepControllerVisualsHidden()
        {
            if (_left != null)
                HideControllerVisualsUnder(_left.transform.parent);
            if (_right != null)
                HideControllerVisualsUnder(_right.transform.parent);
        }

        SimpleHandVisual EnsureHand(Transform controller, bool rightHand)
        {
            var existing = controller.GetComponentInChildren<SimpleHandVisual>(true);
            if (existing != null)
            {
                existing.gameObject.SetActive(true);
                existing.Configure(openHandSprite, grabHandSprite, rightHand);
                return existing;
            }

            var handGo = new GameObject(rightHand ? "RightHandVisual" : "LeftHandVisual");
            handGo.transform.SetParent(controller, false);
            handGo.layer = controller.gameObject.layer;
            handGo.AddComponent<SpriteRenderer>();
            var hand = handGo.AddComponent<SimpleHandVisual>();
            hand.Configure(openHandSprite, grabHandSprite, rightHand);
            return hand;
        }

        static void HideControllerVisualsUnder(Transform controller)
        {
            if (controller == null)
                return;

            foreach (var t in controller.GetComponentsInChildren<Transform>(true))
            {
                if (t == controller)
                    continue;
                if (t.GetComponent<SimpleHandVisual>() != null)
                    continue;
                if (t.name.IndexOf("HandVisual", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                string n = t.name;
                bool isControllerModel =
                    n.IndexOf("Controller Visual", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("XR Controller", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("XRController", System.StringComparison.OrdinalIgnoreCase) >= 0;

                if (!isControllerModel)
                    continue;

                if (t.gameObject.activeSelf)
                    t.gameObject.SetActive(false);

                foreach (var renderer in t.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.GetComponent<SimpleHandVisual>() != null)
                        continue;
                    renderer.enabled = false;
                }
            }
        }

        static Transform FindTransform(string exactName)
        {
            var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == exactName)
                    return all[i];
            }

            return null;
        }
    }
}
