using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using IslandAirport;

/// <summary>
/// 难度基准：用真实 AirportGame.BotInput 跑固定步长模拟，输出每类任务的 bot 用时与整局表现。
/// ① 单项任务：首班停稳后，一个 bot 只做指定任务（复用 BotGeneralInput / TryBotFuelInput），
///    记录从停稳到完成的用时与子阶段时间线；登机项用领域 API 预先完成餐食+燃油前置（fixture，报告中注明）。
/// ② 整局：有限 300 秒本地局，分"1 bot（搭档挂机）"与"2 bot"两种配置，记录每班各任务完成时刻、
///    离港余量、错过/失败、分数星级与 bot 时间分配。
/// 步长 0.05s：AirportGame.Step 对移动按 min(slice,0.1) 截断，步长大于 0.1 会让角色变慢、结果失真。
/// bot 偏好使用中性内存（不读写本机合作记忆文件）。
/// batchmode：-executeMethod DifficultyBenchmark.Run（不要加 -quit）；产物在 Evidence/difficulty/。
/// </summary>
[InitializeOnLoad]
public static class DifficultyBenchmark
{
    const string Pending = "PalmBay.DifficultyBenchmark";
    const float Dt = .05f;
    const float IsolatedTimeout = 90f;
    const float StallSeconds = 10f;
    const string OutDir = "Evidence/difficulty";
    static readonly string[] KindNames = { "Meals", "Baggage", "Fuel", "Boarding" };

