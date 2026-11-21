using GuerraDasBandeiras.Core;
using GuerraDasBandeiras.Gameplay;

namespace GuerraDasBandeiras.Screens;

/// <summary>
/// Cenas animadas da tela de seleção, montadas com os sprites do jogo + formas simples.
/// Espaço virtual da cena: 320x180, escalado para o painel e recortado (scissor).
/// O herói de cada facção é desenhado num segundo passo, em "unidades do mundo", ampliado.
/// Nada aqui toca na simulação: só lê o tempo.
/// </summary>
public static class FactionScene
{
    public const float VW = 320, VH = 180;
    static readonly RasterizerState Scissor = new() { ScissorTestEnable = true, CullMode = CullMode.None };

    public static void Draw(SpriteBatch sb, FactionId id, Rectangle uiRect, float t)
    {
        var g = Game1.I;
        var gd = g.GraphicsDevice;
        float s = Game1.UiScaleEff;
        var old = gd.ScissorRectangle;
        gd.ScissorRectangle = new Rectangle((int)(uiRect.X * s), (int)(uiRect.Y * s), (int)(uiRect.Width * s), (int)(uiRect.Height * s));
        float k = uiRect.Width / VW;
        var m = Matrix.CreateScale(k) * Matrix.CreateTranslation(uiRect.X, uiRect.Y, 0) * g.UiMatrix;

        sb.Begin(transformMatrix: m, samplerState: SamplerState.LinearClamp, rasterizerState: Scissor);
        switch (id)
        {
            case FactionId.Amazonas: Amazonas(sb, t); break;
            case FactionId.MinasGerais: Minas(sb, t); break;
            default: Pampas(sb, t); break;
        }
        sb.End();

        // herói em primeiro plano (escala do mundo x2,3)
        var anchor = new Vector2(id == FactionId.RioGrandeDoSul ? 84 : 72, 170);
        var hm = Matrix.CreateScale(1.65f) * Matrix.CreateTranslation(anchor.X, anchor.Y, 0) * m;
        sb.Begin(transformMatrix: hm, samplerState: SamplerState.LinearClamp, rasterizerState: Scissor);
        switch (id)
        {
            case FactionId.Amazonas: Vigia(sb, t); break;
            case FactionId.MinasGerais: Guardiao(sb, t); break;
            default: Lanceiro(sb, t); break;
        }
        sb.End();

        // vinheta e legenda (no espaço da interface)
        sb.Begin(transformMatrix: g.UiMatrix, samplerState: SamplerState.LinearClamp, rasterizerState: Scissor);
        var bottom = new Rectangle(uiRect.X, uiRect.Bottom - 34, uiRect.Width, 34);
        for (int i = 0; i < 6; i++) sb.Fill(new Rectangle(bottom.X, bottom.Y + i * 6 - 6, bottom.Width, 6), Color.Black * (0.08f * i));
        FlagArt.Draw(sb, id, new Rectangle(uiRect.X + 10, uiRect.Bottom - 26, 27, 18));
        sb.Text(Assets.Font(16), FactionSheet.SceneName(id).ToUpperInvariant(), new Vector2(uiRect.X + 44, uiRect.Bottom - 27), Color.White);
        sb.End();
        gd.ScissorRectangle = old;
    }

    // ---------------- utilidades ----------------

    static void Sky(SpriteBatch sb, Color top, Color bottom, float h)
    {
        for (int i = 0; i < 18; i++)
            sb.Fill(new Vector2(0, i * h / 18f), new Vector2(VW, h / 18f + 1), Color.Lerp(top, bottom, i / 17f));
    }

    static void Sprite(SpriteBatch sb, Texture2D tex, Vector2 feet, float scale, float rot = 0, Color? c = null) =>
        sb.Draw(tex, feet, null, c ?? Color.White, rot, new Vector2(tex.Width / 2f, tex.Height), scale, SpriteEffects.None, 0);

    static float Hash(int i) { float v = MathF.Sin(i * 12.9898f) * 43758.5453f; return v - MathF.Floor(v); }

    // ---------------- Amazonas: mata, passarelas, luz entre as árvores ----------------

