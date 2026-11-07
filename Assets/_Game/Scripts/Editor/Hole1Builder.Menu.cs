using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The menu's backdrop, built with the same NES-style pieces as the holes (see MenuBuilder).
///
/// You stand on a tee box at the top of a tall hill, looking out over a whole retro golf hole in the valley below:
/// a winding checkered fairway, a pond, bunkers, a giant flag on the green, pine and round trees, and a ring of
/// hills, mountains and clouds, plus a security camera right next to you, watching.
/// Nothing from the real holes shows up here, so the menu doesn't spoil them.
///
/// Coordinates: the player stands at the origin facing +Z.
/// </summary>
public static partial class Hole1Builder
{
    const float MenuValleyY = -18f;                // height of the valley floor (the tee box is at 0)
    static readonly Vector2 MenuBowlCenter = new Vector2(0f, 100f);
    static readonly Vector2 MenuBowlRadius = new Vector2(140f, 170f);
    static readonly Vector2 MenuGreenCenter = new Vector2(26f, 160f);
    static readonly Vector2 MenuGreenRadius = new Vector2(12f, 10f);
    static readonly Vector2 MenuPondCenter = new Vector2(-42f, 95f);
    static readonly Vector2 MenuPondRadius = new Vector2(20f, 13f);
    const float MenuWaterY = MenuValleyY - 0.8f;
    static readonly (Vector2 center, Vector2 radius)[] MenuBunkers =
    {
        (new Vector2(14f, 72f), new Vector2(5f, 8f)),      // fairway bunker at the bend
        (new Vector2(13f, 158f), new Vector2(4f, 5.5f)),   // left of the green
        (new Vector2(39f, 165f), new Vector2(4f, 6f)),     // right of the green
        (new Vector2(24f, 173f), new Vector2(7f, 3f)),     // behind the green
    };
    static readonly Vector2[] MenuPathControls =
    {
        new Vector2(0f, 10f), new Vector2(0f, 30f), new Vector2(-8f, 58f), new Vector2(-2f, 88f),
        new Vector2(14f, 118f), new Vector2(26f, 160f), new Vector2(28f, 175f),
    };
    static Vector2[] menuPath;

    /// Builds the whole backdrop under a "Backdrop" object and returns it.
    public static GameObject BuildMenuBackdrop()
    {
        S = RetroStyle();
        S.Label = "Menu";
        S.MeshPrefix = "Menu_";
        L = new HoleLayout { Name = "Menu", CloudCenter = MenuBowlCenter / S.Scale };
        linearColor = PlayerSettings.colorSpace == ColorSpace.Linear;
        animalMeshes = null;
        fireMaterial = null;
        flameMeshes = null;
        smokeMesh = null;
        steamMesh = null;
        DeleteOldGeneratedMeshes();

        // Smooth fairway center line
        var path = new List<Vector2>();
        for (int i = 1; i < MenuPathControls.Length - 2; i++)
            for (int k = 0; k < 20; k++)
                path.Add(CatmullRom(MenuPathControls[i - 1], MenuPathControls[i], MenuPathControls[i + 1], MenuPathControls[i + 2], k / 20f));
        path.Add(MenuPathControls[MenuPathControls.Length - 2]);
        menuPath = path.ToArray();

        var root = new GameObject("Backdrop");
        Material mat = GetPropMaterial();

        BuildMenuTerrain(root.transform, mat);
        BuildMenuWater(root.transform, mat);
        BuildMenuFlag(root.transform, mat);
        BuildMenuTeeMarkers(root.transform, mat);
        BuildMenuTrees(root.transform, mat);
        BuildMenuMountains(root.transform, mat);
        BuildClouds(root.transform);
        BuildMenuSun(root.transform);
        BuildMenuSecurityCamera(root.transform, mat);

        AssetDatabase.SaveAssets();
        return root;
    }

    // ---- Ground ----

    static float MenuPathDistance(Vector2 p) => DistanceToPolyline(p, menuPath);

