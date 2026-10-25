using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Swittcs;

/// <summary>
/// Always-running WASAPI output fed by one mixer. Each key event adds a short
/// cached-sample → pan → volume chain to the mixer; finished chains drop out on their own.
/// </summary>
public sealed class AudioEngine : IDisposable, IMMNotificationClient
{
    const int SampleRate = 44100;
    // WASAPI shared-mode buffer. Windows' audio engine runs on a 10 ms period, so 20 ms leaves one period of headroom.
    // ponytail: raise if slow PCs report crackling.
    const int OutputLatencyMs = 20;

    readonly MixingSampleProvider _mixer = new(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2)) { ReadFully = true };
    readonly MMDeviceEnumerator _devices = new();
    readonly object _outputLock = new();
    WasapiOut? _output;

    // Swapped atomically on profile change; never mutated after publication.
    Dictionary<(KeyGroup, bool), float[][]> _samples = [];
    readonly Dictionary<(KeyGroup, bool), int> _roundRobin = [];

    public float Volume { get; set; } = 1f;
    public bool Enabled { get; set; } = true;
    /// <summary>Set by the license gate; overrides <see cref="Enabled"/>.</summary>
    public bool Muted { get; set; }
    public string? Profile { get; private set; }
    /// <summary>Friendly name of the bound output; null when none. Updated on a pool thread.</summary>
    public string? DeviceName { get; private set; }

    public AudioEngine()
    {
        _devices.RegisterEndpointNotificationCallback(this);
        BindDefaultDevice();
    }

    /// <summary>
    /// Loads every <c>{group}_{down|up}_NN.wav</c> of a <see cref="SoundBank"/> profile (group may contain underscores,
    /// e.g. <c>caps_lock</c>). Several files per slot = round-robin variations. Missing groups borrow their
    /// <see cref="Keymap.Parent"/>'s sounds.
    /// </summary>
    public void LoadProfile(string profile)
    {
        var map = new Dictionary<(KeyGroup, bool), List<float[]>>();
        foreach (string file in SoundBank.Files(profile))
        {
            string[] parts = Path.GetFileNameWithoutExtension(file).Split('_');
            if (!file.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || parts.Length < 3 || parts[^2] is not ("down" or "up")
                || !Enum.TryParse(string.Concat(parts[..^2]), ignoreCase: true, out KeyGroup group))
                continue;
            var key = (group, parts[^2] == "down");
            if (!map.TryGetValue(key, out var list)) map[key] = list = [];
            list.Add(TrimLeadingSilence(Decode($"{profile}/{file}")!));
        }
        var samples = map.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
        foreach (KeyGroup g in Enum.GetValues<KeyGroup>())
            foreach (bool down in (ReadOnlySpan<bool>)[true, false])
                for (KeyGroup? p = Keymap.Parent(g); p != null && !samples.ContainsKey((g, down)); p = Keymap.Parent(p.Value))
                    if (map.TryGetValue((p.Value, down), out var src)) samples[(g, down)] = [.. src];
        _samples = samples;
        lock (_roundRobin) _roundRobin.Clear();
        Profile = profile;
    }

    /// <summary>One-shot UI sound (toggle cues). Plays even while disabled or muted.</summary>
    public void PlayCue(float[] samples) =>
        _mixer.AddMixerInput(new VolumeSampleProvider(new PanningSampleProvider(new SampleSource(samples))) { Volume = Volume });

    public void Play(int vk, bool isDown)
    {
        if (!Enabled || Muted) return;
        var pos = Keymap.Lookup(vk);
        var key = (pos.Group, isDown);
        if (!_samples.TryGetValue(key, out var variants)) return;
        int i;
        lock (_roundRobin)
        {
            _roundRobin.TryGetValue(key, out i);
            _roundRobin[key] = (i + 1) % variants.Length;
        }
        var voice = new PanningSampleProvider(new SampleSource(variants[i])) { Pan = pos.Pan };
        _mixer.AddMixerInput(new VolumeSampleProvider(voice) { Volume = Volume });
    }

    /// <summary>Decodes a <see cref="SoundBank"/> wav or mp3 to 44.1 kHz mono float (pan needs mono input). Null if absent.</summary>
    public static float[]? Decode(string path)
    {
        using var stream = SoundBank.Open(path);
        if (stream is null) return null;
        using WaveStream reader = path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ? new Mp3FileReader(stream) : new WaveFileReader(stream);
        ISampleProvider src = reader.ToSampleProvider();
        if (src.WaveFormat.Channels == 2) src = new StereoToMonoSampleProvider(src) { LeftVolume = 0.5f, RightVolume = 0.5f };
        if (src.WaveFormat.SampleRate != SampleRate) src = new WdlResamplingSampleProvider(src, SampleRate);
        var all = new List<float>();
        var buf = new float[SampleRate];
        int n;
        while ((n = src.Read(buf, 0, buf.Length)) > 0) all.AddRange(buf.AsSpan(0, n));
        return [.. all];
    }

    /// <summary>
    /// Recordings start 5–40 ms before the click. Cut that so the transient plays the moment the key goes down,
    /// keeping 1 ms of lead-in so the cut itself doesn't click.
    /// </summary>
    static float[] TrimLeadingSilence(float[] s)
    {
        float peak = 0;
        foreach (float v in s) peak = Math.Max(peak, Math.Abs(v));
        int onset = Array.FindIndex(s, v => Math.Abs(v) >= peak * 0.02f); // −34 dB below the peak
        int start = Math.Max(0, onset - SampleRate / 1000);
        return start == 0 ? s : s[start..];
    }

    void BindDefaultDevice()
    {
        lock (_outputLock)
        {
            _output?.Dispose();
            _output = null;
            DeviceName = null;
            try
            {
                var device = _devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _output = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: true, latency: OutputLatencyMs);
                _output.Init(new SoftClip(_mixer));
                _output.Play();
                DeviceName = device.FriendlyName;
            }
            catch (Exception) { _output = null; } // no output device; rebinds when one appears
        }
    }

    // Follow the user's default output (headphones plugged in, etc.). COM callback thread: hop off it.
    void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Render && role == Role.Multimedia) Task.Run(BindDefaultDevice);
    }
    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState) { }
    void IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId) { }
    void IMMNotificationClient.OnDeviceRemoved(string deviceId) { }
    void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    public void Dispose()
    {
        _devices.UnregisterEndpointNotificationCallback(this);
        lock (_outputLock) _output?.Dispose();
        _devices.Dispose();
    }

    /// <summary>
    /// Volume goes up to 300%, which pushes peaks past full scale. Below 0.8 the signal passes untouched;
    /// above it, tanh bends peaks smoothly toward 1.0 instead of the device hard-clipping them into crackle.
    /// </summary>
    sealed class SoftClip(ISampleProvider source) : ISampleProvider
    {
        const float Knee = 0.8f, Room = 1f - Knee;
        public WaveFormat WaveFormat => source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int n = source.Read(buffer, offset, count);
            for (int i = offset; i < offset + n; i++)
            {
                float x = buffer[i], a = MathF.Abs(x);
                if (a > Knee) buffer[i] = MathF.CopySign(Knee + Room * MathF.Tanh((a - Knee) / Room), x);
            }
            return n;
        }
    }

    /// <summary>Plays one cached buffer once.</summary>
    sealed class SampleSource(float[] data) : ISampleProvider
    {
        static readonly WaveFormat Mono = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1);
        int _pos;
        public WaveFormat WaveFormat => Mono;

        public int Read(float[] buffer, int offset, int count)
        {
            int n = Math.Min(data.Length - _pos, count);
            data.AsSpan(_pos, n).CopyTo(buffer.AsSpan(offset));
            _pos += n;
            return n;
        }
    }
}

