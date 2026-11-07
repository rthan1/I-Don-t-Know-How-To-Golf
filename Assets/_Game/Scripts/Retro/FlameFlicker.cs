using UnityEngine;

/// <summary>
/// Makes a retro flame flicker by stretching and squashing it in a few choppy steps per second,
/// like an 8-bit sprite animation. Purely visual; no physics.
/// </summary>
public class FlameFlicker : MonoBehaviour
{
    [Tooltip("Animation frames per second. Low numbers feel more 8-bit.")]
    public float framesPerSecond = 10f;

    [Tooltip("How much the flame stretches up and down (0.25 = 25%).")]
    public float amount = 0.25f;

    Vector3 baseScale;
    float seed;

    void Awake()
    {
        baseScale = transform.localScale;
        seed = Random.value * 100f;
    }

    void Update()
    {
        // Snap time to whole frames for a choppy retro look
        float frame = Mathf.Floor(Time.time * framesPerSecond + seed);
        float wave = Mathf.Sin(frame * 2.1f) * 0.6f + Mathf.Sin(frame * 3.7f + 1.3f) * 0.4f;
        float stretch = 1f + amount * wave;
        float squash = 1f / Mathf.Sqrt(stretch);
        transform.localScale = new Vector3(baseScale.x * squash, baseScale.y * stretch, baseScale.z * squash);
    }
}
