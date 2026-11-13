using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace FnrPort.Desktop;

/// <summary>Host de escritorio (Linux/Windows) con SDL2 + contexto OpenGL ES. Sirve para probar el port en PC.</summary>
public sealed class DesktopHost : IPortHost
{
    IntPtr mWindow, mContext;
    readonly IntPtr[] mPads = new IntPtr[PortInput.MaxPads];
    readonly PadSnapshot[] mSnaps = new PadSnapshot[PortInput.MaxPads];
    public bool Fullscreen;
    public int ForcedWidth, ForcedHeight;
    public Func<PortInput, bool> Script; // automatización para pruebas (devuelve false para salir)

    public int DrawableWidth { get; private set; }
    public int DrawableHeight { get; private set; }
    public float RefreshRate { get; private set; } = 60;
    public string OsDescription => RuntimeInformation.OSDescription;

    public void CreateWindow(string caption, int width, int height)
    {
        SDL.SDL_SetHint("SDL_VIDEO_X11_FORCE_EGL", "1");
        SDL.SDL_SetHint("SDL_OPENGL_ES_DRIVER", "1");
        if (SDL.SDL_Init(SDL.INIT_VIDEO | SDL.INIT_GAMECONTROLLER | SDL.INIT_JOYSTICK | SDL.INIT_EVENTS) != 0)
            throw new Exception("SDL_Init: " + SDL.Error());
        if (File.Exists("gamecontrollerdb.txt"))
            SDL.SDL_GameControllerAddMappingsFromRW(SDL.SDL_RWFromFile("gamecontrollerdb.txt", "rb"), 1);
        SDL.SDL_GL_SetAttribute(SDL.GL_CONTEXT_PROFILE_MASK, SDL.GL_CONTEXT_PROFILE_ES);
        SDL.SDL_GL_SetAttribute(SDL.GL_CONTEXT_MAJOR_VERSION, 3);
        SDL.SDL_GL_SetAttribute(SDL.GL_CONTEXT_MINOR_VERSION, 0);
        SDL.SDL_GL_SetAttribute(SDL.GL_DOUBLEBUFFER, 1);
        if (SDL.SDL_GetCurrentDisplayMode(0, out var mode) == 0 && mode.refresh_rate > 0) RefreshRate = mode.refresh_rate;
        int w = ForcedWidth > 0 ? ForcedWidth : width, h = ForcedHeight > 0 ? ForcedHeight : height;
        uint flags = SDL.WINDOW_OPENGL | SDL.WINDOW_SHOWN | SDL.WINDOW_RESIZABLE | SDL.WINDOW_ALLOW_HIGHDPI;
        if (Fullscreen) flags |= SDL.WINDOW_FULLSCREEN_DESKTOP;
        mWindow = SDL.SDL_CreateWindow(caption, SDL.WINDOWPOS_CENTERED, SDL.WINDOWPOS_CENTERED, w, h, flags);
        if (mWindow == IntPtr.Zero) throw new Exception("SDL_CreateWindow: " + SDL.Error());
        mContext = SDL.SDL_GL_CreateContext(mWindow);
        if (mContext == IntPtr.Zero)
        {
            Console.WriteLine("[FnrPort] Sin GLES 3.0, probando 2.0: " + SDL.Error());
            SDL.SDL_GL_SetAttribute(SDL.GL_CONTEXT_MAJOR_VERSION, 2);
            mContext = SDL.SDL_GL_CreateContext(mWindow);
            if (mContext == IntPtr.Zero) throw new Exception("SDL_GL_CreateContext: " + SDL.Error());
        }
        SDL.SDL_GL_SetSwapInterval(1);
        SDL.SDL_StartTextInput();
        UpdateSize();
    }

    void UpdateSize()
    {
        SDL.SDL_GL_GetDrawableSize(mWindow, out int w, out int h);
        DrawableWidth = w; DrawableHeight = h;
    }

    public unsafe bool PumpEvents(PortInput input, PortCore core)
    {
        bool keepGoing = true;
        while (SDL.SDL_PollEvent(out var e) != 0)
        {
            switch (e.type)
            {
                case SDL.QUIT: keepGoing = false; break;
                case SDL.WINDOWEVENT:
                    if (e.windowEvent == SDL.WINDOWEVENT_FOCUS_GAINED) core.SetFocus(true);
                    else if (e.windowEvent == SDL.WINDOWEVENT_FOCUS_LOST) core.SetFocus(false);
                    break;
                case SDL.KEYDOWN:
                    if (e.keyRepeat != 0) break;
                    if (e.scancode == 68) { Fullscreen = !Fullscreen; SDL.SDL_SetWindowFullscreen(mWindow, Fullscreen ? SDL.WINDOW_FULLSCREEN_DESKTOP : 0); break; } // F11
                    input.KeyDown(MapScancode(e.scancode));
                    break;
                case SDL.KEYUP:
                    input.KeyUp(MapScancode(e.scancode));
                    break;
                case SDL.TEXTINPUT:
                {
                    int n = 0; while (n < 32 && e.text[n] != 0) n++;
                    input.Text(Encoding.UTF8.GetString(e.text, n));
                    break;
                }
                case SDL.MOUSEMOTION: input.MouseMove(e.motionX, e.motionY); break;
                case SDL.MOUSEBUTTONDOWN: input.MouseButton(MouseIndex(e.button), true); break;
                case SDL.MOUSEBUTTONUP: input.MouseButton(MouseIndex(e.button), false); break;
            }
        }
        UpdateSize();
        UpdatePads();
        input.UpdatePads(mSnaps);
        if (Script != null && !Script(input)) keepGoing = false;
        return keepGoing;
    }

