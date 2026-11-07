// 用途：合并版 mod 的配置。点歌 + 网页 + 扫码 + 成绩回传都在这一份里。
// 状态：现行。所有项落 Package/UserData/MelonPreferences.cfg。
using MelonLoader;

namespace MaiPlanner
{
    internal static class Config
    {
        /// <summary>整个 mod 的开关</summary>
        public static bool Enable = true;

        /// <summary>HTTP 端口（网页 + 扫码页 + 接口共用，只有一个端口）</summary>
        public static int Port = 8790;

        /// <summary>是否监听局域网（手机扫码要开；无鉴权，别在公共网络开）</summary>
        public static bool LanAccess = true;

        /// <summary>详细日志</summary>
        public static bool VerboseLog = false;

        /// <summary>跳曲后停在难度选择页（而不是直接进谱）</summary>
        public static bool JumpToDifficultyScreen = true;

        /// <summary>结算成绩回传地址；空 = 不回传</summary>
        public static string ScoreCallback = "";

        /// <summary>自动回传时对方机器上的端口</summary>
        public static int CallbackPort = 20010;

        public static void Load()
        {
            var c = MelonPreferences.CreateCategory("MaiPlanner", "打歌计划器（mod 版）");
            Enable = c.CreateEntry("启用", true, "整个 mod 的开关").Value;
            Port = c.CreateEntry("接口端口", 8790, "网页、扫码页、接口共用的端口").Value;
            LanAccess = c.CreateEntry("局域网", true, "同时监听局域网 IP，手机才能扫码（无鉴权）").Value;
            VerboseLog = c.CreateEntry("详细日志", false, "输出跳曲每一步").Value;
            JumpToDifficultyScreen = c.CreateEntry("跳到难度页", true, "跳曲后停在难度选择页").Value;
            ScoreCallback = (c.CreateEntry("回传地址", "",
                "手动覆盖回传地址；留空 = 不回传外部程序").Value ?? "").Trim();
            CallbackPort = c.CreateEntry("回传端口", 20010,
                "自动回传用的端口（对方机器上监听成绩的端口）").Value;
            _category = c;

            MelonPreferences.Save();
        }

        private static MelonPreferences_Category _category;

        /// <summary>把内存里的值写回配置项并落盘</summary>
        public static void Save()
        {
            try
            {
                var c = _category ?? MelonPreferences.CreateCategory("MaiPlanner", "打歌计划器（mod 版）");
                _category = c;
                c.CreateEntry("启用", true, "整个 mod 的开关").Value = Enable;
                c.CreateEntry("接口端口", 8790, "网页、扫码页、接口共用的端口").Value = Port;
                c.CreateEntry("局域网", true, "同时监听局域网 IP，手机才能扫码（无鉴权）").Value = LanAccess;
                c.CreateEntry("详细日志", false, "输出跳曲每一步").Value = VerboseLog;
                c.CreateEntry("跳到难度页", true, "跳曲后停在难度选择页").Value = JumpToDifficultyScreen;
                c.CreateEntry("回传地址", "", "手动覆盖回传地址；留空 = 不回传外部程序").Value = ScoreCallback;
                c.CreateEntry("回传端口", 20010, "自动回传用的端口").Value = CallbackPort;
                MelonPreferences.Save();
            }
            catch (System.Exception e)
            {
                MelonLoader.MelonLogger.Warning("保存配置失败: " + e.Message);
            }
        }
    }
}
