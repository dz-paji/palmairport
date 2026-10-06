using System.Collections.Generic;
using UnityEngine;

namespace IslandAirport
{
    public static class CabinWorld
    {
        static readonly Color Navy = AirportStyle.Hex("203B58");
        static readonly Color SeatBlue = AirportStyle.Hex("246E91");
        static readonly Color SeatLight = AirportStyle.Hex("418FA8");
        static readonly Color Wine = AirportStyle.Hex("8E2D3F");
        static readonly Color WineLight = AirportStyle.Hex("B64851");
        static readonly Color Shell = AirportStyle.Hex("DCDCCD");
        static readonly Color Trim = AirportStyle.Hex("AB9C83");
        static readonly Color Carpet = AirportStyle.Hex("405967");
        static readonly Color CarpetLight = AirportStyle.Hex("597481");
        static readonly Color Lamp = AirportStyle.Hex("F3D29A");
        static readonly Color Window = AirportStyle.Hex("BFE4EC");
        static readonly Color WarmGlow = AirportStyle.Hex("FFE2B5");
        static readonly Color AisleGlow = AirportStyle.Hex("8FE3E0");
        static readonly Color Ivory = AirportStyle.Hex("E7E4D7");
        static readonly Color Frame = AirportStyle.Hex("86989B");
        static readonly Color Wood = AirportStyle.Hex("AA8160");
        static readonly Color DeepTeal = AirportStyle.Hex("315863");
        // Meshes are shared by dimensions; all seating rows reuse the same tiny meshes.
        static readonly Dictionary<Vector4, Mesh> BevelMeshes = new Dictionary<Vector4, Mesh>();
        static readonly Dictionary<Vector4, Mesh> WindowMeshes = new Dictionary<Vector4, Mesh>();

        public static Transform Build(Transform parent = null)
        {
            var rootObject = new GameObject("Cabin World");
            Transform root = rootObject.transform;
            if (parent) root.SetParent(parent, false);

            BuildShell(root);
            BuildWindows(root);
            BuildBerths(root);
            BuildEconomySeats(root);
            BuildGalley(root);
            BuildCockpit(root);
            BuildLamps(root);
            return root;
        }

        /// <summary>
        /// 机舱光照：高侧向主光 + 软阴影（外壳不投影）、暖色环境三色光、轻雾增景深、
        /// 两盏卧铺暖点光。与机场 ApplyEnvironment 独立，雾在机场侧关闭。
        /// </summary>
        public static Light SetupLighting(Transform parent = null)
        {
            Light sun = null;
            if (parent)
            {
                foreach (var light in parent.GetComponentsInChildren<Light>(true))
                    if (light.type == LightType.Directional) { sun = light; break; }
            }
            if (!sun)
            {
                var sunObject = new GameObject("Cabin softbox light");
                if (parent) sunObject.transform.SetParent(parent, false);
                sun = sunObject.AddComponent<Light>();
                sun.type = LightType.Directional;
            }
            sun.transform.rotation = Quaternion.Euler(56, -42, 0);
            sun.color = AirportStyle.Hex("FFF1DA");
            sun.intensity = 0.95f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.42f;
            sun.shadowBias = 0.05f;
            sun.shadowNormalBias = 0.5f;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = AirportStyle.Hex("E8DECC");
            RenderSettings.ambientEquatorColor = AirportStyle.Hex("C8C0B4");
            RenderSettings.ambientGroundColor = AirportStyle.Hex("6C6A68");
            RenderSettings.ambientIntensity = 0.85f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = AirportStyle.Hex("E9E2D4");
            RenderSettings.fogStartDistance = 30f;
            RenderSettings.fogEndDistance = 95f;
            QualitySettings.shadowDistance = 45f;
            QualitySettings.antiAliasing = 4;

            if (parent)
            {
                PointLight(parent, "Berth lamp left", new Vector3(-3.45f, 3.6f, 17.2f), WarmGlow, 9f, 1.1f);
                PointLight(parent, "Berth lamp right", new Vector3(3.45f, 3.6f, 17.2f), WarmGlow, 9f, 1.1f);
                PointLight(parent, "Galley lamp", new Vector3(-5.1f, 5.6f, 24.6f), WarmGlow, 8f, 0.8f);
            }
            return sun;
        }

        static void PointLight(Transform parent, string name, Vector3 position, Color color, float range, float intensity)
        {
            Transform existing = parent.Find(name);
            Light light = existing ? existing.GetComponent<Light>() : null;
            if (!light)
            {
                var lightObject = new GameObject(name);
                lightObject.transform.SetParent(parent, false);
                light = lightObject.AddComponent<Light>();
            }
            light.transform.localPosition = position;
            light.type = LightType.Point;
            light.color = color;
            light.range = range;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
        }

