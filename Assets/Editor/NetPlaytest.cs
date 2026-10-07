using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using IslandAirport;

[InitializeOnLoad]
public static partial class NetPlaytest
{
    const string Pending = "PalmBay.NetPlaytest";
    const double WatchdogSeconds = 150.0;
    const double StageTimeoutSeconds = 25.0;
    const float PickupProbeDelta = 0.1f;
    static readonly string[] PreferenceKeys = { "palmbay.profile", "palmbay.auth.refresh", "palmbay.auth.age-status" };
    static readonly StringBuilder report = new StringBuilder();
    static readonly List<string> runtimeErrors = new List<string>();
    static string scenario;
    static string controlDirectory;
    static string joinAddress;
    static string role;
    static AppState state;
    static AirportGame game;
    static int stage;
    static bool initialized;
    static bool authInjected;
    static bool authReady;
    static bool runtimeFault;
    static bool completionWritten;
    static bool contestHostPressAfterStep;
    static bool contestHostReleaseAfterStep;
    static double startedAt;
    static double lastUpdate;
    static double stageStarted;
    static double ownershipStableAt = -1.0;
    static double hostStartTime;
    static double contestSignalSeenAt = -1.0;
    static float hostContestOwnerAtSim;
    static double movementStartTime;
    static float stepOverride = -1f;
    static Vector3 clientHostStartPosition;
    static Vector3 hostRemoteStartPosition;
    static Vector3 hostLocalStartPosition;
    static Vector3 hostLocalEndPosition;
    static bool pickupSent;
    static bool releaseSent;
    static string signalAfterStep;
    static bool suppressGameStep;
    static Flight mealFlight;
    static string mealCargoFlightId = string.Empty;
    static bool mealOrderSent;
    static bool mealGrabSent;
    static bool mealTakeSent;
    static Flight baggageFlightA;
    static Flight baggageFlightB;
    static string baggageFlightAId = string.Empty;
    static string baggageFlightBId = string.Empty;
    static bool baggageGrabSent;
    static bool baggagePickupSent;
    static bool baggageReturnSent;
    static bool baggageLoadSent;
    static bool baggageCancelStarted;
    static bool baggageWrongStarted;
    static bool baggageReloadSent;
    static bool baggageCorrectStarted;
    static int baggageScoreBefore;

    static NetPlaytest()
    {
        EditorApplication.update += Poll;
    }

    public static void RunHostMovement()
    {
        StartRun("host-movement");
    }

    public static void RunClientMovement()
    {
        StartRun("client-movement");
    }

    public static void RunHostDeparture()
    {
        StartRun("host-departure");
    }

    public static void RunClientDeparture()
    {
        StartRun("client-departure");
    }

    public static void RunHostMeal()
    {
        StartRun("host-meal");
    }

    public static void RunClientMeal()
    {
        StartRun("client-meal");
    }

    public static void RunHostBaggage()
    {
        StartRun("host-baggage");
    }

    public static void RunClientBaggage()
    {
        StartRun("client-baggage");
    }

    static void StartRun(string runScenario)
    {
        ValidateIsolationOrThrow();
        CapturePreferences();
        Environment.SetEnvironmentVariable("PALMBAY_FIREBASE_KEY", null);
        if (!File.Exists(LobbyBuilder.ScenePath))
            throw new FileNotFoundException("NetPlaytest requires the saved CabinLobby scene.", LobbyBuilder.ScenePath);

        SessionState.SetString(Pending + ".scenario", runScenario);
        SessionState.SetString(Pending + ".control", Environment.GetEnvironmentVariable("PALMBAY_NETPLAY_CONTROL_DIR") ?? string.Empty);
        SessionState.SetBool(Pending, true);
        SessionState.SetBool(Pending + ".stop", false);
        SessionState.SetBool(Pending + ".failed", false);
        EditorSceneManager.OpenScene(LobbyBuilder.ScenePath);
        EditorApplication.isPlaying = true;
    }

    static void CapturePreferences()
    {
        SessionState.SetBool(Pending + ".prefsCaptured", true);
        for (int i = 0; i < PreferenceKeys.Length; i++)
        {
            string key = PreferenceKeys[i];
            SessionState.SetBool(Pending + ".prefHas." + i, PlayerPrefs.HasKey(key));
            SessionState.SetString(Pending + ".prefValue." + i, PlayerPrefs.GetString(key, string.Empty));
        }
    }

    static void Poll()
    {
        if (!SessionState.GetBool(Pending, false)) return;
        if (SessionState.GetBool(Pending + ".stop", false))
        {
            if (EditorApplication.isPlaying) return;
            RestoreAndClearPreferences();
            SessionState.SetBool(Pending, false);
            if (Application.isBatchMode)
                EditorApplication.Exit(SessionState.GetBool(Pending + ".failed", true) ? 1 : 0);
            return;
        }
        if (!EditorApplication.isPlaying) return;

        EditorApplication.QueuePlayerLoopUpdate();
        try
        {
            if (!initialized) InitializeRun();
            double now = EditorApplication.timeSinceStartup;
            double watchdog = scenario != null && scenario.IndexOf("-m4-", StringComparison.Ordinal) >= 0 ? 330.0 : scenario != null &&
                (scenario.EndsWith("-meal", StringComparison.Ordinal) || scenario.EndsWith("-baggage", StringComparison.Ordinal) ||
                    scenario.EndsWith("-fuel", StringComparison.Ordinal) || scenario.EndsWith("-boarding", StringComparison.Ordinal))
                ? 280.0 : WatchdogSeconds;
            if (now - startedAt > watchdog)
                throw new TimeoutException("NetPlaytest exceeded the " + watchdog + " second watchdog. " + Diagnostics());
            double stageTimeout = scenario.IndexOf("-m4-", StringComparison.Ordinal) >= 0 ? 150.0 : scenario.StartsWith("host-", StringComparison.Ordinal) && stage == 1
                ? 100.0
                : scenario.EndsWith("-fuel", StringComparison.Ordinal) && (stage == 1 || stage == 2)
                    ? 100.0
                    : scenario.EndsWith("-fuel", StringComparison.Ordinal) && stage == 9
                        ? 100.0
                        : scenario.EndsWith("-fuel", StringComparison.Ordinal) && stage == 8
                            ? 60.0
                : scenario.EndsWith("-baggage", StringComparison.Ordinal) && scenario.StartsWith("host-", StringComparison.Ordinal) && stage == 2
                    ? 100.0
                    : scenario.EndsWith("-baggage", StringComparison.Ordinal) && stage == 1 ? 100.0
                        : scenario.EndsWith("-baggage", StringComparison.Ordinal) ? 45.0
                        : scenario.EndsWith("-boarding", StringComparison.Ordinal) ? 60.0 : StageTimeoutSeconds;
            if (now - stageStarted > stageTimeout)
                throw new TimeoutException("NetPlaytest stage " + stage + " exceeded " + stageTimeout + " seconds. " + Diagnostics());

            double elapsed = now - lastUpdate;
            if (elapsed < 1.0 / 60.0) return;
            float dt = Mathf.Min((float)elapsed, 0.1f);
            lastUpdate = now;
            state = AppState.Instance != null ? AppState.Instance : AppState.Ensure();
            state.ExternalControl = true;
            if (!authInjected)
            {
                state.UseAuthForTesting(new FakeAuthService());
                if (!state.IsFakeAuth || state.Auth == null)
                    throw new Exception("AppState did not accept the injected FakeAuthService.");
                state.SignInGoogle();
                authInjected = true;
                report.AppendLine("PASS FakeAuthService injected and sign-in started; auth configuration and credentials are excluded.");
            }

            game = UnityEngine.Object.FindAnyObjectByType<AirportGame>();
            bool driveGame = game != null && game.Started && (game.Sandbox || game.Remote || scenario.IndexOf("-m4-", StringComparison.Ordinal) >= 0);
            if (driveGame)
                game.ExternalControl = true;
            state.PumpNet(dt);
            suppressGameStep = false;

            if (!authReady)
            {
                if (state.Auth == null || state.Auth.User == null) return;
                string ageError;
                Require(state.TryVerifyAge(2000, 1, 1, out ageError), "Fake age verification failed: " + ageError);
                Require(state.CanPlayWithOthers, "Fake user did not pass CanPlayWithOthers after age verification.");
                authReady = true;
                report.AppendLine("PASS Fake sign-in completed and TryVerifyAge authorized multiplayer before Room.Host/Join.");
            }

            AdvanceScenario(now, dt);

            if (driveGame && !suppressGameStep && game != null && game.Started && SceneManager.GetActiveScene().name == AppState.ScenePalmBay)
            {
                float frameDelta = stepOverride > 0f ? stepOverride : dt;
                stepOverride = -1f;
                game.Step(frameDelta);
                if (!string.IsNullOrEmpty(signalAfterStep))
                {
                    Signal(signalAfterStep);
                    signalAfterStep = null;
                }
                if (contestHostPressAfterStep)
                {
                    contestHostPressAfterStep = false;
                    Require(HostOwnsCartZeroOnly(), "Host did not win same-cart contention with seat 0 as the sole owner.");
                    hostContestOwnerAtSim = game.Sim.Elapsed;
                    report.AppendLine("PASS Simultaneous UDP/local grab contention resolved once to host seat 0; no second owner was created.");
                    Signal("s1-host-contest-owner");
                }
                if (contestHostReleaseAfterStep)
                {
                    contestHostReleaseAfterStep = false;
                    Require(CartZeroUnowned(), "Host contention release did not leave cart 0 unowned.");
                    Signal("s1-host-contest-released");
                }
            }
        }
        catch (Exception error)
        {
            Complete(true, error.ToString());
        }
    }

