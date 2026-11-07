using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;

// Goes on the club head (a child of the club). Measures how fast the head is moving,
// detects when it hits the ball, and launches the ball using the current ClubData.
// The head never pushes the ball physically. It only detects the hit and calls GolfBall.Launch().
[RequireComponent(typeof(SphereCollider))]
public class ClubHead : MonoBehaviour
{
    public enum DetectionMode { TriggerOnly, SweepOnly, Both }

    [Header("Club")]
    [Tooltip("Numbers for the club currently in hand. ClubSwitcher changes this.")]
    [SerializeField] private ClubData clubData;

    [Header("Detection")]
    [Tooltip("Trigger = the head overlaps the ball. Sweep = checks the path the head traveled, which catches fast swings the trigger misses. Both = whichever catches it first.")]
    public DetectionMode detectionMode = DetectionMode.Both;

    [Tooltip("Layers the sweep looks for the ball on.")]
    public LayerMask ballLayers;

    [Tooltip("Seconds after a hit before the club can hit again.")]
    [Min(0f)] public float hitCooldown = 0.5f;

    [Tooltip("Only hit while someone is holding the club. Stops a dropped club from hitting the ball.")]
    public bool requireHeld = true;

    [Tooltip("Which way the club face points, in the club head's local space. Only matters if the club's Face Aim Influence is above 0.")]
    public Vector3 localFaceDirection = Vector3.forward;

    [Tooltip("How many physics steps of movement to average for the swing speed. Smooths out tracking jitter.")]
    [Range(1, 6)] public int speedSamples = 3;

    [Header("Events")]
    [Tooltip("Fires on every successful legit hit. Hook the stroke counter up to this.")]
    public UnityEvent onLegitSwing;

    [Header("Debug")]
    [Tooltip("Print each hit to the Console.")]
    public bool logHits = true;

    // Read-only info for other scripts and the debug readout.
    public ClubData CurrentClub => clubData;
    public float CurrentSpeed { get; private set; }
    public float LastSwingSpeed { get; private set; }
    public float LastBallSpeed { get; private set; }
    public string LastDetector { get; private set; } = "-";
    public bool IsHeld => holder != null;

    private SphereCollider headTrigger;
    private Rigidbody clubBody;
    private XRGrabInteractable grab;
    private IXRSelectInteractor holder;

    // Where the head sits relative to the club's Rigidbody. Worked out once in CacheHeadOffset().
    private Vector3 headOffsetLocal;
    private Quaternion headRotationLocal = Quaternion.identity;

    private Vector3 lastHeadPosition;
    private bool hasLastPosition;
    private readonly Vector3[] velocitySamples = new Vector3[6];
    private int sampleIndex;
    private int sampleCount;
    private Vector3 smoothedVelocity;
    private float lastHitTime = -999f;

    // Runs when the component is first added in the Editor: pre-fills the Ball layer.
    private void Reset()
    {
        ballLayers = LayerMask.GetMask("Ball");
    }

    private void Awake()
    {
        headTrigger = GetComponent<SphereCollider>();
        headTrigger.isTrigger = true;
        clubBody = GetComponentInParent<Rigidbody>();
        grab = GetComponentInParent<XRGrabInteractable>();

        if (ballLayers.value == 0)
        {
            ballLayers = LayerMask.GetMask("Ball");
        }

        if (gameObject.layer != LayerMask.NameToLayer("ClubHead"))
        {
            Debug.LogWarning($"{name}: ClubHead should be on the 'ClubHead' layer.", this);
        }

        if (grab == null && requireHeld)
        {
            Debug.LogWarning($"{name}: no XRGrabInteractable found above the club head, so 'Require Held' will block every hit.", this);
        }

        CacheHeadOffset();
        ApplyClubData();
    }

    private void OnEnable()
    {
        if (grab != null)
        {
            grab.selectEntered.AddListener(OnGrabbed);
            grab.selectExited.AddListener(OnReleased);
        }
    }

