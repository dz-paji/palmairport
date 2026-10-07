using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using IslandAirport;

/// <summary>
/// M1 审查证据生成：在 PalmBay 产品场景的 Play Mode 中合成"忙碌班岗"构图，
/// 用 RenderTexture + ReadPixels 截图（batchmode 下 ScreenCapture 无产物）。
/// Canvas 为 Screen Space - Camera，UI 随相机一并渲进 RT。
/// 玩法推进不走真实时间：设置 AirportGame.ExternalControl 后以固定步长调 Step，
/// batchmode 播放器循环停摆不影响结果，构图完全确定。
/// 用法：-executeMethod M1ReviewCapture.Run（不要加 -quit，完成后自行退出）。
/// </summary>
[InitializeOnLoad]
public static class M1ReviewCapture
{
    static string EvidenceRoot { get { return SessionState.GetString("M1ReviewCapture.Evidence", Environment.GetEnvironmentVariable("PALMBAY_VISUAL_EVIDENCE") ?? "Evidence"); } }
    static IEnumerator steps;
    static CooperationMemoryFixtureScope memoryScope;
    static AirportGame game;
    static AirportHudCanvas hud;
    static double started;
    static bool failed;
    static readonly System.Text.StringBuilder report = new System.Text.StringBuilder();
    const string Pending = "PalmBay.M1ReviewCapture";
    const float Dt = 0.05f; // 固定步长（秒/步）
    static M1ReviewCapture() { EditorApplication.update += Poll; }

    [MenuItem("Palm Bay/Capture M1 review screenshots")]
    public static void Run()
    {
        SessionState.EraseString("M1ReviewCapture.Evidence");
        EditorSceneManager.OpenScene(DemoBuilder.PalmBayScenePath);
        SessionState.SetBool(Pending, true); SessionState.SetBool(Pending + ".Stop", false);
        EditorApplication.isPlaying = true;
    }

    [MenuItem("Palm Bay/Capture visual polish - airport")]
    public static void RunVisualPolish()
    {
        Run();
        SessionState.SetString("M1ReviewCapture.Evidence", "Evidence/visual-polish/after");
        Directory.CreateDirectory(EvidenceRoot);
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
        // 泵一次播放器循环让各 Start 跑完（玩法本身由 Step 驱动，不依赖它）。
        EditorApplication.QueuePlayerLoopUpdate();
        try
        {
            if (steps == null)
            {
                game = UnityEngine.Object.FindAnyObjectByType<AirportGame>();
                if (game == null) return;
                memoryScope = new CooperationMemoryFixtureScope(AppState.Ensure());
                started = EditorApplication.timeSinceStartup; failed = false;
                Application.logMessageReceived += OnLog;
                steps = Exercise();
            }
            if (EditorApplication.timeSinceStartup - started > 150) throw new Exception("M1 capture exceeded 150 seconds");
            if (steps.MoveNext()) return;
            Complete(false, "PASS");
        }
        catch (Exception e) { Complete(true, e.ToString()); }
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
        Directory.CreateDirectory(EvidenceRoot);
        File.WriteAllText(Path.Combine(EvidenceRoot, "m1-review.txt"),
            "M1 review capture: " + (error ? "FAIL" : message) + "\n" +
            "Unity runtime errors: " + failed + "\n" + report);
        Debug.Log((error ? "M1_REVIEW_FAILED " : "M1_REVIEW_PASSED ") + message);
        SessionState.SetBool(Pending + ".Failed", error);
        SessionState.SetBool(Pending + ".Stop", true);
        steps = null;
        EditorApplication.isPlaying = false;
    }

    static void Shot(string path, int width, int height, bool reframe = true)
    {
        Camera cam = game.View;
        if (!cam) throw new Exception("No game camera");
        // 播放器循环可能停摆，HUD 的 LateUpdate 不保证执行，Render 前手动刷新。
        if (hud != null) hud.RefreshNow();
        Canvas.ForceUpdateCanvases();
        var rt = new RenderTexture(width, height, 24);
        rt.antiAliasing = Mathf.Max(1, QualitySettings.antiAliasing);
        var oldTarget = cam.targetTexture; float oldAspect = cam.aspect;
        cam.targetTexture = rt; cam.aspect = width / (float)height;
        if (reframe) Level1Map.ConfigureCamera(cam, false);
        Canvas.ForceUpdateCanvases();
        cam.Render();
        RenderTexture.active = rt;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
        File.WriteAllBytes(path, image.EncodeToPNG());
        RenderTexture.active = null;
        cam.targetTexture = oldTarget; cam.aspect = oldAspect;
        Level1Map.ConfigureCamera(cam, false);
        UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(rt);
        report.AppendLine(path + "  " + width + "x" + height);
    }

    static IEnumerator WaitReal(float seconds)
    {
        double end = EditorApplication.timeSinceStartup + seconds;
        while (EditorApplication.timeSinceStartup < end) yield return null;
    }

