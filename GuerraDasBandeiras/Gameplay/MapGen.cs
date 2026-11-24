namespace GuerraDasBandeiras.Gameplay;

/// <summary>
/// Mapa "Encontro das Fronteiras" (seção 05 do caderno): vale fictício com mata a oeste,
/// serra a leste, planície ao sul, rio raso com duas travessias e duas bandeiras no centro.
/// 80x50 tiles. Na escaramuça e no 1x1 em rede a posição de cada base é sorteada (lado do rio,
/// norte/centro/sul, pequeno deslocamento e espelhamento vertical), sempre com o mesmo "kit"
/// de recursos à mesma distância: muda o caminho, não a vantagem. Tudo sai da semente da partida,
/// então as duas máquinas de uma partida em rede geram exatamente o mesmo mapa.
/// </summary>
public static class MapGen
{
    public static int RiverX(int y) => 39 + (int)MathF.Round(2.75f * MathF.Sin(y * 0.264f + 0.4f));
    public static readonly int[] FordRows = { 12, 36 };
    public const int ShortcutRow = 25;

    /// <summary>Linhas (centro da base) dos três lugares possíveis de cada lado do rio: norte, centro e sul.</summary>
    public static readonly int[] BaseRows = { 10, 25, 39 };
    /// <summary>Lugar clássico (tutorial, missão, testes): time 0 a oeste e time 1 a leste, ambos ao sul.</summary>
    const int ClassicSlot = 2;

    // "kit" de cada base, em tiles a partir do centro do Centro de Comando, olhando para o rio (dx > 0 = rumo ao rio)
    static readonly (int dx, int dy)[] KitFood = { (4, -3), (5, -2), (5, -4), (4, -1), (6, -3), (6, -1), (7, -2), (5, 0) };
    static readonly (int dx, int dy)[] KitFood2 = { (-2, 5), (-3, 4), (-1, 6), (-4, 5) };
    static readonly (int dx, int dy)[] KitWood = { (-5, -5), (-4, -6), (-3, -5), (-5, -3), (-4, -4), (-6, -4), (-3, -7), (-6, -6) };
    static readonly (int dx, int dy)[] KitOre = { (2, 5), (3, 6), (4, 5), (1, 6) };
    /// <summary>Destinos dos caminhos de terra (comida, madeira, minério).</summary>
    static readonly (int dx, int dy)[] KitRoads = { (5, -2), (-4, -5), (3, 5) };

    /// <summary>Tile do kit da base t: dx no sentido do rio, dy espelhado conforme o sorteio.</summary>
    public static Point Kit(World w, int t, int dx, int dy)
    {
        var st = w.StartTiles[t];
        return new Point(st.X + 1 + w.BaseDir[t] * dx, st.Y + 1 + w.BaseFlip[t] * dy);
    }

    static float S(float v) => v * 1.25f;   // o desenho original era 64x40; o mapa novo é 25% maior em cada eixo

