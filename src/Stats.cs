// 用途：统计某个玩家打得怎么样 —— 覆盖率、RANK 分布、族、版本聚合、FC/AP 计数。产出 stats.json。
//       本文件是 tools/scores_stats/scores_stats.py 的等价物，输出与原 stats.json 同构。
// 状态：现行。纯统计，不含账号信息以外的东西。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace MaiPlanner
{
    internal static class Stats
    {
        private static readonly string[] Familes = { "std", "dx", "utage", "utage2p" };
        private static readonly string[] ComboNames = { "没有", "FC", "FC+", "AP", "AP+" };

        private static readonly object _gate = new object();
        private static readonly Dictionary<string, string> _cache = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> _cacheKey = new Dictionary<string, string>(StringComparer.Ordinal);

        private static string NowStamp()
        {
            return DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) + "+0800";
        }

        /// <summary>按玩家索引取 stats.json 文本；没有玩家回 null。供路由直接拼 {"ok":true,"stats":...}。</summary>
        internal static string Build(int user)
        {
            string json, error;
            Json(user.ToString(CultureInfo.InvariantCulture), out json, out error);
            return json;
        }

        /// <summary>返回 (stats.json, error)。error 非空时 json 为 null。</summary>
        internal static void Json(string user, out string json, out string error)
        {
            json = null;
            error = null;
            var u = string.IsNullOrEmpty(user) ? "0" : user;
            var player = PlayerStore.Get(ParseIndex(u));
            if (player == null)
            {
                error = "没有这个玩家";
                return;
            }
            try
            {
                var key = Fingerprint(player);
                lock (_gate)
                {
                    string oldKey;
                    string oldJson;
                    if (_cacheKey.TryGetValue(u, out oldKey) && oldKey == key
                        && _cache.TryGetValue(u, out oldJson))
                    {
                        json = oldJson;
                        return;
                    }
                }
                var text = BuildJson(u, player);
                lock (_gate)
                {
                    _cache[u] = text;
                    _cacheKey[u] = key;
                }
                UserFiles.WriteText(u, "stats.json", text);
                json = text;
            }
            catch (Exception e)
            {
                error = "统计失败：" + e.Message;
            }
        }

        private static string Fingerprint(PlayerStore.Player p)
        {
            var sb = new StringBuilder(64);
            sb.Append(GameSnapshot.BuiltAt).Append('|').Append(p.Index).Append('|');
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
                    sum = (sum * 31 + sh.Achievement + sh.Combo * 7L + sh.Sync * 13L + sh.Rank * 17L) % 1000000007L;
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

        private static double Pct(int part, int whole)
        {
            return whole == 0 ? 0.0 : Math.Round(100.0 * part / whole, 2);
        }

        private static string BuildJson(string user, PlayerStore.Player player)
        {
            // 曲库分母：只统计曲库里有的谱（用户要求）
            var lib = new Dictionary<string, KeyValuePair<string, string>>(StringComparer.Ordinal);
            var familyTotal = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var f in Familes)
            {
                familyTotal[f] = 0;
            }
            foreach (var s in GameSnapshot.Songs)
            {
                if (s == null || s.Disable || s.InternalId <= 0)
                {
                    continue;
                }
                var family = FamilyOf(s.InternalId);
                for (int d = 0; d < 5; d++)
                {
                    if (!s.Enabled[d])
                    {
                        continue;
                    }
                    var version = Versions.Canonical(s.Version);
                    if (string.IsNullOrEmpty(version))
                    {
                        version = "?";
                    }
                    lib[UserFiles.Key(s.InternalId, d)] = new KeyValuePair<string, string>(version, family);
                    familyTotal[family] = familyTotal[family] + 1;
                }
            }

            // 成绩：先按内部号:难度摊平
            var rows = new List<KeyValuePair<string, PlayerStore.Sheet>>();
            foreach (var kv in player.Scores)
            {
                foreach (var kv2 in kv.Value)
                {
                    if (kv2.Value == null)
                    {
                        continue;
                    }
                    rows.Add(new KeyValuePair<string, PlayerStore.Sheet>(UserFiles.Key(kv.Key, kv2.Key), kv2.Value));
                }
            }

            var rankHist = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var name in Rating.RankNames)
            {
                rankHist[name] = 0;
            }
            var comboHist = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var name in ComboNames)
            {
                comboHist[name] = 0;
            }
            var familyPlayed = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var f in Familes)
            {
                familyPlayed[f] = 0;
            }
            var versions = new Dictionary<string, VersionBucket>(StringComparer.Ordinal);
            var playedKeys = new HashSet<string>(StringComparer.Ordinal);
            int hundred = 0, apPlus = 0, fcUp = 0;
            long achievementSum = 0;
            int fdx = 0, fdxPlus = 0;
            var dxHist = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i <= 5; i++)
            {
                dxHist[i.ToString(CultureInfo.InvariantCulture)] = 0;
            }
            int dxUnknown = 0, notInLibrary = 0;

            foreach (var kvp in rows)
            {
                var key = kvp.Key;
                var sh = kvp.Value;
                long achievement = sh.Achievement;
                achievementSum += achievement;
                // dxscore 在 mod 快照里恒有值（0..5），源脚本的 unknown 分支这里不会触发
                var bucket = sh.DeluxScore.ToString(CultureInfo.InvariantCulture);
                dxHist[bucket] = dxHist.ContainsKey(bucket) ? dxHist[bucket] + 1 : 1;

                KeyValuePair<string, string> libHit;
                if (!lib.TryGetValue(key, out libHit))
                {
                    notInLibrary++;
                    continue;
                }
                playedKeys.Add(key);
                if (achievement >= 1000000)
                {
                    hundred++;
                }
                int combo = sh.Combo;
                if (combo >= 1 && combo < ComboNames.Length)
                {
                    comboHist[ComboNames[combo]] = comboHist[ComboNames[combo]] + 1;
                    fcUp++;
                }
                else
                {
                    comboHist[ComboNames[0]] = comboHist[ComboNames[0]] + 1;
                }
                if (combo == 4)
                {
                    apPlus++;
                }
                int sync = sh.Sync;
                if (sync == 3)
                {
                    fdx++;
                }
                else if (sync == 4)
                {
                    fdxPlus++;
                }
                var rankName = Rating.RankName(sh.Rank);
                rankHist[rankName] = rankHist.ContainsKey(rankName) ? rankHist[rankName] + 1 : 1;

                var version = libHit.Key;
                var family = libHit.Value;
                if (!string.IsNullOrEmpty(family))
                {
                    familyPlayed[family] = familyPlayed[family] + 1;
                }
                var vb = Bucket(versions, version);
                vb.Played++;
                if (achievement >= 1000000)
                {
                    vb.Hundred++;
                }
                if (combo == 4)
                {
                    vb.Ap++;
                }
            }
            // 分母：按版本铺满（没打过的版本也要出现）
            foreach (var libHit in lib.Values)
            {
                Bucket(versions, libHit.Key).Sheets++;
            }

            var versionRows = new List<VersionBucket>();
            foreach (var kv in versions)
            {
                var vb = kv.Value;
                vb.Version = kv.Key;
                versionRows.Add(vb);
            }
            versionRows.Sort(delegate (VersionBucket a, VersionBucket b)
            {
                int c = b.Sheets.CompareTo(a.Sheets);
                return c != 0 ? c : string.CompareOrdinal(a.Version, b.Version);
            });

            int totalSheets = familyTotal.Values.Sum();
            var sb = new StringBuilder(4096);
            sb.Append("{\"schema\":1,\"runId\":\"mod\"");
            sb.Append(",\"generatedAt\":\"").Append(JsonMini.Esc(NowStamp())).Append('"');
            sb.Append(",\"kind\":\"scores_stats\"");
            sb.Append(",\"user\":{\"id\":").Append(Q(user)).Append(",\"name\":").Append(Q(player.Name)).Append('}');
            sb.Append(",\"dxRating\":null");
            sb.Append(",\"updatedAt\":\"").Append(JsonMini.Esc(NowStamp())).Append('"');
            int played = playedKeys.Count;
            sb.Append(",\"totals\":{\"sheetsInLibrary\":").Append(totalSheets);
            sb.Append(",\"played\":").Append(played);
            sb.Append(",\"coverage\":").Append(N(Pct(played, totalSheets)));
            sb.Append(",\"hundred\":").Append(hundred);
            sb.Append(",\"hundredPct\":").Append(N(Pct(hundred, played)));
            sb.Append(",\"fc\":").Append(comboHist["FC"]);
            sb.Append(",\"fcPlus\":").Append(comboHist["FC+"]);
            sb.Append(",\"ap\":").Append(comboHist["AP"]);
            sb.Append(",\"apPlus\":").Append(comboHist["AP+"]);
            sb.Append(",\"fdx\":").Append(fdx);
            sb.Append(",\"fdxPlus\":").Append(fdxPlus);
            sb.Append(",\"fullCombo\":").Append(comboHist["FC"] + comboHist["FC+"]);
            sb.Append(",\"allPerfect\":").Append(comboHist["AP"] + comboHist["AP+"]);
            sb.Append(",\"fcUp\":").Append(fcUp);
            sb.Append(",\"apPlusCount\":").Append(apPlus);
            sb.Append(",\"avgAchievement\":").Append(N(rows.Count == 0 ? 0.0 : Math.Round(achievementSum / 10000.0 / rows.Count, 4)));
            sb.Append(",\"notInLibrary\":").Append(notInLibrary).Append('}');

            sb.Append(",\"rank\":{");
            AppendHist(sb, rankHist, Rating.RankNames);
            sb.Append('}');
            sb.Append(",\"dxscore\":{");
            AppendHist(sb, dxHist, new[] { "0", "1", "2", "3", "4", "5" });
            sb.Append('}');
            sb.Append(",\"dxscoreUnknown\":").Append(dxUnknown);
            sb.Append(",\"combo\":{");
            AppendHist(sb, comboHist, ComboNames);
            sb.Append('}');

            sb.Append(",\"families\":{");
            bool ff = true;
            foreach (var f in Familes)
            {
                if (!ff)
                {
                    sb.Append(',');
                }
                ff = false;
                sb.Append(Q(f)).Append(":{\"sheets\":").Append(familyTotal[f])
                  .Append(",\"played\":").Append(familyPlayed[f])
                  .Append(",\"coverage\":").Append(N(Pct(familyPlayed[f], familyTotal[f]))).Append('}');
            }
            sb.Append('}');

            sb.Append(",\"versions\":[");
            for (int i = 0; i < versionRows.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                var vb = versionRows[i];
                sb.Append("{\"version\":").Append(Q(vb.Version));
                sb.Append(",\"sheets\":").Append(vb.Sheets);
                sb.Append(",\"played\":").Append(vb.Played);
                sb.Append(",\"coverage\":").Append(N(Pct(vb.Played, vb.Sheets)));
                sb.Append(",\"hundred\":").Append(vb.Hundred);
                sb.Append(",\"ap\":").Append(vb.Ap).Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private sealed class VersionBucket
        {
            internal string Version = "";
            internal int Sheets;
            internal int Played;
            internal int Hundred;
            internal int Ap;
        }

        private static VersionBucket Bucket(Dictionary<string, VersionBucket> map, string version)
        {
            VersionBucket b;
            if (!map.TryGetValue(version, out b))
            {
                b = new VersionBucket();
                map[version] = b;
            }
            return b;
        }

        private static string FamilyOf(int internalId)
        {
            if (internalId >= 200000)
            {
                return "utage2p";
            }
            if (internalId >= 100000)
            {
                return "utage";
            }
            return internalId >= 10000 ? "dx" : "std";
        }

        private static void AppendHist(StringBuilder sb, Dictionary<string, int> hist, string[] order)
        {
            for (int i = 0; i < order.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                int v;
                hist.TryGetValue(order[i], out v);
                sb.Append(Q(order[i])).Append(':').Append(v);
            }
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
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
