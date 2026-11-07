using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Survives scene loads and stores each finished hole's score for the current round.
/// StrokeManager still resets per hole; this is the future scorecard data source.
/// </summary>
public sealed class RoundScoreTracker : MonoBehaviour
{
    public readonly struct HoleScore
    {
        public readonly string sceneName;
        public readonly string displayName;
        public readonly int par;
        public readonly int strokes;

        public HoleScore(string sceneName, string displayName, int par, int strokes)
        {
            this.sceneName = sceneName;
            this.displayName = displayName;
            this.par = par;
            this.strokes = strokes;
        }

        public int ToPar => strokes - par;
    }

    public static RoundScoreTracker Instance { get; private set; }

    readonly List<HoleScore> _holes = new List<HoleScore>(4);

    public IReadOnlyList<HoleScore> Holes => _holes;

    public int TotalStrokes
    {
        get
        {
            int total = 0;
            for (int i = 0; i < _holes.Count; i++)
                total += _holes[i].strokes;
            return total;
        }
    }

    public int TotalToPar
    {
        get
        {
            int total = 0;
            for (int i = 0; i < _holes.Count; i++)
                total += _holes[i].ToPar;
            return total;
        }
    }

    public static RoundScoreTracker Ensure()
    {
        if (Instance != null)
            return Instance;

        var existing = FindAnyObjectByType<RoundScoreTracker>();
        if (existing != null)
        {
            Instance = existing;
            return Instance;
        }

        var go = new GameObject("RoundScoreTracker");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<RoundScoreTracker>();
        return Instance;
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void ResetRound() => _holes.Clear();

    public void RecordHole(string sceneName, int par, int strokes)
    {
        if (string.IsNullOrEmpty(sceneName))
            return;

        for (int i = 0; i < _holes.Count; i++)
        {
            if (_holes[i].sceneName == sceneName)
            {
                _holes[i] = new HoleScore(sceneName, HoleProgression.DisplayName(sceneName), par, strokes);
                return;
            }
        }

        _holes.Add(new HoleScore(sceneName, HoleProgression.DisplayName(sceneName), par, strokes));
    }

    public void RecordFrom(StrokeManager manager)
    {
        if (manager == null)
            return;

        RecordHole(manager.HoleName, manager.Par, manager.StrokeCount);
    }

    public string FormatToPar(int toPar)
    {
        if (toPar == 0) return "E";
        if (toPar > 0) return $"+{toPar}";
        return toPar.ToString();
    }
}
