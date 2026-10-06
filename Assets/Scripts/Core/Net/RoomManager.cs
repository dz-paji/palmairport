using System;
using System.Collections.Generic;

namespace IslandAirport
{
    /// <summary>房间状态机相位。</summary>
    public enum RoomPhase
    {
        Idle = 0,
        Listening = 1,
        Connecting = 2,
        Joining = 3,
        InRoom = 4,
        Starting = 5,
        Playing = 6,
        Closed = 7
    }

    /// <summary>一席的对外信息（HUD 名牌/搭档状态用）。</summary>
    public class SeatInfo
    {
        public string Name = string.Empty;
        public bool Occupied;
        public bool Bot;
        public bool CreditEligible;
    }

    /// <summary>
    /// 双角色房间状态机（host 权威 / client 意图）。
    /// host：Listening 收 join（Playing→reject ingame；满→reject full；token 空→reject auth；
    /// 否则分席 accept+roster）；Playing 收 in 按 seq 去旧存 latest[seat]；
    /// 心跳超时→席 Bot=true+roster+事件。roster 房间内 1Hz 周期广播（携带房间 phase，丢包自愈）。
    /// client：join 重试至 accept/reject；production start/roster 携带 round/epoch/match。
    /// Practice 失联回大厅；production 从最后权威检查点接任且保留席位。
    /// M2 的 join auth 校验只做「token 非空」格式校验（真校验留 M3）。
    /// 全部计时走 Pump(dt)，禁墙钟。
    /// </summary>
    public class RoomManager
    {
        private readonly Queue<string> _events = new Queue<string>();
        private ReliableControl _control;
        private IUdpSocket _socket;
        private string _peerAddress;
        private int _peerPort;
        private bool _hasPeer;

        private float _rosterTimer;
        private float _pingTimer;
        private float _joinTimer;
        private float _closeDeadline = -1f;
        private int _joinSeq;
        private int _rosterSeq;
        private int _startSeq;
        private int _leaveSeq;
        private bool _startAcked;
        private bool _timeoutMarked;
        private readonly int[] _lastInputSeq = new int[NetProtocol.SeatCount];
        private readonly InputFrame[] _latestInput = new InputFrame[NetProtocol.SeatCount];
        private readonly bool[] _hasInput = new bool[NetProtocol.SeatCount];
        private readonly bool[] _pendingPressed = new bool[NetProtocol.SeatCount];
        private readonly bool[] _pendingCycle = new bool[NetProtocol.SeatCount];
        private readonly float[] _lastHeard = new float[NetProtocol.SeatCount];
        private float _lastHeardHost;
        private Snapshot _latestSnapshot;
        private Snapshot _lastAuthoritativeSnapshot;
        private readonly Queue<Snapshot> _replaySnapshots = new Queue<Snapshot>();
        private string _roomId = string.Empty;
        private int _lastSnapshotTick = -1;
        private bool _replayReady;
        private int _lastRosterSeq = -1;

        public RoomManager()
        {
            Seats = new SeatInfo[NetProtocol.SeatCount];
            for (int i = 0; i < Seats.Length; i++)
            {
                Seats[i] = new SeatInfo();
                _lastInputSeq[i] = -1;
            }

            _control = new ReliableControl(SendPeer);
            Phase = RoomPhase.Idle;
            LocalSeat = -1;
        }

        /// <summary>套接工厂注入（测试给 LoopbackSocket；缺省用真实 UDP）。</summary>
        public Func<int, IUdpSocket> SocketFactory;

        public RoomPhase Phase { get; private set; }

        public bool IsHost { get; private set; }
        public bool IsPractice { get; private set; } = true;
        public int RoundId { get; private set; }
        public int AuthorityEpoch { get; private set; }
        public string MatchId { get; private set; } = string.Empty;
        public Snapshot LastAuthoritativeSnapshot { get { return _lastAuthoritativeSnapshot; } }
        public Snapshot MigrationSnapshot { get; private set; }

        /// <summary>本端席位：host=0，client=accept 分席，未入房 -1。</summary>
        public int LocalSeat { get; private set; }

        public SeatInfo[] Seats { get; private set; }

        public string RoomName = string.Empty;

        /// <summary>本地时钟（Pump 累计）。</summary>
        public float Now { get; private set; }

        /// <summary>host 累计外发快照数（批测断言面）。</summary>
        public int SnapshotsSent { get; private set; }

