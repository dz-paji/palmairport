using System.Collections.Generic;
using UnityEngine;

namespace IslandAirport
{
    /// <summary>
    /// Procedural art for the small island airport.  The class intentionally owns
    /// no gameplay state: Build creates the scenery, while the three public
    /// factory methods create the moving actors used by the game layer.
    /// M1: 玩具感 v7 化——坡屋顶设施 + 图标牌 + 站点道具、分形态车辆、
    /// 分层海岸、锥桶与花丛装饰、统一柔和光照（SetupLighting）。
    /// 颜色与材质事实源在 <see cref="AirportStyle"/>。
    /// </summary>
    public static partial class AirportWorld
    {
        // 环境色引用 AirportStyle（保留字段名以减少调用点改动）。
        private static readonly Color Sand = AirportStyle.Sand;
        private static readonly Color Ground = AirportStyle.Ground;
        private static readonly Color Water = AirportStyle.SeaDeep;
        private static readonly Color Shallow = AirportStyle.SeaShallow;
        private static readonly Color WaterHighlight = AirportStyle.Foam;
        private static readonly Color RoadColor = AirportStyle.Surfaces.Road;
        private static readonly Color RunwayColor = AirportStyle.Surfaces.Runway;
        private static readonly Color Pavement = AirportStyle.Surfaces.Pavement;
        private static readonly Color WhiteLine = AirportStyle.Surfaces.WhiteLine;
        private static readonly Color YellowLine = AirportStyle.Surfaces.YellowLine;
        private static readonly Color ApronConcrete = AirportStyle.Surfaces.Apron;
        private static readonly Color DarkMetal = Hex("#314650");
        private static readonly Color Wood = Hex("#A36B45");
        private static readonly Color Trunk = Hex("#9A6546");
        private static readonly Color Leaf = Hex("#4F9C70");
        private static readonly Color LeafLight = Hex("#6FBF7E");
        private static readonly Color Window = Hex("#193A51");
        private static readonly Color Skin = Hex("#F2B993");
        private static readonly Color Hair = Hex("#4D3D3B");
        private static readonly Color VestYellow = Hex("#F6D34A");

        /// <summary>
        /// 唯一光照事实源：暖阳 + 柔和阴影 + 环境光 + 阴影距离。
        /// 场景已有方向光时复用，避免重复创建。编辑器烘焙与运行时共用。
        /// </summary>
        public static Light SetupLighting()
        {
            Light sun = null;
            foreach (var light in Object.FindObjectsByType<Light>())
            {
                if (light.type == LightType.Directional) { sun = light; break; }
            }
            if (!sun)
            {
                var sunObject = new GameObject("Afternoon sun");
                sun = sunObject.AddComponent<Light>();
                sun.type = LightType.Directional;
            }
            sun.transform.rotation = Quaternion.Euler(52.0f, -34.0f, 0.0f);
            sun.color = Hex("FFF3DF");
            sun.intensity = .92f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.38f;
            sun.shadowBias = 0.04f;
            sun.shadowNormalBias = 0.55f;
            ApplyEnvironment();
            return sun;
        }

        /// <summary>环境光与质量设置（烘焙场景与运行时 PalmBay 都要执行）。</summary>
        public static void ApplyEnvironment()
        {
            // Explicit hemisphere lighting also works in scenes with no skybox/probes.
            // A skybox-only ambient mode left procedural roofs nearly black.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Hex("CFDFE5");
            RenderSettings.ambientEquatorColor = Hex("A5BEC2");
            RenderSettings.ambientGroundColor = Hex("958773");
            RenderSettings.ambientIntensity = 0.8f;
            RenderSettings.fog = false;
            QualitySettings.shadowDistance = 70.0f;
            QualitySettings.antiAliasing = 4;
        }

        /// <summary>
        /// Creates the complete environment and returns its root transform.
        /// Geometry follows <see cref="Level1Map"/> so the visuals always match
        /// the drivable/walkable/taxi rules. No camera, light, character,
        /// cart, or aircraft is spawned here.
        /// </summary>
        public static Transform Build()
        {
            GameObject worldObject = new GameObject("Airport World");
            Transform world = worldObject.transform;
            world.position = Vector3.zero;
            world.rotation = Quaternion.identity;

            BuildIsland(world);
            BuildRoadNetwork(world);
            BuildPavements(world);
            BuildFacilities(world);
            BuildAprons(world);
            BuildTaxiwayRunway(world);
            BuildDecorations(world);
            BuildPier(world);

            return world;
        }

        /// <summary>
        /// Parses #RGB, #RGBA, #RRGGBB, or #RRGGBBAA. 实现收敛在 AirportStyle.Hex，
        /// 此处保留旧入口以保持既有调用点兼容。
        /// </summary>
        public static Color Hex(string hex) { return AirportStyle.Hex(hex); }

        /// <summary>
        /// Adds a simple built-in TextMesh label. It is laid flat for an overhead view.
        /// </summary>
        public static TextMesh WorldLabel(string text, Vector3 pos, Color color, float size = 0.2f)
        {
            return CreateLabel(null, text, pos, color, size);
        }

        private static void Surface(Transform parent, string name, Rect rect, float top, Color color)
        {
            Primitive(parent, name, PrimitiveType.Cube, new Vector3(rect.center.x, top - .025f, rect.center.y), new Vector3(rect.width, .05f, rect.height), color, Quaternion.identity);
        }

