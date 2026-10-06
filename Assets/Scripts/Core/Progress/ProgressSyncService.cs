using System;
using System.Collections.Generic;

namespace IslandAirport
{
    /// <summary>One request at a time, current account only. Pump on the persistent app singleton.</summary>
    public sealed class ProgressSyncService : IDisposable
    {
        enum Operation { None, Create, VerifyExisting, Load }
        readonly string project;
        readonly Func<AuthUser> currentUser;
        readonly Func<bool> isFakeAuth;
        readonly ProgressOutbox outbox;
        readonly IProgressTransport transport;
        readonly Dictionary<string, ProgressSettlement> known = new Dictionary<string, ProgressSettlement>();
        readonly Dictionary<string, ProgressSettlement> loading = new Dictionary<string, ProgressSettlement>();
        readonly HashSet<string> blocked = new HashSet<string>();
        readonly HashSet<string> seenPageTokens = new HashSet<string>();
        string account = "", requestToken = "", rejectedToken = "", nextPage = "";
        Operation operation;
        ProgressSettlement active;
        bool loadRequested, loadingPages, disposed, verifyingDeniedCreate, hasLoadedSummary;
        float wait, storageWait;
        int failures;
        public string Status { get; private set; }
        public bool IsConfigured { get { return FirestoreProgressProtocol.ValidProject(project); } }
        public int PendingCount { get { return outbox.CountFor(AccountUid()); } }
        ProgressSummary summary;
        public ProgressSummary CurrentProgress
        {
            get { return AccountUid() == account ? summary : ProgressSummary.Derive(new ProgressSettlement[0]); }
            private set { summary = value; }
        }

        public ProgressSyncService(string projectId, Func<AuthUser> currentUser, Func<bool> isFakeAuth,
            ProgressOutbox outbox, IProgressTransport transport)
        {
            if (currentUser == null || isFakeAuth == null || outbox == null || transport == null) throw new ArgumentNullException();
            project = projectId; this.currentUser = currentUser; this.isFakeAuth = isFakeAuth;
            this.outbox = outbox; this.transport = transport;
            CurrentProgress = ProgressSummary.Derive(known.Values);
            Status = IsConfigured ? "signed_out" : "unconfigured";
        }

        AuthUser AuthenticatedUser()
        {
            var user = currentUser();
            return SettlementIdentity.CaptureIdentity(user, isFakeAuth()) == null ? null : user;
        }
        string AccountUid() { var user = AuthenticatedUser(); return user == null ? "" : user.Uid; }

        public bool RecordSettlement(SettlementIdentity capturedIdentity, string matchId, int roundId, string levelId,
            int stars, int score, int completedFlights, int completedTasks, int elapsedSeconds, bool eligibleAtSettlement)
        {
            if (disposed || !eligibleAtSettlement || capturedIdentity == null) return false;
            var user = AuthenticatedUser();
            if (user == null || capturedIdentity.Uid != user.Uid) return false;
            ProgressSettlement receipt;
            try { receipt = new ProgressSettlement(capturedIdentity.Uid, matchId, roundId, levelId,
                stars, score, completedFlights, completedTasks, elapsedSeconds); }
            catch (ArgumentException) { Status = "invalid_settlement"; return false; }
            ProgressSettlement existing;
            if (known.TryGetValue(receipt.ReceiptId, out existing) && existing.Uid == receipt.Uid)
            {
                if (!existing.SameResult(receipt)) { Status = "receipt_conflict"; return false; }
                return true;
            }
            if (!outbox.Add(receipt)) { Status = "receipt_conflict"; return false; }
            Status = outbox.Ready ? (IsConfigured ? "pending" : "unconfigured") : "storage_error";
            return true;
        }

        public void RequestLoad()
        {
            if (disposed) return;
            loadRequested = true;
            if (operation == Operation.None) { wait = 0; failures = 0; }
        }

