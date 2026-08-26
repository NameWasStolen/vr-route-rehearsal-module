using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;
using Random = UnityEngine.Random;

namespace VRTutorial.EditorTools
{
    /// <summary>
    /// Builds the navigation-tutorial environment: start pad -> 20 m stone path ->
    /// four-way intersection -> three arms, the right one opening into the end zone.
    /// Grass verge either side of every path, metal bar fence around the whole perimeter,
    /// plus scattered low-poly dressing (kerbs, paving seams, lamp posts, trees, shrubs,
    /// rocks, grass tufts).
    ///
    /// Menu:  Tools > VR Tutorial > ...
    /// All measurements are in metres and live in the LAYOUT region below.
    /// Every batch of dressing is combined into a single mesh, so the whole environment
    /// is roughly 17 renderers regardless of how many props are scattered.
    /// </summary>
    public static class VRTutorialSceneBuilder
    {
        // ---------------------------------------------------------------- paths
        private const string RootName        = "TutorialEnvironment";
        private const string BaseFolder      = "Assets/VRTutorial";
        private const string MaterialFolder  = BaseFolder + "/Materials";
        private const string GeneratedFolder = BaseFolder + "/Generated";
        private const string SceneFolder     = BaseFolder + "/Scenes";

        // --------------------------------------------------------------- LAYOUT
        #region LAYOUT  (edit these to reshape the level)

        // Vertical
        private const float GroundThickness = 0.20f;   // grass slab thickness
        private const float PathTopY        = 0.02f;   // paving sits 2 cm proud of grass
        private const float PathThickness   = 0.12f;

        // Fence
        private const float FenceHeight  = 1.20f;
        private const float PostSpacing  = 2.50f;
        private const float PostSize     = 0.09f;
        private const float RailSize     = 0.07f;
        private const float BarSpacing   = 0.30f;
        private const float BarSize      = 0.035f;
        private const float ColliderThickness = 0.20f;

        // Dressing
        private const int   PropSeed      = 20260826; // change for a different scatter
        private const float KerbWidth     = 0.16f;
        private const float KerbHeight    = 0.10f;
        private const float SeamSpacing   = 2.00f;    // distance between paving joints
        private const float LampHeight    = 3.20f;

        private const float TreeDensity  = 0.075f;    // props per square metre of grass
        private const float ShrubDensity = 0.120f;
        private const float RockDensity  = 0.090f;
        private const float TuftDensity  = 0.650f;

        // Grass footprint rectangles: (xMin, zMin, xMax, zMax)
        private static readonly Vector4[] GrassRects =
        {
            new Vector4( -4f, -10f,   4f,  35f),  // main corridor + start + north arm
            new Vector4(-15f,  16f,  -4f,  24f),  // west arm
            new Vector4(  4f,  16f,  21f,  24f),  // east arm
            new Vector4( 21f,  12f,  34f,  28f),  // end zone
        };

        // Stone paving rectangles: (xMin, zMin, xMax, zMax)
        private static readonly Vector4[] PathRects =
        {
            new Vector4(-2.5f, -6f,  2.5f, -1f),  // start pad, 5 x 5
            new Vector4(-1f,   -1f,  1f,   31f),  // main path + intersection + north arm
            new Vector4(-11f,  19f, -1f,   21f),  // west arm, 10 m
            new Vector4( 1f,   19f, 21f,   21f),  // east arm, 20 m
            new Vector4(21f,   15f, 31f,   25f),  // end zone paving, 10 x 10
        };

        // Fence perimeter, walked as a closed loop around the union of GrassRects.
        private static readonly Vector2[] FencePerimeter =
        {
            new Vector2( -4f, -10f), new Vector2(  4f, -10f),
            new Vector2(  4f,  16f), new Vector2( 21f,  16f),
            new Vector2( 21f,  12f), new Vector2( 34f,  12f),
            new Vector2( 34f,  28f), new Vector2( 21f,  28f),
            new Vector2( 21f,  24f), new Vector2(  4f,  24f),
            new Vector2(  4f,  35f), new Vector2( -4f,  35f),
            new Vector2( -4f,  24f), new Vector2(-15f,  24f),
            new Vector2(-15f,  16f), new Vector2( -4f,  16f),
        };

