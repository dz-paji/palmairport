using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using IslandAirport;

// Real UDP M4 acceptance. Domain setup is explicitly a checkpoint/result
// fixture, not evidence of a human completing all four tasks in five minutes.
public static partial class NetPlaytest
{
    static string m4Case;
    static int m4Round;
    static int m4Score;
    static float m4Elapsed;
    static Vector3 m4MoveStart;
    static Flight m4Flight;
    static bool m4FinalObserved;

    public static void RunHostM4Shift() { StartRun("host-m4-shift"); }
    public static void RunClientM4Shift() { StartRun("client-m4-shift"); }
    public static void RunHostM4Clientdrop() { StartRun("host-m4-clientdrop"); }
    public static void RunClientM4Clientdrop() { StartRun("client-m4-clientdrop"); }
    public static void RunHostM4Hostleave() { StartRun("host-m4-hostleave"); }
    public static void RunClientM4Hostleave() { StartRun("client-m4-hostleave"); }
    public static void RunHostM4Hostloss() { StartRun("host-m4-hostloss"); }
    public static void RunClientM4Hostloss() { StartRun("client-m4-hostloss"); }

    static string M4Signal(string suffix) { return "m4-" + m4Case + "-" + suffix; }
    static string M4File(string suffix) { return Path.Combine(controlDirectory, M4Signal(suffix)); }
    static void AdvanceM4(double now, float dt)
    {
        m4Case = scenario.Substring(scenario.IndexOf("-m4-", StringComparison.Ordinal) + 4);
        if (role == "host") AdvanceM4Host(now, dt);
        else AdvanceM4Client(now, dt);
    }

    static void M4Freeze(float dt)
    {
        suppressGameStep = true;
        FieldInfo field = typeof(AirportGame).GetField("netSession", BindingFlags.Instance | BindingFlags.NonPublic);
        NetSession session = field.GetValue(game) as NetSession;
        Require(session != null, "M4 freeze requires live NetSession.");
        session.StepHost(dt);
    }

    static void PrepareM4Fixture()
    {
        foreach (Crew crew in game.Crew) crew.Bot = false;
        m4Flight = game.Sim.ActiveAtStand(0);
        Require(m4Flight != null && game.StandReady(0), "Fixture requires an actually taxied aircraft.");
        if (m4Case == "shift")
        {
            Require(game.Sim.ReturnArrivalBags(m4Flight), "Result fixture arrival return.");
            for (int kind = 0; kind < 4; kind++)
                Require(game.Sim.TryAdvance(m4Flight, (ServiceKind)kind, 1f), "Result fixture completed task " + kind);
            Require(game.Sim.DepartedCount == 1 && game.Sim.Score > 0, "Nonzero result fixture must depart exactly one flight.");
            report.AppendLine("FIXTURE: one flight completed through domain progress APIs to test nonzero result/stat preservation; remaining schedule, all 300 seconds, network results and retry run normally. This is not a human gameplay completion claim.");
        }
        else
        {
            Require(game.Sim.ReturnArrivalBags(m4Flight), "Checkpoint fixture arrival return.");
            Require(game.Sim.TryAdvance(m4Flight, ServiceKind.Meals, 1), "Checkpoint meal prerequisite.");
            Require(game.Sim.TryAdvance(m4Flight, ServiceKind.Fuel, 1), "Checkpoint fuel prerequisite.");
            Require(game.Shift.OpenGate(0, m4Flight.Id), "Checkpoint opens passenger manifest.");
            Require(game.Shift.CloseGate(0, m4Flight.Id), "Checkpoint retains closed gate and released passenger.");
            Require(game.Shift.OrderMeal(1), "Checkpoint has in-progress station order.");
            Require(game.Shift.TakeNozzle(1), "Checkpoint takes unique nozzle.");
            Require(game.Shift.AttachNozzle(1, 2), "Checkpoint station attachment.");
            Require(game.Shift.SetValve(1, true), "Checkpoint starts station fill.");
            game.Shift.Tick(.6f);
            game.Shift.EndFrame();
            Require(game.Shift.SetValve(1, false), "Checkpoint closes station valve.");
            Require(game.Shift.NozzleState == NozzlePhase.AtStation, "Checkpoint valve close auto-returned the station nozzle.");
            Require(game.Shift.TakeHose(1), "Checkpoint holds the partly filled truck's own hose.");
            Require(game.Shift.TryClaimCart(0, 1), "Checkpoint gives departing host baggage cart.");
            game.Crew[0].Cart = game.Carts[1]; game.Carts[1].Owner = game.Crew[0];
            game.Crew[0].Position = game.Carts[1].Position;
            Require(game.Shift.LoadCart(0, 1, m4Flight.Id), "Checkpoint preserves real loaded cargo.");
            report.AppendLine("FIXTURE: domain APIs prepared a partial meal order, partly filled truck with its hose in seat 1's hand, loaded host-owned baggage cart, and closed gate with stable waiting/released passengers. No client snapshot or promotion state is manually replaced.");
        }
        m4Score = game.Sim.Score;
        m4Elapsed = game.Sim.Elapsed;
        m4Round = state.Room.RoundId;
        File.WriteAllText(M4File("expected"), m4Score + "\n" + m4Elapsed.ToString("R", CultureInfo.InvariantCulture) + "\n" + m4Flight.Id + "\n" + m4Round);
    }

