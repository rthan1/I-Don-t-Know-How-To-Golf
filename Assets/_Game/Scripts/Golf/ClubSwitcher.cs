using Golf.Throwing;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Goes on the club's root object. Cycles through the clubs (Driver, Iron, Putter) when the player
// presses the secondary button (B on the right controller, Y on the left) on the hand holding the club.
// A swap changes the numbers on the ClubHead, moves the head to the new shaft length, and swaps the model.
// The shaft runs down this object's local -Y axis, with the grip at this object's origin.
public class ClubSwitcher : MonoBehaviour
{
    [Header("Clubs")]
    [Tooltip("Clubs to cycle through, in order.")]
    public ClubData[] clubs;

    [Tooltip("Which club in the list above the player starts with (0 = first).")]
    [Min(0)] public int startIndex = 0;

    [Header("Parts")]
    [Tooltip("The ClubHead child. Found automatically if left empty.")]
    public ClubHead clubHead;

    [Tooltip("Placeholder shaft, stretched to each club's shaft length. Hidden when a club has its own Visual Prefab.")]
    public Transform grayBoxShaft;

    [Tooltip("Placeholder head block. Hidden when a club has its own Visual Prefab.")]
    public Transform grayBoxHead;

    [Tooltip("Where a club's Visual Prefab gets spawned. Uses this object if left empty.")]
    public Transform visualRoot;

    [Header("Input")]
    [Tooltip("Cycles clubs while holding. Quest B/Y, or M in the simulator.")]
    public InputAction switchClubButton = new InputAction("Switch Club", InputActionType.Button);

    [Header("Events")]
    [Tooltip("Fires every time the club changes.")]
    public UnityEvent onClubChanged;

    public int CurrentIndex { get; private set; }
    public ClubData CurrentClub => cueMode ? Cue : (clubs != null && clubs.Length > 0 ? clubs[CurrentIndex] : null);

    /// <summary>True while the pool table is up: the club is a pool cue and can't be switched.</summary>
    public bool CueMode => cueMode;

    private bool cueMode;
    private ClubData cue;

    private XRGrabInteractable grab;
    private GameObject spawnedVisual;
    private bool blockedByBallHold;

    private void Awake()
    {
        grab = GetComponent<XRGrabInteractable>();

        if (clubHead == null)
        {
            clubHead = GetComponentInChildren<ClubHead>();
        }

        if (GetComponent<DesktopClubGrabInput>() == null)
            gameObject.AddComponent<DesktopClubGrabInput>();

        if (GetComponent<ClubEquipGrab>() == null)
            gameObject.AddComponent<ClubEquipGrab>();

        if (GetComponent<ClubStanceLock>() == null)
            gameObject.AddComponent<ClubStanceLock>();
    }

    // Start (not Awake) so the ClubHead has finished its own setup first.
    private void Start()
    {
        SetClub(startIndex);
    }

    private void Update() => SyncGrabBlockedByBallHold();

    private void OnEnable()
    {
        EnsureSwitchBindings();
        switchClubButton.performed += OnSwitchPressed;
        switchClubButton.Enable();
    }

    private void OnDisable()
    {
        switchClubButton.performed -= OnSwitchPressed;
        switchClubButton.Disable();

        if (blockedByBallHold && grab != null)
        {
            blockedByBallHold = false;
            grab.enabled = true;
        }
    }

    void EnsureSwitchBindings()
    {
        if (switchClubButton.bindings.Count > 0)
            return;

        // Quest: B (right) / Y (left). Simulator Device Controls map secondary → M.
        switchClubButton.AddBinding("<XRController>{RightHand}/secondaryButton");
        switchClubButton.AddBinding("<XRController>{LeftHand}/secondaryButton");
        // Direct keyboard fallback so simulator club-switch works even if XR secondary isn't wired.
        switchClubButton.AddBinding("<Keyboard>/m");
    }

    void SyncGrabBlockedByBallHold()
    {
        if (grab == null)
            return;

        bool block = IsAnyBallHeldOrThrowing();
        if (block)
        {
            if (!blockedByBallHold)
                blockedByBallHold = true;

            if (grab.enabled)
                grab.enabled = false;
            return;
        }

        if (!blockedByBallHold)
            return;

        blockedByBallHold = false;
        grab.enabled = true;
    }

    public static bool IsAnyBallHeldOrThrowing()
    {
        var sequences = Object.FindObjectsByType<ThrowSequenceController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < sequences.Length; i++)
        {
            ThrowSequenceController sequence = sequences[i];
            if (sequence == null)
                continue;

            if (sequence.IsActive)
                return true;

            var ballGrab = sequence.GetComponent<XRGrabInteractable>();
            if (ballGrab != null && ballGrab.isSelected)
                return true;
        }

