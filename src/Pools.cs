// 用途：打歌助手的卡池条件（md 第 3 节 3.1–3.4 逐条）。
//       mastered / pushable_b35 / pushable_b15 / ap / practice / accuracy / challenge / unplayed /
//       favorite / underdog —— 公共前提（排除宴谱、排除黑名单、只对常规谱）在这里统一把关。
// 状态：现行。条件全部对齐「个人拟合与曲池说明.md」；门槛/潜力算法见 3.1。
using System;
using System.Collections.Generic;

namespace MaiPlanner
{
    /// <summary>卡池设置（underdog 用得到，其余池忽略多余字段）。</summary>
    internal sealed class PoolSpec
    {
        internal string Pool = "";
        internal double? LevelMin;
        internal double? LevelMax;
        internal double? AchMax;
        internal int? ComboMax;
        internal int? SyncMax;
        internal bool ExcludeUnplayed;
        internal string Version = "";
        internal List<string> Difficulties = new List<string>();
        internal string PlateId = "";
    }

    /// <summary>一条候选谱面（助手抽卡用）。</summary>
    internal sealed class Candidate
    {
        internal int InternalId;
        internal int ChartId;
        internal string Title;
        internal string Difficulty;
        internal double Level;
        internal List<string> Labels;
        internal double? Fit;
        internal double? Ach;
        internal string Family;
        internal string Version;
        internal int CardIndex;
    }

    internal static class Pools
    {
        /// <summary>牌子条件：返回该谱是否属于某个牌子（mod 里没有牌子数据，默认 null = 全部通过）。</summary>
        internal delegate bool PlateDelegate(int internalId, int chartId, string plateId);
        internal static PlateDelegate PlateProvider;

        // ---------------------------------------------------------------- 3.1 公共前提

        /// <summary>旧曲 / 新曲门槛：按单曲 rating 降序取第 35 / 15 名；池子不够就是 0。</summary>
        internal static void Cutoffs(AnalysisResult doc, out int b35, out int b15)
        {
            b35 = doc == null ? 0 : doc.B35;
            b15 = doc == null ? 0 : doc.B15;
        }

        /// <summary>推分潜力 = 按 100.0% 和 100.5% 各算一次 rating，取较大的那个（md 3.1）。</summary>
        internal static int PushPotential(double localLevel)
        {
            return Math.Max(Rating.Of(localLevel, 100.0), Rating.Of(localLevel, 100.5));
        }

        /// <summary>谱面是否被当前卡池公共前提排除：宴谱 / 黑名单 / 非常规谱。</summary>
        internal static bool IsExcluded(AnalysisChart chart, HashSet<string> blacklist)
        {
            if (chart == null)
            {
                return true;
            }
            if (chart.Family == "utage" || chart.Family == "utage2p")
            {
                return true;
            }
            return blacklist != null && blacklist.Contains(chart.CKey);
        }

        // ---------------------------------------------------------------- 3.2 各池条件

