namespace GuerraDasBandeiras.Gameplay;

/// <summary>
/// IA de primeira versão (Bíblia de Gameplay, seção 06): máquina de estados simples e previsível
/// EXPANDIR (Desenvolver) → PRODUZIR → RECONHECER → ATACAR → RECUAR/DEFENDER.
/// </summary>
public class AIController
{
    public enum Mode { Desenvolver, Defender, Atacar, Recuar }

    readonly World _w;
    readonly int _team;
    readonly Player _p;
    readonly Random _rng;
    float _timer;
    float _attackCooldown;
    int _waveSize;
    int _launched;
    float _retreatTimer;
    int _reconIdx;
    public Mode State { get; private set; } = Mode.Desenvolver;
    /// <summary>Na missão guiada a IA só cuida da economia.</summary>
    public bool Passive;

    public AIController(World w, int team)
    {
        _w = w;
        _team = team;
        _p = w.Players[team];
        _rng = new Random(w.Config.Seed + 77);
        var bc = Balance.C;
        _waveSize = bc.AiWaveSize + w.Config.Difficulty switch { 0 => 1, 2 => -1, _ => 0 };
        _attackCooldown = bc.AiFirstAttack + w.Config.Difficulty switch { 0 => 90, 2 => -60, _ => 0 }; // ritmo: sem grande ataque no começo
    }

    public void Update(float dt)
    {
        _timer -= dt;
        _attackCooldown -= dt;
        if (_timer > 0) return;
        _timer = 0.5f;
        Think();
    }

    IEnumerable<Building> Mine(BuildingType t) => _w.Buildings.Where(b => b.Team == _team && b.Def.Type == t);
    int Count(BuildingType t) => Mine(t).Count();

