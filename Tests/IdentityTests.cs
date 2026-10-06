using System;
using System.Collections.Generic;
using IslandAirport;

/// <summary>
/// 身份与埋点 Core（Auth 命名空间 + GameEvents）的零依赖 console 覆盖。
/// JSONL 落盘 IO 用注入委托，测试给内存实现。模式同 SimulationTests。
/// </summary>
public static class IdentityTests
{
    private static int _passed;
    private static int _failed;
    private static int _assertions;

    public static void Main()
    {
        Run("名字校验 trim 后 1-12", TestNameValidation);
        Run("profile 持久化 roundtrip", TestProfileRoundtrip);
        Run("事件去重与 JSONL 落盘往返", TestEventDedupeAndJsonlRoundtrip);
        Run("年龄结果按 UID 与认证类型绑定", TestAgeVerificationBinding);
        Run("无效 DOB 不写入年龄状态", TestInvalidBirthDates);
        Run("13 岁生日当天准入", TestAgeBoundary);
        Run("用户切换、refresh 与注销年龄状态", TestAgeIdentityLifecycle);
        Run("FakeAuth 登录/续期/登出流程", TestFakeAuthFlow);
        Run("FakeAuth 故障注入：取消/网络/未配置", TestFakeAuthFailures);

        Console.WriteLine("Identity tests: {0} passed, {1} failed, {2} assertions.",
            _passed, _failed, _assertions);

        if (_failed != 0)
        {
            Environment.Exit(1);
        }
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            _passed++;
            Console.WriteLine("PASS  " + name);
        }
        catch (Exception exception)
        {
            _failed++;
            Console.WriteLine("FAIL  " + name + ": " + exception.Message);
            Console.WriteLine(exception.ToString());
        }
    }

    private static void Assert(bool condition, string message)
    {
        _assertions++;
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        _assertions++;
        if (!object.Equals(expected, actual))
        {
            throw new Exception(message + " expected=" + expected + " actual=" + actual);
        }
    }

    private static void AssertNear(double expected, double actual, double tolerance, string message)
    {
        _assertions++;
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw new Exception(message + " expected=" + expected + " actual=" + actual);
        }
    }

    // ---- 用例 ----

    private static void TestNameValidation()
    {
        Assert(PlayerProfile.IsValidName("小岛"), "中文名合法");
        Assert(PlayerProfile.IsValidName("  Guest_028  "), "带空白 trim 后合法");
        AssertEqual("Guest_028", PlayerProfile.NormalizeName("  Guest_028  "), "trim 规整");
        Assert(PlayerProfile.IsValidName("一二三四五六七八九十甲乙"), "12 字合法");
        Assert(!PlayerProfile.IsValidName("一二三四五六七八九十甲乙丙"), "13 字非法");
        Assert(!PlayerProfile.IsValidName(""), "空名非法");
        Assert(!PlayerProfile.IsValidName("   "), "全空白非法");
        Assert(!PlayerProfile.IsValidName(null), "null 非法");
        AssertEqual("", PlayerProfile.NormalizeName(null), "null 规整为空串");
        AssertEqual(12, PlayerProfile.MaxNameLength, "上限 12");
    }

    private static void TestProfileRoundtrip()
    {
        // 内存字典模拟持久化介质。
        Dictionary<string, string> store = new Dictionary<string, string>();
        PlayerProfile profile = new PlayerProfile();
        profile.Name = "岛民甲";
        profile.ColorIndex = 3;
        profile.GuestId = "Guest_028";
        store["palmbay.profile"] = profile.ToJson();

        PlayerProfile loaded = PlayerProfile.FromJson(store["palmbay.profile"]);
        AssertEqual("岛民甲", loaded.Name, "名字往返");
        AssertEqual(3, loaded.ColorIndex, "颜色往返");
        AssertEqual("Guest_028", loaded.GuestId, "游客 ID 往返");

        AssertEqual(null, PlayerProfile.FromJson("{broken"), "损坏数据返回 null");
        AssertEqual(null, PlayerProfile.FromJson(null), "null 输入返回 null");

        // 缺键给默认值。
        PlayerProfile partial = PlayerProfile.FromJson("{\"name\":\"乙\"}");
        AssertEqual("乙", partial.Name, "部分字段名字");
        AssertEqual(0, partial.ColorIndex, "缺省颜色 0");
        AssertEqual("", partial.GuestId, "缺省游客 ID 空串");
    }

    private static void TestEventDedupeAndJsonlRoundtrip()
    {
        List<string> disk = new List<string>();
        GameEventQueue queue = new GameEventQueue(disk.Add, () => disk);
        queue.MatchId = "m-001";
        queue.ActorId = "uid-1";
        queue.PairId = "p-9";

        queue.Tick(1.5f);
        Assert(queue.Enqueue(GameEventQueue.SandboxStarted, "sandbox:m-001", null), "首条入队");
        Assert(!queue.Enqueue(GameEventQueue.SandboxStarted, "sandbox:m-001", null), "同 dedupeKey 去重");
        AssertEqual(1, queue.DroppedCount, "去重计数");
        Assert(queue.Enqueue(GameEventQueue.SandboxStarted, "sandbox:m-002", null), "换 key 可入队");

        Dictionary<string, object> parameters = new Dictionary<string, object>();
        parameters["role"] = "餐食车";
        parameters["seat"] = 1.0;
        Assert(queue.Enqueue(GameEventQueue.InviteCreated, null, parameters), "无 dedupeKey 不去重");
        Assert(queue.Enqueue(GameEventQueue.InviteCreated, null, parameters), "无 key 重复也入队");
        AssertEqual(4, queue.PendingCount, "待落盘 4 条");

        AssertEqual(4, queue.Flush(), "落盘 4 行");
        AssertEqual(0, queue.PendingCount, "落盘后清空");
        AssertEqual(4, disk.Count, "JSONL 4 行");

        List<GameEventRecord> loaded = queue.LoadAll();
        AssertEqual(4, loaded.Count, "读回 4 条");
        AssertEqual(GameEventQueue.SandboxStarted, loaded[0].Name, "首条事件名");
        AssertNear(1.5, loaded[0].Ts, 0.0001, "ts 往返");
        AssertEqual("m-001", loaded[0].MatchId, "matchId 往返");
        AssertEqual("uid-1", loaded[0].ActorId, "actorId 往返");
        AssertEqual("p-9", loaded[0].PairId, "pairId 往返");
        AssertEqual("sandbox:m-001", loaded[0].DedupeKey, "dedupeKey 往返");
        AssertEqual("餐食车", MiniJson.GetString(loaded[2].Params, "role", null), "中文参数往返");
        AssertNear(1.0, MiniJson.GetNumber(loaded[2].Params, "seat", 0), 0.0001, "数字参数往返");

        // 坏行被跳过。
        disk.Add("{oops");
        AssertEqual(4, queue.LoadAll().Count, "坏行跳过不炸");

        // 事件常量清单（E25）。
        AssertEqual("invite_created", GameEventQueue.InviteCreated, "常量 invite_created");
        AssertEqual("share_panel_opened", GameEventQueue.SharePanelOpened, "常量 share_panel_opened");
        AssertEqual("login_started", GameEventQueue.LoginStarted, "常量 login_started");
        AssertEqual("login_ok", GameEventQueue.LoginOk, "常量 login_ok");
        AssertEqual("login_fail", GameEventQueue.LoginFail, "常量 login_fail");
        AssertEqual("age_blocked", GameEventQueue.AgeBlocked, "常量 age_blocked");
        AssertEqual("room_created", GameEventQueue.RoomCreated, "常量 room_created");
        AssertEqual("room_joined", GameEventQueue.RoomJoined, "常量 room_joined");
        AssertEqual("room_rejected", GameEventQueue.RoomRejected, "常量 room_rejected");
        AssertEqual("room_started", GameEventQueue.RoomStarted, "常量 room_started");
        AssertEqual("room_left", GameEventQueue.RoomLeft, "常量 room_left");
        AssertEqual("sandbox_started", GameEventQueue.SandboxStarted, "常量 sandbox_started");
        AssertEqual("shift_started", GameEventQueue.ShiftStarted, "常量 shift_started");
        AssertEqual("first_shift_completed", GameEventQueue.FirstShiftCompleted, "常量 first_shift_completed");
        AssertEqual("retry_clicked", GameEventQueue.RetryClicked, "常量 retry_clicked");
        AssertEqual("solo_invite_intent", GameEventQueue.SoloInviteIntent, "常量 solo_invite_intent");
    }

    private static void TestFakeAuthFlow()
    {
        FakeAuthService auth = new FakeAuthService();
        AssertEqual(null, auth.User, "初始未登录");
        Assert(!auth.Busy, "初始不忙");

        string result;
        auth.SignInGoogle();
        Assert(auth.Busy, "登录中忙");
        Assert(!auth.TryDequeue(out result), "未完成无事件");

        // Pump 驱动到 Delay 出结果。
        for (int i = 0; i < 10 && auth.Busy; i++)
        {
            auth.Pump(0.05f);
        }
        Assert(auth.TryDequeue(out result), "登录完成出事件");
        AssertEqual("ok:fake-uid-1", result, "确定性 fake 凭据");
        AssertEqual("fake-uid-1", auth.User.Uid, "用户 uid");
        AssertEqual("FakeUser", auth.User.DisplayName, "显示名");
        AssertEqual("fake-refresh-1", auth.User.RefreshToken, "refresh token");

        // 静默续期：只认自己签发的 token。
        auth.Restore("fake-refresh-1");
        Assert(auth.TryDequeue(out result), "续期出事件");
        AssertEqual("restored:fake-uid-1", result, "续期成功");
        auth.Restore("bogus-token");
        Assert(auth.TryDequeue(out result), "坏 token 出事件");
        AssertEqual("fail:token", result, "坏 token 失败");

        // 登出。
        auth.SignOut();
        AssertEqual(null, auth.User, "登出清用户");
        Assert(auth.TryDequeue(out result), "登出出事件");
        AssertEqual("out", result, "登出事件名");
    }

    private static void TestAgeVerificationBinding()
    {
        AgeVerificationStore store = new AgeVerificationStore();
        DateTime today = new DateTime(2030, 5, 4);
        bool allowed;
        string error;

        Assert(store.TryRecordBirthDate("uid-real-1", false, 2010, 5, 4, today, out allowed, out error),
            "合法 DOB 被处理");
        Assert(allowed, "符合年龄要求");
        Assert(store.IsVerified("uid-real-1", false), "真实 UID 状态保存");
        Assert(!store.IsVerified("uid-real-1", true), "Fake UID 空间与真实 UID 分开");

        store.SetStatus("uid-shared", false, true);
        store.SetStatus("uid-shared", true, false);
        Assert(store.IsVerified("uid-shared", false), "同名真实 UID 保持通过状态");
        Assert(!store.IsVerified("uid-shared", true), "同名 Fake UID 保持阻止状态");

        string json = store.ToJson();
        Assert(!json.Contains("2010"), "持久化不包含原始 DOB");
        AgeVerificationStore loaded = AgeVerificationStore.FromJson(json);
        Assert(loaded.IsVerified("uid-real-1", false), "年龄状态 JSON 往返");
        Assert(!loaded.IsVerified("uid-shared", true), "Fake 年龄状态 JSON 往返");
        Assert(!AgeVerificationStore.FromJson("{broken").IsVerified("uid-real-1", false), "损坏状态安全回退");
    }

    private static void TestInvalidBirthDates()
    {
        AgeVerificationStore store = new AgeVerificationStore();
        DateTime today = new DateTime(2030, 2, 28);
        bool allowed;
        string error;

        Assert(!store.TryRecordBirthDate("uid-1", false, 2030, 2, 29, today, out allowed, out error),
            "不存在日期被拒绝");
        Assert(!string.IsNullOrEmpty(error), "不存在日期提供提示");
        Assert(!store.TryGetStatus("uid-1", false, out allowed), "不存在日期不写状态");

        Assert(!store.TryRecordBirthDate("uid-1", false, 2030, 3, 1, today, out allowed, out error),
            "未来日期被拒绝");
        AssertEqual("出生日期不能晚于今天。", error, "未来日期提示");
        Assert(!store.TryGetStatus("uid-1", false, out allowed), "未来日期不写状态");
        Assert(!store.TryRecordBirthDate("", false, 2010, 1, 1, today, out allowed, out error), "缺少 UID 被拒绝");
    }

    private static void TestAgeBoundary()
    {
        AgeVerificationStore store = new AgeVerificationStore();
        DateTime today = new DateTime(2030, 5, 4);
        bool allowed;
        string error;

        Assert(store.TryRecordBirthDate("uid-birthday", false, 2017, 5, 4, today, out allowed, out error),
            "13 岁生日 DOB 有效");
        Assert(allowed, "13 岁生日当天准入");
        Assert(store.TryRecordBirthDate("uid-before", false, 2017, 5, 5, today, out allowed, out error),
            "生日未到 DOB 有效");
        Assert(!allowed, "13 岁生日之前阻止组队");
        Assert(store.TryRecordBirthDate("uid-after", false, 2017, 5, 3, today, out allowed, out error),
            "13 岁生日之后 DOB 有效");
        Assert(allowed, "13 岁生日之后准入");
    }

    private static void TestAgeIdentityLifecycle()
    {
        AgeVerificationStore store = new AgeVerificationStore();
        store.SetStatus("uid-a", false, true);
        AuthUser firstUser = new AuthUser();
        firstUser.Uid = "uid-a";
        Assert(store.IsVerifiedFor(firstUser, false), "通过记录绑定当前 UID");

        AuthUser secondUser = new AuthUser();
        secondUser.Uid = "uid-b";
        Assert(!store.IsVerifiedFor(secondUser, false), "切换 UID 不继承年龄状态");

        AgeVerificationStore refreshed = AgeVerificationStore.FromJson(store.ToJson());
        AuthUser refreshedUser = new AuthUser();
        refreshedUser.Uid = "uid-a";
        Assert(refreshed.IsVerifiedFor(refreshedUser, false), "同 UID token refresh 后年龄状态保留");
        Assert(!refreshed.IsVerifiedFor(null, false), "注销后无当前用户不能组队");
        refreshed.ClearCurrentUser(refreshedUser);
        Assert(!refreshedUser.AgeVerified, "注销清除当前用户年龄准入态");
        Assert(refreshed.IsVerified("uid-a", false), "注销不丢失该 UID 的已存年龄结果");
        Assert(refreshed.Remove("uid-a", false), "显式移除指定 UID 状态");
        Assert(!refreshed.IsVerified("uid-a", false), "移除后不残留通过状态");
    }

    private static void TestFakeAuthFailures()
    {
        string result;
        FakeAuthService cancel = new FakeAuthService();
        cancel.Mode = FakeAuthService.FakeMode.Cancel;
        cancel.SignInGoogle();
        for (int i = 0; i < 10 && cancel.Busy; i++)
        {
            cancel.Pump(0.05f);
        }
        Assert(cancel.TryDequeue(out result), "取消出事件");
        AssertEqual("cancelled", result, "用户取消");
        AssertEqual(null, cancel.User, "取消后仍无用户");

        FakeAuthService network = new FakeAuthService();
        network.Mode = FakeAuthService.FakeMode.NetworkError;
        network.SignInGoogle();
        for (int i = 0; i < 10 && network.Busy; i++)
        {
            network.Pump(0.05f);
        }
        Assert(network.TryDequeue(out result), "网络错误出事件");
        AssertEqual("fail:network", result, "网络错误");

        FakeAuthService unconfigured = new FakeAuthService();
        unconfigured.Mode = FakeAuthService.FakeMode.Unconfigured;
        unconfigured.SignInGoogle();
        Assert(!unconfigured.Busy, "未配置不进入忙态");
        Assert(unconfigured.TryDequeue(out result), "未配置立即出事件");
        AssertEqual("fail:unconfigured", result, "未配置");
    }
}
