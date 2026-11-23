using GuerraDasBandeiras.Core;

namespace GuerraDasBandeiras.Gameplay;

public class WorldRenderer
{
    readonly World _w;
    const int T = World.T;
    readonly List<(float y, Entity e)> _sorted = new();
    Texture2D _fogTex;
    Color[] _fogData;

    static readonly int[] ForestTiles = { 42, 43, 44, 46, 47, 48 };
    static readonly Color FogDark = new(12, 20, 26);
    static readonly Color Shore = new(214, 190, 128);
    static readonly Color Foam = new(235, 248, 255);
    Color GrassEdge = new(80, 186, 92);
    readonly Texture2D _toneTex;
    readonly List<Vector2> _dirt = new();
    static readonly Color RoadEdge = new(150, 112, 70);
    static readonly Color RoadFill = new(196, 156, 104);

    public WorldRenderer(World w)
    {
        _w = w;
        // cor média da grama, para as transições combinarem com o tile
        var g = Assets.Tile(57);
        var d = new Color[g.Width * g.Height];
        g.GetData(d);
        long r = 0, gg = 0, b = 0;
        foreach (var c in d) { r += c.R; gg += c.G; b += c.B; }
        GrassEdge = new Color((int)(r / d.Length), (int)(gg / d.Length), (int)(b / d.Length));
        _toneTex = BuildToneTexture();
        // manchas de terra batida nas bases: carimbos sobrepostos em vez de quadrados
        var rng = new Random(w.Config.Seed);
        for (int x = 0; x < w.W; x++)
            for (int y = 0; y < w.H; y++)
                if (w.Ter[x, y] == Terrain.Dirt)
                    _dirt.Add(w.CenterOf(new Point(x, y)) + new Vector2(rng.Next(-10, 11), rng.Next(-10, 11)));
    }

    /// <summary>
    /// Variação suave de tonalidade (ruído de baixa frequência, determinístico pela seed do mapa),
    /// desenhada esticada com filtro linear: quebra a repetição da grama sem custo por quadro.
    /// </summary>
    Texture2D BuildToneTexture()
    {
        int k = 3, tw = _w.W * k, th = _w.H * k;
        var data = new Color[tw * th];
        float seed = _w.Config.Seed % 1000;
        for (int y = 0; y < th; y++)
            for (int x = 0; x < tw; x++)
            {
                float fx = x / (float)k, fy = y / (float)k;
                float n = MathF.Sin(fx * 0.23f + seed) * MathF.Cos(fy * 0.29f - seed * 0.7f) * 0.6f
                        + MathF.Sin((fx + fy) * 0.11f + seed * 1.3f) * 0.4f
                        + MathF.Sin(fx * 0.71f - fy * 0.53f + seed) * 0.18f;
                var ter = _w.TerrainAt((int)fx, (int)fy);
                if (ter is Terrain.Water or Terrain.Ford) { data[y * tw + x] = Color.Transparent; continue; }
                float a = Math.Clamp(MathF.Abs(n), 0, 1) * (ter == Terrain.Grass ? 0.16f : 0.08f);
                data[y * tw + x] = (n > 0 ? new Color(236, 226, 120) : new Color(8, 46, 22)) * a;
            }
        var t = new Texture2D(Game1.I.GraphicsDevice, tw, th);
        t.SetData(data);
        return t;
    }

    Texture2D TileTex(int x, int y)
    {
        int v = _w.TileVar[x, y];
        return _w.Ter[x, y] switch
        {
            Terrain.Forest => Assets.Tile(ForestTiles[v % ForestTiles.Length]),
            Terrain.Hill => Assets.Tile(15 + (v & 1)),
            Terrain.Water => Assets.Tile(27 + (v & 1)),
            Terrain.Ford => Assets.Tile(1 + (v & 1)),
            Terrain.Dirt => Assets.Tile(57 + (v & 1)), // terra batida é desenhada como manchas orgânicas por cima
            _ => Assets.Tile(57 + (v & 1)),
        };
    }

    bool IsWater(int x, int y) => _w.TerrainAt(x, y) == Terrain.Water;
    bool IsHill(int x, int y) => _w.TerrainAt(x, y) == Terrain.Hill;

