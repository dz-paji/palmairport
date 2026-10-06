using System;
using System.Collections.Generic;

namespace IslandAirport
{
    /// <summary>餐食状态机（出货口阻塞语义：Ready 未被餐车取走时不可再下单）。</summary>
    public enum MealPhase
    {
        Idle = 0,
        Ordered = 1,
        Ready = 2
    }

    /// <summary>站内加油枪（单实例物品）的位置状态机：枪架 → 手持 → 插在站边油车上。永不随车离站（M3.3r）。</summary>
    public enum NozzlePhase
    {
        AtStation = 0,
        Held = 1,
        OnTruck = 2
    }

    /// <summary>油车自带的车载油管（M3.3r，专用于机位加注）：收在车上 → 手持 → 接在飞机上。</summary>
    public enum HosePhase
    {
        OnTruck = 0,
        Held = 1,
        OnAircraft = 2
    }

    /// <summary>M3.3r 燃油单次点按的解析结果（站内/油车/飞机三处点按的优先级由 ShiftSim.Resolve*Tap 裁定）。</summary>
    public enum FuelTap
    {
        None = 0,
        TakeNozzle,
        ReturnNozzle,
        InsertNozzle,
        PullNozzle,
        OpenValve,
        CloseValve,
        TakeHose,
        ReturnHose,
        AttachHose,
        DetachHose,
        Drive
    }

    /// <summary>一处漫油油渍区（路面打滑区）。坐标为纯 float（Core 零 Unity 依赖），由表现层注入原点。</summary>
    public sealed class ShiftSpill
    {
        public int Id;
        public float X;
        public float Z;
        public float Radius;
        /// <summary>清理读条 0..1：徒步按住清理累计；中断保留（油渍不是航班任务读条）。</summary>
        public float CleanWork;
        /// <summary>本帧清理读条占用席位（-1 = 空）；一座一区，同帧他席清理忽略。</summary>
        internal int CleanSeat = -1;
        internal bool CleanTouched;
    }

    /// <summary>一辆共享车辆的任务域状态（读条在 domain，不在 crew）。位置/Visual/Owner 仍属表现层。</summary>
    public sealed class ShiftCart
    {
        /// <summary>货物归属航班号；空串 = 空车。</summary>
        public string CargoFlightId = string.Empty;
        public bool Arrival;
        public float DeliverWork;
        /// <summary>本次读条绑定的接收航班，切换机位不会继承读条。</summary>
        public string DeliverFlightId = string.Empty;
        /// <summary>本帧是否被 Deliver 触碰（EndFrame 据此清零未续持的读条）。</summary>
        internal bool DeliverTouched;
        /// <summary>本帧持有读条的席位（-1 = 空）。一座一车的读条占用语义：同帧他席 Deliver 忽略。</summary>
        internal int DeliverSeat = -1;

        public bool Loaded
        {
            get { return CargoFlightId.Length > 0; }
        }

        public void Clear()
        {
            CargoFlightId = string.Empty;
            Arrival = false;
            DeliverWork = 0f;
            DeliverFlightId = string.Empty;
            DeliverTouched = false;
            DeliverSeat = -1;
        }
    }

    /// <summary>一名旅客的参数化状态：沿 PavementPath 的 (waypoint, progress01)，不含世界坐标。</summary>
    public sealed class ShiftPassenger
    {
        public string FlightId = string.Empty;
        /// <summary>同一航班内的序号（0..7），稳定 ID = (flightId, seq)。</summary>
        public int Seq;
        /// <summary>当前目标路点下标（1..path.Length-1）。</summary>
        public int Waypoint = 1;
        /// <summary>当前路段进度 0..1（按有效腿长，含 0.08m 到站余量）。</summary>
        public float Progress01;
        public float Delay;
        /// <summary>已放行旅客继续行进；未放行旅客在关门时保留排队延迟。</summary>
        public bool Released;
        public int Seat = -1;
        public int Stand;
    }

    /// <summary>
    /// M3 任务领域编排器：组合 AirportSimulation（航班/进度/计分不动），
    /// 持有原 AirportGame 散字段（餐食/燃油/车辆货物/登机/旅客/机位就绪），
    /// 本地与 LAN host 共用同一份规则实现；client 仅持只读镜像（ApplyMirror 整块覆盖）。
    /// 零 Unity 依赖：几何以纯 float（人行道分段腿长、漫油原点）注入；计时全走 Tick(dt)。
    /// M3.1 公共规则：一座一车（同帧唯一读条占用）、装载归属 cargo→flightId、
    /// 交付读条 2.5s 由 Deliver 推进、取消餐食读条清零货物留车、出货口阻塞、
    /// 空车复用、重复交付防护（deliver_ignored）；LAN 任务练习经 AttachScheduler 循环到场。
    /// M3.3r 燃油流程（2026-10-05 定稿）：站内储备 = 无限资源库（不消耗、不需补充）；
    /// 两套油管：站内油枪（单实例，一座一枪）只在油站与站边油车之间使用，永不随车离站——
    /// 拿枪 → 插到油车 → 开阀加注 → 关阀（插在车上的油枪自动归位）；油车自带车载油管专用于机位——
    /// 取管 → 接到飞机后由 Tick 自动转移（储量↔航班燃油进度 1:1，中断保留）→ 断开即自动收回。
    /// 全部单次点按；在油车上无法操作油枪、油管与阀门。
    /// 漫油（§待定项 4）：阀门开着且油枪未插车 → 立即漫油；油车满箱仍开阀、或飞机已满仍接管
    /// 持续 OverfillGraceSeconds 后漫油（满溢宽限）。生成路面油渍打滑区（车辆经过减速），需玩家
    /// 徒步按住清理；不判任务失败、不扣分、不消耗站内储备与油车储量。
    /// </summary>
    public sealed class ShiftSim
    {
        public const int CartCount = 3;
        public const int EventHistoryLimit = 32;
        public const float MealPrepareSeconds = 5f;
        /// <summary>站内开阀后油车储量 0→1 的注满时长（储备无限，只限流量）。</summary>
        public const float StationFillSeconds = 6f;
        /// <summary>机位自动加注：油车储量→航班燃油进度的转移时长（1:1，双向中断保留）。</summary>
        public const float DockFuelSeconds = 3f;
        /// <summary>满溢宽限：油车满箱仍开阀、或飞机已满仍接管，持续该时长后开始漫油（M3.3r）。</summary>
        public const float OverfillGraceSeconds = 2f;
        /// <summary>徒步按住清理一处油渍的时长（中断保留进度）。</summary>
        public const float SpillCleanSeconds = 2.5f;
        public const float DeliverSeconds = 2.5f;
        public const int PassengersPerGate = 8;
        public const float PassengerStagger = .65f;
        public const float PassengerSpeed = 1.7f;
        /// <summary>旅客距路点此距离内即算到达（与旧 MoveTowards 判定一致）。</summary>
        public const float WaypointSlack = .08f;
        public const float BoardingPerPassenger = .125f;
        /// <summary>油渍区半径（表现层按此渲染打滑区并做车辆打滑判定）。</summary>
        public const float SpillRadius = 1.35f;
        public const int MaxSpills = 4;

        /// <summary>四项任务的中文名（原 AirportGame.Names，事件文案共用）。</summary>
        public static readonly string[] TaskNames = { "餐食", "行李", "燃油", "登机" };

        readonly float[][] legLengths;
        readonly float spillX;
        readonly float spillZ;
        readonly List<ShiftEvent> pending = new List<ShiftEvent>();
        readonly List<ShiftEvent> eventHistory = new List<ShiftEvent>();
        readonly HashSet<string> boarding = new HashSet<string>();
        readonly Dictionary<string, int> gateSeats = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly bool[] standReady = { true, true, true };
        readonly int[] cartDrivers = { -1, -1, -1 };
        PracticeFlightScheduler scheduler;
        int nextEventSequence;
        int nextSpillId;
        bool spilling;
        bool hoseSpilling;
        bool fuelTruckAtStation = true;
        readonly float[] dockSpillX = new float[AirportSimulation.StandCount];
        readonly float[] dockSpillZ = new float[AirportSimulation.StandCount];
        readonly bool[] dockSpillSet = new bool[AirportSimulation.StandCount];
        public readonly int[] HumanTaskCounts = new int[4];
        readonly bool[] humanSeats = { true, true };
        public void SetHumanSeat(int seat, bool human) { if (seat >= 0 && seat < humanSeats.Length) humanSeats[seat] = human; }

        public ShiftSim(AirportSimulation sim, float[][] pavementLegLengths)
            : this(sim, pavementLegLengths, 0f, 0f)
        {
        }

        /// <summary>spillX/spillZ：漫油油渍生成原点（油站前路面，纯 float 注入，表现层给世界坐标）。</summary>
        public ShiftSim(AirportSimulation sim, float[][] pavementLegLengths, float spillX, float spillZ)
        {
            if (sim == null) throw new ArgumentNullException("sim");
            Sim = sim;
            legLengths = pavementLegLengths ?? new float[AirportSimulation.StandCount][];
            this.spillX = spillX;
            this.spillZ = spillZ;
            Carts = new ShiftCart[CartCount];
            for (int i = 0; i < Carts.Length; i++)
            {
                Carts[i] = new ShiftCart();
            }

            Passengers = new List<ShiftPassenger>();
            Failed = new HashSet<string>();
            Spills = new List<ShiftSpill>();
            NozzleSeat = -1;
            HoseSeat = -1;
            HoseFlightId = string.Empty;
        }

