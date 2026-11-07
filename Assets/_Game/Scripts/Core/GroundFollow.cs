using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Keeps the player standing on the ground when they walk around physically (or with the XR simulator's WASD).
///
/// Thumbstick walking already stays on the ground, but walking with your actual body (or the simulator) moves
/// your head directly, so on our hilly courses you'd walk straight into a slope. This moves the play area's floor
/// up and down every frame so it always matches the ground right under your head.
///
/// It attaches itself to the player rig automatically in every scene; nothing to set up.
/// </summary>
public class GroundFollow : MonoBehaviour
{
    [Tooltip("How far above the head to start looking down for the ground (meters).")]
    public float probeHeight = 3f;

    [Tooltip("How far below the floor to look for the ground (meters).")]
    public float maxDrop = 60f;

    Transform head;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoAttach()
    {
        AttachToPlayer();
        SceneManager.sceneLoaded += (scene, mode) => AttachToPlayer();
    }

    /// Finds the player rig (the root of the VR camera) and adds this component if it isn't there yet.
    static void AttachToPlayer()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        Transform rig = cam.transform.root;
        if (rig == cam.transform || rig.GetComponent<GroundFollow>() != null) return;
        if (rig.GetComponent<CharacterController>() == null) return; // only the VR player rig (it walks with one)
        rig.gameObject.AddComponent<GroundFollow>().head = cam.transform;
    }

    void LateUpdate()
    {
        if (head == null)
        {
            if (Camera.main == null) return;
            head = Camera.main.transform;
        }

        // Look straight down from above the head for the highest ground surface
        var from = new Vector3(head.position.x, head.position.y + probeHeight, head.position.z);
        float ground = float.NegativeInfinity;
        foreach (RaycastHit hit in Physics.RaycastAll(from, Vector3.down, probeHeight + maxDrop + head.localPosition.y,
                                                       ~0, QueryTriggerInteraction.Ignore))
        {
            // Ground only: the terrain and greens are solid mesh colliders. This skips the ball, barrels, trees,
            // and anything that's part of the player.
            if (!(hit.collider is MeshCollider mesh) || mesh.convex) continue;
            if (hit.collider.attachedRigidbody != null) continue;
            if (hit.collider.transform.IsChildOf(transform)) continue;
            ground = Mathf.Max(ground, hit.point.y);
        }
        if (float.IsNegativeInfinity(ground)) return;

        Vector3 position = transform.position;
        if (Mathf.Abs(position.y - ground) < 0.002f) return;
        position.y = ground;
        transform.position = position;
        Physics.SyncTransforms(); // let the character controller know we moved it
    }
}
