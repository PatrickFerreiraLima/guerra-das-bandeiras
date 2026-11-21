using System.Globalization;
using System.Text;
using System.Text.Json;
using GuerraDasBandeiras.Gameplay;

namespace GuerraDasBandeiras.Tools;

/// <summary>
/// "Fotografia" de todos os números que afetam o equilíbrio: balance.json + atributos das unidades,
/// construções e facções. O teste compara com Assets/Testes/balanco_referencia.txt para provar
/// que uma etapa de polimento não mexeu no balanceamento.
/// Gerar de novo (quando o balanceamento mudar de propósito): GuerraDasBandeiras --snapshot caminho
/// </summary>
public static class BalanceSnapshot
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Build()
    {
        var sb = new StringBuilder();
        sb.AppendLine(JsonSerializer.Serialize(Balance.C, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var u in Defs.Units.Values.OrderBy(u => u.Type))
            sb.AppendLine(string.Create(Inv, $"unidade {u.Type}: custo {u.Cost.Food}/{u.Cost.Wood}/{u.Cost.Ore} tempo {u.Time} vida {u.Hp} dano {u.Dmg} alcance {u.Range} recarga {u.Cooldown} vel {u.Speed} visão {u.Sight} pop {u.Pop} hab {u.AbilityDur}/{u.AbilityCd}"));
        foreach (var b in Defs.Buildings.Values.OrderBy(b => b.Type))
            sb.AppendLine(string.Create(Inv, $"construção {b.Type}: custo {b.Cost.Food}/{b.Cost.Wood}/{b.Cost.Ore} tempo {b.Time} vida {b.Hp} tamanho {b.Size} pop {b.PopBonus} ataque {b.AttackDmg}/{b.AttackRangeTiles}/{b.AttackCd}"));
        foreach (var f in Defs.Factions)
            sb.AppendLine(string.Create(Inv, $"facção {f.Id}: coleta {f.Gather[0]}/{f.Gather[1]}/{f.Gather[2]} mata {f.ForestSpeed} planície {f.PlainSpeed} vel {f.UnitSpeed} prédios {f.BuildingHp} campo {f.OpenFieldDamageTaken} torre {f.TowerRangeBonus} poder {f.PowerDur}/{f.PowerCd}"));
        return sb.ToString().Replace("\r\n", "\n");
    }

    public static void Save(string path) => File.WriteAllText(path, Build());
}
