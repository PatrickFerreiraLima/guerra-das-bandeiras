using GuerraDasBandeiras.Core;

namespace GuerraDasBandeiras.Gameplay;

public enum UnitOrder { Idle, Move, AttackMove, Attack, Gather, Return, Build }

public class Unit : Entity
{
    public UnitDef Def;
    public Player Owner;

    public UnitOrder Order;
    public Entity Target;
    public Vector2? AttackMoveDest;
    public Vector2 GuardPos;
    public bool AutoTarget;
    public readonly List<QueuedOrder> Queue = new();

    public List<Vector2> Path;
    public int PathIdx;
    float _repath, _acquire, _attackTimer, _gatherTimer, _repairCostTimer;

    public ResourceNode GatherNode;
    public Res CargoKind;
    public int Cargo;
    public Res? LastGatherKind;

    public bool FacingLeft;
    public float MovedTime;
    public float RevealTimer;
    public float AnimTime;
    public bool MovedThisFrame;
    public float AttackAnim;
    public float DustTimer;

    // habilidades e estados especiais
    public float AbilityCooldown, AbilityTime;
    public float StunTime;
    public float CamoTime;
    public bool ChargeBoost;

    public override float Radius => Def.IsSiege ? 16f : Def.Tags.HasFlag(Tag.Cavalry) ? 14f : 12f;
    public override RectangleF WorldRect => new(Pos.X - 14, Pos.Y - 34 * Def.Scale, 28, 44 * Def.Scale);
    public override string DisplayName => Def.Name;
    public bool IsWorker => Def.IsWorker;
    public bool IsMilitary => !Def.IsWorker;
    public bool IsIdle => Order == UnitOrder.Idle;
    public bool InForest => World.TerrainAt(Pos) == Terrain.Forest;
    public float SightPx => (Def.Sight + (World.TerrainAt(Pos) == Terrain.Hill ? 2 : 0)
                             + (Def.Type == UnitType.VigiaDaMata && InForest ? 2 : 0)) * Defs.Tile;
    public float RangePx => Def.IsRanged && World.TerrainAt(Pos) == Terrain.Hill ? Def.Range * 1.15f : Def.Range;

    public bool HasAbility => Def.Ability != null;
    public bool AbilityActive => AbilityTime > 0;
    public bool AbilityReady => AbilityBlock() == null;
    public bool StoneStance => Def.Type == UnitType.GuardiaoDasGerais && AbilityTime > 0;

    // ---------------- Comandos ----------------

    public void CmdStop()
    {
        Order = UnitOrder.Idle; Path = null; Target = null; AttackMoveDest = null; AutoTarget = false;
    }

    public void CmdMove(Vector2 dest)
    {
        CmdStop();
        Order = UnitOrder.Move;
        PathTo(dest);
    }

    public void CmdAttackMove(Vector2 dest)
    {
        CmdStop();
        Order = UnitOrder.AttackMove;
        AttackMoveDest = dest;
        PathTo(dest);
    }

    public void CmdAttack(Entity e, bool auto = false)
    {
        var amd = auto ? AttackMoveDest : null;
        CmdStop();
        Order = UnitOrder.Attack;
        Target = e;
        AttackMoveDest = amd;
        AutoTarget = auto;
        if (auto && amd == null) GuardPos = Pos;
        _repath = 0;
    }

    public void CmdGather(ResourceNode n)
    {
        if (!IsWorker) { CmdMove(n.Pos); return; }
        if (Cargo > 0 && CargoKind != n.Kind) Cargo = 0; // troca de recurso: descarta a carga anterior
        CmdStop();
        Order = UnitOrder.Gather;
        GatherNode = n;
        LastGatherKind = n.Kind;
        _repath = 0;
    }

    /// <summary>Construir (se ainda não pronto) ou reparar (se danificado).</summary>
    public void CmdBuild(Building b)
    {
        if (!IsWorker) { CmdMove(b.Pos); return; }
        CmdStop();
        Order = UnitOrder.Build;
        Target = b;
        _repath = 0;
    }

    public void CmdReturn()
    {
        CmdStop();
        Order = UnitOrder.Return;
        _repath = 0;
    }

