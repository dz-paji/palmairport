using System.Collections.Generic;
using UnityEngine;

namespace IslandAirport
{
    /// <summary>Shared layout for both scenes. Geometry transcribed from the named groups in
    /// Docs/level1-figma.svg (Figma VNZIQucVbOKU7wdDRlxZEH, frame 1:2, 2026-09-22).
    /// One design pixel is 0.04 world units. Roads, pavement and taxiways retain separate groups.</summary>
    public static class Level1Map
    {
        public const int StandCount = AirportSimulation.StandCount;
        public const int LayoutVersion = 202609221;
        public static Vector3 FromDesign(float x, float y) { return new Vector3((x-330f)*.04f, 0, (201f-y)*.04f); }
        public static Rect DesignRect(float x,float y,float width,float height)
        {
            Vector3 p=FromDesign(x,y+height);return new Rect(p.x,p.z,width*.04f,height*.04f);
        }
        public static readonly Rect WorldBounds = DesignRect(0,0,874,402);
        public static readonly Vector3 ViewCenter = FromDesign(437,201);
        public static readonly Vector2 WalkMin = new Vector2(-12.7f,-7.8f);
        public static readonly Vector2 WalkMax = new Vector2(7.0f,7.8f);
        public static Vector3 CrewSpawn(int player) { return FromDesign(155+player*35,190); }
        /// <summary>局内唯一相机配置点。overhead=true 保持正交俯视（回放/布局验证）；
        /// 局内镜头（M1）为 v7 式高角度斜视透视：FOV≈34°、俯角≈50°、
        /// 看向偏西的作业区中心（不含跑道），并按屏幕纵横比自适应后移，
        /// 保证横向可视宽 ≥33m（19.5:9 手机与 16:9 都可读）。</summary>
        public static void ConfigureCamera(Camera camera,bool overhead=false)
        {
            if (overhead)
            {
                camera.orthographic=true;
                camera.transform.position=ViewCenter+new Vector3(0,36,-.01f);
                camera.transform.LookAt(ViewCenter);
                camera.orthographicSize=Mathf.Max(10.5f,19.5f/Mathf.Max(.5f,camera.aspect));
                return;
            }
            camera.orthographic=false;
            camera.fieldOfView=34f;
            // 作业区中心：设施列（x≈-10）到机位 A 中心（x≈0.9）之间，偏西取 -4。
            Vector3 target=new Vector3(-4f,0,0);
            Vector3 offset=new Vector3(0,25,-20.5f);
            // 目标距离处横向可视半宽 = dist*tan(fov/2)*aspect；不足 33m 全宽时整体后移。
            float dist=offset.magnitude;
            float halfWidth=dist*Mathf.Tan(camera.fieldOfView*.5f*Mathf.Deg2Rad)*Mathf.Max(.5f,camera.aspect);
            if (halfWidth*2f<33f) offset*=33f/(halfWidth*2f);
            camera.transform.position=target+offset;
            camera.transform.LookAt(target);
        }
        public static readonly Rect[] Ramps = { DesignRect(287,19,213,92), DesignRect(205,166,295,92), DesignRect(287,303,213,92) };
        public static readonly float[] StandZ = { FromDesign(0,65).z, FromDesign(0,212).z, FromDesign(0,349).z };
        public static readonly Rect[] RoadSurfaces = {
            DesignRect(235f, 64f, 52f, 25f),
            DesignRect(128f, 314f, 43f, 25f),
            DesignRect(232f, 314f, 55f, 25f),
            DesignRect(128f, 232f, 43f, 25f),
            DesignRect(129f, 60f, 42f, 25f),
            DesignRect(171f, 43f, 64f, 328f)
        };
        public static readonly Rect[] PavementSurfaces = {
            DesignRect(449f, 111f, 13f, 38f),
            DesignRect(191f, 166f, 14f, 109f),
            DesignRect(205f, 259f, 257f, 16f),
            DesignRect(449f, 273f, 13f, 30f),
            DesignRect(129f, 149f, 333f, 17f)
        };
        public static readonly Vector3[][] TaxiwaySurfaces = {
            new[] { FromDesign(792.0950f, 14.0000f), FromDesign(821.6525f, 51.1047f), FromDesign(735.3686f, 119.8381f), FromDesign(705.8112f, 82.7334f) },
            new[] { FromDesign(752.8380f, 105.2830f), FromDesign(715.7335f, 134.8407f), FromDesign(646.9996f, 48.5572f), FromDesign(684.1042f, 18.9995f) },
            new[] { FromDesign(742.8380f, 252.2840f), FromDesign(705.7335f, 281.8417f), FromDesign(636.9996f, 195.5582f), FromDesign(674.1042f, 166.0005f) },
            new[] { FromDesign(666.5570f, 382.8380f), FromDesign(636.9993f, 345.7335f), FromDesign(723.2828f, 276.9996f), FromDesign(752.8405f, 314.1042f) },
            new[] { FromDesign(500.0000f, 66.0000f), FromDesign(500.0000f, 19.0000f), FromDesign(684.0000f, 19.0000f), FromDesign(684.0000f, 66.0000f) },
            new[] { FromDesign(500.0000f, 213.0000f), FromDesign(500.0000f, 166.0000f), FromDesign(674.0000f, 166.0000f), FromDesign(674.0000f, 213.0000f) },
            new[] { FromDesign(500.0000f, 383.0000f), FromDesign(500.0000f, 336.0000f), FromDesign(667.0000f, 336.0000f), FromDesign(667.0000f, 383.0000f) },
            new[] { FromDesign(706.0000f, 314.1050f), FromDesign(735.5570f, 277.0000f), FromDesign(821.8410f, 345.7320f), FromDesign(792.2840f, 382.8370f) },
            new[] { FromDesign(706.0000f, 83.0000f), FromDesign(753.0000f, 83.0000f), FromDesign(753.0000f, 314.0000f), FromDesign(706.0000f, 314.0000f) }
        };
        public static readonly Rect Runway = DesignRect(784,7,75,388);
        public const float RunwayX = (821.5f-330f)*.04f;
        public static Vector3 Station(ServiceKind kind)
        {
            float y=kind==ServiceKind.Meals?72.5f:kind==ServiceKind.Baggage?244.5f:kind==ServiceKind.Fuel?326.5f:157.5f;
            return FromDesign(139,y);
        }
        public static Vector3 CartPark(ServiceKind kind)
        {
            // Park beside the spine, away from branch junctions and the ramp-3
            // pedestrian strip, leaving a continuous lane for the other carts.
            // The fuel truck's service bay sits beside, but outside the foot
            // interaction radius of, the valve station. That gives E distinct
            // meanings at the valve and at the truck without a hidden toggle.
            return FromDesign(218,kind==ServiceKind.Meals?112:kind==ServiceKind.Baggage?211:326.5f);
        }
        public static Vector3 Dock(int stand)
        {
            return FromDesign(stand==1?249:300,stand==0?76.5f:stand==1?244.5f:326.5f);
        }
        public static Vector3 PlanePark(int stand) { return FromDesign(393.5f,stand==0?65:stand==1?212:349); }
        public static Vector3 BoardingPoint(int stand) { return FromDesign(stand==0?456:455,stand==0?110:stand==1?165:303); }
        public static bool InsideParkedAircraftArea(Vector3 p,int stand)
        {
            // Service lanes on the west side remain usable, including the extended middle ramp.
            Vector3 park=PlanePark(stand);return Mathf.Abs(p.x-park.x)<2.65f && Mathf.Abs(p.z-park.z)<1.55f;
        }
        public static Vector3[] PavementPath(int stand)
        {
            var gate=Station(ServiceKind.Boarding);
            if(stand==2)return new[]{gate,FromDesign(198,157.5f),FromDesign(198,267),FromDesign(455.5f,267),BoardingPoint(stand)};
            return new[]{gate,FromDesign(455.5f,157.5f),BoardingPoint(stand)};
        }
        // Taxiway branches are connected through the shared north-south spine, not separate runway stubs.
        public static Vector3[] TaxiInPath(int stand)
        {
            var route=new List<Vector3>{FromDesign(821.5f,-65)+Vector3.up*1.8f,FromDesign(821.5f,20),FromDesign(821.5f,45),FromDesign(795,45),FromDesign(729.5f,110)};
            if(stand==0){route.Add(FromDesign(675,42.5f));route.Add(FromDesign(500,42.5f));}
            else if(stand==1){route.Add(FromDesign(729.5f,258));route.Add(FromDesign(665,189.5f));route.Add(FromDesign(500,189.5f));}
            else{route.Add(FromDesign(729.5f,301));route.Add(FromDesign(657,359.5f));route.Add(FromDesign(500,359.5f));}
            route.Add(FromDesign(450,stand==0?65:stand==1?212:349));route.Add(PlanePark(stand));return route.ToArray();
        }
        public static Vector3[] TaxiOutPath(int stand)
        {
            var route=new List<Vector3>{PlanePark(stand),FromDesign(450,stand==0?65:stand==1?212:349)};
            if(stand==0){route.Add(FromDesign(500,42.5f));route.Add(FromDesign(675,42.5f));route.Add(FromDesign(729.5f,110));}
            else if(stand==1){route.Add(FromDesign(500,189.5f));route.Add(FromDesign(665,189.5f));route.Add(FromDesign(729.5f,258));}
            else{route.Add(FromDesign(500,359.5f));route.Add(FromDesign(657,359.5f));}
            route.Add(FromDesign(729.5f,301));route.Add(FromDesign(795,360));route.Add(FromDesign(821.5f,360));route.Add(FromDesign(821.5f,389));return route.ToArray();
        }
        public static readonly Rect[] Drivable = CreateDrivable();
        static Rect[] CreateDrivable()
        {
            var result=new List<Rect>(RoadSurfaces);result.AddRange(Ramps);return result.ToArray();
        }

