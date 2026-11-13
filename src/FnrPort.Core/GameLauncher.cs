using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using SebaGames.Framework;

namespace FnrPort;

/// <summary>
/// Carga el FIGHT_N_RAGE.exe del usuario y lo arranca con el backend del port.
/// Hace lo mismo que el launcher de GOG (GogWindowsLauncher.Program) sin Galaxy ni Windows.
/// </summary>
public static class GameLauncher
{
    public const string ExeName = "FIGHT_N_RAGE.exe";
    static string sExePath;
    static string sSavePath;
    static object sCore; // PortCore (object para no cargar el exe al tocar esta clase)
    static bool sResolverInstalled;

    public sealed class Options
    {
        public string GameDir;          // carpeta con FIGHT_N_RAGE.exe y data/
        public string CacheDir;         // donde dejar el exe preparado
        public string SaveFile;         // ruta del data.sav (formato de PC, gzip)
        public int WindowScale;         // Constants.WINDOW_SCALE (resolución interna = 354x200 x escala)
        public bool Vsync = true;
        public object Audio;            // IAudioEngine; null = sin sonido (se declara object para no cargar el exe antes de tiempo)
        public string[] Args = Array.Empty<string>();
        public Action<object> ConfigureCore; // recibe el PortCore
    }

    /// <summary>Comprueba que la carpeta tiene lo necesario. Devuelve null si está bien, o el problema.</summary>
    public static string Validate(string gameDir)
    {
        if (!File.Exists(Path.Combine(gameDir, ExeName))) return $"Falta {ExeName}";
        if (!File.Exists(Path.Combine(gameDir, "data", "0.dat"))) return "Falta la carpeta data (data/0.dat)";
        return null;
    }

    /// <summary>
    /// Copia el exe quitando la marca "solo 32 bits" de la cabecera .NET (como "corflags /32BIT-").
    /// Sin esto los runtimes de 64 bits se niegan a cargarlo. No se toca nada más.
    /// </summary>
    public static string PrepareExe(string gameDir, string cacheDir)
    {
        string src = Path.Combine(gameDir, ExeName);
        Directory.CreateDirectory(cacheDir);
        string dst = Path.Combine(cacheDir, ExeName);
        var d = File.ReadAllBytes(src);
        int pe = BitConverter.ToInt32(d, 0x3c);
        ushort magic = BitConverter.ToUInt16(d, pe + 24);
        int dd = pe + 24 + (magic == 0x10b ? 96 : 112);
        int cliRva = BitConverter.ToInt32(d, dd + 14 * 8);
        int nsec = BitConverter.ToUInt16(d, pe + 6);
        int so = pe + 24 + BitConverter.ToUInt16(d, pe + 20);
        int off = -1;
        for (int i = 0; i < nsec; i++)
        {
            int vs = BitConverter.ToInt32(d, so + i * 40 + 8), va = BitConverter.ToInt32(d, so + i * 40 + 12);
            int rs = BitConverter.ToInt32(d, so + i * 40 + 16), rp = BitConverter.ToInt32(d, so + i * 40 + 20);
            if (cliRva >= va && cliRva < va + Math.Max(vs, rs)) off = cliRva - va + rp;
        }
        if (off < 0) throw new InvalidDataException("El exe no parece un ensamblado .NET");
        uint flags = BitConverter.ToUInt32(d, off + 16);
        flags &= ~0x2u;      // COMIMAGE_FLAGS_32BITREQUIRED
        flags &= ~0x20000u;  // COMIMAGE_FLAGS_32BITPREFERRED
        BitConverter.GetBytes(flags).CopyTo(d, off + 16);
        if (!File.Exists(dst) || !SameBytes(dst, d)) File.WriteAllBytes(dst, d);
        return dst;
    }

    static bool SameBytes(string path, byte[] data)
    {
        var fi = new FileInfo(path);
        if (fi.Length != data.Length) return false;
        var cur = File.ReadAllBytes(path);
        for (int i = 0; i < cur.Length; i++) if (cur[i] != data[i]) return false;
        return true;
    }

