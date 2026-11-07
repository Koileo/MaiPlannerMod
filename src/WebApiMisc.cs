// 用途：mod 版没有外部脚本链，这里给 /api/run、/api/build、/api/stop 与头像接口的落地实现。
//       建库/小更新/别名三块都返回"不需要"的明确提示，页面不会卡在等待。
// 状态：现行。头像是玩家自己上传的 PNG，落在 dataUser/<user>/avatar.png。
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
        /// <summary>运行状态：mod 里没有外部进程，永远是空闲的</summary>
        private static void RunState(HttpListenerContext ctx, Dictionary<string, object> q)
        {
            Json(ctx, 200, "{\"ok\":true,\"running\":false,\"steps\":[],\"log\":\"\","
                + "\"info\":\"mod 版直接用游戏自己的曲库，不需要建库\"}");
        }

        private static void BuildOrStop(HttpListenerContext ctx, Dictionary<string, object> q)
        {
            var stop = ctx.Request.Url != null
                && ctx.Request.Url.AbsolutePath.EndsWith("/stop", StringComparison.Ordinal);
            if (stop)
            {
                Json(ctx, 200, "{\"stopped\":false,\"info\":\"mod 版没有外部任务在跑\"}");
                return;
            }
            Json(ctx, 200, "{\"started\":true,\"info\":\"mod 版不需要建库，数据直接来自游戏\"}");
        }

        // ── 头像 ──
        private static string AvatarPath(string user)
        {
            return Path.Combine(UserFiles.DirOf(user), "avatar.png");
        }

        private static void Avatar(HttpListenerContext ctx, Dictionary<string, object> q)
        {
            var path = AvatarPath(UserIndex(q).ToString(CultureInfo.InvariantCulture));
            if (!File.Exists(path))
            {
                Json(ctx, 404, "{\"ok\":false,\"error\":\"这个玩家还没有头像\"}");
                return;
            }
            try
            {
                var bytes = File.ReadAllBytes(path);
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "image/png";
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

        /// <summary>决定上传了哪张图（mod 里没有系统对话框，改由前端传 data URL）</summary>
        private static void AvatarPick(HttpListenerContext ctx, Dictionary<string, object> q)
        {
            var user = UserIndex(q).ToString(CultureInfo.InvariantCulture);
            var dataUrl = QS(q, "image");
            if (dataUrl.Length == 0)
            {
                Json(ctx, 409, "{\"ok\":false,\"info\":\"mod 版没有文件对话框，直接把图片拖进页面即可\"}");
                return;
            }
            var saved = SaveAvatar(user, dataUrl);
            if (saved == null)
            {
                Json(ctx, 409, "{\"ok\":false,\"info\":\"图片读不出来（只支持 PNG/JPEG）\"}");
                return;
            }
            Json(ctx, 200, "{\"ok\":true,\"info\":\"已保存\",\"avatar\":\"avatar.png\"}");
        }

        private static void AvatarSave(HttpListenerContext ctx, Dictionary<string, object> q)
        {
            var user = UserIndex(q).ToString(CultureInfo.InvariantCulture);
            var saved = SaveAvatar(user, QS(q, "image"));
            if (saved == null)
            {
                Json(ctx, 409, "{\"ok\":false,\"info\":\"图片读不出来\"}");
                return;
            }
            Json(ctx, 200, "{\"ok\":true,\"info\":\"已保存\",\"avatar\":\"avatar.png\"}");
        }

        /// <summary>data:image/png;base64,... → 落盘</summary>
        private static string SaveAvatar(string user, string dataUrl)
        {
            if (string.IsNullOrEmpty(dataUrl))
            {
                return null;
            }
            try
            {
                var comma = dataUrl.IndexOf(',');
                if (comma < 0)
                {
                    return null;
                }
                var bytes = Convert.FromBase64String(dataUrl.Substring(comma + 1));
                if (bytes.Length == 0 || bytes.Length > 8 * 1024 * 1024)
                {
                    return null;
                }
                // 只认 PNG/JPEG 魔数
                bool png = bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50;
                bool jpg = bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8;
                if (!png && !jpg)
                {
                    return null;
                }
                var path = AvatarPath(user);
                var tmp = path + ".part";
                File.WriteAllBytes(tmp, bytes);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                File.Move(tmp, path);
                return path;
            }
            catch
            {
                return null;
            }
        }
    }
}
