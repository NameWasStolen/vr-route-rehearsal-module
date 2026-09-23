using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR;

namespace VRTutorial
{
    /// <summary>
    /// A blue "maps app" route line on the ground, running from the participant's feet to the
    /// end zone, with slow chevrons moving along it toward the destination.
    ///
    /// How it decides where to draw:
    ///
    ///   The route is a fixed CENTRELINE through the paving (see routePoints - by default the
    ///   middle of each PathRect from the start pad, up to the intersection, right, through the
    ///   zigzag and into the end zone). Every frame the participant's position is projected onto
    ///   that centreline.
    ///
    ///   - ON the path (within onPathDistance of the centreline): the line starts at their feet,
    ///     eases onto the centreline a short way ahead, and follows it to the end. It never
    ///     leaves the paving.
    ///   - OFF the path (on the grass, or down the wrong arm of the intersection): the line runs
    ///     from their feet to the nearest point on the centreline they can actually walk to in a
    ///     straight line - it will not aim through a hedge or fence - and continues along the
    ///     route from there. This is the only time the line crosses grass.
    ///
    /// A little hysteresis (offPathDistance > onPathDistance) stops the line flickering between
    /// the two modes when someone walks along the edge of the paving.
    ///
    /// TUTORIAL SCENE ONLY. The line gives away the right turn at the intersection, so it must
    /// not be placed in a study scene where wrong turns are measured.
    ///
    /// MODES
    ///
    ///   Always    - the original behaviour: Show() draws the line and it stays until Hide().
    ///   OnRequest - the reduced form (default). Show() only ARMS the guide; nothing is drawn
    ///               until the participant asks (a quick tap of A/X -> RequestShow, which draws
    ///               it for requestSeconds) or goes the wrong way at the intersection (3 m down
    ///               a wrong arm -> a controller buzz, onWrongTurn for the panel, and the line
    ///               back until they return). Helping only on request or after a mistake keeps
    ///               the route decision theirs (Parush et al. 2007; Brugger et al. 2019).
    ///
    /// Wiring (see route-guide-line.md in the project):
    ///   StepGoToExit -> TutorialStep.onStepEnter        -> RouteGuideLine.Show
    ///   end zone     -> TutorialZoneTrigger.onPlayerEntered -> RouteGuideLine.Hide
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class RouteGuideLine : MonoBehaviour
    {
        // ------------------------------------------------------------------ route
        [Header("Route (world X, Z)")]
        [Tooltip("Centreline of the route, start to destination, in world X/Z. The defaults are the " +
                 "middle of each PathRect in GuidedTutorialEnvironment, shifted +4 on z to match the " +
                 "TutorialEnvironment root. If the path is reshaped or the root moved, update these.")]
        [SerializeField] private Vector2[] routePoints =
        {
            // PathRect centres + 4 on z, because TutorialEnvironment sits at (0, 0, 4) in
            // Tutorial.unity. If that root is ever moved, shift these by the same amount.
            new Vector2( 0f,   0.5f),   // start pad
            new Vector2( 0f,  24f),     // four-way intersection
            new Vector2( 6f,  24f),     // ez1 -> ez2
            new Vector2( 6f,  27f),     // ez2 -> ez3
            new Vector2(11f,  27f),     // ez3 -> ez4
            new Vector2(11f,  21f),     // ez4 -> ez5
            new Vector2(16f,  21f),     // ez5 -> ez6
            new Vector2(16f,  24f),     // ez6 -> ez7
            new Vector2(26f,  24f),     // centre of the end zone
        };

        [Tooltip("Within this distance of the centreline the participant counts as on the path. " +
                 "The paving is 2 m wide, so 1 m is its edge.")]
        [SerializeField] private float onPathDistance = 1.05f;

        [Tooltip("Beyond this distance they count as off the path. Must be larger than On Path " +
                 "Distance; the gap between them stops the line flickering at the paving edge.")]
        [SerializeField] private float offPathDistance = 1.30f;

        [Tooltip("When on the path, how far ahead along the centreline the line rejoins it. The " +
                 "line never skips past a corner to do so.")]
        [SerializeField] private float joinAhead = 1.5f;

        // ------------------------------------------------------------------ player
        [Header("Player")]
        [Tooltip("The headset camera. Left empty, Camera.main is used (the rig lives in Bootstrap, " +
                 "so it is found at runtime).")]
        [SerializeField] private Transform head;

        [Tooltip("The rig root, used for floor height. Left empty, the object tagged Player is used.")]
        [SerializeField] private Transform rigRoot;

        [SerializeField] private string playerTag = "Player";

        [Tooltip("Start the line this far in front of the head's ground position, so it begins at " +
                 "the toes rather than directly under the participant, where they would have to look " +
                 "straight down to see it.")]
        [SerializeField] private float startAhead = 0.25f;

        // ------------------------------------------------------------------ look
        [Header("Look")]
        [SerializeField] private Material material;
        [SerializeField] private float width = 0.40f;

        [Tooltip("Height above the surface the ground raycast hits. The paving COLLIDER tops out at " +
                 "about 0.8 cm, but the visible slabs laid over it reach about 4 cm (3 cm thick, " +
                 "randomly lifted up to 6 mm), so anything under ~0.04 disappears beneath the tiles " +
                 "and only shows in the joints. 0.05 clears the tallest slab by about a centimetre.")]
        [SerializeField] private float lift = 0.05f;

        [Tooltip("Radius used to round the corners, in metres.")]
        [SerializeField] private float cornerRadius = 0.6f;

        [Tooltip("Metres of line per chevron.")]
        [SerializeField] private float chevronSpacing = 0.9f;

        [Tooltip("How fast the chevrons move toward the destination, in m/s. 0 stops them. Keep it " +
                 "slow - fast motion on the floor is uncomfortable in a headset.")]
        [SerializeField] private float scrollSpeed = 0.5f;

        [SerializeField] private Color lineColour    = new Color(0.10f, 0.45f, 1.00f);
        [SerializeField] private Color edgeColour    = new Color(0.04f, 0.16f, 0.45f);
        [SerializeField] private Color chevronColour = Color.white;

        // ------------------------------------------------------------------ behaviour
        [Header("Behaviour")]
        [Tooltip("Visible from the start of the scene. Leave off in normal use; Show() is wired to " +
                 "the \"All done\" step. Handy for testing.")]
        [SerializeField] private bool showOnStart = false;

        [Tooltip("How fast the line draws itself out from the feet when shown, in m/s.")]
        [SerializeField] private float revealSpeed = 20f;

        [Tooltip("Hide automatically within this distance of the destination, as a backup to the " +
                 "end-zone trigger.")]
        [SerializeField] private float arriveDistance = 2.0f;

        [Tooltip("Layers the ground raycast and the line-of-sight check can hit. Exclude the " +
                 "player's own layer if it is not ignored already.")]
        [SerializeField] private LayerMask collisionMask = Physics.DefaultRaycastLayers;

        [Tooltip("Height of the line-of-sight check when rejoining from the grass. Below the 0.9 m " +
                 "route hedge and the 1.2 m fence, above the 5 cm kerbs.")]
        [SerializeField] private float sightHeight = 0.4f;

        // ------------------------------------------------------------------ reduced guidance
        public enum GuideMode { Always, OnRequest }

        [Header("Mode")]
        [Tooltip("Always: Show() draws the line until Hide().\n" +
                 "OnRequest: Show() only arms the guide. The line appears when the participant " +
                 "taps A/X (RequestShow) or goes the wrong way, and retracts afterwards.")]
        [SerializeField] private GuideMode mode = GuideMode.OnRequest;

        [Header("On request")]
        [Tooltip("How long the line stays after a tap of A/X, in seconds. Another tap restarts it.")]
        [SerializeField] private float requestSeconds = 8f;

        [Tooltip("Session log event for a tap. How often and where people ask is worth having.")]
        [SerializeField] private string requestLogEvent = "tutorial_route_requested";

        [Header("Wrong turn")]
        [Tooltip("The wrong arms of the intersection, in world X/Z. A wrong turn fires once the " +
                 "participant is Trigger Distance along an arm from its Entry and within Half " +
                 "Width of its centreline (wide enough to include the grass beside it). It re-arms " +
                 "when they come back to the intersection.")]
        [SerializeField] private WrongArm[] wrongArms =
        {
            new WrongArm { name = "West",  entry = new Vector2(-1f, 24f), direction = new Vector2(-1f, 0f) },
            new WrongArm { name = "North", entry = new Vector2( 0f, 25f), direction = new Vector2( 0f, 1f) },
        };

        [Tooltip("After they come back to the intersection, keep the line up this long so they " +
                 "see which way to go next.")]
        [SerializeField] private float lingerSeconds = 4f;

        [Tooltip("Strength of the controller buzz on a wrong turn, 0-1.")]
        [Range(0f, 1f)]
        [SerializeField] private float hapticAmplitude = 0.6f;

        [Tooltip("Length of each buzz, in seconds.")]
        [SerializeField] private float hapticPulseSeconds = 0.15f;

        [Tooltip("Number of buzzes. Two short ones read as 'attention' rather than a collision.")]
        [SerializeField] private int hapticPulses = 2;

        [Tooltip("Session log event for a wrong turn; the arm name is the detail.")]
        [SerializeField] private string wrongTurnLogEvent = "tutorial_wrong_turn";

        [Tooltip("Fires on a wrong turn. Wire to TutorialFlow.RevealStep(\"WrongWay\") for the panel.")]
        public UnityEvent onWrongTurn;

        [Tooltip("Fires when they come back to the intersection after a wrong turn.")]
        public UnityEvent onBackOnRoute;

        [Serializable]
        public class WrongArm
        {
            public string name = "Arm";
            public Vector2 entry;
            public Vector2 direction = Vector2.left;
            [Tooltip("Metres along the arm, from Entry, before it counts as a wrong turn.")]
            public float triggerDistance = 3f;
            [Tooltip("Metres either side of the arm's centreline that still count as being in it.")]
            public float halfWidth = 4f;
            [NonSerialized] public bool fired;
        }

        [Header("Debug")]
        [SerializeField] private bool logStateChanges = false;

        /// <summary>True while the participant is on the paving along the route.</summary>
        public bool IsOnPath { get; private set; } = true;
        public bool IsShown { get; private set; }

        /// <summary>OnRequest mode: armed by Show() at the "All done" step, disarmed by Hide().</summary>
        public bool IsArmed { get; private set; }

        /// <summary>
        /// Set by ShowWayTask while the lesson runs, so practice taps are not logged as real
        /// "show me the way" requests.
        /// </summary>
        public bool SuppressLogging { get; set; }

        /// <summary>True while the line is on screen (drawing, drawn or retracting).</summary>
        public bool IsVisible => _visible;

        // ------------------------------------------------------------------ internals
        private const float SampleSpacing = 0.5f;   // centreline samples for the rejoin search
        private const float MeshSpacing   = 0.25f;  // max distance between ribbon cross-sections
        private const int   CornerSteps   = 6;

        private Mesh _mesh;
        private MeshRenderer _renderer;
        private Material _runtimeMaterial;
        private Texture2D _texture;

        private float[] _cumulative;                // arc length at each route point
        private float _totalLength;
        private readonly List<Vector2> _samples = new List<Vector2>();
        private readonly List<float> _sampleS = new List<float>();

        private readonly List<Vector2> _raw = new List<Vector2>();
        private readonly List<Vector2> _rounded = new List<Vector2>();
        private readonly List<Vector3> _points = new List<Vector3>();
        private readonly List<float> _pointS = new List<float>();   // arc-length-to-destination
        private readonly List<Vector3> _verts = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<int> _tris = new List<int>();

        private float _revealed = float.PositiveInfinity;
        private Vector2 _lastFoot = new Vector2(float.NaN, float.NaN);
        private int _rejoinIndex = -1;
        private float _nextRejoinSearch;
        private float _scroll;

        private bool _visible;
        private bool _retracting;
        private float _drawnLength;
        private float _requestUntil = -1f;
        private float _lingerUntil = -1f;
        private WrongArm _correctingArm;
        private Coroutine _haptics;

        // ================================================================== lifecycle
        private void Awake()
        {
            _renderer = GetComponent<MeshRenderer>();
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;

            _mesh = new Mesh { name = "RouteGuideLine" };
            _mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = _mesh;

            BuildTexture();
            _runtimeMaterial = material != null
                ? new Material(material)
                : CreateFallbackMaterial();
            if (_runtimeMaterial != null)
            {
                _runtimeMaterial.mainTexture = _texture;
                _runtimeMaterial.color = Color.white;
                _renderer.sharedMaterial = _runtimeMaterial;
            }

            PrepareRoute();

            SetHidden();
            if (showOnStart) Show();
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_runtimeMaterial != null) Destroy(_runtimeMaterial);
            if (_texture != null) Destroy(_texture);
        }

