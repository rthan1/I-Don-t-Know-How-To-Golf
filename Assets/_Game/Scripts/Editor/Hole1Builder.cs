using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds Hole 1 as one smooth, hilly terrain: an elevated tee, a curving fairway with mowing stripes,
/// sunken bunkers, a contoured green with a real cup, a pond, rolling rough, hills framing the hole, and trees.
/// Run it from the menu: Golf > Build Hole 1.
///
/// Running it again deletes and rebuilds everything under "Hole1_Course". To change the hole, edit the
/// layout values below and re-run, rather than hand-editing the generated pieces.
///
/// Coordinates: the player tees off at the world origin facing +Z. The hole is about 65 m long and doglegs right.
/// </summary>
public static class Hole1Builder
{
    const string ScenePath = "Assets/_Game/Scenes/Hole1.unity";
    const string MaterialFolder = "Assets/_Game/Materials";
    const string MeshFolder = "Assets/_Game/Models/Generated";
    const string RootName = "Hole1_Course";
    const string VertexColorShader = "Golf/VertexColorLit";

    static readonly string[] XROriginPrefabPaths =
    {
        "Assets/_Game/Prefabs/XR Origin (XR Rig).prefab",
        "Assets/Samples/XR Interaction Toolkit/3.6.1/Starter Assets/Prefabs/XR Origin (XR Rig).prefab",
    };

    // ================= LAYOUT (meters) =================

    // Terrain grid. Smaller Cell = smoother but heavier mesh.
    const float GridMinX = -28f, GridMaxX = 28f, GridMinZ = -14f, GridMaxZ = 84f;
    const float Cell = 0.4f;

    // Center line of the hole, from the tee to the green (first and last points only steer the curve).
    static readonly Vector2[] PathControlPoints =
    {
        new Vector2(0f, -6f),
        new Vector2(0f, 0f),     // tee
        new Vector2(0.5f, 16f),
        new Vector2(3.5f, 32f),
        new Vector2(8.5f, 46f),
        new Vector2(10f, 62f),   // green
        new Vector2(10f, 70f),
    };

    // Where along the path (0 = tee, 1 = green) the mown fairway starts and ends.
    const float FairwayStart = 0.13f, FairwayEnd = 0.86f;

    static readonly Vector2 TeeCenter = new Vector2(0f, 0f);
    static readonly Vector2 TeeHalfSize = new Vector2(2.2f, 1.8f);
    const float TeeRaise = 0.3f;

    static readonly Vector2 GreenCenter = new Vector2(10f, 62f);
    static readonly Vector2 GreenRadius = new Vector2(6f, 7f);

    // The cup snaps to the nearest terrain grid point. It's ~2x a real cup so putting is forgiving in VR.
    static readonly Vector2 CupTarget = new Vector2(10.3f, 63.5f);
    const float CupRadius = 0.1f;
    const float CupDepth = 0.12f;

    // Bunkers: center (x, z), radius (x, z). They're sunken into the ground.
    static readonly (Vector2 center, Vector2 radius)[] Bunkers =
    {
        (new Vector2(-5.5f, 24f), new Vector2(2.2f, 3.2f)),  // left fairway bunker
        (new Vector2(11f, 37f), new Vector2(2.4f, 3.0f)),    // right fairway bunker (inside the dogleg)
        (new Vector2(2.5f, 60f), new Vector2(1.6f, 3.0f)),   // left greenside
        (new Vector2(17.5f, 64f), new Vector2(1.5f, 2.6f)),  // right greenside
        (new Vector2(13f, 70.5f), new Vector2(2.8f, 1.3f)),  // behind the green
    };
    const float BunkerDepth = 0.45f;

    static readonly Vector2 PondCenter = new Vector2(-12f, 42f);
    static readonly Vector2 PondRadius = new Vector2(5f, 7f);

    const int TreeCount = 110;
    const int RandomSeed = 2026;

    // ================= COLORS =================

    static readonly Color RoughA = new Color(0.17f, 0.38f, 0.14f);
    static readonly Color RoughB = new Color(0.24f, 0.46f, 0.16f);
    static readonly Color FairwayDark = new Color(0.33f, 0.64f, 0.22f);
    static readonly Color FairwayLight = new Color(0.40f, 0.73f, 0.27f);
    static readonly Color FringeColor = new Color(0.30f, 0.60f, 0.22f);
    static readonly Color GreenDark = new Color(0.40f, 0.78f, 0.30f);
    static readonly Color GreenLight = new Color(0.46f, 0.85f, 0.34f);
    static readonly Color SandColor = new Color(0.93f, 0.84f, 0.60f);
    static readonly Color MudColor = new Color(0.33f, 0.30f, 0.20f);
    static readonly Color CupLiner = new Color(0.95f, 0.95f, 0.95f);
    static readonly Color CupInside = new Color(0.12f, 0.10f, 0.08f);
    static readonly Color TrunkColor = new Color(0.40f, 0.27f, 0.16f);

