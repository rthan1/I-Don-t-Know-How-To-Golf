using System.Collections;
using UnityEngine;

/// <summary>
/// Makes the pool table rise up out of the ground when it appears, in choppy 8-bit steps with a little rumble.
/// Anyone standing on that spot rides up with it (GroundFollow keeps the player on top of whatever is under them),
/// and a ball sitting there gets carried up too.
/// </summary>
public class PoolTableRise : MonoBehaviour
{
    [Tooltip("How far below its final spot the table starts, in meters.")]
    public float riseDistance = 2.5f;

    [Tooltip("How long the rise takes, in seconds.")]
    public float duration = 2f;

    [Tooltip("Animation steps per second. Low numbers feel more 8-bit.")]
    public float framesPerSecond = 12f;

    [Tooltip("How much the table shakes side to side while it rises, in meters.")]
    public float rumble = 0.04f;

    Vector3 restPosition;
    bool hasRestPosition;
    GreenSwap swap;

    void Awake()
    {
        restPosition = transform.localPosition;
        hasRestPosition = true;
        swap = GetComponentInParent<GreenSwap>();
    }

    void OnEnable()
    {
        if (!hasRestPosition) Awake();
        if (!Application.isPlaying) return;
        StopAllCoroutines();
        StartCoroutine(Rise());
    }

    void OnDisable()
    {
        if (hasRestPosition) transform.localPosition = restPosition;
    }

    IEnumerator Rise()
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            // Snap to whole animation frames, ease out at the top
            float stepped = Mathf.Floor(elapsed * framesPerSecond) / framesPerSecond;
            float f = Mathf.Clamp01(stepped / duration);
            f = 1f - (1f - f) * (1f - f);
            float shake = (1f - f) * rumble;
            var jitter = new Vector3(Random.Range(-shake, shake), 0f, Random.Range(-shake, shake));
            transform.localPosition = restPosition + Vector3.down * (riseDistance * (1f - f)) + jitter;

            // Carry a ball sitting here up with the table
            if (swap != null) swap.PutBallsOnSurface(gameObject);
            yield return null;
        }
        transform.localPosition = restPosition;
        if (swap != null) swap.PutBallsOnSurface(gameObject);
    }
}