        private void LateUpdate()
        {
            bool active = mode == GuideMode.Always ? IsShown : IsArmed;
            if (!active) return;
            if (!ResolvePlayer(out Vector2 foot, out float floorY)) return;

            if (Vector2.Distance(foot, routePoints[routePoints.Length - 1]) < arriveDistance)
            {
                Hide();
                return;
            }

            if (mode == GuideMode.OnRequest) CheckWrongArms(foot);

            float now = Time.unscaledTime;
            bool want = mode == GuideMode.Always
                        || now < _requestUntil
                        || _correctingArm != null
                        || now < _lingerUntil;

            if (want && !_visible) BeginDraw();
            else if (want && _retracting) _retracting = false;         // asked again mid-retract
            else if (!want && _visible && !_retracting) _retracting = true;

            if (!_visible) return;

            float dt = Time.unscaledDeltaTime;
            bool animating;
            if (_retracting)
            {
                // Pull the line back in toward the feet rather than popping it off.
                if (float.IsPositiveInfinity(_revealed)) _revealed = _drawnLength;
                _revealed -= revealSpeed * dt;
                if (_revealed <= 0f) { EndDraw(); return; }
                animating = true;
            }
            else
            {
                animating = _revealed < float.PositiveInfinity;
                if (animating)
                {
                    _revealed += revealSpeed * dt;
                    if (_revealed > _totalLength + 50f) _revealed = float.PositiveInfinity;
                }
            }

            bool moved = float.IsNaN(_lastFoot.x) || (foot - _lastFoot).sqrMagnitude > 0.0004f;
            if (moved || animating)
            {
                BuildLine(foot, floorY);
                _lastFoot = foot;
            }

            if (scrollSpeed > 0f && _runtimeMaterial != null && chevronSpacing > 0f)
            {
                // Texture v increases toward the destination; moving the offset down makes the
                // pattern travel toward it.
                _scroll = Mathf.Repeat(_scroll - scrollSpeed / chevronSpacing * dt, 1f);
                _runtimeMaterial.mainTextureOffset = new Vector2(0f, _scroll);
            }
        }

