using GuerraDasBandeiras.Gameplay;

namespace GuerraDasBandeiras.Core;

/// <summary>
/// Personagens em pixel art gerados por código (como as bandeiras em FlagArt): cada tipo de unidade
/// ganha roupa, chapéu e arma da sua facção, com contorno escuro e sombreamento simples.
///   Amazonas: cocar de penas de arara, colete de folhas, pintura de urucum, arco, borduna e zarabatana.
///   Minas Gerais: elmo de ferro, túnica branca com o triângulo vermelho, mineiros de capacete com lanterna,
///                 batedor tropeiro de chapéu de feltro, Guardião de armadura com penacho dourado.
///   Rio Grande do Sul: chapéu campeiro com barbicacho, poncho listrado, lenço, bombacha e botas com espora.
/// A cor do time (lenço, faixa ou cinto) separa os dois lados num espelho (mesma facção dos dois lados).
/// O desenho olha para a direita; o renderizador espelha quando a unidade anda para a esquerda.
/// </summary>
public static class CharArt
{
    public const int GW = 20, GH = 28, S = 3;          // grade de "pixels de arte" e ampliação gravada na textura
    /// <summary>Escala de desenho no mundo que deixa o boneco com ~45 px de altura (como os sprites antigos).</summary>
    public const float WorldScale = 0.54f;

    static readonly Dictionary<(UnitType, FactionId, int), Texture2D> _cache = new();

    public static Texture2D Get(UnitType type, FactionId fac, int colorSet)
    {
        var key = (type, fac, colorSet);
        if (_cache.TryGetValue(key, out var t)) return t;
        var c = new Canvas();
        var team = Defs.SetColors[colorSet];
        switch (fac)
        {
            case FactionId.Amazonas: Amazonas(c, type, team); break;
            case FactionId.MinasGerais: Minas(c, type, team); break;
            default: RioGrande(c, type, team); break;
        }
        t = c.ToTexture();
        _cache[key] = t;
        return t;
    }

    // ------------------------------------------------------------------ tela de pixels

    sealed class Canvas
    {
        readonly Color[,] _p = new Color[GW, GH];
        // as coordenadas de desenho usam uma grade 18x26; a margem de 1 pixel guarda o contorno
        public void Px(int x, int y, Color c)
        {
            x += 1; y += 1;
            if (x >= 0 && y >= 0 && x < GW && y < GH) _p[x, y] = c;
        }
        public void Rect(int x, int y, int w, int h, Color c)
        {
            for (int i = 0; i < w; i++) for (int j = 0; j < h; j++) Px(x + i, y + j, c);
        }
        /// <summary>Retângulo com luz vindo de cima-esquerda: borda esquerda clara, direita e base escuras.</summary>
        public void Shaded(int x, int y, int w, int h, Color c)
        {
            Rect(x, y, w, h, c);
            if (w > 2) for (int j = 0; j < h; j++) { Px(x, y + j, Lighten(c, 0.18f)); Px(x + w - 1, y + j, Darken(c, 0.22f)); }
            if (h > 2) for (int i = 1; i < w - 1; i++) Px(x + i, y + h - 1, Darken(c, 0.12f));
        }
        public void VLine(int x, int y0, int y1, Color c) { for (int y = y0; y <= y1; y++) Px(x, y, c); }
        public void HLine(int x0, int x1, int y, Color c) { for (int x = x0; x <= x1; x++) Px(x, y, c); }

        public Texture2D ToTexture()
        {
            // contorno: todo pixel vazio encostado (4 vizinhos) num pixel cheio vira traço escuro
            var outline = new Color(34, 24, 18);
            var o = new bool[GW, GH];
            for (int x = 0; x < GW; x++)
                for (int y = 0; y < GH; y++)
                {
                    if (_p[x, y].A != 0) continue;
                    if ((x > 0 && _p[x - 1, y].A != 0) || (x < GW - 1 && _p[x + 1, y].A != 0) ||
                        (y > 0 && _p[x, y - 1].A != 0) || (y < GH - 1 && _p[x, y + 1].A != 0)) o[x, y] = true;
                }
            var d = new Color[GW * S * GH * S];
            for (int py = 0; py < GH * S; py++)
                for (int px = 0; px < GW * S; px++)
                {
                    int x = px / S, y = py / S;
                    d[py * GW * S + px] = o[x, y] ? outline : _p[x, y];
                }
            var t = new Texture2D(Game1.I.GraphicsDevice, GW * S, GH * S);
            t.SetData(d);
            return t;
        }
    }

