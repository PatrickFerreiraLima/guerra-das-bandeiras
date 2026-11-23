using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using NVorbis;

namespace GuerraDasBandeiras.Core;

/// <summary>
/// Carrega texturas, fontes e sons direto dos arquivos (sem content pipeline),
/// o que facilita trocar/adicionar assets: basta jogar o arquivo na pasta Assets.
/// </summary>
public static class Assets
{
    public static string Root => Path.Combine(AppContext.BaseDirectory, "Assets");

    static GraphicsDevice _gd;
    static readonly Dictionary<string, Texture2D> _tex = new();
    static readonly Dictionary<string, SoundEffect> _sfx = new();
    static FontSystem _fonts, _fontsNarrow, _fontsBold;

    public static Texture2D Pixel { get; private set; }
    public static Texture2D Circle { get; private set; }
    public static Texture2D Ring { get; private set; }

    // três canais de volume (ajustáveis na pausa): geral, efeitos, música/ambiente
    public static float MasterVolume = 0.9f;
    public static float SfxVolume = 0.7f;
    public static float MusicVolume = 0.6f;
    static readonly Dictionary<string, int> _lastPlay = new();
    static readonly Queue<int> _recent = new();
    public static int Skipped;   // quantos sons o limitador cortou (diagnóstico)

    public static void Init(GraphicsDevice gd)
    {
        _gd = gd;
        Pixel = new Texture2D(gd, 1, 1);
        Pixel.SetData(new[] { Color.White });
        Circle = MakeCircle(64, false);
        Ring = MakeCircle(64, true);

        _fonts = new FontSystem();
        _fonts.AddFont(File.ReadAllBytes(Path.Combine(Root, "Fonts", "KenneyFuture.ttf")));
        _fontsNarrow = new FontSystem();
        // texto corrido: Nunito (OFL), mais legível que a fonte de títulos; títulos seguem na Kenney Future
        _fontsNarrow.AddFont(File.ReadAllBytes(Path.Combine(Root, "Fonts", "Nunito-SemiBold.ttf")));
        _fontsBold = new FontSystem();
        _fontsBold.AddFont(File.ReadAllBytes(Path.Combine(Root, "Fonts", "Nunito-ExtraBold.ttf")));
    }

    public static SpriteFontBase Font(int size) => _fonts.GetFont(size);
    public static SpriteFontBase FontNarrow(int size) => _fontsNarrow.GetFont(size);
    public static SpriteFontBase FontBold(int size) => _fontsBold.GetFont(size);

    /// <summary>Ex.: Tex("Unit/medievalUnit_01")</summary>
    public static Texture2D Tex(string name)
    {
        if (_tex.TryGetValue(name, out var t)) return t;
        var path = Path.Combine(Root, "Sprites", name + ".png");
        t = File.Exists(path) ? Texture2D.FromFile(_gd, path) : Pixel;
        if (t != Pixel) t = TrimAndPremultiply(t);
        _tex[name] = t;
        return t;
    }

    /// <summary>Imagens de tela (logo etc.) em Assets/Images, sem recorte de margem.</summary>
    public static Texture2D Image(string name)
    {
        var key = "img:" + name;
        if (_tex.TryGetValue(key, out var t)) return t;
        var path = Path.Combine(Root, "Images", name + ".png");
        t = Pixel;
        if (File.Exists(path))
        {
            t = Texture2D.FromFile(_gd, path);
            var d = new Color[t.Width * t.Height];
            t.GetData(d);
            for (int i = 0; i < d.Length; i++) d[i] = Color.FromNonPremultiplied(d[i].R, d[i].G, d[i].B, d[i].A);
            t.SetData(d);
        }
        _tex[key] = t;
        return t;
    }

    public static Texture2D Unit(int n) => Tex($"Unit/medievalUnit_{n:00}");
    public static Texture2D Structure(int n) => Tex($"Structure/medievalStructure_{n:00}");
    public static Texture2D Env(int n) => Tex($"Environment/medievalEnvironment_{n:00}");
    public static Texture2D Tile(int n) => Tex($"Tile/medievalTile_{n:00}");

