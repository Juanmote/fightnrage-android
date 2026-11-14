using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Database;
using Android.Graphics;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Widget;
using FnrPort;

namespace FnrPort.Droid;

/// <summary>Launcher: copia los archivos de tu juego (GOG/Steam) al almacenamiento de la app y lo arranca.</summary>
[Activity(Label = "Fight'N Rage", MainLauncher = true, Theme = "@android:style/Theme.Black.NoTitleBar.Fullscreen",
    Icon = "@mipmap/ic_launcher", RoundIcon = "@mipmap/ic_launcher",
    ScreenOrientation = ScreenOrientation.SensorLandscape, ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
public partial class MainActivity : Activity
{
    const int ReqFolder = 1, ReqSave = 2;
    TextView mStatus;
    Settings mCfg;
    bool mBusy;
    LinearLayout mRoot;
    LauncherTheme T;

    string FilesDir_ => GetExternalFilesDir(null).AbsolutePath;
    string GameDir => System.IO.Path.Combine(FilesDir_, "game");
    string SavePath => Settings.SavePath(this, mCfg.Profile);

    protected override void OnCreate(Bundle savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Log.Start(this);
        mCfg = Settings.Load(this);
        T = new LauncherTheme(this);
        Window.AddFlags(WindowManagerFlags.Fullscreen);
        SetContentView(T.BuildFrame(out mRoot));
        Refresh();
    }

    /// <summary>Reconstruye la pantalla (conservando el elemento con el foco del mando).</summary>
    void Refresh()
    {
        int focusIdx = -1;
        var f = mRoot.FindFocus();
        if (f != null) focusIdx = mRoot.GetFocusables(FocusSearchDirection.Down).IndexOf(f);
        mRoot.RemoveAllViews();
        Build();
        var l = mRoot.GetFocusables(FocusSearchDirection.Down);
        if (focusIdx >= 0 && l.Count > 0) l[Math.Min(focusIdx, l.Count - 1)].RequestFocus();
        else if (l.Count > 0) l[0].RequestFocus();
    }

    void Build()
    {
        T.Header(mRoot);
        string problem = GameLauncher.Validate(GameDir);

        var game = T.Card(mRoot, L.T("Juego", "Game"));
        mStatus = T.Text(problem == null
            ? L.T("Juego instalado. ", "Game installed. ") + (File.Exists(SavePath) ? L.T("Hay partida guardada en este perfil.", "This profile has a saved game.") : L.T("Este perfil no tiene partida todavía.", "No saved game in this profile yet."))
            : L.T("Juego no instalado: pulsa «Elegir carpeta del juego» y selecciona la carpeta de Fight'N Rage (GOG o Steam), la que tiene FIGHT_N_RAGE.exe y data.",
                  "Game not installed: press \"Choose game folder\" and select your Fight'N Rage folder (GOG or Steam), the one with FIGHT_N_RAGE.exe and data."),
            15, problem == null ? LauncherTheme.OK : LauncherTheme.WARN);
        game.AddView(mStatus);
        if (problem == null && !mBusy) game.AddView(T.Button(L.T("▶  JUGAR", "▶  PLAY"), true, Play));
        game.AddView(T.Button(L.T("Perfil: ", "Profile: ") + Settings.ProfileNames[mCfg.Profile], false,
            () => { mCfg.Profile = (mCfg.Profile + 1) % 2; mCfg.Save(this); Refresh(); }, !mBusy));
        game.AddView(T.Text(mCfg.Profile == 0
            ? L.T("Principal: tu partida de siempre, para jugar desde cero y desbloquear las cosas en la tienda.",
                  "Main: your regular save, to play from scratch and unlock things in the shop.")
            : L.T("Todo desbloqueado: todos los personajes, trajes, modos, extras y consejos de la tienda conseguidos. Tiene su propia partida aparte y aquí no se consiguen logros.",
                  "Everything unlocked: every character, costume, mode, extra and tip in the shop already owned. It has its own separate save and doesn't earn achievements."),
            13, LauncherTheme.MUTED));
        game.AddView(T.Button(L.T("Elegir carpeta del juego", "Choose game folder"), false, PickFolder, !mBusy));
        game.AddView(T.Button(L.T("Importar partida de PC (data.sav) a este perfil", "Import PC save (data.sav) into this profile"), false, PickSave, !mBusy));

        var got = Achievements.Load(Settings.AchievementsPath(this));
        var ach = T.Card(mRoot, L.T("Logros", "Achievements"));
        ach.AddView(T.Button(L.T($"Ver logros ({got.Count}/{Achievements.All.Length})", $"View achievements ({got.Count}/{Achievements.All.Length})"), false,
            () => StartActivity(new Intent(this, typeof(AchievementsActivity)))));

        var video = T.Card(mRoot, L.T("Imagen y sonido", "Video and sound"));
        video.AddView(T.Button(L.T("Resolución: ", "Resolution: ") + (mCfg.Scale == 0 ? $"auto (x{Settings.AutoScale(this)})" : "x" + mCfg.Scale), false,
            () => { mCfg.Scale = (mCfg.Scale + 1) % 5; mCfg.Save(this); Refresh(); }));
        video.AddView(T.Button(L.T("Formato: ", "Aspect: ") + Settings.FormatNames[mCfg.Format], false,
            () => { mCfg.Format = (mCfg.Format + 1) % 3; mCfg.Save(this); Refresh(); }));
        video.AddView(T.Text(L.T("En el juego, Options → Video: «Full Screen» estira la imagen a toda la pantalla y «Windowed» la deja sin deformar (con bandas negras si hace falta). 4:3 y 8:7 recortan los laterales: se pierde un poco del marcador de los lados.",
            "In the game, Options → Video: \"Full Screen\" stretches the picture to the whole screen and \"Windowed\" keeps its shape (with black bars if needed). 4:3 and 8:7 crop the sides: a bit of the side HUD is lost."), 13, LauncherTheme.MUTED));
        video.AddView(T.Button(L.T("Sonido: ", "Sound: ") + (mCfg.Sound ? L.T("sí", "on") : L.T("no", "off")), false, () => { mCfg.Sound = !mCfg.Sound; mCfg.Save(this); Refresh(); }));
        video.AddView(T.Button(L.T("Vibración: ", "Vibration: ") + (mCfg.Vibration == 0 ? L.T("no", "off") : mCfg.Vibration + " %"), false,
            () => { mCfg.Vibration = mCfg.Vibration >= 100 ? 0 : mCfg.Vibration + 25 - mCfg.Vibration % 25; mCfg.Save(this); Refresh(); TestVibration(); }));
        video.AddView(T.Text(L.T("Golpes, caídas, explosiones y temblores. Vibra el mando si tiene motor; si no, la consola.",
            "Hits, falls, explosions and screen shakes. The gamepad vibrates if it has a motor; otherwise, the console."), 13, LauncherTheme.MUTED));
        video.AddView(T.Button(L.T("Música del launcher: ", "Launcher music: ") + (mCfg.Music ? L.T("sí", "on") : L.T("no", "off")), false,
            () => { mCfg.Music = !mCfg.Music; mCfg.Save(this); LauncherMusic.Update(this); Refresh(); }));

        var ctl = T.Card(mRoot, L.T("Controles", "Controls"));
        ctl.AddView(T.Text(L.T("Mando de serie (como en la Odin): Y golpe, B salto, A especial. En el juego, Settings → Configure Controls para cambiarlo (SELECT vuelve a configurar, START acepta). Los botones C y Z (Retroid Pocket Classic) también se pueden asignar ahí.",
            "Default gamepad layout (as on the Odin): Y attack, B jump, A special. In the game, Settings → Configure Controls to change it (SELECT reconfigures, START accepts). The C and Z buttons (Retroid Pocket Classic) can be assigned there too."), 13, LauncherTheme.MUTED));
        ctl.AddView(T.Button(L.T("Controles táctiles: ", "Touch controls: ") + (mCfg.TouchAuto ? L.T("AUTOMÁTICO", "AUTOMATIC") : L.T("DESACTIVADOS", "OFF")), false,
            () => { mCfg.TouchAuto = !mCfg.TouchAuto; mCfg.Save(this); Refresh(); }));
        ctl.AddView(T.Text(L.T("Automático: salen cuando no hay mando físico y desaparecen en cuanto usas uno. Si tocas la pantalla, vuelven.",
            "Automatic: they show up when there's no physical gamepad and hide as soon as you use one. Touch the screen to bring them back."), 13, LauncherTheme.MUTED));
        ctl.AddView(T.Button(L.T($"Opacidad de los botones: {mCfg.TouchAlpha} %", $"Button opacity: {mCfg.TouchAlpha} %"), false,
            () => { mCfg.TouchAlpha = mCfg.TouchAlpha >= 90 ? 15 : mCfg.TouchAlpha + 15; mCfg.Save(this); Refresh(); }));
        ctl.AddView(T.Button(L.T($"Tamaño de los botones: {mCfg.TouchSize} %", $"Button size: {mCfg.TouchSize} %"), false,
            () => { mCfg.TouchSize = mCfg.TouchSize >= 140 ? 70 : mCfg.TouchSize + 10; mCfg.Save(this); Refresh(); }));

        BuildNetCard();

        var help = T.Card(mRoot, L.T("Ayuda", "Help"));
        help.AddView(T.Text(L.T("Partida de PC (GOG): %USERPROFILE%\\AppData\\LocalLow\\sebagamesdev\\gog\\fightnrage\\savedata\\data.sav\nSi algo falla: «Guardar registro en Descargas» y pásame ese archivo.",
            "PC save (GOG): %USERPROFILE%\\AppData\\LocalLow\\sebagamesdev\\gog\\fightnrage\\savedata\\data.sav\nIf something goes wrong: \"Save log to Downloads\" and send me that file."), 13, LauncherTheme.MUTED));
        help.AddView(T.Button(L.T("Guardar registro en Descargas", "Save log to Downloads"), false, () => ExportLog(false)));
        help.AddView(T.Button(L.T("Compartir registro", "Share log"), false, () => ExportLog(true)));
    }

    void TestVibration()
    {
        if (mCfg.Vibration <= 0) return;
        Haptics.Init(this);
        Rumble.Strength = mCfg.Vibration / 100f;
        Haptics.Vibrate(90, 220);
    }

    partial void BuildNetCardImpl(ref bool done);
    void BuildNetCard() { bool done = false; BuildNetCardImpl(ref done); }

    void ExportLog(bool share)
    {
        mStatus.Text = L.T("Preparando registro...", "Preparing log...");
        Task.Run(() =>
        {
            try
            {
                string text = LogExport.Build(this);
                if (!share)
                {
                    string where = LogExport.SaveToDownloads(this, text);
                    RunOnUiThread(() => { mStatus.Text = L.T("Registro guardado en ", "Log saved to ") + where; Toast.MakeText(this, L.T("Guardado en ", "Saved to ") + where, ToastLength.Long).Show(); });
                }
                else
                {
                    if (text.Length > 400_000) text = text.Substring(text.Length - 400_000);
                    RunOnUiThread(() =>
                    {
                        var i = new Intent(Intent.ActionSend);
                        i.SetType("text/plain");
                        i.PutExtra(Intent.ExtraSubject, "Registro Fight'N Rage Android");
                        i.PutExtra(Intent.ExtraText, text);
                        StartActivity(Intent.CreateChooser(i, L.T("Compartir registro", "Share log")));
                        Refresh();
                    });
                }
            }
            catch (Exception ex) { RunOnUiThread(() => mStatus.Text = L.T("No se pudo exportar el registro: ", "Could not export the log: ") + ex.Message); }
        });
    }

    void Play() => StartActivity(new Intent(this, typeof(GameActivity)));

    void PickFolder()
    {
        var i = new Intent(Intent.ActionOpenDocumentTree);
        i.AddFlags(ActivityFlags.GrantReadUriPermission);
        StartActivityForResult(i, ReqFolder);
    }

    void PickSave()
    {
        var i = new Intent(Intent.ActionOpenDocument);
        i.AddCategory(Intent.CategoryOpenable);
        i.SetType("*/*");
        StartActivityForResult(i, ReqSave);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (resultCode != Result.Ok || data?.Data == null) return;
        if (requestCode == ReqFolder) CopyGame(data.Data);
        else if (requestCode == ReqSave) ImportSave(data.Data);
    }

    void ImportSave(Android.Net.Uri uri)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SavePath));
            if (File.Exists(SavePath)) File.Copy(SavePath, SavePath + ".bak", true);
            using (var input = ContentResolver.OpenInputStream(uri))
            using (var output = File.Create(SavePath)) input.CopyTo(output);
            Toast.MakeText(this, L.T("Partida importada (la anterior queda como data.sav.bak)", "Save imported (the previous one is kept as data.sav.bak)"), ToastLength.Long).Show();
        }
        catch (Exception ex) { Toast.MakeText(this, L.T("No se pudo importar: ", "Could not import: ") + ex.Message, ToastLength.Long).Show(); }
        Refresh();
    }

    // ------------------------------------------------------------ copia de la carpeta (SAF)

    record Entry(string DocId, string Name, bool IsDir, long Size);

    List<Entry> ListChildren(Android.Net.Uri tree, string docId)
    {
        var res = new List<Entry>();
        var uri = DocumentsContract.BuildChildDocumentsUriUsingTree(tree, docId);
        string[] cols = { DocumentsContract.Document.ColumnDocumentId, DocumentsContract.Document.ColumnDisplayName,
            DocumentsContract.Document.ColumnMimeType, DocumentsContract.Document.ColumnSize };
        using ICursor c = ContentResolver.Query(uri, cols, null, null, null);
        while (c != null && c.MoveToNext())
            res.Add(new Entry(c.GetString(0), c.GetString(1), c.GetString(2) == DocumentsContract.Document.MimeTypeDir, c.IsNull(3) ? -1 : c.GetLong(3)));
        return res;
    }

    void CopyGame(Android.Net.Uri tree)
    {
        mBusy = true; Refresh();
        mStatus.Text = L.T("Buscando FIGHT_N_RAGE.exe...", "Looking for FIGHT_N_RAGE.exe...");
        Task.Run(() =>
        {
            try
            {
                string rootId = DocumentsContract.GetTreeDocumentId(tree);
                // la carpeta elegida o una subcarpeta (hasta 2 niveles) que contenga el exe
                string gameId = FindGameDir(tree, rootId, 0) ?? throw new Exception(L.T("No encuentro FIGHT_N_RAGE.exe en esa carpeta.", "FIGHT_N_RAGE.exe is not in that folder."));
                var top = ListChildren(tree, gameId);
                var dataDir = top.Find(e => e.IsDir && e.Name.Equals("data", StringComparison.OrdinalIgnoreCase))
                    ?? throw new Exception(L.T("Falta la carpeta data junto al exe.", "The data folder is missing next to the exe."));
                var dataFiles = ListChildren(tree, dataDir.DocId).FindAll(e => !e.IsDir);

                string tmp = GameDir + ".tmp";
                if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
                Directory.CreateDirectory(System.IO.Path.Combine(tmp, "data"));
                var extras = top.FindAll(e => !e.IsDir && (e.Name == GameLauncher.ExeName || e.Name == "gamecontrollerdb.txt"));
                int total = dataFiles.Count + extras.Count, done = 0;
                foreach (var f in extras) { CopyDoc(tree, f.DocId, System.IO.Path.Combine(tmp, f.Name)); done++; }
                foreach (var f in dataFiles)
                {
                    CopyDoc(tree, f.DocId, System.IO.Path.Combine(tmp, "data", f.Name));
                    if (++done % 25 == 0) { int d = done; RunOnUiThread(() => { if (mStatus != null) mStatus.Text = L.T("Copiando archivos del juego... ", "Copying game files... ") + $"{d}/{total}"; }); }
                }
                string problem = GameLauncher.Validate(tmp);
                if (problem != null) throw new Exception(problem);
                if (Directory.Exists(GameDir)) Directory.Delete(GameDir, true);
                Directory.Move(tmp, GameDir);
                RunOnUiThread(() => { mBusy = false; Refresh(); Toast.MakeText(this, L.T("Juego copiado. ¡Listo para jugar!", "Game copied. Ready to play!"), ToastLength.Long).Show(); });
            }
            catch (Exception ex)
            {
                Log.Write("Copia fallida: " + ex);
                RunOnUiThread(() => { mBusy = false; Refresh(); mStatus.Text = "Error: " + ex.Message; mStatus.SetTextColor(new Color(unchecked((int)LauncherTheme.WARN))); });
            }
        });
    }

    string FindGameDir(Android.Net.Uri tree, string docId, int depth)
    {
        var children = ListChildren(tree, docId);
        if (children.Exists(e => !e.IsDir && e.Name.Equals(GameLauncher.ExeName, StringComparison.OrdinalIgnoreCase))) return docId;
        if (depth >= 2) return null;
        foreach (var d in children)
            if (d.IsDir && !d.Name.Equals("data", StringComparison.OrdinalIgnoreCase))
            {
                var r = FindGameDir(tree, d.DocId, depth + 1);
                if (r != null) return r;
            }
        return null;
    }

    void CopyDoc(Android.Net.Uri tree, string docId, string dst)
    {
        var uri = DocumentsContract.BuildDocumentUriUsingTree(tree, docId);
        using var input = ContentResolver.OpenInputStream(uri);
        using var output = File.Create(dst);
        input.CopyTo(output, 1 << 16);
    }

    protected override void OnResume() { base.OnResume(); if (mStatus != null) Refresh(); }
    protected override void OnStart() { base.OnStart(); LauncherMusic.Shown(this); }
    protected override void OnStop() { base.OnStop(); LauncherMusic.Hidden(this); }
}
