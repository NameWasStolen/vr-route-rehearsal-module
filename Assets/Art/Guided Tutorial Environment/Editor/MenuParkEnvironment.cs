using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

namespace VRTutorial.EditorTools
{
    // partial: reuses the tutorial builder's materials, trees, bench, batching and sky, so the
    // menu park looks like the same world the participant is about to practise in.
    public static partial class VRTutorialSceneBuilder
    {
        /// <summary>
        /// Builds a quiet park around the main menu, replacing the bare grey floor.
        ///
        /// The participant stands on a small paved square facing the menu (which floats 6 m ahead,
        /// its bottom edge 0.5 m off the ground). Around them: lawn in three soft greens, two low
        /// flower beds either side of a path that runs under the menu to a cross path behind it,
        /// benches, scattered shrubs and a loose ring of trees, under the tutorial's sky with its
        /// fog softening the distance, and the run's birdsong played quietly.
        ///
        /// Kept CALM ON PURPOSE. The menu is what they have to read: nothing tall or bright sits
        /// directly behind it, flowers are the tutorial's soft colours, and everything else is
        /// green, grey and timber. Nothing moves.
        ///
        /// Everything goes under one root, MenuEnvironment, in MainMenu.unity. MenuController shows
        /// it only while the participant is in the menu area: the Map (500 m east) and the runs
        /// (500 m south) share the same world, so it would otherwise sit on their horizon.
        ///
        /// Safe to re-run: the old MenuEnvironment is replaced. The scene's old grey Floor is
        /// switched off (not deleted), so it is easy to bring back.
        /// </summary>
        [MenuItem("Tools/VR Full Route/Build Main Menu Park", false, 30)]
        public static void BuildMenuPark()
        {
            MenuPark.Build();
        }

        private static class MenuPark
        {
            private const string ScenePath = "Assets/Scenes/MainMenu.unity";
            private const string Root = "MenuEnvironment";                  // = MenuController.EnvironmentRootName
            private const string AmbienceClipPath = "Assets/Audio/Ambience/Ambience_Suburb_Morning.mp3";
            private const int Seed = 20261009;

            // ---------------------------------------------------------------- LAYOUT (metres)
            // Player stands at the origin facing +Z; the menu panel is at z = 6, x = -3..3.
            private const float LawnHalf = 150f;     // past the fog's end (130 m), so no edge shows
            private const float LawnTile = 25f;

            private static readonly Rect Pad = Rect.MinMaxRect(-1.8f, -1.6f, 1.8f, 2.0f);
            // Paths: x0, z0, x1, z1.
            private static readonly Rect[] Paths =
            {
                Rect.MinMaxRect(-0.8f, 2.0f, 0.8f, 10.2f),      // from the square, under the menu
                Rect.MinMaxRect(-70f, 10.2f, 70f, 12.0f),       // the cross path behind it
                Rect.MinMaxRect(1.8f, -0.4f, 3.4f, 1.0f),       // a short spur to the right-hand bench
            };
            // Flower beds either side of the path, below the menu.
            private static readonly Rect[] Beds =
            {
                Rect.MinMaxRect(-3.2f, 2.6f, -1.3f, 4.8f),
                Rect.MinMaxRect(1.3f, 2.6f, 3.2f, 4.8f),
                Rect.MinMaxRect(-4.8f, -3.6f, -2.8f, -1.6f),    // one behind-left, seen when turning
            };
            // Benches: position, and the point they face.
            private static readonly Vector3[] BenchAt =
            {
                new Vector3(4.6f, 0f, 0.3f),
                new Vector3(-5.5f, 0f, 13.1f),
                new Vector3(6.0f, 0f, 13.1f),
            };
            private static readonly Vector3[] BenchFaces =
            {
                Vector3.zero,
                new Vector3(-5.5f, 0f, 0f),
                new Vector3(6.0f, 0f, 0f),
            };
            // A few nearer trees, placed by hand so the square feels sheltered rather than exposed.
            private static readonly Vector2[] FeatureTrees =
            {
                new Vector2(-7.5f, 7.5f), new Vector2(8.5f, 6.5f), new Vector2(-9.5f, -1.5f),
                new Vector2(9.0f, -6.0f), new Vector2(-4.5f, -9.5f), new Vector2(4.0f, -11.0f),
                new Vector2(13.5f, 15.5f), new Vector2(-14.0f, 16.0f),
            };

