using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using IslandAirport;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Identity renewal/revocation regression through the actual lobby and scene flow.
/// Uses explicit FakeAuth and in-memory room transport; never opens OAuth or calls cloud services.</summary>
[InitializeOnLoad]
public static class M7IdentityReview
{
    const string Pending = "PalmBay.M7IdentityReview";
    const string AgeKey = "palmbay.auth.age-status";
    static readonly StringBuilder report = new StringBuilder();
    static IEnumerator steps;
    static AppState state;
    static CooperationMemoryFixtureScope memory;
    static bool errors;
    static double started;
    static string savedAge;
    static bool agePresent;
    static string originalName;
    static int originalColor;

    sealed class FixtureAuth : IAuthService
    {
        readonly Queue<string> events = new Queue<string>();
        readonly string uid = "m7-identity-" + Guid.NewGuid().ToString("N");
        public AuthUser User { get; private set; }
        public bool Busy { get { return false; } }
        public void SignInGoogle()
        {
            User = new AuthUser { Uid = uid, IdToken = "fixture-token", RefreshToken = "fixture-refresh" };
            events.Enqueue("ok:" + uid);
        }
        public void SignOut() { User = null; events.Enqueue("out"); }
        public void Restore(string token) { throw new InvalidOperationException("No credential restoration in this fixture"); }
        public void Pump(float dt) { }
        public bool TryDequeue(out string result)
        { result = events.Count == 0 ? null : events.Dequeue(); return result != null; }
        public void Refresh() { User.IdToken = "renewed-fixture-token"; events.Enqueue("refreshed:" + uid); }
        public void Fail(bool revoke) { if (revoke) User = null; events.Enqueue("fail:token"); }
        public void DropWithoutEvent() { User = null; }
    }

    static M7IdentityReview() { EditorApplication.update += Poll; }
    [MenuItem("Palm Bay/Review M7 identity recovery")]
    public static void Run()
    {
        // A configured real editor session could restore a token in Awake. Refuse that setup
        // before entering play mode, matching the existing Fake-only acceptance contract.
        using (var auth = new FirebaseRestAuth())
            Require(!auth.IsConfigured, "M7 fixture requires an unconfigured desktop OAuth editor (FakeAuth)");
        savedAge = PlayerPrefs.GetString(AgeKey, string.Empty); agePresent = PlayerPrefs.HasKey(AgeKey);
        SessionState.SetString(Pending + ".Age", savedAge); SessionState.SetBool(Pending + ".AgePresent", agePresent);
        report.Length = 0; steps = null; errors = false;
        DemoBuilder.ConfigureBuildSettings();
        EditorSceneManager.OpenScene(LobbyBuilder.ScenePath);
        SessionState.SetBool(Pending, true); SessionState.SetBool(Pending + ".Stop", false);
        EditorApplication.isPlaying = true;
    }

