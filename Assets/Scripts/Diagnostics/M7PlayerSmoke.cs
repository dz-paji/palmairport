using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IslandAirport
{
    /// <summary>Opt-in, isolated macOS player acceptance. Never runs in ordinary play.</summary>
    public sealed class M7PlayerSmoke : MonoBehaviour
    {
        [Serializable] public sealed class Isolation
        {
            public string protocol, nonce, company, product, bundle, home, evidence, mode;
        }
        [Serializable] sealed class Result
        {
            public string status, reason, company, product, bundle, persistentDataPath, evidence, isolationMode;
            public string scope = "Local offline guest + bot; accelerated synthetic input; no real human, OAuth, cloud, device or multiplayer acceptance";
            public int runtimeErrors, assertions;
            public float simulatedSeconds, wallSeconds;
        }
        static Isolation isolation;
        readonly StringBuilder report = new StringBuilder();
        readonly List<string> errors = new List<string>();
        IEnumerator exercise;
        AirportGame game;
        AppState state;
        float started;
        float simulatedSeconds;
        int assertions;
        bool complete;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            string[] args = Environment.GetCommandLineArgs();
            bool commandLine = Array.IndexOf(args, "-palmbayM7Smoke") >= 0;
            if (Application.isEditor)
            {
                if (commandLine) Debug.LogError("M7 player smoke requires a standalone player.");
                return;
            }
            try
            {
                string evidence;
                if (commandLine)
                {
                    string marker = Argument(args, "-palmbayM7Isolation");
                    evidence = Argument(args, "-palmbayM7Evidence");
                    if (!Path.IsPathRooted(marker) || !Path.IsPathRooted(evidence)) throw new Exception("Absolute isolation and evidence paths required.");
                    isolation = JsonUtility.FromJson<Isolation>(File.ReadAllText(marker));
                    if (isolation != null) isolation.mode = "cli";
                }
                else
                {
                    // This temporary resource exists only inside BuildMacUiSmoke's
                    // dedicated GUID player. Ordinary product builds have no resource.
                    var config = Resources.Load<TextAsset>("palmbay-m7-ui-smoke");
                    if (config == null) return;
                    isolation = JsonUtility.FromJson<Isolation>(config.text);
                    if (isolation == null || isolation.mode != "ui-launch") throw new Exception("Invalid embedded UI smoke configuration.");
                    evidence = isolation.evidence;
                }
                if (isolation == null || isolation.protocol != "PALMBAY_M7_PLAYER_SMOKE_V1") throw new Exception("Invalid isolation marker.");
                Guid nonce;
                if (!Guid.TryParseExact(isolation.nonce, "N", out nonce)) throw new Exception("Invalid isolation nonce.");
                if (isolation.company != "Palm Bay M7 Smoke " + isolation.nonce || isolation.product != "M7Smoke-" + isolation.nonce ||
                    isolation.bundle != "com.palmbay.m7smoke." + isolation.nonce) throw new Exception("Dedicated smoke build identity required.");
                if (Application.companyName != isolation.company || Application.productName != isolation.product || Application.identifier != isolation.bundle)
                    throw new Exception("Player identity does not match the dedicated smoke build.");
                if (Application.platform != RuntimePlatform.OSXPlayer) throw new Exception("This smoke entry is for macOS players.");
                if (string.IsNullOrEmpty(evidence) || !Path.IsPathRooted(evidence) || Path.GetFullPath(evidence) != Path.GetFullPath(isolation.evidence) || !Directory.Exists(evidence))
                    throw new Exception("Evidence directory does not match isolation marker.");
                if (isolation.mode == "cli")
                {
                    if (!Directory.Exists(isolation.home) || Environment.GetEnvironmentVariable("HOME") != isolation.home ||
                        Environment.GetEnvironmentVariable("CFFIXED_USER_HOME") != isolation.home) throw new Exception("Independent HOME and CFFIXED_USER_HOME required.");
                }
                else
                {
                    if (!string.IsNullOrEmpty(isolation.home)) throw new Exception("UI launch uses a dedicated identity, not a HOME override.");
                    if (Directory.GetFileSystemEntries(evidence).Length != 0) throw new Exception("UI smoke evidence directory must be empty; preserve old evidence and rebuild to a new path.");
                }
                string data = Path.GetFullPath(Application.persistentDataPath).TrimEnd(Path.DirectorySeparatorChar);
                bool companyProductPath = Path.GetFileName(data) == isolation.product && Path.GetFileName(Path.GetDirectoryName(data)) == isolation.company;
                bool bundlePath = Path.GetFileName(data) == isolation.bundle && Path.GetFileName(Path.GetDirectoryName(data)) == "Application Support";
                if (!companyProductPath && !bundlePath)
                    throw new Exception("Unity persistent storage did not resolve to the dedicated smoke identity: " + data);
                if (Directory.Exists(data) && Directory.GetFileSystemEntries(data).Length != 0) throw new Exception("Smoke storage is not fresh; rebuild to obtain a new identity.");
                // Never read real credential values; this already uses the verified dedicated preference domain.
                if (PlayerPrefs.HasKey("palmbay.auth.refresh") || PlayerPrefs.HasKey("palmbay.profile") || PlayerPrefs.HasKey("palmbay.auth.age-status"))
                    throw new Exception("Smoke preference domain is not fresh; rebuild to obtain a new identity.");
                var holder = new GameObject("M7 isolated player acceptance");
                DontDestroyOnLoad(holder);
                holder.AddComponent<M7PlayerSmoke>();
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("M7_SMOKE_ISOLATION_REJECTED: " + e);
                // Application.Quit is deferred; stop before AppState.Awake can load preferences.
                Environment.Exit(2);
            }
        }

        static string Argument(string[] args, string name)
        {
            int index = Array.IndexOf(args, name);
            if (index < 0 || index + 1 >= args.Length || string.IsNullOrEmpty(args[index + 1])) throw new Exception("Missing argument " + name);
            return args[index + 1];
        }
        void Awake()
        {
            started = Time.realtimeSinceStartup;
            Application.logMessageReceivedThreaded += OnLog;
            SceneManager.sceneLoaded += OnSceneLoaded;
            report.AppendLine("Scope: local offline guest + bot. Fixed 0.25-second Step calls with synthetic movement, not real human input.");
            report.AppendLine("No authentication injection, OAuth sign-in, cloud sync, room creation, native share or deployment performed.");
            report.AppendLine(isolation.mode == "ui-launch"
                ? "Isolation: standard system UI launch; fresh dedicated GUID company/product/bundle; verified preference and persistent storage domains. HOME is unchanged."
                : "Isolation: dedicated company/product/bundle; independent HOME/CFFIXED_USER_HOME; fresh preference and persistent storage domains.");
            exercise = Exercise();
            StartCoroutine(Drive());
        }
        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            state = AppState.Ensure(); state.ExternalControl = true;
            game = FindObjectOfType<AirportGame>();
            if (game != null) game.ExternalControl = true;
        }
        void OnLog(string message, string trace, LogType kind)
        {
            if (kind != LogType.Error && kind != LogType.Exception && kind != LogType.Assert) return;
            lock (errors) errors.Add(kind + ": " + message + "\n" + trace);
        }
        IEnumerator Drive()
        {
            while (!complete)
            {
                object next = null;
                bool more = false;
                try
                {
                    Require(Time.realtimeSinceStartup - started < 180f, "Acceptance timeout");
                    lock (errors) { if (errors.Count > 0) throw new Exception("Runtime error: " + errors[0]); }
                    more = exercise.MoveNext(); if (more) next = exercise.Current;
                }
                catch (Exception e) { Complete(false, e.ToString()); }
                if (!complete && !more) Complete(true, "All local standalone-player assertions passed.");
                if (!complete) yield return next;
            }
        }
        void Require(bool ok, string text)
        {
            if (!ok) throw new Exception(text);
            assertions++;
        }
        void Pass(string text) { report.AppendLine("PASS " + text); }
        IEnumerator Exercise()
        {
            for (int i = 0; i < 5; i++) yield return null;
            Require(SceneManager.GetActiveScene().name == AppState.SceneCabinLobby, "Build must start in CabinLobby");
            var lobby = LobbyApp.Instance;
            Require(lobby != null && state != null, "Initial lobby and persistent state exist");
            Require(!state.IsFakeAuth && !lobby.IsFakeAuth, "Standalone startup must not silently use FakeAuth");
            Require(state.Auth == null || state.Auth.User == null, "Isolated startup remains a guest");
            Require(lobby.AuthBadge == "游客" && !lobby.HasPartner && !lobby.CanBeginShift, "Initial guest lobby has no partner");
            Shot(lobby.ViewCamera, "01-initial-lobby.png");
            Pass("CabinLobby is the initial scene; guest, non-FakeAuth, no automatic partner.");
            lobby.AddBot();
            Require(lobby.Choice == LobbyApp.PartnerChoice.Bot && lobby.CanBeginShift, "Guest can select bot without signing in");
            Shot(lobby.ViewCamera, "02-bot-ready.png");
            lobby.BeginShift();
            for (int i = 0; i < 5; i++) yield return null;
            Require(SceneManager.GetActiveScene().name == AppState.ScenePalmBay && game != null, "Lobby launches PalmBay");
            Require(game.ExternalControl && game.Started && game.Solo && !game.Sandbox && !game.NetworkShift && !game.Remote, "Actual finite local solo shift starts under external control");
            Require(game.Crew.Count == 2 && !game.Crew[0].Bot && game.Crew[1].Bot && game.DisplayElapsed == 0, "Solo starts fresh with one input seat and one bot");
            string firstMatch = game.SettlementMatchId;
            Vector3 initialHuman = game.Crew[0].Position, initialBot = game.Crew[1].Position;
            bool humanMoved = false, botMoved = false;
            Shot(game.View, "03-shift-start.png");
            for (int i = 0; i < 1201 && !game.MatchFinished; i++)
            {
                // Input is deliberately synthetic and carries no claim of human play acceptance.
                game.SetInput(0, new CrewInput { Move = i < 80 ? new Vector2(i < 40 ? 1 : -1, 0) : Vector2.zero });
                game.Step(.25f);
                humanMoved |= Vector3.Distance(initialHuman, game.Crew[0].Position) > .1f;
                botMoved |= Vector3.Distance(initialBot, game.Crew[1].Position) > .1f;
                if (i % 24 == 0) yield return null;
            }
            Require(humanMoved && botMoved, "Injected input seat and real bot logic both moved");
            Require(game.MatchFinished && game.DisplayElapsed == 300f && game.DisplayRemaining == 0f, "Actual 300-second shift reaches zero and finishes");
            simulatedSeconds = game.DisplayElapsed;
            var hud = game.GetComponent<AirportHudCanvas>();
            Require(hud != null, "Results HUD exists"); hud.RefreshNow();
            Require(hud.ResultsVisible && hud.RetryInteractable && hud.ShareInteractable, "Results and enabled actions appear");
            Require(hud.VisibleResultStars == new string('★', game.DisplayStars) + new string('☆', 3 - game.DisplayStars), "Result stars match simulation");
            Require(hud.VisibleSettlementStatus.Contains("游客") && !state.IsFakeAuth && (state.Auth == null || state.Auth.User == null), "Guest settlement is disclosed without auth injection");
            Require(hud.ResultsActionsInsideSafeArea, "Result actions fit the safe area");
            string frozen = DomainState();
            for (int i = 0; i < 120; i++)
            {
                game.SetInput(0, new CrewInput { Move = Vector2.one, Pressed = true, Held = true, Cycle = true });
                game.Step(.25f);
                Require(DomainState() == frozen, "Ended shift rejects movement and interaction; domain state remains frozen");
                if (i % 24 == 0) yield return null;
            }
            Require(game.ReplayAvailable && game.ReplayFrameCount > 100 && game.ReplayFraction == 1f, "Recorded replay reaches final frame");
            float terminal = game.ReplayTime;
            Vector3 heldCrew = game.Crew[0].Visual.position, heldCart = game.Carts[0].Visual.position;
            game.Step(2f);
            Require(game.ReplayTime == terminal && game.ReplayFraction == 1f && Vector3.Distance(heldCrew, game.Crew[0].Visual.position) < .001f &&
                Vector3.Distance(heldCart, game.Carts[0].Visual.position) < .001f && DomainState() == frozen, "Replay holds its terminal pose and stable result");
            Shot(game.View, "04-results-final-replay.png");
            Pass("300-second simulation, synthetic input + bot movement, timer zero, frozen domain/input, result stars and guest status, replay terminal hold.");
            hud.ResultButton("Next").onClick.Invoke(); hud.RefreshNow();
            Require(hud.VisibleResultToast == "后续关卡还未解锁" && game.MatchFinished, "Locked next action provides visible feedback");
            hud.ResultButton("Retry").onClick.Invoke(); hud.RefreshNow();
            Require(game.SettlementMatchId != firstMatch && game.Started && !game.MatchFinished && game.DisplayElapsed == 0f && game.DisplayScore == 0 &&
                game.ReplayTime == 0 && game.ReplayFrameCount == 1 && !hud.ResultsVisible && string.IsNullOrEmpty(game.ExportedVideoPath), "Retry starts a fresh match and clears result/replay identity");
            game.SetInput(0, new CrewInput()); game.Step(.25f);
            Require(game.DisplayElapsed == .25f, "Retried match advances normally");
            Shot(game.View, "05-retry.png");
            Pass("Result Retry creates a fresh match; locked Next displays its reason.");
            string retryMatch = game.SettlementMatchId;
            game.BackToLobby();
            for (int i = 0; i < 5; i++) yield return null;
            lobby = LobbyApp.Instance;
            Require(SceneManager.GetActiveScene().name == AppState.SceneCabinLobby && lobby != null, "Return loads CabinLobby");
            Require(AppState.Instance == state && !lobby.HasPartner && !lobby.CanBeginShift && !lobby.HasAuthUser && !lobby.IsFakeAuth, "Returned lobby retains guest state and clears partner selection");
            Shot(lobby.ViewCamera, "06-returned-lobby.png");
            lobby.AddBot(); lobby.BeginShift();
            for (int i = 0; i < 5; i++) yield return null;
            Require(game != null && SceneManager.GetActiveScene().name == AppState.ScenePalmBay && game.Solo && !game.MatchFinished && game.DisplayElapsed == 0 &&
                game.SettlementMatchId != retryMatch && game.Crew[1].Bot, "Guest can re-enter a fresh solo + bot shift after returning");
            game.SetInput(0, new CrewInput()); game.Step(.25f);
            Require(game.DisplayElapsed == .25f, "Second entry advances normally");
            Shot(game.View, "07-second-entry.png");
            Pass("BackToLobby and second local entry preserve the singleton, guest state and fresh shift identity.");
            yield return null;
        }
        string DomainState()
        {
            var snap = new Snapshot { IsPractice = false, Ended = game.MatchFinished, MatchId = game.SettlementMatchId,
                Elapsed = game.DisplayElapsed, Score = game.DisplayScore, Stars = game.DisplayStars,
                CompletedFlights = game.DisplayDeparted, MissedFlights = game.Sim.MissedCount, CompletedTasks = game.Sim.CompletedTaskCount,
                Deliveries = game.Deliveries, TrafficStops = game.TrafficStops, Shift = game.Shift.CaptureShift() };
            for (int i = 0; i < game.Crew.Count; i++) snap.Crew[i] = new CrewSnap { X = game.Crew[i].Position.x, Z = game.Crew[i].Position.z, Sel = game.Crew[i].Selected };
            for (int i = 0; i < game.Carts.Count; i++) snap.Carts[i] = new CartSnap { X = game.Carts[i].Position.x, Z = game.Carts[i].Position.z, OwnerSeat = game.Carts[i].Owner == null ? -1 : game.Carts[i].Owner.Index };
            snap.Flights = new FlightSnap[game.Sim.Flights.Count];
            for (int i = 0; i < snap.Flights.Length; i++)
            {
                Flight f = game.Sim.Flights[i]; snap.Flights[i] = new FlightSnap { Id = f.Id, Status = (int)f.Status, Stand = f.Stand,
                    Arrival = f.ArrivalTime, Deadline = f.Deadline, Progress = (float[])f.Progress.Clone(), ArrivalBagsReturned = f.ArrivalBagsReturned };
            }
            return NetProtocol.BuildSnapshot(snap);
        }
        void Shot(Camera camera, string name)
        {
            Require(camera != null, "Screenshot camera exists: " + name);
            const int width = 1920, height = 1080;
            RenderTexture previousTarget = camera.targetTexture, previousActive = RenderTexture.active;
            float previousAspect = camera.aspect;
            var target = new RenderTexture(width, height, 24); Texture2D pixels = null;
            try
            {
                camera.targetTexture = target; camera.aspect = (float)width / height;
                Canvas.ForceUpdateCanvases();
                var hud = game == null ? null : game.GetComponent<AirportHudCanvas>(); if (hud != null) hud.RefreshNow();
                var lobby = LobbyApp.Instance; if (lobby != null) { var canvas = lobby.GetComponent<LobbyCanvas>(); if (canvas != null) canvas.RefreshNow(); }
                camera.Render(); RenderTexture.active = target;
                pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
                byte[] png = pixels.EncodeToPNG(); Require(png != null && png.Length > 10000, "Rendered PNG contains image data: " + name);
                File.WriteAllBytes(Path.Combine(isolation.evidence, name), png);
            }
            finally
            {
                camera.targetTexture = previousTarget; camera.aspect = previousAspect; RenderTexture.active = previousActive;
                if (pixels != null) Destroy(pixels); target.Release(); Destroy(target);
            }
        }
        void Complete(bool success, string reason)
        {
            if (complete) return; complete = true;
            Application.logMessageReceivedThreaded -= OnLog; SceneManager.sceneLoaded -= OnSceneLoaded;
            try
            {
                if (game != null) game.CancelReplayExport();
                lock (errors)
                {
                    success &= errors.Count == 0;
                    var result = new Result { status = success ? "PASS" : "FAIL", reason = reason, company = Application.companyName,
                        product = Application.productName, bundle = Application.identifier, persistentDataPath = Application.persistentDataPath,
                        evidence = isolation.evidence, isolationMode = isolation.mode, runtimeErrors = errors.Count, assertions = assertions, simulatedSeconds = simulatedSeconds,
                        wallSeconds = Time.realtimeSinceStartup - started };
                    File.WriteAllText(Path.Combine(isolation.evidence, "m7-player-smoke.json"), JsonUtility.ToJson(result, true));
                    File.WriteAllText(Path.Combine(isolation.evidence, "m7-player-smoke.txt"), "M7 standalone player: " + result.status + "\n" + reason + "\n" + report + "\n" + string.Join("\n", errors.ToArray()));
                }
                Console.WriteLine("M7_PLAYER_SMOKE_" + (success ? "PASS" : "FAIL") + " evidence=" + isolation.evidence);
            }
            catch (Exception e) { success = false; Console.Error.WriteLine("M7_SMOKE_REPORT_FAILED: " + e); }
            Application.Quit(success ? 0 : 1);
        }
    }
}
