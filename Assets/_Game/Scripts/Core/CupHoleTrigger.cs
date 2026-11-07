using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// On a CupTrigger collider: finishes the hole only after the ball stays in the cup
/// and settles. A bounce that enters and comes back out does not count.
/// </summary>
[DisallowMultipleComponent]
public sealed class CupHoleTrigger : MonoBehaviour
{
    const float SettleSeconds = 0.35f;
    const float MaxSpeedToSettle = 0.55f;
    // Trigger sits near the cup floor; allow the full cup depth plus a little rim slop
    // (Hole 3's uneven groundhog green is especially unforgiving here).
    const float MaxHeightAboveTrigger = 0.22f;
    // Mesh cup radius is 0.1; keep the trigger at least that wide so a ball
    // resting against the cup wall still counts.
    const float MinTriggerRadius = 0.12f;

    GolfBall _ballInside;
    float _settleTimer;
    bool _finished;
    float _triggerRadius = MinTriggerRadius;

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

            if (col is SphereCollider sphere)
                sphere.radius = Mathf.Max(sphere.radius, MinTriggerRadius);
        }
    }

    void Awake()
    {
        var sphere = GetComponent<SphereCollider>();
        if (sphere != null)
        {
            sphere.radius = Mathf.Max(sphere.radius, MinTriggerRadius);
            _triggerRadius = sphere.radius * MaxAbsScale(transform);
        }
        else
        {
            _triggerRadius = MinTriggerRadius;
        }
    }

    void OnDisable()
    {
        ClearTracking();
    }

    void OnTriggerEnter(Collider other)
    {
        if (!isActiveAndEnabled || _finished)
            return;

        GolfBall ball = ResolveBall(other);
        if (ball == null)
            return;

        _ballInside = ball;
        _settleTimer = 0f;
    }

    void OnTriggerExit(Collider other)
    {
        GolfBall ball = ResolveBall(other);
        if (ball == null || ball != _ballInside)
            return;

        ClearTracking();
    }

    void FixedUpdate()
    {
        if (_finished || !isActiveAndEnabled)
            return;

        if (StrokeManager.Instance != null && StrokeManager.Instance.HoleFinished)
        {
            _finished = true;
            return;
        }

        // Balls that come to rest against the cup wall can miss a clean trigger enter
        // on uneven greens (Hole 3); pick them up from an overlap check.
        if (_ballInside == null)
            TryAcquireBallFromOverlap();

        if (_ballInside == null)
            return;

        if (!IsBallSettledInCup(_ballInside))
        {
            // Still overlapping but not settled yet — keep tracking.
            if (!IsBallInCupVolume(_ballInside))
                ClearTracking();
            else
                _settleTimer = 0f;
            return;
        }

        _settleTimer += Time.fixedDeltaTime;
        if (_settleTimer < SettleSeconds)
            return;

        _finished = true;
        HoldBallInCup(_ballInside);
        ClearTracking();
        PlayCupSound();

        if (StrokeManager.Instance != null)
            StrokeManager.Instance.FinishHole();
    }

    void TryAcquireBallFromOverlap()
    {
        Collider[] hits = Physics.OverlapSphere(
            transform.position,
            _triggerRadius,
            ~0,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hits.Length; i++)
        {
            GolfBall ball = ResolveBall(hits[i]);
            if (ball == null || !IsBallInCupVolume(ball))
                continue;

            _ballInside = ball;
            _settleTimer = 0f;
            return;
        }
    }

    bool IsBallSettledInCup(GolfBall ball)
    {
        if (!IsBallInCupVolume(ball))
            return false;

        if (ball.IsAtRest)
            return true;

        Rigidbody body = ball.GetComponent<Rigidbody>();
        if (body == null)
            return false;

        return body.linearVelocity.magnitude <= MaxSpeedToSettle
               && body.angularVelocity.magnitude <= MaxSpeedToSettle * 4f;
    }

    bool IsBallInCupVolume(GolfBall ball)
    {
        if (ball == null)
            return false;

        Vector3 delta = ball.transform.position - transform.position;
        if (delta.y > MaxHeightAboveTrigger || delta.y < -0.05f)
            return false;

        delta.y = 0f;
        float maxDist = _triggerRadius + 0.02f;
        return delta.sqrMagnitude <= maxDist * maxDist;
    }

    void HoldBallInCup(GolfBall ball)
    {
        if (ball == null)
            return;

        // Sit slightly above the trigger center so it rests on the cup floor visually.
        Vector3 rest = transform.position;
        rest.y += 0.02f;
        ball.ForceRestAt(rest);
    }

    // Plays when the ball counts as in (not on every rim bounce). 2D, so the player hears it
    // wherever they're standing. The clip comes from the SoundLibrary asset.
    void PlayCupSound()
    {
        AudioClip clip = SoundLibrary.Instance != null ? SoundLibrary.Instance.cupIn : null;
        if (clip == null)
            return;

        var source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.PlayOneShot(clip);
    }

    void ClearTracking()
    {
        _ballInside = null;
        _settleTimer = 0f;
    }

    static float MaxAbsScale(Transform t)
    {
        Vector3 s = t.lossyScale;
        return Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
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
