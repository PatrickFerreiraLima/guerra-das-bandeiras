using GuerraDasBandeiras.Core;
using GuerraDasBandeiras.Gameplay;

namespace GuerraDasBandeiras.Screens;

/// <summary>
/// Seleção de facção: abas com as bandeiras, cena animada da facção escolhida e ficha curta
/// (montada com os dados vivos do jogo via FactionSheet). Opções de partida embaixo.
/// </summary>
public class SetupScreen : Screen
{
    public override float LayoutScale => FitScale(1000, 720);
    readonly GameMode _mode;
    FactionId _player = FactionId.Amazonas;
    int _enemy = -1; // -1 aleatório
    int _difficulty = 1;
    readonly List<UiButton> _buttons = new();
    float _dt, _time, _switchAnim = 1f;

    public FactionId Selected => _player;
    public GameConfig LastConfig { get; private set; }

    public SetupScreen(GameMode mode) { _mode = mode; }

    float Top => Math.Max(0, (Ui.Y - 700) / 2f);
    float Cx => Ui.X / 2f;
    public Rectangle TabRect(int i) => new((int)(Cx - 470 + i * 318), (int)(Top + 74), 304, 58);
    Rectangle SceneRect => new((int)(Cx - 470), (int)(Top + 146), 560, 315);
    Rectangle SheetRect => new((int)(Cx - 470 + 576), (int)(Top + 146), 364, 315);
    float OptionsY => Top + 476;

    public void Select(FactionId f)
    {
        if (f == _player) return;
        _player = f;
        _switchAnim = 0;
        Sfx.Select();
    }

    void Build()
    {
        _buttons.Clear();
        for (int i = 0; i < 3; i++)
        {
            var f = (FactionId)i;
            _buttons.Add(new UiButton { Rect = TabRect(i), Hidden = true, OnClick = () => Select(f) });
        }
        float y = OptionsY;
        if (_mode == GameMode.Skirmish)
        {
            string[] enemies = { "Aleatório", "     Amazonas", "     Minas Gerais", "     Rio G. do Sul" };
            for (int i = 0; i < 4; i++)
            {
                int e = i - 1;
                _buttons.Add(new UiButton
                {
                    Rect = new Rectangle((int)Cx - 320 + i * 156, (int)y, 150, 38), Label = enemies[i], FontSize = 12,
                    Selected = _enemy == e, Accent = Pal.Slate, OnClick = () => _enemy = e
                });
            }
            for (int i = 0; i < 3; i++)
            {
                int d = i;
                _buttons.Add(new UiButton
                {
                    Rect = new Rectangle((int)Cx - 320 + i * 156, (int)y + 46, 150, 38), Label = GameConfig.DifficultyNames[i], FontSize = 13,
                    Selected = _difficulty == d, Accent = Pal.Slate, OnClick = () => _difficulty = d
                });
            }
        }
        int by = (int)(Top + 600);
        _buttons.Add(new UiButton { Rect = new Rectangle((int)Cx - 290, by, 200, 54), Label = "Voltar", Accent = Pal.InkSoft, Hotkey = Keys.Escape, OnClick = () => { Ambience.Stop(); Game.SetScreen(new MenuScreen()); } });
        _buttons.Add(new UiButton { Rect = new Rectangle((int)Cx - 70, by, 360, 54), Label = "Começar!", FontSize = 22, Hotkey = Keys.Enter, OnClick = Start });
    }

    public void Start()
    {
        var rng = new Random();
        var cfg = new GameConfig { Mode = _mode, PlayerFaction = _player, Difficulty = _mode == GameMode.Skirmish ? _difficulty : 0 };
        if (_enemy >= 0 && _mode == GameMode.Skirmish) cfg.EnemyFaction = (FactionId)_enemy;
        else
        {
            var others = Enum.GetValues<FactionId>().Where(f => f != _player).ToArray();
            cfg.EnemyFaction = others[rng.Next(others.Length)];
        }
        LastConfig = cfg;
        Sfx.StartGame();
        Game.SetScreen(new GameScreen(cfg));
    }

    public override void Update(float dt)
    {
        _dt = dt;
        _time += dt;
        _switchAnim = Math.Min(1, _switchAnim + dt * 3);
        Build();
        Gui.Handle(_buttons);
        if (Input.KeyPressed(Keys.Left)) Select((FactionId)(((int)_player + 2) % 3));
        if (Input.KeyPressed(Keys.Right)) Select((FactionId)(((int)_player + 1) % 3));
        if (Input.KeyPressed(Keys.D1)) Select(FactionId.Amazonas);
        if (Input.KeyPressed(Keys.D2)) Select(FactionId.MinasGerais);
        if (Input.KeyPressed(Keys.D3)) Select(FactionId.RioGrandeDoSul);
        Ambience.SetScene(_player);
    }

