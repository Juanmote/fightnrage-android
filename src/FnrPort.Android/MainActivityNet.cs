using System;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Text;
using Android.Widget;
using FnrPort.Net;

namespace FnrPort.Droid;

/// <summary>Tarjeta «Juego en red» del launcher (como en Outfoxies y Streets of Rage Remake).</summary>
public partial class MainActivity
{
    partial void BuildNetCardImpl(ref bool done)
    {
        done = true;
        bool installed = GameLauncher.Validate(GameDir) == null;
        var nc = T.Card(mRoot, "Juego en red (2 jugadores)");
        if (installed) nc.AddView(T.Button("Crear partida (anfitrión)", false, HostGame, !mBusy));
        nc.AddView(T.Button("Unirse en la misma wifi", false, JoinWifi, !mBusy));
        nc.AddView(T.Button("Unirse por internet (código o IP)", false, AskCode, !mBusy));
        nc.AddView(T.Text("Quien crea la partida juega con su copia del juego y ve arriba su IP de la wifi y un código de 8 letras para internet. " +
            "El otro no necesita el juego: ve la imagen, oye el sonido y juega con su mando o los táctiles como un jugador más " +
            "(en el juego, que pulse START para entrar). Las dos consolas necesitan esta misma versión de la app.", 13, LauncherTheme.MUTED));
    }

    void HostGame()
    {
        var i = new Intent(this, typeof(GameActivity));
        i.PutExtra(GameActivity.ExtraNetHost, true);
        StartActivity(i);
    }

    void JoinWifi()
    {
        mBusy = true; Refresh();
        mStatus.Text = "Buscando partidas en la wifi...";
        WifiLock.Acquire(this);
        Task.Run(() =>
        {
            string ip = null;
            if (NetSession.SearchStart())
                for (int k = 0; k < 60 && (ip = NetSession.Found()) == null; k++) System.Threading.Thread.Sleep(100);
            NetSession.Stop();
            WifiLock.Release();
            if (ip == null)
            {
                RunOnUiThread(() =>
                {
                    mBusy = false; Refresh();
                    mStatus.Text = "No se ha encontrado ninguna partida en esta wifi. ¿Ha pulsado el otro «Crear partida»? " +
                        "Si no aparece, usa «Unirse por internet» y escribe la IP que ve él arriba.";
                });
                return;
            }
            Connect(ip, NetSession.Port);
        });
    }

    void AskCode()
    {
        var prefs = GetSharedPreferences("fnr", FileCreationMode.Private);
        var et = new EditText(this) { Text = prefs.GetString("net_last_code", ""), Hint = "ABCD-EFGH  o  192.168.1.20" };
        et.SetSingleLine(true);
        et.InputType = InputTypes.ClassText | InputTypes.TextFlagCapCharacters | InputTypes.TextFlagNoSuggestions;
        new AlertDialog.Builder(this)
            .SetTitle("Unirse a una partida")
            .SetMessage("Escribe el código de 8 letras que ve el anfitrión arriba de su pantalla (o su IP).")
            .SetView(et)
            .SetPositiveButton("Conectar", (s, e) =>
            {
                string txt = et.Text?.Trim() ?? "";
                if (!Inet.Target(txt, out string host, out int port))
                {
                    Toast.MakeText(this, "Ese código no es válido. Revisa las letras.", ToastLength.Long).Show();
                    return;
                }
                prefs.Edit().PutString("net_last_code", txt).Apply();
                mBusy = true; Refresh();
                mStatus.Text = "Conectando con " + txt + "...";
                Task.Run(() => Connect(host, port));
            })
            .SetNegativeButton("Cancelar", (s, e) => { })
            .Show();
    }

    /// <summary>En un hilo aparte: entra en la partida y abre la pantalla del invitado.</summary>
    void Connect(string host, int port)
    {
        bool ok = NetSession.Join(host, port);
        RunOnUiThread(() =>
        {
            mBusy = false; Refresh();
            if (!ok)
            {
                mStatus.Text = "No se ha podido entrar: " + NetSession.LastError;
                return;
            }
            StartActivity(new Intent(this, typeof(GuestActivity)));
        });
    }
}
