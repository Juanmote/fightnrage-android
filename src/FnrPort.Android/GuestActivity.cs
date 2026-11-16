using System;
using System.Collections.Generic;
using System.Threading;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Media;
using Android.OS;
using Android.Views;
using Android.Widget;
using FnrPort.Net;

namespace FnrPort.Droid;

/// <summary>
/// Invitado del juego en red: no ejecuta el juego (no hace falta tenerlo); pinta la imagen que manda el anfitrión,
/// reproduce su sonido y le manda los botones (mando físico o táctiles), que allí son el jugador 2.
/// Salir: botón Atrás del sistema, o mantener SELECT + START un segundo y medio.
/// </summary>
[Activity(Label = "Fight'N Rage (en red)", Theme = "@android:style/Theme.Black.NoTitleBar.Fullscreen",
    ScreenOrientation = ScreenOrientation.SensorLandscape, LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.Keyboard |
        ConfigChanges.KeyboardHidden | ConfigChanges.Navigation | ConfigChanges.ScreenLayout | ConfigChanges.UiMode)]
public class GuestActivity : Activity, ISurfaceHolderCallback, IPadSink
{
    public const string ExtraHost = "host", ExtraPort = "port";
    SurfaceView mView;
    TouchOverlay mTouch;
    TextView mInfo;
    Thread mThread;
    volatile bool mRun;
    volatile Surface mSurface;
    readonly Dictionary<int, PadSnapshot> mPads = new();
    Settings mCfg;
    long mExitHeldSince;

