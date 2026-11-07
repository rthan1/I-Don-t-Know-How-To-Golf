using UnityEngine;

namespace Golf.Throwing
{
    /// <summary>
    /// Hand sprite parented to an XR controller, always facing the camera.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class SimpleHandVisual : MonoBehaviour
    {
        [SerializeField] Sprite openSprite;
        [SerializeField] Sprite grabSprite;
        [SerializeField] bool isRightHand = true;
        [SerializeField] float openWorldSize = 0.14f;
        [SerializeField] float grabWorldSize = 0.16f;
        [SerializeField] Vector3 localOffset = new Vector3(0f, -0.02f, 0.05f);

        SpriteRenderer _renderer;
        bool _grabbing;
        static Material s_SpriteMaterial;

        public bool IsRightHand => isRightHand;

        public void Configure(Sprite open, Sprite grab, bool rightHand)
        {
            openSprite = open;
            grabSprite = grab;
            isRightHand = rightHand;

            float inward = rightHand ? -0.045f : 0.045f;
            localOffset = new Vector3(inward, -0.025f, 0.06f);

            EnsureRenderer();
            SetGrabbing(false);
            FaceCamera();
        }

        public void SetGrabbing(bool grabbing)
        {
            _grabbing = grabbing;
            EnsureRenderer();
            ApplySpriteAndScale();
        }

        void LateUpdate()
        {
            FaceCamera();
        }

        void FaceCamera()
        {
            Camera cam = Camera.main;
            if (cam == null)
                return;

            Vector3 toCam = cam.transform.position - transform.position;
            if (toCam.sqrMagnitude < 0.0001f)
                return;

            transform.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
            transform.localPosition = localOffset;
        }

        void ApplySpriteAndScale()
        {
            if (_renderer == null)
                return;

            Sprite sprite = _grabbing && grabSprite != null ? grabSprite : openSprite;
            if (sprite != null)
                _renderer.sprite = sprite;

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
