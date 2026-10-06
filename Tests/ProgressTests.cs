using System;
using System.Collections.Generic;
using System.IO;
using IslandAirport;

class ProgressTests
{
    static int assertions;
    static void Check(bool condition, string message)
    {
        assertions++; if (!condition) throw new Exception(message);
    }
    sealed class Disk
    {
        public string Json = "";
        public bool FailWrites, FailReads;
        public int Writes, WriteAttempts;
        public ProgressOutbox Open()
        {
            return new ProgressOutbox(() => { if (FailReads) throw new IOException(); return Json; },
                value => { WriteAttempts++; if (FailWrites) throw new IOException(); Json = value; Writes++; });
        }
    }
    sealed class Server : IProgressTransport
    {
        public readonly Dictionary<string, ProgressSettlement> Documents = new Dictionary<string, ProgressSettlement>();
        public readonly List<ProgressHttpRequest> Requests = new List<ProgressHttpRequest>();
        public readonly Queue<ProgressHttpResponse> Scripted = new Queue<ProgressHttpResponse>();
        public bool LoseNextCreateResponse, HoldResponses;
        public int Creates, Cancels;
        ProgressHttpResponse response;
        public static string Key(ProgressSettlement row) { return row.Uid + "/" + row.ReceiptId; }
        public static object Document(ProgressSettlement row)
        {
            var root = MiniJson.ParseObject(FirestoreProgressProtocol.Encode(row));
            root["name"] = "projects/palmairport/databases/(default)/documents/accountProgress/" + Key(row).Replace("/", "/settlements/");
            return root;
        }
        public static ProgressHttpResponse DocumentResponse(ProgressSettlement row)
        {
            return new ProgressHttpResponse { StatusCode = 200, Body = MiniJson.Serialize(Document(row)) };
        }
        public static ProgressHttpResponse Page(string token, params ProgressSettlement[] rows)
        {
            var docs = new List<object>(); foreach (var row in rows) docs.Add(Document(row));
            return new ProgressHttpResponse { StatusCode = 200, Body = MiniJson.Serialize(new Dictionary<string, object> {
                {"documents", docs}, {"nextPageToken", token} }) };
        }
        public void Start(ProgressHttpRequest request)
        {
            Requests.Add(request);
            if (Scripted.Count > 0) { response = Scripted.Dequeue(); return; }
            string suffix = request.Url.Substring(request.Url.IndexOf("/accountProgress/", StringComparison.Ordinal) + 17);
            string uid = Uri.UnescapeDataString(suffix.Split('/')[0]);
            if (request.Method == "POST")
            {
                string receiptId = request.Url.Substring(request.Url.IndexOf("?documentId=", StringComparison.Ordinal) + 12);
                var document = MiniJson.ParseObject(request.Body);
                document["name"] = "projects/palmairport/databases/(default)/documents/accountProgress/" + uid + "/settlements/" + receiptId;
                var row = FirestoreProgressProtocol.DecodeDocument(document, "palmairport", uid);
                if (Documents.ContainsKey(Key(row))) response = new ProgressHttpResponse { StatusCode = 409, Body = "{}" };
                else
                {
                    Documents.Add(Key(row), row); Creates++;
                    response = LoseNextCreateResponse ? new ProgressHttpResponse { StatusCode = 0, Body = "" } : DocumentResponse(row);
                    LoseNextCreateResponse = false;
                }
            }
            else if (request.Url.Contains("?pageSize="))
            {
                var rows = new List<ProgressSettlement>(); foreach (var row in Documents.Values) if (row.Uid == uid) rows.Add(row);
                response = Page("", rows.ToArray());
            }
            else
            {
                string receiptId = suffix.Substring(suffix.LastIndexOf('/') + 1);
                ProgressSettlement row;
                response = Documents.TryGetValue(uid + "/" + receiptId, out row) ? DocumentResponse(row) : new ProgressHttpResponse { StatusCode = 404, Body = "{}" };
            }
        }
        public bool TryTakeResponse(out ProgressHttpResponse result)
        {
            result = null; if (HoldResponses || response == null) return false;
            result = response; response = null; return true;
        }
        public void Cancel() { response = null; Cancels++; }
        public void Dispose() { Cancel(); }
    }
    sealed class Harness
    {
        public AuthUser User = UserFor("account-a");
        public bool Fake;
        public readonly Disk Disk;
        public readonly Server Server;
        public readonly ProgressSyncService Sync;
        public Harness(Disk disk = null, Server server = null, string project = "palmairport")
        {
            Disk = disk ?? new Disk(); Server = server ?? new Server();
            Sync = new ProgressSyncService(project, () => User, () => Fake, Disk.Open(), Server);
        }
        public SettlementIdentity Capture() { return SettlementIdentity.CaptureIdentity(User, Fake); }
        public bool Record(string match = "match-a", bool eligible = true, SettlementIdentity identity = null, int stars = 2)
        {
            return Sync.RecordSettlement(identity ?? Capture(), match, 1, "PalmBay", stars, 400, 2, 8, 300, eligible);
        }
        public void Run(int steps = 12, float dt = 1) { for (int i = 0; i < steps; i++) Sync.Pump(dt); }
    }
    static AuthUser UserFor(string uid) { return new AuthUser { Uid = uid, IdToken = "real-test-token-" + uid }; }
    static ProgressSettlement Receipt(string uid = "account-a", string match = "match-a", int stars = 2)
    {
        return new ProgressSettlement(uid, match, 1, "PalmBay", stars, 400, 2, 8, 300);
    }