    static void InitializeRun()
    {
        ValidateIsolationOrThrow();
        scenario = SessionState.GetString(Pending + ".scenario", string.Empty);
        controlDirectory = SessionState.GetString(Pending + ".control", string.Empty);
        if (string.IsNullOrEmpty(scenario)) throw new Exception("NetPlaytest scenario was not provided.");
        if (string.IsNullOrEmpty(controlDirectory)) throw new Exception("PALMBAY_NETPLAY_CONTROL_DIR is missing.");
        Directory.CreateDirectory(controlDirectory);
        joinAddress = Environment.GetEnvironmentVariable("PALMBAY_JOIN");
        if (string.IsNullOrEmpty(joinAddress)) joinAddress = "127.0.0.1";
        joinAddress = joinAddress.Trim();
        role = scenario.StartsWith("host-", StringComparison.Ordinal) ? "host" : "client";
        startedAt = EditorApplication.timeSinceStartup;
        lastUpdate = startedAt;
        stageStarted = startedAt;
        stage = 0;
        report.Length = 0;
        runtimeErrors.Clear();
        runtimeFault = false;
        authInjected = false;
        authReady = false;
        completionWritten = false;
        contestHostPressAfterStep = false;
        contestHostReleaseAfterStep = false;
        contestSignalSeenAt = -1.0;
        hostContestOwnerAtSim = 0f;
        signalAfterStep = null;
        suppressGameStep = false;
        pickupSent = false;
        releaseSent = false;
        ownershipStableAt = -1.0;
        mealFlight = null;
        mealCargoFlightId = string.Empty;
        mealOrderSent = false;
        mealGrabSent = false;
        mealTakeSent = false;
        baggageFlightA = null;
        baggageFlightB = null;
        baggageFlightAId = string.Empty;
        baggageFlightBId = string.Empty;
        baggageGrabSent = false;
        baggagePickupSent = false;
        baggageReturnSent = false;
        baggageLoadSent = false;
        baggageCancelStarted = false;
        baggageWrongStarted = false;
        baggageReloadSent = false;
        baggageCorrectStarted = false;
        baggageScoreBefore = 0;
        Application.logMessageReceived += OnRuntimeLog;
        initialized = true;
    }

    static void AdvanceScenario(double now, float dt)
    {
        if (scenario.IndexOf("-m4-", StringComparison.Ordinal) >= 0)
            AdvanceM4(now, dt);
        else if (scenario == "host-movement")
            AdvanceHostMovement(now, dt);
        else if (scenario == "client-movement")
            AdvanceClientMovement(now, dt);
        else if (scenario == "host-departure")
            AdvanceHostDeparture(now);
        else if (scenario == "client-departure")
            AdvanceClientDeparture(now);
        else if (scenario == "host-meal")
            AdvanceHostMeal(now, dt);
        else if (scenario == "client-meal")
            AdvanceClientMeal(now, dt);
        else if (scenario == "host-baggage")
            AdvanceHostBaggage(now, dt);
        else if (scenario == "client-baggage")
            AdvanceClientBaggage(now, dt);
        else if (scenario == "host-fuel")
            AdvanceHostFuel(now, dt);
        else if (scenario == "client-fuel")
            AdvanceClientFuel(now, dt);
        else if (scenario == "host-boarding")
            AdvanceHostBoarding(now, dt);
        else if (scenario == "client-boarding")
            AdvanceClientBoarding(now, dt);
        else
            throw new Exception("Unknown NetPlaytest scenario: " + scenario);
    }

    static void AdvanceHostMovement(double now, float dt)
    {
        if (stage == 0)
        {
            Require(SceneManager.GetActiveScene().name == AppState.SceneCabinLobby, "Host did not start in CabinLobby.");
            Require(state.Room.SocketFactory == null, "Host RoomManager is not using its real UDP socket factory.");
            Require(state.CanPlayWithOthers, "Host authentication did not pass multiplayer eligibility.");
            Require(state.Room.Host("NetPlay Disconnect", "NetplayHost", FakeAuthToken()), "Room.Host failed.");
            Signal("s1-host-ready");
            report.AppendLine("PASS Real Room.Host bound the host UDP session before client startup.");
            SetStage(1, now);
            return;
        }

        if (stage == 1)
        {
            if (!state.Room.Seats[1].Occupied) return;
            Require(state.Room.Phase == RoomPhase.InRoom || state.Room.Phase == RoomPhase.Listening,
                "Host accepted a peer in an unexpected phase: " + state.Room.Phase);
            LobbyApp lobby = UnityEngine.Object.FindAnyObjectByType<LobbyApp>();
            Require(lobby != null, "CabinLobby LobbyApp was not available to start the room.");
            lobby.BeginPracticeShift();
            Require(state.Launch.Mode == AppState.GameMode.HostSandbox, "LobbyApp did not launch HostSandbox.");
            SetStage(2, now);
            return;
        }

        if (stage == 2)
        {
            if (!game || !game.Started) return;
            Require(SceneManager.GetActiveScene().name == AppState.ScenePalmBay, "Host did not load PalmBay after start.");
            Require(game.Sandbox && !game.Remote && game.Sim != null && game.Sim.Endless,
                "Host scene did not start in the endless sandbox mode.");
            ConfigureMovementFixtures();
            hostRemoteStartPosition = game.Crew[1].Position;
            hostLocalStartPosition = game.Crew[0].Position;
            Signal("s1-host-game-ready");
            report.AppendLine("PASS Host launched the sandbox through CabinLobby and began sending UDP snapshots.");
            SetStage(3, now);
            return;
        }

        if (stage == 3)
        {
            if (!HasSignal("s1-client-moved")) return;
            float moved = Vector3.Distance(hostRemoteStartPosition, game.Crew[1].Position);
            Require(moved > 2.0f, "Client UDP move intent moved the host actor only " + moved.ToString("0.00") + "m.");
            report.AppendLine("PASS Client move intent reached the host over UDP; displacement=" + moved.ToString("0.00") + "m.");
            Signal("s1-host-client-move-verified");
            SetStage(4, now);
            return;
        }

        if (stage == 4)
        {
            if (!HasSignal("s1-client-ready-hostmove")) return;
            hostLocalStartPosition = game.Crew[0].Position;
            hostStartTime = now;
            SetStage(5, now);
        }

        if (stage == 5)
        {
            if (now - hostStartTime < 0.9)
            {
                game.SetInput(0, new CrewInput { Move = Vector2.right });
                return;
            }
            game.SetInput(0, new CrewInput());
            hostLocalEndPosition = game.Crew[0].Position;
            float moved = Vector3.Distance(hostLocalStartPosition, hostLocalEndPosition);
            Require(moved > 2.0f, "Host controlled actor did not move far enough: " + moved.ToString("0.00") + "m.");
            report.AppendLine("PASS Host moved its crew through AirportGame.Step; displacement=" + moved.ToString("0.00") + "m.");
            Signal("s1-host-moved");
            SetStage(6, now);
            return;
        }

        if (stage == 6)
        {
            suppressGameStep = true;
            if (!HasSignal("s1-client-contest-pressed")) return;
            if (contestSignalSeenAt < 0.0)
            {
                contestSignalSeenAt = now;
                suppressGameStep = true;
                return;
            }
            if (now - contestSignalSeenAt < 0.15)
            {
                suppressGameStep = true;
                return;
            }
            game.Crew[0].Position = game.Carts[0].Position;
            game.Crew[0].Visual.position = game.Carts[0].Position;
            game.SetInput(0, new CrewInput { Pressed = true });
            contestHostPressAfterStep = true;
            suppressGameStep = false;
            SetStage(7, now);
            return;
        }

        if (stage == 7)
        {
            if (!HasSignal("s1-client-contest-seen")) return;
            if (game.Sim != null && game.Sim.Elapsed - hostContestOwnerAtSim < 0.35f) return;
            game.SetInput(0, new CrewInput { Pressed = true });
            contestHostReleaseAfterStep = true;
            SetStage(8, now);
            return;
        }

        if (stage == 8)
        {
            if (!HasSignal("s1-client-contest-clear")) return;
            if (ClientOwnsCartZero())
            {
                if (ownershipStableAt < 0.0) ownershipStableAt = now;
                if (now - ownershipStableAt >= 0.6)
                {
                    report.AppendLine("PASS One client Pressed edge grabbed cart 0 and stayed consumed across repeated host Steps.");
                    Signal("s1-host-grab-stable");
                    ownershipStableAt = -1.0;
                    SetStage(9, now);
                }
            }
            else
            {
                ownershipStableAt = -1.0;
            }
            return;
        }

        if (stage == 9)
        {
            if (!HasSignal("s1-client-release-seen")) return;
            if (CartZeroUnowned())
            {
                if (ownershipStableAt < 0.0) ownershipStableAt = now;
                if (now - ownershipStableAt >= 0.5)
                {
                    report.AppendLine("PASS Client release edge reached the host and left the cart unowned.");
                    Signal("s1-host-release-stable");
                    SetStage(10, now);
                }
            }
            else
            {
                ownershipStableAt = -1.0;
            }
            return;
        }

        if (stage == 10)
        {
            if (!HasSignal("s1-client-socket-closed")) return;
            if (!state.Room.Seats[1].Bot || !game.Crew[1].Bot) return;
            report.AppendLine("PASS Silent client UDP socket loss was detected; RoomManager and host crew switched seat 1 to BOT.");
            Signal("s1-host-bot-confirmed");
            SetStage(11, now);
            return;
        }

        if (stage == 11)
        {
            if (!HasSignal("s1-client-complete")) return;
            state.Room.CloseRoom();
            Complete(false, "Session 1 host verified real UDP movement, snapshots, cart edges, and abrupt client disconnect takeover.");
        }
    }