    /// <summary>Ordem do jogador: com Shift entra na fila, sem Shift substitui tudo.</summary>
    public void Give(QueuedOrder o, bool queue)
    {
        if (queue) Enqueue(o);
        else { Queue.Clear(); Execute(o); }
    }

    public void Enqueue(QueuedOrder o)
    {
        if (Order == UnitOrder.Idle && Queue.Count == 0) Execute(o);
        else Queue.Add(o);
    }

    void Execute(QueuedOrder o)
    {
        switch (o.Kind)
        {
            case UnitOrder.Move: CmdMove(o.Pos); break;
            case UnitOrder.AttackMove: CmdAttackMove(o.Pos); break;
            case UnitOrder.Attack: if (o.Target != null && !o.Target.Dead) CmdAttack(o.Target); break;
            case UnitOrder.Gather: if (o.Target is ResourceNode n && !n.Dead) CmdGather(n); break;
            case UnitOrder.Build: if (o.Target is Building b && !b.Dead) CmdBuild(b); break;
        }
    }

    /// <summary>Por que a habilidade não pode ser usada agora (null = pode). Vale igual para jogador e IA.</summary>
    public string AbilityBlock()
    {
        if (!HasAbility) return "Esta unidade não tem habilidade.";
        if (StunTime > 0) return "Atordoado.";
        if (AbilityTime > 0) return $"{Def.Ability} já está ativa.";
        if (AbilityCooldown > 0) return $"{Def.Ability} recarrega em {Math.Ceiling(AbilityCooldown)}s.";
        if (Def.Type == UnitType.VigiaDaMata && !InForest) return "Camuflagem só funciona dentro da mata.";
        return null;
    }

    /// <summary>Ativa a habilidade da unidade especial. Retorna true se ativou.</summary>
    public bool UseAbility()
    {
        if (AbilityBlock() != null) return false;
        AbilityTime = Def.AbilityDur;
        AbilityCooldown = Def.AbilityCd;
        Owner.AbilitiesUsed++;
        switch (Def.Type)
        {
            case UnitType.VigiaDaMata:
                CamoTime = Def.AbilityDur;
                RevealTimer = 0;
                World.AddLeaves(Pos, 10);
                break;
            case UnitType.LanceiroDosPampas:
                ChargeBoost = true;
                World.AddDust(Pos + new Vector2(0, 8), 1);
                break;
        }
        if (Def.Type == UnitType.GuardiaoDasGerais) World.AddStones(Pos + new Vector2(0, -14), 7);
        World.AddText(Pos + new Vector2(0, -52 * Def.Scale), Def.Ability + "!", Pal.Gold);
        World.AddRing(Pos, Pal.Gold);
        Sfx.Ability();
        return true;
    }

    // ---------------- Atualização ----------------

