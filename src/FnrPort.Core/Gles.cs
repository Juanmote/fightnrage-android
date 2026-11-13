using System;
using System.Runtime.InteropServices;
using System.Text;

namespace FnrPort;

/// <summary>Enlaces mínimos a OpenGL ES 2.0 (libGLESv2). Sirven igual en Android y en Linux.</summary>
public static unsafe class GL
{
    const string Lib = "GLESv2";

    public const int TEXTURE_2D = 0x0DE1, TEXTURE0 = 0x84C0, TEXTURE1 = 0x84C1;
    public const int RGBA = 0x1908, UNSIGNED_BYTE = 0x1401, FLOAT = 0x1406;
    public const int TEXTURE_MIN_FILTER = 0x2801, TEXTURE_MAG_FILTER = 0x2800, TEXTURE_WRAP_S = 0x2802, TEXTURE_WRAP_T = 0x2803;
    public const int NEAREST = 0x2600, LINEAR = 0x2601, REPEAT = 0x2901, MIRRORED_REPEAT = 0x8370, CLAMP_TO_EDGE = 0x812F;
    public const int FRAMEBUFFER = 0x8D40, COLOR_ATTACHMENT0 = 0x8CE0, FRAMEBUFFER_COMPLETE = 0x8CD5;
    public const int SCISSOR_TEST = 0x0C11, BLEND = 0x0BE2, COLOR_BUFFER_BIT = 0x4000;
    public const int FUNC_ADD = 0x8006, MAX = 0x8008;
    public const int ZERO = 0, ONE = 1, SRC_COLOR = 0x0300, ONE_MINUS_SRC_COLOR = 0x0301, SRC_ALPHA = 0x0302, ONE_MINUS_SRC_ALPHA = 0x0303,
        DST_ALPHA = 0x0304, ONE_MINUS_DST_ALPHA = 0x0305, DST_COLOR = 0x0306, ONE_MINUS_DST_COLOR = 0x0307;
    public const int POINTS = 0, LINES = 1, LINE_LOOP = 2, LINE_STRIP = 3, TRIANGLES = 4, TRIANGLE_STRIP = 5, TRIANGLE_FAN = 6;
    public const int VERTEX_SHADER = 0x8B31, FRAGMENT_SHADER = 0x8B30, COMPILE_STATUS = 0x8B81, LINK_STATUS = 0x8B82, INFO_LOG_LENGTH = 0x8B84;
    public const int ACTIVE_UNIFORMS = 0x8B86, SAMPLER_2D = 0x8B5E;
    public const int ARRAY_BUFFER = 0x8892, STREAM_DRAW = 0x88E0;
    public const int VENDOR = 0x1F00, RENDERER = 0x1F01, VERSION = 0x1F02, SHADING_LANGUAGE_VERSION = 0x8B8C;
    public const int FRAMEBUFFER_BINDING = 0x8CA6;

