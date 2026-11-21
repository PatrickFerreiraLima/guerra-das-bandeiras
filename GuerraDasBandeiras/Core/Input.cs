namespace GuerraDasBandeiras.Core;

public static class Input
{
    public static MouseState M, PM;
    public static KeyboardState K, PK;
    public static bool Active = true;

    public static void Update(bool active)
    {
        Active = active;
        PM = M; PK = K;
        M = Mouse.GetState();
        K = Keyboard.GetState();
    }

    public static Point MousePos => M.Position;
    /// <summary>Posição do mouse em coordenadas de interface (já descontada a escala da UI).</summary>
    public static Vector2 UiMouse => M.Position.ToVector2() / Game1.UiScaleEff;
    public static Point UiMousePt => UiMouse.ToPoint();

    public static bool LeftPressed => Active && M.LeftButton == ButtonState.Pressed && PM.LeftButton == ButtonState.Released;
    public static bool LeftReleased => M.LeftButton == ButtonState.Released && PM.LeftButton == ButtonState.Pressed;
    public static bool LeftDown => M.LeftButton == ButtonState.Pressed;
    public static bool RightPressed => Active && M.RightButton == ButtonState.Pressed && PM.RightButton == ButtonState.Released;
    public static bool MiddleDown => M.MiddleButton == ButtonState.Pressed;
    public static int Wheel => Active ? M.ScrollWheelValue - PM.ScrollWheelValue : 0;
    public static Point MouseDelta => M.Position - PM.Position;

    public static bool KeyPressed(Keys k) => Active && K.IsKeyDown(k) && !PK.IsKeyDown(k);
    public static bool KeyDown(Keys k) => Active && K.IsKeyDown(k);
    public static bool AnyKeyPressed => Active && K.GetPressedKeys().Any(k => !PK.IsKeyDown(k));
    public static bool Shift => K.IsKeyDown(Keys.LeftShift) || K.IsKeyDown(Keys.RightShift);
    public static bool Ctrl => K.IsKeyDown(Keys.LeftControl) || K.IsKeyDown(Keys.RightControl);
}
