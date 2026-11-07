using Golf.Throwing;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Pool table shot. While the club is a pool cue and held, aim is locked at the cup and a
/// meter runs — same accuracy-style bar as the cheat throw (green in the middle).
/// Press A (or F) when the marker is in the green: that power rolls the ball to the cup.
/// Lower = short, higher = long. ClubSwitcher adds this automatically.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(ClubSwitcher))]
public sealed class PoolCueShot : MonoBehaviour
{
    [Tooltip("How fast the marker moves up and down.")]
    [Min(0.1f)] public float meterSpeed = 1.6f;

    [Tooltip("Meter value that is perfect power (accuracy green center is 0.5).")]
    [Range(0.4f, 0.6f)] public float perfectPoint = 0.5f;

    [Tooltip("Aim for the ball to stop this far past the cup center so it drops in.")]
    [Min(0f)] public float stopPastCup = 0.14f;

    [Tooltip("Extra speed on top of the physics estimate so a green hit still reaches on real felt.")]
    [Min(1f)] public float perfectSpeedBoost = 1.32f;

    const float StartDelay = 0.4f;
    const float ConfirmCooldown = 0.3f;

    ClubSwitcher _switcher;
    XRGrabInteractable _grab;
    ClubHead _head;
    ThrowMeterUI _meterUI;
    readonly OscillatingMeter _meter = new OscillatingMeter();

    bool _running;
    float _heldSince = -1f;
    float _confirmReadyAt;
    bool _aHeldLastFrame;

    void Awake()
    {
        _switcher = GetComponent<ClubSwitcher>();
        _grab = GetComponent<XRGrabInteractable>();
        _head = GetComponentInChildren<ClubHead>();
    }

    void OnDisable() => StopMeter();

    void Update()
    {
        bool held = _grab != null && _grab.isSelected;
        if (!held || _switcher == null || !_switcher.CueMode)
        {
            _heldSince = -1f;
            StopMeter();
            return;
        }

        GolfBall ball = FindRestingBall();
        if (ball == null)
        {
            StopMeter();
            return;
        }

        if (!_running)
        {
            if (_heldSince < 0f) _heldSince = Time.time;
            if (Time.time < _heldSince + StartDelay) return;
            StartMeter();
        }

        _meter.Tick(Time.deltaTime);
        _meterUI.SetValue(_meter.Value);

        if (WasConfirmPressed() && Time.time >= _confirmReadyAt)
            Shoot(ball);
    }

    void StartMeter()
    {
        if (_meterUI == null)
            _meterUI = gameObject.AddComponent<ThrowMeterUI>();

        if (Camera.main != null)
            _meterUI.BindFollowTarget(Camera.main.transform);

        // Same bar as throw accuracy: green band in the middle.
        _meter.Start(meterSpeed, 0f);
        _meterUI.Show("POWER", "Press A once in the green", ThrowMeterUI.MeterDisplayMode.AccuracyNeedle);
        _meterUI.SetValue(_meter.Value);
        _running = true;
        _confirmReadyAt = Time.time + ConfirmCooldown;
        _aHeldLastFrame = true;
    }

    void StopMeter()
    {
        if (!_running) return;
        _running = false;
        _meter.Reset();
        if (_meterUI != null) _meterUI.Hide();
    }

    void Shoot(GolfBall ball)
    {
        float meterValue = _meter.Stop();
        StopMeter();
        if (_head == null) _head = GetComponentInChildren<ClubHead>();
        if (_head == null) return;

        Vector3 ballPos = ball.transform.position;
        Transform cup = FindNearestActiveCup(ballPos);
        Vector3 toCup = cup != null
            ? cup.position - ballPos
            : (Camera.main != null ? Camera.main.transform.forward : Vector3.forward);
        toCup.y = 0f;

        float distance = cup != null ? toCup.magnitude : 3f;
        Vector3 direction = toCup.sqrMagnitude > 0.0001f ? toCup.normalized : Vector3.forward;

        // Green center (perfectPoint) → speed that rolls to / just past the cup.
        float perfectSpeed = SpeedToRoll(ball, distance + stopPastCup) * perfectSpeedBoost;
        float scale = meterValue / Mathf.Max(0.05f, perfectPoint);
        float speed = Mathf.Clamp(perfectSpeed * scale, 0.15f, perfectSpeed * 2f);

        _head.CueShot(ball, direction * speed);
    }

    // Launch speed that makes the ball roll `distance` meters before stopping, using the
    // ball's ground damping + rolling resistance (same order as GolfBall.FixedUpdate).
    static float SpeedToRoll(GolfBall ball, float distance)
    {
        float lo = 0f, hi = 40f;
        for (int i = 0; i < 32; i++)
        {
            float mid = (lo + hi) * 0.5f;
            if (RollDistance(ball, mid) < distance) lo = mid;
            else hi = mid;
        }
        return hi;
    }

    static float RollDistance(GolfBall ball, float speed)
    {
        float dt = Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : 0.02f;
        float damp = Mathf.Max(0f, ball.groundDamping);
        float resist = Mathf.Max(0f, ball.rollingResistance);
        float stop = Mathf.Max(0.01f, ball.restSpeed);

        float v = speed, x = 0f;
        for (int step = 0; step < 8000 && v > stop; step++)
        {
            // Match GolfBall: rolling resistance first, then Unity linearDamping.
            v = Mathf.Max(0f, v - resist * dt);
            v /= 1f + damp * dt;
            x += v * dt;
        }
        return x;
    }

    bool WasConfirmPressed()
    {
        bool aHeld = false;
        var right = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
        if (right.isValid)
            right.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out aHeld);

        bool aPressed = aHeld && !_aHeldLastFrame;
        _aHeldLastFrame = aHeld;
        if (aPressed) return true;

        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        return keyboard != null && keyboard.fKey.wasPressedThisFrame;
    }

    static GolfBall FindRestingBall()
    {
        foreach (GolfBall ball in FindObjectsByType<GolfBall>(FindObjectsSortMode.None))
            if (ball.IsAtRest) return ball;
        return null;
    }

    static Transform FindNearestActiveCup(Vector3 from)
    {
        Transform best = null;
        float bestDist = float.MaxValue;
        var all = FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == null || t.name != "CupTrigger")
                continue;

            float d = (t.position - from).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = t;
            }
        }

        return best;
    }
}
