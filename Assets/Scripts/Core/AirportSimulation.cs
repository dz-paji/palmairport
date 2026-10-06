using System;
using System.Collections.Generic;

namespace IslandAirport
{
    /// <summary>
    /// The four pieces of ground service that a flight needs before it can leave.
    /// The enum order is also the order used by Flight.Progress.
    /// </summary>
    public enum ServiceKind
    {
        Meals = 0,
        Baggage = 1,
        Fuel = 2,
        Boarding = 3
    }

    public enum FlightStatus
    {
        Scheduled,
        Servicing,
        Departed,
        Missed
    }

    /// <summary>
    /// Domain state for one flight. Progress values are normalised to [0, 1].
    /// </summary>
    public class Flight
    {
        private const int ServiceCount = 4;
        private float[] _progress;

        public Flight()
            : this(string.Empty, string.Empty, 0f, 75f, -1)
        {
        }

        public Flight(string id, string destination, float arrivalTime, float deadline, int stand = -1)
        {
            Id = id ?? string.Empty;
            Destination = destination ?? string.Empty;
            ArrivalTime = arrivalTime;
            Deadline = deadline;
            Stand = stand >= 0 && stand < AirportSimulation.StandCount ? stand : -1;
            Status = FlightStatus.Scheduled;
            ArrivalBagsReturned = false;
            _progress = new float[ServiceCount];
        }

        public Flight(string id, float arrivalTime, float deadline, int stand = -1)
            : this(id, string.Empty, arrivalTime, deadline, stand)
        {
        }

        public string Id { get; set; }

        public string Destination { get; set; }

        public float ArrivalTime { get; set; }

        public float Deadline { get; set; }

        /// <summary>实际获分配机位的权威时间，迁移滑行恢复使用。</summary>
        public float AssignedAt { get; set; }

        /// <summary>
        /// -1 means that the flight is waiting for a stand; 0..StandCount-1 are the stands.
        /// </summary>
        public int Stand { get; set; }

        public FlightStatus Status { get; set; }

        /// <summary>
        /// Progress in enum order: Meals, Baggage, Fuel, Boarding.
        /// </summary>
        public float[] Progress
        {
            get { return _progress; }
            set
            {
                if (value == null || value.Length != ServiceCount)
                {
                    throw new ArgumentException("Flight progress must contain exactly four values.", "value");
                }

                _progress = value;
            }
        }

        public bool ArrivalBagsReturned { get; set; }

        private readonly bool[] failedTasks = new bool[ServiceCount];

        public bool IsTaskFailed(ServiceKind kind)
        {
            int index = (int)kind;
            return index >= 0 && index < failedTasks.Length && failedTasks[index];
        }

        internal void SetTaskFailed(ServiceKind kind, bool failed)
        {
            int index = (int)kind;
            if (index >= 0 && index < failedTasks.Length)
            {
                failedTasks[index] = failed;
            }
        }

        public bool IsComplete
        {
            get
            {
                for (int i = 0; i < _progress.Length; i++)
                {
                    if (_progress[i] < 1f || failedTasks[i])
                    {
                        return false;
                    }
                }

                return true;
            }
        }
    }

    /// <summary>
    /// A small engine-independent simulation of a five-minute airport shift.
    /// Time is advanced in event-sized slices so a large Tick cannot skip a
    /// flight deadline or leave a stand occupied after a timeout.
    /// </summary>
    public class AirportSimulation
    {
        public const float ShiftDuration = 300f;

        // A 75-second service window leaves enough room for two players to
        // finish two flights while still creating overlap between arrivals.
        private const float DefaultServiceWindow = 75f;

        // Level 1 opens all three ramps drawn in the Figma level design.
        public const int StandCount = 3;

        // Permanent per-task failures must be enforced by the lowest public
        // progress entry point so callers cannot bypass ShiftSim's UI rules.
        bool applyingBoundaryWork;

        private readonly HashSet<string> failedTasks = new HashSet<string>(StringComparer.Ordinal);

        public AirportSimulation()
            : this(null)
        {
        }

        public AirportSimulation(IEnumerable<Flight> flights)
        {
            Flights = new List<Flight>();

            IEnumerable<Flight> source = flights;
            if (source == null)
            {
                source = CreateDefaultFlights();
            }

            foreach (Flight flight in source)
            {
                if (flight != null)
                {
                    Flights.Add(flight);
                }
            }

            LastEvent = "Shift started";
            ProcessEventsAtCurrentTime();
        }

        public List<Flight> Flights { get; private set; }

        public float Elapsed { get; private set; }

        public float Remaining
        {
            get
            {
                if (Finished)
                {
                    return 0f;
                }

                return Math.Max(0f, ShiftDuration - Elapsed);
            }
        }

