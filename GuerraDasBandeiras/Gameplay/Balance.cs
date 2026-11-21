using System.Text.Json;

namespace GuerraDasBandeiras.Gameplay;

/// <summary>
/// Todos os números de habilidades, comandantes, bandeiras e limites num lugar só.
/// Os valores padrão podem ser sobrescritos por Assets/balance.json (sem recompilar).
/// Multiplicadores de dano: 1.4 = +40%. Reduções: 0.35 = recebe 35% menos.
/// </summary>
public class BalanceConfig
{
    // --- Amazonas: Vigia da Mata / Camuflagem ---
    public float CamoDuration { get; set; } = 8f;
    public float CamoCooldown { get; set; } = 25f;
    public float CamoAmbushDamage { get; set; } = 1.4f;      // primeiro ataque oculto da Camuflagem
    public float ForestAmbushDamage { get; set; } = 1.2f;    // furtivos saindo da mata (Amazonas)
    public float CamoDetectRadius { get; set; } = 45f;       // inimigos a esta distância enxergam

    // --- Minas Gerais: Guardião / Postura de Pedra ---
    public float StanceDuration { get; set; } = 6f;
    public float StanceCooldown { get; set; } = 28f;
    public float StanceReduction { get; set; } = 0.35f;
    public float StanceSpeed { get; set; } = 0.6f;
    public float GuardianArrowTaken { get; set; } = 0.5f;    // dano de projéteis recebido pelo Guardião
    public float ShieldWallReduction { get; set; } = 0.15f;  // aliados perto do Guardião contra flechas

    // --- Rio Grande do Sul: Lanceiro dos Pampas / Investida ---
    public float ChargeDuration { get; set; } = 4f;
    public float ChargeCooldown { get; set; } = 18f;
    public float ChargeDamage { get; set; } = 1.6f;
    public float ChargeStun { get; set; } = 0.6f;
    public float ChargeSpeed { get; set; } = 1.4f;
    public float RunChargeDamage { get; set; } = 1.3f;       // passivo: golpe depois de correr
    public float RunChargeExposed { get; set; } = 1.5f;      // idem contra atiradores/trabalhadores/cerco

    // --- Comandantes ---
    public float VeuDuration { get; set; } = 8f;
    public float VeuCooldown { get; set; } = 120f;
    public float ForestRegen { get; set; } = 1.0f;           // vida/s na mata (passivo Amazonas)
    public float FortifyDuration { get; set; } = 12f;
    public float FortifyCooldown { get; set; } = 120f;
    public float FortifyReduction { get; set; } = 0.35f;
    public float FortifyHeal { get; set; } = 0.10f;
    public float CoordDuration { get; set; } = 8f;
    public float CoordCooldown { get; set; } = 120f;
    public float CoordSpeed { get; set; } = 1.15f;
    public float CoordDamage { get; set; } = 1.1f;
    public float CavalryPassive { get; set; } = 1.1f;        // passivo RS
    public float FirstPowerDelay { get; set; } = 60f;

    // --- Limites contra combinações exageradas ---
    public float MaxDamageReduction { get; set; } = 0.5f;    // nunca recebe menos que 50% do dano
    public float MaxBonusDamage { get; set; } = 1.8f;        // bônus somados nunca passam de +80%

    // --- Economia e terreno das facções ---
    public float AmWoodBonus { get; set; } = 1.3f;
    public float AmOpenFieldPenalty { get; set; } = 1.06f;   // dano extra recebido fora da mata
    public float MgOreBonus { get; set; } = 1.3f;
    public float MgUnitSpeed { get; set; } = 0.9f;
    public float RsFoodBonus { get; set; } = 1.12f;
    public float RsPlainSpeed { get; set; } = 1.05f;

    // --- Ritmo da IA (Normal; Fácil e Difícil ajustam a partir daqui) ---
    public float AiFirstAttack { get; set; } = 300f;    // segundos até o primeiro grande ataque
    public int AiWaveSize { get; set; } = 8;            // soldados na primeira onda (cresce 3 por onda)

    // --- Bandeiras ---
    public float FlagFoodBonus { get; set; } = 1.10f;        // Bandeira do Sul (econômica)
    public float FlagDefense { get; set; } = 0.10f;          // Bandeira do Norte (militar)
}

public static class Balance
{
    public static BalanceConfig C { get; private set; } = new();
    public static string Source { get; private set; } = "padrão";

