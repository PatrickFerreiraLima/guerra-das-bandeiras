namespace GuerraDasBandeiras.Core;

/// <summary>
/// Paleta da Bíblia de Gameplay (seção 08): verde profundo #243D32, areia #D5B980,
/// terracota #A65B3C, ouro #D5A13D, azul ardósia #283D56.
/// </summary>
public static class Pal
{
    public static readonly Color DeepGreen = new(0x24, 0x3D, 0x32);
    public static readonly Color Sand = new(0xD5, 0xB9, 0x80);
    public static readonly Color Terracotta = new(0xA6, 0x5B, 0x3C);
    public static readonly Color Slate = new(0x28, 0x3D, 0x56);

    public static readonly Color Paper = new(242, 230, 200);         // areia clara para cartões
    public static readonly Color PaperDark = Sand;
    public static readonly Color Ink = DeepGreen;
    public static readonly Color InkSoft = new(62, 84, 96);
    public static readonly Color Green = new(48, 104, 76);
    public static readonly Color GreenLight = new(110, 190, 120);
    public static readonly Color Gold = new(0xD5, 0xA1, 0x3D);
    public static readonly Color Red = new(206, 84, 60);
    public static readonly Color PanelBg = new(22, 34, 50);           // ardósia escura
    public static readonly Color PanelBg2 = Slate;
    public static readonly Color PanelLine = new(140, 160, 190);
    public static readonly Color Food = new(232, 96, 80);
    public static readonly Color Wood = new(186, 128, 70);
    public static readonly Color Ore = new(170, 190, 210);
}

public class UiButton
{
    public Rectangle Rect;
    public string Label;
    public string Sub;
    public Texture2D Icon;
    public bool Enabled = true;
    public bool Selected;
    public string Tooltip;
    public string TooltipTitle;
    public Action OnClick;
    public Action OnRightClick;
    public Keys Hotkey = Keys.None;
    public float Progress = -1;
    public Color Accent = Pal.Green;
    public int FontSize = 18;
    public bool Hidden;  // clicável, mas desenhado à parte
    public Gameplay.Cost? Cost;                 // custo mostrado com ícones de recurso
    public Func<Gameplay.Res, int> Stock;       // quanto o jogador tem (pinta de vermelho o que falta)
    public string Warn;                         // motivo de estar indisponível (aparece na dica)
    public bool Highlight;                      // destaque do tutorial
    public bool Hover => Rect.Contains(Input.UiMouse.ToPoint());
}

public static class Gui
{
    /// <summary>Para onde vão os motivos de "indisponível" quando o jogador clica mesmo assim.</summary>
    public static Action<string> WarnSink;

    /// <summary>Processa cliques/atalhos. Retorna true se algum botão consumiu a entrada.</summary>
    public static bool Handle(List<UiButton> list)
    {
        foreach (var b in list)
        {
            bool hot = b.Hotkey != Keys.None && Input.KeyPressed(b.Hotkey) && !Input.Ctrl;
            if ((b.Hover && Input.LeftPressed) || hot)
            {
                if (!b.Enabled) { Sfx.Error(); if (b.Warn != null) WarnSink?.Invoke(b.Warn); return true; }
                Sfx.Click();
                b.OnClick?.Invoke();
                return true;
            }
            if (b.Hover && Input.RightPressed && b.OnRightClick != null)
            {
                Sfx.Click();
                b.OnRightClick();
                return true;
            }
        }
        return false;
    }

    public static bool AnyHover(List<UiButton> list) => list.Any(b => b.Hover);

