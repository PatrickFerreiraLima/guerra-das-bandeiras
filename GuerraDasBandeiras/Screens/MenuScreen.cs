using GuerraDasBandeiras.Core;
using GuerraDasBandeiras.Gameplay;

namespace GuerraDasBandeiras.Screens;

public class MenuScreen : Screen
{
    public MenuScreen() { Ambience.Stop(); }
    public override float LayoutScale => FitScale(1000, 820);
    readonly List<UiButton> _buttons = new();
    bool _help;
    float _dt;

    void Build()
    {
        _buttons.Clear();
        var c = Ui / 2;
        int w = 420, h = 56, y = (int)(c.Y - 10);
        if (_help)
        {
            _buttons.Add(new UiButton { Rect = new Rectangle((int)c.X - 110, (int)Ui.Y - 110, 220, 50), Label = "Voltar", OnClick = () => _help = false, Hotkey = Keys.Escape });
            return;
        }
        _buttons.Add(new UiButton
        {
            Rect = new Rectangle((int)c.X - w / 2, y, w, h), Label = "Tutorial rápido", Sub = "6 passos, uns 5 minutos — opcional", Accent = Pal.Terracotta,
            OnClick = () => Game.SetScreen(new SetupScreen(GameMode.Tutorial))
        });
        y += 64;
        _buttons.Add(new UiButton
        {
            Rect = new Rectangle((int)c.X - w / 2, y, w, h), Label = "Missão guiada", Sub = "A primeira bandeira — história curta com contra-ataque",
            OnClick = () => Game.SetScreen(new SetupScreen(GameMode.Mission))
        });
        _buttons.Add(new UiButton
        {
            Rect = new Rectangle((int)c.X - w / 2, y + 64, w, h), Label = "Escaramuça", Sub = "Você contra a IA no mapa Encontro das Fronteiras",
            OnClick = () => Game.SetScreen(new SetupScreen(GameMode.Skirmish))
        });
        _buttons.Add(new UiButton
        {
            Rect = new Rectangle((int)c.X - w / 2, y + 128, w, h), Label = "Jogar 1x1 em rede", Sub = "Crie uma sala ou entre na de outra pessoa (rede local ou IP)", Accent = Pal.Slate,
            OnClick = () => Game.SetScreen(new MultiplayerScreen())
        });
        _buttons.Add(new UiButton
        {
            Rect = new Rectangle((int)c.X - w / 2, y + 192, w / 2 - 5, 46), Label = "Como jogar", Accent = Pal.InkSoft, FontSize = 16,
            OnClick = () => _help = true
        });
        _buttons.Add(new UiButton
        {
            Rect = new Rectangle((int)c.X + 5, y + 192, w / 2 - 5, 46), Label = "Sair", Accent = Pal.InkSoft, FontSize = 16,
            OnClick = () => Game.Exit()
        });
    }

    public override void Update(float dt)
    {
        _dt = dt;
        Build();
        Gui.Handle(_buttons);
    }

