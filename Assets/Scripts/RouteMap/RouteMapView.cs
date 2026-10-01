using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The main menu's Map button: a tabletop model of the full route, seen before the first
/// unguided run.
///
/// Lives on the root of RouteMap.unity, which Tools > VR Full Route > Build Route Map Scene
/// generates (never edited by hand - re-run the tool after any route change). MenuController
/// loads that scene additively, the same way it loads RunSystem, and calls Begin. From there
/// this component owns the visit:
///
///   - moves the participant to the map's standing point and puts the table in front of where
///     they are actually looking, at a height taken from their eye height (so it suits standing
///     and seated use alike);
///   - holds locomotion off for the whole visit, as the pause menu does. Look only: the table
///     faces them with the bus stop nearest and the shopping centre at the far end;
///   - walks a small figure along the ideal route, start to finish, on a loop;
///   - empties a small ring on the table's front edge over the visit (no numbers, no
///     countdown - decided with Kade, to avoid time pressure);
///   - after ViewSeconds fades out, hands locomotion back, returns to the main menu and
///     unloads its own scene.
///
/// Logged to the session log as map_view_opened / map_view_closed, in a session file of its own
/// (each run opens its own file too).
///
/// The model itself (the clipped route, the blue line, the figure, the labels, the end-zone
/// picture) is built by the editor tool; this script only places it and moves the figure.
/// </summary>
[DisallowMultipleComponent]
public class RouteMapView : MonoBehaviour
{
    /// <summary>The camera the labels turn to face, once a visit has begun.</summary>
    public static Transform ViewCamera { get; private set; }

    [Header("Placement")]
    [Tooltip("Where the participant is moved to. The table is then placed in front of their " +
             "actual view direction, so a turned head does not leave the table off to one side.")]
    [SerializeField] private Transform standingPoint;

    [Tooltip("Table, board and everything on it. Moved and turned at Begin.")]
    [SerializeField] private Transform tableRig;

    [Tooltip("Floor disc, centred under the participant at Begin.")]
    [SerializeField] private Transform floor;

    [Tooltip("Metres from the eyes, horizontally, to the near edge of the table.")]
    [SerializeField] private float nearEdgeAhead = 0.25f;

    [Tooltip("How far below eye height the near edge of the table sits, in metres.")]
    [SerializeField] private float tableBelowEye = 0.6f;

    [Tooltip("Lowest and highest table height (near edge, above the floor), in metres.")]
    [SerializeField] private Vector2 tableHeightRange = new Vector2(0.5f, 1.05f);

    [Header("Timing")]
    [Tooltip("How long the map stays up before returning to the menu (decided: 60 s).")]
    [SerializeField] private float viewSeconds = 60f;

    [Tooltip("Fade to and from the map, in seconds.")]
    [SerializeField] private float fadeSeconds = 0.5f;

    [Header("Route (model space)")]
    [Tooltip("The scaled model. Route points and the figure are in its local space (route metres).")]
    [SerializeField] private Transform model;

    [Tooltip("The ideal route, bus stop to end zone, in model space. Baked by the editor tool.")]
    [SerializeField] private Vector3[] routePoints = new Vector3[0];

    [Header("Walking figure")]
    [SerializeField] private Transform walker;

    [Tooltip("Seconds for the figure to walk the whole route.")]
    [SerializeField] private float walkSeconds = 14f;

    [Tooltip("Seconds it waits at the end zone before starting again.")]
    [SerializeField] private float holdAtEndSeconds = 1.5f;

    [Tooltip("Seconds it is hidden between the end and the next start.")]
    [SerializeField] private float gapSeconds = 0.6f;

    [Tooltip("Route metres ahead used to turn the figure, so it rounds corners smoothly.")]
    [SerializeField] private float lookAhead = 6f;

    [Header("Time ring")]
    [Tooltip("Full grey circle under the ring.")]
    [SerializeField] private MeshFilter ringTrack;

    [Tooltip("Blue arc that empties over the visit.")]
    [SerializeField] private MeshFilter ringFill;

    [SerializeField] private float ringRadius = 0.03f;
    [Tooltip("Width of the ring band, metres.")]
    [SerializeField] private float ringWidth = 0.008f;
    [SerializeField] private int ringSegments = 64;

    public bool IsRunning { get; private set; }

    private float[] _cumulative;
    private float _length;
    private float _startTime;
    private bool _suspended;
    private bool _finishing;
    private Vector3 _lastHeading = Vector3.forward;

    // ------------------------------------------------------------------ editor setup
    /// <summary>Called by the editor tool that generates RouteMap.unity.</summary>
    public void SetUp(Transform standing, Transform rig, Transform floorDisc, Transform modelRoot,
                      Vector3[] points, Transform figure, MeshFilter track, MeshFilter fill, float radius)
    {
        standingPoint = standing;
        tableRig = rig;
        floor = floorDisc;
        model = modelRoot;
        routePoints = points ?? new Vector3[0];
        walker = figure;
        ringTrack = track;
        ringFill = fill;
        ringRadius = radius;
        _cumulative = null;
        DrawRing(ringTrack, 1f);
        DrawRing(ringFill, 1f);
    }

