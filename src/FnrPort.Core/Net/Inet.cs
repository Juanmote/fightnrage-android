using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace FnrPort.Net;

/// <summary>
/// Juego en red por internet (igual que en Outfoxies y SorR): el anfitrión intenta abrir el puerto en el router con
/// UPnP, averigua su IP pública y la da como un código de 8 letras (ABCD-EFGH) que el otro escribe. Si desde fuera no
/// se puede entrar (CGNAT, datos móviles, router sin UPnP...), se conecta HACIA FUERA a un servidor de túneles con el
/// protocolo de bore (bore.pub:7835) y el código lleva el puerto que le da ese servidor. Con Tailscale/ZeroTier el
/// código lleva la IP de esa red.
/// </summary>
public static class Inet
{
    public const int BasePort = NetSession.Port;
    const string Alpha = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    const uint CodeXor = 0xA5C3E1F7u;
    public static string RelayHost = "bore.pub";
    public static int RelayPort = 7835;

    public enum State { Idle, Working, Ok, Manual, Cgnat, Fail, Vpn, Mobile, Double, Relay }
    static volatile State sState = State.Idle, sWhy = State.Idle;
    static volatile string sCode = "";
    static volatile bool sQuit;
    static Thread sThread;
    static string sCtlHost, sCtlPath, sSvcType; static int sCtlPort, sExtPort; static bool sMapped;

    // ------------------------------------------------------------------ código

    static uint Check4(uint ip, int k)
    {
        uint s = 7;
        for (int i = 0; i < 8; i++) s = s * 5 + ((ip >> (i * 4)) & 15);
        return (uint)((s + k * 3) & 15);
    }

    public static string Encode(uint ip, int k)
    {
        ulong v = ((ulong)(ip ^ CodeXor) << 8) | (uint)(k & 15) << 4 | Check4(ip, k);
        var c = new char[8];
        for (int i = 0; i < 8; i++) c[i] = Alpha[(int)((v >> (35 - i * 5)) & 31)];
        return new string(c);
    }

    static int CharIndex(char c)
    {
        c = char.ToUpperInvariant(c);
        if (c == '0') c = 'O'; if (c == '1') c = 'I';
        if (c == 'O') c = 'Q'; if (c == 'I') c = 'J';
        return Alpha.IndexOf(c);
    }

    public static bool Decode(string s, out uint ip, out int k)
    {
        ip = 0; k = 0;
        ulong v = 0; int n = 0;
        foreach (char ch in s ?? "")
        {
            if (ch == '-' || ch == ' ') continue;
            int d = CharIndex(ch);
            if (d < 0 || n >= 8) return false;
            v = v << 5 | (uint)d; n++;
        }
        if (n != 8) return false;
        uint x = (uint)(v >> 8) ^ CodeXor; int kk = (int)(v >> 4) & 15;
        if ((v & 15) != Check4(x, kk)) return false;
        ip = x; k = kk;
        return true;
    }

    /// <summary>A dónde conectar con un código (o con una IP escrita a mano, con o sin :puerto).</summary>
    public static bool Target(string text, out string host, out int port)
    {
        host = null; port = BasePort;
        text = (text ?? "").Trim();
        var m = Regex.Match(text, @"^([0-9]{1,3}(\.[0-9]{1,3}){3})(:(\d+))?$");
        if (m.Success) { host = m.Groups[1].Value; if (m.Groups[4].Success) port = int.Parse(m.Groups[4].Value); return true; }
        if (!Decode(text, out uint ip, out int k)) return false;
        if ((ip >> 24) == 0) { host = RelayHost; port = (int)(ip & 0xFFFF); }   // servidor intermedio: 0.0.puerto
        else { host = $"{ip >> 24}.{(ip >> 16) & 255}.{(ip >> 8) & 255}.{ip & 255}"; port = BasePort + k; }
        return true;
    }