        private static void BuildIsland(Transform world)
        {
            Rect bounds = Level1Map.WorldBounds;
            Surface(world, "Surrounding Sea", new Rect(bounds.xMin - 30, bounds.yMin - 30, bounds.width + 60, bounds.height + 60), -.6f, Water);
            // Rounded terraces sit outside the original playable rectangle. No collider or
            // navigation boundary changes; all detail below is presentation-only.
            RoundedTerrace(world, "Lagoon shelf", bounds, 3.4f, 3.6f, -.51f, Hex("48B2BC"));
            RoundedTerrace(world, "Shallow Water", bounds, 2.1f, 2.8f, -.42f, Shallow);
            RoundedTerrace(world, "Wet sand", bounds, 1.15f, 2.1f, -.27f, Hex("CFCAA3"));
            RoundedTerrace(world, "Sandy Island", bounds, .72f, 1.8f, -.15f, Sand);
            Surface(world, "Cream Walkable Ground", bounds, .015f, Ground);

            var vertices = new List<Vector3>(); var triangles = new List<int>();
            for (int side = 0; side < 4; side++)
            {
                bool horizontal = side < 2;
                float length = horizontal ? bounds.width : bounds.height;
                int count = Mathf.FloorToInt(length / 1.8f);
                for (int i = 0; i < count; i++)
                {
                    float along = (i + .5f) * length / count;
                    float offset = 1.48f + .09f * Mathf.Sin(i * 1.7f);
                    Vector3 center = horizontal
                        ? new Vector3(bounds.xMin + along, -.395f, side == 0 ? bounds.yMin - offset : bounds.yMax + offset)
                        : new Vector3(side == 2 ? bounds.xMin - offset : bounds.xMax + offset, -.395f, bounds.yMin + along);
                    AddFlatDetail(vertices, triangles, center, horizontal ? .65f : .045f, horizontal ? .045f : .65f);
                }
            }
            MeshObject(world, "Shore foam ribbons", vertices.ToArray(), triangles.ToArray(), WaterHighlight);
            vertices.Clear(); triangles.Clear();
            for (int i = 0; i < 48; i++)
            {
                float x = bounds.xMin - 8 + (i * 7.137f) % (bounds.width + 16);
                float z = bounds.yMin - 7 + (i * 4.719f) % (bounds.height + 14);
                if (x > bounds.xMin - 3.5f && x < bounds.xMax + 3.5f && z > bounds.yMin - 3.5f && z < bounds.yMax + 3.5f) continue;
                AddFlatDetail(vertices, triangles, new Vector3(x, -.575f, z), .30f + .06f * (i % 4), .025f);
            }
            MeshObject(world, "Sea glints", vertices.ToArray(), triangles.ToArray(), Hex("70C9CE"));
        }

