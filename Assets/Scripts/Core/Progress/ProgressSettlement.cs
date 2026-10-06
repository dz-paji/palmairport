using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace IslandAirport
{
    /// <summary>Capture at round start; a guest/fake round can never acquire an account later.</summary>
    public sealed class SettlementIdentity
    {
        public string Uid { get; private set; }
        private SettlementIdentity(string uid) { Uid = uid; }
        public static SettlementIdentity CaptureIdentity(AuthUser user, bool isFakeAuth)
        {
            if (isFakeAuth || user == null || !ProgressSettlement.ValidUid(user.Uid) ||
                string.IsNullOrEmpty(user.IdToken) || user.IdToken.StartsWith("fake-", StringComparison.Ordinal)) return null;
            return new SettlementIdentity(user.Uid);
        }
    }

    /// <summary>Immutable, account-scoped receipt. Contains no token or mutable roster reference.</summary>
    public sealed class ProgressSettlement
    {
        public string Uid { get; private set; }
        public string MatchId { get; private set; }
        public int RoundId { get; private set; }
        public string LevelId { get; private set; }
        public int Stars { get; private set; }
        public int Score { get; private set; }
        public int CompletedFlights { get; private set; }
        public int CompletedTasks { get; private set; }
        public int ElapsedSeconds { get; private set; }
        public string ReceiptId { get; private set; }

        public ProgressSettlement(string uid, string matchId, int roundId, string levelId,
            int stars, int score, int completedFlights, int completedTasks, int elapsedSeconds)
        {
            if (!ValidUid(uid) || string.IsNullOrEmpty(matchId) || matchId.Length > 200 ||
                roundId < 0 || roundId > 1000000000 || !ValidLevelId(levelId) || stars < 0 || stars > 3 ||
                score < 0 || score > 100000000 || completedFlights < 0 || completedFlights > 10000 ||
                completedTasks < 0 || completedTasks > 100000 || elapsedSeconds < 1 || elapsedSeconds > 86400)
                throw new ArgumentException("Invalid settlement.");
            Uid = uid; MatchId = matchId; RoundId = roundId; LevelId = levelId;
            Stars = stars; Score = score; CompletedFlights = completedFlights;
            CompletedTasks = completedTasks; ElapsedSeconds = elapsedSeconds;
            using (SHA256 hash = SHA256.Create())
            {
                byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(matchId + "\n" + roundId.ToString(CultureInfo.InvariantCulture)));
                StringBuilder hex = new StringBuilder(64);
                foreach (byte b in bytes) hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                ReceiptId = hex.ToString();
            }
        }

        public static bool ValidUid(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 128 || value == "." || value == "..") return false;
            foreach (char c in value) if (c == '/' || char.IsControl(c)) return false;
            return true;
        }
        public static bool ValidLevelId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64) return false;
            foreach (char c in value) if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') &&
                !(c >= '0' && c <= '9') && c != '_' && c != '-') return false;
            return true;
        }
        public Dictionary<string, object> ToValues()
        {
            return new Dictionary<string, object> {
                {"schemaVersion", 1}, {"uid", Uid}, {"receiptId", ReceiptId}, {"matchId", MatchId},
                {"roundId", RoundId}, {"levelId", LevelId}, {"stars", Stars}, {"score", Score},
                {"completedFlights", CompletedFlights}, {"completedTasks", CompletedTasks}, {"elapsedSeconds", ElapsedSeconds}
            };
        }
        public bool SameResult(ProgressSettlement other)
        {
            return other != null && Uid == other.Uid && MatchId == other.MatchId && RoundId == other.RoundId &&
                LevelId == other.LevelId && Stars == other.Stars && Score == other.Score &&
                CompletedFlights == other.CompletedFlights && CompletedTasks == other.CompletedTasks && ElapsedSeconds == other.ElapsedSeconds;
        }
        public static ProgressSettlement FromValues(IDictionary<string, object> values)
        {
            if (ReadInt(values, "schemaVersion") != 1) throw new FormatException("Invalid progress schema.");
            ProgressSettlement result = new ProgressSettlement(MiniJson.GetString(values, "uid", ""),
                MiniJson.GetString(values, "matchId", ""), ReadInt(values, "roundId"),
                MiniJson.GetString(values, "levelId", ""), ReadInt(values, "stars"), ReadInt(values, "score"),
                ReadInt(values, "completedFlights"), ReadInt(values, "completedTasks"), ReadInt(values, "elapsedSeconds"));
            if (MiniJson.GetString(values, "receiptId", "") != result.ReceiptId) throw new FormatException("Invalid receipt identity.");
            return result;
        }
        private static int ReadInt(IDictionary<string, object> values, string key)
        {
            double value = MiniJson.GetNumber(values, key, double.NaN);
            if (double.IsNaN(value) || double.IsInfinity(value) || value != Math.Truncate(value) || value < int.MinValue || value > int.MaxValue)
                throw new FormatException("Invalid receipt number.");
            return (int)value;
        }
    }

    public sealed class ProgressSummary
    {
        public int CompletedMatches { get; private set; }
        public long TotalCompletedFlights { get; private set; }
        public long TotalCompletedTasks { get; private set; }
        public long TotalScore { get; private set; }
        private readonly Dictionary<string, int> bestStars = new Dictionary<string, int>();
        public IDictionary<string, int> LevelBestStars { get { return new Dictionary<string, int>(bestStars); } }
        public static ProgressSummary Derive(IEnumerable<ProgressSettlement> receipts)
        {
            ProgressSummary summary = new ProgressSummary();
            HashSet<string> seen = new HashSet<string>();
            foreach (ProgressSettlement receipt in receipts)
            {
                if (!seen.Add(receipt.Uid + "/" + receipt.ReceiptId)) continue;
                summary.CompletedMatches++; summary.TotalCompletedFlights += receipt.CompletedFlights;
                summary.TotalCompletedTasks += receipt.CompletedTasks; summary.TotalScore += receipt.Score;
                int best;
                if (!summary.bestStars.TryGetValue(receipt.LevelId, out best) || receipt.Stars > best)
                    summary.bestStars[receipt.LevelId] = receipt.Stars;
            }
            return summary;
        }
    }
}
