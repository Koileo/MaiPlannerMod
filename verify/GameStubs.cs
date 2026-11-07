// 端到端用的游戏侧替身：曲库/成绩是可写的假全局，PlayerStore.Get 返回假玩家。
using System;
using System.Collections.Generic;

namespace MaiPlanner
{
    internal static class GameSnapshot
    {
        internal sealed class Song
        {
            public int InternalId;
            public int DataId;
            public string Title = "";
            public string Artist = "";
            public string Version = "";
            public int Category;
            public bool Disable;
            public string[] LevelText = new string[5];
            public int[] LevelValue = new int[5];
            public string[] Designer = new string[5];
            public int[] NoteCount = new int[5];
            public bool[] Enabled = new bool[5];
        }

        internal static readonly List<Song> _songs = new List<Song>();
        internal static bool Loaded { get { return true; } }
        internal static int Count { get { return _songs.Count; } }
        internal static List<Song> Songs { get { return _songs; } }
        internal static long BuiltAt { get { return 1; } }
    }

    internal static class PlayerStore
    {
        internal sealed class Sheet
        {
            public int ChartId;
            public uint Achievement;
            public int Rank;
            public int Combo;
            public int Sync;
            public uint PlayCount;
            public uint DeluxScore;
        }

        internal sealed class Player
        {
            public int Index;
            public string Name = "";
            public bool IsEntry;
            public int Rating;
            public readonly Dictionary<int, Dictionary<int, Sheet>> Scores =
                new Dictionary<int, Dictionary<int, Sheet>>();
        }

        internal static List<Player> _all = new List<Player>();

        internal static List<Player> All { get { return _all; } }

        internal static Player Get(int index)
        {
            foreach (var p in _all)
            {
                if (p.Index == index)
                {
                    return p;
                }
            }
            return null;
        }

        internal static Sheet ScoreOf(Player p, int internalId, int chartId)
        {
            if (p == null)
            {
                return null;
            }
            Dictionary<int, Sheet> byChart;
            if (p.Scores.TryGetValue(internalId, out byChart))
            {
                Sheet s;
                if (byChart.TryGetValue(chartId, out s))
                {
                    return s;
                }
            }
            return null;
        }
    }
}
