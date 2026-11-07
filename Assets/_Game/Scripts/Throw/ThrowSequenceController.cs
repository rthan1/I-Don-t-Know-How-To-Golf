using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Golf.Throwing
{
    /// <summary>
    /// Cheat-throw flow: Aim → Power → Accuracy → Launch.
    /// Confirm with Quest A (right primary) or keyboard F.
    /// Space (simulator quick-action) is suppressed while throwing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ThrowSequenceController : MonoBehaviour, ICatchableCheat
    {
        const float ConfirmCooldownSeconds = 0.25f;

        [Header("Throw Tuning")]
        [SerializeField] float minThrowSpeed = 4f;
        [SerializeField] float maxThrowSpeed = 38f;
        [SerializeField] float minLoftDegrees = 12f;
        [SerializeField] float maxLoftDegrees = 58f;
        [SerializeField] float maxAccuracyYawDegrees = 28f;
        [SerializeField] float powerMeterSpeed = 1.35f;
        [SerializeField] float accuracyMeterSpeed = 1.6f;

        [Header("Refs")]
        [SerializeField] ThrowMeterUI meterUI;
        [SerializeField] ThrowAimVisualizer aimVisualizer;
        [SerializeField] Transform headsetFollow;

        readonly OscillatingMeter _meter = new OscillatingMeter();

        XRGrabInteractable _grab;
        Rigidbody _body;
        GolfBall _ball;
        IXRSelectInteractor _interactor;
        Transform _handAttach;
        XRInteractionManager _manager;
        bool _savedThrowOnDetach;
        bool _savedTrackPosition = true;
        bool _savedTrackRotation = true;
        bool _launchQueued;
        bool _locomotionLocked;
        float _nextConfirmTime;

        XRInputButtonReader _simulatorQuickActionInput;
        XRInputButtonReader.InputSourceMode _savedQuickActionMode;
        bool _quickActionSuppressed;
        bool _aButtonHeldLastFrame;

        public ThrowPhase Phase { get; private set; } = ThrowPhase.Idle;
        public bool IsActive => Phase != ThrowPhase.Idle && Phase != ThrowPhase.Launching;

        public Vector3 AimDirection { get; private set; } = Vector3.forward;
        public float LockedLoftDegrees { get; private set; } = 18f;
        public float LockedPower { get; private set; }
        public float LockedAccuracy { get; private set; }

        public void Initialize(XRGrabInteractable grab, Rigidbody body)
        {
            _grab = grab;
            _body = body;
            _ball = GetComponent<GolfBall>();

            if (meterUI == null)
                meterUI = gameObject.AddComponent<ThrowMeterUI>();
            if (aimVisualizer == null)
                aimVisualizer = gameObject.AddComponent<ThrowAimVisualizer>();

            ResolveHeadsetFollow();
            meterUI.BindFollowTarget(headsetFollow);
        }

        public bool TryBegin(SelectEnterEventArgs args)
        {
            if (Phase != ThrowPhase.Idle)
                return false;

            if (args.interactorObject is not IXRInteractor interactor)
                return false;

            _interactor = args.interactorObject;
            _manager = args.manager;
            _handAttach = interactor.transform;
            _launchQueued = false;

            _savedThrowOnDetach = _grab.throwOnDetach;
            _grab.throwOnDetach = false;
            _savedTrackPosition = _grab.trackPosition;
            _savedTrackRotation = _grab.trackRotation;
            _grab.trackPosition = false;
            _grab.trackRotation = false;

            if (VrHandPresenter.Instance != null)
                VrHandPresenter.Instance.SetRightHandGrabbing(true);

            if (_ball != null)
                _ball.SetVisible(false);
            SnapBallToPlayer();

            SetLocomotionLocked(true);
            SuppressSimulatorSpaceQuickAction(true);

            UpdateAimFromController();
            _nextConfirmTime = Time.time + ConfirmCooldownSeconds;
            EnterAiming();
            return true;
        }

        public void HandleReleased(SelectExitEventArgs args)
        {
            if (Phase == ThrowPhase.Idle)
                return;

            if (_launchQueued)
            {
                ApplyLaunchVelocity();
                EndThrow(restoreBallVisibility: true, resetToRest: false);
                return;
            }

            EndThrow(restoreBallVisibility: true, resetToRest: true);
        }

        void Update()
        {
            if (Phase == ThrowPhase.Idle || Phase == ThrowPhase.Launching)
                return;

            if (WasConfirmPressed())
                TryConfirm();

            if (Phase == ThrowPhase.Aiming)
            {
                UpdateAimFromController();
                UpdateAimLine();
                return;
            }

            if (Phase == ThrowPhase.Power || Phase == ThrowPhase.Accuracy)
            {
                _meter.Tick(Time.deltaTime);
                meterUI.SetValue(_meter.Value);
                UpdateAimLine();
            }
        }

        void LateUpdate()
        {
            if (Phase == ThrowPhase.Aiming || Phase == ThrowPhase.Power || Phase == ThrowPhase.Accuracy)
                SnapBallToPlayer();
        }

        void TryConfirm()
        {
            if (Time.time < _nextConfirmTime)
                return;
            if (Phase != ThrowPhase.Aiming && Phase != ThrowPhase.Power && Phase != ThrowPhase.Accuracy)
                return;

            _nextConfirmTime = Time.time + ConfirmCooldownSeconds;
            ConfirmCurrentPhase();
        }

        void ConfirmCurrentPhase()
        {
            switch (Phase)
            {
                case ThrowPhase.Aiming:
                    LockAimAndEnterPower();
                    break;
                case ThrowPhase.Power:
                    LockPowerAndEnterAccuracy();
                    break;
                case ThrowPhase.Accuracy:
                    LockAccuracyAndLaunch();
                    break;
            }
        }

        void EnterAiming()
        {
            Phase = ThrowPhase.Aiming;
            meterUI.Show("AIM", "Press A to lock direction", ThrowMeterUI.MeterDisplayMode.Hidden);
            UpdateAimLine(forceShow: true);
        }

        void LockAimAndEnterPower()
        {
            UpdateAimFromController();
            UpdateAimLine();
            EnterPower();
        }

        void EnterPower()
        {
            Phase = ThrowPhase.Power;
            _meter.Start(powerMeterSpeed, 0f);
            meterUI.Show("POWER", "Press A once in the green", ThrowMeterUI.MeterDisplayMode.PowerFill);
            meterUI.SetValue(_meter.Value);
            UpdateAimLine(forceShow: true);
        }

        void LockPowerAndEnterAccuracy()
        {
            LockedPower = _meter.Stop();
            UpdateAimLine();

            Phase = ThrowPhase.Accuracy;
            _meter.Start(accuracyMeterSpeed, 0f);
            meterUI.Show("ACCURACY", "Press A once in the green", ThrowMeterUI.MeterDisplayMode.AccuracyNeedle);
            meterUI.SetValue(_meter.Value);
        }

        void UpdateAimLine(bool forceShow = false)
        {
            if (aimVisualizer == null)
                return;

            float previewPower = Phase == ThrowPhase.Power
                ? Mathf.Clamp01(_meter.Value)
                : Phase == ThrowPhase.Accuracy
                    ? Mathf.Clamp01(LockedPower)
                    : 0.55f;
            float previewSpeed = Mathf.Lerp(minThrowSpeed, maxThrowSpeed, previewPower);

            Vector3 origin = GetPlayerThrowOrigin();
            if (forceShow)
                aimVisualizer.Show(origin, AimDirection, previewSpeed, LockedLoftDegrees);
            else
                aimVisualizer.UpdateArc(origin, AimDirection, previewSpeed, LockedLoftDegrees);
        }

        void SnapBallToPlayer()
        {
            Vector3 origin = GetPlayerThrowOrigin();
            if (_ball != null)
            {
                _ball.TeleportWhileAtRest(origin);
                return;
            }

            transform.position = origin;
            if (_body != null)
            {
                _body.position = origin;
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
            }
        }

        void RestoreGrabTracking()
        {
            if (_grab == null)
                return;

            _grab.throwOnDetach = _savedThrowOnDetach;
            _grab.trackPosition = _savedTrackPosition;
            _grab.trackRotation = _savedTrackRotation;
        }

        Vector3 GetPlayerThrowOrigin()
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 flatFwd = Flatten(cam.transform.forward);
                return cam.transform.position + flatFwd * 0.35f + Vector3.down * 0.4f;
            }

            if (_handAttach != null)
                return _handAttach.position;

            return transform.position;
        }

        void LockAccuracyAndLaunch()
        {
            LockedAccuracy = _meter.Stop();
            meterUI.Hide();
            aimVisualizer.Hide();

            Phase = ThrowPhase.Launching;
            _launchQueued = true;
            SnapBallToPlayer();

            if (_manager != null && _interactor != null && _grab != null && _grab.isSelected)
                _manager.SelectExit(_interactor, _grab);
            else
            {
                ApplyLaunchVelocity();
                EndThrow(restoreBallVisibility: true, resetToRest: false);
            }
        }

        void ApplyLaunchVelocity()
        {
            float powerStrength = Mathf.Clamp01(LockedPower);
            float speed = Mathf.Lerp(minThrowSpeed, maxThrowSpeed, powerStrength);
            float yawError = (LockedAccuracy - 0.5f) * 2f * maxAccuracyYawDegrees;
            Vector3 lofted = Quaternion.LookRotation(AimDirection) * Quaternion.Euler(-LockedLoftDegrees, 0f, 0f) * Vector3.forward;
            Vector3 velocity = Quaternion.AngleAxis(yawError, Vector3.up) * lofted * speed;

            if (_ball != null)
            {
                _ball.SetVisible(true);
                if (_ball.Launch(velocity))
                    return;
            }

            if (_body == null)
                return;

            _body.constraints = RigidbodyConstraints.None;
            _body.isKinematic = false;
            _body.useGravity = true;
            _body.linearVelocity = velocity;
            _body.angularVelocity = Random.onUnitSphere * (speed * 0.35f);
        }

        void EndThrow(bool restoreBallVisibility, bool resetToRest)
        {
            meterUI.Hide();
            aimVisualizer.Hide();
            _meter.Reset();
            RestoreGrabTracking();

            if (VrHandPresenter.Instance != null)
                VrHandPresenter.Instance.SetRightHandGrabbing(false);

            if (_ball != null)
            {
                if (restoreBallVisibility)
                    _ball.SetVisible(true);
                if (resetToRest)
                    _ball.ResetToLastRestPosition();
            }

            SuppressSimulatorSpaceQuickAction(false);

            bool startFlightFollow = !resetToRest && _ball != null;
            if (startFlightFollow)
            {
                // Keep locomotion locked through flight; follower unlocks on landing.
                _locomotionLocked = false;
                BallFlightFollower.Begin(_ball);
            }
            else
            {
                UnlockLocomotion();
            }

            _interactor = null;
            _handAttach = null;
            _manager = null;
            _launchQueued = false;
            _nextConfirmTime = 0f;
            _aButtonHeldLastFrame = false;
            Phase = ThrowPhase.Idle;
        }

        bool WasConfirmPressed()
        {
            // Quest A = right-hand primary button. Keep F for desktop/simulator.
            bool aHeld = false;
            var right = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
            if (right.isValid)
                right.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out aHeld);

            bool aPressed = aHeld && !_aButtonHeldLastFrame;
            _aButtonHeldLastFrame = aHeld;
            if (aPressed)
                return true;

            var keyboard = Keyboard.current;
            return keyboard != null && keyboard.fKey.wasPressedThisFrame;
        }

        void SuppressSimulatorSpaceQuickAction(bool suppress)
        {
            if (suppress)
            {
                if (_quickActionSuppressed)
                    return;

                var sim = XRInteractionSimulator.instance
                          ?? Object.FindAnyObjectByType<XRInteractionSimulator>(FindObjectsInactive.Include);
                if (sim == null || sim.togglePerformQuickActionInput == null)
                    return;

                _simulatorQuickActionInput = sim.togglePerformQuickActionInput;
                _savedQuickActionMode = _simulatorQuickActionInput.inputSourceMode;
                _simulatorQuickActionInput.inputSourceMode = XRInputButtonReader.InputSourceMode.Unused;
                _quickActionSuppressed = true;
                return;
            }

            if (!_quickActionSuppressed || _simulatorQuickActionInput == null)
            {
                _quickActionSuppressed = false;
                _simulatorQuickActionInput = null;
                return;
            }

            _simulatorQuickActionInput.inputSourceMode = _savedQuickActionMode;
            _simulatorQuickActionInput = null;
            _quickActionSuppressed = false;
        }

        void SetLocomotionLocked(bool locked)
        {
            if (locked == _locomotionLocked)
                return;

            _locomotionLocked = locked;
            PlayerLocomotionLock.SetLocked(locked);
        }

        void UnlockLocomotion()
        {
            _locomotionLocked = false;
            PlayerLocomotionLock.ForceUnlock();
        }

        void OnEnable() => SecurityCameraSystem.Register(this);

        void OnDisable() => SecurityCameraSystem.Unregister(this);

        void OnDestroy()
        {
            SuppressSimulatorSpaceQuickAction(false);
            UnlockLocomotion();
        }

        // ---- Security cameras: the cheat throw counts as cheating from grabbing the ball until launch ----
        public bool IsCheating => IsActive;
        public string CheatName => "Cheat Throw";
        public void OnCaught() => CancelThrow();

        /// <summary>Stops the throw without launching. The ball goes back where it last stopped.</summary>
        public void CancelThrow()
        {
            if (Phase == ThrowPhase.Idle)
                return;

            _launchQueued = false;

            // Always drop the XR grab first. Ending the sequence while still selected
            // restores trackPosition and the ball sticks to the hand with no throw UI.
            ForceSelectExit();

            // SelectExit normally reaches EndThrow via HandleReleased. If selection was
            // already gone or SelectExit no-op'd (wrong interactor), finish locally.
            if (Phase != ThrowPhase.Idle)
                EndThrow(restoreBallVisibility: true, resetToRest: true);
        }

        void ForceSelectExit()
        {
            if (_grab == null || !_grab.isSelected)
                return;

            IXRSelectInteractor interactor = _interactor ?? _grab.firstInteractorSelecting;
            XRInteractionManager manager = _manager
                                           ?? _grab.interactionManager
                                           ?? Object.FindAnyObjectByType<XRInteractionManager>();

            if (manager != null && interactor != null)
                manager.SelectExit(interactor, _grab);

            // Toggle-select can leave a stuck selection if the interactor ref was stale.
            if (_grab.isSelected)
            {
                _grab.enabled = false;
                _grab.enabled = true;
            }
        }

        void UpdateAimFromController()
        {
            Vector3 aim = GetAimForward();
            AimDirection = Flatten(aim);
            LockedLoftDegrees = GetLoftFromAim(aim);
        }

        float GetLoftFromAim(Vector3 aim)
        {
            if (aim.sqrMagnitude < 0.0001f)
                return minLoftDegrees;

            float pitch = Mathf.Asin(Mathf.Clamp(aim.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
            return Mathf.Clamp(pitch, minLoftDegrees, maxLoftDegrees);
        }

        Vector3 GetAimForward()
        {
            if (_handAttach != null)
                return _handAttach.forward;

            if (_interactor is IXRInteractor ixr && ixr.transform != null)
                return ixr.transform.forward;

            return transform.forward;
        }

        void ResolveHeadsetFollow()
        {
            if (headsetFollow != null)
                return;

            Camera cam = Camera.main;
            if (cam != null)
                headsetFollow = cam.transform;
        }

        static Vector3 Flatten(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
                return Vector3.forward;
            return direction.normalized;
        }
    }
}