    void Think()
    {
        var centro = Mine(BuildingType.Centro).FirstOrDefault();
        if (centro == null) return;
        var units = _w.Units.Where(u => u.Team == _team).ToList();
        var workers = units.Where(u => u.IsWorker).ToList();
        var army = units.Where(u => u.IsMilitary).ToList();
        int targetWorkers = Passive ? 7 : _w.Config.Difficulty == 0 ? 12 : 16;

        // 1) Trabalhadores
        if (workers.Count + centro.Queue.Count < targetWorkers && centro.Queue.Count < 2)
            TryTrain(centro, UnitType.Worker);

        // 2) Coleta
        foreach (var wk in workers)
            if (wk.IsIdle) AssignGather(wk, centro);

        // 3) Construções
        bool building = _w.Buildings.Any(b => b.Team == _team && !b.Done);
        if (!building)
        {
            if (_p.PopCap - _p.Pop <= 3 && _p.PopCap < Defs.MaxPop) TryBuild(BuildingType.Casa, centro, workers);
            else if (!Passive && workers.Count >= 5 && Count(BuildingType.Quartel) == 0) TryBuild(BuildingType.Quartel, centro, workers);
            else if (workers.Count >= 8 && Count(BuildingType.Coletor) == 0) TryBuild(BuildingType.Coletor, centro, workers, nearWood: true);
            else if (!Passive && workers.Count >= 8 && Count(BuildingType.Oficina) == 0 && Mine(BuildingType.Quartel).Any(b => b.Done)) TryBuild(BuildingType.Oficina, centro, workers);
            else if (!Passive && army.Count >= 4 && Count(BuildingType.Torre) < (_w.Config.Difficulty == 0 ? 1 : 2)) TryBuild(BuildingType.Torre, centro, workers, towardEnemy: true);
            else if (!Passive && _w.Config.Difficulty == 2 && _p.Wood > 350 && Count(BuildingType.Quartel) < 2) TryBuild(BuildingType.Quartel, centro, workers);
        }

        if (Passive) return;

        // 4) Exército
        // a unidade especial é a identidade da facção: com a Oficina pronta, a IA guarda recursos para ela
        var oficina = Mine(BuildingType.Oficina).FirstOrDefault(b => b.Done);
        int specials = army.Count(u => u.Def.Type == _p.Faction.Special) + (oficina?.Queue.Count(t => t == _p.Faction.Special) ?? 0);
        bool saveForSpecial = oficina != null && specials < 3 && !_p.CanPay(Defs.Units[_p.Faction.Special].Cost);
        if (!saveForSpecial)
            foreach (var q in Mine(BuildingType.Quartel).Where(b => b.Done && b.Queue.Count < 2))
                TryTrain(q, PickBarracksUnit());
        foreach (var o in Mine(BuildingType.Oficina).Where(b => b.Done && b.Queue.Count < 1))
        {
            int scouts = army.Count(u => u.Def.Type == UnitType.Scout);
            int rams = army.Count(u => u.Def.IsSiege);
            double roll = _rng.NextDouble();
            var pick = specials < 3 ? _p.Faction.Special
                     : scouts == 0 && roll < 0.4 ? UnitType.Scout
                     : army.Count >= 8 && rams < 2 && roll < 0.3 ? UnitType.Ram
                     : _p.Faction.Special;
            TryTrain(o, pick);
        }

        Recon(army, centro);
        UseAbilities(army);

        // 5) Comportamento
        // CONTESTAR: o inimigo domina as duas bandeiras ou está tomando uma nossa -> reage já,
        // sem esperar a próxima onda (antes a IA assistia o relógio de 90 s correr)
        bool enemyHolds = _w.HoldTimer[1 - _team] > 0;
        bool flagUnderAttack = _w.Flags.Any(f => f.Owner == _team && f.Capturer == 1 - _team);
        if ((enemyHolds || flagUnderAttack) && State != Mode.Atacar && army.Count >= 3
            && (State != Mode.Recuar || _w.HoldTimer[1 - _team] > 30))
        {
            State = Mode.Atacar;
            _launched = army.Count;
            _attackCooldown = 45;
            var flag = _w.Flags.Where(f => f.Owner != _team).OrderBy(f => Vector2.Distance(f.Pos, centro.Pos)).FirstOrDefault()
                       ?? _w.Flags.OrderBy(f => Vector2.Distance(f.Pos, centro.Pos)).First();
            if (_team == 1) _w.Notify?.Invoke("O inimigo veio retomar as bandeiras!", Core.Pal.Red);
            foreach (var a in army) a.CmdAttackMove(Jitter(flag.Pos));
            return;
        }

        if (State == Mode.Recuar)
        {
            _retreatTimer -= 0.5f;
            if (_retreatTimer > 0) return;
            State = Mode.Desenvolver;
        }
        var threats = _w.Units.Where(u => u.Team != _team && _w.Buildings.Any(b => b.Team == _team && Vector2.Distance(b.Pos, u.Pos) < 11 * World.T) && _w.CanSee(_team, u)).ToList();
        if (threats.Count > 0)
        {
            State = Mode.Defender;
            if (threats.Count >= 4) TryPower(defending: true);
            foreach (var a in army)
            {
                if (a.Def.Type == UnitType.Scout && a.Order == UnitOrder.Move) continue;
                if (a.Order == UnitOrder.Attack && a.Target is Unit) continue;
                var near = threats.OrderBy(t => Vector2.DistanceSquared(t.Pos, a.Pos)).First();
                a.CmdAttack(near);
            }
            // trabalhadores ajudam se a base estiver sendo invadida com força
            if (threats.Count > army.Count + 2)
                foreach (var wk in workers.Where(w => Vector2.Distance(w.Pos, centro.Pos) < 8 * World.T).Take(4))
                    wk.CmdAttack(threats[0]);
            return;
        }

        if (State == Mode.Defender) State = Mode.Desenvolver;

        if (State == Mode.Desenvolver && army.Count >= _waveSize && _attackCooldown <= 0)
        {
            State = Mode.Atacar;
            _waveSize = Math.Min(_waveSize + 3, 20);
            _attackCooldown = 60;
            _launched = army.Count;
            LaunchAttack(army);
        }
        else if (State == Mode.Atacar)
        {
            if (army.Count < 3) { State = Mode.Desenvolver; _attackCooldown = 45; return; }
            // RECUAR: perdeu mais da metade e está em desvantagem perto da frente
            var front = new Vector2(army.Average(a => a.Pos.X), army.Average(a => a.Pos.Y));
            int enemyNear = _w.Units.Count(u => u.Team != _team && u.IsMilitary && Vector2.Distance(u.Pos, front) < 8 * World.T);
            if (army.Count < _launched * 0.45f && enemyNear > army.Count)
            {
                State = Mode.Recuar;
                _retreatTimer = 25;
                _attackCooldown = 40;
                var home = centro.Pos + new Vector2(_team == 1 ? -3 * World.T : 3 * World.T, 0);
                foreach (var a in army) a.CmdMove(Jitter(home));
                return;
            }
            if (enemyNear >= 4) TryPower(defending: false);
            var target = AttackTarget();
            foreach (var a in army.Where(a => a.IsIdle && a.Def.Type != UnitType.Scout)) a.CmdAttackMove(Jitter(target));
        }
        else
        {
            // reúne o exército na frente da base, voltado para o rio
            var rally = centro.Pos + new Vector2(_team == 1 ? -5 * World.T : 5 * World.T, -2 * World.T);
            // Vigias da Mata ficam de emboscada na vegetação mais próxima da frente
            foreach (var v in army.Where(a => a.IsIdle && a.Def.Type == UnitType.VigiaDaMata && !a.InForest))
            {
                var spot = NearestForest(rally, 9);
                if (spot is Vector2 s) v.CmdMove(s);
            }
            foreach (var a in army.Where(a => a.IsIdle && !(a.Def.Type == UnitType.VigiaDaMata && a.InForest) && Vector2.Distance(a.Pos, rally) > 3 * World.T)) a.CmdMove(Jitter(rally));
            foreach (var b in _w.Buildings.Where(b => b.Team == _team && b.Def.Trains.Any(t => t != UnitType.Worker)))
                b.Rally = rally;
        }
    }