    static void AdvanceM4Host(double now, float dt)
    {
        if (stage == 0)
        {
            Require(state.Room.SocketFactory == null, "M4 requires real UDP.");
            Require(state.Room.Host("M4 " + m4Case, "M4Host", FakeAuthToken()), "M4 Host failed.");
            Signal(M4Signal("host-ready"));
            SetStage(1, now); return;
        }
        if (stage == 1)
        {
            if (!state.Room.Seats[1].Occupied) return;
            UnityEngine.Object.FindObjectOfType<LobbyApp>().BeginShift();
            SetStage(2, now); return;
        }
        if (stage == 2)
        {
            if (game == null || !game.Started) return;
            Require(game.NetworkShift && !game.Remote && !game.Sim.Endless && !state.Room.IsPractice,
                "Production lobby must launch finite authoritative M4, not practice.");
            Require(game.Sim.Flights.Count == 5, "M4 must begin with five scheduled flights.");
            foreach (Crew crew in game.Crew) crew.Bot = false;
            stepOverride = .25f;
            if (!game.StandReady(0)) return;
            PrepareM4Fixture();
            M4Freeze(dt);
            Signal(M4Signal("fixture-ready"));
            SetStage(3, now); return;
        }
        if (stage == 3)
        {
            M4Freeze(dt);
            if (!HasSignal(M4Signal("client-mirrored"))) return;
            report.AppendLine("PASS Client mirrored finite mode, round, score and populated checkpoint before continuing.");
            if (m4Case == "hostleave")
            {
                Signal(M4Signal("host-departing"));
                game.BackToLobby();
                SetStage(9, now); return;
            }
            if (m4Case == "hostloss")
            {
                Signal(M4Signal("host-departing"));
                CloseClientSocketAbruptly(); // helper closes this process's real Room socket (host here).
                Complete(false, "Abrupt host-loss fixture closed the real socket without sending leave; client must independently timeout, restore checkpoint and finish.");
                return;
            }
            SetStage(4, now); return;
        }
        if (stage == 4)
        {
            if (m4Case == "clientdrop" && !state.Room.Seats[1].Bot)
            { M4Freeze(dt); return; }
            if (m4Case == "clientdrop" && !m4FinalObserved)
            {
                Require(state.Room.IsHost && state.Room.LocalSeat == 0 && state.Room.RoundId == m4Round,
                    "Client loss must preserve authority, seat and round.");
                Require(game.Sim.Score == m4Score && game.Sim.Elapsed >= m4Elapsed, "Client loss cannot reset checkpoint.");
                m4FinalObserved = true;
                report.AppendLine("PASS Silent client socket loss became bot takeover in the same round; host continued.");
            }
            stepOverride = .5f;
            if (!game.Sim.Finished) return;
            Require(Mathf.Abs(game.Sim.Elapsed - 300f) < .001f, "Finite match must stop exactly at 300 seconds.");
            Require(game.Sim.Score >= m4Score, "Finished match retains completed result.");
            m4Score = game.Sim.Score;
            File.WriteAllText(M4File("result"), m4Score + "\n" + game.Sim.DepartedCount + "\n" + game.DisplayStars);
            SetStage(5, now); return;
        }
        if (stage == 5)
        {
            // Continue pumping authoritative replay and live result snapshots.
            if (game.ReplayFraction < .999f) { stepOverride = .25f; return; }
            game.SetInput(0, new CrewInput { Move = Vector2.right, Pressed = true, Held = true, Cycle = true });
            game.SetInput(1, new CrewInput { Move = Vector2.left, Pressed = true, Held = true, Cycle = true });
            game.Step(.1f);
            Require(game.Sim.Elapsed == 300f && game.Sim.Score == m4Score, "Finished input cannot advance simulation or score.");
            Require(game.ReplayFrameCount > 1, "M4 replay must come from recorded match history.");
            File.WriteAllText(M4File("replay-count"), game.ReplayFrameCount.ToString());
            string[] replayHashes = new string[game.ReplayFrameCount];
            for (int i = 0; i < replayHashes.Length; i++) replayHashes[i] = game.ReplaySampleHash(i);
            File.WriteAllLines(M4File("replay-hashes"), replayHashes);
            Shot("Evidence/m4-" + m4Case + "-results-host.png", 1920, 1080);
            report.AppendLine("PASS 300-second result frozen, input ignored, actual history replay reaches final hold. frames=" + game.ReplayFrameCount);
            Signal(M4Signal("results-ready"));
            if (m4Case == "clientdrop")
            {
                Complete(false, "Host completed finite match after abrupt client disconnect, with retained checkpoint and bot takeover.");
                return;
            }
            SetStage(6, now); return;
        }
        if (stage == 6)
        {
            if (!HasSignal(M4Signal("client-results"))) return;
            game.Retry(); game.Retry();
            SetStage(7, now); return;
        }
        if (stage == 7)
        {
            if (game.RoundId == m4Round) return;
            Require(game.RoundId == m4Round + 1, "Repeated retry must create exactly one next round.");
            // Room events are pumped before AirportGame consumes its retry event.
            // Wait for the new gameplay state, not just the new room identity.
            if (game.MatchFinished || game.Sim.Score != 0) return;
            if (!HasSignal(M4Signal("client-retried"))) return;
            report.AppendLine("PASS Host retry propagated once, reset results/history and kept both seats in the same next round.");
            state.Room.CloseRoom(); Signal(M4Signal("host-close"));
            SetStage(8, now); return;
        }
        if (stage == 8)
        {
            if (!HasSignal(M4Signal("client-returned"))) return;
            Complete(false, "Finite shift, synchronized score/stars/results/history, final freeze and one authoritative retry passed.");
        }
        if (stage == 9)
        {
            if (!HasSignal(M4Signal("survivor-finished"))) return;
            Require(SceneManager.GetActiveScene().name == AppState.SceneCabinLobby,
                "Voluntarily departing host must reach lobby while survivor continues.");
            Complete(false, "Voluntary host exit handed off to surviving seat and survivor finished original match.");
        }
    }

