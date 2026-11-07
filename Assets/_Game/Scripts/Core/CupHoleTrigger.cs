using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// On a CupTrigger collider: when the golf ball enters, finish the hole.
/// Wired onto every CupTrigger at runtime so existing hole scenes don't need a rebuild.
/// </summary>
[DisallowMultipleComponent]
public sealed class CupHoleTrigger : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        WireAllCupsInScene();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => WireAllCupsInScene();

    static void WireAllCupsInScene()
    {
        var all = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t == null || t.name != "CupTrigger")
                continue;

            if (t.GetComponent<CupHoleTrigger>() == null)
                t.gameObject.AddComponent<CupHoleTrigger>();

            var col = t.GetComponent<Collider>();
            if (col != null)
                col.isTrigger = true;
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (!isActiveAndEnabled)
            return;

        GolfBall ball = ResolveBall(other);
        if (ball == null)
            return;

        if (StrokeManager.Instance == null)
            return;

        StrokeManager.Instance.FinishHole();
    }

    static GolfBall ResolveBall(Collider other)
    {
        if (other == null)
            return null;

        GolfBall ball = other.GetComponentInParent<GolfBall>();
        if (ball != null)
            return ball;

        if (other.attachedRigidbody != null)
            return other.attachedRigidbody.GetComponent<GolfBall>();

        return null;
    }
}
