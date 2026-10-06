using System;
using System.Collections.Generic;
using System.Globalization;

namespace IslandAirport
{
    public sealed class ProgressHttpRequest
    {
        public string Method, Url, Body, IdToken;
    }
    public sealed class ProgressHttpResponse
    {
        public long StatusCode;
        public string Body;
    }
    public interface IProgressTransport : IDisposable
    {
        void Start(ProgressHttpRequest request);
        bool TryTakeResponse(out ProgressHttpResponse response);
        void Cancel();
    }
    public static class FirestoreProgressProtocol
    {
        public static bool ValidProject(string project)
        {
            if (string.IsNullOrEmpty(project) || project.Length > 63) return false;
            foreach (char c in project) if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '-') return false;
            return true;
        }
        public static string CollectionUrl(string project, string uid)
        {
            return "https://firestore.googleapis.com/v1/projects/" + project +
                "/databases/(default)/documents/accountProgress/" + Uri.EscapeDataString(uid) + "/settlements";
        }
        public static string Encode(ProgressSettlement receipt)
        {
            Dictionary<string, object> fields = new Dictionary<string, object>();
            foreach (var field in receipt.ToValues())
                fields[field.Key] = new Dictionary<string, object> { {field.Value is string ? "stringValue" : "integerValue",
                    Convert.ToString(field.Value, CultureInfo.InvariantCulture)} };
            return MiniJson.Serialize(new Dictionary<string, object> { {"fields", fields} });
        }
        public static ProgressSettlement DecodeDocument(IDictionary<string, object> document, string project, string uid)
        {
            var fields = MiniJson.GetObject(document, "fields");
            if (fields == null || fields.Count != 11) throw new FormatException("Invalid receipt fields.");
            Dictionary<string, object> values = new Dictionary<string, object>();
            foreach (var field in fields)
            {
                var typed = field.Value as Dictionary<string, object>;
                if (typed == null || typed.Count != 1) throw new FormatException();
                if (typed.ContainsKey("stringValue")) values[field.Key] = MiniJson.GetString(typed, "stringValue", "");
                else
                {
                    long integer;
                    if (!long.TryParse(MiniJson.GetString(typed, "integerValue", ""), NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out integer)) throw new FormatException();
                    values[field.Key] = integer;
                }
            }
            var receipt = ProgressSettlement.FromValues(values);
            string name = "projects/" + project + "/databases/(default)/documents/accountProgress/" + uid + "/settlements/" + receipt.ReceiptId;
            if (receipt.Uid != uid || MiniJson.GetString(document, "name", "") != name) throw new FormatException("Receipt account mismatch.");
            return receipt;
        }
    }
}