        /// <summary>
        /// 判定一张谱是否属于某个池。返回 null = 「不属」，非空 = 命中（值就是该标签）。
        /// favorite / underdog 的额外条件由调用方（这里已含黑名单过滤）。
        /// </summary>
        internal static string Match(PoolSpec spec, AnalysisChart chart, AnalysisScore score, AnalysisRec rec,
                                    double[] ability, double[] confRange, HashSet<string> weakPat,
                                    HashSet<string> weakEval, int b35, int b15, bool favorite)
        {
            if (spec == null || chart == null)
            {
                return null;
            }
            string pool = spec.Pool ?? "";
            double abLo = ability[0], abHi = ability[1];
            double confHi = confRange[1];
            double abilityMid = (abLo + abHi) / 2;
            switch (pool)
            {
                case "favorite":
                    // 只认收藏，不要求分析标签（md 3.4）
                    return favorite ? "favorite" : null;

                case "underdog":
                    // 只看自定义筛选，不要求分析标签（md 3.4）
                    return MatchUnderdog(spec, chart, score, "") ? "underdog" : null;

                case "blacklist":
                    return null;    // 由调用方按黑名单单独处理
            }

            if (rec == null)
            {
                // 没有拟合记录（宴谱/无定数）不参与分类池
                return null;
            }
            bool isNew = rec.Pool == "new";
            int cutoff = isNew ? b15 : b35;
            double local = rec.LocalLevel;
            if (score != null && score.Achievement > 0)
            {
                double ach = score.Achievement;
                int combo = score.Combo;
                switch (pool)
                {
                    case "mastered":
                        // 达成率 ≥ 100.6%，能力下界 ≤ 个人拟合 ≤ 能力上界
                        if (ach >= 100.6 && abLo <= rec.Fit && rec.Fit <= abHi)
                        {
                            return "mastered";
                        }
                        return null;

                    case "pushable_b35":
                    case "pushable_b15":
                        if (isNew != (pool == "pushable_b15"))
                        {
                            return null;
                        }
                        // 个人拟合 < 置信上界 - 0.15；达成率 < 100.5；已在 b50 内或推分潜力 > 门槛
                        if (rec.Fit < confHi - 0.15 && ach < 100.5 && local > 0
                            && (rec.InB50 || PushPotential(local) > cutoff))
                        {
                            return pool;
                        }
                        return null;

                    case "ap":
                        // 达成率 > 100.85%；尚未 AP/AP+；已在 b50 内或 AP 后 rating > 门槛
                        if (rec.InConf && ach > 100.85 && local > 0 && combo < 3
                            && (rec.InB50 || Rating.Of(local, ach, true) > cutoff))
                        {
                            return "ap";
                        }
                        return null;

                    case "practice":
                    {
                        // 拟合在置信区间内；达成率 < 100.6；中点 ≤ 评价定数 ≤ 能力上界；
                        // 至少 2 个弱项配置标签，或至少 1 个弱项评价标签
                        if (!rec.InConf || ach >= 100.6 || !(abilityMid < rec.EvalLevel && rec.EvalLevel < abHi))
                        {
                            return null;
                        }
                        int patHits = 0, evalHits = 0;
                        foreach (var t in chart.Tags)
                        {
                            if (weakPat.Contains(t.Name))
                            {
                                patHits++;
                            }
                            if (weakEval.Contains(t.Name))
                            {
                                evalHits++;
                            }
                        }
                        return (patHits >= 2 || evalHits >= 1) ? "practice" : null;
                    }

                    case "accuracy":
                        // 能力下界 ≤ 评价定数 ≤ 能力区间中点；达成率 > 100.7%；尚未 AP/AP+
                        if (abLo <= rec.EvalLevel && rec.EvalLevel <= abilityMid && ach > 100.7 && combo < 3)
                        {
                            return "accuracy";
                        }
                        return null;

                    case "challenge":
                        if (abHi < rec.Fit && rec.Fit <= confHi)
                        {
                            return "challenge";
                        }
                        return null;

                    case "unplayed":
                        return null;
                }
                return null;
            }

            // 没打过（md 3.2 待开荒）：中点 ≤ 评价定数 ≤ 能力上界
            if (pool == "unplayed")
            {
                if (abilityMid <= rec.EvalLevel && rec.EvalLevel <= abHi)
                {
                    return "unplayed";
                }
                return null;
            }
            if (pool == "challenge")
            {
                if (abHi < rec.Fit && rec.Fit <= confHi)
                {
                    return "challenge";
                }
            }
            return null;
        }

        /// <summary>underdog 自定义筛选（server.py assistant_candidates 的 underdog 分支）。</summary>
        private static bool MatchUnderdog(PoolSpec spec, AnalysisChart chart, AnalysisScore score, string _)
        {
            if (spec.LevelMin != null && chart.Level < spec.LevelMin.Value)
            {
                return false;
            }
            if (spec.LevelMax != null && chart.Level > spec.LevelMax.Value)
            {
                return false;
            }
            if (spec.Difficulties != null && spec.Difficulties.Count > 0)
            {
                bool hit = false;
                foreach (var d in spec.Difficulties)
                {
                    if (string.Equals(d, chart.Difficulty, StringComparison.OrdinalIgnoreCase))
                    {
                        hit = true;
                        break;
                    }
                }
                if (!hit)
                {
                    return false;
                }
            }
            if (!string.IsNullOrEmpty(spec.Version)
                && !string.Equals(Versions.Canonical(chart.Version), Versions.Canonical(spec.Version), StringComparison.Ordinal))
            {
                return false;
            }
            bool played = score != null && score.Achievement > 0;
            if (!played && spec.ExcludeUnplayed)
            {
                return false;
            }
            // 成绩 / 连击 / 双人：配置了的条件之间取「或」；三项都没配置时不限制
            bool anyLimit = false;
            bool anyPass = false;
            if (spec.AchMax != null)
            {
                anyLimit = true;
                if (played && score.Achievement < spec.AchMax.Value)
                {
                    anyPass = true;
                }
            }
            if (spec.ComboMax != null)
            {
                anyLimit = true;
                if (played && score.Combo < spec.ComboMax.Value)
                {
                    anyPass = true;
                }
            }
            if (spec.SyncMax != null)
            {
                anyLimit = true;
                if (played && score.Sync < spec.SyncMax.Value)
                {
                    anyPass = true;
                }
            }
            if (anyLimit && !anyPass)
            {
                return false;
            }
            var plate = PlateProvider;
            if (!string.IsNullOrEmpty(spec.PlateId) && plate != null)
            {
                try
                {
                    if (!plate(chart.InternalId, chart.ChartId, spec.PlateId))
                    {
                        return false;
                    }
                }
                catch { }
            }
            return true;
        }

