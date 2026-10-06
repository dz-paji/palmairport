using UnityEngine;

namespace IslandAirport
{
    public static partial class AirportWorld
    {
        /// <summary>
        /// M1 车辆工厂：按任务类型生成不同轮廓的玩具车辆。
        /// 餐车=厢式货箱；行李车=牵引车头+两节拖斗；油车=圆柱罐+胶管。
        /// 车头朝 +z；只返回根 Transform，货件由玩法层自行挂载显隐。
        /// </summary>
        public static Transform CreateCart(ServiceKind kind, string name, Vector3 position)
        {
            GameObject cartObject = new GameObject(string.IsNullOrEmpty(name) ? "Service Cart" : name);
            Transform cart = cartObject.transform;
            cart.position = position;
            cart.rotation = Quaternion.identity;
            Color color = AirportStyle.ServiceColor(kind);

            if (kind == ServiceKind.Baggage) BuildBaggageTrain(cart, color);
            else if (kind == ServiceKind.Fuel) BuildFuelTruck(cart, color);
            else BuildMealTruck(cart, color);

            return cart;
        }

        /// <summary>旧签名保留：按名称推断类型的通用货车（行李车造型）。</summary>
        public static Transform CreateCart(string name, Color color, Vector3 position)
        {
            GameObject cartObject = new GameObject(string.IsNullOrEmpty(name) ? "Service Cart" : name);
            Transform cart = cartObject.transform;
            cart.position = position;
            cart.rotation = Quaternion.identity;
            BuildMealTruck(cart, color);
            return cart;
        }

        /// <summary>
        /// 车载货件可视化（1.0.9）：餐车=保温餐箱堆，行李车=行李堆，油车=带箍油桶。
        /// 挂在车辆根节点下的固定位置，默认隐藏；玩法层只做显隐。无碰撞体。
        /// </summary>
        public static Transform CreateCargo(ServiceKind kind, Transform cart)
        {
            Transform cargo = new GameObject("Cargo " + kind).transform;
            cargo.SetParent(cart, false);
            // 各车型可见位置：餐车箱顶、行李车第一节拖斗内、油车泵箱顶。
            cargo.localPosition = kind == ServiceKind.Fuel ? new Vector3(0, 1.09f, -.95f)
                : kind == ServiceKind.Baggage ? new Vector3(0, .616f, -.55f) : new Vector3(0, 1.47f, -.30f);
            Color color = AirportStyle.ServiceColor(kind);
            Color cream = Hex("FBF7EC");
            if (kind == ServiceKind.Fuel)
            {
                // 横卧油桶（旋转体轴向 x），两道箍、端盖与标签。
                VehicleRevolved(cargo, "Drum", new Vector3(0, .22f, 0), new[]
                {
                    new Vector2(-.21f, .001f), new Vector2(-.21f, .19f), new Vector2(-.19f, .22f), new Vector2(-.06f, .22f),
                    new Vector2(.06f, .22f), new Vector2(.19f, .22f), new Vector2(.21f, .19f), new Vector2(.21f, .001f)
                }, 18, false, false, color, AirportStyle.Finish.Plastic, "fuel drum");
                foreach (float x in new[] { -.12f, .12f })
                    Primitive(cargo, "Drum hoop " + x, PrimitiveType.Cylinder, new Vector3(x, .22f, 0), new Vector3(.46f, .012f, .46f), Darken(color, .25f), Quaternion.Euler(0, 0, 90f));
                Primitive(cargo, "Drum cap", PrimitiveType.Cylinder, new Vector3(.215f, .30f, .06f), new Vector3(.07f, .012f, .07f), DarkMetal, Quaternion.Euler(0, 0, 90f));
                ModelBox(cargo, "Drum label", new Vector3(0, .22f, .222f), new Vector3(.16f, .12f, .012f), cream, .006f, Quaternion.identity);
                foreach (float x in new[] { -.14f, .14f })
                    ModelBox(cargo, "Drum chock " + x, new Vector3(x, .03f, 0), new Vector3(.06f, .06f, .36f), DarkMetal, .012f, Quaternion.identity);
            }
            else if (kind == ServiceKind.Baggage)
            {
                ModelBox(cargo, "Case A", new Vector3(-.16f, .12f, .02f), new Vector3(.34f, .24f, .40f), Hex("E8855A"), .035f, Quaternion.Euler(0, 6f, 0));
                ModelBox(cargo, "Case B", new Vector3(.16f, .11f, -.14f), new Vector3(.28f, .22f, .32f), Hex("6FBF7E"), .032f, Quaternion.Euler(0, -12f, 0));
                ModelBox(cargo, "Case C", new Vector3(.02f, .33f, 0), new Vector3(.30f, .20f, .32f), Hex("F2C94C"), .032f, Quaternion.Euler(0, 20f, 0));
                ModelBox(cargo, "Case C handle", new Vector3(.02f, .45f, 0), new Vector3(.12f, .035f, .04f), DarkMetal, .010f, Quaternion.Euler(0, 20f, 0));
                ModelBox(cargo, "Case A strap", new Vector3(-.16f, .12f, .02f), new Vector3(.35f, .25f, .05f), Darken(Hex("E8855A"), .2f), .010f, Quaternion.Euler(0, 6f, 0));
            }
            else
            {
                for (int i = 0; i < 2; i++)
                {
                    float y = .08f + i * .17f;
                    ModelBox(cargo, "Meal box " + i, new Vector3(0, y, 0), new Vector3(.52f, .15f, .44f), cream, .030f, Quaternion.Euler(0, i * 8f, 0));
                    ModelBox(cargo, "Meal lid " + i, new Vector3(0, y + .085f, 0), new Vector3(.54f, .035f, .46f), color, .012f, Quaternion.Euler(0, i * 8f, 0));
                    ModelBox(cargo, "Meal latch " + i, new Vector3(0, y, .23f), new Vector3(.10f, .06f, .018f), DarkMetal, .006f, Quaternion.Euler(0, i * 8f, 0));
                }
                ModelBox(cargo, "Tray", new Vector3(.05f, .36f, 0), new Vector3(.36f, .025f, .26f), Hex("B9C1C4"), .008f, Quaternion.Euler(0, -14f, 0));
                Primitive(cargo, "Cup", PrimitiveType.Cylinder, new Vector3(.12f, .41f, .05f), new Vector3(.07f, .045f, .07f), color, Quaternion.identity, AirportStyle.Finish.Plastic);
                Primitive(cargo, "Bun", PrimitiveType.Sphere, new Vector3(-.03f, .40f, -.04f), new Vector3(.11f, .07f, .11f), Hex("E9C37E"), Quaternion.identity);
            }
            cargo.gameObject.SetActive(false);
            return cargo;
        }

