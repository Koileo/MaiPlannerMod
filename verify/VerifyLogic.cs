// Versions / Blacklist / Pools 纯逻辑的独立验证（不碰 Unity）。
using System;
using System.Collections.Generic;
using System.Globalization;

namespace MaiPlanner
{
    internal static class Verify
    {
        private static int _fail;

        private static void Check(string what, object got, object want)
        {
            var g = Convert.ToString(got, CultureInfo.InvariantCulture);
            var w = Convert.ToString(want, CultureInfo.InvariantCulture);
            if (g != w)
            {
                _fail++;
                Console.WriteLine("FAIL " + what + " got=" + g + " want=" + w);
            }
        }

        private static int Main()
        {
            // ── Versions 别名 ──
            Check("alias1", Versions.Canonical("maimaiでらっくす"), "DX");
            Check("alias2", Versions.Canonical("maimaDXPLUS"), "DX PLUS");
            Check("alias3", Versions.Canonical(" CiRCLEPLUS "), "CiRCLE PLUS");
            Check("alias4", Versions.Canonical("MAGiCAL"), "MAGiCAL");
            Check("alias5", Versions.Canonical(null), "");
            Check("alias6", Versions.Canonical("未知版本"), "未知版本");

            // ── 版本序 ──
            Check("order first", Versions.Order[0], "maimai");
            Check("order last", Versions.Order[Versions.Order.Length - 1], "MAGiCAL");
            Check("order len", Versions.Order.Length, 28);
            Check("idx DX", Versions.IndexOf("DX"), 13);
            Check("idx unknown", Versions.IndexOf("nope"), -1);
            Check("idx alias", Versions.IndexOf("maimaDX"), 13);
            Check("default ver", Versions.DefaultRatingVersion, "MAGiCAL");
            Check("rating ver count", Versions.RatingVersions.Count, 15);
            Check("rating ver first", Versions.RatingVersions[0], "DX");
            Check("isRating old", Versions.IsRatingVersion("FiNALE"), false);
            Check("isRating dx", Versions.IsRatingVersion("DX"), true);
            Check("isNew MAGiCAL", Versions.IsNewSongVersion("MAGiCAL"), true);
            Check("isNew CiRCLE PLUS", Versions.IsNewSongVersion("CiRCLE PLUS"), true);
            Check("isNew CiRCLE", Versions.IsNewSongVersion("CiRCLE"), false);

            // ── new_song_versions ──
            HashSet<string> nv;
            var cur = Versions.NewSongVersions(new[] { "maimai", "DX", "CiRCLE", "MAGiCAL" }, out nv);
            Check("newcur", cur, "MAGiCAL");
            Check("newcount", nv.Count, 2);
            Check("newhas mag", nv.Contains("MAGiCAL"), true);
            Check("newhas cpl", nv.Contains("CiRCLE PLUS"), true);
            var empty = Versions.NewSongVersions(new string[0], out nv);
            Check("new empty cur", empty, "");
            Check("new empty count", nv.Count, 0);

            // ── Blacklist：时长与有效期（规则来自 md/lib）──
            Check("dur 1d", Blacklist.ParseDuration("1d"), 1);
            Check("dur 30d", Blacklist.ParseDuration("30d"), 30);
            Check("dur 7D", Blacklist.ParseDuration(" 7D "), 7);
            Check("dur 14d", Blacklist.ParseDuration("14d"), 14);
            Check("dur forever", Blacklist.ParseDuration("forever") == null, true);
            Check("dur perm", Blacklist.ParseDuration("perm") == null, true);
            Check("dur 永久", Blacklist.ParseDuration("永久") == null, true);
            Check("dur bad", Blacklist.ParseDuration("xx"), -1);
            Check("dur empty", Blacklist.ParseDuration(null) == -1, true);

            var today = DateTime.Today;
            Check("bl today 1d active", Blacklist.IsActive(Entry("1d", today), today), true);
            Check("bl 1d expiry day", Blacklist.IsActive(Entry("1d", today.AddDays(-1)), today), true);
            Check("bl 1d expired", Blacklist.IsActive(Entry("1d", today.AddDays(-2)), today), false);
            Check("bl 7d boundary", Blacklist.IsActive(Entry("7d", today.AddDays(-7)), today), true);
            Check("bl 7d over", Blacklist.IsActive(Entry("7d", today.AddDays(-8)), today), false);
            Check("bl forever", Blacklist.IsActive(Entry("forever", today.AddDays(-999)), today), true);
            Check("bl forever no date", Blacklist.IsActive(Entry("forever", null), today), true);
            Check("bl 3d no date", Blacklist.IsActive(Entry("3d", null), today), false);
            Check("bl bad dur", Blacklist.IsActive(Entry("zz", today), today), false);

            // 兼容 until 形状（UserFiles 早期落盘）
            var until = new Dictionary<string, object>();
            until["internalId"] = 1;
            until["chartid"] = 2;
            until["until"] = ToUnix(DateTime.Now.AddDays(1));
            Check("bl until future", Blacklist.IsActive(until), true);
            var untilOld = new Dictionary<string, object>();
            untilOld["internalId"] = 1;
            untilOld["chartid"] = 2;
            untilOld["until"] = ToUnix(DateTime.Now.AddDays(-1));
            Check("bl until past", Blacklist.IsActive(untilOld), false);
            var untilForever = new Dictionary<string, object>();
            untilForever["internalId"] = 1;
            untilForever["chartid"] = 2;
            untilForever["until"] = 0L;
            Check("bl until 0 = forever", Blacklist.IsActive(untilForever), true);

            // ── Pools 潜力（公式同 Rating 的 100.0/100.5 取大）──
            Check("potential 13.7", Math.Max(Rating.Of(13.7, 100.0), Rating.Of(13.7, 100.5)), Rating.Of(13.7, 100.5));

            Console.WriteLine("fail=" + _fail);
            return _fail == 0 ? 0 : 1;
        }

        private static Dictionary<string, object> Entry(string dur, DateTime? added)
        {
            var e = new Dictionary<string, object>();
            e["internalId"] = 1;
            e["chartid"] = 2;
            e["duration"] = dur;
            if (added != null)
            {
                e["addedAt"] = added.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            return e;
        }

        private static long ToUnix(DateTime local)
        {
            return (long)(local.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }
    }
}