    static void AdvanceClientMovement(double now, float dt)
    {
        if (stage == 0)
        {
            Require(state.Room.SocketFactory == null, "Client RoomManager is not using its real UDP socket factory.");
            Require(state.CanPlayWithOthers, "Client authentication did not pass multiplayer eligibility.");
            Require(state.Room.Join(joinAddress, "NetplayClient", FakeAuthToken()), "Room.Join failed for " + joinAddress + ".");
            state.Room.RoomName = "NetPlay Disconnect";
            report.AppendLine("PASS Real Room.Join started for " + joinAddress + ".");
            SetStage(1, now);
            return;
        }

        if (stage == 1)
        {
            if (state.Room.Phase != RoomPhase.Starting && state.Room.Phase != RoomPhase.Playing) return;
            if (!game || !game.Started) return;
            Require(SceneManager.GetActiveScene().name == AppState.ScenePalmBay, "Client did not load PalmBay after host start.");
            // M3.1：练习模式镜像带航班（host 有时刻表生成器），旧"空航班表"断言按计划作废。
            Require(game.Remote && !game.Sandbox && game.Shift != null && game.Sim != null &&
                game.Sim.Endless && game.RemoteSeat == 1,
                "Client scene did not start in read-only mirror Remote mode on seat 1.");
            if (game.Snaps == null || game.Snaps.Count < 2 || !game.Crew[0].Visual.gameObject.activeSelf) return;
            if (!HasSignal("s1-host-game-ready")) return;
            clientHostStartPosition = game.Crew[0].Position;
            Signal("s1-client-game-ready");
            report.AppendLine("PASS Client launched Remote after the real start control and consumed host snapshots.");
            movementStartTime = now;
            SetStage(2, now);
            return;
        }

        if (stage == 2)
        {
            if (now - movementStartTime < 1.2)
            {
                game.SetInput(0, new CrewInput { Move = Vector2.right });
                return;
            }
            game.SetInput(0, new CrewInput());
            Signal("s1-client-moved");
            report.AppendLine("PASS Client supplied movement through its Remote input path.");
            SetStage(3, now);
            return;
        }

        if (stage == 3)
        {
            if (!HasSignal("s1-host-client-move-verified")) return;
            Signal("s1-client-ready-hostmove");
            SetStage(4, now);
            return;
        }

        if (stage == 4)
        {
            if (!HasSignal("s1-host-moved")) return;
            float moved = Vector3.Distance(clientHostStartPosition, game.Crew[0].Position);
            if (moved <= 2.0f) return;
            report.AppendLine("PASS Host movement arrived at the client through UDP snapshots; displacement=" + moved.ToString("0.00") + "m.");
            Signal("s1-client-host-snapshot-seen");
            SetStage(5, now);
            return;
        }

        if (stage == 5)
        {
            if (!pickupSent)
            {
                game.SetInput(0, new CrewInput { Pressed = true });
                stepOverride = PickupProbeDelta;
                pickupSent = true;
                signalAfterStep = "s1-client-contest-pressed";
                report.AppendLine("Probe: simultaneous contention queued one client Pressed=true Remote step at dt=0.1s.");
                SetStage(6, now);
                return;
            }
        }

        if (stage == 6)
        {
            if (!HasSignal("s1-host-contest-owner")) return;
            if (!HostSeatOwnsCartZeroSnapshot()) return;
            report.AppendLine("PASS Client snapshot confirms host seat 0 won simultaneous cart contention with a single owner.");
            Signal("s1-client-contest-seen");
            SetStage(7, now);
            return;
        }

        if (stage == 7)
        {
            if (!HasSignal("s1-host-contest-released")) return;
            if (!CartZeroUnowned()) return;
            Signal("s1-client-contest-clear");
            SetStage(8, now);
            return;
        }

        if (stage == 8)
        {
            if (!HasSignal("s1-host-contest-released")) return;
            if (!releaseSent)
            {
                game.SetInput(0, new CrewInput { Pressed = true });
                stepOverride = PickupProbeDelta;
                pickupSent = false;
                signalAfterStep = "s1-client-grab-probe-sent";
                releaseSent = true;
                report.AppendLine("Probe: one Pressed=true client grab step used dt=0.1s, then neutral frames.");
                SetStage(9, now);
                return;
            }
        }

        if (stage == 9)
        {
            if (!HasSignal("s1-client-grab-probe-sent")) return;
            if (!ClientOwnsCartZero())
            {
                ownershipStableAt = -1.0;
                return;
            }
            if (ownershipStableAt < 0.0) ownershipStableAt = now;
            if (now - ownershipStableAt < 0.4) return;
            report.AppendLine("PASS Client snapshot matches host cart 0 ownership by seat 1 after repeated host Steps.");
            Signal("s1-client-grab-seen");
            SetStage(10, now);
            return;
        }

        if (stage == 10)
        {
            if (!HasSignal("s1-host-grab-stable")) return;
            game.SetInput(0, new CrewInput { Pressed = true });
            stepOverride = PickupProbeDelta;
            report.AppendLine("Probe: one Pressed=true release step used dt=0.1s, then neutral frames.");
            ownershipStableAt = -1.0;
            SetStage(11, now);
            return;
        }

        if (stage == 11)
        {
            if (ClientOwnsCartZero())
            {
                ownershipStableAt = -1.0;
                return;
            }
            if (game.Crew[1].Cart != null || game.Carts[0].Owner != null) return;
            if (ownershipStableAt < 0.0) ownershipStableAt = now;
            if (now - ownershipStableAt < 0.25) return;
            report.AppendLine("PASS Client snapshot reports no owner after the single release edge and neutral frames.");
            Signal("s1-client-release-seen");
            SetStage(12, now);
            return;
        }

        if (stage == 12)
        {
            if (!HasSignal("s1-host-release-stable")) return;
            CloseClientSocketAbruptly();
            report.AppendLine("PASS Client closed its real UDP session socket without sending leave.");
            Signal("s1-client-socket-closed");
            SetStage(13, now);
            return;
        }

        if (stage == 13)
        {
            if (!HasSignal("s1-host-bot-confirmed")) return;
            if (SceneManager.GetActiveScene().name != AppState.SceneCabinLobby) return;
            Require(state.Room.Phase == RoomPhase.Idle || state.Room.Phase == RoomPhase.Closed,
                "Disconnected client RoomManager did not clean up before returning to CabinLobby.");
            report.AppendLine("PASS Client heartbeat timeout returned the session to CabinLobby after the host confirmed BOT takeover.");
            Signal("s1-client-complete");
            Complete(false, "Session 1 client verified replicated movement and cart ownership, then disconnected without a leave packet.");
        }
    }

    static void AdvanceHostDeparture(double now)
    {
        if (stage == 0)
        {
            Require(SceneManager.GetActiveScene().name == AppState.SceneCabinLobby, "Second host did not start in CabinLobby.");
            Require(state.Room.SocketFactory == null, "Second host RoomManager is not using real UDP.");
            Require(state.CanPlayWithOthers, "Second host authentication did not pass multiplayer eligibility.");
            Require(state.Room.Host("NetPlay Host Close", "NetplayHost2", FakeAuthToken()), "Second Room.Host failed.");
            Signal("s2-host-ready");
            report.AppendLine("PASS Fresh session 2 opened a real Room.Host socket in a new Unity process.");
            SetStage(1, now);
            return;
        }

        if (stage == 1)
        {
            if (!state.Room.Seats[1].Occupied) return;
            LobbyApp lobby = UnityEngine.Object.FindAnyObjectByType<LobbyApp>();
            Require(lobby != null, "Second CabinLobby LobbyApp was not available.");
            lobby.BeginPracticeShift();
            Require(state.Launch.Mode == AppState.GameMode.HostSandbox, "Second host did not launch HostSandbox.");
            SetStage(2, now);
            return;
        }

        if (stage == 2)
        {
            if (!game || !game.Started) return;
            Require(game.Sandbox && !game.Remote && game.Sim != null && game.Sim.Endless,
                "Second host scene did not enter sandbox.");
            if (state.Room.SnapshotsSent < 2 || !HasSignal("s2-client-game-ready")) return;
            state.Room.CloseRoom();
            report.AppendLine("PASS Host sent the real Room.CloseRoom control packet in session 2.");
            Signal("s2-host-close-sent");
            SetStage(3, now);
            return;
        }

        if (stage == 3)
        {
            if (!HasSignal("s2-client-returned")) return;
            Complete(false, "Session 2 host departure control returned the remote client to CabinLobby.");
        }
    }

    static void AdvanceClientDeparture(double now)
    {
        if (stage == 0)
        {
            Require(state.Room.SocketFactory == null, "Second client RoomManager is not using real UDP.");
            Require(state.CanPlayWithOthers, "Second client authentication did not pass multiplayer eligibility.");
            Require(state.Room.Join(joinAddress, "NetplayClient2", FakeAuthToken()), "Second Room.Join failed for " + joinAddress + ".");
            state.Room.RoomName = "NetPlay Host Close";
            report.AppendLine("PASS Fresh session 2 called real Room.Join in a new Unity process.");
            SetStage(1, now);
            return;
        }

        if (stage == 1)
        {
            if (state.Room.Phase != RoomPhase.Starting && state.Room.Phase != RoomPhase.Playing) return;
            if (!game || !game.Started || !game.Remote) return;
            if (game.Snaps == null || game.Snaps.Count < 2) return;
            Signal("s2-client-game-ready");
            report.AppendLine("PASS Fresh session 2 started Remote and received host snapshots before departure.");
            SetStage(2, now);
            return;
        }

        if (stage == 2)
        {
            if (!HasSignal("s2-host-close-sent")) return;
            if (SceneManager.GetActiveScene().name != AppState.SceneCabinLobby) return;
            Require(UnityEngine.Object.FindAnyObjectByType<LobbyApp>() != null, "Client returned to CabinLobby without rebuilding LobbyApp.");
            Require(state.IsFakeAuth && state.Auth is FakeAuthService, "Returning to CabinLobby lost its injected FakeAuthService.");
            report.AppendLine("PASS Room.CloseRoom reached the client over UDP and NetSession returned it to CabinLobby.");
            Signal("s2-client-returned");
            Complete(false, "Session 2 client returned to CabinLobby after the host sent its close control.");
        }
    }

    static void ConfigureMovementFixtures()
    {
        Vector3 clientPosition = new Vector3(-5f, 0f, 0f);
        Vector3 cartPosition = new Vector3(1f, 0f, 0f);
        Vector3 hostPosition = new Vector3(-7f, 0f, -5f);
        game.Crew[0].Position = hostPosition;
        game.Crew[0].Visual.position = hostPosition;
        game.Crew[1].Position = clientPosition;
        game.Crew[1].Visual.position = clientPosition;
        game.Crew[1].Cart = null;
        for (int i = 0; i < game.Carts.Count; i++)
        {
            game.Carts[i].Owner = null;
            game.Shift.Carts[i].Clear();
            game.Carts[i].Position = i == 0 ? cartPosition : (i == 1 ? new Vector3(5f, 0f, 5f) : new Vector3(-10f, 0f, 7f));
            game.Carts[i].Visual.position = game.Carts[i].Position;
            if (game.Carts[i].Cargo) game.Carts[i].Cargo.gameObject.SetActive(false);
        }
    }

