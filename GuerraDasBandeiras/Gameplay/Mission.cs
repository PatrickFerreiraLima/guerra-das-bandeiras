using GuerraDasBandeiras.Core;

namespace GuerraDasBandeiras.Gameplay;

/// <summary>
/// Missão demonstrativa "A primeira bandeira" (seção 05 do caderno):
/// coletar 100 de madeira, erguer um quartel, recrutar três soldados, tomar a bandeira junto ao rio,
/// capturar o segundo ponto e resistir ao contra-ataque.
/// </summary>
public class Mission
{
    public record Step(string Goal, string Hint);

    public static readonly Step[] Steps =
    {
        new("Colete 100 de madeira",
            "Clique em um trabalhador para selecioná-lo. Depois clique com o botão DIREITO numa árvore: ele corta a madeira e leva até o Centro."),
        new("Construa um Quartel",
            "Com um trabalhador selecionado, clique em QUARTEL no painel de baixo e escolha um lugar livre perto da base."),
        new("Recrute 3 soldados",
            "Clique no Quartel pronto e treine Espadachins, Lanceiros ou Atiradores. Se faltar população, construa uma Casa."),
        new("Tome a Bandeira do Sul",
            "Arraste uma caixa com o mouse para selecionar os soldados e clique com o botão direito perto da bandeira, no rio. Fiquem lá até ela mudar de cor."),
        new("Capture a Bandeira do Norte",
            "Leve seu grupo até a outra travessia do rio. Dica: o batedor anda rápido e enxerga longe."),
        new("Resista ao contra-ataque!",
            "Uma força rival marcha para retomar as bandeiras. Derrote todos os atacantes. Lanceiros seguram cavalaria; atiradores castigam infantaria."),
    };

    readonly World _w;
    public int Index { get; private set; }
    public Step Current => Steps[Math.Min(Index, Steps.Length - 1)];
    public float StepAnim;
    readonly List<Unit> _counter = new();
    public Action<int> StepCompleted;

    public Mission(World w)
    {
        _w = w;
        _w.HoldToWin = false;
    }

    public string Progress()
    {
        var p = _w.Players[0];
        return Index switch
        {
            0 => $"{Math.Min(100, p.Gathered[(int)Res.Wood])}/100",
            2 => $"{Math.Min(3, p.MilitaryTrained)}/3",
            5 => $"restam {_counter.Count(u => !u.Dead)}",
            _ => ""
        };
    }

    public void Update(float dt)
    {
        StepAnim += dt;
        if (_w.Winner >= 0 || Index >= Steps.Length) return;
        var p = _w.Players[0];
        bool done = Index switch
        {
            0 => p.Gathered[(int)Res.Wood] >= 100,
            1 => _w.Buildings.Any(b => b.Team == 0 && b.Done && b.Def.Type == BuildingType.Quartel),
            2 => p.MilitaryTrained >= 3,
            3 => _w.Flags[1].Owner == 0,
            4 => _w.Flags[0].Owner == 0,
            5 => _counter.Count > 0 && _counter.All(u => u.Dead),
            _ => false
        };
        if (!done) return;

        Sfx.StepDone();
        StepCompleted?.Invoke(Index);
        Index++;
        StepAnim = 0;

        if (Index == 5) SpawnCounterAttack();
        if (Index >= Steps.Length) _w.SetWinner(0, "Missão cumprida! A vila está segura e as duas bandeiras são suas.");
    }

    void SpawnCounterAttack()
    {
        var enemy = _w.Players[1];
        var origin = _w.CenterOf(new Point(_w.W - 6, 12));
        var types = new[] { UnitType.Swordsman, UnitType.Swordsman, UnitType.Spearman, UnitType.Archer, UnitType.Archer, enemy.Faction.Special };
        for (int i = 0; i < types.Length; i++)
        {
            var pos = origin + new Vector2((i % 3) * 36 - 36, (i / 3) * 36);
            var t = _w.TileOf(pos);
            if (!_w.Passable(t)) pos = origin;
            var u = _w.AddUnit(types[i], 1, pos);
            u.CmdAttackMove(_w.Flags[0].Pos);
            _counter.Add(u);
        }
        _w.Notify?.Invoke("Contra-ataque! Tropas rivais descem da serra.", Pal.Red);
        Sfx.CapitalAttacked();
    }
}