        // Each terrace is a single low-poly mesh, avoiding one renderer per shoreline tile.
        private static void RoundedTerrace(Transform parent, string name, Rect bounds, float expand, float radius, float top, Color color)
        {
            Rect r = Rect.MinMaxRect(bounds.xMin - expand, bounds.yMin - expand, bounds.xMax + expand, bounds.yMax + expand);
            var outline = new List<Vector2>();
            for (int corner = 0; corner < 4; corner++)
            {
                Vector2 center = new Vector2(corner == 0 || corner == 3 ? r.xMax - radius : r.xMin + radius,
                    corner < 2 ? r.yMax - radius : r.yMin + radius);
                for (int step = 0; step <= 6; step++)
                {
                    float angle = (corner * 90 + step * 15) * Mathf.Deg2Rad;
                    outline.Add(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
                }
            }
            ExtrudedPolygon(parent, name, outline.ToArray(), top - .10f, top, color);
        }

        private static void AddFlatDetail(List<Vector3> vertices, List<int> triangles, Vector3 center, float width, float depth)
        {
            int i = vertices.Count;
            vertices.Add(center + new Vector3(-width * .5f, 0, -depth * .5f));
            vertices.Add(center + new Vector3(-width * .5f, 0, depth * .5f));
            vertices.Add(center + new Vector3(width * .5f, 0, depth * .5f));
            vertices.Add(center + new Vector3(width * .5f, 0, -depth * .5f));
            triangles.Add(i); triangles.Add(i + 1); triangles.Add(i + 2);
            triangles.Add(i); triangles.Add(i + 2); triangles.Add(i + 3);
        }

        private static void BuildRoadNetwork(Transform world)
        {
            Transform group = new GameObject("road gorup - connected vehicle surface").transform; group.SetParent(world, false);
            for (int i = 0; i < Level1Map.RoadSurfaces.Length; i++) Surface(group, "Road part " + i, Level1Map.RoadSurfaces[i], .075f, RoadColor);
            // No per-object curbs: seams inside this union are traversable junctions.
            for (float y = 49; y < 370; y += 27)
            {
                if (y > 139 && y < 177) continue;
                var p = Level1Map.FromDesign(180, y);
                Primitive(group, "Road guide " + y, PrimitiveType.Cube, p + Vector3.up * .083f, new Vector3(.06f, .01f, .42f), YellowLine, Quaternion.identity);
            }
        }

        private static void BuildPavements(Transform world)
        {
            Transform group = new GameObject("pavement group - passenger priority").transform; group.SetParent(world, false);
            for (int i = 0; i < Level1Map.PavementSurfaces.Length; i++) Surface(group, "Pavement part " + i, Level1Map.PavementSurfaces[i], .10f, Pavement);
            // The Figma ramp-3 route follows x=198 INSIDE the vehicle spine.
            // Keep this brown shared strip visible instead of inventing a separate south channel.
            for (float x = 173; x < 235; x += 9)
            {
                var p = Level1Map.FromDesign(x, 157.5f);
                Primitive(group, "Gate crossing stripe " + x, PrimitiveType.Cube, p + Vector3.up * .115f, new Vector3(.17f, .012f, .58f), WhiteLine, Quaternion.identity);
            }
            for (int stand = 0; stand < Level1Map.StandCount; stand++)
            {
                Vector3 bp = Level1Map.BoardingPoint(stand);
                Primitive(group, "Boarding Point " + (stand + 1), PrimitiveType.Cylinder, bp + Vector3.up * .12f, new Vector3(.56f, .015f, .40f), WhiteLine, Quaternion.identity);
            }
        }

        private static void BuildFacilities(Transform world)
        {
            BuildFacility(world, ServiceKind.Meals, "Kitchen", "MEALS", 30);
            BuildFacility(world, ServiceKind.Boarding, "Boarding gate", "BOARDING", 119);
            BuildFacility(world, ServiceKind.Baggage, "Baggage", "BAGS", 208);
            BuildFacility(world, ServiceKind.Fuel, "Fuel", "FUEL", 297);
        }

        /// <summary>
        /// v7 化设施：基座 + 楼体 + 三棱柱坡屋顶（屋脊沿 x）+ 东侧白色图标牌 +
        /// 条纹遮阳棚 + 柜台 + 各站点道具。图标全部是程序化几何，无外部资产。
        /// </summary>
        private static void BuildFacility(Transform world, ServiceKind kind, string name, string label, float designY)
        {
            Color color = AirportStyle.ServiceColor(kind);
            Rect r = Level1Map.DesignRect(23, designY, 106, 59);
            Vector3 center = new Vector3(r.center.x, 0, r.center.y);
            Surface(world, name + " footprint", r, .10f, Darken(color, .25f));
            Primitive(world, name + " building", PrimitiveType.Cube, center + Vector3.up * .62f, new Vector3(r.width - .50f, 1.10f, r.height - .50f), Hex("FBF7EC"), Quaternion.identity);

            // 坡屋顶：z-y 截面三角形，沿 x 拉伸（借用竖直尾翼挤出器再旋转 90°）。
            float roofHalfDepth = (r.height - .30f) * .5f;
            Vector2[] gable =
            {
                new Vector2(-roofHalfDepth, 1.16f), new Vector2(roofHalfDepth, 1.16f), new Vector2(0.0f, 1.72f)
            };
            var roof = ExtrudedVerticalFin(world, name + " roof", gable, -(r.width - .30f) * .5f, (r.width - .30f) * .5f, color);
            roof.transform.localPosition = center;
            roof.transform.localRotation = Quaternion.Euler(0.0f, 90.0f, 0.0f);

            // Fine roof edges and a recessed south-facing window make the four small
            // facilities read as buildings, while leaving their interaction frontage clear.
            Primitive(world, name + " roof ridge", PrimitiveType.Cube, center + Vector3.up * 1.73f,
                new Vector3(r.width - .25f, .055f, .085f), Darken(color, .16f), Quaternion.identity);
            foreach (float side in new[] { -1f, 1f })
                Primitive(world, name + " eave " + side, PrimitiveType.Cube,
                    center + new Vector3(0, 1.16f, side * roofHalfDepth),
                    new Vector3(r.width - .24f, .09f, .075f), WhiteLine, Quaternion.identity);
            BuildFacilityDetail(world, kind, name, r, center, color, roofHalfDepth);
            Vector3 window = new Vector3(r.center.x, .70f, r.yMin + .244f);
            Primitive(world, name + " window frame", PrimitiveType.Cube, window,
                new Vector3(r.width * .53f, .49f, .05f), Darken(color, .12f), Quaternion.identity);
            Primitive(world, name + " window glass", PrimitiveType.Cube, window + new Vector3(0, .01f, -.035f),
                new Vector3(r.width * .53f - .10f, .36f, .025f), Hex("5E8F9A"), Quaternion.identity, AirportStyle.Finish.Glass);
            Primitive(world, name + " window mullion", PrimitiveType.Cube, window + new Vector3(0, .01f, -.055f),
                new Vector3(.045f, .38f, .035f), WhiteLine, Quaternion.identity);
            Primitive(world, name + " window sill", PrimitiveType.Cube, window + new Vector3(0, -.26f, -.07f),
                new Vector3(r.width * .58f, .065f, .14f), WhiteLine, Quaternion.identity);

            // 条纹遮阳棚（面向东侧站点交互点）。
            for (int i = 0; i < 5; i++)
            {
                float z = r.center.y - (r.height - .42f) * .5f + .21f + i * ((r.height - .42f) / 4f);
                Primitive(world, name + " awning " + i, PrimitiveType.Cube,
                    new Vector3(r.xMax + .18f, 1.06f, z), new Vector3(.62f, .05f, (r.height - .42f) / 4f - .02f), i % 2 == 0 ? color : WhiteLine, Quaternion.Euler(0.0f, 0.0f, -18.0f));
            }
            // 柜台。
            Primitive(world, name + " counter", PrimitiveType.Cube,
                new Vector3(r.xMax - .06f, .42f, r.center.y), new Vector3(.44f, .72f, r.height - .80f), Darken(color, .12f), Quaternion.identity, AirportStyle.Finish.Plastic);
            Primitive(world, name + " counter top", PrimitiveType.Cube,
                new Vector3(r.xMax - .02f, .80f, r.center.y), new Vector3(.56f, .07f, r.height - .68f), Hex("FBF7EC"), Quaternion.identity);

            // 东侧立面图标牌：白底圆角牌 + 程序化任务图标。
            BuildStationIcon(world, kind, name, new Vector3(r.xMax + .02f, 1.42f, r.center.y), color);

            CreateLabel(world, label, new Vector3(r.xMax + 1.15f, .13f, r.center.y), DarkMetal, .10f);

            BuildFacilityProps(world, kind, name, r, color);
        }

        /// <summary>
        /// 1.0.9 设施细节：墙面横向板缝、四角立柱、屋面瓦线、西侧带台阶的门、
        /// 屋顶通风口（餐食中心为烟囱）、门边壁灯。均为薄盒，不改设施占地。
        /// </summary>
        private static void BuildFacilityDetail(Transform world, ServiceKind kind, string name, Rect r, Vector3 center, Color color, float roofHalfDepth)
        {
            Color wall = Hex("FBF7EC");
            Color seam = Darken(wall, .09f);
            Color trim = Darken(color, .10f);
            float wallW = r.width - .50f, wallD = r.height - .50f;
            foreach (float side in new[] { -1f, 1f })
            {
                // 长墙板缝（南墙窗户区段跳过中间一道）。
                for (int i = 0; i < 3; i++)
                {
                    float y = .33f + i * .27f;
                    if (side < 0 && i == 1) continue;
                    Primitive(world, name + " siding " + side + " " + i, PrimitiveType.Cube,
                        center + new Vector3(0, y, side * (wallD * .5f + .006f)), new Vector3(wallW - .12f, .018f, .014f), seam, Quaternion.identity);
                }
                for (int i = 0; i < 2; i++)
                    Primitive(world, name + " siding end " + side + " " + i, PrimitiveType.Cube,
                        center + new Vector3(side * (wallW * .5f + .006f), .40f + i * .34f, 0), new Vector3(.014f, .018f, wallD - .12f), seam, Quaternion.identity);
            }
            foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
                ModelBox(world, name + " corner " + sx + " " + sz, center + new Vector3(sx * wallW * .5f, .62f, sz * wallD * .5f),
                    new Vector3(.11f, 1.12f, .11f), trim, .022f, Quaternion.identity, AirportStyle.Finish.Matte);

            // 屋面瓦线：每侧坡面两道，平行屋脊并贴合坡度。
            float slope = Mathf.Atan2(.56f, roofHalfDepth) * Mathf.Rad2Deg;
            foreach (float side in new[] { -1f, 1f })
            for (int i = 1; i <= 2; i++)
            {
                float t = i / 3f;
                Vector3 p = center + new Vector3(0, Mathf.Lerp(1.16f, 1.72f, t) + .018f, side * Mathf.Lerp(roofHalfDepth, 0, t));
                Primitive(world, name + " tile line " + side + " " + i, PrimitiveType.Cube, p,
                    new Vector3(r.width - .30f, .02f, .05f), Darken(color, .14f), Quaternion.Euler(side * slope, 0, 0));
            }

            // 屋顶通风口 / 烟囱。
            Vector3 vent = center + new Vector3(-r.width * .28f, 0, roofHalfDepth * .35f);
            if (kind == ServiceKind.Meals)
            {
                ModelBox(world, name + " chimney", vent + Vector3.up * 1.62f, new Vector3(.30f, .50f, .30f), Darken(wall, .04f), .03f, Quaternion.identity, AirportStyle.Finish.Matte);
                ModelBox(world, name + " chimney cap", vent + Vector3.up * 1.90f, new Vector3(.38f, .07f, .38f), DarkMetal, .02f, Quaternion.identity, AirportStyle.Finish.Matte);
                Primitive(world, name + " chimney pipe", PrimitiveType.Cylinder, vent + Vector3.up * 1.98f, new Vector3(.12f, .08f, .12f), DarkMetal, Quaternion.identity);
            }
            else
            {
                Primitive(world, name + " vent pipe", PrimitiveType.Cylinder, vent + Vector3.up * 1.58f, new Vector3(.13f, .16f, .13f), Hex("B9C1C4"), Quaternion.identity, AirportStyle.Finish.Plastic);
                Cone(world, name + " vent cap", vent + Vector3.up * 1.76f, .12f, .03f, .10f, 8, DarkMetal, Quaternion.identity);
            }

            // 西侧门 + 两级台阶 + 壁灯（西侧是花丛一侧，不影响东侧交互正面）。
            Vector3 door = new Vector3(r.xMin + .25f, .50f, r.center.y + wallD * .18f);
            ModelBox(world, name + " door frame", door + new Vector3(-.012f, 0, 0), new Vector3(.05f, .82f, .52f), trim, .012f, Quaternion.identity, AirportStyle.Finish.Matte);
            ModelBox(world, name + " door panel", door + new Vector3(-.036f, -.01f, 0), new Vector3(.03f, .72f, .42f), color, .010f, Quaternion.identity);
            ModelBox(world, name + " door window", door + new Vector3(-.055f, .18f, 0), new Vector3(.012f, .18f, .26f), Hex("5E8F9A"), .004f, Quaternion.identity, AirportStyle.Finish.Glass);
            Primitive(world, name + " door knob", PrimitiveType.Sphere, door + new Vector3(-.058f, -.06f, -.14f), new Vector3(.035f, .035f, .035f), Hex("D9C77A"), Quaternion.identity, AirportStyle.Finish.Plastic);
            for (int i = 0; i < 2; i++)
                ModelBox(world, name + " step " + i, new Vector3(r.xMin + .25f - .16f - i * .14f, .13f - i * .045f, door.z),
                    new Vector3(.30f - i * .0f, .07f, .62f + i * .10f), Darken(wall, .12f), .015f, Quaternion.identity, AirportStyle.Finish.Matte);
            ModelBox(world, name + " lamp arm", door + new Vector3(-.05f, .52f, .36f), new Vector3(.08f, .04f, .04f), DarkMetal, .01f, Quaternion.identity, AirportStyle.Finish.Matte);
            Primitive(world, name + " lamp", PrimitiveType.Sphere, door + new Vector3(-.10f, .48f, .36f), new Vector3(.10f, .12f, .10f), Hex("FFF1A8"), Quaternion.identity, AirportStyle.Finish.Glass);
        }

        /// <summary>站点正面白底图标牌 + 几何图标（餐盘/箱/油泵/登机门）。</summary>
        private static void BuildStationIcon(Transform world, ServiceKind kind, string name, Vector3 pos, Color color)
        {
            Primitive(world, name + " icon board", PrimitiveType.Cube, pos, new Vector3(.10f, .62f, .92f), Hex("FBF7EC"), Quaternion.identity, AirportStyle.Finish.Plastic);
            Vector3 p = pos + new Vector3(.065f, 0.0f, 0.0f);
            if (kind == ServiceKind.Meals)
            {
                Primitive(world, name + " icon plate", PrimitiveType.Cylinder, p, new Vector3(.30f, .02f, .30f), color, Quaternion.Euler(0.0f, 0.0f, 90.0f));
                Primitive(world, name + " icon dome", PrimitiveType.Sphere, p + new Vector3(.015f, 0.0f, 0.0f), new Vector3(.05f, .22f, .22f), color, Quaternion.identity);
            }
            else if (kind == ServiceKind.Baggage)
            {
                Primitive(world, name + " icon case", PrimitiveType.Cube, p, new Vector3(.05f, .30f, .40f), color, Quaternion.identity);
                Primitive(world, name + " icon handle", PrimitiveType.Cube, p + new Vector3(0.0f, .20f, 0.0f), new Vector3(.05f, .10f, .16f), color, Quaternion.identity);
            }
            else if (kind == ServiceKind.Fuel)
            {
                Primitive(world, name + " icon pump", PrimitiveType.Cube, p + new Vector3(0.0f, -0.02f, -0.10f), new Vector3(.05f, .34f, .24f), color, Quaternion.identity);
                Primitive(world, name + " icon hose", PrimitiveType.Cylinder, p + new Vector3(0.0f, -0.05f, 0.16f), new Vector3(.045f, .22f, .045f), color, Quaternion.Euler(22.0f, 0.0f, 0.0f));
            }
            else
            {
                Primitive(world, name + " icon gate", PrimitiveType.Cube, p, new Vector3(.05f, .34f, .26f), color, Quaternion.identity);
                Primitive(world, name + " icon arrow", PrimitiveType.Cube, p + new Vector3(0.0f, -0.02f, 0.24f), new Vector3(.05f, .10f, .20f), color, Quaternion.identity);
            }
        }

        /// <summary>站点道具：餐箱堆 / 行李堆 / 油桶与油泵 / 登机绳栏。</summary>
        private static void BuildFacilityProps(Transform world, ServiceKind kind, string name, Rect r, Color color)
        {
            float x = r.xMax + .75f;
            if (kind == ServiceKind.Meals)
            {
                for (int i = 0; i < 3; i++)
                    Primitive(world, name + " crate " + i, PrimitiveType.Cube,
                        new Vector3(x + .18f * (i % 2), .18f + .28f * (i / 2), r.yMin + .55f), new Vector3(.34f, .26f, .30f), i % 2 == 0 ? Hex("FBF7EC") : color, Quaternion.Euler(0.0f, i * 14.0f, 0.0f));
            }
            else if (kind == ServiceKind.Baggage)
            {
                Primitive(world, name + " suitcase stack A", PrimitiveType.Cube, new Vector3(x, .16f, r.yMin + .55f), new Vector3(.40f, .30f, .30f), Hex("5FA8E0"), Quaternion.identity, AirportStyle.Finish.Plastic);
                Primitive(world, name + " suitcase stack B", PrimitiveType.Cube, new Vector3(x + .05f, .43f, r.yMin + .52f), new Vector3(.34f, .24f, .26f), Hex("E8855A"), Quaternion.Euler(0.0f, -18.0f, 0.0f), AirportStyle.Finish.Plastic);
            }
            else if (kind == ServiceKind.Fuel)
            {
                for (int i = 0; i < 2; i++)
                    Primitive(world, name + " barrel " + i, PrimitiveType.Cylinder,
                        new Vector3(x + .30f * i, .28f, r.yMin + .55f), new Vector3(.34f, .28f, .34f), color, Quaternion.identity, AirportStyle.Finish.Plastic);
                // 油泵立柱 + 悬管。
                Primitive(world, name + " pump post", PrimitiveType.Cube, new Vector3(x, .55f, r.yMax - .55f), new Vector3(.26f, 1.05f, .26f), Hex("FBF7EC"), Quaternion.identity, AirportStyle.Finish.Plastic);
                Primitive(world, name + " pump dial", PrimitiveType.Cube, new Vector3(x, .82f, r.yMax - .41f), new Vector3(.16f, .16f, .04f), color, Quaternion.identity);
                Primitive(world, name + " pump hose", PrimitiveType.Cylinder, new Vector3(x + .16f, .50f, r.yMax - .55f), new Vector3(.05f, .50f, .05f), DarkMetal, Quaternion.Euler(0.0f, 0.0f, -10.0f));
            }
            else
            {
                // 绳栏：两根立柱 + 一条下垂绳（细圆柱近似）。
                for (int i = 0; i < 2; i++)
                    Primitive(world, name + " rope post " + i, PrimitiveType.Cylinder,
                        new Vector3(x, .34f, r.center.y - .5f + i * 1.0f), new Vector3(.07f, .34f, .07f), DarkMetal, Quaternion.identity);
                Primitive(world, name + " rope", PrimitiveType.Cylinder,
                    new Vector3(x, .52f, r.center.y), new Vector3(.045f, .50f, .045f), color, Quaternion.Euler(90.0f, 0.0f, 0.0f));
            }
        }

        private static void BuildAprons(Transform world)
        {
            for (int stand = 0; stand < Level1Map.StandCount; stand++)
            {
                Rect r = Level1Map.Ramps[stand]; string name = "Ramp " + (stand + 1);
                Surface(world, name, r, .080f, ApronConcrete);
                var joints = new List<Vector3>(); var jointIndices = new List<int>();
                for (float x = r.xMin + 1.8f; x < r.xMax; x += 1.8f)
                    AddFlatDetail(joints, jointIndices, new Vector3(x, .082f, r.center.y), .016f, r.height - .25f);
                AddFlatDetail(joints, jointIndices, new Vector3(r.center.x, .082f, r.center.y), r.width - .25f, .016f);
                MeshObject(world, name + " concrete joints", joints.ToArray(), jointIndices.ToArray(), Hex("B8B8A9"));
                // v7 黄色机位框线。
                float t = .09f, y = .098f;
                Primitive(world, name + " box N", PrimitiveType.Cube, new Vector3(r.center.x, y, r.yMax - t * .5f), new Vector3(r.width - t, .012f, t), YellowLine, Quaternion.identity);
                Primitive(world, name + " box S", PrimitiveType.Cube, new Vector3(r.center.x, y, r.yMin + t * .5f), new Vector3(r.width - t, .012f, t), YellowLine, Quaternion.identity);
                Primitive(world, name + " box W", PrimitiveType.Cube, new Vector3(r.xMin + t * .5f, y, r.center.y), new Vector3(t, .012f, r.height - t), YellowLine, Quaternion.identity);
                Primitive(world, name + " box E", PrimitiveType.Cube, new Vector3(r.xMax - t * .5f, y, r.center.y), new Vector3(t, .012f, r.height - t), YellowLine, Quaternion.identity);

                Vector3 park = Level1Map.PlanePark(stand);
                Primitive(world, name + " center guide", PrimitiveType.Cube, park + Vector3.up * .096f, new Vector3(4.8f, .016f, .06f), YellowLine, Quaternion.identity);
                Primitive(world, name + " nose stop", PrimitiveType.Cube, park + new Vector3(-2.35f, .10f, 0), new Vector3(.07f, .018f, 1.25f), WhiteLine, Quaternion.identity);
                CreateLabel(world, "RAMP " + (stand + 1), new Vector3(r.xMin + 1.15f, .115f, r.yMax - .55f), DarkMetal, .075f);
                var dock = Level1Map.Dock(stand);
                Primitive(world, "Aircraft service point " + "ABC"[stand], PrimitiveType.Cylinder, dock + Vector3.up * .095f, new Vector3(.75f, .015f, .75f), Hex("80C7A6"), Quaternion.identity);
                CreateLabel(world, "SERVICE " + "ABC"[stand], dock + Vector3.up * .14f, DarkMetal, .06f);
                for (int side = 0; side < 2; side++) for (int i = 0; i < 4; i++)
                        ApronLight(world, name + " light " + side + " " + i, new Vector3(r.xMin + .4f + i * (r.width - .8f) / 3, .12f, side == 0 ? r.yMin + .18f : r.yMax - .18f));
            }
        }

        private static void BuildTaxiwayRunway(Transform world)
        {
            Transform group = new GameObject("taxiway group - aircraft only").transform; group.SetParent(world, false);
            for (int i = 0; i < Level1Map.TaxiwaySurfaces.Length; i++)
            {
                var source = Level1Map.TaxiwaySurfaces[i]; var polygon = new Vector2[source.Length];
                // Reverse SVG winding after flipping the design's y axis into world z.
                for (int j = 0; j < source.Length; j++) polygon[j] = new Vector2(source[source.Length - 1 - j].x, source[source.Length - 1 - j].z);
                ExtrudedPolygon(group, "Taxiway part " + i, polygon, .018f, .070f, RoadColor);
            }
            Rect runway = Level1Map.Runway; Surface(world, "Runway", runway, .075f, RunwayColor);
            for (float y = 32; y < 389; y += 38)
            {
                Vector3 p = Level1Map.FromDesign(821.5f, y);
                Primitive(world, "Runway center " + y, PrimitiveType.Cube, p + Vector3.up * .086f, new Vector3(.10f, .014f, .80f), WhiteLine, Quaternion.identity);
            }
            foreach (float y in new[] { 20f, 382f }) for (int i = -1; i <= 1; i++)
                {
                    var p = Level1Map.FromDesign(821.5f + i * 19, y);
                    Primitive(world, "Runway threshold " + y + " " + i, PrimitiveType.Cube, p + Vector3.up * .086f, new Vector3(.30f, .014f, .50f), WhiteLine, Quaternion.identity);
                }
            CreateLabel(world, "RUNWAY", Level1Map.FromDesign(821.5f, 207) + Vector3.up * .1f, WhiteLine, .14f);
        }

        /// <summary>锥桶、花丛、围栏与棕榈：让岛看起来有人在经营。</summary>
        private static void BuildDecorations(Transform world)
        {
            Transform group = new GameObject("decoration group").transform; group.SetParent(world, false);
            // 每个机位四角摆锥桶（v7 的停机坪氛围）。
            for (int stand = 0; stand < Level1Map.StandCount; stand++)
            {
                Rect r = Level1Map.Ramps[stand];
                TrafficCone(group, "Cone " + stand + " NW", new Vector3(r.xMin + .55f, .08f, r.yMax - .55f), Hex("F08A24"));
                TrafficCone(group, "Cone " + stand + " NE", new Vector3(r.xMax - .55f, .08f, r.yMax - .55f), Hex("F08A24"));
                TrafficCone(group, "Cone " + stand + " SW", new Vector3(r.xMin + .55f, .08f, r.yMin + .55f), Hex("F08A24"));
                TrafficCone(group, "Cone " + stand + " SE", new Vector3(r.xMax - .55f, .08f, r.yMin + .55f), Hex("F08A24"));
            }
            // 设施西侧花丛。
            FlowerBush(group, "Bush meals", new Vector3(-12.4f, 0.0f, 6.4f));
            FlowerBush(group, "Bush gate", new Vector3(-12.5f, 0.0f, 1.9f));
            FlowerBush(group, "Bush bags", new Vector3(-12.4f, 0.0f, -2.6f));
            FlowerBush(group, "Bush fuel", new Vector3(-12.5f, 0.0f, -6.7f));
            FlowerBush(group, "Bush shore N", new Vector3(2.0f, 0.0f, 7.3f));
            FlowerBush(group, "Bush shore S", new Vector3(-4.0f, 0.0f, -7.3f));

            BuildPalm(group, "Palm West North", new Vector3(-13.1f, -0.02f, 7.9f), 0.0f);
            BuildPalm(group, "Palm West South", new Vector3(-13.1f, -0.02f, -7.9f), 16.0f);
            BuildPalm(group, "Palm East North", new Vector3(21.5f, -0.02f, 7.9f), -13.0f);
            BuildPalm(group, "Palm East South", new Vector3(21.8f, -0.02f, -7.6f), 24.0f);
            BuildPalm(group, "Palm Beach", new Vector3(8.5f, -0.02f, 7.4f), 40.0f);
        }

        private static void FlowerBush(Transform parent, string name, Vector3 position)
        {
            Primitive(parent, name + " leaves", PrimitiveType.Sphere, position + new Vector3(0.0f, .20f, 0.0f), new Vector3(.58f, .40f, .58f), LeafLight, Quaternion.identity);
            Primitive(parent, name + " lobe A", PrimitiveType.Sphere, position + new Vector3(-.22f, .15f, .08f), new Vector3(.36f, .30f, .36f), Leaf, Quaternion.identity);
            Primitive(parent, name + " lobe B", PrimitiveType.Sphere, position + new Vector3(.20f, .14f, -.12f), new Vector3(.34f, .28f, .34f), Leaf, Quaternion.identity);
            Primitive(parent, name + " lobe C", PrimitiveType.Sphere, position + new Vector3(.06f, .17f, .22f), new Vector3(.30f, .26f, .30f), LeafLight, Quaternion.identity);
            Primitive(parent, name + " flower A", PrimitiveType.Sphere, position + new Vector3(-.16f, .38f, .10f), new Vector3(.12f, .10f, .12f), Hex("F2A0C4"), Quaternion.identity);
            Primitive(parent, name + " flower B", PrimitiveType.Sphere, position + new Vector3(.14f, .40f, -.08f), new Vector3(.11f, .09f, .11f), Hex("FFF1A8"), Quaternion.identity);
            Primitive(parent, name + " flower C", PrimitiveType.Sphere, position + new Vector3(.02f, .42f, .18f), new Vector3(.10f, .09f, .10f), Hex("F2A0C4"), Quaternion.identity);
        }

        private static void BuildPier(Transform world)
        {
            // The pier moved to the north shore so it no longer shares the
            // east edge with the runway.
            Primitive(world, "Pier Deck", PrimitiveType.Cube,
                new Vector3(4.0f, -0.30f, 9.6f), new Vector3(1.70f, 0.22f, 4.20f), Wood, Quaternion.identity);
            for (int i = 0; i < 8; i++)
            {
                float z = 8.05f + i * 0.53f;
                Primitive(world, "Pier Plank " + i, PrimitiveType.Cube,
                    new Vector3(4.0f, -0.17f, z), new Vector3(1.88f, 0.055f, 0.42f), Lighten(Wood, 0.10f), Quaternion.identity);
            }
            for (int i = 0; i < 5; i++)
            {
                float z = 8.15f + i * 0.85f;
                Primitive(world, "Pier Left Post " + i, PrimitiveType.Cylinder,
                    new Vector3(3.22f, -0.78f, z), new Vector3(0.12f, 1.00f, 0.12f), DarkMetal, Quaternion.identity);
                Primitive(world, "Pier Right Post " + i, PrimitiveType.Cylinder,
                    new Vector3(4.78f, -0.78f, z), new Vector3(0.12f, 1.00f, 0.12f), DarkMetal, Quaternion.identity);
            }
            Primitive(world, "Pier Lamp Pole", PrimitiveType.Cylinder,
                new Vector3(4.0f, 0.58f, 11.3f), new Vector3(0.055f, 1.75f, 0.055f), DarkMetal, Quaternion.identity);
            Primitive(world, "Pier Lamp", PrimitiveType.Sphere,
                new Vector3(4.0f, 1.48f, 11.3f), new Vector3(0.18f, 0.18f, 0.18f), Hex("#FFF1A8"), Quaternion.identity, AirportStyle.Finish.Glass);
            TextMesh dockLabel = CreateLabel(world, "PIER", new Vector3(4.0f, -0.15f, 9.0f), WhiteLine, 0.09f);
            dockLabel.transform.localRotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);
        }