    public void Update(float dt)
    {
        _attackTimer -= dt;
        _acquire -= dt;
        RevealTimer -= dt;
        AbilityCooldown -= dt;
        CamoTime -= dt;
        if (AbilityTime > 0)
        {
            AbilityTime -= dt;
            if (AbilityTime <= 0) ChargeBoost = false;
        }
        AttackAnim = Math.Max(0, AttackAnim - dt * 4);
        MovedThisFrame = false;
        _wantsMove = false;

        // camuflagem só vale dentro da vegetação: sair da mata desfaz
        if (CamoTime > 0 && !InForest)
        {
            CamoTime = 0;
            if (Def.Type == UnitType.VigiaDaMata && AbilityTime > 0) AbilityTime = 0;
        }

        // passivo da Mara das Águas: resgate dentro da mata
        if (Owner.Faction.Id == FactionId.Amazonas && Hp < MaxHp && InForest) Hp = Math.Min(MaxHp, Hp + Balance.C.ForestRegen * dt);

        if (StunTime > 0) { StunTime -= dt; return; }

        switch (Order)
        {
            case UnitOrder.Idle:
                if (Def.Dmg > 0 && !IsWorker && _acquire <= 0)
                {
                    _acquire = 0.35f;
                    var e = Acquire();
                    if (e != null) CmdAttack(e, auto: true);
                }
                break;

            case UnitOrder.Move:
                FollowPath(dt);
                if (PathDone) Order = UnitOrder.Idle;
                break;

            case UnitOrder.AttackMove:
                if (_acquire <= 0)
                {
                    _acquire = 0.3f;
                    var e = Acquire();
                    if (e != null) { CmdAttack(e, auto: true); break; }
                }
                FollowPath(dt);
                if (PathDone) { AttackMoveDest = null; Order = UnitOrder.Idle; }
                break;

            case UnitOrder.Attack: UpdateAttack(dt); break;
            case UnitOrder.Gather: UpdateGather(dt); break;
            case UnitOrder.Return: UpdateReturn(dt); break;
            case UnitOrder.Build: UpdateBuild(dt); break;
        }

        CheckStuck(dt);

        if (Order == UnitOrder.Idle && Queue.Count > 0)
        {
            var next = Queue[0];
            Queue.RemoveAt(0);
            Execute(next);
        }

        // folhinhas soltas enquanto camuflado (só visual, sorteio separado da simulação)
        if (CamoTime > 0 && World.VisualFx && World.FxRng.NextDouble() < dt * 1.5) World.AddLeaves(Pos, 1);

        // revalida depois do movimento: saiu da mata neste quadro, perde a camuflagem na hora
        if (CamoTime > 0 && !InForest) { CamoTime = 0; if (Def.Type == UnitType.VigiaDaMata) AbilityTime = 0; }

        if (MovedThisFrame)
        {
            MovedTime += dt; AnimTime += dt;
            DustTimer -= dt;
            if (DustTimer <= 0)
            {
                DustTimer = Def.Tags.HasFlag(Tag.Cavalry) || AbilityActive ? 0.12f : 0.35f;
                if (World.TerrainAt(Pos) is Terrain.Dirt or Terrain.Ford or Terrain.Grass) World.AddDust(Pos + new Vector2(0, 8));
            }
        }
        else if (Order != UnitOrder.Attack) MovedTime = 0;
    }

    Entity Acquire() => World.FindEnemyFor(Team, Pos, SightPx, buildingsOnly: Def.IsSiege);

    bool HiddenNow => CamoTime > 0 || (Def.Tags.HasFlag(Tag.Stealth) && InForest && RevealTimer <= 0);

    void UpdateAttack(float dt)
    {
        if (Target == null || Target.Dead || !World.CanSee(Team, Target))
        {
            Target = null;
            if (AttackMoveDest is Vector2 d) { var keep = d; CmdAttackMove(keep); }
            else
            {
                bool wasAuto = AutoTarget;
                CmdStop();
                _acquire = 0;
                // se estava defendendo, volta para o posto quando não há mais ninguém por perto
                if (wasAuto && Vector2.Distance(Pos, GuardPos) > Defs.Tile * 2 && Acquire() == null)
                    CmdMove(GuardPos);
            }
            return;
        }

        // não persegue indefinidamente quando o alvo foi escolhido automaticamente
        if (AutoTarget && AttackMoveDest == null && Vector2.Distance(Pos, GuardPos) > Defs.Tile * 9)
        {
            CmdMove(GuardPos);
            return;
        }

        if (Approach(Target, RangePx, dt))
        {
            FacingLeft = Target.Pos.X < Pos.X;
            if (_attackTimer <= 0)
            {
                _attackTimer = Def.Cooldown;
                AttackAnim = 1f;
                bool ambush = HiddenNow;
                float dmg = Combat.Damage(this, Target, ambush);
                if (ambush || Def.Tags.HasFlag(Tag.Stealth)) { RevealTimer = 2.5f; CamoTime = 0; if (Def.Type == UnitType.VigiaDaMata) AbilityTime = 0; }
                if (Def.IsRanged)
                {
                    World.Shoot(this, Target, dmg);
                    Sfx.Shoot(World, Pos);
                }
                else
                {
                    World.Damage(Target, dmg, this);
                    World.AddSparks(Target is Unit ? Target.Pos + new Vector2(0, -14) : Vector2.Lerp(Pos, Target.Pos, 0.7f), Def.IsSiege ? Pal.Wood : Color.White);
                    Sfx.Melee(World, Pos, Target is Building, Def.IsSiege);
                }
                MovedTime = 0;
            }
        }
    }

