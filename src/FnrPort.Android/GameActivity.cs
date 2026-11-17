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
    TextView mNetInfo;
    public const string ExtraNetHost = "net_host";
    static bool sNetHost;

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
        mNetInfo = new TextView(this) { TextSize = 13, Visibility = ViewStates.Gone };
        mNetInfo.SetTextColor(Android.Graphics.Color.White);
        mNetInfo.SetBackgroundColor(new Android.Graphics.Color(0, 0, 0, 150));
        mNetInfo.SetPadding(24, 10, 24, 10);
        frame.AddView(mNetInfo, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Top | GravityFlags.CenterHorizontal));
        SetContentView(frame);
        if (sThread == null) sNetHost = Intent?.GetBooleanExtra(ExtraNetHost, false) ?? false;
        if (sNetHost) { WifiLock.AcquireLowLatency(this); StartNetStatus(); }
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
    protected override void OnResume() { base.OnResume(); sHost?.SetPaused(false); sAudio?.SetPaused(false); HideSystemUi(); LauncherMusic.Update(this); }

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
                        .SetMessage(ex.Message + L.T("\n\nEn el launcher pulsa «Guardar registro en Descargas» y pásame el archivo.", "\n\nIn the launcher, press \"Save log to Downloads\" and send me the file."))
                        .SetPositiveButton(L.T("Cerrar", "Close"), (s, e) => Finish()).SetCancelable(false).Show();
                });
                sRunning = false; sThread = null;
                return;
            }
            sRunning = false; sThread = null; sHost = null;
            // Al salir del juego se cierra todo (también el launcher), como un juego de PC.
            try { FnrPort.Net.NetSession.Stop(); } catch { }
            Log.Write("Salida del juego: se cierra la app");
            RunOnUiThread(() =>
            {
                LauncherMusic.Release();
                FinishAndRemoveTask();
                new Handler(Looper.MainLooper).PostDelayed(() => Android.OS.Process.KillProcess(Android.OS.Process.MyPid()), 400);
            });
        }, 64 * 1024 * 1024) { Name = "FnrGame" };
        sThread.Start();
    }

    void RunGame(string gameDir, string files, int scale)
    {
        sHost.OnPausedInGameThread = GameLauncher.StoreSave;
        var cfg = Settings.Load(this);
        Haptics.Init(this);
        FnrPort.Rumble.Strength = cfg.Vibration / 100f;
        FnrPort.Rumble.Calm = cfg.VibMode == 1;
        FnrPort.Rumble.Sink = cfg.VibMode != 2 ? new Action<int, int>(Haptics.Vibrate) : null;
        mGameStart = Java.Lang.JavaSystem.CurrentTimeMillis();
        mImported = 0;
        FnrPort.Achievements.OnUnlocked = OnAchievement;
        sAudio = cfg.Sound ? new AudioTrackOutput() : null;
        GameLauncher.Run(sHost, new GameLauncher.Options
        {
            GameDir = gameDir,
            CacheDir = Path.Combine(CacheDir.AbsolutePath, "exe"),
            SaveFile = Settings.SavePath(this, cfg.Profile),
            AllContent = cfg.Profile == 1,
            AchievementsFile = Settings.AchievementsPath(this),
            WindowScale = scale,
            ScreenFormat = cfg.Format,
            NetHost = sNetHost,
            Vsync = true,
            AudioOutput = sAudio,
        });
        sAudio = null;
        FnrPort.Rumble.Sink = null;
        FnrPort.Achievements.OnUnlocked = null;
    }

    // ------------------------------------------------------------ logros: aviso encima del juego

    static long mGameStart;
    static int mImported;

    void OnAchievement(string id)
    {
        long now = Java.Lang.JavaSystem.CurrentTimeMillis();
        // Al cargar la partida el juego vuelve a anunciar los que ya tenía (p. ej. una partida importada de PC):
        // en los primeros segundos no se avisa uno a uno, sino con un único mensaje.
        if (now - mGameStart < 15000)
        {
            if (mImported++ == 0)
                new Handler(Looper.MainLooper).PostDelayed(() =>
                {
                    if (!IsFinishing) Toast.MakeText(this, L.T($"🏆 {mImported} logro(s) recuperados de tu partida", $"🏆 {mImported} achievement(s) recovered from your save"), ToastLength.Long).Show();
                }, 4000);
            return;
        }
        var info = AchievementInfo.Get(this, id);
        RunOnUiThread(() =>
        {
            Haptics.Vibrate(160, 255);
            Toast.MakeText(this, L.T("🏆 Logro conseguido: ", "🏆 Achievement unlocked: ") + info.Name, ToastLength.Long).Show();
        });
    }

    // ------------------------------------------------------------ juego en red (anfitrión): avisos encima del juego

    void StartNetStatus()
    {
        long eventUntil = 0; string eventText = null;
        var h = new Handler(Looper.MainLooper);
        void Tick()
        {
            if (IsFinishing || IsDestroyed) return;
            int ev = FnrPort.Net.NetSession.HostEvent();
            long now = Java.Lang.JavaSystem.CurrentTimeMillis();
            if (ev % 10 == 1) { eventText = L.T($"Ha entrado el jugador {ev / 10} por red (que pulse START)", $"Player {ev / 10} has joined online (press START)"); eventUntil = now + 4000; }
            if (ev % 10 == 2) { eventText = L.T($"El jugador {ev / 10} se ha ido de la partida en red", $"Player {ev / 10} has left the online game"); eventUntil = now + 4000; }
            string t = now < eventUntil ? eventText : FnrPort.Net.NetSession.HostStatus();
            mNetInfo.Text = t;
            mNetInfo.Visibility = string.IsNullOrEmpty(t) ? ViewStates.Gone : ViewStates.Visible;
            h.PostDelayed(Tick, 500);
        }
        h.PostDelayed(Tick, 500);
    }

    // ------------------------------------------------------------ entrada

    public override bool DispatchKeyEvent(KeyEvent e)
    {
        if (sHost == null) return base.DispatchKeyEvent(e);
        if (InputMapper.IsPad(e)) { mTouch?.OnPhysicalPadInput(); Haptics.UsePad(e.Device); }
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
