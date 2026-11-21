namespace GuerraDasBandeiras.Gameplay;

/// <summary>
/// Mapa "Encontro das Fronteiras" (seção 05 do caderno): vale fictício com mata a oeste,
/// serra a leste, planície ao sul, rio raso com duas travessias e duas bandeiras no centro.
/// </summary>
public static class MapGen
{
    public static int RiverX(int y) => 31 + (int)MathF.Round(2.2f * MathF.Sin(y * 0.33f + 0.4f));
    public static readonly int[] FordRows = { 10, 29 };
    public const int ShortcutRow = 20;

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

        // Mata (oeste/noroeste)
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
            {
                float dx = (x - 13f) / 12.5f, dy = (y - 12.5f) / 9.5f;
                float noise = (float)r.NextDouble() * 0.16f;
                if (dx * dx + dy * dy < 1f - noise) w.Ter[x, y] = Terrain.Forest;
            }
        // pequeno bosque perto da base leste, para dar algum abrigo a quem estiver lá
        Blob(w, 59, 22, 3.2f, 2.4f, Terrain.Forest, r);
        // e um capão na planície sul (caminho de flanco)
        Blob(w, 22, 35, 2.6f, 1.8f, Terrain.Forest, r);
        Blob(w, 41, 35, 2.6f, 1.8f, Terrain.Forest, r);
        // bosques perto das travessias: pontos de emboscada (Bíblia: "bosque oculta parcialmente tropas")
        Blob(w, 25.5f, 13.5f, 2.2f, 1.8f, Terrain.Forest, r);
        Blob(w, 38.5f, 6.5f, 2.2f, 1.8f, Terrain.Forest, r);
        Blob(w, 25.5f, 24.5f, 1.8f, 1.5f, Terrain.Forest, r);
        Blob(w, 38.5f, 33.5f, 1.8f, 1.5f, Terrain.Forest, r);

        // Serra (leste/nordeste)
        var poly = new Vector2[] { new(42, 2), new(57, 3), new(63, 12), new(57, 21), new(47, 19), new(41, 10) };
        for (int x = 0; x < W; x++)
            for (int y = 0; y < H; y++)
                if (PointInPoly(new Vector2(x + 0.5f, y + 0.5f), poly)) w.Ter[x, y] = Terrain.Hill;
        // colina pequena perto da base oeste (vigia)
        Blob(w, 4, 37, 2.5f, 1.5f, Terrain.Hill, r);

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

        // Chão batido das bases
        Blob(w, 7.5f, 31.5f, 4.2f, 3.6f, Terrain.Dirt, r, onlyOn: Terrain.Grass);
        Blob(w, 56.5f, 31.5f, 4.2f, 3.6f, Terrain.Dirt, r, onlyOn: Terrain.Grass);

        // Bandeiras de controle sobre as travessias
        w.Flags.Add(new ControlPoint { Name = "Bandeira do Norte", Bonus = FlagBonus.Defense, Pos = w.CenterOf(new Point(RiverX(FordRows[0]), FordRows[0])) });
        w.Flags.Add(new ControlPoint { Name = "Bandeira do Sul", Bonus = FlagBonus.Food, Pos = w.CenterOf(new Point(RiverX(FordRows[1]), FordRows[1])) });

        // Bases
        w.StartTiles[0] = new Point(6, 30);
        w.StartTiles[1] = new Point(55, 30);
        for (int t = 0; t < 2; t++)
        {
            var st = w.StartTiles[t];
            w.AddBuilding(BuildingType.Centro, t, st, true);
            int dir = t == 0 ? 1 : -1;
            var c = w.CenterOf(new Point(st.X + 1, st.Y + 1));
            w.AddUnit(UnitType.Worker, t, c + new Vector2(dir * 140, 40));
            w.AddUnit(UnitType.Worker, t, c + new Vector2(dir * 140, 90));
            w.AddUnit(UnitType.Worker, t, c + new Vector2(dir * 100, 120));
            w.AddUnit(UnitType.Scout, t, c + new Vector2(dir * 150, -40));
        }

