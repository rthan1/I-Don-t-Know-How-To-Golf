using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Golf.Throwing
{
    /// <summary>
    /// Adds XR grab + throw-sequence wiring to the gameplay GolfBall.
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
        bool _selectHeldLastFrame;
        float _suppressPickupUntil;

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
            TryProximityGrabFallback();
        }

        void OnDestroy() => Unwire();

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

            // Force collider/interaction refresh after runtime component adds.
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

            // Brief lockout after a manual cancel so the same press can't re-grab.
            if (Time.time < _suppressPickupUntil)
            {
                if (_grab.enabled)
                    _grab.enabled = false;
                return;
            }

            // Keep grab enabled while already held / in throw UI.
            if (_grab.isSelected || (_sequence != null && _sequence.IsActive))
                return;

            // Holding a club means the ball is not grabbable (Quest + simulator).
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

            // Default layer so NearFar casters can hit it even when the ball is on Ball.
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

            // G toggles: pick up when at rest, drop/cancel if already holding.
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
            // Block an immediate re-grab from the same select press after we force-exit.
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

        void TryProximityGrabFallback()
        {
            if (_grab == null || _sequence == null || _ball == null)
                return;
            if (!_ball.IsAtRest || !_grab.enabled)
                return;
            if (_grab.isSelected || _sequence.IsActive)
                return;
            if (IsAnyClubHeld())
                return;

            XRBaseInputInteractor interactor = FindPreferredInteractor(out bool selectPressed);
            if (interactor == null)
                return;

            // Already holding something (club, etc.) with this hand — don't steal for the ball.
            if (interactor.hasSelection)
            {
                _selectHeldLastFrame = selectPressed;
                return;
            }

            bool risingEdge = selectPressed && !_selectHeldLastFrame;
            _selectHeldLastFrame = selectPressed;
            if (!risingEdge)
                return;

            if (Vector3.Distance(interactor.transform.position, transform.position) > ProximityGrabDistance)
                return;

            TryForceSelectEnter(interactor);
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

            manager.SelectEnter((IXRSelectInteractor)interactor, (IXRSelectInteractable)_grab);
        }

        void TryForceSelectExit()
        {
            if (_grab == null || !_grab.isSelected)
                return;

            var interactor = _grab.firstInteractorSelecting;
            var manager = _grab.interactionManager
                          ?? FindAnyObjectByType<XRInteractionManager>();
            if (manager == null || interactor == null)
                return;

            manager.SelectExit(interactor, _grab);
        }

        static XRBaseInputInteractor FindPreferredInteractor(out bool selectPressed)
        {
            selectPressed = false;
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

                bool pressed = interactor.selectInput.ReadIsPerformed();
                bool isRight = interactor.handedness == InteractorHandedness.Right
                               || interactor.name.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0;

                if (isRight)
                {
                    right = interactor;
                    if (pressed)
                    {
                        selectPressed = true;
                        return interactor;
                    }
                }

                if (pressed && anyPressed == null)
                {
                    anyPressed = interactor;
                    selectPressed = true;
                }
            }

            if (anyPressed != null)
                return anyPressed;

            selectPressed = false;
            return right;
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
                if (args.manager != null && args.interactorObject != null)
                    args.manager.SelectExit(args.interactorObject, _grab);
                return;
            }

            if (IsAnyClubHeld())
            {
                if (args.manager != null && args.interactorObject != null)
                    args.manager.SelectExit(args.interactorObject, _grab);
                return;
            }

            if (_ball != null && !_ball.IsAtRest)
            {
                if (args.manager != null && args.interactorObject != null)
                    args.manager.SelectExit(args.interactorObject, _grab);
                return;
            }

            if (!_sequence.TryBegin(args))
            {
                if (args.manager != null && args.interactorObject != null)
                    args.manager.SelectExit(args.interactorObject, _grab);
            }
        }

        void OnSelectExited(SelectExitEventArgs args) => _sequence.HandleReleased(args);

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
