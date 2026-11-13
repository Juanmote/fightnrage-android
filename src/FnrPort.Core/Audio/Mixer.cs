using System;
using System.Collections.Generic;
using System.Threading;

namespace FnrPort.Audio;

/// <summary>Una voz que suena en el mezclador (equivale a un "channel" de FMOD).</summary>
public sealed class Voice
{
    internal FrameReader Reader;
    internal bool Mono;
    /// <summary>Volumen lineal (1 = normal).</summary>
    public float Volume = 1;
    /// <summary>-1 = izquierda, 0 = centro, 1 = derecha (como Channel.setPan de FMOD).</summary>
    public float Pan;
    /// <summary>Multiplicador de velocidad/tono (1 = normal).</summary>
    public float Pitch = 1;
    public bool Loop;
    public bool Paused;
    volatile bool mFinished;
    public bool Finished => mFinished;

    // estado de remuestreo (solo lo toca el hilo del mezclador)
    float pL, pR, nL, nR;
    double mFrac;
    bool mStarted, mSourceEnded, mStopRequested;
    float mGainL = -1, mGainR = -1;

    internal void RequestStop() { mStopRequested = true; }

    /// <summary>Suma <paramref name="frames"/> frames a <paramref name="mix"/>. Devuelve false cuando la voz ha terminado.</summary>
    internal bool MixInto(float[] mix, int frames, int outRate, float master)
    {
        if (mStopRequested) { Finish(); return false; }
        if (Paused) return true;
        float pitch = Pitch;
        if (!(pitch > 0)) return true; // tono 0 o NaN: congelada
        if (!mStarted)
        {
            mStarted = true;
            if (!Reader.Next(Loop, out pL, out pR)) { Finish(); return false; }
            if (!Reader.Next(Loop, out nL, out nR)) { nL = nR = 0; mSourceEnded = true; }
        }

        // Ganancias (ley de potencia constante para mono, balance para estéreo, como FMOD).
        float vol = Volume * master, pan = Math.Clamp(Pan, -1f, 1f), gl, gr;
        if (Mono)
        {
            double a = (pan + 1) * Math.PI / 4;
            gl = (float)Math.Cos(a) * vol; gr = (float)Math.Sin(a) * vol;
        }
        else
        {
            gl = (pan > 0 ? 1 - pan : 1) * vol;
            gr = (pan < 0 ? 1 + pan : 1) * vol;
        }
        if (mGainL < 0) { mGainL = gl; mGainR = gr; }
        // Rampa de ganancia a lo largo del bloque para evitar chasquidos en los fundidos.
        float dgl = (gl - mGainL) / frames, dgr = (gr - mGainR) / frames;
        float cgl = mGainL, cgr = mGainR;

        double step = (double)Reader.Rate * pitch / outRate;
        double frac = mFrac;
        for (int i = 0, o = 0; i < frames; i++, o += 2)
        {
            float f = (float)frac;
            mix[o] += (pL + (nL - pL) * f) * cgl;
            mix[o + 1] += (pR + (nR - pR) * f) * cgr;
            cgl += dgl; cgr += dgr;
            frac += step;
            while (frac >= 1)
            {
                frac -= 1;
                if (mSourceEnded) { mGainL = gl; mGainR = gr; Finish(); return false; }
                pL = nL; pR = nR;
                if (!Reader.Next(Loop, out nL, out nR)) { nL = nR = 0; mSourceEnded = true; }
            }
        }
        mFrac = frac;
        mGainL = gl; mGainR = gr;
        return true;
    }

    void Finish()
    {
        if (mFinished) return;
        mFinished = true;
        try { Reader?.Dispose(); } catch { }
    }

    internal void ForceFinish() => Finish();
}

