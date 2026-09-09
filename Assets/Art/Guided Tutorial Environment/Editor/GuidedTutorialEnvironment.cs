using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using Random = UnityEngine.Random;

namespace VRTutorial.EditorTools
{
    /// <summary>
    /// Builds the navigation-tutorial environment as a quiet Australian suburban streetscape:
    /// start pad -> 20 m footpath -> four-way intersection -> three arms, the east one
    /// zigzagging into an end zone beside a road with a bus shelter.
    ///
    /// The PATH LAYOUT IS UNCHANGED from the original build - PathRects, FencePerimeter,
    /// KerbLines and LampPosts are the same numbers. Everything added here dresses that
    /// skeleton; the route a participant walks is identical.
    ///
    /// Menu:  Tools > VR Tutorial > ...
    /// All measurements are in metres and live in the LAYOUT region below.
    ///
    /// Performance: every batch of dressing is merged into one mesh with one renderer, so
    /// prop count costs vertices, not draw calls. Everything is marked static, so the scene
    /// is ready for a lighting bake - which is the single biggest visual win available and
    /// costs nothing at runtime.
    ///
    /// Research note: landmarks at the decision points are behind a menu toggle
    /// (Tools > VR Tutorial > Options > Include Landmarks). Landmark availability changes
    /// wayfinding difficulty, so it is a variable you set deliberately, not a decoration.
    /// </summary>
    public static class VRTutorialSceneBuilder
    {
        // ---------------------------------------------------------------- paths
        private const string RootName        = "TutorialEnvironment";
        private const string BaseFolder      = "Assets/VRTutorial";
        private const string MaterialFolder  = BaseFolder + "/Materials";
        private const string GeneratedFolder = BaseFolder + "/Generated";
        private const string SceneFolder     = BaseFolder + "/Scenes";
        private const string LandmarkPrefKey = "VRTutorial.IncludeLandmarks";

        // --------------------------------------------------------------- LAYOUT
        #region LAYOUT  (edit these to reshape the level)

        // Vertical
        private const float GroundThickness = 0.20f;   // grass slab thickness
        private const float PathTopY        = 0.02f;   // paving sits 2 cm proud of grass
        private const float PathThickness   = 0.12f;
        private const float OuterGroundTopY = -0.01f;  // just under the park, so no z-fighting
        private const float RoadTopY        = -0.04f;  // roads sit below kerb level
        private const float RoadThickness   = 0.25f;

        // Fence - now timber palings rather than metal bar, same footprint and height
        private const float FenceHeight  = 1.20f;
        private const float PostSpacing  = 2.50f;
        private const float PostSize     = 0.09f;
        private const float RailSize     = 0.07f;
        private const float PalingWidth  = 0.14f;
        private const float PalingGap    = 0.035f;
        private const float ColliderThickness = 0.20f;

        // Dressing
        private const int   PropSeed      = 20260826; // change for a different scatter
        private const float KerbWidth     = 0.16f;
        private const float KerbHeight    = 0.10f;
        private const float SlabSize      = 1.10f;    // paving slab pitch
        private const float SlabGap       = 0.045f;   // joint width between slabs
        private const float LampHeight    = 3.20f;
        private const float StreetLampHeight = 5.40f; // taller, along the road
        private const float HedgeHeight   = 1.55f;

        private const float TreeDensity  = 0.075f;    // props per square metre of grass
        private const float ShrubDensity = 0.120f;
        private const float RockDensity  = 0.055f;
        private const float TuftDensity  = 0.650f;

        // Grass footprint rectangles: (xMin, zMin, xMax, zMax)   [UNCHANGED]
        private static readonly Vector4[] GrassRects =
        {
            new Vector4( -4f, -10f,   4f,  35f),  // main corridor + start + north arm
            new Vector4(-15f,  16f,  -4f,  24f),  // west arm
            new Vector4(  4f,  13f,  21f,  27f),  // east arm (widened for the zigzag)
            new Vector4( 21f,  12f,  34f,  28f),  // end zone
        };

        // Stone paving rectangles: (xMin, zMin, xMax, zMax)      [UNCHANGED]
        private static readonly Vector4[] PathRects =
        {
            new Vector4(-2.5f, -6f,  2.5f, -1f),  // start pad, 5 x 5
            new Vector4(-1f,   -1f,  1f,   31f),  // main path + intersection + north arm
            new Vector4(-11f,  19f, -1f,   21f),  // west arm, 10 m
            new Vector4( 1f,   19f,  7f,   21f),  // ez1: leaving the intersection
            new Vector4( 5f,   19f,  7f,   24f),  // ez2: bend north
            new Vector4( 5f,   22f, 12f,   24f),  // ez3: east along the top
            new Vector4(10f,   16f, 12f,   24f),  // ez4: bend south, crossing back down
            new Vector4(10f,   16f, 17f,   18f),  // ez5: east along the bottom
            new Vector4(15f,   16f, 17f,   21f),  // ez6: bend back to centre
            new Vector4(15f,   19f, 21f,   21f),  // ez7: into the end zone
            new Vector4(21f,   15f, 31f,   25f),  // end zone paving, 10 x 10
        };

        // Fence perimeter, walked as a closed loop.                [UNCHANGED]
        private static readonly Vector2[] FencePerimeter =
        {
            new Vector2( -4f, -10f), new Vector2(  4f, -10f),
            new Vector2(  4f,  13f), new Vector2( 21f,  13f),
            new Vector2( 21f,  12f), new Vector2( 34f,  12f),
            new Vector2( 34f,  28f), new Vector2( 21f,  28f),
            new Vector2( 21f,  27f), new Vector2(  4f,  27f),
            new Vector2(  4f,  35f), new Vector2( -4f,  35f),
            new Vector2( -4f,  24f), new Vector2(-15f,  24f),
            new Vector2(-15f,  16f), new Vector2( -4f,  16f),
        };

        // ------------------------------------------------------------ NEW: street
        // One large ground slab under everything, so the world does not end at the fence.
        private static readonly Vector4 OuterGround = new Vector4(-34f, -30f, 54f, 50f);

        // Asphalt: (xMin, zMin, xMax, zMax)
        private static readonly Vector4[] RoadRects =
        {
            new Vector4( 37f, -28f, 45f, 46f),   // east road, the one the end zone faces
            new Vector4(-30f, -22f, 45f, -15f),  // south road, behind the start pad
        };

        // Centre-line dashes: (xMin, zMin, xMax, zMax) of the road, plus axis flag in W==1 for X-run
        private static readonly Vector4[] RoadCentreLines =
        {
            new Vector4( 41f, -28f, 41f,  46f),  // runs along Z
            new Vector4(-30f, -18.5f, 45f, -18.5f), // runs along X
        };

        // Public footpath on the far side of each road: (xMin, zMin, xMax, zMax)
        private static readonly Vector4[] StreetFootpaths =
        {
            new Vector4( 45f, -28f, 47.2f, 46f),
            new Vector4(-30f, -17.2f, 45f, -15f),
        };

        // House frontage lines: (x0, z0, x1, z1). Houses face the park side of the line.
        private static readonly Vector4[] HouseLines =
        {
            new Vector4( 51f, -24f, 51f,  44f),   // across the east road
            new Vector4(-28f, -25f, 44f, -25f),   // across the south road
            new Vector4(-23f,   8f, -23f, 34f),   // behind the west arm
            new Vector4(-14f,  41f,  30f, 41f),   // behind the north arm
        };

        // Street tree lines along the verge between fence and road: (x0, z0, x1, z1)
        private static readonly Vector4[] StreetTreeLines =
        {
            new Vector4( 35.5f, -12f, 35.5f, 40f),
            new Vector4(-12f, -12.5f, 34f, -12.5f),
        };

        // Parked cars: (x, z, yawDegrees, variant)
        private static readonly Vector4[] ParkedCars =
        {
            new Vector4(38.6f,   2f, 0f, 0f),
            new Vector4(38.6f,   8f, 0f, 1f),
            new Vector4(38.6f,  22f, 0f, 2f),
            new Vector4(38.6f,  33f, 0f, 0f),
            new Vector4(43.4f, -4f, 180f, 1f),
            new Vector4(43.4f, 16f, 180f, 2f),
            new Vector4(  6f, -16.6f,  90f, 0f),
            new Vector4( 18f, -16.6f,  90f, 2f),
        };

        // Landmarks at the decision points: (x, z, yawDegrees, kind)
        // kind: 0 bus shelter, 1 noticeboard / park map, 2 bench + bin, 3 postbox
        private static readonly Vector4[] Landmarks =
        {
            new Vector4( 32.5f, 20.0f,  90f, 0f),   // end zone, facing the road
            new Vector4( -2.3f, 17.4f,  40f, 1f),   // approaching the intersection
            new Vector4( -6.0f, 22.4f, 180f, 2f),   // west arm
            new Vector4(  2.1f, 28.5f, 180f, 3f),   // north arm
        };

        // Kerb runs laid just outside the paving: (x0, z0, x1, z1).   [UNCHANGED]
        private static readonly Vector4[] KerbLines =
        {
            // start pad
            new Vector4(-2.58f, -6.00f, -2.58f, -1.00f),
            new Vector4( 2.58f, -6.00f,  2.58f, -1.00f),
            new Vector4(-2.58f, -6.08f,  2.58f, -6.08f),
            new Vector4(-2.58f, -0.92f, -1.08f, -0.92f),
            new Vector4( 1.08f, -0.92f,  2.58f, -0.92f),
            // main path, broken either side of the intersection
            new Vector4(-1.08f, -1.00f, -1.08f, 19.00f),
            new Vector4( 1.08f, -1.00f,  1.08f, 19.00f),
            new Vector4(-1.08f, 21.00f, -1.08f, 31.00f),
            new Vector4( 1.08f, 21.00f,  1.08f, 31.00f),
            new Vector4(-1.08f, 31.08f,  1.08f, 31.08f),   // north dead end cap
            // west arm
            new Vector4(-11.00f, 18.92f, -1.08f, 18.92f),
            new Vector4(-11.00f, 21.08f, -1.08f, 21.08f),
            new Vector4(-11.08f, 18.92f, -11.08f, 21.08f), // west dead end cap
            // east arm (zigzag)
            new Vector4( 1.00f, 18.92f,  7.00f, 18.92f), new Vector4( 1.00f, 21.08f,  7.00f, 21.08f),
            new Vector4( 4.92f, 19.00f,  4.92f, 24.00f), new Vector4( 7.08f, 19.00f,  7.08f, 24.00f),
            new Vector4( 5.00f, 21.92f, 12.00f, 21.92f), new Vector4( 5.00f, 24.08f, 12.00f, 24.08f),
            new Vector4( 9.92f, 16.00f,  9.92f, 24.00f), new Vector4(12.08f, 16.00f, 12.08f, 24.00f),
            new Vector4(10.00f, 15.92f, 17.00f, 15.92f), new Vector4(10.00f, 18.08f, 17.00f, 18.08f),
            new Vector4(14.92f, 16.00f, 14.92f, 21.00f), new Vector4(17.08f, 16.00f, 17.08f, 21.00f),
            new Vector4(15.00f, 18.92f, 21.00f, 18.92f), new Vector4(15.00f, 21.08f, 21.00f, 21.08f),
            // end zone, broken where the east arm enters
            new Vector4(20.92f, 15.00f, 20.92f, 19.00f),
            new Vector4(20.92f, 21.00f, 20.92f, 25.00f),
            new Vector4(21.00f, 14.92f, 31.00f, 14.92f),
            new Vector4(21.00f, 25.08f, 31.00f, 25.08f),
            new Vector4(31.08f, 14.92f, 31.08f, 25.08f),
        };