        /// <summary>机位满溢漫油的油渍原点（表现层注入该机位停车点世界坐标；未注入时回落到油站原点）。</summary>
        public void SetDockSpillOrigin(int stand, float x, float z)
        {
            if (stand < 0 || stand >= dockSpillSet.Length) return;
            dockSpillX[stand] = x;
            dockSpillZ[stand] = z;
            dockSpillSet[stand] = true;
        }

        public AirportSimulation Sim { get; private set; }

        public MealPhase Meal { get; private set; }

        public float MealProgress01 { get; private set; }

        // ---- M3.3 燃油新流程（加油站式；站内储备无限不设上限，不消耗、不需补充）----

        /// <summary>油车储量 0..1：站内开阀经油枪注入；机位接管后经车载油管 1:1 自动转给航班燃油进度。</summary>
        public float FuelTruckTank { get; private set; }

        /// <summary>油车是否停在油站加注区。位置由表现层以纯 bool 空间事实回填，Core 不依赖 Unity。</summary>
        public bool FuelTruckAtStation { get { return fuelTruckAtStation; } }

        /// <summary>站内加油枪位置状态机（单实例物品，占用语义同车辆：一座一枪；永不离站）。</summary>
        public NozzlePhase NozzleState { get; private set; }

        /// <summary>持枪席位（-1 = 无人持枪）。</summary>
        public int NozzleSeat { get; private set; }

        /// <summary>车载油管状态机（M3.3r）：收在车上 / 手持 / 接在飞机上。</summary>
        public HosePhase HoseState { get; private set; }

        /// <summary>手持油管或接管的席位（OnTruck 时 -1；OnAircraft 时为接管者，用于完成归因）。</summary>
        public int HoseSeat { get; private set; }

        /// <summary>车载油管所接飞机航班号（HoseState==OnAircraft 时有效）。</summary>
        public string HoseFlightId { get; private set; }

        /// <summary>站内加注阀门。开着且油枪未插车 → 立即漫油；油车满箱仍开阀 → 宽限后漫油。</summary>
        public bool ValveOpen { get; private set; }

        /// <summary>油车满箱仍开阀的累计满溢时长（达 OverfillGraceSeconds 起漫油；关阀/取枪清零）。</summary>
        public float StationOverfill { get; private set; }

        /// <summary>飞机燃油已满仍接管的累计满溢时长（达 OverfillGraceSeconds 起漫油；断管清零）。</summary>
        public float HoseOverfill { get; private set; }

        /// <summary>当前是否处于漫油状态（站内或机位任一来源）。</summary>
        public bool Spilling { get { return spilling || hoseSpilling; } }

        /// <summary>站内来源（阀门）是否正在漫油。</summary>
        public bool StationSpilling { get { return spilling; } }

        /// <summary>机位来源（飞机满溢仍接管）是否正在漫油。</summary>
        public bool HoseSpilling { get { return hoseSpilling; } }

        /// <summary>路面油渍打滑区列表（漫油生成、徒步清理移除；进快照 client 镜像可见）。</summary>
        public List<ShiftSpill> Spills { get; private set; }

        /// <summary>更新油车是否仍停在油站加注区。权威端每 Step 根据纯位置判定；镜像端不调用。</summary>
        public void SetFuelTruckAtStation(bool atStation)
        {
            fuelTruckAtStation = atStation;
        }

        /// <summary>油车可以行驶的规则门禁（M3.3r）：阀门开着、站内油枪插在车上、
        /// 或车载油管不在车上（手持或接在飞机上）时都不能开走。</summary>
        public bool CanDriveCart(int cartId)
        {
            if (cartId < 0 || cartId >= CartCount)
            {
                return false;
            }

            return cartId != (int)ServiceKind.Fuel ||
                   (!ValveOpen && NozzleState != NozzlePhase.OnTruck && HoseState == HosePhase.OnTruck);
        }

        /// <summary>占用共享车辆。每席同时只能驾驶一辆，每车只有一个驾驶者。</summary>
        public bool TryClaimCart(int seat, int cartId)
        {
            if (Sim.Finished) return false;
            if (!ValidSeat(seat) || cartId < 0 || cartId >= CartCount || !CanDriveCart(cartId) || cartDrivers[cartId] != -1)
            {
                return false;
            }

            for (int i = 0; i < cartDrivers.Length; i++)
            {
                if (cartDrivers[i] == seat)
                {
                    return false;
                }
            }

            cartDrivers[cartId] = seat;
            // M3.3r：站内油枪永不随车离站、车载油管不离开油车——上任一辆车时手里的油枪/油管自动归位。
            if (NozzleState == NozzlePhase.Held && NozzleSeat == seat)
            {
                NozzleState = NozzlePhase.AtStation;
                NozzleSeat = -1;
                Emit(ShiftEventTypes.NozzleReturned, string.Empty, (int)ServiceKind.Fuel, seat,
                    "上车前油枪已自动归位到油站", false);
            }

            if (HoseState == HosePhase.Held && HoseSeat == seat)
            {
                ReelHose();
                Emit(ShiftEventTypes.HoseReturned, string.Empty, (int)ServiceKind.Fuel, seat,
                    "上车前油管已自动收回油车", false);
            }

            return true;
        }

        /// <summary>放开共享车辆。仅当前驾驶者可释放。</summary>
        public bool ReleaseCart(int seat, int cartId)
        {
            if (Sim.Finished) return false;
            if (!ValidSeat(seat) || cartId < 0 || cartId >= CartCount || cartDrivers[cartId] != seat)
            {
                return false;
            }

            cartDrivers[cartId] = -1;
            return true;
        }

        public int CartDriverSeat(int cartId)
        {
            return cartId >= 0 && cartId < CartCount ? cartDrivers[cartId] : -1;
        }

        bool IsDriving(int seat)
        {
            for (int i = 0; i < cartDrivers.Length; i++)
            {
                if (cartDrivers[i] == seat) return true;
            }

            return false;
        }

        static bool ValidSeat(int seat)
        {
            return seat >= 0 && seat < 2;
        }

        /// <summary>按 cartId 0..2 对齐 ServiceKind Meals/Baggage/Fuel。</summary>
        public ShiftCart[] Carts { get; private set; }

        public List<ShiftPassenger> Passengers { get; private set; }

        /// <summary>"flightId:kind" 永久失败集合（M3.2 送错行李才产生；本切片恒空）。</summary>
        public HashSet<string> Failed { get; private set; }

        /// <summary>镜像专用：最近快照的计分/班岗计时（host 端读 Sim，旧练习镜像端 Sim.Elapsed 恒 0；有限班岗 RestoreCheckpoint 含完整计时）。</summary>
        public int MirrorScore { get; private set; }
        public float MirrorElapsed { get; private set; }

        /// <summary>LAN 任务练习模式：挂循环时刻表航班生成器（仅 host/本地权威端；client 镜像只读，不得挂载也不得 Tick）。</summary>
        public void AttachScheduler(PracticeFlightScheduler practiceScheduler)
        {
            scheduler = practiceScheduler;
        }

        // ---------- 事件 ----------

        void Emit(string type, string flightId, int kind, int seat, string text, bool bell)
        {
            ShiftEvent emitted = new ShiftEvent
            {
                Sequence = ++nextEventSequence,
                Time = Sim.Elapsed,
                Type = type,
                FlightId = flightId ?? string.Empty,
                RelatedFlightId = string.Empty,
                Kind = kind,
                Seat = seat,
                Text = text ?? string.Empty,
                Bell = bell
            };
            if (type == ShiftEventTypes.Delivered && kind >= 0 && kind < 4 && seat >= 0 && seat < humanSeats.Length && humanSeats[seat]) HumanTaskCounts[kind]++;
            pending.Add(emitted);
            eventHistory.Add(emitted);
            if (eventHistory.Count > EventHistoryLimit)
            {
                eventHistory.RemoveAt(0);
            }
        }

        void EmitWrongDelivery(string receiverFlightId, string cargoFlightId, int seat, string text)
        {
            Emit(ShiftEventTypes.WrongDelivery, receiverFlightId, (int)ServiceKind.Baggage, seat, text, false);
            ShiftEvent emitted = pending[pending.Count - 1];
            emitted.RelatedFlightId = cargoFlightId ?? string.Empty;
            pending[pending.Count - 1] = emitted;
            eventHistory[eventHistory.Count - 1] = emitted;
        }

        /// <summary>只读查询永久失败状态；进度锁也由 AirportSimulation 强制执行。</summary>
        public bool IsTaskFailed(string flightId, ServiceKind kind)
        {
            return Sim.IsTaskFailed(flightId, kind) || Failed.Contains(AirportSimulation.TaskKey(flightId, kind));
        }

        /// <summary>取出并清空事件队列（host 每 Step 末尾转 toast/音效/统计）。</summary>
        public List<ShiftEvent> DrainEvents()
        {
            List<ShiftEvent> drained = new List<ShiftEvent>(pending);
            pending.Clear();
            return drained;
        }

        // ---------- 查询 ----------

        public bool StandReady(int stand)
        {
            return stand >= 0 && stand < standReady.Length && standReady[stand];
        }

        public void SetStandReady(int stand, bool ready)
        {
            if (stand >= 0 && stand < standReady.Length)
            {
                standReady[stand] = ready;
            }
        }

        public bool IsBoarding(string flightId)
        {
            return flightId != null && boarding.Contains(flightId);
        }

        public Flight FindFlight(string flightId)
        {
            if (string.IsNullOrEmpty(flightId))
            {
                return null;
            }

            for (int i = 0; i < Sim.Flights.Count; i++)
            {
                if (Sim.Flights[i].Id == flightId)
                {
                    return Sim.Flights[i];
                }
            }

            return null;
        }