    void UpdateGather(float dt)
    {
        if (Cargo >= Defs.CarryCapacity) { CmdReturnKeepNode(); return; }

        if (GatherNode == null || GatherNode.Dead)
        {
            var kind = LastGatherKind ?? Res.Wood;
            GatherNode = World.NearestResource(kind, Pos, Defs.Tile * 10, this);
            if (GatherNode == null)
            {
                if (Cargo > 0) CmdReturnKeepNode();
                else CmdStop();
                return;
            }
        }

        if (Approach(GatherNode, Defs.Tile * 0.45f, dt))
        {
            FacingLeft = GatherNode.Pos.X < Pos.X;
            float mult = Owner.Faction.Gather[(int)GatherNode.Kind] * Owner.GatherMult * (GatherNode.Kind == Res.Food ? World.FlagBonus(Team, FlagBonus.Food) : 1f);
            _gatherTimer += dt * mult;
            AnimTime += dt;
            if (_gatherTimer >= Defs.GatherInterval)
            {
                _gatherTimer -= Defs.GatherInterval;
                AttackAnim = 1f;
                if (Cargo > 0 && CargoKind != GatherNode.Kind) Cargo = 0;
                CargoKind = GatherNode.Kind;
                Cargo++;
                GatherNode.Amount--;
                GatherNode.Shake = 0.25f;
                World.AddChips(Vector2.Lerp(Pos, GatherNode.Pos, 0.6f) + new Vector2(0, -8), GatherNode.Kind);
                if (Team == 0 && World.FxRng.NextDouble() < 0.35)
                    Sfx.Gather(World, Pos, GatherNode.Kind);
                if (GatherNode.Amount <= 0) World.Deplete(GatherNode);
            }
        }
    }

    void CmdReturnKeepNode()
    {
        var node = GatherNode;
        var last = LastGatherKind;
        Order = UnitOrder.Return;
        Path = null;
        _repath = 0;
        GatherNode = node;
        LastGatherKind = last;
    }

    void UpdateReturn(float dt)
    {
        if (Cargo <= 0) { ResumeGather(); return; }
        var drop = World.NearestDropoff(Team, Pos);
        if (drop == null) { CmdStop(); return; }
        if (Approach(drop, 10f, dt))
        {
            Owner.Add(CargoKind, Cargo);
            if (Team == 0) World.AddText(Pos + new Vector2(0, -40), $"+{Cargo}", Defs.ResColor(CargoKind));
            if (Team == 0) Sfx.Deposit(World, Pos);
            Cargo = 0;
            ResumeGather();
        }
    }

    void ResumeGather()
    {
        var node = GatherNode;
        if (node == null || node.Dead)
            node = LastGatherKind is Res k ? World.NearestResource(k, Pos, Defs.Tile * 12, this) : null;
        if (node != null) CmdGather(node);
        else CmdStop();
    }

    void UpdateBuild(float dt)
    {
        if (Target is not Building b || b.Dead || (b.Done && !b.Damaged))
        {
            var finished = Target as Building;
            CmdStop();
            // depois de erguer um depósito, já vai coletar o que estiver perto
            if (finished != null && !finished.Dead && finished.Def.Dropoff && Queue.Count == 0)
            {
                var n = World.NearestResource(LastGatherKind ?? Res.Wood, Pos, Defs.Tile * 7, this)
                        ?? World.NearestAnyResource(Pos, Defs.Tile * 7);
                if (n != null) CmdGather(n);
            }
            return;
        }
        if (Approach(b, 10f, dt))
        {
            FacingLeft = b.Pos.X < Pos.X;
            AnimTime += dt;
            bool mg = Owner.Faction.Id == FactionId.MinasGerais;
            if (!b.Done) b.AddProgress(dt / b.Def.Time);
            else
            {
                // reparo: metade da velocidade de construção, consome madeira (grátis para Minas)
                if (!mg)
                {
                    _repairCostTimer += dt;
                    if (_repairCostTimer >= 1.5f)
                    {
                        _repairCostTimer = 0;
                        if (Owner.Wood <= 0) { CmdStop(); if (Team == 0) World.Notify?.Invoke("Sem madeira para reparar.", Pal.Red); return; }
                        Owner.Wood--;
                    }
                }
                b.Hp = Math.Min(b.MaxHp, b.Hp + b.MaxHp / b.Def.Time * 0.5f * (mg ? 1.5f : 1f) * dt);
            }
            _gatherTimer += dt;
            if (_gatherTimer > 0.9f)
            {
                _gatherTimer = 0;
                AttackAnim = 1f;
                World.AddChips(Vector2.Lerp(Pos, b.Pos, 0.4f) + new Vector2(0, -10), Res.Wood);
                if (Team == 0) Sfx.Hammer(World, Pos);
            }
        }
    }

