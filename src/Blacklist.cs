// 用途：黑名单的纯逻辑 —— 时长 → 天数、到期判定、读条目、取当前生效集合。
//       本文件是 lib/blacklist.py 的等价物。规则：addedAt 只到日，含到期当天仍生效，forever 永久。
// 状态：现行（与源项目 lib/blacklist.py 一一对应）。
// 落盘形状：既认源项目的 {"duration":"7d","addedAt":"YYYY-MM-DD[THH:MM:SS]"}，
//           也认 UserFiles 早期写的 {"until": unix 秒}（0 / 缺省 = 永久）。
using System;
using System.Collections.Generic;
using System.Globalization;

namespace MaiPlanner
{
    internal static class Blacklist
    {
        /// <summary>时长 → 天数；永久 = null；认不出 = -1。</summary>
        private static readonly Dictionary<string, int> DurationDays = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            { "1d", 1 }, { "3d", 3 }, { "7d", 7 }, { "30d", 30 },
        };

        private static readonly string[] Permanent = { "forever", "perm", "永久" };

        /// <summary>'1d'/'3d'/'7d'/'30d' → 天数；永久 → null；认不出 → -1。</summary>
        internal static int? ParseDuration(string text)
        {
            var t = (text ?? "").Trim().ToLowerInvariant();
            if (IsPermanent(t))
            {
                return null;
            }
            return ParseDurationDays(t);
        }

