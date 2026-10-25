using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace Swittcs;

/// <summary>
/// Timed typing test drawn by hand: the target words, what was typed (black when right, red when wrong) and a caret.
/// The global hook plays the switch sounds, so this control only tracks text and stats.
/// </summary>
sealed class TypingTest : Control
{
    const int WordCount = 250;   // more than anyone types in 60 s
    const int VisibleLines = 3;
    static readonly Font TextFont = new("Segoe UI", 18f), HintFont = new("Segoe UI Semibold", 10f);

    readonly Stopwatch _clock = new();
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };
    readonly Dictionary<char, int> _charWidth = [];
    string[] _words = [], _pool = [];
    readonly List<string> _typed = [];
    int _keys, _correctKeys, _duration = 30;

    /// <summary>Raised after every keystroke and on each timer tick while running.</summary>
    public event Action? StatsChanged;

    public bool Finished { get; private set; }
    double Elapsed => Math.Min(_clock.Elapsed.TotalSeconds, _duration);
    public int SecondsLeft => (int)Math.Ceiling(_duration - Elapsed);
    public double Wpm => Elapsed < 1 ? 0 : CorrectChars() / 5.0 / (Elapsed / 60);
    public double Accuracy => _keys == 0 ? 1 : (double)_correctKeys / _keys;

    public TypingTest()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.IBeam;
        Text = "Typing test";
        _timer.Tick += (_, _) =>
        {
            if (Elapsed >= _duration) Finish();
            StatsChanged?.Invoke();
        };
    }

    public void Restart(string[] pool, int seconds)
    {
        _pool = pool;
        _duration = seconds;
        _words = new string[WordCount];
        for (int i = 0; i < WordCount; i++)
            do _words[i] = pool[Random.Shared.Next(pool.Length)]; while (i > 0 && _words[i] == _words[i - 1]);
        _typed.Clear();
        _typed.Add("");
        _keys = _correctKeys = 0;
        _clock.Reset();
        _timer.Stop();
        Finished = false;
        Invalidate();
        StatsChanged?.Invoke();
    }

    void Finish()
    {
        _clock.Stop();
        _timer.Stop();
        Finished = true;
        Invalidate();
    }

    /// <summary>Characters of correctly typed words plus their spaces, and the correct prefix of the current word.</summary>
    int CorrectChars()
    {
        int n = 0, cur = _typed.Count - 1;
        for (int i = 0; i < cur; i++)
            if (_typed[i] == _words[i]) n += _words[i].Length + 1;
        if (_words[cur].StartsWith(_typed[cur], StringComparison.Ordinal)) n += _typed[cur].Length;
        return n;
    }

    protected override bool IsInputKey(Keys k) => k == Keys.Tab || base.IsInputKey(k);

    // Every char is ours. Otherwise WinForms treats plain letters as mnemonics: typing "r" would press &Restart, "q" &Quit.
    protected override bool IsInputChar(char c) => true;

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        e.Handled = true;
        if (e.KeyChar == '\t') { Restart(_pool, _duration); return; }
        if (Finished || _words.Length == 0) return;
        int cur = _typed.Count - 1;
        string word = _words[cur], typed = _typed[cur];
        switch (e.KeyChar)
        {
            case '\b':
                if (typed.Length > 0) _typed[cur] = typed[..^1];
                break;
            case ' ':
                if (typed.Length == 0) return;
                _keys++;
                if (typed == word) _correctKeys++;
                _typed.Add("");
                break;
            case var c when !char.IsControl(c):
                if (typed.Length >= word.Length + 10) return; // runaway extra letters
                if (!_clock.IsRunning) { _clock.Start(); _timer.Start(); }
                _keys++;
                if (typed.Length < word.Length && word[typed.Length] == c) _correctKeys++;
                _typed[cur] = typed + c;
                break;
            default:
                return;
        }
        Invalidate();
        StatsChanged?.Invoke();
    }

    protected override void OnMouseDown(MouseEventArgs e) { Focus(); base.OnMouseDown(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    int CharWidth(Graphics g, char c)
    {
        if (!_charWidth.TryGetValue(c, out int w))
            _charWidth[c] = w = TextRenderer.MeasureText(g, c.ToString(), TextFont, Size.Empty, TextFormatFlags.NoPadding).Width;
        return w;
    }

    protected override void OnDpiChangedAfterParent(EventArgs e) { _charWidth.Clear(); base.OnDpiChangedAfterParent(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        float s = DeviceDpi / 96f;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent!.BackColor);
        Theme.Fill(g, new RectangleF(0, 0, Width - 1, Height - 1), 12 * s, Theme.Surface);
        if (_words.Length == 0) return;

        int pad = (int)(24 * s), lineHeight = (int)(TextFont.GetHeight(g) * 1.35f), space = CharWidth(g, ' ');
        int cur = _typed.Count - 1;

        // Lay out every word; remember which line each starts on.
        var pos = new (int Line, int X)[_words.Length];
        int line = 0, x = 0, maxX = Width - 2 * pad;
        for (int i = 0; i < _words.Length; i++)
        {
            string shown = i <= cur && _typed[i].Length > _words[i].Length ? _typed[i] : _words[i];
            int w = 0;
            foreach (char c in shown) w += CharWidth(g, c);
            if (x > 0 && x + w > maxX) { line++; x = 0; }
            pos[i] = (line, x);
            x += w + space;
        }

        // Keep the current word on the second visible line once past the first.
        int first = Math.Max(0, pos[cur].Line - 1);
        int top = (Height - VisibleLines * lineHeight) / 2;
        using var miss = new Pen(Theme.Error, 2 * s);
        for (int i = 0; i < _words.Length; i++)
        {
            int row = pos[i].Line - first;
            if (row < 0) continue;
            if (row >= VisibleLines) break;
            int px = pad + pos[i].X, py = top + row * lineHeight;
            string word = _words[i];
            string typed = i <= cur ? _typed[i] : "";
            int len = Math.Max(word.Length, typed.Length);
            for (int j = 0; j < len; j++)
            {
                char c = j < word.Length ? word[j] : typed[j];
                Color color = j >= typed.Length ? Theme.Muted
                    : j < word.Length && typed[j] == word[j] ? Theme.Ink : Theme.Error;
                TextRenderer.DrawText(g, c.ToString(), TextFont, new Point(px, py), color, TextFormatFlags.NoPadding);
                px += CharWidth(g, c);
            }
            if (i < cur && typed != word) // finished word with a mistake
                g.DrawLine(miss, pad + pos[i].X, py + lineHeight - 6 * s, px, py + lineHeight - 6 * s);
            if (i == cur && Focused && !Finished)
            {
                int cx = pad + pos[i].X;
                for (int j = 0; j < typed.Length; j++) cx += CharWidth(g, j < word.Length ? word[j] : typed[j]);
                using var caret = new Pen(Theme.Ink, 2 * s);
                g.DrawLine(caret, cx, py + 6 * s, cx, py + lineHeight - 8 * s);
            }
        }

        if (!Focused && !Finished)
        {
            const string hint = "Click here to start typing";
            var size = TextRenderer.MeasureText(g, hint, HintFont);
            var r = new RectangleF((Width - size.Width) / 2f - 16 * s, (Height - size.Height) / 2f - 10 * s, size.Width + 32 * s, size.Height + 20 * s);
            Theme.Fill(g, r, r.Height / 2, Theme.Ink);
            TextRenderer.DrawText(g, hint, HintFont, Rectangle.Round(r), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}

static class Words
{
    public static readonly string[] English = Split("""
        the be to of and a in that have it for not on with he as you do at this but his by from they we say her she or an
        will my one all would there their what so up out if about who get which go me when make can like time no just him
        know take people into year your good some could them see other than then now look only come its over think also
        back after use two how our work first well way even new want because any these give day most us is was are been
        has had were said did made find long down very great where through much before right too old same tell feel high
        last own life world might never under while house home school still small every place again point hand off play
        turn move live start show part keep help word read thing open seem together next white begin got walk example
        paper group always music those both mark often letter until mile river car feet care second book carry took
        science eat room friend began idea fish mountain stop once base hear horse cut sure watch color face wood main
        enough plain girl usual young ready above ever red list though talk bird soon body dog family direct leave song
        measure door black short class wind question happen complete ship area half rock order fire south problem piece
        told knew pass since top whole king space heard best hour better true during hundred five remember step early
        hold west ground interest reach fast sing listen six table travel less morning simple several toward war lay
        against pattern slow center love person money serve appear road map rain rule pull cold notice voice power town
        fine certain fly fall lead cry dark machine note wait plan figure star box noun field rest able pound done beauty
        drive stood contain front teach week final gave green quick develop ocean warm free minute strong special mind
        behind clear tail produce fact street inch nothing course stay wheel full force blue object decide surface deep
        moon island foot system busy test record boat common gold possible plane dry wonder laugh thousand ago ran check
        game shape yes hot miss brought heat snow tire bring distant fill east paint language among
        """);

    public static readonly string[] Indonesian = Split("""
        yang dan di ini itu dengan untuk tidak dari dalam akan pada juga saya ke karena tersebut bisa ada mereka lebih
        kami sudah atau hanya oleh telah kita bahwa seperti jika sebagai harus tahun kata orang baru dapat masih anak
        bukan semua sangat hari waktu banyak satu dua tiga empat lima besar kecil rumah jalan kerja makan minum tidur
        pergi pulang datang lihat dengar baca tulis bicara tanya jawab buat ambil beri bawa pakai cari temu tunggu jual
        beli bayar mulai selesai buka tutup naik turun masuk keluar duduk berdiri lari main belajar ajar tahu kenal
        ingat lupa suka cinta senang sedih marah takut berani baik buruk benar salah mudah sulit cepat lambat panjang
        pendek tinggi rendah jauh dekat lama muda tua kaya miskin bersih kotor panas dingin terang gelap pagi siang sore
        malam besok kemarin sekarang nanti selalu sering kadang pernah belum lagi cuma paling kurang sama beda air api
        tanah udara langit laut gunung sungai pohon bunga buah daun batu kota desa negara dunia sekolah kantor pasar
        toko buku meja kursi pintu jendela kamar dapur mobil motor kereta kapal pesawat teman keluarga ibu ayah kakak
        adik guru murid dokter polisi uang harga warna merah putih hitam biru hijau kuning nama suara musik lagu cerita
        berita kabar hujan angin matahari bulan bintang pantai hutan kebun sawah nasi roti ikan ayam sayur garam gula
        kopi teh susu telur tangan kaki kepala mata telinga mulut hidung rambut badan hati pikir rasa coba bantu jaga
        kirim terima pilih ubah tambah kurang hitung ukur simpan hapus tekan ketik layar papan tombol suara dengar
        pesan surat kertas pena gambar foto berapa kapan dimana siapa mengapa bagaimana apa mana sini sana situ semua
        setiap beberapa banyak sedikit cukup penuh kosong awal akhir tengah atas bawah depan belakang kiri kanan dalam
        luar antara sekitar bersama sendiri lain baru indah bagus enak manis pahit asin pedas segar hangat sejuk ramai
        sepi aman bebas penting perlu mungkin pasti boleh mau ingin akan sedang masih hampir tentu memang hanya
        """);

    static string[] Split(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray();
}
