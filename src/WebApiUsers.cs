// 用途：玩家列表、用户增删改、成绩统计/rating/改分、收藏、黑名单。
// 状态：现行。数据源是机台登录玩家 + UserFiles 落盘。
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
        private static void Users(HttpListenerContext ctx, Dictionary<string, object> q)
        {
            var sb = new StringBuilder(512);
            sb.Append("{\"ok\":true,\"users\":[");
            bool first = true;
            foreach (var p in PlayerStore.All)
            {
                if (!p.IsEntry) continue;
                if (!first)
                {
                    sb.Append(',');
                }
                first = false;
                var id = p.Index.ToString(CultureInfo.InvariantCulture);
                sb.Append("{\"id\":\"").Append(id).Append('"');
                sb.Append(",\"userIndex\":").Append(p.Index);
                sb.Append(",\"name\":\"").Append(JsonMini.Esc(p.Name.Length > 0 ? p.Name : ("玩家 " + (p.Index + 1)))).Append('"');
                sb.Append(",\"rating\":").Append(p.Rating);
                sb.Append(",\"isEntry\":").Append(p.IsEntry ? "true" : "false");
                sb.Append(",\"fromGame\":true");
                sb.Append(",\"hasScores\":").Append(HasAnyScore(p) ? "true" : "false");
                var avatar = File.Exists(Path.Combine(UserFiles.DirOf(id), "avatar.png"));
                sb.Append(",\"avatar\":").Append(avatar ? "\"avatar.png\"" : "null");
                sb.Append(",\"summary\":null,\"updatedAt\":null}");
            }
            sb.Append("]}");
            Json(ctx, 200, sb.ToString());
        }

        /// <summary>该玩家是不是真的打过曲库里的谱（只看曲库里存在的谱，别把打了别处的算进来）</summary>
        private static bool HasAnyScore(PlayerStore.Player p)
        {
            foreach (var kv in p.Scores)
            {
                if (kv.Value.Count > 0 && GameSnapshot.Find(kv.Key) != null)
                {
                    return true;
                }
            }
            return false;
        }

        private static void UserRoute(HttpListenerContext ctx, Dictionary<string, object> q)
        {
            // 机台玩家不能增删，改名/排序只落到本地偏好；这里给出稳定回应，前端不会卡住
            var action = ctx.Request.Url == null ? "" : ctx.Request.Url.AbsolutePath;
            if (action.EndsWith("/user-create", StringComparison.Ordinal))
            {
                Json(ctx, 409, "{\"created\":false,\"info\":\"机台玩家由游戏登录产生，先进账号即可\"}");
                return;
            }
            if (action.EndsWith("/user-delete", StringComparison.Ordinal))
            {
                Json(ctx, 409, "{\"ok\":false,\"info\":\"机台玩家由游戏登录产生，没法在这里删\"}");
                return;
            }
            var user = QS(q, "user");
            var name = QS(q, "name");
            Json(ctx, 200, "{\"ok\":true,\"info\":\"已保存\",\"user\":{\"id\":\"" + JsonMini.Esc(user)
                + "\",\"name\":\"" + JsonMini.Esc(name) + "\"}}");
        }

        /// <summary>mod 里没有系统文件对话框，选中路径由前端自己填；这里只做确认回显。</summary>
        private static void PickRoute(HttpListenerContext ctx, Dictionary<string, object> q)
        {
            var path = QS(q, "path");
            Json(ctx, 200, "{\"path\":\"" + JsonMini.Esc(path)
                + "\",\"note\":\"mod 版直接用游戏自己的曲库，不需要选目录\",\"warning\":null}");
        }

        private static void ScoresRoute(HttpListenerContext ctx, string method, string path)
        {
            var q = method == "POST" ? Body(ctx) : Query(ctx);
            BeginRequest(q);
            var user = UserIndex(q);
            if (path.EndsWith("/stats", StringComparison.Ordinal))
            {
                string payload;
                string err;
                Stats.Json(user.ToString(CultureInfo.InvariantCulture), out payload, out err);
                if (string.IsNullOrEmpty(payload))
                {
                    Json(ctx, 409, "{\"ok\":false,\"info\":\"" + JsonMini.Esc(err ?? "还没有成绩") + "\"}");
                    return;
                }
                Json(ctx, 200, "{\"ok\":true,\"stats\":" + payload + "}");
                return;
            }
            var version = QS(q, "version");
            if (version.Length == 0)
            {
                version = Versions.DefaultRatingVersion;
            }
            string rating;
            string ratingErr;
            DxRating.Json(user.ToString(CultureInfo.InvariantCulture), version, out rating, out ratingErr);
            if (string.IsNullOrEmpty(rating))
            {
                Json(ctx, 409, "{\"ok\":false,\"info\":\"" + JsonMini.Esc(ratingErr ?? "算不出来") + "\"}");
                return;
            }
            Json(ctx, 200, rating);
        }

        private static void ScoreEdit(HttpListenerContext ctx, Dictionary<string, object> q)
        {
            var user = UserIndex(q);
            var player = PlayerStore.Get(user);
            var iid = QInt(q, "internalId", 0);
            var cid = QInt(q, "chartid", -1);
            if (player == null || iid <= 0 || cid < 0 || cid > 4)
            {
                Json(ctx, 400, "{\"ok\":false,\"error\":\"参数不完整\"}");
                return;
            }
            // 达成率必填；其余缺省时沿用当前值（改分不该把连击/游玩次数冲掉）
            var ach = QInt(q, "achievement", 0);
            if (ach <= 0)
            {
                Json(ctx, 400, "{\"ok\":false,\"error\":\"achievement 要是 ×10000 的整数\"}");
                return;
            }
            var old = PlayerStore.ScoreOf(player, iid, cid);
            var combo = q.ContainsKey("comboStatus") ? QInt(q, "comboStatus", 0)
                : q.ContainsKey("combo") ? QInt(q, "combo", 0)
                : (old == null ? 0 : old.Combo);
            var sync = q.ContainsKey("syncStatus") ? QInt(q, "syncStatus", 0)
                : q.ContainsKey("sync") ? QInt(q, "sync", 0)
                : (old == null ? 0 : old.Sync);
            var playCount = q.ContainsKey("playCount") ? QInt(q, "playCount", 0)
                : (old == null ? 0 : (int)old.PlayCount);
            var rank = q.ContainsKey("rank") ? QInt(q, "rank", 0) : RankOf(ach);
            // deluxscoreMax 只影响 DX 分显示，落盘保留
            var saved = PlayerStore.Override(user, iid, cid, (uint)ach, rank, combo, sync, (uint)playCount,
                q.ContainsKey("deluxscoreMax") ? QInt(q, "deluxscoreMax", 0) : 0);
            UserFiles.WriteText(user.ToString(CultureInfo.InvariantCulture), "overrides.json", PlayerStore.OverridesJson(user));
            Json(ctx, 200, "{\"ok\":true,\"score\":{\"internalId\":" + iid + ",\"chartid\":" + cid
                + ",\"achievement\":" + saved.Achievement + ",\"rank\":" + saved.Rank + "}}");
        }

        private static int RankOf(int achievement)
        {
            var cuts = new[] { 1005000, 1000000, 995000, 990000, 980000, 970000, 940000, 900000,
                               800000, 750000, 700000, 600000, 500000 };
            for (int i = 0; i < cuts.Length; i++)
            {
                if (achievement >= cuts[i])
                {
                    return 13 - i;
                }
            }
            return 0;
        }

        private static void Favorites(HttpListenerContext ctx, Dictionary<string, object> q)
        {
            var user = QUser;
            var sb = new StringBuilder(256);
            sb.Append("{\"ok\":true,\"entries\":[");
            bool first = true;
            foreach (var key in UserFiles.Favorites(user))
            {
                var bits = key.Split(':');
                if (bits.Length != 2)
                {
                    continue;
                }
                if (!first)
                {
                    sb.Append(',');
                }
                first = false;
                sb.Append("{\"internalId\":").Append(bits[0]).Append(",\"chartid\":").Append(bits[1]).Append('}');
            }
            sb.Append("]}");
            Json(ctx, 200, sb.ToString());
        }

        private static void FavoriteRoute(HttpListenerContext ctx, Dictionary<string, object> q)
        {
            var add = ctx.Request.Url != null
                && !ctx.Request.Url.AbsolutePath.EndsWith("/remove", StringComparison.Ordinal);
            var iid = QInt(q, "internalId", 0);
            var cid = QInt(q, "chartid", -1);
            if (iid <= 0 || cid < 0)
            {
                Json(ctx, 400, "{\"ok\":false,\"error\":\"缺 internalId / chartid\"}");
                return;
            }
            UserFiles.ToggleFavorite(QUser, iid, cid, add);
            Json(ctx, 200, "{\"ok\":true,\"favorite\":" + (add ? "true" : "false") + "}");
        }

        private static void BlacklistRoute(HttpListenerContext ctx, string method)
        {
            if (method == "GET")
            {
                var map = UserFiles.Blacklist(QUser);
                var sb = new StringBuilder(256);
                sb.Append("{\"ok\":true,\"entries\":[");
                bool first = true;
                foreach (var kv in map)
                {
                    var bits = kv.Key.Split(':');
                    if (bits.Length != 2)
                    {
                        continue;
                    }
                    if (!first)
                    {
                        sb.Append(',');
                    }
                    first = false;
                    sb.Append("{\"internalId\":").Append(bits[0]).Append(",\"chartid\":").Append(bits[1])
                      .Append(",\"until\":").Append(kv.Value).Append('}');
                }
                sb.Append("]}");
                Json(ctx, 200, sb.ToString());
                return;
            }
            var q = Body(ctx);
            BeginRequest(q);
            var iid = QInt(q, "internalId", 0);
            var cid = QInt(q, "chartid", -1);
            if (iid <= 0 || cid < 0)
            {
                Json(ctx, 400, "{\"ok\":false,\"error\":\"缺 internalId / chartid\"}");
                return;
            }
            var dur = QS(q, "duration");
            if (dur.Length == 0)
            {
                dur = "forever";
            }
            UserFiles.SetBlacklist(QUser, iid, cid, dur, false);
            Json(ctx, 200, "{\"ok\":true,\"entry\":{\"internalId\":" + iid + ",\"chartid\":" + cid
                + ",\"duration\":\"" + JsonMini.Esc(dur) + "\"}}");
        }

        private static void BlacklistRemove(HttpListenerContext ctx, Dictionary<string, object> q)
        {
            var iid = QInt(q, "internalId", 0);
            var cid = QInt(q, "chartid", -1);
            UserFiles.SetBlacklist(QUser, iid, cid, "", true);
            Json(ctx, 200, "{\"ok\":true,\"removed\":true}");
        }

        private static void AnalysisRoute(HttpListenerContext ctx, string method)
        {
            var q = method == "POST" ? Body(ctx) : Query(ctx);
            BeginRequest(q);
            var force = QS(q, "run") == "1" || method == "POST";
            var json = Analysis.Json(UserIndex(q).ToString(CultureInfo.InvariantCulture), force).json;
            if (string.IsNullOrEmpty(json))
            {
                Json(ctx, 404, "{\"ok\":false,\"error\":\"还没有分析结果\"}");
                return;
            }
            Json(ctx, 200, json);
        }
    }
}
