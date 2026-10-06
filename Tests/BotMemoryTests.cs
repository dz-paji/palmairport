using System;
using IslandAirport;
class BotMemoryTests
{
    static int assertions, groups;
    static void Require(bool condition, string message) { assertions++; if (!condition) throw new Exception(message); }
    static void Test(string name, Action test) { test(); groups++; Console.WriteLine("PASS  " + name); }
    static void Main()
    {
        Test("Human-only contributions and duplicate settlement survive reload", () => {
            var memory = new BotMemory();
            Require(memory.TryRecordMatch("match-a", 2, new[] { 3, 1, 0, 2 }, "2026-10-04"), "record");
            Require(!memory.TryRecordMatch("match-a", 99, new[] { 9, 9, 9, 9 }, "2026-10-05"), "duplicate");
            var reload = BotMemory.FromJson(memory.ToJson());
            Require(reload != null && reload.SharedRounds == 1 && reload.DepartedFlights == 2, "counts");
            Require(reload.HumanTaskCount(0) == 3 && reload.HumanTaskCount(2) == 0 && reload.LastSharedDate == "2026-10-04", "human attribution");
            Require(!reload.TryRecordMatch("match-a", 5, new int[4], "2026-10-04"), "duplicate after reload");
            Require(reload.TryRecordMatch("match-b", 1, new[] { 0, 0, 1, 0 }, "2026-10-05") && reload.SharedRounds == 2 && reload.DepartedFlights == 3, "second match");
        });
        Test("Bot leaves common human task when alternatives exist", () => {
            var memory = new BotMemory(); memory.TryRecordMatch("preference", 0, new[] { 8, 3, 1, 0 }, "2026-10-04");
            Require(memory.ChooseBotTask(new[] { true, true, true, false }) == 2, "least human task");
            Require(memory.ChooseBotTask(new[] { true, false, false, false }) == 0, "only available");
            Require(memory.ChooseBotTask(new bool[4]) == -1 && memory.ChooseBotTask(null) == -1, "no task");
            Require(new BotMemory().ChooseBotTask(new[] { true, true, false, false }) == 0, "stable tie");
        });
        Test("Invalid results do not partially mutate memory", () => {
            var memory = new BotMemory(); string before = memory.ToJson();
            Require(!memory.TryRecordMatch("", 1, new int[4], "2026-10-04"), "empty id");
            Require(!memory.TryRecordMatch("bad", -1, new int[4], "2026-10-04"), "negative flights");
            Require(!memory.TryRecordMatch("bad", 1, new[] { 1, -1, 0, 0 }, "2026-10-04"), "negative tasks");
            Require(!memory.TryRecordMatch("bad", 1, new int[3], "2026-10-04"), "wrong count");
            Require(!memory.TryRecordMatch("bad", 1, new int[4], "2026-02-30"), "invalid date");
            Require(before == memory.ToJson(), "unchanged");
            Require(memory.TryRecordMatch("bad", 0, new int[4], "2024-02-29"), "valid leap date retry");
        });
        Test("Strict persisted JSON rejects corruption", () => {
            string[] bad = { "", "null", "[]", "{}", "{", "{\"version\":1,\"rounds\":-1}", "{\"version\":1,\"rounds\":0.5}" };
            foreach (string value in bad) Require(BotMemory.FromJson(value) == null, "corrupt JSON accepted");
            var memory = new BotMemory(); memory.TryRecordMatch("persist", 1, new[] { 1, 0, 0, 0 }, "2026-10-04"); string json = memory.ToJson();
            Require(BotMemory.FromJson(json.Replace("\"rounds\":1", "\"rounds\":2")) == null, "missing recorded IDs");
            Require(BotMemory.FromJson(json.Replace("\"humanTasks\":[1,0,0,0]", "\"humanTasks\":[1,0,0]")) == null, "bad array");
            Require(BotMemory.FromJson(new BotMemory().ToJson()) != null, "empty valid memory");
        });
        Test("Injected persistence saves once and retries write failure", () => {
            string disk = "broken"; int writes = 0;
            var store = new BotMemoryStore(() => disk, json => { writes++; disk = json; });
            Require(store.Memory.SharedRounds == 0, "recover corruption");
            Require(store.Record("disk", 2, new[] { 0, 1, 0, 0 }, "2026-10-04") && writes == 1, "write once");
            Require(!store.Record("disk", 2, new int[4], "2026-10-04") && writes == 1, "duplicate write");
            var loaded = new BotMemoryStore(() => disk, json => disk = json);
            Require(loaded.Memory.DepartedFlights == 2 && loaded.Memory.HumanTaskCount(1) == 1, "disk reload");
            bool fail = true; var retry = new BotMemoryStore(() => "", json => { if (fail) throw new Exception("IO fixture"); disk = json; });
            bool threw = false;
            try { retry.Record("retry", 1, new int[4], "2026-10-04"); } catch (Exception) { threw = true; }
            Require(threw, "write must fail");
            Require(retry.Memory.SharedRounds == 0, "failed write consumed result"); fail = false;
            Require(retry.Record("retry", 1, new int[4], "2026-10-04") && retry.Memory.SharedRounds == 1, "retry preserved");
        });
        Test("Bounded recent ledger preserves cumulative counts and round identities", () => {
            var memory = new BotMemory();
            for (int i = 0; i < BotMemory.RecentMatchLimit + 3; i++)
                Require(memory.TryRecordMatch("session:" + i, 1, new[] { 0, 1, 0, 0 }, "2026-10-04"), "round settlement");
            var reload = BotMemory.FromJson(memory.ToJson());
            Require(reload != null && reload.SharedRounds == 259 && reload.DepartedFlights == 259 && reload.HumanTaskCount(1) == 259, "cumulative preserved");
            Require(!reload.HasRecordedMatch("session:0") && reload.HasRecordedMatch("session:258"), "bounded recent horizon");
            Require(!reload.TryRecordMatch("session:258", 1, new int[4], "2026-10-04"), "migrated round duplicate");
            Require(reload.TryRecordMatch("session:259", 1, new int[4], "2026-10-04"), "retry round distinct");
        });
        Console.WriteLine("Bot memory tests: " + groups + " passed, 0 failed, " + assertions + " assertions.");
    }
}