        /// <summary>某机位人行道分段数（= 路点数 - 1）。</summary>
        public int PathLegCount(int stand)
        {
            return stand >= 0 && stand < legLengths.Length && legLengths[stand] != null
                ? legLengths[stand].Length
                : 0;
        }

        /// <summary>旅客当前路段的有效长度（含到站余量；表现层还原坐标用同一口径）。</summary>
        public float EffectiveLegLength(int stand, int waypoint)
        {
            if (stand < 0 || stand >= legLengths.Length || legLengths[stand] == null ||
                waypoint < 1 || waypoint > legLengths[stand].Length)
            {
                return 1f;
            }

            return Math.Max(legLengths[stand][waypoint - 1] - WaypointSlack, .01f);
        }

        // ---------- 意图 API（本地玩家、bot、remote 输入同一入口）----------

        /// <summary>餐食站点下单。Ready 未取走时拒绝（出货口阻塞）；制作中重复下单静默忽略。</summary>
        public bool OrderMeal(int seat)
        {
            if (Sim.Finished) return false;
            if (Meal == MealPhase.Ready)
            {
                Emit(ShiftEventTypes.MealBlocked, string.Empty, (int)ServiceKind.Meals, seat,
                    "出货口被挡住了 · 请开餐车取走", false);
                return false;
            }

            if (Meal == MealPhase.Ordered)
            {
                return false;
            }

            Meal = MealPhase.Ordered;
            MealProgress01 = 0f;
            Emit(ShiftEventTypes.MealOrdered, string.Empty, (int)ServiceKind.Meals, seat,
                "餐食已下单 · 5 秒后备好，可以先做别的任务", false);
            return true;
        }

        /// <summary>餐食备好，装上餐车（取走出货口）。flightId 为目标航班。</summary>
        public bool TakeMeal(int seat, int cartId, string flightId)
        {
            if (Sim.Finished) return false;
            if (!ValidCart(cartId, ServiceKind.Meals) || Meal != MealPhase.Ready || Carts[cartId].Loaded)
            {
                return false;
            }

            Flight flight = FindFlight(flightId);
            if (!LoadOnto(cartId, flight, seat))
            {
                return false;
            }

            Meal = MealPhase.Idle;
            MealProgress01 = 0f;
            Emit(ShiftEventTypes.MealTaken, flightId, (int)ServiceKind.Meals, seat, string.Empty, false);
            return true;
        }

        /// <summary>站内装载出发货物：行李车装出发行李（需先归还到达行李）。餐食走 TakeMeal、燃油走加油站式流程（油车不装货）。</summary>
        public bool LoadCart(int seat, int cartId, string flightId)
        {
            if (Sim.Finished) return false;
            if (cartId < 0 || cartId >= CartCount || Carts[cartId].Loaded ||
                cartId == (int)ServiceKind.Meals || cartId == (int)ServiceKind.Fuel)
            {
                if (cartId == (int)ServiceKind.Fuel)
                {
                    Emit(ShiftEventTypes.LoadRefused, flightId ?? string.Empty, (int)ServiceKind.Fuel, seat,
                        "油车不装货 · 拿加油枪插到油车，开阀加注", false);
                }

                return false;
            }

            Flight flight = FindFlight(flightId);
            if (flight == null)
            {
                return false;
            }

            if (cartId == (int)ServiceKind.Baggage && !flight.ArrivalBagsReturned)
            {
                Emit(ShiftEventTypes.LoadRefused, flightId, (int)ServiceKind.Baggage, seat,
                    "先开空行李车到 " + flightId + "，卸下到达行李。", false);
                return false;
            }

            return LoadOnto(cartId, flight, seat);
        }

        /// <summary>机位侧：空行李车取下到达行李（装车、标记 Arrival）。</summary>
        public bool PickupArrivalBags(int seat, int cartId, string flightId)
        {
            if (Sim.Finished) return false;
            if (!ValidCart(cartId, ServiceKind.Baggage) || Carts[cartId].Loaded)
            {
                return false;
            }

            Flight flight = FindFlight(flightId);
            if (flight == null || flight.ArrivalBagsReturned)
            {
                return false;
            }

            if (!LoadOnto(cartId, flight, seat))
            {
                return false;
            }

            Carts[cartId].Arrival = true;
            Emit(ShiftEventTypes.ArrivalPicked, flightId, (int)ServiceKind.Baggage, seat,
                "到达行李已取下 · 送回左侧行李站", false);
            return true;
        }

        /// <summary>行李站归还到达行李。无论航班是否仍可操作，车辆都清空可用。</summary>
        public bool ReturnArrivalBagsFromCart(int seat, int cartId)
        {
            if (Sim.Finished) return false;
            if (!ValidCart(cartId, ServiceKind.Baggage) || !Carts[cartId].Arrival)
            {
                return false;
            }

            string flightId = Carts[cartId].CargoFlightId;
            Flight flight = FindFlight(flightId);
            bool accepted = flight != null && Sim.ReturnArrivalBags(flight);
            Carts[cartId].Clear();
            Emit(accepted ? ShiftEventTypes.ArrivalReturned : ShiftEventTypes.ArrivalRecycled,
                flightId, (int)ServiceKind.Baggage, seat,
                accepted ? "到达行李已送回！现在可装载出发行李。" : "航班已截止 · 到达行李已回收，车辆可继续使用。",
                false);
            return true;
        }

        // ---------- M3.3r 燃油意图（站内：拿枪—插车—开阀—关阀自动归位；机位：取管—接管自动加注—断管自动收回）----------

        /// <summary>徒步在油站拿起站内油枪：AtStation→Held。插在车上的油枪只经关阀（或 ReturnNozzle）归位。</summary>
        public bool TakeNozzle(int seat)
        {
            if (Sim.Finished) return false;
            if (!ValidSeat(seat) || IsDriving(seat))
            {
                if (ValidSeat(seat))
                {
                    Emit(ShiftEventTypes.NozzleRefused, string.Empty, (int)ServiceKind.Fuel, seat,
                        "先下车，再操作加油枪。", false);
                }
                return false;
            }

            if (NozzleState == NozzlePhase.Held)
            {
                if (NozzleSeat != seat)
                {
                    Emit(ShiftEventTypes.NozzleBusy, string.Empty, (int)ServiceKind.Fuel, seat,
                        "油枪在搭档手里 · 等 TA 用完", false);
                }

                return false;
            }

            if (NozzleState != NozzlePhase.AtStation || (HoseState == HosePhase.Held && HoseSeat == seat))
            {
                return false;
            }

            NozzleState = NozzlePhase.Held;
            NozzleSeat = seat;
            Emit(ShiftEventTypes.NozzleTaken, string.Empty, (int)ServiceKind.Fuel, seat,
                "加油枪已拿起 · 插到站边油车上再开阀加注", false);
            return true;
        }

        /// <summary>
        /// 站内油枪归位：手持枪（本席）→ 枪架；插在油车上且阀门关着的枪 → 枪架（未开阀就想撤枪时用）。
        /// 阀门开着时插车枪只能经关阀自动归位。
        /// </summary>
        public bool ReturnNozzle(int seat)
        {
            if (Sim.Finished) return false;
            if (!ValidSeat(seat) || IsDriving(seat))
            {
                return false;
            }

            if (NozzleState == NozzlePhase.OnTruck)
            {
                if (ValveOpen)
                {
                    return false;
                }

                NozzleState = NozzlePhase.AtStation;
                NozzleSeat = -1;
                Emit(ShiftEventTypes.NozzleReturned, string.Empty, (int)ServiceKind.Fuel, seat,
                    "油枪已从油车拔下并归位", false);
                return true;
            }

            if (NozzleState != NozzlePhase.Held || NozzleSeat != seat)
            {
                return false;
            }

            NozzleState = NozzlePhase.AtStation;
            NozzleSeat = -1;
            Emit(ShiftEventTypes.NozzleReturned, string.Empty, (int)ServiceKind.Fuel, seat,
                "加油枪已归还", false);
            return true;
        }

        /// <summary>把手中站内油枪插到站边油车加注口（cartId 必须是油车，油车须停在油站）。</summary>
        public bool AttachNozzle(int seat, int cartId)
        {
            if (Sim.Finished) return false;
            if (!ValidSeat(seat) || IsDriving(seat) || !ValidCart(cartId, ServiceKind.Fuel) || !FuelTruckAtStation)
            {
                if (ValidSeat(seat) && !FuelTruckAtStation)
                {
                    Emit(ShiftEventTypes.NozzleRefused, string.Empty, (int)ServiceKind.Fuel, seat,
                        "油车不在油站加注区，无法接入站内油枪。", false);
                }
                return false;
            }

            if (NozzleState != NozzlePhase.Held || NozzleSeat != seat)
            {
                return false;
            }

            NozzleState = NozzlePhase.OnTruck;
            NozzleSeat = -1;
            Emit(ShiftEventTypes.NozzleAttached, string.Empty, (int)ServiceKind.Fuel, seat,
                "油枪已插入油车 · 点油站开阀加注", false);
            return true;
        }