    // ---------- M3.1 餐食剧本（session 3，带图形双端同刻截图） ----------

    static void AdvanceHostMeal(double now, float dt)
    {
        if (stage == 0)
        {
            Require(SceneManager.GetActiveScene().name == AppState.SceneCabinLobby, "Meal host did not start in CabinLobby.");
            Require(state.Room.SocketFactory == null, "Meal host RoomManager is not using its real UDP socket factory.");
            Require(state.CanPlayWithOthers, "Meal host authentication did not pass multiplayer eligibility.");
            Require(state.Room.Host("NetPlay Meal", "NetplayHost3", FakeAuthToken()), "Meal Room.Host failed.");
            Signal("s3-host-ready");
            report.AppendLine("PASS Meal session host bound the real UDP session socket.");
            SetStage(1, now);
            return;
        }

        if (stage == 1)
        {
            if (!state.Room.Seats[1].Occupied) return;
            LobbyApp lobby = UnityEngine.Object.FindAnyObjectByType<LobbyApp>();
            Require(lobby != null, "Meal CabinLobby LobbyApp was not available to start the room.");
            lobby.BeginPracticeShift();
            Require(state.Launch.Mode == AppState.GameMode.HostSandbox, "Meal lobby did not launch HostSandbox.");
            SetStage(2, now);
            return;
        }

        if (stage == 2)
        {
            if (!game || !game.Started) return;
            Require(SceneManager.GetActiveScene().name == AppState.ScenePalmBay, "Meal host did not load PalmBay.");
            Require(game.Sandbox && !game.Remote && game.Sim != null && game.Sim.Endless,
                "Meal host scene did not enter endless practice mode.");
            for (int s = 0; s < Level1Map.StandCount; s++)
            {
                Flight f = game.Sim.ActiveAtStand(s);
                if (f != null && game.StandReady(s)) { mealFlight = f; break; }
            }
            if (mealFlight == null) return;
            ConfigureMealFixtures();
            Signal("s3-host-meal-station");
            report.AppendLine("PASS Practice scheduler produced servicing flight " + mealFlight.Id +
                " at stand " + mealFlight.Stand + "; fixtures placed the client seat by the meal station.");
            SetStage(3, now);
            return;
        }

        if (stage == 3)
        {
            // client 在餐食站旁点按 → 远端意图经 UDP 由 host 的 Interact 映射到 ShiftSim.OrderMeal。
            if (game.Shift.Meal != MealPhase.Ordered) return;
            report.AppendLine("PASS Client order intent reached the host over UDP; ShiftSim accepted OrderMeal from seat 1.");
            Signal("s3-host-meal-ordered");
            SetStage(4, now);
            return;
        }

        if (stage == 4)
        {
            if (game.Shift.Meal != MealPhase.Ready) return;
            // 出货口阻塞旁证：Ready 未取走时 host 自己下单也被拒。
            Require(!game.Shift.OrderMeal(0), "Meal output was not blocked while Ready.");
            game.Shift.DrainEvents(); // 吞掉探针事件，不污染剧本 toast 对照
            Shot("Evidence/m3-meal-ready-host.png", 1920, 1080);
            report.AppendLine("PASS Host captured the meal-ready moment (meal card, flight board, practice timer).");
            Signal("s3-host-ready-captured");
            SetStage(5, now);
            return;
        }

        if (stage == 5)
        {
            if (!HasSignal("s3-client-ready-captured")) return; // 双端同刻对照收齐再推进
            Crew client = game.Crew[1];
            Cart cart = game.Carts[0];
            cart.Position = client.Position;
            cart.Visual.position = client.Position;
            Signal("s3-host-cart-ready");
            SetStage(6, now);
            return;
        }

        if (stage == 6)
        {
            // client 两次点按：抓车 → 装餐（TakeMeal 绑定 mealFlight）。
            if (game.Shift.Meal == MealPhase.Ready) return;
            Require(game.Shift.Carts[0].Loaded && game.Shift.Carts[0].CargoFlightId == mealFlight.Id,
                "Meal was not loaded onto the client cart for " + mealFlight.Id + ".");
            Require(game.Carts[0].Owner == game.Crew[1], "Client does not own the meal cart after the grab edge.");
            Require(game.Shift.Meal == MealPhase.Idle, "Meal output was not cleared after take-meal.");
            report.AppendLine("PASS Client grab + take-meal executed on host; cargo bound to " + mealFlight.Id + ".");
            Crew client = game.Crew[1];
            Vector3 dock = Level1Map.Dock(mealFlight.Stand) + new Vector3(0.4f, 0, 0.4f);
            client.Position = dock;
            client.Visual.position = dock;
            game.Carts[0].Position = dock;
            game.Carts[0].Visual.position = dock;
            Signal("s3-host-at-dock");
            SetStage(7, now);
            return;
        }

        if (stage == 7)
        {
            // client 按住交付：读条在 domain（ShiftSim.Carts[0].DeliverWork）按 dt 推进。
            float work = game.Shift.Carts[0].DeliverWork;
            if (work < 0.3f) return;
            Require(work < 1f, "Delivery finished before the dual-end read-out capture.");
            Shot("Evidence/m3-meal-deliver-host.png", 1920, 1080);
            report.AppendLine("PASS Host captured the in-progress delivery read-out at work=" + work.ToString("0.00") + ".");
            Signal("s3-host-deliver-captured");
            SetStage(8, now);
            return;
        }

        if (stage == 8)
        {
            if (mealFlight.Progress[0] < 1f) return;
            Require(!game.Shift.Carts[0].Loaded, "Delivered meal cart was not cleared for reuse.");
            Require(game.Sim.Score >= 50, "Meal delivery did not score on the host.");
            report.AppendLine("PASS Client held-deliver over UDP completed " + mealFlight.Id +
                " meals exactly once; cart cleared for reuse; host score=" + game.Sim.Score + ".");
            Signal("s3-host-delivered");
            SetStage(9, now);
            return;
        }

        if (stage == 9)
        {
            if (!HasSignal("s3-client-verified")) return;
            state.Room.CloseRoom();
            report.AppendLine("PASS Meal session host sent the real close control after dual-end verification.");
            Signal("s3-host-close-sent");
            SetStage(10, now);
            return;
        }

        if (stage == 10)
        {
            if (!HasSignal("s3-client-returned")) return;
            Complete(false, "Session 3 host verified the client meal flow (order, output block, take, deliver, cart reuse) with dual-end same-moment captures.");
        }
    }

    static void AdvanceClientMeal(double now, float dt)
    {
        if (stage == 0)
        {
            Require(state.Room.SocketFactory == null, "Meal client RoomManager is not using its real UDP socket factory.");
            Require(state.CanPlayWithOthers, "Meal client authentication did not pass multiplayer eligibility.");
            Require(state.Room.Join(joinAddress, "NetplayClient3", FakeAuthToken()), "Meal Room.Join failed for " + joinAddress + ".");
            state.Room.RoomName = "NetPlay Meal";
            report.AppendLine("PASS Meal session client called the real Room.Join for " + joinAddress + ".");
            SetStage(1, now);
            return;
        }

        if (stage == 1)
        {
            if (state.Room.Phase != RoomPhase.Starting && state.Room.Phase != RoomPhase.Playing) return;
            if (!game || !game.Started || !game.Remote) return;
            if (game.Snaps == null || game.Snaps.Count < 2) return;
            if (!HasSignal("s3-host-meal-station")) return;
            Require(game.Sim.Flights.Count > 0, "Client mirror has no practice flights.");
            Flight servicing = FirstServicingFlight();
            Require(servicing != null, "Client mirror shows no servicing flight.");
            report.AppendLine("PASS Client read-only mirror shows practice flight " + servicing.Id +
                " at stand " + servicing.Stand + " before ordering (dual-end flight board parity).");
            SetStage(2, now);
            return;
        }

        if (stage == 2)
        {
            if (!mealOrderSent)
            {
                game.SetInput(0, new CrewInput { Pressed = true });
                stepOverride = PickupProbeDelta;
                mealOrderSent = true;
                report.AppendLine("Probe: one Pressed=true Remote step queued for the meal order.");
                return;
            }
            if (game.Shift.Meal != MealPhase.Ordered) return;
            report.AppendLine("PASS Client's own press ordered the meal through UDP; the mirror reads back Ordered.");
            Signal("s3-client-ordered");
            SetStage(3, now);
            return;
        }

        if (stage == 3)
        {
            if (game.Shift.Meal != MealPhase.Ready) return;
            if (!HasSignal("s3-host-ready-captured")) return;
            Require(game.Shift.MirrorElapsed > 1f,
                "Client mirror elapsed did not advance for the practice HUD timer: " + game.Shift.MirrorElapsed);
            Shot("Evidence/m3-meal-ready-client.png", 1920, 1080);
            report.AppendLine("PASS Client captured the same meal-ready moment; MirrorElapsed=" +
                game.Shift.MirrorElapsed.ToString("0.0") + " MirrorScore=" + game.Shift.MirrorScore + ".");
            Signal("s3-client-ready-captured");
            SetStage(4, now);
            return;
        }

        if (stage == 4)
        {
            if (!HasSignal("s3-host-cart-ready")) return;
            if (!mealGrabSent)
            {
                game.SetInput(0, new CrewInput { Pressed = true });
                stepOverride = PickupProbeDelta;
                mealGrabSent = true;
                return;
            }
            if (game.Crew[1].Cart != game.Carts[0] || game.Carts[0].Owner != game.Crew[1]) return;
            report.AppendLine("PASS Client snapshot confirms seat 1 owns the meal cart after the grab edge.");
            Signal("s3-client-grabbed");
            SetStage(5, now);
            return;
        }

        if (stage == 5)
        {
            if (!mealTakeSent)
            {
                game.SetInput(0, new CrewInput { Pressed = true });
                stepOverride = PickupProbeDelta;
                mealTakeSent = true;
                return;
            }
            if (game.Shift.Meal == MealPhase.Ready || !game.Shift.Carts[0].Loaded) return;
            mealCargoFlightId = game.Shift.Carts[0].CargoFlightId;
            Require(mealCargoFlightId.Length > 0, "Mirror cargo has no flight binding after take-meal.");
            report.AppendLine("PASS Client mirror shows meal cargo bound to " + mealCargoFlightId + " (cargo→flightId).");
            SetStage(6, now);
            return;
        }

        if (stage == 6)
        {
            if (!HasSignal("s3-host-at-dock")) return;
            game.SetInput(0, new CrewInput { Held = true });
            float work = game.Shift.Carts[0].DeliverWork;
            if (work < 0.2f) return;
            Shot("Evidence/m3-meal-deliver-client.png", 1920, 1080);
            report.AppendLine("PASS Client captured the delivery read-out at mirror work=" + work.ToString("0.00") + ".");
            Signal("s3-client-deliver-captured");
            SetStage(7, now);
            return;
        }

        if (stage == 7)
        {
            if (game.Shift.Carts[0].CargoFlightId.Length != 0)
            {
                game.SetInput(0, new CrewInput { Held = true });
                return;
            }
            game.SetInput(0, new CrewInput());
            Require(game.Shift.MirrorScore >= 50, "Client mirror score did not record the delivery: " + game.Shift.MirrorScore);
            Flight delivered = FindMirrorFlight(mealCargoFlightId);
            Require(delivered != null && delivered.Progress[0] >= 1f,
                "Client mirror flight progress did not reach 1 for " + mealCargoFlightId + ".");
            report.AppendLine("PASS Client mirror confirms " + mealCargoFlightId +
                " meals completed exactly once; cart cleared; MirrorScore=" + game.Shift.MirrorScore +
                " MirrorElapsed=" + game.Shift.MirrorElapsed.ToString("0.0") + ".");
            Signal("s3-client-verified");
            SetStage(8, now);
            return;
        }

        if (stage == 8)
        {
            if (!HasSignal("s3-host-close-sent")) return;
            if (SceneManager.GetActiveScene().name != AppState.SceneCabinLobby) return;
            Require(UnityEngine.Object.FindAnyObjectByType<LobbyApp>() != null, "Meal client returned to CabinLobby without rebuilding LobbyApp.");
            report.AppendLine("PASS Meal session client returned to CabinLobby after the host close control.");
            Signal("s3-client-returned");
            Complete(false, "Session 3 client ran the full meal flow over UDP with mirror-consistent task feedback.");
        }
    }

