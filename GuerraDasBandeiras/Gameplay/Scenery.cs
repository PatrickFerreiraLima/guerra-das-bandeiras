using GuerraDasBandeiras.Core;

namespace GuerraDasBandeiras.Gameplay;

/// <summary>
/// Paisagem brasileira desenhada por código, só visual (não bloqueia caminho nem é clicável):
/// coqueiros na beira do rio, ipês floridos (amarelo, roxo e branco) com tucanos, vitórias-régias,
/// capivaras na margem, revoadas de araras e, perto de cada capital, elementos do estado:
/// açaizeiros e bananeiras (Amazonas), igreja barroca colonial (Minas Gerais), araucárias e gado (RS).
/// Usa um gerador aleatório próprio para não mudar o mapa nem o balanceamento.
/// </summary>
public static class Scenery
{
    public enum Kind { Coqueiro, Ipe, Araucaria, Acai, Bananeira, Igreja, VitoriaRegia, Capivara, Boi }

    public class Prop
    {
        public Kind Kind;
        public Vector2 Pos;
        public float Scale = 1f;
        public int Var;
    }

    const int T = World.T;

    // ------------------------------------------------------------------ distribuição no mapa

    public static void Populate(World w)
    {
        var r = new Random(w.Config.Seed * 7919 + 2027);
        var props = w.Props;

        bool FreeAround(int x, int y, int rad)
        {
            for (int dx = -rad; dx <= rad; dx++)
                for (int dy = -rad; dy <= rad; dy++)
                {
                    int px = x + dx, py = y + dy;
                    if (px < 0 || py < 0 || px >= w.W || py >= w.H) return false;
                    if (w.Occ[px, py] != null) return false;
                }
            return true;
        }
        bool NearWater(int x, int y) =>
            w.TerrainAt(x - 1, y) == Terrain.Water || w.TerrainAt(x + 1, y) == Terrain.Water ||
            w.TerrainAt(x, y - 1) == Terrain.Water || w.TerrainAt(x, y + 1) == Terrain.Water;
        bool NearCrossing(int y) =>
            Math.Abs(y - MapGen.FordRows[0]) <= 3 || Math.Abs(y - MapGen.FordRows[1]) <= 3 || Math.Abs(y - MapGen.ShortcutRow) <= 1;
        Vector2 Jitter(int x, int y, int amount) => w.CenterOf(new Point(x, y)) + new Vector2(r.Next(-amount, amount + 1), r.Next(-amount, amount + 1));

        int capivaraGroups = 0;
        for (int y = 1; y < w.H - 1; y++)
            for (int x = 1; x < w.W - 1; x++)
            {
                var ter = w.Ter[x, y];
                if (ter == Terrain.Water)
                {
                    if (NearCrossing(y) || r.NextDouble() > 0.32) continue;
                    int n = 1 + r.Next(2);
                    for (int i = 0; i < n; i++)
                        props.Add(new Prop { Kind = Kind.VitoriaRegia, Pos = Jitter(x, y, 20), Scale = 0.8f + (float)r.NextDouble() * 0.45f, Var = r.Next(1000) });
                    continue;
                }
                if (ter != Terrain.Grass && ter != Terrain.Hill) continue;
                if (MapGen.NearFordOrBase(w, x, y) || NearCrossing(y) && Math.Abs(x - MapGen.RiverX(y)) <= 5) continue;

                if (ter == Terrain.Grass && NearWater(x, y))
                {
                    if (r.NextDouble() < 0.13 && FreeAround(x, y, 0))
                        props.Add(new Prop { Kind = Kind.Coqueiro, Pos = Jitter(x, y, 14), Scale = 0.9f + (float)r.NextDouble() * 0.3f, Var = r.Next(1000) });
                    else if (capivaraGroups < 5 && r.NextDouble() < 0.06)
                    {
                        capivaraGroups++;
                        var p = Jitter(x, y, 10);
                        props.Add(new Prop { Kind = Kind.Capivara, Pos = p, Scale = 1f, Var = r.Next(1000) });
                        props.Add(new Prop { Kind = Kind.Capivara, Pos = p + new Vector2(22, 10), Scale = 0.65f, Var = r.Next(1000) });
                        if (r.Next(2) == 0) props.Add(new Prop { Kind = Kind.Capivara, Pos = p + new Vector2(-16, 12), Scale = 0.6f, Var = r.Next(1000) });
                    }
                    continue;
                }
                if (!NearWater(x, y) && r.NextDouble() < (ter == Terrain.Hill ? 0.018 : 0.010) && FreeAround(x, y, 1))
                    props.Add(new Prop { Kind = Kind.Ipe, Pos = Jitter(x, y, 12), Scale = 0.95f + (float)r.NextDouble() * 0.3f, Var = r.Next(1000) });
            }

        // elementos de cada estado, atrás da capital (longe do rio)
        for (int t = 0; t < 2; t++)
        {
            var st = w.StartTiles[t];
            float back = -w.BaseDir[t];   // lado de trás da base (longe do rio)
            float vf = w.BaseFlip[t];
            var c = w.CenterOf(new Point(st.X + 1, st.Y + 1));
            // posição atrás da base; se cair em cima de um recurso (moita, árvore, pedra), recua até achar chão livre
            Vector2 At(float dx, float dy)
            {
                for (int k = 0; k < 4; k++)
                {
                    var p = c + new Vector2(back * (dx + k * 48), vf * dy);
                    var tile = w.TileOf(p);
                    bool busy = false;
                    for (int ox = -1; ox <= 1 && !busy; ox++)
                        for (int oy = -1; oy <= 0 && !busy; oy++)
                        {
                            var q = new Point(tile.X + ox, tile.Y + oy);
                            if (w.InBounds(q) && w.Occ[q.X, q.Y] != null) busy = true;
                        }
                    if (!busy && p.X > 40 && p.X < w.W * T - 40) return p;
                }
                return c + new Vector2(back * dx, vf * dy);
            }
            switch (w.Players[t].Faction.Id)
            {
                case FactionId.Amazonas:
                    for (int i = 0; i < 3; i++)
                        props.Add(new Prop { Kind = Kind.Acai, Pos = At((215 + r.Next(40)), -40 + i * 70 + r.Next(-10, 10)), Scale = 1f + (i % 2) * 0.15f, Var = r.Next(1000) });
                    props.Add(new Prop { Kind = Kind.Bananeira, Pos = At(150, 150), Scale = 1.05f, Var = r.Next(1000) });
                    props.Add(new Prop { Kind = Kind.Bananeira, Pos = At(205, 175), Scale = 0.9f, Var = r.Next(1000) });
                    break;
                case FactionId.MinasGerais:
                    props.Add(new Prop { Kind = Kind.Igreja, Pos = At(205, -40), Scale = 1f, Var = r.Next(1000) });
                    props.Add(new Prop { Kind = Kind.Ipe, Pos = At(150, 140), Scale = 1.1f, Var = 0 });     // ipê-amarelo
                    props.Add(new Prop { Kind = Kind.Ipe, Pos = At(250, 120), Scale = 0.95f, Var = 1 });   // ipê-roxo
                    break;
                default:
                    for (int i = 0; i < 3; i++)
                        props.Add(new Prop { Kind = Kind.Araucaria, Pos = At((250 + (i % 2) * 40), -250 + i * 55), Scale = 1f + (i % 2) * 0.15f, Var = r.Next(1000) });
                    for (int i = 0; i < 4; i++)
                        props.Add(new Prop { Kind = Kind.Boi, Pos = At((130 + i * 34), 150 + (i % 2) * 26), Scale = 1f, Var = r.Next(1000) });
                    break;
            }
        }
        // desenho de trás para frente
        props.Sort((a, b) => a.Pos.Y.CompareTo(b.Pos.Y));
    }

