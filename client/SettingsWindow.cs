using System.Drawing.Drawing2D;
using System.Globalization;

namespace Swittcs;

/// <summary>Main window with two pages: Sound (switch, volume) and Typing test. Closing hides it; the tray icon keeps running.</summary>
sealed class SettingsWindow : Form
{
    readonly TrayApp _app;
    readonly LinkLabel _navSound = NavLink("Sound"), _navTyping = NavLink("Typing test");
    readonly Panel _soundPage = Page(), _typingPage = Page();
    readonly Label _switchName = new() { Location = new(40, 194), Size = new(330, 44), AutoEllipsis = true, Font = new Font("Segoe UI Semibold", 22f) };
    readonly ToggleSwitch _enabled = new() { Text = "Play keyboard sounds", Location = new(388, 200), Size = new(52, 30) };
    readonly Pill _status = new() { Location = new(40, 244) };
    readonly Label _volumeValue = new() { Location = new(40, 316), AutoSize = true, Font = new Font("Segoe UI Semibold", 20f) };
    readonly FlatSlider _volume = new() { Maximum = 300, Location = new(40, 362), Size = new(400, 28) };
    readonly Label _keystrokes = Value(40), _samples = Value(180), _device = Value(320);
    readonly FlowLayoutPanel _tiles = new() { Location = new(472, 196), Size = new(456, 404), AutoScroll = true, Padding = new Padding(0) };
    readonly Segmented _language = new("Word language", "English", "Indonesia") { Bounds = new(40, 140, 220, 36) };
    readonly Segmented _duration = new("Test length", "15s", "30s", "60s") { Bounds = new(276, 140, 180, 36) };
    readonly TypingTest _test = new() { Bounds = new(40, 300, 880, 170) };
    readonly Label _wpm = Value(40, 220), _accuracy = Value(220, 220), _timeLeft = Value(400, 220), _testSwitch = Value(580, 220, 340);
    readonly Label _testHint = Muted("", 40, 486);
    readonly System.Windows.Forms.Timer _tick = new() { Interval = 500 };
    bool _syncing;
    bool _hintShown;

    public SettingsWindow(TrayApp app)
    {
        _app = app;
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Swittcs";
        Icon = Program.LoadIcon("app.ico", new Size(32, 32));
        Font = new Font("Segoe UI", 9.75f);
        BackColor = Color.White;
        ForeColor = Theme.Ink;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(960, 630);

        // Top bar. Added before the pages so it stays in front of them.
        Controls.Add(new PictureBox { Image = Program.LoadIcon("app.ico", new Size(32, 32)).ToBitmap(), Bounds = new(40, 24, 32, 32), SizeMode = PictureBoxSizeMode.Zoom });
        Controls.Add(new Label { Text = "Swittcs", AutoSize = true, Location = new(80, 25), Font = new Font("Segoe UI Semibold", 15f) });
        var nav = new FlowLayoutPanel { Bounds = new(196, 24, 220, 32), Padding = new Padding(0, 7, 0, 0), WrapContents = false };
        nav.Controls.AddRange([_navSound, _navTyping]);
        Controls.Add(nav);
        var quit = new Button { Text = "&Quit", Bounds = new(848, 24, 72, 32), FlatStyle = FlatStyle.Flat, BackColor = Theme.Surface };
        quit.FlatAppearance.BorderSize = 0;
        Controls.Add(quit);

        // Sound page, left: now playing
        var s = _soundPage.Controls;
        s.Add(Title("Sound"));
        s.Add(Heading("Now playing", 40, 140));
        s.Add(Muted("Current switch", 40, 174));
        s.AddRange([_switchName, _enabled, _status]);
        s.Add(Muted("Volume (up to 300% boost)", 40, 296));
        s.AddRange([_volumeValue, _volume]);
        s.Add(new Panel { Bounds = new(40, 412, 400, 1), BackColor = Theme.Line });
        s.AddRange([Muted("Keystrokes", 40, 432), _keystrokes, Muted("this session", 40, 484)]);
        s.AddRange([Muted("Samples", 180, 432), _samples, Muted("in this switch", 180, 484)]);
        s.AddRange([Muted("Output", 320, 432), _device, Muted("Windows default", 320, 484)]);
        var tryBox = new RoundPanel { Bounds = new(40, 540, 400, 52) };
        tryBox.Controls.Add(new TextBox
        {
            PlaceholderText = "Type here to hear the selected switch",
            AccessibleName = "Try the selected switch",
            BorderStyle = BorderStyle.None, BackColor = Theme.Surface, Bounds = new(16, 16, 368, 24),
        });
        s.Add(tryBox);

        // Sound page, right: switch tiles
        s.Add(Heading("Switches", 480, 140));
        s.Add(Muted($"{SoundBank.Profiles.Length} installed. Pick one, then type.", 480, 174));
        foreach (string p in SoundBank.Profiles)
        {
            int count = SoundBank.Files(p).Count(f => f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase));
            var tile = new Tile(p, count) { Size = new(136, 92), Margin = new Padding(4) };
            tile.Click += (_, _) => { if (!_syncing) _app.SetProfile(p); };
            _tiles.Controls.Add(tile);
        }
        s.Add(_tiles);