        // ---------------------------------------------------------------- 候选枚举

        /// <summary>
        /// 取某卡池的全部候选（谱面粒度）。doc 由 Analysis 提供；黑名单/收藏从 UserFiles 读。
        /// </summary>
        internal static List<Candidate> Candidates(string user, AnalysisResult doc, PoolSpec spec)
        {
            var outList = new List<Candidate>();
            if (doc == null || spec == null)
            {
                return outList;
            }
            var blacklist = Blacklist.ActiveKeys(user);
            var favorites = UserFiles.Favorites(user);
            var charts = Analysis.LoadCharts();
            var ability = doc.Ability ?? new double[] { 0.0, 99.0 };
            var confRange = new double[] { doc.Lo, doc.Hi };
            var weakPat = new HashSet<string>(doc.WeakPattern ?? new List<string>(), StringComparer.Ordinal);
            var weakEval = new HashSet<string>(doc.WeakEval ?? new List<string>(), StringComparer.Ordinal);

            foreach (var chart in charts)
            {
                if (IsExcluded(chart, blacklist))
                {
                    continue;
                }
                var key = chart.CKey;
                var fav = favorites.Contains(key);
                AnalysisRec rec;
                doc.ByKey.TryGetValue(key, out rec);
                AnalysisScore score = null;
                if (rec != null && rec.Rating >= 0)
                {
                    // 成绩从 PlayerStore 快照取（与 Analysis.LoadScores 同源，这里按需读）
                    var player = PlayerStore.Get(ParseIndex(user));
                    var sh = PlayerStore.ScoreOf(player, chart.InternalId, chart.ChartId);
                    if (sh != null)
                    {
                        score = new AnalysisScore();
                        score.Achievement = sh.Achievement / 10000.0;
                        score.Combo = sh.Combo;
                        score.Sync = sh.Sync;
                        score.PlayCount = (int)sh.PlayCount;
                        score.Rank = sh.Rank;
                    }
                }
                var hit = Match(spec, chart, score, rec, ability, confRange, weakPat, weakEval, doc.B35, doc.B15, fav);
                if (hit == null)
                {
                    continue;
                }
                var cand = new Candidate();
                cand.InternalId = chart.InternalId;
                cand.ChartId = chart.ChartId;
                cand.Title = chart.Title;
                cand.Difficulty = chart.Difficulty;
                cand.Level = chart.Level;
                cand.Family = chart.Family;
                cand.Version = chart.Version;
                cand.Labels = new List<string>();
                cand.Labels.Add(hit);
                cand.Fit = rec == null ? (double?)null : rec.Fit;
                cand.Ach = score == null ? (double?)null : score.Achievement;
                outList.Add(cand);
            }
            return outList;
        }

