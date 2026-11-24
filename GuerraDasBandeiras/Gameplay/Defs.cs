using GuerraDasBandeiras.Core;

namespace GuerraDasBandeiras.Gameplay;

public enum Res { Food, Wood, Ore }
public enum UnitType { Worker, Swordsman, Spearman, Archer, Scout, Ram, VigiaDaMata, GuardiaoDasGerais, LanceiroDosPampas }
public enum BuildingType { Centro, Casa, Coletor, Quartel, Torre, Oficina }
public enum FactionId { Amazonas, MinasGerais, RioGrandeDoSul }
public enum Terrain : byte { Grass, Forest, Hill, Water, Ford, Dirt }
public enum GameMode { Mission, Skirmish, Tutorial, Versus }

[Flags]
public enum Tag { None = 0, Infantry = 1, Ranged = 2, Cavalry = 4, Worker = 8, Heavy = 16, Stealth = 32, Siege = 64 }

/// <summary>Qual "papel" de sprite do pacote Kenney a unidade usa.</summary>
public enum Role { Worker, WorkerAlt, Staff, Sword, Heavy, Cleric, Ram }

public readonly record struct Cost(int Food, int Wood, int Ore)
{
    public int this[Res r] => r switch { Res.Food => Food, Res.Wood => Wood, _ => Ore };
    public string Short()
    {
        var parts = new List<string>();
        if (Food > 0) parts.Add($"{Food}C");
        if (Wood > 0) parts.Add($"{Wood}M");
        if (Ore > 0) parts.Add($"{Ore}Mi");
        return string.Join(" ", parts);
    }
    public string Long()
    {
        var parts = new List<string>();
        if (Food > 0) parts.Add($"{Food} comida");
        if (Wood > 0) parts.Add($"{Wood} madeira");
        if (Ore > 0) parts.Add($"{Ore} minério");
        return parts.Count == 0 ? "grátis" : string.Join(", ", parts);
    }
}

public class UnitDef
{
    public UnitType Type;
    public string Name, Desc, Strong, Weak;
    public Cost Cost;
    public float Time;
    public int Hp;
    public float Dmg;
    public float Range;       // distância de borda a borda, em pixels de mundo
    public float Cooldown;
    public float Speed;       // pixels de mundo por segundo
    public int Sight;         // em tiles
    public int Pop = 1;
    public Tag Tags;
    public Role Role;
    public float Scale = 1f;
    // habilidade acionável (só unidades especiais)
    public string Ability, AbilityDesc;
    public float AbilityCd, AbilityDur;
    public bool IsRanged => Tags.HasFlag(Tag.Ranged);
    public bool IsWorker => Tags.HasFlag(Tag.Worker);
    public bool IsSiege => Tags.HasFlag(Tag.Siege);
}

public class BuildingDef
{
    public BuildingType Type;
    public string Name, Desc;
    public Cost Cost;
    public float Time;
    public int Hp;
    public int Size;
    public int Sprite;
    public int PopBonus;
    public bool Dropoff;
    public UnitType[] Trains = Array.Empty<UnitType>();
    public bool TrainsSpecial;
    public float AttackDmg, AttackRangeTiles, AttackCd;
    public int Sight = 5;
}

public class FactionDef
{
    public FactionId Id;
    public string Name, Title, Abbrev, Style;
    public int ColorSet;
    public UnitType Special;
    public float[] Gather = { 1f, 1f, 1f };
    public float ForestSpeed = 0.75f;
    public float PlainSpeed = 1f;
    public float UnitSpeed = 1f;
    public float BuildingHp = 1f;
    public float OpenFieldDamageTaken = 1f;
    public float TowerRangeBonus = 0f;
    public string Bonus, Weakness, Pitch;
    // comandante (GDD 5.7): um passivo e um poder com recarga
    public string Commander, CommanderRole, Passive, PassiveDesc, Power, PowerDesc;
    public float PowerCd = 100f, PowerDur = 10f;
    public Color Color => Defs.SetColors[ColorSet];

