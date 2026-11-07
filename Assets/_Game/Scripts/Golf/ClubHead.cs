using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;

// Goes on the club head (a child of the club). Every physics step it slides the club face along the path
// the head just traveled, in small slices, and checks for the ball. On a good contact it launches the ball
// using the current ClubData, scaled by how close to the middle of the face the ball was hit.
// The head never pushes the ball physically. It only detects the hit and calls GolfBall.Launch().
// Face layout in this object's local space: heel to toe runs along X, and the face points along Local Face Direction.
public class ClubHead : MonoBehaviour
{
    [Header("Club")]
    [Tooltip("Numbers for the club currently in hand. ClubSwitcher changes this.")]
    [SerializeField] private ClubData clubData;

    [Header("Detection")]
    [Tooltip("Layers to look for the ball on.")]
    public LayerMask ballLayers;

    [Tooltip("Longest gap (m) between checks along the swing path. Smaller = more precise.")]
    [Min(0.005f)] public float maxSliceDistance = 0.03f;

    [Tooltip("Seconds after a hit before the club can hit again.")]
    [Min(0f)] public float hitCooldown = 0.5f;

    [Tooltip("Only hit while someone is holding the club. Stops a dropped club from hitting the ball.")]
    public bool requireHeld = true;

    [Tooltip("Which way the club face points, in the club head's local space. Only matters if the club's Face Aim Influence is above 0.")]
    public Vector3 localFaceDirection = Vector3.forward;

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
    public float LastSweetSpot { get; private set; } // 1 = dead center, 0 = very edge
    public bool IsHeld => holder != null;

    private Rigidbody clubBody;
    private XRGrabInteractable grab;
    private IXRSelectInteractor holder;

    // Where the head sits relative to the club's Rigidbody. Worked out in CacheHeadOffset().
    private Vector3 headOffsetLocal;
    private Quaternion headRotationLocal = Quaternion.identity;

    private Vector3 lastHeadPosition;
    private Quaternion lastHeadRotation;
    private bool hasLastPose;
    private bool wasTouchingBall;
    private float lastHitTime = -999f;
    private readonly Collider[] overlapResults = new Collider[4];

    // Runs when the component is first added in the Editor: pre-fills the Ball layer.
    private void Reset()
    {
        ballLayers = LayerMask.GetMask("Ball");
    }

    private void Awake()
    {
        clubBody = GetComponentInParent<Rigidbody>();
        grab = GetComponentInParent<XRGrabInteractable>();

        if (ballLayers.value == 0)
        {
            ballLayers = LayerMask.GetMask("Ball");
        }

        if (grab == null && requireHeld)
        {
            Debug.LogWarning($"{name}: no XRGrabInteractable found above the club head, so 'Require Held' will block every hit.", this);
        }

        CacheHeadOffset();
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
    }

    // Re-measures where the head sits on the club. Call this if the head gets moved
    // (ClubSwitcher does when it changes shaft length).
    public void CacheHeadOffset()
    {
        if (clubBody != null)
        {
            Transform body = clubBody.transform;
            headOffsetLocal = Quaternion.Inverse(body.rotation) * (transform.position - body.position);
            headRotationLocal = Quaternion.Inverse(body.rotation) * transform.rotation;
        }

        ResetTracking();
    }

    private void FixedUpdate()
    {
        Vector3 headPosition = GetHeadPosition();
        Quaternion headRotation = GetHeadRotation();

        if (!hasLastPose)
        {
            lastHeadPosition = headPosition;
            lastHeadRotation = headRotation;
            hasLastPose = true;
            return;
        }

        // Speed for this one physics step, not an average, so it matches the moment of contact.
        Vector3 velocity = (headPosition - lastHeadPosition) / Time.fixedDeltaTime;
        CurrentSpeed = velocity.magnitude;

        if (clubData != null && (!requireHeld || IsHeld))
        {
            CheckSwingPath(lastHeadPosition, lastHeadRotation, headPosition, headRotation, velocity);
        }
        else
        {
            wasTouchingBall = false;
        }

        lastHeadPosition = headPosition;
        lastHeadRotation = headRotation;
    }

    // Slides the face from its last pose to its current pose in small slices (turning as it goes),
    // checking for the ball at each one. Fast swings can't skip past the ball this way.
    private void CheckSwingPath(Vector3 fromPosition, Quaternion fromRotation, Vector3 toPosition, Quaternion toRotation, Vector3 velocity)
    {
        float distance = Vector3.Distance(fromPosition, toPosition);
        int slices = Mathf.Clamp(Mathf.CeilToInt(distance / maxSliceDistance), 1, 16);

        for (int i = 1; i <= slices; i++)
        {
            float t = (float)i / slices;
            Vector3 position = Vector3.Lerp(fromPosition, toPosition, t);
            Quaternion rotation = Quaternion.Slerp(fromRotation, toRotation, t);

            Collider ballCollider = FindBallAt(position, rotation);
            bool touching = ballCollider != null;

            // Only the first slice where the face meets the ball counts, not a club resting against it.
            if (touching && !wasTouchingBall)
            {
                TryHit(ballCollider, position, rotation, velocity);
            }

            wasTouchingBall = touching;
        }
    }

