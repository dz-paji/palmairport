using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using IslandAirport;

/// <summary>
/// M3.3r 实际玩法验收：使用 PalmBay 场景内的 AirportGame 与 BotInput 按简化流程完成一班燃油
/// （拿枪—插车—开阀—等待—关阀自动归位—驾驶—取管—接管自动加注—断管自动收回），
/// 然后以真实 Step/Move 验证油渍只减慢驾驶、不减慢徒步。燃油流程只推进真实 bot，
/// 允许开始前完成餐食/行李前置并延长目标航班截止；油渍速度检查是独立的合成油渍 fixture。
/// batchmode 用 -executeMethod M3FuelPlaytest.Run（不要加 -quit）。
/// </summary>
[InitializeOnLoad]
public static class M3FuelPlaytest
{
    const string Pending = "PalmBay.M3FuelPlaytest";
    const float Dt = 0.05f;
    const int MaxFuelSteps = 1200; // 最多 60 个模拟秒；必须主动断管，不可等截止清理

    static IEnumerator steps;
    static AirportGame game;
    static Crew bot;
    static Cart fuelCart;
    static Flight targetFlight;
    static double started;
    static double requestedAt;
    static int runtimeErrorCount;
    static readonly StringBuilder report = new StringBuilder();
    static string lastNozzleState = string.Empty;
    static string lastHoseState = string.Empty;
    static bool lastValveOpen;
    static Vector3 lastFuelCartPosition;
    static bool hasLastFuelCartPosition;

    static bool sawNozzleHeld;
    static bool sawNozzleOnTruck;
    static bool sawAutoReturn;
    static bool sawValveOpen;
    static bool sawValveClosed;
    static bool sawFullTank;
    static bool sawFuelTruckDriving;
    static bool sawHoseHeld;
    static bool sawHoseOnAircraft;
    static bool sawHoseReeled;
    static bool sawFuelProgress;
    static bool fuelFlowCompleted;
    static float maxTank;
    static float maxTransferBalanceError;
    static bool trackingTransfer;
    static int firstHeldStep = -1;
    static int firstOnTruckStep = -1;
    static int firstValveOpenStep = -1;
    static int firstFullTankStep = -1;
    static int firstValveClosedStep = -1;
    static int firstTruckMoveStep = -1;
    static int firstHoseHeldStep = -1;
    static int firstOnAircraftStep = -1;
    static int firstFuelProgressStep = -1;
    static int firstHoseReeledStep = -1;
    static int scoreBefore;
    static int completedBefore;
    static int deliveriesBefore;

    static M3FuelPlaytest()
    {
        EditorApplication.update += Poll;
        Application.logMessageReceived += OnLog;
    }

    [MenuItem("Palm Bay/Verify M3.3r fuel bot in Play Mode")]
    public static void Run()
    {
        ResetRunState();
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        EditorSceneManager.OpenScene(DemoBuilder.PalmBayScenePath);
        SessionState.SetBool(Pending, true);
        SessionState.SetBool(Pending + ".Stop", false);
        SessionState.SetBool(Pending + ".Failed", false);
        requestedAt = EditorApplication.timeSinceStartup;
        EditorApplication.isPlaying = true;
    }

    static void ResetRunState()
    {
        steps = null;
        game = null;
        bot = null;
        fuelCart = null;
        targetFlight = null;
        report.Length = 0;
        started = requestedAt = 0;
        runtimeErrorCount = 0;
        lastNozzleState = string.Empty;
        lastHoseState = string.Empty;
        lastValveOpen = false;
        lastFuelCartPosition = Vector3.zero;
        hasLastFuelCartPosition = false;
        sawNozzleHeld = sawNozzleOnTruck = sawAutoReturn = false;
        sawValveOpen = sawValveClosed = sawFullTank = sawFuelTruckDriving = false;
        sawHoseHeld = sawHoseOnAircraft = sawHoseReeled = false;
        sawFuelProgress = trackingTransfer = fuelFlowCompleted = false;
        firstHeldStep = firstOnTruckStep = firstValveOpenStep = firstFullTankStep = -1;
        firstValveClosedStep = firstTruckMoveStep = firstHoseHeldStep = -1;
        firstOnAircraftStep = firstFuelProgressStep = firstHoseReeledStep = -1;
        maxTank = maxTransferBalanceError = 0f;
        scoreBefore = completedBefore = deliveriesBefore = 0;
    }