    static IEnumerator steps;
    static AirportGame game;
    static AppState state;
    static double started;
    static bool runtimeErrors;
    static readonly FieldInfo storeField = typeof(AppState).GetField("botMemoryStore", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly MethodInfo generalInput = typeof(AirportGame).GetMethod("BotGeneralInput", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly MethodInfo fuelInput = typeof(AirportGame).GetMethod("TryBotFuelInput", BindingFlags.Instance | BindingFlags.NonPublic);
    static BotMemoryStore originalStore;
    static readonly List<Dictionary<string, object>> isolated = new List<Dictionary<string, object>>();
    static readonly List<Dictionary<string, object>> shifts = new List<Dictionary<string, object>>();
    static readonly StringBuilder trace = new StringBuilder();

    static DifficultyBenchmark() { EditorApplication.update += Poll; }

    [MenuItem("Palm Bay/Run difficulty benchmark")]
    public static void Run()
    {
        EditorSceneManager.OpenScene(DemoBuilder.PalmBayScenePath);
        SessionState.SetBool(Pending, true);
        SessionState.SetBool(Pending + ".Stop", false);
        SessionState.SetBool(Pending + ".Failed", false);
        EditorApplication.isPlaying = true;
    }

    static void Poll()
    {
        if (!SessionState.GetBool(Pending, false)) return;
        if (SessionState.GetBool(Pending + ".Stop", false))
        {
            if (EditorApplication.isPlaying) return;
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
                game = UnityEngine.Object.FindAnyObjectByType<AirportGame>();
                if (game == null) return;
                game.ExternalControl = true;
                state = AppState.Ensure(); state.ExternalControl = true;
                started = EditorApplication.timeSinceStartup;
                runtimeErrors = false;
                isolated.Clear(); shifts.Clear(); trace.Length = 0;
                Application.logMessageReceived += OnLog;
                steps = Exercise();
            }
            Require(EditorApplication.timeSinceStartup - started <= 600, "Difficulty benchmark exceeded 600 wall seconds");
            if (steps.MoveNext()) return;
            Complete(false, "PASS");
        }
        catch (Exception e) { Complete(true, e.ToString()); }
    }

    static void OnLog(string text, string stack, LogType type)
    { if (type == LogType.Exception || type == LogType.Error) runtimeErrors = true; }
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

    static void Complete(bool failed, string details)
    {
        Application.logMessageReceived -= OnLog;
        if (originalStore != null && state != null) storeField.SetValue(state, originalStore);
        failed |= runtimeErrors;
        try { WriteOutputs(failed, details); }
        catch (Exception e) { failed = true; details += "\nOutput failure: " + e; }
        Debug.Log((failed ? "DIFFICULTY_BENCHMARK_FAILED " : "DIFFICULTY_BENCHMARK_PASSED ") + details);
        SessionState.SetBool(Pending + ".Failed", failed);
        SessionState.SetBool(Pending + ".Stop", true);
        steps = null;
        EditorApplication.isPlaying = false;
    }

    static IEnumerator Exercise()
    {
        double waitUntil = EditorApplication.timeSinceStartup + .7;
        while (EditorApplication.timeSinceStartup < waitUntil) yield return null;
        Require(storeField != null && generalInput != null && fuelInput != null, "AirportGame bot methods / AppState memory field not found");
        originalStore = (BotMemoryStore)storeField.GetValue(state);
        // 中性偏好：所有人类计数为 0，ChooseBotTask 退化为按任务序号选择；写入丢弃。
        storeField.SetValue(state, new BotMemoryStore(() => string.Empty, json => { }));

        for (int kind = 0; kind < 4; kind++)
        {
            IEnumerator run = RunIsolated((ServiceKind)kind);
            while (run.MoveNext()) yield return null;
        }
        IEnumerator solo = RunShift("1 bot (partner idle)", false, true);
        while (solo.MoveNext()) yield return null;
        IEnumerator duo = RunShift("2 bots", true, true);
        while (duo.MoveNext()) yield return null;
    }

    // ---------- ① 单项任务 ----------

    static IEnumerator RunIsolated(ServiceKind kind)
    {
        game.StartShift(true);
        game.Crew[0].Bot = false; game.Crew[1].Bot = false;
        int guard = 0;
        while (!(game.StandReady(0) && game.Sim.ActiveAtStand(0) != null) && guard++ < 1200)
        { game.Step(Dt); if (guard % 64 == 0) yield return null; }
        Flight f = game.Sim.ActiveAtStand(0);
        Require(f != null && game.StandReady(0), "First flight did not park for isolated " + kind);
        float taxi = game.Sim.Elapsed - f.AssignedAt;
        bool fixture = false;
        if (kind == ServiceKind.Boarding)
        {
            Require(game.Sim.TryAdvance(f, ServiceKind.Meals, 1f) && game.Sim.TryAdvance(f, ServiceKind.Fuel, 1f), "Boarding prerequisite fixture");
            fixture = true;
        }
        Crew bot = game.Crew[0];
        bot.Selected = game.Sim.Flights.IndexOf(f);
        float ready = game.Sim.Elapsed;
        var phases = new List<object>();
        string last = null;
        float walk = 0f, drive = 0f, walkTime = 0f, driveTime = 0f, idleTime = 0f;
        int k = (int)kind, n = 0;
        while (f.Progress[k] < 1f && f.Status == FlightStatus.Servicing && game.Sim.Elapsed - ready < IsolatedTimeout)
        {
            CrewInput input = IsolatedInput(bot, f, kind);
            Vector3 before = bot.Position; bool driving = bot.Cart != null; float t = game.Sim.Elapsed;
            game.SetInput(0, input);
            game.Step(Dt);
            float step = game.Sim.Elapsed - t, moved = Vector3.Distance(before, bot.Position);
            if (moved < .001f) idleTime += step;
            else if (driving) { drive += moved; driveTime += step; }
            else { walk += moved; walkTime += step; }
            string sig = Signature(bot, kind, f);
            if (sig != last)
            {
                phases.Add(new Dictionary<string, object> { { "t", Round(game.Sim.Elapsed - ready) }, { "state", sig } });
                last = sig;
            }
            if (++n % 64 == 0) yield return null;
        }
        bool done = f.Progress[k] >= 1f;
        isolated.Add(new Dictionary<string, object> {
            { "task", KindNames[k] }, { "flight", f.Id }, { "completed", done },
            { "seconds", Round(game.Sim.Elapsed - ready) }, { "taxiSeconds", Round(taxi) },
            { "walkMeters", Round(walk) }, { "driveMeters", Round(drive) },
            { "walkSeconds", Round(walkTime) }, { "driveSeconds", Round(driveTime) }, { "stationarySeconds", Round(idleTime) },
            { "fixture", fixture ? "Meals+Fuel pre-completed via domain API so the gate can open" : "" },
            { "phases", phases } });
    }

    static CrewInput IsolatedInput(Crew bot, Flight f, ServiceKind kind)
    {
        if (kind == ServiceKind.Fuel)
        {
            object[] args = { bot, Dt, f, null };
            return (bool)fuelInput.Invoke(game, args) ? (CrewInput)args[3] : new CrewInput();
        }
        return (CrewInput)generalInput.Invoke(game, new object[] { bot, Dt, f, (int)kind });
    }

    static string Signature(Crew bot, ServiceKind kind, Flight f)
    {
        var s = new StringBuilder(bot.Cart != null ? "driving " + bot.Cart.Kind : "on foot");
        ShiftSim sh = game.Shift;
        if (kind == ServiceKind.Fuel)
        {
            s.Append(" | nozzle=").Append(sh.NozzleState).Append(" hose=").Append(sh.HoseState)
             .Append(" valve=").Append(sh.ValveOpen ? "open" : "closed")
             .Append(" tank=").Append(sh.FuelTruckTank <= .001f ? "empty" : sh.FuelTruckTank >= .999f ? "full" : "filling");
        }
        else if (kind == ServiceKind.Boarding)
        {
            s.Append(" | gate=").Append(sh.IsBoarding(f.Id) ? "open" : "closed")
             .Append(" boarded=").Append(Mathf.RoundToInt(f.Progress[3] / ShiftSim.BoardingPerPassenger)).Append('/').Append(ShiftSim.PassengersPerGate);
        }
        else
        {
            ShiftCart cart = sh.Carts[(int)kind];
            if (kind == ServiceKind.Meals) s.Append(" | meal=").Append(sh.Meal);
            else s.Append(" | arrivalBagsReturned=").Append(f.ArrivalBagsReturned);
            s.Append(" cargo=").Append(cart.Arrival ? "arrival bags" : cart.Loaded ? "loaded" : "empty");
            if (cart.DeliverWork > 0f) s.Append(" delivering");
        }
        return s.ToString();
    }

    // ---------- ② 整局 ----------

    sealed class FlightTrack
    {
        public Flight F; public float Assigned = -1, Ready = -1, Departed = -1; public int Stand = -1;
        public readonly float[] Done = { -1, -1, -1, -1 };
    }

    static IEnumerator RunShift(string label, bool bot0, bool bot1)
    {
        game.StartShift(true);
        game.Crew[0].Bot = bot0; game.Crew[1].Bot = bot1;
        var tracks = new List<FlightTrack>();
        foreach (Flight f in game.Sim.Flights) tracks.Add(new FlightTrack { F = f });
        int crew = game.Crew.Count;
        var walkT = new float[crew]; var driveT = new float[crew]; var idleT = new float[crew];
        var cartT = new float[crew, 3];
        var still = new float[crew]; var longest = new float[crew]; var stalls = new int[crew]; var stalledT = new float[crew];
        int spills = 0, lastSpills = 0, n = 0;
        while (!game.MatchFinished && n < Mathf.CeilToInt(AirportSimulation.ShiftDuration / Dt) + 200)
        {
            var before = new Vector3[crew]; var drv = new Cart[crew];
            for (int i = 0; i < crew; i++) { before[i] = game.Crew[i].Position; drv[i] = game.Crew[i].Cart; }
            float t = game.Sim.Elapsed;
            game.Step(Dt);
            float step = game.Sim.Elapsed - t, now = game.Sim.Elapsed;
            for (int i = 0; i < crew; i++)
            {
                if (!game.Crew[i].Bot) continue;
                bool moved = Vector3.Distance(before[i], game.Crew[i].Position) > .001f;
                if (drv[i] != null) { cartT[i, (int)drv[i].Kind] += step; if (moved) driveT[i] += step; else idleT[i] += step; }
                else if (moved) walkT[i] += step; else idleT[i] += step;
                // 停滞 = 连续 StallSeconds 以上原地不动（读条/备餐/加注最长 6s，超过即视为卡死）。
                if (moved) still[i] = 0f;
                else
                {
                    still[i] += step;
                    if (still[i] >= StallSeconds) { if (still[i] - step < StallSeconds) { stalls[i]++; stalledT[i] += still[i]; } else stalledT[i] += step; }
                }
                longest[i] = Mathf.Max(longest[i], still[i]);
            }
            foreach (var tr in tracks)
            {
                Flight f = tr.F;
                if (f.Status == FlightStatus.Servicing)
                {
                    if (tr.Assigned < 0) { tr.Assigned = f.AssignedAt; tr.Stand = f.Stand; }
                    if (tr.Ready < 0 && game.StandReady(f.Stand)) tr.Ready = now;
                }
                for (int k = 0; k < 4; k++) if (tr.Done[k] < 0 && f.Progress[k] >= 1f) tr.Done[k] = now;
                if (f.Status == FlightStatus.Departed && tr.Departed < 0) tr.Departed = now;
            }
            if (n % 40 == 0)
                for (int i = 0; i < crew; i++)
                {
                    Crew c = game.Crew[i];
                    if (!c.Bot) continue;
                    Flight sel = game.SelectedFlight(c);
                    ShiftCart cs = c.Cart == null ? null : game.CartState(c.Cart);
                    trace.AppendLine(label + " t=" + now.ToString("0.0", CultureInfo.InvariantCulture) + " seat" + i +
                        " pos=" + c.Position.ToString("F1") + " cart=" + (c.Cart == null ? "-" : c.Cart.Kind + (cs.Loaded ? "(loaded " + cs.CargoFlightId + (cs.Arrival ? " arrival" : "") + ")" : "(empty)")) +
                        " sel=" + (sel == null ? "-" : sel.Id) + " blocked=" + c.BlockedByTraffic + " hint=" + c.Hint +
                        " carts=" + game.Carts[0].Position.ToString("F1") + game.Carts[1].Position.ToString("F1") + game.Carts[2].Position.ToString("F1") +
                        " nozzle=" + game.Shift.NozzleState + " hose=" + game.Shift.HoseState + " tank=" + game.Shift.FuelTruckTank.ToString("0.00"));
                }
            int count = game.Shift.Spills.Count;
            if (count > lastSpills) spills += count - lastSpills;
            lastSpills = count;
            if (++n % 64 == 0) yield return null;
        }
        Require(game.MatchFinished, "Shift did not finish: " + label);

        var flights = new List<object>();
        foreach (var tr in tracks)
        {
            Flight f = tr.F;
            var tasks = new Dictionary<string, object>();
            for (int k = 0; k < 4; k++)
                tasks[KindNames[k]] = new Dictionary<string, object> {
                    { "doneAt", tr.Done[k] < 0 ? null : (object)Round(tr.Done[k]) },
                    { "sinceReady", tr.Done[k] < 0 || tr.Ready < 0 ? null : (object)Round(tr.Done[k] - tr.Ready) },
                    { "failed", game.Sim.IsTaskFailed(f.Id, (ServiceKind)k) } };
            flights.Add(new Dictionary<string, object> {
                { "id", f.Id }, { "arrival", Round(f.ArrivalTime) }, { "deadline", Round(f.Deadline) },
                { "stand", tr.Stand }, { "assignedAt", tr.Assigned < 0 ? null : (object)Round(tr.Assigned) },
                { "readyAt", tr.Ready < 0 ? null : (object)Round(tr.Ready) },
                { "status", f.Status.ToString() },
                { "departedAt", tr.Departed < 0 ? null : (object)Round(tr.Departed) },
                { "slackSeconds", tr.Departed < 0 ? null : (object)Round(f.Deadline - tr.Departed) },
                { "turnaroundSeconds", tr.Departed < 0 || tr.Ready < 0 ? null : (object)Round(tr.Departed - tr.Ready) },
                { "tasks", tasks } });
        }
        var bots = new List<object>();
        for (int i = 0; i < crew; i++)
        {
            if (!game.Crew[i].Bot) continue;
            bots.Add(new Dictionary<string, object> {
                { "seat", i }, { "walkingSeconds", Round(walkT[i]) }, { "drivingSeconds", Round(driveT[i]) },
                { "stationarySeconds", Round(idleT[i]) },
                { "mealsCartSeconds", Round(cartT[i, 0]) }, { "baggageCartSeconds", Round(cartT[i, 1]) }, { "fuelTruckSeconds", Round(cartT[i, 2]) },
                { "stalls", stalls[i] }, { "stalledSeconds", Round(stalledT[i]) }, { "longestStillSeconds", Round(longest[i]) } });
        }
        shifts.Add(new Dictionary<string, object> {
            { "label", label }, { "score", game.Sim.Score }, { "stars", game.Sim.Stars },
            { "departed", game.Sim.DepartedCount }, { "missed", game.Sim.MissedCount },
            { "tasksCompleted", game.Sim.CompletedTaskCount }, { "tasksTotal", tracks.Count * 4 },
            { "failedTasks", game.Sim.Flights.Count == 0 ? 0 : game.Shift.Failed.Count }, { "spills", spills },
            { "flights", flights }, { "bots", bots } });
    }

    // ---------- 输出 ----------

    static double Round(float v) { return Math.Round(v, 2); }

    static void WriteOutputs(bool failed, string details)
    {
        Directory.CreateDirectory(OutDir);
        var config = new Dictionary<string, object> {
            { "stepSeconds", Round(Dt) }, { "shiftSeconds", Round(AirportSimulation.ShiftDuration) }, { "stands", AirportSimulation.StandCount },
            { "mealPrepareSeconds", Round(ShiftSim.MealPrepareSeconds) }, { "stationFillSeconds", Round(ShiftSim.StationFillSeconds) },
            { "dockFuelSeconds", Round(ShiftSim.DockFuelSeconds) }, { "deliverSeconds", Round(ShiftSim.DeliverSeconds) },
            { "passengersPerGate", ShiftSim.PassengersPerGate }, { "passengerStagger", Round(ShiftSim.PassengerStagger) },
            { "passengerSpeed", Round(ShiftSim.PassengerSpeed) }, { "starThresholds", "1★ ≥1 departed · 2★ ≥2 · 3★ ≥4" } };
        var root = new Dictionary<string, object> {
            { "generated", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) },
            { "result", failed ? "FAIL" : "PASS" }, { "details", details }, { "runtimeErrors", runtimeErrors },
            { "config", config }, { "isolated", isolated }, { "shifts", shifts } };
        File.WriteAllText(Path.Combine(OutDir, "difficulty-metrics.json"), MiniJson.Serialize(root));
        File.WriteAllText(Path.Combine(OutDir, "difficulty-metrics.md"), Markdown(failed, details));
        File.WriteAllText(Path.Combine(OutDir, "bot-trace.txt"), trace.ToString());
    }

