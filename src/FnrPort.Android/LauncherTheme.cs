using System;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Util;
using Android.Views;
using Android.Widget;

namespace FnrPort.Droid;

/// <summary>
/// Estilo del launcher (mismo planteamiento que el port de Streets of Rage Remake): ilustración a la
/// derecha fundida con el fondo, columna de opciones a la izquierda, títulos con el degradado de fuego
/// del logo, el verde del contorno del logo como acento y foco del mando muy visible.
/// </summary>
sealed class LauncherTheme
{
    // Colores sacados del logo y la portada: fuego amarillo/naranja/rojo, contorno verde y fondo morado oscuro.
    public const uint BG = 0xFF0A0710, BG_TOP = 0xFF24102C, CARD = 0xE0130E18, CARD_EDGE = 0x6654D63A;
    public const uint FIRE_RED = 0xFFE3261B, FIRE_ORANGE = 0xFFFF7A18, FIRE_YELLOW = 0xFFFFD52E, GREEN = 0xFF4CD137;
    public const uint BTN = 0xFF221A26, BTN_FOCUS = 0xFF3A2440, TEXT = 0xFFF6EEE8, MUTED = 0xFFB8A9B4, WARN = 0xFFFFB74D, OK = 0xFF9CCC65;
    const float Column = 0.56f;

    readonly Activity mA;
    public readonly Typeface FontTitle, FontBody;

    public LauncherTheme(Activity a)
    {
        mA = a;
        try { FontTitle = Typeface.CreateFromAsset(a.Assets, "fonts/Bangers-Regular.ttf"); } catch { FontTitle = Typeface.DefaultBold; }
        try { FontBody = Typeface.CreateFromAsset(a.Assets, "fonts/BarlowCondensed-Medium.ttf"); } catch { FontBody = Typeface.Default; }
    }

    static Color C(uint argb) => new Color(unchecked((int)argb));
    public int Dp(float v) => (int)TypedValue.ApplyDimension(ComplexUnitType.Dip, v, mA.Resources.DisplayMetrics);
    static bool IsWide(int w, int h) => w >= h * 1.5f;

    public View BuildFrame(out LinearLayout root)
    {
        var frame = new FrameLayout(mA);
        frame.AddView(new ArtView(mA), new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        var sv = new ScrollView(mA);
        root = new LinearLayout(mA) { Orientation = Orientation.Vertical };
        int pad = Dp(20);
        root.SetPadding(pad, pad, pad, pad);
        sv.AddView(root);
        if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O)
        {
            sv.DefaultFocusHighlightEnabled = false; root.DefaultFocusHighlightEnabled = false; frame.DefaultFocusHighlightEnabled = false;
        }
        sv.Focusable = false;
        var dm = mA.Resources.DisplayMetrics;
        int sw = Math.Max(dm.WidthPixels, dm.HeightPixels), sh = Math.Min(dm.WidthPixels, dm.HeightPixels);
        int colW = IsWide(sw, sh) ? (int)(sw * Column) : ViewGroup.LayoutParams.MatchParent;
        frame.AddView(sv, new FrameLayout.LayoutParams(colW, ViewGroup.LayoutParams.MatchParent));
        return frame;
    }

    public void Header(LinearLayout root)
    {
        var title = Text("FIGHT'N RAGE", 40, TEXT);
        title.Typeface = FontTitle;
        title.LetterSpacing = 0.05f;
        Fire(title);
        root.AddView(title);
        var sub = Text("PORT ANDROID  ·  TU COPIA DE GOG O STEAM", 19, TEXT);
        sub.Typeface = FontTitle;
        sub.LetterSpacing = 0.06f;
        Fire(sub);
        root.AddView(sub);
        var by = Text("  PORTED BY JUANMOTE  ", 15, 0xFF0A0710);
        by.Typeface = FontTitle;
        by.LetterSpacing = 0.08f;
        var pill = new GradientDrawable();
        pill.SetColor(C(GREEN));
        pill.SetCornerRadius(Dp(4));
        pill.SetStroke(Dp(2), Color.Black);
        by.Background = pill;
        by.SetPadding(Dp(8), Dp(2), Dp(8), Dp(2));
        var lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { TopMargin = Dp(6), BottomMargin = Dp(14) };
        root.AddView(by, lp);
    }

    public LinearLayout Card(LinearLayout root, string title)
    {
        var c = new LinearLayout(mA) { Orientation = Orientation.Vertical };
        int p = Dp(14);
        c.SetPadding(p, Dp(10), p, p);
        var bg = new GradientDrawable();
        bg.SetColor(C(CARD));
        bg.SetCornerRadius(Dp(6));
        bg.SetStroke(Dp(1), C(CARD_EDGE));
        c.Background = bg;
        var t = Text(title.ToUpperInvariant(), 24, TEXT);
        t.Typeface = FontTitle;
        t.LetterSpacing = 0.05f;
        Fire(t);
        c.AddView(t);
        var lp = Match(); lp.BottomMargin = Dp(12);
        root.AddView(c, lp);
        return c;
    }