        public bool Finished { get; private set; }

        /// <summary>
        /// Endless=true 时永不按 300s 结算（sandbox 自由练习），Tick 照走提供时间基。
        /// </summary>
        public bool Endless;

        public int Score { get; private set; }

        public int DepartedCount { get; private set; }

        public int MissedCount { get; private set; }

        public int CompletedTaskCount { get; private set; }

        /// <summary>永久失败任务键：flightId:kind（kind 为 ServiceKind 数字）。</summary>
        public static string TaskKey(string flightId, ServiceKind kind)
        {
            return (flightId ?? string.Empty) + ":" + (int)kind;
        }

        public bool IsTaskFailed(string flightId, ServiceKind kind)
        {
            return !string.IsNullOrEmpty(flightId) && IsValidService(kind) &&
                failedTasks.Contains(TaskKey(flightId, kind));
        }

        /// <summary>永久锁定一项任务。重复标记返回 false，不影响计分。</summary>
        public bool FailTask(string flightId, ServiceKind kind)
        {
            if (string.IsNullOrEmpty(flightId) || !IsValidService(kind))
            {
                return false;
            }

            bool added = failedTasks.Add(TaskKey(flightId, kind));
            for (int i = 0; i < Flights.Count; i++)
            {
                if (Flights[i] != null && Flights[i].Id == flightId)
                {
                    Flights[i].SetTaskFailed(kind, true);
                }
            }

            return added;
        }

        /// <summary>恢复快照中的永久失败集合；无效键会被忽略。</summary>
        public void ReplaceFailedTasks(IEnumerable<string> taskKeys)
        {
            failedTasks.Clear();
            for (int i = 0; i < Flights.Count; i++)
            {
                if (Flights[i] == null) continue;
                for (int kind = 0; kind < 4; kind++)
                {
                    Flights[i].SetTaskFailed((ServiceKind)kind, false);
                }
            }

            if (taskKeys == null)
            {
                return;
            }

            foreach (string taskKey in taskKeys)
            {
                if (string.IsNullOrEmpty(taskKey))
                {
                    continue;
                }

                int separator = taskKey.LastIndexOf(':');
                int kindValue;
                if (separator <= 0 || separator >= taskKey.Length - 1 ||
                    !int.TryParse(taskKey.Substring(separator + 1), out kindValue))
                {
                    continue;
                }

                ServiceKind kind = (ServiceKind)kindValue;
                if (IsValidService(kind))
                {
                    failedTasks.Add(TaskKey(taskKey.Substring(0, separator), kind));
                }
            }

            foreach (string taskKey in failedTasks)
            {
                int separator = taskKey.LastIndexOf(':');
                ServiceKind kind = (ServiceKind)int.Parse(taskKey.Substring(separator + 1));
                string flightId = taskKey.Substring(0, separator);
                for (int i = 0; i < Flights.Count; i++)
                {
                    if (Flights[i] != null && Flights[i].Id == flightId)
                    {
                        Flights[i].SetTaskFailed(kind, true);
                    }
                }
            }
        }

        public int Stars
        {
            get
            {
                if (DepartedCount >= 4)
                {
                    return 3;
                }

                if (DepartedCount >= 2)
                {
                    return 2;
                }

                if (DepartedCount >= 1)
                {
                    return 1;
                }

                return 0;
            }
        }

        public string LastEvent { get; private set; }

        /// <summary>
        /// Return the flight currently being serviced at a stand, or null when
        /// the stand is free or the stand number is invalid.
        /// </summary>
        public Flight ActiveAtStand(int stand)
        {
            if (stand < 0 || stand >= StandCount)
            {
                return null;
            }

            for (int i = 0; i < Flights.Count; i++)
            {
                Flight flight = Flights[i];
                if (flight.Status == FlightStatus.Servicing && flight.Stand == stand)
                {
                    return flight;
                }
            }

            return null;
        }

        /// <summary>
        /// Advance simulation time. Events at every arrival and deadline in the
        /// interval are processed in chronological order.
        /// </summary>
        public void Tick(float dt) { TickWithWork(dt, null); }

        /// <summary>区间末先裁定本区间工作，再处理同刻截止；工作回调只收到截止前实际 dt。</summary>
        public void TickWithWork(float dt, Action<float> work)
        {
            if (Finished || dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt))
            {
                return;
            }

            ProcessEventsAtCurrentTime();
            if (Finished)
            {
                return;
            }

            float target = Elapsed + dt;
            if (!Endless && target >= ShiftDuration)
            {
                target = ShiftDuration;
            }

