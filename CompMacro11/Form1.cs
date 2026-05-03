using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CompMacro11
{
    // ── Панель номеров строк ──────────────────────────────────────
    // Располагается слева от RichTextBox.
    // Синхронизируется через GetFirstCharIndexFromLine + GetPositionFromCharIndex.
    public class LineNumPanel : Panel
    {
        private RichTextBox _rtb;
        public int ErrorLine { get; set; } = -1;

        private static readonly Color BG = Color.FromArgb(30, 30, 30);
        private static readonly Color FG = Color.FromArgb(100, 100, 100);
        private static readonly Color FG_ERR = Color.FromArgb(240, 100, 80);
        private static readonly Color BG_ERR = Color.FromArgb(90, 25, 25);

        public LineNumPanel(RichTextBox rtb)
        {
            _rtb = rtb;
            Width = 46;
            Dock = DockStyle.Left;
            BackColor = BG;
            DoubleBuffered = true;

            // Перерисовывать при прокрутке и изменении текста редактора
            _rtb.VScroll += (s, e) => Invalidate();
            _rtb.TextChanged += (s, e) => Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.FillRectangle(new SolidBrush(BG), ClientRectangle);

            if (_rtb.Lines.Length == 0) return;

            var font = _rtb.Font;
            var sfmt = new StringFormat
            {
                Alignment = StringAlignment.Far,
                LineAlignment = StringAlignment.Near
            };

            int lineCount = _rtb.Lines.Length;
            for (int i = 0; i < lineCount; i++)
            {
                int charIdx = _rtb.GetFirstCharIndexFromLine(i);
                if (charIdx < 0) break;
                Point pt = _rtb.GetPositionFromCharIndex(charIdx);

                // Учесть что Panel смещена левее RichTextBox — Y совпадает
                if (pt.Y + font.Height < 0) continue;
                if (pt.Y > _rtb.Height) break;

                int lineNum = i + 1;
                bool isErr = lineNum == ErrorLine;

                if (isErr)
                    g.FillRectangle(new SolidBrush(BG_ERR),
                        0, pt.Y, Width, font.Height + 1);

                var rc = new RectangleF(0, pt.Y, Width - 4, font.Height);
                g.DrawString(lineNum.ToString(), font,
                    new SolidBrush(isErr ? FG_ERR : FG), rc, sfmt);
            }

            sfmt.Dispose();
        }
    }

    public partial class Form1 : Form
    {
        // ── WinAPI для Undo/Redo в RichTextBox ───────────────────
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private const int WM_SETREDRAW = 0x000B;

        private static void BeginRtbUpdate(RichTextBox rtb) =>
            SendMessage(rtb.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
        private static void EndRtbUpdate(RichTextBox rtb)
        {
            SendMessage(rtb.Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
            rtb.Invalidate();
        }

        // ── Цвета ─────────────────────────────────────────────────
        static readonly Color C_BG = Color.FromArgb(28, 28, 28);
        static readonly Color C_BG2 = Color.FromArgb(37, 37, 38);
        static readonly Color C_BG3 = Color.FromArgb(22, 22, 22);
        static readonly Color C_TEXT = Color.FromArgb(212, 212, 212);
        static readonly Color C_KEYWORD = Color.FromArgb(86, 156, 214);
        static readonly Color C_NUMBER = Color.FromArgb(181, 206, 168);
        static readonly Color C_COMMENT = Color.FromArgb(106, 153, 85);
        static readonly Color C_TYPE = Color.FromArgb(78, 201, 176);
        static readonly Color C_ACCENT = Color.FromArgb(0, 122, 204);
        static readonly Color C_ERROR = Color.FromArgb(240, 100, 80);
        static readonly Color C_OK = Color.FromArgb(78, 201, 176);
        static readonly Color C_GRAY = Color.FromArgb(120, 120, 120);

        static readonly Font F_MONO = new Font("Consolas", 10.5f);
        static readonly Font F_UI = new Font("Segoe UI", 9f);
        static readonly Font F_UI_B = new Font("Segoe UI", 9f, FontStyle.Bold);

        // ── Контролы ──────────────────────────────────────────────
        private RichTextBox _src;
        private RichTextBox _out;
        private LineNumPanel _linePanel;
        private Label _status;
        private bool _highlighting;
        private System.Windows.Forms.Timer _hlTimer;
        private SpriteEditor _spriteEditor;
        private string _emulatorPath = "";   // путь к UKNCBTL.exe
        private McProject _project = null;   // текущий проект

        public Form1()
        {
            InitializeComponent();
            BuildUI();
            // Загружаем путь к эмулятору из окружения этой машины
            _emulatorPath = AppEnvironment.EmulatorPath;
        }

        private void BuildUI()
        {
            Text = "Mini-C → Macro-11";
            Size = new Size(1360, 820);
            MinimumSize = new Size(900, 600);
            BackColor = C_BG;
            Font = F_UI;
            KeyPreview = true;
            KeyDown += OnKeyDown;

            // ── Toolbar ───────────────────────────────────────────
            var bar = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = C_BG2 };

            var btnCompile = MakeBtn("▶  Компилировать  [F5]", 190, C_ACCENT, 8);
            btnCompile.Click += (_, __) => Compile();

            var btnClear = MakeBtn("✕", 36, Color.FromArgb(60, 60, 60), 206);
            btnClear.Font = new Font("Segoe UI", 12f);
            btnClear.Click += (_, __) =>
            {
                _src.Clear(); _out.Clear();
                _linePanel.ErrorLine = -1;
                _linePanel.Invalidate();
                SetStatus("", false);
            };

            var btnExample = MakeBtn("Пример", 75, Color.FromArgb(60, 60, 60), 250);
            btnExample.Click += (_, __) => { _src.Text = LoadSample(); Highlight(); };

            var btnCopy = MakeBtn("📋 Копировать", 115, Color.FromArgb(60, 60, 60), 333);
            btnCopy.Click += (_, __) =>
            {
                if (string.IsNullOrEmpty(_out.Text)) return;
                Clipboard.SetText(_out.Text);
                btnCopy.Text = "✔ Скопировано";
                btnCopy.ForeColor = C_OK;
                var t = new System.Windows.Forms.Timer { Interval = 1500 };
                t.Tick += (s2, e2) =>
                {
                    btnCopy.Text = "📋 Копировать";
                    btnCopy.ForeColor = Color.White;
                    t.Stop(); t.Dispose();
                };
                t.Start();
            };

            var btnRun = MakeBtn("▶ Эмулятор", 105, Color.FromArgb(30, 90, 50), 456);
            btnRun.Click += (_, __) => RunInEmulator();

            var btnHelp = MakeBtn("?", 32, Color.FromArgb(50, 80, 120), 569);
            btnHelp.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            btnHelp.Click += (_, __) => ShowHelp();

            var btnSprites = MakeBtn("🎨 Спрайты", 100, Color.FromArgb(60, 40, 80), 609);
            btnSprites.Click += (_, __) =>
            {
                if (_spriteEditor == null || _spriteEditor.IsDisposed)
                {
                    _spriteEditor = new SpriteEditor(code =>
                    {
                        string text = _src.Text;

                        // Удалить старый блок комментариев спрайтов если есть
                        // Блок начинается с "// ── Спрайты" и заканчивается
                        // перед первой непустой некомментарной строкой
                        var startMatch = Regex.Match(text,
                            @"// ── Спрайты[^\n]*\n(// [^\n]*\n)*");
                        if (startMatch.Success)
                        {
                            text = text.Substring(0, startMatch.Index)
                                 + text.Substring(startMatch.Index + startMatch.Length);
                        }

                        // Вставить новый блок в начало
                        _src.Text = code + "\n" + text;
                        _src.SelectionStart = 0;
                        Highlight();
                    });
                }
                if (_spriteEditor.Visible)
                    _spriteEditor.BringToFront();
                else
                    _spriteEditor.Show(this);
            };

            _status = new Label
            {
                Text = "",
                Height = 32,
                Font = F_UI_B,
                ForeColor = C_GRAY,
                BackColor = Color.Transparent,
                AutoSize = false,
                TextAlign = System.Drawing.ContentAlignment.MiddleRight,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };
            bar.SizeChanged += (_, __) =>
            {
                _status.Width = bar.Width - 845;
                _status.Location = new Point(840, 7);
            };

            // ── Меню Проект ───────────────────────────────────────
            var btnProject = MakeBtn("📁 Проект ▾", 110, Color.FromArgb(40, 70, 40), 717);
            btnProject.Click += (_, __) =>
            {
                var menu = new ContextMenuStrip();
                menu.BackColor = Color.FromArgb(45, 45, 48);
                menu.ForeColor = Color.White;

                var miNew = new ToolStripMenuItem("Новый проект...");
                var miOpen = new ToolStripMenuItem("Открыть проект...");
                var miSave = new ToolStripMenuItem("Сохранить проект") { Enabled = _project != null };
                var miSaveAs = new ToolStripMenuItem("Сохранить как...") { Enabled = _project != null };
                var miClose = new ToolStripMenuItem("Закрыть проект") { Enabled = _project != null };
                var miRecent = new ToolStripMenuItem("Последние проекты");

                var recent = RecentProjects.Load();
                if (recent.Count == 0)
                    miRecent.DropDownItems.Add(new ToolStripMenuItem("(пусто)") { Enabled = false });
                else
                    foreach (var r in recent)
                    {
                        string rr = r;
                        var mi = new ToolStripMenuItem(System.IO.Path.GetFileNameWithoutExtension(rr));
                        mi.Click += (s2, e2) => ProjectOpen(rr);
                        miRecent.DropDownItems.Add(mi);
                    }

                miNew.Click += (s2, e2) => ProjectNew();
                miOpen.Click += (s2, e2) => ProjectOpenDialog();
                miSave.Click += (s2, e2) => ProjectSave();
                miSaveAs.Click += (s2, e2) => ProjectSaveAs();
                miClose.Click += (s2, e2) => ProjectClose();

                menu.Items.AddRange(new ToolStripItem[] { miNew, miOpen, new ToolStripSeparator(), miSave, miSaveAs, miClose, new ToolStripSeparator(), miRecent });
                menu.Show(btnProject, new System.Drawing.Point(0, btnProject.Height));
            };

            bar.Controls.AddRange(new Control[] { btnCompile, btnClear, btnExample, btnCopy, btnRun, btnHelp, btnSprites, btnProject, _status });

            // ── Заголовки ─────────────────────────────────────────
            var hdr = new Panel
            {
                Dock = DockStyle.Top,
                Height = 28,
                BackColor = Color.FromArgb(30, 30, 30)
            };
            hdr.Controls.Add(MakeLabel("  Mini-C  —  исходный код", 0));
            hdr.Controls.Add(MakeLabel("  Macro-11  —  результат", 650));

            var btnCopyHdr = new Button
            {
                Text = "📋 Копировать",
                Width = 110,
                Height = 22,
                Location = new Point(860, 3),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = Color.FromArgb(180, 180, 180),
                Font = new Font("Segoe UI", 8f),
                Cursor = Cursors.Hand
            };
            btnCopyHdr.FlatAppearance.BorderSize = 0;
            btnCopyHdr.Click += (_, __) =>
            {
                if (string.IsNullOrEmpty(_out.Text)) return;
                Clipboard.SetText(_out.Text);
                btnCopyHdr.Text = "✔ Скопировано";
                btnCopyHdr.ForeColor = C_OK;
                var t = new System.Windows.Forms.Timer { Interval = 1500 };
                t.Tick += (s2, e2) =>
                {
                    btnCopyHdr.Text = "📋 Копировать";
                    btnCopyHdr.ForeColor = Color.FromArgb(180, 180, 180);
                    t.Stop(); t.Dispose();
                };
                t.Start();
            };
            hdr.Controls.Add(btnCopyHdr);

            // ── Разделитель ───────────────────────────────────────
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                BackColor = Color.FromArgb(55, 55, 55),
                SplitterWidth = 3,
                SplitterDistance = 620
            };
            // При первом показе установить 2/3 на редактор, 1/3 на ассемблер
            split.SizeChanged += (s, e) =>
            {
                if (split.Width > 0)
                {
                    int d = split.Width * 2 / 3;
                    if (d > split.Panel1MinSize && d < split.Width - split.Panel2MinSize)
                        split.SplitterDistance = d;
                }
            };

            // ── Левая панель: нумерация + редактор ────────────────
            _src = new RichTextBox
            {
                Dock = DockStyle.Fill,
                Font = F_MONO,
                BackColor = C_BG,
                ForeColor = C_TEXT,
                BorderStyle = BorderStyle.None,
                ScrollBars = RichTextBoxScrollBars.Both,
                WordWrap = false,
                AcceptsTab = true
            };
            // Дебаунс подсветки — 400мс после последнего нажатия
            _hlTimer = new System.Windows.Forms.Timer { Interval = 400 };
            _hlTimer.Tick += (_, __) => { _hlTimer.Stop(); Highlight(); };
            _src.TextChanged += (_, __) => { if (!_highlighting) { _hlTimer.Stop(); _hlTimer.Start(); } };

            _linePanel = new LineNumPanel(_src);

            // Контейнер: Panel слева (номера) + RichTextBox справа
            var leftContainer = new Panel { Dock = DockStyle.Fill };
            leftContainer.Controls.Add(_src);        // Fill — занимает остаток
            leftContainer.Controls.Add(_linePanel);  // Left — прибивается слева

            // ── Правая панель ─────────────────────────────────────
            _out = new RichTextBox
            {
                Dock = DockStyle.Fill,
                Font = F_MONO,
                BackColor = C_BG3,
                ForeColor = C_TEXT,
                BorderStyle = BorderStyle.None,
                ScrollBars = RichTextBoxScrollBars.Both,
                WordWrap = false,
                ReadOnly = true
            };

            split.Panel1.Controls.Add(leftContainer);
            split.Panel2.Controls.Add(_out);

            Controls.Add(split);
            Controls.Add(hdr);
            Controls.Add(bar);

            _src.Text = LoadSample();
            Highlight();
        }

        // ── Хелперы UI ────────────────────────────────────────────
        private Button MakeBtn(string text, int w, Color bg, int x)
        {
            var b = new Button
            {
                Text = text,
                Width = w,
                Height = 32,
                Location = new Point(x, 7),
                FlatStyle = FlatStyle.Flat,
                BackColor = bg,
                ForeColor = Color.White,
                Font = F_UI_B,
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        private Label MakeLabel(string text, int x) => new Label
        {
            Text = text,
            Location = new Point(x, 5),
            Width = 400,
            Height = 20,
            Font = F_UI_B,
            ForeColor = C_GRAY,
            BackColor = Color.Transparent
        };

        // ── Справка ───────────────────────────────────────────────
        private void ShowHelp()
        {
            var dlg = new Form
            {
                Text = "Справка — Mini-C для УКНЦ",
                Size = new Size(700, 620),
                MinimumSize = new Size(500, 400),
                BackColor = Color.FromArgb(28, 28, 28),
                ForeColor = Color.FromArgb(212, 212, 212),
                Font = F_UI,
                StartPosition = FormStartPosition.CenterParent,
                KeyPreview = true
            };
            dlg.KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) dlg.Close(); };

            var rtb = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(28, 28, 28),
                ForeColor = Color.FromArgb(212, 212, 212),
                Font = new Font("Consolas", 10f),
                BorderStyle = BorderStyle.None,
                ReadOnly = true,
                WordWrap = true,
                ScrollBars = RichTextBoxScrollBars.Vertical
            };
            dlg.Controls.Add(rtb);
            dlg.Show();
            FillHelp(rtb);
        }

        private void FillHelp(RichTextBox r)
        {
            HelpT(r, "\n═══ СИНТАКСИС Mini-C ═══════════════════════════════════\n", Color.FromArgb(86, 156, 214));
            HelpT(r, "\n  Типы:        int, void, bool\n", C_TEXT);
            HelpT(r, "  Массивы:     int a[10];   int b[4][8];   bool flags[8];\n", C_TEXT);
            HelpT(r, "  Инициализ.:  int x = 5;   bool f = true;   int a[10] = {0};\n", C_TEXT);
            HelpT(r, "\n  bool:        хранится как 0/1, true=1, false=0\n", C_TEXT);
            HelpT(r, "               bool ok = true;  if (ok) { ... }\n", C_TEXT);
            HelpT(r, "\n  Операторы:   if / else / while / for / return / break / continue\n", C_TEXT);
            HelpT(r, "  Скобки:      {} необязательны для одного оператора\n", C_TEXT);
            HelpT(r, "\n  Арифметика:  + - * / %\n", C_TEXT);
            HelpT(r, "  Сравнение:   == != < > <= >=\n", C_TEXT);
            HelpT(r, "  Логика:      && || !\n", C_TEXT);
            HelpT(r, "  Присвоение:  = += -= *= /=\n", C_TEXT);
            HelpT(r, "  Унарные:     - ! ++ --\n", C_TEXT);

            HelpT(r, "\n═══ ВСТРОЕННЫЕ ФУНКЦИИ ══════════════════════════════════\n", Color.FromArgb(86, 156, 214));
            HelpFn(r, "cls", "[mode]", "настройка экрана + очистка; mode 0..3 (0 по умолчанию)");
            HelpFn(r, "init", "[mode]", "то же что cls([mode])");
            HelpFn(r, "pause", "", "пауза ~177777 итераций NOP");
            HelpFn(r, "print", "\"text\"", "вывод строки на терминал");
            HelpFn(r, "printnum", "n", "вывод числа на терминал");
            HelpFn(r, "waitkey", "", "ждать клавишу → код (блокирующее)");
            HelpFn(r, "getkey", "", "прочитать клавишу → код или 0 (неблокирующее)");
            HelpFn(r, "getTimer", "", "счётчик времени LTC @#177546 → int (для анимации)");
            HelpFn(r, "sprite", "x,y,w,h,ptr", "спрайт из массива данных (MOV — перезапись)");
            HelpFn(r, "spriteOr", "x,y,w,h,ptr", "спрайт через BIS (OR с экраном — прозрачность)");
            HelpFn(r, "box", "x,y,w,h,c", "прямоугольник словами; x,w в словах; c = 0-3");

            HelpT(r, "\n═══ basicGraphic — пакет попиксельной графики ═══════════\n", Color.FromArgb(78, 201, 176));
            HelpT(r, "  Фундамент: point(). Все остальные функции строятся поверх.\n", Color.FromArgb(150, 150, 150));
            HelpT(r, "  Экран: 320×264 (режим 0/2) или 640×264 (режим 1/3), 4 цвета.\n", Color.FromArgb(150, 150, 150));
            HelpT(r, "  Цвета: 0=чёрный  1=синий  2=зелёный  3=белый\n\n", Color.FromArgb(150, 150, 150));
            HelpFn(r, "point", "x, y, c", "пиксель — фундамент пакета. Нативный, максимальная скорость.");
            HelpFn(r, "line", "x0,y0, x1,y1, c", "линия Брезенхэма. Нативный. Клиппинг Cohen-Sutherland.");
            HelpFn(r, "rect", "x, y, w, h, c", "прямоугольник (контур). Нативный. 4 стороны через line.");

            HelpT(r, "\n═══ ЦВЕТА (индекс для box) ════════════════════════════\n", Color.FromArgb(86, 156, 214));
            HelpKv(r, "0", "чёрный   (0)");
            HelpKv(r, "1", "синий    (255)");
            HelpKv(r, "2", "зелёный  (65280)");
            HelpKv(r, "3", "белый    (65535)");

            HelpT(r, "\n═══ КЛАВИШИ УКНЦ (десятичные) ══════════════════════════\n", Color.FromArgb(86, 156, 214));
            HelpKv(r, "67", "→  вправо  (103 окт)");
            HelpKv(r, "68", "←  влево   (104 окт)");
            HelpKv(r, "65", "↑  вверх   (101 окт)");
            HelpKv(r, "66", "↓  вниз    (102 окт)");
            HelpKv(r, "32", "   пробел  ( 40 окт)");
            HelpKv(r, "13", "   Enter   ( 15 окт)");

            HelpT(r, "\n═══ ЭКРАН УКНЦ ══════════════════════════════════════════\n", Color.FromArgb(86, 156, 214));
            HelpT(r, "  Разрешение:  320 × 264 пикселей\n", C_TEXT);
            HelpT(r, "  1 слово X  = 8 пикселей по горизонтали\n", C_TEXT);
            HelpT(r, "  1 строка Y = 1 пиксель по вертикали\n", C_TEXT);
            HelpT(r, "  Ширина:      40 слов (x: 0-39)\n", C_TEXT);
            HelpT(r, "  Высота:      264 строки (y: 0-263)\n", C_TEXT);
            HelpT(r, "  Клетка 2×32 = видимый прямоугольник 16×32 пикселей\n", C_TEXT);

            HelpT(r, "\n═══ ГОРЯЧИЕ КЛАВИШИ РЕДАКТОРА ══════════════════════════\n", Color.FromArgb(86, 156, 214));
            HelpT(r, "  F5       — компилировать\n", C_TEXT);
            HelpT(r, "  Ctrl+D   — дублировать строку\n", C_TEXT);
            HelpT(r, "  Esc      — закрыть справку\n", C_TEXT);

            r.SelectionStart = 0;
            r.ScrollToCaret();
        }

        private void HelpT(RichTextBox r, string s, Color c)
        {
            r.SelectionStart = r.TextLength;
            r.SelectionLength = 0;
            r.SelectionColor = c;
            r.AppendText(s);
        }

        private void HelpFn(RichTextBox r, string name, string args, string desc)
        {
            HelpT(r, "\n  ", C_TEXT);
            HelpT(r, name, Color.FromArgb(78, 201, 176));
            HelpT(r, "(" + args + ")", C_TEXT);
            HelpT(r, "  — " + desc, Color.FromArgb(120, 120, 120));
        }

        private void HelpKv(RichTextBox r, string key, string val)
        {
            HelpT(r, "  " + key, Color.FromArgb(181, 206, 168));
            HelpT(r, " = " + val + "\n", Color.FromArgb(120, 120, 120));
        }

        private void SetStatus(string msg, bool err)
        {
            _status.Text = msg;
            _status.ForeColor = err ? C_ERROR : C_OK;
        }

        // ── Горячие клавиши ───────────────────────────────────────
        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5)
            {
                Compile();
                e.Handled = true;
                return;
            }

            if (e.Control && e.KeyCode == Keys.D)
            {
                DuplicateLine();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        // ── Ctrl+D: дублировать текущую строку ───────────────────
        private void DuplicateLine()
        {
            int selStart = _src.SelectionStart;
            int lineIdx = _src.GetLineFromCharIndex(selStart);
            if (lineIdx < 0 || lineIdx >= _src.Lines.Length) return;

            string line = _src.Lines[lineIdx];
            int lineEnd = _src.GetFirstCharIndexFromLine(lineIdx) + line.Length;

            // Вставить перенос строки + копию после текущей строки
            _src.SelectionStart = lineEnd;
            _src.SelectionLength = 0;
            _src.SelectedText = "\n" + line;

            // Перевести курсор на начало новой строки
            _src.SelectionStart = lineEnd + 1;
            _src.SelectionLength = line.Length;
        }

        // ── Компиляция ────────────────────────────────────────────
        private void Compile()
        {
            _linePanel.ErrorLine = -1;
            _linePanel.Invalidate();
            _src.SelectAll();
            _src.SelectionBackColor = C_BG;
            _src.SelectionLength = 0;

            _out.ForeColor = C_TEXT;
            _out.Clear();

            string src = _src.Text;
            if (string.IsNullOrWhiteSpace(src))
            {
                SetStatus("⚠  Пустой исходный код", true);
                return;
            }

            try
            {
                int spriteLineOffset;
                string fullSrc = InjectSprites(src, out spriteLineOffset);
                fullSrc = StdLib.Inject(fullSrc);       // встроенные Mini-C библиотеки

                var tokens = new Lexer(fullSrc).Tokenize();
                var ast = new Parser(tokens).ParseProgram();
                string asm = new CodeGen().Generate(ast);

                _out.ForeColor = C_TEXT;
                _out.Text = asm;
                SetStatus("✔  Компиляция успешна", false);
                ColorizeAsm();
            }
            catch (Exception ex)
            {
                _out.ForeColor = C_ERROR;
                _out.Text = "ОШИБКА:\r\n\r\n" + ex.Message;
                SetStatus("✘  " + ex.Message, true);

                var m = Regex.Match(ex.Message, @"Строка\s+(\d+)");
                if (m.Success && int.TryParse(m.Groups[1].Value, out int lineNum))
                {
                    // Вычесть строки вставленного кода спрайтов
                    int spriteOffset;
                    InjectSprites(src, out spriteOffset);
                    int userLine = Math.Max(1, lineNum - spriteOffset);

                    _linePanel.ErrorLine = userLine;
                    _linePanel.Invalidate();

                    int idx = _src.GetFirstCharIndexFromLine(userLine - 1);
                    if (idx >= 0 && userLine - 1 < _src.Lines.Length)
                    {
                        _src.SelectionStart = idx;
                        _src.SelectionLength = _src.Lines[userLine - 1].Length;
                        _src.SelectionBackColor = Color.FromArgb(90, 25, 25);
                        _src.SelectionLength = 0;
                        _src.ScrollToCaret();
                    }
                }
            }
        }

        // ── Запуск в эмуляторе UKNCBTL ───────────────────────────
        // Пайплайн:
        //   Mini-C → [наш компилятор] → A.MAC
        //   Скопировать A.MAC в папку с UKNCBTL.exe, которая настроена для компиляции и запуска Эмулятора
        //   Запускаем Run.bat       
        private void RunInEmulator()
        {
            // 1. Найти UKNCBTL.exe — сначала из окружения машины
            string emulPath = _emulatorPath;
            if (string.IsNullOrEmpty(emulPath))
                emulPath = AppEnvironment.EmulatorPath;

            if (string.IsNullOrEmpty(emulPath) || !System.IO.File.Exists(emulPath))
            {
                string here = System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location);
                string candidate = System.IO.Path.Combine(here, "UKNCBTL.exe");
                if (System.IO.File.Exists(candidate))
                    emulPath = candidate;
                else
                {
                    using (var dlg = new OpenFileDialog
                    {
                        Title = "Укажите UKNCBTL.exe",
                        Filter = "UKNCBTL|UKNCBTL.exe|Все файлы|*.*"
                    })
                    {
                        if (dlg.ShowDialog() != DialogResult.OK) return;
                        emulPath = dlg.FileName;
                    }
                }
                // Сохраняем в окружение — больше не спрашиваем
                _emulatorPath = emulPath;
                AppEnvironment.EmulatorPath = emulPath;
            }
            string workDir = System.IO.Path.GetDirectoryName(emulPath);

            // 2. Компилировать Mini-C → A.MAC
            Compile();
            if (string.IsNullOrWhiteSpace(_out.Text)) return;
            string macPath = System.IO.Path.Combine(workDir, "A.MAC");
            System.IO.File.WriteAllText(macPath, _out.Text, System.Text.Encoding.ASCII);

            string runbat = System.IO.Path.Combine(workDir, "Run.bat");
            RunProcess(runbat, "", workDir, out string macroOut);


        }



        private bool RunProcess(string exe, string args, string workDir, out string output)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    WorkingDirectory = workDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                var proc = System.Diagnostics.Process.Start(psi);
                string stdout = "", stderr = "";
                var t1 = new System.Threading.Thread(() => stdout = proc.StandardOutput.ReadToEnd());
                var t2 = new System.Threading.Thread(() => stderr = proc.StandardError.ReadToEnd());
                t1.Start(); t2.Start();
                bool done = proc.WaitForExit(30000);
                if (!done) { proc.Kill(); output = "[timeout]"; return false; }
                t1.Join(3000); t2.Join(3000);
                sb.Append(stdout); sb.Append(stderr);
                output = sb.ToString();
                return proc.ExitCode == 0;
            }
            catch (Exception ex) { output = ex.Message; return false; }
        }

        private bool RunProcess(string exe, string args, string workDir)
        {
            return RunProcess(exe, args, workDir, out _);
        }

        private string FindTool(string emulDir, string name)
        {
            foreach (var dir in new[]
            {
                emulDir,
                System.IO.Path.GetDirectoryName(emulDir),
                System.IO.Path.Combine(emulDir, "tools"),
                System.IO.Path.Combine(emulDir, "utils"),
            })
            {
                if (dir == null) continue;
                string p = System.IO.Path.Combine(dir, name);
                if (System.IO.File.Exists(p)) return p;
            }
            return null;
        }

        private string FindDiskImage(string emulDir, string workDir)
        {
            foreach (var dir in new[] { emulDir,
                System.IO.Path.GetDirectoryName(emulDir),
                workDir })
            {
                if (dir == null) continue;
                foreach (var ext in new[] { "*.dsk", "*.rtd", "*.img" })
                    foreach (var f in System.IO.Directory.GetFiles(dir, ext))
                        return f;
            }
            return null;
        }

        // ── Встроенная Mini-C библиотека ─────────────────────────
        // Функции добавляются автоматически если используются в коде.
        private string InjectSprites(string src, out int lineOffset)
        {
            lineOffset = 0;
            string spriteCode = BuildSpriteCode(src);
            if (string.IsNullOrEmpty(spriteCode)) return src;

            // Вставляем перед первой функцией (первое вхождение типа + имя + '(')
            var m = Regex.Match(src, @"^\s*(void|int)\s+\w+\s*\(", RegexOptions.Multiline);
            int insertPos = m.Success ? m.Index : 0;

            // Смещение строк — сколько строк в spriteCode
            lineOffset = spriteCode.Split('\n').Length;

            return src.Substring(0, insertPos) + spriteCode + "\n" + src.Substring(insertPos);
        }

        private string BuildSpriteCode(string src)
        {
            var mentioned = new System.Collections.Generic.HashSet<string>();
            foreach (Match m in Regex.Matches(src, @"//\s*sprite:\s*(\w+)"))
                mentioned.Add(m.Groups[1].Value);
            if (mentioned.Count == 0) return "";

            List<Sprite> sprites = null;
            if (_spriteEditor != null && !_spriteEditor.IsDisposed)
                sprites = _spriteEditor.GetSprites();
            else
                sprites = LoadSpritesFromFile();

            if (sprites == null) sprites = new List<Sprite>();

            var missing = new System.Collections.Generic.List<string>();
            var found = new System.Collections.Generic.HashSet<string>();
            foreach (var s in sprites) found.Add(s.Name);
            foreach (var name in mentioned)
                if (!found.Contains(name)) missing.Add(name);
            if (missing.Count > 0)
                SetStatus($"⚠  Спрайты не найдены: {string.Join(", ", missing)}", true);

            // Собрать объявления спрайтов
            var sb = new System.Text.StringBuilder();
            foreach (var s in sprites)
                if (mentioned.Contains(s.Name))
                    sb.AppendLine(s.ExportC());
            return sb.ToString();
        }

        private List<Sprite> LoadSpritesFromFile()
        {
            try
            {
                if (!System.IO.File.Exists(SpriteEditor.AutoSavePath)) return null;
                var text = System.IO.File.ReadAllText(SpriteEditor.AutoSavePath, System.Text.Encoding.UTF8);
                text = text.Replace("\r\n", "\n");
                var blocks = text.Split(new[] { "---\n" }, StringSplitOptions.RemoveEmptyEntries);
                var result = new List<Sprite>();
                foreach (var b in blocks)
                {
                    var sp = Sprite.Deserialize(b);
                    if (sp.Pixels != null && sp.Pixels.Length == sp.PixelWidth * sp.Height)
                        result.Add(sp);
                }
                return result;
            }
            catch { return null; }
        }

        // ── Подсветка Mini-C ──────────────────────────────────────
        private static readonly HashSet<string> KwTypes = new HashSet<string> { "int", "void", "bool" };
        private static readonly HashSet<string> KwControl = new HashSet<string> { "if", "else", "while", "for", "return", "break", "continue" };
        private static readonly HashSet<string> KwBuiltin = new HashSet<string>(
            new[]{"cls","box","sprite","spriteOr","point","circle","print",
                  "waitkey","getkey","getTimer","pause","init","fillhline"})
        { };
        static Form1()
        {
            foreach (var n in StdLib.Names) KwBuiltin.Add(n);
        }
        private static readonly HashSet<string> KwBool = new HashSet<string> { "true", "false" };

        // Имена спрайтов для подсветки — обновляются из редактора/файла
        private HashSet<string> GetSpriteNames()
        {
            var names = new HashSet<string>();
            List<Sprite> sprites = null;
            if (_spriteEditor != null && !_spriteEditor.IsDisposed)
                sprites = _spriteEditor.GetSprites();
            else
                sprites = LoadSpritesFromFile();
            if (sprites != null)
                foreach (var s in sprites) names.Add(s.Name);
            return names;
        }

        private void Highlight()
        {
            if (_highlighting) return;
            _highlighting = true;

            int sel = _src.SelectionStart;
            int topChar = _src.GetCharIndexFromPosition(new System.Drawing.Point(1, 1));

            _src.SuspendLayout();
            BeginRtbUpdate(_src);

            string text = _src.Text;
            int n = text.Length;
            var spans = new System.Collections.Generic.List<(int s, int l, Color c)>(256);
            var spriteNames = GetSpriteNames();

            int i = 0;
            while (i < n)
            {
                char ch = text[i];

                // Однострочный комментарий
                if (ch == '/' && i + 1 < n && text[i + 1] == '/')
                {
                    int start = i;
                    while (i < n && text[i] != '\n') i++;
                    spans.Add((start, i - start, C_COMMENT));
                    continue;
                }

                // Блочный комментарий
                if (ch == '/' && i + 1 < n && text[i + 1] == '*')
                {
                    int start = i; i += 2;
                    while (i < n - 1 && !(text[i] == '*' && text[i + 1] == '/')) i++;
                    i += 2;
                    spans.Add((start, i - start, C_COMMENT));
                    continue;
                }

                // Число
                if (char.IsDigit(ch) && (i == 0 || !char.IsLetterOrDigit(text[i - 1])))
                {
                    int start = i;
                    while (i < n && char.IsDigit(text[i])) i++;
                    if (i >= n || !char.IsLetter(text[i]))
                        spans.Add((start, i - start, C_NUMBER));
                    continue;
                }

                // Идентификатор или ключевое слово
                if (char.IsLetter(ch) || ch == '_')
                {
                    int start = i;
                    while (i < n && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                    string word = text.Substring(start, i - start);
                    if (KwTypes.Contains(word)) spans.Add((start, i - start, C_TYPE));
                    else if (KwControl.Contains(word)) spans.Add((start, i - start, C_KEYWORD));
                    else if (KwBuiltin.Contains(word)) spans.Add((start, i - start, C_ACCENT));
                    else if (KwBool.Contains(word)) spans.Add((start, i - start, C_NUMBER));
                    continue;
                }

                i++;
            }

            // Применить все spans
            _src.SelectAll();
            _src.SelectionColor = C_TEXT;
            foreach (var (s, l, c) in spans)
            {
                _src.SelectionStart = s;
                _src.SelectionLength = l;
                _src.SelectionColor = c;
            }

            // Восстановить каретку ДО EndRtbUpdate
            _src.SelectionStart = sel;
            _src.SelectionLength = 0;
            _src.SelectionColor = C_TEXT;

            EndRtbUpdate(_src);
            _src.ResumeLayout();

            // Восстановить позицию скролла ПОСЛЕ перерисовки:
            // сначала прокрутить к topChar, затем вернуть реальную каретку
            _src.SelectionStart = topChar;
            _src.ScrollToCaret();
            _src.SelectionStart = sel;
            _src.SelectionLength = 0;

            _highlighting = false;
        }

        private void PaintSrc(int start, int len, Color color)
        {
            _src.SelectionStart = start;
            _src.SelectionLength = len;
            _src.SelectionColor = color;
        }

        // ── Подсветка Macro-11 ────────────────────────────────────
        private static readonly string[] AsmOps =
        {
            "MOV","CLR","INC","DEC","NEG","TST","CMP","ADD","SUB","MUL","DIV",
            "ASL","ASR","JSR","RTS","BR","BEQ","BNE","BLT","BGT","BLE","BGE",
            "JMP","SOB","NOP","HALT","ROL","ROR"
        };
        private static readonly string[] AsmDir =
        {
            "\\.TITLE","\\.PSECT","\\.END","\\.WORD","\\.BLKW","\\.BYTE",
            "\\.EVEN","\\.MACRO","\\.ENDM","\\.MCall"
        };

        private void ColorizeAsm()
        {
            _out.SuspendLayout();
            BeginRtbUpdate(_out);

            _out.SelectAll();
            _out.SelectionColor = C_TEXT;
            string text = _out.Text;

            foreach (var d in AsmDir)
                foreach (Match m in Regex.Matches(text, d, RegexOptions.IgnoreCase))
                    PaintOut(m.Index, m.Length, C_TYPE);
            foreach (var op in AsmOps)
                foreach (Match m in Regex.Matches(text, $@"\b{op}\b"))
                    PaintOut(m.Index, m.Length, C_KEYWORD);
            foreach (Match m in Regex.Matches(text, @"^[A-Z][A-Z0-9]{0,5}:", RegexOptions.Multiline))
                PaintOut(m.Index, m.Length, C_OK);
            foreach (Match m in Regex.Matches(text, @"#-?\d+\."))
                PaintOut(m.Index, m.Length, C_NUMBER);
            foreach (Match m in Regex.Matches(text, @";[^\n]*"))
                PaintOut(m.Index, m.Length, C_COMMENT);
            foreach (Match m in Regex.Matches(text, @"\b(R[0-7]|SP|PC)\b"))
                PaintOut(m.Index, m.Length, C_TYPE);

            _out.SelectionStart = 0;
            _out.SelectionLength = 0;
            _out.SelectionColor = C_TEXT;

            EndRtbUpdate(_out);
            _out.ResumeLayout();
        }

        private void PaintOut(int start, int len, Color color)
        {
            _out.SelectionStart = start;
            _out.SelectionLength = len;
            _out.SelectionColor = color;
        }

        // ── Загрузка примера из файла ─────────────────────────────
        private static readonly string SamplePath =
            System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location),
                "sample.c");

        private string LoadSample()
        {
            try
            {
                if (System.IO.File.Exists(SamplePath))
                    return System.IO.File.ReadAllText(SamplePath, System.Text.Encoding.UTF8);
                // Файл не найден — сообщить и вернуть пустой пример
                SetStatus($"⚠  Файл sample.c не найден: {SamplePath}", true);
                return $"// Файл sample.c не найден.\n// Ожидается по пути:\n// {SamplePath}\n";
            }
            catch (Exception ex)
            {
                SetStatus($"⚠  Ошибка чтения sample.c: {ex.Message}", true);
                return $"// Ошибка чтения sample.c:\n// {ex.Message}\n";
            }
        }

        // ── Пример (резервный — оставлен на случай отсутствия файла) ─
        private string Example() =>
