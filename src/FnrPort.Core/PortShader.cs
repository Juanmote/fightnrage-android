using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using SebaGames.Framework;

namespace FnrPort;

/// <summary>Convierte los shaders de fragmento de PC (GLSL 1.10 + pipeline fijo) a GLSL ES 1.00.</summary>
public static class ShaderConverter
{
    public const string FragmentHeader =
        "#version 100\n" +
        "#ifdef GL_FRAGMENT_PRECISION_HIGH\nprecision highp float;\n#else\nprecision mediump float;\n#endif\n" +
        "varying vec4 fnr_TexCoord0; varying vec4 fnr_Color;\n";

    public sealed class UniformDefault { public string Type, Name; public float[] Values; }

    /// <summary>Convierte el shader. Los "uniform x = valor;" (no válidos en ES) se separan en <paramref name="defaults"/>.</summary>
    public static string ConvertFragment(string src, List<UniformDefault> defaults, bool fixIntLiterals = false)
    {
        src = src.Replace("\r", "");
        src = Regex.Replace(src, @"^\s*#version[^\n]*\n", "", RegexOptions.Multiline);
        src = src.Replace("gl_TexCoord[0]", "fnr_TexCoord0").Replace("gl_Color", "fnr_Color");
        src = Regex.Replace(src, @"uniform\s+(\w+)\s+(\w+)\s*=\s*([^;]+);", m =>
        {
            var vals = new List<float>();
            string expr = m.Groups[3].Value.Trim();
            if (expr == "true") vals.Add(1); else if (expr == "false") vals.Add(0);
            else foreach (Match n in Regex.Matches(expr, @"[-+]?(\d+\.?\d*|\.\d+)([eE][-+]?\d+)?"))
                vals.Add(float.Parse(n.Value, System.Globalization.CultureInfo.InvariantCulture));
            defaults?.Add(new UniformDefault { Type = m.Groups[1].Value, Name = m.Groups[2].Value, Values = vals.ToArray() });
            return $"uniform {m.Groups[1].Value} {m.Groups[2].Value};";
        });
        if (fixIntLiterals) src = FixIntLiterals(src);
        return FragmentHeader + "#line 1\n" + src;
    }

    // Último recurso si un shader no compila: "2" -> "2.0" en líneas sin enteros.
    static string FixIntLiterals(string src)
    {
        return Regex.Replace(src, @"(?<![\w.\[])(\d+)(?![\w.\]])", m =>
        {
            int lineStart = src.LastIndexOf('\n', Math.Max(0, m.Index - 1)) + 1;
            if (src.Substring(lineStart, m.Index - lineStart).TrimStart().StartsWith("#")) return m.Value;
            int lineEnd = src.IndexOf('\n', m.Index); if (lineEnd < 0) lineEnd = src.Length;
            string line = src.Substring(lineStart, lineEnd - lineStart);
            if (Regex.IsMatch(line, @"\bint\b|\bivec\d\b|\bbool\b")) return m.Value;
            return m.Value + ".0";
        });
    }
}

/// <summary>IShader sobre un programa GLES (réplica de OTKShader).</summary>
public class PortShader : IShader
{
    struct Uniform { public int Location; public int Type; }

    public uint ProgramId { get; private set; }
    readonly Dictionary<string, Uniform> mUniforms = new();
    readonly string mName;

    public PortShader(string fragmentCode, string name)
    {
        mName = name;
        string vertex = Renderer.VertexSrc;
        int vs = fragmentCode.IndexOf("//CMD:VERTEX_START", StringComparison.Ordinal);
        if (vs >= 0)
            Console.WriteLine($"[FnrPort] Shader {name}: trae vertex shader propio; se ignora (no soportado aún).");
        var defaults = new List<ShaderConverter.UniformDefault>();
        try { ProgramId = BuildProgram(vertex, ShaderConverter.ConvertFragment(fragmentCode, defaults), name); }
        catch (Exception ex)
        {
            Console.WriteLine($"[FnrPort] Reintentando shader {name} con corrección de literales: {ex.Message}");
            defaults.Clear();
            ProgramId = BuildProgram(vertex, ShaderConverter.ConvertFragment(fragmentCode, defaults, fixIntLiterals: true), name);
        }
        FillUniforms();
        foreach (var d in defaults)
        {
            if (!mUniforms.TryGetValue(d.Name, out var u)) continue;
            var v = d.Values;
            WithProgram(() =>
            {
                if (d.Type == "bool" || d.Type == "int") GL.glUniform1i(u.Location, v.Length > 0 ? (int)v[0] : 0);
                else if (v.Length == 1 && d.Type == "float") GL.glUniform1f(u.Location, v[0]);
                else if (d.Type == "vec2" && v.Length >= 2) GL.glUniform2f(u.Location, v[0], v[1]);
                else if (d.Type == "vec2" && v.Length == 1) GL.glUniform2f(u.Location, v[0], v[0]);
                else if (d.Type == "vec3" && v.Length >= 3) GL.glUniform3f(u.Location, v[0], v[1], v[2]);
                else if (d.Type == "vec4" && v.Length >= 4) GL.glUniform4f(u.Location, v[0], v[1], v[2], v[3]);
            });
        }
    }