    public void Draw(SpriteBatch sb, Camera cam, HashSet<Entity> selection, float time, Entity hover = null)
    {
        var view = cam.ViewRect;
        int x0 = Math.Max(0, (int)(view.X / T) - 1), y0 = Math.Max(0, (int)(view.Y / T) - 1);
        int x1 = Math.Min(_w.W - 1, (int)(view.Right / T) + 1), y1 = Math.Min(_w.H - 1, (int)(view.Bottom / T) + 2);

        // ===== Chão =====
        sb.Begin(transformMatrix: cam.Transform, samplerState: SamplerState.LinearClamp);
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                sb.Draw(TileTex(x, y), new Rectangle(x * T, y * T, T + 1, T + 1), Color.White);

        sb.Draw(_toneTex, new Rectangle(0, 0, _w.W * T, _w.H * T), Color.White);

        // caminhos de terra (só visuais): borda escura primeiro, miolo depois, para se fundirem
        foreach (var p in _dirt) if (view.Intersects(p.X - 60, p.Y - 60, 120, 120)) sb.Disc(p, 58, 48, RoadEdge * 0.8f);
        foreach (var p in _w.RoadPoints) if (view.Intersects(p.X - 30, p.Y - 30, 60, 60)) sb.Disc(p, 21, 14, RoadEdge * 0.75f);
        foreach (var p in _dirt) if (view.Intersects(p.X - 60, p.Y - 60, 120, 120)) sb.Disc(p, 53, 44, RoadFill);
        foreach (var p in _w.RoadPoints) if (view.Intersects(p.X - 30, p.Y - 30, 60, 60)) sb.Disc(p, 16, 10, RoadFill);

        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                DrawTileDetail(sb, x, y, time);
        Scenery.DrawWaterProps(sb, _w, view, time);

        foreach (var d in _w.Decors)
            if (view.Intersects(d.Pos.X - 40, d.Pos.Y - 40, 80, 80))
            {
                if (d.Structure) { sb.Disc(d.Pos + new Vector2(8, -2), 26 * d.Scale, 8 * d.Scale, Color.Black * 0.18f); sb.SpriteFeet(Assets.Structure(d.Sprite), d.Pos, 0.5f * d.Scale, Color.White); }
                else sb.SpriteFeet(Assets.Env(d.Sprite), d.Pos, 0.5f * d.Scale, Color.White);
            }
        Scenery.DrawLandProps(sb, _w, view, time);

        foreach (var f in _w.Flags) DrawFlagGround(sb, f, time);

        if (hover != null && !hover.Dead && !selection.Contains(hover))
        {
            var hc = (hover.Team == 0 ? Pal.GreenLight : hover.Team < 0 ? Pal.Gold : Pal.Red) * 0.7f;
            if (hover is Unit hu) sb.Ellipse(hu.Pos + new Vector2(0, 8), 17 * hu.Def.Scale, 8.5f * hu.Def.Scale, hc);
            else if (hover is ResourceNode hn) sb.Ellipse(hn.Pos + new Vector2(0, 16), 28, 12, hc);
            else if (hover is Building hb) { var r = hb.WorldRect; sb.Ellipse(new Vector2(r.X + r.W / 2, r.Bottom - 10), r.W * 0.55f, 22, hc); }
        }

        // anéis de seleção (pulsando de leve)
        float pulse = 1f + MathF.Sin(time * 6f) * 0.06f;
        foreach (var e in selection)
        {
            if (e.Dead) continue;
            var col = e.Team == 0 ? Pal.GreenLight : e.Team < 0 ? Pal.Gold : Pal.Red;
            if (e is Unit u) sb.Ellipse(u.Pos + new Vector2(0, 8), 18 * u.Def.Scale * pulse, 9 * u.Def.Scale * pulse, col);
            else if (e is Building b)
            {
                var r = b.WorldRect;
                sb.Ellipse(new Vector2(r.X + r.W / 2, r.Bottom - 10), r.W * 0.55f * pulse, 22 * pulse, col * 0.9f);
            }
            else if (e is ResourceNode n) sb.Ellipse(n.Pos + new Vector2(0, 14), 26, 12, col);
        }

        // sombras e marcadores de time (aliado = elipse, inimigo = losango: não depende só da cor)
        foreach (var u in _w.Units)
        {
            if (!view.Intersects(u.Pos.X - 30, u.Pos.Y - 50, 60, 70) || !_w.CanSee(0, u)) continue;
            var feet = u.Pos + new Vector2(0, 9);
            float s = u.Def.Scale * (u.Def.IsSiege ? 1.5f : 1f);
            sb.Disc(feet, 13 * s, 5.5f * s, Color.Black * 0.28f);
            if (u.Team == 0) sb.Ellipse(feet, 12 * s, 5.5f * s, u.Owner.Color * 0.8f);
            else Diamond(sb, feet, 14 * s, 7 * s, u.Owner.Color * 0.9f);
            if (u.AbilityActive) sb.Ellipse(feet, (16 + MathF.Sin(time * 10) * 2) * s, 8 * s, Pal.Gold);
        }
        sb.End();

        // ===== Objetos ordenados por profundidade =====
        _sorted.Clear();
        foreach (var r in _w.Resources)
            if (view.Intersects(r.Pos.X - 64, r.Pos.Y - 100, 128, 140) && _w.IsExplored(r)) _sorted.Add((r.SortY, r));
        foreach (var b in _w.Buildings)
            if (view.Intersects(b.WorldRect.X - 20, b.WorldRect.Y - 120, b.WorldRect.W + 40, b.WorldRect.H + 140) && (b.Team == 0 || _w.IsExplored(b))) _sorted.Add((b.SortY, b));
        foreach (var u in _w.Units)
            if (view.Intersects(u.Pos.X - 40, u.Pos.Y - 60, 80, 80) && (u.Team == 0 || _w.CanSee(0, u))) _sorted.Add((u.SortY, u));
        _sorted.Sort((a, b) => a.y.CompareTo(b.y));

        sb.Begin(transformMatrix: cam.Transform, samplerState: SamplerState.LinearClamp);
        // corpos caídos ficam por baixo de tudo
        foreach (var fx in _w.Effects)
            if (fx.Kind == Fx.Corpse && fx.Tex != null)
            {
                float k = fx.Life / fx.MaxLife;
                sb.Draw(fx.Tex, fx.Pos, null, fx.Color * Math.Min(1, k * 2.5f), fx.Rot * Math.Min(1, (1 - k) * 8), new Vector2(fx.Tex.Width / 2f, fx.Tex.Height), fx.Scale,
                    fx.Flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0);
            }

        foreach (var (_, e) in _sorted)
        {
            switch (e)
            {
                case ResourceNode n: DrawResource(sb, n, time); break;
                case Building b: DrawBuilding(sb, b, time); break;
                case Unit u: DrawUnit(sb, u, time); break;
            }
        }
        foreach (var f in _w.Flags) DrawFlagPole(sb, f, time);

        // projéteis em arco
        foreach (var p in _w.Projectiles)
        {
            var t = p.Target?.Pos ?? p.Pos;
            var dir = t - p.Pos;
            if (dir.LengthSquared() < 1) continue;
            dir.Normalize();
            float arc = p.Total > 0 ? MathF.Sin(MathHelper.Pi * Math.Clamp(p.Traveled / p.Total, 0, 1)) * Math.Min(40, p.Total * 0.15f) : 0;
            var pos = p.Pos - new Vector2(0, arc);
            if (!_w.Visible[Math.Clamp((int)(p.Pos.X / T), 0, _w.W - 1), Math.Clamp((int)(p.Pos.Y / T), 0, _w.H - 1)]) continue;
            sb.Disc(p.Pos + new Vector2(0, 14), 4, 2, Color.Black * 0.2f);
            sb.Line(pos - dir * 14, pos, new Color(90, 60, 30), 2.2f);
            sb.Line(pos - dir * 14, pos - dir * 10, Color.White * 0.9f, 3f);
            sb.Line(pos - dir * 3, pos, new Color(200, 200, 210), 2.6f);
        }

        // partículas
        foreach (var fx in _w.Effects)
        {
            float k = fx.Life / fx.MaxLife;
            switch (fx.Kind)
            {
                case Fx.Ring:
                    float s = fx.Size * (0.5f + (1 - k) * 0.9f);
                    sb.Ellipse(fx.Pos, s, s * 0.5f, fx.Color * k);
                    break;
                case Fx.Dust:
                    float ds = fx.Size * (1.3f - k * 0.5f);
                    sb.Disc(fx.Pos, ds, ds * 0.8f, fx.Color * (k * 0.45f));
                    break;
                case Fx.Smoke:
                    float ss = fx.Size * (1 + (1 - k) * 2.2f);
                    sb.Disc(fx.Pos, ss, ss, fx.Color * (k * 0.35f));
                    break;
                case Fx.Chip:
                    sb.Fill(fx.Pos - new Vector2(fx.Size / 2), new Vector2(fx.Size), fx.Color * Math.Min(1, k * 2));
                    break;
                case Fx.Spark:
                    sb.Line(fx.Pos, fx.Pos - fx.Vel * 0.02f, fx.Color * k, 2f);
                    break;
                case Fx.Leaf:
                {
                    var d = new Vector2(MathF.Cos(fx.Rot), MathF.Sin(fx.Rot)) * fx.Size;
                    sb.Line(fx.Pos - d, fx.Pos + d, fx.Color * Math.Min(1, k * 2), fx.Size * 0.9f);
                    break;
                }
            }
        }

        // revoadas de araras
        Scenery.DrawBirds(sb, _w, view, time);

        // sombras de nuvens passando
        for (int i = 0; i < 7; i++)
        {
            float span = _w.W * T + 900;
            float cx = ((i * 1733 + time * (10 + i * 2)) % span) - 450;
            float cy = (i * 911) % (_w.H * T);
            if (!view.Intersects(cx - 320, cy - 200, 640, 400)) continue;
            sb.Disc(new Vector2(cx, cy), 280, 150, Color.Black * 0.05f);
            sb.Disc(new Vector2(cx + 140, cy + 40), 200, 110, Color.Black * 0.04f);
        }
        sb.End();

        // ===== Névoa de guerra suave =====
        UpdateFogTexture();
        sb.Begin(transformMatrix: cam.Transform, samplerState: SamplerState.LinearClamp);
        sb.Draw(_fogTex, new Rectangle(-T - T / 2, -T - T / 2, (_w.W + 2) * T, (_w.H + 2) * T), Color.White);
        sb.Fill(new Rectangle(-3000, -3000, _w.W * T + 6000, 3000 - T), FogDark);
        sb.Fill(new Rectangle(-3000, _w.H * T + T, _w.W * T + 6000, 3000), FogDark);
        sb.Fill(new Rectangle(-3000, -T, 3000 - T, _w.H * T + 2 * T), FogDark);
        sb.Fill(new Rectangle(_w.W * T + T, -T, 3000, _w.H * T + 2 * T), FogDark);
        sb.End();

        // ===== Barras de vida, ícones e textos (sobre a névoa) =====
        sb.Begin(transformMatrix: cam.Transform, samplerState: SamplerState.LinearClamp);
        foreach (var (_, e) in _sorted)
        {
            bool sel = selection.Contains(e);
            if (e is Unit u && _w.CanSee(0, u))
            {
                float top = -44 * u.Def.Scale - (IsMounted(u) ? 10 : 0);
                if (sel || u.Hp < u.MaxHp)
                    sb.BarF(u.Pos + new Vector2(-14, top), new Vector2(28, 4), u.Hp / u.MaxHp, u.Team == 0 ? Pal.GreenLight : Pal.Red, Color.Black * 0.6f);
                // indicador de habilidade ativa: barra dourada com o tempo restante + ícone
                if (u.AbilityActive || u.CamoTime > 0)
                {
                    float frac = u.CamoTime > 0 ? u.CamoTime / Math.Max(0.1f, u.Def.Type == UnitType.VigiaDaMata ? u.Def.AbilityDur : u.Owner.Faction.PowerDur) : u.AbilityTime / Math.Max(0.1f, u.Def.AbilityDur);
                    var bp = u.Pos + new Vector2(-14, top + 6);
                    sb.BarF(bp, new Vector2(28, 3), frac, u.CamoTime > 0 ? Pal.GreenLight : Pal.Gold, Color.Black * 0.5f);
                    var ic = bp + new Vector2(-7, 1.5f);
                    if (u.StoneStance) { sb.Disc(ic, 4, 5, new Color(170, 178, 192)); sb.Ellipse(ic, 4, 5, new Color(80, 86, 98)); }
                    else if (u.CamoTime > 0) sb.Disc(ic, 3.5f, 3.5f, new Color(60, 150, 70));
                    else sb.Disc(ic, 3.5f, 3.5f, Pal.Gold);
                }
                if (u.StunTime > 0)
                    for (int i = 0; i < 3; i++)
                    {
                        float a = time * 6 + i * MathHelper.TwoPi / 3;
                        sb.Disc(u.Pos + new Vector2(MathF.Cos(a) * 10, top - 6 + MathF.Sin(a) * 3), 2.5f, 2.5f, Pal.Gold);
                    }
                // habilidade pronta: pequena estrela dourada para unidades especiais do jogador
                if (u.Team == 0 && u.HasAbility && u.AbilityReady)
                    sb.Disc(u.Pos + new Vector2(18, top + 2), 3.5f, 3.5f, Pal.Gold * (0.7f + 0.3f * MathF.Sin(time * 5)));
            }
            else if (e is Building b && (sel || b.Hp < b.MaxHp || !b.Done) && (b.Team == 0 || _w.CanSee(0, b)))
            {
                var r = b.WorldRect;
                float top = r.Y - 10;
                sb.BarF(new Vector2(r.X + 12, top), new Vector2(r.W - 24, 6), b.Hp / b.MaxHp, b.Team == 0 ? Pal.GreenLight : Pal.Red, Color.Black * 0.6f);
                if (!b.Done) sb.BarF(new Vector2(r.X + 12, top + 9), new Vector2(r.W - 24, 4), b.Progress, Pal.Gold, Color.Black * 0.6f);
                else if (b.Queue.Count > 0 && b.Team == 0) sb.BarF(new Vector2(r.X + 12, top + 9), new Vector2(r.W - 24, 4), b.ProdTimer / Defs.Units[b.Queue[0]].Time, Color.White, Color.Black * 0.6f);
            }
        }
        foreach (var f in _w.Flags)
        {
            sb.TextCenter(Assets.FontNarrow(15), f.Name, f.Pos + new Vector2(0, 56), Color.White);
            if (Math.Abs(f.Progress) > 0.5f && Math.Abs(f.Progress) < 100 || f.Contested)
            {
                var col = f.Progress > 0 ? _w.Players[0].Color : _w.Players[1].Color;
                sb.BarF(f.Pos + new Vector2(-30, 74), new Vector2(60, 5), Math.Abs(f.Progress) / 100f, col, Color.Black * 0.6f);
                if (f.Contested) sb.TextCenter(Assets.FontNarrow(13), "EM DISPUTA", f.Pos + new Vector2(0, 90), Pal.Gold);
            }
        }
        var ft = Assets.Font(15);
        foreach (var t in _w.Texts)
            sb.TextCenter(ft, t.Text, t.Pos, t.Color * Math.Min(1, t.Life * 2));
        sb.End();
    }