        public static void ConfigureCamera(Camera camera)
        {
            if (!camera) return;
            // 更低更近的机位：少看天花板，多看卧铺与座椅；UI 面板之外的中左区域是主景。
            camera.transform.position = new Vector3(-0.6f, 5.9f, -8.2f);
            camera.transform.rotation = Quaternion.LookRotation(new Vector3(0.2f, 2.3f, 17f) - camera.transform.position, Vector3.up);
            camera.fieldOfView = 40;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100;
            camera.backgroundColor = AirportStyle.Hex("E9E2D4");
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.allowHDR = false;
            camera.allowMSAA = true;
        }

        static void BuildShell(Transform root)
        {
            Block(root, "Cabin floor", new Vector3(0, -0.35f, 12), new Vector3(17.4f, 0.55f, 32), Carpet);
            // 地毯横纹：每排座位一道浅色条，给大面积地面一点节奏。
            for (float z = -3f; z < 28f; z += 2.4f)
                Block(root, "Carpet stripe", new Vector3(0, -0.072f, z), new Vector3(17.2f, 0.012f, 0.55f), AirportStyle.Hex("4B6473"));
            Block(root, "Center aisle", new Vector3(0, -0.055f, 12), new Vector3(2.25f, 0.035f, 31), CarpetLight);
            Block(root, "Aisle runner", new Vector3(0, -0.03f, 12), new Vector3(1.52f, 0.025f, 31), DeepTeal);
            NoShadow(Block(root, "Left cabin wall", new Vector3(-8.55f, 4.6f, 12), new Vector3(0.55f, 9.6f, 32), Shell));
            NoShadow(Block(root, "Right cabin wall", new Vector3(8.55f, 4.6f, 12), new Vector3(0.55f, 9.6f, 32), Shell));
            NoShadow(Block(root, "Cabin ceiling cap", new Vector3(0, 10.3f, 12), new Vector3(17.5f, 0.4f, 32), AirportStyle.Hex("A9B5B2")));
            NoShadow(Block(root, "Far cabin bulkhead", new Vector3(0, 4.5f, 28.25f), new Vector3(17, 9, 0.4f), Shell));
            // 拱形天花板：沿圆弧排布的 10 块板，带板缝与两条暖光带。
            const int panels = 10;
            const float radius = 22.3f;
            float centerY = 9.65f - radius;
            float angleSpan = Mathf.Asin(8.35f / radius) * 2f;
            float panelWidth = radius * angleSpan / panels + 0.03f;
            for (int i = 0; i < panels; i++)
            {
                float a = -angleSpan * 0.5f + angleSpan * (i + 0.5f) / panels;
                Vector3 p = new Vector3(Mathf.Sin(a) * radius, centerY + Mathf.Cos(a) * radius, 12);
                var panel = Block(root, "Ceiling panel " + i, p, new Vector3(panelWidth, 0.16f, 32), i % 2 == 0 ? AirportStyle.Hex("E3E0D3") : AirportStyle.Hex("DAD7C9"));
                panel.transform.localRotation = Quaternion.Euler(0, 0, -a * Mathf.Rad2Deg);
                NoShadow(panel);
                if (i > 0)
                {
                    float sa = -angleSpan * 0.5f + angleSpan * i / panels;
                    var seam = Block(root, "Ceiling seam " + i, new Vector3(Mathf.Sin(sa) * radius, centerY + Mathf.Cos(sa) * (radius - 0.09f), 12), new Vector3(0.03f, 0.05f, 31.6f), Frame);
                    seam.transform.localRotation = Quaternion.Euler(0, 0, -sa * Mathf.Rad2Deg);
                    NoShadow(seam);
                }
            }
            foreach (float x in new[] { -3.9f, 3.9f })
            {
                float a = Mathf.Asin(x / radius);
                var strip = Block(root, "Ceiling light strip", new Vector3(x, centerY + Mathf.Cos(a) * (radius - 0.12f), 12.5f), new Vector3(0.28f, 0.06f, 29f), WarmGlow, AirportStyle.Finish.Glow);
                strip.transform.localRotation = Quaternion.Euler(0, 0, -a * Mathf.Rad2Deg);
                NoShadow(strip);
            }
            for (int side = -1; side <= 1; side += 2)
            {
                NoShadow(Block(root, "Cabin lower wall lining", new Vector3(side * 8.23f, 1.6f, 12), new Vector3(0.13f, 3.25f, 32), AirportStyle.Hex("9DB4B0")));
                Block(root, "Cabin timber belt", new Vector3(side * 8.1f, 3.36f, 12), new Vector3(0.15f, 0.18f, 32), Wood);
                Block(root, "Cabin floor skirting", new Vector3(side * 8.1f, 0.14f, 12), new Vector3(0.18f, 0.25f, 32), DeepTeal);
                Block(root, "Aisle edge light", new Vector3(side * 1.08f, -0.02f, 12), new Vector3(0.06f, 0.03f, 31), AisleGlow, AirportStyle.Finish.Glow);
                NoShadow(BeveledBlock(root, side < 0 ? "Left overhead bin" : "Right overhead bin", new Vector3(side * 6.9f, 7.0f, 12), new Vector3(2.4f, 1.15f, 32), Ivory, 0.14f));
                NoShadow(Block(root, "Overhead bin underside", new Vector3(side * 6.85f, 6.4f, 12), new Vector3(2.1f, 0.09f, 31.6f), Frame));
                Block(root, "Overhead bin timber lip", new Vector3(side * 5.65f, 6.51f, 12), new Vector3(0.11f, 0.16f, 31.6f), Wood);
                Block(root, "Overhead bin wash light", new Vector3(side * 5.75f, 6.39f, 12), new Vector3(0.14f, 0.03f, 31.4f), WarmGlow, AirportStyle.Finish.Glow);
                for (int bay = 0; bay < 7; bay++)
                {
                    float z = -1.8f + bay * 4.6f;
                    Block(root, "Overhead bin seam", new Vector3(side * 5.688f, 7.0f, z), new Vector3(0.027f, 0.82f, 0.035f), Frame);
                    BeveledBlock(root, "Overhead bin latch", new Vector3(side * 5.66f, 6.72f, z + 2.05f), new Vector3(0.07f, 0.1f, 0.45f), DeepTeal, 0.025f);
                }
            }
        }