        /// <summary>
        /// 开关站内加注阀门。开着且油枪未插车 → 立即漫油；油车满箱仍开阀 → 宽限后漫油（Tick 判定）。
        /// 关阀时插在车上的油枪自动归位到油站；手持的油枪仍在手上。
        /// </summary>
        public bool SetValve(int seat, bool open)
        {
            if (Sim.Finished) return false;
            if (!ValidSeat(seat) || IsDriving(seat))
            {
                if (ValidSeat(seat))
                {
                    Emit(ShiftEventTypes.NozzleRefused, string.Empty, (int)ServiceKind.Fuel, seat,
                        "先下车，再操作阀门。", false);
                }
                return false;
            }

            if (open == ValveOpen)
            {
                return false;
            }

            ValveOpen = open;
            StationOverfill = 0f;
            if (open)
            {
                Emit(ShiftEventTypes.ValveOpened, string.Empty, (int)ServiceKind.Fuel, seat,
                    NozzleState == NozzlePhase.OnTruck
                        ? (FuelTruckTank >= 1f ? "阀门已开 · 油车已满，2 秒内关阀否则漫油" : "阀门已开 · 油车加注中")
                        : "阀门已开 · 油枪未插车，燃油外溢！", false);
            }
            else
            {
                Emit(ShiftEventTypes.ValveClosed, string.Empty, (int)ServiceKind.Fuel, seat,
                    "阀门已关", false);
                if (NozzleState == NozzlePhase.OnTruck)
                {
                    NozzleState = NozzlePhase.AtStation;
                    NozzleSeat = -1;
                    Emit(ShiftEventTypes.NozzleReturned, string.Empty, (int)ServiceKind.Fuel, seat,
                        "关阀 · 油枪已自动归位，油车可以出发", false);
                }
            }

            return true;
        }

        /// <summary>徒步从油车取出车载油管（OnTruck→Held）。机位优先级（航班需油且车有油）由表现层裁定。</summary>
        public bool TakeHose(int seat)
        {
            if (Sim.Finished) return false;
            if (!ValidSeat(seat) || IsDriving(seat))
            {
                if (ValidSeat(seat))
                {
                    Emit(ShiftEventTypes.NozzleRefused, string.Empty, (int)ServiceKind.Fuel, seat,
                        "先下车，再取油管。", false);
                }
                return false;
            }

            if (HoseState == HosePhase.Held && HoseSeat != seat)
            {
                Emit(ShiftEventTypes.NozzleBusy, string.Empty, (int)ServiceKind.Fuel, seat,
                    "油管在搭档手里 · 等 TA 接好", false);
                return false;
            }

            if (HoseState != HosePhase.OnTruck || (NozzleState == NozzlePhase.Held && NozzleSeat == seat))
            {
                return false;
            }

            HoseState = HosePhase.Held;
            HoseSeat = seat;
            HoseFlightId = string.Empty;
            Emit(ShiftEventTypes.HoseTaken, string.Empty, (int)ServiceKind.Fuel, seat,
                "已取出车载油管 · 点飞机接管，自动加注", false);
            return true;
        }

        /// <summary>手持车载油管点油车 = 收回（取消），油管自动卷回车上。</summary>
        public bool ReturnHose(int seat)
        {
            if (Sim.Finished) return false;
            if (!ValidSeat(seat) || IsDriving(seat) || HoseState != HosePhase.Held || HoseSeat != seat)
            {
                return false;
            }

            ReelHose();
            Emit(ShiftEventTypes.HoseReturned, string.Empty, (int)ServiceKind.Fuel, seat,
                "油管已收回油车", false);
            return true;
        }

        /// <summary>机位接管：把手中车载油管接到飞机加油口（油车须停在该机位旁），之后 Tick 自动注油。</summary>
        public bool AttachHose(int seat, string flightId, bool fuelTruckAtDock)
        {
            if (Sim.Finished) return false;
            if (!ValidSeat(seat) || IsDriving(seat) || HoseState != HosePhase.Held || HoseSeat != seat)
            {
                return false;
            }

            if (!fuelTruckAtDock)
            {
                Emit(ShiftEventTypes.NozzleRefused, flightId ?? string.Empty, (int)ServiceKind.Fuel, seat,
                    "先把油车开到机位，再接上油管。", false);
                return false;
            }

            Flight flight = FindFlight(flightId);
            if (flight == null || flight.Status != FlightStatus.Servicing ||
                !StandReady(flight.Stand) || IsTaskFailed(flight.Id, ServiceKind.Fuel) ||
                flight.Progress[(int)ServiceKind.Fuel] >= 1f)
            {
                return false;
            }

            if (FuelTruckTank <= 0f)
            {
                Emit(ShiftEventTypes.NozzleRefused, flight.Id, (int)ServiceKind.Fuel, seat,
                    "油车没油了 · 先回油站加注。", false);
                return false;
            }

            HoseState = HosePhase.OnAircraft;
            HoseSeat = seat;
            HoseFlightId = flight.Id;
            HoseOverfill = 0f;
            Emit(ShiftEventTypes.HoseAttached, flight.Id, (int)ServiceKind.Fuel, seat,
                "已接管 " + flight.Id + " · 自动加注中，加满后点飞机断开", false);
            return true;
        }

        /// <summary>再点飞机断开：接在飞机上的油管断开并自动收回油车（任一徒步席位可操作）。</summary>
        public bool DetachHose(int seat)
        {
            if (Sim.Finished) return false;
            if (!ValidSeat(seat) || IsDriving(seat) || HoseState != HosePhase.OnAircraft)
            {
                return false;
            }

            string flightId = HoseFlightId ?? string.Empty;
            ReelHose();
            Emit(ShiftEventTypes.HoseDetached, flightId, (int)ServiceKind.Fuel, seat,
                "已从 " + (flightId.Length > 0 ? flightId : "飞机") + " 断开 · 油管已收回油车", false);
            return true;
        }

        void ReelHose()
        {
            HoseState = HosePhase.OnTruck;
            HoseSeat = -1;
            HoseFlightId = string.Empty;
            HoseOverfill = 0f;
            hoseSpilling = false;
        }

        /// <summary>
        /// 机位自动加注一个时间片：油车储量 1:1 转给油管所接航班的燃油进度（速率 DockFuelSeconds 满一项）。
        /// 油车空即停止转移（不漫油）；飞机已满仍接管则累计满溢计时。
        /// </summary>
        void TickHose(float dt)
        {
            if (HoseState != HosePhase.OnAircraft || string.IsNullOrEmpty(HoseFlightId))
            {
                HoseOverfill = 0f;
                hoseSpilling = false;
                return;
            }

            Flight flight = FindFlight(HoseFlightId);
            if (flight == null || flight.Status != FlightStatus.Servicing ||
                !StandReady(flight.Stand) || IsTaskFailed(flight.Id, ServiceKind.Fuel))
            {
                HoseOverfill = 0f;
                hoseSpilling = false;
                return;
            }

            float before = flight.Progress[(int)ServiceKind.Fuel];
            if (before >= 1f)
            {
                HoseOverfill += dt;
                if (HoseOverfill >= OverfillGraceSeconds)
                {
                    float x = spillX, z = spillZ;
                    int stand = flight.Stand;
                    if (stand >= 0 && stand < dockSpillSet.Length && dockSpillSet[stand])
                    {
                        x = dockSpillX[stand];
                        z = dockSpillZ[stand];
                    }

                    if (!hoseSpilling)
                    {
                        hoseSpilling = true;
                        SpawnSpill(x, z);
                        Emit(ShiftEventTypes.SpillStarted, flight.Id, (int)ServiceKind.Fuel, -1,
                            flight.Id + " 已加满仍接着油管 · 漫油了！点飞机断开，徒步按住清理", false);
                    }
                    else if (!SpillZoneAlive(x, z))
                    {
                        SpawnSpill(x, z);
                    }
                }

                return;
            }

            HoseOverfill = 0f;
            hoseSpilling = false;
            if (FuelTruckTank <= 0f)
            {
                return;
            }

            float transfer = Math.Min(dt / DockFuelSeconds, FuelTruckTank);
            transfer = Math.Min(transfer, 1f - before);
            bool exhaustsTank = transfer >= FuelTruckTank;
            // Independent float additions/subtractions can leave less than one
            // progress ULP in the tank. Resolve only the final transfer within
            // 1e-6 of completion; a genuinely insufficient load remains partial.
            const float completionEpsilon = 0.000001f;
            if (exhaustsTank && 1f - before - transfer <= completionEpsilon)
                transfer = 1f - before;
            bool advanced = Sim.TryAdvance(flight, ServiceKind.Fuel, transfer);
            if (advanced)
            {
                float actualTransfer = flight.Progress[(int)ServiceKind.Fuel] - before;
                FuelTruckTank = exhaustsTank ? 0f : Math.Max(0f, FuelTruckTank - actualTransfer);
                if (flight.Progress[(int)ServiceKind.Fuel] >= 1f && FuelTruckTank <= completionEpsilon)
                    FuelTruckTank = 0f;
            }
            if (advanced && before < 1f && flight.Progress[(int)ServiceKind.Fuel] >= 1f)
            {
                Emit(ShiftEventTypes.Delivered, flight.Id, (int)ServiceKind.Fuel, HoseSeat,
                    flight.Id + " · 燃油已完成 · 点飞机断开油管", true);
            }
        }

        // ---------- M3.3r 单次点按优先级（纯规则；表现层只回填空间事实并据此分派）----------

        /// <summary>航班当前是否仍需燃油（服务中、机位停稳、未完成、未失败）。</summary>
        public bool FlightNeedsFuel(string flightId)
        {
            Flight flight = FindFlight(flightId);
            return flight != null && flight.Status == FlightStatus.Servicing && StandReady(flight.Stand) &&
                   flight.Progress[(int)ServiceKind.Fuel] < 1f && !IsTaskFailed(flight.Id, ServiceKind.Fuel);
        }

        /// <summary>点油站（阀门）：阀门开 → 关阀；枪在枪架 → 拿枪；本席持枪 → 归还；否则 → 开阀。</summary>
        public FuelTap ResolveStationTap(int seat)
        {
            if (!ValidSeat(seat) || IsDriving(seat)) return FuelTap.None;
            if (ValveOpen) return FuelTap.CloseValve;
            if (NozzleState == NozzlePhase.AtStation) return FuelTap.TakeNozzle;
            if (NozzleState == NozzlePhase.Held && NozzleSeat == seat) return FuelTap.ReturnNozzle;
            return FuelTap.OpenValve;
        }

