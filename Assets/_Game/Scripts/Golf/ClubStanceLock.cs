using System.Collections;
using Golf.Throwing;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// When the club is grabbed, equips it to the hand, then locks the player at address
/// beside the ball. Distance/height scale with shaft length. GroundFollow stays off while held.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(XRGrabInteractable))]
[RequireComponent(typeof(ClubSwitcher))]
public sealed class ClubStanceLock : MonoBehaviour
{
    const float GroundProbeUp = 3f;
    const float GroundProbeDown = 40f;
    const float StandSlightlyBehind = 0.04f;
    // Pool cue stance. The cue is 1.45 m and held near the butt, so the hand has to start well back
    // or the tip would already be past the ball.
    const float CueStanceBehind = 1.3f;  // head this far behind the ball
    const float CueEyeHeight = 0.55f;    // eyes this far above the ball (bent over the table)

    ClubSwitcher _switcher;
    XRGrabInteractable _grab;
    ClubEquipGrab _equip;
    bool _stanceLocked;
    bool _wired;
    Coroutine _stanceRoutine;

    GroundFollow _suspendedGroundFollow;
    bool _groundFollowWasEnabled;

    // GroundFollow is off while the club is held, so the player rides with the ball's height instead:
    // if the pool table lifts the ball (or drops it back down), the locked stance moves with it.
    GolfBall _stanceBall;
    Rigidbody _stanceBallBody;
    float _stanceBallY;
    Transform _stanceOrigin;
    CharacterController _suspendedCharacter;
    bool _characterWasEnabled;

    void Awake()
    {
        _switcher = GetComponent<ClubSwitcher>();
        _grab = GetComponent<XRGrabInteractable>();
        _equip = GetComponent<ClubEquipGrab>();
        if (_equip == null)
            _equip = gameObject.AddComponent<ClubEquipGrab>();
    }

    void OnEnable()
    {
        Wire();
        if (_switcher != null)
            _switcher.onClubChanged.AddListener(OnClubChanged);
    }

    void OnDisable()
    {
        Unwire();
        if (_switcher != null)
            _switcher.onClubChanged.RemoveListener(OnClubChanged);
        if (_stanceRoutine != null)
        {
            StopCoroutine(_stanceRoutine);
            _stanceRoutine = null;
        }

        ReleaseStanceLock();
    }

    void Wire()
    {
        if (_wired || _grab == null)
            return;

        _grab.selectEntered.AddListener(OnGrabbed);
        _grab.selectExited.AddListener(OnReleased);
        _wired = true;
    }

    void Unwire()
    {
        if (!_wired || _grab == null)
            return;

        _grab.selectEntered.RemoveListener(OnGrabbed);
        _grab.selectExited.RemoveListener(OnReleased);
        _wired = false;
    }

    void OnGrabbed(SelectEnterEventArgs args)
    {        if (!_stanceLocked)
        {
            PlayerLocomotionLock.SetLocked(true);
            _stanceLocked = true;
        }

        SuspendFloorFollow(true);

        if (_stanceRoutine != null)
            StopCoroutine(_stanceRoutine);
        _stanceRoutine = StartCoroutine(EquipThenStance(args));
    }

    void OnReleased(SelectExitEventArgs args)
    {
        if (_stanceRoutine != null)
        {
            StopCoroutine(_stanceRoutine);
            _stanceRoutine = null;
        }

        ReleaseStanceLock();
    }

    void OnClubChanged()
    {
        if (_grab == null || !_grab.isSelected)
            return;

        ApplyStance();
        if (_equip != null)
            _equip.ForceSnapToCurrentInteractor();
    }

    IEnumerator EquipThenStance(SelectEnterEventArgs args)
    {
        // Let XRI finish select/attach, then hard-equip to the hand.
        yield return null;
        if (_equip != null)
            _equip.ForceSnapToInteractor(args.interactorObject);

        // Move the player to the ball; club is already in-hand so it comes along.
        ApplyStance();

        // One more snap after the teleport so the club cannot lag mid-air.
        yield return null;
        if (_equip != null)
            _equip.ForceSnapToCurrentInteractor();

        _stanceRoutine = null;
    }