        // Lamp posts: (x, z, armDirX, armDirZ) - the arm points toward the path. [UNCHANGED]
        private static readonly Vector4[] LampPosts =
        {
            new Vector4( 1.6f,  5.0f, -1f,  0f),
            new Vector4( 1.6f, 27.0f, -1f,  0f),
            new Vector4( 1.8f, 21.8f, -1f, -1f),
            new Vector4(-1.8f, 18.2f,  1f,  1f),
            new Vector4(-6.0f, 21.8f,  0f, -1f),
            new Vector4( 6.0f, 25.5f,  0f, -1f),
            new Vector4(13.5f, 14.5f,  0f,  1f),
            new Vector4(18.0f, 22.5f,  0f, -1f),
            new Vector4(22.5f, 26.5f,  0f, -1f),
            new Vector4(26.0f, 13.5f,  0f,  1f),
        };

        // Street lights along the road verge: (x, z, armDirX, armDirZ)
        private static readonly Vector4[] StreetLamps =
        {
            new Vector4(35.6f,  -6f,  1f, 0f),
            new Vector4(35.6f,  10f,  1f, 0f),
            new Vector4(35.6f,  26f,  1f, 0f),
            new Vector4(35.6f,  40f,  1f, 0f),
            new Vector4( -2f, -13.2f, 0f, -1f),
            new Vector4( 14f, -13.2f, 0f, -1f),
            new Vector4( 30f, -13.2f, 0f, -1f),
        };

        private const float CheckpointHeight = 3f;

        #endregion

        // ------------------------------------------------------------ menu items
        private static bool IncludeLandmarks
        {
            get { return EditorPrefs.GetBool(LandmarkPrefKey, true); }
            set { EditorPrefs.SetBool(LandmarkPrefKey, value); }
        }

        [MenuItem("Tools/VR Tutorial/Build Tutorial Scene (current scene)", false, 0)]
        public static void BuildInCurrentScene()
        {
            Build();
        }

        [MenuItem("Tools/VR Tutorial/Build Tutorial Scene (new scene)", false, 1)]
        public static void BuildInNewScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            Build();

            EnsureFolder(SceneFolder);
            string path = AssetDatabase.GenerateUniqueAssetPath(SceneFolder + "/NavigationTutorial.unity");
            EditorSceneManager.SaveScene(scene, path);
            Debug.Log("[VRTutorial] Scene saved to " + path);
        }

        /// <summary>
        /// Landmarks make the three arms visually distinguishable, which makes wayfinding
        /// easier. That is an independent variable in a navigation study, so it is a toggle
        /// rather than a fixed part of the build. Rebuild after changing it.
        /// </summary>
        [MenuItem("Tools/VR Tutorial/Options/Include Landmarks", false, 10)]
        public static void ToggleLandmarks()
        {
            IncludeLandmarks = !IncludeLandmarks;
            Debug.Log("[VRTutorial] Landmarks " + (IncludeLandmarks ? "ON" : "OFF") + " - rebuild to apply.");
        }

        [MenuItem("Tools/VR Tutorial/Options/Include Landmarks", true)]
        public static bool ToggleLandmarksValidate()
        {
            Menu.SetChecked("Tools/VR Tutorial/Options/Include Landmarks", IncludeLandmarks);
            return true;
        }

        [MenuItem("Tools/VR Tutorial/Clear Tutorial Scene", false, 20)]
        public static void Clear()
        {
            var existing = GameObject.Find(RootName);
            if (existing == null)
            {
                Debug.Log("[VRTutorial] No '" + RootName + "' found in the open scene.");
                return;
            }
            Undo.DestroyObjectImmediate(existing);
        }

        // ----------------------------------------------------------------- build
        private static void Build()
        {
            EnsureFolder(MaterialFolder);
            EnsureFolder(GeneratedFolder);

            var old = GameObject.Find(RootName);
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Build Tutorial Scene");

            // Flat-colour materials. No textures exist in the project, so variation has to
            // come from geometry and from several near-identical materials per surface -
            // three slightly different greens read as a lawn, one reads as a billiard table.
            Material[] grassMats = Variants("M_Grass",  new Color(0.30f, 0.47f, 0.22f), 0.05f, 3, 0.045f);
            Material[] stoneMats = Variants("M_Stone",  new Color(0.66f, 0.65f, 0.62f), 0.10f, 3, 0.05f);
            Material   outerMat   = GetOrCreateMaterial("M_OuterGround", new Color(0.27f, 0.41f, 0.21f), 0.04f);
            Material   endZoneMat = GetOrCreateMaterial("M_EndZone",    new Color(0.45f, 0.58f, 0.72f), 0.15f);
            Material   metalMat   = GetOrCreateMaterial("M_FenceMetal", new Color(0.42f, 0.44f, 0.47f), 0.65f);
            Material   kerbMat    = GetOrCreateMaterial("M_PathEdge",   new Color(0.55f, 0.54f, 0.51f), 0.12f);
            Material[] foliageMats = Variants("M_Foliage", new Color(0.20f, 0.38f, 0.18f), 0.05f, 3, 0.055f);
            Material   flowerMat  = GetOrCreateMaterial("M_Flower",     new Color(0.86f, 0.80f, 0.42f), 0.08f);
            Material   stripeMat  = GetOrCreateMaterial("M_GrassStripe",new Color(0.35f, 0.53f, 0.25f), 0.05f);
            Material   tuftMat    = GetOrCreateMaterial("M_GrassTuft",  new Color(0.26f, 0.46f, 0.20f), 0.05f);
            Material   barkMat    = GetOrCreateMaterial("M_Bark",       new Color(0.34f, 0.26f, 0.19f), 0.05f);
            Material   rockMat    = GetOrCreateMaterial("M_Rock",       new Color(0.52f, 0.51f, 0.50f), 0.15f);
            Material   lampMat    = GetOrCreateMaterial("M_LampHead",   new Color(0.95f, 0.90f, 0.72f), 0.35f);
            Material   timberMat  = GetOrCreateMaterial("M_Timber",     new Color(0.52f, 0.42f, 0.31f), 0.08f);
            Material   asphaltMat = GetOrCreateMaterial("M_Asphalt",    new Color(0.17f, 0.17f, 0.18f), 0.18f);
            Material   lineMat    = GetOrCreateMaterial("M_RoadLine",   new Color(0.88f, 0.86f, 0.76f), 0.10f);
            Material   hedgeMat   = GetOrCreateMaterial("M_Hedge",      new Color(0.16f, 0.31f, 0.15f), 0.04f);
            Material   renderMat  = GetOrCreateMaterial("M_HouseWall",  new Color(0.78f, 0.74f, 0.67f), 0.08f);
            Material   roofMat    = GetOrCreateMaterial("M_HouseRoof",  new Color(0.36f, 0.31f, 0.30f), 0.10f);
            Material   glassMat   = GetOrCreateMaterial("M_Glass",      new Color(0.28f, 0.36f, 0.40f), 0.85f);
            Material   postboxMat = GetOrCreateMaterial("M_Postbox",    new Color(0.62f, 0.13f, 0.12f), 0.25f);
            Material[] carMats   = Variants("M_Car", new Color(0.60f, 0.62f, 0.65f), 0.55f, 3, 0.18f);

            BuildWorldSettings();
            BuildOuterGround(root.transform, outerMat);
            BuildRoads(root.transform, asphaltMat, lineMat);
            BuildGround(root.transform, grassMats);
            BuildPaths(root.transform, stoneMats[1], endZoneMat);
            BuildPaving(root.transform, stoneMats, endZoneMat);
            BuildPathDetail(root.transform, kerbMat);
            BuildStreetKerbs(root.transform, kerbMat);
            BuildFence(root.transform, timberMat, metalMat);
            BuildHedge(root.transform, hedgeMat);
            BuildLamps(root.transform, metalMat, lampMat);
            BuildStreetLamps(root.transform, metalMat, lampMat);
            BuildHouses(root.transform, renderMat, roofMat, glassMat);
            BuildParkedCars(root.transform, carMats, glassMat);
            BuildStreetTrees(root.transform, barkMat, foliageMats[1]);
            BuildLawnStripes(root.transform, stripeMat);
            BuildScatter(root.transform, barkMat, foliageMats, tuftMat, rockMat, flowerMat);

            if (IncludeLandmarks)
                BuildLandmarks(root.transform, metalMat, timberMat, glassMat, postboxMat, lineMat);

            BuildCheckpoints(root.transform);
            BuildSpawnAndLight(root.transform);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();

            Debug.Log("[VRTutorial] Suburban streetscape built. Route unchanged: start pad -> 20 m " +
                      "footpath -> intersection at (0, 20); north and west arms 10 m, east arm zigzags " +
                      "into a 10 x 10 m end zone at (26, 20) beside the road. Landmarks " +
                      (IncludeLandmarks ? "INCLUDED" : "OMITTED") +
                      ". Bake lighting (Window > Rendering > Lighting > Generate) for the intended look.");
        }

