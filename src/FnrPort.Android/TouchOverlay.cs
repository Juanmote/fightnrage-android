using System;
using System.Collections.Generic;
using Android.Content;
using Android.Graphics;
using Android.Hardware.Input;
using Android.Views;

namespace FnrPort.Droid;

/// <summary>
/// Controles táctiles con el arte de Juanmote (sin rótulos, valen para cualquier idioma): cruceta a la
/// izquierda; Golpe, Salto y Especial a la derecha; Back y Start arriba. Funcionan como un mando más
/// (botones Y, B, A, Start y Back: la configuración por defecto del port). Sin tocar se ven
/// semitransparentes; lo que se pulsa (incluido cada lado de la cruceta) se ve opaco. Se ocultan solos
/// cuando se usa un mando físico y vuelven al tocar la pantalla.
/// </summary>
sealed class TouchOverlay : View, InputManager.IInputDeviceListener
{
    public const int DeviceId = -4242;
    readonly IPadSink mHost;
    readonly Settings mCfg;
    readonly InputManager mIm;
    bool mActive;          // los controles están a la vista y cuentan como mando
    bool mPadSeen;         // se ha usado un mando físico desde la última vez que se tocó la pantalla

    enum Ctl { None, Dpad, Attack, Jump, Special, Start, Back }
    sealed class Btn { public Ctl Kind; public float X, Y, R, W, H; public Bitmap Img; }
    readonly List<Btn> mButtons = new();
    Btn mDpad;
    readonly Dictionary<int, Ctl> mPointers = new();       // dedo -> control
    readonly Dictionary<int, (float x, float y)> mPos = new();
    readonly Paint mPaint = new(PaintFlags.AntiAlias | PaintFlags.FilterBitmap);
    readonly Bitmap mImgDpad, mImgAttack, mImgJump, mImgSpecial, mImgStart, mImgBack;
    readonly RectF mDst = new();

    public TouchOverlay(Context c, IPadSink host, Settings cfg) : base(c)
    {
        mHost = host; mCfg = cfg;
        Bitmap Img(int id) { try { return BitmapFactory.DecodeResource(c.Resources, id); } catch { return null; } }
        mImgDpad = Img(Resource.Drawable.tc_dpad); mImgAttack = Img(Resource.Drawable.tc_attack); mImgJump = Img(Resource.Drawable.tc_jump);
        mImgSpecial = Img(Resource.Drawable.tc_special); mImgStart = Img(Resource.Drawable.tc_start); mImgBack = Img(Resource.Drawable.tc_back);
        mIm = (InputManager)c.GetSystemService(Context.InputService);
        try { mIm?.RegisterInputDeviceListener(this, null); } catch { }
        RefreshVisibility();
    }

    /// <summary>¿Hay algún mando físico conectado?</summary>
    static bool PhysicalPadConnected()
    {
        foreach (int id in InputDevice.GetDeviceIds() ?? Array.Empty<int>())
        {
            var d = InputDevice.GetDevice(id);
            if (d == null || d.IsVirtual) continue;
            var s = d.Sources;
            if ((s & InputSourceType.Gamepad) == InputSourceType.Gamepad || (s & InputSourceType.Joystick) == InputSourceType.Joystick) return true;
        }
        return false;
    }

    void RefreshVisibility()
    {
        bool show = mCfg.TouchAuto && !mPadSeen && !PhysicalPadConnected();
        SetActive(show);
    }

    void SetActive(bool on)
    {
        if (on == mActive) return;
        mActive = on;
        mPointers.Clear(); mPos.Clear();
        if (on) mHost.UpdatePad(DeviceId, "Touch", p => new PadSnapshot { Connected = true, Name = "Touch" });
        else mHost.RemovePad(DeviceId);
        Invalidate();
    }

    /// <summary>Llamado al recibir cualquier evento de un mando físico: los táctiles se apartan.</summary>
    public void OnPhysicalPadInput()
    {
        if (mPadSeen) return;
        mPadSeen = true;
        Post(() => SetActive(false));
    }

