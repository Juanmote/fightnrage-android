// Declaraciones mínimas (firmas) de los tipos públicos de FIGHT_N_RAGE.exe que usa el backend.
// Todos los cuerpos lanzan excepción: nunca se ejecutan, en tiempo de ejecución se carga el exe real.
#pragma warning disable
using System;
using System.Xml;

namespace SebaGames.Core
{
    public struct Color
    {
        public byte R { get => throw null; set => throw null; }
        public byte G { get => throw null; set => throw null; }
        public byte B { get => throw null; set => throw null; }
        public byte A { get => throw null; set => throw null; }
        public Color(byte red, byte green, byte blue, byte alpha) => throw null;
        public ColorF ToColorF() => throw null;
    }
    public struct ColorF
    {
        public float R, G, B, A;
        public ColorF(float aR, float aG, float aB, float aA) => throw null;
    }
    public struct Rectangle
    {
        public int X { get => throw null; set => throw null; }
        public int Y { get => throw null; set => throw null; }
        public int Width { get => throw null; set => throw null; }
        public int Height { get => throw null; set => throw null; }
        public int Top => throw null;
        public int Left => throw null;
        public int Right => throw null;
        public int Bottom => throw null;
        public Rectangle(int aX, int aY, int aWidth, int aHeight) => throw null;
    }
    public struct Coord2D
    {
        public int X { get => throw null; set => throw null; }
        public int Y { get => throw null; set => throw null; }
        public Coord2D(int aX, int aY) => throw null;
    }
    public struct Vector2D
    {
        public float X { get => throw null; set => throw null; }
        public float Y { get => throw null; set => throw null; }
        public static Vector2D One => throw null;
        public Vector2D(float aX, float aY) => throw null;
    }
    public struct Matrix4d
    {
        public double M00, M01, M02, M03, M10, M11, M12, M13, M20, M21, M22, M23, M30, M31, M32, M33;
    }
    public static class PathIndexer
    {
        public static void SetFromDatFile(string aPath) => throw null;
        public static string GetPath(string aKey, bool aIgnoreFail = true) => throw null;
        public static bool IsCompressed(string aPath) => throw null;
        public static string ReadAllText(string aPath) => throw null;
    }
}

namespace SebaGames.Framework
{
    using SebaGames.Core;

    public delegate void Updater(float aDeltaTime);
    public delegate void ErrorProvider(string aError);
    public delegate void PerformanceReport(int aUpdateCalls, int aRenderCalls);

    public enum BLEND_MODE { NORMAL, ADD, MAX, COLOR_ADD, INVERT, MULTIPLY, NONE, MASK, MASK_BELOW }
    public enum PrimitiveType { POINTS, LINES, LINE_LOOP, LINE_STRIP, TRIANGLES, TRIANGLE_STRIP, TRIANGLE_FAN, QUADS, QUAD_STRIP, POLYGON, LINES_ADJACENCY, LINE_STRIP_ADJACENCY, TRIANGLES_ADJACENCY, TRIANGLE_STRIP_ADJACENCY }
    public enum TextureAddressingMode { WRAP, MIRROR, CLAMP }

    public struct Vertex2D { public Vector2D Position; public Vector2D TextCoord; public ColorF Color; }

    public struct Transformation
    {
        public Vector2D Position;
        public Vector2D Scale;
        public float Rotation;
        public float X { get => throw null; set => throw null; }
        public float Y { get => throw null; set => throw null; }
        public static Transformation Default => throw null;
    }

    public struct JoypadState
    {
        public string Description { get => throw null; set => throw null; }
        public bool IsConnected { get => throw null; set => throw null; }
        public bool[] ButtonStates { get => throw null; set => throw null; }
        public float[] StickStates { get => throw null; set => throw null; }
        public static JoypadState Empty => throw null;
        public float GetStickPos(int aStickIndex) => throw null;
        public void SetStickPos(int aStickIndex, float aPos) => throw null;
        public void SetButtonState(uint aButtonIndex, bool aState) => throw null;
        public void Reset() => throw null;
    }

    public struct MouseState
    {
        public Coord2D Pos { get => throw null; set => throw null; }
        public bool[] ButtonStates { get => throw null; set => throw null; }
        public static MouseState Empty => throw null;
    }

    public struct AppDescriptor
    {
        public enum SCALE_MODE { NO_SCALE, KEEP_ASPECT_RATIO, FULL, KEEP_PIXEL_ASPECT }
        public string Caption { get => throw null; set => throw null; }
        public uint DisplayWidth { get => throw null; set => throw null; }
        public uint DisplayHeight { get => throw null; set => throw null; }
        public uint MainSurfaceWidth { get => throw null; set => throw null; }
        public uint MainSurfaceHeight { get => throw null; set => throw null; }
        public SCALE_MODE ScaleMode { get => throw null; set => throw null; }
        public bool SmoothScale { get => throw null; set => throw null; }
        public bool IntegerScale { get => throw null; set => throw null; }
        public bool Vsync { get => throw null; set => throw null; }
        public uint MaxFps { get => throw null; set => throw null; }
        public static AppDescriptor Default => throw null;
    }

    public class App
    {
        public class SystemInfo
        {
            public SystemInfo(int aScreenWidth, int aScreenHeight, float aRefreshRate, int aBitsPerPixel, int aScreenAmount, string aVideoCard, string aShaderVersion, string aGLVersion, string aOperatingSystem) => throw null;
        }
        public static App Inst => throw null;
        public bool IgnoreCurrentFrameSkip { get => throw null; set => throw null; }
        public bool IsPlayingReplay => throw null;
    }

