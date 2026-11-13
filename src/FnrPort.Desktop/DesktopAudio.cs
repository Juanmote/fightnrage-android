using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using FnrPort.Audio;

namespace FnrPort.Desktop;

/// <summary>Salida de audio con SDL2 (cola de audio, float estéreo).</summary>
public sealed unsafe class SdlAudioOutput : AudioOutput
{
    uint mDev;
    int mRate = 48000;
    public override int SampleRate => mRate;
    public override int BlockFrames => 512;

    public override void Open()
    {
        if (SDL.SDL_InitSubSystem(SDL.INIT_AUDIO) != 0) throw new Exception("SDL audio: " + SDL.Error());
        var want = new SDL.AudioSpec { freq = 48000, format = SDL.AUDIO_F32, channels = 2, samples = 512 };
        mDev = SDL.SDL_OpenAudioDevice(IntPtr.Zero, 0, ref want, out var have, 0);
        if (mDev == 0) throw new Exception("SDL_OpenAudioDevice: " + SDL.Error());
        mRate = have.freq;
        Console.WriteLine("[FnrPort] SDL audio: " + Marshal.PtrToStringAnsi(SDL.SDL_GetCurrentAudioDriver()));
        SDL.SDL_PauseAudioDevice(mDev, 0);
    }

    public override void Write(float[] stereo, int frames)
    {
        uint bytes = (uint)(frames * 2 * sizeof(float));
        // Mantener ~3 bloques en cola (latencia baja sin cortes).
        while (SDL.SDL_GetQueuedAudioSize(mDev) > bytes * 3) Thread.Sleep(1);
        fixed (float* p = stereo) SDL.SDL_QueueAudio(mDev, p, bytes);
    }

    protected override void OnPause() { SDL.SDL_PauseAudioDevice(mDev, 1); }
    protected override void OnResume() { SDL.SDL_PauseAudioDevice(mDev, 0); }

    public override void Close()
    {
        if (mDev != 0) { SDL.SDL_CloseAudioDevice(mDev); mDev = 0; }
    }
}

/// <summary>Para pruebas: graba lo que suena en un WAV (16 bits estéreo), a ritmo real.</summary>
public sealed class WavFileOutput : AudioOutput
{
    readonly string mPath;
    readonly NullAudioOutput mClock = new(48000);
    FileStream mFs;
    long mFrames;
    public WavFileOutput(string path) { mPath = path; }
    public override int SampleRate => 48000;

    public override void Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(mPath)));
        mFs = File.Create(mPath);
        mFs.Write(new byte[44]);
        mClock.Open();
    }

    public override void Write(float[] stereo, int frames)
    {
        var b = new byte[frames * 4];
        for (int i = 0; i < frames * 2; i++)
        {
            short v = (short)Math.Round(Math.Clamp(stereo[i], -1f, 1f) * 32767f);
            b[i * 2] = (byte)v; b[i * 2 + 1] = (byte)(v >> 8);
        }
        mFs.Write(b);
        mFrames += frames;
        mClock.Write(stereo, frames);
    }

    public override void Close()
    {
        if (mFs == null) return;
        long data = mFrames * 4;
        mFs.Position = 0;
        var w = new BinaryWriter(mFs);
        w.Write("RIFF"u8); w.Write((int)(36 + data)); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(48000); w.Write(48000 * 4); w.Write((short)4); w.Write((short)16);
        w.Write("data"u8); w.Write((int)data);
        w.Flush();
        mFs.Dispose(); mFs = null;
        Console.WriteLine($"[FnrPort] Audio grabado en {mPath} ({mFrames / 48000.0:0.0} s)");
    }
}