    /// Ground height at a world (x, z) point.
    static float MenuHeight(Vector2 p)
    {
        // The tee hill: a flat top where you stand, falling away to the valley
        float hill = Mathf.Lerp(-0.3f, MenuValleyY - 2f, Smooth(6f, 42f, p.magnitude));

        // Rolling valley floor, calmer on the fairway and green
        float calm = Smooth(8f, 24f, MenuPathDistance(p));
        float valley = MenuValleyY + (Noise(p, 0.02f, 3f) - 0.5f) * 7f * Mathf.Lerp(0.2f, 1f, calm);

        // Hills all around the edge of the valley
        float q = Ellipse(p, MenuBowlCenter, MenuBowlRadius);
        valley += Smooth(0.72f, 1.2f, q) * 48f + (Noise(p, 0.035f, 9f) - 0.5f) * 14f * Smooth(0.7f, 1.05f, q);

        // Flat green
        float g = Ellipse(p, MenuGreenCenter, MenuGreenRadius);
        valley = Mathf.Lerp(MenuValleyY + 0.6f, valley, Smooth(1f, 1.7f, g));

        // Sunken bunkers and pond
        foreach (var b in MenuBunkers)
        {
            float e = Ellipse(p, b.center, b.radius);
            if (e < 1f) valley -= 0.9f * (1f - e * e);
        }
        float pond = Ellipse(p, MenuPondCenter, MenuPondRadius);
        valley = Mathf.Lerp(MenuWaterY - 1.5f, valley, Smooth(0.75f, 1.3f, pond));

        return Mathf.Max(hill, valley);
    }

    static void BuildMenuTerrain(Transform parent, Material mat)
    {
        const float minX = -180f, maxX = 180f, minZ = -120f, maxZ = 300f, cell = 5f;
        int nx = Mathf.CeilToInt((maxX - minX) / cell), nz = Mathf.CeilToInt((maxZ - minZ) / cell);
        var heights = new float[nx + 1, nz + 1];
        for (int i = 0; i <= nx; i++)
            for (int j = 0; j <= nz; j++)
                heights[i, j] = MenuHeight(new Vector2(minX + i * cell, minZ + j * cell));

        // NES greens and sand
        Color fairwayA = Hex(0x58D854), fairwayB = Hex(0x00B800);
        Color greenA = Hex(0xB8F818), greenB = Hex(0x58D854);
        Color firstCut = Hex(0x00A800), roughA = Hex(0x007800), roughB = Hex(0x006800), deep = Hex(0x005800);
        Color sand = Hex(0xF8D878), shore = Hex(0xFCE0A8), tee = Hex(0x58D854);
        Color A(Color c) { c.a = 0f; return c; }

        var mb = new MeshBuilder();
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
            {
                float x0 = minX + i * cell, z0 = minZ + j * cell;
                var c = new Vector2(x0 + cell * 0.5f, z0 + cell * 0.5f);
                bool odd = (i + j) % 2 == 1;
                float h = (heights[i, j] + heights[i + 1, j] + heights[i, j + 1] + heights[i + 1, j + 1]) * 0.25f;

                Color col;
                bool inBunker = false;
                foreach (var b in MenuBunkers)
                    if (Ellipse(c, b.center, b.radius) < 1f) { inBunker = true; break; }
                float d = MenuPathDistance(c);

                if (Ellipse(c, MenuPondCenter, MenuPondRadius) < 1.15f && h < MenuWaterY + 0.6f) col = shore;
                else if (inBunker) col = sand;
                else if (c.magnitude < 7f) col = tee;
                else if (Ellipse(c, MenuGreenCenter, MenuGreenRadius) < 1f) col = odd ? greenA : greenB;
                else if (d < 8f && c.y > 20f) col = odd ? fairwayA : fairwayB;
                else if (d < 11f && c.y > 18f) col = firstCut;
                else if (h > MenuValleyY + 20f) col = deep;
                else col = Noise(c, 0.08f, 5f) > 0.5f ? roughA : roughB;
                col = A(col);

                var p00 = new Vector3(x0, heights[i, j], z0);
                var p10 = new Vector3(x0 + cell, heights[i + 1, j], z0);
                var p01 = new Vector3(x0, heights[i, j + 1], z0 + cell);
                var p11 = new Vector3(x0 + cell, heights[i + 1, j + 1], z0 + cell);
                mb.Tri(p00, p01, p11, Vector3.up, col);
                mb.Tri(p00, p11, p10, Vector3.up, col);
            }

        var go = new GameObject("Valley");
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh(S.MeshPrefix + "Valley");
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
    }

    static void BuildMenuWater(Transform parent, Material mat)
    {
        var mb = new MeshBuilder();
        Color water = new Color(0f, 0.35f, 0.97f, 0f);
        const int sides = 32;
        var center = new Vector3(MenuPondCenter.x, MenuWaterY, MenuPondCenter.y);
        for (int k = 0; k < sides; k++)
        {
            float a0 = k * Mathf.PI * 2f / sides, a1 = (k + 1) * Mathf.PI * 2f / sides;
            Vector3 Edge(float a) => center + new Vector3(Mathf.Cos(a) * MenuPondRadius.x, 0f, Mathf.Sin(a) * MenuPondRadius.y) * 1.02f;
            mb.Tri(center, Edge(a0), Edge(a1), Vector3.up, water);
        }
        var go = new GameObject("Pond");
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh(S.MeshPrefix + "Pond");
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
    }

