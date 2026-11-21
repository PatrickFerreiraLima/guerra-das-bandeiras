using System.Globalization;
using System.Text;
using GuerraDasBandeiras.Gameplay;

namespace GuerraDasBandeiras.Tools;

/// <summary>
/// Diagnóstico de balanceamento: "GuerraDasBandeiras --balance saida N"
/// Roda IA x IA em todos os confrontos, nas duas posições iniciais, com N sementes fixas (reproduzível).
/// Gera saida.csv (uma linha por partida) e saida.md (tabela por confronto e posição).
/// </summary>
public static class BalanceRunner
{
    const float Dt = 1 / 30f;
    const float TimeLimit = 1800f;
    static readonly string[] Ab = { "AM", "MG", "RS" };
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public class Row
    {
        public int A, B, Seed;          // A = oeste (time 0), B = leste (time 1)
        public int Winner = -1;         // 0 = oeste, 1 = leste, -1 = empate por tempo
        public string Reason = "tempo";
        public float Time;
        public int[][] Gathered = new int[2][], Spent = new int[2][];
        public int[] Produced = new int[2], Lost = new int[2], Alive = new int[2], Abil = new int[2], Pow = new int[2], Specials = new int[2], BLost = new int[2];
        public float[] FirstAttack = new float[2], FirstFlag = new float[2];
    }

    public static List<Row> RunAll(int seeds, Action<string> progress = null)
    {
        var rows = new List<Row>();
        var pairs = new[] { (0, 1), (0, 2), (1, 2) };
        foreach (var (x, y) in pairs)
            foreach (var (a, b) in new[] { (x, y), (y, x) })
                for (int s = 0; s < seeds; s++)
                {
                    rows.Add(RunOne(a, b, 1000 + s));
                    progress?.Invoke($"{Ab[a]} x {Ab[b]} seed {1000 + s}");
                }
        return rows;
    }

    public static Row RunOne(int a, int b, int seed)
    {
        var w = new World(new GameConfig { Mode = GameMode.Skirmish, PlayerFaction = (FactionId)a, EnemyFaction = (FactionId)b, Seed = seed, Difficulty = 1 });
        var ai0 = new AIController(w, 0);
        var ai1 = new AIController(w, 1);
        var r = new Row { A = a, B = b, Seed = seed };
        w.UnitTrained = u => { if (u.HasAbility) r.Specials[u.Team]++; };
        while (w.Winner < 0 && w.Time < TimeLimit)
        {
            w.Update(Dt); ai0.Update(Dt); ai1.Update(Dt);
        }
        r.Winner = w.Winner;
        r.Time = w.Time;
        r.Reason = w.Winner < 0 ? "tempo" : w.WinReason.Contains("Centro") ? "capital" : "bandeiras";
        for (int t = 0; t < 2; t++)
        {
            var p = w.Players[t];
            r.Gathered[t] = (int[])p.Gathered.Clone();
            r.Spent[t] = (int[])p.Spent.Clone();
            r.Produced[t] = p.UnitsTrained;
            r.Lost[t] = p.UnitsLost;
            r.BLost[t] = p.BuildingsLost;
            r.Alive[t] = w.Units.Count(u => u.Team == t);
            r.Abil[t] = p.AbilitiesUsed;
            r.Pow[t] = p.PowersUsed;
            r.FirstAttack[t] = p.FirstAttackTime;
            r.FirstFlag[t] = p.FirstFlagTime;
        }
        return r;
    }