    static void ConfigureMealFixtures()
    {
        for (int i = 0; i < game.Carts.Count; i++)
        {
            game.Shift.Carts[i].Clear();
            game.Carts[i].Owner = null;
            game.Crew[0].Cart = null;
            game.Crew[1].Cart = null;
            if (game.Carts[i].Cargo) game.Carts[i].Cargo.gameObject.SetActive(false);
        }
        Crew client = game.Crew[1];
        client.Selected = game.Sim.Flights.IndexOf(mealFlight);
        Vector3 nearStation = AirportGame.Station(ServiceKind.Meals) + new Vector3(0.9f, 0, -0.9f);
        client.Position = nearStation;
        client.Visual.position = nearStation;
        // 餐车留在自然车位（点按半径外）：第一次点按下单而非抓车。
        Cart mealCart = game.Carts[0];
        mealCart.Position = Level1Map.CartPark(ServiceKind.Meals);
        mealCart.Visual.position = mealCart.Position;
    }

    // ---------- M3.2 行李剧本（session 4，真实 UDP press/held + 双端截图） ----------

    static void AdvanceHostBaggage(double now, float dt)
    {
        if (stage == 0)
        {
            Require(SceneManager.GetActiveScene().name == AppState.SceneCabinLobby, "Baggage host did not start in CabinLobby.");
            Require(state.Room.SocketFactory == null, "Baggage host RoomManager is not using its real UDP socket factory.");
            Require(state.CanPlayWithOthers, "Baggage host authentication did not pass multiplayer eligibility.");
            Require(state.Room.Host("NetPlay Baggage", "NetplayHost4", FakeAuthToken()), "Baggage Room.Host failed.");
            Signal("s4-host-ready");
            report.AppendLine("PASS Session 4 host bound a real UDP socket for the baggage scenario.");
            SetStage(1, now);
            return;
        }

        if (stage == 1)
        {
            if (!state.Room.Seats[1].Occupied) return;
            LobbyApp lobby = UnityEngine.Object.FindAnyObjectByType<LobbyApp>();
            Require(lobby != null, "Baggage CabinLobby LobbyApp was not available to start the room.");
            lobby.BeginPracticeShift();
            Require(state.Launch.Mode == AppState.GameMode.HostSandbox, "Baggage lobby did not launch HostSandbox.");
            SetStage(2, now);
            return;
        }

        if (stage == 2)
        {
            if (!game || !game.Started) return;
            Require(SceneManager.GetActiveScene().name == AppState.ScenePalmBay, "Baggage host did not load PalmBay.");
            Require(game.Sandbox && !game.Remote && game.Sim != null && game.Sim.Endless,
                "Baggage host scene did not enter endless practice mode.");
            if (!TryFindReadyBaggageFlights(out baggageFlightA, out baggageFlightB)) return;
            Require(baggageFlightA.Id != baggageFlightB.Id && baggageFlightA.Stand != baggageFlightB.Stand,
                "Baggage test flights must occupy distinct stands.");
            Require(!baggageFlightA.ArrivalBagsReturned && !baggageFlightB.ArrivalBagsReturned,
                "Baggage fixture flights unexpectedly returned their arrival bags already.");
            for (int i = 0; i < game.Shift.Carts.Length; i++)
                Require(!game.Shift.Carts[i].Loaded, "Baggage session started with loaded domain cargo on cart " + i + ".");
            for (int i = 0; i < game.Carts.Count; i++)
                Require(game.Carts[i].Owner == null, "Baggage session started with an owned cart " + i + ".");

            baggageScoreBefore = game.Sim.Score;
            game.Crew[1].Selected = game.Sim.Flights.IndexOf(baggageFlightA);
            Cart baggageCart = game.Carts[(int)ServiceKind.Baggage];
            baggageCart.Position = Level1Map.CartPark(ServiceKind.Baggage);
            baggageCart.Visual.position = baggageCart.Position;
            Vector3 clientStart = baggageCart.Position + new Vector3(.45f, 0f, .2f);
            game.Crew[1].Position = clientStart;
            game.Crew[1].Visual.position = clientStart;
            report.AppendLine("PASS Practice scheduler supplied flight A=" + baggageFlightA.Id + " at stand " + baggageFlightA.Stand +
                " and flight B=" + baggageFlightB.Id + " at stand " + baggageFlightB.Stand +
                "; fixtures placed the client beside the baggage cart.");
            Signal("s4-host-baggage-ready");
            SetStage(3, now);
            return;
        }

        if (stage == 3)
        {
            if (!ClientOwnsBaggageCart()) return;
            report.AppendLine("PASS Client Pressed input grabbed the baggage cart through host Interact after UDP delivery.");
            PlaceClientAndBaggageCart(AirportGame.Dock(baggageFlightA.Stand) + new Vector3(.4f, 0f, .4f));
            Signal("s4-host-at-arrival-dock");
            SetStage(4, now);
            return;
        }

        if (stage == 4)
        {
            ShiftCartSnapGuard();
            ShiftCart baggage = game.Shift.Carts[(int)ServiceKind.Baggage];
            if (!baggage.Arrival || baggage.CargoFlightId != baggageFlightA.Id) return;
            report.AppendLine("PASS Client Pressed input unloaded " + baggageFlightA.Id + " arrival bags at its stand through UDP.");
            PlaceClientAndBaggageCart(AirportGame.Station(ServiceKind.Baggage));
            Signal("s4-host-at-return-station");
            SetStage(5, now);
            return;
        }

        if (stage == 5)
        {
            ShiftCart baggage = game.Shift.Carts[(int)ServiceKind.Baggage];
            if (baggage.Arrival || baggage.Loaded) return;
            Require(baggageFlightA.ArrivalBagsReturned, "Arrival bags were cleared without marking flight A returned.");
            Require(baggageFlightA.Progress[(int)ServiceKind.Baggage] == 0f,
                "Returning arrival bags incorrectly advanced flight A baggage progress.");
            game.Crew[1].Selected = game.Sim.Flights.IndexOf(baggageFlightA);
            report.AppendLine("PASS Client returned the arrival baggage through the station Pressed edge; flight A now permits outgoing bags.");
            Signal("s4-host-ready-load-a");
            SetStage(6, now);
            return;
        }

        if (stage == 6)
        {
            ShiftCart baggage = game.Shift.Carts[(int)ServiceKind.Baggage];
            if (!baggage.Loaded) return;
            Require(baggage.CargoFlightId == baggageFlightA.Id && !baggage.Arrival,
                "Client did not load flight A outgoing baggage after returning arrivals.");
            Require(ClientOwnsBaggageCart(), "Client lost the baggage cart after loading flight A.");
            report.AppendLine("PASS Client Pressed input loaded outgoing baggage for flight A=" + baggageFlightA.Id + ".");
            PlaceClientAndBaggageCart(AirportGame.Dock(baggageFlightA.Stand) + new Vector3(.4f, 0f, .4f));
            Signal("s4-host-at-cancel-dock");
            SetStage(7, now);
            return;
        }

        if (stage == 7)
        {
            ShiftCart baggage = game.Shift.Carts[(int)ServiceKind.Baggage];
            if (!HasSignal("s4-client-cancel-started")) return;
            if (baggage.DeliverWork > .001f) return;
            Require(baggage.Loaded && baggage.CargoFlightId == baggageFlightA.Id && !baggage.Arrival,
                "Canceling baggage delivery did not preserve flight A cargo.");
            Require(baggageFlightA.Progress[(int)ServiceKind.Baggage] == 0f,
                "Canceled baggage delivery advanced flight A progress.");
            report.AppendLine("PASS Client released Held after a partial A delivery; host EndFrame reset DeliverWork to 0 and preserved cargo.");
            PlaceClientAndBaggageCart(AirportGame.Dock(baggageFlightB.Stand) + new Vector3(.4f, 0f, .4f));
            Signal("s4-host-at-wrong-dock");
            SetStage(8, now);
            return;
        }

        if (stage == 8)
        {
            ShiftCart baggage = game.Shift.Carts[(int)ServiceKind.Baggage];
            string failedKey = baggageFlightB.Id + ":" + (int)ServiceKind.Baggage;
            if (!game.Shift.Failed.Contains(failedKey)) return;
            Require(!game.Shift.Failed.Contains(baggageFlightA.Id + ":" + (int)ServiceKind.Baggage),
                "Wrong delivery incorrectly failed the source flight A baggage task.");
            Require(!baggage.Loaded && string.IsNullOrEmpty(baggage.CargoFlightId),
                "Wrongly delivered baggage was not consumed from the cart.");
            Require(baggageFlightA.Progress[(int)ServiceKind.Baggage] == 0f &&
                baggageFlightB.Progress[(int)ServiceKind.Baggage] == 0f,
                "Wrong delivery advanced a flight's baggage progress.");
            Require(HasBaggageFailureAttribution(game.Toast),
                "Host toast does not attribute the failed receipt to receiver " + baggageFlightB.Id +
                " and source " + baggageFlightA.Id + ": " + game.Toast);
            Shot("Evidence/m3-baggage-wrong-host.png", 1920, 1080);
            report.AppendLine("PASS Host captured wrong-delivery failure " + failedKey + " with consumed cargo and visible source/receiver attribution.");
            Signal("s4-host-wrong-captured");
            SetStage(9, now);
            return;
        }

        if (stage == 9)
        {
            if (!HasSignal("s4-client-wrong-captured")) return;
            Require(game.Shift.Failed.Contains(baggageFlightB.Id + ":" + (int)ServiceKind.Baggage),
                "Receiver flight B failure disappeared before the reload test.");
            game.Crew[1].Selected = game.Sim.Flights.IndexOf(baggageFlightA);
            PlaceClientAndBaggageCart(AirportGame.Station(ServiceKind.Baggage));
            Signal("s4-host-resupply-a");
            SetStage(10, now);
            return;
        }

        if (stage == 10)
        {
            ShiftCart baggage = game.Shift.Carts[(int)ServiceKind.Baggage];
            if (!baggage.Loaded) return;
            Require(baggage.CargoFlightId == baggageFlightA.Id && !baggage.Arrival,
                "Flight A could not be reloaded after flight B failed.");
            Require(game.Shift.Failed.Contains(baggageFlightB.Id + ":" + (int)ServiceKind.Baggage) &&
                !game.Shift.Failed.Contains(baggageFlightA.Id + ":" + (int)ServiceKind.Baggage),
                "Reloading flight A changed the permanent receiver failure attribution.");
            report.AppendLine("PASS Source flight A was unaffected and its outgoing baggage could be loaded again while flight B stayed Failed.");
            PlaceClientAndBaggageCart(AirportGame.Dock(baggageFlightA.Stand) + new Vector3(.4f, 0f, .4f));
            Signal("s4-host-at-correct-dock");
            SetStage(11, now);
            return;
        }

        if (stage == 11)
        {
            if (baggageFlightA.Progress[(int)ServiceKind.Baggage] < 1f) return;
            ShiftCart baggage = game.Shift.Carts[(int)ServiceKind.Baggage];
            Require(!baggage.Loaded && string.IsNullOrEmpty(baggage.CargoFlightId),
                "Correctly delivered flight A baggage did not clear the reusable cart.");
            Require(game.Shift.Failed.Contains(baggageFlightB.Id + ":" + (int)ServiceKind.Baggage),
                "Flight B baggage failure did not remain permanent after A was delivered.");
            Require(game.Sim.Score == baggageScoreBefore + 50,
                "Correctly delivering flight A baggage did not award exactly one task score; score=" + game.Sim.Score + ".");
            Shot("Evidence/m3-baggage-deliver-host.png", 1920, 1080);
            report.AppendLine("PASS Host captured normal baggage delivery: flight A reached 1 once, the cart cleared, and flight B remained Failed.");
            Signal("s4-host-correct-captured");
            SetStage(12, now);
            return;
        }

        if (stage == 12)
        {
            if (!HasSignal("s4-client-delivery-verified")) return;
            state.Room.CloseRoom();
            report.AppendLine("PASS Session 4 host sent the real close control after both failure and delivery captures were confirmed.");
            Signal("s4-host-close-sent");
            SetStage(13, now);
            return;
        }

        if (stage == 13)
        {
            if (!HasSignal("s4-client-returned")) return;
            Complete(false, "Session 4 verified arrival-bag return, outgoing load, canceled delivery reset, permanent wrong-receiver failure, source reload, and correct delivery over real client UDP input.");
        }
    }

