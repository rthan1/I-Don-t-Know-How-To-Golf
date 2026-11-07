using TMPro;
using UnityEngine;

// Shows the StrokeManager's stroke list as small text floating just above the player's left controller.
// The text always turns to face the player's head, so raising your left hand is enough to read it.
// StrokeManager adds this automatically.
[RequireComponent(typeof(StrokeManager))]
public class StrokeDisplay : MonoBehaviour
{
    [Tooltip("Where the text sits relative to the left controller, in meters.")]
    public Vector3 offset = new Vector3(0f, 0.08f, 0f);

    [Tooltip("Text size. Raise it if it's hard to read in the headset.")]
    public float fontSize = 0.12f;

    private StrokeManager manager;
    private TextMeshPro text;
    private Transform anchor;
    private Camera playerCamera;

    private void Start()
    {
        manager = GetComponent<StrokeManager>();
        playerCamera = Camera.main;
        anchor = FindLeftController();

        var textObject = new GameObject("StrokeDisplayText");
        text = textObject.AddComponent<TextMeshPro>();
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Bottom;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.rectTransform.sizeDelta = new Vector2(0.3f, 0.2f);

        manager.Changed += Refresh;
        Refresh();
    }

    private void OnDestroy()
    {
        if (manager != null)
        {
            manager.Changed -= Refresh;
        }

        if (text != null)
        {
            Destroy(text.gameObject);
        }
    }

    private void LateUpdate()
    {
        if (text == null) return;

        // Follow the wrist, or sit low in front of the player if there's no left controller.
        if (anchor != null)
        {
            text.transform.position = anchor.TransformPoint(offset);
        }
        else if (playerCamera != null)
        {
            text.transform.position = playerCamera.transform.TransformPoint(new Vector3(0f, -0.3f, 0.7f));
        }

        // Turn to face the player.
        if (playerCamera != null)
        {
            Vector3 away = text.transform.position - playerCamera.transform.position;
            if (away.sqrMagnitude > 0.0001f)
            {
                text.transform.rotation = Quaternion.LookRotation(away, Vector3.up);
            }
        }
    }

    private void Refresh()
    {
        if (text != null)
        {
            text.text = manager.GetSummaryText();
        }
    }

    // The starter XR rig names the left hand "Left Controller".
    private static Transform FindLeftController()
    {
        foreach (Transform t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (t.name == "Left Controller")
            {
                return t;
            }
        }

        return null;
    }
}