    static void Poll()
    {
        if (!SessionState.GetBool(Pending, false)) return;
        if (SessionState.GetBool(Pending + ".Stop", false))
        {
            if (EditorApplication.isPlaying) return;
            SessionState.SetBool(Pending, false);
            if (Application.isBatchMode)
                EditorApplication.Exit(SessionState.GetBool(Pending + ".Failed", false) ? 1 : 0);
            return;
        }
        if (!EditorApplication.isPlaying) return;

        EditorApplication.QueuePlayerLoopUpdate();
        try
        {
            if (steps == null)
            {
                game = UnityEngine.Object.FindAnyObjectByType<AirportGame>();
                if (game == null || !game.Started || game.Crew.Count < 2 || game.Carts.Count < 3)
                {
                    if (EditorApplication.timeSinceStartup - requestedAt > 30.0)
                        throw new Exception("PalmBay AirportGame did not become ready in Play Mode");
                    return;
                }

                started = EditorApplication.timeSinceStartup;
                steps = Exercise();
            }

            if (EditorApplication.timeSinceStartup - started > 150.0)
                throw new Exception("M3.3 fuel playtest exceeded 150 real seconds");
            if (steps.MoveNext()) return;
            Complete(false, "PASS: AirportGame BotInput completed a fuel task and the live Move path passed spill speed checks.");
        }
        catch (Exception e)
        {
            Complete(true, e.ToString());
        }
    }

    static void OnLog(string message, string stack, LogType kind)
    {
        if (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert)
        {
            runtimeErrorCount++;
            if (runtimeErrorCount <= 8)
                report.AppendLine("RUNTIME " + kind + ": " + message);
        }
    }

    static void Complete(bool failed, string message)
    {
        Application.logMessageReceived -= OnLog;
        failed |= runtimeErrorCount > 0;
        Directory.CreateDirectory("Evidence");
        File.WriteAllText("Evidence/m3-fuel-playtest.txt",
            "M3.3r fuel bot playtest: " + (failed ? "FAIL" : "PASS") + "\n" +
            message + "\n" +
            "Unity runtime errors (Error/Assert/Exception): " + runtimeErrorCount + "\n" +
            "Assertions and observations:\n" + report);
        Debug.Log((failed ? "M3_FUEL_PLAYTEST_FAILED " : "M3_FUEL_PLAYTEST_PASSED ") + message);
        SessionState.SetBool(Pending + ".Failed", failed);
        SessionState.SetBool(Pending + ".Stop", true);
        steps = null;
        EditorApplication.isPlaying = false;
    }

    static void Require(bool condition, string assertion)
    {
        if (!condition) throw new Exception(assertion);
        report.AppendLine("PASS " + assertion);
    }

    static void Ensure(bool condition, string assertion)
    {
        if (!condition) throw new Exception(assertion);
    }

    static string NozzleName()
    {
        return game.Shift.NozzleState.ToString();
    }

    static string HoseName()
    {
        return game.Shift.HoseState.ToString();
    }

