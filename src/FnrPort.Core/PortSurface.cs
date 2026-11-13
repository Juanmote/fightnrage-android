using System;
using SebaGames.Core;
using SebaGames.Framework;

namespace FnrPort;

/// <summary>IDrawableSurface: réplica de OTKFrameBuffer (FBO o pantalla) sobre GLES.</summary>
public class PortSurface : IDrawableSurface
{
    uint mFbo;
    PortTexture mTexture;
    int mWidth, mHeight;
    bool mIsBackBuffer;
    Rectangle mFullRect, mClipRect;
    internal bool mClipRectMustChange;
    // Matriz al estilo OpenTK (fila-mayor, vector fila): M[r,c] = M(r+1)(c+1)
    readonly double[] M = Identity();
    Color mColor;
    bool mColorSet;
    BLEND_MODE mBlendMode;
    bool mSmoothing;
    IShader mShader;

    public uint Fbo => mFbo;
    public bool IsBackBuffer => mIsBackBuffer;
    public int Width => mWidth;
    public int Height => mHeight;
    public Color Color { get => mColorSet ? mColor : White(); set { mColor = value; mColorSet = true; } }
    public BLEND_MODE BlendMode { get => mBlendMode; set => mBlendMode = value; }
    public bool Smoothing { get => mSmoothing; set => mSmoothing = value; }
    public IShader Shader { get => mShader; set => mShader = value; }
    public Rectangle FullRect => mFullRect;
    public Rectangle ClipRect { get => mClipRect; set { mClipRect = value; mClipRectMustChange = true; } }
    public int LastDrawCallsCount => 0;

    static Color White() => new Color(255, 255, 255, 255);

    public static PortSurface CreateNew(int w, int h)
    {
        var tex = PortTexture.FromRgba(w, h, new byte[w * h * 4], keepCopy: false);
        Renderer.Flush();
        uint fbo = GL.GenFramebuffer();
        GL.glBindFramebuffer(GL.FRAMEBUFFER, fbo);
        GL.glFramebufferTexture2D(GL.FRAMEBUFFER, GL.COLOR_ATTACHMENT0, GL.TEXTURE_2D, tex.Id, 0);
        int st = GL.glCheckFramebufferStatus(GL.FRAMEBUFFER);
        if (st != GL.FRAMEBUFFER_COMPLETE) Console.WriteLine($"[FnrPort] FBO {w}x{h} incompleto: 0x{st:X}");
        GL.glBindFramebuffer(GL.FRAMEBUFFER, Renderer.BoundFbo);
        tex.FrameBufferId = fbo;
        var s = new PortSurface { mWidth = w, mHeight = h, mFullRect = new Rectangle(0, 0, w, h), mFbo = fbo, mTexture = tex };
        s.mClipRect = s.mFullRect;
        return s;
    }

    public static PortSurface CreateBackBuffer(int w, int h)
    {
        var s = new PortSurface { mFbo = 0, mWidth = w, mHeight = h, mFullRect = new Rectangle(0, 0, w, h), mIsBackBuffer = true };
        s.mClipRect = s.mFullRect;
        return s;
    }

    internal void SetSize(int w, int h) { mWidth = w; mHeight = h; }

    public void Clear(Color aColor)
    {
        var c = aColor.ToColorF();
        Renderer.RawBindFramebuffer(mFbo);
        mClipRectMustChange = true;
        RefreshClipRect();
        GL.glClearColor(c.R, c.G, c.B, c.A);
        GL.glClear(GL.COLOR_BUFFER_BIT);
        GL.glBindFramebuffer(GL.FRAMEBUFFER, Renderer.BoundFbo);
    }

    // ------------------------------------------------------------- matrices

