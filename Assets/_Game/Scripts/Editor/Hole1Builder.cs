using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds golf holes as one smooth, hilly terrain: an elevated tee, a curving fairway, sunken bunkers,
/// a contoured green with a real cup, ponds, rolling rough, hills framing the hole, and trees.
/// Each hole is a HoleLayout (see Hole1Layout / Hole2Layout below).
///
///   Golf > Build Retro Hole 1           -> RetroHole1 scene, NES-style 8-bit look.
///   Golf > Build Retro Hole 2 (Meteor)  -> RetroHole2 scene (created automatically): par 4 dogleg right with a
///                                          giant meteorite in its crater at the turn, thick woods inside the
///                                          dogleg, and one narrow gap straight through the trees to the green.
///   Golf > Build Retro Hole 3 (Terrible Green) -> RetroHole3 scene (created automatically): short par 3 to a
///                                          comically unputtable green, plus a hidden flat pool-table green.
///                                          Call GreenSwap.SetPoolTable(true) to switch (for the cheat).
///   Golf > Build Retro Hole 4 (Nuclear) -> RetroHole4 scene (created automatically): par 4. A nuclear plant dumps
///                                          into a glowing river you must carry off the tee, then the fairway
///                                          veers left down a stretch lined with radioactive barrels.
///   Golf > Build Hole 1 (Realistic)     -> Hole1 scene: natural colors, stripes, fog.
///
/// Running a build again deletes and rebuilds everything under "HoleN_Course" in that scene.
/// To change the hole, edit the layout values below and re-run instead of hand-editing generated pieces.
///
/// Coordinates: the player tees off at the world origin facing +Z. The hole is about 65 m long and doglegs right.
/// </summary>
public static class Hole1Builder
{
    const string MaterialFolder = "Assets/_Game/Materials";
    const string MeshFolder = "Assets/_Game/Models/Generated";
    const string VertexColorShader = "Golf/VertexColorLit";
    const string SkyShader = "Golf/RetroSky";
    const string PixelShader = "Golf/PixelRetro";
    const string BakedShader = "Golf/PixelBaked";
    const string TextureFolder = "Assets/_Game/Textures/Generated";

    // Bump this when the builder's retro material settings change, so existing retro materials get updated.
    const string RetroMaterialVersion = "nes-3";

    // ================= RETRO PIXEL BAKE =================

    const float BakePixelSize = 0.1f;         // size of one 8-bit "pixel" on the ground, in meters
    const float BakeCheckerContrast = 0.12f;
    const bool BakeDither = false;            // true = checkered blending between colors; false = solid colors
    const float BakeSpeckleDensity = 0.08f;   // share of ground pixels that get a random dark speck
    const float BakeSunStrength = 0.5f;
    static readonly Quaternion RetroSunRotation = Quaternion.Euler(55f, -30f, 0f);
    static readonly Color RetroSunColor = new Color(1f, 0.98f, 0.92f);
    const float RetroSunIntensity = 1.2f;
    static readonly Color BakeAmbientSky = new Color(0.75f, 0.75f, 0.78f);
    static readonly Color BakeAmbientGround = new Color(0.45f, 0.45f, 0.45f);

    // A subset of the classic NES palette (sRGB), matching the PixelRetro shader.
    static readonly Color[] NesPalette =
    {
        Hex(0x000000), Hex(0x7C7C7C), Hex(0xBCBCBC), Hex(0xFCFCFC),
        Hex(0x005800), Hex(0x006800), Hex(0x007800), Hex(0x00A800),
        Hex(0x00B800), Hex(0x58D854), Hex(0xB8F818), Hex(0xD8F878),
        Hex(0xB8F8B8), Hex(0x00A844), Hex(0x503000), Hex(0x881400),
        Hex(0xAC7C00), Hex(0xF8B800), Hex(0xF8D878), Hex(0xFCE0A8),
        Hex(0xFCA044), Hex(0xA81000), Hex(0xF83800), Hex(0x0058F8),
        Hex(0x0078F8), Hex(0x3CBCFC), Hex(0xA4E4FC), Hex(0x008888),
    };

    static readonly int[] Bayer4x4 = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

