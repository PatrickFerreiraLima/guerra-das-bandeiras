using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using GuerraDasBandeiras.Gameplay;

namespace GuerraDasBandeiras.Net;

/// <summary>Constantes e utilidades da partida em rede.</summary>
public static class NetProto
{
    /// <summary>Muda quando o protocolo ou a simulação mudam de um jeito incompatível.</summary>
    public const int Version = 2;
    /// <summary>Porta TCP da sala (o anfitrião escuta aqui; libere no roteador para jogar pela internet).</summary>
    public const int GamePort = 27027;
    /// <summary>Porta UDP dos anúncios de sala na rede local.</summary>
    public const int DiscoveryPort = 27026;
    const string BeaconTag = "GDB-SALA";

    /// <summary>
    /// "Impressão digital" do balanceamento: as duas máquinas precisam dos mesmos números (balance.json),
    /// senão as simulações divergem. Comparada no aperto de mão.
    /// </summary>
    public static string BalanceHash()
    {
        try
        {
            var bytes = File.ReadAllBytes(Path.Combine(Core.Assets.Root, "balance.json"));
            uint h = 2166136261;
            foreach (var b in bytes) if (b != '\r') h = unchecked((h ^ b) * 16777619);   // ignora diferença de quebra de linha
            return h.ToString("x8");
        }
        catch { return "sem-arquivo"; }
    }

    /// <summary>IPv4 desta máquina (para o anfitrião passar ao amigo), sem os de loopback.</summary>
    public static List<string> LocalAddresses()
    {
        var list = new List<string>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork) list.Add(ua.Address.ToString());
            }
        }
        catch { }
        return list.Distinct().ToList();
    }

    /// <summary>Endereços de broadcast de cada rede (Wi-Fi, cabo, VPNs como Radmin/ZeroTier) + o global.</summary>
    static List<IPEndPoint> BroadcastTargets()
    {
        var set = new HashSet<string> { IPAddress.Broadcast.ToString(), IPAddress.Loopback.ToString() };
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask == null) continue;
                    var ip = ua.Address.GetAddressBytes();
                    var mask = ua.IPv4Mask.GetAddressBytes();
                    var bc = new byte[4];
                    for (int i = 0; i < 4; i++) bc[i] = (byte)(ip[i] | ~mask[i]);
                    set.Add(new IPAddress(bc).ToString());
                }
            }
        }
        catch { }
        return set.Select(s => new IPEndPoint(IPAddress.Parse(s), DiscoveryPort)).ToList();
    }

    public static byte[] BeaconBytes(int roomId, string name, FactionId faction, int port) =>
        Encoding.UTF8.GetBytes($"{BeaconTag}|{Version}|{roomId}|{port}|{(int)faction}|{name.Replace('|', '/')}");

    public static RoomInfo ParseBeacon(byte[] data, IPAddress from)
    {
        try
        {
            var parts = Encoding.UTF8.GetString(data).Split('|');
            if (parts.Length < 2 || parts[0] != BeaconTag) return null;
            int ver = int.Parse(parts[1]);
            // sala de outra versão: aparece na lista como incompatível
            if (ver != Version || parts.Length < 6) return new RoomInfo { Version = ver, Id = from.GetHashCode(), Name = "(outra versão do jogo)", Address = from.ToString(), LastSeen = Environment.TickCount64 };
            return new RoomInfo
            {
                Version = ver, Id = int.Parse(parts[2]), Port = int.Parse(parts[3]), Faction = (FactionId)int.Parse(parts[4]),
                Name = string.Join("|", parts.Skip(5)), Address = from.ToString(), LastSeen = Environment.TickCount64,
            };
        }
        catch { return null; }
    }

    internal static void SendBeacon(UdpClient udp, byte[] data)
    {
        foreach (var ep in BroadcastTargets())
            try { udp.Send(data, data.Length, ep); } catch { }
    }

    /// <summary>
    /// Lê o que a pessoa digitou para entrar: código da sala ("K7F2-9QXA"), "192.168.0.10", "192.168.0.10:27027"
    /// ou o nome de uma máquina.
    /// </summary>
    public static bool TryParseAddress(string text, out string host, out int port)
    {
        host = null; port = GamePort;
        text = (text ?? "").Trim();
        if (text.Length == 0) return false;
        if (TryDecodeCode(text, out host, out port)) return true;
        int colon = text.LastIndexOf(':');
        if (colon > 0 && int.TryParse(text[(colon + 1)..], out int p)) { port = p; text = text[..colon]; }
        host = text;
        return port > 0 && port < 65536;
    }

    // ---------------- código da sala ----------------
    // 8 letras/números = IP (4 bytes) + porta (1 byte, a partir de 27027), em base 32 sem letras ambíguas (sem I, L, O, U).
    const string CodeAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>Código curto da sala para passar ao amigo (em vez do IP).</summary>
    public static string EncodeCode(IPAddress ip, int port)
    {
        var b = ip.GetAddressBytes();
        ulong v = ((ulong)b[0] << 32) | ((ulong)b[1] << 24) | ((ulong)b[2] << 16) | ((ulong)b[3] << 8) | (byte)(port - GamePort);
        var sb = new StringBuilder();
        for (int i = 7; i >= 0; i--) sb.Append(CodeAlphabet[(int)((v >> (i * 5)) & 31)]);
        return sb.ToString(0, 4) + "-" + sb.ToString(4, 4);
    }

    public static bool TryDecodeCode(string text, out string host, out int port)
    {
        host = null; port = GamePort;
        var s = new string(text.ToUpperInvariant().Where(c => c != '-' && c != ' ').ToArray())
            .Replace('O', '0').Replace('I', '1').Replace('L', '1');
        if (s.Length != 8 || text.Contains('.') || text.Contains(':')) return false;
        ulong v = 0;
        foreach (var c in s)
        {
            int k = CodeAlphabet.IndexOf(c);
            if (k < 0) return false;
            v = (v << 5) | (uint)k;
        }
        host = $"{(v >> 32) & 255}.{(v >> 24) & 255}.{(v >> 16) & 255}.{(v >> 8) & 255}";
        port = GamePort + (int)(v & 255);
        return true;
    }
}

