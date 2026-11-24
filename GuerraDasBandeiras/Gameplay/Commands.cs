namespace GuerraDasBandeiras.Gameplay;

/// <summary>
/// Ordens do jogador como dados. No jogo solo são aplicadas na hora; na partida em rede viajam pela conexão
/// e as duas máquinas aplicam as mesmas ordens no mesmo "tick" da simulação (lockstep), então os dois mundos
/// continuam idênticos. Toda ordem é validada de novo na hora de aplicar (dono, custo, espaço livre...).
/// </summary>
public enum CmdType : byte { Order, Return, Stop, Place, Rally, Train, Cancel, Ability, Power }

public struct Cmd
{
    public CmdType Type;
    public byte Team;
    /// <summary>Tipo de ordem (UnitOrder), de prédio (BuildingType) ou de unidade (UnitType), conforme o comando.</summary>
    public byte Kind;
    /// <summary>Id da unidade/prédio que recebe a ordem (ou X do tile, em Place).</summary>
    public int A;
    /// <summary>Id do alvo (0 = nenhum), índice da fila (Cancel) ou Y do tile (Place).</summary>
    public int B;
    public float X, Y;
    /// <summary>Shift: enfileira em vez de substituir.</summary>
    public bool Queue;
    /// <summary>Trabalhadores que vão erguer o prédio (Place).</summary>
    public int[] Ids;

    public static Cmd Order(Unit u, UnitOrder kind, Vector2 pos, Entity target, bool queue) =>
        new() { Type = CmdType.Order, A = u.Id, Kind = (byte)kind, B = target?.Id ?? 0, X = pos.X, Y = pos.Y, Queue = queue };
}

public static class Commands
{
    public static void Apply(World w, Cmd c)
    {
        int team = c.Team;
        switch (c.Type)
        {
            case CmdType.Order:
            {
                if (OwnUnit(w, c.A, team) is not Unit u) return;
                Entity target = c.B != 0 ? w.Find(c.B) : null;
                // o alvo pode ter morrido entre o clique e a execução: vira um simples "mover"
                if (c.B != 0 && target == null) { u.Give(new QueuedOrder(UnitOrder.Move, new Vector2(c.X, c.Y), null), c.Queue); return; }
                u.Give(new QueuedOrder((UnitOrder)c.Kind, new Vector2(c.X, c.Y), target), c.Queue);
                break;
            }
            case CmdType.Return:
                if (OwnUnit(w, c.A, team) is Unit ru) ru.CmdReturn();
                break;
            case CmdType.Stop:
                if (OwnUnit(w, c.A, team) is Unit su) { su.Queue.Clear(); su.CmdStop(); }
                break;
            case CmdType.Place:
            {
                var b = w.PlaceBuilding((BuildingType)c.Kind, new Point(c.A, c.B), team);
                if (b == null) return;
                foreach (var id in c.Ids ?? Array.Empty<int>())
                {
                    if (OwnUnit(w, id, team) is not Unit u || !u.IsWorker) continue;
                    if (c.Queue) u.Enqueue(new QueuedOrder(UnitOrder.Build, b.Pos, b));
                    else { u.Queue.Clear(); u.CmdBuild(b); }
                }
                break;
            }
            case CmdType.Rally:
                if (w.Find(c.A) is Building rb && rb.Team == team)
                {
                    rb.Rally = new Vector2(c.X, c.Y);
                    rb.RallyTarget = c.B != 0 ? w.Find(c.B) as ResourceNode : null;
                }
                break;
            case CmdType.Train:
            {
                if (w.Find(c.A) is not Building tb || tb.Team != team || !tb.Done) return;
                var type = (UnitType)c.Kind;
                if (!tb.TrainOptions.Contains(type) || tb.Queue.Count >= 6) return;
                var p = w.Players[team];
                var def = Defs.Units[type];
                if (!p.CanPay(def.Cost)) return;
                p.Pay(def.Cost);
                tb.Queue.Add(type);
                break;
            }
            case CmdType.Cancel:
            {
                if (w.Find(c.A) is not Building cb || cb.Team != team || c.B < 0 || c.B >= cb.Queue.Count) return;
                w.Players[team].Refund(Defs.Units[cb.Queue[c.B]].Cost);
                cb.Queue.RemoveAt(c.B);
                if (c.B == 0) cb.ProdTimer = 0;
                break;
            }
            case CmdType.Ability:
                if (OwnUnit(w, c.A, team) is Unit au) au.UseAbility();
                break;
            case CmdType.Power:
                w.UsePower(team);
                break;
        }
    }

    static Unit OwnUnit(World w, int id, int team) => w.Find(id) is Unit u && u.Team == team ? u : null;

    // ---------------- serialização (rede) ----------------

    public static void Write(BinaryWriter bw, Cmd c)
    {
        bw.Write((byte)c.Type); bw.Write(c.Team); bw.Write(c.Kind);
        bw.Write(c.A); bw.Write(c.B); bw.Write(c.X); bw.Write(c.Y); bw.Write(c.Queue);
        var ids = c.Ids ?? Array.Empty<int>();
        bw.Write((ushort)ids.Length);
        foreach (var id in ids) bw.Write(id);
    }

    public static Cmd Read(BinaryReader br)
    {
        var c = new Cmd
        {
            Type = (CmdType)br.ReadByte(), Team = br.ReadByte(), Kind = br.ReadByte(),
            A = br.ReadInt32(), B = br.ReadInt32(), X = br.ReadSingle(), Y = br.ReadSingle(), Queue = br.ReadBoolean(),
        };
        int n = br.ReadUInt16();
        if (n > 0) { c.Ids = new int[n]; for (int i = 0; i < n; i++) c.Ids[i] = br.ReadInt32(); }
        return c;
    }
}
