using System.Net.Sockets;
using GuerraDasBandeiras.Gameplay;

namespace GuerraDasBandeiras.Net;

/// <summary>
/// O que cada lado precisa saber para refazer a conexão se ela cair no meio da partida.
/// O anfitrião continua escutando na porta da sala; o convidado sabe para onde ligar de novo.
/// O código da partida (sorteado pelo anfitrião) impede que outra pessoa "entre no lugar".
/// </summary>
public sealed class NetLink
{
    public int SessionId;
    /// <summary>Só no anfitrião: a sala segue escutando (e com a porta aberta no roteador) durante a partida.</summary>
    public RoomHost Room;
    /// <summary>Só no convidado: endereço do anfitrião.</summary>
    public string HostAddress;
    public int HostPort;
    public bool IsHost => Room != null;
}

/// <summary>
/// Simulação em "lockstep": as duas máquinas rodam o mesmo mundo, em passos fixos (ticks de 1/30 s),
/// e só trocam as ordens dos jogadores. Uma ordem dada agora é marcada para daqui a <see cref="Delay"/> ticks;
/// assim dá tempo de ela chegar ao outro lado e as duas máquinas a aplicam exatamente no mesmo tick.
/// Se as ordens do adversário para um tick ainda não chegaram, a simulação espera (aparece "aguardando").
/// A cada segundo as duas comparam um checksum do estado: se divergir, a partida é encerrada com aviso.
///
/// Reconexão: se a conexão cair (sem o adversário ter desistido), a partida congela e o convidado liga de novo
/// para o anfitrião a cada 2 s, por até 60 s. Ao voltar, cada lado reenvia as ordens que o outro ainda não
/// recebeu (todas ficam guardadas até o outro lado confirmar que já passou daquele tick) e o jogo segue igual.
/// </summary>
public sealed class Lockstep
{
    public const float TickDt = 1 / 30f;
    /// <summary>Atraso das ordens em ticks (4 = ~133 ms): cobre a latência de rede local e de boa parte da internet.</summary>
    public const int Delay = 4;
    const int HashEvery = 30;
    /// <summary>Tempo máximo esperando a conexão voltar.</summary>
    public const float ReconnectWindow = 60f;

    readonly World _w;
    readonly int _local;
    readonly NetLink _link;
    public NetPeer Peer { get; private set; }

    int _tick;                 // próximo tick a executar
    int _sentUpTo = Delay - 1; // os primeiros ticks não têm ordens (ninguém clicou ainda)
    float _acc;
    readonly List<Cmd> _pending = new();
    readonly Dictionary<int, List<Cmd>> _mine = new(), _theirs = new();
    readonly SortedDictionary<int, byte[]> _sentLog = new();   // pacotes enviados, guardados para reenvio
    readonly Dictionary<int, uint> _myHash = new(), _theirHash = new();
    readonly Queue<(int tick, uint hash)> _hashOut = new();

    // reconexão
    readonly List<(NetPeer peer, float age)> _candidates = new();
    Task<TcpClient> _connecting;
    float _retry;

    /// <summary>Mensagens de chat recebidas (a tela consome).</summary>
    public readonly Queue<string> ChatIn = new();

    /// <summary>Esperando as ordens do adversário (rede lenta ou o outro jogo travou).</summary>
    public bool Waiting { get; private set; }
    public float WaitTime { get; private set; }
    /// <summary>A conexão caiu e estamos tentando refazê-la (a partida fica congelada).</summary>
    public bool Reconnecting { get; private set; }
    public float ReconnectTime { get; private set; }
    /// <summary>Quantas vezes a conexão caiu e voltou nesta partida.</summary>
    public int Reconnects { get; private set; }
    /// <summary>Erro grave (dessincronização); a partida não pode continuar.</summary>
    public string Error { get; private set; }
    /// <summary>O adversário saiu (desistiu, fechou o jogo ou a conexão não voltou a tempo).</summary>
    public bool OpponentLeft { get; private set; }
    public string LeftReason { get; private set; } = "";

    public int Tick => _tick;

    public Lockstep(World w, NetPeer peer, int localTeam, NetLink link = null)
    {
        _w = w; Peer = peer; _local = localTeam;
        _link = link ?? new NetLink();
    }

    /// <summary>Ordem dada por quem joga nesta máquina: vai para o próximo pacote.</summary>
    public void Queue(Cmd c)
    {
        c.Team = (byte)_local;
        _pending.Add(c);
    }

