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
            if (OnUpdate != null && !App.Inst.IgnoreCurrentFrameSkip) OnUpdate((float)mIdealDelta);
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
        bool integer = mDesc.IntegerScale;
        float mw = main.Width, mh = main.Height;
        var scale = new Vector2D(ww / mw, wh / mh);
        switch (mDesc.ScaleMode)
        {
            case AppDescriptor.SCALE_MODE.NO_SCALE:
                scale = Vector2D.One; t.Position = new Vector2D(main.Width / 2, main.Height / 2); break;
            case AppDescriptor.SCALE_MODE.FULL:
                t.Position = new Vector2D(ww / 2, wh / 2); break;
            default: // KEEP_ASPECT_RATIO / KEEP_PIXEL_ASPECT
                float s = Math.Min(scale.X, scale.Y);
                scale = new Vector2D(s, s);
                if (mDesc.ScaleMode == AppDescriptor.SCALE_MODE.KEEP_PIXEL_ASPECT) integer = true;
                t.Position = new Vector2D(ww / 2, wh / 2); break;
        }
        if (integer)
        {
            int ix = (int)scale.X;
            if (ix > 0 && ix <= scale.Y) scale = new Vector2D((int)scale.X, (int)scale.Y);
        }
        t.Scale = scale;
        mBackBuffer.Color = new Color(255, 255, 255, 255);
        mBackBuffer.ClipRect = new Rectangle(0, 0, ww, wh);
        if (ww > mw * scale.X || wh > mh * scale.Y) mBackBuffer.Clear(new Color(0, 0, 0, 255));
        mBackBuffer.Smoothing = mDesc.SmoothScale;
        mBackBuffer.BlendMode = BLEND_MODE.NORMAL;
        mBackBuffer.Draw(main.GetBitmap(), main.FullRect, t, new Vector2D(mDesc.MainSurfaceWidth / 2, mDesc.MainSurfaceHeight / 2));
        Renderer.Flush();
        AfterRender?.Invoke(this);
        mHost.SwapBuffers();
        frameCount++;
        FrameNumber++;
    }

    public void Close() => mClosed = true;

    public void ShowMessage(string aType, string aMessage, string aSubMessage) =>
        Console.WriteLine($"[FnrPort] {aType}: {aMessage} {aSubMessage}");

    internal void RaiseError(string e) => OnError?.Invoke(e);
}
