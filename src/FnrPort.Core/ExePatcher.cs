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
        int usStart = c.Pe.PEHeaders.MetadataStartOffset + md.GetHeapMetadataOffset(HeapIndex.UserString);
        int usSize = md.GetHeapSize(HeapIndex.UserString);
        UserStringHandle credits = default, donor = default;
        string creditsText = null;
        int donorBytes = 0;
        for (var h = MetadataTokens.UserStringHandle(1); MetadataTokens.GetHeapOffset(h) < usSize; h = md.GetNextHandle(h))
        {
            if (h.IsNil) break;
            string s = md.GetUserString(h);
            if (creditsText == null && s.StartsWith("GAME BY ") && s.Contains(" - MUSIC BY ")) { credits = h; creditsText = s; }
            // Mensajes de error de FMOD: nunca se muestran en el port.
            if (s.Length > 120 && (s.Contains("FMOD_") || s.Contains("DSP") || s.Contains("subsound")))
            {
                int bytes = s.Length * 2 + 1;
                if (bytes > donorBytes) { donor = h; donorBytes = bytes; }
            }
        }
        if (creditsText == null || donor.IsNil) return false;
        if (creditsText.Contains("PORTED BY")) return true;
        string text = creditsText + PortCredit;
        int need = text.Length * 2 + 1;
        if (need > donorBytes || need < 128 || need > 0x3FFF) return false;

        // Escribir el texto nuevo en el sitio del donante (longitud comprimida de 2 bytes, igual que el original).
        int donorOff = usStart + MetadataTokens.GetHeapOffset(donor);
        if ((c.D[donorOff] & 0xC0) != 0x80) return false;
        var entry = new byte[2 + need];
        entry[0] = (byte)(0x80 | (need >> 8)); entry[1] = (byte)need;
        Encoding.Unicode.GetBytes(text).CopyTo(entry, 2);
        entry[^1] = 1; // tiene caracteres fuera de ASCII (Á, Í)
        plan.Add((donorOff, entry));

        // Que el título use el texto nuevo (ldstr <créditos> -> ldstr <donante>).
        var oldLd = Op(0x72, MetadataTokens.GetToken(credits));
        var newLd = Op(0x72, MetadataTokens.GetToken(donor));
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

    // ------------------------------------------------------------------ pantalla DEVICE CONFIGURATION

    /// <summary>
    /// La pantalla de configurar dispositivos del PC pide teclado: «WHEN READY, PRESS 'ESC'» para salir y
    /// «Press 'ENTER' to accept» para confirmar. Se cambian esas dos comprobaciones por las del juego que
    /// aceptan teclado o mando: Esc -> IsPressedAnyBackOnMenu (BACK/SELECT o Esc) y
    /// Enter -> IsPressedAnyAccept (START, JUMP o Enter). La cancelación mientras se asignan botones se deja igual.
    /// </summary>
    static bool PatchInputConfig(Ctx c, List<(int, byte[])> plan)
    {
        var process = c.Method("BeatEmUpGame.GameUI.InputConfigComponent", "Process", 2);
        var isPressed = c.Method("SebaGames.Framework.Keyboard", "IsPressed", 1);
        var inst = c.Method("BeatEmUpGame.Data.GameSession", "get_Inst", 0);
        var accept = c.Method("BeatEmUpGame.Data.GameSession", "IsPressedAnyAccept", 0);
        var back = c.Method("BeatEmUpGame.Data.GameSession", "IsPressedAnyBackOnMenu", 0);
        if (process.IsNil || isPressed.IsNil || inst.IsNil || accept.IsNil || back.IsNil) return false;
        var (start, len) = c.Body(process);
        if (start < 0) return false;
        var call = Op(0x28, Token(isPressed));
        var escCheck = Concat(new byte[] { 0x20, 0x00, 0x01, 0x00, 0x00 }, call);   // ldc.i4 256 (Esc)
        var enterCheck = Concat(new byte[] { 0x20, 0x16, 0x01, 0x00, 0x00 }, call); // ldc.i4 278 (Enter)
        int esc = Find(c.D, start, len, escCheck);      // la primera: «cuando estés listo, pulsa ESC»
        int enter = Find(c.D, start, len, enterCheck);  // «¿es correcta la configuración? ENTER»
        if (esc < 0 || enter < 0) return false;
        plan.Add((esc, Concat(Op(0x28, Token(inst)), Op(0x6F, Token(back)))));
        plan.Add((enter, Concat(Op(0x28, Token(inst)), Op(0x6F, Token(accept)))));
        return true;
    }
}
