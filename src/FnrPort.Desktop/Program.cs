using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace FnrPort.Desktop;

/// <summary>
/// Lanzador de escritorio para probar el port en PC.
///   FnrPort.Desktop &lt;carpeta_del_juego&gt; [--scale N] [--size WxH] [--fullscreen]
///                   [--shots 60,300] [--keys 120:Enter,200:Down] [--frames N] [--out carpeta]
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        NativeLibrary.SetDllImportResolver(typeof(GL).Assembly, ResolveNative);
        NativeLibrary.SetDllImportResolver(typeof(Program).Assembly, ResolveNative);
        if (args.Length == 0) { Console.WriteLine("Uso: FnrPort.Desktop <carpeta del juego> [opciones]"); return 1; }

        string gameDir = Path.GetFullPath(args[0]);
        int scale = 3, frames = 0, w = 0, h = 0;
        bool fullscreen = false;
        string outDir = Path.GetFullPath("capturas");
        var shots = new HashSet<long>();
        var keys = new List<(long frame, int key, bool down)>();
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--scale": scale = int.Parse(args[++i]); break;
                case "--fullscreen": fullscreen = true; break;
                case "--frames": frames = int.Parse(args[++i]); break;
                case "--out": outDir = Path.GetFullPath(args[++i]); break;
                case "--size": { var p = args[++i].Split('x'); w = int.Parse(p[0]); h = int.Parse(p[1]); break; }
                case "--shots": foreach (var s in args[++i].Split(',')) shots.Add(long.Parse(s)); break;
                case "--keys":
                    foreach (var s in args[++i].Split(','))
                    {
                        var p = s.Split(':');
                        long f = long.Parse(p[0]);
                        int k = (int)typeof(GameKey).GetField(p[1]).GetValue(null);
                        int hold = p.Length > 2 ? int.Parse(p[2]) : 6;
                        keys.Add((f, k, true)); keys.Add((f + hold, k, false));
                    }
                    break;
            }
        }

        string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "fnrport");
        GameLauncher.Prepare(gameDir, Path.Combine(data, "cache"));
        return Run(gameDir, data, scale, fullscreen, w, h, frames, outDir, shots, keys);
    }

    static int Run(string gameDir, string data, int scale, bool fullscreen, int w, int h, int frames, string outDir,
        HashSet<long> shots, List<(long frame, int key, bool down)> keys)
    {
        var host = new DesktopHost { Fullscreen = fullscreen, ForcedWidth = w, ForcedHeight = h };
        PortCore coreRef = null;
        int nextKey = 0;
        keys.Sort((a, b) => a.frame.CompareTo(b.frame));
        host.Script = input =>
        {
            long frame = coreRef?.FrameNumber ?? 0;
            while (nextKey < keys.Count && keys[nextKey].frame <= frame)
            {
                var k = keys[nextKey++];
                if (k.down) input.KeyDown(k.key); else input.KeyUp(k.key);
            }
            return frames <= 0 || frame < frames;
        };
        var opts = new GameLauncher.Options
        {
            GameDir = gameDir,
            CacheDir = Path.Combine(data, "cache"),
            SaveFile = Path.Combine(data, "savedata", "data.sav"),
            WindowScale = scale,
            ConfigureCore = c =>
            {
                var core = (PortCore)c;
                coreRef = core;
                if (shots.Count > 0)
                    core.AfterRender = pc =>
                    {
                        long frame = pc.FrameNumber + 1;
                        if (!shots.Contains(frame)) return;
                        Directory.CreateDirectory(outDir);
                        string f = Path.Combine(outDir, $"frame_{frame:D5}.png");
                        Screenshot.SaveBackBuffer(f, host.DrawableWidth, host.DrawableHeight);
                        Console.WriteLine($"[FnrPort] Captura {f} (draw calls este frame: {Renderer.DrawCalls})");
                    };
                core.AfterRender += _ => Renderer.DrawCalls = 0;
            }
        };
        return GameLauncher.Run(host, opts);
    }

    static IntPtr ResolveNative(string name, Assembly asm, DllImportSearchPath? path)
    {
        string[] candidates = name switch
        {
            "GLESv2" => new[] { "libGLESv2.so.2", "libGLESv2.so", "libGLESv2.dll", "libGLESv2" },
            "SDL2" => new[] { "libSDL2-2.0.so.0", "libSDL2.so", "SDL2.dll", "libSDL2-2.0.0.dylib" },
            _ => Array.Empty<string>()
        };
        foreach (var c in candidates)
            if (NativeLibrary.TryLoad(c, asm, path, out var h)) return h;
        return IntPtr.Zero;
    }
}

static unsafe class Screenshot
{
    public static void SaveBackBuffer(string file, int w, int h)
    {
        var px = new byte[w * h * 4];
        GL.glBindFramebuffer(GL.FRAMEBUFFER, 0);
        fixed (byte* p = px) GL.glReadPixels(0, 0, w, h, GL.RGBA, GL.UNSIGNED_BYTE, p);
        GL.glBindFramebuffer(GL.FRAMEBUFFER, Renderer.BoundFbo);
        var flipped = new byte[px.Length];
        for (int y = 0; y < h; y++) Buffer.BlockCopy(px, (h - 1 - y) * w * 4, flipped, y * w * 4, w * 4);
        using var fs = File.Create(file);
        PngCodec.Encode(fs, w, h, flipped);
    }
}