@"// Поле 10x10, заполненное 1. Герой движется по стрелкам.
// Экран УКНЦ: 1 слово = 8 пикселей по X, 1 строка по Y.
// Клетка: 4 слова x 16 строк = 32x16 пикселей.
// Поле на экране: 40 слов x 160 строк.

// Нарисовать одну клетку поля
void drawCell(int x, int y, int color) {
    box(x * 4, y * 16, 4, 16, color);
}

// Отобразить героя в позиции (hx, hy)
void HERO(int hx, int hy) {
    drawCell(hx, hy, 3);
}

// Нарисовать всё поле
void drawField(int field[][10]) {
    int x = 0;
    int y = 0;
    for (y = 0; y < 10; y++) {
        for (x = 0; x < 10; x++) {
            drawCell(x, y, field[y][x]);
        }
    }
}

int main(void) {
    init();
    cls();

    int field[10][10] = {1};

    int hx = 0;
    int hy = 0;
    int k  = 0;

    drawField(field);
    HERO(hx, hy);

    while (k != 32) {
        k = getkey();

        if (k == 67) {
            if (hx < 9) {
                drawCell(hx, hy, field[hy][hx]);
                hx = hx + 1;
                HERO(hx, hy);
            }
        }
        if (k == 68) {
            if (hx > 0) {
                drawCell(hx, hy, field[hy][hx]);
                hx = hx - 1;
                HERO(hx, hy);
            }
        }
        if (k == 65) {
            if (hy > 0) {
                drawCell(hx, hy, field[hy][hx]);
                hy = hy - 1;
                HERO(hx, hy);
            }
        }
        if (k == 66) {
            if (hy < 9) {
                drawCell(hx, hy, field[hy][hx]);
                hy = hy + 1;
                HERO(hx, hy);
            }
        }
    }

    return 0;
}";


        // ── Методы работы с проектом ──────────────────────────────────

        private void ProjectNew()
        {
            if (!ProjectCheckSave()) return;
            var dlg = new NewProjectDialog();
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            if (string.IsNullOrWhiteSpace(dlg.ProjectName)) return;
            try
            {
                _project = McProject.CreateNew(dlg.ProjectName, dlg.ProjectFolder);
                RecentProjects.Add(_project.ProjectPath);
                AppEnvironment.LastProjectPath = _project.ProjectPath;
                _src.Text = _project.ReadMainCode();
                UpdateTitle();
                SetStatus("✓ Проект создан: " + _project.Name, false);
            }
            catch (Exception ex) { MessageBox.Show("Ошибка создания проекта:\n" + ex.Message); }
        }

        private void ProjectOpenDialog()
        {
            if (!ProjectCheckSave()) return;
            var dlg = new OpenFileDialog
            {
                Title = "Открыть проект",
                Filter = "Проект Mini-C (*.pkc)|*.pkc",
                DefaultExt = "pkc"
            };
            if (dlg.ShowDialog(this) == DialogResult.OK)
                ProjectOpen(dlg.FileName);
        }

        private void ProjectOpen(string path)
        {
            if (!ProjectCheckSave()) return;
            try
            {
                _project = McProject.Load(path);
                RecentProjects.Add(path);
                AppEnvironment.LastProjectPath = path;
                _src.Text = _project.ReadMainCode();
                UpdateTitle();
                SetStatus("✓ Открыт: " + _project.Name, false);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка открытия проекта:\n" + ex.Message);
                RecentProjects.Remove(path);
            }
        }

        private void ProjectSave()
        {
            if (_project == null) return;
            _project.WriteMainCode(_src.Text);
            _project.Save();
            UpdateTitle();
            SetStatus("✓ Сохранено", false);
        }

        private void ProjectSaveAs()
        {
            if (_project == null) return;
            var dlg = new SaveFileDialog
            {
                Title = "Сохранить проект как",
                Filter = "Проект Mini-C (*.pkc)|*.pkc",
                DefaultExt = "pkc",
                FileName = _project.Name + ".pkc"
            };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _project.WriteMainCode(_src.Text);
                _project.SaveAs(dlg.FileName);
                RecentProjects.Add(dlg.FileName);
                AppEnvironment.LastProjectPath = dlg.FileName;
                UpdateTitle();
                SetStatus("✓ Сохранено как: " + dlg.FileName, false);
            }
        }

        private void ProjectClose()
        {
            if (!ProjectCheckSave()) return;
            _project = null;
            UpdateTitle();
            SetStatus("Проект закрыт", false);
        }

        private bool ProjectCheckSave()
        {
            if (_project == null || !_project.IsModified) return true;
            var r = MessageBox.Show(
                "Проект \"" + _project.Name + "\" изменён. Сохранить?",
                "Сохранить проект?",
                MessageBoxButtons.YesNoCancel);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.Yes) ProjectSave();
            return true;
        }

        private void UpdateTitle()
        {
            Text = _project != null
                ? "Mini-C → Macro-11  |  " + _project.Name
                : "Mini-C → Macro-11";
        }
    }
}
