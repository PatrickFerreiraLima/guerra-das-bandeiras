using GuerraDasBandeiras.Core;

namespace GuerraDasBandeiras.Gameplay;

public class Player
{
    public int Id;
    public FactionDef Faction;
    public int ColorSet;
    public bool IsAI;
    public Color Color => Defs.SetColors[ColorSet];

    public int Food, Wood, Ore;
    public int Pop, PopCap;
    public float GatherMult = 1f, ProdMult = 1f;

    // poder do comandante
    public float PowerCd = 30f;   // começa com uma recarga curta
    public float PowerTime;
    public bool PowerActive => PowerTime > 0;
    public bool PowerReady => PowerCd <= 0 && PowerTime <= 0;

    public readonly int[] Gathered = new int[3];
    public int UnitsTrained, MilitaryTrained, Kills, Losses, BuildingsBuilt, PowersUsed, AbilitiesUsed;
    // estatísticas para o diagnóstico de balanceamento
    public readonly int[] Spent = new int[3];
    public int UnitsLost, BuildingsLost, FlagsCaptured;
    public float FirstFlagTime = -1, FirstAttackTime = -1;

    public int Get(Res r) => r switch { Res.Food => Food, Res.Wood => Wood, _ => Ore };
    public void Add(Res r, int n)
    {
        switch (r) { case Res.Food: Food += n; break; case Res.Wood: Wood += n; break; default: Ore += n; break; }
        if (n > 0) Gathered[(int)r] += n;
    }
    public bool CanPay(Cost c) => Food >= c.Food && Wood >= c.Wood && Ore >= c.Ore;
    public void Pay(Cost c) { Food -= c.Food; Wood -= c.Wood; Ore -= c.Ore; Spent[0] += c.Food; Spent[1] += c.Wood; Spent[2] += c.Ore; }
    public void Refund(Cost c) { Food += c.Food; Wood += c.Wood; Ore += c.Ore; Spent[0] -= c.Food; Spent[1] -= c.Wood; Spent[2] -= c.Ore; }

    public string Missing(Cost c)
    {
        var m = new List<string>();
        if (Food < c.Food) m.Add($"{c.Food - Food} de comida");
        if (Wood < c.Wood) m.Add($"{c.Wood - Wood} de madeira");
        if (Ore < c.Ore) m.Add($"{c.Ore - Ore} de minério");
        return "Faltam " + string.Join(", ", m);
    }
}

public abstract class Entity
{
    public int Id;
    public int Team = -1;
    public Vector2 Pos;
    public float Hp, MaxHp;
    public bool Dead;
    public float HitFlash;
    public World World;

    public virtual float Radius => 12f;
    /// <summary>Retângulo em tiles ocupado (para pathfinding até o alvo).</summary>
    public virtual Rectangle TileRect => new((int)MathF.Floor(Pos.X / Defs.Tile), (int)MathF.Floor(Pos.Y / Defs.Tile), 1, 1);
    /// <summary>Retângulo em pixels de mundo (para distâncias e cliques).</summary>
    public virtual RectangleF WorldRect => new(Pos.X - Radius, Pos.Y - Radius, Radius * 2, Radius * 2);
    public virtual bool BlocksTile => false;
    public virtual float SortY => Pos.Y;
    public abstract string DisplayName { get; }
}

public class ResourceNode : Entity
{
    public Res Kind;
    public int Amount, Initial;
    public int Sprite;
    public Point Tile;
    public float Shake;

    public override Rectangle TileRect => new(Tile.X, Tile.Y, 1, 1);
    public override RectangleF WorldRect => new(Tile.X * Defs.Tile, Tile.Y * Defs.Tile, Defs.Tile, Defs.Tile);
    public override bool BlocksTile => true;
    public override float SortY => Pos.Y + 18;
    public override string DisplayName => Kind switch
    {
        Res.Food => "Arbusto de frutas",
        Res.Wood => "Árvore",
        _ => "Rocha de minério"
    };
    public Texture2D Texture => Assets.Env(Sprite);
    public float DrawScale => Kind switch { Res.Wood => Sprite == 1 || Sprite == 21 ? 1.05f : 1.15f, Res.Food => 1.15f, _ => 0.72f };
}

