using System.Collections.Generic;
using UnityEngine;
using VRTutorial;

/// <summary>
/// Where the Guided line goes when the participant is not on the ideal walking line (decided
/// with Kade, 30 Sep 2026): the shortest walk to the end over the route's FOOTPATH NETWORK - both
/// footpaths of the route street, joined only at its zebras (Park, N10, School) - starting from
/// the nearest footpath they can walk to in a straight line.
///
/// So the line:
///   - follows whichever footpath they are on, instead of cutting across the road to the ideal
///     line (the far footpath before N10 runs straight to the N10 zebra; the school-side
///     footpath after it runs to the forecourt);
///   - only ever shows crossing the main road on a zebra;
///   - from inside a side street, leads back to the corner on that side of the road.
/// From the bus stop, the shortest walk over the network IS the ideal walking line.
///
/// Rules for picking where to join:
///   - the NEAREST reachable point of the network (a fence, hedge, wall or parked car in the
///     way at knee height rules a point out); never "the point with the shortest total walk",
///     which would happily draw the line straight across the road to the far footpath;
///   - among points within tieMetres of the nearest (standing in the middle of the road, or at
///     a zebra's end), the one with the shortest walk to the end.
///
/// Baked by the editor into RouteDefinition (Tools > VR Full Route > Update Route Definition in
/// RunSystem). Plain C#, no NavMesh.
/// </summary>
public class FootpathNetwork : RouteGuideLine.IGuidePath
{
    public float SightHeight = 0.4f;          // below fences and hedges, above kerbs
    public LayerMask Mask = Physics.DefaultRaycastLayers;
    public float TieMetres = 1.0f;
    public float OnPathDistance = 1.5f;       // closer than this, ease onto the footpath ahead
    public float JoinAhead = 1.5f;
    public float MaxJoinDistance = 80f;

    private readonly Vector2[] nodes;
    private readonly int[] linkA, linkB;
    private readonly bool[] linkZebra;
    private readonly float[] toEnd;           // shortest walk from each node to the destination
    private readonly int[] next;              // next node on that walk
    private readonly int dest;

    private struct Candidate { public int Link; public Vector2 Point; public float Dist, Total; public int Via; }
    private readonly List<Candidate> candidates = new List<Candidate>();
    private Vector2 lastFoot = new Vector2(float.NaN, float.NaN);
    private float lastTime = -1f;
    private readonly List<Vector2> lastPath = new List<Vector2>();

    public int NodeCount => nodes.Length;
    /// <summary>Shortest walk from a node to the end, in metres.</summary>
    public float WalkToEnd(int node) => toEnd[node];

    public FootpathNetwork(RouteDefinition route)
    {
        int n = route.FootNodeCount;
        nodes = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            Vector3 w = route.FootNode(i);
            nodes[i] = new Vector2(w.x, w.z);
        }

        var links = route.FootLinks;
        linkA = new int[links.Count];
        linkB = new int[links.Count];
        linkZebra = new bool[links.Count];
        var adj = new List<int>[n];
        for (int i = 0; i < n; i++) adj[i] = new List<int>();
        for (int i = 0; i < links.Count; i++)
        {
            linkA[i] = links[i].a;
            linkB[i] = links[i].b;
            linkZebra[i] = links[i].kind == "zebra";
            if (linkA[i] < 0 || linkB[i] < 0 || linkA[i] >= n || linkB[i] >= n) continue;
            adj[linkA[i]].Add(i);
            adj[linkB[i]].Add(i);
        }

