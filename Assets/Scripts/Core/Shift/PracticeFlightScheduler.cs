using System;
using System.Collections.Generic;

namespace IslandAirport
{
    /// <summary>
    /// LAN 任务练习模式的循环时刻表航班生成器（M3.1，纯 C# 零 Unity 依赖）。
    /// 每个 cycle（默认 240s）按既定 offset 放出航班；cycle 循环时航班号数字 +1，
    /// 保证稳定 ID flightId 跨 cycle 唯一（重复交付防护以 (flightId,ServiceKind) 计）。
    /// 提前 LeadTime 秒把航班放进 AirportSimulation.Flights（Scheduled，HUD 可见即将到达）；
    /// 已离港/错过的航班只保留最近 KeepFinished 架，航班表与快照体量有界。
    /// 与 AirportSimulation 的关系：生成器只 Add/Remove 航班对象；到场分配机位、
    /// 截止错过、离港判定仍全部由 AirportSimulation.Tick 处理（Endless 不做 300s 结算）。
    /// 仅 host/本地权威端挂载（ShiftSim.AttachScheduler）；client 镜像只读、永不 Pump。
    /// </summary>
    public sealed class PracticeFlightScheduler
    {
        public struct Entry
        {
            /// <summary>航班号字母段（如 "UBA"）。</summary>
            public string Code;
            /// <summary>首个 cycle 的航班号数字段；之后每 cycle +1。</summary>
            public int Number;
            public string Destination;
            /// <summary>cycle 内到场时刻（秒）。</summary>
            public float Offset;
            /// <summary>服务窗口（秒）。</summary>
            public float Window;
        }

        public const float DefaultLeadTime = 8f;
        public const int DefaultKeepFinished = 3;

        readonly Entry[] entries;
        readonly float cycleLength;
        readonly float leadTime;
        readonly int keepFinished;
        int nextIndex;
        int nextCycle;

        public PracticeFlightScheduler(Entry[] entries, float cycleLength, float leadTime, int keepFinished)
        {
            if (entries == null || entries.Length == 0)
            {
                throw new ArgumentException("practice timetable must not be empty", "entries");
            }

            if (cycleLength <= 0f || float.IsNaN(cycleLength) || float.IsInfinity(cycleLength))
            {
                throw new ArgumentOutOfRangeException("cycleLength");
            }

            this.entries = (Entry[])entries.Clone();
            Array.Sort(this.entries, delegate (Entry a, Entry b) { return a.Offset.CompareTo(b.Offset); });
            for (int i = 0; i < this.entries.Length; i++)
            {
                Entry e = this.entries[i];
                if (string.IsNullOrEmpty(e.Code) || e.Offset < 0f || e.Window <= 0f ||
                    e.Offset + e.Window > cycleLength)
                {
                    throw new ArgumentException("practice timetable entry outside its cycle: " + e.Code);
                }
            }

            this.cycleLength = cycleLength;
            this.leadTime = leadTime < 0f ? 0f : leadTime;
            this.keepFinished = keepFinished < 0 ? 0 : keepFinished;
        }

        public float CycleLength { get { return cycleLength; } }

        public float LeadTime { get { return leadTime; } }

        /// <summary>默认时刻表：4 班/240s，75s 服务窗（沿用本地班岗节奏）；前三班形成三机位并波。</summary>
        public static PracticeFlightScheduler CreateDefault()
        {
            return new PracticeFlightScheduler(new[]
            {
                new Entry { Code = "UBA", Number = 826, Destination = "北湾", Offset = 0f, Window = 75f },
                new Entry { Code = "QMR", Number = 152, Destination = "珊瑚角", Offset = 35f, Window = 75f },
                new Entry { Code = "AZU", Number = 407, Destination = "棕榈滩", Offset = 70f, Window = 75f },
                new Entry { Code = "GLO", Number = 219, Destination = "蓝泻湖", Offset = 140f, Window = 75f }
            }, 240f, DefaultLeadTime, DefaultKeepFinished);
        }

        /// <summary>把 lead 窗口内到场的航班放进 sim.Flights；每次 Pump 收尾修剪历史航班。</summary>
        public void Pump(AirportSimulation sim)
        {
            if (sim == null || sim.Finished)
            {
                return;
            }

            float horizon = sim.Elapsed + leadTime;
            while (NextArrival() <= horizon)
            {
                Entry e = entries[nextIndex];
                float arrival = NextArrival();
                sim.Flights.Add(new Flight(e.Code + (e.Number + nextCycle), e.Destination, arrival, arrival + e.Window));
                Advance();
            }

            Prune(sim);
        }

        float NextArrival()
        {
            return nextCycle * cycleLength + entries[nextIndex].Offset;
        }

        void Advance()
        {
            nextIndex++;
            if (nextIndex >= entries.Length)
            {
                nextIndex = 0;
                nextCycle++;
            }
        }

        /// <summary>离港/错过航班只保留最近 keepFinished 架（按截止时刻淘汰最旧），服务中/候机航班不动。</summary>
        void Prune(AirportSimulation sim)
        {
            int finished = 0;
            for (int i = 0; i < sim.Flights.Count; i++)
            {
                FlightStatus status = sim.Flights[i].Status;
                if (status == FlightStatus.Departed || status == FlightStatus.Missed)
                {
                    finished++;
                }
            }

            while (finished > keepFinished)
            {
                int oldest = -1;
                for (int i = 0; i < sim.Flights.Count; i++)
                {
                    Flight flight = sim.Flights[i];
                    if (flight.Status != FlightStatus.Departed && flight.Status != FlightStatus.Missed)
                    {
                        continue;
                    }

                    if (oldest < 0 || flight.Deadline < sim.Flights[oldest].Deadline)
                    {
                        oldest = i;
                    }
                }

                if (oldest < 0)
                {
                    return;
                }

                sim.Flights.RemoveAt(oldest);
                finished--;
            }
        }
    }
}
