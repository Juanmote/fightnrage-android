using System;
using System.Runtime.InteropServices;
using SebaGames.Framework;

namespace FnrPort;

/// <summary>
/// Sustituye al "modo inmediato" de OpenGL de escritorio (glBegin/glEnd + matrices fijas) que usa
/// el backend de PC. Los vértices se transforman en CPU y se agrupan en lotes; el lote se vacía
/// cada vez que cambia algún estado de GL, así el resultado es el mismo que llamada a llamada.
/// </summary>
public static unsafe class Renderer
{
    // --- Estado global (equivalente a GLHelper del juego) ---
    public static uint BoundFbo { get; internal set; }
    static int sScX, sScY, sScW, sScH;           // scissor actual
    static BLEND_MODE sBlend = (BLEND_MODE)(-1);
    static uint sProgram = uint.MaxValue;
    static uint sTexture = uint.MaxValue;
    static int sViewW, sViewH;

    // Lote pendiente
    const int Floats = 8; // x y u v r g b a
    static float[] sVerts = new float[8 * 4096];
    static int sCount;            // vértices
    static int sMode = GL.TRIANGLES;
    static uint sVbo;
    static bool sInit;

    public static uint ProgTextured, ProgColor;
    public static uint WhiteTex;
    public static int DrawCalls;

    public const string VertexSrc =
        "attribute vec2 a_pos; attribute vec2 a_tc; attribute vec4 a_color;\n" +
        "varying vec4 fnr_TexCoord0; varying vec4 fnr_Color;\n" +
        "void main(){ gl_Position = vec4(a_pos, 0.0, 1.0); fnr_TexCoord0 = vec4(a_tc, 0.0, 1.0); fnr_Color = a_color; gl_PointSize = 1.0; }\n";

