using GuerraDasBandeiras.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GuerraDasBandeiras.Tools;

/// <summary>
/// Ferramenta de desenvolvimento: "GuerraDasBandeiras --sheet saida.png"
/// gera uma folha com todos os sprites numerados, para escolher assets.
/// </summary>
public static class SpriteSheetDump
{
    public static void Run(GraphicsDevice gd, SpriteBatch sb, string outPath)
    {
        var groups = new (string name, int count)[] { ("Unit", 24), ("Structure", 23), ("Environment", 21), ("Tile", 58) };
        const int cell = 140, cols = 12;
        int rows = groups.Sum(g => (g.count + cols - 1) / cols);
        var rt = new RenderTarget2D(gd, cell * cols, cell * rows + groups.Length * 30);
        gd.SetRenderTarget(rt);
        gd.Clear(new Color(60, 90, 140));
        sb.Begin(samplerState: SamplerState.PointClamp);
        int y = 0;
        var f = Assets.Font(16);
        foreach (var (name, count) in groups)
        {
            sb.DrawString(f, name, new Vector2(4, y + 4), Color.Yellow);
            y += 30;
            for (int i = 1; i <= count; i++)
            {
                var t = Assets.Tex($"{name}/medieval{name}_{i:00}");
                int c = (i - 1) % cols, r = (i - 1) / cols;
                var pos = new Vector2(c * cell + cell / 2, y + r * cell + cell / 2 - 8);
                float sc = Math.Min(1f, 110f / Math.Max(t.Width, t.Height));
                sb.Draw(t, pos, null, Color.White, 0, new Vector2(t.Width, t.Height) / 2, sc, SpriteEffects.None, 0);
                sb.DrawString(f, i.ToString(), new Vector2(c * cell + 4, y + r * cell + cell - 22), Color.White);
            }
            y += (count + cols - 1) / cols * cell;
        }
        sb.End();
        gd.SetRenderTarget(null);
        using var fs = File.Create(outPath);
        rt.SaveAsPng(fs, rt.Width, rt.Height);
    }

    /// <summary>
    /// "GuerraDasBandeiras --chars saida.png": folha ampliada com todos os personagens gerados (Core/CharArt),
    /// uma linha por facção, mais uma linha com o time azul (espelho), para revisar a pixel art.
    /// </summary>
    /// <summary>
    /// "GuerraDasBandeiras --maps saida.png": visão de cima de 12 mapas sorteados (sementes 1 a 12), para conferir
    /// onde as bases caem. Cores do minimapa; quadrado = Centro (verde = time 0, vermelho = time 1); pontos = recursos.
    /// </summary>
    public static void RunMaps(GraphicsDevice gd, SpriteBatch sb, string outPath)
    {
        const int px = 4, cols = 4, rows = 3, pad = 26;
        var probe = new Gameplay.World(new Gameplay.GameConfig { Seed = 1 });
        int mw = probe.W * px, mh = probe.H * px;
        var rt = new RenderTarget2D(gd, cols * (mw + pad) + pad, rows * (mh + pad + 18) + pad);
        gd.SetRenderTarget(rt);
        gd.Clear(new Color(30, 40, 50));
        sb.Begin(samplerState: SamplerState.PointClamp);
        var f = Assets.FontBold(15);
        for (int i = 0; i < cols * rows; i++)
        {
            var w = new Gameplay.World(new Gameplay.GameConfig { Seed = i + 1, PlayerFaction = Gameplay.FactionId.Amazonas, EnemyFaction = Gameplay.FactionId.RioGrandeDoSul });
            int ox = pad + (i % cols) * (mw + pad), oy = pad + 18 + (i / cols) * (mh + pad + 18);
            for (int x = 0; x < w.W; x++)
                for (int y = 0; y < w.H; y++)
                {
                    var c = w.Ter[x, y] switch
                    {
                        Gameplay.Terrain.Forest => new Color(38, 112, 62), Gameplay.Terrain.Hill => new Color(150, 146, 132),
                        Gameplay.Terrain.Water => new Color(70, 140, 205), Gameplay.Terrain.Ford => new Color(222, 204, 160),
                        Gameplay.Terrain.Dirt => new Color(150, 116, 80), _ => new Color(92, 168, 88),
                    };
                    sb.Fill(new Rectangle(ox + x * px, oy + y * px, px, px), c);
                }
            foreach (var r in w.Resources)
                sb.Fill(new Rectangle(ox + r.Tile.X * px + 1, oy + r.Tile.Y * px + 1, px - 2, px - 2), r.Kind switch { Gameplay.Res.Food => new Color(230, 80, 70), Gameplay.Res.Wood => new Color(20, 70, 30), _ => new Color(230, 230, 240) });
            foreach (var fl in w.Flags) sb.Fill(new Rectangle(ox + (int)(fl.Pos.X / Gameplay.World.T) * px - 3, oy + (int)(fl.Pos.Y / Gameplay.World.T) * px - 3, 10, 10), Pal.Gold);
            for (int t = 0; t < 2; t++)
            {
                var st = w.StartTiles[t];
                var rect = new Rectangle(ox + st.X * px - 2, oy + st.Y * px - 2, 3 * px + 4, 3 * px + 4);
                sb.Fill(rect, Color.Black);
                sb.Fill(new Rectangle(rect.X + 2, rect.Y + 2, rect.Width - 4, rect.Height - 4), t == 0 ? new Color(60, 220, 90) : new Color(240, 60, 50));
            }
            sb.DrawString(f, $"semente {i + 1}", new Vector2(ox, oy - 19), Color.White);
        }
        sb.End();
        gd.SetRenderTarget(null);
        using var fs = File.Create(outPath);
        rt.SaveAsPng(fs, rt.Width, rt.Height);
    }

    public static void RunChars(GraphicsDevice gd, SpriteBatch sb, string outPath)
    {
        var types = Enum.GetValues<Gameplay.UnitType>().Where(t => t != Gameplay.UnitType.Ram).ToArray();
        var rows = new (Gameplay.FactionId fac, int set)[] { (Gameplay.FactionId.Amazonas, 2), (Gameplay.FactionId.MinasGerais, 3), (Gameplay.FactionId.RioGrandeDoSul, 1), (Gameplay.FactionId.RioGrandeDoSul, 0) };
        const int cell = 130;
        var rt = new RenderTarget2D(gd, cell * types.Length, cell * rows.Length + 30);
        gd.SetRenderTarget(rt);
        gd.Clear(new Color(84, 150, 84));
        sb.Begin(samplerState: SamplerState.PointClamp);
        var f = Assets.FontNarrow(14);
        for (int i = 0; i < types.Length; i++) sb.DrawString(f, types[i].ToString(), new Vector2(i * cell + 4, 6), Color.White);
        for (int r = 0; r < rows.Length; r++)
            for (int i = 0; i < types.Length; i++)
            {
                var t = CharArt.Get(types[i], rows[r].fac, rows[r].set);
                sb.Draw(t, new Vector2(i * cell + (cell - t.Width * 1.6f) / 2, 30 + r * cell + 8), null, Color.White, 0, Vector2.Zero, 1.6f, SpriteEffects.None, 0);
            }
        sb.End();
        gd.SetRenderTarget(null);
        using var fs = File.Create(outPath);
        rt.SaveAsPng(fs, rt.Width, rt.Height);
    }
}