            public static void Build()
            {
                // ---- the menu scene, open and active (RenderSettings belong to the active scene)
                Scene scene = SceneManager.GetSceneByPath(ScenePath);
                if (!scene.isLoaded)
                {
                    if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                    scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                }
                if (!scene.IsValid())
                {
                    EditorUtility.DisplayDialog("Build Main Menu Park", "Could not open " + ScenePath + ".", "OK");
                    return;
                }
                SceneManager.SetActiveScene(scene);

                EnsureFolder(MaterialFolder);
                EnsureFolder(GeneratedFolder);

                foreach (GameObject go in scene.GetRootGameObjects())
                {
                    if (go.name == Root) Undo.DestroyObjectImmediate(go);
                }
                foreach (GameObject go in scene.GetRootGameObjects())
                {
                    if (go.name == "Floor" && go.activeSelf)
                    {
                        Undo.RecordObject(go, "Hide old floor");
                        go.SetActive(false);
                        Debug.Log("[MenuPark] Switched off the old grey 'Floor' in MainMenu (not deleted) - the lawn replaces it.");
                    }
                }

                var root = new GameObject(Root);
                SceneManager.MoveGameObjectToScene(root, scene);
                Undo.RegisterCreatedObjectUndo(root, "Build Main Menu Park");

                // ---- materials: the tutorial's own, same names and colours (existing textures kept)
                Material[] grass   = Variants("M_Grass",  new Color(0.30f, 0.47f, 0.22f), 0.05f, 3, 0.045f);
                Material[] stone   = Variants("M_Stone",  new Color(0.66f, 0.65f, 0.62f), 0.10f, 3, 0.05f);
                Material   edge    = GetOrCreateMaterial("M_PathEdge",   new Color(0.55f, 0.54f, 0.51f), 0.12f);
                Material[] foliage = Variants("M_Foliage", new Color(0.20f, 0.38f, 0.18f), 0.05f, 3, 0.055f);
                Material[] flowers = FlowerMaterials();
                Material   tuft    = GetOrCreateMaterial("M_GrassTuft",  new Color(0.26f, 0.46f, 0.20f), 0.05f);
                Material   bark    = GetOrCreateMaterial("M_Bark",       new Color(0.34f, 0.26f, 0.19f), 0.05f);
                Material   rock    = GetOrCreateMaterial("M_Rock",       new Color(0.52f, 0.51f, 0.50f), 0.15f);
                Material   timber  = GetOrCreateMaterial("M_Timber",     new Color(0.52f, 0.42f, 0.31f), 0.08f);
                Material   metal   = GetOrCreateMaterial("M_FenceMetal", new Color(0.42f, 0.44f, 0.47f), 0.65f);
                Material   mulch   = GetOrCreateMaterial("M_Mulch",      new Color(0.29f, 0.22f, 0.16f), 0.04f);

                Random.State previous = Random.state;
                Random.InitState(Seed);
                try
                {
                    BuildWorldSettings();          // the tutorial's sky, ambient light and fog
                    BuildLawn(root.transform, grass);
                    BuildPaving(root.transform, stone, edge);
                    BuildBeds(root.transform, mulch, foliage, flowers);
                    BuildBenches(root.transform, timber, metal);
                    BuildPlanting(root.transform, bark, foliage, flowers, tuft, rock);
                    BuildAmbience(root.transform);
                }
                finally
                {
                    Random.state = previous;
                }

                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Selection.activeGameObject = root;

                Debug.Log("[MenuPark] Main menu park built and MainMenu.unity saved. It shows only while the " +
                          "participant is in the menu area (MenuController).");
            }

            // ------------------------------------------------------------ ground

