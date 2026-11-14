using System;
using System.IO;
using SebaGames.Core;
using SebaGames.Framework;

namespace FnrPort.Audio;

/// <summary>
/// IAudioEngine del port: mezclador propio en C# (sustituye a FModAudioImplementation del PC).
/// Mismo comportamiento que la versión FMOD: cada ISound trae una instancia por defecto (Inst),
/// cada instancia suena en una sola voz a la vez (Play la reinicia) y IsPlaying sigue siendo
/// true mientras está en pausa.
/// </summary>
public sealed class PortAudioEngine : IAudioEngine
{
    readonly Mixer mMixer = new();
    readonly AudioPump mPump;
    float mMasterVolume = 1;
    bool mMuted, mInitialized;
    int mLoaded, mFailed;

    public PortAudioEngine(AudioOutput output) { mPump = new AudioPump(mMixer, output ?? new NullAudioOutput()); }

    internal Mixer Mixer => mMixer;

    public float MasterVolume { get => mMasterVolume; set { mMasterVolume = value; Apply(); } }
    public bool Muted { get => mMuted; set { mMuted = value; Apply(); } }
    void Apply() => mMixer.Master = mMuted ? 0 : mMasterVolume;

    public void Initialize()
    {
        Console.WriteLine("[FnrPort] Iniciando audio (mezclador propio)");
        if (mInitialized) throw new Exception("Already has been initialized!");
        mInitialized = true;
        mPump.Start();
        MasterVolume = 1;
    }

    public void Dispose()
    {
        Console.WriteLine($"[FnrPort] Cerrando audio ({mLoaded} sonidos cargados, {mFailed} fallidos)");
        mPump.Stop();
        mMixer.StopAll();
    }

    public ISound CreateSound(string aFile) => new PortSound(this, Load(aFile, false), aFile);
    public IMusic CreateMusic(string aFile) => new PortSound(this, Load(aFile, true), aFile);
    public ISoundInstance CreateSoundInstance() => new PortSoundInstance(this);

    AudioData Load(string file, bool music)
    {
        string path = null;
        try
        {
            path = PathIndexer.GetPath(file, true);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) path = File.Exists(file) ? file : null;
            if (path == null) throw new FileNotFoundException("no está en el índice de data/");
            var d = music ? AudioData.LoadMusic(path) : AudioData.LoadSound(path);
            mLoaded++;
            return d;
        }
        catch (Exception ex)
        {
            mFailed++;
            Console.WriteLine($"[FnrPort] Audio: no se pudo cargar {file} ({path}): {ex.Message}");
            return null; // sonido mudo: el juego sigue
        }
    }
}

/// <summary>ISound / IMusic (como FMOD_Sound): datos + instancia por defecto.</summary>
public sealed class PortSound : ISound, IMusic
{
    readonly PortAudioEngine mEngine;
    readonly PortSoundInstance mInst;
    internal AudioData Data { get; private set; }
    public readonly string Name;
    internal readonly int RumbleMs, RumbleAmp; // vibración que acompaña a este efecto (golpes, caídas...)

    public PortSound(PortAudioEngine engine, AudioData data, string name)
    {
        mEngine = engine; Data = data; Name = name;
        (RumbleMs, RumbleAmp) = Rumble.ForSound(name);
        mInst = new PortSoundInstance(engine);
        mInst.ChangeSound(this);
    }

    public IAudioPlayback Inst => mInst;
    public ISoundInstance CreateInstance()
    {
        var i = new PortSoundInstance(mEngine);
        i.ChangeSound(this);
        return i;
    }

    public void Play(float aVolume, float aPosition, float aPitch, bool aLoop)
    {
        mInst.Volume = aVolume;
        mInst.Pan = aPosition;
        mInst.Pitch = aPitch;
        mInst.Loop = aLoop;
        mInst.Play();
    }

    public void Play() => mInst.Play();
    public void Stop() => mInst.Stop();
    public void Pause() => mInst.Pause();
    public void Resume() => mInst.Resume();
    public float Pan { get => mInst.Pan; set => mInst.Pan = value; }
    public float Volume { get => mInst.Volume; set => mInst.Volume = value; }
    public float Pitch { get => mInst.Pitch; set => mInst.Pitch = value; }
    public bool Loop { get => mInst.Loop; set => mInst.Loop = value; }
    public bool IsPlaying => mInst.IsPlaying;
    public ISound GetBaseSound() => this;
    public IMusic GetBaseMusic() => mInst;

    public void Dispose()
    {
        mInst.Dispose();
        Data?.Dispose();
        Data = null;
    }
}

/// <summary>ISoundInstance (como FMOD_SoundInstance): una voz con su volumen, pan, tono y bucle.</summary>
public sealed class PortSoundInstance : ISoundInstance, IMusic
{
    readonly PortAudioEngine mEngine;
    PortSound mSource;
    Voice mVoice;
    float mPan, mVolume = 1, mPitch = 1;
    bool mLoop;

    public PortSoundInstance(PortAudioEngine engine) { mEngine = engine; }

    public ISound Sound => mSource;

    public void ChangeSound(ISound aSound)
    {
        Stop();
        mSource = aSound?.GetBaseSound() as PortSound;
    }

    public ISoundInstance GetBaseSoundInstance() => this;
    public IMusic GetBaseMusic() => this;

    public void Play()
    {
        if (mSource == null) return;
        Stop();
        var data = mSource.Data;
        if (data == null) return;
        FrameReader reader;
        try { reader = data.CreateReader(); }
        catch (Exception ex) { Console.WriteLine($"[FnrPort] Audio: no se pudo reproducir {mSource.Name}: {ex.Message}"); return; }
        mVoice = mEngine.Mixer.Start(reader, mVolume, mPan, mPitch, mLoop);
        if (mSource.RumbleMs > 0 && mVolume > 0.05f) Rumble.Fire(mSource.RumbleMs, mSource.RumbleAmp);
    }

    public void Stop()
    {
        if (mVoice == null) return;
        mEngine.Mixer.Stop(mVoice);
        mVoice = null;
    }

    public void Pause() { if (mVoice != null) mVoice.Paused = true; }
    public void Resume() { if (mVoice != null) mVoice.Paused = false; }

    public float Pan { get => mPan; set { mPan = value; if (mVoice != null) mVoice.Pan = value; } }
    public float Volume { get => mVolume; set { mVolume = value; if (mVoice != null) mVoice.Volume = value; } }
    public float Pitch { get => mPitch; set { mPitch = value; if (mVoice != null) mVoice.Pitch = value; } }
    public bool Loop { get => mLoop; set { mLoop = value; if (mVoice != null) mVoice.Loop = value; } }

    public bool IsPlaying => mVoice != null && !mVoice.Finished;

    public void Dispose()
    {
        Stop();
        mSource = null;
    }
}
