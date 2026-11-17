using System;
using System.IO;
using System.Reflection;
using Android.App;
using Android.Content;

namespace FnrPort.Droid;

/// <summary>
/// El exe del juego está compilado para .NET Framework 4 y pide "mscorlib", "System", "System.Core"
/// y "System.Xml". En Android esas fachadas no se empaquetan solas, así que van como assets y se
/// cargan bajo demanda. Las clases de <see cref="Anchor"/> obligan a incluir en la APK los
/// ensamblados reales a los que apuntan.
/// </summary>
static class Facades
{
    static string sDir;

    public static void Install(Context ctx, string dir)
    {
        if (sDir != null) return;
        Anchor.Touch();
        sDir = dir;
        Directory.CreateDirectory(dir);
        var assets = ctx.Assets;
        foreach (var name in assets.List("facades") ?? Array.Empty<string>())
        {
            string dst = Path.Combine(dir, name);
            using var src = assets.Open("facades/" + name);
            using var ms = new MemoryStream();
            src.CopyTo(ms);
            if (File.Exists(dst) && new FileInfo(dst).Length == ms.Length) continue;
            File.WriteAllBytes(dst, ms.ToArray());
        }
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            var n = new AssemblyName(e.Name).Name;
            string p = Path.Combine(sDir, n + ".dll");
            if (File.Exists(p))
            {
                Console.WriteLine("[FnrPort] Cargando fachada " + n);
                return Assembly.LoadFrom(p);
            }
            if (n == "FIGHT_N_RAGE" || n.EndsWith(".resources")) return null; // FIGHT_N_RAGE lo resuelve GameLauncher
            Console.WriteLine("[FnrPort] Ensamblado no encontrado: " + e.Name);
            return null;
        };
    }
}

/// <summary>
/// Arranque del proceso: registro, fachadas y resolvedor del exe ANTES de abrir cualquier pantalla.
/// </summary>
[Application(Label = "Fight'N Rage")]
public class FnrApplication : Application
{
    public FnrApplication(IntPtr handle, Android.Runtime.JniHandleOwnership transfer) : base(handle, transfer) { }

    public override void OnCreate()
    {
        base.OnCreate();
        Log.Start(this);
        try { FnrPort.Lang.Spanish = Java.Util.Locale.Default.Language == "es"; } catch { }
        try
        {
            Facades.Install(this, Path.Combine(CacheDir.AbsolutePath, "facades"));
            string files = GetExternalFilesDir(null).AbsolutePath;
            GameLauncher.InstallLazyResolver(Path.Combine(files, "game"), Path.Combine(CacheDir.AbsolutePath, "exe"));
            Console.WriteLine("[FnrPort] Aplicación iniciada (fachadas y resolvedor listos)");
        }
        catch (Exception ex) { Log.Write("Error al iniciar: " + ex); }
    }
}

/// <summary>Referencias a tipos de cada ensamblado que usa el juego, para que se empaqueten.</summary>
static class Anchor
{
    public static Type[] Touch() => new[]
    {
        typeof(System.Collections.Generic.Stack<int>), typeof(System.Collections.Generic.SortedDictionary<int,int>),
        typeof(System.Collections.Concurrent.ConcurrentDictionary<int,int>), typeof(System.Collections.ArrayList),
        typeof(System.Collections.Specialized.NameValueCollection), typeof(System.Collections.ObjectModel.ObservableCollection<int>),
        typeof(System.Collections.Hashtable), typeof(System.Collections.Specialized.OrderedDictionary),
        typeof(System.IO.Compression.GZipStream), typeof(System.Linq.Enumerable), typeof(System.Linq.Expressions.Expression),
        typeof(System.Xml.XmlDocument), typeof(System.Xml.XmlReader), typeof(System.Xml.Linq.XDocument), typeof(System.Xml.Serialization.XmlSerializer),
        typeof(System.Text.RegularExpressions.Regex), typeof(System.Uri), typeof(System.Threading.Thread), typeof(System.Threading.Timer),
        typeof(System.Text.UTF7Encoding), typeof(System.Text.CodePagesEncodingProvider), typeof(System.Console),
        typeof(System.ComponentModel.TypeConverter), typeof(System.ComponentModel.Component), typeof(System.ComponentModel.INotifyPropertyChanged),
        typeof(System.Diagnostics.Stopwatch), typeof(System.Diagnostics.Process), typeof(System.Diagnostics.StackTrace), typeof(System.Diagnostics.Trace),
        typeof(System.Runtime.InteropServices.Marshal), typeof(System.Runtime.Serialization.DataContractAttribute),
        typeof(System.Security.Cryptography.MD5), typeof(System.Globalization.CultureInfo), typeof(System.Buffers.ArrayPool<byte>),
        typeof(System.Numerics.BigInteger), typeof(System.IO.MemoryMappedFiles.MemoryMappedFile), typeof(System.Net.WebUtility),
        typeof(System.IO.FileSystemWatcher), typeof(System.Resources.ResourceManager), typeof(System.Reflection.Emit.DynamicMethod),
        typeof(System.Drawing.Point), typeof(System.Drawing.Color), typeof(System.Data.DataTable),
        typeof(Microsoft.Win32.SafeHandles.SafeFileHandle), typeof(System.Web.HttpUtility), typeof(System.Transactions.Transaction),
    };
}