    /// <summary>Arquitetura regional: cada facção usa prédios diferentes do pacote (índices de Structure).</summary>
    public Dictionary<BuildingType, int> Buildings = new();
    public int BuildingSprite(BuildingType t) => Buildings.TryGetValue(t, out var s) ? s : Defs.Buildings[t].Sprite;
}

public static class Defs
{
    public const int Tile = 64;
    public const float GatherInterval = 0.8f;
    public const int CarryCapacity = 10;
    public const int MaxPop = 80;

    // Conjuntos de sprites (Kenney Medieval RTS): Worker, WorkerAlt, Staff, Sword, Heavy, Cleric
    public static readonly int[][] SpriteSets =
    {
        new[] { 1, 24, 2, 3, 4, 5 },        // 0 azul
        new[] { 7, 6, 8, 9, 10, 11 },       // 1 vermelho
        new[] { 13, 12, 14, 15, 16, 17 },   // 2 verde
        new[] { 19, 18, 20, 21, 22, 23 },   // 3 cinza
    };
    public static readonly Color[] SetColors = { new(60, 130, 230), new(222, 70, 56), new(48, 168, 84), new(232, 178, 40) };

    /// <summary>
    /// Sprite da unidade: personagens em pixel art gerados por facção (Core/CharArt), com a cor do time nos detalhes.
    /// Sem facção informada, ela é deduzida do conjunto de cores padrão (verde = AM, amarelo = MG, vermelho = RS).
    /// </summary>
    public static Texture2D UnitSprite(UnitDef d, int colorSet, FactionId? faction = null) =>
        d.Role == Role.Ram ? Assets.Env(5)
        : CharArt.Get(d.Type, faction ?? colorSet switch { 2 => FactionId.Amazonas, 3 => FactionId.MinasGerais, _ => FactionId.RioGrandeDoSul }, colorSet);

    /// <summary>Escala de desenho no mundo para o sprite da unidade (os gerados têm mais resolução que os do pacote).</summary>
    public static float SpriteScale(UnitDef d) => d.Role == Role.Ram ? 0.9f : CharArt.WorldScale;

