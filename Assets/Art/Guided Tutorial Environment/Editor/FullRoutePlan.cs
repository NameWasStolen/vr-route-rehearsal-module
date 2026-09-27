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

        public static readonly BranchDef[] Branches =
        {
            new BranchDef("BusRoad",  1, new Vector2( 0.5039f,  0.8638f),  46.0f,  17.0f,  27.0f),
            new BranchDef("SouthRoad",  1, new Vector2( 0.0000f, -1.0000f),  42.0f,   8.6f,  20.0f),
            new BranchDef("HouseCross_NE",  2, new Vector2( 0.5055f,  0.8628f),  42.0f,   8.6f,  20.0f),
            new BranchDef("HouseCross_SW",  2, new Vector2(-0.4672f, -0.8841f),  42.0f,   8.6f,  20.0f),
            new BranchDef("Cross4_N",  4, new Vector2( 0.0000f,  1.0000f),  27.1f,   8.6f,  20.0f),
            new BranchDef("Cross4_S",  4, new Vector2( 0.0000f, -1.0000f),  42.0f,   8.6f,  20.0f),
            new BranchDef("Side5_S",  5, new Vector2( 0.0000f, -1.0000f),  42.0f,   8.6f,  20.0f),
            new BranchDef("Corner6_S",  6, new Vector2( 0.0000f, -1.0000f),  42.0f,   8.6f,  20.0f),
            new BranchDef("Side7_W",  7, new Vector2(-1.0000f,  0.0000f),  42.0f,   8.6f,  20.0f),
            new BranchDef("Side8_E",  8, new Vector2( 1.0000f,  0.0000f),  32.4f,   8.6f,  20.0f),
            new BranchDef("Side9_W",  9, new Vector2(-1.0000f,  0.0000f),  16.4f,   8.6f,  12.4f),
            new BranchDef("Side10_E", 10, new Vector2( 1.0000f,  0.0000f),  42.0f,   8.6f,  20.0f),
            new BranchDef("Side11_N", 11, new Vector2( 0.0000f,  1.0000f),  42.0f,   8.6f,  20.0f),
            new BranchDef("Cross12_N", 12, new Vector2( 0.0000f,  1.0000f),  42.0f,   8.6f,  20.0f),
            new BranchDef("Cross12_S", 12, new Vector2( 0.0000f, -1.0000f),  15.4f,   8.6f,  11.4f),
            new BranchDef("Side14_SW", 14, new Vector2(-0.2905f, -0.9569f),  30.0f,   8.6f,  20.0f),
            new BranchDef("Cross15_NNE", 15, new Vector2( 0.2510f,  0.9680f),  42.0f,   8.6f,  20.0f),
            new BranchDef("Cross15_WNW", 15, new Vector2(-0.9675f,  0.2528f),  42.0f,   8.6f,  20.0f),
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
    public enum FenceStyle { None, Front, School, SpecialPicket, ParkLow, ParkBack, Hedge }

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
    public struct CarSpot { public Vector2 Pos, Dir; public int Variant; }
    public struct Across { public int Street; public float S; public string Name; }
    public struct NamedBox { public string Name; public Obb Box; }

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
        public Vector2 Spawn, SpawnFacing;
        public Vector2 ShelterPos, ShelterFacing, BusFlagPos, BusFlagDir;
        public Vector2 ShopSignPos, ShopSignFacing;
        public Vector2 BoundsMin, BoundsMax;

        readonly System.Random _rng = new System.Random(FullRouteLayout.Seed);
        float R(float a, float b) { return a + (float)_rng.NextDouble() * (b - a); }

        const float HR = FullRouteLayout.HalfReserve;

        public FullRoutePlan()
        {
            BuildStreets();
            BuildVolumes();
            BuildZones();
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

            for (int i = 0; i < br.Length; i++)
            {
                if (i == busI || i == southI) continue;
                Vector2 a = n[br[i].Node];
                var st = new RouteStreet(br[i].Name, new[] { a, a + br[i].Dir * br[i].Length })
                {
                    Index = Streets.Count, BranchIndex = i, OpenEnd = true,
                };
                st.Triggers.Add(br[i].TriggerAt);
                st.Closures.Add(br[i].ClosureAt);
                Streets.Add(st);
            }
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

        public bool InAnyCarriageway(Vector2 p, int except = -1, float grow = 0f) { return InOther(_carriageway, except, p, grow); }
        public bool InAnyReserve(Vector2 p, int except = -1, float grow = 0f) { return InOther(_reserve, except, p, grow); }
        public float Distance(Vector2 a, Vector2 b) { return Vector2.Distance(a, b); }

        /// <summary>On (or within r of) any footpath or carriageway, of any street.</summary>
        public bool OnWalkway(Vector2 p, float r)
        {
            if (InOther(_carriageway, -1, p, r)) return true;
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
            foreach (var st in Streets)
            {
                int k = st.Index;
                Bands.Add(new Band(k, 0f, st.Length, -cw, cw, BandKind.Road));

                for (int side = -1; side <= 1; side += 2)
                {
                    Bands.Add(new Band(k, 0f, st.Length, side * FullRouteLayout.KerbEdge, side * FullRouteLayout.FootInner, BandKind.Nature));

                    // Footpath: stops at every other street's kerb line.
                    foreach (var r in Runs(st, side * FullRouteLayout.FootCentre,
                                           (s, p) => !InAnyCarriageway(p, k) && !st.InOwnOtherSegment(s, p, FullRouteLayout.KerbEdge)))
                        Bands.Add(new Band(k, r.x, r.y, side * FullRouteLayout.FootInner, side * FullRouteLayout.FootOuter, BandKind.Footpath));

                    // Kerb: broken at side-street mouths, and where a footpath crosses (the ramp).
                    foreach (var r in Runs(st, side * (cw + FullRouteLayout.KerbWidth * 0.5f),
                                           (s, p) => !InAnyCarriageway(p, k, 0.02f) && !InOther(_footBand, k, p, 0.05f)
                                                     && !st.InOwnOtherSegment(s, p, FullRouteLayout.KerbEdge)))
                        Bands.Add(new Band(k, r.x, r.y, side * cw, side * FullRouteLayout.KerbEdge, BandKind.Kerb));
                }

                // Broken centre line, 3 m dash every 6 m, kept out of junctions.
                for (float s = 2f; s + 3f < st.Length; s += 6f)
                {
                    bool clear = true;
                    for (float t = s - 1f; t <= s + 4f; t += 0.5f)
                        if (InAnyCarriageway(st.Point(Mathf.Clamp(t, 0f, st.Length)), k, 0.5f)) { clear = false; break; }
                    if (clear) Bands.Add(new Band(k, s, s + 3f, -0.06f, 0.06f, BandKind.Dash));
                }
            }
        }

        // ------------------------------------------------------------------- fences
        FenceStyle Classify(RouteStreet st, Vector2 p)
        {
            if (InAnyReserve(p, st.Index)) return FenceStyle.None;
            if (Forecourt.Contains(p, 0.6f) || ShopBuilding.Contains(p, 0.6f)) return FenceStyle.None;
            if (Park.Contains(p, st.IsRoute ? 1.5f : 0.6f)) return st.IsRoute ? FenceStyle.None : FenceStyle.ParkLow;
            if (School.Contains(p, 0.6f)) return FenceStyle.School;
            if (SpecialLot.Contains(p, 0.6f)) return FenceStyle.SpecialPicket;
            return FenceStyle.Front;
        }

        void BuildFences()
        {
            float off = HR + 0.05f;
            var styles = new[] { FenceStyle.Front, FenceStyle.School, FenceStyle.SpecialPicket, FenceStyle.ParkLow };

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
                }
                LandmarkLamps.Add(pos);
            }
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

            foreach (var st in Streets)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    for (float s = R(3f, 7f); s < st.Length - 3f; s += R(9.5f, 12.5f))
                    {
                        Vector2 p = st.Offset(s, side * FullRouteLayout.NatureCentre);
                        if (InAnyReserve(p, st.Index, 2.0f) || OnWalkway(p, 0.4f)) continue;
                        if (NearAny(p, lampPos, 3.2f) || NearAny(p, LandmarkLamps, 5f)) continue;
                        if (Vector2.Distance(p, ShelterPos) < 6f || Vector2.Distance(p, BusFlagPos) < 3f) continue;
                        if (NearClosure(st, s, 3f)) continue;
                        if (Forecourt.Contains(p, 2f)) continue;
                        if (_rng.NextDouble() < 0.22) continue;   // the odd missing tree
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
                        if (st.IsRoute && ss > st.Length - 14f) continue;
                        Vector2 d = st.Dir(st.Seg(ss));
                        var body = new Obb(p, d, 2.3f, 0.95f);
                        bool clear = true;
                        foreach (var corner in body.Corners())
                            if (InOther(_footBand, -1, corner, 0.1f) || InAnyCarriageway(corner, st.Index, 0.5f)
                                || !InOwnCarriageway(st, corner)) { clear = false; break; }
                        if (!clear) continue;
                        // Australia drives on the left, so a car on the left kerb faces the way
                        // the street runs and one on the right kerb faces back.
                        Cars.Add(new CarSpot { Pos = p, Dir = side > 0 ? d : -d, Variant = _rng.Next(0, 3) });
                    }
                }
            }
        }

        // ------------------------------------------------------------------- houses
        bool HouseFits(Obb fp)
        {
            foreach (var list in _reserve) foreach (var b in list) if (fp.Intersects(b, 0.2f)) return false;
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
                if (!HouseFits(fp)) continue;
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
        /// Planting in the private land behind the front fences: shrubs along the fence line
        /// and in front yards, and the odd tree in back gardens. Short blocks leave lots too
        /// small for a house, and bare lawn there reads as a vacant block.
        /// </summary>
        void BuildGardens()
        {
            Obb[] zones = { Park, School, SpecialLot, Forecourt, ShopBuilding };
            for (float x = BoundsMin.x + 20f; x < BoundsMax.x - 20f; x += 3.2f)
            {
                for (float z = BoundsMin.y + 20f; z < BoundsMax.y - 20f; z += 3.2f)
                {
                    Vector2 p = new Vector2(x + R(-1.2f, 1.2f), z + R(-1.2f, 1.2f));
                    if (InAnyReserve(p, -1, 0.9f)) continue;
                    if (!InAnyReserve(p, -1, 24f)) continue;          // only near streets
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
