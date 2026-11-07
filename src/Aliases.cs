// 用途：曲目别名。数据来自内嵌的 aliases.json（按曲名索引，来自扫码点歌内置曲库的搜索用别名）。
// 状态：现行。只在首次用到时加载一次；按曲名精确匹配（大小写不敏感）。
using System;
using System.Collections.Generic;

namespace MaiPlanner
{
    internal static class Aliases
    {
        private static readonly object _lock = new object();
        private static Dictionary<string, List<string>> _map;
        private static bool _tried;

        private static Dictionary<string, List<string>> Map
        {
            get
            {
                lock (_lock)
                {
                    if (_tried)
                    {
                        return _map;
                    }
                    _tried = true;
                    _map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        var json = EmbeddedWeb.ReadText("aliases.json");
                        var doc = JsonMini.ParseObj(json);
                        var songs = doc == null ? null : JsonMini.Obj(doc, "songs");
                        if (songs != null)
                        {
                            foreach (var kv in songs)
                            {
                                var list = new List<string>();
                                var arr = kv.Value as List<object>;
                                if (arr != null)
                                {
                                    foreach (var item in arr)
                                    {
                                        var a = Convert.ToString(item);
                                        if (!string.IsNullOrEmpty(a))
                                        {
                                            list.Add(a);
                                        }
                                    }
                                }
                                _map[kv.Key] = list;
                            }
                        }
                        ModLog.Info("别名表: " + _map.Count + " 首");
                    }
                    catch (Exception e)
                    {
                        ModLog.WarnOnce("读别名表失败: " + e.Message);
                    }
                    return _map;
                }
            }
        }

        /// <summary>已加载的别名条数（自检用）</summary>
        internal static int LoadedCount { get { return Map.Count; } }

        /// <summary>该曲名对应的别名（没有就是空表）</summary>
        internal static List<string> ByTitle(string title)
        {
            if (string.IsNullOrEmpty(title))
            {
                return new List<string>();
            }
            List<string> list;
            return Map.TryGetValue(title, out list) ? list : new List<string>();
        }

        /// <summary>兼容按 internalId 取（曲名要现查）</summary>
        internal static List<string> For(int internalId)
        {
            var song = WebApi.FindSong(internalId);
            return song == null ? new List<string>() : ByTitle(song.Title);
        }
    }
}