    // ------------------------------------------------------------------ desenho no mapa

    /// <summary>Plantas aquáticas (vão por baixo de tudo, logo depois da água).</summary>
    public static void DrawWaterProps(SpriteBatch sb, World w, RectangleF view, float time)
    {
        foreach (var p in w.Props)
            if (p.Kind == Kind.VitoriaRegia && view.Intersects(p.Pos.X - 30, p.Pos.Y - 30, 60, 60))
                VitoriaRegia(sb, p.Pos, p.Scale, time, p.Var);
    }

    /// <summary>Árvores, construções e bichos (no chão, antes das unidades).</summary>
    public static void DrawLandProps(SpriteBatch sb, World w, RectangleF view, float time)
    {
        foreach (var p in w.Props)
        {
            if (p.Kind == Kind.VitoriaRegia || !view.Intersects(p.Pos.X - 70, p.Pos.Y - 140, 140, 160)) continue;
            switch (p.Kind)
            {
                case Kind.Coqueiro: Coqueiro(sb, p.Pos, p.Scale, time, p.Var); break;
                case Kind.Ipe: Ipe(sb, p.Pos, p.Scale, time, p.Var); break;
                case Kind.Araucaria: Araucaria(sb, p.Pos, p.Scale, time, p.Var); break;
                case Kind.Acai: Acai(sb, p.Pos, p.Scale, time, p.Var); break;
                case Kind.Bananeira: Bananeira(sb, p.Pos, p.Scale, time, p.Var); break;
                case Kind.Igreja: Igreja(sb, p.Pos, p.Scale); break;
                case Kind.Capivara:
                {
                    // passeia devagar pela margem, parando para pastar
                    float k = time * 0.15f + p.Var;
                    float dx = MathF.Sin(k) * 18;
                    float dir = MathF.Cos(k) >= 0 ? 1 : -1;
                    Capivara(sb, p.Pos + new Vector2(dx, 0), p.Scale, time, p.Var, dir);
                    break;
                }
                case Kind.Boi:
                {
                    float k = time * 0.1f + p.Var;
                    float dir = MathF.Cos(k) >= 0 ? 1 : -1;
                    Boi(sb, p.Pos + new Vector2(MathF.Sin(k) * 14, 0), p.Scale, time, p.Var, dir);
                    break;
                }
            }
        }
    }

