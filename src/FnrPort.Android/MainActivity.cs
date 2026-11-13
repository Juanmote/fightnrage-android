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
[Activity(Label = "Fight'N Rage", MainLauncher = true, Theme = "@android:style/Theme.Material.NoActionBar",
    ScreenOrientation = ScreenOrientation.SensorLandscape, ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
public class MainActivity : Activity
{
    const int ReqFolder = 1, ReqSave = 2;
    TextView mStatus;
    Button mPlay, mPick, mSave, mScale, mSound;
    Settings mCfg;
    bool mBusy;

    string FilesDir_ => GetExternalFilesDir(null).AbsolutePath;
    string GameDir => System.IO.Path.Combine(FilesDir_, "game");
    string SavePath => System.IO.Path.Combine(FilesDir_, "savedata", "data.sav");

    protected override void OnCreate(Bundle savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Log.Start(this);
        mCfg = Settings.Load(this);

        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetPadding(48, 32, 48, 32);
        root.SetBackgroundColor(Color.Rgb(20, 12, 12));
        var title = new TextView(this) { Text = "FIGHT'N RAGE", TextSize = 30 };
        title.SetTextColor(Color.Rgb(255, 170, 40));
        title.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Bold);
        root.AddView(title);
        var sub = new TextView(this) { Text = "Launcher no oficial para Android. Necesitas tu copia del juego de PC (GOG o Steam): la carpeta que contiene FIGHT_N_RAGE.exe y data.", TextSize = 14 };
        sub.SetTextColor(Color.LightGray);
        root.AddView(sub);
        mStatus = new TextView(this) { TextSize = 15 };
        mStatus.SetTextColor(Color.White);
        mStatus.SetPadding(0, 24, 0, 24);
        root.AddView(mStatus);

        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        mPlay = AddButton(row, "▶ JUGAR", (s, e) => Play());
        mPick = AddButton(row, "Elegir carpeta del juego", (s, e) => PickFolder());
        mSave = AddButton(row, "Importar partida de PC", (s, e) => PickSave());
        mScale = AddButton(row, "", (s, e) => CycleScale());
        root.AddView(row);
        var row2 = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row2.SetPadding(0, 16, 0, 0);
        mSound = AddButton(row2, "", (s, e) => { mCfg.Sound = !mCfg.Sound; mCfg.Save(this); Refresh(); });
        AddButton(row2, "Guardar registro en Descargas", (s, e) => ExportLog(false));
        AddButton(row2, "Compartir registro", (s, e) => ExportLog(true));
        root.AddView(row2);

        var help = new TextView(this) { TextSize = 12, Text =
            "Partida de PC (GOG): %USERPROFILE%\\AppData\\LocalLow\\sebagamesdev\\gog\\fightnrage\\savedata\\data.sav\n" +
            "Si algo falla: «Guardar registro en Descargas» y pásame ese archivo.\n" +
            "De momento: sin sonido y sin controles táctiles (usa mando o teclado)." };
        help.SetTextColor(Color.Gray);
        help.SetPadding(0, 24, 0, 0);
        root.AddView(help);

        var scroll = new ScrollView(this);
        scroll.AddView(root);
        SetContentView(scroll);
        Refresh();
    }

    Button AddButton(LinearLayout row, string text, EventHandler click)
    {
        var b = new Button(this) { Text = text };
        b.Click += click;
        var lp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);
        lp.SetMargins(0, 0, 16, 0);
        row.AddView(b, lp);
        return b;
    }

    void Refresh()
    {
        string problem = GameLauncher.Validate(GameDir);
        mPlay.Enabled = problem == null && !mBusy;
        mPick.Enabled = mSave.Enabled = mScale.Enabled = !mBusy;
        mSound.Text = "Sonido: " + (mCfg.Sound ? "sí" : "no");
        mScale.Text = "Resolución: " + (mCfg.Scale == 0 ? $"auto (x{Settings.AutoScale(this)})" : "x" + mCfg.Scale);
        if (!mBusy)
            mStatus.Text = problem == null
                ? "Juego instalado. " + (File.Exists(SavePath) ? "Hay partida guardada." : "Sin partida guardada todavía.")
                : "Juego no instalado: pulsa «Elegir carpeta del juego» y selecciona la carpeta de Fight'N Rage.";
        if (problem == null && !mBusy) mPlay.RequestFocus(); else mPick.RequestFocus();
    }

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

    void CycleScale()
    {
        mCfg.Scale = (mCfg.Scale + 1) % 5; // 0=auto,1..4
        mCfg.Save(this);
        Refresh();
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
                    if (++done % 25 == 0) { int d = done; RunOnUiThread(() => mStatus.Text = $"Copiando archivos del juego... {d}/{total}"); }
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
                RunOnUiThread(() => { mBusy = false; Refresh(); mStatus.Text = "Error: " + ex.Message; });
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
