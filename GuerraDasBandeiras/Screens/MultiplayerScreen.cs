using System.Net.Sockets;
using GuerraDasBandeiras.Core;
using GuerraDasBandeiras.Gameplay;
using GuerraDasBandeiras.Net;

namespace GuerraDasBandeiras.Screens;

/// <summary>
/// Saguão do 1x1 em rede, sem servidor: quem cria a sala é o anfitrião (o jogo dele escuta conexões e anuncia
/// a sala na rede local); o outro jogador vê a sala na lista ou digita o IP do anfitrião. Ao entrar, os dois
/// trocam nome, facção e versão; o anfitrião sorteia o mapa e a partida começa nas duas máquinas.
/// A sala fecha quando o anfitrião sai desta tela ou quando a partida começa (1x1: um adversário só).
/// </summary>
public class MultiplayerScreen : Screen
{
    public override float LayoutScale => FitScale(1000, 720);

    // lembrados entre uma visita e outra a esta tela
    static string _name = "";   // a pessoa preenche; vira também o nome da sala (pode repetir)
    static FactionId _faction = FactionId.Amazonas;
    static string _ipText = "";

    readonly List<UiButton> _buttons = new();
    readonly RoomBrowser _browser = new();
    RoomHost _host;
    NetPeer _peer;                       // conexão em negociação (antes de a partida começar)
    bool _peerIsGuest;                   // true = nós entramos na sala de alguém
    Task<TcpClient> _connecting;
    CancellationTokenSource _connectCts;
    string _connectingTo, _joinHost;
    int _joinPort;
    string _status = "", _error = "";
    float _dt, _time;
    int _focus;                          // 0 nenhum, 1 nome, 2 IP
    List<string> _myIps = NetProto.LocalAddresses();

    public MultiplayerScreen() { Ambience.Stop(); }

    float Top => Math.Max(0, (Ui.Y - 700) / 2f);
    float Cx => Ui.X / 2f;
    Rectangle NameField => new((int)Cx - 470 + 120, (int)Top + 86, 260, 38);
    Rectangle FactionRect(int i) => new((int)Cx + 20 + i * 152, (int)Top + 80, 146, 50);
    Rectangle HostPanel => new((int)Cx - 470, (int)Top + 150, 456, 380);
    Rectangle JoinPanel => new((int)Cx + 14, (int)Top + 150, 456, 380);
    Rectangle IpField => new(JoinPanel.X + 20, JoinPanel.Bottom - 62, 290, 40);
    bool Busy => _host != null || _peer != null || _connecting != null;

    // ------------------------------------------------------------------ lógica

    public override void Update(float dt)
    {
        _dt = dt; _time += dt;
        HandleTyping();
        Build();
        Gui.Handle(_buttons);
        if (Input.LeftPressed)
        {
            var m = Input.UiMousePt;
            // clicar num campo dá foco a ele; clicar num botão mantém o foco; clicar fora tira
            _focus = NameField.Contains(m) && _host == null ? 1 : IpField.Contains(m) && !Busy ? 2 : _buttons.Any(b => b.Hover) ? _focus : 0;
        }
        Input.TextCapture = _focus != 0;   // digitando: Esc e letras não viram atalhos

        // anfitrião: alguém bateu na porta?
        if (_host != null && _peer == null)
        {
            var p = _host.Poll();
            if (_host.Error != null) { _error = _host.Error; CloseRoom(); }
            else if (p != null) { _peer = p; _peerIsGuest = false; _status = $"Conectando com {p.Remote}…"; Sfx.Select(); }
        }

        // convidado: a tentativa de conexão terminou?
        if (_connecting != null && _connecting.IsCompleted)
        {
            if (_connecting.Status == TaskStatus.RanToCompletion)
            {
                _peer = new NetPeer(_connecting.Result);
                _peerIsGuest = true;
                _status = "Conectado. Aguardando o anfitrião…";
                using var ms = new MemoryStream();
                using (var bw = new BinaryWriter(ms)) { bw.Write(NetProto.Version); bw.Write(NetProto.BalanceHash()); bw.Write(_name); bw.Write((byte)_faction); }
                _peer.Send(Msg.Hello, ms.ToArray());
            }
            else
            {
                _error = $"Não foi possível conectar em {_connectingTo}. Confira o IP, se a sala está aberta e se o firewall/roteador libera a porta {NetProto.GamePort}.";
                _status = "";
            }
            _connecting = null;
        }

        if (_peer != null) Handshake();
    }

