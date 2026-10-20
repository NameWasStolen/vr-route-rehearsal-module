using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

namespace VRTutorial.EditorTools
{
    public static partial class VRTutorialSceneBuilder
    {
        /// <summary>
        /// Builds the full route scene: getting off a bus and walking through suburban streets
        /// to the nearest shopping centre. The layout lives in FullRoutePlan.cs; this class only
        /// turns that plan into meshes.
        ///
        /// Built on the same principles as the guided tutorial, and nested inside its builder so
        /// it shares the tutorial's helpers rather than copying them: the same procedural
        /// textures, the same shared materials (M_Stone_*, M_Grass_*, M_Asphalt ...), the same
        /// house, tree, bus shelter and bench geometry, and the same batching approach.
        ///
        /// FOOTSTEPS. Every walkable surface carries a collider and an explicit SurfaceMarker:
        /// footpaths and the forecourt are Paving, nature strips, lawns and yards are Grass, the
        /// road is Asphalt. With no Asphalt clips assigned, FootstepAudio falls back to Paving,
        /// which is right for a road. The materials also keep the tutorial's names, so the
        /// material-keyword fallback would resolve the same way even without the markers.
        ///
        /// Menu: Tools > VR Full Route > ...
        ///
        /// Research note: as in the tutorial, landmarks are behind a toggle (Options > Include
        /// Landmarks). Off, the landmark lamps become ordinary street lights and the special
        /// house becomes an ordinary house. The school, park, bus stop and shopping centre stay,
        /// because they are places on the route rather than cues.
        ///
        /// Performance: geometry is merged into one mesh per material per 48 m chunk, so it
        /// costs vertices, not draw calls, and chunks outside the view are still culled.
        /// Everything is static and ready for a lighting bake.
        /// </summary>
        public static class FullRoute
        {
            const string FRRoot       = "FullRouteEnvironment";
            const string FRFolder     = "Assets/FullRoute";
            const string FRGeneratedRoot = FRFolder + "/Generated";
            // One mesh folder per target scene, so rebuilding one never breaks another's meshes.
            static string FRGenerated = FRGeneratedRoot + "/Untitled";
            const string FRMaterials  = FRFolder + "/Materials";
            const string FRTextures   = FRFolder + "/Textures";
            const string FRSceneName  = "FullRoute";
            const string FRScenePath  = "Assets/Scenes/" + FRSceneName + ".unity";
            const string FRLandmarkKey = "VRFullRoute.IncludeLandmarks";
            const string RunSystemScenePath = "Assets/Scenes/RunSystem.unity";

            // Where the route sits inside RunSystem. The main menu stays loaded (hidden) during
            // a run, and its floor and panel are at the origin - right on top of the bus-stop
            // junction. 500 m south puts the menu well past the fog, so it is never seen.
            static readonly Vector3 RunSystemOffset = new Vector3(0f, 0f, -500f);
            const float  ChunkSize    = 48f;
            const float  CheckpointH  = 3f;

            // SurfaceKind enum order in SurfaceMarker.cs: Grass, Paving, Asphalt
            const int SurfGrass = 0, SurfPaving = 1, SurfAsphalt = 2;

            static bool IncludeLandmarksFR
            {
                get { return EditorPrefs.GetBool(FRLandmarkKey, true); }
                set { EditorPrefs.SetBool(FRLandmarkKey, value); }
            }

            // ------------------------------------------------------------ menu items
            [MenuItem("Tools/VR Full Route/Build Full Route Scene (new scene)", false, 0)]
            public static void BuildNewScene()
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

                if (System.IO.File.Exists(FRScenePath) &&
                    !EditorUtility.DisplayDialog("Build Full Route",
                        FRScenePath + " already exists. Replace it?", "Replace", "Cancel"))
                    return;

                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                BuildFR(FRSceneName, false);
                EditorSceneManager.SaveScene(scene, FRScenePath);
                AddSceneToBuildSettings(FRScenePath);
                Debug.Log("[FullRoute] Scene saved to " + FRScenePath + " and added to Build Settings.");
            }

            [MenuItem("Tools/VR Full Route/Build Full Route (current scene)", false, 1)]
            public static void BuildCurrentScene()
            {
                string name = EditorSceneManager.GetActiveScene().name;
                BuildFR(string.IsNullOrEmpty(name) ? "Untitled" : name, false);
            }

            /// <summary>
            /// Puts the route where the main menu's Guided and Unguided buttons go. Both buttons
            /// load RunSystem additively and call RunSystemController.StartRun, which moves the
            /// player to its run start point - so installing here needs no runtime code changes.
            ///
            /// What it does to RunSystem.unity:
            ///  - deactivates the old test map (Map1), keeping it in the scene in case it is wanted;
            ///  - builds the route under FullRouteEnvironment, offset away from the menu;
            ///  - adds a TimerController, makes CP_Start a START trigger (the timer starts as the
            ///    participant walks away from the bus stop) and CP_EndZone an END trigger (arriving
            ///    at the shopping centre ends the run and returns to the menu);
            ///  - makes every WrongTurn_* zone a WRONG_TURN trigger on a WrongTurnController;
            ///  - points RunSystemController's run start point at the bus stop.
            /// Safe to run again: it replaces the route and re-wires, nothing else.
            /// </summary>
            [MenuItem("Tools/VR Full Route/Install Route in RunSystem (Guided + Unguided)", false, 2)]
            public static void InstallInRunSystem()
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                if (!System.IO.File.Exists(RunSystemScenePath))
                {
                    EditorUtility.DisplayDialog("Install Full Route", RunSystemScenePath + " was not found.", "OK");
                    return;
                }

                var scene = EditorSceneManager.OpenScene(RunSystemScenePath, OpenSceneMode.Single);
                foreach (var go in scene.GetRootGameObjects())
                {
                    if (go.name == "Map1" && go.activeSelf)
                    {
                        go.SetActive(false);
                        Debug.Log("[FullRoute] Deactivated the old test map 'Map1' in RunSystem (kept, not deleted).");
                    }
                }

                var root = BuildFR("RunSystem", true);
                root.transform.position = RunSystemOffset;
                WireRunSystem(root);

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("[FullRoute] Installed in " + RunSystemScenePath + ". Guided and Unguided both start at the bus stop.");
            }

