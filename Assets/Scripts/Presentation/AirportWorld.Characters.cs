using System.Collections.Generic;
using UnityEngine;

namespace IslandAirport
{
    public static partial class AirportWorld
    {
        // 1.0.9 角色精修：地勤与旅客改为旋转体（lathe）躯干/四肢/头部，
        // 取代 Unity 球体+胶囊体拼装；反光背心贴合躯干，耳机、帽檐、工牌、
        // 对讲机为独立小件。脚底仍在 y=0，整体高约 1.22，包围盒与旧模型相当，
        // 玩法层（位置/朝向/缩放）无需改动。无碰撞体。
        static readonly Dictionary<string, Mesh> CharacterMeshes = new Dictionary<string, Mesh>();

        static readonly Color SkinShadow = Hex("D9987A");
        static readonly Color Cheek = Hex("F3AA95");
        static readonly Color Mouth = Hex("9B5A52");
        static readonly Color Reflective = Hex("E6ECE9");
        static readonly Color Denim = Hex("4F6273");
        static readonly Color Khaki = Hex("CDBF9E");
        static readonly Color ShoeBrown = Hex("6B4A2E");
        static readonly Color EyeWhite = Hex("FFFFFF");

        /// <summary>
        /// Builds a compact cartoon ground-crew character facing +z.
        /// The input position is the character's foot position.
        /// </summary>
        public static Transform CreateCrew(string name, Color color, Vector3 position)
        {
            GameObject crewObject = new GameObject(string.IsNullOrEmpty(name) ? "Crew" : name);
            Transform crew = crewObject.transform;
            crew.position = position;
            crew.rotation = Quaternion.identity;

            Color uniform = color;
            Color uniformShadow = Darken(color, 0.27f);

            // 脚底身份环：远比制服色更容易在高角度镜头下辨认玩家。
            Primitive(crew, "Identity Ring", PrimitiveType.Cylinder,
                new Vector3(0.0f, 0.02f, 0.0f), new Vector3(0.62f, 0.015f, 0.62f), color, Quaternion.identity);

            BuildCharacterBody(crew, uniform, uniformShadow, DarkMetal, Hex("3B4A52"), uniform, uniformShadow, false);

            // 反光背心：贴合躯干的薄壳 + 两道反光带 + 拉链。
            CharacterLathe(crew, "Vest", "vest", new[]
            {
                new Vector2(.236f, .555f), new Vector2(.250f, .590f), new Vector2(.258f, .660f),
                new Vector2(.250f, .740f), new Vector2(.215f, .785f), new Vector2(.150f, .815f), new Vector2(.105f, .830f)
            }, 24, Vector3.zero, Quaternion.identity, new Vector3(1.10f, 1f, .92f), VestYellow, AirportStyle.Finish.Plastic);
            foreach (float y in new[] { .605f, .705f })
                CharacterLathe(crew, "Reflective band " + y, "vest band", new[]
                {
                    new Vector2(.256f, -.012f), new Vector2(.268f, -.007f), new Vector2(.268f, .007f), new Vector2(.256f, .012f)
                }, 24, new Vector3(0, y, 0), Quaternion.identity, new Vector3(1.10f, 1f, .92f), Reflective, AirportStyle.Finish.Plastic);
            ModelBox(crew, "Vest zip", new Vector3(0, .685f, .242f), new Vector3(.018f, .25f, .012f), Darken(VestYellow, .35f), .005f, Quaternion.identity);

            // 工牌挂绳 + 工牌。
            foreach (int side in new[] { -1, 1 })
                ModelBox(crew, "Lanyard " + side, new Vector3(side * .045f, .775f, .215f), new Vector3(.011f, .10f, .011f), DarkMetal, .004f, Quaternion.Euler(0, 0, -side * 22f));
            ModelBox(crew, "Work badge", new Vector3(0, .688f, .262f), new Vector3(.075f, .092f, .014f), WhiteLine, .006f, Quaternion.identity);
            ModelBox(crew, "Badge stripe", new Vector3(0, .722f, .270f), new Vector3(.075f, .020f, .008f), uniform, .003f, Quaternion.identity);
            ModelBox(crew, "Badge photo", new Vector3(-.018f, .676f, .270f), new Vector3(.026f, .030f, .006f), Skin, .002f, Quaternion.identity);
            ModelBox(crew, "Badge text", new Vector3(.016f, .676f, .270f), new Vector3(.024f, .006f, .006f), DarkMetal, .002f, Quaternion.identity);

            // 腰带对讲机。
            ModelBox(crew, "Pocket radio", new Vector3(-.215f, .525f, .105f), new Vector3(.048f, .092f, .036f), DarkMetal, .010f, Quaternion.Euler(0, 24f, 0));
            Primitive(crew, "Radio aerial", PrimitiveType.Cylinder,
                new Vector3(-.222f, .595f, .105f), new Vector3(.009f, .032f, .009f), DarkMetal, Quaternion.identity);

            Transform head = BuildCharacterHead(crew, Hair, 0, false);

            // 耳机：耳罩 + 头带 + 麦克风杆。
            foreach (int side in new[] { -1, 1 })
            {
                Primitive(head, "Ear cup " + side, PrimitiveType.Cylinder,
                    new Vector3(side * .228f, .20f, -.005f), new Vector3(.135f, .028f, .135f), DarkMetal, Quaternion.Euler(0, 0, 90f));
                Primitive(head, "Ear pad " + side, PrimitiveType.Cylinder,
                    new Vector3(side * .252f, .20f, -.005f), new Vector3(.100f, .012f, .100f), uniformShadow, Quaternion.Euler(0, 0, 90f), AirportStyle.Finish.Plastic);
            }
            const int bandSegments = 6;
            for (int i = 0; i < bandSegments; i++)
            {
                float a0 = Mathf.PI * (i + .5f) / bandSegments;
                Vector3 p = new Vector3(-Mathf.Cos(a0) * .252f, .20f + Mathf.Sin(a0) * .252f, -.005f);
                ModelBox(head, "Headband " + i, p, new Vector3(.142f, .030f, .046f), DarkMetal, .009f,
                    Quaternion.Euler(0, 0, Mathf.Rad2Deg * a0 - 90f));
            }
            Vector3 micFrom = new Vector3(.215f, .165f, .075f), micTo = new Vector3(.075f, .075f, .215f);
            Primitive(head, "Mic boom", PrimitiveType.Cylinder, (micFrom + micTo) * .5f,
                new Vector3(.012f, (micTo - micFrom).magnitude * .5f, .012f), DarkMetal, Quaternion.FromToRotation(Vector3.up, micTo - micFrom));
            Primitive(head, "Mic", PrimitiveType.Sphere, micTo, new Vector3(.034f, .034f, .034f), DarkMetal, Quaternion.identity);

            // 工作帽：圆顶 + 帽带 + 帽檐 + 顶扣 + 前徽。
            BuildCharacterCap(head, uniform, uniformShadow, VestYellow);

            return crew;
        }