    public override void Draw(SpriteBatch sb)
    {
        MenuBackground.Draw(sb, _dt);
        sb.Begin(transformMatrix: Game.UiMatrix, samplerState: SamplerState.LinearClamp);
        var c = Ui / 2;
        if (_help) DrawHelp(sb);
        else
        {
            float tm = (float)Environment.TickCount / 1000f;
            // logo oficial ("Heróis do Brasil"), com moldura dourada e um leve "respirar"
            var logo = Assets.Image("logo");
            float ls = 318 * (1f + 0.008f * MathF.Sin(tm * 1.6f));
            var lc = new Vector2(c.X, c.Y - 222);
            var lr = new Rectangle((int)(lc.X - ls / 2), (int)(lc.Y - ls / 2), (int)ls, (int)ls);
            sb.Fill(new Rectangle(lr.X + 6, lr.Y + 8, lr.Width, lr.Height), Color.Black * 0.35f);
            sb.Fill(new Rectangle(lr.X - 4, lr.Y - 4, lr.Width + 8, lr.Height + 8), Pal.Gold);
            sb.Draw(logo, lr, Color.White);
            // as três bandeiras da demo tremulando dos lados do logo
            var flagPos = new[] { new Vector2(c.X - 300, c.Y - 290), new Vector2(c.X + 240, c.Y - 240), new Vector2(c.X - 300, c.Y - 180) };
            for (int i = 0; i < 3; i++)
            {
                var fx = flagPos[i];
                Gfx.Line(sb, fx + new Vector2(0, -6), fx + new Vector2(0, 50), new Color(90, 64, 40), 3);
                FlagArt.DrawWaving(sb, (FactionId)i, fx + new Vector2(1, -4), 54, 36, tm, i * 1.7f);
            }
            sb.TextCenter(Assets.FontNarrow(20), "Escolha seu estado. Erga sua base. Defenda sua bandeira.", new Vector2(c.X, c.Y - 42), Color.White);
            Gui.DrawAll(sb, _buttons);
        }
        sb.TextCenter(Assets.FontNarrow(13), "Arte, fontes e sons: Kenney.nl (CC0)   •   Feito com .NET + MonoGame   •   F11: tela cheia",
            new Vector2(c.X, Ui.Y - 18), Color.White * 0.6f);
        if (_help) Gui.DrawAll(sb, _buttons);
        sb.End();
    }

    public static readonly string[] HelpLines =
    {
        "CONTROLES",
        "Clique esquerdo: seleciona  •  Arraste: seleciona vários  •  Duplo clique: todos do mesmo tipo",
        "Clique direito: mover, atacar, coletar ou construir (depende do alvo)",
        "WASD / setas / mouse na borda / botão do meio: mover a câmera  •  Roda do mouse: zoom",
        "Shift + ordem: enfileira ordens (aparecem marcadores numerados)  •  Botão direito em prédio danificado: reparar",
        "F: atacar-mover  •  X: parar  •  V: habilidade especial  •  C: poder do comandante  •  H: Centro  •  Espaço: centralizar",
        "Ctrl+1..5: criar grupo  •  1..5: chamar grupo  •  Q E R T Y: botões do painel  •  . : ocioso  •  Esc: pausa  •  F1: ajuda",
        "",
        "COMO VENCER",
        "Domine as duas bandeiras do rio por 90 segundos OU destrua o Centro de Comando inimigo.",
        "",
        "VANTAGENS",
        "Lanceiro vence cavalaria  •  Atirador vence infantaria lenta  •  Infantaria alcança atiradores  •  Aríete derruba prédios",
        "Mata esconde batedores  •  Serra dá mais visão e alcance  •  Rio só se atravessa nas partes rasas  •  Só tropas capturam bandeiras",
        "",
        "ECONOMIA",
        "Comida (frutas) treina tropas  •  Madeira (árvores) ergue construções  •  Minério (rochas) para defesa e especiais",
        "Casas aumentam a população  •  Coletores encurtam a viagem dos trabalhadores",
    };

    void DrawHelp(SpriteBatch sb)
    {
        var r = new Rectangle((int)(Ui.X / 2 - 520), 60, 1040, (int)Ui.Y - 190);
        sb.Panel(r, Pal.Paper, Pal.Ink, 3);
        sb.TextCenter(Assets.Font(28), "Como jogar", new Vector2(r.Center.X, r.Y + 34), Pal.Ink, false);
        float y = r.Y + 70;
        foreach (var l in HelpLines)
        {
            bool head = l.Length > 0 && l == l.ToUpperInvariant() && !l.Contains('•');
            sb.Text(head ? Assets.Font(17) : Assets.FontNarrow(16), l, new Vector2(r.X + 36, y), head ? Pal.Green : Pal.Ink, false);
            y += head ? 26 : 23;
        }
    }
}