        /// <summary>beacon 用房间状态：lobby / playing。</summary>
        public string BeaconState
        {
            get { return Phase == RoomPhase.Playing || Phase == RoomPhase.Starting ? "playing" : "lobby"; }
        }

        /// <summary>已占席数。</summary>
        public int TakenCount
        {
            get
            {
                int taken = 0;
                for (int i = 0; i < Seats.Length; i++)
                {
                    if (Seats[i].Occupied)
                    {
                        taken++;
                    }
                }

                return taken;
            }
        }

        // ---- 角色入口 ----

        /// <summary>host 开房：绑定会话端口，进入 Listening。</summary>
        public bool Host(string roomName, string hostName, string authToken)
        {
            if (Phase != RoomPhase.Idle)
            {
                return false;
            }

            _socket = CreateSocket(NetProtocol.SessionPort);
            if (_socket == null)
            {
                return false;
            }

            ResetAllInputState();
            IsHost = true;
            LocalSeat = 0;
            RoomName = roomName ?? string.Empty;
            Seats[0].Name = hostName ?? string.Empty;
            Seats[0].Occupied = true;
            Seats[0].Bot = false;
            Seats[0].CreditEligible = true;
            _roomId = Guid.NewGuid().ToString("N");
            Phase = RoomPhase.Listening;
            _rosterTimer = 0f;
            _pingTimer = 0f;
            return true;
        }

        /// <summary>client 加入：向目标地址发 join（1s 重试至 accept/reject，5s 超时）。</summary>
        public bool Join(string address, string displayName, string authToken)
        {
            if (Phase != RoomPhase.Idle || string.IsNullOrEmpty(address))
            {
                return false;
            }

            _socket = CreateSocket(0);
            if (_socket == null)
            {
                return false;
            }

            ResetAllInputState();
            IsHost = false;
            LocalSeat = -1;
            _peerAddress = address;
            _peerPort = NetProtocol.SessionPort;
            _hasPeer = true;
            _joinSeq++;
            _joinTimer = 0f;
            _pingTimer = 0f;
            _lastHeardHost = Now;
            Phase = RoomPhase.Joining;
            _control.Track("join:" + _joinSeq,
                NetProtocol.BuildJoin(_joinSeq, displayName, authToken),
                NetProtocol.ControlRetry, NetProtocol.JoinTimeout);
            return true;
        }

        /// <summary>host 开始自由练习：start 重复至 client 首帧 in，roster 转 playing。</summary>
        public bool StartSandbox()
        {
            if (!IsHost || (Phase != RoomPhase.Listening && Phase != RoomPhase.InRoom))
            {
                return false;
            }

            Phase = RoomPhase.Playing;
            if (_hasPeer && Seats[1].Occupied && !Seats[1].Bot)
            {
                _startSeq++;
                _startAcked = false;
                _control.Track("start:" + _startSeq, NetProtocol.BuildStart(_startSeq),
                    NetProtocol.ControlRetry, 10f);
            }

            return true;
        }

        /// <summary>Production 300-second match; practice remains a separate compatibility API.</summary>
        public bool StartShift()
        {
            if (!IsHost || (Phase != RoomPhase.Listening && Phase != RoomPhase.InRoom)) return false;
            IsPractice = false;
            AuthorityEpoch = 1;
            BeginShiftRound();
            return true;
        }

        /// <summary>Only the current authority can retry a completed production match.</summary>
        public bool RetryShift()
        {
            if (!IsHost || IsPractice || Phase != RoomPhase.Playing ||
                _lastAuthoritativeSnapshot == null || !_lastAuthoritativeSnapshot.Ended) return false;
            BeginShiftRound();
            _events.Enqueue("retry");
            return true;
        }

        private void BeginShiftRound()
        {
            RoundId++;
            MatchId = _roomId + ":" + RoundId;
            Phase = RoomPhase.Playing;
            ResetRoundState();
            if (_hasPeer && Seats[1].Occupied && !Seats[1].Bot)
            {
                _startSeq++;
                _control.Track("start:" + _startSeq,
                    NetProtocol.BuildStart(_startSeq, false, RoundId, AuthorityEpoch, MatchId),
                    NetProtocol.ControlRetry, 30f);
                BroadcastRoster();
            }
        }

