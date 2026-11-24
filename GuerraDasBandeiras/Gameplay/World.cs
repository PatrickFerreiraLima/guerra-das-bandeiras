using GuerraDasBandeiras.Core;

namespace GuerraDasBandeiras.Gameplay;

public class World
{
    public const int T = Defs.Tile;
    public readonly int W = 80, H = 50;
    public readonly Terrain[,] Ter;
    public readonly byte[,] TileVar;
    public readonly Entity[,] Occ;
    public readonly bool[,] Visible, Explored;   // visão de quem joga nesta máquina (time Local)
    /// <summary>Visão de cada time. Fora do modo em rede só a do time 0 é calculada (a IA enxerga o mapa).</summary>
    public readonly bool[][,] TeamVisible = new bool[2][,], TeamExplored = new bool[2][,];

    /// <summary>Time controlado nesta máquina: 0 no jogo solo; na rede, 0 = anfitrião e 1 = convidado.</summary>
    public readonly int Local;
    public int Foe => 1 - Local;
    public Player Me => Players[Local];
    public Player Enemy => Players[1 - Local];
    /// <summary>Partida 1x1 em rede: simulação simétrica (os dois times têm névoa) e determinística.</summary>
    public readonly bool Versus;

    public readonly List<Unit> Units = new();
    public readonly List<Building> Buildings = new();
    public readonly List<ResourceNode> Resources = new();
    public readonly List<Projectile> Projectiles = new();
    public readonly List<FloatText> Texts = new();
    public readonly List<Fx> Effects = new();
    public readonly List<Decor> Decors = new();
    public readonly List<Scenery.Prop> Props = new();   // paisagem brasileira (só visual)
    public readonly List<Vector2> RoadPoints = new();   // caminhos de terra (apenas visual)
    public readonly List<ControlPoint> Flags = new();
    public readonly Player[] Players = new Player[2];

    public readonly Pathing Pathing;
    public readonly Random Rng;
    /// <summary>Sorteios só de efeitos visuais: separados para que partículas nunca mudem o resultado da partida.</summary>
    public readonly Random FxRng;
    /// <summary>Desliga a criação de partículas (usado nos testes para provar que os efeitos não alteram a simulação).</summary>
    public bool VisualFx = true;
    public readonly GameConfig Config;

    public float Time;
    public int Winner = -1;
    public string WinReason = "";
    public bool HoldToWin = true;
    public const float HoldGoal = 90f;
    public readonly float[] HoldTimer = new float[2];

    public Point[] StartTiles = new Point[2];
    /// <summary>Para que lado cada base "olha": +1 = base a oeste (o rio fica à direita), -1 = base a leste.</summary>
    public readonly int[] BaseDir = { 1, -1 };
    /// <summary>+1 ou -1: o "kit" de recursos da base pode vir espelhado na vertical (madeira em cima ou embaixo).</summary>
    public readonly int[] BaseFlip = { 1, 1 };

    // Ganchos para a tela (mensagens, sons, alertas)
    public Action<string, Color> Notify;
    public Action<Entity, Entity> Damaged;
    public Func<Vector2, bool> OnScreen = _ => true;
    public Action<Building> BuildingCompleted;
    public Action<Unit> UnitTrained;

    int _nextId;
    float _fogTimer;

    public World(GameConfig cfg)
    {
        Config = cfg;
        Rng = new Random(cfg.Seed);
        FxRng = new Random(cfg.Seed ^ 0x5F3759);
        Ter = new Terrain[W, H];
        TileVar = new byte[W, H];
        Occ = new Entity[W, H];
        Local = cfg.Versus ? cfg.LocalTeam : 0;
        Versus = cfg.Versus;
        for (int t = 0; t < 2; t++) { TeamVisible[t] = new bool[W, H]; TeamExplored[t] = new bool[W, H]; }
        Visible = TeamVisible[Local];
        Explored = TeamExplored[Local];
        Pathing = new Pathing(this);

        var pf = Defs.Faction(cfg.PlayerFaction);
        var ef = Defs.Faction(cfg.EnemyFaction);
        Players[0] = new Player { Id = 0, Faction = pf, ColorSet = pf.ColorSet };
        Players[1] = new Player { Id = 1, Faction = ef, ColorSet = ef.ColorSet == pf.ColorSet ? 0 : ef.ColorSet, IsAI = !cfg.Versus };
        if (!cfg.Versus)
        {
            // dificuldade só existe contra a IA; entre pessoas os dois lados são iguais
            Players[1].GatherMult = cfg.Difficulty switch { 0 => 0.75f, 2 => 1.25f, _ => 1f };
            Players[1].ProdMult = cfg.Difficulty switch { 0 => 0.85f, 2 => 1.15f, _ => 1f };
        }
        foreach (var p in Players) { p.Food = 150; p.Wood = 60; p.Ore = 40; p.PowerCd = Balance.C.FirstPowerDelay; }

        MapGen.Build(this);
        UpdateFog();
    }

    // ---------------- Grade ----------------

