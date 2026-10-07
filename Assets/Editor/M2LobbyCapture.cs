using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using IslandAirport;

[InitializeOnLoad]
public static class M2LobbyCapture
{
    static string EvidenceRoot { get { return SessionState.GetString("M2LobbyCapture.Evidence", Environment.GetEnvironmentVariable("PALMBAY_VISUAL_EVIDENCE") ?? "Evidence"); } }
    static IEnumerator steps;
    static double started;
    static bool failed;
    static string savedName;
    static int savedColor;
    static bool savedProfileKeyPresent;
    static bool savedAgeKeyPresent;
    static bool savedRefreshKeyPresent;
    static string savedProfileKey;
    static string savedAgeKey;
    static string savedRefreshKey;
    static string savedClipboard;
    static string savedEventsPath;
    static bool savedEventsPresent;
    static string savedEventsContents;
    static string savedEventsBackupPath;
    static bool snapshotsSaved;
    static readonly StringBuilder report = new StringBuilder();
    const string Pending = "PalmBay.M2LobbyCapture";

    sealed class CaptureAuthService : IAuthService
    {
        readonly FakeAuthService inner = new FakeAuthService();
        readonly Queue<string> events = new Queue<string>();
        readonly string uidPrefix = "m2-capture-" + Guid.NewGuid().ToString("N") + "-";
        int signInCount;

        public AuthUser User { get; private set; }
        public bool Busy { get { return inner.Busy; } }
        public FakeAuthService.FakeMode Mode { get { return inner.Mode; } set { inner.Mode = value; } }
        public float Delay { get { return inner.Delay; } set { inner.Delay = value; } }

        public void SignInGoogle() { inner.SignInGoogle(); }

        public void SignOut()
        {
            inner.SignOut();
            DrainInnerEvents();
        }

        public void Restore(string refreshToken) { inner.Restore(refreshToken); }

        public bool TryDequeue(out string result)
        {
            if (events.Count > 0)
            {
                result = events.Dequeue();
                return true;
            }
            result = null;
            return false;
        }

        public void Pump(float dt)
        {
            inner.Pump(dt);
            DrainInnerEvents();
        }

        void DrainInnerEvents()
        {
            string result;
            while (inner.TryDequeue(out result))
            {
                if (result != null && (result.StartsWith("ok:", StringComparison.Ordinal) || result.StartsWith("restored:", StringComparison.Ordinal) || result.StartsWith("refreshed:", StringComparison.Ordinal)))
                {
                    AuthUser source = inner.User;
                    if (source != null)
                    {
                        signInCount++;
                        User = new AuthUser
                        {
                            Uid = uidPrefix + signInCount,
                            DisplayName = source.DisplayName,
                            IdToken = source.IdToken,
                            RefreshToken = source.RefreshToken,
                            AgeVerified = source.AgeVerified
                        };
                        result = result.Substring(0, result.IndexOf(':')) + ":" + User.Uid;
                    }
                }
                else if (result == "out")
                {
                    User = null;
                }
                events.Enqueue(result);
            }
        }
    }

    static M2LobbyCapture()
    {
        EditorApplication.update += Poll;
    }

    [MenuItem("Palm Bay/Capture M2 CabinLobby smoke")]
    public static void Run()
    {
        SessionState.EraseString("M2LobbyCapture.Evidence");
        if (!File.Exists(LobbyBuilder.ScenePath)) LobbyBuilder.CreateScene();
        DemoBuilder.ConfigureBuildSettings();
        EditorSceneManager.OpenScene(LobbyBuilder.ScenePath);
        SessionState.SetBool(Pending, true);
        SessionState.SetBool(Pending + ".Stop", false);
        SessionState.SetBool(Pending + ".Failed", false);
        EditorApplication.isPlaying = true;
    }

    [MenuItem("Palm Bay/Capture visual polish - lobby")]
    public static void RunVisualPolish()
    {
        // The lobby ships a baked cabin; rebuild/save/reload so captures and players
        // actually use the latest procedural art rather than the old scene geometry.
        LobbyBuilder.CreateScene();
        LobbyBuilder.ValidateSavedScene();
        Run();
        SessionState.SetString("M2LobbyCapture.Evidence", "Evidence/visual-polish/after");
        Directory.CreateDirectory(EvidenceRoot);
    }

