// Stats / DxRating 的数值口径验证。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MaiPlanner
{
    internal static class Verify
    {
        private static int _fail;

        private static void Check(string what, bool ok)
        {
            if (!ok)
            {
                _fail++;
                Console.WriteLine("FAIL " + what);
            }
        }

        private static void Eq(string what, object got, object want)
        {
            var g = Convert.ToString(got, CultureInfo.InvariantCulture);
            var w = Convert.ToString(want, CultureInfo.InvariantCulture);
            if (g != w)
            {
                _fail++;
                Console.WriteLine("FAIL " + what + " got=" + g + " want=" + w);
            }
        }

        private static void Main()
        {
            // ── _pools 分界：CiRCLE 起 B15 = 本版本 + 上版本；更早只本版本 ──
            List<string> b15, b35;
            DxRating.PoolsOf("MAGiCAL", out b15, out b35);
            Eq("MAGiCAL b15 count", b15.Count, 2);
            Eq("MAGiCAL b15 last", b15[b15.Count - 1], "MAGiCAL");
            Eq("MAGiCAL b15 prev", b15[0], "CiRCLE PLUS");
            Eq("MAGiCAL b35 has FiNALE", b35.Contains("FiNALE"), true);
            Eq("MAGiCAL b35 last", b35[b35.Count - 1], "CiRCLE");
            Eq("MAGiCAL b35 no overlap", b35.Intersect(b15).Count(), 0);

            DxRating.PoolsOf("CiRCLE", out b15, out b35);
            Eq("CiRCLE b15 count", b15.Count, 2);
            Eq("CiRCLE b15 first", b15[0], "PRiSM PLUS");

            DxRating.PoolsOf("PRiSM", out b15, out b35);
            Eq("PRiSM b15 count", b15.Count, 1);
            Eq("PRiSM b15 only", b15[0], "PRiSM");

            DxRating.PoolsOf("DX", out b15, out b35);
            Eq("DX b15 count", b15.Count, 1);
            Eq("DX b35 count", b35.Count, 13);   // DX 是第一个 DX 版本，前面只有 13 个旧框版本

            // ── 造数据：跨版本，验 b35/b15 分池 + AP 加成 ──
            var songs = GameSnapshot._songs;
            AddSong(songs, 1, "maimai", 13.0);          // 旧框 → b35
            AddSong(songs, 2, "FESTiVAL", 14.0);        // 旧 DX → b35
            AddSong(songs, 3, "CiRCLE PLUS", 14.5);     // 上版本 → b15
            AddSong(songs, 4, "MAGiCAL", 14.8);         // 本版本 → b15
            var player = new PlayerStore.Player();
            player.Index = 0;
            AddScore(player, 1, 1005000, 1);            // 100.5
            AddScore(player, 2, 1000000, 0);
            AddScore(player, 3, 990000, 1);
            AddScore(player, 4, 1009999, 4);            // AP+，>100.5 按 100.5 封顶
            PlayerStore._all = new List<PlayerStore.Player> { player };

            var json = DxRating.Build(0, "MAGiCAL");
            Check("dxrating json", json != null);
            if (json != null)
            {
                var doc = JsonMini.ParseObj(json);
                Check("ok true", JsonMini.Bool(doc, "ok"));
                var rowsB15 = JsonMini.Arr(doc, "b15");
                var rowsB35 = JsonMini.Arr(doc, "b35");
                Eq("b15 size", rowsB15.Count, 2);
                Eq("b35 size", rowsB35.Count, 2);
                int total = JsonMini.Int(doc, "dxRating");
                // 手算：1 号 13.0@100.5 = int(22.4*13*100.5/100)=int(292.656)=292
                //       2 号 14.0@100.0 = int(21.6*14*100/100)=int(302.4)=302
                //       3 号 14.5@99.0 = int(20.8*14.5*99/100)=int(298.584)=298
                //       4 号 14.8@100.9999→100.5, AP = int(22.4*14.8*100.5/100)+1=int(333.13)+1=334
                // 行按 rating 降序排：b35 = [2 号 302, 1 号 292]，b15 = [4 号 334, 3 号 298]
                Eq("rating song2 (b35 first)", JsonMini.Int((Dictionary<string, object>)rowsB35.First(), "rating"), 302);
                Eq("rating song1 (b35 last)", JsonMini.Int((Dictionary<string, object>)rowsB35.Last(), "rating"), 292);
                Eq("rating song4 AP (b15 first)", JsonMini.Int((Dictionary<string, object>)rowsB15.First(), "rating"), 334);
                Eq("rating song3 (b15 last)", JsonMini.Int((Dictionary<string, object>)rowsB15.Last(), "rating"), 298);
                Eq("total", total, 292 + 302 + 298 + 334);
                var c = JsonMini.Obj(doc, "counts");
                Eq("skippedOtherVersion 0", JsonMini.Int(c, "skippedOtherVersion"), 0);
            }

            // 版本不在列表 → 出错
            Eq("bad version", DxRating.Build(0, "FiNALE"), null);
            // 别名版本可用
            Check("alias version ok", DxRating.Build(0, "maimaDXPLUS") != null);

            // ── Stats ──
            var statsJson = Stats.Build(0);
            Check("stats json", statsJson != null);
            if (statsJson != null)
            {
                var doc = JsonMini.ParseObj(statsJson);
                // RANK 直方图按快照 Rank 字段落桶：0(D) / 4(BBB) / 10(SS) / 13(SSS+)
                player.Scores[1][3].Rank = 0;
                player.Scores[2][3].Rank = 4;
                player.Scores[3][3].Rank = 10;
                player.Scores[4][3].Rank = 13;
                statsJson = Stats.Build(0);          // 重算（指纹变了会重建）
                doc = JsonMini.ParseObj(statsJson);
                var totals = JsonMini.Obj(doc, "totals");
                Eq("sheetsInLibrary", JsonMini.Int(totals, "sheetsInLibrary"), 4);
                Eq("played", JsonMini.Int(totals, "played"), 4);
                Eq("coverage 100", JsonMini.Dbl(totals, "coverage"), 100.0);
                Eq("hundred", JsonMini.Int(totals, "hundred"), 3);     // 1,2,4 号 ≥100%
                Eq("apPlus", JsonMini.Int(totals, "apPlusCount"), 1);
                var rank = JsonMini.Obj(doc, "rank");
                Eq("rank D", JsonMini.Int(rank, "D"), 1);
                Eq("rank BBB", JsonMini.Int(rank, "BBB"), 1);
                Eq("rank SS", JsonMini.Int(rank, "SS"), 1);
                Eq("rank SSS+", JsonMini.Int(rank, "SSS+"), 1);
                Eq("rank SSS 0", JsonMini.Int(rank, "SSS"), 0);
                var combo = JsonMini.Obj(doc, "combo");
                Eq("combo 没有", JsonMini.Int(combo, "没有"), 1);     // combo=1 → FC 桶；combo=0 → 没有
                Eq("combo FC", JsonMini.Int(combo, "FC"), 2);
                Eq("combo AP+", JsonMini.Int(combo, "AP+"), 1);
                var families = JsonMini.Obj(doc, "families");
                Eq("family std sheets", JsonMini.Int(JsonMini.Obj(families, "std"), "sheets"), 4);
                var versions = JsonMini.Arr(doc, "versions");
                // 版本行按 sheets 降序、版本名升序；4 个版本各 1 张
                Eq("version rows", versions.Count, 4);
                bool hasMaimai = versions.Cast<Dictionary<string, object>>()
                    .Any(x => JsonMini.Str(x, "version") == "maimai");
                Check("version maimai row", hasMaimai);
                // 按族/版本的 played 计数
                var fest = versions.Cast<Dictionary<string, object>>()
                    .First(x => JsonMini.Str(x, "version") == "FESTiVAL");
                Eq("version FESTiVAL played", JsonMini.Int(fest, "played"), 1);
                Eq("version FESTiVAL coverage", JsonMini.Dbl(fest, "coverage"), 100.0);
            }

            Console.WriteLine("fail=" + _fail);
            Environment.Exit(_fail == 0 ? 0 : 1);
        }

        private static void AddSong(List<GameSnapshot.Song> songs, int id, string ver, double level)
        {
            var s = new GameSnapshot.Song();
            s.InternalId = id;
            s.Version = ver;
            s.Enabled[3] = true;
            s.LevelValue[3] = (int)Math.Round(level * 10);
            songs.Add(s);
        }

        private static void AddScore(PlayerStore.Player p, int id, uint ach, int combo)
        {
            var sh = new PlayerStore.Sheet();
            sh.ChartId = 3;
            sh.Achievement = ach;
            sh.Combo = combo;
            sh.PlayCount = 5;
            sh.Rank = 13;
            Dictionary<int, PlayerStore.Sheet> byChart;
            if (!p.Scores.TryGetValue(id, out byChart))
            {
                byChart = new Dictionary<int, PlayerStore.Sheet>();
                p.Scores[id] = byChart;
            }
            byChart[3] = sh;
        }
    }
}
