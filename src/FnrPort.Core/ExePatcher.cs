using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

namespace FnrPort;

/// <summary>
/// Pequeños parches sobre la COPIA del exe que usa el port (el original del usuario no se toca).
/// Todos buscan por nombre de método y por patrón de bytes: si algo no cuadra (otra versión del
/// juego), ese parche se salta sin romper nada.
/// </summary>
public static class ExePatcher
{
    public const string PortCredit = " - PORTED BY JUANMOTE";
    /// <summary>Línea que se añade al final de la pantalla de créditos, debajo de la web.</summary>
    public const string PortCreditLine = "- Ported by Juanmote -";

    public static void Apply(byte[] d)
    {
        try
        {
            var plan = new List<(int off, byte[] bytes)>();
            using (var pe = new PEReader(ImmutableArray.Create(d)))
            {
                var md = pe.GetMetadataReader();
                var ctx = new Ctx(pe, md, d);
                TryRun("créditos", () => PatchCredits(ctx, plan));
                TryRun("configuración de mandos", () => PatchInputConfig(ctx, plan));
                TryRun("créditos (pantalla)", () => PatchCreditsScreen(ctx, plan));
                TryRun("botones por defecto del mando", () => PatchDefaultPad(ctx, plan));
            }
            foreach (var (off, bytes) in plan) bytes.CopyTo(d, off);
        }
        catch (Exception ex) { Console.WriteLine("[FnrPort] Parches del exe: " + ex.Message); }
    }

    static void TryRun(string name, Func<bool> f)
    {
        try { Console.WriteLine($"[FnrPort] Parche {name}: " + (f() ? "aplicado" : "no encontrado (se deja como está)")); }
        catch (Exception ex) { Console.WriteLine($"[FnrPort] Parche {name}: error {ex.Message}"); }
    }

    sealed class Ctx
    {
        public readonly PEReader Pe; public readonly MetadataReader Md; public readonly byte[] D;
        public Ctx(PEReader pe, MetadataReader md, byte[] d) { Pe = pe; Md = md; D = d; }

        public int RvaToOffset(int rva)
        {
            foreach (var s in Pe.PEHeaders.SectionHeaders)
                if (rva >= s.VirtualAddress && rva < s.VirtualAddress + Math.Max(s.VirtualSize, s.SizeOfRawData))
                    return rva - s.VirtualAddress + s.PointerToRawData;
            return -1;
        }

        List<(UserStringHandle h, int bytes)> mDonors;

        /// <summary>
        /// Escribe un texto nuevo en el montón #US encima de un mensaje de error de FMOD (la librería de
        /// sonido del PC, que el port no usa) y devuelve su token para un ldstr. 0 si no cabe.
        /// </summary>
        public int NewString(string text, List<(int, byte[])> plan)
        {
            int usStart = Pe.PEHeaders.MetadataStartOffset + Md.GetHeapMetadataOffset(HeapIndex.UserString);
            if (mDonors == null)
            {
                mDonors = new();
                int usSize = Md.GetHeapSize(HeapIndex.UserString);
                for (var h = MetadataTokens.UserStringHandle(1); MetadataTokens.GetHeapOffset(h) < usSize; h = Md.GetNextHandle(h))
                {
                    if (h.IsNil) break;
                    string s = Md.GetUserString(h);
                    if (s.Length > 70 && (s.Contains("FMOD_") || s.Contains("DSP") || s.Contains("subsound")))
                        mDonors.Add((h, s.Length * 2 + 1));
                }
                mDonors.Sort((a, b) => b.bytes.CompareTo(a.bytes)); // los más grandes primero
            }
            int need = text.Length * 2 + 1;
            for (int i = 0; i < mDonors.Count; i++)
            {
                var (h, bytes) = mDonors[i];
                if (bytes < need) continue;
                int off = usStart + MetadataTokens.GetHeapOffset(h);
                var entry = new List<byte>();
                // longitud comprimida canónica; lo que sobra del donante queda como relleno sin uso
                if (need + 2 > bytes || need > 0x3FFF) continue;
                if (need < 0x80) entry.Add((byte)need);
                else { entry.Add((byte)(0x80 | (need >> 8))); entry.Add((byte)need); }
                entry.AddRange(Encoding.Unicode.GetBytes(text));
                bool wide = false; foreach (char ch in text) if (ch > 0x7E || ch < 0x20) wide = true;
                entry.Add((byte)(wide ? 1 : 0));
                plan.Add((off, entry.ToArray()));
                mDonors.RemoveAt(i);
                return MetadataTokens.GetToken(h);
            }
            return 0;
        }

