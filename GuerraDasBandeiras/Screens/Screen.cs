using GuerraDasBandeiras.Core;

namespace GuerraDasBandeiras.Screens;

public abstract class Screen
{
    protected Game1 Game => Game1.I;
    protected Vector2 Ui => Game.UiSize;
    /// <summary>Menus desenhados para ~1120x860 encolhem para caber em telas menores (ex.: 1366x768).</summary>
    public virtual float LayoutScale => 1f;
    protected static float FitScale(float designW, float designH) =>
        Math.Min(1f, Math.Min(Game1.I.W / (Game1.UiScale * designW), Game1.I.H / (Game1.UiScale * designH)));
    public abstract void Update(float dt);
    public abstract void Draw(SpriteBatch sb);
}

/// <summary>Fundo animado dos menus: um pedaço de vale com vila e tropas passeando.</summary>
public static class MenuBackground
{
    static float _t;

    public static void Draw(SpriteBatch sb, float dt)
    {
        _t += dt;
        var g = Game1.I;
        float scale = 1.25f;
        int tile = (int)(64 * scale);
        var off = new Vector2(-(_t * 12f % tile), 0);
        sb.Begin(samplerState: SamplerState.LinearClamp);
        int cols = g.W / tile + 3, rows = g.H / tile + 2;
        for (int x = 0; x < cols; x++)
            for (int y = 0; y < rows; y++)
            {
                int gx = x + (int)(_t * 12f / tile);
                int h = (gx * 73856093) ^ (y * 19349663);
                int id = y < 2 ? new[] { 42, 44, 47, 48 }[Math.Abs(h) % 4] : y == rows - 2 ? 13 + (Math.Abs(h) & 1) : 57 + (Math.Abs(h) & 1);
                sb.Draw(Assets.Tile(id), new Rectangle((int)off.X + x * tile, y * tile, tile + 1, tile + 1), Color.White);
            }
        // vila
        float baseY = g.H * 0.62f;
        var structs = new[] { 18, 9, 20, 13, 6, 21, 17 };
        for (int i = 0; i < 9; i++)
        {
            float sx = ((i * 260 - _t * 12f) % (cols * tile + 260)) - 130;
            if (sx < -200) sx += cols * tile + 260;
            var tex = Assets.Structure(structs[i % structs.Length]);
            sb.SpriteFeet(tex, new Vector2(sx, baseY + (i % 2) * 40), 1.1f, Color.White);
        }
        // tropas marchando
        for (int i = 0; i < 14; i++)
        {
            float speed = 40 + (i % 4) * 12;
            float ux = ((i * 157 + _t * speed) % (g.W + 200)) - 100;
            float bob = MathF.Abs(MathF.Sin(_t * 10 + i)) * 3;
            var tex = Assets.Unit(new[] { 15, 14, 21, 9, 3, 17, 22, 10 }[i % 8]);
            sb.SpriteFeet(tex, new Vector2(ux, g.H * 0.8f + (i % 3) * 26 - bob), 0.9f, Color.White);
        }
        sb.Fill(new Rectangle(0, 0, g.W, g.H), new Color(14, 26, 20) * 0.55f);
        sb.End();
    }
}