            private static void BuildLawn(Transform root, Material[] grass)
            {
                var group = NewGroup("Lawn", root);
                var batches = new Transform[grass.Length];
                for (int i = 0; i < grass.Length; i++) batches[i] = BeginBatch(group);

                // Tiles side by side at one height, in three greens, so the lawn is mottled
                // without any surface lying on top of another (which would flicker in the distance).
                int n = Mathf.CeilToInt(LawnHalf * 2f / LawnTile);
                for (int ix = 0; ix < n; ix++)
                for (int iz = 0; iz < n; iz++)
                {
                    float x = -LawnHalf + (ix + 0.5f) * LawnTile;
                    float z = -LawnHalf + (iz + 0.5f) * LawnTile;
                    int v = (HashInt(ix, iz, Seed) & 0x7fffffff) % grass.Length;
                    AddPrimitive(batches[v], PrimitiveType.Cube, new Vector3(x, -0.1f, z),
                                 new Vector3(LawnTile, 0.2f, LawnTile), Quaternion.identity);
                }

                for (int i = 0; i < grass.Length; i++)
                    EndBatch(batches[i], group, "Lawn_" + i, "MenuPark_LawnMesh_" + i, grass[i], 3f);

                // One collider for the whole lawn: something solid under the participant's feet.
                var col = new GameObject("Lawn_Collider");
                col.transform.SetParent(group, false);
                col.transform.localPosition = new Vector3(0f, -0.1f, 0f);
                col.AddComponent<BoxCollider>().size = new Vector3(LawnHalf * 2f, 0.2f, LawnHalf * 2f);
                col.isStatic = true;
            }

            private static void BuildPaving(Transform root, Material[] stone, Material edge)
            {
                var group = NewGroup("Paving", root);
                var padBatch = BeginBatch(group);
                var pathBatch = BeginBatch(group);
                var edgeBatch = BeginBatch(group);

                // The square they stand on: 3 cm proud of the lawn, with a slightly raised edge.
                Slab(padBatch, Pad, 0.03f, 0.15f);
                Border(edgeBatch, Pad, 0.05f, 0.12f);

                // Paths sit 2 cm proud of the lawn and 1 cm below the square, so where they meet
                // there is a clear step of colour, never two surfaces at one height.
                foreach (Rect r in Paths) Slab(pathBatch, r, 0.02f, 0.12f);

                EndBatch(padBatch, group, "Square", "MenuPark_SquareMesh", stone[1], 1.2f);
                EndBatch(pathBatch, group, "Paths", "MenuPark_PathMesh", stone[0], 1.2f);
                EndBatch(edgeBatch, group, "Square_Edge", "MenuPark_SquareEdgeMesh", edge, 1.2f);
            }

            private static void BuildBeds(Transform root, Material mulch, Material[] foliage, Material[] flowers)
            {
                var group = NewGroup("FlowerBeds", root);
                var soil = BeginBatch(group);
                var bushes = new Transform[foliage.Length];
                for (int i = 0; i < foliage.Length; i++) bushes[i] = BeginBatch(group);
                var blooms = new Transform[flowers.Length];
                for (int i = 0; i < flowers.Length; i++) blooms[i] = BeginBatch(group);

                for (int b = 0; b < Beds.Length; b++)
                {
                    Rect r = Beds[b];
                    Slab(soil, r, 0.04f, 0.12f);

                    // A loose double row of flowering shrubs, kept low (under 0.7 m) so they never
                    // reach the bottom of the menu.
                    int cols = Mathf.Max(1, Mathf.RoundToInt(r.width / 0.75f));
                    int rows = Mathf.Max(1, Mathf.RoundToInt(r.height / 0.85f));
                    for (int cx = 0; cx < cols; cx++)
                    for (int cz = 0; cz < rows; cz++)
                    {
                        Vector2 p = new Vector2(
                            Mathf.Lerp(r.xMin, r.xMax, (cx + 0.5f) / cols) + Random.Range(-0.12f, 0.12f),
                            Mathf.Lerp(r.yMin, r.yMax, (cz + 0.5f) / rows) + Random.Range(-0.12f, 0.12f));
                        Transform bush = bushes[Random.Range(0, bushes.Length)];
                        // Each bed keeps to two colours, so it reads as planted, not random.
                        int colour = (b * 2 + ((cx + cz) & 1)) % flowers.Length;
                        AddShrub(bush, blooms[colour], p, Random.Range(0.55f, 0.8f), 0.04f);
                    }
                }

                EndBatch(soil, group, "Beds_Soil", "MenuPark_BedSoilMesh", mulch, 1f);
                for (int i = 0; i < bushes.Length; i++)
                    EndBatch(bushes[i], group, "Beds_Foliage_" + i, "MenuPark_BedFoliageMesh_" + i, foliage[i], 1.5f);
                for (int i = 0; i < blooms.Length; i++)
                    EndBatch(blooms[i], group, "Beds_Flowers" + FlowerBatchNames[i],
                             "MenuPark_BedFlowerMesh" + FlowerBatchNames[i], flowers[i]);
            }