        public UserStringHandle FindString(string text)
        {
            int usSize = Md.GetHeapSize(HeapIndex.UserString);
            for (var h = MetadataTokens.UserStringHandle(1); MetadataTokens.GetHeapOffset(h) < usSize; h = Md.GetNextHandle(h))
            {
                if (h.IsNil) break;
                if (Md.GetUserString(h) == text) return h;
            }
            return default;
        }

        public string TypeName(TypeDefinitionHandle h)
        {
            var t = Md.GetTypeDefinition(h);
            string n = Md.GetString(t.Name), ns = Md.GetString(t.Namespace);
            if (t.IsNested) return TypeName(t.GetDeclaringType()) + "/" + n;
            return ns.Length > 0 ? ns + "." + n : n;
        }

        public MethodDefinitionHandle Method(string type, string name, int paramCount)
        {
            foreach (var th in Md.TypeDefinitions)
            {
                if (TypeName(th) != type) continue;
                foreach (var mh in Md.GetTypeDefinition(th).GetMethods())
                {
                    var m = Md.GetMethodDefinition(mh);
                    if (Md.GetString(m.Name) == name && m.GetParameters().Count == paramCount) return mh;
                }
            }
            return default;
        }

        public IEnumerable<MethodDefinitionHandle> MethodsOf(string type)
        {
            foreach (var th in Md.TypeDefinitions)
                if (TypeName(th) == type)
                    foreach (var mh in Md.GetTypeDefinition(th).GetMethods()) yield return mh;
        }

        /// <summary>Inicio y longitud del código IL de un método en el archivo.</summary>
        public (int start, int len) Body(MethodDefinitionHandle mh)
        {
            int rva = Md.GetMethodDefinition(mh).RelativeVirtualAddress;
            if (rva == 0) return (-1, 0);
            int off = RvaToOffset(rva);
            byte h = D[off];
            if ((h & 3) == 2) return (off + 1, h >> 2);                       // cabecera tiny
            int size = (D[off + 1] >> 4) * 4;                                  // cabecera fat
            return (off + size, BitConverter.ToInt32(D, off + 4));
        }
    }

    static int Token(MethodDefinitionHandle h) => MetadataTokens.GetToken(h);

    static int Find(byte[] d, int start, int len, byte[] pat)
    {
        for (int i = start; i <= start + len - pat.Length; i++)
        {
            int j = 0;
            while (j < pat.Length && d[i + j] == pat[j]) j++;
            if (j == pat.Length) return i;
        }
        return -1;
    }

    static byte[] Op(byte op, int token) { var b = new byte[5]; b[0] = op; BitConverter.GetBytes(token).CopyTo(b, 1); return b; }
    static byte[] Concat(params byte[][] parts) { var l = new List<byte>(); foreach (var p in parts) l.AddRange(p); return l.ToArray(); }

    // ------------------------------------------------------------------ créditos del título

