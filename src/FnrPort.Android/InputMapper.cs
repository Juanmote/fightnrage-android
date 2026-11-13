using System;
using Android.Views;

namespace FnrPort.Droid;

/// <summary>Traduce eventos de Android (mandos y teclado físico) al formato del port.</summary>
static class InputMapper
{
    static bool IsPad(InputEvent e)
    {
        var src = e.Source;
        if ((src & InputSourceType.Gamepad) == InputSourceType.Gamepad) return true;
        if ((src & InputSourceType.Joystick) == InputSourceType.Joystick) return true;
        var dev = e.Device;
        if (dev != null)
        {
            var ds = dev.Sources;
            if ((ds & InputSourceType.Gamepad) == InputSourceType.Gamepad || (ds & InputSourceType.Joystick) == InputSourceType.Joystick)
                return (src & InputSourceType.Dpad) == InputSourceType.Dpad || (src & InputSourceType.Keyboard) == InputSourceType.Keyboard;
        }
        return false;
    }

    public static bool HandleKey(AndroidHost host, KeyEvent e)
    {
        if (e.RepeatCount > 0 && e.Action == KeyEventActions.Down) return true;
        bool down = e.Action == KeyEventActions.Down;
        if (e.Action != KeyEventActions.Down && e.Action != KeyEventActions.Up) return false;
        var k = e.KeyCode;

        if (IsPad(e) && IsPadKey(k))
        {
            string name = e.Device?.Name ?? "Gamepad";
            host.UpdatePad(e.DeviceId, name, p =>
            {
                switch (k)
                {
                    case Keycode.ButtonA: p.A = down; break;
                    case Keycode.ButtonB: p.B = down; break;
                    case Keycode.ButtonX: p.X = down; break;
                    case Keycode.ButtonY: p.Y = down; break;
                    case Keycode.ButtonL1: p.LB = down; break;
                    case Keycode.ButtonR1: p.RB = down; break;
                    case Keycode.ButtonL2: p.LT = down ? 1f : 0f; break;
                    case Keycode.ButtonR2: p.RT = down ? 1f : 0f; break;
                    case Keycode.ButtonThumbl: p.LS = down; break;
                    case Keycode.ButtonThumbr: p.RS = down; break;
                    case Keycode.ButtonStart: p.Start = down; break;
                    case Keycode.ButtonSelect: p.Back = down; break;
                    case Keycode.Back: p.Back = down; break;
                    case Keycode.ButtonMode: p.Guide = down; break;
                    case Keycode.DpadUp: p.DUp = down; break;
                    case Keycode.DpadDown: p.DDown = down; break;
                    case Keycode.DpadLeft: p.DLeft = down; break;
                    case Keycode.DpadRight: p.DRight = down; break;
                }
                return p;
            });
            return true;
        }

        int gk = MapKey(k);
        if (gk == 0) return false;
        host.Enqueue(down ? (i => i.KeyDown(gk)) : (i => i.KeyUp(gk)));
        if (down && e.UnicodeChar > 0 && !char.IsControl((char)e.UnicodeChar))
        {
            string s = ((char)e.UnicodeChar).ToString();
            host.Enqueue(i => i.Text(s));
        }
        return true;
    }

    static bool IsPadKey(Keycode k) => k switch
    {
        Keycode.ButtonA or Keycode.ButtonB or Keycode.ButtonX or Keycode.ButtonY or Keycode.ButtonL1 or Keycode.ButtonR1 or
        Keycode.ButtonL2 or Keycode.ButtonR2 or Keycode.ButtonThumbl or Keycode.ButtonThumbr or Keycode.ButtonStart or
        Keycode.ButtonSelect or Keycode.ButtonMode or Keycode.DpadUp or Keycode.DpadDown or Keycode.DpadLeft or Keycode.DpadRight or Keycode.Back => true,
        _ => false
    };

    public static bool HandleMotion(AndroidHost host, MotionEvent e)
    {
        if (!IsPad(e) || e.Action != MotionEventActions.Move) return false;
        string name = e.Device?.Name ?? "Gamepad";
        float Ax(Axis a) => e.GetAxisValue(a);
        float hx = Ax(Axis.HatX), hy = Ax(Axis.HatY);
        float lt = Math.Max(Ax(Axis.Ltrigger), Ax(Axis.Brake));
        float rt = Math.Max(Ax(Axis.Rtrigger), Ax(Axis.Gas));
        host.UpdatePad(e.DeviceId, name, p =>
        {
            p.LX = Ax(Axis.X); p.LY = Ax(Axis.Y); p.RX = Ax(Axis.Z); p.RY = Ax(Axis.Rz);
            p.LT = lt; p.RT = rt;
            // Cruceta por ejes HAT (muchos mandos la envían así en vez de como teclas)
            if (hx != 0 || hy != 0 || mHatSeen.Contains(e.DeviceId))
            {
                mHatSeen.Add(e.DeviceId);
                p.DLeft = hx < -0.5f; p.DRight = hx > 0.5f; p.DUp = hy < -0.5f; p.DDown = hy > 0.5f;
            }
            return p;
        });
        return true;
    }

    static readonly System.Collections.Generic.HashSet<int> mHatSeen = new();

    static int MapKey(Keycode k)
    {
        int c = (int)k;
        if (c >= (int)Keycode.A && c <= (int)Keycode.Z) return GameKey.A + (c - (int)Keycode.A);
        if (c >= (int)Keycode.Num0 && c <= (int)Keycode.Num9) return GameKey.Num0 + (c - (int)Keycode.Num0);
        if (c >= (int)Keycode.F1 && c <= (int)Keycode.F12) return GameKey.F1 + (c - (int)Keycode.F1);
        return k switch
        {
            Keycode.Enter or Keycode.NumpadEnter => GameKey.Enter,
            Keycode.Escape or Keycode.Back => GameKey.Escape,
            Keycode.Del => GameKey.Backspace,
            Keycode.ForwardDel => GameKey.Delete,
            Keycode.Tab => GameKey.Tab,
            Keycode.Space => GameKey.Space,
            Keycode.DpadUp => GameKey.Up,
            Keycode.DpadDown => GameKey.Down,
            Keycode.DpadLeft => GameKey.Left,
            Keycode.DpadRight => GameKey.Right,
            Keycode.ShiftLeft => GameKey.LShift,
            Keycode.ShiftRight => GameKey.RShift,
            Keycode.CtrlLeft => GameKey.LControl,
            Keycode.CtrlRight => GameKey.RControl,
            Keycode.AltLeft => GameKey.LAlt,
            Keycode.AltRight => GameKey.RAlt,
            Keycode.Comma => GameKey.Comma,
            Keycode.Period => GameKey.Period,
            Keycode.Minus => GameKey.Dash,
            Keycode.Equals => GameKey.Plus,
            Keycode.Slash => GameKey.Slash,
            Keycode.Backslash => GameKey.BackSlash,
            Keycode.Semicolon => GameKey.SemiColon,
            Keycode.Apostrophe => GameKey.Quote,
            Keycode.Grave => GameKey.Tilde,
            Keycode.LeftBracket => GameKey.LBracket,
            Keycode.RightBracket => GameKey.RBracket,
            Keycode.PageUp => GameKey.PageUp,
            Keycode.PageDown => GameKey.PageDown,
            Keycode.MoveHome => GameKey.Home,
            Keycode.MoveEnd => GameKey.End,
            _ => 0
        };
    }
}