        // Dijkstra from the destination: every node's shortest walk to the end, and which way.
        dest = route.FootDestination;
        toEnd = new float[n];
        next = new int[n];
        var done = new bool[n];
        for (int i = 0; i < n; i++) { toEnd[i] = float.PositiveInfinity; next[i] = -1; }
        toEnd[dest] = 0f;
        for (int iter = 0; iter < n; iter++)
        {
            int u = -1;
            for (int i = 0; i < n; i++)
                if (!done[i] && (u < 0 || toEnd[i] < toEnd[u])) u = i;
            if (u < 0 || float.IsPositiveInfinity(toEnd[u])) break;
            done[u] = true;
            foreach (int li in adj[u])
            {
                int v = linkA[li] == u ? linkB[li] : linkA[li];
                float c = toEnd[u] + Vector2.Distance(nodes[u], nodes[v]);
                if (c < toEnd[v]) { toEnd[v] = c; next[v] = u; }
            }
        }
    }

    public bool TryGetPath(Vector2 foot, float floorY, List<Vector2> path)
    {
        path.Clear();

        // The line is rebuilt most frames while it animates; the answer only changes as they move.
        if (lastPath.Count > 0 && Time.unscaledTime - lastTime < 0.25f && (foot - lastFoot).sqrMagnitude < 0.01f)
        {
            path.AddRange(lastPath);
            return true;
        }

        candidates.Clear();
        for (int i = 0; i < linkA.Length; i++)
        {
            int a = linkA[i], b = linkB[i];
            if (a < 0 || b < 0 || a >= nodes.Length || b >= nodes.Length) continue;
            Vector2 q = Closest(foot, nodes[a], nodes[b]);
            float d = Vector2.Distance(foot, q);
            if (d > MaxJoinDistance) continue;
            float viaA = Vector2.Distance(q, nodes[a]) + toEnd[a];
            float viaB = Vector2.Distance(q, nodes[b]) + toEnd[b];
            candidates.Add(new Candidate
            {
                Link = i, Point = q, Dist = d,
                Total = Mathf.Min(viaA, viaB), Via = viaA <= viaB ? a : b,
            });
        }
        if (candidates.Count == 0) return false;
        candidates.Sort((x, y) => x.Dist.CompareTo(y.Dist));

        // Nearest point they can walk to, then the best total walk among near-equals.
        int first = -1;
        for (int i = 0; i < candidates.Count && i < 40; i++)
            if (Reachable(foot, candidates[i].Point, floorY)) { first = i; break; }
        if (first < 0) first = 0;   // boxed in: fall back to the plain nearest

        Candidate best = candidates[first];
        float window = best.Dist + TieMetres;
        for (int i = first + 1; i < candidates.Count; i++)
        {
            Candidate c = candidates[i];
            if (c.Dist > window) break;
            if (c.Total < best.Total - 0.01f && Reachable(foot, c.Point, floorY)) best = c;
        }
        if (float.IsPositiveInfinity(best.Total)) return false;

        // Standing on the footpath: ease onto it a little ahead rather than jinking sideways.
        Vector2 join = best.Point;
        int node = best.Via;
        if (best.Dist < OnPathDistance && !linkZebra[best.Link])
        {
            Vector2 toNode = nodes[node] - join;
            float along = toNode.magnitude;
            if (along > 0.01f) join += toNode * (Mathf.Min(JoinAhead, along) / along);
        }

        path.Add(join);
        int guard = 0;
        while (node >= 0 && guard++ <= nodes.Length)
        {
            if ((nodes[node] - path[path.Count - 1]).sqrMagnitude > 0.0001f) path.Add(nodes[node]);
            if (node == dest) break;
            node = next[node];
        }

        lastPath.Clear();
        lastPath.AddRange(path);
        lastFoot = foot;
        lastTime = Time.unscaledTime;
        return true;
    }

    /// <summary>
    /// Can they walk from foot to p in a straight line - nothing solid at knee height in the way?
    /// Their own body (the rig's CharacterController or anything tagged Player) is ignored.
    /// </summary>
    private bool Reachable(Vector2 foot, Vector2 p, float floorY)
    {
        Vector3 a = new Vector3(foot.x, floorY + SightHeight, foot.y);
        Vector3 b = new Vector3(p.x, floorY + SightHeight, p.y);
        Vector3 d = b - a;
        float len = d.magnitude;
        if (len < 0.3f) return true;
        foreach (RaycastHit h in Physics.RaycastAll(a, d / len, len, Mask, QueryTriggerInteraction.Ignore))
        {
            if (h.collider.CompareTag("Player")) continue;
            if (h.collider.GetComponentInParent<CharacterController>() != null) continue;
            return false;
        }
        return true;
    }

    private static Vector2 Closest(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float l2 = ab.sqrMagnitude;
        float t = l2 > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2) : 0f;
        return a + ab * t;
    }
}
