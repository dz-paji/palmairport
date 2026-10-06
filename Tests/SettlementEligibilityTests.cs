using System;
using IslandAirport;

class SettlementEligibilityTests
{
    static int groups, assertions;
    static void Require(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new Exception(message);
    }
    static void Test(string name, Action action)
    {
        action(); groups++; Console.WriteLine("PASS  " + name);
    }
    static bool Eligible(SettlementEligibility identity, string uid = "account-A", bool real = true,
        string match = "room:round1", int seat = 0, bool started = true, bool finished = true,
        bool practice = false, RoomPhase phase = RoomPhase.Playing, bool occupied = true,
        bool bot = false, bool credit = true)
    {
        return identity.CanSettle(uid, real, match, seat, started, finished, practice, phase, occupied, bot, credit);
    }
    static void Observe(SettlementEligibility identity, string uid = "account-A", bool real = true,
        string match = "room:round1", int seat = 0, bool started = true, bool practice = false,
        RoomPhase phase = RoomPhase.Playing, bool occupied = true, bool bot = false, bool credit = true)
    {
        identity.Observe(uid, real, match, seat, started, practice, phase, occupied, bot, credit);
    }
    static SettlementEligibility Opening(bool network = true, int seat = 0)
    {
        return new SettlementEligibility("account-A", true, "room:round1", seat, network);
    }
    static void Throws(Action action, string message)
    {
        bool threw = false;
        try { action(); } catch (ArgumentOutOfRangeException) { threw = true; }
        Require(threw, message);
    }
    static void Main()
    {
        Test("Local finished account requires stable opening identity", () => {
            var identity = Opening(false);
            Require(Eligible(identity, phase: RoomPhase.Idle, occupied: false, credit: false), "local needs no room");
            Require(!Eligible(identity, uid: "account-B"), "different UID");
            Require(!Eligible(identity, uid: "ACCOUNT-A"), "UID exact case");
            Require(!Eligible(identity, match: "room:round2"), "different match");
            Require(!Eligible(identity, seat: 1), "local seat zero only");
            Require(!Eligible(identity, real: false), "fake current auth");
            Require(!Eligible(identity, started: false), "not participating");
            Require(!Eligible(identity, finished: false), "not finished");
            Require(!Eligible(identity, practice: true), "practice excluded");
        });
        Test("Guest fake and malformed opening contexts cannot qualify", () => {
            foreach (string uid in new[] { null, "", " " })
                Require(!Eligible(new SettlementEligibility(uid, true, "room:round1", 0, true)), "guest UID");
            Require(!Eligible(new SettlementEligibility("account-A", false, "room:round1", 0, true)), "fake opening");
            Require(!Eligible(new SettlementEligibility("account-A", true, "", 0, true), match: ""), "empty match");
            Require(!Eligible(new SettlementEligibility("account-A", true, "room:round1", -1, true), seat: -1), "negative seat");
            Require(!Eligible(new SettlementEligibility("account-A", true, "room:round1", 2, true), seat: 2), "out of roster");
            Require(!Eligible(new SettlementEligibility("account-A", true, "room:round1", 1, false), seat: 1), "bad local seat");
        });
        Test("LAN final snapshot requires live room and eligible human seat", () => {
            var identity = Opening();
            Require(Eligible(identity), "live host qualifies");
            foreach (RoomPhase phase in Enum.GetValues(typeof(RoomPhase)))
                Require(Eligible(identity, phase: phase) == (phase == RoomPhase.Playing), "only Playing settles");
            Require(!Eligible(identity, occupied: false), "empty seat");
            Require(!Eligible(identity, bot: true), "bot substitute excluded");
            Require(!Eligible(identity, credit: false), "revoked credit");
        });
        Test("Leave before Finish invalidates stale eligible seat during grace", () => {
            var identity = Opening();
            Observe(identity, phase: RoomPhase.Closed);
            Require(identity.IsInvalidated, "Closed revokes immediately");
            Require(!Eligible(identity, phase: RoomPhase.Closed), "latched final snapshot does not credit leave");
            Observe(identity);
            Require(!Eligible(identity), "reopening cannot revive same context");
        });
        Test("Logout then same UID relogin remains excluded but refresh stays valid", () => {
            var identity = Opening();
            Observe(identity);
            Require(!identity.IsInvalidated && Eligible(identity), "same UID refresh remains eligible");
            Observe(identity, uid: null, real: false);
            Observe(identity);
            Require(identity.IsInvalidated && !Eligible(identity), "same UID relogin cannot erase logout");
            var changed = Opening(); Observe(changed, uid: "account-B"); Observe(changed);
            Require(!Eligible(changed), "identity switch cannot revive opening account");
        });
        Test("Loss of human participation remains excluded after bot finishes", () => {
            foreach (int cause in new[] { 0, 1, 2, 3, 4 }) {
                var identity = Opening();
                Observe(identity, occupied: cause != 0, bot: cause == 1, credit: cause != 2,
                    started: cause != 3, practice: cause == 4);
                Observe(identity);
                Require(!Eligible(identity), "participation loss " + cause);
            }
        });
        Test("Starting observation and stable migrated seat one retain identity", () => {
            var identity = Opening(seat: 1);
            Observe(identity, seat: 1, phase: RoomPhase.Starting);
            Require(!identity.IsInvalidated, "Starting is expected before play");
            Observe(identity, seat: 1);
            Require(Eligible(identity, seat: 1), "survivor qualifies after authority migration");
            Require(!Eligible(identity, seat: 0), "promotion does not change local seat");
            Require(identity.Uid == "account-A" && identity.MatchId == "room:round1" && identity.Seat == 1 && identity.Network,
                "opening values retained");
        });
        Test("Wrong round or seat observations permanently revoke context", () => {
            var wrongRound = Opening(); Observe(wrongRound, match: "room:round2");
            Require(!Eligible(wrongRound), "old round cannot resettle");
            var wrongSeat = Opening(seat: 1); Observe(wrongSeat, seat: 0);
            Require(!Eligible(wrongSeat, seat: 1), "seat transfer cannot resettle");
            var idle = Opening(); Observe(idle, phase: RoomPhase.Idle);
            Require(!Eligible(idle), "disconnected room excluded");
        });
        Test("Decision is caller latched and independent contexts do not deduplicate", () => {
            var identity = Opening();
            bool completedDecision = Eligible(identity);
            Require(completedDecision && Eligible(identity), "query itself is repeatable");
            Observe(identity, started: false, phase: RoomPhase.Idle, occupied: false, credit: false);
            Require(completedDecision && !Eligible(identity), "post-Finish cleanup does not change latched decision");
            Require(Eligible(Opening()), "a duplicate context is not an upload ledger");
        });
        Test("Five-minute export has 624 frames and exact final hold", () => {
            var timeline = new ReplayExportTimeline(300f);
            Require(timeline.FrameCount == 624, "25 seconds plus one hold at 24 FPS");
            Require(timeline.Duration == 300f && timeline.TimeAt(0) == 0f, "starts at true history origin");
            Require(timeline.TimeAt(599) == 299.5f, "last moving sample");
            for (int i = 600; i < 624; i++) Require(timeline.TimeAt(i) == 300f, "terminal hold " + i);
            for (int i = 1; i < timeline.FrameCount; i++)
                Require(timeline.TimeAt(i) >= timeline.TimeAt(i - 1), "monotonic timeline " + i);
        });
        Test("Fractional exports cover terminal state and reject bad frame indices", () => {
            var timeline = new ReplayExportTimeline(.6f);
            Require(timeline.FrameCount == 26, "round partial video frame up");
            Require(timeline.TimeAt(0) == 0f && timeline.TimeAt(1) == .5f, "fixed history steps");
            Require(timeline.TimeAt(2) == .6f && timeline.TimeAt(25) == .6f, "terminal state included");
            Throws(() => timeline.TimeAt(-1), "negative frame");
            Throws(() => timeline.TimeAt(timeline.FrameCount), "past final frame");
        });
        Test("Export rejects nonfinite zero negative and oversized durations", () => {
            foreach (float duration in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0f, -1f, 330.01f })
                Throws(() => new ReplayExportTimeline(duration), "invalid duration " + duration);
            Require(new ReplayExportTimeline(330f).FrameCount == 684, "maximum supported duration");
        });
        Console.WriteLine("Settlement tests: " + groups + " passed, 0 failed, " + assertions + " assertions.");
    }
}
