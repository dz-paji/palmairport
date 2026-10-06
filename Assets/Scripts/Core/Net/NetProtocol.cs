using System;
using System.Collections.Generic;
using System.Globalization;

namespace IslandAirport
{
    /// <summary>一名玩家的一帧完整输入意图（30Hz 全量上行，天然抗丢包）。</summary>
    public struct InputFrame
    {
        public int Seq;
        public int RoundId;
        public int AuthorityEpoch;
        public string MatchId;
        public float Mx;
        public float My;
        public bool Pressed;
        public bool Held;
        public bool Cycle;
    }

    /// <summary>快照里一名角色的状态。HeldCart=-1 表示空手。</summary>
    public struct CrewSnap
    {
        public float X;
        public float Z;
        public float RotY;
        public int HeldCart;
        /// <summary>该席当前目标航班在 Flights 块中的下标（M3.1 增补键，client 目标 chip 用；
        /// 旧解码端忽略未知键，缺省 -1 = 自动跟随首个服务中航班）。</summary>
        public int Sel;
        /// <summary>权威移动判定被旅客/服务车辆阻挡；client 镜像同一事实显示避让提示。</summary>
        public bool BlockedByTraffic;
    }

    /// <summary>快照里一辆车的状态。OwnerSeat=-1 表示无人驾驶。</summary>
    public struct CartSnap
    {
        public float X;
        public float Z;
        public float RotY;
        public int OwnerSeat;
        public int Cargo;
    }

    /// <summary>权威端广播的一帧世界快照（15Hz，定长数组按下标对齐稳定 ID）。
    /// Ver=3 包括任务恢复数据、局身份、权威代次、有限结算与有界回放索引。</summary>
    public class Snapshot
    {
        public int Tick;
        public int RoundId;
        public int AuthorityEpoch;
        public string MatchId = string.Empty;
        public bool IsPractice = true;
        public bool Ended;
        public int ReplayIndex = -1;
        public int ReplayCount;
        public int[] ReplayMissing = new int[0];
        public bool ReplayIncomplete;
        public float ReplayTime;
        public bool ReplayStarted;
        public int CompletedFlights;
        public int MissedFlights;
        public int Stars;
        public int CompletedTasks;
        public int Deliveries;
        public int TrafficStops;
        public CrewSnap[] Crew = new CrewSnap[NetProtocol.SeatCount];
        public CartSnap[] Carts = new CartSnap[NetProtocol.CartCount];
        /// <summary>每座交互钮文案，client 交互钮文案的唯一来源。</summary>
        public string[] Labels = new string[NetProtocol.SeatCount];
        public string Toast = string.Empty;
        public int ToastSeq;
        /// <summary>任务域镜像（餐食/燃油/车辆货物/登机/旅客/失败集合/机位就绪），永不为 null。</summary>
        public ShiftSnap Shift = new ShiftSnap();
        /// <summary>航班镜像（id/status/stand/progress[4]/arrival/deadline）。</summary>
        public FlightSnap[] Flights = new FlightSnap[0];
        public int Score;
        public float Elapsed;
    }

    /// <summary>局域网发现表里的一条房间记录。LastSeen 为本地 Pump 累计时钟。</summary>
    public class RoomInfo
    {
        public string Name = string.Empty;
        public string HostName = string.Empty;
        public string Address = string.Empty;
        /// <summary>lobby / playing。</summary>
        public string State = "lobby";
        public int Port;
        public int Seats = NetProtocol.SeatCount;
        public int Taken;
        public float LastSeen;
    }

    /// <summary>解析后的一条协议消息：Type 取消息名，Data 为原始字段。</summary>
    public class NetMessage
    {
        public string Type = string.Empty;
        public Dictionary<string, object> Data = new Dictionary<string, object>();

        public string GetString(string key, string fallback)
        {
            return MiniJson.GetString(Data, key, fallback);
        }

        public int GetInt(string key, int fallback)
        {
            return (int)MiniJson.GetNumber(Data, key, fallback);
        }

        public float GetFloat(string key, float fallback)
        {
            return (float)MiniJson.GetNumber(Data, key, fallback);
        }

        public bool GetBool(string key, bool fallback)
        {
            return MiniJson.GetBool(Data, key, fallback);
        }
    }

