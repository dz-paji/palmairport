using UnityEngine;
using UnityEngine.UI;

namespace IslandAirport
{
    public sealed class LobbyCanvas : MonoBehaviour
    {
        static readonly Color[] CrewColors =
        {
            AirportStyle.Hex("199FA7"), AirportStyle.Hex("E5585B"), AirportStyle.Hex("F0A51B"),
            AirportStyle.Hex("7D49C7"), AirportStyle.Player1, AirportStyle.Player2
        };

        LobbyApp app;
        RectTransform canvasRoot;
        Canvas canvas;
        Text guestLabel;
        Text nameLabel;
        Text seatLabel;
        Text partnerCaption;
        Text partnerHint;
        Text cooperationMemory, accountProgress;
        RectTransform accountProgressCard;
        Text startLabel;
        Text toastLabel;
        Text discoveryHint;
        Text roomStatus;
        Text roomSeats;
        Text startHint;
        Text signInMessage;
        Text authBadgeLabel;
        Text authStatusLabel;
        Text ageMessage;
        Text invitePreview;
        Text inviteResult;
        Text roomFixtureLabel;
        RectTransform toastRoot;
        RectTransform floatingNameplate;
        RectTransform localControlsRoot;
        RectTransform networkEntryRoot;
        RectTransform networkRoomRoot;
        RectTransform signInOverlay;
        RectTransform ageOverlay;
        RectTransform inviteOverlay;
        Text floatingNameLabel;
        InputField nameField;
        InputField ipField;
        Button startButton;
        Button createRoomButton;
        Button joinManualButton;
        Button authActionButton;
        Button signOutButton;
        Button[] inviteRoleButtons = new Button[3];
        Text authActionLabel;
        UiKit.DobPickerParts dobPicker;
        readonly UiKit.RoomRowParts[] roomRows = new UiKit.RoomRowParts[2];
        readonly Button[] swatches = new Button[CrewColors.Length];
        readonly GameObject[] swatchRims = new GameObject[CrewColors.Length];
        readonly GameObject[] swatchCenters = new GameObject[CrewColors.Length];

        public void Build(LobbyApp lobbyApp)
        {
            if (canvasRoot) return;
            app = lobbyApp;
            UiKit.EnsureEventSystem();

            var canvasObject = new GameObject("Cabin Lobby Canvas");
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = app.ViewCamera;
            canvas.planeDistance = 1.0f;
            canvas.sortingOrder = 20;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            canvasObject.AddComponent<GraphicRaycaster>();

            canvasRoot = UiKit.Node(canvasObject.transform, "Safe Area", Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            canvasRoot.offsetMin = canvasRoot.offsetMax = Vector2.zero;
            BuildProfileCard();
            BuildWardrobeCard();
            BuildSeatCount();
            BuildPartnerPanel();
            BuildFooter();
            BuildToast();
            BuildFloatingNameplate();
            BuildSignInOverlay();
            BuildAgeOverlay();
            BuildInvitationOverlay();
            RefreshNow();
        }

        void LateUpdate()
        {
            ApplySafeArea();
            if (!canvasRoot || !floatingNameplate || !app || !app.ViewCamera) return;
            Vector3 screen = app.ViewCamera.WorldToScreenPoint(CabinWorld.PlayerNamePosition + Vector3.up * 0.45f);
            bool visible = screen.z > 0;
            floatingNameplate.gameObject.SetActive(visible);
            if (!visible) return;
            Vector2 local;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRoot, screen, app.ViewCamera, out local))
                floatingNameplate.anchoredPosition = local;
        }

        void ApplySafeArea()
        {
            if (!canvasRoot || Screen.width <= 0 || Screen.height <= 0) return;
            Rect safe = Screen.safeArea;
            Vector2 min = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
            Vector2 max = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
            if (min == canvasRoot.anchorMin && max == canvasRoot.anchorMax) return;
            canvasRoot.anchorMin = min;
            canvasRoot.anchorMax = max;
            canvasRoot.offsetMin = canvasRoot.offsetMax = Vector2.zero;
        }