    public static readonly Dictionary<UnitType, UnitDef> Units = new()
    {
        [UnitType.Worker] = new UnitDef
        {
            Type = UnitType.Worker, Name = "Trabalhador", Desc = "Coleta comida, madeira e minério, ergue e repara construções.",
            Strong = "Economia", Weak = "Não luta bem", Cost = new(50, 0, 0), Time = 8, Hp = 40, Dmg = 3, Range = 8, Cooldown = 1.2f, Speed = 92, Sight = 5,
            Tags = Tag.Worker, Role = Role.Worker
        },
        [UnitType.Swordsman] = new UnitDef
        {
            Type = UnitType.Swordsman, Name = "Espadachim", Desc = "Infantaria de linha de frente. Alcança e derruba atiradores.",
            Strong = "Forte contra atiradores", Weak = "Fraco contra atiradores protegidos", Cost = new(35, 0, 15), Time = 10, Hp = 120, Dmg = 11, Range = 8, Cooldown = 1.0f, Speed = 78, Sight = 6,
            Tags = Tag.Infantry, Role = Role.Sword
        },
        [UnitType.Spearman] = new UnitDef
        {
            Type = UnitType.Spearman, Name = "Lanceiro", Desc = "Lança longa: segura cavalaria e batedores.",
            Strong = "Forte contra cavalaria", Weak = "Fraco contra atiradores", Cost = new(40, 20, 0), Time = 10, Hp = 95, Dmg = 9, Range = 14, Cooldown = 1.1f, Speed = 80, Sight = 6,
            Tags = Tag.Infantry, Role = Role.Staff
        },
        [UnitType.Archer] = new UnitDef
        {
            Type = UnitType.Archer, Name = "Atirador", Desc = "Pressiona à distância. Ótimo atrás de uma linha de lanceiros.",
            Strong = "Forte contra infantaria lenta", Weak = "Fraco contra cavalaria", Cost = new(40, 10, 0), Time = 11, Hp = 60, Dmg = 9, Range = 250, Cooldown = 1.5f, Speed = 80, Sight = 7,
            Tags = Tag.Ranged, Role = Role.Cleric
        },
        [UnitType.Scout] = new UnitDef
        {
            Type = UnitType.Scout, Name = "Batedor", Desc = "Muito rápido, enxerga longe e fica invisível dentro da mata.",
            Strong = "Exploração e emboscada", Weak = "Fraco contra lanceiros", Cost = new(55, 0, 10), Time = 9, Hp = 70, Dmg = 6, Range = 8, Cooldown = 0.9f, Speed = 150, Sight = 9,
            Tags = Tag.Cavalry | Tag.Stealth, Role = Role.WorkerAlt
        },
        [UnitType.Ram] = new UnitDef
        {
            Type = UnitType.Ram, Name = "Aríete", Desc = "Cerco leve: derruba construções rapidamente, mas quase não fere tropas.",
            Strong = "Destrói construções", Weak = "Indefeso contra ataque direto", Cost = new(0, 120, 40), Time = 18, Hp = 220, Dmg = 45, Range = 8, Cooldown = 2.5f, Speed = 55, Sight = 4, Pop = 3,
            Tags = Tag.Siege, Role = Role.Ram, Scale = 1.1f
        },
        [UnitType.VigiaDaMata] = new UnitDef
        {
            Type = UnitType.VigiaDaMata, Name = "Vigia da Mata", Desc = "Especial do Amazonas: arqueiro leve, invisível na mata e com +2 de visão entre as árvores.",
            Strong = "Emboscada a distância", Weak = "Frágil em campo aberto", Cost = new(60, 40, 30), Time = 14, Hp = 75, Dmg = 13, Range = 280, Cooldown = 1.4f, Speed = 92, Sight = 8,
            Tags = Tag.Ranged | Tag.Stealth, Role = Role.Cleric, Scale = 1.15f,
            Ability = "Camuflagem", AbilityDesc = "Fica invisível em qualquer terreno por 8s. O primeiro disparo escondido causa dano dobrado (Emboscada) e revela a posição.",
            AbilityCd = 22, AbilityDur = 8
        },
        [UnitType.GuardiaoDasGerais] = new UnitDef
        {
            Type = UnitType.GuardiaoDasGerais, Name = "Guardião das Gerais", Desc = "Especial de Minas: escudo pesado, recebe 60% menos dano de projéteis e protege aliados próximos de flechas (-25%).",
            Strong = "Ancora a linha defensiva", Weak = "Lento, sofre com cerco e flanco", Cost = new(60, 0, 60), Time = 15, Hp = 230, Dmg = 13, Range = 8, Cooldown = 1.2f, Speed = 64, Sight = 6,
            Tags = Tag.Infantry | Tag.Heavy, Role = Role.Heavy, Scale = 1.2f,
            Ability = "Postura de Pedra", AbilityDesc = "Por 7s recebe 65% menos dano de tudo, mas anda na metade da velocidade.",
            AbilityCd = 18, AbilityDur = 7
        },
        [UnitType.LanceiroDosPampas] = new UnitDef
        {
            Type = UnitType.LanceiroDosPampas, Name = "Lanceiro dos Pampas", Desc = "Especial do RS: cavalaria de carga. Depois de correr, o primeiro golpe causa dano dobrado (triplo contra unidades expostas).",
            Strong = "Ataques rápidos e flanco", Weak = "Lanceiros e passagens estreitas", Cost = new(90, 20, 30), Time = 15, Hp = 130, Dmg = 13, Range = 12, Cooldown = 1.1f, Speed = 125, Sight = 7, Pop = 2,
            Tags = Tag.Cavalry, Role = Role.Staff, Scale = 1.15f,
            Ability = "Investida", AbilityDesc = "Dispara a +60% de velocidade por 4s. O próximo golpe causa 2,5x de dano e atordoa o alvo.",
            AbilityCd = 16, AbilityDur = 4
        },
    };

