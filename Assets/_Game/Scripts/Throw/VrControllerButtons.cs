using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Golf.Throwing
{
    /// <summary>
    /// Quest-style controller buttons: index trigger (top) vs side grip (bottom).
    /// </summary>
    public static class VrControllerButtons
    {
        public static bool IsTriggerHeld(XRNode node)
        {
            var device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid)
                return false;

            if (device.TryGetFeatureValue(CommonUsages.triggerButton, out bool pressed) && pressed)
                return true;

            if (device.TryGetFeatureValue(CommonUsages.trigger, out float value) && value >= 0.55f)
                return true;

            return false;
        }

        public static bool IsGripHeld(XRNode node)
        {
            var device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid)
                return false;

            if (device.TryGetFeatureValue(CommonUsages.gripButton, out bool pressed) && pressed)
                return true;

            if (device.TryGetFeatureValue(CommonUsages.grip, out float value) && value >= 0.55f)
                return true;

            return false;
        }

        public static XRNode NodeForInteractor(XRBaseInteractor interactor)
        {
            if (interactor == null)
                return XRNode.RightHand;

            if (interactor.handedness == InteractorHandedness.Left
                || interactor.name.IndexOf("Left", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return XRNode.LeftHand;

            return XRNode.RightHand;
        }

        public static bool IsTriggerHeld(XRBaseInteractor interactor) =>
            IsTriggerHeld(NodeForInteractor(interactor));

        public static bool IsGripHeld(XRBaseInteractor interactor) =>
            IsGripHeld(NodeForInteractor(interactor));
    }
}