    public static string Pretty(string code) => code.Length == 8 ? code.Substring(0, 4) + "-" + code.Substring(4) : code;

    // ------------------------------------------------------------------ anfitrión

    public static void HostBegin()
    {
        HostEnd();
        sQuit = false; sState = State.Working; sCode = ""; sMapped = false; sWhy = State.Idle;
        if (Environment.GetEnvironmentVariable("FNR_NOINET") != null) { sState = State.Idle; return; }
        sThread = new Thread(Worker) { IsBackground = true, Name = "FnrInet" };
        sThread.Start();
    }

    public static void HostEnd()
    {
        sQuit = true;
        sThread?.Join(3000); sThread = null;
        if (sMapped)
        {
            try { Soap("DeletePortMapping", $"<NewRemoteHost></NewRemoteHost><NewExternalPort>{sExtPort}</NewExternalPort><NewProtocol>TCP</NewProtocol>", out _); } catch { }
            sMapped = false;
        }
        sState = State.Idle; sCode = "";
    }

    public static State CurrentState => sState;
    public static string Code => sCode.Length == 8 ? Pretty(sCode) : "";

    public static string StatusText()
    {
        string c = Code;
        return sState switch
        {
            State.Working => Lang.T("Internet: preparando el código...", "Internet: preparing the code..."),
            State.Ok => Lang.T("Código para internet: ", "Internet code: ") + c,
            State.Relay => Lang.T("Código para internet: ", "Internet code: ") + c + Lang.T(" (por el servidor intermedio ", " (through the relay server ") + RelayHost + ")",
            State.Vpn => Lang.T("Código para la VPN (Tailscale...): ", "Code for the VPN (Tailscale...): ") + c,
            State.Manual => Lang.T("Código para internet: ", "Internet code: ") + c + Lang.T(". El router no tiene UPnP: abre en él el puerto TCP ", ". The router has no UPnP: forward TCP port ") + BasePort + Lang.T(" a esta consola", " to this console"),
            State.Cgnat => Lang.T("Internet: tu operador comparte la IP (CGNAT). Que cree la partida el otro, o usad Tailscale", "Internet: your provider shares your IP (CGNAT). Let the other player host, or use Tailscale"),
            State.Double => Lang.T("Internet: hay dos routers en cadena. Abre el TCP ", "Internet: there are two routers in a row. Forward TCP ") + BasePort + Lang.T(" en el del operador, o usad Tailscale", " on the provider's one, or use Tailscale"),
            State.Mobile => Lang.T("Internet: con datos móviles nadie puede entrar. Únete tú a la partida del otro, o usad Tailscale", "Internet: nobody can join over mobile data. Join the other player's game instead, or use Tailscale"),
            State.Fail => Lang.T("Internet: no hay conexión", "Internet: no connection"),
            _ => "",
        };
    }

