// 用途：用户数据落盘（收藏、黑名单、成绩导入、设置）。放 UserData/MaiPlannerMod/data/<user>/。
// 状态：现行。只操作文件，不碰 Unity；任何线程可调。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MaiPlanner
{
    internal static class UserFiles
    {
        private static string _root;
        private static readonly object _fileLock = new object();

        /// <summary>UserData/MaiPlannerMod/data</summary>
        internal static string Root
        {
            get
            {
                if (_root != null)
                {
                    return _root;
                }
                try
                {
                    var baseDir = Path.Combine(Environment.CurrentDirectory, "UserData", "MaiPlannerMod", "data");
                    Directory.CreateDirectory(baseDir);
                    _root = baseDir;
                }
                catch
                {
                    _root = Path.Combine(Path.GetTempPath(), "MaiPlannerMod", "data");
                    try { Directory.CreateDirectory(_root); } catch { }
                }
                return _root;
            }
        }

        internal static string DirOf(string user)
        {
            var safe = SafeId(user);
            var dir = Path.Combine(Root, safe);
            try { Directory.CreateDirectory(dir); } catch { }
            return dir;
        }

        private static string SafeId(string user)
        {
            if (string.IsNullOrEmpty(user))
            {
                return "0";
            }
            var sb = new StringBuilder(user.Length);
            foreach (var ch in user)
            {
                sb.Append(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '_');
            }
            return sb.Length == 0 ? "0" : sb.ToString();
        }

        internal static string ReadText(string user, string name)
        {
            lock (_fileLock)
            {
                try
                {
                    var path = Path.Combine(DirOf(user), name);
                    return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
                }
                catch
                {
                    return null;
                }
            }
        }

        internal static void WriteText(string user, string name, string text)
        {
            lock (_fileLock)
            {
                string tmp = null;
                try
                {
                    var dir = DirOf(user);
                    var path = Path.Combine(dir, name);
                    tmp = path + "." + Guid.NewGuid().ToString("N") + ".part";
                    File.WriteAllText(tmp, text ?? "", new UTF8Encoding(false));
                    if (File.Exists(path))
                    {
                        File.Replace(tmp, path, null);
                    }
                    else
                    {
                        File.Move(tmp, path);
                    }
                }
                catch (Exception e)
                {
                    ModLog.WarnOnce("写 " + name + " 失败: " + e.Message);
                }
                finally
                {
                    try { if (tmp != null && File.Exists(tmp)) File.Delete(tmp); } catch { }
                }
            }
        }

        // ── 收藏 ──
        internal static HashSet<string> Favorites(string user)
        {
            var set = new HashSet<string>();
            var doc = JsonMini.ParseObj(ReadText(user, "favorites.json"));
            var arr = doc == null ? null : JsonMini.Get(doc, "entries") as List<object>;
            if (arr == null)
            {
                return set;
            }
            foreach (var item in arr)
            {
                var row = item as Dictionary<string, object>;
                if (row == null)
                {
                    continue;
                }
                set.Add(Key(JsonMini.Int(row, "internalId"), JsonMini.Int(row, "chartid")));
            }
            return set;
        }

        internal static bool ToggleFavorite(string user, int internalId, int chartId, bool add)
        {
            lock (_fileLock)
            {
                var set = Favorites(user);
                var key = Key(internalId, chartId);
                if (add)
                {
                    set.Add(key);
                }
                else
                {
                    set.Remove(key);
                }
                var sb = new StringBuilder("{\"entries\":[");
                bool first = true;
                foreach (var k in set)
                {
                    var bits = k.Split(':');
                    if (!first)
                    {
                        sb.Append(',');
                    }
                    first = false;
                    sb.Append("{\"internalId\":").Append(bits[0])
                      .Append(",\"chartid\":").Append(bits[1]).Append('}');
                }
                sb.Append("]}");
                WriteText(user, "favorites.json", sb.ToString());
                return add;
            }
        }

        // ── 黑名单 ──
        internal static Dictionary<string, long> Blacklist(string user)
        {
            var map = new Dictionary<string, long>();     // key -> 到期 unix 秒（0 = 永久）
            var doc = JsonMini.ParseObj(ReadText(user, "blacklist.json"));
            var arr = doc == null ? null : JsonMini.Get(doc, "entries") as List<object>;
            if (arr == null)
            {
                return map;
            }
            var now = NowUnix();
            foreach (var item in arr)
            {
                var row = item as Dictionary<string, object>;
                if (row == null)
                {
                    continue;
                }
                var until = JsonMini.Long(row, "until");
                if (until > 0 && until < now)
                {
                    continue;
                }
                map[Key(JsonMini.Int(row, "internalId"), JsonMini.Int(row, "chartid"))] = until;
            }
            return map;
        }

        internal static void SetBlacklist(string user, int internalId, int chartId, string duration, bool remove)
        {
            lock (_fileLock)
            {
                var map = Blacklist(user);
                var key = Key(internalId, chartId);
                if (remove)
                {
                    map.Remove(key);
                }
                else
                {
                    map[key] = ExpiryOf(duration);
                }
                var sb = new StringBuilder("{\"entries\":[");
                bool first = true;
                foreach (var kv in map)
                {
                    var bits = kv.Key.Split(':');
                    if (!first)
                    {
                        sb.Append(',');
                    }
                    first = false;
                    sb.Append("{\"internalId\":").Append(bits[0])
                      .Append(",\"chartid\":").Append(bits[1])
                      .Append(",\"until\":").Append(kv.Value).Append('}');
                }
                sb.Append("]}");
                WriteText(user, "blacklist.json", sb.ToString());
            }
        }

        private static long ExpiryOf(string duration)
        {
            switch ((duration ?? "").ToLowerInvariant())
            {
                case "1d": return NowUnix() + 86400;
                case "3d": return NowUnix() + 86400 * 3;
                case "7d": return NowUnix() + 86400 * 7;
                case "30d": return NowUnix() + 86400 * 30;
                case "forever": return 0;
                default: return 0;
            }
        }

        private static long NowUnix()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
        }

        internal static string Key(int internalId, int chartId)
        {
            return internalId.ToString(CultureInfo.InvariantCulture) + ":" + chartId.ToString(CultureInfo.InvariantCulture);
        }

        // ── 玩家设置（助手卡片等）──
        internal static string ReadSettings(string user, string name)
        {
            return ReadText(user, name);
        }

        internal static void WriteSettings(string user, string name, string json)
        {
            WriteText(user, name, json);
        }
    }
}
