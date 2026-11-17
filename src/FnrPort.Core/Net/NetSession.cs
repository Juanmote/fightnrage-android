using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace FnrPort.Net;

/// <summary>
/// Juego en red, mismo modelo que los ports de The Outfoxies y Streets of Rage Remake (y que Días de Furia, de
/// Mckeijas): solo el ANFITRIÓN ejecuta el juego; manda imagen y sonido a los INVITADOS (hasta 2: el juego admite
/// 3 jugadores) y recibe sus botones, que entran como mandos (jugadores 2 y 3). No puede desincronizarse y los
/// invitados no necesitan el juego. Cada invitado tiene su propia cola de envío y su propia imagen de referencia.
///
/// Una conexión TCP (puerto 47830) con mensajes [tipo:1][longitud:4, big endian][datos]:
///   invitado -> anfitrión: HOLA "FNRN"+versión | ENTRADAS (2 bytes de botones) | PING marca | ADIOS
///   anfitrión -> invitado: BIENVENIDO número de jugador | VERSION (no coincide) | LLENA | VIDEO | AUDIO | PONG marca | FIN
/// VIDEO: la imagen del juego reducida a 354x200 (su resolución de pixel art) en RGB, sin pérdida: clave:1, ancho:2,
///   alto:2, dx:1, dy:1, banderas:1 y los datos con zlib; si clave = 0, lo comprimido es el XOR con la imagen anterior
///   desplazada (dx, dy): el fondo que se desliza casi no ocupa.
/// AUDIO: el mezclador del anfitrión a la mitad de frecuencia en ADPCM IMA de 4 bits (estado al principio).
/// En la wifi el invitado encuentra al anfitrión por UDP: pregunta "FNRQ1" al 47830 y el anfitrión se anuncia ("FNRN1")
/// al 47831 cada segundo. Por internet: código de 8 letras (ver <see cref="Inet"/>).
/// </summary>
public static class NetSession
{
    public const int Port = 47830;
    public const int Version = 2;
    /// <summary>Invitados a la vez (el juego admite 3 jugadores: el anfitrión y dos más).</summary>
    public const int MaxGuests = 2;
    public const int MaxW = 354, MaxH = 200;
    const int FrameMax = MaxW * MaxH * 3;
    const int QueueLimit = 700 * 1024, SkipLimit = 140 * 1024;
    const byte M_HOLA = 1, M_ENTRADAS = 2, M_ADIOS = 4, M_PING = 5, M_BIENVENIDO = 10, M_VERSION = 12, M_VIDEO = 14, M_FIN = 16,
        M_PONG = 17, M_AUDIO = 19, M_LLENA = 20;

    public enum Mode { Off, Host, Search, Guest }
    /// <summary>Bits de los botones del invitado.</summary>
    [Flags]
    public enum Btn : ushort { Up = 1, Down = 2, Left = 4, Right = 8, A = 16, B = 32, X = 64, Y = 128, LB = 256, RB = 512, Start = 1024, Back = 2048, C = 4096, Z = 8192 }

    static readonly object sLock = new();
    static volatile Mode sMode;
    static volatile bool sQuit, sConnected;
    static Thread sConnThread, sSendThread, sUdpThread;
    static TcpListener sListener;
    static UdpClient sUdp;
    static Socket sClient;            // invitado: la conexión con el anfitrión
    static Socket sOffered;           // anfitrión: conexión que llega por el servidor intermedio
    static readonly Queue<int> sHostEvents = new(); // anfitrión: 10*jugador + 1 (ha entrado) / + 2 (se ha ido)

    /// <summary>Anfitrión: un invitado conectado (jugador 2 o 3).</summary>
    sealed class Peer
    {
        public int Slot;              // 0 o 1 (jugador 2 o 3)
        public Socket S;
        public volatile bool Alive = true;
        public volatile int Bits;
        public readonly LinkedList<(byte[] data, bool video)> Queue = new();
        public int QueueBytes;
        public bool ForceKey = true;
        public readonly byte[] Ref = new byte[FrameMax], Tmp = new byte[FrameMax];
        public int W, H;
        public long RefId = -1;       // qué imagen hay en Ref (-1 = ninguna)
        public Thread Send, Recv;
    }
    static readonly Peer[] sPeers = new Peer[MaxGuests];
    static string sFoundIp; static long sFoundAt;
    static volatile int sPingMs = -1;

    public static Mode CurrentMode => sMode;
    public static bool Connected => sMode == Mode.Host ? GuestCount > 0 : sConnected;
    /// <summary>Anfitrión: invitados conectados ahora mismo.</summary>
    public static int GuestCount { get { int n = 0; lock (sLock) foreach (var p in sPeers) if (p != null && p.Alive) n++; return n; } }
    public static bool IsHost => sMode == Mode.Host;
    public static int PingMs => sPingMs;
    public static bool Log = Environment.GetEnvironmentVariable("FNR_NETLOG") != null;

    static readonly Stopwatch sClock = Stopwatch.StartNew();
    static long Now => sClock.ElapsedMilliseconds;

