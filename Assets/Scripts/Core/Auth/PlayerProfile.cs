using System;
using System.Collections.Generic;

namespace IslandAirport
{
    /// <summary>
    /// 玩家档案（衣柜改名换色 + 游客 ID）。纯数据，MiniJson 序列化做持久化往返，
    /// 存储介质由调用方注入（Unity 侧 PlayerPrefs/文件，测试内存）。
    /// </summary>
    public class PlayerProfile
    {
        public const int MaxNameLength = 12;

        public string Name = "Guest";
        public int ColorIndex;
        public string GuestId = string.Empty;

        /// <summary>名字规整：去首尾空白；null 归一空串。</summary>
        public static string NormalizeName(string raw)
        {
            return raw == null ? string.Empty : raw.Trim();
        }

        /// <summary>名字校验：trim 后 1–12 字符。</summary>
        public static bool IsValidName(string raw)
        {
            string name = NormalizeName(raw);
            return name.Length >= 1 && name.Length <= MaxNameLength;
        }

        /// <summary>序列化为 JSON 单行。</summary>
        public string ToJson()
        {
            Dictionary<string, object> obj = new Dictionary<string, object>();
            obj["name"] = Name ?? string.Empty;
            obj["color"] = (double)ColorIndex;
            obj["guest"] = GuestId ?? string.Empty;
            return MiniJson.Serialize(obj);
        }

        /// <summary>从 JSON 解析；非法输入返回 null。</summary>
        public static PlayerProfile FromJson(string json)
        {
            Dictionary<string, object> obj;
            try
            {
                obj = MiniJson.ParseObject(json);
            }
            catch (FormatException)
            {
                return null;
            }
            catch (ArgumentNullException)
            {
                return null;
            }

            PlayerProfile profile = new PlayerProfile();
            profile.Name = MiniJson.GetString(obj, "name", "Guest");
            profile.ColorIndex = (int)MiniJson.GetNumber(obj, "color", 0);
            profile.GuestId = MiniJson.GetString(obj, "guest", string.Empty);
            return profile;
        }
    }
}
