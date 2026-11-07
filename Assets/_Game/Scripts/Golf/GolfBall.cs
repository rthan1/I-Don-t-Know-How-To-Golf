using UnityEngine;
using UnityEngine.Events;

// The golf ball. Sets up its own physics, figures out when it has stopped,
// and accepts launches from the club. The club never pushes the ball physically;
// it calls Launch() with a velocity and Unity handles the flight and roll from there.
// Put this on the ball along with a Rigidbody and a SphereCollider.
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(SphereCollider))]
public class GolfBall : MonoBehaviour
{
    [Header("Damping")]
    [Tooltip("Slowdown while in the air. Keep this low so shots carry.")]
    [Min(0f)] public float airDamping = 0.05f;

    [Tooltip("Slowdown while touching the ground. Higher = the ball stops rolling sooner.")]
    [Min(0f)] public float groundDamping = 0.8f;

    [Tooltip("How fast the ball's spin dies down.")]
    [Min(0f)] public float angularDamping = 1f;

    [Tooltip("Fixed slowdown while rolling on the ground, in m/s². This is what lets the ball actually stop, even on gentle slopes.")]
    [Min(0f)] public float rollingResistance = 1.5f;

    [Header("Ground Check")]
    [Tooltip("Which layers count as ground.")]
    public LayerMask groundLayers = ~0;

    [Tooltip("How far below the ball's surface still counts as touching the ground, in meters.")]
    [Min(0f)] public float groundCheckDistance = 0.02f;

    [Header("Stopping")]
    [Tooltip("Below this speed (m/s) the ball starts counting as stopped.")]
    [Min(0f)] public float restSpeed = 0.15f;

    [Tooltip("How long (seconds) the ball has to stay slow and on the ground before it counts as stopped.")]
    [Min(0f)] public float restTime = 0.3f;

    [Tooltip("Right after a launch, skip the stop check for this long (seconds).")]
    [Min(0f)] public float launchGraceTime = 0.25f;

    [Tooltip("Freeze the ball when the scene starts so it's ready for the first shot.")]
    public bool startAtRest = true;

    [Header("Lift")]
    [Tooltip("How quickly backspin lift dies off after launch, in seconds. Higher = the ball floats longer.")]
    [Min(0.01f)] public float liftFadeTime = 0.8f;

    [Header("Events")]
    [Tooltip("Fires when the ball gets hit or thrown.")]
    public UnityEvent onLaunched;

    [Tooltip("Fires when the ball comes to a stop (or gets reset). The next stroke can start.")]
    public UnityEvent onStopped;

    [Header("Testing")]
    [Tooltip("World-space velocity (m/s) used by the Test Launch menu item.")]
    public Vector3 testLaunchVelocity = new Vector3(0f, 3f, 8f);

    [Tooltip("Lift used by the Test Launch menu item, so you can try lift values without a club.")]
    [Min(0f)] public float testLaunchLift = 0f;

    // Read-only info other scripts can check.
    public bool IsAtRest { get; private set; }
    public bool IsGrounded { get; private set; }
    public Vector3 LastRestPosition { get; private set; }

    private Rigidbody rb;
    private SphereCollider sphere;
    private MeshRenderer meshRenderer;
    private float slowTimer;
    private float timeSinceLaunch;
    private float currentLift;
    private bool liftActive;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        sphere = GetComponent<SphereCollider>();
        meshRenderer = GetComponent<MeshRenderer>();

        // Small, fast object: use the most careful collision mode so it doesn't pass through walls.
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        // Smooths movement between physics steps so the ball doesn't look jittery in the headset.
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        // Unity caps spin speed by default, which makes a small ball skid instead of roll.
        rb.maxAngularVelocity = 1000f;

        if (!CompareTag("Ball"))
        {
            Debug.LogWarning($"{name}: GolfBall should be tagged 'Ball'.", this);
        }