    static void ObserveFuelStep(int step)
    {
        string nozzle = NozzleName();
        string hose = HoseName();
        bool valve = game.Shift.ValveOpen;
        bool nozzleChanged = nozzle != lastNozzleState;
        bool hoseChanged = hose != lastHoseState;
        bool valveChanged = valve != lastValveOpen;
        if (nozzleChanged)
        {
            report.AppendLine("STATE step=" + step + " time=" + game.Sim.Elapsed.ToString("0.00") +
                " nozzle=" + lastNozzleState + "->" + nozzle + " seat=" + game.Shift.NozzleSeat +
                " botDriving=" + (bot.Cart == fuelCart));
        }
        if (hoseChanged)
        {
            report.AppendLine("STATE step=" + step + " time=" + game.Sim.Elapsed.ToString("0.00") +
                " hose=" + lastHoseState + "->" + hose + " seat=" + game.Shift.HoseSeat +
                " flight=" + game.Shift.HoseFlightId + " botDriving=" + (bot.Cart == fuelCart));
        }
        if (valveChanged)
        {
            report.AppendLine("STATE step=" + step + " time=" + game.Sim.Elapsed.ToString("0.00") +
                " valve=" + lastValveOpen + "->" + valve + " tank=" + game.Shift.FuelTruckTank.ToString("0.000") +
                " botDriving=" + (bot.Cart == fuelCart));
        }

        if (nozzleChanged || hoseChanged || valveChanged)
            Ensure(bot.Cart != fuelCart, "nozzle, hose and valve interactions occur on foot");

        if (valveChanged && !valve && lastNozzleState == "OnTruck")
        {
            Ensure(nozzle == "AtStation", "closing the valve auto-returns the inserted station nozzle in the same Step");
            sawAutoReturn = true;
        }

        if (nozzle == "Held" && game.Shift.NozzleSeat == bot.Index)
        {
            sawNozzleHeld = true;
            if (firstHeldStep < 0) firstHeldStep = step;
        }
        if (nozzle == "OnTruck")
        {
            Ensure(game.Shift.FuelTruckAtStation, "station nozzle is only ever inserted while the truck is at the station");
            sawNozzleOnTruck = true;
            if (firstOnTruckStep < 0) firstOnTruckStep = step;
        }
        if (!game.Shift.FuelTruckAtStation)
            Ensure(nozzle != "OnTruck", "station nozzle never leaves the station with the truck");
        if (valve)
        {
            sawValveOpen = true;
            if (firstValveOpenStep < 0) firstValveOpenStep = step;
        }
        if (sawValveOpen && !valve)
        {
            sawValveClosed = true;
            if (firstValveClosedStep < 0) firstValveClosedStep = step;
        }
        if (game.Shift.FuelTruckTank > maxTank) maxTank = game.Shift.FuelTruckTank;
        if (game.Shift.FuelTruckTank >= 0.999f)
        {
            sawFullTank = true;
            if (firstFullTankStep < 0) firstFullTankStep = step;
        }
        if (hose == "Held" && game.Shift.HoseSeat == bot.Index)
        {
            sawHoseHeld = true;
            if (firstHoseHeldStep < 0) firstHoseHeldStep = step;
        }
        if (game.Shift.HoseFlightId == targetFlight.Id && hose == "OnAircraft")
        {
            sawHoseOnAircraft = true;
            if (firstOnAircraftStep < 0) firstOnAircraftStep = step;
            if (game.Shift.FuelTruckTank >= 0.999f || targetFlight.Progress[(int)ServiceKind.Fuel] > 0f)
                trackingTransfer = true;
        }
        if (hoseChanged && lastHoseState == "OnAircraft" && hose == "OnTruck")
        {
            sawHoseReeled = true;
            if (firstHoseReeledStep < 0) firstHoseReeledStep = step;
        }

        float fuelProgress = targetFlight.Progress[(int)ServiceKind.Fuel];
        if (fuelProgress > 0f)
        {
            sawFuelProgress = true;
            if (firstFuelProgressStep < 0) firstFuelProgressStep = step;
        }
        if (trackingTransfer)
        {
            float balanceError = Mathf.Abs(game.Shift.FuelTruckTank + fuelProgress - 1f);
            if (balanceError > maxTransferBalanceError) maxTransferBalanceError = balanceError;
        }

        bool drivingFuelCart = bot.Cart == fuelCart;
        if (drivingFuelCart)
        {
            Ensure(game.Shift.CanDriveCart((int)ServiceKind.Fuel),
                "fuel cart driving is permitted only with valve closed, nozzle home and hose reeled");
            Ensure(!valve && nozzle != "OnTruck" && hose == "OnTruck",
                "bot cannot drive while the valve is open, the nozzle is inserted or the hose is out");
            if (hasLastFuelCartPosition && Vector3.Distance(lastFuelCartPosition, fuelCart.Position) > 0.005f)
            {
                sawFuelTruckDriving = true;
                if (firstTruckMoveStep < 0) firstTruckMoveStep = step;
            }
            lastFuelCartPosition = fuelCart.Position;
            hasLastFuelCartPosition = true;
        }
        else
        {
            hasLastFuelCartPosition = false;
        }

        if (hose == "OnAircraft")
        {
            Ensure(!drivingFuelCart, "aircraft hose connection is made on foot");
            Ensure(game.Shift.HoseFlightId == targetFlight.Id, "aircraft hose is bound to the selected flight");
        }

        lastNozzleState = nozzle;
        lastHoseState = hose;
        lastValveOpen = valve;
    }