    protected override void OnCreate(Bundle b)
    {
        base.OnCreate(b);
        Log.Start(this);
        mCfg = Settings.Load(this);
        Window.AddFlags(WindowManagerFlags.KeepScreenOn | WindowManagerFlags.Fullscreen);
        mView = new SurfaceView(this);
        mView.Holder.AddCallback(this);
        mView.Focusable = true; mView.FocusableInTouchMode = true;
        var frame = new FrameLayout(this);
        frame.AddView(mView, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        mTouch = new TouchOverlay(this, this, mCfg);
        frame.AddView(mTouch, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        mInfo = new TextView(this) { TextSize = 13 };
        mInfo.SetTextColor(Color.White);
        mInfo.SetShadowLayer(4, 0, 0, Color.Black);
        mInfo.SetPadding(24, 12, 24, 12);
        frame.AddView(mInfo, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Top | GravityFlags.Left));
        SetContentView(frame);
        mView.RequestFocus();
        HideSystemUi();
        mRun = true;
        mThread = new Thread(Loop) { Name = "FnrGuest", IsBackground = true };
        mThread.Start();
        new Thread(AudioLoop) { Name = "FnrGuestAudio", IsBackground = true }.Start();
    }

    void HideSystemUi()
    {
#pragma warning disable CA1422
        Window.DecorView.SystemUiVisibility = (StatusBarVisibility)(SystemUiFlags.Fullscreen | SystemUiFlags.HideNavigation |
            SystemUiFlags.ImmersiveSticky | SystemUiFlags.LayoutFullscreen | SystemUiFlags.LayoutHideNavigation | SystemUiFlags.LayoutStable);
#pragma warning restore CA1422
    }

    public override void OnWindowFocusChanged(bool hasFocus) { base.OnWindowFocusChanged(hasFocus); if (hasFocus) HideSystemUi(); }

    protected override void OnDestroy()
    {
        mRun = false;
        NetSession.Stop();
        base.OnDestroy();
    }

    public void SurfaceCreated(ISurfaceHolder holder) => mSurface = holder.Surface;
    public void SurfaceChanged(ISurfaceHolder holder, [Android.Runtime.GeneratedEnum] Format format, int width, int height) => mSurface = holder.Surface;
    public void SurfaceDestroyed(ISurfaceHolder holder) => mSurface = null;

    // ------------------------------------------------------------ IPadSink: se juntan todos los mandos en uno

    public void UpdatePad(int deviceId, string name, Func<PadSnapshot, PadSnapshot> change)
    {
        lock (mPads) { mPads.TryGetValue(deviceId, out var p); p.Connected = true; mPads[deviceId] = change(p); }
    }
    public void RemovePad(int deviceId) { lock (mPads) mPads.Remove(deviceId); }
    public void Enqueue(Action<PortInput> a) { }

    int Bits()
    {
        int b = 0;
        lock (mPads)
            foreach (var p in mPads.Values)
            {
                const float dz = 0.4f;
                if (p.DUp || p.LY < -dz) b |= (int)NetSession.Btn.Up;
                if (p.DDown || p.LY > dz) b |= (int)NetSession.Btn.Down;
                if (p.DLeft || p.LX < -dz) b |= (int)NetSession.Btn.Left;
                if (p.DRight || p.LX > dz) b |= (int)NetSession.Btn.Right;
                if (p.A) b |= (int)NetSession.Btn.A; if (p.B) b |= (int)NetSession.Btn.B;
                if (p.X) b |= (int)NetSession.Btn.X; if (p.Y) b |= (int)NetSession.Btn.Y;
                if (p.LB) b |= (int)NetSession.Btn.LB; if (p.RB) b |= (int)NetSession.Btn.RB;
                if (p.Start) b |= (int)NetSession.Btn.Start; if (p.Back) b |= (int)NetSession.Btn.Back;
                if (p.C) b |= (int)NetSession.Btn.C; if (p.Z) b |= (int)NetSession.Btn.Z;
            }
        return b;
    }

    // ------------------------------------------------------------ bucle: botones, imagen y avisos

    void Loop()
    {
        var rgb = new byte[NetSession.MaxW * NetSession.MaxH * 3];
        int[] px = null; Bitmap bmp = null;
        var paint = new Paint(PaintFlags.FilterBitmap);
        var dst = new Rect();
        long lastFrame = System.Environment.TickCount64, lastInfo = 0;
        while (mRun)
        {
            int bits = Bits();
            NetSession.GuestInputs(bits);
            // SELECT + START un segundo y medio: salir
            const int both = (int)NetSession.Btn.Start | (int)NetSession.Btn.Back;
            if ((bits & both) == both) { if (mExitHeldSince == 0) mExitHeldSince = System.Environment.TickCount64; else if (System.Environment.TickCount64 - mExitHeldSince > 1500) { RunOnUiThread(Finish); break; } }
            else mExitHeldSince = 0;

            if (!NetSession.Connected)
            {
                RunOnUiThread(() =>
                {
                    Toast.MakeText(this, L.T("Se ha cortado la conexión con el anfitrión", "The connection with the host was lost"), ToastLength.Long).Show();
                    Finish();
                });
                break;
            }
            bool got = NetSession.GuestFrame(rgb, out int w, out int h);
            var surf = mSurface;
            if (got && surf != null && surf.IsValid)
            {
                lastFrame = System.Environment.TickCount64;
                if (bmp == null || bmp.Width != w || bmp.Height != h) { bmp = Bitmap.CreateBitmap(w, h, Bitmap.Config.Argb8888); px = new int[w * h]; }
                for (int i = 0, j = 0; i < px.Length; i++, j += 3) px[i] = unchecked((int)0xFF000000) | rgb[j] << 16 | rgb[j + 1] << 8 | rgb[j + 2];
                bmp.SetPixels(px, 0, w, 0, 0, w, h);
                Canvas c = null;
                try
                {
                    c = surf.LockCanvas(null);
                    if (c != null)
                    {
                        int cw = c.Width, ch = c.Height;
                        c.DrawColor(Color.Black);
                        int f = mCfg.Format;
                        // mismo formato que en el anfitrión: 16:9 entero, o recortado a 4:3 / 8:7, sin deformar
                        float srcW = f == 1 ? h * 4f / 3f : f == 2 ? h * 8f / 7f : w;
                        int sx = (int)((w - srcW) / 2);
                        var src = new Rect(sx, 0, sx + (int)srcW, h);
                        float s = Math.Min(cw / srcW, ch / (float)h);
                        int dw = (int)(srcW * s), dh = (int)(h * s);
                        dst.Set((cw - dw) / 2, (ch - dh) / 2, (cw + dw) / 2, (ch + dh) / 2);
                        c.DrawBitmap(bmp, src, dst, paint);
                    }
                }
                catch { }
                finally { if (c != null) try { surf.UnlockCanvasAndPost(c); } catch { } }
            }
            long now = System.Environment.TickCount64;
            if (now - lastInfo > 500)
            {
                lastInfo = now;
                bool waiting = now - lastFrame > 700;
                int ping = NetSession.PingMs;
                string me = L.T($"En red · jugador {NetSession.PlayerNumber}", $"Online · player {NetSession.PlayerNumber}");
                string t = waiting ? L.T("Esperando al anfitrión...", "Waiting for the host...") : (ping >= 0 ? me + $" · {ping} ms" : me);
                t += L.T("   (salir: Atrás, o SELECT + START 1,5 s)", "   (exit: Back, or SELECT + START 1.5 s)");
                RunOnUiThread(() => mInfo.Text = t);
            }
            Thread.Sleep(got ? 1 : 4);
        }
    }

    // ------------------------------------------------------------ sonido

    void AudioLoop()
    {
        AudioTrack track = null;
        int rate = 0;
        var buf = new short[1024 * 2];
        try
        {
            while (mRun && NetSession.CurrentMode == NetSession.Mode.Guest)
            {
                int r = NetSession.GuestAudioRate;
                if (track == null || r != rate)
                {
                    track?.Release();
                    rate = r;
                    int min = AudioTrack.GetMinBufferSize(rate, ChannelOut.Stereo, Encoding.Pcm16bit);
                    track = new AudioTrack.Builder()
                        .SetAudioAttributes(new AudioAttributes.Builder().SetUsage(AudioUsageKind.Game).SetContentType(AudioContentType.Music).Build())
                        .SetAudioFormat(new AudioFormat.Builder().SetEncoding(Encoding.Pcm16bit).SetSampleRate(rate).SetChannelMask(ChannelOut.Stereo).Build())
                        .SetBufferSizeInBytes(Math.Max(min * 2, rate / 10 * 4))
                        .SetTransferMode(AudioTrackMode.Stream)
                        .Build();
                    track.Play();
                }
                // no dejar que se acumule retraso: como mucho ~150 ms en cola
                NetSession.GuestAudioTrim(rate * 15 / 100);
                int n = NetSession.GuestAudio(buf, 1024);
                if (n == 0) { Thread.Sleep(5); continue; }
                track.Write(buf, 0, n * 2);
            }
        }
        catch (Exception ex) { Console.WriteLine("[FnrNet] sonido del invitado: " + ex.Message); }
        try { track?.Stop(); track?.Release(); } catch { }
    }

    // ------------------------------------------------------------ entrada

    public override bool DispatchKeyEvent(KeyEvent e)
    {
        bool pad = InputMapper.IsPad(e);
        if (!pad && e.KeyCode == Keycode.Back)
        {
            if (e.Action == KeyEventActions.Up) Finish();
            return true;
        }
        if (pad) mTouch?.OnPhysicalPadInput();
        if (pad && InputMapper.HandleKey(this, e)) return true;
        return base.DispatchKeyEvent(e);
    }

    public override bool DispatchGenericMotionEvent(MotionEvent e)
    {
        if (InputMapper.IsPad(e)) { mTouch?.OnPhysicalPadInput(); if (InputMapper.HandleMotion(this, e)) return true; }
        return base.DispatchGenericMotionEvent(e);
    }
}