    // ---------------- Detalhes do terreno ----------------

    void DrawTileDetail(SpriteBatch sb, int x, int y, float time)
    {
        var ter = _w.Ter[x, y];
        int px = x * T, py = y * T;
        int v = _w.TileVar[x, y];

        if (ter == Terrain.Water)
        {
            // margens: faixa de areia do lado da terra e espuma do lado da água
            if (!IsWater(x, y - 1)) { sb.Fill(new Rectangle(px, py - 4, T, 6), Shore); sb.Fill(new Rectangle(px, py + 2, T, 3), Foam * 0.6f); }
            if (!IsWater(x, y + 1)) { sb.Fill(new Rectangle(px, py + T - 3, T, 7), Shore); sb.Fill(new Rectangle(px, py + T - 6, T, 3), Foam * 0.6f); }
            if (!IsWater(x - 1, y)) { sb.Fill(new Rectangle(px - 4, py, 6, T), Shore); sb.Fill(new Rectangle(px + 2, py, 3, T), Foam * 0.6f); }
            if (!IsWater(x + 1, y)) { sb.Fill(new Rectangle(px + T - 3, py, 7, T), Shore); sb.Fill(new Rectangle(px + T - 6, py, 3, T), Foam * 0.6f); }
            float s = MathF.Sin(time * 1.6f + x * 0.9f + y * 1.3f);
            if (s > 0.55f) sb.Fill(new Vector2(px + 14 + v % 20, py + 22 + s * 6), new Vector2(18, 2), Color.White * ((s - 0.55f) * 0.9f));
        }
        else if (ter == Terrain.Ford)
        {
            // água rasa por cima da areia e pedras de travessia
            float s = 0.22f + 0.05f * MathF.Sin(time * 2 + x + y);
            sb.Fill(new Rectangle(px, py, T, T), new Color(120, 190, 230) * s);
            for (int i = 0; i < 3; i++)
            {
                int h = (v * (i + 3) * 37) & 255;
                var p = new Vector2(px + 10 + h % 44, py + 10 + (h / 7) % 44);
                sb.Disc(p + new Vector2(0, 2), 7, 4, Color.Black * 0.15f);
                sb.Disc(p, 7, 4.5f, new Color(170, 165, 150));
                sb.Disc(p + new Vector2(-1, -1), 4, 2.5f, new Color(205, 200, 185));
            }
            float w = MathF.Sin(time * 2.4f + x * 1.7f + y);
            if (w > 0.6f) sb.Fill(new Vector2(px + 8 + v % 30, py + 30), new Vector2(14, 1.5f), Color.White * ((w - 0.6f) * 1.2f));
        }
        else if (ter == Terrain.Hill)
        {
            // relevo da serra: brilho no topo e paredão escuro embaixo
            if (!IsHill(x, y - 1)) sb.Fill(new Rectangle(px, py, T, 4), Color.White * 0.18f);
            if (!IsHill(x, y + 1))
            {
                sb.Fill(new Rectangle(px, py + T - 12, T, 12), new Color(90, 84, 74));
                sb.Fill(new Rectangle(px, py + T - 12, T, 2), new Color(70, 64, 56));
                sb.Fill(new Rectangle(px, py + T, T, 8), Color.Black * 0.18f);
            }
            if (!IsHill(x - 1, y)) sb.Fill(new Rectangle(px, py, 3, T), Color.Black * 0.12f);
            if (!IsHill(x + 1, y)) sb.Fill(new Rectangle(px + T - 3, py, 3, T), Color.Black * 0.12f);
        }
        else if (ter == Terrain.Dirt)
        {
            // pedrinhas e marcas de passos
            if ((v & 3) == 0)
            {
                var p = new Vector2(px + 12 + v % 40, py + 14 + (v >> 3) % 36);
                sb.Disc(p, 3.5f, 2.5f, new Color(120, 86, 56));
                sb.Disc(p + new Vector2(9, 5), 2.5f, 2f, new Color(165, 130, 95));
            }
        }
        else if (ter == Terrain.Grass)
        {
            // sombra suave na grama encostada na mata (as copas projetam sombra para baixo e para a direita)
            if (_w.TerrainAt(x, y - 1) == Terrain.Forest) sb.Fill(new Rectangle(px, py, T, 10), Color.Black * 0.10f);
            if (_w.TerrainAt(x - 1, y) == Terrain.Forest) sb.Fill(new Rectangle(px, py, 8, T), Color.Black * 0.08f);

            // segundo "sorteio" independente para espalhar os detalhes sem padrão visível
            int h = (x * 73856093) ^ (y * 19349663) ^ _w.Config.Seed;
            h = (h >> 7) & 1023;
            var p = new Vector2(px + 8 + h % 46, py + 14 + (h >> 4) % 40);
            switch (h % 9)
            {
                case 0:
                case 1:
                {
                    var c = (h & 16) == 0 ? new Color(62, 142, 66) : new Color(108, 182, 88);
                    sb.Line(p, p + new Vector2(-3, -7), c, 2);
                    sb.Line(p, p + new Vector2(0, -9), c, 2);
                    sb.Line(p, p + new Vector2(3, -7), c, 2);
                    break;
                }
                case 2:
                {
                    // florzinhas em grupo
                    var fc = (h & 32) != 0 ? new Color(250, 226, 110) : (h & 64) != 0 ? new Color(236, 120, 160) : new Color(245, 245, 250);
                    for (int i = 0; i < 3; i++) sb.Disc(p + new Vector2(i * 6 - 6, (i % 2) * 4), 2.2f, 2.2f, fc);
                    break;
                }
                case 3:
                    // pedrinhas
                    sb.Disc(p + new Vector2(1, 1), 3.5f, 2.5f, Color.Black * 0.2f);
                    sb.Disc(p, 3.5f, 2.5f, new Color(170, 170, 160));
                    sb.Disc(p + new Vector2(7, 3), 2.2f, 1.6f, new Color(150, 150, 140));
                    break;
                case 4:
                    // mancha de grama mais escura
                    sb.Disc(p, 14, 7, new Color(20, 90, 40) * 0.12f);
                    break;
            }
        }
    }