/// <summary>Ajustes del launcher.</summary>
sealed class Settings
{
    public int Scale; // 0 = automático
    public bool Sound = true;
    public int Format;            // 0 = 16:9, 1 = 4:3, 2 = 8:7 (recortando laterales)
    public bool TouchAuto = true; // controles táctiles automáticos (sin mando físico)
    public int TouchAlpha = 45;   // opacidad de los botones táctiles (%)
    public int TouchSize = 100;   // tamaño de los botones táctiles (%)
    public int Profile;           // 0 = principal (desde cero), 1 = todo desbloqueado
    public int Vibration = 75;    // fuerza de la vibración: 25 ... 100
    public int VibMode;           // 0 = Rock And Roll Baby (todo), 1 = De tranquis (solo golpes y caídas), 2 = sin vibración
    public bool Music = true;     // música del launcher

    public static Settings Load(Context ctx)
    {
        var p = ctx.GetSharedPreferences("fnr", FileCreationMode.Private);
        var st = new Settings
        {
            Scale = p.GetInt("scale", 0), Sound = p.GetBoolean("sound", true), Format = p.GetInt("format", 0),
            TouchAuto = p.GetBoolean("touch_auto", true), TouchAlpha = p.GetInt("touch_alpha", 45), TouchSize = p.GetInt("touch_size", 100),
            Profile = p.GetInt("profile", 0), Vibration = p.GetInt("vibration", 75), VibMode = p.GetInt("vib_mode", -1), Music = p.GetBoolean("music", true),
        };
        // v0.1.12 tenía solo la fuerza (0 = sin vibración)
        if (st.VibMode < 0 || st.VibMode > 2) st.VibMode = st.Vibration == 0 ? 2 : 0;
        if (st.Vibration < 25) st.Vibration = 75;
        return st;
    }

    public void Save(Context ctx)
    {
        var p = ctx.GetSharedPreferences("fnr", FileCreationMode.Private).Edit();
        p.PutInt("scale", Scale); p.PutBoolean("sound", Sound); p.PutInt("format", Format);
        p.PutBoolean("touch_auto", TouchAuto); p.PutInt("touch_alpha", TouchAlpha); p.PutInt("touch_size", TouchSize);
        p.PutInt("profile", Profile); p.PutInt("vibration", Vibration); p.PutInt("vib_mode", VibMode); p.PutBoolean("music", Music);
        p.Apply();
    }

    public static string[] FormatNames => new[] { "16:9 (original)", L.T("4:3 (recorta los laterales)", "4:3 (crops the sides)"), L.T("8:7 (recorta los laterales)", "8:7 (crops the sides)") };

    public static string[] VibModeNames => new[] { "Rock And Roll Baby", L.T("De tranquis", "Chill"), L.T("sin vibración", "off") };

    public static string[] ProfileNames => new[] { L.T("Principal", "Main"), L.T("Todo desbloqueado", "Everything unlocked") };

    /// <summary>Opción de vídeo del juego guardada en la partida de esta consola: true = pantalla completa.</summary>
    public static bool SaveStretch(Context ctx, int profile)
    {
        try
        {
            var path = SavePath(ctx, profile);
            if (!File.Exists(path)) return true;
            using var fs = File.OpenRead(path);
            using var z = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Decompress);
            using var r = new StreamReader(z);
            var head = new char[4096]; int n = r.ReadBlock(head, 0, head.Length);
            return !new string(head, 0, n).Contains("video_mode=\"SCREEN_MODE_WINDOW\"");
        }
        catch { return true; }
    }

    /// <summary>Partida de cada perfil (formato de PC). El principal sigue en savedata/data.sav como antes.</summary>
    public static string SavePath(Context ctx, int profile)
    {
        string dir = Path.Combine(ctx.GetExternalFilesDir(null).AbsolutePath, "savedata");
        return profile == 1 ? Path.Combine(dir, "desbloqueado", "data.sav") : Path.Combine(dir, "data.sav");
    }

    public static string AchievementsPath(Context ctx) => Path.Combine(ctx.GetExternalFilesDir(null).AbsolutePath, "logros.txt");

    /// <summary>Resolución interna = 354x200 x escala. Igual que el PC pero tope 4 por rendimiento.</summary>
    public static int AutoScale(Context ctx)
    {
        var m = ctx.Resources.DisplayMetrics;
        int w = Math.Max(m.WidthPixels, m.HeightPixels), h = Math.Min(m.WidthPixels, m.HeightPixels);
        return Math.Clamp(Math.Min(w / 354, h / 200), 1, 4);
    }
}