    /// <summary>
    /// Añade " - PORTED BY JUANMOTE" al texto de créditos de la pantalla de título. El texto nuevo
    /// es más largo que el original, así que se escribe encima de un mensaje de error de FMOD (la
    /// librería de sonido del PC, que el port no usa) y el título pasa a apuntar a ese texto.
    /// </summary>
    static bool PatchCredits(Ctx c, List<(int, byte[])> plan)
    {
        var md = c.Md;
        int usSize = md.GetHeapSize(HeapIndex.UserString);
        UserStringHandle credits = default;
        string creditsText = null;
        for (var h = MetadataTokens.UserStringHandle(1); MetadataTokens.GetHeapOffset(h) < usSize; h = md.GetNextHandle(h))
        {
            if (h.IsNil) break;
            string s = md.GetUserString(h);
            if (s.StartsWith("GAME BY ") && s.Contains(" - MUSIC BY ")) { credits = h; creditsText = s; break; }
        }
        if (creditsText == null) return false;
        if (creditsText.Contains("PORTED BY")) return true;
        int tok = c.NewString(creditsText + PortCredit, plan);
        if (tok == 0) return false;

        // Que el título use el texto nuevo (ldstr <créditos> -> ldstr <nuevo>).
        var oldLd = Op(0x72, MetadataTokens.GetToken(credits));
        var newLd = Op(0x72, tok);
        bool any = false;
        foreach (var mh in c.MethodsOf("BeatEmUpGame.GameStates.TitleState"))
        {
            var (start, len) = c.Body(mh);
            if (start < 0) continue;
            for (int i = Find(c.D, start, len, oldLd); i >= 0; i = Find(c.D, i + 5, start + len - i - 5, oldLd))
            {
                plan.Add((i, newLd));
                any = true;
            }
        }
        return any;
    }

    // ------------------------------------------------------------------ pantalla de créditos

    /// <summary>
    /// Pantalla de créditos (menú principal): al final, debajo de la web de SebaGamesDev, añade
    /// «- Ported by Juanmote -». Los dos AddMargin() del final (14 bytes) pasan a ser
    /// AddTextLine("- Ported by Juanmote -", false) (12 bytes) + 2 nop.
    /// </summary>
    static bool PatchCreditsScreen(Ctx c, List<(int, byte[])> plan)
    {
        var ctor = c.Method("BeatEmUpGame.GameUI.CreditsComponent", ".ctor", 2);
        var addText = c.Method("BeatEmUpGame.GameUI.CreditsComponent", "AddTextLine", 2);
        var addMargin = c.Method("BeatEmUpGame.GameUI.CreditsComponent", "AddMargin", 1);
        var web = c.FindString("HTTP://SEBAGAMESDEV.COM");
        if (ctor.IsNil || addText.IsNil || addMargin.IsNil || web.IsNil) return false;
        var (start, len) = c.Body(ctor);
        if (start < 0) return false;
        var margin = Concat(new byte[] { 0x02, 0x1E }, Op(0x28, Token(addMargin)));      // ldarg.0 ldc.i4.8 call AddMargin
        var tail = Concat(new byte[] { 0x02 }, Op(0x72, MetadataTokens.GetToken(web)), new byte[] { 0x16 });
        int w = Find(c.D, start, len, tail);
        if (w < 0) return false;
        int m = w + tail.Length + 5;                                                         // tras el call AddTinyTextLine
        if (Find(c.D, m, 14, Concat(margin, margin)) != m) return false;
        int tok = c.NewString(PortCreditLine, plan);
        if (tok == 0) return false;
        plan.Add((m, Concat(new byte[] { 0x02 }, Op(0x72, tok), new byte[] { 0x16 }, Op(0x28, Token(addText)), new byte[] { 0x00, 0x00 })));
        return true;
    }

    // ------------------------------------------------------------------ botones por defecto del mando