    /// A giant flag on the green, so it reads all the way from the tee.
    static void BuildMenuFlag(Transform parent, Material mat)
    {
        const float height = 2.2f;
        var mb = new MeshBuilder();
        for (int b = 0; b < 8; b++)
            mb.Cylinder(0.03f, height * b / 8f, height * (b + 1) / 8f, 8, b % 2 == 0 ? S.PinColor : S.PinBandColor);
        var top = new Vector3(0f, height - 0.02f, 0f);
        var low = new Vector3(0f, height - 0.52f, 0f);
        var tip = new Vector3(0.75f, height - 0.27f, 0f);
        mb.Tri(top, low, tip, Vector3.back, S.FlagColor);
        mb.Tri(top, low, tip, Vector3.forward, S.FlagColor * 0.85f);

        Vector2 cupSpot = MenuGreenCenter + new Vector2(1f, 1f);
        var go = new GameObject("Flag");
        go.transform.SetParent(parent, false);
        go.transform.position = new Vector3(cupSpot.x, MenuHeight(cupSpot), cupSpot.y);
        go.transform.localScale = Vector3.one * 6f;
        go.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh(S.MeshPrefix + "Flag");
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
    }

    /// Blue tee markers on the platform, either side of the player.
    static void BuildMenuTeeMarkers(Transform parent, Material mat)
    {
        var mb = new MeshBuilder();
        mb.Blob(Vector3.zero, 0.14f, 4, 7, S.TeeMarkerColor, 3, 0.1f);
        Mesh mesh = mb.ToMesh(S.MeshPrefix + "TeeMarker");
        foreach (float x in new[] { -1.6f, 1.6f })
        {
            var m = new GameObject("TeeMarker");
            m.transform.SetParent(parent, false);
            m.transform.position = new Vector3(x, 0.1f, 1.8f);
            m.AddComponent<MeshFilter>().sharedMesh = mesh;
            m.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }
    }

    static void BuildMenuTrees(Transform parent, Material mat)
    {
        var meshes = new List<Mesh>();
        for (int v = 0; v < 3; v++)
        {
            var pine = new MeshBuilder();
            pine.Cylinder(0.14f, -0.3f, 1.0f, 6, S.Trunk);
            pine.Cone(1.25f, 0.7f, 1.6f, 8, S.PineColors[v]);
            pine.Cone(0.95f, 1.4f, 1.4f, 8, S.PineColors[v] * 1.1f);
            pine.Cone(0.65f, 2.1f, 1.2f, 8, S.PineColors[v] * 1.2f);
            meshes.Add(pine.ToMesh(S.MeshPrefix + "Pine" + v));

            var round = new MeshBuilder();
            round.Cylinder(0.16f, -0.3f, S.RoundTrunkTop, 6, S.Trunk);
            round.Blob(new Vector3(0f, S.RoundLeavesY, 0f), S.RoundLeavesRadius, 4, 8, S.RoundColors[v], 20 + v, 0.2f);
            meshes.Add(round.ToMesh(S.MeshPrefix + "RoundTree" + v));
        }

        var treeParent = new GameObject("Trees").transform;
        treeParent.SetParent(parent, false);
        var rng = new System.Random(RandomSeed + 300);
        var placed = new List<Vector2>();
        for (int attempt = 0; attempt < 3000 && placed.Count < 260; attempt++)
        {
            var p = new Vector2(Mathf.Lerp(-170f, 170f, (float)rng.NextDouble()), Mathf.Lerp(-60f, 290f, (float)rng.NextDouble()));
            if (p.magnitude < 18f) continue;                                   // keep the view off the tee clear
            if (MenuPathDistance(p) < 14f && p.y > 12f) continue;              // off the fairway
            if (Ellipse(p, MenuGreenCenter, MenuGreenRadius) < 1.9f) continue;
            if (Ellipse(p, MenuPondCenter, MenuPondRadius) < 1.4f) continue;
            if (Ellipse(p, MenuBowlCenter, MenuBowlRadius) > 1.35f) continue;  // not up on the far mountainsides
            bool inBunker = false;
            foreach (var b in MenuBunkers)
                if (Ellipse(p, b.center, b.radius) < 1.6f) { inBunker = true; break; }
            if (inBunker || IsCrowded(placed, p, 5f)) continue;
            placed.Add(p);

            var tree = new GameObject("Tree");
            tree.transform.SetParent(treeParent, false);
            tree.transform.position = new Vector3(p.x, MenuHeight(p), p.y);
            tree.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            tree.transform.localScale = Vector3.one * Mathf.Lerp(2.4f, 3.6f, (float)rng.NextDouble());
            tree.AddComponent<MeshFilter>().sharedMesh = meshes[rng.Next(meshes.Count)];
            tree.AddComponent<MeshRenderer>().sharedMaterial = mat;
            GameObjectUtility.SetStaticEditorFlags(tree, StaticEditorFlags.BatchingStatic);
        }
    }

