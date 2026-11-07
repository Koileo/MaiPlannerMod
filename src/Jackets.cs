// 用途：曲绘。从游戏包的 jacket / jacket_s 资源包取图，解出来给网页用（带内存缓存）。
// 状态：现行。AssetBundle 只能在主线程加载，所以按需派到主线程并缓存 PNG 字节。
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace MaiPlanner
{
    internal static class Jackets
    {
        private static readonly Dictionary<int, byte[]> _cache = new Dictionary<int, byte[]>();
        private static readonly Dictionary<int, long> _missing = new Dictionary<int, long>();
        private static readonly Queue<int> _cacheOrder = new Queue<int>();
        private static readonly object _lock = new object();
        private const int MaxCache = 600;

        private static readonly Lazy<List<string>> _roots = new Lazy<List<string>>(FindRoots);

        /// <summary>
        /// 曲绘可能分布在多个版本目录（A000 / N100 …），全部收进来按名字从新到旧排，
        /// 新的优先。用 List 不用单值：早期只认 A000 会漏掉新版本的歌。
        /// </summary>
        private static List<string> Roots
        {
            get { return _roots.Value; }
        }

        private static List<string> FindRoots()
        {
            var roots = new List<string>();
            try
            {
                foreach (var root in new[]
                {
                    Environment.CurrentDirectory,
                    Path.GetDirectoryName(typeof(Jackets).Assembly.Location) ?? "",
                    AppDomain.CurrentDomain.BaseDirectory ?? "",
                })
                {
                    if (string.IsNullOrEmpty(root))
                    {
                        continue;
                    }
                    var sa = Path.Combine(root, "Sinmai_Data", "StreamingAssets");
                    if (!Directory.Exists(sa))
                    {
                        continue;
                    }
                    foreach (var dir in Directory.GetDirectories(sa))
                    {
                        foreach (var sub in new[] { "jacket_s", "jacket" })
                        {
                            var cand = Path.Combine(dir, "AssetBundleImages", sub);
                            if (Directory.Exists(cand) && !roots.Contains(cand))
                            {
                                roots.Add(cand);
                            }
                        }
                    }
                }
                // 版本目录名大致递增，倒序让新版本排在前面
                roots.Sort(StringComparer.OrdinalIgnoreCase);
                roots.Reverse();
                foreach (var r in roots)
                {
                    ModLog.Info("曲绘目录: " + r);
                }
                if (roots.Count == 0)
                {
                    ModLog.WarnOnce("找不到曲绘目录，基准=" + Environment.CurrentDirectory);
                }
            }
            catch (Exception e)
            {
                ModLog.WarnOnce("找曲绘目录失败: " + e.Message);
            }
            return roots;
        }

        /// <summary>第一个可用目录（给诊断显示用）</summary>
        private static string BundleRoot
        {
            get
            {
                var list = Roots;
                return list.Count > 0 ? list[0] : "";
            }
        }
        /// <summary>曲绘目录（自检用）</summary>
        internal static string RootPath { get { return BundleRoot; } }

        /// <summary>新版本优先；同一版本内先缩略图，再原图。</summary>
        private static IEnumerable<string> FindBundles(int dataId)
        {
            var name = "ui_jacket_" + dataId.ToString("D6");
            foreach (var root in Roots)
            {
                foreach (var variant in new[] { name + "_s.ab", name + ".ab" })
                {
                    var path = Path.Combine(root, variant);
                    if (File.Exists(path))
                    {
                        yield return path;
                    }
                }
            }
        }

        /// <summary>使用正式加载通路读取 146，返回 PNG 字节数与候选路径（不重复打开游戏已持有的包）。</summary>
        internal static string RealProbeJson()
        {
            try
            {
                var probe = MainThread.Run(delegate { return ProbeOnMain(); }, 4000, null);
                return probe ?? "{\"onMain\":false,\"note\":\"主线程派工超时（游戏可能没在跑或卡住）\"}";
            }
            catch (Exception e)
            {
                return "{\"error\":\"" + JsonMini.Esc(e.Message) + "\"}";
            }
        }

        private static string ProbeOnMain()
        {
            var sb = new StringBuilder(256);
            sb.Append('{');
            try
            {
                sb.Append("\"onMain\":").Append(MainThread.IsMain ? "true" : "false");
                sb.Append(",\"root\":\"").Append(JsonMini.Esc(BundleRoot)).Append('"');
                var png = Get(146);
                sb.Append(",\"pngBytes\":").Append(png == null ? 0 : png.Length);
                var tried = new List<string>();
                foreach (var path in FindBundles(146))
                {
                    tried.Add(path + ":文件存在");
                }
                sb.Append(",\"tried\":[");
                for (int i = 0; i < tried.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }
                    sb.Append('"').Append(JsonMini.Esc(tried[i])).Append('"');
                }
                sb.Append(']');
            }
            catch (Exception e)
            {
                sb.Append(",\"error\":\"").Append(JsonMini.Esc(e.Message)).Append('"');
            }
            sb.Append('}');
            return sb.ToString();
        }

        internal static void Serve(HttpListenerContext ctx)
        {
            int dataId;
            if (!int.TryParse(QS(ctx, "dataid"), out dataId) || dataId <= 0)
            {
                WebApi.Json(ctx, 400, "{\"ok\":false,\"error\":\"bad dataid\"}");
                return;
            }
            var png = Get(dataId);
            if (png == null)
            {
                var exists = ProbablyExists(dataId);
                if (exists) ctx.Response.Headers["Retry-After"] = "10";
                WebApi.Json(ctx, exists ? 503 : 404, exists
                    ? "{\"ok\":false,\"error\":\"曲绘暂不可用，请稍后重试\"}"
                    : "{\"ok\":false,\"error\":\"没有曲绘\"}");
                return;
            }
            try
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "image/png";
                ctx.Response.Headers["Cache-Control"] = "public, max-age=604800";
                ctx.Response.ContentLength64 = png.Length;
                ctx.Response.OutputStream.Write(png, 0, png.Length);
            }
            catch { }
            finally
            {
                try { ctx.Response.Close(); } catch { }
            }
        }

        private static string QS(HttpListenerContext ctx, string key)
        {
            var q = ctx.Request.Url == null ? "" : ctx.Request.Url.Query;
            foreach (var pair in q.TrimStart('?').Split('&'))
            {
                var eq = pair.IndexOf('=');
                if (eq > 0 && string.Equals(pair.Substring(0, eq), key, StringComparison.OrdinalIgnoreCase))
                {
                    return WebUtility.UrlDecode(pair.Substring(eq + 1));
                }
            }
            return "";
        }

        /// <summary>
        /// 从包里取贴图。资产名是带路径的全名（Assets/AssetBundle/Jacket_S/UI_Jacket_000017_s.png），
        /// 大小写敏感，所以按名字/后缀多路匹配，最后退回"包里唯一一张贴图"。
        /// </summary>
        private static UnityEngine.Texture2D LoadTexture(UnityEngine.AssetBundle bundle, string[] names)
        {
            var shortName = "";
            try
            {
                if (names != null)
                {
                    foreach (var n in names)
                    {
                        if (n == null)
                        {
                            continue;
                        }
                        if (n.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                        {
                            var trimmed = System.IO.Path.GetFileNameWithoutExtension(n);
                            if (shortName.Length == 0 && trimmed != null)
                            {
                                shortName = trimmed;
                            }
                        }
                        var byName = bundle.LoadAsset<UnityEngine.Texture2D>(n);
                        if (byName != null)
                        {
                            return byName;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ModLog.WarnOnce("按名取贴图出错: " + e.Message);
            }
            try
            {
                var all = bundle.LoadAllAssets<UnityEngine.Texture2D>();
                if (all != null && all.Length > 0)
                {
                    ModLog.Info("曲绘回退取首张贴图: " + (shortName.Length > 0 ? shortName : "?"));
                    return all[0];
                }
            }
            catch (Exception e)
            {
                ModLog.WarnOnce("枚举贴图出错: " + e.Message);
            }
            return null;
        }

        /// <summary>
        /// 打开资源包。Unity 的 LoadFromMemory/LoadFromFile 在这里都静默返回 null，
        /// 而包本身完好（UnityFS 头、版本、manifest 齐全），所以依次试：
        ///   ① 读 manifest 里的 CRC 走 LoadFromFile(path, crc, 0)   ② 无 CRC 的 LoadFromFile
        ///   ③ LoadFromMemory
        /// 每次失败都记原因：Unity 返回 null 不抛异常，只记 catch 永远看不到真因。
        /// 注意：必须在主线程调。
        /// </summary>
        private static UnityEngine.AssetBundle LoadBundle(string path)
        {
            var crc = ReadManifestCrc(path);
            if (crc != 0)
            {
                try
                {
                    var b = UnityEngine.AssetBundle.LoadFromFile(path, crc, 0);
                    if (b != null)
                    {
                        return b;
                    }
                    ModLog.WarnOnce("带CRC加载返回 null(" + Path.GetFileName(path) + ", crc=" + crc + ")");
                }
                catch (Exception e)
                {
                    ModLog.WarnOnce("带CRC加载抛异常(" + Path.GetFileName(path) + "): " + e.Message);
                }
            }
            try
            {
                var b = UnityEngine.AssetBundle.LoadFromFile(path, 0, 0);
                if (b != null)
                {
                    return b;
                }
                ModLog.WarnOnce("文件加载返回 null(" + Path.GetFileName(path) + ")");
            }
            catch (Exception e)
            {
                ModLog.WarnOnce("文件加载抛异常(" + Path.GetFileName(path) + "): " + e.Message);
            }
            try
            {
                var bytes = File.ReadAllBytes(path);
                var b = UnityEngine.AssetBundle.LoadFromMemory(bytes);
                if (b != null)
                {
                    return b;
                }
                ModLog.WarnOnce("内存加载返回 null(" + Path.GetFileName(path) + ", " + bytes.Length + " 字节)");
            }
            catch (Exception e)
            {
                ModLog.WarnOnce("内存加载抛异常(" + Path.GetFileName(path) + "): " + e.Message);
            }
            return null;
        }

        /// <summary>读 .ab.manifest 里的 CRC（没有就给 0）</summary>
        private static uint ReadManifestCrc(string path)
        {
            try
            {
                var manifest = path + ".manifest";
                if (!File.Exists(manifest))
                {
                    return 0;
                }
                foreach (var line in File.ReadAllLines(manifest))
                {
                    var trimmed = line.Trim();
                    if (!trimmed.StartsWith("CRC:", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    uint crc;
                    if (uint.TryParse(trimmed.Substring(4).Trim(), out crc))
                    {
                        return crc;
                    }
                }
            }
            catch { }
            return 0;
        }

        internal static byte[] Get(int dataId)
        {
            if (dataId <= 0) return null;
            lock (_lock)
            {
                byte[] hit;
                if (_cache.TryGetValue(dataId, out hit))
                {
                    return hit;
                }
                long retryAt;
                if (_missing.TryGetValue(dataId, out retryAt) && DateTime.UtcNow.Ticks < retryAt)
                {
                    return null;
                }
            }
            try
            {
                return MainThread.Run(delegate { return LoadAndCache(dataId); }, 2500, null);
            }
            catch { return null; }
        }

        private static byte[] LoadAndCache(int dataId)
        {
            // 同一曲绘的并发请求只解码一次。超时/未执行的任务不写入失败缓存。
            lock (_lock)
            {
                byte[] hit;
                if (_cache.TryGetValue(dataId, out hit)) return hit;
                long retryAt;
                if (_missing.TryGetValue(dataId, out retryAt) && DateTime.UtcNow.Ticks < retryAt) return null;
            }
            var png = Load(dataId);
            lock (_lock)
            {
                if (png == null || png.Length == 0)
                {
                    if (_missing.Count >= MaxCache) _missing.Clear();
                    _missing[dataId] = DateTime.UtcNow.AddSeconds(10).Ticks;
                    return null;
                }
                _missing.Remove(dataId);
                if (_cache.Count >= MaxCache)
                {
                    _cache.Remove(_cacheOrder.Dequeue());
                }
                _cache[dataId] = png;
                _cacheOrder.Enqueue(dataId);
                return png;
            }
        }

        // ── 主线程：从资源包取 Texture2D 再编码 PNG ──
        private static byte[] Load(int dataId)
        {
            if (!MainThread.IsMain)
            {
                ModLog.WarnOnce("解析曲绘时不在主线程");
                return null;
            }
            // 游戏已持有的包不能重复打开；只借用贴图，绝不能 Destroy 游戏对象。
            try
            {
                var assets = AssetManager.Instance();
                if (assets != null)
                {
                    var name = "UI_Jacket_" + dataId.ToString("D6");
                    foreach (var assetPath in new[] { "Jacket_S/" + name + "_S.png", "Jacket/" + name + ".png" })
                    {
                        var tex = assets.LoadAsset<UnityEngine.Texture2D>(assetPath);
                        if (tex == null) continue;
                        var png = EncodeTexture(tex);
                        if (png != null && png.Length > 0) return png;
                    }
                }
            }
            catch (Exception e) { ModLog.WarnOnce("读取游戏曲绘缓存失败: " + e.Message); }

            foreach (var path in FindBundles(dataId))
            {
                var variant = Path.GetFileName(path);
                UnityEngine.AssetBundle bundle = null;
                try
                {
                    bundle = LoadBundle(path);
                    if (bundle == null)
                    {
                        ModLog.WarnOnce("曲绘包打不开: " + variant);
                        continue;
                    }
                    var names = bundle.GetAllAssetNames();
                    var tex = LoadTexture(bundle, names);
                    if (tex == null)
                    {
                        ModLog.WarnOnce("曲绘包内没有贴图: " + variant + " 资产数="
                            + (names == null ? -1 : names.Length)
                            + (names != null && names.Length > 0 ? " 首个=" + names[0] : ""));
                        continue;
                    }
                    var png = EncodeTexture(tex);
                    if (png != null && png.Length > 0) return png;
                }
                catch (Exception e)
                {
                    ModLog.WarnOnce("解曲绘失败 " + dataId + ": " + e.Message);
                }
                finally
                {
                    try { if (bundle != null) bundle.Unload(true); } catch { }
                }
            }
            return null;
        }

        /// <summary>曲绘是否存在（不看内容，只判文件在不在）——给 /api/songs 决定要不要给 URL</summary>
        internal static bool ProbablyExists(int dataId)
        {
            foreach (var path in FindBundles(dataId)) return true;
            return false;
        }

        // 压缩或不可读的游戏贴图先转成可读 RGBA；所有临时 Unity 对象在主线程释放。
        private static byte[] EncodeTexture(UnityEngine.Texture2D texture)
        {
            if (texture.isReadable && (texture.format == UnityEngine.TextureFormat.RGBA32
                || texture.format == UnityEngine.TextureFormat.ARGB32
                || texture.format == UnityEngine.TextureFormat.RGB24))
                return UnityEngine.ImageConversion.EncodeToPNG(texture);

            var previous = UnityEngine.RenderTexture.active;
            UnityEngine.RenderTexture target = null;
            UnityEngine.Texture2D copy = null;
            try
            {
                target = UnityEngine.RenderTexture.GetTemporary(texture.width, texture.height, 0,
                    UnityEngine.RenderTextureFormat.ARGB32, UnityEngine.RenderTextureReadWrite.sRGB);
                UnityEngine.Graphics.Blit(texture, target);
                UnityEngine.RenderTexture.active = target;
                copy = new UnityEngine.Texture2D(texture.width, texture.height, UnityEngine.TextureFormat.RGBA32, false);
                copy.ReadPixels(new UnityEngine.Rect(0, 0, texture.width, texture.height), 0, 0);
                copy.Apply();
                return UnityEngine.ImageConversion.EncodeToPNG(copy);
            }
            finally
            {
                UnityEngine.RenderTexture.active = previous;
                if (copy != null) UnityEngine.Object.Destroy(copy);
                if (target != null) UnityEngine.RenderTexture.ReleaseTemporary(target);
            }
        }
    }
}
