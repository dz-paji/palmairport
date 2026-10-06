using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using IslandAirport;

/// <summary>Bounded, offline M4 UI/memory acceptance; fixtures are explicitly disclosed in the report.</summary>
[InitializeOnLoad]
public static class M4CoopCapture
{
    const string Pending = "PalmBay.M4CoopCapture";
    const float Dt = .25f;
    static IEnumerator steps;
    static AirportGame game;
    static AirportHudCanvas hud;
    static AppState state;
    static double started;
    static bool runtimeErrors;
    static readonly StringBuilder report = new StringBuilder();
    static readonly FieldInfo storeField = typeof(AppState).GetField("botMemoryStore", BindingFlags.Instance | BindingFlags.NonPublic);
    static BotMemoryStore originalStore;
    static byte[] originalFile;
    static string originalPath, fixturePath;

    static M4CoopCapture() { EditorApplication.update += Poll; }
    [MenuItem("Palm Bay/Capture M4 cooperation evidence")]
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
                game = UnityEngine.Object.FindObjectOfType<AirportGame>();
                if (game == null) return;
                game.ExternalControl = true;
                state = AppState.Ensure(); state.ExternalControl = true;
                started = EditorApplication.timeSinceStartup;
                runtimeErrors = false;
                Application.logMessageReceived += OnLog;
                steps = Exercise();
            }
            Require(EditorApplication.timeSinceStartup - started <= 180, "Offline M4 capture exceeded 180 wall seconds");
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
        try
        {
            if (originalStore != null && state != null) storeField.SetValue(state, originalStore);
            if (originalPath != null)
            {
                byte[] current = File.Exists(originalPath) ? File.ReadAllBytes(originalPath) : null;
                bool same = BytesEqual(originalFile, current);
                if (!same)
                {
                    if (originalFile == null) File.Delete(originalPath);
                    else File.WriteAllBytes(originalPath, originalFile);
                    failed = true; details += "\nReal memory file changed unexpectedly; restored original bytes.";
                }
                report.AppendLine("Real per-device memory bytes preserved: " + same);
            }
        }
        catch (Exception e) { failed = true; details += "\nMemory restore failure: " + e; }
        failed |= runtimeErrors;
        Directory.CreateDirectory("Evidence");
        File.WriteAllText("Evidence/m4-coop-review.txt", "M4 offline cooperation review: " + (failed ? "FAIL" : "PASS") +
            "\nDetails: " + details + "\nUnity runtime errors: " + runtimeErrors + "\n" + report);
        Debug.Log((failed ? "M4_COOP_REVIEW_FAILED " : "M4_COOP_REVIEW_PASSED ") + details);
        SessionState.SetBool(Pending + ".Failed", failed);
        SessionState.SetBool(Pending + ".Stop", true);
        steps = null;
        EditorApplication.isPlaying = false;
    }

    static bool BytesEqual(byte[] a, byte[] b)
    {
        if (a == null || b == null) return a == b;
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    static IEnumerator Exercise()
    {
        // Let the actual Canvas Start run before taking over all simulation steps.
        double waitUntil = EditorApplication.timeSinceStartup + .7;
        while (EditorApplication.timeSinceStartup < waitUntil) yield return null;
        hud = UnityEngine.Object.FindObjectOfType<AirportHudCanvas>();
        Require(hud != null && storeField != null, "HUD/storage not available");
        originalPath = Path.Combine(Application.persistentDataPath, "palmbay-cooperation.json");
        originalFile = File.Exists(originalPath) ? File.ReadAllBytes(originalPath) : null;
        originalStore = (BotMemoryStore)storeField.GetValue(state);
        fixturePath = Path.Combine(Path.GetTempPath(), "palmbay-m4-memory-" + Guid.NewGuid().ToString("N") + ".json");
        var fixtureStore = new BotMemoryStore(() => string.Empty, json => File.WriteAllText(fixturePath, json));
        storeField.SetValue(state, fixtureStore);
        Require(state.RecordCoopMatch("preference-seed", 2, new[] { 8, 0, 4, 2 }, "2026-10-04"), "Seed preference fixture");
        report.AppendLine("FIXTURE: isolated temporary JSON store; original device file is backed up and restored. Seeded historical HUMAN counts Meals8/Baggage0/Fuel4/Boarding2, two departed flights. This is not player-earned history.");

        game.StartShift(true);
        hud.RefreshNow();
        Require(!game.Sim.Endless && hud.VisibleTimer == "05:00" && !hud.ResultsVisible, "Local finite HUD starts at five minutes");
        int parked = 0;
        // Keep the bot stationary until an actual aircraft finishes its taxi-in.
        game.Crew[1].Bot = false;
        while (!game.StandReady(0) && parked++ < 240)
        { game.Step(Dt); if (parked % 16 == 0) yield return null; }
        Flight flight = game.Sim.ActiveAtStand(0);
        Require(flight != null && game.StandReady(0), "First actual flight did not park within 60 sim seconds");
        Crew bot = game.Crew[1];
        bot.Bot = true; bot.Selected = game.Sim.Flights.IndexOf(flight);
        bot.Position = game.Carts[(int)ServiceKind.Baggage].Position;
        bot.Visual.position = bot.Position;
        game.Step(Dt);
        Require(bot.Cart != null && bot.Cart.Kind == ServiceKind.Baggage,
            "Actual BotInput must claim less-common baggage when meals/fuel alternatives exist");
        report.AppendLine("PASS actual AirportGame.BotInput selected and claimed baggage from historical preference; meals/fuel were unfinished and available.");

        // Domain attribution fixture: complete baggage as HUMAN0 and meals as BOT1.
        Require(game.Shift.ReleaseCart(1, (int)ServiceKind.Baggage), "Release preference probe cart");
        bot.Cart.Owner = null; bot.Cart = null;
        Require(game.Sim.ReturnArrivalBags(flight), "Attribution fixture arrival return");
        Require(game.Shift.TryClaimCart(0, (int)ServiceKind.Baggage), "Human fixture claim");
        Require(game.Shift.LoadCart(0, (int)ServiceKind.Baggage, flight.Id), "Human fixture load");
        Require(game.Shift.Deliver(0, (int)ServiceKind.Baggage, flight.Id, true, 5f), "Human fixture delivery");
        game.Shift.EndFrame();
        Require(game.Shift.ReleaseCart(0, (int)ServiceKind.Baggage), "Human fixture release");
        Require(game.Shift.OrderMeal(1), "Bot fixture order");
        game.Step(5f);
        Require(game.Shift.TakeMeal(1, (int)ServiceKind.Meals, flight.Id), "Bot fixture takes meal");
        Require(game.Shift.Deliver(1, (int)ServiceKind.Meals, flight.Id, true, 5f), "Bot fixture meal delivery");
        game.Shift.EndFrame();
        Require(game.Shift.HumanTaskCounts[1] == 1 && game.Shift.HumanTaskCounts[0] == 0, "Bot completion cannot become human credit");
        report.AppendLine("FIXTURE: domain APIs completed one HUMAN baggage task and one BOT meal task to test attribution; this does not claim human play completed a full flight.");

        // Camera/actor arrangement is presentation-only; it never replaces simulation state or network authority.
        Vector3 botPosition = bot.Position;
        Vector3 cameraPosition = game.View.transform.position;
        float cameraSize = game.View.orthographicSize;
        game.View.orthographicSize = 3f;
        game.View.transform.position = game.Crew[0].Position - game.View.transform.forward * 30f;
        bot.Position = game.Crew[0].Position + Vector3.right * 40f;
        bot.Visual.position = bot.Position;
        foreach (Transform actor in UnityEngine.Object.FindObjectsOfType<Transform>())
            if (actor.parent == null && actor.name == bot.Name)
                report.AppendLine("BOT visual root diagnostic: position=" + actor.position + " active=" + actor.gameObject.activeSelf + " isCurrent=" + (actor == bot.Visual));
        hud.ReviewSafeAreaNormalized = new Rect(.04f, .03f, .92f, .94f);
        report.AppendLine("FIXTURE: camera crop + partner at +40 world-x, normalized safe insets4% horizontal/3% vertical, verified separately at16:9 and19.5:9; not real-device notch certification.");
        Shot("Evidence/m4-coop-offscreen-16x9.png", 1920, 1080, true);
        Shot("Evidence/m4-coop-offscreen-19_5x9.png", 2340, 1080, true);
        bot.Position = game.Crew[0].Position + Vector3.right * .5f;
        bot.Visual.position = bot.Position;
        hud.RefreshNow(); Canvas.ForceUpdateCanvases(); hud.RefreshNow();
        Require(string.IsNullOrEmpty(hud.VisibleTeammateIndicator), "Offscreen indicator remained stale after partner returned onscreen");
        report.AppendLine("PASS returning onscreen hides the offscreen indicator.");
        game.View.transform.position = cameraPosition; game.View.orthographicSize = cameraSize;
        bot.Position = botPosition; bot.Visual.position = bot.Position;
        hud.ReviewSafeAreaNormalized = null;

        int totalSteps = 0;
        while (!game.MatchFinished && totalSteps++ < 1201)
        { game.Step(Dt); if (totalSteps % 32 == 0) yield return null; }
        Require(game.MatchFinished && game.DisplayElapsed == 300f && game.DisplayRemaining == 0f, "Finite offline match did not freeze at300 seconds");
        hud.RefreshNow();
        Require(game.DisplayRemaining == 0f && hud.VisibleTimer == string.Empty && hud.ResultsVisible && hud.RetryInteractable, "M6 result layout hides live timer while keeping frozen result/retry controls");
        Require(game.ReplayAvailable && game.ReplayFrameCount > 1, "Offline replay requires actual recorded match history");
        Require(state.BotMemory.SharedRounds == 2 && state.BotMemory.HumanTaskCount(1) == 1 && state.BotMemory.HumanTaskCount(0) == 8,
            "Settlement recorded exactly one round with only human contributions");
        int score = game.DisplayScore; int rounds = state.BotMemory.SharedRounds;
        for (int i = 0; i < 20; i++) game.Step(Dt);
        Require(game.DisplayElapsed == 300f && game.DisplayScore == score && state.BotMemory.SharedRounds == rounds, "Finished updates duplicated result/memory");
        float replayBefore = game.ReplayTime;
        game.Step(.25f);
        Require(game.ReplayTime > replayBefore, "Automatic replay advances from real recorded history");
        for (int i = 0; i < 120 && game.ReplayFraction < 1f; i++) game.Step(.25f);
        Require(game.ReplayFraction == 1f, "Automatic replay reaches final hold within thirty sim seconds");
        float heldTime = game.ReplayTime;
        game.Step(1f);
        Require(game.ReplayTime == heldTime, "Automatic replay stays on its final frame");
        Shot("Evidence/m4-coop-results.png", 1920, 1080, false);
        BotMemory disk = BotMemory.FromJson(File.ReadAllText(fixturePath));
        Require(disk != null && disk.SharedRounds == rounds && disk.HumanTaskCount(1) == 1, "Isolated JSON disk reload");
        report.AppendLine("PASS bounded fixed-step local300-second match, frozen score/time, actual automatic replay/finalhold, human-only settlement once and JSON reload. score=" + score + " stars=" + game.DisplayStars + " frames=" + game.ReplayFrameCount);

        game.Retry(); hud.RefreshNow();
        Require(!game.MatchFinished && game.DisplayElapsed == 0f && hud.VisibleTimer == "05:00" && !hud.ResultsVisible, "Retry must reset local finite HUD/results");
        // A second finite round demonstrates a new identity without borrowing a previous finish callback.
        for (int i = 0; i < 1201 && !game.MatchFinished; i++)
        { game.Step(Dt); if (i % 32 == 31) yield return null; }
        Require(game.MatchFinished && state.BotMemory.SharedRounds == rounds + 1, "Legitimate retry must settle one distinct round");
        game.Step(Dt);
        Require(state.BotMemory.SharedRounds == rounds + 1, "Retried finish must stay idempotent");
        report.AppendLine("PASS retry completed a distinct finite300-second round and credited exactly once.");
        File.Delete(fixturePath);
    }

    static void Shot(string path, int width, int height, bool requireIndicator)
    {
        Camera camera = game.View;
        RenderTexture previousTarget = camera.targetTexture, previousActive = RenderTexture.active;
        float previousAspect = camera.aspect;
        var target = new RenderTexture(width, height, 24);
        Texture2D pixels = null;
        try
        {
            camera.targetTexture = target; camera.aspect = width / (float)height;
            Canvas.ForceUpdateCanvases(); hud.RefreshNow(); Canvas.ForceUpdateCanvases(); hud.RefreshNow();
            Require(hud.ToolbarInsideSafeArea && !hud.DeparturePillOverlapsBoard, "Top toolbar must stay inside safe area and departure pill must not obscure flight deadline board");
            report.AppendLine("PASS visible toolbar and unobstructed deadline board " + width + "x" + height);
            if (requireIndicator)
            {
                Vector3 actualVisualViewport = camera.WorldToViewportPoint(game.Crew[1].Visual.position);
                Require(actualVisualViewport.z <= 0 || actualVisualViewport.x < 0 || actualVisualViewport.x > 1 || actualVisualViewport.y < 0 || actualVisualViewport.y > 1,
                    "Offscreen fixture must move the actual current BOT visual outside the camera, not only its logical position");
                report.AppendLine("Current BOT visual viewport=" + actualVisualViewport + " world=" + game.Crew[1].Visual.position);
                Require(hud.VisibleTeammateIndicator.Contains(game.Crew[1].Name) && hud.VisibleTeammateIndicator.Contains("BOT"), "Indicator must include teammate name and BOT");
                Require(hud.TeammateIndicatorInsideSafeArea, "Full offscreen tag must stay inside safe-area bounds");
                report.AppendLine("PASS safe-area indicator " + width + "x" + height + " position=" + hud.TeammateIndicatorPosition + " label=" + hud.VisibleTeammateIndicator);
            }
            camera.Render(); RenderTexture.active = target;
            pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, pixels.EncodeToPNG());
            report.AppendLine(path + " " + width + "x" + height);
        }
        finally
        {
            camera.targetTexture = previousTarget; camera.aspect = previousAspect; RenderTexture.active = previousActive;
            if (pixels) UnityEngine.Object.DestroyImmediate(pixels);
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