    /// <summary>Texto con el degradado de fuego del logo (amarillo arriba, rojo abajo) y sombra oscura.</summary>
    public void Fire(TextView t)
    {
        t.SetShadowLayer(Dp(2), 0, Dp(1), C(0xE0000000));
        t.LayoutChange += (s, e) =>
        {
            int h = e.Bottom - e.Top;
            if (h <= 0) return;
            t.Paint.SetShader(new LinearGradient(0, t.PaddingTop, 0, h - t.PaddingBottom,
                new int[] { unchecked((int)FIRE_YELLOW), unchecked((int)FIRE_ORANGE), unchecked((int)FIRE_RED) },
                new float[] { 0.1f, 0.55f, 1f }, Shader.TileMode.Clamp));
        };
    }

    public TextView Text(string s, float sp, uint color)
    {
        var t = new TextView(mA) { Text = s };
        t.SetTextSize(ComplexUnitType.Sp, sp);
        t.SetTextColor(C(color));
        t.Typeface = FontBody;
        t.SetPadding(0, Dp(2), 0, Dp(2));
        return t;
    }

    public Button Button(string label, bool primary, Action click, bool enabled = true)
    {
        var b = new Button(mA) { Text = label, Enabled = enabled };
        b.SetAllCaps(false);
        b.SetTextColor(primary ? Color.White : C(TEXT));
        b.Typeface = primary ? FontTitle : FontBody;
        b.SetTextSize(ComplexUnitType.Sp, primary ? 24 : 17);
        if (primary) { b.LetterSpacing = 0.05f; b.SetShadowLayer(Dp(2), 0, Dp(1), C(0xC0000000)); }
        if (!enabled) b.Alpha = 0.45f;
        b.StateListAnimator = null;
        if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O) b.DefaultFocusHighlightEnabled = false;
        // normal / pulsado / con foco (mando): el del foco lleva un marco amarillo grueso
        GradientDrawable n, pr, fo;
        if (primary)
        {
            var tb = GradientDrawable.Orientation.TopBottom;
            n = new GradientDrawable(tb, new[] { unchecked((int)FIRE_ORANGE), unchecked((int)FIRE_RED) });
            pr = new GradientDrawable(tb, new[] { unchecked((int)FIRE_YELLOW), unchecked((int)FIRE_ORANGE) });
            fo = new GradientDrawable(tb, new[] { unchecked((int)0xFFFF9A3A), unchecked((int)FIRE_RED) });
            n.SetStroke(Dp(2), C(0x66000000));
        }
        else
        {
            n = new GradientDrawable(); pr = new GradientDrawable(); fo = new GradientDrawable();
            n.SetColor(C(BTN)); n.SetStroke(Dp(1), C(0x33FFFFFF));
            pr.SetColor(C(0xFF4A2A40)); fo.SetColor(C(BTN_FOCUS));
        }
        pr.SetStroke(Dp(3), C(FIRE_YELLOW));
        fo.SetStroke(Dp(3), C(FIRE_YELLOW));
        foreach (var g in new[] { n, pr, fo }) g.SetCornerRadius(Dp(6));
        var bg = new StateListDrawable();
        bg.AddState(new[] { Android.Resource.Attribute.StatePressed }, pr);
        bg.AddState(new[] { Android.Resource.Attribute.StateFocused }, fo);
        bg.AddState(Array.Empty<int>(), n);
        b.Background = bg;
        b.Click += (s, e) => click();
        b.SetPadding(Dp(12), Dp(primary ? 12 : 10), Dp(12), Dp(primary ? 12 : 10));
        b.LayoutParameters = Match();
        return b;
    }

    public LinearLayout.LayoutParams Match() => new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = Dp(6) };

    /// <summary>Fondo: degradado morado y la ilustración a la derecha (en pantallas casi cuadradas, detrás y oscurecida).</summary>
    sealed class ArtView : View
    {
        readonly Bitmap mArt;
        readonly Paint mPaint = new(PaintFlags.FilterBitmap | PaintFlags.AntiAlias);
        readonly Paint mScrim = new();
        readonly RectF mDst = new();

        public ArtView(Context c) : base(c)
        {
            try { mArt = BitmapFactory.DecodeResource(c.Resources, Resource.Drawable.launcher_art); } catch { }
        }

        protected override void OnDraw(Canvas cv)
        {
            int w = Width, h = Height;
            mScrim.SetShader(new LinearGradient(0, 0, 0, h, C(BG_TOP), C(BG), Shader.TileMode.Clamp));
            cv.DrawRect(0, 0, w, h, mScrim);
            mScrim.SetShader(null);
            if (mArt == null || w == 0 || h == 0) return;
            bool wide = IsWide(w, h);
            // La ilustración es apaisada (4:3): se ajusta a la altura y se pega a la derecha.
            float aw = h * mArt.Width / (float)mArt.Height;
            float left = wide ? w - aw : (w - aw) / 2f;
            mDst.Set(left, 0, left + aw, h);
            if (!wide) { mPaint.Alpha = 110; cv.DrawBitmap(mArt, null, mDst, mPaint); return; }
            mPaint.Alpha = 255;
            float edge = w * Column + h * 0.10f;
            int layer = cv.SaveLayer(mDst, null);
            cv.DrawBitmap(mArt, null, mDst, mPaint);
            if (left < edge)
            {
                mScrim.SetShader(new LinearGradient(left, 0, edge, 0, C(0x00000000), C(0xFF000000), Shader.TileMode.Clamp));
                mScrim.SetXfermode(new PorterDuffXfermode(PorterDuff.Mode.DstIn));
                cv.DrawRect(mDst, mScrim);
                mScrim.SetXfermode(null);
                mScrim.SetShader(null);
            }
            cv.RestoreToCount(layer);
        }
    }
}
