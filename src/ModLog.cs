// 用途：日志（与上游同名同义，供 SelectDriver/MainThread 原样调用）。
// 状态：现行。
using System.Collections.Generic;
using MelonLoader;

namespace MaiPlanner
{
    internal static class ModLog
    {
        private static readonly HashSet<string> _warned = new HashSet<string>();
        private static readonly HashSet<string> _errored = new HashSet<string>();

        /// <summary>默认显示（只留启动地址这类）</summary>
        internal static void Always(string msg)
        {
            MelonLogger.Msg(msg);
        }

        /// <summary>详细日志=true 才输出</summary>
        internal static void Info(string msg)
        {
            if (Config.VerboseLog)
            {
                MelonLogger.Msg(msg);
            }
        }

        /// <summary>同一条只提示一次，防刷屏</summary>
        internal static void WarnOnce(string msg)
        {
            lock (_warned)
            {
                if (!_warned.Add(msg))
                {
                    return;
                }
            }
            MelonLogger.Warning(msg);
        }

        internal static void ErrorOnce(string msg)
        {
            lock (_errored)
            {
                if (!_errored.Add(msg))
                {
                    return;
                }
            }
            MelonLogger.Error(msg);
        }
    }
}
