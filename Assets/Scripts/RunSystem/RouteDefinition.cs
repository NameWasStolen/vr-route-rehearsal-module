using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The full route's layout, for the run statistics and (later) the Guided line.
///
/// Baked by the editor from FullRoutePlan - Tools > VR Full Route > Install Route in RunSystem
/// does it as part of the build, and Tools > VR Full Route > Update Route Definition refreshes
/// just this without rebuilding the meshes. Never typed by hand, so it cannot drift from the
/// route that was actually built.
///
/// Lives on the FullRouteEnvironment root. Everything is stored in that root's local space and
/// turned into world space on the way out, so moving the root (RunSystem puts it at z = -500)
/// needs no re-bake.
///
/// Holds:
///   - the walking line: the middle of the footpath along the ideal route, bus stop to
///     forecourt, crossing at the N10 zebra; plus where the run starts and ends along it;
///   - the route nodes N0-N16, with the six decision points marked;
///   - the 18 branches - one per WrongTurn_ trigger, same names - with where each leaves the
///     route and which way is into it;
///   - the decision zones and the zebra crossings as boxes.
/// And answers the questions the statistics need: how far along the route is this point, how
/// far off it, which node is nearest, which zone is it in.
/// </summary>
[DisallowMultipleComponent]
public class RouteDefinition : MonoBehaviour
{
    [Serializable]
    public class Node
    {
        public string name;
        public Vector3 position;
        public bool decision;
    }

    [Serializable]
    public class Branch
    {
        [Tooltip("Same as its WrongTurn_ trigger.")]
        public string name;
        [Tooltip("The route node it leaves from, e.g. N10.")]
        public string node;
        [Tooltip("True if that node is a decision point.")]
        public bool atDecision;
        [Tooltip("Where it leaves the route (local space).")]
        public Vector3 mouth;
        [Tooltip("Unit direction down the branch, away from the route (local space).")]
        public Vector3 into;
        [Tooltip("Centre of its WrongTurn_ trigger (local space).")]
        public Vector3 trigger;
    }

    [Serializable]
    public class Zone
    {
        public string name;
        public Vector3 centre;
        [Tooltip("Local forward of the box (local space).")]
        public Vector3 forward;
        [Tooltip("Width (x), height (y) and depth along forward (z), in metres.")]
        public Vector3 size;
    }

    /// <summary>A link of the footpath network: nodes a and b, walkable in a straight line.</summary>
    [Serializable]
    public class FootLink
    {
        public int a, b;
        [Tooltip("footpath, busroad, zebra or forecourt.")]
        public string kind;
    }

    [Header("Walking line (local space)")]
    [SerializeField] private Vector3[] walkingLine = new Vector3[0];
    [Tooltip("Distance along the walking line where the run timer starts (leaving CP_Start).")]
    [SerializeField] private float startDistance;
    [Tooltip("Distance along the walking line of the N10 zebra - the one road the route crosses.")]
    [SerializeField] private float crossingDistance;
    [Tooltip("Distance along the walking line where the run ends (entering CP_EndZone).")]
    [SerializeField] private float endDistance;

    [Header("Route street")]
    [Tooltip("Half the width of the route's street reserve (road, nature strips and both footpaths), " +
             "measured from the street centreline through the nodes. Anywhere inside it is on the route.")]
    [SerializeField] private float streetHalfWidth = 6.55f;
    [Tooltip("How far behind N0 (the bus stop) still counts as on the route, in metres - the spawn is " +
             "just behind it, and CP_Start reaches 3.5 m. Beyond this, walking up the bus road the wrong way is off the route.")]
    [SerializeField] private float startSlack = 3.5f;
    [Tooltip("How far past N16 still counts as on the route, in metres - the forecourt (end zone).")]
    [SerializeField] private float endSlack = 12f;

    [Header("Layout (local space)")]
    [SerializeField] private Node[] nodes = new Node[0];
    [SerializeField] private Branch[] branches = new Branch[0];
    [SerializeField] private Zone[] decisionZones = new Zone[0];
    [SerializeField] private Zone[] zebras = new Zone[0];