        private void ResetRoundState()
        {
            int queued = _events.Count;
            for (int i = 0; i < queued; i++)
            {
                string evt = _events.Dequeue();
                if (evt != "start" && evt != "retry" && evt != "replayready" &&
                    !evt.StartsWith("replayrequest:", StringComparison.Ordinal)) _events.Enqueue(evt);
            }
            ResetAllInputState();
            _latestSnapshot = null;
            _lastAuthoritativeSnapshot = null;
            MigrationSnapshot = null;
            _lastSnapshotTick = -1;
            _replaySnapshots.Clear();
            _replayReady = false;
            _startAcked = false;
            _control.AckPrefix("start:");
            _control.AckPrefix("replayready:");
        }

        /// <summary>client 离开；production host 交接当局，practice host 关闭房间。</summary>
        public void Leave()
        {
            if (IsHost)
            {
                if (!IsPractice && Phase == RoomPhase.Playing && _hasPeer && !Seats[1].Bot)
                {
                    _control.Reset();
                    _control.TrackBurst(NetProtocol.BuildRoundControl("handoff", RoundId,
                        AuthorityEpoch, MatchId, _lastAuthoritativeSnapshot), 3, 0.15f);
                    _closeDeadline = Now + 1f;
                    Phase = RoomPhase.Closed;
                }
                else CloseRoom();
                return;
            }

            if (Phase == RoomPhase.Idle || Phase == RoomPhase.Closed)
            {
                return;
            }

            if (_hasPeer)
            {
                _leaveSeq++;
                _control.TrackBurst(NetProtocol.BuildLeave(_leaveSeq, LocalSeat, RoundId, AuthorityEpoch, MatchId), 3, 0.15f);
                _closeDeadline = Now + 1f;
                Phase = RoomPhase.Closed;
            }
            else
            {
                Cleanup();
            }
        }

        /// <summary>host 关房：close 300ms 内连发 3 次后释放。</summary>
        public void CloseRoom()
        {
            if (!IsHost || Phase == RoomPhase.Idle || Phase == RoomPhase.Closed)
            {
                return;
            }

            if (_hasPeer && Seats[1].Occupied && !Seats[1].Bot)
            {
                _leaveSeq++;
                _control.TrackBurst((IsPractice ? NetProtocol.BuildClose(_leaveSeq) : NetProtocol.BuildRoundControl("close", RoundId, AuthorityEpoch, MatchId)), 3, 0.15f);
                _closeDeadline = Now + 1f;
                Phase = RoomPhase.Closed;
            }
            else
            {
                Cleanup();
            }
        }

        // ---- 玩法面 ----

        /// <summary>client 上行一帧输入（30Hz 全量，天然抗丢包）。</summary>
        public void SendInput(InputFrame frame)
        {
            if (IsHost || !_hasPeer || (Phase != RoomPhase.Starting && Phase != RoomPhase.Playing))
            {
                return;
            }

            frame.RoundId = RoundId;
            frame.AuthorityEpoch = AuthorityEpoch;
            frame.MatchId = MatchId;
            SendPeer(NetProtocol.BuildInput(frame));
        }

        /// <summary>host 取远端席最新输入（无输入返回零帧）。</summary>
        public InputFrame LatestInput(int seat)
        {
            if (seat < 0 || seat >= NetProtocol.SeatCount)
            {
                return new InputFrame();
            }

            return _latestInput[seat];
        }

        public InputFrame ConsumeLatestInput(int seat)
        {
            if (seat < 0 || seat >= NetProtocol.SeatCount)
            {
                return new InputFrame();
            }

            InputFrame frame = _latestInput[seat];
            frame.Pressed = _pendingPressed[seat];
            frame.Cycle = _pendingCycle[seat];
            _pendingPressed[seat] = false;
            _pendingCycle[seat] = false;
            return frame;
        }

        /// <summary>host：该席是否收到过远端输入。</summary>
        public bool HasRemoteInput(int seat)
        {
            return seat >= 0 && seat < NetProtocol.SeatCount && _hasInput[seat];
        }

        /// <summary>host：该席是否远端真人（占用且非 bot）。</summary>
        public bool IsRemoteSeat(int seat)
        {
            return IsHost && seat >= 0 && seat != LocalSeat && seat < NetProtocol.SeatCount &&
                Seats[seat].Occupied && !Seats[seat].Bot;
        }