        static void BuildCab(Transform cart, Color body, float z, float width)
        {
            Color bodyShadow = Darken(body, 0.24f);
            // Separate rounded lower body and glass cabin retain the original seat envelope.
            ModelBox(cart, "Cab", new Vector3(0, .66f, z), new Vector3(width, .48f, .64f), body, .09f, Quaternion.identity);
            ModelBox(cart, "Cab Upper", new Vector3(0, .99f, z - .025f), new Vector3(width * .91f, .31f, .55f), body, .065f, Quaternion.identity);
            ModelBox(cart, "Cab Roof", new Vector3(0, 1.17f, z), new Vector3(width + .10f, .10f, .74f), bodyShadow, .045f, Quaternion.identity);
            ModelBox(cart, "Windshield", new Vector3(0, 1.00f, z + .257f), new Vector3(width * .74f, .22f, .026f), Window, .025f, Quaternion.identity, AirportStyle.Finish.Glass);
            ModelBox(cart, "Windshield Divider", new Vector3(0, 1.00f, z + .272f), new Vector3(.025f, .225f, .018f), bodyShadow, .006f, Quaternion.identity);
            ModelBox(cart, "Front Bumper", new Vector3(0, .40f, z + .35f), new Vector3(width + .14f, .14f, .11f), YellowLine, .04f, Quaternion.identity);
            ModelBox(cart, "Front Grille", new Vector3(0, .59f, z + .319f), new Vector3(width * .42f, .16f, .021f), DarkMetal, .015f, Quaternion.identity);
            for (int side = -1; side <= 1; side += 2)
            {
                ModelBox(cart, "Side Window " + side, new Vector3(side * (width * .455f - .005f), 1.0f, z - .035f), new Vector3(.024f, .21f, .33f), Window, .012f, Quaternion.identity, AirportStyle.Finish.Glass);
                ModelBox(cart, "Door Inset " + side, new Vector3(side * (width * .5f - .008f), .71f, z - .015f), new Vector3(.025f, .28f, .37f), bodyShadow, .012f, Quaternion.identity);
                ModelBox(cart, "Door Panel " + side, new Vector3(side * (width * .5f + .005f), .71f, z - .015f), new Vector3(.021f, .235f, .325f), body, .009f, Quaternion.identity);
                ModelBox(cart, "Door Handle " + side, new Vector3(side * (width * .5f + .018f), .79f, z - .10f), new Vector3(.018f, .03f, .10f), Hex("FBF7EC"), .009f, Quaternion.identity);
                Primitive(cart, side < 0 ? "Headlight L" : "Headlight R", PrimitiveType.Sphere,
                    new Vector3(side * width * .32f, .58f, z + .315f), new Vector3(.115f, .105f, .075f), Hex("FFF6C8"), Quaternion.identity, AirportStyle.Finish.Glass);
            }
        }

