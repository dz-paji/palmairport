using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace IslandAirport
{
    /// <summary>
    /// 跨场景单例（DontDestroyOnLoad）：玩家档案/认证/房间/局域网发现/埋点的唯一持有者。
    /// Ensure() 兜底创建（直接 Play PalmBay 也有实例），重复实例自动销毁。
    /// M2 步骤 4 先落地骨架：本步不挂进任何场景，AirportGame 不引用；
    /// CabinLobby 场景步骤 5 才建（场景名常量先定义），步骤 6 起 AirportGame 按 Launch.Mode 分发。
    /// 全部网络推进走 PumpNet(dt)：Update 以 Time.deltaTime 调用，batchmode 批测可手动固定步长驱动。
    /// </summary>
    public sealed class AppState : MonoBehaviour
    {
        /// <summary>当局运行模式：本地单人 / 本地双人 / 联机 host 自由练习 / 联机 client 自由练习。</summary>
        public enum GameMode { LocalSolo = 0, LocalCoop = 1, HostSandbox = 2, ClientSandbox = 3, HostShift = 4, ClientShift = 5 }

        /// <summary>场景名常量（与 Build Settings 对齐；CabinLobby 场景步骤 5 才建）。</summary>
        public const string SceneCabinLobby = "CabinLobby";
        public const string ScenePalmBay = "PalmBay";
        public const string SceneMainLoop = "MainLoop";

        /// <summary>一次性启动参数：模式/直连地址/房名；被消费后调 Reset() 还原。</summary>
        public sealed class LaunchConfig
        {
            public GameMode Mode = GameMode.LocalSolo;
            public string JoinAddress = string.Empty;
            public string RoomName = string.Empty;

            public void Reset()
            {
                Mode = GameMode.LocalSolo;
                JoinAddress = string.Empty;
                RoomName = string.Empty;
            }
        }

        public static AppState Instance { get; private set; }

        /// <summary>玩家档案（名字校验+持久化往返见 Core/Auth/PlayerProfile.cs）。</summary>
        public PlayerProfile Profile { get; private set; }

        /// <summary>认证服务。步骤 8 有配置时换 FirebaseRestAuth（缺配置整体回退 Fake+提示）。</summary>
        public IAuthService Auth { get; private set; }

        public bool IsFakeAuth { get; private set; }
        public int AuthSessionRevision { get; private set; }
        public bool NetworkAuthInvalidated { get; private set; }

        public string AuthNotice { get; private set; }

        public AccountProgressRuntime AccountProgress { get; private set; }
        public string AccountProgressNotice
        {
            get
            {
                if (IsFakeAuth) return "测试模式 · 不会上传账号进度";
                if (Auth == null || Auth.User == null) return "登录后可查看账号进度";
                if (AccountProgress == null) return "云端进度暂不可用";
                switch (AccountProgress.Status)
                {
                    case "synced": return "账号进度已同步";
                    case "loading": return "正在读取账号进度…";
                    case "pending": case "syncing": return "本局已保存在本机，正在同步账号进度…";
                    case "storage_error": return "本机保存失败，正在重试；退出游戏可能丢失本局记录";
                    case "auth_required": return "登录已过期，账号记录待重新登录后同步";
                    case "receipt_conflict": return "本局记录存在冲突，未重复记账";
                    case "invalid_settlement": return "本局记录无效，未同步进度";
                    case "unconfigured": return "云端进度尚未启用，账号记录保存在本机待同步";
                    default: return "云端暂不可用，账号记录保存在本机待重试";
                }
            }
        }

        public bool ExternalControl;

        public bool CanPlayWithOthers
        {
            get { return AgeVerifications != null && AgeVerifications.IsVerifiedFor(Auth == null ? null : Auth.User, IsFakeAuth); }
        }

        /// <summary>房间状态机（host 权威 / client 意图，全 Pump(dt) 计时）。</summary>
        public RoomManager Room { get; private set; }

        /// <summary>局域网房间发现：开播端（host 建房后 Start）。绑定失败时为 null。</summary>
        public BeaconAnnouncer Beacon { get; private set; }

        /// <summary>局域网房间发现：收听端（大厅房间列表）。绑定失败时为 null（如双开占用端口）。</summary>
        public BeaconListener Listener { get; private set; }

        /// <summary>跨场景一次性启动参数。</summary>
        public LaunchConfig Launch { get; private set; }

        /// <summary>埋点事件队列（JSONL 落盘 persistentDataPath/palmbay-events.jsonl）。</summary>
        public GameEventQueue Events { get; private set; }

        BotMemoryStore botMemoryStore;
        public BotMemory BotMemory { get { return botMemoryStore == null ? null : botMemoryStore.Memory; } }
        const string BotMemoryFileName = "palmbay-cooperation.json";

        public bool RecordCoopMatch(string matchId, int departedFlights, int[] humanTaskCounts, string date = null)
        {
            if (botMemoryStore == null) return false;
            try { return botMemoryStore.Record(matchId, departedFlights, humanTaskCounts, date ?? DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)); }
            catch (Exception e) { Debug.LogWarning("合作记录保存失败，稍后可重试：" + e.Message); return false; }
        }

        void LoadBotMemory()
        {
            string path = Path.Combine(Application.persistentDataPath, BotMemoryFileName);
            botMemoryStore = new BotMemoryStore(() => {
                try { return File.Exists(path) ? File.ReadAllText(path) : string.Empty; }
                catch (IOException) { return string.Empty; }
                catch (UnauthorizedAccessException) { return string.Empty; }
            }, json => {
                Directory.CreateDirectory(Application.persistentDataPath);
                string temporary = path + ".tmp";
                File.WriteAllText(temporary, json);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            });
        }

        const string ProfilePrefsKey = "palmbay.profile";
        const string AuthRefreshPrefsKey = "palmbay.auth.refresh";
        const string AgeVerificationPrefsKey = "palmbay.auth.age-status";
        const string EventsFileName = "palmbay-events.jsonl";
        const float EventsFlushInterval = 5f;

        IUdpSocket beaconSendSocket;
        IUdpSocket beaconListenSocket;
        readonly Queue<string> pendingAuthEvents = new Queue<string>();
        AgeVerificationStore AgeVerifications;
        float eventsFlushTimer;
        bool fakeSignInCancelled;
        bool shuttingDown;

        /// <summary>取全局实例；不存在则兜底创建。</summary>
        public static AppState Ensure()
        {
            if (Instance != null) return Instance;
            Instance = FindAnyObjectByType<AppState>();
            if (Instance != null) return Instance;
            var go = new GameObject("AppState");
            return go.AddComponent<AppState>(); // Awake 登记 Instance + DontDestroyOnLoad
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // 重复实例（多入口场景各自兜底创建时）销毁，保留首个。
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            Profile = LoadProfile();
            LoadBotMemory();
            Room = new RoomManager();
            Launch = new LaunchConfig();
            Events = new GameEventQueue(AppendEventLine, ReadEventLines);
            AgeVerifications = AgeVerificationStore.FromJson(PlayerPrefs.GetString(AgeVerificationPrefsKey, string.Empty));
            ConfigureAuth();
            var progressAuth = Auth as FirebaseRestAuth;
            AccountProgress = new AccountProgressRuntime(progressAuth == null || progressAuth.Configuration == null ? string.Empty : progressAuth.Configuration.ProjectId,
                Application.persistentDataPath, () => Auth == null ? null : Auth.User, () => IsFakeAuth);

            try
            {
                beaconSendSocket = new UdpSocketReal(0, true);
                Beacon = new BeaconAnnouncer(beaconSendSocket);
            }
            catch (Exception e)
            {
                Debug.LogWarning("AppState: beacon 发送套接创建失败，房间开播不可用。" + e.Message);
            }

            AndroidNet.Install();
            try
            {
                beaconListenSocket = new UdpSocketReal(NetProtocol.BeaconPort, true);
                Listener = new BeaconListener(beaconListenSocket);
            }
            catch (Exception e)
            {
                // 同机双实例只有一个能绑定广播端口；收听失败不阻断其余功能（可手动输入 IP 加入）。
                Debug.LogWarning("AppState: beacon 收听套接绑定失败，自动发现不可用。" + e.Message);
            }
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            ShutdownRuntime();
            Instance = null;
        }

        void OnApplicationQuit()
        {
            if (Instance == this) FlushEvents();
        }

        void Update()
        {
            if (!ExternalControl) PumpNet(Time.deltaTime);
        }

        /// <summary>驱动房间/发现/认证/埋点的 Pump。批测可绕过 Update 手动固定步长调用。</summary>
        public void PumpNet(float dt)
        {
            if (shuttingDown) return;
            if (Room != null) Room.Pump(dt);
            if (Beacon != null) Beacon.Pump(dt);
            if (Listener != null) Listener.Pump(dt);
            if (Auth != null) Auth.Pump(dt);
            DrainAuthEvents();
            if (AccountProgress != null) AccountProgress.Pump(dt);
            if (Events != null) Events.Tick(dt);
            if (dt > 0f && !float.IsNaN(dt) && !float.IsInfinity(dt))
            {
                eventsFlushTimer += dt;
                if (eventsFlushTimer >= EventsFlushInterval)
                {
                    FlushEvents();
                    eventsFlushTimer = 0f;
                }
            }
        }

        public void SignInGoogle()
        {
            if (Auth == null) return;
            fakeSignInCancelled = false;
            AuthNotice = IsFakeAuth
                ? "Fake 测试登录中；不会连接 Firebase。"
                : "正在打开 Google 登录。";
            UpdateEventIdentity();
            Events.Enqueue(GameEventQueue.LoginStarted, null, AuthEventParams("auth_mode", IsFakeAuth ? "fake" : "google"));
            Auth.SignInGoogle();
            DrainAuthEvents();
        }

        public void CancelSignIn()
        {
            if (Auth == null || !Auth.Busy) return;
            FirebaseRestAuth firebase = Auth as FirebaseRestAuth;
            if (firebase != null)
            {
                firebase.Cancel();
                DrainAuthEvents();
                return;
            }

            if (Auth.User == null)
            {
                Auth.SignOut();
                string ignored;
                while (Auth.TryDequeue(out ignored))
                {
                }
            }
            else
            {
                fakeSignInCancelled = true;
            }

            HandleAuthResult("cancelled");
            pendingAuthEvents.Enqueue("cancelled");
        }

        public void SignOut()
        {
            if (Auth == null) return;
            AuthSessionRevision++;
            fakeSignInCancelled = false;
            AgeVerifications.ClearCurrentUser(Auth.User);
            Auth.SignOut();
            if (!IsFakeAuth) ClearRefreshToken();
            UpdateEventIdentity();
            DrainAuthEvents();
        }

        public bool TryDequeueAuth(out string result)
        {
            if (pendingAuthEvents.Count > 0)
            {
                result = pendingAuthEvents.Dequeue();
                return true;
            }

            result = null;
            return false;
        }

        public bool TryVerifyAge(int year, int month, int day, out string error)
        {
            error = "请先登录，再验证年龄。";
            if (Auth == null || Auth.User == null) return false;

            AuthUser user = Auth.User;
            bool ageVerified;
            bool validDate = AgeVerifications.TryRecordBirthDate(user.Uid, IsFakeAuth, year, month, day,
                DateTime.Today, out ageVerified, out error);
            if (!validDate) return false;

            user.AgeVerified = ageVerified;
            PlayerPrefs.SetString(AgeVerificationPrefsKey, AgeVerifications.ToJson());
            PlayerPrefs.Save();
            UpdateEventIdentity();
            if (ageVerified) return true;

            Dictionary<string, object> parameters = new Dictionary<string, object>();
            parameters["minimum_age"] = (double)AgeCheck.DefaultMinAge;
            parameters["scope"] = "online_party";
            Events.Enqueue(GameEventQueue.AgeBlocked, null, parameters);
            FlushEvents();
            return false;
        }

        public void UseAuthForTesting(IAuthService auth)
        {
            if (auth == null) throw new ArgumentNullException("auth");
            if (auth is FirebaseRestAuth) throw new ArgumentException("Testing auth must not use Firebase.", "auth");
            SetAuth(auth, true, "Fake 测试认证已注入；不会读取或修改已保存的登录状态。", true);
            fakeSignInCancelled = false;
            pendingAuthEvents.Clear();
            UpdateEventIdentity();
        }

        /// <summary>档案改完（衣柜改名换色）后落盘。</summary>
        public void SaveProfile()
        {
            PlayerPrefs.SetString(ProfilePrefsKey, Profile.ToJson());
            PlayerPrefs.Save();
        }

        static PlayerProfile LoadProfile()
        {
            PlayerProfile profile = PlayerProfile.FromJson(PlayerPrefs.GetString(ProfilePrefsKey, string.Empty));
            if (profile == null) profile = new PlayerProfile();
            if (string.IsNullOrEmpty(profile.GuestId))
            {
                // 首次运行签发游客 ID 并立即落盘，保证下次启动稳定。
                profile.GuestId = string.Format("Guest_{0:000}", UnityEngine.Random.Range(0, 1000));
                PlayerPrefs.SetString(ProfilePrefsKey, profile.ToJson());
                PlayerPrefs.Save();
            }

            return profile;
        }

        void ConfigureAuth()
        {
            FirebaseRestAuth firebase = new FirebaseRestAuth();
            if (firebase.IsConfigured)
            {
                SetAuth(firebase, false, string.Empty, false);
                string refreshToken = PlayerPrefs.GetString(AuthRefreshPrefsKey, string.Empty);
                if (!string.IsNullOrEmpty(refreshToken)) Auth.Restore(refreshToken);
                return;
            }

#if UNITY_EDITOR
            string notice = firebase.Platform == FirebaseAuthPlatform.Desktop
                ? "Desktop OAuth client 未配置；当前使用 Fake 测试账号，不会连接 Firebase。"
                : "Google 登录当前不可用；当前使用 Fake 测试账号，不会连接 Firebase。";
            firebase.Dispose();
            SetAuth(new FakeAuthService(), true, notice, false);
#else
            // A shipped player must never turn missing OAuth configuration into
            // a successful test identity. Offline guest play stays available.
            SetAuth(firebase, false, "Google 登录尚未配置，暂不能真人组队；可先与 bot 离线游玩。", false);
#endif
        }

        void SetAuth(IAuthService auth, bool fake, string notice, bool disposePrevious)
        {
            FirebaseRestAuth previousFirebase = Auth as FirebaseRestAuth;
            if (previousFirebase != null) previousFirebase.TokenRefreshed -= OnTokenRefreshed;
            if (disposePrevious && Auth != null)
            {
                IDisposable disposable = Auth as IDisposable;
                if (disposable != null) disposable.Dispose();
            }

            AuthSessionRevision++;
            Auth = auth;
            NetworkAuthInvalidated = false;
            IsFakeAuth = fake;
            AuthNotice = notice ?? string.Empty;
            FirebaseRestAuth currentFirebase = Auth as FirebaseRestAuth;
            if (currentFirebase != null) currentFirebase.TokenRefreshed += OnTokenRefreshed;
            UpdateEventIdentity();
        }

        void OnTokenRefreshed(AuthUser user)
        {
            if (IsFakeAuth || user == null || Auth == null || Auth.User != user) return;
            ApplyAgeVerification(user);
            SaveRefreshToken(user);
        }

        void DrainAuthEvents()
        {
            if (Auth == null) return;
            string result;
            while (Auth.TryDequeue(out result))
            {
                if (IsFakeAuth && fakeSignInCancelled)
                {
                    fakeSignInCancelled = false;
                    if (result != null && result.StartsWith("ok:", StringComparison.Ordinal))
                    {
                        Auth.SignOut();
                        string ignored;
                        while (Auth.TryDequeue(out ignored))
                        {
                        }
                    }

                    continue;
                }

                HandleAuthResult(result);
                pendingAuthEvents.Enqueue(result);
            }
        }

        void HandleAuthResult(string result)
        {
            if (string.IsNullOrEmpty(result)) return;

            if (result.StartsWith("ok:", StringComparison.Ordinal) ||
                result.StartsWith("restored:", StringComparison.Ordinal) ||
                result.StartsWith("refreshed:", StringComparison.Ordinal))
            {
                AuthUser user = Auth == null ? null : Auth.User;
                int separator = result.IndexOf(':');
                string resultUid = separator >= 0 ? result.Substring(separator + 1) : string.Empty;
                if (user == null || !string.Equals(user.Uid, resultUid, StringComparison.Ordinal)) return;

                if (!result.StartsWith("refreshed:", StringComparison.Ordinal)) AuthSessionRevision++;
                NetworkAuthInvalidated = false;
                ApplyAgeVerification(user);
                if (!IsFakeAuth) SaveRefreshToken(user);
                AuthNotice = IsFakeAuth
                    ? "Fake 测试账号已登录；不会连接 Firebase。"
                    : string.Empty;
                UpdateEventIdentity();
                if (result.StartsWith("ok:", StringComparison.Ordinal))
                {
                    Events.Enqueue(GameEventQueue.LoginOk, null, AuthEventParams("auth_mode", IsFakeAuth ? "fake" : "google"));
                }
                return;
            }

            if (result == "out")
            {
                AuthSessionRevision++;
                InvalidateNetworkAuth();
                if (!IsFakeAuth) ClearRefreshToken();
                AuthNotice = IsFakeAuth ? "Fake 测试账号已退出。" : "已退出登录。";
                UpdateEventIdentity();
                return;
            }

            if (result == "cancelled")
            {
                AuthNotice = IsFakeAuth
                    ? "Fake 登录已取消；不会连接 Firebase。"
                    : "登录已取消，可重试。";
                return;
            }

            if (result.StartsWith("fail:", StringComparison.Ordinal))
            {
                string reason = result.Substring("fail:".Length);
                if (Auth == null || Auth.User == null) InvalidateNetworkAuth();
                if (reason == "token" && !IsFakeAuth) ClearRefreshToken();
                AuthNotice = AuthFailureNotice(reason);
                Events.Enqueue(GameEventQueue.LoginFail, null, AuthEventParams("reason", SafeAuthReason(reason)));
            }
        }

        void InvalidateNetworkAuth()
        {
            if (Room == null || Room.Phase == RoomPhase.Idle || Room.Phase == RoomPhase.Closed) return;
            AuthSessionRevision++;
            NetworkAuthInvalidated = true;
            Room.Leave(); // Production host hands the running shift to its peer.
            if (Beacon != null) Beacon.Stop();
            if (Launch != null) Launch.Reset();
        }

        void ApplyAgeVerification(AuthUser user)
        {
            if (user == null) return;
            user.AgeVerified = AgeVerifications.IsVerified(user.Uid, IsFakeAuth);
        }

        void SaveRefreshToken(AuthUser user)
        {
            if (IsFakeAuth || user == null) return;
            if (string.IsNullOrEmpty(user.RefreshToken))
            {
                ClearRefreshToken();
                return;
            }

            PlayerPrefs.SetString(AuthRefreshPrefsKey, user.RefreshToken);
            PlayerPrefs.Save();
        }

        void ClearRefreshToken()
        {
            PlayerPrefs.DeleteKey(AuthRefreshPrefsKey);
            PlayerPrefs.Save();
        }

        void UpdateEventIdentity()
        {
            if (Events == null) return;
            AuthUser user = Auth == null ? null : Auth.User;
            Events.ActorId = user != null && !string.IsNullOrEmpty(user.Uid)
                ? user.Uid
                : (Profile == null ? string.Empty : Profile.GuestId);
        }

        static Dictionary<string, object> AuthEventParams(string key, string value)
        {
            Dictionary<string, object> parameters = new Dictionary<string, object>();
            parameters[key] = value ?? string.Empty;
            return parameters;
        }

        static string SafeAuthReason(string reason)
        {
            switch (reason)
            {
                case "token":
                case "network":
                case "configuration":
                case "provider":
                case "response":
                case "browser":
                case "timeout":
                case "unconfigured":
                    return reason;
                default:
                    return "unknown";
            }
        }

        string AuthFailureNotice(string reason)
        {
            switch (reason)
            {
                case "token":
                    return "登录凭据已失效，请重新登录。";
                case "unconfigured":
                case "configuration":
                    return IsFakeAuth
                        ? "Fake 登录当前不可用；不会连接 Firebase。"
                        : "Google 登录配置不可用，请稍后重试。";
                case "network":
                case "timeout":
                    return "登录失败，请检查网络后重试。";
                default:
                    return "Google 登录未完成，请重试。";
            }
        }

        void FlushEvents()
        {
            if (Events != null) Events.Flush();
        }

        void ShutdownRuntime()
        {
            if (shuttingDown) return;
            shuttingDown = true;
            if (AccountProgress != null) AccountProgress.Dispose();

            if (Room != null)
            {
                Room.Leave();
                for (int i = 0; i < 10 && Room.Phase == RoomPhase.Closed; i++) Room.Pump(0.15f);
            }

            if (Beacon != null) Beacon.Stop();
            if (beaconListenSocket != null)
            {
                beaconListenSocket.Close();
                beaconListenSocket = null;
            }

            if (beaconSendSocket != null)
            {
                beaconSendSocket.Close();
                beaconSendSocket = null;
            }

            AndroidNet.Release();
            FirebaseRestAuth firebase = Auth as FirebaseRestAuth;
            if (firebase != null)
            {
                firebase.TokenRefreshed -= OnTokenRefreshed;
                firebase.Dispose();
            }

            FlushEvents();
        }

        string EventsPath
        {
            get { return Path.Combine(Application.persistentDataPath, EventsFileName); }
        }

        void AppendEventLine(string line)
        {
            try
            {
                File.AppendAllText(EventsPath, line + "\n");
            }
            catch (Exception e)
            {
                Debug.LogWarning("AppState: 埋点落盘失败。" + e.Message);
            }
        }

        IEnumerable<string> ReadEventLines()
        {
            try
            {
                return File.Exists(EventsPath) ? File.ReadAllLines(EventsPath) : new string[0];
            }
            catch (Exception e)
            {
                Debug.LogWarning("AppState: 埋点读取失败。" + e.Message);
                return new string[0];
            }
        }
    }
}