    void UpdateFogTexture()
    {
        int fw = _w.W + 2, fh = _w.H + 2;
        if (_fogTex == null)
        {
            _fogTex = new Texture2D(Game1.I.GraphicsDevice, fw, fh);
            _fogData = new Color[fw * fh];
        }
        for (int y = 0; y < fh; y++)
            for (int x = 0; x < fw; x++)
            {
                int tx = x - 1, ty = y - 1;
                float a;
                if (tx < 0 || ty < 0 || tx >= _w.W || ty >= _w.H) a = 1f;
                else a = !_w.Explored[tx, ty] ? 1f : !_w.Visible[tx, ty] ? 0.5f : 0f;
                _fogData[y * fw + x] = FogDark * a;
            }
        _fogTex.SetData(_fogData);
    }

    static void Diamond(SpriteBatch sb, Vector2 c, float rx, float ry, Color col)
    {
        var l = c - new Vector2(rx, 0); var r = c + new Vector2(rx, 0);
        var t = c - new Vector2(0, ry); var b = c + new Vector2(0, ry);
        sb.Line(l, t, col, 2); sb.Line(t, r, col, 2); sb.Line(r, b, col, 2); sb.Line(b, l, col, 2);
    }

    // ---------------- Objetos ----------------

    // Luz vem de cima-esquerda: todas as sombras deslocam um pouco para a direita.
    static readonly Vector2 ShadowOff = new(5, 0);