    public static void Build(World w)
    {
        var r = w.Rng;
        int W = w.W, H = w.H;

        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
            {
                w.Ter[x, y] = Terrain.Grass;
                w.TileVar[x, y] = (byte)r.Next(256);
            }

        // ---------------- onde fica cada base ----------------
        bool random = w.Config.UsesRandomBases;
        int westTeam = random ? r.Next(2) : 0;              // quem fica a oeste também é sorteado
        for (int t = 0; t < 2; t++)
        {
            bool west = t == westTeam;
            int slot = random ? r.Next(3) : ClassicSlot;
            int cy = BaseRows[slot] + (random ? r.Next(-1, 2) : 0);
            int cx = random ? 7 + r.Next(3) : 7;              // 7 a 9 tiles da borda
            if (!west) cx = W - 1 - cx;
            w.StartTiles[t] = new Point(cx - 1, cy - 1);
            w.BaseDir[t] = west ? 1 : -1;
            w.BaseFlip[t] = random && r.Next(2) == 0 ? -1 : 1;
        }

        // ---------------- relevo ----------------
        // Mata (oeste/noroeste)
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
            {
                float dx = (x - S(13f)) / S(12.5f), dy = (y - S(12.5f)) / S(9.5f);
                float noise = (float)r.NextDouble() * 0.16f;
                if (dx * dx + dy * dy < 1f - noise) w.Ter[x, y] = Terrain.Forest;
            }
        // bosque do lado leste, capões na planície sul (flancos) e bosques junto às travessias (emboscadas)
        Blob(w, S(59), S(22), S(3.2f), S(2.4f), Terrain.Forest, r);
        Blob(w, S(22), S(35), S(2.6f), S(1.8f), Terrain.Forest, r);
        Blob(w, S(41), S(35), S(2.6f), S(1.8f), Terrain.Forest, r);
        Blob(w, S(25.5f), S(13.5f), S(2.2f), S(1.8f), Terrain.Forest, r);
        Blob(w, S(38.5f), S(6.5f), S(2.2f), S(1.8f), Terrain.Forest, r);
        Blob(w, S(25.5f), S(24.5f), S(1.8f), S(1.5f), Terrain.Forest, r);
        Blob(w, S(38.5f), S(33.5f), S(1.8f), S(1.5f), Terrain.Forest, r);

        // Serra (leste/nordeste)
        var poly = new Vector2[] { new(S(42), S(2)), new(S(57), S(3)), new(S(63), S(12)), new(S(57), S(21)), new(S(47), S(19)), new(S(41), S(10)) };
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
                if (PointInPoly(new Vector2(x + 0.5f, y + 0.5f), poly)) w.Ter[x, y] = Terrain.Hill;

        // Rio com duas travessias rasas
        for (int y = 0; y < H; y++)
        {
            int cx = RiverX(y);
            for (int x = cx - 1; x <= cx + 1; x++) w.Ter[x, y] = Terrain.Water;
        }
        foreach (int fy in FordRows)
            for (int y = fy - 1; y <= fy + 1; y++)
            {
                int cx = RiverX(y);
                for (int x = cx - 1; x <= cx + 1; x++) w.Ter[x, y] = Terrain.Ford;
            }

        // Atalho arriscado: uma passagem rasa e estreita (1 tile) no meio do rio, sem bandeira
        {
            int cx = RiverX(ShortcutRow);
            for (int x = cx - 1; x <= cx + 1; x++) w.Ter[x, ShortcutRow] = Terrain.Ford;
        }

        // ---------------- bases: clareira, colina de vigia e chão batido ----------------
        for (int t = 0; t < 2; t++)
        {
            var c = Kit(w, t, 0, 0);
            // se a base caiu na mata ou na serra, abre uma clareira para a vila
            for (int x = c.X - 7; x <= c.X + 7; x++)
                for (int y = c.Y - 7; y <= c.Y + 7; y++)
                {
                    if (x < 0 || y < 0 || x >= W || y >= H) continue;
                    float ex = (x - c.X) / 7.2f, ey = (y - c.Y) / 7.2f;
                    if (ex * ex + ey * ey < 1f - (float)r.NextDouble() * 0.12f && w.Ter[x, y] is Terrain.Forest or Terrain.Hill) w.Ter[x, y] = Terrain.Grass;
                }
            var hill = Kit(w, t, -3, 6);
            Blob(w, hill.X + 0.5f, hill.Y + 0.5f, 2.5f, 1.5f, Terrain.Hill, r);
            Blob(w, c.X + 0.5f, c.Y + 0.5f, 4.2f, 3.6f, Terrain.Dirt, r, onlyOn: Terrain.Grass);
        }

        // Bandeiras de controle sobre as travessias
        w.Flags.Add(new ControlPoint { Name = "Bandeira do Norte", Bonus = FlagBonus.Defense, Pos = w.CenterOf(new Point(RiverX(FordRows[0]), FordRows[0])) });
        w.Flags.Add(new ControlPoint { Name = "Bandeira do Sul", Bonus = FlagBonus.Food, Pos = w.CenterOf(new Point(RiverX(FordRows[1]), FordRows[1])) });

        // Centros de Comando e unidades iniciais
        for (int t = 0; t < 2; t++)
        {
            var st = w.StartTiles[t];
            w.AddBuilding(BuildingType.Centro, t, st, true);
            int dir = w.BaseDir[t], vf = w.BaseFlip[t];
            var c = w.CenterOf(new Point(st.X + 1, st.Y + 1));
            w.AddUnit(UnitType.Worker, t, c + new Vector2(dir * 140, vf * 40));
            w.AddUnit(UnitType.Worker, t, c + new Vector2(dir * 140, vf * 90));
            w.AddUnit(UnitType.Worker, t, c + new Vector2(dir * 100, vf * 120));
            w.AddUnit(UnitType.Scout, t, c + new Vector2(dir * 150, vf * -40));
        }

        // Recursos de cada base: o mesmo kit, à mesma distância, virado para o rio
        for (int t = 0; t < 2; t++)
        {
            foreach (var (dx, dy) in KitFood) w.AddResource(Res.Food, Kit(w, t, dx, dy), 200, 19);
            foreach (var (dx, dy) in KitFood2) w.AddResource(Res.Food, Kit(w, t, dx, dy), 200, 19);
            foreach (var (dx, dy) in KitWood) w.AddResource(Res.Wood, Kit(w, t, dx, dy), 100, r.Next(2) == 0 ? 3 : 2);
            foreach (var (dx, dy) in KitOre) w.AddResource(Res.Ore, Kit(w, t, dx, dy), 250, 17 + r.Next(2));
        }

        // Recursos neutros, espelhados dos dois lados do rio: comida no meio do caminho e minério na planície sul
        foreach (var mirror in new[] { false, true })
        {
            int M(float x) { int v = (int)MathF.Round(x); return mirror ? W - 1 - v : v; }
            foreach (var p in new[] { (24, 31), (25, 32), (24, 26), (23, 27), (25, 25) })
                w.AddResource(Res.Food, new Point(M(S(p.Item1)), (int)MathF.Round(S(p.Item2))), 200, 19);
            foreach (var p in new[] { (18, 38), (19, 38) })
                w.AddResource(Res.Ore, new Point(M(S(p.Item1)), (int)MathF.Round(S(p.Item2))), 250, 10 + r.Next(2));
        }

        // Árvores dentro da mata (mais densas nas bordas) e rochas na serra
        for (int x = 1; x < W - 1; x++)
            for (int y = 1; y < H - 1; y++)
            {
                if (NearFordOrBase(w, x, y)) continue;
                var t = w.Ter[x, y];
                if (t == Terrain.Forest)
                {
                    bool edge = false;
                    for (int dx = -1; dx <= 1; dx++)
                        for (int dy = -1; dy <= 1; dy++)
                            if (w.Ter[x + dx, y + dy] != Terrain.Forest) edge = true;
                    if (r.NextDouble() < (edge ? 0.30 : 0.10))
                        w.AddResource(Res.Wood, new Point(x, y), 100, r.Next(3) switch { 0 => 1, 1 => 3, _ => 2 });
                }
                else if (t == Terrain.Hill && r.NextDouble() < 0.07)
                    w.AddResource(Res.Ore, new Point(x, y), 250, 9 + r.Next(3));
                else if (t == Terrain.Grass && r.NextDouble() < 0.012 && Math.Abs(x - RiverX(y)) > 3)
                    w.AddResource(Res.Wood, new Point(x, y), 100, r.Next(2) == 0 ? 1 : 21);
            }

        AddRoads(w);
        AddFactionDecor(w, r);

        // Enfeites que não bloqueiam (pedrinhas, tocos)
        for (int i = 0; i < 140; i++)
        {
            var p = new Point(r.Next(W), r.Next(H));
            var t = w.Ter[p.X, p.Y];
            if (t == Terrain.Water || t == Terrain.Ford || w.Occ[p.X, p.Y] != null) continue;
            int sprite = t == Terrain.Hill ? 6 : r.Next(3) switch { 0 => 6, 1 => 13, _ => 12 };
            w.Decors.Add(new Decor { Pos = w.CenterOf(p) + new Vector2(r.Next(-20, 20), r.Next(-20, 20)), Sprite = sprite, Scale = 0.8f });
        }

        // coqueiros, ipês, vitórias-régias, capivaras e os elementos de cada estado (gerador próprio: não altera o mapa)
        Scenery.Populate(w);
    }