    public bool InBounds(Point p) => p.X >= 0 && p.Y >= 0 && p.X < W && p.Y < H;
    public Point TileOf(Vector2 p) => new((int)MathF.Floor(p.X / T), (int)MathF.Floor(p.Y / T));
    public Vector2 CenterOf(Point t) => new(t.X * T + T / 2f, t.Y * T + T / 2f);
    public Terrain TerrainAt(Vector2 p) { var t = TileOf(p); return InBounds(t) ? Ter[t.X, t.Y] : Terrain.Water; }
    public Terrain TerrainAt(int x, int y) => x >= 0 && y >= 0 && x < W && y < H ? Ter[x, y] : Terrain.Grass;
    public bool Passable(Point t) => InBounds(t) && Ter[t.X, t.Y] != Terrain.Water && Occ[t.X, t.Y] == null;

    // ---------------- Criação ----------------

    public Unit AddUnit(UnitType type, int team, Vector2 pos)
    {
        var def = Defs.Units[type];
        var u = new Unit { Id = ++_nextId, World = this, Def = def, Team = team, Owner = Players[team], Pos = pos, Hp = def.Hp, MaxHp = def.Hp, GuardPos = pos };
        Units.Add(u);
        return u;
    }

    public Building AddBuilding(BuildingType type, int team, Point tile, bool done)
    {
        var def = Defs.Buildings[type];
        var owner = Players[team];
        float hp = def.Hp * owner.Faction.BuildingHp;
        var b = new Building
        {
            Id = ++_nextId, World = this, Def = def, Team = team, Owner = owner, Tile = tile,
            MaxHp = hp, Hp = done ? hp : hp * 0.1f, Done = done, Progress = done ? 1 : 0
        };
        b.Pos = new Vector2((tile.X + def.Size / 2f) * T, (tile.Y + def.Size / 2f) * T);
        for (int x = 0; x < def.Size; x++)
            for (int y = 0; y < def.Size; y++)
                Occ[tile.X + x, tile.Y + y] = b;
        Buildings.Add(b);
        PushUnitsOut(b);
        RecountPop();
        return b;
    }

    public ResourceNode AddResource(Res kind, Point tile, int amount, int sprite)
    {
        if (!InBounds(tile) || Occ[tile.X, tile.Y] != null || Ter[tile.X, tile.Y] == Terrain.Water || Ter[tile.X, tile.Y] == Terrain.Ford) return null;
        var n = new ResourceNode { Id = ++_nextId, World = this, Kind = kind, Tile = tile, Amount = amount, Initial = amount, Sprite = sprite, Pos = CenterOf(tile), Hp = 1, MaxHp = 1 };
        Occ[tile.X, tile.Y] = n;
        Resources.Add(n);
        return n;
    }

    public bool CanPlace(BuildingType type, Point tl, int team) => PlaceProblem(type, tl, team) == null;

    /// <summary>Retorna o motivo pelo qual não dá para construir ali (ou null se pode).</summary>
    public string PlaceProblem(BuildingType type, Point tl, int team)
    {
        int s = Defs.Buildings[type].Size;
        for (int x = 0; x < s; x++)
            for (int y = 0; y < s; y++)
            {
                var p = new Point(tl.X + x, tl.Y + y);
                if (!InBounds(p) || p.X == 0 || p.Y == 0 || p.X == W - 1 || p.Y == H - 1) return "Fora do mapa";
                if ((team == 0 || Versus) && !TeamExplored[team][p.X, p.Y]) return "Área não explorada";
                var t = Ter[p.X, p.Y];
                if (t == Terrain.Water || t == Terrain.Ford) return "Não dá para construir na água";
                if (t == Terrain.Forest) return "A mata é densa demais aqui";
                if (Occ[p.X, p.Y] is ResourceNode) return "Há recursos no caminho";
                if (Occ[p.X, p.Y] != null) return "Espaço ocupado";
            }
        var center = new Vector2((tl.X + s / 2f) * T, (tl.Y + s / 2f) * T);
        foreach (var f in Flags) if (Vector2.Distance(center, f.Pos) < ControlPoint.Radius + s * T / 2f) return "Muito perto de uma bandeira";
        foreach (var u in Units)
            if (u.Team != team && u.Pos.X > tl.X * T - 8 && u.Pos.X < (tl.X + s) * T + 8 && u.Pos.Y > tl.Y * T - 8 && u.Pos.Y < (tl.Y + s) * T + 8) return "Inimigos no local";
        return null;
    }

    public Building PlaceBuilding(BuildingType type, Point tl, int team)
    {
        var p = Players[team];
        var def = Defs.Buildings[type];
        if (!CanPlace(type, tl, team) || !p.CanPay(def.Cost)) return null;
        p.Pay(def.Cost);
        AddDust(CenterOf(new Point(tl.X + s2(def), tl.Y + s2(def))), 3);
        return AddBuilding(type, team, tl, false);
        static int s2(BuildingDef d) => d.Size / 2;
    }

    void PushUnitsOut(Building b)
    {
        var r = b.WorldRect;
        foreach (var u in Units)
        {
            if (!r.Contains(u.Pos)) continue;
            var free = FreeTileAround(b, u.Pos);
            if (free is Point f) u.Pos = CenterOf(f);
        }
    }

