using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Desktop/simulator: right mouse button toggles grabbing the golf club
/// with the right-hand interactor.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(XRGrabInteractable))]
public sealed class DesktopClubGrabInput : MonoBehaviour
{
    const float GrabDistance = 4f;

    XRGrabInteractable _grab;

    void Awake()
    {
        _grab = GetComponent<XRGrabInteractable>();
    }

    void Update()
    {
        var mouse = Mouse.current;
        if (mouse == null || !mouse.rightButton.wasPressedThisFrame)
            return;

        if (_grab == null)
            return;

        if (_grab.isSelected)
        {
            TryRelease();
            return;
        }

        TryGrab();
    }

    void TryGrab()
    {
        XRBaseInputInteractor interactor = FindRightInteractor();
        if (interactor == null)
            return;

        if (Vector3.Distance(interactor.transform.position, transform.position) > GrabDistance)
            return;

        var manager = interactor.interactionManager
                      ?? Object.FindAnyObjectByType<XRInteractionManager>();
        if (manager == null)
            return;

        manager.SelectEnter((IXRSelectInteractor)interactor, (IXRSelectInteractable)_grab);
    }

    void TryRelease()
    {
        var interactor = _grab.firstInteractorSelecting;
        var manager = _grab.interactionManager
                      ?? Object.FindAnyObjectByType<XRInteractionManager>();
        if (manager == null || interactor == null)
            return;

        manager.SelectExit(interactor, _grab);
    }

    static XRBaseInputInteractor FindRightInteractor()
    {
        XRBaseInputInteractor fallback = null;
        var interactors = Object.FindObjectsByType<XRBaseInputInteractor>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < interactors.Length; i++)
        {
            var interactor = interactors[i];
            if (interactor == null || !interactor.isActiveAndEnabled)
                continue;
            if (interactor.gameObject.name.IndexOf("Teleport", System.StringComparison.OrdinalIgnoreCase) >= 0)
                continue;

            bool isRight = interactor.handedness == InteractorHandedness.Right
                           || interactor.name.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (isRight)
                return interactor;

            if (fallback == null)
                fallback = interactor;
        }

        return fallback;
    }
}