    public static unsafe uint BuildProgram(string vertexSrc, string fragmentSrc, string name)
    {
        uint Compile(int type, string src)
        {
            uint sh = GL.glCreateShader(type);
            GL.ShaderSource(sh, src);
            GL.glCompileShader(sh);
            int ok; GL.glGetShaderiv(sh, GL.COMPILE_STATUS, &ok);
            string log = GL.ShaderInfoLog(sh);
            if (ok == 0) throw new Exception($"Shader '{name}' no compila:\n{log}\n--- código ---\n{src}");
            if (!string.IsNullOrWhiteSpace(log)) Console.WriteLine($"[FnrPort] Shader '{name}': {log}");
            return sh;
        }
        Renderer.Flush();
        uint v = Compile(GL.VERTEX_SHADER, vertexSrc), f = Compile(GL.FRAGMENT_SHADER, fragmentSrc);
        uint p = GL.glCreateProgram();
        GL.glAttachShader(p, v); GL.glAttachShader(p, f);
        GL.glBindAttribLocation(p, 0, "a_pos");
        GL.glBindAttribLocation(p, 1, "a_tc");
        GL.glBindAttribLocation(p, 2, "a_color");
        GL.glLinkProgram(p);
        int linked; GL.glGetProgramiv(p, GL.LINK_STATUS, &linked);
        if (linked == 0) throw new Exception($"Shader '{name}' no enlaza: {GL.ProgramInfoLog(p)}");
        GL.glDeleteShader(v); GL.glDeleteShader(f);
        // el sampler principal siempre en la unidad 0
        int loc = GL.glGetUniformLocation(p, "texture");
        if (loc >= 0) { uint prev = Renderer.CurrentProgram; GL.glUseProgram(p); GL.glUniform1i(loc, 0); GL.glUseProgram(prev == uint.MaxValue ? 0 : prev); }
        return p;
    }

    unsafe void FillUniforms()
    {
        int count; GL.glGetProgramiv(ProgramId, GL.ACTIVE_UNIFORMS, &count);
        var buf = new byte[256];
        for (uint i = 0; i < count; i++)
        {
            int len, size, type;
            fixed (byte* b = buf) GL.glGetActiveUniform(ProgramId, i, buf.Length, &len, &size, &type, b);
            string n = System.Text.Encoding.ASCII.GetString(buf, 0, len);
            if (n.EndsWith("[0]")) n = n.Substring(0, n.Length - 3);
            mUniforms[n] = new Uniform { Location = GL.glGetUniformLocation(ProgramId, n), Type = type };
        }
    }

    Uniform Get(string name)
    {
        if (!mUniforms.TryGetValue(name, out var u))
            throw new ArgumentException("There's no active parameter named \"" + name + "\"");
        return u;
    }

    // Igual que el original: activa el programa, pone el valor y restaura el anterior.
    void WithProgram(Action a)
    {
        Renderer.Flush();
        uint prev = Renderer.CurrentProgram;
        GL.glUseProgram(ProgramId);
        a();
        GL.glUseProgram(prev == uint.MaxValue ? 0 : prev);
    }

    public IShader GetBaseShader() => this;
    public void SetParameter(string name, float x, float y, float z, float w) { var u = Get(name); WithProgram(() => GL.glUniform4f(u.Location, x, y, z, w)); }
    public void SetParameter(string name, float x, float y, float z) { var u = Get(name); WithProgram(() => GL.glUniform3f(u.Location, x, y, z)); }
    public void SetParameter(string name, float x, float y) { var u = Get(name); WithProgram(() => GL.glUniform2f(u.Location, x, y)); }
    public void SetParameter(string name, float x) { var u = Get(name); WithProgram(() => GL.glUniform1f(u.Location, x)); }

    public void SetTexture(string name, IBitmap texture)
    {
        var u = Get(name);
        if (u.Type != GL.SAMPLER_2D) throw new ArgumentException("There's no Sampler2D parameter named \"" + name + "\"");
        var tex = texture.GetBaseBitmap() as PortTexture;
        WithProgram(() =>
        {
            GL.glActiveTexture(GL.TEXTURE1);
            GL.glBindTexture(GL.TEXTURE_2D, tex.Id);
            GL.glUniform1i(u.Location, 1);
            GL.glActiveTexture(GL.TEXTURE0);
        });
    }

    public void Dispose()
    {
        if (ProgramId != 0) { Renderer.Flush(); GL.glDeleteProgram(ProgramId); ProgramId = 0; }
        mUniforms.Clear();
    }
}