        if (gameObject.layer != LayerMask.NameToLayer("Ball"))
        {
            Debug.LogWarning($"{name}: GolfBall should be on the 'Ball' layer.", this);
        }
    }

    /// <summary>Used by the cheat-throw hold so the hand sprite can show the ball instead.</summary>
    public void SetVisible(bool visible)
    {
        if (meshRenderer == null)
            meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer != null)
            meshRenderer.enabled = visible;
    }

    /// <summary>Teleports the resting ball (keeps freeze/rest state).</summary>
    public void TeleportWhileAtRest(Vector3 worldPosition)
    {
        rb.position = worldPosition;
        transform.position = worldPosition;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    private void Start()
    {
        LastRestPosition = transform.position;

        if (startAtRest)
        {
            Freeze(false);
        }
    }

    private void FixedUpdate()
    {
        if (IsAtRest)
        {
            return;
        }

        // Damping is set every step so Inspector tweaks work live in Play mode.
        IsGrounded = CheckGrounded();
        rb.linearDamping = IsGrounded ? groundDamping : airDamping;
        rb.angularDamping = angularDamping;

        if (IsGrounded)
        {
            ApplyRollingResistance();
        }

        // Backspin lift only lasts until the ball first lands.
        if (liftActive)
        {
            if (IsGrounded && timeSinceLaunch >= launchGraceTime)
            {
                liftActive = false;
            }
            else
            {
                ApplyLift();
            }
        }

        timeSinceLaunch += Time.fixedDeltaTime;
        if (timeSinceLaunch < launchGraceTime)
        {
            return;
        }

        // Only count as stopped if the ball is slow AND on the ground,
        // so a ball hanging at the top of a high shot doesn't get frozen in mid-air.
        bool isSlow = rb.linearVelocity.magnitude < restSpeed;
        if (isSlow && IsGrounded)
        {
            slowTimer += Time.fixedDeltaTime;
            if (slowTimer >= restTime)
            {
                Freeze(true);
            }
        }
        else
        {
            slowTimer = 0f;
        }
    }

    // Sends the ball off with the given world-space velocity (m/s) and optional backspin lift.
    // Only works while the ball is at rest. Returns true if the launch happened.
    public bool Launch(Vector3 velocity, float lift = 0f)
    {
        if (!IsAtRest)
        {
            return false;
        }

        rb.constraints = RigidbodyConstraints.None;
        rb.angularVelocity = Vector3.zero;
        rb.linearVelocity = velocity;
        rb.WakeUp();

        IsAtRest = false;
        IsGrounded = false;
        slowTimer = 0f;
        timeSinceLaunch = 0f;
        currentLift = lift;
        liftActive = lift > 0f;

        onLaunched?.Invoke();
        return true;
    }

    // Puts the ball back where it last stopped. Meant for out-of-bounds and water.
    // Fires onStopped so the next stroke can start.
    public void ResetToLastRestPosition()
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.position = LastRestPosition;
        transform.position = LastRestPosition;

        Freeze(true);
    }

    // Stops the ball completely and locks it in place so it can't creep down slopes.
    private void Freeze(bool fireEvent)
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.constraints = RigidbodyConstraints.FreezeAll;

        IsAtRest = true;
        IsGrounded = true;
        slowTimer = 0f;
        liftActive = false;
        LastRestPosition = rb.position;

        if (fireEvent)
        {
            onStopped?.Invoke();
        }
    }

    // Real balls lose about the same amount of speed every second while rolling, no matter how fast.
    // Unity's damping doesn't work that way, so without this a ball on a slight slope rolls forever.
    // This takes a fixed bite out of the speed each step and slows the spin to match,
    // so the spin doesn't push the ball back up to speed.
    private void ApplyRollingResistance()
    {
        Vector3 velocity = rb.linearVelocity;
        float speed = velocity.magnitude;
        if (speed < 0.0001f)
        {
            return;
        }

        float newSpeed = Mathf.Max(0f, speed - rollingResistance * Time.fixedDeltaTime);
        float scale = newSpeed / speed;
        rb.linearVelocity = velocity * scale;
        rb.angularVelocity *= scale;
    }

    // Pushes the ball upward like backspin would: stronger when the ball is fast,
    // fading out over time as the "spin" dies down.
    private void ApplyLift()
    {
        float fade = Mathf.Exp(-timeSinceLaunch / liftFadeTime);
        float liftAcceleration = currentLift * rb.linearVelocity.magnitude * fade;
        rb.AddForce(Vector3.up * liftAcceleration, ForceMode.Acceleration);
    }

    // Shoots a short ray straight down from the ball's center to see if anything is just below it.
    private bool CheckGrounded()
    {
        Vector3 scale = transform.lossyScale;
        float biggestScale = Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
        float worldRadius = sphere.radius * biggestScale;
        Vector3 center = transform.TransformPoint(sphere.center);

        return Physics.Raycast(center, Vector3.down, worldRadius + groundCheckDistance,
            groundLayers, QueryTriggerInteraction.Ignore);
    }

    // Lets you test the ball without a club: in Play mode, right-click the GolfBall
    // component's header in the Inspector and pick "Test Launch".
    [ContextMenu("Test Launch")]
    private void TestLaunch()
    {
        if (!Application.isPlaying)
        {
            Debug.Log("Test Launch only works in Play mode.");
            return;
        }

        if (!Launch(testLaunchVelocity, testLaunchLift))
        {
            Debug.Log("Ball is still moving. Wait for it to stop.");
        }
    }
}
