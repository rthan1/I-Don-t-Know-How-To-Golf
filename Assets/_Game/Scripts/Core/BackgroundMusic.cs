using UnityEngine;

// Plays the music from the SoundLibrary on a loop for the whole game.
// It creates itself when the game starts and survives scene loads, so the song keeps going
// from the menu through every hole without restarting.
public class BackgroundMusic : MonoBehaviour
{
    private static BackgroundMusic instance;
    private AudioSource source;

    // Clears the leftover instance when Play mode starts, in case Unity's domain reload is turned off.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null) return;

        SoundLibrary library = SoundLibrary.Instance;
        if (library == null || library.music == null) return;

        var musicObject = new GameObject("BackgroundMusic");
        DontDestroyOnLoad(musicObject);
        instance = musicObject.AddComponent<BackgroundMusic>();
    }

    private void Awake()
    {
        SoundLibrary library = SoundLibrary.Instance;

        source = gameObject.AddComponent<AudioSource>();
        source.clip = library.music;
        source.loop = true;
        source.volume = library.musicVolume;
        source.spatialBlend = 0f; // 2D: same volume no matter where the player is
        source.priority = 0;      // highest priority, so a burst of sound effects never cuts the music
        source.playOnAwake = false;
        source.Play();
    }

    // Reads the volume every frame, so dragging the slider on the SoundLibrary asset works live in Play mode.
    private void Update()
    {
        SoundLibrary library = SoundLibrary.Instance;
        if (library != null)
        {
            source.volume = library.musicVolume;
        }
    }
}