        // Kerb runs laid just outside the paving: (x0, z0, x1, z1).
        // Gaps are deliberate - they are the openings where paths meet.
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
            // east arm
            new Vector4( 1.08f, 18.92f, 21.00f, 18.92f),
            new Vector4( 1.08f, 21.08f, 21.00f, 21.08f),
            // end zone, broken where the east arm enters
            new Vector4(20.92f, 15.00f, 20.92f, 19.00f),
            new Vector4(20.92f, 21.00f, 20.92f, 25.00f),
            new Vector4(21.00f, 14.92f, 31.00f, 14.92f),
            new Vector4(21.00f, 25.08f, 31.00f, 25.08f),
            new Vector4(31.08f, 14.92f, 31.08f, 25.08f),
        };

        // Lamp posts: (x, z, armDirX, armDirZ) - the arm points toward the path.
        private static readonly Vector4[] LampPosts =
        {
            new Vector4( 1.6f,  5.0f, -1f,  0f),
            new Vector4(-1.6f, 13.0f,  1f,  0f),
            new Vector4( 1.6f, 27.0f, -1f,  0f),
            new Vector4( 1.6f, 21.6f, -1f, -1f),   // intersection corners
            new Vector4(-1.6f, 21.6f,  1f, -1f),
            new Vector4( 1.6f, 18.4f, -1f,  1f),
            new Vector4(-1.6f, 18.4f,  1f,  1f),
            new Vector4(-6.0f, 21.6f,  0f, -1f),   // west arm
            new Vector4( 7.0f, 21.6f,  0f, -1f),   // east arm
            new Vector4(13.0f, 18.4f,  0f,  1f),
            new Vector4(19.0f, 21.6f,  0f, -1f),
            new Vector4(22.5f, 26.5f,  0f, -1f),   // end zone
            new Vector4(29.5f, 26.5f,  0f, -1f),
            new Vector4(26.0f, 13.5f,  0f,  1f),
        };

        private const float CheckpointHeight = 3f;

        #endregion