        // -------------------------------------------------------- world settings
        /// <summary>
        /// Sky, fog and sun. Cheapest immersion in the whole file - no geometry, and fog is
        /// what stops the world visibly ending at the far houses.
        /// </summary>
        private static void BuildWorldSettings()
        {
            Shader skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader != null)
            {
                string skyPath = MaterialFolder + "/M_Sky.mat";
                var sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
                if (sky == null)
                {
                    sky = new Material(skyShader);
                    sky.name = "M_Sky";
                    AssetDatabase.CreateAsset(sky, skyPath);
                }
                if (sky.HasProperty("_SunSize"))            sky.SetFloat("_SunSize", 0.035f);
                if (sky.HasProperty("_AtmosphereThickness")) sky.SetFloat("_AtmosphereThickness", 0.85f);
                if (sky.HasProperty("_SkyTint"))            sky.SetColor("_SkyTint", new Color(0.58f, 0.68f, 0.82f));
                if (sky.HasProperty("_GroundColor"))        sky.SetColor("_GroundColor", new Color(0.42f, 0.42f, 0.40f));
                if (sky.HasProperty("_Exposure"))           sky.SetFloat("_Exposure", 1.15f);
                EditorUtility.SetDirty(sky);
                RenderSettings.skybox = sky;
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor     = new Color(0.55f, 0.61f, 0.70f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.44f, 0.44f);
            RenderSettings.ambientGroundColor  = new Color(0.24f, 0.25f, 0.22f);

            // Linear fog, starting well beyond the fenced area so the walkable space stays
            // crisp and only the distant houses soften.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.72f, 0.78f, 0.85f);
            RenderSettings.fogStartDistance = 45f;
            RenderSettings.fogEndDistance = 130f;
        }

        // ---------------------------------------------------------------- ground
        private static void BuildOuterGround(Transform root, Material mat)
        {
            var group = NewGroup("Ground_Outer", root);
            CreateSlab("OuterGround", group, OuterGround.x, OuterGround.y, OuterGround.z, OuterGround.w,
                       OuterGroundTopY, 0.4f, mat, true);
        }

        private static void BuildGround(Transform root, Material[] grass)
        {
            var group = NewGroup("Ground_Grass", root);
            for (int i = 0; i < GrassRects.Length; i++)
            {
                var r = GrassRects[i];
                CreateSlab("Grass_" + i.ToString("00"), group, r.x, r.y, r.z, r.w,
                           0f, GroundThickness, grass[i % grass.Length], true);
            }
        }

        // ----------------------------------------------------------------- roads
        private static void BuildRoads(Transform root, Material asphalt, Material line)
        {
            var group = NewGroup("Roads", root);

            for (int i = 0; i < RoadRects.Length; i++)
            {
                var r = RoadRects[i];
                CreateSlab("Road_" + i.ToString("00"), group, r.x, r.y, r.z, r.w,
                           RoadTopY, RoadThickness, asphalt, true);
            }

            // Broken centre line. Dashes rather than a solid strip - a solid line reads as
            // a racetrack, dashes read as a suburban street.
            var batch = BeginBatch(group);
            foreach (var c in RoadCentreLines)
            {
                bool alongZ = Mathf.Abs(c.z - c.x) < 0.01f;
                float from = alongZ ? c.y : c.x;
                float to   = alongZ ? c.w : c.z;
                float fixedAxis = alongZ ? c.x : c.y;

                for (float t = from; t < to; t += 6f)
                {
                    float len = Mathf.Min(3f, to - t);
                    if (len < 0.5f) break;

                    Vector3 pos = alongZ
                        ? new Vector3(fixedAxis, RoadTopY + 0.006f, t + len * 0.5f)
                        : new Vector3(t + len * 0.5f, RoadTopY + 0.006f, fixedAxis);
                    Vector3 scale = alongZ
                        ? new Vector3(0.14f, 0.01f, len)
                        : new Vector3(len, 0.01f, 0.14f);

                    AddPrimitive(batch, PrimitiveType.Cube, pos, scale, Quaternion.identity);
                }
            }
            EndBatch(batch, group, "Road_Markings", "RoadMarkingMesh", line);

            // The footpath opposite, so the far side of the road is not bare ground.
            var farPath = NewGroup("StreetFootpaths", group);
            for (int i = 0; i < StreetFootpaths.Length; i++)
            {
                var r = StreetFootpaths[i];
                CreateSlab("StreetFootpath_" + i.ToString("00"), farPath, r.x, r.y, r.z, r.w,
                           PathTopY, PathThickness, GetOrCreateMaterial("M_Stone_1",
                           new Color(0.66f, 0.65f, 0.62f), 0.10f), false);
            }
        }

        /// <summary>Kerb along both sides of every road, so the asphalt has an edge.</summary>
        private static void BuildStreetKerbs(Transform root, Material kerbMat)
        {
            var group = NewGroup("StreetKerbs", root);
            var batch = BeginBatch(group);

            foreach (var r in RoadRects)
            {
                bool alongZ = (r.w - r.y) > (r.z - r.x);
                float length = alongZ ? (r.w - r.y) : (r.z - r.x);
                float mid    = alongZ ? (r.y + r.w) * 0.5f : (r.x + r.z) * 0.5f;

                float[] edges = alongZ ? new float[] { r.x, r.z } : new float[] { r.y, r.w };
                foreach (float e in edges)
                {
                    Vector3 pos = alongZ
                        ? new Vector3(e, KerbHeight * 0.35f, mid)
                        : new Vector3(mid, KerbHeight * 0.35f, e);
                    Vector3 scale = alongZ
                        ? new Vector3(0.22f, KerbHeight * 1.4f, length)
                        : new Vector3(length, KerbHeight * 1.4f, 0.22f);
                    AddPrimitive(batch, PrimitiveType.Cube, pos, scale, Quaternion.identity);
                }
            }

            EndBatch(batch, group, "StreetKerbs_Visual", "StreetKerbMesh", kerbMat);
        }

        // ----------------------------------------------------------------- paths
        /// <summary>The concrete body under the paving, so slab gaps never show through.</summary>
        private static void BuildPaths(Transform root, Material stone, Material endZone)
        {
            var group = NewGroup("Paths", root);
            string[] names =
            {
                "StartPad", "MainPath", "WestArm",
                "EastArm_1", "EastArm_2", "EastArm_3", "EastArm_4",
                "EastArm_5", "EastArm_6", "EastArm_7",
                "EndZonePaving"
            };

            for (int i = 0; i < PathRects.Length; i++)
            {
                var r = PathRects[i];
                Material m = (i == PathRects.Length - 1) ? endZone : stone;
                // Colliders ON, unlike the original. The paving is what the player actually
                // walks on, and a downward raycast needs to hit it to tell path from grass -
                // footstep sounds, surface logging, anything that asks "where am I standing".
                // The 2 cm lip is well inside the CharacterController's 0.3 m step offset.
                CreateSlab(names[i], group, r.x, r.y, r.z, r.w, PathTopY - 0.012f,
                           PathThickness, m, true);
            }
        }

        /// <summary>
        /// Individual paving slabs laid over the concrete body, each nudged a few millimetres
        /// and drawn from three near-identical greys. Replaces the original thin seam strips:
        /// a learner walking for the first time spends a lot of time looking down, and real
        /// slab edges do far more than painted-on joints.
        /// </summary>
        private static void BuildPaving(Transform root, Material[] stones, Material endZone)
        {
            var group = NewGroup("Paving", root);

            Random.State previous = Random.state;
            Random.InitState(PropSeed + 7);

            var batches = new Transform[stones.Length];
            for (int i = 0; i < stones.Length; i++) batches[i] = BeginBatch(group);
            var endBatch = BeginBatch(group);

            for (int i = 0; i < PathRects.Length; i++)
            {
                var r = PathRects[i];
                bool isEndZone = (i == PathRects.Length - 1);

                for (float x = r.x; x < r.z - 0.05f; x += SlabSize)
                {
                    for (float z = r.y; z < r.w - 0.05f; z += SlabSize)
                    {
                        float w = Mathf.Min(SlabSize, r.z - x) - SlabGap;
                        float d = Mathf.Min(SlabSize, r.w - z) - SlabGap;
                        if (w < 0.12f || d < 0.12f) continue;

                        float lift = Random.Range(-0.004f, 0.006f);
                        Transform target = isEndZone ? endBatch : batches[Random.Range(0, stones.Length)];

                        AddPrimitive(target, PrimitiveType.Cube,
                                     new Vector3(x + w * 0.5f + SlabGap * 0.5f,
                                                 PathTopY + lift,
                                                 z + d * 0.5f + SlabGap * 0.5f),
                                     new Vector3(w, 0.03f, d), Quaternion.identity);
                    }
                }
            }

            Random.state = previous;

            for (int i = 0; i < batches.Length; i++)
                EndBatch(batches[i], group, "Paving_" + i, "PavingMesh_" + i, stones[i]);
            EndBatch(endBatch, group, "Paving_EndZone", "PavingMeshEndZone", endZone);
        }

        // ----------------------------------------------------------- path detail
        private static void BuildPathDetail(Transform root, Material kerbMat)
        {
            var group = NewGroup("PathDetail", root);
            var batch = BeginBatch(group);

            // Kerb stones along the edge of every paved surface, now with a chamfered cap so
            // they catch the light along the top edge instead of reading as flat bars.
            foreach (var line in KerbLines)
            {
                Vector2 a = new Vector2(line.x, line.y);
                Vector2 b = new Vector2(line.z, line.w);
                Vector2 delta = b - a;
                float length = delta.magnitude;
                if (length < 0.01f) continue;

                Vector3 dir = new Vector3(delta.x, 0f, delta.y).normalized;
                Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
                Vector3 mid = new Vector3((a.x + b.x) * 0.5f, KerbHeight * 0.5f, (a.y + b.y) * 0.5f);

                AddPrimitive(batch, PrimitiveType.Cube, mid,
                             new Vector3(KerbWidth, KerbHeight, length + KerbWidth), rot);

                // chamfer: a narrower cap sitting proud of the kerb top
                AddPrimitive(batch, PrimitiveType.Cube,
                             mid + Vector3.up * (KerbHeight * 0.5f + 0.012f),
                             new Vector3(KerbWidth * 0.72f, 0.025f, length + KerbWidth), rot);
            }

            EndBatch(batch, group, "PathDetail", "PathDetailMesh", kerbMat);
        }

        // ----------------------------------------------------------------- fence
        /// <summary>
        /// Timber paling fence on the original perimeter and at the original height, so the
        /// colliders and the sense of enclosure are unchanged - only the read is different.
        /// A suburban paling fence says "someone's back boundary"; the metal bar rail said
        /// "municipal enclosure", which is a colder thing to be inside.
        /// </summary>
        private static void BuildFence(Transform root, Material timber, Material metal)
        {
            var group = NewGroup("Fence", root);
            var palingBatch = BeginBatch(group);
            var railBatch = BeginBatch(group);
            var colliderGroup = NewGroup("Fence_Colliders", group);

            for (int i = 0; i < FencePerimeter.Length; i++)
            {
                Vector2 a = FencePerimeter[i];
                Vector2 b = FencePerimeter[(i + 1) % FencePerimeter.Length];
                BuildFenceRunParts(palingBatch, railBatch, a, b);
                CreateFenceCollider(colliderGroup, "FenceCollider_" + i.ToString("00"), a, b);
            }

            EndBatch(palingBatch, group, "Fence_Palings", "FenceMesh", timber);
            EndBatch(railBatch, group, "Fence_Frame", "FenceFrameMesh", metal);
        }

