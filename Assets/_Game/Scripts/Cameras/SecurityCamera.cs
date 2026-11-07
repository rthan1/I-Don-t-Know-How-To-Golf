using UnityEngine;

/// <summary>
/// One security camera prop. Purely visual: shows its red light while the hole's cameras are on.
/// All cameras on a hole share the SecurityCameraSystem's timer, so they always switch together.
///
/// Give it a "light on" object (the glowing red light) and optionally a "light off" object (the dark light),
/// and this turns them on and off. The hole builder sets these up.
/// </summary>
public class SecurityCamera : MonoBehaviour
{
    [Tooltip("Shown while the cameras are on (the glowing red light).")]
    public GameObject lightOn;

    [Tooltip("Shown while the cameras are off (the black, unlit light). Optional.")]
    public GameObject lightOff;

    void OnEnable()
    {
        SecurityCameraSystem.CamerasSwitched += Show;
        var system = SecurityCameraSystem.Instance;
        Show(system != null && system.CamerasOn);
    }

    void OnDisable()
    {
        SecurityCameraSystem.CamerasSwitched -= Show;
    }

    void Show(bool on)
    {
        if (lightOn != null) lightOn.SetActive(on);
        if (lightOff != null) lightOff.SetActive(!on);
    }
}