    static string F(object v) { return v == null ? "—" : Convert.ToDouble(v).ToString("0.0", CultureInfo.InvariantCulture); }

    static string Markdown(bool failed, string details)
    {
        var md = new StringBuilder();
        md.AppendLine("# PALM BAY difficulty benchmark");
        md.AppendLine();
        md.AppendLine("Result: **" + (failed ? "FAIL" : "PASS") + "** · generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) +
            " · real `AirportGame.BotInput`, fixed step " + Dt + "s, neutral bot memory.");
        if (failed) md.AppendLine().AppendLine("```").AppendLine(details).AppendLine("```");
        md.AppendLine();
        md.AppendLine("## 1. Single-task bot time (first flight, from aircraft parked)");
        md.AppendLine();
        md.AppendLine("| Task | Done | Seconds | Walk s / m | Drive s / m | Stationary s | Note |");
        md.AppendLine("|---|---|---:|---:|---:|---:|---|");
        foreach (var r in isolated)
            md.AppendLine("| " + r["task"] + " | " + ((bool)r["completed"] ? "yes" : "**no**") + " | " + F(r["seconds"]) +
                " | " + F(r["walkSeconds"]) + " / " + F(r["walkMeters"]) + " | " + F(r["driveSeconds"]) + " / " + F(r["driveMeters"]) +
                " | " + F(r["stationarySeconds"]) + " | " + r["fixture"] + " |");
        if (isolated.Count > 0) md.AppendLine().AppendLine("Taxi-in before the stand opens: " + F(isolated[0]["taxiSeconds"]) + "s. Service window per flight: 75s from arrival.");
        foreach (var r in isolated)
        {
            md.AppendLine().AppendLine("<details><summary>" + r["task"] + " timeline</summary>").AppendLine();
            foreach (Dictionary<string, object> p in (List<object>)r["phases"]) md.AppendLine("- " + F(p["t"]) + "s — " + p["state"]);
            md.AppendLine().AppendLine("</details>");
        }
        md.AppendLine();
        md.AppendLine("## 2. Difficulty budget (derived from section 1)");
        md.AppendLine();
        if (isolated.Count == 4)
        {
            double work = 0; foreach (var r in isolated) work += Convert.ToDouble(r["seconds"]);
            double taxi = Convert.ToDouble(isolated[0]["taxiSeconds"]);
            var arrivals = new List<float>(); foreach (Flight f in new AirportSimulation().Flights) arrivals.Add(f.ArrivalTime);
            var gaps = new List<string>(); double gapSum = 0;
            for (int i = 1; i < arrivals.Count; i++) { gaps.Add((arrivals[i] - arrivals[i - 1]).ToString("0", CultureInfo.InvariantCulture)); gapSum += arrivals[i] - arrivals[i - 1]; }
            double window = 75 - taxi, gap = gapSum / Math.Max(1, arrivals.Count - 1);
            md.AppendLine("| Measure | Value |");
            md.AppendLine("|---|---:|");
            md.AppendLine("| Bot work per flight (sum of four single tasks, no travel between them) | " + work.ToString("0.0", CultureInfo.InvariantCulture) + "s |");
            md.AppendLine("| Usable window per flight (75s − taxi-in) | " + window.ToString("0.0", CultureInfo.InvariantCulture) + "s |");
            md.AppendLine("| One worker's window utilisation | " + (work / window * 100).ToString("0", CultureInfo.InvariantCulture) + "% |");
            md.AppendLine("| Arrival gaps | " + string.Join(" / ", gaps.ToArray()) + "s (mean " + gap.ToString("0.0", CultureInfo.InvariantCulture) + "s) |");
            md.AppendLine("| Workers needed to keep pace (work ÷ mean gap) | " + (work / gap).ToString("0.00", CultureInfo.InvariantCulture) + " |");
            md.AppendLine("| Flights in a 300s shift / needed for 3★ | " + arrivals.Count + " / 4 |");
        }
        md.AppendLine();
        md.AppendLine("## 3. Full 300s shift");
        foreach (var s in shifts)
        {
            md.AppendLine().AppendLine("### " + s["label"]).AppendLine();
            md.AppendLine("Score **" + s["score"] + "** · " + s["stars"] + "★ · departed " + s["departed"] + " / missed " + s["missed"] +
                " · tasks " + s["tasksCompleted"] + "/" + s["tasksTotal"] + " · failed tasks " + s["failedTasks"] + " · spills " + s["spills"]);
            md.AppendLine();
            md.AppendLine("| Flight | Arrive | Stand ready | Deadline | Meals | Baggage | Fuel | Boarding | Result | Slack s |");
            md.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---|---:|");
            foreach (Dictionary<string, object> f in (List<object>)s["flights"])
            {
                var tasks = (Dictionary<string, object>)f["tasks"];
                md.Append("| " + f["id"] + " | " + F(f["arrival"]) + " | " + F(f["readyAt"]) + " | " + F(f["deadline"]) + " |");
                foreach (string k in KindNames)
                {
                    var t = (Dictionary<string, object>)tasks[k];
                    md.Append(" " + ((bool)t["failed"] ? "FAILED" : t["sinceReady"] == null ? "—" : "+" + F(t["sinceReady"])) + " |");
                }
                md.AppendLine(" " + f["status"] + " | " + F(f["slackSeconds"]) + " |");
            }
            md.AppendLine().AppendLine("Task cells = seconds after the stand opened. Bot time split:").AppendLine();
            foreach (Dictionary<string, object> b in (List<object>)s["bots"])
                md.AppendLine("- seat " + b["seat"] + ": walking " + F(b["walkingSeconds"]) + "s, driving " + F(b["drivingSeconds"]) +
                    "s, stationary " + F(b["stationarySeconds"]) + "s (cart held: meals " + F(b["mealsCartSeconds"]) +
                    "s, baggage " + F(b["baggageCartSeconds"]) + "s, fuel " + F(b["fuelTruckSeconds"]) + "s) · **stalls ≥" + StallSeconds +
                    "s: " + b["stalls"] + " (" + F(b["stalledSeconds"]) + "s total, longest " + F(b["longestStillSeconds"]) + "s)**");
        }
        return md.ToString();
    }
}
