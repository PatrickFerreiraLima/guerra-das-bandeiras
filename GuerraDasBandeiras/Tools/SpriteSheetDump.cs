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
