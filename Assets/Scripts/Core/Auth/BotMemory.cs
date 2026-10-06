using System;
using System.Collections.Generic;

namespace IslandAirport
{
    /// <summary>Per-device cooperation history. Only caller-attributed human task completions are stored.</summary>
    public sealed class BotMemory
    {
        readonly int[] humanTasks = new int[4];
        public const int RecentMatchLimit = 256;
        readonly Queue<string> matchOrder = new Queue<string>();
        readonly HashSet<string> settledMatches = new HashSet<string>(StringComparer.Ordinal);
        public int SharedRounds { get; private set; }
        public int DepartedFlights { get; private set; }
        public string LastSharedDate { get; private set; } = string.Empty;
        public int HumanTaskCount(int kind) { return kind >= 0 && kind < 4 ? humanTasks[kind] : 0; }
        public bool HasRecordedMatch(string matchId) { return matchId != null && settledMatches.Contains(matchId); }

        public bool TryRecordMatch(string matchId, int departedFlights, int[] humanTaskCounts, string date)
        {
            if (!ValidId(matchId) || settledMatches.Contains(matchId) || departedFlights < 0 ||
                humanTaskCounts == null || humanTaskCounts.Length != 4 || !ValidDate(date)) return false;
            if (SharedRounds == int.MaxValue || DepartedFlights > int.MaxValue - departedFlights) return false;
            for (int i = 0; i < 4; i++)
                if (humanTaskCounts[i] < 0 || humanTasks[i] > int.MaxValue - humanTaskCounts[i]) return false;
            // Validate the whole result first: rejection cannot leave partial counters or a consumed match ID.
            SharedRounds++;
            DepartedFlights += departedFlights;
            for (int i = 0; i < 4; i++) humanTasks[i] += humanTaskCounts[i];
            LastSharedDate = date;
            settledMatches.Add(matchId);
            matchOrder.Enqueue(matchId);
            if (matchOrder.Count > RecentMatchLimit) settledMatches.Remove(matchOrder.Dequeue());
            return true;
        }

        /// <summary>Leave the human's most common tasks to them when another unfinished task is available.</summary>
        public int ChooseBotTask(bool[] available)
        {
            if (available == null || available.Length != 4) return -1;
            int best = -1;
            for (int i = 0; i < 4; i++)
                if (available[i] && (best < 0 || humanTasks[i] < humanTasks[best])) best = i;
            return best;
        }

        public string ToJson()
        {
            var counts = new List<object>();
            for (int i = 0; i < 4; i++) counts.Add((double)humanTasks[i]);
            var ids = new List<string>(matchOrder);
            var matches = new List<object>();
            for (int i = 0; i < ids.Count; i++) matches.Add(ids[i]);
            return MiniJson.Serialize(new Dictionary<string, object> {
                { "version", 1d }, { "rounds", (double)SharedRounds }, { "departed", (double)DepartedFlights },
                { "humanTasks", counts }, { "lastDate", LastSharedDate }, { "matches", matches }
            });
        }

        public static BotMemory FromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            Dictionary<string, object> root;
            try { root = MiniJson.ParseObject(json); }
            catch (FormatException) { return null; }
            catch (ArgumentNullException) { return null; }
            int version, rounds, departed;
            if (!ReadCount(root, "version", out version) || version != 1 ||
                !ReadCount(root, "rounds", out rounds) || !ReadCount(root, "departed", out departed)) return null;
            object rawCounts, rawMatches, rawDate;
            if (!root.TryGetValue("humanTasks", out rawCounts) || !root.TryGetValue("matches", out rawMatches) ||
                !root.TryGetValue("lastDate", out rawDate)) return null;
            var counts = rawCounts as List<object>; var matches = rawMatches as List<object>; string date = rawDate as string;
            if (counts == null || counts.Count != 4 || matches == null || matches.Count != Math.Min(rounds, RecentMatchLimit) || date == null ||
                (rounds == 0 ? date.Length != 0 || departed != 0 : !ValidDate(date))) return null;
            var memory = new BotMemory();
            for (int i = 0; i < 4; i++)
                if (!ReadCount(counts[i], out memory.humanTasks[i]) || (rounds == 0 && memory.humanTasks[i] != 0)) return null;
            for (int i = 0; i < matches.Count; i++)
            {
                string match = matches[i] as string;
                if (!ValidId(match) || !memory.settledMatches.Add(match)) return null;
                memory.matchOrder.Enqueue(match);
            }
            memory.SharedRounds = rounds; memory.DepartedFlights = departed; memory.LastSharedDate = date;
            return memory;
        }

        static bool ReadCount(Dictionary<string, object> root, string key, out int count)
        { object raw; count = 0; return root.TryGetValue(key, out raw) && ReadCount(raw, out count); }
        static bool ReadCount(object raw, out int count)
        {
            count = 0;
            if (!(raw is double)) return false;
            double value = (double)raw;
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value > int.MaxValue || value != Math.Floor(value)) return false;
            count = (int)value; return true;
        }
        static bool ValidId(string id) { return !string.IsNullOrEmpty(id) && id.Length <= 160 && id.Trim() == id; }
        static bool ValidDate(string date)
        {
            if (date == null || date.Length != 10 || date[4] != '-' || date[7] != '-') return false;
            for (int i = 0; i < date.Length; i++) if (i != 4 && i != 7 && (date[i] < '0' || date[i] > '9')) return false;
            int year, month, day;
            if (!int.TryParse(date.Substring(0, 4), out year) || !int.TryParse(date.Substring(5, 2), out month) ||
                !int.TryParse(date.Substring(8, 2), out day) || year < 1 || year > 9999 || month < 1 || month > 12) return false;
            return day >= 1 && day <= DateTime.DaysInMonth(year, month);
        }
    }

    /// <summary>JSON persistence with injected storage: no engine, account, clock, voice or chat dependency.</summary>
    public sealed class BotMemoryStore
    {
        readonly Action<string> write;
        public BotMemory Memory { get; private set; }
        public BotMemoryStore(Func<string> read, Action<string> writeJson)
        {
            if (read == null || writeJson == null) throw new ArgumentNullException("storage");
            write = writeJson;
            Memory = BotMemory.FromJson(read()) ?? new BotMemory();
        }
        public bool Record(string matchId, int departedFlights, int[] humanTasks, string date)
        {
            // Write first, then publish the new memory; an IO failure remains retryable.
            BotMemory updated = BotMemory.FromJson(Memory.ToJson());
            if (!updated.TryRecordMatch(matchId, departedFlights, humanTasks, date)) return false;
            write(updated.ToJson()); Memory = updated; return true;
        }
    }
}