    public Point? FreeTileAround(Building b, Vector2 prefer)
    {
        Point? best = null;
        float bestD = float.MaxValue;
        for (int ring = 1; ring <= 4 && best == null; ring++)
        {
            for (int x = b.Tile.X - ring; x < b.Tile.X + b.Size + ring; x++)
                for (int y = b.Tile.Y - ring; y < b.Tile.Y + b.Size + ring; y++)
                {
                    bool edge = x == b.Tile.X - ring || y == b.Tile.Y - ring || x == b.Tile.X + b.Size + ring - 1 || y == b.Tile.Y + b.Size + ring - 1;
                    if (!edge) continue;
                    var p = new Point(x, y);
                    if (!Passable(p)) continue;
                    float d = Vector2.DistanceSquared(CenterOf(p), prefer);
                    if (d < bestD) { bestD = d; best = p; }
                }
        }
        return best;
    }

    // ---------------- Poder do comandante ----------------

    public bool UsePower(int team)
    {
        var p = Players[team];
        if (!p.PowerReady) return false;
        var f = p.Faction;
        p.PowerTime = f.PowerDur;
        p.PowerCd = f.PowerCd;
        p.PowersUsed++;
        switch (f.Id)
        {
            case FactionId.Amazonas:
                // só quem já está na vegetação se camufla
                foreach (var u in Units) if (u.Team == team && u.InForest) { u.CamoTime = f.PowerDur; u.RevealTimer = 0; AddRing(u.Pos, Pal.GreenLight); }
                break;
            case FactionId.MinasGerais:
                foreach (var b in Buildings) if (b.Team == team && b.Done) { b.Hp = Math.Min(b.MaxHp, b.Hp + b.MaxHp * Balance.C.FortifyHeal); AddRing(b.Pos, p.Color, b.Radius); }
                break;
            case FactionId.RioGrandeDoSul:
                foreach (var u in Units) if (u.Team == team && u.IsMilitary) AddRing(u.Pos, Pal.Gold);
                break;
        }
        Notify?.Invoke(team == Local ? $"{f.Commander}: {f.Power}!" : $"Comandante inimigo usou {f.Power}!", team == Local ? Pal.Gold : Pal.Red);
        Sfx.Power();
        return true;
    }

    // ---------------- Bandeiras: bônus territoriais ----------------

    /// <summary>Multiplicador do bônus da bandeira (1 = sem bônus). Some na hora em que o controle é perdido.</summary>
    public float FlagBonus(int team, FlagBonus kind)
    {
        foreach (var f in Flags)
            if (f.Owner == team && f.Bonus == kind)
                return kind == Gameplay.FlagBonus.Food ? Balance.C.FlagFoodBonus : 1f + Balance.C.FlagDefense;
        return 1f;
    }

    // ---------------- Atualização ----------------

    public void Update(float dt)
    {
        if (Winner >= 0) dt *= 0.3f; // câmera lenta no final
        Time += dt;

        foreach (var p in Players) { p.PowerCd -= dt; p.PowerTime = Math.Max(0, p.PowerTime - dt); }

        foreach (var u in Units.ToArray()) if (!u.Dead) u.Update(dt);
        Separate(dt);
        // o empurrão entre unidades pode tirar alguém da mata: camuflagem só vale dentro da vegetação
        foreach (var u in Units) if (u.CamoTime > 0 && !u.InForest) { u.CamoTime = 0; if (u.Def.Type == UnitType.VigiaDaMata) u.AbilityTime = 0; }
        foreach (var b in Buildings.ToArray()) if (!b.Dead) UpdateBuilding(b, dt);
        UpdateProjectiles(dt);
        UpdateFlags(dt);

        foreach (var t in Texts) { t.Life -= dt; t.Pos.Y -= 22 * dt; }
        Texts.RemoveAll(t => t.Life <= 0);
        foreach (var e in Effects)
        {
            e.Life -= dt;
            e.Pos += e.Vel * dt;
            if (e.Kind == Fx.Chip || e.Kind == Fx.Spark) e.Vel.Y += 500 * dt;
            if (e.Kind == Fx.Smoke) e.Vel.X += 6 * dt;
            if (e.Kind == Fx.Leaf) { e.Vel.X = MathF.Sin((e.MaxLife - e.Life) * 6 + e.Rot) * 28; e.Rot += dt * 5; }
        }
        Effects.RemoveAll(e => e.Life <= 0);
        if (Effects.Count > 500) Effects.RemoveRange(0, Effects.Count - 500); // teto de partículas
        foreach (var e in Units) e.HitFlash = Math.Max(0, e.HitFlash - dt);
        foreach (var e in Buildings) e.HitFlash = Math.Max(0, e.HitFlash - dt);
        foreach (var r in Resources) r.Shake = Math.Max(0, r.Shake - dt);

        Units.RemoveAll(u => u.Dead);
        Buildings.RemoveAll(b => b.Dead);
        Resources.RemoveAll(r => r.Dead);
        RecountPop();

        _fogTimer -= dt;
        if (_fogTimer <= 0) { _fogTimer = 0.15f; UpdateFog(); }

        CheckVictory();
    }