        // Typing page
        var t = _typingPage.Controls;
        t.Add(Title("Typing test"));
        t.AddRange([_language, _duration]);
        var restart = new Button { Text = "&Restart", Bounds = new(840, 140, 80, 36), FlatStyle = FlatStyle.Flat, BackColor = Theme.Surface };
        restart.FlatAppearance.BorderSize = 0;
        t.Add(restart);
        t.AddRange([Muted("WPM", 40, 200), _wpm, Muted("Accuracy", 220, 200), _accuracy, Muted("Time left", 400, 200), _timeLeft, Muted("Switch", 580, 200), _testSwitch]);
        t.AddRange([_test, _testHint]);
        _duration.Selected = 1;

        Controls.AddRange([_soundPage, _typingPage]);
        ResumeLayout();

        _enabled.CheckedChanged += (_, _) => { if (!_syncing) _app.Toggle(); };
        _volume.ValueChanged += (_, _) => { _volumeValue.Text = $"{_volume.Value}%"; if (!_syncing) _app.SetVolume(_volume.Value / 100f); };
        _navSound.LinkClicked += (_, _) => ShowPage(typing: false);
        _navTyping.LinkClicked += (_, _) => ShowPage(typing: true);
        _language.SelectedChanged += (_, _) => NewTest();
        _duration.SelectedChanged += (_, _) => NewTest();
        restart.Click += (_, _) => NewTest();
        _test.StatsChanged += UpdateTestStats;
        quit.Click += (_, _) => _app.Quit();
        _tick.Tick += (_, _) => UpdateLive();
        VisibleChanged += (_, _) => { _tick.Enabled = Visible; if (Visible) UpdateLive(); };

