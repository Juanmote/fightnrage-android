using System;
using Android.App;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace FnrPort.Droid;

/// <summary>
/// Pestaña de logros (como en Demons With Shotguns): los 20 logros de Steam con su icono, guardados en
/// la consola con la fecha. No se sincronizan con Steam. Los de una partida importada de PC aparecen
/// la primera vez que se juega con ella (el juego los vuelve a anunciar al cargarla).
/// </summary>
[Activity(Label = "Fight'N Rage", Theme = "@android:style/Theme.Black.NoTitleBar.Fullscreen",
    ScreenOrientation = ScreenOrientation.SensorLandscape, ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
public class AchievementsActivity : Activity
{
    protected override void OnCreate(Bundle savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Window.AddFlags(WindowManagerFlags.Fullscreen);
        var T = new LauncherTheme(this);
        SetContentView(T.BuildFrame(out var root));
        T.Header(root);
        var got = FnrPort.Achievements.Load(Settings.AchievementsPath(this));
        var card = T.Card(root, L.T($"Logros  {got.Count}/{FnrPort.Achievements.All.Length}", $"Achievements  {got.Count}/{FnrPort.Achievements.All.Length}"));
        card.AddView(T.Text(L.T("Los de Steam, guardados en esta consola (no se sincronizan con Steam). Si importas tu partida de PC, los que ya tenías aparecen al jugar con ella. En el perfil «Todo desbloqueado» no se consiguen logros.",
            "The Steam ones, stored on this console (not synced with Steam). If you import your PC save, the ones you already had show up once you play with it. Achievements can't be earned in the \"Everything unlocked\" profile."), 13, LauncherTheme.MUTED));
        View first = null;
        // primero los conseguidos (por fecha), luego el resto en el orden de siempre
        var ids = new System.Collections.Generic.List<string>(FnrPort.Achievements.All);
        ids.Sort((a, b) =>
        {
            bool ga = got.TryGetValue(a, out var da), gb = got.TryGetValue(b, out var db);
            if (ga != gb) return ga ? -1 : 1;
            if (ga) return db.CompareTo(da);
            return Array.IndexOf(FnrPort.Achievements.All, a).CompareTo(Array.IndexOf(FnrPort.Achievements.All, b));
        });
        foreach (var id in ids)
        {
            bool ok = got.TryGetValue(id, out var when);
            var row = Row(T, id, ok, when);
            card.AddView(row);
            first ??= row;
        }
        card.AddView(T.Button(L.T("Volver", "Back"), false, Finish));
        first?.RequestFocus();
    }

    View Row(LauncherTheme T, string id, bool ok, DateTime when)
    {
        var info = AchievementInfo.Get(this, id);
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal, Focusable = true, Clickable = true };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetPadding(T.Dp(8), T.Dp(6), T.Dp(8), T.Dp(6));
        var n = new GradientDrawable(); n.SetColor(new Color(unchecked((int)(ok ? 0xFF2A2030 : 0xFF17121B)))); n.SetCornerRadius(T.Dp(6));
        var f = new GradientDrawable(); f.SetColor(new Color(unchecked((int)LauncherTheme.BTN_FOCUS))); f.SetCornerRadius(T.Dp(6)); f.SetStroke(T.Dp(3), new Color(unchecked((int)LauncherTheme.FIRE_YELLOW)));
        var bg = new StateListDrawable();
        bg.AddState(new[] { Android.Resource.Attribute.StateFocused }, f);
        bg.AddState(Array.Empty<int>(), n);
        row.Background = bg;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O) row.DefaultFocusHighlightEnabled = false;

        var icon = new ImageView(this);
        var bmp = AchievementInfo.Icon(this, id, ok);
        if (bmp != null) icon.SetImageBitmap(bmp);
        else icon.SetImageResource(ok ? Android.Resource.Drawable.StarBigOn : Android.Resource.Drawable.StarBigOff);
        if (!ok) icon.Alpha = 0.55f;
        row.AddView(icon, new LinearLayout.LayoutParams(T.Dp(52), T.Dp(52)) { RightMargin = T.Dp(12) });

        var col = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var name = T.Text(info.Name, 18, ok ? LauncherTheme.FIRE_YELLOW : LauncherTheme.TEXT);
        name.Typeface = T.FontTitle; name.LetterSpacing = 0.03f;
        col.AddView(name);
        if (!string.IsNullOrEmpty(info.Desc)) col.AddView(T.Text(info.Desc, 14, LauncherTheme.MUTED));
        col.AddView(T.Text(ok
            ? (when > DateTime.MinValue ? L.T("Conseguido el ", "Unlocked on ") + when.ToString(L.ES ? "dd/MM/yyyy HH:mm" : "yyyy-MM-dd HH:mm") : L.T("Conseguido", "Unlocked"))
            : L.T("Sin conseguir", "Locked"), 13, ok ? LauncherTheme.OK : LauncherTheme.MUTED));
        row.AddView(col, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
        var lp = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = T.Dp(6) };
        row.LayoutParameters = lp;
        return row;
    }

    protected override void OnStart() { base.OnStart(); LauncherMusic.Shown(this); }
    protected override void OnStop() { base.OnStop(); LauncherMusic.Hidden(this); }
}
