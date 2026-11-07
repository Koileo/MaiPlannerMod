using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace MaiPlanner
{
    internal static class VerifyRuntime
    {
        private static int _checks;
        private static void Check(string name, bool value)
        {
            if (!value) throw new Exception("FAIL " + name);
            _checks++;
            Console.WriteLine("PASS " + name);
        }

        // 仅用队列深度作线程同步，行为断言通过任务结果与副作用完成。
        private static void WaitQueued(int count)
        {
            var queue = (ICollection)typeof(MainThread).GetField("_queue", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var gate = typeof(MainThread).GetField("_lock", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 3000)
            {
                lock (gate) { if (queue.Count >= count) return; }
                Thread.Sleep(1);
            }
            throw new Exception("任务未入队");
        }

        private static Texture2D Texture(bool compressed = false)
        { return new Texture2D(64, 64, compressed ? TextureFormat.DXT1 : TextureFormat.RGBA32, false) { isReadable = !compressed }; }

        private static string Bundle(string root, string version, int id)
        {
            var dir = Path.Combine(root, "Sinmai_Data", "StreamingAssets", version, "AssetBundleImages", "jacket_s");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "ui_jacket_" + id.ToString("D6") + "_s.ab");
            File.WriteAllBytes(path, new byte[] { 0 });
            return path;
        }

        private static int Main()
        {
            var previous = Environment.CurrentDirectory;
            var root = Path.Combine(Path.GetTempPath(), "MaiPlanner-verify-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Environment.CurrentDirectory = root;
            try
            {
                MainThread.Init();
                Check("主线程直接执行", MainThread.Run(() => 7, 500, -1) == 7);
                int calls = 0;
                var expired = Task.Run(() => MainThread.Run(() => Interlocked.Increment(ref calls), 30, -1));
                Check("未处理任务按时返回", expired.Wait(2000) && expired.Result == -1);
                MainThread.Pump();
                Check("超时任务不会在稍后修改游戏状态", calls == 0);

                var first = Task.Run(() => MainThread.Run(() => { Thread.Sleep(12); return Interlocked.Increment(ref calls); }, 5000, -1));
                WaitQueued(1);
                var second = Task.Run(() => MainThread.Run(() => Interlocked.Increment(ref calls), 5000, -1));
                WaitQueued(2);
                MainThread.Pump();
                Check("单次慢任务后让出当前帧", first.Wait(1000) && calls == 1 && !second.IsCompleted);
                MainThread.Pump();
                Check("余下任务在下一帧继续", second.Wait(1000) && second.Result == 2);

                var oldBundle = Bundle(root, "A000", 12);
                Bundle(root, "N100", 13);
                Check("较早版本曲绘仍可发现", Jackets.ProbablyExists(12));
                var owned = Texture();
                AssetBundle.Files[oldBundle] = owned;
                Check("较早版本资源包可读取", Jackets.Get(12)?.Length > 0);
                Check("自有资源包贴图随包释放", owned.Destroyed && AssetBundle.Unloads == 1);

                var borrowed = Texture(true);
                AssetManager.Current.Textures["Jacket_S/UI_Jacket_000014_S.png"] = borrowed;
                var active = new RenderTexture();
                RenderTexture.active = active;
                Check("压缩且不可读的游戏曲绘可转码", Jackets.Get(14)?.Length > 0);
                Check("借用游戏贴图不被销毁", !borrowed.Destroyed);
                Check("GPU 状态和临时纹理均恢复", ReferenceEquals(RenderTexture.active, active) && RenderTexture.Outstanding == 0);
                int encodes = ImageConversion.Encodes;
                Check("重复请求命中 PNG 缓存", Jackets.Get(14)?.Length > 0 && ImageConversion.Encodes == encodes);

                var failed = Texture(true);
                AssetManager.Current.Textures["Jacket_S/UI_Jacket_000015_S.png"] = failed;
                Graphics.FailBlit = true;
                Check("转换失败返回空值", Jackets.Get(15) == null);
                Check("转换失败也恢复 GPU 状态且不销毁原图", !failed.Destroyed && ReferenceEquals(RenderTexture.active, active) && RenderTexture.Outstanding == 0);
                Graphics.FailBlit = false;

                var late = Task.Run(() => Jackets.Get(16));
                Check("曲绘主线程超时可返回", late.Wait(4000) && late.Result == null);
                MainThread.Pump();
                AssetManager.Current.Textures["Jacket_S/UI_Jacket_000016_S.png"] = Texture();
                Check("超时不会永久标记曲绘缺失", Jackets.Get(16)?.Length > 0);

                Parallel.For(0, 40, i => UserFiles.ToggleFavorite("test", 100 + i, 3, true));
                Check("并发收藏不丢更新", UserFiles.Favorites("test").Count == 40);
                Parallel.For(0, 40, i => UserFiles.SetBlacklist("test", 100 + i, 3, "forever", false));
                Check("并发黑名单不丢更新", UserFiles.Blacklist("test").Count == 40);
                Parallel.For(0, 40, i => UserFiles.WriteText("test", "stats.json", "{\"value\":" + i + "}"));
                Check("并发统计写入保持完整 JSON", JsonMini.ParseObj(UserFiles.ReadText("test", "stats.json")) != null);
                Check("写入完成不残留临时文件", Directory.GetFiles(UserFiles.DirOf("test"), "*.part").Length == 0);
                Console.WriteLine(_checks + "/" + _checks + " 通过（Unity 使用替身）");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            finally
            {
                Environment.CurrentDirectory = previous;
                Directory.Delete(root, true);
            }
        }
    }
}