    static Color Darken(Color c, float k) => new((int)(c.R * (1 - k)), (int)(c.G * (1 - k)), (int)(c.B * (1 - k)), c.A);
    static Color Lighten(Color c, float k) => new((int)(c.R + (255 - c.R) * k), (int)(c.G + (255 - c.G) * k), (int)(c.B + (255 - c.B) * k), c.A);

    // paleta comum
    static readonly Color Steel = new(176, 184, 196), SteelDark = new(110, 118, 132), SteelLight = new(226, 232, 240);
    static readonly Color Wood = new(132, 88, 50), WoodDark = new(92, 60, 34);
    static readonly Color Gold = new(236, 186, 52), White = new(240, 238, 228), Eye = new(30, 24, 22);
    static readonly Color ArRed = new(214, 44, 40), ArYellow = new(250, 204, 44), ArBlue = new(40, 96, 206);

    // ------------------------------------------------------------------ corpo base

    struct Look
    {
        public Color Skin, Hair, Shirt, Sleeve, Pants, Boots, Belt;
        public bool LongHair, Barefoot, Mustache;
    }

    /// <summary>Corpo de frente-perfil (olhando para a direita). Grade 18x26, pés na linha 25.</summary>
    static void Body(Canvas c, Look k, bool wideLegs = false)
    {
        var skinD = Darken(k.Skin, 0.22f);
        // pernas
        if (wideLegs)
        {
            // bombacha: calça larga com pregas
            c.Shaded(5, 19, 4, 4, k.Pants);
            c.Shaded(9, 19, 4, 4, k.Pants);
            c.Px(7, 20, Darken(k.Pants, 0.25f)); c.Px(11, 20, Darken(k.Pants, 0.25f));
        }
        else
        {
            c.Shaded(6, 19, 3, 4, k.Pants);
            c.Shaded(9, 19, 3, 4, k.Pants);
        }
        // pés / botas (o pé da frente aponta para a direita)
        var boot = k.Barefoot ? skinD : k.Boots;
        c.Rect(6, 23, 3, 3, boot);
        c.Rect(9, 23, 4, 3, boot);
        c.HLine(6, 8, 23, Lighten(boot, 0.15f));
        c.HLine(9, 12, 23, Lighten(boot, 0.15f));
        // tronco
        c.Shaded(5, 12, 8, 7, k.Shirt);
        c.HLine(5, 12, 18, k.Belt);
        // braço de trás e braço da frente
        c.Shaded(4, 12, 1, 6, Darken(k.Sleeve, 0.15f));
        c.Px(4, 18, skinD);
        c.Rect(13, 12, 1, 5, k.Sleeve);
        c.Px(13, 17, k.Skin); c.Px(14, 17, k.Skin);
        // pescoço e cabeça
        c.Rect(8, 11, 2, 1, skinD);
        c.Rect(6, 5, 6, 6, k.Skin);
        c.VLine(6, 5, 10, skinD);
        c.Px(10, 7, Eye);
        c.Px(11, 9, skinD);
        // cabelo
        c.HLine(6, 11, 4, k.Hair);
        c.HLine(6, 9, 5, k.Hair);
        c.VLine(6, 5, 8, k.Hair);
        c.Px(7, 6, k.Hair);
        if (k.LongHair) { c.VLine(5, 5, 13, k.Hair); c.VLine(6, 9, 12, k.Hair); }
        if (k.Mustache) { c.Px(10, 9, k.Hair); c.Px(11, 9, k.Hair); }
    }

    // ------------------------------------------------------------------ armas

    static void Sword(Canvas c)
    {
        c.VLine(14, 6, 15, Steel);
        c.VLine(15, 6, 15, SteelDark);
        c.Px(14, 5, SteelLight);
        c.HLine(12, 16, 16, Gold);
        c.Px(14, 18, WoodDark);
    }

    static void Spear(Canvas c, bool feathers)
    {
        c.VLine(15, 2, 24, Wood);
        c.Px(15, 0, SteelLight); c.Rect(14, 1, 3, 2, Steel); c.Px(16, 2, SteelDark);
        if (feathers) { c.Px(16, 4, ArRed); c.Px(16, 5, ArYellow); c.Px(14, 4, ArBlue); }
    }

    static void Bow(Canvas c)
    {
        // arco curvo à frente do corpo, com a corda
        c.Px(15, 7, Wood); c.Px(16, 8, Wood);
        c.VLine(17, 9, 18, Wood);
        c.Px(16, 19, Wood); c.Px(15, 20, Wood);
        c.VLine(15, 8, 19, new Color(230, 222, 200));
        c.Px(17, 13, WoodDark);
    }