            while (!Finished)
            {
                float nextEvent = target;

                for (int i = 0; i < Flights.Count; i++)
                {
                    Flight flight = Flights[i];

                    if (flight.Status == FlightStatus.Scheduled &&
                        flight.ArrivalTime > Elapsed &&
                        flight.ArrivalTime < nextEvent)
                    {
                        nextEvent = flight.ArrivalTime;
                    }

                    if (flight.Status == FlightStatus.Servicing &&
                        flight.Deadline > Elapsed &&
                        flight.Deadline < nextEvent)
                    {
                        nextEvent = flight.Deadline;
                    }
                }

                // If the next event is now, ProcessEventsAtCurrentTime has
                // already dealt with it. Move to the requested target to avoid
                // getting stuck on a float boundary.
                if (nextEvent <= Elapsed)
                {
                    Elapsed = target;
                    ProcessEventsAtCurrentTime();
                    break;
                }

                float advanced = nextEvent - Elapsed;
                Elapsed = nextEvent;
                if (work != null && advanced > 0f)
                {
                    applyingBoundaryWork = true;
                    try { work(advanced); }
                    finally { applyingBoundaryWork = false; }
                }
                ProcessEventsAtCurrentTime();

                if (Elapsed >= target)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Return the arrival baggage for a servicing flight. Baggage progress
        /// cannot advance until this has happened.
        /// </summary>
        public bool ReturnArrivalBags(Flight flight)
        {
            ProcessEventsAtCurrentTime();

            if (!CanOperateOn(flight) || flight.ArrivalBagsReturned)
            {
                return false;
            }

            flight.ArrivalBagsReturned = true;
            LastEvent = flight.Id + " arrival baggage returned";
            return true;
        }

        /// <summary>
        /// Add normalised progress to one service. A task awards 50 points the
        /// first time it reaches 1.0. Completing all four services departs the
        /// flight immediately and frees its stand.
        /// </summary>
        public bool TryAdvance(Flight flight, ServiceKind kind, float amount)
        {
            ProcessEventsAtCurrentTime();

            if (!CanOperateOn(flight) || !IsValidService(kind) ||
                amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount))
            {
                return false;
            }

            int serviceIndex = (int)kind;
            if (IsTaskFailed(flight.Id, kind))
            {
                return false;
            }

            if (kind == ServiceKind.Baggage && !flight.ArrivalBagsReturned)
            {
                return false;
            }

            if (kind == ServiceKind.Boarding &&
                (flight.Progress[(int)ServiceKind.Meals] < 1f ||
                 flight.Progress[(int)ServiceKind.Fuel] < 1f))
            {
                return false;
            }

            float previous = flight.Progress[serviceIndex];
            if (previous >= 1f)
            {
                return false;
            }

            float updated = previous + amount;
            if (updated > 1f)
            {
                updated = 1f;
            }

            flight.Progress[serviceIndex] = updated;

            if (previous < 1f && updated >= 1f)
            {
                CompletedTaskCount++;
                Score += 50;
                LastEvent = flight.Id + " " + kind + " completed";
            }

            if (flight.IsComplete)
            {
                DepartFlight(flight);
            }

            return true;
        }

        /// <summary>
        /// Freeze the shift and mark all remaining work as missed. Completed
        /// tasks and already departed flights retain their score and status.
        /// </summary>
        public void EndShift()
        {
            if (Finished)
            {
                return;
            }

            Finished = true;

            for (int i = 0; i < Flights.Count; i++)
            {
                Flight flight = Flights[i];
                if (flight.Status == FlightStatus.Scheduled ||
                    flight.Status == FlightStatus.Servicing)
                {
                    flight.Status = FlightStatus.Missed;
                    flight.Stand = -1;
                    MissedCount++;
                }
            }

            LastEvent = "Shift ended";
        }

        /// <summary>仅供镜像/权威检查点恢复；不重新触发到场、过期或重复计分。</summary>
        public void RestoreState(float elapsed, bool finished, bool endless, int score, int departed, int missed, int completedTasks)
        {
            Elapsed = Math.Max(0f, endless ? elapsed : Math.Min(ShiftDuration, elapsed));
            Finished = finished;
            Endless = endless;
            Score = Math.Max(0, score);
            DepartedCount = Math.Max(0, departed);
            MissedCount = Math.Max(0, missed);
            CompletedTaskCount = Math.Max(0, completedTasks);
            LastEvent = finished ? "Shift ended" : "Checkpoint restored";
        }

