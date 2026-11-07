// 用途：极简 JSON —— 只要解析 object/array/string/number/bool/null 和拼串时够用就行。
// 状态：现行。自己写是为了不引 Newtonsoft 也不依赖 Unity 的 JsonUtility（它不支持字典）。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MaiPlanner
{
    internal static class JsonMini
    {
        internal static object Parse(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }
            int pos = 0;
            try
            {
                var value = ParseValue(text, ref pos);
                SkipWs(text, ref pos);
                return pos >= text.Length ? value : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>只接受顶层是对象的解析；失败返回 null</summary>
        internal static Dictionary<string, object> ParseObj(string text)
        {
            return Parse(text) as Dictionary<string, object>;
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length)
            {
                return null;
            }
            var ch = s[i];
            switch (ch)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return ParseString(s, ref i);
                case 't':
                    i += 4;
                    return true;
                case 'f':
                    i += 5;
                    return false;
                case 'n':
                    i += 4;
                    return null;
                default: return ParseNumber(s, ref i);
            }
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var map = new Dictionary<string, object>(StringComparer.Ordinal);
            i++;                       // {
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}')
            {
                i++;
                return map;
            }
            while (i < s.Length)
            {
                var key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':')
                {
                    break;
                }
                i++;
                map[key] = ParseValue(s, ref i);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',')
                {
                    i++;
                    continue;
                }
                if (i < s.Length && s[i] == '}')
                {
                    i++;
                }
                break;
            }
            return map;
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            var list = new List<object>();
            i++;                       // [
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']')
            {
                i++;
                return list;
            }
            while (i < s.Length)
            {
                list.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',')
                {
                    i++;
                    continue;
                }
                if (i < s.Length && s[i] == ']')
                {
                    i++;
                }
                break;
            }
            return list;
        }

        private static string ParseString(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"')
            {
                return "";
            }
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                var ch = s[i++];
                if (ch == '"')
                {
                    break;
                }
                if (ch != '\\')
                {
                    sb.Append(ch);
                    continue;
                }
                if (i >= s.Length)
                {
                    break;
                }
                var esc = s[i++];
                switch (esc)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 <= s.Length
                            && int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber,
                                            CultureInfo.InvariantCulture, out var code))
                        {
                            sb.Append((char)code);
                            i += 4;
                        }
                        break;
                    default: sb.Append(esc); break;
                }
            }
            return sb.ToString();
        }

        private static object ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.'
                                    || s[i] == 'e' || s[i] == 'E'))
            {
                i++;
            }
            var raw = s.Substring(start, i - start);
            if (raw.IndexOf('.') < 0 && raw.IndexOf('e') < 0 && raw.IndexOf('E') < 0)
            {
                long l;
                if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out l))
                {
                    return l;
                }
            }
            double d;
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? (object)d : 0L;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
            {
                i++;
            }
        }

        // ── 取字段 ──
        internal static object Get(Dictionary<string, object> map, string key)
        {
            object v;
            return map != null && map.TryGetValue(key, out v) ? v : null;
        }

        internal static string Str(Dictionary<string, object> map, string key)
        {
            var v = Get(map, key);
            return v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        internal static int Int(Dictionary<string, object> map, string key)
        {
            var v = Get(map, key);
            if (v == null)
            {
                return 0;
            }
            if (v is long)
            {
                return (int)(long)v;
            }
            if (v is double)
            {
                return (int)(double)v;
            }
            int n;
            return int.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture),
                                NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : 0;
        }

        internal static long Long(Dictionary<string, object> map, string key)
        {
            var v = Get(map, key);
            if (v is long)
            {
                return (long)v;
            }
            if (v is double)
            {
                return (long)(double)v;
            }
            long n;
            return long.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture),
                                 NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : 0;
        }

        internal static double Dbl(Dictionary<string, object> map, string key)
        {
            var v = Get(map, key);
            if (v is double)
            {
                return (double)v;
            }
            if (v is long)
            {
                return (long)v;
            }
            double n;
            return double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture),
                                   NumberStyles.Float, CultureInfo.InvariantCulture, out n) ? n : 0;
        }

        internal static bool Bool(Dictionary<string, object> map, string key)
        {
            var v = Get(map, key);
            if (v is bool)
            {
                return (bool)v;
            }
            if (v is long)
            {
                return (long)v != 0;
            }
            return string.Equals(Convert.ToString(v, CultureInfo.InvariantCulture), "true",
                                 StringComparison.OrdinalIgnoreCase);
        }

        internal static List<object> Arr(Dictionary<string, object> map, string key)
        {
            return Get(map, key) as List<object>;
        }

        internal static Dictionary<string, object> Obj(Dictionary<string, object> map, string key)
        {
            return Get(map, key) as Dictionary<string, object>;
        }

        // ── 拼串 ──
        internal static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return "";
            }
            var sb = new StringBuilder(s.Length + 8);
            foreach (var ch in s)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 0x20)
                        {
                            sb.Append("\\u").Append(((int)ch).ToString("x4"));
                        }
                        else
                        {
                            sb.Append(ch);
                        }
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