    // SebaGames.Framework.MouseButtons: Left=0, Right=1, Middle=2 (como OpenTK)
    static int MouseIndex(byte sdlButton) => sdlButton switch { 1 => 0, 3 => 1, _ => 2 };

    void UpdatePads()
    {
        int slot = 0;
        int n = SDL.SDL_NumJoysticks();
        for (int j = 0; j < n && slot < PortInput.MaxPads; j++)
        {
            if (SDL.SDL_IsGameController(j) == 0) continue;
            if (mPads[slot] == IntPtr.Zero || SDL.SDL_GameControllerGetAttached(mPads[slot]) == 0)
                mPads[slot] = SDL.SDL_GameControllerOpen(j);
            var c = mPads[slot];
            if (c == IntPtr.Zero) continue;
            bool B(int b) => SDL.SDL_GameControllerGetButton(c, b) != 0;
            float A(int a) => SDL.SDL_GameControllerGetAxis(c, a) / 32767f;
            mSnaps[slot] = new PadSnapshot
            {
                Connected = true, Name = Marshal.PtrToStringAnsi(SDL.SDL_GameControllerName(c)),
                A = B(0), B = B(1), X = B(2), Y = B(3), Back = B(4), Guide = B(5), Start = B(6), LS = B(7), RS = B(8), LB = B(9), RB = B(10),
                DUp = B(11), DDown = B(12), DLeft = B(13), DRight = B(14),
                LX = A(0), LY = A(1), RX = A(2), RY = A(3), LT = A(4), RT = A(5),
            };
            slot++;
        }
        for (; slot < PortInput.MaxPads; slot++) mSnaps[slot] = default;
    }

    public void SwapBuffers() => SDL.SDL_GL_SwapWindow(mWindow);

    public void DestroyWindow()
    {
        if (mContext != IntPtr.Zero) SDL.SDL_GL_DeleteContext(mContext);
        if (mWindow != IntPtr.Zero) SDL.SDL_DestroyWindow(mWindow);
        SDL.SDL_Quit();
    }

    static readonly Dictionary<int, int> sKeys = BuildKeys();

    static Dictionary<int, int> BuildKeys()
    {
        var k = new Dictionary<int, int>();
        for (int i = 0; i < 26; i++) k[4 + i] = GameKey.A + i;                 // A..Z
        for (int i = 1; i <= 9; i++) k[29 + i] = GameKey.Num0 + i;             // 1..9
        k[39] = GameKey.Num0;
        k[40] = GameKey.Enter; k[41] = GameKey.Escape; k[42] = GameKey.Backspace; k[43] = GameKey.Tab; k[44] = GameKey.Space;
        k[45] = GameKey.Dash; k[46] = GameKey.Plus; k[47] = GameKey.LBracket; k[48] = GameKey.RBracket; k[49] = GameKey.BackSlash;
        k[51] = GameKey.SemiColon; k[52] = GameKey.Quote; k[53] = GameKey.Tilde; k[54] = GameKey.Comma; k[55] = GameKey.Period; k[56] = GameKey.Slash;
        for (int i = 0; i < 12; i++) k[58 + i] = GameKey.F1 + i;               // F1..F12
        for (int i = 0; i < 3; i++) k[104 + i] = GameKey.F1 + 12 + i;          // F13..F15
        k[72] = GameKey.Pause; k[73] = GameKey.Insert; k[74] = GameKey.Home; k[75] = GameKey.PageUp; k[76] = GameKey.Delete; k[77] = GameKey.End; k[78] = GameKey.PageDown;
        k[79] = GameKey.Right; k[80] = GameKey.Left; k[81] = GameKey.Down; k[82] = GameKey.Up;
        k[84] = GameKey.NumpadDivide; k[85] = GameKey.NumpadMultiply; k[86] = GameKey.NumpadMinus; k[87] = GameKey.NumpadAdd; k[88] = GameKey.NumpadEnter;
        for (int i = 0; i < 9; i++) k[89 + i] = GameKey.Numpad0 + 1 + i;       // KP 1..9
        k[98] = GameKey.Numpad0; k[99] = GameKey.NumpadDecimal;
        k[224] = GameKey.LControl; k[225] = GameKey.LShift; k[226] = GameKey.LAlt; k[227] = GameKey.LSystem;
        k[228] = GameKey.RControl; k[229] = GameKey.RShift; k[230] = GameKey.RAlt; k[231] = GameKey.RSystem;
        return k;
    }

    static int MapScancode(int sc) => sKeys.TryGetValue(sc, out var v) ? v : 0;
}