        private static void BuildFenceRunParts(Transform palings, Transform frame, Vector2 a, Vector2 b)
        {
            Vector2 delta = b - a;
            float length = delta.magnitude;
            if (length < 0.01f) return;

            Vector3 dir = new Vector3(delta.x, 0f, delta.y).normalized;
            Vector3 start = new Vector3(a.x, 0f, a.y);
            Vector3 mid = start + dir * (length * 0.5f);
            Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);

            // Posts
            int postCount = Mathf.Max(2, Mathf.RoundToInt(length / PostSpacing) + 1);
            for (int i = 0; i < postCount; i++)
            {
                float t = length * i / (postCount - 1);
                AddPrimitive(frame, PrimitiveType.Cube,
                             start + dir * t + Vector3.up * (FenceHeight * 0.5f + 0.04f),
                             new Vector3(PostSize * 1.25f, FenceHeight + 0.08f, PostSize * 1.25f), rot);
            }

            // Two horizontal rails behind the palings
            float[] railHeights = { FenceHeight * 0.80f, FenceHeight * 0.30f };
            foreach (float h in railHeights)
                AddPrimitive(frame, PrimitiveType.Cube, mid + Vector3.up * h,
                             new Vector3(RailSize * 0.7f, RailSize, length), rot);

            // Palings: vertical boards with a small gap, each varied slightly in height so
            // the top line is not laser-straight.
            float pitch = PalingWidth + PalingGap;
            int palingCount = Mathf.Max(1, Mathf.FloorToInt(length / pitch));
            float spacing = length / palingCount;

