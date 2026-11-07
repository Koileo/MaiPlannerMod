// 用途：/api/assistant/* 路由。动作分派到 Assistant。
// 状态：现行。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;

namespace MaiPlanner
{
    internal static partial class WebApi
    {
        private static void AssistantRoute(HttpListenerContext ctx, string method, string action)
        {
            var q = method == "POST" ? Body(ctx) : Query(ctx);
            BeginRequest(q);
            var user = UserIndex(q).ToString(CultureInfo.InvariantCulture);

            switch (action)
            {
                case "state":
                    if (method != "GET")
                    {
                        Json(ctx, 405, "{\"ok\":false,\"error\":\"用 GET\"}");
                        return;
                    }
                    Json(ctx, 200, Assistant.Handle(user, "state", q));
                    return;
                case "log":
                    Json(ctx, 200, Assistant.Handle(user, "log", q));
                    return;
                case "bridge-next":
                {
                    var head = Assistant.HeadJson(user);
                    Json(ctx, 200, "{\"ok\":true,\"next\":" + (head ?? "null") + "}");
                    return;
                }
                default:
                    if (method != "POST")
                    {
                        Json(ctx, 405, "{\"ok\":false,\"error\":\"用 POST\"}");
                        return;
                    }
                    break;
            }

            switch (action)
            {
                case "cards":
                case "settings":
                    Json(ctx, 200, Assistant.Handle(user, action, q));
                    return;
                case "redraw":
                case "skip":
                {
                    var index = QInt(q, "index", 0);
                    var body = action == "redraw"
                        ? Assistant.Redraw(user, index, JsonMini.Bool(q, "force"))
                        : Assistant.Skip(user, index);
                    Json(ctx, 200, body);
                    return;
                }
                case "start":
                {
                    var mode = QS(q, "mode");
                    // 先检查游戏状态，避免无法推歌时仍强制重算整份分析。
                    string error = !Mod.Ready ? "游戏数据还在加载，请稍后重试"
                        : PlayerStore.Get(int.Parse(user))?.IsEntry != true ? "请先在机台登录玩家"
                        : mode == "push" && !SelectDriver.InSelectCached ? "当前不在选曲界面，请回到选曲界面后启动助手"
                        : null;
                    if (error != null)
                    {
                        Assistant.Log("启动失败：" + error);
                        Json(ctx, 409, "{\"ok\":false,\"error\":\"" + JsonMini.Esc(error) + "\"}");
                        return;
                    }
                    var analysis = mode == "push" ? Analysis.Json(user, false).json : null;
                    var result = Assistant.Start(user, mode, analysis);
                    Json(ctx, result.Contains("\"ok\":true") ? 200 : 409, result);
                    return;
                }
                case "stop":
                    Json(ctx, 200, Assistant.Stop());
                    return;
                case "repush":
                {
                    var result = Assistant.Repush(user);
                    Json(ctx, result.Contains("\"ok\":true") ? 200 : 409, result);
                    return;
                }
                case "push":
                    Jump(ctx, q);
                    return;
                case "bridge-score":
                {
                    // 外部（含 ScoreReporter）推过来的原始成绩
                    var iid = QInt(q, "internalId", QInt(q, "musicId", 0));
                    var cid = QInt(q, "chartid", QInt(q, "difficulty", -1));
                    ApplyScore(user, q, iid, cid);
                    Json(ctx, 200, "{\"ok\":true,\"next\":" + (Assistant.HeadJson(user) ?? "null") + "}");
                    return;
                }
                case "refill":
                    Json(ctx, 200, Assistant.Handle(user, "state", q));
                    return;
                default:
                    Json(ctx, 404, "{\"ok\":false,\"error\":\"没有这个助手动作\"}");
                    return;
            }
        }

        /// <summary>把一局成绩并进覆盖表，然后交给助手顺延</summary>
        private static void ApplyScore(string user, Dictionary<string, object> q, int internalId, int chartId)
        {
            if (internalId <= 0 || chartId < 0)
            {
                return;
            }
            var ach = QInt(q, "achievement", QInt(q, "achive", 0));
            var raw = ach > 0 ? ach : (int)Math.Round(JsonMini.Dbl(q, "achive") * 10000);
            if (raw > 0)
            {
                var rank = QInt(q, "rank", 0);
                if (rank <= 0 && raw >= 500000)
                {
                    rank = 1;
                }
                PlayerStore.Override(int.Parse(user), internalId, chartId, (uint)raw, rank,
                    QInt(q, "combo", 0), QInt(q, "sync", 0), 1);
                UserFiles.WriteText(user, "overrides.json", PlayerStore.OverridesJson(int.Parse(user)));
            }
            Assistant.OnScore(user, internalId, chartId);
        }
    }
}