    void DrawResource(SpriteBatch sb, ResourceNode n, float time)
    {
        var feet = n.Pos + new Vector2(0, n.Kind == Res.Wood ? 22 : 16);
        float scale = n.DrawScale * (n.Kind == Res.Wood ? 1f : 0.85f + 0.15f * n.Amount / (float)n.Initial);
        var tex = n.Texture;
        float w = tex.Width * scale;
        sb.Disc(feet + ShadowOff + new Vector2(n.Kind == Res.Wood ? 6 : 2, -2), Math.Max(14, w * 0.55f), 6, Color.Black * 0.24f);
        float rot = n.Shake > 0 ? MathF.Sin(n.Shake * 60) * 0.06f : n.Kind == Res.Wood ? MathF.Sin(time * 1.3f + n.Id) * 0.015f : 0;
        sb.Draw(tex, feet, null, Color.White, rot, new Vector2(tex.Width / 2f, tex.Height), scale, SpriteEffects.None, 0);
        // brilho no minério: sinaliza "isto é coletável"
        float tw = MathF.Sin(time * 2.2f + n.Id * 1.7f);
        if (n.Kind == Res.Ore && tw > 0.75f)
        {
            var g = feet + new Vector2(-w * 0.2f + (n.Id % 7) * 3, -tex.Height * scale * 0.6f);
            float a = (tw - 0.75f) * 4;
            sb.Line(g - new Vector2(4, 0), g + new Vector2(4, 0), Color.White * a, 1.5f);
            sb.Line(g - new Vector2(0, 4), g + new Vector2(0, 4), Color.White * a, 1.5f);
        }
    }

    void DrawBuilding(SpriteBatch sb, Building b, float time)
    {
        var tex = b.Texture;
        var r = b.WorldRect;
        float scale = r.W * 0.95f / tex.Width;
        var fac = b.Owner.Faction.Id;
        // Amazonas: casas sobre palafitas (o prédio sobe um pouco)
        float lift = fac == FactionId.Amazonas && b.Done ? 8 : 0;
        var feet = new Vector2(r.X + r.W / 2, r.Bottom - 2 - lift);
        sb.Disc(new Vector2(feet.X, r.Bottom - 8) + ShadowOff * 1.5f, r.W * 0.52f, 16, Color.Black * 0.24f);
        Color tint = Color.White;
        if (b.HitFlash > 0) tint = Color.Lerp(tint, Color.Red, 0.4f);
        if (b.Team != 0 && !_w.CanSee(0, b)) tint = Color.Lerp(tint, Color.Gray, 0.4f);
        float sw = tex.Width * scale, sh = tex.Height * scale;

        if (b.Done) DrawFactionBase(sb, b, fac, feet, sw, lift);

        if (!b.Done)
        {
            // a construção "sobe" do chão conforme o progresso
            float shown = 0.2f + 0.8f * b.Progress;
            int srcH = Math.Max(1, (int)(tex.Height * shown));
            var src = new Rectangle(0, tex.Height - srcH, tex.Width, srcH);
            sb.Draw(tex, feet, src, tint * 0.9f, 0, new Vector2(tex.Width / 2f, srcH), scale, SpriteEffects.None, 0);
            var wood = new Color(140, 95, 50);
            float topY = feet.Y - sh * Math.Max(shown, 0.5f);
            for (int i = 0; i < 3; i++)
            {
                float x = r.X + 10 + i * (r.W - 20) / 2;
                sb.Line(new Vector2(x, r.Bottom - 4), new Vector2(x, topY), wood, 3);
            }
            sb.Line(new Vector2(r.X + 8, (r.Bottom + topY) / 2), new Vector2(r.Right - 8, (r.Bottom + topY) / 2), wood, 3);
            sb.Line(new Vector2(r.X + 8, topY + 4), new Vector2(r.Right - 8, topY + 4), wood, 3);
            float h = MathF.Abs(MathF.Sin(time * 8)) * 2;
            sb.TextCenter(Assets.Font(13), $"{b.Progress * 100:0}%", new Vector2(feet.X, topY - 14 - h), Pal.Gold);
        }
        else
        {
            sb.SpriteFeet(tex, feet, scale, tint);
            DrawFactionTop(sb, b, fac, feet, sw, sh);
        }

        // Fortificar (poder de Minas): contorno de pedra
        if (b.Owner.PowerActive && fac == FactionId.MinasGerais && b.Done)
            sb.Ellipse(new Vector2(feet.X, feet.Y - r.H * 0.3f), r.W * 0.62f, r.H * 0.55f, new Color(180, 200, 230) * (0.6f + 0.3f * MathF.Sin(time * 4)));

        // estandarte: bandeira do estado do dono, com ponteira na cor do time (distingue espelhos)
        var pole = new Vector2(r.Right - 14, r.Y + r.H * 0.25f);
        float ph = b.Size >= 3 ? 1.25f : 1f;
        sb.Line(pole + new Vector2(0, 2), pole + new Vector2(0, -36 * ph), new Color(70, 50, 30), 2.5f);
        sb.Disc(pole + new Vector2(0, -37 * ph), 3, 3, b.Owner.Color);
        FlagArt.DrawWaving(sb, b.Owner.Faction.Id, pole + new Vector2(1, -35 * ph), 21 * ph, 14 * ph, time, b.Id);
    }