    [DllImport(Lib)] public static extern void glActiveTexture(int texture);
    [DllImport(Lib)] public static extern void glAttachShader(uint program, uint shader);
    [DllImport(Lib)] public static extern void glBindAttribLocation(uint program, uint index, string name);
    [DllImport(Lib)] public static extern void glBindBuffer(int target, uint buffer);
    [DllImport(Lib)] public static extern void glBindFramebuffer(int target, uint framebuffer);
    [DllImport(Lib)] public static extern void glBindTexture(int target, uint texture);
    [DllImport(Lib)] public static extern void glBlendEquation(int mode);
    [DllImport(Lib)] public static extern void glBlendFunc(int s, int d);
    [DllImport(Lib)] public static extern void glBlendFuncSeparate(int sRGB, int dRGB, int sA, int dA);
    [DllImport(Lib)] public static extern void glBufferData(int target, IntPtr size, void* data, int usage);
    [DllImport(Lib)] public static extern int glCheckFramebufferStatus(int target);
    [DllImport(Lib)] public static extern void glClear(int mask);
    [DllImport(Lib)] public static extern void glClearColor(float r, float g, float b, float a);
    [DllImport(Lib)] public static extern void glCompileShader(uint shader);
    [DllImport(Lib)] public static extern uint glCreateProgram();
    [DllImport(Lib)] public static extern uint glCreateShader(int type);
    [DllImport(Lib)] public static extern void glDeleteFramebuffers(int n, uint* fbs);
    [DllImport(Lib)] public static extern void glDeleteProgram(uint program);
    [DllImport(Lib)] public static extern void glDeleteShader(uint shader);
    [DllImport(Lib)] public static extern void glDeleteTextures(int n, uint* tex);
    [DllImport(Lib)] public static extern void glDisable(int cap);
    [DllImport(Lib)] public static extern void glDrawArrays(int mode, int first, int count);
    [DllImport(Lib)] public static extern void glEnable(int cap);
    [DllImport(Lib)] public static extern void glEnableVertexAttribArray(uint index);
    [DllImport(Lib)] public static extern void glFinish();
    [DllImport(Lib)] public static extern void glFlush();
    [DllImport(Lib)] public static extern void glFramebufferTexture2D(int target, int attachment, int textarget, uint texture, int level);
    [DllImport(Lib)] public static extern void glGenBuffers(int n, uint* buffers);
    [DllImport(Lib)] public static extern void glGenFramebuffers(int n, uint* fbs);
    [DllImport(Lib)] public static extern void glGenTextures(int n, uint* tex);
    [DllImport(Lib)] public static extern void glGetActiveUniform(uint program, uint index, int bufSize, int* length, int* size, int* type, byte* name);
    [DllImport(Lib)] public static extern int glGetError();
    [DllImport(Lib)] public static extern void glGetIntegerv(int pname, int* data);
    [DllImport(Lib)] public static extern void glGetProgramiv(uint program, int pname, int* param);
    [DllImport(Lib)] public static extern void glGetProgramInfoLog(uint program, int bufSize, int* length, byte* log);
    [DllImport(Lib)] public static extern void glGetShaderiv(uint shader, int pname, int* param);
    [DllImport(Lib)] public static extern void glGetShaderInfoLog(uint shader, int bufSize, int* length, byte* log);
    [DllImport(Lib)] public static extern IntPtr glGetString(int name);
    [DllImport(Lib)] public static extern int glGetUniformLocation(uint program, string name);
    [DllImport(Lib)] public static extern void glLinkProgram(uint program);
    [DllImport(Lib)] public static extern void glReadPixels(int x, int y, int w, int h, int format, int type, void* data);
    [DllImport(Lib)] public static extern void glScissor(int x, int y, int w, int h);
    [DllImport(Lib)] public static extern void glShaderSource(uint shader, int count, IntPtr* strings, int* lengths);
    [DllImport(Lib)] public static extern void glTexImage2D(int target, int level, int internalformat, int w, int h, int border, int format, int type, void* pixels);
    [DllImport(Lib)] public static extern void glTexParameteri(int target, int pname, int param);
    [DllImport(Lib)] public static extern void glUniform1f(int loc, float x);
    [DllImport(Lib)] public static extern void glUniform2f(int loc, float x, float y);
    [DllImport(Lib)] public static extern void glUniform3f(int loc, float x, float y, float z);
    [DllImport(Lib)] public static extern void glUniform4f(int loc, float x, float y, float z, float w);
    [DllImport(Lib)] public static extern void glUniform1i(int loc, int x);
    [DllImport(Lib)] public static extern void glUseProgram(uint program);
    [DllImport(Lib)] public static extern void glVertexAttribPointer(uint index, int size, int type, byte normalized, int stride, IntPtr pointer);
    [DllImport(Lib)] public static extern void glViewport(int x, int y, int w, int h);

    public static string GetString(int name)
    {
        var p = glGetString(name);
        return p == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(p) ?? "";
    }

    public static uint GenTexture() { uint t; glGenTextures(1, &t); return t; }
    public static uint GenFramebuffer() { uint t; glGenFramebuffers(1, &t); return t; }
    public static uint GenBuffer() { uint t; glGenBuffers(1, &t); return t; }
    public static void DeleteTexture(uint t) => glDeleteTextures(1, &t);
    public static void DeleteFramebuffer(uint t) => glDeleteFramebuffers(1, &t);

    public static void ShaderSource(uint shader, string src)
    {
        IntPtr p = Marshal.StringToHGlobalAnsi(src);
        try { int len = Encoding.ASCII.GetByteCount(src); glShaderSource(shader, 1, &p, &len); }
        finally { Marshal.FreeHGlobal(p); }
    }

    public static string ShaderInfoLog(uint shader)
    {
        int len; glGetShaderiv(shader, INFO_LOG_LENGTH, &len);
        if (len <= 1) return "";
        var buf = new byte[len]; fixed (byte* b = buf) glGetShaderInfoLog(shader, len, &len, b);
        return Encoding.ASCII.GetString(buf, 0, Math.Max(0, len));
    }

    public static string ProgramInfoLog(uint program)
    {
        int len; glGetProgramiv(program, INFO_LOG_LENGTH, &len);
        if (len <= 1) return "";
        var buf = new byte[len]; fixed (byte* b = buf) glGetProgramInfoLog(program, len, &len, b);
        return Encoding.ASCII.GetString(buf, 0, Math.Max(0, len));
    }
}
