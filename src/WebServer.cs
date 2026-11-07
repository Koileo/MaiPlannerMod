// 用途：全部网页/接口的 HTTP 宿主。一个端口，127.0.0.1 常驻，局域网按配置附加。
// 状态：现行。只做路由分发，业务实现分在 WebApi 的各个分部类里。
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace MaiPlanner
{
    internal static class WebServer
    {
        private static HttpListener _listener;
        private static volatile bool _running;
        private static readonly List<string> _prefixes = new List<string>();
        private static List<string> _lanIps = new List<string>();

        internal static bool Running { get { return _running; } }
        internal static int Port { get { return _prefixes.Count > 0 ? _prefixPort : 0; } }
        private static int _prefixPort;

        /// <summary>局域网可用的扫码地址（给状态接口和日志用）</summary>
        internal static List<string> ScanUrls()
        {
            var list = new List<string>();
            if (!_running || !Config.LanAccess)
            {
                return list;
            }
            foreach (var ip in _lanIps)
            {
                list.Add("http://" + ip + ":" + _prefixPort + "/scan");
            }
            return list;
        }

        internal static void Start(int port)
        {
            if (_running)
            {
                return;
            }
            try
            {
                _prefixPort = port;
                _prefixes.Clear();
                var l = new HttpListener();
                Add(l, "http://127.0.0.1:" + port + "/");
                if (Config.LanAccess)
                {
                    _lanIps = LanIps();
                    foreach (var ip in _lanIps)
                    {
                        Add(l, "http://" + ip + ":" + port + "/");
                    }
                }
                _listener = l;
                _listener.Start();
                _running = true;
                var th = new Thread(Loop) { IsBackground = true, Name = "MaiPlanner.Web" };
                th.Start();
                foreach (var u in _prefixes)
                {
                    ModLog.Always("[点歌] 网页: " + u.TrimEnd('/') + "/   ·   手机扫码: " + u.TrimEnd('/') + "/scan");
                }
                if (!Config.LanAccess)
                {
                    ModLog.Always("[点歌] 局域网关闭，手机扫码要打开配置里的「局域网」");
                }
            }
            catch (Exception e)
            {
                MelonLoader.MelonLogger.Error("启动网页失败(端口 " + port + "): " + e.Message);
            }
        }

        private static void Add(HttpListener l, string prefix)
        {
            _prefixes.Add(prefix);
            l.Prefixes.Add(prefix);
        }

        internal static void Stop()
        {
            _running = false;
            try
            {
                if (_listener != null)
                {
                    _listener.Stop();
                    _listener.Close();
                }
            }
            catch { }
        }

        /// <summary>
        /// 本机可用的 IPv4。先排出虚拟网卡；但代理/隧道网卡常常才是唯一能连的网口
        /// （Mihomo、Clash 之类会把真实出口改道），所以真实网卡一个都没有时退回去把虚拟网卡也算上。
        /// </summary>
        private static List<string> LanIps()
        {
            var real = CollectIps(false);
            return real.Count > 0 ? real : CollectIps(true);
        }

        private static List<string> CollectIps(bool includeVirtual)
        {
            var list = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up
                        || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    {
                        continue;
                    }
                    var name = (ni.Name + " " + ni.Description).ToLowerInvariant();
                    var isVirtual = name.Contains("vmware") || name.Contains("virtualbox") || name.Contains("hyper-v")
                        || name.Contains("vethernet") || name.Contains("zerotier") || name.Contains("radmin")
                        || name.Contains("tap-") || name.Contains("wsl") || name.Contains("docker")
                        || name.Contains("tailscale") || name.Contains("tunnel") || name.Contains("clash")
                        || name.Contains("mihomo");
                    if (!includeVirtual && isVirtual)
                    {
                        continue;
                    }
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork)
                        {
                            continue;
                        }
                        var s = ua.Address.ToString();
                        if (s.StartsWith("127.") || s.StartsWith("169.254.") || s.StartsWith("198.18.") || s.StartsWith("198.19."))
                        {
                            continue;
                        }
                        list.Add(s);
                    }
                }
            }
            catch { }
            return list;
        }

        private static void Loop()
        {
            while (_running)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = _listener.GetContext();
                }
                catch
                {
                    break;
                }
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        Dispatch(ctx);
                    }
                    catch (Exception e)
                    {
                        ModLog.WarnOnce("处理请求异常: " + e.Message);
                        try { ctx.Response.Abort(); } catch { }
                    }
                });
            }
        }

        private static void Dispatch(HttpListenerContext ctx)
        {
            var path = ctx.Request.Url == null ? "/" : ctx.Request.Url.AbsolutePath;
            var lower = path.ToLowerInvariant();

            try
            {
                // ── 页面 ──
                if (lower == "/" || lower == "/index.html")
                {
                    SendEmbedded(ctx, "index.html");
                    return;
                }
                if (lower == "/scan" || lower == "/scan/")
                {
                    SendEmbedded(ctx, "scan/index.html");
                    return;
                }

                // ── 接口 ──
                if (lower.StartsWith("/api/") || lower == "/probe" || lower == "/jump"
                    || lower == "/callback" || lower == "/status")
                {
                    WebApi.Handle(ctx, lower, ctx.Request.HttpMethod);
                    return;
                }
                if (lower == "/jacket")
                {
                    Jackets.Serve(ctx);
                    return;
                }

                // ── 静态 ──
                if (TryStatic(ctx, path))
                {
                    return;
                }

                WebApi.Json(ctx, 404, "{\"ok\":false,\"error\":\"没有这个路径\"}");
            }
            catch (Exception e)
            {
                ModLog.WarnOnce("请求失败 " + path + ": " + e.Message);
                try { WebApi.Json(ctx, 500, "{\"ok\":false,\"error\":\"服务器错误\"}"); } catch { }
            }
        }

        private static bool TryStatic(HttpListenerContext ctx, string path)
        {
            var rel = path.TrimStart('/');
            if (rel.Length == 0)
            {
                return false;
            }
            // 防目录穿越
            if (rel.Contains(".."))
            {
                return false;
            }
            var bytes = EmbeddedWeb.Read(rel);
            if (bytes == null)
            {
                return false;
            }
            var type = EmbeddedWeb.ContentTypeFor(rel);
            var strong = !rel.EndsWith(".html", StringComparison.OrdinalIgnoreCase);
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = type;
            ctx.Response.Headers["Cache-Control"] = strong ? "public, max-age=86400" : "no-store";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
            return true;
        }

        private static void SendEmbedded(HttpListenerContext ctx, string rel)
        {
            var bytes = EmbeddedWeb.Read(rel);
            if (bytes == null)
            {
                WebApi.Json(ctx, 500, "{\"ok\":false,\"error\":\"缺页面 " + rel + "\"}");
                return;
            }
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.Headers["Cache-Control"] = "no-store";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();
        }
    }
}
