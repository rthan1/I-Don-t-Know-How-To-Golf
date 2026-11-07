using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Anything the security cameras can catch. Cheats register themselves with
/// SecurityCameraSystem.Register in OnEnable and Unregister in OnDisable.
/// </summary>
public interface ICatchableCheat
{
    /// <summary>Is the cheat happening right now? (Mid cheat-throw, pool table up, ...)</summary>
    bool IsCheating { get; }

    /// <summary>A short name for messages and logs, e.g. "Cheat Throw".</summary>
    string CheatName { get; }

    /// <summary>Called when the cameras catch this cheat. Undo it here.</summary>
    void OnCaught();
}

/// <summary>
/// One per hole. Every security camera on the hole runs off this one shared timer:
/// the cameras are off for a while, then on for a while, over and over.
/// If any cheat is happening while the cameras are on, you're caught: the cheat is undone and onCaught fires.
///
/// The SecurityCamera objects around the hole are just the visuals (their red light shows when this is on).
/// </summary>
public class SecurityCameraSystem : MonoBehaviour
{
    [Header("Timing (seconds)")]
    [Tooltip("How long the cameras stay off. Make it long enough to fit a cheat throw comfortably.")]
    [Min(0.1f)] public float offDuration = 10f;

    [Tooltip("How long the cameras stay on.")]
    [Min(0.1f)] public float onDuration = 5f;

    [Tooltip("Right after the cameras switch on, cheating doesn't count for this long. Covers a cheat that ends right on the edge.")]
    [Min(0f)] public float graceTime = 0.5f;

    [Tooltip("Start the hole with the cameras off, so the player gets a window right away.")]
    public bool startOff = true;

    [Header("Sound")]
    [Tooltip("Played when a cheat gets caught. Leave empty to use the default \"Hey!\" (Resources/Audio/Caught_Hey).")]
    public AudioClip caughtSound;

    [Tooltip("Volume of the caught sound, 0 to 1.")]
    [Range(0f, 1f)] public float caughtVolume = 1f;

    [Tooltip("Played when the cameras switch on. Leave empty to use the default (Resources/Audio/Camera_On).")]
    public AudioClip camerasOnSound;

    [Tooltip("Played when the cameras switch off. Leave empty to use the default (Resources/Audio/Camera_Off).")]
    public AudioClip camerasOffSound;

    [Tooltip("Volume of the camera on/off sounds, 0 to 1.")]
    [Range(0f, 1f)] public float switchVolume = 0.8f;

    [Header("Events")]
    [Tooltip("Fires when a cheat gets caught on camera. The cheat has already been undone. Hook the stroke penalty, siren, etc. up here.")]
    public UnityEvent onCaught;

    [Tooltip("Fires when the cameras switch on.")]
    public UnityEvent onCamerasOn;

    [Tooltip("Fires when the cameras switch off.")]
    public UnityEvent onCamerasOff;

    /// <summary>The system on the current hole (null if the hole has no cameras).</summary>
    public static SecurityCameraSystem Instance { get; private set; }

    /// <summary>Fires whenever the cameras on any hole switch on (true) or off (false).</summary>
    public static event Action<bool> CamerasSwitched;

    /// <summary>Fires when a cheat gets caught, with the cheat that was caught (for code that wants to know which one).</summary>
    public static event Action<ICatchableCheat> CheatCaught;

    /// <summary>Are the cameras on right now (red light showing)?</summary>
    public bool CamerasOn { get; private set; }

    /// <summary>Seconds until the cameras next switch on or off.</summary>
    public float TimeUntilSwitch => Mathf.Max(0f, (CamerasOn ? onDuration : offDuration) - phaseTime);

    static readonly List<ICatchableCheat> cheats = new List<ICatchableCheat>();

    float phaseTime;
    AudioSource audioSource;

    // Clears leftovers from the last Play session (in case domain reload is turned off)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        cheats.Clear();
        Instance = null;
        CamerasSwitched = null;
        CheatCaught = null;
    }

    public static void Register(ICatchableCheat cheat)
    {
        if (cheat != null && !cheats.Contains(cheat)) cheats.Add(cheat);
    }

    public static void Unregister(ICatchableCheat cheat) => cheats.Remove(cheat);

    /// <summary>Is any registered cheat happening right now? (Works on holes without cameras too.)</summary>
    public static bool AnyCheating
    {
        get
        {
            foreach (ICatchableCheat cheat in cheats)
            {
                if (cheat == null || (cheat is UnityEngine.Object obj && obj == null)) continue;
                if (cheat.IsCheating) return true;
            }
            return false;
        }
    }

    void OnEnable()
    {
        if (Instance != null && Instance != this)
            Debug.LogWarning("SecurityCameraSystem: more than one in the scene. Only the newest one is used.", this);
        Instance = this;

        phaseTime = 0f;
        SetCameras(!startOff, force: true);
    }

    void OnDisable()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        phaseTime += Time.deltaTime;
        if (phaseTime >= (CamerasOn ? onDuration : offDuration))
        {
            phaseTime = 0f;
            SetCameras(!CamerasOn);
        }

        if (CamerasOn && phaseTime >= graceTime)
            CheckForCheats();
    }

    void CheckForCheats()
    {
        // Copy, because OnCaught can unregister cheats
        foreach (ICatchableCheat cheat in cheats.ToArray())
        {
            if (cheat == null || (cheat is UnityEngine.Object obj && obj == null)) { cheats.Remove(cheat); continue; }
            if (!cheat.IsCheating) continue;
            Catch(cheat);
        }
    }

    void Catch(ICatchableCheat cheat)
    {
        Debug.Log("Caught on camera: " + cheat.CheatName);
        cheat.OnCaught();
        PlayCaughtSound();
        onCaught?.Invoke();
        CheatCaught?.Invoke(cheat);
    }

    void PlayCaughtSound()
    {
        if (caughtSound == null) caughtSound = Resources.Load<AudioClip>("Audio/Caught_Hey");
        PlaySound(caughtSound, caughtVolume);
    }

    void PlaySwitchSound(bool on)
    {
        if (on)
        {
            if (camerasOnSound == null) camerasOnSound = Resources.Load<AudioClip>("Audio/Camera_On");
            PlaySound(camerasOnSound, switchVolume);
        }
        else
        {
            if (camerasOffSound == null) camerasOffSound = Resources.Load<AudioClip>("Audio/Camera_Off");
            PlaySound(camerasOffSound, switchVolume);
        }
    }

    // 2D (not from a camera's position), so the player hears it clearly wherever they are.
    void PlaySound(AudioClip clip, float volume)
    {
        if (clip == null) return;

        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
        }

        audioSource.PlayOneShot(clip, volume);
    }

    void SetCameras(bool on, bool force = false)
    {
        if (!force && on == CamerasOn) return;
        CamerasOn = on;
        if (!force) PlaySwitchSound(on); // not on the initial setup when the hole starts
        if (on) onCamerasOn?.Invoke();
        else onCamerasOff?.Invoke();
        CamerasSwitched?.Invoke(on);
    }
}
