using GuerraDasBandeiras.Core;
using GuerraDasBandeiras.Screens;

namespace GuerraDasBandeiras;

public class Game1 : Game
{
    public static Game1 I { get; private set; }
    public static float UiScale = 1f;

    readonly string[] _args;
    public readonly GraphicsDeviceManager Gdm;
    SpriteBatch _sb;
    Screen _screen, _next;
    bool _resizing;
    Tools.ShotRunner _shot;

    public int W => GraphicsDevice.PresentationParameters.BackBufferWidth;
    public int H => GraphicsDevice.PresentationParameters.BackBufferHeight;
    /// <summary>Escala efetiva: preferência do jogador x ajuste da tela atual (menus encolhem em telas baixas).</summary>
    public static float ScreenScale = 1f;
    public static float UiScaleEff => UiScale * ScreenScale;
    public Vector2 UiSize => new Vector2(W, H) / UiScaleEff;
    public Matrix UiMatrix => Matrix.CreateScale(UiScaleEff);

    public Game1(string[] args)
    {
        I = this;
        _args = args;
        Gdm = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
            SynchronizeWithVerticalRetrace = true,
            HardwareModeSwitch = false,
        };
        IsFixedTimeStep = false;
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
        Window.Title = "Brasil: Guerra das Bandeiras";
        Window.ClientSizeChanged += OnResize;
        Window.TextInput += (_, e) => Input.OnText(e.Character);
    }

    protected override void Initialize()
    {
        // janela com ~85% da tela do monitor
        var dm = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
        Gdm.PreferredBackBufferWidth = Math.Max(1280, (int)(dm.Width * 0.85f));
        Gdm.PreferredBackBufferHeight = Math.Max(720, (int)(dm.Height * 0.85f));
        if (Gdm.PreferredBackBufferWidth > dm.Width) Gdm.PreferredBackBufferWidth = dm.Width;
        if (Gdm.PreferredBackBufferHeight > dm.Height) Gdm.PreferredBackBufferHeight = dm.Height;
        var size = Environment.GetEnvironmentVariable("GB_SIZE"); // ex.: 1366x768 (testes de resolução)
        if (size != null && size.Split('x') is [var sw, var sh])
        {
            Gdm.PreferredBackBufferWidth = int.Parse(sw);
            Gdm.PreferredBackBufferHeight = int.Parse(sh);
        }
        Gdm.ApplyChanges();
        UiScale = Math.Clamp(Gdm.PreferredBackBufferHeight / 820f, 1f, 1.6f);
        UiScale = MathF.Round(UiScale * 10) / 10f;
        base.Initialize();
    }

    void OnResize(object sender, EventArgs e)
    {
        if (_resizing || Gdm.IsFullScreen) return;
        var b = Window.ClientBounds;
        if (b.Width < 200 || b.Height < 200) return;
        _resizing = true;
        Gdm.PreferredBackBufferWidth = b.Width;
        Gdm.PreferredBackBufferHeight = b.Height;
        Gdm.ApplyChanges();
        _resizing = false;
    }

    protected override void LoadContent()
    {
        _sb = new SpriteBatch(GraphicsDevice);
        Assets.Init(GraphicsDevice);
        var balancePath = Path.Combine(Assets.Root, "balance.json");
        if (!File.Exists(balancePath)) Gameplay.Balance.Save(balancePath);
        Gameplay.Balance.Load(balancePath);
        if (_args.Length >= 2 && _args[0] == "--sheet")
        {
            Tools.SpriteSheetDump.Run(GraphicsDevice, _sb, _args[1]);
            Exit();
            return;
        }
        if (_args.Length >= 2 && _args[0] == "--chars")
        {
            Tools.SpriteSheetDump.RunChars(GraphicsDevice, _sb, _args[1]);
            Exit();
            return;
        }
        if (_args.Length >= 2 && _args[0] == "--snapshot")
        {
            Tools.BalanceSnapshot.Save(_args[1]);
            Exit();
            return;
        }
        if (_args.Length >= 2 && _args[0] == "--balance")
        {
            Tools.BalanceRunner.Run(_args[1], _args.Length >= 3 ? int.Parse(_args[2]) : 30);
            Exit();
            return;
        }
        if (_args.Length >= 2 && _args[0] == "--test")
        {
            Tools.TestRunner.Run(_args[1]);
            Exit();
            return;
        }
        _shot = new Tools.ShotRunner(_args);
        if (_shot.Active)
            _screen = _shot.CreateScreen();
        else if (_args.Length >= 1 && _args[0] == "--quick")
            _screen = new GameScreen(new Gameplay.GameConfig { Mode = Gameplay.GameMode.Skirmish });
        else
            _screen = new MenuScreen();
    }

    public void SetScreen(Screen s) => _next = s;

    protected override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        Input.Update(IsActive);
        if (_next != null) { _screen = _next; _next = null; Input.TextCapture = false; }

        if (Input.KeyPressed(Keys.F11))
        {
            Gdm.IsFullScreen = !Gdm.IsFullScreen;
            if (Gdm.IsFullScreen)
            {
                var dm = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
                Gdm.PreferredBackBufferWidth = dm.Width;
                Gdm.PreferredBackBufferHeight = dm.Height;
            }
            Gdm.ApplyChanges();
        }

        ScreenScale = _screen?.LayoutScale ?? 1f;
        _screen?.Update(Math.Min(dt, 0.1f));
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        if (_shot != null && _shot.Active)
        {
            _shot.BeforeDraw(_screen);
            if (_shot.ShouldCapture)
            {
                _shot.Capture(GraphicsDevice, sb => _screen.Draw(sb), _sb);
                Exit();
                return;
            }
        }
        GraphicsDevice.Clear(new Color(20, 30, 26));
        _screen?.Draw(_sb);
        base.Draw(gameTime);
    }

    /// <summary>Usado para gerar capturas de tela de teste (--shot).</summary>
    public Screen CurrentScreen => _screen;
}