        /// <summary>
        /// 点油车。站内：本席手持油枪 → 插枪；油枪插着且阀门关 → 拔枪归位；否则 → 上车驾驶。
        /// 机位（dockFlightId = 油车所停机位的航班，表现层回填）：该航班仍需燃油且油车有油 → 取管；否则 → 上车驾驶。
        /// 本席手持车载油管 → 收回。能否真正开走由 CanDriveCart 裁定。
        /// </summary>
        public FuelTap ResolveTruckTap(int seat, string dockFlightId)
        {
            if (!ValidSeat(seat) || IsDriving(seat)) return FuelTap.None;
            if (HoseState == HosePhase.Held && HoseSeat == seat) return FuelTap.ReturnHose;
            if (FuelTruckAtStation && NozzleState == NozzlePhase.Held && NozzleSeat == seat) return FuelTap.InsertNozzle;
            if (NozzleState == NozzlePhase.OnTruck) return ValveOpen ? FuelTap.None : FuelTap.PullNozzle;
            if (HoseState == HosePhase.OnTruck && !FuelTruckAtStation && FuelTruckTank > 0f && !string.IsNullOrEmpty(dockFlightId) &&
                FlightNeedsFuel(dockFlightId))
                return FuelTap.TakeHose;
            return FuelTap.Drive;
        }

        /// <summary>
        /// 点飞机：油管接在该航班 → 断开（自动收回）；本席手持油管且该航班需油、油车停在机位 → 接管；否则无操作。
        /// </summary>
        public FuelTap ResolveAircraftTap(int seat, string flightId, bool fuelTruckAtDock)
        {
            if (!ValidSeat(seat) || IsDriving(seat) || string.IsNullOrEmpty(flightId)) return FuelTap.None;
            if (HoseState == HosePhase.OnAircraft) return HoseFlightId == flightId ? FuelTap.DetachHose : FuelTap.None;
            if (HoseState == HosePhase.Held && HoseSeat == seat && fuelTruckAtDock && FuelTruckTank > 0f &&
                FlightNeedsFuel(flightId))
                return FuelTap.AttachHose;
            return FuelTap.None;
        }

        /// <summary>执行一次已解析的燃油点按（Drive 由表现层占车，这里返回 false）。</summary>
        public bool ApplyFuelTap(int seat, FuelTap tap, string flightId, bool fuelTruckAtDock)
        {
            switch (tap)
            {
                case FuelTap.TakeNozzle: return TakeNozzle(seat);
                case FuelTap.ReturnNozzle:
                case FuelTap.PullNozzle: return ReturnNozzle(seat);
                case FuelTap.InsertNozzle: return AttachNozzle(seat, (int)ServiceKind.Fuel);
                case FuelTap.OpenValve: return SetValve(seat, true);
                case FuelTap.CloseValve: return SetValve(seat, false);
                case FuelTap.TakeHose: return TakeHose(seat);
                case FuelTap.ReturnHose: return ReturnHose(seat);
                case FuelTap.AttachHose: return AttachHose(seat, flightId, fuelTruckAtDock);
                case FuelTap.DetachHose: return DetachHose(seat);
                default: return false;
            }
        }

        /// <summary>徒步按住清理油渍（同帧一座一区；读条中断保留在油渍上）。</summary>
        public bool CleanSpill(int seat, int spillId, float dt)
        {
            if (Sim.Finished) return false;
            if (!ValidSeat(seat) || IsDriving(seat))
            {
                return false;
            }

            ShiftSpill spill = null;
            for (int i = 0; i < Spills.Count; i++)
            {
                if (Spills[i] != null && Spills[i].Id == spillId)
                {
                    spill = Spills[i];
                    break;
                }
            }

            if (spill == null)
            {
                return false;
            }

            if (spill.CleanSeat != -1 && spill.CleanSeat != seat)
            {
                return false;
            }

            spill.CleanSeat = seat;
            spill.CleanTouched = true;
            if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt))
            {
                return true;
            }

            spill.CleanWork += dt / SpillCleanSeconds;
            if (spill.CleanWork < 1f)
            {
                return true;
            }

