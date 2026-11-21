namespace GuerraDasBandeiras.Core;

/// <summary>Atalhos de desenho: retângulos, linhas, elipses, texto com sombra, barras.</summary>
public static class Gfx
{
    public static void Fill(this SpriteBatch sb, Rectangle r, Color c) => sb.Draw(Assets.Pixel, r, c);

    public static void Fill(this SpriteBatch sb, Vector2 pos, Vector2 size, Color c) =>
        sb.Draw(Assets.Pixel, pos, null, c, 0f, Vector2.Zero, size, SpriteEffects.None, 0f);

    public static void Border(this SpriteBatch sb, Rectangle r, Color c, int t = 1)
    {
        sb.Fill(new Rectangle(r.X, r.Y, r.Width, t), c);
        sb.Fill(new Rectangle(r.X, r.Bottom - t, r.Width, t), c);
        sb.Fill(new Rectangle(r.X, r.Y + t, t, r.Height - 2 * t), c);
        sb.Fill(new Rectangle(r.Right - t, r.Y + t, t, r.Height - 2 * t), c);
    }

    public static void BorderF(this SpriteBatch sb, Vector2 pos, Vector2 size, Color c, float t)
    {
        sb.Fill(pos, new Vector2(size.X, t), c);
        sb.Fill(pos + new Vector2(0, size.Y - t), new Vector2(size.X, t), c);
        sb.Fill(pos, new Vector2(t, size.Y), c);
        sb.Fill(pos + new Vector2(size.X - t, 0), new Vector2(t, size.Y), c);
    }

    public static void Line(this SpriteBatch sb, Vector2 a, Vector2 b, Color c, float thick = 1f)
    {
        var d = b - a;
        float len = d.Length();
        if (len < 0.01f) return;
        sb.Draw(Assets.Pixel, a, null, c, MathF.Atan2(d.Y, d.X), new Vector2(0, 0.5f), new Vector2(len, thick), SpriteEffects.None, 0f);
    }

    public static void Disc(this SpriteBatch sb, Vector2 center, float rx, float ry, Color c) =>
        sb.Draw(Assets.Circle, center, null, c, 0f, new Vector2(64, 64), new Vector2(rx / 64f, ry / 64f), SpriteEffects.None, 0f);

    public static void Ellipse(this SpriteBatch sb, Vector2 center, float rx, float ry, Color c) =>
        sb.Draw(Assets.Ring, center, null, c, 0f, new Vector2(64, 64), new Vector2(rx / 64f, ry / 64f), SpriteEffects.None, 0f);

    public static void Text(this SpriteBatch sb, SpriteFontBase f, string s, Vector2 p, Color c, bool shadow = true)
    {
        if (string.IsNullOrEmpty(s)) return;
        if (shadow) sb.DrawString(f, s, p + new Vector2(1, 2), Color.Black * (0.55f * c.A / 255f));
        sb.DrawString(f, s, p, c);
    }

    public static void TextCenter(this SpriteBatch sb, SpriteFontBase f, string s, Vector2 center, Color c, bool shadow = true)
    {
        var size = f.MeasureString(s);
        sb.Text(f, s, new Vector2(MathF.Round(center.X - size.X / 2), MathF.Round(center.Y - size.Y / 2)), c, shadow);
    }

    public static void TextRight(this SpriteBatch sb, SpriteFontBase f, string s, Vector2 rightTop, Color c, bool shadow = true)
    {
        var size = f.MeasureString(s);
        sb.Text(f, s, new Vector2(MathF.Round(rightTop.X - size.X), rightTop.Y), c, shadow);
    }

    public static void Bar(this SpriteBatch sb, Rectangle r, float frac, Color fg, Color bg)
    {
        sb.Fill(r, bg);
        sb.Fill(new Rectangle(r.X, r.Y, (int)(r.Width * Math.Clamp(frac, 0, 1)), r.Height), fg);
    }

    public static void BarF(this SpriteBatch sb, Vector2 pos, Vector2 size, float frac, Color fg, Color bg)
    {
        sb.Fill(pos - Vector2.One, size + new Vector2(2, 2), Color.Black * 0.6f);
        sb.Fill(pos, size, bg);
        sb.Fill(pos, new Vector2(size.X * Math.Clamp(frac, 0, 1), size.Y), fg);
    }

    public static void Panel(this SpriteBatch sb, Rectangle r, Color fill, Color border, int t = 2)
    {
        sb.Fill(new Rectangle(r.X + 3, r.Y + 4, r.Width, r.Height), Color.Black * 0.25f);
        sb.Fill(r, fill);
        sb.Border(r, border, t);
    }

    /// <summary>Desenha um sprite ancorado pela base (pés/chão) no ponto informado.</summary>
    public static void SpriteFeet(this SpriteBatch sb, Texture2D t, Vector2 feet, float scale, Color c, bool flip = false) =>
        sb.Draw(t, feet, null, c, 0f, new Vector2(t.Width / 2f, t.Height), scale, flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);

    /// <summary>Desenha o sprite centralizado e encaixado dentro da caixa.</summary>
    public static void SpriteFit(this SpriteBatch sb, Texture2D t, Rectangle box, Color c, float maxScale = 4f)
    {
        float s = Math.Min(maxScale, Math.Min(box.Width / (float)t.Width, box.Height / (float)t.Height));
        sb.Draw(t, box.Center.ToVector2(), null, c, 0f, new Vector2(t.Width, t.Height) / 2f, s, SpriteEffects.None, 0f);
    }

    /// <summary>Triângulo preenchido por linhas horizontais (montanhas, telhados nas cenas).</summary>
    public static void Triangle(this SpriteBatch sb, Vector2 a, Vector2 b, Vector2 c, Color col)
    {
        var p = new[] { a, b, c }.OrderBy(v => v.Y).ToArray();
        float y0 = p[0].Y, y2 = p[2].Y;
        for (float y = MathF.Floor(y0); y <= y2; y += 1f)
        {
            float xa = Lerp(p[0], p[2], y), xb = y < p[1].Y ? Lerp(p[0], p[1], y) : Lerp(p[1], p[2], y);
            if (xa > xb) (xa, xb) = (xb, xa);
            sb.Fill(new Vector2(xa, y), new Vector2(xb - xa + 0.5f, 1.2f), col);
        }
        static float Lerp(Vector2 u, Vector2 v, float y) => MathF.Abs(v.Y - u.Y) < 0.001f ? u.X : u.X + (v.X - u.X) * ((y - u.Y) / (v.Y - u.Y));
    }

    public static List<string> Wrap(SpriteFontBase f, string text, float width)
    {
        var lines = new List<string>();
        foreach (var para in text.Split('\n'))
        {
            var line = "";
            foreach (var w in para.Split(' '))
            {
                var test = line.Length == 0 ? w : line + " " + w;
                if (f.MeasureString(test).X > width && line.Length > 0) { lines.Add(line); line = w; }
                else line = test;
            }
            lines.Add(line);
        }
        return lines;
    }
}
