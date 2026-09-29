using Godot;

namespace PalaceRoomViewer;

public enum RehearsalSound { Reveal, Knew, Missed, Clear, Record }

// Short chimes synthesized at startup, so the viewer ships no audio files.
public partial class RehearsalSounds : Node
{
    private const int MixRate = 22050;
    // Survives the scene reload that loads another Room.
    public static bool Enabled { get; set; } = true;
    private readonly Dictionary<RehearsalSound, AudioStreamWav> _streams = [];
    private readonly List<AudioStreamPlayer> _players = [];

    public override void _Ready()
    {
        _streams[RehearsalSound.Reveal] = Tones((1320, .05f));
        _streams[RehearsalSound.Knew] = Tones((784, .07f), (1175, .12f));
        _streams[RehearsalSound.Missed] = Tones((220, .09f), (185, .16f));
        _streams[RehearsalSound.Clear] = Tones((523, .09f), (659, .09f), (784, .09f), (1047, .25f));
        _streams[RehearsalSound.Record] = Tones((523, .08f), (659, .08f), (784, .08f), (1047, .08f), (1319, .08f), (1568, .35f));
        // A few voices so quick answers do not cut each other off.
        for (var i = 0; i < 4; i++)
        {
            var player = new AudioStreamPlayer { VolumeDb = -12 };
            AddChild(player);
            _players.Add(player);
        }
    }

    // Combo steps raise the pitch a semitone at a time, up to an octave.
    public void Play(RehearsalSound sound, int combo = 0)
    {
        if (!Enabled) return;
        var player = _players.FirstOrDefault(p => !p.Playing) ?? _players[0];
        player.Stream = _streams[sound];
        player.PitchScale = sound == RehearsalSound.Knew ? Mathf.Pow(2, Mathf.Min(Math.Max(combo - 1, 0), 12) / 12f) : 1;
        player.Play();
    }

    private static AudioStreamWav Tones(params (float Hz, float Seconds)[] notes)
    {
        var samples = new List<short>();
        foreach (var (hz, seconds) in notes)
        {
            var count = (int)(seconds * MixRate);
            for (var i = 0; i < count; i++)
            {
                var t = i / (float)MixRate;
                // Fast attack, exponential decay, and a soft second harmonic for a bell-like tone.
                var envelope = Mathf.Min(1, i / 60f) * Mathf.Exp(-4f * t / seconds);
                var wave = Mathf.Sin(Mathf.Tau * hz * t) + .3f * Mathf.Sin(Mathf.Tau * hz * 2 * t);
                samples.Add((short)(wave / 1.3f * envelope * 0.8f * short.MaxValue));
            }
        }
        var data = new byte[samples.Count * 2];
        Buffer.BlockCopy(samples.ToArray(), 0, data, 0, data.Length);
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = MixRate, Stereo = false, Data = data };
    }
}
