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
        private const string TextureFolder   = BaseFolder + "/Textures";
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
        // Kerb height above the GRASS. The paving top sits at PathTopY, so the lip the player
        // actually sees is (KerbHeight + chamfer) - PathTopY. At 0.10 that lip was over 10 cm,
        // which read as a raised divider rather than a path edge.
        private const float KerbHeight    = 0.05f;
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
            // Opens WEST, toward the end zone the player walks in from. A real shelter faces
            // the road, but then its blank back is the first thing a participant sees, which
            // is useless as a landmark.
            new Vector4( 32.5f, 20.0f, 270f, 0f),   // end zone
            // Both of these were moved off a lamp post: the bench sat 0.6 m from the west-arm
            // lamp and the noticeboard 0.9 m from the intersection lamp, so the post stood
            // between the player and the landmark.
            new Vector4( -2.6f, 16.4f,  40f, 1f),   // approaching the intersection
            new Vector4( -8.6f, 22.5f, 180f, 2f),   // west arm
            new Vector4(  2.4f, 29.5f, 180f, 3f),   // north arm
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

        // Rough centre of the fenced area. Used to decide which way things face.
        private static readonly Vector3 ParkCentre = new Vector3(9.5f, 0f, 12.5f);

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
            Texture2D grassA, grassN, concA, concN, asphA, asphN, timbA, timbN,
                      roofA, roofN, rendA, rendN, foliA;
            BuildTextures(out grassA, out grassN, out concA, out concN, out asphA, out asphN,
                          out timbA, out timbN, out roofA, out roofN, out rendA, out rendN,
                          out foliA);

            Material[] grassMats = Variants("M_Grass",  new Color(0.30f, 0.47f, 0.22f), 0.05f, 3, 0.045f, grassA, grassN);
            Material[] stoneMats = Variants("M_Stone",  new Color(0.66f, 0.65f, 0.62f), 0.10f, 3, 0.05f, concA, concN);
            Material   outerMat   = GetOrCreateMaterial("M_OuterGround", new Color(0.27f, 0.41f, 0.21f), 0.04f, grassA, grassN);
            Material   endZoneMat = GetOrCreateMaterial("M_EndZone",    new Color(0.45f, 0.58f, 0.72f), 0.15f, concA, concN);
            Material   metalMat   = GetOrCreateMaterial("M_FenceMetal", new Color(0.42f, 0.44f, 0.47f), 0.65f);
            Material   kerbMat    = GetOrCreateMaterial("M_PathEdge",   new Color(0.55f, 0.54f, 0.51f), 0.12f, concA, concN);
            Material[] foliageMats = Variants("M_Foliage", new Color(0.20f, 0.38f, 0.18f), 0.05f, 3, 0.055f, foliA, null);
            Material   flowerMat  = GetOrCreateMaterial("M_Flower",     new Color(0.86f, 0.80f, 0.42f), 0.08f);
            Material   stripeMat  = GetOrCreateMaterial("M_GrassStripe",new Color(0.35f, 0.53f, 0.25f), 0.05f, grassA, grassN);
            Material   tuftMat    = GetOrCreateMaterial("M_GrassTuft",  new Color(0.26f, 0.46f, 0.20f), 0.05f, foliA, null);
            Material   barkMat    = GetOrCreateMaterial("M_Bark",       new Color(0.34f, 0.26f, 0.19f), 0.05f, timbA, null);
            Material   rockMat    = GetOrCreateMaterial("M_Rock",       new Color(0.52f, 0.51f, 0.50f), 0.15f, concA, concN);
            Material   lampMat    = GetOrCreateMaterial("M_LampHead",   new Color(0.95f, 0.90f, 0.72f), 0.35f);
            Material   timberMat  = GetOrCreateMaterial("M_Timber",     new Color(0.52f, 0.42f, 0.31f), 0.08f, timbA, timbN);
            Material   asphaltMat = GetOrCreateMaterial("M_Asphalt",    new Color(0.17f, 0.17f, 0.18f), 0.18f, asphA, asphN);
            Material   lineMat    = GetOrCreateMaterial("M_RoadLine",   new Color(0.88f, 0.86f, 0.76f), 0.10f);
            Material   hedgeMat   = GetOrCreateMaterial("M_Hedge",      new Color(0.16f, 0.31f, 0.15f), 0.04f, foliA, null);
            Material   renderMat  = GetOrCreateMaterial("M_HouseWall",  new Color(0.78f, 0.74f, 0.67f), 0.08f, rendA, rendN);
            Material   roofMat    = GetOrCreateMaterial("M_HouseRoof",  new Color(0.36f, 0.31f, 0.30f), 0.10f, roofA, roofN);
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

            WarnOnLandmarkClashes();
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
                       OuterGroundTopY, 0.4f, mat, true, 4f);
        }

        private static void BuildGround(Transform root, Material[] grass)
        {
            var group = NewGroup("Ground_Grass", root);
            for (int i = 0; i < GrassRects.Length; i++)
            {
                var r = GrassRects[i];
                CreateSlab("Grass_" + i.ToString("00"), group, r.x, r.y, r.z, r.w,
                           0f, GroundThickness, grass[i % grass.Length], true, 3f);
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
                           RoadTopY, RoadThickness, asphalt, true, 3f);
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
                           PathThickness, m, true, 1.2f);
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
                EndBatch(batches[i], group, "Paving_" + i, "PavingMesh_" + i, stones[i], 1.2f);
            EndBatch(endBatch, group, "Paving_EndZone", "PavingMeshEndZone", endZone, 1.2f);
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

                // Chamfer: a narrower cap catching the light along the top edge. Kept thin so
                // the whole edge reads as a trim, not a step.
                AddPrimitive(batch, PrimitiveType.Cube,
                             mid + Vector3.up * (KerbHeight * 0.5f + 0.006f),
                             new Vector3(KerbWidth * 0.72f, 0.014f, length + KerbWidth), rot);
            }

            EndBatch(batch, group, "PathDetail", "PathDetailMesh", kerbMat, 1f);
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

            EndBatch(palingBatch, group, "Fence_Palings", "FenceMesh", timber, 1f);
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
            EndBatch(batch, group, "Hedge_Visual", "HedgeMesh", hedgeMat, 1.5f);
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
                Vector3 facing = new Vector3(-dir.z, 0f, dir.x);
                Vector3 start = new Vector3(a.x, 0f, a.y);

                // The perpendicular could point either way depending on how the frontage line
                // was wound, and two of the four rows were wound the other way - so the north
                // and west houses turned their backs on the park, which is exactly the view
                // straight ahead from the spawn. Resolve it from the geometry instead of
                // trusting the winding.
                Vector3 mid = start + dir * (length * 0.5f);
                if (Vector3.Dot(facing, ParkCentre - mid) < 0f) facing = facing * -1f;

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

            EndBatch(wallBatch, group, "House_Walls", "HouseWallMesh", wall, 2f);
            EndBatch(roofBatch, group, "House_Roofs", "HouseRoofMesh", roof, 1.2f);
            EndBatch(glassBatch, group, "House_Windows", "HouseWindowMesh", glass);
            EndBatch(trimBatch, group, "House_Trim", "HouseTrimMesh",
                     GetOrCreateMaterial("M_HouseTrim", new Color(0.90f, 0.89f, 0.86f), 0.10f));
        }

        /// <summary>
        /// Fills the triangle between the wall top and the two roof planes at a gable end.
        /// Hips close themselves, so they never call this.
        ///
        /// Sized from the ROOF rather than from the wall: it spans the full eave-to-eave width
        /// and rises to the exact ridge height, so its sloping edges lie flush against the roof
        /// planes and no sliver of sky can open up between them. It therefore overhangs the
        /// wall a little at each corner, where it has already tapered to nothing, tucked in
        /// under the verge.
        /// </summary>
        private static void AddGableInfill(Transform wallBatch, Vector3 centre, float baseY,
                                           float w, float d, float overhang, float slopeDeg,
                                           Quaternion rot, Vector3 facing, Vector3 side)
        {
            float W = w * 0.5f + overhang;
            float D = d * 0.5f + overhang;

            bool ridgeAlongZ = D >= W;
            float across = ridgeAlongZ ? W : D;
            float rise = across * Mathf.Tan(slopeDeg * Mathf.Deg2Rad);

            // The gable ends sit at the ends of the ridge, so which pair of walls they are
            // depends on which way the ridge runs.
            Vector3 endDir = ridgeAlongZ ? facing : side;
            float standoff = (ridgeAlongZ ? d * 0.5f : w * 0.5f) - 0.05f;
            Quaternion faceRot = ridgeAlongZ ? rot : rot * Quaternion.Euler(0f, 90f, 0f);

            // Dropped 10 cm so it bites into the wall below, and raised by the same 10 cm so
            // the apex lands exactly on the ridge. Any more and a wall-coloured nick appears
            // above the ridge line; any less and the peak opens up.
            for (int s = -1; s <= 1; s += 2)
            {
                AddMesh(wallBatch, GableMesh,
                        centre + endDir * (s * standoff) + Vector3.up * (baseY - 0.10f),
                        new Vector3(across * 2f, rise + 0.10f, 0.12f),
                        faceRot);
            }
        }

        private static void AddHouse(Transform wallBatch, Transform roofBatch, Transform glassBatch,
                                     Transform trimBatch, Vector3 frontage, Vector3 facing)
        {
            float w = Random.Range(8.5f, 11.5f);      // across the frontage
            float d = Random.Range(7.5f, 10f);        // back from the road
            float h = Random.Range(2.6f, 3.1f);       // wall height to the eaves
            float slope = Random.Range(22f, 30f);
            bool hipRoof = Random.Range(0f, 1f) < 0.45f;

            const float eaveX = 0.55f;                // roof overhang, all round

            Quaternion rot = Quaternion.LookRotation(facing, Vector3.up);
            Vector3 side = Vector3.Cross(Vector3.up, facing).normalized;
            Vector3 centre = frontage - facing * (d * 0.5f);


            // ---- walls
            AddPrimitive(wallBatch, PrimitiveType.Cube,
                         centre + Vector3.up * (h * 0.5f), new Vector3(w, h, d), rot);

            // ---- roof, as one mesh
            AddMesh(roofBatch, MakeRoofMesh(w, d, eaveX, slope, 0.16f, hipRoof),
                    centre + Vector3.up * h, Vector3.one, rot);

            // ---- gable end infill
            // Only gables need it - a hip closes itself with its end faces.
            if (!hipRoof)
            {
                AddGableInfill(wallBatch, centre, h, w, d, eaveX, slope, rot, facing, side);
            }

            // ---- fascia along the eaves
            // The eave is simply the roof mesh's outer edge, which sits at y = h by
            // construction, so there is no separate height calculation to get wrong.
            {
                float W = w * 0.5f + eaveX;
                float D = d * 0.5f + eaveX;

                for (int s = -1; s <= 1; s += 2)
                {
                    AddPrimitive(trimBatch, PrimitiveType.Cube,
                                 centre + side * (s * W) + Vector3.up * (h - 0.04f),
                                 new Vector3(0.09f, 0.20f, D * 2f), rot);
                }

                if (hipRoof)
                {
                    for (int s = -1; s <= 1; s += 2)
                    {
                        AddPrimitive(trimBatch, PrimitiveType.Cube,
                                     centre + facing * (s * D) + Vector3.up * (h - 0.04f),
                                     new Vector3(W * 2f, 0.20f, 0.09f), rot);
                    }
                }
            }

            // ---- chimney, on about half of them
            if (Random.Range(0f, 1f) < 0.5f)
            {
                // Chimney height is taken from the same roof maths, so it always lands on the
                // slope rather than floating above it or sinking into it.
                float W = w * 0.5f + eaveX;
                float D = d * 0.5f + eaveX;
                bool ridgeAlongZ = D >= W;
                float across = ridgeAlongZ ? W : D;
                float rise = across * Mathf.Tan(slope * Mathf.Deg2Rad);

                float cx = Random.Range(0.18f, 0.32f) * w * (Random.Range(0, 2) == 0 ? 1f : -1f);
                float roofYAtC = h + rise * (1f - Mathf.Abs(cx) / across);
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

                AddMesh(roofBatch, MakeRoofMesh(gw, gd, 0.30f, slope, 0.14f, false),
                        gCentre + Vector3.up * gh, Vector3.one, rot);

                // The garage is always gabled, so its ends always need filling. Leaving this
                // out is why you could see sky straight through the small roofs.
                AddGableInfill(wallBatch, gCentre, gh, gw, gd, 0.30f, slope, rot, facing, side);

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
        /// <summary>
        /// One trunk mesh and one canopy mesh. No stacked cylinders, no ring of overlapping
        /// spheres - both of those produce visible joins, which is exactly what reads as
        /// "assembled from parts". Variation comes from picking a different blob, rotating it
        /// and scaling it unevenly, so no two trees repeat while each stays a single volume.
        /// </summary>
        private static void AddTree(Transform barkBatch, Transform foliageBatch,
                                    Vector3 basePos, float height, float canopy)
        {
            int variant = Random.Range(0, BlobVariants);
            float leanX = Random.Range(-3.5f, 3.5f);
            float leanZ = Random.Range(-3.5f, 3.5f);
            Quaternion leanRot = Quaternion.Euler(leanX, Random.Range(0f, 360f), leanZ);

            // Trunk. The mesh is built 1 unit tall with a unit-ish radius, so scaling gives
            // any proportion; it is sunk slightly so the flared foot beds into the ground.
            AddMesh(barkBatch, TrunkVariant(variant),
                    basePos + Vector3.down * 0.05f,
                    new Vector3(height * 0.24f, height + 0.05f, height * 0.24f),
                    leanRot);

            // Canopy: a single closed surface, squashed and rotated per tree.
            Vector3 crown = basePos + leanRot * new Vector3(0f, height * 0.97f + canopy * 0.22f, 0f);

            // NOTE the blob mesh already spans 2 units, so these are DIAMETER multipliers.
            // Treating them as radii made every canopy twice the intended size.
            AddMesh(foliageBatch, BlobVariant(variant),
                    crown,
                    new Vector3(canopy * Random.Range(0.85f, 1.05f),
                                canopy * Random.Range(0.68f, 0.88f),
                                canopy * Random.Range(0.85f, 1.05f)),
                    Quaternion.Euler(Random.Range(-14f, 14f), Random.Range(0f, 360f),
                                     Random.Range(-14f, 14f)));

            // A second, smaller mass low on one side gives an irregular silhouette without
            // reintroducing a visible join, because it is buried well inside the main canopy.
            if (Random.Range(0f, 1f) < 0.55f)
            {
                float ang = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                Vector3 off = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * canopy * 0.42f;

                AddMesh(foliageBatch, BlobVariant(variant + 3),
                        crown + off + Vector3.down * (canopy * Random.Range(0.20f, 0.45f)),
                        Vector3.one * (canopy * Random.Range(0.55f, 0.72f)),
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
                    float height = Random.Range(6.0f, 7.6f);
                    float canopy = Random.Range(1.45f, 1.90f);
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
                float height = Random.Range(4.6f, 6.4f);
                float canopy = Random.Range(1.15f, 1.60f);

                AddTree(barkBatch, foliageBatches[Random.Range(0, foliageBatches.Length)],
                        new Vector3(p.x, 0f, p.y), height, canopy);

                // No mulch ring: the flared trunk foot now meets the lawn on its own.

                var col = new GameObject("TreeCollider_" + treeIndex.ToString("00"));
                col.transform.SetParent(trunkColliders, false);
                col.transform.localPosition = new Vector3(p.x, height * 0.5f, p.y);
                var capsule = col.AddComponent<CapsuleCollider>();
                capsule.radius = 0.26f;
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

                // Remember each lobe, so flowers can be planted on an actual surface rather
                // than at a guessed height. Guessing is what left them hanging in mid-air.
                var lobeAt = new List<Vector3>();
                var lobeR = new List<float>();

                for (int i = 0; i < lobes; i++)
                {
                    float drop = 1f - (i / (float)lobes) * 0.45f;      // smaller toward the top
                    float s = Random.Range(0.34f, 0.62f) * scaleBase * drop;
                    Vector2 offset = Random.insideUnitCircle * 0.30f * scaleBase;
                    float y = (0.16f + (i / (float)lobes) * 0.42f) * scaleBase;

                    Vector3 at = new Vector3(p.x + offset.x, y, p.y + offset.y);
                    AddPrimitive(bush, PrimitiveType.Sphere, at,
                                 new Vector3(s, s * Random.Range(0.75f, 0.95f), s),
                                 Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));

                    lobeAt.Add(at);
                    lobeR.Add(s * 0.5f);           // primitive sphere scale is diameter
                }

                if (Random.Range(0f, 1f) < 0.17f)
                {
                    int flowers = Random.Range(5, 10);
                    for (int i = 0; i < flowers; i++)
                    {
                        int k = Random.Range(0, lobeAt.Count);

                        // Outward and upward, then pulled in to 80% of the lobe radius so the
                        // bloom is bedded into the foliage instead of floating off it.
                        float ang = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                        float up = Random.Range(0.25f, 0.95f);
                        float flat = 1f - up;
                        Vector3 dir = new Vector3(Mathf.Sin(ang) * flat, up,
                                                  Mathf.Cos(ang) * flat).normalized;

                        AddPrimitive(flowerBatch, PrimitiveType.Sphere,
                                     lobeAt[k] + dir * (lobeR[k] * 0.80f),
                                     Vector3.one * Random.Range(0.07f, 0.12f), Quaternion.identity);
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
                EndBatch(foliageBatches[i], group, "Foliage_" + i, "FoliageMesh_" + i, foliages[i], 1.5f);
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

            EndBatch(batch, group, "Lawn_Stripes", "LawnStripeMesh", stripe, 3f);
        }

        /// <summary>
        /// A landmark is only a landmark if you can see it. Flags any that a lamp post would
        /// stand in front of, rather than leaving it to be spotted in the headset.
        /// </summary>
        private static void WarnOnLandmarkClashes()
        {
            if (!IncludeLandmarks) return;

            foreach (var l in Landmarks)
            {
                foreach (var p in LampPosts)
                {
                    float dx = l.x - p.x, dz = l.y - p.y;
                    float dist = Mathf.Sqrt(dx * dx + dz * dz);
                    if (dist < 1.6f)
                    {
                        Debug.LogWarning("[VRTutorial] Lamp post at (" + p.x + ", " + p.y +
                                         ") is only " + dist.ToString("0.00") +
                                         " m from the landmark at (" + l.x + ", " + l.y +
                                         ") and will obscure it.");
                    }
                }
            }
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
                                     string meshAssetName, Material material,
                                     float metresPerTile = 2f)
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
            ApplyBoxUVs(mesh, metresPerTile);

            // Tangents must come AFTER the UVs, because they are derived from them. Without
            // this every normal-mapped surface in the scene is lit using undefined tangents,
            // which shows up as light and dark banding that has nothing to do with the shape.
            mesh.RecalculateTangents();

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
            float topY, float thickness, Material mat, bool withCollider,
            float metresPerTile = 2f)
        {
            Vector3 size = new Vector3(Mathf.Abs(x1 - x0), thickness, Mathf.Abs(z1 - z0));

            // Built at size with a scale of 1, so the box projection gives this slab the same
            // texel density as everything else. A scaled primitive cannot.
            Mesh mesh = MakeBoxMesh(size);
            mesh.name = name + "Mesh";
            ApplyBoxUVs(mesh, metresPerTile);
            mesh.RecalculateTangents();

            string meshPath = GeneratedFolder + "/" + mesh.name + ".asset";
            AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(mesh, meshPath);

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3((x0 + x1) * 0.5f, topY - thickness * 0.5f, (z0 + z1) * 0.5f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;

            if (withCollider)
            {
                var box = go.AddComponent<BoxCollider>();
                box.size = size;
            }

            go.isStatic = true;
            return go;
        }

        // ------------------------------------------------------------------ UVs
        /// <summary>
        /// Box projection: each vertex takes its UV from the two world axes perpendicular to
        /// its dominant normal, divided by a fixed metres-per-tile.
        ///
        /// This is the whole reason textures are usable here. Unity primitives carry 0-1 UVs
        /// per face, so a cube stretched to 30 m across has that single tile smeared over the
        /// whole thing while a 16 cm kerb squashes it - and CombineMeshes just preserves
        /// whatever it is handed. Projecting from world position instead gives every surface
        /// the same texel density regardless of the object's scale, with no UV authoring and
        /// no custom shader. It is baked once at build time, so it costs nothing at runtime.
        /// </summary>
        private static void ApplyBoxUVs(Mesh mesh, float metresPerTile)
        {
            Vector3[] verts = mesh.vertices;
            Vector3[] norms = mesh.normals;
            if (verts.Length == 0 || norms.Length != verts.Length) return;

            var uvs = new Vector2[verts.Length];
            float s = 1f / Mathf.Max(0.01f, metresPerTile);

            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 n = norms[i];
                float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);

                if (ay >= ax && ay >= az)                       // floor or ceiling
                    uvs[i] = new Vector2(verts[i].x * s, verts[i].z * s);
                else if (ax >= az)                              // facing east or west
                    uvs[i] = new Vector2(verts[i].z * s, verts[i].y * s);
                else                                            // facing north or south
                    uvs[i] = new Vector2(verts[i].x * s, verts[i].y * s);
            }

            mesh.uv = uvs;
        }

        /// <summary>
        /// A box built at its real size with flat normals, rather than a unit primitive scaled
        /// up. Scaling a primitive is what wrecks its UVs; building at size means the box
        /// projection above lands correctly.
        /// </summary>
        private static Mesh MakeBoxMesh(Vector3 size)
        {
            Vector3 h = new Vector3(size.x * 0.5f, size.y * 0.5f, size.z * 0.5f);

            var verts = new List<Vector3>();
            var tris = new List<int>();

            AddQuad(verts, tris, new Vector3(-h.x, h.y, -h.z), new Vector3(-h.x, h.y, h.z),
                                 new Vector3( h.x, h.y,  h.z), new Vector3( h.x, h.y, -h.z));   // top
            AddQuad(verts, tris, new Vector3(-h.x, -h.y, h.z), new Vector3(-h.x, -h.y, -h.z),
                                 new Vector3( h.x, -h.y, -h.z), new Vector3( h.x, -h.y, h.z));  // bottom
            AddQuad(verts, tris, new Vector3(-h.x, -h.y, -h.z), new Vector3(-h.x, h.y, -h.z),
                                 new Vector3( h.x,  h.y, -h.z), new Vector3( h.x, -h.y, -h.z)); // -Z
            AddQuad(verts, tris, new Vector3( h.x, -h.y, h.z), new Vector3( h.x, h.y, h.z),
                                 new Vector3(-h.x,  h.y, h.z), new Vector3(-h.x, -h.y, h.z));   // +Z
            AddQuad(verts, tris, new Vector3(-h.x, -h.y,  h.z), new Vector3(-h.x, h.y,  h.z),
                                 new Vector3(-h.x,  h.y, -h.z), new Vector3(-h.x, -h.y, -h.z)); // -X
            AddQuad(verts, tris, new Vector3( h.x, -h.y, -h.z), new Vector3( h.x, h.y, -h.z),
                                 new Vector3( h.x,  h.y,  h.z), new Vector3( h.x, -h.y,  h.z)); // +X

            var m = new Mesh { name = "Box" };
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        private static void AddQuad(List<Vector3> verts, List<int> tris,
                                    Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
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

        // --------------------------------------------------- organic tree geometry
        // Overlapping spheres can never read as one mass: wherever two intersect you see the
        // circle of intersection, and that is the "obvious connection". The only way to remove
        // it is to make the canopy a SINGLE closed surface, so there is nothing to intersect.
        // Each canopy is therefore one sphere mesh whose vertices are pushed in and out by 3D
        // noise - continuous by construction, smooth-shaded, and different for every seed.

        private static int Hash3(int x, int y, int z, int seed)
        {
            int h = x * 374761393 + y * 668265263 + z * 2147483647 + seed * 1274126177;
            h = (h ^ (h >> 13)) * 1274126177;
            return h ^ (h >> 16);
        }

        private static float Hash3F(int x, int y, int z, int seed)
        {
            return (Hash3(x, y, z, seed) & 0x7fffffff) / 2147483647f;
        }

        /// <summary>Trilinear value noise in three dimensions.</summary>
        private static float Noise3(Vector3 p, int seed)
        {
            int x0 = Mathf.FloorToInt(p.x), y0 = Mathf.FloorToInt(p.y), z0 = Mathf.FloorToInt(p.z);
            float fx = p.x - x0, fy = p.y - y0, fz = p.z - z0;

            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            fz = fz * fz * (3f - 2f * fz);

            float c000 = Hash3F(x0,     y0,     z0,     seed);
            float c100 = Hash3F(x0 + 1, y0,     z0,     seed);
            float c010 = Hash3F(x0,     y0 + 1, z0,     seed);
            float c110 = Hash3F(x0 + 1, y0 + 1, z0,     seed);
            float c001 = Hash3F(x0,     y0,     z0 + 1, seed);
            float c101 = Hash3F(x0 + 1, y0,     z0 + 1, seed);
            float c011 = Hash3F(x0,     y0 + 1, z0 + 1, seed);
            float c111 = Hash3F(x0 + 1, y0 + 1, z0 + 1, seed);

            float x00 = c000 + (c100 - c000) * fx;
            float x10 = c010 + (c110 - c010) * fx;
            float x01 = c001 + (c101 - c001) * fx;
            float x11 = c011 + (c111 - c011) * fx;

            float y0v = x00 + (x10 - x00) * fy;
            float y1v = x01 + (x11 - x01) * fy;

            return y0v + (y1v - y0v) * fz;
        }

        /// <summary>
        /// A closed blob: a sphere whose radius is modulated by two octaves of 3D noise.
        /// Poles are single shared vertices and the seam column wraps, so RecalculateNormals
        /// gives fully smooth shading with no seam line anywhere.
        /// </summary>
        /// <summary>Radius of the blob in a given direction: 1, pushed about by two octaves.</summary>
        private static Vector3 BlobRadius(Vector3 dir, int seed, float lumpiness)
        {
            float d = 1f
                    + (Noise3(dir * 1.9f + new Vector3(11f, 7f, 3f), seed) - 0.5f) * lumpiness
                    + (Noise3(dir * 4.1f + new Vector3(3f, 17f, 9f), seed + 31) - 0.5f)
                      * lumpiness * 0.45f;
            return dir * Mathf.Max(0.35f, d);
        }

        private static int BlobRing(int r, int s, int segments)
        {
            return 1 + (r - 1) * segments + (s % segments);
        }

        private static Mesh MakeBlobMesh(int seed, int rings, int segments, float lumpiness)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();

            verts.Add(BlobRadius(Vector3.up, seed, lumpiness));      // 0 = north pole

            for (int r = 1; r < rings; r++)
            {
                float phi = r * Mathf.PI / rings;
                float sy = Mathf.Cos(phi), sr = Mathf.Sin(phi);

                for (int s = 0; s < segments; s++)
                {
                    float th = s * 2f * Mathf.PI / segments;
                    Vector3 dir = new Vector3(Mathf.Cos(th) * sr, sy, Mathf.Sin(th) * sr);
                    verts.Add(BlobRadius(dir, seed, lumpiness));
                }
            }

            int south = verts.Count;
            verts.Add(BlobRadius(Vector3.up * -1f, seed, lumpiness));   // south pole

            for (int s = 0; s < segments; s++)                    // north cap
            {
                tris.Add(0); tris.Add(BlobRing(1, s + 1, segments)); tris.Add(BlobRing(1, s, segments));
            }

            for (int r = 1; r < rings - 1; r++)                   // bands
            {
                for (int s = 0; s < segments; s++)
                {
                    tris.Add(BlobRing(r, s, segments)); tris.Add(BlobRing(r + 1, s + 1, segments)); tris.Add(BlobRing(r + 1, s, segments));
                    tris.Add(BlobRing(r, s, segments)); tris.Add(BlobRing(r, s + 1, segments));     tris.Add(BlobRing(r + 1, s + 1, segments));
                }
            }

            for (int s = 0; s < segments; s++)                    // south cap
            {
                tris.Add(south); tris.Add(BlobRing(rings - 1, s, segments)); tris.Add(BlobRing(rings - 1, s + 1, segments));
            }

            var m = new Mesh { name = "Blob" };
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>
        /// One continuous tapered trunk with a gentle lean, rather than stacked cylinders.
        /// Stacked sections are visible as steps however carefully the radii are chosen.
        /// </summary>
        /// <summary>
        /// A straight tapered tube. Deliberately the simplest thing that can work.
        ///
        /// Earlier versions curved for three separate reasons - a smoothstep taper, a
        /// quadratic lean applied per ring, and per-ring noise on the centreline - and each
        /// one is invisible in the code but obvious in the silhouette. Here the radius is a
        /// straight linear function of height and the centreline is exactly vertical, so the
        /// outline is a cone and nothing else. Irregularity comes only from varying the radius
        /// PER SIDE, constant up the whole trunk: the cross-section is slightly out of round,
        /// which reads as organic, while every vertical edge stays dead straight.
        ///
        /// Lean is applied by rotating the whole mesh when it is placed, never by displacing
        /// vertices - a rotation cannot bend anything.
        /// </summary>
        private static Mesh MakeTrunkMesh(int seed, int sides, int rings,
                                          float baseRadius, float topRadius, float unusedLean)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();

            // Per-side multipliers, computed once and reused at every height.
            var sideScale = new float[sides];
            for (int s = 0; s < sides; s++)
                sideScale[s] = 1f + (HashF(s, seed, 0) - 0.5f) * 0.14f;

            for (int r = 0; r <= rings; r++)
            {
                float v = r / (float)rings;
                // No root flare. Twice now a swell at the foot has been stretched across a
                // whole ring spacing and smooth-shaded into a curve, which is precisely the
                // artefact being chased. A cone with nothing added to it cannot do that.
                float radius = Mathf.Lerp(baseRadius, topRadius, v);

                for (int s = 0; s < sides; s++)
                {
                    float th = s * 2f * Mathf.PI / sides;
                    float rr = radius * sideScale[s];
                    verts.Add(new Vector3(Mathf.Cos(th) * rr, v, Mathf.Sin(th) * rr));
                }
            }

            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < sides; s++)
                {
                    int a = r * sides + s;
                    int b = r * sides + (s + 1) % sides;
                    int c = (r + 1) * sides + s;
                    int d = (r + 1) * sides + (s + 1) % sides;

                    // Wound OUTWARD. Reversed, these two triangles face into the trunk:
                    // backface culling then hides the near wall and shows the inside of the
                    // far one, which is a genuinely concave surface lit by inward normals.
                    // That was the "concave trunk" the whole time - not the profile at all.
                    tris.Add(a); tris.Add(c); tris.Add(d);
                    tris.Add(a); tris.Add(d); tris.Add(b);
                }
            }

            var m = new Mesh { name = "Trunk" };
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // A handful of canopy and trunk shapes, reused with random rotation and scaling.
        // Regenerating one per tree would be slower for no visible gain, since they all end
        // up merged into a single batch mesh anyway.
        private const int BlobVariants = 10;
        private static Mesh[] _blobs;
        private static Mesh[] _trunks;

        private static Mesh BlobVariant(int i)
        {
            if (_blobs == null)
            {
                _blobs = new Mesh[BlobVariants];
                for (int k = 0; k < BlobVariants; k++)
                    _blobs[k] = MakeBlobMesh(1000 + k * 37, 14, 18, 0.42f);
            }
            return _blobs[((i % BlobVariants) + BlobVariants) % BlobVariants];
        }

        private static Mesh TrunkVariant(int i)
        {
            if (_trunks == null)
            {
                _trunks = new Mesh[BlobVariants];
                for (int k = 0; k < BlobVariants; k++)
                    _trunks[k] = MakeTrunkMesh(2000 + k * 53, 16, 6, 0.16f, 0.122f, 0f);
            }
            return _trunks[((i % BlobVariants) + BlobVariants) % BlobVariants];
        }

        /// <summary>
        /// A real roof, as a single closed mesh.
        ///
        /// The previous version intersected full rectangles, which cannot make a correct hip:
        /// the long faces of a hip are TRAPEZOIDS and the ends are TRIANGLES, so crossing four
        /// rectangles leaves planes poking through each other at every corner. It also always
        /// ran the ridge along Z, which is wrong whenever the house is wider than it is deep.
        ///
        /// Origin is at eave level, centred on plan. Built at real size, so it is placed with
        /// a scale of 1.
        /// </summary>
        private static Mesh MakeRoofMesh(float w, float d, float overhang, float slopeDeg,
                                         float thickness, bool hip)
        {
            float W = w * 0.5f + overhang;
            float D = d * 0.5f + overhang;

            // The ridge runs along whichever plan dimension is longer. Forcing it along one
            // axis is what collapsed the hip to zero ridge length on wide houses.
            bool ridgeAlongZ = D >= W;
            float across = ridgeAlongZ ? W : D;
            float along  = ridgeAlongZ ? D : W;

            float rise = across * Mathf.Tan(slopeDeg * Mathf.Deg2Rad);
            float ridgeHalf = hip ? Mathf.Max(0f, along - across) : along;

            var verts = new List<Vector3>();
            var tris = new List<int>();

            var loop = new List<Vector3>();

            // Top surface
            if (ridgeAlongZ)
            {
                AddQuad(verts, tris, new Vector3(-across, 0f, -along), new Vector3(-across, 0f, along),
                                     new Vector3(0f, rise, ridgeHalf), new Vector3(0f, rise, -ridgeHalf));
                AddQuad(verts, tris, new Vector3(0f, rise, -ridgeHalf), new Vector3(0f, rise, ridgeHalf),
                                     new Vector3(across, 0f, along), new Vector3(across, 0f, -along));
                if (hip)
                {
                    AddTri(verts, tris, new Vector3(-across, 0f, along), new Vector3(across, 0f, along),
                                        new Vector3(0f, rise, ridgeHalf));
                    AddTri(verts, tris, new Vector3(across, 0f, -along), new Vector3(-across, 0f, -along),
                                        new Vector3(0f, rise, -ridgeHalf));
                    loop.Add(new Vector3(-across, 0f, -along)); loop.Add(new Vector3(across, 0f, -along));
                    loop.Add(new Vector3(across, 0f, along));   loop.Add(new Vector3(-across, 0f, along));
                }
                else
                {
                    loop.Add(new Vector3(-across, 0f, -along)); loop.Add(new Vector3(0f, rise, -ridgeHalf));
                    loop.Add(new Vector3(across, 0f, -along));  loop.Add(new Vector3(across, 0f, along));
                    loop.Add(new Vector3(0f, rise, ridgeHalf)); loop.Add(new Vector3(-across, 0f, along));
                }
            }
            else
            {
                // Rotated, NOT mirrored: (u, y, v) -> (v, y, -u). Swapping two axes instead
                // would reflect the mesh and invert every face.
                AddQuad(verts, tris, new Vector3(-along, 0f, across), new Vector3(along, 0f, across),
                                     new Vector3(ridgeHalf, rise, 0f), new Vector3(-ridgeHalf, rise, 0f));
                AddQuad(verts, tris, new Vector3(-ridgeHalf, rise, 0f), new Vector3(ridgeHalf, rise, 0f),
                                     new Vector3(along, 0f, -across), new Vector3(-along, 0f, -across));
                if (hip)
                {
                    AddTri(verts, tris, new Vector3(along, 0f, across), new Vector3(along, 0f, -across),
                                        new Vector3(ridgeHalf, rise, 0f));
                    AddTri(verts, tris, new Vector3(-along, 0f, -across), new Vector3(-along, 0f, across),
                                        new Vector3(-ridgeHalf, rise, 0f));
                    loop.Add(new Vector3(-along, 0f, across)); loop.Add(new Vector3(along, 0f, across));
                    loop.Add(new Vector3(along, 0f, -across)); loop.Add(new Vector3(-along, 0f, -across));
                }
                else
                {
                    loop.Add(new Vector3(-along, 0f, across));    loop.Add(new Vector3(-ridgeHalf, rise, 0f));
                    loop.Add(new Vector3(-along, 0f, -across));   loop.Add(new Vector3(along, 0f, -across));
                    loop.Add(new Vector3(ridgeHalf, rise, 0f));   loop.Add(new Vector3(along, 0f, across));
                }
            }

            // Underside: the same faces dropped by the thickness, wound the other way.
            int topCount = verts.Count;
            var drop = new Vector3(0f, -thickness, 0f);
            for (int i = 0; i < topCount; i++) verts.Add(verts[i] + drop);

            int topTris = tris.Count;
            for (int i = 0; i < topTris; i += 3)
            {
                tris.Add(topCount + tris[i]);
                tris.Add(topCount + tris[i + 2]);
                tris.Add(topCount + tris[i + 1]);
            }

            // Rim around the outer edge, closing the slab so the eave reads as having depth.
            for (int i = 0; i < loop.Count; i++)
            {
                Vector3 a = loop[i];
                Vector3 b = loop[(i + 1) % loop.Count];
                AddQuad(verts, tris, a, b, b + drop, a + drop);
            }

            var m = new Mesh { name = "Roof" };
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
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
                                           int count, float spread,
                                           Texture2D albedo = null, Texture2D normal = null)
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
                mats[i] = GetOrCreateMaterial(baseName + "_" + i, c, smoothness, albedo, normal);
            }
            return mats;
        }

        private static Material GetOrCreateMaterial(string name, Color colour, float smoothness,
                                                    Texture2D albedo = null, Texture2D normal = null)
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

            // Tiling stays at 1. Scale is controlled by the metres-per-tile used when the
            // UVs were projected, which is per surface rather than per material.
            if (albedo != null)
            {
                if (mat.HasProperty("_BaseMap"))  mat.SetTexture("_BaseMap", albedo);
                if (mat.HasProperty("_MainTex"))  mat.SetTexture("_MainTex", albedo);
            }

            if (normal != null && mat.HasProperty("_BumpMap"))
            {
                mat.SetTexture("_BumpMap", normal);
                if (mat.HasProperty("_BumpScale")) mat.SetFloat("_BumpScale", 1f);
                mat.EnableKeyword("_NORMALMAP");
            }

            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ------------------------------------------------------- procedural textures
        // Everything here is generated in code and written out as PNG assets, so the project
        // needs no downloaded art. It is tileable by construction: the noise lattice wraps at
        // the texture size, so there is no seam where tiles meet.
        //
        // Normal maps matter more than albedo at this level of detail. Under a baked bounce,
        // relief is what makes a surface read as concrete or grass; a flat colour with a good
        // normal beats a busy albedo with none.

        private const int TexSize = 512;

        private static int HashInt(int x, int y, int seed)
        {
            int h = x * 374761393 + y * 668265263 + seed * 1274126177;
            h = (h ^ (h >> 13)) * 1274126177;
            return h ^ (h >> 16);
        }

        private static float HashF(int x, int y, int seed)
        {
            return (HashInt(x, y, seed) & 0x7fffffff) / 2147483647f;
        }

        private static int Wrap(int v, int period)
        {
            int r = v % period;
            return r < 0 ? r + period : r;
        }

        /// <summary>Value noise that wraps at <paramref name="period"/>, so the result tiles.</summary>
        private static float Noise(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);

            float v00 = HashF(Wrap(x0, period),     Wrap(y0, period),     seed);
            float v10 = HashF(Wrap(x0 + 1, period), Wrap(y0, period),     seed);
            float v01 = HashF(Wrap(x0, period),     Wrap(y0 + 1, period), seed);
            float v11 = HashF(Wrap(x0 + 1, period), Wrap(y0 + 1, period), seed);

            float a = v00 + (v10 - v00) * fx;
            float b = v01 + (v11 - v01) * fx;
            return a + (b - a) * fy;
        }

        /// <summary>Several octaves of the above, still tiling.</summary>
        private static float Fbm(float u, float v, int basePeriod, int octaves, int seed)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            int period = basePeriod;

            for (int o = 0; o < octaves; o++)
            {
                sum += Noise(u * period, v * period, period, seed + o * 17) * amp;
                norm += amp;
                amp *= 0.5f;
                period *= 2;
            }
            return sum / norm;
        }

        /// <summary>
        /// The surface set. Each is deliberately quiet: the point is that a participant can
        /// tell footpath from grass from road at a glance, not that the ground is busy.
        /// Generated once and cached as assets - delete the Textures folder to regenerate.
        /// </summary>
        private static void BuildTextures(out Texture2D grassA, out Texture2D grassN,
                                          out Texture2D concA,  out Texture2D concN,
                                          out Texture2D asphA,  out Texture2D asphN,
                                          out Texture2D timbA,  out Texture2D timbN,
                                          out Texture2D roofA,  out Texture2D roofN,
                                          out Texture2D rendA,  out Texture2D rendN,
                                          out Texture2D foliA)
        {
            // Grass: broad mottling plus a fine speckle, so it does not read as felt.
            grassA = Albedo("T_Grass_A", (u, v) =>
            {
                float broad = Fbm(u, v, 4, 4, 11);
                float fine  = Fbm(u, v, 48, 2, 23);
                float k = broad * 0.7f + fine * 0.3f;
                return new Color(0.20f + k * 0.14f, 0.36f + k * 0.20f, 0.14f + k * 0.10f);
            });
            grassN = NormalMap("T_Grass_N", (u, v) => Fbm(u, v, 40, 3, 23), 2.2f);

            // Concrete: aggregate speckle over gentle blotching.
            concA = Albedo("T_Concrete_A", (u, v) =>
            {
                float blotch = Fbm(u, v, 5, 3, 31);
                float grit   = Fbm(u, v, 90, 2, 37);
                float k = 0.62f + blotch * 0.10f + (grit - 0.5f) * 0.16f;
                return new Color(k, k * 0.995f, k * 0.97f);
            });
            concN = NormalMap("T_Concrete_N", (u, v) => Fbm(u, v, 80, 3, 37), 1.6f);

            // Asphalt: coarse and dark, with a scatter of paler stones.
            asphA = Albedo("T_Asphalt_A", (u, v) =>
            {
                float grit = Fbm(u, v, 70, 3, 41);
                float k = 0.15f + grit * 0.10f;
                if (grit > 0.78f) k += 0.14f;           // exposed aggregate
                return new Color(k, k, k * 1.04f);
            });
            asphN = NormalMap("T_Asphalt_N", (u, v) => Fbm(u, v, 70, 3, 41), 2.6f);

            // Timber: vertical grain, with the boards running along V.
            timbA = Albedo("T_Timber_A", (u, v) =>
            {
                float grain = Fbm(u * 6f, v * 0.35f, 24, 3, 53);
                float rings = Mathf.Abs(Mathf.Sin((u * 26f + grain * 3.5f) * 3.14159f));
                float k = 0.46f + grain * 0.16f - rings * 0.09f;
                return new Color(k, k * 0.80f, k * 0.60f);
            });
            timbN = NormalMap("T_Timber_N",
                (u, v) => Fbm(u * 6f, v * 0.35f, 24, 3, 53) * 0.6f
                          + Mathf.Abs(Mathf.Sin(u * 26f * 3.14159f)) * 0.4f, 1.4f);

            // Roof: horizontal tile courses. The ridges do most of the work here.
            roofA = Albedo("T_Roof_A", (u, v) =>
            {
                float course = Frac(v * 8f);
                float shade = course < 0.10f ? 0.62f : 1f;      // shadow under each lap
                float grit = Fbm(u, v, 40, 2, 67);
                float k = (0.30f + grit * 0.09f) * shade;
                return new Color(k, k * 0.94f, k * 0.92f);
            });
            roofN = NormalMap("T_Roof_N", (u, v) =>
            {
                float course = Frac(v * 8f);
                float lap = course < 0.14f ? (course / 0.14f) : 1f;
                return lap * 0.75f + Fbm(u, v, 40, 2, 67) * 0.25f;
            }, 3.2f);

            // Render: fine stucco, the calmest surface in the set.
            rendA = Albedo("T_Render_A", (u, v) =>
            {
                float stipple = Fbm(u, v, 110, 2, 71);
                float k = 0.74f + (stipple - 0.5f) * 0.10f;
                return new Color(k, k * 0.985f, k * 0.95f);
            });
            rendN = NormalMap("T_Render_N", (u, v) => Fbm(u, v, 110, 2, 71), 1.1f);

            // Foliage: mottled green so canopies are not one solid mass.
            foliA = Albedo("T_Foliage_A", (u, v) =>
            {
                float k = Fbm(u, v, 14, 4, 83);
                return new Color(0.13f + k * 0.16f, 0.28f + k * 0.24f, 0.11f + k * 0.12f);
            });
        }

        private static void EnsureTextureFolder()
        {
            EnsureFolder(TextureFolder);
        }

        private static string DiskPath(string assetPath)
        {
            string projectRoot = Application.dataPath.Substring(
                0, Application.dataPath.Length - "Assets".Length);
            return projectRoot + assetPath;
        }

        private static Texture2D WritePng(string name, Color[] pixels, bool isNormalMap)
        {
            EnsureTextureFolder();
            string assetPath = TextureFolder + "/" + name + ".png";

            var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false);
            tex.SetPixels(pixels);
            tex.Apply();

            System.IO.File.WriteAllBytes(DiskPath(assetPath), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(assetPath);

            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = isNormalMap ? TextureImporterType.NormalMap
                                                   : TextureImporterType.Default;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Bilinear;
                importer.anisoLevel = 4;
                importer.mipmapEnabled = true;
                importer.maxTextureSize = TexSize;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        /// <summary>Albedo from a colour function over 0..1 UV space. Cached on disk.</summary>
        private static Texture2D Albedo(string name, System.Func<float, float, Color> fn)
        {
            string assetPath = TextureFolder + "/" + name + ".png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (existing != null) return existing;

            var px = new Color[TexSize * TexSize];
            for (int y = 0; y < TexSize; y++)
            {
                for (int x = 0; x < TexSize; x++)
                    px[y * TexSize + x] = fn(x / (float)TexSize, y / (float)TexSize);
            }
            return WritePng(name, px, false);
        }

        /// <summary>
        /// Normal map derived from a height function by central differences, then encoded.
        /// Sampling wraps, so the normal map tiles exactly as the albedo does.
        /// </summary>
        private static Texture2D NormalMap(string name, System.Func<float, float, float> height,
                                           float strength)
        {
            string assetPath = TextureFolder + "/" + name + ".png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (existing != null) return existing;

            float step = 1f / TexSize;
            var px = new Color[TexSize * TexSize];

            for (int y = 0; y < TexSize; y++)
            {
                float v = y / (float)TexSize;
                for (int x = 0; x < TexSize; x++)
                {
                    float u = x / (float)TexSize;

                    float hL = height(u - step, v), hR = height(u + step, v);
                    float hD = height(u, v - step), hU = height(u, v + step);

                    Vector3 n = new Vector3((hL - hR) * strength, (hD - hU) * strength, 1f).normalized;
                    px[y * TexSize + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f,
                                                    n.z * 0.5f + 0.5f, 1f);
                }
            }
            return WritePng(name, px, true);
        }

        private static float Frac(float v) { return v - Mathf.Floor(v); }

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
