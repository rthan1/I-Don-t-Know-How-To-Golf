using UnityEngine;

/// <summary>
/// Switches a hole between its normal (terrible) green and the flat "pool table" green.
/// Each version has its own cup, CupTrigger and flagstick, so only the active one counts.
///
/// For the cheat, find this component and call:
///     FindFirstObjectByType&lt;GreenSwap&gt;().SetPoolTable(true);
/// </summary>
public class GreenSwap : MonoBehaviour
{
    [Tooltip("The normal, comically bad green (with its own cup and flag).")]
    public GameObject terribleGreen;

    [Tooltip("The perfectly flat pool table version (with its own cup and flag).")]
    public GameObject poolTable;

    [Tooltip("Start the hole with the pool table already active.")]
    public bool startAsPoolTable;

    [Tooltip("When the green swaps, move any ball sitting on it onto the new surface so it can't fall through.")]
    public bool keepBallsOnTop = true;

    public bool IsPoolTable => poolTable != null && poolTable.activeSelf;

    void Awake()
    {
        SetPoolTable(startAsPoolTable);
    }

    /// <summary>true = flat pool table, false = the terrible green.</summary>
    public void SetPoolTable(bool on)
    {
        if (terribleGreen != null) terribleGreen.SetActive(!on);
        if (poolTable != null) poolTable.SetActive(on);
        if (keepBallsOnTop && Application.isPlaying) PutBallsOnSurface(on ? poolTable : terribleGreen);
    }

    public void TogglePoolTable() => SetPoolTable(!IsPoolTable);

    /// Finds loose rigidbodies (the ball) over the green and sets them down on top of the new surface.
    /// (Public so the rising pool table can carry the ball up with it.)
    public void PutBallsOnSurface(GameObject surface)
    {
        if (surface == null) return;
        Physics.SyncTransforms();

        // Area covered by the green
        var renderers = surface.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        Bounds area = renderers[0].bounds;
        foreach (Renderer r in renderers) area.Encapsulate(r.bounds);
        area.Expand(new Vector3(0f, 4f, 0f));

        var surfaceColliders = surface.GetComponentsInChildren<Collider>();
        foreach (Collider nearby in Physics.OverlapBox(area.center, area.extents))
        {
            Rigidbody body = nearby.attachedRigidbody;
            if (body == null || body.isKinematic) continue;

            // Look straight down from above for the highest point of the new surface
            Vector3 from = new Vector3(body.position.x, area.max.y + 1f, body.position.z);
            float radius = nearby.bounds.extents.y;
            float top = float.NegativeInfinity;
            foreach (RaycastHit hit in Physics.RaycastAll(from, Vector3.down, area.size.y + 2f))
                if (System.Array.IndexOf(surfaceColliders, hit.collider) >= 0)
                    top = Mathf.Max(top, hit.point.y);

            // Only move it if it would otherwise end up inside or under the new surface
            if (!float.IsNegativeInfinity(top) && top + radius > body.position.y - 0.01f)
            {
                body.position = new Vector3(body.position.x, top + radius + 0.01f, body.position.z);
                body.WakeUp();
            }
        }
    }
}