    static void Poll()
    {
        if (!SessionState.GetBool(Pending, false)) return;
        if (SessionState.GetBool(Pending + ".Stop", false))
        {
            if (EditorApplication.isPlaying) return;
            RestoreSnapshots(null);
            RestoreEventFile();
            SessionState.SetBool(Pending, false);
            if (Application.isBatchMode) EditorApplication.Exit(SessionState.GetBool(Pending + ".Failed", false) ? 1 : 0);
            return;
        }
        if (!EditorApplication.isPlaying) return;

        EditorApplication.QueuePlayerLoopUpdate();
        try
        {
            if (steps == null)
            {
                started = EditorApplication.timeSinceStartup;
                failed = false;
                savedName = null;
                savedColor = 0;
                snapshotsSaved = false;
                Application.logMessageReceived += OnLog;
                steps = Exercise();
            }
            if (EditorApplication.timeSinceStartup - started > 120)
                throw new Exception("M2 lobby capture exceeded 120 seconds");
            if (steps.MoveNext()) return;
            Complete(false, "PASS");
        }
        catch (Exception e)
        {
            Complete(true, e.ToString());
        }
    }

    static void OnLog(string message, string stack, LogType kind)
    {
        if (kind == LogType.Exception || kind == LogType.Error) failed = true;
    }

