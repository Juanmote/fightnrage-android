using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace FnrPort;

/// <summary>
/// Añadidos del port que miran dentro del juego mientras corre: perfil con todo desbloqueado,
/// textos de la pantalla de configurar mandos y vibración por temblores de pantalla.
/// Todo lo que toca tipos del juego va por reflexión o en métodos separados (NoInlining) para que
/// el JIT no pida el exe antes de tiempo.
/// </summary>
public static class GameHooks
{
    /// <summary>Perfil «todo desbloqueado»: usa el interruptor del propio juego (Program.ENABLED_ALL_CONTENT).</summary>
    public static bool AllContent;

    static bool sTextsDone;
    static int sTextTries;
    static object sField;            // GameField.Inst de la partida en curso
    static FieldInfo sWorldShake, sGroundShake, sGroundMag;
    static PropertyInfo sFieldInst;
    static float sLastWorld, sLastGround = 1f;
    static bool sNoField;

    /// <summary>Lo llama PortCore al arrancar el motor (el juego ya ha puesto sus valores por defecto).</summary>
    internal static void OnEngineInitialize()
    {
        sTextsDone = false; sTextTries = 0;
        if (AllContent)
        {
            try { SetAllContent(); Console.WriteLine("[FnrPort] Perfil con todo desbloqueado (ENABLED_ALL_CONTENT)"); }
            catch (Exception ex) { Console.WriteLine("[FnrPort] No se pudo activar todo el contenido: " + ex.Message); }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void SetAllContent() => BeatEmUpGame.Program.ENABLED_ALL_CONTENT = true;

    /// <summary>Una vez por actualización del juego (60 por segundo).</summary>
    static int sTicks;
    static readonly string sTestReq = Environment.GetEnvironmentVariable("FNR_TESTREQ");

    internal static void Tick()
    {
        // Prueba (solo PC): FNR_TESTREQ=NOMBRE hace que el juego consiga ese requisito a los 10 s, por su camino normal.
        if (sTestReq != null && ++sTicks == 600)
            try
            {
                var sd = Type.GetType("BeatEmUpGame.Data.SaveData, FIGHT_N_RAGE");
                var req = Enum.Parse(Type.GetType("BeatEmUpGame.Constants+REQ, FIGHT_N_RAGE"), sTestReq);
                var inst = sd.GetProperty("Inst", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                sd.GetMethod("MeetNewRequeriment", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Invoke(inst, new[] { req });
                Console.WriteLine("[FnrPort] Prueba: requisito " + sTestReq + " conseguido");
            }
            catch (Exception ex) { Console.WriteLine("[FnrPort] Prueba de requisito: " + ex); }

        if (!sTextsDone && (++sTextTries % 30) == 1) sTextsDone = PatchTexts();
        ShakeRumble();
    }

    // ------------------------------------------------------------ textos de DEVICE CONFIGURATION

    /// <summary>
    /// La pantalla de configurar mandos habla de las teclas ESC y ENTER del PC. En el port esas
    /// comprobaciones aceptan el mando (ver ExePatcher), así que el texto pasa a SELECT y START en
    /// todos los idiomas.
    /// </summary>
    static readonly string[] sConfigKeys =
    {
        "WHEN READY, PRESS 'ESC' KEY",
        "Press 'ENTER' to accept or 'ESCAPE' to config again",
        "press 'ESC' key to cancel.",
    };

    static bool PatchTexts()
    {
        try
        {
            var t = Type.GetType("SebaGames.Services.LocalizableTextManager, FIGHT_N_RAGE");
            var inst = t?.GetProperty("Inst", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (inst == null) return false;
            if (t.GetField("mTextByLanguage", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(inst)
                is not Dictionary<string, Dictionary<string, string>> langs || langs.Count == 0) return false;
            int n = 0;
            foreach (var (lang, texts) in langs)
                foreach (var key in sConfigKeys)
                {
                    string cur = texts.TryGetValue(key, out var v) ? v : key;
                    string fixedText = PadWording(cur);
                    if (fixedText != cur) { texts[key] = fixedText; n++; }
                    Console.WriteLine($"[FnrPort] Texto [{lang}] -> \"{fixedText.Replace("\n", " | ")}\"");
                }
            Console.WriteLine($"[FnrPort] Textos de configurar mandos adaptados al mando ({n})");
            return true;
        }
        catch (Exception ex) { Console.WriteLine("[FnrPort] Textos: " + ex.Message); return true; }
    }

    static string PadWording(string s)
    {
        // ESC/ESCAPE/ESCAPAR... -> SELECT ; ENTER/INTRO... -> START (entre comillas o no)
        string r = System.Text.RegularExpressions.Regex.Replace(s, @"\b(ESCAPE|ESCAPAR|ESC|Esc|Escape|ÉCHAP|ECHAP)\b", "SELECT");
        r = System.Text.RegularExpressions.Regex.Replace(r, @"\b(ENTER|ENTRAR|INTRO|Enter|Intro|ENTRÉE|ENTREE|Entrée)\b", "START");
        r = System.Text.RegularExpressions.Regex.Replace(r, @"'(SELECT|START)' (KEY|key)", "'$1'");
        return r;
    }

    // ------------------------------------------------------------ vibración por temblor de pantalla

    static void ShakeRumble()
    {
        if (Rumble.Sink == null || sNoField) return;
        try
        {
            if (sFieldInst == null)
            {
                var t = Type.GetType("BeatEmUpGame.Data.GameField, FIGHT_N_RAGE");
                if (t == null) { sNoField = true; return; }
                const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
                sFieldInst = t.GetProperty("Inst", BindingFlags.Public | BindingFlags.Static);
                sWorldShake = t.GetField("mWorldShakeTime", F);
                sGroundShake = t.GetField("mGroundShakeTime", F);
                sGroundMag = t.GetField("mGroundShakeMag", F);
            }
            var f = sFieldInst.GetValue(null);
            if (f == null) return;
            if (!ReferenceEquals(f, sField)) { sField = f; sLastWorld = 0; sLastGround = 1f; }
            if (sWorldShake != null)
            {
                float w = (float)sWorldShake.GetValue(f);
                if (w > sLastWorld + 0.02f) Rumble.Fire((int)Math.Clamp(w * 1000f, 80, 450), 220, "temblor de pantalla");
                sLastWorld = w;
            }
            if (sGroundShake != null)
            {
                float g = (float)sGroundShake.GetValue(f);
                if (g < sLastGround - 0.05f && g < 0.2f)
                {
                    float mag = sGroundMag != null ? (float)sGroundMag.GetValue(f) : 4f;
                    Rumble.Fire((int)Math.Clamp(120 + mag * 25, 120, 400), (int)Math.Clamp(120 + mag * 20, 140, 255), "temblor del suelo");
                }
                sLastGround = g;
            }
        }
        catch { }
    }
}

/// <summary>Vibración: el juego no tiene, el port la añade en golpes, caídas y temblores.</summary>
public static class Rumble
{
    /// <summary>La pone la plataforma: (milisegundos, intensidad 1-255).</summary>
    public static Action<int, int> Sink;
    /// <summary>0..1 (ajuste del launcher).</summary>
    public static float Strength = 1f;
    /// <summary>
    /// «De tranquis»: solo golpes y caídas de los personajes. Sin temblores de pantalla (el título y los menús
    /// tiemblan al ritmo de la música) ni explosiones, puertas, cristales, bidones...
    /// </summary>
    public static bool Calm;

    static long sUntil;
    static int sAmp;

    /// <summary>Qué ha provocado la última vibración (para el registro).</summary>
    public static string LastSource = "";

    public static void Fire(int ms, int amp, string source = "", bool calmOk = false)
    {
        if (Calm && !calmOk) return;
        LastSource = source;
        var sink = Sink;
        if (sink == null || Strength <= 0f || ms <= 0) return;
        long now = Environment.TickCount64;
        // No pisar una vibración más fuerte que todavía está sonando.
        if (now < sUntil && amp < sAmp) return;
        int a = Math.Clamp((int)(amp * Strength), 1, 255);
        sUntil = now + ms; sAmp = amp;
        try { sink(ms, a); } catch { }
    }

    /// <summary>
    /// Vibración para cada efecto de sonido del juego (por nombre de archivo). 0 = nada.
    /// calm = también suena en el modo «De tranquis» (golpes y caídas de los personajes).
    /// </summary>
    public static (int ms, int amp, bool calm) ForSound(string file)
    {
        if (string.IsNullOrEmpty(file)) return (0, 0, false);
        string n = Path.GetFileNameWithoutExtension(file.Replace('\\', '/')).ToLowerInvariant();
        if (n.StartsWith("rock_huge_impact")) return (220, 255, false);
        if (n == "impact0" || n.StartsWith("hit_on_metal") || n == "metal_pipe" || n == "broken_window" || n == "door_break"
            || n == "drum_break" || n == "concrete_impact" || n == "marble_impact" || n.StartsWith("shotgun")) return (75, 210, false);
        if (n.StartsWith("final_") || n == "whiplash_strong" || n == "bone_crushed") return (75, 210, true);
        if (n == "body_fall" || n == "bone_fall") return (60, 150, true);
        if (n.StartsWith("punch") || n.StartsWith("kick") || n == "slap" || n == "cut0" || n == "whiplash_light") return (32, 110, true);
        return (0, 0, false);
    }
}

/// <summary>
/// Logros: los 20 de Steam/GOG. El juego avisa al launcher con Constants.REQ (igual que en PC, donde
/// se mandan a Steam o GOG); aquí se guardan en la consola con la fecha. Al cargar la partida el
/// juego vuelve a avisar de todos los que ya tiene, así que los de una partida importada de PC
/// aparecen solos.
/// </summary>
public static class Achievements
{
    public static readonly string[] All =
    {
        "DIRECT_CLEAR_EASY", "DIRECT_CLEAR_NORMAL", "DIRECT_CLEAR_HARD", "DIRECT_CLEAR_HARDEST", "ANY_1CC", "ANY_CLEAR_EXTRA_SPEED",
        "GAL_BLACK_BELT", "MINOTAUR_BLACK_BELT", "MASTER_BLACK_BELT", "VIEW_QUARTER_ENDINGS", "VIEW_HALF_ENDINGS", "VIEW_ALL_ENDINGS",
        "SCORE_RANK_S_ALL", "TIME_ATTACK_RANK_S_ALL_FIX_2", "SURVIVAL_RANK_S_ALL", "ALL_CHARACTERS_SOLD", "ALL_COMPLEMENTS_SOLD",
        "ALL_COSTUMES_GAL", "ALL_COSTUMES_MINOTAUR", "ALL_COSTUMES_MASTER",
    };

    /// <summary>Archivo donde se guardan (id TAB fecha). null = no se guardan (perfil con todo desbloqueado).</summary>
    public static string File;
    /// <summary>Aviso de logro nuevo (id). Lo usa la plataforma para enseñarlo.</summary>
    public static Action<string> OnUnlocked;

    static readonly object sLock = new();

    public static Dictionary<string, DateTime> Load(string file)
    {
        var d = new Dictionary<string, DateTime>();
        try
        {
            if (file == null || !System.IO.File.Exists(file)) return d;
            foreach (var line in System.IO.File.ReadAllLines(file))
            {
                var p = line.Split('\t');
                if (p.Length < 1 || Array.IndexOf(All, p[0]) < 0) continue;
                DateTime when = DateTime.MinValue;
                if (p.Length > 1) DateTime.TryParse(p[1], System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out when);
                d[p[0]] = when;
            }
        }
        catch (Exception ex) { Console.WriteLine("[FnrPort] Logros: " + ex.Message); }
        return d;
    }

    /// <summary>Llamado por el juego (ExternalCall con un Constants.REQ).</summary>
    internal static void Report(string req)
    {
        string id = req == "TIME_ATTACK_RANK_S_ALL" ? req + "_FIX_2" : req;
        if (Array.IndexOf(All, id) < 0 || File == null) return;
        bool isNew;
        lock (sLock)
        {
            var d = Load(File);
            isNew = !d.ContainsKey(id);
            if (isNew)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(File));
                System.IO.File.AppendAllText(File, id + "\t" + DateTime.Now.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) + "\n");
            }
        }
        if (isNew)
        {
            Console.WriteLine("[FnrPort] Logro conseguido: " + id);
            try { OnUnlocked?.Invoke(id); } catch { }
        }
    }
}

/// <summary>Idioma de los textos del port: castellano si el sistema está en español; si no, inglés.</summary>
public static class Lang
{
    public static bool Spanish = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es";
    public static string T(string es, string en) => Spanish ? es : en;
}