public class EndScreen : Screen
{
    public override float LayoutScale => FitScale(980, 720);
    readonly GameConfig _cfg;
    readonly World _w;
    readonly List<UiButton> _buttons = new();
    float _dt, _time;
    readonly bool _win;

    public record StatRow(string Label, string You, string Enemy);
    /// <summary>Linhas exibidas (expostas para os testes).</summary>
    public List<StatRow> Rows { get; }
    public string Condition { get; }
    public FactionDef WinnerFaction { get; }

    public EndScreen(GameConfig cfg, World w)
    {
        _cfg = cfg; _w = w;
        _win = w.Winner == w.Local;
        WinnerFaction = w.Winner >= 0 ? w.Players[w.Winner].Faction : w.Me.Faction;
        Condition = w.WinReason.Contains("bandeiras") ? "Domínio das duas bandeiras por 90 s"
                  : w.WinReason.Contains("Centro") ? "Destruição do Centro de Comando"
                  : w.WinReason.Contains("abandono") ? "Abandono do adversário"
                  : w.WinReason.Contains("Missão") || w.WinReason.Contains("Tutorial") ? "Objetivos cumpridos" : "—";
        var p = w.Me; var e = w.Enemy;
        static string Res3(Player q) => $"{q.Gathered[0]} · {q.Gathered[1]} · {q.Gathered[2]}";
        Rows = new List<StatRow>
        {
            new("Recursos coletados (comida · madeira · minério)", Res3(p), Res3(e)),
            new("Unidades produzidas", p.UnitsTrained.ToString(), e.UnitsTrained.ToString()),
            new("Unidades perdidas", p.UnitsLost.ToString(), e.UnitsLost.ToString()),
            new("Construções inimigas destruídas", e.BuildingsLost.ToString(), p.BuildingsLost.ToString()),
            new("Bandeiras capturadas", p.FlagsCaptured.ToString(), e.FlagsCaptured.ToString()),
            new("Habilidades / poderes usados", $"{p.AbilitiesUsed} / {p.PowersUsed}", $"{e.AbilitiesUsed} / {e.PowersUsed}"),
        };
        Ambience.Stop();
        if (_win) Sfx.Victory(); else Sfx.Defeat();
    }

    float Top => Math.Max(0, (Ui.Y - 640) / 2f);
    static string Short(string s) => s.Length > 12 ? s[..12] + "…" : s;

    public override void Update(float dt)
    {
        _dt = dt; _time += dt;
        _buttons.Clear();
        float cx = Ui.X / 2;
        int y = (int)(Top + 570);
        if (_cfg.Versus)
        {
            // partida em rede: a conexão já foi encerrada; volta para o saguão para abrir ou procurar outra sala
            _buttons.Add(new UiButton { Rect = new Rectangle((int)cx - 220, y, 210, 52), Label = "Nova partida", Hotkey = Keys.Enter, OnClick = () => Game.SetScreen(new MultiplayerScreen()) });
            _buttons.Add(new UiButton { Rect = new Rectangle((int)cx + 10, y, 210, 52), Label = "Menu", Accent = Pal.InkSoft, Hotkey = Keys.Escape, OnClick = () => Game.SetScreen(new MenuScreen()) });
            Gui.Handle(_buttons);
            return;
        }
        _buttons.Add(new UiButton
        {
            Rect = new Rectangle((int)cx - 330, y, 210, 52), Label = "Jogar de novo", Hotkey = Keys.Enter,
            OnClick = () => Game.SetScreen(new GameScreen(new GameConfig { Mode = _cfg.Mode, PlayerFaction = _cfg.PlayerFaction, EnemyFaction = _cfg.EnemyFaction, Difficulty = _cfg.Difficulty }))
        });
        _buttons.Add(new UiButton
        {
            Rect = new Rectangle((int)cx - 105, y, 210, 52), Label = "Outra facção", Accent = Pal.Gold,
            OnClick = () => Game.SetScreen(new SetupScreen(_cfg.Mode != GameMode.Skirmish && _win ? GameMode.Skirmish : _cfg.Mode))
        });
        _buttons.Add(new UiButton { Rect = new Rectangle((int)cx + 120, y, 210, 52), Label = "Menu", Accent = Pal.InkSoft, Hotkey = Keys.Escape, OnClick = () => Game.SetScreen(new MenuScreen()) });
        Gui.Handle(_buttons);
    }