    void Handshake()
    {
        _peer.Service();
        while (_peer != null && _peer.TryReceive(out var type, out var data))
        {
            using var br = new BinaryReader(new MemoryStream(data));
            try
            {
                if (!_peerIsGuest && type == Msg.Hello)
                {
                    int ver = br.ReadInt32(); string bal = br.ReadString(); string guestName = Clean(br.ReadString()); var guestFac = (FactionId)br.ReadByte();
                    string problem = ver != NetProto.Version ? "Versões diferentes do jogo. Os dois precisam usar o mesmo executável."
                        : bal != NetProto.BalanceHash() ? "O balanceamento (balance.json) é diferente entre os dois jogos."
                        : null;
                    if (problem != null)
                    {
                        using var rm = new MemoryStream();
                        using (var bw = new BinaryWriter(rm)) bw.Write(problem);
                        _peer.Send(Msg.Reject, rm.ToArray());
                        _peer.Close("recusado");
                        _peer = null;
                        _error = $"{guestName} tentou entrar, mas: {problem}";
                        _status = "Sala aberta: aguardando adversário…";
                        return;
                    }
                    // o anfitrião sorteia o mapa e manda tudo o que a outra máquina precisa para montar o mesmo mundo
                    int seed = new Random().Next(1, int.MaxValue);
                    int session = new Random().Next(1, int.MaxValue);   // "senha" desta partida, usada para reconectar
                    using var sm = new MemoryStream();
                    using (var bw = new BinaryWriter(sm)) { bw.Write(seed); bw.Write(session); bw.Write(_name); bw.Write((byte)_faction); bw.Write(guestName); bw.Write((byte)guestFac); }
                    _peer.Send(Msg.Start, sm.ToArray());
                    StartGame(_peer, host: true, seed, session, _name, _faction, guestName, guestFac);
                    return;
                }
                if (_peerIsGuest && type == Msg.Reject)
                {
                    _error = "O anfitrião recusou: " + br.ReadString();
                    _peer.Close("recusado"); _peer = null; _status = "";
                    return;
                }
                if (_peerIsGuest && type == Msg.Start)
                {
                    int seed = br.ReadInt32(); int session = br.ReadInt32(); string hostName = Clean(br.ReadString()); var hostFac = (FactionId)br.ReadByte();
                    string myName = Clean(br.ReadString()); var myFac = (FactionId)br.ReadByte();
                    StartGame(_peer, host: false, seed, session, hostName, hostFac, myName, myFac);
                    return;
                }
                if (type == Msg.Bye) { _peer.Close("O outro jogador saiu."); }
            }
            catch (Exception) { _error = "Resposta inválida do outro jogo."; _peer.Close("erro"); }
        }
        if (_peer != null && _peer.Closed)
        {
            _error = _peerIsGuest ? "A conexão com a sala caiu: " + _peer.CloseReason : "O adversário desconectou antes de começar.";
            _peer = null;
            _status = _host != null ? "Sala aberta: aguardando adversário…" : "";
        }
    }

