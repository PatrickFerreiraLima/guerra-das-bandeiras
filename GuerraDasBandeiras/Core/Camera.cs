namespace GuerraDasBandeiras.Core;

public class Camera
{
    public Vector2 Pos;
    public float Zoom = 1f;
    public int ViewW, ViewH;

    public Matrix Transform
    {
        get
        {
            var p = new Vector2(MathF.Round(Pos.X * Zoom) / Zoom, MathF.Round(Pos.Y * Zoom) / Zoom);
            return Matrix.CreateTranslation(-p.X, -p.Y, 0) * Matrix.CreateScale(Zoom);
        }
    }

    public Vector2 ScreenToWorld(Vector2 s) => s / Zoom + Pos;
    public Vector2 WorldToScreen(Vector2 w) => (w - Pos) * Zoom;
    public Vector2 ViewSizeWorld => new Vector2(ViewW, ViewH) / Zoom;
    public RectangleF ViewRect => new(Pos.X, Pos.Y, ViewW / Zoom, ViewH / Zoom);

    public void CenterOn(Vector2 w) => Pos = w - ViewSizeWorld / 2f;

    public void ZoomAt(float newZoom, Vector2 screenPoint)
    {
        var before = ScreenToWorld(screenPoint);
        Zoom = newZoom;
        var after = ScreenToWorld(screenPoint);
        Pos += before - after;
    }

    /// <summary>Mantém a câmera dentro do mapa (com folga para a barra superior e o painel inferior).</summary>
    public void Clamp(float worldW, float worldH, float topPad, float bottomPad)
    {
        var v = ViewSizeWorld;
        float top = -topPad / Zoom, bottom = worldH - v.Y + bottomPad / Zoom;
        Pos.X = v.X >= worldW ? (worldW - v.X) / 2 : Math.Clamp(Pos.X, 0, worldW - v.X);
        Pos.Y = bottom <= top ? (worldH - v.Y) / 2 : Math.Clamp(Pos.Y, top, bottom);
    }
}

public readonly record struct RectangleF(float X, float Y, float W, float H)
{
    public float Right => X + W;
    public float Bottom => Y + H;
    public bool Intersects(float x, float y, float w, float h) => x < Right && x + w > X && y < Bottom && y + h > Y;
    public bool Contains(Vector2 p) => p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;
}