    static void Amazonas(SpriteBatch sb, float t)
    {
        Sky(sb, new Color(70, 140, 120), new Color(170, 210, 160), 110);
        // copas ao fundo (tiles de mata escurecidos)
        for (int x = -1; x < 6; x++) sb.Draw(Assets.Tile(44 + (x & 1) * 4), new Rectangle(x * 60 - 10, 52, 62, 62), new Color(150, 170, 150));
        // raios de luz
        for (int i = 0; i < 3; i++)
        {
            float a = 0.07f + 0.04f * MathF.Sin(t * 0.8f + i * 2);
            var p = new Vector2(120 + i * 70, -10);
            sb.Line(p, p + new Vector2(-50, 190), new Color(255, 240, 170) * a, 16);
        }
        sb.Fill(new Vector2(0, 112), new Vector2(VW, 70), new Color(54, 122, 64));
        for (int x = 0; x < 6; x++) sb.Draw(Assets.Tile(57 + (x & 1)), new Rectangle(x * 60, 130, 61, 61), new Color(200, 220, 200));
        // árvores balançando
        int[] trees = { 2, 3, 1, 3, 2, 21, 3 };
        for (int i = 0; i < trees.Length; i++)
        {
            float x = 20 + i * 48 + (i % 2) * 10;
            Sprite(sb, Assets.Env(trees[i]), new Vector2(x, 118 + (i % 3) * 4), 0.75f + (i % 3) * 0.12f, MathF.Sin(t * 1.4f + i) * 0.03f);
        }
        // palafitas e passarela entre duas casas de madeira
        var post = new Color(96, 64, 36);
        Sprite(sb, Assets.Structure(23), new Vector2(222, 122), 0.55f);
        Sprite(sb, Assets.Structure(9), new Vector2(286, 120), 0.45f);
        foreach (var px in new[] { 202f, 222f, 242f, 270f, 300f }) sb.Line(new Vector2(px, 120), new Vector2(px, 136), post, 2.5f);
        sb.Fill(new Vector2(240, 118), new Vector2(34, 3), new Color(170, 120, 70));
        sb.Line(new Vector2(240, 117), new Vector2(274, 117), post, 1);
        // bandeira do Amazonas
        sb.Line(new Vector2(176, 132), new Vector2(176, 62), new Color(80, 55, 30), 2);
        FlagArt.DrawWaving(sb, FactionId.Amazonas, new Vector2(177, 63), 36, 24, t);
        // névoa leve
        for (int i = 0; i < 4; i++)
        {
            float x = (i * 97 + t * (6 + i * 2)) % 420 - 50;
            sb.Disc(new Vector2(x, 125 + i * 8), 70, 10, Color.White * 0.07f);
        }
        // folhas caindo
        for (int i = 0; i < 12; i++)
        {
            float x = (Hash(i) * 340 + t * (8 + i % 4) + MathF.Sin(t * 1.5f + i) * 8) % 340 - 10;
            float y = (Hash(i + 50) * 200 + t * (14 + i % 5)) % 200 - 10;
            var d = new Vector2(MathF.Cos(t * 3 + i), MathF.Sin(t * 3 + i)) * 2f;
            sb.Line(new Vector2(x, y) - d, new Vector2(x, y) + d, (i % 2 == 0 ? new Color(70, 160, 70) : new Color(150, 190, 70)) * 0.9f, 1.8f);
        }
        // samambaias no primeiro plano
        for (int i = 0; i < 5; i++) Sprite(sb, Assets.Env(12), new Vector2(150 + i * 40, 182), 0.9f, MathF.Sin(t * 2 + i) * 0.05f);
    }

    static void Vigia(SpriteBatch sb, float t)
    {
        var def = Defs.Units[UnitType.VigiaDaMata];
        var tex = Defs.UnitSprite(def, 2);
        float breath = MathF.Sin(t * 2.1f) * 0.025f;
        float sway = MathF.Sin(t * 0.9f) * 1.5f;
        var feet = new Vector2(sway, 0);
        float sc = 0.9f * def.Scale;
        float h = tex.Height * sc;
        sb.Disc(feet + new Vector2(4, 1), 16, 4, Color.Black * 0.3f);
        // capa de folhas
        var back = feet + new Vector2(-4, -h * 0.55f);
        sb.Disc(back + new Vector2(0, 2), 9, 12, new Color(40, 120, 60));
        sb.Disc(back + new Vector2(-3, 8), 7, 7, new Color(60, 150, 70));
        sb.Draw(tex, feet, null, Color.White, 0, new Vector2(tex.Width / 2f, tex.Height), new Vector2(sc, sc * (1 + breath)), SpriteEffects.None, 0);
        var head = feet + new Vector2(0, -h * (1 + breath) + 3);
        sb.Disc(head + new Vector2(0, 2), 8.5f, 5, new Color(46, 130, 62));
        sb.Line(head + new Vector2(-3, 1), head + new Vector2(-9, -10 + MathF.Sin(t * 3) * 1.2f), new Color(40, 140, 70), 3);
        sb.Line(head + new Vector2(-4, 0), head + new Vector2(-8, -7), new Color(120, 200, 110), 1.5f);
    }

    // ---------------- Minas Gerais: serras, pedra, fortaleza ----------------