    static void Club(Canvas c)
    {
        // borduna: clava de madeira escura, mais larga na ponta, com penas penduradas
        c.VLine(14, 9, 18, WoodDark);
        c.Rect(13, 5, 3, 4, new Color(110, 66, 36));
        c.Px(13, 5, new Color(140, 90, 52));
        c.Px(16, 8, ArRed); c.Px(16, 9, ArYellow);
    }

    static void Blowgun(Canvas c)
    {
        // zarabatana: tubo comprido na altura da boca
        c.HLine(11, 17, 9, new Color(150, 120, 70));
        c.Px(17, 9, WoodDark);
        c.Px(14, 10, new Color(150, 120, 70));
    }

    static void Crossbow(Canvas c)
    {
        c.HLine(11, 17, 15, Wood);
        c.VLine(16, 12, 18, SteelDark);
        c.Px(17, 15, SteelLight);
    }

    static void Bolas(Canvas c)
    {
        // boleadeiras: tiras de couro com três pedras
        var rope = new Color(160, 120, 80);
        c.Px(14, 16, rope); c.Px(15, 14, rope); c.Px(15, 15, rope); c.Px(16, 12, rope);
        c.Px(14, 11, rope);
        var stone = new Color(120, 112, 104);
        c.Rect(16, 10, 2, 2, stone); c.Rect(13, 9, 2, 2, stone); c.Rect(16, 14, 2, 2, stone);
    }

    static void Pickaxe(Canvas c)
    {
        c.Px(14, 17, Wood); c.Px(14, 16, Wood); c.Px(15, 15, Wood); c.Px(15, 14, Wood); c.Px(15, 13, Wood); c.Px(16, 12, Wood);
        c.HLine(14, 17, 11, Steel); c.Px(13, 12, SteelDark); c.Px(17, 12, SteelDark);
    }

    static void Axe(Canvas c)
    {
        c.VLine(15, 10, 19, Wood);
        c.Rect(16, 10, 2, 3, Steel); c.Px(17, 13, SteelDark); c.Px(16, 10, SteelLight);
    }

    // ------------------------------------------------------------------ Amazonas

    static void Amazonas(Canvas c, UnitType t, Color team)
    {
        var skin = new Color(168, 106, 66);
        var leaf = new Color(54, 136, 66);
        var leafD = new Color(32, 96, 50);
        var fiber = new Color(198, 170, 112);
        bool worker = t == UnitType.Worker;
        var k = new Look
        {
            Skin = skin, Hair = new Color(26, 22, 22), LongHair = true, Barefoot = true,
            Shirt = worker ? fiber : leaf, Sleeve = skin, Pants = worker ? fiber : new Color(176, 146, 92), Belt = team,
        };
        if (worker)
        {
            // cesto (paneiro) trançado nas costas
            for (int x = 1; x <= 4; x++) for (int y = 11; y <= 17; y++)
                c.Px(x, y, ((x + y) & 1) == 0 ? new Color(170, 120, 60) : new Color(130, 86, 40));
            c.HLine(1, 4, 11, new Color(200, 150, 80));
        }
        if (t == UnitType.VigiaDaMata)
        {
            // manto de folhas comprido que cobre as costas (a camuflagem da mata)
            for (int y = 6; y <= 21; y++) c.HLine(3, 5, y, ((y & 1) == 0) ? leaf : leafD);
        }
        Body(c, k);
        // pernas e braços à mostra; tanga de fibra
        c.Shaded(6, 21, 3, 2, Darken(skin, 0.08f));
        c.Shaded(9, 21, 3, 2, Darken(skin, 0.08f));
        if (!worker)
        {
            // colete de folhas: recortes em zigue-zague
            for (int x = 5; x <= 12; x++) c.Px(x, ((x & 1) == 0) ? 17 : 16, leafD);
            c.Px(7, 13, leafD); c.Px(10, 14, leafD); c.Px(8, 15, leafD);
            // braçadeira de urucum
            c.Px(13, 13, ArRed);
        }
        // pintura de urucum no rosto
        c.Px(9, 8, ArRed); c.Px(11, 8, ArRed);
        // faixa da cabeça na cor do time
        c.HLine(6, 11, 5, team);
        // cocar: penas de arara em leque (maior no Vigia da Mata), uma pena só para o trabalhador
        if (worker) { c.VLine(7, 1, 4, ArRed); c.Px(7, 0, ArYellow); }
        else
        {
            int tall = t == UnitType.VigiaDaMata ? 5 : 3;
            Color[] feathers = { ArRed, ArYellow, ArBlue, ArRed, ArYellow, ArBlue };
            for (int i = 0; i < 6; i++)
            {
                int h = tall - Math.Abs(i - 2) / 2;
                c.VLine(6 + i, 4 - h, 3, feathers[i]);
                c.Px(6 + i, 4 - h, Lighten(feathers[i], 0.4f));
            }
        }
        if (t == UnitType.VigiaDaMata)
        {
            // capuz de folhas por cima do cocar baixo
            c.HLine(5, 10, 4, leaf); c.Px(5, 5, leaf); c.Px(4, 6, leafD);
        }
        switch (t)
        {
            case UnitType.Swordsman: Club(c); break;
            case UnitType.Spearman: Spear(c, true); break;
            case UnitType.Archer: case UnitType.VigiaDaMata: Bow(c); break;
            case UnitType.Scout: Blowgun(c); break;
        }
    }

