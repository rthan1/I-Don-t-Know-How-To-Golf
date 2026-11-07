using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;

namespace Golf.Throwing
{
    /// <summary>
    /// Stops walking while throwing. Does not freeze look / controller aim —
    /// only zeros simulator translate speeds and disables locomotion providers.
    /// </summary>
    public static class PlayerLocomotionLock
    {
        static readonly List<Behaviour> DisabledBehaviours = new List<Behaviour>();

        static GameObject LocomotionRoot;
        static bool LocomotionRootWasActive;
        static CharacterController Character;
        static bool CharacterWasEnabled;

        static XRInteractionSimulator InteractionSimulator;
        static float SavedTranslateX;
        static float SavedTranslateY;
        static float SavedTranslateZ;
        static float SavedBodyMultiplier;
        static bool InteractionSpeedsSaved;

#pragma warning disable CS0618
        static XRDeviceSimulator DeviceSimulator;
        static float SavedDeviceX;
        static float SavedDeviceY;
        static float SavedDeviceZ;
        static bool DeviceSpeedsSaved;
#pragma warning restore CS0618

        static int LockCount;

        public static bool IsLocked => LockCount > 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            LockCount = 0;
            DisabledBehaviours.Clear();
            LocomotionRoot = null;
            Character = null;
            InteractionSimulator = null;
            InteractionSpeedsSaved = false;
            DeviceSimulator = null;
            DeviceSpeedsSaved = false;
        }

        public static void SetLocked(bool locked)
        {
            if (locked)
            {
                if (LockCount++ == 0)
                    ApplyLock();
            }
            else
            {
                if (LockCount <= 0)
                {
                    LockCount = 0;
                    return;
                }

                if (--LockCount == 0)
                    ReleaseLock();
            }
        }

        /// <summary>Always clears the lock, even if lock/unlock calls got out of sync.</summary>
        public static void ForceUnlock()
        {
            if (LockCount > 0)
                ReleaseLock();
            LockCount = 0;
        }

        static void ApplyLock()
        {
            DisabledBehaviours.Clear();

            var locomotion = FindTransform("Locomotion");
            if (locomotion != null)
            {
                LocomotionRoot = locomotion.gameObject;
                LocomotionRootWasActive = LocomotionRoot.activeSelf;
                LocomotionRoot.SetActive(false);
            }
            else
            {
                DisableAllLocomotionProviders();
            }

            var origin = Object.FindFirstObjectByType<XROrigin>();
            Transform originTransform = null;
            if (origin != null)
                originTransform = origin.Origin != null ? origin.Origin.transform : origin.transform;
            if (originTransform == null)
                originTransform = FindTransform("XR Origin (XR Rig)");

            if (originTransform != null)
            {
                Character = originTransform.GetComponent<CharacterController>();
                if (Character != null)
                {
                    CharacterWasEnabled = Character.enabled;
                    Character.enabled = false;
                }
            }

            InteractionSimulator = XRInteractionSimulator.instance
                                   ?? Object.FindFirstObjectByType<XRInteractionSimulator>(FindObjectsInactive.Include);

            if (InteractionSimulator != null)
            {
                SavedTranslateX = InteractionSimulator.translateXSpeed;
                SavedTranslateY = InteractionSimulator.translateYSpeed;
                SavedTranslateZ = InteractionSimulator.translateZSpeed;
                SavedBodyMultiplier = InteractionSimulator.bodyTranslateMultiplier;
                InteractionSpeedsSaved = true;

                InteractionSimulator.translateXSpeed = 0f;
                InteractionSimulator.translateYSpeed = 0f;
                InteractionSimulator.translateZSpeed = 0f;
                InteractionSimulator.bodyTranslateMultiplier = 0f;
            }
            else
            {
                InteractionSpeedsSaved = false;
            }

#pragma warning disable CS0618
            DeviceSimulator = XRDeviceSimulator.instance
                              ?? Object.FindFirstObjectByType<XRDeviceSimulator>(FindObjectsInactive.Include);
            if (DeviceSimulator != null)
            {
                SavedDeviceX = DeviceSimulator.keyboardXTranslateSpeed;
                SavedDeviceY = DeviceSimulator.keyboardYTranslateSpeed;
                SavedDeviceZ = DeviceSimulator.keyboardZTranslateSpeed;
                DeviceSpeedsSaved = true;

                DeviceSimulator.keyboardXTranslateSpeed = 0f;
                DeviceSimulator.keyboardYTranslateSpeed = 0f;
                DeviceSimulator.keyboardZTranslateSpeed = 0f;
            }
            else
            {
                DeviceSpeedsSaved = false;
            }
#pragma warning restore CS0618

        }