    /// <summary>Caminhos de terra da capital até as áreas de coleta e as travessias (só visual).</summary>
    static void AddRoads(World w)
    {
        for (int t = 0; t < 2; t++)
        {
            bool west = w.BaseDir[t] > 0;
            var start = w.CenterOf(Kit(w, t, 2, 0));
            var targets = new[]
            {
                Kit(w, t, KitRoads[0].dx, KitRoads[0].dy), Kit(w, t, KitRoads[1].dx, KitRoads[1].dy), Kit(w, t, KitRoads[2].dx, KitRoads[2].dy),
                new Point(RiverX(FordRows[1]) + (west ? -2 : 2), FordRows[1]),
                new Point(RiverX(FordRows[0]) + (west ? -2 : 2), FordRows[0]),
            };
            foreach (var tg in targets)
            {
                var path = w.Pathing.FindPathToRect(start, new Rectangle(tg.X, tg.Y, 1, 1), !w.Passable(tg));
                var prev = start;
                int seg = 0;
                foreach (var p in path)
                {
                    var d = p - prev; float len = d.Length();
                    var perp = len > 0 ? new Vector2(-d.Y, d.X) / len : Vector2.Zero;
                    for (float s = 0; s < len; s += 12)
                    {
                        var q = prev + d * (s / len) + perp * MathF.Sin((s + seg * 37) * 0.03f) * 6;
                        var ter = w.TerrainAt(q);
                        if (ter is Terrain.Grass or Terrain.Dirt or Terrain.Hill) w.RoadPoints.Add(q);
                    }
                    prev = p; seg++;
                }
            }
        }
    }