    static void CaptureAndParticipation()
    {
        var h = new Harness();
        Check(SettlementIdentity.CaptureIdentity(null, false) == null, "Guest capture must be empty.");
        Check(SettlementIdentity.CaptureIdentity(h.User, true) == null, "Fake service capture must be empty.");
        Check(SettlementIdentity.CaptureIdentity(new AuthUser { Uid = "fake-uid", IdToken = "fake-id" }, false) == null, "Fake token cannot become real progress.");
        Check(!h.Record(eligible: false), "Departed/BOT/practice seat must not receive progress.");
        h.Fake = true; Check(!h.Record(), "Fake runtime cannot queue real progress."); h.Fake = false;
        var identity = h.Capture(); h.User = UserFor("account-b");
        Check(!h.Record(identity: identity), "Account changed since round start.");
        h.User = null;
        Check(!h.Sync.RecordSettlement(identity, "m", 1, "PalmBay", 1, 1, 1, 1, 1, true), "Signed-out settlement rejected.");
        h.User = UserFor("account-a");
        Check(!h.Sync.RecordSettlement(null, "guest-started", 1, "PalmBay", 1, 1, 1, 1, 1, true), "Later sign-in must not credit guest round.");
        Check(h.Record(identity: identity), "Remaining participant accepted after authority migration.");
        h.Run();
        Check(h.Server.Creates == 1 && h.Sync.CurrentProgress.CompletedMatches == 1, "Only participating own account credited.");
    }
    static void DurableAndIdempotent()
    {
        var disk = new Disk(); var server = new Server { LoseNextCreateResponse = true };
        var h = new Harness(disk, server);
        Check(h.Record(), "Capture succeeds.");
        Check(!disk.Json.Contains("real-test-token") && !disk.Json.Contains("IdToken"), "Durable outbox must not contain credentials.");
        Check(h.Record(), "Identical duplicate capture accepted.");
        Check(!h.Record(stars: 3), "Mutated duplicate cannot replace pending result.");
        h.Run(2);
        Check(server.Creates == 1 && h.Sync.PendingCount == 1, "Lost response retains durable receipt.");
        h.Sync.Dispose();
        var restored = new Harness(disk, server); restored.Run(20);
        Check(server.Creates == 1 && restored.Sync.PendingCount == 0, "Restart retries the same receipt without incrementing twice.");
        Check(restored.Sync.CurrentProgress.CompletedMatches == 1, "Derived totals count deterministic receipt once.");
        Check(restored.Record(), "Duplicate after acknowledgement accepted."); restored.Run();
        Check(server.Creates == 1, "Acknowledged receipt isn't recreated.");
        Check(restored.Record("match-retry"), "New round has distinct receipt."); restored.Run();
        Check(server.Creates == 2 && restored.Sync.CurrentProgress.CompletedMatches == 2, "A real retry is another completed match.");
    }
    static void AccountsAndCancellation()
    {
        var h = new Harness(); h.Record();
        h.User = UserFor("account-b"); h.Record("match-b"); h.Run();
        Check(h.Server.Creates == 1 && h.Server.Documents.ContainsKey(Server.Key(Receipt("account-b", "match-b"))), "Only signed-in UID uploads.");
        Check(h.Sync.CurrentProgress.CompletedMatches == 1, "Summary belongs to B.");
        h.User = null; h.Run(1);
        Check(h.Sync.CurrentProgress.CompletedMatches == 0 && h.Sync.PendingCount == 0, "Sign-out clears displayed account data.");
        h.User = UserFor("account-a"); h.Run();
        Check(h.Server.Creates == 2 && h.Sync.CurrentProgress.CompletedMatches == 1, "Original account gets its own deferred settlement only.");
        var delayed = new Harness(); delayed.Record(); delayed.Server.HoldResponses = true; delayed.Run(1);
        delayed.User = UserFor("account-b"); delayed.Run(1);
        Check(delayed.Server.Cancels >= 2 && delayed.Sync.CurrentProgress.CompletedMatches == 0, "Account switch cancels old HTTP response.");
        delayed.Server.HoldResponses = false; delayed.Run(); delayed.User = UserFor("account-a"); delayed.Run();
        Check(delayed.Server.Creates == 1 && delayed.Sync.PendingCount == 0, "Committed/cancelled request remains safe to retry under original account.");
    }
    static void ErrorsAndRenewal()
    {
        var h = new Harness(); h.Record(); h.Server.Scripted.Enqueue(new ProgressHttpResponse { StatusCode = 401, Body = "{}" }); h.Run(2);
        int requests = h.Server.Requests.Count; h.Run(20);
        Check(h.Sync.Status == "auth_required" && h.Sync.PendingCount == 1 && h.Server.Requests.Count == requests, "401 waits for renewed token, keeps receipt.");
        h.User.IdToken = "renewed-token"; h.Run();
        Check(h.Sync.PendingCount == 0 && h.Server.Creates == 1, "Renewed token resumes queue.");
        var denied = new Harness(); denied.Record(); denied.Server.Scripted.Enqueue(new ProgressHttpResponse { StatusCode = 403, Body = "{}" }); denied.Run(2); denied.Run(10);
        Check(denied.Sync.Status == "permission_denied" && denied.Server.Requests.Count == 2 && denied.Sync.PendingCount == 1, "Missing rules report permission failure with bounded retry.");
        denied.Run(75);
        Check(denied.Sync.PendingCount == 0, "Permissions repaired can retry without lost result.");
        var deniedDuplicate = new Harness(); var committed = Receipt(); deniedDuplicate.Server.Documents.Add(Server.Key(committed), committed);
        deniedDuplicate.Record(); deniedDuplicate.Server.Scripted.Enqueue(new ProgressHttpResponse { StatusCode = 403, Body = "{}" }); deniedDuplicate.Run();
        Check(deniedDuplicate.Sync.PendingCount == 0 && deniedDuplicate.Server.Creates == 0 && deniedDuplicate.Sync.CurrentProgress.CompletedMatches == 1,
            "Rule rejection of duplicate create can confirm identical owner-readable receipt.");
        var conflict = new Harness(); var other = Receipt(stars: 3); conflict.Server.Documents.Add(Server.Key(other), other); conflict.Record(); conflict.Run();
        Check(conflict.Sync.Status == "receipt_conflict" && conflict.Sync.PendingCount == 1 && conflict.Server.Creates == 0, "409 must compare payload, never silently accept conflicting result.");
        var malformed = new Harness(); malformed.Record(); malformed.Server.Scripted.Enqueue(new ProgressHttpResponse { StatusCode = 200, Body = "{}" }); malformed.Run(2);
        Check(malformed.Sync.Status == "invalid_response" && malformed.Sync.PendingCount == 1, "Malformed success cannot discard receipt.");
        malformed.Run(); Check(malformed.Sync.PendingCount == 0, "Malformed success retries.");
        var missing = new Harness(project: ""); missing.Record(); missing.Run();
        Check(missing.Sync.Status == "unconfigured" && missing.Server.Requests.Count == 0 && missing.Sync.PendingCount == 1, "No backend configuration never claims cloud success.");
    }
    static void StorageRecovery()
    {
        var disk = new Disk { FailWrites = true }; var h = new Harness(disk);
        Check(h.Record(), "Transient storage failure preserves capture in memory."); h.Run();
        Check(h.Sync.Status == "storage_error" && h.Server.Requests.Count == 0 && h.Sync.PendingCount == 1, "No upload until durable save.");
        disk.FailWrites = false; h.Run(20);
        Check(h.Sync.PendingCount == 0 && h.Server.Creates == 1, "Storage repaired persists and uploads captured result.");
        var corrupt = new Disk { Json = "broken json" }; var c = new Harness(corrupt); c.Record(); c.Run();
        Check(c.Sync.Status == "storage_error" && corrupt.Json == "broken json" && corrupt.Writes == 0, "Corrupt outbox is never overwritten.");
        Check(c.Server.Requests.Count == 0, "Corrupt storage blocks network safely.");
        corrupt.Json = ""; c.Run(20);
        Check(c.Server.Creates == 1, "Recovered storage includes retained in-memory capture.");
        var interrupted = new Disk { FailReads = true }; var r = new Harness(interrupted); r.Record(); r.Run(2);
        interrupted.FailReads = false; r.Run(20);
        Check(r.Server.Creates == 1, "Read failure recovers without losing new capture.");
    }
    static void PaginationAndValidation()
    {
        var h = new Harness(); var first = Receipt(); var second = Receipt(match: "match-b", stars: 3);
        h.Server.Scripted.Enqueue(Server.Page("next token", first));
        h.Server.Scripted.Enqueue(Server.Page("", first, second));
        h.Run(2);
        Check(h.Sync.CurrentProgress.CompletedMatches == 0, "Partial pages must not publish incomplete aggregate.");
        h.Run(4);
        Check(h.Sync.CurrentProgress.CompletedMatches == 2 && h.Sync.CurrentProgress.TotalScore == 800 &&
            h.Sync.CurrentProgress.LevelBestStars["PalmBay"] == 3, "Paginated distinct receipts derive total/best stars.");
        Check(h.Server.Requests[1].Url.Contains("pageToken=next%20token"), "Page tokens encoded exactly.");
        h.Sync.RequestLoad(); h.Server.Scripted.Enqueue(Server.Page("", Receipt("account-b"))); h.Run(2);
        Check(h.Sync.Status == "invalid_response" && h.Sync.CurrentProgress.CompletedMatches == 2, "Cross-account response rejected without replacing last complete progress.");
        bool failed = false; try { Receipt(stars: 4); } catch (ArgumentException) { failed = true; }
        Check(failed, "Stars input bounded.");
        failed = false; try { new ProgressSettlement("uid/bad", "m", 0, "PalmBay", 1, 1, 1, 1, 1); } catch (ArgumentException) { failed = true; }
        Check(failed, "Path traversal UID rejected.");
        var encoded = MiniJson.ParseObject(FirestoreProgressProtocol.Encode(first)); encoded["name"] = "wrong";
        failed = false; try { FirestoreProgressProtocol.DecodeDocument(encoded, "palmairport", "account-a"); } catch (FormatException) { failed = true; }
        Check(failed, "Wire document path verified.");
    }
    static void PersistenceWithoutCloudAccess()
    {
        var disk = new Disk { FailWrites = true }; var h = new Harness(disk);
        Check(h.Record(), "Capture retained during initial write failure.");
        h.Run(2); int attempts = disk.WriteAttempts; h.Run(2);
        Check(disk.WriteAttempts == attempts, "Storage retry is bounded independently of network backoff.");
        h.User = null; disk.FailWrites = false; h.Run(12);
        Check(h.Sync.Status == "signed_out" && disk.Writes == 1 && h.Server.Requests.Count == 0,
            "Signed-out immutable receipt recovers durable save without cloud requests.");
        Check(disk.Open().CountFor("account-a") == 1, "Original UID's signed-out capture survives restart.");
        var reopened = new Harness(disk, h.Server); reopened.User = UserFor("account-b"); reopened.Run();
        Check(reopened.Server.Creates == 0 && reopened.Sync.CurrentProgress.CompletedMatches == 0,
            "Another account cannot upload or display the signed-out account's receipt.");
        reopened.User = UserFor("account-a"); reopened.Run();
        Check(reopened.Server.Creates == 1 && reopened.Sync.PendingCount == 0,
            "Original account can safely upload its recovered receipt later.");

        var missingDisk = new Disk { FailWrites = true }; var missing = new Harness(missingDisk, project: "");
        Check(missing.Record(), "Unconfigured backend still captures immutable result."); missing.Run(2);
        missingDisk.FailWrites = false; missing.Run(12);
        Check(missing.Sync.Status == "unconfigured" && missingDisk.Writes == 1 && missing.Server.Requests.Count == 0,
            "Backend absence does not prevent bounded durable storage recovery.");
        Check(missingDisk.Open().CountFor("account-a") == 1, "Unconfigured backend capture is durable.");
    }
    static void InitialHistoryMustCompleteBeforeSynced()
    {
        var h = new Harness(); var old = Receipt(match: "older-cloud-match");
        h.Server.Documents.Add(Server.Key(old), old); h.Record(); h.Run(2);
        Check(h.Sync.Status == "loading" && h.Sync.CurrentProgress.CompletedMatches == 0,
            "New receipt acknowledgement cannot present an incomplete account history as synced.");
        h.Server.HoldResponses = true; h.Run(3);
        Check(h.Sync.Status == "loading" && h.Sync.CurrentProgress.CompletedMatches == 0,
            "Incomplete initial history stays unpublished during delayed load.");
        h.Server.HoldResponses = false; h.Run(3);
        Check(h.Sync.Status == "synced" && h.Sync.CurrentProgress.CompletedMatches == 2,
            "Synced status requires the full initial history plus the new receipt.");

        var duringLoad = new Harness(); duringLoad.Run(1); duringLoad.Record(); duringLoad.Run(1);
        Check(duringLoad.Sync.Status == "pending" && duringLoad.Sync.PendingCount == 1,
            "Completing history with a newly queued settlement must not claim all progress synced.");
        duringLoad.Run(); Check(duringLoad.Sync.Status == "synced" && duringLoad.Sync.CurrentProgress.CompletedMatches == 1,
            "Known complete history updates after its new receipt is acknowledged.");
    }
    public static int Main()
    {
        try
        {
            CaptureAndParticipation(); DurableAndIdempotent(); AccountsAndCancellation(); ErrorsAndRenewal(); StorageRecovery(); PaginationAndValidation();
            PersistenceWithoutCloudAccess(); InitialHistoryMustCompleteBeforeSynced();
            Console.WriteLine("Progress tests passed: " + assertions + " assertions."); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