    /// <summary>Manda uma mensagem de chat. Devolve false se a conexão estiver caída no momento.</summary>
    public bool SendChat(string text)
    {
        if (Peer == null || Peer.Closed || Reconnecting) return false;
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms)) bw.Write(text);
        Peer.Send(Msg.Chat, ms.ToArray());
        return !Peer.Closed;
    }

    /// <summary>Chamar todo quadro com o tempo real decorrido. Roda quantos ticks couberem (no máximo 8 por quadro).</summary>
    public void Update(float dt)
    {
        Pump(dt);
        if (Error != null || OpponentLeft) return;

        _acc = Math.Min(_acc + dt, 1f);
        int steps = 0;
        Waiting = false;
        while (_acc >= TickDt && steps < 8)
        {
            // manda (uma única vez) as ordens que valem para daqui a Delay ticks
            while (_sentUpTo < _tick + Delay) SendBatch(_sentUpTo + 1);
            if (_tick >= Delay && !_theirs.ContainsKey(_tick)) { Waiting = true; break; }
            Execute(_tick);
            _tick++;
            _acc -= TickDt;
            steps++;
            if (_w.Winner >= 0) break;
        }
        if (Reconnecting) Waiting = true;
        WaitTime = Waiting ? WaitTime + dt : 0;
    }

    void SendBatch(int t)
    {
        var list = new List<Cmd>(_pending);
        _pending.Clear();
        _mine[t] = list;
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms))
        {
            bw.Write(t);
            bw.Write((ushort)list.Count);
            foreach (var c in list) Commands.Write(bw, c);
            bw.Write((byte)_hashOut.Count);
            while (_hashOut.Count > 0) { var (ht, hv) = _hashOut.Dequeue(); bw.Write(ht); bw.Write(hv); }
        }
        var bytes = ms.ToArray();
        _sentLog[t] = bytes;           // guardado: se a conexão cair, é reenviado depois
        Peer?.Send(Msg.Tick, bytes);
        _sentUpTo = t;
    }

    void Execute(int t)
    {
        // as ordens entram sempre na mesma ordem nas duas máquinas: primeiro o anfitrião (time 0), depois o convidado
        _mine.Remove(t, out var mine);
        _theirs.Remove(t, out var theirs);
        var first = _local == 0 ? mine : theirs;
        var second = _local == 0 ? theirs : mine;
        if (first != null) foreach (var c in first) Commands.Apply(_w, c);
        if (second != null) foreach (var c in second) Commands.Apply(_w, c);

        _w.Update(TickDt);

        if (t % HashEvery == 0)
        {
            uint h = _w.Checksum();
            _myHash[t] = h;
            _hashOut.Enqueue((t, h));
            Compare(t);
        }
    }

    void Compare(int t)
    {
        if (_myHash.TryGetValue(t, out var a) && _theirHash.TryGetValue(t, out var b))
        {
            if (a != b) Error = "As duas partidas divergiram (dessincronização). Confirmem que os dois usam a mesma versão do jogo.";
            _myHash.Remove(t); _theirHash.Remove(t);
        }
        else if (_theirHash.ContainsKey(t) && t < _tick - 4 * HashEvery) _theirHash.Remove(t); // repetido por reenvio
    }

    // ------------------------------------------------------------------ rede

    void Pump(float dt)
    {
        if (_link.IsHost) AcceptReconnections(dt);
        if (Reconnecting) ReconnectStep(dt);

        if (Peer != null)
        {
            if (!Peer.Closed) Peer.Service();
            // lê o que chegou mesmo se a conexão já fechou: o "tchau" pode estar na fila logo antes do fechamento
            while (Peer != null && Peer.TryReceive(out var type, out var data))
                if (!Handle(Peer, type, data)) return;
        }

        if ((Peer == null || Peer.Closed) && !Reconnecting && !OpponentLeft && Error == null)
        {
            // caiu sem "tchau": congela e tenta voltar
            Reconnecting = true;
            ReconnectTime = 0;
            _retry = 0;
        }
    }

    /// <summary>Trata uma mensagem. Devolve false se a partida acabou para nós (o adversário saiu).</summary>
    bool Handle(NetPeer from, byte type, byte[] data)
    {
        try
        {
            using var br = new BinaryReader(new MemoryStream(data));
            switch (type)
            {
                case Msg.Bye:
                    OpponentLeft = true;
                    LeftReason = "O adversário saiu da partida.";
                    from.Close(LeftReason);
                    return false;
                case Msg.Chat:
                    ChatIn.Enqueue(br.ReadString());
                    break;
                case Msg.Resume:
                {
                    int sid = br.ReadInt32(), next = br.ReadInt32();
                    if (sid != _link.SessionId) { from.Close("partida diferente"); return false; }
                    Resend(next);
                    if (Reconnecting) { Reconnecting = false; Reconnects++; }
                    break;
                }
                case Msg.Tick:
                {
                    int t = br.ReadInt32();
                    int n = br.ReadUInt16();
                    var list = new List<Cmd>(n);
                    int foe = 1 - _local;
                    for (int i = 0; i < n; i++)
                    {
                        var c = Commands.Read(br);
                        c.Team = (byte)foe;   // ninguém dá ordens pelo outro time
                        list.Add(c);
                    }
                    if (t >= _tick) _theirs[t] = list;   // pacotes repetidos (reenvio) de ticks já jogados são ignorados
                    int hn = br.ReadByte();
                    for (int i = 0; i < hn; i++)
                    {
                        int ht = br.ReadInt32(); uint hv = br.ReadUInt32();
                        _theirHash[ht] = hv;
                        Compare(ht);
                    }
                    // quando o outro manda o tick t, ele já jogou tudo antes de t - Delay: não precisa mais desses pacotes
                    foreach (var k in _sentLog.Keys.TakeWhile(k => k < t - Delay).ToList()) _sentLog.Remove(k);
                    break;
                }
            }
        }
        catch (Exception) { Error = "Mensagem de rede corrompida."; }
        return true;
    }

    void Resend(int fromTick)
    {
        foreach (var kv in _sentLog)
            if (kv.Key >= fromTick) Peer?.Send(Msg.Tick, kv.Value);
    }

    byte[] ResumeBytes()
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms)) { bw.Write(_link.SessionId); bw.Write(_tick); }
        return ms.ToArray();
    }

    /// <summary>
    /// Anfitrião: a sala continua escutando. Quem bater na porta precisa se apresentar com o código desta partida;
    /// aí vira a conexão atual (mesmo que a antiga ainda não tenha sido dada como morta).
    /// </summary>
    void AcceptReconnections(float dt)
    {
        var fresh = _link.Room.Poll();
        if (fresh != null) _candidates.Add((fresh, 0));
        for (int i = _candidates.Count - 1; i >= 0; i--)
        {
            var (p, age) = _candidates[i];
            age += dt;
            _candidates[i] = (p, age);
            p.Service();
            bool done = false;
            while (!done && p.TryReceive(out var type, out var data))
            {
                if (type != Msg.Resume) continue;
                using var br = new BinaryReader(new MemoryStream(data));
                int sid = br.ReadInt32(), next = br.ReadInt32();
                if (sid != _link.SessionId) { p.Close("partida diferente"); done = true; break; }
                var old = Peer;
                Peer = p;
                old?.Close("substituída pela reconexão");
                Peer.Send(Msg.Resume, ResumeBytes());
                Resend(next);
                if (Reconnecting) { Reconnecting = false; Reconnects++; }
                _candidates.RemoveAt(i);
                done = true;
            }
            if (done) continue;
            if (p.Closed || age > 6f) { p.Close("sem apresentação"); _candidates.RemoveAt(i); }
        }
    }

    void ReconnectStep(float dt)
    {
        ReconnectTime += dt;
        if (ReconnectTime > ReconnectWindow)
        {
            Reconnecting = false;
            OpponentLeft = true;
            LeftReason = $"A conexão não voltou em {ReconnectWindow:0} s.";
            return;
        }
        if (_link.IsHost || _link.HostAddress == null) return;   // o anfitrião só espera; quem liga é o convidado

        // convidado: liga de novo a cada 2 s até conseguir
        if (_connecting != null)
        {
            if (!_connecting.IsCompleted) return;
            if (_connecting.Status == TaskStatus.RanToCompletion)
            {
                Peer = new NetPeer(_connecting.Result);
                Peer.Send(Msg.Resume, ResumeBytes());   // "sou eu, desta partida, e estou no tick X"
            }
            _connecting = null;
            _retry = 2f;
            return;
        }
        if (Peer != null && !Peer.Closed) return;   // conectado, esperando o anfitrião responder
        _retry -= dt;
        if (_retry > 0) return;
        _retry = 2f;
        string host = _link.HostAddress; int port = _link.HostPort;
        _connecting = Task.Run(async () =>
        {
            var c = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            try { await c.ConnectAsync(host, port, cts.Token); return c; }
            catch { c.Dispose(); throw; }
        });
    }

    /// <summary>Sair da partida avisando o adversário (e fechando a sala, se formos o anfitrião).</summary>
    public void Leave()
    {
        Peer?.Leave();
        foreach (var (p, _) in _candidates) p.Close("saindo");
        _candidates.Clear();
        _link.Room?.Dispose();
    }
}