    /// <summary>
    /// 联机协议常量与消息编解码。消息表：
    /// beacon / join / accept / reject(full|ingame|auth|timeout) / roster(含房间 phase)
    /// / start(sandbox) / in(输入上行) / snap(快照下行) / leave / ping / pong / close。
    /// 数据报语义：一条消息 = 一个 JSON 文本包，天然分帧，无编帧层。
    /// </summary>
    public static class NetProtocol
    {
        /// <summary>Ver=3：有限班岗身份、恢复检查点和回放补帧；与旧版设备不互通。</summary>
        public const int Ver = 3;
        public const int BeaconPort = 47777;
        public const int SessionPort = 47778;
        public const float HeartbeatInterval = 1f;
        public const float HeartbeatTimeout = 5f;
        public const float SnapshotPeriod = 1f / 15f;
        public const float InputPeriod = 1f / 30f;
        public const float ControlRetry = 1f;
        public const float JoinTimeout = 5f;
        public const int SeatCount = 2;
        public const int CartCount = 3;

        /// <summary>房间 phase：idle / lobby（Listening 可加入）/ starting / playing。</summary>
        public const string PhaseIdle = "idle";
        public const string PhaseLobby = "lobby";
        public const string PhaseStarting = "starting";
        public const string PhasePlaying = "playing";

        // ---- 消息构建 ----

        private static Dictionary<string, object> Base(string type)
        {
            Dictionary<string, object> obj = new Dictionary<string, object>();
            obj["t"] = type;
            obj["v"] = (double)Ver;
            return obj;
        }

        public static string BuildBeacon(RoomInfo room)
        {
            Dictionary<string, object> obj = Base("beacon");
            obj["name"] = room.Name;
            obj["host"] = room.HostName;
            obj["state"] = room.State;
            obj["port"] = (double)room.Port;
            obj["seats"] = (double)room.Seats;
            obj["taken"] = (double)room.Taken;
            return MiniJson.Serialize(obj);
        }

        public static string BuildJoin(int seq, string name, string token)
        {
            Dictionary<string, object> obj = Base("join");
            obj["seq"] = (double)seq;
            obj["name"] = name ?? string.Empty;
            obj["token"] = token ?? string.Empty;
            return MiniJson.Serialize(obj);
        }

        public static string BuildAccept(int seq, int seat)
        {
            Dictionary<string, object> obj = Base("accept");
            obj["seq"] = (double)seq;
            obj["seat"] = (double)seat;
            return MiniJson.Serialize(obj);
        }

        public static string BuildReject(int seq, string reason)
        {
            Dictionary<string, object> obj = Base("reject");
            obj["seq"] = (double)seq;
            obj["reason"] = reason ?? string.Empty;
            return MiniJson.Serialize(obj);
        }

        /// <summary>roster 周期广播：房间 phase + 每席占用/名字/是否 bot，丢包自愈。</summary>
        public static string BuildRoster(int seq, string phase, string[] names, bool[] occupied, bool[] bots, bool isPractice = true, int roundId = 0, int authorityEpoch = 0, string matchId = "")
        {
            Dictionary<string, object> obj = Base("roster");
            obj["seq"] = (double)seq;
            obj["phase"] = phase ?? PhaseLobby;
            AddRound(obj, isPractice, roundId, authorityEpoch, matchId);
            List<object> seats = new List<object>();
            for (int i = 0; i < SeatCount; i++)
            {
                Dictionary<string, object> seat = new Dictionary<string, object>();
                seat["name"] = names != null && i < names.Length ? names[i] ?? string.Empty : string.Empty;
                seat["occ"] = occupied != null && i < occupied.Length && occupied[i];
                seat["bot"] = bots != null && i < bots.Length && bots[i];
                seats.Add(seat);
            }

            obj["list"] = seats;
            return MiniJson.Serialize(obj);
        }

        public static string BuildStart(int seq, bool isPractice = true, int roundId = 0, int authorityEpoch = 0, string matchId = "")
        {
            Dictionary<string, object> obj = Base("start");
            obj["seq"] = (double)seq;
            AddRound(obj, isPractice, roundId, authorityEpoch, matchId);
            return MiniJson.Serialize(obj);
        }