        /// <summary>host 15Hz 广播快照。</summary>
        public void BroadcastSnapshot(Snapshot snap)
        {
            if (!IsHost || snap == null || Phase != RoomPhase.Playing)
            {
                return;
            }

            if (!IsPractice && _lastAuthoritativeSnapshot != null &&
                _lastAuthoritativeSnapshot.Ended && !snap.Ended) return;
            snap.RoundId = RoundId;
            snap.AuthorityEpoch = AuthorityEpoch;
            snap.MatchId = MatchId;
            snap.IsPractice = IsPractice;
            // Keep a serialized checkpoint so a caller's later mutation cannot change recovery.
            _lastAuthoritativeSnapshot = NetProtocol.DecodeSnapshot(NetProtocol.Parse(NetProtocol.BuildSnapshot(snap)));
            if (!_hasPeer) return;
            SendPeer(NetProtocol.BuildSnapshot(snap));
            SnapshotsSent++;
        }

        /// <summary>client 取最新快照（消费语义：取走即清）。</summary>
        public bool TryTakeSnapshot(out Snapshot snap)
        {
            snap = _latestSnapshot;
            _latestSnapshot = null;
            return snap != null;
        }

        public void RequestReplayFrame(int index)
        {
            if (IsHost || IsPractice || Phase != RoomPhase.Playing || index < 0 || index > 1201) return;
            SendPeer(NetProtocol.BuildRoundControl("replayrequest", RoundId, AuthorityEpoch, MatchId, null, index));
        }

        public void SendReplayFrame(int index, Snapshot snapshot)
        {
            if (!IsHost || IsPractice || snapshot == null || index < 0 || index > 1201) return;
            SendPeer(NetProtocol.BuildRoundControl("replayframe", RoundId, AuthorityEpoch, MatchId, snapshot, index));
        }

        public bool TryTakeReplaySnapshot(out Snapshot snapshot)
        {
            snapshot = _replaySnapshots.Count > 0 ? _replaySnapshots.Dequeue() : null;
            return snapshot != null;
        }

        public void ReplayReady()
        {
            if (IsHost || IsPractice || Phase != RoomPhase.Playing || _replayReady) return;
            _replayReady = true;
            _control.Track("replayready:" + RoundId,
                // One retained control entry lives until acknowledgement, peer loss or round reset.
                NetProtocol.BuildRoundControl("replayready", RoundId, AuthorityEpoch, MatchId), .5f, float.MaxValue);
        }

        /// <summary>事件队列：joined:seat:name / left:seat / roster / start / reject:* / hostlost / closed。</summary>
        public bool TryDequeue(out string evt)
        {
            if (_events.Count > 0)
            {
                evt = _events.Dequeue();
                return true;
            }

            evt = null;
            return false;
        }

        // ---- 主驱动 ----

        /// <summary>推进房间逻辑：收包、控制重试、roster/心跳节拍、超时判定。</summary>
        public void Pump(float dt)
        {
            if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt))
            {
                return;
            }

            Now += dt;
            _control.Pump(dt);

            if (Phase == RoomPhase.Idle || _socket == null)
            {
                return;
            }

            DrainSocket();

            if (Phase == RoomPhase.Closed)
            {
                if (_control.PendingCount == 0 || Now >= _closeDeadline)
                {
                    Cleanup();
                }

                return;
            }

