using GuerraDasBandeiras.Gameplay;

namespace GuerraDasBandeiras.Core;

/// <summary>
/// Bandeiras estaduais desenhadas por código (estilizadas, em pixel art), geradas uma vez como textura.
/// Amazonas: faixas branca-vermelha-branca com cantão azul estrelado.
/// Minas Gerais: branca com triângulo vermelho (e o lema sugerido em arco).
/// Rio Grande do Sul: diagonal verde-vermelho-amarelo com brasão ao centro.
/// </summary>
public static class FlagArt
{
    const int W = 48, H = 32, S = 4; // grade "pixel art" 48x32, gravada 4x maior para continuar nítida
    static readonly Dictionary<FactionId, Texture2D> _tex = new();
    static Texture2D _neutral;

    static readonly Color White = new(244, 244, 238);
    static readonly Color Red = new(206, 32, 40);
    static readonly Color Blue = new(30, 64, 150);
    static readonly Color Green = new(18, 140, 64);
    static readonly Color Yellow = new(250, 206, 30);

    public static Texture2D Tex(FactionId f)
    {
        if (_tex.TryGetValue(f, out var t)) return t;
        var d = new Color[W * S * H * S];
        for (int py = 0; py < H * S; py++)
            for (int px = 0; px < W * S; px++)
            {
                int x = px / S, y = py / S;
                d[py * W * S + px] = f switch
                {
                    FactionId.Amazonas => Amazonas(x, y),
                    FactionId.MinasGerais => Minas(x, y),
                    _ => RioGrande(x, y),
                };
            }
        t = new Texture2D(Game1.I.GraphicsDevice, W * S, H * S);
        t.SetData(d);
        _tex[f] = t;
        return t;
    }

    /// <summary>Pano branco da bandeira ainda sem dono.</summary>
    public static Texture2D Neutral
    {
        get
        {
            if (_neutral != null) return _neutral;
            var d = new Color[W * S * H * S];
            for (int i = 0; i < d.Length; i++) d[i] = (i / (W * S) / S) % 6 == 0 ? new Color(225, 225, 215) : White;
            _neutral = new Texture2D(Game1.I.GraphicsDevice, W * S, H * S);
            _neutral.SetData(d);
            return _neutral;
        }
    }

    static Color Amazonas(int x, int y)
    {
        // faixas horizontais branca / vermelha / branca e cantão azul no alto à esquerda
        if (x < 21 && y < 13)
        {
            // estrelas: uma maior e várias pequenas
            bool big = (x - 10) * (x - 10) + (y - 6) * (y - 6) <= 2;
            bool small = (x % 4 == 1 && y % 4 == 2) && !((x > 7 && x < 13) && (y > 3 && y < 9));
            return big || small ? White : Blue;
        }
        return y >= 13 && y < 19 ? Red : White;
    }

    static Color Minas(int x, int y)
    {
        // triângulo vermelho centralizado, ponta para cima
        float cx = W / 2f, top = 7, bottom = 25;
        float half = (y - top) / (bottom - top) * 9.5f;
        if (y >= top && y <= bottom && Math.Abs(x + 0.5f - cx) <= half) return Red;
        // lema em arco (sugerido por tracinhos vermelhos)
        float dx = x + 0.5f - cx, dy = y + 0.5f - 18;
        float r = MathF.Sqrt(dx * dx + dy * dy);
        if (r > 13.2f && r < 14.6f && dy > -14 && dy < 2 && ((int)(MathF.Atan2(dy, dx) * 9) & 1) == 0) return new Color(170, 40, 40);
        return White;
    }

    static Color RioGrande(int x, int y)
    {
        // diagonal: verde em cima à esquerda, faixa vermelha, amarelo embaixo à direita
        float s = (x + 0.5f) / W + (y + 0.5f) / H; // 0 no canto superior esquerdo, 2 no inferior direito
        Color c = s < 0.83f ? Green : s > 1.17f ? Yellow : Red;
        // brasão: oval branca com borda
        float ex = (x + 0.5f - W / 2f) / 6.5f, ey = (y + 0.5f - H / 2f) / 8.5f;
        float e = ex * ex + ey * ey;
        if (e < 1f) c = e > 0.6f ? new Color(120, 70, 30) : e > 0.35f ? White : new Color(40, 120, 200);
        return c;
    }

    /// <summary>Bandeira parada (cartões, HUD), com contorno.</summary>
    public static void Draw(SpriteBatch sb, FactionId? f, Rectangle r, bool border = true)
    {
        var tex = f is FactionId id ? Tex(id) : Neutral;
        if (border) sb.Fill(new Rectangle(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), Color.Black * 0.55f);
        sb.Draw(tex, r, Color.White);
        sb.Fill(new Rectangle(r.X, r.Y, r.Width, Math.Max(1, r.Height / 10)), Color.White * 0.18f);
    }

    /// <summary>Bandeira tremulando: o pano é fatiado em colunas que ondulam a partir do mastro.</summary>
    public static void DrawWaving(SpriteBatch sb, FactionId? f, Vector2 topLeft, float w, float h, float time, float phase = 0, Color? tint = null)
    {
        var tex = f is FactionId id ? Tex(id) : Neutral;
        const int slices = 12;
        float sw = w / slices;
        var col = tint ?? Color.White;
        for (int i = 0; i < slices; i++)
        {
            float k = i / (float)(slices - 1);
            float wave = MathF.Sin(time * 5f - i * 0.6f + phase) * h * 0.12f * k;
            float shade = 0.88f + 0.12f * MathF.Sin(time * 5f - i * 0.6f + phase + 1.2f);
            var src = new Rectangle(i * W * S / slices, 0, Math.Max(1, W * S / slices), H * S);
            var dst = new Rectangle((int)MathF.Round(topLeft.X + i * sw), (int)MathF.Round(topLeft.Y + wave), (int)MathF.Ceiling(sw) + 1, (int)MathF.Round(h));
            sb.Draw(tex, dst, src, new Color((int)(col.R * shade), (int)(col.G * shade), (int)(col.B * shade), col.A));
        }
    }
}
