using System;

namespace IslandAirport
{
    /// <summary>
    /// Opening identity for one local account. Observe participation changes until Finish,
    /// then latch CanSettle's decision with the immutable result. Later lobby cleanup does
    /// not revoke that result. This object never credits the other device's account.
    /// </summary>
    public sealed class SettlementEligibility
    {
        public readonly string Uid;
        public readonly string MatchId;
        public readonly int Seat;
        public readonly bool Network;
        public bool IsInvalidated { get; private set; }

        public SettlementEligibility(string uid, bool realAuth, string matchId, int seat, bool network)
        {
            Uid = uid ?? string.Empty;
            MatchId = matchId ?? string.Empty;
            Seat = seat;
            Network = network;
            IsInvalidated = !realAuth || string.IsNullOrWhiteSpace(Uid) ||
                string.IsNullOrWhiteSpace(MatchId) || seat < 0 || seat > (network ? 1 : 0);
        }

        /// <summary>
        /// Permanently revoke participation when identity or room membership is lost.
        /// Call on auth changes as well as simulation steps, so logout and relogin within
        /// one frame cannot hide a lost session. Same-UID token refresh is harmless.
        /// Starting is a valid observation phase; settlement itself requires Playing.
        /// Stop observing after the caller has latched its settlement decision.
        /// </summary>
        public void Observe(string currentUid, bool realAuth, string currentMatchId, int currentSeat,
            bool started, bool practice, RoomPhase phase, bool occupied, bool bot, bool creditEligible)
        {
            if (!IdentityMatches(currentUid, realAuth, currentMatchId, currentSeat) || !started || practice ||
                Network && ((phase != RoomPhase.Starting && phase != RoomPhase.Playing) ||
                    !occupied || bot || !creditEligible))
                IsInvalidated = true;
        }

        /// <summary>
        /// Read-only decision for the first actual Finish. No deduplication or upload is
        /// performed here; the caller records once using MatchId and Uid and owns retries.
        /// A room's stale eligible seat during its Closed cleanup grace is insufficient.
        /// </summary>
        public bool CanSettle(string currentUid, bool realAuth, string currentMatchId, int currentSeat,
            bool started, bool finished, bool practice, RoomPhase phase, bool occupied, bool bot, bool creditEligible)
        {
            return !IsInvalidated && IdentityMatches(currentUid, realAuth, currentMatchId, currentSeat) &&
                started && finished && !practice &&
                (!Network || phase == RoomPhase.Playing && occupied && !bot && creditEligible);
        }

        bool IdentityMatches(string currentUid, bool realAuth, string currentMatchId, int currentSeat)
        {
            return realAuth && currentSeat == Seat &&
                string.Equals(Uid, currentUid, StringComparison.Ordinal) &&
                string.Equals(MatchId, currentMatchId, StringComparison.Ordinal);
        }
    }
}