            Spills.Remove(spill);
            Emit(ShiftEventTypes.SpillCleared, string.Empty, (int)ServiceKind.Fuel, seat,
                "油渍已清理 · 路面恢复", false);
            return true;
        }

        /// <summary>指定坐标是否落在任一处油渍打滑区内（表现层车辆打滑减速判定）。</summary>
        public bool SpillAt(float x, float z)
        {
            for (int i = 0; i < Spills.Count; i++)
            {
                ShiftSpill spill = Spills[i];
                if (spill == null)
                {
                    continue;
                }

                float dx = spill.X - x;
                float dz = spill.Z - z;
                float radius = spill.Radius > 0f ? spill.Radius : SpillRadius;
                if (dx * dx + dz * dz <= radius * radius)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>漫油 episode 生成一处油渍（同一次漫油被清理后阀门仍开着会无声重现）。</summary>
        void SpawnSpill(float x, float z)
        {
            if (Spills.Count >= MaxSpills)
            {
                return;
            }

            Spills.Add(new ShiftSpill
            {
                Id = ++nextSpillId,
                X = x,
                Z = z,
                Radius = SpillRadius,
                CleanWork = 0f,
                CleanSeat = -1,
                CleanTouched = false
            });
        }

        /// <summary>本次漫油 episode 是否仍有活油渍（清理后供 Tick 无声重现判定）。</summary>
        bool SpillZoneAlive(float x, float z)
        {
            for (int i = 0; i < Spills.Count; i++)
            {
                ShiftSpill spill = Spills[i];
                if (spill == null)
                {
                    continue;
                }

                float dx = spill.X - x;
                float dz = spill.Z - z;
                if (dx * dx + dz * dz <= 0.25f)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 机位侧交付：正确交付正常推进；行李送错时同样读条 2.5s，完成后货物被接收航班吞下，
        /// 并永久锁定该航班的行李任务。pressed 保留为兼容参数；失败只在读条完成时成立。
        /// </summary>
        public bool Deliver(int seat, int cartId, string dockFlightId, bool pressed, float dt)
        {
            if (Sim.Finished) return false;
            if (cartId < 0 || cartId >= CartCount || !Carts[cartId].Loaded)
            {
                return false;
            }

            ShiftCart cart = Carts[cartId];
            if (cart.Arrival)
            {
                if (pressed)
                {
                    Emit(ShiftEventTypes.ArrivalAtDock, cart.CargoFlightId, cartId, seat,
                        "这是到达行李，请先送回行李站。", false);
                }

                return false;
            }

            Flight flight = FindFlight(dockFlightId);
            if (flight == null || flight.Status != FlightStatus.Servicing || !StandReady(flight.Stand))
            {
                return false;
            }

            bool wrongFlight = cart.CargoFlightId != dockFlightId;

            if (wrongFlight && cartId != (int)ServiceKind.Baggage)
            {
                if (pressed)
                {
                    Emit(ShiftEventTypes.WrongDock, dockFlightId, cartId, seat,
                        "送错机位！车上是 " + cart.CargoFlightId + " 的货物。", false);
                }

                return false;
            }

            if (wrongFlight && flight.Progress[(int)ServiceKind.Baggage] >= 1f &&
                !IsTaskFailed(dockFlightId, ServiceKind.Baggage))
            {
                if (pressed)
                {
                    Emit(ShiftEventTypes.WrongDock, dockFlightId, cartId, seat,
                        dockFlightId + " 的行李任务已完成 · 货物仍在车上。", false);
                }

                return false;
            }

            if (cart.DeliverSeat != -1 && cart.DeliverSeat != seat)
            {
                return false;
            }

            if (cart.DeliverTouched && cart.DeliverFlightId != dockFlightId)
            {
                return false;
            }

            if (cart.DeliverFlightId != dockFlightId)
            {
                cart.DeliverWork = 0f;
                cart.DeliverFlightId = dockFlightId;
            }

            cart.DeliverTouched = true;
            cart.DeliverSeat = seat;
            if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt))
            {
                return true;
            }

            cart.DeliverWork += dt / DeliverSeconds;
            if (cart.DeliverWork < 1f)
            {
                return true;
            }

            // 重复交付防护（M3.1）：同一 (flightId,kind) 只记一次。读条完成时该航班此项
            // 已满（装载防护之外的兜底路径），本次交付忽略、货物回收、车辆可复用。
            if (flight != null && flight.Status == FlightStatus.Servicing && flight.Progress[cartId] >= 1f && !wrongFlight)
            {
                string ignoredFlight = cart.CargoFlightId;
                cart.Clear();
                Emit(ShiftEventTypes.DeliverIgnored, ignoredFlight, cartId, seat,
                    ignoredFlight + " 的" + TaskNames[cartId] + "已交付过 · 重复交付忽略", false);
                return true;
            }

            if (wrongFlight)
            {
                string cargoFlightId = cart.CargoFlightId;
                string message = dockFlightId + " 收到了 " + cargoFlightId + " 航班的行李 · 行李任务失败";
                cart.Clear();
                bool newlyFailed = Sim.FailTask(dockFlightId, ServiceKind.Baggage);
                if (newlyFailed)
                {
                    Failed.Add(AirportSimulation.TaskKey(dockFlightId, ServiceKind.Baggage));
                    ClearCargoForFailedBaggageTask(dockFlightId);
                }
                EmitWrongDelivery(dockFlightId, cargoFlightId, seat, message);
                if (newlyFailed)
                {
                    Emit(ShiftEventTypes.TaskFailed, dockFlightId, (int)ServiceKind.Baggage, seat, message, false);
                    ShiftEvent emitted = pending[pending.Count - 1];
                    emitted.RelatedFlightId = cargoFlightId;
                    pending[pending.Count - 1] = emitted;
                    eventHistory[eventHistory.Count - 1] = emitted;
                }

                return true;
            }

            bool advanced = Sim.TryAdvance(flight, (ServiceKind)cartId, 1f);
            cart.DeliverWork = 0f;
            if (advanced)
            {
                string doneFlight = cart.CargoFlightId;
                cart.Clear();
                Emit(ShiftEventTypes.Delivered, doneFlight, cartId, seat,
                    doneFlight + " · " + TaskNames[cartId] + "已完成", true);
            }

            return true;
        }

        /// <summary>开放登机：首次建立固定序号队伍；重开只继续放行现有队伍，不重生已登机旅客。</summary>
        public bool OpenGate(int seat, string flightId)
        {
            if (Sim.Finished) return false;
            if (!ValidSeat(seat) || IsDriving(seat) || string.IsNullOrEmpty(flightId) || Sim.Finished)
            {
                return false;
            }

            Flight flight = FindFlight(flightId);
            if (flight == null || flight.Status != FlightStatus.Servicing)
            {
                return false;
            }

            if (flight.Progress[(int)ServiceKind.Boarding] >= 1f || IsTaskFailed(flightId, ServiceKind.Boarding))
            {
                Emit(ShiftEventTypes.BoardingStopped, flightId, (int)ServiceKind.Boarding, seat,
                    flightId + " 已停止登机，旅客返回候机区。", false);
                return false;
            }

            if (boarding.Contains(flightId))
            {
                Emit(ShiftEventTypes.GateBusy, flightId, (int)ServiceKind.Boarding, seat,
                    "旅客正在前往 " + flightId + " · 人行道繁忙", false);
                return false;
            }

            if (flight.Stand < 0 || !StandReady(flight.Stand))
            {
                Emit(ShiftEventTypes.GateUnready, flightId, (int)ServiceKind.Boarding, seat,
                    "飞机还没停稳 · 稍后再放行旅客。", false);
                return false;
            }

            if (flight.Progress[0] < 1f || flight.Progress[2] < 1f ||
                IsTaskFailed(flightId, ServiceKind.Meals) || IsTaskFailed(flightId, ServiceKind.Fuel))
            {
                Emit(ShiftEventTypes.GatePrereq, flightId, (int)ServiceKind.Boarding, seat,
                    "先完成餐食与燃油，再开放登机。", false);
                return false;
            }

            boarding.Add(flightId);
            gateSeats[flightId] = seat;
            bool hasManifest = false;
            for (int i = 0; i < Passengers.Count; i++)
            {
                if (Passengers[i].FlightId == flightId) hasManifest = true;
            }
            // 进度通常只能由旅客到达增加；也兼容有预置登机进度的任务 fixture。
            int boardedCount = (int)Math.Ceiling(flight.Progress[(int)ServiceKind.Boarding] / BoardingPerPassenger);
            for (int i = boardedCount; !hasManifest && i < PassengersPerGate; i++)
            {
                Passengers.Add(new ShiftPassenger
                {
                    FlightId = flightId,
                    Seq = i,
                    Waypoint = 1,
                    Progress01 = 0f,
                    Delay = (i - boardedCount) * PassengerStagger,
                    Released = i == boardedCount,
                    Stand = flight.Stand
                });
            }

            Emit(ShiftEventTypes.GateOpened, flightId, (int)ServiceKind.Boarding, seat,
                flightId + " 开始登机 · 旅客沿人行道前往登机点", false);
            return true;
        }

        /// <summary>关门仅暂停未出发旅客；已经放行的旅客、航班登机进度与固定序号队伍全部保留。</summary>
        public bool CloseGate(int seat, string flightId)
        {
            if (Sim.Finished) return false;
            if (!ValidSeat(seat) || IsDriving(seat) || string.IsNullOrEmpty(flightId) ||
                !boarding.Contains(flightId)) return false;
            return CloseGateCore(seat, flightId);
        }

        bool CloseGateCore(int seat, string flightId)
        {
            if (!boarding.Remove(flightId)) return false;
            gateSeats.Remove(flightId);
            Emit(ShiftEventTypes.GateClosed, flightId, (int)ServiceKind.Boarding, seat,
                flightId + " 登机口已关闭 · 已放行旅客继续，登机进度保留", false);
            return true;
        }

        /// <summary>取消本席开放的登机口与当前读条；货物、燃油与已登机进度保留。</summary>
        public void Cancel(int seat)
        {
            if (Sim.Finished) return;
            if (!ValidSeat(seat)) return;
            List<string> gates = new List<string>();
            foreach (KeyValuePair<string, int> gate in gateSeats)
                if (gate.Value == seat) gates.Add(gate.Key);
            for (int i = 0; i < gates.Count; i++) CloseGateCore(seat, gates[i]);
            for (int i = 0; i < Carts.Length; i++)
            {
                ShiftCart cart = Carts[i];
                if (cart.DeliverSeat != seat && cartDrivers[i] != seat) continue;
                cart.DeliverWork = 0f;
                cart.DeliverFlightId = string.Empty;
                cart.DeliverTouched = false;
                cart.DeliverSeat = -1;
            }
        }

        /// <summary>
        /// 每 Step 交互结束后调用：未被本帧 Deliver/CleanSpill 续持的读条占用清零
        /// （旧取消语义：餐食/行李读条不保留、释放占用不复制资源；燃油储量/航班进度天然累计保留；
        /// 油渍清理读条保留在油渍上）。错过/离港航班的货物也在此回收。
        /// </summary>
        public void EndFrame()
        {
            CleanupOrphanedCargo();
            CleanupBoarding();
            for (int i = 0; i < Carts.Length; i++)
            {
                if (!Carts[i].DeliverTouched)
                {
                    Carts[i].DeliverWork = 0f;
                    Carts[i].DeliverFlightId = string.Empty;
                }

                Carts[i].DeliverTouched = false;
                Carts[i].DeliverSeat = -1;
            }

            for (int i = 0; i < Spills.Count; i++)
            {
                Spills[i].CleanTouched = false;
                Spills[i].CleanSeat = -1;
            }
        }

        // ---------- 计时 ----------

        /// <summary>推进航班事件、餐食倒计时、燃油加注/漫油与旅客行进。班岗结束即冻结（与旧 Step 顺序一致）。</summary>
        public void Tick(float dt) { Tick(dt, null); }

        /// <summary>工作、旅客到达与航班截止在同一权威时间片内裁定，恰好截止的完成有效。</summary>
        public void Tick(float dt, Action<float> work)
        {
            if (Sim.Finished || dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            if (scheduler != null) { scheduler.Pump(Sim); PruneFailuresForRemovedFlights(); }
            float remaining = Sim.Endless ? dt : Math.Min(dt, Sim.Remaining);
            while (remaining > 0f && !Sim.Finished)
            {
                float slice = Passengers.Count > 0 ? NextPassengerEvent(remaining) : remaining;
                float advanced = 0f;
                Sim.TickWithWork(slice, delegate(float step)
                {
                    advanced += step;
                    TickMeal(step);
                    TickFuel(step);
                    TickPassengers(step);
                    if (work != null) work(step);
                });
                CleanupBoarding();
                remaining = Math.Max(0f, remaining - advanced);
                if (advanced <= 0f) break;
            }
        }

        float NextPassengerEvent(float remaining)
        {
            float next = remaining;
            for (int i = 0; i < Passengers.Count; i++)
            {
                ShiftPassenger p = Passengers[i];
                float untilEvent;
                if (!p.Released)
                {
                    if (!boarding.Contains(p.FlightId)) continue;
                    untilEvent = Math.Max(0f, p.Delay);
                }
                else
                {
                    float distance = 0f;
                    int legs = PathLegCount(p.Stand);
                    if (p.Waypoint <= legs)
                    {
                        distance = Math.Max(0f, 1f - p.Progress01) * EffectiveLegLength(p.Stand, p.Waypoint);
                        for (int waypoint = p.Waypoint + 1; waypoint <= legs; waypoint++)
                            distance += EffectiveLegLength(p.Stand, waypoint);
                    }
                    untilEvent = distance / PassengerSpeed;
                }
                next = Math.Min(next, Math.Max(.0001f, untilEvent));
            }
            return next;
        }

        void PruneFailuresForRemovedFlights()
        {
            if (Failed.Count == 0) return;
            HashSet<string> liveFlightIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < Sim.Flights.Count; i++)
            {
                Flight flight = Sim.Flights[i];
                if (flight != null && !string.IsNullOrEmpty(flight.Id)) liveFlightIds.Add(flight.Id);
            }

            List<string> removed = new List<string>();
            foreach (string taskKey in Failed)
            {
                int separator = taskKey.LastIndexOf(':');
                string flightId = separator > 0 ? taskKey.Substring(0, separator) : string.Empty;
                if (!liveFlightIds.Contains(flightId)) removed.Add(taskKey);
            }

            if (removed.Count == 0) return;
            for (int i = 0; i < removed.Count; i++) Failed.Remove(removed[i]);
            Sim.ReplaceFailedTasks(Failed);
        }

        void TickMeal(float dt)
        {
            if (Meal != MealPhase.Ordered)
            {
                return;
            }

            MealProgress01 += dt / MealPrepareSeconds;
            if (MealProgress01 >= 1f)
            {
                MealProgress01 = 1f;
                Meal = MealPhase.Ready;
                Emit(ShiftEventTypes.MealReady, string.Empty, (int)ServiceKind.Meals, -1,
                    "餐食备好了 · 出货口被挡住了，开餐车来取吧！", false);
            }
        }

        /// <summary>
        /// 燃油 Tick（M3.3r）：站内阀门开 + 油枪插车 + 油箱未满 → 油车储量上升；阀门开且油枪未插车 → 立即漫油；
        /// 油箱满仍开阀 → 满溢计时达 OverfillGraceSeconds 后漫油。机位侧由 TickHose 自动转移/满溢。
        /// 漫油不消耗站内储备与油车储量、不扣分。
        /// </summary>
        void TickFuel(float dt)
        {
            TickStation(dt);
            TickHose(dt);
        }

        void TickStation(float dt)
        {
            if (!ValveOpen)
            {
                StationOverfill = 0f;
                spilling = false;
                return;
            }

            bool inserted = NozzleState == NozzlePhase.OnTruck && FuelTruckAtStation;
            if (inserted && FuelTruckTank < 1f)
            {
                StationOverfill = 0f;
                spilling = false;
                FuelTruckTank = Math.Min(1f, FuelTruckTank + dt / StationFillSeconds);
                return;
            }

            if (inserted)
            {
                StationOverfill += dt;
                if (StationOverfill < OverfillGraceSeconds)
                {
                    return;
                }
            }

            if (!spilling)
            {
                spilling = true;
                SpawnSpill(spillX, spillZ);
                Emit(ShiftEventTypes.SpillStarted, string.Empty, (int)ServiceKind.Fuel, -1,
                    inserted ? "油车已满仍开阀 · 漫油了！立即关阀，徒步按住清理油渍"
                             : "漫油了！路面打滑 · 徒步按住清理油渍", false);
                return;
            }

            // 同一次漫油中油渍被清理但阀门仍开着：油渍无声重现（不重复发事件）。
            if (!SpillZoneAlive(spillX, spillZ))
            {
                SpawnSpill(spillX, spillZ);
            }
        }

        void TickPassengers(float dt)
        {
            for (int i = Passengers.Count - 1; i >= 0; i--)
            {
                ShiftPassenger p = Passengers[i];
                Flight flight = FindFlight(p.FlightId);
                if (flight == null || flight.Status != FlightStatus.Servicing)
                {
                    Passengers.RemoveAt(i);
                    continue;
                }

                float walkingTime = dt;
                if (!p.Released)
                {
                    if (!boarding.Contains(p.FlightId)) continue;
                    if (p.Delay > dt)
                    {
                        p.Delay -= dt;
                        continue;
                    }
                    walkingTime = Math.Max(0f, dt - p.Delay);
                    p.Delay = 0f;
                    p.Released = true;
                    int releasingSeat; p.Seat=gateSeats.TryGetValue(p.FlightId,out releasingSeat)?releasingSeat:-1;
                }

                // 消耗本步剩余时间，可跨越多段路线；错峰延迟不计入步行时间。
                int pathLegs = PathLegCount(p.Stand);
                while (p.Waypoint <= pathLegs)
                {
                    float length = EffectiveLegLength(p.Stand, p.Waypoint);
                    float toWaypoint = Math.Max(0f, 1f - p.Progress01) * length / PassengerSpeed;
                    if (walkingTime + .000001f < toWaypoint)
                    {
                        p.Progress01 += walkingTime * PassengerSpeed / length;
                        break;
                    }
                    walkingTime = Math.Max(0f, walkingTime - toWaypoint);
                    p.Waypoint++;
                    p.Progress01 = 0f;
                }
                if (p.Waypoint <= pathLegs) continue;

                // 到达登机点：逐个登机累计；停止登机（进度已满/航班不可操作）时提示返回。
                if (!Sim.TryAdvance(flight, ServiceKind.Boarding, BoardingPerPassenger))
                {
                    Emit(ShiftEventTypes.BoardingStopped, p.FlightId, (int)ServiceKind.Boarding, -1,
                        p.FlightId + " 已停止登机，旅客返回候机区。", false);
                }
                else if (flight.Progress[(int)ServiceKind.Boarding] >= 1f)
                {
                    int completingSeat=p.Seat;
                    boarding.Remove(p.FlightId);
                    gateSeats.Remove(p.FlightId);
                    Emit(ShiftEventTypes.Delivered, p.FlightId, (int)ServiceKind.Boarding, completingSeat,
                        p.FlightId + " · 登机已完成", true);
                }

                Passengers.RemoveAt(i);
            }
        }

        void CleanupBoarding()
        {
            List<string> staleGates = new List<string>();
            foreach (string flightId in boarding)
            {
                Flight flight = FindFlight(flightId);
                if (Sim.Finished || flight == null || flight.Status != FlightStatus.Servicing ||
                    flight.Progress[(int)ServiceKind.Boarding] >= 1f || IsTaskFailed(flightId, ServiceKind.Boarding))
                    staleGates.Add(flightId);
            }
            for (int i = 0; i < staleGates.Count; i++)
            {
                boarding.Remove(staleGates[i]);
                gateSeats.Remove(staleGates[i]);
            }
            for (int i = Passengers.Count - 1; i >= 0; i--)
            {
                Flight flight = FindFlight(Passengers[i].FlightId);
                if (Sim.Finished || flight == null || flight.Status != FlightStatus.Servicing ||
                    flight.Progress[(int)ServiceKind.Boarding] >= 1f || IsTaskFailed(flight.Id, ServiceKind.Boarding))
                    Passengers.RemoveAt(i);
            }
        }

        /// <summary>错过/离港航班不永久占用共享车辆；接在已离场飞机上的车载油管自动收回油车（装备不消失不复制）。</summary>
        void CleanupOrphanedCargo()
        {
            for (int i = 0; i < Carts.Length; i++)
            {
                if (!Carts[i].Loaded)
                {
                    continue;
                }

                Flight flight = FindFlight(Carts[i].CargoFlightId);
                if (flight == null || flight.Status != FlightStatus.Servicing)
                {
                    Carts[i].Clear();
                }
            }

            if (HoseState == HosePhase.OnAircraft)
            {
                Flight anchor = FindFlight(HoseFlightId);
                if (anchor == null || anchor.Status != FlightStatus.Servicing)
                {
                    ReelHose();
                }
            }
        }

        bool ValidCart(int cartId, ServiceKind kind)
        {
            return cartId == (int)kind && cartId >= 0 && cartId < CartCount;
        }

        void ClearCargoForFailedBaggageTask(string flightId)
        {
            int baggageCartId = (int)ServiceKind.Baggage;
            ShiftCart cart = Carts[baggageCartId];
            if (cart.Loaded && !cart.Arrival && cart.CargoFlightId == flightId)
            {
                cart.Clear();
            }
        }

        /// <summary>旧 Load() 判定：航班须服务中且该项未完成；失败时统一归因文案。</summary>
        bool LoadOnto(int cartId, Flight flight, int seat)
        {
            if (flight == null || flight.Status != FlightStatus.Servicing ||
                flight.Progress[cartId] >= 1f || IsTaskFailed(flight.Id, (ServiceKind)cartId))
            {
                Emit(ShiftEventTypes.LoadRefused, flight == null ? string.Empty : flight.Id, cartId, seat,
                    flight != null && IsTaskFailed(flight.Id, (ServiceKind)cartId)
                        ? flight.Id + " 的" + TaskNames[cartId] + "任务已失败 · 无法再次装载"
                        : "该航班此项已完成 · 请切换目标航班", false);
                return false;
            }

            Carts[cartId].CargoFlightId = flight.Id;
            Carts[cartId].Arrival = false;
            Carts[cartId].DeliverWork = 0f;
            Carts[cartId].DeliverFlightId = string.Empty;
            Emit(ShiftEventTypes.CartLoaded, flight.Id, cartId, seat, string.Empty, false);
            return true;
        }

        // ---------- 快照 ----------

        /// <summary>抓取当前任务域状态（Ver=2 Snapshot.Shift 块）。</summary>
        public ShiftSnap CaptureShift()
        {
            ShiftSnap snap = new ShiftSnap();
            snap.NextEventSequence = nextEventSequence;
            snap.NextSpillId = nextSpillId;
            snap.FuelTruckAtStation = fuelTruckAtStation;
            snap.CartDrivers = (int[])cartDrivers.Clone();
            snap.HumanTaskCounts = (int[])HumanTaskCounts.Clone();
            snap.MealPhase = (int)Meal;
            snap.MealProgress = MealProgress01;
            snap.FuelTruckTank = FuelTruckTank;
            snap.NozzleState = (int)NozzleState;
            snap.NozzleSeat = NozzleSeat;
            snap.HoseState = (int)HoseState;
            snap.HoseSeat = HoseSeat;
            snap.HoseFlightId = HoseFlightId ?? string.Empty;
            snap.ValveOpen = ValveOpen;
            snap.StationOverfill = StationOverfill;
            snap.HoseOverfill = HoseOverfill;
            snap.Spilling = spilling;
            snap.HoseSpilling = hoseSpilling;
            snap.Spills = new SpillSnap[Spills.Count];
            for (int i = 0; i < Spills.Count; i++)
            {
                ShiftSpill source = Spills[i];
                snap.Spills[i] = new SpillSnap
                {
                    Id = source.Id,
                    X = source.X,
                    Z = source.Z,
                    Radius = source.Radius,
                    CleanWork = source.CleanWork
                };
            }
            for (int i = 0; i < Carts.Length && i < snap.Carts.Length; i++)
            {
                snap.Carts[i].FlightId = Carts[i].CargoFlightId;
                snap.Carts[i].Arrival = Carts[i].Arrival;
                snap.Carts[i].DeliverWork = Carts[i].DeliverWork;
                snap.Carts[i].DeliverFlightId = Carts[i].DeliverFlightId;
            }

            List<string> gateList = new List<string>(boarding);
            gateList.Sort(StringComparer.Ordinal);
            snap.Boarding = gateList.ToArray();
            snap.GateSeats = new int[gateList.Count];
            for (int i=0;i<gateList.Count;i++) { int owner; snap.GateSeats[i] = gateSeats.TryGetValue(gateList[i],out owner) ? owner : -1; }
            snap.Passengers = new PassengerSnap[Passengers.Count];
            for (int i = 0; i < Passengers.Count; i++)
            {
                ShiftPassenger p = Passengers[i];
                snap.Passengers[i] = new PassengerSnap
                {
                    FlightId = p.FlightId,
                    Seq = p.Seq,
                    Stand = p.Stand,
                    Waypoint = p.Waypoint,
                    Progress01 = p.Progress01,
                    Delay = p.Delay,
                    Released = p.Released,
                    Seat = p.Seat
                };
            }

            snap.Failed = new List<string>(Failed).ToArray();
            snap.Events = new ShiftEventSnap[eventHistory.Count];
            for (int i = 0; i < eventHistory.Count; i++)
            {
                ShiftEvent source = eventHistory[i];
                snap.Events[i] = new ShiftEventSnap
                {
                    Sequence = source.Sequence,
                    Time = source.Time,
                    Type = source.Type,
                    FlightId = source.FlightId,
                    RelatedFlightId = source.RelatedFlightId,
                    Kind = source.Kind,
                    Seat = source.Seat,
                    Text = source.Text,
                    Bell = source.Bell
                };
            }
            for (int i = 0; i < standReady.Length && i < snap.StandReady.Length; i++)
            {
                snap.StandReady[i] = standReady[i];
            }

            return snap;
        }

        /// <summary>抓取航班镜像块（id/status/stand/progress[4]/arrival/deadline）。</summary>
        public FlightSnap[] CaptureFlights()
        {
            FlightSnap[] result = new FlightSnap[Sim.Flights.Count];
            for (int i = 0; i < Sim.Flights.Count; i++)
            {
                Flight flight = Sim.Flights[i];
                result[i] = new FlightSnap
                {
                    Id = flight.Id,
                    Status = (int)flight.Status,
                    Stand = flight.Stand,
                    Progress = (float[])flight.Progress.Clone(),
                    Arrival = flight.ArrivalTime,
                    Deadline = flight.Deadline,
                    ArrivalBagsReturned = flight.ArrivalBagsReturned,
                    PlaneAssignedAt = flight.AssignedAt
                };
            }

            return result;
        }

        /// <summary>恢复可继续运行的有限班岗；不产生事件、不重新计分。镜像和提升权威都复用。</summary>
        public void RestoreCheckpoint(ShiftSnap snap, FlightSnap[] flights, int score, float elapsed,
            bool ended, int departed, int missed, int completedTasks)
        {
            ApplyMirror(snap, flights, score, elapsed);
            scheduler = null;
            Sim.RestoreState(elapsed, ended, false, score, departed, missed, completedTasks);
            for (int i=0;i<Carts.Length;i++) { Carts[i].DeliverSeat = -1; Carts[i].DeliverTouched = false; }
        }

        /// <summary>client 只读镜像：逐快照整块覆盖。镜像端不得调用意图 API 与 Tick。</summary>
        public void ApplyMirror(ShiftSnap snap, FlightSnap[] flights, int score, float elapsed)
        {
            if (snap == null)
            {
                snap = new ShiftSnap();
            }

            nextSpillId = snap.NextSpillId;
            fuelTruckAtStation = snap.FuelTruckAtStation;
            for (int i=0;i<cartDrivers.Length;i++) cartDrivers[i] = snap.CartDrivers != null && i<snap.CartDrivers.Length ? snap.CartDrivers[i] : -1;
            for (int i=0;i<HumanTaskCounts.Length;i++) HumanTaskCounts[i] = snap.HumanTaskCounts != null && i<snap.HumanTaskCounts.Length ? snap.HumanTaskCounts[i] : 0;
            Meal = (MealPhase)snap.MealPhase;
            MealProgress01 = snap.MealProgress;
            FuelTruckTank = snap.FuelTruckTank;
            NozzleState = (NozzlePhase)snap.NozzleState;
            NozzleSeat = snap.NozzleSeat;
            HoseState = (HosePhase)snap.HoseState;
            HoseSeat = snap.HoseSeat;
            HoseFlightId = snap.HoseFlightId ?? string.Empty;
            ValveOpen = snap.ValveOpen;
            StationOverfill = snap.StationOverfill;
            HoseOverfill = snap.HoseOverfill;
            spilling = snap.Spilling;
            hoseSpilling = snap.HoseSpilling;
            Spills.Clear();
            if (snap.Spills != null)
            {
                for (int i = 0; i < snap.Spills.Length; i++)
                {
                    SpillSnap source = snap.Spills[i];
                    if (source == null)
                    {
                        continue;
                    }

                    Spills.Add(new ShiftSpill
                    {
                        Id = source.Id,
                        X = source.X,
                        Z = source.Z,
                        Radius = source.Radius,
                        CleanWork = source.CleanWork,
                        CleanSeat = -1,
                        CleanTouched = false
                    });
                    if (source.Id > nextSpillId)
                    {
                        nextSpillId = source.Id;
                    }
                }
            }
            for (int i = 0; i < Carts.Length && i < snap.Carts.Length; i++)
            {
                ShiftCartSnap source = snap.Carts[i];
                Carts[i].CargoFlightId = source == null ? string.Empty : source.FlightId ?? string.Empty;
                Carts[i].Arrival = source != null && source.Arrival;
                Carts[i].DeliverWork = source == null ? 0f : source.DeliverWork;
                Carts[i].DeliverFlightId = source == null ? string.Empty : source.DeliverFlightId ?? string.Empty;
                Carts[i].DeliverTouched = false;
            }

            boarding.Clear();
            gateSeats.Clear();
            if (snap.Boarding != null)
            {
                for (int i = 0; i < snap.Boarding.Length; i++)
                {
                    if (!string.IsNullOrEmpty(snap.Boarding[i]))
                    {
                        boarding.Add(snap.Boarding[i]);
                        gateSeats[snap.Boarding[i]] = snap.GateSeats != null && i < snap.GateSeats.Length ? snap.GateSeats[i] : -1;
                    }
                }
            }

            Passengers.Clear();
            if (snap.Passengers != null)
            {
                for (int i = 0; i < snap.Passengers.Length; i++)
                {
                    PassengerSnap source = snap.Passengers[i];
                    if (source == null)
                    {
                        continue;
                    }

                    Passengers.Add(new ShiftPassenger
                    {
                        FlightId = source.FlightId ?? string.Empty,
                        Seq = source.Seq,
                        Stand = source.Stand,
                        Waypoint = source.Waypoint,
                        Progress01 = source.Progress01,
                        Delay = source.Delay,
                        Released = source.Released,
                        Seat = source.Seat
                    });
                }
            }

            Failed.Clear();
            if (snap.Failed != null)
            {
                for (int i = 0; i < snap.Failed.Length; i++)
                {
                    if (!string.IsNullOrEmpty(snap.Failed[i]))
                    {
                        Failed.Add(snap.Failed[i]);
                    }
                }
            }

            eventHistory.Clear();
            pending.Clear();
            nextEventSequence = snap.NextEventSequence;
            if (snap.Events != null)
            {
                int start = Math.Max(0, snap.Events.Length - EventHistoryLimit);
                for (int i = start; i < snap.Events.Length; i++)
                {
                    ShiftEventSnap source = snap.Events[i];
                    if (source == null)
                    {
                        continue;
                    }

                    ShiftEvent mirrored = new ShiftEvent
                    {
                        Sequence = source.Sequence,
                        Time = source.Time,
                        Type = source.Type ?? string.Empty,
                        FlightId = source.FlightId ?? string.Empty,
                        RelatedFlightId = source.RelatedFlightId ?? string.Empty,
                        Kind = source.Kind,
                        Seat = source.Seat,
                        Text = source.Text ?? string.Empty,
                        Bell = source.Bell
                    };
                    eventHistory.Add(mirrored);
                    if (mirrored.Sequence > nextEventSequence)
                    {
                        nextEventSequence = mirrored.Sequence;
                    }
                }
            }

            for (int i = 0; i < standReady.Length && i < snap.StandReady.Length; i++)
            {
                standReady[i] = snap.StandReady[i];
            }

            MirrorScore = score;
            MirrorElapsed = elapsed;
            Sim.Flights.Clear();
            Sim.ReplaceFailedTasks(Failed);
            if (flights == null)
            {
                Sim.ReplaceFailedTasks(Failed);
                return;
            }

            for (int i = 0; i < flights.Length; i++)
            {
                FlightSnap source = flights[i];
                if (source == null)
                {
                    continue;
                }

                float[] progress = source.Progress != null && source.Progress.Length == 4
                    ? (float[])source.Progress.Clone()
                    : new float[4];
                Flight mirror = new Flight(source.Id ?? string.Empty, source.Arrival, source.Deadline)
                {
                    Status = (FlightStatus)source.Status,
                    Progress = progress,
                    ArrivalBagsReturned = source.ArrivalBagsReturned,
                    AssignedAt = source.PlaneAssignedAt
                };
                mirror.Stand = source.Stand;
                Sim.Flights.Add(mirror);
            }

            Sim.ReplaceFailedTasks(Failed);
        }
    }
}