        // Recursos próximos às bases (espelhados)
        foreach (var mirror in new[] { false, true })
        {
            int M(int x) => mirror ? W - 1 - x : x;
            // comida
            foreach (var p in new[] { (11, 28), (12, 29), (12, 27), (11, 30), (13, 28), (13, 30), (14, 29), (12, 31) })
                w.AddResource(Res.Food, new Point(M(p.Item1), p.Item2), 200, 19);
            // segunda moita de frutas, um pouco mais longe
            foreach (var p in new[] { (5, 36), (4, 35), (6, 37), (3, 36) })
                w.AddResource(Res.Food, new Point(M(p.Item1), p.Item2), 200, 19);
            // madeira (bosquezinho)
            foreach (var p in new[] { (2, 26), (3, 25), (4, 26), (2, 28), (3, 27), (1, 27), (4, 24), (1, 25) })
                w.AddResource(Res.Wood, new Point(M(p.Item1), p.Item2), 100, r.Next(2) == 0 ? 3 : 2);
            // minério
            foreach (var p in new[] { (9, 36), (10, 37), (11, 36), (8, 37) })
                w.AddResource(Res.Ore, new Point(M(p.Item1), p.Item2), 250, 17 + r.Next(2));
            // comida no meio do caminho
            foreach (var p in new[] { (24, 31), (25, 32), (24, 26), (23, 27), (25, 25) })
                w.AddResource(Res.Food, new Point(M(p.Item1), p.Item2), 200, 19);
            // minério na planície
            foreach (var p in new[] { (18, 38), (19, 38) })
                w.AddResource(Res.Ore, new Point(M(p.Item1), p.Item2), 250, 10 + r.Next(2));
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
        for (int i = 0; i < 90; i++)
        {
            var p = new Point(r.Next(W), r.Next(H));
            var t = w.Ter[p.X, p.Y];
            if (t == Terrain.Water || t == Terrain.Ford || w.Occ[p.X, p.Y] != null) continue;
            int sprite = t == Terrain.Hill ? 6 : r.Next(3) switch { 0 => 6, 1 => 13, _ => 12 };
            w.Decors.Add(new Decor { Pos = w.CenterOf(p) + new Vector2(r.Next(-20, 20), r.Next(-20, 20)), Sprite = sprite, Scale = 0.8f });
        }
    }

    /// <summary>Caminhos de terra da capital até as áreas de coleta e as travessias (só visual).</summary>
    static void AddRoads(World w)
    {
        for (int t = 0; t < 2; t++)
        {
            var st = w.StartTiles[t];
            bool west = t == 0;
            var start = w.CenterOf(new Point(west ? st.X + 3 : st.X - 1, st.Y + 1));
            int M(int x) => west ? x : w.W - 1 - x;
            var targets = new[]
            {
                new Point(M(12), 29), new Point(M(3), 26), new Point(M(10), 36),
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
            bool west = t == 0;
            float back = west ? -1 : 1; // lado de trás da base (longe do rio)
            var c = w.CenterOf(new Point(st.X + 1, st.Y + 1));
            switch (w.Players[t].Faction.Id)
            {
                case FactionId.Amazonas:
                    for (int i = 0; i < 7; i++)
                        w.Decors.Add(new Decor { Pos = c + new Vector2(back * (150 + r.Next(60)), -150 + i * 50 + r.Next(-10, 10)), Sprite = 12, Scale = 1.6f + (i % 3) * 0.2f });
                    break;
                // Minas: a identidade vem da arquitetura de pedra e dos carrinhos de minério nos prédios
                // (pedras soltas aqui seriam confundidas com minério coletável)
                case FactionId.RioGrandeDoSul:
                    w.Decors.Add(new Decor { Pos = c + new Vector2(back * 170, -110), Sprite = 14, Scale = 1.5f, Structure = true });
                    break;
            }
        }
    }

    static bool NearFordOrBase(World w, int x, int y)
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