        public static string BuildInput(InputFrame frame)
        {
            Dictionary<string, object> obj = Base("in");
            obj["seq"] = (double)frame.Seq;
            AddRound(obj, true, frame.RoundId, frame.AuthorityEpoch, frame.MatchId);
            obj["mx"] = (double)frame.Mx;
            obj["my"] = (double)frame.My;
            obj["p"] = frame.Pressed;
            obj["h"] = frame.Held;
            obj["c"] = frame.Cycle;
            return MiniJson.Serialize(obj);
        }

        public static string BuildSnapshot(Snapshot snap)
        {
            Dictionary<string, object> obj = Base("snap");
            obj["tick"] = (double)snap.Tick;
            AddRound(obj, snap.IsPractice, snap.RoundId, snap.AuthorityEpoch, snap.MatchId);
            obj["ended"] = snap.Ended;
            obj["replayIndex"] = (double)snap.ReplayIndex;
            obj["replayCount"] = (double)snap.ReplayCount;
            obj["replayMissing"] = EncodeInts(snap.ReplayMissing);
            obj["replayIncomplete"] = snap.ReplayIncomplete;
            obj["replayTime"] = (double)snap.ReplayTime;
            obj["replayStarted"] = snap.ReplayStarted;
            obj["completed"] = (double)snap.CompletedFlights;
            obj["missed"] = (double)snap.MissedFlights;
            obj["stars"] = (double)snap.Stars;
            obj["tasks"] = (double)snap.CompletedTasks;
            obj["deliveries"] = (double)snap.Deliveries;
            obj["stops"] = (double)snap.TrafficStops;
            List<object> crew = new List<object>();
            for (int i = 0; i < snap.Crew.Length; i++)
            {
                CrewSnap c = snap.Crew[i];
                Dictionary<string, object> entry = new Dictionary<string, object>();
                entry["x"] = (double)c.X;
                entry["z"] = (double)c.Z;
                entry["r"] = (double)c.RotY;
                entry["cart"] = (double)c.HeldCart;
                entry["sel"] = (double)c.Sel;
                entry["blocked"] = c.BlockedByTraffic;
                crew.Add(entry);
            }

            obj["crew"] = crew;
            List<object> carts = new List<object>();
            for (int i = 0; i < snap.Carts.Length; i++)
            {
                CartSnap c = snap.Carts[i];
                Dictionary<string, object> entry = new Dictionary<string, object>();
                entry["x"] = (double)c.X;
                entry["z"] = (double)c.Z;
                entry["r"] = (double)c.RotY;
                entry["own"] = (double)c.OwnerSeat;
                entry["cargo"] = (double)c.Cargo;
                carts.Add(entry);
            }

            obj["carts"] = carts;
            List<object> labels = new List<object>();
            for (int i = 0; i < snap.Labels.Length; i++)
            {
                labels.Add(snap.Labels[i] ?? string.Empty);
            }

            obj["labels"] = labels;
            obj["toast"] = snap.Toast ?? string.Empty;
            obj["tseq"] = (double)snap.ToastSeq;
            obj["shift"] = EncodeShift(snap.Shift);
            List<object> flights = new List<object>();
            if (snap.Flights != null)
            {
                for (int i = 0; i < snap.Flights.Length; i++)
                {
                    FlightSnap f = snap.Flights[i];
                    if (f == null)
                    {
                        continue;
                    }

                    Dictionary<string, object> entry = new Dictionary<string, object>();
                    entry["id"] = f.Id ?? string.Empty;
                    entry["st"] = (double)f.Status;
                    entry["stand"] = (double)f.Stand;
                    List<object> progress = new List<object>();
                    for (int k = 0; k < 4; k++)
                    {
                        progress.Add((double)(f.Progress != null && k < f.Progress.Length ? f.Progress[k] : 0f));
                    }

                    entry["p"] = progress;
                    entry["arr"] = (double)f.Arrival;
                    entry["dl"] = (double)f.Deadline;
                    entry["ab"] = f.ArrivalBagsReturned;
                    entry["assigned"] = (double)f.PlaneAssignedAt;
                    flights.Add(entry);
                }
            }

            obj["flights"] = flights;
            obj["score"] = (double)snap.Score;
            obj["elapsed"] = (double)snap.Elapsed;
            return MiniJson.Serialize(obj);
        }