    static IEnumerator Exercise()
    {
        IEnumerator delay = WaitReal(1.0f);
        while (delay.MoveNext()) yield return delay.Current;
        LobbyApp app = UnityEngine.Object.FindAnyObjectByType<LobbyApp>();
        LobbyCanvas view = UnityEngine.Object.FindAnyObjectByType<LobbyCanvas>();
        if (!app || !view || !app.ViewCamera) throw new Exception("CabinLobby app, view, or camera did not start");
        AppState state = AppState.Ensure();
        savedName = state.Profile.Name;
        savedColor = state.Profile.ColorIndex;
        SaveSnapshots(state);
        view.RefreshNow();
        AssertManualJoinLayout(view, 16f / 9f);
        AssertManualJoinLayout(view, 19.5f / 9f);
        Shot(Path.Combine(EvidenceRoot, "m2-lobby-main.png"), 1600, 900, app.ViewCamera);
        Shot(Path.Combine(EvidenceRoot, "m2-lobby-main-19_5x9.png"), 2340, 1080, app.ViewCamera);

        IEnumerator identity = ExerciseIdentity(app, view, state);
        while (identity.MoveNext()) yield return identity.Current;
        app = UnityEngine.Object.FindAnyObjectByType<LobbyApp>();
        view = UnityEngine.Object.FindAnyObjectByType<LobbyCanvas>();
        if (!app || !view) throw new Exception("Lobby identity flow did not return to the main view");

        app.SaveDisplayName("LobbySmoke");
        app.SetColor(2);
        app.AddBot();
        app.BeginLocalShift();

        IEnumerator wait = WaitForScene(AppState.ScenePalmBay);
        while (wait.MoveNext()) yield return wait.Current;
        AirportGame game = UnityEngine.Object.FindAnyObjectByType<AirportGame>();
        if (!game || !game.Started || game.Sim == null || game.Sandbox || game.Remote || !game.Solo || !game.Crew[1].Bot)
            throw new Exception("Local solo shift did not consume the lobby launch mode");
        if (game.Crew[0].Name != "LobbySmoke" || game.Crew[0].Color != CabinWorld.PlayerColor(2))
            throw new Exception("Wardrobe name or color did not reach the local player");
        game.ExternalControl = true;
        game.BackToLobby();

        wait = WaitForScene(AppState.SceneCabinLobby);
        while (wait.MoveNext()) yield return wait.Current;
        if (state.Profile.Name != "LobbySmoke" || state.Profile.ColorIndex != 2)
            throw new Exception("Wardrobe profile did not survive the scene round-trip");
        delay = WaitReal(.2f);
        while (delay.MoveNext()) yield return delay.Current;
        app = UnityEngine.Object.FindAnyObjectByType<LobbyApp>();
        view = UnityEngine.Object.FindAnyObjectByType<LobbyCanvas>();
        if (!app || !view) throw new Exception("CabinLobby did not rebuild after solo round-trip");
        app.SelectLocalCoop();
        app.BeginLocalShift();

        wait = WaitForScene(AppState.ScenePalmBay);
        while (wait.MoveNext()) yield return wait.Current;
        game = UnityEngine.Object.FindAnyObjectByType<AirportGame>();
        if (!game || !game.Started || game.Sim == null || game.Sandbox || game.Remote || game.Solo || game.Crew[1].Bot)
            throw new Exception("Local coop shift did not consume the lobby launch mode");
        if (game.Crew[0].Name != "LobbySmoke" || game.Crew[0].Color != CabinWorld.PlayerColor(2))
            throw new Exception("Wardrobe appearance was lost in local coop");
        game.ExternalControl = true;
        game.BackToLobby();

        wait = WaitForScene(AppState.SceneCabinLobby);
        while (wait.MoveNext()) yield return wait.Current;
        if (state.Profile.Name != "LobbySmoke" || state.Profile.ColorIndex != 2)
            throw new Exception("Wardrobe profile did not survive the coop round-trip");

        state.Launch.Mode = AppState.GameMode.HostSandbox;
        SceneManager.LoadScene(AppState.ScenePalmBay);
        wait = WaitForScene(AppState.ScenePalmBay);
        while (wait.MoveNext()) yield return wait.Current;
        game = UnityEngine.Object.FindAnyObjectByType<AirportGame>();
        // M3.1 任务练习模式：sandbox 不再是空航班表，航班由循环时刻表生成器驱动（M3 计划 §M3 LAN 对局形态）。
        if (!game || !game.Started || !game.Sandbox || game.Remote || game.Sim == null || !game.Sim.Endless)
            throw new Exception("Host sandbox did not start in endless practice mode");
        game.ExternalControl = true;
        Vector3 before = game.Crew[0].Position;
        game.SetInput(0, new CrewInput { Move = Vector2.right });
        game.Step(.1f);
        if (Vector3.Distance(before, game.Crew[0].Position) < .1f || game.Sim.Finished)
            throw new Exception("Practice movement failed");
        if (game.Sim.Flights.Count == 0)
            throw new Exception("Practice scheduler did not spawn a flight on the first step");
        game.Crew[0].Position = game.Carts[0].Position;
        game.SetInput(0, new CrewInput { Pressed = true });
        game.Step(.05f);
        if (game.Crew[0].Cart != game.Carts[0]) throw new Exception("Practice cart pickup failed");
        game.SetInput(0, new CrewInput { Pressed = true });
        game.Step(.05f);
        if (game.Crew[0].Cart != null) throw new Exception("Practice cart release failed");
        game.Retry();
        if (!game.Sandbox || game.Remote || game.Sim == null || !game.Sim.Endless || game.Sim.Flights.Count != 0)
            throw new Exception("Practice retry changed the run mode");
        game.BackToLobby();

        wait = WaitForScene(AppState.SceneCabinLobby);
        while (wait.MoveNext()) yield return wait.Current;
        state.Launch.Mode = AppState.GameMode.ClientSandbox;
        SceneManager.LoadScene(AppState.ScenePalmBay);
        wait = WaitForScene(AppState.ScenePalmBay);
        while (wait.MoveNext()) yield return wait.Current;
        game = UnityEngine.Object.FindAnyObjectByType<AirportGame>();
        if (!game || !game.Started || !game.Remote || game.Sandbox || game.Shift == null || game.Sim == null ||
            game.Sim.Flights.Count != 0 || !game.Sim.Endless || game.Snaps == null)
            throw new Exception("Remote placeholder did not start with a read-only mirror simulation and snapshot buffer");
        if (game.RemoteSeat < 0 || game.RemoteSeat > 1 || game.Crew[game.RemoteSeat].Visual.gameObject.activeSelf)
            throw new Exception("Remote seat mapping or first-snapshot visibility is invalid");
        game.ExternalControl = true;
        game.SetInput(0, new CrewInput { Move = Vector2.right, Pressed = true });
        game.Step(.1f);
        if (game.SelectedFlight(game.Crew[game.RemoteSeat]) != null || game.ActionLabel(game.Crew[game.RemoteSeat]) == null)
            throw new Exception("Remote mirror-simulation HUD paths were not safe");
        game.Retry();
        if (!game.Remote || game.Sandbox || game.Shift == null || game.Sim == null || game.Sim.Flights.Count != 0)
            throw new Exception("Remote retry became authoritative");
        game.BackToLobby();

        wait = WaitForScene(AppState.SceneCabinLobby);
        while (wait.MoveNext()) yield return wait.Current;
        if (state.Profile.Name != "LobbySmoke" || state.Profile.ColorIndex != 2)
            throw new Exception("Wardrobe archive was not retained after all mode round-trips");

        state.Profile.Name = savedName;
        state.Profile.ColorIndex = savedColor;
        state.SaveProfile();
        report.AppendLine("CabinLobby 16:9 + 19.5:9 capture: PASS");
        report.AppendLine("Local solo and coop launch, wardrobe appearance, and return round-trips: PASS");
        report.AppendLine("Host sandbox practice mode (endless + scheduled first flight), movement, cart pickup/release, and retry: PASS");
        report.AppendLine("Remote mirror-simulation input/HUD/retry placeholder: PASS");
        report.AppendLine("Wardrobe archive retained across all mode round-trips: PASS");
    }

