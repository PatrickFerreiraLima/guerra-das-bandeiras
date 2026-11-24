using GuerraDasBandeiras.Core;
using GuerraDasBandeiras.Gameplay;
using GuerraDasBandeiras.Net;

namespace GuerraDasBandeiras.Screens;

public class GameScreen : Screen
{
    readonly GameConfig _cfg;
    public readonly World W;
    readonly WorldRenderer _renderer;
    readonly Camera _cam = new();
    /// <summary>IA do time 1 (não existe na partida em rede).</summary>
    readonly AIController _ai;
    /// <summary>Modo de teste: uma IA também joga pelo time 0.</summary>
    public AIController AutoPlayer;
    /// <summary>Partida em rede (1x1): simulação em lockstep com o outro jogador. Nulo no jogo solo.</summary>
    readonly Lockstep _net;
    public Lockstep Net => _net;
    /// <summary>Erro que encerra a partida em rede (ex.: dessincronização).</summary>
    string _netError;

    /// <summary>Avança a simulação rapidamente (usado nos testes automáticos).</summary>
    public void Simulate(float seconds)
    {
        const float step = 1 / 30f;
        for (float t = 0; t < seconds && W.Winner < 0; t += step)
        {
            W.Update(step);
            _ai?.Update(step);
            AutoPlayer?.Update(step);
            _mission?.Update(step);
            _tutorial?.Update(step, SelUnits.Count(u => u.IsWorker));
        }
    }

    public const float IntroDuration = 3.2f;
    float _introT;
    bool _introSkipped;
    public bool IntroActive => !_introSkipped && _introT < IntroDuration;
    public void SkipIntro() => _introSkipped = true;
    public bool AttackMoveMode => _attackMoveMode;
    public IReadOnlyCollection<Entity> Selection => _selection;
    public Vector2 WorldToScreen(Vector2 w) => _cam.WorldToScreen(w);
    public Vector2 CameraPos => _cam.Pos;
    public void FocusOn(Vector2 p, float zoom) { _cam.Zoom = zoom; _cam.CenterOn(p); }
    public void SelectForShot(Func<Entity, bool> pred) { _selection.Clear(); foreach (var u in W.Units.Cast<Entity>().Concat(W.Buildings).Where(pred)) _selection.Add(u); }
    readonly Mission _mission;
    public readonly Tutorial TutorialRef;
    Tutorial _tutorial => TutorialRef;

    readonly HashSet<Entity> _selection = new();
    BuildingType? _placing;
    bool _attackMoveMode;
    Vector2? _dragStart;
    bool _dragging;
    readonly List<UiButton> _buttons = new();
    readonly List<(string text, Color color, float time)> _msgs = new();
    readonly Dictionary<int, List<Entity>> _groups = new();
    bool _paused, _help, _objCollapsed;
    float _endTimer, _alertCooldown, _lastClickTime, _miniTimer, _realTime;
    Entity _lastClicked;
    float _gameSpeed = 1f;
    Vector2? _ping; float _pingTime;
    int _idleCycle;

    Texture2D _miniTerrain, _miniFog;
    Color[] _fogData;

    Rectangle _top, _bottom, _mini, _info, _cmd, _obj, _power;

    public GameScreen(GameConfig cfg, NetPeer peer = null, NetLink link = null)
    {
        _cfg = cfg;
        W = new World(cfg);
        _renderer = new WorldRenderer(W);
        if (cfg.Versus && peer != null) _net = new Lockstep(W, peer, W.Local, link);
        else _ai = new AIController(W, 1) { Passive = cfg.Mode != GameMode.Skirmish };
        if (cfg.Mode == GameMode.Tutorial)
        {
            TutorialRef = new Tutorial(W);
            TutorialRef.StepCompleted = i => Notify($"Passo concluído: {Tutorial.Steps[i].Goal}", Pal.Gold);
        }
        if (cfg.Mode == GameMode.Mission)
        {
            _mission = new Mission(W);
            _mission.StepCompleted = i => Notify($"Objetivo cumprido: {Mission.Steps[i].Goal}", Pal.Gold);
        }

        W.Notify = Notify;
        Gui.WarnSink = s => Notify(s, Pal.Gold);
        W.OnScreen = p => _cam.ViewRect.Contains(p);
        W.Damaged = OnDamaged;
        W.BuildingCompleted = b =>
        {
            if (b.Team != W.Local) return;
            Notify($"{b.Def.Name} pronto(a)!", Pal.GreenLight);
            Sfx.BuildingDone();
        };
        W.UnitTrained = u => { if (u.Team == W.Local) Sfx.UnitReady(); };

        _cam.Zoom = 1.1f;
        _cam.ViewW = Game.W; _cam.ViewH = Game.H;
        // câmera na própria base, puxada para o lado do rio (base oeste: para a direita; base leste: para a esquerda)
        var home = W.StartTiles[W.Local];
        _cam.CenterOn(W.CenterOf(new Point(home.X + (home.X < W.W / 2 ? 4 : -2), home.Y + 1)));
        BuildMinimapTextures();

        Notify(cfg.Mode == GameMode.Tutorial ? "Tutorial: 6 passos rápidos. Siga o destaque dourado."
            : cfg.Mode == GameMode.Mission ? "Missão: A primeira bandeira"
            : cfg.Versus ? $"{cfg.Names[W.Local]} ({W.Me.Faction.Name}) x {cfg.Names[W.Foe]} ({W.Enemy.Faction.Name}) — boa sorte!"
            : $"{W.Me.Faction.Name} x {W.Enemy.Faction.Name} — boa sorte!", Pal.Gold);
        Notify("F1 mostra os controles a qualquer momento.", Color.White);
        if (_net != null) Notify("Enter abre o chat com o adversário.", Color.White);
    }

    /// <summary>Avisos discretos: no máximo 4, somem sozinhos, sem repetir a mesma frase em sequência.</summary>
    void Notify(string text, Color c)
    {
        if (_msgs.Count > 0 && _msgs[^1].text == text) { _msgs[^1] = (text, c, 4f); return; }
        _msgs.Add((text, c, 4f));
        if (_msgs.Count > 4) _msgs.RemoveAt(0);
    }

    float _capitalAlertCd, _capitalFlash, _popWarnCd;
    string Clock => $"{(int)(W.Time / 60):00}:{(int)(W.Time % 60):00}";

    /// <summary>
    /// Toda ordem do jogador passa por aqui. No jogo solo é aplicada na hora (igual a antes);
    /// na rede vai para o lockstep e é aplicada nas duas máquinas no mesmo tick.
    /// </summary>
    void Send(Cmd c)
    {
        c.Team = (byte)W.Local;
        if (_net != null) _net.Queue(c);
        else Commands.Apply(W, c);
    }

    /// <summary>Situação da conexão: adversário que sai perde por abandono; dessincronização encerra a partida.</summary>
    void CheckNet()
    {
        if (_net == null) return;
        if (_net.Error != null && _netError == null)
        {
            _netError = _net.Error;
            _net.Leave();
            Sfx.Error();
            return;
        }
        if (_net.OpponentLeft && W.Winner < 0)
        {
            W.SetWinner(W.Local, $"{_cfg.Names[W.Foe]} saiu da partida. Vitória por abandono.");
            Notify(_net.LeftReason, Pal.Gold);
        }
        if (_net.Reconnecting && !_wasReconnecting) { AddChat(null, "Conexão perdida. Tentando reconectar…", Pal.Gold); Sfx.Warning(); }
        if (!_net.Reconnecting && _wasReconnecting && !_net.OpponentLeft) { AddChat(null, "Conexão restabelecida. A partida continua!", Pal.GreenLight); Sfx.Click(); }
        _wasReconnecting = _net.Reconnecting;
    }
    bool _wasReconnecting;

    // ===================================================================
    // Chat (só na partida em rede): Enter abre, Enter manda, Esc fecha
    // ===================================================================

    public bool ChatOpen => _chatOpen;
    bool _chatOpen;
    string _chatText = "";
    readonly List<(string who, string text, Color color, float age)> _chat = new();
    const int ChatMax = 120;

    /// <summary>Linha no histórico do chat (who = null para avisos do sistema).</summary>
    void AddChat(string who, string text, Color c)
    {
        _chat.Add((who, text, c, 0));
        if (_chat.Count > 40) _chat.RemoveAt(0);
    }