    public static void Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                C = JsonSerializer.Deserialize<BalanceConfig>(File.ReadAllText(path), new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) ?? new();
                Source = path;
            }
        }
        catch { C = new(); Source = "padrão (balance.json inválido)"; }
        Apply();
    }

    public static void Save(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(C, new JsonSerializerOptions { WriteIndented = true }));

    /// <summary>Copia os valores para as definições e reescreve os textos com os números atuais.</summary>
    public static void Apply()
    {
        var c = C;
        static string P(float m) => $"{Math.Round((m - 1) * 100):0}%";
        static string R(float r) => $"{Math.Round(r * 100):0}%";

        var vig = Defs.Units[UnitType.VigiaDaMata];
        vig.AbilityDur = c.CamoDuration; vig.AbilityCd = c.CamoCooldown;
        vig.AbilityDesc = $"Só pode ser ativada dentro da mata. Fica camuflado por até {c.CamoDuration:0}s (sair da mata desfaz). O primeiro ataque oculto causa +{P(c.CamoAmbushDamage)} de dano.";

        var gua = Defs.Units[UnitType.GuardiaoDasGerais];
        gua.AbilityDur = c.StanceDuration; gua.AbilityCd = c.StanceCooldown;
        gua.AbilityDesc = $"Por {c.StanceDuration:0}s recebe {R(c.StanceReduction)} menos dano, mas anda a {R(c.StanceSpeed)} da velocidade.";
        gua.Desc = $"Especial de Minas: escudo pesado (recebe {R(c.GuardianArrowTaken)} do dano de projéteis) e protege aliados próximos de flechas (-{R(c.ShieldWallReduction)}).";

        var pam = Defs.Units[UnitType.LanceiroDosPampas];
        pam.AbilityDur = c.ChargeDuration; pam.AbilityCd = c.ChargeCooldown;
        pam.AbilityDesc = $"Corre a +{P(c.ChargeSpeed)} por {c.ChargeDuration:0}s. O próximo golpe causa {c.ChargeDamage:0.0#}x de dano e atordoa por {c.ChargeStun:0.0}s.";
        pam.Desc = $"Especial do RS: cavalaria de carga. Depois de correr, o golpe causa +{P(c.RunChargeDamage)} (+{P(c.RunChargeExposed)} contra unidades expostas).";

        var am = Defs.Faction(FactionId.Amazonas);
        am.Gather[1] = c.AmWoodBonus; am.OpenFieldDamageTaken = c.AmOpenFieldPenalty;
        am.Weakness = $"Defesa em campo aberto (+{P(c.AmOpenFieldPenalty)} de dano fora da mata)";
        am.PowerDur = c.VeuDuration; am.PowerCd = c.VeuCooldown;
        am.PowerDesc = $"Unidades suas que estiverem na mata ficam camufladas por {c.VeuDuration:0}s.";
        am.PassiveDesc = $"Suas unidades dentro da mata recuperam {c.ForestRegen:0.#} de vida por segundo.";

        var mg = Defs.Faction(FactionId.MinasGerais);
        mg.Gather[2] = c.MgOreBonus; mg.UnitSpeed = c.MgUnitSpeed;
        mg.Weakness = $"Unidades mais lentas (-{R(1 - c.MgUnitSpeed)} de velocidade)";
        mg.Bonus = $"+{P(c.MgOreBonus)} na coleta de minério\nConstruções com +40% de vida\nTorres com mais alcance";
        mg.PowerDur = c.FortifyDuration; mg.PowerCd = c.FortifyCooldown;
        mg.PowerDesc = $"Por {c.FortifyDuration:0}s suas construções recebem {R(c.FortifyReduction)} menos dano e recuperam {R(c.FortifyHeal)} da vida na hora.";

        var rs = Defs.Faction(FactionId.RioGrandeDoSul);
        rs.Gather[0] = c.RsFoodBonus; rs.PlainSpeed = c.RsPlainSpeed;
        rs.PowerDur = c.CoordDuration; rs.PowerCd = c.CoordCooldown;
        rs.PowerDesc = $"Por {c.CoordDuration:0}s suas tropas ganham +{P(c.CoordSpeed)} de velocidade e +{P(c.CoordDamage)} de dano.";
        rs.PassiveDesc = $"Sua cavalaria causa +{P(c.CavalryPassive)} de dano.";
        rs.Bonus = $"+{P(c.RsFoodBonus)} na coleta de comida\n+{P(c.RsPlainSpeed)} de velocidade nas planícies\nCavalaria com +{P(c.CavalryPassive)} de dano";
        am.Bonus = $"+{P(c.AmWoodBonus)} na coleta de madeira\nTropas rápidas dentro da mata\nEmboscadas saindo da mata (+{P(c.ForestAmbushDamage)})";
    }
}