    void UpdateBuilding(Building b, float dt)
    {
        if (!b.Done) return;
        var owner = b.Owner;

        // passivo do Tomás Ferraz: construções se recuperam sozinhas fora de combate
        if (owner.Faction.Id == FactionId.MinasGerais && b.Hp < b.MaxHp && Time - b.LastDamaged > 8f)
            b.Hp = Math.Min(b.MaxHp, b.Hp + b.MaxHp * 0.006f * dt);

        if (b.Def.Type == BuildingType.Oficina && FxRng.NextDouble() < dt * 1.5)
            AddSmoke(new Vector2(b.WorldRect.Right - 26, b.WorldRect.Y + 12));

        if (b.Queue.Count > 0)
        {
            var def = Defs.Units[b.Queue[0]];
            b.PopBlocked = owner.Pop + def.Pop > owner.PopCap;
            if (!b.PopBlocked)
            {
                b.ProdTimer += dt * owner.ProdMult;
                if (b.ProdTimer >= def.Time)
                {
                    b.ProdTimer = 0;
                    b.Queue.RemoveAt(0);
                    SpawnFrom(b, def.Type);
                }
            }
        }
        else b.PopBlocked = false;

        if (b.Def.AttackDmg > 0)
        {
            b.AttackTimer -= dt;
            if (b.AttackTimer <= 0)
            {
                var target = FindEnemyFor(b.Team, b.Pos, b.AttackRange + b.Radius, unitsOnly: true);
                if (target != null)
                {
                    b.AttackTimer = b.Def.AttackCd;
                    var pr = new Projectile { Pos = b.Pos + new Vector2(0, -50), Target = target, Source = b, Damage = Combat.TowerDamage(b, target), Team = b.Team };
                    pr.Start = pr.Pos; pr.Total = Vector2.Distance(pr.Pos, target.Pos);
                    Projectiles.Add(pr);
                }
                else b.AttackTimer = 0.3f;
            }
        }
    }

    public void SpawnFrom(Building b, UnitType type)
    {
        var prefer = b.Rally ?? new Vector2(b.Pos.X, b.WorldRect.Bottom + T);
        var free = FreeTileAround(b, prefer);
        var pos = free is Point f ? CenterOf(f) : b.Pos + new Vector2(0, b.Radius + 20);
        var u = AddUnit(type, b.Team, pos);
        b.Owner.UnitsTrained++;
        if (u.IsMilitary) b.Owner.MilitaryTrained++;
        AddDust(pos, 1);
        if (b.RallyTarget is ResourceNode rn && !rn.Dead && u.IsWorker) u.CmdGather(rn);
        else if (b.Rally is Vector2 r) { if (u.IsMilitary) u.CmdAttackMove(r); else u.CmdMove(r); }
        UnitTrained?.Invoke(u);
    }

    public void OnBuildingCompleted(Building b)
    {
        b.Owner.BuildingsBuilt++;
        AddDust(b.Pos + new Vector2(0, b.Radius * 0.6f), 3);
        RecountPop();
        BuildingCompleted?.Invoke(b);
    }

    void RecountPop()
    {
        foreach (var p in Players) { p.Pop = 0; p.PopCap = 0; }
        foreach (var u in Units) if (!u.Dead) Players[u.Team].Pop += u.Def.Pop;
        foreach (var b in Buildings) if (!b.Dead && b.Done) Players[b.Team].PopCap += b.Def.PopBonus;
        foreach (var p in Players) p.PopCap = Math.Min(p.PopCap, Defs.MaxPop);
    }

    /// <summary>Empurra unidades que se sobrepõem, sem deixá-las entrar em tiles bloqueados.</summary>
    void Separate(float dt)
    {
        int n = Units.Count;
        for (int i = 0; i < n; i++)
        {
            var a = Units[i];
            for (int j = i + 1; j < n; j++)
            {
                var b = Units[j];
                var d = b.Pos - a.Pos;
                float min = a.Radius + b.Radius - 4;
                float ls = d.LengthSquared();
                if (ls >= min * min) continue;
                float len = MathF.Sqrt(ls);
                Vector2 dir = len < 0.01f ? new Vector2((float)Rng.NextDouble() - 0.5f, (float)Rng.NextDouble() - 0.5f) : d / len;
                float push = (min - len) * Math.Min(1f, dt * 10f);
                float wa = a.MovedThisFrame || a.IsIdle ? 0.5f : 0.15f;
                float wb = b.MovedThisFrame || b.IsIdle ? 0.5f : 0.15f;
                if (a.StoneStance) wa = 0.02f;
                if (b.StoneStance) wb = 0.02f;
                float tot = wa + wb;
                TryNudge(a, -dir * push * (wa / tot));
                TryNudge(b, dir * push * (wb / tot));
            }
        }
    }

    void TryNudge(Unit u, Vector2 delta)
    {
        var np = u.Pos + delta;
        np.X = Math.Clamp(np.X, 4, W * T - 4);
        np.Y = Math.Clamp(np.Y, 4, H * T - 4);
        if (Passable(TileOf(np))) u.Pos = np;
    }

    void UpdateProjectiles(float dt)
    {
        foreach (var p in Projectiles)
        {
            if (p.Target == null || p.Target.Dead) { p.Done = true; continue; }
            var tp = p.Target is Unit ? p.Target.Pos + new Vector2(0, -14) : p.Target.Pos;
            var d = tp - p.Pos;
            float len = d.Length();
            float step = p.Speed * dt;
            if (len <= step + 4)
            {
                p.Done = true;
                Damage(p.Target, p.Damage, p.Source);
                if (p.Target is Unit) AddSparks(tp, new Color(255, 230, 180), 3);
                Sfx.ArrowHit(this, tp);
            }
            else { p.Pos += d / len * step; p.Traveled += step; }
        }
        Projectiles.RemoveAll(p => p.Done);
    }