    void LateUpdate()
    {
        if (!_stanceLocked || _stanceBall == null || _stanceBallBody == null || _stanceOrigin == null)
            return;
        if (!_stanceBall.IsAtRest)
            return; // ball was hit: it's rolling/flying, not being carried

        float ballY = _stanceBallBody.position.y;
        float dy = ballY - _stanceBallY;
        if (Mathf.Abs(dy) < 0.001f)
            return;
        _stanceOrigin.position += Vector3.up * dy;
        _stanceBallY = ballY;
        if (_equip != null)
            _equip.ForceSnapToCurrentInteractor();
    }

    void ReleaseStanceLock()
    {        _stanceBall = null;
        _stanceBallBody = null;
        _stanceOrigin = null;
        SuspendFloorFollow(false);

        if (!_stanceLocked)
            return;

        PlayerLocomotionLock.SetLocked(false);
        _stanceLocked = false;
    }

    void SuspendFloorFollow(bool suspend)
    {
        if (suspend)
        {
            if (!TryResolveRig(out _, out _, out CharacterController character, out GroundFollow groundFollow))
                return;

            if (character != null && _suspendedCharacter == null)
            {
                _suspendedCharacter = character;
                _characterWasEnabled = character.enabled;
                character.enabled = false;
            }

            if (groundFollow != null && _suspendedGroundFollow == null)
            {
                _suspendedGroundFollow = groundFollow;
                _groundFollowWasEnabled = groundFollow.enabled;
                groundFollow.enabled = false;
            }

            return;
        }

        if (_suspendedCharacter != null)
        {
            _suspendedCharacter.enabled = _characterWasEnabled;
            _suspendedCharacter = null;
        }

        if (_suspendedGroundFollow != null)
        {
            _suspendedGroundFollow.enabled = _groundFollowWasEnabled;
            _suspendedGroundFollow = null;
        }
    }

    void ApplyStance()
    {
        GolfBall ball = FindPlayBall();
        if (ball == null)
            return;

        ClubData club = _switcher != null ? _switcher.CurrentClub : null;
        float shaft = club != null ? club.shaftLength : 1f;
        float stanceDistance = StanceDistanceForShaft(shaft);
        float addressEyeHeight = AddressEyeHeightForShaft(shaft);

        if (!TryResolveRig(out Transform originRoot, out Transform camera, out _, out _))
            return;

        Vector3 ballPos = ball.transform.position;
        Vector3 towardHole = GetTowardHole(ballPos);
        Vector3 right = Vector3.Cross(Vector3.up, towardHole);
        if (right.sqrMagnitude < 0.0001f)
            right = Vector3.right;
        else
            right.Normalize();

        Vector3 left = -right;
        Vector3 desiredCamera;
        Vector3 lookDir;
        if (club != null && club.isCue)
        {
            // Pool cue: stand behind the ball looking down the line to the hole, bent over the table.
            desiredCamera = ballPos - towardHole * CueStanceBehind;
            desiredCamera.y = ballPos.y + CueEyeHeight;
            lookDir = towardHole;
        }
        else
        {
            desiredCamera = ballPos + left * stanceDistance - towardHole * StandSlightlyBehind;
            // Target headset height above the ball so a gripped club can reach turf.
            // (Origin often sits on the ground via GroundFollow, so "drop from origin height" was a no-op.)
            desiredCamera.y = ballPos.y + addressEyeHeight;
            // Face the ball (not the cup) so the club head starts in view for an easier hit.
            lookDir = Flatten(ballPos - desiredCamera);
        }

        YawRigToward(originRoot, camera, lookDir);

        Vector3 camOffset = camera.position - originRoot.position;
        Vector3 targetOrigin = originRoot.position;
        targetOrigin.x = desiredCamera.x - camOffset.x;
        targetOrigin.z = desiredCamera.z - camOffset.z;
        targetOrigin.y = desiredCamera.y - camOffset.y;

        originRoot.position = targetOrigin;

        // Remember the ball's height so LateUpdate can carry the player if it moves up or down.
        _stanceBall = ball;
        _stanceBallBody = ball.GetComponent<Rigidbody>();
        _stanceBallY = _stanceBallBody != null ? _stanceBallBody.position.y : ballPos.y;
        _stanceOrigin = originRoot;
    }