    /// <summary>
    /// Configuración de mando por defecto (Data.CreateXboxGamePad): en el PC es X golpe, A salto y B
    /// especial. En el port, como en la Odin: Y golpe, B salto y A especial. Solo cambian los tres números.
    /// </summary>
    static bool PatchDefaultPad(Ctx c, List<(int, byte[])> plan)
    {
        var mh = c.Method("BeatEmUpGame.GameUI.InputConfigComponent/Data", "CreateXboxGamePad", 1);
        if (mh.IsNil) return false;
        var (start, len) = c.Body(mh);
        if (start < 0) return false;
        // data.Indexs[num++] = N  ->  ldloc.0 dup ldc.i4.1 add stloc.0 ldc.i4.N stelem.i4
        var pre = new byte[] { 0x06, 0x25, 0x17, 0x58, 0x0A };
        var pos = new List<int>();
        for (int i = Find(c.D, start, len, pre); i >= 0 && pos.Count < 9; i = Find(c.D, i + 1, start + len - i - 1, pre))
            if (c.D[i + 6] == 0x9E) pos.Add(i + 5);
        // arriba, abajo, izq., der., golpe, salto, especial, start, back
        var expect = new byte[] { 0x17, 0x17, 0x16, 0x16, 0x18, 0x16, 0x17, 0x1D, 0x1C };
        if (pos.Count != 9) return false;
        for (int k = 0; k < 9; k++) if (c.D[pos[k]] != expect[k]) return false;
        plan.Add((pos[4], new byte[] { 0x19 })); // golpe = 3 (Y)
        plan.Add((pos[5], new byte[] { 0x17 })); // salto = 1 (B)
        plan.Add((pos[6], new byte[] { 0x16 })); // especial = 0 (A)
        return true;
    }

    // ------------------------------------------------------------------ pantalla DEVICE CONFIGURATION

    /// <summary>
    /// La pantalla de configurar dispositivos del PC pide teclado: ESC para salir / volver a configurar /
    /// cancelar y ENTER para aceptar. Se cambian esas comprobaciones por las del juego que aceptan
    /// teclado o mando: Esc -> IsPressedAnyCancelation (Esc o el botón BACK = SELECT) y
    /// Enter -> IsPressedAnyStart (START; en teclado, la tecla START configurada, Enter de serie).
    /// Los textos de la pantalla los cambia GameHooks (ESC -> SELECT, ENTER -> START).
    /// </summary>
    static bool PatchInputConfig(Ctx c, List<(int, byte[])> plan)
    {
        var process = c.Method("BeatEmUpGame.GameUI.InputConfigComponent", "Process", 2);
        var isPressed = c.Method("SebaGames.Framework.Keyboard", "IsPressed", 1);
        var inst = c.Method("BeatEmUpGame.Data.GameSession", "get_Inst", 0);
        var start_ = c.Method("BeatEmUpGame.Data.GameSession", "IsPressedAnyStart", 0);
        var cancel = c.Method("BeatEmUpGame.Data.GameSession", "IsPressedAnyCancelation", 0);
        if (process.IsNil || isPressed.IsNil || inst.IsNil || start_.IsNil || cancel.IsNil) return false;
        var (start, len) = c.Body(process);
        if (start < 0) return false;
        var call = Op(0x28, Token(isPressed));
        var escCheck = Concat(new byte[] { 0x20, 0x00, 0x01, 0x00, 0x00 }, call);   // ldc.i4 256 (Esc)
        var enterCheck = Concat(new byte[] { 0x20, 0x16, 0x01, 0x00, 0x00 }, call); // ldc.i4 278 (Enter)
        var toCancel = Concat(Op(0x28, Token(inst)), Op(0x6F, Token(cancel)));
        var toStart = Concat(Op(0x28, Token(inst)), Op(0x6F, Token(start_)));
        int n = 0;
        for (int i = Find(c.D, start, len, escCheck); i >= 0; i = Find(c.D, i + 10, start + len - i - 10, escCheck)) { plan.Add((i, toCancel)); n++; }
        for (int i = Find(c.D, start, len, enterCheck); i >= 0; i = Find(c.D, i + 10, start + len - i - 10, enterCheck)) { plan.Add((i, toStart)); n++; }
        Console.WriteLine($"[FnrPort] Configurar mandos: {n} comprobaciones de teclado pasadas al mando");
        return n >= 2;
    }
}