    [Header("Footpath network (local space) - for the Guided line")]
    [Tooltip("Both footpaths of the route street, joined only at its zebras, with the bus road start " +
             "and the forecourt. The Guided line takes the shortest walk to the end over this.")]
    [SerializeField] private Vector3[] footNodes = new Vector3[0];
    [SerializeField] private FootLink[] footLinks = new FootLink[0];
    [SerializeField] private int footDestination = -1;

    [Header("Bake")]
    [SerializeField] private string bakedAt;

    [Header("Scene view")]
    [SerializeField] private bool drawWhenNotSelected = false;

    private float[] cumulative;

    // ------------------------------------------------------------------ public data
    public float StartDistance => startDistance;
    public float CrossingDistance => crossingDistance;
    public float EndDistance => endDistance;

    /// <summary>Length of the ideal walk from leaving the bus stop to reaching the end zone, in metres.</summary>
    public float OptimalLength => endDistance - startDistance;

    public int PointCount => walkingLine.Length;
    public IReadOnlyList<Node> Nodes => nodes;
    public IReadOnlyList<Branch> Branches => branches;
    public IReadOnlyList<Zone> DecisionZones => decisionZones;
    public IReadOnlyList<Zone> Zebras => zebras;
    public string BakedAt => bakedAt;

    /// <summary>True once the footpath network has been baked (older bakes have none).</summary>
    public bool HasFootpathNetwork =>
        footNodes != null && footLinks != null && footNodes.Length > 1 && footLinks.Length > 0 &&
        footDestination >= 0 && footDestination < footNodes.Length;
    public int FootNodeCount => footNodes != null ? footNodes.Length : 0;
    public Vector3 FootNode(int i) => transform.TransformPoint(footNodes[i]);
    public IReadOnlyList<FootLink> FootLinks => footLinks;
    public int FootDestination => footDestination;
    public float StreetHalfWidth => streetHalfWidth;

    /// <summary>The first RouteDefinition in the scene of the given object, or any loaded one.</summary>
    public static RouteDefinition Find(GameObject near = null)
    {
        RouteDefinition fallback = null;
        foreach (RouteDefinition r in FindObjectsByType<RouteDefinition>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (near != null && r.gameObject.scene == near.scene) return r;
            if (fallback == null) fallback = r;
        }
        return fallback;
    }

    // ------------------------------------------------------------------ world-space access
    public Vector3 WalkingPoint(int i) => transform.TransformPoint(walkingLine[i]);
    public Vector3 NodePosition(Node n) => transform.TransformPoint(n.position);
    public Vector3 BranchMouth(Branch b) => transform.TransformPoint(b.mouth);
    public Vector3 BranchInto(Branch b) => transform.TransformDirection(b.into);

    /// <summary>World position at a distance along the walking line.</summary>
    public Vector3 PointAt(float distance)
    {
        EnsureCumulative();
        if (walkingLine.Length == 0) return transform.position;
        if (distance <= 0f) return WalkingPoint(0);
        for (int i = 1; i < walkingLine.Length; i++)
        {
            if (distance <= cumulative[i])
            {
                float seg = cumulative[i] - cumulative[i - 1];
                float t = seg > 0f ? (distance - cumulative[i - 1]) / seg : 0f;
                return transform.TransformPoint(Vector3.Lerp(walkingLine[i - 1], walkingLine[i], t));
            }
        }
        return WalkingPoint(walkingLine.Length - 1);
    }

    /// <summary>
    /// Projects a world position onto the walking line, on the ground plane. Returns the distance
    /// along the line; lateral is the horizontal distance from it (always positive).
    /// </summary>
    public float Project(Vector3 worldPosition, out float lateral)
    {
        EnsureCumulative();
        lateral = float.MaxValue;
        if (walkingLine.Length < 2) return 0f;

        Vector3 local = transform.InverseTransformPoint(worldPosition);
        Vector2 p = new Vector2(local.x, local.z);
        float best = 0f;
        for (int i = 1; i < walkingLine.Length; i++)
        {
            Vector2 a = new Vector2(walkingLine[i - 1].x, walkingLine[i - 1].z);
            Vector2 b = new Vector2(walkingLine[i].x, walkingLine[i].z);
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
            float d = Vector2.Distance(p, a + ab * t);
            if (d < lateral)
            {
                lateral = d;
                best = cumulative[i - 1] + t * Mathf.Sqrt(len2);
            }
        }
        return best;
    }