    static IEnumerator ExerciseIdentity(LobbyApp app, LobbyCanvas view, AppState state)
    {
        CaptureAuthService cancelAuth = new CaptureAuthService { Delay = 60f };
        state.UseAuthForTesting(cancelAuth);
        app.CreateRoom();
        if (app.View != LobbyApp.ViewState.SignIn || !app.HasPendingRoomAction)
            throw new Exception("Create room did not preserve a pending action through sign-in");
        view.RefreshNow();
        Shot(Path.Combine(EvidenceRoot, "m2-auth-signin.png"), 1600, 900, app.ViewCamera);
        Shot(Path.Combine(EvidenceRoot, "m2-auth-signin-19_5x9.png"), 2340, 1080, app.ViewCamera);

        app.HandleSignInAction();
        if (!cancelAuth.Busy) throw new Exception("Fake sign-in did not enter the cancellable state");
        app.HandleSignInAction();
        IEnumerator delay = WaitReal(.15f);
        while (delay.MoveNext()) yield return delay.Current;
        if (app.View != LobbyApp.ViewState.Main || cancelAuth.User != null || app.HasPendingRoomAction)
            throw new Exception("Cancelling sign-in did not return to the lobby and clear its pending action");

        CaptureAuthService failedAuth = new CaptureAuthService { Mode = FakeAuthService.FakeMode.NetworkError, Delay = .05f };
        state.UseAuthForTesting(failedAuth);
        app.CreateRoom();
        app.HandleSignInAction();
        delay = WaitReal(.35f);
        while (delay.MoveNext()) yield return delay.Current;
        if (app.View != LobbyApp.ViewState.SignIn || !app.HasPendingRoomAction || string.IsNullOrEmpty(state.AuthNotice))
            throw new Exception("Fake authentication failure did not keep retry available and preserve the create action");

        CaptureAuthService successAuth = new CaptureAuthService { Delay = .08f };
        state.UseAuthForTesting(successAuth);
        app.HandleSignInAction();
        delay = WaitReal(.35f);
        while (delay.MoveNext()) yield return delay.Current;
        if (successAuth.User == null || app.View != LobbyApp.ViewState.AgeCheck || !app.IsFakeAuth || app.AuthBadge != "Fake 测试")
            throw new Exception("Fake login did not display the explicit fake identity and age gate");
        if (view.transform.Find("Cabin Lobby Canvas/Safe Area/Toast").gameObject.activeSelf)
            throw new Exception("Successful login left a stale toast visible over age verification");
        view.RefreshNow();
        Shot(Path.Combine(EvidenceRoot, "m2-auth-dob.png"), 1600, 900, app.ViewCamera);
        Shot(Path.Combine(EvidenceRoot, "m2-auth-dob-19_5x9.png"), 2340, 1080, app.ViewCamera);

        DateTime future = DateTime.Today.AddDays(1);
        if (app.VerifyAge(future.Year, future.Month, future.Day) || app.AgeMessage.IndexOf("晚于", StringComparison.Ordinal) < 0)
            throw new Exception("Future DOB was not rejected by the age check");
        SelectDob(view, DateTime.Today.Year - 30, 2, 31);
        InvokeButton(view, "Cabin Lobby Canvas/Safe Area/Age verification overlay/Age verification panel/Verify age");
        if (app.View != LobbyApp.ViewState.AgeCheck || app.AgeMessage.IndexOf("有效的出生日期", StringComparison.Ordinal) < 0)
            throw new Exception("Invalid calendar DOB was not rejected through the DOB picker");

        DateTime underThirteen = DateTime.Today.AddYears(-12);
        SelectDob(view, underThirteen.Year, underThirteen.Month, underThirteen.Day);
        InvokeButton(view, "Cabin Lobby Canvas/Safe Area/Age verification overlay/Age verification panel/Verify age");
        if (app.View != LobbyApp.ViewState.AgeCheck || app.AgeMessage.IndexOf("未满 13 岁", StringComparison.Ordinal) < 0 || state.CanPlayWithOthers)
            throw new Exception("Under-13 DOB was not blocked from online play");

        DateTime exactThirteen = DateTime.Today.AddYears(-13);
        SelectDob(view, exactThirteen.Year, exactThirteen.Month, exactThirteen.Day);
        InvokeButton(view, "Cabin Lobby Canvas/Safe Area/Age verification overlay/Age verification panel/Verify age");
        if (!state.CanPlayWithOthers || app.HasPendingRoomAction || state.Room.Phase != RoomPhase.Listening)
            throw new Exception("Exact-13 DOB did not resume the pending room creation");
        if (state.Room.Seats[0].Name != state.Profile.Name)
            throw new Exception("Host room name did not use the saved wardrobe name");

        app.SignOut();
        delay = WaitReal(.15f);
        while (delay.MoveNext()) yield return delay.Current;
        if (state.Auth.User != null || state.CanPlayWithOthers || state.Room.Phase != RoomPhase.Idle || state.Beacon != null && state.Beacon.Announcing)
            throw new Exception("Logout did not clear identity/age and safely leave the active room");

        const string joinTarget = "127.0.0.1";
        app.JoinAddress(joinTarget);
        if (app.View != LobbyApp.ViewState.SignIn || app.PendingJoinAddress != joinTarget)
            throw new Exception("Manual join address was not retained while login was required");
        app.HandleSignInAction();
        delay = WaitReal(.35f);
        while (delay.MoveNext()) yield return delay.Current;
        if (successAuth.User == null || app.View != LobbyApp.ViewState.AgeCheck || app.PendingJoinAddress != joinTarget)
            throw new Exception("Join target did not survive login and age verification");
        SelectDob(view, exactThirteen.Year, exactThirteen.Month, exactThirteen.Day);
        InvokeButton(view, "Cabin Lobby Canvas/Safe Area/Age verification overlay/Age verification panel/Verify age");
        if (state.Room.Phase != RoomPhase.Joining || app.JoinTargetAddress != joinTarget)
            throw new Exception("Pending manual join did not resume with its original IP");
        view.RefreshNow();
        Shot(Path.Combine(EvidenceRoot, "m2-lobby-connecting.png"), 1600, 900, app.ViewCamera);
        Shot(Path.Combine(EvidenceRoot, "m2-lobby-connecting-19_5x9.png"), 2340, 1080, app.ViewCamera);
        report.AppendLine("Fake sign-in cancel, failure/retry, login marker, and logout cleanup: PASS");
        report.AppendLine("Invalid/future/under-13/exact-13 DOB and pending create/join resumption: PASS");
        report.AppendLine("Pending manual IP retained through login and age verification: " + joinTarget);

        app.LeaveRoom();
        delay = WaitReal(1.15f);
        while (delay.MoveNext()) yield return delay.Current;
        if (state.Room.Phase != RoomPhase.Idle) throw new Exception("The connecting join fixture did not leave cleanly");

        RoomInfo open = new RoomInfo { Name = "Palm Bay 客舱练习", HostName = "海风", Address = "127.0.0.1", State = "lobby", Seats = 2, Taken = 1 };
        RoomInfo playing = new RoomInfo { Name = "已起飞的班岗", HostName = "云朵", Address = "127.0.0.1", State = "playing", Seats = 2, Taken = 1 };
        app.DebugInjectRooms(open, playing);
        view.RefreshNow();
        Shot(Path.Combine(EvidenceRoot, "m2-lobby-roomlist.png"), 1600, 900, app.ViewCamera);
        Shot(Path.Combine(EvidenceRoot, "m2-lobby-roomlist-19_5x9.png"), 2340, 1080, app.ViewCamera);
        Button openJoin = FindButton(view, "Cabin Lobby Canvas/Safe Area/Partner panel/Network room discovery/Room row 0/Join");
        Button playingJoin = FindButton(view, "Cabin Lobby Canvas/Safe Area/Partner panel/Network room discovery/Room row 1/Join");
        if (!openJoin.interactable || playingJoin.interactable || playingJoin.GetComponentInChildren<Text>().text != "已开局")
            throw new Exception("Lobby discovery did not enable open rooms and block rooms already in play");

        RoomInfo full = new RoomInfo { Name = "已满员的班岗", HostName = "海风", Address = "127.0.0.1", State = "lobby", Seats = 2, Taken = 2 };
        app.DebugInjectRooms(full);
        view.RefreshNow();
        Shot(Path.Combine(EvidenceRoot, "m2-lobby-room-full.png"), 1600, 900, app.ViewCamera);
        Button fullJoin = FindButton(view, "Cabin Lobby Canvas/Safe Area/Partner panel/Network room discovery/Room row 0/Join");
        if (fullJoin.interactable || fullJoin.GetComponentInChildren<Text>().text != "已满")
            throw new Exception("Full room row was not disabled with the full label");
        report.AppendLine("Room list lobby/ingame/full states and manual-IP entry: PASS");

        app.DebugEnterRoomFixture("双人自由练习示例", "搭档示例");
        view.RefreshNow();
        Shot(Path.Combine(EvidenceRoot, "m2-lobby-host-fixture.png"), 1600, 900, app.ViewCamera);
        Shot(Path.Combine(EvidenceRoot, "m2-lobby-host-fixture-19_5x9.png"), 2340, 1080, app.ViewCamera);
        app.DebugEnterClientRoomFixture("好友的房间");
        view.RefreshNow();
        Button startButton = FindButton(view, "Cabin Lobby Canvas/Safe Area/Footer/Start shift");
        if (app.CanBeginShift || startButton.interactable || app.StartButtonText != "等待房主开始")
            throw new Exception("Client fixture exposed an enabled start action");
        Shot(Path.Combine(EvidenceRoot, "m2-lobby-client-fixture.png"), 1600, 900, app.ViewCamera);
        Shot(Path.Combine(EvidenceRoot, "m2-lobby-client-fixture-19_5x9.png"), 2340, 1080, app.ViewCamera);
        app.DebugExitRoomFixture();
        app.DebugClearRooms();
        report.AppendLine("Host/client room display fixtures are tagged and client start remains disabled: PASS");

        int eventsBeforeInvite = state.Events.PendingCount;
        app.OpenInvitationPanel();
        if (state.Events.PendingCount != eventsBeforeInvite + 1)
            throw new Exception("Opening the invitation panel did not record exactly one share-panel event");
        InvokeButton(view, "Cabin Lobby Canvas/Safe Area/Invitation overlay/Invitation panel/Fuel role");
        view.RefreshNow();
        if (app.InviteMessage.IndexOf("燃油车", StringComparison.Ordinal) < 0 || app.InviteMessage.Contains("https://palmbay.app"))
            throw new Exception("Fuel invitation preview did not reflect the selected role");
        int eventsBeforeShare = state.Events.PendingCount;
        InvokeButton(view, "Cabin Lobby Canvas/Safe Area/Invitation overlay/Invitation panel/Share invitation");
        if (!GUIUtility.systemCopyBuffer.Contains("燃油车") || !GUIUtility.systemCopyBuffer.Contains("同一个 Wi-Fi") || app.ShareResultMessage.IndexOf("复制", StringComparison.Ordinal) < 0)
            throw new Exception("Invite action did not report an honest clipboard result");
        if (state.Events.PendingCount != eventsBeforeShare)
            throw new Exception("Sharing the invitation recorded an extra panel-open event");
        view.RefreshNow();
        var preview = view.transform.Find("Cabin Lobby Canvas/Safe Area/Invitation overlay/Invitation panel/Invitation preview panel/Invitation preview").GetComponent<Text>();
        Canvas.ForceUpdateCanvases();
        if (preview.preferredHeight > preview.rectTransform.rect.height + 1f)
            throw new Exception("Invitation instructions are clipped in the preview");
        Shot(Path.Combine(EvidenceRoot, "m2-lobby-invite.png"), 1600, 900, app.ViewCamera);
        Shot(Path.Combine(EvidenceRoot, "m2-lobby-invite-19_5x9.png"), 2340, 1080, app.ViewCamera);
        app.CloseInvitationPanel();
        app.OpenInvitationPanel();
        if (!string.IsNullOrEmpty(app.ShareResultMessage))
            throw new Exception("Reopening the invitation panel retained a stale prior share result");
        app.CloseInvitationPanel();
        app.SignOut();
        report.AppendLine("Invitation roles, link-free LAN setup instructions, preview bounds, clipboard result, and panel-open event: PASS");
    }