        static GameObject NoShadow(GameObject item)
        {
            // 外壳只接收阴影，不投影：否则封闭机舱里方向光会被天花板整体挡住。
            var renderer = item.GetComponent<Renderer>();
            if (renderer) renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return item;
        }

        static void BuildWindows(Transform root)
        {
            for (int row = 0; row < 5; row++)
            {
                float z = 3.1f + row * 4.65f;
                for (int side = -1; side <= 1; side += 2)
                {
                    float x = side * 8.17f;
                    WindowPanel(root, "Window surround", new Vector3(x, 5.1f, z), new Vector3(0.18f, 2.42f, 2.72f), 0.48f, Ivory);
                    WindowPanel(root, "Window inset gasket", new Vector3(x - side * 0.115f, 5.1f, z), new Vector3(0.055f, 2.12f, 2.39f), 0.4f, Frame);
                    NoShadow(WindowPanel(root, "Window glass", new Vector3(x - side * 0.155f, 5.1f, z), new Vector3(0.035f, 1.95f, 2.22f), 0.36f, Window, AirportStyle.Finish.Glow));
                    // A few flat, inset shapes give the opaque glass a calm sea view.
                    NoShadow(WindowPanel(root, "Window sea", new Vector3(x - side * 0.18f, 4.67f, z), new Vector3(0.012f, 0.78f, 2.04f), 0.25f, AirportStyle.Hex("5FB3BE"), AirportStyle.Finish.Glow));
                    NoShadow(Block(root, "Window horizon", new Vector3(x - side * 0.195f, 5.02f, z), new Vector3(0.014f, 0.045f, 1.94f), AirportStyle.Hex("F2F7F2"), AirportStyle.Finish.Glow));
                    NoShadow(WindowPanel(root, "Window cloud", new Vector3(x - side * 0.195f, 5.53f, z + 0.3f), new Vector3(0.014f, 0.18f, 0.78f), 0.09f, Ivory, AirportStyle.Finish.Glow));
                    NoShadow(WindowPanel(root, "Window sun", new Vector3(x - side * 0.195f, 5.62f, z - 0.55f), new Vector3(0.012f, 0.30f, 0.30f), 0.15f, AirportStyle.Hex("FFE39A"), AirportStyle.Finish.Glow));
                    Block(root, "Window blind rail", new Vector3(x - side * 0.17f, 6.28f, z), new Vector3(0.09f, 0.12f, 2.5f), Wood);
                    BeveledBlock(root, "Window lower trim", new Vector3(x - side * 0.16f, 3.85f, z), new Vector3(0.28f, 0.14f, 2.7f), Wood, 0.05f);
                }
            }
        }

        static void BuildBerths(Transform root)
        {
            BuildBerth(root, "Left business berth", -3.45f, 16.5f, true);
            BuildBerth(root, "Right business berth", 3.45f, 16.5f, false);
            BeveledBlock(root, "Left berth privacy wing", new Vector3(-5.55f, 1.55f, 16.4f), new Vector3(0.32f, 2.8f, 4.7f), DeepTeal, 0.1f);
            BeveledBlock(root, "Right berth privacy wing", new Vector3(5.55f, 1.55f, 16.4f), new Vector3(0.32f, 2.8f, 4.7f), DeepTeal, 0.1f);
            AddRestingGuest(root, "Resting guest", new Vector3(-3.45f, 1.48f, 16.05f), AirportStyle.Player2);
            AddRestingGuest(root, "Bot guest", new Vector3(3.45f, 1.48f, 16.05f), AirportStyle.Player1);
            AddRestingGuest(root, "Local partner", new Vector3(3.45f, 1.48f, 16.05f), AirportStyle.Player2);
            root.Find("Bot guest").gameObject.SetActive(false);
            root.Find("Local partner").gameObject.SetActive(false);
            AddOpenSeatOutline(root, new Vector3(3.45f, 2.65f, 16.4f));
            Block(root, "Player name plate", PlayerNamePosition, new Vector3(2.5f, 0.58f, 0.18f), Navy);
        }

