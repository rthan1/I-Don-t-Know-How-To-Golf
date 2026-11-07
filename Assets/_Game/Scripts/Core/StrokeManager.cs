using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

// Counts strokes for the current hole: every legit swing, every throw, and every penalty.
// It creates itself when a scene with a GolfBall loads, so it doesn't need to be added to any scene.
// Other scripts can call AddPenalty() (out of bounds, water) and FinishHole() (the cup).
public class StrokeManager : MonoBehaviour
{
    public enum StrokeKind { Swing, Throw, Penalty }

    public struct Stroke
    {
        public int number;
        public StrokeKind kind;
        public string label;
    }

    [Tooltip("Par for this hole. 0 = work it out from the scene name.")]
    [Min(0)] public int par = 0;

    [Tooltip("Fires every time a stroke is added.")]
    public UnityEvent onStrokesChanged;

    [Tooltip("Fires when the ball goes in the cup.")]
    public UnityEvent onHoleFinished;

    public static StrokeManager Instance { get; private set; }

    // Fires every time a stroke is added (for the wrist display).
    public event Action Changed;

    public string HoleName { get; private set; }
    public int Par => par > 0 ? par : ParForScene(HoleName);
    public int StrokeCount => strokes.Count;
    public IReadOnlyList<Stroke> Strokes => strokes;
    public bool HoleFinished { get; private set; }

    private readonly List<Stroke> strokes = new List<Stroke>();

    // After every scene load: if the scene has a golf ball and no stroke counter yet, make one.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        CreateIfNeeded();
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => CreateIfNeeded();

    private static void CreateIfNeeded()
    {
        if (FindAnyObjectByType<StrokeManager>() != null) return;
        if (FindAnyObjectByType<GolfBall>() == null) return;

        new GameObject("StrokeManager").AddComponent<StrokeManager>();
    }

    private void Awake()
    {
        Instance = this;
        HoleName = gameObject.scene.name;
    }

    private void Start()
    {
        // Puts the stroke list on the player's left wrist.
        if (GetComponent<StrokeDisplay>() == null)
        {
            gameObject.AddComponent<StrokeDisplay>();
        }

        Debug.Log($"[Strokes] {HoleName}, par {Par}");
    }

    private void OnEnable()
    {
        GolfBall.Launched += HandleLaunched;
        SecurityCameraSystem.CheatCaught += HandleCaught;
    }

    private void OnDisable()
    {
        GolfBall.Launched -= HandleLaunched;
        SecurityCameraSystem.CheatCaught -= HandleCaught;

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void HandleLaunched(GolfBall ball, ShotType shotType, string label)
    {
        AddStroke(shotType == ShotType.Swing ? StrokeKind.Swing : StrokeKind.Throw, label);
    }

    private void HandleCaught(ICatchableCheat cheat)
    {
        AddPenalty("CAUGHT: " + cheat.CheatName);
    }

    // Adds a one-stroke penalty, e.g. AddPenalty("Water") or AddPenalty("Out of bounds").
    public void AddPenalty(string reason)
    {
        AddStroke(StrokeKind.Penalty, reason);
    }

    // Call when the ball drops in the cup. Stops counting and fires onHoleFinished.
    public void FinishHole()
    {
        if (HoleFinished) return;

        HoleFinished = true;
        Debug.Log($"[Strokes] {HoleName} finished in {StrokeCount} (par {Par})\n{GetSummaryText()}");

        // Persist this hole for the future scorecard; StrokeManager itself is recreated next hole.
        RoundScoreTracker.Ensure().RecordFrom(this);

        onHoleFinished?.Invoke();
        Changed?.Invoke();
    }

    private void AddStroke(StrokeKind kind, string label)
    {
        if (HoleFinished) return;

        strokes.Add(new Stroke { number = strokes.Count + 1, kind = kind, label = label });
        Debug.Log($"[Strokes] {strokes.Count}: {label}" + (kind == StrokeKind.Penalty ? " (+1 penalty)" : ""));

        onStrokesChanged?.Invoke();
        Changed?.Invoke();
    }

    // Text for the wrist display: hole, par, count, then the most recent strokes.
    public string GetSummaryText(int maxLines = 8)
    {
        var text = new StringBuilder();
        text.AppendLine($"{HoleName}  PAR {Par}");
        text.AppendLine(HoleFinished ? $"DONE IN {StrokeCount}" : $"STROKES: {StrokeCount}");

        int first = Mathf.Max(0, strokes.Count - maxLines);
        for (int i = first; i < strokes.Count; i++)
        {
            Stroke stroke = strokes[i];
            string penalty = stroke.kind == StrokeKind.Penalty ? " +1" : "";
            text.AppendLine($"{stroke.number}. {stroke.label}{penalty}");
        }

        return text.ToString();
    }

    // Par by scene name, since the holes don't store their own par yet.
    private static int ParForScene(string sceneName)
    {
        switch (sceneName)
        {
            case "RetroHole2": return 4;
            case "RetroHole4": return 4;
            default: return 3;
        }
    }
}