/// <summary>Registro en Android/data/&lt;paquete&gt;/files/log.txt (además de logcat).</summary>
static class Log
{
    static StreamWriter sFile;
    static readonly object sLock = new();

    public static void Start(Context ctx)
    {
        if (sFile != null) return;
        try
        {
            string dir = ctx.GetExternalFilesDir(null).AbsolutePath;
            string path = Path.Combine(dir, "log.txt");
            // conservar el registro de la sesión anterior (por si se cerró de golpe)
            if (File.Exists(path)) File.Copy(path, Path.Combine(dir, "log_anterior.txt"), true);
            sFile = new StreamWriter(path, false) { AutoFlush = true };
            var tee = new TeeWriter(Console.Out, sFile);
            Console.SetOut(tee);
            Console.SetError(tee);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Write("NO CONTROLADA: " + e.ExceptionObject);
            Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (s, e) => Write("NO CONTROLADA (Android): " + e.Exception);
            Write("Fight'N Rage port - " + DateTime.Now);
        }
        catch { }
    }

    public static void Write(string s)
    {
        lock (sLock) { try { sFile?.WriteLine(s); } catch { } }
        Android.Util.Log.Info("FnrPort", s);
    }

    sealed class TeeWriter : TextWriter
    {
        readonly TextWriter a, b;
        public TeeWriter(TextWriter a, TextWriter b) { this.a = a; this.b = b; }
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
        public override void Write(char value) { lock (sLock) { a.Write(value); try { b.Write(value); } catch { } } }
        public override void Write(string value) { lock (sLock) { a.Write(value); try { b.Write(value); } catch { } } }
        public override void WriteLine(string value) { lock (sLock) { a.WriteLine(value); try { b.WriteLine(value); } catch { } } }
    }
}

/// <summary>Junta el registro actual, el anterior y el logcat de la app en un único texto.</summary>
static class LogExport
{
    public static string Build(Context ctx)
    {
        var sb = new System.Text.StringBuilder();
        string dir = ctx.GetExternalFilesDir(null).AbsolutePath;
        sb.AppendLine("=== Fight'N Rage Android - informe " + DateTime.Now + " ===");
        sb.AppendLine("Dispositivo: " + Android.OS.Build.Manufacturer + " " + Android.OS.Build.Model + " | Android " + Android.OS.Build.VERSION.Release + " (API " + (int)Android.OS.Build.VERSION.SdkInt + ")");
        try { sb.AppendLine("Versión app: " + ctx.PackageManager.GetPackageInfo(ctx.PackageName, 0).VersionName); } catch { }
        foreach (var f in new[] { "log_anterior.txt", "log.txt" })
        {
            sb.AppendLine().AppendLine("=== " + f + " ===");
            string p = Path.Combine(dir, f);
            try { sb.AppendLine(File.Exists(p) ? ReadShared(p) : "(no existe)"); } catch (Exception ex) { sb.AppendLine("(error: " + ex.Message + ")"); }
        }
        sb.AppendLine().AppendLine("=== logcat ===");
        try
        {
            var proc = Java.Lang.Runtime.GetRuntime().Exec(new[] { "logcat", "-d", "-t", "3000", "-v", "time" });
            using var r = new StreamReader(proc.InputStream);
            sb.Append(r.ReadToEnd());
        }
        catch (Exception ex) { sb.AppendLine("(no se pudo leer logcat: " + ex.Message + ")"); }
        return sb.ToString();
    }

    static string ReadShared(string p)
    {
        using var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var r = new StreamReader(fs);
        return r.ReadToEnd();
    }