    /// <summary>The route node nearest a world position (horizontal distance), or null.</summary>
    public Node NearestNode(Vector3 worldPosition, out float distance)
    {
        Node best = null;
        distance = float.MaxValue;
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        foreach (Node n in nodes)
        {
            float d = Vector2.Distance(new Vector2(local.x, local.z), new Vector2(n.position.x, n.position.z));
            if (d < distance) { distance = d; best = n; }
        }
        return best;
    }

    /// <summary>
    /// Name of the decision zone or zebra containing a world position (horizontally), or null.
    /// Decision zones win if the two overlap.
    /// </summary>
    public string ZoneAt(Vector3 worldPosition)
    {
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        foreach (Zone z in decisionZones) if (Contains(z, local, 0f)) return z.name;
        foreach (Zone z in zebras) if (Contains(z, local, 0f)) return z.name;
        return null;
    }

    /// <summary>Name of the zebra containing a world position, grown by 'grow' metres, or null.</summary>
    public string ZebraAt(Vector3 worldPosition, float grow)
    {
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        foreach (Zone z in zebras) if (Contains(z, local, grow)) return z.name;
        return null;
    }

    /// <summary>
    /// Is a world position on the route street - within StreetHalfWidth (plus margin) of the
    /// centreline through the nodes, from just behind the bus stop to the far side of the end
    /// zone? Side streets count only as far as their mouth.
    /// </summary>
    public bool OnRouteStreet(Vector3 worldPosition, float margin = 0.5f)
    {
        if (nodes.Length < 2) return true;
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        Vector2 p = new Vector2(local.x, local.z);
        float limit = streetHalfWidth + margin;
        for (int i = 1; i < nodes.Length; i++)
        {
            Vector2 a = new Vector2(nodes[i - 1].position.x, nodes[i - 1].position.z);
            Vector2 b = new Vector2(nodes[i].position.x, nodes[i].position.z);
            Vector2 ab = b - a;
            float len = Mathf.Sqrt(ab.sqrMagnitude);
            if (len < 1e-4f) continue;
            Vector2 dir = ab * (1f / len);
            float along = Vector2.Dot(p - a, dir);
            float lo = i == 1 ? -startSlack : 0f;
            float hi = i == nodes.Length - 1 ? len + endSlack : len;
            // The two ends of the route are cut square: behind the bus stop and past the end
            // zone is off the route, not a rounded cap around the end node.
            if ((i == 1 && along < lo) || (i == nodes.Length - 1 && along > hi)) continue;
            along = Mathf.Clamp(along, lo, hi);
            if (Vector2.Distance(p, a + dir * along) <= limit) return true;
        }
        return false;
    }

    private static bool Contains(Zone z, Vector3 local, float grow)
    {
        Vector2 f = new Vector2(z.forward.x, z.forward.z);
        if (f.sqrMagnitude < 1e-6f) f = Vector2.up;
        f.Normalize();
        Vector2 r = new Vector2(f.y, -f.x);
        Vector2 d = new Vector2(local.x - z.centre.x, local.z - z.centre.z);
        return Mathf.Abs(Vector2.Dot(d, f)) <= z.size.z * 0.5f + grow && Mathf.Abs(Vector2.Dot(d, r)) <= z.size.x * 0.5f + grow;
    }