    /// <summary>Revoadas de araras cruzando o mapa (por cima das unidades, com sombra no chão).</summary>
    public static void DrawBirds(SpriteBatch sb, World w, RectangleF view, float time)
    {
        float span = w.W * T + 1400;
        for (int f = 0; f < 2; f++)
        {
            float dir = f == 0 ? 1 : -1;
            float x0 = ((time * 95 + f * 1900) % span) - 700;
            if (dir < 0) x0 = w.W * T - x0;
            float y0 = (f == 0 ? 0.28f : 0.62f) * w.H * T + MathF.Sin(time * 0.3f + f) * 60;
            for (int i = 0; i < 3; i++)
            {
                var pos = new Vector2(x0 - dir * i * 46, y0 + (i % 2) * 28 - i * 8 + MathF.Sin(time * 1.4f + i) * 5);
                if (!view.Intersects(pos.X - 40, pos.Y - 40, 80, 160)) continue;
                sb.Disc(pos + new Vector2(10, 96), 9, 3.5f, Color.Black * 0.12f);
                Arara(sb, pos, dir, time * 9 + i * 1.7f + f, i);
            }
        }
    }

    // ------------------------------------------------------------------ peças (também usadas nas cenas de facção)

    static readonly Color Outline = new(28, 40, 26);

    static void DiscRot(SpriteBatch sb, Vector2 c, float rx, float ry, float rot, Color col) =>
        sb.Draw(Assets.Circle, c, null, col, rot, new Vector2(64, 64), new Vector2(rx / 64f, ry / 64f), SpriteEffects.None, 0f);

    static void Shadow(SpriteBatch sb, Vector2 p, float rx, float ry) => sb.Disc(p + new Vector2(rx * 0.25f, -1), rx, ry, Color.Black * 0.22f);

    public static void Coqueiro(SpriteBatch sb, Vector2 p, float s, float time, int v)
    {
        Shadow(sb, p + new Vector2(14 * s, 0), 24 * s, 7 * s);
        float lean = ((v & 1) == 0 ? 1 : -1) * (0.2f + (v % 5) * 0.04f);
        var pts = new Vector2[9];
        for (int i = 0; i < 9; i++) { float k = i / 8f; pts[i] = p + new Vector2(lean * 44 * s * k * k, -82 * s * k); }
        for (int i = 0; i < 8; i++) sb.Line(pts[i], pts[i + 1], new Color(70, 48, 30), 7.5f * s);
        for (int i = 0; i < 8; i++) sb.Line(pts[i], pts[i + 1], i % 2 == 0 ? new Color(160, 120, 78) : new Color(134, 98, 62), 5 * s);
        var top = pts[8];
        float sway = MathF.Sin(time * 1.3f + v) * 3 * s;
        float[] angles = { 192, 214, 238, 262, 286, 310, 334, 356, 172 };
        foreach (var pass in new[] { 0, 1 })
            foreach (var a in angles)
            {
                float rad = MathHelper.ToRadians(a);
                var prev = top;
                for (int k = 1; k <= 4; k++)
                {
                    float f = k / 4f;
                    var q = top + new Vector2(MathF.Cos(rad) * 34 * s * f + sway * f, MathF.Sin(rad) * 18 * s * f + 22 * s * f * f);
                    if (pass == 0) sb.Line(prev, q, new Color(22, 70, 34), 8f * s * (1.1f - f * 0.5f));
                    else
                    {
                        sb.Line(prev, q, new Color(64, 154, 66), 5.8f * s * (1.1f - f * 0.5f));
                        sb.Line(prev, q, new Color(126, 196, 96), 1.2f * s);
                    }
                    prev = q;
                }
            }
        for (int i = 0; i < 3; i++) sb.Disc(top + new Vector2((i - 1) * 4 * s, 4 * s + (i % 2) * 2 * s), 3.2f * s, 3.2f * s, new Color(104, 70, 38));
    }