    static void AdvanceClientBaggage(double now, float dt)
    {
        // ApplyMirror reconstructs Flight objects on each snapshot; retain stable IDs and
        // refresh object references before making assertions against current mirror state.
        if (stage >= 1 && stage <= 9 && !string.IsNullOrEmpty(baggageFlightAId))
        {
            baggageFlightA = FindMirrorFlight(baggageFlightAId);
            baggageFlightB = FindMirrorFlight(baggageFlightBId);
            if (baggageFlightA == null || baggageFlightB == null) return;
        }

        if (stage == 0)
        {
            Require(state.Room.SocketFactory == null, "Baggage client RoomManager is not using its real UDP socket factory.");
            Require(state.CanPlayWithOthers, "Baggage client authentication did not pass multiplayer eligibility.");
            Require(state.Room.Join(joinAddress, "NetplayClient4", FakeAuthToken()), "Baggage Room.Join failed for " + joinAddress + ".");
            state.Room.RoomName = "NetPlay Baggage";
            report.AppendLine("PASS Session 4 client called the real Room.Join for " + joinAddress + ".");
            SetStage(1, now);
            return;
        }

        if (stage == 1)
        {
            if (state.Room.Phase != RoomPhase.Starting && state.Room.Phase != RoomPhase.Playing) return;
            if (!game || !game.Started || !game.Remote || game.Snaps == null || game.Snaps.Count < 2) return;
            if (!HasSignal("s4-host-baggage-ready")) return;
            if (!TryFindReadyBaggageFlights(out baggageFlightA, out baggageFlightB)) return;
            Require(baggageFlightA.Id != baggageFlightB.Id, "Client mirror did not provide two distinct baggage flights.");
            baggageFlightAId = baggageFlightA.Id;
            baggageFlightBId = baggageFlightB.Id;
            Require(!baggageFlightA.ArrivalBagsReturned, "Client mirror shows arrival baggage already returned for flight A.");
            report.AppendLine("PASS Read-only client mirror shows flight A=" + baggageFlightA.Id + " and receiver flight B=" + baggageFlightB.Id + " before the first input.");
            SetStage(2, now);
            return;
        }

        if (stage == 2)
        {
            if (!baggageGrabSent)
            {
                if (Vector3.Distance(game.Crew[1].Position, Level1Map.CartPark(ServiceKind.Baggage)) > 1.9f) return;
                game.SetInput(0, new CrewInput { Pressed = true });
                stepOverride = PickupProbeDelta;
                baggageGrabSent = true;
                report.AppendLine("Probe: one client Pressed=true Remote step queued to grab the baggage cart.");
                return;
            }
            if (!HasSignal("s4-host-at-arrival-dock") || !ClientOwnsBaggageCart()) return;
            baggagePickupSent = false;
            SetStage(3, now);
            return;
        }

        if (stage == 3)
        {
            if (!baggagePickupSent)
            {
                if (!HasSignal("s4-host-at-arrival-dock") || !ClientOwnsBaggageCart()) return;
                if (Vector3.Distance(game.Crew[1].Position, AirportGame.Dock(baggageFlightA.Stand)) > 1.5f) return;
                game.SetInput(0, new CrewInput { Pressed = true });
                stepOverride = PickupProbeDelta;
                baggagePickupSent = true;
                report.AppendLine("Probe: client Pressed=true Remote step queued at flight A's dock to pick up arrival baggage.");
                return;
            }
            // The host moves the crew/cart to the baggage station as soon as the
            // pickup press lands. After sending the dock press, wait for that new
            // fixture signal and its mirrored state instead of requiring the old dock.
            if (!HasSignal("s4-host-at-return-station") || !ClientOwnsBaggageCart()) return;
            ShiftCart baggage = game.Shift.Carts[(int)ServiceKind.Baggage];
            if (!baggage.Arrival || baggage.CargoFlightId != baggageFlightA.Id ||
                Vector3.Distance(game.Crew[1].Position, AirportGame.Station(ServiceKind.Baggage)) > 1.5f) return;
            SetStage(4, now);
            return;
        }

        if (stage == 4)
        {
            if (!baggageReturnSent)
            {
                game.SetInput(0, new CrewInput { Pressed = true });
                stepOverride = PickupProbeDelta;
                baggageReturnSent = true;
                report.AppendLine("Probe: client Pressed=true Remote step queued at the baggage station to return arrival bags.");
                return;
            }
            if (!HasSignal("s4-host-ready-load-a")) return;
            if (!baggageFlightA.ArrivalBagsReturned || game.Shift.Carts[(int)ServiceKind.Baggage].Loaded ||
                Vector3.Distance(game.Crew[1].Position, AirportGame.Station(ServiceKind.Baggage)) > 1.5f) return;
            SetStage(5, now);
            return;
        }

        if (stage == 5)
        {
            if (!baggageLoadSent)
            {
                game.SetInput(0, new CrewInput { Pressed = true });
                stepOverride = PickupProbeDelta;
                baggageLoadSent = true;
                report.AppendLine("Probe: client Pressed=true Remote step queued to load flight A outgoing baggage.");
                return;
            }
            if (!HasSignal("s4-host-at-cancel-dock")) return;
            ShiftCart baggage = game.Shift.Carts[(int)ServiceKind.Baggage];
            if (!baggage.Loaded || baggage.CargoFlightId != baggageFlightA.Id || baggage.Arrival ||
                Vector3.Distance(game.Crew[1].Position, AirportGame.Dock(baggageFlightA.Stand)) > 1.5f) return;
            SetStage(6, now);
            return;
        }

        if (stage == 6)
        {
            if (!HasSignal("s4-host-at-cancel-dock")) return;
            if (!baggageCancelStarted)
            {
                game.SetInput(0, new CrewInput { Pressed = true, Held = true });
                baggageCancelStarted = true;
                report.AppendLine("Probe: client started the A delivery readout with Pressed+Held over Remote UDP.");
                return;
            }
            float work = game.Shift.Carts[(int)ServiceKind.Baggage].DeliverWork;
            if (work < .25f)
            {
                game.SetInput(0, new CrewInput { Held = true });
                return;
            }
            game.SetInput(0, new CrewInput());
            signalAfterStep = "s4-client-cancel-started";
            report.AppendLine("PASS Client mirror saw partial A delivery work=" + work.ToString("0.00") + "; the client sent a neutral frame to cancel.");
            SetStage(7, now);
            return;
        }

        if (stage == 7)
        {
            if (!HasSignal("s4-host-at-wrong-dock")) return;
            ShiftCart baggage = game.Shift.Carts[(int)ServiceKind.Baggage];
            if (!baggageWrongStarted)
            {
                if (!baggage.Loaded || baggage.CargoFlightId != baggageFlightA.Id || baggage.DeliverWork != 0f ||
                    Vector3.Distance(game.Crew[1].Position, AirportGame.Dock(baggageFlightB.Stand)) > 1.5f) return;
                game.SetInput(0, new CrewInput { Pressed = true, Held = true });
                baggageWrongStarted = true;
                report.AppendLine("Probe: client started a held delivery at receiver flight B's dock with Pressed+Held over Remote UDP.");
                return;
            }
            if (!game.Shift.Failed.Contains(baggageFlightB.Id + ":" + (int)ServiceKind.Baggage))
            {
                game.SetInput(0, new CrewInput { Held = true });
                return;
            }
            game.SetInput(0, new CrewInput());
            Require(!game.Shift.Carts[(int)ServiceKind.Baggage].Loaded,
                "Client mirror did not show consumed cargo after wrong delivery.");
            Require(!game.Shift.Failed.Contains(baggageFlightA.Id + ":" + (int)ServiceKind.Baggage),
                "Client mirror incorrectly failed source flight A.");
            Require(HasBaggageFailureAttribution(game.Toast),
                "Client toast does not mirror the failure attribution: " + game.Toast);
            if (!HasSignal("s4-host-wrong-captured")) return;
            Shot("Evidence/m3-baggage-wrong-client.png", 1920, 1080);
            report.AppendLine("PASS Client mirror and toast show receiver key " + baggageFlightB.Id + ":1, consumed A cargo, and the same source/receiver attribution.");
            Signal("s4-client-wrong-captured");
            SetStage(8, now);
            return;
        }

        if (stage == 8)
        {
            if (!HasSignal("s4-host-resupply-a")) return;
            Require(game.Shift.Failed.Contains(baggageFlightB.Id + ":" + (int)ServiceKind.Baggage),
                "Client mirror lost receiver flight B failure before source reload.");
            if (!baggageReloadSent)
            {
                if (game.Shift.Carts[(int)ServiceKind.Baggage].Loaded ||
                    Vector3.Distance(game.Crew[1].Position, AirportGame.Station(ServiceKind.Baggage)) > 1.5f) return;
                game.SetInput(0, new CrewInput { Pressed = true });
                stepOverride = PickupProbeDelta;
                baggageReloadSent = true;
                report.AppendLine("Probe: client Pressed=true Remote step queued to reload flight A from the baggage station.");
                return;
            }
            if (!HasSignal("s4-host-at-correct-dock")) return;
            ShiftCart baggage = game.Shift.Carts[(int)ServiceKind.Baggage];
            if (!baggage.Loaded || baggage.CargoFlightId != baggageFlightA.Id || baggage.Arrival ||
                Vector3.Distance(game.Crew[1].Position, AirportGame.Dock(baggageFlightA.Stand)) > 1.5f) return;
            SetStage(9, now);
            return;
        }

        if (stage == 9)
        {
            if (!HasSignal("s4-host-at-correct-dock")) return;
            if (!baggageCorrectStarted)
            {
                game.SetInput(0, new CrewInput { Pressed = true, Held = true });
                baggageCorrectStarted = true;
                report.AppendLine("Probe: client started the correct A baggage delivery with Pressed+Held over Remote UDP.");
                return;
            }
            if (baggageFlightA.Progress[(int)ServiceKind.Baggage] < 1f ||
                game.Shift.Carts[(int)ServiceKind.Baggage].Loaded)
            {
                game.SetInput(0, new CrewInput { Held = true });
                return;
            }
            game.SetInput(0, new CrewInput());
            Require(game.Shift.Failed.Contains(baggageFlightB.Id + ":" + (int)ServiceKind.Baggage),
                "Client mirror shows flight B failure was not permanent through A's correct delivery.");
            if (!HasSignal("s4-host-correct-captured")) return;
            Shot("Evidence/m3-baggage-deliver-client.png", 1920, 1080);
            report.AppendLine("PASS Client captured normal flight A baggage delivery with flight B's permanent failure still mirrored.");
            Signal("s4-client-delivery-verified");
            SetStage(10, now);
            return;
        }

        if (stage == 10)
        {
            if (!HasSignal("s4-host-close-sent")) return;
            if (SceneManager.GetActiveScene().name != AppState.SceneCabinLobby) return;
            Require(UnityEngine.Object.FindAnyObjectByType<LobbyApp>() != null,
                "Baggage client returned to CabinLobby without rebuilding LobbyApp.");
            report.AppendLine("PASS Session 4 client returned to CabinLobby after the host close control.");
            Signal("s4-client-returned");
            Complete(false, "Session 4 client completed arrival return, cargo reload, canceled/held delivery inputs, wrong-receiver failure, and correct delivery through real UDP input and snapshots.");
        }
    }