        static Dictionary<string, object> EncodeShift(ShiftSnap shift)
        {
            Dictionary<string, object> obj = new Dictionary<string, object>();
            if (shift == null)
            {
                shift = new ShiftSnap();
            }

            obj["nextEvent"] = (double)shift.NextEventSequence;
            obj["nextSpill"] = (double)shift.NextSpillId;
            obj["atStation"] = shift.FuelTruckAtStation;
            obj["drivers"] = EncodeInts(shift.CartDrivers);
            obj["gateSeats"] = EncodeInts(shift.GateSeats);
            obj["humanTasks"] = EncodeInts(shift.HumanTaskCounts);
            obj["meal"] = (double)shift.MealPhase;
            obj["mealp"] = (double)shift.MealProgress;
            obj["tank"] = (double)shift.FuelTruckTank;
            obj["noz"] = (double)shift.NozzleState;
            obj["nozs"] = (double)shift.NozzleSeat;
            obj["hose"] = (double)shift.HoseState;
            obj["hoses"] = (double)shift.HoseSeat;
            obj["hosef"] = shift.HoseFlightId ?? string.Empty;
            obj["valve"] = shift.ValveOpen;
            obj["ovs"] = (double)shift.StationOverfill;
            obj["ovh"] = (double)shift.HoseOverfill;
            obj["spillon"] = shift.Spilling;
            obj["hspill"] = shift.HoseSpilling;
            List<object> spills = new List<object>();
            if (shift.Spills != null)
            {
                for (int i = 0; i < shift.Spills.Length; i++)
                {
                    SpillSnap s = shift.Spills[i];
                    if (s == null) continue;
                    Dictionary<string, object> entry = new Dictionary<string, object>();
                    entry["id"] = (double)s.Id;
                    entry["x"] = (double)s.X;
                    entry["z"] = (double)s.Z;
                    entry["r"] = (double)s.Radius;
                    entry["w"] = (double)s.CleanWork;
                    spills.Add(entry);
                }
            }

            obj["spills"] = spills;
            List<object> carts = new List<object>();
            for (int i = 0; i < shift.Carts.Length; i++)
            {
                ShiftCartSnap c = shift.Carts[i] ?? new ShiftCartSnap();
                Dictionary<string, object> entry = new Dictionary<string, object>();
                entry["f"] = c.FlightId ?? string.Empty;
                entry["arr"] = c.Arrival;
                entry["w"] = (double)c.DeliverWork;
                entry["dw"] = c.DeliverFlightId ?? string.Empty;
                carts.Add(entry);
            }

            obj["carts"] = carts;
            List<object> boarding = new List<object>();
            if (shift.Boarding != null)
            {
                for (int i = 0; i < shift.Boarding.Length; i++)
                {
                    boarding.Add(shift.Boarding[i] ?? string.Empty);
                }
            }

            obj["board"] = boarding;
            List<object> pax = new List<object>();
            if (shift.Passengers != null)
            {
                for (int i = 0; i < shift.Passengers.Length; i++)
                {
                    PassengerSnap p = shift.Passengers[i];
                    if (p == null)
                    {
                        continue;
                    }

                    Dictionary<string, object> entry = new Dictionary<string, object>();
                    entry["f"] = p.FlightId ?? string.Empty;
                    entry["s"] = (double)p.Seq;
                    entry["stand"] = (double)p.Stand;
                    entry["w"] = (double)p.Waypoint;
                    entry["p"] = (double)p.Progress01;
                    entry["d"] = (double)p.Delay;
                    entry["rel"] = p.Released;
                    entry["seat"] = (double)p.Seat;
                    pax.Add(entry);
                }
            }

            obj["pax"] = pax;
            List<object> failed = new List<object>();
            if (shift.Failed != null)
            {
                for (int i = 0; i < shift.Failed.Length; i++)
                {
                    failed.Add(shift.Failed[i] ?? string.Empty);
                }
            }

            obj["fail"] = failed;
            List<object> events = new List<object>();
            if (shift.Events != null)
            {
                for (int i = 0; i < shift.Events.Length; i++)
                {
                    ShiftEventSnap item = shift.Events[i];
                    if (item == null) continue;
                    Dictionary<string, object> entry = new Dictionary<string, object>();
                    entry["seq"] = (double)item.Sequence;
                    entry["time"] = (double)item.Time;
                    entry["type"] = item.Type ?? string.Empty;
                    entry["f"] = item.FlightId ?? string.Empty;
                    entry["rf"] = item.RelatedFlightId ?? string.Empty;
                    entry["kind"] = (double)item.Kind;
                    entry["seat"] = (double)item.Seat;
                    entry["text"] = item.Text ?? string.Empty;
                    entry["bell"] = item.Bell;
                    events.Add(entry);
                }
            }

            obj["events"] = events;
            List<object> ready = new List<object>();
            for (int i = 0; i < shift.StandReady.Length; i++)
            {
                ready.Add(shift.StandReady[i]);
            }

            obj["ready"] = ready;
            return obj;
        }

