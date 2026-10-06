using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IslandAirport
{
    public sealed class LobbyApp : MonoBehaviour
    {
        public enum PartnerChoice { None, Bot, LocalCoop }
        public enum ViewState { Main, SignIn, AgeCheck, Invitation, InRoom }
        enum PendingRoomAction { None, Create, Join }

        public static LobbyApp Instance { get; private set; }
        public PartnerChoice Choice { get; private set; }
        public ViewState View { get; private set; }
        public string SignInMessage { get; private set; }
        public string AgeMessage { get; private set; }
        public string ShareResultMessage { get; private set; }
        public InviteRole SelectedInviteRole { get; private set; }
        public string InviteMessage { get { return ShareService.BuildInviteMessage(SelectedInviteRole, InviteSourceId); } }
        public string InviteSourceId
        {
            get
            {
                AuthUser user = state == null || state.Auth == null ? null : state.Auth.User;
                return user != null && !string.IsNullOrEmpty(user.Uid) ? user.Uid : state == null ? string.Empty : state.Profile.GuestId;
            }
        }
        public bool AuthBusy { get { return state != null && state.Auth != null && state.Auth.Busy; } }
        public bool HasAuthUser { get { return state != null && state.Auth != null && state.Auth.User != null; } }
        public bool IsFakeAuth { get { return state != null && state.IsFakeAuth; } }
        public string AuthBadge
        {
            get
            {
                if (IsFakeAuth) return "Fake 测试";
                return HasAuthUser ? "Google" : "游客";
            }
        }
        public string AuthStatus
        {
            get
            {
                if (AuthBusy) return IsFakeAuth ? "Fake 测试登录中 · 不会连接 Firebase" : "正在打开 Google 登录";
                if (HasAuthUser)
                {
                    return IsFakeAuth ? "Fake 测试账号已登录 · 不会连接 Firebase" : "Google 账号已登录";
                }
                if (!string.IsNullOrEmpty(state == null ? string.Empty : state.AuthNotice)) return state.AuthNotice;
                return "游客模式 · 本地单人和 bot 无需登录";
            }
        }
        public string SignInActionLabel
        {
            get
            {
                if (AuthBusy) return "取消登录";
                if (HasAuthUser) return "重新登录";
                return IsFakeAuth ? "开始 Fake 测试登录" : "使用 Google 登录";
            }
        }
        public bool HasRoomFixture { get { return debugRooms != null; } }
        public bool InRoomFixture { get { return debugRoomFixture; } }
        public bool HasPendingRoomAction { get { return pendingRoomAction != PendingRoomAction.None; } }
        public string FixtureNotice { get { return debugRoomFixture ? "截图示例 · 非真实联机" : HasRoomFixture ? "截图示例 · 非实时局域网房间" : string.Empty; } }
        public bool HasPartner { get { return Choice != PartnerChoice.None; } }
        public string PartnerLabel { get { return Choice == PartnerChoice.Bot ? "BOT 已在右侧舱位" : Choice == PartnerChoice.LocalCoop ? "本地双人已就位" : "等待选择搭档"; } }
        public string PartnerHint { get { return Choice == PartnerChoice.Bot ? "一个人也能玩，无需登录" : Choice == PartnerChoice.LocalCoop ? "两位玩家共用一台设备" : "添加 bot 或选择同机双人后开始"; } }
        public bool NetworkRoomActive { get { return debugRoomFixture || state != null && state.Room != null && (state.Room.Phase == RoomPhase.Listening || state.Room.Phase == RoomPhase.InRoom); } }
        public bool Connecting { get { return state != null && state.Room != null && state.Room.Phase == RoomPhase.Joining; } }
        public bool NetworkHost { get { return NetworkRoomActive && (debugRoomFixture ? debugRoomFixtureHost : state.Room.IsHost); } }
        public bool InNetworkRoom { get { return View == ViewState.InRoom || Connecting; } }
        public bool DiscoveryAvailable { get { return state != null && state.Listener != null; } }
        public int RoomTaken { get { return debugRoomFixture ? 2 : state != null && state.Room != null ? state.Room.TakenCount : 0; } }
        public List<RoomInfo> Rooms { get { return debugRooms != null ? new List<RoomInfo>(debugRooms) : state != null && state.Listener != null ? state.Listener.Rooms() : new List<RoomInfo>(); } }
        public string StartButtonText { get { return InNetworkRoom ? NetworkHost ? "开始执勤" : "等待房主开始" : "开始执勤"; } }
        public string StartHint
        {
            get
            {
                if (Connecting) return "正在连接房间…";
                if (NetworkRoomActive) return NetworkHost ? NetworkRoomReady ? "两位真人已就位，开始 300 秒班次" : "等待第二位真人加入后开始" : "房主开始后将进入 300 秒班次";
                return HasPartner ? "搭档已就位，可以开始" : "添加 bot 或选择同机双人后开始";
            }
        }
        public bool CanBeginShift
        {
            get
            {
                if (NetworkRoomActive) return NetworkRoomReady && (debugRoomFixture || HasVerifiedRoomIdentity);
                return !Connecting && HasPartner;
            }
        }

        AppState state;
        LobbyCanvas view;
        Transform cabinRoot;
        Camera cabinCamera;
        Light cabinLight;
        float roomRefreshElapsed;
        string joinedRoomName = string.Empty;
        string debugRoomFixtureName = string.Empty;
        string debugRoomFixturePeer = string.Empty;
        bool debugRoomFixtureHost = true;
        List<RoomInfo> debugRooms;
        bool debugRoomFixture;
        PendingRoomAction pendingRoomAction;
        string pendingJoinAddress = string.Empty;
        string pendingJoinRoomName = string.Empty;
        string activeJoinAddress = string.Empty;
        string activeJoinRoomName = string.Empty;
        public Camera ViewCamera { get { return cabinCamera; } }
        public string PendingJoinAddress { get { return pendingRoomAction == PendingRoomAction.Join ? pendingJoinAddress : string.Empty; } }
        public string JoinTargetAddress { get { return pendingRoomAction == PendingRoomAction.Join ? pendingJoinAddress : activeJoinAddress; } }
        public string RoomDisplayName { get { return debugRoomFixture ? debugRoomFixtureName : state != null && state.Room != null && !string.IsNullOrEmpty(state.Room.RoomName) ? state.Room.RoomName : joinedRoomName; } }

        bool NetworkRoomReady
        {
            get
            {
                if (debugRoomFixture) return debugRoomFixtureHost;
                return NetworkHost && state.Room.Seats.Length > 1 && state.Room.Seats[1].Occupied && !state.Room.Seats[1].Bot &&
                    (state.Room.Phase == RoomPhase.Listening || state.Room.Phase == RoomPhase.InRoom);
            }
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            state = AppState.Ensure();
            view = GetComponent<LobbyCanvas>();
            EnsureCabinContent();
            EnsureEventSystem();
        }

        void Start()
        {
            if (!view) view = gameObject.AddComponent<LobbyCanvas>();
            view.Build(this);
            SetCabinVisible(true);
            string toast = NetSession.TakeLobbyToast();
            if (!string.IsNullOrEmpty(toast)) ShowToast(toast);
            RefreshView();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            DrainAuthEvents();
            UpdateBeacon();
            roomRefreshElapsed += dt;
            bool refresh = roomRefreshElapsed >= 1f;
            if (refresh) roomRefreshElapsed %= 1f;
            string evt;
            while (state.Room != null && state.Room.TryDequeue(out evt))
            {
                if (HandleRoomEvent(evt)) return;
                refresh = true;
            }
            if (refresh) RefreshView();
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
        }

        void EnsureCabinContent()
        {
            cabinRoot = transform.Find("Cabin World");
            if (!cabinRoot) cabinRoot = CabinWorld.Build(transform);
            cabinLight = CabinWorld.SetupLighting(transform);
            cabinCamera = GetComponentInChildren<Camera>(true);
            if (!cabinCamera)
            {
                var cameraObject = new GameObject("Cabin Lobby Camera");
                cameraObject.transform.SetParent(transform, false);
                cabinCamera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }
            cabinCamera.tag = "MainCamera";
            CabinWorld.ConfigureCamera(cabinCamera);
            CabinWorld.SetPlayerColor(cabinRoot, state.Profile.ColorIndex);
            CabinWorld.SetPartnerAppearance(cabinRoot, false, false);
        }

        void SetCabinVisible(bool visible)
        {
            if (cabinRoot) cabinRoot.gameObject.SetActive(visible);
            if (cabinCamera) cabinCamera.gameObject.SetActive(visible);
            if (cabinLight) cabinLight.enabled = visible;
            if (view) view.SetVisible(visible);
        }

        void EnsureEventSystem()
        {
            var eventSystem = GetComponentInChildren<UnityEngine.EventSystems.EventSystem>(true);
            if (eventSystem) return;
            var eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.transform.SetParent(transform, false);
            eventSystemObject.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystemObject.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        void UpdateBeacon()
        {
            if (state == null || state.Room == null || state.Beacon == null) return;
            RoomManager room = state.Room;
            bool hostRoom = room.IsHost && (room.Phase == RoomPhase.Listening || room.Phase == RoomPhase.InRoom || room.Phase == RoomPhase.Playing);
            if (!hostRoom)
            {
                if (state.Beacon.Announcing) state.Beacon.Stop();
                return;
            }
            if (!state.Beacon.Announcing)
            {
                state.Beacon.Start(new RoomInfo());
            }
            RoomInfo beacon = state.Beacon.Room;
            beacon.Name = room.RoomName;
            int hostSeat = room.LocalSeat >= 0 && room.LocalSeat < room.Seats.Length ? room.LocalSeat : 0;
            beacon.HostName = room.Seats.Length > 0 ? room.Seats[hostSeat].Name : state.Profile.Name;
            beacon.Address = string.Empty;
            beacon.State = room.BeaconState;
            beacon.Port = NetProtocol.SessionPort;
            beacon.Seats = NetProtocol.SeatCount;
            beacon.Taken = room.TakenCount;
        }

        bool HandleRoomEvent(string evt)
        {
            if (evt == "start")
            {
                if (state.Room.IsHost) return false;
                state.Launch.Mode = state.Room.IsPractice ? AppState.GameMode.ClientSandbox : AppState.GameMode.ClientShift;
                state.Launch.RoomName = RoomDisplayName;
                state.Launch.JoinAddress = string.Empty;
                SceneManager.LoadScene(AppState.ScenePalmBay);
                return true;
            }
            if (evt.StartsWith("joined:", StringComparison.Ordinal))
            {
                View = ViewState.InRoom;
                Choice = PartnerChoice.None;
                activeJoinAddress = string.Empty;
                activeJoinRoomName = string.Empty;
                state.Events.Enqueue(GameEventQueue.RoomJoined);
                ApplyPartnerAppearance();
                ShowToast(state.Room.IsHost ? "搭档已加入房间。" : "已加入房间，等待房主开始。");
            }
            else if (evt.StartsWith("reject:", StringComparison.Ordinal))
            {
                string reason = evt.Substring("reject:".Length);
                Dictionary<string, object> parameters = new Dictionary<string, object>();
                parameters["reason"] = reason;
                state.Events.Enqueue(GameEventQueue.RoomRejected, null, parameters);
                if (reason == "auth")
                {
                    pendingRoomAction = PendingRoomAction.Join;
                    pendingJoinAddress = activeJoinAddress;
                    pendingJoinRoomName = activeJoinRoomName;
                    activeJoinAddress = string.Empty;
                    activeJoinRoomName = string.Empty;
                    ShowSignIn("登录校验失败，请重试或退出当前账号。" + AuthNoticeSuffix());
                }
                else
                {
                    activeJoinAddress = string.Empty;
                    activeJoinRoomName = string.Empty;
                    View = ViewState.Main;
                    ShowToast(RejectMessage(reason));
                }
            }
            else if (evt == "closed")
            {
                View = ViewState.Main;
                if (state.Beacon != null) state.Beacon.Stop();
                ShowToast("房间已关闭。");
            }
            else if (evt == "hostlost")
            {
                View = ViewState.Main;
                ShowToast("房主已离开，连接已断开。");
            }
            else if (evt.StartsWith("left:", StringComparison.Ordinal))
            {
                ShowToast("搭档已离开房间。");
            }
            if (evt == "roster" || evt.StartsWith("joined:", StringComparison.Ordinal)) ApplyPartnerAppearance();
            return false;
        }

        static string RejectMessage(string reason)
        {
            switch (reason)
            {
                case "full": return "房间已满，无法加入。";
                case "ingame": return "这班已经起飞，无法加入。";
                case "auth": return "登录校验失败，请重新登录后加入。";
                case "timeout": return "连接超时，请检查 IP 和 Wi-Fi。";
                default: return "加入失败，请检查网络后重试。";
            }
        }

        void ApplyPartnerAppearance()
        {
            if (debugRoomFixture)
            {
                CabinWorld.SetPartnerAppearance(cabinRoot, true, false);
            }
            else if (NetworkRoomActive)
            {
                bool occupied = state.Room.TakenCount > 1;
                CabinWorld.SetPartnerAppearance(cabinRoot, occupied, false);
            }
            else
            {
                CabinWorld.SetPartnerAppearance(cabinRoot, HasPartner, Choice == PartnerChoice.Bot);
            }
        }

        public void AddBot()
        {
            if (NetworkRoomActive || Connecting) return;
            Choice = PartnerChoice.Bot;
            CabinWorld.SetPartnerAppearance(cabinRoot, true, true);
            RefreshView();
            ShowToast("BOT 搭档已加入右侧舱位。");
        }

        public void SelectLocalCoop()
        {
            if (NetworkRoomActive || Connecting) return;
            Choice = PartnerChoice.LocalCoop;
            CabinWorld.SetPartnerAppearance(cabinRoot, true, false);
            RefreshView();
            ShowToast("本地双人已就位，开始后可使用两套键位。");
        }

        public void BeginShift() { BeginNetworkOrLocalShift(false); }

        /// <summary>Explicit unlimited practice entry retained for M3 acceptance fixtures.</summary>
        public void BeginPracticeShift() { BeginNetworkOrLocalShift(true); }

        void BeginNetworkOrLocalShift(bool practice)
        {
            if (NetworkRoomActive)
            {
                if (debugRoomFixture)
                {
                    ShowToast("截图示例仅用于展示，不会启动真实联机。 ");
                    return;
                }
                if (!NetworkHost)
                {
                    ShowToast("等待房主开始。");
                    return;
                }
                if (!HasVerifiedRoomIdentity)
                {
                    LeaveRoom();
                    RequireVerifiedUser();
                    return;
                }
                if (!NetworkRoomReady)
                {
                    ShowToast("两位真人都加入后才能开始。");
                    return;
                }
                if (!(practice ? state.Room.StartSandbox() : state.Room.StartShift()))
                {
                    ShowToast("暂时无法开始，请稍后重试。");
                    return;
                }
                state.Events.Enqueue(GameEventQueue.RoomStarted);
                state.Launch.Mode = practice ? AppState.GameMode.HostSandbox : AppState.GameMode.HostShift;
                state.Launch.RoomName = state.Room.RoomName;
                state.Launch.JoinAddress = string.Empty;
                SceneManager.LoadScene(AppState.ScenePalmBay);
                return;
            }
            BeginLocalShift();
        }

        public void BeginLocalShift()
        {
            if (!HasPartner)
            {
                ShowToast("先添加 bot 或选择同机双人。");
                return;
            }

            state.Launch.Mode = Choice == PartnerChoice.Bot ? AppState.GameMode.LocalSolo : AppState.GameMode.LocalCoop;
            state.Launch.JoinAddress = string.Empty;
            state.Launch.RoomName = string.Empty;
            SceneManager.LoadScene(AppState.ScenePalmBay);
        }

        bool HasVerifiedRoomIdentity
        {
            get
            {
                AuthUser user = state == null || state.Auth == null ? null : state.Auth.User;
                return user != null && state.CanPlayWithOthers && !string.IsNullOrEmpty(user.IdToken);
            }
        }

        bool RequireVerifiedUser()
        {
            AuthUser user = state.Auth == null ? null : state.Auth.User;
            if (user == null)
            {
                ShowSignIn("真人房间需要登录并年满 13 岁。" + AuthNoticeSuffix());
                return false;
            }
            if (!state.CanPlayWithOthers)
            {
                ShowAgeCheck("加入真人房间前，请先完成 13 岁年龄验证。");
                return false;
            }
            if (string.IsNullOrEmpty(user.IdToken))
            {
                ShowSignIn("登录凭据无效，请重试或退出当前账号。" + AuthNoticeSuffix());
                return false;
            }
            return true;
        }

        public void CreateRoom()
        {
            pendingRoomAction = PendingRoomAction.Create;
            if (!RequireVerifiedUser()) return;
            pendingRoomAction = PendingRoomAction.None;
            CreateRoomVerified();
        }

        void CreateRoomVerified()
        {
            if (state.Room == null || state.Room.Phase != RoomPhase.Idle)
            {
                CancelPendingRoomAction();
                ShowToast("正在离开上一个房间，请稍后重试。");
                return;
            }
            AuthUser user = state.Auth.User;
            string hostName = state.Profile.Name;
            string roomName = state.Profile.Name + " 的房间";
            if (!state.Room.Host(roomName, hostName, user.IdToken))
            {
                CancelPendingRoomAction();
                ShowToast("创建房间失败，请检查网络后重试。");
                return;
            }
            state.Events.Enqueue(GameEventQueue.RoomCreated);
            View = ViewState.InRoom;
            Choice = PartnerChoice.None;
            ApplyPartnerAppearance();
            UpdateBeacon();
            RefreshView();
            ShowToast(state.Beacon == null ? "房间已创建 · 自动发现不可用，可让搭档手动输入 IP。" : "房间已创建，等待搭档加入。");
        }

        public void JoinDiscoveredRoom(int index)
        {
            List<RoomInfo> rooms = Rooms;
            if (index < 0 || index >= rooms.Count) return;
            RoomInfo room = rooms[index];
            if (room.State == "playing")
            {
                ShowToast("这班已经起飞，无法加入。");
                return;
            }
            if (room.Taken >= room.Seats)
            {
                ShowToast("房间已满，无法加入。");
                return;
            }
            JoinAddress(room.Address, room.Name);
        }

        public void JoinAddress(string address)
        {
            JoinAddress(address, "好友房间");
        }

        void JoinAddress(string address, string roomName)
        {
            address = (address ?? string.Empty).Trim();
            if (address.Length == 0)
            {
                ShowToast("请输入搭档的 IP 地址。");
                return;
            }
            pendingRoomAction = PendingRoomAction.Join;
            pendingJoinAddress = address;
            pendingJoinRoomName = string.IsNullOrEmpty(roomName) ? "好友房间" : roomName;
            if (!RequireVerifiedUser()) return;
            pendingRoomAction = PendingRoomAction.None;
            if (state.Room == null || state.Room.Phase != RoomPhase.Idle)
            {
                CancelPendingRoomAction();
                ShowToast("正在离开上一个房间，请稍后重试。");
                return;
            }
            AuthUser user = state.Auth.User;
            string displayName = state.Profile.Name;
            joinedRoomName = pendingJoinRoomName;
            activeJoinAddress = pendingJoinAddress;
            activeJoinRoomName = pendingJoinRoomName;
            pendingJoinAddress = string.Empty;
            pendingJoinRoomName = string.Empty;
            if (!state.Room.Join(address, displayName, user.IdToken))
            {
                activeJoinAddress = string.Empty;
                activeJoinRoomName = string.Empty;
                ShowToast("无法连接房间，请检查 IP 和网络。");
                return;
            }
            View = ViewState.InRoom;
            Choice = PartnerChoice.None;
            RefreshView();
            ShowToast("正在连接房间…");
        }

        public void LeaveRoom()
        {
            bool hadRoom = !debugRoomFixture && state.Room != null && state.Room.Phase != RoomPhase.Idle && state.Room.Phase != RoomPhase.Closed;
            if (hadRoom) state.Events.Enqueue(GameEventQueue.RoomLeft);
            debugRoomFixture = false;
            debugRoomFixtureName = string.Empty;
            debugRoomFixturePeer = string.Empty;
            if (state.Room != null) state.Room.Leave();
            if (state.Beacon != null) state.Beacon.Stop();
            CancelPendingRoomAction();
            activeJoinAddress = string.Empty;
            activeJoinRoomName = string.Empty;
            View = ViewState.Main;
            Choice = PartnerChoice.None;
            ApplyPartnerAppearance();
            RefreshView();
            ShowToast("已离开房间。");
        }

        public void OpenSignIn()
        {
            if (HasAuthUser && !state.CanPlayWithOthers)
            {
                ShowAgeCheck("请完成 13 岁年龄验证后加入真人房间。");
                return;
            }
            ShowSignIn("真人房间需要登录并年满 13 岁；本地单人和 bot 无需登录。" + AuthNoticeSuffix());
        }

        public void CloseSignIn()
        {
            CancelPendingRoomAction();
            if (AuthBusy) state.CancelSignIn();
            SignInMessage = string.Empty;
            View = ViewState.Main;
            RefreshView();
        }

        public void CloseAgeCheck()
        {
            CancelPendingRoomAction();
            AgeMessage = string.Empty;
            View = ViewState.Main;
            RefreshView();
        }

        public void HandleSignInAction()
        {
            if (AuthBusy)
            {
                state.CancelSignIn();
                CancelPendingRoomAction();
                SignInMessage = string.Empty;
                View = ViewState.Main;
                RefreshView();
                return;
            }
            SignInMessage = IsFakeAuth ? "正在运行 Fake 测试登录，不会连接 Firebase。" : "正在打开 Google 登录。";
            View = ViewState.SignIn;
            RefreshView();
            state.SignInGoogle();
            RefreshView();
        }

        public void SignOut()
        {
            if (state == null || state.Auth == null) return;
            bool hadRoom = !debugRoomFixture && state.Room != null && state.Room.Phase != RoomPhase.Idle && state.Room.Phase != RoomPhase.Closed;
            if (hadRoom) state.Events.Enqueue(GameEventQueue.RoomLeft);
            debugRoomFixture = false;
            debugRoomFixtureName = string.Empty;
            debugRoomFixturePeer = string.Empty;
            if (state.Room != null) state.Room.Leave();
            if (state.Beacon != null) state.Beacon.Stop();
            state.SignOut();
            CancelPendingRoomAction();
            activeJoinAddress = string.Empty;
            activeJoinRoomName = string.Empty;
            Choice = PartnerChoice.None;
            joinedRoomName = string.Empty;
            SignInMessage = string.Empty;
            AgeMessage = string.Empty;
            View = ViewState.Main;
            CabinWorld.SetPartnerAppearance(cabinRoot, false, false);
            RefreshView();
            ShowToast("已退出登录。局域网房间已安全关闭。");
        }

        public bool VerifyAge(int year, int month, int day)
        {
            bool hadPendingAction = pendingRoomAction != PendingRoomAction.None;
            string error;
            if (!state.TryVerifyAge(year, month, day, out error))
            {
                ClearToast();
                AgeMessage = error;
                View = ViewState.AgeCheck;
                RefreshView();
                return false;
            }

            ClearToast();
            AgeMessage = "已完成 13 岁年龄验证。出生日期不会保存。";
            View = ViewState.Main;
            RefreshView();
            ResumePendingRoomAction();
            if (!hadPendingAction) ShowToast("年龄验证完成，可以加入真人房间。");
            return true;
        }

        public void OpenInvitationPanel()
        {
            SelectedInviteRole = InviteRole.Meals;
            ShareResultMessage = string.Empty;
            View = ViewState.Invitation;
            if (state != null && state.Events != null)
            {
                ShareService.RecordSharePanelOpened(state.Events, state.Events.ActorId, state.Events.PairId, state.Events.MatchId);
            }
            RefreshView();
        }

        public void CloseInvitationPanel()
        {
            View = NetworkRoomActive ? ViewState.InRoom : ViewState.Main;
            RefreshView();
        }

        public void SelectInviteRole(InviteRole role)
        {
            if (role != InviteRole.Meals && role != InviteRole.Baggage && role != InviteRole.Fuel) return;
            SelectedInviteRole = role;
            RefreshView();
        }

        public void ShareInvite()
        {
            ShareActionResult result = ShareService.ShareInvite(SelectedInviteRole, InviteSourceId);
            ShareResultMessage = ShareService.GetResultMessage(result);
            ShowToast(ShareResultMessage);
            RefreshView();
        }

        public void DebugInjectRooms(params RoomInfo[] rooms)
        {
            debugRooms = new List<RoomInfo>();
            if (rooms != null) debugRooms.AddRange(rooms);
            RefreshView();
        }

        public void DebugClearRooms()
        {
            debugRooms = null;
            RefreshView();
        }

        public void DebugEnterRoomFixture(string roomName, string peerName)
        {
            debugRoomFixture = true;
            debugRoomFixtureHost = true;
            debugRoomFixtureName = string.IsNullOrEmpty(roomName) ? "截图示例房间" : roomName;
            debugRoomFixturePeer = string.IsNullOrEmpty(peerName) ? "搭档示例" : peerName;
            View = ViewState.InRoom;
            Choice = PartnerChoice.None;
            ApplyPartnerAppearance();
            RefreshView();
        }

        public void DebugEnterClientRoomFixture(string roomName)
        {
            debugRoomFixture = true;
            debugRoomFixtureHost = false;
            debugRoomFixtureName = string.IsNullOrEmpty(roomName) ? "好友的房间" : roomName;
            debugRoomFixturePeer = "房主示例";
            View = ViewState.InRoom;
            Choice = PartnerChoice.None;
            ApplyPartnerAppearance();
            RefreshView();
        }

        public void DebugExitRoomFixture()
        {
            debugRoomFixture = false;
            debugRoomFixtureHost = true;
            debugRoomFixtureName = string.Empty;
            debugRoomFixturePeer = string.Empty;
            View = ViewState.Main;
            ApplyPartnerAppearance();
            RefreshView();
        }

        public string FixturePeerName { get { return debugRoomFixturePeer; } }

        void ShowSignIn(string message)
        {
            View = ViewState.SignIn;
            SignInMessage = message;
            RefreshView();
        }

        void ShowAgeCheck(string message)
        {
            ClearToast();
            View = ViewState.AgeCheck;
            AgeMessage = message;
            RefreshView();
        }

        void ClearToast()
        {
            if (view) view.ShowToast(string.Empty);
        }

        string AuthNoticeSuffix()
        {
            if (string.IsNullOrEmpty(state.AuthNotice)) return string.Empty;
            if (state.IsFakeAuth && state.Auth.User == null) return "\n" + state.AuthNotice;
            return string.Empty;
        }

        void CancelPendingRoomAction()
        {
            pendingRoomAction = PendingRoomAction.None;
            pendingJoinAddress = string.Empty;
            pendingJoinRoomName = string.Empty;
        }

        void ResumePendingRoomAction()
        {
            PendingRoomAction action = pendingRoomAction;
            string address = pendingJoinAddress;
            string roomName = pendingJoinRoomName;
            CancelPendingRoomAction();
            if (action == PendingRoomAction.Create) CreateRoom();
            else if (action == PendingRoomAction.Join) JoinAddress(address, roomName);
        }

        void DrainAuthEvents()
        {
            if (state == null) return;
            string result;
            while (state.TryDequeueAuth(out result))
            {
                if (string.IsNullOrEmpty(result)) continue;
                if (result.StartsWith("refreshed:", StringComparison.Ordinal))
                {
                    // Silent token renewal must not dismiss the room or an open overlay.
                    RefreshView();
                    continue;
                }
                if (result.StartsWith("ok:", StringComparison.Ordinal) || result.StartsWith("restored:", StringComparison.Ordinal))
                {
                    ClearToast();
                    if (state.Auth != null && state.Auth.User != null && !state.CanPlayWithOthers)
                    {
                        ShowAgeCheck("首次登录需要确认已满 13 岁。仅保存验证结果，不保存出生日期。");
                    }
                    else
                    {
                        View = ViewState.Main;
                        SignInMessage = string.Empty;
                        RefreshView();
                    }

                    if (state.CanPlayWithOthers) ResumePendingRoomAction();
                    continue;
                }
                if (result == "cancelled")
                {
                    CancelPendingRoomAction();
                    SignInMessage = state.AuthNotice;
                    if (View == ViewState.SignIn) RefreshView();
                    else ShowToast("登录已取消，房间操作已取消。");
                    continue;
                }
                if (result == "out")
                {
                    if (NetworkRoomActive || Connecting) LeaveRoom();
                    CancelPendingRoomAction();
                    Choice = PartnerChoice.None;
                    if (state.Beacon != null) state.Beacon.Stop();
                    View = ViewState.Main;
                    RefreshView();
                    continue;
                }
                if (result.StartsWith("fail:", StringComparison.Ordinal))
                {
                    if (NetworkRoomActive && HasVerifiedRoomIdentity)
                    {
                        ShowToast(state.AuthNotice);
                        RefreshView();
                        continue;
                    }
                    if ((NetworkRoomActive || Connecting) && !HasVerifiedRoomIdentity) LeaveRoom();
                    SignInMessage = state.AuthNotice;
                    View = ViewState.SignIn;
                    RefreshView();
                }
            }
        }

        public void SaveDisplayName(string rawName)
        {
            if (!PlayerProfile.IsValidName(rawName))
            {
                ShowToast("名字需为 1–12 个字符。");
                return;
            }

            state.Profile.Name = PlayerProfile.NormalizeName(rawName);
            state.SaveProfile();
            RefreshView();
            ShowToast("名字已保存。");
        }

        public void SetColor(int colorIndex)
        {
            if (colorIndex < 0 || colorIndex >= 6) return;
            state.Profile.ColorIndex = colorIndex;
            state.SaveProfile();
            CabinWorld.SetPlayerColor(cabinRoot, colorIndex);
            RefreshView();
        }

        void RefreshView()
        {
            ApplyPartnerAppearance();
            if (view) view.RefreshNow();
        }

        void ShowToast(string message)
        {
            if (view) view.ShowToast(message);
        }
    }
}
