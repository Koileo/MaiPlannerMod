// 用途：状态、配置、曲库查询、点歌跳曲、成绩回传。业务实现主体。
// 状态：现行。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;

namespace MaiPlanner
{
    internal static partial class WebApi
    {
        // ── 状态 ──
        private static void Status(HttpListenerContext ctx, Dictionary<string, object> q)
        {
            var urls = WebServer.ScanUrls();
            var sb = new StringBuilder(256);
            sb.Append("{\"ok\":true,\"ready\":").Append(Mod.Ready ? "true" : "false");
            sb.Append(",\"version\":\"").Append(JsonMini.Esc(ModVersion())).Append('"');
            sb.Append(",\"songs\":").Append(GameSnapshot.Count);
            sb.Append(",\"inSelect\":").Append(SafeBool(delegate { return SelectDriver.InSelectCached; }) ? "true" : "false");
            sb.Append(",\"busy\":").Append(SafeBool(delegate { return SelectDriver.Busy; }) ? "true" : "false");
            sb.Append(",\"inGame\":").Append(InGame() ? "true" : "false");
            sb.Append(",\"lan\":").Append(Config.LanAccess ? "true" : "false");
            sb.Append(",\"scanUrls\":[");
            for (int i = 0; i < urls.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                sb.Append('"').Append(JsonMini.Esc(urls[i])).Append('"');
            }
            sb.Append("],\"players\":").Append(PlayerStore.All.Count);
            sb.Append(",\"anyEntry\":").Append(PlayerStore.AnyEntry ? "true" : "false");
            sb.Append('}');
            Json(ctx, 200, sb.ToString());
        }

        private static string ModVersion()
        {
            try
            {
                return typeof(WebApi).Assembly.GetName().Version.ToString();
            }
            catch
            {
                return "0";
            }
        }

        private static bool SafeBool(Func<bool> f)
        {
            try { return f(); } catch { return false; }
        }

        /// <summary>是否在游玩中（成绩单有内容就算）</summary>
        internal static bool InGame()
        {
            return ScoreReporter.InGame;
        }

        // ── 配置 ──
        private static void ConfigRoute(HttpListenerContext ctx, string method)
        {
            if (method == "POST")
            {
                var body = Body(ctx);
                if (body.ContainsKey("lan"))
                {
                    Config.LanAccess = JsonMini.Bool(body, "lan");
                }
                if (body.ContainsKey("verboseLog"))
                {
                    Config.VerboseLog = JsonMini.Bool(body, "verboseLog");
                }
                if (body.ContainsKey("jumpToDifficultyScreen"))
                {
                    Config.JumpToDifficultyScreen = JsonMini.Bool(body, "jumpToDifficultyScreen");
                }
                if (body.ContainsKey("scoreCallback"))
                {
                    Config.ScoreCallback = JsonMini.Str(body, "scoreCallback").Trim();
                }
                if (body.ContainsKey("callbackPort"))
                {
                    var p = JsonMini.Int(body, "callbackPort");
                    if (p > 0 && p < 65536)
                    {
                        Config.CallbackPort = p;
                    }
                }
                Config.Save();
            }
            Json(ctx, 200, ConfigJson());
        }

        private static string ConfigJson()
        {
            return "{\"ok\":true,\"config\":{"
                + "\"port\":" + Config.Port
                + ",\"lan\":" + (Config.LanAccess ? "true" : "false")
                + ",\"verboseLog\":" + (Config.VerboseLog ? "true" : "false")
                + ",\"jumpToDifficultyScreen\":" + (Config.JumpToDifficultyScreen ? "true" : "false")
                + ",\"scoreCallback\":\"" + JsonMini.Esc(Config.ScoreCallback) + "\""
                + ",\"callbackPort\":" + Config.CallbackPort
                + "},\"chains\":{},\"chainAges\":{},\"python\":\"\""
                + ",\"projectDir\":\"" + JsonMini.Esc(Environment.CurrentDirectory) + "\""
                + ",\"ratingVersions\":[" + VersionsJson() + "]}";
        }

