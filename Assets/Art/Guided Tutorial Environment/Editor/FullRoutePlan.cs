using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRTutorial.EditorTools
{
    /// <summary>
    /// Layout for the full route: getting off a bus and walking through suburban streets to
    /// the nearest shopping centre. Recreates Kade's route drawing (Sept 2026).
    ///
    /// Everything here is plain data plus geometry. No scene objects are created in this file.
    /// FullRouteEnvironment turns the plan into meshes. Keeping the two apart means the plan
    /// can be checked outside Unity, and a layout change never needs the mesh code to change.
    ///
    /// Units are metres. Vector2 is plan space: x = east, y = north (Unity z).
    ///
    /// SCALE. The drawing has no scale. The route was sized to about 360 m, which is about
    /// 4 minutes at the rig's 1.5 m/s continuous-move speed. A 3-minute (270 m) version was
    /// tried and did not fit: with real street widths every block collapses to its minimum
    /// and the school, park and shopping centre no longer fit. Six short stretches of the
    /// drawing are at their minimum length. Everything else keeps the drawing's proportions,
    /// and every street keeps the drawing's order and turn directions.
    ///
    /// Side streets are drawn as stubs on the map. Here each one can be walked for about 20 m
    /// before a soft end (planter and low fence across the street), with a wrong-turn trigger
    /// just inside its mouth, the same as the tutorial's north and west arms.
    /// </summary>
    public static class FullRouteLayout
    {
        // ------------------------------------------------------------ cross-section
        // A quiet two-way local street: a 6.6 m carriageway, then kerb, nature strip and
        // footpath on each side. That makes a 13.1 m reserve, property line to property line.
        public const float HalfCarriageway = 3.30f;
        public const float KerbWidth       = 0.15f;
        public const float NatureStrip     = 1.40f;
        public const float Footpath        = 1.50f;   // Melbourne standard width
        public const float BoundaryMargin  = 0.20f;

        public const float KerbEdge     = HalfCarriageway + KerbWidth;       // 3.45
        public const float FootInner    = KerbEdge + NatureStrip;            // 4.85
        public const float FootOuter    = FootInner + Footpath;              // 6.35
        public const float HalfReserve  = FootOuter + BoundaryMargin;        // 6.55
        public const float FootCentre   = FootInner + Footpath * 0.5f;       // 5.60
        public const float NatureCentre = KerbEdge + NatureStrip * 0.5f;     // 4.15

        // ------------------------------------------------------------------ heights
        // Each layer sits a clear centimetre above the one under it, so where two overlap
        // the higher one always wins and nothing z-fights. Footpath over road is the only
        // pair that must never overlap, and the plan clips footpaths out of other streets'
        // carriageways for exactly that reason.
        public const float BaseGroundTop = -0.02f;   // lots, front yards
        public const float NatureTop     =  0.00f;
        public const float RoadTop       =  0.01f;
        public const float FootBodyTop   =  0.022f;  // concrete body, has the collider
        public const float FootSlabTop   =  0.030f;  // slabs laid on it, visual only
        public const float KerbTop       =  0.10f;

        public const int Seed = 20260927;

        // ------------------------------------------------------------ dead ends
        // Wrong-turn side streets: long enough that the end is not obvious from the junction,
        // short enough that a wrong turn costs about a minute (there and back at 1.5 m/s).
        public const float DeadEndLength = 40f;   // junction to the centre of the court
        public const float BendAt        = 15f;   // straight run before the bend
        public const float CourtRadius   = 7.0f;  // kerb radius of the turning circle (older-suburb court)
        public const float CourtReserve  = CourtRadius + (HalfReserve - HalfCarriageway);   // 10.25
        public const float CourtFootMid  = CourtRadius + KerbWidth + NatureStrip + Footpath * 0.5f;
        public const float CourtFootIn   = CourtRadius + KerbWidth + NatureStrip;   // inner edge of the court's footpath ring, 8.55

        // Where a dead end's footpath joins its court's footpath ring (fixed 1 Oct 2026, after
        // Kade's screenshot). The ring used to run on round the court until it reached the
        // street's carriageway, so a tongue of paving crossed the nature strip to the kerb at
        // each entry, with a grass wedge beside it. Now:
        //  - the ring stops where its inner edge meets the line of the street footpath's inner
        //    edge (CourtJoinAngle either side of the street), so no paving crosses the nature strip;
        //  - the street footpath runs on CourtJoinAlong from the court centre, which puts its
        //    square end wholly inside the ring, so there is no gap between them;
        //  - the ring sits CourtFootDrop below the street footpath, so where the two overlap the
        //    street footpath shows and nothing z-fights (the same trick v3 used for ramps).
        public static readonly float CourtJoinAngle = Mathf.Asin(FootInner / CourtFootIn);                 // ~34.6 deg
        public static readonly float CourtJoinAlong = CourtFootIn * Mathf.Cos(CourtJoinAngle) - 0.02f;     // ~7.02 m
        public const float CourtFootDrop = 0.003f;

        // ------------------------------------------------------- T and L ends (v5)
        // Some dead ends finish at a T-intersection (or, for one, an L-corner) instead of a
        // court, agreed with Kade on 1 Oct 2026. Each arm runs ArmLength from the centre of
        // the junction (or the corner) to the end of the street, with the same planter closure
        // the bus road uses ArmClosureBack short of the end and a hedge across the very end.
        // An end is only converted where its arms clear every other street, court and zone by
        // the same 1.5 m the dead-end search uses; otherwise the court stays and a warning is
        // logged. Everything else about the dead end (length, bend, trigger) is unchanged.
        public const float ArmLength      = 12f;
        public const float ArmClosureBack = 3f;
        public static readonly string[] TeeEnds   = { "HouseCross_NE", "Side11_N", "Cross12_N", "Cross15_NNE", "Cross15_WNW" };
        public static readonly string[] ElbowEnds = { "Side5_S" };

        // ------------------------------------------------------------ crossings
        public const float ZebraWidth   = 3.0f;   // along the road
        public const float TactileDepth = 0.6f;   // yellow pad, measured back from the kerb

        // ------------------------------------------------------------------- route
        // N0..N16 in walking order. N0 lies on the bus road; the route street proper runs
        // N1 -> N16. Decision points marked with a circle on the drawing: N2, N4, N6, N10,
        // N12, N15.
        public static readonly Vector2[] RouteNodes =
        {
            new Vector2(    6.05f,    10.37f),   // N0  bus stop (start) - on the bus road, 12 m before the junction
            new Vector2(    0.00f,     0.00f),   // N1  bus road junction
            new Vector2(  -38.36f,    23.97f),   // N2  cross street by the special house
            new Vector2(  -63.53f,    38.60f),   // N3  bend onto the straight
            new Vector2(  -74.53f,    38.60f),   // N4  four-way
            new Vector2(  -96.47f,    38.60f),   // N5  side street south
            new Vector2( -116.48f,    38.60f),   // N6  corner - turn right (north), park ahead on the right
            new Vector2( -116.48f,    55.74f),   // N7  side street west
            new Vector2( -116.48f,    76.12f),   // N8  side street east (end of the park)
            new Vector2( -116.48f,    96.12f),   // N9  T - turn right (east)
            new Vector2(  -98.48f,    96.12f),   // N10 T - turn left (north)
            new Vector2(  -98.48f,   116.12f),   // N11 T - turn left (west), school ahead on the right
            new Vector2( -143.19f,   116.12f),   // N12 four-way past the school
            new Vector2( -159.62f,   116.12f),   // N13 slight bend
            new Vector2( -169.27f,   118.73f),   // N14 side street south-west
            new Vector2( -190.01f,   124.33f),   // N15 four-way - turn left (south-south-west)
            new Vector2( -198.64f,    92.92f),   // N16 end of the street at the shopping centre
        };

        // Side streets. The bus road and south road are one through road with soft closures
        // (planter + low fence) well past the bus stop. Every other side street is a wrong turn
        // that runs DeadEndLength metres, bends after BendAt so its end is out of sight from the
        // junction, and finishes in a cul-de-sac court. Which way each one bends is worked out
        // by the plan so the courts fit between the other streets.
        public static readonly BranchDef[] Branches =
        {
            new BranchDef("BusRoad",  1, new Vector2( 0.5039f,  0.8638f),  70.0f,  17.0f,  52.0f),
            new BranchDef("SouthRoad",  1, new Vector2( 0.0000f, -1.0000f),  58.0f,   8.6f,  40.0f),
            new BranchDef("HouseCross_NE",  2, new Vector2( 0.5055f,  0.8628f),  DeadEndLength, 8.6f, -1f),
            new BranchDef("HouseCross_SW",  2, new Vector2(-0.4672f, -0.8841f),  DeadEndLength, 8.6f, -1f),
            new BranchDef("Cross4_N",  4, new Vector2( 0.0000f,  1.0000f),  DeadEndLength, 8.6f, -1f),
            new BranchDef("Cross4_S",  4, new Vector2( 0.0000f, -1.0000f),  DeadEndLength, 8.6f, -1f),
            new BranchDef("Side5_S",  5, new Vector2( 0.0000f, -1.0000f),  DeadEndLength, 8.6f, -1f),
            new BranchDef("Corner6_S",  6, new Vector2( 0.0000f, -1.0000f),  DeadEndLength, 8.6f, -1f),
            new BranchDef("Side7_W",  7, new Vector2(-1.0000f,  0.0000f),  DeadEndLength, 8.6f, -1f),
            new BranchDef("Side8_E",  8, new Vector2( 1.0000f,  0.0000f),  DeadEndLength, 8.6f, -1f),
            new BranchDef("Side9_W",  9, new Vector2(-1.0000f,  0.0000f),  DeadEndLength, 8.6f, -1f),
            new BranchDef("Side10_E", 10, new Vector2( 1.0000f,  0.0000f),  DeadEndLength, 8.6f, -1f),
            new BranchDef("Side11_N", 11, new Vector2( 0.0000f,  1.0000f),  DeadEndLength, 8.6f, -1f),
            new BranchDef("Cross12_N", 12, new Vector2( 0.0000f,  1.0000f),  DeadEndLength, 8.6f, -1f),
            new BranchDef("Cross12_S", 12, new Vector2( 0.0000f, -1.0000f),  DeadEndLength, 8.6f, -1f),
            new BranchDef("Side14_SW", 14, new Vector2(-0.2905f, -0.9569f),  DeadEndLength, 8.6f, -1f),
            new BranchDef("Cross15_NNE", 15, new Vector2( 0.2510f,  0.9680f),  DeadEndLength, 8.6f, -1f),
            new BranchDef("Cross15_WNW", 15, new Vector2(-0.9675f,  0.2528f),  DeadEndLength, 8.6f, -1f),
        };

        public static readonly StarDef[] LandmarkLampStars =
        {
            new StarDef( 1, new Vector2( 0.9510f, -0.3091f)),
            new StarDef( 2, new Vector2( 0.2316f, -0.9728f)),
            new StarDef( 5, new Vector2(-0.7918f, -0.6108f)),
            new StarDef( 6, new Vector2(-0.9993f, -0.0377f)),
            new StarDef( 9, new Vector2(-0.0540f,  0.9985f)),
            new StarDef(11, new Vector2(-0.8064f,  0.5914f)),
            new StarDef(12, new Vector2( 0.6968f, -0.7173f)),
            new StarDef(14, new Vector2( 0.1660f,  0.9861f)),
            new StarDef(15, new Vector2( 0.8818f,  0.4717f)),
        };

        public static readonly int[] DecisionNodes = { 2, 4, 6, 10, 12, 15 };

        public struct BranchDef
        {
            public string Name; public int Node; public Vector2 Dir;
            public float Length;      // visible length of the street
            public float TriggerAt;   // wrong-turn trigger, metres from the node
            public float ClosureAt;   // soft end, metres from the node
            public BranchDef(string name, int node, Vector2 dir, float length, float trigger, float closure)
            { Name = name; Node = node; Dir = dir.normalized; Length = length; TriggerAt = trigger; ClosureAt = closure; }
        }

        /// <summary>A star on the drawing: which node, and which way from it.</summary>
        public struct StarDef
        {
            public int Node; public Vector2 Dir;
            public StarDef(int node, Vector2 dir) { Node = node; Dir = dir.normalized; }
        }

        public static float RouteLength
        {
            get
            {
                float total = Vector2.Distance(RouteNodes[0], RouteNodes[1]);
                for (int i = 1; i < RouteNodes.Length - 1; i++)
                    total += Vector2.Distance(RouteNodes[i], RouteNodes[i + 1]);
                return total;
            }
        }
    }

    // =====================================================================================
    //  Geometry primitives
    // =====================================================================================

    /// <summary>Oriented rectangle in plan.</summary>
    public struct Obb
    {
        public Vector2 C, U;
        public float HU, HV;

        public Obb(Vector2 centre, Vector2 u, float halfU, float halfV)
        { C = centre; U = u.normalized; HU = halfU; HV = halfV; }

        public Vector2 V { get { return new Vector2(-U.y, U.x); } }

        public bool Contains(Vector2 p, float grow = 0f)
        {
            Vector2 d = p - C;
            return Mathf.Abs(Vector2.Dot(d, U)) <= HU + grow &&
                   Mathf.Abs(Vector2.Dot(d, V)) <= HV + grow;
        }

        /// <summary>Distance from p to the box (0 inside).</summary>
        public float Distance(Vector2 p)
        {
            Vector2 d = p - C;
            float du = Mathf.Max(Mathf.Abs(Vector2.Dot(d, U)) - HU, 0f);
            float dv = Mathf.Max(Mathf.Abs(Vector2.Dot(d, V)) - HV, 0f);
            return Mathf.Sqrt(du * du + dv * dv);
        }

        public Vector2[] Corners()
        {
            Vector2 a = U * HU, b = V * HV;
            return new[] { C - a - b, C + a - b, C + a + b, C - a + b };
        }

        /// <summary>Separating-axis test, with both boxes grown by 'grow'.</summary>
        public bool Intersects(Obb o, float grow = 0f)
        {
            Vector2[] axes = { U, V, o.U, o.V };
            foreach (var ax in axes)
            {
                float r1 = HU * Mathf.Abs(Vector2.Dot(U, ax)) + HV * Mathf.Abs(Vector2.Dot(V, ax));
                float r2 = o.HU * Mathf.Abs(Vector2.Dot(o.U, ax)) + o.HV * Mathf.Abs(Vector2.Dot(o.V, ax));
                if (Mathf.Abs(Vector2.Dot(o.C - C, ax)) > r1 + r2 + 2f * grow) return false;
            }
            return true;
        }
    }

    /// <summary>A street centreline: a polyline with arc-length lookups and offset edges.</summary>
    public sealed class RouteStreet
    {
        public string Name;
        public int Index;
        public bool IsRoute;
        public int BranchIndex = -1;
        public readonly List<float> Triggers = new List<float>();   // wrong-turn triggers, arc length
        public readonly List<float> Closures = new List<float>();   // soft ends, arc length
        public bool OpenStart, OpenEnd;                             // ends that run off into the suburb
        public int CourtIndex = -1;                                 // cul-de-sac at the end, if any
        public int ArmOf = -1;                                      // cross street of a T: the dead end it heads
        public string EndKind = "";                                 // dead ends: "court", "T", "L" or "stub"; T cross streets: "arm"
        public Vector2[] Pts;
        public float[] Cum;
        public float Length;

        public RouteStreet(string name, Vector2[] pts)
        {
            Name = name; Pts = pts;
            Cum = new float[pts.Length];
            for (int i = 1; i < pts.Length; i++) Cum[i] = Cum[i - 1] + Vector2.Distance(pts[i - 1], pts[i]);
            Length = Cum[pts.Length - 1];
        }

        public int SegmentCount { get { return Pts.Length - 1; } }

        public int Seg(float s)
        {
            for (int i = 0; i < Pts.Length - 2; i++) if (s < Cum[i + 1]) return i;
            return Pts.Length - 2;
        }

        public Vector2 Dir(int seg) { return (Pts[seg + 1] - Pts[seg]).normalized; }
        public Vector2 Left(int seg) { Vector2 d = Dir(seg); return new Vector2(-d.y, d.x); }

        public Vector2 Point(float s)
        {
            int i = Seg(s);
            return Pts[i] + Dir(i) * (s - Cum[i]);
        }

        /// <summary>Point offset sideways (positive = left of travel), using the local segment.</summary>
        public Vector2 Offset(float s, float off)
        {
            int i = Seg(s);
            return Pts[i] + Dir(i) * (s - Cum[i]) + Left(i) * off;
        }

        /// <summary>
        /// The offset edge between s0 and s1: the two end stations plus a mitred station at
        /// every joint strictly between them. Two edges of the same interval always have the
        /// same number of stations, so they can be stitched into quads directly.
        /// </summary>
        public List<Vector2> Edge(float s0, float s1, float off)
        {
            var pts = new List<Vector2>();
            int a = SegAtStart(s0), b = SegAtEnd(s1);
            pts.Add(Pts[a] + Dir(a) * (s0 - Cum[a]) + Left(a) * off);
            for (int j = a + 1; j <= b; j++)
            {
                Vector2 n1 = Left(j - 1), n2 = Left(j);
                float k = 1f + Vector2.Dot(n1, n2);
                Vector2 miter = k > 0.2f ? (n1 + n2) / k : n2;
                pts.Add(Pts[j] + miter * off);
            }
            pts.Add(Pts[b] + Dir(b) * (s1 - Cum[b]) + Left(b) * off);
            return pts;
        }

        /// <summary>An s sitting exactly on a joint belongs to the segment before it.</summary>
        private int SegAtEnd(float s)
        {
            for (int i = 0; i < Pts.Length - 2; i++) if (s <= Cum[i + 1] + 1e-4f) return i;
            return Pts.Length - 2;
        }

        /// <summary>If s is within tol of an interior joint, the joint's arc length; otherwise s.</summary>
        public float SnapToJoint(float s, float tol)
        {
            for (int j = 1; j < Pts.Length - 1; j++) if (Mathf.Abs(s - Cum[j]) < tol) return Cum[j];
            return s;
        }

        /// <summary>Like Seg, but an s sitting exactly on a joint belongs to the segment after it.</summary>
        private int SegAtStart(float s)
        {
            for (int i = 0; i < Pts.Length - 2; i++) if (s < Cum[i + 1] - 1e-4f) return i;
            return Pts.Length - 2;
        }

        /// <summary>
        /// How far a segment's box must run past an interior joint to cover the outer corner
        /// of a mitred ribbon of this half-width: halfWidth * tan(turn / 2). Using the full
        /// half-width at every joint over-covers gentle bends and eats into the inner side.
        /// </summary>
        public float JointExtension(int joint, float halfWidth)
        {
            if (joint <= 0 || joint >= Pts.Length - 1) return 0f;
            float cos = Mathf.Clamp(Vector2.Dot(Dir(joint - 1), Dir(joint)), -1f, 1f);
            float half = Mathf.Acos(cos) * 0.5f;
            return halfWidth * Mathf.Min(Mathf.Tan(half), 2f);
        }

        /// <summary>
        /// True when p lies inside this street's own band of the given half-width, on a segment
        /// other than the one at arc length s. That is what a point on the inner side of a bend
        /// does once it has run past the corner: it is on the neighbouring segment's ground.
        /// </summary>
        public bool InOwnOtherSegment(float s, Vector2 p, float halfWidth)
        {
            int self = Seg(s);
            for (int i = 0; i < SegmentCount; i++)
            {
                if (i == self) continue;
                Vector2 d = Dir(i);
                var box = new Obb((Pts[i] + Pts[i + 1]) * 0.5f, d, (Cum[i + 1] - Cum[i]) * 0.5f, halfWidth);
                if (box.Contains(p, -0.001f)) return true;
            }
            return false;
        }

        List<Obb> _reserveCache;
        /// <summary>Reserve boxes, computed once - the layout search asks for them constantly.</summary>
        public List<Obb> ReserveBoxes()
        {
            if (_reserveCache == null) _reserveCache = Boxes(FullRouteLayout.HalfReserve);
            return _reserveCache;
        }

        /// <summary>One box per segment, lengthened at interior joints to cover the bend.</summary>
        public List<Obb> Boxes(float halfWidth)
        {
            var list = new List<Obb>();
            for (int i = 0; i < SegmentCount; i++)
            {
                Vector2 d = Dir(i);
                Vector2 a = Pts[i] - d * JointExtension(i, halfWidth);
                Vector2 b = Pts[i + 1] + d * JointExtension(i + 1, halfWidth);
                list.Add(new Obb((a + b) * 0.5f, d, Vector2.Distance(a, b) * 0.5f, halfWidth));
            }
            return list;
        }
    }

    // =====================================================================================
    //  The plan
    // =====================================================================================

    public enum BandKind { Road, Nature, Footpath, Kerb, Pad, Dash }
    public enum FenceStyle { None, Front, School, SpecialPicket, ParkLow, ParkBack, Hedge, Screen }

    public struct Band
    {
        public int Street; public float S0, S1, Off0, Off1; public BandKind Kind;
        public Band(int street, float s0, float s1, float off0, float off1, BandKind kind)
        { Street = street; S0 = s0; S1 = s1; Off0 = Mathf.Min(off0, off1); Off1 = Mathf.Max(off0, off1); Kind = kind; }
    }

    /// <summary>A fence line as a list of points (a run along a street follows its bends).</summary>
    public struct FenceLine
    {
        public List<Vector2> Pts; public FenceStyle Style; public string Owner;
        public FenceLine(List<Vector2> pts, FenceStyle style, string owner) { Pts = pts; Style = style; Owner = owner; }
    }

    /// <summary>
    /// A house: where its front wall sits, which way it faces, and its size. Sized here
    /// rather than at random in the builder so that it is known to fit its lot - blocks on
    /// this route are short, and a fixed house size leaves most of them empty.
    /// </summary>
    public struct HouseSpot
    {
        public Vector2 Frontage, Facing; public Obb Footprint;
        public float Width, Depth;   // main body
        public int GarageSide;       // 0 none, +1 / -1 to the house's right / left
        public int Seed;
    }
    public struct LampSpot { public Vector2 Pos, Arm; }
    /// <summary>
    /// A parked car. Dir is the way its bonnet points. Long = a 5.35 m outline also fits
    /// here, so the builder may put a dual-cab ute in this spot.
    /// </summary>
    public struct CarSpot { public Vector2 Pos, Dir; public int Variant; public bool Long; }
    public struct Across { public int Street; public float S; public string Name; }
    public struct NamedBox { public string Name; public Obb Box; }

    /// <summary>The turning circle at the end of a wrong-turn street.</summary>
    public sealed class Court { public int Street; public Vector2 C; public Vector2 InDir; public float Bend; }

    /// <summary>An annular band around a court, as one or more arcs (radians).</summary>
    public struct RingBand
    {
        public int Court; public float R0, R1, A0, A1; public BandKind Kind;
    }

    /// <summary>Convex quadrilateral in plan, corners stored counter-clockwise.</summary>
    public struct Quad4
    {
        public Vector2 A, B, C, D;

        public Quad4(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            A = a; B = b; C = c; D = d;
            float area = Cross(B - A, C - A) + Cross(C - A, D - A);
            if (area < 0f) { B = d; D = b; }
        }

        static float Cross(Vector2 u, Vector2 v) { return u.x * v.y - u.y * v.x; }

        public Vector2 Centre { get { return (A + B + C + D) * 0.25f; } }
        public Vector2[] Corners() { return new[] { A, B, C, D }; }

        /// <summary>Inside, or within 'grow' of every edge line (a slightly grown quad).</summary>
        public bool Contains(Vector2 p, float grow = 0f)
        {
            var c = Corners();
            for (int i = 0; i < 4; i++)
            {
                Vector2 e = c[(i + 1) % 4] - c[i];
                float len = e.magnitude;
                if (len < 1e-5f) continue;
                if (Cross(e, p - c[i]) / len < -grow) return false;
            }
            return true;
        }
    }

    /// <summary>
    /// A kerb ramp: paving from the road edge through the nature strip to the footpath, laid
    /// square to the kerb of the street it opens onto. 'Slope' runs from the road edge up to
    /// just past the kerb line (so there is no lip to trip on); 'Flat' continues at footpath
    /// height. Out points from the road into the footpath.
    /// </summary>
    public struct Opening
    {
        public Quad4 Slope, Flat;
        public Vector2 Out;
        public bool Zebra, Apron;
        public bool Contains(Vector2 p, float grow = 0f) { return Slope.Contains(p, grow) || Flat.Contains(p, grow); }
    }

    public struct Zebra { public int Street; public float S; public string Name; public bool School; }

    /// <summary>
    /// A side street (or the wrong way along the bus road) off the route, for the run
    /// statistics. Name matches its WrongTurn_ trigger. Mouth is where it leaves the route; Into
    /// points down it, away from the route.
    /// </summary>
    public struct Branch { public string Name; public int Node; public Vector2 Mouth, Into, Trigger; }
    public struct CrossingPost { public Vector2 Pos, RoadDir; public bool School; }

    public sealed class FullRoutePlan
    {
        // Inputs
        public readonly List<RouteStreet> Streets = new List<RouteStreet>();
        public RouteStreet Route { get { return Streets[0]; } }
        public RouteStreet BusRoad;
        public float BusStopS;   // arc length of the bus stop along BusRoad

        // Collision volumes in plan
        readonly List<List<Obb>> _carriageway = new List<List<Obb>>();
        readonly List<List<Obb>> _reserve = new List<List<Obb>>();
        readonly List<List<Obb>> _footBand = new List<List<Obb>>();

        // Zones
        public Obb Park, School, SpecialLot, Forecourt, ShopBuilding, BusStop;
        public Vector2 SchoolFrontDir;   // from the school toward its street
        public Vector2 SpecialFacing;    // from the special house toward the route
        public Vector2 ShopFacing;       // from the building toward the route

        // Outputs
        public readonly List<Band> Bands = new List<Band>();
        public readonly List<FenceLine> Fences = new List<FenceLine>();
        public readonly List<HouseSpot> Houses = new List<HouseSpot>();
        public readonly List<LampSpot> Lamps = new List<LampSpot>();
        public readonly List<Vector2> LandmarkLamps = new List<Vector2>();
        public readonly List<Vector2> Trees = new List<Vector2>();
        public readonly List<Vector3> GardenShrubs = new List<Vector3>();   // x, z, size
        public readonly List<Vector2> GardenTrees = new List<Vector2>();
        public readonly List<CarSpot> Cars = new List<CarSpot>();
        public readonly List<Across> Closures = new List<Across>();
        public readonly List<Across> EndCaps = new List<Across>();
        public readonly List<NamedBox> WrongTurns = new List<NamedBox>();
        public readonly List<NamedBox> Checkpoints = new List<NamedBox>();
        public readonly List<Court> Courts = new List<Court>();
        public readonly List<RingBand> Rings = new List<RingBand>();
        public readonly List<Opening> Openings = new List<Opening>();
        public readonly List<Quad4> Tactiles = new List<Quad4>();
        public readonly List<Zebra> Zebras = new List<Zebra>();
        public readonly List<Obb> ZebraStripes = new List<Obb>();
        public readonly List<CrossingPost> CrossingPosts = new List<CrossingPost>();

        // Route definition for the run statistics (see BuildWalkingLine).
        public readonly List<Vector2> WalkingLine = new List<Vector2>();
        public float WalkingStartS, WalkingCrossS, WalkingEndS;   // arc lengths along WalkingLine

        // Footpath network for the Guided line (see BuildFootpathNetwork).
        public struct FootLink { public int A, B; public string Kind; }
        public readonly List<Vector2> FootNodes = new List<Vector2>();
        public readonly List<FootLink> FootLinks = new List<FootLink>();
        public int FootDestination = -1;
        public readonly List<Branch> Branches = new List<Branch>();
        public readonly List<string> Warnings = new List<string>();
        public Vector2 Spawn, SpawnFacing;
        public Vector2 ShelterPos, ShelterFacing, BusFlagPos, BusFlagDir;
        public Vector2 ShopSignPos, ShopSignFacing;
        public Vector2 BoundsMin, BoundsMax;

        System.Random _rng = new System.Random(FullRouteLayout.Seed);
        float R(float a, float b) { return a + (float)_rng.NextDouble() * (b - a); }

        const float HR = FullRouteLayout.HalfReserve;

        public FullRoutePlan()
        {
            BuildZones();       // zones only need the route nodes, and the dead ends must avoid them
            BuildStreets();     // every dead end still ends in a court (or a stub) here

            // v5: trees, cars, houses and gardens are drawn from one shared random stream, so
            // any change to a street's geometry would reshuffle every house and car in the
            // suburb, the route included. To keep everything away from the T and L ends exactly
            // as it was reviewed, the suburb is first laid out as before (all courts) and those
            // placements are kept; then the ends are converted and only the ground around them
            // is filled again, from a stream of its own.
            int searchWarnings = Warnings.Count;
            LayOut();
            int layoutWarnings = Warnings.Count - searchWarnings;
            var kept = new Placements(this);
            BuildArmEnds();
            if (TeeEnds.Count + ElbowEnds.Count == 0) return;   // nothing converted: the layout stands

            Clear();
            Warnings.RemoveRange(searchWarnings, layoutWarnings);   // LayOut adds them again
            _refill = new Refill(this);
            _rng = new System.Random(FullRouteLayout.Seed + 5);
            kept.Seed(this);
            LayOut();
            _refill = null;
        }

        void LayOut()
        {
            BuildVolumes();
            BuildBands();
            BuildFences();
            BuildLandmarkLamps();
            BuildBusStop();
            BuildLamps();
            BuildTrees();
            BuildCars();
            BuildHouses();
            BuildTriggers();
            BuildBounds();
            BuildGardens();
            BuildWalkingLine();
            BuildFootpathNetwork();
        }

        /// <summary>Drops everything LayOut builds, ready to lay out again.</summary>
        void Clear()
        {
            _carriageway.Clear(); _reserve.Clear(); _footBand.Clear();
            Bands.Clear(); Fences.Clear(); Houses.Clear(); Lamps.Clear(); LandmarkLamps.Clear();
            Trees.Clear(); GardenShrubs.Clear(); GardenTrees.Clear(); Cars.Clear();
            Closures.Clear(); EndCaps.Clear(); WrongTurns.Clear(); Checkpoints.Clear();
            Rings.Clear(); Openings.Clear(); Tactiles.Clear(); Zebras.Clear(); ZebraStripes.Clear();
            CrossingPosts.Clear(); WalkingLine.Clear(); FootNodes.Clear(); FootLinks.Clear(); Branches.Clear();
        }

        // ------------------------------------------------------------ refill (v5)
        /// <summary>
        /// The ground a T or L end changes: within RefillReach of each old court's reserve, or of
        /// the reserve of a new arm. Houses and garden planting outside it are kept from the
        /// pre-v5 layout; inside it they are placed again. RefillReach covers a house set back
        /// 6 m with a 10 m deep body, so every house that faced a court, or now faces an arm, or
        /// would overlap an arm's reserve, is inside. Street trees and parked cars are only
        /// placed again on the converted dead ends and the new arms themselves: nothing about
        /// any other street changed, so its trees and cars stay exactly where they were.
        /// </summary>
        sealed class Refill
        {
            const float RefillReach = 16f;
            readonly List<Vector2> _courts = new List<Vector2>();
            readonly List<Vector2[]> _arms = new List<Vector2[]>();
            readonly List<RouteStreet> _changed = new List<RouteStreet>();

            public Refill(FullRoutePlan plan)
            {
                _courts.AddRange(plan._convertedCourts);
                foreach (var st in plan.Streets)
                {
                    if (st.ArmOf >= 0) { _arms.Add(new[] { st.Pts[0], st.Pts[st.Pts.Length - 1] }); _changed.Add(st); }
                    if (st.EndKind == "L") { _arms.Add(new[] { st.Pts[st.Pts.Length - 2], st.Pts[st.Pts.Length - 1] }); _changed.Add(st); }
                    if (st.EndKind == "T") _changed.Add(st);
                }
            }

            /// <summary>Inside the refill, and on a converted dead end or a new arm.</summary>
            public bool OnChangedStreet(Vector2 p)
            {
                if (!Inside(p)) return false;
                foreach (var st in _changed)
                    foreach (var b in st.ReserveBoxes()) if (b.Contains(p)) return true;
                return false;
            }

            public bool Inside(Vector2 p)
            {
                foreach (var c in _courts)
                    if (Vector2.Distance(p, c) < FullRouteLayout.CourtReserve + RefillReach) return true;
                foreach (var a in _arms)
                {
                    Vector2 d = a[1] - a[0];
                    float t = Mathf.Clamp(Vector2.Dot(p - a[0], d) / d.sqrMagnitude, 0f, 1f);
                    if (Vector2.Distance(p, a[0] + d * t) < HR + RefillReach) return true;
                }
                return false;
            }
        }

        Refill _refill;
        readonly List<Vector2> _convertedCourts = new List<Vector2>();   // centres of courts that became T or L ends

        /// <summary>True when a random placement at p should be made now: always, except on the
        /// v5 refill pass, where only the ground around the new ends is filled.</summary>
        bool Placing(Vector2 p) { return _refill == null || _refill.Inside(p); }

        /// <summary>The same, for street trees and parked cars (see Refill).</summary>
        bool PlacingOnStreet(Vector2 p) { return _refill == null || _refill.OnChangedStreet(p); }

        /// <summary>The pre-v5 random placements, and putting back the ones outside the refill.</summary>
        sealed class Placements
        {
            readonly List<Vector2> _trees, _gardenTrees;
            readonly List<CarSpot> _cars;
            readonly List<HouseSpot> _houses;
            readonly List<Vector3> _shrubs;

            public Placements(FullRoutePlan p)
            {
                _trees = new List<Vector2>(p.Trees); _gardenTrees = new List<Vector2>(p.GardenTrees);
                _cars = new List<CarSpot>(p.Cars); _houses = new List<HouseSpot>(p.Houses);
                _shrubs = new List<Vector3>(p.GardenShrubs);
            }

            /// <summary>Called once the ends are converted; the LayOut that follows adds to these.</summary>
            public void Seed(FullRoutePlan p)
            {
                p._keptTrees.Clear(); p._keptCars.Clear(); p._keptHouses.Clear(); p._keptShrubs.Clear(); p._keptGardenTrees.Clear();
                foreach (var t in _trees) if (!p._refill.OnChangedStreet(t)) p._keptTrees.Add(t);
                foreach (var c in _cars) if (!p._refill.OnChangedStreet(c.Pos)) p._keptCars.Add(c);
                // Houses and planting are kept unless they faced a court that is gone; BuildHouses
                // and BuildGardens then drop any that a new street (or new house) now covers.
                foreach (var h in _houses) if (!FacedConvertedCourt(p, h)) p._keptHouses.Add(h);
                p._keptShrubs.AddRange(_shrubs);
                p._keptGardenTrees.AddRange(_gardenTrees);
            }
        }

        static bool FacedConvertedCourt(FullRoutePlan p, HouseSpot h)
        {
            foreach (var c in p._convertedCourts)
            {
                Vector2 to = c - h.Frontage;
                if (to.magnitude < FullRouteLayout.CourtReserve + 7f && Vector2.Dot(h.Facing, to.normalized) > 0.9f) return true;
            }
            return false;
        }

        readonly List<Vector2> _keptTrees = new List<Vector2>(), _keptGardenTrees = new List<Vector2>();
        readonly List<CarSpot> _keptCars = new List<CarSpot>();
        readonly List<HouseSpot> _keptHouses = new List<HouseSpot>();
        readonly List<Vector3> _keptShrubs = new List<Vector3>();

        // ------------------------------------------------------------ walking line
        /// <summary>
        /// The ideal walk, for the run statistics: along the middle of the footpath the natural
        /// route uses, from the bus stop to the shopping-centre forecourt. Draws from no random
        /// numbers, so adding it changed nothing else in the layout.
        ///
        ///   - From the spawn point along the bus road's footpath back to the corner at N1.
        ///   - Round that corner onto the route's right-hand footpath (the inside of the corner,
        ///     so no road is crossed) as far as the route zebra at N10.
        ///   - Straight over the zebra to the left-hand footpath - the one the route continues on
        ///     north from N10 - and along it to N16.
        ///   - One metre into the forecourt (the end zone).
        /// Side-street mouths are crossed along the kerb line, as anyone would walk them.
        ///
        /// WalkingStartS / WalkingEndS are where the run timer starts (leaving CP_Start) and stops
        /// (entering CP_EndZone), so the optimal length is WalkingEndS - WalkingStartS.
        /// </summary>
        void BuildWalkingLine()
        {
            const float fc = FullRouteLayout.FootCentre;
            RouteStreet route = Route, bus = BusRoad;
            WalkingLine.Clear();

            // Bus road: the spawn is on its left footpath; walk back toward N1.
            int bs = bus.Seg(BusStopS);
            Vector2 n1 = route.Pts[0];
            Vector2 corner = Intersect(n1 + bus.Left(bs) * fc, bus.Dir(bs), n1 - route.Left(0) * fc, route.Dir(0));
            WalkingLine.Add(Spawn);
            WalkingLine.Add(corner);

            // Route, right-hand footpath, to the zebra.
            float sCross = route.Cum[10 - 1] - fc;
            foreach (var z in Zebras) if (z.Name == "Zebra_RouteCrossing") sCross = z.S;
            var before = route.Edge(0f, sCross, -fc);
            for (int i = 1; i < before.Count; i++) WalkingLine.Add(before[i]);
            WalkingCrossS = LineLength(WalkingLine);

            // Over the zebra, then the left-hand footpath to the end of the route.
            var after = route.Edge(sCross, route.Length, fc);
            for (int i = 0; i < after.Count; i++) WalkingLine.Add(after[i]);
            WalkingEndS = LineLength(WalkingLine);
            WalkingLine.Add(after[after.Count - 1] + route.Dir(route.SegmentCount - 1) * 1f);

            // The run starts when the participant leaves CP_Start (3.5 m either side of the stop,
            // along the bus road) walking toward N1. The spawn is 0.6 m past the stop.
            WalkingStartS = 0.6f + 3.5f;
        }

        // ------------------------------------------------------------ footpath network
        /// <summary>
        /// Where a pedestrian may walk to the end, for the Guided line (agreed with Kade,
        /// 30 Sep 2026): both footpaths of the route street, joined ONLY at its zebras (Park,
        /// N10, School). The guide finds the shortest walk to the end over this from wherever the
        /// participant is, so it follows the footpath they are on and never shows crossing the
        /// route street away from a zebra.
        ///
        ///   - "footpath": along the middle of each footpath, as the walking line does
        ///     (side-street mouths are crossed along the kerb line, as the walking line does).
        ///   - "busroad": the spawn to the N1 corner, along the bus road's footpath, and on across
        ///     the mouth of the route street to the far corner.
        ///   - "zebra": straight over each route-street zebra.
        ///   - "forecourt": both footpaths' ends into the forecourt, to the walking line's end
        ///     (the destination).
        /// The shortest walk from the spawn is exactly the walking line.
        /// </summary>
        void BuildFootpathNetwork()
        {
            const float fc = FullRouteLayout.FootCentre;
            RouteStreet route = Route, bus = BusRoad;
            FootNodes.Clear();
            FootLinks.Clear();

            // Corners at N1, where each footpath meets the bus road's footpath.
            int bs = bus.Seg(BusStopS);
            Vector2 n1 = route.Pts[0];
            Vector2 busFoot = n1 + bus.Left(bs) * fc;
            Vector2 cornerR = Intersect(busFoot, bus.Dir(bs), n1 - route.Left(0) * fc, route.Dir(0));
            Vector2 cornerL = Intersect(busFoot, bus.Dir(bs), n1 + route.Left(0) * fc, route.Dir(0));
            float sR0 = Mathf.Max(0f, Vector2.Dot(cornerR - n1, route.Dir(0)));
            float sL0 = Mathf.Max(0f, Vector2.Dot(cornerL - n1, route.Dir(0)));

            // Zebras on the route street, in order along it.
            var zs = new List<float>();
            foreach (var z in Zebras) if (z.Street == route.Index) zs.Add(z.S);
            zs.Sort();

            int spawn = AddFootNode(Spawn);
            int[] zR, zL;
            int endR = FootChain(route, sR0, zs, -fc, out zR);
            int endL = FootChain(route, sL0, zs, fc, out zL);
            int startR = AddFootNode(route.Edge(sR0, sR0 + 0.001f, -fc)[0]);
            int startL = AddFootNode(route.Edge(sL0, sL0 + 0.001f, fc)[0]);

            AddFootLink(spawn, startR, "busroad");
            AddFootLink(startR, startL, "busroad");
            for (int i = 0; i < zs.Count; i++) AddFootLink(zR[i], zL[i], "zebra");

            // Into the forecourt: the walking line ends 1 m in, off the left-hand footpath.
            Vector2 dir = route.Dir(route.SegmentCount - 1);
            int dest = AddFootNode(WalkingLine[WalkingLine.Count - 1]);
            AddFootLink(endL, dest, "forecourt");
            int inR = AddFootNode(FootNodes[endR] + dir * 1f);
            AddFootLink(endR, inR, "forecourt");
            AddFootLink(inR, dest, "forecourt");
            FootDestination = dest;
        }

        /// <summary>
        /// One footpath of the route street from s0 to the end, split at each zebra. Returns the
        /// node at the end; zebraNodes gets the node at each zebra.
        /// </summary>
        int FootChain(RouteStreet st, float s0, List<float> zebraS, float off, out int[] zebraNodes)
        {
            zebraNodes = new int[zebraS.Count];
            var cuts = new List<float> { s0 };
            foreach (float z in zebraS) cuts.Add(Mathf.Max(z, s0 + 0.01f));
            cuts.Add(st.Length);

            int prev = -1;
            for (int c = 0; c + 1 < cuts.Count; c++)
            {
                var pts = st.Edge(cuts[c], cuts[c + 1], off);
                for (int i = 0; i < pts.Count; i++)
                {
                    int node = AddFootNode(pts[i]);
                    if (prev >= 0 && node != prev) AddFootLink(prev, node, "footpath");
                    prev = node;
                }
                if (c < zebraS.Count) zebraNodes[c] = prev;
            }
            return prev;
        }

        int AddFootNode(Vector2 p)
        {
            for (int i = 0; i < FootNodes.Count; i++)
                if ((FootNodes[i] - p).sqrMagnitude < 0.0025f) return i;
            FootNodes.Add(p);
            return FootNodes.Count - 1;
        }

        void AddFootLink(int a, int b, string kind)
        {
            if (a == b) return;
            foreach (var l in FootLinks)
                if ((l.A == a && l.B == b) || (l.A == b && l.B == a)) return;
            FootLinks.Add(new FootLink { A = a, B = b, Kind = kind });
        }

        static Vector2 Intersect(Vector2 p, Vector2 d, Vector2 q, Vector2 e)
        {
            float den = d.x * e.y - d.y * e.x;
            if (Mathf.Abs(den) < 1e-6f) return q;
            Vector2 w = q - p;
            float t = (w.x * e.y - w.y * e.x) / den;
            return p + d * t;
        }

        static float LineLength(List<Vector2> pts)
        {
            float len = 0f;
            for (int i = 1; i < pts.Count; i++) len += Vector2.Distance(pts[i - 1], pts[i]);
            return len;
        }

        // ------------------------------------------------------------------ streets
        void BuildStreets()
        {
            var n = FullRouteLayout.RouteNodes;
            var routePts = new Vector2[n.Length - 1];
            Array.Copy(n, 1, routePts, 0, routePts.Length);
            Streets.Add(new RouteStreet("Route", routePts) { IsRoute = true, Index = 0 });

            var br = FullRouteLayout.Branches;

            // The bus road and the south road are one street bending through N1, with the
            // route leaving it as a T - which is how the drawing reads. Built as one polyline
            // so the corner opposite the route is mitred like any other bend.
            int busI = -1, southI = -1;
            for (int i = 0; i < br.Length; i++)
            {
                if (br[i].Name == "BusRoad") busI = i;
                if (br[i].Name == "SouthRoad") southI = i;
            }
            var south = br[southI]; var bus = br[busI];
            BusRoad = new RouteStreet("BusRoad", new[] { n[1] + south.Dir * south.Length, n[1], n[1] + bus.Dir * bus.Length })
            {
                Index = Streets.Count, BranchIndex = busI, OpenStart = true, OpenEnd = true,
            };
            BusRoad.Triggers.Add(south.Length - south.TriggerAt);
            BusRoad.Closures.Add(south.Length - south.ClosureAt);
            BusRoad.Triggers.Add(south.Length + bus.TriggerAt);
            BusRoad.Closures.Add(south.Length + bus.ClosureAt);
            BusStopS = south.Length + Vector2.Distance(n[0], n[1]);
            Streets.Add(BusRoad);

            // Lay out every wrong-turn street together: a backtracking search that always
            // places the street with the fewest remaining options next, and undoes earlier
            // choices when one runs out of room. Candidates are tried longest first, so the
            // first complete layout found keeps the dead ends as long as the space allows.
            // Deterministic: same inputs, same layout.
            //
            // The drawing packs more side streets into the middle blocks than can all end in a
            // court (the streets south from N12 and west from N9 point straight at each other),
            // so the search allows as few "stub" dead ends as it can: first none, then one, and
            // so on. A stub still bends out of sight; it just ends in a planter instead of a court.
            var pending = new List<int>();
            for (int i = 0; i < br.Length; i++) if (i != busI && i != southI) pending.Add(i);
            bool solved = false;
            for (int stubs = 0; stubs <= 4 && !solved; stubs++)
            {
                _budget = 20000;
                solved = SolveDeadEnds(pending, stubs);
                if (!solved) { while (Streets.Count > 2) Streets.RemoveAt(Streets.Count - 1); Courts.Clear(); }
            }
            if (!solved)
            {
                Warnings.Add("Dead-end search found no layout - placed greedily instead.");
                foreach (int i in pending) PlaceDeadEnd(i);
            }
            foreach (var st in Streets)
                if (st.BranchIndex >= 0 && st.CourtIndex < 0 && st != BusRoad)
                    Warnings.Add(st.Name + ": no room for a court here - a " + st.Length.ToString("0") +
                                 " m street that bends and ends in a planter closure instead.");

            // Keep Streets in drawing order after the route and bus road, so names and indices
            // stay stable from build to build.
            var dead = Streets.GetRange(2, Streets.Count - 2);
            dead.Sort((x, y) => x.BranchIndex.CompareTo(y.BranchIndex));
            var remap = new Dictionary<int, int>();
            for (int k = 0; k < dead.Count; k++) { remap[dead[k].Index] = k + 2; Streets[k + 2] = dead[k]; }
            foreach (var st in dead) st.Index = remap[st.Index];
            foreach (var c in Courts) c.Street = remap[c.Street];
        }

        // ------------------------------------------------------------ T and L ends
        public readonly List<string> TeeEnds = new List<string>();     // dead ends that now end in a T
        public readonly List<string> ElbowEnds = new List<string>();   // ... in an L-corner

        /// <summary>
        /// Swaps the court at the end of each dead end named in FullRouteLayout.TeeEnds and
        /// ElbowEnds for a T-intersection or an L-corner (v5, agreed with Kade 1 Oct 2026).
        /// Runs after the dead-end search, so every dead end keeps the length, bend and trigger
        /// it was given there - only what is at its end changes. Draws no random numbers.
        ///
        ///   T: a straight cross street through the end of the dead end, square to its last leg,
        ///      ArmLength each way. The dead end now stops at the cross street's centreline, the
        ///      way every side street meets the route. A planter closure ArmClosureBack short of
        ///      each end, and a hedge end cap across each end.
        ///   L: the dead end itself carries on round a 90-degree corner for ArmLength, toward
        ///      whichever side has more room, and finishes at a planter and end cap like a stub.
        ///
        /// Feasibility is checked here, not assumed: arms must keep the dead-end search's 1.5 m
        /// between reserves, stay clear of courts and the park, school, special lot and shopping
        /// centre. An end that does not fit keeps its court and the builder logs a warning.
        /// Arms added earlier count as streets for the ones after them, so no two can overlap.
        /// </summary>
        void BuildArmEnds()
        {
            const float A = FullRouteLayout.ArmLength;
            foreach (var st in Streets)
                if (st.BranchIndex >= 0 && st != BusRoad)
                    st.EndKind = st.CourtIndex >= 0 ? "court" : "stub";

            foreach (string name in FullRouteLayout.TeeEnds)
            {
                var st = DeadEndNamed(name);
                if (st == null) continue;
                Vector2 end = st.Pts[st.Pts.Length - 1], d = st.Dir(st.SegmentCount - 1);
                Vector2 l = new Vector2(-d.y, d.x);
                float left = ArmRoom(st, end, l), right = ArmRoom(st, end, -l);
                if (left < A || right < A)
                {
                    Warnings.Add(name + ": no room for a T here (arms fit " + left.ToString("0.0") + " m / " +
                                 right.ToString("0.0") + " m, need " + A.ToString("0") + " m) - kept its court.");
                    continue;
                }
                _convertedCourts.Add(Courts[st.CourtIndex].C);
                RemoveCourt(st);
                var cross = new RouteStreet(name + "_T", new[] { end + l * A, end - l * A })
                {
                    Index = Streets.Count, ArmOf = st.Index, OpenStart = true, OpenEnd = true, EndKind = "arm",
                };
                cross.Closures.Add(FullRouteLayout.ArmClosureBack);
                cross.Closures.Add(cross.Length - FullRouteLayout.ArmClosureBack);
                Streets.Add(cross);
                st.EndKind = "T";
                TeeEnds.Add(name);
            }

            foreach (string name in FullRouteLayout.ElbowEnds)
            {
                var st = DeadEndNamed(name);
                if (st == null) continue;
                Vector2 end = st.Pts[st.Pts.Length - 1], d = st.Dir(st.SegmentCount - 1);
                Vector2 l = new Vector2(-d.y, d.x);
                float left = ArmRoom(st, end, l), right = ArmRoom(st, end, -l);
                Vector2 turn = left >= right ? l : -l;
                if (Mathf.Max(left, right) < A)
                {
                    Warnings.Add(name + ": no room for an L-corner here (best arm " + Mathf.Max(left, right).ToString("0.0") +
                                 " m, need " + A.ToString("0") + " m) - kept its court.");
                    continue;
                }
                _convertedCourts.Add(Courts[st.CourtIndex].C);
                RemoveCourt(st);
                var pts = new Vector2[st.Pts.Length + 1];
                Array.Copy(st.Pts, pts, st.Pts.Length);
                pts[pts.Length - 1] = end + turn * A;
                var elbow = new RouteStreet(st.Name, pts) { Index = st.Index, BranchIndex = st.BranchIndex, OpenEnd = true, EndKind = "L" };
                elbow.Triggers.AddRange(st.Triggers);
                elbow.Closures.Add(elbow.Length - FullRouteLayout.ArmClosureBack);
                Streets[st.Index] = elbow;
                ElbowEnds.Add(name);
            }
        }

        RouteStreet DeadEndNamed(string name)
        {
            foreach (var st in Streets)
                if (st.Name == name && st.BranchIndex >= 0 && st != BusRoad)
                {
                    if (st.CourtIndex >= 0) return st;
                    Warnings.Add(name + ": has no court to turn into a T or L-corner - left as it is.");
                    return null;
                }
            Warnings.Add(name + " (in TeeEnds / ElbowEnds) is not a dead end of this layout - skipped.");
            return null;
        }

        /// <summary>
        /// How far an arm can run from 'from' along 'dir' (up to 40 m) with its whole reserve
        /// at least the dead-end search's 1.5 m from every other street's reserve, every other
        /// court and every zone. Measured as the dead-end search does: a centreline point at s
        /// needs HR + 1.5 m of clearance, which also covers the reserve past the arm's end.
        /// The dead end's own last leg is skipped (the arm starts on it); its earlier legs are not.
        /// </summary>
        float ArmRoom(RouteStreet own, Vector2 from, Vector2 dir)
        {
            const float gap = 1.5f, CR = FullRouteLayout.CourtReserve;
            Obb[] zones = { Park, School, SpecialLot, Forecourt, ShopBuilding };
            float room = 0f;
            for (float s = 0f; s <= 40f; s += 0.25f)
            {
                Vector2 q = from + dir * s;
                foreach (var o in Streets)
                {
                    var boxes = o.ReserveBoxes();
                    int n = o == own ? boxes.Count - 1 : boxes.Count;
                    for (int j = 0; j < n; j++)
                        if (boxes[j].Distance(q) < HR + gap) return room;
                }
                foreach (var c in Courts)
                    if (c.Street != own.Index && Vector2.Distance(c.C, q) < CR + HR + gap) return room;
                foreach (var z in zones)
                    if (z.Distance(q) < HR - 0.3f) return room;
                room = s;
            }
            return room;
        }

        void RemoveCourt(RouteStreet st)
        {
            int ci = st.CourtIndex;
            Courts.RemoveAt(ci);
            foreach (var o in Streets)
            {
                if (o.CourtIndex == ci) o.CourtIndex = -1;
                else if (o.CourtIndex > ci) o.CourtIndex--;
            }
        }

        // Sharper first: at 45-55 degrees the houses and screen planting on the inside of the bend
        // hide most of the court from the junction; at 30 degrees about half of it shows.
        static readonly float[] BendTry = { 45f, -45f, 55f, -55f, 40f, -40f, 60f, -60f, 35f, -35f, 30f, -30f, 70f, -70f, 22f, -22f, 15f, -15f };
        static readonly float[] LengthTry = { 1.0f, 0.9f, 0.8f, 0.7f, 0.6f, 0.5f, 0.42f };   // of DeadEndLength

        int _budget;

        List<Vector2[]> Candidates(int i)
        {
            var def = FullRouteLayout.Branches[i];
            Vector2 a = FullRouteLayout.RouteNodes[def.Node];
            var list = new List<Vector2[]>();
            foreach (float lf in LengthTry)
            {
                float len = FullRouteLayout.DeadEndLength * lf;
                float bendAt = Mathf.Min(FullRouteLayout.BendAt, len * 0.45f);
                foreach (float bend in BendTry)
                {
                    Vector2 b = a + def.Dir * bendAt;
                    list.Add(new[] { a, b, b + Rotate(def.Dir, bend) * (len - bendAt) });
                }
            }
            return list;
        }

        bool SolveDeadEnds(List<int> pending, int stubsLeft)
        {
            if (pending.Count == 0) return true;
            if (--_budget < 0) return false;

            // Most constrained street next.
            int pick = -1; List<Vector2[]> pickOpts = null;
            foreach (int i in pending)
            {
                var node = FullRouteLayout.Branches[i].Node;
                var opts = new List<Vector2[]>();
                foreach (var c in Candidates(i)) if (DeadEndFits(c, node)) opts.Add(c);
                if (pickOpts == null || opts.Count < pickOpts.Count) { pick = i; pickOpts = opts; }
                if (opts.Count == 0) break;
            }

            var rest = new List<int>(pending);
            rest.Remove(pick);

            if (pickOpts.Count > 0)
            {
                foreach (var pts in pickOpts)
                {
                    AddDeadEnd(pick, pts);
                    if (SolveDeadEnds(rest, stubsLeft)) return true;
                    Courts.RemoveAt(Courts.Count - 1);
                    Streets.RemoveAt(Streets.Count - 1);
                    if (_budget < 0) return false;
                }
                return false;
            }

            // No court fits for this street: make it a stub, if the allowance permits.
            if (stubsLeft <= 0) return false;
            int nodeIdx = FullRouteLayout.Branches[pick].Node;
            foreach (var pts in Candidates(pick))
            {
                var st = new RouteStreet("stub", pts);
                if (!PathClear(st, nodeIdx, 1.5f)) continue;
                AddStub(pick, pts);
                if (SolveDeadEnds(rest, stubsLeft - 1)) return true;
                Streets.RemoveAt(Streets.Count - 1);
                if (_budget < 0) return false;
            }
            return false;
        }

        void AddStub(int i, Vector2[] pts)
        {
            var def = FullRouteLayout.Branches[i];
            var st = new RouteStreet(def.Name, pts) { Index = Streets.Count, BranchIndex = i, OpenEnd = true };
            st.Triggers.Add(def.TriggerAt);
            st.Closures.Add(Mathf.Max(def.TriggerAt + 3f, st.Length - 4f));
            Streets.Add(st);
        }

        void AddDeadEnd(int i, Vector2[] pts)
        {
            var def = FullRouteLayout.Branches[i];
            var st = new RouteStreet(def.Name, pts) { Index = Streets.Count, BranchIndex = i };
            st.Triggers.Add(def.TriggerAt);
            Vector2 d0 = (pts[1] - pts[0]).normalized, d1 = (pts[2] - pts[1]).normalized;
            float bend = Mathf.Atan2(d0.x * d1.y - d0.y * d1.x, Vector2.Dot(d0, d1)) * Mathf.Rad2Deg;
            st.CourtIndex = Courts.Count;
            Courts.Add(new Court { Street = st.Index, C = pts[2], InDir = d1, Bend = bend });
            Streets.Add(st);
        }

        int CountLayouts(int i)
        {
            var def = FullRouteLayout.Branches[i];
            Vector2 a = FullRouteLayout.RouteNodes[def.Node];
            int count = 0;
            foreach (float lf in LengthTry)
            {
                float len = FullRouteLayout.DeadEndLength * lf;
                float bendAt = Mathf.Min(FullRouteLayout.BendAt, len * 0.45f);
                foreach (float bend in BendTry)
                {
                    Vector2 b = a + def.Dir * bendAt;
                    if (DeadEndFits(new[] { a, b, b + Rotate(def.Dir, bend) * (len - bendAt) }, def.Node)) count++;
                }
            }
            return count;
        }

        static Vector2 Rotate(Vector2 v, float deg)
        {
            float r = deg * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        /// <summary>
        /// Lays out one wrong-turn street: straight for BendAt metres, then a bend, then a court.
        /// Tries bends either way and, only if nothing fits, shorter lengths - first fit wins,
        /// so the result is deterministic. A bend of 30-40 degrees puts the court well off the
        /// line of sight from the junction, behind the houses on the inside of the bend.
        /// </summary>
        void PlaceDeadEnd(int i)
        {
            var def = FullRouteLayout.Branches[i];
            Vector2 a = FullRouteLayout.RouteNodes[def.Node];
            Vector2[] best = null;

            foreach (float lf in LengthTry)
            {
                float len = FullRouteLayout.DeadEndLength * lf;
                float bendAt = Mathf.Min(FullRouteLayout.BendAt, len * 0.45f);
                foreach (float bend in BendTry)
                {
                    Vector2 b = a + def.Dir * bendAt;
                    Vector2 c = b + Rotate(def.Dir, bend) * (len - bendAt);
                    var pts = new[] { a, b, c };
                    if (DeadEndFits(pts, def.Node)) { best = pts; break; }
                }
                if (best != null) break;
            }

            if (best == null)
            {
                // No room for a court at all: a short straight stub, as long as fits, ending
                // in the planter closure used on the bus road. Never overlaps anything.
                float len = 0f;
                for (float l = 30f; l >= 12f; l -= 1f)
                {
                    if (StubFits(a, def.Dir, l, def.Node)) { len = l; break; }
                }
                if (len <= 0f) len = 12f;
                var stub = new RouteStreet(def.Name, new[] { a, a + def.Dir * len })
                {
                    Index = Streets.Count, BranchIndex = i, OpenEnd = true,
                };
                stub.Triggers.Add(def.TriggerAt);
                stub.Closures.Add(Mathf.Max(def.TriggerAt + 3f, len - 4f));
                Streets.Add(stub);
                return;
            }

            AddDeadEnd(i, best);
        }

        bool StubFits(Vector2 a, Vector2 dir, float len, int node)
        {
            var pts = new[] { a, a + dir * (len * 0.5f), a + dir * len };
            return PathClear(new RouteStreet("stub", pts), node, 2.0f);
        }

        bool DeadEndFits(Vector2[] pts, int node)
        {
            const float gap = 1.5f;
            const float CR = FullRouteLayout.CourtReserve;
            var cand = new RouteStreet("candidate", pts);
            Vector2 court = pts[pts.Length - 1];
            Obb[] zones = { Park, School, SpecialLot, Forecourt, ShopBuilding };
            // Past the edge of the drawing's area is fine; the world just grows.

            // The court against everything already placed.
            foreach (var o in Streets)
                foreach (var box in o.ReserveBoxes())
                    if (box.Distance(court) < CR + gap) return false;
            foreach (var c in Courts)
                if (Vector2.Distance(c.C, court) < 2f * CR + gap) return false;
            foreach (var z in zones)
                if (z.Distance(court) < CR - 0.3f) return false;

            return PathClear(cand, node, gap);
        }

        /// <summary>
        /// The street itself, once clear of the junction it leaves from. The route segments
        /// meeting at that node are skipped - the street starts on them by design.
        /// </summary>
        bool PathClear(RouteStreet cand, int node, float gap)
        {
            const float CR = FullRouteLayout.CourtReserve;
            Obb[] zones = { Park, School, SpecialLot, Forecourt, ShopBuilding };
            int routeJoint = node - 1;   // route polyline index of this node
            Vector2 nodePos = FullRouteLayout.RouteNodes[node];
            for (float s = HR + 3f; s <= cand.Length; s += 1f)
            {
                Vector2 p = cand.Point(s);
                foreach (var o in Streets)
                {
                    var boxes = o.ReserveBoxes();
                    // Streets leaving the same junction share their first stretch of ground
                    // with this one by design, as the route does.
                    bool sibling = !o.IsRoute && Vector2.Distance(o.Pts[0], nodePos) < 0.01f;
                    bool nearJunction = s < 2f * HR + gap + 2f;
                    for (int j = 0; j < boxes.Count; j++)
                    {
                        if (o.IsRoute && (j == routeJoint || j == routeJoint - 1)) continue;
                        if (sibling && j == 0 && nearJunction) continue;
                        // Right at the junction the street may sit close to a nearby bend of the
                        // route (N3 is only 11 m from N4): reserves may touch, not overlap.
                        float need = nearJunction && o.IsRoute ? HR : HR + gap;
                        if (boxes[j].Distance(p) < need) return false;
                    }
                }
                foreach (var c in Courts)
                    if (Vector2.Distance(c.C, p) < CR + HR + gap) return false;
                foreach (var z in zones)
                    if (z.Distance(p) < HR - 0.3f) return false;
            }
            return true;
        }

        void BuildVolumes()
        {
            foreach (var st in Streets)
            {
                _carriageway.Add(st.Boxes(FullRouteLayout.KerbEdge));
                _reserve.Add(st.Boxes(HR));

                var bands = new List<Obb>();
                for (int i = 0; i < st.SegmentCount; i++)
                {
                    Vector2 d = st.Dir(i), l = st.Left(i);
                    Vector2 a = st.Pts[i] - d * st.JointExtension(i, HR);
                    Vector2 b = st.Pts[i + 1] + d * st.JointExtension(i + 1, HR);
                    Vector2 mid = (a + b) * 0.5f;
                    float hl = Vector2.Distance(a, b) * 0.5f;
                    bands.Add(new Obb(mid + l * FullRouteLayout.FootCentre, d, hl, FullRouteLayout.Footpath * 0.5f));
                    bands.Add(new Obb(mid - l * FullRouteLayout.FootCentre, d, hl, FullRouteLayout.Footpath * 0.5f));
                }
                _footBand.Add(bands);
            }
        }

        bool InOther(List<List<Obb>> set, int self, Vector2 p, float grow)
        {
            for (int k = 0; k < set.Count; k++)
            {
                if (k == self) continue;
                foreach (var b in set[k]) if (b.Contains(p, grow)) return true;
            }
            return false;
        }

        public bool InAnyCarriageway(Vector2 p, int except = -1, float grow = 0f)
        {
            return InOther(_carriageway, except, p, grow) || InCourt(p, except, FullRouteLayout.CourtRadius + FullRouteLayout.KerbWidth + grow);
        }

        public bool InAnyReserve(Vector2 p, int except = -1, float grow = 0f)
        {
            return InOther(_reserve, except, p, grow) || InCourt(p, except, FullRouteLayout.CourtReserve + grow);
        }

        /// <summary>Within radius r of the centre of any court not belonging to street 'except'.</summary>
        public bool InCourt(Vector2 p, int except, float r)
        {
            foreach (var c in Courts)
                if (c.Street != except && Vector2.Distance(p, c.C) < r) return true;
            return false;
        }

        /// <summary>Distance from p to the centre of this street's own court (infinite if none).</summary>
        float OwnCourtDist(RouteStreet st, Vector2 p)
        {
            return st.CourtIndex < 0 ? float.MaxValue : Vector2.Distance(p, Courts[st.CourtIndex].C);
        }
        public float Distance(Vector2 a, Vector2 b) { return Vector2.Distance(a, b); }

        /// <summary>
        /// How far p is back up the street from the centre of its own court, measured along the
        /// street's last leg (infinite if it has no court, or p is nowhere near it).
        /// </summary>
        float OwnCourtAlong(RouteStreet st, Vector2 p)
        {
            if (st.CourtIndex < 0) return float.MaxValue;
            var c = Courts[st.CourtIndex];
            if (Vector2.Distance(p, c.C) > FullRouteLayout.CourtReserve + 2f) return float.MaxValue;
            return Vector2.Dot(c.C - p, c.InDir);
        }

        /// <summary>On (or within r of) any footpath or carriageway, of any street.</summary>
        public bool OnWalkway(Vector2 p, float r)
        {
            if (InOther(_carriageway, -1, p, r)) return true;
            if (InCourt(p, -1, FullRouteLayout.CourtReserve + r)) return true;
            if (InOpening(p, r)) return true;
            if (OnBuiltFootpath(p, r)) return true;
            return InOther(_footBand, -1, p, r);
        }

        // -------------------------------------------------------------------- zones
        static Obb Quad(Vector2 origin, Vector2 u, Vector2 v, float lu, float lv)
        {
            // origin is a corner; u and v span the rectangle
            Vector2 c = origin + u * (lu * 0.5f) + v * (lv * 0.5f);
            return new Obb(c, u, lu * 0.5f, lv * 0.5f);
        }

        void BuildZones()
        {
            var n = FullRouteLayout.RouteNodes;

            // Park: the block on the east side of the street that runs north from N6, between
            // the route (south) and the side street at N8 (north). Matches the drawing.
            Vector2 u = (n[8] - n[6]).normalized, v = (n[5] - n[6]).normalized;
            float len = Vector2.Distance(n[6], n[8]) - 2f * HR;
            Park = Quad(n[6] + (u + v) * HR, u, v, len, Mathf.Min(17f, len * 0.7f));

            // School: north side of the long straight between N12 and N11.
            u = (n[11] - n[12]).normalized; v = new Vector2(-u.y, u.x);
            len = Vector2.Distance(n[11], n[12]) - 2f * HR;
            School = Quad(n[12] + (u + v) * HR, u, v, len, 34f);
            SchoolFrontDir = -v;

            // Special house: the corner lot north of the route, west of the cross street at N2.
            u = (n[3] - n[2]).normalized;
            v = FullRouteLayout.Branches[2].Dir;   // HouseCross_NE, runs north-east
            SpecialLot = Quad(n[2] + (u + v) * HR, u, v, 17f, 19f);
            SpecialFacing = -v;

            // Shopping centre, where the route street ends.
            u = (n[16] - n[15]).normalized; Vector2 a = new Vector2(-u.y, u.x);
            Forecourt = new Obb(n[16] + u * 5.5f, u, 5.5f, HR + 5f);
            ShopBuilding = new Obb(n[16] + u * (11f + 13f), u, 13f, 22f);
            ShopFacing = -u;
            // Pylon sign on the west side of the street end - the circle on the drawing.
            Vector2 west = a.x < 0f ? a : -a;
            ShopSignPos = n[16] + west * (HR + 1.6f) - u * 1.5f;
            ShopSignFacing = -u;
        }

        // -------------------------------------------------------------------- bands
        /// <summary>
        /// Samples a line along a street and returns the stretches where 'ok' holds, with the
        /// ends refined by bisection so neighbouring pieces meet to the millimetre.
        /// </summary>
        public List<Vector2> Runs(RouteStreet st, float off, Func<float, Vector2, bool> ok, float step = 0.1f, float minLen = 0.3f)
        {
            var result = new List<Vector2>();
            float s = 0f; bool inside = ok(0f, st.Offset(0f, off)); float start = 0f;
            while (s < st.Length)
            {
                float next = Mathf.Min(st.Length, s + step);
                bool nowOk = ok(next, st.Offset(next, off));
                if (nowOk != inside)
                {
                    float lo = s, hi = next;
                    for (int k = 0; k < 12; k++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        if (ok(mid, st.Offset(mid, off)) == inside) lo = mid; else hi = mid;
                    }
                    // A boundary that lands just past a bend is really at the bend: the sample
                    // after the joint uses the next segment's side, which is a different line.
                    float edge = st.SnapToJoint((lo + hi) * 0.5f, 0.15f);
                    if (inside && edge - start >= minLen) result.Add(new Vector2(start, edge));
                    start = edge; inside = nowOk;
                }
                s = next;
            }
            if (inside && st.Length - start >= minLen) result.Add(new Vector2(start, st.Length));
            return result;
        }

        void BuildBands()
        {
            const float cw = FullRouteLayout.HalfCarriageway;

            // Pass 1: road, nature strips and footpaths.
            foreach (var st in Streets)
            {
                int k = st.Index;
                Bands.Add(new Band(k, 0f, st.Length, -cw, cw, BandKind.Road));

                for (int side = -1; side <= 1; side += 2)
                {
                    Bands.Add(new Band(k, 0f, st.Length, side * FullRouteLayout.KerbEdge, side * FullRouteLayout.FootInner, BandKind.Nature));

                    // Footpath: stops at every other street's kerb line, and runs into its own
                    // court's footpath ring rather than across the turning circle.
                    foreach (var r in Runs(st, side * FullRouteLayout.FootCentre,
                                           (s, p) => !InAnyCarriageway(p, k) && !st.InOwnOtherSegment(s, p, FullRouteLayout.KerbEdge)
                                                     && OwnCourtAlong(st, p) > FullRouteLayout.CourtJoinAlong))
                        Bands.Add(new Band(k, r.x, r.y, side * FullRouteLayout.FootInner, side * FullRouteLayout.FootOuter, BandKind.Footpath));
                }
            }

            BuildCourtRings();
            BuildZebras();
            BuildOpenings();

            // Pass 2: kerbs, now that every opening is known. The kerb breaks at side-street
            // mouths, wherever a footpath meets the road, and at every opening.
            foreach (var st in Streets)
            {
                int k = st.Index;
                for (int side = -1; side <= 1; side += 2)
                {
                    foreach (var r in Runs(st, side * (cw + FullRouteLayout.KerbWidth * 0.5f),
                                           (s, p) => !InAnyCarriageway(p, k, 0.02f) && !InOther(_footBand, k, p, 0.05f)
                                                     && !st.InOwnOtherSegment(s, p, FullRouteLayout.KerbEdge)
                                                     && !InOpening(p, 0.02f)
                                                     && OwnCourtDist(st, p) > FullRouteLayout.CourtRadius + 0.1f))
                        Bands.Add(new Band(k, r.x, r.y, side * cw, side * FullRouteLayout.KerbEdge, BandKind.Kerb));
                }

                // Broken centre line, 3 m dash every 6 m, kept out of junctions, courts and
                // zebra crossings.
                for (float s = 2f; s + 3f < st.Length; s += 6f)
                {
                    bool clear = true;
                    for (float t = s - 1f; t <= s + 4f; t += 0.5f)
                    {
                        Vector2 p = st.Point(Mathf.Clamp(t, 0f, st.Length));
                        if (InAnyCarriageway(p, k, 0.5f) || OwnCourtDist(st, p) < FullRouteLayout.CourtRadius + 1f) { clear = false; break; }
                    }
                    foreach (var z in Zebras)
                        if (z.Street == k && s < z.S + FullRouteLayout.ZebraWidth * 0.5f + 3f && s + 3f > z.S - FullRouteLayout.ZebraWidth * 0.5f - 3f)
                            clear = false;
                    if (clear) Bands.Add(new Band(k, s, s + 3f, -0.06f, 0.06f, BandKind.Dash));
                }
            }
        }

        // ------------------------------------------------------------------- courts
        /// <summary>
        /// Rings for each cul-de-sac. The road disk and nature strip are whole circles - where
        /// they overlap the street leading in, the higher layer wins as everywhere else. The
        /// kerb and footpath rings are cut where the street enters, so neither crosses the road.
        /// </summary>
        void BuildCourtRings()
        {
            const float R = FullRouteLayout.CourtRadius, kw = FullRouteLayout.KerbWidth;
            for (int ci = 0; ci < Courts.Count; ci++)
            {
                var c = Courts[ci];
                float start = Mathf.Atan2(-c.InDir.y, -c.InDir.x);   // pointing back up the street

                Rings.Add(new RingBand { Court = ci, R0 = 0f, R1 = R, A0 = 0f, A1 = 2f * Mathf.PI, Kind = BandKind.Road });
                Rings.Add(new RingBand { Court = ci, R0 = R + kw, R1 = R + kw + FullRouteLayout.NatureStrip, A0 = 0f, A1 = 2f * Mathf.PI, Kind = BandKind.Nature });

                foreach (var arc in Arcs(c.C, R + kw * 0.5f, start, p => !InOther(_carriageway, -1, p, 0.02f)))
                    Rings.Add(new RingBand { Court = ci, R0 = R, R1 = R + kw, A0 = arc.x, A1 = arc.y, Kind = BandKind.Kerb });

                // Footpath ring: all the way round except across the street coming in, ending
                // exactly where its inner edge meets the line of the street's footpath (see
                // CourtJoinAngle). The street's footpaths run on into the ring to meet it.
                float f0 = FullRouteLayout.CourtFootIn, ja = FullRouteLayout.CourtJoinAngle;
                Rings.Add(new RingBand { Court = ci, R0 = f0, R1 = f0 + FullRouteLayout.Footpath,
                                         A0 = start + ja, A1 = start + 2f * Mathf.PI - ja, Kind = BandKind.Footpath });
                foreach (var arc in Arcs(c.C, FullRouteLayout.CourtFootMid, start + ja, p => InOther(_carriageway, Streets[c.Street].Index, p, 0f)))
                    if (arc.x < start + 2f * Mathf.PI - ja)
                    {
                        Warnings.Add(Streets[c.Street].Name + ": its court's footpath ring crosses another street - check it in the Scene view.");
                        break;
                    }
            }
        }

        /// <summary>Arcs of a circle where 'ok' holds, sampled every half degree from 'start'.</summary>
        List<Vector2> Arcs(Vector2 centre, float r, float start, Func<Vector2, bool> ok)
        {
            var result = new List<Vector2>();
            const int n = 720;
            float step = 2f * Mathf.PI / n;
            float runStart = float.NaN;
            for (int i = 0; i <= n; i++)
            {
                float a = start + i * step;
                bool good = i < n && ok(centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
                if (good && float.IsNaN(runStart)) runStart = a;
                if (!good && !float.IsNaN(runStart))
                {
                    if (a - step - runStart > 0.02f) result.Add(new Vector2(runStart, a - step));
                    runStart = float.NaN;
                }
            }
            return result;
        }

        // ---------------------------------------------------------------- crossings
        /// <summary>
        /// Zebra crossings, at the four places agreed with Kade (29 Sep 2026):
        ///  - Route: across the street at the dog-leg (N10), in line with the footpath the route
        ///    continues on - the one full road the natural route has to cross.
        ///  - School: across the street outside the school gate.
        ///  - Bus stop: across the bus road just past the stop.
        ///  - Park: across the long straight beside the park, before the corner at N6.
        /// </summary>
        void BuildZebras()
        {
            var n = FullRouteLayout.RouteNodes;
            var route = Route;
            float half = FullRouteLayout.ZebraWidth * 0.5f;

            // Route index of node k is k - 1.
            AddZebra(route, route.Cum[10 - 1] - FullRouteLayout.FootCentre, "Zebra_RouteCrossing", false);

            Vector2 d = (n[12] - n[11]).normalized;
            AddZebra(route, route.Cum[11 - 1] + Vector2.Dot(School.C - n[11], d), "Zebra_School", true);

            AddZebra(BusRoad, BusStopS + 7.5f, "Zebra_BusStop", false);

            AddZebra(route, (route.Cum[5 - 1] + route.Cum[6 - 1]) * 0.5f, "Zebra_Park", false);

            foreach (var z in Zebras)
            {
                var st = Streets[z.Street];
                int seg = st.Seg(z.S);
                Vector2 dir = st.Dir(seg), left = st.Left(seg), mid = st.Point(z.S);

                // Stripes run with the traffic, spaced across the road: 0.5 m white, 0.5 m gap.
                for (float x = -FullRouteLayout.HalfCarriageway + 0.55f; x < FullRouteLayout.HalfCarriageway - 0.3f; x += 1.0f)
                    ZebraStripes.Add(new Obb(mid + left * x, dir, half, 0.25f));

                for (int side = -1; side <= 1; side += 2)
                {
                    RectOpening(st, seg, side, mid, FullRouteLayout.ZebraWidth, true);
                    CrossingPosts.Add(new CrossingPost
                    {
                        Pos = mid + left * (side * FullRouteLayout.NatureCentre) + dir * (side * (half + 0.6f)),
                        RoadDir = dir, School = z.School,
                    });
                }
            }
        }

        void AddZebra(RouteStreet st, float s, string name, bool school)
        {
            Zebras.Add(new Zebra { Street = st.Index, S = s, Name = name, School = school });
        }

        // Ramp profile, measured square to the kerb from the centreline of the street.
        //   RampStart - 6 cm out onto the asphalt, so no hairline of grass can show at the edge.
        //   RampTop   - where the slope reaches footpath height: 0.24 m past the kerb line.
        //   Pads run from PadStart for TactileDepth, on the flat, parallel to the kerb.
        const float RampStart = FullRouteLayout.HalfCarriageway - 0.06f;
        const float RampTop   = FullRouteLayout.KerbEdge + 0.24f;
        const float PadStart  = FullRouteLayout.KerbEdge + 0.27f;
        const float RampEnd   = FullRouteLayout.KerbEdge + FullRouteLayout.NatureStrip + 0.3f;

        /// <summary>A footpath end that stops at a road: centre point at the kerb line,
        /// direction across the road, and the street the footpath belongs to.</summary>
        struct FootEnd { public Vector2 P, Dir; public int Street; }

        /// <summary>
        /// The street segment whose carriageway (kerb included) holds p, nearest centreline
        /// first. Courts are not included - footpath ends near them are skipped anyway.
        /// </summary>
        bool CarriagewayAt(Vector2 p, out int street, out int seg)
        {
            street = -1; seg = -1;
            float best = float.MaxValue;
            foreach (var st in Streets)
                for (int i = 0; i < st.SegmentCount; i++)
                {
                    Vector2 rel = p - st.Pts[i];
                    float along = Vector2.Dot(rel, st.Dir(i));
                    float lat = Mathf.Abs(Vector2.Dot(rel, st.Left(i)));
                    if (along < -0.5f || along > st.Cum[i + 1] - st.Cum[i] + 0.5f) continue;
                    if (lat > FullRouteLayout.KerbEdge + 0.01f || lat >= best) continue;
                    best = lat; street = st.Index; seg = i;
                }
            return street >= 0;
        }

        /// <summary>
        /// Slide c along 'dir' until it is 'dist' from the centreline of the street whose side
        /// normal is n (n points away from the road). cosA = -dot(dir, n) &gt; 0.
        /// </summary>
        static Vector2 AtDistance(Vector2 c, Vector2 dir, Vector2 n, Vector2 origin, float cosA, float dist)
        {
            return c + dir * ((Vector2.Dot(c - origin, n) - dist) / cosA);
        }

        /// <summary>
        /// A ramp square to the kerb of street st (segment seg, side +1 = left), centred on the
        /// line through 'onLine' that runs straight across the road. Used for zebra crossings
        /// and for the far side of crossings that land on a nature strip.
        /// </summary>
        void RectOpening(RouteStreet st, int seg, int side, Vector2 onLine, float width, bool zebra)
        {
            Vector2 d = st.Dir(seg), n = st.Left(seg) * side, o = st.Pts[seg];
            float h = Vector2.Dot(onLine - o, n);
            Vector2 c = onLine - n * h;                     // on the centreline
            Vector2 hw = d * (width * 0.5f);
            Openings.Add(new Opening
            {
                Slope = new Quad4(c + n * RampStart - hw, c + n * RampStart + hw, c + n * RampTop + hw, c + n * RampTop - hw),
                Flat  = new Quad4(c + n * RampTop - hw,   c + n * RampTop + hw,   c + n * RampEnd + hw, c + n * RampEnd - hw),
                Out = n, Zebra = zebra,
            });
            Vector2 pw = d * (width * 0.5f - 0.05f);
            float p0 = PadStart, p1 = PadStart + FullRouteLayout.TactileDepth;
            Tactiles.Add(new Quad4(c + n * p0 - pw, c + n * p0 + pw, c + n * p1 + pw, c + n * p1 - pw));
        }

        /// <summary>
        /// The near side of a crossing: a footpath that stops at another street's kerb line.
        /// The ramp keeps the footpath's own edges (so it lines up with the paving it continues)
        /// but its road end and its pad run parallel to the kerb it meets, which is what makes
        /// a crossing on an angled street read cleanly. Returns false if this end should not
        /// get a ramp - it meets its own street at a bend, or the road at a glancing angle.
        /// </summary>
        bool EndOpening(FootEnd e)
        {
            int k, seg;
            if (!CarriagewayAt(e.P + e.Dir * 0.3f, out k, out seg) || k == e.Street) return false;
            var st = Streets[k];
            Vector2 o = st.Pts[seg], L = st.Left(seg);
            Vector2 n = L * (Vector2.Dot(e.P - o, L) >= 0f ? 1f : -1f);
            float cosA = -Vector2.Dot(e.Dir, n);
            if (cosA < 0.35f) return false;

            Vector2 lat = new Vector2(-e.Dir.y, e.Dir.x);
            Vector2 a = e.P + lat * (FullRouteLayout.Footpath * 0.5f), b = e.P - lat * (FullRouteLayout.Footpath * 0.5f);
            // The back edge: 0.3 m behind the footpath end, but never short of RampEnd's reach.
            System.Func<Vector2, Vector2> back = c =>
                AtDistance(c, e.Dir, n, o, cosA, Mathf.Max(Vector2.Dot(c - o, n) + 0.3f * cosA, RampTop + 0.3f));
            System.Func<Vector2, float, Vector2> at = (c, dist) => AtDistance(c, e.Dir, n, o, cosA, dist);

            Openings.Add(new Opening
            {
                Slope = new Quad4(at(a, RampStart), at(b, RampStart), at(b, RampTop), at(a, RampTop)),
                Flat  = new Quad4(at(a, RampTop), at(b, RampTop), back(b), back(a)),
                Out = n, Apron = true,
            });
            float shrink = (FullRouteLayout.Footpath * 0.5f - 0.05f) / (FullRouteLayout.Footpath * 0.5f);
            Vector2 pa = e.P + (a - e.P) * shrink, pb = e.P + (b - e.P) * shrink;
            float p0 = PadStart, p1 = PadStart + FullRouteLayout.TactileDepth;
            Tactiles.Add(new Quad4(at(pa, p0), at(pb, p0), at(pb, p1), at(pa, p1)));
            return true;
        }

        /// <summary>On a footpath band that was actually laid (not just where one could be).</summary>
        bool OnBuiltFootpath(Vector2 p, float grow)
        {
            foreach (var b in Bands)
            {
                if (b.Kind != BandKind.Footpath) continue;
                var st = Streets[b.Street];
                for (int i = 0; i < st.SegmentCount; i++)
                {
                    Vector2 d = st.Dir(i), l = st.Left(i), rel = p - st.Pts[i];
                    float along = Vector2.Dot(rel, d), lat = Vector2.Dot(rel, l);
                    float s = st.Cum[i] + along;
                    if (along < -grow || s > st.Cum[i + 1] + grow) continue;
                    if (s < b.S0 - grow || s > b.S1 + grow) continue;
                    if (lat >= b.Off0 - grow && lat <= b.Off1 + grow) return true;
                }
            }
            return false;
        }

        public bool InOpening(Vector2 p, float grow)
        {
            foreach (var o in Openings) if (o.Contains(p, grow)) return true;
            return false;
        }

        /// <summary>Does opening 'i' overlap any opening before it? Sampled both ways.</summary>
        bool OverlapsEarlier(int i)
        {
            var o = Openings[i];
            for (int j = 0; j < i; j++)
            {
                var q = Openings[j];
                if (SampleIn(o, q) || SampleIn(q, o)) return true;
            }
            return false;
        }

        static bool SampleIn(Opening a, Opening b)
        {
            foreach (var quad in new[] { a.Slope, a.Flat })
            {
                var c = quad.Corners();
                for (int u = 0; u <= 4; u++)
                    for (int v = 0; v <= 4; v++)
                    {
                        Vector2 p = Vector2.Lerp(Vector2.Lerp(c[0], c[1], u / 4f), Vector2.Lerp(c[3], c[2], u / 4f), v / 4f);
                        if (b.Contains(p, -0.05f)) return true;
                    }
            }
            return false;
        }

        bool InZebraOpening(Vector2 p, float grow)
        {
            foreach (var o in Openings) if (o.Zebra && o.Contains(p, grow)) return true;
            return false;
        }

        /// <summary>
        /// Every place a footpath meets a road is a crossing point, wrong way or right, and
        /// gets exactly one ramp and one pad:
        ///  - Near side: where a footpath stops at a kerb, a ramp continues it to the asphalt -
        ///    unless a zebra's wider ramp is already there.
        ///  - Far side: follow the footpath's line straight across; if it lands on a nature
        ///    strip with a footpath behind it (the head of a T, the outside of a corner), a ramp
        ///    square to that kerb, so the crossing lands on paving, not a kerb and a lawn.
        /// Real streets put ramps in pairs like this.
        /// </summary>
        void BuildOpenings()
        {
            var ends = FootpathEnds();
            var crossing = new List<FootEnd>();
            foreach (var e in ends)
            {
                if (InZebraOpening(e.P, 0.3f) || InZebraOpening(e.P + e.Dir * 0.3f, 0.3f)) { crossing.Add(e); continue; }
                if (EndOpening(e)) crossing.Add(e);
            }

            foreach (var e in crossing)
            {
                Vector2 p = e.P, dir = e.Dir;
                float t = 0.05f;
                while (t < 20f && InOther(_carriageway, -1, p + dir * t, 0f)) t += 0.05f;
                if (t >= 20f || t < 1f) continue;
                Vector2 far = p + dir * t;

                // Already paving on the far side?
                if (InOther(_footBand, -1, far + dir * 0.3f, 0f) || InOpening(far + dir * 0.3f, 0.2f)) continue;
                // Only open onto a nature strip that has an actual footpath behind it.
                float reach = 0f;
                for (float u = 0.2f; u < FullRouteLayout.NatureStrip + 1.0f; u += 0.1f)
                    if (OnBuiltFootpath(far + dir * u, -0.2f)) { reach = u; break; }
                if (reach <= 0f) continue;
                if (!InAnyReserve(far + dir * 0.5f)) continue;

                int k, seg;
                if (!CarriagewayAt(far - dir * 0.1f, out k, out seg)) continue;
                var st = Streets[k];
                Vector2 L = st.Left(seg);
                int side = Vector2.Dot(far - st.Pts[seg], L) >= 0f ? 1 : -1;
                if (Vector2.Dot(dir, L * side) < 0.35f) continue;
                // Don't stack a second ramp on (or half over) one that is already there -
                // at an angled junction the near-side ramp of another footpath often is.
                int before = Openings.Count;
                RectOpening(st, seg, side, far, FullRouteLayout.Footpath, false);
                if (OverlapsEarlier(before))
                {
                    Openings.RemoveAt(before);
                    Tactiles.RemoveAt(Tactiles.Count - 1);
                }
            }
        }

        /// <summary>
        /// Footpath ends that stop at a road (not at a street end or a court): the point on the
        /// footpath centre line at the kerb, and the direction across the road.
        /// </summary>
        List<FootEnd> FootpathEnds()
        {
            var list = new List<FootEnd>();
            foreach (var b in Bands)
            {
                if (b.Kind != BandKind.Footpath) continue;
                var st = Streets[b.Street];
                float mid = (b.Off0 + b.Off1) * 0.5f;
                for (int e = 0; e < 2; e++)
                {
                    float s = e == 0 ? b.S0 : b.S1;
                    if (s < 0.05f || s > st.Length - 0.05f) continue;
                    // An end sitting exactly on a bend belongs to the segment the band runs
                    // along, not the next one - Offset() would put it on the wrong side line.
                    int seg = e == 0 ? st.Seg(s + 0.01f) : st.Seg(s - 0.01f);
                    Vector2 p = st.Pts[seg] + st.Dir(seg) * (s - st.Cum[seg]) + st.Left(seg) * mid;
                    if (OwnCourtDist(st, p) < FullRouteLayout.CourtFootMid + 1.5f) continue;
                    Vector2 dir = st.Dir(seg) * (e == 0 ? -1f : 1f);
                    // Only ends that actually stop at a road edge.
                    if (!InOther(_carriageway, -1, p + dir * 0.3f, 0f)) continue;
                    list.Add(new FootEnd { P = p, Dir = dir, Street = st.Index });
                }
            }
            return list;
        }

        // ------------------------------------------------------------------- fences
        FenceStyle Classify(RouteStreet st, Vector2 p)
        {
            if (InAnyReserve(p, st.Index)) return FenceStyle.None;
            if (OwnCourtDist(st, p) < FullRouteLayout.CourtReserve) return FenceStyle.None;
            if (Forecourt.Contains(p, 0.6f) || ShopBuilding.Contains(p, 0.6f)) return FenceStyle.None;
            if (Park.Contains(p, st.IsRoute ? 1.5f : 0.6f)) return st.IsRoute ? FenceStyle.None : FenceStyle.ParkLow;
            if (School.Contains(p, 0.6f)) return FenceStyle.School;
            if (SpecialLot.Contains(p, 0.6f)) return FenceStyle.SpecialPicket;
            if (OnInnerCornerOfBend(st, p)) return FenceStyle.Screen;
            return FenceStyle.Front;
        }

        /// <summary>
        /// True on the inside of a dead end's bend, within ScreenReach of the bend. That corner
        /// lot is too small for a house, so without something tall there the court is in plain
        /// view from the junction. A 2.2 m clipped hedge - common on a suburban corner - blocks
        /// eye level, and above it the participant sees only more rooftops, not an end.
        /// </summary>
        bool OnInnerCornerOfBend(RouteStreet st, Vector2 p)
        {
            // The first bend only. An L-corner end (v5) adds a second, 90-degree one further
            // on; that corner needs no screen, as the arm round it is out of sight already.
            if (st.IsRoute || st == BusRoad || st.Pts.Length < 3) return false;
            Vector2 d0 = (st.Pts[1] - st.Pts[0]).normalized, d1 = (st.Pts[2] - st.Pts[1]).normalized;
            float turn = d0.x * d1.y - d0.y * d1.x;              // + = bends left
            if (Mathf.Abs(turn) < 0.1f) return false;
            Vector2 b = st.Pts[1], rel = p - b;
            if (rel.magnitude > ScreenReach) return false;
            float sideOfFirst = d0.x * rel.y - d0.y * rel.x;     // + = left of the first leg
            float sideOfSecond = d1.x * rel.y - d1.y * rel.x;
            return turn > 0f ? (sideOfFirst > 0f || sideOfSecond > 0f) && Vector2.Dot(rel, d0) > -ScreenReach
                             : (sideOfFirst < 0f || sideOfSecond < 0f) && Vector2.Dot(rel, d0) > -ScreenReach;
        }

        const float ScreenReach = 14f;

        void BuildFences()
        {
            float off = HR + 0.05f;
            var styles = new[] { FenceStyle.Front, FenceStyle.School, FenceStyle.SpecialPicket, FenceStyle.ParkLow, FenceStyle.Screen };

            foreach (var st in Streets)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    foreach (var style in styles)
                    {
                        foreach (var r in Runs(st, side * off, (s, p) => !st.InOwnOtherSegment(s, p, HR) && Classify(st, p) == style, 0.1f, 1.0f))
                            Fences.Add(new FenceLine(st.Edge(r.x, r.y, side * off), style, st.Name));
                    }
                }

                // Close the far end of every side street (beyond its soft end, so visual only
                // in practice, but it stops the street reading as running off into nothing).
                if (st.OpenEnd) EndCaps.Add(new Across { Street = st.Index, S = st.Length, Name = st.Name + "_End" });
                if (st.OpenStart) EndCaps.Add(new Across { Street = st.Index, S = 0f, Name = st.Name + "_Start" });
            }

            // Back edges of the park, the school and the shopping forecourt: whatever part of
            // their outline is not already a street frontage.
            AddZoneOutline(Park, FenceStyle.ParkBack, "Park");
            AddZoneOutline(School, FenceStyle.School, "School");
            AddZoneOutline(Forecourt, FenceStyle.Hedge, "Forecourt", true);

            // Front fences around each court, broken where the street comes in.
            foreach (var c in Courts)
            {
                float r = FullRouteLayout.CourtReserve + 0.05f;
                float start = Mathf.Atan2(-c.InDir.y, -c.InDir.x);
                foreach (var arc in Arcs(c.C, r, start, p => !InAnyReserve(p, -1, 0f)))
                {
                    var pts = new List<Vector2>();
                    int steps = Mathf.Max(2, Mathf.CeilToInt((arc.y - arc.x) / (3f * Mathf.Deg2Rad)));
                    for (int i = 0; i <= steps; i++)
                    {
                        float a = Mathf.Lerp(arc.x, arc.y, i / (float)steps);
                        pts.Add(c.C + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
                    }
                    Fences.Add(new FenceLine(pts, FenceStyle.Front, Streets[c.Street].Name + "_Court"));
                }
            }

            CloseCornerGaps();
        }

        void AddZoneOutline(Obb zone, FenceStyle style, string owner, bool isForecourt = false)
        {
            var c = zone.Corners();
            for (int i = 0; i < 4; i++)
            {
                Vector2 a = c[i], b = c[(i + 1) % 4];
                float len = Vector2.Distance(a, b);
                Vector2 d = (b - a) / len;
                int steps = Mathf.Max(2, Mathf.CeilToInt(len / 0.1f));
                float runStart = -1f;
                for (int j = 0; j <= steps; j++)
                {
                    float t = len * j / steps;
                    Vector2 p = a + d * t;
                    bool keep = !InAnyReserve(p, -1, 0.05f) && !ShopBuilding.Contains(p, 0.05f)
                                && (isForecourt || !Forecourt.Contains(p, 0.3f));
                    if (keep && runStart < 0f) runStart = t;
                    if ((!keep || j == steps) && runStart >= 0f)
                    {
                        float runEnd = keep ? t : len * (j - 1) / steps;
                        if (runEnd - runStart > 0.3f)
                            Fences.Add(new FenceLine(new List<Vector2> { a + d * runStart, a + d * runEnd }, style, owner));
                        runStart = -1f;
                    }
                }
            }
        }

        /// <summary>
        /// Where two fence runs from different streets stop just short of each other at a
        /// corner, join them. Sampling leaves gaps of a few centimetres to a metre or so, and
        /// a gap that size is enough to lose a participant into someone's front yard.
        /// </summary>
        void CloseCornerGaps()
        {
            var ends = new List<KeyValuePair<Vector2, int>>();
            for (int i = 0; i < Fences.Count; i++)
            {
                if (Fences[i].Style == FenceStyle.None) continue;
                ends.Add(new KeyValuePair<Vector2, int>(Fences[i].Pts[0], i));
                ends.Add(new KeyValuePair<Vector2, int>(Fences[i].Pts[Fences[i].Pts.Count - 1], i));
            }

            var used = new bool[ends.Count];
            for (int i = 0; i < ends.Count; i++)
            {
                if (used[i]) continue;
                int best = -1; float bestD = 2.5f;
                for (int j = 0; j < ends.Count; j++)
                {
                    if (j == i || used[j] || ends[j].Value == ends[i].Value) continue;
                    float d = Vector2.Distance(ends[i].Key, ends[j].Key);
                    if (d < bestD) { bestD = d; best = j; }
                }
                if (best < 0) continue;
                used[i] = used[best] = true;
                if (bestD < 0.02f) continue;
                var style = Fences[ends[i].Value].Style;
                Fences.Add(new FenceLine(new List<Vector2> { ends[i].Key, ends[best].Key }, style, "CornerJoin"));
            }
        }

        // --------------------------------------------------------------- landmarks
        List<Vector2> RoadDirsAt(int node)
        {
            var n = FullRouteLayout.RouteNodes;
            var dirs = new List<Vector2>();
            if (node >= 2) dirs.Add((n[node - 1] - n[node]).normalized);
            if (node >= 1 && node < n.Length - 1) dirs.Add((n[node + 1] - n[node]).normalized);
            foreach (var b in FullRouteLayout.Branches) if (b.Node == node) dirs.Add(b.Dir);
            return dirs;
        }

        /// <summary>
        /// Stars on the drawing become heritage lamps on the nature strip at that corner. A star
        /// in a quadrant between two streets goes a little way along the nearer one; a star
        /// opposite a T goes on the far nature strip, facing straight down the side street.
        /// </summary>
        void BuildLandmarkLamps()
        {
            var n = FullRouteLayout.RouteNodes;
            foreach (var star in FullRouteLayout.LandmarkLampStars)
            {
                Vector2 node = n[star.Node];
                var dirs = RoadDirsAt(star.Node);

                Vector2 best = dirs[0]; float bestDot = -2f;
                foreach (var d in dirs) { float k = Vector2.Dot(d, star.Dir); if (k > bestDot) { bestDot = k; best = d; } }

                Vector2 pos;
                if (bestDot >= 0.45f)
                {
                    Vector2 perp = new Vector2(-best.y, best.x);
                    if (Vector2.Dot(perp, star.Dir) < 0f) perp = -perp;
                    pos = node + best * (HR + 1.8f) + perp * FullRouteLayout.NatureCentre;
                }
                else
                {
                    Vector2 bestN = Vector2.zero; float bn = -2f;
                    foreach (var d in dirs)
                    {
                        foreach (var nn in new[] { new Vector2(-d.y, d.x), new Vector2(d.y, -d.x) })
                        {
                            float k = Vector2.Dot(nn, star.Dir);
                            if (k > bn) { bn = k; bestN = nn; }
                        }
                    }
                    pos = node + bestN * FullRouteLayout.NatureCentre;
                    best = new Vector2(-bestN.y, bestN.x);   // along the nature strip
                }

                // Keep it off ramps and footpaths: slide along the nature strip if needed, and
                // failing that, move to the nature strip of the other street at this corner.
                Vector2 placed;
                if (!ClearSpotAlong(pos, best, out placed))
                {
                    bool found = false;
                    var byDot = new List<Vector2>(dirs);
                    byDot.Sort((x, y) => Vector2.Dot(y, star.Dir).CompareTo(Vector2.Dot(x, star.Dir)));
                    foreach (var alt in byDot)
                    {
                        if (Vector2.Distance(alt, best) < 0.01f) continue;
                        Vector2 perp = new Vector2(-alt.y, alt.x);
                        if (Vector2.Dot(perp, star.Dir) < 0f) perp = -perp;
                        Vector2 p2 = node + alt * (HR + 1.8f) + perp * FullRouteLayout.NatureCentre;
                        if (ClearSpotAlong(p2, alt, out placed)) { found = true; break; }
                    }
                    if (!found)
                    {
                        placed = pos;
                        Warnings.Add("Landmark lamp at N" + star.Node + " could not be kept clear of paving - check it in the Scene view.");
                    }
                }
                LandmarkLamps.Add(placed);
            }
        }

        bool ClearSpotAlong(Vector2 pos, Vector2 along, out Vector2 result)
        {
            foreach (float shift in new[] { 0f, 1.5f, -1.5f, 3f, -3f, 4.5f, -4.5f, 6f, -6f })
            {
                Vector2 q = pos + along * shift;
                if (!OnWalkway(q, 0.35f)) { result = q; return true; }
            }
            result = pos;
            return false;
        }

        void BuildBusStop()
        {
            float s = BusStopS;
            Vector2 d = BusRoad.Dir(BusRoad.Seg(s)), left = BusRoad.Left(BusRoad.Seg(s));
            // Shelter on the nature strip, back to the footpath, open to the kerb. Concrete pad
            // under it so it is not standing in the lawn.
            ShelterPos = BusRoad.Offset(s, FullRouteLayout.NatureCentre + 0.05f);
            ShelterFacing = -left;
            Bands.Add(new Band(BusRoad.Index, s - 3.2f, s + 3.2f, FullRouteLayout.KerbEdge, FullRouteLayout.FootInner, BandKind.Pad));
            BusStop = new Obb(ShelterPos, d, 4f, 2f);

            // Flag sign a few metres toward the junction, so it is in view from the spawn.
            BusFlagPos = BusRoad.Offset(s - 3.0f, FullRouteLayout.KerbEdge + 0.35f);
            BusFlagDir = d;

            Spawn = BusRoad.Offset(s + 0.6f, FullRouteLayout.FootCentre);
            SpawnFacing = -d;   // toward the junction - the way the participant has to go
        }

        // ------------------------------------------------------------ street furniture
        bool NearAny(Vector2 p, List<Vector2> list, float r)
        {
            foreach (var q in list) if (Vector2.Distance(p, q) < r) return true;
            return false;
        }

        bool NearClosure(RouteStreet st, float s, float r)
        {
            foreach (float c in st.Closures) if (Mathf.Abs(s - c) < r) return true;
            return false;
        }

        void BuildLamps()
        {
            foreach (var st in Streets)
            {
                int k = 0;
                for (float s = 9f; s < st.Length - 3f; s += 25f, k++)
                {
                    int side = (k + st.Index) % 2 == 0 ? 1 : -1;
                    Vector2 p = st.Offset(s, side * (FullRouteLayout.KerbEdge + 0.45f));
                    if (InAnyReserve(p, st.Index, 1.5f) || OnWalkway(p, 0.3f)) continue;
                    if (NearAny(p, LandmarkLamps, 7f)) continue;
                    if (Vector2.Distance(p, ShelterPos) < 5f || Vector2.Distance(p, BusFlagPos) < 3f) continue;
                    if (NearClosure(st, s, 3f)) continue;
                    Lamps.Add(new LampSpot { Pos = p, Arm = -st.Left(st.Seg(s)) * side });
                }
            }
        }

        void BuildTrees()
        {
            var lampPos = new List<Vector2>();
            foreach (var l in Lamps) lampPos.Add(l.Pos);
            if (_refill != null) Trees.AddRange(_keptTrees);

            foreach (var st in Streets)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    for (float s = R(3f, 7f); s < st.Length - 3f; s += R(9.5f, 12.5f))
                    {
                        Vector2 p = st.Offset(s, side * FullRouteLayout.NatureCentre);
                        if (InAnyReserve(p, st.Index, 2.0f) || OnWalkway(p, 0.4f)) continue;
                        if (NearAny(p, lampPos, 3.2f) || NearAny(p, LandmarkLamps, 5f)) continue;
                        bool nearPost = false;
                        foreach (var cp in CrossingPosts) if (Vector2.Distance(cp.Pos, p) < 3f) nearPost = true;
                        if (nearPost) continue;
                        if (Vector2.Distance(p, ShelterPos) < 6f || Vector2.Distance(p, BusFlagPos) < 3f) continue;
                        if (NearClosure(st, s, 3f)) continue;
                        if (Forecourt.Contains(p, 2f)) continue;
                        if (_rng.NextDouble() < 0.22) continue;   // the odd missing tree
                        if (!PlacingOnStreet(p) || (_refill != null && NearAny(p, _keptTrees, 6f))) continue;
                        Trees.Add(p);
                    }
                }
            }
        }

        /// <summary>
        /// Parked cars, static - nothing in this scene moves, for the same reasons as the
        /// tutorial. Kept away from junctions so no car hides the view into a side street.
        /// </summary>
        bool InOwnCarriageway(RouteStreet st, Vector2 p)
        {
            foreach (var b in _carriageway[st.Index]) if (b.Contains(p, -0.2f)) return true;
            return false;
        }

        void BuildCars()
        {
            if (_refill != null) Cars.AddRange(_keptCars);
            foreach (var st in Streets)
            {
                for (float s = 10f; s < st.Length - 6f; s += 13f)
                {
                    for (int side = -1; side <= 1; side += 2)
                    {
                        if (_rng.NextDouble() > 0.45) continue;
                        float ss = s + R(-1.5f, 1.5f);
                        Vector2 p = st.Offset(ss, side * (FullRouteLayout.HalfCarriageway - 1.05f));
                        if (InAnyReserve(p, st.Index, 4f)) continue;
                        if (NearClosure(st, ss, 6f)) continue;
                        if (st == BusRoad && Mathf.Abs(ss - BusStopS) < 14f) continue;
                        bool nearZebra = false;
                        foreach (var z in Zebras) if (z.Street == st.Index && Mathf.Abs(ss - z.S) < 10f) nearZebra = true;
                        if (nearZebra) continue;
                        if (OwnCourtDist(st, p) < FullRouteLayout.CourtReserve + 3f) continue;
                        if (st.IsRoute && ss > st.Length - 14f) continue;
                        Vector2 d = st.Dir(st.Seg(ss));
                        if (!CarFits(st, p, d, 2.3f)) continue;
                        if (!PlacingOnStreet(p) || NearKeptCar(p)) continue;
                        // Australia drives on the left, and Road Rule 208 says a parallel-parked
                        // car faces the way traffic on its side of the road travels. 'side' +1 is
                        // the left of the street's direction d, so a car on that kerb faces d and
                        // one on the other kerb faces back along -d. Either way the kerb is on the
                        // car's left.
                        // Long is tested without drawing from _rng, so the rest of the layout
                        // (houses, gardens) comes out exactly as before.
                        Cars.Add(new CarSpot { Pos = p, Dir = side > 0 ? d : -d, Variant = _rng.Next(0, 3),
                                               Long = CarFits(st, p, d, 2.68f) });
                    }
                }
            }
        }

        bool NearKeptCar(Vector2 p)
        {
            if (_refill == null) return false;
            foreach (var c in _keptCars) if (Vector2.Distance(c.Pos, p) < 6.5f) return true;
            return false;
        }

        /// <summary>
        /// A car outline of half length 'halfLen' fits at p: the whole outline, every tenth of
        /// each side, plus the centre - angled ramps at oblique crossings can poke a corner
        /// into the lane.
        /// </summary>
        bool CarFits(RouteStreet st, Vector2 p, Vector2 d, float halfLen)
        {
            var body = new Obb(p, d, halfLen, 0.95f);
            var probes = new List<Vector2> { p };
            var cs = body.Corners();
            for (int ci = 0; ci < 4; ci++)
                for (float f = 0f; f < 1f; f += 0.1f)
                    probes.Add(Vector2.Lerp(cs[ci], cs[(ci + 1) % 4], f));
            foreach (var corner in probes)
                if (InOther(_footBand, -1, corner, 0.1f) || InAnyCarriageway(corner, st.Index, 0.5f)
                    || !InOwnCarriageway(st, corner) || InOpening(corner, 0.3f)
                    || InCourt(corner, st.Index, FullRouteLayout.CourtReserve)) return false;
            return true;
        }

        // ------------------------------------------------------------------- houses
        bool HouseFits(Obb fp)
        {
            foreach (var list in _reserve) foreach (var b in list) if (fp.Intersects(b, 0.2f)) return false;
            foreach (var c in Courts) if (fp.Distance(c.C) < FullRouteLayout.CourtReserve + 0.2f) return false;
            Obb[] zones = { Park, School, SpecialLot, Forecourt, ShopBuilding, new Obb(ShopSignPos, ShopFacing, 1.5f, 2f) };
            foreach (var z in zones) if (fp.Intersects(z, 1.2f)) return false;
            foreach (var h in Houses) if (fp.Intersects(h.Footprint, 0.3f)) return false;
            return true;
        }

        static readonly float[] HouseWidths = { 12.5f, 11f, 9.5f, 8f, 7f };

        bool TryHouse(Vector2 centreLine, Vector2 along, Vector2 away, float width, out HouseSpot spot)
        {
            spot = new HouseSpot();
            for (int attempt = 0; attempt < 2; attempt++)
            {
                float depth = attempt == 0 ? R(8f, 10f) : R(7f, 8f);
                float setback = attempt == 0 ? R(4.5f, 6f) : R(3.5f, 4.5f);
                bool garage = width >= 11f && _rng.NextDouble() < 0.6;
                float body = garage ? width - 3.2f : width;
                Vector2 frontage = centreLine + away * (HR + setback);
                var fp = new Obb(frontage + away * (depth * 0.5f), along, width * 0.5f + 0.2f, depth * 0.5f + 0.2f);
                if (!HouseFits(fp) || !Placing(fp.C)) continue;
                int gside = garage ? (_rng.NextDouble() < 0.5 ? 1 : -1) : 0;
                // The body sits off-centre so body + garage together fill the footprint.
                Vector2 shift = along * (gside * -1.6f);
                spot = new HouseSpot
                {
                    Frontage = frontage + (garage ? shift : Vector2.zero), Facing = -away, Footprint = fp,
                    Width = body, Depth = depth, GarageSide = gside, Seed = _rng.Next(),
                };
                return true;
            }
            return false;
        }

        void BuildHouses()
        {
            // v5 refill pass: the houses kept from the pre-v5 layout go in first, so the new
            // ones around the T and L ends fit between them.
            if (_refill != null)
                foreach (var h in _keptHouses)
                    if (HouseFits(h.Footprint)) Houses.Add(h);

            // Route first, so where two streets compete for a corner the route side wins.
            foreach (var st in Streets)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    float s = R(0.5f, 2.5f);
                    while (s < st.Length - 3f)
                    {
                        bool placed = false;
                        foreach (float w in HouseWidths)
                        {
                            float c = s + w * 0.5f;
                            if (c > st.Length - 1f) continue;
                            int seg = st.Seg(c);
                            Vector2 away = st.Left(seg) * side;
                            HouseSpot spot;
                            if (!TryHouse(st.Point(c), st.Dir(seg), away, w, out spot)) continue;
                            Houses.Add(spot);
                            s += w + R(1.5f, 3.5f);
                            placed = true;
                            break;
                        }
                        if (!placed) s += 1.5f;
                    }
                }

                // A house across the far end of each side street.
                if (st.CourtIndex >= 0) HousesAroundCourt(Courts[st.CourtIndex]);

                for (int e = 0; e < 2; e++)
                {
                    if (e == 0 ? !st.OpenEnd : !st.OpenStart) continue;
                    Vector2 d = e == 0 ? st.Dir(st.SegmentCount - 1) : -st.Dir(0);
                    Vector2 tip = e == 0 ? st.Pts[st.Pts.Length - 1] : st.Pts[0];
                    Vector2 end = tip - d * HR;   // TryHouse adds HR back on
                    foreach (float w in HouseWidths)
                    {
                        HouseSpot spot;
                        if (TryHouse(end, new Vector2(-d.y, d.x), d, w, out spot)) { Houses.Add(spot); break; }
                    }
                }
            }
        }

        /// <summary>Houses facing into a court, walked round the circle from beside the entry.</summary>
        void HousesAroundCourt(Court c)
        {
            float r = FullRouteLayout.CourtReserve;
            float a = Mathf.Atan2(-c.InDir.y, -c.InDir.x) + 0.35f;
            float end = a + 2f * Mathf.PI - 0.7f;
            while (a < end)
            {
                Vector2 away = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 along = new Vector2(-away.y, away.x);
                bool placed = false;
                foreach (float w in HouseWidths)
                {
                    HouseSpot spot;
                    // TryHouse measures its setback from HR past the point it is given.
                    if (!TryHouse(c.C + away * (r - HR), along, away, w, out spot)) continue;
                    Houses.Add(spot);
                    a += (w + 2.5f) / (r + 5f);
                    placed = true;
                    break;
                }
                if (!placed) a += 0.08f;
            }
        }

        // ----------------------------------------------------------------- triggers
        void BuildTriggers()
        {
            foreach (var st in Streets)
            {
                for (int i = 0; i < st.Triggers.Count; i++)
                {
                    float t = st.Triggers[i];
                    string name = "WrongTurn_" + st.Name + (st.Triggers.Count > 1 ? (t < BusStopS - 12f ? "_South" : "_North") : "");
                    WrongTurns.Add(new NamedBox { Name = name, Box = new Obb(st.Point(t), st.Dir(st.Seg(t)), 0.6f, HR) });
                    Branches.Add(MakeBranch(st, name, t));
                }
                foreach (float c in st.Closures)
                    Closures.Add(new Across { Street = st.Index, S = c, Name = st.Name });
            }

            var n = FullRouteLayout.RouteNodes;
            Checkpoints.Add(new NamedBox { Name = "CP_Start", Box = new Obb(BusRoad.Point(BusStopS), BusRoad.Dir(BusRoad.Seg(BusStopS)), 3.5f, HR) });
            foreach (int i in FullRouteLayout.DecisionNodes)
            {
                Vector2 d = (n[i + 1] - n[i]).normalized;
                Checkpoints.Add(new NamedBox { Name = "CP_Decision_N" + i, Box = new Obb(n[i], d, HR, HR) });
            }
            Checkpoints.Add(new NamedBox { Name = "CP_EndZone", Box = Forecourt });
        }

        /// <summary>
        /// Where a wrong-turn trigger's street leaves the route, and which way is into it. Side
        /// streets start at their route node. The bus road runs through: its south arm leaves at
        /// N1, and its north arm is the wrong way from the bus stop itself.
        /// </summary>
        Branch MakeBranch(RouteStreet st, string name, float triggerS)
        {
            var n = FullRouteLayout.RouteNodes;
            Vector2 trigger = st.Point(triggerS);
            if (st == BusRoad)
            {
                bool south = triggerS < BusStopS - 12f;
                if (south)
                    return new Branch { Name = name, Node = 1, Mouth = st.Pts[1], Into = -st.Dir(0), Trigger = trigger };
                return new Branch { Name = name, Node = 0, Mouth = st.Point(BusStopS), Into = st.Dir(st.Seg(BusStopS)), Trigger = trigger };
            }
            int node = 0; float best = float.MaxValue;
            for (int i = 0; i < n.Length; i++)
            {
                float d = Vector2.Distance(n[i], st.Pts[0]);
                if (d < best) { best = d; node = i; }
            }
            return new Branch { Name = name, Node = node, Mouth = st.Pts[0], Into = st.Dir(0), Trigger = trigger };
        }

        /// <summary>
        /// Planting in the private land behind the front fences: shrubs along the fence line
        /// and in front yards, and the odd tree in back gardens. Short blocks leave lots too
        /// small for a house, and bare lawn there reads as a vacant block.
        /// </summary>
        void BuildGardens()
        {
            Obb[] zones = { Park, School, SpecialLot, Forecourt, ShopBuilding };
            if (_refill != null)
            {
                // Kept planting, unless a new street or a new house now covers it.
                foreach (var g in _keptShrubs)
                    if (GardenClear(new Vector2(g.x, g.y), 0.9f)) GardenShrubs.Add(g);
                foreach (var t in _keptGardenTrees)
                    if (GardenClear(t, 4f)) GardenTrees.Add(t);
            }
            for (float x = BoundsMin.x + 20f; x < BoundsMax.x - 20f; x += 3.2f)
            {
                for (float z = BoundsMin.y + 20f; z < BoundsMax.y - 20f; z += 3.2f)
                {
                    Vector2 p = new Vector2(x + R(-1.2f, 1.2f), z + R(-1.2f, 1.2f));
                    if (InAnyReserve(p, -1, 0.9f)) continue;
                    if (!InAnyReserve(p, -1, 24f)) continue;          // only near streets
                    if (!Placing(p) || (_refill != null && NearKeptPlanting(p))) continue;
                    bool blocked = false;
                    foreach (var zn in zones) if (zn.Contains(p, 1.2f)) { blocked = true; break; }
                    if (blocked) continue;
                    foreach (var h in Houses) if (h.Footprint.Contains(p, 1.0f)) { blocked = true; break; }
                    if (blocked) continue;

                    bool nearFence = InAnyReserve(p, -1, 2.4f);
                    double roll = _rng.NextDouble();
                    if (nearFence ? roll < 0.45 : roll < 0.10)
                        GardenShrubs.Add(new Vector3(p.x, p.y, R(0.7f, 1.5f)));
                    else if (!nearFence && roll > 0.965 && !InAnyReserve(p, -1, 4f))
                        GardenTrees.Add(p);
                }
            }
        }

        bool GardenClear(Vector2 p, float reserveGap)
        {
            if (InAnyReserve(p, -1, reserveGap)) return false;
            foreach (var h in Houses) if (h.Footprint.Contains(p, 1.0f)) return false;
            return true;
        }

        bool NearKeptPlanting(Vector2 p)
        {
            foreach (var g in _keptShrubs) if ((new Vector2(g.x, g.y) - p).sqrMagnitude < 1.0f) return true;
            foreach (var t in _keptGardenTrees) if ((t - p).sqrMagnitude < 9f) return true;
            return false;
        }

        void BuildBounds()
        {
            Vector2 mn = new Vector2(float.MaxValue, float.MaxValue), mx = -mn;
            foreach (var st in Streets)
                foreach (var p in st.Pts) { mn = Vector2.Min(mn, p); mx = Vector2.Max(mx, p); }
            foreach (var c in ShopBuilding.Corners()) { mn = Vector2.Min(mn, c); mx = Vector2.Max(mx, c); }
            BoundsMin = mn - new Vector2(40f, 40f);
            BoundsMax = mx + new Vector2(40f, 40f);
        }
    }
}