        return false;
    }

    void OnSwitchPressed(InputAction.CallbackContext context)
    {
        // Any switch input while holding the club cycles it (no handedness gate —
        // simulator interactors often report None and were blocking M / secondary).
        if (grab == null || !grab.isSelected)
            return;

        NextClub();
    }

    public void NextClub()
    {
        if (clubs == null || clubs.Length == 0) return;
        if (cueMode) return; // it's a pool cue until the table goes away

        SetClub((CurrentIndex + 1) % clubs.Length);
    }

    /// <summary>Turns the club into a pool cue (on) or back into the selected golf club (off).</summary>
    public void SetCueMode(bool on)
    {
        if (on == cueMode) return;
        cueMode = on;
        SetClub(CurrentIndex);
    }

    public void SetClub(int index)
    {
        if (clubs == null || clubs.Length == 0)
        {
            Debug.LogWarning($"{name}: ClubSwitcher has no clubs in its list.", this);
            return;
        }

        CurrentIndex = Mathf.Clamp(index, 0, clubs.Length - 1);
        ClubData club = cueMode ? Cue : clubs[CurrentIndex];
        if (club == null)
        {
            Debug.LogWarning($"{name}: club slot {CurrentIndex} is empty.", this);
            return;
        }

        ApplyShape(club);
        ApplyVisual(club);

        if (clubHead != null)
        {
            clubHead.SetClub(club);
            clubHead.CacheHeadOffset(); // the head just moved, so re-measure it
        }

        onClubChanged?.Invoke();
        Debug.Log($"Club: {club.clubName}", this);
    }

    // The pool cue's numbers, made once in code (it only exists while the pool table is up).
    // A long thin stick with a small tip, no loft or lift, gentle power suited to a table.
    private ClubData Cue
    {
        get
        {
            if (cue != null) return cue;

            cue = ScriptableObject.CreateInstance<ClubData>();
            cue.name = "Cue";
            cue.clubName = "Cue";
            cue.isCue = true;
            cue.shaftLength = 1.45f;
            cue.faceWidth = 0.024f;     // the tip
            cue.faceThickness = 0.012f;
            cue.loftDegrees = 0f;
            cue.liftStrength = 0f;
            cue.speedCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(4f, 6f)); // stroke speed -> ball speed
            cue.minSwingSpeed = 0.25f;
            cue.maxBallSpeed = 7f;
            cue.faceAimInfluence = 0f;
            cue.edgePower = 1f;
            cue.edgeCurveDegrees = 0f;
            cue.hapticAmplitude = 0.35f;
            cue.hapticDuration = 0.04f;
            cue.hitVolume = 0.6f;

            // Borrow the putter's soft "tock" for the cue tip.
            if (clubs != null)
                foreach (ClubData c in clubs)
                    if (c != null && c.clubName == "Putter") cue.hitSound = c.hitSound;

            return cue;
        }
    }

    // Puts the head at the end of the shaft and sizes the gray-box placeholder to match.
    private void ApplyShape(ClubData club)
    {
        float length = club.shaftLength;
        Vector3 headPosition = new Vector3(0f, -length, 0f);

        if (clubHead != null)
        {
            clubHead.transform.localPosition = headPosition;
        }

        if (grayBoxShaft != null)
        {
            // Unity's cylinder is 2 m tall, so half the length goes in the Y scale.
            grayBoxShaft.localPosition = new Vector3(0f, -length / 2f, 0f);
            grayBoxShaft.localScale = new Vector3(0.015f, length / 2f, 0.015f);
        }

        if (grayBoxHead != null)
        {
            // Same size as the hit shape: face width heel to toe (X), face thickness up/down and front/back.
            float thickness = club.faceThickness * 2f;
            grayBoxHead.localPosition = headPosition;
            grayBoxHead.localScale = new Vector3(club.faceWidth, thickness, thickness);
        }
    }

    // Shows the club's own model if it has one, otherwise the gray box.
    private void ApplyVisual(ClubData club)
    {
        if (spawnedVisual != null)
        {
            Destroy(spawnedVisual);
            spawnedVisual = null;
        }

        bool hasModel = club.visualPrefab != null;
        if (grayBoxShaft != null) grayBoxShaft.gameObject.SetActive(!hasModel);
        if (grayBoxHead != null) grayBoxHead.gameObject.SetActive(!hasModel);

        if (hasModel)
        {
            Transform parent = visualRoot != null ? visualRoot : transform;
            spawnedVisual = Instantiate(club.visualPrefab, parent);

            // Models are for looks only. A collider here would become part of the club's physics body.
            foreach (Collider modelCollider in spawnedVisual.GetComponentsInChildren<Collider>())
            {
                Destroy(modelCollider);
            }
        }
    }
}
