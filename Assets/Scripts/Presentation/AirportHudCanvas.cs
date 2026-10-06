using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace IslandAirport
{
    /// <summary>
    /// M1 局内 HUD：运行时程序化 uGUI Canvas（v7 五区布局），替代 OnGUI 版 AirportHUD。
    /// Screen Space - Camera（worldCamera=游戏相机）：相机渲进 RenderTexture 时 UI 一并入图，
    /// batchmode 证据截图与真机画面一致。参考分辨率 1920×1080，match 0.5。
    /// 触屏输入（摇杆/按住按钮/目标切换）在此组帧后经 AirportGame.SetInput 注入 P1，
    /// 键盘在 ReadInput 内按位合并，P2 不受影响。
    /// </summary>
    public sealed class AirportHudCanvas : MonoBehaviour
    {
        AirportGame game;
        Canvas canvas;
        RectTransform safeRoot, hud, timerPanel, flightBoard, timeline, departedPanel, sandboxPanel, chipRoot, resultsPanel, helpPanel;

        // 顶部：倒计时 pill / 航班信息板 / 胶囊时间线 / 右上按钮
        Text timerText, departedText, fpsText, migrationText;
        Tag teammateIndicator;
        Button retryButton, shareButton;
        RectTransform resultsActions;
        Text resultsProgress, resultsShare;
        Text sandboxRoomText, sandboxPartnerText;
        readonly Text[] standId = new Text[Level1Map.StandCount];
        readonly Text[] standSub = new Text[Level1Map.StandCount];
        readonly Text[] standTime = new Text[Level1Map.StandCount];
        readonly Image[,] standCells = new Image[Level1Map.StandCount, 4];
        readonly Text[,] standCellText = new Text[Level1Map.StandCount, 4];
        readonly List<Image> capsuleBg = new List<Image>();
        readonly List<Image> capsuleAccent = new List<Image>();
        readonly List<Text> capsuleText = new List<Text>();

        // 底部：摇杆 / 交互钮 / 目标 chip / 提示
        VirtualJoystick joystick;
        ActionHoldButton actionButton;
        Text chipText, hintText, toastText;
        Image partnerHintSurface;
        RectTransform toast;
        bool pendingCycle;

        // 世界跟随提示
        sealed class Tag { public RectTransform Root; public Image Bg; public Text Label; public Image WorkFill; }
        sealed class StationCard { public RectTransform Root; public Text Title; public Image Fill; }
        readonly Tag[] crewTags = new Tag[2];
        StationCard mealCard, fuelCard;

        // 大厅 / 结算 / 手册动态文本
        Text resultsStars, resultsTitle, resultsStats, resultsHint;
        Image replayFill;
        public string VisibleTimer { get { return timerPanel != null && timerPanel.gameObject.activeInHierarchy ? timerText.text : string.Empty; } }
        public string VisibleMigrationStatus { get { return migrationText != null && migrationText.gameObject.activeInHierarchy ? migrationText.text : string.Empty; } }
        public string VisibleTeammateIndicator { get { return teammateIndicator != null && teammateIndicator.Root.gameObject.activeInHierarchy ? teammateIndicator.Label.text : string.Empty; } }
        public Vector2 TeammateIndicatorPosition { get { return teammateIndicator == null ? Vector2.zero : teammateIndicator.Root.anchoredPosition; } }
        public Rect? ReviewSafeAreaNormalized;
        public bool TeammateIndicatorInsideSafeArea
        {
            get
            {
                if (teammateIndicator == null || !teammateIndicator.Root.gameObject.activeInHierarchy) return false;
                Vector2 center = teammateIndicator.Root.anchoredPosition, half = teammateIndicator.Root.rect.size * 0.5f;
                Rect safe = safeRoot.rect;
                return center.x - half.x >= safe.xMin && center.x + half.x <= safe.xMax && center.y - half.y >= safe.yMin && center.y + half.y <= safe.yMax;
            }
        }
        public bool DeparturePillOverlapsBoard { get { return hud != null && RelativeHudRect(departedPanel).Overlaps(RelativeHudRect(flightBoard)); } }
        public bool ToolbarInsideSafeArea
        {
            get
            {
                if (hud == null) return false;
                Rect safe = hud.rect;
                foreach (string name in new[] { "Mic Button", "Gear Button" })
                {
                    var item = hud.Find(name) as RectTransform;
                    if (item == null) return false;
                    Rect bounds = RelativeHudRect(item);
                    if (bounds.xMin < safe.xMin || bounds.xMax > safe.xMax || bounds.yMin < safe.yMin || bounds.yMax > safe.yMax) return false;
                }
                return true;
            }
        }
        Rect RelativeHudRect(RectTransform item)
        {
            var corners = new Vector3[4]; item.GetWorldCorners(corners);
            Vector2 min = hud.InverseTransformPoint(corners[0]), max = min;
            for (int i = 1; i < corners.Length; i++) { Vector2 point = hud.InverseTransformPoint(corners[i]); min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
        public bool ResultsVisible { get { return resultsPanel != null && resultsPanel.gameObject.activeInHierarchy; } }
        public bool RetryInteractable { get { return retryButton != null && retryButton.interactable; } }
        public bool ShareInteractable { get { return shareButton != null && shareButton.interactable; } }
        public string VisibleResultStars { get { return resultsStars == null ? string.Empty : resultsStars.text; } }
        public string VisibleSettlementStatus { get { return resultsProgress == null ? string.Empty : resultsProgress.text; } }
        public string VisibleResultToast { get { return toast != null && toast.gameObject.activeInHierarchy ? toastText.text : string.Empty; } }
        public bool ResultsActionsInsideSafeArea
        {
            get
            {
                if (resultsActions == null) return false;
                var corners = new Vector3[4]; resultsActions.GetWorldCorners(corners);
                foreach (var point in corners)
                {
                    Vector2 local = safeRoot.InverseTransformPoint(point);
                    if (local.x < safeRoot.rect.xMin - 1 || local.x > safeRoot.rect.xMax + 1 || local.y < safeRoot.rect.yMin - 1 || local.y > safeRoot.rect.yMax + 1) return false;
                }
                return true;
            }
        }
        public Button ResultButton(string name)
        { var item = resultsActions == null ? null : resultsActions.Find(name); return item == null ? null : item.GetComponent<Button>(); }

        float fpsAccum; int fpsFrames; float fpsNext;

        /// <summary>当前结算遮罩上实际可见的回放提示，供编辑器验收读取。</summary>
        public string VisibleResultsHint
        {
            get
            {
                return resultsPanel != null && resultsHint != null &&
                    resultsPanel.gameObject.activeInHierarchy && resultsHint.gameObject.activeInHierarchy
                    ? resultsHint.text : string.Empty;
            }
        }

        void Awake()
        {
            game = GetComponent<AirportGame>();
        }

        void Start()
        {
            UiKit.EnsureEventSystem();
            BuildCanvas();
            BuildHud();
            BuildSandboxPanel();
            BuildResults();
            BuildHelp();
        }

        void BuildCanvas()
        {
            var canvasObject = new GameObject("Airport HUD Canvas");
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = game.View;
            canvas.planeDistance = 1.0f;
            canvas.sortingOrder = 10;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            var rootObject = new GameObject("Safe Area", typeof(RectTransform));
            safeRoot = (RectTransform)rootObject.transform;
            safeRoot.SetParent(canvas.transform, false);
            safeRoot.anchorMin = Vector2.zero;
            safeRoot.anchorMax = Vector2.one;
            safeRoot.offsetMin = safeRoot.offsetMax = Vector2.zero;
        }

        // ---------- 局内 HUD ----------
        void BuildHud()
        {
            hud = UiKit.Node(safeRoot, "In Game HUD", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            hud.offsetMin = hud.offsetMax = Vector2.zero;

            // 左上：班岗倒计时 pill
            var timerPill = UiKit.Panel(hud, "Timer Pill", AirportStyle.Paper, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, 24), new Vector2(220, 84));
            timerPanel = (RectTransform)timerPill.transform;
            UiKit.Elevate(timerPill);
            UiKit.LabelAt(timerPill.transform as RectTransform, "Caption", "PALM BAY · 班岗剩余", new Vector2(20, 10), new Vector2(180, 22), 14, AirportStyle.Muted, FontStyle.Normal);
            timerText = UiKit.LabelAt(timerPill.transform as RectTransform, "Timer", "05:00", new Vector2(20, 34), new Vector2(180, 46), 38, AirportStyle.Ink, FontStyle.Bold);

            BuildBoard();

            // 右上：送走统计 + 麦克风 + 齿轮
            var departedPill = UiKit.Panel(hud, "Departed Pill", AirportStyle.Paper, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, 112), new Vector2(180, 56));
            departedPanel = (RectTransform)departedPill.transform;
            UiKit.Elevate(departedPill);
            departedText = UiKit.LabelAt(departedPill.transform as RectTransform, "Text", "共同送走 0/5", new Vector2(0, 0), new Vector2(180, 56), 21, AirportStyle.Ink, FontStyle.Bold, TextAnchor.MiddleCenter);
            // 圆钮图标用文字占位（动态字体无 emoji 字形），中文“麦/停”。
            // 角钮加大并内移：56px 贴 24px 边距在真机上太小、离边缘太近难命中。
            var mic = UiKit.RoundButton(hud, "Mic Button", "麦", AirportStyle.Paper, Vector2.zero, new Vector2(72, 72), () => game.Notify("语音将在后续版本开放"), 22);
            mic.GetComponentInChildren<Text>().color = AirportStyle.Ink;
            var gear = UiKit.RoundButton(hud, "Gear Button", "停", AirportStyle.Paper, Vector2.zero, new Vector2(72, 72), () => { if (!game.MatchFinished) game.Paused = !game.Paused; }, 22);
            gear.GetComponentInChildren<Text>().color = AirportStyle.Ink;
            AnchorTopRight((RectTransform)mic.transform, new Vector2(-112, -24));
            AnchorTopRight((RectTransform)gear.transform, new Vector2(-24, -24));
            fpsText = UiKit.LabelAt(hud, "FPS", "", new Vector2(-124, 176), new Vector2(100, 20), 13, AirportStyle.Paper, FontStyle.Normal, TextAnchor.MiddleRight);
            var fpsHost = (RectTransform)fpsText.transform.parent;
            fpsHost.anchorMin = fpsHost.anchorMax = new Vector2(1, 1);

            // 左下：虚拟摇杆
            joystick = VirtualJoystick.Create(hud, "Move Joystick");
            ((RectTransform)joystick.transform).anchorMin = ((RectTransform)joystick.transform).anchorMax = new Vector2(0, 0);
            ((RectTransform)joystick.transform).pivot = new Vector2(0, 0);
            ((RectTransform)joystick.transform).anchoredPosition = new Vector2(48, 48);

            // 右下：交互大圆钮 + 上方目标 chip
            actionButton = ActionHoldButton.Create(hud, "Action Button");
            actionButton.Label.fontStyle = FontStyle.Normal;
            actionButton.Label.fontSize = 30;
            actionButton.Label.raycastTarget = false;
            UiKit.Elevate(actionButton.GetComponent<Image>());
            var actionRect = (RectTransform)actionButton.transform;
            actionRect.anchorMin = actionRect.anchorMax = new Vector2(1, 0);
            actionRect.pivot = new Vector2(1, 0);
            actionRect.anchoredPosition = new Vector2(-48, 56);
            var chip = UiKit.RoundButton(hud, "Target Chip", "目标 —", AirportStyle.Ink, new Vector2(-290, -230), new Vector2(220, 52), () => { pendingCycle = true; }, 20);
            chipText = chip.GetComponentInChildren<Text>();
            var chipRect = (RectTransform)chip.transform;
            chipRoot = chipRect;
            chipRect.anchorMin = chipRect.anchorMax = new Vector2(1, 0);
            chipRect.pivot = new Vector2(1, 0);
            chipRect.anchoredPosition = new Vector2(-40, 244);

            // 底部中央：P2 提示条（P2 仍是键盘）
            partnerHintSurface = UiKit.Panel(hud, "Partner hint surface", new Color(0.08f, 0.20f, 0.24f, 0.58f), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(0, -72), new Vector2(824, 38), false);
            hintText = UiKit.LabelAt(hud, "P2 Hint", "", new Vector2(0, 66), new Vector2(760, 26), 16, AirportStyle.Paper, FontStyle.Normal, TextAnchor.MiddleCenter);
            var hintRect = (RectTransform)hintText.transform.parent;
            hintRect.anchorMin = hintRect.anchorMax = new Vector2(0.5f, 0);
            hintRect.pivot = new Vector2(0.5f, 1f);
            hintRect.anchoredPosition = new Vector2(0, 66);

            // Toast
            toast = UiKit.Node(safeRoot, "Toast", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 108), new Vector2(760, 54));
            var toastBg = toast.gameObject.AddComponent<Image>();
            toastBg.sprite = AirportStyle.RoundedSprite; toastBg.type = Image.Type.Sliced; toastBg.color = AirportStyle.Ink; toastBg.raycastTarget = false;
            toastText = UiKit.Label(toast, "Text", "", 20, Color.white, FontStyle.Normal, TextAnchor.MiddleCenter);

            migrationText = UiKit.LabelAt(hud, "Authority status", "", new Vector2(0, 268), new Vector2(840, 28), 18, AirportStyle.Paper, FontStyle.Bold, TextAnchor.MiddleCenter);
            var migrationRect = (RectTransform)migrationText.transform.parent;
            migrationRect.anchorMin = migrationRect.anchorMax = new Vector2(0.5f, 1);
            migrationRect.pivot = new Vector2(0.5f, 1);
            migrationRect.anchoredPosition = new Vector2(0, -268);
            BuildWorldHints();
        }

        static void AnchorTopRight(RectTransform target, Vector2 position)
        {
            target.anchorMin = target.anchorMax = new Vector2(1, 1);
            target.pivot = new Vector2(1, 1);
            target.anchoredPosition = position;
        }

        void BuildBoard()
        {
            // 顶部中央航班信息板：表头四任务色字 + 每机位一行 + 底部 5 航班胶囊时间线。
            var board = UiKit.Panel(hud, "Flight Board", AirportStyle.Ink, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, 18), new Vector2(940, 178));
            var boardRect = (RectTransform)board.transform;
            UiKit.Elevate(board, true);
            flightBoard = boardRect;
            UiKit.Panel(boardRect, "Header rule", new Color(0.78f, 0.90f, 0.91f, 0.14f), new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, 33), new Vector2(892, 1), false);
            UiKit.LabelAt(boardRect, "Col Flight", "航班 / 目的地", new Vector2(24, 10), new Vector2(200, 22), 14, AirportStyle.Hex("BCD1D7"));
            for (int k = 0; k < 4; k++)
            {
                var service = (ServiceKind)k;
                UiKit.LabelAt(boardRect, "Col " + k, AirportGame.Names[k], new Vector2(300 + k * 120, 10), new Vector2(110, 22), 15, AirportStyle.ServiceColor(service), FontStyle.Bold, TextAnchor.MiddleCenter);
            }
            UiKit.LabelAt(boardRect, "Col Time", "离港", new Vector2(790, 10), new Vector2(126, 22), 14, AirportStyle.Hex("BCD1D7"), FontStyle.Normal, TextAnchor.MiddleCenter);

            for (int s = 0; s < Level1Map.StandCount; s++)
            {
                float y = 36 + s * 46;
                if (s == 1)
                    UiKit.Panel(boardRect, "Alternate row", new Color(0.80f, 0.92f, 0.92f, 0.045f), new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(12, y), new Vector2(916, 44), false);
                standId[s] = UiKit.LabelAt(boardRect, "Flight " + s, "", new Vector2(24, y), new Vector2(110, 24), 20, Color.white, FontStyle.Bold);
                standSub[s] = UiKit.LabelAt(boardRect, "Sub " + s, "", new Vector2(24, y + 22), new Vector2(260, 18), 12, AirportStyle.Hex("BCD1D7"));
                for (int k = 0; k < 4; k++)
                {
                    var cell = UiKit.Panel(boardRect, "Cell " + s + " " + k, AirportStyle.InkSoft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(332 + k * 120, y + 2), new Vector2(48, 30), false);
                    standCells[s, k] = cell;
                    standCellText[s, k] = UiKit.Label(cell.transform as RectTransform, "Text", "—", 15, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
                }
                standTime[s] = UiKit.LabelAt(boardRect, "Time " + s, "", new Vector2(790, y), new Vector2(126, 36), 24, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
            }

            // 胶囊时间线（信息板下方一行）
            var strip = UiKit.Node(hud, "Flight Timeline", new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -206), new Vector2(960, 44));
            timeline = strip;
            for (int i = 0; i < 5; i++)
            {
                var pill = UiKit.Panel(strip, "Capsule " + i, AirportStyle.Paper, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(i * 192 + 8, 0), new Vector2(180, 40), false);
                capsuleBg.Add(pill);
                var pillRoot = (RectTransform)pill.transform;
                capsuleAccent.Add(UiKit.Panel(pillRoot, "Status accent", AirportStyle.Teal, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(3, 18), false));
                capsuleText.Add(UiKit.Label(pillRoot, "Text", "", 14, AirportStyle.Ink, FontStyle.Normal, TextAnchor.MiddleCenter));
            }
        }

        void BuildWorldHints()
        {
            var hints = UiKit.Node(hud, "World Hints", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            hints.offsetMin = hints.offsetMax = Vector2.zero;
            for (int i = 0; i < 2; i++)
            {
                var root = UiKit.Node(hints, "Crew Tag " + i, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(132, 34));
                var bg = root.gameObject.AddComponent<Image>();
                bg.sprite = AirportStyle.RoundedSprite; bg.type = Image.Type.Sliced; bg.raycastTarget = false;
                var label = UiKit.Label(root, "Label", "P" + (i + 1), 13, AirportStyle.Ink, FontStyle.Bold, TextAnchor.MiddleCenter);
                label.resizeTextForBestFit = true; label.resizeTextMinSize = 10; label.resizeTextMaxSize = 13;
                Image fill;
                UiKit.Bar(root, "Work", new Vector2(2, 38), new Vector2(76, 7), new Color(0.12f, 0.23f, 0.29f, 0.35f), out fill);
                crewTags[i] = new Tag { Root = root, Bg = bg, Label = label, WorkFill = fill };
            }
            var edge = UiKit.Node(hud, "Offscreen teammate", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(190, 42));
            var edgeBg = edge.gameObject.AddComponent<Image>();
            edgeBg.sprite = AirportStyle.RoundedSprite; edgeBg.type = Image.Type.Sliced; edgeBg.raycastTarget = false;
            teammateIndicator = new Tag { Root = edge, Bg = edgeBg, Label = UiKit.Label(edge, "Name and direction", "", 16, AirportStyle.Ink, FontStyle.Bold, TextAnchor.MiddleCenter) };
            teammateIndicator.Label.resizeTextForBestFit = true; teammateIndicator.Label.resizeTextMinSize = 10; teammateIndicator.Label.resizeTextMaxSize = 16;
            mealCard = BuildStationCard(hints, "Meals Card");
            fuelCard = BuildStationCard(hints, "Fuel Card");
        }

        StationCard BuildStationCard(RectTransform parent, string name)
        {
            var root = UiKit.Node(parent, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(150, 46));
            var bg = root.gameObject.AddComponent<Image>();
            bg.sprite = AirportStyle.RoundedSprite; bg.type = Image.Type.Sliced; bg.color = AirportStyle.Paper; bg.raycastTarget = false;
            var title = UiKit.LabelAt(root, "Title", "", new Vector2(10, 4), new Vector2(130, 24), 15, AirportStyle.Ink, FontStyle.Bold, TextAnchor.MiddleCenter);
            Image fill;
            UiKit.Bar(root, "Bar", new Vector2(13, 32), new Vector2(124, 6), new Color(0.12f, 0.23f, 0.29f, 0.15f), out fill);
            return new StationCard { Root = root, Title = title, Fill = fill };
        }

        void BuildSandboxPanel()
        {
            // M3.1 任务练习头版：房名 + 练习计时（client 读镜像 MirrorElapsed）+ 返回大厅。
            // 宽度收窄到 440，给顶部航班信息板留位（任务练习可见四任务进度）。
            sandboxPanel = UiKit.Node(hud, "Practice Header", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, 24), new Vector2(440, 84));
            var panel = sandboxPanel.gameObject.AddComponent<Image>();
            panel.sprite = AirportStyle.RoundedSprite; panel.type = Image.Type.Sliced; panel.color = AirportStyle.Paper; panel.raycastTarget = false;
            sandboxRoomText = UiKit.LabelAt(sandboxPanel, "Room", "任务练习", new Vector2(18, 10), new Vector2(240, 30), 19, AirportStyle.Ink, FontStyle.Bold);
            sandboxPartnerText = UiKit.LabelAt(sandboxPanel, "Timer", "练习 00:00", new Vector2(18, 42), new Vector2(240, 34), 22, AirportStyle.Muted, FontStyle.Bold);
            UiKit.RoundButton(sandboxPanel, "Return", "返回大厅", AirportStyle.Teal, new Vector2(268, 12), new Vector2(152, 60), () => game.BackToLobby(), 18);
        }

        // ---------- 结算 ----------
        void BuildResults()
        {
            resultsPanel = UiKit.Node(safeRoot, "Results", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            resultsPanel.offsetMin = resultsPanel.offsetMax = Vector2.zero;
            var panel = UiKit.Panel(resultsPanel, "Results Summary", new Color(.96f,.97f,.93f,.93f), new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, 18), new Vector2(1120, 150));
            var r = (RectTransform)panel.transform;
            UiKit.Elevate(panel);
            resultsStars = UiKit.LabelAt(r, "Stars", "★★★", new Vector2(22, 16), new Vector2(190, 65), 44, AirportStyle.Hex("D99B3F"), FontStyle.Bold, TextAnchor.MiddleCenter);
            UiKit.LabelAt(r, "Brand", "PALM BAY · 班岗结束", new Vector2(22, 87), new Vector2(190, 24), 16, AirportStyle.Teal, FontStyle.Bold, TextAnchor.MiddleCenter);
            resultsTitle = UiKit.LabelAt(r, "Title", "", new Vector2(236, 14), new Vector2(862, 36), 28, AirportStyle.Ink, FontStyle.Bold);
            resultsStats = UiKit.LabelAt(r, "Stats", "", new Vector2(236, 56), new Vector2(862, 25), 17, AirportStyle.Muted);
            resultsHint = UiKit.LabelAt(r, "Replay Hint", "", new Vector2(236, 94), new Vector2(862, 25), 17, AirportStyle.Teal);
            Image fill;
            UiKit.Bar(r, "Replay", new Vector2(236, 133), new Vector2(862, 4), new Color(.12f,.23f,.29f,.12f), out fill);
            replayFill = fill;
            var actions = UiKit.Panel(resultsPanel, "Results Actions", new Color(.96f,.97f,.93f,.91f), new Vector2(.5f,0), new Vector2(.5f,0), new Vector2(.5f,0), new Vector2(0,-18), new Vector2(1120,160));
            resultsActions = (RectTransform)actions.transform;
            UiKit.Elevate(actions);
            resultsProgress = UiKit.LabelAt(resultsActions, "Progress", "", new Vector2(24, 10), new Vector2(1072, 24), 16, AirportStyle.Muted, FontStyle.Normal, TextAnchor.MiddleCenter);
            resultsShare = UiKit.LabelAt(resultsActions, "Share Status", "", new Vector2(24, 39), new Vector2(1072, 24), 16, AirportStyle.Teal, FontStyle.Normal, TextAnchor.MiddleCenter);
            shareButton = UiKit.RoundButton(resultsActions, "Share", "分享", AirportStyle.Teal, new Vector2(24, 82), new Vector2(256, 58), () => game.ShareReplay());
            retryButton = UiKit.RoundButton(resultsActions, "Retry", "再试一次", AirportStyle.Teal, new Vector2(296, 82), new Vector2(256, 58), () => game.Retry());
            UiKit.RoundButton(resultsActions, "Next", "下一关 · 待开放", AirportStyle.Hex("D3D8D3"), new Vector2(568, 82), new Vector2(256, 58), () => game.Notify("后续关卡还未解锁")).GetComponentInChildren<Text>().color = AirportStyle.Ink;
            UiKit.RoundButton(resultsActions, "Lobby", "返回大厅", AirportStyle.Muted, new Vector2(840, 82), new Vector2(256, 58), () => game.BackToLobby());
        }

        // ---------- 手册 / 暂停 ----------
        void BuildHelp()
        {
            helpPanel = UiKit.Node(safeRoot, "Help", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            helpPanel.offsetMin = helpPanel.offsetMax = Vector2.zero;
            var dim = UiKit.Panel(helpPanel, "Dim", new Color(0.06f, 0.14f, 0.18f, 0.85f), Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            dim.rectTransform.offsetMin = dim.rectTransform.offsetMax = Vector2.zero;
            var panel = UiKit.Panel(helpPanel, "Help Card", AirportStyle.Paper, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(880, 700));
            var r = (RectTransform)panel.transform;
            UiKit.Elevate(panel);
            UiKit.LabelAt(r, "Title", "第一次值班 · 地勤手册", new Vector2(44, 30), new Vector2(780, 46), 30, AirportStyle.Ink, FontStyle.Bold);
            string[] titles = { "01  先分工，再出发", "02  餐食：下单 → 等待 → 装车 → 交付", "03  行李：先卸到达，再送出发", "04  燃油：提前补油，搭档接力", "05  登机：最后放行，注意让路" };
            string[] copy = {
                "左下摇杆 / WASD 移动，右下大圆钮交互；Q / Enter 选择目标航班。空地上点按交互放开车辆。",
                "靠近餐食站点按下单，5 秒备好；开餐车来取，机位旁按住交互卸货。",
                "开空行李车去机位取到达行李，送回行李站；再装载目标航班的行李并交付。",
                "油车停站边：点油站拿枪→点油车插枪→点油站开阀，再点关阀油枪自动归位。到机位：点油车取管→点飞机接管自动加注→再点飞机断开。加满后 2 秒内不关阀 / 断管会漫油。",
                "餐食与燃油完成后，空手在登机口点按交互放行所选航班。再点按关口会暂停放行，已出发旅客继续前进；旅客过斑马线时会挡车。"
            };
            for (int i = 0; i < 5; i++)
            {
                float y = 96 + i * 88;
                UiKit.LabelAt(r, "H" + i, titles[i], new Vector2(44, y), new Vector2(780, 26), 19, AirportStyle.Teal, FontStyle.Bold);
                UiKit.LabelAt(r, "C" + i, copy[i], new Vector2(44, y + 30), new Vector2(780, 44), 16, AirportStyle.Muted);
            }
            UiKit.RoundButton(r, "Continue", "准备好了，继续", AirportStyle.Teal, new Vector2(44, 560), new Vector2(380, 56), () => { game.Paused = false; game.Help = false; });
            UiKit.RoundButton(r, "Quit", "结束试玩，返回大厅", AirportStyle.Hex("E4E8E1"), new Vector2(448, 560), new Vector2(380, 56), () => game.BackToLobby()).GetComponentInChildren<Text>().color = AirportStyle.Ink;
        }

        // ---------- 每帧刷新 ----------
        void LateUpdate() { RefreshNow(); }
        /// <summary>立即刷新 HUD。LateUpdate 每帧调用；编辑器截图工具在 Render 前手动调用，避开 batchmode 播放器循环停摆。</summary>
        public void RefreshNow()
        {
            if (canvas == null) return;
            ApplySafeArea();
            bool inGame = game.Started;
            bool practice = !game.NetworkShift && (game.Sandbox || game.Remote);
            hud.gameObject.SetActive(inGame && !game.MatchFinished);
            timerPanel.gameObject.SetActive(inGame && !practice);
            // M3.1 任务练习：航班信息板与得分 pill 在练习模式同样可见（四任务进度双端一致）。
            flightBoard.gameObject.SetActive(inGame);
            timeline.gameObject.SetActive(inGame && !practice);
            departedPanel.gameObject.SetActive(inGame);
            sandboxPanel.gameObject.SetActive(inGame && practice);
            resultsPanel.gameObject.SetActive(inGame && !practice && game.MatchFinished);
            migrationText.text = game.MigrationStatus;
            migrationText.gameObject.SetActive(inGame && !string.IsNullOrEmpty(game.MigrationStatus));
            helpPanel.gameObject.SetActive(game.Paused || game.Help);
            if (!inGame) return;

            if (practice) RefreshPracticeHeader();
            if (game.Sim != null) RefreshTop();
            RefreshBottom();
            RefreshWorldHints();
            PushTouchInput();
            UpdateFps();
        }

        void ApplySafeArea()
        {
            Rect safe = ReviewSafeAreaNormalized ?? new Rect(Screen.safeArea.xMin / Screen.width, Screen.safeArea.yMin / Screen.height, Screen.safeArea.width / Screen.width, Screen.safeArea.height / Screen.height);
            Vector2 min = safe.min;
            Vector2 max = safe.max;
            if (min != safeRoot.anchorMin || max != safeRoot.anchorMax)
            {
                safeRoot.anchorMin = min; safeRoot.anchorMax = max;
                safeRoot.offsetMin = safeRoot.offsetMax = Vector2.zero;
            }
        }

        bool touchActiveLast;
        void PushTouchInput()
        {
            // 组帧注入 P1；键盘在 ReadInput 内按位合并，P2 不受影响。
            // 空闲帧不发送（避免覆盖脚本/键盘注入），但松开后的首个空帧必须送达以清掉 Held。
            bool active = joystick.Value.sqrMagnitude > 0.0001f || actionButton.Held || actionButton.Pressed || pendingCycle;
            if (!active && !touchActiveLast) return;
            var input = new CrewInput { Move = joystick.Value, Held = actionButton.Held, Pressed = actionButton.Pressed, Cycle = pendingCycle };
            actionButton.Pressed = false;
            pendingCycle = false;
            touchActiveLast = active;
            game.SetInput(game.NetworkShift ? game.LocalSeat : 0, input); // Practice uses channel 0; finite authority maps the stable local seat.
        }

        void RefreshTop()
        {
            bool practice = !game.NetworkShift && (game.Sandbox || game.Remote);
            // 计时源（M3.1）：host/本地读权威 Sim；client 镜像端 Sim.Elapsed 恒 0，读 MirrorElapsed。
            float elapsed = game.DisplayElapsed;
            if (!practice)
            {
                float left = game.DisplayRemaining;
                timerText.text = string.Format("{0:00}:{1:00}", (int)left / 60, (int)left % 60);
                timerText.color = left < 30 ? AirportStyle.Warn : AirportStyle.Ink;
                departedText.text = "共同送走 " + game.DisplayDeparted + "/5";
            }
            else
            {
                // 任务练习无 300s 结算与星级：得分 pill 只显示当前得分（client 读镜像分）。
                departedText.text = "得分 " + (game.Remote ? game.Shift.MirrorScore : game.Sim.Score);
            }

            for (int s = 0; s < Level1Map.StandCount; s++)
            {
                Flight f = game.Sim.ActiveAtStand(s);
                if (f == null)
                {
                    standId[s].text = "机位 " + "ABC"[s];
                    standSub[s].text = "等待下一架航班";
                    standTime[s].text = "";
                    for (int k = 0; k < 4; k++) { standCells[s, k].color = AirportStyle.InkSoft; standCellText[s, k].text = ""; }
                    continue;
                }
                standId[s].text = f.Id;
                standSub[s].text = f.Destination + (!f.ArrivalBagsReturned ? "  ·  到达行李待卸" : "  ·  地勤作业中");
                for (int k = 0; k < 4; k++)
                {
                    bool failed = game.Shift != null && game.Shift.IsTaskFailed(f.Id, (ServiceKind)k);
                    bool done = f.Progress[k] >= 1;
                    standCells[s, k].color = failed ? AirportStyle.Warn : done ? AirportStyle.Good : AirportStyle.InkSoft;
                    standCellText[s, k].text = failed ? "失败" : done ? "✓" : f.Progress[k] > 0 ? Mathf.RoundToInt(f.Progress[k] * 100) + "%" : "—";
                    standCellText[s, k].color = done && !failed ? AirportStyle.Ink : Color.white;
                    standCellText[s, k].fontSize = failed ? 12 : 15;
                }
                float time = Mathf.Max(0, f.Deadline - elapsed);
                standTime[s].text = string.Format("{0:0}:{1:00}", (int)time / 60, (int)time % 60);
                standTime[s].color = time < 30 ? AirportStyle.Hex("FFC385") : Color.white;
            }

            if (practice) return;
            for (int i = 0; i < capsuleText.Count; i++)
            {
                if (i >= game.Sim.Flights.Count) { capsuleBg[i].gameObject.SetActive(false); continue; }
                capsuleBg[i].gameObject.SetActive(true);
                Flight f = game.Sim.Flights[i];
                capsuleBg[i].color = f.Status == FlightStatus.Departed ? AirportStyle.Hex("B3DECA") : f.Status == FlightStatus.Missed ? AirportStyle.Hex("E7B6A2") : AirportStyle.Paper;
                capsuleAccent[i].color = f.Status == FlightStatus.Missed ? AirportStyle.Warn : f.Status == FlightStatus.Departed ? AirportStyle.Good : f.Status == FlightStatus.Servicing ? AirportStyle.Teal : AirportStyle.Hex("AFBBB4");
                string state = f.Status == FlightStatus.Departed ? "已起飞" : f.Status == FlightStatus.Missed ? "已延误" : f.Status == FlightStatus.Servicing ? "作业中" : f.ArrivalTime <= elapsed ? "等待机位" : "+" + Mathf.CeilToInt(f.ArrivalTime - elapsed) + "s";
                capsuleText[i].text = f.Id + "  " + state;
            }
        }

        void RefreshBottom()
        {
            bool practice = !game.NetworkShift && (game.Sandbox || game.Remote);
            // M3.1：目标 chip 在任务练习同样可见（client 经快照 Sel 下标只读显示目标航班）。
            chipRoot.gameObject.SetActive(true);
            Crew p1 = game.Crew[game.LocalSeat];
            actionButton.Label.text = game.Remote && game.RemoteSeat>=0 && game.RemoteSeat<game.RemoteLabels.Length
                ? game.RemoteLabels[game.RemoteSeat] : game.ActionLabel(p1);
            Flight selected = game.Remote ? MirrorSelected() : game.SelectedFlight(p1);
            chipText.text = "目标 " + (selected == null ? "—" : selected.Id) + " ▾";
            Crew p2 = game.Crew.Count > 1 ? game.Crew[1 - game.LocalSeat] : null;
            hintText.text = practice ? (p2 == null ? "" : (p2.Bot ? "BOT 搭档 · " : p2.Name + " · ") + (game.Remote ? "联机任务练习 · 配合完成四类地勤任务" : "任务练习 · 航班循环到场"))
                : p2 == null ? "" : game.NetworkShift ? (p2.Bot ? "BOT 搭档 · " : p2.Name + " · ") + "共同执勤 · 配合完成四类地勤任务" : (p2.Bot ? "BOT 搭档协作中 · " : "P2 方向键 · 右 Shift · Enter    ") + game.Context(p2);
            partnerHintSurface.gameObject.SetActive(!string.IsNullOrEmpty(hintText.text));

            bool showToast = game.ToastTime > 0 && !game.ExportRendering;
            toast.gameObject.SetActive(showToast);
            if (showToast) { toastText.text = game.Toast; toast.anchoredPosition = new Vector2(0, game.MatchFinished ? 196 : 108); toast.SetAsLastSibling(); }

            if (game.MatchFinished && !practice)
            {
                resultsStars.text = new string('★', game.DisplayStars) + new string('☆', 3 - game.DisplayStars);
                resultsTitle.text = "一起送走了 " + game.DisplayDeparted + " 架飞机";
                resultsStats.text = "共同得分 " + game.DisplayScore + "   ·   星级按送走架数：1 架 ★ / 2 架 ★★ / 4 架 ★★★";
                if (game.ExportRendering)
                    resultsHint.text = game.ExportCaption;
                else if (game.ReplayNoticeRemaining > 0f && !string.IsNullOrEmpty(game.ReplayNotice))
                    resultsHint.text = game.ReplayNotice;
                else if (game.NetworkShift && !string.IsNullOrEmpty(game.ReplayStatus) && game.ReplayStatus != "共同回放已同步")
                    resultsHint.text = game.ReplayStatus;
                else if (!game.ReplayAvailable)
                    resultsHint.text = "本班次没有可回放的路线。";
                else
                    resultsHint.text = game.ReplayFraction < 1 ? "正在回看地勤路线 · 12 倍速" : "这一班辛苦了。下一次，一定更默契。";
                retryButton.interactable = game.CanRetry;
                retryButton.GetComponentInChildren<Text>().text = game.CanRetry ? "再试一次" : "等待房主重开";
                resultsActions.gameObject.SetActive(!game.ExportRendering);
                resultsProgress.text = game.SettlementStatus;
                resultsShare.text = game.ExportStatus;
                shareButton.interactable = game.ReplayAvailable && !game.ExportBusy && !game.ShareBusy;
                shareButton.GetComponentInChildren<Text>().text = game.ExportBusy ? "正在导出…" : game.ShareBusy ? "正在分享…" : "分享";
                UiKit.SetBar(replayFill, 862, 4, game.ExportRendering ? game.ExportFraction : game.ReplayFraction, AirportStyle.Teal);
            }
        }

        /// <summary>client 目标 chip 的只读镜像查找：快照 Sel 下标有效且服务中即用，否则跟随首个服务中航班。</summary>
        Flight MirrorSelected()
        {
            if (game.Sim == null || game.RemoteSeat < 0 || game.RemoteSeat >= game.Crew.Count) return null;
            Crew me = game.Crew[game.RemoteSeat];
            if (me.Selected >= 0 && me.Selected < game.Sim.Flights.Count &&
                game.Sim.Flights[me.Selected].Status == FlightStatus.Servicing) return game.Sim.Flights[me.Selected];
            foreach (var f in game.Sim.Flights) if (f.Status == FlightStatus.Servicing) return f;
            return null;
        }

        void RefreshWorldHints()
        {
            bool visible = !game.MatchFinished;
            for (int i = 0; i < 2; i++)
            {
                var tag = crewTags[i];
                Crew c = game.Crew[i];
                bool actorVisible=visible && c.Visual && c.Visual.gameObject.activeSelf;
                tag.Root.gameObject.SetActive(actorVisible);
                if (!actorVisible) continue;
                PlaceAtWorld(tag.Root, c.Position + Vector3.up * 2.0f);
                tag.Bg.color = c.Color;
                tag.Label.text = c.Name + (c.Bot ? " · BOT" : "");
                UiKit.SetBar(tag.WorkFill, 76, 7, game.DeliverWorkOf(c), AirportStyle.Teal);
            }
            RefreshTeammateIndicator(visible);
            bool showStations=visible;
            ShiftSim shift = game.Shift;
            PlaceStationCard(mealCard, AirportGame.Station(ServiceKind.Meals), shift == null ? 0 : shift.MealProgress01,
                shift != null && shift.Meal == MealPhase.Ready ? "餐食待取" : shift != null && shift.Meal == MealPhase.Ordered ? "备餐中" : "餐食中心", AirportStyle.Meals, showStations);
            Vector3 fuelTruck = game.Carts.Count > (int)ServiceKind.Fuel
                ? game.Carts[(int)ServiceKind.Fuel].Position
                : AirportGame.Station(ServiceKind.Fuel);
            PlaceStationCard(fuelCard, fuelTruck, game.FuelTruckTankDisplay,
                "油车储量", AirportStyle.Fuel, showStations);
        }

        void RefreshTeammateIndicator(bool visible)
        {
            int other = 1 - game.LocalSeat;
            Crew teammate = game.Crew.Count > other ? game.Crew[other] : null;
            if (!visible || teammate == null || !teammate.Visual || !teammate.Visual.gameObject.activeSelf)
            { teammateIndicator.Root.gameObject.SetActive(false); return; }
            Vector3 projected = game.View.WorldToViewportPoint(teammate.Position + Vector3.up * 2f);
            Vector2 canvasSize = ((RectTransform)canvas.transform).rect.size;
            Vector2 center = ((RectTransform)canvas.transform).InverseTransformPoint(safeRoot.position);
            Vector2 point = new Vector2((projected.x - 0.5f) * canvasSize.x, (projected.y - 0.5f) * canvasSize.y) - center;
            Rect bounds = safeRoot.rect;
            bool offscreen = projected.z <= 0 || point.x < bounds.xMin || point.x > bounds.xMax || point.y < bounds.yMin || point.y > bounds.yMax;
            teammateIndicator.Root.gameObject.SetActive(offscreen);
            if (!offscreen) return;
            crewTags[other].Root.gameObject.SetActive(false);
            if (projected.z <= 0) point = -point;
            if (point.sqrMagnitude < 0.001f) point = Vector2.down;
            // Keep the full tag within the safe area and outside the top board/bottom touch controls.
            float halfWidth = Mathf.Max(1f, bounds.width * 0.5f - 112f);
            float halfHeight = Mathf.Max(1f, bounds.height * 0.5f - 270f);
            float factor = Mathf.Min(halfWidth / Mathf.Max(0.001f, Mathf.Abs(point.x)), halfHeight / Mathf.Max(0.001f, Mathf.Abs(point.y)));
            teammateIndicator.Root.anchoredPosition = point * factor;
            string direction = Mathf.Abs(point.x) > Mathf.Abs(point.y) ? (point.x > 0 ? "→" : "←") : (point.y > 0 ? "↑" : "↓");
            teammateIndicator.Bg.color = teammate.Color;
            teammateIndicator.Label.text = direction + " " + teammate.Name + (teammate.Bot ? " · BOT" : "");
        }

        void RefreshPracticeHeader()
        {
            AppState state=AppState.Instance;
            string room=state!=null && state.Room!=null ? state.Room.RoomName : string.Empty;
            if(string.IsNullOrEmpty(room))room=game.LaunchRoomName;
            sandboxRoomText.text=string.IsNullOrEmpty(room) ? (game.Remote ? "联机任务练习" : "任务练习") : "房间 · " + room;
            // HUD 计时器（M3.1）：练习模式计累计时长（无 300s 结算）；client 读镜像 MirrorElapsed，host 读 Sim.Elapsed。
            float elapsed=game.Remote ? (game.Shift==null ? 0f : game.Shift.MirrorElapsed) : (game.Sim==null ? 0f : game.Sim.Elapsed);
            sandboxPartnerText.text="练习 " + string.Format("{0:00}:{1:00}", (int)elapsed / 60, (int)elapsed % 60);
        }

        void PlaceStationCard(StationCard card, Vector3 world, float value, string title, Color color, bool visible)
        {
            card.Root.gameObject.SetActive(visible);
            if (!visible) return;
            PlaceAtWorld(card.Root, world + new Vector3(-0.8f, 2.4f, 0));
            card.Title.text = title;
            UiKit.SetBar(card.Fill, 124, 6, value, color);
        }

        void PlaceAtWorld(RectTransform target, Vector3 world)
        {
            // Screen Space - Camera：以相机视口换算到 canvas 中心锚点坐标，
            // 相机渲进 RenderTexture 时像素空间一致，截图证据与真机相同。
            Vector3 screen = game.View.WorldToScreenPoint(world);
            Vector2 viewport = game.View.ScreenToViewportPoint(screen);
            Vector2 size = ((RectTransform)canvas.transform).rect.size;
            target.anchoredPosition = new Vector2((viewport.x - 0.5f) * size.x, (viewport.y - 0.5f) * size.y);
        }

        void UpdateFps()
        {
            fpsAccum += Time.unscaledDeltaTime; fpsFrames++;
            if (Time.unscaledTime < fpsNext) return;
            fpsText.text = Mathf.RoundToInt(fpsFrames / Mathf.Max(0.0001f, fpsAccum)) + " FPS";
            fpsAccum = 0; fpsFrames = 0; fpsNext = Time.unscaledTime + 0.5f;
        }
    }
}
