// 用途：机台玩家与成绩的只读快照。成绩只从游戏内存读，不改游戏存档。
// 状态：现行。主线程采样 → 缓存；网页线程只读缓存。
using System;
using System.Collections.Generic;
using System.Text;
using Manager;

namespace MaiPlanner
{
    internal static class PlayerStore
    {
        internal sealed class Sheet
        {
            public int ChartId;
            public uint Achievement;      // ×10000
            public int Rank;
            public int Combo;
            public int Sync;
            public uint PlayCount;
            public uint DeluxScore;
        }

        internal sealed class Player
        {
            public int Index;
            public string Name = "";
            public bool IsEntry;
            public int Rating;
            public readonly Dictionary<int, Dictionary<int, Sheet>> Scores =
                new Dictionary<int, Dictionary<int, Sheet>>();
        }

        private static volatile List<Player> _players = new List<Player>();
        private static volatile bool _seen;
        private static int _lastBuild;
        private static int _lastSampleAt;

        /// <summary>是否已经见过至少一个登录玩家（没见过就别让页面用 0 号空档当数据）</summary>
        internal static bool AnyEntry
        {
            get
            {
                foreach (var p in _players)
                {
                    if (p.IsEntry)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        internal static List<Player> All { get { return _players; } }

        internal static Player Get(int index)
        {
            foreach (var p in _players)
            {
                if (p.Index == index)
                {
                    return p;
                }
            }
            return null;
        }

        /// <summary>
        /// 主线程每帧：只有登录组合变了才重建（成绩是几千条，不能每帧全捞）。
        /// 另有一个慢速兜底（5 秒）应对同名登录/成绩本身变化。
        /// </summary>
        internal static void Tick()
        {
            if (!MainThread.IsMain)
            {
                return;
            }
            if (_players.Count == 0)
            {
                _lastBuild = 0;        // 还没拉过，立刻建
            }
            var now = Environment.TickCount;
            var sig = EntrySignature();
            var changed = sig != _signature;
            var stale = _lastBuild == 0 || unchecked(now - _lastBuild) >= 5000;
            if (!changed && !stale)
            {
                return;
            }
            if (unchecked(now - _lastSampleAt) < 400 && now >= _lastSampleAt && _lastSampleAt != 0)
            {
                return;                // 最快 400ms 一次，别连着重捞
            }
            _lastSampleAt = now;
            _signature = sig;
            _lastBuild = now;
            Sample();
        }

        private static string _signature = "";

        /// <summary>登录组合的指纹（谁登录了、叫什么、rating 多少），只在主线程用</summary>
        private static string EntrySignature()
        {
            try
            {
                var mgr = MAI2.Util.Singleton<UserDataManager>.Instance;
                if (mgr == null)
                {
                    return "n";
                }
                var sb = new StringBuilder();
                for (int i = 0; i < 4; i++)
                {
                    UserData ud = null;
                    try { ud = mgr.GetUserData((long)i); } catch { }
                    if (ud == null)
                    {
                        sb.Append('x');
                        continue;
                    }
                    try { sb.Append(ud.IsEntry ? '1' : '0'); } catch { sb.Append('?'); }
                    sb.Append(':');
                    try { sb.Append(ud.Detail == null ? 0 : (int)ud.Detail.Rating); } catch { }
                    sb.Append(':');
                    try { sb.Append(ud.Detail == null ? "" : (ud.Detail.UserName ?? "")).Append('|'); } catch { }
                }
                return sb.ToString();
            }
            catch
            {
                return "e";
            }
        }

        private static void Sample()
        {
            try
            {
                var mgr = MAI2.Util.Singleton<UserDataManager>.Instance;
                if (mgr == null)
                {
                    return;
                }
                var list = new List<Player>();
                for (int i = 0; i < 4; i++)
                {
                    UserData ud = null;
                    try { ud = mgr.GetUserData((long)i); } catch { }
                    if (ud == null)
                    {
                        continue;
                    }
                    var p = new Player();
                    p.Index = i;
                    try { p.IsEntry = ud.IsEntry; } catch { }
                    try { p.Name = ud.Detail == null ? "" : (ud.Detail.UserName ?? ""); } catch { }
                    try { p.Rating = ud.Detail == null ? 0 : (int)ud.Detail.Rating; } catch { }
                    if (p.IsEntry)
                    {
                        _seen = true;
                        CollectScores(ud, p);
                    }
                    list.Add(p);
                }
                _players = list;
            }
            catch (Exception e)
            {
                ModLog.WarnOnce("采样玩家失败: " + e.Message);
            }
        }

        private static void CollectScores(UserData ud, Player p)
        {
            try
            {
                var dic = ud.ScoreDic;
                if (dic == null)
                {
                    return;
                }
                for (int diff = 0; diff < dic.Length; diff++)
                {
                    var byMusic = dic[diff];
                    if (byMusic == null)
                    {
                        continue;
                    }
                    foreach (var kv in byMusic)
                    {
                        var us = kv.Value;
                        if (us == null)
                        {
                            continue;
                        }
                        var sh = new Sheet();
                        sh.ChartId = diff;
                        sh.Achievement = us.achivement;
                        sh.Rank = (int)us.scoreRank;
                        sh.Combo = (int)us.combo;
                        sh.Sync = (int)us.sync;
                        sh.PlayCount = us.playcount;
                        sh.DeluxScore = us.deluxscore;
                        Dictionary<int, Sheet> byChart;
                        if (!p.Scores.TryGetValue(kv.Key, out byChart))
                        {
                            byChart = new Dictionary<int, Sheet>();
                            p.Scores[kv.Key] = byChart;
                        }
                        byChart[diff] = sh;
                    }
                }
            }
            catch (Exception e)
            {
                ModLog.WarnOnce("读取玩家成绩失败: " + e.Message);
            }
        }

        internal static Sheet ScoreOf(Player p, int internalId, int chartId)
        {
            if (p != null)
            {
                var ov = OverrideOf(p.Index, internalId, chartId);
                if (ov != null)
                {
                    return ov;
                }
            }
            Dictionary<int, Sheet> byChart;
            if (p != null && p.Scores.TryGetValue(internalId, out byChart))
            {
                Sheet s;
                if (byChart.TryGetValue(chartId, out s))
                {
                    return s;
                }
            }
            return null;
        }

        // ── 本地改分（只在 mod 内生效，不动游戏存档）──
        private static readonly Dictionary<string, Sheet> _overrides = new Dictionary<string, Sheet>();

        private static string OverrideKey(int user, int internalId, int chartId)
        {
            return user + ":" + internalId + ":" + chartId;
        }

        /// <summary>把一条成绩写进覆盖表；之后读取优先返回覆盖值。</summary>
        internal static Sheet Override(int user, int internalId, int chartId, uint achievement,
                                       int rank, int combo, int sync, uint playCount)
        {
            return Override(user, internalId, chartId, achievement, rank, combo, sync, playCount, 0);
        }

        internal static Sheet Override(int user, int internalId, int chartId, uint achievement,
                                       int rank, int combo, int sync, uint playCount, int deluxScore)
        {
            var sh = new Sheet();
            sh.ChartId = chartId;
            sh.Achievement = achievement;
            sh.Rank = rank;
            sh.Combo = combo;
            sh.Sync = sync;
            sh.PlayCount = playCount;
            sh.DeluxScore = (uint)deluxScore;
            _overrides[OverrideKey(user, internalId, chartId)] = sh;
            return sh;
        }

        internal static Sheet OverrideOf(int user, int internalId, int chartId)
        {
            Sheet s;
            return _overrides.TryGetValue(OverrideKey(user, internalId, chartId), out s) ? s : null;
        }

        internal static string OverridesJson(int user)
        {
            var sb = new System.Text.StringBuilder("{\"entries\":[");
            bool first = true;
            var prefix = user + ":";
            foreach (var kv in _overrides)
            {
                if (!kv.Key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }
                var bits = kv.Key.Split(':');
                if (bits.Length != 3)
                {
                    continue;
                }
                if (!first)
                {
                    sb.Append(',');
                }
                first = false;
                sb.Append("{\"internalId\":").Append(bits[1]).Append(",\"chartid\":").Append(bits[2])
                  .Append(",\"achievement\":").Append(kv.Value.Achievement)
                  .Append(",\"rank\":").Append(kv.Value.Rank)
                  .Append(",\"combo\":").Append(kv.Value.Combo)
                  .Append(",\"sync\":").Append(kv.Value.Sync)
                  .Append(",\"playCount\":").Append(kv.Value.PlayCount).Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>改过分的谱：给分析/统计用，按 (internalId, chartid) 给出</summary>
        internal static IEnumerable<KeyValuePair<string, Sheet>> OverridesOf(int user)
        {
            var prefix = user + ":";
            foreach (var kv in _overrides)
            {
                if (!kv.Key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }
                var bits = kv.Key.Split(':');
                if (bits.Length != 3)
                {
                    continue;
                }
                yield return new KeyValuePair<string, Sheet>(bits[1] + ":" + bits[2], kv.Value);
            }
        }
    }
}
