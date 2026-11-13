using System;
using System.Runtime.InteropServices;

namespace FnrPort.Desktop;

/// <summary>Enlaces mínimos a SDL2 (solo lo que usa el host de escritorio).</summary>
static unsafe class SDL
{
    const string Lib = "SDL2";
    public const uint INIT_VIDEO = 0x20, INIT_GAMECONTROLLER = 0x2000, INIT_JOYSTICK = 0x200, INIT_EVENTS = 0x4000;
    public const uint WINDOW_OPENGL = 0x2, WINDOW_RESIZABLE = 0x20, WINDOW_SHOWN = 0x4, WINDOW_FULLSCREEN_DESKTOP = 0x1001, WINDOW_ALLOW_HIGHDPI = 0x2000;
    public const int WINDOWPOS_CENTERED = 0x2FFF0000;
    public const int GL_CONTEXT_MAJOR_VERSION = 17, GL_CONTEXT_MINOR_VERSION = 18, GL_CONTEXT_PROFILE_MASK = 21, GL_DOUBLEBUFFER = 5;
    public const int GL_CONTEXT_PROFILE_ES = 4;

    public const uint QUIT = 0x100, WINDOWEVENT = 0x200, KEYDOWN = 0x300, KEYUP = 0x301, TEXTINPUT = 0x303,
        MOUSEMOTION = 0x400, MOUSEBUTTONDOWN = 0x401, MOUSEBUTTONUP = 0x402, CONTROLLERDEVICEADDED = 0x653, CONTROLLERDEVICEREMOVED = 0x654;
    public const byte WINDOWEVENT_FOCUS_GAINED = 12, WINDOWEVENT_FOCUS_LOST = 13;

    [StructLayout(LayoutKind.Explicit, Size = 56)]
    public struct Event
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(12)] public byte windowEvent;     // SDL_WindowEvent.event
        [FieldOffset(12)] public byte keyState;        // SDL_KeyboardEvent.state
        [FieldOffset(13)] public byte keyRepeat;
        [FieldOffset(16)] public int scancode;         // SDL_Keysym.scancode
        [FieldOffset(20)] public int keycode;
        [FieldOffset(12)] public fixed byte text[32];  // SDL_TextInputEvent.text
        [FieldOffset(20)] public int motionX;          // SDL_MouseMotionEvent.x
        [FieldOffset(24)] public int motionY;
        [FieldOffset(16)] public byte button;          // SDL_MouseButtonEvent.button
        [FieldOffset(20)] public int buttonX;
        [FieldOffset(24)] public int buttonY;
    }

    [DllImport(Lib)] public static extern int SDL_Init(uint flags);
    [DllImport(Lib)] public static extern void SDL_Quit();
    [DllImport(Lib)] public static extern IntPtr SDL_GetError();
    [DllImport(Lib)] public static extern int SDL_SetHint(string name, string value);
    [DllImport(Lib)] public static extern int SDL_GL_SetAttribute(int attr, int value);
    [DllImport(Lib)] public static extern IntPtr SDL_CreateWindow(string title, int x, int y, int w, int h, uint flags);
    [DllImport(Lib)] public static extern void SDL_DestroyWindow(IntPtr w);
    [DllImport(Lib)] public static extern IntPtr SDL_GL_CreateContext(IntPtr w);
    [DllImport(Lib)] public static extern void SDL_GL_DeleteContext(IntPtr c);
    [DllImport(Lib)] public static extern int SDL_GL_SetSwapInterval(int interval);
    [DllImport(Lib)] public static extern void SDL_GL_SwapWindow(IntPtr w);
    [DllImport(Lib)] public static extern void SDL_GL_GetDrawableSize(IntPtr w, out int x, out int y);
    [DllImport(Lib)] public static extern int SDL_PollEvent(out Event e);
    [DllImport(Lib)] public static extern void SDL_StartTextInput();
    [DllImport(Lib)] public static extern int SDL_GetCurrentDisplayMode(int display, out DisplayMode mode);
    [DllImport(Lib)] public static extern void SDL_SetWindowFullscreen(IntPtr w, uint flags);
    [DllImport(Lib)] public static extern int SDL_GameControllerAddMappingsFromRW(IntPtr rw, int freerw);
    [DllImport(Lib)] public static extern IntPtr SDL_RWFromFile(string file, string mode);
    [DllImport(Lib)] public static extern int SDL_NumJoysticks();
    [DllImport(Lib)] public static extern int SDL_IsGameController(int index);
    [DllImport(Lib)] public static extern IntPtr SDL_GameControllerOpen(int index);
    [DllImport(Lib)] public static extern void SDL_GameControllerClose(IntPtr c);
    [DllImport(Lib)] public static extern int SDL_GameControllerGetAttached(IntPtr c);
    [DllImport(Lib)] public static extern byte SDL_GameControllerGetButton(IntPtr c, int button);
    [DllImport(Lib)] public static extern short SDL_GameControllerGetAxis(IntPtr c, int axis);
    [DllImport(Lib)] public static extern IntPtr SDL_GameControllerName(IntPtr c);

    [StructLayout(LayoutKind.Sequential)]
    public struct DisplayMode { public uint format; public int w, h, refresh_rate; public IntPtr driverdata; }

    // Audio (modo cola: sin callback)
    public const uint INIT_AUDIO = 0x10;
    public const ushort AUDIO_F32 = 0x8120;
    [StructLayout(LayoutKind.Sequential)]
    public struct AudioSpec { public int freq; public ushort format; public byte channels, silence; public ushort samples, padding; public uint size; public IntPtr callback, userdata; }
    [DllImport(Lib)] public static extern int SDL_InitSubSystem(uint flags);
    [DllImport(Lib)] public static extern void SDL_QuitSubSystem(uint flags);
    [DllImport(Lib)] public static extern uint SDL_OpenAudioDevice(IntPtr device, int iscapture, ref AudioSpec desired, out AudioSpec obtained, int allowedChanges);
    [DllImport(Lib)] public static extern void SDL_PauseAudioDevice(uint dev, int pause);
    [DllImport(Lib)] public static extern int SDL_QueueAudio(uint dev, void* data, uint len);
    [DllImport(Lib)] public static extern uint SDL_GetQueuedAudioSize(uint dev);
    [DllImport(Lib)] public static extern void SDL_ClearQueuedAudio(uint dev);
    [DllImport(Lib)] public static extern void SDL_CloseAudioDevice(uint dev);
    [DllImport(Lib)] public static extern IntPtr SDL_GetCurrentAudioDriver();

    public static string Error() => Marshal.PtrToStringAnsi(SDL_GetError());
}
