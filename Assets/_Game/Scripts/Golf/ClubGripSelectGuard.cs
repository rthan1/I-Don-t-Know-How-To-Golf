using Golf.Throwing;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Club uses the bottom grip. Rejects a select that came from the index trigger alone
/// when a real XR controller is present (Quest top trigger is reserved for the ball throw).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(XRGrabInteractable))]
public sealed class ClubGripSelectGuard : MonoBehaviour
{
    XRGrabInteractable _grab;

    void Awake() => _grab = GetComponent<XRGrabInteractable>();

    void OnEnable()
    {
        if (_grab != null)
            _grab.selectEntered.AddListener(OnSelectEntered);
    }

    void OnDisable()
    {
        if (_grab != null)
            _grab.selectEntered.RemoveListener(OnSelectEntered);
    }

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (_grab == null || args.interactorObject is not XRBaseInteractor interactor)
            return;

        var node = VrControllerButtons.NodeForInteractor(interactor);
        var device = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid)
            return; // Editor / simulator: keep existing select (mouse / sim grip).

        // Real headset: require the side grip. Index trigger alone is for the ball.
        if (VrControllerButtons.IsGripHeld(node))
            return;

        if (args.manager != null)
            args.manager.SelectExit(args.interactorObject, _grab);
    }
}
