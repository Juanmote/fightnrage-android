using System;
using System.Diagnostics;
using SebaGames.Core;
using SebaGames.Framework;

namespace FnrPort;

/// <summary>Lo que tiene que aportar cada plataforma (ventana + contexto GLES + eventos).</summary>
public interface IPortHost
{
    /// <summary>Crea la ventana/superficie y deja el contexto GLES actual en este hilo.</summary>
    void CreateWindow(string caption, int width, int height);
    int DrawableWidth { get; }
    int DrawableHeight { get; }
    float RefreshRate { get; }
    /// <summary>Procesa eventos y vuelca el estado en el input. Devuelve false si hay que salir.</summary>
    bool PumpEvents(PortInput input, PortCore core);
    void SwapBuffers();
    void DestroyWindow();
    string OsDescription { get; }
}

/// <summary>Cómo se coloca la imagen del juego en la pantalla (opciones de vídeo).</summary>
public static class ScreenSettings
{
    /// <summary>true = FULL SCREEN del juego (estira a toda la pantalla); false = WINDOW (sin deformar, con bandas).</summary>
    public static volatile bool Stretch = true;
    /// <summary>0 = 16:9 original, 1 = 4:3, 2 = 8:7 (recortando los laterales).</summary>
    public static volatile int Format;
}

/// <summary>ICoreEngine: réplica del bucle de OTKCore (paso fijo con salto de frames).</summary>
public class PortCore : ICoreEngine
{
    readonly IPortHost mHost;
    readonly PortDisplay mDisplay = new();
    readonly PortInput mInput = new();
    readonly IAudioEngine mAudio;
    App.SystemInfo mSystemInfo;
    AppDescriptor mDesc;
    PortSurface mBackBuffer;
    bool mClosed, mFocused = true, mEnableRender;
    double mIdealDelta, mAccum, mTime;
    int mMaxFrameSkip = 4, frameCount, updateEventCount, frameSkipAverage, mFps, mUpd, mSkip;

    /// <summary>Se llama tras cada frame dibujado (para capturas, superposiciones, etc.).</summary>
    public Action<PortCore> AfterRender;
    /// <summary>Si &gt; 0, fuerza la frecuencia (como FORCE_60HZ).</summary>
    public int ForceFrequency;

    public PortCore(IPortHost host, IAudioEngine audio) { mHost = host; mAudio = audio; }

    public PortInput Input => mInput;
    public IDisplayEngine DisplayEngine => mDisplay;
    public IInputEngine InputEngine => mInput;
    public IAudioEngine SoundEngine => mAudio;
    public App.SystemInfo SystemInfo => mSystemInfo;
    public int WindowWidth => mHost.DrawableWidth;
    public int WindowHeight => mHost.DrawableHeight;
    public int FpsCountReport => mFps;
    public int UpdateCountReport => mUpd;
    public int FrameSkipReport => mSkip;
    public int TargetFrequency { get; private set; }
    public long FrameNumber { get; private set; }

    public event Action OnStart;
    public event Action OnClose;
    public event Updater OnUpdate;
    public event Updater OnRender;
    public event ErrorProvider OnError;
    public event Action OnLosesFocus;
    public event Action OnRegainFocus;
    public event PerformanceReport OnPassedOneSecond;

