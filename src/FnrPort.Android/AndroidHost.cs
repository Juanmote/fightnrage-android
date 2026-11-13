using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Android.Opengl;
using Android.Views;

namespace FnrPort.Droid;

/// <summary>Host de Android: EGL sobre un SurfaceView, bucle en un hilo propio, mandos y teclado.</summary>
public sealed class AndroidHost : IPortHost
{
    // Constantes EGL (valores de la especificación)
    const int EGL_NONE = 0x3038, EGL_RED_SIZE = 0x3024, EGL_GREEN_SIZE = 0x3023, EGL_BLUE_SIZE = 0x3022, EGL_ALPHA_SIZE = 0x3021,
        EGL_RENDERABLE_TYPE = 0x3040, EGL_SURFACE_TYPE = 0x3033, EGL_WINDOW_BIT = 0x4, EGL_OPENGL_ES2_BIT = 0x4, EGL_OPENGL_ES3_BIT = 0x40,
        EGL_CONTEXT_CLIENT_VERSION = 0x3098, EGL_WIDTH = 0x3057, EGL_HEIGHT = 0x3056;

    EGLDisplay mDisplay;
    EGLConfig mConfig;
    EGLContext mContext;
    EGLSurface mSurface;

    readonly object mLock = new();
    Surface mPendingSurface;     // superficie nueva entregada por la UI
    bool mSurfaceLost;           // la UI pide soltar la superficie actual
    bool mSurfaceReleased;
    bool mPaused, mWasPaused, mQuit;

    readonly ConcurrentQueue<Action<PortInput>> mEvents = new();
    readonly PadSnapshot[] mPads = new PadSnapshot[PortInput.MaxPads];
    readonly Dictionary<int, int> mPadSlots = new();

    public int DrawableWidth { get; private set; } = 1280;
    public int DrawableHeight { get; private set; } = 720;
    public float RefreshRate { get; set; } = 60;
    public string OsDescription => "Android " + Android.OS.Build.VERSION.Release + " (" + Android.OS.Build.Model + ")";

    /// <summary>Se llama en el hilo del juego cuando la app pasa a segundo plano (para guardar).</summary>
    public Action OnPausedInGameThread;

    // ------------------------------------------------------------ desde la UI

    public void SetSurface(Surface s)
    {
        lock (mLock) { mPendingSurface = s; Monitor.PulseAll(mLock); }
    }

    /// <summary>Bloquea hasta que el hilo del juego suelta la superficie (obligatorio en surfaceDestroyed).</summary>
    public void ReleaseSurface(bool gameRunning)
    {
        lock (mLock)
        {
            mPendingSurface = null;
            if (!gameRunning || mSurface == null) return;
            mSurfaceLost = true; mSurfaceReleased = false;
            Monitor.PulseAll(mLock);
            var until = DateTime.UtcNow.AddSeconds(3);
            while (!mSurfaceReleased && DateTime.UtcNow < until) Monitor.Wait(mLock, 100);
        }
    }

    public void SetPaused(bool paused) { lock (mLock) { mPaused = paused; Monitor.PulseAll(mLock); } }
    public void RequestQuit() { lock (mLock) { mQuit = true; Monitor.PulseAll(mLock); } }
    public void Enqueue(Action<PortInput> a) => mEvents.Enqueue(a);

    public void UpdatePad(int deviceId, string name, Func<PadSnapshot, PadSnapshot> change)
    {
        lock (mPads)
        {
            if (!mPadSlots.TryGetValue(deviceId, out int slot))
            {
                slot = -1;
                for (int i = 0; i < mPads.Length; i++) if (!mPads[i].Connected && !mPadSlots.ContainsValue(i)) { slot = i; break; }
                if (slot < 0) return;
                mPadSlots[deviceId] = slot;
                mPads[slot] = new PadSnapshot { Connected = true, Name = name };
            }
            mPads[slot] = change(mPads[slot]);
        }
    }

    public void RemovePad(int deviceId)
    {
        lock (mPads)
            if (mPadSlots.Remove(deviceId, out int slot)) mPads[slot] = default;
    }

    // ------------------------------------------------------------ hilo del juego