        /// <summary>
        /// 旅客：同一套身体，换便装、发型、墨镜与行李（拉杆箱/背包）。
        /// seed 决定款式；玩法层按 .65 缩放并自行定位。
        /// </summary>
        public static Transform CreatePassenger(string name, Color color, Vector3 position, int seed)
        {
            GameObject root = new GameObject(string.IsNullOrEmpty(name) ? "Passenger" : name);
            Transform passenger = root.transform;
            passenger.position = position;
            passenger.rotation = Quaternion.identity;
            int look = ((seed % 4) + 4) % 4;

            Color shirt = color;
            Color shirtShadow = Darken(color, .22f);
            Color trousers = (look & 1) == 0 ? Denim : Khaki;
            Color trousersShadow = Darken(trousers, .2f);
            Color shoes = (look & 1) == 0 ? ShoeBrown : Hex("F4F1E8");
            Color hair = new[] { Hair, Hex("2B2320"), Hex("8A5A3B"), Hex("D8B56A") }[look];

            Color sole = (look & 1) == 0 ? Hex("3B2A1C") : Hex("B9C1C4");
            BuildCharacterBody(passenger, trousers, trousersShadow, shoes, sole, shirt, shirtShadow, true);

            // 领口 / 纽扣细节。
            CharacterLathe(passenger, "Collar", "collar", new[]
            {
                new Vector2(.100f, .815f), new Vector2(.150f, .805f), new Vector2(.165f, .835f), new Vector2(.105f, .850f)
            }, 20, Vector3.zero, Quaternion.identity, new Vector3(1.10f, 1f, .92f), shirtShadow, AirportStyle.Finish.Plastic);
            for (int i = 0; i < 3; i++)
                Primitive(passenger, "Button " + i, PrimitiveType.Sphere, new Vector3(0, .745f - i * .07f, i == 0 ? .204f : .210f),
                    new Vector3(.022f, .022f, .012f), WhiteLine, Quaternion.identity);

            Transform head = BuildCharacterHead(passenger, hair, look, true);

            if (look == 0)
            {
                // 墨镜。
                ModelBox(head, "Sunglasses", new Vector3(0, .225f, .198f), new Vector3(.235f, .055f, .022f), Hex("1D2B33"), .012f, Quaternion.identity, AirportStyle.Finish.Glass);
                foreach (int side in new[] { -1, 1 })
                    ModelBox(head, "Glasses arm " + side, new Vector3(side * .205f, .232f, .08f), new Vector3(.012f, .014f, .22f), Hex("1D2B33"), .004f, Quaternion.identity);
            }
            else if (look == 2)
            {
                // 遮阳帽。
                CharacterLathe(head, "Sun hat", "sunhat", new[]
                {
                    new Vector2(.330f, .255f), new Vector2(.335f, .275f), new Vector2(.250f, .300f), new Vector2(.240f, .335f),
                    new Vector2(.225f, .395f), new Vector2(.150f, .445f), new Vector2(.001f, .460f)
                }, 24, Vector3.zero, Quaternion.identity, Vector3.one, Hex("FBF2DC"), AirportStyle.Finish.Matte);
                CharacterLathe(head, "Hat ribbon", "hat ribbon", new[]
                {
                    new Vector2(.246f, .300f), new Vector2(.252f, .315f), new Vector2(.246f, .340f)
                }, 24, Vector3.zero, Quaternion.identity, Vector3.one, shirt, AirportStyle.Finish.Plastic);
            }

            // 行李：偶数 seed 拉杆箱，奇数 seed 背包。
            if ((look & 1) == 0)
            {
                Color caseColor = new[] { Hex("E8855A"), Hex("5FA8E0"), Hex("45B85C"), Hex("F2C94C") }[((seed / 4) % 4 + 4) % 4];
                Transform caseRoot = new GameObject("Suitcase").transform;
                caseRoot.SetParent(passenger, false);
                caseRoot.localPosition = new Vector3(.36f, 0, -.16f);
                caseRoot.localRotation = Quaternion.Euler(-14f, 0, 0);
                ModelBox(caseRoot, "Case", new Vector3(0, .24f, 0), new Vector3(.17f, .34f, .26f), caseColor, .032f, Quaternion.identity);
                foreach (float z in new[] { -.085f, .085f })
                    ModelBox(caseRoot, "Case rib " + z, new Vector3(0, .24f, z), new Vector3(.178f, .32f, .022f), Darken(caseColor, .18f), .008f, Quaternion.identity);
                foreach (float x in new[] { -.045f, .045f })
                    Primitive(caseRoot, "Handle rod " + x, PrimitiveType.Cylinder, new Vector3(x, .43f, -.06f), new Vector3(.013f, .10f, .013f), Hex("B9C1C4"), Quaternion.identity);
                ModelBox(caseRoot, "Handle grip", new Vector3(0, .53f, -.06f), new Vector3(.13f, .028f, .032f), DarkMetal, .010f, Quaternion.identity);
                foreach (float x in new[] { -.07f, .07f })
                    Primitive(caseRoot, "Case wheel " + x, PrimitiveType.Cylinder, new Vector3(x, .035f, .07f), new Vector3(.06f, .012f, .06f), DarkMetal, Quaternion.Euler(0, 0, 90f));
            }
            else
            {
                Color packColor = new[] { Hex("F2C94C"), Hex("45C4CE"), Hex("E8855A"), Hex("6FBF7E") }[((seed / 4) % 4 + 4) % 4];
                ModelBox(passenger, "Backpack", new Vector3(0, .655f, -.265f), new Vector3(.30f, .30f, .14f), packColor, .045f, Quaternion.identity);
                ModelBox(passenger, "Backpack pocket", new Vector3(0, .60f, -.335f), new Vector3(.20f, .13f, .05f), Darken(packColor, .18f), .018f, Quaternion.identity);
                foreach (int side in new[] { -1, 1 })
                    ModelBox(passenger, "Strap " + side, new Vector3(side * .11f, .79f, -.05f), new Vector3(.05f, .035f, .28f), Darken(packColor, .25f), .010f, Quaternion.Euler(-28f, 0, 0));
            }

            return passenger;
        }