        static void BuildWheels(Transform cart, Vector3[] positions, float radius)
        {
            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 p = positions[i];
                float side = p.x < 0 ? -1 : 1;
                string suffix = p.x + " " + p.z;
                // An eight-ring tire profile gives real rounded shoulders and a recessed centre.
                VehicleRevolved(cart, "Wheel " + suffix, p, new[]
                {
                    new Vector2(-.11f, radius * .50f), new Vector2(-.11f, radius * .76f),
                    new Vector2(-.093f, radius * .91f), new Vector2(-.065f, radius),
                    new Vector2(.065f, radius), new Vector2(.093f, radius * .91f),
                    new Vector2(.11f, radius * .76f), new Vector2(.11f, radius * .50f)
                }, 20, false, true, Hex("263941"), AirportStyle.Finish.Matte);
                Primitive(cart, "Wheel Rim " + suffix, PrimitiveType.Cylinder,
                    p + new Vector3(side * .113f, 0, 0), new Vector3(radius * 1.08f, .025f, radius * 1.08f), Hex("D8DFD9"), Quaternion.Euler(0, 0, 90));
                Primitive(cart, "Hub " + suffix, PrimitiveType.Cylinder,
                    p + new Vector3(side * .144f, 0, 0), new Vector3(radius * .56f, .026f, radius * .56f), YellowLine, Quaternion.Euler(0, 0, 90), AirportStyle.Finish.Plastic);
                ModelBox(cart, "Fender " + suffix, p + new Vector3(-side * .03f, radius + .045f, 0),
                    new Vector3(.21f, .09f, radius * 2.30f), Hex("526268"), .037f, Quaternion.identity);
            }
        }