        /// <summary>Rating 版本列表：来自画像/评级需要，先给固定序列由 Versions 模块提供</summary>
        private static string VersionsJson()
        {
            var sb = new StringBuilder();
            try
            {
                var list = Versions.RatingVersions;
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }
                    sb.Append('"').Append(JsonMini.Esc(list[i])).Append('"');
                }
            }
            catch { }
            return sb.ToString();
        }

        // ── 曲库 ──
        private static void Songs(HttpListenerContext ctx, Dictionary<string, object> q)
        {
            var player = PlayerStore.Get(UserIndex(q));
            var versions = new HashSet<string>(StringComparer.Ordinal);
            var sb = new StringBuilder(1 << 20);
            sb.Append("{\"ok\":true,\"count\":").Append(GameSnapshot.Count).Append(",\"songs\":[");
            bool first = true;
            foreach (var s in GameSnapshot.Songs)
            {
                if (s.Disable || s.InternalId <= 0)
                {
                    continue;
                }
                if (!first)
                {
                    sb.Append(',');
                }
                first = false;
                versions.Add(s.Version);
                AppendSong(sb, s, player, false);
            }
            sb.Append("],\"versions\":[");
            bool vfirst = true;
            foreach (var v in versions)
            {
                if (!vfirst)
                {
                    sb.Append(',');
                }
                vfirst = false;
                sb.Append('"').Append(JsonMini.Esc(v)).Append('"');
            }
            sb.Append("]}");
            Json(ctx, 200, sb.ToString());
        }

        /// <summary>家族判定：内部号 ≥ 100000 宴谱、≥ 10000 DX，其余 STD</summary>
        internal static string FamilyOf(int internalId)
        {
            if (internalId >= 100000)
            {
                return "utage";
            }
            return internalId >= 10000 ? "dx" : "std";
        }

        internal static void AppendAssistantSong(StringBuilder sb, int internalId, int chartId, string user)
        {
            var song = GameSnapshot.Find(internalId);
            if (song == null || chartId < 0 || chartId >= song.Enabled.Length || !song.Enabled[chartId])
            {
                sb.Append("null");
                return;
            }
            int index;
            var player = int.TryParse(user, out index) ? PlayerStore.Get(index) : null;
            AppendSong(sb, song, player, true, chartId, user);
        }

        private static void AppendSong(StringBuilder sb, GameSnapshot.Song s, PlayerStore.Player player, bool detail,
                                       int onlyChart = -1, string user = null)
        {
            var selectedUser = user ?? QUser;
            var family = FamilyOf(s.InternalId);
            sb.Append("{\"internalId\":").Append(s.InternalId);
            sb.Append(",\"family\":\"").Append(family).Append('"');
            sb.Append(",\"type\":\"").Append(family == "std" ? "std" : "dx").Append('"');
            sb.Append(",\"dataid\":").Append(s.DataId);
            sb.Append(",\"title\":\"").Append(JsonMini.Esc(s.Title)).Append('"');
            sb.Append(",\"artist\":\"").Append(JsonMini.Esc(s.Artist)).Append('"');
            sb.Append(",\"version\":\"").Append(JsonMini.Esc(s.Version)).Append('"');
            sb.Append(",\"jacket\":").Append(Jackets.ProbablyExists(s.DataId)
                ? "\"/jacket?dataid=" + s.DataId + "\"" : "null");
            sb.Append(",\"aliasesTag\":[").Append(AliasesJson(s.InternalId)).Append(']');

            var favs = UserFiles.Favorites(selectedUser);
            var blocked = UserFiles.Blacklist(selectedUser);
            var doc = Analysis.Snapshot(selectedUser);
            bool hasRemaster = false;
            var sheets = new StringBuilder(256);
            bool sheefirst = true;
            for (int d = 0; d < 5; d++)
            {
                if (!s.Enabled[d] || (onlyChart >= 0 && d != onlyChart))
                {
                    continue;
                }
                if (d == 4)
                {
                    hasRemaster = true;
                }
                var sheet = PlayerStore.ScoreOf(player, s.InternalId, d);
                if (!sheefirst)
                {
                    sheets.Append(',');
                }
                sheefirst = false;
                sheets.Append("{\"chartid\":").Append(d);
                sheets.Append(",\"difficulty\":\"").Append(DiffName(d)).Append('"');
                sheets.Append(",\"level\":").Append(LevelFloat(s.LevelValue[d]).ToString("0.0#", CultureInfo.InvariantCulture));
                sheets.Append(",\"levelText\":\"").Append(JsonMini.Esc(s.LevelText[d])).Append('"');
                sheets.Append(",\"noteDesigner\":\"").Append(JsonMini.Esc(s.Designer[d])).Append('"');
                sheets.Append(",\"noteCounts\":").Append(s.NoteCount[d]);
                AppendScoreBits(sheets, sheet);
                var key = UserFiles.Key(s.InternalId, d);
                AnalysisRec rec = null;
                if (doc != null)
                {
                    doc.ByKey.TryGetValue(key, out rec);
                }
                AppendFitBits(sheets, rec);
                if (detail)
                {
                    sheets.Append(",\"favorite\":").Append(favs.Contains(key) ? "true" : "false");
                    sheets.Append(",\"blacklisted\":").Append(blocked.ContainsKey(key) ? "true" : "false");
                }
                sheets.Append('}');
            }
            sb.Append(",\"hasRemaster\":").Append(hasRemaster ? "true" : "false");
            sb.Append(",\"sheets\":[").Append(sheets).Append("]}");
        }

        /// <summary>分析字段（拟合/标签/评价定数）。没有分析结果时全 null，前端画白。</summary>
        private static void AppendFitBits(StringBuilder sb, AnalysisRec rec)
        {
            if (rec == null)
            {
                sb.Append(",\"fit\":null,\"fitTier\":null,\"fitEvalLevel\":null,\"fitInRange\":false")
                  .Append(",\"tagCorr\":null,\"tagRaw\":null,\"tagK\":null,\"tagFactor\":null")
                  .Append(",\"tagBalance\":null,\"tagScope\":null,\"tagSample\":null")
                  .Append(",\"waterKind\":null,\"waterPrior\":null,\"pool\":null,\"inB50\":false");
                return;
            }
            sb.Append(",\"fit\":").Append(Num(rec.Fit));
            sb.Append(",\"fitTier\":").Append(rec.Tier);
            sb.Append(",\"fitEvalLevel\":").Append(Num(rec.EvalLevel));
            sb.Append(",\"fitInRange\":").Append(rec.InRange ? "true" : "false");
            sb.Append(",\"tagCorr\":").Append(Num(rec.TagCorr));
            sb.Append(",\"tagRaw\":").Append(Num(rec.TagRaw));
            sb.Append(",\"tagK\":").Append(rec.TagK);
            sb.Append(",\"tagFactor\":").Append(Num(rec.TagFactor));
            sb.Append(",\"tagBalance\":").Append(Num(rec.TagBalance));
            sb.Append(",\"tagScope\":").Append(Num(rec.TagScope));
            sb.Append(",\"tagSample\":").Append(Num(rec.TagSample));
            sb.Append(",\"waterKind\":").Append(rec.WaterKind == null ? "null" : "\"" + JsonMini.Esc(rec.WaterKind) + "\"");
            sb.Append(",\"waterPrior\":").Append(rec.WaterPrior.HasValue ? Num(rec.WaterPrior.Value) : "null");
            sb.Append(",\"pool\":").Append(rec.Pool == null ? "null" : "\"" + JsonMini.Esc(rec.Pool) + "\"");
            sb.Append(",\"inB50\":").Append(rec.InB50 ? "true" : "false");
        }

        private static string Num(double v)
        {
            return v.ToString("0.####", CultureInfo.InvariantCulture);
        }

        private static void AppendScoreBits(StringBuilder sb, PlayerStore.Sheet sheet)
        {
            if (sheet == null)
            {
                sb.Append(",\"rank\":null,\"achievement\":null,\"combo\":null,\"sync\":null,\"playCount\":null");
                return;
            }
            sb.Append(",\"rank\":").Append(sheet.Rank);
            sb.Append(",\"achievement\":").Append(sheet.Achievement);
            sb.Append(",\"combo\":").Append(sheet.Combo);
            sb.Append(",\"sync\":").Append(sheet.Sync);
            sb.Append(",\"playCount\":").Append(sheet.PlayCount);
        }

        internal static string DiffName(int d)
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

        private static float LevelFloat(int value)
        {
            return value / 10f;
        }

        /// <summary>当前请求的玩家 id（曲库/收藏都要用）</summary>
        [ThreadStatic]
        private static string _quser;

        private static string QUser
        {
            get { return _quser ?? "0"; }
            set { _quser = value; }
        }

        internal static string AliasesJson(int internalId)
        {
            var sb = new StringBuilder();
            try
            {
                var song = FindSong(internalId);
                var list = song == null ? null : Aliases.ByTitle(song.Title);
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }
                    sb.Append('"').Append(JsonMini.Esc(list[i])).Append('"');
                }
            }
            catch { }
            return sb.ToString();
        }

        // ── 单曲详情 ──
        private static void SongDetail(HttpListenerContext ctx, Dictionary<string, object> q)
        {
            var id = QInt(q, "internalId", 0);
            var s = FindSong(id);
            if (s == null)
            {
                Json(ctx, 404, "{\"ok\":false,\"error\":\"没有这首\"}");
                return;
            }
            var player = PlayerStore.Get(UserIndex(q));
            var sb = new StringBuilder(2048);
            sb.Append("{\"ok\":true,\"song\":");
            AppendSong(sb, s, player, true);
            sb.Append(",\"fitModel\":").Append(Analysis.ToFitModelJson(Analysis.Snapshot(QUser)));
            sb.Append('}');
            Json(ctx, 200, sb.ToString());
        }

        internal static GameSnapshot.Song FindSong(int internalId)
        {
            foreach (var s in GameSnapshot.Songs)
            {
                if (s.InternalId == internalId)
                {
                    return s;
                }
            }
            // DX/STD 互查（10000 偏移）
            var alt = internalId >= 10000 ? internalId - 10000 : internalId + 10000;
            foreach (var s in GameSnapshot.Songs)
            {
                if (s.InternalId == alt)
                {
                    return s;
                }
            }
            return null;
        }

        // ── 跳曲 ──
        private static void Jump(HttpListenerContext ctx, Dictionary<string, object> q)
        {
            if (!Mod.Ready)
            {
                Json(ctx, 503, "{\"ok\":false,\"error\":\"游戏数据还在加载\"}");
                return;
            }
            RememberCaller(ctx);

            var id = QInt(q, "internalId", QInt(q, "id", 0));
            if (id <= 0)
            {
                Json(ctx, 400, "{\"ok\":false,\"error\":\"缺 internalId\"}");
                return;
            }
            var diff = QInt(q, "chartid", QInt(q, "diff", -1));
            if (diff < -1 || diff > 4)
            {
                Json(ctx, 400, "{\"ok\":false,\"error\":\"chartid 要是 0..4\"}");
                return;
            }
            int kind = -1;
            var ts = QS(q, "type").ToLowerInvariant();
            if (ts == "std")
            {
                kind = 0;
            }
            else if (ts == "dx")
            {
                kind = 1;
            }
            if (SongTable.Rev <= 0)
            {
                Json(ctx, 409, "{\"ok\":false,\"error\":\"曲目表还没建立，先进一次选曲界面\"}");
                return;
            }
            var result = SelectDriver.Enqueue(id, diff, kind);
            var superseded = result != null
                && result.StartsWith(SelectDriver.SupersededPrefix, StringComparison.Ordinal);
            Json(ctx, 200, "{\"ok\":" + (superseded ? "false" : "true")
                + ",\"superseded\":" + (superseded ? "true" : "false")
                + ",\"internalId\":" + id + ",\"chartid\":" + diff
                + ",\"msg\":\"" + JsonMini.Esc(result ?? "") + "\"}");
        }

        private static void RememberCaller(HttpListenerContext ctx)
        {
            try
            {
                var ep = ctx.Request.RemoteEndPoint;
                if (ep == null)
                {
                    return;
                }
                var ip = ep.Address.ToString();
                if (ip.StartsWith("::ffff:", StringComparison.Ordinal))
                {
                    ip = ip.Substring(7);
                }
                if (ip.Length > 0)
                {
                    Mod.LastCallerIp = ip;
                }
            }
            catch { }
        }

        private static void Probe(HttpListenerContext ctx, Dictionary<string, object> q)
        {
            Json(ctx, 200, SelectDriver.ProbeJson());
        }

        /// <summary>自检：曲绘目录、cwd、快照是否建好、成员数量</summary>
        private static void Diag(HttpListenerContext ctx, Dictionary<string, object> q)
        {
            var sb = new StringBuilder(512);
            sb.Append("{\"ok\":true");
            sb.Append(",\"cwd\":\"").Append(JsonMini.Esc(Environment.CurrentDirectory)).Append('"');
            sb.Append(",\"asmDir\":\"").Append(JsonMini.Esc(AppDomain.CurrentDomain.BaseDirectory ?? "")).Append('"');
            sb.Append(",\"jacketRoot\":\"").Append(JsonMini.Esc(Jackets.RootPath)).Append('"');
            sb.Append(",\"jacketProbe\":").Append(Jackets.ProbablyExists(146) ? "true" : "false");
            sb.Append(",\"jacketRealLoad\":").Append(Jackets.RealProbeJson());
            sb.Append(",\"songs\":").Append(GameSnapshot.Count);
            sb.Append(",\"songTable\":").Append(SongTable.Rev);
            sb.Append(",\"players\":").Append(PlayerStore.All.Count);
            sb.Append(",\"ready\":").Append(Mod.Ready ? "true" : "false");
            sb.Append(",\"aliasCount\":").Append(Aliases.LoadedCount);
            sb.Append('}');
            Json(ctx, 200, sb.ToString());
        }

        // ── 成绩回传 ──
        private static void Callback(HttpListenerContext ctx, Dictionary<string, object> q)
        {
            var url = QS(q, "url");
            if (url.Length == 0)
            {
                Json(ctx, 400, "{\"ok\":false,\"error\":\"缺 url\"}");
                return;
            }
            Mod.CallbackOverride = url;
            ModLog.Always("[点歌] 成绩回传改为: " + url);
            Json(ctx, 200, "{\"ok\":true}");
        }

        /// <summary>成绩回传地址：/callback 临时 → 配置 → 空</summary>
        internal static string CallbackTo()
        {
            if (!string.IsNullOrEmpty(Mod.CallbackOverride))
            {
                return Mod.CallbackOverride;
            }
            return Config.ScoreCallback ?? "";
        }
    }
}