        static List<object> EncodeInts(int[] values)
        {
            List<object> list = new List<object>();
            if (values != null) for (int i = 0; i < values.Length; i++) list.Add((double)values[i]);
            return list;
        }

        static int[] DecodeInts(Dictionary<string, object> obj, string key, int[] fallback)
        {
            List<object> list = MiniJson.GetArray(obj, key);
            if (list == null) return fallback;
            int[] result = new int[list.Count];
            for (int i = 0; i < result.Length; i++) if (list[i] is double) result[i] = (int)(double)list[i];
            return result;
        }

        static void AddRound(Dictionary<string, object> obj, bool practice, int round, int epoch, string match)
        {
            obj["mode"] = practice ? "sandbox" : "shift";
            obj["round"] = (double)round;
            obj["epoch"] = (double)epoch;
            obj["match"] = match ?? string.Empty;
        }

        public static string BuildRoundControl(string type, int round, int epoch, string match, Snapshot checkpoint = null, int index = -1)
        {
            Dictionary<string, object> obj = Base(type);
            AddRound(obj, false, round, epoch, match);
            obj["index"] = (double)index;
            if (checkpoint != null) obj["checkpoint"] = MiniJson.ParseObject(BuildSnapshot(checkpoint));
            return MiniJson.Serialize(obj);
        }

        public static string BuildLeave(int seq, int seat, int round = 0, int epoch = 0, string match = "")
        {
            Dictionary<string, object> obj = Base("leave");
            obj["seq"] = (double)seq;
            obj["seat"] = (double)seat;
            AddRound(obj, true, round, epoch, match);
            return MiniJson.Serialize(obj);
        }

        public static string BuildPing(float clock)
        {
            Dictionary<string, object> obj = Base("ping");
            obj["clk"] = (double)clock;
            return MiniJson.Serialize(obj);
        }

        public static string BuildPong(float clock)
        {
            Dictionary<string, object> obj = Base("pong");
            obj["clk"] = (double)clock;
            return MiniJson.Serialize(obj);
        }

        public static string BuildClose(int seq)
        {
            Dictionary<string, object> obj = Base("close");
            obj["seq"] = (double)seq;
            return MiniJson.Serialize(obj);
        }

        // ---- 消息解析 ----

        /// <summary>解析一条消息；版本不符或缺类型返回 null。</summary>
        public static NetMessage Parse(string text)
        {
            Dictionary<string, object> obj;
            try
            {
                obj = MiniJson.ParseObject(text);
            }
            catch (FormatException)
            {
                return null;
            }

            if ((int)MiniJson.GetNumber(obj, "v", -1) != Ver)
            {
                return null;
            }

            string type = MiniJson.GetString(obj, "t", null);
            if (type == null)
            {
                return null;
            }

            NetMessage message = new NetMessage();
            message.Type = type;
            message.Data = obj;
            return message;
        }

        /// <summary>从 in 消息解码输入帧。</summary>
        public static InputFrame DecodeInput(NetMessage message)
        {
            InputFrame frame = new InputFrame();
            frame.Seq = message.GetInt("seq", 0);
            frame.RoundId = message.GetInt("round", 0);
            frame.AuthorityEpoch = message.GetInt("epoch", 0);
            frame.MatchId = message.GetString("match", string.Empty);
            frame.Mx = message.GetFloat("mx", 0f);
            frame.My = message.GetFloat("my", 0f);
            frame.Pressed = message.GetBool("p", false);
            frame.Held = message.GetBool("h", false);
            frame.Cycle = message.GetBool("c", false);
            return frame;
        }

