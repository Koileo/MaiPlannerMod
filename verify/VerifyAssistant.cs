// Exercise the real assistant startup against controllable game responses.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MaiPlanner
{
    internal static class VerifyAssistant
    {
        static void Check(bool condition, string name)
        {
            if (!condition) throw new Exception(name);
            Console.WriteLine("PASS " + name);
        }

        static void Main()
        {
            var previous = Environment.CurrentDirectory;
            var sandbox = Path.Combine(Path.GetTempPath(), "MaiPlanner-start-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sandbox);
            try
            {
                Environment.CurrentDirectory = sandbox;
                Check(!JsonMini.Bool(JsonMini.ParseObj(Assistant.Start("0", "push", null)), "ok"), "empty table rejects startup");
                UserFiles.WriteText("0", "assistant.json", "{\"cards\":[{\"pool\":\"unplayed\"}],\"preview\":[{\"internalId\":10146,\"chartid\":3,\"title\":\"Test\"},{},{ }]}");
                foreach (var response in new[] { "当前不在选曲界面", "游戏正在切换画面, 请稍后再点", "找不到这首歌: 10146", "SUPERSEDED:被取代", null })
                {
                    SelectDriver.Result = response;
                    var result = JsonMini.ParseObj(Assistant.Start("0", "push", null));
                    Check(!JsonMini.Bool(result, "ok") && !Assistant.Running, "failed jump cannot start: " + (response ?? "timeout"));
                    Check(Assistant.LogTail().Contains("启动失败：" + (response ?? "点歌超时")), "failure remains in server log");
                }
                SelectDriver.Result = "已跳转: Test [DX MASTER]";
                var started = JsonMini.ParseObj(Assistant.Start("0", "push", null));
                Check(JsonMini.Bool(started, "ok") && JsonMini.Bool(started, "running") && Assistant.Running, "successful jump returns running immediately");
                Check(!JsonMini.Bool(JsonMini.ParseObj(Assistant.Start("0", "push", null)), "ok"), "duplicate startup rejected");
                Check(JsonMini.Bool(JsonMini.ParseObj(Assistant.Repush("0")), "ok"), "repush sends the running player's head");
                Check(!JsonMini.Bool(JsonMini.ParseObj(Assistant.Repush("1")), "ok"), "repush cannot use a different player");
                SelectDriver.Result = "当前不在选曲界面";
                Check(!JsonMini.Bool(JsonMini.ParseObj(Assistant.Repush("0")), "ok") && Assistant.Running, "repush returns actual failure without discarding the queue");
                Check(!JsonMini.Bool(JsonMini.ParseObj(Assistant.Stop()), "running") && !Assistant.Running, "stop returns stopped immediately");
                Check(!JsonMini.Bool(JsonMini.ParseObj(Assistant.Repush("0")), "ok"), "stopped assistant cannot repush");
                SelectDriver.Result = null;
                Check(JsonMini.Bool(JsonMini.ParseObj(Assistant.Start("0", "score", null)), "running"), "score-only startup needs no jump");
                Assistant.Stop();
            }
            finally { Environment.CurrentDirectory = previous; }
        }
    }

    internal static class SelectDriver
    {
        internal static string Result;
        internal static string Enqueue(int id, int chart, int kind) => Result;
    }
    internal static class GameSnapshot
    {
        internal sealed class Song { internal string Title; internal int DataId; internal string[] LevelText; }
        internal static string LevelTextOf(int id, int chart) => "";
    }
    internal static class WebApi
    {
        internal static GameSnapshot.Song FindSong(int id) => null;
        internal static string DiffName(int chart) => "master";
        internal static void AppendAssistantSong(StringBuilder sb, int id, int chart, string user) => sb.Append("null");
    }
    internal static class Analysis
    {
        internal sealed class Doc { internal string json; }
        internal sealed class Result { internal Dictionary<string, double> TagUPattern, TagUEval; }
        internal static Result Snapshot(string user) => null;
        internal static Doc Json(string user, bool run) => new Doc();
    }
    internal static class PlayerStore
    {
        internal sealed class Player { internal bool IsEntry; internal int Rating; }
        internal static Player Get(int index) => null;
    }
    internal static class Versions { internal static string[] Order = Array.Empty<string>(); }
    internal static class Pools
    {
        internal static int Count(string user, Dictionary<string, object> card) => 0;
        internal static Dictionary<string, object> Draw(string user, Dictionary<string, object> card, List<object> recent, string analysis) => null;
    }
    internal sealed class PoolSpec
    {
        internal string Pool, Version, PlateId;
        internal double? LevelMin, LevelMax, AchMax;
        internal int? ComboMax, SyncMax;
        internal bool ExcludeUnplayed;
        internal List<string> Difficulties = new List<string>();
    }
}