    static bool TryFindReadyBaggageFlights(out Flight first, out Flight second)
    {
        first = null;
        second = null;
        if (game == null || game.Sim == null) return false;
        for (int i = 0; i < game.Sim.Flights.Count; i++)
        {
            Flight flight = game.Sim.Flights[i];
            if (flight == null || flight.Status != FlightStatus.Servicing || !game.StandReady(flight.Stand)) continue;
            if (first == null) first = flight;
            else if (flight.Id != first.Id)
            {
                second = flight;
                return true;
            }
        }
        return false;
    }

    static void PlaceClientAndBaggageCart(Vector3 position)
    {
        Crew client = game.Crew[1];
        Cart cart = game.Carts[(int)ServiceKind.Baggage];
        client.Position = position;
        client.Visual.position = position;
        cart.Position = position;
        cart.Visual.position = position;
    }

    static bool ClientOwnsBaggageCart()
    {
        return game != null && game.Crew.Count > 1 && game.Carts.Count > (int)ServiceKind.Baggage &&
            game.Crew[1].Cart == game.Carts[(int)ServiceKind.Baggage] &&
            game.Carts[(int)ServiceKind.Baggage].Owner == game.Crew[1];
    }

    static bool HasBaggageFailureAttribution(string text)
    {
        return !string.IsNullOrEmpty(text) && baggageFlightA != null && baggageFlightB != null &&
            text.IndexOf(baggageFlightA.Id, StringComparison.Ordinal) >= 0 &&
            text.IndexOf(baggageFlightB.Id, StringComparison.Ordinal) >= 0 &&
            text.IndexOf("行李", StringComparison.Ordinal) >= 0 &&
            text.IndexOf("失败", StringComparison.Ordinal) >= 0;
    }

    static void ShiftCartSnapGuard()
    {
        Require(game != null && game.Shift != null && game.Shift.Carts.Length > (int)ServiceKind.Baggage,
            "Baggage ShiftSim cart snapshot was unavailable.");
    }

    static Flight FirstServicingFlight()
    {
        if (game == null || game.Sim == null) return null;
        for (int i = 0; i < game.Sim.Flights.Count; i++)
        {
            if (game.Sim.Flights[i].Status == FlightStatus.Servicing) return game.Sim.Flights[i];
        }
        return null;
    }

    static Flight FindMirrorFlight(string flightId)
    {
        if (game == null || game.Sim == null || string.IsNullOrEmpty(flightId)) return null;
        for (int i = 0; i < game.Sim.Flights.Count; i++)
        {
            if (game.Sim.Flights[i].Id == flightId) return game.Sim.Flights[i];
        }
        return null;
    }

    static void Shot(string path, int width, int height)
    {
        if (game == null || !game.View) throw new Exception("NetPlay capture has no game camera.");
        AirportHudCanvas hud = UnityEngine.Object.FindAnyObjectByType<AirportHudCanvas>();
        // 播放器循环可能停摆，HUD 的 LateUpdate 不保证执行，Render 前手动刷新（M1 惯例）。
        if (hud != null) hud.RefreshNow();
        Canvas.ForceUpdateCanvases();
        Camera cam = game.View;
        var rt = new RenderTexture(width, height, 24);
        var oldTarget = cam.targetTexture;
        float oldAspect = cam.aspect;
        cam.targetTexture = rt;
        cam.aspect = width / (float)height;
        Level1Map.ConfigureCamera(cam, false);
        Canvas.ForceUpdateCanvases();
        cam.Render();
        RenderTexture.active = rt;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        image.Apply();
        Directory.CreateDirectory("Evidence");
        File.WriteAllBytes(path, image.EncodeToPNG());
        RenderTexture.active = null;
        cam.targetTexture = oldTarget;
        cam.aspect = oldAspect;
        Level1Map.ConfigureCamera(cam, false);
        UnityEngine.Object.DestroyImmediate(image);
        UnityEngine.Object.DestroyImmediate(rt);
        report.AppendLine(path + "  " + width + "x" + height);
    }

    static bool ClientOwnsCartZero()
    {
        return game != null && game.Crew.Count > 1 && game.Carts.Count > 0 &&
            game.Crew[1].Cart == game.Carts[0] && game.Carts[0].Owner == game.Crew[1];
    }

    static string FakeAuthToken()
    {
        Require(state != null && state.IsFakeAuth && state.Auth is FakeAuthService &&
            state.Auth.User != null && !string.IsNullOrEmpty(state.Auth.User.IdToken),
            "Expected a signed-in FakeAuthService user before joining a room.");
        return state.Auth.User.IdToken;
    }

