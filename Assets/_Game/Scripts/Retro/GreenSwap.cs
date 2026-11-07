using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

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

    bool started; // clubs are only adjusted after the scene has started, never on the initial setup

    void Awake()
    {
        SetPoolTable(startAsPoolTable);
    }

    void Start() => started = true;

    /// <summary>true = flat pool table, false = the terrible green.</summary>
    public void SetPoolTable(bool on)
    {        if (terribleGreen != null) terribleGreen.SetActive(!on);
        if (poolTable != null) poolTable.SetActive(on);
        if (keepBallsOnTop && Application.isPlaying)
        {
            PutBallsOnSurface(on ? poolTable : terribleGreen);
            // Going back to the normal green: a ball or club left on the (now gone) table would hang in mid-air.
            if (!on)
            {
                DropRestingBallsToGround();
                SettleLooseClubs(allowDown: true);
            }
        }

        // The club is a pool cue while the table is up (after the ball has moved, so a held club's
        // stance lines up with where the ball actually is).
        if (Application.isPlaying)
            foreach (ClubSwitcher club in FindObjectsByType<ClubSwitcher>(FindObjectsSortMode.None))
                club.SetCueMode(on);    }

    /// Drops every resting ball onto whatever is under it now. A ball in the player's hand stays put,
    /// but its saved rest spot still gets fixed, so cancelling the throw doesn't put it back in the air.
    void DropRestingBallsToGround()
    {
        Physics.SyncTransforms();
        bool ballInHand = ClubSwitcher.IsAnyBallHeldOrThrowing();
        foreach (GolfBall ball in FindObjectsByType<GolfBall>(FindObjectsSortMode.None))
            ball.DropToGround(restPositionOnly: ballInHand);
    }

    /// A dropped club is kinematic with no gravity, so it stays exactly where it was let go.
    /// This sets any club that isn't in a hand onto the surface under it: lifted when the table rises
    /// into it, and (allowDown) lowered when the table it was lying on goes away.
    void SettleLooseClubs(bool allowDown)
    {
        if (!started) return;
        Transform player = Camera.main != null ? Camera.main.transform.root : null;

        foreach (ClubSwitcher club in FindObjectsByType<ClubSwitcher>(FindObjectsSortMode.None))
        {
            var grab = club.GetComponent<XRGrabInteractable>();
            if (grab != null && grab.isSelected) continue;

            // The club's footprint, from what you can see (shaft, head, model). Its only collider is a
            // short capsule at the grip, so measuring colliders would leave the head under the surface.
            var renderers = club.GetComponentsInChildren<Renderer>();
            bool any = false;
            Bounds bounds = default;
            foreach (Renderer r in renderers)
            {
                if (!r.enabled) continue;
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            if (!any) continue;

            // Highest surface under any part of the club (its middle, plus the middle of each piece,
            // so the grip end and head end both get checked), ignoring the club, the player and balls.
            float top = SurfaceTopBelow(bounds.center, bounds.max.y, club.transform, player);
            foreach (Renderer r in renderers)
                if (r.enabled) top = Mathf.Max(top, SurfaceTopBelow(r.bounds.center, bounds.max.y, club.transform, player));
            if (float.IsNegativeInfinity(top)) continue;

            float shift = top + 0.005f - bounds.min.y;     // + up into place, - down onto the surface
            if (Mathf.Abs(shift) < 0.005f || (shift < 0f && !allowDown)) continue;

            Vector3 moved = club.transform.position + Vector3.up * shift;
            club.transform.position = moved;
            var body = club.GetComponent<Rigidbody>();
            if (body != null) body.position = moved;
        }
    }

    // Highest solid surface straight below a point, starting a bit above the club.
    static float SurfaceTopBelow(Vector3 point, float clubTop, Transform club, Transform player)
    {
        Vector3 from = new Vector3(point.x, clubTop + 3f, point.z);
        float top = float.NegativeInfinity;
        foreach (RaycastHit hit in Physics.RaycastAll(from, Vector3.down, 300f, ~0, QueryTriggerInteraction.Ignore))
        {
            Transform t = hit.collider.transform;
            if (t.IsChildOf(club)) continue;
            if (player != null && t.IsChildOf(player)) continue;
            if (hit.rigidbody != null && hit.rigidbody.GetComponent<GolfBall>() != null) continue;
            top = Mathf.Max(top, hit.point.y);
        }
        return top;
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
        // Solid colliders only: the ball also has a big invisible grab-zone trigger (0.25 m), and using
        // that as its size would set the ball down floating well above the felt.
        foreach (Collider nearby in Physics.OverlapBox(area.center, area.extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
        {
            Rigidbody body = nearby.attachedRigidbody;
            if (body == null || body.isKinematic || nearby.isTrigger) continue;

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

        // Clubs are kinematic (skipped above), so lift any lying here up with the surface too.
        SettleLooseClubs(allowDown: false);
    }
}
