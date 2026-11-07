// 用途：/api/assistant/* 路由。动作分派到 Assistant。
// 状态：现行。
using System;
using System.Collections.Generic;
using System;
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
                    var analysis = Analysis.Json(user, true).json;
                    var result = Assistant.Start(user, QS(q, "mode"), analysis);
                    Json(ctx, result.Contains("\"ok\":true") ? 200 : 409, result);
                    return;
                }
                case "stop":
                    Json(ctx, 200, Assistant.Stop());
                    return;
                case "push":
                {
                    var iid = QInt(q, "internalId", 0);
                    var cid = QInt(q, "chartid", -1);
                    if (iid <= 0 || cid < 0)
                    {
                        Json(ctx, 400, "{\"ok\":false,\"error\":\"internalId/chartid 不对\"}");
                        return;
                    }
                    var result = SelectDriver.Enqueue(iid, cid, -1);
                    Json(ctx, 200, "{\"ok\":true,\"info\":\"" + JsonMini.Esc(result ?? "") + "\"}");
                    return;
                }
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