    static IEnumerator Exercise()
    {
        game.ExternalControl = true;
        game.StartShift(false);
        Require(game.Crew.Count == 2 && game.Carts.Count == 3, "real PalmBay scene crew and shared carts are present");

        // Wait for the real first aircraft taxi-in; keep both actors idle until fixture setup is done.
        Crew p1 = game.Crew[0];
        bot = game.Crew[1];
        p1.Bot = false;
        bot.Bot = false;
        int setupSteps = 0;
        int stand = -1;
        while (setupSteps < 1600 && targetFlight == null)
        {
            game.Step(Dt);
            for (int i = 0; i < Level1Map.StandCount; i++)
            {
                Flight candidate = game.Sim.ActiveAtStand(i);
                if (candidate != null && game.StandReady(i))
                {
                    targetFlight = candidate;
                    stand = i;
                    break;
                }
            }
            setupSteps++;
            if (setupSteps % 16 == 0) yield return null;
        }

        Require(targetFlight != null, "first real flight taxied into a ready stand");
        Require(targetFlight.Progress[(int)ServiceKind.Fuel] == 0f, "target fuel progress starts untouched");

        // Approved fixture only: skip unrelated meal/baggage work and give this flight enough time.
        targetFlight.Progress[(int)ServiceKind.Meals] = 1f;
        targetFlight.Progress[(int)ServiceKind.Baggage] = 1f;
        targetFlight.Deadline = Mathf.Max(targetFlight.Deadline, game.Sim.Elapsed + 240f);
        bot.Selected = game.Sim.Flights.IndexOf(targetFlight);
        bot.Bot = true;
        fuelCart = game.Carts[(int)ServiceKind.Fuel];
        scoreBefore = game.Sim.Score;
        completedBefore = game.Sim.CompletedTaskCount;
        deliveriesBefore = game.Deliveries;
        maxTank = game.Shift.FuelTruckTank;
        lastNozzleState = NozzleName();
        lastHoseState = HoseName();
        lastValveOpen = game.Shift.ValveOpen;
        report.AppendLine("FIXTURE flight=" + targetFlight.Id + " stand=" + stand +
            " elapsed=" + game.Sim.Elapsed.ToString("0.00") + "; Meals/Baggage progress set to 1 before bot begins; deadline=" +
            targetFlight.Deadline.ToString("0.00") + "; no fuel state/progress/position edited.");
        report.AppendLine("BOT seat=" + bot.Index + " enabled through AirportGame.BotInput; fixed Step dt=" + Dt.ToString("0.00"));

        int fuelSteps = 0;
        while (fuelSteps < MaxFuelSteps)
        {
            if (game.Sim.Finished) throw new Exception("shift ended before bot completed fuel task");
            bool wasDriving = bot.Cart == fuelCart;
            string beforeNozzle = NozzleName();
            string beforeHose = HoseName();
            bool beforeValve = game.Shift.ValveOpen;
            float beforeProgress = targetFlight.Progress[(int)ServiceKind.Fuel];

            game.Step(Dt);
            fuelSteps++;
            ObserveFuelStep(fuelSteps);
            float afterProgress = targetFlight.Progress[(int)ServiceKind.Fuel];
            Ensure(afterProgress - beforeProgress <= 0.00001f || bot.Cart != fuelCart,
                "fuel progress cannot increase while the bot is driving");

            if (wasDriving && bot.Cart == fuelCart)
            {
                Ensure(beforeNozzle == NozzleName() && beforeHose == HoseName() && beforeValve == game.Shift.ValveOpen,
                    "nozzle, hose and valve cannot be operated during a driving frame");
            }

            Ensure(game.Sim.Elapsed < targetFlight.Deadline, "bot must disconnect the hose before deadline cleanup");
            if (targetFlight.Progress[(int)ServiceKind.Fuel] >= 0.999f && !game.Shift.ValveOpen &&
                game.Shift.HoseState == HosePhase.OnTruck && game.Shift.NozzleState == NozzlePhase.AtStation &&
                game.Shift.CanDriveCart((int)ServiceKind.Fuel))
            {
                fuelFlowCompleted = true;
                for (int i = 0; i < 20; i++)
                {
                    game.Step(Dt);
                    fuelSteps++;
                    ObserveFuelStep(fuelSteps);
                    if (i % 8 == 0) yield return null;
                }
                break;
            }

            if (fuelSteps % 16 == 0) yield return null;
        }

        Require(fuelFlowCompleted, "bot autonomously finished and disconnected its hose within 60 simulation seconds");
        Require(game.Sim.Elapsed < targetFlight.Deadline && targetFlight.Status == FlightStatus.Servicing,
            "bot completion precedes flight deadline and automatic cleanup");
        Require(sawNozzleHeld, "bot picked up the station nozzle on foot");
        Require(sawNozzleOnTruck, "bot inserted the nozzle into the fuel truck at the station");
        Require(sawValveOpen && sawValveClosed, "bot opened the station valve, filled the truck, and closed it");
        Require(sawAutoReturn, "closing the valve auto-returned the inserted nozzle (no walk back to stow)");
        Require(sawFullTank && maxTank >= 0.999f, "truck tank actually reached full capacity");
        Require(sawFuelTruckDriving, "bot moved the fuel truck through AirportGame.Move");
        Require(sawHoseHeld, "bot took the truck's own hose at the stand");
        Require(sawHoseOnAircraft, "bot connected the truck hose to the selected aircraft on foot");
        Require(sawHoseReeled, "bot tapped the aircraft again and the hose auto-reeled onto the truck");
        Require(firstHeldStep > 0 && firstHeldStep < firstOnTruckStep &&
            firstOnTruckStep < firstValveOpenStep && firstValveOpenStep < firstFullTankStep &&
            firstFullTankStep < firstValveClosedStep && firstValveClosedStep < firstTruckMoveStep &&
            firstTruckMoveStep < firstHoseHeldStep && firstHoseHeldStep < firstOnAircraftStep &&
            firstOnAircraftStep <= firstFuelProgressStep && firstFuelProgressStep < firstHoseReeledStep,
            "observed lifecycle order was take, insert, open, fill, close(auto-return), drive, take hose, attach, auto-transfer, detach");
        Require(sawFuelProgress && targetFlight.Progress[(int)ServiceKind.Fuel] >= 0.999f,
            "bot transferred fuel and completed the flight fuel task without holding");
        Require(game.Shift.FuelTruckTank <= 0.001f,
            "one full truck load was conserved as completed flight fuel with no residue");
        Require(maxTransferBalanceError <= 0.002f,
            "truck fuel plus flight progress stayed 1:1 during aircraft transfer (max error=" +
            maxTransferBalanceError.ToString("0.0000") + ")");
        Require(!game.Shift.ValveOpen && game.Shift.HoseState == HosePhase.OnTruck && game.Shift.NozzleState == NozzlePhase.AtStation,
            "final state: valve closed, station nozzle home, truck hose reeled");
        Require(game.Shift.CanDriveCart((int)ServiceKind.Fuel), "fuel truck can be driven after fueling is complete");
        Require(game.Sim.Score == scoreBefore + 50, "one fuel completion awarded exactly one 50-point task score");
        Require(game.Sim.CompletedTaskCount == completedBefore + 1, "fuel completion was counted exactly once");
        Require(game.Deliveries == deliveriesBefore + 1, "one fuel completion event was delivered exactly once");
        Require(game.Shift.Spills.Count == 0 && !game.Shift.Spilling,
            "bot closes the valve and disconnects within the 2 s overfill grace: no spill at all");

        int fuelDeliveredEvents = 0;
        ShiftEventSnap[] recentEvents = game.Shift.CaptureShift().Events;
        for (int i = 0; recentEvents != null && i < recentEvents.Length; i++)
        {
            ShiftEventSnap e = recentEvents[i];
            if (e != null && e.FlightId == targetFlight.Id && e.Kind == (int)ServiceKind.Fuel &&
                e.Type == ShiftEventTypes.Delivered) fuelDeliveredEvents++;
        }
        Require(fuelDeliveredEvents == 1, "snapshot event history contains one fuel delivered event for the target flight");
        report.AppendLine("BOT_RESULT steps=" + fuelSteps + " elapsed=" + game.Sim.Elapsed.ToString("0.00") +
            " tank=" + game.Shift.FuelTruckTank.ToString("0.0000") +
            " progress=" + targetFlight.Progress[(int)ServiceKind.Fuel].ToString("0.0000") +
            " scoreDelta=" + (game.Sim.Score - scoreBefore) + " deliveriesDelta=" + (game.Deliveries - deliveriesBefore));

        var spillCheck = CheckActualMoveSpillResponse();
        while (spillCheck.MoveNext()) yield return spillCheck.Current;

        var inputCheck = CheckFuelInteractionInputs();
        while (inputCheck.MoveNext()) yield return inputCheck.Current;

        Require(runtimeErrorCount == 0, "Unity runtime error count stayed zero");
    }

