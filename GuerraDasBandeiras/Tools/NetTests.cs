using System.Net;
using System.Net.Sockets;
using GuerraDasBandeiras.Gameplay;
using GuerraDasBandeiras.Net;
using GuerraDasBandeiras.Screens;

namespace GuerraDasBandeiras.Tools;

/// <summary>Testes da partida 1x1 em rede (conexões TCP reais no próprio computador).</summary>
public static partial class TestRunner
{
    static (NetPeer host, NetPeer guest) PeerPair()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        var c = new TcpClient();
        c.Connect(IPAddress.Loopback, port);
        var s = l.AcceptTcpClient();
        l.Stop();
        return (new NetPeer(s), new NetPeer(c));
    }

    static GameConfig VersusCfg(int local, int seed = 77, FactionId host = FactionId.Amazonas, FactionId guest = FactionId.RioGrandeDoSul, bool randomBases = false) =>
        new() { Mode = GameMode.Versus, PlayerFaction = host, EnemyFaction = guest, Seed = seed, LocalTeam = local, Names = new[] { "Anfitrião", "Convidado" }, RandomBases = randomBases };

    /// <summary>
    /// Jogador automático que só age por ordens (Cmd), exatamente como uma pessoa pela interface:
    /// coleta, ergue quartel, treina espadachins e ataca o Centro inimigo.
    /// </summary>
    sealed class CmdBot
    {
        readonly World _w; readonly Lockstep _ls; readonly int _team;
        float _next = 1f;
        int _step;
        public int Sent;
        public CmdBot(World w, Lockstep ls) { _w = w; _ls = ls; _team = w.Local; }

        void Send(Cmd c) { _ls.Queue(c); Sent++; }

        public void Think()
        {
            if (_w.Time < _next) return;
            _next = _w.Time + 1f;
            _step++;
            var me = _w.Players[_team];
            var mine = _w.Units.Where(u => u.Team == _team).ToList();
            int i = 0;
            foreach (var u in mine.Where(u => u.IsWorker && u.IsIdle))
            {
                var n = _w.NearestResource(i++ % 3 == 0 ? Res.Wood : Res.Food, u.Pos, World.T * 25, u);
                if (n != null) Send(Cmd.Order(u, UnitOrder.Gather, n.Pos, n, false));
            }
            var centro = _w.Buildings.FirstOrDefault(b => b.Team == _team && b.Def.Type == BuildingType.Centro);
            if (centro == null) return;
            if (centro.Queue.Count == 0 && mine.Count(u => u.IsWorker) < 8 && me.CanPay(Defs.Units[UnitType.Worker].Cost))
                Send(new Cmd { Type = CmdType.Train, A = centro.Id, Kind = (byte)UnitType.Worker });

            var quartel = _w.Buildings.FirstOrDefault(b => b.Team == _team && b.Def.Type == BuildingType.Quartel);
            if (quartel == null && me.CanPay(Defs.Buildings[BuildingType.Quartel].Cost) && _step % 3 == 0)
            {
                // procura o lugar livre (e já explorado) mais perto da base
                var st = _w.StartTiles[_team];
                var spot = Enumerable.Range(-8, 17).SelectMany(dx => Enumerable.Range(-7, 15).Select(dy => new Point(st.X + dx, st.Y + dy)))
                    .Where(tl => Math.Abs(tl.X - st.X) + Math.Abs(tl.Y - st.Y) >= 4)
                    .OrderBy(tl => Math.Abs(tl.X - st.X) + Math.Abs(tl.Y - st.Y))
                    .FirstOrDefault(tl => _w.PlaceProblem(BuildingType.Quartel, tl, _team) == null, new Point(-1, -1));
                if (spot.X >= 0)
                {
                    var builders = mine.Where(u => u.IsWorker).Take(2).Select(u => u.Id).ToArray();
                    Send(new Cmd { Type = CmdType.Place, Kind = (byte)BuildingType.Quartel, A = spot.X, B = spot.Y, Ids = builders });
                }
            }
            if (quartel != null && quartel.Done && quartel.Queue.Count < 2 && me.CanPay(Defs.Units[UnitType.Swordsman].Cost))
                Send(new Cmd { Type = CmdType.Train, A = quartel.Id, Kind = (byte)UnitType.Swordsman });

            var army = mine.Where(u => u.IsMilitary && u.IsIdle).ToList();
            var target = _w.Buildings.FirstOrDefault(b => b.Team != _team && b.Def.Type == BuildingType.Centro);
            if (army.Count >= 3 && target != null)
                foreach (var u in army) Send(Cmd.Order(u, UnitOrder.AttackMove, target.Pos, null, false));
            if (_step % 20 == 0 && me.PowerReady) Send(new Cmd { Type = CmdType.Power });
        }
    }

    static void NetLockstepSync()
    {
        const string name = "Rede: lockstep entre 2 jogos via TCP mantém os mundos idênticos";
        var (ph, pg) = PeerPair();
        // bases sorteadas: as duas máquinas precisam gerar o mesmo mapa a partir da semente
        var wa = new World(VersusCfg(0, randomBases: true));
        var wb = new World(VersusCfg(1, randomBases: true));
        var la = new Lockstep(wa, ph, 0);
        var lb = new Lockstep(wb, pg, 1);
        var ba = new CmdBot(wa, la);
        var bb = new CmdBot(wb, lb);
        int target = (int)(480 / Lockstep.TickDt);   // 8 minutos de partida
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int waits = 0;
        while ((la.Tick < target || lb.Tick < target) && sw.ElapsedMilliseconds < 120000 && la.Error == null && lb.Error == null && wa.Winner < 0)
        {
            int ta = la.Tick, tb = lb.Tick;
            if (la.Tick < target) { la.Update(Lockstep.TickDt); ba.Think(); }
            if (lb.Tick < target) { lb.Update(Lockstep.TickDt); bb.Think(); }
            if (la.Tick == ta && lb.Tick == tb) { waits++; Thread.Sleep(1); }
        }
        // deixa os dois chegarem exatamente ao mesmo tick antes de comparar
        while (la.Tick != lb.Tick && sw.ElapsedMilliseconds < 125000)
        {
            if (la.Tick < lb.Tick) la.Update(Lockstep.TickDt); else lb.Update(Lockstep.TickDt);
            Thread.Sleep(1);
        }
        uint ha = wa.Checksum(), hb = wb.Checksum();
        int deaths = wa.Players.Sum(p => p.UnitsLost) + wa.Players.Sum(p => p.BuildingsLost);
        int trained = wa.Players.Sum(p => p.UnitsTrained);
        bool ok = la.Error == null && lb.Error == null && la.Tick == lb.Tick && ha == hb && ba.Sent > 20 && bb.Sent > 20 && trained > 6 && deaths > 0;
        Check(name, ok,
            $"ticks {la.Tick}/{lb.Tick} ({wa.Time:0}s de jogo em {sw.ElapsedMilliseconds} ms, {waits} esperas); checksum {ha:x8} x {hb:x8}; " +
            $"ordens enviadas {ba.Sent}/{bb.Sent}; unidades treinadas {trained}; perdas {deaths}; vencedor {wa.Winner}/{wb.Winner}; erro: {la.Error ?? lb.Error ?? "nenhum"}");
        ph.Dispose(); pg.Dispose();
    }

    static void NetOwnership()
    {
        const string name = "Rede: ninguém dá ordens às unidades do adversário";
        var w = new World(VersusCfg(0));
        var enemy = w.Units.First(u => u.Team == 1);
        var before = enemy.Pos;
        var centro1 = w.Buildings.First(b => b.Team == 1 && b.Def.Type == BuildingType.Centro);
        int food1 = w.Players[1].Food;
        // time 0 tenta mover uma unidade do time 1 e treinar no Centro dele
        Commands.Apply(w, new Cmd { Type = CmdType.Order, Team = 0, A = enemy.Id, Kind = (byte)UnitOrder.Move, X = 100, Y = 100 });
        Commands.Apply(w, new Cmd { Type = CmdType.Train, Team = 0, A = centro1.Id, Kind = (byte)UnitType.Worker });
        Commands.Apply(w, new Cmd { Type = CmdType.Stop, Team = 0, A = enemy.Id });
        for (int i = 0; i < 30; i++) w.Update(Lockstep.TickDt);
        bool ok = enemy.Order != UnitOrder.Move && centro1.Queue.Count == 0 && w.Players[1].Food == food1;
        // e o próprio dono consegue
        var mine = w.Units.First(u => u.Team == 0 && u.IsWorker);
        Commands.Apply(w, new Cmd { Type = CmdType.Order, Team = 0, A = mine.Id, Kind = (byte)UnitOrder.Move, X = mine.Pos.X + 100, Y = mine.Pos.Y });
        ok &= mine.Order == UnitOrder.Move;
        Check(name, ok, $"ordem alheia ignorada: {enemy.Order != UnitOrder.Move}; fila do Centro inimigo {centro1.Queue.Count}; comida dele {food1}->{w.Players[1].Food}; ordem própria aceita: {mine.Order == UnitOrder.Move}");
    }

    static void NetOpponentLeaves()
    {
        const string name = "Rede: adversário que sai = vitória por abandono";
        var (ph, pg) = PeerPair();
        var gs = new GameScreen(VersusCfg(0), ph);
        gs.SkipIntro();
        pg.Leave();                               // o convidado desiste
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (gs.W.Winner < 0 && sw.ElapsedMilliseconds < 3000) { gs.Update(1 / 60f); Thread.Sleep(5); }
        bool ok = gs.W.Winner == 0 && gs.W.WinReason.Contains("abandono") && gs.Net.OpponentLeft;
        Check(name, ok, $"vencedor {gs.W.Winner}; motivo \"{gs.W.WinReason}\"; detectado em {sw.ElapsedMilliseconds} ms");
        ph.Dispose();
    }

    static void NetRoomDiscovery()
    {
        const string name = "Rede: sala anunciada na rede local e conexão aceita";
        using var browser = new RoomBrowser();
        using var host = new RoomHost("Sala de teste", FactionId.MinasGerais, upnp: false);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        RoomInfo found = null;
        while (found == null && sw.ElapsedMilliseconds < 4000)
        {
            host.Poll();
            found = browser.Rooms.FirstOrDefault(r => r.Name == "Sala de teste");
            Thread.Sleep(50);
        }
        // entra na sala pelo endereço anunciado
        NetPeer accepted = null;
        TcpClient client = null;
        if (found != null)
        {
            client = new TcpClient();
            client.Connect(found.Address == "127.0.0.1" ? "127.0.0.1" : found.Address, found.Port);
            var t = System.Diagnostics.Stopwatch.StartNew();
            while (accepted == null && t.ElapsedMilliseconds < 2000) { accepted = host.Poll(); Thread.Sleep(10); }
        }
        bool ok = found != null && found.Faction == FactionId.MinasGerais && found.Compatible && accepted != null && host.Error == null;
        Check(name, ok, found == null
            ? $"sala não apareceu em 4 s ({browser.Error ?? host.Error ?? "sem erro"}) — firewall bloqueando UDP {NetProto.DiscoveryPort}?"
            : $"encontrada em {sw.ElapsedMilliseconds} ms em {found.Address}:{found.Port} ({found.Faction}); conexão aceita: {accepted != null}");
        accepted?.Dispose();
        client?.Dispose();
    }

    static void NetGuestPerspective()
    {
        const string name = "Rede: guest (time 1) vê o mundo do seu lado (névoa, câmera, HUD)";
        var (ph, pg) = PeerPair();
        var gs = new GameScreen(VersusCfg(1, host: FactionId.MinasGerais, guest: FactionId.Amazonas), pg);
        var w = gs.W;
        var myBase = w.CenterOf(w.StartTiles[1]);
        var theirBase = w.CenterOf(w.StartTiles[0]);
        var myTile = w.TileOf(myBase); var theirTile = w.TileOf(theirBase);
        bool fog = w.Visible[myTile.X, myTile.Y] && !w.Visible[theirTile.X, theirTile.Y];
        bool hostFog = w.TeamVisible[0][theirTile.X, theirTile.Y] && !w.TeamVisible[0][myTile.X, myTile.Y];
        bool me = w.Me == w.Players[1] && w.Me.Faction.Id == FactionId.Amazonas && w.Local == 1 && w.Foe == 0;
        bool cam = gs.CameraPos.X > w.W * World.T / 2f;
        // o anfitrião não "vê tudo" como a IA: a visão dele também depende da névoa
        var guestUnit = w.Units.First(u => u.Team == 1);
        bool symmetric = !w.CanSee(0, guestUnit) && w.CanSee(1, guestUnit);
        // construir em área não explorada é proibido para os dois lados
        string farProblem = w.PlaceProblem(BuildingType.Casa, new Point(10, 10), 1);
        bool ok = fog && hostFog && me && cam && symmetric && farProblem == "Área não explorada";
        Check(name, ok, $"névoa local ok: {fog}; névoa do anfitrião ok: {hostFog}; \"eu\" = time 1 AM: {me}; câmera na base leste: {cam}; visão simétrica: {symmetric}; construir longe: {farProblem}");
        ph.Dispose(); pg.Dispose();
    }
}

