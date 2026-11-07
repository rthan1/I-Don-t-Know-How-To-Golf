using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
///   Golf > Build Menu -> Menu scene: the start screen. You stand on a tee box on top of a tall hill overlooking a
///                        retro golf valley (see Hole1Builder.Menu.cs), facing the game's title and a blinking
///                        "PRESS A TO START". Pressing it loads Retro Hole 1.
///
/// Also puts the Menu scene first in the build (so the game starts there) and makes sure Retro Hole 1 is in it.
/// Running it again rebuilds the whole Menu scene from scratch.
/// </summary>
public static class MenuBuilder
{
    const string ScenePath = "Assets/_Game/Scenes/Menu.unity";
    const string FirstHolePath = "Assets/_Game/Scenes/RetroHole1.unity";
    const string MaterialFolder = "Assets/_Game/Materials";

    static readonly string[] XROriginPrefabPaths =
    {
        "Assets/_Game/Prefabs/XR Origin (XR Rig).prefab",
        "Assets/Samples/XR Interaction Toolkit/3.6.1/Starter Assets/Prefabs/XR Origin (XR Rig).prefab",
    };

    // NES palette colors
    static readonly Color NesYellow = new Color(0.973f, 0.722f, 0f);
    static readonly Color NesWhite = new Color(0.988f, 0.988f, 0.988f);
    static readonly Color NesGreen = new Color(0f, 0.659f, 0f);
    static readonly Color NesDarkGreen = new Color(0f, 0.408f, 0f);

    [MenuItem("Golf/Build Menu")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        SetupSkyAndLight();
        BuildPlatform();
        Hole1Builder.BuildMenuBackdrop(); // the retro golf valley, mountains, clouds, sun, etc. around the tee
        GameObject player = AddPlayer(scene);
        BuildTitle();

        EditorSceneManager.SaveScene(scene, ScenePath);
        PutInBuild();

        Selection.activeGameObject = player;
        Debug.Log("Menu built and saved into " + ScenePath + ". It's now the first scene in the build.");
    }

    static void SetupSkyAndLight()
    {
        // The same banded NES sky as the retro holes (made by the hole builder)
        var sky = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/Nes_Sky.mat");
        if (sky != null) RenderSettings.skybox = sky;
        else Debug.LogWarning("Build Menu: Nes_Sky.mat not found. Build a retro hole first to get the NES sky.");

        RenderSettings.fog = false;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.75f, 0.85f, 1.0f);
        RenderSettings.ambientEquatorColor = new Color(0.60f, 0.70f, 0.65f);
        RenderSettings.ambientGroundColor = new Color(0.35f, 0.40f, 0.30f);

        var sunGo = new GameObject("Directional Light");
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.98f, 0.92f);
        sun.intensity = 1.2f;
        sun.shadows = LightShadows.None;
        sunGo.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
    }

    /// A round tee-box platform floating in the sky, with a darker rim.
    static void BuildPlatform()
    {
        var root = new GameObject("Platform");

        GameObject Disc(string name, float radius, float thickness, float topY, Color color, bool ground)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(root.transform, false);
            // A Unity cylinder is 2 units tall, centered on its pivot
            go.transform.localPosition = new Vector3(0f, topY - thickness * 0.5f, 0f);
            go.transform.localScale = new Vector3(radius * 2f, thickness * 0.5f, radius * 2f);
            go.GetComponent<MeshRenderer>().sharedMaterial = GetLitMaterial("Menu_" + name, color);

            // Swap the rounded capsule collider for an exact, flat one (GroundFollow stands the player on it)
            Object.DestroyImmediate(go.GetComponent<Collider>());
            if (ground) go.AddComponent<MeshCollider>();
            return go;
        }

        Disc("Grass", 3f, 0.4f, 0f, NesGreen, true);
        Disc("Rim", 3.15f, 0.6f, -0.02f, NesDarkGreen, false);
    }

    static GameObject AddPlayer(Scene scene)
    {
        GameObject prefab = null;
        foreach (string path in XROriginPrefabPaths)
        {
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null) break;
        }
        if (prefab == null)
        {
            Debug.LogWarning("Build Menu: XR Origin (XR Rig) prefab not found. Add the player to the scene by hand.");
            return null;
        }

        var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        player.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); // facing +Z, toward the title
        return player;
    }

    /// The title and the blinking prompt on a big world-space canvas in front of the player.
    static void BuildTitle()
    {
        var menuGo = new GameObject("Menu", typeof(RectTransform));
        var canvas = menuGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        menuGo.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;

        // 1600 x 800 canvas units at 0.003 m each = a 4.8 m x 2.4 m sign, 5 m in front of the player
        var rect = menuGo.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(1600f, 800f);
        menuGo.transform.position = new Vector3(0f, 2.1f, 5f);
        menuGo.transform.localScale = Vector3.one * 0.003f;

        Text title = AddText(menuGo.transform, "Title", "I DON'T KNOW\nHOW TO GOLF", 170, NesYellow, 0.4f, 1f);
        title.lineSpacing = 0.9f;
        Text prompt = AddText(menuGo.transform, "Prompt", "PRESS A TO START", 80, NesWhite, 0.05f, 0.3f);

        var menu = menuGo.AddComponent<MainMenu>();
        menu.prompt = prompt;
        menu.firstHoleScene = System.IO.Path.GetFileNameWithoutExtension(FirstHolePath);
    }

    static Text AddText(Transform parent, string name, string content, int size, Color color, float bottom, float top)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, bottom);
        rect.anchorMax = new Vector2(1f, top);
        rect.offsetMin = rect.offsetMax = Vector2.zero;

        var text = go.AddComponent<Text>();
        text.text = content;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;

        // Chunky black outline, so it reads against the sky
        var outline = go.AddComponent<Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(6f, -6f);
        return text;
    }

    /// Menu first (the game starts there), then Retro Hole 1. Anything else already in the build stays after them.
    static void PutInBuild()
    {
        var scenes = new List<EditorBuildSettingsScene>
        {
            new EditorBuildSettingsScene(ScenePath, true),
            new EditorBuildSettingsScene(FirstHolePath, true),
        };
        foreach (EditorBuildSettingsScene existing in EditorBuildSettings.scenes)
            if (existing.path != ScenePath && existing.path != FirstHolePath)
                scenes.Add(existing);
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    /// Loads a URP Lit material with this color, or creates it the first time.
    static Material GetLitMaterial(string name, Color color)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0f);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}
