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
        var nc = T.Card(mRoot, L.T("Juego en red (hasta 3 jugadores)", "Netplay (up to 3 players)"));
        if (installed) nc.AddView(T.Button(L.T("Crear partida (anfitrión)", "Host a game"), false, HostGame, !mBusy));
        nc.AddView(T.Button(L.T("Unirse en la misma wifi", "Join on the same wifi"), false, JoinWifi, !mBusy));
        nc.AddView(T.Button(L.T("Unirse por internet (código o IP)", "Join over the internet (code or IP)"), false, AskCode, !mBusy));
        nc.AddView(T.Text(L.T("Quien crea la partida juega con su copia del juego y ve arriba su IP de la wifi y un código de 8 letras para internet. Pueden entrar dos invitados con el mismo código o IP (jugadores 2 y 3). " +
            "Los invitados no necesitan el juego: ven la imagen, oyen el sonido y juegan con su mando o los táctiles como un jugador más " +
            "(en el juego, que pulsen START para entrar). Todas las consolas necesitan esta misma versión de la app.",
            "Whoever hosts plays with their copy of the game and sees their wifi IP and an 8-letter internet code at the top. Two guests can join with the same code or IP (players 2 and 3). " +
            "Guests don't need the game: they see the picture, hear the sound and play with their gamepad or the touch controls as one more player " +
            "(in the game, press START to join). Every console needs this same version of the app."), 13, LauncherTheme.MUTED));
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
        mStatus.Text = L.T("Buscando partidas en la wifi...", "Looking for games on the wifi...");
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
                    mStatus.Text = L.T("No se ha encontrado ninguna partida en esta wifi. ¿Ha pulsado el otro «Crear partida»? " +
                        "Si no aparece, usa «Unirse por internet» y escribe la IP que ve él arriba.",
                        "No game found on this wifi. Did the other player press \"Host a game\"? " +
                        "If it doesn't show up, use \"Join over the internet\" and type the IP they see at the top.");
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
            .SetTitle(L.T("Unirse a una partida", "Join a game"))
            .SetMessage(L.T("Escribe el código de 8 letras que ve el anfitrión arriba de su pantalla (o su IP).", "Type the 8-letter code the host sees at the top of their screen (or their IP)."))
            .SetView(et)
            .SetPositiveButton(L.T("Conectar", "Connect"), (s, e) =>
            {
                string txt = et.Text?.Trim() ?? "";
                if (!Inet.Target(txt, out string host, out int port))
                {
                    Toast.MakeText(this, L.T("Ese código no es válido. Revisa las letras.", "That code is not valid. Check the letters."), ToastLength.Long).Show();
                    return;
                }
                prefs.Edit().PutString("net_last_code", txt).Apply();
                mBusy = true; Refresh();
                mStatus.Text = L.T("Conectando con ", "Connecting to ") + txt + "...";
                Task.Run(() => Connect(host, port));
            })
            .SetNegativeButton(L.T("Cancelar", "Cancel"), (s, e) => { })
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
                mStatus.Text = L.T("No se ha podido entrar: ", "Could not join: ") + NetSession.LastError;
                return;
            }
            StartActivity(new Intent(this, typeof(GuestActivity)));
        });
    }
}