/// <summary>Sala encontrada na rede local.</summary>
public class RoomInfo
{
    public string Name, Address;
    public int Port, Version, Id;
    public FactionId Faction;
    public long LastSeen;
    public bool Compatible => Version == NetProto.Version;
}

/// <summary>
/// Sala aberta por quem hospeda. Escuta conexões TCP, anuncia a sala na rede local a cada segundo e tenta
/// abrir a porta no roteador (UPnP) para quem está pela internet. Quando a partida começa ela para de se
/// anunciar, mas continua escutando: é por ela que o adversário volta se a conexão cair.
/// Dispose fecha tudo (inclusive a porta no roteador).
/// </summary>
public sealed class RoomHost : IDisposable
{
    readonly TcpListener _listener;
    readonly UdpClient _udp;
    readonly byte[] _beacon;
    long _lastBeacon;
    bool _announce = true;
    public readonly int Port;
    public readonly int RoomId = Random.Shared.Next(1, int.MaxValue);
    public string Error;
    /// <summary>Abertura automática da porta no roteador (pode falhar: CGNAT, UPnP desligado...).</summary>
    public readonly Upnp Upnp;

    public RoomHost(string name, FactionId faction, bool upnp = true)
    {
        // se a porta padrão estiver ocupada (ex.: duas cópias do jogo no mesmo PC), tenta as seguintes
        for (int p = NetProto.GamePort; p < NetProto.GamePort + 10; p++)
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, p);
                _listener.Start(4);
                Port = p;
                break;
            }
            catch (SocketException) { _listener = null; }
        }
        if (_listener == null) { Error = "Não foi possível abrir a sala: as portas 27027-27036 estão ocupadas."; return; }
        try { _udp = new UdpClient { EnableBroadcast = true }; } catch { _udp = null; }
        _beacon = NetProto.BeaconBytes(RoomId, name, faction, Port);
        if (upnp) Upnp = Upnp.Start(Port);
    }

    /// <summary>Código para quem está pela internet (só existe quando o roteador abriu a porta).</summary>
    public string InternetCode => Upnp != null && Upnp.InternetReady && IPAddress.TryParse(Upnp.ExternalIp, out var ip) ? NetProto.EncodeCode(ip, Port) : null;

    /// <summary>A partida começou: a sala some da lista, mas segue escutando para reconexões.</summary>
    public void StopAnnouncing() => _announce = false;

    /// <summary>Chamar todo quadro. Devolve a conexão quando alguém bate na porta.</summary>
    public NetPeer Poll()
    {
        if (_listener == null) return null;
        long now = Environment.TickCount64;
        if (_announce && _udp != null && now - _lastBeacon > 1000) { _lastBeacon = now; NetProto.SendBeacon(_udp, _beacon); }
        try
        {
            if (_listener.Pending()) return new NetPeer(_listener.AcceptTcpClient());
        }
        catch (Exception e) { Error = "Erro na sala: " + e.Message; }
        return null;
    }

    public void Dispose()
    {
        try { _listener?.Stop(); } catch { }
        try { _udp?.Close(); } catch { }
        var u = Upnp;
        if (u != null) Task.Run(u.Remove);
    }
}

/// <summary>Escuta os anúncios de sala na rede local e mantém a lista (salas somem após 4 s sem anúncio).</summary>
public sealed class RoomBrowser : IDisposable
{
    readonly UdpClient _udp;
    readonly Dictionary<string, RoomInfo> _rooms = new();
    readonly object _lock = new();
    volatile bool _closed;
    public string Error;

    public RoomBrowser()
    {
        try
        {
            _udp = new UdpClient(AddressFamily.InterNetwork);
            // permite mais de uma cópia do jogo ouvindo no mesmo PC
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, NetProto.DiscoveryPort));
            new Thread(Loop) { IsBackground = true, Name = "rede-salas" }.Start();
        }
        catch (Exception e) { Error = "Busca de salas indisponível: " + e.Message; }
    }

    void Loop()
    {
        var any = new IPEndPoint(IPAddress.Any, 0);
        while (!_closed)
        {
            try
            {
                var data = _udp.Receive(ref any);
                var room = NetProto.ParseBeacon(data, any.Address);
                if (room == null) continue;
                lock (_lock) _rooms[$"{room.Id}@{room.Address}"] = room;
            }
            catch { if (_closed) return; }
        }
    }

    public List<RoomInfo> Rooms
    {
        get
        {
            long now = Environment.TickCount64;
            lock (_lock)
            {
                foreach (var k in _rooms.Where(kv => now - kv.Value.LastSeen > 4000).Select(kv => kv.Key).ToList()) _rooms.Remove(k);
                // a mesma sala pode chegar por mais de uma rede (Wi-Fi, VPN, loopback): mostra uma vez só.
                // Salas diferentes com o mesmo nome aparecem separadas (cada sala tem um Id sorteado).
                return _rooms.Values.GroupBy(r => r.Id).Select(g => g.OrderBy(r => r.Address == "127.0.0.1" ? 1 : 0).First())
                    .OrderBy(r => r.Name).ToList();
            }
        }
    }

    public void Dispose()
    {
        _closed = true;
        try { _udp?.Close(); } catch { }
    }
}