    /// <summary>Detalhes regionais em volta de cada base (não bloqueiam e ficam atrás da vila).</summary>
    static void AddFactionDecor(World w, Random r)
    {
        for (int t = 0; t < 2; t++)
        {
            var st = w.StartTiles[t];
            float back = -w.BaseDir[t]; // lado de trás da base (longe do rio)
            float vf = w.BaseFlip[t];
            var c = w.CenterOf(new Point(st.X + 1, st.Y + 1));
            switch (w.Players[t].Faction.Id)
            {
                case FactionId.Amazonas:
                    for (int i = 0; i < 7; i++)
                        w.Decors.Add(new Decor { Pos = c + new Vector2(back * (150 + r.Next(60)), vf * (-150 + i * 50 + r.Next(-10, 10))), Sprite = 12, Scale = 1.6f + (i % 3) * 0.2f });
                    break;
                // Minas: a identidade vem da arquitetura de pedra e dos carrinhos de minério nos prédios
                // (pedras soltas aqui seriam confundidas com minério coletável)
                case FactionId.RioGrandeDoSul:
                    w.Decors.Add(new Decor { Pos = c + new Vector2(back * 170, vf * -110), Sprite = 14, Scale = 1.5f, Structure = true });
                    break;
            }
        }
    }

    public static bool NearFordOrBase(World w, int x, int y)
    {
        foreach (int fy in FordRows)
            if (Math.Abs(y - fy) <= 3 && Math.Abs(x - RiverX(fy)) <= 6) return true;
        if (Math.Abs(y - ShortcutRow) <= 1 && Math.Abs(x - RiverX(ShortcutRow)) <= 4) return true;
        foreach (var s in w.StartTiles)
            if (x >= s.X - 3 && x <= s.X + 5 && y >= s.Y - 3 && y <= s.Y + 5) return true;
        return false;
    }

    static void Blob(World w, float cx, float cy, float rx, float ry, Terrain t, Random r, Terrain? onlyOn = null)
    {
        for (int x = (int)(cx - rx - 1); x <= cx + rx + 1; x++)
            for (int y = (int)(cy - ry - 1); y <= cy + ry + 1; y++)
            {
                if (x < 0 || y < 0 || x >= w.W || y >= w.H) continue;
                float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry;
                if (dx * dx + dy * dy < 1f - (float)r.NextDouble() * 0.15f)
                    if (onlyOn == null || w.Ter[x, y] == onlyOn) w.Ter[x, y] = t;
            }
    }

    static bool PointInPoly(Vector2 p, Vector2[] poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) &&
                p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                inside = !inside;
        }
        return inside;
    }
}