        static void BuildMealTruck(Transform cart, Color body)
        {
            Color bodyShadow = Darken(body, .24f);
            Color cream = Hex("FBF7EC");
            ModelBox(cart, "Chassis", new Vector3(0, .32f, 0), new Vector3(1.16f, .18f, 1.62f), DarkMetal, .04f, Quaternion.identity);
            ModelBox(cart, "Cargo Box", new Vector3(0, .92f, -.30f), new Vector3(1.10f, .90f, .92f), cream, .065f, Quaternion.identity);
            ModelBox(cart, "Cargo Box Stripe", new Vector3(0, 1.05f, -.30f), new Vector3(1.13f, .14f, .95f), body, .03f, Quaternion.identity);
            ModelBox(cart, "Cargo Box Roof", new Vector3(0, 1.42f, -.30f), new Vector3(1.16f, .10f, .98f), bodyShadow, .04f, Quaternion.identity);
            ModelBox(cart, "Sign Board", new Vector3(0, 1.44f, .10f), new Vector3(.66f, .40f, .08f), cream, .035f, Quaternion.identity);
            Primitive(cart, "Sign Plate", PrimitiveType.Cylinder, new Vector3(0, 1.44f, .15f), new Vector3(.20f, .03f, .20f), body, Quaternion.Euler(90, 0, 0));
            // Recessed serving shutters, sills and shallow horizontal slats read on both sides.
            for (int side = -1; side <= 1; side += 2)
            {
                ModelBox(cart, "Shutter Frame " + side, new Vector3(side * .553f, .91f, -.32f), new Vector3(.022f, .65f, .70f), bodyShadow, .009f, Quaternion.identity);
                ModelBox(cart, "Shutter Panel " + side, new Vector3(side * .568f, .93f, -.32f), new Vector3(.014f, .58f, .62f), Hex("E5E4D8"), .006f, Quaternion.identity);
                for (int rib = 0; rib < 4; rib++)
                    ModelBox(cart, "Shutter Slat " + side + " " + rib, new Vector3(side * .578f, .74f + rib * .12f, -.32f), new Vector3(.004f, .012f, .59f), Hex("ADB9B3"), .001f, Quaternion.identity);
                ModelBox(cart, "Shutter Sill " + side, new Vector3(side * .563f, .61f, -.32f), new Vector3(.032f, .055f, .72f), body, .013f, Quaternion.identity);
            }
            ModelBox(cart, "Rear Cargo Doors", new Vector3(0, .91f, -.763f), new Vector3(.90f, .68f, .018f), Hex("E8E7DA"), .009f, Quaternion.identity);
            ModelBox(cart, "Rear Door Seam", new Vector3(0, .91f, -.777f), new Vector3(.018f, .61f, .009f), Hex("879B98"), .004f, Quaternion.identity);
            ModelBox(cart, "Rear Bumper", new Vector3(0, .43f, -.774f), new Vector3(1.07f, .12f, .072f), bodyShadow, .028f, Quaternion.identity);
            BuildCab(cart, body, .55f, 1.06f);
            BuildWheels(cart, new[] { new Vector3(-.62f,.24f,-.55f), new Vector3(.62f,.24f,-.55f), new Vector3(-.62f,.24f,.55f), new Vector3(.62f,.24f,.55f) }, .20f);
        }

        static void BuildBaggageTrain(Transform cart, Color body)
        {
            Color bodyShadow = Darken(body, .24f);
            ModelBox(cart, "Chassis", new Vector3(0, .32f, .55f), new Vector3(1.10f, .18f, 1.10f), DarkMetal, .04f, Quaternion.identity);
            ModelBox(cart, "Engine Hood", new Vector3(0, .62f, .82f), new Vector3(.92f, .42f, .52f), bodyShadow, .085f, Quaternion.identity);
            ModelBox(cart, "Hood Vent", new Vector3(0, .72f, 1.075f), new Vector3(.52f, .16f, .017f), DarkMetal, .008f, Quaternion.identity);
            BuildCab(cart, body, .30f, 1.00f);
            for (int trailer = 0; trailer < 2; trailer++)
            {
                float z = -.55f - trailer * 1.15f;
                ModelBox(cart, "Trailer Tow Bar " + trailer, new Vector3(0, .41f, z + .57f), new Vector3(.13f, .09f, .35f), DarkMetal, .025f, Quaternion.identity);
                ModelBox(cart, "Trailer Bed " + trailer, new Vector3(0, .52f, z), new Vector3(1.02f, .14f, .95f), bodyShadow, .035f, Quaternion.identity);
                ModelBox(cart, "Trailer Inner Floor " + trailer, new Vector3(0, .602f, z), new Vector3(.89f, .028f, .80f), Hex("A4BEC5"), .01f, Quaternion.identity);
                ModelBox(cart, "Trailer Rim L " + trailer, new Vector3(-.49f, .70f, z), new Vector3(.06f, .30f, .95f), body, .025f, Quaternion.identity);
                ModelBox(cart, "Trailer Rim R " + trailer, new Vector3(.49f, .70f, z), new Vector3(.06f, .30f, .95f), body, .025f, Quaternion.identity);
                ModelBox(cart, "Trailer Rim B " + trailer, new Vector3(0, .70f, z - .45f), new Vector3(1.02f, .30f, .06f), body, .025f, Quaternion.identity);
                for (int side = -1; side <= 1; side += 2)
                    ModelBox(cart, "Trailer Edge Rail " + trailer + " " + side, new Vector3(side * .49f, .827f, z), new Vector3(.055f, .045f, .92f), Hex("D7E9E8"), .02f, Quaternion.identity);
                var suitcase = ModelBox(cart, "Spare Suitcase " + trailer, new Vector3(.18f, .72f, z + .18f), new Vector3(.34f, .26f, .22f), Hex("E8855A"), .045f, Quaternion.Euler(0, 12, 0));
                ModelBox(suitcase.transform, "Suitcase Strap", new Vector3(0, .006f, 0), new Vector3(.045f, .263f, .223f), Hex("B2603F"), .012f, Quaternion.identity);
                ModelBox(suitcase.transform, "Suitcase Handle", new Vector3(0, .134f, 0), new Vector3(.12f, .027f, .045f), DarkMetal, .01f, Quaternion.identity);
                BuildWheels(cart, new[] { new Vector3(-.56f, .20f, z), new Vector3(.56f, .20f, z) }, .17f);
            }
            BuildWheels(cart, new[] { new Vector3(-.58f,.24f,.30f), new Vector3(.58f,.24f,.30f), new Vector3(-.58f,.24f,.85f), new Vector3(.58f,.24f,.85f) }, .20f);
        }