    public interface IShader : IDisposable
    {
        IShader GetBaseShader();
        void SetParameter(string name, float x, float y, float z, float w);
        void SetParameter(string name, float x, float y, float z);
        void SetParameter(string name, float x, float y);
        void SetParameter(string name, float x);
        void SetTexture(string name, IBitmap texture);
    }
    public interface IBitmap : IDisposable
    {
        int Width { get; }
        int Height { get; }
        Rectangle FullRect { get; }
        Color GetPixel(Coord2D aPos);
        void SetPixel(Coord2D aPos, Color aColor);
        void SetPixels(byte[] aPixels);
        void SetPixels(byte[] aPixels, Rectangle aZone);
        byte[] GetPixels();
        void Save(string aFileName);
        IBitmap GetBaseBitmap();
    }
    public interface IDrawableSurface : IDisposable
    {
        int Width { get; }
        int Height { get; }
        Color Color { get; set; }
        BLEND_MODE BlendMode { get; set; }
        bool Smoothing { get; set; }
        IShader Shader { get; set; }
        Rectangle FullRect { get; }
        Rectangle ClipRect { get; set; }
        int LastDrawCallsCount { get; }
        void Clear(Color aColor);
        void ResetMatrix();
        Matrix4d GetMatrix();
        void SetMatrix(Matrix4d aMatrixObj);
        void ApplyMatrixTransformation(Transformation aTransformation);
        void Draw(IBitmap aBitmap, Rectangle aSourceRect, Transformation aTransformation, Vector2D aAxis);
        void DrawVertexs(IBitmap aBitmap, PrimitiveType aPrimitiveType, TextureAddressingMode aTextureAddressingMode, int maxVertex, params Vertex2D[] aVertexs);
        void DrawVertexs(IBitmap aBitmap, PrimitiveType aPrimitiveType, TextureAddressingMode aTextureAddressingMode, params Vertex2D[] aVertexs);
        IBitmap GetBitmap();
        void Renderize();
        IDrawableSurface GetBaseSurface();
    }
    public interface IDisplayEngine
    {
        IDrawableSurface MainSurface { get; }
        IDrawableSurface CreateSurface(int aWidth, int aHeight);
        IBitmap CreateBitmap(int aWidth, int aHeight);
        IBitmap CreateBitmap(string aFile);
        bool IsShaderAvailable();
        IShader CreateShader(string aFile);
    }
    public interface IInputEngine
    {
        event Action<int> KeyPressed;
        event Action<int> KeyReleased;
        event Action<char> WriteChar;
        JoypadState[] GetJoyPadStates();
        MouseState GetMouseState();
    }
    public interface IAudioPlayback : IDisposable
    {
        float Pan { get; set; }
        float Volume { get; set; }
        float Pitch { get; set; }
        bool Loop { get; set; }
        bool IsPlaying { get; }
        void Play();
        void Stop();
        void Pause();
        void Resume();
    }
    public interface ISound : IDisposable
    {
        IAudioPlayback Inst { get; }
        ISoundInstance CreateInstance();
        void Play(float aVolume, float aPosition, float aPitch, bool aLoop);
        void Stop();
        ISound GetBaseSound();
    }
    public interface ISoundInstance : IAudioPlayback, IDisposable
    {
        ISound Sound { get; }
        void ChangeSound(ISound aSound);
        ISoundInstance GetBaseSoundInstance();
    }
    public interface IMusic : IAudioPlayback, IDisposable
    {
        IMusic GetBaseMusic();
    }
    public interface IAudioEngine
    {
        float MasterVolume { get; set; }
        bool Muted { get; set; }
        ISound CreateSound(string aFile);
        ISoundInstance CreateSoundInstance();
        IMusic CreateMusic(string aFile);
        void Initialize();
        void Dispose();
    }
    public interface ICoreEngine
    {
        IDisplayEngine DisplayEngine { get; }
        IInputEngine InputEngine { get; }
        IAudioEngine SoundEngine { get; }
        App.SystemInfo SystemInfo { get; }
        int WindowWidth { get; }
        int WindowHeight { get; }
        int FpsCountReport { get; }
        int UpdateCountReport { get; }
        int FrameSkipReport { get; }
        int TargetFrequency { get; }
        event Action OnStart;
        event Action OnClose;
        event Updater OnUpdate;
        event Updater OnRender;
        event ErrorProvider OnError;
        event Action OnLosesFocus;
        event Action OnRegainFocus;
        event PerformanceReport OnPassedOneSecond;
        int Initialize(AppDescriptor aDescriptor);
        void Close();
        void ShowMessage(string aType, string aMessage, string aSubMessage);
    }
}

namespace Empty_AudioImplementation
{
    public class Empty_AudioEngine : SebaGames.Framework.IAudioEngine
    {
        public Empty_AudioEngine() => throw null;
        public float MasterVolume { get => throw null; set => throw null; }
        public bool Muted { get => throw null; set => throw null; }
        public SebaGames.Framework.ISound CreateSound(string aFile) => throw null;
        public SebaGames.Framework.ISoundInstance CreateSoundInstance() => throw null;
        public SebaGames.Framework.IMusic CreateMusic(string aFile) => throw null;
        public void Initialize() => throw null;
        public void Dispose() => throw null;
    }
}

namespace BeatEmUpGame
{
    public static class Program
    {
        public static bool STEAM_VERSION;
        public static bool VSYNC;
        public static bool DISABLED_SETTING_STORAGE;
        public static int Run(SebaGames.Framework.ICoreEngine aEngineInstance, Func<object, object> aExternalCall, string aSaveData, string[] args) => throw null;
    }
    public static class ExternalCommands
    {
        public static string GetSaveDataInnerXml() => throw null;
    }
    public static class Constants
    {
        public static int WINDOW_SCALE;
    }
}
