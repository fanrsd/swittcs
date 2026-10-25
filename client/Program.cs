using System.Text.Json;

namespace Swittcs;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var single = new Mutex(true, "Swittcs.SingleInstance", out bool first);
        if (!first) return;
        ApplicationConfiguration.Initialize();

        Application.Run(new TrayApp());
    }

    /// <summary>Embedded multi-size .ico (app.ico / app-off.ico), picking the frame nearest <paramref name="size"/>.</summary>
    public static Icon LoadIcon(string name, Size size)
    {
        using var s = typeof(Program).Assembly.GetManifestResourceStream(name)!;
        return new Icon(s, size);
    }
}

sealed record AppSettings(string? Profile = null, float Volume = 1f, bool Enabled = true)
{
    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Swittcs", "settings.json");

    public static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
    }
}

sealed class TrayApp : ApplicationContext
{
    static readonly float[] Volumes = [0.25f, 0.5f, 0.75f, 1f, 1.5f, 2f, 3f];

    public readonly AudioEngine Audio = new();
    readonly KeyboardHook _hook = new();
    readonly NotifyIcon _tray;
    readonly Icon _iconOn = Program.LoadIcon("app.ico", SystemInformation.SmallIconSize);
    readonly Icon _iconOff = Program.LoadIcon("app-off.ico", SystemInformation.SmallIconSize);
    readonly Control _ui = new(); // marshals pool-thread events onto the UI thread
    readonly SettingsWindow _window;
    AppSettings _settings = AppSettings.Load();
    int _keystrokes;

    public int Keystrokes => Volatile.Read(ref _keystrokes);

    public TrayApp()
    {
        _ui.CreateControl();

        var profiles = SoundBank.Profiles;
        string? profile = profiles.Contains(_settings.Profile) ? _settings.Profile : profiles.FirstOrDefault();
        if (profile != null) Audio.LoadProfile(profile);
        Audio.Volume = _settings.Volume;
        Audio.Enabled = _settings.Enabled;

        _tray = new NotifyIcon { Text = "Swittcs", Visible = true, ContextMenuStrip = new ContextMenuStrip() };
        _tray.ContextMenuStrip.Opening += (_, _) => BuildMenu();
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };

        _hook.Key += Audio.Play;
        _hook.Key += (_, down) => { if (down) Interlocked.Increment(ref _keystrokes); };
        _hook.Install();

        _window = new SettingsWindow(this);
        Sync();
        ShowWindow();
    }

    void BuildMenu()
    {
        var m = _tray.ContextMenuStrip!.Items;
        m.Clear();
        m.Add(new ToolStripMenuItem("Open Swittcs", null, (_, _) => ShowWindow()) { Font = new Font(_tray.ContextMenuStrip.Font, FontStyle.Bold) });
        m.Add(new ToolStripSeparator());
        m.Add(new ToolStripMenuItem("Enabled", null, (_, _) => Toggle()) { Checked = Audio.Enabled });

        var sound = new ToolStripMenuItem("Switch");
        foreach (string p in SoundBank.Profiles)
            sound.DropDownItems.Add(new ToolStripMenuItem(SettingsWindow.DisplayName(p), null, (_, _) => SetProfile(p)) { Checked = p == Audio.Profile });
        if (SoundBank.Profiles.Length == 0) sound.DropDownItems.Add(new ToolStripMenuItem("No switch sounds in this build") { Enabled = false });
        m.Add(sound);

        var volume = new ToolStripMenuItem("Volume");
        foreach (float v in Volumes)
            volume.DropDownItems.Add(new ToolStripMenuItem($"{v:P0}", null, (_, _) => SetVolume(v)) { Checked = Math.Abs(v - Audio.Volume) < 0.01f });
        m.Add(volume);

        m.Add(new ToolStripSeparator());
        m.Add("Quit", null, (_, _) => Quit());
    }

    void ShowWindow()
    {
        _window.Show();
        if (_window.WindowState == FormWindowState.Minimized) _window.WindowState = FormWindowState.Normal;
        _window.Activate();
    }

    public void ShowHint(string title, string text) => _tray.ShowBalloonTip(4000, title, text, ToolTipIcon.Info);

    public void Toggle()
    {
        Audio.Enabled = !Audio.Enabled;
        Save();
    }

    public void SetProfile(string name)
    {
        Audio.LoadProfile(name);
        Save();
    }

    public void SetVolume(float v)
    {
        Audio.Volume = v;
        Save();
    }

    public void Quit() => ExitThread();

    void Save()
    {
        (_settings = _settings with { Profile = Audio.Profile, Volume = Audio.Volume, Enabled = Audio.Enabled }).Save();
        Sync();
    }

    /// <summary>Reflects current state in the tray icon and the window.</summary>
    void Sync()
    {
        bool playing = Audio.Enabled;
        _tray.Icon = playing ? _iconOn : _iconOff;
        _tray.Text = playing ? "Swittcs" : "Swittcs (off)";
        _window.Sync();
    }

    protected override void ExitThreadCore()
    {
        _hook.Dispose();
        Audio.Dispose();
        _window.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        base.ExitThreadCore();
    }
}
