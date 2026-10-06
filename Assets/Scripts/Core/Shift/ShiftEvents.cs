using System;

namespace IslandAirport
{
    /// <summary>ShiftSim 意图/计时产生的一条领域事件。Text 为可直接播出的 toast 文案
    /// （行为保持：与 M3.0 之前 AirportGame 内联 Notify 的原文逐字一致）；Bell 标记送达提示音。</summary>
    public struct ShiftEvent
    {
        /// <summary>单局内单调递增的事件序号，网络镜像与埋点去重共用。</summary>
        public int Sequence;
        public float Time;
        public string Type;
        public string FlightId;
        /// <summary>关联航班；错送事件中为货物原属航班。</summary>
        public string RelatedFlightId;
        /// <summary>ServiceKind 的 int，无关联为 -1。</summary>
        public int Kind;
        /// <summary>触发席位，无关联为 -1。</summary>
        public int Seat;
        public string Text;
        public bool Bell;
    }

    /// <summary>事件类型常量。供 core、HUD、LAN 镜像与本地回放统一使用。</summary>
    public static class ShiftEventTypes
    {
        public const string MealOrdered = "meal_ordered";
        public const string MealReady = "meal_ready";
        public const string MealTaken = "meal_taken";
        public const string MealBlocked = "meal_blocked";
        public const string CartLoaded = "cart_loaded";
        public const string LoadRefused = "load_refused";
        public const string ArrivalPicked = "arrival_picked";
        public const string ArrivalReturned = "arrival_returned";
        public const string ArrivalRecycled = "arrival_recycled";
        public const string ArrivalAtDock = "arrival_at_dock";
        /// <summary>wrong_dock — 不满足 M3.2 错送条件时的可纠正提示，不会吞货或失败。</summary>
        public const string WrongDock = "wrong_dock";
        /// <summary>delivered:flight:kind — 交付完成（航班进度 +1 项）。</summary>
        public const string Delivered = "delivered";
        /// <summary>deliver_ignored:flight:kind — 重复交付防护：同一 (flightId,kind) 只记一次，
        /// 读条完成时该航班此项已满即忽略并回收货物（M3.1）。</summary>
        public const string DeliverIgnored = "deliver_ignored";
        /// <summary>wrong_delivery:receiver — 错送交付完成，FlightId 是误接收航班。</summary>
        public const string WrongDelivery = "wrong_delivery";
        public const string GateOpened = "gate_opened";
        public const string GateClosed = "gate_closed";
        public const string GateBusy = "gate_busy";
        public const string GateUnready = "gate_unready";
        public const string GatePrereq = "gate_prereq";
        public const string BoardingStopped = "boarding_stopped";
        /// <summary>task_failed:flight:kind — 任务永久失败，FlightId 是失败航班。</summary>
        public const string TaskFailed = "task_failed";
        // M3.3r 燃油事件：站内油枪（nozzle_*）与车载油管（hose_*）分开。
        /// <summary>nozzle_taken — 油站拿起站内油枪。</summary>
        public const string NozzleTaken = "nozzle_taken";
        /// <summary>nozzle_attached — 站内油枪插入站边油车。</summary>
        public const string NozzleAttached = "nozzle_attached";
        /// <summary>nozzle_returned — 站内油枪归位（手持归还，或关阀时自动从油车归位）。</summary>
        public const string NozzleReturned = "nozzle_returned";
        /// <summary>nozzle_busy — 油枪/油管被他人持有（占用竞争提示）。</summary>
        public const string NozzleBusy = "nozzle_busy";
        /// <summary>nozzle_refused — 燃油操作被拒（驾驶中、油车不在位、没油等）。</summary>
        public const string NozzleRefused = "nozzle_refused";
        /// <summary>hose_taken — 从油车取出车载油管。</summary>
        public const string HoseTaken = "hose_taken";
        /// <summary>hose_attached — 车载油管接到飞机（FlightId 为所接航班），开始自动加注。</summary>
        public const string HoseAttached = "hose_attached";
        /// <summary>hose_detached — 从飞机断开，油管自动收回油车（FlightId 为原所接航班）。</summary>
        public const string HoseDetached = "hose_detached";
        /// <summary>hose_returned — 手持油管点油车收回（取消）。</summary>
        public const string HoseReturned = "hose_returned";
        public const string ValveOpened = "valve_opened";
        public const string ValveClosed = "valve_closed";
        /// <summary>spill_started — 漫油处罚事件：阀门开着且油枪未插车（立即）/油箱满仍开阀或飞机满仍接管（2 秒宽限后），不扣分不判失败。</summary>
        public const string SpillStarted = "spill_started";
        public const string SpillCleared = "spill_cleared";
        /// <summary>fuel_delivered — 机位加注完成（delivered 事件的燃油通道，本地 JSONL 埋点名）。</summary>
        public const string FuelDelivered = "fuel_delivered";
    }
}
