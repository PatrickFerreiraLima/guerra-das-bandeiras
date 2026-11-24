using System.Collections.Concurrent;
using System.Net.Sockets;

namespace GuerraDasBandeiras.Net;

/// <summary>Tipos de mensagem do protocolo (1 byte no começo de cada mensagem).</summary>
public static class Msg
{
    public const byte Hello = 1;    // convidado -> anfitrião: versão, balanceamento, nome, facção
    public const byte Reject = 2;   // anfitrião -> convidado: motivo da recusa
    public const byte Start = 3;    // anfitrião -> convidado: semente do mapa, nomes e facções
    public const byte Tick = 4;     // ordens de um jogador para um tick (+ checksum de vez em quando)
    public const byte Ping = 5;     // mantém a conexão viva e mede a latência
    public const byte Pong = 6;
    public const byte Bye = 7;      // saída voluntária (desistiu / fechou a sala)
    public const byte Resume = 8;   // reconexão: código da partida + próximo tick que falta receber
    public const byte Chat = 9;     // mensagem de chat
}

/// <summary>
/// Uma conexão TCP com o outro jogador. Cada mensagem vai como [tamanho:int32][tipo:byte][dados].
/// Uma thread só recebe e põe as mensagens numa fila; o jogo lê a fila no Update (sem travar o quadro).
/// </summary>
public sealed class NetPeer : IDisposable
{
    readonly TcpClient _client;
    readonly NetworkStream _stream;
    readonly object _sendLock = new();
    readonly ConcurrentQueue<(byte type, byte[] data)> _inbox = new();
    readonly Thread _rx;
    readonly Timer _pinger;
    long _lastRecvTicks = Environment.TickCount64;
    long _lastSendTicks = Environment.TickCount64;
    long _pingSentAt;

    /// <summary>Endereço do outro lado (para mostrar na tela).</summary>
    public readonly string Remote;
    public volatile bool Closed;
    public string CloseReason = "";
    /// <summary>Latência medida (ida e volta), em milissegundos.</summary>
    public int PingMs { get; private set; }

    /// <summary>Sem nenhuma mensagem por este tempo, a conexão é dada como perdida.</summary>
    const int TimeoutMs = 10000;

    public NetPeer(TcpClient client)
    {
        _client = client;
        _client.NoDelay = true;               // mensagens pequenas e frequentes: sem esperar juntar pacotes
        _stream = client.GetStream();
        Remote = client.Client.RemoteEndPoint?.ToString() ?? "?";
        _rx = new Thread(ReceiveLoop) { IsBackground = true, Name = "rede-recebe" };
        _rx.Start();
        // ping num timer próprio: mesmo com a janela arrastada/minimizada (laço do jogo parado) a conexão segue viva
        _pinger = new Timer(_ => { _pingSentAt = Environment.TickCount64; Send(Msg.Ping); }, null, 500, 1000);
    }

    public void Send(byte type, byte[] data = null)
    {
        if (Closed) return;
        data ??= Array.Empty<byte>();
        try
        {
            lock (_sendLock)
            {
                var head = new byte[5];
                BitConverter.TryWriteBytes(head.AsSpan(0, 4), data.Length + 1);
                head[4] = type;
                _stream.Write(head, 0, 5);
                if (data.Length > 0) _stream.Write(data, 0, data.Length);
                _stream.Flush();
                _lastSendTicks = Environment.TickCount64;
            }
        }
        catch (Exception e) { Close("Conexão perdida (" + e.Message + ")"); }
    }

    public bool TryReceive(out byte type, out byte[] data)
    {
        // pings são tratados aqui mesmo, sem incomodar o jogo
        while (_inbox.TryDequeue(out var m))
        {
            if (m.type == Msg.Ping) { Send(Msg.Pong); continue; }
            if (m.type == Msg.Pong) { PingMs = (int)(Environment.TickCount64 - _pingSentAt); continue; }
            type = m.type; data = m.data;
            return true;
        }
        type = 0; data = null;
        return false;
    }

    /// <summary>Chamar todo quadro: detecta silêncio prolongado (o outro lado manda ping a cada segundo).</summary>
    public void Service()
    {
        if (Closed) return;
        if (Environment.TickCount64 - Interlocked.Read(ref _lastRecvTicks) > TimeoutMs) Close("O outro jogador parou de responder.");
    }

    void ReceiveLoop()
    {
        var head = new byte[4];
        try
        {
            while (!Closed)
            {
                if (!ReadExact(head, 4)) break;
                int len = BitConverter.ToInt32(head, 0);
                if (len < 1 || len > 1 << 20) { Close("Mensagem inválida recebida."); return; }
                var body = new byte[len];
                if (!ReadExact(body, len)) break;
                Interlocked.Exchange(ref _lastRecvTicks, Environment.TickCount64);
                _inbox.Enqueue((body[0], body.AsSpan(1).ToArray()));
            }
            Close("O outro jogador desconectou.");
        }
        catch (Exception) { Close("O outro jogador desconectou."); }
    }

    bool ReadExact(byte[] buf, int n)
    {
        int got = 0;
        while (got < n)
        {
            int r = _stream.Read(buf, got, n - got);
            if (r <= 0) return false;
            got += r;
        }
        return true;
    }

    public void Close(string reason)
    {
        if (Closed) return;
        CloseReason = reason;
        Closed = true;
        try { _pinger?.Dispose(); } catch { }
        try { _client.Close(); } catch { }
    }

    /// <summary>Sai avisando o outro lado (ele vê "o adversário saiu" na hora, sem esperar o tempo limite).</summary>
    public void Leave()
    {
        if (!Closed) Send(Msg.Bye);
        Close("Você saiu.");
    }

    public void Dispose() => Close("Encerrado.");
}
