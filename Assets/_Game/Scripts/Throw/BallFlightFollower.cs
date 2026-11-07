using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Golf.Throwing
{
    /// <summary>
    /// Follows behind the ball on the flag line of sight, then on landing places
    /// the player behind the ball facing the hole and brings the club nearby.
    /// </summary>
    public sealed class BallFlightFollower : MonoBehaviour
    {
        const float FollowSmoothTime = 0.1f;
        const float FlightBackDistance = 1.0f;
        const float MinFlightSpeed = 0.4f;
        const float MinHeightAboveGround = 0.75f;
        const float LandStandBack = 0.25f;
        const float LandStandLeft = 0.4f;
        const float LandCameraRaise = 0.75f;
        const float ClubNearbyForward = 0.1f;
        const float ClubNearbyRight = 0.7f;
        const float ClubNearbyHeight = 1.2f;
        const float GroundProbeUp = 3f;
        const float GroundProbeDown = 40f;

        static BallFlightFollower s_Instance;

        GolfBall _ball;
        Transform _holeTarget;
        XROrigin _origin;
        Transform _camera;
        Transform _originRoot;
        CharacterController _character;
        bool _characterWasEnabled;
        GroundFollow _groundFollow;
        bool _groundFollowWasEnabled;
        bool _groundFollowSuspended;
        Vector3 _followVelocity;
        Vector3 _lastTravelDir = Vector3.forward;
        float _landOriginHeightAboveGround;
        float _landFallbackOriginY;
        bool _hasLandGroundAnchor;
        bool _following;

        public static void Begin(GolfBall ball)
        {
            if (ball == null)
                return;

            EnsureInstance().StartFollow(ball);
        }

        static BallFlightFollower EnsureInstance()
        {
            if (s_Instance != null)
                return s_Instance;

            var existing = Object.FindAnyObjectByType<BallFlightFollower>();
            if (existing != null)
            {
                s_Instance = existing;
                return s_Instance;
            }

            var go = new GameObject("BallFlightFollower");
            s_Instance = go.AddComponent<BallFlightFollower>();
            return s_Instance;
        }

        void Awake() => s_Instance = this;

        void OnDestroy()
        {
            if (s_Instance == this)
                s_Instance = null;
            UnbindBall();
        }

        void StartFollow(GolfBall ball)
        {
            UnbindBall();

            _ball = ball;
            _ball.onStopped.AddListener(OnBallStopped);

            ResolveRig();
            if (_origin == null || _camera == null || _originRoot == null)
            {
                Finish(placeBehindBall: false);
                return;
            }

            CaptureLandingStandingHeight();

            if (_character != null)
            {
                _characterWasEnabled = _character.enabled;
                _character.enabled = false;
            }

            if (_groundFollow != null)
            {
                _groundFollowWasEnabled = _groundFollow.enabled;
                _groundFollow.enabled = false;
                _groundFollowSuspended = true;
            }

            if (VrHandPresenter.Instance != null)
                VrHandPresenter.Instance.SetHandsVisible(false);

            PlayerLocomotionLock.SetLocked(true);
            _followVelocity = Vector3.zero;
            _holeTarget = FindHoleTarget();
            _following = true;

            Rigidbody body = _ball.GetComponent<Rigidbody>();
            if (body != null && body.linearVelocity.sqrMagnitude > 0.01f)
                _lastTravelDir = Flatten(body.linearVelocity);
            else
            {
                Vector3 fromCam = _ball.transform.position - _camera.position;
                if (fromCam.sqrMagnitude > 0.001f)
                    _lastTravelDir = Flatten(fromCam);
            }

            FollowBall(smooth: false);
        }

        void Finish(bool placeBehindBall)
        {
            if (!_following && _ball == null)
                return;

            GolfBall landedBall = _ball;
            _following = false;
            _followVelocity = Vector3.zero;
            UnbindBall();

            if (placeBehindBall && landedBall != null)
            {
                PlaceBehindBallFacingHole(landedBall);
                BringClubNearby();
            }

            _holeTarget = null;

            if (VrHandPresenter.Instance != null)
                VrHandPresenter.Instance.SetHandsVisible(true);

            if (_character != null)
            {
                _character.enabled = _characterWasEnabled;
                _character = null;
            }

            if (_groundFollowSuspended && _groundFollow != null)
                _groundFollow.enabled = _groundFollowWasEnabled;
            _groundFollowSuspended = false;
            _groundFollow = null;

            PlayerLocomotionLock.ForceUnlock();
        }

        void LateUpdate()
        {
            if (!_following || _ball == null || _camera == null || _originRoot == null)
                return;

            if (_character != null && _character.enabled)
                _character.enabled = false;

            if (_ball.IsAtRest)
            {
                Finish(placeBehindBall: true);
                return;
            }

            FollowBall(smooth: true);
        }

        void OnBallStopped() => Finish(placeBehindBall: true);

        void FollowBall(bool smooth)
        {
            Vector3 ballPos = _ball.transform.position;
            Vector3 towardFlag = GetTowardFlagDirection(ballPos);

            Vector3 desiredCameraPos = ballPos - towardFlag * FlightBackDistance;
            desiredCameraPos = ClampAboveGround(desiredCameraPos);

            MoveRigSoCameraReaches(desiredCameraPos, smooth);
            YawRigToward(towardFlag);
        }

        void PlaceBehindBallFacingHole(GolfBall ball)
        {
            ResolveRig();
            if (_origin == null || _camera == null || _originRoot == null)
                return;

            Vector3 ballPos = ball.transform.position;
            Vector3 towardHole = GetTowardFlagDirection(ballPos);

            YawRigToward(towardHole);

            Vector3 camOffset = _camera.position - _originRoot.position;
            // Stand a little behind the ball and to its left, so it sits just off your right side, ready to putt.
            Vector3 rightOfHole = Vector3.Cross(Vector3.up, towardHole).normalized;
            Vector3 desiredCamera = ballPos - towardHole * LandStandBack - rightOfHole * LandStandLeft;
            Vector3 targetOrigin = _originRoot.position;
            targetOrigin.x = desiredCamera.x - camOffset.x;
            targetOrigin.z = desiredCamera.z - camOffset.z;
            targetOrigin.y = ResolveLandingOriginY(targetOrigin) + LandCameraRaise;
            _originRoot.position = targetOrigin;
            _followVelocity = Vector3.zero;
        }

        void BringClubNearby()
        {
            if (_camera == null)
                return;

            Vector3 flatFwd = Flatten(_camera.forward);
            Vector3 right = Vector3.Cross(flatFwd, Vector3.up);
            if (right.sqrMagnitude < 0.0001f)
                right = Flatten(_camera.right);
            else
                right.Normalize();

            Vector3 pos = _camera.position + flatFwd * ClubNearbyForward + right * ClubNearbyRight;
            if (TryGetGroundY(pos, out float groundY))
                pos.y = groundY + ClubNearbyHeight;
            else
                pos.y = _camera.position.y - 0.35f;

            Quaternion rot = Quaternion.Euler(0f, _camera.eulerAngles.y, 0f);

            var clubs = Object.FindObjectsByType<ClubSwitcher>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < clubs.Length; i++)
            {
                ClubSwitcher club = clubs[i];
                if (club == null)
                    continue;

                var grab = club.GetComponent<XRGrabInteractable>();
                if (grab != null && grab.isSelected)
                    continue;

                var body = club.GetComponent<Rigidbody>();
                if (body != null)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    body.position = pos;
                    body.rotation = rot;
                }

                club.transform.SetPositionAndRotation(pos, rot);
            }
        }

        Vector3 GetTowardFlagDirection(Vector3 fromPos)
        {
            if (_holeTarget == null)
                _holeTarget = FindHoleTarget();

            if (_holeTarget != null)
            {
                Vector3 toward = Flatten(_holeTarget.position - fromPos);
                _lastTravelDir = toward;
                return toward;
            }

            UpdateTravelFallback();
            return _lastTravelDir;
        }

        void YawRigToward(Vector3 worldForward)
        {
            Vector3 desired = Flatten(worldForward);
            Vector3 currentForward = Flatten(_camera.forward);
            float yaw = Vector3.SignedAngle(currentForward, desired, Vector3.up);
            if (Mathf.Abs(yaw) > 0.01f)
                _originRoot.RotateAround(_camera.position, Vector3.up, yaw);
        }

        void MoveRigSoCameraReaches(Vector3 desiredCameraPos, bool smooth)
        {
            Vector3 targetCam = desiredCameraPos;
            if (smooth)
                targetCam = Vector3.SmoothDamp(_camera.position, desiredCameraPos, ref _followVelocity, FollowSmoothTime);

            Vector3 delta = targetCam - _camera.position;
            if (delta.sqrMagnitude < 0.0000001f)
                return;

            _originRoot.position += delta;
        }

        void UpdateTravelFallback()
        {
            Rigidbody body = _ball != null ? _ball.GetComponent<Rigidbody>() : null;
            if (body != null && body.linearVelocity.sqrMagnitude >= MinFlightSpeed * MinFlightSpeed)
            {
                _lastTravelDir = Flatten(body.linearVelocity);
                return;
            }

            if (_ball != null && _camera != null)
            {
                Vector3 toBall = _ball.transform.position - _camera.position;
                if (toBall.sqrMagnitude > 0.001f)
                    _lastTravelDir = Flatten(toBall);
            }
        }

        void CaptureLandingStandingHeight()
        {
            _landFallbackOriginY = _originRoot.position.y;
            if (TryGetGroundY(_originRoot.position, out float groundY))
            {
                _landOriginHeightAboveGround = _originRoot.position.y - groundY;
                _hasLandGroundAnchor = true;
            }
            else
            {
                _landOriginHeightAboveGround = 0f;
                _hasLandGroundAnchor = false;
            }
        }

        float ResolveLandingOriginY(Vector3 targetOriginXz)
        {
            if (_hasLandGroundAnchor && TryGetGroundY(targetOriginXz, out float groundY))
                return groundY + _landOriginHeightAboveGround;

            return _landFallbackOriginY;
        }

        static Vector3 ClampAboveGround(Vector3 worldPos)
        {
            if (!TryGetGroundY(worldPos, out float groundY))
                return worldPos;

            float minY = groundY + MinHeightAboveGround;
            if (worldPos.y < minY)
                worldPos.y = minY;
            return worldPos;
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

        void UnbindBall()
        {
            if (_ball == null)
                return;

            _ball.onStopped.RemoveListener(OnBallStopped);
            _ball = null;
        }

        void ResolveRig()
        {
            _origin = Object.FindAnyObjectByType<XROrigin>();
            if (_origin == null)
            {
                _camera = null;
                _originRoot = null;
                _character = null;
                return;
            }

            _camera = _origin.Camera != null ? _origin.Camera.transform : Camera.main?.transform;
            _originRoot = _origin.Origin != null ? _origin.Origin.transform : _origin.transform;
            _character = _originRoot != null
                ? _originRoot.GetComponent<CharacterController>()
                : null;
            if (_character == null && _originRoot != null)
                _character = _originRoot.GetComponentInChildren<CharacterController>();

            _groundFollow = _originRoot != null
                ? _originRoot.GetComponent<GroundFollow>()
                : null;
            if (_groundFollow == null && _originRoot != null)
                _groundFollow = _originRoot.GetComponentInChildren<GroundFollow>();
            if (_groundFollow == null && _origin != null)
                _groundFollow = _origin.GetComponentInChildren<GroundFollow>();
        }

        static Transform FindHoleTarget()
        {
            Transform cup = FindTransformByName("CupTrigger");
            if (cup != null)
                return cup;

            return FindTransformByName("Flagstick");
        }

        static Transform FindTransformByName(string exactName)
        {
            var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == exactName)
                    return all[i];
            }

            return null;
        }

        static Vector3 Flatten(Vector3 v)
        {
            v.y = 0f;
            if (v.sqrMagnitude < 0.0001f)
                return Vector3.forward;
            return v.normalized;
        }
    }
}
