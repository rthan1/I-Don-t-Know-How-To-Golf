using UnityEngine;

// One of these assets exists per club (Driver, Iron, Putter).
// It only holds numbers and references. The ClubHead script reads them when a hit happens.
// Create one in the Project window: right-click > Create > Golf > Club Data.
[CreateAssetMenu(fileName = "NewClub", menuName = "Golf/Club Data")]
public class ClubData : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Name shown on the debug readout, e.g. Driver.")]
    public string clubName = "New Club";

    [Tooltip("The 3D model shown in the player's hand for this club.")]
    public GameObject visualPrefab;

    [Header("Shape")]
    [Tooltip("Distance from the grip to the club head, in meters.")]
    [Min(0.1f)] public float shaftLength = 1.0f;

    [Tooltip("Length of the club face from heel to toe, in meters. Wider = more forgiving side to side.")]
    [Min(0.02f)] public float faceWidth = 0.08f;

    [Tooltip("Half the thickness of the club face, in meters. Bigger = more forgiving up and down.")]
    [Min(0.005f)] public float faceThickness = 0.015f;

    [Header("Launch")]
    [Tooltip("Launch angle above horizontal, in degrees. Low for driver, high for iron, near 0 for putter.")]
    [Range(0f, 60f)] public float loftDegrees = 10f;

    [Tooltip("Backspin lift. Higher = the ball climbs more and carries farther before dropping. 0 = plain arc (putter).")]
    [Min(0f)] public float liftStrength = 0f;

    [Tooltip("Maps swing speed (m/s, horizontal axis) to ball launch speed (m/s, vertical axis).")]
    public AnimationCurve speedCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(15f, 15f));

    [Tooltip("Swings slower than this (m/s) don't count as a hit.")]
    [Min(0f)] public float minSwingSpeed = 2f;

    [Tooltip("Ball speed never goes above this (m/s), no matter how hard the swing.")]
    [Min(0f)] public float maxBallSpeed = 18f;

    [Tooltip("How much the club face direction bends the shot. 0 = follow the swing path only, 1 = follow the face only.")]
    [Range(0f, 1f)] public float faceAimInfluence = 0f;

    [Header("Contact")]
    [Tooltip("Power you keep when the ball is hit at the very toe or heel (1 = no penalty, 0.5 = half power).")]
    [Range(0f, 1f)] public float edgePower = 0.7f;

    [Tooltip("How far (degrees) a hit at the very toe or heel gets pushed to the side.")]
    [Range(0f, 30f)] public float edgeCurveDegrees = 6f;

    [Header("Feel")]
    [Tooltip("Controller vibration strength on hit, 0 to 1.")]
    [Range(0f, 1f)] public float hapticAmplitude = 0.7f;

    [Tooltip("Controller vibration length on hit, in seconds.")]
    [Min(0f)] public float hapticDuration = 0.1f;

    [Header("Sound")]
    [Tooltip("Played at the club head when this club hits the ball.")]
    public AudioClip hitSound;

    [Tooltip("Volume of a full-power hit, 0 to 1. Softer hits play quieter than this.")]
    [Range(0f, 1f)] public float hitVolume = 1f;

    // Turns a measured swing speed into a ball launch speed.
    // Returns 0 if the swing was too slow to count.
    public float GetBallSpeed(float swingSpeed)
    {
        if (swingSpeed < minSwingSpeed)
        {
            return 0f;
        }

        float ballSpeed = speedCurve.Evaluate(swingSpeed);
        return Mathf.Clamp(ballSpeed, 0f, maxBallSpeed);
    }
}