    static void Worker()
    {
        try
        {
            // redes de la consola
            uint vpn = 0; bool lan = false, mobile = false;
            foreach (var (ip, _, n) in NetSession.LocalAddresses())
            {
                uint a = ToU(ip);
                if (n.StartsWith("tun") || n.StartsWith("tailscale") || n.StartsWith("zt") || n.StartsWith("wg"))
                { if (vpn == 0 || (a >> 22) == (100u << 2 | 1)) vpn = a; }
                else if (n.StartsWith("rmnet") || n.StartsWith("ccmni") || n.StartsWith("pdp") || n.StartsWith("seth")) mobile = true;
                else lan = true;
            }
            if (vpn != 0) { sCode = Encode(vpn, 0); sState = State.Vpn; return; }
            if (mobile && !lan) { sWhy = State.Mobile; RelayLoop(); return; }

            uint extIp = 0, local = 0; int k = -1;
            if (UpnpSetup() && !sQuit)
            {
                if (Soap("GetExternalIPAddress", "", out string r, out local) == 200)
                {
                    var m = Regex.Match(r, "NewExternalIPAddress>\\s*([0-9.]+)");
                    if (m.Success && IPAddress.TryParse(m.Groups[1].Value, out var eip)) extIp = ToU(eip);
                }
                for (int i = 0; i < 16 && local != 0 && !sQuit; i++)
                    if (Map(BasePort + i, local, 14400) || Map(BasePort + i, local, 0)) { k = i; break; }
                if (k >= 0) { sMapped = true; sExtPort = BasePort + k; }
            }
            if (sQuit) return;
            uint pub = Ipify();
            if (sQuit) return;
            State st;
            if (k >= 0 && extIp != 0 && !Private(extIp) && (pub == 0 || pub == extIp)) { sCode = Encode(extIp, k); st = State.Ok; }
            else if (k >= 0 && (Private(extIp) || (pub != 0 && pub != extIp)))
            {
                if (pub != 0) sCode = Encode(pub, k);
                st = extIp != 0 && (extIp >> 22) != (100u << 2 | 1) && Private(extIp) ? State.Double : State.Cgnat;
            }
            else if (pub != 0) { sCode = Encode(pub, 0); st = State.Manual; }
            else st = State.Fail;
            sState = st;
            if (NetSession.Log) Console.WriteLine($"[FnrNet] inet: {st} código {sCode} router {extIp:X8} ipify {pub:X8}");
            if (st != State.Ok && Environment.GetEnvironmentVariable("FNR_NORELAY") == null)
            {
                sWhy = st; sState = State.Working; sCode = "";
                RelayLoop();
            }
        }
        catch (Exception ex) { Console.WriteLine("[FnrNet] inet: " + ex.Message); if (sState == State.Working) sState = State.Fail; }
    }

