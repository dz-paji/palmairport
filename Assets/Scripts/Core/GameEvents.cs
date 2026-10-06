using System;
using System.Collections.Generic;

namespace IslandAirport
{
    /// <summary>一条埋点事件记录。</summary>
    public class GameEventRecord
    {
        public string Name = string.Empty;
        public Dictionary<string, object> Params;
        public float Ts;
        public string MatchId = string.Empty;
        public string ActorId = string.Empty;
        public string PairId = string.Empty;
        public string DedupeKey = string.Empty;

        /// <summary>序列化为 JSONL 单行。</summary>
        public string ToJson()
        {
            Dictionary<string, object> obj = new Dictionary<string, object>();
            obj["name"] = Name;
            obj["ts"] = (double)Ts;
            obj["matchId"] = MatchId;
            obj["actorId"] = ActorId;
            obj["pairId"] = PairId;
            obj["dedupeKey"] = DedupeKey;
            obj["params"] = Params ?? new Dictionary<string, object>();
            return MiniJson.Serialize(obj);
        }

        /// <summary>解析一行 JSONL；非法输入返回 null。</summary>
        public static GameEventRecord FromJson(string line)
        {
            Dictionary<string, object> obj;
            try
            {
                obj = MiniJson.ParseObject(line);
            }
            catch (FormatException)
            {
                return null;
            }
            catch (ArgumentNullException)
            {
                return null;
            }

            GameEventRecord record = new GameEventRecord();
            record.Name = MiniJson.GetString(obj, "name", string.Empty);
            record.Ts = (float)MiniJson.GetNumber(obj, "ts", 0);
            record.MatchId = MiniJson.GetString(obj, "matchId", string.Empty);
            record.ActorId = MiniJson.GetString(obj, "actorId", string.Empty);
            record.PairId = MiniJson.GetString(obj, "pairId", string.Empty);
            record.DedupeKey = MiniJson.GetString(obj, "dedupeKey", string.Empty);
            record.Params = MiniJson.GetObject(obj, "params");
            return record;
        }
    }

    /// <summary>
    /// 埋点事件队列：{name,params,ts,matchId,actorId,pairId,dedupeKey} + 去重 + JSONL 落盘。
    /// IO 委托注入（生产写文件，测试给内存实现）；ts 取内部 Pump 累计时钟，禁墙钟。
    /// 去重语义：dedupeKey 非空时同 key 只留首条。
    /// </summary>
    public class GameEventQueue
    {
        // E25 埋点事件清单。
        public const string InviteCreated = "invite_created";
        public const string SharePanelOpened = "share_panel_opened";
        public const string LoginStarted = "login_started";
        public const string LoginOk = "login_ok";
        public const string LoginFail = "login_fail";
        public const string AgeBlocked = "age_blocked";
        public const string RoomCreated = "room_created";
        public const string RoomJoined = "room_joined";
        public const string RoomRejected = "room_rejected";
        public const string RoomStarted = "room_started";
        public const string RoomLeft = "room_left";
        public const string SandboxStarted = "sandbox_started";
        public const string ShiftStarted = "shift_started";
        public const string FirstShiftCompleted = "first_shift_completed";
        public const string WrongDelivery = "wrong_delivery";
        public const string TaskFailed = "task_failed";
        // M3.3 燃油新流程埋点（nozzle_*/valve_*/spill_* 与 ShiftEventTypes 同名；
        // FuelDelivered 是 delivered:kind=Fuel 的燃油通道名）。
        public const string NozzleTaken = "nozzle_taken";
        public const string NozzleAttached = "nozzle_attached";
        public const string NozzleDetached = "nozzle_detached";
        public const string NozzleReturned = "nozzle_returned";
        // M3.3r 车载油管埋点（与 ShiftEventTypes.Hose* 同名）。
        public const string HoseTaken = "hose_taken";
        public const string HoseAttached = "hose_attached";
        public const string HoseDetached = "hose_detached";
        public const string HoseReturned = "hose_returned";
        public const string ValveOpened = "valve_opened";
        public const string ValveClosed = "valve_closed";
        public const string SpillStarted = "spill_started";
        public const string SpillCleared = "spill_cleared";
        public const string FuelDelivered = "fuel_delivered";
        public const string RetryClicked = "retry_clicked";
        public const string SoloInviteIntent = "solo_invite_intent";

        private readonly Queue<GameEventRecord> _pending = new Queue<GameEventRecord>();
        private readonly HashSet<string> _seen = new HashSet<string>();
        private readonly Action<string> _appendLine;
        private readonly Func<IEnumerable<string>> _readLines;

        /// <summary>appendLine：落盘写一行；readLines：读回全部行。均可为 null（纯内存）。</summary>
        public GameEventQueue(Action<string> appendLine, Func<IEnumerable<string>> readLines)
        {
            _appendLine = appendLine;
            _readLines = readLines;
        }

        /// <summary>纯内存构造（不落盘）。</summary>
        public GameEventQueue()
            : this(null, null)
        {
        }

        /// <summary>内部时钟（Tick 累计）。</summary>
        public float Now { get; private set; }

        public string MatchId = string.Empty;
        public string ActorId = string.Empty;
        public string PairId = string.Empty;

        public int PendingCount
        {
            get { return _pending.Count; }
        }

        /// <summary>因去重丢弃的事件数。</summary>
        public int DroppedCount { get; private set; }

        /// <summary>推进内部时钟。</summary>
        public void Tick(float dt)
        {
            if (dt > 0f && !float.IsNaN(dt) && !float.IsInfinity(dt))
            {
                Now += dt;
            }
        }

        /// <summary>入队一条事件。dedupeKey 非空时同 key 只留首条，重复返回 false。</summary>
        public bool Enqueue(string name, string dedupeKey, IDictionary<string, object> eventParams)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(dedupeKey) && !_seen.Add(dedupeKey))
            {
                DroppedCount++;
                return false;
            }

            GameEventRecord record = new GameEventRecord();
            record.Name = name;
            record.Ts = Now;
            record.MatchId = MatchId;
            record.ActorId = ActorId;
            record.PairId = PairId;
            record.DedupeKey = dedupeKey ?? string.Empty;
            record.Params = eventParams != null
                ? new Dictionary<string, object>(eventParams)
                : new Dictionary<string, object>();
            _pending.Enqueue(record);
            return true;
        }

        /// <summary>入队一条无参数事件。</summary>
        public bool Enqueue(string name)
        {
            return Enqueue(name, null, null);
        }

        /// <summary>把队列写往落盘委托，返回写出行数。</summary>
        public int Flush()
        {
            int written = 0;
            while (_pending.Count > 0)
            {
                GameEventRecord record = _pending.Dequeue();
                if (_appendLine != null)
                {
                    _appendLine(record.ToJson());
                }

                written++;
            }

            return written;
        }

        /// <summary>读回全部落盘行并解析（JSONL 往返）。</summary>
        public List<GameEventRecord> LoadAll()
        {
            List<GameEventRecord> list = new List<GameEventRecord>();
            if (_readLines == null)
            {
                return list;
            }

            foreach (string line in _readLines())
            {
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                GameEventRecord record = GameEventRecord.FromJson(line);
                if (record != null)
                {
                    list.Add(record);
                }
            }

            return list;
        }
    }
}