    public static void Draw(SpriteBatch sb, UiButton b)
    {
        var r = b.Rect;
        bool hover = b.Hover && b.Enabled;
        var fill = !b.Enabled ? new Color(70, 70, 70) : b.Selected ? b.Accent : hover ? Color.Lerp(b.Accent, Color.White, 0.18f) : Color.Lerp(b.Accent, Color.Black, 0.25f);
        sb.Fill(new Rectangle(r.X, r.Y + 3, r.Width, r.Height), Color.Black * 0.35f);
        sb.Fill(r, fill);
        sb.Fill(new Rectangle(r.X, r.Y, r.Width, 2), Color.White * (b.Enabled ? 0.25f : 0.08f));
        sb.Border(r, b.Selected ? Pal.Gold : Color.Black * 0.5f, b.Selected ? 2 : 1);

        var textColor = b.Enabled ? Color.White : new Color(160, 160, 160);
        if (b.Icon != null)
        {
            var iconBox = b.Label == null ? new Rectangle(r.X + 4, r.Y + 4, r.Width - 8, r.Height - 8)
                : new Rectangle(r.X + 4, r.Y + 3, r.Width - 8, r.Height - (b.Sub != null || b.Cost != null ? 38 : 24));
            sb.SpriteFit(b.Icon, iconBox, b.Enabled ? Color.White : Color.White * 0.45f, 2.2f);
            if (b.Label != null)
                sb.TextCenter(Assets.FontBold(14), b.Label, new Vector2(r.Center.X, r.Bottom - (b.Sub != null || b.Cost != null ? 28 : 12)), textColor);
            if (b.Cost is Gameplay.Cost cost) DrawCost(sb, cost, b.Stock, new Vector2(r.Center.X, r.Bottom - 11));
            else if (b.Sub != null)
                sb.TextCenter(Assets.FontBold(12), b.Sub, new Vector2(r.Center.X, r.Bottom - 10), b.Enabled ? Pal.Gold : textColor);
        }
        else if (b.Label != null)
        {
            if (b.Sub != null)
            {
                sb.TextCenter(Assets.Font(b.FontSize), b.Label, new Vector2(r.Center.X, r.Center.Y - 9), textColor);
                sb.TextCenter(Assets.FontNarrow(13), b.Sub, new Vector2(r.Center.X, r.Center.Y + 13), Color.White * 0.8f);
            }
            else sb.TextCenter(Assets.Font(b.FontSize), b.Label, r.Center.ToVector2(), textColor);
        }

        if (b.Highlight)
        {
            float k = 0.5f + 0.5f * MathF.Sin(Environment.TickCount / 150f);
            sb.Border(new Rectangle(r.X - 3, r.Y - 3, r.Width + 6, r.Height + 6), Pal.Gold * (0.5f + 0.5f * k), 3);
        }

        if (b.Progress >= 0)
            sb.Bar(new Rectangle(r.X + 3, r.Bottom - 6, r.Width - 6, 4), b.Progress, Pal.Gold, Color.Black * 0.5f);

        if (b.Hotkey != Keys.None && r.Width >= 60)
        {
            var key = new Rectangle(r.X + 3, r.Y + 3, 16, 15);
            sb.Fill(key, Color.Black * 0.35f);
            sb.TextCenter(Assets.FontBold(11), b.Hotkey.ToString(), key.Center.ToVector2(), Color.White * 0.85f, false);
        }
    }

    public static void DrawAll(SpriteBatch sb, List<UiButton> list)
    {
        foreach (var b in list) if (!b.Hidden) Draw(sb, b);
    }

    /// <summary>Custo com ícones: cada recurso que falta aparece em vermelho.</summary>
    public static void DrawCost(SpriteBatch sb, Gameplay.Cost cost, Func<Gameplay.Res, int> stock, Vector2 center)
    {
        var f = Assets.FontBold(13);
        var parts = new List<(Gameplay.Res r, int v)>();
        foreach (Gameplay.Res res in Enum.GetValues<Gameplay.Res>()) if (cost[res] > 0) parts.Add((res, cost[res]));
        float total = parts.Sum(p => 14 + 2 + f.MeasureString(p.v.ToString()).X) + (parts.Count - 1) * 6;
        float x = center.X - total / 2;
        foreach (var (res, v) in parts)
        {
            sb.SpriteFit(Gameplay.Defs.ResIcon(res), new Rectangle((int)x, (int)center.Y - 7, 14, 14), Color.White, 1f);
            x += 16;
            bool ok = stock == null || stock(res) >= v;
            sb.Text(f, v.ToString(), new Vector2(x, center.Y - 8), ok ? Color.White : new Color(255, 120, 100));
            x += f.MeasureString(v.ToString()).X + 6;
        }
    }

    public static void Tooltip(SpriteBatch sb, string title, string body, Vector2 anchor, Vector2 uiSize, string warn = null)
    {
        var ft = Assets.Font(16);
        var fb = Assets.FontNarrow(15);
        float w = 320;
        var lines = Gfx.Wrap(fb, body ?? "", w - 20);
        var warnLines = warn == null ? new List<string>() : Gfx.Wrap(Assets.FontBold(14), warn, w - 20);
        float h = 36 + lines.Count * 19 + warnLines.Count * 18 + (warnLines.Count > 0 ? 6 : 0);
        var pos = new Vector2(Math.Clamp(anchor.X - w / 2, 6, uiSize.X - w - 6), anchor.Y - h - 8);
        var r = new Rectangle((int)pos.X, (int)pos.Y, (int)w, (int)h);
        sb.Panel(r, Pal.Paper, Pal.Ink);
        sb.Text(ft, title, pos + new Vector2(10, 8), Pal.Ink, false);
        for (int i = 0; i < lines.Count; i++)
            sb.Text(fb, lines[i], pos + new Vector2(10, 32 + i * 19), Pal.Ink, false);
        for (int i = 0; i < warnLines.Count; i++)
            sb.Text(Assets.FontBold(14), warnLines[i], pos + new Vector2(10, 38 + lines.Count * 19 + i * 18), Pal.Terracotta, false);
    }
}
