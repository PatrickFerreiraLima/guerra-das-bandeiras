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
}
