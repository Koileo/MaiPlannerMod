// 用途：打歌助手 —— 曲池卡片 → 预览队列 → 自动推歌 → 收成绩顺延。
// 状态：现行。mod 版没有外部 Python bridge，推歌=本地直接跳曲，成绩=本机 ScoreReporter 捕获。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MaiPlanner
{
    internal static class Assistant
    {
        internal static readonly Dictionary<string, string> PoolLabels = new Dictionary<string, string>
        {
            { "mastered", "熟练" }, { "pushable_b35", "推b35" }, { "pushable_b15", "推b15" },
            { "ap", "AP推分" }, { "practice", "弱点练习" }, { "accuracy", "准度练习" },
            { "challenge", "有挑战" }, { "unplayed", "待开荒" }, { "favorite", "收藏" },
            { "underdog", "随机下埋" },
        };

        private static readonly object _lock = new object();
        private static string _activeUser = "0";
        private static bool _running;
        private static string _mode = "push";        // push = 推歌；score = 只接成绩
        private static readonly StringBuilder _log = new StringBuilder();

        internal static bool Running { get { return _running; } }

        internal static void Log(string text)
        {
            lock (_lock)
            {
                _log.Append('[').Append(DateTime.Now.ToString("HH:mm:ss")).Append("] ")
                    .Append(text).Append('\n');
                if (_log.Length > 20000)
                {
                    _log.Remove(0, _log.Length - 16000);
                }
            }
        }

        internal static string LogTail()
        {
            lock (_lock)
            {
                return _log.ToString();
            }
        }

        internal static string Label(string pool)
        {
            string v;
            return PoolLabels.TryGetValue(pool ?? "", out v) ? v : (pool ?? "?");
        }

        // ── 状态落盘 ──
        private static string StateJson(string user)
        {
            return UserFiles.ReadText(user, "assistant.json");
        }

        private static Dictionary<string, object> LoadState(string user)
        {
            return JsonMini.ParseObj(StateJson(user)) ?? new Dictionary<string, object>();
        }

        private static void SaveState(string user, Dictionary<string, object> state)
        {
            UserFiles.WriteText(user, "assistant.json",
                Dump(state));
        }

        private static string Dump(Dictionary<string, object> doc)
        {
            var sb = new StringBuilder();
            DumpValue(sb, doc);
            return sb.ToString();
        }

        private static void DumpValue(StringBuilder sb, object v)
        {
            if (v == null)
            {
                sb.Append("null");
                return;
            }
            if (v is string)
            {
                sb.Append('"').Append(JsonMini.Esc((string)v)).Append('"');
                return;
            }
            if (v is bool)
            {
                sb.Append((bool)v ? "true" : "false");
                return;
            }
            if (v is int || v is long)
            {
                sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture));
                return;
            }
            if (v is double)
            {
                sb.Append(((double)v).ToString("0.####", CultureInfo.InvariantCulture));
                return;
            }
            var map = v as Dictionary<string, object>;
            if (map != null)
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
                    DumpValue(sb, kv.Value);
                }
                sb.Append('}');
                return;
            }
            var list = v as List<object>;
            if (list != null)
            {
                sb.Append('[');
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }
                    DumpValue(sb, list[i]);
                }
                sb.Append(']');
                return;
            }
            sb.Append('"').Append(JsonMini.Esc(Convert.ToString(v, CultureInfo.InvariantCulture))).Append('"');
        }

        // ── 设置 ──
        internal static Dictionary<string, object> LoadSettings(string user)
        {
            return JsonMini.ParseObj(UserFiles.ReadText(user,
                "assistant-settings.json")) ?? new Dictionary<string, object>();
        }

        internal static Dictionary<string, object> SaveSettings(string user, Dictionary<string, object> body)
        {
            var cur = LoadSettings(user);
            foreach (var key in new[] { "host", "modPort", "callbackPort" })
            {
                if (body.ContainsKey(key))
                {
                    cur[key] = body[key];
                }
            }
            UserFiles.WriteText(user,
                "assistant-settings.json", Dump(cur));
            return cur;
        }

        // ── 预览队列 ──
        private static List<object> Preview(Dictionary<string, object> state)
        {
            var list = JsonMini.Arr(state, "preview");
            if (list == null)
            {
                list = new List<object>();
                state["preview"] = list;
            }
            return list;
        }

        private static List<object> Cards(Dictionary<string, object> state)
        {
            var list = JsonMini.Arr(state, "cards");
            if (list == null)
            {
                list = new List<object>();
                state["cards"] = list;
            }
            return list;
        }

        /// <summary>按卡片配置的位置抽一首候选，补满 3 个预览位</summary>
        private static void FillPreview(string user, Dictionary<string, object> state, string analysisJson)
        {
            var cards = Cards(state);
            if (cards.Count == 0)
            {
                return;
            }
            _scratchState = state;
            var preview = Preview(state);
            var recent = JsonMini.Arr(state, "recent");
            if (recent == null)
            {
                recent = new List<object>();
                state["recent"] = recent;
            }
            int nextCard = JsonMini.Int(state, "nextCard");
            int guard = 0;
            while (preview.Count < 3 && guard++ < 24)
            {
                var card = cards[nextCard % cards.Count] as Dictionary<string, object>;
                nextCard++;
                if (card == null)
                {
                    continue;
                }
                var pick = Draw(user, card, recent, analysisJson);
                if (pick == null)
                {
                    Log("卡池已空：" + Label(JsonMini.Str(card, "pool")));
                    continue;
                }
                preview.Add(pick);
                recent.Add(JsonMini.Int(pick, "internalId") + ":" + JsonMini.Int(pick, "chartid"));
                while (recent.Count > 12)
                {
                    recent.RemoveAt(0);
                }
            }
            state["nextCard"] = nextCard;
        }

        /// <summary>抽一首（近期去重、优先级加权都在 Pools.Draw 里）；返回预览条目</summary>
        private static Dictionary<string, object> Draw(string user, Dictionary<string, object> card,
                                                       List<object> recent, string analysisJson)
        {
            var pick = Pools.Draw(user, card, recent, analysisJson);
            if (pick == null)
            {
                return null;
            }
            // 补上 UI 要的展示字段（Pools.Draw 只给谱面 key 与池子）
            var iid = JsonMini.Int(pick, "internalId");
            var cid = JsonMini.Int(pick, "chartid");
            var song = WebApi.FindSong(iid);
            if (song != null)
            {
                pick["title"] = song.Title ?? "";
                pick["dataid"] = song.DataId;
                pick["level"] = GameSnapshot.LevelTextOf(0, 0) == "" ? "" : LevelText(song.LevelText, cid);
            }
            pick["cardIndex"] = Cards(_scratchState).IndexOf(card);
            return pick;
        }

        private static string LevelText(string[] texts, int cid)
        {
            return texts != null && cid >= 0 && cid < texts.Length ? (texts[cid] ?? "") : "";
        }

        private static readonly Random _rng = new Random();

        /// <summary>Draw 里要算 cardIndex，借当前 state 定位；FillPreview 前会设。</summary>
        private static Dictionary<string, object> _scratchState;

        /// <summary>卡片字典 → PoolSpec</summary>
        private static PoolSpec SpecOf(Dictionary<string, object> card)
        {
            var spec = new PoolSpec();
            spec.Pool = JsonMini.Str(card, "pool");
            var settings = JsonMini.Obj(card, "settings");
            if (settings != null)
            {
                spec.LevelMin = NullableDbl(settings, "levelMin");
                spec.LevelMax = NullableDbl(settings, "levelMax");
                spec.AchMax = NullableDbl(settings, "achMax");
                spec.ComboMax = NullableInt(settings, "comboMax");
                spec.SyncMax = NullableInt(settings, "syncMax");
                spec.ExcludeUnplayed = JsonMini.Bool(settings, "excludeUnplayed");
                spec.Version = JsonMini.Str(settings, "version");
                spec.PlateId = JsonMini.Str(settings, "plateId");
                var diffs = JsonMini.Arr(settings, "difficulties");
                if (diffs != null)
                {
                    foreach (var d in diffs)
                    {
                        spec.Difficulties.Add(Convert.ToString(d, CultureInfo.InvariantCulture));
                    }
                }
            }
            return spec;
        }

        private static double? NullableDbl(Dictionary<string, object> map, string key)
        {
            var v = JsonMini.Get(map, key);
            if (v == null || string.IsNullOrEmpty(Convert.ToString(v, CultureInfo.InvariantCulture)))
            {
                return null;
            }
            return JsonMini.Dbl(map, key);
        }

        private static int? NullableInt(Dictionary<string, object> map, string key)
        {
            var v = JsonMini.Get(map, key);
            if (v == null || string.IsNullOrEmpty(Convert.ToString(v, CultureInfo.InvariantCulture)))
            {
                return null;
            }
            return JsonMini.Int(map, key);
        }

        // ── 推歌 ──
        internal static bool PushNext(string user)
        {
            string result;
            return PushNext(user, out result);
        }

        private static bool PushNext(string user, out string result)
        {
            result = "预览队列为空，请先配置有候选谱面的卡池";
            var state = LoadState(user);
            var preview = Preview(state);
            if (preview.Count == 0)
            {
                return false;
            }
            var head = preview[0] as Dictionary<string, object>;
            if (head == null)
            {
                return false;
            }
            var iid = JsonMini.Int(head, "internalId");
            var cid = JsonMini.Int(head, "chartid");
            result = SelectDriver.Enqueue(iid, cid, -1) ?? "点歌超时，请确认游戏正常运行并回到选曲界面后重试";
            Log("推送 " + JsonMini.Str(head, "title") + " [" + WebApi.DiffName(cid) + "] → " + (result ?? "?"));
            return result.StartsWith("已跳转:", StringComparison.Ordinal);
        }

        /// <summary>收到成绩：匹配当前预览头则顺延并推下一首</summary>
        internal static void OnScore(string user, int internalId, int chartId)
        {
            if (!_running || _mode != "push")
            {
                return;
            }
            var state = LoadState(user);
            var preview = Preview(state);
            if (preview.Count == 0)
            {
                return;
            }
            var head = preview[0] as Dictionary<string, object>;
            if (head == null)
            {
                return;
            }
            if (JsonMini.Int(head, "internalId") != internalId || JsonMini.Int(head, "chartid") != chartId)
            {
                Log("成绩不匹配当前预览（" + internalId + ":" + chartId + "），不顺延");
                return;
            }
            preview.RemoveAt(0);
            var analysis = Analysis.Json(user, true).json;
            FillPreview(user, state, analysis);
            SaveState(user, state);
            PreviewJson(user, state);
            if (!PushNext(user))
            {
                Log("预览空了，助手停下");
                _running = false;
            }
        }

        // ── 对外载荷 ──
        internal static string HeadJson(string user)
        {
            var state = LoadState(user);
            var preview = Preview(state);
            if (preview.Count == 0)
            {
                return null;
            }
            var p = preview[0] as Dictionary<string, object>;
            if (p == null)
            {
                return null;
            }
            return "{\"internalId\":" + JsonMini.Int(p, "internalId")
                + ",\"chartid\":" + JsonMini.Int(p, "chartid")
                + ",\"difficulty\":\"" + WebApi.DiffName(JsonMini.Int(p, "chartid")) + "\""
                + ",\"title\":\"" + JsonMini.Esc(JsonMini.Str(p, "title")) + "\""
                + ",\"pool\":\"" + JsonMini.Esc(JsonMini.Str(p, "pool")) + "\""
                + ",\"poolLabel\":\"" + JsonMini.Esc(Label(JsonMini.Str(p, "pool"))) + "\"}";
        }

        internal static string PreviewJson(string user, Dictionary<string, object> state)
        {
            var sb = new StringBuilder();
            sb.Append("{\"ok\":true");
            sb.Append(",\"running\":").Append(_running ? "true" : "false");
            sb.Append(",\"mode\":\"").Append(JsonMini.Esc(_mode)).Append('"');
            sb.Append(",\"log\":\"").Append(JsonMini.Esc(LogTail())).Append('"');
            sb.Append(",\"profile\":");
            AppendProfile(sb, user);
            sb.Append(",\"versions\":[");
            bool firstVersion = true;
            foreach (var version in Versions.Order)
            {
                if (!firstVersion) sb.Append(',');
                firstVersion = false;
                sb.Append('"').Append(JsonMini.Esc(version)).Append('"');
            }
            sb.Append(']');

            sb.Append(",\"cards\":[");
            var cards = Cards(state);
            for (int i = 0; i < cards.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                var card = cards[i] as Dictionary<string, object>;
                var pool = JsonMini.Str(card, "pool");
                sb.Append("{\"pool\":\"").Append(JsonMini.Esc(pool)).Append('"')
                  .Append(",\"settings\":");
                DumpValue(sb, JsonMini.Obj(card, "settings") ?? new Dictionary<string, object>());
                sb
                  .Append(",\"poolLabel\":\"").Append(JsonMini.Esc(Label(pool))).Append('"')
                  .Append(",\"count\":").Append(Pools.Count(user, card)).Append('}');
            }
            sb.Append("],\"preview\":[");
            var preview = Preview(state);
            for (int i = 0; i < preview.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                var p = preview[i] as Dictionary<string, object>;
                sb.Append("{\"internalId\":").Append(JsonMini.Int(p, "internalId"))
                  .Append(",\"chartid\":").Append(JsonMini.Int(p, "chartid"))
                  .Append(",\"difficulty\":\"").Append(WebApi.DiffName(JsonMini.Int(p, "chartid"))).Append('"')
                  .Append(",\"title\":\"").Append(JsonMini.Esc(JsonMini.Str(p, "title"))).Append('"')
                  .Append(",\"level\":\"").Append(JsonMini.Esc(JsonMini.Str(p, "level"))).Append('"')
                  .Append(",\"dataid\":").Append(JsonMini.Int(p, "dataid"))
                  .Append(",\"pool\":\"").Append(JsonMini.Esc(JsonMini.Str(p, "pool"))).Append('"')
                  .Append(",\"poolLabel\":\"").Append(JsonMini.Esc(Label(JsonMini.Str(p, "pool")))).Append('"')
                  .Append(",\"cardIndex\":").Append(JsonMini.Int(p, "cardIndex"));
                sb.Append(",\"song\":");
                WebApi.AppendAssistantSong(sb, JsonMini.Int(p, "internalId"), JsonMini.Int(p, "chartid"), user);
                sb.Append('}');
            }
            sb.Append("],\"settings\":");
            DumpValue(sb, LoadSettings(user));
            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendProfile(StringBuilder sb, string user)
        {
            int index;
            var player = int.TryParse(user, out index) ? PlayerStore.Get(index) : null;
            var analysis = Analysis.Snapshot(user);
            sb.Append("{\"rating\":").Append(player != null && player.IsEntry
                ? player.Rating.ToString(CultureInfo.InvariantCulture) : "null");
            sb.Append(",\"config\":");
            AppendAxes(sb, analysis?.TagUPattern);
            sb.Append(",\"eval\":");
            AppendAxes(sb, analysis?.TagUEval);
            sb.Append('}');
        }

        private static void AppendAxes(StringBuilder sb, Dictionary<string, double> axes)
        {
            sb.Append('[');
            bool first = true;
            if (axes != null) foreach (var axis in axes)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"tag\":\"").Append(JsonMini.Esc(axis.Key)).Append("\",\"value\":")
                  .Append(axis.Value.ToString("0.####", CultureInfo.InvariantCulture)).Append('}');
            }
            sb.Append(']');
        }

        internal static string Repush(string user)
        {
            if (!_running || _mode != "push" || user != _activeUser)
                return "{\"ok\":false,\"error\":\"请先启动当前玩家的推歌助手\"}";
            string result;
            bool ok = PushNext(user, out result);
            return "{\"ok\":" + (ok ? "true" : "false") + ",\"info\":\"" + JsonMini.Esc(result) + "\"}";
        }

        // ── 路由动作 ──
        internal static string Handle(string user, string action, Dictionary<string, object> body)
        {
            var state = LoadState(user);
            switch (action)
            {
                case "state":
                {
                    var analysis = Analysis.Json(user, false).json;
                    if (JsonMini.Arr(state, "preview") == null || Preview(state).Count == 0)
                    {
                        if (Cards(state).Count > 0)
                        {
                            FillPreview(user, state, analysis);
                            SaveState(user, state);
                        }
                    }
                    return PreviewJson(user, state);
                }
                case "cards":
                {
                    var cards = JsonMini.Arr(body, "cards");
                    if (cards != null && cards.Count <= 12)
                    {
                        state["cards"] = cards;
                        state["preview"] = new List<object>();
                        state["nextCard"] = 0;
                        FillPreview(user, state, Analysis.Json(user, false).json);
                        SaveState(user, state);
                        return PreviewJson(user, state);
                    }
                    return "{\"ok\":false,\"error\":\"cards 必须是 <=12 的数组\"}";
                }
                case "settings":
                    SaveSettings(user, body);
                    return PreviewJson(user, state);
                case "log":
                    return PreviewJson(user, state);
                default:
                    return null;
            }
        }

        internal static string Start(string user, string mode, string analysisJson)
        {
            if (_running)
            {
                return StartError("助手已在运行，请先停止助手");
            }
            if (mode != "push" && mode != "score")
            {
                return StartError("mode 只能是 push 或 score");
            }
            var state = LoadState(user);
            if (mode == "push")
            {
                if (Cards(state).Count == 0)
                {
                    return StartError("至少配置 1 张卡才能启动");
                }
                FillPreview(user, state, analysisJson);
                SaveState(user, state);
                if (Preview(state).Count == 0)
                {
                    return StartError("所有卡池都是空的，请调整卡池筛选条件");
                }
                string result;
                if (!PushNext(user, out result)) return StartError(result);
            }
            _activeUser = user;
            _mode = mode;
            _running = true;
            Log(mode == "push" ? "→ 启动（推歌模式）" : "→ 启动（只接成绩）");
            return "{\"ok\":true,\"running\":true,\"mode\":\"" + mode + "\",\"info\":\"已启动\"}";
        }

        private static string StartError(string message)
        {
            Log("启动失败：" + message);
            return "{\"ok\":false,\"info\":\"" + JsonMini.Esc(message) + "\"}";
        }

        internal static string Stop()
        {
            if (!_running)
            {
                return "{\"ok\":false,\"info\":\"没有在运行\"}";
            }
            _running = false;
            Log("→ 已停止");
            return "{\"ok\":true,\"running\":false,\"info\":\"已停止\"}";
        }

        internal static string Redraw(string user, int index, bool force)
        {
            var state = LoadState(user);
            var preview = Preview(state);
            if (index < 0 || index >= preview.Count)
            {
                return "{\"ok\":false,\"error\":\"预览位置不存在\"}";
            }
            var cards = Cards(state);
            if (cards.Count == 0)
            {
                return "{\"ok\":false,\"error\":\"没有卡片\"}";
            }
            var entry = preview[index] as Dictionary<string, object>;
            var cardIndex = JsonMini.Int(entry, "cardIndex") % cards.Count;
            var card = cards[cardIndex] as Dictionary<string, object>;
            var recent = JsonMini.Arr(state, "recent") ?? new List<object>();
            var pick = Draw(user, card, recent, Analysis.Json(user, false).json);
            if (pick != null)
            {
                pick["cardIndex"] = cardIndex;
                preview[index] = pick;
                recent.Add(JsonMini.Int(pick, "internalId") + ":" + JsonMini.Int(pick, "chartid"));
                SaveState(user, state);
                return PreviewJson(user, state);
            }
            if (!force)
            {
                return "{\"ok\":true,\"empty\":true,\"emptyLabel\":\""
                    + JsonMini.Esc(Label(JsonMini.Str(card, "pool"))) + "\"}";
            }
            preview.RemoveAt(index);
            SaveState(user, state);
            return PreviewJson(user, state);
        }

        internal static string Skip(string user, int index)
        {
            var state = LoadState(user);
            var preview = Preview(state);
            if (index < 0 || index >= preview.Count)
            {
                return "{\"ok\":false,\"error\":\"预览位置不存在\"}";
            }
            preview.RemoveAt(index);
            FillPreview(user, state, Analysis.Json(user, false).json);
            SaveState(user, state);
            return PreviewJson(user, state);
        }
    }
}
