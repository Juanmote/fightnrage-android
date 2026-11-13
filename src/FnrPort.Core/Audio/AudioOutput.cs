using System;
using System.Diagnostics;
using System.Threading;

namespace FnrPort.Audio;

/// <summary>
/// Salida de audio de la plataforma (AudioTrack en Android, SDL en PC...).
/// Recibe bloques de audio estéreo en float intercalado (L, R, L, R...) desde el hilo del mezclador.
/// No usa ningún tipo del juego, así que se puede crear antes de cargar el exe.
/// </summary>
public abstract class AudioOutput
{
    readonly object mLock = new();
    bool mPaused;

    /// <summary>Frecuencia de salida (Hz). Válida tras <see cref="Open"/>.</summary>
    public abstract int SampleRate { get; }
    /// <summary>Tamaño del bloque que el mezclador genera en cada vuelta.</summary>
    public virtual int BlockFrames => 512;
    public abstract void Open();
    /// <summary>Escribe <paramref name="frames"/> frames estéreo. Debe bloquear hasta que haya hueco (marca el ritmo).</summary>
    public abstract void Write(float[] stereo, int frames);
    protected virtual void OnPause() { }
    protected virtual void OnResume() { }
    public abstract void Close();

    /// <summary>Pausa/reanuda la salida (la app pasa a segundo plano). Se puede llamar desde cualquier hilo.</summary>
    public void SetPaused(bool paused)
    {
        lock (mLock) { mPaused = paused; Monitor.PulseAll(mLock); }
    }

    public bool IsPaused { get { lock (mLock) return mPaused; } }

    /// <summary>Si está en pausa, para la salida y espera a que se reanude (o a que <paramref name="running"/> sea false).</summary>
    internal void WaitWhilePaused(Func<bool> running)
    {
        lock (mLock)
        {
            if (!mPaused) return;
            try { OnPause(); } catch (Exception ex) { Console.WriteLine("[FnrPort] Audio pausa: " + ex.Message); }
            while (mPaused && running()) Monitor.Wait(mLock, 200);
        }
        try { OnResume(); } catch (Exception ex) { Console.WriteLine("[FnrPort] Audio reanudar: " + ex.Message); }
    }

    internal void Wake() { lock (mLock) Monitor.PulseAll(mLock); }
}

/// <summary>Salida muda que consume el audio a ritmo real (si no hay dispositivo de sonido).</summary>
public sealed class NullAudioOutput : AudioOutput
{
    readonly int mRate;
    readonly Stopwatch mClock = new();
    long mFramesWritten;
    public NullAudioOutput(int rate = 48000) { mRate = rate; }
    public override int SampleRate => mRate;
    public override void Open() { mClock.Restart(); mFramesWritten = 0; }
    public override void Write(float[] stereo, int frames)
    {
        mFramesWritten += frames;
        double ahead = (double)mFramesWritten / mRate - mClock.Elapsed.TotalSeconds;
        if (ahead > 0.02) Thread.Sleep((int)((ahead - 0.01) * 1000));
    }
    protected override void OnResume() { mClock.Restart(); mFramesWritten = 0; }
    public override void Close() { }
}
