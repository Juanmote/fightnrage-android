using System;
using System.IO;
using System.IO.Compression;

namespace FnrPort;

/// <summary>Decodificador/codificador PNG mínimo (sin dependencias). Devuelve RGBA de 8 bits, fila 0 arriba.</summary>
public static class PngCodec
{
    public struct Image { public int Width, Height; public byte[] Rgba; }

    public static Image Decode(Stream input)
    {
        var ms = new MemoryStream();
        input.CopyTo(ms);
        var d = ms.ToArray();
        if (d.Length < 8 || d[0] != 0x89 || d[1] != 'P') throw new InvalidDataException("No es un PNG");
        int pos = 8, w = 0, h = 0, depth = 0, ctype = 0, interlace = 0;
        byte[] pal = null, trns = null;
        var idat = new MemoryStream();
        while (pos + 8 <= d.Length)
        {
            int len = BE(d, pos); string type = System.Text.Encoding.ASCII.GetString(d, pos + 4, 4);
            int p = pos + 8;
            switch (type)
            {
                case "IHDR": w = BE(d, p); h = BE(d, p + 4); depth = d[p + 8]; ctype = d[p + 9]; interlace = d[p + 12]; break;
                case "PLTE": pal = new byte[len]; Array.Copy(d, p, pal, 0, len); break;
                case "tRNS": trns = new byte[len]; Array.Copy(d, p, trns, 0, len); break;
                case "IDAT": idat.Write(d, p, len); break;
            }
            pos = p + len + 4;
            if (type == "IEND") break;
        }
        if (interlace != 0) throw new NotSupportedException("PNG entrelazado no soportado");
        int channels = ctype switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new NotSupportedException("Tipo PNG " + ctype) };
        int bitsPP = channels * depth;
        int bpp = Math.Max(1, bitsPP / 8);
        int stride = (w * bitsPP + 7) / 8;
        var raw = new byte[(stride + 1) * h];
        idat.Position = 0;
        using (var z = new ZLibStream(idat, CompressionMode.Decompress))
        {
            int read = 0, n;
            while (read < raw.Length && (n = z.Read(raw, read, raw.Length - read)) > 0) read += n;
        }
        var cur = new byte[stride]; var prev = new byte[stride];
        var outp = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            int f = raw[y * (stride + 1)];
            Buffer.BlockCopy(raw, y * (stride + 1) + 1, cur, 0, stride);
            Unfilter(f, cur, prev, bpp);
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                byte r, g, b, a = 255;
                if (ctype == 3 || (depth < 8))
                {
                    int bit = x * bitsPP; int v = cur[bit >> 3];
                    int sample = depth == 8 ? v : (v >> (8 - depth - (bit & 7))) & ((1 << depth) - 1);
                    if (ctype == 3)
                    {
                        r = pal[sample * 3]; g = pal[sample * 3 + 1]; b = pal[sample * 3 + 2];
                        if (trns != null && sample < trns.Length) a = trns[sample];
                    }
                    else { byte gr = (byte)(sample * 255 / ((1 << depth) - 1)); r = g = b = gr; }
                }
                else if (depth == 16)
                {
                    int i = x * bpp;
                    switch (ctype)
                    {
                        case 0: r = g = b = cur[i]; break;
                        case 2: r = cur[i]; g = cur[i + 2]; b = cur[i + 4]; break;
                        case 4: r = g = b = cur[i]; a = cur[i + 2]; break;
                        default: r = cur[i]; g = cur[i + 2]; b = cur[i + 4]; a = cur[i + 6]; break;
                    }
                }
                else
                {
                    int i = x * bpp;
                    switch (ctype)
                    {
                        case 0: r = g = b = cur[i]; if (trns != null && trns.Length >= 2 && cur[i] == trns[1]) a = 0; break;
                        case 2: r = cur[i]; g = cur[i + 1]; b = cur[i + 2];
                            if (trns != null && trns.Length >= 6 && r == trns[1] && g == trns[3] && b == trns[5]) a = 0; break;
                        case 4: r = g = b = cur[i]; a = cur[i + 1]; break;
                        default: r = cur[i]; g = cur[i + 1]; b = cur[i + 2]; a = cur[i + 3]; break;
                    }
                }
                outp[o] = r; outp[o + 1] = g; outp[o + 2] = b; outp[o + 3] = a;
            }
            (cur, prev) = (prev, cur);
        }
        return new Image { Width = w, Height = h, Rgba = outp };
    }

    static int BE(byte[] d, int p) => (d[p] << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3];

    static void Unfilter(int f, byte[] c, byte[] p, int bpp)
    {
        int n = c.Length;
        switch (f)
        {
            case 1: for (int i = bpp; i < n; i++) c[i] += c[i - bpp]; break;
            case 2: for (int i = 0; i < n; i++) c[i] += p[i]; break;
            case 3: for (int i = 0; i < n; i++) c[i] += (byte)(((i >= bpp ? c[i - bpp] : 0) + p[i]) >> 1); break;
            case 4:
                for (int i = 0; i < n; i++)
                {
                    int a = i >= bpp ? c[i - bpp] : 0, b = p[i], cc = i >= bpp ? p[i - bpp] : 0;
                    int pa = Math.Abs(b - cc), pb = Math.Abs(a - cc), pc = Math.Abs(a + b - 2 * cc);
                    c[i] += (byte)(pa <= pb && pa <= pc ? a : pb <= pc ? b : cc);
                }
                break;
        }
    }

    public static void Encode(Stream s, int w, int h, byte[] rgba)
    {
        void Chunk(string type, byte[] data)
        {
            var t = System.Text.Encoding.ASCII.GetBytes(type);
            WriteBE(s, data.Length); s.Write(t); s.Write(data);
            uint crc = Crc(t, 0xFFFFFFFF); crc = Crc(data, crc) ^ 0xFFFFFFFF; WriteBE(s, (int)crc);
        }
        s.Write(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10 });
        var hdr = new byte[13];
        hdr[0] = (byte)(w >> 24); hdr[1] = (byte)(w >> 16); hdr[2] = (byte)(w >> 8); hdr[3] = (byte)w;
        hdr[4] = (byte)(h >> 24); hdr[5] = (byte)(h >> 16); hdr[6] = (byte)(h >> 8); hdr[7] = (byte)h;
        hdr[8] = 8; hdr[9] = 6;
        Chunk("IHDR", hdr);
        var zms = new MemoryStream();
        using (var z = new ZLibStream(zms, CompressionLevel.Fastest, true))
            for (int y = 0; y < h; y++) { z.WriteByte(0); z.Write(rgba, y * w * 4, w * 4); }
        Chunk("IDAT", zms.ToArray());
        Chunk("IEND", Array.Empty<byte>());
    }

    static void WriteBE(Stream s, int v) { s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v); }

    static uint[] sCrcTable;
    static uint Crc(byte[] data, uint crc)
    {
        if (sCrcTable == null)
        {
            sCrcTable = new uint[256];
            for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1; sCrcTable[n] = c; }
        }
        foreach (var b in data) crc = sCrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }
}