    // ================= BUILD STATE (computed each run) =================

    static List<Vector2> pathPoints;
    static List<float> pathT;      // 0..1 along the path for each point
    static Vector2 cup;
    static int cupI, cupJ;         // grid indices of the cup
    static float teeHeight, greenHeight, waterLevel;
    static float[,] gridHeights;

    [MenuItem("Golf/Build Hole 1")]
    public static void Build()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
        {
            EditorUtility.DisplayDialog("Build Hole 1", "Couldn't find " + ScenePath, "OK");
            return;
        }
        if (SceneManager.GetActiveScene().path != ScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
        }
        Scene scene = SceneManager.GetActiveScene();

        try
        {
            EditorUtility.DisplayProgressBar("Build Hole 1", "Shaping terrain...", 0.1f);

            GameObject old = GameObject.Find(RootName);
            if (old != null) Undo.DestroyObjectImmediate(old);
            RemoveLeftoverFloors(scene);
            DeleteOldGeneratedMeshes();

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Build Hole 1");

            Material terrainMat = GetVertexColorMaterial();
            Material whiteMat = GetLitMaterial("White", new Color(0.95f, 0.95f, 0.95f), 0.3f);
            Material flagMat = GetLitMaterial("Flag", new Color(0.90f, 0.12f, 0.12f), 0.2f);
            Material waterMat = GetLitMaterial("Water", new Color(0.16f, 0.42f, 0.62f), 0.95f);

            PrepareLayout();
            BuildTerrain(root.transform, terrainMat);

            EditorUtility.DisplayProgressBar("Build Hole 1", "Adding water, flag and trees...", 0.6f);
            BuildWater(root.transform, waterMat);
            BuildCupAndFlag(root.transform, whiteMat, flagMat);
            BuildTeeMarkers(root.transform, whiteMat);
            BuildTrees(root.transform, terrainMat);

            Vector3 playerSpawn = new Vector3(TeeCenter.x, teeHeight, TeeCenter.y - 1.0f);
            Vector3 ballSpawn = new Vector3(TeeCenter.x, teeHeight + 0.03f, TeeCenter.y + 0.4f);
            Marker("PlayerSpawn", root.transform, playerSpawn);
            Marker("BallSpawn", root.transform, ballSpawn);
            BuildTestBall(root.transform, ballSpawn, whiteMat);

            EnsurePlayer(scene, playerSpawn);
            SetupAtmosphere(scene);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Selection.activeGameObject = root;
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
            Debug.Log("Hole 1 built and saved.");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    // ================= LAYOUT MATH =================

    static void PrepareLayout()
    {
        BuildPath();

        cupI = Mathf.RoundToInt((CupTarget.x - GridMinX) / Cell);
        cupJ = Mathf.RoundToInt((CupTarget.y - GridMinZ) / Cell);
        cup = new Vector2(GridMinX + cupI * Cell, GridMinZ + cupJ * Cell);

        teeHeight = Elevation(0f) + TeeRaise;
        greenHeight = Elevation(1f) + 0.15f;

        // Water sits just below the lowest point of the pond's banks, so the shore always meets the ground.
        float lowest = float.MaxValue;
        for (int k = 0; k < 32; k++)
        {
            float a = k * Mathf.PI * 2f / 32f;
            Vector2 p = PondCenter + new Vector2(PondRadius.x * Mathf.Cos(a), PondRadius.y * Mathf.Sin(a)) * 1.2f;
            lowest = Mathf.Min(lowest, Evaluate(p, false, out _));
        }
        waterLevel = lowest - 0.25f;
    }

    static void BuildPath()
    {
        pathPoints = new List<Vector2>();
        const int steps = 60;
        for (int i = 1; i < PathControlPoints.Length - 2; i++)
            for (int k = 0; k < steps; k++)
                pathPoints.Add(CatmullRom(PathControlPoints[i - 1], PathControlPoints[i],
                                          PathControlPoints[i + 1], PathControlPoints[i + 2], k / (float)steps));
        pathPoints.Add(PathControlPoints[PathControlPoints.Length - 2]);

        pathT = new List<float> { 0f };
        float total = 0f;
        for (int i = 1; i < pathPoints.Count; i++)
        {
            total += Vector2.Distance(pathPoints[i - 1], pathPoints[i]);
            pathT.Add(total);
        }
        for (int i = 0; i < pathT.Count; i++) pathT[i] /= total;
    }

    static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    /// Distance from p to the hole's center line, and how far along the hole (0..1) the closest point is.
    static void ProjectOnPath(Vector2 p, out float distance, out float t)
    {
        float best = float.MaxValue;
        t = 0f;
        for (int i = 0; i < pathPoints.Count - 1; i++)
        {
            Vector2 a = pathPoints[i], ab = pathPoints[i + 1] - a;
            float u = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            float d2 = (a + ab * u - p).sqrMagnitude;
            if (d2 < best)
            {
                best = d2;
                t = Mathf.Lerp(pathT[i], pathT[i + 1], u);
            }
        }
        distance = Mathf.Sqrt(best);
    }

    static Vector2 PathPoint(float t)
    {
        for (int i = 0; i < pathPoints.Count - 1; i++)
            if (t <= pathT[i + 1])
                return Vector2.Lerp(pathPoints[i], pathPoints[i + 1], Mathf.InverseLerp(pathT[i], pathT[i + 1], t));
        return pathPoints[pathPoints.Count - 1];
    }

    /// Half the fairway's width at a point along the hole: narrow off the tee, wide in the middle.
    static float HalfWidth(float t) => 3.6f + 1.5f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t));

    /// Ground height along the hole: a high tee, a drop into a rolling fairway, then a rise to the green.
    static float Elevation(float t)
    {
        float drop = 1.2f * (1f - Smooth(0f, 0.22f, t));
        float roll = 0.3f * Mathf.Sin(t * 13f) * Smooth(0.15f, 0.3f, t) * (1f - Smooth(0.7f, 0.85f, t));
        float rise = 0.55f * Smooth(0.72f, 1f, t);
        return 0.3f + drop + roll + rise;
    }

    /// Smoothstep from edge a to edge b (works in either direction).
    static float Smooth(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / (b - a));
        return t * t * (3f - 2f * t);
    }

    /// 1 on the ellipse's edge, 0 at its center, above 1 outside.
    static float Ellipse(Vector2 p, Vector2 center, Vector2 radius)
    {
        float dx = (p.x - center.x) / radius.x, dz = (p.y - center.y) / radius.y;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    static float Noise(Vector2 p, float scale, float offset) => Mathf.PerlinNoise(p.x * scale + offset, p.y * scale + offset * 1.7f);

    /// The heart of the course: returns the ground height at p and the ground color there.
    static float Evaluate(Vector2 p, bool withPond, out Color color)
    {
        ProjectOnPath(p, out float d, out float t);
        float w = HalfWidth(t);
        float elev = Elevation(t);

        // ---- Height ----
        // Rolling rough with mounds lining the fairway
        float mounds = Smooth(w + 2f, w + 9f, d) * (0.7f + 1.6f * Noise(p, 0.09f, 200f));
        float rough = elev + (Noise(p, 0.045f, 31.7f) - 0.5f) * 3.2f + (Noise(p, 0.13f, 5.1f) - 0.5f) * 0.7f + mounds;
        // Gentle undulation in the play corridor
        float corridor = elev + (Noise(p, 0.07f, 90f) - 0.5f) * 0.5f;
        float h = Mathf.Lerp(rough, corridor, Smooth(w + 7f, w + 1.5f, d));

        // Big hills around the edges to frame the hole
        float edge = Mathf.Min(Mathf.Min(p.x - GridMinX, GridMaxX - p.x), Mathf.Min(p.y - GridMinZ, GridMaxZ - p.y));
        float rim = Smooth(14f, 0f, edge);
        h += rim * rim * 9f * (0.7f + 0.6f * Noise(p, 0.08f, 400f));

        // Raised, flat tee box
        Vector2 tp = p - TeeCenter;
        float teeN = Mathf.Max(Mathf.Abs(tp.x) / TeeHalfSize.x, Mathf.Abs(tp.y) / TeeHalfSize.y);
        h = Mathf.Lerp(h, teeHeight, Smooth(1.6f, 1.0f, teeN));

        // Contoured green, perfectly flat right around the cup
        float gN = Ellipse(p, GreenCenter, GreenRadius);
        Vector2 fromCup = p - cup;
        float slopeAmount = Smooth(0.6f, 1.6f, fromCup.magnitude);
        float greenSurface = greenHeight + slopeAmount *
            (0.018f * fromCup.x - 0.012f * fromCup.y + 0.06f * Mathf.Sin(p.x * 0.5f) * Mathf.Cos(p.y * 0.4f));
        h = Mathf.Lerp(h, greenSurface, Smooth(1.45f, 1.0f, gN));

        // Sunken bunkers with a little raised lip
        float sand = 0f;
        foreach (var b in Bunkers)
        {
            float bn = Ellipse(p, b.center, b.radius);
            if (bn > 1.6f) continue;
            float lip = (bn - 1.08f) / 0.14f;
            h += -BunkerDepth * Smooth(1.0f, 0.4f, bn) + 0.12f * Mathf.Exp(-lip * lip);
            sand = Mathf.Max(sand, Smooth(0.95f, 0.85f, bn));
        }

        // Pond basin
        float mud = 0f;
        if (withPond)
        {
            float pn = Ellipse(p, PondCenter, PondRadius);
            h = Mathf.Lerp(h, waterLevel - 0.7f, Smooth(1.25f, 0.75f, pn));
            mud = Smooth(1.4f, 1.15f, pn);
        }

        // ---- Color ----
        color = Color.Lerp(RoughA, RoughB, Noise(p, 0.2f, 11f));

        float tF = Mathf.Clamp(t, FairwayStart, FairwayEnd);
        float fairwayDist = (t < FairwayStart || t > FairwayEnd) ? Vector2.Distance(p, PathPoint(tF)) : d;
        float fairwayMask = Smooth(HalfWidth(tF) + 0.3f, HalfWidth(tF) - 0.3f, fairwayDist);
        float stripes = Smooth(-0.25f, 0.25f, Mathf.Sin((p.x + p.y) * 0.65f));
        color = Color.Lerp(color, Color.Lerp(FairwayDark, FairwayLight, stripes), fairwayMask);

        float greenStripes = Smooth(-0.3f, 0.3f, Mathf.Sin(p.x * 1.4f));
        Color greenColor = Color.Lerp(GreenDark, GreenLight, greenStripes);
        color = Color.Lerp(color, FringeColor, Smooth(1.18f, 1.1f, gN));
        color = Color.Lerp(color, greenColor, Smooth(1.04f, 0.98f, gN));
        color = Color.Lerp(color, greenColor, Smooth(1.1f, 1.0f, teeN));
        color = Color.Lerp(color, MudColor, mud);
        color = Color.Lerp(color, SandColor * (0.94f + 0.1f * Noise(p, 1.3f, 70f)), sand);

        return h;
    }

    /// Height of the actual terrain mesh at (x, z), matching its triangles exactly (so trees sit on the ground).
    static float GroundHeight(Vector2 p)
    {
        float fx = (p.x - GridMinX) / Cell, fz = (p.y - GridMinZ) / Cell;
        int i = Mathf.Clamp(Mathf.FloorToInt(fx), 0, gridHeights.GetLength(0) - 2);
        int j = Mathf.Clamp(Mathf.FloorToInt(fz), 0, gridHeights.GetLength(1) - 2);
        float u = fx - i, v = fz - j;
        float h00 = gridHeights[i, j], h10 = gridHeights[i + 1, j], h01 = gridHeights[i, j + 1], h11 = gridHeights[i + 1, j + 1];
        return v > u ? h00 + v * (h01 - h00) + u * (h11 - h01)
                     : h00 + u * (h10 - h00) + v * (h11 - h10);
    }

    // ================= TERRAIN MESH =================

    static void BuildTerrain(Transform parent, Material mat)
    {
        int nx = Mathf.RoundToInt((GridMaxX - GridMinX) / Cell);
        int nz = Mathf.RoundToInt((GridMaxZ - GridMinZ) / Cell);
        int vx = nx + 1, vz = nz + 1;

        var verts = new List<Vector3>(vx * vz + 1000);
        var colors = new List<Color>(vx * vz + 1000);
        var tris = new List<int>(nx * nz * 6 + 3000);
        gridHeights = new float[vx, vz];
        bool linear = PlayerSettings.colorSpace == ColorSpace.Linear;

        for (int j = 0; j < vz; j++)
        {
            if (j % 20 == 0) EditorUtility.DisplayProgressBar("Build Hole 1", "Shaping terrain...", 0.1f + 0.5f * j / vz);
            for (int i = 0; i < vx; i++)
            {
                var p = new Vector2(GridMinX + i * Cell, GridMinZ + j * Cell);
                float h = Evaluate(p, true, out Color c);
                // The 3x3 grid points around the cup must be exactly level with the cup.
                if (Mathf.Abs(i - cupI) <= 1 && Mathf.Abs(j - cupJ) <= 1) h = greenHeight;
                gridHeights[i, j] = h;
                verts.Add(new Vector3(p.x, h, p.y));
                colors.Add(linear ? c.linear : c);
            }
        }

        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                // Leave a 2x2 cell opening around the cup; it's filled by the cup piece below.
                if ((i == cupI - 1 || i == cupI) && (j == cupJ - 1 || j == cupJ)) continue;
                int v00 = j * vx + i, v10 = v00 + 1, v01 = v00 + vx, v11 = v01 + 1;
                tris.Add(v00); tris.Add(v01); tris.Add(v11);
                tris.Add(v00); tris.Add(v11); tris.Add(v10);
            }

        AddCupGeometry(verts, colors, tris, linear);

        var mesh = new Mesh { name = "Hole1_Terrain" };
        if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetColors(colors);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals(); // shared grid vertices = smooth shading
        mesh.RecalculateBounds();
        SaveMesh(mesh);

        var go = new GameObject("Terrain");
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
    }

    /// Fills the opening around the cup with a flat square that has a round hole, plus the cup itself.
    static void AddCupGeometry(List<Vector3> verts, List<Color> colors, List<int> tris, bool linear)
    {
        const int segments = 32;
        float top = greenHeight, liner = top - 0.025f, bottom = top - CupDepth;
        Evaluate(cup, true, out Color green);

        void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 facing, Color col)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), facing) < 0f) (b, c) = (c, b);
            if (linear) col = col.linear;
            int s = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            colors.Add(col); colors.Add(col); colors.Add(col);
            tris.Add(s); tris.Add(s + 1); tris.Add(s + 2);
        }
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing, Color col)
        {
            Tri(a, b, c, facing, col);
            Tri(a, c, d, facing, col);
        }
        Vector3 At(Vector2 dir, float radius, float y) => new Vector3(cup.x + dir.x * radius, y, cup.y + dir.y * radius);

        for (int k = 0; k < segments; k++)
        {
            float a0 = k * Mathf.PI * 2f / segments, a1 = (k + 1) * Mathf.PI * 2f / segments;
            var d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
            var d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
            // Point on the square's edge in this direction
            float s0 = Cell / Mathf.Max(Mathf.Abs(d0.x), Mathf.Abs(d0.y));
            float s1 = Cell / Mathf.Max(Mathf.Abs(d1.x), Mathf.Abs(d1.y));

            Quad(At(d0, s0, top), At(d1, s1, top), At(d1, CupRadius, top), At(d0, CupRadius, top), Vector3.up, green);

            Vector3 inward0 = -new Vector3(d0.x + d1.x, 0f, d0.y + d1.y);
            Quad(At(d0, CupRadius, top), At(d1, CupRadius, top), At(d1, CupRadius, liner), At(d0, CupRadius, liner), inward0, CupLiner);
            Quad(At(d0, CupRadius, liner), At(d1, CupRadius, liner), At(d1, CupRadius, bottom), At(d0, CupRadius, bottom), inward0, CupInside);
            Tri(new Vector3(cup.x, bottom, cup.y), At(d0, CupRadius, bottom), At(d1, CupRadius, bottom), Vector3.up, CupInside * 0.7f);
        }
    }

    // ================= PROPS =================

    static void BuildWater(Transform parent, Material mat)
    {
        var verts = new List<Vector3>();
        var tris = new List<int>();
        const int segments = 48;
        verts.Add(Vector3.zero);
        for (int k = 0; k < segments; k++)
        {
            float a = k * Mathf.PI * 2f / segments;
            verts.Add(new Vector3(PondRadius.x * 1.2f * Mathf.Cos(a), 0f, PondRadius.y * 1.2f * Mathf.Sin(a)));
        }
        for (int k = 0; k < segments; k++)
        {
            int a = k + 1, b = (k + 1) % segments + 1;
            tris.Add(0); tris.Add(b); tris.Add(a); // clockwise from above = faces up
        }
        var mesh = new Mesh { name = "Hole1_Water" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        SaveMesh(mesh);

        var go = new GameObject("Pond");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(PondCenter.x, waterLevel, PondCenter.y);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        // Trigger so gameplay code can detect a ball landing in the water
        var trigger = go.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(PondRadius.x * 2f, 1f, PondRadius.y * 2f);
        trigger.center = new Vector3(0f, -0.4f, 0f);
    }

    static void BuildCupAndFlag(Transform parent, Material pinMat, Material flagMat)
    {
        float cupBottom = greenHeight - CupDepth;

        var cupGo = new GameObject("CupTrigger");
        cupGo.transform.SetParent(parent, false);
        cupGo.transform.localPosition = new Vector3(cup.x, cupBottom + 0.05f, cup.y);
        var trigger = cupGo.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = 0.07f;

        const float pinHeight = 2.2f;
        GameObject pin = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pin.name = "FlagPin";
        Object.DestroyImmediate(pin.GetComponent<Collider>());
        pin.transform.SetParent(parent, false);
        pin.transform.localPosition = new Vector3(cup.x, cupBottom + pinHeight * 0.5f, cup.y);
        pin.transform.localScale = new Vector3(0.025f, pinHeight * 0.5f, 0.025f);
        pin.GetComponent<Renderer>().sharedMaterial = pinMat;

        GameObject flag = GameObject.CreatePrimitive(PrimitiveType.Cube);
        flag.name = "Flag";
        Object.DestroyImmediate(flag.GetComponent<Collider>());
        flag.transform.SetParent(parent, false);
        flag.transform.localPosition = new Vector3(cup.x, cupBottom + pinHeight - 0.2f, cup.y + 0.3f);
        flag.transform.localScale = new Vector3(0.01f, 0.35f, 0.55f);
        flag.GetComponent<Renderer>().sharedMaterial = flagMat;
    }

    static void BuildTeeMarkers(Transform parent, Material mat)
    {
        foreach (float x in new[] { -1.2f, 1.2f })
        {
            GameObject m = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            m.name = "TeeMarker";
            Object.DestroyImmediate(m.GetComponent<Collider>());
            m.transform.SetParent(parent, false);
            m.transform.localPosition = new Vector3(TeeCenter.x + x, teeHeight + 0.06f, TeeCenter.y + 0.9f);
            m.transform.localScale = Vector3.one * 0.12f;
            m.GetComponent<Renderer>().sharedMaterial = mat;
        }
    }

    static void BuildTrees(Transform parent, Material mat)
    {
        bool linear = PlayerSettings.colorSpace == ColorSpace.Linear;
        Color[] pineColors = { new Color(0.13f, 0.36f, 0.17f), new Color(0.16f, 0.42f, 0.20f), new Color(0.11f, 0.31f, 0.20f) };
        Color[] roundColors = { new Color(0.25f, 0.50f, 0.18f), new Color(0.31f, 0.55f, 0.20f), new Color(0.21f, 0.45f, 0.21f) };
        var meshes = new List<Mesh>();
        for (int v = 0; v < 3; v++)
        {
            meshes.Add(PineMesh("Hole1_Pine" + v, pineColors[v], linear));
            meshes.Add(RoundTreeMesh("Hole1_RoundTree" + v, roundColors[v], v, linear));
        }

        var treesParent = new GameObject("Trees").transform;
        treesParent.SetParent(parent, false);
        var rng = new System.Random(RandomSeed);
        var placed = new List<Vector2>();

        for (int attempt = 0; attempt < 5000 && placed.Count < TreeCount; attempt++)
        {
            var p = new Vector2(Mathf.Lerp(GridMinX + 1f, GridMaxX - 1f, (float)rng.NextDouble()),
                                Mathf.Lerp(GridMinZ + 1f, GridMaxZ - 1f, (float)rng.NextDouble()));
            ProjectOnPath(p, out float d, out float t);
            if (d < HalfWidth(t) + 6.5f) continue;                        // keep the play area clear
            if (Ellipse(p, GreenCenter, GreenRadius) < 1.9f) continue;
            if (Vector2.Distance(p, TeeCenter) < 7f) continue;
            if (Ellipse(p, PondCenter, PondRadius) < 1.5f) continue;
            bool crowded = false;
            foreach (Vector2 other in placed)
                if ((other - p).sqrMagnitude < 2.6f * 2.6f) { crowded = true; break; }
            if (crowded) continue;
            placed.Add(p);

            var tree = new GameObject("Tree");
            tree.transform.SetParent(treesParent, false);
            tree.transform.localPosition = new Vector3(p.x, GroundHeight(p), p.y);
            tree.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            tree.transform.localScale = Vector3.one * Mathf.Lerp(0.8f, 1.5f, (float)rng.NextDouble());
            tree.AddComponent<MeshFilter>().sharedMesh = meshes[rng.Next(meshes.Count)];
            tree.AddComponent<MeshRenderer>().sharedMaterial = mat;
            var trunk = tree.AddComponent<CapsuleCollider>();
            trunk.center = new Vector3(0f, 0.8f, 0f);
            trunk.height = 1.6f;
            trunk.radius = 0.2f;
            GameObjectUtility.SetStaticEditorFlags(tree, StaticEditorFlags.BatchingStatic);
        }
    }

    /// Low-poly pine: a hexagonal trunk and three stacked cones.
    static Mesh PineMesh(string name, Color leaves, bool linear)
    {
        var mb = new MeshBuilder(linear);
        mb.Cylinder(0.14f, -0.3f, 1.0f, 6, TrunkColor);
        mb.Cone(1.25f, 0.7f, 1.6f, 8, leaves);
        mb.Cone(0.95f, 1.4f, 1.4f, 8, leaves * 1.1f);
        mb.Cone(0.65f, 2.1f, 1.2f, 8, leaves * 1.2f);
        return mb.ToMesh(name);
    }

    /// Low-poly round tree: a trunk and a lumpy ball of leaves.
    static Mesh RoundTreeMesh(string name, Color leaves, int seed, bool linear)
    {
        var mb = new MeshBuilder(linear);
        mb.Cylinder(0.15f, -0.3f, 1.5f, 6, TrunkColor);
        mb.Blob(new Vector3(0f, 2.1f, 0f), 1.15f, 5, 8, leaves, seed);
        return mb.ToMesh(name);
    }

    /// Placeholder ball for testing the course. Replace with the real Ball prefab later.
    static void BuildTestBall(Transform parent, Vector3 position, Material mat)
    {
        GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "TestBall (placeholder)";
        ball.transform.SetParent(parent, false);
        ball.transform.localPosition = position;
        ball.transform.localScale = Vector3.one * 0.043f;
        ball.GetComponent<Renderer>().sharedMaterial = mat;

        var rb = ball.AddComponent<Rigidbody>();
        rb.mass = 0.046f;
        rb.linearDamping = 0.1f;
        rb.angularDamping = 0.6f;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    static void Marker(string name, Transform parent, Vector3 position)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
    }

    /// Adds the XR rig at the tee (if the scene doesn't have one) and removes the default Main Camera.
    static void EnsurePlayer(Scene scene, Vector3 spawn)
    {
        GameObject xr = null;
        foreach (GameObject go in scene.GetRootGameObjects())
            if (go.name.StartsWith("XR Origin")) { xr = go; break; }

        if (xr == null)
        {
            GameObject prefab = null;
            foreach (string path in XROriginPrefabPaths)
            {
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null) break;
            }
            if (prefab == null)
            {
                Debug.LogWarning("Hole1Builder: XR Origin (XR Rig) prefab not found. Add the player to the scene by hand.");
                return;
            }
            xr = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Undo.RegisterCreatedObjectUndo(xr, "Add XR Origin");
        }
        xr.transform.SetPositionAndRotation(spawn, Quaternion.identity);

        foreach (GameObject go in scene.GetRootGameObjects())
            if (go.name == "Main Camera" && go.GetComponent<Camera>() != null)
                Undo.DestroyObjectImmediate(go);
    }

    /// Removes old test floors (Unity Planes, ProBuilder test shapes) that would poke through the terrain.
    static void RemoveLeftoverFloors(Scene scene)
    {
        foreach (GameObject go in scene.GetRootGameObjects())
        {
            if (go.name == "Plane" || go.name.StartsWith("pb_Mesh"))
            {
                Debug.Log("Hole1Builder: removed leftover floor object '" + go.name + "' (Ctrl+Z to bring it back).");
                Undo.DestroyObjectImmediate(go);
            }
        }
    }

    /// Warm afternoon sun and soft distance fog.
    static void SetupAtmosphere(Scene scene)
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.74f, 0.85f, 0.95f);
        RenderSettings.fogStartDistance = 40f;
        RenderSettings.fogEndDistance = 160f;

        foreach (GameObject go in scene.GetRootGameObjects())
            foreach (Light light in go.GetComponentsInChildren<Light>())
            {
                if (light.type != LightType.Directional) continue;
                Undo.RecordObject(light, "Hole 1 lighting");
                Undo.RecordObject(light.transform, "Hole 1 lighting");
                light.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
                light.color = new Color(1f, 0.95f, 0.86f);
                light.intensity = 1.3f;
                light.shadows = LightShadows.None; // real-time shadows are expensive on Quest 2
            }
    }

    // ================= MESH HELPERS =================

    /// Builds flat-shaded, vertex-colored meshes (each triangle has its own vertices).
    class MeshBuilder
    {
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Color> colors = new List<Color>();
        readonly List<int> tris = new List<int>();
        readonly bool linear;

        public MeshBuilder(bool linear) { this.linear = linear; }

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 facing, Color col)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), facing) < 0f) (b, c) = (c, b);
            if (linear) col = col.linear;
            int s = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            colors.Add(col); colors.Add(col); colors.Add(col);
            tris.Add(s); tris.Add(s + 1); tris.Add(s + 2);
        }

        public void Cylinder(float radius, float y0, float y1, int sides, Color col)
        {
            for (int k = 0; k < sides; k++)
            {
                float a0 = k * Mathf.PI * 2f / sides, a1 = (k + 1) * Mathf.PI * 2f / sides;
                var p0 = new Vector3(Mathf.Cos(a0) * radius, 0f, Mathf.Sin(a0) * radius);
                var p1 = new Vector3(Mathf.Cos(a1) * radius, 0f, Mathf.Sin(a1) * radius);
                Vector3 outward = p0 + p1;
                Vector3 b0 = p0 + Vector3.up * y0, b1 = p1 + Vector3.up * y0, t0 = p0 + Vector3.up * y1, t1 = p1 + Vector3.up * y1;
                Tri(b0, b1, t1, outward, col);
                Tri(b0, t1, t0, outward, col);
            }
        }

        public void Cone(float radius, float baseY, float height, int sides, Color col)
        {
            var apex = new Vector3(0f, baseY + height, 0f);
            var center = new Vector3(0f, baseY, 0f);
            for (int k = 0; k < sides; k++)
            {
                float a0 = k * Mathf.PI * 2f / sides, a1 = (k + 1) * Mathf.PI * 2f / sides;
                var p0 = new Vector3(Mathf.Cos(a0) * radius, baseY, Mathf.Sin(a0) * radius);
                var p1 = new Vector3(Mathf.Cos(a1) * radius, baseY, Mathf.Sin(a1) * radius);
                Vector3 outward = (p0 + p1) * 0.5f - center + Vector3.up * (radius * radius / height);
                Tri(p0, p1, apex, outward, col);
                Tri(center, p0, p1, Vector3.down, col * 0.6f);
            }
        }

        /// A lumpy low-poly sphere.
        public void Blob(Vector3 center, float radius, int rings, int sides, Color col, int seed)
        {
            var rng = new System.Random(seed * 977 + 13);
            var ring = new Vector3[rings - 1, sides];
            for (int r = 1; r < rings; r++)
                for (int k = 0; k < sides; k++)
                {
                    float lat = Mathf.PI * r / rings, lon = Mathf.PI * 2f * k / sides;
                    float rad = radius * (0.85f + 0.3f * (float)rng.NextDouble());
                    ring[r - 1, k] = center + new Vector3(Mathf.Sin(lat) * Mathf.Cos(lon), Mathf.Cos(lat), Mathf.Sin(lat) * Mathf.Sin(lon)) * rad;
                }
            Vector3 top = center + Vector3.up * radius * 0.95f, bottom = center + Vector3.down * radius * 0.8f;

            for (int k = 0; k < sides; k++)
            {
                int k1 = (k + 1) % sides;
                Tri(top, ring[0, k], ring[0, k1], ring[0, k] + ring[0, k1] + top - 3f * center, col * 1.15f);
                Tri(bottom, ring[rings - 2, k], ring[rings - 2, k1], ring[rings - 2, k] + ring[rings - 2, k1] + bottom - 3f * center, col * 0.8f);
                for (int r = 0; r < rings - 2; r++)
                {
                    Vector3 a = ring[r, k], b = ring[r, k1], c = ring[r + 1, k1], d = ring[r + 1, k];
                    Vector3 outward = a + b + c + d - 4f * center;
                    Color shade = col * Mathf.Lerp(1.1f, 0.85f, r / (float)(rings - 2));
                    Tri(a, b, c, outward, shade);
                    Tri(a, c, d, outward, shade);
                }
            }
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            SaveMesh(mesh);
            return mesh;
        }
    }

    // ================= ASSETS =================

    static void SaveMesh(Mesh mesh)
    {
        EnsureFolder(MeshFolder);
        AssetDatabase.CreateAsset(mesh, MeshFolder + "/" + mesh.name + ".asset");
    }

    /// Generated meshes are rebuilt every run, so clear out the previous ones first.
    static void DeleteOldGeneratedMeshes()
    {
        if (!AssetDatabase.IsValidFolder(MeshFolder)) return;
        foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { MeshFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileName(path).StartsWith("Hole1_")) AssetDatabase.DeleteAsset(path);
        }
    }

    static Material GetVertexColorMaterial()
    {
        EnsureFolder(MaterialFolder);
        string path = MaterialFolder + "/Hole_Terrain.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        Shader shader = Shader.Find(VertexColorShader);
        if (shader == null)
        {
            Debug.LogError("Hole1Builder: shader '" + VertexColorShader + "' not found. Is _Game/Shaders/VertexColorLit.shader in the project?");
            shader = Shader.Find("Universal Render Pipeline/Lit");
        }
        mat = new Material(shader);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static Material GetLitMaterial(string name, Color color, float smoothness)
    {
        EnsureFolder(MaterialFolder);
        string path = MaterialFolder + "/Hole_" + name + ".mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat; // keep any changes the art team made

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        mat = new Material(shader);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
