using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using IslandAirport;

/// <summary>
/// M3.2 回放失败归因验收：固定 Step 推进真实 AirportGame 交互，完成一次错送，
/// 结束整班后验证结算遮罩提示、提示计时、重试清理，并保存可见状态截图。
/// 用法：-executeMethod M3ReplayFailureCapture.Run（不要加 -quit，完成后自行退出）。
/// </summary>
[InitializeOnLoad]
public static class M3ReplayFailureCapture
{
    static IEnumerator steps;
    static CooperationMemoryFixtureScope memoryScope;
    static AirportGame game;
    static AirportHudCanvas hud;
    static double started;
    static bool failed;
    static readonly StringBuilder report = new StringBuilder();
    const string Pending = "PalmBay.M3ReplayFailureCapture";
    const float Dt = 0.05f;
    const string ReceiverId = "RX-318";
    const string SourceId = "SRC-724";

    static M3ReplayFailureCapture() { EditorApplication.update += Poll; }

    [MenuItem("Palm Bay/Capture M3 replay failure evidence")]
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
                game = UnityEngine.Object.FindObjectOfType<AirportGame>();
                if (game == null) return;
                memoryScope = new CooperationMemoryFixtureScope(AppState.Ensure());
                started = EditorApplication.timeSinceStartup;
                failed = false;
                Application.logMessageReceived += OnLog;
                steps = Exercise();
            }
            if (EditorApplication.timeSinceStartup - started > 180)
                throw new Exception("M3 replay capture exceeded 180 seconds");
            if (steps.MoveNext()) return;
            Complete(false, "PASS");
        }
        catch (Exception e) { Complete(true, e + "\n" + StateDiagnostic()); }
    }

    static void OnLog(string message, string stack, LogType kind)
    {
        if (kind == LogType.Exception || kind == LogType.Error) failed = true;
    }

    static void Complete(bool error, string message)
    {
        Application.logMessageReceived -= OnLog;
        if (memoryScope != null) { memoryScope.Dispose(); memoryScope = null; }
        error |= failed;
        Directory.CreateDirectory("Evidence");
        File.WriteAllText("Evidence/m3-baggage-replay.txt",
            "M3.2 replay failure capture: " + (error ? "FAIL" : message) + "\n" +
            "Details: " + message + "\n" +
            "Unity runtime errors: " + failed + "\n" + report);
        Debug.Log((error ? "M3_REPLAY_CAPTURE_FAILED " : "M3_REPLAY_CAPTURE_PASSED ") + message);
        SessionState.SetBool(Pending + ".Failed", error);
        SessionState.SetBool(Pending + ".Stop", true);
        steps = null;
        EditorApplication.isPlaying = false;
    }

    static void Shot(string path, int width, int height)
    {
        if (!game.View) throw new Exception("No game camera");
        hud.RefreshNow();
        Canvas.ForceUpdateCanvases();
        var rt = new RenderTexture(width, height, 24);
        Camera cam = game.View;
        RenderTexture oldTarget = cam.targetTexture;
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
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, image.EncodeToPNG());
        RenderTexture.active = null;
        cam.targetTexture = oldTarget;
        cam.aspect = oldAspect;
        UnityEngine.Object.DestroyImmediate(image);
        UnityEngine.Object.DestroyImmediate(rt);
        report.AppendLine(path + "  " + width + "x" + height);
    }

    static IEnumerator Exercise()
    {
        yield return WaitReal(1f);
        hud = UnityEngine.Object.FindObjectOfType<AirportHudCanvas>();
        if (hud == null) throw new Exception("AirportHudCanvas was not created");

        game.ExternalControl = true;
        game.StartShift(false);

        // Prepare two real service flights and a baggage cart at the receiver stand.
        // The test still completes the wrong delivery through SetInput + Step -> Interact -> ShiftSim.Deliver.
        Flight receiver = new Flight(ReceiverId, "Palm Bay", 0f, AirportSimulation.ShiftDuration + 1f, 0)
        { Status = FlightStatus.Servicing };
        Flight source = new Flight(SourceId, "Coral Cape", 0f, AirportSimulation.ShiftDuration + 1f, 1)
        { Status = FlightStatus.Servicing };
        game.Sim.Flights.Clear();
        game.Sim.Flights.Add(receiver);
        game.Sim.Flights.Add(source);
        // Updating the visible aircraft starts real taxi-in motion and marks each stand busy.
        // Advance fixed simulation steps until both arrivals complete, as the M1 review capture does.
        int parkingSteps = 0;
        while (parkingSteps < 600 && (!game.StandReady(0) || !game.StandReady(1)))
        {
            game.Step(Dt);
            parkingSteps++;
            if (parkingSteps % 16 == 0) yield return null;
        }
        if (receiver.Status != FlightStatus.Servicing || source.Status != FlightStatus.Servicing ||
            !game.StandReady(0) || !game.StandReady(1))
            throw new Exception("Fixture flights did not reach servicing with both planes parked within 30 sim seconds: " + StateDiagnostic());
        report.AppendLine("Both fixture planes completed real taxi-in after " + parkingSteps + " fixed steps at " + game.Sim.Elapsed + " seconds");

        Crew crew = game.Crew[0];
        Cart baggage = game.Carts[(int)ServiceKind.Baggage];
        crew.Position = AirportGame.Dock(0);
        crew.Visual.position = crew.Position;
        crew.Cart = baggage;
        baggage.Owner = crew;
        baggage.Position = crew.Position;
        baggage.Visual.position = baggage.Position;
        game.Shift.Carts[(int)ServiceKind.Baggage].CargoFlightId = source.Id;

        if (receiver.Status != FlightStatus.Servicing || source.Status != FlightStatus.Servicing ||
            !game.StandReady(0) || !game.StandReady(1))
            throw new Exception("Wrong-delivery interaction preconditions were lost before input: " + StateDiagnostic());

        bool failedBaggage = false;
        for (int i = 0; i < 80 && !failedBaggage; i++)
        {
            game.SetInput(crew.Index, new CrewInput { Held = true });
            game.Step(Dt);
            failedBaggage = receiver.IsTaskFailed(ServiceKind.Baggage);
            if (i % 16 == 15) yield return null;
        }
        if (!failedBaggage) throw new Exception("Wrong baggage delivery did not fail the receiver task: " + StateDiagnostic());
        if (baggage.Owner != crew || crew.Cart != baggage)
            throw new Exception("Wrong delivery did not follow the carried-cart interaction path: " + StateDiagnostic());

        ShiftSnap eventSnapshot = game.Shift.CaptureShift();
        int wrongCount = 0;
        int taskFailedCount = 0;
        bool attributionRecorded = false;
        for (int i = 0; i < eventSnapshot.Events.Length; i++)
        {
            ShiftEventSnap e = eventSnapshot.Events[i];
            if (e == null) continue;
            if (e.Type == ShiftEventTypes.WrongDelivery && e.FlightId == ReceiverId && e.RelatedFlightId == SourceId)
                wrongCount++;
            if (e.Type == ShiftEventTypes.TaskFailed && e.FlightId == ReceiverId && e.RelatedFlightId == SourceId)
            {
                taskFailedCount++;
                attributionRecorded = e.Text.Contains(ReceiverId) && e.Text.Contains(SourceId);
            }
        }
        if (wrongCount != 1 || taskFailedCount != 1 || !attributionRecorded)
            throw new Exception("ShiftSim did not record exactly one receiver/source failure attribution: " + StateDiagnostic());
        report.AppendLine("Real local interaction completed one wrong baggage delivery: " + ReceiverId + " <- " + SourceId);

        int shiftSteps = Mathf.CeilToInt((AirportSimulation.ShiftDuration - game.Sim.Elapsed) / Dt);
        for (int i = 0; i < shiftSteps && !game.Sim.Finished; i++)
        {
            game.Step(Dt);
            if (i % 100 == 99) yield return null;
        }
        if (!game.Sim.Finished) throw new Exception("Fixed-step progression did not finish the local shift");
        if (!receiver.IsTaskFailed(ServiceKind.Baggage)) throw new Exception("Failure state was not preserved through shift end");
        report.AppendLine("Fixed Step(" + Dt + ") progression reached shift end at " + game.Sim.Elapsed + " seconds");

        bool noticeReached = false;
        for (int i = 0; i < 120 && !noticeReached; i++)
        {
            game.Step(Dt);
            hud.RefreshNow();
            noticeReached = game.ReplayNoticeCount > 0;
            if (i % 16 == 15) yield return null;
        }
        if (!noticeReached) throw new Exception("Replay never crossed the recorded task_failed frame");
        if (game.ReplayNoticeCount != 1) throw new Exception("Failure replay notice was triggered " + game.ReplayNoticeCount + " times");
        if (!game.ReplayNotice.Contains(ReceiverId) || !game.ReplayNotice.Contains(SourceId))
            throw new Exception("Replay notice omitted receiver/source IDs: " + game.ReplayNotice);
        string visible = hud.VisibleResultsHint;
        if (!visible.Contains(ReceiverId) || !visible.Contains(SourceId))
            throw new Exception("Visible settlement hint omitted receiver/source IDs: " + visible);
        report.AppendLine("Visible resultsHint: " + visible);
        Shot("Evidence/m3-baggage-replay.png", 1920, 1080);

        int replaySteps = 0;
        while (game.ReplayNoticeRemaining > 0f && replaySteps < 100)
        {
            game.Step(Dt);
            hud.RefreshNow();
            replaySteps++;
            if (replaySteps % 16 == 0) yield return null;
        }
        if (game.ReplayNoticeRemaining > 0f || !string.IsNullOrEmpty(game.ReplayNotice))
            throw new Exception("Replay notice did not expire through Step(dt)");
        if (game.ReplayNoticeCount != 1)
            throw new Exception("Failure notice retriggered while replay advanced; count=" + game.ReplayNoticeCount);
        if (hud.VisibleResultsHint.Contains(ReceiverId) || hud.VisibleResultsHint.Contains(SourceId))
            throw new Exception("Expired failure attribution remained in the visible hint");
        report.AppendLine("Replay notice expired after " + replaySteps + " fixed 0.05 second steps with one trigger");

        game.Retry();
        hud.RefreshNow();
        if (!string.IsNullOrEmpty(game.ReplayNotice) || game.ReplayNoticeRemaining != 0f || game.ReplayNoticeCount != 0)
            throw new Exception("Retry did not clear replay notice state");
        if (game.Sim == null || game.Sim.Finished) throw new Exception("Retry did not start a fresh local shift");
        report.AppendLine("Retry cleared replay notice and started a fresh local shift: PASS");
    }

    static string StateDiagnostic()
    {
        if (game == null) return "Diagnostics: AirportGame unavailable";
        AirportSimulation sim = game.Sim;
        ShiftSim shift = game.Shift;
        Flight receiver = null, source = null;
        if (sim != null)
        {
            for (int i = 0; i < sim.Flights.Count; i++)
            {
                Flight f = sim.Flights[i];
                if (f == null) continue;
                if (f.Id == ReceiverId) receiver = f;
                if (f.Id == SourceId) source = f;
            }
        }
        Crew crew = game.Crew.Count > 0 ? game.Crew[0] : null;
        Cart baggage = null;
        for (int i = 0; i < game.Carts.Count; i++)
            if (game.Carts[i] != null && game.Carts[i].Kind == ServiceKind.Baggage) baggage = game.Carts[i];
        ShiftCart baggageState = shift != null && shift.Carts.Length > (int)ServiceKind.Baggage
            ? shift.Carts[(int)ServiceKind.Baggage] : null;
        string crewPosition = crew == null ? "n/a" : crew.Position.ToString("F2");
        string cartPosition = baggage == null ? "n/a" : baggage.Position.ToString("F2");
        return string.Format(
            "Diagnostics: elapsed={0:0.00}, standReady=[0:{1},1:{2}], flights=[{3}:{4}, {5}:{6}], receiverBaggageFailed={7}, work={8:0.00}, cargo={9}, cargoArrival={10}, crewPos={11}, cartPos={12}, cartOwner={13}",
            sim == null ? -1f : sim.Elapsed,
            game.StandReady(0), game.StandReady(1),
            ReceiverId, receiver == null ? "missing" : receiver.Status.ToString(),
            SourceId, source == null ? "missing" : source.Status.ToString(),
            receiver != null && receiver.IsTaskFailed(ServiceKind.Baggage),
            baggageState == null ? 0f : baggageState.DeliverWork,
            baggageState == null ? "n/a" : (baggageState.Loaded ? baggageState.CargoFlightId : "empty"),
            baggageState != null && baggageState.Arrival,
            crewPosition, cartPosition,
            baggage == null || baggage.Owner == null ? "none" : baggage.Owner.Index.ToString());
    }

    static IEnumerator WaitReal(float seconds)
    {
        double end = EditorApplication.timeSinceStartup + seconds;
        while (EditorApplication.timeSinceStartup < end) yield return null;
    }
}