    public int Initialize(AppDescriptor d)
    {
        mDesc = d;
        GameHooks.OnEngineInitialize();
        Console.WriteLine($"[FnrPort] Ventana {d.DisplayWidth}x{d.DisplayHeight}, superficie {d.MainSurfaceWidth}x{d.MainSurfaceHeight}");
        mHost.CreateWindow(d.Caption, (int)d.DisplayWidth, (int)d.DisplayHeight);
        Renderer.Initialize();

        double freq = ForceFrequency > 0 ? ForceFrequency : d.Vsync ? Math.Max(60.0, mHost.RefreshRate) : d.MaxFps != 0 ? d.MaxFps : 60.0;
        if (freq > 500) freq = 500;
        if (freq <= 0) freq = 60;
        // En móviles la pantalla puede ir a 90/120 Hz: el juego está pensado para 60.
        if (ForceFrequency <= 0 && freq > 60) freq = 60;
        mIdealDelta = 1.0 / freq;
        TargetFrequency = (int)freq;

        mInput.Initialize();
        mDisplay.Initialize((int)d.MainSurfaceWidth, (int)d.MainSurfaceHeight);
        mAudio.Initialize();
        mBackBuffer = PortSurface.CreateBackBuffer(mHost.DrawableWidth, mHost.DrawableHeight);
        Console.WriteLine("[FnrPort] GL: " + GL.GetString(GL.RENDERER) + " | " + GL.GetString(GL.VERSION) + " | GLSL " + GL.GetString(GL.SHADING_LANGUAGE_VERSION));
        mSystemInfo = new App.SystemInfo(mHost.DrawableWidth, mHost.DrawableHeight, mHost.RefreshRate, 32, 2,
            GL.GetString(GL.RENDERER), GL.GetString(GL.SHADING_LANGUAGE_VERSION), GL.GetString(GL.VERSION), mHost.OsDescription);
        OnStart?.Invoke();

        var sw = Stopwatch.StartNew();
        while (!mClosed)
        {
            if (!mHost.PumpEvents(mInput, this)) { mClosed = true; break; }
            double dt = sw.Elapsed.TotalSeconds; sw.Restart();
            Update(dt);
            if (mClosed) break;
            Render();
        }
        OnClose?.Invoke();
        mDisplay.Dispose();
        mAudio.Dispose();
        mHost.DestroyWindow();
        return 0;
    }

    public void SetFocus(bool focused)
    {
        if (focused == mFocused) return;
        mFocused = focused;
        if (focused) OnRegainFocus?.Invoke();
        else { OnLosesFocus?.Invoke(); mInput.LosesFocus(); }
    }

    void Update(double dt)
    {
        updateEventCount++;
        mTime += dt;
        if ((mTime - dt) % 1.0 > mTime % 1.0)
        {
            mFps = frameCount; mUpd = updateEventCount; mSkip = frameSkipAverage;
            OnPassedOneSecond?.Invoke(updateEventCount, frameCount);
            updateEventCount = 0; frameCount = 0; frameSkipAverage = 0;
        }
        mAccum += dt;
        mAccum = Math.Min(mIdealDelta * (mMaxFrameSkip + 1), mAccum);
        int n = 0;
        while (mAccum >= mIdealDelta)
        {
            mAccum -= mIdealDelta;
            if (n == 0) mEnableRender = true;
            if (OnUpdate != null && !App.Inst.IgnoreCurrentFrameSkip) { OnUpdate((float)mIdealDelta); GameHooks.Tick(); }
            if (mClosed) return;
            n++;
            if (n - 1 > mMaxFrameSkip || mAccum < mIdealDelta * 1.5) break;
        }
        if (App.Inst != null && App.Inst.IgnoreCurrentFrameSkip)
        {
            App.Inst.IgnoreCurrentFrameSkip = false;
            mAccum -= mIdealDelta * n;
        }
        if (n > 0) frameSkipAverage += n - 1;
        // Con vsync el bucle ya va a ritmo de pantalla; sin frames pendientes, cede CPU.
        if (n == 0 && !mEnableRender) System.Threading.Thread.Sleep(1);
    }

