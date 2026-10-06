using System;
using System.Collections.Generic;

namespace IslandAirport
{
    /// <summary>Store callbacks must atomically replace a durable file. Credentials are never persisted.</summary>
    public sealed class ProgressOutbox
    {
        readonly Func<string> read;
        readonly Action<string> write;
        readonly Dictionary<string, ProgressSettlement> pending = new Dictionary<string, ProgressSettlement>();
        bool initialized, dirty;
        public bool Ready { get { return initialized && !dirty; } }
        public string Error { get; private set; }
        public ProgressOutbox(Func<string> read, Action<string> write)
        {
            if (read == null || write == null) throw new ArgumentNullException();
            this.read = read; this.write = write;
            Flush();
        }
        static string Key(ProgressSettlement receipt) { return receipt.Uid + "/" + receipt.ReceiptId; }
        public bool Add(ProgressSettlement receipt)
        {
            ProgressSettlement existing;
            if (pending.TryGetValue(Key(receipt), out existing)) return existing.SameResult(receipt);
            pending.Add(Key(receipt), receipt); dirty = true; Flush(); return true;
        }
        public void Remove(ProgressSettlement receipt)
        {
            if (pending.Remove(Key(receipt))) { dirty = true; Flush(); }
        }
        public int CountFor(string uid)
        {
            int count = 0; foreach (var receipt in pending.Values) if (receipt.Uid == uid) count++; return count;
        }
        public List<ProgressSettlement> ForAccount(string uid)
        {
            List<ProgressSettlement> rows = new List<ProgressSettlement>();
            foreach (var receipt in pending.Values) if (receipt.Uid == uid) rows.Add(receipt);
            rows.Sort((a, b) => string.CompareOrdinal(a.ReceiptId, b.ReceiptId));
            return rows;
        }
        public bool Flush()
        {
            try
            {
                if (!initialized)
                {
                    string json = read();
                    Dictionary<string, ProgressSettlement> disk = new Dictionary<string, ProgressSettlement>();
                    if (!string.IsNullOrEmpty(json))
                    {
                        var root = MiniJson.ParseObject(json);
                        if (MiniJson.GetNumber(root, "schemaVersion", -1) != 1) throw new FormatException();
                        var rows = MiniJson.GetArray(root, "pending");
                        if (rows == null) throw new FormatException();
                        foreach (object row in rows)
                        {
                            var values = row as Dictionary<string, object>;
                            if (values == null) throw new FormatException();
                            var receipt = ProgressSettlement.FromValues(values);
                            ProgressSettlement existing;
                            if (disk.TryGetValue(Key(receipt), out existing) && !existing.SameResult(receipt)) throw new FormatException();
                            disk[Key(receipt)] = receipt;
                        }
                    }
                    foreach (var row in pending)
                    {
                        ProgressSettlement existing;
                        if (disk.TryGetValue(row.Key, out existing) && !existing.SameResult(row.Value)) throw new FormatException();
                        disk[row.Key] = row.Value;
                    }
                    pending.Clear(); foreach (var row in disk) pending.Add(row.Key, row.Value);
                    initialized = true;
                }
                if (dirty)
                {
                    List<object> rows = new List<object>();
                    foreach (var receipt in pending.Values) rows.Add(receipt.ToValues());
                    write(MiniJson.Serialize(new Dictionary<string, object> { {"schemaVersion", 1}, {"pending", rows} }));
                    dirty = false;
                }
                Error = string.Empty; return true;
            }
            catch (Exception e)
            {
                // Keep immutable in-memory capture, but do not send before durable storage succeeds.
                if (!(e is System.IO.IOException) && !(e is UnauthorizedAccessException) &&
                    !(e is FormatException) && !(e is ArgumentException) && !(e is NotSupportedException) &&
                    !(e is System.Security.SecurityException)) throw;
                Error = "storage_error"; return false;
            }
        }
    }
}