    // ------------------------------------------------------------------ utilidades de socket

    static void SendMsg(Socket s, byte type, ReadOnlySpan<byte> data)
    {
        var b = new byte[5 + data.Length];
        b[0] = type; Put32(b, 1, data.Length); data.CopyTo(b.AsSpan(5));
        SendAll(s, b);
    }

    static void SendAll(Socket s, byte[] b)
    {
        int off = 0;
        while (off < b.Length) { int k = s.Send(b, off, b.Length - off, SocketFlags.None); if (k <= 0) throw new IOException("envío"); off += k; }
    }

    static bool RecvAll(Socket s, byte[] b, int n)
    {
        int off = 0;
        while (off < n) { int k; try { k = s.Receive(b, off, n - off, SocketFlags.None); } catch { return false; } if (k <= 0) return false; off += k; }
        return true;
    }

    static void Put32(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
    static int Get32(byte[] b, int o) => b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3];

    /// <summary>Lee un mensaje. Devuelve el tipo o -1.</summary>
    static int RecvMsg(Socket s, ref byte[] buf, out int len)
    {
        len = 0;
        var h = new byte[5];
        if (!RecvAll(s, h, 5)) return -1;
        int n = Get32(h, 1);
        if (n < 0 || n > FrameMax + 4096) return -1;
        if (buf.Length < n) buf = new byte[n + 1024];
        if (n > 0 && !RecvAll(s, buf, n)) return -1;
        len = n;
        return h[0];
    }

