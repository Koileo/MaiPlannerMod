// 用途：网页线程 ↔ 游戏主线程的边界约定。所有游戏对象/Unity API 只能在主线程碰。
// 状态：现行。Web 线程读游戏数据一律走 Main.Of(...) 派到主线程，带超时兜底。
using System;
using System.Collections.Generic;

namespace MaiPlanner
{
    /// <summary>
    /// 游戏数据的只读快照层：全部在主线程构建，网页线程只读缓存。
    /// 曲目表 / 玩家成绩 / 游戏状态都从这里取，网页线程不直接摸 Unity 对象。
    /// </summary>
    internal static class GameSnapshot
    {
        internal sealed class Song
        {
            public int InternalId;
            public int DataId;
            public string Title = "";
            public string Artist = "";
            public string Version = "";
            public int Category;
            public bool Disable;
            public string[] LevelText = new string[5];
            public int[] LevelValue = new int[5];      // xi 100（137 = 13.7）
            public string[] Designer = new string[5];
            public int[] NoteCount = new int[5];
            public bool[] Enabled = new bool[5];
        }

        private static volatile List<Song> _songs = new List<Song>();
        private static volatile bool _loaded;
        private static long _builtAt;

        internal static bool Loaded { get { return _loaded; } }
        internal static int Count { get { return _songs.Count; } }
        internal static List<Song> Songs { get { return _songs; } }
        internal static long BuiltAt { get { return _builtAt; } }

        private static Dictionary<int, Song> _index;

        /// <summary>按内部号查（含索引缓存）；查不到返回 null</summary>
        internal static Song Find(int internalId)
        {
            var index = _index;
            if (index == null || index.Count != _songs.Count)
            {
                index = new Dictionary<int, Song>();
                foreach (var s in _songs)
                {
                    index[s.InternalId] = s;
                }
                _index = index;
            }
            Song hit;
            return index.TryGetValue(internalId, out hit) ? hit : null;
        }

        /// <summary>主线程：从已建立的曲目表生成快照（SelectDriver/DataManager 就绪后调用）。</summary>
        internal static void Rebuild()
        {
            if (!MainThread.IsMain)
            {
                return;
            }
            var list = new List<Song>();
            try
            {
                foreach (var kv in SongTable.Each())
                {
                    var md = kv.Value;
                    if (md == null)
                    {
                        continue;
                    }
                    var s = new Song();
                    s.InternalId = kv.Key;
                    s.DataId = kv.Key % 10000;
                    try { s.Title = md.name == null ? "" : (md.name.str ?? ""); } catch { }
                    try { s.Artist = md.artistName == null ? "" : (md.artistName.str ?? ""); } catch { }
                    try { s.Version = md.AddVersion == null ? "" : (md.AddVersion.str ?? ""); } catch { }
                    s.Category = SafeInt(delegate { return (int)md.genreName.id; });
                    s.Disable = SafeBool(delegate { return md.disable; });
                    int i = 0;
                    try
                    {
                        foreach (var nt in md.notesData)
                        {
                            if (i > 4)
                            {
                                break;
                            }
                            s.Enabled[i] = nt.isEnable;
                            s.LevelValue[i] = nt.level * 10 + Math.Abs(nt.levelDecimal % 10);
                            s.LevelText[i] = LevelTextOf(nt.level, nt.levelDecimal);
                            s.NoteCount[i] = nt.maxNotes;
                            s.Designer[i] = SafeStr(delegate { return nt.notesDesigner.str; });
                            i++;
                        }
                    }
                    catch { }
                    list.Add(s);
                }
            }
            catch (Exception e)
            {
                ModLog.WarnOnce("构建曲目快照失败: " + e.Message);
            }
            if (list.Count > 0)
            {
                _songs = list;
                _loaded = true;
                _builtAt = Environment.TickCount;
            }
        }

        private static int SafeInt(Func<int> f) { try { return f(); } catch { return 0; } }
        private static bool SafeBool(Func<bool> f) { try { return f(); } catch { return false; } }
        private static string SafeStr(Func<string> f) { try { return f() ?? ""; } catch { return ""; } }

        /// <summary>定数文本：137 → "13.7"，130 → "13"，宴谱给 "?"</summary>
        internal static string LevelTextOf(int level, int dec)
        {
            if (level <= 0)
            {
                return "";
            }
            var d = Math.Abs(dec) % 10;
            return d == 0 ? level.ToString() : level + "." + d;
        }
    }
}