        public void Pump(float dt)
        {
            if (disposed || dt < 0 || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            var user = AuthenticatedUser();
            string uid = user == null ? "" : user.Uid;
            if (uid != account)
            {
                transport.Cancel(); operation = Operation.None; active = null; account = uid;
                known.Clear(); loading.Clear(); blocked.Clear(); seenPageTokens.Clear();
                CurrentProgress = ProgressSummary.Derive(known.Values);
                hasLoadedSummary = false;
                loadRequested = uid.Length > 0; loadingPages = false; nextPage = "";
                wait = 0; failures = 0; rejectedToken = "";
            }
            // Captured receipts belong to their original accounts even after sign-out. Their
            // durable save must recover independently of cloud access or the current auth state.
            storageWait = Math.Max(0, storageWait - dt);
            if (!outbox.Ready)
            {
                if (storageWait > 0 || !outbox.Flush())
                {
                    Status = "storage_error";
                    if (storageWait <= 0) storageWait = 5;
                    return;
                }
                storageWait = 0;
            }
            if (!IsConfigured) { Status = "unconfigured"; return; }
            if (user == null) { Status = "signed_out"; return; }
            if (operation != Operation.None)
            {
                ProgressHttpResponse response;
                if (transport.TryTakeResponse(out response)) Process(response, user);
                return;
            }
            wait = Math.Max(0, wait - dt);
            if (wait > 0) return;
            if (rejectedToken.Length > 0 && rejectedToken == user.IdToken) { Status = "auth_required"; return; }
            rejectedToken = "";
            if (loadingPages) { StartLoad(user); return; }
            foreach (var receipt in outbox.ForAccount(uid))
            {
                if (blocked.Contains(receipt.ReceiptId)) continue;
                active = receipt;
                Start(Operation.Create, new ProgressHttpRequest { Method = "POST",
                    Url = FirestoreProgressProtocol.CollectionUrl(project, uid) + "?documentId=" + receipt.ReceiptId,
                    Body = FirestoreProgressProtocol.Encode(receipt), IdToken = user.IdToken });
                return;
            }
            if (loadRequested)
            {
                loading.Clear(); seenPageTokens.Clear(); nextPage = ""; loadingPages = true;
                StartLoad(user); return;
            }
            Status = SettledStatus();
        }

        string SettledStatus()
        {
            if (!outbox.Ready) return "storage_error";
            if (blocked.Count > 0) return "receipt_conflict";
            if (outbox.CountFor(account) > 0) return "pending";
            if (loadRequested || loadingPages) return "loading";
            return "synced";
        }

        void StartLoad(AuthUser user)
        {
            string url = FirestoreProgressProtocol.CollectionUrl(project, account) + "?pageSize=100";
            if (nextPage.Length > 0) url += "&pageToken=" + Uri.EscapeDataString(nextPage);
            Start(Operation.Load, new ProgressHttpRequest { Method = "GET", Url = url, IdToken = user.IdToken });
        }
        void Start(Operation kind, ProgressHttpRequest request)
        {
            operation = kind; requestToken = request.IdToken;
            Status = kind == Operation.Load ? "loading" : "syncing";
            try { transport.Start(request); }
            catch (Exception e)
            {
                if (!(e is System.IO.IOException) && !(e is InvalidOperationException)) throw;
                operation = Operation.None; Retry("network_error");
            }
        }

        void Process(ProgressHttpResponse response, AuthUser user)
        {
            Operation completed = operation; operation = Operation.None;
            if (response == null) { Retry("network_error"); return; }
            if (response.StatusCode == 401)
            {
                rejectedToken = requestToken; Status = "auth_required"; return;
            }
            // Some rule/precondition combinations reject a duplicate create before returning
            // ALREADY_EXISTS. An owner-readable identical immutable receipt still confirms commit.
            if (completed == Operation.Create && (response.StatusCode == 409 || response.StatusCode == 403))
            {
                verifyingDeniedCreate = response.StatusCode == 403;
                Start(Operation.VerifyExisting, new ProgressHttpRequest { Method = "GET",
                    Url = FirestoreProgressProtocol.CollectionUrl(project, account) + "/" + active.ReceiptId,
                    IdToken = user.IdToken });
                return;
            }
            if (response.StatusCode == 403 || (completed == Operation.VerifyExisting && verifyingDeniedCreate && response.StatusCode == 404))
            {
                Retry("permission_denied"); wait = Math.Max(60, wait); return;
            }
            if (response.StatusCode < 200 || response.StatusCode >= 300)
            {
                Retry(response.StatusCode == 404 ? "backend_missing" : response.StatusCode == 400 ? "invalid_request" : "network_error");
                return;
            }
            try
            {
                var root = MiniJson.ParseObject(response.Body);
                if (completed == Operation.Create || completed == Operation.VerifyExisting)
                {
                    var stored = FirestoreProgressProtocol.DecodeDocument(root, project, account);
                    if (!active.SameResult(stored))
                    {
                        blocked.Add(active.ReceiptId); Status = "receipt_conflict"; active = null; return;
                    }
                    known[stored.ReceiptId] = stored;
                    // A newly acknowledged receipt is not the account's complete history.
                    // Retain the last complete summary until initial pagination has finished.
                    if (hasLoadedSummary) CurrentProgress = ProgressSummary.Derive(known.Values);
                    outbox.Remove(active); active = null;
                    Status = SettledStatus();
                }
                else if (completed == Operation.Load)
                {
                    var documents = MiniJson.GetArray(root, "documents");
                    if (root.ContainsKey("documents") && documents == null) throw new FormatException();
                    if (documents != null) foreach (object item in documents)
                    {
                        var document = item as Dictionary<string, object>;
                        if (document == null) throw new FormatException();
                        var stored = FirestoreProgressProtocol.DecodeDocument(document, project, account);
                        loading[stored.ReceiptId] = stored;
                    }
                    if (root.ContainsKey("nextPageToken") && !(root["nextPageToken"] is string)) throw new FormatException();
                    nextPage = MiniJson.GetString(root, "nextPageToken", "");
                    if (nextPage.Length > 0 && !seenPageTokens.Add(nextPage)) throw new FormatException("Repeated page token.");
                    if (nextPage.Length == 0)
                    {
                        known.Clear(); foreach (var row in loading) known.Add(row.Key, row.Value);
                        CurrentProgress = ProgressSummary.Derive(known.Values);
                        hasLoadedSummary = true;
                        loading.Clear(); loadingPages = false; loadRequested = false; Status = SettledStatus();
                    }
                }
                failures = 0; wait = 0;
            }
            catch (Exception e)
            {
                if (!(e is FormatException) && !(e is ArgumentException)) throw;
                Retry("invalid_response");
            }
        }
        void Retry(string reason)
        {
            Status = reason; failures = Math.Min(failures + 1, 6);
            wait = Math.Min(60, (float)Math.Pow(2, failures));
            // A load retries from its first page to avoid exposing a partial aggregate.
            if (loadingPages) { loadingPages = false; loading.Clear(); nextPage = ""; loadRequested = true; }
        }
        public void Dispose()
        {
            if (disposed) return;
            outbox.Flush(); transport.Dispose(); disposed = true;
        }
    }
}