        NewTest();
        ShowPage(typing: false);
        Sync();
    }

    static Panel Page() => new() { Bounds = new(0, 0, 960, 630), BackColor = Color.White };
    static Label Title(string text) => new() { Text = text, AutoSize = true, Location = new(34, 72), Font = new Font("Segoe UI Semibold", 30f), ForeColor = Theme.Faint };
    static LinkLabel NavLink(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(0, 0, 20, 0), ActiveLinkColor = Theme.Ink };

    void ShowPage(bool typing)
    {
        _soundPage.Visible = !typing;
        _typingPage.Visible = typing;
        foreach (var (link, active) in new[] { (_navSound, !typing), (_navTyping, typing) })
        {
            link.LinkColor = active ? Theme.Ink : Theme.Muted;
            link.LinkBehavior = active ? LinkBehavior.AlwaysUnderline : LinkBehavior.HoverUnderline;
        }
        if (typing) _test.Focus();
    }

    void NewTest()
    {
        _test.Restart(_language.Selected == 0 ? Words.English : Words.Indonesian, _duration.Selected switch { 0 => 15, 1 => 30, _ => 60 });
        _test.Focus();
    }

    void UpdateTestStats()
    {
        _wpm.Text = _test.Wpm.ToString("0", CultureInfo.CurrentCulture);
        _accuracy.Text = _test.Accuracy.ToString("P0", CultureInfo.CurrentCulture);
        _timeLeft.Text = $"{_test.SecondsLeft}s";
        _testHint.Text = _test.Finished
            ? $"Done: {_wpm.Text} WPM at {_accuracy.Text} accuracy. Press Tab or Restart to go again."
            : "The timer starts on your first key. Space moves to the next word, Tab restarts.";
    }

    static Label Heading(string text, int x, int y) => new() { Text = text, AutoSize = true, Location = new(x, y), Font = new Font("Segoe UI Semibold", 14f) };
    static Label Muted(string text, int x, int y) => new() { Text = text, AutoSize = true, Location = new(x, y), ForeColor = Theme.Muted };
    static Label Value(int x, int y = 452, int width = 124) => new() { Location = new(x, y), Size = new(width, 30), AutoEllipsis = true, Font = new Font("Segoe UI Semibold", 15f) };

    /// <summary>"akko-cs-jelly-black" → "Akko Cs Jelly Black".</summary>
    public static string DisplayName(string profile) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(profile.Replace('-', ' '));

    void UpdateLive()
    {
        _keystrokes.Text = _app.Keystrokes.ToString("N0", CultureInfo.CurrentCulture);
        _device.Text = _app.Audio.DeviceName ?? "None";
    }

    /// <summary>Pulls current state from the app. Called after any change, including ones made from the tray menu.</summary>
    public void Sync()
    {
        _syncing = true;
        _enabled.Checked = _app.Audio.Enabled;
        _switchName.Text = _testSwitch.Text = _app.Audio.Profile is { } p ? DisplayName(p) : "No switch installed";
        (_status.Text, _status.Fill) = _app.Audio.Enabled ? ("Playing", Theme.Good) : ("Off", Theme.Surface);
        _status.Invalidate();
        _volume.Value = (int)MathF.Round(_app.Audio.Volume * 100);
        _volumeValue.Text = $"{_volume.Value}%";
        foreach (Tile t in _tiles.Controls)
        {
            t.Selected = t.Profile == _app.Audio.Profile;
            if (t.Selected) _samples.Text = t.Samples.ToString(CultureInfo.CurrentCulture);
        }
        UpdateLive();
        _syncing = false;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason != CloseReason.UserClosing) return;
        e.Cancel = true;
        Hide();
        if (_hintShown) return;
        _hintShown = true;
        _app.ShowHint("Swittcs is still running", "Click the tray icon to open it again, or right-click for quick settings.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tick.Dispose();
        base.Dispose(disposing);
    }
}

static class Theme
{
    public static readonly Color Ink = Color.FromArgb(17, 17, 17);
    public static readonly Color Muted = Color.FromArgb(107, 114, 128);   // 4.8:1 on white
    public static readonly Color Faint = Color.FromArgb(139, 144, 153);   // page title only: large text, 3.3:1
    public static readonly Color Surface = Color.FromArgb(240, 241, 243);
    public static readonly Color SurfaceHover = Color.FromArgb(228, 230, 233);
    public static readonly Color Line = Color.FromArgb(229, 231, 235);
    public static readonly Color Accent = Color.FromArgb(0, 225, 240);    // selected switch only
    public static readonly Color OnAccent = Color.FromArgb(0, 72, 80);
    public static readonly Color Good = Color.FromArgb(134, 239, 120);
    public static readonly Color Bad = Color.FromArgb(254, 202, 202);
    public static readonly Color Error = Color.FromArgb(220, 38, 38);     // mistyped letters, 4.8:1 on white

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void Fill(Graphics g, RectangleF r, float radius, Color c)
    {
        using var path = Round(r, radius);
        using var brush = new SolidBrush(c);
        g.FillPath(brush, path);
    }