        // ================================================================== public API
        /// <summary>
        /// Always mode: shows the line, drawing it out from the feet, until Hide().
        /// OnRequest mode: arms the guide - taps and wrong turns work from now on, but nothing is
        /// drawn yet. Wired to the "All done" step either way, so switching mode needs no rewiring.
        /// </summary>
        public void Show()
        {
            if (mode == GuideMode.OnRequest) { Arm(); return; }
            if (IsShown) return;
            IsShown = true;
            if (logStateChanges) Debug.Log("[RouteGuideLine] shown", this);
        }

        /// <summary>Shows the full line at once, with no draw-out (Always mode).</summary>
        public void ShowImmediate()
        {
            Show();
            if (mode == GuideMode.Always)
            {
                BeginDraw();
                _revealed = float.PositiveInfinity;
            }
        }

        /// <summary>OnRequest mode: taps and wrong-turn detection become live.</summary>
        public void Arm()
        {
            if (IsArmed) return;
            IsArmed = true;
            foreach (WrongArm arm in wrongArms) arm.fired = false;
            if (logStateChanges) Debug.Log("[RouteGuideLine] armed", this);
        }

        /// <summary>
        /// Draws the line for requestSeconds. Wire to AssistanceController.onTapped (a quick tap
        /// of A/X). Ignored before the guide is armed, so a tap during the lessons does nothing.
        /// </summary>
        public void RequestShow()
        {
            if (mode == GuideMode.Always) return;
            if (!IsArmed)
            {
                if (logStateChanges) Debug.Log("[RouteGuideLine] tap ignored - not armed yet", this);
                return;
            }
            _requestUntil = Time.unscaledTime + requestSeconds;
            if (!SuppressLogging) SessionLog.Record(requestLogEvent);
        }