/// <summary>Mezclador en software: suma todas las voces activas y entrega estéreo float.</summary>
public sealed class Mixer
{
    readonly List<Voice> mVoices = new();
    readonly object mLock = new();
    public int OutputRate { get; internal set; } = 48000;
    /// <summary>Volumen general (ya incluye el silencio por "Muted").</summary>
    public float Master = 1;
    public int ActiveVoices { get { lock (mLock) return mVoices.Count; } }

    public Voice Start(FrameReader reader, float volume, float pan, float pitch, bool loop)
    {
        var v = new Voice { Reader = reader, Mono = reader.Channels == 1, Volume = volume, Pan = pan, Pitch = pitch, Loop = loop };
        lock (mLock) mVoices.Add(v);
        return v;
    }

    public void Stop(Voice v)
    {
        if (v == null) return;
        v.RequestStop();
    }

    public void StopAll()
    {
        lock (mLock)
        {
            foreach (var v in mVoices) v.ForceFinish();
            mVoices.Clear();
        }
    }

    public void Render(float[] stereo, int frames)
    {
        Array.Clear(stereo, 0, frames * 2);
        lock (mLock)
        {
            float master = Master;
            for (int i = mVoices.Count - 1; i >= 0; i--)
            {
                bool alive;
                try { alive = mVoices[i].MixInto(stereo, frames, OutputRate, master); }
                catch (Exception ex)
                {
                    Console.WriteLine("[FnrPort] Audio: error en una voz, se descarta: " + ex.Message);
                    mVoices[i].ForceFinish();
                    alive = false;
                }
                if (!alive) mVoices.RemoveAt(i);
            }
        }
        for (int i = 0; i < frames * 2; i++)
        {
            float s = stereo[i];
            stereo[i] = s > 1 ? 1 : s < -1 ? -1 : s;
        }
    }
}

/// <summary>Hilo que pide audio al mezclador y lo entrega a la salida de la plataforma.</summary>
public sealed class AudioPump
{
    readonly Mixer mMixer;
    AudioOutput mOut;
    Thread mThread;
    volatile bool mRunning;

    public AudioPump(Mixer mixer, AudioOutput output) { mMixer = mixer; mOut = output; }

    public string OutputName => mOut?.GetType().Name;

    public void Start()
    {
        try { mOut.Open(); }
        catch (Exception ex)
        {
            Console.WriteLine("[FnrPort] Audio: no se pudo abrir la salida (" + ex.Message + "), sigo sin sonido.");
            var paused = mOut.IsPaused;
            mOut = new NullAudioOutput();
            mOut.SetPaused(paused);
            mOut.Open();
        }
        mMixer.OutputRate = mOut.SampleRate;
        Console.WriteLine($"[FnrPort] Audio: salida {mOut.GetType().Name} a {mOut.SampleRate} Hz, bloques de {mOut.BlockFrames}");
        mRunning = true;
        mThread = new Thread(Loop) { Name = "FnrAudio", IsBackground = true, Priority = ThreadPriority.AboveNormal };
        mThread.Start();
    }

    void Loop()
    {
        int block = Math.Max(64, mOut.BlockFrames);
        var buf = new float[block * 2];
        while (mRunning)
        {
            mOut.WaitWhilePaused(() => mRunning);
            if (!mRunning) break;
            mMixer.Render(buf, block);
            if (FnrPort.Net.NetSession.HostWantsFrame) FnrPort.Net.NetSession.HostAudio(buf, block, mOut.SampleRate);
            try { mOut.Write(buf, block); }
            catch (Exception ex)
            {
                Console.WriteLine("[FnrPort] Audio: fallo de la salida (" + ex.Message + "), sigo sin sonido.");
                try { mOut.Close(); } catch { }
                mOut = new NullAudioOutput(mMixer.OutputRate);
                mOut.Open();
            }
        }
        try { mOut.Close(); } catch (Exception ex) { Console.WriteLine("[FnrPort] Audio: al cerrar: " + ex.Message); }
    }

    public void Stop()
    {
        mRunning = false;
        mOut?.Wake();
        mThread?.Join(2000);
        mThread = null;
    }
}