    static void Poll()
    {
        if (!SessionState.GetBool(Pending, false)) return;
        if (SessionState.GetBool(Pending + ".Stop", false))
        {
            if (EditorApplication.isPlaying) return;
            // SessionState survives editor domain reloads between edit and play mode.
            if (SessionState.GetBool(Pending + ".AgePresent", false))
                PlayerPrefs.SetString(AgeKey, SessionState.GetString(Pending + ".Age", string.Empty));
            else PlayerPrefs.DeleteKey(AgeKey);
            PlayerPrefs.Save();
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
                if (LobbyApp.Instance == null || GetField<RectTransform>(LobbyApp.Instance.GetComponent<LobbyCanvas>(), "networkRoomRoot") == null) return;
                state = AppState.Ensure(); Require(state.IsFakeAuth, "Acceptance must never use real auth");
                originalName = state.Profile.Name; originalColor = state.Profile.ColorIndex;
                state.ExternalControl = true;
                SetField(state, "<Events>k__BackingField", new GameEventQueue());
                // Disable discovery output. All tested session traffic uses an in-memory hub.
                if (state.Beacon != null) state.Beacon.Stop();
                SetField(state, "<Beacon>k__BackingField", null);
                SetField(state, "<Listener>k__BackingField", null);
                memory = new CooperationMemoryFixtureScope(state);
                started = EditorApplication.timeSinceStartup;
                Application.logMessageReceived += OnLog;
                steps = Exercise();
            }
            Require(EditorApplication.timeSinceStartup - started < 90, "M7 identity review timeout");
            if (steps.MoveNext()) return;
            Complete(false, "PASS");
        }
        catch (Exception exception) { Complete(true, exception.ToString()); }
    }

    static IEnumerator Exercise()
    {
        LobbyApp app = LobbyApp.Instance;
        var hub = new LoopbackHub(7);
        state.Room.SocketFactory = port => hub.CreateSocket("fixture-host", port);
        var auth = new FixtureAuth(); state.UseAuthForTesting(auth);
        Login(app, auth);
        app.CreateRoom(); FillPartner();
        Require(app.View == LobbyApp.ViewState.InRoom && app.CanBeginShift, "Authenticated ready room required");
        auth.Refresh(); PumpLobby(app);
        Require(app.View == LobbyApp.ViewState.InRoom && app.CanBeginShift &&
            GetField<RectTransform>(app.GetComponent<LobbyCanvas>(), "networkRoomRoot").gameObject.activeSelf,
            "Silent refresh must retain actual room panel and start availability");
        report.AppendLine("Successful silent token refresh retains room panel and ready state: PASS");

        auth.Fail(false); PumpLobby(app);
        Require(state.Room.Phase == RoomPhase.Listening && app.NetworkRoomActive &&
            app.View == LobbyApp.ViewState.InRoom && app.CanBeginShift &&
            GetField<RectTransform>(app.GetComponent<LobbyCanvas>(), "networkRoomRoot").gameObject.activeSelf,
            "A failed sign-in with an existing valid identity must preserve room panel and View");
        auth.Fail(true); PumpLobby(app);
        Require(state.Room.Phase == RoomPhase.Idle && !app.NetworkRoomActive && !app.CanBeginShift &&
            app.View == LobbyApp.ViewState.SignIn, "Revoked identity must leave room and expose login retry");
        report.AppendLine("Revocation closes lobby room; existing valid identity is preserved on unrelated failure: PASS");

        Login(app, auth); app.CreateRoom(); FillPartner();
        auth.DropWithoutEvent();
        Require(!app.CanBeginShift, "Invalid identity disables start even before event delivery");
        app.BeginShift();
        Require(state.Room.Phase == RoomPhase.Idle && SceneManager.GetActiveScene().name == AppState.SceneCabinLobby &&
            app.View == LobbyApp.ViewState.SignIn, "Direct start callback must reject invalid identity");
        report.AppendLine("Stale UI/direct start callback cannot start a revoked account: PASS");

        Login(app, auth);
        var remote = new RoomManager { SocketFactory = port => hub.CreateSocket("fixture-remote", port) };
        Require(remote.Host("Remote fixture", "Other player", "fixture-token"), "In-memory remote room required");
        app.JoinAddress("fixture-remote");
        for (int i = 0; i < 10 && state.Room.Phase != RoomPhase.InRoom; i++)
        { hub.Pump(.05f); remote.Pump(.05f); hub.Pump(.05f); PumpLobby(app); }
        Require(state.Room.Phase == RoomPhase.InRoom && !state.Room.IsHost, "Client fixture joined through protocol");
        auth.Refresh(); PumpLobby(app);
        Require(app.View == LobbyApp.ViewState.InRoom, "Client silent refresh retains waiting room");
        auth.Fail(true); PumpLobby(app); state.PumpNet(1.1f);
        Require(state.Room.Phase == RoomPhase.Idle && app.View == LobbyApp.ViewState.SignIn,
            "Client revocation sends leave and releases room for another party");
        remote.Leave(); remote.Pump(1.1f);
        report.AppendLine("Client refresh retains waiting room; revoked client leaves and can regroup: PASS");

        Login(app, auth); app.CreateRoom(); app.SignOut(); PumpLobby(app);
        Require(state.Room.Phase == RoomPhase.Idle && auth.User == null, "Explicit signout closes room");
        app.AddBot(); Require(app.CanBeginShift, "Offline bot remains available without auth");
        report.AppendLine("Explicit logout leaves room; guest + bot remains available: PASS");

        Login(app, auth); app.CreateRoom(); FillPartner(); app.BeginShift();
        IEnumerator wait = WaitForScene(AppState.ScenePalmBay);
        while (wait.MoveNext()) yield return wait.Current;
        var game = UnityEngine.Object.FindAnyObjectByType<AirportGame>();
        Require(game != null && game.Started && game.NetworkShift, "Network shift scene must start");
        game.ExternalControl = true;
        // Seed a real authority checkpoint, then exercise RoomManager's production promotion
        // using an explicit seat-1 mirror fixture. No editor method emulates retry itself.
        state.Profile.Name = "M7SeatOne"; state.Profile.ColorIndex = 2;
        game.Step(.5f);
        Require(state.Room.LastAuthoritativeSnapshot != null, "Actual checkpoint required for promotion");
        state.Room.Seats[0].Name = "Departed host";
        state.Room.Seats[1].Name = state.Profile.Name;
        SetField(state.Room, "<IsHost>k__BackingField", false);
        SetField(state.Room, "<LocalSeat>k__BackingField", 1);
        Require((bool)typeof(RoomManager).GetMethod("TryPromote", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(state.Room, null),
            "Checkpoint promotion must succeed");
        game.Step(.05f);
        Require(game.LocalSeat == 1 && game.Crew[0].Bot && !game.Crew[1].Bot, "Promotion keeps local seat 1 and abandoned seat bot");
        for (int i = 0; i < 1201 && !game.MatchFinished; i++)
        { game.Step(.25f); if (i % 48 == 0) yield return null; }
        Require(game.MatchFinished && state.Room.LastAuthoritativeSnapshot.Ended, "Completed checkpoint required for real retry");
        int previousRound = state.Room.RoundId;
        game.Retry(); game.Step(.05f);
        Require(state.Room.RoundId == previousRound + 1 && !game.MatchFinished && game.LocalSeat == 1 &&
            game.Crew[1].Name == state.Profile.Name && game.Crew[1].Color == CabinWorld.PlayerColor(state.Profile.ColorIndex) &&
            game.Crew[0].Name != state.Profile.Name && game.Crew[0].Color != CabinWorld.PlayerColor(state.Profile.ColorIndex) &&
            game.Crew[0].Bot && !game.Crew[1].Bot,
            "Migrated authority retry must retain seat-1 profile name/color and bot seat 0");
        report.AppendLine("Production checkpoint promotion + completed-shift retry preserves seat-1 name/color and bot mapping: PASS");

        auth.Fail(true); state.PumpNet(.05f); game.Step(.05f);
        report.AppendLine("Revocation dispatch: invalidated=" + state.NetworkAuthInvalidated + ", room=" + state.Room.Phase + ", gameStarted=" + game.Started + ", scene=" + SceneManager.GetActiveScene().name);
        Require(state.NetworkAuthInvalidated && auth.User == null, "Global auth loss must invalidate the network session");
        Require(!game.Started, "NetSession must consume invalidation and request lobby navigation");
        wait = WaitForScene(AppState.SceneCabinLobby);
        while (wait.MoveNext()) yield return wait.Current;
        Require(state.Room.Phase == RoomPhase.Idle && !LobbyApp.Instance.NetworkRoomActive && auth.User == null,
            "Mid-shift revocation must return to a clean lobby: room=" + state.Room.Phase + ", authUser=" + (auth.User != null));
        report.AppendLine("Global mid-shift auth revocation returns to lobby without real credentials: PASS");
        report.AppendLine("FIXTURE: explicit FakeAuth, in-memory room socket, no backend calls or share posting.");
    }

    static void FillPartner()
    {
        // Roster-only fixture: no remote endpoint, so close is synchronous and deterministic.
        state.Room.Seats[1].Name = "Fixture partner";
        state.Room.Seats[1].Occupied = true; state.Room.Seats[1].Bot = false;
        state.Room.Seats[1].CreditEligible = true;
    }
    static void Login(LobbyApp app, FixtureAuth auth)
    { state.SignInGoogle(); PumpLobby(app); Require(app.VerifyAge(2000, 1, 1), "Fixture adult age verification"); }
    static void PumpLobby(LobbyApp app)
    { state.PumpNet(.05f); typeof(LobbyApp).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(app, null); }
    static T GetField<T>(object owner, string name) where T : class
    { return owner == null ? null : (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner); }
    static void SetField(object owner, string name, object value)
    { owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value); }
    static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    static IEnumerator WaitForScene(string sceneName)
    {
        double deadline = EditorApplication.timeSinceStartup + 10;
        while (true)
        {
            bool ready = SceneManager.GetActiveScene().name == sceneName;
            if (sceneName == AppState.SceneCabinLobby)
                ready &= LobbyApp.Instance != null && GetField<RectTransform>(LobbyApp.Instance.GetComponent<LobbyCanvas>(), "networkRoomRoot") != null;
            else
            {
                var game = UnityEngine.Object.FindAnyObjectByType<AirportGame>();
                ready &= game != null && game.Started;
            }
            if (ready) yield break;
            Require(EditorApplication.timeSinceStartup < deadline,
                "Scene initialization timeout: target=" + sceneName + ", actual=" + SceneManager.GetActiveScene().name +
                ", lobby=" + (LobbyApp.Instance != null) + ", room=" + state.Room.Phase);
            yield return null;
        }
    }
    static void OnLog(string message, string trace, LogType kind)
    { if (kind == LogType.Error || kind == LogType.Exception) errors = true; }
    static void Complete(bool failed, string reason)
    {
        Application.logMessageReceived -= OnLog;
        if (state != null) { state.Profile.Name = originalName; state.Profile.ColorIndex = originalColor; }
        if (memory != null) { memory.Dispose(); memory = null; }
        failed |= errors;
        Directory.CreateDirectory("Evidence");
        File.WriteAllText("Evidence/m7-identity-review.txt", "M7 identity review: " + (failed ? "FAIL" : "PASS") + "\n" + reason + "\nRuntime errors: " + errors + "\n" + report);
        SessionState.SetBool(Pending + ".Failed", failed); SessionState.SetBool(Pending + ".Stop", true);
        steps = null; EditorApplication.isPlaying = false;
    }
}
