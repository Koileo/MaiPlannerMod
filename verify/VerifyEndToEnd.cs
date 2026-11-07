// 端到端：造假曲库 + 假成绩，跑完整分析，检查 JSON 同构与分类语义。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

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

        private static void Main()
        {
            // 造曲库：std 旧曲 40 张（13.0~14.0）、dx 新曲 20 张（13.0~14.5）
            var songs = GameSnapshot._songs;
            int id = 1;
            for (int i = 0; i < 40; i++)
            {
                var s = new GameSnapshot.Song();
                s.InternalId = id++;
                s.Version = "FESTiVAL";
                s.Enabled[3] = true;
                s.LevelValue[3] = 130 + (i % 11);
                s.Title = "old" + i;
                songs.Add(s);
            }
            for (int i = 0; i < 20; i++)
            {
                var s = new GameSnapshot.Song();
                s.InternalId = 20000 + id++;
                s.Version = "MAGiCAL";
                s.Enabled[3] = true;
                s.LevelValue[3] = 130 + (i % 16);
                s.Title = "new" + i;
                songs.Add(s);
            }
            for (int i = 0; i < 2; i++)
            {
                var s = new GameSnapshot.Song();
                s.InternalId = 100000 + i;
                s.Version = "宴";
                s.Enabled[3] = true;
                s.LevelValue[3] = 140;
                songs.Add(s);
            }

            // 造成绩：全部打一遍，100.2~100.6%，少量 AP
            var player = new PlayerStore.Player();
            player.Index = 0;
            player.Name = "tester";
            int k = 0;
            foreach (var s in songs)
            {
                var sh = new PlayerStore.Sheet();
                sh.ChartId = 3;
                sh.Achievement = (uint)(1002000 + (k % 5) * 1000);
                if (k % 4 == 1)
                {
                    sh.Achievement = 1010000;            // 一部分 101%（>100.85，ap 池候选）
                }
                sh.Combo = (k % 7 == 0) ? 3 : 1;
                sh.Sync = 1;
                sh.PlayCount = 20;
                sh.Rank = 12;
                Dictionary<int, PlayerStore.Sheet> byChart;
                if (!player.Scores.TryGetValue(s.InternalId, out byChart))
                {
                    byChart = new Dictionary<int, PlayerStore.Sheet>();
                    player.Scores[s.InternalId] = byChart;
                }
                byChart[3] = sh;
                k++;
            }
            PlayerStore._all = new List<PlayerStore.Player> { player };

            Analysis.TagProvider = delegate (int iid, int cid)
            {
                var tags = new List<AnalysisTag>();
                if (iid % 3 == 0)
                {
                    tags.Add(new AnalysisTag { Name = "交互", Group = "PatternsTag" });
                }
                if (iid % 5 == 0)
                {
                    tags.Add(new AnalysisTag { Name = "体力谱", Group = "EvaluationTag" });
                }
                if (iid % 11 == 0)
                {
                    tags.Add(new AnalysisTag { Name = "诈称谱", Group = "EvaluationTag" });
                }
                return tags;
            };

            var res = Analysis.Build("0", true);
            Check("result not null", res != null);
            if (res == null)
            {
                Console.WriteLine("fail=1");
                Environment.Exit(1);
            }
            var json = res.Json;
            Check("json not null", json != null);
            Check("json starts {", json.StartsWith("{", StringComparison.Ordinal));
            Check("schema", json.Contains("\"schema\":1"));
            Check("fitModel", json.Contains("\"fitModel\":"));
            Check("counts", json.Contains("\"counts\":"));
            Check("charts", json.Contains("\"charts\":["));
            Check("abilityCurve", json.Contains("\"abilityCurve\":"));
            Check("tagStats", json.Contains("\"tagStats\":"));
            Check("params", json.Contains("\"params\":"));

            Check("no utage rows", !res.Rows.Any(r => r.Family == "utage" || r.Family == "utage2p"));
            Check("rows == 60", res.Rows.Count == 60);
            Check("conf lo", Math.Abs(res.Lo - (res.Ability[0] - 0.3)) < 1e-9);
            Check("conf hi", Math.Abs(res.Hi - (res.Ability[1] + 0.3)) < 1e-9);
            Check("b35 > 0", res.B35 > 0);
            Check("b15 > 0", res.B15 > 0);
            Check("oldCount 40", res.OldCount == 40);
            Check("newCount 20", res.NewCount == 20);

            var r0 = res.Rows[0];
            Check("row effectiveLevel present", r0.EffectiveLevel != null);
            Check("row labels list", r0.Labels != null);

            UserFiles.WriteText("0", "blacklist.json",
                "{\"entries\":[{\"internalId\":1,\"chartid\":3,\"duration\":\"forever\"}]}");
            var res2 = Analysis.Build("0", true);
            var blocked = res2.Rows.FirstOrDefault(x => x.InternalId == 1 && x.ChartId == 3);
            Check("blocked row exists", blocked != null);
            if (blocked != null)
            {
                Check("blocked is blacklisted", blocked.Blacklisted);
                Check("blocked labels empty", blocked.Labels.Count == 0);
            }
            UserFiles.WriteText("0", "blacklist.json", "{\"entries\":[]}");

            var doc = Analysis.Build("0", true);
            var spec = new PoolSpec();
            spec.Pool = "mastered";
            var cands = Pools.Candidates("0", doc, spec);
            Check("mastered pool non-empty", cands.Count > 0);
            var specAp = new PoolSpec();
            specAp.Pool = "ap";
            var apCands = Pools.Candidates("0", doc, specAp);
            Check("ap pool non-empty", apCands.Count > 0);

            var parsed = JsonMini.ParseObj(json);
            Check("json parses", parsed != null);
            if (parsed != null)
            {
                var charts = JsonMini.Arr(parsed, "charts");
                Check("parsed charts count", charts != null && charts.Count == 60);
            }

            Console.WriteLine("rows=" + res.Rows.Count + " mastered=" + cands.Count + " ap=" + apCands.Count
                + " ab=[" + res.Ability[0] + "," + res.Ability[1] + "] lo/hi=[" + res.Lo + "," + res.Hi + "]");
            Console.WriteLine("fail=" + _fail);
            Environment.Exit(_fail == 0 ? 0 : 1);
        }
    }
}
