using Golf.Throwing;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Pool table shot. While the club is a pool cue and held, the aim is locked straight at the cup and a
/// power meter (the same one the cheat throw uses) runs. Press A (or F on the keyboard) to take the shot.
///
/// The meter's green band is "just right": stopping the marker in the middle of the green rolls the ball
/// to the cup. Lower is short, higher is long. ClubSwitcher adds this to the club automatically.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(ClubSwitcher))]
public sealed class PoolCueShot : MonoBehaviour
{
    [Tooltip("How fast the power marker moves up and down.")]
    [Min(0.1f)] public float meterSpeed = 1.2f;

    [Tooltip("Where on the meter (0 bottom, 1 top) the perfect power sits. 0.875 = middle of the green band.")]
    [Range(0.3f, 1f)] public float perfectPoint = 0.875f;

    [Tooltip("Aim for the ball to stop this far past the middle of the cup, so it doesn't die on the lip.")]
    [Min(0f)] public float stopPastCup = 0.05f;

    const float StartDelay = 0.4f;      // let the stance/teleport finish before the meter starts
    const float ConfirmCooldown = 0.3f; // so the grab press can't also take the shot

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
        {
            _meterUI = gameObject.AddComponent<ThrowMeterUI>();
            if (Camera.main != null) _meterUI.BindFollowTarget(Camera.main.transform);
        }

        _meter.Start(meterSpeed, 0f);
        _meterUI.Show("POWER", "Aimed at the cup. Press A in the green", ThrowMeterUI.MeterDisplayMode.PowerFill);
        _meterUI.SetValue(_meter.Value);
        _running = true;
        _confirmReadyAt = Time.time + ConfirmCooldown;
        _aHeldLastFrame = true; // ignore an A that's already down
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
        float power = _meter.Stop();
        StopMeter();
        if (_head == null) _head = GetComponentInChildren<ClubHead>();
        if (_head == null) return;

        // Locked aim: straight from the ball to the cup, flat along the felt.
        Vector3 ballPos = ball.transform.position;
        Transform cup = FindActiveNamed("CupTrigger");
        Vector3 toCup = cup != null ? cup.position - ballPos : (Camera.main != null ? Camera.main.transform.forward : Vector3.forward);
        toCup.y = 0f;
        float distance = cup != null ? toCup.magnitude : 3f;
        Vector3 direction = toCup.sqrMagnitude > 0.0001f ? toCup.normalized : Vector3.forward;

        // Perfect power rolls it to just past the cup; the meter scales that (green middle = perfect).
        float perfectSpeed = SpeedToRoll(ball, distance + stopPastCup);
        float speed = perfectSpeed * (power / perfectPoint);
        speed = Mathf.Clamp(speed, 0.2f, perfectSpeed * 1.6f + 0.5f);

        _head.CueShot(ball, direction * speed);
    }

    // Launch speed that makes the ball roll `distance` meters on the felt before stopping, using the
    // ball's own slowdown settings (ground damping + rolling resistance), stepped like the physics engine.
    static float SpeedToRoll(GolfBall ball, float distance)
    {
        float lo = 0f, hi = 30f;
        for (int i = 0; i < 30; i++)
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
        float v = speed, x = 0f;
        for (int step = 0; step < 5000 && v > ball.restSpeed; step++)
        {
            v = Mathf.Max(0f, v - ball.rollingResistance * dt);
            v /= 1f + ball.groundDamping * dt; // how Unity applies linear damping
            x += v * dt;
        }
        return x;
    }

    bool WasConfirmPressed()
    {
        // Quest A = right-hand primary button (same as the cheat throw). F for desktop/simulator.
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

    static Transform FindActiveNamed(string exactName)
    {
        foreach (Transform t in FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (t.name == exactName) return t;
        return null;
    }
}