    static void Minas(SpriteBatch sb, float t)
    {
        Sky(sb, new Color(120, 160, 205), new Color(230, 215, 190), 120);
        // serras em camadas
        sb.Triangle(new Vector2(-30, 120), new Vector2(70, 30), new Vector2(170, 120), new Color(126, 128, 138));
        sb.Triangle(new Vector2(70, 30), new Vector2(170, 120), new Vector2(110, 120), new Color(104, 106, 116));
        sb.Triangle(new Vector2(110, 120), new Vector2(220, 18), new Vector2(340, 120), new Color(140, 140, 146));
        sb.Triangle(new Vector2(220, 18), new Vector2(340, 120), new Vector2(260, 120), new Color(116, 116, 124));
        sb.Triangle(new Vector2(200, 120), new Vector2(300, 55), new Vector2(380, 120), new Color(128, 124, 120));
        sb.Fill(new Vector2(0, 114), new Vector2(VW, 70), new Color(150, 128, 98));
        for (int x = 0; x < 6; x++) sb.Draw(Assets.Tile(15 + (x & 1)), new Rectangle(x * 60, 128, 61, 61), new Color(210, 200, 190));
        // fortaleza: torre de pedra no morro e muralha com telhados cerâmicos
        sb.Disc(new Vector2(156, 122), 44, 14, new Color(120, 110, 96));
        Sprite(sb, Assets.Structure(5), new Vector2(156, 116), 0.55f);
        Sprite(sb, Assets.Structure(2), new Vector2(252, 128), 0.6f);
        // bandeira no alto da torre
        sb.Line(new Vector2(170, 86), new Vector2(170, 40), new Color(80, 55, 30), 2);
        FlagArt.DrawWaving(sb, FactionId.MinasGerais, new Vector2(171, 41), 33, 22, t);
        // carrinho de minério
        var cart = new Vector2(218, 150);
        sb.Fill(cart - new Vector2(11, 12), new Vector2(22, 9), new Color(110, 80, 50));
        sb.Disc(cart + new Vector2(-6, 0), 3.5f, 3.5f, new Color(50, 50, 55));
        sb.Disc(cart + new Vector2(6, 0), 3.5f, 3.5f, new Color(50, 50, 55));
        sb.Disc(cart + new Vector2(-3, -13), 5, 4, new Color(170, 170, 175));
        sb.Disc(cart + new Vector2(4, -14), 4, 3.5f, new Color(200, 180, 90));
        // poeira discreta levantada pelo vento
        for (int i = 0; i < 10; i++)
        {
            float x = (Hash(i) * 360 + t * (18 + i % 4)) % 360 - 20;
            float y = 140 + Hash(i + 9) * 40 + MathF.Sin(t * 2 + i) * 3;
            sb.Disc(new Vector2(x, y), 2.5f + Hash(i + 3) * 3, 1.8f, new Color(210, 190, 150) * 0.35f);
        }
    }

    static void Guardiao(SpriteBatch sb, float t)
    {
        var def = Defs.Units[UnitType.GuardiaoDasGerais];
        var tex = Defs.UnitSprite(def, 3);
        float breath = MathF.Sin(t * 1.8f) * 0.02f;
        float sc = 0.9f * def.Scale;
        float h = tex.Height * sc;
        // a cada 4 s ele ajusta o escudo (sobe e volta)
        float ph = t % 4f;
        float lift = ph < 0.7f ? MathF.Sin(ph / 0.7f * MathHelper.Pi) * 5 : 0;
        sb.Disc(new Vector2(4, 1), 18, 4, Color.Black * 0.3f);
        sb.Draw(tex, Vector2.Zero, null, Color.White, 0, new Vector2(tex.Width / 2f, tex.Height), new Vector2(sc, sc * (1 + breath)), SpriteEffects.None, 0);
        var head = new Vector2(0, -h * (1 + breath) + 3);
        sb.Disc(head + new Vector2(0, 3), 8.5f, 4.5f, new Color(120, 126, 136));
        sb.Fill(head + new Vector2(-1.5f, -3), new Vector2(3, 5), new Color(200, 70, 50));
        var shield = new Vector2(12, -20 - lift);
        sb.Disc(shield + new Vector2(1, 1), 10, 14, Color.Black * 0.25f);
        sb.Disc(shield, 10, 14, new Color(160, 168, 182));
        sb.Ellipse(shield, 10, 14, new Color(80, 86, 98));
        sb.Disc(shield, 3, 3, Defs.SetColors[3]);
        // brilho ocasional na armadura
        float glint = t % 3f;
        if (glint < 0.35f)
        {
            float a = MathF.Sin(glint / 0.35f * MathHelper.Pi);
            var gp = shield + new Vector2(-4, -6);
            sb.Line(gp - new Vector2(4, 0), gp + new Vector2(4, 0), Color.White * a, 1.5f);
            sb.Line(gp - new Vector2(0, 4), gp + new Vector2(0, 4), Color.White * a, 1.5f);
        }
    }