    static void SelectDob(LobbyCanvas view, int year, int month, int day)
    {
        SetDropdownNumber(view, "Cabin Lobby Canvas/Safe Area/Age verification overlay/Age verification panel/DOB picker/Year", year);
        SetDropdownNumber(view, "Cabin Lobby Canvas/Safe Area/Age verification overlay/Age verification panel/DOB picker/Month", month);
        SetDropdownNumber(view, "Cabin Lobby Canvas/Safe Area/Age verification overlay/Age verification panel/DOB picker/Day", day);
    }

    static void SetDropdownNumber(LobbyCanvas view, string path, int value)
    {
        Transform target = view.transform.Find(path);
        Dropdown dropdown = target ? target.GetComponent<Dropdown>() : null;
        if (!dropdown) throw new Exception("DOB picker is missing: " + path);
        for (int i = 0; i < dropdown.options.Count; i++)
        {
            if (ReadDigits(dropdown.options[i].text) == value)
            {
                dropdown.value = i;
                dropdown.RefreshShownValue();
                return;
            }
        }
        throw new Exception("DOB picker does not contain " + value + " at " + path);
    }

    static int ReadDigits(string text)
    {
        int value = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char character = text[i];
            if (character >= '0' && character <= '9') value = value * 10 + character - '0';
        }
        return value;
    }

    static Button FindButton(LobbyCanvas view, string path)
    {
        Transform target = view.transform.Find(path);
        Button button = target ? target.GetComponent<Button>() : null;
        if (!button) throw new Exception("UI button is missing: " + path);
        return button;
    }

    static void InvokeButton(LobbyCanvas view, string path)
    {
        FindButton(view, path).onClick.Invoke();
    }

    static IEnumerator WaitForScene(string sceneName)
    {
        for (int i = 0; i < 1200; i++)
        {
            if (SceneManager.GetActiveScene().name == sceneName)
            {
                if (sceneName != AppState.ScenePalmBay || UnityEngine.Object.FindAnyObjectByType<AirportGame>()?.Started == true)
                    yield break;
            }
            yield return null;
        }
        throw new Exception("Scene did not become ready: " + sceneName);
    }

    static IEnumerator WaitReal(float seconds)
    {
        double end = EditorApplication.timeSinceStartup + seconds;
        while (EditorApplication.timeSinceStartup < end) yield return null;
    }

    static void Shot(string path, int width, int height, Camera camera)
    {
        if (!camera) throw new Exception("Cannot capture without the CabinLobby camera");
        Canvas.ForceUpdateCanvases();
        var oldTarget = camera.targetTexture;
        float oldAspect = camera.aspect;
        var oldActive = RenderTexture.active;
        var rt = new RenderTexture(width, height, 24);
        rt.antiAliasing = Mathf.Max(1, QualitySettings.antiAliasing);
        camera.targetTexture = rt;
        camera.aspect = width / (float)height;
        Canvas.ForceUpdateCanvases();
        camera.Render();
        RenderTexture.active = rt;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        image.Apply();
        Directory.CreateDirectory(EvidenceRoot);
        File.WriteAllBytes(path, image.EncodeToPNG());
        RenderTexture.active = oldActive;
        camera.targetTexture = oldTarget;
        camera.aspect = oldAspect;
        UnityEngine.Object.DestroyImmediate(image);
        UnityEngine.Object.DestroyImmediate(rt);
        report.AppendLine(path + "  " + width + "x" + height);
    }

    static void AssertManualJoinLayout(LobbyCanvas view, float aspect)
    {
        Transform canvasTransform = view.transform.Find("Cabin Lobby Canvas");
        RectTransform safeArea = (RectTransform)view.transform.Find("Cabin Lobby Canvas/Safe Area");
        RectTransform canvasRect = (RectTransform)canvasTransform;
        RectTransform footer = (RectTransform)safeArea.Find("Footer");
        RectTransform manualIp = (RectTransform)safeArea.Find("Partner panel/Network room discovery/Manual IP");
        RectTransform manualJoin = (RectTransform)safeArea.Find("Partner panel/Network room discovery/Join manual IP");
        if (!canvasTransform || !safeArea || !footer || !manualIp || !manualJoin)
            throw new Exception("Manual join layout assertion is missing a required RectTransform");

        Vector2 originalSizeDelta = safeArea.sizeDelta;
        Vector2 anchorSpan = safeArea.anchorMax - safeArea.anchorMin;
        Vector2 desiredSafeSize = new Vector2(aspect * 1080f, 1080f);
        safeArea.sizeDelta = desiredSafeSize - Vector2.Scale(canvasRect.rect.size, anchorSpan);
        Canvas.ForceUpdateCanvases();
        try
        {
            Rect safeBounds = safeArea.rect;
            Rect footerBounds = RectInLocalSpace(footer, safeArea);
            Rect ipBounds = RectInLocalSpace(manualIp, safeArea);
            Rect joinBounds = RectInLocalSpace(manualJoin, safeArea);
            if (!Contains(safeBounds, ipBounds) || !Contains(safeBounds, joinBounds))
                throw new Exception("Manual join controls extend outside the safe area at aspect " + aspect);
            if (footerBounds.Overlaps(ipBounds) || footerBounds.Overlaps(joinBounds))
                throw new Exception("Manual join controls overlap the footer at aspect " + aspect);
            report.AppendLine("Manual IP + join remain inside safe area and above footer at aspect " + aspect.ToString("0.000") + ": PASS");
        }
        finally
        {
            safeArea.sizeDelta = originalSizeDelta;
            Canvas.ForceUpdateCanvases();
        }
    }

    static Rect RectInLocalSpace(RectTransform rectTransform, RectTransform relativeTo)
    {
        var corners = new Vector3[4];
        rectTransform.GetWorldCorners(corners);
        Vector3 lowerLeft = relativeTo.InverseTransformPoint(corners[0]);
        Vector3 upperRight = relativeTo.InverseTransformPoint(corners[2]);
        return Rect.MinMaxRect(lowerLeft.x, lowerLeft.y, upperRight.x, upperRight.y);
    }

    static bool Contains(Rect outer, Rect inner)
    {
        const float tolerance = 0.5f;
        return inner.xMin >= outer.xMin - tolerance && inner.yMin >= outer.yMin - tolerance &&
               inner.xMax <= outer.xMax + tolerance && inner.yMax <= outer.yMax + tolerance;
    }

    static void SaveSnapshots(AppState state)
    {
        if (snapshotsSaved) return;
        savedProfileKeyPresent = PlayerPrefs.HasKey("palmbay.profile");
        savedAgeKeyPresent = PlayerPrefs.HasKey("palmbay.auth.age-status");
        savedRefreshKeyPresent = PlayerPrefs.HasKey("palmbay.auth.refresh");
        savedProfileKey = PlayerPrefs.GetString("palmbay.profile", string.Empty);
        savedAgeKey = PlayerPrefs.GetString("palmbay.auth.age-status", string.Empty);
        savedRefreshKey = PlayerPrefs.GetString("palmbay.auth.refresh", string.Empty);
        savedClipboard = GUIUtility.systemCopyBuffer;
        savedEventsPath = Path.Combine(Application.persistentDataPath, "palmbay-events.jsonl");
        savedEventsPresent = File.Exists(savedEventsPath);
        savedEventsContents = savedEventsPresent ? File.ReadAllText(savedEventsPath) : string.Empty;
        savedEventsBackupPath = Path.Combine(Path.GetTempPath(), "palmbay-m2-lobby-" + Guid.NewGuid().ToString("N") + ".bak");
        if (savedEventsPresent) File.Copy(savedEventsPath, savedEventsBackupPath, true);
        SessionState.SetString(Pending + ".EventsPath", savedEventsPath);
        SessionState.SetString(Pending + ".EventsBackupPath", savedEventsBackupPath);
        SessionState.SetBool(Pending + ".EventsPresent", savedEventsPresent);
        if (state != null)
        {
            savedName = state.Profile.Name;
            savedColor = state.Profile.ColorIndex;
        }
        snapshotsSaved = true;
    }

    static void RestoreSnapshots(AppState state)
    {
        if (!snapshotsSaved) return;
        if (state != null && savedName != null)
        {
            state.Profile.Name = savedName;
            state.Profile.ColorIndex = savedColor;
            state.SaveProfile();
        }
        RestorePreference("palmbay.profile", savedProfileKeyPresent, savedProfileKey);
        RestorePreference("palmbay.auth.age-status", savedAgeKeyPresent, savedAgeKey);
        RestorePreference("palmbay.auth.refresh", savedRefreshKeyPresent, savedRefreshKey);
        PlayerPrefs.Save();
        try
        {
            GUIUtility.systemCopyBuffer = savedClipboard ?? string.Empty;
        }
        catch (Exception)
        {
        }
    }

    static void RestorePreference(string key, bool existed, string value)
    {
        if (existed) PlayerPrefs.SetString(key, value ?? string.Empty);
        else PlayerPrefs.DeleteKey(key);
    }

    static void RestoreEventFile()
    {
        string targetPath = SessionState.GetString(Pending + ".EventsPath", savedEventsPath ?? string.Empty);
        string backupPath = SessionState.GetString(Pending + ".EventsBackupPath", savedEventsBackupPath ?? string.Empty);
        bool originalPresent = SessionState.GetBool(Pending + ".EventsPresent", savedEventsPresent);
        if (string.IsNullOrEmpty(targetPath)) return;
        try
        {
            if (originalPresent)
            {
                string directory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                if (File.Exists(backupPath)) File.Copy(backupPath, targetPath, true);
                else File.WriteAllText(targetPath, savedEventsContents ?? string.Empty);
            }
            else if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
        }
        catch (Exception e)
        {
            SessionState.SetBool(Pending + ".Failed", true);
            Debug.LogError("M2 lobby capture could not restore the events file: " + e.Message);
        }
        finally
        {
            if (File.Exists(backupPath)) File.Delete(backupPath);
            SessionState.SetString(Pending + ".EventsPath", string.Empty);
            SessionState.SetString(Pending + ".EventsBackupPath", string.Empty);
            SessionState.SetBool(Pending + ".EventsPresent", false);
        }
    }

    static void Complete(bool error, string message)
    {
        RestoreSnapshots(AppState.Instance);
        Application.logMessageReceived -= OnLog;
        error |= failed;
        Directory.CreateDirectory(EvidenceRoot);
        File.WriteAllText(Path.Combine(EvidenceRoot, "m2-lobby-review.txt"), "M2 CabinLobby smoke: " + (error ? "FAIL" : message) + "\nRuntime errors: " + failed + "\n" + report);
        Debug.Log((error ? "M2_LOBBY_CAPTURE_FAILED " : "M2_LOBBY_CAPTURE_PASSED ") + message);
        SessionState.SetBool(Pending + ".Failed", error);
        SessionState.SetBool(Pending + ".Stop", true);
        steps = null;
        EditorApplication.isPlaying = false;
    }
}
