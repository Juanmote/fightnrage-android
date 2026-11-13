using System;
using Android.Media;
using FnrPort.Audio;

namespace FnrPort.Droid;

/// <summary>Salida de audio de Android: AudioTrack en modo stream, float estéreo, a la frecuencia nativa del dispositivo.</summary>
public sealed class AudioTrackOutput : AudioOutput
{
    AudioTrack mTrack;
    int mRate = 48000;
    int mBlock = 480;

    public override int SampleRate => mRate;
    public override int BlockFrames => mBlock;

    public override void Open()
    {
        try
        {
            int native = AudioTrack.GetNativeOutputSampleRate(Android.Media.Stream.Music);
            if (native >= 8000 && native <= 192000) mRate = native;
        }
        catch { }
        mBlock = mRate / 100; // bloques de 10 ms
        int min = AudioTrack.GetMinBufferSize(mRate, ChannelOut.Stereo, Encoding.PcmFloat);
        int bytes = Math.Max(min * 2, mBlock * 2 * sizeof(float) * 4);
        mTrack = new AudioTrack.Builder()
            .SetAudioAttributes(new AudioAttributes.Builder()
                .SetUsage(AudioUsageKind.Game)
                .SetContentType(AudioContentType.Music)
                .Build())
            .SetAudioFormat(new AudioFormat.Builder()
                .SetEncoding(Encoding.PcmFloat)
                .SetSampleRate(mRate)
                .SetChannelMask(ChannelOut.Stereo)
                .Build())
            .SetBufferSizeInBytes(bytes)
            .SetTransferMode(AudioTrackMode.Stream)
            .Build();
        if (mTrack.State != AudioTrackState.Initialized) throw new Exception("AudioTrack no se inicializó");
        Console.WriteLine($"[FnrPort] AudioTrack: {mRate} Hz, búfer {bytes} bytes (mínimo {min})");
        mTrack.Play();
    }

    public override void Write(float[] stereo, int frames)
    {
        int total = frames * 2, off = 0;
        while (off < total)
        {
            int n = mTrack.Write(stereo, off, total - off, WriteMode.Blocking);
            if (n <= 0) throw new Exception("AudioTrack.write devolvió " + n);
            off += n;
        }
    }

    protected override void OnPause() => mTrack?.Pause();
    protected override void OnResume() => mTrack?.Play();

    public override void Close()
    {
        if (mTrack == null) return;
        try { mTrack.Stop(); } catch { }
        mTrack.Release();
        mTrack = null;
    }
}