    public static void Run(string outBase, int seeds)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var rows = RunAll(seeds);
        WriteCsv(outBase + ".csv", rows);
        File.WriteAllText(outBase + ".md", Summary(rows, $"{rows.Count} partidas, {seeds} sementes por confronto e posição, {sw.Elapsed.TotalSeconds:0} s de execução"));
    }

    static void WriteCsv(string path, List<Row> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("oeste,leste,seed,vencedor,motivo,minutos,col_comida_o,col_madeira_o,col_minerio_o,col_comida_l,col_madeira_l,col_minerio_l,gasto_o,gasto_l,produzidas_o,produzidas_l,perdidas_o,perdidas_l,vivas_o,vivas_l,predios_perdidos_o,predios_perdidos_l,especiais_o,especiais_l,habilidades_o,habilidades_l,poderes_o,poderes_l,1o_ataque_o,1o_ataque_l,1a_bandeira_o,1a_bandeira_l");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", new object[]
            {
                Ab[r.A], Ab[r.B], r.Seed, r.Winner < 0 ? "empate" : Ab[r.Winner == 0 ? r.A : r.B], r.Reason, (r.Time / 60).ToString("0.00", Inv),
                r.Gathered[0][0], r.Gathered[0][1], r.Gathered[0][2], r.Gathered[1][0], r.Gathered[1][1], r.Gathered[1][2],
                r.Spent[0].Sum(), r.Spent[1].Sum(), r.Produced[0], r.Produced[1], r.Lost[0], r.Lost[1], r.Alive[0], r.Alive[1], r.BLost[0], r.BLost[1],
                r.Specials[0], r.Specials[1], r.Abil[0], r.Abil[1], r.Pow[0], r.Pow[1],
                (r.FirstAttack[0] / 60).ToString("0.00", Inv), (r.FirstAttack[1] / 60).ToString("0.00", Inv),
                (r.FirstFlag[0] / 60).ToString("0.00", Inv), (r.FirstFlag[1] / 60).ToString("0.00", Inv)
            }));
        File.WriteAllText(path, sb.ToString());
    }

    static string F(double v, string fmt = "0") => v.ToString(fmt, Inv);

    public static string Summary(List<Row> rows, string header)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Diagnóstico de balanceamento\n\n{header}\n");

        // vitórias por facção
        var wins = new int[3]; var games = new int[3];
        foreach (var r in rows) { games[r.A]++; games[r.B]++; if (r.Winner == 0) wins[r.A]++; else if (r.Winner == 1) wins[r.B]++; }
        sb.AppendLine("## Vitórias por facção\n\n| Facção | Vitórias | Partidas | Taxa |\n|---|---|---|---|");
        for (int i = 0; i < 3; i++) sb.AppendLine($"| {Ab[i]} | {wins[i]} | {games[i]} | {F(100.0 * wins[i] / Math.Max(1, games[i]))}% |");
        int west = rows.Count(r => r.Winner == 0), east = rows.Count(r => r.Winner == 1), draws = rows.Count(r => r.Winner < 0);
        sb.AppendLine($"\nPor posição: oeste {west}, leste {east}, empates (30 min) {draws}.\n");

        sb.AppendLine("## Por confronto e posição (oeste x leste)\n");
        sb.AppendLine("| Confronto | Vit. oeste | Vit. leste | Empate | Bandeiras/Capital | Duração média (min) | Coletado O/L | Gasto O/L | Produzidas O/L | Perdidas O/L | Vivas O/L | Especiais O/L | Habil. O/L | Poderes O/L | 1º ataque O/L (min) | 1ª bandeira O/L (min) |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var g in rows.GroupBy(r => (r.A, r.B)))
        {
            var l = g.ToList();
            double Avg(Func<Row, double> f) => l.Average(f);
            double AvgPos(Func<Row, double> f) { var v = l.Select(f).Where(x => x >= 0).ToList(); return v.Count == 0 ? -1 : v.Average(); }
            sb.AppendLine($"| {Ab[g.Key.A]} x {Ab[g.Key.B]} | {l.Count(r => r.Winner == 0)} | {l.Count(r => r.Winner == 1)} | {l.Count(r => r.Winner < 0)} | " +
                          $"{l.Count(r => r.Reason == "bandeiras")}/{l.Count(r => r.Reason == "capital")} | {F(Avg(r => r.Time / 60), "0.0")} | " +
                          $"{F(Avg(r => r.Gathered[0].Sum()))}/{F(Avg(r => r.Gathered[1].Sum()))} | {F(Avg(r => r.Spent[0].Sum()))}/{F(Avg(r => r.Spent[1].Sum()))} | " +
                          $"{F(Avg(r => r.Produced[0]), "0.0")}/{F(Avg(r => r.Produced[1]), "0.0")} | {F(Avg(r => r.Lost[0]), "0.0")}/{F(Avg(r => r.Lost[1]), "0.0")} | " +
                          $"{F(Avg(r => r.Alive[0]), "0.0")}/{F(Avg(r => r.Alive[1]), "0.0")} | {F(Avg(r => r.Specials[0]), "0.0")}/{F(Avg(r => r.Specials[1]), "0.0")} | " +
                          $"{F(Avg(r => r.Abil[0]), "0.0")}/{F(Avg(r => r.Abil[1]), "0.0")} | {F(Avg(r => r.Pow[0]), "0.0")}/{F(Avg(r => r.Pow[1]), "0.0")} | " +
                          $"{F(AvgPos(r => r.FirstAttack[0] / 60), "0.0")}/{F(AvgPos(r => r.FirstAttack[1] / 60), "0.0")} | {F(AvgPos(r => r.FirstFlag[0] / 60), "0.0")}/{F(AvgPos(r => r.FirstFlag[1] / 60), "0.0")} |");
        }

        // perfil econômico por facção (coleta média por recurso)
        sb.AppendLine("\n## Economia média por facção (por partida)\n\n| Facção | Comida | Madeira | Minério | Gasto total | Unidades produzidas | Especiais |\n|---|---|---|---|---|---|---|");
        for (int i = 0; i < 3; i++)
        {
            var side = rows.SelectMany(r => new[] { (r, t: 0, f: r.A), (r, t: 1, f: r.B) }).Where(x => x.f == i).ToList();
            sb.AppendLine($"| {Ab[i]} | {F(side.Average(x => x.r.Gathered[x.t][0]))} | {F(side.Average(x => x.r.Gathered[x.t][1]))} | {F(side.Average(x => x.r.Gathered[x.t][2]))} | " +
                          $"{F(side.Average(x => x.r.Spent[x.t].Sum()))} | {F(side.Average(x => x.r.Produced[x.t]), "0.0")} | {F(side.Average(x => x.r.Specials[x.t]), "0.0")} |");
        }
        var dur = rows.Select(r => r.Time / 60).OrderBy(x => x).ToList();
        sb.AppendLine($"\nDuração: média {F(dur.Average(), "0.0")} min, mediana {F(dur[dur.Count / 2], "0.0")} min, mínimo {F(dur.First(), "0.0")}, máximo {F(dur.Last(), "0.0")}.");
        sb.AppendLine($"Vitórias por bandeiras: {rows.Count(r => r.Reason == "bandeiras")}, por capital: {rows.Count(r => r.Reason == "capital")}, por tempo: {draws}.");
        return sb.ToString();
    }
}