    private void OnDisable()
    {
        if (grab != null)
        {
            grab.selectEntered.RemoveListener(OnGrabbed);
            grab.selectExited.RemoveListener(OnReleased);
        }

        holder = null;
    }

    // Swaps to a different club's numbers. ClubSwitcher calls this.
    public void SetClub(ClubData newClub)
    {
        clubData = newClub;
        ApplyClubData();
    }

    // Re-measures where the head sits on the club. Call this if the head gets moved
    // (ClubSwitcher does when it changes shaft length).
    public void CacheHeadOffset()
    {
        if (clubBody != null)
        {
            Transform body = clubBody.transform;
            headOffsetLocal = Quaternion.Inverse(body.rotation) * (HeadCenterWorld() - body.position);
            headRotationLocal = Quaternion.Inverse(body.rotation) * transform.rotation;
        }

        ResetSpeedTracking();
    }

    private void FixedUpdate()
    {
        Vector3 headPosition = GetHeadPosition();

        if (!hasLastPosition)
        {
            lastHeadPosition = headPosition;
            hasLastPosition = true;
            return;
        }

        Vector3 velocity = (headPosition - lastHeadPosition) / Time.fixedDeltaTime;
        AddSpeedSample(velocity);

        if (detectionMode != DetectionMode.TriggerOnly)
        {
            SweepForBall(lastHeadPosition, headPosition);
        }

        lastHeadPosition = headPosition;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (detectionMode == DetectionMode.SweepOnly)
        {
            return;
        }

        TryHit(other, "Trigger");
    }

    // Casts a sphere the size of the head along the path it just traveled.
    // Catches the ball even if the head skipped past it between physics steps.
    private void SweepForBall(Vector3 from, Vector3 to)
    {
        Vector3 path = to - from;
        float distance = path.magnitude;
        if (distance < 0.0001f)
        {
            return;
        }

        if (Physics.SphereCast(from, WorldHeadRadius(), path / distance, out RaycastHit hit,
                distance, ballLayers, QueryTriggerInteraction.Ignore))
        {
            TryHit(hit.collider, "Sweep");
        }
    }

    // Checks that this contact should count, then launches the ball.
    private void TryHit(Collider other, string detector)
    {
        GolfBall ball = other.attachedRigidbody != null
            ? other.attachedRigidbody.GetComponent<GolfBall>()
            : other.GetComponent<GolfBall>();

        if (ball == null || clubData == null) return;
        if (requireHeld && !IsHeld) return;
        if (Time.time < lastHitTime + hitCooldown) return;
        if (!ball.IsAtRest) return;

        float swingSpeed = CurrentSpeed;
        float ballSpeed = clubData.GetBallSpeed(swingSpeed);
        if (ballSpeed <= 0f) return; // too slow to count

        Vector3 direction = GetLaunchDirection(smoothedVelocity);
        if (!ball.Launch(direction * ballSpeed, clubData.liftStrength)) return;

        lastHitTime = Time.time;
        LastSwingSpeed = swingSpeed;
        LastBallSpeed = ballSpeed;
        LastDetector = detector;

        PlayHaptic();
        onLegitSwing?.Invoke();

        if (logHits)
        {
            Debug.Log($"[{clubData.clubName}] {detector} hit: swing {swingSpeed:F1} m/s -> ball {ballSpeed:F1} m/s", this);
        }
    }