    // ------------------------------------------------------------------ Minas Gerais

    static void Minas(Canvas c, UnitType t, Color team)
    {
        var skin = new Color(222, 168, 122);
        var hair = new Color(84, 54, 34);
        var tunic = White;
        var red = new Color(200, 36, 40);
        var mail = new Color(150, 156, 166);
        switch (t)
        {
            case UnitType.Worker:
            {
                // mineiro: macacão, camisa azulada, capacete amarelo com lanterna e picareta
                var k = new Look { Skin = skin, Hair = hair, Shirt = new Color(96, 120, 150), Sleeve = new Color(96, 120, 150), Pants = new Color(120, 86, 52), Boots = new Color(60, 44, 32), Belt = team };
                Body(c, k);
                c.Rect(6, 15, 6, 3, new Color(120, 86, 52));     // peito do macacão
                c.VLine(6, 12, 14, new Color(120, 86, 52)); c.VLine(11, 12, 14, new Color(120, 86, 52));
                var hat = new Color(232, 186, 40);
                c.Rect(6, 2, 6, 3, hat); c.HLine(5, 13, 4, Darken(hat, 0.15f)); c.Px(6, 2, Lighten(hat, 0.3f));
                c.Rect(11, 2, 2, 2, new Color(255, 248, 196));  // lanterna
                Pickaxe(c);
                break;
            }
            case UnitType.Scout:
            {
                // tropeiro: chapéu de feltro de aba larga, colete de couro, capanga a tiracolo
                var k = new Look { Skin = skin, Hair = hair, Shirt = White, Sleeve = White, Pants = new Color(110, 90, 70), Boots = new Color(70, 48, 30), Belt = team, Mustache = true };
                Body(c, k);
                var leather = new Color(140, 92, 52);
                c.Rect(5, 12, 3, 6, leather); c.Rect(10, 12, 3, 6, leather);
                c.Px(8, 13, team); c.Px(9, 14, team); c.Px(10, 15, team);    // alça da capanga
                c.Rect(3, 15, 2, 3, leather);
                var felt = new Color(110, 78, 48);
                c.Rect(6, 1, 6, 3, felt); c.HLine(6, 11, 3, Darken(felt, 0.3f)); c.HLine(3, 14, 4, felt); c.HLine(4, 13, 4, Darken(felt, 0.1f));
                Sword(c);
                break;
            }
            case UnitType.GuardiaoDasGerais:
            {
                // Guardião das Gerais: armadura completa, elmo fechado com penacho dourado
                var k = new Look { Skin = Steel, Hair = SteelDark, Shirt = Steel, Sleeve = SteelDark, Pants = SteelDark, Boots = new Color(70, 74, 84), Belt = Gold };
                Body(c, k);
                c.Rect(6, 3, 6, 8, Steel);
                c.VLine(6, 3, 10, SteelDark); c.Px(7, 3, SteelLight);
                c.HLine(8, 11, 7, Eye);                     // viseira
                c.HLine(7, 10, 2, Gold); c.Px(8, 1, Gold); c.Px(9, 0, ArYellow); c.Px(7, 1, ArYellow);
                // peitoral com o triângulo vermelho e bordas douradas
                c.HLine(5, 12, 12, Gold);
                c.Px(9, 13, red); c.HLine(8, 10, 14, red); c.HLine(7, 11, 15, red);
                c.Px(5, 13, SteelLight); c.Px(5, 14, SteelLight);
                Sword(c);
                break;
            }
            default:
            {
                // soldados: elmo de ferro (morrião), cota de malha e túnica branca com o triângulo vermelho
                var k = new Look { Skin = skin, Hair = hair, Shirt = tunic, Sleeve = mail, Pants = new Color(90, 70, 56), Boots = new Color(50, 38, 30), Belt = team };
                Body(c, k);
                c.Px(9, 13, red); c.HLine(8, 10, 14, red); c.HLine(7, 11, 15, red);
                c.Px(4, 13, Darken(mail, 0.3f)); c.Px(4, 15, Darken(mail, 0.3f)); c.Px(13, 14, Darken(mail, 0.3f));
                c.Px(9, 18, Gold);                          // fivela dourada
                bool leatherCap = t == UnitType.Archer;
                var helm = leatherCap ? new Color(130, 90, 56) : Steel;
                c.Rect(6, 2, 6, 3, helm);
                c.Px(6, 2, Lighten(helm, 0.3f));
                c.HLine(4, 13, 4, Darken(helm, 0.25f));
                if (!leatherCap) { c.HLine(8, 9, 1, SteelDark); c.Px(9, 0, red); }
                if (t == UnitType.Swordsman)
                {
                    Sword(c);
                    // broquel redondo no braço de trás
                    c.Rect(2, 13, 3, 4, Steel); c.Px(2, 13, SteelLight); c.Px(3, 14, Gold); c.VLine(4, 13, 16, SteelDark);
                }
                else if (t == UnitType.Spearman) Spear(c, false);
                else if (t == UnitType.Archer) Crossbow(c);
                break;
            }
        }
    }