    public void CreateWindow(string caption, int width, int height)
    {
        mDisplay = EGL14.EglGetDisplay(EGL14.EglDefaultDisplay);
        int[] v = new int[2];
        if (!EGL14.EglInitialize(mDisplay, v, 0, v, 1)) throw new Exception("eglInitialize falló");
        int version = 3;
        mConfig = Choose(EGL_OPENGL_ES3_BIT);
        if (mConfig == null) { version = 2; mConfig = Choose(EGL_OPENGL_ES2_BIT); }
        if (mConfig == null) throw new Exception("No hay configuración EGL compatible");
        mContext = EGL14.EglCreateContext(mDisplay, mConfig, EGL14.EglNoContext, new[] { EGL_CONTEXT_CLIENT_VERSION, version, EGL_NONE }, 0);
        if (mContext == null || mContext == EGL14.EglNoContext) throw new Exception("eglCreateContext falló");
        Console.WriteLine($"[FnrPort] Contexto OpenGL ES {version}.0 creado");
        WaitForSurface();
    }

    EGLConfig Choose(int renderable)
    {
        var configs = new EGLConfig[1]; var num = new int[1];
        int[] attrs = { EGL_RED_SIZE, 8, EGL_GREEN_SIZE, 8, EGL_BLUE_SIZE, 8, EGL_ALPHA_SIZE, 8,
            EGL_RENDERABLE_TYPE, renderable, EGL_SURFACE_TYPE, EGL_WINDOW_BIT, EGL_NONE };
        if (!EGL14.EglChooseConfig(mDisplay, attrs, 0, configs, 0, 1, num, 0) || num[0] == 0) return null;
        return configs[0];
    }

    /// <summary>Espera a tener superficie y la hace actual. Devuelve false si hay que salir.</summary>
    bool WaitForSurface()
    {
        Surface s;
        lock (mLock)
        {
            while (mPendingSurface == null && !mQuit) Monitor.Wait(mLock);
            if (mQuit) return false;
            s = mPendingSurface;
        }
        mSurface = EGL14.EglCreateWindowSurface(mDisplay, mConfig, s, new[] { EGL_NONE }, 0);
        if (mSurface == null || mSurface == EGL14.EglNoSurface) throw new Exception("eglCreateWindowSurface falló");
        if (!EGL14.EglMakeCurrent(mDisplay, mSurface, mSurface, mContext)) throw new Exception("eglMakeCurrent falló");
        EGL14.EglSwapInterval(mDisplay, 1);
        QuerySize();
        return true;
    }

    void DropSurface()
    {
        EGL14.EglMakeCurrent(mDisplay, EGL14.EglNoSurface, EGL14.EglNoSurface, mContext);
        if (mSurface != null) EGL14.EglDestroySurface(mDisplay, mSurface);
        mSurface = null;
    }

    void QuerySize()
    {
        int[] w = new int[1], h = new int[1];
        EGL14.EglQuerySurface(mDisplay, mSurface, EGL_WIDTH, w, 0);
        EGL14.EglQuerySurface(mDisplay, mSurface, EGL_HEIGHT, h, 0);
        if (w[0] > 0 && h[0] > 0) { DrawableWidth = w[0]; DrawableHeight = h[0]; }
    }

    public bool PumpEvents(PortInput input, PortCore core)
    {
        // Ciclo de vida: pausa / pérdida de superficie
        bool needSurface = false;
        lock (mLock)
        {
            if (mQuit) return false;
            if (mSurfaceLost)
            {
                DropSurface();
                mSurfaceLost = false; mSurfaceReleased = true;
                Monitor.PulseAll(mLock);
                needSurface = true;
            }
        }
        bool paused;
        lock (mLock) paused = mPaused;
        if (paused && !mWasPaused)
        {
            mWasPaused = true;
            core.SetFocus(false);
            try { OnPausedInGameThread?.Invoke(); } catch (Exception ex) { Console.WriteLine("[FnrPort] Guardado al pausar: " + ex.Message); }
        }
        lock (mLock)
        {
            while ((mPaused || (needSurface && mPendingSurface == null) || (mSurface == null && mPendingSurface == null)) && !mQuit)
            {
                if (mSurfaceLost) { DropSurface(); mSurfaceLost = false; mSurfaceReleased = true; Monitor.PulseAll(mLock); }
                Monitor.Wait(mLock, 200);
            }
            if (mQuit) return false;
        }
        if (mSurface == null && !WaitForSurface()) return false;
        if (mWasPaused) { mWasPaused = false; core.SetFocus(true); }

        while (mEvents.TryDequeue(out var a)) a(input);
        PadSnapshot[] snap;
        lock (mPads) snap = (PadSnapshot[])mPads.Clone();
        input.UpdatePads(snap);
        QuerySize();
        return true;
    }

    public void SwapBuffers()
    {
        if (mSurface != null) EGL14.EglSwapBuffers(mDisplay, mSurface);
    }

    public void DestroyWindow()
    {
        DropSurface();
        if (mContext != null) EGL14.EglDestroyContext(mDisplay, mContext);
        EGL14.EglTerminate(mDisplay);
    }
}