            /// <summary>
            /// Refreshes only the RouteDefinition on the route already in RunSystem - the walking
            /// line, nodes, branches and zones the run statistics read - without rebuilding the
            /// meshes. Install Route in RunSystem does this too, as part of a full rebuild.
            /// </summary>
            [MenuItem("Tools/VR Full Route/Update Route Definition in RunSystem", false, 3)]
            public static void UpdateRouteDefinitionInRunSystem()
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                var scene = EditorSceneManager.OpenScene(RunSystemScenePath, OpenSceneMode.Single);
                GameObject root = null;
                foreach (var go in scene.GetRootGameObjects())
                    if (go.name == FRRoot) root = go;
                if (root == null)
                {
                    EditorUtility.DisplayDialog("Update Route Definition",
                        "No " + FRRoot + " in RunSystem. Run Install Route in RunSystem first.", "OK");
                    return;
                }
                BakeRouteDefinition(root, new FullRoutePlan());
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Selection.activeGameObject = root;
            }

            [MenuItem("Tools/VR Full Route/Options/Include Landmarks", false, 10)]
            public static void ToggleLandmarksFR()
            {
                IncludeLandmarksFR = !IncludeLandmarksFR;
                Debug.Log("[FullRoute] Landmarks " + (IncludeLandmarksFR ? "ON" : "OFF") + " - rebuild to apply.");
            }

            [MenuItem("Tools/VR Full Route/Options/Include Landmarks", true)]
            public static bool ToggleLandmarksFRValidate()
            {
                Menu.SetChecked("Tools/VR Full Route/Options/Include Landmarks", IncludeLandmarksFR);
                return true;
            }

            [MenuItem("Tools/VR Full Route/Clear Full Route", false, 20)]
            public static void ClearFR()
            {
                var existing = GameObject.Find(FRRoot);
                if (existing != null) Undo.DestroyObjectImmediate(existing);
            }

            static void AddSceneToBuildSettings(string path)
            {
                var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
                foreach (var s in scenes) if (s.path == path) return;
                scenes.Add(new EditorBuildSettingsScene(path, true));
                EditorBuildSettings.scenes = scenes.ToArray();
            }

            // ============================================================== materials
            class Mats
            {
                public Material Asphalt, RoadLine, Kerb, FootBody, OuterGround, Hedge, Timber, Metal,
                                HouseRoof, Glass, Trim, Bark, LampHead, Tyre, PicketWhite;
                public Material[] Stone, Grass, Foliage, Cars, Walls, Roofs, Flowers;
                public Material HeritageGreen, Gold, SpecialWall, SpecialRoof, PalmFrond, Letterbox,
                                Brick, SchoolFence, ShopWall, ShopAccent, ShopGlassDark, Softfall,
                                PlayRed, PlayYellow, Planter, SignShopWide, SignShopTall, SignSchool, SignBus,
                                Tactile, SignCrossing, SignChildren, Globe, FlagOrange, PoleDark,
                                CarGlass, CarTrim, CarRim, CarHeadlight, CarTaillight, CarIndicator, CarPlate, CarPlateText;
                public Material[] CarPaint;
                // Shopping centre: the supermarket's brand colour, its fascia sign and the
                // window posters (Fruit, Veg, Bakery, Dairy, Specials).
                public Material BrandGreen, SignSupermarket;
                public Material[] Posters;
            }

            /// <summary>A tutorial material, loaded as-is. Created with the tutorial's own values
            /// only if it is missing, so building this scene never alters the tutorial's look.</summary>
            static Material Shared(string name, Color c, float smooth, Texture2D a = null, Texture2D n = null)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/" + name + ".mat");
                return m != null ? m : GetOrCreateMaterial(name, c, smooth, a, n);
            }

            static Material FRMat(string name, Color colour, float smoothness,
                                  Texture2D albedo = null, Texture2D normal = null, float emission = 0f)
            {
                EnsureFolder(FRMaterials);
                string path = FRMaterials + "/" + name + ".mat";
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
                if (albedo != null)
                {
                    if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", albedo);
                    if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", albedo);
                }
                if (normal != null && mat.HasProperty("_BumpMap"))
                {
                    mat.SetTexture("_BumpMap", normal);
                    mat.EnableKeyword("_NORMALMAP");
                }
                if (emission > 0f && albedo != null)
                {
                    // Signs glow faintly with their own image, so they stay legible in shade
                    // and from a low sun angle. Not a light source - it adds nothing to bakes.
                    if (mat.HasProperty("_EmissionMap")) mat.SetTexture("_EmissionMap", albedo);
                    if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", Color.white * emission);
                    mat.EnableKeyword("_EMISSION");
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                }
                EditorUtility.SetDirty(mat);
                return mat;
            }

            static Material SignMat(string name, string texture)
            {
                string path = FRTextures + "/" + texture;
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer != null && (importer.wrapMode != TextureWrapMode.Clamp || importer.anisoLevel < 8))
                {
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.anisoLevel = 8;
                    importer.mipmapEnabled = true;
                    importer.maxTextureSize = 1024;
                    importer.SaveAndReimport();
                }
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex == null)
                    Debug.LogWarning("[FullRoute] Sign texture missing: " + path + " - the sign will be a plain panel.");
                return FRMat(name, tex != null ? Color.white : new Color(0.2f, 0.4f, 0.5f), 0.25f, tex, null, 0.2f);
            }

            static Mats LoadMaterials()
            {
                Texture2D grassA, grassN, concA, concN, asphA, asphN, timbA, timbN, roofA, roofN, rendA, rendN, foliA;
                BuildTextures(out grassA, out grassN, out concA, out concN, out asphA, out asphN,
                              out timbA, out timbN, out roofA, out roofN, out rendA, out rendN, out foliA);

                var m = new Mats();
                // --- the tutorial's own materials, same names and values
                m.Stone = new[]
                {
                    Shared("M_Stone_0", new Color(0.635f, 0.6288f, 0.6025f), 0.10f, concA, concN),
                    Shared("M_Stone_1", new Color(0.66f, 0.65f, 0.62f), 0.10f, concA, concN),
                    Shared("M_Stone_2", new Color(0.685f, 0.6713f, 0.6375f), 0.10f, concA, concN),
                };
                m.Grass = new[]
                {
                    Shared("M_Grass_0", new Color(0.2775f, 0.4509f, 0.2043f), 0.05f, grassA, grassN),
                    Shared("M_Grass_1", new Color(0.30f, 0.47f, 0.22f), 0.05f, grassA, grassN),
                    Shared("M_Grass_2", new Color(0.3225f, 0.4891f, 0.2358f), 0.05f, grassA, grassN),
                };
                m.Foliage = new[]
                {
                    Shared("M_Foliage_0", new Color(0.1725f, 0.3566f, 0.1608f), 0.05f, foliA),
                    Shared("M_Foliage_1", new Color(0.20f, 0.38f, 0.18f), 0.05f, foliA),
                    Shared("M_Foliage_2", new Color(0.2275f, 0.4034f, 0.1993f), 0.05f, foliA),
                };
                m.Cars = new[]
                {
                    Shared("M_Car_0", new Color(0.51f, 0.5435f, 0.587f), 0.55f),
                    Shared("M_Car_1", new Color(0.60f, 0.62f, 0.65f), 0.55f),
                    Shared("M_Car_2", new Color(0.69f, 0.6965f, 0.713f), 0.55f),
                };
                m.Flowers = new[]
                {
                    Shared("M_Flower",      new Color(0.86f, 0.80f, 0.42f), 0.08f),
                    Shared("M_Flower_Pink", new Color(0.90f, 0.56f, 0.68f), 0.08f),
                    Shared("M_Flower_Coral",new Color(0.86f, 0.40f, 0.31f), 0.08f),
                };
                m.Asphalt     = Shared("M_Asphalt",     new Color(0.17f, 0.17f, 0.18f), 0.18f, asphA, asphN);
                m.RoadLine    = Shared("M_RoadLine",    new Color(0.88f, 0.86f, 0.76f), 0.10f);
                m.Kerb        = Shared("M_PathEdge",    new Color(0.55f, 0.54f, 0.51f), 0.12f, concA, concN);
                m.OuterGround = Shared("M_OuterGround", new Color(0.27f, 0.41f, 0.21f), 0.04f, grassA, grassN);
                m.Hedge       = Shared("M_Hedge",       new Color(0.16f, 0.31f, 0.15f), 0.04f, foliA);
                m.Timber      = Shared("M_Timber",      new Color(0.52f, 0.42f, 0.31f), 0.08f, timbA, timbN);
                m.Metal       = Shared("M_FenceMetal",  new Color(0.42f, 0.44f, 0.47f), 0.65f);
                m.HouseRoof   = Shared("M_HouseRoof",   new Color(0.36f, 0.31f, 0.30f), 0.10f, roofA, roofN);
                m.Glass       = Shared("M_Glass",       new Color(0.28f, 0.36f, 0.40f), 0.85f);
                m.Trim        = Shared("M_HouseTrim",   new Color(0.90f, 0.89f, 0.86f), 0.10f);
                m.Bark        = Shared("M_Bark",        new Color(0.34f, 0.26f, 0.19f), 0.05f, timbA);
                m.LampHead    = Shared("M_LampHead",    new Color(0.95f, 0.90f, 0.72f), 0.35f);
                m.Tyre        = Shared("M_Tyre",        new Color(0.10f, 0.10f, 0.11f), 0.10f);
                m.Walls = new[]
                {
                    Shared("M_HouseWall", new Color(0.78f, 0.74f, 0.67f), 0.08f, rendA, rendN),
                    FRMat("M_FR_Wall_Brick", new Color(0.55f, 0.33f, 0.26f), 0.06f, rendA, rendN),
                    FRMat("M_FR_Wall_Cream", new Color(0.86f, 0.82f, 0.70f), 0.08f, rendA, rendN),
                    FRMat("M_FR_Wall_Grey",  new Color(0.66f, 0.66f, 0.64f), 0.08f, rendA, rendN),
                };
                // The footpath body: the same M_Stone family the tutorial's paths use, so the
                // "stone" footstep keyword resolves it as paving even without the marker.
                m.FootBody = m.Stone[1];

                // --- new materials for this scene, kept in Assets/FullRoute/Materials
                m.Roofs = new[]
                {
                    m.HouseRoof,
                    FRMat("M_FR_Roof_Charcoal", new Color(0.24f, 0.24f, 0.25f), 0.12f, roofA, roofN),
                    FRMat("M_FR_Roof_Brown",    new Color(0.40f, 0.30f, 0.24f), 0.10f, roofA, roofN),
                };
                m.PicketWhite   = FRMat("M_FR_PicketWhite",   new Color(0.92f, 0.91f, 0.88f), 0.12f, timbA, timbN);
                m.HeritageGreen = FRMat("M_FR_HeritageGreen", new Color(0.08f, 0.30f, 0.20f), 0.45f);
                m.Gold          = FRMat("M_FR_Gold",          new Color(0.85f, 0.66f, 0.22f), 0.60f);
                // Strong colour for the special house, and nothing else on the route uses it.
                m.SpecialWall   = FRMat("M_FR_SpecialWall",   new Color(0.16f, 0.36f, 0.74f), 0.10f, rendA, rendN);
                m.SpecialRoof   = FRMat("M_FR_SpecialRoof",   new Color(0.66f, 0.20f, 0.13f), 0.12f, roofA, roofN);
                m.PalmFrond     = FRMat("M_FR_PalmFrond",     new Color(0.30f, 0.52f, 0.18f), 0.08f, foliA);
                m.Letterbox     = FRMat("M_FR_Letterbox",     new Color(0.97f, 0.76f, 0.08f), 0.40f);
                m.Brick         = FRMat("M_FR_SchoolBrick",   new Color(0.62f, 0.36f, 0.26f), 0.06f, rendA, rendN);
                m.SchoolFence   = FRMat("M_FR_SchoolFence",   new Color(0.10f, 0.32f, 0.20f), 0.45f);
                m.ShopWall      = FRMat("M_FR_ShopWall",      new Color(0.80f, 0.80f, 0.78f), 0.15f, concA, concN);
                m.ShopAccent    = FRMat("M_FR_ShopAccent",    new Color(0.06f, 0.36f, 0.42f), 0.30f);
                m.ShopGlassDark = FRMat("M_FR_ShopGlassDark", new Color(0.12f, 0.16f, 0.19f), 0.90f);
                m.Softfall      = FRMat("M_FR_Softfall",      new Color(0.46f, 0.24f, 0.20f), 0.05f, asphA);
                m.PlayRed       = FRMat("M_FR_PlayRed",       new Color(0.80f, 0.18f, 0.14f), 0.40f);
                m.PlayYellow    = FRMat("M_FR_PlayYellow",    new Color(0.95f, 0.75f, 0.12f), 0.40f);
                m.Planter       = FRMat("M_FR_Planter",       new Color(0.60f, 0.59f, 0.56f), 0.10f, concA, concN);
                m.SignShopWide  = SignMat("M_FR_Sign_ShoppingWide", "T_FR_Sign_Shopping_Wide.png");
                m.SignShopTall  = SignMat("M_FR_Sign_ShoppingTall", "T_FR_Sign_Shopping_Tall.png");
                m.SignSchool    = SignMat("M_FR_Sign_School",       "T_FR_Sign_School.png");
                // An invented supermarket brand ("FreshWay", green) - an Australian high-street
                // feel without copying any real chain's name or logo.
                m.BrandGreen      = FRMat("M_FR_BrandGreen", new Color(0.13f, 0.55f, 0.20f), 0.30f);
                m.SignSupermarket = SignMat("M_FR_Sign_Supermarket", "T_FR_Sign_Supermarket.png");
                m.Posters = new[]
                {
                    SignMat("M_FR_Poster_Fruit",    "T_FR_Poster_Fruit.png"),
                    SignMat("M_FR_Poster_Veg",      "T_FR_Poster_Veg.png"),
                    SignMat("M_FR_Poster_Bakery",   "T_FR_Poster_Bakery.png"),
                    SignMat("M_FR_Poster_Dairy",    "T_FR_Poster_Dairy.png"),
                    SignMat("M_FR_Poster_Specials", "T_FR_Poster_Specials.png"),
                };
                m.SignBus       = SignMat("M_FR_Sign_Bus",          "T_FR_Sign_Bus.png");
                m.SignCrossing  = SignMat("M_FR_Sign_Crossing",     "T_FR_Sign_Crossing.png");
                m.SignChildren  = SignMat("M_FR_Sign_ChildrenCrossing", "T_FR_Sign_ChildrenCrossing.png");
                // Tactile pads: the texture tiles (dome grid), unlike the sign faces.
                var tactileTex = AssetDatabase.LoadAssetAtPath<Texture2D>(FRTextures + "/T_FR_Tactile.png");
                // Tinted down from the raw texture: still strong contrast against the grey paving, but
                // no longer glaring in full sun. Matte, like real polymer pads.
                m.Tactile       = FRMat("M_FR_Tactile", tactileTex != null ? new Color(0.84f, 0.82f, 0.78f) : new Color(0.80f, 0.62f, 0.10f), 0.12f, tactileTex);
                m.Globe         = FRMat("M_FR_CrossingGlobe", new Color(1.00f, 0.55f, 0.10f), 0.60f);
                if (m.Globe.HasProperty("_EmissionColor"))
                {
                    m.Globe.SetColor("_EmissionColor", new Color(1.00f, 0.45f, 0.05f) * 0.45f);
                    m.Globe.EnableKeyword("_EMISSION");
                }
                m.FlagOrange    = FRMat("M_FR_FlagOrange", new Color(0.98f, 0.45f, 0.08f), 0.15f);

                // Cars: glossy paint in the colours of a real Australian street - white, silver,
                // grey and black first, then muted blue, red and bronze. Order matches the
                // weights in BuildCarsFR.
                m.CarPaint = new[]
                {
                    FRMat("M_FR_CarPaint_White",  new Color(0.90f, 0.90f, 0.89f), 0.70f),
                    FRMat("M_FR_CarPaint_Silver", new Color(0.66f, 0.67f, 0.69f), 0.75f),
                    FRMat("M_FR_CarPaint_Grey",   new Color(0.36f, 0.37f, 0.39f), 0.72f),
                    FRMat("M_FR_CarPaint_Black",  new Color(0.06f, 0.06f, 0.07f), 0.78f),
                    FRMat("M_FR_CarPaint_Blue",   new Color(0.13f, 0.22f, 0.40f), 0.72f),
                    FRMat("M_FR_CarPaint_Red",    new Color(0.52f, 0.08f, 0.07f), 0.72f),
                    FRMat("M_FR_CarPaint_Bronze", new Color(0.45f, 0.38f, 0.29f), 0.70f),
                };
                m.CarGlass      = FRMat("M_FR_CarGlass",     new Color(0.07f, 0.09f, 0.11f), 0.92f);
                m.CarTrim       = FRMat("M_FR_CarTrim",      new Color(0.09f, 0.09f, 0.10f), 0.25f);
                m.CarRim        = FRMat("M_FR_CarRim",       new Color(0.72f, 0.73f, 0.75f), 0.80f);
                m.CarHeadlight  = FRMat("M_FR_CarHeadlight", new Color(0.86f, 0.88f, 0.90f), 0.92f);
                m.CarTaillight  = FRMat("M_FR_CarTaillight", new Color(0.62f, 0.06f, 0.06f), 0.85f);
                m.CarIndicator  = FRMat("M_FR_CarIndicator", new Color(0.90f, 0.52f, 0.10f), 0.85f);
                m.CarPlate      = FRMat("M_FR_CarPlate",     new Color(0.93f, 0.93f, 0.91f), 0.35f);
                m.CarPlateText  = FRMat("M_FR_CarPlateText", new Color(0.08f, 0.12f, 0.32f), 0.30f);
                m.PoleDark      = FRMat("M_FR_PoleDark",   new Color(0.08f, 0.08f, 0.09f), 0.40f);
                return m;
            }

            // ============================================================== batching
            /// <summary>Raw triangles for one output mesh. Vertices are in world space.</summary>
            class Accum
            {
                public readonly List<Vector3> V = new List<Vector3>();
                public readonly List<int> T = new List<int>();
                public Material Mat; public float Tile = 2f; public string Group; public string Name;
                public bool Collider; public int Surface = -1;
            }

            /// <summary>
            /// Collects geometry per (name, chunk) so each chunk becomes one mesh and one draw
            /// call. Two kinds of source: raw quads written straight into an Accum (fast, used
            /// for everything this file draws), and GameObject batches for the tutorial's own
            /// helpers (houses, trees, shelter, benches), which build from primitives.
            /// </summary>
            class Batcher
            {
                readonly Dictionary<string, Accum> _acc = new Dictionary<string, Accum>();
                readonly Dictionary<string, Transform> _go = new Dictionary<string, Transform>();
                readonly Dictionary<string, Accum> _goInfo = new Dictionary<string, Accum>();
                readonly Transform _root;
                public Batcher(Transform root) { _root = root; }

                static string ChunkKey(Vector3 p)
                {
                    return Mathf.FloorToInt(p.x / ChunkSize) + "_" + Mathf.FloorToInt(p.z / ChunkSize);
                }

                public Accum Get(string group, string name, Material mat, Vector3 at, float tile = 2f,
                                 bool collider = false, int surface = -1)
                {
                    string key = name + "_" + ChunkKey(at);
                    Accum a;
                    if (!_acc.TryGetValue(key, out a))
                    {
                        a = new Accum { Mat = mat, Tile = tile, Group = group, Name = key, Collider = collider, Surface = surface };
                        _acc[key] = a;
                    }
                    return a;
                }

                public Transform GetGO(string group, string name, Material mat, Vector3 at, float tile = 2f)
                {
                    string key = name + "_" + ChunkKey(at);
                    Transform t;
                    if (!_go.TryGetValue(key, out t))
                    {
                        t = BeginBatch(_root);
                        _go[key] = t;
                        _goInfo[key] = new Accum { Mat = mat, Tile = tile, Group = group, Name = key };
                    }
                    return t;
                }

                public int Finish()
                {
                    var groups = new Dictionary<string, Transform>();
                    int meshes = 0;
                    System.Func<string, Transform> group = g =>
                    {
                        Transform t;
                        if (!groups.TryGetValue(g, out t)) { t = NewGroup(g, _root); groups[g] = t; }
                        return t;
                    };

                    foreach (var kv in _acc)
                    {
                        var a = kv.Value;
                        if (a.T.Count == 0) continue;
                        var mesh = new Mesh { name = "FR_" + a.Name, indexFormat = IndexFormat.UInt32 };
                        mesh.SetVertices(a.V);
                        mesh.SetTriangles(a.T, 0);
                        mesh.RecalculateNormals();
                        mesh.RecalculateBounds();
                        ApplyBoxUVs(mesh, a.Tile);
                        mesh.RecalculateTangents();
                        SaveMesh(mesh);
                        var go = new GameObject(a.Name);
                        go.transform.SetParent(group(a.Group), false);
                        go.AddComponent<MeshFilter>().sharedMesh = mesh;
                        go.AddComponent<MeshRenderer>().sharedMaterial = a.Mat;
                        if (a.Collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
                        if (a.Surface >= 0) Mark(go, a.Surface);
                        go.isStatic = true;
                        meshes++;
                    }

                    foreach (var kv in _go)
                    {
                        var info = _goInfo[kv.Key];
                        var filters = kv.Value.GetComponentsInChildren<MeshFilter>();
                        if (filters.Length == 0) { Object.DestroyImmediate(kv.Value.gameObject); continue; }
                        var combine = new CombineInstance[filters.Length];
                        for (int i = 0; i < filters.Length; i++)
                        {
                            combine[i].mesh = filters[i].sharedMesh;
                            combine[i].transform = kv.Value.worldToLocalMatrix * filters[i].transform.localToWorldMatrix;
                        }
                        var mesh = new Mesh { name = "FR_" + info.Name, indexFormat = IndexFormat.UInt32 };
                        mesh.CombineMeshes(combine, true, true);
                        mesh.RecalculateBounds();
                        ApplyBoxUVs(mesh, info.Tile);
                        mesh.RecalculateTangents();
                        SaveMesh(mesh);
                        Object.DestroyImmediate(kv.Value.gameObject);
                        var go = new GameObject(info.Name);
                        go.transform.SetParent(group(info.Group), false);
                        go.AddComponent<MeshFilter>().sharedMesh = mesh;
                        go.AddComponent<MeshRenderer>().sharedMaterial = info.Mat;
                        go.isStatic = true;
                        meshes++;
                    }
                    return meshes;
                }
            }

            static void SaveMesh(Mesh mesh)
            {
                string path = FRGenerated + "/" + mesh.name + ".asset";
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(mesh, path);
            }

            static void Mark(GameObject go, int surface)
            {
                // SurfaceMarker lives in the runtime assembly; added by name, the same way the
                // tutorial builder adds SceneSpawnPoint.
                var type = System.Type.GetType("SurfaceMarker, Assembly-CSharp");
                if (type == null) return;
                var c = go.AddComponent(type);
                var so = new SerializedObject(c);
                var p = so.FindProperty("kind");
                if (p != null) { p.enumValueIndex = surface; so.ApplyModifiedPropertiesWithoutUndo(); }
            }

            // ------------------------------------------------------ raw geometry helpers
            static void Quad(Accum a, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 outward)
            {
                if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), outward) < 0f)
                {
                    Vector3 t = p1; p1 = p3; p3 = t;
                }
                int i = a.V.Count;
                a.V.Add(p0); a.V.Add(p1); a.V.Add(p2); a.V.Add(p3);
                a.T.Add(i); a.T.Add(i + 1); a.T.Add(i + 2);
                a.T.Add(i); a.T.Add(i + 2); a.T.Add(i + 3);
            }

            /// <summary>An oriented box. 'fwd' is the box's local Z in plan; size is (x, y, z).</summary>
            static void Box(Accum a, Vector3 centre, Vector3 size, Vector3 fwd, bool bottom = false)
            {
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
                fwd.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                Vector3 hx = right * (size.x * 0.5f), hy = Vector3.up * (size.y * 0.5f), hz = fwd * (size.z * 0.5f);
                Vector3 c = centre;
                // top
                Quad(a, c + hy - hx - hz, c + hy - hx + hz, c + hy + hx + hz, c + hy + hx - hz, Vector3.up);
                if (bottom) Quad(a, c - hy - hx - hz, c - hy + hx - hz, c - hy + hx + hz, c - hy - hx + hz, Vector3.down);
                Quad(a, c - hx - hy - hz, c - hx + hy - hz, c - hx + hy + hz, c - hx - hy + hz, -right);
                Quad(a, c + hx - hy - hz, c + hx - hy + hz, c + hx + hy + hz, c + hx + hy - hz, right);
                Quad(a, c - hz - hx - hy, c - hz + hx - hy, c - hz + hx + hy, c - hz - hx + hy, -fwd);
                Quad(a, c + hz - hx - hy, c + hz - hx + hy, c + hz + hx + hy, c + hz + hx - hy, fwd);
            }

            /// <summary>
            /// A slab on a plan quad. Its top rises from 'topLow' along the edge nearest the
            /// road (the lowest corners along 'up', which points away from the road) to
            /// 'topHigh' along the far edge; pass up = zero for a level top at topHigh.
            /// </summary>
            static void Prism(Accum a, Quad4 q, Vector2 up, float topLow, float topHigh, float thickness)
            {
                var c = q.Corners();
                float[] h = new float[4];
                if (up.sqrMagnitude > 1e-6f)
                {
                    float lo = float.MaxValue, hi = float.MinValue;
                    for (int i = 0; i < 4; i++) { float d = Vector2.Dot(c[i], up); lo = Mathf.Min(lo, d); hi = Mathf.Max(hi, d); }
                    for (int i = 0; i < 4; i++)
                    {
                        float t = hi - lo > 1e-4f ? (Vector2.Dot(c[i], up) - lo) / (hi - lo) : 1f;
                        h[i] = Mathf.Lerp(topLow, topHigh, t);
                    }
                }
                else for (int i = 0; i < 4; i++) h[i] = topHigh;
                float bot = Mathf.Min(Mathf.Min(h[0], h[1]), Mathf.Min(h[2], h[3])) - thickness;
                Quad(a, W(c[0], h[0]), W(c[1], h[1]), W(c[2], h[2]), W(c[3], h[3]), Vector3.up);
                Vector2 centre = q.Centre;
                for (int i = 0; i < 4; i++)
                {
                    int j = (i + 1) % 4;
                    Vector2 mid = (c[i] + c[j]) * 0.5f;
                    Quad(a, W(c[i], bot), W(c[j], bot), W(c[j], h[j]), W(c[i], h[i]), W3(mid - centre));
                }
            }

            static Vector3 W(Vector2 p, float y) { return new Vector3(p.x, y, p.y); }
            static Vector3 W3(Vector2 d) { return new Vector3(d.x, 0f, d.y); }

            /// <summary>
            /// A slab following a street between arc lengths s0 and s1 and side offsets off0 and
            /// off1, mitred at bends. Top face, both long sides and both end caps.
            /// </summary>
            static void Ribbon(Accum a, RouteStreet st, float s0, float s1, float off0, float off1,
                               float top, float thickness)
            {
                if (s1 - s0 < 0.01f) return;
                var e0 = st.Edge(s0, s1, off0);
                var e1 = st.Edge(s0, s1, off1);
                float bot = top - thickness;
                for (int i = 0; i < e0.Count - 1; i++)
                {
                    Vector2 a0 = e0[i], a1 = e0[i + 1], b0 = e1[i], b1 = e1[i + 1];
                    if ((a1 - a0).sqrMagnitude < 1e-8f && (b1 - b0).sqrMagnitude < 1e-8f) continue;
                    Quad(a, W(a0, top), W(a1, top), W(b1, top), W(b0, top), Vector3.up);
                    Vector3 outA = W3(((a0 + a1) - (b0 + b1)).normalized);
                    Quad(a, W(a0, bot), W(a0, top), W(a1, top), W(a1, bot), outA);
                    Quad(a, W(b0, bot), W(b0, top), W(b1, top), W(b1, bot), -outA);
                }
                int n = e0.Count - 1;
                Vector3 back = W3((e0[0] - e0[1]).normalized);
                Quad(a, W(e0[0], bot), W(e0[0], top), W(e1[0], top), W(e1[0], bot), back);
                Vector3 fore = W3((e0[n] - e0[n - 1]).normalized);
                Quad(a, W(e0[n], bot), W(e0[n], top), W(e1[n], top), W(e1[n], bot), fore);
            }

            // ================================================================== build
            /// <summary>
            /// Builds the route into the active scene for the main menu's Map button (the
            /// tabletop model). Used by Tools > VR Full Route > Build Route Map Scene
            /// (RouteMapSetup.cs). Its meshes go to their own folder, so it never touches
            /// RunSystem's. Built like RunSystem's copy - no light or spawn point of its own -
            /// and with no run wiring. Pass the plan in to reuse it for the map's labels.
            /// </summary>
            public static GameObject BuildForRouteMap(FullRoutePlan plan)
            {
                return BuildFR("RouteMap", true, plan);
            }

            /// <summary>Options > Include Landmarks, for the map's labels.</summary>
            public static bool LandmarksIncluded { get { return IncludeLandmarksFR; } }

            static GameObject BuildFR(string meshFolderName, bool forRunSystem, FullRoutePlan planIn = null)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                FRGenerated = FRGeneratedRoot + "/" + meshFolderName;
                var plan = planIn ?? new FullRoutePlan();
                bool landmarks = IncludeLandmarksFR;

                EnsureFolder(FRFolder);
                EnsureFolder(FRMaterials);
                EnsureFolder(FRGeneratedRoot);
                // Generated meshes are rebuilt every time; clear this scene's old set so chunks
                // that no longer exist do not linger as orphaned assets.
                if (AssetDatabase.IsValidFolder(FRGenerated)) AssetDatabase.DeleteAsset(FRGenerated);
                EnsureFolder(FRGenerated);

                var old = GameObject.Find(FRRoot);
                if (old != null) Object.DestroyImmediate(old);
                var root = new GameObject(FRRoot);
                Undo.RegisterCreatedObjectUndo(root, "Build Full Route");

                var m = LoadMaterials();
                var b = new Batcher(root.transform);
                var rng = new System.Random(FullRouteLayout.Seed + 1);

                BuildWorldSettings();       // the tutorial's sky, ambient and fog
                BuildBaseGround(root.transform, plan, m);
                BuildStreetSurfaces(b, plan, m, rng);
                BuildCourts(b, plan, m, rng);
                BuildCrossings(b, root.transform, plan, m);
                BuildFences(b, root.transform, plan, m, landmarks, rng);
                BuildClosures(b, root.transform, plan, m);
                BuildEndCaps(b, root.transform, plan, m);

                Random.InitState(FullRouteLayout.Seed + 11);
                BuildHousesFR(b, plan, m);
                BuildStreetTreesFR(b, root.transform, plan, m);
                BuildGardensFR(b, plan, m);
                BuildLampsFR(b, root.transform, plan, m, landmarks);
                BuildCarsFR(b, root.transform, plan, m);
                BuildBusStop(b, root.transform, plan, m);
                BuildPark(b, root.transform, plan, m);
                BuildSchool(b, root.transform, plan, m);
                BuildShoppingCentre(b, root.transform, plan, m);
                if (landmarks) BuildSpecialHouse(b, plan, m);
                else BuildOrdinaryHouseOnSpecialLot(b, plan, m);

                int meshes = b.Finish();
                BuildTriggersFR(root.transform, plan);
                BuildSpawnAndLightFR(root.transform, plan, forRunSystem);
                BakeRouteDefinition(root, plan);

                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(root.scene);
                Selection.activeGameObject = root;

                Debug.Log(string.Format(
                    "[FullRoute] Built in {0:0.0} s: {1} meshes, {2} houses, {3} street lights, {4} landmark lamps, " +
                    "{5} cul-de-sac dead ends, {12} T-intersection ends, {13} L-corner ends, {6} planter closures, " +
                    "{7} zebra crossings, {8} kerb ramps. " +
                    "Route bus stop -> shopping centre is {9:0} m, about {10:0.0} min at 1.5 m/s. Landmarks {11}. " +
                    "Bake lighting for the intended look.",
                    sw.Elapsed.TotalSeconds, meshes, plan.Houses.Count, plan.Lamps.Count, plan.LandmarkLamps.Count,
                    plan.Courts.Count, plan.Closures.Count, plan.Zebras.Count, plan.Openings.Count,
                    FullRouteLayout.RouteLength, FullRouteLayout.RouteLength / 1.5f / 60f,
                    landmarks ? "INCLUDED" : "OMITTED", plan.TeeEnds.Count, plan.ElbowEnds.Count));
                foreach (var w in plan.Warnings) Debug.LogWarning("[FullRoute] " + w);
                return root;
            }

            // -------------------------------------------------------- run system
            static void WireRunSystem(GameObject root)
            {
                System.Type timerT = System.Type.GetType("TimerController, Assembly-CSharp");
                System.Type triggerT = System.Type.GetType("TriggerController, Assembly-CSharp");
                System.Type wrongT = System.Type.GetType("WrongTurnController, Assembly-CSharp");
                System.Type runT = System.Type.GetType("RunSystemController, Assembly-CSharp");
                if (timerT == null || triggerT == null || wrongT == null || runT == null)
                {
                    Debug.LogError("[FullRoute] Run system scripts not found (TimerController, TriggerController, " +
                                   "WrongTurnController, RunSystemController). The route was built but not wired.");
                    return;
                }

                var wiring = NewGroup("RunSystemWiring", root.transform);
                var timerGo = new GameObject("RunTimer");
                timerGo.transform.SetParent(wiring, false);
                var timer = timerGo.AddComponent(timerT);
                var wrongGo = new GameObject("WrongTurnLog");
                wrongGo.transform.SetParent(wiring, false);
                var wrong = wrongGo.AddComponent(wrongT);

                // TriggerController.TriggerType: START = 0, END = 1, WRONG_TURN = 2
                Transform cps = root.transform.Find("Checkpoints");
                int wired = 0;
                if (cps != null)
                {
                    wired += SetTrigger(cps.Find("CP_Start"), triggerT, 0, timer, null);
                    wired += SetTrigger(cps.Find("CP_EndZone"), triggerT, 1, timer, null);
                }
                Transform zones = root.transform.Find("WrongTurnZones");
                if (zones != null)
                    foreach (Transform z in zones) wired += SetTrigger(z, triggerT, 2, null, wrong);

                Transform spawn = root.transform.Find("PlayerSpawn");
                int controllers = 0;
                foreach (var c in Object.FindObjectsByType(runT, FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    var so = new SerializedObject(c);
                    var p = so.FindProperty("runStartPoint");
                    if (p == null) continue;
                    p.objectReferenceValue = spawn;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    controllers++;
                }

                if (controllers == 0)
                    Debug.LogWarning("[FullRoute] No RunSystemController found in the scene - the run start point was not set.");
                Debug.Log("[FullRoute] Run system wired: " + wired + " triggers, " + controllers +
                          " RunSystemController start point(s) set to the bus stop.");
            }

            static int SetTrigger(Transform t, System.Type triggerT, int kind, Component timer, Component wrong)
            {
                if (t == null) return 0;
                var c = t.gameObject.AddComponent(triggerT);
                var so = new SerializedObject(c);
                var k = so.FindProperty("triggerType");
                if (k != null) k.enumValueIndex = kind;
                var tp = so.FindProperty("timerController");
                if (tp != null) tp.objectReferenceValue = timer;
                var wp = so.FindProperty("wrongTurnController");
                if (wp != null) wp.objectReferenceValue = wrong;
                so.ApplyModifiedPropertiesWithoutUndo();
                return 1;
            }

            // ------------------------------------------------------------ base ground
            static void BuildBaseGround(Transform root, FullRoutePlan plan, Mats m)
            {
                var group = NewGroup("Ground", root);
                Vector2 mn = plan.BoundsMin, mx = plan.BoundsMax;
                Vector3 size = new Vector3(mx.x - mn.x, 0.4f, mx.y - mn.y);
                Mesh mesh = MakeBoxMesh(size);
                mesh.name = "FR_BaseGround";
                ApplyBoxUVs(mesh, 4f);
                mesh.RecalculateTangents();
                SaveMesh(mesh);

                var go = new GameObject("BaseGround");
                go.transform.SetParent(group, false);
                go.transform.localPosition = new Vector3((mn.x + mx.x) * 0.5f, FullRouteLayout.BaseGroundTop - 0.2f, (mn.y + mx.y) * 0.5f);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = m.OuterGround;
                go.AddComponent<BoxCollider>().size = size;
                Mark(go, SurfGrass);
                go.isStatic = true;
            }

            // -------------------------------------------------------- street surfaces
            static void BuildStreetSurfaces(Batcher b, FullRoutePlan plan, Mats m, System.Random rng)
            {
                const string G = "Streets";
                foreach (var band in plan.Bands)
                {
                    var st = plan.Streets[band.Street];
                    Vector3 at = W(st.Point((band.S0 + band.S1) * 0.5f), 0f);
                    switch (band.Kind)
                    {
                        case BandKind.Road:
                            Ribbon(b.Get(G, "Road", m.Asphalt, at, 3f, true, SurfAsphalt),
                                   st, band.S0, band.S1, band.Off0, band.Off1, FullRouteLayout.RoadTop, 0.12f);
                            break;
                        case BandKind.Nature:
                            Ribbon(b.Get(G, "NatureStrip_" + (st.Index % 3), m.Grass[st.Index % 3], at, 3f, true, SurfGrass),
                                   st, band.S0, band.S1, band.Off0, band.Off1, FullRouteLayout.NatureTop, 0.10f);
                            break;
                        case BandKind.Kerb:
                            Ribbon(b.Get(G, "Kerb", m.Kerb, at, 1f),
                                   st, band.S0, band.S1, band.Off0, band.Off1, FullRouteLayout.KerbTop, 0.20f);
                            break;
                        case BandKind.Dash:
                            Ribbon(b.Get(G, "RoadMarking", m.RoadLine, at),
                                   st, band.S0, band.S1, band.Off0, band.Off1, FullRouteLayout.RoadTop + 0.004f, 0.01f);
                            break;
                        case BandKind.Pad:
                        case BandKind.Footpath:
                            // Body: the walkable collider, marked as paving.
                            Ribbon(b.Get(G, "FootpathBody", m.Kerb, at, 1.2f, true, SurfPaving),
                                   st, band.S0, band.S1, band.Off0, band.Off1, FullRouteLayout.FootBodyTop, 0.10f);
                            // Slabs laid on it with a joint about every 1.6 m, drawn from the
                            // tutorial's three greys. Visual only - the body underneath takes the
                            // footsteps.
                            //
                            // Laid along the band's own mitred edges, not by arc length along the
                            // street (2 Oct 2026). Since v5.2 a footpath runs through its street's
                            // bends as one band, and on the inside of a bend the last few metres of
                            // arc length before the corner (and the first few after it) have no
                            // footpath: the mitre has already turned. Slabs cut by arc length were
                            // drawn there anyway, straight on past the corner, so near the school
                            // (N10, N11) and at every other inside corner a run of slabs stuck out
                            // across the nature strip and onto the road. The body was right; only
                            // the slabs on top were wrong.
                            //
                            // The variants are still drawn from rng exactly as before, so the
                            // shared stream - and everything built after this - is unchanged.
                            const float pitch = 1.6f, joint = 0.018f;
                            var variants = new List<int>();
                            for (float s = band.S0; s < band.S1 - 0.05f; s += pitch)
                                variants.Add(rng.Next(0, m.Stone.Length));
                            if (variants.Count == 0) break;
                            LaySlabs(b, G, m, at, st, band, pitch, joint, variants);
                            break;
                    }
                }

                // A kerb along both sides of each street end, so a street finishing at a fence
                // does not show the raw end of the asphalt.
                foreach (var st in plan.Streets)
                {
                    if (!st.OpenEnd && !st.OpenStart) continue;
                    for (int e = 0; e < 2; e++)
                    {
                        if (e == 0 ? !st.OpenEnd : !st.OpenStart) continue;
                        float s = e == 0 ? st.Length : 0f;
                        Vector2 d = e == 0 ? st.Dir(st.SegmentCount - 1) : -st.Dir(0);
                        Vector2 p = st.Point(s);
                        Box(b.Get(G, "Kerb", m.Kerb, W(p, 0f), 1f),
                            W(p - d * 0.08f, FullRouteLayout.KerbTop - 0.1f),
                            new Vector3(FullRouteLayout.KerbEdge * 2f, 0.2f, 0.16f), W3(d));
                    }
                }
            }

            /// <summary>
            /// Footpath slabs over one band, following its mitred edges: each straight piece of
            /// the band (between two of its bends) is cut into slabs of about 'pitch' metres, so
            /// a slab never reaches past the mitre on the inside of a bend. A bend is always a
            /// slab joint.
            /// </summary>
            static void LaySlabs(Batcher b, string group, Mats m, Vector3 at, RouteStreet st, Band band,
                                 float pitch, float joint, List<int> variants)
            {
                var e0 = st.Edge(band.S0, band.S1, band.Off0 + joint);
                var e1 = st.Edge(band.S0, band.S1, band.Off1 - joint);
                int k = 0;
                for (int i = 0; i < e0.Count - 1; i++)
                {
                    Vector2 a0 = e0[i], a1 = e0[i + 1], b0 = e1[i], b1 = e1[i + 1];
                    float len = (Vector2.Distance(a0, a1) + Vector2.Distance(b0, b1)) * 0.5f;
                    if (len < 0.05f) continue;
                    int n = Mathf.Max(1, Mathf.RoundToInt(len / pitch));
                    float dt = Mathf.Min(0.45f / n, joint * 0.5f / len);
                    for (int j = 0; j < n; j++)
                    {
                        float t0 = (float)j / n + dt, t1 = (float)(j + 1) / n - dt;
                        var q = new Quad4(Vector2.Lerp(a0, a1, t0), Vector2.Lerp(a0, a1, t1),
                                          Vector2.Lerp(b0, b1, t1), Vector2.Lerp(b0, b1, t0));
                        int v = variants[k++ % variants.Count];
                        Prism(b.Get(group, "FootpathSlabs_" + v, m.Stone[v], at, 1.2f), q, Vector2.zero,
                              FullRouteLayout.FootSlabTop, FullRouteLayout.FootSlabTop, 0.012f);
                    }
                }
            }

            // -------------------------------------------------------------- courts
            /// <summary>An annular slab (or disk, with r0 = 0) between angles a0 and a1.</summary>
            static void Ring(Accum acc, Vector2 c, float r0, float r1, float a0, float a1, float top, float thickness)
            {
                if (a1 - a0 < 1e-3f) return;
                int n = Mathf.Max(2, Mathf.CeilToInt((a1 - a0) / (4f * Mathf.Deg2Rad)));
                float bot = top - thickness;
                System.Func<float, float, Vector2> at = (r, a) => c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                for (int i = 0; i < n; i++)
                {
                    float t0 = Mathf.Lerp(a0, a1, i / (float)n), t1 = Mathf.Lerp(a0, a1, (i + 1) / (float)n);
                    Vector2 i0 = at(r0, t0), i1 = at(r0, t1), o0 = at(r1, t0), o1 = at(r1, t1);
                    Quad(acc, W(i0, top), W(i1, top), W(o1, top), W(o0, top), Vector3.up);
                    Vector3 outN = W3(at(1f, (t0 + t1) * 0.5f) - c);
                    Quad(acc, W(o0, bot), W(o0, top), W(o1, top), W(o1, bot), outN);
                    if (r0 > 0.05f) Quad(acc, W(i0, bot), W(i0, top), W(i1, top), W(i1, bot), -outN);
                }
                bool full = a1 - a0 > 2f * Mathf.PI - 1e-3f;
                if (!full)
                {
                    Vector3 s0 = W3(at(1f, a0 - Mathf.PI * 0.5f) - c), s1 = W3(at(1f, a1 + Mathf.PI * 0.5f) - c);
                    Quad(acc, W(at(r0, a0), bot), W(at(r0, a0), top), W(at(r1, a0), top), W(at(r1, a0), bot), s0);
                    Quad(acc, W(at(r0, a1), bot), W(at(r0, a1), top), W(at(r1, a1), top), W(at(r1, a1), bot), s1);
                }
            }

            /// <summary>
            /// The cul-de-sacs: an asphalt turning circle, kerb, nature strip and a footpath all
            /// the way round, laid in slabs like every other footpath. Same layers, heights,
            /// materials and surface markers as the streets, so footsteps sound the same.
            /// </summary>
            static void BuildCourts(Batcher b, FullRoutePlan plan, Mats m, System.Random rng)
            {
                const string G = "Streets";
                const float pitch = 1.6f, joint = 0.018f;
                foreach (var r in plan.Rings)
                {
                    var court = plan.Courts[r.Court];
                    Vector2 c = court.C;
                    Vector3 at = W(c, 0f);
                    int st = court.Street;
                    switch (r.Kind)
                    {
                        case BandKind.Road:
                            Ring(b.Get(G, "Road", m.Asphalt, at, 3f, true, SurfAsphalt), c, r.R0, r.R1, r.A0, r.A1, FullRouteLayout.RoadTop, 0.12f);
                            break;
                        case BandKind.Nature:
                            Ring(b.Get(G, "NatureStrip_" + (st % 3), m.Grass[st % 3], at, 3f, true, SurfGrass), c, r.R0, r.R1, r.A0, r.A1, FullRouteLayout.NatureTop, 0.10f);
                            break;
                        case BandKind.Kerb:
                            Ring(b.Get(G, "Kerb", m.Kerb, at, 1f), c, r.R0, r.R1, r.A0, r.A1, FullRouteLayout.KerbTop, 0.20f);
                            break;
                        case BandKind.Footpath:
                            // A few millimetres below the street footpaths, which run on into the
                            // ring at each entry: where they overlap, the street's slabs show.
                            const float drop = FullRouteLayout.CourtFootDrop;
                            Ring(b.Get(G, "FootpathBody", m.Kerb, at, 1.2f, true, SurfPaving), c, r.R0, r.R1, r.A0, r.A1, FullRouteLayout.FootBodyTop - drop, 0.10f);
                            float mid = (r.R0 + r.R1) * 0.5f;
                            float step = pitch / mid, gapA = joint / mid;
                            for (float a = r.A0; a < r.A1 - 0.01f; a += step)
                            {
                                int v = rng.Next(0, m.Stone.Length);
                                Ring(b.Get(G, "FootpathSlabs_" + v, m.Stone[v], at, 1.2f), c, r.R0 + joint, r.R1 - joint,
                                     a + gapA * 0.5f, Mathf.Min(a + step, r.A1) - gapA * 0.5f, FullRouteLayout.FootSlabTop - drop, 0.012f);
                            }
                            break;
                    }
                }
            }

            // ------------------------------------------------------------ crossings
            /// <summary>
            /// Kerb ramps (paving from the road edge through the nature strip), yellow tactile
            /// pads at the top of every ramp and at every footpath end that meets a road, zebra
            /// stripes, and a post each side of each zebra with an orange globe and a pictogram
            /// sign - children crossing outside the school, the pedestrian symbol elsewhere.
            /// </summary>
            static void BuildCrossings(Batcher b, Transform root, FullRoutePlan plan, Mats m)
            {
                const string G = "Crossings";
                // Ramps: the slope rises from just above the asphalt to footpath height a
                // little past the kerb line, so there is no lip at the road edge. The flat part
                // sits 3 mm under the footpath slabs, so where they overlap the footpath wins
                // cleanly instead of flickering.
                const float roadTop = FullRouteLayout.RoadTop, body = FullRouteLayout.FootBodyTop;
                const float slabTop = FullRouteLayout.FootSlabTop - 0.003f;
                foreach (var o in plan.Openings)
                {
                    Vector3 at = W(o.Flat.Centre, 0f);
                    var bodyAcc = b.Get("Streets", "FootpathBody", m.Kerb, at, 1.2f, true, SurfPaving);
                    var slabAcc = b.Get("Streets", "FootpathSlabs_1", m.Stone[1], at, 1.2f);
                    Prism(bodyAcc, o.Slope, o.Out, roadTop + 0.001f, body, 0.10f);
                    Prism(bodyAcc, o.Flat,  o.Out, body, body, 0.10f);
                    Prism(slabAcc, o.Slope, o.Out, roadTop + 0.0015f, slabTop, 0.012f);
                    Prism(slabAcc, o.Flat,  o.Out, slabTop, slabTop, 0.012f);
                }

                // Pads sit on the flat, 6 mm proud: enough to read as a raised mat without
                // becoming a trip edge. One pad per ramp, none overlapping.
                foreach (var t in plan.Tactiles)
                    Prism(b.Get(G, "Tactile", m.Tactile, W(t.Centre, 0f), 0.3f), t, Vector2.zero,
                          slabTop + 0.006f, slabTop + 0.006f, 0.02f);

                foreach (var z in plan.ZebraStripes)
                    Box(b.Get(G, "ZebraStripes", m.RoadLine, W(z.C, 0f)),
                        W(z.C, FullRouteLayout.RoadTop + 0.003f), new Vector3(z.HV * 2f, 0.008f, z.HU * 2f), W3(z.U));

                var group = NewGroup("CrossingSigns", root);
                int i = 0;
                foreach (var post in plan.CrossingPosts)
                {
                    Vector3 bp = W(post.Pos, FullRouteLayout.NatureTop);
                    const float H = 2.9f;
                    var dark = b.GetGO(G, "CrossingPoleDark", m.PoleDark, bp);
                    var light = b.GetGO(G, "CrossingPoleLight", m.PicketWhite, bp);
                    // Banded pole, so it reads as a crossing post from down the street.
                    int bands = 7;
                    for (int k = 0; k < bands; k++)
                    {
                        float h0 = k * (H / bands);
                        AddPrimitive(k % 2 == 0 ? dark : light, PrimitiveType.Cylinder, bp + Vector3.up * (h0 + H / bands * 0.5f),
                                     new Vector3(0.09f, H / bands * 0.5f, 0.09f), Quaternion.identity);
                    }
                    AddPrimitive(b.GetGO(G, "CrossingGlobe", m.Globe, bp), PrimitiveType.Sphere, bp + Vector3.up * (H + 0.18f),
                                 Vector3.one * 0.36f, Quaternion.identity);
                    SignFace(group, "CrossingSign_" + i, post.School ? m.SignChildren : m.SignCrossing,
                             bp + Vector3.up * 2.15f, W3(post.RoadDir), 0.6f, 0.6f, true);
                    AddPrimitive(dark, PrimitiveType.Cube, bp + Vector3.up * 2.15f, new Vector3(0.64f, 0.64f, 0.05f),
                                 Quaternion.LookRotation(W3(post.RoadDir), Vector3.up));
                    if (post.School)
                    {
                        // Orange flags on a short arm, the school-crossing signal.
                        Vector3 side = Vector3.Cross(Vector3.up, W3(post.RoadDir));
                        var flag = b.GetGO(G, "CrossingFlags", m.FlagOrange, bp);
                        AddPrimitive(dark, PrimitiveType.Cube, bp + Vector3.up * 2.62f + side * 0.2f, new Vector3(0.03f, 0.03f, 0.5f),
                                     Quaternion.LookRotation(side, Vector3.up));
                        AddPrimitive(flag, PrimitiveType.Cube, bp + Vector3.up * 2.4f + side * 0.42f, new Vector3(0.02f, 0.40f, 0.55f),
                                     Quaternion.LookRotation(W3(post.RoadDir), Vector3.up));
                    }
                    LampCollider(group, "CrossingPost_" + (i++), bp, H, 0.12f);
                }
            }

            // -------------------------------------------------------------- fences
            enum FrontKind { Picket, Wall, Hedge, Paling }

            static void BuildFences(Batcher b, Transform root, FullRoutePlan plan, Mats m, bool landmarks, System.Random rng)
            {
                var colliders = NewGroup("Fence_Colliders", root);
                int colliderIndex = 0;

                foreach (var f in plan.Fences)
                {
                    var style = f.Style;
                    if (style == FenceStyle.SpecialPicket && !landmarks) style = FenceStyle.Front;

                    // A front boundary changes style every lot or so - picket, low rendered wall,
                    // clipped hedge - which is what makes a street read as many households.
                    float along = 0f;
                    float lotLen = 12f + (float)rng.NextDouble() * 6f;
                    FrontKind kind = (FrontKind)rng.Next(0, 3);

                    for (int i = 0; i < f.Pts.Count - 1; i++)
                    {
                        Vector2 a = f.Pts[i], c = f.Pts[i + 1];
                        float len = Vector2.Distance(a, c);
                        if (len < 0.02f) continue;
                        Vector2 d = (c - a) / len;

                        float height = FenceHeight(style);
                        AddFenceCollider(colliders, "Fence_" + (colliderIndex++).ToString("000"), a, c, height);

                        float t = 0f;
                        while (t < len - 0.01f)
                        {
                            float piece = Mathf.Min(len - t, lotLen - along);
                            Vector2 p0 = a + d * t, p1 = a + d * (t + piece);
                            DrawFence(b, m, style, kind, p0, p1);
                            t += piece; along += piece;
                            if (along >= lotLen - 0.01f)
                            {
                                along = 0f; lotLen = 12f + (float)rng.NextDouble() * 6f;
                                kind = (FrontKind)rng.Next(0, 3);
                            }
                        }
                    }
                }
            }

            static float FenceHeight(FenceStyle s)
            {
                switch (s)
                {
                    case FenceStyle.School:   return 1.8f;
                    case FenceStyle.ParkBack: return 1.8f;
                    case FenceStyle.Hedge:    return 1.5f;
                    case FenceStyle.Screen:   return 2.2f;
                    default:                  return 1.1f;
                }
            }

            static void AddFenceCollider(Transform parent, string name, Vector2 a, Vector2 c, float height)
            {
                float len = Vector2.Distance(a, c);
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = W((a + c) * 0.5f, height * 0.5f);
                go.transform.localRotation = Quaternion.LookRotation(W3(c - a), Vector3.up);
                var box = go.AddComponent<BoxCollider>();
                // Never lower than 1.1 m: the CharacterController steps 0.3 m and a low
                // fence must still stop it, even where the visual is a knee-high wall.
                box.size = new Vector3(0.2f, Mathf.Max(1.1f, height), len + 0.1f);
                go.isStatic = true;
            }

            static void DrawFence(Batcher b, Mats m, FenceStyle style, FrontKind kind, Vector2 a, Vector2 c)
            {
                float len = Vector2.Distance(a, c);
                if (len < 0.05f) return;
                Vector3 at = W(a, 0f);
                const string G = "Fences";

                switch (style)
                {
                    case FenceStyle.School:
                        Palisade(b.Get(G, "SchoolFence", m.SchoolFence, at), a, c, 1.8f, 0.13f);
                        break;
                    case FenceStyle.SpecialPicket:
                        Picket(b.Get(G, "PicketWhite", m.PicketWhite, at, 1f), a, c, 1.0f);
                        break;
                    case FenceStyle.ParkLow:
                        PostAndRail(b.Get(G, "ParkRail", m.Timber, at, 1f), a, c, 0.9f);
                        break;
                    case FenceStyle.ParkBack:
                        Paling(b.Get(G, "Paling", m.Timber, at, 1f), a, c, 1.8f);
                        break;
                    case FenceStyle.Hedge:
                        HedgeRun(b.Get(G, "Hedge", m.Hedge, at, 1.5f), a, c, 1.5f);
                        break;
                    case FenceStyle.Screen:
                        // Inside corner of a dead end's bend: tall enough to block eye level.
                        HedgeRun(b.Get(G, "Hedge", m.Hedge, at, 1.5f), a, c, 2.2f);
                        break;
                    default:
                        switch (kind)
                        {
                            case FrontKind.Picket: Picket(b.Get(G, "PicketWhite", m.PicketWhite, at, 1f), a, c, 1.0f); break;
                            case FrontKind.Wall:   LowWall(b.Get(G, "LowWall", m.Walls[0], at), b.Get(G, "LowWallCap", m.Kerb, at, 1f), a, c); break;
                            default:               HedgeRun(b.Get(G, "Hedge", m.Hedge, at, 1.5f), a, c, 1.1f); break;
                        }
                        break;
                }
            }

            static void Posts(Accum acc, Vector2 a, Vector2 c, float h, float spacing, float size)
            {
                float len = Vector2.Distance(a, c);
                Vector3 fwd = W3((c - a) / len);
                int n = Mathf.Max(1, Mathf.RoundToInt(len / spacing));
                for (int i = 0; i <= n; i++)
                {
                    Vector2 p = Vector2.Lerp(a, c, i / (float)n);
                    Box(acc, W(p, h * 0.5f), new Vector3(size, h, size), fwd);
                }
            }

            static void Rail(Accum acc, Vector2 a, Vector2 c, float y, float w, float h)
            {
                float len = Vector2.Distance(a, c);
                Box(acc, W((a + c) * 0.5f, y), new Vector3(w, h, len), W3(c - a));
            }

            static void Picket(Accum acc, Vector2 a, Vector2 c, float h)
            {
                float len = Vector2.Distance(a, c);
                Vector3 fwd = W3((c - a) / len);
                Posts(acc, a, c, h + 0.08f, 2.4f, 0.10f);
                Rail(acc, a, c, h * 0.30f, 0.04f, 0.07f);
                Rail(acc, a, c, h * 0.78f, 0.04f, 0.07f);
                int n = Mathf.Max(1, Mathf.FloorToInt(len / 0.15f));
                for (int i = 0; i < n; i++)
                {
                    Vector2 p = Vector2.Lerp(a, c, (i + 0.5f) / n);
                    Box(acc, W(p, h * 0.5f) + Vector3.Cross(Vector3.up, fwd) * 0.03f, new Vector3(0.022f, h, 0.085f), fwd);
                }
            }

            static void Palisade(Accum acc, Vector2 a, Vector2 c, float h, float pitch)
            {
                float len = Vector2.Distance(a, c);
                Vector3 fwd = W3((c - a) / len);
                Posts(acc, a, c, h + 0.1f, 2.4f, 0.08f);
                Rail(acc, a, c, 0.18f, 0.05f, 0.05f);
                Rail(acc, a, c, h - 0.18f, 0.05f, 0.05f);
                int n = Mathf.Max(1, Mathf.FloorToInt(len / pitch));
                for (int i = 0; i < n; i++)
                {
                    Vector2 p = Vector2.Lerp(a, c, (i + 0.5f) / n);
                    Box(acc, W(p, h * 0.5f + 0.02f), new Vector3(0.025f, h, 0.025f), fwd);
                }
            }

            static void PostAndRail(Accum acc, Vector2 a, Vector2 c, float h)
            {
                Posts(acc, a, c, h, 2.0f, 0.12f);
                Rail(acc, a, c, h - 0.08f, 0.06f, 0.10f);
                Rail(acc, a, c, h * 0.45f, 0.06f, 0.10f);
            }

            static void Paling(Accum acc, Vector2 a, Vector2 c, float h)
            {
                float len = Vector2.Distance(a, c);
                Vector3 fwd = W3((c - a) / len);
                Posts(acc, a, c, h + 0.05f, 2.5f, 0.10f);
                int n = Mathf.Max(1, Mathf.FloorToInt(len / 0.16f));
                for (int i = 0; i < n; i++)
                {
                    Vector2 p = Vector2.Lerp(a, c, (i + 0.5f) / n);
                    float hh = h - Mathf.Abs(Mathf.Sin(i * 12.9898f)) * 0.035f;
                    Box(acc, W(p, hh * 0.5f), new Vector3(0.03f, hh, 0.145f), fwd);
                }
            }

            static void LowWall(Accum wall, Accum cap, Vector2 a, Vector2 c)
            {
                float len = Vector2.Distance(a, c);
                Vector3 fwd = W3((c - a) / len);
                Box(wall, W((a + c) * 0.5f, 0.35f), new Vector3(0.22f, 0.70f, len), fwd);
                Box(cap, W((a + c) * 0.5f, 0.73f), new Vector3(0.30f, 0.06f, len + 0.04f), fwd);
                int n = Mathf.Max(1, Mathf.RoundToInt(len / 3f));
                for (int i = 0; i <= n; i++)
                    Box(wall, W(Vector2.Lerp(a, c, i / (float)n), 0.45f), new Vector3(0.34f, 0.90f, 0.34f), fwd);
            }

            static void HedgeRun(Accum acc, Vector2 a, Vector2 c, float h)
            {
                float len = Vector2.Distance(a, c);
                Vector3 fwd = W3((c - a) / len);
                // A body and a narrower crown, broken into short lengths of slightly varying
                // height, so the top line is not ruled. Boxes, not spheres: a sphere is 500
                // vertices and this runs for hundreds of metres.
                int n = Mathf.Max(1, Mathf.RoundToInt(len / 1.2f));
                for (int i = 0; i < n; i++)
                {
                    Vector2 p0 = Vector2.Lerp(a, c, i / (float)n), p1 = Vector2.Lerp(a, c, (i + 1) / (float)n);
                    float seg = Vector2.Distance(p0, p1);
                    float hh = h + Mathf.Sin(i * 7.31f + a.x) * 0.06f;
                    float w = 0.62f + Mathf.Sin(i * 3.17f + a.y) * 0.04f;
                    Box(acc, W((p0 + p1) * 0.5f, (hh - 0.12f) * 0.5f), new Vector3(w, hh - 0.12f, seg + 0.02f), fwd);
                    Box(acc, W((p0 + p1) * 0.5f, hh - 0.08f), new Vector3(w * 0.8f, 0.16f, seg * 0.92f), fwd);
                }
            }

            // ----------------------------------------------------------- closures
            /// <summary>
            /// Soft end of a side street: a planter across the road with low post-and-rail
            /// fence across the verges. Reads as a council street closure - a real thing in
            /// Melbourne - rather than as the program being broken. The collider spans the whole
            /// reserve, property line to property line.
            /// </summary>
            static void BuildClosures(Batcher b, Transform root, FullRoutePlan plan, Mats m)
            {
                var colliders = NewGroup("Closure_Colliders", root);
                const float HR = FullRouteLayout.HalfReserve;
                foreach (var c in plan.Closures)
                {
                    var st = plan.Streets[c.Street];
                    int seg = st.Seg(c.S);
                    Vector2 p = st.Point(c.S), d = st.Dir(seg), l = st.Left(seg);
                    Vector3 at = W(p, 0f);

                    var planter = b.Get("Closures", "Planter", m.Planter, at, 1f);
                    Box(planter, W(p, 0.24f), new Vector3(FullRouteLayout.HalfCarriageway * 2f - 0.3f, 0.48f, 1.3f), W3(d));

                    var shrubs = b.GetGO("Closures", "PlanterShrubs", m.Foliage[1], at);
                    for (int i = 0; i < 5; i++)
                    {
                        float x = -2.4f + i * 1.2f;
                        AddMesh(shrubs, BlobVariant(i + c.Street), W(p + l * x, 0.62f),
                                new Vector3(1.05f, 0.62f, 0.95f), Quaternion.Euler(0f, i * 57f, 0f));
                    }

                    var rail = b.Get("Closures", "ClosureRail", m.Timber, at, 1f);
                    for (int side = -1; side <= 1; side += 2)
                        PostAndRail(rail, p + l * (side * (FullRouteLayout.HalfCarriageway - 0.1f)), p + l * (side * HR), 1.0f);

                    var go = new GameObject("Closure_" + c.Name + "_" + colliders.childCount);
                    go.transform.SetParent(colliders, false);
                    go.transform.localPosition = W(p, 0.6f);
                    go.transform.localRotation = Quaternion.LookRotation(W3(d), Vector3.up);
                    go.AddComponent<BoxCollider>().size = new Vector3(HR * 2f + 0.2f, 1.2f, 1.3f);
                    go.isStatic = true;
                }
            }

            /// <summary>A hedge across the far end of each side street, beyond its soft end.</summary>
            static void BuildEndCaps(Batcher b, Transform root, FullRoutePlan plan, Mats m)
            {
                var colliders = NewGroup("EndCap_Colliders", root);
                const float HR = FullRouteLayout.HalfReserve;
                foreach (var c in plan.EndCaps)
                {
                    var st = plan.Streets[c.Street];
                    bool atEnd = c.S > 0.01f;
                    int seg = atEnd ? st.SegmentCount - 1 : 0;
                    Vector2 d = atEnd ? st.Dir(seg) : -st.Dir(seg);
                    Vector2 l = new Vector2(-d.y, d.x);
                    Vector2 p = st.Point(c.S) + d * 0.5f;
                    HedgeRun(b.Get("Fences", "Hedge", m.Hedge, W(p, 0f), 1.5f), p - l * (HR + 0.1f), p + l * (HR + 0.1f), 1.6f);
                    AddFenceCollider(colliders, "EndCap_" + c.Name, p - l * (HR + 0.1f), p + l * (HR + 0.1f), 1.6f);
                }
            }

            // -------------------------------------------------------------- houses
            static void BuildHousesFR(Batcher b, FullRoutePlan plan, Mats m)
            {
                foreach (var h in plan.Houses) AddSizedHouse(b, m, h, m.Walls, m.Roofs, 1);
            }

            /// <summary>
            /// The tutorial's house (walls, hip or gable roof, fascia, chimney, door, windows,
            /// porch, garage), with its size set by the plan so it is known to fit its lot.
            /// Roof, gable and fascia geometry is the tutorial's own.
            /// </summary>
            static float AddSizedHouse(Batcher b, Mats m, HouseSpot spot, Material[] walls, Material[] roofs, int storeys,
                                       Material wallOverride = null, Material roofOverride = null)
            {
                Random.State prev = Random.state;
                Random.InitState(spot.Seed);

                Vector3 frontage = W(spot.Frontage, 0f);
                Vector3 facing = W3(spot.Facing).normalized;
                int wv = Random.Range(0, walls.Length), rv = Random.Range(0, roofs.Length);
                Material wallMat = wallOverride != null ? wallOverride : walls[wv];
                Material roofMat = roofOverride != null ? roofOverride : roofs[rv];
                string wallKey = wallOverride != null ? "SpecialWall" : "HouseWall_" + wv;
                string roofKey = roofOverride != null ? "SpecialRoof" : "HouseRoof_" + rv;

                const string G = "Houses";
                var wallBatch  = b.GetGO(G, wallKey, wallMat, frontage);
                var roofBatch  = b.GetGO(G, roofKey, roofMat, frontage, 1.2f);
                var glassBatch = b.GetGO(G, "HouseGlass", m.Glass, frontage);
                var trimBatch  = b.GetGO(G, "HouseTrim", m.Trim, frontage);

                float w = spot.Width, d = spot.Depth;
                float storeyH = Random.Range(2.6f, 3.0f);
                float h = storeyH * storeys;
                float slope = Random.Range(22f, 30f);
                bool hipRoof = storeys > 1 || Random.Range(0f, 1f) < 0.45f;
                const float eaveX = 0.55f;

                Quaternion rot = Quaternion.LookRotation(facing, Vector3.up);
                Vector3 side = Vector3.Cross(Vector3.up, facing).normalized;
                Vector3 centre = frontage - facing * (d * 0.5f);

                AddPrimitive(wallBatch, PrimitiveType.Cube, centre + Vector3.up * (h * 0.5f), new Vector3(w, h, d), rot);
                AddMesh(roofBatch, MakeRoofMesh(w, d, eaveX, slope, 0.16f, hipRoof), centre + Vector3.up * h, Vector3.one, rot);
                if (!hipRoof) AddGableInfill(wallBatch, centre, h, w, d, eaveX, slope, rot, facing, side);

                {
                    float Wd = w * 0.5f + eaveX, Dd = d * 0.5f + eaveX;
                    for (int s = -1; s <= 1; s += 2)
                        AddPrimitive(trimBatch, PrimitiveType.Cube, centre + side * (s * Wd) + Vector3.up * (h - 0.04f),
                                     new Vector3(0.09f, 0.20f, Dd * 2f), rot);
                    if (hipRoof)
                        for (int s = -1; s <= 1; s += 2)
                            AddPrimitive(trimBatch, PrimitiveType.Cube, centre + facing * (s * Dd) + Vector3.up * (h - 0.04f),
                                         new Vector3(Wd * 2f, 0.20f, 0.09f), rot);
                }

                if (storeys == 1 && Random.Range(0f, 1f) < 0.5f)
                {
                    float Wd = w * 0.5f + eaveX, Dd = d * 0.5f + eaveX;
                    bool ridgeAlongZ = Dd >= Wd;
                    float across = ridgeAlongZ ? Wd : Dd;
                    float rise = across * Mathf.Tan(slope * Mathf.Deg2Rad);
                    float cx = Random.Range(0.18f, 0.32f) * w * (Random.Range(0, 2) == 0 ? 1f : -1f);
                    float roofYAtC = h + rise * (1f - Mathf.Abs(cx) / across);
                    float ch = Random.Range(0.9f, 1.4f);
                    float cz = Random.Range(-1.5f, 1.5f);
                    AddPrimitive(wallBatch, PrimitiveType.Cube, centre + side * cx + facing * cz + Vector3.up * (roofYAtC + ch * 0.5f - 0.2f),
                                 new Vector3(0.75f, ch, 0.75f), rot);
                    AddPrimitive(trimBatch, PrimitiveType.Cube, centre + side * cx + facing * cz + Vector3.up * (roofYAtC + ch - 0.16f),
                                 new Vector3(0.92f, 0.12f, 0.92f), rot);
                }

                Vector3 front = centre + facing * (d * 0.5f + 0.04f);
                AddPrimitive(trimBatch, PrimitiveType.Cube, front + side * (w * 0.02f) + Vector3.up * 1.05f, new Vector3(1.0f, 2.1f, 0.07f), rot);
                AddPrimitive(glassBatch, PrimitiveType.Cube, front + side * (w * 0.02f) + Vector3.up * 1.72f, new Vector3(0.62f, 0.5f, 0.05f), rot);

                for (int f = 0; f < storeys; f++)
                {
                    for (int s = -1; s <= 1; s += 2)
                    {
                        if (f == 0 && w < 8.5f && s < 0) continue;   // narrow house: door plus one window
                        Vector3 wp = front + side * (s * w * 0.28f) + Vector3.up * (f * storeyH + storeyH * 0.6f);
                        float ww = Mathf.Min(w * 0.25f, 2.6f);
                        AddPrimitive(trimBatch, PrimitiveType.Cube, wp, new Vector3(ww + 0.16f, storeyH * 0.34f + 0.16f, 0.05f), rot);
                        AddPrimitive(glassBatch, PrimitiveType.Cube, wp + facing * 0.02f, new Vector3(ww, storeyH * 0.34f, 0.05f), rot);
                    }
                }

                if (storeys == 1 && Random.Range(0f, 1f) < 0.55f)
                {
                    float pd = 1.5f;
                    AddPrimitive(roofBatch, PrimitiveType.Cube, front + facing * (pd * 0.5f) + Vector3.up * (h - 0.30f),
                                 new Vector3(w * 0.42f, 0.14f, pd), rot * Quaternion.Euler(8f, 0f, 0f));
                    for (int s = -1; s <= 1; s += 2)
                        AddPrimitive(trimBatch, PrimitiveType.Cube,
                                     front + facing * (pd - 0.1f) + side * (s * w * 0.19f) + Vector3.up * ((h - 0.35f) * 0.5f),
                                     new Vector3(0.13f, h - 0.35f, 0.13f), rot);
                }

                if (spot.GarageSide != 0)
                {
                    float gw = 3.2f, gd = 5.5f, gh = 2.5f;
                    float gs = spot.GarageSide;
                    Vector3 gCentre = centre + side * (gs * (w * 0.5f + gw * 0.5f - 0.15f)) - facing * (d * 0.5f - gd * 0.5f);
                    AddPrimitive(wallBatch, PrimitiveType.Cube, gCentre + Vector3.up * (gh * 0.5f), new Vector3(gw, gh, gd), rot);
                    AddMesh(roofBatch, MakeRoofMesh(gw, gd, 0.30f, slope, 0.14f, false), gCentre + Vector3.up * gh, Vector3.one, rot);
                    AddGableInfill(wallBatch, gCentre, gh, gw, gd, 0.30f, slope, rot, facing, side);
                    AddPrimitive(trimBatch, PrimitiveType.Cube, gCentre + facing * (gd * 0.5f + 0.04f) + Vector3.up * (gh * 0.42f),
                                 new Vector3(gw * 0.82f, gh * 0.72f, 0.07f), rot);
                }

                Random.state = prev;
                return storeyH;
            }

            // --------------------------------------------------------------- trees
            static void BuildStreetTreesFR(Batcher b, Transform root, FullRoutePlan plan, Mats m)
            {
                var colliders = NewGroup("StreetTree_Colliders", root);
                int i = 0;
                foreach (var p in plan.Trees)
                {
                    Vector3 pos = W(p, FullRouteLayout.NatureTop);
                    float height = Random.Range(5.2f, 7.2f);
                    float canopy = Random.Range(1.35f, 1.85f);
                    AddTree(b.GetGO("Trees", "TreeTrunks", m.Bark, pos),
                            b.GetGO("Trees", "TreeCanopy_" + (i % 3), m.Foliage[i % 3], pos), pos, height, canopy);

                    var col = new GameObject("StreetTreeCollider_" + (i++).ToString("000"));
                    col.transform.SetParent(colliders, false);
                    col.transform.localPosition = pos + Vector3.up * (height * 0.5f);
                    var capsule = col.AddComponent<CapsuleCollider>();
                    capsule.radius = 0.26f; capsule.height = height;
                    col.isStatic = true;
                }
            }

            static void BuildGardensFR(Batcher b, FullRoutePlan plan, Mats m)
            {
                int i = 0;
                foreach (var g in plan.GardenShrubs)
                {
                    Vector3 pos = W(new Vector2(g.x, g.y), FullRouteLayout.BaseGroundTop);
                    float s = g.z;
                    int v = i % 3;
                    AddMesh(b.GetGO("Gardens", "Shrubs_" + v, m.Foliage[v], pos), BlobVariant(i),
                            pos + Vector3.up * (s * 0.28f), new Vector3(s, s * 0.72f, s * 0.9f),
                            Quaternion.Euler(0f, (i * 73) % 360, 0f));
                    // Every fifth shrub is in flower.
                    if (i % 5 == 0)
                    {
                        var fb = b.GetGO("Gardens", "Flowers_" + (i % 3), m.Flowers[i % 3], pos);
                        for (int k = 0; k < 5; k++)
                        {
                            float ang = k * 72f * Mathf.Deg2Rad;
                            AddPrimitive(fb, PrimitiveType.Cube,
                                         pos + new Vector3(Mathf.Sin(ang) * s * 0.30f, s * 0.58f, Mathf.Cos(ang) * s * 0.28f),
                                         Vector3.one * 0.12f, Quaternion.Euler(0f, k * 31f, 0f));
                        }
                    }
                    i++;
                }
                foreach (var p in plan.GardenTrees)
                {
                    Vector3 pos = W(p, FullRouteLayout.BaseGroundTop);
                    AddTree(b.GetGO("Gardens", "GardenTrunks", m.Bark, pos),
                            b.GetGO("Gardens", "GardenCanopy_" + (i % 3), m.Foliage[i % 3], pos),
                            pos, Random.Range(4.5f, 7.5f), Random.Range(1.4f, 2.2f));
                    i++;
                }
            }

            // --------------------------------------------------------------- lamps
            static void BuildLampsFR(Batcher b, Transform root, FullRoutePlan plan, Mats m, bool landmarks)
            {
                var colliders = NewGroup("Lamp_Colliders", root);
                int i = 0;
                foreach (var l in plan.Lamps) AddStreetLamp(b, colliders, m, l.Pos, l.Arm, i++);

                foreach (var p in plan.LandmarkLamps)
                {
                    if (landmarks) AddHeritageLamp(b, colliders, m, p, i++);
                    else
                    {
                        // Same spot, ordinary light: the arm points at the nearest road.
                        Vector2 arm = Vector2.zero; float best = float.MaxValue;
                        foreach (var st in plan.Streets)
                        {
                            for (float s = 0f; s <= st.Length; s += 1f)
                            {
                                float dd = Vector2.Distance(st.Point(s), p);
                                if (dd < best) { best = dd; arm = (st.Point(s) - p).normalized; }
                            }
                        }
                        AddStreetLamp(b, colliders, m, p, arm, i++);
                    }
                }
            }

            /// <summary>The tutorial's roadside light: tall post, curved arm, flat head.</summary>
            static void AddStreetLamp(Batcher b, Transform colliders, Mats m, Vector2 p, Vector2 arm, int i)
            {
                const float H = 5.4f;
                Vector3 basePos = W(p, FullRouteLayout.NatureTop);
                Vector3 armDir = W3(arm).normalized;
                Quaternion rot = Quaternion.LookRotation(armDir, Vector3.up);
                var post = b.GetGO("Lamps", "StreetLampPosts", m.Metal, basePos);
                var head = b.GetGO("Lamps", "StreetLampHeads", m.LampHead, basePos);

                AddPrimitive(post, PrimitiveType.Cylinder, basePos + Vector3.up * (H * 0.5f), new Vector3(0.17f, H * 0.5f, 0.17f), Quaternion.identity);
                for (int s = 0; s < 3; s++)
                {
                    float f = s / 2f;
                    AddPrimitive(post, PrimitiveType.Cube, basePos + Vector3.up * (H - 0.10f - f * 0.16f) + armDir * (0.35f + f * 0.55f),
                                 new Vector3(0.09f, 0.09f, 0.62f), rot);
                }
                AddPrimitive(head, PrimitiveType.Cube, basePos + Vector3.up * (H - 0.44f) + armDir * 1.55f, new Vector3(0.44f, 0.13f, 0.78f), rot);
                LampCollider(colliders, "Lamp_" + i.ToString("000"), basePos, H, 0.17f);
            }

            /// <summary>
            /// Landmark lamp - the stars on the drawing. A heritage post in dark green and gold
            /// with a lantern and two hanging flower baskets: nothing else on the route looks
            /// like it, and it needs no English. Deliberately shorter than the street lights
            /// (3.9 m), so the lantern and baskets sit close to eye level where a participant
            /// looking along the footpath will actually see them.
            /// </summary>
            static void AddHeritageLamp(Batcher b, Transform colliders, Mats m, Vector2 p, int i)
            {
                Vector3 bp = W(p, FullRouteLayout.NatureTop);
                const string G = "LandmarkLamps";
                var green = b.GetGO(G, "HeritageGreen", m.HeritageGreen, bp);
                var gold  = b.GetGO(G, "HeritageGold", m.Gold, bp);
                var glass = b.GetGO(G, "HeritageLantern", m.LampHead, bp);
                var fol   = b.GetGO(G, "HeritageBaskets", m.Foliage[2], bp);
                var flw   = b.GetGO(G, "HeritageFlowers", m.Flowers[1], bp);
                var flw2  = b.GetGO(G, "HeritageFlowers2", m.Flowers[2], bp);

                AddPrimitive(green, PrimitiveType.Cylinder, bp + Vector3.up * 0.30f, new Vector3(0.36f, 0.30f, 0.36f), Quaternion.identity);
                AddPrimitive(green, PrimitiveType.Cylinder, bp + Vector3.up * 0.75f, new Vector3(0.24f, 0.18f, 0.24f), Quaternion.identity);
                AddPrimitive(green, PrimitiveType.Cylinder, bp + Vector3.up * 2.2f, new Vector3(0.13f, 1.35f, 0.13f), Quaternion.identity);
                foreach (float y in new[] { 0.62f, 0.95f, 1.9f, 3.45f })
                    AddPrimitive(gold, PrimitiveType.Cylinder, bp + Vector3.up * y, new Vector3(0.22f, 0.025f, 0.22f), Quaternion.identity);

                // Lantern: glass box, green frame, pyramid cap, gold finial.
                AddPrimitive(glass, PrimitiveType.Cube, bp + Vector3.up * 3.72f, new Vector3(0.34f, 0.44f, 0.34f), Quaternion.Euler(0f, 45f, 0f));
                AddPrimitive(green, PrimitiveType.Cube, bp + Vector3.up * 3.49f, new Vector3(0.42f, 0.05f, 0.42f), Quaternion.Euler(0f, 45f, 0f));
                AddPrimitive(green, PrimitiveType.Cube, bp + Vector3.up * 3.96f, new Vector3(0.44f, 0.06f, 0.44f), Quaternion.Euler(0f, 45f, 0f));
                AddPrimitive(green, PrimitiveType.Cube, bp + Vector3.up * 4.08f, new Vector3(0.26f, 0.26f, 0.26f), Quaternion.Euler(45f, 45f, 0f));
                AddPrimitive(gold, PrimitiveType.Sphere, bp + Vector3.up * 4.30f, Vector3.one * 0.10f, Quaternion.identity);

                // Cross arm with a basket on each end.
                Quaternion armRot = Quaternion.Euler(0f, (i * 37f) % 180f, 0f);
                Vector3 armDir = armRot * Vector3.forward;
                AddPrimitive(gold, PrimitiveType.Cube, bp + Vector3.up * 2.95f, new Vector3(0.05f, 0.05f, 1.4f), armRot);
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 hook = bp + Vector3.up * 2.95f + armDir * (s * 0.62f);
                    AddPrimitive(gold, PrimitiveType.Cube, hook + Vector3.down * 0.18f, new Vector3(0.02f, 0.36f, 0.02f), Quaternion.identity);
                    Vector3 basket = hook + Vector3.down * 0.52f;
                    AddMesh(fol, BlobVariant(i + s + 3), basket, new Vector3(0.62f, 0.46f, 0.62f), Quaternion.Euler(0f, i * 40f, 0f));
                    for (int k = 0; k < 7; k++)
                    {
                        float ang = k * (360f / 7f) * Mathf.Deg2Rad;
                        var target = k % 2 == 0 ? flw : flw2;
                        AddPrimitive(target, PrimitiveType.Sphere,
                                     basket + new Vector3(Mathf.Sin(ang) * 0.27f, 0.10f + (k % 3) * 0.05f - (k == 3 ? 0.30f : 0f), Mathf.Cos(ang) * 0.27f),
                                     Vector3.one * 0.14f, Quaternion.identity);
                    }
                }
                LampCollider(colliders, "LandmarkLamp_" + i.ToString("000"), bp, 3.9f, 0.20f);
            }

            static void LampCollider(Transform parent, string name, Vector3 basePos, float h, float r)
            {
                var col = new GameObject(name);
                col.transform.SetParent(parent, false);
                col.transform.localPosition = basePos + Vector3.up * (h * 0.5f);
                var capsule = col.AddComponent<CapsuleCollider>();
                capsule.radius = r; capsule.height = h;
                col.isStatic = true;
            }

            // ---------------------------------------------------------------- cars
            // ---------------------------------------------------------------- cars
            // Parked cars, built from raw quads so they batch with everything else (about 1.5k
            // triangles each). Four body types - sedan, hatchback, SUV and dual-cab ute - so
            // the street reads as a real Australian suburb. Every car has a clear front (grille,
            // headlights, plate, bonnet sloping down, windscreen raked back) and back (red tail
            // lights, boot or tailgate), so the way it faces is obvious at a glance.
            //
            // Local car space: +z is the front of the car, +x its right, y up from the road.
            // The plan's CarSpot.Dir already obeys Road Rule 208 (parked facing the way traffic
            // on that side of the road travels, kerb on the car's left), and the car is rotated
            // so +z = Dir.

            enum CarType { Sedan, Hatch, Suv, Ute }

            /// <summary>One cross-section of the lower body: z along the car, half width, sill and top.</summary>
            struct CarStation
            {
                public float Z, HW, Y0, Y1;
                public CarStation(float z, float hw, float y0, float y1) { Z = z; HW = hw; Y0 = y0; Y1 = y1; }
            }

            /// <summary>Dimensions and profile of one body type.</summary>
            class CarSpec
            {
                public CarType Type;
                public float Length, HalfWidth, Roof, WheelR, AxleF, AxleR;
                public CarStation[] Body;          // front to rear is not required - sorted by Z
                // Cabin (greenhouse): windscreen base z, windscreen top z, roof end z, rear glass
                // base z; belt and roof heights; half widths at belt and roof (tumblehome).
                public float CabF0, CabF1, CabR1, CabR0, Belt, CabHWBelt, CabHWRoof;
                public float BPillar;              // z of the B pillar, splitting the side glass
                public bool RoofRails, Tub;        // SUV rails; ute tray
            }

            static readonly CarSpec[] CarSpecs =
            {
                // Sedan - a family sedan, 4.6 m. Long bonnet, separate boot.
                new CarSpec
                {
                    Type = CarType.Sedan, Length = 4.60f, HalfWidth = 0.90f, Roof = 1.44f, WheelR = 0.31f, AxleF = 1.36f, AxleR = -1.38f,
                    Body = new[]
                    {
                        new CarStation(-2.30f, 0.78f, 0.36f, 0.84f), new CarStation(-2.17f, 0.88f, 0.28f, 0.98f),
                        new CarStation(-1.10f, 0.90f, 0.26f, 0.98f), new CarStation( 1.05f, 0.90f, 0.26f, 0.98f),
                        new CarStation( 2.12f, 0.87f, 0.28f, 0.80f), new CarStation( 2.30f, 0.74f, 0.36f, 0.66f),
                    },
                    CabF0 = 1.05f, CabF1 = 0.22f, CabR1 = -0.55f, CabR0 = -1.10f, Belt = 0.98f, CabHWBelt = 0.80f, CabHWRoof = 0.64f,
                    BPillar = -0.05f,
                },
                // Hatchback - a small city car, 4.05 m. Short nose, steep rear hatch.
                new CarSpec
                {
                    Type = CarType.Hatch, Length = 4.05f, HalfWidth = 0.87f, Roof = 1.47f, WheelR = 0.30f, AxleF = 1.25f, AxleR = -1.30f,
                    Body = new[]
                    {
                        new CarStation(-2.025f, 0.76f, 0.36f, 0.84f), new CarStation(-1.93f, 0.85f, 0.28f, 0.95f),
                        new CarStation( 0.95f, 0.87f, 0.26f, 0.95f), new CarStation( 1.82f, 0.84f, 0.28f, 0.79f),
                        new CarStation( 2.025f, 0.72f, 0.36f, 0.64f),
                    },
                    CabF0 = 0.95f, CabF1 = 0.15f, CabR1 = -1.30f, CabR0 = -1.93f, Belt = 0.95f, CabHWBelt = 0.77f, CabHWRoof = 0.62f,
                    BPillar = -0.20f,
                },
                // SUV / wagon - 4.6 m, tall, square back, roof rails.
                new CarSpec
                {
                    Type = CarType.Suv, Length = 4.60f, HalfWidth = 0.93f, Roof = 1.70f, WheelR = 0.36f, AxleF = 1.34f, AxleR = -1.34f,
                    Body = new[]
                    {
                        new CarStation(-2.30f, 0.84f, 0.44f, 0.96f), new CarStation(-2.20f, 0.92f, 0.36f, 1.06f),
                        new CarStation( 1.10f, 0.93f, 0.34f, 1.06f), new CarStation( 2.10f, 0.90f, 0.36f, 0.94f),
                        new CarStation( 2.30f, 0.78f, 0.44f, 0.82f),
                    },
                    CabF0 = 1.10f, CabF1 = 0.30f, CabR1 = -2.05f, CabR0 = -2.20f, Belt = 1.06f, CabHWBelt = 0.83f, CabHWRoof = 0.70f,
                    BPillar = -0.20f, RoofRails = true,
                },
                // Dual-cab ute - 5.3 m, tall cab and a tub with a flat tonneau cover.
                new CarSpec
                {
                    Type = CarType.Ute, Length = 5.30f, HalfWidth = 0.93f, Roof = 1.80f, WheelR = 0.37f, AxleF = 1.62f, AxleR = -1.48f,
                    Body = new[]
                    {
                        new CarStation(-0.30f, 0.93f, 0.40f, 1.18f), new CarStation( 1.40f, 0.93f, 0.40f, 1.18f),
                        new CarStation( 2.42f, 0.90f, 0.42f, 1.05f), new CarStation( 2.65f, 0.78f, 0.48f, 0.92f),
                    },
                    CabF0 = 1.40f, CabF1 = 0.62f, CabR1 = -0.22f, CabR0 = -0.30f, Belt = 1.18f, CabHWBelt = 0.83f, CabHWRoof = 0.72f,
                    BPillar = 0.30f, Tub = true,
                },
            };

            const float UteTubTop = 1.24f;   // top of the tub sides, a little above the cab's belt

            /// <summary>Car-local to world: yaw only.</summary>
            struct CarFrame
            {
                public Vector3 O; public Quaternion R;
                public Vector3 P(float x, float y, float z) { return O + R * new Vector3(x, y, z); }
                public Vector3 D(float x, float y, float z) { return R * new Vector3(x, y, z); }
            }

            /// <summary>Convex polygon, fan triangulated, wound to face 'outward'.</summary>
            static void Poly(Accum a, IList<Vector3> pts, Vector3 outward)
            {
                Vector3 n = Vector3.zero;
                for (int i = 0; i < pts.Count; i++)
                {
                    Vector3 p = pts[i], q = pts[(i + 1) % pts.Count];
                    n.x += (p.y - q.y) * (p.z + q.z); n.y += (p.z - q.z) * (p.x + q.x); n.z += (p.x - q.x) * (p.y + q.y);
                }
                bool flip = Vector3.Dot(n, outward) < 0f;
                int b = a.V.Count;
                foreach (var p in pts) a.V.Add(p);
                for (int i = 1; i + 1 < pts.Count; i++)
                {
                    a.T.Add(b);
                    if (flip) { a.T.Add(b + i + 1); a.T.Add(b + i); }
                    else      { a.T.Add(b + i);     a.T.Add(b + i + 1); }
                }
            }

            /// <summary>Box in car space: centre and full size in car axes.</summary>
            static void CarBox(Accum a, CarFrame f, float cx, float cy, float cz, float sx, float sy, float sz)
            {
                Box(a, f.P(cx, cy, cz), new Vector3(sx, sy, sz), f.D(0f, 0f, 1f));
            }

            /// <summary>Cylinder whose axis is car-space x: tread plus both faces.</summary>
            static void CarCyl(Accum a, CarFrame f, float cx, float cy, float cz, float r, float width, int sides)
            {
                var inner = new Vector3[sides];
                var outer = new Vector3[sides];
                for (int i = 0; i < sides; i++)
                {
                    float t = (i + 0.5f) / sides * Mathf.PI * 2f;
                    float y = cy + Mathf.Sin(t) * r, z = cz + Mathf.Cos(t) * r;
                    inner[i] = f.P(cx - width * 0.5f, y, z);
                    outer[i] = f.P(cx + width * 0.5f, y, z);
                }
                Vector3 axis = f.D(1f, 0f, 0f), centre = f.P(cx, cy, cz);
                for (int i = 0; i < sides; i++)
                {
                    int j = (i + 1) % sides;
                    Vector3 mid = (inner[i] + inner[j] + outer[i] + outer[j]) * 0.25f;
                    Quad(a, inner[i], inner[j], outer[j], outer[i], mid - (centre + axis * Vector3.Dot(mid - centre, axis)));
                }
                Poly(a, outer, axis);
                Poly(a, inner, -axis);
            }

            /// <summary>Eight-point lower-body section at one station, chamfered top and bottom.</summary>
            static Vector3[] CarSection(CarFrame f, CarStation s)
            {
                float h = s.Y1 - s.Y0;
                float cb = Mathf.Min(0.06f, h * 0.2f), ct = Mathf.Min(0.10f, h * 0.3f);
                return new[]
                {
                    f.P(-s.HW + cb, s.Y0, s.Z), f.P(s.HW - cb, s.Y0, s.Z), f.P(s.HW, s.Y0 + cb, s.Z), f.P(s.HW, s.Y1 - ct, s.Z),
                    f.P(s.HW - ct, s.Y1, s.Z), f.P(-s.HW + ct, s.Y1, s.Z), f.P(-s.HW, s.Y1 - ct, s.Z), f.P(-s.HW, s.Y0 + cb, s.Z),
                };
            }

            /// <summary>A closed loft through the stations (sorted by z).</summary>
            static void CarLoft(Accum a, CarFrame f, CarStation[] st)
            {
                var secs = new Vector3[st.Length][];
                for (int i = 0; i < st.Length; i++) secs[i] = CarSection(f, st[i]);
                for (int i = 0; i + 1 < st.Length; i++)
                {
                    Vector3 axis = f.P(0f, (st[i].Y0 + st[i].Y1 + st[i + 1].Y0 + st[i + 1].Y1) * 0.25f, (st[i].Z + st[i + 1].Z) * 0.5f);
                    for (int k = 0; k < 8; k++)
                    {
                        int l = (k + 1) % 8;
                        Vector3 p0 = secs[i][k], p1 = secs[i][l], p2 = secs[i + 1][l], p3 = secs[i + 1][k];
                        Quad(a, p0, p1, p2, p3, (p0 + p1 + p2 + p3) * 0.25f - axis);
                    }
                }
                Poly(a, secs[0], f.D(0f, 0f, -1f));
                Poly(a, secs[st.Length - 1], f.D(0f, 0f, 1f));
            }

            /// <summary>The lower-body section interpolated at z (for placing arches and wheels).</summary>
            static CarStation CarStationAt(CarStation[] st, float z)
            {
                if (z <= st[0].Z) return st[0];
                for (int i = 0; i + 1 < st.Length; i++)
                    if (z <= st[i + 1].Z)
                    {
                        float t = (z - st[i].Z) / (st[i + 1].Z - st[i].Z);
                        return new CarStation(z, Mathf.Lerp(st[i].HW, st[i + 1].HW, t),
                                              Mathf.Lerp(st[i].Y0, st[i + 1].Y0, t), Mathf.Lerp(st[i].Y1, st[i + 1].Y1, t));
                    }
                return st[st.Length - 1];
            }

            class CarMats { public Accum Paint, Glass, Trim, Tyre, Rim, Head, Tail, Amber, Plate, PlateText; }

            static void BuildCarsFR(Batcher b, Transform root, FullRoutePlan plan, Mats m)
            {
                var colliders = NewGroup("Car_Colliders", root);
                // Paint mix of a typical Australian street: mostly white, silver, grey and black.
                int[] paintWeights = { 26, 18, 18, 14, 10, 7, 7 };   // matches m.CarPaint order
                int i = 0;
                foreach (var c in plan.Cars)
                {
                    // Type and colour come from the car's position, not the plan's random stream,
                    // so they stay the same on every rebuild and the layout is untouched.
                    int hx = Mathf.RoundToInt(c.Pos.x * 100f), hz = Mathf.RoundToInt(c.Pos.y * 100f);
                    var rng = new System.Random(((hx * 73856093) ^ (hz * 19349663) ^ (c.Variant * 83492791)) & 0x7fffffff);
                    int roll = rng.Next(100);
                    CarType type = c.Long
                        ? (roll < 25 ? CarType.Ute : roll < 55 ? CarType.Suv : roll < 78 ? CarType.Sedan : CarType.Hatch)
                        : (roll < 40 ? CarType.Suv : roll < 70 ? CarType.Sedan : CarType.Hatch);
                    int total = 0; foreach (int w in paintWeights) total += w;
                    int pick = rng.Next(total), paint = 0;
                    while (pick >= paintWeights[paint]) { pick -= paintWeights[paint]; paint++; }

                    var spec = CarSpecs[(int)type];
                    Vector3 basePos = W(c.Pos, FullRouteLayout.RoadTop);
                    var f = new CarFrame { O = basePos, R = Quaternion.LookRotation(W3(c.Dir), Vector3.up) };
                    var cm = new CarMats
                    {
                        Paint = b.Get("Cars", "CarPaint_" + paint, m.CarPaint[paint], basePos, 2f),
                        Glass = b.Get("Cars", "CarGlass", m.CarGlass, basePos),
                        Trim = b.Get("Cars", "CarTrim", m.CarTrim, basePos),
                        Tyre = b.Get("Cars", "CarTyre", m.Tyre, basePos),
                        Rim = b.Get("Cars", "CarRim", m.CarRim, basePos),
                        Head = b.Get("Cars", "CarHeadlight", m.CarHeadlight, basePos),
                        Tail = b.Get("Cars", "CarTaillight", m.CarTaillight, basePos),
                        Amber = b.Get("Cars", "CarIndicator", m.CarIndicator, basePos),
                        Plate = b.Get("Cars", "CarPlate", m.CarPlate, basePos),
                        PlateText = b.Get("Cars", "CarPlateText", m.CarPlateText, basePos),
                    };
                    BuildCar(f, spec, cm, rng);

                    var go = new GameObject("CarCollider_" + (i++).ToString("00") + "_" + type);
                    go.transform.SetParent(colliders, false);
                    go.transform.localPosition = basePos + Vector3.up * (spec.Roof * 0.5f);
                    go.transform.localRotation = f.R;
                    go.AddComponent<BoxCollider>().size = new Vector3(spec.HalfWidth * 2f + 0.04f, spec.Roof, spec.Length);
                    go.isStatic = true;
                }
            }

            static void BuildCar(CarFrame f, CarSpec s, CarMats cm, System.Random rng)
            {
                var body = s.Body;
                float zF = body[body.Length - 1].Z, zR = s.Tub ? -s.Length * 0.5f : body[0].Z;

                // ---- lower body, and the ute's tub behind the cab
                CarLoft(cm.Paint, f, body);
                if (s.Tub)
                {
                    const float tubTop = UteTubTop;
                    var tub = new[]
                    {
                        new CarStation(zR, s.HalfWidth - 0.08f, 0.48f, tubTop - 0.04f), new CarStation(zR + 0.10f, s.HalfWidth, 0.44f, tubTop),
                        new CarStation(body[0].Z + 0.02f, s.HalfWidth, 0.44f, tubTop),
                    };
                    CarLoft(cm.Paint, f, tub);
                    // Flat black tonneau cover, and a ladder chassis showing under the tub.
                    CarBox(cm.Trim, f, 0f, tubTop + 0.012f, (zR + body[0].Z) * 0.5f, (s.HalfWidth - 0.07f) * 2f, 0.024f, body[0].Z - zR - 0.22f);
                    CarBox(cm.Trim, f, 0f, 0.40f, (zR + body[0].Z) * 0.5f, 1.10f, 0.14f, body[0].Z - zR - 0.2f);
                }

                // ---- cabin: windscreen, roof, rear glass and the two sides
                float yb = s.Belt, yr = s.Roof, wb = s.CabHWBelt, wr = s.CabHWRoof;
                Vector3 fl = f.P(-wb, yb, s.CabF0), fr = f.P(wb, yb, s.CabF0), rl = f.P(-wb, yb, s.CabR0), rr = f.P(wb, yb, s.CabR0);
                Vector3 tfl = f.P(-wr, yr, s.CabF1), tfr = f.P(wr, yr, s.CabF1), trl = f.P(-wr, yr, s.CabR1), trr = f.P(wr, yr, s.CabR1);
                Vector3 cc = f.P(0f, (yb + yr) * 0.5f, (s.CabF0 + s.CabR0) * 0.5f);
                Quad(cm.Paint, fl, fr, tfr, tfl, (fl + fr + tfr + tfl) * 0.25f - cc);
                Quad(cm.Paint, tfl, tfr, trr, trl, f.D(0f, 1f, 0f));
                Quad(cm.Paint, rl, rr, trr, trl, (rl + rr + trr + trl) * 0.25f - cc);
                Quad(cm.Paint, fr, rr, trr, tfr, f.D(1f, 0.3f, 0f));
                Quad(cm.Paint, fl, rl, trl, tfl, f.D(-1f, 0.3f, 0f));

                // ---- glass, 6 mm proud of the cabin faces, framed by the pillars
                GlassPanel(cm.Glass, f, -1f, s.CabF0, s.CabF1, yb, yr, wb, wr, 0.07f, 0.06f, 0.05f);
                GlassPanel(cm.Glass, f, +1f, s.CabR0, s.CabR1, yb, yr, wb, wr, 0.07f, 0.06f, 0.05f);
                for (int side = -1; side <= 1; side += 2)
                {
                    SideGlass(cm.Glass, f, s, side, s.CabF0, s.BPillar + 0.05f, 0.07f, true);
                    SideGlass(cm.Glass, f, s, side, s.BPillar - 0.05f, s.CabR0, 0.07f, false);
                }

                // ---- wheels and arches
                foreach (float az in new[] { s.AxleF, s.AxleR })
                {
                    bool onTub = s.Tub && az < body[0].Z;
                    var at = CarStationAt(body, az);
                    float hw = onTub ? s.HalfWidth : at.HW, sill = onTub ? 0.44f : at.Y0;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        // Black arch lining just proud of the body side.
                        float ax = side * (hw + 0.006f);
                        var arch = new List<Vector3>();
                        // Down to the sill where the sill is below the axle; above it, the
                        // semicircle alone.
                        float ar = s.WheelR + 0.07f;
                        bool skirt = sill < s.WheelR - 0.01f;
                        if (skirt) arch.Add(f.P(ax, sill, az + ar));
                        for (int k = 0; k <= 8; k++)
                        {
                            float t = k / 8f * Mathf.PI;
                            arch.Add(f.P(ax, s.WheelR + Mathf.Sin(t) * ar, az + Mathf.Cos(t) * ar));
                        }
                        if (skirt) arch.Add(f.P(ax, sill, az - ar));
                        Poly(cm.Trim, arch, f.D(side, 0f, 0f));

                        float tw = 0.22f, tx = side * (hw + 0.012f - tw * 0.5f);
                        CarCyl(cm.Tyre, f, tx, s.WheelR, az, s.WheelR, tw, 14);
                        CarCyl(cm.Rim, f, side * (hw + 0.016f), s.WheelR, az, s.WheelR * 0.62f, 0.012f, 12);
                        CarCyl(cm.Trim, f, side * (hw + 0.022f), s.WheelR, az, s.WheelR * 0.16f, 0.012f, 8);
                        // Five spokes, dark between them, so the rims read as alloys.
                        for (int k = 0; k < 5; k++)
                        {
                            float t = k / 5f * Mathf.PI * 2f + 0.3f;
                            float ry = s.WheelR + Mathf.Sin(t) * s.WheelR * 0.38f, rz = az + Mathf.Cos(t) * s.WheelR * 0.38f;
                            Box(cm.Trim, f.P(side * (hw + 0.021f), ry, rz), new Vector3(0.006f, 0.06f, 0.06f), f.D(0f, 0f, 1f));
                        }
                    }
                }

                // ---- front: headlights, indicators, grille, bumper, plate
                var nose = body[body.Length - 1];
                float fy = nose.Y1 - 0.07f, fz = zF - 0.02f;
                for (int side = -1; side <= 1; side += 2)
                {
                    CarBox(cm.Head, f, side * (nose.HW - 0.20f), fy, fz, 0.30f, 0.10f, 0.08f);
                    CarBox(cm.Amber, f, side * (nose.HW - 0.03f), fy, fz - 0.03f, 0.06f, 0.08f, 0.06f);
                    // Mirrors: body-coloured housing on a black stalk at the base of the A pillar.
                    CarBox(cm.Trim, f, side * (wb + 0.05f), yb + 0.07f, s.CabF0 - 0.18f, 0.10f, 0.03f, 0.05f);
                    CarBox(cm.Paint, f, side * (wb + 0.13f), yb + 0.12f, s.CabF0 - 0.20f, 0.16f, 0.12f, 0.09f);
                    CarBox(cm.Glass, f, side * (wb + 0.13f), yb + 0.12f, s.CabF0 - 0.25f, 0.13f, 0.09f, 0.01f);
                }
                float grilleW = (nose.HW - 0.38f) * 2f, grilleH = (s.Type == CarType.Suv || s.Type == CarType.Ute) ? 0.20f : 0.12f;
                CarBox(cm.Trim, f, 0f, fy - 0.02f - grilleH * 0.3f, fz + 0.005f, grilleW, grilleH, 0.06f);
                CarBox(cm.Trim, f, 0f, nose.Y0 + 0.06f, zF + 0.02f, nose.HW * 2f + 0.06f, 0.12f, 0.10f);
                Plate(cm, f, nose.Y0 + 0.20f, zF + 0.05f, 1f);

                // ---- rear: tail lights, bumper, plate
                float ty, tz = zR + 0.02f;
                float rearHW = s.Tub ? s.HalfWidth - 0.08f : body[0].HW;
                float rearY0 = s.Tub ? 0.48f : body[0].Y0, rearY1 = s.Tub ? UteTubTop - 0.04f : body[0].Y1;
                if (s.Tub)
                {
                    ty = 1.02f;
                    for (int side = -1; side <= 1; side += 2)
                        CarBox(cm.Tail, f, side * (rearHW - 0.05f), ty, tz, 0.10f, 0.32f, 0.08f);
                    CarBox(cm.Trim, f, 0f, 0.52f, zR - 0.02f, rearHW * 2f, 0.14f, 0.14f);   // step bumper
                }
                else
                {
                    ty = rearY1 - 0.10f;
                    for (int side = -1; side <= 1; side += 2)
                        CarBox(cm.Tail, f, side * (rearHW - 0.16f), ty, tz, 0.28f, 0.11f, 0.08f);
                    CarBox(cm.Trim, f, 0f, rearY0 + 0.06f, zR - 0.02f, rearHW * 2f + 0.06f, 0.12f, 0.10f);
                }
                Plate(cm, f, (rearY0 + rearY1) * 0.5f - 0.02f, zR - 0.05f, -1f);

                // ---- extras
                if (s.RoofRails)
                    for (int side = -1; side <= 1; side += 2)
                        CarBox(cm.Trim, f, side * (wr - 0.10f), yr + 0.04f, (s.CabF1 + s.CabR1) * 0.5f, 0.04f, 0.05f, s.CabF1 - s.CabR1 - 0.2f);
            }

            /// <summary>
            /// Windscreen (end -1: the front face, from CabF0 up to CabF1) or rear glass (end +1),
            /// inset from the face edges and pushed 6 mm out along the face normal.
            /// </summary>
            static void GlassPanel(Accum a, CarFrame f, float end, float zBase, float zTop, float yb, float yr,
                                   float wb, float wr, float side, float bottom, float top)
            {
                float v0 = bottom, v1 = 1f - top;
                System.Func<float, float, Vector3> at = (u, v) =>
                {
                    float hw = Mathf.Lerp(wb, wr, v) - side;
                    return f.P(u * hw, Mathf.Lerp(yb, yr, v), Mathf.Lerp(zBase, zTop, v));
                };
                Vector3 p0 = at(-1f, v0), p1 = at(1f, v0), p2 = at(1f, v1), p3 = at(-1f, v1);
                Vector3 n = Vector3.Cross(p1 - p0, p3 - p0).normalized;
                Vector3 outward = f.D(0f, 0.2f, end < 0f ? 1f : -1f);
                if (Vector3.Dot(n, outward) < 0f) n = -n;
                Vector3 off = n * 0.006f;
                Quad(a, p0 + off, p1 + off, p2 + off, p3 + off, outward);
            }

            /// <summary>
            /// One side window between z0 and z1 (either order), following the cabin's side face:
            /// below the roof, above the belt, and kept a pillar's width inside the sloping
            /// windscreen or rear-glass edge.
            /// </summary>
            static void SideGlass(Accum a, CarFrame f, CarSpec s, int side, float zA, float zB, float pillar, bool front)
            {
                float yb = s.Belt, yr = s.Roof;
                const float v0 = 0.10f, v1 = 0.86f;
                // Cabin edges at height fraction v: front edge runs CabF0 -> CabF1, rear CabR0 -> CabR1.
                System.Func<float, float> frontZ = v => Mathf.Lerp(s.CabF0, s.CabF1, v) - pillar;
                System.Func<float, float> rearZ  = v => Mathf.Lerp(s.CabR0, s.CabR1, v) + pillar;
                System.Func<float, float, Vector3> at = (z, v) =>
                    f.P(side * (Mathf.Lerp(s.CabHWBelt, s.CabHWRoof, v) + 0.006f), Mathf.Lerp(yb, yr, v), z);
                float zHi = Mathf.Max(zA, zB), zLo = Mathf.Min(zA, zB);
                var pts = new List<Vector3>();
                if (front)
                {
                    // Front edge follows the A pillar; rear edge is the vertical B pillar.
                    pts.Add(at(Mathf.Min(frontZ(v0), zHi), v0));
                    pts.Add(at(Mathf.Min(frontZ(v1), zHi), v1));
                    pts.Add(at(zLo, v1));
                    pts.Add(at(zLo, v0));
                }
                else
                {
                    pts.Add(at(zHi, v0));
                    pts.Add(at(zHi, v1));
                    pts.Add(at(Mathf.Max(rearZ(v1), zLo), v1));
                    pts.Add(at(Mathf.Max(rearZ(v0), zLo), v0));
                }
                if (Mathf.Abs(pts[0].x - pts[3].x) + Mathf.Abs(pts[0].z - pts[3].z) < 0.05f) return;
                Poly(a, pts, f.D(side, 0.25f, 0f));
            }

            /// <summary>
            /// Number plate: white, 372 x 134 mm like a Victorian standard plate, with six navy
            /// blocks for the characters (no real registration). 'facing' +1 = front of car.
            /// </summary>
            static void Plate(CarMats cm, CarFrame f, float y, float z, float facing)
            {
                CarBox(cm.Plate, f, 0f, y, z, 0.372f, 0.134f, 0.012f);
                for (int k = 0; k < 6; k++)
                {
                    float x = -0.13f + k * 0.052f + (k >= 3 ? 0.012f : 0f);
                    CarBox(cm.PlateText, f, x, y - 0.005f, z + facing * 0.007f, 0.034f, 0.07f, 0.004f);
                }
            }

            // -------------------------------------------------------------- signs
            /// <summary>
            /// A textured sign face. Built as its own quad rather than into a batch, because the
            /// batches re-project UVs from world position and a sign needs its 0-1 UVs intact.
            /// Double-sided signs are two quads back to back, so neither side reads mirrored.
            /// </summary>
            static void SignFace(Transform parent, string name, Material mat, Vector3 centre, Vector3 facing,
                                 float w, float h, bool doubleSided)
            {
                facing.y = 0f; facing.Normalize();
                for (int s = 0; s < (doubleSided ? 2 : 1); s++)
                {
                    Vector3 f = s == 0 ? facing : -facing;
                    var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    Object.DestroyImmediate(go.GetComponent<Collider>());
                    go.name = name + (s == 0 ? "" : "_Back");
                    go.transform.SetParent(parent, false);
                    go.transform.localPosition = centre + f * 0.035f;
                    // A Quad's visible face looks down -Z.
                    go.transform.localRotation = Quaternion.LookRotation(-f, Vector3.up);
                    go.transform.localScale = new Vector3(w, h, 1f);
                    go.GetComponent<MeshRenderer>().sharedMaterial = mat;
                    go.isStatic = true;
                }
            }

            // ------------------------------------------------------------ bus stop
            static void BuildBusStop(Batcher b, Transform root, FullRoutePlan plan, Mats m)
            {
                var group = NewGroup("BusStop", root);
                Vector3 pos = W(plan.ShelterPos, FullRouteLayout.FootBodyTop);
                Quaternion rot = Quaternion.LookRotation(W3(plan.ShelterFacing), Vector3.up);
                AddBusShelter(b.GetGO("BusStop", "ShelterMetal", m.Metal, pos),
                              b.GetGO("BusStop", "ShelterGlass", m.Glass, pos),
                              b.GetGO("BusStop", "ShelterTimber", m.Timber, pos), pos, rot);
                AddLandmarkCollider(group, "ShelterCollider", pos, rot, 0);
                // That shared collider only blocks the back wall; close both short end walls
                // too, so the shelter can only be entered from the open front.
                for (int s = -1; s <= 1; s += 2)
                {
                    var end = new GameObject(s < 0 ? "ShelterCollider_EndGlass" : "ShelterCollider_EndPanel");
                    end.transform.SetParent(group, false);
                    end.transform.localRotation = rot;
                    end.transform.localPosition = pos + rot * new Vector3(s * 1.7f, 1.2f, -0.05f);
                    end.AddComponent<BoxCollider>().size = new Vector3(0.2f, 2.4f, 1.6f);
                }

                // Flag: pole with a square sign at the top, facing up and down the street.
                Vector3 fp = W(plan.BusFlagPos, FullRouteLayout.NatureTop);
                AddPrimitive(b.GetGO("BusStop", "FlagPole", m.Metal, fp), PrimitiveType.Cylinder,
                             fp + Vector3.up * 1.4f, new Vector3(0.07f, 1.4f, 0.07f), Quaternion.identity);
                AddPrimitive(b.GetGO("BusStop", "FlagPlate", m.Metal, fp), PrimitiveType.Cube,
                             fp + Vector3.up * 2.45f, new Vector3(0.66f, 0.66f, 0.05f), Quaternion.LookRotation(W3(plan.BusFlagDir)));
                SignFace(group, "BusStopSign", m.SignBus, fp + Vector3.up * 2.45f, W3(plan.BusFlagDir), 0.6f, 0.6f, true);
                LampCollider(group, "FlagCollider", fp, 2.8f, 0.08f);
            }

            // ---------------------------------------------------------------- park
            static void BuildPark(Batcher b, Transform root, FullRoutePlan plan, Mats m)
            {
                var group = NewGroup("Park", root);
                var z = plan.Park;
                Vector3 c = W(z.C, 0f);
                Vector3 u = W3(z.U), v = W3(z.V);

                // Lawn: its own slab just above the yards, walkable, marked as grass.
                var lawn = b.Get("Park", "ParkLawn", m.Grass[1], c, 3f, true, SurfGrass);
                Box(lawn, c + Vector3.up * (FullRouteLayout.NatureTop - 0.05f), new Vector3(z.HV * 2f, 0.10f, z.HU * 2f), u);

                // Mown stripes along the long axis, like the tutorial lawn.
                var stripes = b.Get("Park", "ParkStripes", m.Grass[2], c, 3f);
                for (float t = -z.HV + 0.85f; t < z.HV - 0.85f; t += 3.4f)
                    Box(stripes, c + v * t + Vector3.up * (FullRouteLayout.NatureTop + 0.003f), new Vector3(1.7f, 0.004f, z.HU * 2f - 0.4f), u);

                var colliders = NewGroup("Park_Colliders", group);

                // Big trees, back half of the park, so the view in from the route stays open.
                // The back of the park is the side away from the route street along its west edge.
                Vector2 back = z.V * Mathf.Sign(Vector2.Dot(z.V, z.C - FullRouteLayout.RouteNodes[6]));
                Vector2[] treeSpots =
                {
                    z.C + z.U * (z.HU - 3f) + back * (z.HV - 3f), z.C - z.U * (z.HU - 3.5f) + back * (z.HV - 3f),
                    z.C + back * (z.HV - 2.5f), z.C + z.U * (z.HU - 3f) - back * (z.HV * 0.1f),
                };
                int k = 0;
                foreach (var tp in treeSpots)
                {
                    Vector3 pos = W(tp, 0f);
                    float h = Random.Range(7f, 9f), can = Random.Range(2.3f, 2.9f);
                    AddTree(b.GetGO("Park", "ParkTrunks", m.Bark, pos), b.GetGO("Park", "ParkCanopy", m.Foliage[0], pos), pos, h, can);
                    var col = new GameObject("ParkTree_" + (k++));
                    col.transform.SetParent(colliders, false);
                    col.transform.localPosition = pos + Vector3.up * (h * 0.5f);
                    var cap = col.AddComponent<CapsuleCollider>(); cap.radius = 0.35f; cap.height = h;
                    col.isStatic = true;
                }

                // Playground: soft-fall, a swing frame and a slide with a red roof - bright,
                // simple shapes that read as "playground" from the street without any sign.
                Vector2 pg = z.C - z.U * (z.HU * 0.25f);
                Vector3 pgc = W(pg, 0f);
                var soft = b.Get("Park", "Softfall", m.Softfall, pgc, 2f, true, SurfGrass);
                Box(soft, pgc + Vector3.up * 0.005f, new Vector3(7f, 0.03f, 9f), u);

                var metal = b.GetGO("Park", "PlayMetal", m.Metal, pgc);
                var red = b.GetGO("Park", "PlayRed", m.PlayRed, pgc);
                var yellow = b.GetGO("Park", "PlayYellow", m.PlayYellow, pgc);
                Quaternion ur = Quaternion.LookRotation(u, Vector3.up);

                // Swing frame
                Vector3 sw = pgc + u * 2.3f;
                for (int s = -1; s <= 1; s += 2)
                {
                    AddPrimitive(metal, PrimitiveType.Cube, sw + v * (s * 1.6f) + Vector3.up * 1.2f, new Vector3(0.08f, 2.4f, 0.08f), ur);
                    AddPrimitive(red, PrimitiveType.Cube, sw + v * (s * 0.6f) + Vector3.up * 0.45f, new Vector3(0.45f, 0.05f, 0.22f), ur);
                    for (int r = -1; r <= 1; r += 2)
                        AddPrimitive(metal, PrimitiveType.Cube, sw + v * (s * 0.6f + r * 0.2f) + Vector3.up * 1.45f,
                                     new Vector3(0.02f, 1.9f, 0.02f), ur);
                }
                AddPrimitive(metal, PrimitiveType.Cube, sw + Vector3.up * 2.4f, new Vector3(3.4f, 0.1f, 0.1f), Quaternion.LookRotation(v, Vector3.up) * Quaternion.Euler(0f, 90f, 0f));

                // Slide tower
                Vector3 tw = pgc - u * 2.4f;
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                        AddPrimitive(metal, PrimitiveType.Cube, tw + u * (sx * 0.6f) + v * (sz * 0.6f) + Vector3.up * 1.35f, new Vector3(0.08f, 2.7f, 0.08f), ur);
                AddPrimitive(yellow, PrimitiveType.Cube, tw + Vector3.up * 1.2f, new Vector3(1.3f, 0.08f, 1.3f), ur);
                AddPrimitive(red, PrimitiveType.Cube, tw + Vector3.up * 2.75f, new Vector3(1.5f, 0.08f, 1.5f), ur);
                AddPrimitive(yellow, PrimitiveType.Cube, tw + v * 1.7f + Vector3.up * 0.65f, new Vector3(0.6f, 0.06f, 2.3f),
                             Quaternion.LookRotation(v, Vector3.up) * Quaternion.Euler(28f, 0f, 0f));

                var playCol = new GameObject("PlaygroundCollider");
                playCol.transform.SetParent(colliders, false);
                playCol.transform.localPosition = pgc + Vector3.up * 1.2f;
                playCol.transform.localRotation = ur;
                playCol.AddComponent<BoxCollider>().size = new Vector3(4.2f, 2.4f, 7f);
                playCol.isStatic = true;

                // Benches facing the playground.
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 bp = pgc + v * (s * 4.6f);
                    Quaternion br = Quaternion.LookRotation(-v * s, Vector3.up);
                    AddBenchAndBin(b.GetGO("Park", "BenchTimber", m.Timber, bp), b.GetGO("Park", "BenchMetal", m.Metal, bp), bp, br);
                    AddLandmarkCollider(colliders, "Bench_" + s, bp, br, 2);
                }
            }

            // -------------------------------------------------------------- school
            static void BuildSchool(Batcher b, Transform root, FullRoutePlan plan, Mats m)
            {
                var group = NewGroup("School", root);
                var z = plan.School;
                Vector2 front = plan.SchoolFrontDir;               // toward the street
                Vector2 along = new Vector2(-front.y, front.x);
                float halfFront = Mathf.Abs(Vector2.Dot(z.U, along)) > 0.5f ? z.HU : z.HV;   // half-width along the street
                float halfDeep  = Mathf.Abs(Vector2.Dot(z.U, along)) > 0.5f ? z.HV : z.HU;
                Vector2 frontEdge = z.C + front * halfDeep;
                Vector3 facing = W3(front);
                Quaternion rot = Quaternion.LookRotation(facing, Vector3.up);
                Vector3 side = Vector3.Cross(Vector3.up, facing);

                // Asphalt yard behind the fence, so the school does not read as a house lot.
                Vector3 yc = W(z.C, 0f);
                var yard = b.Get("School", "SchoolYard", m.Asphalt, yc, 3f);
                Box(yard, W(z.C, FullRouteLayout.BaseGroundTop + 0.01f), new Vector3(halfFront * 2f - 1f, 0.02f, halfDeep * 2f - 1f), facing);

                // Block A: long single-storey classroom block facing the street.
                Vector2 aC = frontEdge - front * (8f + 5f);
                float aw = Mathf.Min(halfFront * 2f - 6f, 26f), ad = 10f, ah = 3.8f;
                SchoolBlock(b, m, W(aC, 0f), facing, aw, ad, ah);

                // Block B: behind, at right angles.
                Vector2 bC = frontEdge - front * 27f + along * (halfFront - 8f);
                SchoolBlock(b, m, W(bC, 0f), W3(along), 12f, 11f, 3.6f);

                // Entry canopy in front of block A.
                Vector3 ac = W(aC, 0f);
                var trim = b.GetGO("School", "SchoolTrim", m.Trim, ac);
                AddPrimitive(trim, PrimitiveType.Cube, ac + facing * (ad * 0.5f + 1.6f) + Vector3.up * 3.0f, new Vector3(5f, 0.18f, 3.4f), rot);
                for (int s = -1; s <= 1; s += 2)
                    AddPrimitive(trim, PrimitiveType.Cube, ac + facing * (ad * 0.5f + 3.1f) + side * (s * 2.3f) + Vector3.up * 1.5f,
                                 new Vector3(0.14f, 3f, 0.14f), rot);

                // Flagpole on the front lawn.
                Vector3 fp = W(frontEdge - front * 3.5f - along * (halfFront - 4f), 0f);
                AddPrimitive(b.GetGO("School", "Flagpole", m.Metal, fp), PrimitiveType.Cylinder, fp + Vector3.up * 3.5f, new Vector3(0.08f, 3.5f, 0.08f), Quaternion.identity);

                // A couple of shade trees in the yard.
                foreach (var t in new[] { z.C - front * (halfDeep - 5f) - along * (halfFront - 5f), z.C - front * (halfDeep - 4f) - along * (halfFront * 0.3f) })
                {
                    Vector3 tp = W(t, 0f);
                    AddTree(b.GetGO("School", "SchoolTrunks", m.Bark, tp), b.GetGO("School", "SchoolCanopy", m.Foliage[1], tp), tp, 7.5f, 2.4f);
                }

                BuildSchoolSignWall(b, group, plan, m, facing, side, rot);
            }

            /// <summary>
            /// The school sign on a brick feature wall standing in the front fence line, as at
            /// most Australian primary schools. The plan breaks the fence for it
            /// (FullRoutePlan.SchoolSignPos, SchoolSignHalfGap), so there are no bars in front of
            /// the sign. The wall is a little wider than the gap and its end piers swallow the
            /// fence ends. Sign 3.4 x 1.7 m, centred at 1.45 m - about eye height.
            /// </summary>
            static void BuildSchoolSignWall(Batcher b, Transform group, FullRoutePlan plan, Mats m,
                                            Vector3 facing, Vector3 side, Quaternion rot)
            {
                const float WallH = 2.4f, WallD = 0.35f, PierW = 0.45f, PierExtra = 0.25f;
                const float SignW = 3.4f, SignH = 1.7f, SignY = 1.45f;
                float wallW = FullRoutePlan.SchoolSignHalfGap * 2f + 0.6f;

                // On the fence line, which runs 0.05 m inside the school's boundary.
                Vector3 c = W(plan.SchoolSignPos - plan.SchoolFrontDir * 0.05f, 0f);

                var brick = b.GetGO("School", "SignWall", m.Brick, c);
                AddPrimitive(brick, PrimitiveType.Cube, c + Vector3.up * (WallH * 0.5f), new Vector3(wallW, WallH, WallD), rot);
                for (int s = -1; s <= 1; s += 2)
                    AddPrimitive(brick, PrimitiveType.Cube, c + side * (s * (wallW * 0.5f - PierW * 0.5f)) + Vector3.up * ((WallH + PierExtra) * 0.5f),
                                 new Vector3(PierW, WallH + PierExtra, WallD + 0.1f), rot);

                // Concrete coping along the top and on the piers.
                var cap = b.GetGO("School", "SignWallCap", m.Planter, c);
                AddPrimitive(cap, PrimitiveType.Cube, c + Vector3.up * (WallH + 0.04f), new Vector3(wallW - PierW * 2f, 0.08f, WallD + 0.08f), rot);
                for (int s = -1; s <= 1; s += 2)
                    AddPrimitive(cap, PrimitiveType.Cube, c + side * (s * (wallW * 0.5f - PierW * 0.5f)) + Vector3.up * (WallH + PierExtra + 0.04f),
                                 new Vector3(PierW + 0.1f, 0.08f, WallD + 0.18f), rot);

                // Dark green frame, then the sign itself, on the street face.
                Vector3 face = c + facing * (WallD * 0.5f);
                AddPrimitive(b.GetGO("School", "SignFrame", m.SchoolFence, c), PrimitiveType.Cube, face + facing * 0.015f + Vector3.up * SignY,
                             new Vector3(SignW + 0.16f, SignH + 0.16f, 0.03f), rot);
                SignFace(group, "SchoolSign", m.SignSchool, face + Vector3.up * SignY, facing, SignW, SignH, false);

                // The wall is part of the boundary: it must stop the participant like the fence.
                var col = new GameObject("SchoolSignWallCollider");
                col.transform.SetParent(group, false);
                col.transform.localPosition = c + Vector3.up * ((WallH + PierExtra) * 0.5f);
                col.transform.localRotation = rot;
                col.AddComponent<BoxCollider>().size = new Vector3(wallW, WallH + PierExtra, WallD + 0.1f);
                col.isStatic = true;
            }

            static void SchoolBlock(Batcher b, Mats m, Vector3 centre, Vector3 facing, float w, float d, float h)
            {
                Quaternion rot = Quaternion.LookRotation(facing, Vector3.up);
                Vector3 side = Vector3.Cross(Vector3.up, facing);
                var brick = b.GetGO("School", "SchoolBrick", m.Brick, centre);
                var roof = b.GetGO("School", "SchoolRoof", m.Roofs[1], centre, 1.2f);
                var glass = b.GetGO("School", "SchoolGlass", m.Glass, centre);
                var trim = b.GetGO("School", "SchoolTrim", m.Trim, centre);

                AddPrimitive(brick, PrimitiveType.Cube, centre + Vector3.up * (h * 0.5f), new Vector3(w, h, d), rot);
                AddMesh(roof, MakeRoofMesh(w, d, 0.7f, 16f, 0.16f, true), centre + Vector3.up * h, Vector3.one, rot);
                int n = Mathf.Max(2, Mathf.FloorToInt(w / 3.2f));
                for (int f = -1; f <= 1; f += 2)
                {
                    for (int i = 0; i < n; i++)
                    {
                        float x = -w * 0.5f + (i + 0.5f) * (w / n);
                        Vector3 wp = centre + facing * (f * (d * 0.5f + 0.03f)) + side * x + Vector3.up * 1.9f;
                        AddPrimitive(trim, PrimitiveType.Cube, wp, new Vector3(2.1f, 1.5f, 0.05f), rot);
                        AddPrimitive(glass, PrimitiveType.Cube, wp + facing * (f * 0.02f), new Vector3(1.9f, 1.3f, 0.05f), rot);
                    }
                }
            }

            // --------------------------------------------------- shopping centre
            static void BuildShoppingCentre(Batcher b, Transform root, FullRoutePlan plan, Mats m)
            {
                var group = NewGroup("ShoppingCentre", root);
                var bz = plan.ShopBuilding;
                var fz = plan.Forecourt;
                Vector3 facing = W3(plan.ShopFacing);
                Vector3 side = Vector3.Cross(Vector3.up, facing);
                Quaternion rot = Quaternion.LookRotation(facing, Vector3.up);
                Vector3 c = W(bz.C, 0f);
                const float H = 7.5f;
                float halfDepth = bz.HU, halfWidth = bz.HV;   // U runs toward the route street

                // Forecourt paving: walkable, paving footsteps, flush with the footpaths.
                Vector3 fc = W(fz.C, 0f);
                Box(b.Get("Shopping", "ForecourtBody", m.Kerb, fc, 1.2f, true, SurfPaving),
                    W(fz.C, FullRouteLayout.FootBodyTop - 0.05f), new Vector3(fz.HV * 2f, 0.10f, fz.HU * 2f + 0.4f), W3(fz.U));
                for (float x = -fz.HV + 0.8f; x < fz.HV - 0.8f; x += 1.6f)
                    for (float y = -fz.HU + 0.8f; y < fz.HU + 0.2f; y += 1.6f)
                    {
                        int v = Mathf.Abs(Mathf.RoundToInt(x * 7f + y * 13f)) % 3;
                        Vector2 p = fz.C + fz.V * x + fz.U * y;
                        Box(b.Get("Shopping", "ForecourtSlabs_" + v, m.Stone[v], fc, 1.2f), W(p, FullRouteLayout.FootSlabTop - 0.006f),
                            new Vector3(1.58f, 0.012f, 1.58f), W3(fz.U));
                    }

                // Building body, parapet band, glazed shopfront, entrance, canopy.
                var wall = b.GetGO("Shopping", "ShopWall", m.ShopWall, c);
                var accent = b.GetGO("Shopping", "ShopAccent", m.ShopAccent, c);
                var glass = b.GetGO("Shopping", "ShopGlass", m.Glass, c);
                var dark = b.GetGO("Shopping", "ShopGlassDark", m.ShopGlassDark, c);

                AddPrimitive(wall, PrimitiveType.Cube, c + Vector3.up * (H * 0.5f), new Vector3(halfWidth * 2f, H, halfDepth * 2f), rot);
                // Parapet band in the supermarket's brand green, all the way round.
                AddPrimitive(b.GetGO("Shopping", "BrandBand", m.BrandGreen, c), PrimitiveType.Cube, c + Vector3.up * (H + 0.35f),
                             new Vector3(halfWidth * 2f + 0.3f, 0.7f, halfDepth * 2f + 0.3f), rot);
                Vector3 frontFace = c + facing * (halfDepth + 0.03f);
                AddPrimitive(glass, PrimitiveType.Cube, frontFace + Vector3.up * 1.6f, new Vector3(halfWidth * 2f - 4f, 3.0f, 0.06f), rot);
                AddPrimitive(dark, PrimitiveType.Cube, frontFace + facing * 0.02f + Vector3.up * 1.25f, new Vector3(4.2f, 2.5f, 0.06f), rot);
                AddPrimitive(accent, PrimitiveType.Cube, frontFace + facing * 1.9f + Vector3.up * 3.45f, new Vector3(14f, 0.35f, 3.8f), rot);
                for (int s = -1; s <= 1; s += 2)
                    AddPrimitive(accent, PrimitiveType.Cube, frontFace + facing * 3.5f + side * (s * 6.5f) + Vector3.up * 1.7f,
                                 new Vector3(0.22f, 3.4f, 0.22f), rot);

                // Supermarket fascia sign over the canopy (the pylon keeps "SHOPPING CENTRE"), on a
                // green backing panel that sits proud of the wall.
                AddPrimitive(b.GetGO("Shopping", "FasciaPanel", m.BrandGreen, c), PrimitiveType.Cube,
                             frontFace + facing * 0.06f + Vector3.up * 5.45f, new Vector3(10.6f, 2.9f, 0.12f), rot);
                SignFace(group, "ShopFasciaSign", m.SignSupermarket, frontFace + facing * 0.12f + Vector3.up * 5.45f, facing, 10f, 2.5f, false);

                // Window posters: mostly pictures, so they read without much English. Either side
                // of the doors, just in front of the glass, bottoms at 0.55 m.
                if (m.Posters != null && m.Posters.Length >= 5)
                {
                    float[] px = { -12.5f, -8.0f, -3.8f, 3.8f, 8.0f, 12.5f };
                    int[] which = { 4, 2, 0, 1, 3, 4 };   // Specials, Bakery, Fruit | Veg, Dairy, Specials
                    for (int i = 0; i < px.Length; i++)
                    {
                        if (Mathf.Abs(px[i]) + 0.8f > halfWidth - 2f) continue;   // stay on the glass
                        SignFace(group, "WindowPoster_" + i, m.Posters[which[i]],
                                 frontFace + facing * 0.02f + side * px[i] + Vector3.up * 1.55f, facing, 1.5f, 2.0f, false);
                    }
                }

                var col = new GameObject("ShopCollider");
                col.transform.SetParent(group, false);
                col.transform.localPosition = c + Vector3.up * (H * 0.5f);
                col.transform.localRotation = rot;
                col.AddComponent<BoxCollider>().size = new Vector3(halfWidth * 2f, H, halfDepth * 2f);
                col.isStatic = true;

                // Pylon sign - the circle on the drawing - beside the street end, both faces
                // toward the approach so it is the first thing seen of the shopping centre.
                Vector3 pp = W(plan.ShopSignPos, FullRouteLayout.NatureTop);
                Vector3 pf = W3(plan.ShopSignFacing);
                Quaternion pr = Quaternion.LookRotation(pf, Vector3.up);
                var pylon = b.GetGO("Shopping", "PylonBody", m.ShopAccent, pp);
                AddPrimitive(pylon, PrimitiveType.Cube, pp + Vector3.up * 3.1f, new Vector3(1.9f, 6.2f, 0.45f), pr);
                AddPrimitive(b.GetGO("Shopping", "PylonBase", m.Planter, pp), PrimitiveType.Cube, pp + Vector3.up * 0.25f,
                             new Vector3(2.3f, 0.5f, 0.9f), pr);
                SignFace(group, "PylonSign", m.SignShopTall, pp + Vector3.up * 3.6f, pf, 1.6f, 3.2f, true);
                var pcol = new GameObject("PylonCollider");
                pcol.transform.SetParent(group, false);
                pcol.transform.localPosition = pp + Vector3.up * 3.1f;
                pcol.transform.localRotation = pr;
                pcol.AddComponent<BoxCollider>().size = new Vector3(2.3f, 6.2f, 0.9f);
                pcol.isStatic = true;

                // Two planters with small trees either side of the entrance.
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 pl = frontFace + facing * 6.5f + side * (s * 9f);
                    pl.y = 0f;
                    AddPrimitive(b.GetGO("Shopping", "Planters", m.Planter, pl), PrimitiveType.Cube, pl + Vector3.up * 0.3f, new Vector3(2.2f, 0.6f, 2.2f), rot);
                    AddTree(b.GetGO("Shopping", "ShopTrunks", m.Bark, pl), b.GetGO("Shopping", "ShopCanopy", m.Foliage[2], pl), pl + Vector3.up * 0.55f, 4.2f, 1.3f);
                    var pc = new GameObject("PlanterCollider_" + s);
                    pc.transform.SetParent(group, false);
                    pc.transform.localPosition = pl + Vector3.up * 0.6f;
                    pc.transform.localRotation = rot;
                    pc.AddComponent<BoxCollider>().size = new Vector3(2.2f, 1.2f, 2.2f);
                    pc.isStatic = true;
                }
            }

            // ------------------------------------------------------- special house
            /// <summary>
            /// The special house: double storey where every other house is single, a strong blue
            /// with a red roof among beige and grey neighbours, a tall palm in the front yard and a
            /// bright yellow letterbox at the gate. Three independent cues, so it still reads if a
            /// participant does not register one of them.
            /// </summary>
            static void BuildSpecialHouse(Batcher b, FullRoutePlan plan, Mats m)
            {
                var z = plan.SpecialLot;
                Vector2 facing = plan.SpecialFacing;
                float halfDeep = Mathf.Abs(Vector2.Dot(z.U, facing)) > 0.5f ? z.HU : z.HV;
                Vector2 along = new Vector2(-facing.y, facing.x);
                Vector2 frontEdge = z.C + facing * halfDeep;

                var spot = new HouseSpot
                {
                    Frontage = frontEdge - facing * 5.5f,
                    Facing = facing, Width = 10.5f, Depth = 9f, GarageSide = 0, Seed = FullRouteLayout.Seed + 77,
                };
                float storeyH = AddSizedHouse(b, m, spot, m.Walls, m.Roofs, 2, m.SpecialWall, m.SpecialRoof);

                Vector3 fwd = W3(facing);
                Quaternion rot = Quaternion.LookRotation(fwd, Vector3.up);
                Vector3 side = Vector3.Cross(Vector3.up, fwd);
                Vector3 front = W(spot.Frontage, 0f);

                // White band between the storeys and a balcony over the door.
                var trim = b.GetGO("Houses", "HouseTrim", m.Trim, front);
                AddPrimitive(trim, PrimitiveType.Cube, front - fwd * 4.5f + Vector3.up * storeyH, new Vector3(10.7f, 0.22f, 9.2f), rot);
                AddPrimitive(trim, PrimitiveType.Cube, front + fwd * 0.9f + Vector3.up * storeyH, new Vector3(3.6f, 0.16f, 1.8f), rot);
                AddPrimitive(trim, PrimitiveType.Cube, front + fwd * 1.75f + Vector3.up * (storeyH + 0.5f), new Vector3(3.6f, 0.9f, 0.08f), rot);
                for (int s = -1; s <= 1; s += 2)
                    AddPrimitive(trim, PrimitiveType.Cube, front + fwd * 1.7f + side * (s * 1.7f) + Vector3.up * (storeyH * 0.5f),
                                 new Vector3(0.16f, storeyH, 0.16f), rot);

                // Path from the gate to the door.
                Vector2 gate = frontEdge + along * 0f;
                var path = b.Get("Houses", "SpecialPath", m.Stone[0], front, 1.2f);
                Box(path, W((gate + spot.Frontage) * 0.5f, FullRouteLayout.BaseGroundTop + 0.02f), new Vector3(1.2f, 0.04f, 5.5f), fwd);

                // Palm, to one side of the front yard.
                Vector2 palmP = frontEdge - facing * 2.6f + along * 3.4f;
                AddPalm(b, m, W(palmP, FullRouteLayout.BaseGroundTop), 7.2f);

                // Letterbox on a brick pillar by the gate.
                Vector3 lb = W(frontEdge - facing * 0.45f - along * 1.4f, 0f);
                AddPrimitive(b.GetGO("Houses", "LetterboxPillar", m.Walls[1], lb), PrimitiveType.Cube, lb + Vector3.up * 0.55f, new Vector3(0.45f, 1.1f, 0.45f), rot);
                var yellow = b.GetGO("Houses", "Letterbox", m.Letterbox, lb);
                AddPrimitive(yellow, PrimitiveType.Cube, lb + Vector3.up * 1.32f, new Vector3(0.40f, 0.42f, 0.50f), rot);
                AddPrimitive(yellow, PrimitiveType.Cube, lb + Vector3.up * 1.58f, new Vector3(0.44f, 0.08f, 0.54f), rot * Quaternion.Euler(0f, 0f, 0f));
            }

            static void BuildOrdinaryHouseOnSpecialLot(Batcher b, FullRoutePlan plan, Mats m)
            {
                var z = plan.SpecialLot;
                Vector2 facing = plan.SpecialFacing;
                float halfDeep = Mathf.Abs(Vector2.Dot(z.U, facing)) > 0.5f ? z.HU : z.HV;
                var spot = new HouseSpot
                {
                    Frontage = z.C + facing * halfDeep - facing * 5.5f, Facing = facing,
                    Width = 10.5f, Depth = 9f, GarageSide = 0, Seed = FullRouteLayout.Seed + 77,
                };
                AddSizedHouse(b, m, spot, m.Walls, m.Roofs, 1);
            }

            static void AddPalm(Batcher b, Mats m, Vector3 basePos, float height)
            {
                var trunk = b.GetGO("Houses", "PalmTrunk", m.Bark, basePos);
                var fronds = b.GetGO("Houses", "PalmFronds", m.PalmFrond, basePos);
                // Trunk: tapered drums on a gentle curve.
                int n = 12;
                Vector3 top = basePos;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)n;
                    float r = Mathf.Lerp(0.40f, 0.26f, t);
                    Vector3 p = basePos + new Vector3(Mathf.Sin(t * 1.6f) * 0.45f, height * t + height / n * 0.5f, 0f);
                    AddPrimitive(trunk, PrimitiveType.Cylinder, p, new Vector3(r, height / n * 0.52f, r), Quaternion.Euler(0f, 0f, -t * 8f));
                    top = p + Vector3.up * (height / n * 0.5f);
                }
                AddPrimitive(fronds, PrimitiveType.Sphere, top, new Vector3(0.7f, 0.6f, 0.7f), Quaternion.identity);
                // Fronds: long, flat, arching out and down.
                for (int k = 0; k < 11; k++)
                {
                    float yaw = k * (360f / 11f) + (k % 2) * 12f;
                    float droop = 22f + (k % 3) * 12f;
                    Quaternion q = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(droop, 0f, 0f);
                    AddPrimitive(fronds, PrimitiveType.Sphere, top + q * new Vector3(0f, 0f, 1.35f), new Vector3(0.55f, 0.07f, 2.8f), q);
                }
            }

            // ------------------------------------------------------------ triggers
            /// <summary>
            /// Empty trigger volumes, named, the same as the tutorial's checkpoints: CP_Start at
            /// the bus stop, CP_Decision_N* at each circled decision point, CP_EndZone on the
            /// shopping centre forecourt, and WrongTurn_* just inside each side street. Wire
            /// TriggerController onto them once the run system for this scene is set up.
            /// </summary>
            /// <summary>
            /// Writes the route's layout into a RouteDefinition on the route root, in the root's
            /// local space: the walking line (bus stop to forecourt along the footpath, over the
            /// N10 zebra), the nodes, one branch per WrongTurn_ trigger, the decision zones and
            /// the zebras. Everything comes from the same plan the meshes and triggers were built
            /// from, so it always matches them.
            /// </summary>
            static void BakeRouteDefinition(GameObject root, FullRoutePlan plan)
            {
                var def = root.GetComponent<RouteDefinition>();
                if (def == null) def = root.AddComponent<RouteDefinition>();

                var line = new Vector3[plan.WalkingLine.Count];
                for (int i = 0; i < line.Length; i++) line[i] = W(plan.WalkingLine[i], FullRouteLayout.FootSlabTop);

                var n = FullRouteLayout.RouteNodes;
                var decision = new HashSet<int>(FullRouteLayout.DecisionNodes);
                var nodes = new RouteDefinition.Node[n.Length];
                for (int i = 0; i < n.Length; i++)
                    nodes[i] = new RouteDefinition.Node { name = "N" + i, position = W(n[i], FullRouteLayout.FootSlabTop), decision = decision.Contains(i) };

                var branches = new RouteDefinition.Branch[plan.Branches.Count];
                for (int i = 0; i < branches.Length; i++)
                {
                    var b = plan.Branches[i];
                    branches[i] = new RouteDefinition.Branch
                    {
                        name = b.Name, node = "N" + b.Node, atDecision = decision.Contains(b.Node),
                        mouth = W(b.Mouth, FullRouteLayout.RoadTop), into = W3(b.Into),
                        trigger = W(b.Trigger, CheckpointH * 0.5f),
                    };
                }

                var zones = new List<RouteDefinition.Zone>();
                foreach (var cp in plan.Checkpoints)
                    if (cp.Name.StartsWith("CP_Decision_"))
                        zones.Add(new RouteDefinition.Zone
                        {
                            name = cp.Name, centre = W(cp.Box.C, CheckpointH * 0.5f), forward = W3(cp.Box.U),
                            size = new Vector3(cp.Box.HV * 2f, CheckpointH, cp.Box.HU * 2f),
                        });

                var zebras = new List<RouteDefinition.Zone>();
                foreach (var z in plan.Zebras)
                {
                    var st = plan.Streets[z.Street];
                    zebras.Add(new RouteDefinition.Zone
                    {
                        name = z.Name, centre = W(st.Point(z.S), FullRouteLayout.RoadTop),
                        forward = W3(st.Dir(st.Seg(z.S))),
                        size = new Vector3(FullRouteLayout.HalfCarriageway * 2f, 0.2f, FullRouteLayout.ZebraWidth),
                    });
                }

                def.SetData(line, plan.WalkingStartS, plan.WalkingCrossS, plan.WalkingEndS, nodes, branches,
                            zones.ToArray(), zebras.ToArray(),
                            System.DateTime.Now.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture));

                // Footpath network for the Guided line: both footpaths, joined only at zebras.
                var footNodes = new Vector3[plan.FootNodes.Count];
                for (int i = 0; i < footNodes.Length; i++) footNodes[i] = W(plan.FootNodes[i], FullRouteLayout.FootSlabTop);
                var footLinks = new RouteDefinition.FootLink[plan.FootLinks.Count];
                for (int i = 0; i < footLinks.Length; i++)
                    footLinks[i] = new RouteDefinition.FootLink { a = plan.FootLinks[i].A, b = plan.FootLinks[i].B, kind = plan.FootLinks[i].Kind };
                def.SetFootpathNetwork(footNodes, footLinks, plan.FootDestination);
                EditorUtility.SetDirty(def);
                Debug.Log(string.Format(
                    "[FullRoute] Route definition baked: walking line {0} points, ideal walk {1:0} m from leaving the " +
                    "bus stop to the end zone (zebra at {2:0} m), {3} nodes ({4} decision points), {5} branches, " +
                    "{6} decision zones, {7} zebras, footpath network {8} points / {9} links.",
                    line.Length, plan.WalkingEndS - plan.WalkingStartS, plan.WalkingCrossS - plan.WalkingStartS,
                    nodes.Length, decision.Count, branches.Length, zones.Count, zebras.Count,
                    footNodes.Length, footLinks.Length), def);
            }

            static void BuildTriggersFR(Transform root, FullRoutePlan plan)
            {
                var cps = NewGroup("Checkpoints", root);
                foreach (var cp in plan.Checkpoints) TriggerBox(cps, cp.Name, cp.Box);
                var wrong = NewGroup("WrongTurnZones", root);
                foreach (var w in plan.WrongTurns) TriggerBox(wrong, w.Name, w.Box);
            }

            static void TriggerBox(Transform parent, string name, Obb box)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = W(box.C, CheckpointH * 0.5f);
                go.transform.localRotation = Quaternion.LookRotation(W3(box.U), Vector3.up);
                var col = go.AddComponent<BoxCollider>();
                col.isTrigger = true;
                col.size = new Vector3(box.HV * 2f, CheckpointH, box.HU * 2f);
            }

            static void BuildSpawnAndLightFR(Transform root, FullRoutePlan plan, bool forRunSystem)
            {
                var spawn = new GameObject("PlayerSpawn");
                spawn.transform.SetParent(root, false);
                spawn.transform.localPosition = W(plan.Spawn, FullRouteLayout.FootSlabTop);
                spawn.transform.localRotation = Quaternion.LookRotation(W3(plan.SpawnFacing), Vector3.up);
                // In RunSystem, RunSystemController places the player; a SceneSpawnPoint there
                // would compete with the tutorial's for SceneTransitionController. And RunSystem
                // is always loaded on top of Bootstrap, whose sun and sky are the tutorial's, so
                // it gets no light of its own (a second sun would double the lighting).
                if (forRunSystem) return;

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
                    light.color = new Color(1.00f, 0.96f, 0.89f);
                    light.shadows = LightShadows.Soft;
                }
            }
        }
    }
}
