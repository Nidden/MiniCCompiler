using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace CompMacro11
{
    public class Sprite
    {
        public string Name = "sprite";
        public int Words = 1;
        public int Height = 16;
        public int[] Pixels;
        public int PalIdx = 0;

        public int PixelWidth => Words * 8;

        public Sprite(string name = "sprite", int words = 1, int height = 16)
        {
            Name = name; Words = words; Height = height;
            Pixels = new int[PixelWidth * height];
        }

        public void Resize(int newWords, int newHeight)
        {
            newHeight = Math.Max(1, Math.Min(200, newHeight));
            newWords = Math.Max(1, newWords);
            int nw = newWords * 8, ow = PixelWidth;
            var np = new int[nw * newHeight];
            for (int y = 0; y < Math.Min(Height, newHeight); y++)
                for (int x = 0; x < Math.Min(ow, nw); x++)
                    np[y * nw + x] = Pixels[y * ow + x];
            Words = newWords; Height = newHeight; Pixels = np;
        }

        public string Serialize()
        {
            return $"NAME={Name}\nWORDS={Words}\nHEIGHT={Height}\nPAL={PalIdx}\nDATA={string.Join(",", Pixels)}\n";
        }

        public static Sprite Deserialize(string block)
        {
            var s = new Sprite();
            foreach (var line in block.Split('\n'))
            {
                if (line.StartsWith("NAME=")) s.Name = line.Substring(5);
                if (line.StartsWith("WORDS=")) { int.TryParse(line.Substring(6), out s.Words); }
                if (line.StartsWith("HEIGHT=")) { int.TryParse(line.Substring(7), out s.Height); }
                if (line.StartsWith("PAL=")) { int.TryParse(line.Substring(4), out s.PalIdx); }
                if (line.StartsWith("DATA="))
                {
                    var parts = line.Substring(5).Split(',');
                    s.Pixels = new int[parts.Length];
                    for (int i = 0; i < parts.Length; i++)
                        int.TryParse(parts[i], out s.Pixels[i]);
                }
            }
            if (s.Pixels == null || s.Pixels.Length != s.PixelWidth * s.Height)
                s.Pixels = new int[s.PixelWidth * s.Height];
            return s;
        }

        public string ExportC()
        {
            int pw = PixelWidth;
            var wordList = new List<int>();

            for (int y = 0; y < Height; y++)
                for (int wx = 0; wx < Words; wx++)
                {
                    int plane0 = 0, plane1 = 0;
                    for (int b = 0; b < 8; b++)
                    {
                        int c = Pixels[y * pw + wx * 8 + b] & 3;
                        // Цвета 0..3 → два бита: бит0=план0, бит1=план1
                        // план0: цвета 0/1 (младший бит цвета)
                        // план1: цвета 0/2 (старший бит цвета)
                        if ((c & 1) != 0) plane0 |= (1 << b);
                        if ((c & 2) != 0) plane1 |= (1 << b);
                    }
                    // Слово: младший байт = план0, старший байт = план1
                    wordList.Add(plane0 | (plane1 << 8));
                }

            var sb = new StringBuilder();
            // Комментарий с названием и размером
            sb.AppendLine($"// {Name}  {pw}x{Height}");
            sb.AppendLine($"int {Name}[{wordList.Count}] = {{");
            for (int i = 0; i < wordList.Count; i += 8)
            {
                sb.Append("    ");
                int end = Math.Min(i + 8, wordList.Count);
                for (int j = i; j < end; j++)
                    sb.Append(wordList[j] + (j < wordList.Count - 1 ? ", " : ""));
                sb.AppendLine();
            }
            sb.AppendLine("};");
            sb.AppendLine($"// sprite(x, y, {Words}, {Height}, {Name});");
            return sb.ToString();
        }
    }

    public class SpriteEditor : Form
    {
        static readonly Color[][] PAL = {
            new[]{ Color.Black, Color.Red,     Color.Green,  Color.Yellow },  // палитра 1
            new[]{ Color.Blue,  Color.Magenta, Color.Cyan,   Color.White  },  // палитра 2
            new[]{ Color.Black, Color.FromArgb(85,85,85), Color.FromArgb(170,170,170), Color.White } // Ч/Б
        };
        static readonly string[] PAL_NAMES = { "Набор 1", "Набор 2", "Ч/Б" };

        static readonly Color C_BG = Color.FromArgb(28, 28, 28);
        static readonly Color C_BG2 = Color.FromArgb(37, 37, 38);
        static readonly Color C_BG3 = Color.FromArgb(22, 22, 22);
        static readonly Color C_TEXT = Color.FromArgb(212, 212, 212);
        static readonly Color C_GRAY = Color.FromArgb(100, 100, 100);
        static readonly Color C_SEL = Color.FromArgb(0, 100, 180);
        static readonly Font F_UI = new Font("Segoe UI", 9f);
        static readonly Font F_UI_B = new Font("Segoe UI", 9f, FontStyle.Bold);
        static readonly Font F_SMALL = new Font("Segoe UI", 7.5f);

        List<Sprite> _sprites = new List<Sprite>();
        int _cur = 0;
        int _colorIdx = 0;
        int _zoom = 8;
        bool _drawing = false;
        bool _busy = false;

        Action<string> _insertCode;

        FlowLayoutPanel _thumbPanel;
        PictureBox _pic;
        Label _zoomLbl;
        Label _sprNameLbl;
        ComboBox _selW;
        NumericUpDown _inpH;
        Button[] _palBtns = new Button[3];
        Panel[] _swatches = new Panel[4];

        // Автосохранение
        public static readonly string AutoSavePath = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location),
            "sprites_autosave.spr");

        System.Windows.Forms.Timer _autoSaveTimer;

        public List<Sprite> GetSprites() => new List<Sprite>(_sprites);

        public SpriteEditor(Action<string> insertCode)
        {
            _insertCode = insertCode;
            StartPosition = FormStartPosition.CenterScreen;

            // Таймер автосохранения — 2 секунды после последнего изменения
            _autoSaveTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            _autoSaveTimer.Tick += (s, e) => { _autoSaveTimer.Stop(); AutoSave(); };

            BuildUI();

            // AutoLoad ПОСЛЕ BuildUI — контролы уже созданы
            AutoLoad();
        }

        void BuildUI()
        {
            Text = "Редактор спрайтов — УКНЦ";
            Size = new Size(1000, 680);
            MinimumSize = new Size(700, 480);
            BackColor = C_BG;
            Font = F_UI;

            // ── Тулбар ──────────────────────────────────────────
            var bar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = C_BG2 };
            int bx = 8;
            TBtn(bar, "+ Новый", ref bx, C_BG3, () => NewSprite());
            TBtn(bar, "✎ Имя", ref bx, C_BG3, () => RenameCurrent());
            TBtn(bar, "📂 Открыть", ref bx, C_BG3, () => OpenFile());
            TBtn(bar, "🖼 Импорт", ref bx, C_BG3, () => ImportImage());
            TBtn(bar, "💾 Сохранить", ref bx, C_BG3, () => SaveFile());
            TBtnSep(bar, ref bx);
            TBtn(bar, "✕ Удалить", ref bx, Color.FromArgb(70, 25, 25), () => DeleteSprite());
            TBtnSep(bar, ref bx);
            TBtn(bar, "→ В код", ref bx, Color.FromArgb(0, 80, 50), () => ExportCode());


            // ── Нижняя панель ────────────────────────────────────
            var bot = new Panel { Dock = DockStyle.Bottom, Height = 68, BackColor = C_BG2 };

            // Имя спрайта
            _sprNameLbl = new Label
            {
                Location = new Point(8, 4),
                Width = 120,
                Height = 18,
                Text = "sprite",
                ForeColor = C_TEXT,
                Font = F_UI_B,
                Cursor = Cursors.Hand
            };
            _sprNameLbl.DoubleClick += (s, e) => RenameCurrent();
            bot.Controls.Add(_sprNameLbl);

            // Размер
            MiniLabel(bot, "W:", 8, 26);
            _selW = new ComboBox
            {
                Location = new Point(28, 22),
                Width = 72,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = C_BG3,
                ForeColor = C_TEXT,
                FlatStyle = FlatStyle.Flat
            };
            for (int i = 1; i <= 8; i++) _selW.Items.Add(i * 8 + " пикс");
            _selW.SelectedIndex = 0;
            _selW.SelectedIndexChanged += (s, e) => { if (!_busy) ApplySize(); };
            bot.Controls.Add(_selW);

            MiniLabel(bot, "H:", 108, 26);
            _inpH = new NumericUpDown
            {
                Location = new Point(122, 22),
                Width = 52,
                Minimum = 1,
                Maximum = 200,
                Value = 16,
                BackColor = C_BG3,
                ForeColor = C_TEXT,
                BorderStyle = BorderStyle.None
            };
            _inpH.ValueChanged += (s, e) => { if (!_busy) ApplySize(); };
            bot.Controls.Add(_inpH);

            // Зум
            VSep(bot, 186, 4);
            MiniLabel(bot, "Зум:", 194, 6);
            SmallBtn(bot, "−", 194, 24, 26, () => SetZoom(_zoom / 2));
            _zoomLbl = new Label
            {
                Location = new Point(224, 24),
                Width = 30,
                Height = 22,
                Text = "8×",
                ForeColor = C_TEXT,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = F_UI_B
            };
            bot.Controls.Add(_zoomLbl);
            SmallBtn(bot, "+", 256, 24, 26, () => SetZoom(_zoom * 2));
            SmallBtn(bot, "1:1", 286, 24, 34, () => SetZoom(1));

            // Палитра
            VSep(bot, 334, 4);
            MiniLabel(bot, "Палитра:", 342, 6);
            for (int i = 0; i < 3; i++)
            {
                int ii = i;
                _palBtns[i] = new Button
                {
                    Text = PAL_NAMES[i],
                    Location = new Point(342 + i * 74, 22),
                    Width = 70,
                    Height = 22,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = ii == 0 ? C_SEL : C_BG3,
                    ForeColor = C_TEXT,
                    Font = F_SMALL,
                    Cursor = Cursors.Hand
                };
                _palBtns[i].FlatAppearance.BorderSize = 0;
                _palBtns[i].Click += (s, e) => {
                    _sprites[_cur].PalIdx = ii;
                    UpdatePal(); DrawCanvas(); RefreshThumbs();
                };
                bot.Controls.Add(_palBtns[i]);
            }

            // Свотчи
            VSep(bot, 564, 4);
            MiniLabel(bot, "Цвет:", 572, 6);
            for (int i = 0; i < 4; i++)
            {
                int ii = i;
                _swatches[i] = new Panel
                {
                    Location = new Point(572 + i * 34, 22),
                    Width = 28,
                    Height = 28,
                    Cursor = Cursors.Hand
                };
                _swatches[i].Click += (s, e) => { _colorIdx = ii; UpdateSwatches(); };
                _swatches[i].Paint += (s, e) => {
                    if (ii == _colorIdx)
                        e.Graphics.DrawRectangle(new Pen(Color.White, 2), 1, 1, 24, 24);
                };
                bot.Controls.Add(_swatches[i]);
            }


            // ── Левая: миниатюры ─────────────────────────────────
            var leftPanel = new Panel { Dock = DockStyle.Left, Width = 120, BackColor = C_BG3 };
            var leftHdr = new Label
            {
                Text = " Спрайты",
                Dock = DockStyle.Top,
                Height = 22,
                ForeColor = C_GRAY,
                BackColor = C_BG2,
                Font = F_SMALL
            };
            _thumbPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = C_BG3,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(4)
            };
            leftPanel.Controls.Add(_thumbPanel);
            leftPanel.Controls.Add(leftHdr);

            // ── Центр: холст ──────────────────────────────────────
            var wrap = new Panel { Dock = DockStyle.Fill, BackColor = C_BG3, AutoScroll = true };
            _pic = new PictureBox { Location = new Point(8, 8), SizeMode = PictureBoxSizeMode.AutoSize, Cursor = Cursors.Cross };
            _pic.MouseDown += (s, e) => { _drawing = true; PaintPixel(e.X, e.Y); };
            _pic.MouseMove += (s, e) => { if (_drawing) PaintPixel(e.X, e.Y); };
            _pic.MouseUp += (s, e) => _drawing = false;
            wrap.Controls.Add(_pic);

            // ── Порядок добавления важен для WinForms Dock ────────
            // Top и Bottom — первыми, затем Left, затем Fill последним
            Controls.Add(wrap);       // Fill — самый последний
            Controls.Add(leftPanel);  // Left — перед Fill
            Controls.Add(bot);        // Bottom
            Controls.Add(bar);        // Top
        }

        // ── Хелперы UI ───────────────────────────────────────────
        void TBtn(Panel p, string t, ref int x, Color bg, Action click, int w = 82)
        {
            var b = new Button
            {
                Text = t,
                Location = new Point(x, 5),
                Width = w,
                Height = 30,
                FlatStyle = FlatStyle.Flat,
                BackColor = bg,
                ForeColor = Color.White,
                Font = F_UI,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            b.Click += (s, e) => click();
            p.Controls.Add(b); x += w + 4;
        }
        void TBtnSep(Panel p, ref int x)
        {
            p.Controls.Add(new Panel { Location = new Point(x, 8), Width = 1, Height = 24, BackColor = C_GRAY });
            x += 9;
        }
        void SmallBtn(Panel p, string t, int x, int y, int w, Action click)
        {
            var b = new Button
            {
                Text = t,
                Location = new Point(x, y),
                Width = w,
                Height = 22,
                FlatStyle = FlatStyle.Flat,
                BackColor = C_BG3,
                ForeColor = C_TEXT,
                Font = F_UI,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = C_GRAY; b.FlatAppearance.BorderSize = 1;
            b.Click += (s, e) => click();
            p.Controls.Add(b);
        }
        void VSep(Panel p, int x, int y)
            => p.Controls.Add(new Panel { Location = new Point(x, y), Width = 1, Height = 58, BackColor = C_GRAY });
        void MiniLabel(Panel p, string t, int x, int y)
            => p.Controls.Add(new Label { Text = t, Location = new Point(x, y), AutoSize = true, ForeColor = C_GRAY, Font = F_SMALL });

        // ── Операции со спрайтами ────────────────────────────────
        void NewSprite()
        {
            _sprites.Add(new Sprite($"sprite_{_sprites.Count}", 1, 16));
            _cur = _sprites.Count - 1;
            FullRefresh();
        }

        void DeleteSprite()
        {
            if (_sprites.Count <= 1) return;
            _sprites.RemoveAt(_cur);
            _cur = Math.Min(_cur, _sprites.Count - 1);
            FullRefresh();
        }

        void RenameCurrent()
        {
            var dlg = new Form
            {
                Text = "Переименовать",
                Size = new Size(280, 110),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = C_BG
            };
            var tb = new TextBox
            {
                Text = _sprites[_cur].Name,
                Location = new Point(10, 10),
                Width = 240,
                BackColor = C_BG3,
                ForeColor = C_TEXT,
                BorderStyle = BorderStyle.FixedSingle
            };
            var ok = new Button
            {
                Text = "OK",
                Location = new Point(150, 44),
                Width = 80,
                Height = 26,
                DialogResult = DialogResult.OK,
                FlatStyle = FlatStyle.Flat,
                BackColor = C_SEL,
                ForeColor = Color.White
            };
            dlg.Controls.AddRange(new Control[] { tb, ok });
            dlg.AcceptButton = ok;
            if (dlg.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(tb.Text))
            {
                _sprites[_cur].Name = tb.Text.Trim();
                FullRefresh();
            }
        }

        void ApplySize()
        {
            _sprites[_cur].Resize(_selW.SelectedIndex + 1, (int)_inpH.Value);
            DrawCanvas(); RefreshThumbs();
        }

        // ── Рисование ────────────────────────────────────────────
        void PaintPixel(int mx, int my)
        {
            var s = _sprites[_cur];
            int pw = s.PixelWidth;
            int px = mx / _zoom, py = my / _zoom;
            if (px < 0 || py < 0 || px >= pw || py >= s.Height) return;
            if (s.Pixels[py * pw + px] == _colorIdx) return;
            s.Pixels[py * pw + px] = _colorIdx;
            DrawCanvas(); RefreshThumb(_cur);
            ScheduleSave();
        }

        // ── Отрисовка холста ─────────────────────────────────────
        void DrawCanvas()
        {
            var s = _sprites[_cur];
            int pw = s.PixelWidth, ph = s.Height;
            int bw = pw * _zoom, bh = ph * _zoom;
            var bmp = new Bitmap(bw, bh);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                for (int y = 0; y < ph; y++)
                    for (int x = 0; x < pw; x++)
                    {
                        var c = PAL[s.PalIdx][s.Pixels[y * pw + x] % 4];
                        using (var br = new SolidBrush(c))
                            g.FillRectangle(br, x * _zoom, y * _zoom, _zoom, _zoom);
                    }
                // Сетка каждого пикселя
                if (_zoom >= 4)
                    using (var pen = new Pen(Color.FromArgb(45, 255, 255, 255), 1))
                    {
                        for (int x = 0; x <= pw; x++) g.DrawLine(pen, x * _zoom, 0, x * _zoom, bh);
                        for (int y = 0; y <= ph; y++) g.DrawLine(pen, 0, y * _zoom, bw, y * _zoom);
                    }
                // Жирная сетка по 8 пикселей (слово УКНЦ)
                if (_zoom >= 2)
                    using (var pen = new Pen(Color.FromArgb(110, 80, 160, 255), 1))
                    {
                        for (int x = 0; x <= pw; x += 8) g.DrawLine(pen, x * _zoom, 0, x * _zoom, bh);
                        for (int y = 0; y <= ph; y += 8) g.DrawLine(pen, 0, y * _zoom, bw, y * _zoom);
                    }
            }
            _pic.Image = bmp;
        }

        // ── Миниатюры ────────────────────────────────────────────
        void RefreshThumbs()
        {
            _thumbPanel.Controls.Clear();
            for (int i = 0; i < _sprites.Count; i++) AddThumb(i);
        }

        void RefreshThumb(int idx)
        {
            if (idx < _thumbPanel.Controls.Count)
            {
                var card = _thumbPanel.Controls[idx] as Panel;
                if (card != null) FillThumbCard(card, idx);
            }
        }

        void AddThumb(int idx)
        {
            var card = new Panel
            {
                Width = 110,
                Height = 90,
                Cursor = Cursors.Hand,
                Margin = new Padding(2),
                BackColor = idx == _cur ? C_SEL : C_BG
            };
            FillThumbCard(card, idx);
            int ii = idx;
            card.Click += (s, e) => { _cur = ii; FullRefresh(); };
            foreach (Control c in card.Controls) { int jj = ii; c.Click += (s, e) => { _cur = jj; FullRefresh(); }; }
            _thumbPanel.Controls.Add(card);
        }

        void FillThumbCard(Panel card, int idx)
        {
            card.Controls.Clear();
            card.BackColor = idx == _cur ? C_SEL : C_BG;
            var s = _sprites[idx];
            int pw = s.PixelWidth, ph = s.Height;
            int sc = Math.Max(1, Math.Min(100 / pw, 70 / ph));
            var bmp = new Bitmap(pw * sc, ph * sc);
            using (var g = Graphics.FromImage(bmp))
                for (int y = 0; y < ph; y++)
                    for (int x = 0; x < pw; x++)
                    {
                        var c = PAL[s.PalIdx][s.Pixels[y * pw + x] % 4];
                        using (var br = new SolidBrush(c))
                            g.FillRectangle(br, x * sc, y * sc, sc, sc);
                    }
            var pic = new PictureBox
            {
                Image = bmp,
                SizeMode = PictureBoxSizeMode.AutoSize,
                Location = new Point((110 - pw * sc) / 2, 4)
            };
            var lbl = new Label
            {
                Text = s.Name,
                Width = 110,
                Height = 14,
                Location = new Point(0, 76),
                ForeColor = C_TEXT,
                Font = F_SMALL,
                TextAlign = ContentAlignment.MiddleCenter
            };
            card.Controls.Add(pic); card.Controls.Add(lbl);
        }

        // ── Зум и палитра ────────────────────────────────────────
        void SetZoom(int z)
        {
            _zoom = Math.Max(1, Math.Min(32, z));
            _zoomLbl.Text = _zoom + "×";
            DrawCanvas();
        }

        void UpdatePal()
        {
            int pi = _sprites[_cur].PalIdx;
            for (int i = 0; i < 3; i++)
                _palBtns[i].BackColor = (i == pi) ? C_SEL : C_BG3;
            UpdateSwatches();
        }

        void UpdateSwatches()
        {
            int pi = _sprites[_cur].PalIdx;
            for (int i = 0; i < 4; i++)
            {
                _swatches[i].BackColor = PAL[pi][i];
                _swatches[i].Invalidate();
            }
        }

        // ── Файлы ────────────────────────────────────────────────
        void OpenFile()
        {
            using (var dlg = new OpenFileDialog { Filter = "Спрайты (*.spr)|*.spr|Все файлы|*.*" })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                try
                {
                    var text = File.ReadAllText(dlg.FileName);
                    var blocks = text.Split(new[] { "---\n" }, StringSplitOptions.RemoveEmptyEntries);
                    _sprites.Clear();
                    foreach (var b in blocks) _sprites.Add(Sprite.Deserialize(b));
                    if (_sprites.Count == 0) NewSprite();
                    _cur = 0; FullRefresh();
                }
                catch { MessageBox.Show("Ошибка загрузки", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        void SaveFile()
        {
            using (var dlg = new SaveFileDialog { Filter = "Спрайты (*.spr)|*.spr", FileName = "sprites" })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                var sb = new StringBuilder();
                foreach (var s in _sprites) { sb.Append(s.Serialize()); sb.AppendLine("---"); }
                File.WriteAllText(dlg.FileName, sb.ToString());
            }
        }

        void ImportImage()
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Импортировать изображение как спрайт",
                Filter = "Изображения (*.png;*.bmp;*.gif)|*.png;*.bmp;*.gif|Все файлы|*.*"
            })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                try
                {
                    var orig = new Bitmap(dlg.FileName);
                    int pw = ((orig.Width + 7) / 8) * 8;
                    int ph = Math.Min(orig.Height, 200);
                    int pi = _sprites.Count > 0 ? _sprites[_cur].PalIdx : 0;

                    // Диалог выбора алгоритма
                    var dlgAlg = new Form
                    {
                        Text = "Метод преобразования",
                        Size = new Size(280, 150),
                        FormBorderStyle = FormBorderStyle.FixedDialog,
                        StartPosition = FormStartPosition.CenterParent,
                        MaximizeBox = false,
                        MinimizeBox = false,
                        BackColor = C_BG
                    };
                    var rb1 = new RadioButton
                    {
                        Text = "Ближайший цвет (быстро)",
                        Location = new Point(12, 12),
                        Width = 240,
                        ForeColor = C_TEXT,
                        Checked = false
                    };
                    var rb2 = new RadioButton
                    {
                        Text = "Floyd-Steinberg (качественно)",
                        Location = new Point(12, 36),
                        Width = 240,
                        ForeColor = C_TEXT,
                        Checked = true
                    };
                    var rb3 = new RadioButton
                    {
                        Text = "Ordered 4x4 Bayer (паттерн)",
                        Location = new Point(12, 60),
                        Width = 240,
                        ForeColor = C_TEXT,
                        Checked = false
                    };
                    var ok2 = new Button
                    {
                        Text = "OK",
                        Location = new Point(160, 90),
                        Width = 80,
                        Height = 26,
                        DialogResult = DialogResult.OK,
                        FlatStyle = FlatStyle.Flat,
                        BackColor = C_SEL,
                        ForeColor = Color.White
                    };
                    dlgAlg.Controls.AddRange(new Control[] { rb1, rb2, rb3, ok2 });
                    dlgAlg.AcceptButton = ok2;
                    if (dlgAlg.ShowDialog() != DialogResult.OK) return;

                    int alg = rb1.Checked ? 0 : rb3.Checked ? 2 : 1;
                    var name = Path.GetFileNameWithoutExtension(dlg.FileName);
                    var s = new Sprite(name, pw / 8, ph);
                    s.PalIdx = pi;

                    // Сконвертировать изображение в расширенный буфер float для дитеринга
                    // [y][x] = {R,G,B} как float
                    float[][] rBuf = new float[ph][];
                    float[][] gBuf = new float[ph][];
                    float[][] bBuf = new float[ph][];
                    for (int y = 0; y < ph; y++)
                    {
                        rBuf[y] = new float[pw];
                        gBuf[y] = new float[pw];
                        bBuf[y] = new float[pw];
                        for (int x = 0; x < pw; x++)
                        {
                            Color px = x < orig.Width ? orig.GetPixel(x, y) : Color.Black;
                            rBuf[y][x] = px.R; gBuf[y][x] = px.G; bBuf[y][x] = px.B;
                        }
                    }
                    orig.Dispose();

                    var pal = PAL[pi];

                    if (alg == 0)
                    {
                        // Простой — ближайший цвет
                        for (int y = 0; y < ph; y++)
                            for (int x = 0; x < pw; x++)
                                s.Pixels[y * pw + x] = NearestColorF(rBuf[y][x], gBuf[y][x], bBuf[y][x], pal);
                    }
                    else if (alg == 1)
                    {
                        // Floyd-Steinberg dithering
                        // Ошибка распространяется на соседей:
                        //         [x+1] += err * 7/16
                        // [x-1]   [x]   [x+1] (следующая строка)
                        // [x-1] += err * 3/16, [x] += err * 5/16, [x+1] += err * 1/16
                        for (int y = 0; y < ph; y++)
                            for (int x = 0; x < pw; x++)
                            {
                                int ci = NearestColorF(rBuf[y][x], gBuf[y][x], bBuf[y][x], pal);
                                s.Pixels[y * pw + x] = ci;
                                float er = rBuf[y][x] - pal[ci].R;
                                float eg = gBuf[y][x] - pal[ci].G;
                                float eb = bBuf[y][x] - pal[ci].B;
                                // Распространить ошибку
                                DiffErr(rBuf, gBuf, bBuf, y, x + 1, ph, pw, er * 7 / 16f, eg * 7 / 16f, eb * 7 / 16f);
                                DiffErr(rBuf, gBuf, bBuf, y + 1, x - 1, ph, pw, er * 3 / 16f, eg * 3 / 16f, eb * 3 / 16f);
                                DiffErr(rBuf, gBuf, bBuf, y + 1, x, ph, pw, er * 5 / 16f, eg * 5 / 16f, eb * 5 / 16f);
                                DiffErr(rBuf, gBuf, bBuf, y + 1, x + 1, ph, pw, er * 1 / 16f, eg * 1 / 16f, eb * 1 / 16f);
                            }
                    }
                    else
                    {
                        // Ordered Bayer 4x4 dithering
                        int[,] bayer = {
                            { 0, 8, 2,10}, { 12, 4,14, 6},
                            { 3,11, 1, 9}, { 15, 7,13, 5}
                        };
                        for (int y = 0; y < ph; y++)
                            for (int x = 0; x < pw; x++)
                            {
                                float threshold = (bayer[y % 4, x % 4] / 16f - 0.5f) * 64f;
                                float r = Clamp(rBuf[y][x] + threshold);
                                float g = Clamp(gBuf[y][x] + threshold);
                                float b2 = Clamp(bBuf[y][x] + threshold);
                                s.Pixels[y * pw + x] = NearestColorF(r, g, b2, pal);
                            }
                    }

                    _sprites.Add(s);
                    _cur = _sprites.Count - 1;
                    FullRefresh();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Ошибка импорта:\n" + ex.Message, "Ошибка",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        static void DiffErr(float[][] r, float[][] g, float[][] b,
                            int y, int x, int h, int w,
                            float er, float eg, float eb)
        {
            if (y < 0 || y >= h || x < 0 || x >= w) return;
            r[y][x] = Clamp(r[y][x] + er);
            g[y][x] = Clamp(g[y][x] + eg);
            b[y][x] = Clamp(b[y][x] + eb);
        }

        static float Clamp(float v) => v < 0 ? 0 : v > 255 ? 255 : v;

        static int NearestColorF(float r, float g, float b, Color[] pal)
        {
            int best = 0, bestDist = int.MaxValue;
            for (int i = 0; i < pal.Length; i++)
            {
                float dr = r - pal[i].R, dg = g - pal[i].G, db = b - pal[i].B;
                // Взвешенное расстояние с учётом восприятия (luma)
                int dist = (int)(dr * dr * 0.299f + dg * dg * 0.587f + db * db * 0.114f);
                if (dist < bestDist) { bestDist = dist; best = i; }
            }
            return best;
        }

        int NearestColor(Color c, int palIdx)
        {
            int best = 0, bestDist = int.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                var p = PAL[palIdx][i];
                int dr = c.R - p.R, dg = c.G - p.G, db = c.B - p.B;
                int dist = dr * dr + dg * dg + db * db;
                if (dist < bestDist) { bestDist = dist; best = i; }
            }
            return best;
        }

        void ExportCode()
        {
            // Вставляем только комментарии — реальные данные подставит компилятор
            var sb = new StringBuilder();
            sb.AppendLine("// ── Спрайты (данные подставляются автоматически при компиляции) ──");
            foreach (var s in _sprites)
            {
                sb.AppendLine($"// sprite: {s.Name}  {s.PixelWidth}x{s.Height}");
                sb.AppendLine($"// sprite(x, y, {s.Words}, {s.Height}, {s.Name});");
            }
            var code = sb.ToString();
            if (_insertCode != null) _insertCode(code);
        }

        // Сгенерировать полный код всех спрайтов (для компилятора)
        public string GenerateAllSpritesCode()
        {
            var sb = new StringBuilder();
            foreach (var s in _sprites) sb.AppendLine(s.ExportC());
            return sb.ToString();
        }

        // ── Автосохранение ───────────────────────────────────────
        void ScheduleSave()
        {
            _autoSaveTimer.Stop();
            _autoSaveTimer.Start();
        }

        void AutoSave()
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var s in _sprites) { sb.Append(s.Serialize()); sb.AppendLine("---"); }
                File.WriteAllText(AutoSavePath, sb.ToString(), System.Text.Encoding.UTF8);
            }
            catch { /* тихо игнорировать */ }
        }

        void AutoLoad()
        {
            try
            {
                if (File.Exists(AutoSavePath))
                {
                    var text = File.ReadAllText(AutoSavePath, System.Text.Encoding.UTF8);
                    // Поддержка \r\n (Windows) и \n (Unix)
                    text = text.Replace("\r\n", "\n");
                    var blocks = text.Split(new[] { "---\n" }, StringSplitOptions.RemoveEmptyEntries);
                    var loaded = new List<Sprite>();
                    foreach (var b in blocks)
                    {
                        var sp = Sprite.Deserialize(b);
                        if (sp.Pixels != null && sp.Pixels.Length == sp.PixelWidth * sp.Height)
                            loaded.Add(sp);
                    }
                    if (loaded.Count > 0)
                    {
                        _sprites = loaded;
                        _cur = 0;
                        FullRefresh();
                        return;
                    }
                }
            }
            catch { /* тихо игнорировать */ }
            NewSprite();
        }

        // ── Полное обновление ────────────────────────────────────
        void FullRefresh()
        {
            if (_busy) return;
            _busy = true;
            var s = _sprites[_cur];
            _sprNameLbl.Text = s.Name + "  (двойной клик — переименовать)";
            _selW.SelectedIndex = Math.Min(s.Words - 1, _selW.Items.Count - 1);
            _inpH.Value = s.Height;
            UpdatePal();
            DrawCanvas();
            RefreshThumbs();
            _busy = false;
            ScheduleSave();
        }
    }
}
