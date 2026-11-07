using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Golf.Throwing
{
    /// <summary>
    /// Adds XR grab + throw-sequence wiring to the gameplay GolfBall.
    /// On Quest: index trigger (top) grabs the ball; side grip (bottom) is left for the club.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GolfBall))]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CheatThrowBallSetup : MonoBehaviour
    {
        const float GrabWorldRadius = 0.25f;
        const float ProximityGrabDistance = 2.5f;

        GolfBall _ball;
        ThrowSequenceController _sequence;
        XRGrabInteractable _grab;
        SphereCollider _grabVolume;
        bool _wired;
        bool _triggerHeldLastFrame;
        float _suppressPickupUntil;
        bool _expectTriggerGrab;

        XRBaseInputInteractor _selectDriver;
        XRInputButtonReader.InputSourceMode _savedSelectMode;
        bool _drivingSelectFromTrigger;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            WireAllBallsInScene();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => WireAllBallsInScene();

        static void WireAllBallsInScene()
        {
            var balls = Object.FindObjectsByType<GolfBall>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < balls.Length; i++)
            {
                if (balls[i] != null)
                    EnsureOn(balls[i].gameObject);
            }
        }

        public static CheatThrowBallSetup EnsureOn(GameObject ballObject)
        {
            var setup = ballObject.GetComponent<CheatThrowBallSetup>();
            if (setup == null)
                setup = ballObject.AddComponent<CheatThrowBallSetup>();
            setup.EnsureWired();
            return setup;
        }

        void Awake() => EnsureWired();

        void Update()
        {
            SyncGrabAvailable();
            TryKeyboardPickup();
            TryProximityTriggerGrab();
            DriveTriggerSelectWhileHeld();
        }

        void OnDestroy()
        {
            RestoreSelectDriver();
            Unwire();
        }

        public void EnsureWired()
        {
            var body = GetComponent<Rigidbody>();
            _ball = GetComponent<GolfBall>();

            _grab = GetComponent<XRGrabInteractable>();
            if (_grab == null)
                _grab = gameObject.AddComponent<XRGrabInteractable>();

            EnsureGrabVolume();

            _grab.throwOnDetach = false;
            _grab.useDynamicAttach = true;
            _grab.selectMode = InteractableSelectMode.Single;
            _grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
            _grab.interactionLayers = 1;

            var cols = new List<Collider>();
            if (_grabVolume != null)
                cols.Add(_grabVolume);
            var mainCol = GetComponent<SphereCollider>();
            if (mainCol != null && !cols.Contains(mainCol))
                cols.Add(mainCol);

            _grab.colliders.Clear();
            _grab.colliders.AddRange(cols);

            _sequence = GetComponent<ThrowSequenceController>();
            if (_sequence == null)
                _sequence = gameObject.AddComponent<ThrowSequenceController>();

            _sequence.Initialize(_grab, body);

            if (_grab.enabled)
            {
                _grab.enabled = false;
                _grab.enabled = true;
            }

            Wire();
            SyncGrabAvailable();
        }

        void SyncGrabAvailable()
        {
            if (_grab == null || _ball == null)
                return;

            if (Time.time < _suppressPickupUntil)
            {
                if (_grab.enabled)
                    _grab.enabled = false;
                return;
            }

            if (_grab.isSelected || (_sequence != null && _sequence.IsActive))
                return;

            bool allowPickup = _ball.IsAtRest && !IsAnyClubHeld();
            if (_grab.enabled != allowPickup)
                _grab.enabled = allowPickup;
        }

        void EnsureGrabVolume()
        {
            Transform existing = transform.Find("GrabVolume");
            GameObject volumeGo;
            if (existing != null)
            {
                volumeGo = existing.gameObject;
                _grabVolume = volumeGo.GetComponent<SphereCollider>();
                if (_grabVolume == null)
                    _grabVolume = volumeGo.AddComponent<SphereCollider>();
            }
            else
            {
                volumeGo = new GameObject("GrabVolume");
                volumeGo.transform.SetParent(transform, false);
                _grabVolume = volumeGo.AddComponent<SphereCollider>();
            }

            volumeGo.layer = 0;
            volumeGo.transform.localPosition = Vector3.zero;
            volumeGo.transform.localRotation = Quaternion.identity;
            volumeGo.transform.localScale = Vector3.one;

            float lossy = Mathf.Max(transform.lossyScale.x, 0.0001f);
            _grabVolume.isTrigger = true;
            _grabVolume.center = Vector3.zero;
            _grabVolume.radius = GrabWorldRadius / lossy;
        }

        void TryKeyboardPickup()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.gKey.wasPressedThisFrame)
                return;

            if (_grab == null || _sequence == null || _ball == null)
                return;

            if (_grab.isSelected || _sequence.IsActive)
            {
                DropOrCancelHold();
                return;
            }

            if (!_ball.IsAtRest || !_grab.enabled || IsAnyClubHeld())
                return;

            TryForceSelectEnter();
        }

        void DropOrCancelHold()
        {
            _suppressPickupUntil = Time.time + 0.25f;

            if (_sequence != null && _sequence.Phase != ThrowPhase.Idle)
                _sequence.CancelThrow();

            TryForceSelectExit();

            if (_grab != null && _grab.isSelected)
            {
                _grab.enabled = false;
                _grab.enabled = true;
            }

            if (_grab != null)
                _grab.enabled = false;
        }

        void TryProximityTriggerGrab()
        {
            if (_grab == null || _sequence == null || _ball == null)
                return;
            if (!_ball.IsAtRest || !_grab.enabled)
                return;
            if (_grab.isSelected || _sequence.IsActive)
                return;
            if (IsAnyClubHeld())
                return;

            XRBaseInputInteractor interactor = FindPreferredInteractor(out bool grabPressed);
            if (interactor == null)
                return;

            if (interactor.hasSelection)
            {
                _triggerHeldLastFrame = grabPressed;
                return;
            }

            bool risingEdge = grabPressed && !_triggerHeldLastFrame;
            _triggerHeldLastFrame = grabPressed;
            if (!risingEdge)
                return;

            if (Vector3.Distance(interactor.transform.position, transform.position) > ProximityGrabDistance)
                return;

            TryForceSelectEnter(interactor);
        }

        void DriveTriggerSelectWhileHeld()
        {
            if (!_drivingSelectFromTrigger || _selectDriver == null)
                return;

            if (_grab == null || !_grab.isSelected)
            {
                RestoreSelectDriver();
                return;
            }

            bool held = ReadBallGrabButton(_selectDriver);
            _selectDriver.selectInput.inputSourceMode = XRInputButtonReader.InputSourceMode.ManualValue;
            _selectDriver.selectInput.QueueManualState(held, held ? 1f : 0f);
        }

        void BeginTriggerDrivenSelect(XRBaseInputInteractor interactor)
        {
            if (interactor == null)
                return;

            if (_selectDriver != null && _selectDriver != interactor)
                RestoreSelectDriver();

            if (!_drivingSelectFromTrigger)
            {
                _selectDriver = interactor;
                _savedSelectMode = interactor.selectInput.inputSourceMode;
                _drivingSelectFromTrigger = true;
            }

            interactor.selectInput.inputSourceMode = XRInputButtonReader.InputSourceMode.ManualValue;
            interactor.selectInput.QueueManualState(true, 1f);
        }

        void RestoreSelectDriver()
        {
            if (_selectDriver != null && _drivingSelectFromTrigger)
            {
                _selectDriver.selectInput.inputSourceMode = _savedSelectMode;
                // Clear any queued manual state so grip works again next frame.
                _selectDriver.selectInput.QueueManualState(false, 0f);
            }

            _selectDriver = null;
            _drivingSelectFromTrigger = false;
        }

        void TryForceSelectEnter(XRBaseInputInteractor preferred = null)
        {
            if (IsAnyClubHeld())
                return;

            XRBaseInputInteractor interactor = preferred;
            if (interactor == null)
                interactor = FindPreferredInteractor(out _);
            if (interactor == null)
                return;

            if (interactor.hasSelection)
                return;

            if (Vector3.Distance(interactor.transform.position, transform.position) > ProximityGrabDistance)
                return;

            var manager = interactor.interactionManager
                          ?? FindAnyObjectByType<XRInteractionManager>();
            if (manager == null)
                return;

            BeginTriggerDrivenSelect(interactor);
            _expectTriggerGrab = true;
            manager.SelectEnter((IXRSelectInteractor)interactor, (IXRSelectInteractable)_grab);
            _expectTriggerGrab = false;
        }

        void TryForceSelectExit()
        {
            if (_grab == null || !_grab.isSelected)
            {
                RestoreSelectDriver();
                return;
            }

            var interactor = _grab.firstInteractorSelecting;
            var manager = _grab.interactionManager
                          ?? FindAnyObjectByType<XRInteractionManager>();
            if (manager == null || interactor == null)
            {
                RestoreSelectDriver();
                return;
            }

            manager.SelectExit(interactor, _grab);
            RestoreSelectDriver();
        }

        static XRBaseInputInteractor FindPreferredInteractor(out bool grabPressed)
        {
            grabPressed = false;
            XRBaseInputInteractor right = null;
            XRBaseInputInteractor anyPressed = null;

            var interactors = FindObjectsByType<XRBaseInputInteractor>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < interactors.Length; i++)
            {
                var interactor = interactors[i];
                if (interactor == null || !interactor.isActiveAndEnabled)
                    continue;
                if (interactor.gameObject.name.IndexOf("Teleport", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                bool pressed = ReadBallGrabButton(interactor);
                bool isRight = interactor.handedness == InteractorHandedness.Right
                               || interactor.name.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0;

                if (isRight)
                {
                    right = interactor;
                    if (pressed)
                    {
                        grabPressed = true;
                        return interactor;
                    }
                }

                if (pressed && anyPressed == null)
                {
                    anyPressed = interactor;
                    grabPressed = true;
                }
            }

            if (anyPressed != null)
                return anyPressed;

            grabPressed = false;
            return right;
        }

        /// <summary>
        /// Quest: index trigger grabs the ball. Editor / device simulator without XR nodes
        /// falls back to the interactor select so mouse/sim still works.
        /// </summary>
        static bool ReadBallGrabButton(XRBaseInputInteractor interactor)
        {
            if (interactor == null)
                return false;

            var node = VrControllerButtons.NodeForInteractor(interactor);
            var device = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(node);
            if (device.isValid)
                return VrControllerButtons.IsTriggerHeld(node);

            return interactor.selectInput.ReadIsPerformed();
        }

        void Wire()
        {
            if (_wired || _grab == null || _sequence == null)
                return;

            _grab.selectEntered.AddListener(OnSelectEntered);
            _grab.selectExited.AddListener(OnSelectExited);
            _wired = true;
        }

        void Unwire()
        {
            if (!_wired || _grab == null)
                return;

            _grab.selectEntered.RemoveListener(OnSelectEntered);
            _grab.selectExited.RemoveListener(OnSelectExited);
            _wired = false;
        }

        void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (Time.time < _suppressPickupUntil)
            {
                RejectSelect(args);
                return;
            }

            if (IsAnyClubHeld())
            {
                RejectSelect(args);
                return;
            }

            if (_ball != null && !_ball.IsAtRest)
            {
                RejectSelect(args);
                return;
            }

            // Real VR: grip (bottom) must not pick up the ball — only the index trigger (top).
            if (!_expectTriggerGrab
                && args.interactorObject is XRBaseInteractor baseInteractor)
            {
                var node = VrControllerButtons.NodeForInteractor(baseInteractor);
                var device = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(node);
                if (device.isValid && !VrControllerButtons.IsTriggerHeld(node))
                {
                    RejectSelect(args);
                    return;
                }

                if (baseInteractor is XRBaseInputInteractor inputInteractor)
                    BeginTriggerDrivenSelect(inputInteractor);
            }

            if (!_sequence.TryBegin(args))
                RejectSelect(args);
        }

        void OnSelectExited(SelectExitEventArgs args)
        {
            RestoreSelectDriver();
            _sequence.HandleReleased(args);
        }

        void RejectSelect(SelectEnterEventArgs args)
        {
            if (args.manager != null && args.interactorObject != null && _grab != null)
                args.manager.SelectExit(args.interactorObject, _grab);
            RestoreSelectDriver();
        }

        static bool IsAnyClubHeld()
        {
            var clubs = Object.FindObjectsByType<ClubSwitcher>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < clubs.Length; i++)
            {
                if (clubs[i] == null)
                    continue;

                var clubGrab = clubs[i].GetComponent<XRGrabInteractable>();
                if (clubGrab != null && clubGrab.isSelected)
                    return true;
            }

            return false;
        }
    }
}
