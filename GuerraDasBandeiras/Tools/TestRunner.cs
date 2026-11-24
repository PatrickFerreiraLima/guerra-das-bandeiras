using GuerraDasBandeiras.Core;
using GuerraDasBandeiras.Gameplay;
using GuerraDasBandeiras.Screens;

namespace GuerraDasBandeiras.Tools;

/// <summary>
/// Testes automáticos de regras: "GuerraDasBandeiras --test resultado.txt".
/// Cada teste monta uma situação no mundo real do jogo, simula e confere o resultado.
/// </summary>
public static partial class TestRunner
{
    record Result(string Name, bool Pass, string Detail);
    static readonly List<Result> _results = new();
    const float Dt = 1 / 30f;

    public static void Run(string outPath)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var tests = new (string, Action)[]
        {
            ("Partida completa AM x MG (IA x IA)", () => FullMatch(0, 1)),
            ("Partida completa MG x RS (IA x IA)", () => FullMatch(1, 2)),
            ("Partida completa RS x AM (IA x IA)", () => FullMatch(2, 0)),
            ("Equilíbrio entre facções (24 partidas IA x IA)", BalanceMatrix),
            ("Vitória ao destruir o Centro inimigo", VictoryByCentro),
            ("Derrota ao perder o próprio Centro", DefeatByCentro),
            ("Vitória por domínio das 2 bandeiras (90s)", VictoryByHold),
            ("Domínio interrompido zera a contagem", HoldResets),
            ("Captura, bônus econômico e perda imediata do bônus", FlagFoodBonus),
            ("Bandeira militar: +10% de resistência", FlagDefenseBonus),
            ("Trabalhadores não capturam bandeiras", WorkersDontCapture),
            ("Camuflagem só na vegetação; emboscada +40%", Camouflage),
            ("Camuflagem some ao sair da mata", CamoBreaksOutsideForest),
            ("Postura de Pedra: -35% de dano", StoneStance),
            ("Investida: 1,6x de dano + atordoamento curto", Charge),
            ("Limites: bônus <= +80% e redução <= 50%", Caps),
            ("Poderes de comandante com recarga", CommanderPowers),
            ("Construção completa e aumenta população", Construction),
            ("Reparo com botão direito (custo de madeira)", Repair),
            ("Reparo grátis para Minas Gerais", RepairMinas),
            ("Destruição libera o terreno", Destruction),
            ("Pathfinding pelo atalho estreito do rio", PathShortcut),
            ("Pathfinding pelas travessias com 30 unidades", PathCrowd),
            ("Destino na água: vai até a margem e avisa", UnreachableWater),
            ("Ordens em fila com Shift", ShiftQueue),
            ("Atalhos: V habilidade, C poder, X parar, F atacar, WASD câmera", Hotkeys),
            ("Tutorial completo (6 passos)", TutorialFlow),
            ("Mouse: arrastar caixa e duplo clique (entrada simulada)", InputDragAndDoubleClick),
            ("Mouse: Shift + botão direito enfileira ordens", InputShiftRightClick),
            ("Mouse: botão direito em prédio danificado repara", InputRightClickRepair),
            ("Todos os sons carregam (OGG)", SoundsLoad),
            ("Seleção das três facções (abas, teclado, Começar)", FactionSelection),
            ("Bandeiras estaduais corretas (cores-chave)", StateFlags),
            ("Fichas das facções usam os dados atuais", SheetsUseLiveData),
            ("Introdução: congela a simulação e pode ser pulada", IntroBehaviour),
            ("Efeitos visuais das habilidades", AbilityEffects),
            ("Efeitos visuais não alteram a partida (determinismo)", EffectsDontChangeOutcome),
            ("Tela de vitória/derrota com estatísticas corretas", EndScreenStats),
            ("Balanceamento inalterado (comparado à referência)", BalanceUnchanged),
            ("Limitador de sons repetidos", SoundLimiter),
            ("Rede: lockstep entre 2 jogos via TCP mantém os mundos idênticos", NetLockstepSync),
            ("Rede: ninguém dá ordens às unidades do adversário", NetOwnership),
            ("Rede: adversário que sai = vitória por abandono", NetOpponentLeaves),
            ("Rede: sala anunciada na rede local e conexão aceita", NetRoomDiscovery),
            ("Rede: guest (time 1) vê o mundo do seu lado (névoa, câmera, HUD)", NetGuestPerspective),
            ("Rede: conexão cai no meio da partida, volta sozinha e o jogo segue igual (+ chat)", NetReconnect),
            ("Rede: código da sala (IP + porta) vai e volta sem erro", NetRoomCode),
            ("Rede: duas salas com o mesmo nome aparecem separadas", NetDuplicateNames),
        };
        foreach (var (name, fn) in tests)
        {
            try { fn(); }
            catch (Exception e) { _results.Add(new Result(name, false, "EXCEÇÃO: " + e.GetType().Name + ": " + e.Message + " @ " + e.StackTrace?.Split('\n').FirstOrDefault()?.Trim())); }
            if (_results.Count == 0 || _results[^1].Name != name) _results.Add(new Result(name, false, "teste não registrou resultado"));
        }
        var lines = _results.Select(r => $"[{(r.Pass ? "PASSOU" : "FALHOU")}] {r.Name}\n         {r.Detail}");
        File.WriteAllText(outPath, string.Join("\n", lines) + $"\n\nTotal: {_results.Count(r => r.Pass)}/{_results.Count} passaram em {sw.ElapsedMilliseconds} ms\n");
    }

    static void Check(string name, bool ok, string detail) => _results.Add(new Result(name, ok, detail));

    // ---------------- auxiliares ----------------

    /// <summary>Ponto do mapa antigo (64x40) levado ao mapa novo (80x50): os testes miram os mesmos lugares (planície, rio, bosques).</summary>
    static Point P(int x, int y) => new((int)MathF.Round(x * 80 / 64f), (int)MathF.Round(y * 50 / 40f));

    static World NewWorld(FactionId p = FactionId.Amazonas, FactionId e = FactionId.MinasGerais, GameMode mode = GameMode.Skirmish) =>
        new(new GameConfig { Mode = mode, PlayerFaction = p, EnemyFaction = e, Seed = 11, RandomBases = false });

    static void Sim(World w, float secs, params AIController[] ais)
    {
        for (float t = 0; t < secs; t += Dt)
        {
            w.Update(Dt);
            foreach (var a in ais) a.Update(Dt);
        }
    }

    static Vector2 GrassNear(World w, Vector2 p)
    {
        var t = w.TileOf(p);
        for (int r = 0; r < 10; r++)
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                {
                    var q = new Point(t.X + dx, t.Y + dy);
                    if (w.Passable(q) && w.Ter[q.X, q.Y] is Terrain.Grass or Terrain.Dirt) return w.CenterOf(q);
                }
        return p;
    }

    static Vector2 ForestTile(World w)
    {
        for (int x = 2; x < 25; x++)
            for (int y = 2; y < 22; y++)
            {
                var q = new Point(x, y);
                bool ok = w.Passable(q) && w.Ter[x, y] == Terrain.Forest;
                for (int dx = -1; dx <= 1 && ok; dx++)
                    for (int dy = -1; dy <= 1 && ok; dy++)
                        ok = w.TerrainAt(x + dx, y + dy) == Terrain.Forest;
                if (ok) return w.CenterOf(q);
            }
        return Vector2.Zero;
    }

    static void ClearUnits(World w) { foreach (var u in w.Units) u.Dead = true; w.Units.Clear(); }

    // ---------------- testes ----------------

    static readonly string[] Abbr = { "AM", "MG", "RS" };

    static void FullMatch(int a, int b)
    {
        string name = $"Partida completa {Abbr[a]} x {Abbr[b]} (IA x IA)";
        var w = NewWorld((FactionId)a, (FactionId)b);
        var ai0 = new AIController(w, 0); var ai1 = new AIController(w, 1);
        int recuos = 0, camoForaMata = 0; float scoutFar = 0;
        int[] espMax = new int[2];
        float[] ofTime = { -1, -1 }; int[] espTrained = new int[2];
        w.UnitTrained = u => { if (u.HasAbility) espTrained[u.Team]++; };
        var prev = new[] { ai0.State, ai1.State };
        for (float t = 0; t < 1800 && w.Winner < 0; t += Dt)
        {
            w.Update(Dt); ai0.Update(Dt); ai1.Update(Dt);
            var ais = new[] { ai0, ai1 };
            for (int k = 0; k < 2; k++)
            {
                if (ais[k].State == AIController.Mode.Recuar && prev[k] != AIController.Mode.Recuar) recuos++;
                prev[k] = ais[k].State;
            }
            for (int k = 0; k < 2; k++) if (ofTime[k] < 0 && w.Buildings.Any(b => b.Team == k && b.Done && b.Def.Type == BuildingType.Oficina)) ofTime[k] = w.Time;
            for (int k = 0; k < 2; k++) espMax[k] = Math.Max(espMax[k], w.Units.Count(u => u.Team == k && u.HasAbility));
            foreach (var u in w.Units)
            {
                if (u.CamoTime > 0 && !u.InForest) camoForaMata++;
                if (u.Def.Type == UnitType.Scout && u.Team == 1) scoutFar = Math.Max(scoutFar, Vector2.Distance(u.Pos, w.CenterOf(w.StartTiles[1])) / World.T);
            }
        }
        var p0 = w.Players[0]; var p1 = w.Players[1];
        bool ok = w.Winner >= 0 && camoForaMata == 0;
        Check(name, ok,
            $"vencedor: {(w.Winner < 0 ? "nenhum" : w.Players[w.Winner].Faction.Name)} em {w.Time / 60:0.0} min ({w.WinReason}) | " +
            $"oficina pronta {(ofTime[0] < 0 ? "não" : $"{ofTime[0] / 60:0.0}")}/{(ofTime[1] < 0 ? "não" : $"{ofTime[1] / 60:0.0}")} min, especiais treinados {espTrained[0]}/{espTrained[1]} (máx em campo {espMax[0]}/{espMax[1]}), habilidades {p0.AbilitiesUsed}/{p1.AbilitiesUsed}, poderes {p0.PowersUsed}/{p1.PowersUsed}, recuos da IA {recuos}, " +
            $"batedor inimigo explorou até {scoutFar:0} tiles da base, camuflagem fora da mata: {camoForaMata}");
    }

    static void BalanceMatrix()
    {
        int[] wins = new int[3], games = new int[3];
        int[] side = new int[2]; var pair = new Dictionary<string, int>();
        float total = 0; int n = 0;
        for (int seed = 1; seed <= 4; seed++)
            for (int a = 0; a < 3; a++)
                for (int b = 0; b < 3; b++)
                {
                    if (a == b) continue;
                    var w = new World(new GameConfig { Mode = GameMode.Skirmish, PlayerFaction = (FactionId)a, EnemyFaction = (FactionId)b, Seed = seed * 97 });
                    var ai0 = new AIController(w, 0); var ai1 = new AIController(w, 1);
                    for (float t = 0; t < 1800 && w.Winner < 0; t += Dt) { w.Update(Dt); ai0.Update(Dt); ai1.Update(Dt); }
                    games[a]++; games[b]++;
                    if (w.Winner == 0) wins[a]++; else if (w.Winner == 1) wins[b]++;
                    // as bases são sorteadas: o lado (oeste/leste) vem do mapa, não do número do time
                    if (w.Winner >= 0) side[w.BaseDir[w.Winner] > 0 ? 0 : 1]++;
                    var key = $"{Abbr[a]} x {Abbr[b]}"; pair[key] = pair.GetValueOrDefault(key) + (w.Winner == 0 ? 1 : 0);
                    total += w.Time; n++;
                }
        string rates = string.Join(", ", Enumerable.Range(0, 3).Select(i => $"{Abbr[i]} {wins[i]}/{games[i]} ({100f * wins[i] / games[i]:0}%)"));
        bool ok = Enumerable.Range(0, 3).All(i => wins[i] / (float)games[i] is >= 0.2f and <= 0.8f);
        Check("Equilíbrio entre facções (24 partidas IA x IA)", ok, $"vitórias: {rates}; por lado: oeste {side[0]}, leste {side[1]}; vitórias do 1º time por confronto (de 4): {string.Join(", ", pair.Select(p => p.Key + " " + p.Value))}; duração média {total / n / 60:0.0} min");
    }

    static void VictoryByCentro()
    {
        var w = NewWorld();
        var c = w.Buildings.First(b => b.Team == 1 && b.Def.Type == BuildingType.Centro);
        w.Damage(c, 1e6f, null);
        Sim(w, 0.2f);
        Check("Vitória ao destruir o Centro inimigo", w.Winner == 0, $"vencedor={w.Winner}, motivo: {w.WinReason}");
    }

    static void DefeatByCentro()
    {
        var w = NewWorld();
        var c = w.Buildings.First(b => b.Team == 0 && b.Def.Type == BuildingType.Centro);
        w.Damage(c, 1e6f, null);
        Sim(w, 0.2f);
        Check("Derrota ao perder o próprio Centro", w.Winner == 1, $"vencedor={w.Winner}, motivo: {w.WinReason}");
    }

    static void PlaceSoldiers(World w, int team, Vector2 at, int n, UnitType t = UnitType.Swordsman)
    {
        for (int i = 0; i < n; i++) w.AddUnit(t, team, GrassNear(w, at) + new Vector2(i * 10 - 10, 0)).CmdMove(at + new Vector2(i * 14 - 14, 0));
    }

    static void VictoryByHold()
    {
        var w = NewWorld();
        ClearUnits(w);
        PlaceSoldiers(w, 0, w.Flags[0].Pos, 3);
        PlaceSoldiers(w, 0, w.Flags[1].Pos, 3);
        Sim(w, 20);
        bool owned = w.Flags.All(f => f.Owner == 0);
        Sim(w, 95);
        Check("Vitória por domínio das 2 bandeiras (90s)", owned && w.Winner == 0, $"as duas capturadas: {owned}; vencedor={w.Winner} aos {w.Time:0}s ({w.WinReason})");
    }

    static void HoldResets()
    {
        var w = NewWorld();
        ClearUnits(w);
        PlaceSoldiers(w, 0, w.Flags[0].Pos, 3);
        PlaceSoldiers(w, 0, w.Flags[1].Pos, 3);
        Sim(w, 50);
        float before = w.HoldTimer[0];
        foreach (var u in w.Units.Where(u => Vector2.Distance(u.Pos, w.Flags[1].Pos) < 300)) u.Dead = true;
        PlaceSoldiers(w, 1, w.Flags[1].Pos, 4);
        Sim(w, 15);
        Check("Domínio interrompido zera a contagem", before > 20 && w.HoldTimer[0] == 0 && w.Winner < 0,
            $"contagem antes: {before:0}s; depois da perda: {w.HoldTimer[0]:0}s; dono da Bandeira do Sul agora: {w.Flags[1].Owner}");
    }

    static void FlagFoodBonus()
    {
        var w = NewWorld();
        ClearUnits(w);
        float before = w.FlagBonus(0, FlagBonus.Food);
        PlaceSoldiers(w, 0, w.Flags[1].Pos, 3);
        float tCap = 0;
        for (float t = 0; t < 30 && w.Flags[1].Owner != 0; t += Dt) { w.Update(Dt); tCap = t; }
        float during = w.FlagBonus(0, FlagBonus.Food);
        foreach (var u in w.Units) u.Dead = true;
        PlaceSoldiers(w, 1, w.Flags[1].Pos, 3);
        float lostAt = -1, bonusAtLoss = -1;
        for (float t = 0; t < 30; t += Dt)
        {
            w.Update(Dt);
            if (w.Flags[1].Owner != 0) { lostAt = t; bonusAtLoss = w.FlagBonus(0, FlagBonus.Food); break; }
        }
        bool ok = before == 1f && Math.Abs(during - 1.10f) < 0.001f && lostAt >= 0 && bonusAtLoss == 1f;
        Check("Captura, bônus econômico e perda imediata do bônus", ok,
            $"bônus antes {before:0.00}; capturou em {tCap:0.0}s; bônus com a bandeira {during:0.00}; perdeu o controle após {lostAt:0.0}s; bônus no mesmo quadro {bonusAtLoss:0.00}");
    }

    static void FlagDefenseBonus()
    {
        var w = NewWorld(FactionId.MinasGerais, FactionId.RioGrandeDoSul);
        ClearUnits(w);
        var probe = w.AddUnit(UnitType.Swordsman, 0, w.CenterOf(w.StartTiles[0]) + new Vector2(200, 0));
        float d0 = Combat.Defense(probe, false);
        w.Flags[0].Owner = 0;
        float d1 = Combat.Defense(probe, false);
        w.Flags[0].Owner = -1;
        float d2 = Combat.Defense(probe, false);
        Check("Bandeira militar: +10% de resistência", Math.Abs(d0 - 1) < 0.001f && Math.Abs(d1 - 0.9f) < 0.001f && Math.Abs(d2 - 1) < 0.001f,
            $"dano recebido sem bandeira {d0:0.00}, com a Bandeira do Norte {d1:0.00}, depois de perder {d2:0.00}");
    }

    static void WorkersDontCapture()
    {
        var w = NewWorld();
        ClearUnits(w);
        PlaceSoldiers(w, 0, w.Flags[0].Pos, 4, UnitType.Worker);
        Sim(w, 20);
        Check("Trabalhadores não capturam bandeiras", w.Flags[0].Owner == -1 && Math.Abs(w.Flags[0].Progress) < 0.01f,
            $"dono após 20s com 4 trabalhadores: {w.Flags[0].Owner}, progresso {w.Flags[0].Progress:0}");
    }

    static void Camouflage()
    {
        var w = NewWorld(FactionId.Amazonas, FactionId.MinasGerais);
        ClearUnits(w);
        var grass = GrassNear(w, w.CenterOf(P(16, 30)));
        var v = w.AddUnit(UnitType.VigiaDaMata, 0, grass);
        string reasonOut = v.AbilityBlock();
        bool usedOut = v.UseAbility();
        var forest = ForestTile(w);
        v.Pos = forest;
        bool usedIn = v.UseAbility();
        var target = w.AddUnit(UnitType.Worker, 1, forest + new Vector2(200, 0));
        float normal = Combat.Damage(v, target, ambush: false);
        float amb = Combat.Damage(v, target, ambush: true);
        bool hidden = w.IsHiddenFrom(v, 1);
        Check("Camuflagem só na vegetação; emboscada +40%", !usedOut && reasonOut != null && usedIn && hidden && Math.Abs(amb / normal - 1.4f) < 0.01f,
            $"fora da mata: ativou={usedOut} (\"{reasonOut}\"); na mata: ativou={usedIn}, escondido do inimigo={hidden}; emboscada/normal = {amb / normal:0.00}");
    }

    static void CamoBreaksOutsideForest()
    {
        var w = NewWorld(FactionId.Amazonas, FactionId.MinasGerais);
        ClearUnits(w);
        var v = w.AddUnit(UnitType.VigiaDaMata, 0, ForestTile(w));
        v.UseAbility();
        float before = v.CamoTime;
        v.Pos = GrassNear(w, w.CenterOf(P(16, 30)));
        Sim(w, 0.1f);
        Check("Camuflagem some ao sair da mata", before > 0 && v.CamoTime <= 0, $"camuflagem antes {before:0.0}s, depois de sair da mata {Math.Max(0, v.CamoTime):0.0}s");
    }

    static void StoneStance()
    {
        var w = NewWorld(FactionId.MinasGerais, FactionId.RioGrandeDoSul);
        ClearUnits(w);
        var g = w.AddUnit(UnitType.GuardiaoDasGerais, 0, GrassNear(w, w.CenterOf(P(16, 30))));
        var atk = w.AddUnit(UnitType.Swordsman, 1, g.Pos + new Vector2(30, 0));
        float normal = Combat.Damage(atk, g);
        g.UseAbility();
        float stance = Combat.Damage(atk, g);
        float speedRatio = g.CurrentSpeed() / (g.Def.Speed * g.Owner.Faction.UnitSpeed * g.Owner.Faction.PlainSpeed);
        Sim(w, Balance.C.StanceDuration + 0.2f);
        bool ended = !g.StoneStance;
        Check("Postura de Pedra: -35% de dano", Math.Abs(stance / normal - 0.65f) < 0.01f && ended,
            $"dano na postura/normal = {stance / normal:0.00}; velocidade na postura {speedRatio:0.00}x; acabou após {Balance.C.StanceDuration:0}s: {ended}");
    }

    static void Charge()
    {
        var w = NewWorld(FactionId.RioGrandeDoSul, FactionId.MinasGerais);
        ClearUnits(w);
        var l = w.AddUnit(UnitType.LanceiroDosPampas, 0, GrassNear(w, w.CenterOf(P(16, 30))));
        var t = w.AddUnit(UnitType.Swordsman, 1, l.Pos + new Vector2(40, 0));
        l.MovedTime = 0;
        float normal = Combat.Damage(l, t);
        l.UseAbility();
        float charge = Combat.Damage(l, t);
        float stun = t.StunTime;
        bool consumed = !l.ChargeBoost;
        Check("Investida: 1,6x de dano + atordoamento curto", Math.Abs(charge / normal - 1.6f) < 0.01f && Math.Abs(stun - 0.6f) < 0.01f && consumed,
            $"investida/normal = {charge / normal:0.00}; atordoamento {stun:0.0}s; bônus consumido no golpe: {consumed}");
    }

    static void Caps()
    {
        var w = NewWorld(FactionId.RioGrandeDoSul, FactionId.MinasGerais);
        ClearUnits(w);
        var l = w.AddUnit(UnitType.LanceiroDosPampas, 0, GrassNear(w, w.CenterOf(P(16, 30))));
        var t = w.AddUnit(UnitType.Swordsman, 1, l.Pos + new Vector2(40, 0));
        float baseDmg = l.Def.Dmg;
        w.Players[0].PowerTime = 5; // Ataque Coordenado ativo
        l.UseAbility();
        float dmg = Combat.Damage(l, t);
        // pior caso defensivo: Guardião em postura + bandeira militar + guardião vizinho, contra flechas
        var g = w.AddUnit(UnitType.GuardiaoDasGerais, 1, t.Pos + new Vector2(0, 30));
        g.UseAbility(); t.World.Flags[0].Owner = 1;
        var g2 = w.AddUnit(UnitType.GuardiaoDasGerais, 1, g.Pos + new Vector2(20, 0));
        float def = Combat.Defense(g, true);
        Check("Limites: bônus <= +80% e redução <= 50%", dmg / baseDmg <= 1.8f + 0.001f && def >= 0.5f - 0.001f,
            $"bônus combinado (passivo x poder x investida = {1.1 * 1.1 * 1.6:0.00}) aplicado como {dmg / baseDmg:0.00}x; multiplicador defensivo no pior caso {def:0.00}");
    }

    static void CommanderPowers()
    {
        var details = new List<string>();
        bool ok = true;
        foreach (FactionId f in Enum.GetValues<FactionId>())
        {
            var w = NewWorld(f, f == FactionId.Amazonas ? FactionId.MinasGerais : FactionId.Amazonas);
            var p = w.Players[0];
            bool early = w.UsePower(0);
            p.PowerCd = 0;
            bool first = w.UsePower(0);
            bool again = w.UsePower(0);
            ok &= !early && first && !again && p.PowerCd > 60;
            details.Add($"{p.Faction.Abbrev}: antes da recarga inicial={early}, usou={first}, repetiu={again}, recarga {p.PowerCd:0}s");
        }
        Check("Poderes de comandante com recarga", ok, string.Join("; ", details));
    }

    static void Construction()
    {
        var w = NewWorld();
        var p = w.Players[0];
        p.Wood = 500;
        int capBefore = p.PopCap;
        var worker = w.Units.First(u => u.Team == 0 && u.IsWorker);
        Building b = null;
        for (int x = 3; x < 20 && b == null; x++)
            for (int y = 24; y < 38 && b == null; y++)
                b = w.CanPlace(BuildingType.Casa, new Point(x, y), 0) ? w.PlaceBuilding(BuildingType.Casa, new Point(x, y), 0) : null;
        worker.CmdBuild(b);
        float t = 0;
        while (!b.Done && t < 60) { w.Update(Dt); t += Dt; }
        Check("Construção completa e aumenta população", b.Done && p.PopCap == capBefore + 5,
            $"casa pronta em {t:0.0}s (1 trabalhador); população máxima {capBefore} -> {p.PopCap}");
    }

    static (float hpBefore, float hpAfter, int woodBefore, int woodAfter) DoRepair(FactionId f)
    {
        var w = NewWorld(f, FactionId.RioGrandeDoSul);
        var c = w.Buildings.First(b => b.Team == 0 && b.Def.Type == BuildingType.Centro);
        c.Hp = c.MaxHp * 0.5f;
        c.LastDamaged = 1e9f; // impede a regeneração passiva de Minas de mascarar o teste
        var p = w.Players[0];
        p.Wood = 100;
        var worker = w.Units.First(u => u.Team == 0 && u.IsWorker);
        float hb = c.Hp; int wb = p.Wood;
        worker.Give(new QueuedOrder(UnitOrder.Build, c.Pos, c), false); // o mesmo comando do botão direito
        Sim(w, 15);
        return (hb, c.Hp, wb, p.Wood);
    }

    static void Repair()
    {
        var (hb, ha, wb, wa) = DoRepair(FactionId.Amazonas);
        Check("Reparo com botão direito (custo de madeira)", ha > hb + 50 && wa < wb, $"vida {hb:0} -> {ha:0} em 15s; madeira {wb} -> {wa}");
    }

    static void RepairMinas()
    {
        var (hb, ha, wb, wa) = DoRepair(FactionId.MinasGerais);
        Check("Reparo grátis para Minas Gerais", ha > hb + 50 && wa == wb, $"vida {hb:0} -> {ha:0} em 15s; madeira {wb} -> {wa}");
    }

    static void Destruction()
    {
        var w = NewWorld();
        w.Players[0].Wood = 500;
        Building b = null;
        for (int x = 3; x < 20 && b == null; x++)
            for (int y = 24; y < 38 && b == null; y++)
                b = w.CanPlace(BuildingType.Casa, new Point(x, y), 0) ? w.PlaceBuilding(BuildingType.Casa, new Point(x, y), 0) : null;
        var tile = b.Tile;
        bool occBefore = w.Occ[tile.X, tile.Y] == b;
        w.Damage(b, 1e6f, null);
        Sim(w, 0.1f);
        bool freed = w.Occ[tile.X, tile.Y] == null && w.Passable(tile) && !w.Buildings.Contains(b);
        Check("Destruição libera o terreno", occBefore && freed, $"ocupado antes: {occBefore}; livre e transitável depois: {freed}");
    }

    static void PathShortcut()
    {
        var w = NewWorld();
        int row = MapGen.ShortcutRow, rx = MapGen.RiverX(row);
        var a = w.CenterOf(new Point(rx - 4, row));
        var b = w.CenterOf(new Point(rx + 4, row));
        var path = w.Pathing.FindPath(a, b);
        float len = 0; var prev = a; bool clear = true;
        foreach (var p in path) { len += Vector2.Distance(prev, p); clear &= w.Pathing.LineClear(prev, p) || w.Passable(w.TileOf(p)); prev = p; }
        bool reached = Vector2.Distance(path[^1], b) < 2;
        Check("Pathfinding pelo atalho estreito do rio", reached && len < 10 * World.T && clear,
            $"chegou: {reached}; comprimento {len / World.T:0.0} tiles (reto seriam 8); trechos livres: {clear}");
    }

    static void PathCrowd()
    {
        var w = NewWorld();
        ClearUnits(w);
        var units = new List<Unit>();
        for (int i = 0; i < 30; i++) units.Add(w.AddUnit(UnitType.Swordsman, 0, GrassNear(w, w.CenterOf(new Point(20 + i % 5, 25 + i / 5)))));
        var dest = w.CenterOf(P(44, 30));
        foreach (var (u, i) in units.Select((u, i) => (u, i))) u.CmdMove(dest + new Vector2((i % 6 - 2.5f) * 32, (i / 6 - 2.5f) * 32));
        int inBlocked = 0;
        for (float t = 0; t < 60; t += Dt)
        {
            w.Update(Dt);
            foreach (var u in units) if (!w.Passable(w.TileOf(u.Pos)) && w.Ter[w.TileOf(u.Pos).X, w.TileOf(u.Pos).Y] == Terrain.Water) inBlocked++;
        }
        int arrived = units.Count(u => Vector2.Distance(u.Pos, dest) < 5 * World.T);
        Check("Pathfinding pelas travessias com 30 unidades", arrived >= 28 && inBlocked == 0,
            $"{arrived}/30 chegaram ao outro lado em 60s; quadros com unidade dentro d'água: {inBlocked}");
    }

    static void UnreachableWater()
    {
        var w = NewWorld();
        ClearUnits(w);
        var u = w.AddUnit(UnitType.Swordsman, 0, GrassNear(w, w.CenterOf(P(25, 24))));
        // um trecho reto do rio (fora das curvas, onde a margem encosta na diagonal)
        int wr = Enumerable.Range(27, 8).First(y => MapGen.RiverX(y - 1) == MapGen.RiverX(y) && MapGen.RiverX(y + 1) == MapGen.RiverX(y));
        var water = w.CenterOf(new Point(MapGen.RiverX(wr), wr));
        u.CmdMove(water);
        bool flagged = u.Unreachable;
        Sim(w, 20);
        bool onLand = w.TerrainAt(u.Pos) != Terrain.Water;
        Check("Destino na água: vai até a margem e avisa", flagged && onLand && u.IsIdle,
            $"marcado como inalcançável (gera aviso na tela): {flagged}; terminou em terra firme: {onLand}; parado: {u.IsIdle}; distância até o ponto pedido {Vector2.Distance(u.Pos, water) / World.T:0.0} tiles");
    }

    static void ShiftQueue()
    {
        var w = NewWorld();
        ClearUnits(w);
        var u = w.AddUnit(UnitType.Scout, 0, GrassNear(w, w.CenterOf(P(12, 33))));
        var a = GrassNear(w, w.CenterOf(P(18, 33)));
        var b = GrassNear(w, w.CenterOf(P(18, 26)));
        u.Give(new QueuedOrder(UnitOrder.Move, a, null), false);
        u.Give(new QueuedOrder(UnitOrder.Move, b, null), true);
        int queued = u.Queue.Count;
        float minA = float.MaxValue;
        for (float t = 0; t < 15; t += Dt) { w.Update(Dt); minA = Math.Min(minA, Vector2.Distance(u.Pos, a)); }
        bool atB = Vector2.Distance(u.Pos, b) < 40;
        // sem Shift a fila é substituída
        u.Give(new QueuedOrder(UnitOrder.Move, a, null), true);
        u.Give(new QueuedOrder(UnitOrder.Move, b, null), false);
        bool replaced = u.Queue.Count == 0;
        Check("Ordens em fila com Shift", queued == 1 && minA < 40 && atB && replaced,
            $"na fila após o Shift: {queued}; passou pelo 1º ponto (dist. mín. {minA:0}px); terminou no 2º: {atB}; ordem sem Shift limpa a fila: {replaced}");
    }

    static void Press(GameScreen gs, params Keys[] keys)
    {
        Input.PK = new KeyboardState();
        Input.K = new KeyboardState(keys);
        Input.PM = Input.M = new MouseState(600, 400, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);
        Input.Active = true;
        gs.Update(Dt);
        Input.PK = Input.K; Input.K = new KeyboardState();
        gs.Update(Dt);
    }

    static void Hotkeys()
    {
        var gs = new GameScreen(new GameConfig { Mode = GameMode.Skirmish, PlayerFaction = FactionId.RioGrandeDoSul, EnemyFaction = FactionId.MinasGerais, Seed = 5, RandomBases = false });
        gs.SkipIntro();
        var w = gs.W;
        var l = w.AddUnit(UnitType.LanceiroDosPampas, 0, GrassNear(w, w.CenterOf(P(14, 30))));
        gs.SelectForShot(e => e == l);
        Press(gs, Keys.V);
        bool v = l.AbilityActive;

        w.Players[0].PowerCd = 0;
        Press(gs, Keys.C);
        bool c = w.Players[0].PowerActive;

        l.CmdMove(w.CenterOf(P(25, 33)));
        Press(gs, Keys.X);
        bool x = l.IsIdle && l.Queue.Count == 0;

        Press(gs, Keys.F);
        bool f = gs.AttackMoveMode;
        Press(gs, Keys.Escape);

        var before = gs.CameraPos;
        Input.PK = new KeyboardState(); Input.K = new KeyboardState(Keys.D);
        for (int i = 0; i < 10; i++) gs.Update(Dt);
        Input.K = new KeyboardState(Keys.S);
        for (int i = 0; i < 10; i++) gs.Update(Dt);
        Input.K = new KeyboardState();
        var after = gs.CameraPos;
        bool wasd = after.X > before.X + 10 && after.Y > before.Y + 10;
        Check("Atalhos: V habilidade, C poder, X parar, F atacar, WASD câmera", v && c && x && f && wasd,
            $"V ativou Investida: {v}; C ativou o poder: {c}; X parou e limpou a fila: {x}; F entrou no modo atacar-mover: {f}; D+S moveram a câmera: {wasd} ({before.X:0},{before.Y:0} -> {after.X:0},{after.Y:0})");
    }

    // ---------------- entrada real de mouse/teclado passando pela tela de jogo ----------------

    static void MouseFrame(GameScreen gs, Vector2 screen, bool left = false, bool right = false, params Keys[] keys)
    {
        Input.Active = true;
        Input.PM = Input.M; Input.PK = Input.K;
        Input.M = new MouseState((int)screen.X, (int)screen.Y, 0, left ? ButtonState.Pressed : ButtonState.Released, ButtonState.Released,
                                 right ? ButtonState.Pressed : ButtonState.Released, ButtonState.Released, ButtonState.Released);
        Input.K = new KeyboardState(keys);
        gs.Update(Dt);
    }

    static (GameScreen gs, World w, Vector2 center) InputArena()
    {
        var gs = new GameScreen(new GameConfig { Mode = GameMode.Skirmish, PlayerFaction = FactionId.Amazonas, EnemyFaction = FactionId.MinasGerais, Seed = 21, RandomBases = false });
        gs.SkipIntro();
        var w = gs.W;
        var center = GrassNear(w, w.CenterOf(P(15, 31)));
        gs.FocusOn(center, 1f);
        MouseFrame(gs, new Vector2(Game1.I.W / 2f, Game1.I.H / 2f)); // estabiliza câmera/estado
        return (gs, w, center);
    }

    static void InputDragAndDoubleClick()
    {
        var (gs, w, center) = InputArena();
        var sw = new List<Unit>();
        for (int i = 0; i < 3; i++) sw.Add(w.AddUnit(UnitType.Swordsman, 0, center + new Vector2(i * 34 - 34, 0)));
        var worker = w.AddUnit(UnitType.Worker, 0, center + new Vector2(0, 36));
        MouseFrame(gs, gs.WorldToScreen(center));
        // arrasto de caixa: pega soldados e trabalhador; a seleção prioriza soldados
        var a = gs.WorldToScreen(center + new Vector2(-70, -50));
        var b = gs.WorldToScreen(center + new Vector2(70, 60));
        MouseFrame(gs, a);
        MouseFrame(gs, a, left: true);
        MouseFrame(gs, (a + b) / 2, left: true);
        MouseFrame(gs, b, left: true);
        MouseFrame(gs, b);
        var sel = gs.Selection.OfType<Unit>().ToList();
        bool drag = sel.Count == 3 && sel.All(u => u.Def.Type == UnitType.Swordsman);
        // duplo clique num espadachim: seleciona todos os espadachins na tela
        var neutral = gs.WorldToScreen(center + new Vector2(0, 200));
        MouseFrame(gs, neutral, left: true); MouseFrame(gs, neutral); // limpa a seleção
        int afterClear = gs.Selection.Count;
        var p = gs.WorldToScreen(sw[1].Pos - new Vector2(0, 10));
        MouseFrame(gs, p, left: true); MouseFrame(gs, p);
        int single = gs.Selection.Count;
        MouseFrame(gs, p, left: true); MouseFrame(gs, p);
        int dbl = gs.Selection.OfType<Unit>().Count(u => u.Def.Type == UnitType.Swordsman);
        Check("Mouse: arrastar caixa e duplo clique (entrada simulada)", drag && afterClear == 0 && single == 1 && dbl == 3,
            $"arrasto selecionou {sel.Count} ({string.Join(",", sel.Select(u => u.Def.Name).Distinct())}); clique no vazio limpou: {afterClear == 0}; 1º clique: {single}; duplo clique: {dbl} espadachins");
    }

    static void InputShiftRightClick()
    {
        var (gs, w, center) = InputArena();
        var s = w.AddUnit(UnitType.Swordsman, 0, center);
        gs.SelectForShot(e => e == s);
        var p1 = gs.WorldToScreen(center + new Vector2(120, 0));
        var p2 = gs.WorldToScreen(center + new Vector2(120, -120));
        MouseFrame(gs, p1, keys: Keys.LeftShift);
        MouseFrame(gs, p1, right: true, keys: Keys.LeftShift);
        MouseFrame(gs, p1, keys: Keys.LeftShift);
        MouseFrame(gs, p2, right: true, keys: Keys.LeftShift);
        MouseFrame(gs, p2, keys: Keys.LeftShift);
        int queued = s.Queue.Count;
        bool moving = s.Order == UnitOrder.Move;
        bool marker = w.Effects.Any(e => e.Kind == Fx.Ring);
        // sem Shift: substitui a fila
        MouseFrame(gs, p1, right: true); MouseFrame(gs, p1);
        Check("Mouse: Shift + botão direito enfileira ordens", queued == 1 && moving && marker && s.Queue.Count == 0,
            $"após 2 cliques com Shift: movendo={moving}, {queued} na fila; marcador de destino exibido: {marker}; clique sem Shift limpou a fila: {s.Queue.Count == 0}");
    }

    static void InputRightClickRepair()
    {
        var (gs, w, center) = InputArena();
        var c = w.Buildings.First(b => b.Team == 0 && b.Def.Type == BuildingType.Centro);
        c.Hp = c.MaxHp * 0.5f; c.LastDamaged = 1e9f;
        w.Players[0].Wood = 100;
        var worker = w.Units.First(u => u.Team == 0 && u.IsWorker);
        gs.FocusOn(c.Pos, 1f);
        MouseFrame(gs, new Vector2(Game1.I.W / 2f, Game1.I.H / 2f));
        gs.SelectForShot(e => e == worker);
        var p = gs.WorldToScreen(c.Pos);
        MouseFrame(gs, p, right: true); MouseFrame(gs, p);
        bool ordered = worker.Order == UnitOrder.Build && worker.Target == c;
        float hb = c.Hp;
        for (int i = 0; i < 300; i++) MouseFrame(gs, p);
        Check("Mouse: botão direito em prédio danificado repara", ordered && c.Hp > hb,
            $"trabalhador recebeu a ordem de reparo: {ordered}; vida {hb:0} -> {c.Hp:0} em 10s");
    }

    static void SoundsLoad()
    {
        var files = Directory.GetFiles(Path.Combine(Assets.Root, "Audio"), "*.ogg");
        var bad = files.Where(f => !Assets.CanLoad(Path.GetFileNameWithoutExtension(f))).Select(Path.GetFileName).ToList();
        Check("Todos os sons carregam (OGG)", files.Length > 0 && bad.Count == 0, $"{files.Length - bad.Count}/{files.Length} arquivos decodificados{(bad.Count > 0 ? "; com erro: " + string.Join(", ", bad) : "")}");
    }

    // ---------------- etapa de identidade visual ----------------

    static void ScreenFrame(Screen s, Vector2 uiPoint, bool left = false, params Keys[] keys)
    {
        Game1.ScreenScale = s.LayoutScale;
        var scr = uiPoint * Game1.UiScaleEff;
        Input.Active = true;
        Input.PM = Input.M; Input.PK = Input.K;
        Input.M = new MouseState((int)scr.X, (int)scr.Y, 0, left ? ButtonState.Pressed : ButtonState.Released, ButtonState.Released,
                                 ButtonState.Released, ButtonState.Released, ButtonState.Released);
        Input.K = new KeyboardState(keys);
        s.Update(Dt);
    }

    static void FactionSelection()
    {
        var ss = new SetupScreen(GameMode.Skirmish);
        ScreenFrame(ss, Vector2.Zero);
        var log = new List<string>();
        bool ok = true;
        for (int i = 2; i >= 0; i--)
        {
            var c = ss.TabRect(i).Center.ToVector2();
            ScreenFrame(ss, c, left: true); ScreenFrame(ss, c);
            ok &= ss.Selected == (FactionId)i;
            log.Add($"clique na aba {i + 1} -> {ss.Selected}");
        }
        ScreenFrame(ss, Vector2.Zero, keys: Keys.D3); ScreenFrame(ss, Vector2.Zero);
        bool key3 = ss.Selected == FactionId.RioGrandeDoSul;
        ScreenFrame(ss, Vector2.Zero, keys: Keys.Right); ScreenFrame(ss, Vector2.Zero);
        bool right = ss.Selected == FactionId.Amazonas;
        var starts = new List<string>();
        foreach (FactionId f in Enum.GetValues<FactionId>())
        {
            var s2 = new SetupScreen(GameMode.Skirmish);
            s2.Select(f);
            ScreenFrame(s2, Vector2.Zero); // solta as teclas antes de apertar Enter
            ScreenFrame(s2, Vector2.Zero, keys: Keys.Enter);
            ok &= s2.LastConfig?.PlayerFaction == f && s2.LastConfig.EnemyFaction != f;
            starts.Add($"{f}: partida criada com {s2.LastConfig?.PlayerFaction}");
        }
        Game1.ScreenScale = 1f;
        Check("Seleção das três facções (abas, teclado, Começar)", ok && key3 && right,
            $"{string.Join("; ", log)}; tecla 3 -> RS: {key3}; seta direita volta ao AM: {right}; {string.Join("; ", starts)}");
    }

    static void StateFlags()
    {
        Color Px(FactionId? f, int x, int y)
        {
            var t = f is FactionId id ? FlagArt.Tex(id) : FlagArt.Neutral;
            var d = new Color[t.Width * t.Height];
            t.GetData(d);
            int s = t.Width / 48;
            return d[(y * s + s / 2) * t.Width + x * s + s / 2];
        }
        bool Near(Color c, int r, int g, int b) => Math.Abs(c.R - r) < 40 && Math.Abs(c.G - g) < 40 && Math.Abs(c.B - b) < 40;
        var am = new[] { Near(Px(FactionId.Amazonas, 4, 4), 30, 64, 150), Near(Px(FactionId.Amazonas, 35, 15), 206, 32, 40), Near(Px(FactionId.Amazonas, 35, 26), 244, 244, 238) };
        var mg = new[] { Near(Px(FactionId.MinasGerais, 24, 20), 206, 32, 40), Near(Px(FactionId.MinasGerais, 2, 2), 244, 244, 238) };
        var rs = new[] { Near(Px(FactionId.RioGrandeDoSul, 3, 3), 18, 140, 64), Near(Px(FactionId.RioGrandeDoSul, 45, 29), 250, 206, 30), Near(Px(FactionId.RioGrandeDoSul, 6, 27), 206, 32, 40) };
        bool neutral = Near(Px(null, 20, 3), 244, 244, 238);
        Check("Bandeiras estaduais corretas (cores-chave)", am.All(x => x) && mg.All(x => x) && rs.All(x => x) && neutral,
            $"AM cantão azul/faixa vermelha/branco: {string.Join("/", am)}; MG triângulo vermelho/fundo branco: {string.Join("/", mg)}; RS verde/amarelo/vermelho: {string.Join("/", rs)}; neutra branca: {neutral}");
    }

    static void SheetsUseLiveData()
    {
        var c = Balance.C;
        var log = new List<string>();
        bool ok = true;
        foreach (var f in Defs.Factions)
        {
            var rows = FactionSheet.Build(f).ToDictionary(r => r.Label, r => r.Value);
            ok &= rows.Count == 6 && rows["Comandante"].Contains(f.Commander) && rows["Unidade especial"] == Defs.Units[f.Special].Name && rows["Estilo"] == f.Style;
            log.Add($"{f.Abbrev}: \"{rows["Habilidade"]}\" / \"{rows["Ponto fraco"]}\"");
        }
        ok &= FactionSheet.Ability(FactionId.Amazonas).Contains($"+{Math.Round((c.CamoAmbushDamage - 1) * 100)}%");
        ok &= FactionSheet.Ability(FactionId.MinasGerais).Contains($"-{Math.Round(c.StanceReduction * 100)}%");
        // muda o balanceamento em memória: a ficha tem de acompanhar
        float old = c.CamoAmbushDamage;
        c.CamoAmbushDamage = 1.55f;
        bool follows = FactionSheet.Ability(FactionId.Amazonas).Contains("+55%");
        c.CamoAmbushDamage = old;
        Check("Fichas das facções usam os dados atuais", ok && follows, $"{string.Join("; ", log)}; ao mudar a emboscada para +55% a ficha mudou junto: {follows}");
    }

    static void IntroBehaviour()
    {
        var gs = new GameScreen(new GameConfig { Mode = GameMode.Skirmish, PlayerFaction = FactionId.MinasGerais, EnemyFaction = FactionId.Amazonas, Seed = 3, RandomBases = false });
        bool startActive = gs.IntroActive;
        for (int i = 0; i < 30; i++) MouseFrame(gs, new Vector2(400, 300));
        bool frozen = gs.W.Time == 0 && gs.IntroActive;
        MouseFrame(gs, new Vector2(400, 300), keys: Keys.Space);
        bool skipped = !gs.IntroActive;
        for (int i = 0; i < 30; i++) MouseFrame(gs, new Vector2(400, 300));
        bool runs = gs.W.Time > 0.5f;
        var gs2 = new GameScreen(new GameConfig { Mode = GameMode.Skirmish, PlayerFaction = FactionId.Amazonas, EnemyFaction = FactionId.MinasGerais, Seed = 3, RandomBases = false });
        int frames = 0;
        while (gs2.IntroActive && frames < 300) { MouseFrame(gs2, new Vector2(400, 300)); frames++; }
        var gs3 = new GameScreen(new GameConfig { Mode = GameMode.Skirmish, PlayerFaction = FactionId.Amazonas, EnemyFaction = FactionId.MinasGerais, Seed = 3, RandomBases = false });
        MouseFrame(gs3, new Vector2(400, 300)); MouseFrame(gs3, new Vector2(400, 300), left: true);
        bool click = !gs3.IntroActive;
        Check("Introdução: congela a simulação e pode ser pulada", startActive && frozen && skipped && runs && click && frames * Dt <= GameScreen.IntroDuration + 0.1f,
            $"ativa ao abrir: {startActive}; relógio parado durante 1 s de introdução: {frozen}; pulou com tecla: {skipped}; com clique: {click}; simulação andou depois: {runs}; acabou sozinha em {frames * Dt:0.0}s");
    }

    static void AbilityEffects()
    {
        (int leaves, int chips, int rings) Run(bool fx)
        {
            var w = NewWorld(FactionId.Amazonas, FactionId.MinasGerais);
            w.VisualFx = fx;
            ClearUnits(w);
            w.Effects.Clear();
            var v = w.AddUnit(UnitType.VigiaDaMata, 0, ForestTile(w));
            v.UseAbility();
            int leaves = w.Effects.Count(e => e.Kind == Fx.Leaf);
            var g = w.AddUnit(UnitType.GuardiaoDasGerais, 1, GrassNear(w, w.CenterOf(P(16, 30))));
            g.UseAbility();
            w.Damage(g, 5, null);
            int chips = w.Effects.Count(e => e.Kind == Fx.Chip);
            var l = w.AddUnit(UnitType.LanceiroDosPampas, 1, g.Pos + new Vector2(-60, 0));
            var t = w.AddUnit(UnitType.Swordsman, 0, l.Pos + new Vector2(30, 0));
            l.UseAbility();
            int before = w.Effects.Count(e => e.Kind == Fx.Spark);
            Combat.Damage(l, t);
            int impact = w.Effects.Count(e => e.Kind == Fx.Spark) - before;
            return (leaves, chips, impact);
        }
        var on = Run(true);
        var off = Run(false);
        Check("Efeitos visuais das habilidades", on.leaves > 0 && on.chips > 0 && on.rings > 0 && off.leaves == 0 && off.chips == 0 && off.rings == 0,
            $"Camuflagem: {on.leaves} folhas; Postura (ativar + golpe segurado): {on.chips} lascas; impacto da Investida: {on.rings} faíscas; com efeitos desligados: {off.leaves}/{off.chips}/{off.rings}");
    }

    static void EffectsDontChangeOutcome()
    {
        (int winner, float time, int k0, int k1, int g0, int g1) Run(bool fx)
        {
            var w = new World(new GameConfig { Mode = GameMode.Skirmish, PlayerFaction = FactionId.Amazonas, EnemyFaction = FactionId.RioGrandeDoSul, Seed = 1005, RandomBases = false });
            w.VisualFx = fx;
            var a0 = new AIController(w, 0); var a1 = new AIController(w, 1);
            while (w.Winner < 0 && w.Time < 900) { w.Update(Dt); a0.Update(Dt); a1.Update(Dt); }
            return (w.Winner, w.Time, w.Players[0].Kills, w.Players[1].Kills, w.Players[0].Gathered.Sum(), w.Players[1].Gathered.Sum());
        }
        var a = Run(true);
        var b = Run(false);
        Check("Efeitos visuais não alteram a partida (determinismo)", a == b,
            $"com efeitos: vencedor {a.winner}, {a.time / 60:0.00} min, abates {a.k0}/{a.k1}, coleta {a.g0}/{a.g1}; sem efeitos: vencedor {b.winner}, {b.time / 60:0.00} min, abates {b.k0}/{b.k1}, coleta {b.g0}/{b.g1}");
    }

    static void EndScreenStats()
    {
        var cfg = new GameConfig { Mode = GameMode.Skirmish, PlayerFaction = FactionId.RioGrandeDoSul, EnemyFaction = FactionId.MinasGerais, Seed = 1001, RandomBases = false };
        var w = new World(cfg);
        var a0 = new AIController(w, 0); var a1 = new AIController(w, 1);
        while (w.Winner < 0 && w.Time < 1800) { w.Update(Dt); a0.Update(Dt); a1.Update(Dt); }
        var es = new EndScreen(cfg, w);
        var p = w.Players[0]; var e = w.Players[1];
        bool rows = es.Rows.Count == 6
            && es.Rows[0].You == $"{p.Gathered[0]} · {p.Gathered[1]} · {p.Gathered[2]}"
            && es.Rows[1].You == p.UnitsTrained.ToString() && es.Rows[2].You == p.UnitsLost.ToString()
            && es.Rows[3].You == e.BuildingsLost.ToString() && es.Rows[4].You == p.FlagsCaptured.ToString() && es.Rows[4].Enemy == e.FlagsCaptured.ToString();
        bool winner = w.Winner >= 0 && es.WinnerFaction == w.Players[w.Winner].Faction && es.Condition != "—";
        // derrota por perda da capital
        var w2 = new World(cfg);
        w2.Damage(w2.Buildings.First(b => b.Team == 0 && b.Def.Type == BuildingType.Centro), 1e6f, null);
        w2.Update(Dt);
        var es2 = new EndScreen(cfg, w2);
        bool defeat = es2.WinnerFaction == w2.Players[1].Faction && es2.Condition == "Destruição do Centro de Comando";
        Check("Tela de vitória/derrota com estatísticas corretas", rows && winner && defeat,
            $"vencedor {es.WinnerFaction.Name} por \"{es.Condition}\"; {string.Join("; ", es.Rows.Select(r => $"{r.Label}: {r.You} x {r.Enemy}"))}; derrota por capital mostra vencedor inimigo: {defeat}");
    }

    static void BalanceUnchanged()
    {
        var path = Path.Combine(Assets.Root, "Testes", "balanco_referencia.txt");
        bool exists = File.Exists(path);
        string reference = exists ? File.ReadAllText(path).Replace("\r\n", "\n") : "";
        string now = BalanceSnapshot.Build();
        var diff = reference.Split('\n').Zip(now.Split('\n')).Where(z => z.First != z.Second).Select(z => $"\"{z.First.Trim()}\" -> \"{z.Second.Trim()}\"").Take(3).ToList();
        Check("Balanceamento inalterado (comparado à referência)", exists && reference == now,
            exists ? (diff.Count == 0 && reference == now ? $"{now.Split('\n').Length} linhas idênticas (balance.json, unidades, construções e facções)" : "diferenças: " + string.Join("; ", diff)) : "arquivo de referência ausente");
    }

    static void SoundLimiter()
    {
        int before = Assets.Skipped;
        for (int i = 0; i < 6; i++) Assets.Play("click_002", 0.01f);
        int cut = Assets.Skipped - before;
        Check("Limitador de sons repetidos", cut >= 5, $"6 disparos do mesmo som no mesmo instante: {6 - cut} tocou, {cut} cortados");
    }

    static void TutorialFlow()
    {
        var w = NewWorld(FactionId.Amazonas, FactionId.MinasGerais, GameMode.Tutorial);
        var tut = new Tutorial(w);
        var ai = new AIController(w, 1) { Passive = true };
        var log = new List<string>();
        void Step(float secs, int sel = 0) { for (float t = 0; t < secs; t += Dt) { w.Update(Dt); ai.Update(Dt); tut.Update(Dt, sel); } }

        Step(0.5f, sel: 3); log.Add($"1:{tut.Index >= 1}");
        var workers = w.Units.Where(u => u.Team == 0 && u.IsWorker).ToList();
        var c = w.CenterOf(w.StartTiles[0]);
        workers[0].CmdGather(w.NearestResource(Res.Wood, c, 20 * World.T));
        workers[1].CmdGather(w.NearestResource(Res.Food, c, 20 * World.T));
        workers[2].CmdGather(w.NearestResource(Res.Wood, c, 20 * World.T));
        float t1 = 0; while (tut.Index == 1 && t1 < 120) { Step(1); t1++; }
        log.Add($"2:{tut.Index >= 2}({t1:0}s)");

        var p = w.Players[0];
        p.Wood = Math.Max(p.Wood, 120);
        Building q = null;
        for (int x = 9; x < 20 && q == null; x++)
            for (int y = 32; y < 38 && q == null; y++)
                q = w.CanPlace(BuildingType.Quartel, new Point(x, y), 0) ? w.PlaceBuilding(BuildingType.Quartel, new Point(x, y), 0) : null;
        workers[0].CmdBuild(q); workers[2].CmdBuild(q);
        float t2 = 0; while (tut.Index == 2 && t2 < 90) { Step(1); t2++; }
        log.Add($"3:{tut.Index >= 3}({t2:0}s)");

        p.Food = Math.Max(p.Food, 200); p.Ore = Math.Max(p.Ore, 60);
        for (int i = 0; i < 2; i++) { p.Pay(Defs.Units[UnitType.Swordsman].Cost); q.Queue.Add(UnitType.Swordsman); }
        float t3 = 0; while (tut.Index == 3 && t3 < 60) { Step(1); t3++; }
        log.Add($"4:{tut.Index >= 4}({t3:0}s)");

        bool camp = tut.Camp != null;
        foreach (var s in w.Units.Where(u => u.Team == 0 && u.IsMilitary && u.Def.Type != UnitType.Scout)) s.CmdAttack(tut.Camp);
        float t4 = 0; while (tut.Index == 4 && t4 < 120) { Step(1); t4++; }
        log.Add($"5:{tut.Index >= 5}({t4:0}s, acampamento criado={camp})");

        foreach (var s in w.Units.Where(u => u.Team == 0 && u.IsMilitary)) s.CmdMove(w.Flags[1].Pos);
        float t5 = 0; while (w.Winner < 0 && t5 < 120) { Step(1); t5++; }
        log.Add($"6:{w.Winner == 0}({t5:0}s)");
        Check("Tutorial completo (6 passos)", w.Winner == 0 && tut.Index == Tutorial.Steps.Length,
            $"passos: {string.Join(" ", log)}; resultado: {w.WinReason}");
    }
}