public static partial class TestRunner
{
    static void NetReconnect()
    {
        const string name = "Rede: conexão cai no meio da partida, volta sozinha e o jogo segue igual (+ chat)";
        using var room = new RoomHost("Sala reconexão", FactionId.Amazonas, upnp: false);
        // primeira conexão, como no saguão
        var gc = new TcpClient();
        gc.Connect(IPAddress.Loopback, room.Port);
        NetPeer ph = null;
        var t0 = System.Diagnostics.Stopwatch.StartNew();
        while (ph == null && t0.ElapsedMilliseconds < 2000) { ph = room.Poll(); Thread.Sleep(5); }
        var pg = new NetPeer(gc);
        room.StopAnnouncing();
        const int session = 4242;
        var wa = new World(VersusCfg(0, seed: 91));
        var wb = new World(VersusCfg(1, seed: 91));
        var la = new Lockstep(wa, ph, 0, new NetLink { SessionId = session, Room = room });
        var lb = new Lockstep(wb, pg, 1, new NetLink { SessionId = session, HostAddress = "127.0.0.1", HostPort = room.Port });
        var ba = new CmdBot(wa, la);
        var bb = new CmdBot(wb, lb);

        int dropAt = (int)(90 / Lockstep.TickDt), target = (int)(240 / Lockstep.TickDt);
        bool dropped = false, sawReconnecting = false;
        string chatGot = null;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while ((la.Tick < target || lb.Tick < target) && sw.ElapsedMilliseconds < 120000 && la.Error == null && lb.Error == null && !la.OpponentLeft && !lb.OpponentLeft)
        {
            int ta = la.Tick, tb = lb.Tick;
            if (la.Tick < target) { la.Update(Lockstep.TickDt); ba.Think(); }
            if (lb.Tick < target) { lb.Update(Lockstep.TickDt); bb.Think(); }
            if (!dropped && lb.Tick >= dropAt)
            {
                // queda: o cabo "sai" do lado do convidado (o socket fecha sem mensagem de saída)
                dropped = true;
                lb.Peer.Close("queda simulada");
            }
            sawReconnecting |= la.Reconnecting || lb.Reconnecting;
            if (dropped && lb.Reconnects > 0 && chatGot == null && la.Reconnects > 0) lb.SendChat("voltei! bora");
            if (la.ChatIn.Count > 0) chatGot = la.ChatIn.Dequeue();
            if (la.Tick == ta && lb.Tick == tb) Thread.Sleep(1);
        }
        while (la.Tick != lb.Tick && sw.ElapsedMilliseconds < 125000)
        {
            if (la.Tick < lb.Tick) la.Update(Lockstep.TickDt); else lb.Update(Lockstep.TickDt);
            if (la.ChatIn.Count > 0) chatGot = la.ChatIn.Dequeue();
            Thread.Sleep(1);
        }
        for (int i = 0; i < 50 && chatGot == null; i++) { la.Update(0); if (la.ChatIn.Count > 0) chatGot = la.ChatIn.Dequeue(); Thread.Sleep(10); }
        uint ha = wa.Checksum(), hb = wb.Checksum();
        bool ok = dropped && sawReconnecting && la.Reconnects >= 1 && lb.Reconnects >= 1 && la.Error == null && lb.Error == null
                  && !la.OpponentLeft && !lb.OpponentLeft && la.Tick == lb.Tick && la.Tick >= target && ha == hb && chatGot == "voltei! bora";
        Check(name, ok, $"queda no tick {dropAt}; reconexões {la.Reconnects}/{lb.Reconnects}; ticks {la.Tick}/{lb.Tick}; checksum {ha:x8} x {hb:x8}; " +
                        $"chat recebido: \"{chatGot}\"; saiu: {la.OpponentLeft}/{lb.OpponentLeft}; erro: {la.Error ?? lb.Error ?? "nenhum"}; {sw.ElapsedMilliseconds} ms");
        la.Leave(); lb.Leave();
    }