    /// <summary>Usado pelos testes: o arquivo existe e decodifica?</summary>
    public static bool CanLoad(string name) => LoadOgg(Path.Combine(Root, "Audio", name + ".ogg")) != null;

    /// <summary>
    /// Toca um som no canal de efeitos (ou de música). Limitador: o mesmo som não repete em menos de 70 ms
    /// e no máximo 10 efeitos começam a cada 250 ms — evita a "metralhadora" de sons em batalhas grandes.
    /// </summary>
    public static void Play(string name, float vol = 1f, float pitch = 0f, bool music = false)
    {
        int now = Environment.TickCount;
        if (!music)
        {
            if (_lastPlay.TryGetValue(name, out int last) && now - last < 70) { Skipped++; return; }
            while (_recent.Count > 0 && now - _recent.Peek() > 250) _recent.Dequeue();
            if (_recent.Count >= 10) { Skipped++; return; }
            _recent.Enqueue(now);
            _lastPlay[name] = now;
        }
        if (!_sfx.TryGetValue(name, out var s))
        {
            s = LoadOgg(Path.Combine(Root, "Audio", name + ".ogg"));
            _sfx[name] = s;
        }
        float ch = music ? MusicVolume : SfxVolume;
        // áudio nunca pode derrubar o jogo: se o limite de vozes do sistema estourar, o som é pulado
        try { s?.Play(Math.Clamp(vol * ch * MasterVolume, 0, 1), Math.Clamp(pitch, -1, 1), 0f); }
        catch (Exception) { Skipped++; }
    }

    static SoundEffect LoadOgg(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var vr = new VorbisReader(path);
            var samples = new List<float>();
            var buf = new float[4096];
            int read;
            while ((read = vr.ReadSamples(buf, 0, buf.Length)) > 0)
                for (int i = 0; i < read; i++) samples.Add(buf[i]);
            var pcm = new byte[samples.Count * 2];
            for (int i = 0; i < samples.Count; i++)
            {
                short v = (short)(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
                pcm[i * 2] = (byte)v;
                pcm[i * 2 + 1] = (byte)(v >> 8);
            }
            return new SoundEffect(pcm, vr.SampleRate, vr.Channels == 1 ? AudioChannels.Mono : AudioChannels.Stereo);
        }
        catch { return null; }
    }

    /// <summary>
    /// Os PNGs do pacote vêm centralizados numa tela 128x128 com margem transparente.
    /// Recortamos a área visível para que "pés", sombras e cliques fiquem alinhados.
    /// </summary>
    static Texture2D TrimAndPremultiply(Texture2D t)
    {
        int w = t.Width, h = t.Height;
        var d = new Color[w * h];
        t.GetData(d);
        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (d[y * w + x].A > 8)
                {
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                }
        if (maxX < 0) { minX = 0; minY = 0; maxX = w - 1; maxY = h - 1; }
        int nw = maxX - minX + 1, nh = maxY - minY + 1;
        var nd = new Color[nw * nh];
        for (int y = 0; y < nh; y++)
            for (int x = 0; x < nw; x++)
            {
                var c = d[(y + minY) * w + x + minX];
                nd[y * nw + x] = Color.FromNonPremultiplied(c.R, c.G, c.B, c.A);
            }
        if (nw == w && nh == h) { t.SetData(nd); return t; }
        var nt = new Texture2D(_gd, nw, nh);
        nt.SetData(nd);
        t.Dispose();
        return nt;
    }

    static Texture2D MakeCircle(int r, bool ring)
    {
        int s = r * 2;
        var t = new Texture2D(_gd, s, s);
        var d = new Color[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = x - r + 0.5f, dy = y - r + 0.5f;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                float a = Math.Clamp(r - dist, 0, 1);
                if (ring) a *= Math.Clamp(dist - (r - 10), 0, 1);
                d[y * s + x] = Color.White * a;
            }
        t.SetData(d);
        return t;
    }
}