    /// <summary>Guarda el informe en Descargas. Devuelve la ruta visible para el usuario.</summary>
    public static string SaveToDownloads(Context ctx, string text)
    {
        string name = "fightnrage_registro_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt";
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Q)
        {
            var values = new ContentValues();
            values.Put(Android.Provider.MediaStore.IMediaColumns.DisplayName, name);
            values.Put(Android.Provider.MediaStore.IMediaColumns.MimeType, "text/plain");
            values.Put(Android.Provider.MediaStore.IMediaColumns.RelativePath, "Download/");
            var uri = ctx.ContentResolver.Insert(Android.Provider.MediaStore.Downloads.ExternalContentUri, values);
            using var os = ctx.ContentResolver.OpenOutputStream(uri);
            os.Write(bytes, 0, bytes.Length);
            return "Descargas/" + name;
        }
        string path = Path.Combine(ctx.GetExternalFilesDir(null).AbsolutePath, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }
}

/// <summary>En muchas consolas la wifi tira los paquetes de difusión si nadie pide este cerrojo (búsqueda en la wifi).</summary>
static class WifiLock
{
    static Android.Net.Wifi.WifiManager.MulticastLock sLock;
    public static void Acquire(Context ctx)
    {
        try
        {
            if (sLock == null)
            {
                var wm = (Android.Net.Wifi.WifiManager)ctx.ApplicationContext.GetSystemService(Context.WifiService);
                sLock = wm?.CreateMulticastLock("fnr-net");
                sLock?.SetReferenceCounted(false);
            }
            sLock?.Acquire();
        }
        catch (Exception ex) { Console.WriteLine("[FnrNet] MulticastLock: " + ex.Message); }
    }
    public static void Release() { try { sLock?.Release(); } catch { } }

    // Cerrojo de wifi de baja latencia: evita que la wifi entre en ahorro de energía durante la partida
    // en red (es lo que más tirones mete en la misma wifi). Android 10+: modo LOW_LATENCY; antes, HIGH_PERF.
    static Android.Net.Wifi.WifiManager.WifiLock sFast;
    public static void AcquireLowLatency(Context ctx)
    {
        try
        {
            if (sFast == null)
            {
                var wm = (Android.Net.Wifi.WifiManager)ctx.ApplicationContext.GetSystemService(Context.WifiService);
#pragma warning disable CA1422
                var mode = Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Q ? (Android.Net.WifiMode)4 : (Android.Net.WifiMode)3;
#pragma warning restore CA1422
                sFast = wm?.CreateWifiLock(mode, "fnr-net-fast");
                sFast?.SetReferenceCounted(false);
            }
            sFast?.Acquire();
            Acquire(ctx);
        }
        catch (Exception ex) { Console.WriteLine("[FnrNet] WifiLock: " + ex.Message); }
    }
    public static void ReleaseLowLatency() { try { sFast?.Release(); } catch { } }
}

/// <summary>Textos del launcher: castellano si el sistema está en español; si no, inglés.</summary>
static class L
{
    public static bool ES => FnrPort.Lang.Spanish;
    public static string T(string es, string en) => FnrPort.Lang.T(es, en);
}

/// <summary>
/// Música del launcher (canción propia, res/raw/launcher_theme.ogg). Suena mientras se ve alguna
/// pantalla del launcher (contador de pantallas visibles) y se para al entrar en el juego.
/// </summary>
static class LauncherMusic
{
    static Android.Media.MediaPlayer sPlayer;
    static int sVisible;

    public static void Shown(Context ctx) { sVisible++; Update(ctx); }
    public static void Hidden(Context ctx) { sVisible = Math.Max(0, sVisible - 1); Update(ctx); }

    public static void Update(Context ctx)
    {
        bool on = sVisible > 0 && Settings.Load(ctx).Music;
        try
        {
            if (!on) { if (sPlayer != null && sPlayer.IsPlaying) sPlayer.Pause(); return; }
            if (sPlayer == null)
            {
                sPlayer = Android.Media.MediaPlayer.Create(ctx.ApplicationContext, Resource.Raw.launcher_theme);
                if (sPlayer == null) return;
                sPlayer.Looping = true;
                sPlayer.SetVolume(0.6f, 0.6f);
            }
            if (!sPlayer.IsPlaying) sPlayer.Start();
        }
        catch (Exception ex) { Log.Write("Música del launcher: " + ex.Message); }
    }

    public static void Release()
    {
        try { sPlayer?.Release(); } catch { }
        sPlayer = null;
    }
}

/// <summary>
/// Datos de los 20 logros para enseñarlos: nombre y descripción de Steam (assets/achievements/list.tsv,
/// que baja el workflow junto con los iconos) o, si no están, un nombre sacado del identificador.
/// </summary>
static class AchievementInfo
{
    public sealed class Item
    {
        public string Id, NameEn, DescEn, NameEs, DescEs;
        public string Name => L.ES ? (Es.TryGetValue(Id, out var t) ? t.name : !string.IsNullOrEmpty(NameEs) ? NameEs : NameEn) : NameEn;
        public string Desc => L.ES ? (Es.TryGetValue(Id, out var t) ? t.desc : !string.IsNullOrEmpty(DescEs) ? DescEs : DescEn) : DescEn;
    }

