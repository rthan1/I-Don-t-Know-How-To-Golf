using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Golf.Throwing
{
    /// <summary>
    /// Hold-to-grab: press select to grab, release to drop.
    /// Skips teleport interactors so locomotion select stays independent.
    /// Re-applies on every scene load for the new XR Origin.
    /// </summary>
    public static class ToggleSelectGrabSetup
    {
        static bool _applied;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Apply();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Apply();

        public static void Apply()
        {
            var interactors = Object.FindObjectsByType<XRBaseInputInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < interactors.Length; i++)
            {
                var interactor = interactors[i];
                if (interactor == null)
                    continue;

                if (interactor.gameObject.name.IndexOf("Teleport", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                interactor.selectActionTrigger = XRBaseInputInteractor.InputTriggerType.StateChange;
            }

            _applied = true;
        }

        public static void EnsureApplied()
        {
            if (!_applied)
                Apply();
        }
    }
}