    // ------------------------------------------------------------------ lifecycle
    private void Awake()
    {
        BuildCumulative();
        if (walker != null) walker.gameObject.SetActive(false);
        DrawRing(ringTrack, 1f);
        DrawRing(ringFill, 1f);
    }

    private void OnDisable()
    {
        // Never leave the participant stuck: the scene can be unloaded from elsewhere.
        ReleaseLocomotion();
        if (IsRunning) SessionLog.Record("map_view_closed", "interrupted");
        IsRunning = false;
        ViewCamera = null;
    }

    /// <summary>
    /// Starts the visit. Called by MenuController once RouteMap is loaded, while the view is
    /// faded out.
    /// </summary>
    public void Begin()
    {
        if (IsRunning || _finishing) return;

        XROrigin xrOrigin = FindFirstObjectByType<XROrigin>();
        if (xrOrigin != null && standingPoint != null)
            XRPlayerTeleport.MoveToStandingPoint(xrOrigin, standingPoint, this);
        else
            Debug.LogWarning("[RouteMapView] No XR Origin or standing point - the participant was not moved.", this);

        Transform cam = xrOrigin != null && xrOrigin.Camera != null
            ? xrOrigin.Camera.transform
            : (Camera.main != null ? Camera.main.transform : null);
        ViewCamera = cam;
        PlaceTable(cam);

        if (ControllerHandednessManager.Instance != null)
        {
            ControllerHandednessManager.Instance.SuspendLocomotion(this);
            _suspended = true;
        }

        SessionLog.BeginSession();
        if (cam != null) SessionLog.SetPositionSource(cam);
        SessionLog.Record("map_view_opened",
            $"participant {StudySession.ParticipantId}, {viewSeconds.ToString("0", CultureInfo.InvariantCulture)}s");

        _startTime = Time.unscaledTime;
        IsRunning = true;
        if (walker != null) walker.gameObject.SetActive(true);
        StartCoroutine(RunTimer());
    }

    /// <summary>Ends the visit now (same as the timer running out). For a researcher control.</summary>
    public void EndNow()
    {
        if (IsRunning && !_finishing) StartCoroutine(Finish());
    }

    // ------------------------------------------------------------------ placement
    private void PlaceTable(Transform cam)
    {
        if (tableRig == null) return;

        Vector3 floorPoint = standingPoint != null ? standingPoint.position : transform.position;
        if (cam == null)
        {
            tableRig.position = floorPoint + Vector3.forward * nearEdgeAhead + Vector3.up * 0.9f;
            return;
        }

        Vector3 forward = cam.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-4f)
            forward = standingPoint != null ? standingPoint.forward : Vector3.forward;
        forward.y = 0f;
        forward.Normalize();

        float eye = cam.position.y - floorPoint.y;
        float height = Mathf.Clamp(eye - tableBelowEye, tableHeightRange.x, tableHeightRange.y);

        Vector3 near = cam.position + forward * nearEdgeAhead;
        near.y = floorPoint.y + height;
        tableRig.SetPositionAndRotation(near, Quaternion.LookRotation(forward, Vector3.up));