public class Building : Entity
{
    public BuildingDef Def;
    public Player Owner;
    public Point Tile;
    public float Progress;      // 0..1 durante a construção
    public bool Done;
    public readonly List<UnitType> Queue = new();
    public float ProdTimer;
    public bool PopBlocked;
    public Vector2? Rally;
    public Entity RallyTarget;
    public float AttackTimer;
    public float LastDamaged = -100f;

    public int Size => Def.Size;
    public bool Damaged => Done && Hp < MaxHp - 0.5f;
    public override float Radius => Size * Defs.Tile / 2f;
    public override Rectangle TileRect => new(Tile.X, Tile.Y, Size, Size);
    public override RectangleF WorldRect => new(Tile.X * Defs.Tile, Tile.Y * Defs.Tile, Size * Defs.Tile, Size * Defs.Tile);
    public override bool BlocksTile => true;
    public override float SortY => (Tile.Y + Size) * Defs.Tile - 6;
    public override string DisplayName => Def.Name;

    public Texture2D Texture => Assets.Structure(SpriteIndex);
    public int SpriteIndex => Owner?.Faction.BuildingSprite(Def.Type) ?? Def.Sprite;

    public float AttackRange => (Def.AttackRangeTiles + (Def.Type == BuildingType.Torre ? Owner.Faction.TowerRangeBonus : 0)) * Defs.Tile;

    public IEnumerable<UnitType> TrainOptions
    {
        get
        {
            foreach (var t in Def.Trains) yield return t;
            if (Def.TrainsSpecial) yield return Owner.Faction.Special;
        }
    }

    public void AddProgress(float amount)
    {
        if (Done) return;
        float before = Progress;
        Progress = Math.Min(1f, Progress + amount);
        Hp = Math.Min(MaxHp, Hp + (Progress - before) * MaxHp * 0.9f);
        if (Progress >= 1f)
        {
            Done = true;
            World.OnBuildingCompleted(this);
        }
    }
}

public enum FlagBonus { None, Food, Defense }

public class ControlPoint
{
    public string Name;
    public FlagBonus Bonus;
    public string BonusText => Bonus == FlagBonus.Food ? $"+{Math.Round((Balance.C.FlagFoodBonus - 1) * 100)}% comida" : Bonus == FlagBonus.Defense ? $"+{Math.Round(Balance.C.FlagDefense * 100)}% resistência" : "";
    public string Short => Bonus == FlagBonus.Food ? "Econômica" : "Militar";
    public Vector2 Pos;
    public int Owner = -1;
    public float Progress;      // -100..100 ; positivo = time 0, negativo = time 1
    public bool Contested;
    public int Capturer = -1;   // time que está avançando a captura agora
    public const float Radius = 2.6f * Defs.Tile;
}

public class Projectile
{
    public Vector2 Pos, Start;
    public Entity Target;
    public Entity Source;
    public float Damage;
    public int Team;
    public float Speed = 460f;
    public bool Done;
    public float Traveled, Total;
}

public class FloatText
{
    public Vector2 Pos;
    public string Text;
    public Color Color;
    public float Life = 1.2f;
}

public class Fx
{
    public const int Dust = 0, Ring = 1, Corpse = 2, Chip = 3, Smoke = 4, Spark = 5, Leaf = 6;
    public Vector2 Pos, Vel;
    public float Life, MaxLife;
    public float Size;
    public Color Color;
    public int Kind;
    public Texture2D Tex;
    public float Rot, Scale = 1f;
    public bool Flip;
}

/// <summary>Ordem enfileirada com Shift.</summary>
public record QueuedOrder(UnitOrder Kind, Vector2 Pos, Entity Target);

public class Decor
{
    public Vector2 Pos;
    public bool Structure;   // usa sprite de construção (moinho, etc.)
    public int Sprite;
    public float Scale;
}