    void LaunchAttack(List<Unit> army)
    {
        if (_p.FirstAttackTime < 0) _p.FirstAttackTime = _w.Time;
        var target = AttackTarget();
        if (_team == 1) _w.Notify?.Invoke("O inimigo está avançando!", Core.Pal.Red);
        foreach (var a in army.Where(a => a.Def.Type != UnitType.Scout || army.Count < 4)) a.CmdAttackMove(Jitter(target));
    }

    /// <summary>RECONHECER: um batedor circula pelos pontos importantes do mapa.</summary>
    void Recon(List<Unit> army, Building centro)
    {
        if (_w.Time < 40) return;
        var scout = army.FirstOrDefault(u => u.Def.Type == UnitType.Scout && u.IsIdle);
        if (scout == null) return;
        var pts = new List<Vector2> { _w.Flags[0].Pos, _w.Flags[1].Pos, _w.CenterOf(_w.StartTiles[1 - _team]) + new Vector2(0, -5 * World.T), _w.CenterOf(new Point(_w.W / 2, _w.H - 4)) };
        scout.CmdMove(Jitter(pts[_reconIdx++ % pts.Count]));
    }

    /// <summary>Usa as habilidades das unidades especiais quando faz sentido.</summary>
    void UseAbilities(List<Unit> army)
    {
        foreach (var a in army)
        {
            if (!a.HasAbility || a.AbilityCooldown > 0 || a.AbilityActive || a.StunTime > 0) continue; // a regra da mata é checada abaixo
            switch (a.Def.Type)
            {
                case UnitType.VigiaDaMata:
                {
                    bool enemyNear = _w.FindEnemyFor(_team, a.Pos, a.SightPx, unitsOnly: true) != null;
                    if (a.InForest && enemyNear) a.UseAbility();
                    // fora da mata e com inimigo à vista: corre para a vegetação mais próxima (mesma regra do jogador)
                    else if (enemyNear && !a.InForest && a.Order != UnitOrder.Move && NearestForest(a.Pos, 3) is Vector2 f) a.CmdMove(f);
                    break;
                }
                case UnitType.LanceiroDosPampas:
                    if (a.Order == UnitOrder.Attack && a.Target != null && Vector2.Distance(a.Pos, a.Target.Pos) > 2.5f * World.T) a.UseAbility();
                    break;
                case UnitType.GuardiaoDasGerais:
                    // ergue o escudo ao entrar em combate corpo a corpo ou sob fogo de flechas
                    if (a.Order == UnitOrder.Attack && a.Target != null && (Vector2.Distance(a.Pos, a.Target.Pos) < 2 * World.T || a.Hp < a.MaxHp * 0.85f)) a.UseAbility();
                    break;
            }
        }
    }