        public static readonly Vector3 PlayerNamePosition = new Vector3(-3.45f, 3.35f, 13.75f);

        public static void SetPlayerColor(Transform root, int colorIndex)
        {
            if (!root) return;
            Transform old = root.Find("Resting guest");
            bool active = !old || old.gameObject.activeSelf;
            if (old)
            {
                // Destroy is deferred in play mode; rename so the Find below returns the new guest.
                old.name = "Resting guest (retired)";
                if (Application.isPlaying) Object.Destroy(old.gameObject); else Object.DestroyImmediate(old.gameObject);
            }
            AddRestingGuest(root, "Resting guest", new Vector3(-3.45f, 1.48f, 16.05f), PlayerColor(colorIndex));
            root.Find("Resting guest").gameObject.SetActive(active);
        }

        public static void SetPartnerAppearance(Transform root, bool visible, bool bot)
        {
            if (!root) return;
            Transform outline = root.Find("Partner open seat outline");
            Transform botGuest = root.Find("Bot guest");
            Transform localGuest = root.Find("Local partner");
            if (outline) outline.gameObject.SetActive(!visible);
            if (botGuest) botGuest.gameObject.SetActive(visible && bot);
            if (localGuest) localGuest.gameObject.SetActive(visible && !bot);
        }

        public static Color PlayerColor(int colorIndex)
        {
            Color[] colors = { AirportStyle.Hex("199FA7"), AirportStyle.Hex("E5585B"), AirportStyle.Hex("F0A51B"), AirportStyle.Hex("7D49C7"), AirportStyle.Player1, AirportStyle.Player2 };
            return colorIndex >= 0 && colorIndex < colors.Length ? colors[colorIndex] : colors[0];
        }

        static void BuildBerth(Transform root, string name, float x, float z, bool occupied)
        {
            Color upholstery = occupied ? Wine : AirportStyle.Hex("773143");
            BeveledBlock(root, name + " base", new Vector3(x, 0.62f, z), new Vector3(4.45f, 0.55f, 5.0f), DeepTeal, 0.12f);
            BeveledBlock(root, name + " mattress", new Vector3(x, 1.05f, z - 0.12f), new Vector3(3.95f, 0.42f, 4.55f), upholstery, 0.14f);
            BeveledBlock(root, name + " headrest", new Vector3(x, 1.42f, z + 1.55f), new Vector3(3.55f, 0.42f, 0.85f), WineLight, 0.12f);
            BeveledBlock(root, name + " pillow", new Vector3(x, occupied ? 1.52f : 1.38f, z + 0.94f), new Vector3(1.5f, occupied ? 0.5f : 0.28f, 0.86f), Ivory, 0.12f);
            BeveledBlock(root, name + " foot rail", new Vector3(x, 1.32f, z - 2.22f), new Vector3(3.85f, 0.52f, 0.28f), Wood, 0.08f);
            float throwY = occupied ? 1.57f : 1.29f;
            BeveledBlock(root, name + " folded throw", new Vector3(x, throwY, z - 1.38f), new Vector3(3.55f, 0.08f, 0.84f), SeatLight, 0.035f);
            Block(root, name + " throw border", new Vector3(x, throwY + 0.047f, z - 1.68f), new Vector3(3.32f, 0.014f, 0.045f), Ivory);
            for (int side = -1; side <= 1; side += 2)
            {
                BeveledBlock(root, name + " arm rest", new Vector3(x + side * 2.08f, 1.08f, z), new Vector3(0.38f, 0.86f, 5.0f), Wood, 0.1f);
                Block(root, name + " console", new Vector3(x + side * 2.35f, 1.55f, z - 0.5f), new Vector3(0.34f, 0.16f, 0.72f), AirportStyle.Hex("253A4D"));
                Block(root, name + " lamp", new Vector3(x + side * 2.32f, 2.0f, z + 1.7f), new Vector3(0.17f, 0.48f, 0.17f), AirportStyle.Hex("B88758"));
                Sphere(root, name + " lamp shade", new Vector3(x + side * 2.32f, 2.36f, z + 1.7f), new Vector3(0.42f, 0.30f, 0.42f), WarmGlow, AirportStyle.Finish.Glow);
                Block(root, name + " console screen", new Vector3(x + side * 2.35f, 1.64f, z - 0.5f), new Vector3(0.22f, 0.012f, 0.46f), AisleGlow, AirportStyle.Finish.Glow);
            }
        }