    // ------------------------------------------------------------------ baking
    /// <summary>Called by the editor bake. Everything in this object's local space.</summary>
    public void SetData(Vector3[] line, float start, float crossing, float end,
                        Node[] routeNodes, Branch[] routeBranches, Zone[] decisions, Zone[] crossings,
                        string stamp)
    {
        walkingLine = line ?? new Vector3[0];
        startDistance = start;
        crossingDistance = crossing;
        endDistance = end;
        nodes = routeNodes ?? new Node[0];
        branches = routeBranches ?? new Branch[0];
        decisionZones = decisions ?? new Zone[0];
        zebras = crossings ?? new Zone[0];
        bakedAt = stamp;
        cumulative = null;
    }

    /// <summary>Editor bake: the footpath network, in this object's local space.</summary>
    public void SetFootpathNetwork(Vector3[] localNodes, FootLink[] links, int destination)
    {
        footNodes = localNodes ?? new Vector3[0];
        footLinks = links ?? new FootLink[0];
        footDestination = destination;
    }

    private void EnsureCumulative()
    {
        if (cumulative != null && cumulative.Length == walkingLine.Length) return;
        cumulative = new float[walkingLine.Length];
        for (int i = 1; i < walkingLine.Length; i++)
            cumulative[i] = cumulative[i - 1] + Vector2.Distance(
                new Vector2(walkingLine[i - 1].x, walkingLine[i - 1].z),
                new Vector2(walkingLine[i].x, walkingLine[i].z));
    }

    private void OnValidate() => cumulative = null;

    // ------------------------------------------------------------------ scene view
    private void OnDrawGizmos()
    {
        if (drawWhenNotSelected) Draw();
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawWhenNotSelected) Draw();
    }

    private void Draw()
    {
        if (walkingLine.Length < 2) return;
        Vector3 lift = Vector3.up * 0.1f;

        Gizmos.color = new Color(0.85f, 0.1f, 0.7f);
        for (int i = 1; i < walkingLine.Length; i++)
            Gizmos.DrawLine(WalkingPoint(i - 1) + lift, WalkingPoint(i) + lift);
        Gizmos.color = Color.green;
        Gizmos.DrawSphere(PointAt(startDistance) + lift, 0.4f);
        Gizmos.color = Color.red;
        Gizmos.DrawSphere(PointAt(endDistance) + lift, 0.4f);

        foreach (Node n in nodes)
        {
            Gizmos.color = n.decision ? new Color(1f, 0.8f, 0.1f) : Color.white;
            Gizmos.DrawWireSphere(NodePosition(n) + lift, n.decision ? 1.2f : 0.6f);
        }

        foreach (Branch b in branches)
        {
            Vector3 m = BranchMouth(b) + lift;
            Gizmos.color = b.atDecision ? new Color(0.85f, 0.15f, 0.15f) : new Color(0.95f, 0.5f, 0.1f);
            Gizmos.DrawLine(m, m + BranchInto(b) * 6f);
            Gizmos.DrawWireSphere(transform.TransformPoint(b.trigger) + lift, 0.5f);
        }

        Gizmos.color = new Color(1f, 0.8f, 0.1f, 0.5f);
        foreach (Zone z in decisionZones) DrawZone(z);
        Gizmos.color = new Color(1f, 1f, 1f, 0.5f);
        foreach (Zone z in zebras) DrawZone(z);

        if (HasFootpathNetwork)
        {
            Vector3 up = Vector3.up * 0.15f;
            foreach (FootLink l in footLinks)
            {
                if (l.a < 0 || l.b < 0 || l.a >= footNodes.Length || l.b >= footNodes.Length) continue;
                Gizmos.color = l.kind == "zebra" ? new Color(0.1f, 0.8f, 0.2f) : new Color(0.2f, 0.6f, 1f);
                Gizmos.DrawLine(FootNode(l.a) + up, FootNode(l.b) + up);
            }
        }
    }

    private void DrawZone(Zone z)
    {
        Matrix4x4 old = Gizmos.matrix;
        Vector3 f = z.forward.sqrMagnitude > 1e-6f ? z.forward : Vector3.forward;
        Gizmos.matrix = transform.localToWorldMatrix * Matrix4x4.TRS(z.centre, Quaternion.LookRotation(f, Vector3.up), Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, z.size);
        Gizmos.matrix = old;
    }
}
