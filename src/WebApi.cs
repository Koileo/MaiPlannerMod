// 用途：所有 /api/* 的实现。HTTP 后台线程调用，要碰游戏数据一律先过快照。
// 状态：现行。路由表在 Handle 里；业务实现按领域分 partial 文件。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace MaiPlanner
{
    internal static partial class WebApi
    {
        internal static void Handle(HttpListenerContext ctx, string path, string method)
        {
            // ── 兼容旧接口路径 ──
            if (path == "/status")
            {
                path = "/api/status";
            }
            else if (path == "/probe")
            {
                path = "/api/probe";
            }
            else if (path == "/jump")
            {
                path = "/api/jump";
            }
            else if (path == "/callback")
            {
                path = "/api/callback";
            }

            switch (path)
            {
                case "/api/status": Get(ctx, Status); return;
                case "/api/probe": Get(ctx, Probe); return;
                case "/api/diag": Get(ctx, Diag); return;
                case "/api/config": ConfigRoute(ctx, method); return;
                case "/api/songs": Get(ctx, Songs); return;
                case "/api/song": Get(ctx, SongDetail); return;
                case "/api/plates": Get(ctx, delegate (HttpListenerContext c, Dictionary<string, object> q)
                {
                    Json(c, 200, "{\"plates\":[]}");
                }); return;
                case "/api/users": Get(ctx, Users); return;
                case "/api/user-create":
                case "/api/user-rename":
                case "/api/user-delete":
                case "/api/user-order": Post(ctx, UserRoute); return;
                case "/api/pick-folder":
                case "/api/pick-file": Post(ctx, PickRoute); return;
                case "/api/scores/stats":
                case "/api/scores/dxrating": ScoresRoute(ctx, method, path); return;
                case "/api/score/edit": Post(ctx, ScoreEdit); return;
                case "/api/favorites": Get(ctx, Favorites); return;
                case "/api/favorite":
                case "/api/favorite/remove": Post(ctx, FavoriteRoute); return;
                case "/api/blacklist": BlacklistRoute(ctx, method); return;
                case "/api/blacklist/remove": Post(ctx, BlacklistRemove); return;
                case "/api/analysis": AnalysisRoute(ctx, method); return;
                case "/api/jump": Post(ctx, Jump); return;
                case "/api/callback": Post(ctx, Callback); return;
                case "/api/run": Get(ctx, RunState); return;
                case "/api/build":
                case "/api/stop": Post(ctx, BuildOrStop); return;
                case "/api/avatar": Get(ctx, Avatar); return;
                case "/api/user-avatar": Post(ctx, AvatarPick); return;
                case "/api/user-avatar-save": Post(ctx, AvatarSave); return;
            }

            if (path.StartsWith("/api/assistant/"))
            {
                AssistantRoute(ctx, method, path.Substring("/api/assistant/".Length));
                return;
            }
            if (path.StartsWith("/api/"))
            {
                Json(ctx, 404, "{\"ok\":false,\"error\":\"没有这个接口\"}");
                return;
            }
            Json(ctx, 404, "{\"ok\":false,\"error\":\"没有这个路径\"}");
        }

        // ── 小工具 ──
        private static void Get(HttpListenerContext ctx, Action<HttpListenerContext, Dictionary<string, object>> fn)
        {
            if (ctx.Request.HttpMethod != "GET")
            {
                Json(ctx, 405, "{\"ok\":false,\"error\":\"用 GET\"}");
                return;
            }
            var q = Query(ctx);
            BeginRequest(q);
            fn(ctx, q);
        }

        private static void Post(HttpListenerContext ctx, Action<HttpListenerContext, Dictionary<string, object>> fn)
        {
            if (ctx.Request.HttpMethod != "POST")
            {
                Json(ctx, 405, "{\"ok\":false,\"error\":\"用 POST\"}");
                return;
            }
            var q = Body(ctx);
            BeginRequest(q);
            fn(ctx, q);
        }

        /// <summary>每个请求开头记下用户，供曲库拼装时取收藏/黑名单</summary>
        private static void BeginRequest(Dictionary<string, object> q)
        {
            QUser = UserIndex(q).ToString(CultureInfo.InvariantCulture);
        }

        private static Dictionary<string, object> Query(HttpListenerContext ctx)
        {
            return ParsePairs(ctx.Request.Url == null ? "" : ctx.Request.Url.Query, '&');
        }

        /// <summary>请求体：JSON 或 urlencoded 都认（老客户端发的是表单）。</summary>
        private static Dictionary<string, object> Body(HttpListenerContext ctx)
        {
            string raw;
            try
            {
                var enc = ctx.Request.ContentEncoding ?? Encoding.UTF8;
                using (var r = new StreamReader(ctx.Request.InputStream, enc))
                {
                    raw = r.ReadToEnd();
                }
            }
            catch
            {
                return new Dictionary<string, object>();
            }
            if (string.IsNullOrWhiteSpace(raw))
            {
                return new Dictionary<string, object>();
            }
            var trimmed = raw.TrimStart();
            if (trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                var doc = JsonMini.ParseObj(raw);
                return doc ?? new Dictionary<string, object>();
            }
            return ParsePairs(raw, '&');
        }

        private static Dictionary<string, object> ParsePairs(string raw, char sep)
        {
            var map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(raw))
            {
                return map;
            }
            foreach (var pair in raw.TrimStart('?').Split(sep))
            {
                if (pair.Length == 0)
                {
                    continue;
                }
                var eq = pair.IndexOf('=');
                var k = eq < 0 ? pair : pair.Substring(0, eq);
                var v = eq < 0 ? "" : pair.Substring(eq + 1);
                map[WebUtility.UrlDecode(k)] = WebUtility.UrlDecode(v);
            }
            return map;
        }

        internal static void Json(HttpListenerContext ctx, int status, string json)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(json ?? "{}");
                ctx.Response.StatusCode = status;
                ctx.Response.ContentType = "application/json; charset=utf-8";
                ctx.Response.Headers["Cache-Control"] = "no-store";
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            }
            catch { }
            finally
            {
                try { ctx.Response.Close(); } catch { }
            }
        }

        private static string Q(Dictionary<string, object> q, string key)
        {
            object v;
            return q != null && q.TryGetValue(key, out v) ? Convert.ToString(v) : "";
        }

        private static int QInt(Dictionary<string, object> q, string key, int fallback)
        {
            int n;
            return int.TryParse(Q(q, key), out n) ? n : fallback;
        }

        private static string QS(Dictionary<string, object> q, string key)
        {
            return (Q(q, key) ?? "").Trim();
        }

        /// <summary>玩家索引（0..3）；缺省时用第一个登录的</summary>
        private static int UserIndex(Dictionary<string, object> q)
        {
            var raw = QS(q, "user");
            if (raw.Length > 0)
            {
                int n;
                if (int.TryParse(raw, out n) && n >= 0 && n < 4)
                {
                    return n;
                }
            }
            foreach (var p in PlayerStore.All)
            {
                if (p.IsEntry)
                {
                    return p.Index;
                }
            }
            return 0;
        }
    }
}