    public static void Ipe(SpriteBatch sb, Vector2 p, float s, float time, int v)
    {
        // 0 = ipê-amarelo, 1 = ipê-roxo, 2 = ipê-branco (amarelo é o mais comum)
        int kind = v < 3 ? v : (v % 10) < 6 ? 0 : (v % 10) < 9 ? 1 : 2;
        Color baseC = kind switch { 0 => new Color(244, 196, 40), 1 => new Color(198, 88, 168), _ => new Color(240, 236, 232) };
        Color light = kind switch { 0 => new Color(255, 232, 120), 1 => new Color(238, 150, 214), _ => Color.White };
        Color shade = kind switch { 0 => new Color(196, 142, 26), 1 => new Color(140, 56, 118), _ => new Color(196, 190, 186) };

        Shadow(sb, p + new Vector2(10 * s, 0), 30 * s, 8 * s);
        // flores caídas no chão
        for (int i = 0; i < 9; i++)
        {
            int h = Hash(v * 31 + i);
            sb.Disc(p + new Vector2(((h % 64) - 32) * s, ((h >> 8) % 7) * 1.5f * s - 3), 2.2f * s, 1.6f * s, (i % 2 == 0 ? baseC : light) * 0.95f);
        }
        var trunk = new Color(96, 66, 44);
        sb.Line(p, p + new Vector2(0, -36 * s), new Color(50, 34, 24), 7 * s);
        sb.Line(p + new Vector2(0, -30 * s), p + new Vector2(-17 * s, -50 * s), new Color(50, 34, 24), 4.5f * s);
        sb.Line(p + new Vector2(0, -30 * s), p + new Vector2(15 * s, -52 * s), new Color(50, 34, 24), 4.5f * s);
        sb.Line(p, p + new Vector2(0, -36 * s), trunk, 5 * s);
        sb.Line(p + new Vector2(0, -30 * s), p + new Vector2(-17 * s, -50 * s), trunk, 3 * s);
        sb.Line(p + new Vector2(0, -30 * s), p + new Vector2(15 * s, -52 * s), trunk, 3 * s);
        float sw = MathF.Sin(time * 0.8f + v) * 1.2f;
        (float x, float y, float r)[] blobs = { (0, -60, 17), (-19, -52, 14), (19, -54, 14), (-9, -71, 13), (10, -71, 13), (0, -46, 12) };
        foreach (var b in blobs) sb.Disc(p + new Vector2(b.x * s + sw, b.y * s), (b.r + 2) * s, (b.r + 1.5f) * s * 0.9f, Darken(shade, 0.35f));
        foreach (var b in blobs) sb.Disc(p + new Vector2(b.x * s + sw, b.y * s), b.r * s, b.r * s * 0.88f, shade);
        foreach (var b in blobs) sb.Disc(p + new Vector2((b.x - 3) * s + sw, (b.y - 3) * s), b.r * 0.72f * s, b.r * 0.62f * s, baseC);
        for (int i = 0; i < 14; i++)
        {
            int h = Hash(v * 17 + i + 500);
            sb.Disc(p + new Vector2(((h % 44) - 22) * s + sw, (-46 - (h >> 8) % 30) * s), 2 * s, 2 * s, light);
        }
        // um tucano pousado em alguns ipês
        if (v % 3 == 0) Tucano(sb, p + new Vector2(16 * s + sw, -50 * s), s, time, v);
    }

    public static void Tucano(SpriteBatch sb, Vector2 p, float s, float time, int v)
    {
        float look = MathF.Sin(time * 0.7f + v) > 0.6f ? -1 : 1;
        sb.Disc(p + new Vector2(0, -2 * s), 4.2f * s, 5.4f * s, new Color(20, 20, 24));
        sb.Disc(p + new Vector2(1.2f * look * s, -4 * s), 2.4f * s, 2.6f * s, new Color(250, 246, 220));
        sb.Disc(p + new Vector2(1.6f * look * s, -7 * s), 2.8f * s, 2.4f * s, new Color(20, 20, 24));
        var beakBase = p + new Vector2(3.4f * look * s, -7.4f * s);
        sb.Line(beakBase, beakBase + new Vector2(8 * look * s, 1.5f * s), new Color(252, 150, 30), 3.2f * s);
        sb.Line(beakBase + new Vector2(6 * look * s, 1.2f * s), beakBase + new Vector2(8.5f * look * s, 1.6f * s), Color.Black, 2.4f * s);
        sb.Disc(p + new Vector2(2.4f * look * s, -7.6f * s), 1 * s, 1 * s, new Color(60, 140, 230));
    }

