// 用途：版本主表 / 别名 / DX rating 可用版本 —— server、fusion、analysis、dxrating 共用。
//       本文件是 lib/versions.py 的等价物：版本归一化、先后排序、脏名修复表。
// 状态：现行（与源项目 lib/versions.py 一一对应；主表顺序即版本先后，不得改动）。
using System;
using System.Collections.Generic;

namespace MaiPlanner
{
    internal static class Versions
    {
        // 历史脏名 / 本地原始名 → 规范名。只放「确实存在」的别名；未知值原样返回。
        private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "maimaiでらっくす", "DX" },
            { "maimaiでらっくす PLUS", "DX PLUS" },
            { "maimaiでらっくすPLUS", "DX PLUS" },
            { "maimaDX", "DX" },
            { "maimaDXPLUS", "DX PLUS" },
            { "maimai DX", "DX" },
            { "maimai DX PLUS", "DX PLUS" },
            { "CiRCLEPLUS", "CiRCLE PLUS" },
        };

        // 版本先后（旧 → 新）；b50 的「新曲池」= 最新两个版本。
        internal static readonly string[] Order =
        {
            "maimai", "maimai PLUS", "GreeN", "GreeN PLUS", "ORANGE", "ORANGE PLUS",
            "PiNK", "PiNK PLUS", "MURASAKi", "MURASAKi PLUS", "MiLK", "MiLK PLUS", "FiNALE",
            "DX", "DX PLUS", "Splash", "Splash PLUS", "UNiVERSE", "UNiVERSE PLUS",
            "FESTiVAL", "FESTiVAL PLUS", "BUDDiES", "BUDDiES PLUS",
            "PRiSM", "PRiSM PLUS", "CiRCLE", "CiRCLE PLUS", "MAGiCAL",
        };

        /// <summary>新曲池大小：最新两个版本算「新曲」。</summary>
        internal const int NewPoolSize = 2;

        /// <summary>从这一版起 B15 计「本版本 + 上一版本」；更早只算本版本。</summary>
        internal const string B15DoubleFrom = "CiRCLE";

        private static readonly string[] RatingVersionArray = Slice(IndexOf("DX"));
        private static List<string> _ratingVersions;

        /// <summary>可选「当前版本」列表（DX 时代起，旧 → 新）。</summary>
        internal static List<string> RatingVersions
        {
            get
            {
                if (_ratingVersions == null)
                {
                    _ratingVersions = new List<string>(RatingVersionArray);
                }
                return _ratingVersions;
            }
        }

        /// <summary>版本在总表里的下标；未知回 -1。</summary>
        internal static int IndexOf(string value)
        {
            var name = Canonical(value);
            for (int i = 0; i < Order.Length; i++)
            {
                if (string.Equals(Order[i], name, StringComparison.Ordinal))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>别名 / 脏名 → 规范名；未知值去空白后原样返回（null → ""）。</summary>
        internal static string Canonical(string value)
        {
            if (value == null)
            {
                return "";
            }
            var name = value.Trim();
            string hit;
            return Aliases.TryGetValue(name, out hit) ? hit : name;
        }

        /// <summary>兼容旧调用名；等价于 Canonical。</summary>
        internal static string Fix(string value)
        {
            return Canonical(value);
        }

        /// <summary>该版本是否 DX 时代（可作为当前版本）。</summary>
        internal static bool IsRatingVersion(string value)
        {
            var name = Canonical(value);
            for (int i = 0; i < RatingVersionArray.Length; i++)
            {
                if (string.Equals(RatingVersionArray[i], name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>最新一个 DX 时代版本（rating 页缺省版本）。</summary>
        internal static string DefaultRatingVersion
        {
            get
            {
                return RatingVersionArray.Length > 0
                    ? RatingVersionArray[RatingVersionArray.Length - 1]
                    : "MAGiCAL";
            }
        }

        /// <summary>版本是否属于新曲池（最新两个版本以内）。</summary>
        internal static bool IsNewSongVersion(string value)
        {
            return IndexOf(value) >= Order.Length - NewPoolSize;
        }

        /// <summary>
        /// 按库里出现过的版本推出当前版本，out 新曲池版本集合。
        /// 库里一个认识的版本都没有时返回 "" 和空集。
        /// </summary>
        internal static string NewSongVersions(IEnumerable<string> versions, out HashSet<string> newVersions)
        {
            var presentNames = new HashSet<string>(StringComparer.Ordinal);
            if (versions != null)
            {
                foreach (var v in versions)
                {
                    presentNames.Add(Canonical(v));
                }
            }
            int last = -1;
            for (int i = 0; i < Order.Length; i++)
            {
                if (presentNames.Contains(Order[i]))
                {
                    last = i;
                }
            }
            newVersions = new HashSet<string>(StringComparer.Ordinal);
            if (last < 0)
            {
                return "";
            }
            for (int i = Math.Max(0, last - NewPoolSize + 1); i <= last; i++)
            {
                newVersions.Add(Order[i]);
            }
            return Order[last];
        }

        /// <summary>按版本先后排序（旧 → 新）；未知版本排最后、彼此保持原序。</summary>
        internal static List<string> SortByAge(IEnumerable<string> versions)
        {
            var list = new List<string>();
            if (versions == null)
            {
                return list;
            }
            var known = new List<string>();
            var unknown = new List<string>();
            foreach (var v in versions)
            {
                if (IndexOf(v) >= 0)
                {
                    known.Add(Canonical(v));
                }
                else
                {
                    unknown.Add(v);
                }
            }
            known.Sort(delegate (string a, string b) { return IndexOf(a).CompareTo(IndexOf(b)); });
            list.AddRange(known);
            list.AddRange(unknown);
            return list;
        }

        private static string[] Slice(int from)
        {
            if (from < 0)
            {
                from = 0;
            }
            var list = new string[Order.Length - from];
            Array.Copy(Order, from, list, 0, list.Length);
            return list;
        }
    }
}