    // ---------------- Bandeiras e vitória ----------------

    void UpdateFlags(float dt)
    {
        foreach (var f in Flags)
        {
            int c0 = 0, c1 = 0;
            foreach (var u in Units)
            {
                // os pontos exigem presença militar: trabalhadores e aríetes não capturam
                if (u.Dead || !u.IsMilitary || u.Def.IsSiege) continue;
                if (Vector2.DistanceSquared(u.Pos, f.Pos) > ControlPoint.Radius * ControlPoint.Radius) continue;
                if (u.Team == 0) c0++; else c1++;
            }
            f.Contested = c0 > 0 && c1 > 0;
            f.Capturer = f.Contested || (c0 == 0 && c1 == 0) ? -1 : c0 > 0 ? 0 : 1;
            if (f.Contested || (c0 == 0 && c1 == 0)) continue;
            int team = c0 > 0 ? 0 : 1;
            int count = Math.Min(4, Math.Max(c0, c1));
            float rate = 9f + 4f * count; // pontos por segundo (escala -100..100)
            float sign = team == 0 ? 1 : -1;
            float before = f.Progress;
            f.Progress = Math.Clamp(f.Progress + sign * rate * dt, -100, 100);

            if (f.Owner != team && Math.Abs(f.Progress) >= 100 && Math.Sign(f.Progress) == (int)sign)
            {
                f.Owner = team;
                if (Players[team].FirstFlagTime < 0) Players[team].FirstFlagTime = Time;
                Players[team].FlagsCaptured++;
                Notify?.Invoke(team == Local ? $"Você capturou a {f.Name}! Bônus: {f.BonusText}" : $"O inimigo capturou a {f.Name} ({f.BonusText} para ele).", team == Local ? Pal.GreenLight : Pal.Red);
                if (team == Local) Sfx.FlagCaptured(); else Sfx.FlagLost();
                AddDust(f.Pos, 2, Players[team].Color);
                AddRing(f.Pos, Players[team].Color, ControlPoint.Radius);
            }
            else if (f.Owner >= 0 && f.Owner != team && Math.Sign(before) != Math.Sign(f.Progress))
            {
                if (f.Owner == 0) { Notify?.Invoke($"Você perdeu a {f.Name}: bônus de {f.BonusText} removido.", Pal.Red); Sfx.FlagLost(); }
                else Notify?.Invoke($"Bandeira neutralizada: o inimigo perdeu o bônus da {f.Name}.", Pal.GreenLight);
                f.Owner = -1; // neutralizada no caminho para a captura (bônus sai na hora)
            }
        }

        for (int t = 0; t < 2; t++)
        {
            bool both = Flags.Count > 0 && Flags.All(f => f.Owner == t);
            HoldTimer[t] = both ? HoldTimer[t] + dt : 0;
        }
    }

    void CheckVictory()
    {
        if (Winner >= 0) return;
        for (int t = 0; t < 2; t++)
        {
            if (!Buildings.Any(b => b.Team == t && b.Def.Type == BuildingType.Centro))
            {
                Winner = 1 - t;
                WinReason = t != Local ? "O Centro de Comando inimigo foi destruído." : "Seu Centro de Comando foi destruído.";
                return;
            }
            if (HoldToWin && HoldTimer[t] >= HoldGoal)
            {
                Winner = t;
                WinReason = t == Local ? "Você dominou as duas bandeiras por 90 segundos." : "O inimigo dominou as duas bandeiras por 90 segundos.";
                return;
            }
        }
    }

    public void SetWinner(int team, string reason)
    {
        if (Winner >= 0) return;
        Winner = team;
        WinReason = reason;
    }

    // ---------------- Combate ----------------

    public void Shoot(Unit u, Entity target, float dmg)
    {
        var p = new Projectile { Pos = u.Pos + new Vector2(0, -20), Target = target, Source = u, Damage = dmg, Team = u.Team };
        p.Start = p.Pos; p.Total = Vector2.Distance(p.Pos, target.Pos);
        Projectiles.Add(p);
    }

    public void Damage(Entity target, float dmg, Entity source)
    {
        if (target.Dead) return;
        if (target is Building tb)
        {
            tb.LastDamaged = Time;
            if (tb.Owner.PowerActive && tb.Owner.Faction.Id == FactionId.MinasGerais) dmg *= 1 - Balance.C.FortifyReduction;
        }
        target.Hp -= dmg;
        target.HitFlash = 0.15f;
        if (target is Unit su && su.StoneStance) AddStones(su.Pos + new Vector2(su.FacingLeft ? -10 : 10, -16), 3); // o escudo segurou parte do golpe
        Damaged?.Invoke(target, source);

        // reação: unidades militares paradas revidam
        if (target is Unit tu && source != null && !source.Dead && source.Team != tu.Team)
        {
            if (tu.IsMilitary && !tu.Def.IsSiege && (tu.Order == UnitOrder.Idle || (tu.Order == UnitOrder.Attack && tu.Target is Building)) && CanSee(tu.Team, source))
                tu.CmdAttack(source, auto: true);
        }

        if (target.Hp <= 0) Kill(target, source);
    }