        /// <summary>Hides the line and disarms. Wire to the end-zone trigger.</summary>
        public void Hide()
        {
            if (!IsShown && !IsArmed && !_visible) return;
            SetHidden();
            if (logStateChanges) Debug.Log("[RouteGuideLine] hidden", this);
        }

        private void SetHidden()
        {
            IsShown = false;
            IsArmed = false;
            _requestUntil = -1f;
            _lingerUntil = -1f;
            _correctingArm = null;
            EndDraw();
        }

        private void BeginDraw()
        {
            _visible = true;
            _retracting = false;
            _revealed = 0f;
            _lastFoot = new Vector2(float.NaN, float.NaN);
            if (_renderer != null) _renderer.enabled = true;
        }

        private void EndDraw()
        {
            _visible = false;
            _retracting = false;
            if (_renderer != null) _renderer.enabled = false;
            if (_mesh != null) _mesh.Clear();
        }

        // ================================================================== wrong turns
        private void CheckWrongArms(Vector2 foot)
        {
            foreach (WrongArm arm in wrongArms)
            {
                Vector2 dir = arm.direction.sqrMagnitude > 1e-6f ? arm.direction.normalized : Vector2.left;
                Vector2 rel = foot - arm.entry;
                float along = Vector2.Dot(rel, dir);
                float lateral = Mathf.Abs(rel.x * dir.y - rel.y * dir.x);

                if (!arm.fired && along >= arm.triggerDistance && lateral <= arm.halfWidth)
                {
                    arm.fired = true;
                    _correctingArm = arm;
                    OnWrongTurn(arm);
                }
                else if (arm.fired && along < 0.5f)
                {
                    // Back at the intersection: re-arm this arm, and keep the line up a little
                    // longer so the next thing they see is which way to go.
                    arm.fired = false;
                    if (_correctingArm == arm)
                    {
                        _correctingArm = null;
                        _lingerUntil = Time.unscaledTime + lingerSeconds;
                        onBackOnRoute?.Invoke();
                        if (logStateChanges) Debug.Log("[RouteGuideLine] back at the intersection", this);
                    }
                }
            }
        }