        static void BuildFuelTruck(Transform cart, Color body)
        {
            ModelBox(cart, "Chassis", new Vector3(0, .32f, -.05f), new Vector3(1.16f, .18f, 1.75f), DarkMetal, .04f, Quaternion.identity);
            // A continuous lathed tank has rounded end caps rather than the old flat cylinder ends.
            VehicleRevolved(cart, "Tank", new Vector3(0, .98f, -.28f), new[]
            {
                new Vector2(-.52f, 0), new Vector2(-.51f, .14f), new Vector2(-.47f, .28f),
                new Vector2(-.40f, .38f), new Vector2(-.30f, .43f), new Vector2(.30f, .43f),
                new Vector2(.40f, .38f), new Vector2(.47f, .28f), new Vector2(.51f, .14f), new Vector2(.52f, 0)
            }, 28, true, false, Hex("F4F1E4"), AirportStyle.Finish.Plastic);
            Primitive(cart, "Tank Band A", PrimitiveType.Cylinder, new Vector3(0, .98f, -.52f), new Vector3(.89f, .035f, .89f), body, Quaternion.Euler(90, 0, 0));
            Primitive(cart, "Tank Band B", PrimitiveType.Cylinder, new Vector3(0, .98f, -.04f), new Vector3(.89f, .035f, .89f), body, Quaternion.Euler(90, 0, 0));
            Primitive(cart, "Tank Hatch", PrimitiveType.Cylinder, new Vector3(0, 1.48f, -.28f), new Vector3(.22f, .10f, .22f), DarkMetal, Quaternion.identity);
            ModelBox(cart, "Hatch Handle", new Vector3(0, 1.575f, -.28f), new Vector3(.12f, .01f, .05f), body, .004f, Quaternion.identity);
            ModelBox(cart, "Tank Saddle", new Vector3(0, .54f, -.28f), new Vector3(.64f, .13f, .81f), Darken(body, .24f), .04f, Quaternion.identity);
            ModelBox(cart, "Pump Box", new Vector3(0, .78f, -.95f), new Vector3(.86f, .62f, .30f), body, .055f, Quaternion.identity);
            ModelBox(cart, "Pump Panel", new Vector3(0, .86f, -1.094f), new Vector3(.57f, .33f, .012f), DarkMetal, .006f, Quaternion.identity);
            ModelBox(cart, "Pump Display", new Vector3(-.09f, .92f, -1.098f), new Vector3(.25f, .105f, .003f), Hex("B5DED6"), .001f, Quaternion.identity, AirportStyle.Finish.Glass);
            Primitive(cart, "Pump Dial", PrimitiveType.Cylinder, new Vector3(.17f, .87f, -1.096f), new Vector3(.105f, .004f, .105f), Hex("FBF7EC"), Quaternion.Euler(90, 0, 0));
            VehicleHoseCoil(cart, "Hose A", new Vector3(.478f, .83f, -.95f));
            ModelBox(cart, "Hose B", new Vector3(.494f, .68f, -.87f), new Vector3(.042f, .19f, .045f), DarkMetal, .017f, Quaternion.Euler(15, 0, 0));
            ModelBox(cart, "Hose Nozzle", new Vector3(.494f, .78f, -.875f), new Vector3(.046f, .055f, .13f), Hex("D8DFD9"), .014f, Quaternion.identity);
            BuildCab(cart, body, .72f, 1.06f);
            BuildWheels(cart, new[] { new Vector3(-.62f,.24f,-.62f), new Vector3(.62f,.24f,-.62f), new Vector3(-.62f,.24f,.72f), new Vector3(.62f,.24f,.72f) }, .20f);
        }

