using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace IslandAirport
{
    /// <summary>
    /// 极简 JSON 读写（严格子集：对象/数组/字符串/数字/bool/null）。
    /// 无正则无 LINQ，UTF-8 中文直出（不转 \uXXXX）。协议、持久化、Firebase 响应共用。
    /// 解析结果：Dictionary&lt;string,object&gt; / List&lt;object&gt; / string / double / bool / null。
    /// </summary>
    public static class MiniJson
    {
        /// <summary>把对象图序列化为 JSON 文本。支持 IDictionary/IList/string/bool/数字/null。</summary>
        public static string Serialize(object value)
        {
            StringBuilder builder = new StringBuilder(256);
            WriteValue(value, builder);
            return builder.ToString();
        }

        /// <summary>解析 JSON 文本为对象图；非法输入抛 FormatException。</summary>
        public static object Parse(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException("text");
            }

            int index = 0;
            object result = ReadValue(text, ref index);
            SkipWhitespace(text, ref index);
            if (index != text.Length)
            {
                throw new FormatException("MiniJson: 尾部多余字符 @" + index);
            }

            return result;
        }

        /// <summary>便捷：解析并取对象根，根不是对象时抛 FormatException。</summary>
        public static Dictionary<string, object> ParseObject(string text)
        {
            object value = Parse(text);
            Dictionary<string, object> obj = value as Dictionary<string, object>;
            if (obj == null)
            {
                throw new FormatException("MiniJson: 根节点不是对象");
            }

            return obj;
        }

        /// <summary>从对象图取字符串，缺键或类型不符返回 fallback。</summary>
        public static string GetString(IDictionary<string, object> obj, string key, string fallback)
        {
            object value;
            if (obj != null && obj.TryGetValue(key, out value) && value is string)
            {
                return (string)value;
            }

            return fallback;
        }

        /// <summary>从对象图取 double，缺键或类型不符返回 fallback。</summary>
        public static double GetNumber(IDictionary<string, object> obj, string key, double fallback)
        {
            object value;
            if (obj != null && obj.TryGetValue(key, out value) && IsNumber(value))
            {
                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }

            return fallback;
        }

        /// <summary>从对象图取 bool，缺键或类型不符返回 fallback。</summary>
        public static bool GetBool(IDictionary<string, object> obj, string key, bool fallback)
        {
            object value;
            if (obj != null && obj.TryGetValue(key, out value) && value is bool)
            {
                return (bool)value;
            }

            return fallback;
        }

        /// <summary>从对象图取数组，缺键或类型不符返回 null。</summary>
        public static List<object> GetArray(IDictionary<string, object> obj, string key)
        {
            object value;
            if (obj != null && obj.TryGetValue(key, out value))
            {
                return value as List<object>;
            }

            return null;
        }

        /// <summary>从对象图取子对象，缺键或类型不符返回 null。</summary>
        public static Dictionary<string, object> GetObject(IDictionary<string, object> obj, string key)
        {
            object value;
            if (obj != null && obj.TryGetValue(key, out value))
            {
                return value as Dictionary<string, object>;
            }

            return null;
        }

        private static bool IsNumber(object value)
        {
            return value is double || value is float || value is int || value is long ||
                value is short || value is byte || value is decimal;
        }

        private static void WriteValue(object value, StringBuilder builder)
        {
            if (value == null)
            {
                builder.Append("null");
                return;
            }

            if (value is string)
            {
                WriteString((string)value, builder);
                return;
            }

            if (value is bool)
            {
                builder.Append((bool)value ? "true" : "false");
                return;
            }

            if (value is char)
            {
                WriteString(value.ToString(), builder);
                return;
            }

            if (IsNumber(value))
            {
                double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (double.IsNaN(number) || double.IsInfinity(number))
                {
                    throw new FormatException("MiniJson: 不支持 NaN/Infinity");
                }

                builder.Append(number.ToString("R", CultureInfo.InvariantCulture));
                return;
            }

            IDictionary<string, object> obj = value as IDictionary<string, object>;
            if (obj != null)
            {
                builder.Append('{');
                bool first = true;
                foreach (KeyValuePair<string, object> pair in obj)
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }

                    first = false;
                    WriteString(pair.Key, builder);
                    builder.Append(':');
                    WriteValue(pair.Value, builder);
                }

                builder.Append('}');
                return;
            }

            System.Collections.IEnumerable sequence = value as System.Collections.IEnumerable;
            if (sequence != null)
            {
                builder.Append('[');
                bool first = true;
                foreach (object item in sequence)
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }

                    first = false;
                    WriteValue(item, builder);
                }

                builder.Append(']');
                return;
            }

            throw new FormatException("MiniJson: 不支持的类型 " + value.GetType().FullName);
        }

        private static void WriteString(string text, StringBuilder builder)
        {
            builder.Append('"');
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < ' ')
                        {
                            builder.Append("\\u");
                            builder.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }
                        break;
                }
            }

            builder.Append('"');
        }

        private static void SkipWhitespace(string text, ref int index)
        {
            while (index < text.Length)
            {
                char c = text[index];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                {
                    index++;
                }
                else
                {
                    return;
                }
            }
        }

        private static object ReadValue(string text, ref int index)
        {
            SkipWhitespace(text, ref index);
            if (index >= text.Length)
            {
                throw new FormatException("MiniJson: 意外结束");
            }

            char c = text[index];
            switch (c)
            {
                case '{': return ReadObject(text, ref index);
                case '[': return ReadArray(text, ref index);
                case '"': return ReadString(text, ref index);
                case 't': Expect(text, ref index, "true"); return true;
                case 'f': Expect(text, ref index, "false"); return false;
                case 'n': Expect(text, ref index, "null"); return null;
                default: return ReadNumber(text, ref index);
            }
        }

        private static void Expect(string text, ref int index, string literal)
        {
            if (string.Compare(text, index, literal, 0, literal.Length, StringComparison.Ordinal) != 0)
            {
                throw new FormatException("MiniJson: 期望 " + literal + " @" + index);
            }

            index += literal.Length;
        }

        private static Dictionary<string, object> ReadObject(string text, ref int index)
        {
            Dictionary<string, object> obj = new Dictionary<string, object>();
            index++; // '{'
            SkipWhitespace(text, ref index);
            if (index < text.Length && text[index] == '}')
            {
                index++;
                return obj;
            }

            while (true)
            {
                SkipWhitespace(text, ref index);
                if (index >= text.Length || text[index] != '"')
                {
                    throw new FormatException("MiniJson: 对象键必须是字符串 @" + index);
                }

                string key = ReadString(text, ref index);
                SkipWhitespace(text, ref index);
                if (index >= text.Length || text[index] != ':')
                {
                    throw new FormatException("MiniJson: 期望 ':' @" + index);
                }

                index++;
                obj[key] = ReadValue(text, ref index);
                SkipWhitespace(text, ref index);
                if (index < text.Length && text[index] == ',')
                {
                    index++;
                    continue;
                }

                if (index < text.Length && text[index] == '}')
                {
                    index++;
                    return obj;
                }

                throw new FormatException("MiniJson: 期望 ',' 或 '}' @" + index);
            }
        }

        private static List<object> ReadArray(string text, ref int index)
        {
            List<object> list = new List<object>();
            index++; // '['
            SkipWhitespace(text, ref index);
            if (index < text.Length && text[index] == ']')
            {
                index++;
                return list;
            }

            while (true)
            {
                list.Add(ReadValue(text, ref index));
                SkipWhitespace(text, ref index);
                if (index < text.Length && text[index] == ',')
                {
                    index++;
                    continue;
                }

                if (index < text.Length && text[index] == ']')
                {
                    index++;
                    return list;
                }

                throw new FormatException("MiniJson: 期望 ',' 或 ']' @" + index);
            }
        }

        private static string ReadString(string text, ref int index)
        {
            StringBuilder builder = new StringBuilder();
            index++; // '"'
            while (index < text.Length)
            {
                char c = text[index++];
                if (c == '"')
                {
                    return builder.ToString();
                }

                if (c == '\\')
                {
                    if (index >= text.Length)
                    {
                        break;
                    }

                    char escape = text[index++];
                    switch (escape)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            if (index + 4 > text.Length)
                            {
                                throw new FormatException("MiniJson: \\u 转义不完整 @" + index);
                            }

                            builder.Append((char)int.Parse(text.Substring(index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            index += 4;
                            break;
                        default:
                            throw new FormatException("MiniJson: 非法转义 \\" + escape);
                    }
                }
                else
                {
                    builder.Append(c);
                }
            }

            throw new FormatException("MiniJson: 字符串未闭合");
        }

        private static object ReadNumber(string text, ref int index)
        {
            int start = index;
            if (index < text.Length && text[index] == '-')
            {
                index++;
            }

            while (index < text.Length)
            {
                char c = text[index];
                if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-')
                {
                    index++;
                }
                else
                {
                    break;
                }
            }

            if (index == start)
            {
                throw new FormatException("MiniJson: 非法值 @" + index);
            }

            string token = text.Substring(start, index - start);
            double number;
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            {
                throw new FormatException("MiniJson: 非法数字 " + token);
            }

            return number;
        }
    }
}