        // ------------------------------------------------------------ menu items
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
            Debug.Log($"[VRTutorial] Scene saved to {path}");
        }

        [MenuItem("Tools/VR Tutorial/Clear Tutorial Scene", false, 20)]
        public static void Clear()
        {
            var existing = GameObject.Find(RootName);
            if (existing == null)
            {
                Debug.Log($"[VRTutorial] No '{RootName}' found in the open scene.");
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

            Material grassMat   = GetOrCreateMaterial("M_Grass",      new Color(0.32f, 0.52f, 0.24f), 0.05f);
            Material stoneMat   = GetOrCreateMaterial("M_Stone",      new Color(0.62f, 0.61f, 0.58f), 0.10f);
            Material endZoneMat = GetOrCreateMaterial("M_EndZone",    new Color(0.45f, 0.58f, 0.72f), 0.15f);
            Material metalMat   = GetOrCreateMaterial("M_FenceMetal", new Color(0.42f, 0.44f, 0.47f), 0.65f);
            Material kerbMat    = GetOrCreateMaterial("M_PathEdge",   new Color(0.48f, 0.47f, 0.45f), 0.12f);
            Material foliageMat = GetOrCreateMaterial("M_Foliage",    new Color(0.20f, 0.38f, 0.18f), 0.05f);
            Material tuftMat    = GetOrCreateMaterial("M_GrassTuft",  new Color(0.26f, 0.46f, 0.20f), 0.05f);
            Material barkMat    = GetOrCreateMaterial("M_Bark",       new Color(0.34f, 0.26f, 0.19f), 0.05f);
            Material rockMat    = GetOrCreateMaterial("M_Rock",       new Color(0.52f, 0.51f, 0.50f), 0.15f);
            Material lampMat    = GetOrCreateMaterial("M_LampHead",   new Color(0.90f, 0.86f, 0.68f), 0.35f);

            BuildGround(root.transform, grassMat);
            BuildPaths(root.transform, stoneMat, endZoneMat);
            BuildPathDetail(root.transform, kerbMat);
            BuildFence(root.transform, metalMat);
            BuildLamps(root.transform, metalMat, lampMat);
            BuildScatter(root.transform, barkMat, foliageMat, tuftMat, rockMat);
            BuildCheckpoints(root.transform);
            BuildSpawnAndLight(root.transform);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();

            Debug.Log("[VRTutorial] Environment built. Start pad -> 20 m main path -> intersection at (0, 20); " +
                      "north and west arms 10 m, east arm 20 m into a 10 x 10 m end zone centred on (26, 20).");
        }

        // ---------------------------------------------------------------- ground
        private static void BuildGround(Transform root, Material grass)
        {
            var group = NewGroup("Ground_Grass", root);
            for (int i = 0; i < GrassRects.Length; i++)
            {
                var r = GrassRects[i];
                CreateSlab($"Grass_{i:00}", group, r.x, r.y, r.z, r.w, 0f, GroundThickness, grass, true);
            }
        }

        private static void BuildPaths(Transform root, Material stone, Material endZone)
        {
            var group = NewGroup("Paths", root);
            string[] names = { "StartPad", "MainPath", "WestArm", "EastArm", "EndZonePaving" };

            for (int i = 0; i < PathRects.Length; i++)
            {
                var r = PathRects[i];
                Material m = (i == PathRects.Length - 1) ? endZone : stone;
                CreateSlab(names[i], group, r.x, r.y, r.z, r.w, PathTopY, PathThickness, m, false);
            }
        }

        // ----------------------------------------------------------- path detail
        private static void BuildPathDetail(Transform root, Material kerbMat)
        {
            var group = NewGroup("PathDetail", root);
            var batch = BeginBatch(group);

            // Kerb stones along the edge of every paved surface.
            foreach (var line in KerbLines)
            {
                Vector2 a = new Vector2(line.x, line.y);
                Vector2 b = new Vector2(line.z, line.w);
                Vector2 delta = b - a;
                float length = delta.magnitude;
                if (length < 0.01f) continue;

                Vector3 dir = new Vector3(delta.x, 0f, delta.y).normalized;
                Vector3 mid = new Vector3((a.x + b.x) * 0.5f, KerbHeight * 0.5f, (a.y + b.y) * 0.5f);
                AddPrimitive(batch, PrimitiveType.Cube, mid,
                             new Vector3(KerbWidth, KerbHeight, length + KerbWidth),
                             Quaternion.LookRotation(dir, Vector3.up));
            }

            // Paving joints across each walkway, and a grid across the end zone.
            for (int i = 0; i < PathRects.Length; i++)
            {
                var r = PathRects[i];
                float w = r.z - r.x;
                float d = r.w - r.y;

                if (i == PathRects.Length - 1)      // end zone: joints both ways
                {
                    AddSeams(batch, r, true);
                    AddSeams(batch, r, false);
                }
                else
                {
                    AddSeams(batch, r, w > d);      // joints run across the long axis
                }
            }

            EndBatch(batch, group, "PathDetail", "PathDetailMesh", kerbMat);
        }

        private static void AddSeams(Transform batch, Vector4 rect, bool alongX)
        {
            float x0 = rect.x, z0 = rect.y, x1 = rect.z, z1 = rect.w;
            float y = PathTopY + 0.015f;

            if (alongX)
            {
                for (float x = x0 + SeamSpacing; x < x1 - 0.1f; x += SeamSpacing)
                    AddPrimitive(batch, PrimitiveType.Cube,
                                 new Vector3(x, y, (z0 + z1) * 0.5f),
                                 new Vector3(0.05f, 0.03f, z1 - z0), Quaternion.identity);
            }
            else
            {
                for (float z = z0 + SeamSpacing; z < z1 - 0.1f; z += SeamSpacing)
                    AddPrimitive(batch, PrimitiveType.Cube,
                                 new Vector3((x0 + x1) * 0.5f, y, z),
                                 new Vector3(x1 - x0, 0.03f, 0.05f), Quaternion.identity);
            }
        }

        // ----------------------------------------------------------------- fence
        private static void BuildFence(Transform root, Material metal)
        {
            var group = NewGroup("Fence", root);
            var batch = BeginBatch(group);
            var colliderGroup = NewGroup("Fence_Colliders", group);

            for (int i = 0; i < FencePerimeter.Length; i++)
            {
                Vector2 a = FencePerimeter[i];
                Vector2 b = FencePerimeter[(i + 1) % FencePerimeter.Length];
                BuildFenceRunParts(batch, a, b);
                CreateFenceCollider(colliderGroup, $"FenceCollider_{i:00}", a, b);
            }

            EndBatch(batch, group, "Fence_Visual", "FenceMesh", metal);
        }

        private static void BuildFenceRunParts(Transform parent, Vector2 a, Vector2 b)
        {
            Vector2 delta = b - a;
            float length = delta.magnitude;
            if (length < 0.01f) return;

            Vector3 dir = new Vector3(delta.x, 0f, delta.y).normalized;
            Vector3 start = new Vector3(a.x, 0f, a.y);
            Vector3 mid = start + dir * (length * 0.5f);
            Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);

            int postCount = Mathf.Max(2, Mathf.RoundToInt(length / PostSpacing) + 1);
            for (int i = 0; i < postCount; i++)
            {
                float t = length * i / (postCount - 1);
                AddPrimitive(parent, PrimitiveType.Cube,
                             start + dir * t + Vector3.up * (FenceHeight * 0.5f),
                             new Vector3(PostSize, FenceHeight, PostSize), rot);
            }

            float[] railHeights = { FenceHeight - RailSize * 0.5f, FenceHeight * 0.55f, 0.15f };
            foreach (float h in railHeights)
                AddPrimitive(parent, PrimitiveType.Cube, mid + Vector3.up * h,
                             new Vector3(RailSize, RailSize, length), rot);

            float barHeight = FenceHeight - 0.30f;
            int barCount = Mathf.Max(1, Mathf.FloorToInt(length / BarSpacing) - 1);
            float step = length / (barCount + 1);
            for (int i = 1; i <= barCount; i++)
                AddPrimitive(parent, PrimitiveType.Cube,
                             start + dir * (step * i) + Vector3.up * (0.15f + barHeight * 0.5f),
                             new Vector3(BarSize, barHeight, BarSize), rot);
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

                // post
                AddPrimitive(postBatch, PrimitiveType.Cylinder,
                             basePos + Vector3.up * (LampHeight * 0.5f),
                             new Vector3(0.14f, LampHeight * 0.5f, 0.14f), Quaternion.identity);

                // arm reaching over the path
                AddPrimitive(postBatch, PrimitiveType.Cube,
                             basePos + Vector3.up * (LampHeight - 0.08f) + armDir * 0.30f,
                             new Vector3(0.07f, 0.07f, 0.60f), rot);

                // head
                AddPrimitive(headBatch, PrimitiveType.Cube,
                             basePos + Vector3.up * (LampHeight - 0.18f) + armDir * 0.55f,
                             new Vector3(0.32f, 0.12f, 0.32f), rot);

                var col = new GameObject($"LampCollider_{i:00}");
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

        // --------------------------------------------------------------- scatter
        private static void BuildScatter(Transform root, Material bark, Material foliage,
                                         Material tuft, Material rock)
        {
            var group = NewGroup("Scatter", root);

            Random.State previous = Random.state;
            Random.InitState(PropSeed);

            var barkBatch    = BeginBatch(group);
            var foliageBatch = BeginBatch(group);
            var tuftBatch    = BeginBatch(group);
            var rockBatch    = BeginBatch(group);
            var trunkColliders = NewGroup("Tree_Colliders", group);

            // Trees - trunk cylinder plus one canopy sphere, kept clear of the walkway.
            int treeIndex = 0;
            foreach (Vector2 p in ScatterPoints(TreeDensity, 1.5f, 0.8f))
            {
                float height = Random.Range(2.6f, 3.8f);
                float canopy = Random.Range(1.0f, 1.5f);

                AddPrimitive(barkBatch, PrimitiveType.Cylinder,
                             new Vector3(p.x, height * 0.5f, p.y),
                             new Vector3(0.24f, height * 0.5f, 0.24f), Quaternion.identity);

                AddPrimitive(foliageBatch, PrimitiveType.Sphere,
                             new Vector3(p.x, height + canopy * 0.35f, p.y),
                             new Vector3(canopy * 2f, canopy * 1.6f, canopy * 2f),
                             Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));

                var col = new GameObject($"TreeCollider_{treeIndex:00}");
                col.transform.SetParent(trunkColliders, false);
                col.transform.localPosition = new Vector3(p.x, height * 0.5f, p.y);
                var capsule = col.AddComponent<CapsuleCollider>();
                capsule.radius = 0.20f;
                capsule.height = height;
                col.isStatic = true;
                treeIndex++;
            }

            // Shrubs - two or three squashed cubes clustered together.
            foreach (Vector2 p in ScatterPoints(ShrubDensity, 0.6f, 0.5f))
            {
                int lobes = Random.Range(2, 4);
                for (int i = 0; i < lobes; i++)
                {
                    float s = Random.Range(0.35f, 0.70f);
                    Vector2 offset = Random.insideUnitCircle * 0.25f;
                    AddPrimitive(foliageBatch, PrimitiveType.Cube,
                                 new Vector3(p.x + offset.x, s * 0.4f, p.y + offset.y),
                                 new Vector3(s, s * 0.8f, s),
                                 Quaternion.Euler(Random.Range(-12f, 12f), Random.Range(0f, 360f), Random.Range(-12f, 12f)));
                }
            }

            // Rocks - single rotated cubes, part-buried so they read as embedded.
            foreach (Vector2 p in ScatterPoints(RockDensity, 0.5f, 0.4f))
            {
                float s = Random.Range(0.18f, 0.45f);
                AddPrimitive(rockBatch, PrimitiveType.Cube,
                             new Vector3(p.x, s * 0.25f, p.y),
                             new Vector3(s, s * Random.Range(0.6f, 1.0f), s * Random.Range(0.7f, 1.3f)),
                             Quaternion.Euler(Random.Range(-25f, 25f), Random.Range(0f, 360f), Random.Range(-25f, 25f)));
            }

            // Grass tufts - tiny cubes, the cheapest way to break up the flat green.
            foreach (Vector2 p in ScatterPoints(TuftDensity, 0.35f, 0.3f))
            {
                float s = Random.Range(0.10f, 0.22f);
                AddPrimitive(tuftBatch, PrimitiveType.Cube,
                             new Vector3(p.x, s * 0.6f, p.y),
                             new Vector3(s, s * Random.Range(1.4f, 2.6f), s),
                             Quaternion.Euler(Random.Range(-18f, 18f), Random.Range(0f, 360f), Random.Range(-18f, 18f)));
            }

            Random.state = previous;

            EndBatch(barkBatch,    group, "Tree_Trunks", "TreeTrunkMesh", bark);
            EndBatch(foliageBatch, group, "Foliage",     "FoliageMesh",   foliage);
            EndBatch(tuftBatch,    group, "GrassTufts",  "GrassTuftMesh", tuft);
            EndBatch(rockBatch,    group, "Rocks",       "RockMesh",      rock);
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

                    if (IsClearOfPaths(p, pathMargin)) points.Add(p);
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
            // When Checkpoint.cs is added to the project, restore:
            //     var cp = go.AddComponent<Checkpoint>();
            //     cp.checkpointId = name;
            //     cp.isEndZone = isEndZone;
        }

        private static void BuildSpawnAndLight(Transform root)
        {
            var spawn = new GameObject("PlayerSpawn");
            spawn.transform.SetParent(root, false);
            spawn.transform.localPosition = new Vector3(0f, 0f, -4.5f);
            spawn.transform.localRotation = Quaternion.identity; // facing +Z, down the path

            if (FindAnyLight() == null)
            {
                var lightGo = new GameObject("Directional Light");
                lightGo.transform.SetParent(root, false);
                lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.1f;
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