    /// <summary>Detalhes por baixo do prédio: palafitas (AM), fundação de pedra (MG), cerca (RS).</summary>
    static void DrawFactionBase(SpriteBatch sb, Building b, FactionId fac, Vector2 feet, float sw, float lift)
    {
        var r = b.WorldRect;
        switch (fac)
        {
            case FactionId.Amazonas:
            {
                var post = new Color(96, 64, 36);
                int n = b.Size + 2;
                for (int i = 0; i < n; i++)
                {
                    float x = feet.X - sw * 0.42f + i * (sw * 0.84f / (n - 1));
                    sb.Line(new Vector2(x, feet.Y - 2), new Vector2(x, feet.Y + lift + 1), post, 3);
                }
                // passarela de tábuas saindo pela frente
                var deck = new Color(170, 120, 70);
                sb.Fill(new Vector2(feet.X - 10, feet.Y - 1), new Vector2(20, lift + 3), deck);
                for (int i = 0; i < 3; i++) sb.Line(new Vector2(feet.X - 10, feet.Y + 2 + i * 3), new Vector2(feet.X + 10, feet.Y + 2 + i * 3), post * 0.6f, 1);
                break;
            }
            case FactionId.MinasGerais:
            {
                // fundação de pedra com juntas
                var stone = new Color(150, 146, 136);
                var joint = new Color(110, 106, 98);
                float x0 = feet.X - sw * 0.5f, w = sw;
                sb.Fill(new Vector2(x0, feet.Y - 8), new Vector2(w, 10), stone);
                for (float x = x0 + 8; x < x0 + w; x += 16) sb.Line(new Vector2(x, feet.Y - 8), new Vector2(x, feet.Y + 2), joint, 1.5f);
                sb.Line(new Vector2(x0, feet.Y - 3), new Vector2(x0 + w, feet.Y - 3), joint, 1.2f);
                break;
            }
            case FactionId.RioGrandeDoSul:
            {
                // cerca de estância na frente
                var wood = new Color(120, 82, 46);
                float y = r.Bottom + 4, xl = r.X - 4, xr = r.Right + 4;
                for (float x = xl; x <= xr; x += 14) sb.Line(new Vector2(x, y), new Vector2(x, y - 12), wood, 2.5f);
                sb.Line(new Vector2(xl, y - 9), new Vector2(xr, y - 9), wood, 2);
                sb.Line(new Vector2(xl, y - 4), new Vector2(xr, y - 4), wood, 2);
                break;
            }
        }
    }

    /// <summary>Detalhes por cima: folhagem (AM), carrinho de minério (MG), fardos de feno (RS).</summary>
    static void DrawFactionTop(SpriteBatch sb, Building b, FactionId fac, Vector2 feet, float sw, float sh)
    {
        switch (fac)
        {
            case FactionId.Amazonas:
            {
                var leaf = new Color(46, 120, 60);
                var leaf2 = new Color(70, 150, 74);
                for (int i = 0; i < 4; i++)
                {
                    float x = feet.X - sw * 0.45f + i * sw * 0.3f;
                    float y = feet.Y - sh * (0.62f + (i % 2) * 0.08f);
                    sb.Disc(new Vector2(x, y), 7, 5, leaf);
                    sb.Disc(new Vector2(x + 4, y - 3), 5, 4, leaf2);
                }
                sb.Line(new Vector2(feet.X - sw * 0.46f, feet.Y - sh * 0.6f), new Vector2(feet.X - sw * 0.48f, feet.Y - sh * 0.3f), leaf, 2);
                break;
            }
            case FactionId.MinasGerais:
                if (b.Def.Type is BuildingType.Centro or BuildingType.Coletor or BuildingType.Oficina)
                {
                    var cart = feet + new Vector2(sw * 0.42f + 10, -2);
                    sb.Disc(cart + new Vector2(-7, 0), 3.5f, 3.5f, new Color(50, 50, 55));
                    sb.Disc(cart + new Vector2(7, 0), 3.5f, 3.5f, new Color(50, 50, 55));
                    sb.Fill(cart + new Vector2(-11, -12), new Vector2(22, 9), new Color(110, 80, 50));
                    sb.Disc(cart + new Vector2(-4, -13), 5, 4, new Color(170, 170, 175));
                    sb.Disc(cart + new Vector2(4, -14), 4, 3.5f, new Color(200, 180, 90));
                }
                break;
            case FactionId.RioGrandeDoSul:
                if (b.Def.Type is BuildingType.Centro or BuildingType.Quartel or BuildingType.Coletor)
                {
                    var hay = new Color(222, 186, 92);
                    var hayD = new Color(176, 140, 60);
                    var p = feet + new Vector2(-sw * 0.5f - 12, -4);
                    sb.Disc(p, 10, 7, hayD);
                    sb.Disc(p + new Vector2(0, -2), 9, 6, hay);
                    sb.Disc(p + new Vector2(14, 2), 10, 7, hayD);
                    sb.Disc(p + new Vector2(14, 0), 9, 6, hay);
                }
                break;
        }
    }

    static bool IsMounted(Unit u) => u.Def.Type == UnitType.LanceiroDosPampas;