    static uint ToU(IPAddress a) { var b = a.GetAddressBytes(); return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]); }
    static bool Private(uint ip) => (ip >> 24) == 10 || (ip >> 20) == 0xAC1 || (ip >> 16) == 0xC0A8 || (ip >> 22) == (100u << 2 | 1) || (ip >> 24) == 127 || ip == 0;

    // ------------------------------------------------------------------ HTTP mínimo

    static Socket Connect(string host, int port, int ms)
    {
        var addrs = Dns.GetHostAddresses(host);
        foreach (var a in addrs)
        {
            if (a.AddressFamily != AddressFamily.InterNetwork) continue;
            var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            var ar = s.BeginConnect(a, port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(ms)) { s.Close(); continue; }
            try { s.EndConnect(ar); } catch { s.Close(); continue; }
            s.ReceiveTimeout = ms; s.SendTimeout = ms;
            return s;
        }
        return null;
    }

    static int Http(string host, int port, string req, out string resp, out uint local)
    {
        resp = ""; local = 0;
        using var s = Connect(host, port, 2500);
        if (s == null) return -1;
        if (s.LocalEndPoint is IPEndPoint lep) local = ToU(lep.Address);
        s.Send(Encoding.ASCII.GetBytes(req));
        var ms = new MemoryStream(); var b = new byte[4096]; int n;
        try { while ((n = s.Receive(b)) > 0 && ms.Length < 65536) ms.Write(b, 0, n); } catch { }
        resp = Encoding.UTF8.GetString(ms.ToArray());
        var m = Regex.Match(resp, @"^HTTP/\S+\s+(\d+)");
        return m.Success ? int.Parse(m.Groups[1].Value) : -1;
    }

    static bool SplitUrl(string url, out string host, out int port, out string path)
    {
        host = null; port = 80; path = "/";
        var m = Regex.Match(url, @"^\w+://([^/:]+)(:(\d+))?(/.*)?$");
        if (!m.Success) return false;
        host = m.Groups[1].Value;
        if (m.Groups[3].Success) port = int.Parse(m.Groups[3].Value);
        if (m.Groups[4].Success) path = m.Groups[4].Value;
        return true;
    }

    static uint Ipify()
    {
        if (Http("api.ipify.org", 80, "GET / HTTP/1.1\r\nHost: api.ipify.org\r\nConnection: close\r\n\r\n", out string r, out _) != 200) return 0;
        int i = r.IndexOf("\r\n\r\n");
        if (i < 0) return 0;
        return IPAddress.TryParse(r.Substring(i + 4).Trim(), out var a) && a.AddressFamily == AddressFamily.InterNetwork ? ToU(a) : 0;
    }

    // ------------------------------------------------------------------ UPnP

    static string SsdpFind()
    {
        string[] st = { "urn:schemas-upnp-org:device:InternetGatewayDevice:1", "urn:schemas-upnp-org:service:WANIPConnection:1",
            "urn:schemas-upnp-org:service:WANPPPConnection:1", "urn:schemas-upnp-org:device:InternetGatewayDevice:2" };
        using var u = new UdpClient(AddressFamily.InterNetwork);
        var to = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
        for (int round = 0; round < 2 && !sQuit; round++)
        {
            foreach (var t in st)
            {
                var msg = Encoding.ASCII.GetBytes($"M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\nST: {t}\r\n\r\n");
                try { u.Send(msg, msg.Length, to); } catch { }
            }
            var until = DateTime.UtcNow.AddMilliseconds(2500);
            while (DateTime.UtcNow < until && !sQuit)
            {
                if (!u.Client.Poll(200_000, SelectMode.SelectRead)) continue;
                var ep = new IPEndPoint(IPAddress.Any, 0);
                string r = Encoding.ASCII.GetString(u.Receive(ref ep));
                var m = Regex.Match(r, @"(?im)^LOCATION:\s*(\S+)");
                if (m.Success) return m.Groups[1].Value;
            }
        }
        return null;
    }

    static string Xml(string s, string tag)
    {
        var m = Regex.Match(s, tag + @">\s*([^<]*?)\s*<");
        return m.Success ? m.Groups[1].Value : null;
    }

    static bool UpnpSetup()
    {
        string loc = SsdpFind();
        if (loc == null || !SplitUrl(loc, out string host, out int port, out string path)) return false;
        if (Http(host, port, $"GET {path} HTTP/1.1\r\nHost: {host}:{port}\r\nConnection: close\r\n\r\n", out string desc, out _) != 200) return false;
        int i = -1;
        foreach (var svc in new[] { "urn:schemas-upnp-org:service:WANIPConnection:", "urn:schemas-upnp-org:service:WANPPPConnection:" })
        { i = desc.IndexOf("serviceType>" + svc); if (i >= 0) break; }
        if (i < 0) return false;
        string part = desc.Substring(i);
        sSvcType = Xml(part, "serviceType"); string ctl = Xml(part, "controlURL");
        if (sSvcType == null || ctl == null) return false;
        if (ctl.Contains("://")) return SplitUrl(ctl, out sCtlHost, out sCtlPort, out sCtlPath);
        string bas = Xml(desc, "URLBase");
        if (!SplitUrl(string.IsNullOrEmpty(bas) ? loc : bas, out sCtlHost, out sCtlPort, out _)) return false;
        sCtlPath = (ctl.StartsWith("/") ? "" : "/") + ctl;
        return true;
    }

    static int Soap(string action, string args, out string resp) => Soap(action, args, out resp, out _);

    static int Soap(string action, string args, out string resp, out uint local)
    {
        resp = ""; local = 0;
        if (sCtlHost == null) return -1;
        string body = "<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" " +
            "s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body>" +
            $"<u:{action} xmlns:u=\"{sSvcType}\">{args}</u:{action}></s:Body></s:Envelope>";
        string req = $"POST {sCtlPath} HTTP/1.1\r\nHost: {sCtlHost}:{sCtlPort}\r\nContent-Type: text/xml; charset=\"utf-8\"\r\n" +
            $"SOAPAction: \"{sSvcType}#{action}\"\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";
        return Http(sCtlHost, sCtlPort, req, out resp, out local);
    }

    static bool Map(int ext, uint local, int lease)
    {
        string ip = $"{local >> 24}.{(local >> 16) & 255}.{(local >> 8) & 255}.{local & 255}";
        string args = $"<NewRemoteHost></NewRemoteHost><NewExternalPort>{ext}</NewExternalPort><NewProtocol>TCP</NewProtocol>" +
            $"<NewInternalPort>{BasePort}</NewInternalPort><NewInternalClient>{ip}</NewInternalClient><NewEnabled>1</NewEnabled>" +
            $"<NewPortMappingDescription>Fight'N Rage</NewPortMappingDescription><NewLeaseDuration>{lease}</NewLeaseDuration>";
        return Soap("AddPortMapping", args, out _) == 200;
    }

    // ------------------------------------------------------------------ servidor intermedio (bore)

    static bool BoreSend(Socket s, string json)
    {
        var b = Encoding.UTF8.GetBytes(json + "\0");
        try { return s.Send(b) == b.Length; } catch { return false; }
    }

    /// <summary>Un mensaje hasta el byte 0. null = cerrado; "" = no ha llegado nada en <paramref name="ms"/>.</summary>
    static string BoreRecv(Socket s, int ms)
    {
        var sb = new StringBuilder();
        var one = new byte[1];
        while (true)
        {
            try { if (!s.Poll(ms * 1000, SelectMode.SelectRead)) return sb.Length > 0 ? null : ""; } catch { return null; }
            int n; try { n = s.Receive(one); } catch { return null; }
            if (n != 1) return null;
            if (one[0] == 0) return sb.ToString();
            if (sb.Length < 512) sb.Append((char)one[0]);
            ms = 3000;
        }
    }

    static void RelayLoop()
    {
        int fails = 0;
        while (!sQuit && fails < 4)
        {
            Socket s = null;
            try { s = Connect(RelayHost, RelayPort, 4000); } catch { }
            if (s == null) { fails++; Thread.Sleep(1500); continue; }
            s.ReceiveTimeout = 0;
            int port = 0;
            if (BoreSend(s, "{\"Hello\":0}"))
            {
                string msg;
                while (!sQuit && !string.IsNullOrEmpty(msg = BoreRecv(s, 6000)))
                {
                    var m = Regex.Match(msg, "\"Hello\"\\s*:\\s*(\\d+)");
                    if (m.Success) { port = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture); break; }
                    if (msg.Contains("Challenge") || msg.Contains("Error")) break;
                }
            }
            if (port <= 0 || port > 65535) { s.Close(); fails++; Thread.Sleep(1500); continue; }
            fails = 0;
            sCode = Encode((uint)port, 0); sState = State.Relay;
            Console.WriteLine($"[FnrNet] Servidor intermedio {RelayHost}: puerto {port}, código {Pretty(sCode)}");
            var last = DateTime.UtcNow;
            while (!sQuit)
            {
                string msg = BoreRecv(s, 500);
                if (msg == null) break;
                if (msg.Length == 0) { if ((DateTime.UtcNow - last).TotalSeconds > 15) break; continue; }
                last = DateTime.UtcNow;
                var m = Regex.Match(msg, "\"Connection\"\\s*:\\s*\"([^\"]+)\"");
                if (!m.Success) continue;
                Socket d = null;
                try { d = Connect(RelayHost, RelayPort, 4000); } catch { }
                if (d == null) continue;
                if (!BoreSend(d, "{\"Accept\":\"" + m.Groups[1].Value + "\"}")) { d.Close(); continue; }
                d.ReceiveTimeout = 0; d.SendTimeout = 0;
                NetSession.HostOffer(d);
            }
            try { s.Close(); } catch { }
            if (!sQuit) { sState = State.Working; sCode = ""; Thread.Sleep(1000); }
        }
        if (!sQuit) sState = sWhy;
    }
}