    static IEnumerator Exercise()
    {
        yield return WaitReal(1.0f);
        hud = UnityEngine.Object.FindAnyObjectByType<AirportHudCanvas>();

        // 接管玩法循环：之后一切推进都靠固定步长 Step，确定性构图。
        game.ExternalControl = true;
        game.StartShift(false);

        // 步进到首班飞机停稳（StandReady 由滑入动画末段置位，精确确定）。
        int stand = -1; Flight flight = null;
        for (int i = 0; i < 1600 && flight == null; i++)
        {
            game.Step(Dt);
            if (i % 16 == 15) yield return null; // 让编辑器喘息，避免长帧卡顿看门狗
            for (int s = 0; s < Level1Map.StandCount; s++)
            {
                Flight f = game.Sim.ActiveAtStand(s);
                if (f != null && game.StandReady(s)) { stand = s; flight = f; break; }
            }
        }
        if (flight == null) throw new Exception("No flight parked within 1600 steps");
        Debug.Log("M1CAP parked stand=" + stand + " flight=" + flight.Id + " elapsed=" + game.Sim.Elapsed);

        // 合成忙碌时刻：餐食/燃油已完成，P1 登机口放行旅客；
        // P2 走完整行李流程（取到达→送回站→装出发→机位按住交付），进度条可见。
        Crew p1 = game.Crew[0], p2 = game.Crew[1];
        Cart cart = game.Carts[1]; // 行李车
        flight.Progress[0] = 1; flight.Progress[2] = 1; // 餐食/燃油完成 → 可放行
        p1.Position = AirportGame.Station(ServiceKind.Boarding) + new Vector3(0.5f, 0, 0.5f);
        p2.Cart = cart; cart.Owner = p2;
        p2.Selected = game.Sim.Flights.IndexOf(flight);

        game.SetInput(0, new CrewInput { Pressed = true, Held = true });
        game.Step(Dt);
        Debug.Log("M1CAP gate: passengers=" + game.Shift.Passengers.Count + " toast=" + game.Toast);
        if (game.Shift.Passengers.Count == 0) throw new Exception("Boarding gate did not open");

        // P2：机位取到达行李 → 行李站归还 → 装出发行李 → 回机位。
        p2.Position = Level1Map.Dock(stand) + new Vector3(0.4f, 0, 0.4f); cart.Position = p2.Position;
        game.SetInput(1, new CrewInput { Pressed = true }); game.Step(Dt);   // 取下到达行李
        if (!game.Shift.Carts[1].Loaded || !game.Shift.Carts[1].Arrival) throw new Exception("Arrival baggage pickup failed");
        p2.Position = AirportGame.Station(ServiceKind.Baggage) + new Vector3(0.5f, 0, 0.5f); cart.Position = p2.Position;
        game.SetInput(1, new CrewInput { Pressed = true }); game.Step(Dt);   // 归还到达行李
        game.SetInput(1, new CrewInput { Pressed = true }); game.Step(Dt);   // 装出发行李
        if (!game.Shift.Carts[1].Loaded || game.Shift.Carts[1].Arrival) throw new Exception("Departure baggage load failed");
        p2.Position = Level1Map.Dock(stand) + new Vector3(0.4f, 0, 0.4f); cart.Position = p2.Position;

        // P2 按住交付（进度条推进但不完成），旅客沿人行道走向机位；
        // 按住会随放置即清零，分段注入保持进度条穿过三张截图。
        for (int i = 0; i < 16; i++) { game.SetInput(1, new CrewInput { Held = true }); game.Step(Dt); }
        for (int i = 0; i < 44; i++) game.Step(Dt);
        for (int i = 0; i < 16; i++) { game.SetInput(1, new CrewInput { Held = true }); game.Step(Dt); }
        yield return null;
        Shot(Path.Combine(EvidenceRoot, "m1-ingame-16x9.png"), 1920, 1080);
        Shot(Path.Combine(EvidenceRoot, "m1-ingame-19_5x9.png"), 2340, 1080);

        // 旅客继续走向机位，P2 交付接近完成的班岗全景。
        for (int i = 0; i < 16; i++) { game.SetInput(1, new CrewInput { Held = true }); game.Step(Dt); }
        Shot(Path.Combine(EvidenceRoot, "m1-boarding-16x9.png"), 1920, 1080);

        // 打磨焦点三件套特写：机位 + 餐食站方向 + 餐车。
        Camera cam = game.View;
        Vector3 dock = Level1Map.Dock(stand);
        cam.transform.position = dock + new Vector3(5.5f, 6.5f, -6.5f);
        cam.transform.LookAt(dock + new Vector3(-0.5f, 0.6f, 0));
        Shot(Path.Combine(EvidenceRoot, "m1-sample-trio.png"), 1920, 1080, false);
    }
}