    public static readonly Dictionary<BuildingType, BuildingDef> Buildings = new()
    {
        [BuildingType.Centro] = new BuildingDef
        {
            Type = BuildingType.Centro, Name = "Centro de Comando", Desc = "Treina trabalhadores e recebe recursos. Se cair, a partida acaba.",
            Cost = new(0, 300, 100), Time = 60, Hp = 1800, Size = 3, Sprite = 6, PopBonus = 10, Dropoff = true,
            Trains = new[] { UnitType.Worker }, AttackDmg = 6, AttackRangeTiles = 5, AttackCd = 1.6f, Sight = 8
        },
        [BuildingType.Casa] = new BuildingDef
        {
            Type = BuildingType.Casa, Name = "Casa", Desc = "Aumenta a população máxima em 5.",
            Cost = new(0, 40, 0), Time = 14, Hp = 400, Size = 2, Sprite = 18, PopBonus = 5
        },
        [BuildingType.Coletor] = new BuildingDef
        {
            Type = BuildingType.Coletor, Name = "Coletor", Desc = "Depósito de recursos. Construa perto de árvores, minas ou frutas para encurtar o caminho.",
            Cost = new(0, 60, 0), Time = 16, Hp = 500, Size = 2, Sprite = 21, Dropoff = true
        },
        [BuildingType.Quartel] = new BuildingDef
        {
            Type = BuildingType.Quartel, Name = "Quartel", Desc = "Treina espadachins, lanceiros e atiradores.",
            Cost = new(0, 100, 0), Time = 28, Hp = 900, Size = 3, Sprite = 9,
            Trains = new[] { UnitType.Swordsman, UnitType.Spearman, UnitType.Archer }
        },
        [BuildingType.Torre] = new BuildingDef
        {
            Type = BuildingType.Torre, Name = "Torre", Desc = "Defesa de passagem: atira flechas automaticamente em inimigos próximos.",
            Cost = new(0, 80, 30), Time = 22, Hp = 700, Size = 2, Sprite = 8, AttackDmg = 10, AttackRangeTiles = 5.5f, AttackCd = 1.4f, Sight = 8
        },
        [BuildingType.Oficina] = new BuildingDef
        {
            Type = BuildingType.Oficina, Name = "Oficina", Desc = "Treina batedores, aríetes de cerco e a unidade especial da sua facção.",
            Cost = new(0, 80, 40), Time = 26, Hp = 800, Size = 2, Sprite = 20,
            Trains = new[] { UnitType.Scout, UnitType.Ram }, TrainsSpecial = true
        },
    };

