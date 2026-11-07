// Offline community tag snapshot. The build script resolves public sheet IDs to exact game IDs.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace MaiPlanner
{
    internal static class CommunityTags
    {
        private static readonly Lazy<Dictionary<string, List<AnalysisTag>>> Catalog =
            new Lazy<Dictionary<string, List<AnalysisTag>>>(Load);

        private static Dictionary<string, List<AnalysisTag>> Load()
        {
            var result = new Dictionary<string, List<AnalysisTag>>(StringComparer.Ordinal);
            var doc = JsonMini.ParseObj(EmbeddedWeb.ReadText("community-tags.json"));
            var tags = JsonMini.Arr(doc, "tags");
            var charts = JsonMini.Obj(doc, "charts");
            if (tags == null || charts == null)
            {
                ModLog.WarnOnce("社区标签数据缺失，能力画像暂不可用");
                return result;
            }
            foreach (var chart in charts)
            {
                var indices = chart.Value as List<object>;
                if (indices == null) continue;
                var list = new List<AnalysisTag>();
                foreach (var raw in indices)
                {
                    int index = Convert.ToInt32(raw, CultureInfo.InvariantCulture);
                    if (index < 0 || index >= tags.Count) continue;
                    var tag = tags[index] as Dictionary<string, object>;
                    var name = JsonMini.Str(tag, "name");
                    var group = JsonMini.Str(tag, "group");
                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(group))
                        list.Add(new AnalysisTag { Name = name, Group = group });
                }
                result[chart.Key] = list;
            }
            ModLog.Info("社区标签: " + result.Count + " 张谱面");
            return result;
        }

        internal static List<AnalysisTag> ForChart(int internalId, int chartId)
        {
            List<AnalysisTag> tags;
            return Catalog.Value.TryGetValue(UserFiles.Key(internalId, chartId), out tags)
                ? new List<AnalysisTag>(tags) : new List<AnalysisTag>();
        }
    }
}
