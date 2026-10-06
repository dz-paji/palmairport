using System;
using System.IO;
using System.Text;

namespace IslandAirport
{
    /// <summary>Unity scene-independent adapter owned by AppState. No credentials are stored in the outbox.</summary>
    public sealed class AccountProgressRuntime : IDisposable
    {
        readonly ProgressSyncService service;
        public string Status { get { return service.Status; } }
        public int PendingCount { get { return service.PendingCount; } }
        public bool IsConfigured { get { return service.IsConfigured; } }
        public ProgressSummary CurrentProgress { get { return service.CurrentProgress; } }

        public AccountProgressRuntime(string projectId, string persistentDirectory,
            Func<AuthUser> currentUser, Func<bool> isFakeAuth)
        {
            if (string.IsNullOrEmpty(persistentDirectory)) throw new ArgumentException("Missing persistent directory.");
            string path = Path.Combine(persistentDirectory, "palmbay-progress-outbox.json");
            ProgressOutbox outbox = new ProgressOutbox(
                () => File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : string.Empty,
                json => AtomicWrite(path, json));
            service = new ProgressSyncService(projectId, currentUser, isFakeAuth, outbox, new UnityProgressTransport());
        }

        static void AtomicWrite(string path, string json)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            byte[] bytes = new UTF8Encoding(false).GetBytes(json);
            using (FileStream stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        public bool RecordSettlement(SettlementIdentity capturedIdentity, string matchId, int roundId, string levelId,
            int stars, int score, int completedFlights, int completedTasks, int elapsedSeconds, bool eligibleAtSettlement)
        {
            return service.RecordSettlement(capturedIdentity, matchId, roundId, levelId, stars, score,
                completedFlights, completedTasks, elapsedSeconds, eligibleAtSettlement);
        }
        public void Pump(float dt) { service.Pump(dt); }
        public void RequestLoad() { service.RequestLoad(); }
        public void Dispose() { service.Dispose(); }
    }
}