        /// <summary>从 snap 消息解码世界快照。</summary>
        public static Snapshot DecodeSnapshot(NetMessage message)
        {
            Snapshot snap = new Snapshot();
            snap.Tick = message.GetInt("tick", 0);
            snap.RoundId = message.GetInt("round", 0);
            snap.AuthorityEpoch = message.GetInt("epoch", 0);
            snap.MatchId = message.GetString("match", string.Empty);
            snap.IsPractice = message.GetString("mode", "sandbox") != "shift";
            snap.Ended = message.GetBool("ended", false);
            snap.ReplayIndex = message.GetInt("replayIndex", -1);
            snap.ReplayCount = message.GetInt("replayCount", 0);
            snap.ReplayMissing = DecodeInts(message.Data, "replayMissing", new int[0]);
            snap.ReplayIncomplete = message.GetBool("replayIncomplete", false);
            snap.ReplayTime = message.GetFloat("replayTime", 0f);
            snap.ReplayStarted = message.GetBool("replayStarted", false);
            snap.CompletedFlights = message.GetInt("completed", 0);
            snap.MissedFlights = message.GetInt("missed", 0);
            snap.Stars = message.GetInt("stars", 0);
            snap.CompletedTasks = message.GetInt("tasks", 0);
            snap.Deliveries = message.GetInt("deliveries", 0);
            snap.TrafficStops = message.GetInt("stops", 0);
            List<object> crew = MiniJson.GetArray(message.Data, "crew");
            if (crew != null)
            {
                for (int i = 0; i < snap.Crew.Length && i < crew.Count; i++)
                {
                    Dictionary<string, object> entry = crew[i] as Dictionary<string, object>;
                    if (entry == null)
                    {
                        continue;
                    }

                    snap.Crew[i].X = (float)MiniJson.GetNumber(entry, "x", 0);
                    snap.Crew[i].Z = (float)MiniJson.GetNumber(entry, "z", 0);
                    snap.Crew[i].RotY = (float)MiniJson.GetNumber(entry, "r", 0);
                    snap.Crew[i].HeldCart = (int)MiniJson.GetNumber(entry, "cart", -1);
                    snap.Crew[i].Sel = (int)MiniJson.GetNumber(entry, "sel", -1);
                    snap.Crew[i].BlockedByTraffic = MiniJson.GetBool(entry, "blocked", false);
                }
            }

            List<object> carts = MiniJson.GetArray(message.Data, "carts");
            if (carts != null)
            {
                for (int i = 0; i < snap.Carts.Length && i < carts.Count; i++)
                {
                    Dictionary<string, object> entry = carts[i] as Dictionary<string, object>;
                    if (entry == null)
                    {
                        continue;
                    }

                    snap.Carts[i].X = (float)MiniJson.GetNumber(entry, "x", 0);
                    snap.Carts[i].Z = (float)MiniJson.GetNumber(entry, "z", 0);
                    snap.Carts[i].RotY = (float)MiniJson.GetNumber(entry, "r", 0);
                    snap.Carts[i].OwnerSeat = (int)MiniJson.GetNumber(entry, "own", -1);
                    snap.Carts[i].Cargo = (int)MiniJson.GetNumber(entry, "cargo", 0);
                }
            }

            List<object> labels = MiniJson.GetArray(message.Data, "labels");
            if (labels != null)
            {
                for (int i = 0; i < snap.Labels.Length && i < labels.Count; i++)
                {
                    snap.Labels[i] = labels[i] as string ?? string.Empty;
                }
            }

            snap.Toast = message.GetString("toast", string.Empty);
            snap.ToastSeq = message.GetInt("tseq", 0);
            snap.Shift = DecodeShift(MiniJson.GetObject(message.Data, "shift"));
            List<object> flights = MiniJson.GetArray(message.Data, "flights");
            if (flights != null)
            {
                List<FlightSnap> decoded = new List<FlightSnap>();
                for (int i = 0; i < flights.Count; i++)
                {
                    Dictionary<string, object> entry = flights[i] as Dictionary<string, object>;
                    if (entry == null)
                    {
                        continue;
                    }

                    FlightSnap f = new FlightSnap();
                    f.Id = MiniJson.GetString(entry, "id", string.Empty);
                    f.Status = (int)MiniJson.GetNumber(entry, "st", 0);
                    f.Stand = (int)MiniJson.GetNumber(entry, "stand", -1);
                    List<object> progress = MiniJson.GetArray(entry, "p");
                    for (int k = 0; k < 4 && progress != null && k < progress.Count; k++)
                    {
                        if (progress[k] is double)
                        {
                            f.Progress[k] = (float)(double)progress[k];
                        }
                    }

                    f.Arrival = (float)MiniJson.GetNumber(entry, "arr", 0);
                    f.Deadline = (float)MiniJson.GetNumber(entry, "dl", 0);
                    f.ArrivalBagsReturned = MiniJson.GetBool(entry, "ab", false);
                    f.PlaneAssignedAt = (float)MiniJson.GetNumber(entry, "assigned", 0);
                    decoded.Add(f);
                }

                snap.Flights = decoded.ToArray();
            }

            snap.Score = message.GetInt("score", 0);
            snap.Elapsed = message.GetFloat("elapsed", 0f);
            return snap;
        }