    /// <summary>Steam solo los tiene en inglés: traducción propia para el launcher en castellano.</summary>
    static readonly System.Collections.Generic.Dictionary<string, (string name, string desc)> Es = new()
    {
        ["DIRECT_CLEAR_EASY"] = ("Pan comido", "Termina el juego en dificultad Fácil."),
        ["DIRECT_CLEAR_NORMAL"] = ("Suficiente", "Termina el juego en dificultad Normal."),
        ["DIRECT_CLEAR_HARD"] = ("Más que bien", "Termina el juego en dificultad Difícil."),
        ["DIRECT_CLEAR_HARDEST"] = ("Jugador hardcore", "Termina el juego en la dificultad más alta."),
        ["GAL_BLACK_BELT"] = ("Gal dominada", "Consigue el cinturón negro de Gal."),
        ["MINOTAUR_BLACK_BELT"] = ("Ricardo dominado", "Consigue el cinturón negro de Ricardo."),
        ["MASTER_BLACK_BELT"] = ("F.Norris dominado", "Consigue el cinturón negro de F.Norris."),
        ["ALL_COSTUMES_GAL"] = ("Coleccionista: Gal", "Consigue todos los trajes de Gal."),
        ["ALL_COSTUMES_MINOTAUR"] = ("Coleccionista: Ricardo", "Consigue todos los trajes de Ricardo."),
        ["ALL_COSTUMES_MASTER"] = ("Coleccionista: F.Norris", "Consigue todos los trajes de F.Norris."),
        ["ALL_COMPLEMENTS_SOLD"] = ("Coleccionista de complementos", "Consigue todos los complementos."),
        ["ALL_CHARACTERS_SOLD"] = ("Reclutador", "Consigue todos los personajes."),
        ["ANY_CLEAR_EXTRA_SPEED"] = ("¡Más rápido!", "Termina el juego con cualquier velocidad extra."),
        ["ANY_1CC"] = ("Sin continuar", "Termina el juego sin usar continuaciones, en dificultad Normal o superior."),
        ["SCORE_RANK_S_ALL"] = ("Maestro del Score Attack", "Consigue rango S con todos los personajes en el modo Score Attack."),
        ["TIME_ATTACK_RANK_S_ALL_FIX_2"] = ("Maestro del Time Attack", "Consigue rango S con todos los personajes en el modo Time Attack."),
        ["SURVIVAL_RANK_S_ALL"] = ("Maestro de Survival", "Consigue rango S con todos los personajes en el modo Survival."),
        ["VIEW_QUARTER_ENDINGS"] = ("Finales al 25 %", "Descubre el 25 % de los finales."),
        ["VIEW_HALF_ENDINGS"] = ("Finales al 50 %", "Descubre el 50 % de los finales."),
        ["VIEW_ALL_ENDINGS"] = ("Finales al 100 %", "Descubre todos los finales."),
    };

    static System.Collections.Generic.Dictionary<string, Item> sItems;

    public static Item Get(Context ctx, string id)
    {
        Load(ctx);
        return sItems.TryGetValue(id, out var it) ? it : new Item { Id = id, NameEn = Pretty(id), DescEn = "" };
    }

    static void Load(Context ctx)
    {
        if (sItems != null) return;
        sItems = new();
        try
        {
            using var st = ctx.Assets.Open("achievements/list.tsv");
            using var r = new StreamReader(st);
            string line;
            while ((line = r.ReadLine()) != null)
            {
                var p = line.Split('\t');
                if (p.Length < 3) continue;
                sItems[p[0]] = new Item { Id = p[0], NameEn = p[1], DescEn = p[2], NameEs = p.Length > 3 ? p[3] : null, DescEs = p.Length > 4 ? p[4] : null };
            }
        }
        catch { }
    }

    static string Pretty(string id)
    {
        var w = id.Replace("_FIX_2", "").Split('_');
        for (int i = 0; i < w.Length; i++) w[i] = w[i].Length > 0 ? w[i][0] + w[i].Substring(1).ToLowerInvariant() : w[i];
        return string.Join(" ", w);
    }

    public static Android.Graphics.Bitmap Icon(Context ctx, string id, bool unlocked)
    {
        try
        {
            using var st = ctx.Assets.Open("achievements/" + id + (unlocked ? "" : "_off") + ".jpg");
            return Android.Graphics.BitmapFactory.DecodeStream(st);
        }
        catch { return null; }
    }
}