            private static void BuildBenches(Transform root, Material timber, Material metal)
            {
                var group = NewGroup("Benches", root);
                var wood = BeginBatch(group);
                var iron = BeginBatch(group);

                for (int i = 0; i < BenchAt.Length; i++)
                {
                    Vector3 look = BenchFaces[i] - BenchAt[i];
                    look.y = 0f;
                    Quaternion rot = look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity;
                    AddBenchAndBin(wood, iron, BenchAt[i], rot);
                }

                EndBatch(wood, group, "Bench_Timber", "MenuPark_BenchTimberMesh", timber);
                EndBatch(iron, group, "Bench_Metal", "MenuPark_BenchMetalMesh", metal);
            }

            // ------------------------------------------------------------ trees and scatter

            private static void BuildPlanting(Transform root, Material bark, Material[] foliage,
                                              Material[] flowers, Material tuft, Material rock)
            {
                var group = NewGroup("Planting", root);
                var trunks = BeginBatch(group);
                var crowns = new Transform[foliage.Length];
                for (int i = 0; i < foliage.Length; i++) crowns[i] = BeginBatch(group);
                var blooms = new Transform[flowers.Length];
                for (int i = 0; i < flowers.Length; i++) blooms[i] = BeginBatch(group);
                var tufts = BeginBatch(group);
                var rocks = BeginBatch(group);

                var placed = new List<Vector2>();

                foreach (Vector2 p in FeatureTrees)
                {
                    AddTree(trunks, crowns[Random.Range(0, crowns.Length)], new Vector3(p.x, 0f, p.y),
                            Random.Range(5.6f, 7.2f), Random.Range(1.5f, 1.95f));
                    placed.Add(p);
                }

                // A loose ring further out. The fog softens the far ones into the sky.
                for (int tries = 0, made = 0; tries < 400 && made < 44; tries++)
                {
                    float r = Random.Range(17f, 55f);
                    float a = Random.Range(0f, Mathf.PI * 2f);
                    Vector2 p = new Vector2(Mathf.Sin(a) * r, Mathf.Cos(a) * r);
                    if (!IsOpenGround(p, 1.6f) || TooClose(p, placed, 6f)) continue;
                    AddTree(trunks, crowns[Random.Range(0, crowns.Length)], new Vector3(p.x, 0f, p.y),
                            Random.Range(5.0f, 7.6f), Random.Range(1.3f, 1.9f));
                    placed.Add(p);
                    made++;
                }

                // Shrubs on the lawn, some flowering.
                for (int tries = 0, made = 0; tries < 400 && made < 26; tries++)
                {
                    float r = Random.Range(6f, 24f);
                    float a = Random.Range(0f, Mathf.PI * 2f);
                    Vector2 p = new Vector2(Mathf.Sin(a) * r, Mathf.Cos(a) * r);
                    if (!IsOpenGround(p, 1.0f) || TooClose(p, placed, 2.2f)) continue;
                    // Never right behind the menu, where a shape would sit in the middle of the
                    // reading view.
                    if (Mathf.Abs(p.x) < 4.5f && p.y > 4f && p.y < 16f) continue;

                    Transform flowerBatch = Random.Range(0f, 1f) < 0.35f
                        ? blooms[(HashInt(Mathf.RoundToInt(p.x * 100f), Mathf.RoundToInt(p.y * 100f), 17) & 0x7fffffff) % blooms.Length]
                        : null;
                    AddShrub(crowns[Random.Range(0, crowns.Length)], flowerBatch, p, Random.Range(0.8f, 1.25f), 0f);
                    placed.Add(p);
                    made++;
                }

                // Grass tufts and a few rocks, nearer in, where they are actually seen.
                for (int i = 0; i < 260; i++)
                {
                    float r = Random.Range(2.6f, 22f);
                    float a = Random.Range(0f, Mathf.PI * 2f);
                    Vector2 p = new Vector2(Mathf.Sin(a) * r, Mathf.Cos(a) * r);
                    if (!IsOpenGround(p, 0.35f)) continue;

                    float h = Random.Range(0.14f, 0.30f);
                    float yaw = Random.Range(0f, 360f);
                    for (int k = 0; k < 3; k++)
                    {
                        AddPrimitive(tufts, PrimitiveType.Cube,
                                     new Vector3(p.x + Random.Range(-0.04f, 0.04f), h * 0.45f, p.y + Random.Range(-0.04f, 0.04f)),
                                     new Vector3(Random.Range(0.10f, 0.16f), h, 0.012f),
                                     Quaternion.Euler(Random.Range(-14f, 14f), yaw + k * 60f, Random.Range(-14f, 14f)));
                    }
                }
                for (int i = 0; i < 14; i++)
                {
                    float r = Random.Range(5f, 20f);
                    float a = Random.Range(0f, Mathf.PI * 2f);
                    Vector2 p = new Vector2(Mathf.Sin(a) * r, Mathf.Cos(a) * r);
                    if (!IsOpenGround(p, 0.6f)) continue;
                    float s = Random.Range(0.2f, 0.45f);
                    AddPrimitive(rocks, PrimitiveType.Cube, new Vector3(p.x, s * 0.25f, p.y),
                                 new Vector3(s, s * Random.Range(0.6f, 1.0f), s * Random.Range(0.7f, 1.3f)),
                                 Quaternion.Euler(Random.Range(-25f, 25f), Random.Range(0f, 360f), Random.Range(-25f, 25f)));
                }

                EndBatch(trunks, group, "Tree_Trunks", "MenuPark_TreeTrunkMesh", bark);
                for (int i = 0; i < crowns.Length; i++)
                    EndBatch(crowns[i], group, "Foliage_" + i, "MenuPark_FoliageMesh_" + i, foliage[i], 1.5f);
                for (int i = 0; i < blooms.Length; i++)
                    EndBatch(blooms[i], group, "Flowers" + FlowerBatchNames[i],
                             "MenuPark_FlowerMesh" + FlowerBatchNames[i], flowers[i]);
                EndBatch(tufts, group, "GrassTufts", "MenuPark_GrassTuftMesh", tuft);
                EndBatch(rocks, group, "Rocks", "MenuPark_RockMesh", rock);
            }