    public static void FocusRing(Graphics g, RectangleF r, float radius, float scale)
    {
        using var path = Round(r, radius);
        using var pen = new Pen(Ink, 2 * scale);
        g.DrawPath(pen, path);
    }
}

/// <summary>One switch profile. Click, Space or Enter selects it.</summary>
sealed class Tile : Control
{
    static readonly Font NameFont = new("Segoe UI Semibold", 9.75f), SmallFont = new("Segoe UI", 8.25f), BadgeFont = new("Segoe UI Semibold", 10f);
    public string Profile { get; }
    public int Samples { get; }
    readonly string _brand, _model;
    bool _selected, _hover;

    public Tile(string profile, int samples)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        Profile = profile;
        Samples = samples;
        string name = SettingsWindow.DisplayName(profile);
        string[] words = name.Split(' ');
        _brand = words.Length > 1 ? words[0].ToUpperInvariant() : "";
        _model = words.Length > 1 ? string.Join(' ', words[1..]) : name;
        Text = name; // screen readers read the window text
    }

    public bool Selected
    {
        get => _selected;
        set { if (_selected != value) { _selected = value; Invalidate(); } }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        float s = DeviceDpi / 96f;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent!.BackColor);
        var box = new RectangleF(0, 0, Width - 1, Height - 1);
        Theme.Fill(g, box, 10 * s, _selected ? Theme.Accent : _hover ? Theme.SurfaceHover : Theme.Surface);
        var badge = new RectangleF(10 * s, 10 * s, 24 * s, 24 * s);
        Theme.Fill(g, badge, 6 * s, Theme.Ink);
        TextRenderer.DrawText(g, Text[..1], BadgeFont, Rectangle.Round(badge), Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        var secondary = _selected ? Theme.OnAccent : Theme.Muted;
        TextRenderer.DrawText(g, _brand, SmallFont, new Rectangle((int)(40 * s), (int)(14 * s), Width - (int)(48 * s), (int)(18 * s)), secondary, TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, _model, NameFont, new Rectangle((int)(10 * s), Height - (int)(44 * s), Width - (int)(18 * s), (int)(20 * s)), Theme.Ink, TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, $"{Samples} samples", SmallFont, new Rectangle((int)(10 * s), Height - (int)(24 * s), Width - (int)(18 * s), (int)(18 * s)), secondary, TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues) Theme.FocusRing(g, RectangleF.Inflate(box, -s, -s), 10 * s, s);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnMouseDown(MouseEventArgs e) { Focus(); base.OnMouseDown(e); }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
}

/// <summary>On/off switch. Still a CheckBox underneath, so keyboard and accessibility come for free.</summary>
sealed class ToggleSwitch : CheckBox
{
    public ToggleSwitch()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        AutoSize = false;
        Cursor = Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        float s = DeviceDpi / 96f;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent!.BackColor);
        var track = new RectangleF(4 * s, 4 * s, Width - 8 * s, Height - 8 * s);
        Theme.Fill(g, track, track.Height / 2, !Enabled ? Theme.Line : Checked ? Theme.Ink : Color.FromArgb(209, 213, 219));
        float d = track.Height - 6 * s;
        float x = Checked ? track.Right - 3 * s - d : track.X + 3 * s;
        g.FillEllipse(Brushes.White, x, track.Y + 3 * s, d, d);
        if (Focused && ShowFocusCues) Theme.FocusRing(g, new RectangleF(s, s, Width - 2 * s, Height - 2 * s), Height / 2f, s);
    }
}

/// <summary>Flat 0..Maximum slider in steps of 5. Arrows, PageUp/PageDown, Home/End.</summary>
sealed class FlatSlider : Control
{
    const int Step = 5;
    public int Maximum { get; init; } = 150;
    int _value;
    public event EventHandler? ValueChanged;

