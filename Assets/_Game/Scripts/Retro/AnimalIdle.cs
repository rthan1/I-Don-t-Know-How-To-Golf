using UnityEngine;

/// <summary>
/// Makes a decorative animal look alive: every so often it hops, and it snaps to look in a new direction now
/// and then. Moves in choppy steps, like an 8-bit sprite. Purely visual; no physics.
/// </summary>
public class AnimalIdle : MonoBehaviour
{
    [Tooltip("How high it hops, in meters (0 = never hops).")]
    public float hopHeight = 0.12f;

    [Tooltip("Seconds between hops.")]
    public float hopEvery = 3f;

    [Tooltip("Seconds between looking around.")]
    public float lookEvery = 4f;

    [Tooltip("How far it turns when it looks around, in degrees.")]
    public float lookAngle = 45f;

    [Tooltip("Animation frames per second. Low numbers feel more 8-bit.")]
    public float framesPerSecond = 10f;

    Vector3 basePosition;
    float baseYaw;
    float offset;
    int lastLook = -1;
    float lookYaw;

    void Awake()
    {
        basePosition = transform.localPosition;
        baseYaw = transform.localEulerAngles.y;
        offset = Random.value * 10f;
    }

    void Update()
    {
        float time = Mathf.Floor((Time.time + offset) * framesPerSecond) / framesPerSecond;

        // Hop: a quick up-and-down at the start of each hop cycle
        float y = 0f;
        if (hopHeight > 0f && hopEvery > 0f)
        {
            float phase = Mathf.Repeat(time, hopEvery);
            const float hopTime = 0.35f;
            if (phase < hopTime) y = Mathf.Sin(phase / hopTime * Mathf.PI) * hopHeight;
        }
        transform.localPosition = basePosition + Vector3.up * y;

        // Look around: snap to a new direction every so often
        if (lookEvery > 0f)
        {
            int look = Mathf.FloorToInt(time / lookEvery);
            if (look != lastLook)
            {
                lastLook = look;
                lookYaw = Random.Range(-lookAngle, lookAngle);
                transform.localRotation = Quaternion.Euler(0f, baseYaw + lookYaw, 0f);
            }
        }
    }
}