    /// <summary>Manda uma mensagem (também usado pelos testes).</summary>
    public void SayInChat(string text)
    {
        text = new string(text.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        if (text.Length == 0 || _net == null) return;
        if (text.Length > ChatMax) text = text[..ChatMax];
        if (_net.SendChat(text)) AddChat(_cfg.Names[W.Local], text, W.Me.Color);
        else AddChat(null, "Mensagem não enviada: sem conexão no momento.", Pal.Red);
    }

    /// <summary>Mensagens que chegaram + digitação. Roda antes de tudo para o Enter/Esc do chat não virarem atalhos.</summary>
    void UpdateChat(float dt)
    {
        if (_net == null) return;
        while (_net.ChatIn.Count > 0)
        {
            var msg = _net.ChatIn.Dequeue();
            AddChat(_cfg.Names[W.Foe], msg.Length > ChatMax ? msg[..ChatMax] : msg, W.Enemy.Color);
            Sfx.Click();
        }
        for (int i = 0; i < _chat.Count; i++) _chat[i] = (_chat[i].who, _chat[i].text, _chat[i].color, _chat[i].age + dt);

        if (!_chatOpen)
        {
            if (Input.KeyPressed(Keys.Enter) && W.Winner < 0) { _chatOpen = true; _chatText = ""; Input.TextCapture = true; }
            return;
        }
        if (Input.RawKeyPressed(Keys.Escape)) { CloseChat(); return; }
        foreach (var ch in Input.Typed)
        {
            if (ch == '\b') { if (_chatText.Length > 0) _chatText = _chatText[..^1]; }
            else if (ch == '\r' || ch == '\n') { SayInChat(_chatText); CloseChat(); return; }
            else if (!char.IsControl(ch) && _chatText.Length < ChatMax) _chatText += ch;
        }
    }

    void CloseChat()
    {
        _chatOpen = false;
        _chatText = "";
        Input.TextCapture = false;
    }

    void DrawChat(SpriteBatch sb)
    {
        if (_net == null) return;
        var f = Assets.FontNarrow(15);
        var fb = Assets.FontBold(15);
        int x = 14;
        float y = _bottom.Y - 14 - (_chatOpen ? 36 : 0);
        // as mais recentes embaixo; com o chat fechado, cada mensagem some depois de 12 s
        var lines = _chat.Where(c => _chatOpen || c.age < 12f).TakeLast(_chatOpen ? 10 : 5).Reverse().ToList();
        foreach (var (who, text, color, age) in lines)
        {
            float a = _chatOpen ? 1f : Math.Clamp((12f - age) / 1.5f, 0, 1);
            string prefix = who == null ? "" : who + ": ";
            float pw = who == null ? 0 : fb.MeasureString(prefix).X;
            float tw = f.MeasureString(text).X;
            y -= 24;
            sb.Fill(new Rectangle(x - 6, (int)y - 2, (int)(pw + tw) + 12, 22), Color.Black * (0.45f * a));
            if (who != null) sb.Text(fb, prefix, new Vector2(x, y), Color.Lerp(color, Color.White, 0.35f) * a);
            sb.Text(f, text, new Vector2(x + pw, y), (who == null ? color : Color.White) * a);
        }
        if (_chatOpen)
        {
            var r = new Rectangle(x - 6, _bottom.Y - 44, 520, 32);
            sb.Panel(r, Pal.PanelBg * 0.95f, Pal.Gold, 2);
            string shown = _chatText + ((int)(_realTime * 2) % 2 == 0 ? "|" : "");
            sb.Text(fb, "Chat:", new Vector2(r.X + 8, r.Y + 6), Pal.Gold);
            if (_chatText.Length == 0) sb.Text(f, "digite e aperte Enter  •  Esc fecha", new Vector2(r.X + 62, r.Y + 6), Color.White * 0.45f);
            sb.Text(f, shown, new Vector2(r.X + 56, r.Y + 6), Color.White);
        }
    }

    void OnDamaged(Entity target, Entity source)
    {
        if (target.Team != W.Local) return;
        // a capital tem aviso próprio: vinheta vermelha + som, mesmo se estiver na tela
        if (target is Building { Def.Type: BuildingType.Centro } && _capitalAlertCd <= 0)
        {
            _capitalAlertCd = 10f;
            _capitalFlash = 1.6f;
            Notify("Seu Centro de Comando está sob ataque!", Pal.Red);
            Sfx.CapitalAttacked();
            _ping = target.Pos; _pingTime = 3f;
            return;
        }
        if (_alertCooldown > 0 || W.OnScreen(target.Pos)) return;
        _alertCooldown = 12f;
        Notify(target is Building ? "Sua base está sob ataque!" : "Suas tropas estão sob ataque!", Pal.Red);
        Sfx.UnderAttack();
        _ping = target.Pos; _pingTime = 3f;
    }

    // ===================================================================
    // Atualização
    // ===================================================================

    void Layout()
    {
        var ui = Ui;
        _top = new Rectangle(0, 0, (int)ui.X, 40);
        _bottom = new Rectangle(0, (int)ui.Y - 170, (int)ui.X, 170);
        _mini = new Rectangle(10, _bottom.Y + 12, 240, 150);
        int infoW = (int)Math.Clamp(ui.X * 0.32f, 330, 470);
        _info = new Rectangle(_mini.Right + 12, _bottom.Y + 12, infoW, 150);
        _power = new Rectangle((int)ui.X - 10 - 132, _bottom.Y + 12, 132, 150);
        _cmd = new Rectangle(_info.Right + 12, _bottom.Y + 12, _power.X - _info.Right - 22, 150);
        _obj = _mission != null || _tutorial != null
            ? new Rectangle(10, _top.Bottom + 10, 400, _objCollapsed ? 34 : _tutorial != null ? 140 : 128)
            : new Rectangle(10, _top.Bottom + 10, 400, _objCollapsed ? 34 : 58);
    }

    List<Unit> SelUnits => _selection.OfType<Unit>().Where(u => u.Team == W.Local && !u.Dead).ToList();
    Building SelBuilding => _selection.Count == 1 ? _selection.First() as Building : null;

    public override void Update(float dt)
    {
        _realTime += dt;
        Layout();
        _cam.ViewW = Game.W; _cam.ViewH = Game.H;

        // introdução: congela a simulação (relógio, IA e mundo não andam) até acabar ou ser pulada
        Ambience.SetGame();

        // erro fatal da partida em rede: só o aviso e o botão de voltar
        if (_netError != null)
        {
            _buttons.Clear();
            var c = Ui / 2;
            _buttons.Add(new UiButton { Rect = new Rectangle((int)c.X - 130, (int)c.Y + 40, 260, 46), Label = "Voltar ao menu", Hotkey = Keys.Escape, OnClick = () => Game.SetScreen(new MenuScreen()) });
            Gui.Handle(_buttons);
            return;
        }

        if (IntroActive)
        {
            _introT += dt;
            if (Input.LeftPressed || Input.RightPressed || Input.AnyKeyPressed) SkipIntro();
            _cam.Clamp(W.W * World.T, W.H * World.T, _top.Height * Game1.UiScaleEff, _bottom.Height * Game1.UiScaleEff);
            _net?.Update(0);   // na rede: continua recebendo mensagens (e percebe se o outro saiu) durante a introdução
            CheckNet();
            return;
        }

        UpdateChat(dt);

        if (Input.KeyPressed(Keys.Escape))
        {
            if (_placing != null || _attackMoveMode) { _placing = null; _attackMoveMode = false; }
            else if (_help) _help = false;
            else _paused = !_paused;
        }
        if (Input.KeyPressed(Keys.F1)) _help = !_help;

        if (_paused)
        {
            BuildPauseButtons();
            Gui.Handle(_buttons);
            // contra outra pessoa não existe pausa: o menu abre, mas a partida continua correndo
            if (_net == null) return;
            _net.Update(dt);
            CheckNet();
            return;
        }

        if (W.Winner >= 0)
        {
            _endTimer += dt;
            if (_endTimer > 3f)
            {
                _net?.Leave();
                Game.SetScreen(new EndScreen(_cfg, W));
                return;
            }
        }

        if (_net != null)
        {
            _net.Update(dt);
            CheckNet();
        }
        else
        {
            float sdt = dt * _gameSpeed;
            W.Update(sdt);
            _ai?.Update(sdt);
            AutoPlayer?.Update(sdt);
            _mission?.Update(sdt);
            _tutorial?.Update(sdt, SelUnits.Count(u => u.IsWorker));
        }

        _selection.RemoveWhere(e => e.Dead || (e.Team != W.Local && e is Unit u && !W.CanSee(W.Local, u)));
        foreach (var g in _groups.Values) g.RemoveAll(e => e.Dead);

        for (int i = _msgs.Count - 1; i >= 0; i--)
        {
            var m = _msgs[i];
            m.time -= dt;
            if (m.time <= 0) _msgs.RemoveAt(i); else _msgs[i] = m;
        }
        _alertCooldown -= dt;
        _capitalAlertCd -= dt;
        _capitalFlash = Math.Max(0, _capitalFlash - dt);
        _popWarnCd -= dt;
        if (_popWarnCd <= 0 && W.Buildings.Any(b => b.Team == W.Local && b.PopBlocked))
        {
            _popWarnCd = 20f;
            Notify("População máxima: a produção parou. Construa Casas.", Pal.Gold);
            Sfx.Warning();
        }
        _pingTime -= dt;

        UpdateCamera(dt);

        BuildHudButtons();
        bool consumed = Gui.Handle(_buttons);
        var um = Input.UiMousePt;
        bool overHud = _top.Contains(um) || _bottom.Contains(um) || _obj.Contains(um);

        if (!consumed)
        {
            if (overHud && _dragStart == null) HandleHudMouse(um);
            else HandleWorldMouse(overHud);
        }
        HandleHotkeys();

        _miniTimer -= dt;
        if (_miniTimer <= 0) { _miniTimer = 0.25f; UpdateMinimapFog(); }
    }

    void UpdateCamera(float dt)
    {
        float speed = 900f / _cam.Zoom * dt;
        var move = Vector2.Zero;
        bool ctrl = Input.Ctrl;
        if (Input.KeyDown(Keys.Left) || (Input.KeyDown(Keys.A) && !ctrl)) move.X -= 1;
        if (Input.KeyDown(Keys.Right) || (Input.KeyDown(Keys.D) && !ctrl)) move.X += 1;
        if (Input.KeyDown(Keys.Up) || (Input.KeyDown(Keys.W) && !ctrl)) move.Y -= 1;
        if (Input.KeyDown(Keys.Down) || (Input.KeyDown(Keys.S) && !ctrl)) move.Y += 1;
        if (move != Vector2.Zero) move.Normalize();
        var mp = Input.MousePos;
        if (Input.Active && _dragStart == null && Game.IsActive && mp.X >= 0 && mp.Y >= 0 && mp.X < Game.W && mp.Y < Game.H)
        {
            if (mp.X <= 4) move.X -= 1;
            if (mp.X >= Game.W - 5) move.X += 1;
            if (mp.Y <= 4) move.Y -= 1;
            if (mp.Y >= Game.H - 5) move.Y += 1;
        }
        _cam.Pos += move * speed;
        if (Input.MiddleDown) _cam.Pos -= Input.MouseDelta.ToVector2() / _cam.Zoom;

        var um = Input.UiMousePt;
        if (Input.Wheel != 0 && !_bottom.Contains(um) && !_top.Contains(um))
        {
            float z = Math.Clamp(_cam.Zoom * (Input.Wheel > 0 ? 1.12f : 1 / 1.12f), 0.5f, 2.2f);
            _cam.ZoomAt(z, Input.MousePos.ToVector2());
        }
        _cam.Clamp(W.W * World.T, W.H * World.T, _top.Height * Game1.UiScaleEff, _bottom.Height * Game1.UiScaleEff);
    }

    Vector2 MouseWorld => _cam.ScreenToWorld(Input.MousePos.ToVector2());

    void HandleHudMouse(Point um)
    {
        if (_mini.Contains(um))
        {
            var wp = MiniToWorld(um);
            if (Input.LeftDown && Input.Active) _cam.CenterOn(wp);
            if (Input.RightPressed) IssueContext(wp, null);
        }
        if (_obj.Contains(um) && Input.LeftPressed) _objCollapsed = !_objCollapsed;
    }

    void HandleWorldMouse(bool overHud)
    {
        var wp = MouseWorld;

        if (_placing is BuildingType bt)
        {
            if (Input.LeftPressed && !overHud) TryPlace(bt, wp);
            if (Input.RightPressed) _placing = null;
            return;
        }
        if (_attackMoveMode)
        {
            if (Input.LeftPressed && !overHud)
            {
                MoveGroup(SelUnits, wp, true);
                W.AddClickMarker(wp, Pal.Red);
                Sfx.OrderAttack();
                if (!Input.Shift) _attackMoveMode = false;
            }
            if (Input.RightPressed) _attackMoveMode = false;
            return;
        }

        if (Input.LeftPressed && !overHud) { _dragStart = Input.MousePos.ToVector2(); _dragging = false; }
        if (_dragStart is Vector2 ds && Input.LeftDown && Vector2.Distance(ds, Input.MousePos.ToVector2()) > 6) _dragging = true;
        if (_dragStart is Vector2 start && Input.LeftReleased)
        {
            if (_dragging) BoxSelect(_cam.ScreenToWorld(start), wp);
            else ClickSelect(wp);
            _dragStart = null; _dragging = false;
        }

        if (Input.RightPressed && !overHud) IssueContext(wp, PickEntity(wp));
    }

    void TryPlace(BuildingType bt, Vector2 wp)
    {
        var def = Defs.Buildings[bt];
        var tl = PlacementTile(bt, wp);
        var p = W.Me;
        if (!p.CanPay(def.Cost)) { Notify(p.Missing(def.Cost), Pal.Red); Sfx.Error(); _placing = null; return; }
        var problem = W.PlaceProblem(bt, tl, W.Local);
        if (problem != null) { Notify(problem + ".", Pal.Red); Sfx.Error(); return; }
        // o prédio é criado (e pago) quando a ordem é aplicada; os trabalhadores selecionados vão erguê-lo
        Send(new Cmd
        {
            Type = CmdType.Place, Kind = (byte)bt, A = tl.X, B = tl.Y, Queue = Input.Shift,
            Ids = SelUnits.Where(u => u.IsWorker).Select(u => u.Id).ToArray(),
        });
        Sfx.OrderBuild();
        if (!Input.Shift) _placing = null;
    }

    Point PlacementTile(BuildingType bt, Vector2 wp)
    {
        int s = Defs.Buildings[bt].Size;
        return W.TileOf(wp - new Vector2((s - 1) * World.T / 2f));
    }

    Entity PickEntity(Vector2 wp)
    {
        Unit bestU = null;
        float bestD = float.MaxValue;
        foreach (var u in W.Units)
        {
            if (!u.WorldRect.Contains(wp) || !W.CanSee(W.Local, u)) continue;
            float d = Vector2.DistanceSquared(u.Pos, wp);
            if (d < bestD) { bestD = d; bestU = u; }
        }
        if (bestU != null) return bestU;
        foreach (var b in W.Buildings)
        {
            var r = b.WorldRect;
            var ext = new RectangleF(r.X, r.Y - r.H * 0.35f, r.W, r.H * 1.35f);
            if (ext.Contains(wp) && (b.Team == W.Local || W.IsExplored(b))) return b;
        }
        foreach (var n in W.Resources)
        {
            var r = n.WorldRect;
            var ext = new RectangleF(r.X, r.Y - (n.Kind == Res.Wood ? 30 : 0), r.W, r.H + (n.Kind == Res.Wood ? 30 : 0));
            if (ext.Contains(wp) && W.IsExplored(n)) return n;
        }
        return null;
    }

    void ClickSelect(Vector2 wp)
    {
        var e = PickEntity(wp);
        if (e == null) { if (!Input.Shift) _selection.Clear(); return; }

        bool dbl = e == _lastClicked && _realTime - _lastClickTime < 0.35f;
        _lastClicked = e; _lastClickTime = _realTime;

        if (dbl && e is Unit du && du.Team == W.Local)
        {
            _selection.Clear();
            foreach (var u in W.Units.Where(u => u.Team == W.Local && u.Def.Type == du.Def.Type && _cam.ViewRect.Contains(u.Pos)))
                _selection.Add(u);
        }
        else if (Input.Shift && e is Unit su && su.Team == W.Local)
        {
            if (!_selection.Remove(e)) { _selection.RemoveWhere(x => x is not Unit || x.Team != W.Local); _selection.Add(e); }
        }
        else
        {
            _selection.Clear();
            _selection.Add(e);
        }
        Sfx.Select();
    }

    void BoxSelect(Vector2 a, Vector2 b)
    {
        var min = Vector2.Min(a, b);
        var max = Vector2.Max(a, b);
        var found = W.Units.Where(u => u.Team == W.Local && u.Pos.X >= min.X - 10 && u.Pos.X <= max.X + 10 && u.Pos.Y >= min.Y - 10 && u.Pos.Y <= max.Y + 20).ToList();
        if (!Input.Shift) _selection.Clear();
        else _selection.RemoveWhere(x => x is not Unit || x.Team != W.Local);
        // se a caixa pegou soldados e trabalhadores, prioriza os soldados
        if (found.Any(u => u.IsMilitary) && found.Any(u => u.IsWorker) && !Input.Shift) found = found.Where(u => u.IsMilitary).ToList();
        foreach (var u in found) _selection.Add(u);
        if (found.Count > 0) Sfx.Select();
    }

    void IssueContext(Vector2 wp, Entity target)
    {
        var units = SelUnits;
        if (units.Count == 0)
        {
            var sb = SelBuilding;
            if (sb != null && sb.Team == W.Local && sb.TrainOptions.Any())
            {
                Send(new Cmd { Type = CmdType.Rally, A = sb.Id, B = target is ResourceNode ? target.Id : 0, X = wp.X, Y = wp.Y });
                W.AddClickMarker(wp, Pal.Gold);
                Sfx.OrderMove();
            }
            return;
        }

        bool q = Input.Shift;

        if (target != null && target.Team == W.Foe && (target is Building || W.CanSee(W.Local, target)))
        {
            foreach (var u in units) Send(Cmd.Order(u, UnitOrder.Attack, target.Pos, target, q));
            W.AddClickMarker(target.Pos, Pal.Red);
            Sfx.OrderAttack();
            return;
        }

        if (target is ResourceNode node)
        {
            var workers = units.Where(u => u.IsWorker).ToList();
            bool first = true;
            foreach (var u in workers)
            {
                var n = first ? node : W.NearestResource(node.Kind, node.Pos, World.T * 3, u) ?? node;
                Send(Cmd.Order(u, UnitOrder.Gather, n.Pos, n, q));
                first = false;
            }
            var others = units.Where(u => !u.IsWorker).ToList();
            if (others.Count > 0) MoveGroup(others, wp, false);
            W.AddClickMarker(node.Pos, Pal.Gold);
            Sfx.OrderGather();
            return;
        }

        if (target is Building ob && ob.Team == W.Local)
        {
            var workers = units.Where(u => u.IsWorker).ToList();
            if ((!ob.Done || ob.Damaged) && workers.Count > 0)
            {
                foreach (var u in workers) Send(Cmd.Order(u, UnitOrder.Build, ob.Pos, ob, q));
                W.AddClickMarker(ob.Pos, Pal.Gold);
                Sfx.OrderGather();
                if (ob.Done) Notify(W.Me.Faction.Id == FactionId.MinasGerais ? "Reparando (grátis para Minas)." : "Reparando: custa um pouco de madeira.", Color.White);
                var rest = units.Where(u => !u.IsWorker).ToList();
                if (rest.Count > 0) MoveGroup(rest, wp, false);
                return;
            }
            if (ob.Def.Dropoff && workers.Any(u => u.Cargo > 0))
            {
                foreach (var u in workers.Where(u => u.Cargo > 0)) Send(new Cmd { Type = CmdType.Return, A = u.Id });
                Sfx.OrderGather();
                return;
            }
        }

        MoveGroup(units, wp, false);
        W.AddClickMarker(wp, Pal.GreenLight);
        Sfx.OrderMove();
    }

    void MoveGroup(List<Unit> units, Vector2 dest, bool attackMove)
    {
        int n = units.Count;
        if (n == 0) return;
        bool q = Input.Shift;
        int cols = (int)MathF.Ceiling(MathF.Sqrt(n));
        int rows = (int)MathF.Ceiling(n / (float)cols);
        const float sp = 32f;
        // os mais próximos do destino ficam na frente
        var ordered = units.OrderBy(u => Vector2.DistanceSquared(u.Pos, dest)).ToList();
        for (int i = 0; i < n; i++)
        {
            var off = new Vector2((i % cols - (cols - 1) / 2f) * sp, (i / cols - (rows - 1) / 2f) * sp);
            var d = dest + off;
            if (!W.Passable(W.TileOf(d))) d = dest;
            var kind = attackMove && ordered[i].IsMilitary ? UnitOrder.AttackMove : UnitOrder.Move;
            Send(Cmd.Order(ordered[i], kind, d, null, q));
        }
        // na rede a ordem só vale daqui a alguns ticks: o aviso de "inalcançável" fica para o jogo solo
        if (_net == null && !q && ordered.Any(u => u.Unreachable && (u.Order is UnitOrder.Move or UnitOrder.AttackMove)))
        {
            Notify(W.TerrainAt(dest) == Terrain.Water ? "Não dá para entrar no rio: use as partes rasas. Indo até a margem." : "Destino inalcançável: indo até o ponto mais próximo.", Pal.Gold);
            Sfx.Warning();
        }
    }

    void UseAbilities()
    {
        var specials = SelUnits.Where(u => u.HasAbility).ToList();
        if (specials.Count == 0) return;
        // só manda a ordem para quem pode usar agora (a habilidade é validada de novo ao aplicar)
        var ready = specials.Where(u => u.AbilityReady).ToList();
        foreach (var u in ready) Send(new Cmd { Type = CmdType.Ability, A = u.Id });
        if (ready.Count == 0) { Notify(specials.Select(u => u.AbilityBlock()).FirstOrDefault(s => s != null) ?? "Habilidade indisponível.", Pal.Gold); Sfx.Error(); }
    }

    void UsePower()
    {
        var p = W.Me;
        if (p.PowerReady) Send(new Cmd { Type = CmdType.Power });
        else
        {
            Notify(p.PowerActive ? "O poder já está ativo." : $"{p.Faction.Power} recarrega em {Math.Ceiling(p.PowerCd)}s.", Color.White);
            Sfx.Error();
        }
    }

    void HandleHotkeys()
    {
        if (Input.KeyPressed(Keys.H) && !Input.Ctrl)
        {
            var c = W.Buildings.FirstOrDefault(b => b.Team == W.Local && b.Def.Type == BuildingType.Centro);
            if (c != null) { _selection.Clear(); _selection.Add(c); _cam.CenterOn(c.Pos); }
        }
        if (Input.KeyPressed(Keys.Space) && _selection.Count > 0)
            _cam.CenterOn(new Vector2(_selection.Average(e => e.Pos.X), _selection.Average(e => e.Pos.Y)));
        if (Input.KeyPressed(Keys.OemPeriod)) SelectIdleWorker();

        Keys[] nums = { Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5 };
        for (int i = 0; i < nums.Length; i++)
        {
            if (!Input.KeyPressed(nums[i])) continue;
            if (Input.Ctrl)
            {
                _groups[i] = _selection.Where(e => e.Team == W.Local).ToList();
                Notify($"Grupo {i + 1} criado ({_groups[i].Count}).", Color.White);
            }
            else if (_groups.TryGetValue(i, out var g) && g.Count > 0)
            {
                bool again = _selection.SetEquals(g);
                _selection.Clear();
                foreach (var e in g) _selection.Add(e);
                if (again) _cam.CenterOn(new Vector2(g.Average(e => e.Pos.X), g.Average(e => e.Pos.Y)));
            }
        }
    }

    void SelectIdleWorker()
    {
        var idle = W.Units.Where(u => u.Team == W.Local && u.IsWorker && u.IsIdle).ToList();
        if (idle.Count == 0) { Notify("Nenhum trabalhador ocioso.", Color.White); return; }
        var u = idle[_idleCycle++ % idle.Count];
        _selection.Clear();
        _selection.Add(u);
        _cam.CenterOn(u.Pos);
    }

    void Train(Building b, UnitType t)
    {
        var def = Defs.Units[t];
        var p = W.Me;
        if (b.Queue.Count >= 6) { Notify("Fila cheia (máximo 6).", Pal.Red); Sfx.Error(); return; }
        if (!p.CanPay(def.Cost)) { Notify(p.Missing(def.Cost), Pal.Red); Sfx.Error(); return; }
        bool overPop = p.Pop + b.Queue.Sum(x => Defs.Units[x].Pop) + def.Pop > p.PopCap;
        Send(new Cmd { Type = CmdType.Train, A = b.Id, Kind = (byte)t });
        if (overPop) Notify("População máxima! Construa Casas para liberar espaço.", Pal.Gold);
    }

    void CancelQueue(Building b, int i)
    {
        if (i >= b.Queue.Count) return;
        Send(new Cmd { Type = CmdType.Cancel, A = b.Id, B = i });
    }

    // ===================================================================
    // Botões do HUD
    // ===================================================================

    static readonly Keys[] CmdKeys = { Keys.Q, Keys.E, Keys.R, Keys.T, Keys.Y, Keys.U, Keys.I };

    void BuildHudButtons()
    {
        _buttons.Clear();
        _buttons.Add(new UiButton { Rect = new Rectangle(_top.Right - 96, 5, 88, 30), Label = "Menu", FontSize = 14, Accent = Pal.InkSoft, OnClick = () => _paused = true });
        _buttons.Add(new UiButton { Rect = new Rectangle(_top.Right - 150, 5, 48, 30), Label = "?", FontSize = 16, Accent = Pal.InkSoft, OnClick = () => _help = !_help, TooltipTitle = "Ajuda (F1)", Tooltip = "Mostra os controles e as regras de vantagem." });
        int idle = W.Units.Count(u => u.Team == W.Local && u.IsWorker && u.IsIdle);
        if (idle > 0)
            _buttons.Add(new UiButton
            {
                Rect = new Rectangle(_top.Right - 316, 5, 158, 30), Label = $"Ociosos: {idle}", FontSize = 13, Accent = new Color(150, 110, 40),
                OnClick = SelectIdleWorker, TooltipTitle = "Trabalhadores ociosos (.)", Tooltip = "Clique para encontrar um trabalhador parado."
            });

        var cmds = new List<UiButton>();
        var units = SelUnits;
        var sbld = SelBuilding;
        var p = W.Me;

        if (sbld != null && sbld.Team == W.Local)
        {
            if (sbld.Done)
                foreach (var t in sbld.TrainOptions)
                {
                    var def = Defs.Units[t];
                    var tt = t;
                    var bb = sbld;
                    cmds.Add(new UiButton
                    {
                        Icon = Defs.UnitSprite(def, p.ColorSet, p.Faction.Id), Label = def.Name, Cost = def.Cost, Stock = p.Get, Enabled = p.CanPay(def.Cost), Warn = !p.CanPay(def.Cost) ? p.Missing(def.Cost) + "." : p.Pop + def.Pop > p.PopCap ? "População máxima: entra na fila, mas só sai quando houver espaço (construa Casas)." : null,
                        TooltipTitle = $"{def.Name}  ({def.Cost.Long()})", Tooltip = $"{def.Desc}\n{def.Strong}. Vida {def.Hp}, dano {def.Dmg}, {def.Time:0}s de treino.",
                        OnClick = () => Train(bb, tt)
                    });
                }
            // fila (no painel de informações)
            for (int i = 0; i < sbld.Queue.Count; i++)
            {
                int idx = i;
                var bb = sbld;
                var def = Defs.Units[sbld.Queue[i]];
                _buttons.Add(new UiButton
                {
                    Rect = new Rectangle(_info.X + 92 + i * 46, _info.Bottom - 50, 42, 42), Icon = Defs.UnitSprite(def, p.ColorSet, p.Faction.Id), Accent = Pal.PanelBg2,
                    Progress = i == 0 ? sbld.ProdTimer / def.Time : -1, TooltipTitle = def.Name, Tooltip = "Clique para cancelar e receber os recursos de volta.",
                    OnClick = () => CancelQueue(bb, idx)
                });
            }
        }
        else if (units.Count > 0)
        {
            if (units.Any(u => u.IsWorker))
            {
                foreach (var bt in new[] { BuildingType.Casa, BuildingType.Coletor, BuildingType.Quartel, BuildingType.Torre, BuildingType.Oficina })
                {
                    var def = Defs.Buildings[bt];
                    var btt = bt;
                    var spriteIdx = p.Faction.BuildingSprite(bt);
                    cmds.Add(new UiButton
                    {
                        Icon = Assets.Structure(spriteIdx), Label = def.Name, Cost = def.Cost, Stock = p.Get, Enabled = p.CanPay(def.Cost), Warn = p.CanPay(def.Cost) ? null : p.Missing(def.Cost) + ".", Selected = _placing == bt,
                        TooltipTitle = $"{def.Name}  ({def.Cost.Long()})", Tooltip = def.Desc,
                        OnClick = () => { _placing = btt; _attackMoveMode = false; }
                    });
                }
            }
            var special = units.FirstOrDefault(u => u.HasAbility);
            if (special != null)
            {
                var sd = special.Def;
                var best = units.Where(u => u.Def.Type == sd.Type).OrderBy(u => u.AbilityCooldown).First();
                bool ready = best.AbilityReady;
                cmds.Add(new UiButton
                {
                    Icon = Defs.UnitSprite(sd, p.ColorSet, p.Faction.Id), Label = sd.Ability,
                    Sub = best.AbilityActive ? "ATIVA" : ready ? "pronta" : $"{Math.Ceiling(best.AbilityCooldown)}s",
                    Enabled = ready, Hotkey = Keys.V, Accent = Pal.Gold * 0.9f, Selected = best.AbilityActive,
                    Progress = ready ? -1 : 1 - Math.Max(0, best.AbilityCooldown) / sd.AbilityCd,
                    TooltipTitle = $"{sd.Ability} (V)", Tooltip = $"{sd.AbilityDesc}\nDuração {sd.AbilityDur:0}s  •  Recarga {sd.AbilityCd:0}s", Warn = best.AbilityBlock(), OnClick = UseAbilities
                });
            }
            if (units.Any(u => u.IsMilitary))
                cmds.Add(new UiButton
                {
                    Label = "Atacar", Selected = _attackMoveMode, Hotkey = Keys.F, Accent = Pal.Terracotta, FontSize = 15,
                    TooltipTitle = "Atacar-mover (F)", Tooltip = "Clique no mapa: as tropas avançam e atacam tudo que encontrarem no caminho. Shift enfileira.",
                    OnClick = () => { _attackMoveMode = true; _placing = null; }
                });
            cmds.Add(new UiButton
            {
                Label = "Parar", Hotkey = Keys.X, Accent = Pal.InkSoft, FontSize = 15, TooltipTitle = "Parar (X)", Tooltip = "Interrompe a ordem atual e limpa a fila.",
                OnClick = () => { foreach (var u in SelUnits) Send(new Cmd { Type = CmdType.Stop, A = u.Id }); }
            });

            // retratos da seleção múltipla
            if (units.Count > 1)
            {
                int i = 0;
                foreach (var u in units.Take(18))
                {
                    var uu = u;
                    int col = i % 9, row = i / 9;
                    _buttons.Add(new UiButton
                    {
                        Rect = new Rectangle(_info.X + 10 + col * 50, _info.Y + 40 + row * 52, 46, 48), Icon = Defs.UnitSprite(u.Def, p.ColorSet, p.Faction.Id),
                        Accent = Pal.PanelBg2, Progress = u.Hp / u.MaxHp,
                        OnClick = () => { if (Input.Shift) _selection.Remove(uu); else { _selection.Clear(); _selection.Add(uu); } }
                    });
                    i++;
                }
            }
        }

        // poder do comandante (sempre visível)
        var fac = p.Faction;
        _buttons.Add(new UiButton
        {
            Rect = _power, Icon = Defs.UnitSprite(Defs.Units[UnitType.GuardiaoDasGerais], p.ColorSet, p.Faction.Id), Label = fac.Power,
            Sub = p.PowerActive ? $"ATIVO {Math.Ceiling(p.PowerTime)}s" : p.PowerReady ? "pronto (C)" : $"{Math.Ceiling(p.PowerCd)}s",
            Enabled = p.PowerReady, Hotkey = Keys.C, Accent = Pal.Slate, Selected = p.PowerActive, Hidden = true,
            Progress = p.PowerReady || p.PowerActive ? -1 : 1 - Math.Max(0, p.PowerCd) / fac.PowerCd,
            TooltipTitle = $"{fac.Commander} — {fac.Power} (C)",
            Tooltip = $"{fac.PowerDesc}\nDuração {fac.PowerDur:0}s  •  Recarga {fac.PowerCd:0}s\nPassivo ({fac.Passive}): {fac.PassiveDesc}",
            Warn = p.PowerActive ? "Já está ativo." : p.PowerReady ? null : $"Recarrega em {Math.Ceiling(p.PowerCd)}s.",
            OnClick = UsePower
        });

        // tutorial: destaca o botão da etapa
        if (_tutorial?.HudHighlight is string hh)
            foreach (var c in cmds)
                c.Highlight = hh == "build:Quartel" ? c.Label == Defs.Buildings[BuildingType.Quartel].Name
                            : hh == "train" ? sbld != null && sbld.Def.Type == BuildingType.Quartel && c.Cost != null
                            : hh == "attack" && c.Label == "Atacar";

        // posiciona os botões de comando em grade
        int bw = 108, bh = 70, gap = 8;
        int perRow = Math.Max(1, (_cmd.Width + gap) / (bw + gap));
        int k = 0;
        foreach (var c in cmds)
        {
            c.Rect = new Rectangle(_cmd.X + (k % perRow) * (bw + gap), _cmd.Y + (k / perRow) * (bh + gap), bw, bh);
            if (c.Hotkey == Keys.None && k < CmdKeys.Length) c.Hotkey = CmdKeys[k];
            _buttons.Add(c);
            k++;
        }
    }

    void BuildPauseButtons()
    {
        _buttons.Clear();
        var c = Ui / 2;
        int x = (int)c.X - 170, y = (int)c.Y - 180;
        _buttons.Add(new UiButton { Rect = new Rectangle(x, y, 340, 46), Label = "Continuar", OnClick = () => _paused = false });
        void Row(int dy, Action minus, Action plus)
        {
            _buttons.Add(new UiButton { Rect = new Rectangle(x, y + dy, 46, 38), Label = "-", Accent = Pal.InkSoft, OnClick = minus });
            _buttons.Add(new UiButton { Rect = new Rectangle(x + 294, y + dy, 46, 38), Label = "+", Accent = Pal.InkSoft, OnClick = plus });
        }
        static float Step(float v, float d) => Math.Clamp(MathF.Round((v + d) * 10) / 10, 0, 1);
        Row(58, () => Game1.UiScale = Math.Max(0.7f, MathF.Round((Game1.UiScale - 0.1f) * 10) / 10), () => Game1.UiScale = Math.Min(2.2f, MathF.Round((Game1.UiScale + 0.1f) * 10) / 10));
        Row(104, () => { Assets.MasterVolume = Step(Assets.MasterVolume, -0.1f); Ambience.ApplyVolume(); }, () => { Assets.MasterVolume = Step(Assets.MasterVolume, 0.1f); Ambience.ApplyVolume(); });
        Row(150, () => Assets.SfxVolume = Step(Assets.SfxVolume, -0.1f), () => { Assets.SfxVolume = Step(Assets.SfxVolume, 0.1f); Sfx.Click(); });
        Row(196, () => { Assets.MusicVolume = Step(Assets.MusicVolume, -0.1f); Ambience.ApplyVolume(); }, () => { Assets.MusicVolume = Step(Assets.MusicVolume, 0.1f); Ambience.ApplyVolume(); });
        if (_net != null)
        {
            // em rede não há velocidade nem reinício: os dois mundos andam juntos. Sair = desistir.
            _buttons.Add(new UiButton
            {
                Rect = new Rectangle(x, y + 294, 340, 40), Label = "Desistir e sair", FontSize = 16, Accent = new Color(150, 60, 50),
                TooltipTitle = "Desistir", Tooltip = "Encerra a partida: o adversário vence por abandono.",
                OnClick = () => { _net.Leave(); Game.SetScreen(new MenuScreen()); }
            });
            return;
        }
        _buttons.Add(new UiButton
        {
            Rect = new Rectangle(x, y + 246, 340, 40), Label = $"Velocidade: {_gameSpeed:0.##}x", FontSize = 16, Accent = Pal.InkSoft,
            OnClick = () => _gameSpeed = _gameSpeed switch { 0.75f => 1f, 1f => 1.25f, 1.25f => 1.5f, _ => 0.75f }
        });
        _buttons.Add(new UiButton { Rect = new Rectangle(x, y + 294, 340, 40), Label = "Reiniciar partida", FontSize = 16, Accent = Pal.InkSoft, OnClick = () => Game.SetScreen(new GameScreen(new GameConfig { Mode = _cfg.Mode, PlayerFaction = _cfg.PlayerFaction, EnemyFaction = _cfg.EnemyFaction, Difficulty = _cfg.Difficulty })) });
        _buttons.Add(new UiButton { Rect = new Rectangle(x, y + 342, 340, 40), Label = "Sair para o menu", FontSize = 16, Accent = new Color(150, 60, 50), OnClick = () => Game.SetScreen(new MenuScreen()) });
    }

    // ===================================================================
    // Minimapa
    // ===================================================================

    void BuildMinimapTextures()
    {
        var gd = Game.GraphicsDevice;
        _miniTerrain = new Texture2D(gd, W.W, W.H);
        var data = new Color[W.W * W.H];
        for (int x = 0; x < W.W; x++)
            for (int y = 0; y < W.H; y++)
                data[y * W.W + x] = W.Ter[x, y] switch
                {
                    Terrain.Forest => new Color(38, 112, 62),
                    Terrain.Hill => new Color(150, 146, 132),
                    Terrain.Water => new Color(70, 140, 205),
                    Terrain.Ford => new Color(222, 204, 160),
                    Terrain.Dirt => new Color(150, 116, 80),
                    _ => new Color(92, 168, 88),
                };
        _miniTerrain.SetData(data);
        _miniFog = new Texture2D(gd, W.W, W.H);
        _fogData = new Color[W.W * W.H];
        UpdateMinimapFog();
    }

    void UpdateMinimapFog()
    {
        for (int x = 0; x < W.W; x++)
            for (int y = 0; y < W.H; y++)
                _fogData[y * W.W + x] = !W.Explored[x, y] ? new Color(10, 16, 14) : !W.Visible[x, y] ? new Color(10, 16, 14) * 0.5f : Color.Transparent;
        _miniFog.SetData(_fogData);
    }

    Vector2 MiniToWorld(Point p) => new((p.X - _mini.X) / (float)_mini.Width * W.W * World.T, (p.Y - _mini.Y) / (float)_mini.Height * W.H * World.T);
    Vector2 WorldToMini(Vector2 w) => new(_mini.X + w.X / (W.W * World.T) * _mini.Width, _mini.Y + w.Y / (W.H * World.T) * _mini.Height);

    // ===================================================================
    // Desenho
    // ===================================================================

    public override void Draw(SpriteBatch sb)
    {
        Layout();
        _renderer.Draw(sb, _cam, _selection, W.Time);

        // fantasma da construção e caixa de seleção
        sb.Begin(transformMatrix: _cam.Transform, samplerState: SamplerState.LinearClamp);
        if (_placing is BuildingType bt && !_paused)
        {
            var def = Defs.Buildings[bt];
            var tl = PlacementTile(bt, MouseWorld);
            bool ok = W.CanPlace(bt, tl, W.Local) && W.Me.CanPay(def.Cost);
            for (int x = 0; x < def.Size; x++)
                for (int y = 0; y < def.Size; y++)
                    sb.Fill(new Rectangle((tl.X + x) * World.T + 2, (tl.Y + y) * World.T + 2, World.T - 4, World.T - 4), (ok ? Pal.GreenLight : Pal.Red) * 0.35f);
            var spriteIdx = W.Me.Faction.BuildingSprite(bt);
            var tex = Assets.Structure(spriteIdx);
            float w = def.Size * World.T;
            sb.SpriteFeet(tex, new Vector2((tl.X + def.Size / 2f) * World.T, (tl.Y + def.Size) * World.T - 2), w * 0.95f / tex.Width, Color.White * 0.6f);
        }
        // destaques do tutorial no mapa
        if (_tutorial != null)
            foreach (var hp in _tutorial.WorldHighlights())
            {
                float k = (_realTime * 1.5f) % 1f;
                sb.Ellipse(hp + new Vector2(0, 10), 26 + k * 26, 13 + k * 13, Pal.Gold * (1 - k));
                sb.Ellipse(hp + new Vector2(0, 10), 24, 12, Pal.Gold);
                float bob = MathF.Sin(_realTime * 5) * 5;
                var tip = hp + new Vector2(0, -58 + bob);
                sb.Line(tip + new Vector2(-9, -12), tip, Pal.Gold, 4);
                sb.Line(tip + new Vector2(9, -12), tip, Pal.Gold, 4);
            }

        // ordens enfileiradas (Shift): linha tracejada com marcadores numerados
        foreach (var u in SelUnits.Take(12))
        {
            if (u.Queue.Count == 0) continue;
            var prev = u.Pos;
            int n = 1;
            foreach (var o in u.Queue)
            {
                var to = o.Target?.Pos ?? o.Pos;
                var d = to - prev; float len = d.Length();
                if (len > 1) for (float t = 0; t < len; t += 16) sb.Line(prev + d / len * t, prev + d / len * Math.Min(len, t + 8), Color.White * 0.45f, 1.5f);
                var c = o.Kind switch { UnitOrder.Attack or UnitOrder.AttackMove => Pal.Red, UnitOrder.Gather or UnitOrder.Build => Pal.Gold, _ => Pal.GreenLight };
                sb.Disc(to, 9, 9, Color.Black * 0.5f);
                sb.Disc(to, 7.5f, 7.5f, c);
                sb.TextCenter(Assets.FontNarrow(11), n.ToString(), to, Color.Black, false);
                prev = to; n++;
            }
        }
        if (_placing is BuildingType bt2 && !_paused)
        {
            var tl = PlacementTile(bt2, MouseWorld);
            var problem = W.PlaceProblem(bt2, tl, W.Local) ?? (W.Me.CanPay(Defs.Buildings[bt2].Cost) ? null : "Recursos insuficientes");
            if (problem != null)
                sb.TextCenter(Assets.Font(13), problem, new Vector2((tl.X + Defs.Buildings[bt2].Size / 2f) * World.T, tl.Y * World.T - 34), Pal.Red);
        }
        if (_ping is Vector2 pp && _pingTime > 0)
        {
            float k = (_pingTime % 1f);
            sb.Ellipse(pp, 40 + k * 60, 20 + k * 30, Pal.Red * (1 - k));
        }
        sb.End();

        if (_dragging && _dragStart is Vector2 ds)
        {
            sb.Begin();
            var a = ds; var b = Input.MousePos.ToVector2();
            var min = Vector2.Min(a, b); var size = Vector2.Max(a, b) - min;
            sb.Fill(min, size, Pal.GreenLight * 0.12f);
            sb.BorderF(min, size, Pal.GreenLight, 1.5f);
            sb.End();
        }

        sb.Begin(transformMatrix: Game.UiMatrix, samplerState: SamplerState.LinearClamp);
        DrawTopBar(sb);
        DrawObjectives(sb);
        DrawBottom(sb);
        Gui.DrawAll(sb, _buttons.Where(b => !_paused).ToList());
        DrawMessages(sb);
        DrawCapitalFlash(sb);
        DrawTutorialArrows(sb);
        if (IntroActive) DrawIntro(sb);

        var hover = _buttons.FirstOrDefault(b => b.Hover && b.TooltipTitle != null);
        if (hover != null && !_paused) Gui.Tooltip(sb, hover.TooltipTitle, hover.Tooltip, new Vector2(hover.Rect.Center.X, hover.Rect.Y), Ui, hover.Warn);

        if (_placing != null) sb.TextCenter(Assets.FontNarrow(16), "Clique para construir  •  Shift: várias  •  Botão direito/Esc: cancelar", new Vector2(Ui.X / 2, _bottom.Y - 18), Color.White);
        if (_attackMoveMode) sb.TextCenter(Assets.FontNarrow(16), "Clique no destino do ataque  •  Botão direito/Esc: cancelar", new Vector2(Ui.X / 2, _bottom.Y - 18), Pal.Gold);

        DrawChat(sb);
        if (_net != null && (_net.Reconnecting || _net.WaitTime > 0.4f) && _netError == null && W.Winner < 0)
        {
            // ordens do adversário ainda não chegaram, ou a conexão caiu: a simulação espera por ele
            var wr = new Rectangle((int)Ui.X / 2 - 260, _top.Bottom + 56, 520, 34);
            sb.Panel(wr, Pal.PanelBg * 0.92f, _net.Reconnecting ? Pal.Red : Pal.Gold, 2);
            string msg = _net.Reconnecting
                ? $"Conexão perdida — reconectando… {Math.Max(0, Lockstep.ReconnectWindow - _net.ReconnectTime):0}s"
                : $"Aguardando {_cfg.Names[W.Foe]}… ({_net.WaitTime:0}s)";
            sb.TextCenter(Assets.FontBold(16), msg, wr.Center.ToVector2(), _net.Reconnecting ? new Color(255, 160, 140) : Pal.Gold);
        }
        if (_help) DrawHelp(sb);
        if (_paused) DrawPause(sb);
        if (_netError != null)
        {
            sb.Fill(new Rectangle(0, 0, (int)Ui.X, (int)Ui.Y), Color.Black * 0.65f);
            var er = new Rectangle((int)Ui.X / 2 - 300, (int)Ui.Y / 2 - 110, 600, 220);
            sb.Panel(er, Pal.Paper, Pal.Red, 3);
            sb.TextCenter(Assets.Font(26), "Partida encerrada", new Vector2(er.Center.X, er.Y + 34), Pal.Red, false);
            float ly = er.Y + 70;
            foreach (var line in Gfx.Wrap(Assets.FontNarrow(17), _netError, er.Width - 60)) { sb.TextCenter(Assets.FontNarrow(17), line, new Vector2(er.Center.X, ly), Pal.Ink, false); ly += 24; }
            Gui.DrawAll(sb, _buttons);
        }
        if (W.Winner >= 0)
        {
            float a = Math.Min(1, _endTimer / 2.5f);
            sb.Fill(new Rectangle(0, 0, (int)Ui.X, (int)Ui.Y), Color.Black * (a * 0.6f));
            sb.TextCenter(Assets.Font(64), W.Winner == W.Local ? "VITÓRIA!" : "DERROTA", Ui / 2, (W.Winner == W.Local ? Pal.Gold : Pal.Red) * a);
        }
        sb.End();
    }

    void DrawTopBar(SpriteBatch sb)
    {
        sb.Fill(_top, Pal.PanelBg * 0.96f);
        sb.Fill(new Rectangle(0, _top.Bottom - 2, _top.Width, 2), Pal.Gold * 0.7f);
        var p = W.Me;
        var num = Assets.FontBold(20);
        var lab = Assets.FontBold(10);
        float x = 10;
        foreach (Res r in Enum.GetValues<Res>())
        {
            sb.SpriteFit(Defs.ResIcon(r), new Rectangle((int)x, 6, 28, 28), Color.White, 1.2f);
            sb.Text(num, p.Get(r).ToString(), new Vector2(x + 33, 2), Color.White);
            sb.Text(lab, Defs.ResName(r).ToUpperInvariant(), new Vector2(x + 34, 25), Defs.ResColor(r), false);
            x += 104;
        }
        sb.SpriteFit(Assets.Structure(p.Faction.BuildingSprite(BuildingType.Casa)), new Rectangle((int)x, 6, 28, 28), Color.White, 1f);
        bool full = p.Pop >= p.PopCap;
        bool blink = full && W.Buildings.Any(b => b.Team == W.Local && b.PopBlocked) && (int)(_realTime * 3) % 2 == 0;
        sb.Text(num, $"{p.Pop}/{p.PopCap}", new Vector2(x + 33, 2), full ? (blink ? Color.White : new Color(255, 120, 100)) : Color.White);
        sb.Text(lab, full ? "POPULAÇÃO MÁXIMA" : "POPULAÇÃO", new Vector2(x + 34, 25), full ? new Color(255, 120, 100) : Color.White * 0.6f, false);
        x += 128;

        // painel das bandeiras: dono, bônus e captura em andamento
        float fw = 196;
        float fx = Math.Max(x + 12, _top.Width / 2f - fw);
        for (int i = 0; i < W.Flags.Count; i++)
        {
            var fl = W.Flags[i];
            var box = new Rectangle((int)(fx + i * (fw + 6)), 3, (int)fw, 34);
            var ownerCol = fl.Owner >= 0 ? W.Players[fl.Owner].Color : new Color(235, 235, 225);
            sb.Fill(box, Pal.Slate);
            sb.Border(box, fl.Owner == 0 ? Pal.GreenLight * 0.8f : fl.Owner == 1 ? Pal.Red * 0.8f : Color.White * 0.15f, 1);
            // bandeirinha (forma muda com o dono: cheia = sua, recortada = inimiga)
            var pole = new Vector2(box.X + 10, box.Y + 5);
            sb.Line(pole, pole + new Vector2(0, 24), new Color(210, 190, 150), 2);
            FactionId? ownerFac = fl.Owner >= 0 ? W.Players[fl.Owner].Faction.Id : null;
            FlagArt.Draw(sb, ownerFac, new Rectangle((int)pole.X + 1, (int)pole.Y, 18, 12));
            if (fl.Owner >= 0) sb.Fill(new Rectangle((int)pole.X + 1, (int)pole.Y + 13, 18, 2), ownerCol);
            sb.Text(Assets.FontBold(13), $"{(i == 0 ? "Norte" : "Sul")} · {fl.Short}", new Vector2(box.X + 32, box.Y + 1), Color.White, false);
            string state; Color sc;
            if (fl.Contested) { state = "EM DISPUTA"; sc = (int)(_realTime * 4) % 2 == 0 ? Pal.Gold : Color.White; }
            else if (fl.Capturer >= 0 && fl.Capturer != fl.Owner) { state = fl.Capturer == 0 ? "Capturando..." : "Sendo tomada!"; sc = fl.Capturer == 0 ? Pal.GreenLight : new Color(255, 120, 100); }
            else if (fl.Owner == 0) { state = $"Sua: {fl.BonusText}"; sc = Pal.GreenLight; }
            else if (fl.Owner == 1) { state = $"Inimiga: {fl.BonusText}"; sc = new Color(255, 120, 100); }
            else { state = $"Neutra: {fl.BonusText}"; sc = Color.White * 0.7f; }
            sb.Text(Assets.FontNarrow(12), state, new Vector2(box.X + 32, box.Y + 17), sc, false);
            // barra de captura (-100..100)
            float prog = fl.Progress / 100f;
            var bar = new Rectangle(box.X + 2, box.Bottom - 4, box.Width - 4, 3);
            sb.Fill(bar, Color.Black * 0.4f);
            int half = bar.Width / 2;
            if (prog > 0) sb.Fill(new Rectangle(bar.X + half, bar.Y, (int)(half * prog), 3), W.Me.Color);
            else if (prog < 0) sb.Fill(new Rectangle(bar.X + half - (int)(half * -prog), bar.Y, (int)(half * -prog), 3), W.Enemy.Color);
        }

        // faixa de domínio (contagem para a vitória/derrota)
        int holder = W.HoldTimer[0] > 0 ? 0 : W.HoldTimer[1] > 0 ? 1 : -1;
        if (holder >= 0 && W.HoldToWin)
        {
            float left = Math.Max(0, World.HoldGoal - W.HoldTimer[holder]);
            var band = new Rectangle((int)(Ui.X / 2 - 210), _top.Bottom + 4, 420, 30);
            sb.Fill(band, (holder == 0 ? Pal.DeepGreen : new Color(90, 30, 24)) * 0.92f);
            sb.Border(band, holder == 0 ? Pal.Gold : Pal.Red, 1);
            string txt = holder == 0 ? $"DOMÍNIO: vitória em {left:0}s — segure as duas bandeiras" : $"O INIMIGO DOMINA: derrota em {left:0}s — retome uma bandeira!";
            sb.TextCenter(Assets.FontBold(14), txt, new Vector2(band.Center.X, band.Y + 13), Color.White, false);
            sb.Bar(new Rectangle(band.X + 2, band.Bottom - 4, band.Width - 4, 3), W.HoldTimer[holder] / World.HoldGoal, holder == 0 ? Pal.Gold : Pal.Red, Color.Black * 0.4f);
        }
    }

    void DrawObjectives(SpriteBatch sb)
    {
        var r = _obj;
        sb.Fill(r, Pal.PanelBg * 0.85f);
        sb.Border(r, Pal.Gold * 0.5f, 1);
        if (_tutorial != null)
        {
            int idx = Math.Min(_tutorial.Index, Tutorial.Steps.Length - 1);
            var step = _tutorial.Current;
            sb.Text(Assets.FontBold(12), $"TUTORIAL  •  PASSO {idx + 1}/{Tutorial.Steps.Length}", new Vector2(r.X + 10, r.Y + 4), Pal.Gold, false);
            string prog = _tutorial.Progress();
            float pulse = _tutorial.StepAnim < 1.5f ? 0.6f + 0.4f * MathF.Sin(_tutorial.StepAnim * 12) : 1f;
            sb.Text(Assets.Font(15), step.Goal, new Vector2(r.X + 10, r.Y + 20), Color.White * pulse);
            if (prog.Length > 0) sb.Text(Assets.FontBold(13), prog, new Vector2(r.X + 10, r.Y + 40), Pal.Sand, false);
            if (!_objCollapsed)
            {
                var lines = Gfx.Wrap(Assets.FontNarrow(15), step.Hint, r.Width - 20);
                for (int i = 0; i < Math.Min(4, lines.Count); i++)
                    sb.Text(Assets.FontNarrow(15), lines[i], new Vector2(r.X + 10, r.Y + (prog.Length > 0 ? 60 : 44) + i * 18), Pal.Paper * 0.9f, false);
            }
            for (int i = 0; i < Tutorial.Steps.Length; i++)
                sb.Fill(new Rectangle(r.Right - 12 - (Tutorial.Steps.Length - i) * 12, r.Y + 7, 9, 5), i < _tutorial.Index ? Pal.Gold : i == _tutorial.Index ? Color.White : Color.White * 0.25f);
        }
        else if (_mission != null)
        {
            var step = _mission.Current;
            int idx = Math.Min(_mission.Index, Mission.Steps.Length - 1);
            sb.Text(Assets.FontNarrow(12), $"MISSÃO  •  {Clock}  •  OBJETIVO {idx + 1}/{Mission.Steps.Length}", new Vector2(r.X + 10, r.Y + 4), Pal.Gold, false);
            string prog = _mission.Progress();
            float pulse = _mission.StepAnim < 1.5f ? 0.6f + 0.4f * MathF.Sin(_mission.StepAnim * 12) : 1f;
            sb.Text(Assets.Font(16), step.Goal + (prog.Length > 0 ? $"  ({prog})" : ""), new Vector2(r.X + 10, r.Y + 18), Color.White * pulse);
            if (!_objCollapsed)
            {
                var lines = Gfx.Wrap(Assets.FontNarrow(14), step.Hint, r.Width - 20);
                for (int i = 0; i < Math.Min(5, lines.Count); i++)
                    sb.Text(Assets.FontNarrow(14), lines[i], new Vector2(r.X + 10, r.Y + 44 + i * 17), Pal.Paper * 0.85f, false);
            }
        }
        else
        {
            sb.Text(Assets.FontNarrow(12), (_net != null ? $"OBJETIVO  •  {Clock}  •  VS {_cfg.Names[W.Foe].ToUpperInvariant()}  •  {_net.Peer.PingMs} ms" : $"OBJETIVO  •  {Clock}  •  IA: {_ai?.State.ToString().ToUpperInvariant()}"), new Vector2(r.X + 10, r.Y + 4), Pal.Gold, false);
            sb.Text(Assets.FontNarrow(15), "Domine as 2 bandeiras por 90s", new Vector2(r.X + 10, r.Y + 18), Color.White);
            if (!_objCollapsed) sb.Text(Assets.FontNarrow(14), "ou destrua o Centro de Comando inimigo.", new Vector2(r.X + 10, r.Y + 36), Pal.Paper * 0.85f, false);
        }
    }

    void DrawBottom(SpriteBatch sb)
    {
        sb.Fill(_bottom, Pal.PanelBg * 0.96f);
        sb.Fill(new Rectangle(0, _bottom.Y, _bottom.Width, 2), Pal.Gold * 0.6f);

        // minimapa
        sb.Fill(new Rectangle(_mini.X - 2, _mini.Y - 2, _mini.Width + 4, _mini.Height + 4), Color.Black);
        sb.Draw(_miniTerrain, _mini, Color.White);
        foreach (var r in W.Resources)
        {
            if (!W.IsExplored(r)) continue;
            var m = WorldToMini(r.Pos);
            sb.Fill(new Rectangle((int)m.X, (int)m.Y, 1, 1), Defs.ResColor(r.Kind) * 0.8f);
        }
        foreach (var b in W.Buildings)
        {
            if (b.Team != W.Local && !W.IsExplored(b)) continue;
            var m = WorldToMini(new Vector2(b.WorldRect.X, b.WorldRect.Y));
            var s = b.Size * _mini.Width / (float)W.W;
            sb.Fill(m, new Vector2(s, s), b.Owner.Color);
            sb.BorderF(m, new Vector2(s, s), Color.Black * 0.6f, 1);
        }
        sb.Draw(_miniFog, _mini, Color.White);
        foreach (var u in W.Units)
        {
            if (u.Team != W.Local && !W.CanSee(W.Local, u)) continue;
            var m = WorldToMini(u.Pos);
            sb.Fill(m - Vector2.One * 1.5f, new Vector2(3, 3), _selection.Contains(u) ? Color.White : u.Owner.Color);
        }
        foreach (var fl in W.Flags)
        {
            var m = WorldToMini(fl.Pos);
            var col = fl.Owner >= 0 ? W.Players[fl.Owner].Color : Color.White;
            sb.Fill(m - new Vector2(3, 3), new Vector2(7, 7), Color.Black);
            sb.Fill(m - new Vector2(2, 2), new Vector2(5, 5), col);
        }
        if (_ping is Vector2 pp && _pingTime > 0)
        {
            var m = WorldToMini(pp);
            float k = _pingTime % 1f;
            sb.Ellipse(m, 6 + k * 10, 6 + k * 10, Pal.Red);
        }
        var v = _cam.ViewRect;
        var a = WorldToMini(new Vector2(v.X, v.Y));
        var b2 = WorldToMini(new Vector2(v.Right, v.Bottom));
        var clipA = Vector2.Max(a, new Vector2(_mini.X, _mini.Y));
        var clipB = Vector2.Min(b2, new Vector2(_mini.Right, _mini.Bottom));
        sb.BorderF(clipA, clipB - clipA, Color.White, 1.2f);

        // painel de informações
        sb.Fill(_info, Pal.PanelBg2);
        sb.Border(_info, Pal.PanelLine * 0.4f, 1);
        DrawInfo(sb);

        sb.Fill(_cmd, Pal.PanelBg2 * 0.5f);
        DrawCommander(sb);
    }

    /// <summary>Cartão do comandante: retrato, nome, poder e recarga (ícone dessaturado + contador).</summary>
    void DrawCommander(SpriteBatch sb)
    {
        var p = W.Me;
        var f = p.Faction;
        var r = _power;
        bool hover = r.Contains(Input.UiMousePt);
        sb.Fill(r, p.PowerActive ? Color.Lerp(Pal.Slate, Pal.Gold, 0.35f) : hover && p.PowerReady ? Color.Lerp(Pal.Slate, Color.White, 0.12f) : Pal.Slate);
        sb.Border(r, p.PowerReady ? Pal.Gold : Pal.PanelLine * 0.4f, p.PowerReady ? 2 : 1);
        sb.Text(Assets.FontNarrow(11), "COMANDANTE", new Vector2(r.X + 8, r.Y + 6), Pal.Sand, false);
        FlagArt.Draw(sb, f.Id, new Rectangle(r.Right - 44, r.Y + 6, 18, 12));
        sb.Text(Assets.FontNarrow(11), "C", new Vector2(r.Right - 16, r.Y + 6), Color.White * 0.5f, false);

        var portrait = new Rectangle(r.X + 8, r.Y + 22, 44, 48);
        sb.Fill(portrait, f.Color * 0.35f);
        sb.Border(portrait, f.Color, 1);
        sb.SpriteFit(Defs.UnitSprite(Defs.Units[UnitType.GuardiaoDasGerais], p.ColorSet, p.Faction.Id), portrait, p.PowerReady || p.PowerActive ? Color.White : Color.White * 0.4f, 1.1f);
        var name = Gfx.Wrap(Assets.FontNarrow(12), f.Commander, r.Width - 66);
        for (int i = 0; i < Math.Min(2, name.Count); i++) sb.Text(Assets.FontNarrow(12), name[i], new Vector2(r.X + 58, r.Y + 24 + i * 15), Color.White, false);

        var power = Gfx.Wrap(Assets.Font(13), f.Power, r.Width - 16);
        for (int i = 0; i < Math.Min(2, power.Count); i++) sb.Text(Assets.Font(13), power[i], new Vector2(r.X + 8, r.Y + 78 + i * 17), p.PowerReady ? Pal.Gold : Color.White * 0.6f);

        string status = p.PowerActive ? $"ATIVO  {Math.Ceiling(p.PowerTime)}s" : p.PowerReady ? "PRONTO" : $"recarga {Math.Ceiling(p.PowerCd)}s";
        sb.Text(Assets.FontNarrow(12), status, new Vector2(r.X + 8, r.Bottom - 30), p.PowerActive ? Color.White : p.PowerReady ? Pal.GreenLight : Color.White * 0.6f, false);
        float frac = p.PowerActive ? p.PowerTime / f.PowerDur : p.PowerReady ? 1 : 1 - Math.Max(0, p.PowerCd) / f.PowerCd;
        sb.Bar(new Rectangle(r.X + 8, r.Bottom - 12, r.Width - 16, 5), frac, p.PowerActive ? Color.White : Pal.Gold, Color.Black * 0.4f);
    }

    void DrawInfo(SpriteBatch sb)
    {
        var r = _info;
        var units = SelUnits;
        var p = W.Me;
        var fBig = Assets.Font(18);
        var fS = Assets.FontNarrow(14);

        if (_selection.Count == 0)
        {
            sb.Text(Assets.Font(16), $"{p.Faction.Name} — {p.Faction.Title}", new Vector2(r.X + 12, r.Y + 10), p.Color);
            var lines = Gfx.Wrap(fS, "Selecione unidades com o botão esquerdo (ou arraste uma caixa). Dê ordens com o botão direito: mover, coletar, construir ou atacar.", r.Width - 24);
            for (int i = 0; i < lines.Count; i++) sb.Text(fS, lines[i], new Vector2(r.X + 12, r.Y + 38 + i * 18), Pal.Paper * 0.8f, false);
            return;
        }

        if (units.Count > 1)
        {
            sb.Text(fBig, $"{units.Count} unidades", new Vector2(r.X + 12, r.Y + 10), Color.White);
            var counts = units.GroupBy(u => u.Def.Name).Select(g => $"{g.Count()} {g.Key}");
            { var txt = string.Join("  •  ", counts); var fnt = Assets.FontNarrow(13); while (txt.Length > 4 && fnt.MeasureString(txt).X > r.Width - 160) txt = txt[..^5] + "…"; sb.Text(fnt, txt, new Vector2(r.X + 150, r.Y + 14), Pal.Paper * 0.7f, false); }
            return;
        }

        var e = _selection.First();
        var portrait = new Rectangle(r.X + 10, r.Y + 10, 72, 72);
        sb.Fill(portrait, Pal.PaperDark);
        sb.Border(portrait, Color.Black * 0.4f, 1);

        switch (e)
        {
            case Unit u:
            {
                sb.SpriteFit(Defs.UnitSprite(u.Def, u.Owner.ColorSet, u.Owner.Faction.Id), portrait, Color.White, 1.6f);
                sb.Text(fBig, u.Def.Name, new Vector2(r.X + 92, r.Y + 8), Color.White);
                FlagArt.Draw(sb, u.Owner.Faction.Id, new Rectangle(r.X + 92, r.Y + 34, 18, 12));
                sb.Text(Assets.FontNarrow(13), u.Team == W.Local ? $"{u.Owner.Faction.Name} (você)" : $"{u.Owner.Faction.Name} (inimigo)", new Vector2(r.X + 116, r.Y + 32), u.Owner.Color, false);
                sb.Bar(new Rectangle(r.X + 92, r.Y + 52, 180, 8), u.Hp / u.MaxHp, u.Team == W.Local ? Pal.GreenLight : Pal.Red, Color.Black * 0.5f);
                sb.Text(Assets.FontNarrow(13), $"{Math.Ceiling(u.Hp)}/{u.MaxHp}", new Vector2(r.X + 280, r.Y + 48), Color.White, false);
                string alcance = u.Def.IsRanged ? "à distância" : "corpo a corpo";
                sb.Text(fS, $"Dano {u.Def.Dmg:0}  •  {alcance}  •  Velocidade {u.Def.Speed:0}", new Vector2(r.X + 12, r.Y + 88), Pal.Paper * 0.85f, false);
                sb.Text(fS, u.Def.Strong, new Vector2(r.X + 12, r.Y + 106), Pal.Gold, false);
                if (u.Team == W.Local) sb.Text(fS, u.StatusText(), new Vector2(r.X + 12, r.Y + 124), Color.White, false);
                break;
            }
            case Building b:
            {
                sb.SpriteFit(b.Texture, portrait, Color.White, 1.2f);
                sb.Text(fBig, b.Def.Name, new Vector2(r.X + 92, r.Y + 8), Color.White);
                FlagArt.Draw(sb, b.Owner.Faction.Id, new Rectangle(r.X + 92, r.Y + 34, 18, 12));
                sb.Text(Assets.FontNarrow(13), b.Team == W.Local ? $"{b.Owner.Faction.Name} (você)" : $"{b.Owner.Faction.Name} (inimigo)", new Vector2(r.X + 116, r.Y + 32), b.Owner.Color, false);
                sb.Bar(new Rectangle(r.X + 92, r.Y + 52, 180, 8), b.Hp / b.MaxHp, b.Team == W.Local ? Pal.GreenLight : Pal.Red, Color.Black * 0.5f);
                sb.Text(Assets.FontNarrow(13), $"{Math.Ceiling(b.Hp)}/{Math.Ceiling(b.MaxHp)}", new Vector2(r.X + 280, r.Y + 48), Color.White, false);
                if (!b.Done)
                {
                    sb.Text(fS, $"Em construção: {b.Progress * 100:0}%", new Vector2(r.X + 92, r.Y + 64), Pal.Gold, false);
                    sb.Text(fS, "Selecione trabalhadores e clique com o botão direito aqui para ajudar.", new Vector2(r.X + 12, r.Y + 92), Pal.Paper * 0.8f, false);
                }
                else if (b.Team == W.Local && b.Queue.Count > 0)
                {
                    var def = Defs.Units[b.Queue[0]];
                    sb.Text(fS, $"Treinando {def.Name}: {b.ProdTimer / def.Time * 100:0}%", new Vector2(r.X + 92, r.Y + 64), Color.White, false);
                    if (b.PopBlocked) sb.Text(fS, "População máxima! Construa Casas.", new Vector2(r.X + 12, r.Y + 84), Pal.Red, false);
                    sb.Text(Assets.FontNarrow(12), "FILA", new Vector2(r.X + 12, r.Bottom - 40), Pal.Paper * 0.6f, false);
                }
                else
                {
                    var lines = Gfx.Wrap(fS, b.Def.Desc + (b.Team == W.Local && b.TrainOptions.Any() ? " Botão direito no mapa define o ponto de encontro." : ""), r.Width - 24);
                    for (int i = 0; i < Math.Min(3, lines.Count); i++) sb.Text(fS, lines[i], new Vector2(r.X + 12, r.Y + 90 + i * 18), Pal.Paper * 0.8f, false);
                }
                break;
            }
            case ResourceNode n:
            {
                sb.SpriteFit(n.Texture, portrait, Color.White, 1.6f);
                sb.Text(fBig, n.DisplayName, new Vector2(r.X + 92, r.Y + 8), Color.White);
                sb.Text(fS, $"Restam {n.Amount} de {Defs.ResName(n.Kind)}", new Vector2(r.X + 92, r.Y + 36), Defs.ResColor(n.Kind), false);
                var lines = Gfx.Wrap(fS, "Selecione trabalhadores e clique com o botão direito para coletar.", r.Width - 24);
                for (int i = 0; i < lines.Count; i++) sb.Text(fS, lines[i], new Vector2(r.X + 12, r.Y + 92 + i * 18), Pal.Paper * 0.8f, false);
                break;
            }
        }
    }

    void DrawMessages(SpriteBatch sb)
    {
        // avisos discretos: fonte legível, faixa estreita, abaixo da faixa de domínio
        float y = _top.Bottom + (W.HoldTimer[0] > 0 || W.HoldTimer[1] > 0 ? 42 : 10);
        var f = Assets.FontBold(16);
        foreach (var (text, color, time) in _msgs)
        {
            float a = Math.Min(1, time * 1.5f);
            var size = f.MeasureString(text);
            var pos = new Vector2(MathF.Round(Ui.X / 2 - size.X / 2), y);
            var box = new Rectangle((int)pos.X - 12, (int)y - 2, (int)size.X + 24, (int)size.Y + 4);
            sb.Fill(box, Pal.PanelBg * (0.82f * a));
            sb.Fill(new Rectangle(box.X, box.Y, 3, box.Height), color * a);
            sb.Text(f, text, pos, color * a);
            y += size.Y + 7;
        }
    }

    /// <summary>Setas na borda da tela apontando para o destaque do tutorial quando ele está fora de vista.</summary>
    void DrawTutorialArrows(SpriteBatch sb)
    {
        if (_tutorial == null) return;
        var center = new Vector2(Ui.X / 2, (_top.Bottom + _bottom.Y) / 2f);
        foreach (var hp in _tutorial.WorldHighlights().Take(1))
        {
            var s = _cam.WorldToScreen(hp) / Game1.UiScaleEff;
            if (s.X > 20 && s.X < Ui.X - 20 && s.Y > _top.Bottom + 20 && s.Y < _bottom.Y - 20) continue;
            var d = s - center; d.Normalize();
            float tx = d.X != 0 ? ((d.X > 0 ? Ui.X - 40 : 40) - center.X) / d.X : float.MaxValue;
            float ty = d.Y != 0 ? ((d.Y > 0 ? _bottom.Y - 30 : _top.Bottom + 30) - center.Y) / d.Y : float.MaxValue;
            var p = center + d * Math.Min(Math.Abs(tx), Math.Abs(ty));
            var perp = new Vector2(-d.Y, d.X);
            float pulse = 1 + 0.15f * MathF.Sin(_realTime * 6);
            sb.Line(p - d * 18 * pulse + perp * 12, p, Pal.Gold, 5);
            sb.Line(p - d * 18 * pulse - perp * 12, p, Pal.Gold, 5);
            sb.Line(p - d * 34, p - d * 6, Pal.Gold, 4);
        }
    }

    /// <summary>Apresentação de ~3 s: bandeira, facção, comandante, mapa e objetivo. Pula com clique ou tecla.</summary>
    void DrawIntro(SpriteBatch sb)
    {
        var p = W.Me;
        var f = p.Faction;
        float a = Math.Min(1, _introT / 0.3f) * Math.Min(1, (IntroDuration - _introT) / 0.4f);
        a = Math.Clamp(a, 0, 1);
        sb.Fill(new Rectangle(0, 0, (int)Ui.X, (int)Ui.Y), Pal.DeepGreen * (0.55f * a));
        var c = Ui / 2;
        var band = new Rectangle(0, (int)c.Y - 130, (int)Ui.X, 260);
        sb.Fill(band, Pal.PanelBg * (0.92f * a));
        sb.Fill(new Rectangle(0, band.Y, band.Width, 2), Pal.Gold * a);
        sb.Fill(new Rectangle(0, band.Bottom - 2, band.Width, 2), Pal.Gold * a);
        float slide = (1 - Math.Min(1, _introT / 0.5f)) * 60;
        var white = Color.White * a;
        FlagArt.DrawWaving(sb, f.Id, new Vector2(c.X - 360 - slide, c.Y - 70), 150, 100, _realTime, 0, white);
        float tx = c.X - 180 + slide;
        sb.Text(Assets.Font(40), f.Name.ToUpperInvariant(), new Vector2(tx, c.Y - 96), Pal.Gold * a);
        sb.Text(Assets.FontBold(18), f.Title, new Vector2(tx, c.Y - 48), white);
        sb.Text(Assets.FontNarrow(17), $"Comandante: {f.Commander}  ·  poder {f.Power}", new Vector2(tx, c.Y - 20), white);
        sb.Text(Assets.FontNarrow(17), "Mapa: Encontro das Fronteiras", new Vector2(tx, c.Y + 6), white);
        string goal = _tutorial != null ? "Tutorial: siga os 6 passos com o destaque dourado"
                    : _mission != null ? $"Missão: {Mission.Steps[0].Goal}"
                    : "Domine as duas bandeiras por 90 s ou destrua o Centro de Comando inimigo";
        sb.Text(Assets.FontBold(17), "OBJETIVO: " + goal, new Vector2(tx, c.Y + 38), Pal.Sand * a);
        sb.TextCenter(Assets.FontNarrow(14), "Clique ou aperte qualquer tecla para começar", new Vector2(c.X, band.Bottom - 18), Color.White * (0.6f * a));
        sb.Bar(new Rectangle((int)c.X - 120, band.Bottom - 8, 240, 3), _introT / IntroDuration, Pal.Gold * a, Color.Black * (0.4f * a));
    }

    /// <summary>Vinheta vermelha nas bordas quando a capital é atacada.</summary>
    void DrawCapitalFlash(SpriteBatch sb)
    {
        if (_capitalFlash <= 0) return;
        float a = Math.Min(1, _capitalFlash) * (0.6f + 0.4f * MathF.Sin(_realTime * 12));
        var red = new Color(200, 30, 20);
        int w = (int)Ui.X, h = (int)Ui.Y;
        for (int i = 0; i < 8; i++)
        {
            float k = a * 0.35f * (1 - i / 8f);
            int t = 6;
            sb.Fill(new Rectangle(0, i * t, w, t), red * k);
            sb.Fill(new Rectangle(0, h - (i + 1) * t, w, t), red * k);
            sb.Fill(new Rectangle(i * t, 0, t, h), red * k);
            sb.Fill(new Rectangle(w - (i + 1) * t, 0, t, h), red * k);
        }
    }

    void DrawHelp(SpriteBatch sb)
    {
        var r = new Rectangle((int)(Ui.X / 2 - 520), 70, 1040, 470);
        sb.Panel(r, Pal.Paper * 0.97f, Pal.Ink, 3);
        sb.TextCenter(Assets.Font(24), "Ajuda  (F1 para fechar)", new Vector2(r.Center.X, r.Y + 26), Pal.Ink, false);
        float y = r.Y + 56;
        foreach (var l in MenuScreen.HelpLines)
        {
            bool head = l.Length > 0 && l == l.ToUpperInvariant() && !l.Contains('•');
            sb.Text(head ? Assets.Font(16) : Assets.FontNarrow(15), l, new Vector2(r.X + 30, y), head ? Pal.Green : Pal.Ink, false);
            y += head ? 24 : 21;
        }
    }

    void DrawPause(SpriteBatch sb)
    {
        sb.Fill(new Rectangle(0, 0, (int)Ui.X, (int)Ui.Y), Color.Black * 0.55f);
        var c = Ui / 2;
        var r = new Rectangle((int)c.X - 200, (int)c.Y - 240, 400, 490);
        sb.Panel(r, Pal.PanelBg, Pal.Gold, 2);
        sb.TextCenter(Assets.Font(24), "PAUSA", new Vector2(c.X, r.Y + 30), Pal.Gold);
        int y = (int)c.Y - 180;
        var lf = Assets.FontBold(15);
        sb.TextCenter(lf, $"Escala da interface: {Game1.UiScale * 100:0}%", new Vector2(c.X, y + 77), Color.White);
        sb.TextCenter(lf, $"Volume geral: {Assets.MasterVolume * 100:0}%", new Vector2(c.X, y + 123), Color.White);
        sb.TextCenter(lf, $"Efeitos: {Assets.SfxVolume * 100:0}%", new Vector2(c.X, y + 169), Color.White);
        sb.TextCenter(lf, $"Música e ambiente: {Assets.MusicVolume * 100:0}%", new Vector2(c.X, y + 215), Color.White);
        Gui.DrawAll(sb, _buttons);
    }
}