    // Works out which way the ball should fly.
    private Vector3 GetLaunchDirection(Vector3 clubVelocity)
    {
        // 1. Swing path, flattened so up/down motion in the swing doesn't change the aim. Loft handles height.
        Vector3 pathDirection = Vector3.ProjectOnPlane(clubVelocity, Vector3.up);

        // 2. Which way the face points, also flattened.
        Vector3 faceDirection = Vector3.ProjectOnPlane(GetHeadRotation() * localFaceDirection, Vector3.up);

        // Straight up/down swing with no sideways motion: fall back to the face, then to world forward.
        if (pathDirection.sqrMagnitude < 0.0001f) pathDirection = faceDirection;
        if (pathDirection.sqrMagnitude < 0.0001f) pathDirection = Vector3.forward;
        pathDirection.Normalize();

        Vector3 aim = pathDirection;
        if (clubData.faceAimInfluence > 0f && faceDirection.sqrMagnitude > 0.0001f)
        {
            faceDirection.Normalize();

            // Flip the face if it points backward relative to the swing, so model orientation doesn't matter.
            if (Vector3.Dot(faceDirection, pathDirection) < 0f)
            {
                faceDirection = -faceDirection;
            }

            aim = Vector3.Slerp(pathDirection, faceDirection, clubData.faceAimInfluence).normalized;
        }

        // 3. Tilt up by the club's loft angle.
        float loft = clubData.loftDegrees * Mathf.Deg2Rad;
        return (aim * Mathf.Cos(loft) + Vector3.up * Mathf.Sin(loft)).normalized;
    }

    // Buzzes the controller of the hand holding the club.
    private void PlayHaptic()
    {
        if (holder == null) return;

        HapticImpulsePlayer haptics = holder.transform.GetComponentInParent<HapticImpulsePlayer>();
        if (haptics != null)
        {
            haptics.SendHapticImpulse(clubData.hapticAmplitude, clubData.hapticDuration);
        }
    }

    private void OnGrabbed(SelectEnterEventArgs args)
    {
        holder = args.interactorObject;

        // The club snaps to the hand on grab. Without this reset, that snap would look like a huge swing.
        ResetSpeedTracking();
    }

    private void OnReleased(SelectExitEventArgs args)
    {
        if (args.interactorObject == holder)
        {
            holder = null;
        }
    }

    // Keeps the head trigger's size in meters, matching the club data.
    private void ApplyClubData()
    {
        if (clubData == null || headTrigger == null) return;

        headTrigger.radius = clubData.headRadius / MaxScale(transform.lossyScale);
    }

    // Records one physics step of head movement and updates the averaged swing speed.
    private void AddSpeedSample(Vector3 velocity)
    {
        velocitySamples[sampleIndex] = velocity;
        sampleIndex = (sampleIndex + 1) % velocitySamples.Length;
        sampleCount = Mathf.Min(sampleCount + 1, velocitySamples.Length);

        int count = Mathf.Min(sampleCount, speedSamples);
        Vector3 sum = Vector3.zero;
        for (int i = 0; i < count; i++)
        {
            int index = (sampleIndex - 1 - i + velocitySamples.Length) % velocitySamples.Length;
            sum += velocitySamples[index];
        }

        smoothedVelocity = sum / count;
        CurrentSpeed = smoothedVelocity.magnitude;
    }

    private void ResetSpeedTracking()
    {
        hasLastPosition = false;
        sampleCount = 0;
        smoothedVelocity = Vector3.zero;
        CurrentSpeed = 0f;
    }

    // Head position and rotation, read from the club's physics body so they match what the physics engine sees.
    private Vector3 GetHeadPosition()
    {
        return clubBody != null ? clubBody.position + clubBody.rotation * headOffsetLocal : HeadCenterWorld();
    }

    private Quaternion GetHeadRotation()
    {
        return clubBody != null ? clubBody.rotation * headRotationLocal : transform.rotation;
    }

    private Vector3 HeadCenterWorld()
    {
        return transform.TransformPoint(headTrigger.center);
    }

    private float WorldHeadRadius()
    {
        return headTrigger.radius * MaxScale(transform.lossyScale);
    }

    private static float MaxScale(Vector3 scale)
    {
        return Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
    }

    // Draws the head's hit sphere (yellow) and face direction (cyan) in the Scene view when selected.
    private void OnDrawGizmosSelected()
    {
        SphereCollider sphere = GetComponent<SphereCollider>();
        if (sphere == null) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.TransformPoint(sphere.center), sphere.radius * MaxScale(transform.lossyScale));
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(transform.position, transform.rotation * localFaceDirection * 0.2f);
    }
}