    static bool HostOwnsCartZeroOnly()
    {
        return game != null && game.Crew.Count > 1 && game.Carts.Count > 0 &&
            game.Crew[0].Cart == game.Carts[0] && game.Carts[0].Owner == game.Crew[0] &&
            game.Crew[1].Cart != game.Carts[0];
    }

    static bool HostSeatOwnsCartZeroSnapshot()
    {
        return game != null && game.Crew.Count > 1 && game.Carts.Count > 0 &&
            game.Crew[0].Cart == game.Carts[0] && game.Carts[0].Owner == game.Crew[0] &&
            game.Crew[1].Cart != game.Carts[0];
    }

    static bool CartZeroUnowned()
    {
        return game != null && game.Crew.Count > 1 && game.Carts.Count > 0 &&
            game.Carts[0].Owner == null && game.Crew[0].Cart != game.Carts[0] &&
            game.Crew[1].Cart != game.Carts[0];
    }

    static void CloseClientSocketAbruptly()
    {
        FieldInfo socketField = typeof(RoomManager).GetField("_socket", BindingFlags.Instance | BindingFlags.NonPublic);
        if (socketField == null) throw new MissingFieldException(typeof(RoomManager).FullName, "_socket");
        IUdpSocket socket = socketField.GetValue(state.Room) as IUdpSocket;
        if (!(socket is UdpSocketReal))
            throw new Exception("Expected the client's real UdpSocketReal before abrupt disconnect.");
        socket.Close();
    }

    static void OnRuntimeLog(string message, string stack, LogType kind)
    {
        if (kind != LogType.Error && kind != LogType.Exception) return;
        runtimeFault = true;
        if (runtimeErrors.Count < 12)
            runtimeErrors.Add(kind + ": " + message + (string.IsNullOrEmpty(stack) ? string.Empty : "\n" + stack));
    }

    static void Complete(bool failed, string message)
    {
        if (completionWritten) return;
        completionWritten = true;
        failed |= runtimeFault;
        Application.logMessageReceived -= OnRuntimeLog;
        report.AppendLine("Runtime errors: " + runtimeErrors.Count);
        for (int i = 0; i < runtimeErrors.Count; i++) report.AppendLine(runtimeErrors[i]);
        report.AppendLine("Final: " + (failed ? "FAIL" : "PASS"));
        report.AppendLine("Details: " + message);
        report.AppendLine("Diagnostics: " + Diagnostics());
        Directory.CreateDirectory("Evidence");
        string reportPath = role == "host" ? "Evidence/net-host.txt" : "Evidence/net-client.txt";
        File.WriteAllText(reportPath, report.ToString());
        Debug.Log((failed ? "NETPLAY_FAILED " : "NETPLAY_PASSED ") + scenario + " " + message);
        SessionState.SetBool(Pending + ".failed", failed);
        SessionState.SetBool(Pending + ".stop", true);
        if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
    }

    static void RestoreAndClearPreferences()
    {
        try
        {
            ValidateIsolationOrThrow();
            ValidatePersistentDataIsolationOrThrow();
        }
        catch (Exception error)
        {
            SessionState.SetBool(Pending + ".failed", true);
            try { File.AppendAllText(role == "host" ? "Evidence/net-host.txt" : "Evidence/net-client.txt", "\nFAIL Preference and persistent-data cleanup refused by isolation guard: " + error.Message + "\n"); }
            catch { }
            return;
        }

        if (SessionState.GetBool(Pending + ".prefsCaptured", false))
        {
            for (int i = 0; i < PreferenceKeys.Length; i++)
            {
                string key = PreferenceKeys[i];
                if (SessionState.GetBool(Pending + ".prefHas." + i, false))
                    PlayerPrefs.SetString(key, SessionState.GetString(Pending + ".prefValue." + i, string.Empty));
                else
                    PlayerPrefs.DeleteKey(key);
            }
            PlayerPrefs.Save();
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();
            SessionState.SetBool(Pending + ".prefsCaptured", false);
        }

        try
        {
            string dataPath = Application.persistentDataPath;
            if (!string.IsNullOrEmpty(dataPath) && Directory.Exists(dataPath)) Directory.Delete(dataPath, true);
        }
        catch (Exception error)
        {
            SessionState.SetBool(Pending + ".failed", true);
            try { File.AppendAllText(role == "host" ? "Evidence/net-host.txt" : "Evidence/net-client.txt", "\nFAIL Could not remove isolated persistent data: " + error.Message + "\n"); }
            catch { }
        }
    }

    static void ValidateIsolationOrThrow()
    {
        string root = Environment.GetEnvironmentVariable("PALMBAY_NETPLAY_ROOT");
        string marker = Environment.GetEnvironmentVariable("PALMBAY_NETPLAY_MARKER");
        string control = Environment.GetEnvironmentVariable("PALMBAY_NETPLAY_CONTROL_DIR");
        string runTag = Environment.GetEnvironmentVariable("PALMBAY_NETPLAY_RUN_TAG");
        string roleTag = Environment.GetEnvironmentVariable("PALMBAY_NETPLAY_ROLE_TAG");
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(marker) || string.IsNullOrEmpty(control) ||
            string.IsNullOrEmpty(runTag) || string.IsNullOrEmpty(roleTag))
            throw new InvalidOperationException("NetPlaytest requires launcher isolation environment and refuses to touch preferences otherwise.");

        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string expectedMarker = Path.Combine(root, ".netplay-test-marker");
        string expectedControl = Path.Combine(root, "control");
        if (!string.Equals(Path.GetFullPath(marker), expectedMarker, StringComparison.Ordinal) ||
            !string.Equals(Path.GetFullPath(control), expectedControl, StringComparison.Ordinal))
            throw new InvalidOperationException("NetPlaytest marker/control paths do not match the isolated temp root.");
        if (!Path.GetFileName(root).StartsWith("palmbay-netplay.", StringComparison.Ordinal))
            throw new InvalidOperationException("NetPlaytest temp root name is not trusted.");
        if (!File.Exists(expectedMarker) || File.ReadAllText(expectedMarker) != "PALMBAY_NETPLAY_TEST:" + runTag)
            throw new InvalidOperationException("NetPlaytest temp marker is missing or does not match this run.");
        if (!Directory.Exists(expectedControl) || !Directory.Exists(root))
            throw new InvalidOperationException("NetPlaytest control directory or temp root is missing.");
        if (roleTag != "host1" && roleTag != "client1" && roleTag != "host2" && roleTag != "client2" &&
            roleTag != "host3" && roleTag != "client3" && roleTag != "host4" && roleTag != "client4" &&
            roleTag != "host5" && roleTag != "client5" && roleTag != "host6" && roleTag != "client6" &&
            roleTag != "host7" && roleTag != "client7" && roleTag != "host8" && roleTag != "client8" &&
            roleTag != "host9" && roleTag != "client9" && roleTag != "host10" && roleTag != "client10")
            throw new InvalidOperationException("NetPlaytest role identity is not recognized.");

        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string projectParent = Directory.GetParent(projectRoot).FullName;
        if (!string.Equals(projectParent, root, StringComparison.Ordinal))
            throw new InvalidOperationException("This Unity project is not a direct child of the launcher temp root.");
        string expectedCompany = "Palm Bay Netplaytest " + runTag + " " + roleTag;
        string expectedProduct = "Netplay-" + runTag + "-" + roleTag;
        if (!string.Equals(PlayerSettings.companyName, expectedCompany, StringComparison.Ordinal) ||
            !string.Equals(PlayerSettings.productName, expectedProduct, StringComparison.Ordinal))
            throw new InvalidOperationException("Project company/product identity does not match the launcher isolation tag.");
    }

    static void ValidatePersistentDataIsolationOrThrow()
    {
        string dataPath = Application.persistentDataPath;
        DirectoryInfo dataDirectory = new DirectoryInfo(dataPath);
        string company = PlayerSettings.companyName;
        string product = PlayerSettings.productName;
        if (string.IsNullOrEmpty(dataDirectory.FullName) || dataDirectory.Parent == null ||
            !string.Equals(dataDirectory.Name, product, StringComparison.Ordinal) ||
            !string.Equals(dataDirectory.Parent.Name, company, StringComparison.Ordinal))
            throw new InvalidOperationException("Application.persistentDataPath is not isolated under the test company/product identity.");
    }

    static void Signal(string name)
    {
        File.WriteAllText(Path.Combine(controlDirectory, name), "ready");
    }

    static bool HasSignal(string name)
    {
        return File.Exists(Path.Combine(controlDirectory, name));
    }

    static void SetStage(int value, double now)
    {
        stage = value;
        stageStarted = now;
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    static string Diagnostics()
    {
        string scene = SceneManager.GetActiveScene().name;
        string phase = state == null || state.Room == null ? "none" : state.Room.Phase.ToString();
        int snapshots = state == null || state.Room == null ? 0 : state.Room.SnapshotsSent;
        string gameState = game == null ? "none" : "started=" + game.Started + ",sandbox=" + game.Sandbox + ",remote=" + game.Remote;
        string hostCrew = game == null || game.Crew.Count < 2 ? "none" :
            "p0=" + game.Crew[0].Position + ",p1=" + game.Crew[1].Position + ",cart0=" +
            (game.Carts.Count == 0 || game.Carts[0].Owner == null ? "none" : game.Carts[0].Owner.Index.ToString());
        return "scenario=" + scenario + ",stage=" + stage + ",scene=" + scene + ",room=" + phase +
            ",snapshots=" + snapshots + ",game=" + gameState + ",actors=" + hostCrew +
            ",signals=" + (string.IsNullOrEmpty(controlDirectory) ? "none" : controlDirectory) +
            (scenario != null && scenario.EndsWith("-fuel", StringComparison.Ordinal) ? FuelDiagnostics() : string.Empty) +
            (scenario != null && scenario.EndsWith("-boarding", StringComparison.Ordinal) ? BoardingDiagnostics() : string.Empty) +
            (game != null && game.NetworkShift ? ",round=" + game.RoundId + ",localSeat=" + game.LocalSeat +
                ",elapsed=" + game.DisplayElapsed.ToString("R") + ",score=" + game.DisplayScore +
                ",finished=" + game.MatchFinished + ",replay=" + game.ReplayFrameCount + "/" + game.ReplayFraction +
                ",replayStatus=" + game.ReplayStatus : string.Empty);
    }
}