    /// <summary>一次单击：按下帧（Pressed+Held，与触屏/键盘一致）后跟一个中性帧。</summary>
    static void Tap(int seat)
    {
        game.SetInput(seat, new CrewInput { Pressed = true, Held = true });
        game.Step(Dt);
        game.SetInput(seat, new CrewInput());
        game.Step(Dt);
    }

    static void Idle(float seconds)
    {
        int frames = Mathf.CeilToInt(seconds / Dt);
        for (int i = 0; i < frames; i++)
        {
            game.SetInput(0, new CrewInput());
            game.SetInput(1, new CrewInput());
            game.Step(Dt);
        }
    }

    static IEnumerator CheckFuelInteractionInputs()
    {
        // Independent reset after the autonomous bot result and movement check.
        // Every fuel action below goes through AirportGame.Step with single taps; only actor/cart
        // positions are fixtures (teleporting the tester between interaction zones).
        report.AppendLine("INPUT_FIXTURE independent reset; all fuel actions are real single-tap inputs through AirportGame.Step; only tester/cart positions are set.");
        game.StartShift(false);
        foreach (Crew crew in game.Crew) crew.Bot = false;
        Crew tester = game.Crew[0];
        Flight flight = null;
        for (int i = 0; i < 400; i++)
        {
            game.SetInput(0, new CrewInput());
            game.SetInput(1, new CrewInput());
            game.Step(Dt);
            flight = game.Sim.ActiveAtStand(0);
            if (flight != null && game.StandReady(0)) break;
            if (i % 16 == 0) yield return null;
        }
        Require(flight != null && game.StandReady(0), "independent input fixture has a ready aircraft");
        flight.Deadline = Mathf.Max(flight.Deadline, game.Sim.Elapsed + 240f);
        tester.Selected = game.Sim.Flights.IndexOf(flight);
        Cart cart = game.Carts[(int)ServiceKind.Fuel];
        Vector3 station = AirportGame.Station(ServiceKind.Fuel);
        Vector3 bay = Level1Map.CartPark(ServiceKind.Fuel);
        Require(Vector3.Distance(cart.Position, bay) < 0.01f && game.Shift.FuelTruckAtStation,
            "fuel truck starts in the station service bay");

        // ---- 站内：点油站拿枪 → 点油车插枪 → 点油站开阀 → 等满 → 再点关阀，油枪自动归位 ----
        tester.Position = station;
        Idle(Dt);
        Require(game.ActionLabel(tester) == "拿枪", "station tap label is take nozzle");
        Tap(0);
        Require(game.Shift.NozzleState == NozzlePhase.Held && game.Shift.NozzleSeat == 0, "single tap at the station takes the nozzle");
        tester.Position = bay;
        Idle(Dt);
        Require(game.ActionLabel(tester) == "插枪", "truck tap label with held nozzle is insert");
        Tap(0);
        Require(game.Shift.NozzleState == NozzlePhase.OnTruck, "single tap at the truck inserts the nozzle");
        Require(!game.Shift.CanDriveCart((int)ServiceKind.Fuel), "inserted nozzle blocks driving");
        Tap(0);
        Require(tester.Cart == null, "truck tap with inserted nozzle never mounts the truck");
        Require(game.Shift.NozzleState == NozzlePhase.AtStation, "truck tap with inserted nozzle and closed valve pulls it home");
        tester.Position = station;
        Tap(0);
        tester.Position = bay;
        Tap(0);
        Require(game.Shift.NozzleState == NozzlePhase.OnTruck, "nozzle re-inserted for the fill");
        tester.Position = station;
        Idle(Dt);
        Require(game.ActionLabel(tester) == "开阀", "station tap label with inserted nozzle is open valve");
        Tap(0);
        Require(game.Shift.ValveOpen, "single tap at the station opens the valve");
        Idle(ShiftSim.StationFillSeconds + 0.2f);
        yield return null;
        Require(game.Shift.FuelTruckTank >= 0.999f && !game.Shift.Spilling,
            "truck fills while the player stands idle and the 2 s grace has not run out");
        Require(game.ActionLabel(tester) == "关阀", "open valve label is close valve");
        Tap(0);
        Require(!game.Shift.ValveOpen && game.Shift.NozzleState == NozzlePhase.AtStation,
            "single tap closes the valve and the nozzle auto-returns to the station");
        Require(game.Shift.Spills.Count == 0, "closing within the grace window leaves no spill");

        // ---- 驾驶到机位 ----
        tester.Position = bay;
        Idle(Dt);
        Require(game.ActionLabel(tester) == "上车", "station truck tap without nozzle mounts (drive)");
        Tap(0);
        Require(tester.Cart == cart, "single tap mounts the truck");
        Tap(0);
        Require(tester.Cart == null, "single tap leaves the truck");
        cart.Position = AirportGame.Dock(flight.Stand);
        tester.Position = cart.Position;
        Idle(Dt);
        Require(!game.Shift.FuelTruckAtStation, "dock fixture truck is away from the station");

        // ---- 机位：点油车取管 → 点飞机接管自动加注 → 再点飞机断开 ----
        Require(game.ActionLabel(tester) == "取管", "dock truck tap label is take hose when the flight needs fuel and the truck has fuel");
        game.SetInput(0, new CrewInput { Pressed = true, Held = true });
        game.Step(Dt);
        for (int i = 0; i < 8; i++)
        {
            game.SetInput(0, new CrewInput { Held = true });
            game.Step(Dt);
        }
        game.SetInput(0, new CrewInput());
        game.Step(Dt);
        Require(game.Shift.HoseState == HosePhase.Held && game.Shift.HoseSeat == 0 && tester.Cart == null,
            "a long hold is just one tap: it takes the hose once and never mounts (long-press removed)");
        Require(!game.Shift.CanDriveCart((int)ServiceKind.Fuel), "held hose blocks driving");

        Vector3 dock = AirportGame.Dock(flight.Stand);
        Vector3 truckOnly = cart.Position + (cart.Position - (Vector3)Level1Map.PlanePark(flight.Stand)).normalized * 1.75f;
        tester.Position = truckOnly;
        Idle(Dt);
        Require(Vector3.Distance(truckOnly, dock) > 1.65f && Vector3.Distance(truckOnly, cart.Position) < 1.9f,
            "truck-only fixture stands beside the truck outside the aircraft zone");
        Require(game.ActionLabel(tester) == "收回", "held hose truck tap label is reel back");
        Tap(0);
        Require(game.Shift.HoseState == HosePhase.OnTruck, "tapping the truck with the hose in hand reels it back (cancel)");
        Tap(0);
        Require(game.Shift.HoseState == HosePhase.Held, "tap at the truck takes the hose again");
        tester.Position = dock;
        Idle(Dt);
        Require(game.ActionLabel(tester) == "接管", "held hose at the aircraft label is connect");
        float tankBefore = game.Shift.FuelTruckTank;
        Tap(0);
        Require(game.Shift.HoseState == HosePhase.OnAircraft && game.Shift.HoseFlightId == flight.Id,
            "single tap at the aircraft connects the hose");
        float progressAtConnect = flight.Progress[(int)ServiceKind.Fuel];
        Idle(1f);
        Require(flight.Progress[(int)ServiceKind.Fuel] > progressAtConnect + 0.25f,
            "fuel flows automatically with no input held");
        Require(Mathf.Abs(game.Shift.FuelTruckTank + flight.Progress[(int)ServiceKind.Fuel] - tankBefore) < 0.002f,
            "automatic transfer keeps truck plus flight 1:1");
        Require(game.ActionLabel(tester) == "断开", "connected hose label is disconnect");
        Tap(0);
        float pausedProgress = flight.Progress[(int)ServiceKind.Fuel];
        float pausedTank = game.Shift.FuelTruckTank;
        Require(game.Shift.HoseState == HosePhase.OnTruck, "second aircraft tap disconnects and the hose auto-reels");
        Idle(0.5f);
        Require(Mathf.Abs(flight.Progress[(int)ServiceKind.Fuel] - pausedProgress) < 0.00001f &&
            Mathf.Abs(game.Shift.FuelTruckTank - pausedTank) < 0.00001f, "disconnect preserves both flight progress and truck fuel");

        // ---- 机位满溢：加满后不断管，2 秒宽限后漫油；点飞机仍优先断开，按住再清理 ----
        tester.Position = cart.Position;
        Tap(0);
        tester.Position = dock;
        Tap(0);
        Require(game.Shift.HoseState == HosePhase.OnAircraft, "reconnected for the overfill check");
        for (int i = 0; i < 200 && flight.Progress[(int)ServiceKind.Fuel] < 1f; i++) Idle(Dt);
        Require(flight.Progress[(int)ServiceKind.Fuel] >= 1f, "aircraft filled automatically");
        Idle(ShiftSim.OverfillGraceSeconds - 0.3f);
        Require(!game.Shift.Spilling && game.Shift.Spills.Count == 0, "full aircraft still connected: no spill inside the 2 s grace");
        Idle(0.6f);
        Require(game.Shift.HoseSpilling && game.Shift.Spills.Count == 1, "full aircraft connected past 2 s spills");
        ShiftSpill dockSpill = game.Shift.Spills[0];
        Require(Vector2.Distance(new Vector2(dockSpill.X, dockSpill.Z), new Vector2(dock.x, dock.z)) < 0.01f,
            "dock overfill spill appears at the aircraft's service bay");
        Require(game.ActionLabel(tester) == "断开", "disconnect outranks cleaning while standing on the dock spill");
        game.SetInput(0, new CrewInput { Pressed = true, Held = true });
        game.Step(Dt);
        Require(game.Shift.HoseState == HosePhase.OnTruck && !game.Shift.Spilling,
            "Pressed+Held on the spill disconnects first and the overfill stops");
        for (int i = 0; i < 80 && game.Shift.Spills.Count > 0; i++)
        {
            game.SetInput(0, new CrewInput { Held = true });
            game.Step(Dt);
        }
        game.SetInput(0, new CrewInput());
        game.Step(Dt);
        Require(game.Shift.Spills.Count == 0, "continued hold cleans the dock spill on foot");
        yield return null;

        // ---- 航班已满：点油车 = 上车驾驶 ----
        tester.Position = cart.Position;
        Idle(Dt);
        Require(game.ActionLabel(tester) == "上车", "dock truck tap label is drive once the flight no longer needs fuel");
        Tap(0);
        Require(tester.Cart == cart && cart.Owner == tester, "single tap at the dock mounts the truck");
        Vector3 driveStart = cart.Position;
        game.SetInput(0, new CrewInput { Move = Vector2.left });
        game.Step(Dt);
        Require(Vector3.Distance(driveStart, cart.Position) > 0.01f, "remounted truck resumes actual AirportGame movement");
        Tap(0);
        Require(tester.Cart == null, "driver disembarks");

        // ---- 阀门与油渍重叠：点按关阀优先，继续按住清理 ----
        tester.Position = station + Vector3.right;
        game.Shift.Spills.Clear();
        game.Shift.Spills.Add(new ShiftSpill { Id = 998, X = tester.Position.x,
            Z = tester.Position.z, Radius = ShiftSim.SpillRadius, CleanWork = 0f });
        Ensure(game.Shift.SetValve(0, true), "valve overlap fixture opens valve");
        Require(game.ActionLabel(tester) == "关阀", "valve and spill overlap label prioritizes closing the open valve");
        game.SetInput(0, new CrewInput { Pressed = true, Held = true });
        game.Step(Dt);
        Require(!game.Shift.ValveOpen, "Pressed plus Held closes the valve while standing on nearby oil");
        game.SetInput(0, new CrewInput { Held = true });
        game.Step(Dt);
        bool cleaned = false;
        foreach (ShiftSpill spill in game.Shift.Spills) if (spill.Id == 998) cleaned = spill.CleanWork > 0f;
        Require(cleaned, "continued Held reaches oil cleanup after the valve is closed");
        yield return null;
    }