    /// <summary>Hace que cualquier referencia a "FIGHT_N_RAGE" use el exe del usuario.</summary>
    public static void InstallResolver(string exePath)
    {
        sExePath = exePath;
        if (sResolverInstalled) return;
        sResolverInstalled = true;
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            var n = new AssemblyName(e.Name).Name;
            if (n == "FIGHT_N_RAGE") return Assembly.LoadFrom(sExePath);
            return null;
        };
    }

    /// <summary>
    /// Paso 1 (antes de tocar cualquier tipo del juego): valida la carpeta, prepara el exe e
    /// instala el resolvedor de ensamblados.
    /// </summary>
    public static void Prepare(string gameDir, string cacheDir)
    {
        string problem = Validate(gameDir);
        if (problem != null) throw new FileNotFoundException(problem);
        InstallResolver(PrepareExe(gameDir, cacheDir));
    }

    /// <summary>Paso 2: arranca el juego. Bloquea hasta que se cierra.</summary>
    public static int Run(IPortHost host, Options o)
    {
        if (sExePath == null) Prepare(o.GameDir, o.CacheDir);
        return RunInternal(host, o);
    }

    // Separado para que el JIT no necesite FIGHT_N_RAGE hasta que el resolver esté instalado.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static int RunInternal(IPortHost host, Options o)
    {
        Directory.SetCurrentDirectory(o.GameDir);
        sSavePath = o.SaveFile;

        BeatEmUpGame.Program.STEAM_VERSION = true;   // lo mismo que hace el launcher de GOG
        BeatEmUpGame.Program.VSYNC = o.Vsync;
        BeatEmUpGame.Constants.WINDOW_SCALE = Math.Max(1, o.WindowScale);

        var audio = (o.Audio as IAudioEngine) ?? new Empty_AudioImplementation.Empty_AudioEngine();
        var core = new PortCore(host, audio);
        sCore = core;
        o.ConfigureCore?.Invoke(core);
        string save = LoadSave();
        int r = BeatEmUpGame.Program.Run(core, ExternalCall, save, o.Args);
        StoreSave();
        return r;
    }

    // Peticiones del juego al launcher (ver GogWindowsLauncher.Program.CallMethod).
    static object ExternalCall(object obj)
    {
        try
        {
            if (obj is string s)
            {
                if (s == "paste") return Clipboard?.Invoke(null) ?? "";
                if (s.StartsWith("copy:")) Clipboard?.Invoke(s.Substring(5));
                return null;
            }
            if (obj is int i)
            {
                // 0/1/2/3: pantalla completa / ventana -> en Android siempre pantalla completa.
                if (i == 4) StoreSave();
                return null;
            }
            // Constants.REQ = logros de GOG/Steam: aquí no hay plataforma de logros.
        }
        catch (Exception ex) { Console.WriteLine("[FnrPort] ExternalCall: " + ex); }
        return null;
    }

    /// <summary>Portapapeles opcional que puede aportar la plataforma (null = leer).</summary>
    public static Func<string, string> Clipboard;

    static string LoadSave()
    {
        if (sSavePath == null || !File.Exists(sSavePath)) return "";
        try
        {
            using var fs = File.OpenRead(sSavePath);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            using var sr = new StreamReader(gz);
            return sr.ReadToEnd();
        }
        catch (Exception ex)
        {
            Console.WriteLine("[FnrPort] Partida no comprimida o dañada (" + ex.Message + "), se lee como texto.");
            return File.ReadAllText(sSavePath);
        }
    }

    public static void StoreSave()
    {
        if (sSavePath == null || BeatEmUpGame.Program.DISABLED_SETTING_STORAGE) return;
        string xml = BeatEmUpGame.ExternalCommands.GetSaveDataInnerXml();
        if (string.IsNullOrEmpty(xml)) { Console.WriteLine("[FnrPort] ¿Partida corrupta? No se guarda."); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(sSavePath));
        string tmp = sSavePath + ".tmp";
        using (var fs = File.Create(tmp))
        using (var gz = new GZipStream(fs, CompressionMode.Compress))
        {
            var bytes = Encoding.ASCII.GetBytes(xml);
            gz.Write(bytes, 0, bytes.Length);
        }
        File.Copy(tmp, sSavePath, true);
        File.Delete(tmp);
        Console.WriteLine("[FnrPort] Partida guardada en " + sSavePath);
    }
}