        /// <summary>时长 → 天数（永久 / 认不出都给 -1，用 IsPermanent 区分）。</summary>
        private static int ParseDurationDays(string t)
        {
            int days;
            if (DurationDays.TryGetValue(t, out days))
            {
                return days;
            }
            if (t.Length > 1 && t.EndsWith("d", StringComparison.Ordinal))
            {
                int n;
                var head = t.Substring(0, t.Length - 1);
                if (int.TryParse(head, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                {
                    return n;
                }
            }
            return -1;
        }

        /// <summary>时长文本是否表示永久。</summary>
        internal static bool IsPermanent(string text)
        {
            var t = (text ?? "").Trim().ToLowerInvariant();
            for (int i = 0; i < Permanent.Length; i++)
            {
                if (string.Equals(t, Permanent[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>时长文本是否认得出来（永久也算认得）。</summary>
        internal static bool IsDurationKnown(string text)
        {
            return IsPermanent(text) || ParseDurationDays((text ?? "").Trim().ToLowerInvariant()) >= 0;
        }

        /// <summary>条目里的 addedAt（只取日期部分）；解析不了回 null。</summary>
        internal static DateTime? AddedDate(Dictionary<string, object> entry)
        {
            var raw = JsonMini.Str(entry, "addedAt");
            if (string.IsNullOrEmpty(raw))
            {
                return null;
            }
            DateTime dt;
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                                   DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out dt))
            {
                return dt.Date;
            }
            return null;
        }

        /// <summary>
        /// 到期日（含当天）。永久 → null；解析不了 → MinValue 之外的哨兵。
        /// 用 out bool 表达三态（与 Python 的 None / False / date 对应）。
        /// 返回 true 表示能判定，expires 有值（永久时 expires = DateTime.MaxValue）。
        /// duration 认不出 → 返回 false（源项目 expiry_of 返回 False，即不生效）。
        /// </summary>
        internal static bool TryExpiry(Dictionary<string, object> entry, out DateTime expires)
        {
            expires = DateTime.MaxValue;
            var duration = JsonMini.Str(entry, "duration");
            bool hasUntil = JsonMini.Get(entry, "until") != null;
            if (IsPermanent(duration))
            {
                return true;                       // 永久：MaxValue
            }
            if (string.IsNullOrEmpty(duration) && hasUntil)
            {
                // 只有 until 的形状（UserFiles 早期落盘）：>0 按到期日，0 = 永久
                var u0 = JsonMini.Long(entry, "until");
                if (u0 <= 0)
                {
                    return true;                   // 永久
                }
                expires = FromUnixSeconds(u0).Date;
                return true;
            }
            if (!IsDurationKnown(duration))
            {
                // duration 认不出：源项目判 False（不生效）；带 until 的旧条目退一步按 until 判
                if (hasUntil)
                {
                    var u1 = JsonMini.Long(entry, "until");
                    if (u1 <= 0)
                    {
                        return true;
                    }
                    expires = FromUnixSeconds(u1).Date;
                    return true;
                }
                expires = DateTime.MinValue;
                return false;
            }
            int days = ParseDurationDays(duration.Trim().ToLowerInvariant());
            var added = AddedDate(entry);
            if (added == null)
            {
                // 有 duration 但没有 addedAt：源项目判 false（不生效）。
                // 这里若还有 until 就退一步用 until，否则按不生效处理（expires = MinValue）。
                var until = JsonMini.Long(entry, "until");
                if (until > 0)
                {
                    expires = FromUnixSeconds(until).Date;
                    return true;
                }
                expires = DateTime.MinValue;
                return false;
            }
            expires = added.Value.AddDays(days);
            return true;
        }

        /// <summary>条目当前是否生效。today 缺省用本地今天。</summary>
        internal static bool IsActive(Dictionary<string, object> entry)
        {
            return IsActive(entry, DateTime.Today);
        }

        /// <summary>条目在给定日期是否生效（含到期当天）。</summary>
        internal static bool IsActive(Dictionary<string, object> entry, DateTime today)
        {
            if (entry == null)
            {
                return false;
            }
            DateTime expires;
            if (!TryExpiry(entry, out expires))
            {
                return false;
            }
            if (expires == DateTime.MaxValue)
            {
                return true;
            }
            return today.Date <= expires.Date;
        }

        /// <summary>读黑名单文件里的 entries；读不到就是空表。</summary>
        internal static List<Dictionary<string, object>> LoadEntries(string user)
        {
            var list = new List<Dictionary<string, object>>();
            var doc = JsonMini.ParseObj(UserFiles.ReadText(user, "blacklist.json"));
            if (doc == null)
            {
                return list;
            }
            var arr = JsonMini.Arr(doc, "entries");
            if (arr == null)
            {
                return list;
            }
            foreach (var item in arr)
            {
                var row = item as Dictionary<string, object>;
                if (row != null)
                {
                    list.Add(row);
                }
            }
            return list;
        }

        /// <summary>当前生效中的黑名单条目（过期的不算）。</summary>
        internal static List<Dictionary<string, object>> ActiveEntries(string user)
        {
            var today = DateTime.Today;
            var outList = new List<Dictionary<string, object>>();
            foreach (var entry in LoadEntries(user))
            {
                if (IsActive(entry, today))
                {
                    outList.Add(entry);
                }
            }
            return outList;
        }

        /// <summary>当前生效中的黑名单 key 集合（"internalId:chartid"）。</summary>
        internal static HashSet<string> ActiveKeys(string user)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in ActiveEntries(user))
            {
                var iid = JsonMini.Get(entry, "internalId");
                var cid = JsonMini.Get(entry, "chartid");
                if (iid == null || cid == null)
                {
                    continue;
                }
                set.Add(UserFiles.Key(JsonMini.Int(entry, "internalId"), JsonMini.Int(entry, "chartid")));
            }
            return set;
        }

        /// <summary>新增一条（duration 见 ParseDuration；addedAt 记今天）。</summary>
        internal static void Add(string user, int internalId, int chartId, string duration)
        {
            var dur = IsDurationKnown(duration) ? (duration ?? "forever").Trim().ToLowerInvariant() : "forever";
            var entries = LoadEntries(user);
            var key = UserFiles.Key(internalId, chartId);
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (UserFiles.Key(JsonMini.Int(entries[i], "internalId"),
                                  JsonMini.Int(entries[i], "chartid")) == key)
                {
                    entries.RemoveAt(i);
                }
            }
            var row = new Dictionary<string, object>(StringComparer.Ordinal);
            row["internalId"] = internalId;
            row["chartid"] = chartId;
            row["duration"] = dur;
            row["addedAt"] = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            entries.Add(row);
            Save(user, entries);
        }

        /// <summary>删一条；返回是否删掉了。</summary>
        internal static bool Remove(string user, int internalId, int chartId)
        {
            var entries = LoadEntries(user);
            var key = UserFiles.Key(internalId, chartId);
            bool hit = false;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (UserFiles.Key(JsonMini.Int(entries[i], "internalId"),
                                  JsonMini.Int(entries[i], "chartid")) == key)
                {
                    entries.RemoveAt(i);
                    hit = true;
                }
            }
            if (hit)
            {
                Save(user, entries);
            }
            return hit;
        }

        /// <summary>到期时间戳（毫秒精度足够）；永久回 0。供接口原样回显。</summary>
        internal static long ExpiresUnix(Dictionary<string, object> entry)
        {
            DateTime expires;
            if (!TryExpiry(entry, out expires) || expires == DateTime.MaxValue)
            {
                return 0;
            }
            if (expires == DateTime.MinValue)
            {
                return 0;
            }
            return ToUnixSeconds(expires.AddDays(1).AddSeconds(-1));
        }

        private static void Save(string user, List<Dictionary<string, object>> entries)
        {
            var sb = new System.Text.StringBuilder("{\"entries\":[");
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                var row = entries[i];
                sb.Append("{\"internalId\":").Append(JsonMini.Int(row, "internalId"));
                sb.Append(",\"chartid\":").Append(JsonMini.Int(row, "chartid"));
                sb.Append(",\"duration\":\"").Append(JsonMini.Esc(JsonMini.Str(row, "duration"))).Append('"');
                var added = JsonMini.Str(row, "addedAt");
                if (added.Length > 0)
                {
                    sb.Append(",\"addedAt\":\"").Append(JsonMini.Esc(added)).Append('"');
                }
                sb.Append('}');
            }
            sb.Append("]}");
            UserFiles.WriteText(user, "blacklist.json", sb.ToString());
        }

        private static DateTime FromUnixSeconds(long seconds)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds).ToLocalTime();
        }

        private static long ToUnixSeconds(DateTime local)
        {
            return (long)(local.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }
    }
}
