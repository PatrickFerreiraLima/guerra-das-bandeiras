using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

namespace GuerraDasBandeiras.Net;

/// <summary>
/// Abre a porta da sala no roteador sozinho, via UPnP (o "abrir porta automaticamente" que a maioria dos
/// roteadores domésticos tem). Assim quem está em outra casa consegue entrar sem ninguém configurar nada.
/// Tudo roda em segundo plano; <see cref="Status"/> diz em palavras simples o que aconteceu.
/// Limite: com CGNAT (a operadora divide um IP entre vários clientes) nenhuma porta pode ser aberta.
/// </summary>
public sealed class Upnp
{
    public volatile string Status = "Abrindo a porta no roteador…";
    public volatile string ExternalIp;
    /// <summary>A porta foi aberta e o IP externo é público: dá para jogar pela internet com o código da sala.</summary>
    public volatile bool InternetReady;
    public volatile bool Done;

    readonly int _port;
    string _controlUrl, _serviceType, _localIp;
    bool _mapped;
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(4) };
    static readonly List<Upnp> Active = new();

    static Upnp()
    {
        // se o jogo fechar de repente, tenta fechar as portas abertas
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { lock (Active) foreach (var u in Active.ToList()) u.Remove(); };
    }

    Upnp(int port) { _port = port; }

    public static Upnp Start(int port)
    {
        var u = new Upnp(port);
        Task.Run(u.Run);
        return u;
    }

    async Task Run()
    {
        try
        {
            var location = await Discover();
            if (location == null) { Status = "O roteador não respondeu ao UPnP (desligado ou não suportado)."; return; }
            await FindService(location);
            if (_controlUrl == null) { Status = "O roteador não oferece abertura automática de porta."; return; }
            _localIp = LocalIpTowards(new Uri(location).Host);

            string ext = null;
            try
            {
                var r = await Soap("GetExternalIPAddress", "");
                ext = Tag(r, "NewExternalIPAddress");
            }
            catch { }
            ExternalIp = ext;

            string args =
                "<NewRemoteHost></NewRemoteHost>" +
                $"<NewExternalPort>{_port}</NewExternalPort><NewProtocol>TCP</NewProtocol>" +
                $"<NewInternalPort>{_port}</NewInternalPort><NewInternalClient>{_localIp}</NewInternalClient>" +
                "<NewEnabled>1</NewEnabled><NewPortMappingDescription>Guerra das Bandeiras</NewPortMappingDescription>";
            try { await Soap("AddPortMapping", args + "<NewLeaseDuration>0</NewLeaseDuration>"); }
            catch { await Soap("AddPortMapping", args + "<NewLeaseDuration>7200</NewLeaseDuration>"); } // alguns roteadores exigem prazo
            _mapped = true;
            lock (Active) Active.Add(this);

            if (ext == null || !IPAddress.TryParse(ext, out var ip))
                Status = "Porta aberta, mas o roteador não informou o IP da internet.";
            else if (IsPrivateOrCgnat(ip))
                Status = "Sua operadora usa CGNAT: pela internet, só com VPN de jogos (Radmin/ZeroTier). Na rede local funciona normal.";
            else
            {
                InternetReady = true;
                Status = "Porta aberta no roteador: pronto para jogar pela internet.";
            }
        }
        catch (Exception e)
        {
            Status = "O roteador recusou abrir a porta (" + Short(e.Message) + ").";
        }
        finally { Done = true; }
    }

    static string Short(string s) => s.Length > 60 ? s[..60] + "…" : s;

    public static bool IsPrivateOrCgnat(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        if (b.Length != 4) return true;
        return b[0] == 10 || b[0] == 127 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168)
            || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) || b[0] == 0 || (b[0] == 169 && b[1] == 254);
    }

    static async Task<string> Discover()
    {
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        var dest = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
        foreach (var st in new[] { "urn:schemas-upnp-org:device:InternetGatewayDevice:1", "urn:schemas-upnp-org:device:InternetGatewayDevice:2",
                                   "urn:schemas-upnp-org:service:WANIPConnection:1", "urn:schemas-upnp-org:service:WANPPPConnection:1" })
        {
            var msg = Encoding.ASCII.GetBytes($"M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nST: {st}\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\n\r\n");
            await udp.SendAsync(msg, msg.Length, dest);
        }
        var until = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < until)
        {
            var recv = udp.ReceiveAsync();
            if (await Task.WhenAny(recv, Task.Delay(until - DateTime.UtcNow)) != recv) break;
            var text = Encoding.ASCII.GetString(recv.Result.Buffer);
            foreach (var line in text.Split("\r\n"))
                if (line.StartsWith("LOCATION:", StringComparison.OrdinalIgnoreCase)) return line[9..].Trim();
        }
        return null;
    }

    async Task FindService(string location)
    {
        var xml = XDocument.Parse(await Http.GetStringAsync(location));
        var baseUrl = xml.Descendants().FirstOrDefault(e => e.Name.LocalName == "URLBase")?.Value;
        var root = new Uri(string.IsNullOrWhiteSpace(baseUrl) ? location : baseUrl);
        foreach (var svc in xml.Descendants().Where(e => e.Name.LocalName == "service"))
        {
            var type = svc.Elements().FirstOrDefault(e => e.Name.LocalName == "serviceType")?.Value ?? "";
            if (!type.Contains("WANIPConnection") && !type.Contains("WANPPPConnection")) continue;
            var ctrl = svc.Elements().FirstOrDefault(e => e.Name.LocalName == "controlURL")?.Value;
            if (ctrl == null) continue;
            _serviceType = type;
            _controlUrl = new Uri(root, ctrl).ToString();
            return;
        }
    }

    async Task<string> Soap(string action, string args)
    {
        string body =
            "<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">" +
            $"<s:Body><u:{action} xmlns:u=\"{_serviceType}\">{args}</u:{action}></s:Body></s:Envelope>";
        using var req = new HttpRequestMessage(HttpMethod.Post, _controlUrl) { Content = new StringContent(body, Encoding.UTF8, "text/xml") };
        req.Headers.TryAddWithoutValidation("SOAPAction", $"\"{_serviceType}#{action}\"");
        using var res = await Http.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode) throw new Exception(Tag(text, "errorDescription") ?? res.StatusCode.ToString());
        return text;
    }

    static string Tag(string xml, string name)
    {
        int i = xml.IndexOf("<" + name, StringComparison.Ordinal);
        if (i < 0) return null;
        i = xml.IndexOf('>', i) + 1;
        int j = xml.IndexOf("</" + name, i, StringComparison.Ordinal);
        return j > i ? xml[i..j].Trim() : null;
    }

    /// <summary>IP desta máquina na rede que leva ao roteador (é para ele que a porta é redirecionada).</summary>
    static string LocalIpTowards(string host)
    {
        using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        s.Connect(host, 1900);
        return ((IPEndPoint)s.LocalEndPoint).Address.ToString();
    }

    /// <summary>Fecha a porta no roteador (ao fim da partida ou ao fechar a sala).</summary>
    public void Remove()
    {
        if (!_mapped) return;
        _mapped = false;
        lock (Active) Active.Remove(this);
        try
        {
            Soap("DeletePortMapping", $"<NewRemoteHost></NewRemoteHost><NewExternalPort>{_port}</NewExternalPort><NewProtocol>TCP</NewProtocol>")
                .Wait(TimeSpan.FromSeconds(2));
        }
        catch { }
    }
}