    static IEnumerator CheckActualMoveSpillResponse()
    {
        // This is deliberately after the accepted bot fuel run. The synthetic patch and actor/cart
        // positions are fixtures for comparing actual Move(dt), not steps used by BotInput.
        game.Crew[1].Bot = false;
        Crew tester = game.Crew[0];
        tester.Bot = false;
        Cart cart = fuelCart;
        Require(cart.Owner == null && tester.Cart == null && game.Crew[1].Cart != cart,
            "bot released the fuel truck after its completed task");
        int botFlowSpills = game.Shift.Spills.Count;
        Require(botFlowSpills <= 1 && !game.Shift.Spilling,
            "post-flow spill fixture starts with no active overflow and at most one recorded bot-flow spill");
        report.AppendLine("SPILL_FIXTURE clears " + botFlowSpills +
            " recorded bot-flow spill(s) after recording them, then compares controlled same-point movement.");
        game.Shift.Spills.Clear();

        // Use the gameplay spill origin for both measurements. Baseline and spill steps share
        // the same road coordinate and direction, controlling for map clamps and obstacles.
        Vector3 spillCenter = AirportGame.Station(ServiceKind.Fuel) + new Vector3(1.9f, 0f, 0f);
        tester.Position = spillCenter;
        cart.Position = spillCenter;
        game.SetInput(tester.Index, new CrewInput { Pressed = true, Held = true });
        game.Step(Dt);
        Require(tester.Cart == cart, "spill-check fixture actor boarded fuel cart through AirportGame interaction");
        Vector3 before = cart.Position;
        game.SetInput(tester.Index, new CrewInput { Move = Vector2.right });
        game.Step(Dt);
        float vehicleBaseline = Vector3.Distance(before, cart.Position);
        game.SetInput(tester.Index, new CrewInput { Pressed = true, Held = true });
        game.Step(Dt);
        Require(tester.Cart == null, "spill-check actor left the truck before walking comparison");

        // Measure the unspilled foot baseline before adding oil, at the same point/direction.
        tester.Position = spillCenter;
        before = tester.Position;
        game.SetInput(tester.Index, new CrewInput { Move = Vector2.right });
        game.Step(Dt);
        float footBaseline = Vector3.Distance(before, tester.Position);

        game.Shift.Spills.Add(new ShiftSpill
        {
            Id = 999,
            X = spillCenter.x,
            Z = spillCenter.z,
            Radius = ShiftSim.SpillRadius,
            CleanWork = 0f
        });
        Require(game.Shift.SpillAt(spillCenter.x, spillCenter.z), "synthetic oil spill fixture covers its origin");

        // Board just outside cleanup reach but within truck reach, then the
        // normal claim snaps the driver onto the identical measurement point.
        tester.Position = spillCenter + Vector3.forward * 1.85f;
        cart.Position = spillCenter;
        game.SetInput(tester.Index, new CrewInput { Pressed = true, Held = true });
        game.Step(Dt);
        Require(tester.Cart == cart, "spill-check actor boarded fuel cart from outside cleanup reach for the spill measurement");
        before = cart.Position;
        game.SetInput(tester.Index, new CrewInput { Move = Vector2.right });
        game.Step(Dt);
        float vehicleOnSpill = Vector3.Distance(before, cart.Position);
        Require(vehicleBaseline > 0.14f,
            "unspilled fuel-cart movement baseline is measurable (" + vehicleBaseline.ToString("0.000") + "m)");
        Require(vehicleOnSpill > 0.01f && vehicleOnSpill < vehicleBaseline - 0.02f,
            "actual Move slowed the driven cart on spill from the same point (baseline=" + vehicleBaseline.ToString("0.000") +
            "m, spill=" + vehicleOnSpill.ToString("0.000") + "m)");

        game.SetInput(tester.Index, new CrewInput { Pressed = true, Held = true });
        game.Step(Dt);
        Require(tester.Cart == null, "spill-check actor left the truck before walking comparison");

        tester.Position = spillCenter;
        before = tester.Position;
        game.SetInput(tester.Index, new CrewInput { Move = Vector2.right });
        game.Step(Dt);
        float footOnSpill = Vector3.Distance(before, tester.Position);
        Require(footBaseline > 0.20f && Mathf.Abs(footOnSpill - footBaseline) <= 0.015f,
            "actual Move leaves walking speed unchanged on spill from the same point (baseline=" + footBaseline.ToString("0.000") +
            "m, spill=" + footOnSpill.ToString("0.000") + "m)");

        report.AppendLine("SPILL_FIXTURE synthetic origin=" + spillCenter +
            "; each baseline and spill sample used the same start coordinate/direction; only the post-flow Move comparison used fixture positions/oil; no fuel task state was changed.");
        game.Shift.Spills.Clear();
        yield return null;
    }
}