    static double[] Identity() => new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };

    static double[] Mul(double[] a, double[] b)
    {
        var r = new double[16];
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 4; j++)
            {
                double s = 0;
                for (int k = 0; k < 4; k++) s += a[i * 4 + k] * b[k * 4 + j];
                r[i * 4 + j] = s;
            }
        return r;
    }

    public void ResetMatrix() { var id = Identity(); Array.Copy(id, M, 16); }

    public Matrix4d GetMatrix() => new Matrix4d
    {
        M00 = M[0], M01 = M[1], M02 = M[2], M03 = M[3],
        M10 = M[4], M11 = M[5], M12 = M[6], M13 = M[7],
        M20 = M[8], M21 = M[9], M22 = M[10], M23 = M[11],
        M30 = M[12], M31 = M[13], M32 = M[14], M33 = M[15],
    };

    public void SetMatrix(Matrix4d m)
    {
        M[0] = m.M00; M[1] = m.M01; M[2] = m.M02; M[3] = m.M03;
        M[4] = m.M10; M[5] = m.M11; M[6] = m.M12; M[7] = m.M13;
        M[8] = m.M20; M[9] = m.M21; M[10] = m.M22; M[11] = m.M23;
        M[12] = m.M30; M[13] = m.M31; M[14] = m.M32; M[15] = m.M33;
    }

    public void ApplyMatrixTransformation(Transformation t)
    {
        double c = Math.Cos(t.Rotation), s = Math.Sin(t.Rotation);
        var rot = new double[] { c, s, 0, 0, -s, c, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };
        var scl = new double[] { t.Scale.X, 0, 0, 0, 0, t.Scale.Y, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 };
        var tr = new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, t.X, t.Y, 0, 1 };
        var r = Mul(Mul(Mul(Identity(), rot), scl), tr);
        var res = Mul(r, M);
        Array.Copy(res, M, 16);
    }

    // ------------------------------------------------------------- dibujo

    void RefreshClipRect()
    {
        if (mClipRectMustChange)
        {
            Renderer.Scissor(mClipRect.X, mClipRect.Y, mClipRect.Width, mClipRect.Height);
            mClipRectMustChange = false;
        }
    }

    void Prepare(PortTexture tex, bool smoothing, TextureAddressingMode wrap)
    {
        if (Renderer.BoundFbo != mFbo || mIsBackBuffer)
        {
            bool changed = Renderer.BoundFbo != mFbo;
            Renderer.BindFramebuffer(mFbo, mWidth, mHeight);
            if (changed)
            {
                if (mIsBackBuffer) Renderer.Scissor(0, 0, mWidth, mHeight);
                else { Renderer.Scissor(mClipRect.X, mClipRect.Y, mClipRect.Width, mClipRect.Height); mClipRectMustChange = false; }
            }
        }
        Renderer.SetBlend(mBlendMode);
        var baseShader = mShader?.GetBaseShader() as PortShader;
        if (baseShader != null) Renderer.UseProgram(baseShader.ProgramId);
        else Renderer.UseProgram(tex != null ? Renderer.ProgTextured : Renderer.ProgColor);
        Renderer.BindTexture(tex, smoothing, wrap);
    }

    // Matriz afín 2D final (incluye la proyección ortográfica): X = a*x + c*y + e ; Y = b*x + d*y + f
    void FinalAffine(out float a, out float b, out float c, out float d, out float e, out float f)
    {
        double sx = 2.0 / mWidth, sy = mIsBackBuffer ? -2.0 / mHeight : 2.0 / mHeight, oy = mIsBackBuffer ? 1.0 : -1.0;
        a = (float)(M[0] * sx); c = (float)(M[4] * sx); e = (float)(M[12] * sx - 1.0);
        b = (float)(M[1] * sy); d = (float)(M[5] * sy); f = (float)(M[13] * sy + oy);
    }

    public void Draw(IBitmap aBitmap, Rectangle src, Transformation t, Vector2D axis)
    {
        RefreshClipRect();
        PortTexture tex = null;
        float s0 = 0, s1 = 0, t0 = 0, t1 = 0;
        if (aBitmap != null)
        {
            tex = aBitmap.GetBaseBitmap() as PortTexture;
            float bw = aBitmap.Width, bh = aBitmap.Height;
            s0 = src.Left / bw; s1 = src.Right / bw; t0 = src.Top / bh; t1 = src.Bottom / bh;
        }
        Prepare(tex, mSmoothing, TextureAddressingMode.CLAMP);

        FinalAffine(out float a, out float b, out float c, out float d, out float e, out float f);
        float cos = MathF.Cos(t.Rotation), sin = MathF.Sin(t.Rotation);
        float sx = t.Scale.X, sy = t.Scale.Y, ax = axis.X, ay = axis.Y, px = t.X, py = t.Y;
        float w = src.Width, h = src.Height;

        // esquinas locales -> transformación del sprite -> matriz de la superficie + proyección
        Span<float> lx = stackalloc float[4] { 0, w, w, 0 };
        Span<float> ly = stackalloc float[4] { 0, 0, h, h };
        Span<float> ox = stackalloc float[4];
        Span<float> oy = stackalloc float[4];
        for (int i = 0; i < 4; i++)
        {
            float qx = (lx[i] - ax) * sx, qy = (ly[i] - ay) * sy;
            float X = qx * cos - qy * sin + px, Y = qx * sin + qy * cos + py;
            ox[i] = a * X + c * Y + e; oy[i] = b * X + d * Y + f;
        }
        var col = Color.ToColorF();
        Renderer.Quad(ox[0], oy[0], ox[1], oy[1], ox[2], oy[2], ox[3], oy[3], s0, t0, s1, t1, col.R, col.G, col.B, col.A);
    }

    float[] mXY = new float[64], mUV = new float[64], mRGBA = new float[128];

    public void DrawVertexs(IBitmap aBitmap, SebaGames.Framework.PrimitiveType type, TextureAddressingMode wrap, params Vertex2D[] v)
        => DrawVertexs(aBitmap, type, wrap, v.Length, v);

    public void DrawVertexs(IBitmap aBitmap, SebaGames.Framework.PrimitiveType type, TextureAddressingMode wrap, int max, params Vertex2D[] v)
    {
        RefreshClipRect();
        var tex = aBitmap?.GetBaseBitmap() as PortTexture;
        Prepare(tex, mSmoothing, wrap);
        FinalAffine(out float a, out float b, out float c, out float d, out float e, out float f);
        if (mXY.Length < max * 2) { mXY = new float[max * 2]; mUV = new float[max * 2]; mRGBA = new float[max * 4]; }
        for (int i = 0; i < max; i++)
        {
            var vx = v[i];
            float X = vx.Position.X, Y = vx.Position.Y;
            mXY[i * 2] = a * X + c * Y + e; mXY[i * 2 + 1] = b * X + d * Y + f;
            mUV[i * 2] = vx.TextCoord.X; mUV[i * 2 + 1] = vx.TextCoord.Y;
            mRGBA[i * 4] = vx.Color.R; mRGBA[i * 4 + 1] = vx.Color.G; mRGBA[i * 4 + 2] = vx.Color.B; mRGBA[i * 4 + 3] = vx.Color.A;
        }
        Renderer.AddPrimitive(type, mXY, mUV, mRGBA, max);
    }

    public IBitmap GetBitmap() => mTexture;

    public void Renderize()
    {
        mShader = null;
        mColorSet = false;
        Renderer.Flush();
    }

    public IDrawableSurface GetBaseSurface() => this;

    public void Dispose()
    {
        mTexture?.Dispose();
        if (mFbo != 0) { Renderer.Flush(); GL.DeleteFramebuffer(mFbo); mFbo = 0; }
    }
}
