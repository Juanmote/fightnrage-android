using System;
using System.IO;
using System.Threading;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;

namespace FnrPort.Droid;

[Activity(Label = "Fight'N Rage", Theme = "@android:style/Theme.Black.NoTitleBar.Fullscreen",
    ScreenOrientation = ScreenOrientation.SensorLandscape, LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.Keyboard |
        ConfigChanges.KeyboardHidden | ConfigChanges.Navigation | ConfigChanges.ScreenLayout | ConfigChanges.UiMode | ConfigChanges.Density)]
public class GameActivity : Activity, ISurfaceHolderCallback
{
    static AndroidHost sHost;
    static Thread sThread;
    static bool sRunning;
    static AudioTrackOutput sAudio;
    TouchOverlay mTouch;

    protected override void OnCreate(Bundle savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Log.Start(this);
        Log.Write("[FnrPort] Abriendo GameActivity");
        Window.AddFlags(WindowManagerFlags.KeepScreenOn | WindowManagerFlags.Fullscreen);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.P)
            Window.Attributes.LayoutInDisplayCutoutMode = LayoutInDisplayCutoutMode.ShortEdges;
        var view = new SurfaceView(this);
        view.Holder.AddCallback(this);
        view.Focusable = true; view.FocusableInTouchMode = true;
        if (sHost == null) sHost = new AndroidHost();
        var frame = new FrameLayout(this);
        frame.AddView(view, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        mTouch = new TouchOverlay(this, sHost, Settings.Load(this));
        frame.AddView(mTouch, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        SetContentView(frame);
        view.RequestFocus();
        HideSystemUi();
        try { sHost.RefreshRate = Display?.RefreshRate ?? 60; } catch { }
    }

    void HideSystemUi()
    {
#pragma warning disable CA1422
        Window.DecorView.SystemUiVisibility = (StatusBarVisibility)(SystemUiFlags.Fullscreen | SystemUiFlags.HideNavigation |
            SystemUiFlags.ImmersiveSticky | SystemUiFlags.LayoutFullscreen | SystemUiFlags.LayoutHideNavigation | SystemUiFlags.LayoutStable);
#pragma warning restore CA1422
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus) HideSystemUi();
    }

    protected override void OnPause() { base.OnPause(); sHost?.SetPaused(true); sAudio?.SetPaused(true); }
    protected override void OnResume() { base.OnResume(); sHost?.SetPaused(false); sAudio?.SetPaused(false); HideSystemUi(); }

    // ------------------------------------------------------------ superficie

    public void SurfaceCreated(ISurfaceHolder holder)
    {
        sHost.SetSurface(holder.Surface);
        if (sThread == null) StartGame();
    }

    public void SurfaceChanged(ISurfaceHolder holder, [GeneratedEnum] Android.Graphics.Format format, int width, int height) { }

    public void SurfaceDestroyed(ISurfaceHolder holder) => sHost.ReleaseSurface(sRunning);

    void StartGame()
    {
        var files = GetExternalFilesDir(null).AbsolutePath;
        var cfg = Settings.Load(this);
        string gameDir = Path.Combine(files, "game");
        int scale = cfg.Scale > 0 ? cfg.Scale : Settings.AutoScale(this);
        sRunning = true;
        sThread = new Thread(() =>
        {
            try
            {
                Console.WriteLine($"[FnrPort] Hilo del juego. Carpeta: {gameDir}, escala x{scale}, runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
                if (GameLauncher.PreparedExePath == null) GameLauncher.Prepare(gameDir, Path.Combine(CacheDir.AbsolutePath, "exe"));
                Console.WriteLine("[FnrPort] Exe preparado, arrancando el juego");
                RunGame(gameDir, files, scale);
                Console.WriteLine("[FnrPort] El juego ha terminado con normalidad");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[FnrPort] ERROR: " + ex);
                Log.Write("ERROR: " + ex);
                RunOnUiThread(() =>
                {
                    new AlertDialog.Builder(this).SetTitle("Error")
                        .SetMessage(ex.Message + "\n\nEn el launcher pulsa «Guardar registro en Descargas» y pásame el archivo.")
                        .SetPositiveButton("Cerrar", (s, e) => Finish()).SetCancelable(false).Show();
                });
                sRunning = false; sThread = null;
                return;
            }
            sRunning = false; sThread = null; sHost = null;
            RunOnUiThread(Finish);
        }, 64 * 1024 * 1024) { Name = "FnrGame" };
        sThread.Start();
    }

    void RunGame(string gameDir, string files, int scale)
    {
        sHost.OnPausedInGameThread = GameLauncher.StoreSave;
        var cfg = Settings.Load(this);
        sAudio = cfg.Sound ? new AudioTrackOutput() : null;
        GameLauncher.Run(sHost, new GameLauncher.Options
        {
            GameDir = gameDir,
            CacheDir = Path.Combine(CacheDir.AbsolutePath, "exe"),
            SaveFile = Path.Combine(files, "savedata", "data.sav"),
            WindowScale = scale,
            ScreenFormat = cfg.Format,
            Vsync = true,
            AudioOutput = sAudio,
        });
        sAudio = null;
    }

    // ------------------------------------------------------------ entrada

    public override bool DispatchKeyEvent(KeyEvent e)
    {
        if (sHost == null) return base.DispatchKeyEvent(e);
        if (InputMapper.IsPad(e)) mTouch?.OnPhysicalPadInput();
        if (InputMapper.HandleKey(sHost, e)) return true;
        return base.DispatchKeyEvent(e);
    }

    public override bool DispatchGenericMotionEvent(MotionEvent e)
    {
        if (sHost != null && InputMapper.IsPad(e)) mTouch?.OnPhysicalPadInput();
        if (sHost != null && InputMapper.HandleMotion(sHost, e)) return true;
        return base.DispatchGenericMotionEvent(e);
    }
}