        // Cache the handful of tire, tank and hose meshes across all runtime vehicles.
        static readonly System.Collections.Generic.Dictionary<string, Mesh> VehicleMeshes = new System.Collections.Generic.Dictionary<string, Mesh>();

        static void VehicleRevolved(Transform parent, string name, Vector3 position, Vector2[] profile,
            int segments, bool alongZ, bool closed, Color color, AirportStyle.Finish finish, string keyOverride = null)
        {
            string key = keyOverride ?? (alongZ ? "tank" : "tire" + profile[3].y.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Mesh mesh;
            if (!VehicleMeshes.TryGetValue(key, out mesh) || !mesh)
            {
                var vertices = new System.Collections.Generic.List<Vector3>();
                var triangles = new System.Collections.Generic.List<int>();
                for (int ring = 0; ring < profile.Length; ring++)
                for (int s = 0; s < segments; s++)
                {
                    float a = s * Mathf.PI * 2 / segments;
                    float c = Mathf.Cos(a) * profile[ring].y, d = Mathf.Sin(a) * profile[ring].y;
                    vertices.Add(alongZ ? new Vector3(c, d, profile[ring].x) : new Vector3(profile[ring].x, c, d));
                }
                for (int ring = 0; ring < (closed ? profile.Length : profile.Length - 1); ring++)
                for (int s = 0; s < segments; s++)
                {
                    int a = ring * segments + s, b = ring * segments + (s + 1) % segments;
                    int c = ((ring + 1) % profile.Length) * segments + s, d = ((ring + 1) % profile.Length) * segments + (s + 1) % segments;
                    triangles.Add(a); triangles.Add(b); triangles.Add(c);
                    triangles.Add(b); triangles.Add(d); triangles.Add(c);
                }
                mesh = CreateModelMesh("Rounded vehicle " + key, vertices, triangles);
                VehicleMeshes[key] = mesh;
            }
            VehicleMeshPart(parent, name, position, mesh, color, finish);
        }

        static void VehicleHoseCoil(Transform parent, string name, Vector3 position)
        {
            Mesh mesh;
            if (!VehicleMeshes.TryGetValue("hose coil", out mesh) || !mesh)
            {
                const int segments = 24, sides = 6;
                var vertices = new System.Collections.Generic.List<Vector3>();
                var triangles = new System.Collections.Generic.List<int>();
                for (int s = 0; s < segments; s++)
                for (int t = 0; t < sides; t++)
                {
                    float a = s * Mathf.PI * 2 / segments, b = t * Mathf.PI * 2 / sides;
                    float tube = .027f * Mathf.Cos(b);
                    vertices.Add(new Vector3(.027f * Mathf.Sin(b), (.16f + tube) * Mathf.Cos(a), (.115f + tube) * Mathf.Sin(a)));
                }
                for (int s = 0; s < segments; s++)
                for (int t = 0; t < sides; t++)
                {
                    int a = s * sides + t, b = s * sides + (t + 1) % sides;
                    int c = ((s + 1) % segments) * sides + t, d = ((s + 1) % segments) * sides + (t + 1) % sides;
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
                mesh = CreateModelMesh("Vehicle hose coil", vertices, triangles);
                VehicleMeshes["hose coil"] = mesh;
            }
            VehicleMeshPart(parent, name, position, mesh, Hex("263941"), AirportStyle.Finish.Matte);
        }

        static void VehicleMeshPart(Transform parent, string name, Vector3 position, Mesh mesh, Color color, AirportStyle.Finish finish)
        {
            var item = new GameObject(name);
            item.transform.SetParent(parent, false);
            item.transform.localPosition = position;
            item.AddComponent<MeshFilter>().sharedMesh = mesh;
            item.AddComponent<MeshRenderer>().sharedMaterial = AirportStyle.SharedMaterial(color, finish);
        }
    }
}