    Vector2? NearestForest(Vector2 from, int radius)
    {
        var c = _w.TileOf(from);
        for (int r = 0; r <= radius; r++)
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                {
                    if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
                    var p = new Point(c.X + dx, c.Y + dy);
                    if (_w.Passable(p) && _w.Ter[p.X, p.Y] == Terrain.Forest) return _w.CenterOf(p) + new Vector2(_rng.Next(-12, 13), _rng.Next(-12, 13));
                }
        return null;
    }

    void TryPower(bool defending)
    {
        if (!_p.PowerReady) return;
        if (_p.Faction.Id == FactionId.MinasGerais && !defending) return; // Fortificar é defensivo
        _w.UsePower(_team);
    }

    Vector2 AttackTarget()
    {
        // primeiro as bandeiras que ainda não são da IA; depois a base do jogador
        var flag = _w.Flags.Where(f => f.Owner != _team)
            .OrderBy(f => Vector2.Distance(f.Pos, _w.CenterOf(_w.StartTiles[_team]))).FirstOrDefault();
        if (flag != null && (_rng.NextDouble() < 0.75 || _w.HoldTimer[1 - _team] > 0)) return flag.Pos;
        var enemyCentro = _w.Buildings.FirstOrDefault(b => b.Team != _team && b.Def.Type == BuildingType.Centro);
        return enemyCentro?.Pos ?? _w.Flags[0].Pos;
    }

    Vector2 Jitter(Vector2 p) => p + new Vector2(_rng.Next(-60, 61), _rng.Next(-60, 61));

    UnitType PickBarracksUnit()
    {
        // contra-ataca a composição do jogador
        var enemy = _w.Units.Where(u => u.Team != _team && u.IsMilitary).ToList();
        int cav = enemy.Count(u => u.Def.Tags.HasFlag(Tag.Cavalry));
        int rng = enemy.Count(u => u.Def.IsRanged);
        int inf = enemy.Count(u => u.Def.Tags.HasFlag(Tag.Infantry));
        double roll = _rng.NextDouble();
        if (cav >= 2 && roll < 0.3 + 0.12f * cav) return UnitType.Spearman; // responde à cavalaria inimiga
        if (rng > inf && roll < 0.6) return UnitType.Swordsman;
        if (inf > rng && roll < 0.5) return UnitType.Archer;
        return _rng.Next(3) switch { 0 => UnitType.Swordsman, 1 => UnitType.Spearman, _ => UnitType.Archer };
    }

    void TryTrain(Building b, UnitType t)
    {
        var def = Defs.Units[t];
        if (!_p.CanPay(def.Cost) || _p.Pop + b.Queue.Sum(x => Defs.Units[x].Pop) + def.Pop > _p.PopCap) return;
        _p.Pay(def.Cost);
        b.Queue.Add(t);
    }