        // A cart's margin is the half extent of its (axis-aligned) footprint.
        // The old implementation eroded every source rectangle independently.
        // That turns two rectangles which share an edge into two disconnected
        // roads.  The helpers below work on the union of all rectangles first,
        // then test the whole footprint against that union.
        const float GeometryEpsilon = 0.00001f;

        public static bool InDrivable(Vector3 p, float margin)
        {
            if (!IsFinite(p.x) || !IsFinite(p.z) || !IsFinite(margin)) return false;
            float extent = Mathf.Max(0f, margin);
            float minX = p.x - extent, maxX = p.x + extent;
            float minZ = p.z - extent, maxZ = p.z + extent;

            // The active rectangle set changes only at a rectangle's x edge.
            // Testing the endpoints, every such edge, and one point in each
            // interval therefore tests the complete square footprint, including
            // concave union corners (corner sampling alone misses those).
            float[] xCuts = new float[2 + Drivable.Length * 2];
            int cutCount = 0;
            cutCount = AddSortedCut(xCuts, cutCount, minX);
            cutCount = AddSortedCut(xCuts, cutCount, maxX);
            for (int i = 0; i < Drivable.Length; i++)
            {
                Rect r = Drivable[i];
                if (r.xMin > minX + GeometryEpsilon && r.xMin < maxX - GeometryEpsilon)
                    cutCount = AddSortedCut(xCuts, cutCount, r.xMin);
                if (r.xMax > minX + GeometryEpsilon && r.xMax < maxX - GeometryEpsilon)
                    cutCount = AddSortedCut(xCuts, cutCount, r.xMax);
            }
            SortCuts(xCuts, cutCount);
            for (int i = 0; i < cutCount; i++)
            {
                if (!VerticalSpanCovered(xCuts[i], minZ, maxZ)) return false;
                if (i + 1 < cutCount && xCuts[i + 1] - xCuts[i] > GeometryEpsilon)
                {
                    float sampleX = (xCuts[i] + xCuts[i + 1]) * 0.5f;
                    if (!VerticalSpanCovered(sampleX, minZ, maxZ)) return false;
                }
            }
            return cutCount > 0;
        }

