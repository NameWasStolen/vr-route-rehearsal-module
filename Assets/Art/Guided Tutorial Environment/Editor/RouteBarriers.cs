using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace VRTutorial.EditorTools
{
    /// <summary>
    /// Adds a low clipped hedge along both edges of the zigzagging east arm, so a participant
    /// who drifts off the paving is stopped by something they can see rather than by an
    /// invisible wall or by nothing at all.
    ///
    /// Why a hedge at 0.9 m: it has to read as "not through here" from a standing adult's eye
    /// height without cutting the sightline to the bus shelter at the end zone. A 1.2 m fence
    /// does the first job and fails the second, and an invisible collider fails both - for an
    /// older participant, walking into nothing reads as the program being broken.
    ///
    /// Why only the east arm: the four-way intersection is where the route decision is made,
    /// and the west and north arms are the wrong answers. Walling those would remove the
    /// wrong turn from the study rather than measure it. The zigzag contains no decision at
    /// all, so a barrier there only removes shortcutting, which is noise.
    ///
    /// The hedge is ONE SWEPT SURFACE per side, not a row of boxes. See BuildSweep.
    ///
    /// This is ADDITIVE and does not touch anything else in the scene. It builds one group,
    /// "RouteBarriers", under the existing TutorialEnvironment root, and re-running it
    /// replaces just that group. GuidedTutorialEnvironment.Build() does not know about it, so
    /// a full environment rebuild will drop it - run this again afterwards.
    ///
    /// Menu: Tools > VR Tutorial > Route Barriers > ...
    /// </summary>
    public static class RouteBarriers
    {
        private const string RootName        = "TutorialEnvironment";
        private const string GroupName       = "RouteBarriers";
        private const string BaseFolder      = "Assets/VRTutorial";
        private const string MaterialFolder  = BaseFolder + "/Materials";
        private const string GeneratedFolder = BaseFolder + "/Generated";
        private const string MeshAssetName   = "RouteBarrierMesh";

        // --------------------------------------------------------------- LAYOUT
        #region LAYOUT  (edit these to retune the barrier)

        /// <summary>Finished height of the planting. 0.9 m blocks the body, not the view.</summary>
        private const float HedgeHeight = 0.90f;

        /// <summary>Front-to-back depth of the hedge.</summary>
        private const float HedgeThickness = 0.50f;

        /// <summary>
        /// Gap between the paving edge and the near face of the hedge. The paving corridor is
        /// 2 m and the player's CharacterController is 1 m across (radius 0.5) with an 8 cm
        /// skin, so at 0.15 the walkable gap is about 2.3 m - roughly one body width of slack
        /// either side. Dropping this to 0 makes the corridor feel like a tunnel and makes the
        /// inside of every corner catch.
        /// </summary>
        private const float Clearance = 0.15f;

        /// <summary>
        /// Collider height. Only needs to clear the player capsule's step offset; it is set
        /// slightly above the visual so there is no chance of clipping the crown.
        /// </summary>
        private const float ColliderHeight = 1.00f;

        /// <summary>Resolution the path outline is traced at. 0.1 m divides every coordinate exactly.</summary>
        private const float SampleStep = 0.10f;

        /// <summary>Distance between cross-sections along a straight run. Sets how fine the wobble reads.</summary>
        private const float RingSpacing = 0.45f;

        /// <summary>Segments across the rounded crown. 6 is smooth at arm's length; 3 shows facets.</summary>
        private const int CrownSegments = 6;

        /// <summary>Metres per texture tile, matching the perimeter hedge.</summary>
        private const float MetresPerTile = 1.5f;

        /// <summary>
        /// The seven zigzag rectangles, copied verbatim from PathRects[3..9] in
        /// GuidedTutorialEnvironment. Format: (xMin, zMin, xMax, zMax).
        /// If you reshape the zigzag there, update these to match.
        /// </summary>
        private static readonly Vector4[] RouteRects =
        {
            new Vector4( 1f,   19f,  7f,   21f),  // ez1: leaving the intersection
            new Vector4( 5f,   19f,  7f,   24f),  // ez2: bend north
            new Vector4( 5f,   22f, 12f,   24f),  // ez3: east along the top
            new Vector4(10f,   16f, 12f,   24f),  // ez4: bend south, crossing back down
            new Vector4(10f,   16f, 17f,   18f),  // ez5: east along the bottom
            new Vector4(15f,   16f, 17f,   21f),  // ez6: bend back to centre
            new Vector4(15f,   19f, 21f,   21f),  // ez7: into the end zone
        };

        /// <summary>
        /// Paved areas the zigzag opens onto at each end. Any stretch of outline that faces
        /// one of these is a doorway, not a boundary, and gets no hedge - which is what keeps
        /// the entrance at the intersection and the exit into the end zone clear without
        /// hard-coding either of them.
        /// </summary>
        private static readonly Vector4[] AdjoiningRects =
        {
            new Vector4(-1f,  -1f,  1f, 31f),   // main path + intersection + north arm
            new Vector4(21f,  15f, 31f, 25f),   // end zone paving
        };

        #endregion

        // ------------------------------------------------------------ menu items
        [MenuItem("Tools/VR Tutorial/Route Barriers/Add Barriers Along Zigzag", false, 40)]
        public static void AddBarriers()
        {
            var root = GameObject.Find(RootName);
            if (root == null)
            {
                Debug.LogError("[RouteBarriers] No '" + RootName + "' in the open scene. " +
                               "Open Tutorial.unity, or build the environment first.");
                return;
            }

            EnsureFolder(MaterialFolder);
            EnsureFolder(GeneratedFolder);

            var old = root.transform.Find(GroupName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            var group = new GameObject(GroupName);
            group.transform.SetParent(root.transform, false);
            Undo.RegisterCreatedObjectUndo(group, "Add Route Barriers");

            List<Polyline> chains = Chain(TraceOutline());
            if (chains.Count == 0)
            {
                Debug.LogWarning("[RouteBarriers] Traced no boundary. Check RouteRects.");
                return;
            }

            var colliders = new GameObject("RouteBarriers_Colliders");
            colliders.transform.SetParent(group.transform, false);

            var verts = new List<Vector3>();
            var uvs   = new List<Vector2>();
            var tris  = new List<int>();

            float total = 0f;
            int   index = 0;

            foreach (Polyline chain in chains)
            {
                Vector2[] centres, across;
                MitreOffset(chain, out centres, out across);

                total += BuildSweep(chain, centres, across, verts, uvs, tris);
                BuildColliders(colliders.transform, centres, ref index);
            }

            var mesh = new Mesh { name = MeshAssetName, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();   // rings share vertices, so this comes out smooth
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();

            string meshPath = GeneratedFolder + "/" + MeshAssetName + ".asset";
            AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(mesh, meshPath);

            var visual = new GameObject("RouteBarriers_Visual");
            visual.transform.SetParent(group.transform, false);
            visual.AddComponent<MeshFilter>().sharedMesh = mesh;
            visual.AddComponent<MeshRenderer>().sharedMaterial = LoadHedgeMaterial();
            visual.isStatic = true;

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = group;

            WarnOnClashes(colliders.transform);

            Debug.Log(string.Format(
                "[RouteBarriers] {0} continuous hedge runs, {1:0.0} m total, {2:0.00} m high, " +
                "set {3:0.00} m back from the paving (walkable corridor about {4:0.00} m). " +
                "{5} vertices in one mesh.",
                chains.Count, total, HedgeHeight, Clearance, 2f + Clearance * 2f, verts.Count));
        }

        [MenuItem("Tools/VR Tutorial/Route Barriers/Remove Barriers", false, 41)]
        public static void RemoveBarriers()
        {
            var root = GameObject.Find(RootName);
            var existing = root == null ? null : root.transform.Find(GroupName);
            if (existing == null)
            {
                Debug.Log("[RouteBarriers] Nothing to remove.");
                return;
            }

            Undo.DestroyObjectImmediate(existing.gameObject);
            EditorSceneManager.MarkSceneDirty(root.scene);
        }

        // --------------------------------------------------------------- outline
        private struct Seg
        {
            public Vector2 A, B;     // endpoints on the paving edge
            public Vector2 N;        // outward normal, independent of which way the segment is walked
        }

        /// <summary>An unbroken chain of boundary segments. Normals[k] belongs to Points[k] -> Points[k+1].</summary>
        private class Polyline
        {
            public readonly List<Vector2> Points  = new List<Vector2>();
            public readonly List<Vector2> Normals = new List<Vector2>();
        }

        /// <summary>
        /// Traces the outline of the union of the seven zigzag rectangles.
        ///
        /// Done by sampling rather than by listing segments by hand, because the rectangles
        /// overlap at every bend: an edge of one is often buried inside its neighbour, and a
        /// hand-written list gets that wrong in exactly the places that matter - the corners.
        /// (The existing KerbLines have this problem: the run at x = 4.92 from z = 19 to 24
        /// crosses the walking lane at the first bend, which is harmless at 5 cm and would not
        /// be at 90.)
        ///
        /// A sample survives if the point just outside it is not inside any route rectangle
        /// and not inside the paving the route opens onto. Surviving samples are merged into
        /// straight segments, deduplicated across rectangles that share an edge.
        /// </summary>
        private static List<Seg> TraceOutline()
        {
            // key: "axis|lineCentimetres|outwardSign" -> set of sample indices along the run axis
            var groups = new Dictionary<string, SortedSet<int>>();

            foreach (Vector4 r in RouteRects)
            {
                AddEdgeSamples(groups, 'Z', r.y, r.x, r.z, -1);  // south
                AddEdgeSamples(groups, 'Z', r.w, r.x, r.z, +1);  // north
                AddEdgeSamples(groups, 'X', r.x, r.y, r.w, -1);  // west
                AddEdgeSamples(groups, 'X', r.z, r.y, r.w, +1);  // east
            }

            var segs = new List<Seg>();

            foreach (var entry in groups)
            {
                string[] parts = entry.Key.Split('|');
                char axis  = parts[0][0];
                float line = int.Parse(parts[1]) * 0.01f;
                int sign   = int.Parse(parts[2]);

                int runStart = int.MinValue;
                int previous = int.MinValue;

                foreach (int k in entry.Value)
                {
                    if (k != previous + 1)
                    {
                        if (runStart != int.MinValue)
                            AddSeg(segs, axis, line, sign, runStart * SampleStep, (previous + 1) * SampleStep);
                        runStart = k;
                    }
                    previous = k;
                }

                if (runStart != int.MinValue)
                    AddSeg(segs, axis, line, sign, runStart * SampleStep, (previous + 1) * SampleStep);
            }

            return segs;
        }

        private static void AddEdgeSamples(Dictionary<string, SortedSet<int>> groups,
                                           char axis, float line, float from, float to, int sign)
        {
            int kFrom = Mathf.FloorToInt(from / SampleStep);
            int kTo   = Mathf.CeilToInt(to / SampleStep);

            for (int k = kFrom; k <= kTo; k++)
            {
                float c = (k + 0.5f) * SampleStep;
                if (c < from - 0.0001f || c > to + 0.0001f) continue;

                float px = axis == 'Z' ? c : line + sign * 0.06f;
                float pz = axis == 'Z' ? line + sign * 0.06f : c;

                if (Inside(RouteRects, px, pz) || Inside(AdjoiningRects, px, pz)) continue;

                string key = axis + "|" + Mathf.RoundToInt(line * 100f) + "|" + sign;

                SortedSet<int> set;
                if (!groups.TryGetValue(key, out set))
                {
                    set = new SortedSet<int>();
                    groups[key] = set;
                }
                set.Add(k);
            }
        }

        private static void AddSeg(List<Seg> segs, char axis, float line, int sign, float from, float to)
        {
            if (to - from < 0.15f) return;

            segs.Add(new Seg
            {
                A = axis == 'Z' ? new Vector2(from, line) : new Vector2(line, from),
                B = axis == 'Z' ? new Vector2(to,   line) : new Vector2(line, to),
                N = axis == 'Z' ? new Vector2(0f, sign)   : new Vector2(sign, 0f),
            });
        }

        private static bool Inside(Vector4[] rects, float x, float z)
        {
            foreach (Vector4 r in rects)
                if (x > r.x && x < r.z && z > r.y && z < r.w) return true;
            return false;
        }

        // ---------------------------------------------------------------- chains
        /// <summary>
        /// Joins the loose boundary segments end to end. The zigzag outline comes out as two
        /// open chains - the south side and the north side - broken by the two doorways.
        ///
        /// Chaining is what makes a smooth corner possible at all. Treating each segment as
        /// its own object leaves the corner to be patched by overlapping two pieces, which is
        /// exactly the seam this is meant to avoid.
        /// </summary>
        private static List<Polyline> Chain(List<Seg> segs)
        {
            var incident = new Dictionary<long, List<int>>();
            for (int i = 0; i < segs.Count; i++)
            {
                Add(incident, Key(segs[i].A), i);
                Add(incident, Key(segs[i].B), i);
            }

            var used = new bool[segs.Count];
            var chains = new List<Polyline>();

            // Start from a free end wherever there is one, so open chains come out whole.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < segs.Count; i++)
                {
                    if (used[i]) continue;

                    Vector2 start;
                    if (pass == 0)
                    {
                        bool aFree = incident[Key(segs[i].A)].Count == 1;
                        bool bFree = incident[Key(segs[i].B)].Count == 1;
                        if (!aFree && !bFree) continue;
                        start = aFree ? segs[i].A : segs[i].B;
                    }
                    else
                    {
                        start = segs[i].A;   // closed loop: anywhere will do
                    }

                    var chain = new Polyline();
                    chain.Points.Add(start);

                    Vector2 at = start;
                    while (true)
                    {
                        int next = -1;
                        foreach (int candidate in incident[Key(at)])
                            if (!used[candidate]) { next = candidate; break; }

                        if (next < 0) break;

                        used[next] = true;
                        Vector2 other = Near(segs[next].A, at) ? segs[next].B : segs[next].A;
                        chain.Points.Add(other);
                        chain.Normals.Add(segs[next].N);
                        at = other;

                        if (Near(at, start)) break;   // closed the loop
                    }

                    if (chain.Normals.Count > 0) chains.Add(Orient(chain));
                }
            }

            return chains;
        }

        /// <summary>
        /// Walks the chain so that forward x outward always points up. Chaining can pick
        /// either direction, and the sweep's triangle winding depends on it - get this wrong
        /// and one of the two hedges renders inside out.
        /// </summary>
        private static Polyline Orient(Polyline chain)
        {
            Vector2 d = (chain.Points[1] - chain.Points[0]).normalized;
            Vector2 n = chain.Normals[0];
            if (d.x * n.y - d.y * n.x < 0f) return chain;   // already right-handed

            var flipped = new Polyline();
            for (int i = chain.Points.Count - 1; i >= 0; i--) flipped.Points.Add(chain.Points[i]);
            for (int i = chain.Normals.Count - 1; i >= 0; i--) flipped.Normals.Add(chain.Normals[i]);
            return flipped;
        }

        private static long Key(Vector2 p)
        {
            return (long)Mathf.RoundToInt(p.x * 100f) * 100000L + Mathf.RoundToInt(p.y * 100f);
        }

        private static bool Near(Vector2 a, Vector2 b) { return (a - b).sqrMagnitude < 0.0001f; }

        private static void Add(Dictionary<long, List<int>> map, long key, int value)
        {
            List<int> list;
            if (!map.TryGetValue(key, out list)) { list = new List<int>(); map[key] = list; }
            list.Add(value);
        }

        // ---------------------------------------------------------------- mitres
        /// <summary>
        /// Pushes the chain away from the paving and works out the cross-section axis at every
        /// vertex.
        ///
        /// The corner is the whole problem. Offsetting two edges independently opens a notch
        /// on the outside of a bend and overlaps them on the inside, which is what produced
        /// the visible seams. The fix is the standard mitre: the offset lines of the two edges
        /// are intersected, giving one shared point, and the cross-section at that point is
        /// laid in the bisector plane and widened by the same factor. One surface passes
        /// through the corner with no join in it at all.
        ///
        /// (n1 + n2) / (1 + n1.n2) is that intersection. For the 90 degree bends here the
        /// dot product is 0, so the point sits at 1.41 x the offset along the diagonal -
        /// exactly where the two faces meet.
        /// </summary>
        private static void MitreOffset(Polyline chain, out Vector2[] centres, out Vector2[] across)
        {
            int count = chain.Points.Count;
            centres = new Vector2[count];
            across  = new Vector2[count];

            float offset = Clearance + HedgeThickness * 0.5f;

            for (int i = 0; i < count; i++)
            {
                Vector2 nIn  = chain.Normals[Mathf.Max(0, i - 1)];
                Vector2 nOut = chain.Normals[Mathf.Min(chain.Normals.Count - 1, i)];

                float dot = Vector2.Dot(nIn, nOut);
                Vector2 a = dot < -0.95f ? nIn : (nIn + nOut) / (1f + dot);

                across[i]  = a;
                centres[i] = chain.Points[i] + a * offset;
            }
        }

        // ----------------------------------------------------------------- sweep
        /// <summary>
        /// Extrudes one cross-section along the whole chain as a single continuous surface.
        ///
        /// The earlier version stacked a cube per metre with two spheres on top for a crown.
        /// That reads badly up close for two reasons: the spheres were allowed to be wider
        /// than the cube, so they bulged through its side faces as visible circles, and at a
        /// corner two of those stacks were overlapped to fill the gap, so you could see one
        /// box passing through another. Sweeping removes both - there is one skin, and its
        /// corners are mitred, so there is nothing to intersect.
        ///
        /// Width and height are modulated by two out-of-phase sine waves against distance
        /// along the chain, which keeps the silhouette from being a ruled line. Because they
        /// are driven by arc length and every corner is a single shared cross-section, the
        /// wobble stays continuous through the bends.
        /// </summary>
        private static float BuildSweep(Polyline chain, Vector2[] centres, Vector2[] across,
                                        List<Vector3> verts, List<Vector2> uvs, List<int> tris)
        {
            List<Vector2> profile = BuildProfile();
            float[] profileS = ProfileArcLength(profile);

            // Cross-section stations: one at every mitred vertex, plus intermediates along
            // each edge so the wobble has somewhere to happen.
            var stationPos = new List<Vector2>();
            var stationAxis = new List<Vector2>();
            var stationS = new List<float>();

            stationPos.Add(centres[0]);
            stationAxis.Add(across[0]);
            stationS.Add(0f);

            float travelled = 0f;

            for (int k = 0; k < centres.Length - 1; k++)
            {
                float length = Vector2.Distance(centres[k], centres[k + 1]);
                int steps = Mathf.Max(1, Mathf.RoundToInt(length / RingSpacing));

                for (int j = 1; j < steps; j++)
                {
                    float t = j / (float)steps;
                    stationPos.Add(Vector2.Lerp(centres[k], centres[k + 1], t));
                    stationAxis.Add(chain.Normals[k]);
                    stationS.Add(travelled + length * t);
                }

                travelled += length;
                stationPos.Add(centres[k + 1]);
                stationAxis.Add(across[k + 1]);
                stationS.Add(travelled);
            }

            int ringSize = profile.Count;
            int baseIndex = verts.Count;

            for (int i = 0; i < stationPos.Count; i++)
            {
                float s = stationS[i];
                float widthScale  = 1f + 0.05f * Mathf.Sin(s * 1.9f)        + 0.035f * Mathf.Sin(s * 4.7f + 2.1f);
                float heightScale = 1f + 0.04f * Mathf.Sin(s * 1.3f + 0.9f) + 0.030f * Mathf.Sin(s * 3.1f + 2.7f);

                for (int j = 0; j < ringSize; j++)
                {
                    Vector2 p = stationPos[i] + stationAxis[i] * (profile[j].x * widthScale);
                    verts.Add(new Vector3(p.x, profile[j].y * heightScale, p.y));
                    uvs.Add(new Vector2(s / MetresPerTile, profileS[j] / MetresPerTile));
                }
            }

            for (int i = 0; i < stationPos.Count - 1; i++)
            {
                for (int j = 0; j < ringSize - 1; j++)
                {
                    int i0 = baseIndex + i * ringSize + j;
                    int i1 = i0 + 1;
                    int i2 = i0 + ringSize;
                    int i3 = i2 + 1;

                    tris.Add(i0); tris.Add(i2); tris.Add(i3);
                    tris.Add(i0); tris.Add(i3); tris.Add(i1);
                }
            }

            // Caps, so the open ends at the two doorways are not hollow.
            int last = baseIndex + (stationPos.Count - 1) * ringSize;
            for (int j = 1; j < ringSize - 1; j++)
            {
                tris.Add(baseIndex); tris.Add(baseIndex + j); tris.Add(baseIndex + j + 1);
                tris.Add(last);      tris.Add(last + j + 1);  tris.Add(last + j);
            }

            return travelled;
        }

        /// <summary>
        /// Cross-section: two vertical faces and a half-round crown. The crown radius is the
        /// half-thickness, so the dome meets the sides tangentially - no lip, no place for a
        /// hard edge to catch the light.
        /// </summary>
        private static List<Vector2> BuildProfile()
        {
            float hw = HedgeThickness * 0.5f;
            var profile = new List<Vector2> { new Vector2(-hw, 0f) };

            for (int k = 0; k <= CrownSegments; k++)
            {
                float angle = Mathf.PI * (1f - k / (float)CrownSegments);
                profile.Add(new Vector2(hw * Mathf.Cos(angle),
                                        (HedgeHeight - hw) + hw * Mathf.Sin(angle)));
            }

            profile.Add(new Vector2(hw, 0f));
            return profile;
        }

        private static float[] ProfileArcLength(List<Vector2> profile)
        {
            var s = new float[profile.Count];
            for (int i = 1; i < profile.Count; i++)
                s[i] = s[i - 1] + Vector2.Distance(profile[i - 1], profile[i]);
            return s;
        }

        // ------------------------------------------------------------- colliders
        /// <summary>
        /// One box per edge of the mitred centre line. Boxes overlap at each corner, which is
        /// what you want from a collider - a flat face to slide along and no gap to squeeze
        /// into. The visual mesh is far too fine to use as a collider and does not need to be:
        /// the player only ever touches the sides.
        /// </summary>
        private static void BuildColliders(Transform parent, Vector2[] centres, ref int index)
        {
            for (int k = 0; k < centres.Length - 1; k++)
            {
                Vector2 a = centres[k], b = centres[k + 1];
                float length = Vector2.Distance(a, b);
                if (length < 0.05f) continue;

                Vector2 mid = (a + b) * 0.5f;
                Vector3 dir = new Vector3(b.x - a.x, 0f, b.y - a.y).normalized;

                var go = new GameObject("BarrierCollider_" + index.ToString("00"));
                go.transform.SetParent(parent, false);
                go.transform.localPosition = new Vector3(mid.x, ColliderHeight * 0.5f, mid.y);
                go.transform.localRotation = Quaternion.LookRotation(dir, Vector3.up);

                var box = go.AddComponent<BoxCollider>();
                box.size = new Vector3(HedgeThickness, ColliderHeight, length);
                go.isStatic = true;

                index++;
            }
        }

        // -------------------------------------------------------------- plumbing
        /// <summary>
        /// Reports anything already in the scene that a new barrier now overlaps - most likely
        /// a lamp post or a landmark that was placed close to the path edge. Not an error; a
        /// post standing in a hedge is normal. It just should not be a surprise in the headset.
        /// </summary>
        private static void WarnOnClashes(Transform colliderGroup)
        {
            Physics.SyncTransforms();

            var reported = new HashSet<string>();

            foreach (var box in colliderGroup.GetComponentsInChildren<BoxCollider>())
            {
                Collider[] hits = Physics.OverlapBox(
                    box.transform.TransformPoint(box.center),
                    box.size * 0.5f,
                    box.transform.rotation,
                    ~0, QueryTriggerInteraction.Ignore);

                foreach (var hit in hits)
                {
                    if (hit == box) continue;
                    if (hit.transform.IsChildOf(colliderGroup)) continue;
                    if (!reported.Add(hit.name)) continue;

                    Debug.LogWarning("[RouteBarriers] Barrier overlaps '" + hit.name +
                                     "' - worth an eyeball in the Scene view.", hit.gameObject);
                }
            }
        }

        private static Material LoadHedgeMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/M_Hedge.mat");
            if (mat != null) return mat;

            Debug.LogWarning("[RouteBarriers] M_Hedge.mat not found - building the environment " +
                             "once will create it. Using a plain green stand-in for now.");

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(shader) { name = "M_RouteHedge" };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.16f, 0.31f, 0.15f));
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     new Color(0.16f, 0.31f, 0.15f));
            AssetDatabase.CreateAsset(mat, MaterialFolder + "/M_RouteHedge.mat");
            return mat;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;

            var parts = folder.Split('/');
            string current = parts[0];
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