            if (IsHost)
            {
                PumpHost(dt);
            }
            else
            {
                PumpClient(dt);
            }
        }

        private void PumpHost(float dt)
        {
            // client 心跳超时→席 Bot=true+roster+事件（幂等只标一次）。
            if (Seats[1].Occupied && !Seats[1].Bot && !_timeoutMarked && _hasPeer)
            {
                if (Now - _lastHeard[1] >= NetProtocol.HeartbeatTimeout)
                {
                    Seats[1].Bot = true;
                    Seats[1].CreditEligible = false;
                    _timeoutMarked = true;
                    ResetInputState(1);
                    _events.Enqueue("left:1");
                    BroadcastRoster();
                }
            }

            if (!_hasPeer)
            {
                return;
            }

            _pingTimer += dt;
            if (_pingTimer >= NetProtocol.HeartbeatInterval)
            {
                _pingTimer = 0f;
                SendPeer(IsPractice ? NetProtocol.BuildPing(Now) : NetProtocol.BuildRoundControl("ping", RoundId, AuthorityEpoch, MatchId));
            }

            // roster 房间内 1Hz 周期广播，丢包自愈。
            if (Seats[1].Occupied)
            {
                _rosterTimer += dt;
                if (_rosterTimer >= 1f)
                {
                    _rosterTimer = 0f;
                    BroadcastRoster();
                }
            }
        }

        private void PumpClient(float dt)
        {
            // join 5s 超时→reject:timeout。
            if (Phase == RoomPhase.Joining)
            {
                _joinTimer += dt;
                if (_joinTimer >= NetProtocol.JoinTimeout)
                {
                    _control.Ack("join:" + _joinSeq);
                    _events.Enqueue("reject:timeout");
                    Cleanup();
                    return;
                }
            }

            // host 心跳超时→hostlost。
            if (Phase == RoomPhase.InRoom || Phase == RoomPhase.Starting || Phase == RoomPhase.Playing)
            {
                if (Now - _lastHeardHost >= NetProtocol.HeartbeatTimeout)
                {
                    if (TryPromote()) return;
                    _events.Enqueue("hostlost");
                    Cleanup();
                    return;
                }
            }

            if (!_hasPeer)
            {
                return;
            }

            _pingTimer += dt;
            if (_pingTimer >= NetProtocol.HeartbeatInterval)
            {
                _pingTimer = 0f;
                SendPeer(IsPractice ? NetProtocol.BuildPing(Now) : NetProtocol.BuildRoundControl("ping", RoundId, AuthorityEpoch, MatchId));
            }
        }

        // ---- 收包处理 ----

        private void DrainSocket()
        {
            UdpDatagram datagram;
            // 收包处理可能触发 Cleanup（reject/close/hostlost），套接随时可能变 null。
            while (_socket != null && _socket.TryReceive(out datagram))
            {
                NetMessage message = NetProtocol.Parse(datagram.Payload);
                if (message == null)
                {
                    continue;
                }

                if (IsHost)
                {
                    HandleHostMessage(datagram, message);
                }
                else
                {
                    // client 只收 host 定向包。
                    if (_hasPeer &&
                        string.Equals(datagram.Address, _peerAddress, StringComparison.Ordinal) &&
                        datagram.Port == _peerPort)
                    {
                        if (!IsPractice && !IsCurrentRound(message) &&
                            message.Type != "start" && message.Type != "roster" && message.Type != "snap") continue;
                        HandleClientMessage(message);
                        if (IsPractice || IsCurrentRound(message)) _lastHeardHost = Now;
                    }
                }
            }
        }

        private void HandleHostMessage(UdpDatagram datagram, NetMessage message)
        {
            if (message.Type == "join")
            {
                HandleJoin(datagram, message);
                return;
            }

            // 非 join 只处理已建立对端的包。
            if (!_hasPeer ||
                !string.Equals(datagram.Address, _peerAddress, StringComparison.Ordinal) ||
                datagram.Port != _peerPort)
            {
                return;
            }

            if (!IsPractice && !IsCurrentRound(message)) return;
            _lastHeard[1] = Now;
            switch (message.Type)
            {
                case "in":
                    HandleInput(message);
                    break;
                case "ping":
                    SendPeer(IsPractice ? NetProtocol.BuildPong(Now) : NetProtocol.BuildRoundControl("pong", RoundId, AuthorityEpoch, MatchId));
                    break;
                case "leave":
                    HandleLeave(message);
                    break;
                case "startack":
                    _startAcked = true;
                    _control.AckPrefix("start:");
                    break;
                case "replayrequest":
                    int index = message.GetInt("index", -1);
                    if (index >= 0 && index <= 1201) _events.Enqueue("replayrequest:" + index);
                    break;
                case "replayready":
                    if (_control.FirstSeen("replayready:" + RoundId)) _events.Enqueue("replayready");
                    SendPeer(NetProtocol.BuildRoundControl("replayack", RoundId, AuthorityEpoch, MatchId));
                    break;
                case "pong":
                    break;
            }
        }

        private void HandleJoin(UdpDatagram datagram, NetMessage message)
        {
            int joinSeq = message.GetInt("seq", 0);
            string name = message.GetString("name", string.Empty);
            string token = message.GetString("token", string.Empty);

            // A departed seat cannot rejoin this production match using its old endpoint.
            if (!IsPractice && Phase == RoomPhase.Playing && Seats[1].Bot)
            {
                _socket.Send(datagram.Address, datagram.Port, NetProtocol.BuildReject(joinSeq, "ingame"));
                return;
            }

            // dup join（同对端已占席）→幂等重答同席。
            if (Seats[1].Occupied && _hasPeer &&
                string.Equals(datagram.Address, _peerAddress, StringComparison.Ordinal) &&
                datagram.Port == _peerPort)
            {
                _socket.Send(datagram.Address, datagram.Port, NetProtocol.BuildAccept(joinSeq, 1));
                BroadcastRoster();
                return;
            }

            if (Phase == RoomPhase.Playing || Phase == RoomPhase.Starting)
            {
                _socket.Send(datagram.Address, datagram.Port, NetProtocol.BuildReject(joinSeq, "ingame"));
                return;
            }

            if (Seats[1].Occupied)
            {
                _socket.Send(datagram.Address, datagram.Port, NetProtocol.BuildReject(joinSeq, "full"));
                return;
            }

            // M2 只做「token 非空」格式校验，真校验留 M3。
            if (string.IsNullOrEmpty(token))
            {
                _socket.Send(datagram.Address, datagram.Port, NetProtocol.BuildReject(joinSeq, "auth"));
                return;
            }

            _peerAddress = datagram.Address;
            _peerPort = datagram.Port;
            _hasPeer = true;
            Seats[1].Name = name;
            Seats[1].Occupied = true;
            Seats[1].Bot = false;
            Seats[1].CreditEligible = true;
            _timeoutMarked = false;
            _lastHeard[1] = Now;
            ResetInputState(1);
            Phase = RoomPhase.InRoom;
            SendPeer(NetProtocol.BuildAccept(joinSeq, 1));
            _events.Enqueue("joined:1:" + name);
            BroadcastRoster();
        }

        private void HandleInput(NetMessage message)
        {
            if (Phase != RoomPhase.Playing || !Seats[1].Occupied || Seats[1].Bot)
            {
                return;
            }

            if (!_startAcked)
            {
                // client 首帧 in 确认 start，停止重发。
                _startAcked = true;
                _control.AckPrefix("start:");
            }

            InputFrame frame = NetProtocol.DecodeInput(message);
            int seat = 1;
            // 按 seq 去旧：旧包/重包即弃。
            if (frame.Seq <= _lastInputSeq[seat])
            {
                return;
            }

            _lastInputSeq[seat] = frame.Seq;
            _latestInput[seat] = frame;
            _hasInput[seat] = true;
            _pendingPressed[seat] |= frame.Pressed;
            _pendingCycle[seat] |= frame.Cycle;
        }

        private void HandleLeave(NetMessage message)
        {
            int seat = message.GetInt("seat", 1);
            if (seat != 1 || !Seats[1].Occupied)
            {
                return; // 幂等：重复 leave no-op
            }

            if (Phase == RoomPhase.Playing || Phase == RoomPhase.Starting)
            {
                // 局内离开=掉线：bot 接管，游戏继续。
                if (!Seats[1].Bot)
                {
                    Seats[1].Bot = true;
                    Seats[1].CreditEligible = false;
                    _timeoutMarked = true;
                    ResetInputState(1);
                    _events.Enqueue("left:1");
                    BroadcastRoster();
                }

                return;
            }

            Seats[1].Name = string.Empty;
            Seats[1].Occupied = false;
            Seats[1].Bot = false;
            Seats[1].CreditEligible = false;
            _hasPeer = false;
            ResetInputState(1);
            Phase = RoomPhase.Listening;
            _events.Enqueue("left:1");
        }

        private void HandleClientMessage(NetMessage message)
        {
            switch (message.Type)
            {
                case "accept":
                    if (Phase == RoomPhase.Joining && message.GetInt("seq", -1) == _joinSeq)
                    {
                        _control.Ack("join:" + _joinSeq);
                        LocalSeat = message.GetInt("seat", 1);
                        Phase = RoomPhase.InRoom;
                        Seats[LocalSeat].Occupied = true;
                        _events.Enqueue("joined:" + LocalSeat + ":");
                    }
                    break;
                case "reject":
                    if (Phase == RoomPhase.Joining && message.GetInt("seq", -1) == _joinSeq)
                    {
                        _control.Ack("join:" + _joinSeq);
                        _events.Enqueue("reject:" + message.GetString("reason", "unknown"));
                        Cleanup();
                    }
                    break;
                case "roster":
                    HandleRoster(message);
                    break;
                case "start":
                    if (message.GetString("mode", "sandbox") == "shift")
                    {
                        AcceptShiftStart(message);
                        break;
                    }
                    // dup start 幂等 no-op。
                    if (Phase == RoomPhase.InRoom && _control.FirstSeen("start"))
                    {
                        Phase = RoomPhase.Starting;
                        _events.Enqueue("start");
                    }
                    break;
                case "snap":
                    if (message.GetString("mode", "sandbox") == "shift")
                    {
                        if (!AcceptShiftStart(message) || !IsCurrentRound(message)) break;
                        Snapshot received = NetProtocol.DecodeSnapshot(message);
                        if (received.Tick <= _lastSnapshotTick) break;
                        if (_lastAuthoritativeSnapshot != null && _lastAuthoritativeSnapshot.Ended && !received.Ended) break;
                        _lastSnapshotTick = received.Tick;
                        _latestSnapshot = received;
                        _lastAuthoritativeSnapshot = received;
                        Phase = RoomPhase.Playing;
                        break;
                    }
                    if (!IsPractice) break;
                    if (Phase == RoomPhase.Starting)
                    {
                        Phase = RoomPhase.Playing;
                    }

                    if (Phase == RoomPhase.Playing)
                    {
                        _latestSnapshot = NetProtocol.DecodeSnapshot(message);
                    }
                    break;
                case "ping":
                    SendPeer(IsPractice ? NetProtocol.BuildPong(Now) : NetProtocol.BuildRoundControl("pong", RoundId, AuthorityEpoch, MatchId));
                    break;
                case "handoff":
                    if (!IsCurrentRound(message) && !AcceptShiftStart(message)) break;
                    Dictionary<string, object> raw = MiniJson.GetObject(message.Data, "checkpoint");
                    if (raw != null)
                    {
                        Snapshot checkpoint = NetProtocol.DecodeSnapshot(new NetMessage { Type = "snap", Data = raw });
                        if (checkpoint.Tick > _lastSnapshotTick && checkpoint.RoundId == RoundId && checkpoint.AuthorityEpoch == AuthorityEpoch && checkpoint.MatchId == MatchId)
                        {
                            _lastAuthoritativeSnapshot = checkpoint;
                            _lastSnapshotTick = checkpoint.Tick;
                            Phase = RoomPhase.Playing;
                        }
                    }
                    if (!TryPromote()) { _events.Enqueue("hostlost"); Cleanup(); }
                    break;
                case "replayframe":
                    Dictionary<string, object> frame = MiniJson.GetObject(message.Data, "checkpoint");
                    int replayIndex = message.GetInt("index", -1);
                    if (frame != null && replayIndex >= 0 && replayIndex <= 1201 && _replaySnapshots.Count < 64)
                    {
                        Snapshot replay = NetProtocol.DecodeSnapshot(new NetMessage { Type = "snap", Data = frame });
                        replay.ReplayIndex = replayIndex;
                        _replaySnapshots.Enqueue(replay);
                    }
                    break;
                case "replayack":
                    _control.AckPrefix("replayready:");
                    break;
                case "close":
                    if (_control.FirstSeen("close"))
                    {
                        _events.Enqueue("closed");
                        Cleanup();
                    }
                    break;
            }
        }

        private void HandleRoster(NetMessage message)
        {
            if (message.GetString("mode", "sandbox") == "shift")
            {
                if (!AcceptShiftStart(message)) return;
            }
            else if (!IsPractice) return;
            int seq = message.GetInt("seq", -1);
            if (seq <= _lastRosterSeq)
            {
                return; // 去旧
            }

            _lastRosterSeq = seq;
            for (int i = 0; i < NetProtocol.SeatCount; i++)
            {
                string name;
                bool occupied;
                bool bot;
                NetProtocol.DecodeRosterSeat(message, i, out name, out occupied, out bot);
                Seats[i].Name = name;
                Seats[i].Occupied = occupied;
                Seats[i].Bot = bot;
                Seats[i].CreditEligible = occupied && !bot;
            }

            // roster 携带房间 phase，丢包自愈：见 starting/playing 即进局。
            string phase = message.GetString("phase", NetProtocol.PhaseLobby);
            if (Phase == RoomPhase.InRoom &&
                (phase == NetProtocol.PhaseStarting || phase == NetProtocol.PhasePlaying) &&
                _control.FirstSeen("start"))
            {
                Phase = RoomPhase.Starting;
                _events.Enqueue("start");
            }

            _events.Enqueue("roster");
        }

        private bool IsCurrentRound(NetMessage message)
        {
            return message.GetInt("round", 0) == RoundId &&
                message.GetInt("epoch", 0) == AuthorityEpoch &&
                message.GetString("match", string.Empty) == MatchId;
        }

        private bool AcceptShiftStart(NetMessage message)
        {
            if (message.GetString("mode", "sandbox") != "shift" || IsHost || (Phase != RoomPhase.InRoom && Phase != RoomPhase.Starting && Phase != RoomPhase.Playing)) return false;
            int round = message.GetInt("round", 0);
            int epoch = message.GetInt("epoch", 0);
            string match = message.GetString("match", string.Empty);
            if (round < 1 || epoch < 1 || string.IsNullOrEmpty(match)) return false;
            if (!IsPractice && (epoch != AuthorityEpoch || round < RoundId)) return false;
            if (!IsPractice && round == RoundId)
            {
                if (match != MatchId) return false;
                if (message.Type == "start") SendPeer(NetProtocol.BuildRoundControl("startack", RoundId, AuthorityEpoch, MatchId));
                return true;
            }
            bool retry = !IsPractice;
            IsPractice = false;
            RoundId = round;
            AuthorityEpoch = epoch;
            MatchId = match;
            int split = match.LastIndexOf(':');
            _roomId = split < 0 ? match : match.Substring(0, split);
            ResetRoundState();
            Phase = RoomPhase.Starting;
            _events.Enqueue(retry ? "retry" : "start");
            SendPeer(NetProtocol.BuildRoundControl("startack", RoundId, AuthorityEpoch, MatchId));
            return true;
        }

        private bool TryPromote()
        {
            if (IsPractice || Phase != RoomPhase.Playing || _lastAuthoritativeSnapshot == null) return false;
            MigrationSnapshot = _lastAuthoritativeSnapshot;
            IsHost = true;
            AuthorityEpoch++;
            Seats[0].Bot = true;
            Seats[0].CreditEligible = false;
            Seats[1].CreditEligible = true;
            _hasPeer = false;
            _control.Reset();
            ResetAllInputState();
            _latestSnapshot = null;
            _events.Enqueue("left:0");
            _events.Enqueue("migrated");
            return true;
        }

        // ---- 内部工具 ----

        private void BroadcastRoster()
        {
            if (!_hasPeer)
            {
                return;
            }

            string[] names = new string[NetProtocol.SeatCount];
            bool[] occupied = new bool[NetProtocol.SeatCount];
            bool[] bots = new bool[NetProtocol.SeatCount];
            for (int i = 0; i < NetProtocol.SeatCount; i++)
            {
                names[i] = Seats[i].Name;
                occupied[i] = Seats[i].Occupied;
                bots[i] = Seats[i].Bot;
            }

            _rosterSeq++;
            string phase = Phase == RoomPhase.Playing ? NetProtocol.PhasePlaying :
                Phase == RoomPhase.Starting ? NetProtocol.PhaseStarting : NetProtocol.PhaseLobby;
            SendPeer(NetProtocol.BuildRoster(_rosterSeq, phase, names, occupied, bots, IsPractice, RoundId, AuthorityEpoch, MatchId));
        }

        private IUdpSocket CreateSocket(int port)
        {
            if (SocketFactory != null)
            {
                return SocketFactory(port);
            }

            try
            {
                return new UdpSocketReal(port, false);
            }
            catch (System.Net.Sockets.SocketException)
            {
                return null;
            }
        }

        private void SendPeer(string payload)
        {
            if (_socket != null && _hasPeer && payload != null)
            {
                _socket.Send(_peerAddress, _peerPort, payload);
            }
        }

        private void Cleanup()
        {
            if (_socket != null)
            {
                _socket.Close();
                _socket = null;
            }

            _control.Reset();
            _hasPeer = false;
            _peerAddress = null;
            _peerPort = 0;
            IsHost = false;
            LocalSeat = -1;
            RoomName = string.Empty;
            ResetRoundState();
            IsPractice = true;
            RoundId = 0;
            AuthorityEpoch = 0;
            MatchId = string.Empty;
            _roomId = string.Empty;
            _lastRosterSeq = -1;
            _timeoutMarked = false;
            _startAcked = false;
            ResetAllInputState();
            for (int i = 0; i < NetProtocol.SeatCount; i++)
            {
                Seats[i].Name = string.Empty;
                Seats[i].Occupied = false;
                Seats[i].Bot = false;
                Seats[i].CreditEligible = false;
            }

            Phase = RoomPhase.Idle;
        }

        private void ResetAllInputState()
        {
            for (int i = 0; i < NetProtocol.SeatCount; i++)
            {
                ResetInputState(i);
            }
        }

        private void ResetInputState(int seat)
        {
            _lastInputSeq[seat] = -1;
            _latestInput[seat] = new InputFrame();
            _hasInput[seat] = false;
            _pendingPressed[seat] = false;
            _pendingCycle[seat] = false;
        }
    }
}
