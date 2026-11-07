// Rating.cs 的独立验证：期望值由 Python 语义在此文件内独立重写（不调 Rating.cs 的任何实现）。
using System;
using System.Collections.Generic;
using System.Globalization;

namespace MaiPlanner
{
    internal static class Verify
    {
        // 直接从 lib/rating.py 抄的 COEF_TABLE（Python 元组原样）
        private static readonly double[][] PyTable =
        {
            new double[] { 0.0, 0.0 },
            new double[] { 10.0, 1.6 },
            new double[] { 20.0, 3.2 },
            new double[] { 30.0, 4.8 },
            new double[] { 40.0, 6.4 },
            new double[] { 50.0, 8.0 },
            new double[] { 60.0, 9.6 },
            new double[] { 70.0, 11.2 },
            new double[] { 75.0, 12.0 },
            new double[] { 79.9999, 12.8 },
            new double[] { 80.0, 13.6 },
            new double[] { 90.0, 15.2 },
            new double[] { 94.0, 16.8 },
            new double[] { 96.9999, 17.6 },
            new double[] { 97.0, 20.0 },
            new double[] { 98.0, 20.3 },
            new double[] { 98.9999, 20.6 },
            new double[] { 99.0, 20.8 },
            new double[] { 99.5, 21.1 },
            new double[] { 99.9999, 21.4 },
            new double[] { 100.0, 21.6 },
            new double[] { 100.4999, 22.2 },
            new double[] { 100.5, 22.4 },
        };

        private static readonly string[] PyRanks =
        {
            "D", "D", "D", "D", "D", "C", "B", "BB", "BBB", "BBB",
            "A", "AA", "AAA", "AAA", "S", "S+", "S+", "SS", "SS+", "SS+",
            "SSS", "SSS", "SSS+",
        };

        // Python 实现的逐行照抄（含 min(CAP, ach) 与 int() 向零截断）
        private static int PyRating(double ds, double ach, bool ap)
        {
            int idx = PyTable.Length - 1;
            for (int i = 0; i < PyTable.Length; i++)
            {
                if (i == PyTable.Length - 1 || ach < PyTable[i + 1][0])
                {
                    idx = i;
                    break;
                }
            }
            double coef = PyTable[idx][1];
            double eff = Math.Min(100.5, ach);
            int v = (int)(coef * ds * eff / 100.0);
            return v + (ap ? 1 : 0);
        }

        private static int Main()
        {
            var achs = new double[]
            {
                0.0, 0.5, 9.9999, 10.0, 19.9999, 20.0, 30.0, 39.5, 40.0, 49.9, 50.0, 59.9, 60.0, 69.9, 70.0, 74.9,
                75.0, 79.0, 79.9998, 79.9999, 80.0, 89.9, 90.0, 93.9, 94.0, 95.0, 96.9998, 96.9999, 97.0, 97.5,
                98.0, 98.5, 98.9998, 98.9999, 99.0, 99.4, 99.5, 99.9998, 99.9999, 100.0, 100.4998, 100.4999,
                100.5, 100.6, 101.0, 102.5, 105.0,
            };
            var levels = new double[] { 1.0, 3.5, 7.0, 9.7, 11.2, 12.5, 13.0, 13.2, 13.6, 13.7, 13.8, 14.0, 14.5, 14.8, 15.0 };

            int fail = 0, total = 0;
            foreach (var ach in achs)
            {
                double c1;
                string r1;
                Rating.Coefficient(ach, out c1, out r1);
                // 期望
                int idx = PyTable.Length - 1;
                for (int i = 0; i < PyTable.Length; i++)
                {
                    if (i == PyTable.Length - 1 || ach < PyTable[i + 1][0])
                    {
                        idx = i;
                        break;
                    }
                }
                double ce = PyTable[idx][1];
                string re = PyRanks[idx];
                total++;
                if (c1 != ce || r1 != re)
                {
                    fail++;
                    Console.WriteLine("COEF MISMATCH ach=" + ach.ToString("R", CultureInfo.InvariantCulture)
                        + " got=" + c1 + "/" + r1 + " want=" + ce + "/" + re);
                }
                foreach (var lv in levels)
                {
                    foreach (var ap in new[] { false, true })
                    {
                        total++;
                        int got = Rating.Of(lv, ach, ap);
                        int want = PyRating(lv, ach, ap);
                        if (got != want)
                        {
                            fail++;
                            Console.WriteLine("RATING MISMATCH ds=" + lv + " ach=" + ach + " ap=" + ap
                                + " got=" + got + " want=" + want);
                        }
                    }
                }
            }
            Console.WriteLine("checked=" + total + " fail=" + fail);
            return fail == 0 ? 0 : 1;
        }
    }
}