    public override void Draw(SpriteBatch sb)
    {
        MenuBackground.Draw(sb, _dt);
        sb.Begin(transformMatrix: Game.UiMatrix, samplerState: SamplerState.LinearClamp);
        float cx = Ui.X / 2;
        var r = new Rectangle((int)cx - 450, (int)Top, 900, 550);
        sb.Panel(r, Pal.Paper, Pal.Ink, 3);

        // cabeçalho: bandeira e nome de quem venceu
        var head = new Rectangle(r.X, r.Y, r.Width, 140);
        sb.Fill(head, (_win ? Pal.DeepGreen : new Color(70, 28, 24)) * 0.95f);
        float pop = Math.Min(1, _time / 0.5f);
        FlagArt.DrawWaving(sb, WinnerFaction.Id, new Vector2(r.X + 34, r.Y + 24), 135 * pop, 90 * pop, _time);
        sb.Text(Assets.Font(44), _win ? "VITÓRIA!" : "DERROTA", new Vector2(r.X + 196, r.Y + 18), _win ? Pal.Gold : new Color(255, 140, 120));
        sb.Text(Assets.FontBold(20), $"Vencedor: {WinnerFaction.Name} — {WinnerFaction.Title}", new Vector2(r.X + 198, r.Y + 72), Color.White);
        sb.Text(Assets.FontNarrow(16), $"Condição: {Condition}", new Vector2(r.X + 198, r.Y + 100), Pal.Sand);
        int min = (int)(_w.Time / 60), sec = (int)(_w.Time % 60);
        sb.TextRight(Assets.FontBold(16), $"Duração {min:00}:{sec:00}", new Vector2(r.Right - 20, r.Y + 14), Color.White);

        // tabela: você x inimigo
        var p = _w.Me; var e = _w.Enemy;
        float colYou = r.X + 560, colEnemy = r.X + 750;
        float ty = r.Y + 160;
        FlagArt.Draw(sb, p.Faction.Id, new Rectangle((int)colYou - 50, (int)ty, 24, 16));
        sb.Text(Assets.FontBold(15), _cfg.Versus ? Short(_cfg.Names[_w.Local]) : "Você", new Vector2(colYou - 20, ty - 1), Pal.Ink, false);
        FlagArt.Draw(sb, e.Faction.Id, new Rectangle((int)colEnemy - 50, (int)ty, 24, 16));
        sb.Text(Assets.FontBold(15), _cfg.Versus ? Short(_cfg.Names[_w.Foe]) : "Inimigo", new Vector2(colEnemy - 20, ty - 1), Pal.Ink, false);
        ty += 30;
        var lf = Assets.FontNarrow(17);
        var vf = Assets.FontBold(17);
        for (int i = 0; i < Rows.Count; i++)
        {
            var row = Rows[i];
            if (i % 2 == 0) sb.Fill(new Rectangle(r.X + 20, (int)ty - 5, r.Width - 40, 34), Pal.Ink * 0.06f);
            sb.Text(lf, row.Label, new Vector2(r.X + 34, ty), Pal.Ink, false);
            sb.TextCenter(vf, row.You, new Vector2(colYou, ty + 11), Pal.Green, false);
            sb.TextCenter(vf, row.Enemy, new Vector2(colEnemy, ty + 11), Pal.Terracotta, false);
            ty += 40;
        }
        sb.TextCenter(Assets.FontNarrow(15), _w.WinReason, new Vector2(cx, r.Bottom - 26), Pal.InkSoft, false);
        Gui.DrawAll(sb, _buttons);
        sb.End();
    }
}