    void DrawUnit(SpriteBatch sb, Unit u, float time)
    {
        if (u.Def.IsSiege) { DrawRam(sb, u, time); return; }

        var tex = Defs.UnitSprite(u.Def, u.Owner.ColorSet, u.Owner.Faction.Id);
        float scale = Defs.SpriteScale(u.Def) * u.Def.Scale;
        bool camoOwn = u.Team == 0 && (u.CamoTime > 0 || (u.Def.Tags.HasFlag(Tag.Stealth) && u.RevealTimer <= 0 && u.InForest));
        // Camuflagem: some aos poucos (0,6 s) até ficar translúcido; furtivos na mata ficam meio apagados
        float camoAlpha = 1f;
        if (u.CamoTime > 0) { float ramp = Math.Min(1f, (u.Def.AbilityDur - u.CamoTime) / 0.6f); camoAlpha = MathHelper.Lerp(1f, 0.38f, Math.Clamp(ramp, 0, 1)); }
        else if (camoOwn) camoAlpha = 0.45f;
        var col = camoOwn ? Color.White * (camoAlpha + 0.06f * MathF.Sin(time * 5)) : Color.White;
        if (u.StoneStance) col = Color.Lerp(col, new Color(160, 170, 185), 0.35f);
        if (u.HitFlash > 0) col = Color.Lerp(col, new Color(255, 80, 80), 0.6f);

        // passos, avanço no golpe, tranco ao ser atingido
        float bob = u.MovedThisFrame ? MathF.Abs(MathF.Sin(u.AnimTime * 12f)) * 3f : 0f;
        float dir = u.FacingLeft ? -1 : 1;
        var lunge = new Vector2(dir * u.AttackAnim * 6f - dir * u.HitFlash * 12f, 0);
        var feet = u.Pos + new Vector2(0, 9 - bob) + lunge;
        float lean = u.AttackAnim * 0.25f * dir;

        if (IsMounted(u))
        {
            DrawHorse(sb, feet, dir, u.MovedThisFrame ? u.AnimTime : 0, col);
            feet += new Vector2(dir * 2, -14);
        }
        float breath = u.MovedThisFrame || u.AttackAnim > 0 ? 0 : MathF.Sin(time * 2.2f + u.Id) * 0.025f; // respiração parada
        float bodyH = tex.Height * scale;

        sb.Draw(tex, feet, null, col, lean, new Vector2(tex.Width / 2f, tex.Height), new Vector2(scale, scale * (1 + breath)), u.FacingLeft ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);

        // arco do golpe (corpo a corpo) e recuo do arco (atiradores): leitura clara de quem está batendo
        if (u.AttackAnim > 0.05f && u.Order == UnitOrder.Attack)
        {
            float k = u.AttackAnim;
            var c0 = feet + new Vector2(dir * 10, -bodyH * 0.55f);
            if (!u.Def.IsRanged && !u.IsWorker)
                for (int i = 0; i < 5; i++)
                {
                    float a0 = (-1.2f + i * 0.45f) * (1 - k * 0.3f);
                    float a1 = a0 + 0.4f;
                    var p0 = c0 + new Vector2(dir * MathF.Cos(a0), MathF.Sin(a0)) * 16;
                    var p1 = c0 + new Vector2(dir * MathF.Cos(a1), MathF.Sin(a1)) * 16;
                    sb.Line(p0, p1, Color.White * (k * 0.7f * (i + 1) / 5f), 2.5f);
                }
            else if (u.Def.IsRanged)
                sb.Line(c0, c0 + new Vector2(dir * 14 * (1 - k), -3), new Color(230, 220, 200) * k, 1.5f);
        }

        // chapéus, cocares, elmos e ponchos fazem parte do sprite gerado (Core/CharArt)
        if (u.Def.Type == UnitType.LanceiroDosPampas)
        {
            // lança longa com flâmula
            var hand = feet + new Vector2(dir * 6, -16);
            var tip = hand + new Vector2(dir * 30, -14);
            sb.Line(hand - new Vector2(dir * 10, -5), tip, new Color(120, 86, 50), 2.5f);
            sb.Line(tip, tip + new Vector2(dir * 6, -3), new Color(210, 210, 220), 2.5f);
            var flag = tip - new Vector2(dir * 6, -2);
            // flâmula com a bandeira do estado: tremula mais rápido durante a Investida
            FlagArt.DrawWaving(sb, u.Owner.Faction.Id, new Vector2(dir > 0 ? flag.X - 12 : flag.X, flag.Y - 1), 12, 8, time * (u.AbilityActive ? 2.2f : 1f), u.Id);
            if (u.AbilityActive && u.MovedThisFrame)
                for (int i = 0; i < 3; i++)
                {
                    var o = feet + new Vector2(-dir * (26 + i * 6), -8 - i * 7);
                    sb.Line(o, o - new Vector2(dir * (14 + i * 4), 0), Color.White * 0.55f, 1.5f);
                }
        }

        if (u.Def.Type == UnitType.GuardiaoDasGerais)
        {
            // escudo pesado sempre à mostra; maior e mais claro na Postura de Pedra
            bool st = u.StoneStance;
            var sc = feet + new Vector2(dir * (st ? 13 : 11), st ? -20 : -17);
            float rx = st ? 10 : 7.5f, ry = st ? 14 : 10.5f;
            sb.Disc(sc + new Vector2(1, 1), rx, ry, Color.Black * 0.25f);
            sb.Disc(sc, rx, ry, st ? new Color(170, 178, 192) : new Color(140, 146, 158));
            sb.Ellipse(sc, rx, ry, new Color(80, 86, 98));
            if (st)
            {
                // contorno brilhante pulsando e reflexo no metal
                float g = 0.55f + 0.45f * MathF.Sin(time * 7 + u.Id);
                sb.Ellipse(sc, rx + 2, ry + 2, new Color(235, 240, 255) * g);
                if (((int)(time * 2 + u.Id) % 3) == 0) sb.Line(sc + new Vector2(-3, -6), sc + new Vector2(2, -9), Color.White * g, 2);
            }
            sb.Disc(sc, 2.5f, 2.5f, u.Owner.Color);
        }
        if (u.Cargo > 0 && u.Team == 0)
            sb.SpriteFeet(Defs.ResIcon(u.CargoKind), u.Pos + new Vector2(u.FacingLeft ? 10 : -10, -22), 0.35f, Color.White);
    }