    public static readonly FactionDef[] Factions =
    {
        new FactionDef
        {
            Buildings = new() { [BuildingType.Centro] = 9, [BuildingType.Casa] = 23, [BuildingType.Coletor] = 21, [BuildingType.Quartel] = 3, [BuildingType.Torre] = 8, [BuildingType.Oficina] = 20 },
            Id = FactionId.Amazonas, Name = "Amazonas", Title = "Liga das Correntes", Abbrev = "AM", Style = "Emboscada e mobilidade",
            ColorSet = 2, Special = UnitType.VigiaDaMata, Gather = new[] { 1f, 1.3f, 1f }, ForestSpeed = 1.05f,
            OpenFieldDamageTaken = 1.15f,
            Pitch = "Mobilidade e informação: batedores rápidos, atalhos florestais e coleta de madeira favorecida.",
            Bonus = "+30% na coleta de madeira\nTropas rápidas dentro da mata\nAtaques saindo da mata: Emboscada (+50%)",
            Weakness = "Defesa em campo aberto (+15% de dano fora da mata)",
            Commander = "Mara das Águas", CommanderRole = "Especialista em emboscadas e resgate",
            Passive = "Resgate", PassiveDesc = "Suas unidades dentro da mata recuperam vida aos poucos.",
            Power = "Véu da Mata", PowerDesc = "Todas as suas unidades ficam camufladas por 10s. Quem atacar escondido faz Emboscada.",
            PowerCd = 100, PowerDur = 10
        },
        new FactionDef
        {
            Buildings = new() { [BuildingType.Centro] = 6, [BuildingType.Casa] = 11, [BuildingType.Coletor] = 7, [BuildingType.Quartel] = 2, [BuildingType.Torre] = 5, [BuildingType.Oficina] = 20 },
            Id = FactionId.MinasGerais, Name = "Minas Gerais", Title = "Guardiões da Serra", Abbrev = "MG", Style = "Defesa e avanço paciente",
            ColorSet = 3, Special = UnitType.GuardiaoDasGerais, Gather = new[] { 1f, 1f, 1.3f }, BuildingHp = 1.4f, UnitSpeed = 0.9f,
            TowerRangeBonus = 1.5f,
            Pitch = "Defesa e avanço paciente: mineração eficiente, muralhas fortes e reparos baratos.",
            Bonus = "+30% na coleta de minério\nConstruções com +40% de vida\nTorres com mais alcance",
            Weakness = "Unidades mais lentas (-10% de velocidade)",
            Commander = "Tomás Ferraz", CommanderRole = "Mestre em cerco e proteção de corredores",
            Passive = "Canteiro de Obras", PassiveDesc = "Reparos são grátis e 50% mais rápidos; construções prontas se recuperam sozinhas.",
            Power = "Fortificar", PowerDesc = "Por 15s suas construções recebem 60% menos dano e recuperam 20% da vida na hora.",
            PowerCd = 100, PowerDur = 15
        },
        new FactionDef
        {
            Buildings = new() { [BuildingType.Centro] = 19, [BuildingType.Casa] = 1, [BuildingType.Coletor] = 22, [BuildingType.Quartel] = 16, [BuildingType.Torre] = 8, [BuildingType.Oficina] = 20 },
            Id = FactionId.RioGrandeDoSul, Name = "Rio Grande do Sul", Title = "Confederação dos Pampas", Abbrev = "RS", Style = "Ataque rápido e manobra",
            ColorSet = 1, Special = UnitType.LanceiroDosPampas, Gather = new[] { 1.3f, 1f, 1f }, PlainSpeed = 1.12f, ForestSpeed = 0.6f,
            Pitch = "Ataque rápido e manobra: fazendas produtivas, campo aberto e cavalaria de choque.",
            Bonus = "+30% na coleta de comida\n+12% de velocidade nas planícies\nCavalaria com +15% de dano",
            Weakness = "Florestas (muito lentos na mata) e lanceiros inimigos",
            Commander = "Helena Dornelles", CommanderRole = "Especialista em ataques coordenados",
            Passive = "Estância", PassiveDesc = "Sua cavalaria causa +15% de dano.",
            Power = "Ataque Coordenado", PowerDesc = "Por 10s suas tropas ganham +40% de velocidade e +25% de dano.",
            PowerCd = 100, PowerDur = 10
        },
    };

    public static FactionDef Faction(FactionId id) => Factions[(int)id];

    public static string ResName(Res r) => r switch { Res.Food => "comida", Res.Wood => "madeira", _ => "minério" };
    public static Color ResColor(Res r) => r switch { Res.Food => Pal.Food, Res.Wood => Pal.Wood, _ => Pal.Ore };
    public static Texture2D ResIcon(Res r) => r switch { Res.Food => Assets.Env(19), Res.Wood => Assets.Env(5), _ => Assets.Env(11) };
}

public class GameConfig
{
    public GameMode Mode = GameMode.Skirmish;
    public FactionId PlayerFaction = FactionId.Amazonas;
    public FactionId EnemyFaction = FactionId.MinasGerais;
    public int Difficulty = 1; // 0 fácil, 1 normal, 2 difícil
    public int Seed = Environment.TickCount;
    // ---- 1x1 em rede (GameMode.Versus): o anfitrião é o time 0, quem entra na sala é o time 1 ----
    /// <summary>Time controlado nesta máquina (0 fora do modo em rede).</summary>
    public int LocalTeam;
    /// <summary>Nome de cada time (anfitrião, convidado) mostrado na partida em rede.</summary>
    public string[] Names = { "Você", "Inimigo" };
    public bool Versus => Mode == GameMode.Versus;
    /// <summary>Sorteia onde fica cada base (lado do rio, norte/centro/sul). Tutorial e missão sempre usam o lugar clássico.</summary>
    public bool RandomBases = true;
    public bool UsesRandomBases => RandomBases && Mode is GameMode.Skirmish or GameMode.Versus;
    public static readonly string[] DifficultyNames = { "Fácil", "Normal", "Difícil" };
}