        /// <summary>各池计数（助手卡片上的数量）。</summary>
        internal static Dictionary<string, int> Counts(string user, AnalysisResult doc, IEnumerable<PoolSpec> specs)
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var spec in specs)
            {
                map[spec.Pool] = Candidates(user, doc, spec).Count;
            }
            return map;
        }

        // ---------------------------------------------------------------- 助手抽卡（assistant.py 等价）

        private static readonly Random _rng = new Random();

        /// <summary>从卡片配置构造 PoolSpec。</summary>
        internal static PoolSpec SpecOf(Dictionary<string, object> card)
        {
            var spec = new PoolSpec();
            if (card == null)
            {
                return spec;
            }
            spec.Pool = JsonMini.Str(card, "pool");
            var st = JsonMini.Obj(card, "settings");
            if (st == null)
            {
                return spec;
            }
            if (JsonMini.Get(st, "levelMin") != null && JsonMini.Str(st, "levelMin").Length > 0)
            {
                spec.LevelMin = JsonMini.Dbl(st, "levelMin");
            }
            if (JsonMini.Get(st, "levelMax") != null && JsonMini.Str(st, "levelMax").Length > 0)
            {
                spec.LevelMax = JsonMini.Dbl(st, "levelMax");
            }
            if (JsonMini.Get(st, "achMax") != null && JsonMini.Str(st, "achMax").Length > 0)
            {
                spec.AchMax = JsonMini.Dbl(st, "achMax");
            }
            if (JsonMini.Get(st, "comboMax") != null && JsonMini.Str(st, "comboMax").Length > 0)
            {
                spec.ComboMax = (int)JsonMini.Dbl(st, "comboMax");
            }
            if (JsonMini.Get(st, "syncMax") != null && JsonMini.Str(st, "syncMax").Length > 0)
            {
                spec.SyncMax = (int)JsonMini.Dbl(st, "syncMax");
            }
            spec.ExcludeUnplayed = JsonMini.Bool(st, "excludeUnplayed");
            spec.Version = JsonMini.Str(st, "version");
            spec.PlateId = JsonMini.Str(st, "plateId");
            var diffs = JsonMini.Arr(st, "difficulties");
            if (diffs != null)
            {
                foreach (var d in diffs)
                {
                    if (d != null)
                    {
                        spec.Difficulties.Add(Convert.ToString(d, System.Globalization.CultureInfo.InvariantCulture));
                    }
                }
            }
            return spec;
        }

        /// <summary>
        /// 取分析结果：优先内存里的已构建结果；没有才解析传入的 analysis.json 文本。
        /// （/api/songs 与助手都走这里，避免每次重算。）
        /// </summary>
        private static AnalysisResult DocOf(string user, string analysisJson)
        {
            var snap = Analysis.Snapshot(user);
            if (snap != null)
            {
                return snap;
            }
            if (string.IsNullOrEmpty(analysisJson))
            {
                return null;
            }
            var doc = JsonMini.ParseObj(analysisJson);
            if (doc == null)
            {
                return null;
            }
            var r = new AnalysisResult();
            r.ByKey = new Dictionary<string, AnalysisRec>(StringComparer.Ordinal);
            r.WeakPattern = new List<string>();
            r.WeakEval = new List<string>();
            var model = JsonMini.Obj(doc, "fitModel") ?? new Dictionary<string, object>();
            var ab = JsonMini.Arr(model, "abilityRange");
            if (ab != null && ab.Count >= 2)
            {
                r.Ability = new double[] { JsonMini.Dbl(model, "abilityRange") , 0 };
                r.Ability = new double[] { ToD(ab[0]), ToD(ab[1]) };
            }
            var range = JsonMini.Arr(model, "range");
            if (range != null && range.Count >= 2)
            {
                r.Lo = ToD(range[0]);
                r.Hi = ToD(range[1]);
            }
            var wpat = JsonMini.Arr(model, "weakPattern");
            if (wpat != null)
            {
                foreach (var w in wpat)
                {
                    if (w != null)
                    {
                        r.WeakPattern.Add(Convert.ToString(w, System.Globalization.CultureInfo.InvariantCulture));
                    }
                }
            }
            var weval = JsonMini.Arr(model, "weakEval");
            if (weval != null)
            {
                foreach (var w in weval)
                {
                    if (w != null)
                    {
                        r.WeakEval.Add(Convert.ToString(w, System.Globalization.CultureInfo.InvariantCulture));
                    }
                }
            }
            var b50 = JsonMini.Obj(model, "b50");
            if (b50 != null)
            {
                r.B35 = JsonMini.Int(b50, "b35");
                r.B15 = JsonMini.Int(b50, "b15");
            }
            var charts = JsonMini.Arr(doc, "charts");
            if (charts != null)
            {
                foreach (var item in charts)
                {
                    var row = item as Dictionary<string, object>;
                    if (row == null)
                    {
                        continue;
                    }
                    var rec = new AnalysisRec();
                    rec.Fit = JsonMini.Dbl(row, "effectiveLevel");
                    rec.EvalLevel = JsonMini.Dbl(row, "fitEvalLevel");
                    rec.Tier = JsonMini.Int(row, "fitTier");
                    rec.InConf = JsonMini.Bool(row, "fitInConf");
                    rec.LocalLevel = JsonMini.Dbl(row, "level");
                    rec.Pool = "old";
                    rec.Rating = JsonMini.Get(row, "effectiveLevel") != null ? 1 : -1;
                    var key = JsonMini.Int(row, "internalId") + ":" + JsonMini.Int(row, "chartid");
                    r.ByKey[key] = rec;
                }
            }
            return r;
        }

        private static double ToD(object v)
        {
            if (v is double)
            {
                return (double)v;
            }
            if (v is long)
            {
                return (long)v;
            }
            double d;
            return double.TryParse(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture),
                                   System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out d) ? d : 0.0;
        }

        /// <summary>按卡片取候选（助手用）。</summary>
        internal static List<Candidate> Candidates(string user, Dictionary<string, object> card, string analysisJson)
        {
            var spec = SpecOf(card);
            var doc = DocOf(user, analysisJson);
            if (doc == null)
            {
                return new List<Candidate>();
            }
            var list = Candidates(user, doc, spec);
            int cardIndex = JsonMini.Int(card, "index");
            foreach (var c in list)
            {
                c.CardIndex = cardIndex;
            }
            return list;
        }

        /// <summary>卡片当前候选数。</summary>
        internal static int Count(string user, Dictionary<string, object> card)
        {
            return Candidates(user, card, Analysis.Json(user, false).json).Count;
        }

        private static string CandKey(Dictionary<string, object> c)
        {
            return JsonMini.Int(c, "internalId") + ":" + JsonMini.Int(c, "chartid");
        }

        /// <summary>排序后 45%–55% 区间的中位数；样本太少直接用全部（assistant.py central_median）。</summary>
        private static double CentralMedian(List<double> values)
        {
            var vals = new List<double>();
            foreach (var v in values)
            {
                vals.Add(v);
            }
            vals.Sort();
            if (vals.Count == 0)
            {
                return 0.0;
            }
            if (vals.Count < 8)
            {
                int m = vals.Count / 2;
                return vals.Count % 2 == 1 ? vals[m] : (vals[m - 1] + vals[m]) / 2;
            }
            int lo = (int)(vals.Count * 0.45);
            int hi = Math.Max(lo + 1, (int)(vals.Count * 0.55));
            var slice = new List<double>();
            for (int i = lo; i < hi && i < vals.Count; i++)
            {
                slice.Add(vals[i]);
            }
            return Median(slice);
        }

        private static double Median(List<double> v)
        {
            if (v.Count == 0)
            {
                return 0.0;
            }
            var d = new List<double>(v);
            d.Sort();
            int n = d.Count;
            return n % 2 == 1 ? d[n / 2] : (d[n / 2 - 1] + d[n / 2]) / 2.0;
        }

        /// <summary>高优先项 ×3，两个都满足按 6（assistant.py _priority_weights）。</summary>
        private static List<double> PriorityWeights(List<Candidate> pool, Dictionary<string, object> card)
        {
            var settings = JsonMini.Obj(card, "settings") ?? new Dictionary<string, object>();
            var poolName = JsonMini.Str(card, "pool");
            var outW = new List<double>(pool.Count);
            if (poolName == "underdog")
            {
                var mode = JsonMini.Str(settings, "fitPriority");
                if (mode.Length == 0)
                {
                    mode = "none";
                }
                if (mode != "high" && mode != "low")
                {
                    for (int i = 0; i < pool.Count; i++)
                    {
                        outW.Add(1.0);
                    }
                    return outW;
                }
                double mid;
                var lv = JsonMini.Dbl(settings, "levelMin");
                var hv = JsonMini.Dbl(settings, "levelMax");
                bool hasLo = JsonMini.Get(settings, "levelMin") != null && JsonMini.Str(settings, "levelMin").Length > 0;
                bool hasHi = JsonMini.Get(settings, "levelMax") != null && JsonMini.Str(settings, "levelMax").Length > 0;
                if (hasLo && hasHi)
                {
                    mid = (lv + hv) / 2.0;
                }
                else
                {
                    var levels = new List<double>();
                    foreach (var c in pool)
                    {
                        levels.Add(c.Level);
                    }
                    mid = CentralMedian(levels);
                }
                foreach (var c in pool)
                {
                    bool hit = mode == "low" ? c.Level <= mid : c.Level > mid;
                    outW.Add(hit ? 3.0 : 1.0);
                }
                return outW;
            }
            var fitMode = JsonMini.Str(settings, "fitPriority");
            var achMode = JsonMini.Str(settings, "achPriority");
            if (fitMode.Length == 0)
            {
                fitMode = "none";
            }
            if (achMode.Length == 0)
            {
                achMode = "none";
            }
            double fitMid = 0, achMid = 0;
            if (fitMode != "none")
            {
                var fits = new List<double>();
                foreach (var c in pool)
                {
                    if (c.Fit != null)
                    {
                        fits.Add(c.Fit.Value);
                    }
                }
                fitMid = CentralMedian(fits);
            }
            if (achMode != "none")
            {
                var achs = new List<double>();
                foreach (var c in pool)
                {
                    if (c.Ach != null)
                    {
                        achs.Add(c.Ach.Value);
                    }
                }
                achMid = CentralMedian(achs);
            }
            foreach (var c in pool)
            {
                bool fitHit = false, achHit = false;
                if (fitMode == "high" && c.Fit != null)
                {
                    fitHit = c.Fit.Value > fitMid;
                }
                else if (fitMode == "low" && c.Fit != null)
                {
                    fitHit = c.Fit.Value < fitMid;
                }
                if (achMode == "high" && c.Ach != null)
                {
                    achHit = c.Ach.Value > achMid;
                }
                else if (achMode == "low" && c.Ach != null)
                {
                    achHit = c.Ach.Value < achMid;
                }
                int w = 1;
                if (fitHit)
                {
                    w = 3;
                }
                if (achHit)
                {
                    w = w == 3 ? 6 : 3;
                }
                outW.Add(w);
            }
            return outW;
        }

        /// <summary>
        /// 从卡片候选里抽一首：最近 6 首优先排除，池内全被排除时允许重复（assistant.py draw_candidate）。
        /// 返回可直接塞进预览队列的词典；池空回 null。
        /// </summary>
        internal static Dictionary<string, object> Draw(string user, Dictionary<string, object> card,
                                                        List<object> recent, string analysisJson)
        {
            var cands = Candidates(user, card, analysisJson);
            if (cands.Count == 0)
            {
                return null;
            }
            var recentKeys = new HashSet<string>(StringComparer.Ordinal);
            if (recent != null)
            {
                foreach (var r in recent)
                {
                    var row = r as Dictionary<string, object>;
                    if (row != null)
                    {
                        recentKeys.Add(CandKey(row));
                    }
                }
            }
            var pool = new List<Candidate>();
            foreach (var c in cands)
            {
                if (!recentKeys.Contains(c.InternalId + ":" + c.ChartId))
                {
                    pool.Add(c);
                }
            }
            if (pool.Count == 0)
            {
                pool = cands;
            }
            var weights = PriorityWeights(pool, card);
            Candidate pick;
            double sum = 0;
            foreach (var w in weights)
            {
                sum += w;
            }
            if (sum <= 0)
            {
                pick = pool[_rng.Next(pool.Count)];
            }
            else
            {
                double roll = _rng.NextDouble() * sum;
                int idx = 0;
                for (; idx < pool.Count; idx++)
                {
                    roll -= weights[idx];
                    if (roll <= 0)
                    {
                        break;
                    }
                }
                if (idx >= pool.Count)
                {
                    idx = pool.Count - 1;
                }
                pick = pool[idx];
            }
            var outRow = new Dictionary<string, object>(StringComparer.Ordinal);
            outRow["internalId"] = pick.InternalId;
            outRow["chartid"] = pick.ChartId;
            outRow["title"] = pick.Title;
            outRow["difficulty"] = pick.Difficulty;
            outRow["level"] = pick.Level.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture);
            outRow["dataid"] = pick.InternalId % 10000;
            outRow["pool"] = JsonMini.Str(card, "pool");
            outRow["cardIndex"] = pick.CardIndex;
            outRow["labels"] = pick.Labels;
            if (pick.Fit != null)
            {
                outRow["fit"] = pick.Fit.Value;
            }
            if (pick.Ach != null)
            {
                outRow["ach"] = pick.Ach.Value;
            }
            return outRow;
        }

        private static int ParseIndex(string user)
        {
            int n;
            return int.TryParse(user, out n) ? n : 0;
        }
    }
}
