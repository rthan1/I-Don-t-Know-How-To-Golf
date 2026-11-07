using UnityEngine;

/// <summary>
/// A puff of smoke that rises, grows, then shrinks away and starts again from the bottom.
/// Moves in small steps for a choppy 8-bit feel. Purely visual; no physics.
/// </summary>
public class SmokePuff : MonoBehaviour
{
    [Tooltip("How fast the puff rises, in meters per second.")]
    public float riseSpeed = 3f;

    [Tooltip("How high the puff rises before starting over, in meters.")]
    public float riseHeight = 20f;

    [Tooltip("How big the puff gets at the top, relative to its starting size.")]
    public float growth = 1.8f;

    [Tooltip("Animation steps per second. Low numbers feel more 8-bit.")]
    public float framesPerSecond = 8f;

    Vector3 start;
    Vector3 baseScale;
    Vector3 drift;
    float offset;

    void Awake()
    {
        start = transform.localPosition;
        baseScale = transform.localScale;
        offset = Random.value;
        drift = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)) * 3f;
    }

    void Update()
    {
        float time = Mathf.Floor(Time.time * framesPerSecond) / framesPerSecond;
        float cycle = riseHeight / Mathf.Max(riseSpeed, 0.01f);
        float f = Mathf.Repeat(time / cycle + offset, 1f); // 0 = bottom, 1 = top

        transform.localPosition = start + Vector3.up * (f * riseHeight) + drift * f;
        float fade = f > 0.8f ? Mathf.InverseLerp(1f, 0.8f, f) : 1f; // shrink away near the top
        transform.localScale = baseScale * Mathf.Lerp(0.6f, growth, f) * fade;
    }
}