    public void OnInputDeviceAdded(int deviceId) => RefreshVisibility();
    public void OnInputDeviceChanged(int deviceId) { }
    public void OnInputDeviceRemoved(int deviceId) => RefreshVisibility();

    protected override void OnDetachedFromWindow()
    {
        base.OnDetachedFromWindow();
        try { mIm?.UnregisterInputDeviceListener(this); } catch { }
        mHost.RemovePad(DeviceId);
    }

    protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
    {
        base.OnSizeChanged(w, h, oldw, oldh);
        DoLayout(w, h);
    }

    void DoLayout(int w, int h)
    {
        mButtons.Clear();
        float u = Math.Min(w, h) / 100f * mCfg.TouchSize / 100f; // unidad: 1 % del lado corto
        mDpad = new Btn { Kind = Ctl.Dpad, X = 22 * u + 2 * u, Y = h - 24 * u, R = 19 * u, Img = mImgDpad };
        float bx = w - 16 * u, by = h - 20 * u, br = 9.5f * u;
        // Golpe abajo a la izquierda, Salto abajo a la derecha, Especial encima (como Y, B y A en la Odin)
        mButtons.Add(new Btn { Kind = Ctl.Attack, X = bx - 22 * u, Y = by, R = br, Img = mImgAttack });
        mButtons.Add(new Btn { Kind = Ctl.Jump, X = bx, Y = by - 14 * u, R = br, Img = mImgJump });
        mButtons.Add(new Btn { Kind = Ctl.Special, X = bx - 22 * u, Y = by - 24 * u, R = br, Img = mImgSpecial });
        float ph = 8 * u;
        mButtons.Add(new Btn { Kind = Ctl.Back, X = w / 2f - 13 * u, Y = 7 * u, R = ph * 0.75f, W = ph * 2.07f, H = ph, Img = mImgBack });
        mButtons.Add(new Btn { Kind = Ctl.Start, X = w / 2f + 13 * u, Y = 7 * u, R = ph * 0.75f, W = ph * 2.07f, H = ph, Img = mImgStart });
    }

