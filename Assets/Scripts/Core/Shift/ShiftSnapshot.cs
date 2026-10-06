using System;

namespace IslandAirport
{
    /// <summary>快照里一辆车的任务域状态。FlightId 为空 = 空车（未装载）。</summary>
    public sealed class ShiftCartSnap
    {
        public string FlightId = string.Empty;
        public bool Arrival;
        public float DeliverWork;
        public string DeliverFlightId = string.Empty;
    }

    /// <summary>快照里一名旅客的参数化状态（不含世界坐标；表现层用 PavementPath 还原）。</summary>
    public sealed class PassengerSnap
    {
        public string FlightId = string.Empty;
        public int Seq;
        public int Stand;
        public int Waypoint = 1;
        public float Progress01;
        public float Delay;
        /// <summary>已离开候机队伍；关门后仍沿路线行进，只有已放行旅客阻挡道路车辆。</summary>
        public bool Released;
        public int Seat = -1;
    }

    /// <summary>领域事件镜像：保留近期归因，供 LAN 快照与本地回放读取。</summary>
    public sealed class ShiftEventSnap
    {
        public int Sequence;
        public float Time;
        public string Type = string.Empty;
        public string FlightId = string.Empty;
        public string RelatedFlightId = string.Empty;
        public int Kind = -1;
        public int Seat = -1;
        public string Text = string.Empty;
        public bool Bell;
    }

    /// <summary>快照里一处漫油油渍区（纯数据；client 镜像渲染打滑区与清理读条）。</summary>
    public sealed class SpillSnap
    {
        public int Id;
        public float X;
        public float Z;
        public float Radius;
        public float CleanWork;
    }

    /// <summary>快照里一个航班的镜像数据（对应 AirportSimulation.Flight 的公开可读字段）。</summary>
    public sealed class FlightSnap
    {
        public string Id = string.Empty;
        public int Status;
        public int Stand = -1;
        public float[] Progress = new float[4];
        public float Arrival;
        public float Deadline;
        public bool ArrivalBagsReturned;
        public float PlaneAssignedAt;
    }

    /// <summary>
    /// ShiftSim 的可序列化镜像块（纯数据，无 AirportSimulation/Unity 依赖）。
    /// 并入 NetProtocol.Snapshot（Ver=2）。sandbox 对局恒为空态默认值。
    /// M3.3r 燃油块：油车储量/站内油枪与车载油管两套状态机/阀门/两路满溢计时/油渍区列表；
    /// 站内储备无限不设上限（不再进快照）；Failed 与近期 Events 记录真实失败状态。
    /// </summary>
    public sealed class ShiftSnap
    {
        public const int CartCount = 3;
        public const int StandCount = 3;

        /// <summary>0 Idle / 1 Ordered / 2 Ready（对应 ShiftSim.MealPhase）。</summary>
        public int NextEventSequence;
        public int NextSpillId;
        public bool FuelTruckAtStation = true;
        public int[] CartDrivers = { -1, -1, -1 };
        public int[] GateSeats = new int[0];
        public int[] HumanTaskCounts = new int[4];
        public int MealPhase;
        public float MealProgress;
        public float FuelTruckTank;
        /// <summary>站内油枪：0 AtStation / 1 Held / 2 OnTruck（对应 NozzlePhase；M3.3r 起永不离站）。</summary>
        public int NozzleState;
        public int NozzleSeat = -1;
        /// <summary>车载油管：0 OnTruck / 1 Held / 2 OnAircraft（对应 HosePhase）。</summary>
        public int HoseState;
        /// <summary>手持或接管席位（OnTruck 时 -1）。</summary>
        public int HoseSeat = -1;
        /// <summary>车载油管所接航班号（HoseState==2 时有效）。</summary>
        public string HoseFlightId = string.Empty;
        public bool ValveOpen;
        /// <summary>油车满箱仍开阀的满溢计时（秒，达 ShiftSim.OverfillGraceSeconds 起漫油）。</summary>
        public float StationOverfill;
        /// <summary>飞机已满仍接管的满溢计时（秒）。</summary>
        public float HoseOverfill;
        /// <summary>站内来源（阀门）当前继续漫油（可能清理后仍开阀，但当下油渍被移除）。</summary>
        public bool Spilling;
        /// <summary>机位来源（飞机满溢仍接管）当前继续漫油。</summary>
        public bool HoseSpilling;
        public SpillSnap[] Spills = new SpillSnap[0];
        public ShiftCartSnap[] Carts = new ShiftCartSnap[CartCount];
        public string[] Boarding = new string[0];
        public PassengerSnap[] Passengers = new PassengerSnap[0];
        /// <summary>"flightId:kind" 永久失败集合。</summary>
        public string[] Failed = new string[0];
        /// <summary>近期领域事件（用于错送归因、镜像与回放）；最多保留 ShiftSim.EventHistoryLimit 条。</summary>
        public ShiftEventSnap[] Events = new ShiftEventSnap[0];
        public bool[] StandReady = new bool[StandCount];

        public ShiftSnap()
        {
            for (int i = 0; i < Carts.Length; i++)
            {
                Carts[i] = new ShiftCartSnap();
            }

            for (int i = 0; i < StandReady.Length; i++)
            {
                StandReady[i] = true;
            }
        }
    }
}
