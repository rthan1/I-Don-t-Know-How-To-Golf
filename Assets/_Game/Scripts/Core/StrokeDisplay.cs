using TMPro;
using UnityEngine;

// Shows the StrokeManager's stroke list on a board floating to the player's left.
// It follows the player around (and turns when the player rig turns) but not their head,
// so it stays out of the way until they look over at it.
// StrokeManager adds this automatically.
[RequireComponent(typeof(StrokeManager))]
public class StrokeDisplay : MonoBehaviour
{
    [Tooltip("How far from the player's head the board floats, in meters.")]
    [Min(0.3f)] public float distance = 0.9f;

    [Tooltip("How far left of the direction the player is facing the board sits, in degrees. Look this way to read it.")]
    [Range(0f, 120f)] public float angleLeft = 55f;

    [Tooltip("Height of the board compared to the player's eyes, in meters.")]
    public float heightOffset = -0.1f;

    [Tooltip("How quickly the board catches up when the player moves. Higher = snappier.")]
    [Min(0.5f)] public float followSpeed = 6f;

    [Tooltip("Text size. Raise it if it's hard to read in the headset.")]
    public float fontSize = 0.3f;

    private StrokeManager manager;
    private TextMeshPro text;
    private Camera playerCamera;
    private Transform rig;
    private GiveUpButton giveUpButton;

    private void Start()
    {
        manager = GetComponent<StrokeManager>();
        playerCamera = Camera.main;
        rig = playerCamera != null ? playerCamera.transform.root : null;

        var textObject = new GameObject("StrokeDisplayText");
        text = textObject.AddComponent<TextMeshPro>();
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.rectTransform.sizeDelta = new Vector2(0.5f, 0.5f);

        // Dark outline so the text stays readable against the sky and grass.
        text.outlineWidth = 0.2f;
        text.outlineColor = Color.black;

        // GIVE UP button just under the stroke list. It moves with the board.
        giveUpButton = GiveUpButton.Create(text.transform, new Vector3(-0.13f, -0.3f, 0f));

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

        if (playerCamera == null)
        {
            playerCamera = Camera.main;
            if (playerCamera == null) return;
            rig = playerCamera.transform.root;
        }

        Transform head = playerCamera.transform;

        // "Facing" comes from the player rig, not the head, so looking around doesn't move the board.
        // The rig gets turned toward the hole each time the player is placed at the ball.
        Vector3 facing = Vector3.ProjectOnPlane(rig != null ? rig.forward : head.forward, Vector3.up);
        if (facing.sqrMagnitude < 0.0001f) facing = Vector3.forward;
        facing.Normalize();

        Vector3 toBoard = Quaternion.AngleAxis(-angleLeft, Vector3.up) * facing;
        Vector3 target = head.position + toBoard * distance + Vector3.up * heightOffset;

        // Glide after the player. Jump straight there after a teleport (more than 2 m away).
        Vector3 current = text.transform.position;
        if ((target - current).sqrMagnitude > 4f)
        {
            text.transform.position = target;
        }
        else
        {
            float blend = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
            text.transform.position = Vector3.Lerp(current, target, blend);
        }

        // Turn to face the player.
        Vector3 away = text.transform.position - head.position;
        if (away.sqrMagnitude > 0.0001f)
        {
            text.transform.rotation = Quaternion.LookRotation(away, Vector3.up);
        }
    }

    private void Refresh()
    {
        if (text != null)
        {
            text.text = manager.GetSummaryText();
        }

        // No giving up once the hole is over.
        if (giveUpButton != null)
        {
            giveUpButton.gameObject.SetActive(!manager.HoleFinished);
        }
    }
}