            /// <summary>
            /// A rounded shrub: a wide base sunk into the ground, a few side lobes and a crown, so
            /// nothing floats - the same rules as the tutorial's shrubs, at a chosen size. With a
            /// flower batch, blooms are set into its surface.
            /// </summary>
            private static void AddShrub(Transform bush, Transform flowerBatch, Vector2 p, float size, float groundY)
            {
                var lobeAt = new List<Vector3>();
                var lobeR = new List<float>();
                var lobeRV = new List<float>();

                int lobes = Random.Range(4, 7);
                float baseY = 0f, baseRV = 0f;
                for (int i = 0; i < lobes; i++)
                {
                    float squash = Random.Range(0.75f, 0.95f);
                    Vector2 unit = Random.insideUnitCircle;
                    float s, y;
                    Vector2 offset;
                    if (i == 0)
                    {
                        s = Random.Range(0.58f, 0.72f) * size;
                        offset = unit * 0.06f * size;
                        float rv = s * squash * 0.5f;
                        y = groundY + rv * 0.55f;
                        baseY = y;
                        baseRV = rv;
                    }
                    else if (i == lobes - 1)
                    {
                        s = Random.Range(0.30f, 0.44f) * size;
                        offset = unit * 0.10f * size;
                        y = baseY + baseRV * Random.Range(0.35f, 0.65f);
                    }
                    else
                    {
                        s = Random.Range(0.34f, 0.62f) * size * (1f - (i / (float)lobes) * 0.45f);
                        offset = unit * 0.32f * size;
                        float rv = s * squash * 0.5f;
                        y = groundY + rv * Random.Range(0.35f, 0.8f);
                    }

                    Vector3 at = new Vector3(p.x + offset.x, y, p.y + offset.y);
                    AddPrimitive(bush, PrimitiveType.Sphere, at, new Vector3(s, s * squash, s),
                                 Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                    lobeAt.Add(at);
                    lobeR.Add(s * 0.5f);
                    lobeRV.Add(s * squash * 0.5f);
                }

                if (flowerBatch == null) return;

                int count = Random.Range(7, 12);
                for (int i = 0; i < count; i++)
                {
                    int k = Random.Range(0, lobeAt.Count);
                    float ang = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                    float up = Random.Range(0.25f, 0.95f);
                    float flat = 1f - up;
                    Vector3 dir = new Vector3(Mathf.Sin(ang) * flat, up, Mathf.Cos(ang) * flat).normalized;
                    float a = lobeR[k], b = lobeRV[k];
                    float horiz2 = dir.x * dir.x + dir.z * dir.z;
                    float surface = 1f / Mathf.Sqrt(horiz2 / (a * a) + (dir.y * dir.y) / (b * b));
                    AddPrimitive(flowerBatch, PrimitiveType.Sphere, lobeAt[k] + dir * (surface * 0.85f),
                                 Vector3.one * Random.Range(0.07f, 0.12f), Quaternion.identity);
                }
            }

            // ------------------------------------------------------------ sound

            private static void BuildAmbience(Transform root)
            {
                var go = new GameObject("MenuAmbience");
                go.transform.SetParent(root, false);

                // Added by name, as the other builders do with runtime components.
                System.Type t = System.Type.GetType("RunAmbience, Assembly-CSharp");
                if (t == null)
                {
                    Debug.LogWarning("[MenuPark] RunAmbience not found, so the menu park is silent.");
                    return;
                }
                Component amb = go.AddComponent(t);
                var so = new SerializedObject(amb);
                SerializedProperty clip = so.FindProperty("clip");
                if (clip != null)
                    clip.objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(AmbienceClipPath);
                SerializedProperty vol = so.FindProperty("volume");
                if (vol != null) vol.floatValue = 0.15f;     // the runs use 0.25; quieter here
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // ------------------------------------------------------------ helpers

            private static void Slab(Transform batch, Rect r, float topY, float thickness)
            {
                AddPrimitive(batch, PrimitiveType.Cube,
                             new Vector3(r.center.x, topY - thickness * 0.5f, r.center.y),
                             new Vector3(r.width, thickness, r.height), Quaternion.identity);
            }

            private static void Border(Transform batch, Rect r, float topY, float width)
            {
                float h = 0.14f;
                float y = topY - h * 0.5f;
                AddPrimitive(batch, PrimitiveType.Cube, new Vector3(r.center.x, y, r.yMin - width * 0.5f),
                             new Vector3(r.width + width * 2f, h, width), Quaternion.identity);
                AddPrimitive(batch, PrimitiveType.Cube, new Vector3(r.xMin - width * 0.5f, y, r.center.y),
                             new Vector3(width, h, r.height), Quaternion.identity);
                AddPrimitive(batch, PrimitiveType.Cube, new Vector3(r.xMax + width * 0.5f, y, r.center.y),
                             new Vector3(width, h, r.height), Quaternion.identity);

                // The far side is left open where the path leaves the square.
                float gapHalf = 0.8f;
                float leftW = (0f - gapHalf) - r.xMin + width;
                float rightW = r.xMax - gapHalf + width;
                AddPrimitive(batch, PrimitiveType.Cube, new Vector3(r.xMin - width + leftW * 0.5f, y, r.yMax + width * 0.5f),
                             new Vector3(leftW, h, width), Quaternion.identity);
                AddPrimitive(batch, PrimitiveType.Cube, new Vector3(gapHalf + rightW * 0.5f, y, r.yMax + width * 0.5f),
                             new Vector3(rightW, h, width), Quaternion.identity);
            }

            /// <summary>Lawn that is not paving, a bed, a bench, or the square.</summary>
            private static bool IsOpenGround(Vector2 p, float margin)
            {
                if (Inside(Pad, p, margin + 0.3f)) return false;
                foreach (Rect r in Paths) if (Inside(r, p, margin)) return false;
                foreach (Rect r in Beds) if (Inside(r, p, margin)) return false;
                foreach (Vector3 b in BenchAt)
                    if (Vector2.Distance(p, new Vector2(b.x, b.z)) < 2.2f + margin) return false;
                return true;
            }

            private static bool Inside(Rect r, Vector2 p, float margin)
            {
                return p.x > r.xMin - margin && p.x < r.xMax + margin &&
                       p.y > r.yMin - margin && p.y < r.yMax + margin;
            }

            private static bool TooClose(Vector2 p, List<Vector2> others, float distance)
            {
                foreach (Vector2 o in others)
                    if ((o - p).sqrMagnitude < distance * distance) return true;
                return false;
            }
        }
    }
}
