// 用途：内嵌网页资源的读写。所有前端文件在编译期嵌进 DLL（LogicalName = MaiPlanner.web.<path>）。
// 状态：现行。Mods 里只放一个 DLL 就能跑，不附带 web 目录。
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace MaiPlanner
{
    internal static class EmbeddedWeb
    {
        private const string Prefix = "MaiPlanner.web.";
        private static readonly Dictionary<string, string> _types =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { ".html", "text/html; charset=utf-8" },
                { ".css", "text/css; charset=utf-8" },
                { ".js", "application/javascript; charset=utf-8" },
                { ".json", "application/json; charset=utf-8" },
                { ".svg", "image/svg+xml" },
                { ".png", "image/png" },
                { ".webp", "image/webp" },
                { ".ico", "image/x-icon" },
                { ".ttf", "font/ttf" },
                { ".woff2", "font/woff2" },
                { ".txt", "text/plain; charset=utf-8" },
            };

        /// <summary>内嵌资源名（web/ 之后的部分，正斜杠）</summary>
        internal static bool Exists(string relative)
        {
            return Find(relative) != null;
        }

        internal static byte[] Read(string relative)
        {
            var name = Find(relative);
            if (name == null)
            {
                return null;
            }
            try
            {
                using (var s = typeof(EmbeddedWeb).GetTypeInfo().Assembly.GetManifestResourceStream(name))
                {
                    if (s == null)
                    {
                        return null;
                    }
                    using (var ms = new MemoryStream((int)s.Length))
                    {
                        s.CopyTo(ms);
                        return ms.ToArray();
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        internal static string ReadText(string relative)
        {
            var bytes = Read(relative);
            return bytes == null ? null : Encoding.UTF8.GetString(bytes);
        }

        internal static string ContentTypeFor(string relative)
        {
            var ext = Path.GetExtension(relative ?? "");
            string type;
            return _types.TryGetValue(ext ?? "", out type) ? type : "application/octet-stream";
        }

        private static string Find(string relative)
        {
            if (string.IsNullOrEmpty(relative))
            {
                return null;
            }
            var clean = relative.TrimStart('/').Replace('/', '.');
            var full = Prefix + clean;
            foreach (var name in typeof(EmbeddedWeb).GetTypeInfo().Assembly.GetManifestResourceNames())
            {
                if (string.Equals(name, full, StringComparison.OrdinalIgnoreCase))
                {
                    return name;
                }
            }
            return null;
        }
    }
}