        private static IEnumerable<Flight> CreateDefaultFlights()
        {
            yield return new Flight("UBA826", "北湾", 0f, 0f + DefaultServiceWindow);
            yield return new Flight("QMR152", "珊瑚角", 35f, 35f + DefaultServiceWindow);
            yield return new Flight("AZU407", "棕榈滩", 85f, 85f + DefaultServiceWindow);
            yield return new Flight("GLO219", "蓝泻湖", 140f, 140f + DefaultServiceWindow);
            yield return new Flight("TAM638", "港湾", 195f, 195f + DefaultServiceWindow);
        }

        private static bool IsValidService(ServiceKind kind)
        {
            int value = (int)kind;
            return value >= (int)ServiceKind.Meals && value <= (int)ServiceKind.Boarding;
        }

        private bool CanOperateOn(Flight flight)
        {
            if (Finished || flight == null || !Flights.Contains(flight))
            {
                return false;
            }

            if (flight.Status != FlightStatus.Servicing ||
                flight.Stand < 0 || flight.Stand >= StandCount)
            {
                return false;
            }

            if (Elapsed < flight.ArrivalTime || (applyingBoundaryWork ? Elapsed > flight.Deadline : Elapsed >= flight.Deadline))
            {
                return false;
            }

            return true;
        }

        private void ProcessEventsAtCurrentTime()
        {
            if (Finished || applyingBoundaryWork)
            {
                return;
            }

            // A completed flight always wins a same-time deadline race. The
            // normal path departs as soon as the final task is advanced, while
            // this check also keeps the domain safe if a caller filled the
            // public progress array directly.
            for (int i = 0; i < Flights.Count; i++)
            {
                Flight flight = Flights[i];
                if (flight.Status == FlightStatus.Servicing && flight.IsComplete)
                {
                    DepartFlight(flight);
                }
            }

            // Deadlines are checked before assigning waiting flights. A queued
            // flight keeps its own deadline from arrival and may therefore miss
            // while both stands are occupied.
            for (int i = 0; i < Flights.Count; i++)
            {
                Flight flight = Flights[i];

                if ((flight.Status == FlightStatus.Servicing ||
                     flight.Status == FlightStatus.Scheduled) &&
                    flight.Deadline <= Elapsed &&
                    !flight.IsComplete)
                {
                    MarkMissed(flight);
                }
            }

            AssignWaitingFlights();

            // This also handles a custom Flight whose progress was already full
            // when it received a stand during this pass.
            for (int i = 0; i < Flights.Count; i++)
            {
                Flight flight = Flights[i];
                if (flight.Status == FlightStatus.Servicing && flight.IsComplete)
                {
                    DepartFlight(flight);
                }
            }

            if (!Endless && Elapsed >= ShiftDuration)
            {
                Elapsed = ShiftDuration;
                EndShift();
            }
        }

        private void AssignWaitingFlights()
        {
            while (true)
            {
                int freeStand = FindFreeStand();
                if (freeStand < 0)
                {
                    return;
                }

                Flight candidate = null;
                for (int i = 0; i < Flights.Count; i++)
                {
                    Flight flight = Flights[i];
                    if (flight.Status != FlightStatus.Scheduled ||
                        flight.ArrivalTime > Elapsed ||
                        flight.Deadline <= Elapsed)
                    {
                        continue;
                    }

                    if (candidate == null || flight.ArrivalTime < candidate.ArrivalTime)
                    {
                        candidate = flight;
                    }
                }

                if (candidate == null)
                {
                    return;
                }

                candidate.AssignedAt = Elapsed;
                candidate.Status = FlightStatus.Servicing;
                candidate.Stand = freeStand;
                LastEvent = candidate.Id + " landed on stand " + freeStand;

                if (candidate.IsComplete)
                {
                    DepartFlight(candidate);
                }
            }
        }

        private int FindFreeStand()
        {
            for (int stand = 0; stand < StandCount; stand++)
            {
                if (ActiveAtStand(stand) == null)
                {
                    return stand;
                }
            }

            return -1;
        }

        private void DepartFlight(Flight flight)
        {
            if (flight == null || flight.Status != FlightStatus.Servicing || !flight.IsComplete)
            {
                return;
            }

            float timeLeft = Math.Max(0f, flight.Deadline - Elapsed);
            int timeBonus = (int)timeLeft;

            flight.Status = FlightStatus.Departed;
            flight.Stand = -1;
            DepartedCount++;
            Score += 150 + timeBonus;
            LastEvent = flight.Id + " departed";

            AssignWaitingFlights();
        }

        private void MarkMissed(Flight flight)
        {
            if (flight == null ||
                (flight.Status != FlightStatus.Scheduled && flight.Status != FlightStatus.Servicing))
            {
                return;
            }

            flight.Status = FlightStatus.Missed;
            flight.Stand = -1;
            MissedCount++;
            LastEvent = flight.Id + " missed";
        }
    }
}