            for (int i = 0; i < palingCount; i++)
            {
                float t = spacing * (i + 0.5f);
                float h = FenceHeight - Mathf.Abs(Mathf.Sin(i * 12.9898f) * 0.035f);
                AddPrimitive(palings, PrimitiveType.Cube,
                             start + dir * t + Vector3.up * (h * 0.5f),
                             new Vector3(0.035f, h, PalingWidth), rot);
            }
        }

        private static void CreateFenceCollider(Transform parent, string name, Vector2 a, Vector2 b)
        {
            Vector2 delta = b - a;
            float length = delta.magnitude;
            if (length < 0.01f) return;

            Vector3 dir = new Vector3(delta.x, 0f, delta.y).normalized;
            Vector3 mid = new Vector3((a.x + b.x) * 0.5f, FenceHeight * 0.5f, (a.y + b.y) * 0.5f);

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = mid;
            go.transform.localRotation = Quaternion.LookRotation(dir, Vector3.up);

            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(ColliderThickness, FenceHeight, length);
            go.isStatic = true;
        }

        /// <summary>
        /// A hedge just outside the fence. This is the single biggest fix for the old scene:
        /// a 1.2 m fence you can see straight over into empty ground is what breaks the
        /// illusion, and a mass of green behind it closes the world without walling it in.
        /// </summary>
        private static void BuildHedge(Transform root, Material hedgeMat)
        {
            var group = NewGroup("Hedge", root);
            var batch = BeginBatch(group);

            Random.State previous = Random.state;
            Random.InitState(PropSeed + 31);

            for (int i = 0; i < FencePerimeter.Length; i++)
            {
                Vector2 a = FencePerimeter[i];
                Vector2 b = FencePerimeter[(i + 1) % FencePerimeter.Length];
                Vector2 delta = b - a;
                float length = delta.magnitude;
                if (length < 0.6f) continue;

                Vector3 dir = new Vector3(delta.x, 0f, delta.y).normalized;
                // Outward normal: the perimeter is wound so that right of travel is outside.
                Vector3 outward = new Vector3(dir.z, 0f, -dir.x);
                Vector3 start = new Vector3(a.x, 0f, a.y);

                Quaternion runRot = Quaternion.LookRotation(dir, Vector3.up);
                int blocks = Mathf.Max(1, Mathf.FloorToInt(length / 1.1f));
                float step = length / blocks;

                for (int j = 0; j < blocks; j++)
                {
                    float t = step * (j + 0.5f);
                    float h = HedgeHeight + Random.Range(-0.12f, 0.16f);
                    float w = Random.Range(0.95f, 1.15f);
                    Vector3 at = start + dir * t + outward * 0.65f;

                    // Main body, kept below the finished height so the rounded top can sit on it.
                    AddPrimitive(batch, PrimitiveType.Cube,
                                 at + Vector3.up * ((h - 0.28f) * 0.5f),
                                 new Vector3(w, h - 0.28f, step * 1.08f), runRot);

                    // Rounded crown: overlapping spheres along the run, which is what turns a
                    // row of boxes into clipped planting. The flat-topped box was the problem.
                    int lumps = 2;
                    for (int k = 0; k < lumps; k++)
                    {
                        float lt = step * ((k + 0.5f) / lumps - 0.5f);
                        AddPrimitive(batch, PrimitiveType.Sphere,
                                     at + dir * lt + Vector3.up * (h - 0.30f + Random.Range(-0.04f, 0.05f)),
                                     new Vector3(w * Random.Range(0.98f, 1.12f), 0.62f,
                                                 step / lumps * 1.5f), runRot);
                    }

                    // A few sprigs breaking the silhouette, so the top line is not a ruled edge.
                    if (Random.Range(0f, 1f) < 0.4f)
                    {
                        AddPrimitive(batch, PrimitiveType.Sphere,
                                     at + dir * Random.Range(-step * 0.4f, step * 0.4f)
                                        + outward * Random.Range(-0.15f, 0.15f)
                                        + Vector3.up * (h + Random.Range(0.02f, 0.14f)),
                                     Vector3.one * Random.Range(0.22f, 0.38f), runRot);
                    }
                }
            }

            Random.state = previous;
            EndBatch(batch, group, "Hedge_Visual", "HedgeMesh", hedgeMat);
        }

        // ----------------------------------------------------------------- lamps
        private static void BuildLamps(Transform root, Material metal, Material lampMat)
        {
            var group = NewGroup("LampPosts", root);
            var postBatch = BeginBatch(group);
            var headBatch = BeginBatch(group);
            var colliderGroup = NewGroup("Lamp_Colliders", group);

            for (int i = 0; i < LampPosts.Length; i++)
            {
                var lamp = LampPosts[i];
                Vector3 basePos = new Vector3(lamp.x, 0f, lamp.y);
                Vector3 armDir = new Vector3(lamp.z, 0f, lamp.w).normalized;
                Quaternion rot = armDir.sqrMagnitude > 0.001f
                    ? Quaternion.LookRotation(armDir, Vector3.up)
                    : Quaternion.identity;

                AddPrimitive(postBatch, PrimitiveType.Cylinder,
                             basePos + Vector3.up * (LampHeight * 0.5f),
                             new Vector3(0.12f, LampHeight * 0.5f, 0.12f), Quaternion.identity);

                // wider footing, so the post meets the ground rather than growing out of it
                AddPrimitive(postBatch, PrimitiveType.Cylinder,
                             basePos + Vector3.up * 0.09f,
                             new Vector3(0.20f, 0.09f, 0.20f), Quaternion.identity);

                AddPrimitive(postBatch, PrimitiveType.Cube,
                             basePos + Vector3.up * (LampHeight - 0.08f) + armDir * 0.30f,
                             new Vector3(0.07f, 0.07f, 0.60f), rot);

                AddPrimitive(headBatch, PrimitiveType.Cube,
                             basePos + Vector3.up * (LampHeight - 0.18f) + armDir * 0.55f,
                             new Vector3(0.32f, 0.12f, 0.32f), rot);

                var col = new GameObject("LampCollider_" + i.ToString("00"));
                col.transform.SetParent(colliderGroup, false);
                col.transform.localPosition = basePos + Vector3.up * (LampHeight * 0.5f);
                var capsule = col.AddComponent<CapsuleCollider>();
                capsule.radius = 0.12f;
                capsule.height = LampHeight;
                col.isStatic = true;
            }

            EndBatch(postBatch, group, "Lamp_Posts", "LampPostMesh", metal);
            EndBatch(headBatch, group, "Lamp_Heads", "LampHeadMesh", lampMat);
        }

        /// <summary>Taller lights along the road, which give the streetscape its vertical rhythm.</summary>
        private static void BuildStreetLamps(Transform root, Material metal, Material lampMat)
        {
            var group = NewGroup("StreetLamps", root);
            var postBatch = BeginBatch(group);
            var headBatch = BeginBatch(group);

            foreach (var lamp in StreetLamps)
            {
                Vector3 basePos = new Vector3(lamp.x, 0f, lamp.y);
                Vector3 armDir = new Vector3(lamp.z, 0f, lamp.w).normalized;
                Quaternion rot = armDir.sqrMagnitude > 0.001f
                    ? Quaternion.LookRotation(armDir, Vector3.up)
                    : Quaternion.identity;

                AddPrimitive(postBatch, PrimitiveType.Cylinder,
                             basePos + Vector3.up * (StreetLampHeight * 0.5f),
                             new Vector3(0.17f, StreetLampHeight * 0.5f, 0.17f), Quaternion.identity);

                // gently curved arm, faked with three short segments
                for (int s = 0; s < 3; s++)
                {
                    float f = s / 2f;
                    AddPrimitive(postBatch, PrimitiveType.Cube,
                                 basePos + Vector3.up * (StreetLampHeight - 0.10f - f * 0.16f)
                                         + armDir * (0.35f + f * 0.55f),
                                 new Vector3(0.09f, 0.09f, 0.62f), rot);
                }

                AddPrimitive(headBatch, PrimitiveType.Cube,
                             basePos + Vector3.up * (StreetLampHeight - 0.44f) + armDir * 1.55f,
                             new Vector3(0.44f, 0.13f, 0.78f), rot);
            }

            EndBatch(postBatch, group, "StreetLamp_Posts", "StreetLampPostMesh", metal);
            EndBatch(headBatch, group, "StreetLamp_Heads", "StreetLampHeadMesh", lampMat);
        }

        // ---------------------------------------------------------------- houses
        /// <summary>
        /// Single-storey suburban houses along the far side of each road. Walls, a pitched
        /// roof with real eaves, fascia, a ridge cap, windows, a door, and sometimes a chimney,
        /// a porch or a garage.
        ///
        /// Roof geometry, since getting it wrong is very visible: each plane passes through the
        /// ridge and falls away at 'slope'. At horizontal distance u from the ridge its height
        /// is ridge - u * tan(slope), so at u = w/2 it meets the wall top exactly and anything
        /// past that is overhang. The slab is therefore rotated by the SLOPE, not by
        /// 90 - slope - the latter stands it almost upright.
        /// </summary>
        private static void BuildHouses(Transform root, Material wall, Material roof, Material glass)
        {
            var group = NewGroup("Houses", root);
            var wallBatch = BeginBatch(group);
            var roofBatch = BeginBatch(group);
            var glassBatch = BeginBatch(group);
            var trimBatch = BeginBatch(group);

            Random.State previous = Random.state;
            Random.InitState(PropSeed + 101);

            foreach (var lineDef in HouseLines)
            {
                Vector2 a = new Vector2(lineDef.x, lineDef.y);
                Vector2 b = new Vector2(lineDef.z, lineDef.w);
                Vector2 delta = b - a;
                float length = delta.magnitude;
                if (length < 6f) continue;

                Vector3 dir = new Vector3(delta.x, 0f, delta.y).normalized;
                Vector3 facing = new Vector3(-dir.z, 0f, dir.x);   // toward the park
                Vector3 start = new Vector3(a.x, 0f, a.y);

                int count = Mathf.Max(1, Mathf.FloorToInt(length / 13f));
                float step = length / count;

                for (int i = 0; i < count; i++)
                {
                    float t = step * (i + 0.5f) + Random.Range(-1.6f, 1.6f);
                    Vector3 frontage = start + dir * t;
                    AddHouse(wallBatch, roofBatch, glassBatch, trimBatch, frontage, facing);
                }
            }

            Random.state = previous;

            EndBatch(wallBatch, group, "House_Walls", "HouseWallMesh", wall);
            EndBatch(roofBatch, group, "House_Roofs", "HouseRoofMesh", roof);
            EndBatch(glassBatch, group, "House_Windows", "HouseWindowMesh", glass);
            EndBatch(trimBatch, group, "House_Trim", "HouseTrimMesh",
                     GetOrCreateMaterial("M_HouseTrim", new Color(0.90f, 0.89f, 0.86f), 0.10f));
        }

        private static void AddHouse(Transform wallBatch, Transform roofBatch, Transform glassBatch,
                                     Transform trimBatch, Vector3 frontage, Vector3 facing)
        {
            float w = Random.Range(8.5f, 11.5f);      // across the frontage
            float d = Random.Range(7.5f, 10f);        // back from the road
            float h = Random.Range(2.6f, 3.1f);       // wall height to the eaves
            float slope = Random.Range(22f, 30f);
            bool hipRoof = Random.Range(0f, 1f) < 0.45f;

            const float eaveX = 0.55f;                // overhang across the gable
            const float eaveZ = 0.45f;                // overhang front and back

            Quaternion rot = Quaternion.LookRotation(facing, Vector3.up);
            Vector3 side = Vector3.Cross(Vector3.up, facing).normalized;
            Vector3 centre = frontage - facing * (d * 0.5f);

            float tan = Mathf.Sin(slope * Mathf.Deg2Rad) / Mathf.Cos(slope * Mathf.Deg2Rad);
            float ridgeH = h + (w * 0.5f) * tan;

            // ---- walls
            AddPrimitive(wallBatch, PrimitiveType.Cube,
                         centre + Vector3.up * (h * 0.5f), new Vector3(w, h, d), rot);

            // ---- the two long roof planes
            float runX = w * 0.5f + eaveX;
            float slabX = runX / Mathf.Cos(slope * Mathf.Deg2Rad);
            for (int s = -1; s <= 1; s += 2)
            {
                float u = runX * 0.5f;                       // centre of the plane, from the ridge
                Vector3 pos = centre
                            + side * (s * u)
                            + Vector3.up * (ridgeH - u * tan);

                AddPrimitive(roofBatch, PrimitiveType.Cube, pos,
                             new Vector3(slabX, 0.16f, d + eaveZ * 2f),
                             rot * Quaternion.Euler(0f, 0f, -s * slope));
            }

            // ---- hipped ends, if this one has them
            if (hipRoof)
            {
                float runZ = d * 0.5f + eaveZ;
                float slabZ = runZ / Mathf.Cos(slope * Mathf.Deg2Rad);
                for (int s = -1; s <= 1; s += 2)
                {
                    float u = runZ * 0.5f;
                    Vector3 pos = centre
                                + facing * (s * u)
                                + Vector3.up * (ridgeH - u * tan);

                    AddPrimitive(roofBatch, PrimitiveType.Cube, pos,
                                 new Vector3(w + eaveX * 2f, 0.15f, slabZ),
                                 rot * Quaternion.Euler(s * slope, 0f, 0f));
                }
            }

            // ---- gable end infill
            // Without this the wall stops at the eaves and you see straight through the
            // triangle between the two roof planes - the open gap. Hip roofs close
            // themselves with their end planes, so they do not need it.
            if (!hipRoof)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    AddMesh(wallBatch, GableMesh,
                            centre + facing * (s * (d * 0.5f - 0.05f)) + Vector3.up * h,
                            new Vector3(w, ridgeH - h, 0.10f), rot);
                }
            }

            // ---- ridge cap
            AddPrimitive(roofBatch, PrimitiveType.Cube,
                         centre + Vector3.up * (ridgeH + 0.06f),
                         new Vector3(0.34f, 0.16f, hipRoof ? d * 0.55f : d + eaveZ * 2f + 0.1f), rot);

            // ---- fascia along both eaves: the shadow line that makes a roof read as a roof
            for (int s = -1; s <= 1; s += 2)
            {
                AddPrimitive(trimBatch, PrimitiveType.Cube,
                             centre + side * (s * (runX - 0.02f)) + Vector3.up * (h - runX * tan + 0.06f),
                             new Vector3(0.10f, 0.22f, d + eaveZ * 2f), rot);
            }

            // ---- chimney, on about half of them
            if (Random.Range(0f, 1f) < 0.5f)
            {
                float cx = Random.Range(0.18f, 0.32f) * w * (Random.Range(0, 2) == 0 ? 1f : -1f);
                float roofYAtC = ridgeH - Mathf.Abs(cx) * tan;
                float ch = Random.Range(0.9f, 1.4f);
                AddPrimitive(wallBatch, PrimitiveType.Cube,
                             centre + side * cx + facing * Random.Range(-1.5f, 1.5f)
                                    + Vector3.up * (roofYAtC + ch * 0.5f - 0.2f),
                             new Vector3(0.75f, ch, 0.75f), rot);
                AddPrimitive(trimBatch, PrimitiveType.Cube,
                             centre + side * cx + facing * Random.Range(-1.5f, 1.5f)
                                    + Vector3.up * (roofYAtC + ch - 0.16f),
                             new Vector3(0.92f, 0.12f, 0.92f), rot);
            }

            // ---- front face: door plus two windows
            Vector3 front = centre + facing * (d * 0.5f + 0.04f);

            AddPrimitive(trimBatch, PrimitiveType.Cube,
                         front + side * (w * 0.02f) + Vector3.up * 1.05f,
                         new Vector3(1.0f, 2.1f, 0.07f), rot);
            AddPrimitive(glassBatch, PrimitiveType.Cube,
                         front + side * (w * 0.02f) + Vector3.up * 1.72f,
                         new Vector3(0.62f, 0.5f, 0.05f), rot);

            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 wp = front + side * (s * w * 0.28f) + Vector3.up * (h * 0.6f);
                AddPrimitive(trimBatch, PrimitiveType.Cube, wp,
                             new Vector3(w * 0.25f + 0.16f, h * 0.34f + 0.16f, 0.05f), rot);
                AddPrimitive(glassBatch, PrimitiveType.Cube, wp + facing * 0.02f,
                             new Vector3(w * 0.25f, h * 0.34f, 0.05f), rot);
            }

            // ---- porch over the door, on some
            if (Random.Range(0f, 1f) < 0.55f)
            {
                float pd = 1.5f;
                AddPrimitive(roofBatch, PrimitiveType.Cube,
                             front + facing * (pd * 0.5f) + Vector3.up * (h - 0.30f),
                             new Vector3(w * 0.42f, 0.14f, pd),
                             rot * Quaternion.Euler(8f, 0f, 0f));

                for (int s = -1; s <= 1; s += 2)
                {
                    AddPrimitive(trimBatch, PrimitiveType.Cube,
                                 front + facing * (pd - 0.1f) + side * (s * w * 0.19f)
                                       + Vector3.up * ((h - 0.35f) * 0.5f),
                                 new Vector3(0.13f, h - 0.35f, 0.13f), rot);
                }
            }

            // ---- garage to one side, on some
            if (Random.Range(0f, 1f) < 0.45f)
            {
                float gw = 3.2f, gd = 5.5f, gh = 2.5f;
                float gs = (Random.Range(0, 2) == 0 ? 1f : -1f);
                Vector3 gCentre = centre + side * (gs * (w * 0.5f + gw * 0.5f - 0.15f))
                                         - facing * (d * 0.5f - gd * 0.5f);

                AddPrimitive(wallBatch, PrimitiveType.Cube,
                             gCentre + Vector3.up * (gh * 0.5f), new Vector3(gw, gh, gd), rot);

                float gRidge = gh + (gw * 0.5f) * tan;
                float gRun = gw * 0.5f + 0.3f;
                float gSlab = gRun / Mathf.Cos(slope * Mathf.Deg2Rad);
                for (int s = -1; s <= 1; s += 2)
                {
                    float u = gRun * 0.5f;
                    AddPrimitive(roofBatch, PrimitiveType.Cube,
                                 gCentre + side * (s * u) + Vector3.up * (gRidge - u * tan),
                                 new Vector3(gSlab, 0.14f, gd + 0.5f),
                                 rot * Quaternion.Euler(0f, 0f, -s * slope));
                }

                AddPrimitive(trimBatch, PrimitiveType.Cube,
                             gCentre + facing * (gd * 0.5f + 0.04f) + Vector3.up * (gh * 0.42f),
                             new Vector3(gw * 0.82f, gh * 0.72f, 0.07f), rot);
            }
        }

        /// <summary>
        /// Parked cars. Static and empty on purpose - nothing in this scene moves. Motion in
        /// peripheral vision is both a nausea risk and, for a cohort recruited for travel
        /// anxiety, an unnecessary stressor.
        /// </summary>
        private static void BuildParkedCars(Transform root, Material[] bodyMats, Material glass)
        {
            var group = NewGroup("ParkedCars", root);

            var bodyBatches = new Transform[bodyMats.Length];
            for (int i = 0; i < bodyMats.Length; i++) bodyBatches[i] = BeginBatch(group);
            var glassBatch = BeginBatch(group);
            var wheelBatch = BeginBatch(group);

            foreach (var c in ParkedCars)
            {
                int variant = Mathf.Clamp(Mathf.RoundToInt(c.w), 0, bodyMats.Length - 1);
                Transform body = bodyBatches[variant];
                Quaternion rot = Quaternion.Euler(0f, c.z, 0f);
                Vector3 basePos = new Vector3(c.x, 0f, c.y);

                AddPrimitive(body, PrimitiveType.Cube, basePos + Vector3.up * 0.62f,
                             new Vector3(1.78f, 0.62f, 4.35f), rot);
                AddPrimitive(body, PrimitiveType.Cube, basePos + Vector3.up * 1.14f,
                             new Vector3(1.62f, 0.52f, 2.35f), rot);
                AddPrimitive(glassBatch, PrimitiveType.Cube, basePos + Vector3.up * 1.16f,
                             new Vector3(1.66f, 0.40f, 2.20f), rot);

                for (int sx = -1; sx <= 1; sx += 2)
                {
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        Vector3 offset = rot * new Vector3(sx * 0.86f, 0.32f, sz * 1.42f);
                        AddPrimitive(wheelBatch, PrimitiveType.Cylinder, basePos + offset,
                                     new Vector3(0.32f, 0.10f, 0.32f),
                                     rot * Quaternion.Euler(0f, 0f, 90f));
                    }
                }
            }

            for (int i = 0; i < bodyBatches.Length; i++)
                EndBatch(bodyBatches[i], group, "Car_Bodies_" + i, "CarBodyMesh_" + i, bodyMats[i]);
            EndBatch(glassBatch, group, "Car_Glass", "CarGlassMesh", glass);
            EndBatch(wheelBatch, group, "Car_Wheels", "CarWheelMesh",
                     GetOrCreateMaterial("M_Tyre", new Color(0.10f, 0.10f, 0.11f), 0.10f));
        }

        // ----------------------------------------------------------------- trees
        /// <summary>
        /// A tree with a tapered trunk, a slight lean and several overlapping canopy blobs.
        /// The original single cylinder plus single sphere reads as a lollipop; the whole
        /// gain here is in the silhouette, which is all you get without textures.
        /// </summary>
        private static void AddTree(Transform barkBatch, Transform foliageBatch,
                                    Vector3 basePos, float height, float canopy)
        {
            float lean = Random.Range(0f, 5f);
            Quaternion leanRot = Quaternion.Euler(lean, Random.Range(0f, 360f), 0f);

            // Trunk in two tapering sections.
            AddPrimitive(barkBatch, PrimitiveType.Cylinder,
                         basePos + leanRot * new Vector3(0f, height * 0.26f, 0f),
                         new Vector3(0.30f, height * 0.26f, 0.30f), leanRot);
            AddPrimitive(barkBatch, PrimitiveType.Cylinder,
                         basePos + leanRot * new Vector3(0f, height * 0.68f, 0f),
                         new Vector3(0.20f, height * 0.30f, 0.20f), leanRot);

            // Two branches, which is what stops it reading as a post.
            for (int b = 0; b < 2; b++)
            {
                float yaw = Random.Range(0f, 360f);
                Quaternion br = Quaternion.Euler(0f, yaw, Random.Range(28f, 42f));
                AddPrimitive(barkBatch, PrimitiveType.Cylinder,
                             basePos + Vector3.up * (height * 0.78f) + br * new Vector3(0f, 0.42f, 0f),
                             new Vector3(0.10f, 0.45f, 0.10f), br);
            }

            // Canopy: one main mass plus three smaller lobes pushed out around it.
            Vector3 crown = basePos + leanRot * new Vector3(0f, height + canopy * 0.20f, 0f);
            AddPrimitive(foliageBatch, PrimitiveType.Sphere, crown,
                         new Vector3(canopy * 2.0f, canopy * 1.55f, canopy * 2.0f),
                         Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));

            for (int i = 0; i < 3; i++)
            {
                float ang = Random.Range(0f, 360f);
                Vector3 off = Quaternion.Euler(0f, ang, 0f) * new Vector3(canopy * 0.62f, 0f, 0f);
                AddPrimitive(foliageBatch, PrimitiveType.Sphere,
                             crown + off + Vector3.up * Random.Range(-0.35f, 0.30f),
                             Vector3.one * (canopy * Random.Range(0.95f, 1.35f)),
                             Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            }
        }

        private static void BuildStreetTrees(Transform root, Material bark, Material foliage)
        {
            var group = NewGroup("StreetTrees", root);
            var barkBatch = BeginBatch(group);
            var foliageBatch = BeginBatch(group);
            var colliders = NewGroup("StreetTree_Colliders", group);

            Random.State previous = Random.state;
            Random.InitState(PropSeed + 211);

            int index = 0;
            foreach (var lineDef in StreetTreeLines)
            {
                Vector2 a = new Vector2(lineDef.x, lineDef.y);
                Vector2 b = new Vector2(lineDef.z, lineDef.w);
                Vector2 delta = b - a;
                float length = delta.magnitude;
                if (length < 4f) continue;

                Vector3 dir = new Vector3(delta.x, 0f, delta.y).normalized;
                Vector3 start = new Vector3(a.x, 0f, a.y);

                int count = Mathf.Max(1, Mathf.FloorToInt(length / 9f));
                float step = length / count;

                for (int i = 0; i < count; i++)
                {
                    Vector3 p = start + dir * (step * (i + 0.5f) + Random.Range(-0.8f, 0.8f));
                    float height = Random.Range(4.2f, 5.6f);
                    float canopy = Random.Range(1.5f, 2.1f);
                    AddTree(barkBatch, foliageBatch, p, height, canopy);

                    var col = new GameObject("StreetTreeCollider_" + index.ToString("00"));
                    col.transform.SetParent(colliders, false);
                    col.transform.localPosition = p + Vector3.up * (height * 0.5f);
                    var capsule = col.AddComponent<CapsuleCollider>();
                    capsule.radius = 0.26f;
                    capsule.height = height;
                    col.isStatic = true;
                    index++;
                }
            }

            Random.state = previous;
            EndBatch(barkBatch, group, "StreetTree_Trunks", "StreetTreeTrunkMesh", bark);
            EndBatch(foliageBatch, group, "StreetTree_Foliage", "StreetTreeFoliageMesh", foliage);
        }

        // --------------------------------------------------------------- scatter
        private static void BuildScatter(Transform root, Material bark, Material[] foliages,
                                         Material tuft, Material rock, Material flowerMat)
        {
            var group = NewGroup("Scatter", root);

            Random.State previous = Random.state;
            Random.InitState(PropSeed);

            var barkBatch = BeginBatch(group);
            var foliageBatches = new Transform[foliages.Length];
            for (int i = 0; i < foliages.Length; i++) foliageBatches[i] = BeginBatch(group);
            var flowerBatch = BeginBatch(group);
            var tuftBatch    = BeginBatch(group);
            var rockBatch    = BeginBatch(group);
            var trunkColliders = NewGroup("Tree_Colliders", group);

            int treeIndex = 0;
            foreach (Vector2 p in ScatterPoints(TreeDensity, 1.5f, 0.8f))
            {
                float height = Random.Range(2.8f, 4.2f);
                float canopy = Random.Range(1.1f, 1.7f);

                AddTree(barkBatch, foliageBatches[Random.Range(0, foliageBatches.Length)],
                        new Vector3(p.x, 0f, p.y), height, canopy);

                // Mulch ring, so the trunk meets the lawn instead of spearing through it.
                AddPrimitive(barkBatch, PrimitiveType.Cylinder,
                             new Vector3(p.x, 0.011f, p.y),
                             new Vector3(Random.Range(1.25f, 1.75f), 0.011f, Random.Range(1.25f, 1.75f)),
                             Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));

                var col = new GameObject("TreeCollider_" + treeIndex.ToString("00"));
                col.transform.SetParent(trunkColliders, false);
                col.transform.localPosition = new Vector3(p.x, height * 0.5f, p.y);
                var capsule = col.AddComponent<CapsuleCollider>();
                capsule.radius = 0.22f;
                capsule.height = height;
                col.isStatic = true;
                treeIndex++;
            }

            // Shrubs - a mound of overlapping spheres of falling size, drawn from three
            // greens so a bed does not read as one moulded lump. Roughly one in six flowers.
            foreach (Vector2 p in ScatterPoints(ShrubDensity, 0.6f, 0.5f))
            {
                Transform bush = foliageBatches[Random.Range(0, foliageBatches.Length)];
                float scaleBase = Random.Range(0.75f, 1.35f);
                int lobes = Random.Range(4, 7);

                for (int i = 0; i < lobes; i++)
                {
                    float drop = 1f - (i / (float)lobes) * 0.45f;      // smaller toward the top
                    float s = Random.Range(0.34f, 0.62f) * scaleBase * drop;
                    Vector2 offset = Random.insideUnitCircle * 0.30f * scaleBase;
                    float y = (0.16f + (i / (float)lobes) * 0.42f) * scaleBase;

                    AddPrimitive(bush, PrimitiveType.Sphere,
                                 new Vector3(p.x + offset.x, y, p.y + offset.y),
                                 new Vector3(s, s * Random.Range(0.75f, 0.95f), s),
                                 Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                }

                if (Random.Range(0f, 1f) < 0.17f)
                {
                    int flowers = Random.Range(4, 8);
                    for (int i = 0; i < flowers; i++)
                    {
                        Vector2 offset = Random.insideUnitCircle * 0.34f * scaleBase;
                        AddPrimitive(flowerBatch, PrimitiveType.Sphere,
                                     new Vector3(p.x + offset.x,
                                                 Random.Range(0.35f, 0.66f) * scaleBase,
                                                 p.y + offset.y),
                                     Vector3.one * Random.Range(0.07f, 0.13f), Quaternion.identity);
                    }
                }
            }

            foreach (Vector2 p in ScatterPoints(RockDensity, 0.5f, 0.4f))
            {
                float s = Random.Range(0.18f, 0.45f);
                AddPrimitive(rockBatch, PrimitiveType.Cube,
                             new Vector3(p.x, s * 0.25f, p.y),
                             new Vector3(s, s * Random.Range(0.6f, 1.0f), s * Random.Range(0.7f, 1.3f)),
                             Quaternion.Euler(Random.Range(-25f, 25f), Random.Range(0f, 360f), Random.Range(-25f, 25f)));
            }

            // Tufts - three thin crossed blades per clump rather than one fat box. A single
            // cube reads as a brick from any angle; crossed blades read as grass from most.
            foreach (Vector2 p in ScatterPoints(TuftDensity, 0.35f, 0.3f))
            {
                float height = Random.Range(0.16f, 0.34f);
                float yaw = Random.Range(0f, 360f);

                for (int i = 0; i < 3; i++)
                {
                    AddPrimitive(tuftBatch, PrimitiveType.Cube,
                                 new Vector3(p.x + Random.Range(-0.04f, 0.04f),
                                             height * 0.45f,
                                             p.y + Random.Range(-0.04f, 0.04f)),
                                 new Vector3(Random.Range(0.10f, 0.16f), height, 0.012f),
                                 Quaternion.Euler(Random.Range(-14f, 14f),
                                                  yaw + i * 60f,
                                                  Random.Range(-14f, 14f)));
                }
            }

            Random.state = previous;

            EndBatch(barkBatch, group, "Tree_Trunks", "TreeTrunkMesh", bark);
            for (int i = 0; i < foliageBatches.Length; i++)
                EndBatch(foliageBatches[i], group, "Foliage_" + i, "FoliageMesh_" + i, foliages[i]);
            EndBatch(flowerBatch, group, "Flowers",    "FlowerMesh",    flowerMat);
            EndBatch(tuftBatch,   group, "GrassTufts", "GrassTuftMesh", tuft);
            EndBatch(rockBatch,   group, "Rocks",      "RockMesh",      rock);
        }

        /// <summary>Random points on the grass, rejected if too close to paving or fence.</summary>
        private static List<Vector2> ScatterPoints(float density, float pathMargin, float fenceInset)
        {
            var points = new List<Vector2>();

            foreach (var g in GrassRects)
            {
                float width = g.z - g.x;
                float depth = g.w - g.y;
                int samples = Mathf.RoundToInt(width * depth * density);

                for (int i = 0; i < samples; i++)
                {
                    var p = new Vector2(
                        Random.Range(g.x + fenceInset, g.z - fenceInset),
                        Random.Range(g.y + fenceInset, g.w - fenceInset));

                    if (IsClearOfPaths(p, pathMargin) && IsClearOfLandmarks(p, 2.2f)) points.Add(p);
                }
            }
            return points;
        }

        private static bool IsClearOfPaths(Vector2 p, float margin)
        {
            foreach (var r in PathRects)
            {
                if (p.x > r.x - margin && p.x < r.z + margin &&
                    p.y > r.y - margin && p.y < r.w + margin) return false;
            }
            return true;
        }

        /// <summary>Keeps scatter off the landmarks, so a bench never grows a shrub through it.</summary>
        private static bool IsClearOfLandmarks(Vector2 p, float margin)
        {
            if (!IncludeLandmarks) return true;

            foreach (var l in Landmarks)
            {
                if (Vector2.Distance(p, new Vector2(l.x, l.y)) < margin) return false;
            }
            return true;
        }

        // ------------------------------------------------------------- landmarks
        /// <summary>
        /// One distinct object at each decision point. In a navigation study these are not
        /// decoration: they are the cues a participant uses to tell one arm from another.
        /// Toggle them off to force route memory instead.
        /// </summary>
        private static void BuildLandmarks(Transform root, Material metal, Material timber,
                                           Material glass, Material postbox, Material lineMat)
        {
            var group = NewGroup("Landmarks", root);
            var metalBatch = BeginBatch(group);
            var timberBatch = BeginBatch(group);
            var glassBatch = BeginBatch(group);
            var accentBatch = BeginBatch(group);
            var colliders = NewGroup("Landmark_Colliders", group);

            for (int i = 0; i < Landmarks.Length; i++)
            {
                var l = Landmarks[i];
                Vector3 pos = new Vector3(l.x, 0f, l.y);
                Quaternion rot = Quaternion.Euler(0f, l.z, 0f);
                int kind = Mathf.RoundToInt(l.w);

                switch (kind)
                {
                    case 0: AddBusShelter(metalBatch, glassBatch, timberBatch, pos, rot); break;
                    case 1: AddNoticeboard(timberBatch, accentBatch, pos, rot); break;
                    case 2: AddBenchAndBin(timberBatch, metalBatch, pos, rot); break;
                    default: AddPostbox(accentBatch, metalBatch, pos, rot); break;
                }

                AddLandmarkCollider(colliders, "LandmarkCollider_" + i.ToString("00"), pos, rot, kind);
            }

            EndBatch(metalBatch, group, "Landmark_Metal", "LandmarkMetalMesh", metal);
            EndBatch(timberBatch, group, "Landmark_Timber", "LandmarkTimberMesh", timber);
            EndBatch(glassBatch, group, "Landmark_Glass", "LandmarkGlassMesh", glass);
            EndBatch(accentBatch, group, "Landmark_Accent", "LandmarkAccentMesh", postbox);
        }

        private static void AddBusShelter(Transform metal, Transform glass, Transform timber,
                                          Vector3 pos, Quaternion rot)
        {
            const float w = 3.4f, d = 1.5f, h = 2.45f;

            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    AddPrimitive(metal, PrimitiveType.Cube,
                                 pos + rot * new Vector3(sx * (w * 0.5f - 0.06f), h * 0.5f, sz * (d * 0.5f - 0.06f)),
                                 new Vector3(0.10f, h, 0.10f), rot);
                }
            }

            AddPrimitive(metal, PrimitiveType.Cube, pos + rot * new Vector3(0f, h + 0.05f, 0f),
                         new Vector3(w + 0.30f, 0.11f, d + 0.45f), rot);

            // back wall and one end, glazed
            AddPrimitive(glass, PrimitiveType.Cube, pos + rot * new Vector3(0f, h * 0.55f, -d * 0.5f),
                         new Vector3(w - 0.12f, h * 0.85f, 0.05f), rot);
            AddPrimitive(glass, PrimitiveType.Cube, pos + rot * new Vector3(-w * 0.5f, h * 0.55f, 0f),
                         new Vector3(0.05f, h * 0.85f, d - 0.12f), rot);

            // bench inside
            AddPrimitive(timber, PrimitiveType.Cube, pos + rot * new Vector3(0f, 0.45f, -d * 0.28f),
                         new Vector3(w - 0.8f, 0.07f, 0.36f), rot);
            AddPrimitive(metal, PrimitiveType.Cube, pos + rot * new Vector3(0f, 0.22f, -d * 0.28f),
                         new Vector3(w - 1.4f, 0.44f, 0.07f), rot);

            // timetable panel on the end wall
            AddPrimitive(timber, PrimitiveType.Cube, pos + rot * new Vector3(w * 0.5f, 1.55f, 0f),
                         new Vector3(0.07f, 0.75f, 0.55f), rot);
        }

        private static void AddNoticeboard(Transform timber, Transform accent, Vector3 pos, Quaternion rot)
        {
            for (int s = -1; s <= 1; s += 2)
                AddPrimitive(timber, PrimitiveType.Cube, pos + rot * new Vector3(s * 0.62f, 0.85f, 0f),
                             new Vector3(0.10f, 1.70f, 0.10f), rot);

            AddPrimitive(timber, PrimitiveType.Cube, pos + rot * new Vector3(0f, 1.42f, 0f),
                         new Vector3(1.45f, 0.95f, 0.08f), rot);

            // the map face, in the accent colour so it stands out at a distance
            AddPrimitive(accent, PrimitiveType.Cube, pos + rot * new Vector3(0f, 1.42f, 0.055f),
                         new Vector3(1.28f, 0.80f, 0.02f), rot);

            AddPrimitive(timber, PrimitiveType.Cube, pos + rot * new Vector3(0f, 1.98f, 0.02f),
                         new Vector3(1.60f, 0.12f, 0.24f), rot * Quaternion.Euler(-18f, 0f, 0f));
        }

        private static void AddBenchAndBin(Transform timber, Transform metal, Vector3 pos, Quaternion rot)
        {
            // Bench
            AddPrimitive(timber, PrimitiveType.Cube, pos + rot * new Vector3(0f, 0.44f, 0f),
                         new Vector3(1.75f, 0.07f, 0.46f), rot);
            AddPrimitive(timber, PrimitiveType.Cube, pos + rot * new Vector3(0f, 0.72f, -0.22f),
                         new Vector3(1.75f, 0.36f, 0.06f), rot * Quaternion.Euler(-12f, 0f, 0f));

            for (int s = -1; s <= 1; s += 2)
            {
                AddPrimitive(metal, PrimitiveType.Cube, pos + rot * new Vector3(s * 0.75f, 0.22f, 0f),
                             new Vector3(0.08f, 0.44f, 0.42f), rot);
            }

            // Bin, a little to one side
            Vector3 binPos = pos + rot * new Vector3(1.55f, 0f, 0f);
            AddPrimitive(metal, PrimitiveType.Cylinder, binPos + Vector3.up * 0.42f,
                         new Vector3(0.46f, 0.42f, 0.46f), rot);
            AddPrimitive(metal, PrimitiveType.Cylinder, binPos + Vector3.up * 0.88f,
                         new Vector3(0.52f, 0.04f, 0.52f), rot);
        }

        private static void AddPostbox(Transform accent, Transform metal, Vector3 pos, Quaternion rot)
        {
            AddPrimitive(accent, PrimitiveType.Cylinder, pos + Vector3.up * 0.62f,
                         new Vector3(0.62f, 0.62f, 0.62f), rot);
            AddPrimitive(accent, PrimitiveType.Sphere, pos + Vector3.up * 1.24f,
                         new Vector3(0.62f, 0.30f, 0.62f), rot);
            AddPrimitive(metal, PrimitiveType.Cube, pos + rot * new Vector3(0f, 1.02f, 0.30f),
                         new Vector3(0.34f, 0.07f, 0.06f), rot);
            AddPrimitive(metal, PrimitiveType.Cylinder, pos + Vector3.up * 0.05f,
                         new Vector3(0.70f, 0.05f, 0.70f), rot);
        }

        private static void AddLandmarkCollider(Transform parent, string name, Vector3 pos,
                                                Quaternion rot, int kind)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localRotation = rot;

            var box = go.AddComponent<BoxCollider>();

            switch (kind)
            {
                case 0:   // bus shelter - blocks the back and ends, open at the front
                    go.transform.localPosition = pos + rot * new Vector3(0f, 1.2f, -0.6f);
                    box.size = new Vector3(3.6f, 2.4f, 0.4f);
                    break;
                case 1:   // noticeboard
                    go.transform.localPosition = pos + Vector3.up * 1.0f;
                    box.size = new Vector3(1.5f, 2.0f, 0.3f);
                    break;
                case 2:   // bench
                    go.transform.localPosition = pos + Vector3.up * 0.45f;
                    box.size = new Vector3(1.9f, 0.9f, 0.6f);
                    break;
                default:  // postbox
                    go.transform.localPosition = pos + Vector3.up * 0.65f;
                    box.size = new Vector3(0.7f, 1.3f, 0.7f);
                    break;
            }

            go.isStatic = true;
        }

        /// <summary>
        /// Mown stripes. Alternating bands a shade lighter than the lawn, laid a few
        /// millimetres above it. Almost free, and it is the difference between "a green
        /// plane" and "someone mows this".
        /// </summary>
        private static void BuildLawnStripes(Transform root, Material stripe)
        {
            var group = NewGroup("LawnStripes", root);
            var batch = BeginBatch(group);

            const float band = 1.7f;

            foreach (var g in GrassRects)
            {
                bool alongX = (g.z - g.x) >= (g.w - g.y);
                float from  = alongX ? g.y : g.x;
                float to    = alongX ? g.w : g.z;
                float span  = alongX ? (g.z - g.x) : (g.w - g.y);
                float mid   = alongX ? (g.x + g.z) * 0.5f : (g.y + g.w) * 0.5f;

                int i = 0;
                for (float u = from; u < to; u += band, i++)
                {
                    if (i % 2 == 1) continue;
                    float len = Mathf.Min(band, to - u);
                    if (len < 0.15f) continue;

                    // 6 mm proud of the grass, still under the path slabs so paving hides it.
                    Vector3 pos = alongX
                        ? new Vector3(mid, 0.003f, u + len * 0.5f)
                        : new Vector3(u + len * 0.5f, 0.003f, mid);
                    Vector3 scale = alongX
                        ? new Vector3(span, 0.006f, len)
                        : new Vector3(len, 0.006f, span);

                    AddPrimitive(batch, PrimitiveType.Cube, pos, scale, Quaternion.identity);
                }
            }

            EndBatch(batch, group, "Lawn_Stripes", "LawnStripeMesh", stripe);
        }

        // ----------------------------------------------------------- checkpoints
        private static void BuildCheckpoints(Transform root)
        {
            var group = NewGroup("Checkpoints", root);

            CreateCheckpoint(group, "CP_Start",        new Vector2(0f, -3.5f), new Vector2(5f, 5f),   false);
            CreateCheckpoint(group, "CP_Intersection", new Vector2(0f, 20f),   new Vector2(2f, 2f),   false);
            CreateCheckpoint(group, "CP_EndZone",      new Vector2(26f, 20f),  new Vector2(10f, 10f), true);
        }

        private static void CreateCheckpoint(Transform parent, string name, Vector2 centre, Vector2 size, bool isEndZone)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(centre.x, CheckpointHeight * 0.5f, centre.y);

            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(size.x, CheckpointHeight, size.y);

            // Checkpoint component omitted for now - these are empty marker volumes.
            // Add TutorialZoneTrigger by hand, or restore a Checkpoint component here.
        }

        private static void BuildSpawnAndLight(Transform root)
        {
            var spawn = new GameObject("PlayerSpawn");
            spawn.transform.SetParent(root, false);
            spawn.transform.localPosition = new Vector3(0f, 0f, -4.5f);
            spawn.transform.localRotation = Quaternion.identity; // facing +Z, down the path

            // SceneSpawnPoint lives in the runtime assembly, so it is added by name rather
            // than referenced directly - editor scripts cannot see it at compile time here
            // without an assembly definition.
            var spawnType = System.Type.GetType("SceneSpawnPoint, Assembly-CSharp");
            if (spawnType != null) spawn.AddComponent(spawnType);

            if (FindAnyLight() == null)
            {
                var lightGo = new GameObject("Directional Light");
                lightGo.transform.SetParent(root, false);
                lightGo.transform.rotation = Quaternion.Euler(46f, -38f, 0f);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.05f;
                light.color = new Color(1.00f, 0.96f, 0.89f);   // late-afternoon warmth
                light.shadows = LightShadows.Soft;
            }
        }

        private static Light FindAnyLight()
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindFirstObjectByType<Light>();
#else
            return Object.FindObjectOfType<Light>();