        private static void BuildPalm(Transform world, string name, Vector3 position, float lean)
        {
            GameObject palm = new GameObject(name);
            Transform root = palm.transform;
            root.SetParent(world, false);
            root.localPosition = position;
            root.localRotation = Quaternion.Euler(0.0f, lean, 0.0f);

            Cone(root, "Palm Trunk", new Vector3(0.0f, 1.00f, 0.0f), 0.24f, 0.16f, 2.15f, 7, Trunk, Quaternion.Euler(0.0f, 0.0f, -4.0f));
            Primitive(root, "Palm Collar", PrimitiveType.Cylinder,
                new Vector3(0.0f, 2.06f, 0.0f), new Vector3(0.30f, 0.11f, 0.30f), Trunk, Quaternion.identity);
            // 椰子细节。
            Primitive(root, "Coconut A", PrimitiveType.Sphere, new Vector3(0.10f, 2.02f, 0.06f), new Vector3(0.11f, 0.11f, 0.11f), Hex("#6B4A2E"), Quaternion.identity);
            Primitive(root, "Coconut B", PrimitiveType.Sphere, new Vector3(-0.08f, 2.03f, -0.07f), new Vector3(0.10f, 0.10f, 0.10f), Hex("#6B4A2E"), Quaternion.identity);

            for (int i = 0; i < 7; i++)
            {
                float angle = i * (360.0f / 7.0f);
                Quaternion rotation = Quaternion.Euler(-12.0f, angle, 8.0f * Mathf.Sin(angle * Mathf.Deg2Rad));
                CreatePalmLeaf(root, "Leaf " + i, new Vector3(0.0f, 2.14f, 0.0f), rotation, i % 2 == 0 ? Leaf : LeafLight);
            }
        }

