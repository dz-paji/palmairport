using System.Collections.Generic;
using UnityEngine;

namespace IslandAirport
{
    /// <summary>
    /// M1 起唯一的视觉事实源：语义调色板、材质缓存（按颜色+光泽档）、
    /// 中文字体回退加载、程序化 UI 圆角精灵。世界与 HUD 共用，避免各处硬编码。
    /// </summary>
    public static class AirportStyle
    {
        // ---- 任务语义色（与 PRD v7 及 P0 §3 对齐：餐橙 / 行蓝 / 油绿 / 登机青蓝）----
        public static readonly Color Meals = Hex("F0904A");
        public static readonly Color Baggage = Hex("5FA8E0");
        public static readonly Color Fuel = Hex("45B85C");
        public static readonly Color Boarding = Hex("45C4CE");

        // ---- 玩家身份色 ----
        public static readonly Color Player1 = Hex("F89A55");
        public static readonly Color Player2 = Hex("56B6D9");

        // ---- 环境色 ----
        public static readonly Color Sky = Hex("57BFC5");
        public static readonly Color SeaDeep = Hex("329EAD");
        public static readonly Color SeaShallow = Hex("75CFC4");
        public static readonly Color Foam = Hex("EAFBF6");
        public static readonly Color Sand = Hex("F0D9A6");
        public static readonly Color Ground = Hex("E4DFC7");
        public static readonly SurfaceColors Surfaces = new SurfaceColors();

        // ---- UI 色 ----
        public static readonly Color Ink = Hex("203B49");
        public static readonly Color InkSoft = Hex("3A535F");
        public static readonly Color Muted = Hex("718792");
        public static readonly Color Paper = Hex("F7F5EE");
        public static readonly Color Teal = Hex("2A9D8F");
        public static readonly Color Warn = Hex("D56A50");
        public static readonly Color Good = Hex("69C6A1");

        public sealed class SurfaceColors
        {
            // 停机坪混凝土（暖灰），区别于行道沥青
            public readonly Color Apron = Hex("CFC6B4");
            public readonly Color Road = Hex("4E5F63");
            public readonly Color Runway = Hex("3E505A");
            public readonly Color Pavement = Hex("D9C9A8");
            public readonly Color YellowLine = Hex("F2C94C");
            public readonly Color WhiteLine = Hex("FFF8E7");
        }

        public static Color ServiceColor(ServiceKind kind)
        {
            return kind == ServiceKind.Meals ? Meals : kind == ServiceKind.Baggage ? Baggage : kind == ServiceKind.Fuel ? Fuel : Boarding;
        }

        /// <summary>
        /// 解析 #RGB/#RGBA/#RRGGBB/#RRGGBAA。实现在此（AirportWorld.Hex 委托过来），
        /// 保持 AirportStyle 不依赖 AirportWorld，避免静态构造循环。
        /// </summary>
        public static Color Hex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Color.white;
            string value = hex.Trim();
            if (value.StartsWith("#")) value = value.Substring(1);
            if (value.Length == 3 || value.Length == 4)
            {
                int r3 = HexDigit(value[0]), g3 = HexDigit(value[1]), b3 = HexDigit(value[2]);
                int a3 = value.Length == 4 ? HexDigit(value[3]) : 15;
                if (r3 < 0 || g3 < 0 || b3 < 0 || a3 < 0) return Color.white;
                return new Color(r3 / 15.0f, g3 / 15.0f, b3 / 15.0f, a3 / 15.0f);
            }
            if (value.Length != 6 && value.Length != 8) return Color.white;
            int r = HexByte(value, 0), g = HexByte(value, 2), b = HexByte(value, 4);
            int a = value.Length == 8 ? HexByte(value, 6) : 255;
            if (r < 0 || g < 0 || b < 0 || a < 0) return Color.white;
            return new Color(r / 255.0f, g / 255.0f, b / 255.0f, a / 255.0f);
        }

        static int HexDigit(char value)
        {
            if (value >= '0' && value <= '9') return value - '0';
            if (value >= 'a' && value <= 'f') return value - 'a' + 10;
            if (value >= 'A' && value <= 'F') return value - 'A' + 10;
            return -1;
        }

        static int HexByte(string value, int index)
        {
            if (index < 0 || index + 1 >= value.Length) return -1;
            int high = HexDigit(value[index]);
            int low = HexDigit(value[index + 1]);
            return high < 0 || low < 0 ? -1 : high * 16 + low;
        }

        // ---- 材质缓存：Standard shader，按颜色与光泽档共享 ----
        public enum Finish { Matte = 0, Plastic = 1, Glass = 2, Glow = 3 }
        static readonly Dictionary<System.ValueTuple<Color, Finish>, Material> Cache = new Dictionary<System.ValueTuple<Color, Finish>, Material>();

        public static Material SharedMaterial(Color color, Finish finish = Finish.Matte)
        {
            var key = (color, finish);
            Material cached;
            if (Cache.TryGetValue(key, out cached) && cached) return cached;
            Shader shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            var material = new Material(shader);
            material.name = "Palm Bay " + finish + " " + ColorUtility.ToHtmlStringRGB(color);
            material.enableInstancing = true;
            material.color = color;
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.0f);
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", finish == Finish.Glass ? 0.72f : finish == Finish.Plastic ? 0.34f : 0.12f);
            if (finish == Finish.Glow && material.HasProperty("_EmissionColor"))
            {
                // 自发光：舷窗、灯带等。机舱材质随场景持久化，所以变体会进包。
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                material.SetColor("_EmissionColor", color * 0.85f);
            }
            Cache[key] = material;
            return material;
        }

        // ---- 中文 UI 字体：动态 OS 字体回退（编辑器 macOS、Android Noto、Windows 雅黑）----
        static Font chineseFont;
        public static Font ChineseFont
        {
            get
            {
                if (!chineseFont)
                    chineseFont = Font.CreateDynamicFontFromOSFont(
                        new[] { "PingFang SC", "Noto Sans CJK SC", "Noto Sans SC", "Microsoft YaHei", "Arial Unicode MS", "Arial" }, 20);
                return chineseFont;
            }
        }

        // ---- 程序化圆角精灵（9-slice），供 uGUI Image.Sliced 使用 ----
        static Sprite rounded;
        public static Sprite RoundedSprite
        {
            get
            {
                if (rounded) return rounded;
                const int size = 64, radius = 16;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                for (int x = 0; x < size; x++)
                    for (int y = 0; y < size; y++)
                    {
                        float dx = Mathf.Max(radius - 1 - x, x - (size - radius));
                        float dy = Mathf.Max(radius - 1 - y, y - (size - radius));
                        float distance = Mathf.Sqrt(Mathf.Max(0, dx) * Mathf.Max(0, dx) + Mathf.Max(0, dy) * Mathf.Max(0, dy));
                        texture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(radius - distance + .5f)));
                    }
                texture.Apply();
                texture.name = "Rounded UI Sprite";
                var border = new Vector4(radius, radius, radius, radius);
                rounded = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, border);
                return rounded;
            }
        }

        static Sprite disc;
        /// <summary>实心圆精灵：摇杆底盘、圆钮、身份环。</summary>
        public static Sprite DiscSprite
        {
            get
            {
                if (disc) return disc;
                const int size = 64;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                float center = (size - 1) * .5f;
                for (int x = 0; x < size; x++)
                    for (int y = 0; y < size; y++)
                    {
                        float d = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                        texture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(center + .5f - d)));
                    }
                texture.Apply();
                texture.name = "Disc UI Sprite";
                disc = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100);
                return disc;
            }
        }
    }
}