    static string Clean(string s)
    {
        s = new string((s ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (s.Length == 0) s = "Jogador";
        return s.Length > 16 ? s[..16] : s;
    }

    void StartGame(NetPeer peer, bool host, int seed, int session, string hostName, FactionId hostFac, string guestName, FactionId guestFac)
    {
        // a sala sai da lista (1x1) e a busca para. O anfitrião continua escutando na porta da sala:
        // é por ela que o convidado volta se a conexão cair no meio da partida.
        var link = new NetLink { SessionId = session };
        if (host) { _host.StopAnnouncing(); link.Room = _host; _host = null; }
        else { link.HostAddress = _joinHost; link.HostPort = _joinPort; }
        _browser.Dispose();
        var cfg = new GameConfig
        {
            Mode = GameMode.Versus, PlayerFaction = hostFac, EnemyFaction = guestFac, Seed = seed, Difficulty = 1,
            LocalTeam = host ? 0 : 1, Names = new[] { hostName, guestName },
        };
        Sfx.StartGame();
        Game.SetScreen(new GameScreen(cfg, peer, link));
    }

    // ---- automação (capturas de teste com dois jogos abertos: cenários net_host / net_join do ShotRunner) ----
    public bool IsBusy => Busy;
    public void AutoHost(FactionId f, string name) { _faction = f; _name = name; OpenRoom(); }
    public void AutoJoin(FactionId f, string name, string host, int port) { _faction = f; _name = name; Join(host, port); }

    void OpenRoom()
    {
        _error = "";
        // GB_NO_UPNP=1: não mexe no roteador (testes automáticos)
        _host = new RoomHost(_name, _faction, upnp: Environment.GetEnvironmentVariable("GB_NO_UPNP") != "1");
        if (_host.Error != null) { _error = _host.Error; _host.Dispose(); _host = null; return; }
        _myIps = NetProto.LocalAddresses();
        _status = "Sala aberta: aguardando adversário…";
        Sfx.Click();
    }

    void CloseRoom()
    {
        _peer?.Leave(); _peer = null;
        _host?.Dispose(); _host = null;
        _status = "";
    }

    void Join(string host, int port)
    {
        if (Busy) return;
        if (_name.Trim().Length == 0) { _error = "Digite seu nome antes de entrar numa sala."; _focus = 1; Sfx.Error(); return; }
        _name = Clean(_name);
        _error = "";
        _connectingTo = port == NetProto.GamePort ? host : $"{host}:{port}";
        _joinHost = host; _joinPort = port;
        _status = $"Conectando em {_connectingTo}…";
        _connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        var cts = _connectCts;
        _connecting = Task.Run(async () =>
        {
            var c = new TcpClient();
            try { await c.ConnectAsync(host, port, cts.Token); return c; }
            catch { c.Dispose(); throw; }
        });
        Sfx.Click();
    }

    void CancelJoin()
    {
        _connectCts?.Cancel();
        _connecting = null;
        _peer?.Leave(); _peer = null;
        _status = "";
    }

    void Leave()
    {
        CancelJoin();
        CloseRoom();
        _browser.Dispose();
        Game.SetScreen(new MenuScreen());
    }

    void HandleTyping()
    {
        if (_focus == 0) return;
        if (Input.RawKeyPressed(Keys.Escape)) { _focus = 0; return; }
        foreach (var ch in Input.Typed)
        {
            ref string target = ref (_focus == 1 ? ref _name : ref _ipText);
            if (ch == '\b') { if (target.Length > 0) target = target[..^1]; }
            else if (ch == '\r' || ch == '\n')
            {
                if (_focus == 2 && NetProto.TryParseAddress(_ipText, out var h, out var p)) Join(h, p);
                _focus = 0;
            }
            else if (!char.IsControl(ch))
            {
                if (_focus == 1 && target.Length < 16) target += ch;
                if (_focus == 2 && target.Length < 40 && (char.IsLetterOrDigit(ch) || ch is '.' or ':' or '-')) target += ch;
            }
        }
    }

    // ------------------------------------------------------------------ botões

    void Build()
    {
        _buttons.Clear();
        // facção (trava enquanto a sala está aberta ou negociando)
        for (int i = 0; i < 3; i++)
        {
            var f = (FactionId)i;
            _buttons.Add(new UiButton
            {
                Rect = FactionRect(i), Hidden = true, Enabled = !Busy,
                OnClick = () => { if (_faction != f) { _faction = f; Sfx.Select(); } }
            });
        }

        var hp = HostPanel;
        if (_host == null)
            _buttons.Add(new UiButton
            {
                Rect = new Rectangle(hp.X + 20, hp.Bottom - 70, hp.Width - 40, 52), Label = "Criar sala", FontSize = 22, Enabled = !Busy && _name.Trim().Length > 0,
                Warn = _name.Trim().Length == 0 ? "Digite um nome primeiro." : null,
                OnClick = () => { _name = Clean(_name); OpenRoom(); }
            });
        else
            _buttons.Add(new UiButton
            {
                Rect = new Rectangle(hp.X + 20, hp.Bottom - 70, hp.Width - 40, 52), Label = "Fechar sala", FontSize = 20, Accent = new Color(150, 60, 50),
                OnClick = () => { CloseRoom(); Sfx.Click(); }
            });

        // copiar o código da sala (para mandar por mensagem)
        if (_host?.InternetCode is string code)
            _buttons.Add(new UiButton
            {
                Rect = new Rectangle(hp.X + 262, hp.Y + 134, 172, 40), Label = "Copiar código", FontSize = 14, Accent = Pal.Slate,
                OnClick = () => { if (Clipboard.SetText(code)) { _status = "Código copiado! Cole na conversa com seu amigo."; Sfx.Click(); } }
            });

        var jp = JoinPanel;
        if (_host == null)
        {
            var rooms = _browser.Rooms;
            for (int i = 0; i < Math.Min(4, rooms.Count); i++)
            {
                var r = rooms[i];
                _buttons.Add(new UiButton
                {
                    Rect = new Rectangle(jp.Right - 128, jp.Y + 64 + i * 52, 108, 42), Label = "Entrar", FontSize = 16,
                    Enabled = !Busy && r.Compatible, Warn = r.Compatible ? null : "Sala de outra versão do jogo.",
                    OnClick = () => Join(r.Address, r.Port)
                });
            }
            bool connecting = _connecting != null || (_peer != null && _peerIsGuest);
            _buttons.Add(new UiButton
            {
                Rect = new Rectangle(IpField.Right + 10, IpField.Y, jp.Right - 20 - IpField.Right - 10, 40),
                Label = connecting ? "Cancelar" : "Conectar", FontSize = 16, Accent = connecting ? Pal.InkSoft : Pal.Green,
                Enabled = connecting || NetProto.TryParseAddress(_ipText, out _, out _),
                OnClick = () =>
                {
                    if (connecting) { CancelJoin(); return; }
                    if (NetProto.TryParseAddress(_ipText, out var h, out var p)) Join(h, p);
                }
            });
        }

        _buttons.Add(new UiButton { Rect = new Rectangle((int)Cx - 100, (int)Top + 600, 200, 50), Label = "Voltar", Accent = Pal.InkSoft, Hotkey = Keys.Escape, OnClick = Leave });
    }

    // ------------------------------------------------------------------ desenho

    public override void Draw(SpriteBatch sb)
    {
        MenuBackground.Draw(sb, _dt);
        sb.Begin(transformMatrix: Game.UiMatrix, samplerState: SamplerState.LinearClamp);
        float top = Top;
        sb.TextCenter(Assets.Font(34), "JOGAR 1X1 EM REDE", new Vector2(Cx, top + 30), Pal.Gold);
        sb.TextCenter(Assets.FontNarrow(16), "Um cria a sala, o outro entra. Mesmo objetivo do jogo solo: 2 bandeiras por 90 s ou o Centro inimigo.", new Vector2(Cx, top + 62), Color.White * 0.9f);

        // nome
        sb.Text(Assets.FontBold(17), "Seu nome", new Vector2(Cx - 470, top + 94), Color.White);
        DrawField(sb, NameField, _name, _focus == 1, _host != null, "Digite seu nome");

        // facção
        sb.Text(Assets.FontBold(17), "Facção", new Vector2(Cx - 52, top + 94), Color.White);
        for (int i = 0; i < 3; i++)
        {
            var r = FactionRect(i);
            var f = (FactionId)i;
            bool sel = _faction == f;
            sb.Panel(r, sel ? Pal.Green : Pal.PanelBg * 0.92f, sel ? Pal.Gold : Pal.PanelLine * 0.6f, sel ? 3 : 1);
            FlagArt.Draw(sb, f, new Rectangle(r.X + 8, r.Y + 13, 36, 24));
            sb.Text(Assets.FontBold(14), f == FactionId.RioGrandeDoSul ? "Rio G. do Sul" : Defs.Faction(f).Name, new Vector2(r.X + 52, r.Y + 15), Busy && !sel ? Color.White * 0.4f : Color.White);
        }

        DrawHostPanel(sb);
        DrawJoinPanel(sb);

        // avisos
        float sy = top + 546;
        if (_error.Length > 0)
            foreach (var line in Gfx.Wrap(Assets.FontNarrow(15), _error, 940)) { sb.TextCenter(Assets.FontNarrow(15), line, new Vector2(Cx, sy), new Color(255, 150, 130)); sy += 20; }
        else if (_status.Length > 0)
            sb.TextCenter(Assets.FontBold(16), _status + new string('.', (int)(_time * 2) % 4), new Vector2(Cx, sy), Pal.Gold);

        sb.TextCenter(Assets.FontNarrow(13), "Mesma rede (Wi-Fi/cabo): a sala aparece sozinha.  Redes diferentes: usem Radmin VPN ou ZeroTier, ou o anfitrião libera a porta TCP 27027 no roteador.",
            new Vector2(Cx, Ui.Y - 18), Color.White * 0.65f);
        Gui.DrawAll(sb, _buttons);
        sb.End();
    }

    void DrawHostPanel(SpriteBatch sb)
    {
        var r = HostPanel;
        sb.Panel(r, Pal.Paper, Pal.Ink, 3);
        sb.Text(Assets.Font(22), "Criar sala", new Vector2(r.X + 20, r.Y + 16), Pal.Ink, false);
        var f = Assets.FontNarrow(16);
        float y = r.Y + 58;
        if (_host == null)
        {
            foreach (var line in Gfx.Wrap(f, "Você vira o anfitrião: a sua sala aparece para quem estiver procurando na mesma rede. Para quem está longe o jogo tenta abrir a porta do roteador sozinho e mostra um código curto.", r.Width - 40))
            { sb.Text(f, line, new Vector2(r.X + 20, y), Pal.Ink, false); y += 22; }
            y += 10;
            sb.Text(Assets.FontBold(15), "A sala fecha quando você sai desta tela.", new Vector2(r.X + 20, y), Pal.InkSoft, false);
            return;
        }
        // sala aberta: bandeira tremulando + como o amigo entra
        FlagArt.DrawWaving(sb, _faction, new Vector2(r.X + 22, y), 54, 36, _time);
        sb.Text(Assets.FontBold(18), _peer == null ? "Aguardando adversário" + new string('.', (int)(_time * 2) % 4) : "Adversário chegou!", new Vector2(r.X + 90, y + 2), Pal.Green, false);
        sb.Text(f, $"Sala \"{_name}\" • {Defs.Faction(_faction).Name}", new Vector2(r.X + 90, y + 26), Pal.InkSoft, false);
        y += 54;

        // pela internet: código curto (se o roteador abriu a porta)
        sb.Text(Assets.FontBold(15), "Amigo em outra casa:", new Vector2(r.X + 20, y), Pal.Ink, false);
        y += 22;
        var code = _host.InternetCode;
        var up = _host.Upnp;
        if (code != null)
        {
            sb.Fill(new Rectangle(r.X + 20, (int)y, 230, 40), Pal.Ink * 0.1f);
            sb.Text(Assets.Font(26), code, new Vector2(r.X + 30, y + 6), Pal.Ink, false);
            y += 46;
            sb.Text(Assets.FontNarrow(13), "Passe este código: ele digita em \"Entrar numa sala\".", new Vector2(r.X + 20, y), Pal.InkSoft, false);
            y += 22;
        }
        else
        {
            string st = up == null ? "Abertura automática de porta desligada." : up.Done ? up.Status : "Abrindo a porta no roteador" + new string('.', (int)(_time * 2) % 4);
            foreach (var line in Gfx.Wrap(Assets.FontNarrow(14), st, r.Width - 40).Take(3))
            { sb.Text(Assets.FontNarrow(14), line, new Vector2(r.X + 20, y), up != null && up.Done ? new Color(170, 60, 40) : Pal.InkSoft, false); y += 19; }
            if (up != null && up.Done)
            {
                sb.Text(Assets.FontNarrow(13), $"Alternativa: Radmin VPN/ZeroTier ou liberar a porta TCP {_host.Port} no roteador.", new Vector2(r.X + 20, y), Pal.InkSoft, false);
                y += 20;
            }
            y += 6;
        }

        // mesma rede: a sala já aparece sozinha; os IPs ficam de reserva
        sb.Text(Assets.FontBold(15), "Mesma rede: a sala aparece sozinha. IPs:", new Vector2(r.X + 20, y), Pal.Ink, false);
        y += 22;
        foreach (var ip in _myIps.Take(2))
        {
            string addr = _host.Port == NetProto.GamePort ? ip : $"{ip}:{_host.Port}";
            sb.Text(Assets.FontBold(15), addr, new Vector2(r.X + 28, y), Pal.Ink, false);
            sb.Text(Assets.FontNarrow(13), Hint(ip), new Vector2(r.X + 200, y + 2), Pal.InkSoft, false);
            y += 22;
        }
        if (_myIps.Count == 0) sb.Text(f, "Nenhuma rede encontrada neste computador.", new Vector2(r.X + 20, y), Pal.Red, false);
    }

    static string Hint(string ip) =>
        ip.StartsWith("26.") ? "Radmin VPN" :
        ip.StartsWith("192.168.") || ip.StartsWith("10.") || ip.StartsWith("172.") ? "rede local" :
        ip.StartsWith("100.") ? "VPN" : "";

    void DrawJoinPanel(SpriteBatch sb)
    {
        var r = JoinPanel;
        sb.Panel(r, Pal.Paper, Pal.Ink, 3);
        sb.Text(Assets.Font(22), "Entrar numa sala", new Vector2(r.X + 20, r.Y + 16), Pal.Ink, false);
        if (_host != null)
        {
            foreach (var (line, i) in Gfx.Wrap(Assets.FontNarrow(16), "Sua sala está aberta. Feche a sala para procurar a de outra pessoa.", r.Width - 40).Select((l, i) => (l, i)))
                sb.Text(Assets.FontNarrow(16), line, new Vector2(r.X + 20, r.Y + 64 + i * 22), Pal.InkSoft, false);
            return;
        }
        var rooms = _browser.Rooms;
        if (_browser.Error != null)
            sb.Text(Assets.FontNarrow(14), _browser.Error, new Vector2(r.X + 20, r.Y + 64), Pal.Red, false);
        else if (rooms.Count == 0)
        {
            sb.Text(Assets.FontNarrow(16), "Procurando salas na rede" + new string('.', (int)(_time * 2) % 4), new Vector2(r.X + 20, r.Y + 70), Pal.InkSoft, false);
            sb.Text(Assets.FontNarrow(14), "Nenhuma sala apareceu ainda. Peça para o amigo clicar em \"Criar sala\".", new Vector2(r.X + 20, r.Y + 96), Pal.InkSoft, false);
        }
        for (int i = 0; i < Math.Min(4, rooms.Count); i++)
        {
            var room = rooms[i];
            var row = new Rectangle(r.X + 16, r.Y + 60 + i * 52, r.Width - 32, 48);
            sb.Fill(row, Pal.Ink * (i % 2 == 0 ? 0.07f : 0.03f));
            FlagArt.Draw(sb, room.Faction, new Rectangle(row.X + 8, row.Y + 12, 33, 22));
            sb.Text(Assets.FontBold(17), room.Name, new Vector2(row.X + 50, row.Y + 5), Pal.Ink, false);
            sb.Text(Assets.FontNarrow(13), $"{Defs.Faction(room.Faction).Name} • {room.Address}" + (room.Compatible ? "" : " • versão diferente"), new Vector2(row.X + 50, row.Y + 27), Pal.InkSoft, false);
        }
        sb.Text(Assets.FontBold(15), "Ou digite o código da sala (ou o IP):", new Vector2(r.X + 20, IpField.Y - 26), Pal.Ink, false);
        DrawField(sb, IpField, _ipText, _focus == 2, Busy, "código (K7F2-9QXA) ou IP");
    }

    void DrawField(SpriteBatch sb, Rectangle r, string text, bool focused, bool disabled, string placeholder = "")
    {
        sb.Fill(r, disabled ? new Color(200, 196, 186) : Color.White);
        sb.Border(r, focused ? Pal.Gold : Pal.InkSoft, focused ? 2 : 1);
        var f = Assets.FontBold(17);
        bool empty = string.IsNullOrEmpty(text);
        sb.Text(f, empty && !focused ? placeholder : text, new Vector2(r.X + 10, r.Y + (r.Height - 22) / 2f), empty && !focused ? Pal.InkSoft * 0.6f : Pal.Ink, false);
        if (focused && (int)(_time * 2) % 2 == 0)
        {
            float w = empty ? 0 : f.MeasureString(text).X;
            sb.Fill(new Rectangle(r.X + 12 + (int)w, r.Y + 8, 2, r.Height - 16), Pal.Ink);
        }
    }
}