        /// <summary>
        /// Keep a driven cart inside the road network.  A movement is accepted
        /// only when its whole swept segment stays in the eroded road union;
        /// this prevents a large update from teleporting through a forbidden
        /// gap.  If a diagonal movement is blocked, slide along one axis and
        /// keep the continuous segment that makes the most progress.
        /// </summary>
        public static Vector3 ClampToDrivable(Vector3 from, Vector3 to, float margin = 0.35f)
        {
            float extent = Mathf.Max(0f, margin);
            if (!InDrivable(from, extent)) return from;
            if (IsDrivableSegment(from, to, extent)) return to;

            Vector3 xCorner = new Vector3(to.x, from.y, from.z);
            Vector3 zCorner = new Vector3(from.x, from.y, to.z);
            Vector3 xFirst = FurthestOnSegment(from, xCorner, extent);
            Vector3 zFirst = FurthestOnSegment(from, zCorner, extent);

            float xProgress = ManhattanProgress(from, to, xFirst);
            float zProgress = ManhattanProgress(from, to, zFirst);
            return xProgress >= zProgress ? xFirst : zFirst;
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        /// <summary>
        /// Route through the road union for bots and the automated playtest.
        /// Candidate x/z coordinates come from the rectangle boundaries and
        /// their clearance boundaries.  A four-neighbour graph over those
        /// coordinates finds an axis-aligned route around gaps and concave
        /// corners without assuming which rectangle is the spine or a branch.
        /// Returns an empty array when the straight segment is already
        /// drivable, the turning points otherwise, and null when no
        /// clearance-safe route exists.  Endpoints a few centimetres off the
        /// eroded union are snapped onto the nearest reachable network point
        /// instead of failing, because real carts stop at interaction
        /// distance from stations, not on planner-valid centres.  Callers
        /// must NOT fall back to a straight line on null — it would drive
        /// straight into a non-road area or a parked vehicle and wedge the
        /// cart (the vehicle-vs-vehicle check cancels the whole move, so the
        /// cart cannot slide off a blocking obstacle on its own).
        /// </summary>
        public static Vector3[] DriveRoute(Vector3 from, Vector3 to, IList<Vector3> parkedVehicles = null)
        {
            if (!IsFinite(from.x) || !IsFinite(from.z) ||
                !IsFinite(to.x) || !IsFinite(to.z)) return null;

            // The gameplay clamp uses .35 and the editor playtest checks .3.
            // Planning with a slightly stricter erosion (.37) keeps every emitted
            // segment inside the clamp's feasible region with real slack, so a
            // small corner cut or alignment drift recovers instead of wedging
            // the vehicle against the eroded-road boundary.
            const float RouteMargin = 0.37f;
            if (IsDrivableSegment(from, to, RouteMargin) && ClearOfVehicles(from,to,parkedVehicles)) return new Vector3[0];
            return FindGridRoute(from, to, RouteMargin, parkedVehicles);
        }

        static bool ClearOfVehicles(Vector3 from,Vector3 to,IList<Vector3> parkedVehicles)
        {
            if(parkedVehicles==null)return true;
            from.y=to.y=0;Vector3 direction=to-from;float length=direction.sqrMagnitude;
            foreach(var vehicle in parkedVehicles)
            {
                Vector3 obstacle=new Vector3(vehicle.x,0,vehicle.z);
                float t=length<.00001f?0:Mathf.Clamp01(Vector3.Dot(obstacle-from,direction)/length);
                if(Vector3.Distance(obstacle,from+direction*t)<1.01f)return false;
            }
            return true;
        }

        static int AddSortedCut(float[] cuts, int count, float value)
        {
            for (int i = 0; i < count; i++)
                if (Mathf.Abs(cuts[i] - value) <= GeometryEpsilon) return count;
            cuts[count] = value;
            return count + 1;
        }

        static void SortCuts(float[] cuts, int count)
        {
            for (int i = 1; i < count; i++)
            {
                float value = cuts[i];
                int j = i - 1;
                while (j >= 0 && cuts[j] > value)
                {
                    cuts[j + 1] = cuts[j];
                    j--;
                }
                cuts[j + 1] = value;
            }
        }

        static bool VerticalSpanCovered(float x, float minZ, float maxZ)
        {
            float reach = minZ;
            if (maxZ - minZ <= GeometryEpsilon)
            {
                for (int i = 0; i < Drivable.Length; i++)
                {
                    Rect r = Drivable[i];
                    if (ContainsX(r, x) && r.yMin <= minZ + GeometryEpsilon &&
                        r.yMax >= maxZ - GeometryEpsilon) return true;
                }
                return false;
            }

            // Repeatedly extend the covered interval.  This merges all active
            // z intervals, including intervals that only meet at an edge.
            for (int pass = 0; pass <= Drivable.Length; pass++)
            {
                float nextReach = reach;
                for (int i = 0; i < Drivable.Length; i++)
                {
                    Rect r = Drivable[i];
                    if (ContainsX(r, x) && r.yMin <= reach + GeometryEpsilon &&
                        r.yMax > nextReach + GeometryEpsilon)
                        nextReach = r.yMax;
                }
                if (nextReach <= reach + GeometryEpsilon) break;
                reach = nextReach;
                if (reach >= maxZ - GeometryEpsilon) return true;
            }
            return reach >= maxZ - GeometryEpsilon;
        }

        static bool ContainsX(Rect r, float x)
        {
            return x >= r.xMin - GeometryEpsilon && x <= r.xMax + GeometryEpsilon;
        }

        static bool IsDrivableSegment(Vector3 from, Vector3 to, float margin)
        {
            if (!InDrivable(from, margin) || !InDrivable(to, margin)) return false;
            float dx = to.x - from.x, dz = to.z - from.z;
            // A footprint can enter or leave a rectangle when either of its
            // two x/z faces reaches either rectangle edge: four events per
            // axis, eight per rectangle in total.  The inner (+/- margin)
            // events are essential at concave road corners.
            int maxCuts = 2 + Drivable.Length * 16;
            float[] cuts = new float[maxCuts];
            int count = 0;
            cuts[count++] = 0f;
            cuts[count++] = 1f;
            if (Mathf.Abs(dx) > GeometryEpsilon || Mathf.Abs(dz) > GeometryEpsilon)
            {
                for (int i = 0; i < Drivable.Length; i++)
                {
                    Rect r = Drivable[i];
                    count = AddCrossing(cuts, count, from.x, dx, r.xMin - margin);
                    count = AddCrossing(cuts, count, from.x, dx, r.xMin + margin);
                    count = AddCrossing(cuts, count, from.x, dx, r.xMax - margin);
                    count = AddCrossing(cuts, count, from.x, dx, r.xMax + margin);
                    count = AddCrossing(cuts, count, from.z, dz, r.yMin - margin);
                    count = AddCrossing(cuts, count, from.z, dz, r.yMin + margin);
                    count = AddCrossing(cuts, count, from.z, dz, r.yMax - margin);
                    count = AddCrossing(cuts, count, from.z, dz, r.yMax + margin);
                }
            }
            SortCuts(cuts, count);
            for (int i = 0; i < count; i++)
            {
                if (!InDrivable(Vector3.Lerp(from, to, cuts[i]), margin)) return false;
                if (i + 1 < count && cuts[i + 1] - cuts[i] > GeometryEpsilon &&
                    !InDrivable(Vector3.Lerp(from, to, (cuts[i] + cuts[i + 1]) * .5f), margin))
                    return false;
            }
            return true;
        }

        static int AddCrossing(float[] cuts, int count, float start, float delta, float boundary)
        {
            if (Mathf.Abs(delta) <= GeometryEpsilon) return count;
            float t = (boundary - start) / delta;
            if (t <= GeometryEpsilon || t >= 1f - GeometryEpsilon) return count;
            return AddSortedCut(cuts, count, t);
        }

        static Vector3 FurthestOnSegment(Vector3 from, Vector3 to, float margin)
        {
            if (!InDrivable(from, margin)) return from;
            if (IsDrivableSegment(from, to, margin)) return to;

            float dx = to.x - from.x, dz = to.z - from.z;
            float[] cuts = new float[2 + Drivable.Length * 16];
            int count = 0;
            cuts[count++] = 0f;
            cuts[count++] = 1f;
            for (int i = 0; i < Drivable.Length; i++)
            {
                Rect r = Drivable[i];
                count = AddCrossing(cuts, count, from.x, dx, r.xMin - margin);
                count = AddCrossing(cuts, count, from.x, dx, r.xMin + margin);
                count = AddCrossing(cuts, count, from.x, dx, r.xMax - margin);
                count = AddCrossing(cuts, count, from.x, dx, r.xMax + margin);
                count = AddCrossing(cuts, count, from.z, dz, r.yMin - margin);
                count = AddCrossing(cuts, count, from.z, dz, r.yMin + margin);
                count = AddCrossing(cuts, count, from.z, dz, r.yMax - margin);
                count = AddCrossing(cuts, count, from.z, dz, r.yMax + margin);
            }
            SortCuts(cuts, count);

            float safe = 0f;
            for (int i = 0; i < count - 1; i++)
            {
                float start = cuts[i], end = cuts[i + 1];
                if (!InDrivable(Vector3.Lerp(from, to, start), margin))
                    return Vector3.Lerp(from, to, safe);
                safe = start;
                if (end - start <= GeometryEpsilon) continue;
                float middle = (start + end) * .5f;
                if (!InDrivable(Vector3.Lerp(from, to, middle), margin))
                    return BoundaryBeforeInvalid(from, to, start, middle, margin);
                if (!InDrivable(Vector3.Lerp(from, to, end), margin))
                    return BoundaryBeforeInvalid(from, to, start, end, margin);
                safe = end;
            }
            return Vector3.Lerp(from, to, safe);
        }

        static Vector3 BoundaryBeforeInvalid(Vector3 from, Vector3 to, float validT, float invalidT, float margin)
        {
            float lo = validT, hi = invalidT;
            if (!InDrivable(Vector3.Lerp(from, to, lo), margin)) return Vector3.Lerp(from, to, lo);
            for (int i = 0; i < 24; i++)
            {
                float middle = (lo + hi) * .5f;
                if (InDrivable(Vector3.Lerp(from, to, middle), margin)) lo = middle;
                else hi = middle;
            }
            return Vector3.Lerp(from, to, lo);
        }

        static float ManhattanProgress(Vector3 from, Vector3 to, Vector3 point)
        {
            float total = Mathf.Abs(to.x - from.x) + Mathf.Abs(to.z - from.z);
            if (total <= GeometryEpsilon) return 1f;
            return (Mathf.Abs(point.x - from.x) + Mathf.Abs(point.z - from.z)) / total;
        }

        static Vector3[] FindGridRoute(Vector3 from, Vector3 to, float margin, IList<Vector3> parkedVehicles)
        {
            List<float> xs = BuildRouteCoordinates(from.x, to.x, margin, true, parkedVehicles);
            List<float> zs = BuildRouteCoordinates(from.z, to.z, margin, false, parkedVehicles);
            int xCount = xs.Count, zCount = zs.Count;
            if (xCount == 0 || zCount == 0) return null;

            bool[,] valid = new bool[xCount, zCount];
            for (int x = 0; x < xCount; x++)
                for (int z = 0; z < zCount; z++)
                    valid[x, z] = InDrivable(new Vector3(xs[x], 0f, zs[z]), margin) && ClearOfVehicles(new Vector3(xs[x],0,zs[z]),new Vector3(xs[x],0,zs[z]),parkedVehicles);

            int fromX = FindCoordinate(xs, from.x), fromZ = FindCoordinate(zs, from.z);
            int toX = FindCoordinate(xs, to.x), toZ = FindCoordinate(zs, to.z);
            if (fromX < 0 || fromZ < 0 || toX < 0 || toZ < 0) return null;

            // A real cart rarely sits exactly on a planner-valid point: any
            // stop within interaction radius of a station can leave its
            // footprint millimetres outside the 0.37-eroded road union while
            // still being perfectly drivable under the gameplay clamp (0.35).
            // Refusing to plan from such a point makes NULL depend on
            // sub-centimetre float noise. Snap a slightly-off endpoint to the
            // nearest network point the cart can actually reach.
            bool snappedFrom = !valid[fromX, fromZ];
            if (snappedFrom && !SnapToNetwork(xs, zs, valid, from, parkedVehicles, out fromX, out fromZ)) return null;
            bool snappedTo = !valid[toX, toZ];
            if (snappedTo && !SnapToNetwork(xs, zs, valid, to, parkedVehicles, out toX, out toZ)) return null;

            int total = xCount * zCount;
            int[] previous = new int[total];
            for (int i = 0; i < total; i++) previous[i] = -2;
            int start = fromX * zCount + fromZ, finish = toX * zCount + toZ;
            previous[start] = -1;
            var pending = new Queue<int>();
            pending.Enqueue(start);
            while (pending.Count > 0 && previous[finish] == -2)
            {
                int current = pending.Dequeue();
                int cx = current / zCount, cz = current % zCount;
                TryGridNeighbour(cx - 1, cz, cx, cz, xCount, zCount, xs, zs, valid, previous, pending, margin, parkedVehicles);
                TryGridNeighbour(cx + 1, cz, cx, cz, xCount, zCount, xs, zs, valid, previous, pending, margin, parkedVehicles);
                TryGridNeighbour(cx, cz - 1, cx, cz, xCount, zCount, xs, zs, valid, previous, pending, margin, parkedVehicles);
                TryGridNeighbour(cx, cz + 1, cx, cz, xCount, zCount, xs, zs, valid, previous, pending, margin, parkedVehicles);
            }
            if (previous[finish] == -2) return null;

            var nodes = new List<Vector3>();
            for (int at = finish; at >= 0; at = previous[at])
            {
                int x = at / zCount, z = at % zCount;
                nodes.Add(new Vector3(xs[x], from.y, zs[z]));
                if (at == start) break;
            }
            nodes.Reverse();

            // Return only turning points; callers append the final destination.
            // A snapped start is emitted explicitly: the caller drives from the
            // real position, and the leg real→snap is only safe when routed
            // through the verified snap point.
            var route = new List<Vector3>();
            if (snappedFrom) route.Add(nodes[0]);
            for (int i = 1; i + 1 < nodes.Count; i++)
            {
                Vector3 before = nodes[i - 1], current = nodes[i], after = nodes[i + 1];
                bool sameX = Mathf.Abs(before.x - current.x) <= GeometryEpsilon &&
                    Mathf.Abs(current.x - after.x) <= GeometryEpsilon;
                bool sameZ = Mathf.Abs(before.z - current.z) <= GeometryEpsilon &&
                    Mathf.Abs(current.z - after.z) <= GeometryEpsilon;
                if (!sameX && !sameZ) route.Add(current);
            }
            return route.ToArray();
        }

        // The gameplay clamp (ClampToDrivable) erodes roads by this margin.
        const float GameplayDriveMargin = 0.35f;

        // Nearest network node to a point that sits just off the planner's
        // eroded union. The node must be reachable from the real position:
        // the segment between them has to stay drivable under the gameplay
        // clamp and clear of parked vehicles, otherwise the cart would wedge
        // on its way to the planned route.
        static bool SnapToNetwork(List<float> xs, List<float> zs, bool[,] valid, Vector3 point, IList<Vector3> parkedVehicles, out int bestX, out int bestZ)
        {
            const float SnapRadius = 1.5f;
            bestX = -1; bestZ = -1;
            float bestDistance = SnapRadius;
            for (int x = 0; x < xs.Count; x++)
                for (int z = 0; z < zs.Count; z++)
                {
                    if (!valid[x, z]) continue;
                    Vector3 node = new Vector3(xs[x], 0f, zs[z]);
                    float distance = Vector3.Distance(new Vector3(point.x, 0f, point.z), node);
                    if (distance > bestDistance) continue;
                    if (!IsDrivableSegment(point, node, GameplayDriveMargin)) continue;
                    if (!ClearOfVehicles(point, node, parkedVehicles)) continue;
                    bestDistance = distance;
                    bestX = x; bestZ = z;
                }
            return bestX >= 0;
        }

        static List<float> BuildRouteCoordinates(float from, float to, float margin, bool xAxis, IList<Vector3> parkedVehicles)
        {
            var values = new List<float>(2 + Drivable.Length * 6);
            AddRouteCoordinate(values, from);
            AddRouteCoordinate(values, to);
            for (int i = 0; i < Drivable.Length; i++)
            {
                Rect r = Drivable[i];
                float min = xAxis ? r.xMin : r.yMin;
                float max = xAxis ? r.xMax : r.yMax;
                AddRouteCoordinate(values, min);
                AddRouteCoordinate(values, max);
                AddRouteCoordinate(values, min - margin);
                AddRouteCoordinate(values, min + margin);
                AddRouteCoordinate(values, max - margin);
                AddRouteCoordinate(values, max + margin);
            }
            if(parkedVehicles!=null)foreach(var vehicle in parkedVehicles)
            {
                float center=xAxis?vehicle.x:vehicle.z;
                AddRouteCoordinate(values,center-1.02f);AddRouteCoordinate(values,center+1.02f);
            }
            values.Sort();
            var withMids = new List<float>(values.Count * 2);
            for (int i = 0; i < values.Count; i++)
            {
                AddRouteCoordinate(withMids, values[i]);
                if (i + 1 < values.Count && values[i + 1] - values[i] > GeometryEpsilon)
                    AddRouteCoordinate(withMids, (values[i] + values[i + 1]) * .5f);
            }
            withMids.Sort();
            return withMids;
        }

        static void AddRouteCoordinate(List<float> values, float value)
        {
            for (int i = 0; i < values.Count; i++)
                if (Mathf.Abs(values[i] - value) <= GeometryEpsilon) return;
            values.Add(value);
        }

        static int FindCoordinate(List<float> values, float value)
        {
            for (int i = 0; i < values.Count; i++)
                if (Mathf.Abs(values[i] - value) <= GeometryEpsilon) return i;
            return -1;
        }

        static void TryGridNeighbour(int nx, int nz, int cx, int cz, int xCount, int zCount,
            List<float> xs, List<float> zs, bool[,] valid, int[] previous, Queue<int> pending, float margin, IList<Vector3> parkedVehicles)
        {
            if (nx < 0 || nx >= xCount || nz < 0 || nz >= zCount || !valid[nx, nz]) return;
            int next = nx * zCount + nz;
            if (previous[next] != -2) return;
            Vector3 a = new Vector3(xs[cx], 0f, zs[cz]);
            Vector3 b = new Vector3(xs[nx], 0f, zs[nz]);
            if (!IsDrivableSegment(a, b, margin) || !ClearOfVehicles(a,b,parkedVehicles)) return;
            previous[next] = cx * zCount + cz;
            pending.Enqueue(next);
        }
    }
}