    /// A ring of big low-poly mountains on the horizon, some with snowy peaks.
    static void BuildMenuMountains(Transform parent, Material mat)
    {
        Color green = new Color(0f, 0.35f, 0f, 0f), gray = new Color(0.49f, 0.49f, 0.49f, 0f), snow = new Color(0.99f, 0.99f, 0.99f, 0f);
        var meshes = new List<Mesh>();
        for (int v = 0; v < 2; v++)
        {
            var mb = new MeshBuilder();
            Color body = v == 0 ? green : gray;
            mb.Cone(1f, 0f, 1f, 7, body);
            if (v == 1) mb.Cone(0.34f, 0.66f, 0.345f, 7, snow); // snow cap over the top third
            meshes.Add(mb.ToMesh(S.MeshPrefix + "Mountain" + v));
        }

        var mountainParent = new GameObject("Mountains").transform;
        mountainParent.SetParent(parent, false);
        var rng = new System.Random(RandomSeed + 301);
        const int count = 24;
        for (int k = 0; k < count; k++)
        {
            float angle = (k + (float)rng.NextDouble() * 0.5f) / count * Mathf.PI * 2f;
            float distance = Mathf.Lerp(330f, 400f, (float)rng.NextDouble());
            float height = Mathf.Lerp(70f, 150f, (float)rng.NextDouble());
            float radius = Mathf.Lerp(60f, 95f, (float)rng.NextDouble());
            var m = new GameObject("Mountain");
            m.transform.SetParent(mountainParent, false);
            m.transform.position = new Vector3(MenuBowlCenter.x + Mathf.Cos(angle) * distance, MenuValleyY, MenuBowlCenter.y + Mathf.Sin(angle) * distance);
            m.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            m.transform.localScale = new Vector3(radius, height, radius);
            m.AddComponent<MeshFilter>().sharedMesh = meshes[height > 110f ? 1 : 0];
            m.AddComponent<MeshRenderer>().sharedMaterial = mat;
            GameObjectUtility.SetStaticEditorFlags(m, StaticEditorFlags.BatchingStatic);
        }
    }

    /// A chunky 8-bit sun up in the sky, off to the left of the title.
    static void BuildMenuSun(Transform parent)
    {
        EnsureFireAssets(); // unlit material
        var mb = new MeshBuilder();
        void Disc(float radius, float z, Color col)
        {
            const int sides = 8;
            for (int k = 0; k < sides; k++)
            {
                float a0 = (k + 0.5f) * Mathf.PI * 2f / sides, a1 = (k + 1.5f) * Mathf.PI * 2f / sides;
                mb.Tri(new Vector3(0f, 0f, z),
                       new Vector3(Mathf.Cos(a0) * radius, Mathf.Sin(a0) * radius, z),
                       new Vector3(Mathf.Cos(a1) * radius, Mathf.Sin(a1) * radius, z), Vector3.back, col);
            }
        }
        Disc(1f, 0f, new Color(0.99f, 0.63f, 0.27f, 0f));    // NES orange glow
        Disc(0.78f, -0.01f, new Color(0.97f, 0.72f, 0f, 0f)); // NES yellow sun, just in front

        Vector3 direction = new Vector3(-0.6f, 0.5f, 1f).normalized;
        var sun = new GameObject("Sun");
        sun.transform.SetParent(parent, false);
        sun.transform.position = direction * 480f;
        sun.transform.rotation = Quaternion.LookRotation(direction); // the disc faces back at the player
        sun.transform.localScale = Vector3.one * 34f;
        sun.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh(S.MeshPrefix + "Sun");
        sun.AddComponent<MeshRenderer>().sharedMaterial = fireMaterial;
    }

    /// A security camera on the tee hill, right next to the player, watching. Its red light blinks on and off.
    static void BuildMenuSecurityCamera(Transform parent, Material mat)
    {
        var group = new GameObject("SecurityCameras");
        group.transform.SetParent(parent, false);
        var system = group.AddComponent<SecurityCameraSystem>();
        system.offDurationMin = system.offDurationMax = 2.5f;
        system.onDurationMin = system.onDurationMax = 2.5f;
        system.startOff = false;

        PlaceSecurityCamera(group.transform, "SecurityCamera", new Vector3(4.4f, -0.3f, 3.4f), new Vector3(0f, 1.6f, 0f),
                            MakeSecurityCameraKit(mat));
    }
}