    // ---------------- Anti-travamento ----------------

    public bool Unreachable;
    Vector2 _moveDest, _stuckPos;
    float _stuckTimer;
    int _stuckCount;
    bool _wantsMove;

    /// <summary>Se tenta andar e não sai do lugar, recalcula o caminho; depois de 3 tentativas desiste e avisa.</summary>
    void CheckStuck(float dt)
    {
        if (!_wantsMove) { _stuckTimer = 0; _stuckPos = Pos; _stuckCount = 0; return; }
        _stuckTimer += dt;
        if (_stuckTimer < 1.2f) return;
        bool stuck = Vector2.Distance(Pos, _stuckPos) < 8f;
        _stuckTimer = 0; _stuckPos = Pos;
        if (!stuck) { _stuckCount = 0; return; }
        _stuckCount++;
        if (Order is UnitOrder.Move or UnitOrder.AttackMove)
        {
            // já está praticamente no destino (empurrado pela multidão): considera concluído
            if (Vector2.Distance(Pos, _moveDest) < Defs.Tile) { Order = UnitOrder.Idle; Path = null; return; }
            if (_stuckCount >= 3)
            {
                CmdStop();
                if (Team == 0) World.Notify?.Invoke("Uma unidade ficou bloqueada no caminho e parou.", Pal.Gold);
                return;
            }
            var keep = _moveDest; var o = Order; var amd = AttackMoveDest;
            PathTo(keep);
            if (o == UnitOrder.AttackMove) AttackMoveDest = amd;
        }
        else { Path = null; _repath = 0; }
    }

    // ---------------- Movimento ----------------

    public bool PathDone => Path == null || PathIdx >= Path.Count;

    void PathTo(Vector2 dest)
    {
        Path = World.Pathing.FindPath(Pos, dest);
        PathIdx = 0;
        _moveDest = dest;
        // o A* devolve o ponto alcançável mais próximo: marca se ficou longe do pedido
        var end = Path != null && Path.Count > 0 ? Path[^1] : Pos;
        Unreachable = Vector2.Distance(end, dest) > Defs.Tile * 1.5f;
    }

    void PathToEntity(Entity e)
    {
        Path = World.Pathing.FindPathToRect(Pos, e.TileRect, e.BlocksTile);
        PathIdx = 0;
    }

    public float EdgeDist(Entity e)
    {
        if (e is Unit u) return Vector2.Distance(Pos, u.Pos) - Radius - u.Radius;
        var r = e.WorldRect;
        float dx = Math.Max(Math.Max(r.X - Pos.X, 0), Pos.X - r.Right);
        float dy = Math.Max(Math.Max(r.Y - Pos.Y, 0), Pos.Y - r.Bottom);
        return MathF.Sqrt(dx * dx + dy * dy) - Radius;
    }

    Vector2 ClosestPoint(Entity e)
    {
        if (e is Unit) return e.Pos;
        var r = e.WorldRect;
        return new Vector2(Math.Clamp(Pos.X, r.X, r.Right), Math.Clamp(Pos.Y, r.Y, r.Bottom));
    }

    /// <summary>Aproxima-se do alvo até ficar dentro do alcance. Retorna true quando já está no alcance.</summary>
    bool Approach(Entity e, float range, float dt)
    {
        if (EdgeDist(e) <= range) { Path = null; return true; }
        _repath -= dt;
        if (_repath <= 0 && (PathDone || e is Unit))
        {
            _repath = e is Unit ? 0.5f : 1.2f;
            PathToEntity(e);
        }
        if (!PathDone) FollowPath(dt);
        else MoveToward(ClosestPoint(e), dt);
        return false;
    }

