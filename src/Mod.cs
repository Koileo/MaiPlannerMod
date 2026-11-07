using System;
using System.Reflection;
using MelonLoader;
using HarmonyLib;

[assembly: MelonInfo(typeof(MaiPlanner.Mod), "MaiPlannerMod", "1.0.3", "Koileo")]
[assembly: MelonGame("sega-interactive", "Sinmai")]
[assembly: AssemblyVersion("1.0.3.0")]
[assembly: AssemblyFileVersion("1.0.3.0")]

namespace MaiPlanner
{
    /// <summary>
    /// 合并版：点歌台网页 + 手机扫码点歌 + 跳曲 + 成绩回传 + 打歌计划器页面，全在一个 mod 里。
    /// 游戏对象只在主线程碰；HTTP 全部在后台线程，跨线程一律走 MainThread。
    /// </summary>
    public class Mod : MelonMod
    {
        internal static Mod Instance;

        /// <summary>最近一次调用跳曲的对方 IP</summary>
        internal static string LastCallerIp = "";

        /// <summary>/callback 临时指定的回传 URL</summary>
        internal static string CallbackOverride = "";

        private static volatile bool _ready;
        private static bool _updateErrorLogged;
        private static int _snapshotAt;
        private static bool _snapshotBuilt;

        public override void OnInitializeMelon()
        {
            Instance = this;
            MainThread.Init();
            Config.Load();
            Analysis.TagProvider = CommunityTags.ForChart;
            Analysis.TagRevision = 1;
            try
            {
                HarmonyInstance.PatchAll(typeof(Patch_SelectOnStart));
                HarmonyInstance.PatchAll(typeof(Patch_SelectOnRelease));
                ModLog.Info("Harmony 已挂: MusicSelectProcess.OnStart / OnRelease");
            }
            catch (Exception e)
            {
                MelonLogger.Error("挂 Harmony 失败: " + e.Message);
            }
        }

        public override void OnLateInitializeMelon()
        {
            if (Config.Enable)
            {
                WebServer.Start(Config.Port);
            }
        }

        public override void OnApplicationQuit()
        {
            WebServer.Stop();
        }

        public override void OnUpdate()
        {
            try
            {
                Tick();
            }
            catch (Exception e)
            {
                if (!_updateErrorLogged)
                {
                    _updateErrorLogged = true;
                    MelonLogger.Error("OnUpdate 异常(后续不再重复报): " + e.Message);
                }
            }
        }

        internal static bool Ready { get { return _ready; } }

        private static bool GameReady()
        {
            if (_ready)
            {
                return true;
            }
            try
            {
                var dm = MAI2.Util.Singleton<Manager.DataManager>.Instance;
                if (dm == null || !dm.IsLoaded())
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
            _ready = true;
            return true;
        }

        private void Tick()
        {
            if (!GameReady())
            {
                return;
            }

            MainThread.Pump();
            SelectDriver.UpdateCache();
            SelectDriver.RunPending();
            ScoreReporter.Tick();
            PlayerStore.Tick();

            // 曲目表就绪后建一次快照。建不起来（没进过选曲界面）就每秒再试，别每帧全量扫。
            if (!_snapshotBuilt)
            {
                var now = Environment.TickCount;
                if (_nextSnapshotAt == 0 || unchecked(now - _nextSnapshotAt) >= 0)
                {
                    _nextSnapshotAt = now + 1000;
                    SongTable.Tick();
                    if (SongTable.Rev > 0)
                    {
                        GameSnapshot.Rebuild();
                        _snapshotBuilt = GameSnapshot.Loaded;
                        if (_snapshotBuilt)
                        {
                            ModLog.Always("[点歌] 曲库快照就绪: " + GameSnapshot.Count + " 首");
                        }
                    }
                }
            }
        }

        private static int _nextSnapshotAt;
    }
}
