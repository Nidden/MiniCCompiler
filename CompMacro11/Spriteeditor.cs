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
        public int PalIdx = 0;      // 0=Набор1, 1=Набор2, 2=Ч/Б (4 цвета), 3=8 цветов (ПП)

        public int PixelWidth => Words * 8;

        public Sprite(string name = "sprite", int words = 1, int height = 16)
        {
            Name = name; Words = words; Height = height;
            Pixels = new int[PixelWidth * height];
        }

        public void Resize(int newWords, int newHeight)
        {
            newHeight = Math.Max(1, Math.Min(264, newHeight));
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

        // Пакует спрайт в список слов: [тип, words, height, данные].
        //   Универсальный формат (ЦП/ПП). 8цв=2 слова/октет, 4цв=1 слово/октет.
        public List<int> ToWords()
        {
            int pw = PixelWidth;
            var w = new List<int>();
            w.Add(PalIdx == 3 ? 1 : 0);     // тип
            w.Add(Words);                    // ширина в словах
            w.Add(Height);                   // высота
            if (PalIdx == 3)
            {
                for (int y = 0; y < Height; y++)
                    for (int wx = 0; wx < Words; wx++)
                    {
                        int plane0 = 0, plane1 = 0, plane2 = 0;
                        for (int b = 0; b < 8; b++)
                        {
                            int c = Pixels[y * pw + wx * 8 + b] & 7;
                            if ((c & 1) != 0) plane0 |= (1 << b);
                            if ((c & 2) != 0) plane1 |= (1 << b);
                            if ((c & 4) != 0) plane2 |= (1 << b);
                        }
                        w.Add(plane0);
                        // Порядок байтов подтверждён тестом на реальной машине
                        // (palette_test.mc): 177014 отдаёт МЛАДШИЙ байт под план2
                        // (красный), СТАРШИЙ — под план1 (зелёный). Раньше здесь
                        // было наоборот — красный и зелёный менялись местами.
                        w.Add(plane2 | (plane1 << 8));
                    }
            }
            else
            {
                for (int y = 0; y < Height; y++)
                    for (int wx = 0; wx < Words; wx++)
                    {
                        int plane0 = 0, plane1 = 0;
                        for (int b = 0; b < 8; b++)
                        {
                            int c = Pixels[y * pw + wx * 8 + b] & 3;
                            if ((c & 1) != 0) plane0 |= (1 << b);
                            if ((c & 2) != 0) plane1 |= (1 << b);
                        }
                        w.Add(plane0 | (plane1 << 8));
                    }
            }
            return w;
        }

        // Пакует спрайт в байты (little-endian) для записи в файл на диск.
        public byte[] ToBytes()
        {
            var w = ToWords();
            var b = new byte[w.Count * 2];
            for (int i = 0; i < w.Count; i++)
            {
                b[i * 2] = (byte)(w[i] & 0xFF);
                b[i * 2 + 1] = (byte)((w[i] >> 8) & 0xFF);
            }
            return b;
        }

        public string ExportC()
        {
            int pw = PixelWidth;
            var full = ToWords();
            var wordList = full.GetRange(3, full.Count - 3);   // данные без заголовка

            var sb = new StringBuilder();
            sb.AppendLine($"// {Name}  {pw}x{Height}" + (PalIdx == 3 ? "  (8 цветов, ПП)" : ""));
            sb.AppendLine($"// формат: [тип, words, height, данные] — универсальный (ЦП/ПП)");
            sb.AppendLine($"int {Name}[{wordList.Count + 3}] = {{");
            sb.AppendLine($"    {(PalIdx == 3 ? 1 : 0)}, {Words}, {Height},   // заголовок: тип({(PalIdx == 3 ? "8цв" : "4цв")}), ширина в словах, высота");
            for (int i = 0; i < wordList.Count; i += 8)
            {
                sb.Append("    ");
                int end = Math.Min(i + 8, wordList.Count);
                for (int j = i; j < end; j++)
                    sb.Append(wordList[j] + (j < wordList.Count - 1 ? ", " : ""));
                sb.AppendLine();
            }
            sb.AppendLine("};");
            if (PalIdx == 3)
                sb.AppendLine($"// pp_spr(x, y, {Name});");
            else
                sb.AppendLine($"// spr(x, y, {Name});");
            return sb.ToString();
        }
    }

    public class SpriteEditor : Form
    {
        static readonly Color[][] PAL = {
            new[]{ Color.Black, Color.Red,     Color.Green,  Color.Yellow },  // палитра 1 (0=чёрный,1=красный,2=зелёный,3=жёлтый)
            new[]{ Color.Blue,  Color.Magenta, Color.Cyan,   Color.White  },  // палитра 2 (0=синий,1=пурпурный,2=голубой,3=белый)
            new[]{ Color.Black, Color.FromArgb(85,85,85), Color.FromArgb(170,170,170), Color.White } // Ч/Б
        };
        static readonly string[] PAL_NAMES = { "Набор 1", "Набор 2", "Ч/Б", "8 цветов" };

        // Цвет пикселя: набор 3 → 8-цветная PAL8, наборы 0-2 → 4-цветная PAL.
        static Color PixColor(Sprite s, int idx)
        {
            if (s.PalIdx == 3) return PAL8[idx & 7];
            return PAL[s.PalIdx][idx & 3];
        }
        // Число цветов: набор 3 → 8, остальные → 4.
        static int NumColors(Sprite s) => s.PalIdx == 3 ? 8 : 4;

        // 8-цветная палитра ПП — соответствует РЕАЛЬНОЙ палитре железа УКНЦ.
        // Биты планов: план0(1)=синий, план1(2)=зелёный, план2(4)=красный.
        // (проверено на эмуляторе: редактор показывает те же цвета, что и ПП)
        static readonly Color[] PAL8 = {
            Color.Black,                    // 0 = 000
            Color.Blue,                     // 1 = 001 (план0=синий)
            Color.Green,                    // 2 = 010 (план1=зелёный)
            Color.Cyan,                     // 3 = 011 (синий+зелёный)
            Color.Red,                      // 4 = 100 (план2=красный)
            Color.Magenta,                  // 5 = 101 (синий+красный)
            Color.Yellow,                   // 6 = 110 (зелёный+красный)
            Color.White                     // 7 = 111
        };

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
        // Инструменты рисования
        enum Tool { Pencil, Line, Rect, RectFill, Ellipse, Fill, Picker }
        Tool _tool = Tool.Pencil;
        int _startX = -1, _startY = -1;     // начало фигуры (drag)
        int _curX = -1, _curY = -1;         // текущая позиция (предпросмотр)
        bool _mirror = false;               // изюм: зеркальное рисование (симметрия H)
        bool _onion = false;                // изюм: луковичная кожа (предыдущий спрайт)
        Button[] _toolBtns;
        Button _mirrorBtn, _onionBtn;
        Panel _funcBar;                 // полоса функций выбранной категории
        Button[] _catBtns;              // кнопки категорий (левый столбец)
        int _curCat = 0;
        // Категории тулбара (вертикальный столбец слева)
        static readonly string[] CAT_NAMES = { "Файл", "Инструмент", "Помощники", "Преобразования", "Сдвиг", "Экспорт" };
        static readonly string[] CAT_ICONS = { "📁", "✏", "✨", "⟳", "✛", "→" };

        Action<string> _insertCode;

        FlowLayoutPanel _thumbPanel;
        PictureBox _pic;
        Label _zoomLbl;
        Label _sprNameLbl;
        ComboBox _selW;
        NumericUpDown _inpH;
        Button[] _palBtns = new Button[4];
        Panel[] _swatches = new Panel[8];

        // Автосохранение
        // Путь к файлу спрайтов — задаётся при открытии редактора
        private string _spritesPath = null;
        public string SpritesPath
        {
            get
            {
                return _spritesPath ?? System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location),
                "sprites_autosave.spr");
            }
            set
            {
                _spritesPath = value;
                // Перезагружаем спрайты из нового проекта
                _sprites.Clear();
                AutoLoad();
                _cur = 0;
                if (_sprites.Count == 0) NewSprite();
                FullRefresh();
            }
        }
        // Для совместимости
        public static string AutoSavePath => System.IO.Path.Combine(
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

            // При закрытии — гарантированно сохранить (вдруг таймер не успел)
            FormClosing += (s, e) => { _autoSaveTimer.Stop(); AutoSave(); };

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

            // ── Полоса функций выбранной категории (Top) ──
            _funcBar = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = C_BG2 };
            _toolBtns = new Button[7];

            // ── Узкий столбец категорий (Left) ──
            var catBar = new Panel { Dock = DockStyle.Left, Width = 52, BackColor = C_BG };
            _catBtns = new Button[CAT_NAMES.Length];
            for (int i = 0; i < CAT_NAMES.Length; i++)
            {
                int ii = i;
                _catBtns[i] = new Button
                {
                    Text = CAT_ICONS[i],
                    Location = new Point(4, 6 + i * 50),
                    Width = 44,
                    Height = 44,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = i == 0 ? C_SEL : C_BG3,
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 14f),
                    Cursor = Cursors.Hand
                };
                _catBtns[i].FlatAppearance.BorderSize = 0;
                _catBtns[i].Click += (s, e) => SelectCategory(ii);
                _tt.SetToolTip(_catBtns[i], CAT_NAMES[i]);
                catBar.Controls.Add(_catBtns[i]);
            }


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
            for (int i = 1; i <= 40; i++) _selW.Items.Add(i * 8 + " пикс");
            _selW.SelectedIndex = 0;
            _selW.SelectedIndexChanged += (s, e) => { if (!_busy) ApplySize(); };
            bot.Controls.Add(_selW);

            MiniLabel(bot, "H:", 108, 26);
            _inpH = new NumericUpDown
            {
                Location = new Point(122, 22),
                Width = 52,
                Minimum = 1,
                Maximum = 264,
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
            for (int i = 0; i < 4; i++)
            {
                int ii = i;
                _palBtns[i] = new Button
                {
                    Text = PAL_NAMES[i],
                    Location = new Point(342 + i * 60, 22),
                    Width = 56,
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
                    UpdatePal(); UpdateSwatches(); DrawCanvas(); RefreshThumbs();
                };
                bot.Controls.Add(_palBtns[i]);
            }

            // Свотчи (до 8 — для режима ПП; в режиме ЦП видны первые 4)
            VSep(bot, 584, 4);
            MiniLabel(bot, "Цвет:", 592, 6);
            for (int i = 0; i < 8; i++)
            {
                int ii = i;
                _swatches[i] = new Panel
                {
                    Location = new Point(592 + i * 30, 22),
                    Width = 26,
                    Height = 26,
                    Cursor = Cursors.Hand
                };
                _swatches[i].Click += (s, e) => { _colorIdx = ii; UpdateSwatches(); };
                _swatches[i].Paint += (s, e) => {
                    if (ii == _colorIdx)
                        e.Graphics.DrawRectangle(new Pen(Color.White, 2), 1, 1, 22, 22);
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
            _pic.MouseDown += (s, e) => OnCanvasDown(e.X, e.Y);
            _pic.MouseMove += (s, e) => OnCanvasMove(e.X, e.Y);
            _pic.MouseUp += (s, e) => OnCanvasUp(e.X, e.Y);
            wrap.Controls.Add(_pic);

            // ── Порядок добавления важен для WinForms Dock ────────
            // Fill последний; Left-панели в порядке от центра к краю;
            // Top/Bottom первыми по доку.
            Controls.Add(wrap);       // Fill — самый последний
            Controls.Add(leftPanel);  // Left — миниатюры (ближе к центру)
            Controls.Add(catBar);     // Left — столбец категорий (крайний слева)
            Controls.Add(bot);        // Bottom
            Controls.Add(_funcBar);   // Top — функции выбранной категории

            SelectCategory(0);        // показать первую категорию
        }

        // ── Хелперы UI ───────────────────────────────────────────
        ToolTip _tt = new ToolTip();
        Button TBtn(Panel p, string t, ref int x, Color bg, Action click, int w = 82, string tip = null)
        {
            var b = new Button
            {
                Text = t,
                Location = new Point(x, 22),
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
            if (tip != null) _tt.SetToolTip(b, tip);
            p.Controls.Add(b); x += w + 4;
            return b;
        }
        void TBtnSep(Panel p, ref int x)
        {
            p.Controls.Add(new Panel { Location = new Point(x, 24), Width = 1, Height = 26, BackColor = C_GRAY });
            x += 9;
        }
        // Подпись группы тулбара (над кнопками). Запоминает x начала группы.
        void GroupLabel(Panel p, string title, int x)
        {
            p.Controls.Add(new Label
            {
                Text = title,
                Location = new Point(x, 5),
                AutoSize = true,
                ForeColor = C_GRAY,
                Font = F_SMALL,
                BackColor = Color.Transparent
            });
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

        // Стек отмены: снимки Pixels текущего спрайта перед каждой операцией.
        readonly Stack<int[]> _undo = new Stack<int[]>();

        // Сохранить снимок текущего спрайта в стек отмены (вызывать ДО изменения).
        void PushUndo()
        {
            if (_sprites.Count == 0) return;
            var s = _sprites[_cur];
            if (s.Pixels == null) return;
            _undo.Push((int[])s.Pixels.Clone());
            if (_undo.Count > 50)
            {            // ограничить глубину
                var tmp = _undo.ToArray();
                _undo.Clear();
                for (int i = Math.Min(49, tmp.Length - 1); i >= 0; i--) _undo.Push(tmp[i]);
            }
        }

        void Undo()
        {
            if (_undo.Count == 0 || _sprites.Count == 0) return;
            var prev = _undo.Pop();
            var s = _sprites[_cur];
            // вернуть, только если размер совпадает (size-операции не отменяем здесь)
            if (prev.Length == s.Pixels.Length)
            {
                s.Pixels = prev;
                FullRefresh();
            }
        }

        // Стереть весь спрайт (цвет 0).
        void EditClear()
        {
            if (_sprites.Count == 0) return;
            PushUndo();
            var s = _sprites[_cur];
            for (int i = 0; i < s.Pixels.Length; i++) s.Pixels[i] = 0;
            FullRefresh();
        }

        // Залить весь спрайт текущим цветом.
        void EditFill()
        {
            if (_sprites.Count == 0) return;
            PushUndo();
            var s = _sprites[_cur];
            for (int i = 0; i < s.Pixels.Length; i++) s.Pixels[i] = _colorIdx;
            FullRefresh();
        }

        // Инверсия: цвет 0↔максимальный (для 4 цветов: 0↔3, 1↔2).
        void EditInvert()
        {
            if (_sprites.Count == 0) return;
            PushUndo();
            var s = _sprites[_cur];
            int max = NumColors(s) - 1;
            for (int i = 0; i < s.Pixels.Length; i++) s.Pixels[i] = max - s.Pixels[i];
            FullRefresh();
        }

        // ── Выбор категории (левый столбец) ──
        void SelectCategory(int cat)
        {
            _curCat = cat;
            for (int i = 0; i < _catBtns.Length; i++)
                _catBtns[i].BackColor = (i == cat) ? C_SEL : C_BG3;
            BuildFuncBar();
        }

        // Построить полосу функций для текущей категории.
        void BuildFuncBar()
        {
            _funcBar.Controls.Clear();
            int x = 8;
            switch (_curCat)
            {
                case 0: // Файл
                    FBtn("+ Новый", ref x, C_BG3, () => NewSprite());
                    FBtn("✎ Имя", ref x, C_BG3, () => RenameCurrent());
                    FBtn("📂 Открыть", ref x, C_BG3, () => OpenFile());
                    FBtn("🖼 Импорт", ref x, C_BG3, () => ImportImage());
                    FBtn("💾 Сохранить", ref x, C_BG3, () => SaveFile());
                    FBtn("✕ Удалить", ref x, Color.FromArgb(70, 25, 25), () => DeleteSprite());
                    break;
                case 1: // Инструмент
                    _toolBtns[0] = FBtn("✏ Карандаш", ref x, C_BG3, () => SetTool(Tool.Pencil), 96);
                    _toolBtns[1] = FBtn("╱ Линия", ref x, C_BG3, () => SetTool(Tool.Line), 76);
                    _toolBtns[2] = FBtn("▭ Прямоуг", ref x, C_BG3, () => SetTool(Tool.Rect), 86);
                    _toolBtns[3] = FBtn("▬ Залитый", ref x, C_BG3, () => SetTool(Tool.RectFill), 86);
                    _toolBtns[4] = FBtn("◯ Эллипс", ref x, C_BG3, () => SetTool(Tool.Ellipse), 84);
                    _toolBtns[5] = FBtn("▨ Заливка", ref x, C_BG3, () => SetTool(Tool.Fill), 86);
                    _toolBtns[6] = FBtn("✒ Пипетка", ref x, C_BG3, () => SetTool(Tool.Picker), 86);
                    HighlightTool();
                    break;
                case 2: // Помощники
                    _mirrorBtn = FBtn("⊥ Зеркало", ref x, _mirror ? C_SEL : C_BG3, () => ToggleMirror(), 96);
                    _onionBtn = FBtn("◓ Калька", ref x, _onion ? C_SEL : C_BG3, () => ToggleOnion(), 92);
                    break;
                case 3: // Преобразования
                    FBtn("↶ Отмена", ref x, C_BG3, () => Undo(), 86);
                    FBtn("🗑 Очистить", ref x, C_BG3, () => EditClear(), 96);
                    FBtn("▦ Залить", ref x, C_BG3, () => EditFill(), 84);
                    FBtn("◐ Инверт", ref x, C_BG3, () => EditInvert(), 84);
                    FBtn("⟳ Поворот", ref x, C_BG3, () => EditRotate(), 92);
                    FBtn("▣ Контур", ref x, C_BG3, () => EditOutline(), 84);
                    FBtn("⇆ Симметрия", ref x, C_BG3, () => EditSymmetry(), 104);
                    FBtn("↔ ФлипГ", ref x, C_BG3, () => EditFlipH(), 78);
                    FBtn("↕ ФлипВ", ref x, C_BG3, () => EditFlipV(), 78);
                    break;
                case 4: // Сдвиг
                    FBtn("← Влево", ref x, C_BG3, () => EditShift(-1, 0), 78);
                    FBtn("→ Вправо", ref x, C_BG3, () => EditShift(1, 0), 84);
                    FBtn("↑ Вверх", ref x, C_BG3, () => EditShift(0, -1), 80);
                    FBtn("↓ Вниз", ref x, C_BG3, () => EditShift(0, 1), 74);
                    break;
                case 5: // Экспорт
                    FBtn("→ В код", ref x, Color.FromArgb(0, 80, 50), () => ExportCode(), 100);
                    FBtn("💾 На диск", ref x, Color.FromArgb(0, 60, 90), () => ExportToDisk(), 110);
                    break;
            }
        }

        // Кнопка в полосе функций (выровнена по вертикали в _funcBar).
        Button FBtn(string t, ref int x, Color bg, Action click, int w = 82)
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
            _funcBar.Controls.Add(b); x += w + 4;
            return b;
        }

        // Подсветить активный инструмент (если категория Инструмент открыта).
        void HighlightTool()
        {
            if (_curCat != 1 || _toolBtns == null) return;
            for (int i = 0; i < _toolBtns.Length; i++)
                if (_toolBtns[i] != null)
                    _toolBtns[i].BackColor = ((int)_tool == i) ? C_SEL : C_BG3;
        }

        // ── Выбор инструмента ──
        void SetTool(Tool t)
        {
            _tool = t;
            HighlightTool();
        }

        // ── Изюм: зеркальное рисование ──
        void ToggleMirror()
        {
            _mirror = !_mirror;
            if (_mirrorBtn != null) _mirrorBtn.BackColor = _mirror ? C_SEL : C_BG3;
            DrawCanvas();
        }

        // ── Изюм: луковичная кожа ──
        void ToggleOnion()
        {
            _onion = !_onion;
            if (_onionBtn != null) _onionBtn.BackColor = _onion ? C_SEL : C_BG3;
            DrawCanvas();
        }

        // Поворот на 90° по часовой (квадратная область; для прямоуг. — обрезка).
        void EditRotate()
        {
            if (_sprites.Count == 0) return;
            PushUndo();
            var s = _sprites[_cur];
            int w = s.PixelWidth, h = s.Height;
            var np = new int[w * h];
            // поворот по часовой в исходную решётку (центр сохраняется, края обрезаются)
            int cx = w / 2, cy = h / 2;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    // (x,y) ← из исходной (sx,sy): поворот на -90
                    int rx = x - cx, ry = y - cy;
                    int sx = cx + ry, sy = cy - rx;
                    if (sx >= 0 && sy >= 0 && sx < w && sy < h)
                        np[y * w + x] = s.Pixels[sy * w + sx];
                }
            s.Pixels = np;
            FullRefresh();
        }

        // Контур: обвести непустые пиксели текущим цветом (по пустым соседям).
        void EditOutline()
        {
            if (_sprites.Count == 0) return;
            PushUndo();
            var s = _sprites[_cur];
            int w = s.PixelWidth, h = s.Height;
            var add = new List<int>();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if (s.Pixels[y * w + x] != 0) continue;     // только пустые
                    bool near = false;
                    for (int dy = -1; dy <= 1 && !near; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            if (s.Pixels[ny * w + nx] != 0 && (dx == 0 || dy == 0)) { near = true; break; }
                        }
                    if (near) add.Add(y * w + x);
                }
            foreach (int i in add) s.Pixels[i] = _colorIdx;
            FullRefresh();
        }

        // Симметрия: отзеркалить ЛЕВУЮ половину в правую (мгновенно симметричный спрайт).
        void EditSymmetry()
        {
            if (_sprites.Count == 0) return;
            PushUndo();
            var s = _sprites[_cur];
            int w = s.PixelWidth, h = s.Height;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w / 2; x++)
                    s.Pixels[y * w + (w - 1 - x)] = s.Pixels[y * w + x];
            FullRefresh();
        }

        // Отразить по горизонтали (зеркало лево↔право).
        void EditFlipH()
        {
            if (_sprites.Count == 0) return;
            PushUndo();
            var s = _sprites[_cur];
            int w = s.PixelWidth, h = s.Height;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w / 2; x++)
                {
                    int a = y * w + x, b = y * w + (w - 1 - x);
                    (s.Pixels[a], s.Pixels[b]) = (s.Pixels[b], s.Pixels[a]);
                }
            FullRefresh();
        }

        // Отразить по вертикали (зеркало верх↔низ).
        void EditFlipV()
        {
            if (_sprites.Count == 0) return;
            PushUndo();
            var s = _sprites[_cur];
            int w = s.PixelWidth, h = s.Height;
            for (int y = 0; y < h / 2; y++)
                for (int x = 0; x < w; x++)
                {
                    int a = y * w + x, b = (h - 1 - y) * w + x;
                    (s.Pixels[a], s.Pixels[b]) = (s.Pixels[b], s.Pixels[a]);
                }
            FullRefresh();
        }

        // Сдвиг рисунка на (dx, dy) с заворотом по краям (удобно центрировать).
        void EditShift(int dx, int dy)
        {
            if (_sprites.Count == 0) return;
            PushUndo();
            var s = _sprites[_cur];
            int w = s.PixelWidth, h = s.Height;
            var np = new int[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int nx = ((x + dx) % w + w) % w;
                    int ny = ((y + dy) % h + h) % h;
                    np[ny * w + nx] = s.Pixels[y * w + x];
                }
            s.Pixels = np;
            FullRefresh();
        }

        // Горячая клавиша Ctrl+Z — отмена последнего действия.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Z))
            {
                Undo();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

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
            SetPix(s, px, py, _colorIdx);
            DrawCanvas(); RefreshThumb(_cur);
            ScheduleSave();
        }

        // Поставить пиксель + зеркальное дублирование, если включён _mirror.
        void SetPix(Sprite s, int px, int py, int col)
        {
            int pw = s.PixelWidth;
            if (px < 0 || py < 0 || px >= pw || py >= s.Height) return;
            s.Pixels[py * pw + px] = col;
            if (_mirror)
            {
                int mx2 = pw - 1 - px;
                if (mx2 >= 0 && mx2 < pw) s.Pixels[py * pw + mx2] = col;
            }
        }

        // ── Обработка холста по инструментам ─────────────────────
        void OnCanvasDown(int mx, int my)
        {
            var s = _sprites[_cur];
            int px = mx / _zoom, py = my / _zoom;
            if (px < 0 || py < 0 || px >= s.PixelWidth || py >= s.Height) return;

            if (_tool == Tool.Picker)
            {
                _colorIdx = s.Pixels[py * s.PixelWidth + px] & (NumColors(s) - 1);
                UpdateSwatches();
                return;
            }
            if (_tool == Tool.Fill)
            {
                PushUndo();
                FloodFill(s, px, py, _colorIdx);
                DrawCanvas(); RefreshThumb(_cur); ScheduleSave();
                return;
            }
            PushUndo();
            _drawing = true;
            _startX = px; _startY = py; _curX = px; _curY = py;
            if (_tool == Tool.Pencil)
            {
                SetPix(s, px, py, _colorIdx);
                DrawCanvas(); RefreshThumb(_cur);
            }
            else DrawCanvas(); // предпросмотр фигуры
        }

        void OnCanvasMove(int mx, int my)
        {
            if (!_drawing) return;
            var s = _sprites[_cur];
            int px = mx / _zoom, py = my / _zoom;
            _curX = px; _curY = py;
            if (_tool == Tool.Pencil)
            {
                SetPix(s, px, py, _colorIdx);
                DrawCanvas(); RefreshThumb(_cur);
            }
            else DrawCanvas(); // перерисовать с предпросмотром
        }

        void OnCanvasUp(int mx, int my)
        {
            if (!_drawing) return;
            _drawing = false;
            var s = _sprites[_cur];
            int px = mx / _zoom, py = my / _zoom;
            _curX = px; _curY = py;
            // зафиксировать фигуру
            switch (_tool)
            {
                case Tool.Line: StampLine(s, _startX, _startY, px, py, _colorIdx); break;
                case Tool.Rect: StampRect(s, _startX, _startY, px, py, _colorIdx, false); break;
                case Tool.RectFill: StampRect(s, _startX, _startY, px, py, _colorIdx, true); break;
                case Tool.Ellipse: StampEllipse(s, _startX, _startY, px, py, _colorIdx); break;
            }
            _startX = _startY = _curX = _curY = -1;
            DrawCanvas(); RefreshThumb(_cur); ScheduleSave();
        }

        // ── Растеризация фигур ───────────────────────────────────
        void StampLine(Sprite s, int x0, int y0, int x1, int y1, int col)
        {
            int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;
            while (true)
            {
                SetPix(s, x0, y0, col);
                if (x0 == x1 && y0 == y1) break;
                int e2 = err * 2;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx) { err += dx; y0 += sy; }
            }
        }

        void StampRect(Sprite s, int x0, int y0, int x1, int y1, int col, bool fill)
        {
            int xa = Math.Min(x0, x1), xb = Math.Max(x0, x1);
            int ya = Math.Min(y0, y1), yb = Math.Max(y0, y1);
            if (fill)
            {
                for (int y = ya; y <= yb; y++)
                    for (int x = xa; x <= xb; x++) SetPix(s, x, y, col);
            }
            else
            {
                for (int x = xa; x <= xb; x++) { SetPix(s, x, ya, col); SetPix(s, x, yb, col); }
                for (int y = ya; y <= yb; y++) { SetPix(s, xa, y, col); SetPix(s, xb, y, col); }
            }
        }

        void StampEllipse(Sprite s, int x0, int y0, int x1, int y1, int col)
        {
            // эллипс по двум углам (midpoint algorithm)
            int xa = Math.Min(x0, x1), xb = Math.Max(x0, x1);
            int ya = Math.Min(y0, y1), yb = Math.Max(y0, y1);
            int a = (xb - xa) / 2, b = (yb - ya) / 2;
            int cx = xa + a, cy = ya + b;
            if (a == 0 || b == 0) { StampLine(s, xa, ya, xb, yb, col); return; }
            long a2 = (long)a * a, b2 = (long)b * b;
            long x = 0, y = b;
            long sigma = 2 * b2 + a2 * (1 - 2 * b);
            while (b2 * x <= a2 * y)
            {
                SetPix(s, (int)(cx + x), (int)(cy + y), col);
                SetPix(s, (int)(cx - x), (int)(cy + y), col);
                SetPix(s, (int)(cx + x), (int)(cy - y), col);
                SetPix(s, (int)(cx - x), (int)(cy - y), col);
                if (sigma >= 0) { sigma += 4 * a2 * (1 - y); y--; }
                sigma += b2 * (4 * x + 6);
                x++;
            }
            x = a; y = 0;
            sigma = 2 * a2 + b2 * (1 - 2 * a);
            while (a2 * y <= b2 * x)
            {
                SetPix(s, (int)(cx + x), (int)(cy + y), col);
                SetPix(s, (int)(cx - x), (int)(cy + y), col);
                SetPix(s, (int)(cx + x), (int)(cy - y), col);
                SetPix(s, (int)(cx - x), (int)(cy - y), col);
                if (sigma >= 0) { sigma += 4 * b2 * (1 - x); x--; }
                sigma += a2 * (4 * y + 6);
                y++;
            }
        }

        // Заливка связной области (flood fill, 4-связность)
        void FloodFill(Sprite s, int px, int py, int col)
        {
            int pw = s.PixelWidth, ph = s.Height;
            int target = s.Pixels[py * pw + px];
            if (target == col) return;
            var stack = new Stack<Point>();
            stack.Push(new Point(px, py));
            while (stack.Count > 0)
            {
                var p = stack.Pop();
                if (p.X < 0 || p.Y < 0 || p.X >= pw || p.Y >= ph) continue;
                if (s.Pixels[p.Y * pw + p.X] != target) continue;
                s.Pixels[p.Y * pw + p.X] = col;
                stack.Push(new Point(p.X + 1, p.Y));
                stack.Push(new Point(p.X - 1, p.Y));
                stack.Push(new Point(p.X, p.Y + 1));
                stack.Push(new Point(p.X, p.Y - 1));
            }
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
                // Изюм: луковичная кожа — предыдущий спрайт полупрозрачно под текущим
                if (_onion && _cur > 0)
                {
                    var prev = _sprites[_cur - 1];
                    int ppw = prev.PixelWidth;
                    for (int y = 0; y < prev.Height && y < ph; y++)
                        for (int x = 0; x < ppw && x < pw; x++)
                        {
                            int ci = prev.Pixels[y * ppw + x];
                            if (ci == 0) continue;
                            var bc = PixColor(prev, ci);
                            using (var br = new SolidBrush(Color.FromArgb(70, bc)))
                                g.FillRectangle(br, x * _zoom, y * _zoom, _zoom, _zoom);
                        }
                }
                for (int y = 0; y < ph; y++)
                    for (int x = 0; x < pw; x++)
                    {
                        var c = PixColor(s, s.Pixels[y * pw + x]);
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
                // Ось зеркала (изюм: зеркальное рисование)
                if (_mirror)
                    using (var pen = new Pen(Color.FromArgb(160, 255, 90, 90), 1))
                    {
                        float mxp = (pw / 2f) * _zoom;
                        g.DrawLine(pen, mxp, 0, mxp, bh);
                    }
                // Предпросмотр фигуры при перетаскивании
                if (_drawing && _tool != Tool.Pencil && _startX >= 0 && _curX >= 0)
                {
                    var prev = new bool[pw * ph];
                    var tmp = new Sprite("", s.Words, s.Height);
                    Array.Copy(s.Pixels, tmp.Pixels, s.Pixels.Length);
                    bool om = _mirror;
                    switch (_tool)
                    {
                        case Tool.Line: StampLine(tmp, _startX, _startY, _curX, _curY, _colorIdx); break;
                        case Tool.Rect: StampRect(tmp, _startX, _startY, _curX, _curY, _colorIdx, false); break;
                        case Tool.RectFill: StampRect(tmp, _startX, _startY, _curX, _curY, _colorIdx, true); break;
                        case Tool.Ellipse: StampEllipse(tmp, _startX, _startY, _curX, _curY, _colorIdx); break;
                    }
                    for (int y = 0; y < ph; y++)
                        for (int x = 0; x < pw; x++)
                            if (tmp.Pixels[y * pw + x] != s.Pixels[y * pw + x])
                                using (var br = new SolidBrush(Color.FromArgb(180, PixColor(s, _colorIdx))))
                                    g.FillRectangle(br, x * _zoom, y * _zoom, _zoom, _zoom);
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
                        var c = PixColor(s, s.Pixels[y * pw + x]);
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
            for (int i = 0; i < 4; i++)
                _palBtns[i].BackColor = (i == pi) ? C_SEL : C_BG3;
            UpdateSwatches();
        }

        void UpdateSwatches()
        {
            var s = _sprites[_cur];
            int n = NumColors(s);                  // 8 цветов (палитра ПП)
            for (int i = 0; i < 8; i++)
            {
                bool vis = i < n;
                _swatches[i].Visible = vis;
                if (vis) _swatches[i].BackColor = PixColor(s, i);
                _swatches[i].Invalidate();
            }
            // если выбранный цвет вне диапазона режима — сбросить на 0
            if (_colorIdx >= n) { _colorIdx = 0; }
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
            if (_spritesPath != null)
            {
                // Сохраняем в проект
                var sb = new StringBuilder();
                foreach (var s in _sprites) { sb.Append(s.Serialize()); sb.AppendLine("---"); }
                File.WriteAllText(_spritesPath, sb.ToString(), System.Text.Encoding.UTF8);
                return;
            }
            // Нет проекта — диалог
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
                    int pw = Math.Min(((orig.Width + 7) / 8) * 8, 320);   // до 320 (40 слов)
                    int ph = Math.Min(orig.Height, 264);                  // до 264 строк
                    int pi = _sprites.Count > 0 ? _sprites[_cur].PalIdx : 0;
                    // pi==3 → импорт в 8 цветов (PAL8), иначе в 4 цвета выбранного набора

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

                    var pal = (pi == 3) ? PAL8 : PAL[pi];

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
                sb.AppendLine($"// sprite(x, y, {s.PixelWidth}, {s.Height}, {s.Name});");
            }
            var code = sb.ToString();
            if (_insertCode != null) _insertCode(code);
        }

        // ── Записать текущий спрайт как файл в образ диска (.dsk) ──
        void ExportToDisk()
        {
            if (_sprites.Count == 0) return;
            var spr = _sprites[_cur];
            var bytes = spr.ToBytes();

            // 1. выбрать образ
            string dskPath = LastDiskPath();
            if (dskPath == null || !File.Exists(dskPath))
            {
                using (var dlg = new OpenFileDialog { Filter = "Образы (*.dsk)|*.dsk|Все (*.*)|*.*", Title = "Выберите образ диска" })
                {
                    if (dlg.ShowDialog() != DialogResult.OK) return;
                    dskPath = dlg.FileName;
                }
            }

            // 2. имя файла RT-11 (по имени спрайта, до 6 символов + .SPR)
            string baseName = new string(spr.Name.ToUpper().ToCharArray());
            baseName = System.Text.RegularExpressions.Regex.Replace(baseName, "[^A-Z0-9]", "");
            if (baseName.Length == 0) baseName = "SPRITE";
            if (baseName.Length > 6) baseName = baseName.Substring(0, 6);
            string rtName = baseName + ".SPR";

            // спросить/подтвердить имя
            string entered = AskName(
                "Имя файла в образе (формат RT-11, до 6 символов + .SPR):", rtName);
            if (string.IsNullOrWhiteSpace(entered)) return;
            rtName = entered.ToUpper();

            // 3. запись через DskImage
            try
            {
                var img = new DskImage(dskPath);
                if (!img.AddFile(rtName, bytes))
                {
                    MessageBox.Show($"Недостаточно места для {rtName} " +
                        $"({(bytes.Length + 511) / 512} блоков). Свободно: {img.FreeBlocks()} блоков.",
                        "Нет места", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                SaveLastDiskPath(dskPath);
                MessageBox.Show($"Спрайт {spr.Name} записан в {Path.GetFileName(dskPath)} " +
                    $"как {rtName} ({bytes.Length} байт).\n\n" +
                    $"В программе: {(spr.PalIdx == 3 ? "pp_spr" : "spr")}(x, y, buf); " +
                    $"после fload(\"{rtName}\", buf, {(bytes.Length + 1) / 2 + 1});",
                    "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка записи в образ:\n" + ex.Message,
                    "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Простой модальный ввод строки (без VisualBasic-зависимости).
        static string AskName(string prompt, string def)
        {
            using (var f = new Form
            {
                Text = "Запись на диск",
                Width = 420,
                Height = 160,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false
            })
            {
                var lbl = new Label { Left = 12, Top = 12, Width = 390, Height = 34, Text = prompt };
                var txt = new TextBox { Left = 12, Top = 50, Width = 390, Text = def };
                var ok = new Button { Text = "OK", Left = 226, Top = 82, Width = 80, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Отмена", Left = 314, Top = 82, Width = 80, DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { lbl, txt, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                return f.ShowDialog() == DialogResult.OK ? txt.Text : null;
            }
        }

        // Путь к последнему образу — общий с коммандером (last_disk.txt рядом с exe)
        static string LastDiskFileMarker => Path.Combine(
            Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
            "last_disk.txt");
        static string LastDiskPath()
        {
            try { if (File.Exists(LastDiskFileMarker)) return File.ReadAllText(LastDiskFileMarker).Trim(); }
            catch { }
            return null;
        }
        static void SaveLastDiskPath(string p)
        {
            try { File.WriteAllText(LastDiskFileMarker, p); } catch { }
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

        // Принудительное сохранение без ожидания таймера — вызывается
        // снаружи (Form1.cs), например перед закрытием проекта или сменой
        // файла спрайтов, чтобы не потерять несохранённые изменения.
        public void SaveNow()
        {
            _autoSaveTimer.Stop();
            AutoSave();
        }

        void AutoSave()
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var s in _sprites) { sb.Append(s.Serialize()); sb.AppendLine("---"); }
                File.WriteAllText(SpritesPath, sb.ToString(), System.Text.Encoding.UTF8);
            }
            catch { /* тихо игнорировать */ }
        }

        void AutoLoad()
        {
            try
            {
                if (File.Exists(SpritesPath))
                {
                    var text = File.ReadAllText(SpritesPath, System.Text.Encoding.UTF8);
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
