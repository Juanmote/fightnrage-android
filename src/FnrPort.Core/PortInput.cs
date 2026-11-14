using System;
using System.Collections.Generic;
using SebaGames.Core;
using SebaGames.Framework;

namespace FnrPort;

/// <summary>Estado de un mando tal como lo entrega la plataforma (SDL en PC, InputDevice en Android).</summary>
public struct PadSnapshot
{
    public bool Connected;
    public string Name;
    public bool A, B, X, Y, LB, RB, Back, Start, Guide, LS, RS;
    public bool C, Z; // mandos de seis botones (Retroid Pocket Classic, Saturn...): botones 13 y 14 del juego
    public bool DUp, DDown, DLeft, DRight;
    public float LX, LY, RX, RY, LT, RT; // ejes -1..1, gatillos 0..1
}

/// <summary>
/// IInputEngine. Traduce los mandos igual que SDL_InputExtension del juego (configuración por
/// defecto de la versión GOG: analógicos como cruceta y stick izquierdo sumado a la cruceta).
/// </summary>
public class PortInput : IInputEngine
{
    public const int MaxPads = 8;
    const float DeadZone = 0.25f; // Joypad.DeadZone por defecto

    readonly JoypadState[] mPads = new JoypadState[MaxPads];
    MouseState mMouse;
    readonly HashSet<int> mDownKeys = new();
    public bool UnifyLeftAnalogIntoDpad = true;
    public bool TreatAnalogsAsDpad = true;
    int mGuestSlot = -1;

    public event Action<int> KeyPressed;
    public event Action<int> KeyReleased;
    public event Action<char> WriteChar;

    internal void Initialize()
    {
        for (int i = 0; i < MaxPads; i++) mPads[i] = JoypadState.Empty;
        mMouse = MouseState.Empty;
    }

    public JoypadState[] GetJoyPadStates() => mPads;
    public MouseState GetMouseState() => mMouse;

    // ---- llamadas desde la plataforma ----

    public void KeyDown(int gameKey)
    {
        if (gameKey <= 0) return;
        mDownKeys.Add(gameKey);
        KeyPressed?.Invoke(gameKey);
    }

    public void KeyUp(int gameKey)
    {
        if (gameKey <= 0) return;
        mDownKeys.Remove(gameKey);
        KeyReleased?.Invoke(gameKey);
    }

    public void Text(string s) { foreach (var c in s) WriteChar?.Invoke(c); }

    public void MouseMove(int x, int y) { mMouse.Pos = new Coord2D(x, y); }
    public void MouseButton(int index, bool down) { var b = mMouse.ButtonStates; if (index >= 0 && index < b.Length) b[index] = down; }

    public void LosesFocus()
    {
        foreach (var k in new List<int>(mDownKeys)) KeyUp(k);
    }

    static float AsDpad(float p) => p < -DeadZone ? -1f : p > DeadZone ? 1f : 0f;

    /// <summary>Actualiza los 8 mandos a partir de lo que ve la plataforma (una vez por frame).</summary>
    public void UpdatePads(PadSnapshot[] snaps)
    {
        // Juego en red: los botones del invitado entran como un mando más, en el primer hueco libre.
        if (FnrPort.Net.NetSession.GuestPad(out var guest))
        {
            snaps = snaps == null ? new PadSnapshot[MaxPads] : (PadSnapshot[])snaps.Clone();
            if (mGuestSlot < 0 || mGuestSlot >= snaps.Length || snaps[mGuestSlot].Connected)
            {
                mGuestSlot = -1;
                for (int i = 0; i < snaps.Length; i++) if (!snaps[i].Connected) { mGuestSlot = i; break; }
            }
            if (mGuestSlot >= 0) snaps[mGuestSlot] = guest;
        }
        else mGuestSlot = -1;
        for (int i = 0; i < MaxPads; i++)
        {
            var st = mPads[i];
            var p = snaps != null && i < snaps.Length ? snaps[i] : default;
            if (!p.Connected) { st.Reset(); mPads[i] = st; continue; }
            st.Description = p.Name ?? "Gamepad";
            st.IsConnected = true;
            st.SetStickPos(0, p.DLeft ? -1f : p.DRight ? 1f : 0f);
            st.SetStickPos(1, p.DUp ? -1f : p.DDown ? 1f : 0f);
            if (TreatAnalogsAsDpad)
            {
                st.SetStickPos(2, AsDpad(p.LX)); st.SetStickPos(3, AsDpad(p.LY));
                st.SetStickPos(4, AsDpad(p.RX)); st.SetStickPos(5, AsDpad(p.RY));
            }
            else
            {
                st.SetStickPos(2, p.LX); st.SetStickPos(3, p.LY); st.SetStickPos(4, p.RX); st.SetStickPos(5, p.RY);
            }
            if (UnifyLeftAnalogIntoDpad)
            {
                var s = st.StickStates;
                s[0] = Math.Clamp(s[0] + AsDpad(s[2]), -1f, 1f);
                s[1] = Math.Clamp(s[1] + AsDpad(s[3]), -1f, 1f);
            }
            st.SetStickPos(6, p.LT);
            st.SetStickPos(7, p.RT);
            st.SetButtonState(0u, p.A); st.SetButtonState(1u, p.B); st.SetButtonState(2u, p.X); st.SetButtonState(3u, p.Y);
            st.SetButtonState(4u, p.LB); st.SetButtonState(5u, p.RB); st.SetButtonState(6u, p.Back); st.SetButtonState(7u, p.Start);
            st.SetButtonState(8u, p.Guide);
            st.SetButtonState(11u, p.LT != 0f); st.SetButtonState(12u, p.RT != 0f);
            st.SetButtonState(9u, p.LS); st.SetButtonState(10u, p.RS);
            st.SetButtonState(13u, p.C); st.SetButtonState(14u, p.Z);
            mPads[i] = st;
        }
    }
}

/// <summary>Códigos de tecla del juego (enum SebaGames.Framework.Key).</summary>
public static class GameKey
{
    public const int Num0 = 48, A = 97, Plus = 123, Escape = 256, LControl = 257, LShift = 258, LAlt = 259, LSystem = 260,
        RControl = 261, RShift = 262, RAlt = 263, RSystem = 264, Menu = 265, LBracket = 266, RBracket = 267, SemiColon = 268,
        Comma = 269, Period = 270, Quote = 271, Slash = 272, BackSlash = 273, Tilde = 274, Equal = 275, Dash = 276, Space = 277,
        Enter = 278, Backspace = 279, Tab = 280, PageUp = 281, PageDown = 282, End = 283, Home = 284, Insert = 285, Delete = 286,
        NumpadAdd = 287, Subtract = 288, NumpadMultiply = 289, NumpadDivide = 290, Left = 291, Right = 292, Up = 293, Down = 294,
        Numpad0 = 295, F1 = 305, Pause = 320, NumpadEnter = 321, NumpadMinus = 322, NumpadDecimal = 323;
}