        /// <summary>鞋、裤腿、躯干、腰带、手臂与手。躯干厚度略小于宽度。</summary>
        static void BuildCharacterBody(Transform root, Color trousers, Color trousersShadow, Color shoe, Color sole,
            Color top, Color topShadow, bool shortSleeves)
        {
            foreach (int side in new[] { -1, 1 })
            {
                ModelBox(root, "Shoe " + side, new Vector3(side * .12f, .062f, .045f), new Vector3(.20f, .105f, .33f), shoe, .035f, Quaternion.identity, AirportStyle.Finish.Matte);
                ModelBox(root, "Sole " + side, new Vector3(side * .12f, .018f, .045f), new Vector3(.21f, .036f, .34f), sole, .012f, Quaternion.identity, AirportStyle.Finish.Matte);
                ModelBox(root, "Lace " + side, new Vector3(side * .12f, .117f, .11f), new Vector3(.09f, .012f, .07f), sole, .004f, Quaternion.identity);
                CharacterLathe(root, "Leg " + side, "leg", new[]
                {
                    new Vector2(.062f, .100f), new Vector2(.078f, .125f), new Vector2(.078f, .165f),
                    new Vector2(.070f, .180f), new Vector2(.070f, .320f), new Vector2(.088f, .420f), new Vector2(.060f, .470f)
                }, 14, Vector3.zero, Quaternion.identity, Vector3.one, trousers, AirportStyle.Finish.Matte);
            }

            // 躯干：臀部圆润，腰部略收，胸部外扩，肩部收圆至颈。
            CharacterLathe(root, "Torso", "torso", new[]
            {
                new Vector2(.001f, .330f), new Vector2(.120f, .340f), new Vector2(.185f, .365f), new Vector2(.212f, .420f),
                new Vector2(.212f, .470f), new Vector2(.205f, .540f), new Vector2(.222f, .620f), new Vector2(.222f, .690f),
                new Vector2(.212f, .750f), new Vector2(.175f, .800f), new Vector2(.100f, .840f), new Vector2(.001f, .855f)
            }, 24, Vector3.zero, Quaternion.identity, new Vector3(1.10f, 1f, .92f), top, AirportStyle.Finish.Plastic);
            CharacterLathe(root, "Trouser top", "trouser top", new[]
            {
                new Vector2(.001f, .325f), new Vector2(.125f, .335f), new Vector2(.190f, .362f), new Vector2(.218f, .420f),
                new Vector2(.218f, .470f), new Vector2(.212f, .510f)
            }, 24, Vector3.zero, Quaternion.identity, new Vector3(1.10f, 1f, .92f), trousers, AirportStyle.Finish.Matte);
            CharacterLathe(root, "Belt", "belt", new[]
            {
                new Vector2(.218f, .500f), new Vector2(.228f, .510f), new Vector2(.228f, .540f), new Vector2(.218f, .550f)
            }, 24, Vector3.zero, Quaternion.identity, new Vector3(1.10f, 1f, .92f), trousersShadow, AirportStyle.Finish.Matte);
            ModelBox(root, "Buckle", new Vector3(0, .525f, .212f), new Vector3(.055f, .040f, .016f), Hex("D9C77A"), .006f, Quaternion.identity);

            Primitive(root, "Neck", PrimitiveType.Cylinder, new Vector3(0, .82f, 0), new Vector3(.15f, .05f, .15f), SkinShadow, Quaternion.identity);

            foreach (int side in new[] { -1, 1 })
            {
                Quaternion hang = Quaternion.Euler(-10f, 0, -side * 12f);
                Vector3 shoulder = new Vector3(side * .275f, .775f, 0);
                Primitive(root, "Shoulder " + side, PrimitiveType.Sphere, shoulder, new Vector3(.155f, .145f, .150f), top, Quaternion.identity, AirportStyle.Finish.Plastic);
                if (shortSleeves)
                {
                    CharacterLathe(root, "Sleeve " + side, "sleeve", new[]
                    {
                        new Vector2(.064f, -.150f), new Vector2(.066f, -.100f), new Vector2(.068f, -.040f), new Vector2(.050f, .010f)
                    }, 14, shoulder, hang, Vector3.one, top, AirportStyle.Finish.Plastic);
                    CharacterLathe(root, "Sleeve hem " + side, "sleeve hem", new[]
                    {
                        new Vector2(.066f, -.165f), new Vector2(.074f, -.158f), new Vector2(.074f, -.135f), new Vector2(.066f, -.128f)
                    }, 14, shoulder, hang, Vector3.one, topShadow, AirportStyle.Finish.Plastic);
                    CharacterLathe(root, "Forearm " + side, "forearm", new[]
                    {
                        new Vector2(.040f, -.300f), new Vector2(.056f, -.262f), new Vector2(.058f, -.150f), new Vector2(.050f, -.130f)
                    }, 14, shoulder, hang, Vector3.one, Skin, AirportStyle.Finish.Plastic);
                }
                else
                {
                    CharacterLathe(root, "Arm " + side, "arm", new[]
                    {
                        new Vector2(.040f, -.290f), new Vector2(.058f, -.260f), new Vector2(.064f, -.150f), new Vector2(.068f, -.040f), new Vector2(.050f, .010f)
                    }, 14, shoulder, hang, Vector3.one, top, AirportStyle.Finish.Plastic);
                    CharacterLathe(root, "Cuff " + side, "cuff", new[]
                    {
                        new Vector2(.060f, -.300f), new Vector2(.070f, -.292f), new Vector2(.070f, -.262f), new Vector2(.060f, -.254f)
                    }, 14, shoulder, hang, Vector3.one, topShadow, AirportStyle.Finish.Plastic);
                }
                Transform hand = Primitive(root, "Hand " + side, PrimitiveType.Sphere, Vector3.zero, new Vector3(.105f, .115f, .095f), Skin, Quaternion.identity).transform;
                hand.localPosition = shoulder + hang * new Vector3(0, -.335f, 0);
                hand.localRotation = hang;
            }
        }

