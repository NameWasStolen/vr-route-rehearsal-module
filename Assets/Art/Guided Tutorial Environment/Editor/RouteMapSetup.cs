using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace VRTutorial.EditorTools
{
    /// <summary>
    /// Tools > VR Full Route > Build Route Map Scene (Map button)
    ///
    /// Generates Assets/Scenes/RouteMap.unity: the main menu's Map button, seen before the first
    /// unguided run. A tabletop model of the full route (decided with Kade, 2 Oct 2026):
    ///
    ///   - the real route, built from the same plan as RunSystem's copy, cut to the streets
    ///     around the route and shrunk onto a table (about 1:260);
    ///   - the table faces the participant with the bus stop nearest and the shopping centre at
    ///     the far end; locomotion is off (look only);
    ///   - the ideal route as a blue line, with a small orange figure walking it on a loop;
    ///   - landmarks labelled with an icon and English text on white cards (bus stop, blue house,
    ///     park, school, landmark lamps as icons only); the special house and lamps follow
    ///     Options > Include Landmarks, like the route itself;
    ///   - the shopping centre's card carries a picture of the end zone, taken at eye height from
    ///     the route as you arrive, on a pole down to the end zone;
    ///   - a small ring on the table's front edge empties over the 60 s visit.
    ///
    /// The scene is fully generated: re-run this after any change to the route (as with Install
    /// Route in RunSystem). It replaces RouteMap.unity each time and adds it to Build Settings.
    ///
    /// Runtime: RouteMapView (placing the table, timing, the figure, the return to the menu) and
    /// RouteMapLabel (cards that face the participant and grow with the font-size setting).
    /// </summary>
    public static class RouteMapSetup
    {
        const string ScenePath = "Assets/Scenes/RouteMap.unity";
        const string Folder = "Assets/FullRoute/RouteMap";
        const string IconFolder = Folder + "/Icons";
        const string MatFolder = Folder + "/Materials";
        const string PhotoPath = Folder + "/T_Map_EndZonePhoto.png";
        // BuildFR's own mesh folder for this build, deleted once the clipped copies exist.
        const string BuildMeshFolder = "Assets/FullRoute/Generated/RouteMap";
        const string ModelMeshFolder = "Assets/FullRoute/Generated/RouteMap_Model";

        // Away from the main menu (origin) and RunSystem's route (z = -500); fog hides both.
        static readonly Vector3 MapOrigin = new Vector3(500f, 0f, 0f);

        // ---- the model on the board (metres unless "plan", which is route metres)
        const float MaxModelDepth = 1.10f;   // along the participant's view
        const float MaxModelWidth = 0.95f;
        const float CropMargin = 8f;         // plan: around every street but the bus road
        const float StartReach = 25f;        // plan: bus road kept either side of the bus stop
        const float PlinthDepth = 6f;        // plan: the cut edge of the model
        const float BoardBorder = 0.04f;
        const float FrontLip = 0.09f;        // holds the time ring
        const float BoardThickness = 0.03f;
        const float BoardTilt = 15f;         // far edge raised, degrees

        // ---- route line and figure (plan)
        const float LineWidth = 3.0f, EdgeWidth = 4.4f, LineLift = 0.55f, EdgeLift = 0.45f;
        const float LineEndPast = 3f;        // into the forecourt past the end trigger

        // ---- labels (metres, above the model surface)
        const float IconSize = 0.034f, LampIconSize = 0.018f;
        const float LabelFontSize = 20f;     // TMP units at 0.01 scale: about 2 cm per line
        const float PoleDiameter = 0.003f;

        // ---- end-zone picture
        const float PhotoBackOff = 24f;      // plan metres back along the route from the end
        const float PhotoEye = 1.6f, PhotoFov = 55f;
        const int PhotoPixelsW = 1800, PhotoPixelsH = 1200;
        const float PhotoWidth = 0.34f, PhotoRaise = 0.14f;

        const float RingRadius = 0.03f;

        static readonly Color Navy = new Color(0.137f, 0.169f, 0.220f);

        [MenuItem("Tools/VR Full Route/Build Route Map Scene (Map button)", false, 4)]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (s_pending != null)
            {
                Debug.LogWarning("[RouteMap] A route map build is already waiting to take its picture.");
                return;
            }

            var job = new Job { Clock = System.Diagnostics.Stopwatch.StartNew() };
            job.Scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            bool waiting = false;
            try
            {
                EnsureFolder(Folder);
                EnsureFolder(IconFolder);
                EnsureFolder(MatFolder);

                EditorUtility.DisplayProgressBar("Route map", "Laying out the route...", 0.05f);
                job.Plan = new FullRoutePlan();
                job.Landmarks = VRTutorialSceneBuilder.FullRoute.LandmarksIncluded;

                EditorUtility.DisplayProgressBar("Route map", "Building the route...", 0.2f);
                job.Route = VRTutorialSceneBuilder.FullRoute.BuildForRouteMap(job.Plan);
                job.Def = job.Route.GetComponent<RouteDefinition>();
                if (job.Def == null || job.Def.PointCount < 2)
                {
                    EditorUtility.DisplayDialog("Route map", "The route was built without a walking line, so the map cannot be made.", "OK");
                    return;
                }
                job.RoutePoints = RoutePoints(job.Def);

                // The end-zone picture is taken a few Editor frames later. Taken straight away
                // (the first version did) it came out as sky only (Kade's screenshot, 2 Oct 2026):
                // the route's renderers had only just been created, and Unity does not draw new
                // renderers until it has run a frame with them.
                EditorUtility.DisplayProgressBar("Route map", "Waiting to take the end-zone picture...", 0.4f);
                s_pending = job;
                s_frames = 0;
                EditorApplication.update += Continue;
                waiting = true;
            }
            finally
            {
                if (!waiting) EditorUtility.ClearProgressBar();
            }
        }

        class Job
        {
            public System.Diagnostics.Stopwatch Clock;
            public UnityEngine.SceneManagement.Scene Scene;
            public FullRoutePlan Plan;
            public bool Landmarks;
            public GameObject Route;
            public RouteDefinition Def;
            public Vector3[] RoutePoints;
        }

        static Job s_pending;
        static int s_frames;
        const int FramesBeforePhoto = 8;

        static void Continue()
        {
            s_frames++;
            if (s_frames < FramesBeforePhoto)
            {
                EditorApplication.QueuePlayerLoopUpdate();
                SceneView.RepaintAll();
                return;
            }

            EditorApplication.update -= Continue;
            Job job = s_pending;
            s_pending = null;
            try
            {
                if (job == null || job.Route == null)
                {
                    Debug.LogWarning("[RouteMap] The route was gone before the map could be finished (was the scene changed?). Run the build again.");
                    return;
                }
                Finish(job);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        static void Finish(Job job)
        {
            EditorUtility.DisplayProgressBar("Route map", "Taking the end-zone picture...", 0.45f);
            Sprite photo = CapturePhoto(job.Plan, job.Def);

            EditorUtility.DisplayProgressBar("Route map", "Cutting the model to size...", 0.55f);
            Rect crop = CropRect(job.Plan);
            Strip(job.Route);
            int meshes = ClipAll(job.Route, crop);
            AssetDatabase.DeleteAsset(BuildMeshFolder);

            EditorUtility.DisplayProgressBar("Route map", "Setting the table...", 0.9f);
            float scale = Mathf.Max(crop.width / MaxModelDepth, crop.height / MaxModelWidth);
            Assemble(job.Route, job.Plan, crop, scale, job.RoutePoints, photo, job.Landmarks);

            EditorSceneManager.MarkSceneDirty(job.Scene);
            EditorSceneManager.SaveScene(job.Scene, ScenePath);
            AddToBuildSettings(ScenePath);
            AssetDatabase.SaveAssets();

            Debug.Log(string.Format(
                "[RouteMap] Built {0} in {1:0.0} s: model 1:{2:0} ({3:0} x {4:0} m of route on a {5:0.00} x {6:0.00} m board), " +
                "{7} meshes, route line {8} points, landmarks {9}. Added to Build Settings.",
                ScenePath, job.Clock.Elapsed.TotalSeconds, scale, crop.width, crop.height,
                crop.height / scale + 2f * BoardBorder, crop.width / scale + FrontLip + BoardBorder,
                meshes, job.RoutePoints.Length, job.Landmarks ? "INCLUDED" : "OMITTED"));
        }

        // ================================================================== route data
        /// <summary>The walking line from the bus stop to just inside the end zone (plan, y on the footpath).</summary>
        static Vector3[] RoutePoints(RouteDefinition def)
        {
            float end = def.EndDistance + LineEndPast;
            var pts = new List<Vector3> { def.WalkingPoint(0) };
            float acc = 0f;
            for (int i = 1; i < def.PointCount; i++)
            {
                Vector3 a = def.WalkingPoint(i - 1), b = def.WalkingPoint(i);
                float seg = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
                if (acc + seg >= end) break;
                acc += seg;
                if (Vector3.Distance(pts[pts.Count - 1], b) > 0.05f) pts.Add(b);
            }
            Vector3 last = def.PointAt(end);
            if (Vector3.Distance(pts[pts.Count - 1], last) > 0.05f) pts.Add(last);
            for (int i = 0; i < pts.Count; i++) pts[i] = new Vector3(pts[i].x, FullRouteLayout.FootSlabTop, pts[i].z);
            return pts.ToArray();
        }

        /// <summary>
        /// The part of the suburb on the table: every street around the route plus a margin,
        /// the shopping centre, and the bus road only near the bus stop (it runs off the edge).
        /// </summary>
        static Rect CropRect(FullRoutePlan plan)
        {
            Vector2 mn = new Vector2(float.MaxValue, float.MaxValue), mx = -mn;
            System.Action<Vector2> add = p => { mn = Vector2.Min(mn, p); mx = Vector2.Max(mx, p); };
            foreach (var st in plan.Streets)
            {
                if (st == plan.BusRoad || st.Name == "BusRoad") continue;
                foreach (var p in st.Pts) add(p);
            }
            foreach (var c in plan.ShopBuilding.Corners()) add(c);
            foreach (var c in plan.Forecourt.Corners()) add(c);
            Vector2 n0 = FullRouteLayout.RouteNodes[0];
            add(n0 + new Vector2(StartReach, StartReach));
            add(n0 - new Vector2(StartReach, StartReach));
            add(plan.ShelterPos);
            mn -= Vector2.one * CropMargin;
            mx += Vector2.one * CropMargin;
            return Rect.MinMaxRect(mn.x, mn.y, mx.x, mx.y);
        }

        // ================================================================== end-zone picture
        //
        // The picture on the Finish card: the end zone as a participant sees it arriving - eye
        // height, on the footpath PhotoBackOff metres before the end trigger, looking at the
        // shopping centre's front.
        //
        // HISTORY. The first version came out sky only (2 Oct); waiting a few Editor frames before
        // the shot did not fix it - the 3 Oct build saved the plain default sky and ground, with
        // not even the footpath under the camera drawn. The old empty check missed it (it only
        // looked at the lower half, where the bright horizon band read as detail), so the build
        // reported success.
        //
        // NOW (5 Oct):
        //  - Retake End-Zone Picture takes it in RunSystem, where the route is known to draw, and
        //    overwrites the PNG in place - the Map card uses the new one without rebuilding.
        //  - Each shot is compared with a sky-only shot from the same camera: no difference means
        //    nothing was drawn. A render request is tried first, then Camera.Render.
        //  - An empty shot is never saved: the existing picture is kept.
        //  - The Console reports the camera position and how many objects were in view.

        const string RunSystemScenePath = "Assets/Scenes/RunSystem.unity";
        const string FullRouteRootName = "FullRouteEnvironment";
        const int RetakeFramesBeforePhoto = 8;

        static int s_retakeFrames = -1;                 // -1 = no retake in progress
        static UnityEngine.SceneManagement.Scene s_retakeScene;
        static UnityEngine.SceneManagement.Scene s_retakePreviousActive;
        static bool s_retakeOpened;

        /// <summary>
        /// Tools > VR Full Route > Retake End-Zone Picture (Map button). Takes the Finish card's
        /// picture in RunSystem and overwrites T_Map_EndZonePhoto.png, so the Map shows it with no
        /// rebuild. RunSystem is opened alongside the current scene if it is not open, and closed
        /// again afterwards.
        /// </summary>
        [MenuItem("Tools/VR Full Route/Retake End-Zone Picture (Map button)", false, 5)]
        public static void RetakePhoto()
        {
            if (s_pending != null || s_retakeFrames >= 0)
            {
                Debug.LogWarning("[RouteMap] A picture is already being taken. Wait a moment and try again.");
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Retake End-Zone Picture", "Leave Play mode first.", "OK");
                return;
            }
            if (!File.Exists(RunSystemScenePath))
            {
                EditorUtility.DisplayDialog("Retake End-Zone Picture", RunSystemScenePath + " was not found.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            s_retakePreviousActive = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            s_retakeScene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(RunSystemScenePath);
            s_retakeOpened = !s_retakeScene.isLoaded;
            if (s_retakeOpened)
                s_retakeScene = EditorSceneManager.OpenScene(RunSystemScenePath, OpenSceneMode.Additive);
            // RunSystem's own sky and fog, as in the run.
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(s_retakeScene);

            // A few frames first, so everything just loaded has been drawn once.
            EditorUtility.DisplayProgressBar("End-zone picture", "Opening RunSystem...", 0.3f);
            s_retakeFrames = 0;
            EditorApplication.update += RetakeContinue;
        }

        static void RetakeContinue()
        {
            s_retakeFrames++;
            if (s_retakeFrames < RetakeFramesBeforePhoto)
            {
                EditorApplication.QueuePlayerLoopUpdate();
                SceneView.RepaintAll();
                return;
            }
            EditorApplication.update -= RetakeContinue;

            try
            {
                EditorUtility.DisplayProgressBar("End-zone picture", "Taking the picture...", 0.7f);
                RouteDefinition def = null;
                foreach (var d in Object.FindObjectsByType<RouteDefinition>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (d.gameObject.scene != s_retakeScene) continue;
                    if (def == null || d.gameObject.name == FullRouteRootName) def = d;
                }
                if (def == null || def.PointCount < 2)
                {
                    EditorUtility.DisplayDialog("Retake End-Zone Picture",
                        "RunSystem has no route (no RouteDefinition on " + FullRouteRootName + "). " +
                        "Run Tools > VR Full Route > Install Route in RunSystem first.", "OK");
                    return;
                }

                string report;
                Texture2D tex = TakeEndZonePicture(new FullRoutePlan(), def, s_retakeScene, out report);
                if (tex == null)
                {
                    Debug.LogError("[RouteMap] Retake: the picture still came out empty, so the existing one was kept. " +
                                   report + " Please send this line to Claude.");
                    EditorUtility.DisplayDialog("Retake End-Zone Picture",
                        "The picture came out empty, so the existing one was kept. The Console has the details.", "OK");
                    return;
                }
                SavePhoto(tex);
                Debug.Log("[RouteMap] Retake: new end-zone picture saved to " + PhotoPath + ". " + report +
                          " The Map's Finish card uses it straight away - no need to rebuild the map.");
                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Texture2D>(PhotoPath));
            }
            catch (System.Exception e)
            {
                Debug.LogError("[RouteMap] Retake failed: " + e);
            }
            finally
            {
                if (s_retakePreviousActive.IsValid() && s_retakePreviousActive.isLoaded && s_retakePreviousActive != s_retakeScene)
                    UnityEngine.SceneManagement.SceneManager.SetActiveScene(s_retakePreviousActive);
                if (s_retakeOpened && s_retakeScene.IsValid() && s_retakeScene.isLoaded)
                    EditorSceneManager.CloseScene(s_retakeScene, true);
                s_retakeFrames = -1;
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>
        /// For the map build: takes the picture in the freshly built scene. If it comes out empty
        /// the existing picture is kept (and the Console says to use Retake End-Zone Picture).
        /// </summary>
        static Sprite CapturePhoto(FullRoutePlan plan, RouteDefinition def)
        {
            try
            {
                string report;
                Texture2D tex = TakeEndZonePicture(plan, def, def.gameObject.scene, out report);
                if (tex != null)
                {
                    SavePhoto(tex);
                    Debug.Log("[RouteMap] End-zone picture taken. " + report);
                }
                else
                {
                    Debug.LogWarning("[RouteMap] The end-zone picture came out empty, so the existing " + PhotoPath +
                                     " was kept. Run Tools > VR Full Route > Retake End-Zone Picture (Map button) to take it " +
                                     "in RunSystem instead. " + report);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[RouteMap] Could not take the end-zone picture (" + e.Message + "). The existing " +
                                 PhotoPath + " was kept. Try Tools > VR Full Route > Retake End-Zone Picture (Map button).");
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(PhotoPath);
        }

        static void SavePhoto(Texture2D tex)
        {
            File.WriteAllBytes(Path.GetFullPath(PhotoPath), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(PhotoPath, ImportAssetOptions.ForceUpdate);
            ImportSprite(PhotoPath, 100f, Vector4.zero, 2048);
        }

        /// <summary>
        /// Renders the end zone. Returns null if nothing but sky was drawn. The report gives the
        /// camera position, how many objects were in view, and which render method worked.
        /// </summary>
        static Texture2D TakeEndZonePicture(FullRoutePlan plan, RouteDefinition def,
                                            UnityEngine.SceneManagement.Scene scene, out string report)
        {
            Vector3 eye = def.PointAt(Mathf.Max(0f, def.EndDistance - PhotoBackOff)) + Vector3.up * PhotoEye;
            // The plan is in the route's own space: RunSystem's route sits at z = -500.
            Vector2 front = plan.ShopBuilding.C + plan.ShopFacing * plan.ShopBuilding.HU;
            Vector3 target = def.transform.TransformPoint(new Vector3(front.x, 3.2f, front.y));

            var lightGo = new GameObject("PhotoSun");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightGo, scene);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.05f;
            light.color = new Color(1.00f, 0.96f, 0.89f);
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(46f, -38f, 0f);

            var camGo = new GameObject("PhotoCamera");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, scene);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = PhotoFov;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 600f;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            cam.useOcclusionCulling = false;
            camGo.transform.position = eye;
            camGo.transform.LookAt(target);

            // How many drawable things the camera should see - tells "wrong place" from "not drawn".
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(cam);
            int inView = 0;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (r.enabled && r.gameObject.scene == scene && GeometryUtility.TestPlanesAABB(planes, r.bounds))
                    inView++;

            string where = string.Format("Camera at {0} looking at {1}, {2} objects in view", eye, target, inView);
            var rt = new RenderTexture(PhotoPixelsW, PhotoPixelsH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.Create();
            try
            {
                foreach (bool useRequest in new[] { true, false })
                {
                    cam.cullingMask = 0;
                    Texture2D sky = Render(cam, rt, useRequest);
                    if (sky == null) continue;          // this method is not available
                    cam.cullingMask = ~0;
                    Texture2D shot = Render(cam, rt, useRequest);
                    float drawn = DrawnFraction(sky, shot);
                    Object.DestroyImmediate(sky);
                    string method = useRequest ? "render request" : "Camera.Render";
                    if (drawn > 0.05f)
                    {
                        report = string.Format("{0}; {1} drew {2:0}% of the picture.", where, method, drawn * 100f);
                        return shot;
                    }
                    Object.DestroyImmediate(shot);
                    where += string.Format("; {0} drew {1:0.0}%", method, drawn * 100f);
                }
                report = where + ".";
                return null;
            }
            finally
            {
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(lightGo);
            }
        }

        /// <summary>One shot with the given method, read back to a texture. Null if the method is not available.</summary>
        static Texture2D Render(Camera cam, RenderTexture rt, bool useRequest)
        {
            // Shaders compiled now rather than drawn as placeholders, and one render to warm up
            // before the one that is kept.
            bool async = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    if (useRequest)
                    {
                        var request = new RenderPipeline.StandardRequest { destination = rt };
                        if (!RenderPipeline.SupportsRenderRequest(cam, request)) return null;
                        RenderPipeline.SubmitRenderRequest(cam, request);
                    }
                    else
                    {
                        cam.targetTexture = rt;
                        cam.Render();
                        cam.targetTexture = null;
                    }
                }
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = async;
            }

            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            return tex;
        }

        /// <summary>
        /// Share of the picture that differs from the sky-only shot from the same camera. An empty
        /// scene gives about 0; a street view gives most of the picture.
        /// </summary>
        static float DrawnFraction(Texture2D sky, Texture2D shot)
        {
            Color32[] a = sky.GetPixels32(), b = shot.GetPixels32();
            int differ = 0, n = 0;
            for (int i = 0; i < a.Length && i < b.Length; i += 53)
            {
                int d = Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b);
                if (d > 24) differ++;
                n++;
            }
            return n == 0 ? 0f : (float)differ / n;
        }

        // ================================================================== strip and clip
        /// <summary>Leaves only what is drawn: no colliders, triggers, footstep markers or spawn.</summary>
        static void Strip(GameObject route)
        {
            foreach (string name in new[] { "Checkpoints", "WrongTurnZones", "PlayerSpawn", "RunSystemWiring" })
            {
                Transform t = route.transform.Find(name);
                if (t != null) Object.DestroyImmediate(t.gameObject);
            }

            foreach (var c in route.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var l in route.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(l.gameObject);
            foreach (var b in route.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(b);

            // The model is placed at runtime (the table follows the participant's eye height),
            // so nothing in it can be static-batched.
            foreach (var t in route.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);

            RemoveEmpty(route.transform);
        }

        static bool RemoveEmpty(Transform t)
        {
            bool keep = t.GetComponent<Renderer>() != null;
            for (int i = t.childCount - 1; i >= 0; i--)
                if (RemoveEmpty(t.GetChild(i))) keep = true;
            if (!keep && t.parent != null) Object.DestroyImmediate(t.gameObject);
            return keep;
        }

        /// <summary>
        /// Cuts every mesh to the crop rectangle (in plan, on the ground), so the model has a
        /// clean straight edge like a diorama. Saves the cut meshes to their own folder.
        /// </summary>
        static int ClipAll(GameObject route, Rect crop)
        {
            if (AssetDatabase.IsValidFolder(ModelMeshFolder)) AssetDatabase.DeleteAsset(ModelMeshFolder);
            EnsureFolder(ModelMeshFolder);

            int saved = 0;
            Matrix4x4 rootInv = route.transform.worldToLocalMatrix;
            foreach (var mf in route.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf == null) continue;
                Mesh src = mf.sharedMesh;
                if (src == null) continue;
                if (!src.isReadable)
                {
                    Debug.LogWarning("[RouteMap] " + src.name + " is not readable, left uncut.", mf);
                    continue;
                }

                Mesh cut = ClipMesh(src, rootInv * mf.transform.localToWorldMatrix, crop);
                if (cut == null)
                {
                    // Wholly outside the table. Only the renderer goes; RemoveEmpty tidies up.
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr != null) Object.DestroyImmediate(mr);
                    Object.DestroyImmediate(mf);
                    continue;
                }
                cut.name = "Map_" + saved.ToString("0000") + "_" + Safe(src.name);
                AssetDatabase.CreateAsset(cut, ModelMeshFolder + "/" + cut.name + ".asset");
                mf.sharedMesh = cut;
                saved++;
            }
            RemoveEmpty(route.transform);
            return saved;
        }

        struct V
        {
            public Vector3 P, N; public Vector2 UV; public Color C; public float X, Z;
            public static V Lerp(V a, V b, float t)
            {
                return new V
                {
                    P = Vector3.Lerp(a.P, b.P, t), N = Vector3.Lerp(a.N, b.N, t), UV = Vector2.Lerp(a.UV, b.UV, t),
                    C = Color.Lerp(a.C, b.C, t), X = Mathf.Lerp(a.X, b.X, t), Z = Mathf.Lerp(a.Z, b.Z, t),
                };
            }
        }

        static float Side(V v, int plane, Rect r)
        {
            switch (plane)
            {
                case 0: return v.X - r.xMin;
                case 1: return r.xMax - v.X;
                case 2: return v.Z - r.yMin;
                default: return r.yMax - v.Z;
            }
        }

        /// <summary>A copy of the mesh with every triangle cut to the rectangle, or null if none is left.</summary>
        static Mesh ClipMesh(Mesh src, Matrix4x4 toPlan, Rect r)
        {
            Vector3[] pos = src.vertices;
            Vector3[] nrm = src.normals;
            Vector2[] uv = src.uv;
            Color[] col = src.colors;
            bool hasN = nrm != null && nrm.Length == pos.Length;
            bool hasUV = uv != null && uv.Length == pos.Length;
            bool hasC = col != null && col.Length == pos.Length;

            var verts = new V[pos.Length];
            var inside = new bool[pos.Length];
            for (int i = 0; i < pos.Length; i++)
            {
                Vector3 p = toPlan.MultiplyPoint3x4(pos[i]);
                verts[i] = new V
                {
                    P = pos[i], N = hasN ? nrm[i] : Vector3.up, UV = hasUV ? uv[i] : Vector2.zero,
                    C = hasC ? col[i] : Color.white, X = p.x, Z = p.z,
                };
                inside[i] = p.x >= r.xMin && p.x <= r.xMax && p.z >= r.yMin && p.z <= r.yMax;
            }

            var outV = new List<V>();
            var remap = new int[pos.Length];
            for (int i = 0; i < remap.Length; i++) remap[i] = -1;
            var subs = new List<int>[src.subMeshCount];
            var poly = new List<V>(8);
            var next = new List<V>(8);
            int triCount = 0;

            for (int s = 0; s < src.subMeshCount; s++)
            {
                var outT = new List<int>();
                subs[s] = outT;
                if (src.GetTopology(s) != MeshTopology.Triangles) continue;
                int[] tris = src.GetTriangles(s);
                for (int k = 0; k + 2 < tris.Length; k += 3)
                {
                    int a = tris[k], b = tris[k + 1], c = tris[k + 2];
                    if (inside[a] && inside[b] && inside[c])
                    {
                        outT.Add(Keep(a, verts, remap, outV));
                        outT.Add(Keep(b, verts, remap, outV));
                        outT.Add(Keep(c, verts, remap, outV));
                        triCount++;
                        continue;
                    }

                    poly.Clear();
                    poly.Add(verts[a]); poly.Add(verts[b]); poly.Add(verts[c]);
                    for (int plane = 0; plane < 4 && poly.Count >= 3; plane++)
                    {
                        next.Clear();
                        for (int i = 0; i < poly.Count; i++)
                        {
                            V cur = poly[i], nxt = poly[(i + 1) % poly.Count];
                            float fc = Side(cur, plane, r), fn = Side(nxt, plane, r);
                            if (fc >= 0f) next.Add(cur);
                            if ((fc >= 0f) != (fn >= 0f))
                                next.Add(V.Lerp(cur, nxt, fc / (fc - fn)));
                        }
                        var swap = poly; poly = next; next = swap;
                    }
                    if (poly.Count < 3) continue;

                    int first = outV.Count;
                    outV.AddRange(poly);
                    for (int i = 1; i + 1 < poly.Count; i++)
                    {
                        outT.Add(first); outT.Add(first + i); outT.Add(first + i + 1);
                        triCount++;
                    }
                }
            }

            if (triCount == 0) return null;

            var mesh = new Mesh();
            if (outV.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            var vp = new Vector3[outV.Count];
            var vn = new Vector3[outV.Count];
            var vu = new Vector2[outV.Count];
            var vc = new Color[outV.Count];
            for (int i = 0; i < outV.Count; i++)
            {
                vp[i] = outV[i].P; vn[i] = outV[i].N.normalized; vu[i] = outV[i].UV; vc[i] = outV[i].C;
            }
            mesh.vertices = vp;
            if (hasN) mesh.normals = vn;
            if (hasUV) mesh.uv = vu;
            if (hasC) mesh.colors = vc;
            mesh.subMeshCount = src.subMeshCount;
            for (int s = 0; s < subs.Length; s++) mesh.SetTriangles(subs[s], s);
            if (!hasN) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            if (hasUV) mesh.RecalculateTangents();
            return mesh;
        }

        static int Keep(int i, V[] verts, int[] remap, List<V> outV)
        {
            if (remap[i] < 0) { remap[i] = outV.Count; outV.Add(verts[i]); }
            return remap[i];
        }

        // ================================================================== assembly
        static void Assemble(GameObject route, FullRoutePlan plan, Rect crop, float scale,
                             Vector3[] routePoints, Sprite photo, bool landmarks)
        {
            var mats = new MapMats();

            var rootGo = new GameObject("RouteMapView");
            rootGo.transform.position = MapOrigin;
            var view = rootGo.AddComponent<RouteMapView>();

            var standing = new GameObject("MapStandingPoint").transform;
            standing.SetParent(rootGo.transform, false);

            // Floor: a disc under the participant, solid so the table legs can sink into it.
            var floor = Prim(PrimitiveType.Cylinder, "Floor", rootGo.transform, mats.Floor, false);
            floor.localScale = new Vector3(14f, 0.05f, 14f);
            floor.localPosition = new Vector3(0f, -0.05f, 0f);
            floor.gameObject.AddComponent<BoxCollider>().size = new Vector3(1f, 2f, 1f);

            // Table. TableRig's origin is the near edge of the board's top, placed at runtime.
            float depth = crop.width / scale;            // plan x runs away from the participant
            float width = crop.height / scale;
            float boardW = width + 2f * BoardBorder;
            float boardD = FrontLip + depth + BoardBorder;

            var rig = new GameObject("TableRig").transform;
            rig.SetParent(rootGo.transform, false);
            rig.localPosition = new Vector3(0f, 0.9f, 0.25f);

            var board = new GameObject("Board").transform;
            board.SetParent(rig, false);
            board.localRotation = Quaternion.Euler(-BoardTilt, 0f, 0f);

            var top = Prim(PrimitiveType.Cube, "BoardTop", board, mats.Wood, true);
            top.localScale = new Vector3(boardW, BoardThickness, boardD);
            top.localPosition = new Vector3(0f, -BoardThickness * 0.5f, boardD * 0.5f);

            float tan = Mathf.Tan(BoardTilt * Mathf.Deg2Rad), cos = Mathf.Cos(BoardTilt * Mathf.Deg2Rad);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = 0; sz <= 1; sz++)
                {
                    float z = sz == 0 ? 0.06f : boardD * cos - 0.06f;
                    float topY = z * tan - BoardThickness / cos;
                    const float below = 1.6f;
                    var leg = Prim(PrimitiveType.Cube, "Leg", rig, mats.Wood, true);
                    leg.localScale = new Vector3(0.04f, topY + below, 0.04f);
                    leg.localPosition = new Vector3(sx * (boardW * 0.5f - 0.04f), (topY - below) * 0.5f, z);
                }

            // The model: plan -x away from the participant, plan +z to their right.
            var model = new GameObject("Model").transform;
            model.SetParent(board, false);
            Quaternion rot = Quaternion.Euler(0f, 90f, 0f);
            model.localRotation = rot;
            model.localScale = Vector3.one / scale;
            Vector3 centre = new Vector3(crop.center.x, 0f, crop.center.y);
            model.localPosition = new Vector3(0f, PlinthDepth / scale, FrontLip + depth * 0.5f) - rot * centre / scale;

            route.transform.SetParent(model, false);
            route.transform.localPosition = Vector3.zero;
            route.transform.localRotation = Quaternion.identity;
            route.transform.localScale = Vector3.one;

            var plinth = Prim(PrimitiveType.Cube, "Plinth", model, mats.Plinth, true);
            plinth.localScale = new Vector3(crop.width, PlinthDepth - 0.03f, crop.height);
            plinth.localPosition = new Vector3(crop.center.x, -(PlinthDepth + 0.03f) * 0.5f, crop.center.y);

            // Route line: a navy edge under a blue centre, like the Guided runs' line.
            AddRibbon(model, "RouteLineEdge", routePoints, EdgeWidth, EdgeLift, mats.LineEdge);
            AddRibbon(model, "RouteLine", routePoints, LineWidth, LineLift, mats.Line);

            Transform walker = BuildWalker(model, mats.Walker);

            // Time ring on the front lip, right-hand side.
            var ring = new GameObject("TimeRing").transform;
            ring.SetParent(board, false);
            // Flat on the board, a few millimetres proud of it; the fill sits just above the track.
            ring.localPosition = new Vector3(boardW * 0.5f - 0.055f, 0.003f, FrontLip * 0.5f);
            ring.localRotation = Quaternion.identity;
            MeshFilter track = RingMesh(ring, "Track", mats.RingTrack, 0f);
            MeshFilter fill = RingMesh(ring, "Fill", mats.Line, 0.0015f);

            view.SetUp(standing, rig, floor, model, routePoints, walker, track, fill, RingRadius);

            // Labels.
            var labels = new GameObject("Labels").transform;
            labels.SetParent(board, false);
            Sprite card = ImportSprite(IconFolder + "/MapCard.png", 4000f, new Vector4(40f, 40f, 40f, 40f), 256);
            TMP_FontAsset font = TMP_Settings.defaultFontAsset;
            if (font == null)
                Debug.LogWarning("[RouteMap] No TextMeshPro default font asset, so the labels have icons only.");

            System.Func<Vector2, Vector3> onBoard = p =>
                board.InverseTransformPoint(model.TransformPoint(new Vector3(p.x, 0f, p.y)));

            // Each card is moved off the route so it does not hide the blue line from where the
            // participant stands (Kade, 2 Oct 2026): a short search over sideways, forward/back and
            // height offsets, scored on how much of the line it would cover as seen from the
            // nominal eye position, how much it would cover the cards already placed, and how far
            // it had to move. Its pole then leans from the landmark to the card. The big ones go
            // first so the small ones fit round them.
            var placer = new LabelPlacer(rig, board, model, routePoints, boardW);

            // The finish: shopping centre card with the picture, on a pole down to the end zone.
            Label(labels, placer, "Finish", onBoard(plan.Forecourt.C), PhotoRaise, 0.20f, Icon("MapIcon_Shop"),
                  "Finish: shopping centre", card, font, photo, mats.Pole);
            // The bus stop and blue house cards go to the right of their landmarks, toward the
            // middle of the table, rather than out over the left edge (Kade, 2 Oct 2026).
            Label(labels, placer, "BusStop", onBoard(plan.ShelterPos), 0.06f, 0.24f, Icon("MapIcon_Bus"), "Start: bus stop", card, font, null, mats.Pole, side: 1);
            if (landmarks)
                Label(labels, placer, "BlueHouse", onBoard(plan.SpecialLot.C), 0.08f, 0.24f, Icon("MapIcon_House"), "Blue house", card, font, null, mats.Pole, side: 1);
            Label(labels, placer, "Park", onBoard(plan.Park.C), 0.06f, 0.20f, Icon("MapIcon_Park"), "Park", card, font, null, mats.Pole);
            Label(labels, placer, "School", onBoard(plan.School.C), 0.08f, 0.20f, Icon("MapIcon_School"), "School", card, font, null, mats.Pole);
            if (landmarks)
            {
                int i = 0;
                foreach (var lamp in plan.LandmarkLamps)
                    Label(labels, placer, "Lamp_" + (i++), onBoard(lamp), 0.025f, 0.04f, Icon("MapIcon_Lamp"), null, null, null, null, mats.Pole, LampIconSize);
            }

            Selection.activeGameObject = rootGo;
        }

        static void AddRibbon(Transform parent, string name, Vector3[] pts, float width, float lift, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Mesh mesh = Ribbon(pts, width, lift);
            mesh.name = "Map_" + name;
            AssetDatabase.CreateAsset(mesh, ModelMeshFolder + "/" + mesh.name + ".asset");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>A flat strip along the polyline, mitred at the corners, facing up.</summary>
        static Mesh Ribbon(Vector3[] pts, float width, float lift)
        {
            int n = pts.Length;
            var v = new Vector3[n * 2];
            float half = width * 0.5f;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = new Vector2(pts[i].x, pts[i].z);
                Vector2 dIn = i > 0 ? (p - new Vector2(pts[i - 1].x, pts[i - 1].z)).normalized : Vector2.zero;
                Vector2 dOut = i < n - 1 ? (new Vector2(pts[i + 1].x, pts[i + 1].z) - p).normalized : Vector2.zero;
                if (i == 0) dIn = dOut;
                if (i == n - 1) dOut = dIn;
                Vector2 nIn = new Vector2(-dIn.y, dIn.x), nOut = new Vector2(-dOut.y, dOut.x);
                Vector2 m = (nIn + nOut).sqrMagnitude > 1e-6f ? (nIn + nOut).normalized : nOut;
                float k = half / Mathf.Max(0.35f, Vector2.Dot(m, nOut));
                float y = pts[i].y + lift;
                v[i * 2] = new Vector3(p.x + m.x * k, y, p.y + m.y * k);
                v[i * 2 + 1] = new Vector3(p.x - m.x * k, y, p.y - m.y * k);
            }

            var t = new List<int>();
            for (int i = 0; i < n - 1; i++)
            {
                int l0 = i * 2, r0 = l0 + 1, l1 = l0 + 2, r1 = l0 + 3;
                Tri(t, v, l0, l1, r0);
                Tri(t, v, r0, l1, r1);
            }

            var mesh = new Mesh { vertices = v, triangles = t.ToArray() };
            var normals = new Vector3[v.Length];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            mesh.normals = normals;
            mesh.RecalculateBounds();
            return mesh;
        }

        static void Tri(List<int> t, Vector3[] v, int a, int b, int c)
        {
            // Front face up.
            if (Vector3.Cross(v[b] - v[a], v[c] - v[a]).y < 0f) { int s = b; b = c; c = s; }
            t.Add(a); t.Add(b); t.Add(c);
        }

        /// <summary>A small person: orange body and head on a disc, about 11 route metres tall.</summary>
        static Transform BuildWalker(Transform model, Material mat)
        {
            var w = new GameObject("Walker").transform;
            w.SetParent(model, false);
            float baseY = LineLift + 0.05f;
            var disc = Prim(PrimitiveType.Cylinder, "Disc", w, mat, true);
            disc.localScale = new Vector3(7f, 0.25f, 7f);
            disc.localPosition = new Vector3(0f, baseY + 0.25f, 0f);
            var body = Prim(PrimitiveType.Capsule, "Body", w, mat, true);
            body.localScale = new Vector3(4.4f, 3.4f, 4.4f);
            body.localPosition = new Vector3(0f, baseY + 0.5f + 3.4f, 0f);
            var head = Prim(PrimitiveType.Sphere, "Head", w, mat, true);
            head.localScale = Vector3.one * 3.4f;
            head.localPosition = new Vector3(0f, baseY + 0.5f + 6.8f + 1.9f, 0f);
            // A nose, so the way it faces reads at a glance.
            var nose = Prim(PrimitiveType.Cube, "Nose", w, mat, true);
            nose.localScale = new Vector3(1.4f, 1.0f, 1.6f);
            nose.localPosition = new Vector3(0f, baseY + 0.5f + 6.8f + 1.8f, 1.9f);
            return w;
        }

        static MeshFilter RingMesh(Transform ring, string name, Material mat, float raise)
        {
            var go = new GameObject(name);
            go.transform.SetParent(ring, false);
            go.transform.localPosition = new Vector3(0f, raise, 0f);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return mf;
        }

        /// <summary>
        /// A card on a pole: icon, English text, and optionally the end-zone picture. Placed by
        /// the LabelPlacer within maxShift of its landmark, with the pole leaning from the
        /// landmark up to the card.
        /// </summary>
        static void Label(Transform parent, LabelPlacer placer, string name, Vector3 anchor, float height, float maxShift,
                          Sprite icon, string text, Sprite card, TMP_FontAsset font, Sprite photo, Material poleMat,
                          float iconSize = IconSize, int side = 0)
        {
            var root = new GameObject("Label_" + name).transform;
            root.SetParent(parent, false);
            root.localPosition = anchor + Vector3.up * height;

            SpriteRenderer cardR = null, iconR = null, photoR = null;
            if (card != null)
            {
                cardR = new GameObject("Card").AddComponent<SpriteRenderer>();
                cardR.transform.SetParent(root, false);
                cardR.sprite = card;
                cardR.drawMode = SpriteDrawMode.Sliced;
                cardR.color = Color.white;
                cardR.sortingOrder = 0;
            }
            if (icon != null)
            {
                iconR = new GameObject("Icon").AddComponent<SpriteRenderer>();
                iconR.transform.SetParent(root, false);
                iconR.sprite = icon;
                iconR.sortingOrder = 1;
            }
            TextMeshPro tmp = null;
            if (!string.IsNullOrEmpty(text) && font != null)
            {
                var tgo = new GameObject("Text", typeof(RectTransform));
                tgo.transform.SetParent(root, false);
                tgo.transform.localScale = Vector3.one * 0.01f;
                tmp = tgo.AddComponent<TextMeshPro>();
                tmp.font = font;
                tmp.text = text;
                tmp.fontSize = LabelFontSize;
                tmp.fontStyle = FontStyles.Bold;
                tmp.color = Navy;
                tmp.alignment = TextAlignmentOptions.MidlineLeft;
                tmp.textWrappingMode = TextWrappingModes.NoWrap;
                tmp.GetComponent<MeshRenderer>().sortingOrder = 2;
                tgo.AddComponent<ScalableText>();
            }
            if (photo != null)
            {
                photoR = new GameObject("Photo").AddComponent<SpriteRenderer>();
                photoR.transform.SetParent(root, false);
                photoR.sprite = photo;
                photoR.sortingOrder = 1;
            }

            var label = root.gameObject.AddComponent<RouteMapLabel>();
            label.SetUp(cardR, iconR, tmp, photoR, iconSize, photoR != null ? PhotoWidth : 0f);

            // Off the blue line, then the pole from the landmark to the card's bottom edge.
            root.localPosition = placer.Place(name, anchor, height, maxShift, label.Size, side);

            var pole = Prim(PrimitiveType.Cylinder, "Pole_" + name, parent, poleMat, false);
            Vector3 top = root.localPosition, along = top - anchor;
            pole.localPosition = (anchor + top) * 0.5f;
            pole.localRotation = Quaternion.FromToRotation(Vector3.up, along.normalized);
            pole.localScale = new Vector3(PoleDiameter, along.magnitude * 0.5f, PoleDiameter);
        }

        /// <summary>
        /// Chooses where each label card goes so it hides as little of the blue route line as
        /// possible from where the participant stands.
        ///
        /// Works in the table rig's space (level, before the board's tilt), from a nominal eye at
        /// RouteMapView's defaults: 0.6 m above and 0.25 m behind the near edge. Cards turn to
        /// face the viewer, so each is tested as an upright rectangle facing that eye. A
        /// candidate's cost is the length of route line behind it (in route metres), plus how
        /// much it overlaps cards already placed (as seen from the eye), plus a small charge for
        /// moving it away from its landmark, and a large one for leaving the board.
        /// </summary>
        class LabelPlacer
        {
            const float EyeAbove = 0.6f, EyeBack = 0.25f;
            const float MoveCost = 25f;      // per metre moved (route metres covered are 1 each)
            const float RaiseCost = 15f;     // per metre raised above the given height
            const float OverlapCost = 4f;    // per square degree overlapping another card
            const float Margin = 0.006f;     // around each card, metres

            readonly Transform _board;
            readonly Vector3 _eye;
            readonly List<Vector3> _line = new List<Vector3>();
            readonly List<Rect> _taken = new List<Rect>();
            readonly float _halfBoard;

            public LabelPlacer(Transform rig, Transform board, Transform model, Vector3[] routePoints, float boardWidth)
            {
                _board = board;
                _eye = new Vector3(0f, EyeAbove, -EyeBack);
                _halfBoard = boardWidth * 0.5f;
                // The line sampled every route metre, in rig space.
                for (int i = 1; i < routePoints.Length; i++)
                {
                    Vector3 a = routePoints[i - 1], b = routePoints[i];
                    int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b)));
                    for (int k = 0; k < n; k++)
                        _line.Add(rig.InverseTransformPoint(model.TransformPoint(Vector3.Lerp(a, b, (float)k / n))));
                }
                if (routePoints.Length > 0)
                    _line.Add(rig.InverseTransformPoint(model.TransformPoint(routePoints[routePoints.Length - 1])));
            }

            /// <param name="side">+1 to keep the card to the participant's right of its landmark,
            /// -1 to the left, 0 for either.</param>
            public Vector3 Place(string name, Vector3 anchor, float height, float maxShift, Vector2 size, int side = 0)
            {
                float[] fractions = { 0f, 0.25f, 0.5f, 0.75f, 1f };
                float[] ups = { 0f, 0.03f, 0.06f };
                Vector3 best = anchor + Vector3.up * height;
                float bestCost = float.MaxValue, bestCovered = 0f;
                Rect bestRect = default(Rect);
                foreach (float fx in fractions)
                    for (int sx = -1; sx <= 1; sx += 2)
                    {
                        if (fx == 0f && sx > 0) continue;
                        if (side != 0 && (fx == 0f || sx != side)) continue;
                        foreach (float fz in new[] { 0f, -0.5f, 0.5f })
                            foreach (float up in ups)
                            {
                                Vector3 offset = new Vector3(sx * fx * maxShift, height + up, fz * maxShift * 0.5f);
                                Vector3 root = anchor + offset;
                                float covered = Covered(root, size, out Rect rect);
                                float cost = covered
                                           + MoveCost * new Vector2(offset.x, offset.z).magnitude
                                           + RaiseCost * up
                                           + OverlapCost * Overlap(rect);
                                if (Mathf.Abs(root.x) + size.x * 0.25f > _halfBoard) cost += 1000f;
                                if (cost < bestCost)
                                {
                                    bestCost = cost; best = root; bestRect = rect; bestCovered = covered;
                                }
                            }
                    }
                _taken.Add(bestRect);
                Debug.Log(string.Format("[RouteMap] Label {0}: moved {1:0.00} m sideways, {2:0.00} m back, {3:0.00} m up; " +
                                        "covers {4:0} m of the route line from the nominal eye.",
                                        name, best.x - anchor.x, best.z - anchor.z, best.y - anchor.y - height, bestCovered));
                return best;
            }

            /// <summary>
            /// Route metres hidden behind a card whose bottom centre is at 'root' (board space),
            /// and the card's extent as seen from the eye, in degrees.
            /// </summary>
            float Covered(Vector3 rootBoard, Vector2 size, out Rect angular)
            {
                Vector3 bottom = _board.localRotation * rootBoard + _board.localPosition;
                float w = size.x * 0.5f + Margin, h = size.y + 2f * Margin;
                Vector3 centre = bottom + Vector3.up * (size.y * 0.5f);
                Vector3 n = _eye - centre; n.y = 0f;
                if (n.sqrMagnitude < 1e-8f) n = Vector3.back;
                n.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, n);

                float covered = 0f;
                foreach (Vector3 p in _line)
                {
                    Vector3 d = p - _eye;
                    float denom = Vector3.Dot(n, d);
                    if (Mathf.Abs(denom) < 1e-6f) continue;
                    float t = Vector3.Dot(n, centre - _eye) / denom;
                    if (t <= 0f || t >= 1f) continue;          // card is not between the eye and this point
                    Vector3 local = _eye + d * t - centre;
                    if (Mathf.Abs(Vector3.Dot(local, right)) <= w && Mathf.Abs(local.y) <= h * 0.5f) covered += 1f;
                }

                float minYaw = float.MaxValue, maxYaw = float.MinValue, minPitch = float.MaxValue, maxPitch = float.MinValue;
                for (int i = 0; i < 4; i++)
                {
                    Vector3 c = centre + right * (i % 2 == 0 ? -w : w) + Vector3.up * (i < 2 ? -h * 0.5f : h * 0.5f);
                    Vector3 v = c - _eye;
                    float yaw = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
                    float pitch = Mathf.Atan2(v.y, new Vector2(v.x, v.z).magnitude) * Mathf.Rad2Deg;
                    minYaw = Mathf.Min(minYaw, yaw); maxYaw = Mathf.Max(maxYaw, yaw);
                    minPitch = Mathf.Min(minPitch, pitch); maxPitch = Mathf.Max(maxPitch, pitch);
                }
                angular = Rect.MinMaxRect(minYaw, minPitch, maxYaw, maxPitch);
                return covered;
            }

            float Overlap(Rect r)
            {
                float total = 0f;
                foreach (var t in _taken)
                {
                    float ox = Mathf.Min(r.xMax, t.xMax) - Mathf.Max(r.xMin, t.xMin);
                    float oy = Mathf.Min(r.yMax, t.yMax) - Mathf.Max(r.yMin, t.yMin);
                    if (ox > 0f && oy > 0f) total += ox * oy;
                }
                return total;
            }
        }

        // ================================================================== helpers
        class MapMats
        {
            public Material Floor, Wood, Plinth, Line, LineEdge, Walker, RingTrack, Pole;
            public MapMats()
            {
                Floor = Mat("M_Map_Floor", new Color(0.74f, 0.73f, 0.70f), false, 0.15f);
                Wood = Mat("M_Map_Table", new Color(0.55f, 0.40f, 0.27f), false, 0.35f);
                Plinth = Mat("M_Map_Plinth", new Color(0.20f, 0.22f, 0.25f), false, 0.2f);
                Line = Mat("M_Map_RouteLine", new Color(0.10f, 0.45f, 0.95f), true);
                LineEdge = Mat("M_Map_RouteLineEdge", new Color(0.05f, 0.10f, 0.25f), true);
                Walker = Mat("M_Map_Walker", new Color(1.00f, 0.55f, 0.10f), false, 0.3f, new Color(0.45f, 0.22f, 0.02f));
                RingTrack = Mat("M_Map_RingTrack", new Color(0.62f, 0.65f, 0.70f), true);
                Pole = Mat("M_Map_Pole", new Color(0.25f, 0.27f, 0.30f), false, 0.3f);
            }
        }

        static Material Mat(string name, Color colour, bool unlit, float smoothness = 0.2f, Color? emission = null)
        {
            string path = MatFolder + "/" + name + ".mat";
            Shader shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader) m.shader = shader;

            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", colour);
            if (m.HasProperty("_Color")) m.SetColor("_Color", colour);
            if (!unlit && m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (emission.HasValue && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        static Transform Prim(PrimitiveType type, string name, Transform parent, Material mat, bool shadows)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(parent, false);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go.transform;
        }

        static Sprite Icon(string name)
        {
            string path = IconFolder + "/" + name + ".png";
            if (!File.Exists(Path.GetFullPath(path)))
            {
                Debug.LogWarning("[RouteMap] Missing icon " + path + "; that label has text only.");
                return null;
            }
            return ImportSprite(path, 100f, Vector4.zero, 512);
        }

        static Sprite ImportSprite(string path, float pixelsPerUnit, Vector4 border, int maxSize)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                importer = AssetImporter.GetAtPath(path) as TextureImporter;
            }
            if (importer == null) return null;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spritePixelsPerUnit = pixelsPerUnit;
            settings.spriteBorder = border;
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            importer.SetTextureSettings(settings);
            importer.mipmapEnabled = true;
            importer.alphaIsTransparency = true;
            importer.maxTextureSize = maxSize;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static void AddToBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (var s in scenes)
                if (s.path == path) { s.enabled = true; EditorBuildSettings.scenes = scenes.ToArray(); return; }
            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        static string Safe(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace(' ', '_');
        }
    }
}