        static void BuildEconomySeats(Transform root)
        {
            float[] rows = { 1.2f, 6.0f, 10.2f };
            for (int r = 0; r < rows.Length; r++)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    for (int column = 0; column < 3; column++)
                    {
                        float x = side * (2.55f + column * 1.48f);
                        CreateEconomySeat(root, "Economy seat " + r + " " + side + " " + column, x, rows[r]);
                    }
                }
            }
        }

        static void CreateEconomySeat(Transform root, string name, float x, float z)
        {
            Block(root, name + " pedestal", new Vector3(x, 0.16f, z), new Vector3(0.2f, 0.25f, 0.2f), Frame);
            BeveledBlock(root, name + " seat frame", new Vector3(x, 0.31f, z), new Vector3(1.24f, 0.15f, 1.14f), DeepTeal, 0.04f);
            BeveledBlock(root, name + " cushion", new Vector3(x, 0.58f, z), new Vector3(1.26f, 0.5f, 1.24f), SeatBlue, 0.1f);
            BeveledBlock(root, name + " back", new Vector3(x, 1.35f, z + 0.43f), new Vector3(1.25f, 1.38f, 0.43f), SeatLight, 0.1f);
            BeveledBlock(root, name + " back pocket", new Vector3(x, 1.0f, z + 0.68f), new Vector3(0.9f, 0.5f, 0.08f), SeatBlue, 0.03f);
            Block(root, name + " pocket magazine", new Vector3(x - 0.1f, 1.22f, z + 0.69f), new Vector3(0.42f, 0.14f, 0.05f), AirportStyle.Hex("F0A51B"));
            Block(root, name + " back piping", new Vector3(x, 1.35f, z + 0.655f), new Vector3(1.1f, 1.2f, 0.012f), DeepTeal);
            BeveledBlock(root, name + " head cushion", new Vector3(x, 1.83f, z + 0.17f), new Vector3(0.96f, 0.45f, 0.22f), Ivory, 0.075f);
            Block(root, name + " headrest seam", new Vector3(x, 1.72f, z + 0.052f), new Vector3(0.77f, 0.022f, 0.014f), Trim);
            Block(root, name + " seat belt", new Vector3(x, 0.837f, z - 0.15f), new Vector3(1.02f, 0.018f, 0.08f), DeepTeal);
            BeveledBlock(root, name + " belt buckle", new Vector3(x + 0.12f, 0.856f, z - 0.15f), new Vector3(0.14f, 0.028f, 0.115f), Frame, 0.01f);
            for (int arm = -1; arm <= 1; arm += 2)
                BeveledBlock(root, name + " arm " + arm, new Vector3(x + arm * 0.72f, 0.91f, z - 0.04f), new Vector3(0.17f, 0.62f, 1.05f), Ivory, 0.07f);
        }

        static void BuildGalley(Transform root)
        {
            BeveledBlock(root, "Galley counter", new Vector3(-5.1f, 2.3f, 25.5f), new Vector3(4.6f, 1.2f, 2.0f), Frame, 0.12f);
            BeveledBlock(root, "Galley work top", new Vector3(-5.1f, 2.95f, 25.5f), new Vector3(4.75f, 0.2f, 2.05f), Ivory, 0.06f);
            Block(root, "Galley sign", new Vector3(-5.1f, 5.15f, 26.5f), new Vector3(3.8f, 0.65f, 0.22f), Navy);
            for (int i = 0; i < 4; i++)
            {
                float x = -6.45f + i * 0.9f;
                Block(root, "Galley drawer " + i, new Vector3(x, 1.85f, 24.45f), new Vector3(0.72f, 0.52f, 0.12f), i % 2 == 0 ? AirportStyle.Hex("92A7A7") : AirportStyle.Hex("708A91"));
                Block(root, "Galley drawer pull " + i, new Vector3(x, 1.96f, 24.365f), new Vector3(0.33f, 0.045f, 0.04f), DeepTeal);
                Sphere(root, "Galley canister " + i, new Vector3(x, 3.38f, 25.55f), new Vector3(0.42f, 0.8f, 0.42f), i % 2 == 0 ? AirportStyle.Hex("385968") : AirportStyle.Hex("BD874D"));
            }
            Block(root, "Galley overhead light", new Vector3(-5.1f, 6.35f, 24.3f), new Vector3(4.2f, 0.18f, 0.24f), WarmGlow, AirportStyle.Finish.Glow);
            Block(root, "Galley sign text", new Vector3(-5.1f, 5.15f, 26.37f), new Vector3(2.2f, 0.16f, 0.02f), AisleGlow, AirportStyle.Finish.Glow);
            // 饮料推车停在厨房前，加一点生活感。
            BeveledBlock(root, "Trolley body", new Vector3(-2.6f, 1.05f, 22.6f), new Vector3(0.95f, 1.5f, 1.7f), Frame, 0.08f);
            Block(root, "Trolley top", new Vector3(-2.6f, 1.84f, 22.6f), new Vector3(1.02f, 0.08f, 1.78f), Ivory);
            Block(root, "Trolley rail", new Vector3(-2.6f, 2.0f, 21.74f), new Vector3(0.9f, 0.05f, 0.05f), DeepTeal);
            for (int i = 0; i < 3; i++)
                Block(root, "Trolley drawer " + i, new Vector3(-3.08f, 0.55f + i * 0.45f, 22.6f), new Vector3(0.05f, 0.3f, 1.4f), i == 1 ? DeepTeal : AirportStyle.Hex("708A91"));
            for (int i = 0; i < 3; i++)
                Sphere(root, "Trolley cup " + i, new Vector3(-2.85f + i * 0.28f, 2.02f, 22.2f + (i % 2) * 0.5f), new Vector3(0.2f, 0.26f, 0.2f), i == 1 ? AirportStyle.Meals : AirportStyle.Boarding);
            // 盆栽棕榈，放在舱壁两角。
            foreach (float px in new[] { -7.3f, 7.3f })
            {
                Block(root, "Planter", new Vector3(px, 0.45f, 26.9f), new Vector3(0.9f, 0.9f, 0.9f), AirportStyle.Hex("B98961"));
                Block(root, "Planter rim", new Vector3(px, 0.92f, 26.9f), new Vector3(0.98f, 0.08f, 0.98f), Wood);
                Block(root, "Plant stem", new Vector3(px, 1.5f, 26.9f), new Vector3(0.12f, 1.2f, 0.12f), AirportStyle.Hex("7A5A3C"));
                for (int leaf = 0; leaf < 6; leaf++)
                {
                    float a = leaf * 60f;
                    var frond = Sphere(root, "Plant frond " + leaf, new Vector3(px + Mathf.Sin(a * Mathf.Deg2Rad) * 0.55f, 2.15f, 26.9f + Mathf.Cos(a * Mathf.Deg2Rad) * 0.55f), new Vector3(0.42f, 0.18f, 1.25f), leaf % 2 == 0 ? AirportStyle.Hex("4F9C70") : AirportStyle.Hex("6FBF7E"));
                    frond.transform.localRotation = Quaternion.Euler(-18f, a, 0);
                }
            }
        }

        static void BuildCockpit(Transform root)
        {
            BeveledBlock(root, "Cockpit door outer", new Vector3(0, 4.2f, 27.93f), new Vector3(4.0f, 7.4f, 0.3f), Wood, 0.08f);
            BeveledBlock(root, "Cockpit door", new Vector3(0, 4.2f, 27.72f), new Vector3(3.5f, 6.8f, 0.18f), DeepTeal, 0.065f);
            Block(root, "Cockpit lock plate", new Vector3(0, 5.25f, 27.56f), new Vector3(0.62f, 0.92f, 0.08f), AirportStyle.Hex("E1E5E1"));
            Block(root, "Cockpit lock", new Vector3(0, 4.75f, 27.48f), new Vector3(0.24f, 0.28f, 0.08f), AirportStyle.Hex("5E6870"));
            Block(root, "Cockpit door handle", new Vector3(1.18f, 3.1f, 27.48f), new Vector3(0.11f, 0.8f, 0.08f), AirportStyle.Hex("C88C52"));
            for (int side = -1; side <= 1; side += 2)
            {
                BeveledBlock(root, "Cockpit side panel", new Vector3(side * 3.25f, 4.8f, 27.5f), new Vector3(2.6f, 7.0f, 0.55f), AirportStyle.Hex("AEC1BA"), 0.12f);
                for (int i = 0; i < 3; i++)
                    Block(root, "Cockpit indicator", new Vector3(side * 3.25f, 6.7f - i * 0.55f, 27.18f), new Vector3(0.34f, 0.24f, 0.08f), i == 0 ? AirportStyle.Good : AirportStyle.Hex("E2A15C"));
            }
        }

        static void BuildLamps(Transform root)
        {
            for (int z = 2; z < 27; z += 4)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    BeveledBlock(root, "Warm wall light", new Vector3(side * 7.72f, 4.0f, z), new Vector3(0.25f, 0.72f, 0.82f), Wood, 0.08f);
                    Block(root, "Wall light diffuser", new Vector3(side * 7.575f, 4.0f, z), new Vector3(0.06f, 0.46f, 0.58f), WarmGlow, AirportStyle.Finish.Glow);
                }
            }
            NoShadow(Block(root, "Aisle ceiling light", new Vector3(0, 9.48f, 13), new Vector3(0.26f, 0.08f, 26), AirportStyle.Hex("FFF6E6"), AirportStyle.Finish.Glow));
        }

        static void AddRestingGuest(Transform root, string name, Vector3 position, Color uniformColor)
        {
            // 1.0.9：复用局内地勤模型平躺在卧铺上（头朝 +z，面朝上），取代旧的球体拼装。
            Transform guest = AirportWorld.CreateCrew(name, uniformColor, Vector3.zero);
            guest.SetParent(root, false);
            // 半躺姿势：脚在床尾，上身抬起，脸朝镜头方向可见。
            guest.localPosition = position + new Vector3(0, -0.2f, -0.95f);
            guest.localRotation = Quaternion.Euler(0, 180, 0) * Quaternion.Euler(-74, 0, 0);
            guest.localScale = Vector3.one * 1.7f;
            Transform ring = guest.Find("Identity Ring");
            if (ring) ring.gameObject.SetActive(false);
        }

        static void AddOpenSeatOutline(Transform root, Vector3 center)
        {
            var outline = new GameObject("Partner open seat outline").transform;
            outline.SetParent(root, false);
            Color line = AirportStyle.Hex("FFF2E9");
            DashBlock(outline, "Partner outline", center + new Vector3(0, 0.08f, 0), new Vector3(0.7f, 0.12f, 0.75f), line);
            DashBlock(outline, "Partner outline", center + new Vector3(0, 0.08f, -0.62f), new Vector3(1.3f, 0.12f, 0.32f), line);
            DashBlock(outline, "Partner outline", center + new Vector3(-0.62f, 0.08f, -1.05f), new Vector3(0.28f, 0.12f, 0.55f), line);
            DashBlock(outline, "Partner outline", center + new Vector3(0.62f, 0.08f, -1.05f), new Vector3(0.28f, 0.12f, 0.55f), line);
            DashBlock(outline, "Partner outline", center + new Vector3(-0.35f, 0.08f, -1.45f), new Vector3(0.35f, 0.12f, 0.24f), line);
            DashBlock(outline, "Partner outline", center + new Vector3(0.35f, 0.08f, -1.45f), new Vector3(0.35f, 0.12f, 0.24f), line);
            Block(outline, "Partner plus vertical", center + new Vector3(0, 0.25f, 0.52f), new Vector3(0.15f, 0.12f, 0.68f), Color.white);
            Block(outline, "Partner plus horizontal", center + new Vector3(0, 0.25f, 0.52f), new Vector3(0.68f, 0.12f, 0.15f), Color.white);
        }

        static void DashBlock(Transform parent, string name, Vector3 center, Vector3 size, Color color)
        {
            const int count = 5;
            for (int i = 0; i < count; i++)
            {
                Vector3 segment = size;
                segment.z /= count * 1.65f;
                float z = center.z - size.z * 0.5f + (i + 0.5f) * size.z / count;
                Block(parent, name + " " + i, new Vector3(center.x, center.y, z), segment, color);
            }
        }

        static GameObject BeveledBlock(Transform parent, string name, Vector3 position, Vector3 size, Color color, float bevel)
        {
            bevel = Mathf.Min(bevel, Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.49f);
            var key = new Vector4(size.x, size.y, size.z, bevel);
            Mesh mesh;
            if (!BevelMeshes.TryGetValue(key, out mesh) || !mesh)
            {
                var vertices = new List<Vector3>(96);
                var triangles = new List<int>(132);
                Vector3 half = size * 0.5f;
                // Six broad faces, twelve edge bevels and eight corners: 44 triangles.
                for (int axis = 0; axis < 3; axis++)
                {
                    int b = (axis + 1) % 3, c = (axis + 2) % 3;
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        Vector3 p = Vector3.zero;
                        p[axis] = half[axis] * sign;
                        Vector3 u = Vector3.zero, v = Vector3.zero;
                        u[b] = half[b] - bevel;
                        v[c] = half[c] - bevel;
                        AddFace(vertices, triangles, p + u + v, p - u + v, p - u - v, p + u - v);
                    }
                    for (int sb = -1; sb <= 1; sb += 2)
                    for (int sc = -1; sc <= 1; sc += 2)
                    {
                        Vector3 p = Vector3.zero, q = Vector3.zero, along = Vector3.zero;
                        p[b] = sb * half[b]; p[c] = sc * (half[c] - bevel);
                        q[b] = sb * (half[b] - bevel); q[c] = sc * half[c];
                        along[axis] = half[axis] - bevel;
                        AddFace(vertices, triangles, p - along, q - along, q + along, p + along);
                    }
                }
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                    AddFace(vertices, triangles,
                        new Vector3(x * half.x, y * (half.y - bevel), z * (half.z - bevel)),
                        new Vector3(x * (half.x - bevel), y * half.y, z * (half.z - bevel)),
                        new Vector3(x * (half.x - bevel), y * (half.y - bevel), z * half.z));
                mesh = CreateMesh("Cabin shared bevel", vertices, triangles);
                BevelMeshes[key] = mesh;
            }
            return MeshObject(parent, name, position, mesh, color, AirportStyle.Finish.Matte);
        }

        // Extruded rounded rectangles lie in the wall's YZ plane, independent of thickness.
        static GameObject WindowPanel(Transform parent, string name, Vector3 position, Vector3 size, float radius, Color color, AirportStyle.Finish finish = AirportStyle.Finish.Matte)
        {
            radius = Mathf.Min(radius, Mathf.Min(size.y, size.z) * 0.5f);
            var key = new Vector4(size.x, size.y, size.z, radius);
            Mesh mesh;
            if (!WindowMeshes.TryGetValue(key, out mesh) || !mesh)
            {
                const int cornerSteps = 4;
                var ring = new List<Vector3>(20);
                for (int corner = 0; corner < 4; corner++)
                {
                    float cz = (corner == 0 || corner == 3 ? 1 : -1) * (size.z * 0.5f - radius);
                    float cy = (corner < 2 ? 1 : -1) * (size.y * 0.5f - radius);
                    for (int step = 0; step <= cornerSteps; step++)
                    {
                        float angle = (corner * 90 + step * 90f / cornerSteps) * Mathf.Deg2Rad;
                        ring.Add(new Vector3(0, cy + Mathf.Sin(angle) * radius, cz + Mathf.Cos(angle) * radius));
                    }
                }
                var vertices = new List<Vector3>(200);
                var triangles = new List<int>(240);
                Vector3 depth = Vector3.right * size.x * 0.5f;
                for (int i = 0; i < ring.Count; i++)
                {
                    Vector3 a = ring[i], b = ring[(i + 1) % ring.Count];
                    // Capsule halves meet at duplicate points; skip their zero-area faces.
                    if ((a - b).sqrMagnitude < 0.0000001f) continue;
                    AddFace(vertices, triangles, depth, a + depth, b + depth);
                    AddFace(vertices, triangles, -depth, a - depth, b - depth);
                    AddFace(vertices, triangles, a - depth, b - depth, b + depth, a + depth);
                }
                mesh = CreateMesh("Cabin shared rounded window", vertices, triangles);
                WindowMeshes[key] = mesh;
            }
            return MeshObject(parent, name, position, mesh, color, finish);
        }

        static void AddFace(List<Vector3> vertices, List<int> triangles, params Vector3[] face)
        {
            int start = vertices.Count;
            Vector3 center = Vector3.zero;
            foreach (Vector3 point in face) { vertices.Add(point); center += point; }
            bool outward = Vector3.Dot(Vector3.Cross(face[1] - face[0], face[2] - face[0]), center) >= 0;
            for (int i = 1; i < face.Length - 1; i++)
            {
                triangles.Add(start);
                triangles.Add(start + (outward ? i : i + 1));
                triangles.Add(start + (outward ? i + 1 : i));
            }
        }

        static Mesh CreateMesh(string name, List<Vector3> vertices, List<int> triangles)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static GameObject MeshObject(Transform parent, string name, Vector3 position, Mesh mesh, Color color, AirportStyle.Finish finish)
        {
            var item = new GameObject(name);
            item.transform.SetParent(parent, false);
            item.transform.localPosition = position;
            item.AddComponent<MeshFilter>().sharedMesh = mesh;
            item.AddComponent<MeshRenderer>().sharedMaterial = AirportStyle.SharedMaterial(color, finish);
            return item;
        }

        static GameObject Block(Transform parent, string name, Vector3 position, Vector3 scale, Color color, AirportStyle.Finish finish = AirportStyle.Finish.Matte)
        {
            return Primitive(parent, name, PrimitiveType.Cube, position, scale, color, finish);
        }

        static GameObject Sphere(Transform parent, string name, Vector3 position, Vector3 scale, Color color, AirportStyle.Finish finish = AirportStyle.Finish.Plastic)
        {
            return Primitive(parent, name, PrimitiveType.Sphere, position, scale, color, finish);
        }

        static GameObject Primitive(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 scale, Color color, AirportStyle.Finish finish)
        {
            GameObject item = GameObject.CreatePrimitive(type);
            item.name = name;
            item.transform.SetParent(parent, false);
            item.transform.localPosition = position;
            item.transform.localScale = scale;
            var collider = item.GetComponent<Collider>();
            if (collider) Object.DestroyImmediate(collider);
            var renderer = item.GetComponent<Renderer>();
            renderer.sharedMaterial = AirportStyle.SharedMaterial(color, finish);
            return item;
        }
    }
}
