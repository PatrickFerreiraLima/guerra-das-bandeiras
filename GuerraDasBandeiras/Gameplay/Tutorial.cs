using GuerraDasBandeiras.Core;

namespace GuerraDasBandeiras.Gameplay;

/// <summary>
/// Tutorial curto e opcional, no mapa normal e com os sistemas existentes:
/// 1 selecionar trabalhadores, 2 coletar madeira e alimento, 3 construir quartel,
/// 4 recrutar soldados, 5 atacar um alvo, 6 capturar uma bandeira.
/// Cada passo informa o que destacar na tela (mundo e/ou botão do painel).
/// </summary>
public class Tutorial
{
    public record Step(string Goal, string Hint);

    public static readonly Step[] Steps =
    {
        new("Selecione seus trabalhadores",
            "Segure o botão esquerdo e arraste uma caixa sobre os trabalhadores perto do Centro."),
        new("Colete madeira e alimento",
            "Com eles selecionados, clique com o botão DIREITO numa árvore. Depois selecione um só e mande colher frutas (arbustos vermelhos)."),
        new("Construa um Quartel",
            "Selecione um trabalhador e clique no botão QUARTEL (painel de baixo). Escolha um lugar livre e clique."),
        new("Recrute 2 soldados",
            "Clique no Quartel pronto e treine dois soldados. O custo aparece no botão; o que faltar fica em vermelho."),
        new("Ataque o acampamento inimigo",
            "Arraste para selecionar os soldados e clique com o botão DIREITO no acampamento destacado (ou aperte F e clique)."),
        new("Capture uma bandeira",
            "Leve os soldados até a bandeira destacada no rio e fiquem perto até ela mudar de cor. Só soldados capturam."),
    };

    readonly World _w;
    public int Index { get; private set; }
    public Step Current => Steps[Math.Min(Index, Steps.Length - 1)];
    public float StepAnim;
    public Action<int> StepCompleted;
    public Building Camp { get; private set; }
    int _woodStart, _foodStart;

    public Tutorial(World w)
    {
        _w = w;
        _w.HoldToWin = false;
        var p = w.Players[0];
        _woodStart = p.Gathered[(int)Res.Wood];
        _foodStart = p.Gathered[(int)Res.Food];
    }

    public string Progress()
    {
        var p = _w.Players[0];
        return Index switch
        {
            1 => $"madeira {Math.Min(20, p.Gathered[(int)Res.Wood] - _woodStart)}/20 · comida {Math.Min(20, p.Gathered[(int)Res.Food] - _foodStart)}/20",
            3 => $"{Math.Min(2, p.MilitaryTrained)}/2",
            _ => ""
        };
    }

    /// <summary>Pontos do mapa que devem pulsar neste passo.</summary>
    public IEnumerable<Vector2> WorldHighlights()
    {
        switch (Index)
        {
            case 0:
                foreach (var u in _w.Units.Where(u => u.Team == 0 && u.IsWorker).Take(3)) yield return u.Pos;
                break;
            case 1:
            {
                var c = _w.CenterOf(_w.StartTiles[0]);
                var tree = _w.NearestResource(Res.Wood, c, 20 * World.T);
                var food = _w.NearestResource(Res.Food, c, 20 * World.T);
                if (tree != null) yield return tree.Pos;
                if (food != null) yield return food.Pos;
                break;
            }
            case 3:
                foreach (var b in _w.Buildings.Where(b => b.Team == 0 && b.Def.Type == BuildingType.Quartel)) yield return b.Pos;
                break;
            case 4:
                if (Camp != null && !Camp.Dead) yield return Camp.Pos;
                break;
            case 5:
                yield return _w.Flags[1].Pos;
                break;
        }
    }

    /// <summary>Qual botão do painel destacar: "build:Quartel", "train" ou "attack".</summary>
    public string HudHighlight => Index switch { 2 => "build:Quartel", 3 => "train", 4 => "attack", _ => null };

    public void Update(float dt, int workersSelected)
    {
        StepAnim += dt;
        if (_w.Winner >= 0 || Index >= Steps.Length) return;
        var p = _w.Players[0];
        bool done = Index switch
        {
            0 => workersSelected >= 2,
            1 => p.Gathered[(int)Res.Wood] - _woodStart >= 20 && p.Gathered[(int)Res.Food] - _foodStart >= 20,
            2 => _w.Buildings.Any(b => b.Team == 0 && b.Done && b.Def.Type == BuildingType.Quartel),
            3 => p.MilitaryTrained >= 2,
            4 => Camp != null && Camp.Dead,
            5 => _w.Flags.Any(f => f.Owner == 0),
            _ => false
        };
        if (!done) return;

        Sfx.StepDone();
        StepCompleted?.Invoke(Index);
        Index++;
        StepAnim = 0;

        if (Index == 4) SpawnCamp();
        if (Index >= Steps.Length) _w.SetWinner(0, "Tutorial concluído! Você já sabe coletar, construir, recrutar, atacar e capturar.");
    }

    /// <summary>Acampamento inimigo fraco, perto da base, para o passo de ataque.</summary>
    void SpawnCamp()
    {
        var anchor = new Point(_w.StartTiles[0].X + 12, _w.StartTiles[0].Y - 4);
        for (int ring = 0; ring < 8 && Camp == null; ring++)
            for (int dx = -ring; dx <= ring && Camp == null; dx++)
                for (int dy = -ring; dy <= ring && Camp == null; dy++)
                {
                    var tl = new Point(anchor.X + dx, anchor.Y + dy);
                    if (!_w.CanPlace(BuildingType.Casa, tl, 1)) continue;
                    Camp = _w.AddBuilding(BuildingType.Casa, 1, tl, true);
                }
        if (Camp == null) return;
        Camp.Hp = Camp.MaxHp = 160;
        // deixa o acampamento visível no mapa
        for (int x = Camp.Tile.X - 2; x < Camp.Tile.X + 4; x++)
            for (int y = Camp.Tile.Y - 2; y < Camp.Tile.Y + 4; y++)
                if (_w.InBounds(new Point(x, y))) _w.Explored[x, y] = true;
        _w.Notify?.Invoke("Um acampamento inimigo apareceu perto da sua base!", Pal.Gold);
    }
}