    // Checks whether the face, placed at this pose, overlaps the ball.
    private Collider FindBallAt(Vector3 position, Quaternion rotation)
    {
        GetFaceEnds(position, rotation, out Vector3 heelEnd, out Vector3 toeEnd);
        int count = Physics.OverlapCapsuleNonAlloc(heelEnd, toeEnd, clubData.faceThickness,
            overlapResults, ballLayers, QueryTriggerInteraction.Ignore);

        return count > 0 ? overlapResults[0] : null;
    }

    // The face is a capsule running heel to toe along the head's local X axis.
    private void GetFaceEnds(Vector3 position, Quaternion rotation, out Vector3 heelEnd, out Vector3 toeEnd)
    {
        Vector3 faceAxis = rotation * Vector3.right;
        float halfLength = Mathf.Max(0f, clubData.faceWidth * 0.5f - clubData.faceThickness);
        heelEnd = position - faceAxis * halfLength;
        toeEnd = position + faceAxis * halfLength;
    }

    // Checks that this contact should count, grades it, then launches the ball.
    private void TryHit(Collider other, Vector3 headPosition, Quaternion headRotation, Vector3 velocity)
    {
        GolfBall ball = other.attachedRigidbody != null
            ? other.attachedRigidbody.GetComponent<GolfBall>()
            : other.GetComponent<GolfBall>();

        if (ball == null) return;
        if (Time.time < lastHitTime + hitCooldown) return;
        if (!ball.IsAtRest) return;

        // The head has to be moving toward the ball, not grazing it on the way back.
        Vector3 ballCenter = other.bounds.center;
        if (Vector3.Dot(velocity, ballCenter - headPosition) <= 0f) return;

        float swingSpeed = velocity.magnitude;
        float fullSpeed = clubData.GetBallSpeed(swingSpeed);
        if (fullSpeed <= 0f) return; // too slow to count

        // Where along the face it hit: 0 = dead center, 1 = the very toe or heel.
        Vector3 faceAxis = headRotation * Vector3.right;
        float offsetAlongFace = Vector3.Dot(ballCenter - headPosition, faceAxis);
        float edge = Mathf.Clamp01(Mathf.Abs(offsetAlongFace) / (clubData.faceWidth * 0.5f));

        // Power drops toward the edges. Squared, so the middle of the face is forgiving.
        float ballSpeed = fullSpeed * Mathf.Lerp(1f, clubData.edgePower, edge * edge);

        // Off-center hits get pushed to one side (toe one way, heel the other).
        Vector3 direction = GetLaunchDirection(velocity, headRotation);
        float sidePush = clubData.edgeCurveDegrees * edge * Mathf.Sign(offsetAlongFace);
        direction = Quaternion.AngleAxis(sidePush, Vector3.up) * direction;

        if (!ball.Launch(direction * ballSpeed, clubData.liftStrength, ShotType.Swing, clubData.clubName)) return;

        lastHitTime = Time.time;
        LastSwingSpeed = swingSpeed;
        LastBallSpeed = ballSpeed;
        LastSweetSpot = 1f - edge;

        PlayHaptic();
        onLegitSwing?.Invoke();

        if (logHits)
        {
            Debug.Log($"[{clubData.clubName}] hit: swing {swingSpeed:F1} m/s, sweet spot {LastSweetSpot * 100f:F0}% -> ball {ballSpeed:F1} m/s", this);
        }
    }

    // Works out which way the ball should fly.
    private Vector3 GetLaunchDirection(Vector3 clubVelocity, Quaternion headRotation)
    {
        // 1. Swing path, flattened so up/down motion in the swing doesn't change the aim. Loft handles height.
        Vector3 pathDirection = Vector3.ProjectOnPlane(clubVelocity, Vector3.up);

        // 2. Which way the face points, also flattened.
        Vector3 faceDirection = Vector3.ProjectOnPlane(headRotation * localFaceDirection, Vector3.up);

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
        ResetTracking();
    }

    private void OnReleased(SelectExitEventArgs args)
    {
        if (args.interactorObject == holder)
        {
            holder = null;
        }
    }

    private void ResetTracking()
    {
        hasLastPose = false;
        wasTouchingBall = false;
        CurrentSpeed = 0f;
    }

    // Head position and rotation, read from the club's physics body so they match what the physics engine sees.
    private Vector3 GetHeadPosition()
    {
        return clubBody != null ? clubBody.position + clubBody.rotation * headOffsetLocal : transform.position;
    }

    private Quaternion GetHeadRotation()
    {
        return clubBody != null ? clubBody.rotation * headRotationLocal : transform.rotation;
    }

    // Draws the face hit shape (yellow) and face direction (cyan) in the Scene view when selected.
    private void OnDrawGizmosSelected()
    {
        if (clubData == null) return;

        GetFaceEnds(transform.position, transform.rotation, out Vector3 heelEnd, out Vector3 toeEnd);
        float r = clubData.faceThickness;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(heelEnd, r);
        Gizmos.DrawWireSphere(toeEnd, r);
        Gizmos.DrawLine(heelEnd + transform.up * r, toeEnd + transform.up * r);
        Gizmos.DrawLine(heelEnd - transform.up * r, toeEnd - transform.up * r);
        Gizmos.DrawLine(heelEnd + transform.forward * r, toeEnd + transform.forward * r);
        Gizmos.DrawLine(heelEnd - transform.forward * r, toeEnd - transform.forward * r);

        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(transform.position, transform.rotation * localFaceDirection * 0.2f);
    }
}