    void Render()
    {
        if (!mEnableRender) return;
        OnRender?.Invoke(0f);
        mEnableRender = false;

        int ww = mHost.DrawableWidth, wh = mHost.DrawableHeight;
        var main = mDisplay.MainSurface;
        mBackBuffer.Smoothing = true;
        mBackBuffer.SetSize(ww, wh);
        var t = Transformation.Default;
        float mw = main.Width, mh = main.Height;
        // Formato: recorta los laterales para 4:3 u 8:7 (el juego siempre dibuja 354x200).
        float cw = ScreenSettings.Format switch { 1 => mh * 4f / 3f, 2 => mh * 8f / 7f, _ => mw };
        int sw = Math.Max(1, (int)Math.Round(Math.Min(mw, cw))), sx = (int)Math.Round((mw - sw) / 2f);
        var src = new Rectangle(sx, 0, sw, (int)mh);
        Vector2D scale;
        if (ScreenSettings.Stretch) scale = new Vector2D(ww / (float)sw, wh / mh);   // FULL SCREEN: ocupa toda la pantalla
        else { float s = Math.Min(ww / (float)sw, wh / mh); scale = new Vector2D(s, s); } // WINDOW: sin deformar
        t.Position = new Vector2D(ww / 2f, wh / 2f);
        t.Scale = scale;
        mBackBuffer.Color = new Color(255, 255, 255, 255);
        mBackBuffer.ClipRect = new Rectangle(0, 0, ww, wh);
        if (ww > sw * scale.X + 0.5f || wh > mh * scale.Y + 0.5f) mBackBuffer.Clear(new Color(0, 0, 0, 255));
        mBackBuffer.Smoothing = mDesc.SmoothScale;
        mBackBuffer.BlendMode = BLEND_MODE.NORMAL;
        mBackBuffer.Draw(main.GetBitmap(), src, t, new Vector2D(sw / 2f, mh / 2f));
        if (FnrPort.Net.NetSession.HostWantsFrame) CaptureForNet(main);
        Renderer.Flush();
        AfterRender?.Invoke(this);
        mHost.SwapBuffers();
        frameCount++;
        FrameNumber++;
    }

    // ---- juego en red: la imagen reducida a 354x200 para el invitado
    PortSurface mNetSurf;
    byte[] mNetRgba, mNetRgb;

    unsafe void CaptureForNet(IDrawableSurface main)
    {
        try
        {
            const int W = FnrPort.Net.NetSession.MaxW, H = FnrPort.Net.NetSession.MaxH;
            mNetSurf ??= PortSurface.CreateNew(W, H);
            mNetRgba ??= new byte[W * H * 4];
            mNetRgb ??= new byte[W * H * 3];
            var t = Transformation.Default;
            t.Position = new Vector2D(W / 2f, H / 2f);
            t.Scale = new Vector2D(W / (float)main.Width, H / (float)main.Height);
            mNetSurf.Color = new Color(255, 255, 255, 255);
            mNetSurf.BlendMode = BLEND_MODE.NORMAL;
            mNetSurf.Smoothing = false;
            mNetSurf.Draw(main.GetBitmap(), main.FullRect, t, new Vector2D(main.Width / 2f, main.Height / 2f));
            Renderer.Flush();
            uint prev = Renderer.BoundFbo;
            GL.glBindFramebuffer(GL.FRAMEBUFFER, mNetSurf.Fbo);
            fixed (byte* p = mNetRgba) GL.glReadPixels(0, 0, W, H, GL.RGBA, GL.UNSIGNED_BYTE, p);
            GL.glBindFramebuffer(GL.FRAMEBUFFER, prev);
            for (int i = 0, j = 0; i < mNetRgba.Length; i += 4, j += 3) { mNetRgb[j] = (byte)(mNetRgba[i] & 0xF8); mNetRgb[j + 1] = (byte)(mNetRgba[i + 1] & 0xFC); mNetRgb[j + 2] = (byte)(mNetRgba[i + 2] & 0xF8); } // precisión de 16 bits: comprime mucho mejor
            FnrPort.Net.NetSession.HostFrame(mNetRgb, W, H, 0);
        }
        catch (Exception ex) { Console.WriteLine("[FnrNet] captura: " + ex.Message); }
    }

    public void Close() => mClosed = true;

    public void ShowMessage(string aType, string aMessage, string aSubMessage) =>
        Console.WriteLine($"[FnrPort] {aType}: {aMessage} {aSubMessage}");

    internal void RaiseError(string e) => OnError?.Invoke(e);
}