    static Color Hex(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

    static readonly string[] XROriginPrefabPaths =
    {
        "Assets/_Game/Prefabs/XR Origin (XR Rig).prefab",
        "Assets/Samples/XR Interaction Toolkit/3.6.1/Starter Assets/Prefabs/XR Origin (XR Rig).prefab",
    };

    // ================= HOLE LAYOUTS (meters) =================

    // All layout values are in "design meters". Each style's Scale and HeightScale stretch them into the
    // real world, so a whole hole can be made bigger without re-tuning anything.
    class HoleLayout
    {
        public string Name, Label, RetroScene, RealisticScene, TerrainMaterialName;
        public float MinX, MaxX, MinZ, MaxZ;  // terrain bounds
        public float Cell;                    // terrain grid size override in world meters (0 = use the style's)
        public Vector2[] Path;                // center line, tee to green (first and last points only steer the curve)
        public float FairwayStart, FairwayEnd; // where along the path (0 = tee, 1 = green) the mown fairway runs
        public Vector2 TeeCenter, TeeHalfSize, GreenCenter, GreenRadius;
        public Vector2 GreenSlope;            // how much the green rises per meter in x and z (0.05 = 5%)
        public float GreenRoll;               // height of the gentle humps and dips across the green
        public Vector2 CupTarget;             // snaps to the nearest terrain grid point
        public (Vector2 center, Vector2 radius)[] Bunkers;
        public bool HasPond;
        public Vector2 PondCenter, PondRadius;
        public int TreeCount;
        public Vector2 CloudCenter;

        // Hole 2 extras
        public bool HasMeteor;
        public Vector2 MeteorCenter;
        public float MeteorRadius;            // how wide the rock is (its crater scales with this)
        public float MeteorHeight;            // how tall the rock is compared to its width (1 = round)
        public bool HasShortcut;              // a cleared lane through the trees, straight to the green
        public Vector2 ShortcutStart, ShortcutEnd;
        public float ShortcutHalfWidth;
        public bool SeparateGreen;            // Hole 3: the green is its own swappable object (terrible green / pool table)

        // Hole 4 extras
        public bool HasRiver;                 // a glowing radioactive river cutting across the hole
        public Vector2[] RiverPoints;         // its course (straight pieces between these points)
        public float RiverHalfWidth;
        public bool HasPowerPlant;            // nuclear power plant on a flat concrete pad
        public Vector2 PlantCenter, PlantHalfSize;
        public float PlantPadHeight;
        public bool HasBarrels;               // radioactive barrels lining the fairway
        public float BarrelStart, BarrelEnd;  // where along the hole (0..1) the barrels run
        public bool InsideForest;             // a band of thick woods along the inside of the dogleg
        public float ForestDepth;             // how thick that band of woods is
        public float ForestMaxT;              // how far along the hole (0..1) the woods band runs
    }

    /// Hole 1: par 3-ish gentle dogleg right with a pond on the left.
    static HoleLayout Hole1Layout() => new HoleLayout
    {
        Name = "Hole1",
        Label = "Hole 1",
        RetroScene = "Assets/_Game/Scenes/RetroHole1.unity",
        RealisticScene = "Assets/_Game/Scenes/Hole1.unity",
        TerrainMaterialName = "Terrain",
        MinX = -28f, MaxX = 28f, MinZ = -14f, MaxZ = 109f,
        Path = new[]
        {
            new Vector2(0f, -6f),
            new Vector2(0f, 0f),       // tee
            new Vector2(0.5f, 22.4f),
            new Vector2(3.5f, 44.8f),
            new Vector2(8.5f, 64.4f),
            new Vector2(10f, 86.8f),   // green
            new Vector2(10f, 98f),
        },
        FairwayStart = 0.08f, FairwayEnd = 0.93f,
        TeeCenter = new Vector2(0f, 0f),
        TeeHalfSize = new Vector2(2.2f, 1.8f),
        GreenCenter = new Vector2(10f, 86.8f),
        GreenRadius = new Vector2(4f, 4.5f),
        GreenSlope = new Vector2(0.018f, -0.012f),
        GreenRoll = 0.06f,
        CupTarget = new Vector2(10.3f, 88.3f),
        Bunkers = new[]
        {
            (new Vector2(-5.5f, 33.6f), new Vector2(2.2f, 3.2f)),  // left fairway bunker
            (new Vector2(11.8f, 51.8f), new Vector2(2.4f, 3.0f)),  // right fairway bunker (inside the dogleg)
            (new Vector2(4.6f, 85.8f), new Vector2(1.4f, 2.6f)),   // left greenside
            (new Vector2(15.6f, 88.3f), new Vector2(1.3f, 2.2f)),  // right greenside
            (new Vector2(12f, 93f), new Vector2(2.4f, 1.1f)),      // behind the green
        },
        HasPond = true,
        PondCenter = new Vector2(-12f, 58.8f),
        PondRadius = new Vector2(5f, 7f),
        TreeCount = 110,
        CloudCenter = new Vector2(5f, 48f),
    };

    /// Hole 2: par 4 dogleg right. A comically huge meteorite sits in its crater right where the fairway
    /// turns, and thick woods fill the inside of the dogleg, except for one narrow gap that runs
    /// straight from the fairway to the green.
    static HoleLayout Hole2Layout() => new HoleLayout
    {
        Name = "Hole2",
        Label = "Hole 2",
        RetroScene = "Assets/_Game/Scenes/RetroHole2.unity",
        RealisticScene = null,
        TerrainMaterialName = "Hole2_Terrain",
        MinX = -28f, MaxX = 62f, MinZ = -14f, MaxZ = 102f,
        Cell = 0.8f, // bigger hole, slightly coarser grid to stay light on the Quest 2
        Path = new[]
        {
            new Vector2(0f, -6f),
            new Vector2(0f, 0f),       // tee
            new Vector2(0f, 26f),
            new Vector2(1.5f, 50f),    // fairway starts turning right here...
            new Vector2(8f, 66f),
            new Vector2(22f, 78f),
            new Vector2(40f, 84f),     // green
            new Vector2(48f, 85f),
        },
        FairwayStart = 0.07f, FairwayEnd = 0.94f,
        TeeCenter = new Vector2(0f, 0f),
        TeeHalfSize = new Vector2(2.2f, 1.8f),
        GreenCenter = new Vector2(40f, 84f),
        GreenRadius = new Vector2(4.5f, 4f),
        GreenSlope = new Vector2(0.05f, 0.025f), // tilts down toward the front-left, where you approach from
        GreenRoll = 0.15f,                       // plus some humps and dips to read
        CupTarget = new Vector2(40.5f, 84.3f),
        Bunkers = new[]
        {
            (new Vector2(-6.5f, 44f), new Vector2(2f, 3.5f)),     // left of the fairway, before the meteor
            (new Vector2(18f, 66f), new Vector2(2.2f, 1.6f)),     // inside corner of the dogleg
            (new Vector2(35f, 87.5f), new Vector2(1.4f, 2f)),     // back-left of the green
            (new Vector2(44.5f, 80.5f), new Vector2(1.6f, 1.4f)), // front-right of the green
            (new Vector2(46f, 87f), new Vector2(1.2f, 2.2f)),     // right of the green
        },
        HasPond = true,
        PondCenter = new Vector2(-9f, 74f),   // outside of the turn, past the meteor
        PondRadius = new Vector2(5f, 4.5f),
        TreeCount = 70,
        CloudCenter = new Vector2(20f, 45f),
        HasMeteor = true,
        MeteorCenter = new Vector2(3f, 56.5f), // dead center of the fairway, right where it turns
        MeteorRadius = 6f,                     // ~24 m wide in the world...
        MeteorHeight = 2.4f,                   // ...and ~28 m tall
        HasShortcut = true,
        ShortcutStart = new Vector2(0.5f, 18f),   // leaves the fairway ~36 m off the tee
        ShortcutEnd = new Vector2(37.5f, 81.5f),  // front-left of the green
        ShortcutHalfWidth = 3f,                   // ~12 m wide gap
        InsideForest = true,
        ForestDepth = 6f,                         // the woods are a ~12 m deep wall, so the gap is short
        ForestMaxT = 0.6f,                        // woods line the first stretch, up to the turn
    };

    /// Hole 3: a short par 3 to a comically terrible green: the cup sits on top of a steep mound ringed by a
    /// trench, the whole green tilts hard, and wild ripples run across it. It isn't meant to be puttable.
    /// The green is a separate object with a flat "pool table" twin (see GreenSwap) for the cheat.
    static HoleLayout Hole3Layout() => new HoleLayout
    {
        Name = "Hole3",
        Label = "Hole 3",
        RetroScene = "Assets/_Game/Scenes/RetroHole3.unity",
        RealisticScene = null,
        TerrainMaterialName = "Hole3_Terrain",
        MinX = -28f, MaxX = 32f, MinZ = -14f, MaxZ = 76f,
        Path = new[]
        {
            new Vector2(0f, -6f),
            new Vector2(0f, 0f),     // tee (high up, looking down at the green)
            new Vector2(1f, 20f),
            new Vector2(3f, 40f),
            new Vector2(4f, 55f),    // green
            new Vector2(4f, 62f),
        },
        FairwayStart = 0.35f, FairwayEnd = 0.9f, // par 3: just an apron of fairway in front of the green
        TeeCenter = new Vector2(0f, 0f),
        TeeHalfSize = new Vector2(2.2f, 1.8f),
        GreenCenter = new Vector2(4f, 55f),
        GreenRadius = new Vector2(6f, 5f),       // big, so there's room for all the terribleness
        GreenSlope = Vector2.zero,               // the ground under the green is flat; the green object does the rest
        GreenRoll = 0f,
        CupTarget = new Vector2(5f, 56f),
        Bunkers = new[]
        {
            (new Vector2(-3.5f, 52f), new Vector2(1.6f, 2.6f)), // front-left (where the green tilts toward)
            (new Vector2(11.5f, 56f), new Vector2(1.4f, 2.4f)), // right
            (new Vector2(4f, 61.5f), new Vector2(3f, 1.2f)),    // behind
        },
        HasPond = true,
        PondCenter = new Vector2(-6f, 32f),     // water short-left
        PondRadius = new Vector2(5f, 6f),
        TreeCount = 90,
        CloudCenter = new Vector2(2f, 30f),
        SeparateGreen = true,
    };

    /// Hole 4: par 4. A nuclear power plant off to the right is dumping into a glowing radioactive river that
    /// cuts right across the hole in front of the tee. Carry it, and the fairway veers left; the last stretch
    /// to the green is lined on both sides with radioactive barrels (some tipped over and leaking).
    static HoleLayout Hole4Layout() => new HoleLayout
    {
        Name = "Hole4",
        Label = "Hole 4",
        RetroScene = "Assets/_Game/Scenes/RetroHole4.unity",
        RealisticScene = null,
        TerrainMaterialName = "Hole4_Terrain",
        MinX = -52f, MaxX = 46f, MinZ = -14f, MaxZ = 100f,
        Cell = 0.8f,
        Path = new[]
        {
            new Vector2(0f, -6f),
            new Vector2(0f, 0f),       // tee
            new Vector2(0f, 22f),      // (the river crosses here)
            new Vector2(-2f, 42f),     // landing area for the tee shot
            new Vector2(-10f, 58f),    // fairway veers left...
            new Vector2(-24f, 72f),    // ...down the barrel-lined stretch...
            new Vector2(-34f, 84f),    // ...to the green
            new Vector2(-40f, 90f),
        },
        FairwayStart = 0.3f, FairwayEnd = 0.94f, // fairway starts on the far side of the river
        TeeCenter = new Vector2(0f, 0f),
        TeeHalfSize = new Vector2(2.2f, 1.8f),
        GreenCenter = new Vector2(-34f, 84f),
        GreenRadius = new Vector2(4.5f, 4.5f),
        GreenSlope = new Vector2(0.02f, -0.015f),
        GreenRoll = 0.06f,
        CupTarget = new Vector2(-33.5f, 84.5f),
        Bunkers = new[]
        {
            (new Vector2(3f, 57f), new Vector2(2.2f, 3f)),       // outside of the turn
            (new Vector2(-28.5f, 81f), new Vector2(1.4f, 2.2f)), // front-right of the green
            (new Vector2(-38f, 89.5f), new Vector2(2.2f, 1.2f)), // behind the green
        },
        HasPond = false,
        TreeCount = 80,
        CloudCenter = new Vector2(0f, 45f),
        HasRiver = true,
        RiverPoints = new[]
        {
            new Vector2(34f, 22f),     // flows out of the power plant...
            new Vector2(22f, 21f),
            new Vector2(10f, 20.5f),
            new Vector2(0f, 21.5f),    // ...right across the hole in front of the tee...
            new Vector2(-10f, 23f),
            new Vector2(-22f, 22f),
            new Vector2(-36f, 24f),
            new Vector2(-50f, 26f),    // ...and off the edge of the course
            new Vector2(-60f, 27f),
        },
        RiverHalfWidth = 3f,           // ~12 m wide
        HasPowerPlant = true,
        PlantCenter = new Vector2(32f, 40f),
        PlantHalfSize = new Vector2(11f, 12f),
        PlantPadHeight = 0.6f,
        HasBarrels = true,
        BarrelStart = 0.52f, BarrelEnd = 0.9f,
    };

    // Shortcuts to the current layout's values (so the rest of the code reads naturally)
    static float DesignMinX => L.MinX;
    static float DesignMaxX => L.MaxX;
    static float DesignMinZ => L.MinZ;
    static float DesignMaxZ => L.MaxZ;
    static Vector2[] PathControlPoints => L.Path;
    static float FairwayStart => L.FairwayStart;
    static float FairwayEnd => L.FairwayEnd;
    static Vector2 TeeCenter => L.TeeCenter;
    static Vector2 TeeHalfSize => L.TeeHalfSize;
    static Vector2 GreenCenter => L.GreenCenter;
    static Vector2 GreenRadius => L.GreenRadius;
    static Vector2 CupTarget => L.CupTarget;
    static (Vector2 center, Vector2 radius)[] Bunkers => L.Bunkers;
    static Vector2 PondCenter => L.PondCenter;
    static Vector2 PondRadius => L.PondRadius;
    static int TreeCount => L.TreeCount;
    static string RootName => L.Name + "_Course";

    const float TeeRaise = 0.3f;
    const float CupRadius = 0.1f;   // ~2x a real cup, so putting is forgiving in VR
    const float CupDepth = 0.12f;
    const float BunkerDepth = 0.45f;
    const int RandomSeed = 2026;

    // Terrain bounds in real-world meters
    static float GridMinX => DesignMinX * S.Scale;
    static float GridMaxX => DesignMaxX * S.Scale;
    static float GridMinZ => DesignMinZ * S.Scale;
    static float GridMaxZ => DesignMaxZ * S.Scale;

    /// Converts a layout (design) position to a real-world position.
    static Vector2 ToWorld(Vector2 design) => design * S.Scale;

    // ================= STYLES =================

    class Style
    {
        public string Label, ScenePath, MeshPrefix, MaterialPrefix, TerrainShader;
        public bool Retro, Fog;
        public float Scale;       // how much bigger the hole is than the layout numbers (horizontal)
        public float HeightScale; // how much taller the hills, tee and slopes are
        public float TreeScale;   // size of trees, bushes and posts
        public float Cell;        // terrain grid size in meters; smaller = smoother but heavier
        public float HillScale;   // exaggerates the rough, mounds and surrounding hills
        public Color RoughA, RoughB, DeepRough, FirstCut, FairwayDark, FairwayLight, Fringe, GreenDark, GreenLight;
        public Color Sand, Shore, CupLiner, CupInside, Trunk, PinColor, PinBandColor, FlagColor, TeeMarkerColor;
        public Color WaterDeep, WaterShallow;
        public Color Dirt, ScorchedDirt, MeteorRock, MeteorHot, MeteorHotBright;
        public Color[] PineColors, RoundColors;
        public float FairwayChecker, GreenChecker, TeeChecker; // checker square size in meters (0 = stripes instead)
        public float RoundTrunkTop, RoundLeavesY, RoundLeavesRadius;
    }

    // NES-style retro look. Colors are picked from the NES palette; the PixelRetro shader snaps
    // everything to that palette in chunky world-space pixels. Alpha = how much pixel "speckle"
    // (grass tufts, sand grains, leaves) the surface gets.
    static Style RetroStyle() => new Style
    {
        Label = "Retro Hole 1",
        ScenePath = "Assets/_Game/Scenes/RetroHole1.unity",
        MeshPrefix = "RetroHole1_",
        MaterialPrefix = "Nes_",
        TerrainShader = PixelShader,
        Retro = true,
        Fog = false,
        Scale = 2f,          // ~130 m hole
        HeightScale = 1.5f,
        TreeScale = 1.7f,
        Cell = 0.7f,
        HillScale = 1.35f,
        RoughA = new Color(0.00f, 0.50f, 0.00f, 1f),
        RoughB = new Color(0.00f, 0.55f, 0.02f, 1f),
        DeepRough = new Color(0.00f, 0.38f, 0.00f, 1f),
        FirstCut = new Color(0.00f, 0.66f, 0.00f, 0.6f),
        FairwayDark = new Color(0.20f, 0.79f, 0.18f, 0.2f),
        FairwayLight = new Color(0.20f, 0.79f, 0.18f, 0.2f),
        Fringe = new Color(0.00f, 0.72f, 0.00f, 0.4f),
        GreenDark = new Color(0.60f, 0.93f, 0.28f, 0.05f),
        GreenLight = new Color(0.60f, 0.93f, 0.28f, 0.05f),
        Sand = new Color(0.97f, 0.85f, 0.47f, 0.85f),
        Shore = new Color(0.99f, 0.88f, 0.66f, 0.5f),
        CupLiner = new Color(0.99f, 0.99f, 0.99f, 0f),
        CupInside = new Color(0f, 0f, 0f, 0f),
        Trunk = new Color(0.55f, 0.20f, 0.02f, 0f),
        PinColor = new Color(0.99f, 0.99f, 0.99f, 0f),
        PinBandColor = new Color(0.97f, 0.22f, 0f, 0f),
        FlagColor = new Color(0.97f, 0.22f, 0f, 0f),
        TeeMarkerColor = new Color(0f, 0.35f, 0.97f, 0f),
        WaterDeep = new Color(0f, 0.35f, 0.97f, 0.3f),
        WaterShallow = new Color(0.24f, 0.74f, 0.99f, 0.3f),
        Dirt = new Color(0.67f, 0.49f, 0f, 0.9f),            // NES brown-yellow
        ScorchedDirt = new Color(0.31f, 0.19f, 0f, 1f),      // NES dark brown
        MeteorRock = new Color(0.49f, 0.49f, 0.49f, 0f),     // NES gray
        MeteorHot = new Color(0.97f, 0.22f, 0f, 0f),         // NES red-orange
        MeteorHotBright = new Color(0.99f, 0.63f, 0.27f, 0f), // NES orange
        PineColors = new[] { new Color(0f, 0.35f, 0f, 0.5f), new Color(0f, 0.41f, 0f, 0.5f), new Color(0f, 0.47f, 0f, 0.5f) },
        RoundColors = new[] { new Color(0f, 0.66f, 0f, 0.6f), new Color(0f, 0.72f, 0f, 0.6f), new Color(0.35f, 0.85f, 0.33f, 0.6f) },
        FairwayChecker = 4.0f,
        GreenChecker = 1.5f,
        TeeChecker = 1.5f,
        RoundTrunkTop = 1.9f,
        RoundLeavesY = 2.7f,
        RoundLeavesRadius = 1.25f,
    };

    static Style RealisticStyle() => new Style
    {
        Label = "Hole 1",
        ScenePath = "Assets/_Game/Scenes/Hole1.unity",
        MeshPrefix = "Hole1_",
        MaterialPrefix = "Hole_",
        TerrainShader = VertexColorShader,
        Retro = false,
        Fog = true,
        Scale = 1f,
        HeightScale = 1f,
        TreeScale = 1f,
        Cell = 0.4f,
        HillScale = 1f,
        RoughA = new Color(0.17f, 0.38f, 0.14f),
        RoughB = new Color(0.24f, 0.46f, 0.16f),
        DeepRough = new Color(0.17f, 0.38f, 0.14f),
        FirstCut = new Color(0.24f, 0.46f, 0.16f),
        FairwayDark = new Color(0.33f, 0.64f, 0.22f),
        FairwayLight = new Color(0.40f, 0.73f, 0.27f),
        Fringe = new Color(0.30f, 0.60f, 0.22f),
        GreenDark = new Color(0.40f, 0.78f, 0.30f),
        GreenLight = new Color(0.46f, 0.85f, 0.34f),
        Sand = new Color(0.93f, 0.84f, 0.60f),
        Shore = new Color(0.33f, 0.30f, 0.20f),
        CupLiner = new Color(0.95f, 0.95f, 0.95f),
        CupInside = new Color(0.12f, 0.10f, 0.08f),
        Trunk = new Color(0.40f, 0.27f, 0.16f),
        PinColor = new Color(0.95f, 0.95f, 0.95f),
        PinBandColor = new Color(0.95f, 0.95f, 0.95f),
        FlagColor = new Color(0.90f, 0.12f, 0.12f),
        TeeMarkerColor = new Color(0.95f, 0.95f, 0.95f),
        WaterDeep = new Color(0.16f, 0.42f, 0.62f),
        WaterShallow = new Color(0.16f, 0.42f, 0.62f),
        Dirt = new Color(0.45f, 0.35f, 0.22f),
        ScorchedDirt = new Color(0.20f, 0.15f, 0.11f),
        MeteorRock = new Color(0.35f, 0.33f, 0.32f),
        MeteorHot = new Color(0.90f, 0.30f, 0.05f),
        MeteorHotBright = new Color(1f, 0.65f, 0.20f),
        PineColors = new[] { new Color(0.13f, 0.36f, 0.17f), new Color(0.16f, 0.42f, 0.20f), new Color(0.11f, 0.31f, 0.20f) },
        RoundColors = new[] { new Color(0.25f, 0.50f, 0.18f), new Color(0.31f, 0.55f, 0.20f), new Color(0.21f, 0.45f, 0.21f) },
        FairwayChecker = 0f,
        GreenChecker = 0f,
        TeeChecker = 0f,
        RoundTrunkTop = 1.5f,
        RoundLeavesY = 2.1f,
        RoundLeavesRadius = 1.15f,
    };

    // ================= BUILD STATE (computed each run) =================

    static Style S;
    static HoleLayout L;
    static bool linearColor;
    static float shortcutStartElevation;
    static float riverLevel;
    static float pathLength;       // length of the hole's center line, in layout units
    static List<Vector2> pathPoints;
    static List<float> pathT;      // 0..1 along the path for each point
    static Vector2 cup;
    static int cupI, cupJ;         // grid indices of the cup
    static float teeHeight, greenHeight, waterLevel;
    static float[,] gridHeights;

    [MenuItem("Golf/Build Retro Hole 1")]
    public static void BuildRetro() => Build(RetroStyle(), Hole1Layout());

    [MenuItem("Golf/Build Retro Hole 2 (Meteor)")]
    public static void BuildRetroHole2() => Build(RetroStyle(), Hole2Layout());

    [MenuItem("Golf/Build Retro Hole 4 (Nuclear)")]
    public static void BuildRetroHole4() => Build(RetroStyle(), Hole4Layout());

    [MenuItem("Golf/Build Retro Hole 3 (Terrible Green)")]
    public static void BuildRetroHole3() => Build(RetroStyle(), Hole3Layout());

    [MenuItem("Golf/Build Hole 1 (Realistic)")]
    public static void BuildRealistic() => Build(RealisticStyle(), Hole1Layout());

    static void Build(Style style, HoleLayout layout)
    {
        S = style;
        L = layout;
        linearColor = PlayerSettings.colorSpace == ColorSpace.Linear;
        fireMaterial = null;
        flameMeshes = null;
        smokeMesh = null;
        steamMesh = null;

        // Per-hole names: scene, generated meshes, labels
        S.Label = (S.Retro ? "Retro " : "") + L.Label;
        S.ScenePath = S.Retro ? L.RetroScene : L.RealisticScene;
        S.MeshPrefix = (S.Retro ? "Retro" : "") + L.Name + "_";
        if (L.Cell > 0f) S.Cell = L.Cell;

        if (string.IsNullOrEmpty(S.ScenePath))
        {
            EditorUtility.DisplayDialog("Build " + S.Label, "This hole has no scene set up for this style.", "OK");
            return;
        }
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(S.ScenePath) == null)
        {
            // First build of this hole: make its scene
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Scene created = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EnsureFolder(System.IO.Path.GetDirectoryName(S.ScenePath).Replace('\\', '/'));
            EditorSceneManager.SaveScene(created, S.ScenePath);
        }
        if (SceneManager.GetActiveScene().path != S.ScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(S.ScenePath);
        }
        Scene scene = SceneManager.GetActiveScene();

        try
        {
            Progress("Shaping terrain...", 0.05f);

            GameObject old = GameObject.Find(RootName);
            if (old != null) Undo.DestroyObjectImmediate(old);
            RemoveLeftoverFloors(scene);
            DeleteOldGeneratedMeshes();

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Build " + S.Label);

            Material terrainMat = GetTerrainMaterial();
            Material propMat = S.Retro ? GetPropMaterial() : terrainMat;
            Material ballMat = GetLitMaterial("White", new Color(0.95f, 0.95f, 0.95f), 0.3f);

            PrepareLayout();
            BuildTerrain(root.transform, terrainMat);

            Progress("Adding water, flag and trees...", 0.8f);
            if (L.HasPond) BuildWater(root.transform, S.Retro ? propMat : GetLitMaterial("Water", S.WaterDeep, 0.95f));
            if (L.HasMeteor) BuildMeteor(root.transform, propMat);
            if (L.HasRiver) BuildRiver(root.transform, propMat);
            if (L.HasPowerPlant) BuildPowerPlant(root.transform, propMat);
            if (L.HasBarrels) BuildBarrels(root.transform, propMat);
            if (L.SeparateGreen) BuildSwappableGreens(root.transform, propMat);
            else BuildCupAndFlag(root.transform, propMat);
            BuildTeeMarkers(root.transform, propMat);
            BuildTrees(root.transform, propMat);
            if (S.Retro)
            {
                BuildBushes(root.transform, propMat);
                BuildDistancePosts(root.transform, propMat);
                BuildClouds(root.transform);
            }

            Vector2 tee = ToWorld(TeeCenter);
            Vector3 playerSpawn = new Vector3(tee.x, teeHeight, tee.y - 1.0f);
            Vector3 ballSpawn = new Vector3(tee.x, teeHeight + 0.03f, tee.y + 0.4f);
            Marker("PlayerSpawn", root.transform, playerSpawn);
            Marker("BallSpawn", root.transform, ballSpawn);
            BuildTestBall(root.transform, ballSpawn, ballMat);

            EnsurePlayer(scene, playerSpawn);
            SetupAtmosphere(scene);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Selection.activeGameObject = root;
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
            Debug.Log(S.Label + " built and saved into " + S.ScenePath);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    static void Progress(string message, float amount) => EditorUtility.DisplayProgressBar("Build " + S.Label, message, amount);

    // ================= LAYOUT MATH =================

    static void PrepareLayout()
    {
        BuildPath();

        // The cup (in world meters) snaps to the nearest terrain grid point
        Vector2 cupTarget = ToWorld(CupTarget);
        cupI = Mathf.RoundToInt((cupTarget.x - GridMinX) / S.Cell);
        cupJ = Mathf.RoundToInt((cupTarget.y - GridMinZ) / S.Cell);
        cup = new Vector2(GridMinX + cupI * S.Cell, GridMinZ + cupJ * S.Cell);

        teeHeight = (Elevation(0f) + TeeRaise) * S.HeightScale;
        greenHeight = (Elevation(1f) + 0.15f) * S.HeightScale;

        if (L.HasShortcut)
        {
            ProjectOnPath(L.ShortcutStart, out _, out float startT);
            shortcutStartElevation = Elevation(startT);
        }

        // Water sits just below the lowest point of the pond's banks, so the shore always meets the ground.
        if (L.HasPond)
        {
            float lowest = float.MaxValue;
            for (int k = 0; k < 32; k++)
            {
                float a = k * Mathf.PI * 2f / 32f;
                Vector2 p = ToWorld(PondCenter + new Vector2(PondRadius.x * Mathf.Cos(a), PondRadius.y * Mathf.Sin(a)) * 1.2f);
                lowest = Mathf.Min(lowest, Evaluate(p, false, out _, out _));
            }
            waterLevel = lowest - 0.25f * S.HeightScale;
        }

        // Same idea for the river: its surface sits just below the lowest point along both banks
        if (L.HasRiver)
        {
            float lowest = float.MaxValue;
            for (int i = 0; i < L.RiverPoints.Length - 1; i++)
            {
                Vector2 a = L.RiverPoints[i], b = L.RiverPoints[i + 1];
                Vector2 side = new Vector2(-(b - a).y, (b - a).x).normalized * (L.RiverHalfWidth + 1.5f);
                for (int k = 0; k <= 8; k++)
                {
                    Vector2 c = Vector2.Lerp(a, b, k / 8f);
                    foreach (Vector2 bank in new[] { c + side, c - side })
                    {
                        if (bank.x < DesignMinX || bank.x > DesignMaxX || bank.y < DesignMinZ || bank.y > DesignMaxZ) continue;
                        lowest = Mathf.Min(lowest, Evaluate(ToWorld(bank), false, out _, out _));
                    }
                }
            }
            riverLevel = lowest - 0.3f * S.HeightScale;
        }
    }

    /// Distance from p to the nearest point on a chain of straight pieces.
    static float DistanceToPolyline(Vector2 p, Vector2[] points)
    {
        float best = float.MaxValue;
        for (int i = 0; i < points.Length - 1; i++)
            best = Mathf.Min(best, DistanceToSegment(p, points[i], points[i + 1], out _));
        return best;
    }

    /// Distance from p to the segment a-b, and how far along it (0..1) the closest point is.
    static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b, out float along)
    {
        Vector2 ab = b - a;
        along = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return Vector2.Distance(p, a + ab * along);
    }

    /// Is this world point inside the bend of the dogleg (the right-hand side of the hole), off the fairway?
    static bool InsideDogleg(Vector2 worldPoint)
    {
        Vector2 q = worldPoint / S.Scale;
        ProjectOnPath(q, out float d, out float t);
        float inner = HalfWidth(t) + 2.5f;
        if (t < 0.12f || t > L.ForestMaxT || d < inner || d > inner + L.ForestDepth) return false;
        Vector2 c = PathPoint(t);
        Vector2 tangent = PathPoint(Mathf.Min(1f, t + 0.01f)) - PathPoint(Mathf.Max(0f, t - 0.01f));
        return tangent.x * (q.y - c.y) - tangent.y * (q.x - c.x) < 0f; // negative = right-hand side
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
        pathLength = total;
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

    /// Mown-grass color: flat (the shader adds the checkerboard) or diagonal stripes baked in.
    static Color SurfaceColor(Color dark, Color light, float checker, float wave) =>
        checker > 0f ? Color.Lerp(dark, light, 0.5f) : Color.Lerp(dark, light, Smooth(-0.25f, 0.25f, wave));

    /// The heart of the course: returns the ground height (world meters) at a world (x, z) point, the ground
    /// color there, and the checkerboard pattern (x = square size in meters, y = strength).
    /// Internally everything is worked out in layout ("design") units, then scaled to the world.
    static float Evaluate(Vector2 worldPoint, bool withPond, out Color color, out Vector2 pattern)
    {
        Vector2 p = worldPoint / S.Scale;
        float heightScale = S.HeightScale;
        Vector2 cupDesign = cup / S.Scale;
        ProjectOnPath(p, out float d, out float t);
        float w = HalfWidth(t);
        float elev = Elevation(t);
        float hills = S.HillScale;

        // ---- Height ----
        // Rolling rough with mounds lining the fairway
        float mounds = Smooth(w + 2f, w + 9f, d) * (0.7f + 1.6f * Noise(p, 0.09f, 200f)) * hills;
        float rough = elev + ((Noise(p, 0.045f, 31.7f) - 0.5f) * 3.2f + (Noise(p, 0.13f, 5.1f) - 0.5f) * 0.7f) * hills + mounds;
        // Gentle undulation in the play corridor
        float corridor = elev + (Noise(p, 0.07f, 90f) - 0.5f) * 0.5f;
        float h = Mathf.Lerp(rough, corridor, Smooth(w + 7f, w + 1.5f, d));

        // Big hills around the edges to frame the hole
        float edge = Mathf.Min(Mathf.Min(p.x - DesignMinX, DesignMaxX - p.x), Mathf.Min(p.y - DesignMinZ, DesignMaxZ - p.y));
        float rim = Smooth(14f, 0f, edge);
        h += rim * rim * 9f * hills * (0.7f + 0.6f * Noise(p, 0.08f, 400f));

        // Raised, flat tee box
        Vector2 tp = p - TeeCenter;
        float teeN = Mathf.Max(Mathf.Abs(tp.x) / TeeHalfSize.x, Mathf.Abs(tp.y) / TeeHalfSize.y);
        h = Mathf.Lerp(h, teeHeight / heightScale, Smooth(1.6f, 1.0f, teeN));

        // Shortcut: a smooth, cleared lane that runs straight from the fairway to the green
        float shortcutMask = 0f;
        if (L.HasShortcut)
        {
            float sd = DistanceToSegment(p, L.ShortcutStart, L.ShortcutEnd, out float along);
            float laneWidth = L.ShortcutHalfWidth;
            float laneHeight = Mathf.Lerp(shortcutStartElevation, greenHeight / heightScale, along) + (Noise(p, 0.07f, 90f) - 0.5f) * 0.4f;
            h = Mathf.Lerp(h, laneHeight, Smooth(laneWidth + 3f, laneWidth + 0.5f, sd));
            shortcutMask = Smooth(laneWidth + 0.3f, laneWidth - 0.3f, sd);
        }

        // Contoured green, perfectly flat right around the cup
        float gN = Ellipse(p, GreenCenter, GreenRadius);
        Vector2 fromCup = p - cupDesign;
        float flatRadius = S.Cell * 1.5f / S.Scale; // covers the grid points around the cup
        float slopeAmount = Smooth(flatRadius, flatRadius + 1f, fromCup.magnitude);
        float greenSurface = greenHeight / heightScale + slopeAmount *
            (L.GreenSlope.x * fromCup.x + L.GreenSlope.y * fromCup.y + L.GreenRoll * Mathf.Sin(p.x * 0.5f) * Mathf.Cos(p.y * 0.4f));
        h = Mathf.Lerp(h, greenSurface, Smooth(1.45f, 1.0f, gN));

        // Hole 3: sink the ground well below the green area, out of sight under the separate green objects
        if (L.SeparateGreen) h -= 2.5f * Smooth(0.97f, 0.9f, gN);

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

        // Meteor crater: a bowl under the rock with a raised rim of thrown-up dirt
        float scorch = 0f, meteorDist = 99f;
        if (L.HasMeteor)
        {
            meteorDist = Vector2.Distance(p, L.MeteorCenter) / L.MeteorRadius; // 1 = edge of the rock
            if (meteorDist < 3f)
            {
                h -= 1.4f * Smooth(1.5f, 0.5f, meteorDist);
                float rimX = (meteorDist - 1.6f) / 0.3f;
                h += 1.0f * Mathf.Exp(-rimX * rimX);
                scorch = Smooth(2.0f, 1.3f, meteorDist);
            }
        }

        // Pond basin
        float shore = 0f;
        if (withPond && L.HasPond)
        {
            float pn = Ellipse(p, PondCenter, PondRadius);
            h = Mathf.Lerp(h, waterLevel / heightScale - 0.7f, Smooth(1.25f, 0.75f, pn));
            shore = Smooth(1.4f, 1.15f, pn);
        }

        // Power plant: a flat concrete pad
        float concrete = 0f;
        if (L.HasPowerPlant)
        {
            Vector2 fromPlant = p - L.PlantCenter;
            float padN = Mathf.Max(Mathf.Abs(fromPlant.x) / L.PlantHalfSize.x, Mathf.Abs(fromPlant.y) / L.PlantHalfSize.y);
            h = Mathf.Lerp(h, L.PlantPadHeight, Smooth(1.2f, 1.0f, padN));
            concrete = Smooth(1.02f, 0.98f, padN);
        }

        // Radioactive river: a channel with sloping banks (the glowing surface is added as its own mesh)
        float riverBank = 0f;
        if (withPond && L.HasRiver)
        {
            float dr = DistanceToPolyline(p, L.RiverPoints);
            float hw = L.RiverHalfWidth;
            h = Mathf.Lerp(h, riverLevel / heightScale - 0.8f, Smooth(hw + 1.5f, hw * 0.6f, dr));
            riverBank = Smooth(hw + 2.4f, hw + 1.2f, dr);
        }

        // ---- Color ----
        color = Color.Lerp(S.RoughA, S.RoughB, Noise(p, 0.2f, 11f));
        color = Color.Lerp(color, S.DeepRough, rim);
        pattern = Vector2.zero;

        float tF = Mathf.Clamp(t, FairwayStart, FairwayEnd);
        float wF = HalfWidth(tF);
        float fairwayDist = (t < FairwayStart || t > FairwayEnd) ? Vector2.Distance(p, PathPoint(tF)) : d;

        if (S.Retro) color = Color.Lerp(color, S.FirstCut, Smooth(wF + 1.4f, wF + 0.9f, fairwayDist));

        float fairwayMask = Smooth(wF + 0.3f, wF - 0.3f, fairwayDist);
        color = Color.Lerp(color, SurfaceColor(S.FairwayDark, S.FairwayLight, S.FairwayChecker, Mathf.Sin((p.x + p.y) * 0.65f)), fairwayMask);
        pattern = new Vector2(S.FairwayChecker, S.FairwayChecker > 0f ? fairwayMask : 0f);

        // The shortcut lane is lighter "first cut" grass, so the gap through the trees reads as a path
        float lane = shortcutMask * (1f - fairwayMask);
        color = Color.Lerp(color, S.FirstCut, lane * 0.9f);
        pattern.y *= 1f - lane;

        float fringeMask = Smooth(1.18f, 1.1f, gN);
        color = Color.Lerp(color, S.Fringe, fringeMask);
        pattern.y *= 1f - fringeMask;

        float greenMask = Smooth(1.04f, 0.98f, gN);
        Color greenColor = SurfaceColor(S.GreenDark, S.GreenLight, S.GreenChecker, Mathf.Sin(p.x * 1.4f));
        color = Color.Lerp(color, greenColor, greenMask);
        pattern = Vector2.Lerp(pattern, new Vector2(S.GreenChecker, S.GreenChecker > 0f ? 1f : 0f), greenMask);

        float teeMask = Smooth(1.1f, 1.0f, teeN);
        color = Color.Lerp(color, SurfaceColor(S.GreenDark, S.GreenLight, S.TeeChecker, Mathf.Sin(p.x * 1.4f)), teeMask);
        pattern = Vector2.Lerp(pattern, new Vector2(S.TeeChecker, S.TeeChecker > 0f ? 1f : 0f), teeMask);

        color = Color.Lerp(color, S.Shore, shore);
        pattern.y *= 1f - shore;
        color = Color.Lerp(color, S.Sand * (0.94f + 0.1f * Noise(p, 1.3f, 70f)), sand);
        pattern.y *= 1f - sand;

        // Scorched dirt around the meteor, darkest right under it
        color = Color.Lerp(color, Color.Lerp(S.Dirt, S.ScorchedDirt, Smooth(1.4f, 0.8f, meteorDist)), scorch);
        pattern.y *= 1f - scorch;

        // Dead, muddy banks along the radioactive river, and gray concrete under the power plant
        color = Color.Lerp(color, S.ScorchedDirt, riverBank);
        pattern.y *= 1f - riverBank;
        color = Color.Lerp(color, new Color(0.49f, 0.49f, 0.49f, 0.3f), concrete); // NES gray
        pattern.y *= 1f - concrete;

        return h * heightScale;
    }

    /// Height of the actual terrain mesh at (x, z), matching its triangles exactly (so props sit on the ground).
    static float GroundHeight(Vector2 p)
    {
        float fx = (p.x - GridMinX) / S.Cell, fz = (p.y - GridMinZ) / S.Cell;
        int i = Mathf.Clamp(Mathf.FloorToInt(fx), 0, gridHeights.GetLength(0) - 2);
        int j = Mathf.Clamp(Mathf.FloorToInt(fz), 0, gridHeights.GetLength(1) - 2);
        float u = fx - i, v = fz - j;
        float h00 = gridHeights[i, j], h10 = gridHeights[i + 1, j], h01 = gridHeights[i, j + 1], h11 = gridHeights[i + 1, j + 1];
        return v > u ? h00 + v * (h01 - h00) + u * (h11 - h01)
                     : h00 + u * (h10 - h00) + v * (h11 - h10);
    }

    static Color ToMeshColor(Color c) => linearColor ? c.linear : c;

    // ================= TERRAIN MESH =================

    static void BuildTerrain(Transform parent, Material mat)
    {
        float cell = S.Cell;
        int nx = Mathf.RoundToInt((GridMaxX - GridMinX) / cell);
        int nz = Mathf.RoundToInt((GridMaxZ - GridMinZ) / cell);
        int vx = nx + 1, vz = nz + 1;

        var verts = new List<Vector3>(vx * vz + 1000);
        var colors = new List<Color>(vx * vz + 1000);
        var uvs = new List<Vector2>(vx * vz + 1000);
        var tris = new List<int>(nx * nz * 6 + 3000);
        gridHeights = new float[vx, vz];
        var gridColors = new Color[vx, vz];
        var gridPatterns = new Vector2[vx, vz];

        for (int j = 0; j < vz; j++)
        {
            if (j % 20 == 0) Progress("Shaping terrain...", 0.05f + 0.45f * j / vz);
            for (int i = 0; i < vx; i++)
            {
                var p = new Vector2(GridMinX + i * cell, GridMinZ + j * cell);
                float h = Evaluate(p, true, out Color c, out Vector2 pattern);
                // The 3x3 grid points around the cup must be exactly level with the cup.
                if (!L.SeparateGreen && Mathf.Abs(i - cupI) <= 1 && Mathf.Abs(j - cupJ) <= 1) h = greenHeight;
                gridHeights[i, j] = h;
                gridColors[i, j] = c;
                gridPatterns[i, j] = pattern;
                verts.Add(new Vector3(p.x, h, p.y));
                if (S.Retro)
                {
                    // Retro: the look comes from the baked pixel texture (alpha 1 = use texture)
                    colors.Add(Color.white);
                    uvs.Add(BakedUV(p));
                }
                else
                {
                    colors.Add(ToMeshColor(c));
                    uvs.Add(pattern);
                }
            }
        }

        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                // Leave a 2x2 cell opening around the cup; it's filled by the cup piece below.
                if (!L.SeparateGreen && (i == cupI - 1 || i == cupI) && (j == cupJ - 1 || j == cupJ)) continue;
                int v00 = j * vx + i, v10 = v00 + 1, v01 = v00 + vx, v11 = v01 + 1;
                tris.Add(v00); tris.Add(v01); tris.Add(v11);
                tris.Add(v00); tris.Add(v11); tris.Add(v10);
            }

        if (!L.SeparateGreen) AddCupGeometry(verts, colors, uvs, tris); // Hole 3's cups live on its green objects

        var mesh = new Mesh { name = S.MeshPrefix + "Terrain" };
        if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetColors(colors);
        mesh.SetUVs(0, uvs);
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

        if (S.Retro)
        {
            Progress("Baking 8-bit pixels...", 0.55f);
            Texture2D pixels = BakeTerrainPixels(mesh.normals, gridColors, gridPatterns, vx);
            Undo.RecordObject(mat, "Bake terrain pixels");
            mat.SetTexture("_BaseMap", pixels);
            EditorUtility.SetDirty(mat);
        }
    }

    /// Texture coordinate of a world (x, z) point on the baked terrain texture.
    static Vector2 BakedUV(Vector2 p) =>
        new Vector2((p.x - GridMinX) / (GridMaxX - GridMinX), (p.y - GridMinZ) / (GridMaxZ - GridMinZ));

    /// Bakes the NES look of the ground into a texture: one texel per 8-bit "pixel", with the checkerboard,
    /// speckles, sun shading, NES palette and ordered dithering all applied here instead of in the shader.
    static Texture2D BakeTerrainPixels(Vector3[] normals, Color[,] gridColors, Vector2[,] gridPatterns, int vx)
    {
        int width = Mathf.RoundToInt((GridMaxX - GridMinX) / BakePixelSize);
        int height = Mathf.RoundToInt((GridMaxZ - GridMinZ) / BakePixelSize);
        var pixels = new Color32[width * height];

        Vector3 toSun = -(RetroSunRotation * Vector3.forward);
        Color sun = RetroSunColor.linear * RetroSunIntensity;
        Color ambientSky = BakeAmbientSky.linear, ambientGround = BakeAmbientGround.linear;
        int lastI = gridColors.GetLength(0) - 2, lastJ = gridColors.GetLength(1) - 2;

        for (int y = 0; y < height; y++)
        {
            if (y % 60 == 0) Progress("Baking 8-bit pixels...", 0.55f + 0.25f * y / height);
            for (int x = 0; x < width; x++)
            {
                float px = GridMinX + (x + 0.5f) * BakePixelSize, pz = GridMinZ + (y + 0.5f) * BakePixelSize;

                // Interpolate the terrain grid exactly like the mesh's triangles do
                float fx = (px - GridMinX) / S.Cell, fz = (pz - GridMinZ) / S.Cell;
                int i = Mathf.Clamp(Mathf.FloorToInt(fx), 0, lastI), j = Mathf.Clamp(Mathf.FloorToInt(fz), 0, lastJ);
                float u = fx - i, v = fz - j;
                int iA, jA; float wA, wB, w11;
                if (v > u) { iA = i; jA = j + 1; wA = 1f - v; wB = v - u; w11 = u; }      // corners 00, 01, 11
                else { iA = i + 1; jA = j; wA = 1f - u; wB = u - v; w11 = v; }           // corners 00, 10, 11

                Color c = gridColors[i, j] * wA + gridColors[iA, jA] * wB + gridColors[i + 1, j + 1] * w11;
                Vector2 pattern = gridPatterns[i, j] * wA + gridPatterns[iA, jA] * wB + gridPatterns[i + 1, j + 1] * w11;
                Vector3 n = (normals[j * vx + i] * wA + normals[jA * vx + iA] * wB + normals[(j + 1) * vx + i + 1] * w11).normalized;

                Color albedo = c;
                if (pattern.y > 0.01f)
                {
                    float size = Mathf.Max(pattern.x, BakePixelSize);
                    int sx = Mathf.FloorToInt(px / size), sz = Mathf.FloorToInt(pz / size);
                    int checker = ((sx + sz) % 2 + 2) % 2;
                    albedo *= 1f + (checker * 2 - 1) * BakeCheckerContrast * Mathf.Clamp01(pattern.y);
                }

                // Occasional dark pixels (grass tufts / sand grains)
                float speckle = Mathf.Clamp01(c.a);
                if (Hash(x, y) < BakeSpeckleDensity * speckle) albedo *= 0.72f;

                // Sun + sky lighting, done in linear space like a real shader would
                Color linear = new Color(Mathf.Clamp01(albedo.r), Mathf.Clamp01(albedo.g), Mathf.Clamp01(albedo.b)).linear;
                float diffuse = Mathf.Max(0f, Vector3.Dot(n, toSun)) * BakeSunStrength;
                Color ambient = Color.Lerp(ambientGround, ambientSky, n.y * 0.5f + 0.5f);
                Color lit = linear * (sun * diffuse + ambient);
                Color srgb = new Color(Mathf.Clamp01(lit.r), Mathf.Clamp01(lit.g), Mathf.Clamp01(lit.b)).gamma;

                pixels[y * width + x] = QuantizeToPalette(srgb, x, y, BakeDither);
            }
        }

        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        texture.SetPixels32(pixels);
        texture.Apply();
        byte[] png = texture.EncodeToPNG();
        Object.DestroyImmediate(texture);

        EnsureFolder(TextureFolder);
        string path = TextureFolder + "/" + S.MeshPrefix + "TerrainPixels.png";
        System.IO.File.WriteAllBytes(path, png);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.None;
        importer.filterMode = FilterMode.Point;        // crisp 8-bit pixels up close
        importer.mipmapEnabled = true;                 // smaller copies far away, so distant ground doesn't sparkle
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = 4096; // the long course is ~2460 pixels long
        importer.anisoLevel = 2;
        importer.textureCompression = TextureImporterCompression.Uncompressed; // keep exact palette colors
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// Snaps an sRGB color to the NES palette. With dither on, it mixes the two closest colors in a 4x4
    /// Bayer pattern; with it off, every pixel is simply the closest color (solid, flat areas).
    static Color32 QuantizeToPalette(Color c, int x, int y, bool dither)
    {
        float best = float.MaxValue, second = float.MaxValue;
        Color bestColor = NesPalette[0], secondColor = NesPalette[0];
        foreach (Color p in NesPalette)
        {
            float dr = c.r - p.r, dg = c.g - p.g, db = c.b - p.b;
            float dist = dr * dr * 0.35f + dg * dg * 0.45f + db * db * 0.2f;
            if (dist < best) { second = best; secondColor = bestColor; best = dist; bestColor = p; }
            else if (dist < second) { second = dist; secondColor = p; }
        }
        if (!dither) return bestColor;
        float mix = Mathf.Sqrt(best) / (Mathf.Sqrt(best) + Mathf.Sqrt(second) + 1e-5f);
        float threshold = (Bayer4x4[(y & 3) * 4 + (x & 3)] + 0.5f) / 16f;
        return threshold < mix ? (Color32)secondColor : (Color32)bestColor;
    }

    /// Repeatable pseudo-random number in [0, 1) for a pixel.
    static float Hash(int x, int y)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }

    /// Fills the opening around the cup with a flat square that has a round hole, plus the cup itself.
    static void AddCupGeometry(List<Vector3> verts, List<Color> colors, List<Vector2> uvs, List<int> tris)
    {
        const int segments = 32;
        float top = greenHeight, liner = top - 0.025f, bottom = top - CupDepth;
        Evaluate(cup, true, out Color green, out Vector2 greenPattern);
        if (S.Retro) green = Color.white; // retro: the green around the cup comes from the baked texture

        void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 facing, Color col, Vector2 pattern)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), facing) < 0f) (b, c) = (c, b);
            col = ToMeshColor(col);
            int s = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            colors.Add(col); colors.Add(col); colors.Add(col);
            if (S.Retro)
            {
                uvs.Add(BakedUV(new Vector2(a.x, a.z))); uvs.Add(BakedUV(new Vector2(b.x, b.z))); uvs.Add(BakedUV(new Vector2(c.x, c.z)));
            }
            else
            {
                uvs.Add(pattern); uvs.Add(pattern); uvs.Add(pattern);
            }
            tris.Add(s); tris.Add(s + 1); tris.Add(s + 2);
        }
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing, Color col, Vector2 pattern)
        {
            Tri(a, b, c, facing, col, pattern);
            Tri(a, c, d, facing, col, pattern);
        }
        Vector3 At(Vector2 dir, float radius, float y) => new Vector3(cup.x + dir.x * radius, y, cup.y + dir.y * radius);

        for (int k = 0; k < segments; k++)
        {
            float a0 = k * Mathf.PI * 2f / segments, a1 = (k + 1) * Mathf.PI * 2f / segments;
            var d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
            var d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
            // Point on the square's edge in this direction
            float s0 = S.Cell / Mathf.Max(Mathf.Abs(d0.x), Mathf.Abs(d0.y));
            float s1 = S.Cell / Mathf.Max(Mathf.Abs(d1.x), Mathf.Abs(d1.y));

            Quad(At(d0, s0, top), At(d1, s1, top), At(d1, CupRadius, top), At(d0, CupRadius, top), Vector3.up, green, greenPattern);

            Vector3 inward = -new Vector3(d0.x + d1.x, 0f, d0.y + d1.y);
            Quad(At(d0, CupRadius, top), At(d1, CupRadius, top), At(d1, CupRadius, liner), At(d0, CupRadius, liner), inward, S.CupLiner, Vector2.zero);
            Quad(At(d0, CupRadius, liner), At(d1, CupRadius, liner), At(d1, CupRadius, bottom), At(d0, CupRadius, bottom), inward, S.CupInside, Vector2.zero);
            Tri(new Vector3(cup.x, bottom, cup.y), At(d0, CupRadius, bottom), At(d1, CupRadius, bottom), Vector3.up, S.CupInside * 0.7f, Vector2.zero);
        }
    }

    // ================= PROPS =================

    /// Pond surface: an oval with deeper color in the middle and lighter near the shore.
    static void BuildWater(Transform parent, Material mat)
    {
        const int segments = 48;
        float[] ringScale = { 0.6f, 1.2f };
        var verts = new List<Vector3> { Vector3.zero };
        var colors = new List<Color> { ToMeshColor(S.WaterDeep) };
        foreach (float r in ringScale)
            for (int k = 0; k < segments; k++)
            {
                float a = k * Mathf.PI * 2f / segments;
                verts.Add(new Vector3(PondRadius.x * r * Mathf.Cos(a), 0f, PondRadius.y * r * Mathf.Sin(a)) * S.Scale);
                colors.Add(ToMeshColor(r < 1f ? Color.Lerp(S.WaterDeep, S.WaterShallow, 0.3f) : S.WaterShallow));
            }

        var tris = new List<int>();
        for (int k = 0; k < segments; k++)
        {
            int k1 = (k + 1) % segments;
            int i0 = 1 + k, i1 = 1 + k1, o0 = 1 + segments + k, o1 = 1 + segments + k1;
            // Clockwise when seen from above = facing up
            tris.Add(0); tris.Add(i1); tris.Add(i0);
            tris.Add(i0); tris.Add(i1); tris.Add(o1);
            tris.Add(i0); tris.Add(o1); tris.Add(o0);
        }

        var mesh = new Mesh { name = S.MeshPrefix + "Water" };
        mesh.SetVertices(verts);
        mesh.SetColors(colors);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        SaveMesh(mesh);

        var go = new GameObject("Pond");
        go.transform.SetParent(parent, false);
        Vector2 pondWorld = ToWorld(PondCenter);
        go.transform.localPosition = new Vector3(pondWorld.x, waterLevel, pondWorld.y);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        // Trigger so gameplay code can detect a ball landing in the water
        var trigger = go.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(PondRadius.x * 2f * S.Scale, 1f, PondRadius.y * 2f * S.Scale);
        trigger.center = new Vector3(0f, -0.4f, 0f);
    }

    /// The comically huge meteorite sitting in its crater, with glowing hot spots and chunks of debris around it.
    static void BuildMeteor(Transform parent, Material mat)
    {
        var rng = new System.Random(RandomSeed + 5);

        // Main rock: a big lumpy ball with a couple of extra lumps fused on, built at radius 1 then scaled up
        var mb = new MeshBuilder();
        mb.Blob(Vector3.zero, 1f, 8, 12, S.MeteorRock, 101, 0.28f);
        mb.Blob(new Vector3(0.55f, 0.3f, -0.15f), 0.6f, 6, 10, S.MeteorRock * 0.92f, 102, 0.3f);
        mb.Blob(new Vector3(-0.45f, 0.15f, 0.35f), 0.55f, 6, 10, S.MeteorRock * 0.96f, 103, 0.3f);
        // Glowing hot spots, mostly on the side facing the tee (-Z)
        for (int k = 0; k < 10; k++)
        {
            float yaw = Mathf.Lerp(-2.6f, -0.5f, (float)rng.NextDouble());
            float pitch = Mathf.Lerp(-0.15f, 0.9f, (float)rng.NextDouble());
            var dir = new Vector3(Mathf.Cos(pitch) * Mathf.Cos(yaw), Mathf.Sin(pitch), Mathf.Cos(pitch) * Mathf.Sin(yaw));
            mb.Blob(dir * 0.92f, Mathf.Lerp(0.08f, 0.16f, (float)rng.NextDouble()), 4, 6,
                    k % 3 == 0 ? S.MeteorHotBright : S.MeteorHot, 110 + k, 0.2f);
        }
        Mesh rockMesh = mb.ToMesh(S.MeshPrefix + "Meteor");

        Vector2 center = ToWorld(L.MeteorCenter);
        float radius = L.MeteorRadius * S.Scale;
        var meteor = new GameObject("Meteor");
        meteor.transform.SetParent(parent, false);
        meteor.transform.localPosition = new Vector3(center.x, GroundHeight(center) - radius * 0.35f, center.y);
        meteor.transform.localRotation = Quaternion.Euler(0f, 20f, 0f);
        meteor.transform.localScale = new Vector3(radius, radius * L.MeteorHeight, radius); // tall and towering
        meteor.AddComponent<MeshFilter>().sharedMesh = rockMesh;
        meteor.AddComponent<MeshRenderer>().sharedMaterial = mat;
        meteor.AddComponent<MeshCollider>().sharedMesh = rockMesh; // the ball bounces off it
        GameObjectUtility.SetStaticEditorFlags(meteor, StaticEditorFlags.BatchingStatic);

        BuildMeteorFire(parent, meteor.transform, radius, rng);

        // Debris: smaller rocks scattered around the crater
        var debrisMeshes = new List<Mesh>();
        for (int v = 0; v < 3; v++)
        {
            var d = new MeshBuilder();
            d.Blob(Vector3.zero, 1f, 4, 6, S.MeteorRock * (0.85f + 0.08f * v), 120 + v, 0.35f);
            debrisMeshes.Add(d.ToMesh(S.MeshPrefix + "Debris" + v));
        }
        var debrisParent = new GameObject("MeteorDebris").transform;
        debrisParent.SetParent(parent, false);
        for (int k = 0; k < 18; k++)
        {
            float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
            float distance = Mathf.Lerp(1.15f, 1.9f, (float)rng.NextDouble()) * radius;
            Vector2 p = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
            float size = Mathf.Lerp(0.5f, 1.6f, (float)rng.NextDouble());

            var rock = new GameObject("Debris");
            rock.transform.SetParent(debrisParent, false);
            rock.transform.localPosition = new Vector3(p.x, GroundHeight(p) - size * 0.3f, p.y);
            rock.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            rock.transform.localScale = new Vector3(size, size * 0.8f, size);
            rock.AddComponent<MeshFilter>().sharedMesh = debrisMeshes[rng.Next(debrisMeshes.Count)];
            rock.AddComponent<MeshRenderer>().sharedMaterial = mat;
            rock.AddComponent<SphereCollider>().radius = 0.8f;
            GameObjectUtility.SetStaticEditorFlags(rock, StaticEditorFlags.BatchingStatic);
        }
    }

    /// Big flickering 8-bit flames all over the top of the meteor, with puffs of smoke rising from it.
    static void BuildMeteorFire(Transform parent, Transform meteor, float radius, System.Random rng)
    {
        var fireParent = new GameObject("MeteorFire").transform;
        fireParent.SetParent(parent, false);

        const int flames = 14;
        for (int k = 0; k < flames; k++)
        {
            // Spread over the top of the rock: straight up for the first one, then rings lower down
            float pitch = k == 0 ? Mathf.PI * 0.5f : Mathf.Lerp(0.35f, 1.25f, (float)rng.NextDouble());
            float yaw = (float)rng.NextDouble() * Mathf.PI * 2f;
            var dir = new Vector3(Mathf.Cos(pitch) * Mathf.Cos(yaw), Mathf.Sin(pitch), Mathf.Cos(pitch) * Mathf.Sin(yaw));
            float size = (k == 0 ? 1.0f : Mathf.Lerp(0.45f, 0.8f, (float)rng.NextDouble())) * radius * 0.9f;
            AddFlame(fireParent, meteor.TransformPoint(dir * 0.85f), size, rng);
        }

        // Smoke puffs rising from the top
        Vector3 top = meteor.TransformPoint(Vector3.up * 0.95f);
        for (int k = 0; k < 7; k++)
        {
            Vector3 start = top + new Vector3(((float)rng.NextDouble() - 0.5f) * radius, radius * 0.6f, ((float)rng.NextDouble() - 0.5f) * radius);
            AddSmoke(fireParent, start, radius * Mathf.Lerp(0.25f, 0.4f, (float)rng.NextDouble()), radius * 2.5f, rng);
        }
    }

    // Shared fire pieces (made once per build, used by the meteor and the burning trees)
    static Material fireMaterial;
    static List<Mesh> flameMeshes;
    static Mesh smokeMesh;
    static Mesh steamMesh;

    static void EnsureFireAssets()
    {
        if (fireMaterial != null && flameMeshes != null && smokeMesh != null) return;

        // Unlit: flames glow the same color whichever way they face
        fireMaterial = GetShaderMaterial(S.MaterialPrefix + "Fire", PixelShader, m =>
        {
            m.SetFloat("_PixelSize", 0.25f);
            m.SetColor("_SkyColor", Color.white);
            m.SetColor("_GroundColor", Color.white);
            m.SetFloat("_LightStrength", 0f);
            m.SetFloat("_Dither", 0f);
            m.SetFloat("_Speckle", 0f);
            m.SetFloat("_FogStrength", 0f);
        });

        var red = new Color(0.97f, 0.22f, 0f, 0f);       // NES red-orange
        var orange = new Color(0.99f, 0.63f, 0.27f, 0f); // NES orange
        var yellow = new Color(0.97f, 0.72f, 0f, 0f);    // NES yellow

        // One flame = a fat red tongue with a taller orange tongue and a yellow tongue poking out of its sides
        flameMeshes = new List<Mesh>();
        for (int v = 0; v < 2; v++)
        {
            var mb = new MeshBuilder();
            mb.Cone(0.45f, 0f, 0.8f, 6, red);
            mb.Cone(0.26f, 0f, 1.0f, 6, v == 0 ? orange : yellow, new Vector3(0.22f, 0f, 0.12f));
            mb.Cone(0.2f, 0f, 0.75f, 5, v == 0 ? yellow : orange, new Vector3(-0.2f, 0f, -0.15f));
            flameMeshes.Add(mb.ToMesh(S.MeshPrefix + "Flame" + v));
        }

        var smokeBuilder = new MeshBuilder();
        smokeBuilder.Blob(Vector3.zero, 1f, 4, 7, new Color(0.49f, 0.49f, 0.49f, 0f), 130, 0.25f); // NES gray
        smokeMesh = smokeBuilder.ToMesh(S.MeshPrefix + "Smoke");
    }

    /// One flickering flame, standing at a world position. size = its width in meters.
    static void AddFlame(Transform parent, Vector3 position, float size, System.Random rng)
    {
        EnsureFireAssets();
        var flame = new GameObject("Flame");
        flame.transform.SetParent(parent, false);
        flame.transform.position = position;
        flame.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
        flame.transform.localScale = new Vector3(size, size * 1.4f, size);
        flame.AddComponent<MeshFilter>().sharedMesh = flameMeshes[rng.Next(flameMeshes.Count)];
        flame.AddComponent<MeshRenderer>().sharedMaterial = fireMaterial;
        var flicker = flame.AddComponent<FlameFlicker>();
        flicker.framesPerSecond = Mathf.Lerp(8f, 12f, (float)rng.NextDouble());
        flicker.amount = Mathf.Lerp(0.2f, 0.35f, (float)rng.NextDouble());
    }

    /// One looping smoke puff starting at a world position. size = its starting width in meters.
    /// steam = white instead of gray (cooling towers).
    static void AddSmoke(Transform parent, Vector3 position, float size, float riseHeight, System.Random rng, bool steam = false)
    {
        EnsureFireAssets();
        if (steam && steamMesh == null)
        {
            var steamBuilder = new MeshBuilder();
            steamBuilder.Blob(Vector3.zero, 1f, 4, 7, new Color(0.99f, 0.99f, 0.99f, 0f), 131, 0.25f); // NES white
            steamMesh = steamBuilder.ToMesh(S.MeshPrefix + "Steam");
        }
        var puff = new GameObject(steam ? "Steam" : "Smoke");
        puff.transform.SetParent(parent, false);
        puff.transform.position = position;
        puff.transform.localScale = Vector3.one * size;
        puff.AddComponent<MeshFilter>().sharedMesh = steam ? steamMesh : smokeMesh;
        puff.AddComponent<MeshRenderer>().sharedMaterial = fireMaterial;
        var smoke = puff.AddComponent<SmokePuff>();
        smoke.riseHeight = riseHeight;
        smoke.riseSpeed = Mathf.Lerp(2.5f, 4f, (float)rng.NextDouble());
    }

    // ================= HOLE 4: NUCLEAR =================

    static readonly Color Glow = new Color(0.72f, 0.97f, 0.09f, 0f);          // NES radioactive yellow-green
    static readonly Color GlowDeep = new Color(0.35f, 0.85f, 0.33f, 0f);      // NES green
    static readonly Color HazardYellow = new Color(0.97f, 0.72f, 0f, 0f);     // NES yellow
    static readonly Color NesBlack = new Color(0f, 0f, 0f, 0f);
    static readonly Color ConcreteLight = new Color(0.74f, 0.74f, 0.74f, 0f); // NES light gray
    static readonly Color ConcreteDark = new Color(0.49f, 0.49f, 0.49f, 0f);  // NES gray
    static readonly Color WarningRed = new Color(0.97f, 0.22f, 0f, 0f);       // NES red-orange
    static readonly Color NesWhite = new Color(0.99f, 0.99f, 0.99f, 0f);

    /// The glowing radioactive river: its surface, bubbling blobs, hazard triggers, and warning signs.
    static void BuildRiver(Transform parent, Material mat)
    {
        EnsureFireAssets(); // the glow uses the unlit fire material, so it looks lit from within
        var river = new GameObject("RadioactiveRiver").transform;
        river.SetParent(parent, false);

        // Course of the river in world meters, chopped into short pieces
        var course = new List<Vector2>();
        Vector2[] pts = L.RiverPoints;
        for (int i = 0; i < pts.Length - 1; i++)
            for (int k = 0; k < 6; k++)
                course.Add(ToWorld(Vector2.Lerp(pts[i], pts[i + 1], k / 6f)));
        course.Add(ToWorld(pts[pts.Length - 1]));

        // Surface: a strip that reaches under both banks; bright in the middle, greener at the edges
        var verts = new List<Vector3>();
        var colors = new List<Color>();
        var tris = new List<int>();
        float half = (L.RiverHalfWidth + 1.4f) * S.Scale;
        for (int i = 0; i < course.Count; i++)
        {
            Vector2 dir = (course[Mathf.Min(i + 1, course.Count - 1)] - course[Mathf.Max(i - 1, 0)]).normalized;
            var side = new Vector2(-dir.y, dir.x);
            for (int j = 0; j < 5; j++)
            {
                float f = j / 4f * 2f - 1f; // -1 (one bank) .. 1 (other bank)
                Vector2 w = course[i] + side * (f * half);
                verts.Add(new Vector3(w.x, riverLevel, w.y));
                colors.Add(ToMeshColor(Color.Lerp(Glow, GlowDeep, Mathf.Abs(f))));
            }
        }
        void FacingUp(int a, int b, int c)
        {
            bool up = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]).y > 0f;
            tris.Add(a); tris.Add(up ? b : c); tris.Add(up ? c : b);
        }
        for (int i = 0; i < course.Count - 1; i++)
            for (int j = 0; j < 4; j++)
            {
                int a = i * 5 + j, b = a + 1, c = a + 5, d = a + 6;
                FacingUp(a, b, d);
                FacingUp(a, d, c);
            }
        var mesh = new Mesh { name = S.MeshPrefix + "River" };
        mesh.SetVertices(verts);
        mesh.SetColors(colors);
        mesh.SetUVs(0, new List<Vector2>(new Vector2[verts.Count]));
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        SaveMesh(mesh);

        var surface = new GameObject("GlowingWater");
        surface.transform.SetParent(river, false);
        surface.AddComponent<MeshFilter>().sharedMesh = mesh;
        surface.AddComponent<MeshRenderer>().sharedMaterial = fireMaterial;

        // Hazard triggers so gameplay code can tell when the ball lands in the river
        for (int i = 0; i < pts.Length - 1; i++)
        {
            Vector2 a = ToWorld(pts[i]), b = ToWorld(pts[i + 1]);
            Vector2 along = b - a;
            var hazard = new GameObject("RiverHazard");
            hazard.transform.SetParent(river, false);
            hazard.transform.position = new Vector3((a.x + b.x) * 0.5f, riverLevel - 0.4f, (a.y + b.y) * 0.5f);
            hazard.transform.rotation = Quaternion.LookRotation(new Vector3(along.x, 0f, along.y));
            var box = hazard.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(L.RiverHalfWidth * 2f * S.Scale, 1.2f, along.magnitude);
        }

        // Bubbling blobs on the surface
        var rng = new System.Random(RandomSeed + 40);
        var bubbleBuilder = new MeshBuilder();
        bubbleBuilder.Blob(Vector3.zero, 1f, 4, 7, Glow, 160, 0.3f);
        Mesh bubbleMesh = bubbleBuilder.ToMesh(S.MeshPrefix + "Bubble");
        for (int k = 0; k < 16; k++)
        {
            int i = rng.Next(course.Count - 1);
            Vector2 dir = (course[i + 1] - course[i]).normalized;
            Vector2 w = course[i] + new Vector2(-dir.y, dir.x) * (((float)rng.NextDouble() - 0.5f) * L.RiverHalfWidth * S.Scale);
            float size = Mathf.Lerp(0.5f, 1.3f, (float)rng.NextDouble());
            var bubble = new GameObject("Bubble");
            bubble.transform.SetParent(river, false);
            bubble.transform.position = new Vector3(w.x, riverLevel, w.y);
            bubble.transform.localScale = new Vector3(size, size * 0.6f, size);
            bubble.AddComponent<MeshFilter>().sharedMesh = bubbleMesh;
            bubble.AddComponent<MeshRenderer>().sharedMaterial = fireMaterial;
            var pulse = bubble.AddComponent<FlameFlicker>();
            pulse.framesPerSecond = 5f;
            pulse.amount = 0.45f;
        }

        // Radiation warning signs on the tee side of the river, facing the tee
        Mesh signMesh = RadiationSignMesh();
        Vector2 tee = ToWorld(TeeCenter);
        foreach (Vector2 spot in new[] { new Vector2(-6f, 15.5f), new Vector2(7f, 15f) })
        {
            Vector2 w = ToWorld(spot);
            Vector2 toTee = (tee - w).normalized;
            var sign = new GameObject("RadiationSign");
            sign.transform.SetParent(river, false);
            sign.transform.position = new Vector3(w.x, GroundHeight(w), w.y);
            sign.transform.rotation = Quaternion.LookRotation(new Vector3(-toTee.x, 0f, -toTee.y)); // front (-Z) faces the tee
            sign.transform.localScale = Vector3.one * 1.5f;
            sign.AddComponent<MeshFilter>().sharedMesh = signMesh;
            sign.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }
    }

    /// A yellow sign on two posts with a black radiation trefoil on the front (which faces -Z).
    static Mesh RadiationSignMesh()
    {
        var mb = new MeshBuilder();
        mb.Box(new Vector3(-0.6f, 0f, 0.05f), new Vector3(0.12f, 2.0f, 0.12f), ConcreteDark);
        mb.Box(new Vector3(0.6f, 0f, 0.05f), new Vector3(0.12f, 2.0f, 0.12f), ConcreteDark);
        mb.Box(new Vector3(0f, 1.6f, 0.05f), new Vector3(1.8f, 1.8f, 0.08f), HazardYellow);

        // Trefoil: a center dot and three blades, just in front of the board
        var center = new Vector3(0f, 2.5f, -0.005f);
        Vector3 At(float angle, float radius) => center + new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
        const int dotSides = 12;
        for (int k = 0; k < dotSides; k++)
            mb.Tri(center, At(k * Mathf.PI * 2f / dotSides, 0.13f), At((k + 1) * Mathf.PI * 2f / dotSides, 0.13f), Vector3.back, NesBlack);
        foreach (float bladeDeg in new[] { 90f, 210f, 330f })
        {
            const int steps = 6;
            for (int k = 0; k < steps; k++)
            {
                float a0 = (bladeDeg - 30f + 60f * k / steps) * Mathf.Deg2Rad;
                float a1 = (bladeDeg - 30f + 60f * (k + 1) / steps) * Mathf.Deg2Rad;
                mb.Quad(At(a0, 0.2f), At(a1, 0.2f), At(a1, 0.62f), At(a0, 0.62f), Vector3.back, NesBlack);
            }
        }
        return mb.ToMesh(S.MeshPrefix + "RadiationSign");
    }

    /// The nuclear power plant on its concrete pad: two cooling towers with steam, a domed reactor,
    /// a turbine hall, a striped smokestack, and a big pipe pouring glowing goo into the river.
    static void BuildPowerPlant(Transform parent, Material mat)
    {
        EnsureFireAssets();
        var plant = new GameObject("PowerPlant").transform;
        plant.SetParent(parent, false);
        var rng = new System.Random(RandomSeed + 41);
        Vector2 center = ToWorld(L.PlantCenter);
        float pad = GroundHeight(center);

        GameObject Place(string name, Mesh mesh, Vector2 offset, Vector3 scale, bool meshCollider)
        {
            var go = new GameObject(name);
            go.transform.SetParent(plant, false);
            Vector2 w = center + offset;
            go.transform.position = new Vector3(w.x, pad, w.y);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            if (meshCollider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            return go;
        }

        // Cooling towers (the classic curved shape), puffing steam
        Mesh towerMesh = CoolingTowerMesh();
        const float towerRadius = 10f, towerHeight = 36f;
        foreach (Vector2 offset in new[] { new Vector2(12f, 12f), new Vector2(12f, -12f) })
        {
            Place("CoolingTower", towerMesh, offset, new Vector3(towerRadius, towerHeight, towerRadius), true);
            Vector2 top = center + offset;
            for (int k = 0; k < 5; k++)
            {
                var start = new Vector3(top.x + ((float)rng.NextDouble() - 0.5f) * 8f, pad + towerHeight + 2f, top.y + ((float)rng.NextDouble() - 0.5f) * 8f);
                AddSmoke(plant, start, Mathf.Lerp(6f, 9f, (float)rng.NextDouble()), 40f, rng, true);
            }
        }

        // Reactor: a concrete drum with a dome and a red warning band
        var reactor = new MeshBuilder();
        reactor.Cylinder(7f, 0f, 10f, 16, ConcreteLight);
        reactor.Cylinder(7.05f, 7.5f, 8.3f, 16, WarningRed);
        reactor.Blob(new Vector3(0f, 10f, 0f), 7f, 6, 16, ConcreteLight * 1.05f, 200, 0f);
        Place("Reactor", reactor.ToMesh(S.MeshPrefix + "Reactor"), new Vector2(-8f, 10f), Vector3.one, true);

        // Turbine hall: a long boxy building with a lighter roof
        var hall = new MeshBuilder();
        hall.Box(Vector3.zero, new Vector3(18f, 10f, 14f), ConcreteDark);
        hall.Box(new Vector3(0f, 10f, 0f), new Vector3(18.4f, 0.6f, 14.4f), ConcreteLight);
        var hallGo = Place("TurbineHall", hall.ToMesh(S.MeshPrefix + "TurbineHall"), new Vector2(-8f, -10f), Vector3.one, false);
        var hallCollider = hallGo.AddComponent<BoxCollider>();
        hallCollider.center = new Vector3(0f, 5f, 0f);
        hallCollider.size = new Vector3(18f, 10f, 14f);

        // Smokestack: red and white stripes, with dark smoke
        var stack = new MeshBuilder();
        const float stackHeight = 50f;
        for (int b = 0; b < 10; b++)
            stack.Cylinder(1.6f, stackHeight * b / 10f, stackHeight * (b + 1) / 10f, 10, b % 2 == 0 ? NesWhite : WarningRed);
        var stackGo = Place("Smokestack", stack.ToMesh(S.MeshPrefix + "Smokestack"), new Vector2(-16f, 20f), Vector3.one, false);
        var stackCollider = stackGo.AddComponent<CapsuleCollider>();
        stackCollider.center = new Vector3(0f, stackHeight * 0.5f, 0f);
        stackCollider.height = stackHeight;
        stackCollider.radius = 1.6f;
        Vector2 stackTop = center + new Vector2(-16f, 20f);
        for (int k = 0; k < 4; k++)
            AddSmoke(plant, new Vector3(stackTop.x, pad + stackHeight + 1f, stackTop.y), Mathf.Lerp(2.5f, 4f, (float)rng.NextDouble()), 30f, rng);

        // Outflow pipe from the front of the pad, out over the river, pouring glowing goo
        float padFrontZ = ToWorld(L.PlantCenter - new Vector2(0f, L.PlantHalfSize.y)).y;
        float pipeX = center.x - 10f;
        float riverZ = float.MaxValue; // where the river is under the pipe
        for (float z = padFrontZ; z > padFrontZ - 40f; z -= 0.5f)
        {
            if (DistanceToPolyline(new Vector2(pipeX, z) / S.Scale, L.RiverPoints) < L.RiverHalfWidth * 0.3f) { riverZ = z; break; }
        }
        if (riverZ == float.MaxValue) riverZ = padFrontZ - 12f;
        float pipeLength = padFrontZ - riverZ + 2f;
        float pipeY = pad + 1.5f;

        var pipe = new MeshBuilder();
        pipe.Cylinder(1.2f, -2f, pipeLength, 12, ConcreteDark);
        var pipeGo = new GameObject("OutflowPipe");
        pipeGo.transform.SetParent(plant, false);
        pipeGo.transform.position = new Vector3(pipeX, pipeY, padFrontZ);
        pipeGo.transform.rotation = Quaternion.Euler(-90f, 0f, 0f); // lay it down, pointing toward the river (-Z)
        pipeGo.AddComponent<MeshFilter>().sharedMesh = pipe.ToMesh(S.MeshPrefix + "Pipe");
        pipeGo.AddComponent<MeshRenderer>().sharedMaterial = mat;
        var pipeCollider = pipeGo.AddComponent<CapsuleCollider>();
        pipeCollider.radius = 1.2f;
        pipeCollider.height = pipeLength + 2f;
        pipeCollider.center = new Vector3(0f, (pipeLength - 2f) * 0.5f, 0f);

        var goo = new MeshBuilder();
        goo.Box(Vector3.zero, new Vector3(1.6f, Mathf.Max(0.5f, pipeY - riverLevel), 0.8f), Glow);
        var gooGo = new GameObject("GooPour");
        gooGo.transform.SetParent(plant, false);
        gooGo.transform.position = new Vector3(pipeX, riverLevel, padFrontZ - pipeLength + 2f);
        gooGo.AddComponent<MeshFilter>().sharedMesh = goo.ToMesh(S.MeshPrefix + "GooPour");
        gooGo.AddComponent<MeshRenderer>().sharedMaterial = fireMaterial;
    }

    /// A cooling tower with the classic pinched-waist curve, 1 unit wide and 1 unit tall (scaled up when placed).
    static Mesh CoolingTowerMesh()
    {
        var mb = new MeshBuilder();
        const int rings = 12, sides = 20;
        float RadiusAt(float y)
        {
            float k = (y - 0.75f) / 0.696f;
            return 0.68f * Mathf.Sqrt(1f + k * k); // 1.0 at the bottom, 0.68 at the waist, 0.72 at the top
        }
        for (int r = 0; r < rings; r++)
        {
            float y0 = r / (float)rings, y1 = (r + 1) / (float)rings;
            Color col = (y0 >= 0.83f && y0 < 0.92f) ? ConcreteDark : ConcreteLight; // darker band near the top
            for (int s = 0; s < sides; s++)
            {
                float a0 = s * Mathf.PI * 2f / sides, a1 = (s + 1) * Mathf.PI * 2f / sides;
                var p00 = new Vector3(Mathf.Cos(a0) * RadiusAt(y0), y0, Mathf.Sin(a0) * RadiusAt(y0));
                var p10 = new Vector3(Mathf.Cos(a1) * RadiusAt(y0), y0, Mathf.Sin(a1) * RadiusAt(y0));
                var p11 = new Vector3(Mathf.Cos(a1) * RadiusAt(y1), y1, Mathf.Sin(a1) * RadiusAt(y1));
                var p01 = new Vector3(Mathf.Cos(a0) * RadiusAt(y1), y1, Mathf.Sin(a0) * RadiusAt(y1));
                var outward = new Vector3(Mathf.Cos((a0 + a1) * 0.5f), 0f, Mathf.Sin((a0 + a1) * 0.5f));
                mb.Quad(p00, p10, p11, p01, outward, col);
                mb.Quad(p00, p10, p11, p01, -outward, col * 0.55f); // inside wall, seen from above
            }
        }
        return mb.ToMesh(S.MeshPrefix + "CoolingTower");
    }

    /// Two rows of radioactive barrels down both sides of the last stretch of fairway.
    /// A few in the outer rows have tipped over and are leaking glowing goo.
    static void BuildBarrels(Transform parent, Material mat)
    {
        EnsureFireAssets();
        var barrelParent = new GameObject("RadioactiveBarrels").transform;
        barrelParent.SetParent(parent, false);
        var rng = new System.Random(RandomSeed + 44);

        // A yellow drum with two black bands, 1 unit wide and tall (scaled when placed)
        var mb = new MeshBuilder();
        mb.Cylinder(1f, 0f, 0.15f, 10, HazardYellow);
        mb.Cylinder(1.02f, 0.15f, 0.22f, 10, NesBlack);
        mb.Cylinder(1f, 0.22f, 0.78f, 10, HazardYellow);
        mb.Cylinder(1.02f, 0.78f, 0.85f, 10, NesBlack);
        mb.Cylinder(1f, 0.85f, 1f, 10, HazardYellow);
        Mesh barrelMesh = mb.ToMesh(S.MeshPrefix + "Barrel");

        var puddleBuilder = new MeshBuilder();
        puddleBuilder.Blob(Vector3.zero, 1f, 3, 10, Glow, 170, 0.25f);
        Mesh puddleMesh = puddleBuilder.ToMesh(S.MeshPrefix + "GooPuddle");

        const float barrelRadius = 0.45f, barrelHeight = 1.3f, spacing = 1.25f;
        float tStep = spacing / S.Scale / pathLength;
        foreach (int side in new[] { -1, 1 })
            for (int row = 0; row < 2; row++)
                for (float t = L.BarrelStart; t <= L.BarrelEnd; t += tStep)
                {
                    Vector2 c = PathPoint(t);
                    Vector2 forward = (PathPoint(Mathf.Min(1f, t + 0.005f)) - PathPoint(Mathf.Max(0f, t - 0.005f))).normalized;
                    var normal = new Vector2(-forward.y, forward.x);
                    float offset = (HalfWidth(t) + 0.35f) * S.Scale + row * 1.15f + ((float)rng.NextDouble() - 0.5f) * 0.2f;
                    Vector2 w = ToWorld(c) + normal * (side * offset) + forward * (((float)rng.NextDouble() - 0.5f) * 0.3f);

                    // Keep them out of bunkers and off the green
                    Vector2 design = w / S.Scale;
                    if (Ellipse(design, GreenCenter, GreenRadius) < 1.25f) continue;
                    bool inBunker = false;
                    foreach (var b in Bunkers)
                        if (Ellipse(design, b.center, b.radius) < 1.15f) { inBunker = true; break; }
                    if (inBunker) continue;

                    float ground = GroundHeight(w);
                    bool tipped = row == 1 && rng.NextDouble() < 0.12;
                    var barrel = new GameObject(tipped ? "TippedBarrel" : "Barrel");
                    barrel.transform.SetParent(barrelParent, false);
                    var along = new Vector3(forward.x, 0f, forward.y);
                    if (tipped)
                    {
                        // Lying on its side along the fairway, with a glowing puddle leaking out of the end
                        barrel.transform.position = new Vector3(w.x, ground + barrelRadius, w.y);
                        barrel.transform.rotation = Quaternion.LookRotation(along) * Quaternion.Euler(90f, 0f, 0f);
                        Vector2 spill = w + forward * (barrelHeight + 0.7f);
                        var puddle = new GameObject("GooPuddle");
                        puddle.transform.SetParent(barrelParent, false);
                        puddle.transform.position = new Vector3(spill.x, GroundHeight(spill) + 0.02f, spill.y);
                        puddle.transform.localScale = new Vector3(1.3f, 0.04f, 1.0f);
                        puddle.AddComponent<MeshFilter>().sharedMesh = puddleMesh;
                        puddle.AddComponent<MeshRenderer>().sharedMaterial = fireMaterial;
                    }
                    else
                    {
                        barrel.transform.position = new Vector3(w.x, ground - 0.05f, w.y);
                        barrel.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                    }
                    barrel.transform.localScale = new Vector3(barrelRadius, barrelHeight, barrelRadius);
                    barrel.AddComponent<MeshFilter>().sharedMesh = barrelMesh;
                    barrel.AddComponent<MeshRenderer>().sharedMaterial = mat;
                    var collider = barrel.AddComponent<CapsuleCollider>();
                    collider.center = new Vector3(0f, 0.5f, 0f);
                    collider.radius = 1f;
                    collider.height = 1f;
                    GameObjectUtility.SetStaticEditorFlags(barrel, StaticEditorFlags.BatchingStatic);
                }
    }

    /// cupBottomY / meshSuffix are only used by Hole 3's two greens; other holes use the defaults.
    static void BuildCupAndFlag(Transform parent, Material mat, float cupBottomY = float.NaN, string meshSuffix = "")
    {
        float cupBottom = float.IsNaN(cupBottomY) ? greenHeight - CupDepth : cupBottomY;

        var cupGo = new GameObject("CupTrigger");
        cupGo.transform.SetParent(parent, false);
        cupGo.transform.localPosition = new Vector3(cup.x, cupBottom + 0.05f, cup.y);
        var trigger = cupGo.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = 0.07f;

        // Flagstick + flag as one mesh (no collider, so it never blocks the ball)
        const float pinHeight = 2.2f;
        var mb = new MeshBuilder();
        int bands = S.Retro ? 8 : 1;
        for (int b = 0; b < bands; b++)
            mb.Cylinder(0.022f, pinHeight * b / bands, pinHeight * (b + 1) / bands, 8, b % 2 == 0 ? S.PinColor : S.PinBandColor);

        Vector3 top = new Vector3(0f, pinHeight - 0.02f, 0f), low = new Vector3(0f, pinHeight - 0.52f, 0f);
        if (S.Retro)
        {
            var tip = new Vector3(0f, pinHeight - 0.27f, 0.75f); // triangular pennant
            mb.Tri(top, low, tip, Vector3.right, S.FlagColor);
            mb.Tri(top, low, tip, Vector3.left, S.FlagColor * 0.85f);
        }
        else
        {
            Vector3 topOut = top + Vector3.forward * 0.55f, lowOut = low + Vector3.forward * 0.55f;
            foreach (Vector3 side in new[] { Vector3.right, Vector3.left })
            {
                mb.Tri(top, low, lowOut, side, S.FlagColor);
                mb.Tri(top, lowOut, topOut, side, S.FlagColor);
            }
        }

        var flag = new GameObject("Flagstick");
        flag.transform.SetParent(parent, false);
        flag.transform.localPosition = new Vector3(cup.x, cupBottom, cup.y);
        flag.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh(S.MeshPrefix + "Flagstick" + meshSuffix);
        flag.AddComponent<MeshRenderer>().sharedMaterial = mat;
    }

    // ================= HOLE 3: SWAPPABLE GREEN =================

    const float GreenSurfaceLift = 0.01f; // the green objects sit 1 cm above the ground so they never flicker

    /// Builds the terrible green and its flat pool-table twin as separate objects, each with its own cup,
    /// CupTrigger and flagstick, plus a GreenSwap component the cheat can use to switch between them.
    static void BuildSwappableGreens(Transform parent, Material mat)
    {
        var greenRoot = new GameObject("Green");
        greenRoot.transform.SetParent(parent, false);

        PlanGroundhogDamage();
        GameObject terrible = BuildGreenSurface(greenRoot.transform, mat, true);
        BuildMolehills(terrible.transform, mat);
        GameObject pool = BuildGreenSurface(greenRoot.transform, mat, false);

        var swap = greenRoot.AddComponent<GreenSwap>();
        swap.terribleGreen = terrible;
        swap.poolTable = pool;
        pool.SetActive(false);
    }

    // ---- Groundhog damage (all in layout units) ----
    // Molehills: dirt piles dotted all over the green. Tunnels: raised dirt ridges running between them.
    static List<(Vector2 center, float radius, float height, bool burrow)> molehills;
    static List<(Vector2 a, Vector2 b)> tunnels;
    // Tunnels push the turf up into a rounded grassy ridge (~0.45 m tall, ~2 m wide in the world)
    // with a crack of bare dirt along the very top where the ground split.
    const float TunnelHeight = 0.3f, TunnelWidth = 0.45f, TunnelCrack = 0.86f;

    /// Picks where the molehills and tunnels go (repeatable, so every rebuild looks the same).
    static void PlanGroundhogDamage()
    {
        var rng = new System.Random(RandomSeed + 33);
        Vector2 cupDesign = cup / S.Scale;
        molehills = new List<(Vector2, float, float, bool)>();
        for (int attempt = 0; attempt < 2000 && molehills.Count < 24; attempt++)
        {
            var p = GreenCenter + new Vector2(((float)rng.NextDouble() * 2f - 1f) * GreenRadius.x,
                                              ((float)rng.NextDouble() * 2f - 1f) * GreenRadius.y);
            if (Ellipse(p, GreenCenter, GreenRadius) > 0.85f) continue;   // stay off the very edge
            if (Vector2.Distance(p, cupDesign) < 1.2f) continue;          // leave the cup itself alone
            bool crowded = false;
            foreach (var m in molehills)
                if (Vector2.Distance(m.center, p) < 1.1f) { crowded = true; break; }
            if (crowded) continue;
            molehills.Add((p, Mathf.Lerp(0.3f, 0.55f, (float)rng.NextDouble()), Mathf.Lerp(0.18f, 0.32f, (float)rng.NextDouble()),
                           rng.NextDouble() < 0.45));
        }

        // Tunnels: smooth, meandering runs linking each molehill to a neighbor, plus a few that burrow in
        // from the edge of the green, so the whole surface reads as one big tunnel network.
        tunnels = new List<(Vector2, Vector2)>();
        for (int i = 0; i < molehills.Count; i++)
        {
            if (rng.NextDouble() < 0.15) continue;
            int nearest = -1;
            float best = float.MaxValue;
            for (int j = 0; j < molehills.Count; j++)
            {
                if (j == i) continue;
                float d = Vector2.Distance(molehills[i].center, molehills[j].center) * (0.8f + 0.4f * (float)rng.NextDouble());
                if (d < best) { best = d; nearest = j; }
            }
            if (nearest >= 0) AddTunnel(molehills[i].center, molehills[nearest].center);
        }
        for (int k = 0; k < 5 && molehills.Count > 0; k++)
        {
            float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
            Vector2 edge = GreenCenter + new Vector2(Mathf.Cos(angle) * GreenRadius.x, Mathf.Sin(angle) * GreenRadius.y) * 0.97f;
            AddTunnel(edge, molehills[rng.Next(molehills.Count)].center);
        }

        // A tunnel is a smooth curve through two wobbly midpoints, chopped into short straight pieces
        void AddTunnel(Vector2 from, Vector2 to)
        {
            Vector2 along = to - from;
            Vector2 side = new Vector2(-along.y, along.x).normalized;
            Vector2 mid1 = Vector2.Lerp(from, to, 0.33f) + side * ((float)rng.NextDouble() - 0.5f) * 1.6f;
            Vector2 mid2 = Vector2.Lerp(from, to, 0.66f) + side * ((float)rng.NextDouble() - 0.5f) * 1.6f;
            Vector2[] points = { from - along * 0.1f, from, mid1, mid2, to, to + along * 0.1f };

            Vector2 previous = from;
            for (int seg = 1; seg < points.Length - 2; seg++)
                for (int step = 1; step <= 6; step++)
                {
                    Vector2 next = CatmullRom(points[seg - 1], points[seg], points[seg + 1], points[seg + 2], step / 6f);
                    // Keep tunnels from running through the cup
                    if (Vector2.Distance(next, cupDesign) > 1.0f && Vector2.Distance(previous, cupDesign) > 1.0f)
                        tunnels.Add((previous, next));
                    previous = next;
                }
        }
    }

    /// The groundhog green's surface at a world point: mostly flat, with raised tunnel ridges and a bit of
    /// heaved-up ground around each molehill. Also returns how much bare dirt shows there (0..1).
    static float GroundhogGreenHeight(Vector2 worldPoint, out float dirt)
    {
        Vector2 p = worldPoint / S.Scale;
        Vector2 cupDesign = cup / S.Scale;

        float ridge = 0f;
        foreach (var t in tunnels)
        {
            float d = DistanceToSegment(p, t.a, t.b, out _);
            ridge = Mathf.Max(ridge, Mathf.Exp(-(d * d) / (TunnelWidth * TunnelWidth)));
        }

        float heave = 0f, spill = 0f;
        foreach (var m in molehills)
        {
            float d = Vector2.Distance(p, m.center) / m.radius;
            heave = Mathf.Max(heave, 0.35f * m.height * Mathf.Exp(-d * d * 0.5f));
            spill = Mathf.Max(spill, Smooth(1.6f, 1.05f, d));   // loose dirt scattered around each pile
        }

        float bumps = 0.05f * Mathf.Sin(p.x * 2.3f) * Mathf.Sin(p.y * 1.9f);  // generally lumpy, dug-up ground
        float calm = Smooth(1.0f, 0.85f, Ellipse(p, GreenCenter, GreenRadius)) // smooth at the edge...
                   * Smooth(0.25f, 0.6f, Vector2.Distance(p, cupDesign));       // ...and right at the cup

        // Bare dirt only in the crack along the top of each tunnel; the sides stay grassy (pushed-up turf)
        dirt = Mathf.Max(Smooth(TunnelCrack - 0.04f, TunnelCrack + 0.04f, ridge), spill) * calm;
        return greenHeight + (ridge * TunnelHeight + heave + bumps) * calm * S.HeightScale;
    }

    /// Dirt piles (with a dark burrow hole on some) sitting on the groundhog green. The ball bumps off them.
    static void BuildMolehills(Transform parent, Material mat)
    {
        var dirtColor = S.Dirt;
        dirtColor.a = 0f;
        var holeColor = new Color(0f, 0f, 0f, 0f);
        var meshes = new List<Mesh>();
        for (int v = 0; v < 3; v++)
        {
            var mb = new MeshBuilder();
            mb.Blob(Vector3.zero, 1f, 5, 8, dirtColor * (0.9f + 0.08f * v), 140 + v, 0.3f);
            meshes.Add(mb.ToMesh(S.MeshPrefix + "Molehill" + v));
        }
        var holeBuilder = new MeshBuilder();
        holeBuilder.Blob(Vector3.zero, 1f, 3, 8, holeColor, 150, 0.1f);
        Mesh holeMesh = holeBuilder.ToMesh(S.MeshPrefix + "Burrow");

        var pilesParent = new GameObject("Molehills").transform;
        pilesParent.SetParent(parent, false);
        var rng = new System.Random(RandomSeed + 34);
        foreach (var m in molehills)
        {
            Vector2 world = ToWorld(m.center);
            float radius = m.radius * S.Scale;
            float height = m.height * S.HeightScale;
            float ground = GroundhogGreenHeight(world, out _) + GreenSurfaceLift;

            var pile = new GameObject("Molehill");
            pile.transform.SetParent(pilesParent, false);
            pile.transform.position = new Vector3(world.x, ground - height * 0.25f, world.y);
            pile.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            pile.transform.localScale = new Vector3(radius, height, radius);
            Mesh mesh = meshes[rng.Next(meshes.Count)];
            pile.AddComponent<MeshFilter>().sharedMesh = mesh;
            pile.AddComponent<MeshRenderer>().sharedMaterial = mat;
            var collider = pile.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.convex = true;

            if (m.burrow)
            {
                // A dark hole on top where the groundhog pops out
                var hole = new GameObject("Burrow");
                hole.transform.SetParent(pilesParent, false);
                hole.transform.position = new Vector3(world.x, ground - height * 0.25f + height * 0.9f, world.y);
                hole.transform.localScale = new Vector3(radius * 0.38f, height * 0.12f, radius * 0.38f);
                hole.AddComponent<MeshFilter>().sharedMesh = holeMesh;
                hole.AddComponent<MeshRenderer>().sharedMaterial = mat;
            }
        }
    }

    /// Distance along a ray from 'from' in direction 'dir' to the edge of an ellipse (all in world meters).
    static float RayToEllipse(Vector2 from, Vector2 dir, Vector2 center, Vector2 radius)
    {
        var o = new Vector2((from.x - center.x) / radius.x, (from.y - center.y) / radius.y);
        var v = new Vector2(dir.x / radius.x, dir.y / radius.y);
        float a = Vector2.Dot(v, v), b = 2f * Vector2.Dot(o, v), c = Vector2.Dot(o, o) - 1f;
        return (-b + Mathf.Sqrt(Mathf.Max(0f, b * b - 4f * a * c))) / (2f * a);
    }

    /// One green surface (groundhog green or pool table) as a ring mesh around the cup, with the cup cut into it.
    static GameObject BuildGreenSurface(Transform parent, Material mat, bool terrible)
    {
        const int segments = 128, rings = 64; // fine enough to show the thin tunnel ridges
        Vector2 center = ToWorld(GreenCenter);
        Vector2 radius = GreenRadius * S.Scale;

        Color turf = Color.Lerp(S.GreenDark, S.GreenLight, 0.5f);
        turf.a = 0f;
        Color dirtColor = S.Dirt;
        dirtColor.a = 0f;
        var felt = new Color(0f, 0.47f, 0.97f, 0f);    // NES blue pool-table felt
        var cushion = new Color(0f, 0.35f, 0.97f, 0f); // deeper blue on the rail cushions
        var wood = new Color(0.67f, 0.49f, 0f, 0f);    // NES brown wood
        var turfPattern = new Vector2(S.GreenChecker, 1f);

        var verts = new List<Vector3>();
        var colors = new List<Color>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();

        float HeightAt(Vector2 w, bool atEdge, out float dirt)
        {
            dirt = 0f;
            if (atEdge) return GroundHeight(w) + GreenSurfaceLift;
            return (terrible ? GroundhogGreenHeight(w, out dirt) : greenHeight) + GreenSurfaceLift;
        }

        // Shared-vertex ring grid, from the cup's rim out to the green's edge (denser near the cup)
        var edgePoints = new Vector2[segments];
        for (int k = 0; k <= rings; k++)
            for (int s = 0; s < segments; s++)
            {
                float angle = s * Mathf.PI * 2f / segments;
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float edgeDistance = RayToEllipse(cup, dir, center, radius);
                float dist = Mathf.Lerp(CupRadius, edgeDistance, Mathf.Pow(k / (float)rings, 1.3f));
                Vector2 w = cup + dir * dist;
                if (k == rings) edgePoints[s] = w;
                float h = HeightAt(w, k == rings, out float dirt);
                verts.Add(new Vector3(w.x, h, w.y));
                colors.Add(ToMeshColor(terrible ? Color.Lerp(turf, dirtColor, dirt) : felt));
                uvs.Add(terrible ? new Vector2(turfPattern.x, 1f - dirt) : Vector2.zero); // no checkers on bare dirt
            }
        for (int k = 0; k < rings; k++)
            for (int s = 0; s < segments; s++)
            {
                int s1 = (s + 1) % segments;
                int i0 = k * segments + s, i1 = k * segments + s1, o0 = (k + 1) * segments + s, o1 = (k + 1) * segments + s1;
                // Clockwise seen from above = facing up
                tris.Add(i0); tris.Add(o1); tris.Add(o0);
                tris.Add(i0); tris.Add(i1); tris.Add(o1);
            }

        // Flat-shaded extra pieces (cup walls, pool rails) use their own vertices
        void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 facing, Color col)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), facing) < 0f) (b, c) = (c, b);
            col = ToMeshColor(col);
            int start = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            colors.Add(col); colors.Add(col); colors.Add(col);
            uvs.Add(Vector2.zero); uvs.Add(Vector2.zero); uvs.Add(Vector2.zero);
            tris.Add(start); tris.Add(start + 1); tris.Add(start + 2);
        }
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing, Color col)
        {
            Tri(a, b, c, facing, col);
            Tri(a, c, d, facing, col);
        }

        // Cup: walls hang from the rim (ring 0) down to a flat bottom
        float rimLowest = float.MaxValue;
        for (int s = 0; s < segments; s++) rimLowest = Mathf.Min(rimLowest, verts[s].y);
        float bottom = rimLowest - CupDepth;
        var cupCenter = new Vector3(cup.x, bottom, cup.y);
        for (int s = 0; s < segments; s++)
        {
            Vector3 a = verts[s], b = verts[(s + 1) % segments];
            Vector3 aLiner = a + Vector3.down * 0.025f, bLiner = b + Vector3.down * 0.025f;
            Vector3 aBottom = new Vector3(a.x, bottom, a.z), bBottom = new Vector3(b.x, bottom, b.z);
            Vector3 inward = cupCenter - (a + b) * 0.5f;
            inward.y = 0f;
            Quad(a, b, bLiner, aLiner, inward, S.CupLiner);
            Quad(aLiner, bLiner, bBottom, aBottom, inward, S.CupInside);
            Tri(cupCenter, aBottom, bBottom, Vector3.up, S.CupInside * 0.7f);
        }

        // Pool table: a low wooden rail with a felt cushion around the edge
        if (!terrible)
        {
            const float railHeight = 0.06f, railWidth = 0.35f;
            for (int s = 0; s < segments; s++)
            {
                Vector2 e0 = edgePoints[s], e1 = edgePoints[(s + 1) % segments];
                Vector2 n0 = new Vector2((e0.x - center.x) / (radius.x * radius.x), (e0.y - center.y) / (radius.y * radius.y)).normalized;
                Vector2 n1 = new Vector2((e1.x - center.x) / (radius.x * radius.x), (e1.y - center.y) / (radius.y * radius.y)).normalized;
                Vector2 f0 = e0 + n0 * railWidth, f1 = e1 + n1 * railWidth;
                float base0 = GroundHeight(e0) + GreenSurfaceLift, base1 = GroundHeight(e1) + GreenSurfaceLift;
                float top0 = base0 + railHeight, top1 = base1 + railHeight;
                float outer0 = GroundHeight(f0) - 0.05f, outer1 = GroundHeight(f1) - 0.05f;
                Vector3 outward = new Vector3(n0.x + n1.x, 0f, n0.y + n1.y);

                // Cushion facing the table
                Quad(new Vector3(e0.x, base0, e0.y), new Vector3(e1.x, base1, e1.y), new Vector3(e1.x, top1, e1.y), new Vector3(e0.x, top0, e0.y), -outward, cushion);
                // Wooden top
                Quad(new Vector3(e0.x, top0, e0.y), new Vector3(e1.x, top1, e1.y), new Vector3(f1.x, top1, f1.y), new Vector3(f0.x, top0, f0.y), Vector3.up, wood);
                // Wooden outer side, down into the ground
                Quad(new Vector3(f0.x, top0, f0.y), new Vector3(f1.x, top1, f1.y), new Vector3(f1.x, outer1, f1.y), new Vector3(f0.x, outer0, f0.y), outward, wood * 0.8f);
            }
        }

        string name = terrible ? "TerribleGreen" : "PoolTable";
        var mesh = new Mesh { name = S.MeshPrefix + name };
        mesh.SetVertices(verts);
        mesh.SetColors(colors);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        SaveMesh(mesh);

        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var surface = new GameObject("Surface");
        surface.transform.SetParent(go.transform, false);
        surface.AddComponent<MeshFilter>().sharedMesh = mesh;
        surface.AddComponent<MeshRenderer>().sharedMaterial = mat;
        surface.AddComponent<MeshCollider>().sharedMesh = mesh;

        BuildCupAndFlag(go.transform, mat, bottom, terrible ? "_Terrible" : "_Pool");
        return go;
    }

    static void BuildTeeMarkers(Transform parent, Material mat)
    {
        var mb = new MeshBuilder();
        mb.Blob(Vector3.zero, S.Retro ? 0.14f : 0.07f, 4, 7, S.TeeMarkerColor, 3, 0.1f);
        Mesh mesh = mb.ToMesh(S.MeshPrefix + "TeeMarker");

        Vector2 tee = ToWorld(TeeCenter);
        foreach (float x in new[] { -1.2f, 1.2f })
        {
            var m = new GameObject("TeeMarker");
            m.transform.SetParent(parent, false);
            m.transform.localPosition = new Vector3(tee.x + x * S.Scale, teeHeight + (S.Retro ? 0.1f : 0.05f), tee.y + 0.9f * S.Scale);
            m.AddComponent<MeshFilter>().sharedMesh = mesh;
            m.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }
    }

    /// Is this world point somewhere decorations shouldn't go (fairway, green, tee, pond, bunkers)?
    /// fairwayMargin is in layout units.
    static bool IsPlayArea(Vector2 worldPoint, float fairwayMargin)
    {
        Vector2 p = worldPoint / S.Scale;
        ProjectOnPath(p, out float d, out float t);
        if (d < HalfWidth(t) + fairwayMargin) return true;
        if (Ellipse(p, GreenCenter, GreenRadius) < 1.9f) return true;
        if (Vector2.Distance(p, TeeCenter) < 7f) return true;
        if (L.HasPond && Ellipse(p, PondCenter, PondRadius) < 1.5f) return true;
        if (L.HasMeteor && Vector2.Distance(p, L.MeteorCenter) < L.MeteorRadius * 1.95f) return true;
        if (L.HasShortcut && DistanceToSegment(p, L.ShortcutStart, L.ShortcutEnd, out _) < L.ShortcutHalfWidth + 1f) return true;
        if (L.HasRiver && DistanceToPolyline(p, L.RiverPoints) < L.RiverHalfWidth + 2.5f) return true;
        if (L.HasPowerPlant)
        {
            Vector2 fromPlant = p - L.PlantCenter;
            if (Mathf.Abs(fromPlant.x) < L.PlantHalfSize.x * 1.25f && Mathf.Abs(fromPlant.y) < L.PlantHalfSize.y * 1.25f) return true;
        }
        foreach (var b in Bunkers)
            if (Ellipse(p, b.center, b.radius) < 1.4f) return true;
        return false;
    }

    static void BuildTrees(Transform parent, Material mat)
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
            round.Cylinder(S.Retro ? 0.12f : 0.15f, -0.3f, S.RoundTrunkTop, 6, S.Trunk);
            round.Blob(new Vector3(0f, S.RoundLeavesY, 0f), S.RoundLeavesRadius, 5, 8, S.RoundColors[v], v, S.Retro ? 0.15f : 0.3f);
            meshes.Add(round.ToMesh(S.MeshPrefix + "RoundTree" + v));
        }

        var treesParent = new GameObject("Trees").transform;
        treesParent.SetParent(parent, false);
        var rng = new System.Random(RandomSeed);
        var placed = new List<Vector2>();
        var planted = new List<(GameObject tree, Vector2 position, bool pine)>();

        // Thick woods: fill the inside of the dogleg on a tight jittered grid (the shortcut lane stays clear),
        // and wall off the outside of the meteor crater so going around it is a real struggle.
        if (L.InsideForest || L.HasMeteor)
        {
            float step = 2.1f * S.TreeScale;
            for (float x = GridMinX + step; x < GridMaxX - step; x += step)
                for (float z = GridMinZ + step; z < GridMaxZ - step; z += step)
                {
                    var p = new Vector2(x + ((float)rng.NextDouble() - 0.5f) * step * 0.6f,
                                        z + ((float)rng.NextDouble() - 0.5f) * step * 0.6f);
                    bool inWoods = L.InsideForest && InsideDogleg(p);
                    bool besideMeteor = false;
                    if (L.HasMeteor)
                    {
                        float md = Vector2.Distance(p / S.Scale, L.MeteorCenter) / L.MeteorRadius;
                        besideMeteor = md > 2.0f && md < 2.9f;
                    }
                    if (!inWoods && !besideMeteor) continue;
                    if (IsPlayArea(p, besideMeteor ? 0.5f : 2.5f) || IsCrowded(placed, p, 1.6f * S.TreeScale)) continue;
                    placed.Add(p);
                    PlaceTree(p);
                }
        }

        // A bigger course needs more trees to look as full (capped to stay friendly to the Quest 2)
        int treeCount = placed.Count + Mathf.Min(Mathf.RoundToInt(TreeCount * S.Scale * S.Scale / S.TreeScale * 1.3f), 380);
        for (int attempt = 0; attempt < 8000 && placed.Count < treeCount; attempt++)
        {
            var p = RandomPoint(rng, 1f);
            if (IsPlayArea(p, 6.5f) || IsCrowded(placed, p, 2.6f * S.TreeScale)) continue;
            placed.Add(p);
            PlaceTree(p);
        }

        if (L.HasMeteor) IgniteTreesNearMeteor();

        void PlaceTree(Vector2 p)
        {
            var tree = new GameObject("Tree");
            tree.transform.SetParent(treesParent, false);
            tree.transform.localPosition = new Vector3(p.x, GroundHeight(p), p.y);
            tree.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            tree.transform.localScale = Vector3.one * Mathf.Lerp(0.8f, 1.5f, (float)rng.NextDouble()) * S.TreeScale;
            int meshIndex = rng.Next(meshes.Count);
            planted.Add((tree, p, meshIndex % 2 == 0)); // even = pine, odd = round
            tree.AddComponent<MeshFilter>().sharedMesh = meshes[meshIndex];
            tree.AddComponent<MeshRenderer>().sharedMaterial = mat;
            var trunk = tree.AddComponent<CapsuleCollider>();
            trunk.center = new Vector3(0f, 0.8f, 0f);
            trunk.height = 1.6f;
            trunk.radius = 0.2f;
            GameObjectUtility.SetStaticEditorFlags(tree, StaticEditorFlags.BatchingStatic);
        }

        // The meteor has set a few of the nearest trees on fire: they turn charred and get flames and smoke.
        void IgniteTreesNearMeteor()
        {
            var charred = new Color(0.31f, 0.19f, 0f, 0f);  // NES dark brown
            var black = new Color(0f, 0f, 0f, 0f);
            var burntPineBuilder = new MeshBuilder();
            burntPineBuilder.Cylinder(0.14f, -0.3f, 1.0f, 6, black);
            burntPineBuilder.Cone(1.25f, 0.7f, 1.6f, 8, charred);
            burntPineBuilder.Cone(0.95f, 1.4f, 1.4f, 8, charred);
            burntPineBuilder.Cone(0.65f, 2.1f, 1.2f, 8, charred);
            Mesh burntPine = burntPineBuilder.ToMesh(S.MeshPrefix + "BurntPine");
            var burntRoundBuilder = new MeshBuilder();
            burntRoundBuilder.Cylinder(S.Retro ? 0.12f : 0.15f, -0.3f, S.RoundTrunkTop, 6, black);
            burntRoundBuilder.Blob(new Vector3(0f, S.RoundLeavesY, 0f), S.RoundLeavesRadius, 5, 8, charred, 7, 0.15f);
            Mesh burntRound = burntRoundBuilder.ToMesh(S.MeshPrefix + "BurntRoundTree");

            // Pick the trees closest to the meteor, preferring the ones on the side facing the tee
            Vector2 meteorWorld = ToWorld(L.MeteorCenter);
            float reach = L.MeteorRadius * S.Scale * 3.2f;
            var candidates = planted.FindAll(t => Vector2.Distance(t.position, meteorWorld) < reach);
            candidates.Sort((a, b) =>
            {
                float scoreA = Vector2.Distance(a.position, meteorWorld) + (a.position.y > meteorWorld.y ? reach : 0f);
                float scoreB = Vector2.Distance(b.position, meteorWorld) + (b.position.y > meteorWorld.y ? reach : 0f);
                return scoreA.CompareTo(scoreB);
            });

            var fireRng = new System.Random(RandomSeed + 9);
            var fireParent = new GameObject("BurningTrees").transform;
            fireParent.SetParent(treesParent.parent, false);
            const int burning = 6;
            for (int i = 0; i < Mathf.Min(burning, candidates.Count); i++)
            {
                var (tree, _, pine) = candidates[i];
                tree.GetComponent<MeshFilter>().sharedMesh = pine ? burntPine : burntRound;
                float scale = tree.transform.lossyScale.x;

                // Flames in the canopy (positions in the tree's own units)
                Vector3[] spots = pine
                    ? new[] { new Vector3(0f, 1.5f, 0f), new Vector3(0.45f, 2.3f, 0.15f), new Vector3(-0.35f, 0.95f, -0.3f) }
                    : new[] { new Vector3(0f, S.RoundLeavesY + 0.7f, 0f), new Vector3(0.75f, S.RoundLeavesY, 0.2f), new Vector3(-0.7f, S.RoundLeavesY - 0.2f, -0.3f) };
                foreach (Vector3 spot in spots)
                    AddFlame(fireParent, tree.transform.TransformPoint(spot), scale * Mathf.Lerp(0.9f, 1.3f, (float)fireRng.NextDouble()), fireRng);

                Vector3 top = tree.transform.TransformPoint(new Vector3(0f, pine ? 3.5f : S.RoundLeavesY + 1.5f, 0f));
                AddSmoke(fireParent, top, scale * 0.8f, 15f, fireRng);
            }
        }
    }

    /// Round bushes lining the fairway (retro only).
    static void BuildBushes(Transform parent, Material mat)
    {
        var meshes = new List<Mesh>();
        for (int v = 0; v < 3; v++)
        {
            var mb = new MeshBuilder();
            mb.Blob(new Vector3(0f, 0.3f, 0f), 0.55f, 4, 7, S.RoundColors[v] * 0.9f, 10 + v, 0.2f);
            meshes.Add(mb.ToMesh(S.MeshPrefix + "Bush" + v));
        }

        var bushParent = new GameObject("Bushes").transform;
        bushParent.SetParent(parent, false);
        var rng = new System.Random(RandomSeed + 1);
        var placed = new List<Vector2>();

        int bushCount = Mathf.RoundToInt(45 * S.Scale);
        for (int attempt = 0; attempt < 6000 && placed.Count < bushCount; attempt++)
        {
            var p = RandomPoint(rng, 2f);
            ProjectOnPath(p / S.Scale, out float d, out float t);
            if (d > HalfWidth(t) + 9f) continue; // keep them near the hole so they frame it
            if (IsPlayArea(p, 2.5f) || IsCrowded(placed, p, 1.8f * S.TreeScale)) continue;
            placed.Add(p);

            var bush = new GameObject("Bush");
            bush.transform.SetParent(bushParent, false);
            bush.transform.localPosition = new Vector3(p.x, GroundHeight(p) - 0.05f, p.y);
            bush.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            bush.transform.localScale = Vector3.one * Mathf.Lerp(0.7f, 1.3f, (float)rng.NextDouble()) * S.TreeScale;
            bush.AddComponent<MeshFilter>().sharedMesh = meshes[rng.Next(meshes.Count)];
            bush.AddComponent<MeshRenderer>().sharedMaterial = mat;
            GameObjectUtility.SetStaticEditorFlags(bush, StaticEditorFlags.BatchingStatic);
        }
    }

    /// Colored yardage posts along the left edge of the fairway (retro only).
    static void BuildDistancePosts(Transform parent, Material mat)
    {
        (float t, Color color)[] posts =
        {
            (0.35f, new Color(0f, 0.35f, 0.97f, 0f)),
            (0.55f, new Color(0.99f, 0.99f, 0.99f, 0f)),
            (0.75f, new Color(0.97f, 0.22f, 0f, 0f)),
        };
        var postParent = new GameObject("DistancePosts").transform;
        postParent.SetParent(parent, false);

        foreach (var post in posts)
        {
            var mb = new MeshBuilder();
            mb.Cylinder(0.05f, -0.2f, 0.7f, 6, post.color);
            mb.Cone(0.08f, 0.7f, 0.14f, 6, post.color);
            Mesh mesh = mb.ToMesh(S.MeshPrefix + "Post" + Mathf.RoundToInt(post.t * 100f));

            Vector2 c = PathPoint(post.t);
            Vector2 forward = (PathPoint(post.t + 0.01f) - c).normalized;
            Vector2 left = new Vector2(-forward.y, forward.x);
            Vector2 p = ToWorld(c + left * (HalfWidth(post.t) + 0.6f));

            var go = new GameObject("DistancePost");
            go.transform.SetParent(postParent, false);
            go.transform.localPosition = new Vector3(p.x, GroundHeight(p), p.y);
            go.transform.localScale = Vector3.one * S.TreeScale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }
    }

    /// Big puffy low-poly clouds in a ring around the course (retro only).
    static void BuildClouds(Transform parent)
    {
        Material mat = GetShaderMaterial(S.MaterialPrefix + "Clouds", S.TerrainShader, m =>
        {
            // Neutral white/gray shading only, so the palette snaps clouds to NES white and gray (never blue)
            m.SetColor("_SkyColor", new Color(1f, 1f, 1f));
            m.SetColor("_GroundColor", new Color(0.82f, 0.82f, 0.82f));
            m.SetFloat("_LightStrength", 0.25f);
            m.SetFloat("_FogStrength", 0f);
            m.SetFloat("_Dither", 0f);
            m.SetFloat("_Speckle", 0f);
            m.SetFloat("_PixelSize", 1.0f); // clouds are huge and far away, so use bigger pixels
        });

        Vector3[][] layouts =
        {
            new[] { new Vector3(0f, 0f, 0f), new Vector3(1.1f, -0.15f, 0.2f), new Vector3(-1.0f, -0.2f, -0.1f), new Vector3(0.3f, 0.4f, -0.3f) },
            new[] { new Vector3(0f, 0f, 0f), new Vector3(1.3f, -0.1f, -0.2f), new Vector3(2.3f, -0.3f, 0.1f), new Vector3(-0.9f, -0.25f, 0.2f) },
            new[] { new Vector3(0f, 0f, 0f), new Vector3(-1.2f, -0.1f, 0.3f), new Vector3(0.9f, -0.2f, -0.4f) },
        };
        var meshes = new List<Mesh>();
        for (int v = 0; v < layouts.Length; v++)
        {
            var mb = new MeshBuilder();
            for (int i = 0; i < layouts[v].Length; i++)
                mb.Blob(layouts[v][i], i == 0 ? 1.0f : 0.75f, 5, 9, new Color(1f, 1f, 1f, 0f), 50 + v * 10 + i, 0.15f);
            meshes.Add(mb.ToMesh(S.MeshPrefix + "Cloud" + v));
        }

        var cloudParent = new GameObject("Clouds").transform;
        cloudParent.SetParent(parent, false);
        var rng = new System.Random(RandomSeed + 2);
        Vector2 middle = ToWorld(L.CloudCenter);
        var center = new Vector3(middle.x, 0f, middle.y);
        const int count = 16;
        for (int k = 0; k < count; k++)
        {
            float angle = (k + (float)rng.NextDouble() * 0.6f) / count * Mathf.PI * 2f;
            float distance = Mathf.Lerp(110f, 170f, (float)rng.NextDouble()) * S.Scale;
            var cloud = new GameObject("Cloud");
            cloud.transform.SetParent(cloudParent, false);
            cloud.transform.localPosition = center + new Vector3(Mathf.Cos(angle) * distance, Mathf.Lerp(35f, 65f, (float)rng.NextDouble()) * S.Scale, Mathf.Sin(angle) * distance);
            cloud.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            float scale = Mathf.Lerp(7f, 13f, (float)rng.NextDouble()) * S.Scale;
            cloud.transform.localScale = new Vector3(scale, scale * 0.7f, scale);
            cloud.AddComponent<MeshFilter>().sharedMesh = meshes[rng.Next(meshes.Count)];
            cloud.AddComponent<MeshRenderer>().sharedMaterial = mat;
            GameObjectUtility.SetStaticEditorFlags(cloud, StaticEditorFlags.BatchingStatic);
        }
    }

    static Vector2 RandomPoint(System.Random rng, float margin) =>
        new Vector2(Mathf.Lerp(GridMinX + margin, GridMaxX - margin, (float)rng.NextDouble()),
                    Mathf.Lerp(GridMinZ + margin, GridMaxZ - margin, (float)rng.NextDouble()));

    static bool IsCrowded(List<Vector2> placed, Vector2 p, float spacing)
    {
        foreach (Vector2 other in placed)
            if ((other - p).sqrMagnitude < spacing * spacing) return true;
        return false;
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

    /// Sun, fog, and (for retro) the bright banded sky.
    static void SetupAtmosphere(Scene scene)
    {
        RenderSettings.fog = S.Fog;
        RenderSettings.fogMode = FogMode.Linear;

        if (S.Retro)
        {
            // Flat NES blues in a few hard bands
            var horizon = new Color(0.643f, 0.894f, 0.988f);
            RenderSettings.fogColor = horizon;
            RenderSettings.fogStartDistance = 55f;
            RenderSettings.fogEndDistance = 210f;
            RenderSettings.skybox = GetShaderMaterial(S.MaterialPrefix + "Sky", SkyShader, m =>
            {
                m.SetColor("_TopColor", new Color(0.408f, 0.533f, 0.988f));
                m.SetColor("_HorizonColor", horizon);
                m.SetColor("_BottomColor", horizon);
                m.SetFloat("_Falloff", 0.7f);
                m.SetFloat("_Bands", 4f);
                m.SetFloat("_ColorSteps", 0f);
            });
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.75f, 0.85f, 1.0f);
            RenderSettings.ambientEquatorColor = new Color(0.60f, 0.70f, 0.65f);
            RenderSettings.ambientGroundColor = new Color(0.35f, 0.40f, 0.30f);
        }
        else
        {
            RenderSettings.fogColor = new Color(0.74f, 0.85f, 0.95f);
            RenderSettings.fogStartDistance = 40f;
            RenderSettings.fogEndDistance = 160f;
        }

        Light sun = null;
        foreach (GameObject go in scene.GetRootGameObjects())
            foreach (Light light in go.GetComponentsInChildren<Light>())
                if (light.type == LightType.Directional) { sun = light; break; }
        if (sun == null)
        {
            var go = new GameObject("Directional Light");
            Undo.RegisterCreatedObjectUndo(go, "Add sun");
            sun = go.AddComponent<Light>();
            sun.type = LightType.Directional;
        }

        Undo.RecordObject(sun, "Hole 1 lighting");
        Undo.RecordObject(sun.transform, "Hole 1 lighting");
        // Retro: must match the direction the terrain's pixels were baked with
        sun.transform.rotation = S.Retro ? RetroSunRotation : Quaternion.Euler(40f, -30f, 0f);
        sun.color = S.Retro ? RetroSunColor : new Color(1f, 0.95f, 0.86f);
        sun.intensity = S.Retro ? RetroSunIntensity : 1.3f;
        sun.shadows = LightShadows.None; // real-time shadows are expensive on Quest 2
    }

    // ================= MESH HELPERS =================

    /// Builds flat-shaded, vertex-colored meshes (each triangle has its own vertices).
    class MeshBuilder
    {
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Color> colors = new List<Color>();
        readonly List<int> tris = new List<int>();

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 facing, Color col)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), facing) < 0f) (b, c) = (c, b);
            col = ToMeshColor(col);
            int s = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            colors.Add(col); colors.Add(col); colors.Add(col);
            tris.Add(s); tris.Add(s + 1); tris.Add(s + 2);
        }

        /// a, b, c, d are the corners in order around the edge.
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing, Color col)
        {
            Tri(a, b, c, facing, col);
            Tri(a, c, d, facing, col);
        }

        /// A box sitting on its center point's height (center.y = bottom).
        public void Box(Vector3 center, Vector3 size, Color col)
        {
            Vector3 h = new Vector3(size.x * 0.5f, 0f, size.z * 0.5f);
            float y0 = center.y, y1 = center.y + size.y;
            Vector3 P(float sx, float y, float sz) => new Vector3(center.x + sx * h.x, y, center.z + sz * h.z);
            Quad(P(-1, y1, -1), P(1, y1, -1), P(1, y1, 1), P(-1, y1, 1), Vector3.up, col);                // top
            Quad(P(-1, y0, -1), P(1, y0, -1), P(1, y1, -1), P(-1, y1, -1), Vector3.back, col * 0.9f);     // front (-z)
            Quad(P(-1, y0, 1), P(1, y0, 1), P(1, y1, 1), P(-1, y1, 1), Vector3.forward, col * 0.9f);      // back (+z)
            Quad(P(-1, y0, -1), P(-1, y0, 1), P(-1, y1, 1), P(-1, y1, -1), Vector3.left, col * 0.8f);     // left
            Quad(P(1, y0, -1), P(1, y0, 1), P(1, y1, 1), P(1, y1, -1), Vector3.right, col * 0.8f);        // right
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
                Tri(Vector3.up * y1, t0, t1, Vector3.up, col); // cap
            }
        }

        /// A cone standing on the ground, optionally shifted sideways by offset.
        public void Cone(float radius, float baseY, float height, int sides, Color col, Vector3 offset = default)
        {
            var apex = offset + new Vector3(0f, baseY + height, 0f);
            var center = offset + new Vector3(0f, baseY, 0f);
            for (int k = 0; k < sides; k++)
            {
                float a0 = k * Mathf.PI * 2f / sides, a1 = (k + 1) * Mathf.PI * 2f / sides;
                var p0 = offset + new Vector3(Mathf.Cos(a0) * radius, baseY, Mathf.Sin(a0) * radius);
                var p1 = offset + new Vector3(Mathf.Cos(a1) * radius, baseY, Mathf.Sin(a1) * radius);
                Vector3 outward = (p0 + p1) * 0.5f - center + Vector3.up * (radius * radius / height);
                Tri(p0, p1, apex, outward, col);
                Tri(center, p0, p1, Vector3.down, col * 0.6f);
            }
        }

        /// A lumpy low-poly sphere. lumpiness = how much each point's radius varies (0 = perfectly round).
        public void Blob(Vector3 center, float radius, int rings, int sides, Color col, int seed, float lumpiness)
        {
            var rng = new System.Random(seed * 977 + 13);
            var ring = new Vector3[rings - 1, sides];
            for (int r = 1; r < rings; r++)
                for (int k = 0; k < sides; k++)
                {
                    float lat = Mathf.PI * r / rings, lon = Mathf.PI * 2f * k / sides;
                    float rad = radius * (1f - lumpiness * 0.5f + lumpiness * (float)rng.NextDouble());
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
            mesh.SetUVs(0, new List<Vector2>(new Vector2[verts.Count])); // no checker pattern on props
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

    /// Generated meshes are rebuilt every run, so clear out this style's previous ones first.
    static void DeleteOldGeneratedMeshes()
    {
        if (!AssetDatabase.IsValidFolder(MeshFolder)) return;
        foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { MeshFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileName(path).StartsWith(S.MeshPrefix)) AssetDatabase.DeleteAsset(path);
        }
    }

    /// Ground material. Retro: unlit baked pixel texture. Realistic: vertex-color lit.
    static Material GetTerrainMaterial() => S.Retro
        ? GetShaderMaterial(S.MaterialPrefix + L.TerrainMaterialName, BakedShader, m => m.SetFloat("_FogStrength", 0f))
        : GetShaderMaterial(S.MaterialPrefix + L.TerrainMaterialName, VertexColorShader, null);

    /// Retro material for trees, bushes, water, flag and posts (NES palette + dithering, in world-space pixels).
    static Material GetPropMaterial() =>
        GetShaderMaterial(S.MaterialPrefix + "Props", PixelShader, m =>
        {
            m.SetFloat("_PixelSize", BakePixelSize);
            m.SetColor("_SkyColor", BakeAmbientSky);
            m.SetColor("_GroundColor", BakeAmbientGround);
            m.SetFloat("_LightStrength", BakeSunStrength);
            m.SetFloat("_CheckerContrast", BakeCheckerContrast);
            m.SetFloat("_Dither", 0f);   // solid colors
            m.SetFloat("_Speckle", 0f);  // random dark pixels are only on the ground
            m.SetFloat("_FogStrength", 0f);
        });

    /// Loads a material, or creates it with the given shader and settings the first time.
    /// Existing materials aren't overwritten, so the art team can tweak them in the Inspector. The one exception:
    /// retro materials are refreshed once whenever RetroMaterialVersion changes.
    static Material GetShaderMaterial(string name, string shaderName, System.Action<Material> setup)
    {
        EnsureFolder(MaterialFolder);
        string path = MaterialFolder + "/" + name + ".mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);

        Shader shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogError("Hole1Builder: shader '" + shaderName + "' not found. Are the files in _Game/Shaders?");
            shader = Shader.Find("Universal Render Pipeline/Lit");
        }

        if (mat != null)
        {
            AssetImporter existing = AssetImporter.GetAtPath(path);
            bool upToDate = !S.Retro || (existing != null && existing.userData == RetroMaterialVersion);
            if (upToDate) return mat;

            mat.shader = shader;
            setup?.Invoke(mat);
            EditorUtility.SetDirty(mat);
            StampVersion(path);
            return mat;
        }

        mat = new Material(shader);
        setup?.Invoke(mat);
        AssetDatabase.CreateAsset(mat, path);
        if (S.Retro) StampVersion(path);
        return mat;
    }

    static void StampVersion(string path)
    {
        AssetImporter importer = AssetImporter.GetAtPath(path);
        if (importer == null) return;
        importer.userData = RetroMaterialVersion;
        EditorUtility.SetDirty(importer);
        AssetDatabase.WriteImportSettingsIfDirty(path);
    }

    static Material GetLitMaterial(string name, Color color, float smoothness) =>
        GetShaderMaterial(S.MaterialPrefix + name, "Universal Render Pipeline/Lit", m =>
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        });

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