        /// <summary>头部：蛋形脸 + 后脑头发壳 + 刘海 + 眼睛/眉毛/腮红/鼻/嘴/耳。返回头部根节点（下巴在 y=0）。</summary>
        static Transform BuildCharacterHead(Transform root, Color hair, int look, bool showEars)
        {
            Transform head = new GameObject("Head").transform;
            head.SetParent(root, false);
            head.localPosition = new Vector3(0, .735f, .01f);

            CharacterLathe(head, "Face", "face", new[]
            {
                new Vector2(.001f, 0f), new Vector2(.115f, .018f), new Vector2(.182f, .075f), new Vector2(.212f, .165f),
                new Vector2(.210f, .255f), new Vector2(.185f, .335f), new Vector2(.120f, .395f), new Vector2(.001f, .420f)
            }, 24, Vector3.zero, Quaternion.identity, new Vector3(1f, 1f, .96f), Skin, AirportStyle.Finish.Plastic);

            // 后脑头发壳：只覆盖后 200°，两侧留出鬓角，不遮脸。
            float hairBottom = look == 3 ? -.02f : .13f;
            CharacterLathe(head, "Hair back", "hair back " + look, new[]
            {
                new Vector2(.195f, hairBottom), new Vector2(.225f, .165f), new Vector2(.226f, .255f),
                new Vector2(.202f, .345f), new Vector2(.135f, .410f), new Vector2(.001f, .440f)
            }, 20, new Vector3(0, 0, -.015f), Quaternion.identity, new Vector3(1f, 1f, .98f), hair, AirportStyle.Finish.Matte, 80f, 280f);
            CharacterLathe(head, "Fringe", "fringe", new[]
            {
                new Vector2(.200f, .300f), new Vector2(.218f, .330f), new Vector2(.200f, .370f), new Vector2(.135f, .415f), new Vector2(.001f, .440f)
            }, 24, new Vector3(0, 0, -.005f), Quaternion.identity, new Vector3(1f, 1f, .98f), hair, AirportStyle.Finish.Matte);
            if (look == 1)
                Primitive(head, "Hair bun", PrimitiveType.Sphere, new Vector3(0, .40f, -.17f), new Vector3(.17f, .15f, .15f), hair, Quaternion.identity);

            foreach (int side in new[] { -1, 1 })
            {
                Primitive(head, "Eye " + side, PrimitiveType.Sphere, new Vector3(side * .082f, .225f, .183f), new Vector3(.050f, .072f, .036f), Window, Quaternion.identity, AirportStyle.Finish.Plastic);
                Primitive(head, "Eye light " + side, PrimitiveType.Sphere, new Vector3(side * .072f, .245f, .212f), new Vector3(.016f, .018f, .012f), EyeWhite, Quaternion.identity, AirportStyle.Finish.Plastic);
                ModelBox(head, "Brow " + side, new Vector3(side * .085f, .290f, .186f), new Vector3(.070f, .017f, .018f), hair, .006f, Quaternion.Euler(0, 0, side * 7f));
                Primitive(head, "Cheek " + side, PrimitiveType.Sphere, new Vector3(side * .135f, .150f, .150f), new Vector3(.075f, .040f, .035f), Cheek, Quaternion.identity);
                if (showEars)
                    Primitive(head, "Ear " + side, PrimitiveType.Sphere, new Vector3(side * .205f, .205f, -.015f), new Vector3(.055f, .075f, .045f), Skin, Quaternion.identity, AirportStyle.Finish.Plastic);
            }
            Primitive(head, "Nose", PrimitiveType.Sphere, new Vector3(0, .170f, .210f), new Vector3(.055f, .045f, .050f), SkinShadow, Quaternion.identity, AirportStyle.Finish.Plastic);
            ModelBox(head, "Mouth", new Vector3(0, .105f, .190f), new Vector3(.064f, .015f, .014f), Mouth, .006f, Quaternion.identity);
            return head;
        }