    public static void Araucaria(SpriteBatch sb, Vector2 p, float s, float time, int v)
    {
        Shadow(sb, p + new Vector2(16 * s, 0), 30 * s, 7 * s);
        var bark = new Color(112, 80, 58);
        sb.Line(p, p + new Vector2(0, -104 * s), new Color(48, 34, 26), 6.5f * s);
        sb.Line(p, p + new Vector2(0, -104 * s), bark, 4.2f * s);
        foreach (var y in new[] { -44, -60, -74 })
            sb.Line(p + new Vector2(0, y * s), p + new Vector2(((y & 2) == 0 ? 7 : -7) * s, (y - 5) * s), new Color(70, 50, 36), 2 * s);
        float sw = MathF.Sin(time * 0.6f + v) * 1.5f;
        var dark = new Color(30, 80, 48);
        var mid = new Color(48, 112, 62);
        var light = new Color(82, 148, 82);
        // copa em "candelabro": galhos que saem do tronco e sobem nas pontas, com tufos de folhas
        foreach (var pass in new[] { 0, 1 })
            for (int j = 0; j < 4; j++)
                foreach (var side in new[] { -1, 1 })
                {
                    var from = p + new Vector2(0, (-82 - j * 6) * s);
                    var to = p + new Vector2(side * (34 - j * 7) * s + sw, (-100 - j * 4) * s);
                    if (pass == 0) { sb.Line(from, to, new Color(70, 50, 36), 2.5f * s); continue; }
                    sb.Disc(to + new Vector2(0, 1 * s), (11 - j) * s, 4.8f * s, dark);
                    sb.Disc(to, (9.5f - j) * s, 3.6f * s, mid);
                    sb.Disc(to + new Vector2(-1 * s, -1.5f * s), (6 - j * 0.5f) * s, 1.8f * s, light);
                }
        sb.Disc(p + new Vector2(sw, -114 * s), 8 * s, 4.5f * s, dark);
        sb.Disc(p + new Vector2(sw, -115 * s), 6.5f * s, 3.2f * s, mid);
    }

    public static void Acai(SpriteBatch sb, Vector2 p, float s, float time, int v)
    {
        Shadow(sb, p + new Vector2(10 * s, 0), 18 * s, 5 * s);
        (float x, float h, float lean)[] stems = { (-6, 70, -0.12f), (1, 86, 0.04f), (8, 62, 0.16f) };
        foreach (var (x, h, lean) in stems)
        {
            var b = p + new Vector2(x * s, 0);
            var top = b + new Vector2(lean * h * s, -h * s);
            sb.Line(b, top, new Color(58, 46, 36), 4 * s);
            sb.Line(b, top, new Color(128, 112, 88), 2.4f * s);
            float sway = MathF.Sin(time * 1.5f + v + x) * 2 * s;
            foreach (var pass in new[] { 0, 1 })
                for (int a = 0; a < 6; a++)
                {
                    float rad = MathHelper.ToRadians(190 + a * 32);
                    var prev = top;
                    for (int k = 1; k <= 3; k++)
                    {
                        float f = k / 3f;
                        var q = top + new Vector2(MathF.Cos(rad) * 22 * s * f + sway * f, MathF.Sin(rad) * 10 * s * f + 16 * s * f * f);
                        sb.Line(prev, q, pass == 0 ? new Color(22, 68, 34) : new Color(70, 150, 70), (pass == 0 ? 4 : 2.4f) * s);
                        prev = q;
                    }
                }
            // cacho de açaí (frutinhas roxas) logo abaixo da copa
            for (int i = 0; i < 5; i++)
                sb.Disc(top + new Vector2((i % 3 - 1) * 2.6f * s + 3 * s, (4 + i / 3 * 2.6f) * s), 2 * s, 2 * s, i % 2 == 0 ? new Color(70, 26, 72) : new Color(104, 44, 106));
        }
    }

    public static void Bananeira(SpriteBatch sb, Vector2 p, float s, float time, int v)
    {
        Shadow(sb, p + new Vector2(10 * s, 0), 24 * s, 6 * s);
        sb.Line(p, p + new Vector2(0, -30 * s), new Color(56, 66, 36), 9 * s);
        sb.Line(p, p + new Vector2(0, -30 * s), new Color(126, 146, 72), 6.5f * s);
        var top = p + new Vector2(0, -30 * s);
        float sway = MathF.Sin(time * 1.1f + v) * 0.06f;
        float[] angles = { -2.6f, -2.0f, -1.45f, -0.95f, -0.4f };
        foreach (var pass in new[] { 0, 1 })
            foreach (var a0 in angles)
            {
                float a = a0 + sway;
                var dirv = new Vector2(MathF.Cos(a), MathF.Sin(a) * 0.8f + 0.25f);
                var c = top + dirv * 18 * s;
                if (pass == 0) DiscRot(sb, c, 19 * s, 6.5f * s, MathF.Atan2(dirv.Y, dirv.X), new Color(26, 74, 36));
                else
                {
                    DiscRot(sb, c, 17.5f * s, 5.2f * s, MathF.Atan2(dirv.Y, dirv.X), new Color(84, 170, 76));
                    sb.Line(top, top + dirv * 34 * s, new Color(170, 210, 120), 1.2f * s);
                }
            }
        // cacho de bananas e o "coração" roxo pendurado
        var bunch = top + new Vector2(7 * s, 8 * s);
        for (int i = 0; i < 4; i++) DiscRot(sb, bunch + new Vector2((i % 2) * 3 * s, i * 2.6f * s), 4 * s, 1.8f * s, 0.5f, new Color(200, 210, 70));
        sb.Disc(bunch + new Vector2(2 * s, 13 * s), 2.6f * s, 3.6f * s, new Color(120, 36, 70));
    }