        void BuildProfileCard()
        {
            var card = UiKit.Panel(canvasRoot, "Guest identity card", AirportStyle.Ink, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(36, 30), new Vector2(405, 112));
            UiKit.Elevate(card, true);
            var root = (RectTransform)card.transform;
            var avatar = UiKit.Panel(root, "Avatar ring", AirportStyle.Hex("E4F4EF"), new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, 21), new Vector2(72, 72));
            var avatarText = UiKit.Label((RectTransform)avatar.transform, "Avatar", "P", 32, AirportStyle.Teal, FontStyle.Bold, TextAnchor.MiddleCenter);
            avatarText.fontStyle = FontStyle.Normal;
            UiKit.LabelAt(root, "Identity title", "玩家 ID", new Vector2(112, 15), new Vector2(235, 24), 15, AirportStyle.Hex("BAD0DA"), FontStyle.Bold);
            guestLabel = UiKit.LabelAt(root, "Guest ID", "Guest_000", new Vector2(112, 42), new Vector2(176, 40), 25, Color.white, FontStyle.Bold);
            nameLabel = UiKit.LabelAt(root, "Display name", "Guest", new Vector2(112, 76), new Vector2(210, 23), 16, AirportStyle.Hex("E2EEF0"));
            var badge = UiKit.Panel(root, "Identity badge", AirportStyle.Hex("4D6B7B"), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-14, 0), new Vector2(98, 34));
            authBadgeLabel = UiKit.Label((RectTransform)badge.transform, "Text", "游客", 15, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
            var badgeButton = badge.gameObject.AddComponent<Button>();
            badgeButton.targetGraphic = badge;
            UiKit.StyleSelectable(badgeButton);
            badgeButton.onClick.AddListener(() => app.OpenSignIn());
            var progressCard = UiKit.Panel(canvasRoot, "Account progress", AirportStyle.Paper, new Vector2(0,1),new Vector2(0,1),new Vector2(0,1),new Vector2(36,156),new Vector2(405,76));
            accountProgressCard = (RectTransform)progressCard.transform;
            accountProgress = UiKit.LabelAt(accountProgressCard,"Progress","",new Vector2(16,10),new Vector2(373,56),16,AirportStyle.Teal);
            accountProgress.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        void BuildWardrobeCard()
        {
            var card = UiKit.Panel(canvasRoot, "Wardrobe card", AirportStyle.Paper, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, 30), new Vector2(450, 176));
            UiKit.Elevate(card);
            var root = (RectTransform)card.transform;
            UiKit.LabelAt(root, "Wardrobe title", "衣柜", new Vector2(22, 14), new Vector2(180, 32), 25, AirportStyle.Ink, FontStyle.Bold);
            UiKit.LabelAt(root, "Wardrobe hint", "改名 · 换颜色", new Vector2(22, 48), new Vector2(200, 22), 15, AirportStyle.Muted);
            nameField = UiKit.TextField(root, "Display name", new Vector2(20, 80), new Vector2(278, 48), "输入玩家名字", fontSize: 18);
            nameField.onEndEdit.AddListener(SaveName);
            var save = UiKit.RoundButton(root, "Save name", "保存", AirportStyle.Teal, new Vector2(312, 81), new Vector2(116, 46), () => SaveName(nameField.text), 17);
            save.GetComponentInChildren<Text>().color = Color.white;
            for (int i = 0; i < swatches.Length; i++)
            {
                int colorIndex = i;
                swatches[i] = UiKit.Swatch(root, "Crew color " + i, CrewColors[i], new Vector2(24 + i * 66, 139), 34, () => app.SetColor(colorIndex));
                swatchRims[i] = swatches[i].transform.Find("Selection rim").gameObject;
                swatchCenters[i] = swatches[i].transform.Find("Colour centre").gameObject;
            }
        }

        void BuildSeatCount()
        {
            var card = UiKit.Panel(canvasRoot, "Seat count", AirportStyle.Ink, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-36, 30), new Vector2(284, 82));
            UiKit.Elevate(card, true);
            var root = (RectTransform)card.transform;
            UiKit.LabelAt(root, "Seat icons", "●     ○", new Vector2(22, 5), new Vector2(126, 68), 30, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
            seatLabel = UiKit.LabelAt(root, "Seat count label", "1 / 2", new Vector2(154, 10), new Vector2(105, 58), 29, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
        }

        void BuildPartnerPanel()
        {
            var card = UiKit.Panel(canvasRoot, "Partner panel", AirportStyle.Paper, new Vector2(1, 1), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-36, 137), new Vector2(420, 730));
            UiKit.Elevate(card);
            var root = (RectTransform)card.transform;
            UiKit.LabelAt(root, "Partner headline", "找个搭档，一起出发", new Vector2(24, 27), new Vector2(252, 48), 22, AirportStyle.Ink, FontStyle.Bold);
            var invite = UiKit.RoundButton(root, "Invite friends", "邀请好友", AirportStyle.Hex("E4F4EF"), new Vector2(282, 30), new Vector2(110, 42), () => app.OpenInvitationPanel(), 17);
            invite.GetComponentInChildren<Text>().color = AirportStyle.Teal;
            localControlsRoot = UiKit.Node(root, "Local partner controls", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -88), new Vector2(364, 225));
            var bot = UiKit.RoundButton(localControlsRoot, "Add bot", "添加 bot", AirportStyle.Teal, new Vector2(0, 10), new Vector2(364, 74), () => app.AddBot());
            bot.GetComponentInChildren<Text>().fontSize = 25;
            cooperationMemory = UiKit.LabelAt(localControlsRoot, "Bot memory", "你们一起送走了 0 架", new Vector2(0, 85), new Vector2(364, 27), 16, AirportStyle.Muted, FontStyle.Normal, TextAnchor.MiddleCenter);

            var local = UiKit.RoundButton(localControlsRoot, "Local coop", "同机双人", Color.white, new Vector2(0, 128), new Vector2(364, 60), () => app.SelectLocalCoop(), 23);
            local.GetComponent<Image>().color = AirportStyle.Hex("F0F8F6");
            local.GetComponentInChildren<Text>().color = AirportStyle.Teal;
            UiKit.LabelAt(localControlsRoot, "Local coop hint", "两位玩家共用一台设备", new Vector2(0, 190), new Vector2(364, 27), 16, AirportStyle.Muted, FontStyle.Normal, TextAnchor.MiddleCenter);

            UiKit.Panel(root, "Network separator", AirportStyle.Hex("DFE5E2"), new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, 326), new Vector2(364, 2), false);
            networkEntryRoot = UiKit.Node(root, "Network room discovery", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -338), new Vector2(364, 378));
            UiKit.LabelAt(networkEntryRoot, "Network title", "真人房间 · 需登录并完成年龄验证", new Vector2(0, 0), new Vector2(364, 30), 18, AirportStyle.Ink, FontStyle.Bold);
            var create = UiKit.RoundButton(networkEntryRoot, "Create room", "创建房间", AirportStyle.Teal, new Vector2(0, 38), new Vector2(364, 48), () => app.CreateRoom(), 20);
            createRoomButton = create;
            UiKit.LabelAt(networkEntryRoot, "Room list title", "附近房间", new Vector2(0, 94), new Vector2(364, 28), 18, AirportStyle.Ink, FontStyle.Bold);
            discoveryHint = UiKit.LabelAt(networkEntryRoot, "Discovery status", "同一 Wi-Fi 下还没有房间", new Vector2(0, 121), new Vector2(364, 24), 14, AirportStyle.Muted);
            for (int i = 0; i < roomRows.Length; i++)
            {
                int rowIndex = i;
                roomRows[i] = UiKit.RoomRow(networkEntryRoot, "Room row " + i, new Vector2(0, 145 + i * 72), new Vector2(364, 66), () => app.JoinDiscoveredRoom(rowIndex));
                roomRows[i].Root.gameObject.SetActive(false);
            }
            UiKit.LabelAt(networkEntryRoot, "Manual join title", "手动输入 IP", new Vector2(0, 289), new Vector2(364, 22), 15, AirportStyle.InkSoft, FontStyle.Bold);
            ipField = UiKit.TextField(networkEntryRoot, "Manual IP", new Vector2(0, 315), new Vector2(246, 48), "例如 192.168.1.12", fontSize: 17);
            var joinManual = UiKit.RoundButton(networkEntryRoot, "Join manual IP", "加入", AirportStyle.Ink, new Vector2(254, 315), new Vector2(110, 48), () => app.JoinAddress(ipField.text), 18);
            joinManualButton = joinManual;

            networkRoomRoot = UiKit.Node(root, "Network room details", new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -338), new Vector2(364, 378));
            roomStatus = UiKit.LabelAt(networkRoomRoot, "Room status", "等待房间状态", new Vector2(0, 0), new Vector2(364, 44), 19, AirportStyle.Ink, FontStyle.Bold);
            roomFixtureLabel = UiKit.LabelAt(networkRoomRoot, "Room fixture", "", new Vector2(0, 42), new Vector2(364, 24), 13, AirportStyle.Hex("B15C25"), FontStyle.Bold);
            roomSeats = UiKit.LabelAt(networkRoomRoot, "Room seats", "座位 1\n座位 2", new Vector2(0, 70), new Vector2(364, 106), 18, AirportStyle.InkSoft, FontStyle.Normal);
            partnerCaption = UiKit.LabelAt(networkRoomRoot, "Partner status", "房间已创建", new Vector2(0, 190), new Vector2(364, 28), 16, AirportStyle.Teal, FontStyle.Bold);
            partnerHint = UiKit.LabelAt(networkRoomRoot, "Partner status hint", "等待搭档加入", new Vector2(0, 220), new Vector2(364, 44), 14, AirportStyle.Muted);
            UiKit.RoundButton(networkRoomRoot, "Leave room", "离开房间", AirportStyle.Hex("E9ECE9"), new Vector2(0, 290), new Vector2(364, 48), () => app.LeaveRoom(), 18).GetComponentInChildren<Text>().color = AirportStyle.Ink;
        }

        void BuildFooter()
        {
            var footer = UiKit.Panel(canvasRoot, "Footer", AirportStyle.Hex("F7F5EE"), new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, -20), new Vector2(0, 142), false);
            UiKit.Elevate(footer);
            var root = (RectTransform)footer.transform;
            var level = UiKit.Panel(root, "Level placeholder", Color.white, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(38, 0), new Vector2(590, 98));
            var levelRoot = (RectTransform)level.transform;
            UiKit.Panel(levelRoot, "Island emblem", AirportStyle.Hex("E8F3EE"), new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, 16), new Vector2(108, 66), false);
            UiKit.LabelAt(levelRoot, "Level icon", "ISLAND", new Vector2(18, 16), new Vector2(112, 66), 15, AirportStyle.Teal, FontStyle.Bold, TextAnchor.MiddleCenter);
            UiKit.LabelAt((RectTransform)level.transform, "Level title", "珊瑚湾 · 午后班岗", new Vector2(150, 16), new Vector2(380, 30), 22, AirportStyle.Ink, FontStyle.Bold);
            UiKit.LabelAt((RectTransform)level.transform, "Level meta", "选择关卡   ·   5 架航班 / 3 个机位", new Vector2(150, 52), new Vector2(380, 24), 15, AirportStyle.Muted);
            var start = UiKit.RoundButton(root, "Start shift", "开始执勤", AirportStyle.Hex("9AAAB0"), Vector2.zero, new Vector2(474, 88), () => app.BeginShift(), 28);
            var startRect = (RectTransform)start.transform;
            startRect.anchorMin = startRect.anchorMax = new Vector2(1, 0);
            startRect.pivot = new Vector2(1, 0);
            startRect.anchoredPosition = new Vector2(-38, 18);
            startButton = start;
            startLabel = start.GetComponentInChildren<Text>();
            startHint = UiKit.LabelAt(root, "Start hint", "添加 bot 或选择同机双人后开始", new Vector2(0, 108), new Vector2(440, 22), 15, AirportStyle.Muted, FontStyle.Normal, TextAnchor.MiddleCenter);
            var hintRect = (RectTransform)root.Find("Start hint");
            hintRect.anchorMin = hintRect.anchorMax = new Vector2(1, 0);
            hintRect.pivot = new Vector2(1, 0);
            hintRect.anchoredPosition = new Vector2(-58, 108);
        }

        void BuildSignInOverlay()
        {
            signInOverlay = UiKit.Node(canvasRoot, "Sign in overlay", Vector2.zero, Vector2.one, new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
            var shade = signInOverlay.gameObject.AddComponent<Image>();
            shade.color = new Color(0, 0, 0, .55f);
            var panel = UiKit.Panel(signInOverlay, "Sign in panel", AirportStyle.Paper, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(760, 470));
            RectTransform root = (RectTransform)panel.transform;
            UiKit.Elevate(panel);
            UiKit.LabelAt(root, "Sign in title", "真人组队需要登录", new Vector2(40, 28), new Vector2(680, 52), 30, AirportStyle.Ink, FontStyle.Bold, TextAnchor.MiddleCenter);
            authStatusLabel = UiKit.LabelAt(root, "Authentication status", "游客模式", new Vector2(52, 91), new Vector2(656, 48), 18, AirportStyle.Teal, FontStyle.Bold, TextAnchor.MiddleCenter);
            signInMessage = UiKit.LabelAt(root, "Sign in message", "真人房间需要登录并年满 13 岁。", new Vector2(62, 148), new Vector2(636, 100), 18, AirportStyle.InkSoft, FontStyle.Normal, TextAnchor.MiddleCenter);
            var action = UiKit.RoundButton(root, "Sign in action", "使用 Google 登录", AirportStyle.Teal, new Vector2(48, 280), new Vector2(318, 62), () => app.HandleSignInAction(), 20);
            authActionButton = action;
            authActionLabel = action.GetComponentInChildren<Text>();
            var logout = UiKit.RoundButton(root, "Sign out", "退出登录", AirportStyle.Hex("E8ECE8"), new Vector2(394, 280), new Vector2(318, 62), () => app.SignOut(), 20);
            logout.GetComponentInChildren<Text>().color = AirportStyle.Ink;
            signOutButton = logout;
            UiKit.RoundButton(root, "Sign in back", "返回大厅", AirportStyle.Ink, new Vector2(210, 370), new Vector2(340, 56), () => app.CloseSignIn(), 20);
            signInOverlay.gameObject.SetActive(false);
        }

        void BuildAgeOverlay()
        {
            ageOverlay = UiKit.Node(canvasRoot, "Age verification overlay", Vector2.zero, Vector2.one, new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
            var shade = ageOverlay.gameObject.AddComponent<Image>();
            shade.color = new Color(0, 0, 0, .58f);
            var panel = UiKit.Panel(ageOverlay, "Age verification panel", AirportStyle.Paper, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(780, 470));
            RectTransform root = (RectTransform)panel.transform;
            UiKit.Elevate(panel);
            UiKit.LabelAt(root, "Age title", "确认已满 13 岁", new Vector2(42, 28), new Vector2(696, 52), 30, AirportStyle.Ink, FontStyle.Bold, TextAnchor.MiddleCenter);
            UiKit.LabelAt(root, "Age explanation", "输入出生日期以完成真人组队准入。系统只保存是否通过验证，不保存出生日期。", new Vector2(68, 91), new Vector2(644, 58), 17, AirportStyle.InkSoft, FontStyle.Normal, TextAnchor.MiddleCenter);
            dobPicker = UiKit.DobPicker(root, "DOB picker", new Vector2(70, 176), new Vector2(640, 58), System.DateTime.Today.Year - 120, System.DateTime.Today.Year);
            ageMessage = UiKit.LabelAt(root, "Age message", "请选择出生年份、月份和日期。", new Vector2(70, 250), new Vector2(640, 54), 16, AirportStyle.Muted, FontStyle.Normal, TextAnchor.MiddleCenter);
            UiKit.RoundButton(root, "Verify age", "确认并继续", AirportStyle.Teal, new Vector2(68, 350), new Vector2(310, 58), VerifySelectedAge, 20);
            UiKit.RoundButton(root, "Cancel age check", "返回大厅", AirportStyle.Ink, new Vector2(402, 350), new Vector2(310, 58), () => app.CloseAgeCheck(), 20);
            ageOverlay.gameObject.SetActive(false);
        }

        void BuildInvitationOverlay()
        {
            inviteOverlay = UiKit.Node(canvasRoot, "Invitation overlay", Vector2.zero, Vector2.one, new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
            var shade = inviteOverlay.gameObject.AddComponent<Image>();
            shade.color = new Color(0, 0, 0, .58f);
            var panel = UiKit.Panel(inviteOverlay, "Invitation panel", AirportStyle.Paper, new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(820, 560));
            RectTransform root = (RectTransform)panel.transform;
            UiKit.Elevate(panel);
            UiKit.LabelAt(root, "Invitation title", "邀请好友来搭档", new Vector2(42, 28), new Vector2(736, 50), 30, AirportStyle.Ink, FontStyle.Bold, TextAnchor.MiddleCenter);
            UiKit.LabelAt(root, "Invitation note", "选择岗位并分享组队说明。请先向邀请人获取安装包，再连接同一个 Wi-Fi。", new Vector2(52, 88), new Vector2(716, 50), 17, AirportStyle.InkSoft, FontStyle.Normal, TextAnchor.MiddleCenter);
            inviteRoleButtons[0] = UiKit.Chip(root, "Meals role", "餐食车", new Vector2(52, 154), new Vector2(220, 52), () => app.SelectInviteRole(InviteRole.Meals));
            inviteRoleButtons[1] = UiKit.Chip(root, "Baggage role", "行李车", new Vector2(300, 154), new Vector2(220, 52), () => app.SelectInviteRole(InviteRole.Baggage));
            inviteRoleButtons[2] = UiKit.Chip(root, "Fuel role", "燃油车", new Vector2(548, 154), new Vector2(220, 52), () => app.SelectInviteRole(InviteRole.Fuel));
            var previewPanel = UiKit.Panel(root, "Invitation preview panel", Color.white, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(52, 230), new Vector2(716, 136));
            invitePreview = UiKit.Label((RectTransform)previewPanel.transform, "Invitation preview", "", 17, AirportStyle.InkSoft, FontStyle.Normal, TextAnchor.MiddleLeft);
            invitePreview.horizontalOverflow = HorizontalWrapMode.Wrap;
            invitePreview.verticalOverflow = VerticalWrapMode.Truncate;
            RectTransform previewRect = (RectTransform)invitePreview.transform;
            previewRect.offsetMin = new Vector2(22, 16);
            previewRect.offsetMax = new Vector2(-22, -16);
            inviteResult = UiKit.LabelAt(root, "Invitation result", "", new Vector2(52, 380), new Vector2(716, 34), 15, AirportStyle.Teal, FontStyle.Bold, TextAnchor.MiddleCenter);
            UiKit.RoundButton(root, "Share invitation", "分享邀请", AirportStyle.Teal, new Vector2(52, 430), new Vector2(340, 60), () => app.ShareInvite(), 20);
            UiKit.RoundButton(root, "Close invitation", "返回大厅", AirportStyle.Ink, new Vector2(428, 430), new Vector2(340, 60), () => app.CloseInvitationPanel(), 20);
            inviteOverlay.gameObject.SetActive(false);
        }

        void BuildToast()
        {
            toastRoot = UiKit.Node(canvasRoot, "Toast", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 180), new Vector2(660, 52));
            var background = toastRoot.gameObject.AddComponent<Image>();
            background.sprite = AirportStyle.RoundedSprite;
            background.type = Image.Type.Sliced;
            background.color = AirportStyle.Ink;
            background.raycastTarget = false;
            toastLabel = UiKit.Label(toastRoot, "Text", "", 18, Color.white, FontStyle.Normal, TextAnchor.MiddleCenter);
            toastRoot.gameObject.SetActive(false);
        }

        void BuildFloatingNameplate()
        {
            floatingNameplate = UiKit.Node(canvasRoot, "Floating player nameplate", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(244, 48));
            var background = floatingNameplate.gameObject.AddComponent<Image>();
            background.sprite = AirportStyle.RoundedSprite;
            background.type = Image.Type.Sliced;
            background.color = AirportStyle.Ink;
            background.raycastTarget = false;
            floatingNameLabel = UiKit.Label(floatingNameplate, "Text", "Guest", 19, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
            floatingNameplate.SetAsLastSibling();
        }

        public void RefreshNow()
        {
            if (!app || !canvasRoot) return;
            ApplySafeArea();
            PlayerProfile profile = AppState.Ensure().Profile;
            guestLabel.text = string.IsNullOrEmpty(profile.GuestId) ? "Guest" : profile.GuestId;
            nameLabel.text = profile.Name ?? "Guest";
            floatingNameLabel.text = profile.Name ?? "Guest";
            authBadgeLabel.text = app.AuthBadge;
            authBadgeLabel.transform.parent.GetComponent<Image>().color = app.IsFakeAuth ? AirportStyle.Hex("B76E38") : app.HasAuthUser ? AirportStyle.Teal : AirportStyle.Hex("4D6B7B");
            if (!nameField.isFocused) nameField.SetTextWithoutNotify(profile.Name ?? string.Empty);
            seatLabel.text = app.InNetworkRoom ? app.RoomTaken + " / 2" : app.HasPartner ? "2 / 2" : "1 / 2";
            bool networkRoom = app.InNetworkRoom;
            localControlsRoot.gameObject.SetActive(!networkRoom);
            networkEntryRoot.gameObject.SetActive(!networkRoom);
            networkRoomRoot.gameObject.SetActive(networkRoom);
            createRoomButton.interactable = !networkRoom && !app.Connecting;
            joinManualButton.interactable = !networkRoom && !app.Connecting;
            startButton.interactable = app.CanBeginShift;
            startButton.GetComponent<Image>().color = app.CanBeginShift ? AirportStyle.Teal : AirportStyle.Hex("DCE4DE");
            startLabel.color = app.CanBeginShift ? Color.white : AirportStyle.InkSoft;
            startLabel.text = app.StartButtonText;
            startHint.text = app.StartHint;
            var progressState = AppState.Ensure();
            accountProgressCard.gameObject.SetActive(app.HasAuthUser && !app.IsFakeAuth);
            accountProgress.text = progressState.AccountProgressNotice;
            if (progressState.AccountProgress != null && progressState.AccountProgress.Status == "synced")
            {
                var progress = progressState.AccountProgress.CurrentProgress;
                int best = 0; progress.LevelBestStars.TryGetValue("coral-bay-1", out best);
                accountProgress.text = "账号已完成 " + progress.CompletedMatches + " 班 · 送走 " + progress.TotalCompletedFlights + " 架\n珊瑚湾最佳：" + best + " 星";
            }
            if (cooperationMemory != null) { var memory = AppState.Ensure().BotMemory; cooperationMemory.text = "你们一起送走了 " + (memory == null ? 0 : memory.DepartedFlights) + " 架"; }
            partnerCaption.text = networkRoom ? app.NetworkHost ? "你是房主" : "已加入房间" : app.PartnerLabel;
            partnerHint.text = networkRoom ? app.NetworkHost ? "两位真人都就位后可以开始 300 秒班次" : "房主开始后会一起进入 300 秒班次" : app.PartnerHint;
            for (int i = 0; i < swatches.Length; i++)
            {
                bool selected = i == profile.ColorIndex;
                var swatch = (RectTransform)swatches[i].transform;
                swatch.localScale = selected ? new Vector3(1.18f, 1.18f, 1) : Vector3.one;
                swatchRims[i].SetActive(selected);
                swatchCenters[i].SetActive(selected);
            }
            RefreshNetworkRoom();
            RefreshRoomList();
            signInMessage.text = app.SignInMessage ?? string.Empty;
            authStatusLabel.text = app.AuthStatus;
            authActionLabel.text = app.SignInActionLabel;
            signOutButton.gameObject.SetActive(app.HasAuthUser);
            authActionButton.interactable = true;
            ageMessage.text = app.AgeMessage ?? "请选择出生年份、月份和日期。";
            invitePreview.text = app.InviteMessage;
            inviteResult.text = app.ShareResultMessage ?? string.Empty;
            for (int i = 0; i < inviteRoleButtons.Length; i++)
            {
                InviteRole role = (InviteRole)i;
                bool selected = app.SelectedInviteRole == role;
                Image image = inviteRoleButtons[i].GetComponent<Image>();
                image.color = selected ? AirportStyle.Hex("BFE5DB") : AirportStyle.Paper;
                inviteRoleButtons[i].GetComponentInChildren<Text>().color = selected ? AirportStyle.Teal : AirportStyle.Ink;
            }
            signInOverlay.gameObject.SetActive(app.View == LobbyApp.ViewState.SignIn);
            ageOverlay.gameObject.SetActive(app.View == LobbyApp.ViewState.AgeCheck);
            inviteOverlay.gameObject.SetActive(app.View == LobbyApp.ViewState.Invitation);
            if (app.View == LobbyApp.ViewState.SignIn) signInOverlay.SetAsLastSibling();
            else if (app.View == LobbyApp.ViewState.AgeCheck) ageOverlay.SetAsLastSibling();
            else if (app.View == LobbyApp.ViewState.Invitation) inviteOverlay.SetAsLastSibling();
        }

        void RefreshNetworkRoom()
        {
            if (!app.InNetworkRoom) return;
            AppState state = AppState.Ensure();
            RoomManager room = state.Room;
            roomStatus.text = app.Connecting ? "正在连接房间…" : string.IsNullOrEmpty(app.RoomDisplayName) ? "好友的房间" : app.RoomDisplayName;
            roomFixtureLabel.text = app.FixtureNotice;
            roomFixtureLabel.gameObject.SetActive(app.InRoomFixture);
            string first = app.InRoomFixture ? AppState.Ensure().Profile.Name : room.Seats.Length > 0 && room.Seats[0].Occupied ? room.Seats[0].Name : "等待房主";
            string second = app.InRoomFixture ? app.FixturePeerName : room.Seats.Length > 1 && room.Seats[1].Occupied ? room.Seats[1].Name : "等待搭档加入";
            if (!app.InRoomFixture && room.Seats.Length > 1 && room.Seats[1].Bot) second += " · BOT";
            roomSeats.text = "座位 1   " + first + "\n座位 2   " + second;
            if (app.Connecting)
            {
                partnerCaption.text = "正在连接";
                partnerHint.text = "连接超时或验证失败时会显示原因。";
            }
            else if (app.NetworkHost)
            {
                partnerCaption.text = app.InRoomFixture || room.Seats.Length > 1 && room.Seats[1].Occupied ? "搭档已加入" : "等待搭档";
                partnerHint.text = app.InRoomFixture ? "截图示例席位；开始按钮仅用于版式预览。" : "开始执勤需要两位真人都在房间内。";
            }
            else
            {
                partnerCaption.text = "等待房主开始";
                partnerHint.text = "房主开始后会同步进入 300 秒班次。";
            }
        }

        void RefreshRoomList()
        {
            if (app.InNetworkRoom) return;
            var rooms = app.Rooms;
            discoveryHint.text = app.HasRoomFixture ? app.FixtureNotice : !app.DiscoveryAvailable
                ? "自动发现不可用 · 可手动输入 IP"
                : rooms.Count == 0 ? "同一 Wi-Fi 下还没有房间" : string.Empty;
            for (int i = 0; i < roomRows.Length; i++)
            {
                UiKit.RoomRowParts row = roomRows[i];
                bool active = i < rooms.Count;
                row.Root.gameObject.SetActive(active);
                if (!active) continue;
                RoomInfo room = rooms[i];
                row.Title.text = string.IsNullOrEmpty(room.Name) ? "好友的房间" : room.Name;
                row.Meta.text = room.HostName + "   ·   " + room.Taken + " / " + room.Seats;
                bool playing = room.State == "playing";
                bool full = room.Taken >= room.Seats;
                row.Join.interactable = !playing && !full;
                row.JoinLabel.text = playing ? "已开局" : full ? "已满" : "加入";
                row.Join.GetComponent<Image>().color = row.Join.interactable ? AirportStyle.Teal : AirportStyle.Hex("AEB9B6");
            }
        }

        public void ShowToast(string message)
        {
            if (!toastRoot) return;
            toastLabel.text = message ?? string.Empty;
            toastRoot.gameObject.SetActive(!string.IsNullOrEmpty(message));
        }

        public void SetVisible(bool visible)
        {
            if (canvas) canvas.enabled = visible;
        }

        void SaveName(string value)
        {
            if (app) app.SaveDisplayName(value);
        }

        void VerifySelectedAge()
        {
            app.VerifyAge(ReadDropdownValue(dobPicker.Year), ReadDropdownValue(dobPicker.Month), ReadDropdownValue(dobPicker.Day));
        }

        static int ReadDropdownValue(Dropdown dropdown)
        {
            if (!dropdown || dropdown.options == null || dropdown.value < 0 || dropdown.value >= dropdown.options.Count) return 0;
            string label = dropdown.options[dropdown.value].text;
            int value = 0;
            for (int i = 0; i < label.Length; i++)
            {
                char character = label[i];
                if (character >= '0' && character <= '9') value = value * 10 + character - '0';
            }
            return value;
        }
    }
}