    public override bool OnTouchEvent(MotionEvent e)
    {
        if (!mCfg.TouchAuto) return false;
        if (!mActive)
        {
            // Tocar la pantalla con los controles ocultos los vuelve a mostrar.
            if (e.ActionMasked == MotionEventActions.Down) { mPadSeen = false; SetActive(true); }
            return true;
        }
        int idx = e.ActionIndex;
        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
            case MotionEventActions.PointerDown:
            {
                int id = e.GetPointerId(idx);
                float x = e.GetX(idx), y = e.GetY(idx);
                mPointers[id] = HitTest(x, y);
                mPos[id] = (x, y);
                break;
            }
            case MotionEventActions.Move:
                for (int i = 0; i < e.PointerCount; i++)
                {
                    int id = e.GetPointerId(i);
                    float x = e.GetX(i), y = e.GetY(i);
                    mPos[id] = (x, y);
                    // deslizar el dedo de un botón a otro (salvo desde la cruceta)
                    if (mPointers.TryGetValue(id, out var k) && k != Ctl.Dpad)
                    {
                        var hit = HitTest(x, y);
                        if (hit != Ctl.None && hit != Ctl.Dpad) mPointers[id] = hit;
                    }
                }
                break;
            case MotionEventActions.Up:
            case MotionEventActions.PointerUp:
            {
                int id = e.GetPointerId(idx);
                mPointers.Remove(id); mPos.Remove(id);
                break;
            }
            case MotionEventActions.Cancel:
                mPointers.Clear(); mPos.Clear();
                break;
        }
        Push();
        Invalidate();
        return true;
    }

    Ctl HitTest(float x, float y)
    {
        if (mDpad != null && Dist(x, y, mDpad.X, mDpad.Y) < mDpad.R * 1.5f) return Ctl.Dpad;
        Btn best = null; float bd = float.MaxValue;
        foreach (var b in mButtons)
        {
            float d = Dist(x, y, b.X, b.Y);
            if (d < b.R * 1.45f && d < bd) { bd = d; best = b; }
        }
        return best?.Kind ?? Ctl.None;
    }

    static float Dist(float x0, float y0, float x1, float y1) => MathF.Sqrt((x0 - x1) * (x0 - x1) + (y0 - y1) * (y0 - y1));

    bool mUp, mDown, mLeft, mRight;

    void Push()
    {
        bool up = false, down = false, left = false, right = false, atk = false, jump = false, sp = false, start = false, back = false;
        foreach (var (id, k) in mPointers)
        {
            switch (k)
            {
                case Ctl.Dpad:
                    if (mPos.TryGetValue(id, out var p))
                    {
                        float dx = p.x - mDpad.X, dy = p.y - mDpad.Y;
                        if (Dist(dx, dy, 0, 0) < mDpad.R * 0.22f) break; // zona muerta central
                        double a = Math.Atan2(-dy, dx) * 180 / Math.PI; // 0 = derecha, 90 = arriba
                        if (a < 0) a += 360;
                        // 8 direcciones (sectores de 45°)
                        right |= a < 67.5 || a >= 292.5;
                        up |= a >= 22.5 && a < 157.5;
                        left |= a >= 112.5 && a < 247.5;
                        down |= a >= 202.5 && a < 337.5;
                    }
                    break;
                case Ctl.Attack: atk = true; break;
                case Ctl.Jump: jump = true; break;
                case Ctl.Special: sp = true; break;
                case Ctl.Start: start = true; break;
                case Ctl.Back: back = true; break;
            }
        }
        mUp = up; mDown = down; mLeft = left; mRight = right;
        mHost.UpdatePad(DeviceId, "Touch", p =>
        {
            p.Connected = true; p.Name = "Touch";
            p.DUp = up; p.DDown = down; p.DLeft = left; p.DRight = right;
            p.Y = atk; p.B = jump; p.A = sp; p.Start = start; p.Back = back;
            return p;
        });
    }

    protected override void OnDraw(Canvas cv)
    {
        base.OnDraw(cv);
        if (!mActive || mDpad == null) return;
        int alpha = Math.Clamp(mCfg.TouchAlpha, 10, 100) * 255 / 100;
        var held = new HashSet<Ctl>(mPointers.Values);

        // cruceta: entera semitransparente y, encima, opaco solo el lado (o los lados) que se pulsan
        if (mImgDpad != null)
        {
            float d = mDpad.R * 2f;
            mDst.Set(mDpad.X - mDpad.R, mDpad.Y - mDpad.R, mDpad.X + mDpad.R, mDpad.Y + mDpad.R);
            mPaint.Alpha = alpha;
            cv.DrawBitmap(mImgDpad, null, mDst, mPaint);
            float a = 0.37f * d, l = mDst.Left, t = mDst.Top, r = mDst.Right, b = mDst.Bottom;
            mPaint.Alpha = 255;
            if (mUp) DrawPart(cv, l, t, r, t + a);
            if (mDown) DrawPart(cv, l, b - a, r, b);
            if (mLeft) DrawPart(cv, l, t, l + a, b);
            if (mRight) DrawPart(cv, r - a, t, r, b);
        }

        foreach (var b in mButtons)
        {
            if (b.Img == null) continue;
            bool on = held.Contains(b.Kind);
            float hw = b.W > 0 ? b.W / 2f : b.R, hh = b.H > 0 ? b.H / 2f : b.R;
            if (on) { hw *= 0.94f; hh *= 0.94f; } // se hunde un poco al pulsar
            mDst.Set(b.X - hw, b.Y - hh, b.X + hw, b.Y + hh);
            mPaint.Alpha = on ? 255 : alpha;
            cv.DrawBitmap(b.Img, null, mDst, mPaint);
        }
    }

    void DrawPart(Canvas cv, float l, float t, float r, float b)
    {
        cv.Save();
        cv.ClipRect(l, t, r, b);
        cv.DrawBitmap(mImgDpad, null, mDst, mPaint);
        cv.Restore();
    }
}
