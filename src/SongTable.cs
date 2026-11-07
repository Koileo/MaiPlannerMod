// 用途：曲目表的极简替身 —— 只提供 SelectDriver 需要的那些成员（没网页就不用导出 JSON）。
// 状态：现行。数据源优先反射 DataManager 的私有字段 _musics（别的 mod 会把 GetMusics() 过滤掉）。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace MaiPlanner
{
    internal static class SongTable
    {
        internal static readonly string[] DiffNames = { "BASIC", "ADVANCED", "EXPERT", "MASTER", "Re:MASTER" };

        private static Dictionary<int, Manager.MaiStudio.MusicData> _byId =
            new Dictionary<int, Manager.MaiStudio.MusicData>();

        internal static int Rev
        {
            get { return _byId.Count; }
        }

        private static bool FillFromSelectList(Dictionary<int, Manager.MaiStudio.MusicData> map)
        {
            var list = SelectDriver.SelectList;
            if (list == null)
            {
                return false;
            }
            foreach (var page in list)
            {
                if (page == null)
                {
                    continue;
                }
                foreach (var item in page)
                {
                    if (item == null || item.msDetailData == null || item.isRandomScore)
                    {
                        continue;
                    }
                    int id = item.msDetailData.musicId;
                    if (id <= 0)
                    {
                        continue;
                    }
                    Manager.MaiStudio.MusicData md = null;
                    try
                    {
                        if (item.musicSelectData != null)
                        {
                            for (int s = 0; s < item.musicSelectData.Count && md == null; s++)
                            {
                                if (item.musicSelectData[s] != null)
                                {
                                    md = item.musicSelectData[s].MusicData;
                                }
                            }
                        }
                    }
                    catch
                    {
                    }
                    if (md != null)
                    {
                        map[id] = md;
                    }
                }
            }
            return map.Count > 0;
        }

        private static void Ensure()
        {
            if (_byId.Count > 0 || !MainThread.IsMain)
            {
                return;   // 只在主线程读游戏数据
            }
            var dm = MAI2.Util.Singleton<Manager.DataManager>.Instance;
            if (dm == null)
            {
                return;
            }
            var map = new Dictionary<int, Manager.MaiStudio.MusicData>();
            // 数据源①：游戏自己的选曲列表。不依赖 DataManager 的私有字段名/形状，
            // 也能绕过 MuNet 等 mod 对 GetMusic/GetMusics 的过滤。
            try
            {
                if (FillFromSelectList(map))
                {
                    ModLog.Info("曲目表: " + map.Count + " 首（来源选曲列表）");
                }
            }
            catch (Exception e)
            {
                ModLog.WarnOnce("读取选曲列表失败: " + e.Message);
            }
            if (map.Count == 0)
            {
                try
                {
                    var f = typeof(Manager.DataManager).GetField("_musics",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (f != null)
                    {
                        var v = f.GetValue(dm) as IDictionary;
                        if (v != null)
                        {
                            foreach (DictionaryEntry e in v)
                            {
                                var md = e.Value as Manager.MaiStudio.MusicData;
                                if (md == null)
                                {
                                    continue;
                                }
                                try
                                {
                                    map[md.GetID()] = md;
                                }
                                catch
                                {
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    ModLog.WarnOnce("反射 DataManager._musics 失败: " + e.Message);
                }
            }
            if (map.Count > 0)
            {
                _byId = map;
                ModLog.Info("曲目表: " + map.Count + " 首");
            }
            else
            {
                ModLog.WarnOnce("曲目表还没读到（选曲列表/_musics 都为空）—— 将拒绝跳转");
            }
        }

        internal static Manager.MaiStudio.MusicData GetMusic(int id)
        {
            Ensure();
            var byId = _byId;
            Manager.MaiStudio.MusicData md;
            if (byId.TryGetValue(id, out md))
            {
                return md;
            }
            if (id >= 10000 && byId.TryGetValue(id % 10000, out md))
            {
                return md;
            }
            // 不再回退 DataManager.GetMusic：该接口会被 MuNet 等 mod 过滤，
            // 查不到曲名表时由 Jump 直接拒绝，避免拿错歌/错类型继续跳。
            return null;
        }

        /// <summary>主线程每帧调用，进入选曲界面后把曲目表建起来。</summary>
        internal static void Tick()
        {
            Ensure();
        }

        internal static IEnumerable<KeyValuePair<int, Manager.MaiStudio.MusicData>> Each()
        {
            Ensure();
            return _byId;
        }

        internal static string NameOf(int id)
        {
            try
            {
                var md = GetMusic(id);
                if (md == null || md.name == null)
                {
                    return "";
                }
                return md.name.str ?? "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>DX 行 id ≥ 10000（与上游 /api/songs 的 type 判定一致）</summary>
        internal static void TypeOf(int id, out bool std, out bool dx)
        {
            dx = id >= 10000;
            std = !dx;
        }

        internal static string DifficultyName(int d)
        {
            return (d >= 0 && d < DiffNames.Length) ? DiffNames[d] : "?";
        }

        internal static bool DifficultyEnabled(int musicId, int d)
        {
            try
            {
                var md = GetMusic(musicId);
                if (md == null || md.notesData == null)
                {
                    return false;
                }
                int i = 0;
                foreach (var nt in md.notesData)
                {
                    if (i == d)
                    {
                        return nt.isEnable;
                    }
                    i++;
                }
            }
            catch
            {
            }
            return false;
        }

        internal static int HighestEnabled(int musicId)
        {
            try
            {
                var md = GetMusic(musicId);
                if (md == null || md.notesData == null)
                {
                    return -1;
                }
                int best = -1;
                int i = 0;
                foreach (var nt in md.notesData)
                {
                    if (nt.isEnable && i <= 4 && i > best)
                    {
                        best = i;
                    }
                    i++;
                }
                return best;
            }
            catch
            {
                return -1;
            }
        }

        internal static List<int> RandomPool(int difficulty)
        {
            var list = new List<int>();
            foreach (var kv in Each())
            {
                if (DifficultyEnabled(kv.Key, difficulty))
                {
                    list.Add(kv.Key);
                }
            }
            return list;
        }

        internal static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return "";
            }
            var sb = new StringBuilder(s.Length + 8);
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 0x20)
                        {
                            sb.Append("\\u").Append(((int)ch).ToString("x4"));
                        }
                        else
                        {
                            sb.Append(ch);
                        }
                        break;
                }
            }
            return sb.ToString();
        }

        internal static string DiagJson()
        {
            return "{\"source\":\"_musics\",\"byId\":" + _byId.Count + ",\"rev\":" + Rev + "}";
        }
    }
}
