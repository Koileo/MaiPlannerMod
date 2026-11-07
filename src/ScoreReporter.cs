// 用途：一局打完把成绩 POST 到配置的回调地址（主线程只读快照，网络在后台线程）。
// 状态：现行。触发方式=「游玩中持续采样 → 离开游玩状态时发一次」，不依赖任何结算界面类型名。
using System;
using System.Globalization;
using System.Text;
using System.Threading;

namespace MaiPlanner
{
    internal static class ScoreReporter
    {
        private sealed class Snap
        {
            public int Id;
            public int Diff;
            public uint Notes;
            public int TrackNo;
            public uint Combo;
            public uint MaxCombo;
            public uint Cp;
            public uint Tc;
            public uint Perfect;
            public uint Great;
            public uint Good;
            public uint Miss;
            public string Achive;
            public string Type;
        }

        private static Snap _last;
        private static bool _armed;
        private static volatile bool _inGame;
        internal static bool InGame { get { return _inGame; } }

        /// <summary>每帧在主线程调（只在 Mod.Tick 里调）</summary>
        internal static void Tick()
        {
            if (!Mod.Ready)
            {
                return;
            }

            Manager.GameScoreList gs = null;
            try
            {
                var gpm = MAI2.Util.Singleton<Manager.GamePlayManager>.Instance;
                if (gpm != null)
                {
                    gs = gpm.GetGameScore(0);
                }
            }
            catch
            {
            }

            bool playing = false;
            try
            {
                playing = gs != null && gs.IsEnable;
            }
            catch
            {
            }

            _inGame = playing;
            if (playing)
            {
                _last = Take(gs);
                _armed = true;
                return;
            }

            // 从"在玩"变成"不在玩" = 这一局结束了（结算界面/回选曲界面）
            if (!_armed)
            {
                return;
            }
            _armed = false;
            var s = _last;
            _last = null;
            if (s != null)
            {
                Emit(s);
            }
        }

        private static Snap Take(Manager.GameScoreList gs)
        {
            var s = new Snap();
            try
            {
                s.Id = gs.SessionInfo.musicId;
                s.Diff = gs.SessionInfo.difficulty;
                s.TrackNo = gs.TrackNo;
                s.Combo = gs.Combo;
                s.MaxCombo = gs.MaxCombo;
                s.Notes = gs.TheoryCombo;
                s.Cp = gs.CriticalNum;
                s.Tc = gs.TrueCriticalNum;
                s.Perfect = gs.PerfectNum;
                s.Great = gs.GreatNum;
                s.Good = gs.GoodNum;
                s.Miss = gs.MissNum;
                s.Achive = ((double)gs.GetAchivement()).ToString("0.0000", CultureInfo.InvariantCulture);
            }
            catch
            {
            }
            bool std, dx;
            SongTable.TypeOf(s.Id, out std, out dx);
            s.Type = dx ? "dx" : "std";
            return s;
        }

        private static void Emit(Snap s)
        {
            // 先交给打歌助手（匹配上就顺延推下一首）
            try
            {
                for (int i = 0; i < PlayerStore.All.Count; i++)
                {
                    var p = PlayerStore.All[i];
                    if (p.IsEntry)
                    {
                        Assistant.OnScore(p.Index.ToString(CultureInfo.InvariantCulture), s.Id, s.Diff);
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                ModLog.Info("助手顺延异常: " + e.Message);
            }

            var url = WebApi.CallbackTo();
            if (string.IsNullOrEmpty(url))
            {
                return;
            }
            var json = Build(s);
            ModLog.Info("成绩回传: " + json);
            ThreadPool.QueueUserWorkItem(delegate { Post(url, json); });
        }

        /// <summary>注意：故意不含玩家名（UserName）等身份字段</summary>
        private static string Build(Snap s)
        {
            var sb = new StringBuilder(320);
            sb.Append('{');
            sb.Append("\"musicId\":").Append(s.Id);
            sb.Append(",\"name\":\"").Append(SongTable.Escape(SongTable.NameOf(s.Id))).Append('"');
            sb.Append(",\"type\":\"").Append(s.Type).Append('"');
            sb.Append(",\"difficulty\":").Append(s.Diff);
            sb.Append(",\"difficultyName\":\"").Append(SongTable.DifficultyName(s.Diff)).Append('"');
            sb.Append(",\"achive\":").Append(s.Achive);
            sb.Append(",\"combo\":").Append(s.Combo);
            sb.Append(",\"maxCombo\":").Append(s.MaxCombo);
            sb.Append(",\"notes\":").Append(s.Notes);
            sb.Append(",\"trackNo\":").Append(s.TrackNo);
            sb.Append(",\"judge\":{");
            sb.Append("\"criticalPerfect\":").Append(s.Cp);
            sb.Append(",\"trueCritical\":").Append(s.Tc);
            sb.Append(",\"perfect\":").Append(s.Perfect);
            sb.Append(",\"great\":").Append(s.Great);
            sb.Append(",\"good\":").Append(s.Good);
            sb.Append(",\"miss\":").Append(s.Miss);
            sb.Append('}');
            sb.Append('}');
            return sb.ToString();
        }

        private static void Post(string url, string json)
        {
            try
            {
                var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                req.Method = "POST";
                req.ContentType = "application/json; charset=utf-8";
                req.Timeout = 3000;
                req.ReadWriteTimeout = 3000;
                var bytes = Encoding.UTF8.GetBytes(json);
                req.ContentLength = bytes.Length;
                using (var st = req.GetRequestStream())
                {
                    st.Write(bytes, 0, bytes.Length);
                }
                using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
                {
                    ModLog.Info("成绩回传成功: HTTP " + (int)resp.StatusCode);
                }
            }
            catch (Exception e)
            {
                ModLog.WarnOnce("成绩回传失败（" + url + "）: " + e.Message);
            }
        }
    }
}
