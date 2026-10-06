using System;
using System.Collections.Generic;

namespace IslandAirport
{
    public sealed class AgeVerificationStore
    {
        readonly Dictionary<string, bool> realUsers = new Dictionary<string, bool>(StringComparer.Ordinal);
        readonly Dictionary<string, bool> fakeUsers = new Dictionary<string, bool>(StringComparer.Ordinal);

        public bool IsVerified(string uid, bool isFake)
        {
            bool verified;
            return TryGetStatus(uid, isFake, out verified) && verified;
        }

        public bool IsVerifiedFor(AuthUser user, bool isFake)
        {
            if (user == null)
            {
                return false;
            }

            user.AgeVerified = IsVerified(user.Uid, isFake);
            return user.AgeVerified;
        }

        public void ClearCurrentUser(AuthUser user)
        {
            if (user != null)
            {
                user.AgeVerified = false;
            }
        }

        public bool TryGetStatus(string uid, bool isFake, out bool verified)
        {
            verified = false;
            if (string.IsNullOrEmpty(uid))
            {
                return false;
            }

            return (isFake ? fakeUsers : realUsers).TryGetValue(uid, out verified);
        }

        public bool SetStatus(string uid, bool isFake, bool verified)
        {
            if (string.IsNullOrEmpty(uid))
            {
                return false;
            }

            (isFake ? fakeUsers : realUsers)[uid] = verified;
            return true;
        }

        public bool Remove(string uid, bool isFake)
        {
            if (string.IsNullOrEmpty(uid))
            {
                return false;
            }

            return (isFake ? fakeUsers : realUsers).Remove(uid);
        }

        public bool TryRecordBirthDate(string uid, bool isFake, int year, int month, int day,
            DateTime today, out bool ageVerified, out string error)
        {
            ageVerified = false;
            error = string.Empty;
            if (string.IsNullOrEmpty(uid))
            {
                error = "请先登录，再验证年龄。";
                return false;
            }

            DateTime dateOfBirth;
            try
            {
                dateOfBirth = new DateTime(year, month, day);
            }
            catch (ArgumentOutOfRangeException)
            {
                error = "请输入有效的出生日期。";
                return false;
            }

            if (dateOfBirth.Date > today.Date)
            {
                error = "出生日期不能晚于今天。";
                return false;
            }

            ageVerified = AgeCheck.Allowed(dateOfBirth, today.Date);
            SetStatus(uid, isFake, ageVerified);
            if (!ageVerified)
            {
                error = "未满 13 岁，暂不能与其他玩家组队。";
            }

            return true;
        }

        public string ToJson()
        {
            Dictionary<string, object> root = new Dictionary<string, object>();
            root["real"] = ToObject(realUsers);
            root["fake"] = ToObject(fakeUsers);
            return MiniJson.Serialize(root);
        }

        public static AgeVerificationStore FromJson(string json)
        {
            AgeVerificationStore store = new AgeVerificationStore();
            Dictionary<string, object> root;
            try
            {
                root = MiniJson.ParseObject(json);
            }
            catch (FormatException)
            {
                return store;
            }
            catch (ArgumentNullException)
            {
                return store;
            }

            LoadObject(MiniJson.GetObject(root, "real"), store.realUsers);
            LoadObject(MiniJson.GetObject(root, "fake"), store.fakeUsers);
            return store;
        }

        static Dictionary<string, object> ToObject(Dictionary<string, bool> users)
        {
            Dictionary<string, object> output = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, bool> user in users)
            {
                output[user.Key] = user.Value;
            }

            return output;
        }

        static void LoadObject(Dictionary<string, object> input, Dictionary<string, bool> output)
        {
            if (input == null)
            {
                return;
            }

            foreach (KeyValuePair<string, object> user in input)
            {
                if (!string.IsNullOrEmpty(user.Key) && user.Value is bool)
                {
                    output[user.Key] = (bool)user.Value;
                }
            }
        }
    }
}