    // ------------------------------------------------------------------ Rio Grande do Sul

    static void RioGrande(Canvas c, UnitType t, Color team)
    {
        var skin = new Color(232, 186, 142);
        var hair = new Color(48, 34, 26);
        var bombacha = new Color(206, 194, 168);
        var boots = new Color(36, 28, 24);
        bool worker = t == UnitType.Worker;
        var k = new Look
        {
            Skin = skin, Hair = hair, Mustache = true, Shirt = White, Sleeve = White,
            Pants = bombacha, Boots = boots, Belt = new Color(110, 70, 40),
        };
        Body(c, k, wideLegs: true);
        // espora de prata no calcanhar
        c.Px(5, 24, SteelLight);
        if (worker)
        {
            // camisa branca, guaiaca (cinto largo) e boina; machado na mão
            c.HLine(5, 12, 17, new Color(110, 70, 40)); c.Px(8, 17, SteelLight);
            var boina = new Color(40, 48, 80);
            c.Rect(5, 2, 8, 3, boina); c.Px(12, 1, boina); c.Px(5, 2, Lighten(boina, 0.2f));
            Axe(c);
        }
        else
        {
            // poncho listrado por cima do tronco e dos braços
            var poncho = new Color(150, 46, 40);
            var stripe = new Color(214, 168, 70);
            c.Rect(4, 12, 10, 6, poncho);
            c.HLine(4, 13, 14, stripe); c.HLine(4, 13, 16, Darken(poncho, 0.3f));
            c.Px(4, 12, Lighten(poncho, 0.2f)); c.VLine(13, 12, 17, Darken(poncho, 0.25f));
            c.HLine(4, 13, 17, Darken(poncho, 0.15f));
            c.Px(5, 18, poncho); c.Px(12, 18, poncho);       // franjas
            c.Px(13, 17, skin); c.Px(14, 17, skin);
            // chapéu campeiro de aba larga com barbicacho
            var hat = new Color(46, 34, 28);
            c.Rect(6, 1, 6, 3, hat); c.Px(6, 1, Lighten(hat, 0.25f));
            c.HLine(6, 11, 3, new Color(120, 90, 60));       // fita do chapéu
            c.HLine(3, 14, 4, hat);
            c.Px(7, 9, hat); c.Px(7, 10, hat);                // barbicacho
        }
        // lenço no pescoço, na cor do time (vermelho para os gaúchos)
        c.HLine(7, 10, 11, team); c.Px(8, 12, team); c.Px(9, 12, Darken(team, 0.2f)); c.Px(8, 13, team);
        switch (t)
        {
            case UnitType.Swordsman: Sword(c); break;
            case UnitType.Spearman: Spear(c, false); c.Px(16, 3, team); c.Px(17, 3, team); c.Px(16, 4, team); break;
            case UnitType.Archer: Bolas(c); break;
            case UnitType.Scout: c.HLine(13, 16, 16, SteelLight); c.Px(13, 16, WoodDark); break; // facão
        }
    }
}
