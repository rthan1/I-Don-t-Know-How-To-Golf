using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// A red GIVE UP button. Point at it (or touch it) and squeeze grip.
// The first press asks "SURE?". A second press within a few seconds gives up the hole.
public class GiveUpButton : MonoBehaviour
{
    [Tooltip("Seconds the player has to press again to confirm.")]
    [Min(0.5f)] public float confirmWindow = 3f;

    private static readonly Color IdleColor = new Color(0.75f, 0.1f, 0.1f);
    private static readonly Color HoverColor = new Color(1f, 0.3f, 0.3f);
    private static readonly Color ConfirmColor = new Color(1f, 0.75f, 0f);

    private Renderer plate;
    private TextMeshPro label;
    private XRSimpleInteractable interactable;
    private float confirmUntil = -1f;
    private bool hovered;

    // Builds the button as a child of the given object (the stroke board).
    public static GiveUpButton Create(Transform parent, Vector3 localPosition)
    {
        Vector3 size = new Vector3(0.24f, 0.08f, 0.03f);

        var root = new GameObject("GiveUpButton");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;
        root.AddComponent<BoxCollider>().size = size;

        // Red block. Its own collider is removed so only the root's collider counts for pressing.
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        DestroyImmediate(block.GetComponent<Collider>());
        block.transform.SetParent(root.transform, false);
        block.transform.localScale = size;

        // Label on the side facing the player.
        var labelObject = new GameObject("Label");
        labelObject.transform.SetParent(root.transform, false);
        labelObject.transform.localPosition = new Vector3(0f, 0f, -size.z * 0.5f - 0.002f);
        TextMeshPro text = labelObject.AddComponent<TextMeshPro>();
        text.fontSize = 0.3f;
        text.alignment = TextAlignmentOptions.Center;
        text.rectTransform.sizeDelta = new Vector2(size.x, size.y);
        text.color = Color.white;

        GiveUpButton button = root.AddComponent<GiveUpButton>();
        button.plate = block.GetComponent<Renderer>();
        button.label = text;
        // Added last so it picks up the root's collider when it starts.
        button.interactable = root.AddComponent<XRSimpleInteractable>();
        return button;
    }

    private void Start()
    {
        interactable.selectEntered.AddListener(OnPressed);
        interactable.hoverEntered.AddListener(OnHoverEntered);
        interactable.hoverExited.AddListener(OnHoverExited);
        Refresh();
    }

    private void OnDestroy()
    {
        if (interactable != null)
        {
            interactable.selectEntered.RemoveListener(OnPressed);
            interactable.hoverEntered.RemoveListener(OnHoverEntered);
            interactable.hoverExited.RemoveListener(OnHoverExited);
        }
    }

    private void Update()
    {
        // Confirm window ran out: back to the normal label.
        if (confirmUntil > 0f && Time.time > confirmUntil)
        {
            confirmUntil = -1f;
            Refresh();
        }
    }

    private void OnPressed(SelectEnterEventArgs args)
    {
        StrokeManager manager = StrokeManager.Instance;
        if (manager == null || manager.HoleFinished) return;

        if (confirmUntil > 0f && Time.time <= confirmUntil)
        {
            confirmUntil = -1f;
            manager.GiveUp();
        }
        else
        {
            confirmUntil = Time.time + confirmWindow;
        }

        Refresh();
    }

    private void OnHoverEntered(HoverEnterEventArgs args)
    {
        hovered = true;
        Refresh();
    }

    private void OnHoverExited(HoverExitEventArgs args)
    {
        hovered = false;
        Refresh();
    }

    private void Refresh()
    {
        bool confirming = confirmUntil > 0f;
        label.text = confirming ? "SURE?\nPRESS AGAIN" : "GIVE UP";
        plate.material.color = confirming ? ConfirmColor : hovered ? HoverColor : IdleColor;
    }
}