    // ---------------- Rio Grande do Sul: campo aberto, galpão, cerca, moinho ----------------

    static void Pampas(SpriteBatch sb, float t)
    {
        Sky(sb, new Color(90, 150, 220), new Color(250, 214, 160), 120);
        // coxilhas ao fundo
        sb.Disc(new Vector2(60, 122), 120, 18, new Color(120, 170, 90));
        sb.Disc(new Vector2(260, 124), 140, 20, new Color(110, 160, 86));
        sb.Fill(new Vector2(0, 118), new Vector2(VW, 70), new Color(96, 168, 82));
        for (int x = 0; x < 6; x++) sb.Draw(Assets.Tile(57 + (x & 1)), new Rectangle(x * 60, 132, 61, 61), Color.White);
        // galpão
        Sprite(sb, Assets.Structure(19), new Vector2(236, 128), 0.5f);
        // moinho: torre desenhada + pás do pacote girando
        var mill = new Vector2(292, 80);
        sb.Triangle(new Vector2(282, 128), new Vector2(302, 128), new Vector2(292, 78), new Color(230, 220, 200));
        sb.Triangle(new Vector2(292, 78), new Vector2(302, 128), new Vector2(296, 128), new Color(200, 190, 170));
        var blades = Assets.Structure(13);
        sb.Draw(blades, mill, null, Color.White, t * 1.2f, new Vector2(blades.Width / 2f, blades.Height / 2f), 0.42f, SpriteEffects.None, 0);
        // cerca de estância
        var wood = new Color(120, 82, 46);
        for (float x = 150; x <= 330; x += 14) sb.Line(new Vector2(x, 146), new Vector2(x, 134), wood, 2);
        sb.Line(new Vector2(150, 137), new Vector2(330, 137), wood, 1.6f);
        sb.Line(new Vector2(150, 142), new Vector2(330, 142), wood, 1.6f);
        // bandeira do RS no galpão
        sb.Line(new Vector2(206, 112), new Vector2(206, 74), new Color(80, 55, 30), 2);
        FlagArt.DrawWaving(sb, FactionId.RioGrandeDoSul, new Vector2(207, 75), 30, 20, t, 1f);
        // capim balançando com o vento (primeiro plano)
        for (int i = 0; i < 40; i++)
        {
            float x = i * 8 + Hash(i) * 6;
            float y = 170 + Hash(i + 7) * 12;
            float bend = MathF.Sin(t * 2.2f + x * 0.05f) * 4;
            var c = i % 3 == 0 ? new Color(170, 200, 90) : new Color(90, 160, 70);
            sb.Line(new Vector2(x, y), new Vector2(x + bend, y - 9 - Hash(i + 3) * 6), c, 1.4f);
        }
    }

    static void Lanceiro(SpriteBatch sb, float t)
    {
        var def = Defs.Units[UnitType.LanceiroDosPampas];
        var tex = Defs.UnitSprite(def, 1);
        float breathe = MathF.Sin(t * 1.6f) * 0.8f;     // o cavalo respira
        var feet = new Vector2(0, 0);
        sb.Disc(feet + new Vector2(4, 1), 22, 4, Color.Black * 0.3f);
        WorldRenderer.DrawHorse(sb, feet + new Vector2(0, -breathe * 0.3f), 1, 0, Color.White);
        var rider = feet + new Vector2(2, -14 - breathe * 0.5f);
        float sc = 0.9f * def.Scale;
        sb.Draw(tex, rider, null, Color.White, 0, new Vector2(tex.Width / 2f, tex.Height), sc, SpriteEffects.None, 0);
        var head = rider + new Vector2(0, -tex.Height * sc + 3);
        sb.Disc(head + new Vector2(0, 4), 12, 3.2f, new Color(70, 46, 28));
        sb.Disc(head, 6.5f, 5, new Color(92, 62, 36));
        sb.Fill(head + new Vector2(-6, 2), new Vector2(12, 1.5f), new Color(170, 40, 40));
        // lança com pequeno movimento e flâmula com a bandeira do RS
        float ang = -0.45f + MathF.Sin(t * 1.3f) * 0.05f;
        var hand = rider + new Vector2(6, -16);
        var dirv = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
        var tip = hand + dirv * 38;
        sb.Line(hand - dirv * 12, tip, new Color(120, 86, 50), 2.5f);
        sb.Line(tip, tip + dirv * 6, new Color(210, 210, 220), 2.5f);
        FlagArt.DrawWaving(sb, FactionId.RioGrandeDoSul, tip - dirv * 14 + new Vector2(0, 1), 13, 9, t * 1.4f, 2f);
    }
}