        static void ReleaseLock()
        {
            if (LocomotionRoot != null)
            {
                LocomotionRoot.SetActive(LocomotionRootWasActive);
                LocomotionRoot = null;
            }

            for (int i = 0; i < DisabledBehaviours.Count; i++)
            {
                if (DisabledBehaviours[i] != null)
                    DisabledBehaviours[i].enabled = true;
            }

            DisabledBehaviours.Clear();

            if (Character != null)
            {
                Character.enabled = CharacterWasEnabled;
                Character = null;
            }

            if (InteractionSimulator == null)
            {
                InteractionSimulator = XRInteractionSimulator.instance
                                       ?? Object.FindFirstObjectByType<XRInteractionSimulator>(FindObjectsInactive.Include);
            }

            if (InteractionSimulator != null && InteractionSpeedsSaved)
            {
                InteractionSimulator.translateXSpeed = SavedTranslateX > 0.01f ? SavedTranslateX : 1f;
                InteractionSimulator.translateYSpeed = SavedTranslateY > 0.01f ? SavedTranslateY : 1f;
                InteractionSimulator.translateZSpeed = SavedTranslateZ > 0.01f ? SavedTranslateZ : 1f;
                InteractionSimulator.bodyTranslateMultiplier = SavedBodyMultiplier > 0.01f ? SavedBodyMultiplier : 1f;
            }
            else if (InteractionSimulator != null)
            {
                if (InteractionSimulator.translateXSpeed <= 0.01f) InteractionSimulator.translateXSpeed = 1f;
                if (InteractionSimulator.translateYSpeed <= 0.01f) InteractionSimulator.translateYSpeed = 1f;
                if (InteractionSimulator.translateZSpeed <= 0.01f) InteractionSimulator.translateZSpeed = 1f;
                if (InteractionSimulator.bodyTranslateMultiplier <= 0.01f) InteractionSimulator.bodyTranslateMultiplier = 1f;
            }

#pragma warning disable CS0618
            if (DeviceSimulator == null)
            {
                DeviceSimulator = XRDeviceSimulator.instance
                                  ?? Object.FindFirstObjectByType<XRDeviceSimulator>(FindObjectsInactive.Include);
            }

            if (DeviceSimulator != null && DeviceSpeedsSaved)
            {
                DeviceSimulator.keyboardXTranslateSpeed = SavedDeviceX > 0.01f ? SavedDeviceX : 1f;
                DeviceSimulator.keyboardYTranslateSpeed = SavedDeviceY > 0.01f ? SavedDeviceY : 1f;
                DeviceSimulator.keyboardZTranslateSpeed = SavedDeviceZ > 0.01f ? SavedDeviceZ : 1f;
            }
#pragma warning restore CS0618

            InteractionSimulator = null;
            InteractionSpeedsSaved = false;
            DeviceSimulator = null;
            DeviceSpeedsSaved = false;
        }

        static void DisableAllLocomotionProviders()
        {
            var providers = Object.FindObjectsByType<LocomotionProvider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < providers.Length; i++)
            {
                if (!providers[i].enabled)
                    continue;
                providers[i].enabled = false;
                DisabledBehaviours.Add(providers[i]);
            }
        }

        static Transform FindTransform(string exactName)
        {
            var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == exactName)
                    return all[i];
            }

            return null;
        }
    }
}