    static void NetRoomCode()
    {
        const string name = "Rede: código da sala (IP + porta) vai e volta sem erro";
        var cases = new[] { ("203.0.113.45", 27027), ("187.64.1.200", 27030), ("8.8.4.4", 27036), ("255.255.255.255", 27027) };
        var bad = new List<string>();
        foreach (var (ip, port) in cases)
        {
            var code = NetProto.EncodeCode(IPAddress.Parse(ip), port);
            // a pessoa pode digitar em minúsculas, sem hífen, ou trocar O por 0
            foreach (var typed in new[] { code, code.ToLowerInvariant(), code.Replace("-", ""), code.Replace('0', 'O') })
                if (!NetProto.TryParseAddress(typed, out var h, out var p) || h != ip || p != port) bad.Add($"{typed}->{h}:{p}");
        }
        bool ipStill = NetProto.TryParseAddress("192.168.0.10:27028", out var h2, out var p2) && h2 == "192.168.0.10" && p2 == 27028;
        bool invalid = !NetProto.TryDecodeCode("AB!D-1234", out _, out _);
        var sample = NetProto.EncodeCode(IPAddress.Parse("203.0.113.45"), 27027);
        Check(name, bad.Count == 0 && ipStill && invalid, $"exemplo 203.0.113.45 -> {sample}; falhas: {(bad.Count == 0 ? "nenhuma" : string.Join(", ", bad))}; IP digitado continua valendo: {ipStill}; código inválido recusado: {invalid}");
    }

    static void NetDuplicateNames()
    {
        const string name = "Rede: duas salas com o mesmo nome aparecem separadas";
        using var browser = new RoomBrowser();
        using var r1 = new RoomHost("Pedro", FactionId.Amazonas, upnp: false);
        using var r2 = new RoomHost("Pedro", FactionId.RioGrandeDoSul, upnp: false);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        List<RoomInfo> rooms = new();
        while (sw.ElapsedMilliseconds < 4000)
        {
            r1.Poll(); r2.Poll();
            rooms = browser.Rooms.Where(r => r.Name == "Pedro").ToList();
            if (rooms.Count >= 2) break;
            Thread.Sleep(50);
        }
        Check(name, rooms.Count == 2 && rooms.Select(r => r.Port).Distinct().Count() == 2,
            $"salas \"Pedro\" na lista: {rooms.Count} ({string.Join(", ", rooms.Select(r => $"{r.Faction} porta {r.Port}"))})");
    }
}