    public void Kill(Entity e, Entity killer)
    {
        if (e.Dead) return;
        e.Dead = true;
        if (killer != null && killer.Team >= 0 && killer.Team != e.Team) Players[killer.Team].Kills++;
        if (e.Team >= 0) { Players[e.Team].Losses++; if (e is Unit) Players[e.Team].UnitsLost++; else if (e is Building) Players[e.Team].BuildingsLost++; }

        if (e is Building b)
        {
            for (int x = 0; x < b.Size; x++)
                for (int y = 0; y < b.Size; y++)
                    if (Occ[b.Tile.X + x, b.Tile.Y + y] == b) Occ[b.Tile.X + x, b.Tile.Y + y] = null;
            AddDust(b.Pos, 6, new Color(120, 110, 100));
            for (int i = 0; i < b.Size * 2; i++)
                Decors.Add(new Decor { Pos = b.Pos + new Vector2(FxRng.Next(-b.Size * 24, b.Size * 24), FxRng.Next(-b.Size * 16, b.Size * 20)), Sprite = FxRng.Next(2) == 0 ? 14 : 13, Scale = 1.3f });
            Decors.Add(new Decor { Pos = b.Pos + new Vector2(0, 10), Sprite = 5, Scale = 1.4f });
            Sfx.BuildingDestroyed(this, b.Pos);
            if (!b.Done) Players[b.Team].Refund(new Cost(b.Def.Cost.Food / 2, b.Def.Cost.Wood / 2, b.Def.Cost.Ore / 2));
        }
        else if (e is Unit u)
        {
            AddDust(u.Pos, 1, new Color(200, 200, 200));
            Sfx.Death(this, u.Pos);
            Effects.Add(new Fx
            {
                Kind = Fx.Corpse, Pos = u.Pos + new Vector2(0, 8), Life = 3f, MaxLife = 3f, Tex = Defs.UnitSprite(u.Def, u.Owner.ColorSet, u.Owner.Faction.Id),
                Flip = u.FacingLeft, Rot = u.FacingLeft ? -MathHelper.PiOver2 : MathHelper.PiOver2, Scale = Defs.SpriteScale(u.Def) * u.Def.Scale, Color = Color.White
            });
        }
    }

    public void Deplete(ResourceNode n)
    {
        n.Dead = true;
        if (Occ[n.Tile.X, n.Tile.Y] == n) Occ[n.Tile.X, n.Tile.Y] = null;
        if (n.Kind == Res.Wood) Decors.Add(new Decor { Pos = n.Pos + new Vector2(0, 14), Sprite = 4, Scale = 1f });
        AddDust(n.Pos, 1, Defs.ResColor(n.Kind));
    }

    // ---------------- Consultas ----------------

    /// <summary>Camuflagem: escondido na mata (unidades furtivas) ou por habilidade/poder.</summary>
    public bool IsHiddenFrom(Unit u, int team)
    {
        if (u.Team == team || u.RevealTimer > 0) return false;
        bool forest = u.Def.Tags.HasFlag(Tag.Stealth) && TerrainAt(u.Pos) == Terrain.Forest;
        bool camo = u.CamoTime > 0 && TerrainAt(u.Pos) == Terrain.Forest; // camuflagem só na vegetação
        if (!forest && !camo) return false;
        float detect = camo ? Balance.C.CamoDetectRadius : 90;
        foreach (var o in Units)
            if (o.Team == team && Vector2.DistanceSquared(o.Pos, u.Pos) < detect * detect) return false;
        return true;
    }

    public bool CanSee(int team, Entity e)
    {
        if (e.Team == team) return true;
        if (e is Unit u && IsHiddenFrom(u, team)) return false;
        if (!Versus && team != 0) return true; // a IA enxerga o mapa (exceto unidades camufladas)
        var vis = TeamVisible[team];
        if (e is Building b)
        {
            for (int x = 0; x < b.Size; x++)
                for (int y = 0; y < b.Size; y++)
                    if (vis[b.Tile.X + x, b.Tile.Y + y]) return true;
            return false;
        }
        var t = TileOf(e.Pos);
        return InBounds(t) && vis[t.X, t.Y];
    }

    public bool IsExplored(Entity e)
    {
        var r = e.TileRect;
        for (int x = r.X; x < r.Right; x++)
            for (int y = r.Y; y < r.Bottom; y++)
                if (InBounds(new Point(x, y)) && Explored[x, y]) return true;
        return false;
    }

    public Entity FindEnemyFor(int team, Vector2 pos, float range, bool unitsOnly = false, bool buildingsOnly = false)
    {
        Entity best = null;
        float bestD = range * range;
        if (!buildingsOnly)
        {
            foreach (var u in Units)
            {
                if (u.Dead || u.Team == team) continue;
                float d = Vector2.DistanceSquared(u.Pos, pos);
                if (u.IsWorker || u.Def.IsSiege) d *= 1.6f; // prefere soldados
                if (d < bestD && CanSee(team, u)) { bestD = d; best = u; }
            }
            if (best != null || unitsOnly) return best;
        }
        bestD = range * range;
        foreach (var b in Buildings)
        {
            if (b.Dead || b.Team == team) continue;
            float d = Vector2.DistanceSquared(b.Pos, pos) - b.Radius * b.Radius;
            if (d < bestD && CanSee(team, b)) { bestD = d; best = b; }
        }
        return best;
    }

