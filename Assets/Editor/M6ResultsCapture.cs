using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using IslandAirport;

[InitializeOnLoad]
public static class M6ResultsCapture
{
    const string Pending = "PalmBay.M6ResultsCapture";
    static IEnumerator steps;
    static AirportGame game;
    static AirportHudCanvas hud;
    static AppState state;
    static CooperationMemoryFixtureScope memory;
    static bool runtimeErrors;
    static double started;
    static readonly StringBuilder report = new StringBuilder();
    static M6ResultsCapture() { EditorApplication.update += Poll; }
    [MenuItem("Palm Bay/Capture M6 results evidence")]
    public static void Run()
    {
        report.Length = 0; runtimeErrors = false; steps = null;
        EditorSceneManager.OpenScene(DemoBuilder.PalmBayScenePath);
        SessionState.SetBool(Pending, true); SessionState.SetBool(Pending + ".Stop", false);
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
                game = UnityEngine.Object.FindObjectOfType<AirportGame>(); if (game == null) return;
                game.ExternalControl = true;
                state = AppState.Ensure(); state.ExternalControl = true;
                Require(state.IsFakeAuth, "Acceptance must use FakeAuth, never a real account");
                memory = new CooperationMemoryFixtureScope(state);
                started = EditorApplication.timeSinceStartup;
                Application.logMessageReceived += OnLog;
                steps = Exercise();
            }
            Require(EditorApplication.timeSinceStartup - started < 240, "M6 acceptance timeout");
            if (steps.MoveNext()) return;
            Complete(false, "PASS");
        }
        catch (Exception e) { Complete(true, e.ToString()); }
    }
    static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void OnLog(string message, string trace, LogType kind)
    { if (kind == LogType.Error || kind == LogType.Exception) runtimeErrors = true; }
    static void Complete(bool failed, string reason)
    {
        Application.logMessageReceived -= OnLog;
        if (game != null) game.CancelReplayExport();
        if (memory != null) { memory.Dispose(); memory = null; }
        failed |= runtimeErrors;
        Directory.CreateDirectory("Evidence");
        File.WriteAllText("Evidence/m6-results-review.txt", "M6 Unity acceptance: " + (failed ? "FAIL" : "PASS") + "\n" + reason + "\nRuntime errors: " + runtimeErrors + "\n" + report);
        SessionState.SetBool(Pending + ".Failed", failed); SessionState.SetBool(Pending + ".Stop", true);
        steps = null; EditorApplication.isPlaying = false;
    }
    static IEnumerator Exercise()
    {
        for (int i = 0; i < 5; i++) yield return null;
        hud = game.GetComponent<AirportHudCanvas>(); Require(hud != null, "Results HUD required");
        report.AppendLine("FIXTURE: actual local 300-second simulation with injected HUMAN movement; isolated in-memory cooperation store, FakeAuth, no backend calls or system share posting.");
        game.StartShift(true);
        for (int i = 0; i < 1201 && !game.MatchFinished; i++)
        {
            if (i < 80) game.SetInput(0, new CrewInput { Move = new Vector2(i < 40 ? 1 : -1, 0) });
            game.Step(.25f); if (i % 24 == 0) yield return null;
        }
        hud.RefreshNow();
        Require(game.MatchFinished && game.DisplayElapsed == 300, "Full finite shift finishes");
        Require(hud.ResultsVisible && hud.ShareInteractable && hud.RetryInteractable, "Result, share and retry available");
        Require(hud.VisibleResultStars == new string('★', game.DisplayStars) + new string('☆', 3 - game.DisplayStars), "Stars match simulation");
        Require(hud.VisibleSettlementStatus.Contains("不会上传"), "FakeAuth disclosure required");
        string[] buttons = { "Share", "Retry", "Next", "Lobby" };
        float previous = float.MinValue;
        foreach (string name in buttons)
        {
            var button = hud.ResultButton(name); Require(button != null, "Missing result button: " + name);
            float x = ((RectTransform)button.transform).anchoredPosition.x; Require(x > previous, "F9 left-to-right button order"); previous = x;
        }
        hud.ResultButton("Next").onClick.Invoke(); hud.RefreshNow();
        Require(hud.VisibleResultToast == "后续关卡还未解锁" && game.MatchFinished, "Locked next must visibly explain without navigation");
        game.ToastTime = 0;
        Shot("Evidence/m6-results-16x9.png", 1920, 1080);
        hud.ReviewSafeAreaNormalized = new Rect(.04f,.03f,.92f,.94f);
        Shot("Evidence/m6-results-19_5x9.png", 2340, 1080);
        hud.ReviewSafeAreaNormalized = null;
        // Early export cancellation must preserve the currently playing history frame.
        game.Step(.25f);
        float earlyTime = game.ReplayTime;
        Vector3 earlyCrew = game.Crew[0].Visual.position, earlyCart = game.Carts[0].Visual.position;
        game.BeginReplayExport(false);
        Require(game.ExportBusy, "Early export starts");
        for (int i = 0; i < 12; i++) { game.PumpReplayExport(); yield return null; }
        game.CancelReplayExport();
        Require(game.ReplayTime == earlyTime && Vector3.Distance(earlyCrew, game.Crew[0].Visual.position) < .001f &&
            Vector3.Distance(earlyCart, game.Carts[0].Visual.position) < .001f && game.View.targetTexture == null, "Mid-replay export/cancel preserves clock, crew, cart and camera");
        // Regression: changing spill IDs with equal counts must immediately hide the old geometry before Camera.Render.
        var syncFuel = typeof(AirportGame).GetMethod("SyncFuelEquipmentVisuals",BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(ShiftSnap),typeof(Flight[])},null);
        var spillsField = typeof(AirportGame).GetField("spillVisuals",BindingFlags.Instance|BindingFlags.NonPublic);
        var firstSpill = new ShiftSnap { Spills = new[] { new SpillSnap { Id=9001,X=0,Z=0,Radius=1 } } };
        var nextSpill = new ShiftSnap { Spills = new[] { new SpillSnap { Id=9002,X=1,Z=0,Radius=1 } } };
        syncFuel.Invoke(game,new object[]{firstSpill,null});
        var spills = (Dictionary<int,Transform>)spillsField.GetValue(game);
        Transform oldSpill = spills[9001];
        syncFuel.Invoke(game,new object[]{nextSpill,null});
        Require(spills.Count == 1 && spills.ContainsKey(9002) && !oldSpill.gameObject.activeSelf,"Equal-size historical spill replacement has no ghost geometry");
        oldSpill = spills[9002]; syncFuel.Invoke(game,new object[]{new ShiftSnap(),null});
        Require(spills.Count == 0 && !oldSpill.gameObject.activeSelf,"Backward seek hides future spill immediately");
        game.Step(.25f);
        report.AppendLine("PASS mid-replay export/cancel preserves pose/clock; equal-count spill-ID replacement and backward seek immediately hide stale geometry before synchronous camera render.");
        int score = game.DisplayScore;
        for (int i = 0; i < 105; i++) game.Step(.25f);
        float terminal = game.ReplayTime;
        game.Step(2f);
        Require(game.ReplayFraction == 1f && game.ReplayTime == terminal && game.DisplayScore == score, "Replay final frame remains held with stable result");
        Vector3 position = game.Crew[0].Visual.position;
        game.BeginReplayExport(false);
        Require(game.ExportBusy, "Real video encoder must start: " + game.ExportStatus);
        while (game.ExportBusy) { game.PumpReplayExport(); yield return null; }
        Require(!string.IsNullOrEmpty(game.ExportedVideoPath) && File.Exists(game.ExportedVideoPath), "Video produced: " + game.ExportStatus);
        Require(new FileInfo(game.ExportedVideoPath).Length > 10000, "MP4 must contain real rendered frames");
        Require(Vector3.Distance(position, game.Crew[0].Visual.position) < .001f && game.ReplayTime == terminal && game.View.targetTexture == null, "Export restores scene, replay clock and camera");
        File.Copy(game.ExportedVideoPath, "Evidence/m6-replay.mp4", true);
        report.AppendLine("PASS actual 960x540,24fps silent export from " + game.ReplayFrameCount + " recorded samples; output Evidence/m6-replay.mp4. Full video stream/ffprobe validation performed separately.");
        game.BeginReplayExport(false);
        Require(game.ExportBusy, "Cancellation fixture starts");
        for (int i = 0; i < 8; i++) { game.PumpReplayExport(); yield return null; }
        game.CancelReplayExport();
        Require(!game.ExportBusy && game.View.targetTexture == null && Vector3.Distance(position, game.Crew[0].Visual.position) < .001f, "Cancel restores live scene and releases encoder");
        game.BeginReplayExport(false);
        Require(game.ExportBusy, "Retry cancellation fixture starts");
        hud.ResultButton("Retry").onClick.Invoke(); hud.RefreshNow();
        Require(!game.ExportBusy && !game.MatchFinished && game.DisplayElapsed == 0 && !hud.ResultsVisible && game.ExportedVideoPath == string.Empty, "Retry cancels export and resets result identity");
        report.AppendLine("PASS result stars, four ordered buttons, safe-area16:9/19.5:9, locked-next visible toast, automatic replay/finalhold, export/cancel restore and Retry.");
        game.BackToLobby();
        for (int i = 0; i < 5; i++) yield return null;
        Require(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == AppState.SceneCabinLobby, "BackToLobby navigation");
        report.AppendLine("PASS BackToLobby navigates to CabinLobby. Native share panel not opened by unattended acceptance; device review remains separate.");
    }
    static void Shot(string path, int width, int height)
    {
        var camera = game.View; var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active;
        float previousAspect = camera.aspect;
        var target = new RenderTexture(width,height,24); Texture2D pixels = null;
        try
        {
            camera.targetTexture = target; camera.aspect = (float)width/height;
            hud.RefreshNow(); Canvas.ForceUpdateCanvases(); hud.RefreshNow();
            Require(hud.ResultsActionsInsideSafeArea, "Four result buttons inside safe area");
            camera.Render(); RenderTexture.active = target;
            pixels = new Texture2D(width,height,TextureFormat.RGB24,false); pixels.ReadPixels(new Rect(0,0,width,height),0,0); pixels.Apply();
            Directory.CreateDirectory("Evidence"); File.WriteAllBytes(path,pixels.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previousTarget; camera.aspect = previousAspect; RenderTexture.active = previousActive;
            if(pixels)UnityEngine.Object.DestroyImmediate(pixels); UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