        private static void CreatePalmLeaf(Transform parent, string name, Vector3 position, Quaternion rotation, Color color)
        {
            const int segments = 6;
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float halfWidth = .25f * Mathf.Sin(t * Mathf.PI);
                float height = .35f * Mathf.Sin(t * Mathf.PI) - .26f * t;
                vertices.Add(new Vector3(-halfWidth, height, t * 1.55f));
                vertices.Add(new Vector3(halfWidth, height, t * 1.55f));
            }
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2;
                if (i > 0) { triangles.Add(a); triangles.Add(a + 2); triangles.Add(a + 1); }
                if (i < segments - 1) { triangles.Add(a + 1); triangles.Add(a + 2); triangles.Add(a + 3); }
            }
            // Two-sided thin leaves; underside has its own normal instead of disappearing.
            int frontCount = triangles.Count;
            for (int i = 0; i < frontCount; i += 3)
            { triangles.Add(triangles[i]); triangles.Add(triangles[i + 2]); triangles.Add(triangles[i + 1]); }
            GameObject leaf = MeshObject(parent, name, vertices.ToArray(), triangles.ToArray(), color);
            leaf.transform.localPosition = position;
            leaf.transform.localRotation = rotation;
        }

        private static void ApronLight(Transform parent, string name, Vector3 position)
        {
            Primitive(parent, name + " Stem", PrimitiveType.Cylinder,
                position + new Vector3(0.0f, -0.05f, 0.0f), new Vector3(0.035f, 0.18f, 0.035f), DarkMetal, Quaternion.identity);
            Primitive(parent, name + " Glow", PrimitiveType.Sphere,
                position + new Vector3(0.0f, 0.11f, 0.0f), new Vector3(0.11f, 0.11f, 0.11f), Hex("#FFF0A1"), Quaternion.identity, AirportStyle.Finish.Glass);
        }

        private static void TrafficCone(Transform parent, string name, Vector3 position, Color color)
        {
            Cone(parent, name + " Body", position + new Vector3(0.0f, 0.33f, 0.0f), 0.26f, 0.075f, 0.66f, 6, color, Quaternion.identity, AirportStyle.Finish.Plastic);
            Primitive(parent, name + " White Band", PrimitiveType.Cylinder,
                position + new Vector3(0.0f, 0.42f, 0.0f), new Vector3(0.17f, 0.035f, 0.17f), WhiteLine, Quaternion.identity);
            Primitive(parent, name + " Base", PrimitiveType.Cube,
                position + new Vector3(0.0f, 0.045f, 0.0f), new Vector3(0.46f, 0.09f, 0.32f), color, Quaternion.identity);
        }

        private static TextMesh CreateLabel(Transform parent, string text, Vector3 position, Color color, float size)
        {
            GameObject labelObject = new GameObject("World Label - " + text);
            Transform labelTransform = labelObject.transform;
            if (parent == null)
            {
                labelTransform.position = position;
            }
            else
            {
                labelTransform.SetParent(parent, false);
                labelTransform.localPosition = position;
            }
            labelTransform.localRotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);

            TextMesh label = labelObject.AddComponent<TextMesh>();
            label.text = text;
            label.color = color;
            label.characterSize = size;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.GetComponent<MeshRenderer>().sharedMaterial = label.font.material;
            label.fontSize = 64;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.richText = false;
            label.tabSize = 4;
            return label;
        }

        private static GameObject Primitive(Transform parent, string name, PrimitiveType type, Vector3 position,
            Vector3 scale, Color color, Quaternion rotation, AirportStyle.Finish finish = AirportStyle.Finish.Matte)
        {
            GameObject primitive = GameObject.CreatePrimitive(type);
            primitive.name = name;
            Transform transform = primitive.transform;
            transform.SetParent(parent, false);
            transform.localPosition = position;
            transform.localRotation = rotation;
            transform.localScale = scale;
            ApplyColor(primitive, color, finish);
            RemoveColliders(primitive);
            return primitive;
        }

        private static GameObject MeshObject(Transform parent, string name, Vector3[] vertices, int[] triangles, Color color, AirportStyle.Finish finish = AirportStyle.Finish.Matte)
        {
            GameObject meshObject = new GameObject(name);
            meshObject.transform.SetParent(parent, false);
            MeshFilter filter = meshObject.AddComponent<MeshFilter>();
            MeshRenderer renderer = meshObject.AddComponent<MeshRenderer>();
            Mesh mesh = new Mesh();
            mesh.name = name + " Mesh";
            // Hard-surface forms need separate face vertices: averaged normals across
            // roofs/wing edges produced diagonal dark bands on otherwise flat panels.
            var faceVertices = new Vector3[triangles.Length];
            var faceIndices = new int[triangles.Length];
            for (int i = 0; i < triangles.Length; i++) { faceVertices[i] = vertices[triangles[i]]; faceIndices[i] = i; }
            mesh.vertices = faceVertices;
            mesh.triangles = faceIndices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            filter.sharedMesh = mesh;
            renderer.sharedMaterial = AirportStyle.SharedMaterial(color, finish);
            return meshObject;
        }

        private static GameObject Cone(Transform parent, string name, Vector3 position, float bottomRadius,
            float topRadius, float height, int sides, Color color, Quaternion rotation, AirportStyle.Finish finish = AirportStyle.Finish.Matte)
        {
            if (sides < 3)
            {
                sides = 3;
            }

            Vector3[] vertices = new Vector3[sides * 2 + 2];
            int bottomCenter = sides * 2;
            int topCenter = bottomCenter + 1;
            for (int i = 0; i < sides; i++)
            {
                float angle = (Mathf.PI * 2.0f * i) / sides;
                float x = Mathf.Cos(angle);
                float z = Mathf.Sin(angle);
                vertices[i] = new Vector3(x * bottomRadius, -height * 0.5f, z * bottomRadius);
                vertices[sides + i] = new Vector3(x * topRadius, height * 0.5f, z * topRadius);
            }
            vertices[bottomCenter] = new Vector3(0.0f, -height * 0.5f, 0.0f);
            vertices[topCenter] = new Vector3(0.0f, height * 0.5f, 0.0f);

            int[] triangles = new int[sides * 12];
            int t = 0;
            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;
                triangles[t++] = i;
                triangles[t++] = next;
                triangles[t++] = sides + next;
                triangles[t++] = i;
                triangles[t++] = sides + next;
                triangles[t++] = sides + i;
                triangles[t++] = bottomCenter;
                triangles[t++] = next;
                triangles[t++] = i;
                triangles[t++] = topCenter;
                triangles[t++] = sides + i;
                triangles[t++] = sides + next;
            }

            for (int i = 0; i < triangles.Length; i += 3) { int v = triangles[i + 1]; triangles[i + 1] = triangles[i + 2]; triangles[i + 2] = v; }
            GameObject cone = MeshObject(parent, name, vertices, triangles, color, finish);
            cone.transform.localPosition = position;
            cone.transform.localRotation = rotation;
            return cone;
        }

        private static GameObject ExtrudedPolygon(Transform parent, string name, Vector2[] outline,
            float bottomY, float topY, Color color)
        {
            int count = outline.Length;
            Vector3[] vertices = new Vector3[count * 2];
            for (int i = 0; i < count; i++)
            {
                vertices[i] = new Vector3(outline[i].x, bottomY, outline[i].y);
                vertices[count + i] = new Vector3(outline[i].x, topY, outline[i].y);
            }

            int[] triangles = new int[(count - 2) * 6 + count * 6];
            int t = 0;
            for (int i = 1; i < count - 1; i++)
            {
                triangles[t++] = count;
                triangles[t++] = count + i + 1;
                triangles[t++] = count + i;
                triangles[t++] = 0;
                triangles[t++] = i;
                triangles[t++] = i + 1;
            }
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                triangles[t++] = i;
                triangles[t++] = count + next;
                triangles[t++] = next;
                triangles[t++] = i;
                triangles[t++] = count + i;
                triangles[t++] = count + next;
            }

            return MeshObject(parent, name, vertices, triangles, color);
        }

        private static GameObject ExtrudedPlanform(Transform parent, string name, Vector2[] outline,
            float bottomY, float topY, Color color)
        {
            // Planforms use x/z for their two-dimensional outline and y for thickness.
            return ExtrudedPolygon(parent, name, outline, bottomY, topY, color);
        }

        private static GameObject ExtrudedVerticalFin(Transform parent, string name, Vector2[] outline,
            float frontZ, float backZ, Color color)
        {
            int count = outline.Length;
            Vector3[] vertices = new Vector3[count * 2];
            for (int i = 0; i < count; i++)
            {
                vertices[i] = new Vector3(outline[i].x, outline[i].y, frontZ);
                vertices[count + i] = new Vector3(outline[i].x, outline[i].y, backZ);
            }

            int[] triangles = new int[(count - 2) * 6 + count * 6];
            int t = 0;
            for (int i = 1; i < count - 1; i++)
            {
                triangles[t++] = 0;
                triangles[t++] = i + 1;
                triangles[t++] = i;
                triangles[t++] = count;
                triangles[t++] = count + i;
                triangles[t++] = count + i + 1;
            }
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                // 侧面朝外（CCW 轮廓从 +z 看）：屋顶坡面/尾翼侧面才不会被背面剔除。
                triangles[t++] = i;
                triangles[t++] = count + next;
                triangles[t++] = count + i;
                triangles[t++] = i;
                triangles[t++] = next;
                triangles[t++] = count + next;
            }

            return MeshObject(parent, name, vertices, triangles, color, AirportStyle.Finish.Plastic);
        }

        private static void ApplyColor(GameObject gameObject, Color color, AirportStyle.Finish finish)
        {
            Renderer renderer = gameObject.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = AirportStyle.SharedMaterial(color, finish);
            }
        }

        private static void RemoveColliders(GameObject gameObject)
        {
            Collider[] colliders = gameObject.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (Application.isPlaying)
                {
                    Object.Destroy(colliders[i]);
                }
                else
                {
                    Object.DestroyImmediate(colliders[i]);
                }
            }
        }

        private static Color Darken(Color color, float amount)
        {
            return Color.Lerp(color, Color.black, Mathf.Clamp01(amount));
        }

        private static Color Lighten(Color color, float amount)
        {
            return Color.Lerp(color, Color.white, Mathf.Clamp01(amount));
        }
    }
}