        static ShiftSnap DecodeShift(Dictionary<string, object> obj)
        {
            ShiftSnap shift = new ShiftSnap();
            if (obj == null)
            {
                return shift;
            }

            shift.NextEventSequence = (int)MiniJson.GetNumber(obj, "nextEvent", 0);
            shift.NextSpillId = (int)MiniJson.GetNumber(obj, "nextSpill", 0);
            shift.FuelTruckAtStation = MiniJson.GetBool(obj, "atStation", false);
            shift.CartDrivers = DecodeInts(obj, "drivers", shift.CartDrivers);
            shift.GateSeats = DecodeInts(obj, "gateSeats", shift.GateSeats);
            shift.HumanTaskCounts = DecodeInts(obj, "humanTasks", shift.HumanTaskCounts);
            shift.MealPhase = (int)MiniJson.GetNumber(obj, "meal", 0);
            shift.MealProgress = (float)MiniJson.GetNumber(obj, "mealp", 0);
            shift.FuelTruckTank = (float)MiniJson.GetNumber(obj, "tank", 0);
            shift.NozzleState = (int)MiniJson.GetNumber(obj, "noz", 0);
            shift.NozzleSeat = (int)MiniJson.GetNumber(obj, "nozs", -1);
            shift.HoseState = (int)MiniJson.GetNumber(obj, "hose", 0);
            shift.HoseSeat = (int)MiniJson.GetNumber(obj, "hoses", -1);
            shift.HoseFlightId = MiniJson.GetString(obj, "hosef", string.Empty);
            shift.ValveOpen = MiniJson.GetBool(obj, "valve", false);
            shift.StationOverfill = (float)MiniJson.GetNumber(obj, "ovs", 0);
            shift.HoseOverfill = (float)MiniJson.GetNumber(obj, "ovh", 0);
            shift.Spilling = MiniJson.GetBool(obj, "spillon", false);
            shift.HoseSpilling = MiniJson.GetBool(obj, "hspill", false);
            List<object> spills = MiniJson.GetArray(obj, "spills");
            if (spills != null)
            {
                List<SpillSnap> decodedSpills = new List<SpillSnap>();
                for (int i = 0; i < spills.Count; i++)
                {
                    Dictionary<string, object> entry = spills[i] as Dictionary<string, object>;
                    if (entry == null) continue;
                    decodedSpills.Add(new SpillSnap
                    {
                        Id = (int)MiniJson.GetNumber(entry, "id", 0),
                        X = (float)MiniJson.GetNumber(entry, "x", 0),
                        Z = (float)MiniJson.GetNumber(entry, "z", 0),
                        Radius = (float)MiniJson.GetNumber(entry, "r", ShiftSim.SpillRadius),
                        CleanWork = (float)MiniJson.GetNumber(entry, "w", 0)
                    });
                }

                shift.Spills = decodedSpills.ToArray();
            }
            List<object> carts = MiniJson.GetArray(obj, "carts");
            for (int i = 0; i < shift.Carts.Length && carts != null && i < carts.Count; i++)
            {
                Dictionary<string, object> entry = carts[i] as Dictionary<string, object>;
                if (entry == null)
                {
                    continue;
                }

                shift.Carts[i].FlightId = MiniJson.GetString(entry, "f", string.Empty);
                shift.Carts[i].Arrival = MiniJson.GetBool(entry, "arr", false);
                shift.Carts[i].DeliverWork = (float)MiniJson.GetNumber(entry, "w", 0);
                shift.Carts[i].DeliverFlightId = MiniJson.GetString(entry, "dw", string.Empty);
            }

            shift.Boarding = DecodeStringArray(obj, "board");
            List<object> pax = MiniJson.GetArray(obj, "pax");
            if (pax != null)
            {
                List<PassengerSnap> decoded = new List<PassengerSnap>();
                for (int i = 0; i < pax.Count; i++)
                {
                    Dictionary<string, object> entry = pax[i] as Dictionary<string, object>;
                    if (entry == null)
                    {
                        continue;
                    }

                    decoded.Add(new PassengerSnap
                    {
                        FlightId = MiniJson.GetString(entry, "f", string.Empty),
                        Seq = (int)MiniJson.GetNumber(entry, "s", 0),
                        Stand = (int)MiniJson.GetNumber(entry, "stand", 0),
                        Waypoint = (int)MiniJson.GetNumber(entry, "w", 1),
                        Progress01 = (float)MiniJson.GetNumber(entry, "p", 0),
                        Delay = (float)MiniJson.GetNumber(entry, "d", 0),
                        Released = MiniJson.GetBool(entry, "rel", false),
                        Seat = (int)MiniJson.GetNumber(entry, "seat", -1)
                    });
                }

                shift.Passengers = decoded.ToArray();
            }

            shift.Failed = DecodeStringArray(obj, "fail");
            List<object> rawEvents = MiniJson.GetArray(obj, "events");
            if (rawEvents != null)
            {
                List<ShiftEventSnap> decodedEvents = new List<ShiftEventSnap>();
                for (int i = 0; i < rawEvents.Count; i++)
                {
                    Dictionary<string, object> entry = rawEvents[i] as Dictionary<string, object>;
                    if (entry == null) continue;
                    decodedEvents.Add(new ShiftEventSnap
                    {
                        Sequence = (int)MiniJson.GetNumber(entry, "seq", 0),
                        Time = (float)MiniJson.GetNumber(entry, "time", 0),
                        Type = MiniJson.GetString(entry, "type", string.Empty),
                        FlightId = MiniJson.GetString(entry, "f", string.Empty),
                        RelatedFlightId = MiniJson.GetString(entry, "rf", string.Empty),
                        Kind = (int)MiniJson.GetNumber(entry, "kind", -1),
                        Seat = (int)MiniJson.GetNumber(entry, "seat", -1),
                        Text = MiniJson.GetString(entry, "text", string.Empty),
                        Bell = MiniJson.GetBool(entry, "bell", false)
                    });
                }

                shift.Events = decodedEvents.ToArray();
            }

            List<object> ready = MiniJson.GetArray(obj, "ready");
            for (int i = 0; i < shift.StandReady.Length && ready != null && i < ready.Count; i++)
            {
                if (ready[i] is bool)
                {
                    shift.StandReady[i] = (bool)ready[i];
                }
            }

            return shift;
        }

        static string[] DecodeStringArray(Dictionary<string, object> obj, string key)
        {
            List<object> raw = MiniJson.GetArray(obj, key);
            if (raw == null)
            {
                return new string[0];
            }

            List<string> result = new List<string>();
            for (int i = 0; i < raw.Count; i++)
            {
                result.Add(raw[i] as string ?? string.Empty);
            }

            return result.ToArray();
        }

        /// <summary>从 roster 消息解码第 i 席（名字/占用/bot）。越界返回空席。</summary>
        public static void DecodeRosterSeat(NetMessage message, int seat, out string name, out bool occupied, out bool bot)
        {
            name = string.Empty;
            occupied = false;
            bot = false;
            List<object> seats = MiniJson.GetArray(message.Data, "list");
            if (seats == null || seat < 0 || seat >= seats.Count)
            {
                return;
            }

            Dictionary<string, object> entry = seats[seat] as Dictionary<string, object>;
            if (entry == null)
            {
                return;
            }

            name = MiniJson.GetString(entry, "name", string.Empty);
            occupied = MiniJson.GetBool(entry, "occ", false);
            bot = MiniJson.GetBool(entry, "bot", false);
        }
    }
}
