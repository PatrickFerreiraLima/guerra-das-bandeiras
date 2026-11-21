namespace GuerraDasBandeiras.Gameplay;

/// <summary>A* em grade com 8 direções, sem cortar quinas, e suavização por linha de visão.</summary>
public class Pathing
{
    readonly World _w;
    readonly float[] _g;
    readonly int[] _parent;
    readonly int[] _stamp;
    readonly bool[] _closed;
    int _cur;
    readonly PriorityQueue<int, float> _open = new();

    static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
    static readonly int[] DY = { 0, 0, 1, -1, 1, -1, 1, -1 };

    public Pathing(World w)
    {
        _w = w;
        int n = w.W * w.H;
        _g = new float[n]; _parent = new int[n]; _stamp = new int[n]; _closed = new bool[n];
    }

    public List<Vector2> FindPath(Vector2 from, Vector2 to)
    {
        var t = _w.TileOf(to);
        bool blocked = !_w.Passable(t);
        var path = FindPathToRect(from, new Rectangle(t.X, t.Y, 1, 1), blocked);
        if (!blocked && path != null && path.Count > 0 && _w.TileOf(path[^1]) == t) path[^1] = to;
        else if (!blocked && path != null && path.Count == 0) path.Add(to);
        return path;
    }

    static int ChebDist(int x, int y, Rectangle r)
    {
        int dx = x < r.Left ? r.Left - x : x >= r.Right ? x - r.Right + 1 : 0;
        int dy = y < r.Top ? r.Top - y : y >= r.Bottom ? y - r.Bottom + 1 : 0;
        return Math.Max(dx, dy);
    }

    static float Octile(int x, int y, Rectangle r)
    {
        int dx = x < r.Left ? r.Left - x : x >= r.Right ? x - r.Right + 1 : 0;
        int dy = y < r.Top ? r.Top - y : y >= r.Bottom ? y - r.Bottom + 1 : 0;
        return Math.Max(dx, dy) + 0.414f * Math.Min(dx, dy);
    }

    public List<Vector2> FindPathToRect(Vector2 from, Rectangle goal, bool goalBlocked)
    {
        int W = _w.W, H = _w.H;
        var s = _w.TileOf(from);
        s.X = Math.Clamp(s.X, 0, W - 1); s.Y = Math.Clamp(s.Y, 0, H - 1);
        int goalDist = goalBlocked ? 1 : 0;

        _cur++;
        _open.Clear();
        int si = s.Y * W + s.X;
        _g[si] = 0; _parent[si] = -1; _stamp[si] = _cur; _closed[si] = false;
        _open.Enqueue(si, Octile(s.X, s.Y, goal));
        int best = si; float bestH = float.MaxValue;
        int found = -1, iter = 0;

        while (_open.Count > 0 && iter++ < 6000)
        {
            int ci = _open.Dequeue();
            if (_closed[ci] && _stamp[ci] == _cur) continue;
            _closed[ci] = true;
            int cx = ci % W, cy = ci / W;
            if (ChebDist(cx, cy, goal) <= goalDist && (ci == si || _w.Passable(new Point(cx, cy)))) { found = ci; break; }
            float h = Octile(cx, cy, goal);
            if (h < bestH) { bestH = h; best = ci; }

            for (int d = 0; d < 8; d++)
            {
                int nx = cx + DX[d], ny = cy + DY[d];
                if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                if (!_w.Passable(new Point(nx, ny))) continue;
                if (d >= 4 && (!_w.Passable(new Point(cx + DX[d], cy)) || !_w.Passable(new Point(cx, cy + DY[d])))) continue;
                int ni = ny * W + nx;
                float cost = (d >= 4 ? 1.414f : 1f) * TerrainCost(nx, ny);
                float ng = _g[ci] + cost;
                if (_stamp[ni] != _cur) { _stamp[ni] = _cur; _closed[ni] = false; _g[ni] = float.MaxValue; }
                if (_closed[ni] || ng >= _g[ni]) continue;
                _g[ni] = ng; _parent[ni] = ci;
                _open.Enqueue(ni, ng + Octile(nx, ny, goal) * 1.05f);
            }
        }

        int end = found >= 0 ? found : best;
        var tiles = new List<Point>();
        for (int i = end; i != -1 && i != si; i = _parent[i]) tiles.Add(new Point(i % W, i / W));
        tiles.Reverse();

        var pts = tiles.Select(t => _w.CenterOf(t)).ToList();
        return Smooth(from, pts);
    }

    float TerrainCost(int x, int y) => _w.Ter[x, y] switch
    {
        Terrain.Forest => 1.25f,
        Terrain.Ford => 1.3f,
        Terrain.Hill => 1.15f,
        _ => 1f
    };

    List<Vector2> Smooth(Vector2 from, List<Vector2> pts)
    {
        if (pts.Count < 2) return pts;
        var result = new List<Vector2>();
        var cur = from;
        int i = 0;
        while (i < pts.Count)
        {
            int far = i;
            for (int j = pts.Count - 1; j > i; j--)
                if (LineClear(cur, pts[j])) { far = j; break; }
            result.Add(pts[far]);
            cur = pts[far];
            i = far + 1;
        }
        return result;
    }

    /// <summary>Verifica se a linha passa só por tiles livres (amostrando com folga para o raio da unidade).</summary>
    public bool LineClear(Vector2 a, Vector2 b)
    {
        var d = b - a;
        float len = d.Length();
        if (len < 1) return true;
        var dir = d / len;
        var perp = new Vector2(-dir.Y, dir.X) * 13f;
        for (float t = 0; t <= len; t += 12f)
        {
            var p = a + dir * t;
            if (!_w.Passable(_w.TileOf(p)) || !_w.Passable(_w.TileOf(p + perp)) || !_w.Passable(_w.TileOf(p - perp))) return false;
        }
        return true;
    }
}
