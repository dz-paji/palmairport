using System;
using System.Collections.Generic;
using IslandAirport;

/// <summary>
/// 联机 Core（Net 命名空间）的零依赖 console 覆盖：LoopbackHub 内存传输 +
/// 故障注入（Loss/Duplicate/Reorder/Cut）跑真实控制面。模式同 SimulationTests。
/// </summary>
public static class NetTests
{
    private const string LoopAddr = "127.0.0.1";
    private const int ClientPortA = 50001;
    private const int ClientPortB = 50002;
    private const int ClientPortC = 50003;

    private static int _passed;
    private static int _failed;
    private static int _assertions;

    public static void Main()
    {
        Run("MiniJson 中文嵌套往返", TestMiniJsonChineseNested);
        Run("UdpSession 定向收发与端点过滤", TestUdpSessionEndpoints);
        Run("ReliableControl 重试/ack/超时/连发", TestReliableControl);
        Run("beacon 开播/入表/过期", TestBeaconAnnounceListenExpire);
        Run("握手→accept→roster", TestHandshakeAcceptRoster);
        Run("三拒绝：full / ingame / auth", TestThreeRejects);
        Run("in 消息按 seq 去旧", TestInputSeqDropsOld);
        Run("慢 host Pump 合并 Pressed/Cycle 边沿", TestInputEdgesMergeAcrossPump);
        Run("dup accept / dup start 幂等", TestDupAcceptStartIdempotent);
        Run("Loss=0.5 下 join 经重试仍成功", TestJoinRetriesUnderLoss);
        Run("快照编解码往返（含中文标签）", TestSnapshotCodec);
        Run("Ver=3 协议号与旧版拒绝", TestVersionTwoGating);
        Run("Ver=2 Shift/Flights 镜像块往返", TestSnapshotShiftBlockRoundtrip);
        Run("插值缓冲中点采样", TestSnapshotBufferMidpoint);
        Run("Loss=0.5+Reorder 下采样不倒退", TestSnapshotBufferNeverBackward);
        Run("Cut 后 5s：host 席 Bot / client hostlost", TestCutHeartbeatTimeout);
        Run("M4 finite metadata + recovery/replay codec", TestM4SnapshotCodec);
        Run("M4 reliable shift start/retry and late-round input/snapshot fence", TestM4RetryAndFences);
        Run("M4 voluntary host Leave preserves checkpoint and stable seat", TestM4PlannedMigration);
        Run("M4 abrupt host loss promotes latest consumed checkpoint", TestM4AbruptMigration);
        Run("M4 client Leave/timeout preserves host and credit exclusion", TestM4ClientLoss);
        Run("M4 explicit CloseRoom stays terminal", TestM4CloseTerminal);
        Run("M4 replay repair transport/ready dedup and old-round fence", TestM4ReplayRepair);
        Run("M4 buffer round/authority resets reject late old packets", TestM4BufferFence);
        Run("M4 replay-ready selective loss beyond30s heals and stops on ack", TestM4ReplayReadyLongLoss);
        Run("AgeCheck 生日当天与闰年边界", TestAgeCheckBoundaries);

        Console.WriteLine("Net tests: {0} passed, {1} failed, {2} assertions.",
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

    // ---- 测试工具 ----

    private static RoomManager CreateHost(LoopbackHub hub)
    {
        RoomManager host = new RoomManager();
        host.SocketFactory = port => hub.CreateSocket(LoopAddr, port);
        return host;
    }

    private static RoomManager CreateClient(LoopbackHub hub, int clientPort)
    {
        RoomManager client = new RoomManager();
        client.SocketFactory = port => hub.CreateSocket(LoopAddr, port == 0 ? clientPort : port);
        return client;
    }

    private static void Step(LoopbackHub hub, float dt, params RoomManager[] rooms)
    {
        hub.Pump(dt);
        for (int i = 0; i < rooms.Length; i++)
        {
            rooms[i].Pump(dt);
        }
    }

    private static void Advance(LoopbackHub hub, float seconds, params RoomManager[] rooms)
    {
        float elapsed = 0f;
        while (elapsed < seconds)
        {
            Step(hub, 0.05f, rooms);
            elapsed += 0.05f;
        }
    }

    private static List<string> DrainEvents(RoomManager room)
    {
        List<string> events = new List<string>();
        string evt;
        while (room.TryDequeue(out evt))
        {
            events.Add(evt);
        }

        return events;
    }

    private static bool Contains(List<string> events, string prefix)
    {
        for (int i = 0; i < events.Count; i++)
        {
            if (events[i].StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>跑通 join 握手直到 client InRoom（带步数上限防死循环）。</summary>
    private static void JoinUntilInRoom(LoopbackHub hub, RoomManager host, RoomManager client)
    {
        for (int i = 0; i < 200 && client.Phase != RoomPhase.InRoom; i++)
        {
            Step(hub, 0.05f, host, client);
        }

        AssertEqual(RoomPhase.InRoom, client.Phase, "client should reach InRoom");
    }

    // ---- 用例 ----

    private static void TestMiniJsonChineseNested()
    {
        Dictionary<string, object> inner = new Dictionary<string, object>();
        inner["名字"] = "小岛机场";
        inner["标签"] = new List<object> { "餐食", "燃油", 3.0, true, null };
        Dictionary<string, object> root = new Dictionary<string, object>();
        root["房间"] = inner;
        root["数"] = 42.5;
        root["空"] = new Dictionary<string, object>();

        string json = MiniJson.Serialize(root);
        Assert(json.IndexOf("小岛机场") >= 0, "中文应直出不转义: " + json);

        Dictionary<string, object> parsed = MiniJson.ParseObject(json);
        Dictionary<string, object> parsedInner = MiniJson.GetObject(parsed, "房间");
        AssertEqual("小岛机场", MiniJson.GetString(parsedInner, "名字", null), "中文字符串往返");
        List<object> tags = MiniJson.GetArray(parsedInner, "标签");
        AssertEqual(5, tags.Count, "嵌套数组长度");
        AssertEqual("餐食", tags[0] as string, "数组中文元素");
        AssertEqual(3.0, (double)tags[2], "数组数字元素");
        AssertEqual(true, (bool)tags[3], "数组 bool 元素");
        AssertEqual(null, tags[4], "数组 null 元素");
        AssertNear(42.5, MiniJson.GetNumber(parsed, "数", 0), 0.0001, "数字往返");

        // 转义与控制字符。
        string escaped = MiniJson.Serialize("引\"号\\换\n行\t");
        AssertEqual("引\"号\\换\n行\t", MiniJson.Parse(escaped) as string, "转义字符往返");

        // 非法输入抛 FormatException。
        bool threw = false;
        try
        {
            MiniJson.Parse("{broken");
        }
        catch (FormatException)
        {
            threw = true;
        }
        Assert(threw, "非法 JSON 应抛 FormatException");
    }

    private static void TestUdpSessionEndpoints()
    {
        LoopbackHub hub = new LoopbackHub(1);
        LoopbackSocket socketA = hub.CreateSocket(LoopAddr, 60001);
        LoopbackSocket socketB = hub.CreateSocket(LoopAddr, 60002);
        LoopbackSocket socketC = hub.CreateSocket(LoopAddr, 60003);
        UdpSession a = new UdpSession(socketA, LoopAddr, 60002);
        UdpSession b = new UdpSession(socketB, LoopAddr, 60001);

        a.SendTo(NetProtocol.BuildPing(1f));
        // 第三方包应被会话过滤。
        socketC.Send(LoopAddr, 60002, NetProtocol.BuildPing(9f));
        hub.Pump(0.05f);

        NetMessage message;
        Assert(b.TryReceive(out message), "b 应收到的 ping");
        AssertEqual("ping", message.Type, "消息类型");
        AssertNear(1.0, message.GetFloat("clk", -1f), 0.0001, "只收固定端点的包");
        Assert(!b.TryReceive(out message), "第三方的包应被过滤丢弃");

        a.Close();
        a.SendTo(NetProtocol.BuildPing(2f));
        hub.Pump(0.05f);
        Assert(!b.TryReceive(out message), "关闭后发送应无效");
        Assert(!a.Alive, "关闭后 Alive=false");
        a.Close(); // 幂等
    }

    private static void TestReliableControl()
    {
        List<string> sent = new List<string>();
        ReliableControl control = new ReliableControl(sent.Add);

        control.Track("join:1", "payload-join", 1f, 5f);
        AssertEqual(1, sent.Count, "登记即首发");
        control.Pump(0.5f);
        AssertEqual(1, sent.Count, "未到重试间隔不重发");
        control.Pump(0.6f);
        AssertEqual(2, sent.Count, "到 1s 重试");
        AssertEqual(1, control.PendingCount, "ack 前保持待发");

        Assert(control.Ack("join:1"), "ack 命中");
        AssertEqual(0, control.PendingCount, "ack 后清空");
        control.Pump(3f);
        AssertEqual(2, sent.Count, "ack 后不再重发");

        // 超时报废。
        control.Track("join:2", "payload-2", 1f, 2.5f);
        control.Pump(3f);
        AssertEqual(0, control.PendingCount, "超时后报废");
        Assert(sent.Count <= 6, "超时前重发次数有限: " + sent.Count);

        // 连发：300ms 内 3 次。
        int before = sent.Count;
        control.TrackBurst("bye", 3, 0.15f);
        control.Pump(0.15f);
        control.Pump(0.15f);
        control.Pump(0.15f);
        AssertEqual(before + 3, sent.Count, "连发恰好 3 次");
        AssertEqual(0, control.PendingCount, "连发完毕清空");

        // 接收幂等。
        Assert(control.FirstSeen("accept:7"), "首见返回 true");
        Assert(!control.FirstSeen("accept:7"), "重复返回 false");
        control.ResetSeen();
        Assert(control.FirstSeen("accept:7"), "ResetSeen 后重新首见");
    }

    private static void TestBeaconAnnounceListenExpire()
    {
        LoopbackHub hub = new LoopbackHub(2);
        LoopbackSocket announceSocket = hub.CreateSocket(LoopAddr, 61000);
        LoopbackSocket listenSocket = hub.CreateSocket(LoopAddr, NetProtocol.BeaconPort);
        BeaconAnnouncer announcer = new BeaconAnnouncer(announceSocket);
        announcer.TargetAddress = LoopAddr; // 测试注入回环地址
        BeaconListener listener = new BeaconListener(listenSocket);

        RoomInfo room = new RoomInfo();
        room.Name = "小岛的房间";
        room.HostName = "Host";
        room.State = "lobby";
        room.Port = NetProtocol.SessionPort;
        room.Taken = 1;
        announcer.Start(room);

        announcer.Pump(0.05f);
        hub.Pump(0.05f);
        listener.Pump(0.05f);
        AssertEqual(1, announcer.SentCount, "首 Pump 立即开播");
        AssertEqual(1, listener.Count, "房间入表");
        RoomInfo found = listener.Rooms()[0];
        AssertEqual("小岛的房间", found.Name, "房名往返");
        AssertEqual("Host", found.HostName, "host 名往返");
        AssertEqual("lobby", found.State, "状态往返");
        AssertEqual(LoopAddr, found.Address, "来源地址入表");

        // 停播后 5s 过期剔除。
        announcer.Stop();
        for (int i = 0; i < 100; i++)
        {
            hub.Pump(0.05f);
            listener.Pump(0.05f);
        }
        AssertEqual(0, listener.Count, "5s 未见应剔除");
    }

    private static void TestHandshakeAcceptRoster()
    {
        LoopbackHub hub = new LoopbackHub(3);
        RoomManager host = CreateHost(hub);
        RoomManager client = CreateClient(hub, ClientPortA);

        Assert(host.Host("小岛房", "Host", "host-token"), "host 开房");
        AssertEqual(RoomPhase.Listening, host.Phase, "host 进入 Listening");
        Assert(client.Join(LoopAddr, "Client", "client-token"), "client 发起 join");
        AssertEqual(RoomPhase.Joining, client.Phase, "client 进入 Joining");

        JoinUntilInRoom(hub, host, client);
        AssertEqual(1, client.LocalSeat, "client 分到 1 席");
        Assert(host.Seats[1].Occupied, "host 1 席被占");
        AssertEqual("Client", host.Seats[1].Name, "host 看到 client 名");

        List<string> hostEvents = DrainEvents(host);
        Assert(Contains(hostEvents, "joined:1:Client"), "host 收到 joined 事件");

        // roster 1Hz：再推进 1.2s 应到达，client 看到双席。
        Advance(hub, 1.2f, host, client);
        List<string> clientEvents = DrainEvents(client);
        Assert(Contains(clientEvents, "roster"), "client 收到 roster");
        AssertEqual("Host", client.Seats[0].Name, "roster 带来 host 名");
        AssertEqual("Client", client.Seats[1].Name, "roster 带来 client 名");
        Assert(client.Seats[0].Occupied && client.Seats[1].Occupied, "roster 双席占用");
    }

    private static void TestThreeRejects()
    {
        // full：第三人被拒。
        LoopbackHub hub = new LoopbackHub(4);
        RoomManager host = CreateHost(hub);
        RoomManager clientA = CreateClient(hub, ClientPortA);
        RoomManager clientB = CreateClient(hub, ClientPortB);
        host.Host("房", "Host", "t");
        clientA.Join(LoopAddr, "A", "ta");
        JoinUntilInRoom(hub, host, clientA);
        DrainEvents(host);

        clientB.Join(LoopAddr, "B", "tb");
        for (int i = 0; i < 100 && clientB.Phase != RoomPhase.Idle; i++)
        {
            Step(hub, 0.05f, host, clientB);
        }
        Assert(Contains(DrainEvents(clientB), "reject:full"), "满员应 reject:full");
        AssertEqual(RoomPhase.Idle, clientB.Phase, "被拒后回 Idle");

        // ingame：开局后加入被拒。
        Assert(host.StartSandbox(), "host 开局");
        clientB.Join(LoopAddr, "B2", "tb2");
        for (int i = 0; i < 100 && clientB.Phase != RoomPhase.Idle; i++)
        {
            Step(hub, 0.05f, host, clientB);
        }
        Assert(Contains(DrainEvents(clientB), "reject:ingame"), "已开局应 reject:ingame");

        // auth：token 为空被拒。
        LoopbackHub hub2 = new LoopbackHub(5);
        RoomManager host2 = CreateHost(hub2);
        RoomManager clientC = CreateClient(hub2, ClientPortC);
        host2.Host("房2", "Host2", "t");
        clientC.Join(LoopAddr, "C", "");
        for (int i = 0; i < 100 && clientC.Phase != RoomPhase.Idle; i++)
        {
            Step(hub2, 0.05f, host2, clientC);
        }
        Assert(Contains(DrainEvents(clientC), "reject:auth"), "空 token 应 reject:auth");

        // join 5s 无应答→reject:timeout。
        LoopbackHub hub3 = new LoopbackHub(6);
        RoomManager lonely = CreateClient(hub3, ClientPortA);
        lonely.Join(LoopAddr, "L", "t"); // 没有 host
        Advance(hub3, 5.2f, lonely);
        Assert(Contains(DrainEvents(lonely), "reject:timeout"), "5s 无应答应 reject:timeout");
        AssertEqual(RoomPhase.Idle, lonely.Phase, "超时后回 Idle");
    }

    private static void TestInputSeqDropsOld()
    {
        LoopbackHub hub = new LoopbackHub(7);
        RoomManager host = CreateHost(hub);
        RoomManager client = CreateClient(hub, ClientPortA);
        host.Host("房", "Host", "t");
        client.Join(LoopAddr, "C", "t");
        JoinUntilInRoom(hub, host, client);
        host.StartSandbox();
        DrainEvents(host);

        for (int seq = 1; seq <= 3; seq++)
        {
            InputFrame frame = new InputFrame();
            frame.Seq = seq;
            frame.Mx = seq * 0.5f;
            client.SendInput(frame);
            Step(hub, 0.05f, host, client);
        }
        AssertEqual(3, host.LatestInput(1).Seq, "最新输入 seq=3");
        AssertNear(1.5, host.LatestInput(1).Mx, 0.0001, "最新输入内容");

        // 旧 seq 与重复 seq 即弃。
        InputFrame old = new InputFrame();
        old.Seq = 2;
        old.Mx = 99f;
        client.SendInput(old);
        Step(hub, 0.05f, host, client);
        client.SendInput(old);
        Step(hub, 0.05f, host, client);
        AssertEqual(3, host.LatestInput(1).Seq, "旧包重包不改变最新输入");
        AssertNear(1.5, host.LatestInput(1).Mx, 0.0001, "旧包内容不覆盖");
        Assert(host.HasRemoteInput(1), "host 已收到远端输入");
        Assert(host.IsRemoteSeat(1), "1 席是远端真人");
    }

    private static void TestInputEdgesMergeAcrossPump()
    {
        LoopbackHub hub = new LoopbackHub(13);
        RoomManager host = CreateHost(hub);
        RoomManager client = CreateClient(hub, ClientPortA);
        host.Host("房", "Host", "t");
        client.Join(LoopAddr, "C", "t");
        JoinUntilInRoom(hub, host, client);
        host.StartSandbox();
        Advance(hub, 0.1f, host, client);
        Assert(client.Phase == RoomPhase.Starting || client.Phase == RoomPhase.Playing,
            "client 已进入可发送输入的对局阶段");

        InputFrame first = new InputFrame();
        first.Seq = 1;
        first.Mx = 0.25f;
        first.Pressed = true;
        client.SendInput(first);
        hub.Pump(0.01f);

        InputFrame second = new InputFrame();
        second.Seq = 2;
        second.Mx = 0.5f;
        second.Cycle = true;
        client.SendInput(second);
        hub.Pump(0.01f);

        InputFrame third = new InputFrame();
        third.Seq = 3;
        third.Mx = 0.75f;
        third.Held = true;
        client.SendInput(third);
        hub.Pump(0.01f);
        host.Pump(0.1f);
        client.Pump(0.1f);

        AssertEqual(3, host.LatestInput(1).Seq, "兼容读取仍指向最新 seq");
        Assert(!host.LatestInput(1).Pressed && !host.LatestInput(1).Cycle,
            "最新帧仍保留该帧自己的边沿语义");
        InputFrame consumed = host.ConsumeLatestInput(1);
        AssertEqual(3, consumed.Seq, "合并后保留最新 seq");
        AssertNear(0.75, consumed.Mx, 0.0001, "合并后保留最新移动值");
        Assert(consumed.Held, "合并后保留最新 held 值");
        Assert(consumed.Pressed, "合并 Pump 间隔内的 Pressed 边沿");
        Assert(consumed.Cycle, "合并 Pump 间隔内的 Cycle 边沿");

        InputFrame consumedAgain = host.ConsumeLatestInput(1);
        Assert(!consumedAgain.Pressed && !consumedAgain.Cycle, "消费后边沿只发出一次");

        InputFrame duplicate = new InputFrame();
        duplicate.Seq = 3;
        duplicate.Mx = 99f;
        duplicate.Pressed = true;
        duplicate.Cycle = true;
        client.SendInput(duplicate);
        Step(hub, 0.05f, host, client);
        InputFrame afterDuplicate = host.ConsumeLatestInput(1);
        AssertEqual(3, afterDuplicate.Seq, "重复 seq 不覆盖最新帧");
        AssertNear(0.75, afterDuplicate.Mx, 0.0001, "重复 seq 不覆盖最新移动值");
        Assert(!afterDuplicate.Pressed && !afterDuplicate.Cycle, "重复 seq 不重触发边沿");

        InputFrame pending = new InputFrame();
        pending.Seq = 4;
        pending.Pressed = true;
        pending.Cycle = true;
        client.SendInput(pending);
        Step(hub, 0.05f, host, client);
        client.Leave();
        Step(hub, 0.05f, host, client);
        Assert(host.Seats[1].Bot, "leave 后 host 将席位交给 bot");
        Assert(!host.HasRemoteInput(1), "掉线清除该席输入状态");
        InputFrame afterDrop = host.ConsumeLatestInput(1);
        Assert(!afterDrop.Pressed && !afterDrop.Cycle, "掉线清除未消费边沿");
        AssertNear(0, afterDrop.Mx, 0.0001, "掉线清除旧移动值");
    }

    private static void TestDupAcceptStartIdempotent()
    {
        LoopbackHub hub = new LoopbackHub(8);
        // 测试持有两端套接引用，便于注入 dup 包。
        LoopbackSocket hostSocket = hub.CreateSocket(LoopAddr, NetProtocol.SessionPort);
        LoopbackSocket clientSocket = hub.CreateSocket(LoopAddr, ClientPortA);
        RoomManager host = new RoomManager();
        host.SocketFactory = port => hostSocket;
        RoomManager client = new RoomManager();
        client.SocketFactory = port => clientSocket;

        host.Host("房", "Host", "t");
        client.Join(LoopAddr, "C", "t");
        JoinUntilInRoom(hub, host, client);
        DrainEvents(host);
        DrainEvents(client);

        // dup join（同对端同 seq 重发）→ host 幂等重答同席，不重复 joined 事件。
        clientSocket.Send(LoopAddr, NetProtocol.SessionPort, NetProtocol.BuildJoin(1, "C", "t"));
        Advance(hub, 0.3f, host, client);
        AssertEqual(0, DrainEvents(host).Count, "dup join 不产生重复 joined 事件");
        AssertEqual("C", host.Seats[1].Name, "dup join 席位不变");

        // dup accept → client 已 InRoom，忽略，不产生新的 joined 事件（roster 周期事件属正常）。
        hostSocket.Send(LoopAddr, ClientPortA, NetProtocol.BuildAccept(1, 1));
        Advance(hub, 0.3f, host, client);
        Assert(!Contains(DrainEvents(client), "joined"), "dup accept 幂等无重复 joined 事件");
        AssertEqual(1, client.LocalSeat, "dup accept 席位不变");

        // start：事件只来一次；注入 dup start 幂等 no-op。
        host.StartSandbox();
        for (int i = 0; i < 200 && client.Phase == RoomPhase.InRoom; i++)
        {
            Step(hub, 0.05f, host, client);
        }
        List<string> startEvents = DrainEvents(client);
        Assert(Contains(startEvents, "start"), "client 收到 start");
        hostSocket.Send(LoopAddr, ClientPortA, NetProtocol.BuildStart(99));
        Advance(hub, 0.3f, host, client);
        Assert(!Contains(DrainEvents(client), "start"), "dup start 不再产生事件");

        // client 首帧 in 确认 start（host 停发），host 收到输入。
        InputFrame frame = new InputFrame();
        frame.Seq = 1;
        client.SendInput(frame);
        Advance(hub, 0.2f, host, client);
        AssertEqual(1, host.LatestInput(1).Seq, "host 收到首帧 in");

        // host 快照下行，client 消费。
        Snapshot snap = new Snapshot();
        snap.Tick = 1;
        snap.Labels[0] = "抢车";
        host.BroadcastSnapshot(snap);
        Advance(hub, 0.2f, host, client);
        Snapshot taken;
        Assert(client.TryTakeSnapshot(out taken), "client 取到快照");
        AssertEqual("抢车", taken.Labels[0], "快照标签到达");
        AssertEqual(1, host.SnapshotsSent, "SnapshotsSent 计数");
        Assert(!client.TryTakeSnapshot(out taken), "快照消费语义：取走即清");
    }

    /// <summary>统计 join 发送次数的包装套接（验证重试真实发生）。</summary>
    private class CountingSocket : IUdpSocket
    {
        private readonly IUdpSocket _inner;
        public int JoinSends;

        public CountingSocket(IUdpSocket inner)
        {
            _inner = inner;
        }

        public int LocalPort
        {
            get { return _inner.LocalPort; }
        }

        public void Send(string address, int port, string payload)
        {
            if (payload != null && payload.Contains("\"t\":\"join\""))
            {
                JoinSends++;
            }

            _inner.Send(address, port, payload);
        }

        public bool TryReceive(out UdpDatagram datagram)
        {
            return _inner.TryReceive(out datagram);
        }

        public void Close()
        {
            _inner.Close();
        }
    }

    private static void TestJoinRetriesUnderLoss()
    {
        // 种子 9 在 Loss=0.5 下确定性经历 4 次重试后成功。
        LoopbackHub hub = new LoopbackHub(9);
        hub.LossRate = 0.5f;
        RoomManager host = CreateHost(hub);
        CountingSocket counting = null;
        RoomManager client = new RoomManager();
        client.SocketFactory = port =>
        {
            counting = new CountingSocket(hub.CreateSocket(LoopAddr, port == 0 ? ClientPortA : port));
            return counting;
        };
        host.Host("房", "Host", "t");
        client.Join(LoopAddr, "C", "t");

        bool joined = false;
        for (int i = 0; i < 400; i++)
        {
            Step(hub, 0.05f, host, client);
            if (client.Phase == RoomPhase.InRoom)
            {
                joined = true;
                break;
            }
        }

        Assert(joined, "Loss=0.5 下 join 应经重试成功");
        Assert(counting != null && counting.JoinSends >= 2,
            "重试真实发生（join 发送 " + (counting == null ? -1 : counting.JoinSends) + " 次）");
        AssertEqual(1, client.LocalSeat, "分席正确");
        Assert(host.Seats[1].Occupied, "host 侧占席");
    }

    private static void TestSnapshotCodec()
    {
        Snapshot snap = new Snapshot();
        snap.Tick = 123;
        snap.Crew[0].X = 1.5f;
        snap.Crew[0].Z = -2.5f;
        snap.Crew[0].RotY = 90f;
        snap.Crew[0].HeldCart = 2;
        snap.Crew[0].Sel = 1;
        snap.Crew[0].BlockedByTraffic = true;
        snap.Crew[1].X = -3f;
        snap.Crew[1].HeldCart = -1;
        snap.Carts[1].OwnerSeat = 0;
        snap.Carts[1].Cargo = 4;
        snap.Carts[1].RotY = 180f;
        snap.Labels[0] = "放下餐食车";
        snap.Labels[1] = "";
        snap.Toast = "搭档掉线，bot 接管";
        snap.ToastSeq = 7;

        NetMessage message = NetProtocol.Parse(NetProtocol.BuildSnapshot(snap));
        AssertEqual("snap", message.Type, "快照消息类型");
        Snapshot decoded = NetProtocol.DecodeSnapshot(message);
        AssertEqual(123, decoded.Tick, "tick 往返");
        AssertNear(1.5, decoded.Crew[0].X, 0.0001, "crew x 往返");
        AssertNear(-2.5, decoded.Crew[0].Z, 0.0001, "crew z 往返");
        AssertNear(90.0, decoded.Crew[0].RotY, 0.0001, "crew rotY 往返");
        AssertEqual(2, decoded.Crew[0].HeldCart, "heldCart 往返");
        AssertEqual(1, decoded.Crew[0].Sel, "目标航班下标往返（M3.1 增补键）");
        Assert(decoded.Crew[0].BlockedByTraffic, "权威旅客阻车事实往返");
        Assert(!decoded.Crew[1].BlockedByTraffic, "未阻挡席位往返");
        AssertEqual(0, decoded.Crew[1].Sel, "未设置席位目标下标默认 0 往返");
        AssertEqual(-1, decoded.Crew[1].HeldCart, "空车 -1 往返");
        AssertEqual(0, decoded.Carts[1].OwnerSeat, "cart owner 往返");
        AssertEqual(4, decoded.Carts[1].Cargo, "cart cargo 往返");
        AssertEqual("放下餐食车", decoded.Labels[0], "中文标签往返");
        AssertEqual("搭档掉线，bot 接管", decoded.Toast, "中文 toast 往返");
        AssertEqual(7, decoded.ToastSeq, "toastSeq 往返");
    }

    private static void TestVersionTwoGating()
    {
        AssertEqual(3, NetProtocol.Ver, "协议版本升为 3");

        // 现行消息正常解析。
        NetMessage ping = NetProtocol.Parse(NetProtocol.BuildPing(1.5f));
        Assert(ping != null && ping.Type == "ping", "Ver=3 消息可解析");

        // 旧版（v=1）消息一律拒绝：把版本字段改写后应返回 null。
        string legacy = NetProtocol.BuildPing(1.5f).Replace("\"v\":3", "\"v\":1");
        Assert(legacy.IndexOf("\"v\":1") >= 0, "旧版报文构造");
        Assert(NetProtocol.Parse(legacy) == null, "Ver=1 消息被拒绝");
        Assert(NetProtocol.Parse(NetProtocol.BuildPing(0f).Replace("\"v\":3", "\"v\":2")) == null, "Ver=2 不可混入 M4");

        // 缺版本/坏 JSON 仍然拒绝。
        Assert(NetProtocol.Parse("{\"t\":\"ping\"}") == null, "缺版本拒绝");
        Assert(NetProtocol.Parse("{broken") == null, "坏 JSON 拒绝");
    }

    private static void TestSnapshotShiftBlockRoundtrip()
    {
        Snapshot snap = new Snapshot();
        snap.Tick = 77;
        snap.Score = 320;
        snap.Elapsed = 45.5f;
        snap.Shift.MealPhase = 1;
        snap.Shift.MealProgress = 0.62f;
        snap.Shift.FuelTruckTank = 0.4f;
        snap.Shift.NozzleState = (int)NozzlePhase.OnTruck;
        snap.Shift.NozzleSeat = -1;
        snap.Shift.HoseState = (int)HosePhase.OnAircraft;
        snap.Shift.HoseSeat = 1;
        snap.Shift.HoseFlightId = "CA120";
        snap.Shift.HoseOverfill = 1.25f;
        snap.Shift.ValveOpen = false;
        snap.Shift.Spilling = false;
        snap.Shift.Spills = new[]
        {
            new SpillSnap { Id = 7, X = -6.4f, Z = -5f, Radius = 1.35f, CleanWork = 0.4f },
            new SpillSnap { Id = 8, X = -6.4f, Z = -5f, Radius = 1.35f, CleanWork = 0f }
        };
        snap.Shift.Carts[1].FlightId = "CA120";
        snap.Shift.Carts[1].Arrival = true;
        snap.Shift.Carts[1].DeliverWork = 0.45f;
        snap.Shift.Carts[1].DeliverFlightId = "UB200";
        snap.Shift.Boarding = new[] { "CA120", "UB200" };
        snap.Shift.Passengers = new[]
        {
            new PassengerSnap { FlightId = "CA120", Seq = 3, Stand = 2, Waypoint = 2, Progress01 = 0.31f, Delay = 0f, Released = true },
            new PassengerSnap { FlightId = "CA120", Seq = 7, Stand = 2, Waypoint = 1, Progress01 = 0f, Delay = 1.3f, Released = false }
        };
        snap.Shift.Failed = new[] { "CA120:1" };
        snap.Shift.Events = new[]
        {
            new ShiftEventSnap
            {
                Sequence = 91,
                Time = 45.25f,
                Type = ShiftEventTypes.WrongDelivery,
                FlightId = "CA120",
                RelatedFlightId = "UB200",
                Kind = (int)ServiceKind.Baggage,
                Seat = 1,
                Text = "CA120 收到了 UB200 航班的行李 · 行李任务失败"
            },
            new ShiftEventSnap
            {
                Sequence = 92,
                Time = 45.25f,
                Type = ShiftEventTypes.TaskFailed,
                FlightId = "CA120",
                RelatedFlightId = "UB200",
                Kind = (int)ServiceKind.Baggage,
                Seat = 1,
                Text = "CA120 收到了 UB200 航班的行李 · 行李任务失败"
            }
        };
        snap.Shift.StandReady = new[] { true, false, true };
        snap.Flights = new[]
        {
            new FlightSnap
            {
                Id = "CA120",
                Status = (int)FlightStatus.Servicing,
                Stand = 2,
                Progress = new[] { 1f, 0.5f, 0f, 0.25f },
                Arrival = 12.5f,
                Deadline = 87.5f,
                ArrivalBagsReturned = true
            },
            new FlightSnap { Id = "UB200", Status = (int)FlightStatus.Scheduled, Stand = -1, Arrival = 40f, Deadline = 115f }
        };

        NetMessage message = NetProtocol.Parse(NetProtocol.BuildSnapshot(snap));
        AssertEqual("snap", message.Type, "镜像快照消息类型");
        Snapshot decoded = NetProtocol.DecodeSnapshot(message);

        AssertEqual(77, decoded.Tick, "tick 往返");
        AssertEqual(320, decoded.Score, "Score 往返");
        AssertNear(45.5, decoded.Elapsed, 0.0001, "Elapsed 往返");
        AssertEqual(1, decoded.Shift.MealPhase, "餐食相位往返");
        AssertNear(0.62, decoded.Shift.MealProgress, 0.0001, "餐食读条往返");
        AssertNear(0.4, decoded.Shift.FuelTruckTank, 0.0001, "油车储量往返");
        AssertEqual((int)NozzlePhase.OnTruck, decoded.Shift.NozzleState, "站内油枪状态往返");
        AssertEqual(-1, decoded.Shift.NozzleSeat, "持枪席位往返");
        AssertEqual((int)HosePhase.OnAircraft, decoded.Shift.HoseState, "车载油管状态往返");
        AssertEqual(1, decoded.Shift.HoseSeat, "接管席位往返");
        AssertEqual("CA120", decoded.Shift.HoseFlightId, "车载油管绑定航班往返");
        AssertNear(1.25, decoded.Shift.HoseOverfill, 0.0001, "机位满溢计时往返");
        Assert(!decoded.Shift.ValveOpen, "阀门关闭态往返");
        Assert(!decoded.Shift.Spilling, "无活动漫油态往返");
        AssertEqual(2, decoded.Shift.Spills.Length, "油渍列表长度");
        AssertEqual(7, decoded.Shift.Spills[0].Id, "油渍 id 往返");
        AssertNear(-6.4, decoded.Shift.Spills[0].X, 0.0001, "油渍坐标往返");
        AssertNear(1.35, decoded.Shift.Spills[0].Radius, 0.0001, "油渍半径往返");
        AssertNear(0.4, decoded.Shift.Spills[0].CleanWork, 0.0001, "油渍清理读条往返");
        AssertEqual("CA120", decoded.Shift.Carts[1].FlightId, "车辆货物归属往返");
        Assert(decoded.Shift.Carts[1].Arrival, "车辆到达件往返");
        AssertNear(0.45, decoded.Shift.Carts[1].DeliverWork, 0.0001, "车辆读条往返");
        AssertEqual("UB200", decoded.Shift.Carts[1].DeliverFlightId, "读条绑定接收航班往返");
        AssertEqual("", decoded.Shift.Carts[0].FlightId, "空车往返");
        AssertEqual(2, decoded.Shift.Boarding.Length, "登机集合长度");
        AssertEqual("CA120", decoded.Shift.Boarding[0], "登机集合元素 0");
        AssertEqual("UB200", decoded.Shift.Boarding[1], "登机集合元素 1");
        AssertEqual(2, decoded.Shift.Passengers.Length, "旅客表长度");
        AssertEqual("CA120", decoded.Shift.Passengers[0].FlightId, "旅客航班往返");
        AssertEqual(3, decoded.Shift.Passengers[0].Seq, "旅客序号往返");
        AssertEqual(2, decoded.Shift.Passengers[0].Stand, "旅客机位往返");
        AssertEqual(2, decoded.Shift.Passengers[0].Waypoint, "旅客路点往返");
        AssertNear(0.31, decoded.Shift.Passengers[0].Progress01, 0.0001, "旅客进度往返");
        AssertNear(1.3, decoded.Shift.Passengers[1].Delay, 0.0001, "旅客延迟往返");
        Assert(decoded.Shift.Passengers[0].Released, "已放行旅客状态往返");
        Assert(!decoded.Shift.Passengers[1].Released, "关门队伍状态往返");
        AssertEqual(1, decoded.Shift.Failed.Length, "失败集合长度");
        AssertEqual("CA120:1", decoded.Shift.Failed[0], "失败集合元素");
        AssertEqual(2, decoded.Shift.Events.Length, "近期事件镜像长度");
        AssertEqual(ShiftEventTypes.WrongDelivery, decoded.Shift.Events[0].Type, "错送事件类型往返");
        AssertEqual(91, decoded.Shift.Events[0].Sequence, "错送事件序号往返");
        AssertEqual("CA120", decoded.Shift.Events[0].FlightId, "错送事件接收航班归因往返");
        AssertEqual("UB200", decoded.Shift.Events[0].RelatedFlightId, "错送事件货物来源归因往返");
        AssertEqual((int)ServiceKind.Baggage, decoded.Shift.Events[1].Kind, "失败事件任务种类往返");
        AssertEqual("CA120 收到了 UB200 航班的行李 · 行李任务失败", decoded.Shift.Events[1].Text, "失败归因文字往返");
        Assert(decoded.Shift.StandReady[0] && !decoded.Shift.StandReady[1] && decoded.Shift.StandReady[2],
            "机位就绪往返");

        // M3.3r：手持油枪/油管、站内满溢计时与两路漫油标志与油渍清理读条单独往返。
        Snapshot held = new Snapshot();
        held.Shift.FuelTruckTank = 1f;
        held.Shift.NozzleState = (int)NozzlePhase.Held;
        held.Shift.NozzleSeat = 0;
        held.Shift.HoseState = (int)HosePhase.Held;
        held.Shift.HoseSeat = 1;
        held.Shift.ValveOpen = true;
        held.Shift.StationOverfill = 2.5f;
        held.Shift.Spilling = true;
        held.Shift.HoseSpilling = true;
        held.Shift.Spills = new[]
        {
            new SpillSnap { Id = 19, X = 3f, Z = -4f, Radius = 1.35f, CleanWork = 0.8f }
        };
        Snapshot heldDecoded = NetProtocol.DecodeSnapshot(NetProtocol.Parse(NetProtocol.BuildSnapshot(held)));
        AssertEqual((int)NozzlePhase.Held, heldDecoded.Shift.NozzleState, "手持油枪往返");
        AssertEqual(0, heldDecoded.Shift.NozzleSeat, "持枪席位往返");
        AssertEqual((int)HosePhase.Held, heldDecoded.Shift.HoseState, "手持油管往返");
        AssertEqual(1, heldDecoded.Shift.HoseSeat, "持管席位往返");
        AssertEqual(string.Empty, heldDecoded.Shift.HoseFlightId, "手持油管无绑定航班");
        AssertNear(2.5, heldDecoded.Shift.StationOverfill, 0.0001, "站内满溢计时往返");
        Assert(heldDecoded.Shift.ValveOpen && heldDecoded.Shift.Spilling && heldDecoded.Shift.HoseSpilling,
            "阀门与两路漫油标志往返");
        AssertEqual(19, heldDecoded.Shift.Spills[0].Id, "油渍 id 往返");
        AssertNear(0.8, heldDecoded.Shift.Spills[0].CleanWork, 0.0001, "油渍清理进度往返");
        AssertEqual(2, decoded.Flights.Length, "航班块长度");
        AssertEqual("CA120", decoded.Flights[0].Id, "航班 id 往返");
        AssertEqual((int)FlightStatus.Servicing, decoded.Flights[0].Status, "航班状态往返");
        AssertEqual(2, decoded.Flights[0].Stand, "航班机位往返");
        AssertNear(1.0, decoded.Flights[0].Progress[0], 0.0001, "航班进度 0 往返");
        AssertNear(0.5, decoded.Flights[0].Progress[1], 0.0001, "航班进度 1 往返");
        AssertNear(0.25, decoded.Flights[0].Progress[3], 0.0001, "航班进度 3 往返");
        AssertNear(12.5, decoded.Flights[0].Arrival, 0.0001, "航班到场往返");
        AssertNear(87.5, decoded.Flights[0].Deadline, 0.0001, "航班截止往返");
        Assert(decoded.Flights[0].ArrivalBagsReturned, "航班到达行李往返");
        AssertEqual(-1, decoded.Flights[1].Stand, "候机航班机位往返");

        // 空态（sandbox）镜像块也能往返。
        Snapshot empty = new Snapshot();
        Snapshot emptyDecoded = NetProtocol.DecodeSnapshot(NetProtocol.Parse(NetProtocol.BuildSnapshot(empty)));
        AssertEqual(0, emptyDecoded.Shift.MealPhase, "空态餐食");
        AssertEqual(-1, emptyDecoded.Shift.NozzleSeat, "空态持枪席位");
        AssertEqual(0, emptyDecoded.Shift.NozzleState, "空态油枪在油站");
        AssertEqual((int)HosePhase.OnTruck, emptyDecoded.Shift.HoseState, "空态油管在车上");
        AssertEqual(-1, emptyDecoded.Shift.HoseSeat, "空态持管席位");
        AssertNear(0, emptyDecoded.Shift.StationOverfill + emptyDecoded.Shift.HoseOverfill, 0.0001, "空态满溢计时");
        AssertEqual(0, emptyDecoded.Shift.Spills.Length, "空态油渍列表");
        AssertEqual(0, emptyDecoded.Shift.Boarding.Length, "空态登机集合");
        AssertEqual(0, emptyDecoded.Shift.Passengers.Length, "空态旅客表");
        AssertEqual(0, emptyDecoded.Shift.Failed.Length, "空态失败集合");
        AssertEqual(0, emptyDecoded.Shift.Events.Length, "空态近期事件");
        AssertEqual(0, emptyDecoded.Flights.Length, "空态航班块");
        Assert(emptyDecoded.Shift.StandReady[0] && emptyDecoded.Shift.StandReady[2], "空态机位就绪默认 true");
    }

    private static void TestSnapshotBufferMidpoint()
    {
        SnapshotBuffer buffer = new SnapshotBuffer();
        float p = buffer.Period;

        Snapshot a = new Snapshot();
        a.Tick = 10;
        Snapshot b = new Snapshot();
        b.Tick = 11;
        Assert(buffer.Add(a, 1.0f), "首包入库");
        Assert(buffer.Add(b, 1.0f + p), "次包入库");
        Assert(!buffer.Add(a, 1.0f + 2 * p), "旧包去重去旧");

        // offset = 1.0 − 10P；渲染时刻取 tick 10.5 → localNow = 1.0 + 2.5P。
        Snapshot outA;
        Snapshot outB;
        float t;
        Assert(buffer.Sample(1.0f + 2.5f * p, out outA, out outB, out t), "可采样");
        AssertEqual(10, outA.Tick, "下界帧 tick=10");
        AssertEqual(11, outB.Tick, "上界帧 tick=11");
        AssertNear(0.5, t, 0.001, "中点 t=0.5");

        // 渲染时刻不足首帧时钳到首帧。
        Assert(buffer.Sample(0.5f, out outA, out outB, out t), "过早也可采样（钳制）");
        AssertEqual(10, outA.Tick, "钳到最旧帧");
    }

    private static void TestSnapshotBufferNeverBackward()
    {
        // 经 LoopbackHub 真实传输：Loss=0.5 + Reorder + 少量 Jitter。
        LoopbackHub hub = new LoopbackHub(99);
        hub.LossRate = 0.5f;
        hub.Reorder = true;
        hub.Jitter = 0.02f;
        LoopbackSocket sendSocket = hub.CreateSocket(LoopAddr, 62000);
        LoopbackSocket recvSocket = hub.CreateSocket(LoopAddr, 62001);
        SnapshotBuffer buffer = new SnapshotBuffer();

        float localNow = 0f;
        double lastPosition = double.MinValue;
        int sampled = 0;
        for (int tick = 0; tick < 120; tick++)
        {
            Snapshot snap = new Snapshot();
            snap.Tick = tick;
            sendSocket.Send(LoopAddr, 62001, NetProtocol.BuildSnapshot(snap));
            hub.Pump(0.01f);
            localNow += 0.01f;

            UdpDatagram datagram;
            while (recvSocket.TryReceive(out datagram))
            {
                NetMessage message = NetProtocol.Parse(datagram.Payload);
                if (message != null && message.Type == "snap")
                {
                    buffer.Add(NetProtocol.DecodeSnapshot(message), localNow);
                }
            }

            Snapshot outA;
            Snapshot outB;
            float t;
            if (buffer.Sample(localNow, out outA, out outB, out t))
            {
                double position = outA.Tick + (double)t * (outB.Tick - outA.Tick);
                Assert(position >= lastPosition - 0.0001,
                    "采样位置不倒退 tick=" + tick + " pos=" + position + " last=" + lastPosition);
                lastPosition = position;
                sampled++;
            }
        }

        Assert(sampled > 30, "半数丢包下仍持续采样: " + sampled);
        Assert(buffer.NewestTick >= 110, "最新快照推进到尾部: " + buffer.NewestTick);
    }

    private static void TestCutHeartbeatTimeout()
    {
        LoopbackHub hub = new LoopbackHub(12);
        LoopbackSocket hostSocket = hub.CreateSocket(LoopAddr, NetProtocol.SessionPort);
        LoopbackSocket clientSocket = hub.CreateSocket(LoopAddr, ClientPortA);
        RoomManager host = new RoomManager();
        host.SocketFactory = port => hostSocket;
        RoomManager client = new RoomManager();
        client.SocketFactory = port => clientSocket;
        host.Host("房", "Host", "t");
        client.Join(LoopAddr, "C", "t");
        JoinUntilInRoom(hub, host, client);
        host.StartSandbox();
        DrainEvents(host);
        DrainEvents(client);

        hub.Cut(clientSocket);
        // Cut 后固定 dt 推进 5s+：host 标 bot，client hostlost。
        for (int i = 0; i < 110; i++)
        {
            Step(hub, 0.05f, host, client);
        }

        Assert(host.Seats[1].Bot, "心跳超时后 host 1 席 Bot=true");
        Assert(host.Seats[1].Occupied, "bot 接管仍占席");
        Assert(Contains(DrainEvents(host), "left:1"), "host 收到 left:1 事件");
        Assert(Contains(DrainEvents(client), "hostlost"), "client 收到 hostlost");
        AssertEqual(RoomPhase.Idle, client.Phase, "client 回 Idle");
        Assert(host.Phase == RoomPhase.Playing, "host 继续 Playing");
    }

    private static void ShiftPair(out LoopbackHub hub, out RoomManager host, out RoomManager client,
        out LoopbackSocket hostSocket, out LoopbackSocket clientSocket)
    {
        hub = new LoopbackHub(401);
        hostSocket = hub.CreateSocket(LoopAddr, NetProtocol.SessionPort);
        clientSocket = hub.CreateSocket(LoopAddr, ClientPortA);
        host = new RoomManager();
        LoopbackSocket hs = hostSocket;
        host.SocketFactory = port => hs;
        client = new RoomManager();
        LoopbackSocket cs = clientSocket;
        client.SocketFactory = port => cs;
        Assert(host.Host("shift", "Host", "t"), "host opens");
        Assert(client.Join(LoopAddr, "Client", "t"), "client joins");
        JoinUntilInRoom(hub, host, client);
        DrainEvents(host);
        DrainEvents(client);
        Assert(host.StartShift(), "production start");
        host.BroadcastSnapshot(new Snapshot { Tick = 10, Elapsed = 84.5f, Score = 70 });
        Advance(hub, .2f, host, client);
        AssertEqual(RoomPhase.Playing, client.Phase, "client production playing");
        Assert(!client.IsPractice && !host.IsPractice, "both finite mode");
        AssertEqual(host.MatchId, client.MatchId, "same match identity");
        AssertEqual(1, client.RoundId, "first round");
        AssertEqual(1, client.AuthorityEpoch, "first authority");
        DrainEvents(host);
        DrainEvents(client);
    }

    private static void TestM4SnapshotCodec()
    {
        Snapshot snap = new Snapshot
        {
            Tick = 512, RoundId = 3, AuthorityEpoch = 2, MatchId = "room:3", IsPractice = false,
            Ended = true, CompletedFlights = 4, MissedFlights = 1, Stars = 3,
            CompletedTasks = 16, Deliveries = 4, TrafficStops = 9, Elapsed = 300f,
            ReplayIndex = 601, ReplayCount = 602, ReplayTime = 13.5f, ReplayStarted = true,
            ReplayMissing = new[] { 5, 17 }, ReplayIncomplete = true
        };
        snap.Shift.NextEventSequence = 801;
        snap.Shift.NextSpillId = 22;
        snap.Shift.FuelTruckAtStation = true;
        snap.Shift.CartDrivers = new[] { 1, -1, 0 };
        snap.Shift.Boarding = new[] { "Q1" };
        snap.Shift.GateSeats = new[] { 1 };
        snap.Shift.HumanTaskCounts = new[] { 2, 1, 3, 5 };
        snap.Flights = new[] { new FlightSnap { Id = "Q1", PlaneAssignedAt = 64.25f } };
        Snapshot decoded = NetProtocol.DecodeSnapshot(NetProtocol.Parse(NetProtocol.BuildSnapshot(snap)));
        AssertEqual(3, decoded.RoundId, "round codec");
        AssertEqual(2, decoded.AuthorityEpoch, "epoch codec");
        AssertEqual("room:3", decoded.MatchId, "match codec");
        Assert(decoded.Ended && !decoded.IsPractice, "finite final codec");
        AssertEqual(4, decoded.CompletedFlights, "completed codec");
        AssertEqual(1, decoded.MissedFlights, "missed codec");
        AssertEqual(3, decoded.Stars, "stars codec");
        AssertEqual(16, decoded.CompletedTasks, "task codec");
        AssertEqual(4, decoded.Deliveries, "delivery codec");
        AssertEqual(9, decoded.TrafficStops, "traffic codec");
        AssertEqual(601, decoded.ReplayIndex, "sample index codec");
        AssertEqual(602, decoded.ReplayCount, "sample count codec");
        AssertNear(13.5, decoded.ReplayTime, .0001, "playback clock codec");
        Assert(decoded.ReplayStarted && decoded.ReplayIncomplete, "playback flags codec");
        AssertEqual(17, decoded.ReplayMissing[1], "unrecoverable gap codec");
        AssertEqual(801, decoded.Shift.NextEventSequence, "event counter recovery");
        AssertEqual(22, decoded.Shift.NextSpillId, "spill identity recovery");
        Assert(decoded.Shift.FuelTruckAtStation, "station recovery");
        AssertEqual(1, decoded.Shift.CartDrivers[0], "cart claim recovery");
        AssertEqual(1, decoded.Shift.GateSeats[0], "gate claim recovery");
        AssertEqual(5, decoded.Shift.HumanTaskCounts[3], "human task recovery");
        AssertNear(64.25, decoded.Flights[0].PlaneAssignedAt, .0001, "plane interpolation recovery");
        Assert(NetProtocol.BuildSnapshot(snap).Length < 4096, "compact recovery sample");
    }

    private static void TestM4RetryAndFences()
    {
        LoopbackHub hub; RoomManager host, client; LoopbackSocket hs, cs;
        ShiftPair(out hub, out host, out client, out hs, out cs);
        string oldMatch = host.MatchId;
        Assert(!host.RetryShift(), "cannot retry unfinished round");
        Assert(!client.RetryShift(), "client cannot retry");
        host.BroadcastSnapshot(new Snapshot { Tick = 20, Ended = true, Elapsed = 300f,
            Score = 480, CompletedFlights = 4, Stars = 3 });
        Advance(hub, .15f, host, client);
        Snapshot final;
        Assert(client.TryTakeSnapshot(out final) && final.Ended, "authoritative result delivered");
        host.BroadcastSnapshot(new Snapshot { Tick = 21, Ended = false, Elapsed = 250f });
        Advance(hub, .1f, host, client);
        Snapshot ignored;
        Assert(!client.TryTakeSnapshot(out ignored), "final cannot regress to running");
        // Re-send the final, then lose the first retry control packets. Roster/start retries heal.
        host.BroadcastSnapshot(new Snapshot { Tick = 22, Ended = true, Elapsed = 300f, Score = 480 });
        Advance(hub, .1f, host, client);
        hub.LossRate = 1f;
        Assert(host.RetryShift(), "authority retries finished shift");
        Assert(!host.RetryShift(), "duplicate retry no-op before next result");
        AssertEqual(2, host.RoundId, "retry advances one round");
        Assert(Contains(DrainEvents(host), "retry"), "authority receives retry initialization event");
        Assert(host.MatchId != oldMatch, "new durable match identity");
        hub.LossRate = .35f; hub.DuplicateRate = 1f; hub.Reorder = true;
        for (int i = 0; i < 60 && client.RoundId != 2; i++) Step(hub, .05f, host, client);
        AssertEqual(2, client.RoundId, "retry healed under loss/dup/reorder");
        List<string> events = DrainEvents(client);
        int retryEvents = 0;
        foreach (string evt in events) if (evt == "retry") retryEvents++;
        AssertEqual(1, retryEvents, "new round initialization once");
        hub.LossRate = 0f; hub.DuplicateRate = 0f; hub.Reorder = false;
        host.BroadcastSnapshot(new Snapshot { Tick = 0, Elapsed = 0f });
        Advance(hub, .2f, host, client);
        Assert(client.TryTakeSnapshot(out ignored) && ignored.Tick == 0, "new round tick resets");
        hs.Send(LoopAddr, ClientPortA, NetProtocol.BuildStart(1, false, 1, 1, oldMatch));
        hs.Send(LoopAddr, ClientPortA, NetProtocol.BuildSnapshot(new Snapshot {
            Tick = 99999, RoundId = 1, AuthorityEpoch = 1, MatchId = oldMatch, IsPractice = false, Ended = true }));
        cs.Send(LoopAddr, NetProtocol.SessionPort, NetProtocol.BuildInput(new InputFrame {
            Seq = 9999, RoundId = 1, AuthorityEpoch = 1, MatchId = oldMatch, Pressed = true, Cycle = true, Held = true }));
        Advance(hub, .1f, host, client);
        AssertEqual(2, client.RoundId, "late start cannot reset current round");
        Assert(!client.TryTakeSnapshot(out ignored), "late prior-round result ignored");
        Assert(!host.HasRemoteInput(1), "late prior-round input ignored");
        client.SendInput(new InputFrame { Seq = 0, Pressed = true, Cycle = true });
        Advance(hub, .1f, host, client);
        InputFrame input = host.ConsumeLatestInput(1);
        Assert(input.Pressed && input.Cycle && input.Seq == 0, "fresh round low sequence accepted");
        Assert(!host.ConsumeLatestInput(1).Pressed, "edge consumed once");
    }

    private static void TestM4PlannedMigration()
    {
        LoopbackHub hub; RoomManager host, client; LoopbackSocket hs, cs;
        ShiftPair(out hub, out host, out client, out hs, out cs);
        string match = client.MatchId;
        Snapshot checkpoint = new Snapshot { Tick = 40, Elapsed = 98.25f, Score = 280,
            CompletedFlights = 2, TrafficStops = 7 };
        checkpoint.Crew[1].HeldCart = 2;
        checkpoint.Carts[2].OwnerSeat = 1;
        checkpoint.Shift.FuelTruckTank = .43f;
        checkpoint.Shift.HoseState = (int)HosePhase.OnAircraft;
        checkpoint.Shift.HoseSeat = 1;
        checkpoint.Shift.HoseFlightId = "Q2";
        checkpoint.Shift.ValveOpen = true;
        // Lose the final live packet and first handoff: a repeated handoff carries the checkpoint.
        hub.LossRate = 1f;
        host.BroadcastSnapshot(checkpoint);
        host.Leave();
        hub.LossRate = 0f;
        Advance(hub, .5f, host, client);
        Assert(client.IsHost, "remaining client promoted");
        AssertEqual(1, client.LocalSeat, "promotion keeps seat1");
        AssertEqual(RoomPhase.Playing, client.Phase, "promotion remains in match");
        AssertEqual(2, client.AuthorityEpoch, "authority epoch increments");
        AssertEqual(match, client.MatchId, "same round identity after migration");
        Assert(client.Seats[0].Bot && !client.Seats[0].CreditEligible, "departed host bot excludes credit");
        Assert(client.Seats[1].CreditEligible, "surviving human eligible");
        AssertNear(98.25, client.MigrationSnapshot.Elapsed, .0001, "restore checkpoint time without detection deduction");
        AssertEqual(2, client.MigrationSnapshot.Crew[1].HeldCart, "surviving cart association");
        AssertEqual(1, client.MigrationSnapshot.Carts[2].OwnerSeat, "stable driver association");
        AssertNear(.43, client.MigrationSnapshot.Shift.FuelTruckTank, .0001, "recover partial fuel");
        AssertEqual("Q2", client.MigrationSnapshot.Shift.HoseFlightId, "recover attached truck hose");
        AssertEqual((int)HosePhase.OnAircraft, client.MigrationSnapshot.Shift.HoseState, "recover hose phase");
        List<string> events = DrainEvents(client);
        int migrated = 0;
        foreach (string evt in events) if (evt == "migrated") migrated++;
        AssertEqual(1, migrated, "duplicate handoff promotes once");
        Assert(!Contains(events, "closed") && !Contains(events, "hostlost"), "migration avoids terminal event");
    }

    private static void TestM4AbruptMigration()
    {
        LoopbackHub hub; RoomManager host, client; LoopbackSocket hs, cs;
        ShiftPair(out hub, out host, out client, out hs, out cs);
        Snapshot taken;
        Assert(client.TryTakeSnapshot(out taken), "consumer already took last snapshot");
        hub.Cut(hs);
        Advance(hub, 5.3f, client);
        Assert(client.IsHost && client.LocalSeat == 1, "timeout promotes stable seat1");
        Assert(client.Seats[0].Bot && !client.Seats[0].CreditEligible, "old host bot uncredited");
        AssertNear(84.5, client.MigrationSnapshot.Elapsed, .0001, "retained consumed checkpoint");
        string oldMatch = client.MatchId;
        hub.Uncut(hs);
        hs.Send(LoopAddr, ClientPortA, NetProtocol.BuildSnapshot(new Snapshot {
            Tick = 999, RoundId = 1, AuthorityEpoch = 1, MatchId = oldMatch, IsPractice = false,
            Score = 9999, Elapsed = 200f }));
        hs.Send(LoopAddr, ClientPortA, NetProtocol.BuildRoundControl("close", 1, 1, oldMatch));
        Advance(hub, .3f, client);
        Assert(client.IsHost && client.AuthorityEpoch == 2, "late old authority cannot revoke promotion");
        AssertEqual(70, client.LastAuthoritativeSnapshot.Score, "late old authority cannot overwrite checkpoint");
        AssertEqual(RoomPhase.Playing, client.Phase, "late old close not terminal");
        client.BroadcastSnapshot(new Snapshot { Tick = 0, Ended = true, Elapsed = 300f, Score = 480 });
        Assert(client.RetryShift(), "promoted authority can retry");
        AssertEqual(2, client.RoundId, "promoted retry new round");
        AssertEqual(1, client.LocalSeat, "promoted retry stable seat");
    }

    private static void TestM4ClientLoss()
    {
        for (int abrupt = 0; abrupt < 2; abrupt++)
        {
            LoopbackHub hub; RoomManager host, client; LoopbackSocket hs, cs;
            ShiftPair(out hub, out host, out client, out hs, out cs);
            client.SendInput(new InputFrame { Seq = 2, Held = true, Pressed = true, Cycle = true });
            Advance(hub, .1f, host, client);
            Assert(host.HasRemoteInput(1), "remote input before loss");
            if (abrupt == 1) hub.Cut(cs); else client.Leave();
            Advance(hub, abrupt == 1 ? 5.3f : .6f, host);
            Assert(host.IsHost && host.LocalSeat == 0 && host.Phase == RoomPhase.Playing, "host match continues");
            Assert(host.Seats[1].Bot && !host.Seats[1].CreditEligible, "lost client replaced without credit");
            Assert(!host.HasRemoteInput(1), "lost seat input reset");
            InputFrame input = host.ConsumeLatestInput(1);
            Assert(!input.Held && !input.Pressed && !input.Cycle, "loss clears stale edges and hold");
        }
    }

    private static void TestM4CloseTerminal()
    {
        LoopbackHub hub; RoomManager host, client; LoopbackSocket hs, cs;
        ShiftPair(out hub, out host, out client, out hs, out cs);
        host.CloseRoom();
        Advance(hub, .5f, host, client);
        AssertEqual(RoomPhase.Idle, client.Phase, "explicit terminal close returns idle");
        Assert(!client.IsHost, "terminal close does not migrate");
        Assert(Contains(DrainEvents(client), "closed"), "terminal closed event");
    }

    private static void TestM4ReplayRepair()
    {
        LoopbackHub hub; RoomManager host, client; LoopbackSocket hs, cs;
        ShiftPair(out hub, out host, out client, out hs, out cs);
        client.RequestReplayFrame(3);
        Advance(hub, .1f, host, client);
        Assert(Contains(DrainEvents(host), "replayrequest:3"), "missing sample request reaches authority");
        host.SendReplayFrame(3, new Snapshot { Tick = 45, Elapsed = 3f, ReplayIndex = 3, Score = 40 });
        Advance(hub, .1f, host, client);
        Snapshot replay;
        Assert(client.TryTakeReplaySnapshot(out replay), "sample repair delivered");
        AssertEqual(3, replay.ReplayIndex, "sample repair slot");
        AssertEqual(40, replay.Score, "sample repair content");
        Assert(!client.TryTakeReplaySnapshot(out replay), "sample queue consumed once");
        hub.LossRate = 1f;
        client.ReplayReady();
        hub.LossRate = 0f; hub.DuplicateRate = 1f;
        Advance(hub, 1.2f, host, client);
        int ready = 0;
        foreach (string evt in DrainEvents(host)) if (evt == "replayready") ready++;
        AssertEqual(1, ready, "ready retransmission heals drop and deduplicates");
        string oldMatch = host.MatchId;
        host.BroadcastSnapshot(new Snapshot { Tick = 46, Ended = true });
        Advance(hub, .1f, host, client);
        cs.Send(LoopAddr, NetProtocol.SessionPort, NetProtocol.BuildRoundControl("replayrequest", host.RoundId, host.AuthorityEpoch, host.MatchId, null, 2));
        Advance(hub, .1f, host, client);
        Assert(host.RetryShift(), "new round for replay fence");
        Assert(!Contains(DrainEvents(host), "replayrequest:2"), "pending prior-round replay request cleared");
        Advance(hub, .2f, host, client);
        hs.Send(LoopAddr, ClientPortA, NetProtocol.BuildRoundControl("replayframe", 1, 1, oldMatch,
            new Snapshot { ReplayIndex = 99 }, 99));
        cs.Send(LoopAddr, NetProtocol.SessionPort, NetProtocol.BuildRoundControl("replayrequest", 1, 1, oldMatch, null, 99));
        DrainEvents(host);
        Advance(hub, .1f, host, client);
        Assert(!client.TryTakeReplaySnapshot(out replay), "late old replay repair excluded");
        Assert(!Contains(DrainEvents(host), "replayrequest:99"), "late old replay request excluded");
    }

    private sealed class SelectiveControlSocket : IUdpSocket
    {
        readonly IUdpSocket inner;
        readonly string watched;
        public bool Drop;
        public int Attempts;
        public SelectiveControlSocket(IUdpSocket socket, string type) { inner=socket;watched=type; }
        public int LocalPort { get { return inner.LocalPort; } }
        public void Send(string address, int port, string payload)
        {
            NetMessage message=NetProtocol.Parse(payload);
            if(message!=null && message.Type==watched)
            {
                Attempts++;
                if(Drop)return;
            }
            inner.Send(address,port,payload);
        }
        public bool TryReceive(out UdpDatagram datagram) { return inner.TryReceive(out datagram); }
        public void Close() { inner.Close(); }
    }

    private static void TestM4ReplayReadyLongLoss()
    {
        LoopbackHub hub=new LoopbackHub(450);
        SelectiveControlSocket hostSocket=new SelectiveControlSocket(hub.CreateSocket(LoopAddr,NetProtocol.SessionPort),"replayack");
        SelectiveControlSocket clientSocket=new SelectiveControlSocket(hub.CreateSocket(LoopAddr,ClientPortA),"replayready");
        RoomManager host=new RoomManager();host.SocketFactory=port=>hostSocket;
        RoomManager client=new RoomManager();client.SocketFactory=port=>clientSocket;
        host.Host("shift","Host","t");client.Join(LoopAddr,"Client","t");
        JoinUntilInRoom(hub,host,client);
        Assert(host.StartShift(),"finite start for selective loss");
        host.BroadcastSnapshot(new Snapshot {Tick=1,Ended=true,Elapsed=300f,ReplayCount=1});
        Advance(hub,.2f,host,client);DrainEvents(host);DrainEvents(client);
        clientSocket.Drop=true;
        client.ReplayReady();
        // Only ready controls are lost; heartbeat keeps the peer alive beyond the former30s expiry.
        Advance(hub,35f,host,client);
        Assert(!client.IsHost && client.Phase==RoomPhase.Playing,"heartbeats prevent timeout promotion");
        Assert(clientSocket.Attempts>=65 && clientSocket.Attempts<=72,"one bounded entry retries each halfsecond");
        Assert(!Contains(DrainEvents(host),"replayready"),"all early ready attempts dropped");
        int before=clientSocket.Attempts;
        client.ReplayReady();client.ReplayReady();
        AssertEqual(before,clientSocket.Attempts,"repeated ready calls do not add pending controls");
        clientSocket.Drop=false;hostSocket.Drop=true;
        Advance(hub,2f,host,client);
        int received=0;foreach(string evt in DrainEvents(host))if(evt=="replayready")received++;
        AssertEqual(1,received,"ready after35s emits host event exactly once");
        Assert(hostSocket.Attempts>=3,"lost acknowledgement reanswered on ready retries");
        hostSocket.Drop=false;
        Advance(hub,1f,host,client);
        int afterAck=clientSocket.Attempts;
        Advance(hub,2f,host,client);
        AssertEqual(afterAck,clientSocket.Attempts,"successful ack terminates retransmission");
        Assert(!Contains(DrainEvents(host),"replayready"),"duplicate retries never restart playback event");
    }

    private static void TestM4BufferFence()
    {
        SnapshotBuffer buffer = new SnapshotBuffer();
        Assert(buffer.Add(new Snapshot { Tick = 300, RoundId = 1, AuthorityEpoch = 1, MatchId = "r:1" }, 20f), "old authority sample");
        Assert(buffer.Add(new Snapshot { Tick = 0, RoundId = 1, AuthorityEpoch = 2, MatchId = "r:1" }, 21f), "new authority resets tick");
        AssertEqual(1, buffer.Count, "old epoch buffer cleared");
        Assert(!buffer.Add(new Snapshot { Tick = 999, RoundId = 1, AuthorityEpoch = 1, MatchId = "r:1" }, 21.1f), "old authority rejected");
        Assert(buffer.Add(new Snapshot { Tick = 0, RoundId = 2, AuthorityEpoch = 2, MatchId = "r:2" }, 22f), "new round resets tick");
        Assert(!buffer.Add(new Snapshot { Tick = 999, RoundId = 1, AuthorityEpoch = 2, MatchId = "r:1" }, 22.1f), "old round rejected");
        Assert(!buffer.Add(new Snapshot { Tick = -1, RoundId = 2, AuthorityEpoch = 2, MatchId = "r:2" }, 22.1f), "negative tick rejected");
    }

    private static void TestAgeCheckBoundaries()
    {
        DateTime dob = new DateTime(2000, 1, 15);
        AssertEqual(12, AgeCheck.YearsAt(dob, new DateTime(2013, 1, 14)), "生日前一天 12 岁");
        AssertEqual(13, AgeCheck.YearsAt(dob, new DateTime(2013, 1, 15)), "生日当天 13 岁");
        Assert(!AgeCheck.Allowed(dob, new DateTime(2013, 1, 14)), "生日前一天拦截");
        Assert(AgeCheck.Allowed(dob, new DateTime(2013, 1, 15)), "生日当天满 13 算成年");
        Assert(AgeCheck.Allowed(dob, new DateTime(2026, 9, 23)), "成年人放行");
        Assert(!AgeCheck.Allowed(new DateTime(2020, 6, 1), new DateTime(2026, 9, 23)), "儿童拦截");

        // 闰年 2/29 生日：平年 3/1 视为满岁。
        DateTime leapDob = new DateTime(2000, 2, 29);
        AssertEqual(11, AgeCheck.YearsAt(leapDob, new DateTime(2012, 2, 28)), "12 岁生日前一天 11 岁");
        AssertEqual(12, AgeCheck.YearsAt(leapDob, new DateTime(2012, 2, 29)), "12 岁生日当天满 12");
        AssertEqual(12, AgeCheck.YearsAt(leapDob, new DateTime(2013, 2, 28)), "平年 2/28 仍未满 13");
        Assert(!AgeCheck.Allowed(leapDob, new DateTime(2013, 2, 28)), "平年 2/28 拦截");
        AssertEqual(13, AgeCheck.YearsAt(leapDob, new DateTime(2013, 3, 1)), "平年 3/1 满 13");
        Assert(AgeCheck.Allowed(leapDob, new DateTime(2013, 3, 1)), "平年 3/1 放行");
        AssertEqual(16, AgeCheck.YearsAt(leapDob, new DateTime(2016, 2, 29)), "闰年生日当天满 16");
        Assert(AgeCheck.Allowed(leapDob, new DateTime(2016, 2, 29)), "闰年 2/29 放行");

        // 自定义最小年龄。
        Assert(AgeCheck.Allowed(new DateTime(2010, 9, 23), new DateTime(2026, 9, 23), 16), "16 岁生日当天满 16");
        Assert(!AgeCheck.Allowed(new DateTime(2010, 9, 24), new DateTime(2026, 9, 23), 16), "16 岁生日前一天拦截");
    }
}