#endif
        }

        // --------------------------------------------------------------- batching
        private static Transform BeginBatch(Transform parent)
        {
            var temp = new GameObject("TEMP_Batch");
            temp.transform.SetParent(parent, false);
            temp.hideFlags = HideFlags.HideAndDontSave;
            return temp.transform;
        }

        /// <summary>Merges everything in the batch into one saved mesh and one renderer.</summary>
        private static void EndBatch(Transform batch, Transform parent, string objectName,
                                     string meshAssetName, Material material)
        {
            var filters = batch.GetComponentsInChildren<MeshFilter>();
            if (filters.Length == 0)
            {
                Object.DestroyImmediate(batch.gameObject);
                return;
            }

            var combine = new CombineInstance[filters.Length];
            for (int i = 0; i < filters.Length; i++)
            {
                combine[i].mesh = filters[i].sharedMesh;
                combine[i].transform = batch.worldToLocalMatrix * filters[i].transform.localToWorldMatrix;
            }

            var mesh = new Mesh { name = meshAssetName, indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(combine, true, true);
            mesh.RecalculateBounds();

            string meshPath = GeneratedFolder + "/" + meshAssetName + ".asset";
            AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(mesh, meshPath);

            Object.DestroyImmediate(batch.gameObject);

            var visual = new GameObject(objectName);
            visual.transform.SetParent(parent, false);
            visual.AddComponent<MeshFilter>().sharedMesh = mesh;
            visual.AddComponent<MeshRenderer>().sharedMaterial = material;
            visual.isStatic = true;
        }

        // --------------------------------------------------------------- helpers
        private static Transform NewGroup(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static GameObject CreateSlab(string name, Transform parent,
            float x0, float z0, float x1, float z1,
            float topY, float thickness, Material mat, bool withCollider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3((x0 + x1) * 0.5f, topY - thickness * 0.5f, (z0 + z1) * 0.5f);
            go.transform.localScale = new Vector3(Mathf.Abs(x1 - x0), thickness, Mathf.Abs(z1 - z0));
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;

            if (!withCollider)
            {
                var col = go.GetComponent<Collider>();
                if (col != null) Object.DestroyImmediate(col);
            }

            go.isStatic = true;
            return go;
        }

        // A triangular prism, built by hand because Unity has no primitive for one and a
        // gable end is exactly that shape. Unit size: 1 wide (x -0.5..0.5), 1 tall (y 0..1),
        // 1 deep - so scaling by (width, ridgeHeight - eaveHeight, thickness) fits any gable.
        // Winding is set so every face points outward; Unity takes a face normal as
        // Cross(v1 - v0, v2 - v0), which is the opposite order to the one you first expect.
        private static Mesh _gableMesh;
        private static Mesh GableMesh
        {
            get
            {
                if (_gableMesh != null) return _gableMesh;

                Vector3 a = new Vector3(-0.5f, 0f,  0.5f);
                Vector3 b = new Vector3( 0.5f, 0f,  0.5f);
                Vector3 c = new Vector3( 0f,   1f,  0.5f);
                Vector3 d = new Vector3(-0.5f, 0f, -0.5f);
                Vector3 e = new Vector3( 0.5f, 0f, -0.5f);
                Vector3 f = new Vector3( 0f,   1f, -0.5f);

                var verts = new List<Vector3>();
                var tris = new List<int>();

                AddTri(verts, tris, a, b, c);   // front cap
                AddTri(verts, tris, d, f, e);   // back cap
                AddTri(verts, tris, a, e, b);   // bottom
                AddTri(verts, tris, a, d, e);
                AddTri(verts, tris, a, f, d);   // left slope
                AddTri(verts, tris, a, c, f);
                AddTri(verts, tris, b, f, c);   // right slope
                AddTri(verts, tris, b, e, f);

                var m = new Mesh { name = "GablePrism" };
                m.SetVertices(verts);
                m.SetTriangles(tris, 0);
                m.RecalculateNormals();
                m.RecalculateBounds();
                _gableMesh = m;
                return m;
            }
        }

        private static void AddTri(List<Vector3> verts, List<int> tris, Vector3 a, Vector3 b, Vector3 c)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        }

        /// <summary>Adds a hand-built mesh to a batch, alongside the primitives.</summary>
        private static void AddMesh(Transform parent, Mesh mesh, Vector3 pos, Vector3 scale, Quaternion rot)
        {
            var go = new GameObject("BatchMesh");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
        }

        private static void AddPrimitive(Transform parent, PrimitiveType type,
                                         Vector3 pos, Vector3 scale, Quaternion rot)
        {
            var go = GameObject.CreatePrimitive(type);
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);

            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
        }

        /// <summary>
        /// Several near-identical materials for one surface. Without textures this is the
        /// cheapest way to stop a large area reading as a single flat colour.
        /// </summary>
        private static Material[] Variants(string baseName, Color colour, float smoothness,
                                           int count, float spread)
        {
            var mats = new Material[count];
            for (int i = 0; i < count; i++)
            {
                float t = (count == 1) ? 0f : (i / (float)(count - 1)) - 0.5f;
                Color c = new Color(
                    Mathf.Clamp01(colour.r + t * spread),
                    Mathf.Clamp01(colour.g + t * spread * 0.85f),
                    Mathf.Clamp01(colour.b + t * spread * 0.7f),
                    colour.a);
                mats[i] = GetOrCreateMaterial(baseName + "_" + i, c, smoothness);
            }
            return mats;
        }

        private static Material GetOrCreateMaterial(string name, Color colour, float smoothness)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (mat == null)
            {
                mat = new Material(FindLitShader()) { name = name };
                AssetDatabase.CreateAsset(mat, path);
            }

            if (mat.HasProperty("_BaseColor"))  mat.SetColor("_BaseColor", colour);
            if (mat.HasProperty("_Color"))      mat.SetColor("_Color", colour);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static Shader FindLitShader()
        {
            Shader s = null;
            if (GraphicsSettings.currentRenderPipeline != null)
            {
                s = Shader.Find("Universal Render Pipeline/Lit");
                if (s == null) s = Shader.Find("HDRP/Lit");
            }
            return s != null ? s : Shader.Find("Standard");
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;

            var parts = folder.Split('/');
            string current = parts[0];                       // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
