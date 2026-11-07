using Golf.Throwing;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The start screen: press a button and you're dropped into the first hole.
///
/// Default buttons: A or the trigger on either Quest controller, or Enter / Space on the keyboard.
/// Change them in the Inspector under "Start Button".
///
/// Golf > Build Menu sets this up in the Menu scene.
/// </summary>
public class MainMenu : MonoBehaviour
{
    [Tooltip("Scene to load when the player starts. It must be in File > Build Profiles (Golf > Build Menu adds it).")]
    public string firstHoleScene = "RetroHole1";

    [Tooltip("Button(s) that start the game. If none are set, it uses A / trigger on the controllers and Enter / Space on the keyboard.")]
    public InputAction startButton = new InputAction("StartGame", InputActionType.Button);

    [Tooltip("The \"PRESS A TO START\" text. It blinks, like an old console title screen.")]
    public Text prompt;

    [Tooltip("Blinks per second for the prompt.")]
    public float blinkRate = 1.5f;

    [Tooltip("Ignore presses for this long after the menu opens, so a button still held from before doesn't skip it.")]
    public float inputDelay = 0.5f;

    bool starting;

    void Awake()
    {
        if (startButton.bindings.Count == 0)
        {
            startButton.AddBinding("<XRController>{RightHand}/primaryButton"); // A
            startButton.AddBinding("<XRController>{LeftHand}/primaryButton");  // X
            startButton.AddBinding("<XRController>{RightHand}/triggerPressed");
            startButton.AddBinding("<XRController>{LeftHand}/triggerPressed");
            startButton.AddBinding("<Keyboard>/enter");
            startButton.AddBinding("<Keyboard>/space");
            startButton.AddBinding("<Gamepad>/start");
        }
    }

    void Start()
    {
        // Stand still on the menu: no walking or turning off the little platform
        PlayerLocomotionLock.SetLocked(true);
    }

    void OnEnable()
    {
        startButton.performed += OnStartPressed;
        startButton.Enable();
    }

    void OnDisable()
    {
        startButton.performed -= OnStartPressed;
        startButton.Disable();
    }

    void Update()
    {
        if (prompt != null)
            prompt.enabled = starting || Mathf.Repeat(Time.timeSinceLevelLoad * blinkRate, 1f) < 0.65f;
    }

    void OnStartPressed(InputAction.CallbackContext context)
    {
        if (Time.timeSinceLevelLoad >= inputDelay) StartGame();
    }

    /// <summary>Loads the first hole. Can also be called from a UI button or another script.</summary>
    public void StartGame()
    {
        if (starting) return;
        starting = true;
        if (prompt != null) prompt.text = "LOADING...";

        PlayerLocomotionLock.ForceUnlock();
        SceneManager.LoadScene(firstHoleScene);
    }
}
