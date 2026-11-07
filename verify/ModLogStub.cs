// 验证用最小替身：ModLog 在 net8 下没有 MelonLoader。
using System;
using System.Collections.Generic;

namespace MaiPlanner
{
    internal static class ModLog
    {
        internal static void Always(string msg) { }
        internal static void Info(string msg) { }
        internal static void WarnOnce(string msg) { Console.WriteLine("[warn] " + msg); }
        internal static void ErrorOnce(string msg) { Console.WriteLine("[error] " + msg); }
    }
}
