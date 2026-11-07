using UnityEngine;

namespace Golf.Throwing
{
    /// <summary>
    /// Hand sprite locked to the XR controller pose (Quest-style), not billboarded.
    /// Art is palm-facing with fingers "up" in the PNG; we orient that toward the
    /// player along the controller so Meta Quest 2 first-person view looks natural.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class SimpleHandVisual : MonoBehaviour
    {
        [SerializeField] Sprite openSprite;
        [SerializeField] Sprite grabSprite;
        [SerializeField] bool isRightHand = true;
        [SerializeField] float openWorldSize = 0.14f;
        [SerializeField] float grabWorldSize = 0.16f;

        // Palm faces the headset (sprite looks along -controller.forward), fingers
        // follow controller up, with a slight tilt so hands read clearly in Quest FP.
        static readonly Vector3 RightLocalEuler = new Vector3(-25f, 180f, 0f);
        static readonly Vector3 LeftLocalEuler = new Vector3(-25f, 180f, 0f);
        static readonly Vector3 RightLocalOffset = new Vector3(0.02f, -0.01f, 0.05f);
        static readonly Vector3 LeftLocalOffset = new Vector3(-0.02f, -0.01f, 0.05f);

        SpriteRenderer _renderer;
        bool _grabbing;
        static Material s_SpriteMaterial;

        public bool IsRightHand => isRightHand;

        public void Configure(Sprite open, Sprite grab, bool rightHand)
        {
            openSprite = open;
            grabSprite = grab;
            isRightHand = rightHand;

            EnsureRenderer();
            ApplyLocalPose();
            SetGrabbing(false);
        }

        public void SetGrabbing(bool grabbing)
        {
            _grabbing = grabbing;
            EnsureRenderer();
            ApplySpriteAndScale();
            ApplyLocalPose();
        }

        void LateUpdate()
        {
            // Keep local pose stable if something else moves the transform.
            ApplyLocalPose();
        }

        void ApplyLocalPose()
        {
            transform.localRotation = Quaternion.Euler(isRightHand ? RightLocalEuler : LeftLocalEuler);
            transform.localPosition = isRightHand ? RightLocalOffset : LeftLocalOffset;
        }

        void ApplySpriteAndScale()
        {
            if (_renderer == null)
                return;

            Sprite sprite = _grabbing && grabSprite != null ? grabSprite : openSprite;
            if (sprite != null)
                _renderer.sprite = sprite;

            // Source art is a right hand (palm toward viewer). Mirror for left.
            _renderer.flipX = !isRightHand;

            float worldSize = _grabbing ? grabWorldSize : openWorldSize;
            float ppu = sprite != null ? sprite.pixelsPerUnit : 100f;
            float height = sprite != null ? sprite.rect.height / ppu : 1f;
            float scale = height > 0.001f ? worldSize / height : worldSize;
            transform.localScale = Vector3.one * scale;
        }

        void EnsureRenderer()
        {
            if (_renderer == null)
                _renderer = GetComponent<SpriteRenderer>();

            _renderer.sortingOrder = 200;
            _renderer.sharedMaterial = GetSpriteMaterial();
            _renderer.enabled = true;
            ApplySpriteAndScale();
        }

        static Material GetSpriteMaterial()
        {
            if (s_SpriteMaterial != null)
                return s_SpriteMaterial;

            var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                         ?? Shader.Find("Sprites/Default")
                         ?? Shader.Find("Unlit/Transparent")
                         ?? Shader.Find("Universal Render Pipeline/Unlit");
            s_SpriteMaterial = new Material(shader);
            s_SpriteMaterial.name = "VrHandSpriteMaterial";
            return s_SpriteMaterial;
        }
    }
}