    public override void Draw(SpriteBatch sb)
    {
        MenuBackground.Draw(sb, _dt);
        sb.Begin(transformMatrix: Game.UiMatrix, samplerState: SamplerState.LinearClamp);
        string title = _mode == GameMode.Tutorial ? "Tutorial rápido" : _mode == GameMode.Mission ? "Missão: A primeira bandeira" : "Escaramuça";
        sb.TextCenter(Assets.Font(30), title, new Vector2(Cx, Top + 24), Pal.Gold);
        sb.TextCenter(Assets.FontNarrow(17), "Escolha sua bandeira  (setas ou teclas 1, 2, 3)", new Vector2(Cx, Top + 54), Color.White);
        for (int i = 0; i < 3; i++) DrawTab(sb, Defs.Factions[i], TabRect(i), (int)_player == i);
        sb.Fill(new Rectangle(SceneRect.X - 3, SceneRect.Y - 3, SceneRect.Width + 6, SceneRect.Height + 6), Pal.Gold * 0.8f);
        sb.End();

        FactionScene.Draw(sb, _player, SceneRect, _time);

        sb.Begin(transformMatrix: Game.UiMatrix, samplerState: SamplerState.LinearClamp);
        // fade curto ao trocar de facção
        if (_switchAnim < 1) sb.Fill(SceneRect, Color.Black * (1 - _switchAnim));
        DrawSheet(sb, Defs.Faction(_player), SheetRect);

        float y = OptionsY;
        if (_mode == GameMode.Skirmish)
        {
            sb.Text(Assets.Font(15), "ADVERSÁRIO", new Vector2(Cx - 470, y + 10), Color.White);
            sb.Text(Assets.Font(15), "DIFICULDADE", new Vector2(Cx - 470, y + 56), Color.White);
        }
        else
        {
            string a = _mode == GameMode.Tutorial ? "Seis passos: selecionar, coletar, construir, recrutar, atacar e capturar uma bandeira." : "A vila está ameaçada. Colete, erga um quartel, recrute e tome as bandeiras do rio.";
            string b = _mode == GameMode.Tutorial ? "O inimigo não ataca durante o tutorial. Siga o destaque dourado." : "Dicas aparecem na tela durante a missão.";
            sb.TextCenter(Assets.FontBold(17), a, new Vector2(Cx, y + 18), Color.White);
            sb.TextCenter(Assets.FontNarrow(16), b, new Vector2(Cx, y + 44), Color.White * 0.85f);
        }
        Gui.DrawAll(sb, _buttons);
        if (_mode == GameMode.Skirmish)
            foreach (var bt in _buttons.Skip(4).Take(3))
            {
                var id = Defs.Factions.FirstOrDefault(fd => bt.Label.Trim().StartsWith(fd.Name.Split(' ')[0]))?.Id;
                if (id != null) FlagArt.Draw(sb, id, new Rectangle(bt.Rect.X + 6, bt.Rect.Y + 12, 18, 12));
            }
        sb.End();
    }

    void DrawTab(SpriteBatch sb, FactionDef f, Rectangle r, bool sel)
    {
        bool hover = r.Contains(Input.UiMousePt);
        sb.Fill(new Rectangle(r.X + 3, r.Y + 4, r.Width, r.Height), Color.Black * 0.3f);
        sb.Fill(r, sel ? Color.Lerp(Pal.Slate, f.Color, 0.35f) : hover ? Color.Lerp(Pal.Slate, Color.White, 0.08f) : Pal.PanelBg);
        sb.Border(r, sel ? Pal.Gold : Color.White * 0.15f, sel ? 3 : 1);
        if (sel) FlagArt.DrawWaving(sb, f.Id, new Vector2(r.X + 10, r.Y + 12), 48, 32, _time);
        else FlagArt.Draw(sb, f.Id, new Rectangle(r.X + 10, r.Y + 12, 48, 32));
        sb.Text(Assets.Font(f.Name.Length > 12 ? 15 : 17), f.Name.ToUpperInvariant(), new Vector2(r.X + 70, r.Y + 9), Color.White);
        sb.Text(Assets.FontNarrow(14), f.Title, new Vector2(r.X + 70, r.Y + 32), sel ? Pal.Gold : Color.White * 0.7f, false);
    }

    void DrawSheet(SpriteBatch sb, FactionDef f, Rectangle r)
    {
        sb.Panel(r, Pal.Paper, Pal.Ink, 2);
        FlagArt.Draw(sb, f.Id, new Rectangle(r.X + 14, r.Y + 14, 54, 36));
        sb.Text(Assets.Font(f.Name.Length > 12 ? 17 : 20), f.Name.ToUpperInvariant(), new Vector2(r.X + 78, r.Y + 12), Pal.Ink, false);
        sb.Text(Assets.FontNarrow(14), f.Title, new Vector2(r.X + 78, r.Y + 36), Pal.InkSoft, false);
        sb.Fill(new Rectangle(r.X + 14, r.Y + 60, r.Width - 28, 1), Pal.Ink * 0.25f);
        float y = r.Y + 68;
        var lf = Assets.FontBold(11);
        var vf = Assets.FontNarrow(15);
        foreach (var row in FactionSheet.Build(f))
        {
            var col = row.Label == "Ponto fraco" ? Pal.Red : row.Label == "Ponto forte" ? Pal.Green : Pal.Terracotta;
            sb.Text(lf, row.Label.ToUpperInvariant(), new Vector2(r.X + 14, y), col, false);
            y += 13;
            foreach (var l in Gfx.Wrap(vf, row.Value, r.Width - 28).Take(2))
            {
                sb.Text(vf, l, new Vector2(r.X + 14, y), Pal.Ink, false);
                y += 17;
            }
            y += 5;
        }
    }
}