    public static void Igreja(SpriteBatch sb, Vector2 p, float s)
    {
        // igreja barroca colonial de Minas: paredes caiadas, cunhais de pedra-sabão, duas torres e óculo
        var wall = new Color(242, 238, 226);
        var stone = new Color(176, 156, 112);
        var dark = new Color(58, 46, 38);
        var roof = new Color(178, 92, 60);
        sb.Disc(p + new Vector2(14 * s, 0), 58 * s, 12 * s, Color.Black * 0.22f);
        // telhado da nave por trás
        sb.Triangle(p + new Vector2(-30 * s, -44 * s), p + new Vector2(30 * s, -44 * s), p + new Vector2(0, -66 * s), dark);
        // nave
        sb.Fill(p + new Vector2(-28 * s, -48 * s), new Vector2(56 * s, 48 * s), dark);
        sb.Fill(p + new Vector2(-26 * s, -46 * s), new Vector2(52 * s, 46 * s), wall);
        // frontão com volutas sugeridas
        sb.Triangle(p + new Vector2(-29 * s, -46 * s), p + new Vector2(29 * s, -46 * s), p + new Vector2(0, -70 * s), dark);
        sb.Triangle(p + new Vector2(-25 * s, -47 * s), p + new Vector2(25 * s, -47 * s), p + new Vector2(0, -67 * s), wall);
        sb.Disc(p + new Vector2(-24 * s, -48 * s), 4 * s, 3 * s, stone);
        sb.Disc(p + new Vector2(24 * s, -48 * s), 4 * s, 3 * s, stone);
        sb.Disc(p + new Vector2(0, -55 * s), 5 * s, 5 * s, stone);
        sb.Disc(p + new Vector2(0, -55 * s), 3.2f * s, 3.2f * s, new Color(70, 90, 120));
        // cruz no alto
        sb.Line(p + new Vector2(0, -70 * s), p + new Vector2(0, -82 * s), dark, 2.4f * s);
        sb.Line(p + new Vector2(-4 * s, -77 * s), p + new Vector2(4 * s, -77 * s), dark, 2.4f * s);
        // torres
        foreach (var side in new[] { -1, 1 })
        {
            float x0 = side < 0 ? -44 * s : 26 * s;
            sb.Fill(p + new Vector2(x0 - 1.5f * s, -76 * s), new Vector2(19 * s, 76 * s), dark);
            sb.Fill(p + new Vector2(x0, -74 * s), new Vector2(16 * s, 74 * s), wall);
            sb.Fill(p + new Vector2(x0, -74 * s), new Vector2(3 * s, 74 * s), stone);
            sb.Fill(p + new Vector2(x0 + 13 * s, -74 * s), new Vector2(3 * s, 74 * s), stone);
            sb.Fill(p + new Vector2(x0, -50 * s), new Vector2(16 * s, 3 * s), stone);
            // sineira com sino dourado
            sb.Fill(p + new Vector2(x0 + 4.5f * s, -68 * s), new Vector2(7 * s, 11 * s), dark);
            sb.Disc(p + new Vector2(x0 + 8 * s, -62 * s), 2.6f * s, 3 * s, new Color(230, 180, 60));
            // cúpula arredondada com pináculo
            sb.Disc(p + new Vector2(x0 + 8 * s, -76 * s), 10 * s, 7 * s, dark);
            sb.Disc(p + new Vector2(x0 + 8 * s, -76 * s), 8.5f * s, 5.8f * s, roof);
            sb.Disc(p + new Vector2(x0 + 6 * s, -78 * s), 3.5f * s, 2 * s, new Color(214, 140, 96));
            sb.Line(p + new Vector2(x0 + 8 * s, -82 * s), p + new Vector2(x0 + 8 * s, -89 * s), dark, 2 * s);
            // janela da torre
            sb.Fill(p + new Vector2(x0 + 5.5f * s, -40 * s), new Vector2(5 * s, 8 * s), new Color(70, 90, 120));
        }
        // portada de pedra e porta de madeira com arco
        sb.Fill(p + new Vector2(-10 * s, -26 * s), new Vector2(20 * s, 26 * s), stone);
        sb.Fill(p + new Vector2(-7 * s, -21 * s), new Vector2(14 * s, 21 * s), new Color(104, 62, 36));
        sb.Disc(p + new Vector2(0, -21 * s), 7 * s, 4 * s, new Color(104, 62, 36));
        sb.Line(p + new Vector2(0, -24 * s), p + new Vector2(0, 0), new Color(70, 40, 24), 1.2f * s);
        // janelas do coro
        foreach (var side in new[] { -1, 1 })
            sb.Fill(p + new Vector2(side * 17 * s - 3 * s, -38 * s), new Vector2(6 * s, 8 * s), new Color(70, 90, 120));
        // adro (escadaria)
        sb.Fill(p + new Vector2(-30 * s, -2 * s), new Vector2(60 * s, 5 * s), stone);
        sb.Fill(p + new Vector2(-30 * s, -2 * s), new Vector2(60 * s, 1.2f * s), Darken(stone, 0.2f));
    }