    public Building NearestDropoff(int team, Vector2 pos)
    {
        Building best = null;
        float bestD = float.MaxValue;
        foreach (var b in Buildings)
        {
            if (b.Team != team || !b.Done || !b.Def.Dropoff || b.Dead) continue;
            float d = Vector2.DistanceSquared(b.Pos, pos);
            if (d < bestD) { bestD = d; best = b; }
        }
        return best;
    }

    public ResourceNode NearestResource(Res kind, Vector2 pos, float maxDist, Unit forUnit = null)
    {
        ResourceNode best = null;
        float bestD = maxDist * maxDist;
        foreach (var r in Resources)
        {
            if (r.Dead || r.Kind != kind) continue;
            float d = Vector2.DistanceSquared(r.Pos, pos);
            // evita amontoar todo mundo no mesmo recurso
            if (forUnit != null)
                foreach (var u in Units)
                    if (u != forUnit && u.GatherNode == r && u.Team == forUnit.Team) d += 40 * 40;
            if (d < bestD) { bestD = d; best = r; }
        }
        return best;
    }

    public ResourceNode NearestAnyResource(Vector2 pos, float maxDist)
    {
        ResourceNode best = null;
        float bestD = maxDist * maxDist;
        foreach (var r in Resources)
        {
            float d = Vector2.DistanceSquared(r.Pos, pos);
            if (!r.Dead && d < bestD) { bestD = d; best = r; }
        }
        return best;
    }

    // ---------------- Névoa ----------------

    /// <summary>Só para capturas de teste (cenário "scenery"): mostra o mapa inteiro, sem névoa.</summary>
    public bool RevealAll;

    public void UpdateFog()
    {
        // no jogo solo só o time 0 tem névoa; na rede os dois times têm (e isso entra na simulação)
        int teams = Versus ? 2 : 1;
        for (int t = 0; t < teams; t++)
        {
            var vis = TeamVisible[t];
            var exp = TeamExplored[t];
            Array.Clear(vis);
            foreach (var u in Units) if (u.Team == t) Reveal(vis, exp, TileOf(u.Pos), u.SightPx / T);
            foreach (var b in Buildings) if (b.Team == t) Reveal(vis, exp, TileOf(b.Pos), b.Def.Sight + b.Size / 2f);
        }
        if (RevealAll) for (int x = 0; x < W; x++) for (int y = 0; y < H; y++) { Visible[x, y] = true; Explored[x, y] = true; }
    }

    void Reveal(bool[,] vis, bool[,] exp, Point c, float r)
    {
        int ri = (int)MathF.Ceiling(r);
        float r2 = r * r + r;
        for (int dy = -ri; dy <= ri; dy++)
            for (int dx = -ri; dx <= ri; dx++)
            {
                if (dx * dx + dy * dy > r2) continue;
                int x = c.X + dx, y = c.Y + dy;
                if (x < 0 || y < 0 || x >= W || y >= H) continue;
                vis[x, y] = true;
                exp[x, y] = true;
            }
    }

    // ---------------- Rede ----------------

    /// <summary>Procura uma entidade viva pelo Id (as ordens em rede referenciam unidades e prédios por Id).</summary>
    public Entity Find(int id)
    {
        if (id <= 0) return null;
        foreach (var u in Units) if (u.Id == id) return u.Dead ? null : u;
        foreach (var b in Buildings) if (b.Id == id) return b.Dead ? null : b;
        foreach (var r in Resources) if (r.Id == id) return r.Dead ? null : r;
        return null;
    }

    /// <summary>
    /// Impressão digital do estado da simulação (posições, vida, ordens, recursos, bandeiras).
    /// As duas máquinas comparam este número de tempos em tempos para detectar dessincronização.
    /// Efeitos visuais ficam de fora: eles podem variar de uma máquina para a outra sem problema.
    /// </summary>
    public uint Checksum()
    {
        uint h = 2166136261;
        void Mix(int v) { unchecked { h = (h ^ (uint)v) * 16777619; } }
        void MixF(float v) => Mix(BitConverter.SingleToInt32Bits(v));
        Mix(Units.Count); Mix(Buildings.Count); Mix(Resources.Count);
        foreach (var u in Units) { Mix(u.Id); Mix(u.Team); MixF(u.Pos.X); MixF(u.Pos.Y); MixF(u.Hp); Mix((int)u.Order); Mix(u.Cargo); }
        foreach (var b in Buildings) { Mix(b.Id); MixF(b.Hp); MixF(b.Progress); Mix(b.Queue.Count); MixF(b.ProdTimer); }
        foreach (var r in Resources) { Mix(r.Id); Mix(r.Amount); }
        foreach (var p in Players) { Mix(p.Food); Mix(p.Wood); Mix(p.Ore); MixF(p.PowerCd); }
        foreach (var f in Flags) { Mix(f.Owner); MixF(f.Progress); }
        MixF(HoldTimer[0]); MixF(HoldTimer[1]); MixF(Time);
        return h;
    }

    // ---------------- Efeitos ----------------

    public void AddText(Vector2 pos, string text, Color c) => Texts.Add(new FloatText { Pos = pos, Text = text, Color = c });