        if (floor != null)
            floor.position = new Vector3(cam.position.x, floor.position.y, cam.position.z);
    }

    // ------------------------------------------------------------------ timer and ring
    private IEnumerator RunTimer()
    {
        while (IsRunning)
        {
            float elapsed = Time.unscaledTime - _startTime;
            if (elapsed >= viewSeconds) break;
            DrawRing(ringFill, 1f - elapsed / Mathf.Max(0.01f, viewSeconds));
            yield return null;
        }
        DrawRing(ringFill, 0f);
        if (IsRunning && !_finishing) yield return Finish();
    }

    private IEnumerator Finish()
    {
        _finishing = true;
        float elapsed = Time.unscaledTime - _startTime;
        SessionLog.Record("map_view_closed", elapsed.ToString("F1", CultureInfo.InvariantCulture) + "s");
        IsRunning = false;

        ScreenFader fader = ScreenFader.Instance;
        if (fader != null) yield return fader.FadeTo(1f, fadeSeconds);

        if (walker != null) walker.gameObject.SetActive(false);
        ReleaseLocomotion();

        MenuController menu = FindFirstObjectByType<MenuController>(FindObjectsInactive.Include);
        if (menu != null)
            menu.ShowMainMenu();
        else
            Debug.LogWarning("[RouteMapView] No MenuController found to return to.", this);

        // The fade back in runs on the fader (Bootstrap), because this object goes with the
        // scene it is unloading.
        if (fader != null) fader.FadeIn(fadeSeconds);

        Scene scene = gameObject.scene;
        if (scene.IsValid() && scene.isLoaded)
            SceneManager.UnloadSceneAsync(scene);
    }

    private void ReleaseLocomotion()
    {
        if (!_suspended) return;
        _suspended = false;
        if (ControllerHandednessManager.Instance != null)
            ControllerHandednessManager.Instance.ResumeLocomotion(this);
    }

    /// <summary>
    /// Draws the ring as a flat band in its own transform's XZ plane (the board surface), from
    /// 12 o'clock - the far side as the participant sees it - clockwise for 'fraction' of a
    /// turn. A mesh rather than a LineRenderer: the LineRenderer version stood partly upright
    /// and half of it disappeared into the table (Kade's screenshot, 2 Oct 2026).
    /// </summary>
    private void DrawRing(MeshFilter filter, float fraction)
    {
        if (filter == null) return;
        var renderer = filter.GetComponent<MeshRenderer>();
        fraction = Mathf.Clamp01(fraction);
        if (fraction <= 0.001f)
        {
            if (renderer != null) renderer.enabled = false;
            return;
        }
        if (renderer != null) renderer.enabled = true;

        Mesh mesh = filter.sharedMesh;
        if (mesh == null || mesh.name != "TimeRing")
        {
            mesh = new Mesh { name = "TimeRing" };
            mesh.MarkDynamic();
            filter.sharedMesh = mesh;
        }

        int steps = Mathf.Max(1, Mathf.CeilToInt(ringSegments * fraction));
        float sweep = fraction * Mathf.PI * 2f;
        float r0 = ringRadius - ringWidth * 0.5f, r1 = ringRadius + ringWidth * 0.5f;
        var verts = new Vector3[(steps + 1) * 2];
        var normals = new Vector3[verts.Length];
        var tris = new int[steps * 6];
        for (int i = 0; i <= steps; i++)
        {
            float a = sweep * i / steps;
            var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            verts[i * 2] = d * r0;
            verts[i * 2 + 1] = d * r1;
            normals[i * 2] = normals[i * 2 + 1] = Vector3.up;
        }
        for (int i = 0; i < steps; i++)
        {
            int a0 = i * 2, b0 = a0 + 1, a1 = a0 + 2, b1 = a0 + 3;
            // Clockwise seen from above, so the faces point up.
            tris[i * 6 + 0] = a0; tris[i * 6 + 1] = b0; tris[i * 6 + 2] = a1;
            tris[i * 6 + 3] = b0; tris[i * 6 + 4] = b1; tris[i * 6 + 5] = a1;
        }
        mesh.Clear();
        mesh.vertices = verts;
        mesh.normals = normals;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
    }

    // ------------------------------------------------------------------ walking figure
    private void Update()
    {
        if (!IsRunning || walker == null || _length <= 0f) return;

        float cycle = walkSeconds + holdAtEndSeconds + gapSeconds;
        float t = (Time.unscaledTime - _startTime) % Mathf.Max(0.01f, cycle);

        bool visible = t < walkSeconds + holdAtEndSeconds;
        if (walker.gameObject.activeSelf != visible) walker.gameObject.SetActive(visible);
        if (!visible) return;

        float d = t < walkSeconds ? _length * (t / Mathf.Max(0.01f, walkSeconds)) : _length;
        Vector3 here = PointAt(d);
        Vector3 ahead = PointAt(d + lookAhead) - here;
        if (d + lookAhead > _length) ahead = here - PointAt(d - lookAhead);
        ahead.y = 0f;
        if (ahead.sqrMagnitude > 1e-4f) _lastHeading = ahead.normalized;

        walker.localPosition = here;
        walker.localRotation = Quaternion.LookRotation(_lastHeading, Vector3.up);
    }

    private void BuildCumulative()
    {
        if (routePoints == null || routePoints.Length == 0)
        {
            _cumulative = new float[0];
            _length = 0f;
            return;
        }
        _cumulative = new float[routePoints.Length];
        for (int i = 1; i < routePoints.Length; i++)
            _cumulative[i] = _cumulative[i - 1] + Vector3.Distance(routePoints[i - 1], routePoints[i]);
        _length = _cumulative[routePoints.Length - 1];
    }

    private Vector3 PointAt(float distance)
    {
        if (_cumulative == null || _cumulative.Length != routePoints.Length) BuildCumulative();
        if (routePoints.Length == 0) return Vector3.zero;
        if (distance <= 0f) return routePoints[0];
        for (int i = 1; i < routePoints.Length; i++)
        {
            if (distance <= _cumulative[i])
            {
                float seg = _cumulative[i] - _cumulative[i - 1];
                float k = seg > 0f ? (distance - _cumulative[i - 1]) / seg : 0f;
                return Vector3.Lerp(routePoints[i - 1], routePoints[i], k);
            }
        }
        return routePoints[routePoints.Length - 1];
    }
}
