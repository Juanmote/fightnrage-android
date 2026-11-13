using System;
using System.Collections.Generic;
using Android.Content;
using Android.Graphics;
using Android.Hardware.Input;
using Android.Views;

namespace FnrPort.Droid;

/// <summary>
/// Controles táctiles provisionales (el arte vendrá después): cruceta a la izquierda; Golpe, Salto y
/// Especial a la derecha; Start y Back arriba. Funcionan como un mando más (botones X, A, B, Start y
/// Back del mando tipo Xbox, que es lo que el juego espera por defecto). Se ocultan solos cuando se usa
/// un mando físico y vuelven al tocar la pantalla.
/// </summary>
sealed class TouchOverlay : View, InputManager.IInputDeviceListener
{
    public const int DeviceId = -4242;
    readonly AndroidHost mHost;
    readonly Settings mCfg;
    readonly InputManager mIm;
    bool mActive;          // los controles están a la vista y cuentan como mando
    bool mPadSeen;         // se ha usado un mando físico desde la última vez que se tocó la pantalla

    enum Ctl { None, Dpad, Attack, Jump, Special, Start, Back }
    sealed class Btn { public Ctl Kind; public float X, Y, R; public string Label; public uint Color; }
    readonly List<Btn> mButtons = new();
    Btn mDpad;
    readonly Dictionary<int, Ctl> mPointers = new();       // dedo -> control
    readonly Dictionary<int, (float x, float y)> mPos = new();
    readonly Paint mFill = new(PaintFlags.AntiAlias), mStroke = new(PaintFlags.AntiAlias), mText = new(PaintFlags.AntiAlias);

    public TouchOverlay(Context c, AndroidHost host, Settings cfg) : base(c)
    {
        mHost = host; mCfg = cfg;
        mStroke.SetStyle(Paint.Style.Stroke);
        mText.TextAlign = Paint.Align.Center;
        mText.FakeBoldText = true;
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
        mDpad = new Btn { Kind = Ctl.Dpad, X = 22 * u + 2 * u, Y = h - 24 * u, R = 19 * u };
        float bx = w - 16 * u, by = h - 20 * u, br = 9 * u;
        mButtons.Add(new Btn { Kind = Ctl.Attack, X = bx - 22 * u, Y = by, R = br, Label = "GOLPE", Color = 0xFFE3261B });
        mButtons.Add(new Btn { Kind = Ctl.Jump, X = bx, Y = by - 14 * u, R = br, Label = "SALTO", Color = 0xFF4CD137 });
        mButtons.Add(new Btn { Kind = Ctl.Special, X = bx - 22 * u, Y = by - 24 * u, R = br, Label = "ESP.", Color = 0xFFFFD52E });
        mButtons.Add(new Btn { Kind = Ctl.Back, X = w / 2f - 12 * u, Y = 7 * u, R = 5 * u, Label = "BACK", Color = 0xFFB8A9B4 });
        mButtons.Add(new Btn { Kind = Ctl.Start, X = w / 2f + 12 * u, Y = 7 * u, R = 5 * u, Label = "START", Color = 0xFFB8A9B4 });
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
            p.X = atk; p.A = jump; p.B = sp; p.Start = start; p.Back = back;
            return p;
        });
    }

    protected override void OnDraw(Canvas cv)
    {
        base.OnDraw(cv);
        if (!mActive || mDpad == null) return;
        int alpha = Math.Clamp(mCfg.TouchAlpha, 10, 100) * 255 / 100;
        var held = new HashSet<Ctl>(mPointers.Values);

        // cruceta
        mFill.Color = new Color(0, 0, 0, alpha * 2 / 3);
        cv.DrawCircle(mDpad.X, mDpad.Y, mDpad.R, mFill);
        mStroke.StrokeWidth = mDpad.R * 0.05f;
        mStroke.Color = new Color(255, 255, 255, alpha);
        cv.DrawCircle(mDpad.X, mDpad.Y, mDpad.R, mStroke);
        float a = mDpad.R * 0.55f, t = mDpad.R * 0.22f;
        DrawArrow(cv, mDpad.X, mDpad.Y - a, 0, -1, t, mUp, alpha);
        DrawArrow(cv, mDpad.X, mDpad.Y + a, 0, 1, t, mDown, alpha);
        DrawArrow(cv, mDpad.X - a, mDpad.Y, -1, 0, t, mLeft, alpha);
        DrawArrow(cv, mDpad.X + a, mDpad.Y, 1, 0, t, mRight, alpha);

        foreach (var b in mButtons)
        {
            bool on = held.Contains(b.Kind);
            var col = new Color(unchecked((int)b.Color));
            mFill.Color = new Color(col.R, col.G, col.B, on ? Math.Min(255, alpha + 90) : alpha * 2 / 3);
            cv.DrawCircle(b.X, b.Y, b.R, mFill);
            mStroke.StrokeWidth = b.R * 0.08f;
            mStroke.Color = new Color(255, 255, 255, alpha);
            cv.DrawCircle(b.X, b.Y, b.R, mStroke);
            mText.TextSize = b.R * (b.Label.Length > 4 ? 0.42f : 0.55f);
            mText.Color = new Color(255, 255, 255, Math.Min(255, alpha + 60));
            mText.SetShadowLayer(b.R * 0.08f, 0, 0, Color.Black);
            cv.DrawText(b.Label, b.X, b.Y + mText.TextSize * 0.35f, mText);
        }
    }

    void DrawArrow(Canvas cv, float x, float y, int dx, int dy, float s, bool on, int alpha)
    {
        using var path = new Path();
        // triángulo que apunta en (dx, dy)
        float px = -dy, py = dx;
        path.MoveTo(x + dx * s, y + dy * s);
        path.LineTo(x - dx * s * 0.6f + px * s, y - dy * s * 0.6f + py * s);
        path.LineTo(x - dx * s * 0.6f - px * s, y - dy * s * 0.6f - py * s);
        path.Close();
        mFill.Color = on ? new Color(255, 213, 46, Math.Min(255, alpha + 90)) : new Color(255, 255, 255, alpha);
        cv.DrawPath(path, mFill);
    }
}