    public static void DrawHorse(SpriteBatch sb, Vector2 feet, float dir, float anim, Color tint)
    {
        var horse = new Color(126, 82, 46).MultiplyAlpha(tint);
        var dark = new Color(84, 54, 30).MultiplyAlpha(tint);
        var mane = new Color(50, 34, 22).MultiplyAlpha(tint);
        float g = anim > 0 ? MathF.Sin(anim * 16f) : 0;
        var outline = new Color(40, 26, 16).MultiplyAlpha(tint);
        var hoof = new Color(36, 30, 26).MultiplyAlpha(tint);
        // pernas (com contorno e cascos escuros)
        (float x, float sw, Color c)[] legs = { (-11, g, dark), (-6, -g, horse), (7, -g, dark), (12, g, horse) };
        foreach (var (x, sw, c) in legs)
        {
            var top = feet + new Vector2(dir * x, -10);
            var bottom = feet + new Vector2(dir * x + sw * 4, 0);
            sb.Line(top, bottom, outline, 4.2f);
            sb.Line(top, bottom, c, 2.8f);
            sb.Disc(bottom + new Vector2(0, -0.5f), 2.4f, 1.6f, hoof);
        }
        // rabo, corpo, pescoço, cabeça (contorno primeiro)
        sb.Line(feet + new Vector2(-dir * 16, -16), feet + new Vector2(-dir * 22, -6 + g * 2), outline, 4.5f);
        sb.Line(feet + new Vector2(-dir * 16, -16), feet + new Vector2(-dir * 22, -6 + g * 2), mane, 3);
        sb.Disc(feet + new Vector2(0, -14), 18.2f, 8.7f, outline);
        sb.Line(feet + new Vector2(dir * 12, -16), feet + new Vector2(dir * 19, -27), outline, 8);
        sb.Disc(feet + new Vector2(dir * 22, -28), 7.6f, 5.6f, outline);
        sb.Disc(feet + new Vector2(0, -14), 17, 7.5f, horse);
        sb.Disc(feet + new Vector2(-dir * 4, -17), 10, 3, Color.White.MultiplyAlpha(tint) * 0.12f); // brilho no lombo
        sb.Line(feet + new Vector2(dir * 12, -16), feet + new Vector2(dir * 19, -27), horse, 6);
        sb.Disc(feet + new Vector2(dir * 22, -28), 6.5f, 4.5f, horse);
        sb.Disc(feet + new Vector2(dir * 26, -27), 3, 2.6f, dark);                      // focinho
        sb.Disc(feet + new Vector2(dir * 21, -30), 1.2f, 1.2f, hoof);                   // olho
        sb.Line(feet + new Vector2(dir * 19, -32), feet + new Vector2(dir * 18, -35), dark, 2);   // orelha
        sb.Line(feet + new Vector2(dir * 14, -20), feet + new Vector2(dir * 18, -31), mane, 2.5f);
        // rédeas até a mão do cavaleiro e peitoral de couro
        var leather = new Color(70, 40, 24).MultiplyAlpha(tint);
        sb.Line(feet + new Vector2(dir * 25, -27), feet + new Vector2(dir * 7, -26), leather, 1.2f);
        sb.Line(feet + new Vector2(dir * 13, -12), feet + new Vector2(dir * 16, -20), leather, 1.6f);
        // arreio gaúcho: baixeiro listrado e pelego de lã branca por cima
        sb.Fill(feet + new Vector2(-9, -20), new Vector2(18, 6), new Color(150, 40, 30).MultiplyAlpha(tint));
        sb.Fill(feet + new Vector2(-9, -17), new Vector2(18, 1.5f), new Color(214, 168, 70).MultiplyAlpha(tint));
        sb.Disc(feet + new Vector2(0, -21), 8, 3.4f, new Color(238, 232, 214).MultiplyAlpha(tint));
        sb.Disc(feet + new Vector2(-3, -22), 3, 1.5f, Color.White.MultiplyAlpha(tint));
        // estribo
        sb.Line(feet + new Vector2(dir * 2, -17), feet + new Vector2(dir * 2, -11), leather, 1);
        sb.Fill(feet + new Vector2(dir * 2 - 1.5f, -11), new Vector2(3, 1.5f), new Color(200, 200, 210).MultiplyAlpha(tint));
    }

    void DrawRam(SpriteBatch sb, Unit u, float time)
    {
        float dir = u.FacingLeft ? -1 : 1;
        var feet = u.Pos + new Vector2(dir * u.AttackAnim * 10f, 9);
        var wood = new Color(150, 102, 58);
        var dark = new Color(96, 64, 36);
        var col = u.HitFlash > 0 ? Color.Lerp(Color.White, Color.Red, 0.5f) : Color.White;
        // rodas
        float spin = u.MovedThisFrame ? u.AnimTime * 8 : 0;
        foreach (var wx in new[] { -14f, 14f })
        {
            var c = feet + new Vector2(wx, -6);
            sb.Disc(c, 7, 7, dark.MultiplyAlpha(col));
            sb.Line(c - new Vector2(MathF.Cos(spin), MathF.Sin(spin)) * 6, c + new Vector2(MathF.Cos(spin), MathF.Sin(spin)) * 6, wood, 2);
        }
        // estrutura e teto
        sb.Fill(feet + new Vector2(-22, -20), new Vector2(44, 10), wood.MultiplyAlpha(col));
        sb.Line(feet + new Vector2(-22, -20), feet + new Vector2(0, -38), dark, 4);
        sb.Line(feet + new Vector2(22, -20), feet + new Vector2(0, -38), dark, 4);
        sb.Fill(feet + new Vector2(-6, -34), new Vector2(12, 14), u.Owner.Color);
        // tronco
        var log = Assets.Env(5);
        sb.Draw(log, feet + new Vector2(dir * 6, -16), null, col, 0, new Vector2(log.Width / 2f, log.Height / 2f), 0.85f, u.FacingLeft ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0);
    }

    void DrawFlagGround(SpriteBatch sb, ControlPoint f, float time)
    {
        var col = f.Owner >= 0 ? _w.Players[f.Owner].Color : Color.White;
        float pulse = 0.35f + 0.15f * MathF.Sin(time * 3);
        sb.Disc(f.Pos, ControlPoint.Radius, ControlPoint.Radius * 0.55f, col * 0.10f);
        sb.Ellipse(f.Pos, ControlPoint.Radius, ControlPoint.Radius * 0.55f, col * pulse);
        // marcadores no chão ao redor
        for (int i = 0; i < 8; i++)
        {
            float a = i * MathHelper.TwoPi / 8 + time * 0.2f;
            sb.Disc(f.Pos + new Vector2(MathF.Cos(a) * ControlPoint.Radius, MathF.Sin(a) * ControlPoint.Radius * 0.55f), 4, 2.5f, col * 0.6f);
        }
    }

    void DrawFlagPole(SpriteBatch sb, ControlPoint f, float time)
    {
        var basePos = f.Pos + new Vector2(0, 10);
        sb.Disc(basePos, 16, 6, Color.Black * 0.3f);
        sb.Disc(basePos, 10, 4, new Color(120, 110, 100));
        sb.Line(basePos, basePos + new Vector2(0, -84), new Color(80, 55, 30), 4);
        // ponteira: cor do time (aliado/inimigo), dourada quando neutra
        sb.Disc(basePos + new Vector2(0, -86), 5, 5, f.Owner >= 0 ? _w.Players[f.Owner].Color : Pal.Gold);
        // pano: bandeira do estado que domina o ponto (branca enquanto neutra)
        FactionId? owner = f.Owner >= 0 ? _w.Players[f.Owner].Faction.Id : null;
        FlagArt.DrawWaving(sb, owner, basePos + new Vector2(2, -82), 48, 32, time);
        // enquanto é capturada, a bandeira de quem captura sobe pelo mastro
        if (f.Capturer >= 0 && f.Capturer != f.Owner)
        {
            float k = Math.Clamp(Math.Abs(f.Progress) / 100f, 0, 1);
            if (Math.Sign(f.Progress) == (f.Capturer == 0 ? 1 : -1))
                FlagArt.DrawWaving(sb, _w.Players[f.Capturer].Faction.Id, basePos + new Vector2(2, -20 - 46 * k), 26, 17, time, 1.5f);
        }
    }
}

static class ColorExt
{
    public static Color MultiplyAlpha(this Color c, Color tint) => new Color(c.R * tint.R / 255, c.G * tint.G / 255, c.B * tint.B / 255, c.A * tint.A / 255);
}
