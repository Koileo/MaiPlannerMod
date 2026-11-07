// Analysis 纯数值函数（dynamic_top_n / _score_weight / quantile）的独立验证。
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

        // Python 版 dynamic_top_n 的逐行照抄
        private static int PyTopN(double? level)
        {
            if (level == null)
            {
                return 50;
            }
            double x = level.Value;
            if (x <= 12.5)
            {
                return 50;
            }
            if (x >= 14.0)
            {
                return 200;
            }
            if (x <= 13.0)
            {
                return (int)Math.Round(50 + 72 * (x - 12.5), MidpointRounding.AwayFromZero);
            }
            if (x <= 13.5)
            {
                return (int)Math.Round(86 + 84 * (x - 13.0), MidpointRounding.AwayFromZero);
            }
            return (int)Math.Round(128 + 144 * (x - 13.5), MidpointRounding.AwayFromZero);
        }

        // Python 版 _score_weight 的逐行照抄
        private static double PyWeight(double ach)
        {
            var pts = new double[][]
            {
                new double[] { 0.0, 1.5 }, new double[] { 98.0, 1.5 }, new double[] { 99.0, 1.0 },
                new double[] { 100.0, 0.5 }, new double[] { 100.4, 0.0 }, new double[] { 100.6, 0.0 },
                new double[] { 101.0, -1.0 }, new double[] { 102.0, -1.0 },
            };
            if (ach <= pts[0][0])
            {
                return pts[0][1];
            }
            for (int i = 0; i + 1 < pts.Length; i++)
            {
                double x0 = pts[i][0], y0 = pts[i][1], x1 = pts[i + 1][0], y1 = pts[i + 1][1];
                if (x0 <= ach && ach <= x1)
                {
                    if (x1 == x0)
                    {
                        return y1;
                    }
                    return y0 + (ach - x0) * (y1 - y0) / (x1 - x0);
                }
            }
            return pts[pts.Length - 1][1];
        }

        private static int Main()
        {
            // dynamic_top_n：表里每一段的端点 + 段内点 + 空值
            var anchors = new double?[]
            {
                null, 0.0, 10.0, 12.4, 12.5, 12.500001, 12.6, 12.75, 12.9, 12.999999,
                13.0, 13.000001, 13.2, 13.4, 13.5, 13.500001, 13.6, 13.75, 13.8, 13.999999,
                14.0, 14.2, 14.8, 15.0, 20.0,
            };
            foreach (var a in anchors)
            {
                Check("topN " + a, Analysis.DynamicTopN(a), PyTopN(a));
            }
            // md 的示例：13.8 → 171（128 + 144×0.3 = 171.2 → 171）
            Check("topN md example", Analysis.DynamicTopN(13.8), 171);
            Check("topN 13.0", Analysis.DynamicTopN(13.0), 86);
            Check("topN 12.5", Analysis.DynamicTopN(12.5), 50);
            Check("topN 14.0", Analysis.DynamicTopN(14.0), 200);

            // _score_weight：100.5 是 0 点，低分正 / 高分负
            var achs = new double[]
            {
                0.0, 50.0, 97.9, 98.0, 98.5, 99.0, 99.5, 99.9, 100.0, 100.2, 100.4, 100.5,
                100.6, 100.7, 100.8, 100.9, 101.0, 101.5, 102.0, 105.0,
            };
            foreach (var a in achs)
            {
                Check("weight " + a, Analysis.ScoreWeight(a), PyWeight(a));
            }
            Check("weight 100.5 zero", Analysis.ScoreWeight(100.5), 0.0);
            Check("weight 99.5", Analysis.ScoreWeight(99.5), 0.75);

            // md 2.4 例子：99.5% / 20 局 → 0.2 × 0.75 = 0.15（约 +0.1~0.15）
            Check("md 2.4 example", Math.Round(0.2 * Analysis.ScoreWeight(99.5), 4), 0.15);

            // 截断：单谱总修正 ±0.3
            Check("cap +", Math.Max(-0.3, Math.Min(0.3, 5.0)), 0.3);
            Check("cap -", Math.Max(-0.3, Math.Min(0.3, -5.0)), -0.3);

            Console.WriteLine("fail=" + _fail);
            return _fail == 0 ? 0 : 1;
        }
    }
}