    public void AddDust(Vector2 pos, int size = 0, Color? color = null)
    {
        if (!VisualFx) return;
        var c = color ?? new Color(214, 196, 160);
        int n = size == 0 ? 1 : 4 + size * 2;
        for (int i = 0; i < n; i++)
        {
            float life = 0.4f + (float)FxRng.NextDouble() * 0.5f + size * 0.1f;
            float spread = size == 0 ? 6 : 30 * size;
            Effects.Add(new Fx
            {
                Kind = Fx.Dust,
                Pos = pos + new Vector2((float)(FxRng.NextDouble() - 0.5) * spread, (float)(FxRng.NextDouble() - 0.5) * spread * 0.6f),
                Vel = new Vector2((float)(FxRng.NextDouble() - 0.5) * 20, -10 - (float)FxRng.NextDouble() * 15),
                Life = life, MaxLife = life, Size = size == 0 ? 5 + (float)FxRng.NextDouble() * 4 : 10 + size * 4 + (float)FxRng.NextDouble() * 10, Color = c
            });
        }
    }

    public void AddChips(Vector2 pos, Res kind)
    {
        if (!VisualFx) return;
        var c = kind switch { Res.Wood => new Color(196, 140, 80), Res.Ore => new Color(190, 200, 215), _ => new Color(220, 60, 60) };
        for (int i = 0; i < 4; i++)
        {
            float life = 0.35f + (float)FxRng.NextDouble() * 0.25f;
            Effects.Add(new Fx
            {
                Kind = Fx.Chip, Pos = pos, Vel = new Vector2((float)(FxRng.NextDouble() - 0.5) * 140, -90 - (float)FxRng.NextDouble() * 90),
                Life = life, MaxLife = life, Size = 2.5f + (float)FxRng.NextDouble() * 2, Color = i == 0 && kind == Res.Food ? new Color(60, 150, 70) : c
            });
        }
    }

    public void AddSparks(Vector2 pos, Color c, int n = 5)
    {
        if (!VisualFx) return;
        for (int i = 0; i < n; i++)
        {
            float life = 0.2f + (float)FxRng.NextDouble() * 0.15f;
            Effects.Add(new Fx
            {
                Kind = Fx.Spark, Pos = pos, Vel = new Vector2((float)(FxRng.NextDouble() - 0.5) * 220, -60 - (float)FxRng.NextDouble() * 120),
                Life = life, MaxLife = life, Size = 2f, Color = c
            });
        }
    }

    public void AddSmoke(Vector2 pos)
    {
        if (!VisualFx) return;
        float life = 2.2f + (float)FxRng.NextDouble();
        Effects.Add(new Fx { Kind = Fx.Smoke, Pos = pos, Vel = new Vector2(4, -22), Life = life, MaxLife = life, Size = 6, Color = new Color(210, 210, 205) });
    }

    /// <summary>Folhas caindo e girando (Camuflagem / emboscada).</summary>
    public void AddLeaves(Vector2 pos, int n)
    {
        if (!VisualFx) return;
        for (int i = 0; i < n; i++)
        {
            float life = 0.8f + (float)FxRng.NextDouble() * 0.7f;
            Effects.Add(new Fx
            {
                Kind = Fx.Leaf, Pos = pos + new Vector2((float)(FxRng.NextDouble() - 0.5) * 30, -20 - (float)FxRng.NextDouble() * 25),
                Vel = new Vector2((float)(FxRng.NextDouble() - 0.5) * 40, 10 + (float)FxRng.NextDouble() * 15),
                Life = life, MaxLife = life, Size = 3 + (float)FxRng.NextDouble() * 2, Rot = (float)FxRng.NextDouble() * 6.28f,
                Color = FxRng.Next(2) == 0 ? new Color(60, 150, 70) : new Color(110, 185, 80)
            });
        }
    }

    /// <summary>Lascas de pedra (Postura de Pedra ativando ou segurando um golpe).</summary>
    public void AddStones(Vector2 pos, int n)
    {
        if (!VisualFx) return;
        for (int i = 0; i < n; i++)
        {
            float life = 0.3f + (float)FxRng.NextDouble() * 0.3f;
            Effects.Add(new Fx
            {
                Kind = Fx.Chip, Pos = pos, Vel = new Vector2((float)(FxRng.NextDouble() - 0.5) * 160, -70 - (float)FxRng.NextDouble() * 90),
                Life = life, MaxLife = life, Size = 2.5f + (float)FxRng.NextDouble() * 2, Color = FxRng.Next(2) == 0 ? new Color(150, 150, 145) : new Color(110, 108, 104)
            });
        }
    }

    /// <summary>Impacto da Investida: anel, faíscas e poeira no alvo.</summary>
    public void AddImpact(Vector2 pos)
    {
        if (!VisualFx) return;
        AddRing(pos, Pal.Gold, 30);
        AddSparks(pos + new Vector2(0, -14), new Color(255, 230, 160), 8);
        AddDust(pos, 1, new Color(214, 196, 160));
    }

    public void AddRing(Vector2 pos, Color c, float size = 22) =>
        Effects.Add(new Fx { Kind = Fx.Ring, Pos = pos, Life = 0.6f, MaxLife = 0.6f, Size = size, Color = c });

    public void AddClickMarker(Vector2 pos, Color c) => AddRing(pos, c);

    public void PlayAt(string sfx, Vector2 pos, float vol, float pitch = 0f)
    {
        if (OnScreen(pos)) Assets.Play(sfx, vol, pitch);
    }
}
