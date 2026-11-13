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

    public static Settings Load(Context ctx)
    {
        var p = ctx.GetSharedPreferences("fnr", FileCreationMode.Private);
        return new Settings { Scale = p.GetInt("scale", 0) };
    }

    public void Save(Context ctx)
    {
        var p = ctx.GetSharedPreferences("fnr", FileCreationMode.Private).Edit();
        p.PutInt("scale", Scale); p.Apply();
    }

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
