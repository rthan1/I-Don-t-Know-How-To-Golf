using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// On a CupTrigger collider: finishes the hole only after the ball stays in the cup
/// and settles. A bounce that enters and comes back out does not count.
/// </summary>
[DisallowMultipleComponent]
public sealed class CupHoleTrigger : MonoBehaviour
{
    const float SettleSeconds = 0.4f;
    const float MaxSpeedToSettle = 0.45f;
    const float MaxHeightAboveTrigger = 0.1f;

    GolfBall _ballInside;
    float _settleTimer;
    bool _finished;

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
        if (_finished || !isActiveAndEnabled || _ballInside == null)
            return;

        if (StrokeManager.Instance != null && StrokeManager.Instance.HoleFinished)
        {
            _finished = true;
            return;
        }

        if (!IsBallSettledInCup(_ballInside))
        {
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

    bool IsBallSettledInCup(GolfBall ball)
    {
        if (ball == null)
            return false;

        // Must still be overlapping this cup trigger.
        var col = GetComponent<Collider>();
        var ballCol = ball.GetComponent<Collider>();
        if (col != null && ballCol != null && !col.bounds.Intersects(ballCol.bounds))
            return false;

        // Must be down in the cup, not skimming the rim.
        if (ball.transform.position.y > transform.position.y + MaxHeightAboveTrigger)
            return false;

        if (ball.IsAtRest)
            return true;

        Rigidbody body = ball.GetComponent<Rigidbody>();
        if (body == null)
            return false;

        return body.linearVelocity.magnitude <= MaxSpeedToSettle
               && body.angularVelocity.magnitude <= MaxSpeedToSettle * 4f;
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
    // wherever they're standing. This component is added at runtime, so the clip comes from Resources.
    void PlayCupSound()
    {
        AudioClip clip = Resources.Load<AudioClip>("Audio/Cup_In");
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