/// <summary>
/// Audio compiled into the assembly as resources ("Sounds/&lt;profile&gt;/&lt;file&gt;.wav", "Sounds/&lt;cue&gt;.mp3"),
/// so installs don't carry a folder of loose, copyable sound files.
/// </summary>
public static class SoundBank
{
    const string Prefix = "Sounds/";
    static readonly System.Reflection.Assembly Asm = typeof(SoundBank).Assembly;

    // MSBuild's RecursiveDir puts '\' into the logical names; index them by '/' paths.
    static readonly Dictionary<string, string> Names = Asm.GetManifestResourceNames()
        .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal))
        .ToDictionary(n => n[Prefix.Length..].Replace('\\', '/'), StringComparer.OrdinalIgnoreCase);

    /// <summary>Profiles with at least one alpha key-down sample, sorted.</summary>
    public static readonly string[] Profiles = Names.Keys
        .Select(p => p.Split('/'))
        .Where(p => p.Length == 2 && p[1].StartsWith("alpha_down_", StringComparison.OrdinalIgnoreCase))
        .Select(p => p[0]).Distinct().Order().ToArray();

    /// <summary>File names (without folder) in one profile.</summary>
    public static IEnumerable<string> Files(string profile) =>
        Names.Keys.Where(p => p.StartsWith(profile + "/", StringComparison.OrdinalIgnoreCase)).Select(p => p[(profile.Length + 1)..]);

    public static Stream? Open(string path) => Names.TryGetValue(path, out var name) ? Asm.GetManifestResourceStream(name) : null;
}