    static void AdvanceM4Client(double now, float dt)
    {
        if (stage == 0)
        {
            Require(state.Room.Join(joinAddress, "M4Client", FakeAuthToken()), "M4 Join failed.");
            SetStage(1, now); return;
        }
        if (stage == 1)
        {
            if (game == null || !game.Started || !HasSignal(M4Signal("fixture-ready"))) return;
            if (!game.NetworkShift || !game.Remote || game.Sim == null) return;
            string[] expected = File.ReadAllLines(M4File("expected"));
            m4Score = int.Parse(expected[0]);
            m4Elapsed = float.Parse(expected[1], CultureInfo.InvariantCulture);
            m4Round = int.Parse(expected[3]);
            if (game.DisplayScore != m4Score || game.RoundId != m4Round) return;
            if (m4Case != "shift" && (!game.Shift.Carts[1].Loaded || game.Shift.FuelTruckTank <= 0f || game.Shift.Passengers.Count != 8)) return;
            Require(!game.Sim.Endless && state.Room.LocalSeat == 1, "Remote production shift must be finite at stable seat 1.");
            if (m4Case != "shift")
            {
                Require(game.Carts[1].Owner == game.Crew[0], "Checkpoint baggage ownership mirrored.");
                Require(game.Shift.NozzleState == NozzlePhase.AtStation && game.Shift.HoseState == HosePhase.Held &&
                    game.Shift.HoseSeat == 1, "Checkpoint nozzle and truck hose mirrored.");
            }
            Signal(M4Signal("client-mirrored"));
            if (m4Case == "clientdrop")
            {
                CloseClientSocketAbruptly();
                Complete(false, "Client socket abruptly closed without leave; launcher waits for host same-round completion.");
                return;
            }
            SetStage(m4Case == "shift" ? 2 : 5, now); return;
        }
        if (stage == 2)
        {
            if (!HasSignal(M4Signal("results-ready")) || !game.MatchFinished || game.ReplayFraction < .999f) return;
            string[] result = File.ReadAllLines(M4File("result"));
            Require(game.DisplayScore == int.Parse(result[0]) && game.DisplayDeparted == int.Parse(result[1]) &&
                game.DisplayStars == int.Parse(result[2]) && game.DisplayRemaining == 0f,
                "Client final score, completed flights, stars and zero timer must match authority.");
            int count = int.Parse(File.ReadAllText(M4File("replay-count")));
            if (game.ReplayFrameCount != count) return;
            string[] replayHashes = File.ReadAllLines(M4File("replay-hashes"));
            for (int i = 0; i < count; i++)
                Require(!string.IsNullOrEmpty(replayHashes[i]) && game.ReplaySampleHash(i) == replayHashes[i],
                    "Every authoritative replay sample must match after repair, index " + i);
            Require(game.ReplayAvailable, "Client replay must have real recorded history.");
            game.Retry();
            Require(game.RoundId == m4Round && game.MatchFinished, "Client cannot unilaterally reset its result mirror.");
            Shot("Evidence/m4-shift-results-client.png", 1920, 1080);
            report.AppendLine("PASS Client finished results and replay count match authority; local retry did not reset mirror. frames=" + count);
            Signal(M4Signal("client-results")); SetStage(3, now); return;
        }
        if (stage == 3)
        {
            if (game.RoundId == m4Round) return;
            Require(game.RoundId == m4Round + 1, "Client must receive exactly one authoritative retry.");
            if (state.Room.Phase == RoomPhase.Starting || game.MatchFinished || game.DisplayScore != 0) return;
            Signal(M4Signal("client-retried")); SetStage(4, now); return;
        }
        if (stage == 4)
        {
            if (SceneManager.GetActiveScene().name != AppState.SceneCabinLobby) return;
            Signal(M4Signal("client-returned"));
            Complete(false, "Client mirrored full shift results/replay/retry and explicit terminal room close.");
        }
        if (stage == 5)
        {
            if (!state.Room.IsHost || game == null || game.Remote) return;
            Require(game.NetworkShift && game.LocalSeat == 1 && state.Room.LocalSeat == 1 && game.RoundId == m4Round,
                "Promotion must preserve survivor seat 1 and original round.");
            Require(state.Room.Seats[0].Bot && !state.Room.Seats[1].Bot,
                "Departed authority becomes bot; surviving player remains human.");
            Require(game.Sim.Score == m4Score && game.Sim.Elapsed >= m4Elapsed && game.Sim.Elapsed < m4Elapsed + 1f,
                "Promotion must restore score/time, not restart or charge network timeout time.");
            Require(game.Shift.Carts[1].Loaded && game.Carts[1].Owner == game.Crew[0] &&
                game.Shift.FuelTruckTank > 0f && game.Shift.NozzleState == NozzlePhase.AtStation &&
                game.Shift.HoseState == HosePhase.Held && game.Shift.HoseSeat == 1,
                "Promotion must retain loaded cart, owner, partial truck fuel, nozzle and held truck hose.");
            Require(game.Shift.Passengers.Count == 8 && game.Shift.Meal == MealPhase.Ordered,
                "Promotion must retain waiting/walking passengers and in-progress meal production.");
            Shot("Evidence/m4-" + m4Case + "-takeover-client.png", 1920, 1080);
            report.AppendLine("PASS Actual network loss/leave promoted seat1 with original score, time, cargo owner, partial fuel, nozzle/hose, meal and passenger checkpoint.");
            m4MoveStart = game.Crew[1].Position;
            SetStage(6, now); return;
        }
        if (stage == 6)
        {
            // After promotion, touch/keyboard P1 input must still control local seat 1.
            game.SetInput(0, new CrewInput { Move = Vector2.right });
            if (Vector3.Distance(m4MoveStart, game.Crew[1].Position) < .25f) return;
            game.SetInput(0, new CrewInput());
            report.AppendLine("PASS Surviving player's regular input controls retained local seat1 after promotion.");
            SetStage(7, now); return;
        }
        if (stage == 7)
        {
            stepOverride = .5f;
            if (!game.MatchFinished) return;
            Require(game.Sim.Elapsed == 300f && game.Sim.Score >= m4Score,
                "Promoted survivor must finish same 300-second match with preserved scores.");
            if (game.ReplayFraction < .999f) return;
            Require(game.ReplayAvailable && game.ReplayFrameCount > 1,
                "Promoted survivor must retain real match replay history.");
            Require(game.ReplayStatus != "正在补齐共同回放",
                "Promoted authority must settle replay status after unrecoverable gaps are known.");
            if (game.ReplayNoticeRemaining > 0f) return;
            report.AppendLine("PASS Migrated match automatic replay reached final hold; frames=" + game.ReplayFrameCount + "; status=" + game.ReplayStatus);
            Shot("Evidence/m4-" + m4Case + "-results-client.png", 1920, 1080);
            if (game.ReplayStatus.Contains("缺失"))
            {
                AirportHudCanvas hud = UnityEngine.Object.FindObjectOfType<AirportHudCanvas>();
                Require(hud != null && hud.VisibleResultsHint == game.ReplayStatus,
                    "Missing replay segments must be disclosed visibly on the results HUD.");
                report.AppendLine("PASS Results HUD visibly discloses unrecoverable replay segments.");
            }
            Signal(M4Signal("survivor-finished"));
            Complete(false, "Surviving client restored live authoritative checkpoint, retained control and finished original match after host departure.");
        }
    }
}
