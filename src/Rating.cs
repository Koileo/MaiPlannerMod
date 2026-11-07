// 用途：单曲 DX rating 的唯一实现 —— 系数表 + 公式 + 评级 + 下一档阈值。
//       本文件是 lib/rating.py 的逐行等价物，rating_calc / dxrating / analysis 共用。
// 状态：现行（与源项目 lib/rating.py 一一对应，分段边界与系数一个数都不许改）。
using System;
using System.Collections.Generic;

namespace MaiPlanner
{
    internal static class Rating
    {
        /// <summary>达成率上限：超过 100.5% 一律按 100.5% 计（101% 与 100.5% 同 rating）。</summary>
        internal const double Cap = 100.5;

        /// <summary>系数表最大值 22.4，用来把系数归一化到 0..1。</summary>
        internal const double CoefMax = 22.4;

        // (下界达成率, 系数, 评级) —— 与 maimaidx-prober 逐行一致，含 99.9999 这类哨兵行。
        // 顺序必须严格递增；查表取「下界 ≤ 达成率 < 下一行下界」的那一段。
        private static readonly double[] Floors =
        {
            0.0, 10.0, 20.0, 30.0, 40.0, 50.0, 60.0, 70.0, 75.0, 79.9999,
            80.0, 90.0, 94.0, 96.9999, 97.0, 98.0, 98.9999, 99.0, 99.5, 99.9999,
            100.0, 100.4999, 100.5,
        };

        private static readonly double[] Coefs =
        {
            0.0, 1.6, 3.2, 4.8, 6.4, 8.0, 9.6, 11.2, 12.0, 12.8,
            13.6, 15.2, 16.8, 17.6, 20.0, 20.3, 20.6, 20.8, 21.1, 21.4,
            21.6, 22.2, 22.4,
        };

        private static readonly string[] Ranks =
        {
            "D", "D", "D", "D", "D", "C", "B", "BB", "BBB", "BBB",
            "A", "AA", "AAA", "AAA", "S", "S+", "S+", "SS", "SS+", "SS+",
            "SSS", "SSS", "SSS+",
        };

        /// <summary>达成率 → (系数, 评级)。分段查表；越低达成率越先命中低段。</summary>
        internal static void Coefficient(double achievement, out double coef, out string rank)
        {
            for (int i = 0; i < Floors.Length; i++)
            {
                if (i == Floors.Length - 1 || achievement < Floors[i + 1])
                {
                    coef = Coefs[i];
                    rank = Ranks[i];
                    return;
                }
            }
            coef = Coefs[Coefs.Length - 1];
            rank = Ranks[Ranks.Length - 1];
        }

        /// <summary>只要系数、不要评级（analysis 的 rating_coef）。</summary>
        internal static double Coef(double achievement)
        {
            double coef;
            string rank;
            Coefficient(achievement, out coef, out rank);
            return coef;
        }

        /// <summary>只要评级。</summary>
        internal static string RankOf(double achievement)
        {
            double coef;
            string rank;
            Coefficient(achievement, out coef, out rank);
            return rank;
        }

        /// <summary>
        /// 单曲 rating：
        ///     int(系数 × 定数 × min(100.5, 达成率) / 100) + (AP / AP+ ? 1 : 0)
        /// int() 向零截断，C# 的 (int)double 行为一致。
        /// </summary>
        internal static int Of(double ds, double achievement, bool ap)
        {
            double coef;
            string rank;
            Coefficient(achievement, out coef, out rank);
            double eff = achievement < Cap ? achievement : Cap;
            int value = (int)(coef * ds * eff / 100.0);
            return value + (ap ? 1 : 0);
        }

        /// <summary>单曲 rating（不带 AP 加成）。</summary>
        internal static int Of(double ds, double achievement)
        {
            return Of(ds, achievement, false);
        }

        /// <summary>下一档系数的达成率下限与系数；已到顶返回 (达成率, 当前系数)。</summary>
        internal static void NextThreshold(double achievement, out double floor, out double coef)
        {
            double cur = Coef(achievement);
            for (int i = 0; i < Floors.Length; i++)
            {
                if (Floors[i] > achievement + 1e-9)
                {
                    floor = Floors[i];
                    coef = Coefs[i];
                    return;
                }
            }
            floor = achievement;
            coef = cur;
        }

        /// <summary>评级名 → 序号（D=0 … SSS+=13），与 scores_stats 的 RANK_NAMES 一致。</summary>
        internal static readonly string[] RankNames =
        {
            "D", "C", "B", "BB", "BBB", "A", "AA", "AAA",
            "S", "S+", "SS", "SS+", "SSS", "SSS+",
        };

        /// <summary>0..13 → 评级名。</summary>
        internal static string RankName(int index)
        {
            if (index < 0)
            {
                return RankNames[0];
            }
            if (index >= RankNames.Length)
            {
                return RankNames[RankNames.Length - 1];
            }
            return RankNames[index];
        }

        /// <summary>评级名 → 0..13；认不出回 0（D）。</summary>
        internal static int RankIndex(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return 0;
            }
            for (int i = 0; i < RankNames.Length; i++)
            {
                if (string.Equals(RankNames[i], name, StringComparison.Ordinal))
                {
                    return i;
                }
            }
            return 0;
        }

        /// <summary>AP / AP+ 判定：comboStatus ≥ 3（与源项目一致）。</summary>
        internal static bool IsAp(int comboStatus)
        {
            return comboStatus >= 3;
        }

        /// <summary>批量取系数（调试用）。</summary>
        internal static List<double> AllCoefs()
        {
            var list = new List<double>(Coefs.Length);
            list.AddRange(Coefs);
            return list;
        }
    }
}
