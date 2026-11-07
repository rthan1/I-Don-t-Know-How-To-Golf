using UnityEngine;

/// <summary>
/// Every game sound that isn't tied to a specific club, in one place.
/// The asset lives at Assets/_Game/Resources/SoundLibrary.asset; swap clips by dragging them into its slots.
/// The clips themselves live in Assets/_Game/Audio (not Resources), so only sounds referenced here ship in builds.
/// Club hit sounds stay on each ClubData asset, since they belong to the club.
/// </summary>
[CreateAssetMenu(fileName = "SoundLibrary", menuName = "Golf/Sound Library")]
public class SoundLibrary : ScriptableObject
{
    [Header("Security Cameras")]
    [Tooltip("Played when a cheat gets caught.")]
    public AudioClip caught;

    [Tooltip("Played when the cameras switch on.")]
    public AudioClip camerasOn;

    [Tooltip("Played when the cameras switch off.")]
    public AudioClip camerasOff;

    [Header("Cheating")]
    [Tooltip("Loops while any cheat is happening.")]
    public AudioClip heartbeat;

    [Tooltip("One of these plays at random on each cheat throw.")]
    public AudioClip[] throwGrunts;

    [Header("Hole")]
    [Tooltip("Played when the ball counts as holed.")]
    public AudioClip cupIn;

    static SoundLibrary s_Instance;

    /// <summary>The project's sound library (loaded once). Null only if the asset is missing.</summary>
    public static SoundLibrary Instance
    {
        get
        {
            if (s_Instance == null)
            {
                s_Instance = Resources.Load<SoundLibrary>("SoundLibrary");
                if (s_Instance == null)
                    Debug.LogWarning("SoundLibrary: no asset at Resources/SoundLibrary. Game sounds won't play.");
            }

            return s_Instance;
        }
    }
}