    static void Tune(Socket s)
    {
        s.NoDelay = true;
        s.SendTimeout = 4000;
        try { s.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.TypeOfService, 0x88); } catch { }
    }

    static void CloseQuiet(ref Socket s)
    {
        var x = s; s = null;
        if (x == null) return;
        try { x.Shutdown(SocketShutdown.Both); } catch { }
        try { x.Close(); } catch { }
    }

    // ------------------------------------------------------------------ redes locales

    /// <summary>IPv4 de este aparato con su máscara (sin bucle ni datos móviles).</summary>
    public static List<(IPAddress ip, IPAddress mask, string name)> LocalAddresses()
    {
        var r = new List<(IPAddress, IPAddress, string)>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus == OperationalStatus.Down || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ua.Address)) continue;
                    IPAddress mask;
                    try { mask = ua.IPv4Mask ?? IPAddress.Parse("255.255.255.0"); } catch { mask = IPAddress.Parse("255.255.255.0"); }
                    if (mask.Equals(IPAddress.Any)) mask = IPAddress.Parse("255.255.255.0");
                    r.Add((ua.Address, mask, ni.Name ?? ""));
                }
            }
        }
        catch (Exception ex) { if (Log) Console.WriteLine("[FnrNet] interfaces: " + ex.Message); }
        return r;
    }

    static bool IsMobileIf(string n) => n.StartsWith("rmnet") || n.StartsWith("ccmni") || n.StartsWith("pdp") || n.StartsWith("seth");
    static bool IsVpnIf(string n) => n.StartsWith("tun") || n.StartsWith("tailscale") || n.StartsWith("zt") || n.StartsWith("wg");

    /// <summary>La IP de la wifi (o del punto de acceso) que se enseña al usuario; null si no hay red local.</summary>
    public static string LocalIp()
    {
        foreach (var (ip, _, n) in LocalAddresses()) if (!IsMobileIf(n) && !IsVpnIf(n)) return ip.ToString();
        return null;
    }

    static uint ToU(IPAddress a) { var b = a.GetAddressBytes(); return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]); }
    static IPAddress FromU(uint v) => new IPAddress(new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v });

    static void UdpBroadcast(UdpClient u, int port, string txt)
    {
        var b = Encoding.ASCII.GetBytes(txt);
        try { u.Send(b, b.Length, new IPEndPoint(IPAddress.Broadcast, port)); } catch { }
        foreach (var (ip, mask, n) in LocalAddresses())
        {
            if (IsMobileIf(n)) continue;
            try { u.Send(b, b.Length, new IPEndPoint(FromU(ToU(ip) | ~ToU(mask)), port)); } catch { }
        }
        if (Environment.GetEnvironmentVariable("FNR_NETLOCAL") != null)
            try { u.Send(b, b.Length, new IPEndPoint(IPAddress.Loopback, port)); } catch { }
    }

    /// <summary>Por si la difusión no llega: la pregunta a las 254 direcciones de cada red local.</summary>
    static void UdpSweep(UdpClient u, int port, string txt)
    {
        var b = Encoding.ASCII.GetBytes(txt);
        foreach (var (ip, _, n) in LocalAddresses())
        {
            if (IsMobileIf(n) || IsVpnIf(n)) continue;
            uint a = ToU(ip);
            for (uint k = 1; k < 255; k++)
            {
                if (k == (a & 255)) continue;
                try { u.Send(b, b.Length, new IPEndPoint(FromU((a & 0xFFFFFF00u) | k), port)); } catch { }
            }
        }
    }

    static void UdpThread()
    {
        long next = 0;
        var any = new IPEndPoint(IPAddress.Any, 0);
        while (!sQuit)
        {
            var u = sUdp;
            if (u == null) break;
            if (Now >= next)
            {
                if (sMode == Mode.Host) UdpBroadcast(u, Port + 1, "FNRN1");
                else { UdpBroadcast(u, Port, "FNRQ1"); UdpSweep(u, Port, "FNRQ1"); }
                next = Now + 1000;
            }
            try
            {
                if (!u.Client.Poll(250_000, SelectMode.SelectRead)) continue;
                var ep = any;
                var d = u.Receive(ref ep);
                string m = Encoding.ASCII.GetString(d);
                if (sMode == Mode.Host && m.StartsWith("FNRQ")) u.Send(Encoding.ASCII.GetBytes("FNRN1"), 5, ep);
                else if (sMode != Mode.Host && m.StartsWith("FNRN")) lock (sLock) { sFoundIp = ep.Address.ToString(); sFoundAt = Now; }
            }
            catch { if (sQuit) break; Thread.Sleep(50); }
        }
    }

    static UdpClient UdpOpen(int port)
    {
        try
        {
            var u = new UdpClient(AddressFamily.InterNetwork);
            u.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            u.EnableBroadcast = true;
            u.Client.Bind(new IPEndPoint(IPAddress.Any, port));
            return u;
        }
        catch (Exception ex) { Console.WriteLine("[FnrNet] UDP " + port + ": " + ex.Message); return null; }
    }

    // ------------------------------------------------------------------ anfitrión

    /// <summary>Abre la partida. Devuelve false si el puerto está ocupado.</summary>
    public static bool HostStart()
    {
        Stop();
        try
        {
            sListener = new TcpListener(IPAddress.Any, Port);
            sListener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            sListener.Start(4);
        }
        catch (Exception ex) { Console.WriteLine("[FnrNet] No se puede abrir el puerto " + Port + ": " + ex.Message); sListener = null; return false; }
        sUdp = UdpOpen(Port);
        sQuit = false; sMode = Mode.Host;
        lock (sLock) { sHostEvents.Clear(); Array.Clear(sPeers); }
        sConnThread = new Thread(HostThread) { IsBackground = true, Name = "FnrNet" }; sConnThread.Start();
        if (sUdp != null) { sUdpThread = new Thread(UdpThread) { IsBackground = true, Name = "FnrNetUdp" }; sUdpThread.Start(); }
        Inet.HostBegin();
        Console.WriteLine("[FnrNet] Partida creada en el puerto " + Port + " (IP local " + (LocalIp() ?? "ninguna") + ")");
        return true;
    }

    /// <summary>Servidor intermedio: una conexión de un invitado que ya está hecha.</summary>
    internal static void HostOffer(Socket s)
    {
        if (sMode != Mode.Host) { try { s.Close(); } catch { } return; }
        lock (sLock) { sOffered?.Close(); sOffered = s; }
    }

    static void HostThread()
    {
        var buf = new byte[4096];
        while (!sQuit)
        {
            Socket s = null;
            lock (sLock) { if (sOffered != null) { s = sOffered; sOffered = null; } }
            if (s == null)
            {
                try
                {
                    if (!sListener.Server.Poll(100_000, SelectMode.SelectRead)) continue;
                    s = sListener.AcceptSocket();
                }
                catch { if (sQuit) break; continue; }
            }
            try
            {
                Tune(s);
                s.ReceiveTimeout = 3000;
                int t = RecvMsg(s, ref buf, out int len);
                if (t != M_HOLA || len < 5 || Encoding.ASCII.GetString(buf, 0, 4) != "FNRN") { s.Close(); continue; }
                if (buf[4] != Version) { SendMsg(s, M_VERSION, new byte[] { Version }); s.Close(); continue; }
                Peer p = null;
                lock (sLock)
                {
                    for (int i = 0; i < MaxGuests; i++)
                        if (sPeers[i] == null) { p = sPeers[i] = new Peer { Slot = i, S = s }; break; }
                }
                if (p == null) { SendMsg(s, M_LLENA, default); s.Close(); Console.WriteLine("[FnrNet] Partida llena: rechazado " + s.RemoteEndPoint); continue; }
                SendMsg(s, M_BIENVENIDO, new byte[] { (byte)(p.Slot + 2) });
                s.ReceiveTimeout = 0;
                p.Recv = new Thread(() => PeerRecv(p)) { IsBackground = true, Name = "FnrNetIn" + p.Slot };
                p.Send = new Thread(() => PeerSend(p)) { IsBackground = true, Name = "FnrNetOut" + p.Slot };
                p.Recv.Start(); p.Send.Start();
                lock (sLock) sHostEvents.Enqueue((p.Slot + 2) * 10 + 1);
                Console.WriteLine($"[FnrNet] Ha entrado el jugador {p.Slot + 2} ({s.RemoteEndPoint})");
            }
            catch (Exception ex) { if (Log) Console.WriteLine("[FnrNet] conexión: " + ex.Message); try { s.Close(); } catch { } }
        }
    }

    static void PeerRecv(Peer p)
    {
        var buf = new byte[4096];
        try
        {
            int t;
            while (!sQuit && p.Alive && (t = RecvMsg(p.S, ref buf, out int len)) >= 0 && t != M_ADIOS)
            {
                if (t == M_ENTRADAS && len >= 2) p.Bits = buf[0] << 8 | buf[1];
                else if (t == M_PING && len == 4) PushTo(p, Packet(M_PONG, buf.AsSpan(0, 4)), false);
            }
        }
        catch (Exception ex) { if (Log) Console.WriteLine("[FnrNet] invitado " + (p.Slot + 2) + ": " + ex.Message); }
        DropPeer(p);
    }

    static void DropPeer(Peer p)
    {
        lock (sLock)
        {
            if (!p.Alive) return;
            p.Alive = false; p.Bits = 0;
            p.Queue.Clear(); p.QueueBytes = 0;
            if (sPeers[p.Slot] == p) sPeers[p.Slot] = null;
            if (!sQuit) sHostEvents.Enqueue((p.Slot + 2) * 10 + 2);
            Monitor.PulseAll(sLock);
        }
        try { p.S.Shutdown(SocketShutdown.Both); } catch { }
        try { p.S.Close(); } catch { }
        Console.WriteLine($"[FnrNet] El jugador {p.Slot + 2} se ha ido");
    }

    static byte[] Packet(byte type, ReadOnlySpan<byte> data)
    {
        var b = new byte[5 + data.Length];
        b[0] = type; Put32(b, 1, data.Length); data.CopyTo(b.AsSpan(5));
        return b;
    }

    static void PushTo(Peer p, byte[] pk, bool video)
    {
        lock (sLock)
        {
            if (!p.Alive) return;
            p.Queue.AddLast((pk, video)); p.QueueBytes += pk.Length;
            Monitor.PulseAll(sLock);
        }
    }

    static void PeerSend(Peer p)
    {
        while (true)
        {
            byte[] pk;
            lock (sLock)
            {
                while (p.Queue.Count == 0 && !sQuit && p.Alive) Monitor.Wait(sLock, 200);
                if (sQuit || !p.Alive) break;
                pk = p.Queue.First.Value.data; p.Queue.RemoveFirst(); p.QueueBytes -= pk.Length;
            }
            try { SendAll(p.S, pk); } catch { DropPeer(p); break; }
        }
    }

    /// <summary>
    /// Avisos para el anfitrión (se consumen): 10 * jugador + 1 = ha entrado, + 2 = se ha ido (jugador 2 o 3).
    /// 0 = nada.
    /// </summary>
    public static int HostEvent() { lock (sLock) return sHostEvents.Count > 0 ? sHostEvents.Dequeue() : 0; }

    /// <summary>Botones de un invitado (0 = jugador 2, 1 = jugador 3) como mando.</summary>
    public static bool GuestPad(int slot, out PadSnapshot p)
    {
        p = default;
        if (sMode != Mode.Host || slot < 0 || slot >= MaxGuests) return false;
        var peer = sPeers[slot];
        if (peer == null || !peer.Alive) return false;
        int b = peer.Bits;
        p.Connected = true; p.Name = "Jugador en red " + (slot + 2);
        p.DUp = (b & (int)Btn.Up) != 0; p.DDown = (b & (int)Btn.Down) != 0; p.DLeft = (b & (int)Btn.Left) != 0; p.DRight = (b & (int)Btn.Right) != 0;
        p.A = (b & (int)Btn.A) != 0; p.B = (b & (int)Btn.B) != 0; p.X = (b & (int)Btn.X) != 0; p.Y = (b & (int)Btn.Y) != 0;
        p.LB = (b & (int)Btn.LB) != 0; p.RB = (b & (int)Btn.RB) != 0; p.Start = (b & (int)Btn.Start) != 0; p.Back = (b & (int)Btn.Back) != 0;
        p.C = (b & (int)Btn.C) != 0; p.Z = (b & (int)Btn.Z) != 0;
        return true;
    }

    /// <summary>¿Quiere el anfitrión una imagen este fotograma? (para no leer la pantalla si no hay nadie).</summary>
    public static bool HostWantsFrame { get { if (sMode != Mode.Host) return false; foreach (var p in sPeers) if (p != null && p.Alive) return true; return false; } }

    // --- vídeo
    static long vStatFrames, vStatBytes, vStatMax, vStatKeys;

    // La compresión va en un hilo aparte (antes iba en el del juego y, con dos invitados, le robaba
    // tiempo al anfitrión). Si llegan imágenes más deprisa de lo que se comprimen, vale la última.
    static readonly object sEncLock = new();
    static byte[] sPend = new byte[FrameMax], sWork = new byte[FrameMax];
    static int sPendW, sPendH, sPendFlags;
    static bool sPendReady;
    static Thread sEncThread;
    static long sFrameId;
    // la última imagen comprimida, por si el otro invitado va justo al mismo paso (se reutiliza tal cual)
    static long sLastRefId = -2, sLastFrameId = -1; static bool sLastKey; static byte[] sLastPacket;

    /// <summary>Anfitrión: manda una imagen (RGB24, fila 0 arriba) a cada invitado.</summary>
    public static void HostFrame(byte[] rgb, int w, int h, int flags)
    {
        if (sMode != Mode.Host || w > MaxW || h > MaxH) return;
        lock (sEncLock)
        {
            Buffer.BlockCopy(rgb, 0, sPend, 0, w * h * 3);
            sPendW = w; sPendH = h; sPendFlags = flags; sPendReady = true;
            if (sEncThread == null || !sEncThread.IsAlive)
            {
                sEncThread = new Thread(EncodeLoop) { Name = "FnrNetVideo", IsBackground = true, Priority = ThreadPriority.AboveNormal };
                sEncThread.Start();
            }
            Monitor.Pulse(sEncLock);
        }
    }

    static void EncodeLoop()
    {
        while (true)
        {
            int w, h, flags;
            lock (sEncLock)
            {
                while (!sPendReady) { if (sMode != Mode.Host || sQuit) { sEncThread = null; return; } Monitor.Wait(sEncLock, 500); }
                (sPend, sWork) = (sWork, sPend);
                w = sPendW; h = sPendH; flags = sPendFlags; sPendReady = false;
            }
            try
            {
                long id = ++sFrameId;
                foreach (var peer in sPeers)
                    if (peer != null && peer.Alive) PeerFrame(peer, sWork, w, h, flags, id);
            }
            catch (Exception ex) { Console.WriteLine("[FnrNet] vídeo: " + ex.Message); }
        }
    }

    static void PeerFrame(Peer pr, byte[] rgb, int w, int h, int flags, long id)
    {
        bool key;
        lock (sLock)
        {
            if (pr.QueueBytes > QueueLimit)
            {
                // la red de este invitado no da abasto: fuera su vídeo atrasado
                var n = pr.Queue.First;
                while (n != null) { var nx = n.Next; if (n.Value.video) { pr.QueueBytes -= n.Value.data.Length; pr.Queue.Remove(n); } n = nx; }
                pr.ForceKey = true;
            }
            key = pr.ForceKey || w != pr.W || h != pr.H;
            // aún sale lo de antes: esta imagen no se manda (la siguiente va contra la última enviada)
            if (!key && pr.QueueBytes > SkipLimit) return;
            pr.ForceKey = false;
        }
        int len = w * h * 3, dx = 0, dy = 0;
        // el otro invitado ya ha comprimido esta misma imagen contra la misma de referencia: se reutiliza
        if (sLastPacket != null && sLastFrameId == id && sLastKey == key && (key || sLastRefId == pr.RefId))
        {
            Buffer.BlockCopy(rgb, 0, pr.Ref, 0, len); pr.W = w; pr.H = h; pr.RefId = id;
            PushTo(pr, sLastPacket, true);
            return;
        }
        long refId = pr.RefId;
        if (key) Buffer.BlockCopy(rgb, 0, pr.Tmp, 0, len);
        else { FindShift(rgb, pr.Ref, w, h, out dx, out dy); XorShift(pr.Tmp, rgb, pr.Ref, w, h, dx, dy); }
        Buffer.BlockCopy(rgb, 0, pr.Ref, 0, len); pr.W = w; pr.H = h; pr.RefId = id;
        var ms = new MemoryStream(len / 4 + 64);
        ms.Write(new byte[13]);
        using (var z = new ZLibStream(ms, CompressionLevel.Fastest, true)) z.Write(pr.Tmp, 0, len);
        var p = ms.ToArray();
        p[0] = M_VIDEO; Put32(p, 1, p.Length - 5);
        p[5] = (byte)(key ? 1 : 0); p[6] = (byte)(w >> 8); p[7] = (byte)w; p[8] = (byte)(h >> 8); p[9] = (byte)h;
        p[10] = (byte)(sbyte)dx; p[11] = (byte)(sbyte)dy; p[12] = (byte)flags;
        if (Log)
        {
            vStatFrames++; vStatBytes += p.Length; vStatMax = Math.Max(vStatMax, p.Length); if (key) vStatKeys++;
            if (vStatFrames % 300 == 0) { Console.WriteLine($"[FnrNet] vídeo: media {vStatBytes / 300} B, máx {vStatMax} B, claves {vStatKeys}"); vStatBytes = 0; vStatMax = 0; vStatKeys = 0; }
        }
        sLastPacket = p; sLastFrameId = id; sLastKey = key; sLastRefId = refId;
        PushTo(pr, p, true);
    }

    /// <summary>out = cur XOR ref desplazado (dx, dy); fuera de la imagen, ref vale 0. Sirve para codificar y para decodificar.</summary>
    static void XorShift(byte[] o, byte[] cur, byte[] rf, int w, int h, int dx, int dy)
    {
        int row = w * 3;
        for (int y = 0; y < h; y++)
        {
            int sy = y + dy, x0 = dx < 0 ? -dx : 0, x1 = dx > 0 ? w - dx : w, ro = y * row;
            if (sy < 0 || sy >= h || x0 >= x1) { Buffer.BlockCopy(cur, ro, o, ro, row); continue; }
            if (x0 > 0) Buffer.BlockCopy(cur, ro, o, ro, x0 * 3);
            int ri = (sy * w + x0 + dx) * 3, ci = ro + x0 * 3, n = (x1 - x0) * 3;
            for (int i = 0; i < n; i++) o[ci + i] = (byte)(cur[ci + i] ^ rf[ri + i]);
            if (x1 < w) Buffer.BlockCopy(cur, ro + x1 * 3, o, ro + x1 * 3, (w - x1) * 3);
        }
    }

    /// <summary>Busca cuánto se ha deslizado el fondo (rejilla de 1 de cada 8 puntos).</summary>
    static void FindShift(byte[] cur, byte[] rf, int w, int h, out int bdx, out int bdy)
    {
        int best = int.MaxValue, zero = 0; bdx = bdy = 0;
        for (int dy = -6; dy <= 6; dy++)
            for (int dx = -12; dx <= 12; dx++)
            {
                int bad = 0;
                for (int y = 12; y < h - 8 && bad < best; y += 8)
                {
                    int c = (y * w + 16) * 3, r = ((y + dy) * w + 16 + dx) * 3;
                    for (int x = 16; x < w - 16; x += 8, c += 24, r += 24)
                        if (cur[c] != rf[r] || cur[c + 1] != rf[r + 1] || cur[c + 2] != rf[r + 2]) bad++;
                }
                if (dx == 0 && dy == 0) zero = bad;
                if (bad < best) { best = bad; bdx = dx; bdy = dy; }
            }
        if (best * 10 > zero * 8) bdx = bdy = 0; // si no mejora al menos un 20 %, sin desplazar
    }

    // --- sonido
    static readonly Adpcm[] aEnc = { new(), new() };
    static short[] aHalf = new short[0];

    /// <summary>Anfitrión: lo que acaba de salir del mezclador (float estéreo intercalado).</summary>
    public static void HostAudio(float[] stereo, int frames, int rate)
    {
        if (!HostWantsFrame || frames < 2) return;
        int m = frames / 2;
        var p = new byte[5 + 10 + m];
        p[0] = M_AUDIO; Put32(p, 1, 10 + m);
        p[5] = (byte)(m >> 8); p[6] = (byte)m;
        p[7] = (byte)((rate / 2) >> 8); p[8] = (byte)(rate / 2);
        for (int c = 0; c < 2; c++) { p[9 + c * 3] = (byte)(aEnc[c].Pred >> 8); p[10 + c * 3] = (byte)aEnc[c].Pred; p[11 + c * 3] = (byte)aEnc[c].Idx; }
        for (int i = 0; i < m; i++)
        {
            int l = S16((stereo[i * 4] + stereo[i * 4 + 2]) * 0.5f), r = S16((stereo[i * 4 + 1] + stereo[i * 4 + 3]) * 0.5f);
            p[15 + i] = (byte)(aEnc[0].Encode(l) | aEnc[1].Encode(r) << 4);
        }
        foreach (var peer in sPeers) if (peer != null && peer.Alive) PushTo(peer, p, false);
    }

    static int S16(float v) => (int)Math.Clamp(v * 32767f, -32768f, 32767f);

    sealed class Adpcm
    {
        static readonly int[] Step = { 7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107,
            118, 130, 143, 157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796, 876, 963, 1060, 1166,
            1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484, 7132, 7845,
            8630, 9493, 10442, 11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767 };
        static readonly int[] IdxTab = { -1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8 };
        public int Pred, Idx;
        public int Decode(int code)
        {
            int step = Step[Idx], d = step >> 3;
            if ((code & 4) != 0) d += step;
            if ((code & 2) != 0) d += step >> 1;
            if ((code & 1) != 0) d += step >> 2;
            Pred += (code & 8) != 0 ? -d : d;
            Pred = Math.Clamp(Pred, -32768, 32767);
            Idx = Math.Clamp(Idx + IdxTab[code], 0, 88);
            return Pred;
        }
        public int Encode(int sample)
        {
            int step = Step[Idx], diff = sample - Pred, code = 0;
            if (diff < 0) { code = 8; diff = -diff; }
            if (diff >= step) { code |= 4; diff -= step; }
            if (diff >= step >> 1) { code |= 2; diff -= step >> 1; }
            if (diff >= step >> 2) code |= 1;
            Decode(code);
            return code;
        }
    }

    // ------------------------------------------------------------------ invitado

    /// <summary>Empieza a buscar partidas en la wifi.</summary>
    public static bool SearchStart()
    {
        Stop();
        sUdp = UdpOpen(Port + 1);
        if (sUdp == null) return false;
        sFoundIp = null; sQuit = false; sMode = Mode.Search;
        sUdpThread = new Thread(UdpThread) { IsBackground = true, Name = "FnrNetUdp" }; sUdpThread.Start();
        return true;
    }

    /// <summary>IP de la primera partida encontrada en la wifi (en los últimos 4 s), o null.</summary>
    public static string Found() { lock (sLock) return sFoundIp != null && Now - sFoundAt < 4000 ? sFoundIp : null; }

    // imagen y sonido recibidos
    static readonly byte[] gFrame = new byte[FrameMax], gRef = new byte[FrameMax], gTmp = new byte[FrameMax], gOut = new byte[FrameMax];
    static int gW, gH, gSeq, gSeen, gFlags;
    const int Ring = 1 << 16;
    static readonly short[] gRing = new short[Ring * 2];
    static int gRd, gWr, gRate = 24000;
    public static string LastError;
    /// <summary>Invitado: qué jugador es (2 o 3).</summary>
    public static int PlayerNumber = 2;

    /// <summary>Entra en una partida (IP o nombre, puerto). Devuelve false si no se ha podido.</summary>
    public static bool Join(string host, int port = Port)
    {
        Stop();
        LastError = null;
        Socket s = null;
        try
        {
            var addrs = Dns.GetHostAddresses(host);
            if (addrs.Length == 0) { LastError = Lang.T("No se encuentra ", "Cannot find ") + host; return false; }
            s = new Socket(addrs[0].AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            var ar = s.BeginConnect(addrs[0], port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(5000)) { s.Close(); LastError = Lang.T("No contesta (", "No answer (") + host + ":" + port + ")"; return false; }
            s.EndConnect(ar);
            Tune(s);
            var hello = new byte[] { (byte)'F', (byte)'N', (byte)'R', (byte)'N', Version };
            SendMsg(s, M_HOLA, hello);
            s.ReceiveTimeout = 5000;
            var buf = new byte[64];
            int t = RecvMsg(s, ref buf, out int blen);
            if (t == M_LLENA) { s.Close(); LastError = Lang.T("La partida ya tiene 3 jugadores.", "The game already has 3 players."); return false; }
            PlayerNumber = t == M_BIENVENIDO && blen >= 1 ? buf[0] : 2;
            if (t == M_VERSION) { s.Close(); LastError = Lang.T("El otro tiene otra versión de la app: actualizad los dos.", "The other console has a different version of the app: update both."); return false; }
            if (t != M_BIENVENIDO) { s.Close(); LastError = Lang.T("El anfitrión no ha contestado.", "The host did not answer."); return false; }
            s.ReceiveTimeout = 0;
        }
        catch (Exception ex) { try { s?.Close(); } catch { } LastError = ex.Message; return false; }
        sClient = s; sQuit = false; sMode = Mode.Guest; sConnected = true; sPingMs = -1;
        gSeq = gSeen = 0; gRd = gWr = 0;
        sConnThread = new Thread(GuestThread) { IsBackground = true, Name = "FnrNet" }; sConnThread.Start();
        Console.WriteLine("[FnrNet] Dentro de la partida de " + host + ":" + port);
        return true;
    }

    static void GuestThread()
    {
        var buf = new byte[FrameMax + 4096];
        var s = sClient;
        var dec = new[] { new Adpcm(), new Adpcm() };
        int t;
        try
        {
            while (!sQuit && (t = RecvMsg(s, ref buf, out int len)) >= 0 && t != M_FIN)
            {
                if (t == M_VIDEO && len > 8)
                {
                    bool key = buf[0] != 0;
                    int w = buf[1] << 8 | buf[2], h = buf[3] << 8 | buf[4], n = w * h * 3, dx = (sbyte)buf[5], dy = (sbyte)buf[6];
                    if (w < 1 || w > MaxW || h < 1 || h > MaxH) break;
                    using (var z = new ZLibStream(new MemoryStream(buf, 8, len - 8), CompressionMode.Decompress))
                    {
                        int got = 0, k;
                        while (got < n && (k = z.Read(gTmp, got, n - got)) > 0) got += k;
                        if (got != n) break;
                    }
                    if (key) Buffer.BlockCopy(gTmp, 0, gRef, 0, n);
                    else if (w != gW || h != gH) break;
                    else { XorShift(gOut, gTmp, gRef, w, h, dx, dy); Buffer.BlockCopy(gOut, 0, gRef, 0, n); }
                    lock (sLock) { Buffer.BlockCopy(gRef, 0, gFrame, 0, n); gW = w; gH = h; gFlags = buf[7]; gSeq++; }
                }
                else if (t == M_AUDIO && len >= 10)
                {
                    int m = buf[0] << 8 | buf[1], rate = buf[2] << 8 | buf[3];
                    if (len < 10 + m) continue;
                    for (int c = 0; c < 2; c++) { dec[c].Pred = (short)(buf[4 + c * 3] << 8 | buf[5 + c * 3]); dec[c].Idx = Math.Min(88, (int)buf[6 + c * 3]); }
                    lock (sLock)
                    {
                        if (rate > 0) gRate = rate;
                        for (int i = 0; i < m; i++)
                        {
                            int wi = gWr & (Ring - 1);
                            gRing[wi * 2] = (short)dec[0].Decode(buf[10 + i] & 15);
                            gRing[wi * 2 + 1] = (short)dec[1].Decode(buf[10 + i] >> 4);
                            gWr++;
                            if (gWr - gRd > Ring) gRd = gWr - Ring;
                        }
                    }
                }
                else if (t == M_PONG && len == 4) sPingMs = (int)(Now - (uint)Get32(buf, 0));
            }
        }
        catch (Exception ex) { if (Log) Console.WriteLine("[FnrNet] invitado: " + ex.Message); }
        sConnected = false;
        Console.WriteLine("[FnrNet] Fuera de la partida");
    }

    static int sLastBits = -1; static long sBitsAt, sPingAt;

    /// <summary>Invitado: manda los botones (al cambiar y cada 250 ms) y un ping cada segundo.</summary>
    public static void GuestInputs(int bits)
    {
        if (sMode != Mode.Guest || !sConnected) return;
        long now = Now;
        try
        {
            if (bits != sLastBits || now - sBitsAt > 250)
            {
                SendMsg(sClient, M_ENTRADAS, new[] { (byte)(bits >> 8), (byte)bits });
                sLastBits = bits; sBitsAt = now;
            }
            if (now - sPingAt > 1000)
            {
                var m = new byte[4]; Put32(m, 0, (int)now);
                SendMsg(sClient, M_PING, m); sPingAt = now;
            }
        }
        catch { sConnected = false; }
    }

    /// <summary>Invitado: copia la última imagen recibida (RGB24) si es nueva.</summary>
    public static bool GuestFrame(byte[] rgb, out int w, out int h)
    {
        lock (sLock)
        {
            w = gW; h = gH;
            if (gSeq == gSeen || w == 0) return false;
            Buffer.BlockCopy(gFrame, 0, rgb, 0, w * h * 3);
            gSeen = gSeq;
            return true;
        }
    }

    /// <summary>Invitado: frecuencia del sonido que llega.</summary>
    public static int GuestAudioRate { get { lock (sLock) return gRate; } }
    /// <summary>Invitado: muestras estéreo pendientes.</summary>
    public static int GuestAudioAvailable { get { lock (sLock) return gWr - gRd; } }

    /// <summary>Invitado: saca hasta <paramref name="max"/> muestras estéreo (16 bits). Devuelve cuántas.</summary>
    public static int GuestAudio(short[] o, int max)
    {
        lock (sLock)
        {
            int n = Math.Min(max, gWr - gRd);
            for (int i = 0; i < n; i++, gRd++) { int r = gRd & (Ring - 1); o[i * 2] = gRing[r * 2]; o[i * 2 + 1] = gRing[r * 2 + 1]; }
            return n;
        }
    }

    /// <summary>Invitado: tira el sonido acumulado de más (si se ha ido retrasando).</summary>
    public static void GuestAudioTrim(int keep)
    {
        lock (sLock) if (gWr - gRd > keep) gRd = gWr - keep;
    }

    // ------------------------------------------------------------------ común

    public static void Stop()
    {
        var mode = sMode;
        if (mode == Mode.Off && sListener == null && sUdp == null && sClient == null) return;
        if (mode == Mode.Host) Inet.HostEnd();
        try { if (mode == Mode.Guest && sClient != null && sConnected) SendMsg(sClient, M_ADIOS, default); } catch { }
        Peer[] peers;
        lock (sLock) peers = (Peer[])sPeers.Clone();
        foreach (var pr in peers)
            if (pr != null && pr.Alive) { try { SendMsg(pr.S, M_FIN, default); } catch { } }
        sQuit = true;
        lock (sLock) Monitor.PulseAll(sLock);
        try { sClient?.Shutdown(SocketShutdown.Both); } catch { }
        try { sListener?.Stop(); } catch { }
        try { sUdp?.Close(); } catch { }
        foreach (var pr in peers)
            if (pr != null) { try { pr.S.Shutdown(SocketShutdown.Both); } catch { } try { pr.S.Close(); } catch { } pr.Alive = false; pr.Send?.Join(1000); pr.Recv?.Join(1000); }
        sConnThread?.Join(2000); sSendThread?.Join(2000); sUdpThread?.Join(2000);
        sConnThread = sSendThread = sUdpThread = null;
        CloseQuiet(ref sClient);
        lock (sLock) { Array.Clear(sPeers); sHostEvents.Clear(); sOffered?.Close(); sOffered = null; }
        sListener = null; sUdp = null;
        sConnected = false; sMode = Mode.Off;
    }

    /// <summary>Texto de estado para el anfitrión (encima del juego).</summary>
    public static string HostStatus()
    {
        if (sMode != Mode.Host) return "";
        if (GuestCount > 0) return "";
        string ip = LocalIp();
        var sb = new StringBuilder(Lang.T("JUEGO EN RED: esperando invitados (hasta 2)", "NETPLAY: waiting for guests (up to 2)"));
        sb.Append(ip != null ? "  ·  Wifi: " + ip : Lang.T("  ·  sin wifi", "  ·  no wifi"));
        string inet = Inet.StatusText();
        if (inet.Length > 0) sb.Append("\n").Append(inet);
        return sb.ToString();
    }
}
