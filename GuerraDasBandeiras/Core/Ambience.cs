using GuerraDasBandeiras.Gameplay;
using Microsoft.Xna.Framework.Audio;

namespace GuerraDasBandeiras.Core;

/// <summary>
/// Ambiência gerada por código (sem arquivos externos, portanto sem questão de licença):
/// vento (ruído marrom em loop com respiração lenta) e pios de pássaro sintetizados.
/// Toca no canal de música/ambiente. Cenas: mata (vento leve + pássaros), serra (vento grave + pedrinhas),
/// pampa (vento mais forte), partida (vento bem baixo).
/// </summary>
public static class Ambience
{
    const int Rate = 22050;
    static SoundEffect _wind, _chirp;
    static SoundEffectInstance _loop;
    static string _scene = "";
    static float _baseVol;
    static int _nextEvent;
    static readonly Random _r = new(1234);

    static SoundEffect Build(float[] samples)
    {
        var pcm = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            short v = (short)(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            pcm[i * 2] = (byte)v; pcm[i * 2 + 1] = (byte)(v >> 8);
        }
        return new SoundEffect(pcm, Rate, AudioChannels.Mono);
    }

    static SoundEffect Wind
    {
        get
        {
            if (_wind != null) return _wind;
            int n = Rate * 6;
            var s = new float[n];
            var rnd = new Random(7);
            float brown = 0;
            for (int i = 0; i < n; i++)
            {
                brown = brown * 0.995f + (float)(rnd.NextDouble() * 2 - 1) * 0.04f;
                float swell = 0.55f + 0.45f * MathF.Sin(i / (float)n * MathF.Tau); // período = tamanho do loop
                s[i] = brown * swell * 2.2f;
            }
            int fade = Rate / 3; // costura o fim no começo para o loop não estalar
            for (int i = 0; i < fade; i++) { float k = i / (float)fade; s[i] = s[i] * k + s[n - fade + i] * (1 - k); }
            Array.Resize(ref s, n - fade);
            return _wind = Build(s);
        }
    }

    static SoundEffect Chirp
    {
        get
        {
            if (_chirp != null) return _chirp;
            int n = (int)(Rate * 0.42f);
            var s = new float[n];
            double ph = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                int note = t < 0.16f ? 0 : t < 0.22f ? -1 : 1;
                if (note < 0) { s[i] = 0; continue; }
                float local = note == 0 ? t / 0.16f : (t - 0.22f) / 0.2f;
                double f = note == 0 ? 2600 + 1100 * local : 3100 + 700 * MathF.Sin(local * MathF.PI);
                ph += 2 * Math.PI * f / Rate;
                float env = MathF.Sin(MathF.Min(1, local) * MathF.PI);
                s[i] = (float)Math.Sin(ph) * env * 0.35f;
            }
            return _chirp = Build(s);
        }
    }

    public static string Current => _scene;

    static void StartLoop(float vol, float pitch)
    {
        DisposeLoop();
        try
        {
            _loop = Wind.CreateInstance();
            _loop.IsLooped = true;
            _loop.Pitch = pitch;
            _baseVol = vol;
            ApplyVolume();
            _loop.Play();
        }
        catch (Exception) { DisposeLoop(); } // sem voz disponível: segue sem ambiência
    }

    static void DisposeLoop()
    {
        if (_loop == null) return;
        try { _loop.Stop(); _loop.Dispose(); } catch (Exception) { }
        _loop = null;
    }

    public static void ApplyVolume()
    {
        if (_loop != null) _loop.Volume = Math.Clamp(_baseVol * Assets.MusicVolume * Assets.MasterVolume, 0, 1);
    }

    /// <summary>Ambiência da cena de seleção. Chamado a cada quadro; só troca quando a facção muda.</summary>
    public static void SetScene(FactionId f)
    {
        string key = "cena-" + f;
        if (_scene != key)
        {
            _scene = key;
            switch (f)
            {
                case FactionId.Amazonas: StartLoop(0.35f, 0.2f); break;
                case FactionId.MinasGerais: StartLoop(0.4f, -0.35f); break;
                default: StartLoop(0.55f, 0.05f); break;
            }
            _nextEvent = Environment.TickCount + 1200;
        }
        ApplyVolume();
        if (Environment.TickCount < _nextEvent) return;
        _nextEvent = Environment.TickCount + 2500 + _r.Next(3500);
        float mv = Assets.MusicVolume * Assets.MasterVolume;
        switch (f)
        {
            case FactionId.Amazonas:
                try { Chirp.Play(0.25f * mv, (float)(_r.NextDouble() * 0.4 - 0.2), (float)(_r.NextDouble() * 1.2 - 0.6)); } catch (Exception) { }
                break;
            case FactionId.MinasGerais: Assets.Play("impactMining_001", 0.12f, -0.4f, music: true); break;
        }
    }

    /// <summary>Durante a partida: só um vento bem baixo de fundo.</summary>
    public static void SetGame()
    {
        if (_scene == "jogo") { ApplyVolume(); return; }
        _scene = "jogo";
        StartLoop(0.18f, 0f);
    }

    public static void Stop()
    {
        DisposeLoop();
        _scene = "";
    }
}