    public static void VitoriaRegia(SpriteBatch sb, Vector2 p, float s, float time, int v)
    {
        var q = p + new Vector2(0, MathF.Sin(time * 1.4f + v) * 0.8f);
        sb.Disc(q + new Vector2(1.5f, 1.5f), 14 * s, 8 * s, new Color(20, 60, 70) * 0.35f);
        sb.Disc(q, 14 * s, 8 * s, new Color(30, 92, 42));
        sb.Disc(q, 12.5f * s, 7 * s, new Color(70, 156, 62));
        sb.Ellipse(q, 13 * s, 7.4f * s, new Color(150, 200, 96));
        for (int i = 0; i < 5; i++)
        {
            float a = i * MathHelper.TwoPi / 5 + v;
            sb.Line(q, q + new Vector2(MathF.Cos(a) * 10 * s, MathF.Sin(a) * 5.5f * s), new Color(104, 178, 80), 1);
        }
        if (v % 3 == 0)
        {
            var f = q + new Vector2(5 * s, -2 * s);
            Color petal = v % 2 == 0 ? new Color(250, 246, 246) : new Color(246, 170, 200);
            for (int i = 0; i < 6; i++)
            {
                float a = i * MathHelper.TwoPi / 6;
                sb.Disc(f + new Vector2(MathF.Cos(a) * 3 * s, MathF.Sin(a) * 1.8f * s - 1.5f * s), 2.4f * s, 1.8f * s, petal);
            }
            sb.Disc(f + new Vector2(0, -2 * s), 1.8f * s, 1.5f * s, new Color(250, 210, 70));
        }
    }

    public static void Capivara(SpriteBatch sb, Vector2 p, float s, float time, int v, float dir)
    {
        bool grazing = MathF.Sin(time * 0.5f + v) > 0.2f;
        float bob = grazing ? MathF.Abs(MathF.Sin(time * 3 + v)) * 1.5f * s : 0;
        var fur = new Color(146, 102, 64);
        var furD = new Color(102, 68, 42);
        sb.Disc(p + new Vector2(2 * s, 1), 13 * s, 3.5f * s, Color.Black * 0.2f);
        foreach (var lx in new[] { -7f, -3f, 4f, 8f })
            sb.Line(p + new Vector2(lx * dir * s, -3 * s), p + new Vector2(lx * dir * s, 0), furD, 2.6f * s);
        sb.Disc(p + new Vector2(0, -7 * s), 12.2f * s, 7.4f * s, Outline);
        sb.Disc(p + new Vector2(0, -7 * s), 11 * s, 6.4f * s, fur);
        sb.Disc(p + new Vector2(-2 * dir * s, -10 * s), 7 * s, 2.4f * s, new Color(170, 124, 82));
        var head = p + new Vector2(10 * dir * s, (grazing ? -4 : -9) * s + bob);
        sb.Disc(head, 6.6f * s, 5.4f * s, Outline);
        sb.Disc(head, 5.6f * s, 4.5f * s, fur);
        sb.Disc(head + new Vector2(4 * dir * s, 1 * s), 3.2f * s, 3 * s, furD);   // focinho largo
        sb.Disc(head + new Vector2(-2 * dir * s, -4 * s), 1.6f * s, 1.4f * s, furD); // orelha
        sb.Disc(head + new Vector2(1.5f * dir * s, -1.5f * s), 0.9f * s, 0.9f * s, Color.Black);
    }