        /// <summary>棒球式工作帽：圆顶、帽带、D 形帽檐、顶扣、前徽。</summary>
        static void BuildCharacterCap(Transform head, Color crown, Color band, Color badge)
        {
            CharacterLathe(head, "Cap crown", "cap crown", new[]
            {
                new Vector2(.228f, .360f), new Vector2(.236f, .395f), new Vector2(.222f, .450f),
                new Vector2(.180f, .495f), new Vector2(.105f, .525f), new Vector2(.001f, .535f)
            }, 24, Vector3.zero, Quaternion.identity, new Vector3(1f, 1f, .97f), crown, AirportStyle.Finish.Plastic);
            CharacterLathe(head, "Cap band", "cap band", new[]
            {
                new Vector2(.232f, .345f), new Vector2(.242f, .355f), new Vector2(.242f, .385f), new Vector2(.232f, .395f)
            }, 24, Vector3.zero, Quaternion.identity, new Vector3(1f, 1f, .97f), band, AirportStyle.Finish.Plastic);
            Primitive(head, "Cap button", PrimitiveType.Sphere, new Vector3(0, .538f, 0), new Vector3(.045f, .030f, .045f), band, Quaternion.identity);

            // D 形帽檐：前缘圆弧，沿 x 对称，稍向下倾斜。
            var bill = new List<Vector2>();
            for (int i = 0; i <= 10; i++)
            {
                float a = Mathf.PI * i / 10f;
                bill.Add(new Vector2(Mathf.Cos(a) * .215f, .04f + Mathf.Sin(a) * .235f));
            }
            var billObject = ExtrudedPolygon(head, "Cap bill", bill.ToArray(), 0f, .024f, crown);
            billObject.transform.localPosition = new Vector3(0, .365f, .02f);
            billObject.transform.localRotation = Quaternion.Euler(10f, 0, 0);
            ModelBox(head, "Cap badge", new Vector3(0, .440f, .222f), new Vector3(.075f, .052f, .012f), badge, .008f, Quaternion.Euler(-14f, 0, 0));
        }

