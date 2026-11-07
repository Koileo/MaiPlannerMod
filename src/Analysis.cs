// 用途：打歌助手的画像引擎 —— 把「你打得怎么样」变成「这张谱对你是什么」。
//       本文件是 tools/analyze_profile/analyze_profile.py 的等价物，产出与原 analysis.json 同构的 JSON。
// 状态：现行。公式逐条对齐「个人拟合与曲池说明.md」2.1–2.5；曲线/回归/门控与源脚本一行不差。
//
// 【数据来源差异】mod 里没有 data/fusion/*.json：
//   曲库走 GameSnapshot.Songs，成绩走 PlayerStore，黑名单走 UserFiles。
//   社区标签（dxrating.net）通过内嵌快照读取，水鱼拟合差值保留可赋值委托：
//     TagProvider       —— 返回某张谱的三个组的标签（Mod 初始化时接入 CommunityTags）
//     FitDiffProvider   —— 返回水鱼 fitDiff / avg / stdDev / cnt / diff（默认空 → 全走纯官标）
//   未提供水鱼数据时，评价定数从官标及现有标签先验计算。
//
// 【跑在哪个线程】只读快照，可在后台线程跑；同一用户同一时刻只允许一份计算（按用户加锁）。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace MaiPlanner
{
    internal sealed class AnalysisTag
    {
        internal string Name;
        internal string Group;
    }

    internal sealed class AnalysisChart
    {
        internal int InternalId;
        internal int ChartId;
        internal string Difficulty;
        internal string Tier;
        internal double Level;
        internal double LocalLevel;
        internal string Version;
        internal string Family;
        internal string Title;
        internal readonly List<AnalysisTag> Tags = new List<AnalysisTag>();
        internal string CKey { get { return InternalId.ToString(CultureInfo.InvariantCulture) + ":" + ChartId.ToString(CultureInfo.InvariantCulture); } }
    }

    internal sealed class AnalysisScore
    {
        internal double Achievement;
        internal int Combo;
        internal int Sync;
        internal int PlayCount;
        internal int Rank;
    }

    /// <summary>单张谱的拟合记录（对外给前端取 fit/tagCorr 等）。</summary>
    internal sealed class AnalysisRec
    {
        internal double Fit;
        internal string Mode;
        internal bool InRange;
        internal bool InConf;
        internal int Tier;
        internal double EvalLevel;
        internal double LocalLevel;
        internal int Rating;             // 无成绩时 -1
        internal double TagCorr;
        internal double TagRaw;
        internal int TagK;
        internal double TagFactor;
        internal double TagBalance;
        internal double TagScope;
        internal double TagSample;
        internal string WaterKind;
        internal double? WaterPrior;
        internal string Pool;
        internal double Cutoff;
        internal bool InB50;
    }

    internal sealed class AnalysisRow
    {
        internal int InternalId;
        internal int ChartId;
        internal string Difficulty;
        internal string Tier;
        internal string Family;
        internal double Level;
        internal double? EffectiveLevel;
        internal double? FitEvalLevel;
        internal bool? FitInConf;
        internal int? FitTier;
        internal double? TagCorr;
        internal double? TagRaw;
        internal int? TagK;
        internal double? TagFactor;
        internal double? TagBalance;
        internal double? TagScope;
        internal double? TagSample;
        internal string WaterKind;
        internal double? WaterPrior;
        internal string FitMode;
        internal bool DeltaUnreliable;
        internal double? Delta;
        internal List<string> Tags = new List<string>();
        internal double? Achievement;
        internal int? Combo;
        internal int? Sync;
        internal int PlayCount;
        internal double Expected;
        internal double? Residual;
        internal List<string> Labels = new List<string>();
        internal double Predicted;
        internal bool Blacklisted;
    }

    internal sealed class AnalysisResult
    {
        internal string User;
        internal string Fingerprint;
        internal string Json;
        internal Dictionary<string, AnalysisRec> ByKey;
        internal List<AnalysisRow> Rows;
        internal double Lo;
        internal double Hi;
        internal double[] Ability;
        internal int[] Tiers;
        internal int TopN;
        internal int TopNRequested;
        internal double? TopNAnchor;
        internal string TopNMode;
        internal int TagSampleN;
        internal double TagSampleFactor;
        internal double FitMedian;
        internal List<string> WeakTags;
        internal List<string> WeakPattern;
        internal List<string> WeakEval;
        internal string ProfileStatus;
        internal string TagBalanceMode;
        internal string TagCountMode;
        internal double TagSpread;
        internal double TagBalanceFactor;
        internal Dictionary<string, object> BandDelta;
        internal Dictionary<string, double> TagU;
        internal Dictionary<string, double> TagUPattern;
        internal Dictionary<string, double> TagUEval;
        internal Dictionary<string, double> TopTags;
        internal int Samples;
        internal int ChartCount;
        internal int Played;
        internal int BlacklistedCount;
        internal Dictionary<string, int> Counts;
        internal int B35;
        internal int B15;
        internal string CurrentVersion;
        internal List<string> NewVersions;
        internal int OldCount;
        internal int NewCount;
        internal Dictionary<string, List<CurvePoint>> Curves;
    }

    internal struct CurvePoint
    {
        internal double Level;
        internal double Expected;
        internal int N;
    }

    /// <summary>分析结果的轻量信封：既可直接当 string 用（json），也可取 .json / .doc。</summary>
    internal sealed class AnalysisDoc
    {
        internal string json;
        internal string error;
        internal AnalysisResult doc;

        public static implicit operator string(AnalysisDoc d)
        {
            return d == null ? null : d.json;
        }
    }

    internal static class Analysis
    {
        internal const string PatternGroup = "PatternsTag";
        internal const string EvalGroup = "EvaluationTag";
        internal const string DifficultyGroup = "DifficultyTag";

        private static readonly string[] Familes = { "std", "dx", "utage", "utage2p" };
        private static readonly string[] TgGroups = { PatternGroup, DifficultyGroup, EvalGroup };
        private static readonly string[] WaterTags = { "水", "诈称谱" };
        internal const double DeltaMin = -0.2;      // 水鱼 Δ 下限：说「更简单」超过 0.2 的只认 0.2
        private const double FitCap = 0.3;          // 单谱总修正上限 ±0.3

        // 默认参数（与源脚本 PARAMS + md 一致；tag_ridge_lambda 只在本文件内部用）
        internal sealed class Params
        {
            internal double FitScoreBase = 0.2;
            internal double FitScoreLow = 0.06;
            internal double FitCap = 0.3;
            internal double LevelBin = 0.5;
            internal int BinMinSamples = 3;
            internal double MasteredAch = 100.6;
            internal string TagCountMode = "linear";
            internal double TagCountAlpha = 0.15;
            internal string TagBalanceMode = "spread";
            internal double TagBalanceRef = 0.25;
            internal double TagBalancePower = 2.0;
            internal double TagBalanceFloor = 0.15;
            internal double TagRidgeLambda = 200.0;
        }

        private static readonly Params DefaultParams = new Params();

        // ── 外部数据钩子 ──
        internal delegate List<AnalysisTag> TagProviderDelegate(int internalId, int chartId);
        internal delegate bool FitDiffDelegate(int internalId, int chartId,
                                               out double fitDiff, out double avg, out double stdDev,
                                               out int cnt, out string diff);

        /// <summary>标签来源（默认 null = 没有标签）</summary>
        internal static TagProviderDelegate TagProvider;
        /// <summary>水鱼拟合来源（默认 null = 没有水鱼数据）</summary>
        internal static FitDiffDelegate FitDiffProvider;
        /// <summary>标签数据版本号；变了要重算</summary>
        internal static volatile int TagRevision;

        private static readonly object _gate = new object();
        private static readonly Dictionary<string, object> _userLocks = new Dictionary<string, object>(StringComparer.Ordinal);
        private static readonly Dictionary<string, AnalysisResult> _cache = new Dictionary<string, AnalysisResult>(StringComparer.Ordinal);

        // ---------------------------------------------------------------- 对外入口

        /// <summary>已构建好的结果（不触发重算）；没有就 null。供 /api/songs 拼字段。</summary>
        internal static AnalysisResult Snapshot(string user)
        {
            lock (_gate)
            {
                AnalysisResult r;
                return _cache.TryGetValue(user ?? "0", out r) ? r : null;
            }
        }

        /// <summary>构建 + 写 analysis.json；返回信封（json 为空表示失败，error 有原因）。</summary>
        internal static AnalysisDoc Json(string user, bool run)
        {
            var u = string.IsNullOrEmpty(user) ? "0" : user;
            var box = new AnalysisDoc();
            if (!run)
            {
                lock (_gate)
                {
                    AnalysisResult hit;
                    if (_cache.TryGetValue(u, out hit) && hit.Json != null)
                    {
                        box.json = hit.Json;
                        box.doc = hit;
                        return box;
                    }
                }
            }
            try
            {
                var result = Build(u, run);
                if (result == null)
                {
                    box.error = "还没有分析结果";
                    return box;
                }
                box.json = result.Json;
                box.doc = result;
                return box;
            }
            catch (Exception e)
            {
                ModLog.WarnOnce("分析失败: " + e.Message);
                box.error = "分析失败：" + e.Message;
                return box;
            }
        }

        /// <summary>跑一次完整分析并落盘。run=false 时命中指纹就直接复用。</summary>
        internal static AnalysisResult Build(string user, bool run)
        {
            var u = string.IsNullOrEmpty(user) ? "0" : user;
            object lk;
            lock (_gate)
            {
                if (!_userLocks.TryGetValue(u, out lk))
                {
                    lk = new object();
                    _userLocks[u] = lk;
                }
            }
            lock (lk)
            {
                var player = PlayerStore.Get(ParseIndex(u));
                if (player == null)
                {
                    ModLog.WarnOnce("分析：没有这个玩家 " + u);
                    return null;
                }
                var intent = new BuildIntent(u, player, run);
                return intent.Run();
            }
        }

        private static int ParseIndex(string user)
        {
            int n;
            return int.TryParse(user, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : 0;
        }

        private static string NowStamp()
        {
            return DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) + "+0800";
        }

        // ---------------------------------------------------------------- 计算主体

        private sealed class BuildIntent
        {
            private readonly string _user;
            private readonly PlayerStore.Player _player;
            private readonly bool _run;
            private readonly Params _params = DefaultParams;

            internal BuildIntent(string user, PlayerStore.Player player, bool run)
            {
                _user = user;
                _player = player;
                _run = run;
            }

            internal AnalysisResult Run()
            {
                var charts = LoadCharts();
                var scores = LoadScores(_player);
                var blacklist = Blacklist.ActiveKeys(_user);
                var fingerprint = Fingerprint(charts, scores, blacklist);

                lock (_gate)
                {
                    AnalysisResult old;
                    if (_cache.TryGetValue(_user, out old) && old.Fingerprint == fingerprint
                        && old.Json != null && !_run)
                    {
                        return old;
                    }
                }

                var played = 0;
                foreach (var c in charts)
                {
                    var s = FindScore(scores, c);
                    if (s != null && s.Achievement > 0)
                    {
                        played++;
                    }
                }

                var curves = BuildCurves(charts, scores, _params);
                var fitMap = LoadFitDiff();
                var unreliable = UnreliableFits(charts);
                var v3 = FitV3(charts, scores, fitMap, unreliable, _params, curves);

                var result = new AnalysisResult();
                result.User = _user;
                result.Fingerprint = fingerprint;
                result.ChartCount = charts.Count;
                result.Played = played;
                result.Curves = curves;
                result.Rows = new List<AnalysisRow>();

                var counts = new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    { "mastered", 0 }, { "pushable_b35", 0 }, { "pushable_b15", 0 }, { "ap", 0 },
                    { "practice", 0 }, { "accuracy", 0 }, { "challenge", 0 }, { "unplayed", 0 },
                    { "blacklisted", 0 },
                };

                var weakTags = new HashSet<string>(v3.WeakTags, StringComparer.Ordinal);
                var weakPat = new HashSet<string>(v3.WeakPattern, StringComparer.Ordinal);
                var weakEval = new HashSet<string>(v3.WeakEval, StringComparer.Ordinal);
                var ability = v3.Ability;
                var confRange = new double[] { v3.Lo, v3.Hi };

                foreach (var chart in charts)
                {
                    if (chart.Family == "utage" || chart.Family == "utage2p")
                    {
                        continue;                     // 宴谱不参与分类
                    }
                    var score = FindScore(scores, chart);
                    AnalysisRec rec;
                    v3.ByKey.TryGetValue(chart.CKey, out rec);
                    double effective;
                    string fitMode;
                    if (rec == null)
                    {
                        effective = chart.Level;
                        fitMode = "none";
                    }
                    else
                    {
                        effective = rec.Fit;
                        fitMode = rec.Mode + (rec.InRange ? "" : "-out");
                    }
                    var predicted = ExpectedAt(curves, chart.Level);
                    var labels = Classify(chart, score, effective, ability,
                                          rec == null ? (double?)null : rec.EvalLevel,
                                          rec == null ? (double?)null : rec.LocalLevel,
                                          rec == null ? (double?)null : rec.Cutoff,
                                          rec == null ? null : rec.Pool,
                                          rec != null && rec.InConf,
                                          rec != null && rec.InB50,
                                          confRange,
                                          weakTags, weakPat, weakEval);

                    bool blocked = blacklist.Contains(chart.CKey);
                    if (blocked)
                    {
                        counts["blacklisted"] = counts["blacklisted"] + 1;
                        labels.Clear();
                    }
                    foreach (var label in labels)
                    {
                        counts[label] = counts.ContainsKey(label) ? counts[label] + 1 : 1;
                    }

                var row = new AnalysisRow();
                    row.InternalId = chart.InternalId;
                    row.ChartId = chart.ChartId;
                    row.Difficulty = chart.Difficulty;
                    row.Tier = chart.Tier;
                    row.Family = chart.Family;
                    row.Level = chart.Level;
                    row.EffectiveLevel = rec == null ? (double?)null : rec.Fit;
                    row.FitEvalLevel = rec == null ? (double?)null : rec.EvalLevel;
                    row.FitInConf = rec == null ? (bool?)null : rec.InConf;
                    row.FitTier = rec == null ? (int?)null : rec.Tier;
                    row.TagCorr = rec == null ? (double?)null : rec.TagCorr;
                    row.TagRaw = rec == null ? (double?)null : rec.TagRaw;
                    row.TagK = rec == null ? (int?)null : rec.TagK;
                    row.TagFactor = rec == null ? (double?)null : rec.TagFactor;
                    row.TagBalance = rec == null ? (double?)null : rec.TagBalance;
                    row.TagScope = rec == null ? (double?)null : rec.TagScope;
                    row.TagSample = rec == null ? (double?)null : rec.TagSample;
                    row.WaterKind = rec == null ? null : rec.WaterKind;
                    row.WaterPrior = rec == null ? (double?)null : rec.WaterPrior;
                    row.FitMode = fitMode;
                    row.DeltaUnreliable = rec != null && rec.WaterKind == null
                        && fitMap.ContainsKey(chart.CKey) && unreliable.Contains(chart.CKey);
                    row.Delta = null;
                    foreach (var t in chart.Tags)
                    {
                        row.Tags.Add(t.Name);
                    }
                    row.Achievement = score == null ? (double?)null : score.Achievement;
                    row.Combo = score == null ? (int?)null : score.Combo;
                    row.Sync = score == null ? (int?)null : score.Sync;
                    row.PlayCount = score == null ? 0 : score.PlayCount;
                    row.Expected = ExpectedAt(curves, chart.Tier, chart.Level);
                    row.Residual = (score != null && score.Achievement > 0)
                        ? (double?)(score.Achievement - ExpectedAt(curves, chart.Tier, chart.Level))
                        : (double?)null;
                    row.Labels = labels;
                    row.Predicted = predicted;
                    row.Blacklisted = blocked;
                    result.Rows.Add(row);
                }

                result.ByKey = v3.ByKey;
                result.Lo = v3.Lo;
                result.Hi = v3.Hi;
                result.Ability = v3.Ability;
                result.Tiers = v3.Tiers;
                result.TopN = v3.TopN;
                result.TopNRequested = v3.TopNRequested;
                result.TopNAnchor = v3.TopNAnchor;
                result.TopNMode = v3.TopNMode;
                result.TagSampleN = v3.TagSampleN;
                result.TagSampleFactor = v3.TagSampleFactor;
                result.FitMedian = v3.FitMedian;
                result.WeakTags = v3.WeakTags;
                result.WeakPattern = v3.WeakPattern;
                result.WeakEval = v3.WeakEval;
                result.ProfileStatus = v3.ProfileStatus;
                result.TagBalanceMode = v3.TagBalanceMode;
                result.TagCountMode = v3.TagCountMode;
                result.TagSpread = v3.TagSpread;
                result.TagBalanceFactor = v3.TagBalanceFactor;
                result.BandDelta = v3.BandDelta;
                result.TagU = v3.TagU;
                result.TagUPattern = v3.TagUPattern;
                result.TagUEval = v3.TagUEval;
                result.TopTags = v3.TopTags;
                result.Samples = v3.Samples;
                result.B35 = v3.B35;
                result.B15 = v3.B15;
                result.CurrentVersion = v3.CurrentVersion;
                result.NewVersions = v3.NewVersions;
                result.OldCount = v3.OldCount;
                result.NewCount = v3.NewCount;
                result.Counts = counts;
                result.BlacklistedCount = counts["blacklisted"];

                result.Json = ToJson(result);
                lock (_gate)
                {
                    _cache[_user] = result;
                }
                UserFiles.WriteText(_user, "analysis.json", result.Json);
                return result;
            }
        }

        // ---------------------------------------------------------------- 数据装配

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

        private static string DiffName(int d)
        {
            switch (d)
            {
                case 0: return "basic";
                case 1: return "advanced";
                case 2: return "expert";
                case 3: return "master";
                case 4: return "remaster";
                default: return "?";
            }
        }

        /// <summary>曲库 → 每张启用的谱一条扁平记录（宴谱也收，后续再排除）。</summary>
        internal static List<AnalysisChart> LoadCharts()
        {
            var list = new List<AnalysisChart>();
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
                    double level = s.LevelValue[d] / 10.0;
                    if (level <= 0)
                    {
                        continue;
                    }
                    var c = new AnalysisChart();
                    c.InternalId = s.InternalId;
                    c.ChartId = d;
                    c.Difficulty = DiffName(d);
                    c.Tier = (family == "utage" || family == "utage2p") ? "utage" : c.Difficulty;
                    c.Level = level;
                    c.LocalLevel = level;
                    c.Version = Versions.Fix(s.Version);
                    c.Family = family;
                    c.Title = s.Title;
                    var provider = TagProvider;
                    if (provider != null)
                    {
                        try
                        {
                            var tags = provider(s.InternalId, d);
                            if (tags != null)
                            {
                                foreach (var t in tags)
                                {
                                    if (t != null && !string.IsNullOrEmpty(t.Name))
                                    {
                                        c.Tags.Add(t);
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                    list.Add(c);
                }
            }
            return list;
        }

        private static Dictionary<string, AnalysisScore> LoadScores(PlayerStore.Player player)
        {
            var map = new Dictionary<string, AnalysisScore>(StringComparer.Ordinal);
            foreach (var kv in player.Scores)
            {
                foreach (var kv2 in kv.Value)
                {
                    var sh = kv2.Value;
                    if (sh == null)
                    {
                        continue;
                    }
                    var rec = new AnalysisScore();
                    rec.Achievement = sh.Achievement / 10000.0;
                    rec.Combo = sh.Combo;
                    rec.Sync = sh.Sync;
                    rec.PlayCount = (int)sh.PlayCount;
                    rec.Rank = sh.Rank;
                    map[UserFiles.Key(kv.Key, kv2.Key)] = rec;
                }
            }
            return map;
        }

        private static AnalysisScore FindScore(Dictionary<string, AnalysisScore> scores, AnalysisChart chart)
        {
            AnalysisScore s;
            return scores.TryGetValue(chart.CKey, out s) ? s : null;
        }

        private static Dictionary<string, double> LoadFitDiff()
        {
            var map = new Dictionary<string, double>(StringComparer.Ordinal);
            var provider = FitDiffProvider;
            if (provider == null)
            {
                return map;
            }
            foreach (var s in GameSnapshot.Songs)
            {
                if (s == null || s.Disable || s.InternalId <= 0)
                {
                    continue;
                }
                for (int d = 0; d < 5; d++)
                {
                    if (!s.Enabled[d])
                    {
                        continue;
                    }
                    double fit = 0, avg = 0, sd = 0;
                    int cnt = 0;
                    string diff = "";
                    bool ok;
                    try
                    {
                        ok = provider(s.InternalId, d, out fit, out avg, out sd, out cnt, out diff);
                    }
                    catch
                    {
                        ok = false;
                    }
                    if (ok)
                    {
                        map[UserFiles.Key(s.InternalId, d)] = fit;
                    }
                }
            }
            return map;
        }

        private sealed class FitMeta
        {
            internal double Avg;
            internal string Diff;
        }

        private static Dictionary<string, FitMeta> LoadFitMeta(List<AnalysisChart> charts)
        {
            var map = new Dictionary<string, FitMeta>(StringComparer.Ordinal);
            var provider = FitDiffProvider;
            if (provider == null)
            {
                return map;
            }
            foreach (var c in charts)
            {
                double fit = 0, avg = 0, sd = 0;
                int cnt = 0;
                string diff = "";
                bool ok;
                try
                {
                    ok = provider(c.InternalId, c.ChartId, out fit, out avg, out sd, out cnt, out diff);
                }
                catch
                {
                    ok = false;
                }
                if (!ok)
                {
                    continue;
                }
                var m = new FitMeta();
                m.Avg = avg;
                m.Diff = diff;
                map[c.CKey] = m;
            }
            return map;
        }

        /// <summary>同一档定数里 avg 明显离群的谱：其 Δ 不可信。</summary>
        private static HashSet<string> UnreliableFits(List<AnalysisChart> charts)
        {
            var bad = new HashSet<string>(StringComparer.Ordinal);
            var meta = LoadFitMeta(charts);
            if (meta.Count == 0)
            {
                return bad;
            }
            var byLevel = new Dictionary<int, List<KeyValuePair<string, double>>>();
            foreach (var kv in meta)
            {
                if (string.IsNullOrEmpty(kv.Value.Diff))
                {
                    continue;
                }
                var raw = kv.Value.Diff.Trim().TrimEnd('+', '?');
                double level;
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out level))
                {
                    continue;
                }
                int key = (int)level;
                List<KeyValuePair<string, double>> rows;
                if (!byLevel.TryGetValue(key, out rows))
                {
                    rows = new List<KeyValuePair<string, double>>();
                    byLevel[key] = rows;
                }
                rows.Add(new KeyValuePair<string, double>(kv.Key, kv.Value.Avg));
            }
            foreach (var rows in byLevel.Values)
            {
                if (rows.Count < 3)
                {
                    continue;
                }
                var avgs = new List<double>();
                foreach (var r in rows)
                {
                    avgs.Add(r.Value);
                }
                var med = Median(avgs);
                var sd = PStdDev(avgs);
                if (sd <= 0)
                {
                    continue;
                }
                foreach (var r in rows)
                {
                    if (Math.Abs(r.Value - med) > 1.5 * sd && Math.Abs(r.Value - med) >= 3.0)
                    {
                        bad.Add(r.Key);
                    }
                }
            }
            return bad;
        }

        private static string Fingerprint(List<AnalysisChart> charts, Dictionary<string, AnalysisScore> scores,
                                          HashSet<string> blacklist)
        {
            var sb = new StringBuilder(64);
            sb.Append(GameSnapshot.BuiltAt).Append('|').Append(charts.Count).Append('|')
              .Append(scores.Count).Append('|').Append(blacklist.Count).Append('|')
              .Append(TagRevision).Append('|');
            long sum = 0;
            foreach (var kv in scores)
            {
                sum = (sum * 31 + (long)(kv.Value.Achievement * 10000) + kv.Value.PlayCount) % 1000000007L;
            }
            sb.Append(sum);
            return sb.ToString();
        }

        // ---------------------------------------------------------------- 能力曲线

        private static Dictionary<string, List<CurvePoint>> BuildCurves(
            List<AnalysisChart> charts, Dictionary<string, AnalysisScore> scores, Params p)
        {
            var curves = new Dictionary<string, List<CurvePoint>>(StringComparer.Ordinal);
            var samples = new Dictionary<double, List<double>>();
            double binW = p.LevelBin;
            foreach (var c in charts)
            {
                var score = FindScore(scores, c);
                if (score == null || score.Achievement <= 0)
                {
                    continue;
                }
                double start = Math.Round(c.Level / binW) * binW;
                start = Math.Round(start, 3);
                List<double> values;
                if (!samples.TryGetValue(start, out values))
                {
                    values = new List<double>();
                    samples[start] = values;
                }
                values.Add(score.Achievement);
            }
            var pts = new List<CurvePoint>();
            foreach (var start in samples.Keys.OrderBy(delegate (double d) { return d; }))
            {
                var values = samples[start];
                if (values.Count >= p.BinMinSamples)
                {
                    var pt = new CurvePoint();
                    pt.Level = start + binW / 2;
                    pt.Expected = Math.Round(Median(values), 4);
                    pt.N = values.Count;
                    pts.Add(pt);
                }
            }
            if (pts.Count == 0)
            {
                var allv = new List<double>();
                foreach (var values in samples.Values)
                {
                    allv.AddRange(values);
                }
                if (allv.Count > 0)
                {
                    var pt = new CurvePoint();
                    pt.Level = 0;                     // 曲线只有 "*" 一条，源脚本回退到 [0] 的中位数
                    pt.Expected = Math.Round(Median(allv), 4);
                    pt.N = allv.Count;
                    pts.Add(pt);
                }
            }
            // 强制单调：定数越高期望越低
            double? prev = null;
            for (int i = 0; i < pts.Count; i++)
            {
                var pt = pts[i];
                if (prev != null && pt.Expected > prev.Value)
                {
                    pt.Expected = prev.Value;
                }
                prev = pt.Expected;
                pts[i] = pt;
            }
            curves["*"] = pts;
            return curves;
        }

        private static List<CurvePoint> CurveOf(Dictionary<string, List<CurvePoint>> curves, string diff)
        {
            List<CurvePoint> pts;
            if (curves.TryGetValue("*", out pts))
            {
                return pts;
            }
            if (diff != null && curves.TryGetValue(diff, out pts))
            {
                return pts;
            }
            return new List<CurvePoint>();
        }

        private static double ExpectedAt(Dictionary<string, List<CurvePoint>> curves, string diff, double level)
        {
            var pts = CurveOf(curves, diff);
            if (pts.Count == 0)
            {
                return 0;
            }
            if (level <= pts[0].Level)
            {
                return pts[0].Expected;
            }
            if (level >= pts[pts.Count - 1].Level)
            {
                return pts[pts.Count - 1].Expected;
            }
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                var a = pts[i];
                var b = pts[i + 1];
                if (a.Level <= level && level <= b.Level)
                {
                    if (b.Level == a.Level)
                    {
                        return b.Expected;
                    }
                    double t = (level - a.Level) / (b.Level - a.Level);
                    return a.Expected + (b.Expected - a.Expected) * t;
                }
            }
            return pts[pts.Count - 1].Expected;
        }

        private static double ExpectedAt(Dictionary<string, List<CurvePoint>> curves, double level)
        {
            return ExpectedAt(curves, "*", level);
        }

        /// <summary>曲线在 level 附近的局部斜率（正数，达成率每 1 定数下降多少）。</summary>
        private static double SlopeNear(Dictionary<string, List<CurvePoint>> curves, double level)
        {
            var pts = CurveOf(curves, null);
            if (pts.Count < 2)
            {
                return 1.0;
            }
            var lo = pts[0];
            var hi = pts[pts.Count - 1];
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                if (pts[i].Level <= level && level <= pts[i + 1].Level)
                {
                    lo = pts[i];
                    hi = pts[i + 1];
                    break;
                }
            }
            double drop = hi.Expected - lo.Expected;
            double span = hi.Level - lo.Level;
            if (span <= 0 || drop >= 0)
            {
                return 1.0;
            }
            return Math.Max(0.4, Math.Min(12.0, Math.Abs(drop) / span));
        }

        // ---------------------------------------------------------------- 标签效应

        private static bool IsWater(string name)
        {
            return name == "水";
        }

        private static bool IsFake(string name)
        {
            return name == "诈称谱";
        }

        private static bool IsWaterTag(string name)
        {
            return IsWater(name) || IsFake(name);
        }

        private sealed class Item
        {
            internal double Level;
            internal double LocalLevel;
            internal string Version;
            internal AnalysisScore Score;
            internal HashSet<string> Names = new HashSet<string>(StringComparer.Ordinal);
            internal double? Delta;
            internal double EvalLevel;
            internal string FitBase;
            internal double? WaterPrior;
            internal string WaterKind;
            internal double ObsShift;
        }

        private sealed class V3Result
        {
            internal Dictionary<string, AnalysisRec> ByKey = new Dictionary<string, AnalysisRec>(StringComparer.Ordinal);
            internal double Lo, Hi;
            internal double[] Ability = { 0.0, 99.0 };
            internal int[] Tiers = new int[4];
            internal int TopN;
            internal int TopNRequested;
            internal double? TopNAnchor;
            internal string TopNMode = "dynamic-b35";
            internal int TagSampleN;
            internal double TagSampleFactor = 1.0;
            internal double FitMedian;
            internal List<string> WeakTags = new List<string>();
            internal List<string> WeakPattern = new List<string>();
            internal List<string> WeakEval = new List<string>();
            internal string ProfileStatus = "distinct";
            internal string TagBalanceMode = "none";
            internal string TagCountMode = "none";
            internal double TagSpread;
            internal double TagBalanceFactor = 1.0;
            internal Dictionary<string, object> BandDelta = new Dictionary<string, object>();
            internal Dictionary<string, double> TagU = new Dictionary<string, double>(StringComparer.Ordinal);
            internal Dictionary<string, double> TagUPattern = new Dictionary<string, double>(StringComparer.Ordinal);
            internal Dictionary<string, double> TagUEval = new Dictionary<string, double>(StringComparer.Ordinal);
            internal Dictionary<string, double> TopTags = new Dictionary<string, double>(StringComparer.Ordinal);
            internal int Samples;
            internal int B35, B15;
            internal string CurrentVersion = "";
            internal List<string> NewVersions = new List<string>();
            internal int OldCount, NewCount;
        }

        /// <summary>成绩权重：以 100.5 为 0 点，低分正、高分负（线性插值；md 2.4 的表）。</summary>
        internal static double ScoreWeight(double ach)
        {
            double[,] pts =
            {
                { 0.0, 1.5 }, { 98.0, 1.5 }, { 99.0, 1.0 }, { 100.0, 0.5 },
                { 100.4, 0.0 }, { 100.6, 0.0 }, { 101.0, -1.0 }, { 102.0, -1.0 },
            };
            if (ach <= pts[0, 0])
            {
                return pts[0, 1];
            }
            for (int i = 0; i + 1 < pts.GetLength(0); i++)
            {
                double x0 = pts[i, 0], y0 = pts[i, 1], x1 = pts[i + 1, 0], y1 = pts[i + 1, 1];
                if (x0 <= ach && ach <= x1)
                {
                    if (x1 == x0)
                    {
                        return y1;
                    }
                    return y0 + (ach - x0) * (y1 - y0) / (x1 - x0);
                }
            }
            return pts[pts.GetLength(0) - 1, 1];
        }

        /// <summary>按 b35 末位官方定数取能力样本数（md 2.2 的表）。</summary>
        internal static int DynamicTopN(double? officialLevel)
        {
            if (officialLevel == null)
            {
                return 50;
            }
            double x = officialLevel.Value;
            if (x <= 12.5)
            {
                return 50;
            }
            if (x >= 14.0)
            {
                return 200;
            }
            if (x <= 13.0)
            {
                return (int)Math.Round(50 + 72 * (x - 12.5));
            }
            if (x <= 13.5)
            {
                return (int)Math.Round(86 + 84 * (x - 13.0));
            }
            return (int)Math.Round(128 + 144 * (x - 13.5));
        }

        private static double Quantile(List<double> values, double q)
        {
            if (values.Count == 0)
            {
                return 0.0;
            }
            var data = new List<double>(values);
            data.Sort();
            if (data.Count == 1)
            {
                return data[0];
            }
            double pos = (data.Count - 1) * q;
            int lo = (int)pos;
            int hi = Math.Min(lo + 1, data.Count - 1);
            double frac = pos - lo;
            return data[lo] * (1 - frac) + data[hi] * frac;
        }

        private static V3Result FitV3(List<AnalysisChart> charts, Dictionary<string, AnalysisScore> scores,
                                     Dictionary<string, double> fitMap, HashSet<string> unreliable,
                                     Params p, Dictionary<string, List<CurvePoint>> curves)
        {
            var v = new V3Result();
            var order = new List<string>();
            var items = new Dictionary<string, Item>(StringComparer.Ordinal);
            foreach (var chart in charts)
            {
                if (chart.Family == "utage" || chart.Family == "utage2p")
                {
                    continue;
                }
                var it = new Item();
                it.Level = chart.Level;
                it.LocalLevel = chart.LocalLevel;
                it.Version = chart.Version ?? "";
                it.Score = FindScore(scores, chart);
                foreach (var t in chart.Tags)
                {
                    it.Names.Add(t.Name);
                }
                double fit;
                double? delta = null;
                if (fitMap.TryGetValue(chart.CKey, out fit) && !unreliable.Contains(chart.CKey))
                {
                    delta = fit;
                }
                double? d = null;
                if (delta != null)
                {
                    double dd = delta.Value - it.Level;
                    d = Math.Max(dd, DeltaMin);
                }
                it.Delta = d;
                items[chart.CKey] = it;
                order.Add(chart.CKey);
            }
            v.Samples = items.Count;

            // ① 评价定数：水鱼 > 档位 Δ 先验 > 官标
            var waterGroups = new Dictionary<string, Dictionary<int, List<double>>>(StringComparer.Ordinal);
            waterGroups["water"] = new Dictionary<int, List<double>>();
            waterGroups["fake"] = new Dictionary<int, List<double>>();
            waterGroups["both"] = new Dictionary<int, List<double>>();
            foreach (var k in order)
            {
                var it = items[k];
                if (it.Delta == null)
                {
                    continue;
                }
                bool hw = it.Names.Contains("水");
                bool hf = it.Names.Contains("诈称谱");
                if (!(hw || hf))
                {
                    continue;
                }
                string kind = (hw && hf) ? "both" : (hw ? "water" : "fake");
                int band = (int)Math.Floor(it.Level);
                var g = waterGroups[kind];
                List<double> vals;
                if (!g.TryGetValue(band, out vals))
                {
                    vals = new List<double>();
                    g[band] = vals;
                }
                vals.Add(it.Delta.Value);
            }
            var bandDelta = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var kind in new[] { "water", "fake", "both" })
            {
                var groups = waterGroups[kind];
                var all = new List<double>();
                foreach (var vals in groups.Values)
                {
                    all.AddRange(vals);
                }
                var doc = new Dictionary<string, object>(StringComparer.Ordinal);
                doc["global"] = Math.Round(all.Count == 0 ? 0.0 : Mean(all), 4);
                var bands = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var kv in groups)
                {
                    if (kv.Value.Count == 0)
                    {
                        continue;
                    }
                    bands[kv.Key.ToString(CultureInfo.InvariantCulture)] = Math.Round(Mean(kv.Value), 4);
                }
                doc["bands"] = bands;
                bandDelta[kind] = doc;
            }
            Func<string, int, double> waterPrior = delegate (string kind, int band)
            {
                object o;
                if (!bandDelta.TryGetValue(kind, out o))
                {
                    return 0.0;
                }
                var doc = o as Dictionary<string, object>;
                var bands = JsonLike.ObjOf(doc, "bands");
                object val;
                if (bands != null && bands.TryGetValue(band.ToString(CultureInfo.InvariantCulture), out val))
                {
                    return (double)val;
                }
                return 0.0;
            };

            foreach (var k in order)
            {
                var it = items[k];
                if (it.Delta != null)
                {
                    it.EvalLevel = it.Level + it.Delta.Value;
                    it.FitBase = "water";
                    continue;
                }
                bool hw = it.Names.Contains("水");
                bool hf = it.Names.Contains("诈称谱");
                if (hw || hf)
                {
                    string kind = (hw && hf) ? "both" : (hw ? "water" : "fake");
                    double prior = waterPrior(kind, (int)Math.Floor(it.Level));
                    it.EvalLevel = it.Level + prior;
                    it.FitBase = "band-prior";
                    it.WaterPrior = Math.Round(prior, 4);
                    it.WaterKind = kind;
                }
                else
                {
                    it.EvalLevel = it.Level;
                    it.FitBase = "official";
                }
            }

            // ② 能力区间
            var rated = new List<KeyValuePair<double, string>>();
            foreach (var k in order)
            {
                var it = items[k];
                if (it.Score == null || it.Score.Achievement <= 0)
                {
                    continue;
                }
                bool ap = it.Score.Combo >= 3;
                rated.Add(new KeyValuePair<double, string>(Rating.Of(it.LocalLevel, it.Score.Achievement, ap), k));
            }
            rated = rated.OrderByDescending(delegate (KeyValuePair<double, string> x) { return x.Key; }).ToList();

            var versions = new List<string>();
            foreach (var k in order)
            {
                versions.Add(items[k].Version);
            }
            HashSet<string> newVer;
            var current = Versions.NewSongVersions(versions, out newVer);
            var oldForN = rated.Where(delegate (KeyValuePair<double, string> x) { return !newVer.Contains(items[x.Value].Version); })
                               .OrderByDescending(delegate (KeyValuePair<double, string> x) { return x.Key; }).ToList();
            var newForN = rated.Where(delegate (KeyValuePair<double, string> x) { return newVer.Contains(items[x.Value].Version); })
                               .OrderByDescending(delegate (KeyValuePair<double, string> x) { return x.Key; }).ToList();

            Func<List<KeyValuePair<double, string>>, int, double?> anchorLevel = delegate (List<KeyValuePair<double, string>> rows, int idx)
            {
                if (rows.Count <= idx)
                {
                    return null;
                }
                return items[rows[idx].Value].Level;
            };
            double? anchor;
            if (oldForN.Count >= 35)
            {
                anchor = anchorLevel(oldForN, 34);
            }
            else if (oldForN.Count > 0)
            {
                anchor = anchorLevel(oldForN, oldForN.Count - 1);
            }
            else if (newForN.Count >= 15)
            {
                anchor = anchorLevel(newForN, 14);
            }
            else if (newForN.Count > 0)
            {
                anchor = anchorLevel(newForN, newForN.Count - 1);
            }
            else
            {
                anchor = null;
            }
            int topN = DynamicTopN(anchor);
            v.TopNRequested = topN;
            v.TopNAnchor = anchor;
            var top = rated.Take(topN).ToList();
            v.TopN = top.Count;
            double abLo, abHi;
            if (top.Count > 0)
            {
                var evs = new List<double>();
                foreach (var t in top)
                {
                    evs.Add(items[t.Value].EvalLevel);
                }
                evs.Sort();
                abLo = Quantile(evs, 0.10);
                abHi = Quantile(evs, 0.95);
            }
            else
            {
                abLo = 0.0;
                abHi = 99.0;
            }
            v.Ability[0] = Math.Round(abLo, 3);
            v.Ability[1] = Math.Round(abHi, 3);
            v.Lo = Math.Round(abLo - 0.3, 3);
            v.Hi = Math.Round(abHi + 0.3, 3);

            // ③ 标签效应（岭回归到定数单位）
            var tagNames = new List<string>();
            var tagSet = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var k in order)
            {
                foreach (var n in items[k].Names)
                {
                    if (!IsWaterTag(n))
                    {
                        tagSet.Add(n);
                    }
                }
            }
            tagNames.AddRange(tagSet);

            var obsRows = new List<string>();
            foreach (var k in order)
            {
                var it = items[k];
                var sc = it.Score;
                if (sc == null || sc.Achievement <= 0)
                {
                    continue;
                }
                if (!(abLo <= it.EvalLevel && it.EvalLevel <= abHi))
                {
                    continue;
                }
                double exp = ExpectedAt(curves, it.EvalLevel);
                double slope = Math.Max(1.0, Math.Min(3.0, SlopeNear(curves, it.EvalLevel)));
                double raw = (sc.Achievement - exp) / slope;
                it.ObsShift = Math.Max(-1.5, Math.Min(1.5, raw));
                obsRows.Add(k);
            }
            var tagGamma = new Dictionary<string, double>(StringComparer.Ordinal);
            var tagCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var k in order)
            {
                var it = items[k];
                if (!(abLo <= it.EvalLevel && it.EvalLevel <= abHi))
                {
                    continue;
                }
                var sc = it.Score;
                if (sc == null || sc.Achievement <= 0)
                {
                    continue;
                }
                foreach (var n in it.Names)
                {
                    if (!IsWaterTag(n))
                    {
                        tagCounts[n] = tagCounts.ContainsKey(n) ? tagCounts[n] + 1 : 1;
                    }
                }
            }
            v.TagSampleFactor = obsRows.Count < 300 ? 0.5 : 1.0;
            v.TagSampleN = obsRows.Count;
            if (obsRows.Count > 0 && tagNames.Count > 0)
            {
                Func<double, double> bandOf = delegate (double val) { return Math.Round(val * 2) / 2; };
                var bandCount = new Dictionary<double, int>();
                foreach (var k in obsRows)
                {
                    double b = bandOf(items[k].EvalLevel);
                    bandCount[b] = bandCount.ContainsKey(b) ? bandCount[b] + 1 : 1;
                }
                var bands = bandCount.Where(delegate (KeyValuePair<double, int> kv) { return kv.Value >= 10; })
                                     .Select(delegate (KeyValuePair<double, int> kv) { return kv.Key; })
                                     .OrderBy(delegate (double d) { return d; }).ToList();
                var X = new List<double[]>();
                var y = new List<double>();
                foreach (var k in obsRows)
                {
                    var it = items[k];
                    var row = new double[1 + bands.Count + tagNames.Count];
                    row[0] = 1.0;
                    for (int bi = 0; bi < bands.Count; bi++)
                    {
                        row[1 + bi] = bandOf(it.EvalLevel) == bands[bi] ? 1.0 : 0.0;
                    }
                    for (int ti = 0; ti < tagNames.Count; ti++)
                    {
                        row[1 + bands.Count + ti] = it.Names.Contains(tagNames[ti]) ? 1.0 : 0.0;
                    }
                    X.Add(row);
                    y.Add(it.ObsShift);
                }
                int off = 1 + bands.Count;
                int m = X.Count;
                int dim = X[0].Length;
                var sds = new double[tagNames.Count];
                for (int j = off; j < dim; j++)
                {
                    double sum = 0;
                    for (int i = 0; i < m; i++)
                    {
                        sum += X[i][j];
                    }
                    double mu = sum / m;
                    double sq = 0;
                    for (int i = 0; i < m; i++)
                    {
                        double dv = X[i][j] - mu;
                        sq += dv * dv;
                    }
                    double sd = Math.Sqrt(sq / m);
                    sds[j - off] = sd;
                    if (sd > 1e-9)
                    {
                        for (int i = 0; i < m; i++)
                        {
                            X[i][j] = (X[i][j] - mu) / sd;
                        }
                    }
                }
                var ata = new double[dim, dim];
                var aty = new double[dim];
                for (int i = 0; i < dim; i++)
                {
                    for (int j = 0; j < dim; j++)
                    {
                        double sum = 0;
                        for (int r = 0; r < m; r++)
                        {
                            sum += X[r][i] * X[r][j];
                        }
                        ata[i, j] = sum;
                    }
                    double sy = 0;
                    for (int r = 0; r < m; r++)
                    {
                        sy += X[r][i] * y[r];
                    }
                    aty[i] = sy;
                }
                for (int i = off; i < dim; i++)
                {
                    ata[i, i] += p.TagRidgeLambda;
                }
                var coef = SolveGauss(ata, aty, dim);
                for (int idx = 0; idx < tagNames.Count; idx++)
                {
                    double sd = sds[idx];
                    if (sd <= 1e-9)
                    {
                        tagGamma[tagNames[idx]] = 0.0;
                        continue;
                    }
                    double g = coef[off + idx] / sd;
                    int a = tagCounts.ContainsKey(tagNames[idx]) ? tagCounts[tagNames[idx]] : 0;
                    g *= a / (a + 20.0);
                    tagGamma[tagNames[idx]] = Math.Max(-0.25, Math.Min(0.25, g));
                }
            }
            v.TagU = tagGamma;
            var patNames = new HashSet<string>(StringComparer.Ordinal);
            var evalNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in charts)
            {
                foreach (var t in c.Tags)
                {
                    if (IsWaterTag(t.Name))
                    {
                        continue;
                    }
                    if (t.Group == PatternGroup)
                    {
                        patNames.Add(t.Name);
                    }
                    else if (t.Group == EvalGroup)
                    {
                        evalNames.Add(t.Name);
                    }
                }
            }
            var weakTags = new HashSet<string>(StringComparer.Ordinal);
            foreach (var kv in tagGamma)
            {
                if (kv.Value <= -0.05)
                {
                    weakTags.Add(kv.Key);
                }
            }
            v.WeakTags = weakTags.OrderBy(delegate (string s) { return s; }, StringComparer.Ordinal).ToList();
            v.WeakPattern = weakTags.Where(delegate (string s) { return patNames.Contains(s); })
                                    .OrderBy(delegate (string s) { return s; }, StringComparer.Ordinal).ToList();
            v.WeakEval = weakTags.Where(delegate (string s) { return evalNames.Contains(s); })
                                 .OrderBy(delegate (string s) { return s; }, StringComparer.Ordinal).ToList();

            double tagSpread = 0.0;
            double tagBalanceFactor = 1.0;
            if (tagGamma.Count > 0)
            {
                double mx = double.MinValue, mn = double.MaxValue;
                foreach (var g in tagGamma.Values)
                {
                    mx = Math.Max(mx, g);
                    mn = Math.Min(mn, g);
                }
                tagSpread = mx - mn;
                if (p.TagBalanceMode == "spread")
                {
                    double rf = Math.Max(1e-6, p.TagBalanceRef);
                    double ratio = tagSpread / rf;
                    tagBalanceFactor = Math.Max(p.TagBalanceFloor,
                                               Math.Min(1.0, ratio > 0 ? Math.Pow(ratio, p.TagBalancePower) : 0.0));
                }
            }
            v.TagSpread = Math.Round(tagSpread, 4);
            v.TagBalanceFactor = Math.Round(tagBalanceFactor, 4);
            v.TagBalanceMode = p.TagBalanceMode;
            v.TagCountMode = p.TagCountMode;
            var weakPat = new HashSet<string>(v.WeakPattern, StringComparer.Ordinal);
            var weakEval = new HashSet<string>(v.WeakEval, StringComparer.Ordinal);

            // ④ 逐谱：区间内才修正
            double lo = v.Lo, hi = v.Hi;
            foreach (var k in order)
            {
                var it = items[k];
                var rec = new AnalysisRec();
                double tagCorr = 0.0, tagRaw = 0.0, tagFactor = 1.0, tagScope = 0.0;
                int tagK = 0;
                int tier = 0;
                double fit = it.EvalLevel;
                bool inRange = lo <= it.EvalLevel && it.EvalLevel <= hi;
                if (inRange)
                {
                    double scoreCorr = 0.0;
                    var score = it.Score;
                    if (score != null && score.Achievement > 0)
                    {
                        double coef = score.PlayCount >= 10 ? 0.2 : 0.06;
                        scoreCorr = coef * ScoreWeight(score.Achievement);
                    }
                    double E = it.EvalLevel;
                    if (abLo <= E && E <= abHi)
                    {
                        tagScope = 1.0;
                    }
                    else if (lo <= E && E < abLo)
                    {
                        tagScope = 0.8;
                    }
                    else if (abHi < E && E <= hi)
                    {
                        tagScope = 0.8;
                    }
                    else
                    {
                        tagScope = 0.0;
                    }
                    if (abLo + 0.1 <= E && E <= abHi - 0.1)
                    {
                        tier = 1;
                    }
                    else if (abHi - 0.1 < E && E <= hi)
                    {
                        tier = 2;
                    }
                    else if (lo <= E && E < abLo + 0.1)
                    {
                        tier = 3;
                    }
                    else
                    {
                        tier = 1;
                    }
                    Func<string, double> tagMult = delegate (string name)
                    {
                        double g;
                        if (!tagGamma.TryGetValue(name, out g))
                        {
                            g = 0.0;
                        }
                        if (tier == 2)
                        {
                            if (g <= -0.05)
                            {
                                return 1.5;
                            }
                            if (g < 0)
                            {
                                return 1.2;
                            }
                            return 1.0;
                        }
                        if (tier == 3)
                        {
                            return g < 0 ? 0.5 : 1.0;
                        }
                        return 1.0;
                    };
                    var names = new List<string>();
                    if (tagScope > 0)
                    {
                        foreach (var n in it.Names)
                        {
                            if (!IsWaterTag(n) && tagGamma.ContainsKey(n))
                            {
                                names.Add(n);
                            }
                        }
                    }
                    tagK = names.Count;
                    if (p.TagCountMode == "sqrt")
                    {
                        tagFactor = 1.0 / Math.Sqrt(Math.Max(1, tagK));
                    }
                    else if (p.TagCountMode == "linear")
                    {
                        tagFactor = 1.0 / (1.0 + p.TagCountAlpha * Math.Max(0, tagK - 1));
                    }
                    foreach (var n in names)
                    {
                        tagRaw += tagGamma[n] * tagMult(n);
                    }
                    double tagBase = Math.Max(-0.3, Math.Min(0.3, -tagBalanceFactor * tagFactor * tagRaw * tagScope));
                    tagCorr = tagBase * v.TagSampleFactor;
                    double corr = Math.Max(-0.3, Math.Min(0.3, scoreCorr + tagCorr));
                    fit += corr;
                }
                rec.Fit = Math.Round(fit, 3);
                rec.Mode = it.FitBase;
                rec.InRange = inRange;
                rec.InConf = lo <= fit && fit <= hi;
                rec.Tier = inRange ? tier : 0;
                rec.EvalLevel = Math.Round(it.EvalLevel, 3);
                rec.LocalLevel = it.LocalLevel;
                rec.Rating = (it.Score != null && it.Score.Achievement > 0)
                    ? Rating.Of(it.LocalLevel, it.Score.Achievement, it.Score.Combo >= 3) : -1;
                rec.TagCorr = Math.Round(tagCorr, 4);
                rec.TagRaw = Math.Round(tagRaw, 4);
                rec.TagK = tagK;
                rec.TagFactor = Math.Round(tagFactor, 4);
                rec.TagBalance = Math.Round(tagBalanceFactor, 4);
                rec.TagScope = Math.Round(tagScope, 2);
                rec.TagSample = Math.Round(v.TagSampleFactor, 2);
                rec.WaterKind = it.WaterKind;
                rec.WaterPrior = it.WaterPrior;
                v.ByKey[k] = rec;
            }

            var inConfFits = v.ByKey.Values.Where(delegate (AnalysisRec r) { return r.InConf; })
                                           .Select(delegate (AnalysisRec r) { return r.Fit; })
                                           .OrderBy(delegate (double d) { return d; }).ToList();
            v.FitMedian = inConfFits.Count > 0 ? inConfFits[inConfFits.Count / 2] : 0.0;
            foreach (var rec in v.ByKey.Values)
            {
                if (rec.InRange)
                {
                    int t = rec.Tier == 0 ? 1 : rec.Tier;
                    v.Tiers[t] = v.Tiers[t] + 1;
                }
            }

            var versions2 = new List<string>();
            foreach (var k in order)
            {
                versions2.Add(items[k].Version);
            }
            HashSet<string> newVersions;
            v.CurrentVersion = Versions.NewSongVersions(versions2, out newVersions);
            v.NewVersions = newVersions.OrderBy(delegate (string s) { return s; }, StringComparer.Ordinal).ToList();
            var oldRows = v.ByKey.Where(delegate (KeyValuePair<string, AnalysisRec> kv)
                                        {
                                            return kv.Value.Rating > 0 && !newVersions.Contains(items[kv.Key].Version);
                                        })
                                 .Select(delegate (KeyValuePair<string, AnalysisRec> kv)
                                         { return new KeyValuePair<int, string>(kv.Value.Rating, kv.Key); })
                                 .OrderBy(delegate (KeyValuePair<int, string> x) { return x.Key; }).ToList();
            oldRows.Reverse();
            var newRows = v.ByKey.Where(delegate (KeyValuePair<string, AnalysisRec> kv)
                                        {
                                            return kv.Value.Rating > 0 && newVersions.Contains(items[kv.Key].Version);
                                        })
                                 .Select(delegate (KeyValuePair<string, AnalysisRec> kv)
                                         { return new KeyValuePair<int, string>(kv.Value.Rating, kv.Key); })
                                 .OrderBy(delegate (KeyValuePair<int, string> x) { return x.Key; }).ToList();
            newRows.Reverse();
            v.OldCount = oldRows.Count;
            v.NewCount = newRows.Count;
            v.B35 = oldRows.Count >= 35 ? oldRows[34].Key : 0;
            v.B15 = newRows.Count >= 15 ? newRows[14].Key : 0;
            var inB35 = new HashSet<string>(oldRows.Take(35).Select(delegate (KeyValuePair<int, string> x) { return x.Value; }), StringComparer.Ordinal);
            var inB15 = new HashSet<string>(newRows.Take(15).Select(delegate (KeyValuePair<int, string> x) { return x.Value; }), StringComparer.Ordinal);
            foreach (var kv in v.ByKey)
            {
                bool isNew = newVersions.Contains(items[kv.Key].Version);
                kv.Value.Pool = isNew ? "new" : "old";
                kv.Value.Cutoff = isNew ? v.B15 : v.B35;
                kv.Value.InB50 = isNew ? inB15.Contains(kv.Key) : inB35.Contains(kv.Key);
            }
            bool balanced = v.TagU.Count > 0;
            if (balanced)
            {
                foreach (var g in v.TagU.Values)
                {
                    if (Math.Abs(g) >= 0.05)
                    {
                        balanced = false;
                        break;
                    }
                }
            }
            v.ProfileStatus = balanced ? "balanced" : "distinct";
            v.BandDelta = bandDelta;
            v.TagUPattern = new Dictionary<string, double>(StringComparer.Ordinal);
            v.TagUEval = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var kv in v.TagU)
            {
                if (patNames.Contains(kv.Key))
                {
                    v.TagUPattern[kv.Key] = kv.Value;
                }
                if (evalNames.Contains(kv.Key))
                {
                    v.TagUEval[kv.Key] = kv.Value;
                }
            }
            return v;
        }

        // ---------------------------------------------------------------- 分类

        private static List<string> Classify(AnalysisChart chart, AnalysisScore score, double effective,
                                             double[] ability, double? evalLevel, double? localLevel,
                                             double? poolCutoff, string pool, bool inConf, bool inB50,
                                             double[] confRange, HashSet<string> weakTags,
                                             HashSet<string> weakPat, HashSet<string> weakEval)
        {
            var labels = new List<string>();
            double abLo = ability[0], abHi = ability[1];
            double confHi = confRange[1];
            double abilityMid = (abLo + abHi) / 2;
            double local = localLevel != null ? localLevel.Value : chart.LocalLevel;
            if (score != null && score.Achievement > 0)
            {
                double ach = score.Achievement;
                int combo = score.Combo;
                bool isMastered = ach >= DefaultParams.MasteredAch && abLo <= effective && effective <= abHi;
                string pushLabel = null;
                if (effective < confHi - 0.15 && local > 0 && ach < 100.5)
                {
                    double reach = Math.Max(Rating.Of(local, 100.0), Rating.Of(local, 100.5));
                    if (inB50 || reach > (poolCutoff ?? 0))
                    {
                        pushLabel = pool == "new" ? "pushable_b15" : "pushable_b35";
                    }
                }
                if (isMastered)
                {
                    labels.Add("mastered");
                }
                if (abHi < effective && effective <= confHi)
                {
                    labels.Add("challenge");
                }
                if (pushLabel != null)
                {
                    labels.Add(pushLabel);
                }
                if (inConf && ach > 100.85 && local > 0 && combo < 3)
                {
                    int apRating = Rating.Of(local, ach, true);
                    if (inB50 || apRating > (poolCutoff ?? 0))
                    {
                        labels.Add("ap");
                    }
                }
                int weakPatHits = 0;
                int weakEvalHits = 0;
                foreach (var t in chart.Tags)
                {
                    if (weakPat.Contains(t.Name))
                    {
                        weakPatHits++;
                    }
                    if (weakEval.Contains(t.Name))
                    {
                        weakEvalHits++;
                    }
                }
                if (inConf && ach < 100.6 && abilityMid < effective && effective < abHi
                    && (weakPatHits >= 2 || weakEvalHits >= 1))
                {
                    labels.Add("practice");
                }
                if (abLo <= effective && effective <= abilityMid && ach > 100.7 && combo < 3)
                {
                    labels.Add("accuracy");
                }
                return labels;
            }
            if (abHi < effective && effective <= confHi)
            {
                labels.Add("challenge");
            }
            else if (abilityMid <= effective && effective <= abHi)
            {
                labels.Add("unplayed");
            }
            if (effective < confHi - 0.15 && local > 0)
            {
                double reach = Math.Max(Rating.Of(local, 100.0), Rating.Of(local, 100.5));
                if (reach > (poolCutoff ?? 0))
                {
                    labels.Add(pool == "new" ? "pushable_b15" : "pushable_b35");
                }
            }
            return labels;
        }

        // ---------------------------------------------------------------- 数学小工具

        private static double Mean(List<double> v)
        {
            double s = 0;
            foreach (var x in v)
            {
                s += x;
            }
            return v.Count == 0 ? 0.0 : s / v.Count;
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

        private static double PStdDev(List<double> v)
        {
            if (v.Count == 0)
            {
                return 0.0;
            }
            double m = Mean(v);
            double s = 0;
            foreach (var x in v)
            {
                double d = x - m;
                s += d * d;
            }
            return Math.Sqrt(s / v.Count);
        }

        /// <summary>高斯消元解 A·x = b（A 为方阵，就地修改）。奇异位置留 0。</summary>
        private static double[] SolveGauss(double[,] ata, double[] aty, int n)
        {
            for (int c = 0; c < n; c++)
            {
                int piv = c;
                double best = Math.Abs(ata[c, c]);
                for (int r = c + 1; r < n; r++)
                {
                    double v = Math.Abs(ata[r, c]);
                    if (v > best)
                    {
                        best = v;
                        piv = r;
                    }
                }
                if (best < 1e-12)
                {
                    continue;
                }
                if (piv != c)
                {
                    for (int k = 0; k < n; k++)
                    {
                        double tmp = ata[c, k];
                        ata[c, k] = ata[piv, k];
                        ata[piv, k] = tmp;
                    }
                    double ty = aty[c];
                    aty[c] = aty[piv];
                    aty[piv] = ty;
                }
                for (int r = c + 1; r < n; r++)
                {
                    double f = ata[r, c] / ata[c, c];
                    if (f == 0)
                    {
                        continue;
                    }
                    for (int c2 = c; c2 < n; c2++)
                    {
                        ata[r, c2] -= f * ata[c, c2];
                    }
                    aty[r] -= f * aty[c];
                }
            }
            var outv = new double[n];
            for (int i = n - 1; i >= 0; i--)
            {
                if (Math.Abs(ata[i, i]) < 1e-12)
                {
                    continue;
                }
                double acc = aty[i];
                for (int j = i + 1; j < n; j++)
                {
                    acc -= ata[i, j] * outv[j];
                }
                outv[i] = acc / ata[i, i];
            }
            return outv;
        }

        // ---------------------------------------------------------------- JSON 输出

        private static string N(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
            {
                return "0";
            }
            var s = v.ToString("0.###", CultureInfo.InvariantCulture);
            return s == "-0" ? "-0.0" : s;
        }

        private static string N(double? v)
        {
            return v == null ? "null" : N(v.Value);
        }

        private static string N4(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v))
            {
                return "0";
            }
            return v.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private static string N4(double? v)
        {
            return v == null ? "null" : N4(v.Value);
        }

        private static string Q(string s)
        {
            return s == null ? "null" : "\"" + JsonMini.Esc(s) + "\"";
        }

        private static string Arr(IEnumerable<string> items)
        {
            var sb = new StringBuilder();
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

        private static string IntArr(int[] items)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            for (int i = 0; i < items.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                sb.Append(items[i]);
            }
            sb.Append(']');
            return sb.ToString();
        }

        /// <summary>单独给曲目卡片用的 fitModel 片段（没有结果时给 {}）。</summary>
        internal static string ToFitModelJson(AnalysisResult r)
        {
            if (r == null)
            {
                return "{}";
            }
            var sb = new StringBuilder(512);
            sb.Append("{\"model\":\"v3.2\"");
            sb.Append(",\"range\":[").Append(N(r.Lo)).Append(',').Append(N(r.Hi)).Append(']');
            sb.Append(",\"abilityRange\":[").Append(N(r.Ability[0])).Append(',').Append(N(r.Ability[1])).Append(']');
            sb.Append(",\"fitMedian\":").Append(N(r.FitMedian));
            sb.Append(",\"weakPattern\":").Append(Arr(r.WeakPattern));
            sb.Append(",\"weakEval\":").Append(Arr(r.WeakEval));
            sb.Append(",\"tagU\":");
            AppendTagDouble(sb, r.TagU, 3);
            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>analysis.json 全文（与原文件同构）。</summary>
        internal static string ToJson(AnalysisResult r)
        {
            var sb = new StringBuilder(1 << 20);
            sb.Append("{\"schema\":1,\"runId\":\"mod\"");
            sb.Append(",\"generatedAt\":\"").Append(JsonMini.Esc(NowStamp())).Append('"');
            sb.Append(",\"params\":{\"fit_score_base\":0.2,\"fit_score_low\":0.06");
            sb.Append(",\"fit_cap\":0.3,\"level_bin\":0.5,\"bin_min_samples\":3");
            sb.Append(",\"mastered_ach\":").Append(N(DefaultParams.MasteredAch));
            sb.Append(",\"tag_count_mode\":").Append(Q(DefaultParams.TagCountMode));
            sb.Append(",\"tag_count_alpha\":").Append(N(DefaultParams.TagCountAlpha));
            sb.Append(",\"tag_balance_mode\":").Append(Q(DefaultParams.TagBalanceMode));
            sb.Append(",\"tag_balance_ref\":").Append(N(DefaultParams.TagBalanceRef));
            sb.Append(",\"tag_balance_power\":").Append(N(DefaultParams.TagBalancePower));
            sb.Append(",\"tag_balance_floor\":").Append(N(DefaultParams.TagBalanceFloor));
            sb.Append('}');
            sb.Append(",\"fitModel\":{\"model\":\"v3.2\"");
            sb.Append(",\"range\":[").Append(N(r.Lo)).Append(',').Append(N(r.Hi)).Append(']');
            sb.Append(",\"abilityRange\":[").Append(N(r.Ability[0])).Append(',').Append(N(r.Ability[1])).Append(']');
            sb.Append(",\"fitMedian\":").Append(N(r.FitMedian));
            sb.Append(",\"weakTags\":").Append(Arr(r.WeakTags));
            sb.Append(",\"weakPattern\":").Append(Arr(r.WeakPattern));
            sb.Append(",\"weakEval\":").Append(Arr(r.WeakEval));
            sb.Append(",\"profileStatus\":").Append(Q(r.ProfileStatus));
            sb.Append(",\"tagBalance\":{\"mode\":").Append(Q(r.TagBalanceMode))
              .Append(",\"countMode\":").Append(Q(r.TagCountMode))
              .Append(",\"spread\":").Append(N4(r.TagSpread))
              .Append(",\"factor\":").Append(N4(r.TagBalanceFactor)).Append('}');
            sb.Append(",\"tiers\":{");
            sb.Append("\"1\":").Append(r.Tiers[1]).Append(",\"2\":").Append(r.Tiers[2])
              .Append(",\"3\":").Append(r.Tiers[3]).Append('}');
            sb.Append(",\"b50\":{\"b35\":").Append(r.B35).Append(",\"b15\":").Append(r.B15);
            sb.Append(",\"current\":").Append(Q(r.CurrentVersion));
            sb.Append(",\"newVersions\":").Append(Arr(r.NewVersions));
            sb.Append(",\"oldCount\":").Append(r.OldCount).Append(",\"newCount\":").Append(r.NewCount).Append('}');
            sb.Append(",\"stamina\":{\"eval\":[\"体力谱\",\"键盘谱\",\"高物量\"]");
            sb.Append(",\"pat\":[\"交互\",\"大位移\",\"散打\",\"暴发\",\"纵连\",\"转圈\"]");
            sb.Append(",\"mult\":{\"top\":1.5,\"topPos\":1.0,\"bottom\":0.5}}");
            sb.Append(",\"topN\":").Append(r.TopN);
            sb.Append(",\"topNRequested\":").Append(r.TopNRequested);
            sb.Append(",\"topNMode\":").Append(Q(r.TopNMode));
            sb.Append(",\"topNAnchor\":").Append(N(r.TopNAnchor));
            sb.Append(",\"tagSample\":{\"n\":").Append(r.TagSampleN)
              .Append(",\"factor\":").Append(N(r.TagSampleFactor)).Append('}');
            sb.Append(",\"bandDelta\":");
            AppendBandDelta(sb, r.BandDelta);
            sb.Append(",\"tagU\":");
            AppendTagDouble(sb, r.TagU, 3);
            sb.Append(",\"tagUPattern\":");
            AppendTagDouble(sb, r.TagUPattern, 3);
            sb.Append(",\"tagUEval\":");
            AppendTagDouble(sb, r.TagUEval, 3);
            sb.Append(",\"topTags\":");
            AppendTagDouble(sb, r.TopTags, 0);
            sb.Append(",\"tagCalib\":{\"pattern\":0.093,\"eval\":0.191}");
            sb.Append(",\"scoreWeight\":{\"base\":0.2,\"lowPlays\":0.06");
            sb.Append(",\"table\":[[98,1.5],[99,1.0],[100,0.5],[100.5,0.0],[101,-1.0]],\"cap\":0.3}");
            sb.Append(",\"samples\":").Append(r.Samples).Append('}');

            sb.Append(",\"counts\":{\"charts\":").Append(r.Rows.Count)
              .Append(",\"played\":").Append(r.Played)
              .Append(",\"blacklisted\":").Append(r.BlacklistedCount);
            sb.Append(",\"labels\":{");
            bool lf = true;
            foreach (var kv in r.Counts)
            {
                if (kv.Key == "blacklisted")
                {
                    continue;
                }
                if (!lf)
                {
                    sb.Append(',');
                }
                lf = false;
                sb.Append(Q(kv.Key)).Append(':').Append(kv.Value);
            }
            sb.Append("}}");

            sb.Append(",\"abilityCurve\":{");
            bool cf = true;
            foreach (var kv in r.Curves)
            {
                if (!cf)
                {
                    sb.Append(',');
                }
                cf = false;
                sb.Append(Q(kv.Key)).Append(":[");
                for (int i = 0; i < kv.Value.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }
                    var pt = kv.Value[i];
                    sb.Append("{\"level\":").Append(N(pt.Level))
                      .Append(",\"expected\":").Append(N4(pt.Expected))
                      .Append(",\"n\":").Append(pt.N).Append('}');
                }
                sb.Append(']');
            }
            sb.Append('}');

            sb.Append(",\"tagStats\":[]");

            sb.Append(",\"charts\":[");
            for (int i = 0; i < r.Rows.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                var row = r.Rows[i];
                sb.Append("{\"internalId\":").Append(row.InternalId);
                sb.Append(",\"chartid\":").Append(row.ChartId);
                sb.Append(",\"difficulty\":").Append(Q(row.Difficulty));
                sb.Append(",\"tier\":").Append(Q(row.Tier));
                sb.Append(",\"family\":").Append(Q(row.Family));
                sb.Append(",\"level\":").Append(N(row.Level));
                sb.Append(",\"effectiveLevel\":").Append(N(row.EffectiveLevel));
                sb.Append(",\"fitEvalLevel\":").Append(N(row.FitEvalLevel));
                sb.Append(",\"fitInConf\":").Append(row.FitInConf == null ? "null" : (row.FitInConf.Value ? "true" : "false"));
                sb.Append(",\"fitTier\":").Append(row.FitTier == null ? "null" : row.FitTier.Value.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"tagCorr\":").Append(N4(row.TagCorr));
                sb.Append(",\"tagRaw\":").Append(N4(row.TagRaw));
                sb.Append(",\"tagK\":").Append(row.TagK == null ? "null" : row.TagK.Value.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"tagFactor\":").Append(N4(row.TagFactor));
                sb.Append(",\"tagBalance\":").Append(N4(row.TagBalance));
                sb.Append(",\"tagScope\":").Append(N4(row.TagScope));
                sb.Append(",\"tagSample\":").Append(N4(row.TagSample));
                sb.Append(",\"waterKind\":").Append(Q(row.WaterKind));
                sb.Append(",\"waterPrior\":").Append(N4(row.WaterPrior));
                sb.Append(",\"fitMode\":").Append(Q(row.FitMode));
                sb.Append(",\"deltaUnreliable\":").Append(row.DeltaUnreliable ? "true" : "false");
                sb.Append(",\"delta\":").Append(N(row.Delta));
                sb.Append(",\"tags\":").Append(Arr(row.Tags));
                sb.Append(",\"achievement\":").Append(N4(row.Achievement));
                sb.Append(",\"combo\":").Append(row.Combo == null ? "null" : row.Combo.Value.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"sync\":").Append(row.Sync == null ? "null" : row.Sync.Value.ToString(CultureInfo.InvariantCulture));
                sb.Append(",\"playCount\":").Append(row.PlayCount);
                sb.Append(",\"expected\":").Append(N4(row.Expected));
                sb.Append(",\"residual\":").Append(N4(row.Residual));
                sb.Append(",\"labels\":").Append(Arr(row.Labels));
                sb.Append(",\"predicted\":").Append(N(row.Predicted));
                sb.Append(",\"blacklisted\":").Append(row.Blacklisted ? "true" : "false");
                sb.Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private static void AppendBandDelta(StringBuilder sb, Dictionary<string, object> bandDelta)
        {
            sb.Append('{');
            bool first = true;
            foreach (var kind in new[] { "water", "fake", "both" })
            {
                object o;
                if (!bandDelta.TryGetValue(kind, out o))
                {
                    continue;
                }
                if (!first)
                {
                    sb.Append(',');
                }
                first = false;
                var doc = o as Dictionary<string, object>;
                sb.Append(Q(kind)).Append(":{\"global\":").Append(N4(JsonLike.DblOf(doc, "global")));
                sb.Append(",\"bands\":{");
                var bands = JsonLike.ObjOf(doc, "bands");
                bool bf = true;
                if (bands != null)
                {
                    foreach (var kv in bands)
                    {
                        if (!bf)
                        {
                            sb.Append(',');
                        }
                        bf = false;
                        sb.Append('"').Append(JsonMini.Esc(kv.Key)).Append("\":").Append(N4(Convert.ToDouble(kv.Value, CultureInfo.InvariantCulture)));
                    }
                }
                sb.Append("}}");
            }
            sb.Append('}');
        }

        private static void AppendTagDouble(StringBuilder sb, Dictionary<string, double> map, int digits)
        {
            sb.Append('{');
            bool first = true;
            foreach (var kv in map)
            {
                if (!first)
                {
                    sb.Append(',');
                }
                first = false;
                sb.Append('"').Append(JsonMini.Esc(kv.Key)).Append("\":");
                sb.Append(digits > 0 ? N(Math.Round(kv.Value, digits)) : N(kv.Value));
            }
            sb.Append('}');
        }
    }

    /// <summary>给上面那几个「字典装 object」的地方用的取值小工具。</summary>
    internal static class JsonLike
    {
        internal static Dictionary<string, object> ObjOf(Dictionary<string, object> map, string key)
        {
            if (map == null)
            {
                return null;
            }
            object v;
            if (!map.TryGetValue(key, out v))
            {
                return null;
            }
            return v as Dictionary<string, object>;
        }

        internal static double DblOf(Dictionary<string, object> map, string key)
        {
            if (map == null)
            {
                return 0;
            }
            object v;
            if (!map.TryGetValue(key, out v) || v == null)
            {
                return 0;
            }
            return Convert.ToDouble(v, CultureInfo.InvariantCulture);
        }
    }
}
