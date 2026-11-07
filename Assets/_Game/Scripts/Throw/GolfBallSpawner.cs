using Unity.XR.CoreUtils;
using UnityEngine;

namespace Golf.Throwing
{
    /// <summary>
    /// Spawns the GolfBall prefab in front of the player and wires cheat-throw pickup.
    /// </summary>
    public sealed class GolfBallSpawner : MonoBehaviour
    {
        const string PrefabAssetPath = "Assets/_Game/Prefabs/GolfBall.prefab";

        [SerializeField] GolfBall ballPrefab;
        [SerializeField] bool spawnOnStart = true;

        GolfBall _spawned;

        void Start()
        {
            PlayerLocomotionLock.ForceUnlock();
            ToggleSelectGrabSetup.EnsureApplied();

            if (spawnOnStart)
                Spawn();
        }

        [ContextMenu("Spawn Golf Ball")]
        public GolfBall Spawn()
        {
            if (_spawned != null)
                return _spawned;

            ballPrefab = ResolvePrefab();
            if (ballPrefab == null)
            {
                Debug.LogError($"[Golf] GolfBallSpawner could not find prefab at {PrefabAssetPath}.", this);
                return null;
            }

            Vector3 spawnPos = GetSpawnPositionInFrontOfPlayer();
            _spawned = Instantiate(ballPrefab, spawnPos, Quaternion.identity);
            _spawned.name = "GolfBall";
            CheatThrowBallSetup.EnsureOn(_spawned.gameObject);
            ToggleSelectGrabSetup.Apply();
            return _spawned;
        }

        static Vector3 GetSpawnPositionInFrontOfPlayer()
        {
            var origin = Object.FindFirstObjectByType<XROrigin>();
            if (origin != null)
            {
                Transform cam = origin.Camera != null ? origin.Camera.transform : null;
                Transform root = origin.Origin != null ? origin.Origin.transform : origin.transform;
                Vector3 forward = cam != null ? cam.forward : root.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.001f)
                    forward = root.forward;
                forward.Normalize();

                Vector3 feet = root.position;
                float y = feet.y + 0.08f;
                Vector3 from = cam != null ? cam.position : feet + Vector3.up * 1.4f;
                return new Vector3(from.x, y, from.z) + forward * 0.9f;
            }

            Camera main = Camera.main;
            if (main != null)
            {
                Vector3 forward = main.transform.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.001f)
                    forward = Vector3.forward;
                forward.Normalize();
                Vector3 p = main.transform.position + forward * 0.9f;
                p.y = main.transform.position.y - 1.2f;
                return p;
            }

            return new Vector3(0f, 0.12f, 0.9f);
        }

        GolfBall ResolvePrefab()
        {
            if (ballPrefab != null)
                return ballPrefab;

#if UNITY_EDITOR
            ballPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GolfBall>(PrefabAssetPath);
#endif
            if (ballPrefab == null)
                ballPrefab = Resources.Load<GolfBall>("GolfBall");

            return ballPrefab;
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (ballPrefab == null)
                ballPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GolfBall>(PrefabAssetPath);
        }
#endif
    }
}
