using GuerraDasBandeiras.Gameplay;

namespace GuerraDasBandeiras.Core;

/// <summary>
/// Eventos sonoros do jogo num lugar só. Todos os arquivos vêm dos pacotes da Kenney.nl (licença CC0,
/// cópias das licenças em Assets/Audio/LICENSE-kenney-*.txt). Para trocar um som, mude o nome aqui;
/// se o arquivo não existir, o evento fica mudo (não quebra o jogo).
/// Sons "no mundo" só tocam se o ponto estiver na tela, para não poluir o áudio.
/// </summary>
public static class Sfx
{
    static readonly Random _r = new();
    static float _lastCapital, _lastAttack, _lastSelect;
    static float P(float spread = 0.15f) => (float)(_r.NextDouble() * 2 - 1) * spread;
    static string Pick(params string[] names) => names[_r.Next(names.Length)];
    static void At(World w, Vector2 pos, string name, float vol, float pitch = 0) { if (w == null || w.OnScreen(pos)) Assets.Play(name, vol, pitch); }

    // ---------- interface ----------
    public static void Click() => Assets.Play("click_002", 0.7f);
    public static void Error() => Assets.Play("error_006", 0.5f);
    public static void Warning() => Assets.Play("question_001", 0.55f);
    public static void StartGame() => Assets.Play("confirmation_002", 0.8f);

    // ---------- seleção e ordens ----------
    public static void Select()
    {
        if (Environment.TickCount - _lastSelect < 80) return;
        _lastSelect = Environment.TickCount;
        Assets.Play(Pick("select_001", "select_002", "select_003"), 0.5f, P(0.1f));
    }
    public static void OrderMove() => Assets.Play(Pick("tick_001", "tick_002"), 0.6f, P(0.1f));
    public static void OrderAttack() => Assets.Play(Pick("drawKnife2", "drawKnife3"), 0.45f, P(0.1f));
    public static void OrderGather() => Assets.Play(Pick("pluck_001", "pluck_002"), 0.55f, P(0.1f));
    public static void OrderBuild() => Assets.Play("impactWood_medium_000", 0.6f);

    // ---------- economia ----------
    public static void Gather(World w, Vector2 pos, Res kind)
    {
        switch (kind)
        {
            case Res.Wood: At(w, pos, Pick("chop"), 0.25f, P(0.2f)); break;
            case Res.Ore: At(w, pos, Pick("impactMining_000", "impactMining_001", "impactMining_002"), 0.22f, P(0.15f)); break;
            default: At(w, pos, Pick("impactSoft_medium_000", "impactSoft_medium_001"), 0.25f, P(0.2f)); break;
        }
    }
    public static void Hammer(World w, Vector2 pos) => At(w, pos, Pick("impactWood_medium_000", "impactWood_medium_001", "impactWood_medium_002"), 0.22f, P(0.2f));
    public static void Deposit(World w, Vector2 pos) => At(w, pos, "handleSmallLeather", 0.25f, P(0.1f));
    public static void BuildingDone() => Assets.Play("confirmation_002", 0.7f);
    public static void UnitReady() => Assets.Play("drop_002", 0.55f, P(0.1f));
    public static void BuildingDestroyed(World w, Vector2 pos) => At(w, pos, "impactWood_heavy_000", 0.8f, -0.3f);

    // ---------- combate ----------
    public static void Melee(World w, Vector2 pos, bool vsBuilding, bool siege)
    {
        if (siege) At(w, pos, "impactWood_heavy_000", 0.7f, -0.4f);
        else if (vsBuilding) At(w, pos, Pick("impactWood_medium_001", "impactWood_medium_002"), 0.4f, P(0.2f));
        else At(w, pos, Pick("knifeSlice", "knifeSlice2", "impactPunch_medium_001"), 0.38f, P(0.2f));
    }
    public static void Shoot(World w, Vector2 pos) => At(w, pos, Pick("cloth1", "cloth2"), 0.3f, 0.3f + P(0.1f));
    public static void ArrowHit(World w, Vector2 pos) => At(w, pos, Pick("impactGeneric_light_000", "impactGeneric_light_001"), 0.25f, P(0.2f));
    public static void Death(World w, Vector2 pos) => At(w, pos, "impactPunch_heavy_000", 0.3f, -0.2f + P(0.1f));
    public static void Ability() => Assets.Play("maximize_006", 0.5f, 0.3f);
    public static void Power() => Assets.Play("switch_002", 0.9f, -0.2f);

    // ---------- bandeiras, alertas e fim de jogo ----------
    public static void FlagCaptured() => Assets.Play("jingles_PIZZI01", 0.6f, 0, music: true);
    public static void FlagLost() => Assets.Play("jingles_HIT05", 0.55f, 0, music: true);

    public static void UnderAttack()
    {
        if (Environment.TickCount - _lastAttack < 3000) return;
        _lastAttack = Environment.TickCount;
        Assets.Play("metalLatch", 0.7f, -0.2f);
    }

    /// <summary>Alarme da capital: sino pesado (dois toques).</summary>
    public static void CapitalAttacked()
    {
        if (Environment.TickCount - _lastCapital < 3000) return;
        _lastCapital = Environment.TickCount;
        Assets.Play("impactBell_heavy_000", 0.9f);
        Assets.Play("impactBell_heavy_001", 0.7f, -0.15f);
    }

    public static void Victory() => Assets.Play("jingles_STEEL00", 0.8f, 0, music: true);
    public static void Defeat() => Assets.Play("jingles_HIT10", 0.7f, 0, music: true);
    public static void StepDone() => Assets.Play("jingles_PIZZI10", 0.55f, 0, music: true);
}