    public FlatSlider()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        Text = "Volume";
    }

    public int Value
    {
        get => _value;
        set
        {
            value = Math.Clamp(value, 0, Maximum);
            if (value == _value) return;
            _value = value;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    float Pad => 10 * DeviceDpi / 96f;

    void SetFromX(int x) => Value = (int)MathF.Round((x - Pad) / (Width - 2 * Pad) * Maximum / Step) * Step;

    protected override void OnMouseDown(MouseEventArgs e) { Focus(); if (e.Button == MouseButtons.Left) SetFromX(e.X); base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (e.Button == MouseButtons.Left) SetFromX(e.X); base.OnMouseMove(e); }
    protected override bool IsInputKey(Keys k) => k is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(k);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        int? v = e.KeyCode switch
        {
            Keys.Left or Keys.Down => Value - Step,
            Keys.Right or Keys.Up => Value + Step,
            Keys.PageDown => Value - 25,
            Keys.PageUp => Value + 25,
            Keys.Home => 0,
            Keys.End => Maximum,
            _ => null,
        };
        if (v is int n) { Value = n; e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        float s = DeviceDpi / 96f;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent!.BackColor);
        float cy = Height / 2f, w = Width - 2 * Pad, x = Pad + w * _value / Maximum;
        Theme.Fill(g, new RectangleF(Pad, cy - 2 * s, w, 4 * s), 2 * s, Theme.Line);
        Theme.Fill(g, new RectangleF(Pad, cy - 2 * s, x - Pad, 4 * s), 2 * s, Theme.Ink);
        float r = 8 * s;
        g.FillEllipse(Brushes.White, x - r, cy - r, 2 * r, 2 * r);
        using var pen = new Pen(Theme.Ink, 2 * s);
        g.DrawEllipse(pen, x - r, cy - r, 2 * r, 2 * r);
        if (Focused && ShowFocusCues) g.DrawEllipse(pen, x - r - 4 * s, cy - r - 4 * s, 2 * r + 8 * s, 2 * r + 8 * s);
    }
}

/// <summary>Pick-one row of options. Click or Left/Right.</summary>
sealed class Segmented : Control
{
    readonly string[] _options;
    int _selected;
    public event EventHandler? SelectedChanged;

    public Segmented(string name, params string[] options)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        Text = name;
        _options = options;
    }

    public int Selected
    {
        get => _selected;
        set
        {
            value = Math.Clamp(value, 0, _options.Length - 1);
            if (value == _selected) return;
            _selected = value;
            Invalidate();
            SelectedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e) { Focus(); Selected = e.X * _options.Length / Width; base.OnMouseDown(e); }
    protected override bool IsInputKey(Keys k) => k is Keys.Left or Keys.Right || base.IsInputKey(k);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left) Selected--;
        else if (e.KeyCode == Keys.Right) Selected++;
        base.OnKeyDown(e);
    }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        float s = DeviceDpi / 96f;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent!.BackColor);
        var box = new RectangleF(0, 0, Width - 1, Height - 1);
        Theme.Fill(g, box, 8 * s, Theme.Surface);
        float w = (float)Width / _options.Length;
        for (int i = 0; i < _options.Length; i++)
        {
            var seg = new RectangleF(i * w, 0, w, Height);
            if (i == _selected) Theme.Fill(g, RectangleF.Inflate(seg, -3 * s, -3 * s), 6 * s, Theme.Ink);
            TextRenderer.DrawText(g, _options[i], Font, Rectangle.Round(seg), i == _selected ? Color.White : Theme.Ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        if (Focused && ShowFocusCues) Theme.FocusRing(g, box, 8 * s, s);
    }
}

/// <summary>Rounded status label.</summary>
sealed class Pill : Label
{
    public Color Fill = Theme.Surface;

    public Pill()
    {
        AutoSize = true;
        Padding = new Padding(10, 4, 10, 4);
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent!.BackColor);
        Theme.Fill(e.Graphics, new RectangleF(0, 0, Width - 1, Height - 1), Height / 2f, Fill);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
}

/// <summary>Rounded surface behind the try-it text box.</summary>
sealed class RoundPanel : Panel
{
    public RoundPanel() => SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Theme.Fill(e.Graphics, new RectangleF(0, 0, Width - 1, Height - 1), 10 * DeviceDpi / 96f, Theme.Surface);
    }
}