    static float StanceDistanceForShaft(float shaftLength)
    {
        // Shorter clubs stand closer; driver stays near the tuned reach.
        // Driver ~0.55, Iron ~0.48, Putter ~0.43.
        float reach = shaftLength * 0.5f;
        return Mathf.Clamp(reach, 0.35f, 0.6f);
    }

    static float AddressEyeHeightForShaft(float shaftLength)
    {
        // Eyes above the ball at address. Kept taller so a natural arm hang puts the
        // club head near the ball instead of forcing an upward bend to reach it.
        // Driver ~1.35, Iron ~1.24, Putter ~1.16.
        return Mathf.Clamp(0.5f + shaftLength * 0.75f, 1.05f, 1.45f);
    }

    static void YawRigToward(Transform originRoot, Transform camera, Vector3 worldForward)
    {
        Vector3 desired = Flatten(worldForward);
        Vector3 currentForward = Flatten(camera.forward);
        float yaw = Vector3.SignedAngle(currentForward, desired, Vector3.up);
        if (Mathf.Abs(yaw) > 0.01f)
            originRoot.RotateAround(camera.position, Vector3.up, yaw);
    }

    static bool TryResolveRig(out Transform originRoot, out Transform camera, out CharacterController character, out GroundFollow groundFollow)
    {
        originRoot = null;
        camera = null;
        character = null;
        groundFollow = null;

        var origin = Object.FindAnyObjectByType<XROrigin>();
        if (origin == null)
            return false;

        camera = origin.Camera != null ? origin.Camera.transform : Camera.main != null ? Camera.main.transform : null;
        originRoot = origin.Origin != null ? origin.Origin.transform : origin.transform;
        if (camera == null || originRoot == null)
            return false;

        character = originRoot.GetComponent<CharacterController>();
        if (character == null)
            character = originRoot.GetComponentInChildren<CharacterController>();

        groundFollow = originRoot.GetComponent<GroundFollow>();
        if (groundFollow == null)
            groundFollow = originRoot.GetComponentInChildren<GroundFollow>();
        if (groundFollow == null)
            groundFollow = origin.GetComponentInChildren<GroundFollow>();

        return true;
    }

    static GolfBall FindPlayBall()
    {
        var balls = Object.FindObjectsByType<GolfBall>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        GolfBall fallback = null;
        for (int i = 0; i < balls.Length; i++)
        {
            GolfBall ball = balls[i];
            if (ball == null)
                continue;

            fallback = ball;
            if (ball.IsAtRest)
                return ball;
        }

        return fallback;
    }

    static Vector3 GetTowardHole(Vector3 fromPos)
    {
        Transform hole = FindNamed("CupTrigger") ?? FindNamed("Flagstick");
        if (hole != null)
        {
            Vector3 toward = Flatten(hole.position - fromPos);
            if (toward.sqrMagnitude > 0.0001f)
                return toward;
        }

        Camera cam = Camera.main;
        if (cam != null)
            return Flatten(cam.transform.forward);

        return Vector3.forward;
    }

    static Transform FindNamed(string exactName)
    {
        var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].name == exactName)
                return all[i];
        }

        return null;
    }

    static bool TryGetGroundY(Vector3 worldPos, out float groundY)
    {
        Vector3 probe = new Vector3(worldPos.x, worldPos.y + GroundProbeUp, worldPos.z);
        if (Physics.Raycast(probe, Vector3.down, out RaycastHit hit, GroundProbeDown, ~0, QueryTriggerInteraction.Ignore))
        {
            groundY = hit.point.y;
            return true;
        }

        groundY = 0f;
        return false;
    }

    static Vector3 Flatten(Vector3 v)
    {
        v.y = 0f;
        if (v.sqrMagnitude < 0.0001f)
            return Vector3.forward;
        return v.normalized;
    }
}