    public float CurrentSpeed()
    {
        var f = Owner.Faction;
        float s = Def.Speed * f.UnitSpeed;
        switch (World.TerrainAt(Pos))
        {
            case Terrain.Forest: s *= f.ForestSpeed; break;
            case Terrain.Hill: s *= 0.85f; break;
            case Terrain.Ford: s *= 0.7f; break;
            default: s *= f.PlainSpeed; break;
        }
        if (StoneStance) s *= Balance.C.StanceSpeed;
        if (Def.Type == UnitType.LanceiroDosPampas && AbilityActive) s *= Balance.C.ChargeSpeed;
        if (Owner.PowerActive && f.Id == FactionId.RioGrandeDoSul && IsMilitary) s *= Balance.C.CoordSpeed;
        return s;
    }

    void FollowPath(float dt)
    {
        if (PathDone) return;
        var target = Path[PathIdx];
        var diff = target - Pos;
        float dist = diff.Length();
        float step = CurrentSpeed() * dt;
        if (dist <= step + 1f)
        {
            Pos = target;
            PathIdx++;
        }
        else
        {
            Pos += diff / dist * step;
            if (Math.Abs(diff.X) > 2) FacingLeft = diff.X < 0;
        }
        _wantsMove = true;
        MovedThisFrame = true;
    }

    void MoveToward(Vector2 p, float dt)
    {
        var diff = p - Pos;
        float dist = diff.Length();
        if (dist < 0.5f) return;
        var next = Pos + diff / dist * Math.Min(dist, CurrentSpeed() * dt);
        if (World.Passable(World.TileOf(next)))
        {
            Pos = next;
            if (Math.Abs(diff.X) > 2) FacingLeft = diff.X < 0;
            MovedThisFrame = true;
        }
        _wantsMove = true;
    }

    public string StatusText()
    {
        if (StunTime > 0) return "Atordoado!";
        string s = Order switch
        {
            UnitOrder.Idle => "Parado",
            UnitOrder.Move => "Movendo",
            UnitOrder.AttackMove => "Avançando em formação de ataque",
            UnitOrder.Attack => Target != null ? $"Atacando: {Target.DisplayName}" : "Atacando",
            UnitOrder.Gather => GatherNode != null ? $"Coletando {Defs.ResName(GatherNode.Kind)} ({Cargo}/{Defs.CarryCapacity})" : "Coletando",
            UnitOrder.Return => $"Levando {Cargo} de {Defs.ResName(CargoKind)} ao depósito",
            UnitOrder.Build => Target is Building { Done: true } ? "Reparando" : "Construindo",
            _ => ""
        };
        if (Queue.Count > 0) s += $"  (+{Queue.Count} na fila)";
        return s;
    }
}