        /// <summary>
        /// 绕 y 轴旋转体：profile.x 为半径，profile.y 为高度；首尾半径接近 0 即闭合。
        /// 同 key 共享网格；法线平滑，接缝顶点合并法线。arcStart/arcEnd 可生成部分弧（如后脑头发壳）。
        /// </summary>
        static GameObject CharacterLathe(Transform parent, string name, string key, Vector2[] profile, int segments,
            Vector3 position, Quaternion rotation, Vector3 scale, Color color, AirportStyle.Finish finish,
            float arcStart = 0f, float arcEnd = 360f)
        {
            Mesh mesh;
            if (!CharacterMeshes.TryGetValue(key, out mesh) || !mesh)
            {
                bool full = Mathf.Approximately(arcEnd - arcStart, 360f);
                int ringCount = segments + 1;
                var vertices = new List<Vector3>(profile.Length * ringCount);
                var triangles = new List<int>((profile.Length - 1) * segments * 6);
                for (int ring = 0; ring < profile.Length; ring++)
                for (int i = 0; i <= segments; i++)
                {
                    float angle = Mathf.Deg2Rad * Mathf.Lerp(arcStart, arcEnd, i / (float)segments);
                    vertices.Add(new Vector3(Mathf.Sin(angle) * profile[ring].x, profile[ring].y, Mathf.Cos(angle) * profile[ring].x));
                }
                for (int ring = 0; ring < profile.Length - 1; ring++)
                for (int i = 0; i < segments; i++)
                {
                    int a = ring * ringCount + i, b = a + ringCount;
                    triangles.Add(a); triangles.Add(a + 1); triangles.Add(b);
                    triangles.Add(a + 1); triangles.Add(b + 1); triangles.Add(b);
                }
                mesh = new Mesh { name = "Character " + key };
                mesh.SetVertices(vertices);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals();
                if (full)
                {
                    Vector3[] normals = mesh.normals;
                    for (int ring = 0; ring < profile.Length; ring++)
                    {
                        int a = ring * ringCount, b = a + segments;
                        normals[a] = normals[b] = (normals[a] + normals[b]).normalized;
                    }
                    mesh.normals = normals;
                }
                mesh.RecalculateBounds();
                CharacterMeshes[key] = mesh;
            }
            var item = new GameObject(name);
            item.transform.SetParent(parent, false);
            item.transform.localPosition = position;
            item.transform.localRotation = rotation;
            item.transform.localScale = scale;
            item.AddComponent<MeshFilter>().sharedMesh = mesh;
            item.AddComponent<MeshRenderer>().sharedMaterial = AirportStyle.SharedMaterial(color, finish);
            return item;
        }
    }
}
