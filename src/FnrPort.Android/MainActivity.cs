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
    string SavePath => System.IO.Path.Combine(FilesDir_, "savedata", "data.sav");

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

        var game = T.Card(mRoot, "Juego");
        mStatus = T.Text(problem == null
            ? "Juego instalado. " + (File.Exists(SavePath) ? "Hay partida guardada." : "Sin partida guardada todavía.")
            : "Juego no instalado: pulsa «Elegir carpeta del juego» y selecciona la carpeta de Fight'N Rage (GOG o Steam), la que tiene FIGHT_N_RAGE.exe y data.",
            15, problem == null ? LauncherTheme.OK : LauncherTheme.WARN);
        game.AddView(mStatus);
        if (problem == null && !mBusy) game.AddView(T.Button("▶  JUGAR", true, Play));
        game.AddView(T.Button("Elegir carpeta del juego", false, PickFolder, !mBusy));
        game.AddView(T.Button("Importar partida de PC (data.sav)", false, PickSave, !mBusy));

        var video = T.Card(mRoot, "Imagen y sonido");
        video.AddView(T.Button("Resolución: " + (mCfg.Scale == 0 ? $"auto (x{Settings.AutoScale(this)})" : "x" + mCfg.Scale), false,
            () => { mCfg.Scale = (mCfg.Scale + 1) % 5; mCfg.Save(this); Refresh(); }));
        video.AddView(T.Button("Formato: " + Settings.FormatNames[mCfg.Format], false,
            () => { mCfg.Format = (mCfg.Format + 1) % 3; mCfg.Save(this); Refresh(); }));
        video.AddView(T.Text("En el juego, Options → Video: «Full Screen» estira la imagen a toda la pantalla y «Windowed» la deja sin deformar (con bandas negras si hace falta). " +
            "4:3 y 8:7 recortan los laterales: se pierde un poco del marcador de los lados.", 13, LauncherTheme.MUTED));
        video.AddView(T.Button("Sonido: " + (mCfg.Sound ? "sí" : "no"), false, () => { mCfg.Sound = !mCfg.Sound; mCfg.Save(this); Refresh(); }));

        var ctl = T.Card(mRoot, "Controles");
        ctl.AddView(T.Button("Controles táctiles: " + (mCfg.TouchAuto ? "AUTOMÁTICO" : "DESACTIVADOS"), false,
            () => { mCfg.TouchAuto = !mCfg.TouchAuto; mCfg.Save(this); Refresh(); }));
        ctl.AddView(T.Text("Automático: salen cuando no hay mando físico y desaparecen en cuanto usas uno. Si tocas la pantalla, vuelven.", 13, LauncherTheme.MUTED));
        ctl.AddView(T.Button($"Opacidad de los botones: {mCfg.TouchAlpha} %", false,
            () => { mCfg.TouchAlpha = mCfg.TouchAlpha >= 90 ? 15 : mCfg.TouchAlpha + 15; mCfg.Save(this); Refresh(); }));
        ctl.AddView(T.Button($"Tamaño de los botones: {mCfg.TouchSize} %", false,
            () => { mCfg.TouchSize = mCfg.TouchSize >= 140 ? 70 : mCfg.TouchSize + 10; mCfg.Save(this); Refresh(); }));

        BuildNetCard();

        var help = T.Card(mRoot, "Ayuda");
        help.AddView(T.Text("Partida de PC (GOG): %USERPROFILE%\\AppData\\LocalLow\\sebagamesdev\\gog\\fightnrage\\savedata\\data.sav\n" +
            "Si algo falla: «Guardar registro en Descargas» y pásame ese archivo.", 13, LauncherTheme.MUTED));
        help.AddView(T.Button("Guardar registro en Descargas", false, () => ExportLog(false)));
        help.AddView(T.Button("Compartir registro", false, () => ExportLog(true)));
    }

    partial void BuildNetCardImpl(ref bool done);
    void BuildNetCard() { bool done = false; BuildNetCardImpl(ref done); }

    void ExportLog(bool share)
    {
        mStatus.Text = "Preparando registro...";
        Task.Run(() =>
        {
            try
            {
                string text = LogExport.Build(this);
                if (!share)
                {
                    string where = LogExport.SaveToDownloads(this, text);
                    RunOnUiThread(() => { mStatus.Text = "Registro guardado en " + where; Toast.MakeText(this, "Guardado en " + where, ToastLength.Long).Show(); });
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
                        StartActivity(Intent.CreateChooser(i, "Compartir registro"));
                        Refresh();
                    });
                }
            }
            catch (Exception ex) { RunOnUiThread(() => mStatus.Text = "No se pudo exportar el registro: " + ex.Message); }
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
            Toast.MakeText(this, "Partida importada (la anterior queda como data.sav.bak)", ToastLength.Long).Show();
        }
        catch (Exception ex) { Toast.MakeText(this, "No se pudo importar: " + ex.Message, ToastLength.Long).Show(); }
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
        mStatus.Text = "Buscando FIGHT_N_RAGE.exe...";
        Task.Run(() =>
        {
            try
            {
                string rootId = DocumentsContract.GetTreeDocumentId(tree);
                // la carpeta elegida o una subcarpeta (hasta 2 niveles) que contenga el exe
                string gameId = FindGameDir(tree, rootId, 0) ?? throw new Exception("No encuentro FIGHT_N_RAGE.exe en esa carpeta.");
                var top = ListChildren(tree, gameId);
                var dataDir = top.Find(e => e.IsDir && e.Name.Equals("data", StringComparison.OrdinalIgnoreCase))
                    ?? throw new Exception("Falta la carpeta data junto al exe.");
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
                    if (++done % 25 == 0) { int d = done; RunOnUiThread(() => { if (mStatus != null) mStatus.Text = $"Copiando archivos del juego... {d}/{total}"; }); }
                }
                string problem = GameLauncher.Validate(tmp);
                if (problem != null) throw new Exception(problem);
                if (Directory.Exists(GameDir)) Directory.Delete(GameDir, true);
                Directory.Move(tmp, GameDir);
                RunOnUiThread(() => { mBusy = false; Refresh(); Toast.MakeText(this, "Juego copiado. ¡Listo para jugar!", ToastLength.Long).Show(); });
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
}
