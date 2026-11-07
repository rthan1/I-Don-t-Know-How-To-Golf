using UnityEngine;

/// <summary>
/// Ordered hole scenes for a round. Menu starts the first; finishing a hole advances to the next.
/// </summary>
public static class HoleProgression
{
    public const string MenuScene = "Menu";

    public static readonly string[] HoleScenes =
    {
        "RetroHole1",
        "RetroHole2",
        "RetroHole3",
        "RetroHole4",
    };

    public static string FirstHole => HoleScenes.Length > 0 ? HoleScenes[0] : "RetroHole1";

    public static int IndexOf(string sceneName)
    {
        for (int i = 0; i < HoleScenes.Length; i++)
        {
            if (HoleScenes[i] == sceneName)
                return i;
        }

        return -1;
    }

    public static bool TryGetNextHole(string currentScene, out string nextScene)
    {
        int index = IndexOf(currentScene);
        if (index < 0 || index >= HoleScenes.Length - 1)
        {
            nextScene = null;
            return false;
        }

        nextScene = HoleScenes[index + 1];
        return true;
    }

    public static bool IsFinalHole(string sceneName)
    {
        int index = IndexOf(sceneName);
        return index >= 0 && index == HoleScenes.Length - 1;
    }

    public static string DisplayName(string sceneName)
    {
        int index = IndexOf(sceneName);
        if (index >= 0)
            return $"HOLE {index + 1}";

        return string.IsNullOrEmpty(sceneName) ? "HOLE" : sceneName.ToUpperInvariant();
    }
}