public static class Combat
{
    /// <summary>
    /// Tabela de vantagens dos cadernos:
    /// lanceiro vence cavalaria; atirador vence infantaria lenta; infantaria alcança atiradores;
    /// cerco derruba construções mas é indefeso contra ataque direto.
    /// Os bônus de facção/habilidade/poder são somados e limitados por Balance.MaxBonusDamage;
    /// as reduções de dano são limitadas por Balance.MaxDamageReduction.
    /// </summary>
    public static float Damage(Unit a, Entity target, bool ambush = false)
    {
        var c = Balance.C;
        float dmg = a.Def.Dmg;
        var at = a.Def;
        var fa = a.Owner.Faction;

        // --- bônus (limitados) ---
        float bonus = 1f;
        if (fa.Id == FactionId.RioGrandeDoSul && at.Tags.HasFlag(Tag.Cavalry)) bonus *= c.CavalryPassive;
        if (a.Owner.PowerActive && fa.Id == FactionId.RioGrandeDoSul) bonus *= c.CoordDamage;
        if (ambush)
        {
            bool camo = at.Type == UnitType.VigiaDaMata && a.CamoTime > 0;
            bonus *= camo ? c.CamoAmbushDamage : fa.Id == FactionId.Amazonas ? c.ForestAmbushDamage : 1.1f;
            a.World.AddText(target.Pos + new Vector2(0, -46), "EMBOSCADA!", Pal.GreenLight);
            a.World.AddLeaves(target.Pos, 8);
        }

        if (target is Unit t)
        {
            var tt = t.Def.Tags;
            if (at.IsSiege) return dmg * 0.1f;
            if (at.Type == UnitType.LanceiroDosPampas)
            {
                bool exposed = tt.HasFlag(Tag.Ranged) || tt.HasFlag(Tag.Worker) || tt.HasFlag(Tag.Siege);
                if (a.ChargeBoost)
                {
                    bonus *= c.ChargeDamage;
                    a.ChargeBoost = false;
                    a.AbilityTime = 0;
                    t.StunTime = Math.Max(t.StunTime, c.ChargeStun);
                    a.World.AddText(t.Pos + new Vector2(0, -46), "INVESTIDA!", Pal.Gold);
                    a.World.AddImpact(t.Pos);
                }
                else if (a.MovedTime > 0.8f)
                {
                    bonus *= exposed ? c.RunChargeExposed : c.RunChargeDamage;
                    a.World.AddText(t.Pos + new Vector2(0, -46), "CARGA!", Pal.Gold);
                }
            }
            dmg *= Math.Min(bonus, c.MaxBonusDamage);

            // --- vantagens de tipo (o "pedra-papel-tesoura" do jogo, sem limite) ---
            if (at.Type == UnitType.Spearman && tt.HasFlag(Tag.Cavalry)) dmg *= 3f;
            if (at.IsRanged && tt.HasFlag(Tag.Infantry)) dmg *= 1.5f;
            if (at.Tags.HasFlag(Tag.Infantry) && tt.HasFlag(Tag.Ranged)) dmg *= 2f;
            if (at.IsRanged && tt.HasFlag(Tag.Heavy)) dmg *= c.GuardianArrowTaken;
            if (tt.HasFlag(Tag.Siege) && !at.IsRanged) dmg *= 1.5f;

            dmg *= Defense(t, at.IsRanged);
        }
        else if (target is Building)
        {
            dmg *= Math.Min(bonus, c.MaxBonusDamage);
            if (at.IsSiege) { }
            else if (at.IsRanged) dmg *= 0.25f;
            else if (at.IsWorker) dmg *= 0.4f;
            else if (at.Tags.HasFlag(Tag.Cavalry)) dmg *= 0.5f;
            else dmg *= 0.6f;
        }
        return dmg;
    }

    /// <summary>Multiplicador defensivo do alvo (postura, aura, bandeira militar, terreno), com piso.</summary>
    public static float Defense(Unit t, bool ranged)
    {
        var c = Balance.C;
        float reduce = 1f;
        if (t.StoneStance) reduce *= 1 - c.StanceReduction;
        reduce *= 1 - (t.World.FlagBonus(t.Team, FlagBonus.Defense) - 1f);
        if (ranged && t.Def.Type != UnitType.GuardiaoDasGerais)
        {
            // Parede de Escudos: Guardiões protegem aliados próximos de flechas
            foreach (var g in t.World.Units)
                if (g.Team == t.Team && g.Def.Type == UnitType.GuardiaoDasGerais && Vector2.DistanceSquared(g.Pos, t.Pos) < 90 * 90)
                { reduce *= 1 - c.ShieldWallReduction; break; }
        }
        reduce = Math.Max(reduce, 1 - c.MaxDamageReduction);
        // fraqueza do Amazonas em campo aberto (penalidade, fora do limite)
        if (t.Owner.Faction.OpenFieldDamageTaken > 1f && !t.InForest) reduce *= t.Owner.Faction.OpenFieldDamageTaken;
        return reduce;
    }

    public static float TowerDamage(Building b, Entity target)
    {
        float dmg = b.Def.AttackDmg;
        if (target is Unit t)
        {
            if (t.Def.Tags.HasFlag(Tag.Heavy)) dmg *= Balance.C.GuardianArrowTaken;
            dmg *= Defense(t, true);
        }
        if (target is Building) dmg *= 0.3f;
        return dmg;
    }
}
