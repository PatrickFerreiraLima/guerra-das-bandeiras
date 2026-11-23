using GuerraDasBandeiras.Gameplay;
using GuerraDasBandeiras.Screens;

namespace GuerraDasBandeiras.Tools;

/// <summary>
/// Teste automático: "GuerraDasBandeiras --shot saida.png cenario segundos"
/// cenários: menu, setup, game (IA x IA), mission, end, scenery (mapa revelado; GB_FOCUS=x,y escolhe o tile).
/// Simula a partida acelerada, salva uma captura e um relatório .txt ao lado.
/// </summary>
public class ShotRunner
{
    readonly string _out, _scenario;
    readonly float _seconds;
    int _frame;
    public bool Active => _out != null;

    public ShotRunner(string[] args)
    {
        if (args.Length >= 3 && args[0] == "--shot")
        {
            _out = args[1];
            _scenario = args[2];
            _seconds = args.Length >= 4 ? float.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 0;
        }
    }

    public Screen CreateScreen()
    {
        switch (_scenario)
        {
            case "menu": return new MenuScreen();
            case "setup_am": case "setup_mg": case "setup_rs":
            {
                var ss = new SetupScreen(GameMode.Skirmish);
                ss.Select(_scenario == "setup_mg" ? FactionId.MinasGerais : _scenario == "setup_rs" ? FactionId.RioGrandeDoSul : FactionId.Amazonas);
                return ss;
            }
            case "intro":
            case "end":
                return new GameScreen(new GameConfig { Mode = GameMode.Skirmish, PlayerFaction = FactionId.MinasGerais, EnemyFaction = FactionId.RioGrandeDoSul, Seed = 1003 });
            case "setup": return new SetupScreen(GameMode.Skirmish);
            case "battle":
            {
                var bs = new GameScreen(new GameConfig { Mode = GameMode.Skirmish, PlayerFaction = FactionId.RioGrandeDoSul, EnemyFaction = FactionId.Amazonas, Seed = 3 });
                var w = bs.W;
                var flag = w.Flags[1].Pos;
                var types0 = new[] { UnitType.Swordsman, UnitType.Spearman, UnitType.Archer, UnitType.LanceiroDosPampas, UnitType.Archer, UnitType.Swordsman };
                var types1 = new[] { UnitType.Swordsman, UnitType.Spearman, UnitType.Archer, UnitType.VigiaDaMata, UnitType.Scout, UnitType.Spearman };
                for (int i = 0; i < 6; i++)
                {
                    w.AddUnit(types0[i], 0, flag + new Vector2(-260 - (i % 2) * 40, -80 + i * 32)).CmdAttackMove(flag + new Vector2(200, 0));
                    w.AddUnit(types1[i], 1, flag + new Vector2(260 + (i % 2) * 40, -80 + i * 32)).CmdAttackMove(flag - new Vector2(200, 0));
                }
                w.AddUnit(UnitType.Ram, 0, flag + new Vector2(-330, 40)).CmdMove(flag + new Vector2(-150, 60));
                w.AddUnit(UnitType.GuardiaoDasGerais, 1, flag + new Vector2(200, 120)).UseAbility();
                foreach (var u in w.Units.Where(u => u.Def.Type == UnitType.LanceiroDosPampas)) u.UseAbility();
                w.UsePower(0);
                return bs;
            }
            case "tutorial":
                return new GameScreen(new GameConfig { Mode = GameMode.Tutorial, PlayerFaction = FactionId.MinasGerais, EnemyFaction = FactionId.RioGrandeDoSul, Seed = 42 });
            case "mission":
                return new GameScreen(new GameConfig { Mode = GameMode.Mission, PlayerFaction = FactionId.RioGrandeDoSul, EnemyFaction = FactionId.MinasGerais, Seed = 42 });
            default:
                var fa = Environment.GetEnvironmentVariable("GB_FACTIONS")?.Split(',').Select(int.Parse).ToArray() ?? new[] { 0, 1 };
                var gs = new GameScreen(new GameConfig { Mode = GameMode.Skirmish, PlayerFaction = (FactionId)fa[0], EnemyFaction = (FactionId)fa[1], Difficulty = 1, Seed = 7 });
                if (_scenario == "game" || _scenario == "end") gs.AutoPlayer = new AIController(gs.W, 0);
                if (_scenario == "scenery") gs.W.RevealAll = true;
                return gs;
        }
    }

