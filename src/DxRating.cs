// 用途：按版本算玩家的总 DX rating（B35 + B15 分池取前 35 / 前 15）。产出 dxrating.json。
//       本文件是 tools/dxrating/dxrating.py 的等价物：池规则见 lib/versions.py 的 B15_DOUBLE_FROM 说明。
// 状态：现行。
//
// 【池子怎么划】旧框（FiNALE 及以前）不能作为「当前版本」，但旧曲仍可进 B35。
//   DX 时代每个版本：B15 = 本版本 + 上一版本（CiRCLE 起双版本）；B35 = 再往前的全部版本。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace MaiPlanner
{
    internal static class DxRating
    {
        internal const int B15Count = 15;
        internal const int B35Count = 35;

        private static readonly object _gate = new object();
        private static readonly Dictionary<string, string> _cache = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> _cacheKey = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>→ (B15 版本集合, B35 版本集合)。B35 永远是 B15 范围之前的全部版本。</summary>
        internal static void PoolsOf(string version, out List<string> b15, out List<string> b35)
        {
            var name = Versions.Canonical(version);
            int i = Versions.IndexOf(name);
            b15 = new List<string>();
            b35 = new List<string>();
            if (i < 0)
            {
                return;
            }
            int doubleFrom = Versions.IndexOf(Versions.B15DoubleFrom);
            int span = i >= doubleFrom ? 2 : 1;
            int from = Math.Max(0, i - span + 1);
            for (int k = from; k <= i; k++)
            {
                b15.Add(Versions.Order[k]);
            }
            for (int k = 0; k < from; k++)
            {
                b35.Add(Versions.Order[k]);
            }
        }

        private static string NowStamp()
        {
            return DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) + "+0800";
        }

        /// <summary>按玩家索引 + 版本取 dxrating.json 文本（顶层带 ok:true）；失败回 null。</summary>
        internal static string Build(int user, string version)
        {
            string json, error;
            Json(user.ToString(CultureInfo.InvariantCulture), version, out json, out error);
            return json;
        }

        /// <summary>结果 JSON（顶层带 ok:true）。失败时 json 为 null、error 非空。</summary>
        internal static void Json(string user, string version, out string json, out string error)
        {
            json = null;
            error = null;
            var u = string.IsNullOrEmpty(user) ? "0" : user;
            var ver = Versions.Canonical(string.IsNullOrEmpty(version) ? Versions.DefaultRatingVersion : version);
            if (!Versions.IsRatingVersion(ver))
            {
                error = "不支持的 DX rating 版本";
                return;
            }
            var player = PlayerStore.Get(ParseIndex(u));
            if (player == null)
            {
                error = "没有这个玩家";
                return;
            }
            var key = ver + "|" + Fingerprint(player);
            lock (_gate)
            {
                string oldKey, oldJson;
                if (_cacheKey.TryGetValue(u, out oldKey) && oldKey == key
                    && _cache.TryGetValue(u, out oldJson))
                {
                    json = oldJson;
                    return;
                }
            }
            try
            {
                var text = BuildJson(u, player, ver);
                lock (_gate)
                {
                    _cache[u] = text;
                    _cacheKey[u] = key;
                }
                UserFiles.WriteText(u, "dxrating.json", text);
                json = text;
            }
            catch (Exception e)
            {
                error = "算 rating 失败：" + e.Message;
            }
        }

        private static string Fingerprint(PlayerStore.Player p)
        {
            var sb = new StringBuilder(64);
            sb.Append(GameSnapshot.BuiltAt).Append('|');
            long sum = 0;
            int n = 0;
            foreach (var kv in p.Scores)
            {
                foreach (var kv2 in kv.Value)
                {
                    var sh = kv2.Value;
                    if (sh == null)
                    {
                        continue;
                    }
                    n++;
                    sum = (sum * 31 + sh.Achievement + sh.Combo * 7L) % 1000000007L;
                }
            }
            sb.Append(n).Append('|').Append(sum);
            return sb.ToString();
        }

        private static int ParseIndex(string user)
        {
            int n;
            return int.TryParse(user, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : 0;
        }

        private sealed class Row
        {
            internal int InternalId;
            internal int ChartId;
            internal string Version;
            internal double Level;
            internal double Achievement;
            internal double Coef;
            internal string Rank;
            internal bool Ap;
            internal int Rating;
        }

        private static string BuildJson(string user, PlayerStore.Player player, string version)
        {
            List<string> b15Versions, b35Versions;
            PoolsOf(version, out b15Versions, out b35Versions);
            var b15Set = new HashSet<string>(b15Versions, StringComparer.Ordinal);
            var b35Set = new HashSet<string>(b35Versions, StringComparer.Ordinal);

            // 曲库：key → (version, level)，只在有成绩时才需要
            var library = new Dictionary<string, AnalysisChart>(StringComparer.Ordinal);
            foreach (var c in Analysis.LoadCharts())
            {
                library[c.CKey] = c;
            }

            var b15Pool = new List<Row>();
            var b35Pool = new List<Row>();
            int skippedNoLevel = 0, skippedNoVersion = 0;
            foreach (var kv in player.Scores)
            {
                foreach (var kv2 in kv.Value)
                {
                    var sh = kv2.Value;
                    if (sh == null)
                    {
                        continue;
                    }
                    var ckey = UserFiles.Key(kv.Key, kv2.Key);
                    AnalysisChart chart;
                    if (!library.TryGetValue(ckey, out chart) || chart.Level <= 0)
                    {
                        skippedNoLevel++;
                        continue;
                    }
                    double ach = sh.Achievement / 10000.0;
                    if (ach <= 0)
                    {
                        continue;
                    }
                    bool ap = Rating.IsAp(sh.Combo);
                    var row = new Row();
                    row.InternalId = chart.InternalId;
                    row.ChartId = chart.ChartId;
                    row.Version = chart.Version;
                    row.Level = chart.Level;
                    row.Achievement = ach;
                    double coef;
                    string rank;
                    Rating.Coefficient(ach, out coef, out rank);
                    row.Coef = coef;
                    row.Rank = rank;
                    row.Ap = ap;
                    row.Rating = Rating.Of(chart.Level, ach, ap);
                    if (b15Set.Contains(row.Version))
                    {
                        b15Pool.Add(row);
                    }
                    else if (b35Set.Contains(row.Version))
                    {
                        b35Pool.Add(row);
                    }
                    else
                    {
                        skippedNoVersion++;
                    }
                }
            }
            var b15 = b15Pool.OrderByDescending(delegate (Row r) { return r.Rating; }).Take(B15Count).ToList();
            var b35 = b35Pool.OrderByDescending(delegate (Row r) { return r.Rating; }).Take(B35Count).ToList();
            int total = 0;
            foreach (var r in b15)
            {
                total += r.Rating;
            }
            foreach (var r in b35)
            {
                total += r.Rating;
            }

            var sb = new StringBuilder(8192);
            sb.Append("{\"ok\":true,\"schema\":1,\"runId\":\"mod\"");
            sb.Append(",\"generatedAt\":\"").Append(JsonMini.Esc(NowStamp())).Append('"');
            sb.Append(",\"kind\":\"dxrating\"");
            sb.Append(",\"user\":").Append(Q(user));
            sb.Append(",\"version\":").Append(Q(version));
            sb.Append(",\"b15Versions\":").Append(Arr(b15Versions));
            sb.Append(",\"b35Versions\":").Append(Arr(b35Versions));
            sb.Append(",\"dxRating\":").Append(total);
            sb.Append(",\"b15\":[");
            AppendRows(sb, b15);
            sb.Append(']');
            sb.Append(",\"b35\":[");
            AppendRows(sb, b35);
            sb.Append(']');
            sb.Append(",\"counts\":{\"b15\":").Append(b15.Count)
              .Append(",\"b35\":").Append(b35.Count)
              .Append(",\"candidatesB15\":").Append(b15Pool.Count)
              .Append(",\"candidatesB35\":").Append(b35Pool.Count)
              .Append(",\"skippedNoLevel\":").Append(skippedNoLevel)
              .Append(",\"skippedOtherVersion\":").Append(skippedNoVersion).Append('}');
            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendRows(StringBuilder sb, List<Row> rows)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                var r = rows[i];
                sb.Append("{\"internalId\":").Append(r.InternalId);
                sb.Append(",\"chartid\":").Append(r.ChartId);
                sb.Append(",\"version\":").Append(Q(r.Version));
                sb.Append(",\"level\":").Append(N(r.Level));
                sb.Append(",\"achievement\":").Append(N(Math.Round(r.Achievement, 4)));
                sb.Append(",\"coefficient\":").Append(N(r.Coef));
                sb.Append(",\"rank\":").Append(Q(r.Rank));
                sb.Append(",\"ap\":").Append(r.Ap ? "true" : "false");
                sb.Append(",\"rating\":").Append(r.Rating).Append('}');
            }
        }

        private static string Arr(IEnumerable<string> items)
        {            var sb = new StringBuilder();
            sb.Append('[');
            bool first = true;
            foreach (var s in items)
            {
                if (!first)
                {
                    sb.Append(',');
                }
                first = false;
                sb.Append(Q(s));
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static string Q(string s)
        {
            return s == null ? "null" : "\"" + JsonMini.Esc(s) + "\"";
        }

        private static string N(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
            {
                return "0";
            }
            return v.ToString("0.####", CultureInfo.InvariantCulture);
        }
    }
}
