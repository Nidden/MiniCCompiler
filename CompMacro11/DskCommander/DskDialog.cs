using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace DskCommander
{
    // ══════════════════════════════════════════════════════════════════
    //  Двухпанельный файловый менеджер в стиле Norton/Volkov Commander.
    //  Слева — Windows, справа — образ .dsk (RT-11 через DskImage).
    //
    //  Панели рисуются вручную (Control + OnPaint), а не ListView:
    //  только так получается вид DOS-коммандера — двойная рамка, путь
    //  в верхней рамке, колонки с разделителями, ►SUB-DIR◄, полоса
    //  курсора, строка состояния внизу панели.
    //
    //  Клавиши как в NC: Tab — панель, Enter — вход, Ins — пометить,
    //  Ctrl+A — пометить всё, F1..F10 — нижний ряд, F9 — меню, Esc —
    //  выход. Операции применяются к помеченным файлам, а если ничего
    //  не помечено — к файлу под курсором.
    // ══════════════════════════════════════════════════════════════════

    // ── Палитра EGA/VGA и знакоместо ──────────────────────────────────
    internal static class Dos
    {
        public static readonly Color Blue = Color.FromArgb(0, 0, 168);
        public static readonly Color Cyan = Color.FromArgb(0, 168, 168);
        public static readonly Color LightCyan = Color.FromArgb(85, 255, 255);
        public static readonly Color Gray = Color.FromArgb(168, 168, 168);
        public static readonly Color DarkGray = Color.FromArgb(85, 85, 85);
        public static readonly Color White = Color.FromArgb(255, 255, 255);
        public static readonly Color Yellow = Color.FromArgb(255, 255, 85);
        public static readonly Color Red = Color.FromArgb(168, 0, 0);
        public static readonly Color Green = Color.FromArgb(0, 168, 0);
        public static readonly Color Black = Color.Black;

        public static readonly Font Font = new Font("Consolas", 12f);
        public static readonly int CW;      // ширина знакоместа
        public static readonly int CH;      // высота строки

        const TextFormatFlags TF = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

        static Dos()
        {
            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp))
            {
                var sz = TextRenderer.MeasureText(g, new string('0', 20), Font,
                                                  new Size(4000, 200), TF);
                CW = Math.Max(1, (int)Math.Round(sz.Width / 20.0));
            }
            CH = Font.Height;
        }

        public static void Text(Graphics g, int x, int y, string s, Color fg) =>
            TextRenderer.DrawText(g, s, Font, new Point(x, y), fg, TF);

        public static void Text(Graphics g, int x, int y, string s, Color fg, Color bg) =>
            TextRenderer.DrawText(g, s, Font, new Point(x, y), fg, bg, TF);

        public static void Fill(Graphics g, Rectangle r, Color c)
        { using (var b = new SolidBrush(c)) g.FillRectangle(b, r); }

        public static void Fill(Graphics g, int x, int y, int w, int h, Color c)
        { using (var b = new SolidBrush(c)) g.FillRectangle(b, x, y, w, h); }

        // двойная рамка (╔═╗ ║ ╚═╝) — две линии в 3 px друг от друга
        public static void Frame2(Graphics g, Rectangle r, Color c)
        {
            using (var p = new Pen(c, 1))
            {
                g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
                g.DrawRectangle(p, r.X + 3, r.Y + 3, r.Width - 6, r.Height - 6);
            }
        }

        public static void HLine(Graphics g, int x1, int x2, int y, Color c)
        { using (var p = new Pen(c, 1)) g.DrawLine(p, x1, y, x2, y); }

        public static void VLine(Graphics g, int x, int y1, int y2, Color c)
        { using (var p = new Pen(c, 1)) g.DrawLine(p, x, y1, x, y2); }

        // обрезать строку по ширине в знакоместах
        public static string Cut(string s, int cells)
        {
            if (s == null) return "";
            if (cells <= 0) return "";
            return s.Length <= cells ? s : s.Substring(0, cells);
        }

        // дата/время в формате DOS: 1-10-24 и 4:31p
        public static string DosDate(DateTime d) => $"{d.Day,2}-{d.Month:00}-{d:yy}";
        public static string DosTime(DateTime d) =>
            d.ToString("h:mm") + (d.Hour < 12 ? "a" : "p");
    }

    // ── Строка панели ─────────────────────────────────────────────────
    internal class NcRow
    {
        public string[] Cells;              // текст по колонкам
        public IComparable[] Keys;          // ключи сортировки (может быть null)
        public bool IsDir, IsUp, Marked;
        public object Tag;
        public Color Color = Dos.Gray;
    }

    internal class NcColumn
    {
        public string Title;
        public int Width;                   // в знакоместах; 0 — «резиновая»
        public bool Right;                  // выравнивание вправо
    }

    // ══════════════════════════════════════════════════════════════════
    //  Панель-список: рамка, колонки, курсор, пометки, прокрутка
    // ══════════════════════════════════════════════════════════════════
    internal class NcPanel : Control
    {
        public event Action OnActivate;
        public event Action OnEnterItem;

        protected List<NcRow> Rows = new List<NcRow>();
        protected NcColumn[] Cols = new NcColumn[0];
        public string Title = "";
        public string Info = "";            // справа в строке состояния

        int _cursor, _top, _anchor;
        bool _active;
        int _sortCol, _visible = 1;
        bool _sortDesc;

        public NcPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.Selectable, true);
            TabStop = true;
            BackColor = Dos.Blue;
            Font = Dos.Font;
        }

        public bool IsActive => _active;
        public int CursorIndex => _cursor;
        public NcRow Current => (_cursor >= 0 && _cursor < Rows.Count) ? Rows[_cursor] : null;

        public void SetActive(bool on)
        {
            _active = on;
            if (on && !Focused) Focus();
            Invalidate();
        }

        // Цели операции: помеченные строки, а если пометок нет — строка
        // под курсором (как в NC).
        public List<NcRow> Targets()
        {
            var list = new List<NcRow>();
            foreach (var r in Rows) if (r.Marked && !r.IsUp) list.Add(r);
            if (list.Count == 0 && Current != null && !Current.IsUp) list.Add(Current);
            return list;
        }

        public void MarkAll()
        {
            foreach (var r in Rows) if (!r.IsUp) r.Marked = true;
            Invalidate();
        }

        public void UnmarkAll()
        {
            foreach (var r in Rows) r.Marked = false;
            Invalidate();
        }

        // ── заполнение ──
        protected void SetRows(List<NcRow> rows, string keepName)
        {
            Rows = rows ?? new List<NcRow>();
            SortRows();
            _cursor = 0;
            if (!string.IsNullOrEmpty(keepName))
                for (int i = 0; i < Rows.Count; i++)
                    if (string.Equals(Rows[i].Cells[0], keepName, StringComparison.OrdinalIgnoreCase))
                    { _cursor = i; break; }
            _anchor = _cursor;
            ScrollToCursor();
            Invalidate();
        }

        void SortRows()
        {
            int col = _sortCol;
            bool desc = _sortDesc;
            Rows.Sort((a, b) =>
            {
                int ga = a.IsUp ? 0 : a.IsDir ? 1 : 2;
                int gb = b.IsUp ? 0 : b.IsDir ? 1 : 2;
                if (ga != gb) return ga - gb;
                int r = CmpCell(a, b, col);
                if (desc) r = -r;
                if (r == 0 && col != 0) r = CmpCell(a, b, 0);
                return r;
            });
        }

        static int CmpCell(NcRow a, NcRow b, int col)
        {
            if (a.Keys != null && b.Keys != null && col < a.Keys.Length && col < b.Keys.Length
                && a.Keys[col] != null && b.Keys[col] != null)
                return a.Keys[col].CompareTo(b.Keys[col]);
            string ta = col < a.Cells.Length ? a.Cells[col] : "";
            string tb = col < b.Cells.Length ? b.Cells[col] : "";
            return string.Compare(ta, tb, StringComparison.CurrentCultureIgnoreCase);
        }

        public void SortBy(int col)
        {
            if (col < 0 || col >= Cols.Length) return;
            if (col == _sortCol) _sortDesc = !_sortDesc;
            else { _sortCol = col; _sortDesc = false; }
            string keep = Current != null ? Current.Cells[0] : null;
            SortRows();
            _cursor = 0;
            if (keep != null)
                for (int i = 0; i < Rows.Count; i++)
                    if (Rows[i].Cells[0] == keep) { _cursor = i; break; }
            ScrollToCursor();
            Invalidate();
        }

        // ── курсор/прокрутка ──
        void MoveCursor(int delta, bool mark)
        {
            if (Rows.Count == 0) return;
            int old = _cursor;
            _cursor = Math.Max(0, Math.Min(Rows.Count - 1, _cursor + delta));
            if (mark && old >= 0 && old < Rows.Count && !Rows[old].IsUp)
                Rows[old].Marked = !Rows[old].Marked;
            _anchor = _cursor;
            ScrollToCursor();
            Invalidate();
        }

        void ScrollToCursor()
        {
            if (_visible < 1) _visible = 1;
            if (_cursor < _top) _top = _cursor;
            if (_cursor >= _top + _visible) _top = _cursor - _visible + 1;
            if (_top > Rows.Count - _visible) _top = Rows.Count - _visible;
            if (_top < 0) _top = 0;
        }

        protected override bool IsInputKey(Keys k)
        {
            switch (k & Keys.KeyCode)
            {
                case Keys.Up:
                case Keys.Down:
                case Keys.PageUp:
                case Keys.PageDown:
                case Keys.Home:
                case Keys.End:
                case Keys.Insert:
                case Keys.Enter:
                    return true;
            }
            return base.IsInputKey(k);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            if (OnActivate != null) OnActivate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.Up: MoveCursor(-1, false); e.Handled = true; break;
                case Keys.Down: MoveCursor(1, false); e.Handled = true; break;
                case Keys.PageUp: MoveCursor(-_visible, false); e.Handled = true; break;
                case Keys.PageDown: MoveCursor(_visible, false); e.Handled = true; break;
                case Keys.Home: MoveCursor(-Rows.Count, false); e.Handled = true; break;
                case Keys.End: MoveCursor(Rows.Count, false); e.Handled = true; break;
                case Keys.Insert: MoveCursor(1, true); e.Handled = true; break;
                case Keys.Enter:
                    if (OnEnterItem != null) OnEnterItem();
                    e.Handled = true;
                    break;
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (OnActivate != null) OnActivate();
            int row = e.Y / Dos.CH;
            if (row == 1) { HitHeader(e.X); return; }     // клик по заголовку — сортировка
            int idx = _top + (row - 2);
            if (row < 2 || idx < 0 || idx >= Rows.Count) return;
            if ((ModifierKeys & Keys.Shift) != 0)
            {
                int a = Math.Min(_anchor, idx), b = Math.Max(_anchor, idx);
                for (int i = a; i <= b; i++) if (!Rows[i].IsUp) Rows[i].Marked = true;
            }
            else if ((ModifierKeys & Keys.Control) != 0 || e.Button == MouseButtons.Right)
            {
                if (!Rows[idx].IsUp) Rows[idx].Marked = !Rows[idx].Marked;
                _anchor = idx;
            }
            else _anchor = idx;
            _cursor = idx;
            Invalidate();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Y / Dos.CH >= 2 && OnEnterItem != null) OnEnterItem();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            _top -= Math.Sign(e.Delta) * 3;
            if (_top > Rows.Count - _visible) _top = Rows.Count - _visible;
            if (_top < 0) _top = 0;
            Invalidate();
        }

        void HitHeader(int x)
        {
            var lay = Layout();
            for (int i = 0; i < Cols.Length; i++)
            {
                int x1 = lay[i] * Dos.CW;
                int x2 = (lay[i] + ColWidth(i, lay)) * Dos.CW;
                if (x >= x1 && x < x2) { SortBy(i); return; }
            }
        }

        // ── раскладка колонок: начальные знакоместа ──
        int[] Layout()
        {
            int cols = Math.Max(24, ClientSize.Width / Dos.CW);
            int fixedSum = 0, flex = -1;
            for (int i = 0; i < Cols.Length; i++)
            {
                if (Cols[i].Width == 0) flex = i;
                else fixedSum += Cols[i].Width;
            }
            // 1 знакоместо отступ слева, 2 справа (иначе правая колонка
            // затирает рамку), плюс по знакоместу на каждый разделитель
            int inner = cols - 3 - (Cols.Length - 1);
            int flexW = Math.Max(6, inner - fixedSum);
            var start = new int[Cols.Length];
            int x = 1;
            for (int i = 0; i < Cols.Length; i++)
            {
                start[i] = x;
                x += (i == flex ? flexW : Cols[i].Width) + 1;
            }
            return start;
        }

        int ColWidth(int i, int[] start)
        {
            int cols = Math.Max(24, ClientSize.Width / Dos.CW);
            int next = (i + 1 < start.Length) ? start[i + 1] - 1 : cols - 2;
            return Math.Max(1, next - start[i]);
        }

        protected virtual string StatusText() => Current == null ? "" : Current.Cells[0];

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            int cw = Dos.CW, ch = Dos.CH;
            int W = ClientSize.Width, H = ClientSize.Height;
            if (W < cw * 24 || H < ch * 8 || Cols.Length == 0) { Dos.Fill(g, ClientRectangle, Dos.Blue); return; }

            int rowsTotal = H / ch;
            int cols = W / cw;
            _visible = Math.Max(1, rowsTotal - 4);
            ScrollToCursor();

            Dos.Fill(g, ClientRectangle, Dos.Blue);
            g.SetClip(new Rectangle(0, 0, cols * cw, rowsTotal * ch));

            // нижняя линия рамки проходит по середине последней строки —
            // тогда надпись в ней разрывает линию, как заголовок сверху
            var fr = new Rectangle(cw / 2, ch / 2,
                                   (cols - 1) * cw - cw / 2, (rowsTotal - 1) * ch);
            Dos.Frame2(g, fr, Dos.Cyan);

            var lay = Layout();

            // вертикальные разделители колонок
            int sepTop = ch, sepBot = (rowsTotal - 2) * ch - 1;
            for (int i = 1; i < Cols.Length; i++)
                Dos.VLine(g, (lay[i] - 1) * cw + cw / 2, sepTop, sepBot, Dos.Cyan);

            // линия под заголовками и над строкой состояния
            Dos.HLine(g, fr.X + 1, fr.Right - 1, 2 * ch - 1, Dos.Cyan);
            Dos.HLine(g, fr.X + 1, fr.Right - 1, (rowsTotal - 2) * ch - 1, Dos.Cyan);

            // путь в верхней рамке
            string title = " " + Dos.Cut(Title, Math.Max(4, cols - 8)) + " ";
            int tx = Math.Max(cw, (cols - title.Length) / 2 * cw);
            Dos.Text(g, tx, 0, title, _active ? Dos.Black : Dos.Cyan,
                     _active ? Dos.Cyan : Dos.Blue);

            // заголовки колонок
            for (int i = 0; i < Cols.Length; i++)
            {
                int w = ColWidth(i, lay);
                string t = Cols[i].Title;
                if (i == _sortCol) t = (_sortDesc ? "↑" : "↓") + t;
                t = Dos.Cut(t, w);
                int x = Cols[i].Right ? (lay[i] + w - t.Length) * cw : lay[i] * cw;
                Dos.Text(g, x, ch, t, Dos.White, Dos.Blue);
            }

            // строки списка
            for (int i = 0; i < _visible; i++)
            {
                int idx = _top + i;
                if (idx >= Rows.Count) break;
                var r = Rows[idx];
                int y = (2 + i) * ch;
                bool cur = _active && idx == _cursor;
                Color bg = cur ? Dos.Cyan : Dos.Blue;
                Color fg = r.Marked ? Dos.Yellow : (cur ? Dos.Black : r.Color);
                if (cur) Dos.Fill(g, cw, y, (cols - 3) * cw, ch, bg);
                for (int c = 0; c < Cols.Length && c < r.Cells.Length; c++)
                {
                    int w = ColWidth(c, lay);
                    string t = Dos.Cut(r.Cells[c], w);
                    int x = Cols[c].Right ? (lay[c] + w - t.Length) * cw : lay[c] * cw;
                    Dos.Text(g, x, y, t, fg, bg);
                }
            }

            // полоса прокрутки в правой рамке
            if (Rows.Count > _visible)
            {
                int trackTop = 2 * ch, trackH = _visible * ch;
                int thumbH = Math.Max(ch / 2, trackH * _visible / Rows.Count);
                int thumbY = trackTop + (int)((long)trackH * _top / Rows.Count);
                Dos.Fill(g, fr.Right - 1, thumbY, 3, thumbH, Dos.LightCyan);
            }

            // строка состояния: файл под курсором — на всю ширину панели
            int sy = (rowsTotal - 2) * ch;
            Dos.Text(g, cw, sy, Dos.Cut(StatusText(), cols - 3), Dos.White, Dos.Blue);

            // сводка (свободно / всего) — в нижней рамке, как в NC:
            // так она не сталкивается со строкой состояния
            string info = Dos.Cut(Info, Math.Max(0, cols - 8));
            if (info.Length > 0)
            {
                string box = " " + info + " ";
                int ix = Math.Max(cw, (cols - box.Length) / 2 * cw);
                Dos.Text(g, ix, (rowsTotal - 1) * ch, box, Dos.Yellow, Dos.Blue);
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  Панель Windows
    // ══════════════════════════════════════════════════════════════════
    internal class WinPanel : NcPanel
    {
        public string CurrentDir { get; private set; }

        class Node { public string Path; public bool IsDir, IsDrive, IsUp; }

        public WinPanel()
        {
            Cols = new[]
            {
                new NcColumn { Title = "Имя",    Width = 0 },
                new NcColumn { Title = "Размер", Width = 10, Right = true },
                new NcColumn { Title = "Дата",   Width = 8,  Right = true },
                new NcColumn { Title = "Время",  Width = 6,  Right = true },
            };
            CurrentDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            OnEnterItem += Enter_;
        }

        public void Refresh_() { Refresh_(null); }

        public void Refresh_(string keep)
        {
            var rows = new List<NcRow>();
            Title = CurrentDir ?? "Компьютер";
            if (CurrentDir == null)
            {
                foreach (var d in DriveInfo.GetDrives())
                    rows.Add(new NcRow
                    {
                        Cells = new[] { d.Name, "►DRIVE◄", "", "" },
                        IsDir = true,
                        Color = Dos.White,
                        Tag = new Node { IsDrive = true, Path = d.Name }
                    });
                SetRows(rows, keep);
                return;
            }

            rows.Add(new NcRow
            {
                Cells = new[] { "..", "►UP--DIR◄", "", "" },
                IsDir = true,
                IsUp = true,
                Color = Dos.White,
                Tag = new Node { IsUp = true }
            });
            try
            {
                foreach (var dir in Directory.GetDirectories(CurrentDir))
                {
                    var di = new DirectoryInfo(dir);
                    rows.Add(new NcRow
                    {
                        Cells = new[] { di.Name, "►SUB-DIR◄",
                                        Dos.DosDate(di.LastWriteTime), Dos.DosTime(di.LastWriteTime) },
                        Keys = new IComparable[] { di.Name, 0L, di.LastWriteTime, di.LastWriteTime },
                        IsDir = true,
                        Color = Dos.White,
                        Tag = new Node { Path = dir, IsDir = true }
                    });
                }
                foreach (var f in Directory.GetFiles(CurrentDir))
                {
                    var fi = new FileInfo(f);
                    rows.Add(new NcRow
                    {
                        Cells = new[] { fi.Name, fi.Length.ToString(),
                                        Dos.DosDate(fi.LastWriteTime), Dos.DosTime(fi.LastWriteTime) },
                        Keys = new IComparable[] { fi.Name, fi.Length, fi.LastWriteTime, fi.LastWriteTime },
                        Color = Dos.Gray,
                        Tag = new Node { Path = f }
                    });
                }
            }
            catch (Exception ex) { Title = "[нет доступа] " + ex.Message; }
            SetRows(rows, keep);
        }

        void Enter_()
        {
            var r = Current;
            if (r == null) return;
            var n = (Node)r.Tag;
            if (n.IsUp)
            {
                string keep = CurrentDir != null ? Path.GetFileName(CurrentDir.TrimEnd('\\')) : null;
                var parent = CurrentDir != null ? Directory.GetParent(CurrentDir) : null;
                CurrentDir = parent != null ? parent.FullName : null;
                Refresh_(keep);
            }
            else if (n.IsDrive || n.IsDir)
            {
                CurrentDir = n.Path;
                Refresh_(null);
            }
        }

        protected override string StatusText()
        {
            var r = Current;
            if (r == null) return "";
            return r.Cells[0] + "  " + r.Cells[1] + "  " + r.Cells[2] + " " + r.Cells[3];
        }

        public string SelectedFilePath()
        {
            var r = Current;
            if (r == null) return null;
            var n = (Node)r.Tag;
            return (n.IsDir || n.IsDrive || n.IsUp) ? null : n.Path;
        }

        public List<string> SelectedFilePaths()
        {
            var list = new List<string>();
            foreach (var r in Targets())
            {
                var n = (Node)r.Tag;
                if (!n.IsDir && !n.IsDrive && !n.IsUp) list.Add(n.Path);
            }
            return list;
        }

        // для F8 — файлы и папки (диски и ".." не трогаем)
        public List<string> SelectedDeletablePaths()
        {
            var list = new List<string>();
            foreach (var r in Targets())
            {
                var n = (Node)r.Tag;
                if (!n.IsDrive && !n.IsUp) list.Add(n.Path);
            }
            return list;
        }

        public bool RenameSelected(string newName, out string error)
        {
            error = null;
            var r = Current;
            if (r == null) { error = "ничего не выбрано"; return false; }
            var n = (Node)r.Tag;
            if (n.IsUp || n.IsDrive) { error = "это нельзя переименовать"; return false; }
            string dst = Path.Combine(Path.GetDirectoryName(n.Path) ?? CurrentDir, newName);
            try
            {
                if (n.IsDir) Directory.Move(n.Path, dst);
                else File.Move(n.Path, dst);
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  Панель образа .dsk
    // ══════════════════════════════════════════════════════════════════
    internal class DskPanel : NcPanel
    {
        DskImage _img;                 // открытый том (.dsk или раздел винта)
        HddImage _hdd;                 // открытый винчестер УКНЦ, иначе null
        int _part = -1;                // номер раздела; -1 — показан список разделов

        public event Action OnViewFile;    // Enter на файле тома

        public bool HasImage { get { return _img != null; } }
        public bool HasHdd { get { return _hdd != null; } }
        public bool InPartitionList { get { return _hdd != null && _part < 0; } }

        public string ImagePath
        {
            get { return _img != null ? _img.Path : (_hdd != null ? _hdd.Path : null); }
        }

        // что подсказать, когда тома нет
        public string NoVolumeHint()
        {
            return _hdd != null
                ? "Выберите раздел винчестера: Enter."
                : "Сначала откройте образ (F7).";
        }

        public DskPanel()
        {
            Cols = new[]
            {
                new NcColumn { Title = "Имя",    Width = 0 },
                new NcColumn { Title = "Блоков", Width = 8, Right = true },
                new NcColumn { Title = "Байт",   Width = 9, Right = true },
            };
            Title = "Образ .dsk — F7 открыть";
            OnEnterItem += Enter_;
        }

        public void Open(string path) { Open(path, false, 0); }
        public void Open(string path, bool forceHdd) { Open(path, forceHdd, 0); }

        // polarity: 0 — сама, 1 — как есть, 2 — инверсно
        public void Open(string path, bool forceHdd, int polarity)
        {
            try
            {
                _hdd = HddImage.TryOpen(path, forceHdd || polarity != 0, polarity);
                _part = -1;
                _img = _hdd != null ? null : new DskImage(path);
                Refresh_(null);
            }
            catch (Exception ex)
            {
                _hdd = null; _img = null; _part = -1;
                DosUi.Msg(FindForm(), "Не открыть образ:\n" + ex.Message, "Ошибка");
            }
        }

        // Enter: в списке разделов — войти в раздел, в томе винчестера
        // ".." — вернуться к списку, на файле — отдать просмотр наружу.
        void Enter_()
        {
            var r = Current;
            if (r == null) return;
            if (InPartitionList)
            {
                if (r.Tag is int) OpenPartition((int)r.Tag);
                return;
            }
            if (r.IsUp) { _part = -1; _img = null; Refresh_(null); return; }
            if (OnViewFile != null) OnViewFile();
        }

        public void OpenPartition(int index)
        {
            if (_hdd == null) return;
            try
            {
                _img = _hdd.OpenPartition(index);
                _part = index;
                Refresh_(null);
            }
            catch (Exception ex)
            {
                _img = null; _part = -1;
                DosUi.Msg(FindForm(), "Не открыть раздел:\n" + ex.Message, "Ошибка");
            }
        }

        public void OpenDialog(Action<string> remember) { OpenDialog(remember, false, 0); }
        public void OpenDialog(Action<string> remember, bool forceHdd) { OpenDialog(remember, forceHdd, 0); }

        public void OpenDialog(Action<string> remember, bool forceHdd, int polarity)
        {
            using (var dlg = new OpenFileDialog
            {
                Filter = "Все файлы (*.*)|*.*|Образы и винчестеры (*.dsk;*.hdd;*.img)|*.dsk;*.hdd;*.img"
            })
                if (dlg.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    Open(dlg.FileName, forceHdd, polarity);
                    if (remember != null) remember(dlg.FileName);
                }
        }

        public void Refresh_() { Refresh_(null); }

        public void Refresh_(string keep)
        {
            var rows = new List<NcRow>();

            if (_img == null && _hdd == null)
            {
                Title = "Образ .dsk — F7 открыть";
                Info = "";
                SetRows(rows, null);
                return;
            }

            // список разделов винчестера
            if (InPartitionList)
            {
                foreach (var p in _hdd.Parts)
                    rows.Add(new NcRow
                    {
                        Cells = new[] { "Раздел " + p.Index, p.Blocks.ToString(), ((long)p.Blocks * 512).ToString() },
                        Keys = new IComparable[] { (long)p.Index, (long)p.Blocks, (long)p.Blocks * 512 },
                        IsDir = true,
                        Color = Dos.White,
                        Tag = p.Index
                    });
                Title = Path.GetFileName(_hdd.Path) + (_hdd.Inverted ? " — разделы (инверсный)" : " — разделы");
                Info = "разделов " + _hdd.Parts.Count + ", всего " + _hdd.TotalBlocks + " бл.";
                SetRows(rows, keep);
                return;
            }

            // выход к списку разделов
            if (_hdd != null)
                rows.Add(new NcRow
                {
                    Cells = new[] { "..", "\u25BAРАЗДЕЛЫ\u25C4", "" },
                    IsDir = true,
                    IsUp = true,
                    Color = Dos.White
                });

            foreach (var e in _img.ReadDir())
            {
                if (e.Empty) continue;
                rows.Add(new NcRow
                {
                    Cells = new[] { e.Name, e.Blocks.ToString(), (e.Blocks * 512).ToString() },
                    Keys = new IComparable[] { e.Name, (long)e.Blocks, (long)e.Blocks * 512 },
                    Color = Dos.Gray
                });
            }
            Title = _hdd != null
                ? Path.GetFileName(_hdd.Path) + "  [раздел " + _part + "]"
                : Path.GetFileName(_img.Path);
            int free = _img.FreeBlocks();
            Info = "свободно " + free + " бл. (" + (free * 512 / 1024) + " КБ)";
            SetRows(rows, keep);
        }

        protected override string StatusText()
        {
            if (InPartitionList)
            {
                var pr = Current;
                if (pr == null) return "разделов нет";
                return pr.Cells[0] + "  " + pr.Cells[1] + " бл.  " + pr.Cells[2] + " байт";
            }
            if (_img == null) return "образ не открыт";
            var r = Current;
            if (r == null) return "файлов нет";
            return r.Cells[0] + "  " + r.Cells[1] + " бл.  " + r.Cells[2] + " байт";
        }

        public string SelectedName()
        {
            if (InPartitionList) return null;
            var r = Current;
            return (r == null || r.IsUp) ? null : r.Cells[0];
        }

        public List<string> SelectedNames()
        {
            var list = new List<string>();
            if (InPartitionList) return list;
            foreach (var r in Targets()) if (!r.IsUp) list.Add(r.Cells[0]);
            return list;
        }

        // переоткрыть тот же файл с заданной полярностью
        public void Reopen(int polarity)
        {
            string path = ImagePath;
            if (!string.IsNullOrEmpty(path)) Open(path, true, polarity);
        }

        public void ShowInfo(IWin32Window owner)
        {
            // в списке разделов — сводка по винчестеру
            if (InPartitionList)
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("Винчестер: ").Append(Path.GetFileName(_hdd.Path)).Append("\n");
                sb.Append("Запись: ").Append(_hdd.Inverted ? "инверсная" : "обычная").Append("\n");
                sb.Append("Всего: ").Append(_hdd.TotalBlocks).Append(" бл. (")
                  .Append(_hdd.TotalBlocks / 2).Append(" КБ)\n\n");
                foreach (var p in _hdd.Parts)
                    sb.Append(string.Format("Раздел {0}:  блок {1,6}  длина {2,5} бл. ({3,5} КБ)\n",
                                            p.Index, p.StartBlock, p.Blocks, p.Blocks / 2));
                DosUi.Msg(owner, sb.ToString().TrimEnd(), "Винчестер УКНЦ");
                return;
            }
            if (_img == null) return;
            int total = _img.TotalBlocks, free = _img.FreeBlocks();
            string name = _hdd != null
                ? Path.GetFileName(_hdd.Path) + " [раздел " + _part + "]"
                : Path.GetFileName(_img.Path);
            DosUi.DiskInfo(owner, name, total, total - free, free);
        }

        public byte[] Extract(string n) { return _img == null ? null : _img.Extract(n); }
        public bool Add(string n, byte[] b) { return _img != null && _img.AddFile(n, b); }
        public void Delete(string n) { if (_img != null) _img.DeleteFile(n); }
        public bool NeedsSqueeze() { return _img != null && _img.NeedsSqueeze(); }
        public int Squeeze() { return _img == null ? 0 : _img.Squeeze(); }

        public bool Rename(string oldName, string newName, out string error)
        {
            error = null;
            if (_img == null) { error = "образ не открыт"; return false; }
            if (!_img.RenameFile(oldName, newName)) { error = "имя занято или файл не найден"; return false; }
            return true;
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  Верхнее меню (F9), нижний ряд F-клавиш, строка пути
    // ══════════════════════════════════════════════════════════════════
    internal class DosMenuBar : Control
    {
        readonly List<string> _titles = new List<string>();
        readonly List<ContextMenuStrip> _menus = new List<ContextMenuStrip>();
        int _open = -1;

        public DosMenuBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = Dos.CH;
            BackColor = Dos.Cyan;
            Font = Dos.Font;
        }

        public void Add(string title, ContextMenuStrip menu)
        {
            menu.Font = Dos.Font;
            menu.Renderer = new DosMenuRenderer();
            menu.ShowImageMargin = false;
            menu.Closed += (s, e) => { _open = -1; Invalidate(); };
            _titles.Add(title);
            _menus.Add(menu);
            Invalidate();
        }

        int ItemStart(int i)
        {
            int x = 2;
            for (int k = 0; k < i; k++) x += _titles[k].Length + 2;
            return x;
        }

        public void OpenFirst() { Drop(0); }

        void Drop(int i)
        {
            if (i < 0 || i >= _menus.Count) return;
            _open = i;
            Invalidate();
            _menus[i].Show(this, new Point(ItemStart(i) * Dos.CW, Height));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int cell = e.X / Dos.CW;
            for (int i = 0; i < _titles.Count; i++)
                if (cell >= ItemStart(i) - 1 && cell <= ItemStart(i) + _titles[i].Length)
                { Drop(i); return; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Dos.Fill(g, ClientRectangle, Dos.Cyan);
            for (int i = 0; i < _titles.Count; i++)
            {
                bool on = i == _open;
                Dos.Text(g, ItemStart(i) * Dos.CW, 0, _titles[i],
                         on ? Dos.Yellow : Dos.Black, on ? Dos.Black : Dos.Cyan);
            }
        }
    }

    // серые выпадающие меню, как в DOS
    internal class DosMenuRenderer : ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        { Dos.Fill(e.Graphics, e.AffectedBounds, Dos.Gray); }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            var r = e.AffectedBounds;
            using (var p = new Pen(Dos.Black, 1))
                e.Graphics.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var r = new Rectangle(Point.Empty, e.Item.Size);
            Dos.Fill(e.Graphics, r, e.Item.Selected ? Dos.Cyan : Dos.Gray);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Dos.Black : Dos.DarkGray;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var r = e.Item.Bounds;
            Dos.HLine(e.Graphics, 2, r.Width - 3, r.Height / 2, Dos.DarkGray);
        }
    }

    internal class FKeyBar : Control
    {
        public string[] Labels = new string[10];
        public Action<int> Pressed;

        public FKeyBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = Dos.CH;
            BackColor = Dos.Blue;
            Font = Dos.Font;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int cell = ClientSize.Width / 10;
            if (cell > 0 && Pressed != null) Pressed(Math.Min(9, e.X / cell) + 1);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Dos.Fill(g, ClientRectangle, Dos.Blue);
            int cell = ClientSize.Width / 10;
            if (cell < Dos.CW * 3) return;
            for (int i = 0; i < 10; i++)
            {
                int x = i * cell;
                string num = (i + 1).ToString();
                Dos.Text(g, x, 0, num, Dos.White, Dos.Blue);
                int lx = x + num.Length * Dos.CW;
                int lw = Math.Max(1, (cell - num.Length * Dos.CW) / Dos.CW - 1);
                string lab = Dos.Cut(Labels[i] ?? "", lw).PadRight(lw);
                Dos.Text(g, lx, 0, lab, Dos.Black, Dos.Cyan);
            }
        }
    }

    // строка пути над рядом F-клавиш — приглашение DOS
    internal class PathLine : Control
    {
        string _text = "";
        public void SetText(string s) { _text = s ?? ""; Invalidate(); }

        public PathLine()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Height = Dos.CH;
            BackColor = Dos.Blue;
            Font = Dos.Font;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Dos.Fill(e.Graphics, ClientRectangle, Dos.Blue);
            int cells = Math.Max(1, ClientSize.Width / Dos.CW);
            Dos.Text(e.Graphics, 0, 0, Dos.Cut(_text + ">", cells), Dos.Gray, Dos.Blue);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  Главное окно
    // ══════════════════════════════════════════════════════════════════
    public class DskDialog : Form
    {
        WinPanel _left;
        DskPanel _right;
        DosMenuBar _menu;
        FKeyBar _fkeys;
        PathLine _path;
        Panel _body;
        bool _leftActive = true;

        static string LastDiskFile
        {
            get
            {
                return Path.Combine(
                    Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                    "last_disk.txt");
            }
        }

        public DskDialog() : this(null) { }

        public DskDialog(string startPath)
        {
            Text = "DSK Commander";
            ClientSize = new Size(Dos.CW * 104, Dos.CH * 27);
            MinimumSize = new Size(Dos.CW * 70, Dos.CH * 16);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Dos.Blue;
            KeyPreview = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            _menu = new DosMenuBar { Dock = DockStyle.Top };
            _fkeys = new FKeyBar { Dock = DockStyle.Bottom };
            _path = new PathLine { Dock = DockStyle.Bottom };

            _body = new Panel { Dock = DockStyle.Fill, BackColor = Dos.Blue };
            _left = new WinPanel { Dock = DockStyle.Left };
            _right = new DskPanel { Dock = DockStyle.Fill };
            _body.Controls.Add(_right);
            _body.Controls.Add(_left);
            _body.Resize += (s, e) => { _left.Width = _body.ClientSize.Width / 2; };

            Controls.Add(_body);
            Controls.Add(_path);
            Controls.Add(_fkeys);
            Controls.Add(_menu);

            _fkeys.Labels = new[]
            {
                "Помощь", "Новый", "Просмотр", "Инфо", "Копия",
                "Имя", "Образ", "Удалить", "Меню", "Сжать"
            };
            _fkeys.Pressed = FKey;

            BuildMenu();

            _left.OnActivate += () => SetActive(true);
            _right.OnActivate += () => SetActive(false);
            _left.OnEnterItem += UpdatePath;
            _right.OnViewFile += DoView;

            Load += (s, e) =>
            {
                _left.Width = _body.ClientSize.Width / 2;
                _left.Refresh_();
                string last = (!string.IsNullOrEmpty(startPath) && File.Exists(startPath))
                    ? startPath : RecallPath();
                if (!string.IsNullOrEmpty(last) && File.Exists(last))
                {
                    _right.Open(last);
                    if (last == startPath) RememberPath(last);
                }
                SetActive(true);
            };

            KeyDown += OnKey;
        }

        void BuildMenu()
        {
            var left = new ContextMenuStrip();
            left.Items.Add("Сортировать по имени", null, (s, e) => _left.SortBy(0));
            left.Items.Add("Сортировать по размеру", null, (s, e) => _left.SortBy(1));
            left.Items.Add("Сортировать по дате", null, (s, e) => _left.SortBy(2));
            left.Items.Add(new ToolStripSeparator());
            left.Items.Add("Перечитать      Ctrl+R", null, (s, e) => _left.Refresh_());

            var files = new ContextMenuStrip();
            files.Items.Add("Просмотр            F3", null, (s, e) => DoView());
            files.Items.Add("Копировать          F5", null, (s, e) => DoCopy());
            files.Items.Add("Переименовать       F6", null, (s, e) => DoRename());
            files.Items.Add("Удалить             F8", null, (s, e) => DoDelete());
            files.Items.Add(new ToolStripSeparator());
            files.Items.Add("Пометить всё    Ctrl+A", null, (s, e) => Active().MarkAll());
            files.Items.Add("Снять пометки   Ctrl+U", null, (s, e) => Active().UnmarkAll());

            var disk = new ContextMenuStrip();
            disk.Items.Add("Открыть образ       F7", null, (s, e) => _right.OpenDialog(RememberPath));
            disk.Items.Add("Открыть как винчестер", null, (s, e) => _right.OpenDialog(RememberPath, true));
            disk.Items.Add("Открыть винчестер: как есть", null, (s, e) => _right.OpenDialog(RememberPath, true, 1));
            disk.Items.Add("Открыть винчестер: инверсный", null, (s, e) => _right.OpenDialog(RememberPath, true, 2));
            disk.Items.Add("Перечитать как есть", null, (s, e) => _right.Reopen(1));
            disk.Items.Add("Перечитать инверсно", null, (s, e) => _right.Reopen(2));
            disk.Items.Add(new ToolStripSeparator());
            disk.Items.Add("Сведения о файле образа", null, (s, e) => DoProbe());
            disk.Items.Add("Новый образ         F2", null, (s, e) => DoNew());
            disk.Items.Add("Информация          F4", null, (s, e) => DoInfo());
            disk.Items.Add("Сжать образ        F10", null, (s, e) => DoSqueeze());

            var cmd = new ContextMenuStrip();
            cmd.Items.Add("О программе         F1", null, (s, e) => DoAbout());
            cmd.Items.Add(new ToolStripSeparator());
            cmd.Items.Add("Выход              Esc", null, (s, e) => Close());

            var right = new ContextMenuStrip();
            right.Items.Add("Сортировать по имени", null, (s, e) => _right.SortBy(0));
            right.Items.Add("Сортировать по блокам", null, (s, e) => _right.SortBy(1));
            right.Items.Add(new ToolStripSeparator());
            right.Items.Add("Перечитать      Ctrl+R", null, (s, e) => _right.Refresh_());

            _menu.Add("Левая", left);
            _menu.Add("Файлы", files);
            _menu.Add("Диск", disk);
            _menu.Add("Команды", cmd);
            _menu.Add("Правая", right);
        }

        NcPanel Active() { return _leftActive ? (NcPanel)_left : _right; }

        void SetActive(bool left)
        {
            _leftActive = left;
            _left.SetActive(left);
            _right.SetActive(!left);
            UpdatePath();
        }

        void UpdatePath()
        {
            _path.SetText(_leftActive
                ? (_left.CurrentDir ?? "Компьютер")
                : (_right.HasImage ? "DSK:" + Path.GetFileName(_right.ImagePath) : "DSK:"));
        }

        void FKey(int n)
        {
            switch (n)
            {
                case 1: DoAbout(); break;
                case 2: DoNew(); break;
                case 3: DoView(); break;
                case 4: DoInfo(); break;
                case 5: DoCopy(); break;
                case 6: DoRename(); break;
                case 7: _right.OpenDialog(RememberPath); break;
                case 8: DoDelete(); break;
                case 9: _menu.OpenFirst(); break;
                case 10: DoSqueeze(); break;
            }
        }

        // F10 Windows отдаёт как системную клавишу — ловим здесь
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F10) { DoSqueeze(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Tab: SetActive(!_leftActive); e.Handled = true; break;
                case Keys.Escape: Close(); break;
                case Keys.F1: DoAbout(); e.Handled = true; break;
                case Keys.F2: DoNew(); e.Handled = true; break;
                case Keys.F3: DoView(); e.Handled = true; break;
                case Keys.F4: DoInfo(); e.Handled = true; break;
                case Keys.F5: DoCopy(); e.Handled = true; break;
                case Keys.F6: DoRename(); e.Handled = true; break;
                case Keys.F7: _right.OpenDialog(RememberPath); e.Handled = true; break;
                case Keys.F8: DoDelete(); e.Handled = true; break;
                case Keys.F9: _menu.OpenFirst(); e.Handled = true; break;
                case Keys.A:
                    if (e.Control) { Active().MarkAll(); e.Handled = true; }
                    break;
                case Keys.U:
                    if (e.Control) { Active().UnmarkAll(); e.Handled = true; }
                    break;
                case Keys.R:
                    if (e.Control) { _left.Refresh_(); _right.Refresh_(); e.Handled = true; }
                    break;
            }
        }

        // ── F5: копирование ──
        void DoCopy()
        {
            if (_leftActive)
            {
                var paths = _left.SelectedFilePaths();
                if (paths.Count == 0) { Beep(); return; }
                if (!_right.HasImage) { Msg(_right.NoVolumeHint()); return; }
                int ok = 0;
                var failed = new List<string>();
                foreach (var path in paths)
                {
                    var bytes = File.ReadAllBytes(path);
                    string nm = RtName(Path.GetFileName(path));
                    if (_right.Add(nm, bytes)) ok++; else failed.Add(nm);
                }
                _right.Refresh_();
                if (failed.Count > 0)
                    Msg("Скопировано: " + ok + ".\nНе удалось: " + failed.Count +
                        " (" + string.Join(", ", failed) + ").");
            }
            else
            {
                var names = _right.SelectedNames();
                if (names.Count == 0 || !_right.HasImage) { Beep(); return; }
                int ok = 0;
                var failed = new List<string>();
                foreach (var name in names)
                {
                    var bytes = _right.Extract(name);
                    if (bytes == null) { failed.Add(name); continue; }
                    File.WriteAllBytes(Path.Combine(_left.CurrentDir ?? ".", name), bytes);
                    ok++;
                }
                _left.Refresh_();
                if (failed.Count > 0)
                    Msg("Скопировано: " + ok + ".\nНе удалось: " + failed.Count +
                        " (" + string.Join(", ", failed) + ").");
            }
        }

        // ── F8: удаление ──
        void DoDelete()
        {
            if (_leftActive) { DeleteWindows(); return; }
            var names = _right.SelectedNames();
            if (names.Count == 0 || !_right.HasImage) { Beep(); return; }
            string q = names.Count == 1
                ? "Удалить " + names[0] + " из образа?"
                : "Удалить " + names.Count + " файлов из образа?\n\n" + string.Join(", ", names);
            if (!DosUi.Confirm(this, q, "Удаление")) return;
            foreach (var n in names) _right.Delete(n);
            _right.Refresh_();
        }

        void DeleteWindows()
        {
            var paths = _left.SelectedDeletablePaths();
            if (paths.Count == 0) { Beep(); return; }
            string q = paths.Count == 1
                ? "Удалить в корзину:\n" + Path.GetFileName(paths[0]) + " ?"
                : "Удалить в корзину " + paths.Count + " объектов?\n\n" +
                  string.Join(", ", paths.ConvertAll(p => Path.GetFileName(p)));
            if (!DosUi.Confirm(this, q, "Удаление")) return;

            var failed = new List<string>();
            foreach (var p in paths)
            {
                try
                {
                    if (Directory.Exists(p))
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(p,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    else
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(p,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) { failed.Add(Path.GetFileName(p) + " — " + ex.Message); }
            }
            _left.Refresh_();
            if (failed.Count > 0) Msg("Не удалось удалить:\n" + string.Join("\n", failed));
        }

        // ── F6: переименование ──
        void DoRename()
        {
            if (_leftActive)
            {
                string path = _left.SelectedFilePath();
                if (path == null) { Beep(); return; }
                string oldName = Path.GetFileName(path);
                string newName = DosUi.Prompt(this, "Переименование", "Новое имя:", oldName);
                if (newName == null || newName == oldName) return;
                string err;
                if (!_left.RenameSelected(newName, out err)) { Msg("Не удалось: " + err); return; }
                _left.Refresh_(newName);
            }
            else
            {
                string oldName = _right.SelectedName();
                if (oldName == null || !_right.HasImage) { Beep(); return; }
                string newName = DosUi.Prompt(this, "Переименование",
                                              "Новое имя (RT-11: 6 симв. + 3):", oldName);
                if (newName == null) return;
                newName = RtName(newName);
                if (newName == oldName) return;
                string err;
                if (!_right.Rename(oldName, newName, out err)) { Msg("Не удалось: " + err); return; }
                _right.Refresh_(newName);
            }
        }

        // ── F3: просмотр ──
        void DoView()
        {
            byte[] data;
            string title;
            if (_leftActive)
            {
                string p = _left.SelectedFilePath();
                if (p == null) { Beep(); return; }
                data = File.ReadAllBytes(p);
                title = Path.GetFileName(p);
            }
            else
            {
                string n = _right.SelectedName();
                if (n == null || !_right.HasImage) { Beep(); return; }
                data = _right.Extract(n);
                title = n;
            }
            if (data != null)
                using (var v = new ViewerDialog(title, data)) v.ShowDialog(this);
        }

        // ── F4: информация об образе ──
        void DoInfo()
        {
            if (!_right.HasImage && !_right.HasHdd) { Msg(_right.NoVolumeHint()); return; }
            _right.ShowInfo(this);
        }

        // ── F2: новый образ ──
        void DoNew()
        {
            int? blocks = DosUi.AskImageSize(this);
            if (blocks == null) return;
            using (var dlg = new SaveFileDialog
            {
                Filter = "Образы (*.dsk)|*.dsk|Все (*.*)|*.*",
                DefaultExt = "dsk",
                FileName = "new.dsk",
                InitialDirectory = _left.CurrentDir ?? "",
                Title = "Новый образ"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try { DskImage.CreateBlank(dlg.FileName, blocks.Value); }
                catch (Exception ex) { Msg("Не удалось создать образ:\n" + ex.Message); return; }
                _right.Open(dlg.FileName);
                RememberPath(dlg.FileName);
                _left.Refresh_();
                SetActive(false);
            }
        }

        // ── F10: сжатие ──
        void DoSqueeze()
        {
            if (!_right.HasImage) { Msg(_right.NoVolumeHint()); return; }
            if (!_right.NeedsSqueeze()) { Msg("Образ уже сжат: свободное место одним куском в конце."); return; }
            if (!DosUi.Confirm(this,
                "Сжать образ?\n\nФайлы сдвинутся к началу диска, свободное место\n" +
                "соберётся одним куском в конце.", "Сжатие")) return;
            int moved;
            try { moved = _right.Squeeze(); }
            catch (Exception ex) { Msg("Ошибка сжатия:\n" + ex.Message); return; }
            _right.Refresh_();
            Msg("Готово. Перемещено файлов: " + moved + ".");
        }

        // ── «Сведения о файле образа»: что именно программа увидела
        //    в файле. Нужно, когда образ не открывается — по этому тексту
        //    видно, где разошлись ожидания и реальность. ──
        void DoProbe()
        {
            string path = _right.ImagePath;
            if (string.IsNullOrEmpty(path))
            {
                using (var dlg = new OpenFileDialog { Filter = "Все файлы (*.*)|*.*", Title = "Файл образа" })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    path = dlg.FileName;
                }
            }
            string text;
            try { text = ImageProbe.Describe(path); }
            catch (Exception ex) { text = "Ошибка разбора:\n" + ex; }
            using (var v = new ViewerDialog("Сведения: " + Path.GetFileName(path), text))
                v.ShowDialog(this);
        }

        void DoAbout() { using (var a = new AboutDialog()) a.ShowDialog(this); }

        static string RtName(string fn)
        {
            string nm = Path.GetFileNameWithoutExtension(fn).ToUpper();
            string ex = Path.GetExtension(fn).TrimStart('.').ToUpper();
            if (nm.Length > 6) nm = nm.Substring(0, 6);
            if (ex.Length > 3) ex = ex.Substring(0, 3);
            return ex.Length > 0 ? nm + "." + ex : nm;
        }

        void RememberPath(string p) { try { File.WriteAllText(LastDiskFile, p); } catch { } }
        string RecallPath() { try { if (File.Exists(LastDiskFile)) return File.ReadAllText(LastDiskFile).Trim(); } catch { } return null; }
        void Beep() { System.Media.SystemSounds.Beep.Play(); }
        void Msg(string s) { DosUi.Msg(this, s, "DSK Commander"); }
    }

    // ══════════════════════════════════════════════════════════════════
    //  Диалоги DOS: серый ящик с двойной рамкой и заголовком в рамке
    // ══════════════════════════════════════════════════════════════════
    internal class DosBox : Form
    {
        public string Caption;
        Point _drag;
        bool _dragging;

        public DosBox(string caption, int w, int h)
        {
            Caption = caption;
            FormBorderStyle = FormBorderStyle.None;
            ClientSize = new Size(w, h);
            BackColor = Dos.Gray;
            ForeColor = Dos.Black;
            Font = Dos.Font;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            KeyPreview = true;
            DoubleBuffered = true;
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            int W = ClientSize.Width, H = ClientSize.Height;
            Dos.Fill(g, ClientRectangle, Dos.Gray);
            Dos.Frame2(g, new Rectangle(4, 4, W - 9, H - 9), Dos.Black);
            string t = " " + Caption + " ";
            int x = (W - t.Length * Dos.CW) / 2;
            Dos.Text(g, x, 1, t, Dos.Black, Dos.Gray);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Y < Dos.CH) { _dragging = true; _drag = e.Location; }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging)
                Location = new Point(Location.X + e.X - _drag.X, Location.Y + e.Y - _drag.Y);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
        }
    }

    internal class DosButton : Button
    {
        public DosButton(string text, DialogResult dr)
        {
            Text = text;
            DialogResult = dr;
            FlatStyle = FlatStyle.Flat;
            BackColor = Dos.Gray;
            ForeColor = Dos.Black;
            Font = Dos.Font;
            Height = Dos.CH + 8;
            Width = Math.Max(80, (text.Length + 4) * Dos.CW);
            FlatAppearance.BorderColor = Dos.Black;
            FlatAppearance.BorderSize = 1;
            Cursor = Cursors.Hand;
            GotFocus += (s, e) => { BackColor = Dos.Yellow; };
            LostFocus += (s, e) => { BackColor = Dos.Gray; };
        }
    }

    internal static class DosUi
    {
        static Label Lab(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Left = x,
                Top = y,
                AutoSize = true,
                ForeColor = Dos.Black,
                BackColor = Dos.Gray,
                Font = Dos.Font
            };
        }

        static int LineCount(string s) { return s.Replace("\r\n", "\n").Split('\n').Length; }

        static int MaxLine(string s)
        {
            int m = 0;
            foreach (var l in s.Replace("\r\n", "\n").Split('\n')) m = Math.Max(m, l.Length);
            return m;
        }

        public static void Msg(IWin32Window owner, string text, string title = "Сообщение")
        {
            int w = Math.Max(34, MaxLine(text) + 6) * Dos.CW;
            int h = (LineCount(text) + 4) * Dos.CH + 24;
            using (var f = new DosBox(title, w, h))
            {
                f.Controls.Add(Lab(text, 3 * Dos.CW, 2 * Dos.CH));
                var ok = new DosButton("OK", DialogResult.OK);
                ok.Left = (w - ok.Width) / 2;
                ok.Top = h - ok.Height - Dos.CH;
                f.Controls.Add(ok);
                f.AcceptButton = ok;
                f.CancelButton = ok;
                f.ShowDialog(owner);
            }
        }

        public static bool Confirm(IWin32Window owner, string text, string title = "Подтверждение")
        {
            int w = Math.Max(40, MaxLine(text) + 6) * Dos.CW;
            int h = (LineCount(text) + 4) * Dos.CH + 24;
            using (var f = new DosBox(title, w, h))
            {
                f.Controls.Add(Lab(text, 3 * Dos.CW, 2 * Dos.CH));
                var yes = new DosButton("Да", DialogResult.Yes);
                var no = new DosButton("Нет", DialogResult.No);
                yes.Left = w / 2 - yes.Width - 8;
                no.Left = w / 2 + 8;
                yes.Top = h - yes.Height - Dos.CH;
                no.Top = yes.Top;
                f.Controls.Add(yes);
                f.Controls.Add(no);
                f.AcceptButton = yes;
                f.CancelButton = no;
                return f.ShowDialog(owner) == DialogResult.Yes;
            }
        }

        public static string Prompt(IWin32Window owner, string title, string label, string initial)
        {
            int w = 52 * Dos.CW, h = 7 * Dos.CH + 20;
            using (var f = new DosBox(title, w, h))
            {
                f.Controls.Add(Lab(label, 3 * Dos.CW, 2 * Dos.CH));
                var tb = new TextBox
                {
                    Left = 3 * Dos.CW,
                    Top = 3 * Dos.CH + 4,
                    Width = w - 6 * Dos.CW,
                    Text = initial,
                    BorderStyle = BorderStyle.FixedSingle,
                    BackColor = Dos.Cyan,
                    ForeColor = Dos.Black,
                    Font = Dos.Font
                };
                var ok = new DosButton("OK", DialogResult.OK);
                var cancel = new DosButton("Отмена", DialogResult.Cancel);
                ok.Left = w / 2 - ok.Width - 8;
                cancel.Left = w / 2 + 8;
                ok.Top = h - ok.Height - Dos.CH;
                cancel.Top = ok.Top;
                f.Controls.Add(tb);
                f.Controls.Add(ok);
                f.Controls.Add(cancel);
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                tb.SelectAll();
                bool okPressed = f.ShowDialog(owner) == DialogResult.OK;
                string s = tb.Text.Trim();
                return (okPressed && s.Length > 0) ? s : null;
            }
        }

        // ── F2: размер нового образа ──
        public static int? AskImageSize(IWin32Window owner)
        {
            var names = new[]
            {
                "МЗ УКНЦ, 2 стороны — 800 КБ",
                "МЗ УКНЦ, 1 сторона — 400 КБ",
                "RX01 — 494 блока",
                "RX02 — 988 блоков",
                "Свой размер",
            };
            var blocks = new[] { 1600, 800, 494, 988, 0 };

            int w = 54 * Dos.CW, h = 10 * Dos.CH + 20;
            using (var f = new DosBox("Новый образ", w, h))
            {
                f.Controls.Add(Lab("Тип диска:", 3 * Dos.CW, 2 * Dos.CH));
                var cb = new ComboBox
                {
                    Left = 3 * Dos.CW,
                    Top = 3 * Dos.CH,
                    Width = w - 6 * Dos.CW,
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Dos.Cyan,
                    ForeColor = Dos.Black,
                    Font = Dos.Font
                };
                foreach (var n in names) cb.Items.Add(n);
                f.Controls.Add(cb);

                f.Controls.Add(Lab("Размер, блоков по 512 байт:", 3 * Dos.CW, 5 * Dos.CH));
                var nud = new NumericUpDown
                {
                    Left = 3 * Dos.CW,
                    Top = 6 * Dos.CH,
                    Width = 14 * Dos.CW,
                    Minimum = 32,
                    Maximum = 65535,
                    Value = 1600,
                    BackColor = Dos.Cyan,
                    ForeColor = Dos.Black,
                    Font = Dos.Font,
                    BorderStyle = BorderStyle.FixedSingle
                };
                f.Controls.Add(nud);
                var kb = Lab("= 800 КБ", 19 * Dos.CW, 6 * Dos.CH + 2);
                f.Controls.Add(kb);

                bool sync = false;
                nud.ValueChanged += (s, e) =>
                {
                    int v = (int)nud.Value;
                    kb.Text = "= " + (v / 2) + " КБ";
                    if (sync) return;
                    int idx = names.Length - 1;
                    for (int i = 0; i < blocks.Length - 1; i++) if (blocks[i] == v) idx = i;
                    sync = true; cb.SelectedIndex = idx; sync = false;
                };
                cb.SelectedIndexChanged += (s, e) =>
                {
                    int b = blocks[cb.SelectedIndex];
                    if (sync || b == 0) return;
                    sync = true; nud.Value = b; sync = false;
                    kb.Text = "= " + (b / 2) + " КБ";
                };
                cb.SelectedIndex = 0;

                var ok = new DosButton("Создать", DialogResult.OK);
                var cancel = new DosButton("Отмена", DialogResult.Cancel);
                ok.Left = w / 2 - ok.Width - 8;
                cancel.Left = w / 2 + 8;
                ok.Top = h - ok.Height - Dos.CH;
                cancel.Top = ok.Top;
                f.Controls.Add(ok);
                f.Controls.Add(cancel);
                f.AcceptButton = ok;
                f.CancelButton = cancel;

                if (f.ShowDialog(owner) != DialogResult.OK) return null;
                return (int)nud.Value;
            }
        }

        // ── F4: информация об образе с круговой диаграммой ──
        public static void DiskInfo(IWin32Window owner, string filename, int total, int used, int free)
        {
            int w = 64 * Dos.CW, h = 12 * Dos.CH + 20;
            using (var f = new DosBox("Информация об образе", w, h))
            {
                string txt =
                    "Образ:     " + filename + "\n\n" +
                    string.Format("Всего:     {0,5} бл.  ({1,4} КБ)\n", total, total * 512 / 1024) +
                    string.Format("Занято:    {0,5} бл.  ({1,4} КБ)\n", used, used * 512 / 1024) +
                    string.Format("Свободно:  {0,5} бл.  ({1,4} КБ)", free, free * 512 / 1024);
                f.Controls.Add(Lab(txt, 3 * Dos.CW, 2 * Dos.CH));

                var pb = new PictureBox
                {
                    Left = w - 4 * Dos.CW - 150,
                    Top = 2 * Dos.CH,
                    Width = 150,
                    Height = 150,
                    BackColor = Dos.Gray
                };
                pb.Image = Pie(total, used);
                f.Controls.Add(pb);

                var ok = new DosButton("OK", DialogResult.OK);
                ok.Left = (w - ok.Width) / 2;
                ok.Top = h - ok.Height - Dos.CH;
                f.Controls.Add(ok);
                f.AcceptButton = ok;
                f.CancelButton = ok;
                f.ShowDialog(owner);
            }
        }

        static Bitmap Pie(int total, int used)
        {
            var bmp = new Bitmap(150, 150);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Dos.Gray);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                float deg = total > 0 ? (float)used / total * 360 : 0;
                float pct = total > 0 ? (float)used / total * 100 : 0;
                using (var red = new SolidBrush(Dos.Red))
                using (var green = new SolidBrush(Dos.Green))
                using (var pen = new Pen(Dos.Black, 1.5f))
                using (var fnt = new Font("Consolas", 9f, FontStyle.Bold))
                using (var wb = new SolidBrush(Dos.White))
                using (var blk = new SolidBrush(Dos.Black))
                using (var small = new Font("Consolas", 8f))
                {
                    g.FillPie(red, 15, 8, 118, 118, 0, deg);
                    g.FillPie(green, 15, 8, 118, 118, deg, 360 - deg);
                    g.DrawEllipse(pen, 15, 8, 118, 118);
                    if (pct > 6) Mark(g, fnt, wb, ((int)Math.Round(pct)) + "%", deg / 2);
                    if (100 - pct > 6) Mark(g, fnt, wb, ((int)Math.Round(100 - pct)) + "%", deg + (360 - deg) / 2);
                    g.DrawString("занято / свободно", small, blk, 10, 130);
                }
            }
            return bmp;
        }

        static void Mark(Graphics g, Font f, Brush b, string text, float angleDeg)
        {
            double a = angleDeg * Math.PI / 180;
            float x = 74 + (float)Math.Cos(a) * 38;
            float y = 67 + (float)Math.Sin(a) * 38;
            var sz = g.MeasureString(text, f);
            g.DrawString(text, f, b, x - sz.Width / 2, y - sz.Height / 2);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  F3: просмотрщик (hex + текст) — синий экран, как у NC
    // ══════════════════════════════════════════════════════════════════
    internal class ViewerDialog : Form
    {
        readonly byte[] _data;
        readonly string _name;
        Label _head, _foot;
        TextBox _tb;
        int _mode;                 // 0 — дамп, 1 — MACRO-11, 2 — текст
        bool _single;              // окно с готовым текстом (сведения об образе)

        // просмотр файла: дамп, дизассемблер MACRO-11 и текст
        public ViewerDialog(string title, byte[] data)
        {
            _data = data ?? new byte[0];
            _name = title;
            Build(title);
            _mode = ChooseMode();
            Apply();
        }

        // просмотр готового текста (например «Сведения о файле образа»)
        public ViewerDialog(string title, string text)
        {
            _data = new byte[0];
            _name = title;
            _single = true;
            Build(title);
            _tb.Text = text;
            _foot.Text = " Esc — выход";
        }

        void Build(string title)
        {
            Text = "Просмотр: " + title;
            ClientSize = new Size(Dos.CW * 92, Dos.CH * 28);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Dos.Blue;
            KeyPreview = true;

            _head = new Label
            {
                Dock = DockStyle.Top,
                Height = Dos.CH,
                BackColor = Dos.Cyan,
                ForeColor = Dos.Black,
                Font = Dos.Font,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = " Просмотр: " + title
            };
            _foot = new Label
            {
                Dock = DockStyle.Bottom,
                Height = Dos.CH,
                BackColor = Dos.Cyan,
                ForeColor = Dos.Black,
                Font = Dos.Font,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = " Esc — выход"
            };
            _tb = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                BackColor = Dos.Blue,
                ForeColor = Dos.Gray,
                Font = Dos.Font,
                BorderStyle = BorderStyle.None
            };
            Controls.Add(_tb);
            Controls.Add(_head);
            Controls.Add(_foot);

        }

        // Tab в WinForms — клавиша перехода между элементами, до KeyDown
        // она не доходит; поэтому все клавиши окна ловим здесь, раньше
        // навигации.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape || keyData == Keys.F3) { Close(); return true; }
            if (!_single)
            {
                if (keyData == Keys.Tab) { _mode = (_mode + 1) % 3; Apply(); return true; }
                if (keyData == (Keys.Tab | Keys.Shift)) { _mode = (_mode + 2) % 3; Apply(); return true; }
                if (keyData == Keys.F2) { _mode = 0; Apply(); return true; }
                if (keyData == Keys.F4) { _mode = 1; Apply(); return true; }
                if (keyData == Keys.F5) { _mode = 2; Apply(); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // Чем показывать по умолчанию: .SAV/.SYS/.REL — код, много
        // печатных символов — текст, остальное — дамп.
        int ChooseMode()
        {
            if (Macro11.IsSav(_name)) return 1;
            int n = Math.Min(_data.Length, 2048), printable = 0;
            for (int i = 0; i < n; i++)
            {
                byte b = _data[i];
                if (b == 9 || b == 10 || b == 13 || (b >= 32 && b < 127) || b >= 0xC0) printable++;
            }
            if (n > 0 && printable * 10 >= n * 9) return 2;
            return 0;
        }

        void Apply()
        {
            string mode;
            if (_mode == 1)
            {
                // для .SAV — полный разбор: заголовок, обход кода от точки
                // входа, метки, строки и данные; для прочих файлов — просто
                // дизассемблирование подряд
                if (Macro11.IsSav(_name) && _data.Length > 512)
                {
                    _tb.Text = SavView.Render(_data, _name);
                    mode = "MACRO-11, разбор SAV";
                }
                else
                {
                    _tb.Text = "; " + _name + ": дизассемблирование с адреса 0\r\n\r\n"
                             + Macro11.Render(_data, 0, 0, 64 * 1024);
                    mode = "MACRO-11";
                }
            }
            else if (_mode == 2)
            {
                _tb.Text = AsText(_data);
                mode = "текст";
            }
            else
            {
                _tb.Text = Dump(_data);
                mode = "дамп";
            }
            _tb.Select(0, 0);
            _head.Text = " Просмотр: " + _name + "   [" + mode + "]";
            _foot.Text = " Tab — сменить вид   F2 дамп   F4 MACRO-11   F5 текст   Esc выход";
        }

        static string AsText(byte[] d)
        {
            var sb = new System.Text.StringBuilder();
            int n = Math.Min(d.Length, 256 * 1024);
            for (int i = 0; i < n; i++)
            {
                byte b = d[i];
                if (b == 13) continue;
                if (b == 10) { sb.Append("\r\n"); continue; }
                if (b == 9 || (b >= 32 && b < 127)) sb.Append((char)b);
                else if (b >= 0x80) sb.Append(Koi(b));
                else sb.Append('.');
            }
            return sb.ToString();
        }

        // КОИ-8: строчные 192..223, заглавные 224..255 (как в компиляторе)
        static char Koi(byte b)
        {
            const string low = "юабцдефгхийклмнопярстужвьызшэщчъ";
            const string up = "ЮАБЦДЕФГХИЙКЛМНОПЯРСТУЖВЬЫЗШЭЩЧЪ";
            if (b >= 192 && b < 192 + low.Length) return low[b - 192];
            if (b >= 224 && b < 224 + up.Length) return up[b - 224];
            return '.';
        }

        static string Dump(byte[] d)
        {
            var sb = new System.Text.StringBuilder();
            int n = Math.Min(d.Length, 64 * 1024);
            for (int i = 0; i < n; i += 16)
            {
                sb.Append(i.ToString("X6")).Append("  ");
                for (int j = 0; j < 16; j++)
                {
                    if (i + j < n) sb.Append(d[i + j].ToString("X2")).Append(' ');
                    else sb.Append("   ");
                    if (j == 7) sb.Append(' ');
                }
                sb.Append(" |");
                for (int j = 0; j < 16 && i + j < n; j++)
                {
                    byte b = d[i + j];
                    sb.Append(b >= 32 && b < 127 ? (char)b : '.');
                }
                sb.Append("|\r\n");
            }
            if (d.Length > n) sb.Append("\r\n... (показано " + n + " из " + d.Length + " байт)");
            return sb.ToString();
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  F1: о программе — серый ящик DOS с картинкой УКНЦ
    // ══════════════════════════════════════════════════════════════════
    internal class AboutDialog : DosBox
    {
        public AboutDialog() : base("О программе", 440, 470)
        {
            var ok = new DosButton("OK", DialogResult.OK);
            ok.Left = (ClientSize.Width - ok.Width) / 2;
            ok.Top = 400;
            Controls.Add(ok);
            AcceptButton = ok;
            CancelButton = ok;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            int W = ClientSize.Width;

            DrawUknc(g, 40, 30);
            Dos.HLine(g, 12, W - 13, 262, Dos.DarkGray);

            Center(g, "DSK Commander", new Font("Consolas", 18f, FontStyle.Bold), Dos.Blue, 274);
            Center(g, "ДЛЯ ВСЕХ, КТО ЗНАЕТ УКНЦ", new Font("Consolas", 13f, FontStyle.Bold), Dos.Black, 312);
            Center(g, "Файловый менеджер образов RT-11 (.dsk)", new Font("Consolas", 9.5f), Dos.Black, 346);
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            Center(g, "Версия " + v.Major + "." + v.Minor, new Font("Consolas", 9f), Dos.DarkGray, 368);
            Center(g, "Nid", new Font("Consolas", 6.5f), Dos.DarkGray, ClientSize.Height - 36);
        }

        void Center(Graphics g, string text, Font f, Color c, int y)
        {
            using (f)
            using (var b = new SolidBrush(c))
            {
                var sz = g.MeasureString(text, f);
                g.DrawString(text, f, b, (ClientSize.Width - sz.Width) / 2, y);
            }
        }

        static System.Drawing.Drawing2D.GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new System.Drawing.Drawing2D.GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        static System.Drawing.Drawing2D.LinearGradientBrush Grad(RectangleF r, Color a, Color b)
        {
            return new System.Drawing.Drawing2D.LinearGradientBrush(r, a, b, 90f);
        }

        // ── УКНЦ: монитор, дисковод, клавиатурный блок ──
        static void DrawUknc(Graphics g, float ox, float oy)
        {
            Color top = Color.FromArgb(232, 228, 214), bot = Color.FromArgb(188, 183, 166);
            Color edge = Color.FromArgb(120, 116, 102);

            using (var sh = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
                g.FillEllipse(sh, ox, oy + 204, 360, 18);
            using (var cable = new Pen(Color.FromArgb(40, 40, 50), 3))
                g.DrawBezier(cable, ox + 300, oy + 150, ox + 305, oy + 170, ox + 290, oy + 160, ox + 280, oy + 172);

            var mon = new RectangleF(ox + 60, oy + 0, 180, 140);
            using (var path = Round(mon, 10))
            using (var br = Grad(mon, top, bot))
            using (var pen = new Pen(edge, 1.2f))
            { g.FillPath(br, path); g.DrawPath(pen, path); }

            var bezel = new RectangleF(mon.X + 11, mon.Y + 10, mon.Width - 22, mon.Height - 30);
            using (var path = Round(bezel, 7))
            using (var br = new SolidBrush(Color.FromArgb(38, 38, 44)))
                g.FillPath(br, path);

            var scr = new RectangleF(bezel.X + 7, bezel.Y + 7, bezel.Width - 14, bezel.Height - 14);
            using (var path = Round(scr, 4))
            using (var br = Grad(scr, Color.FromArgb(0, 0, 190), Color.FromArgb(0, 0, 120)))
                g.FillPath(br, path);
            Screen_(g, scr);
            using (var glare = new SolidBrush(Color.FromArgb(28, 255, 255, 255)))
                g.FillEllipse(glare, scr.X - 10, scr.Y - 14, scr.Width * 0.7f, scr.Height * 0.55f);

            using (var br = new SolidBrush(Color.FromArgb(90, 88, 80)))
                g.FillEllipse(br, mon.Right - 24, mon.Bottom - 15, 9, 9);
            using (var br = new SolidBrush(Color.FromArgb(80, 230, 90)))
                g.FillEllipse(br, mon.Right - 36, mon.Bottom - 12, 5, 5);

            using (var br = Grad(new RectangleF(ox + 135, oy + 140, 30, 14), bot, edge))
                g.FillRectangle(br, ox + 136, oy + 140, 28, 10);
            using (var br = Grad(new RectangleF(ox + 105, oy + 147, 90, 10), top, bot))
            using (var pen = new Pen(edge, 1f))
            { g.FillEllipse(br, ox + 105, oy + 147, 90, 10); g.DrawEllipse(pen, ox + 105, oy + 147, 90, 10); }

            var fdd = new RectangleF(ox + 256, oy + 72, 92, 78);
            using (var path = Round(fdd, 6))
            using (var br = Grad(fdd, top, bot))
            using (var pen = new Pen(edge, 1.2f))
            { g.FillPath(br, path); g.DrawPath(pen, path); }
            using (var br = new SolidBrush(Color.FromArgb(205, 200, 184)))
                g.FillRectangle(br, fdd.X + 8, fdd.Y + 22, fdd.Width - 16, 26);
            using (var br = new SolidBrush(Color.FromArgb(30, 30, 36)))
                g.FillRectangle(br, fdd.X + 14, fdd.Y + 33, fdd.Width - 28, 4);
            using (var br = new SolidBrush(Color.FromArgb(70, 68, 62)))
                g.FillRectangle(br, fdd.X + fdd.Width / 2 - 4, fdd.Y + 39, 8, 7);
            using (var br = new SolidBrush(Color.FromArgb(255, 80, 60)))
                g.FillEllipse(br, fdd.X + 12, fdd.Y + 56, 5, 5);

            float yTop = oy + 162, yBot = oy + 206;
            float xlT = ox + 22, xrT = ox + 338, xlB = ox + 6, xrB = ox + 354;
            var body = new[]
            {
                new PointF(xlT, yTop), new PointF(xrT, yTop),
                new PointF(xrB, yBot), new PointF(xlB, yBot)
            };
            using (var br = Grad(new RectangleF(xlB, yTop, xrB - xlB, yBot - yTop + 1), top, bot))
            using (var pen = new Pen(edge, 1.2f))
            { g.FillPolygon(br, body); g.DrawPolygon(pen, body); }
            var lip = new RectangleF(xlB, yBot, xrB - xlB, 9);
            using (var br = Grad(lip, bot, edge)) g.FillRectangle(br, lip);
            using (var f = new Font("Consolas", 5.5f, FontStyle.Bold))
            using (var br = new SolidBrush(Color.FromArgb(70, 66, 56)))
                g.DrawString("ЭЛЕКТРОНИКА МС 0511", f, br, xlB + 8, yBot + 0.5f);

            for (int row = 0; row < 4; row++)
            {
                float ky = yTop + 5 + row * 10;
                float t = (ky - yTop) / (yBot - yTop);
                float left = xlT + (xlB - xlT) * t + 12;
                float right = xrT + (xrB - xrT) * t - 12;
                float kw = row == 0 ? 17 : 13, gap = 3;
                int n = (int)((right - left + gap) / (kw + gap));
                float start = left + ((right - left) - (n * (kw + gap) - gap)) / 2;
                for (int k = 0; k < n; k++)
                {
                    float kx = start + k * (kw + gap), wdt = kw;
                    if (row == 3 && k == n / 2 - 4) { wdt = kw * 8 + gap * 7; k += 7; }
                    var kr = new RectangleF(kx, ky, wdt, 7.5f);
                    Color a = Color.FromArgb(70, 70, 78), b = Color.FromArgb(28, 28, 34);
                    if (row == 0) { a = Color.FromArgb(175, 175, 180); b = Color.FromArgb(120, 120, 128); }
                    else if (row == 1 && k == n - 1) { a = Color.FromArgb(230, 70, 60); b = Color.FromArgb(150, 30, 25); }
                    using (var path = Round(kr, 1.5f))
                    using (var br = Grad(kr, a, b))
                        g.FillPath(br, path);
                }
            }
        }

        // экранчик коммандера внутри монитора
        static void Screen_(Graphics g, RectangleF s)
        {
            float half = (s.Width - 12) / 2;
            using (var frame = new Pen(Color.FromArgb(0, 210, 210), 1f))
            using (var yel = new SolidBrush(Color.FromArgb(255, 255, 120)))
            using (var lb = new SolidBrush(Color.FromArgb(170, 200, 255)))
            using (var teal = new SolidBrush(Color.FromArgb(0, 150, 150)))
            {
                for (int p = 0; p < 2; p++)
                {
                    float x = s.X + 4 + p * (half + 4);
                    var r = new RectangleF(x, s.Y + 4, half, s.Height - 17);
                    g.DrawRectangle(frame, r.X, r.Y, r.Width, r.Height);
                    g.DrawRectangle(frame, r.X + 2, r.Y + 2, r.Width - 4, r.Height - 4);
                    float[] lens = { 0.7f, 0.5f, 0.62f, 0.4f, 0.55f, 0.45f };
                    for (int i = 0; i < lens.Length; i++)
                    {
                        float ly = r.Y + 6 + i * 9;
                        if (ly + 3 > r.Bottom - 4) break;
                        if (p == 1 && i == 2)
                            g.FillRectangle(teal, r.X + 4, ly - 1.5f, r.Width - 8, 6);
                        g.FillRectangle(i == 0 ? yel : lb, r.X + 7, ly, (r.Width - 14) * lens[i], 3);
                    }
                }
                float bw = (s.Width - 8) / 8f;
                for (int i = 0; i < 8; i++)
                    g.FillRectangle(teal, s.X + 4 + i * bw + 1, s.Bottom - 10, bw - 3, 6);
            }
        }
    }
}