    public static void Boi(SpriteBatch sb, Vector2 p, float s, float time, int v, float dir)
    {
        // gado de corte dos pampas: pelo avermelhado, cara branca
        bool grazing = MathF.Sin(time * 0.4f + v) > 0;
        var hide = (v & 1) == 0 ? new Color(150, 72, 40) : new Color(70, 52, 44);
        var hideD = Darken(hide, 0.3f);
        sb.Disc(p + new Vector2(3 * s, 1), 17 * s, 4 * s, Color.Black * 0.2f);
        float g = MathF.Sin(time * 2 + v) * 0.5f;
        foreach (var lx in new[] { -11f, -6f, 6f, 11f })
        {
            sb.Line(p + new Vector2(lx * dir * s, -8 * s), p + new Vector2(lx * dir * s, 0), Outline, 4 * s);
            sb.Line(p + new Vector2(lx * dir * s, -8 * s), p + new Vector2(lx * dir * s, -0.5f), hideD, 2.6f * s);
        }
        sb.Line(p + new Vector2(-15 * dir * s, -12 * s), p + new Vector2((-18 * dir) * s + g, -3 * s), hideD, 1.6f * s);
        sb.Disc(p + new Vector2(0, -12 * s), 16.4f * s, 8.4f * s, Outline);
        sb.Disc(p + new Vector2(0, -12 * s), 15 * s, 7.2f * s, hide);
        sb.Disc(p + new Vector2(2 * dir * s, -8 * s), 8 * s, 2.6f * s, new Color(236, 230, 214));   // barriga branca
        sb.Disc(p + new Vector2(-3 * dir * s, -16 * s), 9 * s, 2.2f * s, Lighten(hide, 0.15f));
        var head = p + new Vector2(16 * dir * s, (grazing ? -6 : -15) * s);
        sb.Disc(head, 6.4f * s, 5.4f * s, Outline);
        sb.Disc(head, 5.2f * s, 4.4f * s, new Color(240, 236, 226));
        sb.Disc(head + new Vector2(3 * dir * s, 2 * s), 2.8f * s, 2.2f * s, new Color(214, 160, 150)); // focinho
        sb.Line(head + new Vector2(-2 * dir * s, -4 * s), head + new Vector2(-6 * dir * s, -8 * s), new Color(230, 222, 196), 1.6f * s);
        sb.Line(head + new Vector2(1 * dir * s, -4 * s), head + new Vector2(4 * dir * s, -8 * s), new Color(230, 222, 196), 1.6f * s);
        sb.Disc(head + new Vector2(0, -1.5f * s), 0.9f * s, 0.9f * s, Color.Black);
    }

    public static void Arara(SpriteBatch sb, Vector2 p, float dir, float phase, int v, float s = 1f)
    {
        Vector2 V(float x, float y) => new(x * s, y * s);
        // arara-vermelha: corpo vermelho, asas com amarelo e azul, cauda longa
        float flap = MathF.Sin(phase);
        var red = new Color(220, 40, 36);
        var blue = new Color(40, 90, 200);
        var yellow = new Color(250, 206, 50);
        sb.Line(p - V(dir * 6, 0), p - V(dir * 22, -4), blue, 2.4f * s);
        sb.Line(p - V(dir * 6, 0), p - V(dir * 20, -1), red, 1.6f * s);
        var wingTip = p + V(-dir * 4, -12 * flap);
        sb.Triangle(p + V(-dir * 4, -1), p + V(dir * 4, -1), wingTip + V(-dir * 6, 0), blue);
        sb.Triangle(p + V(-dir * 2, -1), p + V(dir * 3, -1), wingTip * 0.5f + p * 0.5f, yellow);
        sb.Disc(p, 7 * s, 3.4f * s, red);
        sb.Disc(p + V(dir * 7, -1.5f), 3.2f * s, 2.8f * s, red);
        sb.Disc(p + V(dir * 8, -2.5f), 1.6f * s, 1.4f * s, Color.White);
        sb.Line(p + V(dir * 9.5f, -1.5f), p + V(dir * 12, 0.5f), new Color(40, 34, 30), 2.2f * s);
        if (flap < 0) sb.Triangle(p + V(-dir * 3, 0), p + V(dir * 3, 0), p + V(-dir * 7, 10 * -flap), Darken(blue, 0.25f));
    }

    /// <summary>Embaralha bits (espalha detalhes sem padrão visível).</summary>
    static int Hash(int a)
    {
        uint x = (uint)a * 2654435761u;
        x ^= x >> 15; x *= 2246822519u; x ^= x >> 13;
        return (int)(x & 0x7fffffff);
    }

    static Color Darken(Color c, float k) => new((int)(c.R * (1 - k)), (int)(c.G * (1 - k)), (int)(c.B * (1 - k)), c.A);
    static Color Lighten(Color c, float k) => new((int)(c.R + (255 - c.R) * k), (int)(c.G + (255 - c.G) * k), (int)(c.B + (255 - c.B) * k), c.A);
}
