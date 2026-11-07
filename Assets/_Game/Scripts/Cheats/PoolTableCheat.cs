using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// The pool table cheat: press a button and the hole's green turns into a perfectly flat pool table.
/// It only works while you're standing on the green.
///
/// Default buttons: A on the right Quest controller, or P on the keyboard (for testing in the editor).
/// Change them in the Inspector under "Cheat Button".
///
/// Put this on any active object in a scene that has a GreenSwap (Hole 3's builder adds it to the "Green" object).
/// </summary>
public class PoolTableCheat : MonoBehaviour
{
    [Tooltip("The green to swap. Found automatically if left empty.")]
    public GreenSwap green;

    [Tooltip("Button(s) that trigger the cheat. If none are set, it uses A on the right controller and P on the keyboard.")]
    public InputAction cheatButton = new InputAction("PoolTableCheat", InputActionType.Button);

    [Tooltip("Pressing the button again switches back to the normal green (handy while testing).")]
    public bool pressAgainToUndo = true;

    [Tooltip("Runs when the pool table turns on. Hook up the security cameras / stroke penalty here.")]
    public UnityEvent onCheatUsed;

    [Header("Only on the green")]
    [Tooltip("Center of the green (world position). Set by the hole builder.")]
    public Vector3 greenCenter;

    [Tooltip("Size of the green: half its width (x) and half its length (z), in meters. Set by the hole builder. " +
             "Leave at zero to allow the cheat from anywhere.")]
    public Vector2 greenRadius;

    [Tooltip("Extra room around the green's edge that still counts as being on it, in meters.")]
    public float edgeAllowance = 0.5f;

    [Tooltip("Runs when the button is pressed but the player isn't on the green (for a 'nope' sound, etc.).")]
    public UnityEvent onNotOnGreen;

    void Awake()
    {
        if (cheatButton.bindings.Count == 0)
        {
            cheatButton.AddBinding("<XRController>{RightHand}/primaryButton"); // A on the right Quest controller
            cheatButton.AddBinding("<Keyboard>/p");                            // for testing in the editor
        }
        if (green == null) green = FindFirstObjectByType<GreenSwap>();
    }

    void OnEnable()
    {
        cheatButton.performed += OnCheatPressed;
        cheatButton.Enable();
    }

    void OnDisable()
    {
        cheatButton.performed -= OnCheatPressed;
        cheatButton.Disable();
    }

    void OnCheatPressed(InputAction.CallbackContext context) => Activate();

    /// <summary>Turns the pool table on (or back off, if pressAgainToUndo is set). Can also be called from other scripts.</summary>
    public void Activate()
    {
        if (green == null) green = FindFirstObjectByType<GreenSwap>();
        if (green == null)
        {
            Debug.LogWarning("PoolTableCheat: no GreenSwap in this scene.");
            return;
        }

        if (green.IsPoolTable)
        {
            if (pressAgainToUndo) green.SetPoolTable(false);
            return;
        }

        if (!PlayerOnGreen())
        {
            onNotOnGreen?.Invoke();
            return;
        }

        green.SetPoolTable(true);
        onCheatUsed?.Invoke();
    }

    /// <summary>Is the player (their head, seen from above) standing on the green?</summary>
    public bool PlayerOnGreen()
    {
        if (greenRadius.x <= 0f || greenRadius.y <= 0f) return true; // no green size set: allow anywhere
        Camera head = Camera.main;
        if (head == null) return false;

        Vector3 p = head.transform.position;
        float dx = (p.x - greenCenter.x) / (greenRadius.x + edgeAllowance);
        float dz = (p.z - greenCenter.z) / (greenRadius.y + edgeAllowance);
        return dx * dx + dz * dz <= 1f;
    }
}