    /// <summary>Retorna true quando a captura deve ser feita neste frame.</summary>
    public void BeforeDraw(Screen s)
    {
        _frame++;
        // telas de menu: deixa as animações andarem um pouco antes da captura
        if (_frame == 2 && s is not GameScreen) for (int i = 0; i < 40; i++) s.Update(1 / 30f);
        if (_frame == 2 && s is GameScreen ig && _scenario == "intro") { for (int i = 0; i < 40; i++) ig.Update(1 / 30f); return; }
        if (_frame == 2 && s is GameScreen eg && _scenario == "end")
        {
            eg.SkipIntro();
            var ai0 = new AIController(eg.W, 0);
            eg.AutoPlayer = ai0;
            eg.Simulate(1800);
            Game1.I.SetScreen(new EndScreen(new GameConfig { Mode = GameMode.Skirmish, PlayerFaction = FactionId.MinasGerais, EnemyFaction = FactionId.RioGrandeDoSul }, eg.W));
            _frame = 0; // recomeça a contagem para capturar a tela final
            return;
        }
        if (_frame == 2 && s is GameScreen sg) sg.SkipIntro();
        if (_frame == 2 && s is GameScreen gs)
        {
            var log = new System.Text.StringBuilder();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            float step = 30;
            for (float t = 0; t < _seconds && gs.W.Winner < 0; t += step)
            {
                gs.Simulate(Math.Min(step, _seconds - t));
                var w = gs.W;
                log.AppendLine($"t={w.Time,6:0}s  " + string.Join("  |  ", w.Players.Select(p =>
                    $"P{p.Id} {p.Faction.Abbrev}: C{p.Food} M{p.Wood} Mi{p.Ore} pop {p.Pop}/{p.PopCap} " +
                    $"trab {w.Units.Count(u => u.Team == p.Id && u.IsWorker)} exerc {w.Units.Count(u => u.Team == p.Id && u.IsMilitary)} " +
                    $"pred {w.Buildings.Count(b => b.Team == p.Id)} kills {p.Kills} esp {w.Units.Count(u => u.Team == p.Id && u.HasAbility)} ariete {w.Units.Count(u => u.Team == p.Id && u.Def.IsSiege)} hab {p.AbilitiesUsed} pod {p.PowersUsed}")) +
                    $"  | bandeiras: {string.Join(",", w.Flags.Select(f => f.Owner))} hold {w.HoldTimer[0]:0}/{w.HoldTimer[1]:0}");
            }
            log.AppendLine($"Vencedor: {gs.W.Winner} ({gs.W.WinReason})  tempo real {sw.ElapsedMilliseconds}ms");
            File.WriteAllText(Path.ChangeExtension(_out, ".txt"), log.ToString());

            // enquadra a ação para a captura
            var focus = gs.W.Units.Where(u => u.IsMilitary).Select(u => u.Pos).DefaultIfEmpty(gs.W.CenterOf(gs.W.StartTiles[0])).ToList();
            var center = new Vector2(focus.Average(p => p.X), focus.Average(p => p.Y));
            if (_scenario == "battle") center = gs.W.Flags[1].Pos;
            // "scenery": GB_FOCUS=x,y (tile) escolhe o ponto do mapa a enquadrar
            else if (_scenario == "scenery" && Environment.GetEnvironmentVariable("GB_FOCUS") is string fc)
            { var p = fc.Split(',').Select(int.Parse).ToArray(); center = gs.W.CenterOf(new Point(p[0], p[1])); }
            else if (_scenario is "mission" or "tutorial" || _seconds < 1) center = gs.W.CenterOf(new Point(gs.W.StartTiles[0].X + 5, gs.W.StartTiles[0].Y - 1));
            gs.FocusOn(center, 1.4f);
            gs.SelectForShot(e => e.Team == 0 && e is Unit u && (_scenario == "battle" ? u.IsMilitary : u.IsWorker));
        }
    }

    public bool ShouldCapture => _frame >= 4;

    public void Capture(GraphicsDevice gd, Action<SpriteBatch> draw, SpriteBatch sb)
    {
        var pp = gd.PresentationParameters;
        using var rt = new RenderTarget2D(gd, pp.BackBufferWidth, pp.BackBufferHeight);
        gd.SetRenderTarget(rt);
        gd.Clear(new Color(20, 30, 26));
        draw(sb);
        gd.SetRenderTarget(null);
        using var fs = File.Create(_out);
        rt.SaveAsPng(fs, rt.Width, rt.Height);
    }
}