    public static void Initialize()
    {
        if (sInit) return;
        sInit = true;
        sVbo = GL.GenBuffer();
        ProgTextured = PortShader.BuildProgram(VertexSrc, ShaderConverter.FragmentHeader +
            "uniform sampler2D texture;\nvoid main(){ gl_FragColor = texture2D(texture, fnr_TexCoord0.xy) * fnr_Color; }\n", "default_tex");
        ProgColor = PortShader.BuildProgram(VertexSrc, ShaderConverter.FragmentHeader +
            "void main(){ gl_FragColor = fnr_Color; }\n", "default_color");
        WhiteTex = GL.GenTexture();
        GL.glBindTexture(GL.TEXTURE_2D, WhiteTex);
        uint white = 0xFFFFFFFF;
        GL.glTexImage2D(GL.TEXTURE_2D, 0, GL.RGBA, 1, 1, 0, GL.RGBA, GL.UNSIGNED_BYTE, &white);
        GL.glTexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MIN_FILTER, GL.NEAREST);
        GL.glTexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MAG_FILTER, GL.NEAREST);
        sTexture = uint.MaxValue;
        GL.glEnable(GL.SCISSOR_TEST);
    }

    /// <summary>Invalida la caché de estado (p. ej. tras recrear el contexto).</summary>
    public static void ResetCache()
    {
        sBlend = (BLEND_MODE)(-1); sProgram = uint.MaxValue; sTexture = uint.MaxValue;
    }

    // ------------------------------------------------------------------ estado

    public static void BindFramebuffer(uint fbo, int w, int h)
    {
        if (fbo == BoundFbo && w == sViewW && h == sViewH) return;
        Flush();
        GL.glBindFramebuffer(GL.FRAMEBUFFER, fbo);
        BoundFbo = fbo;
        GL.glViewport(0, 0, w, h);
        sViewW = w; sViewH = h;
    }

    /// <summary>Rebind sin cambiar viewport (lo que hace Clear() del original al terminar).</summary>
    public static void RawBindFramebuffer(uint fbo)
    {
        Flush();
        GL.glBindFramebuffer(GL.FRAMEBUFFER, fbo);
    }

    public static void Scissor(int x, int y, int w, int h)
    {
        if (x == sScX && y == sScY && w == sScW && h == sScH) return;
        Flush();
        GL.glScissor(x, y, Math.Max(0, w), Math.Max(0, h));
        sScX = x; sScY = y; sScW = w; sScH = h;
    }

    public static void SetBlend(BLEND_MODE mode)
    {
        if (mode == sBlend) return;
        Flush();
        sBlend = mode;
        if (mode == BLEND_MODE.NONE) { GL.glDisable(GL.BLEND); return; }
        GL.glEnable(GL.BLEND);
        switch (mode)
        {
            case BLEND_MODE.NORMAL:
                GL.glBlendEquation(GL.FUNC_ADD);
                GL.glBlendFuncSeparate(GL.SRC_ALPHA, GL.ONE_MINUS_SRC_ALPHA, GL.SRC_ALPHA, GL.ONE); break;
            case BLEND_MODE.ADD:
                GL.glBlendEquation(GL.FUNC_ADD); GL.glBlendFunc(GL.SRC_ALPHA, GL.ONE); break;
            case BLEND_MODE.MAX:
                GL.glBlendEquation(GL.MAX);
                GL.glBlendFuncSeparate(GL.SRC_ALPHA, GL.ONE_MINUS_SRC_ALPHA, GL.SRC_ALPHA, GL.ONE); break;
            case BLEND_MODE.MULTIPLY:
                GL.glBlendEquation(GL.FUNC_ADD); GL.glBlendFunc(GL.DST_COLOR, GL.ONE_MINUS_SRC_ALPHA); break;
            case BLEND_MODE.COLOR_ADD:
                GL.glBlendEquation(GL.FUNC_ADD); GL.glBlendFunc(GL.DST_COLOR, GL.ONE); break;
            case BLEND_MODE.INVERT:
                GL.glBlendEquation(GL.FUNC_ADD); GL.glBlendFunc(GL.ONE_MINUS_DST_COLOR, GL.ONE_MINUS_SRC_COLOR); break;
            case BLEND_MODE.MASK:
                GL.glBlendEquation(GL.FUNC_ADD);
                GL.glBlendFuncSeparate(GL.SRC_ALPHA, GL.ONE_MINUS_SRC_ALPHA, GL.ONE_MINUS_SRC_ALPHA, GL.DST_ALPHA); break;
            case BLEND_MODE.MASK_BELOW:
                GL.glBlendEquation(GL.FUNC_ADD);
                GL.glBlendFuncSeparate(GL.SRC_ALPHA, GL.ONE_MINUS_SRC_ALPHA, GL.ONE_MINUS_DST_ALPHA, GL.ONE_MINUS_SRC_ALPHA); break;
        }
    }

    public static void UseProgram(uint program)
    {
        if (program == sProgram) return;
        Flush();
        GL.glUseProgram(program);
        sProgram = program;
    }

    public static uint CurrentProgram => sProgram;

    public static void BindTexture(PortTexture tex, bool smoothing, TextureAddressingMode wrap)
    {
        uint id = tex?.Id ?? WhiteTex;
        if (id != sTexture)
        {
            Flush();
            GL.glActiveTexture(GL.TEXTURE0);
            GL.glBindTexture(GL.TEXTURE_2D, id);
            sTexture = id;
        }
        if (tex == null) return;
        if (tex.Smoothing != smoothing)
        {
            Flush();
            int f = smoothing ? GL.LINEAR : GL.NEAREST;
            GL.glTexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MIN_FILTER, f);
            GL.glTexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MAG_FILTER, f);
            tex.Smoothing = smoothing;
        }
        if (tex.Wrap != wrap)
        {
            Flush();
            int m = wrap switch { TextureAddressingMode.WRAP => GL.REPEAT, TextureAddressingMode.MIRROR => GL.MIRRORED_REPEAT, _ => GL.CLAMP_TO_EDGE };
            GL.glTexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_S, m);
            GL.glTexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_T, m);
            tex.Wrap = wrap;
        }
    }

    /// <summary>Se llama cuando se borra una textura para no dejar el id cacheado.</summary>
    internal static void ForgetTextureBinding() => sTexture = uint.MaxValue;

    internal static void ForgetTexture(uint id) { if (sTexture == id) { Flush(); sTexture = uint.MaxValue; } }

    // ------------------------------------------------------------------ vértices

    static void SetMode(int mode)
    {
        if (mode != sMode) { Flush(); sMode = mode; }
    }

    static void Ensure(int extra)
    {
        int need = (sCount + extra) * Floats;
        if (need > sVerts.Length) Array.Resize(ref sVerts, Math.Max(need, sVerts.Length * 2));
    }

    public static void Vertex(float x, float y, float u, float v, float r, float g, float b, float a)
    {
        int i = sCount * Floats;
        var d = sVerts;
        d[i] = x; d[i + 1] = y; d[i + 2] = u; d[i + 3] = v; d[i + 4] = r; d[i + 5] = g; d[i + 6] = b; d[i + 7] = a;
        sCount++;
    }

    /// <summary>Añade una primitiva convertida al modo de GLES equivalente.</summary>
    public static void AddPrimitive(SebaGames.Framework.PrimitiveType type, float[] xy, float[] uv, float[] rgba, int n)
    {
        switch (type)
        {
            case SebaGames.Framework.PrimitiveType.POINTS:
                SetMode(GL.POINTS); Ensure(n);
                for (int i = 0; i < n; i++) V(xy, uv, rgba, i);
                break;
            case SebaGames.Framework.PrimitiveType.LINES:
                SetMode(GL.LINES); Ensure(n);
                for (int i = 0; i + 1 < n; i += 2) { V(xy, uv, rgba, i); V(xy, uv, rgba, i + 1); }
                break;
            case SebaGames.Framework.PrimitiveType.LINE_STRIP:
            case SebaGames.Framework.PrimitiveType.LINE_LOOP:
                SetMode(GL.LINES); Ensure(n * 2);
                for (int i = 0; i + 1 < n; i++) { V(xy, uv, rgba, i); V(xy, uv, rgba, i + 1); }
                if (type == SebaGames.Framework.PrimitiveType.LINE_LOOP && n > 2) { V(xy, uv, rgba, n - 1); V(xy, uv, rgba, 0); }
                break;
            case SebaGames.Framework.PrimitiveType.TRIANGLES:
                SetMode(GL.TRIANGLES); Ensure(n);
                for (int i = 0; i + 2 < n; i += 3) { V(xy, uv, rgba, i); V(xy, uv, rgba, i + 1); V(xy, uv, rgba, i + 2); }
                break;
            case SebaGames.Framework.PrimitiveType.TRIANGLE_STRIP:
            case SebaGames.Framework.PrimitiveType.QUAD_STRIP:
                SetMode(GL.TRIANGLES); Ensure(n * 3);
                for (int i = 0; i + 2 < n; i++)
                {
                    if ((i & 1) == 0) { V(xy, uv, rgba, i); V(xy, uv, rgba, i + 1); V(xy, uv, rgba, i + 2); }
                    else { V(xy, uv, rgba, i + 1); V(xy, uv, rgba, i); V(xy, uv, rgba, i + 2); }
                }
                break;
            case SebaGames.Framework.PrimitiveType.TRIANGLE_FAN:
            case SebaGames.Framework.PrimitiveType.POLYGON:
                SetMode(GL.TRIANGLES); Ensure(n * 3);
                for (int i = 1; i + 1 < n; i++) { V(xy, uv, rgba, 0); V(xy, uv, rgba, i); V(xy, uv, rgba, i + 1); }
                break;
            case SebaGames.Framework.PrimitiveType.QUADS:
            default:
                SetMode(GL.TRIANGLES); Ensure(n / 4 * 6 + 6);
                for (int i = 0; i + 3 < n; i += 4)
                {
                    V(xy, uv, rgba, i); V(xy, uv, rgba, i + 1); V(xy, uv, rgba, i + 2);
                    V(xy, uv, rgba, i); V(xy, uv, rgba, i + 2); V(xy, uv, rgba, i + 3);
                }
                break;
        }
    }

    static void V(float[] xy, float[] uv, float[] c, int i) =>
        Vertex(xy[i * 2], xy[i * 2 + 1], uv[i * 2], uv[i * 2 + 1], c[i * 4], c[i * 4 + 1], c[i * 4 + 2], c[i * 4 + 3]);

    /// <summary>Quad rápido (Draw de sprites).</summary>
    public static void Quad(float x0, float y0, float x1, float y1, float x2, float y2, float x3, float y3,
        float s0, float t0, float s1, float t1, float r, float g, float b, float a)
    {
        SetMode(GL.TRIANGLES); Ensure(6);
        Vertex(x0, y0, s0, t0, r, g, b, a); Vertex(x1, y1, s1, t0, r, g, b, a); Vertex(x2, y2, s1, t1, r, g, b, a);
        Vertex(x0, y0, s0, t0, r, g, b, a); Vertex(x2, y2, s1, t1, r, g, b, a); Vertex(x3, y3, s0, t1, r, g, b, a);
    }

    public static void Flush()
    {
        if (sCount == 0) return;
        GL.glBindBuffer(GL.ARRAY_BUFFER, sVbo);
        fixed (float* p = sVerts)
            GL.glBufferData(GL.ARRAY_BUFFER, (IntPtr)(sCount * Floats * 4), p, GL.STREAM_DRAW);
        int stride = Floats * 4;
        GL.glEnableVertexAttribArray(0); GL.glVertexAttribPointer(0, 2, GL.FLOAT, 0, stride, IntPtr.Zero);
        GL.glEnableVertexAttribArray(1); GL.glVertexAttribPointer(1, 2, GL.FLOAT, 0, stride, (IntPtr)8);
        GL.glEnableVertexAttribArray(2); GL.glVertexAttribPointer(2, 4, GL.FLOAT, 0, stride, (IntPtr)16);
        GL.glDrawArrays(sMode, 0, sCount);
        DrawCalls++;
        sCount = 0;
    }
}