    void AssignGather(Unit wk, Building centro)
    {
        // distribuição desejada: 45% comida, 35% madeira, 20% minério
        var mine = _w.Units.Where(u => u.Team == _team && u.IsWorker).ToList();
        int total = Math.Max(1, mine.Count);
        float[] want = { 0.45f, 0.33f, 0.22f };
        // ajusta conforme o estoque: o que está sobrando recebe menos gente
        if (_p.Food < 100) want[0] += 0.15f;
        if (_p.Food > 500) want[0] = 0.25f;
        if (_p.Wood > 400) want[1] = 0.15f;
        if (_p.Ore > 400) want[2] = 0.05f;
        float[] have = new float[3];
        foreach (var u in mine) if (u.LastGatherKind is Res k && u != wk) have[(int)k] += 1f / total;
        var order = Enumerable.Range(0, 3).OrderByDescending(i => want[i] - have[i]);
        foreach (int i in order)
        {
            var node = _w.NearestResource((Res)i, centro.Pos, 16 * World.T, wk);
            if (node != null) { wk.CmdGather(node); return; }
        }
    }

    void TryBuild(BuildingType type, Building centro, List<Unit> workers, bool nearWood = false, bool towardEnemy = false)
    {
        var def = Defs.Buildings[type];
        if (!_p.CanPay(def.Cost) || workers.Count == 0) return;

        Vector2 anchor = centro.Pos;
        if (nearWood)
        {
            var wood = _w.NearestResource(Res.Wood, centro.Pos, 14 * World.T);
            if (wood != null) anchor = Vector2.Lerp(centro.Pos, wood.Pos, 0.75f);
        }
        if (towardEnemy) anchor += new Vector2(_team == 1 ? -4 * World.T : 4 * World.T, -World.T);

        var spot = FindSpot(type, _w.TileOf(anchor));
        if (spot == null) return;
        var b = _w.PlaceBuilding(type, spot.Value, _team);
        if (b == null) return;
        var builder = workers.OrderBy(w => (w.Order == UnitOrder.Build ? 1e9f : 0) + Vector2.DistanceSquared(w.Pos, b.Pos)).First();
        builder.CmdBuild(b);
        if (type is BuildingType.Quartel or BuildingType.Oficina && workers.Count > 6)
            workers.Where(w => w != builder && w.Order != UnitOrder.Build).OrderBy(w => Vector2.DistanceSquared(w.Pos, b.Pos)).FirstOrDefault()?.CmdBuild(b);
    }

    Point? FindSpot(BuildingType type, Point anchor)
    {
        int s = Defs.Buildings[type].Size;
        var candidates = new List<Point>();
        for (int ring = 2; ring <= 10 && candidates.Count < 6; ring++)
            for (int dx = -ring; dx <= ring; dx++)
                for (int dy = -ring; dy <= ring; dy++)
                {
                    if (Math.Abs(dx) != ring && Math.Abs(dy) != ring) continue;
                    var tl = new Point(anchor.X + dx - s / 2, anchor.Y + dy - s / 2);
                    if (_w.CanPlace(type, tl, _team) && HasMargin(tl, s)) candidates.Add(tl);
                }
        if (candidates.Count == 0) return null;
        return candidates[_rng.Next(Math.Min(3, candidates.Count))];
    }

    /// <summary>Deixa um corredor livre ao redor para não trancar caminhos.</summary>
    bool HasMargin(Point tl, int s)
    {
        for (int x = tl.X - 1; x <= tl.X + s; x++)
            for (int y = tl.Y - 1; y <= tl.Y + s; y++)
            {
                if (x >= tl.X && x < tl.X + s && y >= tl.Y && y < tl.Y + s) continue;
                var p = new Point(x, y);
                if (!_w.InBounds(p) || _w.Occ[x, y] is Building) return false;
            }
        return true;
    }
}
