namespace GuerraDasBandeiras.Gameplay;

/// <summary>
/// Ficha curta de cada facção, montada a partir dos dados vivos do jogo (Defs + Balance).
/// Se o balance.json mudar, a ficha muda junto — não há texto fixo com números.
/// </summary>
public static class FactionSheet
{
    public record Row(string Label, string Value);

    static string P(float m) => $"{Math.Round((m - 1) * 100):0}%";
    static string R(float r) => $"{Math.Round(r * 100):0}%";

    public static string Strength(FactionId id) => id switch
    {
        FactionId.Amazonas => "Domínio das áreas de mata",
        FactionId.MinasGerais => "Fortificações e avanço seguro",
        _ => "Mobilidade em campo aberto",
    };

    public static string SceneName(FactionId id) => id switch
    {
        FactionId.Amazonas => "Guardiões da Mata",
        FactionId.MinasGerais => "Fortaleza das Serras",
        _ => "Lanceiros dos Pampas",
    };

    /// <summary>Habilidade exclusiva em uma linha, com os números atuais.</summary>
    public static string Ability(FactionId id)
    {
        var c = Balance.C;
        return id switch
        {
            FactionId.Amazonas => $"Camuflagem (na mata): +{P(c.CamoAmbushDamage)} no 1º ataque oculto",
            FactionId.MinasGerais => $"Postura de Pedra: -{R(c.StanceReduction)} de dano por {c.StanceDuration:0}s",
            _ => $"Investida: {c.ChargeDamage:0.0#}x de dano e atordoa {c.ChargeStun:0.0}s",
        };
    }

    /// <summary>Vantagem econômica: o recurso com bônus de coleta.</summary>
    public static string Economy(FactionDef f)
    {
        int best = 0;
        for (int i = 1; i < 3; i++) if (f.Gather[i] > f.Gather[best]) best = i;
        return $"+{P(f.Gather[best])} {Defs.ResName((Res)best)}";
    }

    /// <summary>Fraqueza principal, com os números atuais.</summary>
    public static string Weakness(FactionDef f) => f.Id switch
    {
        FactionId.Amazonas => $"Campo aberto: recebe +{P(f.OpenFieldDamageTaken)} de dano",
        FactionId.MinasGerais => $"Tropas {R(1 - f.UnitSpeed)} mais lentas",
        _ => $"Mata fechada ({R(f.ForestSpeed)} da velocidade) e lanceiros",
    };

    public static List<Row> Build(FactionDef f)
    {
        var sp = Defs.Units[f.Special];
        return new List<Row>
        {
            new("Estilo", f.Style),
            new("Comandante", $"{f.Commander} · poder {f.Power}"),
            new("Unidade especial", sp.Name),
            new("Habilidade", Ability(f.Id)),
            new("Ponto forte", $"{Strength(f.Id)} · {Economy(f)}"),
            new("Ponto fraco", Weakness(f)),
        };
    }
}
