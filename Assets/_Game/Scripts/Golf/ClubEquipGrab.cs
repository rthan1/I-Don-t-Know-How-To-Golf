using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Makes club grab behave like an equip: always snaps to a fixed AttachPoint on the hand,
/// never hangs out at ray/far-hit distance.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(XRGrabInteractable))]
public sealed class ClubEquipGrab : MonoBehaviour
{
    const string AttachPointName = "AttachPoint";

    XRGrabInteractable _grab;
    Transform _attach;

    void Awake()
    {
        _grab = GetComponent<XRGrabInteractable>();
        EnsureAttachPoint();
        ApplyEquipSettings();
    }

    void OnEnable()
    {
        if (_grab == null)
            return;

        _grab.selectEntered.AddListener(OnSelectEntered);
        ApplyEquipSettings();
    }

    void OnDisable()
    {
        if (_grab == null)
            return;

        _grab.selectEntered.RemoveListener(OnSelectEntered);
    }

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        ApplyEquipSettings();
        ForceSnapToInteractor(args.interactorObject);
    }

    /// <summary>Hard-snaps the club grip to the selecting hand (call again after teleporting the player).</summary>
    public void ForceSnapToInteractor(IXRSelectInteractor interactor)
    {
        if (_grab == null || interactor == null)
            return;

        EnsureAttachPoint();
        ApplyEquipSettings();

        Transform handAttach = null;
        if (interactor is XRBaseInteractor baseInteractor)
            handAttach = baseInteractor.GetAttachTransform(_grab);
        else if (interactor is IXRInteractor ixr)
            handAttach = ixr.transform;

        if (handAttach == null || _attach == null)
            return;

        // Place the club so its AttachPoint matches the hand attach pose exactly.
        Quaternion targetRot = handAttach.rotation * Quaternion.Inverse(_attach.localRotation);
        Vector3 targetPos = handAttach.position - targetRot * _attach.localPosition;
        transform.SetPositionAndRotation(targetPos, targetRot);

        var body = GetComponent<Rigidbody>();
        if (body != null)
        {
            body.position = targetPos;
            body.rotation = targetRot;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }

    public void ForceSnapToCurrentInteractor()
    {
        if (_grab == null || !_grab.isSelected)
            return;

        ForceSnapToInteractor(_grab.firstInteractorSelecting);
    }

    void EnsureAttachPoint()
    {
        _attach = transform.Find(AttachPointName);
        if (_attach == null)
        {
            var go = new GameObject(AttachPointName);
            _attach = go.transform;
            _attach.SetParent(transform, false);
            // Match the Club prefab AttachPoint (slightly down the shaft, tilted for grip).
            _attach.localPosition = new Vector3(0f, -0.08f, 0f);
            _attach.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }
    }

    void ApplyEquipSettings()
    {
        if (_grab == null)
            return;

        if (_attach == null)
            EnsureAttachPoint();

        _grab.attachTransform = _attach;
        _grab.useDynamicAttach = false;
        _grab.matchAttachPosition = true;
        _grab.matchAttachRotation = true;
        _grab.snapToColliderVolume = false;
        _grab.attachEaseInTime = 0f;
        _grab.movementType = XRBaseInteractable.MovementType.Instantaneous;
        _grab.throwOnDetach = false;
        _grab.farAttachMode = InteractableFarAttachMode.Near;
    }
}
