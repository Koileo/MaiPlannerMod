// 只模拟资源所有权与主线程边界；真实 Unity 解码仍需游戏内验证。
using System;
using System.Collections.Generic;
using System.Net;

internal sealed class AssetManager
{
    internal static readonly AssetManager Current = new AssetManager();
    internal readonly Dictionary<string, UnityEngine.Texture2D> Textures = new Dictionary<string, UnityEngine.Texture2D>();
    internal int Reads;
    public static AssetManager Instance() { return Current; }
    public T LoadAsset<T>(string path) where T : UnityEngine.Object
    {
        Reads++;
        UnityEngine.Texture2D texture;
        return Textures.TryGetValue(path, out texture) ? texture as T : null;
    }
}

namespace MaiPlanner
{
    internal static class WebApi
    {
        internal static void Json(HttpListenerContext context, int status, string json) { throw new NotSupportedException(); }
    }
}

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public static void Destroy(Object value) { value.Destroyed = true; }
    }
    public enum TextureFormat { RGBA32, ARGB32, RGB24, DXT1 }
    public enum RenderTextureFormat { ARGB32 }
    public enum RenderTextureReadWrite { sRGB }
    public class Texture2D : Object
    {
        public readonly int width, height;
        public bool isReadable = true;
        public readonly TextureFormat format;
        public Texture2D(int w, int h, TextureFormat f, bool mipmap) { width = w; height = h; format = f; }
        public void ReadPixels(Rect rect, int x, int y) { }
        public void Apply() { }
    }
    public struct Rect { public Rect(int x, int y, int w, int h) { } }
    public class RenderTexture : Object
    {
        public static RenderTexture active;
        public static int Outstanding;
        public static RenderTexture GetTemporary(int w, int h, int depth, RenderTextureFormat format, RenderTextureReadWrite readWrite)
        { Outstanding++; return new RenderTexture(); }
        public static void ReleaseTemporary(RenderTexture value) { Outstanding--; }
    }
    public static class Graphics
    {
        public static bool FailBlit;
        public static void Blit(Texture2D source, RenderTexture target)
        { if (FailBlit) throw new InvalidOperationException("模拟 GPU 转换失败"); }
    }
    public static class ImageConversion
    {
        public static int Encodes;
        public static byte[] EncodeToPNG(Texture2D texture)
        {
            if (texture.Destroyed || !texture.isReadable || texture.format == TextureFormat.DXT1)
                throw new InvalidOperationException("不可直接编码的贴图");
            Encodes++;
            return new byte[] { 137, 80, 78, 71 };
        }
    }
    public class AssetBundle
    {
        public static readonly Dictionary<string, Texture2D> Files = new Dictionary<string, Texture2D>();
        public static readonly List<string> Loads = new List<string>();
        public static int Unloads;
        private Texture2D _texture;
        public static AssetBundle LoadFromFile(string path, uint crc, ulong offset)
        {
            Loads.Add(path);
            Texture2D texture;
            return Files.TryGetValue(path, out texture) ? new AssetBundle { _texture = texture } : null;
        }
        public static AssetBundle LoadFromMemory(byte[] data) { return null; }
        public string[] GetAllAssetNames() { return new[] { "jacket.png" }; }
        public T LoadAsset<T>(string name) where T : Object { return _texture as T; }
        public T[] LoadAllAssets<T>() where T : Object { return new[] { _texture as T }; }
        public void Unload(bool destroy) { Unloads++; if (destroy) Object.Destroy(_texture); }
    }
}
