using System;
using System.IO;
using SebaGames.Core;
using SebaGames.Framework;

namespace FnrPort;

/// <summary>IBitmap sobre una textura GLES. Guarda copia en CPU (RGBA) como el original (GetPixel).</summary>
public unsafe class PortTexture : IBitmap
{
    public uint Id { get; private set; }
    public bool Smoothing;
    public TextureAddressingMode Wrap = TextureAddressingMode.CLAMP;
    internal uint FrameBufferId; // 0 = no es superficie
    readonly int mW, mH;
    byte[] mPixels; // RGBA, fila 0 = arriba (como System.Drawing)

    public int Width => mW;
    public int Height => mH;
    public Rectangle FullRect => new Rectangle(0, 0, mW, mH);

    PortTexture(int w, int h) { mW = w; mH = h; }

    public static PortTexture FromRgba(int w, int h, byte[] rgba, bool keepCopy = true)
    {
        var t = new PortTexture(w, h) { mPixels = keepCopy ? rgba : null };
        Renderer.Flush();
        t.Id = GL.GenTexture();
        GL.glActiveTexture(GL.TEXTURE0);
        GL.glBindTexture(GL.TEXTURE_2D, t.Id);
        fixed (byte* p = rgba)
            GL.glTexImage2D(GL.TEXTURE_2D, 0, GL.RGBA, w, h, 0, GL.RGBA, GL.UNSIGNED_BYTE, rgba == null ? null : p);
        GL.glTexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_S, GL.CLAMP_TO_EDGE);
        GL.glTexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_T, GL.CLAMP_TO_EDGE);
        GL.glTexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MIN_FILTER, GL.NEAREST);
        GL.glTexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MAG_FILTER, GL.NEAREST);
        Renderer.ForgetTextureBinding();
        return t;
    }

    public static PortTexture FromStream(Stream s)
    {
        var img = PngCodec.Decode(s);
        return FromRgba(img.Width, img.Height, img.Rgba);
    }

    public Color GetPixel(Coord2D aPos)
    {
        if (mPixels == null) return new Color(0, 0, 0, 0);
        int i = (aPos.Y * mW + aPos.X) * 4;
        return new Color(mPixels[i], mPixels[i + 1], mPixels[i + 2], mPixels[i + 3]);
    }

    public void SetPixel(Coord2D aPos, Color aColor)
    {
        // Igual que en PC: solo cambia la copia en memoria, no la textura.
        if (mPixels == null) return;
        int i = (aPos.Y * mW + aPos.X) * 4;
        mPixels[i] = aColor.R; mPixels[i + 1] = aColor.G; mPixels[i + 2] = aColor.B; mPixels[i + 3] = aColor.A;
    }

    public void SetPixels(byte[] aPixels) { }
    public void SetPixels(byte[] aPixels, Rectangle aZone) { }

    /// <summary>Como el original: solo vale para superficies. Devuelve BGRA, fila 0 = arriba (y del juego).</summary>
    public byte[] GetPixels()
    {
        if (FrameBufferId == 0) throw new NotImplementedException("Por ahora solo funciona con las superficies.");
        Renderer.Flush();
        var rgba = new byte[mW * mH * 4];
        GL.glBindFramebuffer(GL.FRAMEBUFFER, FrameBufferId);
        fixed (byte* p = rgba) GL.glReadPixels(0, 0, mW, mH, GL.RGBA, GL.UNSIGNED_BYTE, p);
        GL.glBindFramebuffer(GL.FRAMEBUFFER, Renderer.BoundFbo);
        for (int i = 0; i < rgba.Length; i += 4) { byte r = rgba[i]; rgba[i] = rgba[i + 2]; rgba[i + 2] = r; }
        return rgba;
    }

    public void Save(string aFileName)
    {
        var bgra = GetPixels();
        for (int i = 0; i < bgra.Length; i += 4) { byte b = bgra[i]; bgra[i] = bgra[i + 2]; bgra[i + 2] = b; }
        var dir = Path.GetDirectoryName(Path.GetFullPath(aFileName));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using var fs = File.Create(aFileName);
        PngCodec.Encode(fs, mW, mH, bgra);
    }

    public IBitmap GetBaseBitmap() => this;

    public void Dispose()
    {
        if (Id != 0)
        {
            Renderer.ForgetTexture(Id);
            GL.DeleteTexture(Id);
            Id = 0;
            mPixels = null;
        }
    }
}