        private void OnWrongTurn(WrongArm arm)
        {
            if (logStateChanges) Debug.Log($"[RouteGuideLine] wrong turn: {arm.name}", this);
            SessionLog.Record(wrongTurnLogEvent, arm.name);

            if (_haptics != null) StopCoroutine(_haptics);
            _haptics = StartCoroutine(Buzz());

            onWrongTurn?.Invoke();
        }

        /// <summary>
        /// Buzzes BOTH controllers - someone who has forgotten which hand they nominated should
        /// still feel it. Goes through UnityEngine.XR directly so it needs no references to the
        /// rig in Bootstrap.
        /// </summary>
        private IEnumerator Buzz()
        {
            for (int i = 0; i < hapticPulses; i++)
            {
                Impulse(XRNode.LeftHand);
                Impulse(XRNode.RightHand);
                yield return new WaitForSecondsRealtime(hapticPulseSeconds * 2f);
            }
            _haptics = null;
        }

        private void Impulse(XRNode node)
        {
            UnityEngine.XR.InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid) return;
            if (device.TryGetHapticCapabilities(out HapticCapabilities caps) && caps.supportsImpulse)
                device.SendHapticImpulse(0u, hapticAmplitude, hapticPulseSeconds);
        }

        // ================================================================== route
        private void PrepareRoute()
        {
            int n = routePoints.Length;
            _cumulative = new float[n];
            for (int i = 1; i < n; i++)
                _cumulative[i] = _cumulative[i - 1] + Vector2.Distance(routePoints[i - 1], routePoints[i]);
            _totalLength = n > 0 ? _cumulative[n - 1] : 0f;

            _samples.Clear();
            _sampleS.Clear();
            for (float s = 0f; s <= _totalLength; s += SampleSpacing)
            {
                _samples.Add(PointAt(s));
                _sampleS.Add(s);
            }
        }

        private Vector2 PointAt(float s)
        {
            int n = routePoints.Length;
            if (s <= 0f) return routePoints[0];
            for (int i = 1; i < n; i++)
            {
                if (s <= _cumulative[i])
                {
                    float seg = _cumulative[i] - _cumulative[i - 1];
                    float t = seg > 0f ? (s - _cumulative[i - 1]) / seg : 0f;
                    return Vector2.Lerp(routePoints[i - 1], routePoints[i], t);
                }
            }
            return routePoints[n - 1];
        }

        /// <summary>Index of the first route point strictly beyond arc length s.</summary>
        private int NextVertexAfter(float s)
        {
            for (int i = 0; i < _cumulative.Length; i++)
                if (_cumulative[i] > s + 0.001f) return i;
            return _cumulative.Length;
        }

        private float Project(Vector2 p, out float distance)
        {
            float bestS = 0f;
            distance = float.MaxValue;
            for (int i = 1; i < routePoints.Length; i++)
            {
                Vector2 a = routePoints[i - 1], b = routePoints[i];
                Vector2 ab = b - a;
                float len2 = ab.sqrMagnitude;
                float t = len2 > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
                float d = Vector2.Distance(p, a + ab * t);
                if (d < distance)
                {
                    distance = d;
                    bestS = _cumulative[i - 1] + t * Mathf.Sqrt(len2);
                }
            }
            return bestS;
        }

        // ================================================================== line
        private void BuildLine(Vector2 foot, float floorY)
        {
            float s = Project(foot, out float d);

            bool wasOnPath = IsOnPath;
            if (IsOnPath && d > offPathDistance) IsOnPath = false;
            else if (!IsOnPath && d < onPathDistance) IsOnPath = true;
            if (wasOnPath != IsOnPath)
            {
                _rejoinIndex = -1;
                if (logStateChanges)
                    Debug.Log($"[RouteGuideLine] {(IsOnPath ? "back on" : "left")} the path", this);
            }

            _raw.Clear();
            _raw.Add(foot);

            float joinS;
            if (IsOnPath)
            {
                // Ease onto the centreline a little ahead, but never past the next corner, so the
                // shortcut stays on the paving.
                int next = NextVertexAfter(s);
                float limit = next < _cumulative.Length ? _cumulative[next] : _totalLength;
                joinS = Mathf.Min(s + joinAhead, limit);
            }
            else
            {
                joinS = FindRejoin(foot, floorY, s);
            }

            Vector2 join = PointAt(joinS);
            if ((join - foot).sqrMagnitude > 0.01f) _raw.Add(join);
            for (int i = NextVertexAfter(joinS); i < routePoints.Length; i++)
                _raw.Add(routePoints[i]);

            RoundCorners(_raw, _rounded);
            Densify(_rounded, floorY);
            BuildMesh();
        }

        /// <summary>
        /// Arc length of the nearest centreline point the participant can walk to in a straight
        /// line - checked at knee height so hedges and fences block it but kerbs do not. The
        /// search is throttled; between searches the last answer is reused.
        /// </summary>
        private float FindRejoin(Vector2 foot, float floorY, float fallbackS)
        {
            if (_rejoinIndex >= 0 && Time.unscaledTime < _nextRejoinSearch)
                return _sampleS[_rejoinIndex];
            _nextRejoinSearch = Time.unscaledTime + 0.2f;

            // Nearest-first order.
            int count = _samples.Count;
            var order = new List<int>(count);
            for (int i = 0; i < count; i++) order.Add(i);
            order.Sort((a, b) => (_samples[a] - foot).sqrMagnitude.CompareTo((_samples[b] - foot).sqrMagnitude));

            Vector3 from = new Vector3(foot.x, floorY + sightHeight, foot.y);
            int checks = Mathf.Min(count, 60);
            for (int k = 0; k < checks; k++)
            {
                Vector2 c = _samples[order[k]];
                Vector3 to = new Vector3(c.x, floorY + sightHeight, c.y);
                if (!Physics.Linecast(from, to, collisionMask, QueryTriggerInteraction.Ignore))
                {
                    _rejoinIndex = order[k];
                    return _sampleS[_rejoinIndex];
                }
            }

            // Nothing in sight (boxed in by hedges): fall back to the plain nearest point.
            _rejoinIndex = -1;
            return fallbackS;
        }

        private void RoundCorners(List<Vector2> src, List<Vector2> dst)
        {
            dst.Clear();
            if (src.Count < 3) { dst.AddRange(src); return; }

            dst.Add(src[0]);
            for (int i = 1; i < src.Count - 1; i++)
            {
                Vector2 a = src[i - 1], b = src[i], c = src[i + 1];
                Vector2 d1 = b - a, d2 = c - b;
                float l1 = d1.magnitude, l2 = d2.magnitude;
                if (l1 < 0.001f || l2 < 0.001f) continue;
                d1 /= l1; d2 /= l2;

                if (Vector2.Dot(d1, d2) > 0.999f) { dst.Add(b); continue; }  // straight through

                float r = Mathf.Min(cornerRadius, l1 * 0.5f, l2 * 0.5f);
                Vector2 p1 = b - d1 * r, p2 = b + d2 * r;
                for (int k = 0; k <= CornerSteps; k++)
                {
                    float t = k / (float)CornerSteps;
                    float u = 1f - t;
                    dst.Add(u * u * p1 + 2f * u * t * b + t * t * p2);   // quadratic Bezier
                }
            }
            dst.Add(src[src.Count - 1]);
        }

        /// <summary>
        /// Splits long straights so the ribbon follows the ground, drops each point onto the
        /// surface below it, trims to the reveal length, and records distance-to-destination
        /// for the texture so the chevrons stay put in the world as the participant walks.
        /// </summary>
        private void Densify(List<Vector2> src, float floorY)
        {
            _points.Clear();
            _pointS.Clear();
            if (src.Count < 2) return;

            // Total length first, so v can be measured back from the destination.
            float total = 0f;
            for (int i = 1; i < src.Count; i++) total += Vector2.Distance(src[i - 1], src[i]);

            float limit = Mathf.Min(total, _revealed);
            float walked = 0f;
            AddPoint(src[0], -total, floorY);

            for (int i = 1; i < src.Count; i++)
            {
                Vector2 a = src[i - 1], b = src[i];
                float seg = Vector2.Distance(a, b);
                if (seg < 0.0001f) continue;

                // Only the part of this segment that is within the reveal length.
                float usable = Mathf.Min(seg, limit - walked);
                if (usable <= 0f) break;

                int steps = Mathf.Max(1, Mathf.CeilToInt(usable / MeshSpacing));
                for (int k = 1; k <= steps; k++)
                {
                    float along = usable * k / steps;
                    AddPoint(a + (b - a) * (along / seg), walked + along - total, floorY);
                }
                walked += usable;
                if (walked >= limit) break;
            }
            _drawnLength = walked;
        }

        private void AddPoint(Vector2 xz, float sFromEnd, float floorY)
        {
            float y = floorY;
            Vector3 origin = new Vector3(xz.x, floorY + 0.6f, xz.y);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 1.6f, collisionMask,
                                QueryTriggerInteraction.Ignore))
                y = hit.point.y;
            _points.Add(new Vector3(xz.x, y + lift, xz.y));
            _pointS.Add(sFromEnd);
        }

        private void BuildMesh()
        {
            _verts.Clear(); _uvs.Clear(); _tris.Clear();
            int n = _points.Count;
            if (n < 2) { _mesh.Clear(); return; }

            float half = width * 0.5f;
            float tile = Mathf.Max(0.05f, chevronSpacing);
            for (int i = 0; i < n; i++)
            {
                Vector3 prev = _points[Mathf.Max(0, i - 1)];
                Vector3 next = _points[Mathf.Min(n - 1, i + 1)];
                Vector3 t = next - prev; t.y = 0f;
                if (t.sqrMagnitude < 1e-8f) t = Vector3.forward;
                t.Normalize();
                Vector3 right = new Vector3(t.z, 0f, -t.x);

                Vector3 p = _points[i];
                _verts.Add(transform.InverseTransformPoint(p - right * half));
                _verts.Add(transform.InverseTransformPoint(p + right * half));
                float v = _pointS[i] / tile;
                _uvs.Add(new Vector2(0f, v));
                _uvs.Add(new Vector2(1f, v));

                if (i > 0)
                {
                    int l0 = (i - 1) * 2, r0 = l0 + 1, l1 = i * 2, r1 = l1 + 1;
                    _tris.Add(l0); _tris.Add(l1); _tris.Add(r0);   // clockwise seen from above
                    _tris.Add(r0); _tris.Add(l1); _tris.Add(r1);
                }
            }

            _mesh.Clear();
            _mesh.SetVertices(_verts);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetTriangles(_tris, 0);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }

        // ================================================================== player
        private bool ResolvePlayer(out Vector2 foot, out float floorY)
        {
            foot = default; floorY = 0f;

            if (head == null && Camera.main != null) head = Camera.main.transform;
            if (rigRoot == null && !string.IsNullOrEmpty(playerTag))
            {
                GameObject tagged = GameObject.FindWithTag(playerTag);
                if (tagged != null) rigRoot = tagged.transform;
            }
            Transform source = head != null ? head : rigRoot;
            if (source == null) return false;

            Vector3 fwd = source.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude > 1e-4f) fwd.Normalize();
            Vector3 p = source.position + fwd * startAhead;
            foot = new Vector2(p.x, p.z);
            floorY = rigRoot != null ? rigRoot.position.y : 0f;
            return true;
        }

        // ================================================================== visuals
        /// <summary>
        /// Builds the ribbon texture in code: a dark edge, a blue body, and a white chevron
        /// pointing toward +v (the destination). Opaque, so there is no transparency sorting
        /// against the paving.
        /// </summary>
        private void BuildTexture()
        {
            const int w = 64, h = 128;
            _texture = new Texture2D(w, h, TextureFormat.RGBA32, true)
            {
                name = "RouteGuideChevrons",
                wrapModeU = TextureWrapMode.Clamp,
                wrapModeV = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 8,
            };

            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h;
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w;
                    float across = Mathf.Abs(u - 0.5f);   // 0 at the middle, 0.5 at the edge

                    Color c = lineColour;
                    if (across > 0.40f) c = edgeColour;
                    else
                    {
                        // Chevron: a "^" whose tip is at the centre and furthest along +v.
                        float d = v - (0.25f + (0.5f - across) * 0.55f);
                        if (across < 0.30f && d >= 0f && d <= 0.16f) c = chevronColour;
                    }
                    px[y * w + x] = c;
                }
            }
            _texture.SetPixels(px);
            _texture.Apply(true, true);
        }

        private Material CreateFallbackMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Texture");
            if (shader == null)
            {
                Debug.LogError("[RouteGuideLine] No Material assigned and no unlit shader found. " +
                               "Assign M_RouteGuide (Tools > VR Tutorial > Route Guide > Add To Scene).", this);
                return null;
            }
            Debug.LogWarning("[RouteGuideLine] No Material assigned - using a runtime one. This can " +
                             "fail in a build if the shader is stripped; assign M_RouteGuide.", this);
            return new Material(shader);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (routePoints == null || routePoints.Length < 2) return;
            Gizmos.color = new Color(0.1f, 0.45f, 1f, 1f);
            for (int i = 1; i < routePoints.Length; i++)
            {
                Vector3 a = new Vector3(routePoints[i - 1].x, 0.1f, routePoints[i - 1].y);
                Vector3 b = new Vector3(routePoints[i].x, 0.1f, routePoints[i].y);
                Gizmos.DrawLine(a, b);
                Gizmos.DrawSphere(b, 0.12f);
            }

            if (wrongArms == null) return;
            Gizmos.color = new Color(1f, 0.35f, 0.2f, 1f);
            foreach (WrongArm arm in wrongArms)
            {
                Vector2 d = arm.direction.sqrMagnitude > 1e-6f ? arm.direction.normalized : Vector2.left;
                Vector2 side = new Vector2(-d.y, d.x) * arm.halfWidth;
                Vector2 near = arm.entry + d * arm.triggerDistance;
                Vector3 a = new Vector3(near.x + side.x, 0.1f, near.y + side.y);
                Vector3 b = new Vector3(near.x - side.x, 0.1f, near.y - side.y);
                Gizmos.DrawLine(a, b);   // the trigger line across each wrong arm
            }
        }

        private void OnValidate()
        {
            if (offPathDistance < onPathDistance) offPathDistance = onPathDistance;
        }
#endif
    }
}
