using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace CompMacro11
{
    // ── Двухпанельный файловый менеджер (Norton Commander стиль) ───────
    //   Верхняя панель — Windows (диски, каталоги, файлы).
    //   Нижняя панель — образ .dsk (файлы RT-11 через DskImage).
    //   F5 — копировать из активной панели в противоположную.
    //   Tab — переключить активную панель. F8 — удалить. F3 — просмотр.
    public class DskDialog : Form
    {
        static readonly Color NC_BG = Color.FromArgb(0, 0, 128);

        private WinPanel _top;      // Windows
        private DskPanel _bot;      // образ .dsk
        private bool _topActive = true;
        private Label _fkeys;

        static string LastDiskFile => Path.Combine(
            Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
            "last_disk.txt");

        public DskDialog()
        {
            Text = "Файлы — Commander";
            Size = new Size(1040, 560);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = NC_BG;
            KeyPreview = true;

            _top = new WinPanel { Dock = DockStyle.Left, Width = 512 };   // левая — Windows
            var split = new Panel { Dock = DockStyle.Left, Width = 4, BackColor = Color.Black };
            _bot = new DskPanel { Dock = DockStyle.Fill };                // правая — образ .dsk

            _fkeys = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 26,
                BackColor = Color.FromArgb(0, 128, 128),
                ForeColor = Color.White,
                Font = new Font("Consolas", 10f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "Tab Панель   F3 Просмотр   F5 Копировать   F8 Удалить   F7 Открыть образ   ESC Выход"
            };

            Controls.Add(_bot);
            Controls.Add(split);
            Controls.Add(_top);
            Controls.Add(_fkeys);

            _top.OnActivate += () => SetActive(true);
            _bot.OnActivate += () => SetActive(false);

            Load += (s, e) =>
            {
                _top.Refresh_();
                string last = RecallPath();
                if (!string.IsNullOrEmpty(last) && File.Exists(last)) _bot.Open(last);
                SetActive(true);
            };

            KeyDown += OnKey;
        }

        private void SetActive(bool top)
        {
            _topActive = top;
            _top.SetActive(top);
            _bot.SetActive(!top);
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Tab: SetActive(!_topActive); e.Handled = true; break;
                case Keys.Escape: Close(); break;
                case Keys.F5: DoCopy(); e.Handled = true; break;
                case Keys.F8: DoDelete(); e.Handled = true; break;
                case Keys.F3: DoView(); e.Handled = true; break;
                case Keys.F7: _bot.OpenDialog(RememberPath); e.Handled = true; break;
            }
        }

        private void DoCopy()
        {
            if (_topActive)
            {
                string path = _top.SelectedFilePath();
                if (path == null) { Beep(); return; }
                if (!_bot.HasImage) { Msg("Сначала откройте образ (F7)."); return; }
                var bytes = File.ReadAllBytes(path);
                string nm = RtName(Path.GetFileName(path));
                if (!_bot.Add(nm, bytes))
                    Msg($"Не удалось записать {nm}: нет места ({(bytes.Length + 511) / 512} блоков нужно).");
                else _bot.Refresh_();
            }
            else
            {
                string name = _bot.SelectedName();
                if (name == null || !_bot.HasImage) { Beep(); return; }
                var bytes = _bot.Extract(name);
                if (bytes == null) { Beep(); return; }
                string dst = Path.Combine(_top.CurrentDir ?? ".", name);
                File.WriteAllBytes(dst, bytes);
                _top.Refresh_();
            }
        }

        private void DoDelete()
        {
            if (_topActive) { Beep(); return; }
            string name = _bot.SelectedName();
            if (name == null || !_bot.HasImage) { Beep(); return; }
            if (MessageBox.Show(this, $"Удалить {name} из образа?", "Подтверждение",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            { _bot.Delete(name); _bot.Refresh_(); }
        }

        private void DoView()
        {
            byte[] data; string title;
            if (_topActive)
            {
                string p = _top.SelectedFilePath();
                if (p == null) { Beep(); return; }
                data = File.ReadAllBytes(p); title = Path.GetFileName(p);
            }
            else
            {
                string n = _bot.SelectedName();
                if (n == null || !_bot.HasImage) { Beep(); return; }
                data = _bot.Extract(n); title = n;
            }
            if (data != null) new ViewerDialog(title, data).ShowDialog(this);
        }

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
        void Beep() => System.Media.SystemSounds.Beep.Play();
        void Msg(string s) => MessageBox.Show(this, s, "Commander", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ══════════ Панель Windows ══════════
    internal class WinPanel : Panel
    {
        public event Action OnActivate;
        public string CurrentDir { get; private set; }
        private ListView _lv;
        private Label _hdr;

        static readonly Color BG = Color.FromArgb(0, 0, 128);
        static readonly Color FG = Color.FromArgb(200, 220, 255);
        static readonly Color DIR = Color.FromArgb(255, 255, 120);
        static readonly Font FT = new Font("Consolas", 10.5f);

        public WinPanel()
        {
            BackColor = BG;
            Padding = new Padding(2);
            _hdr = new Label
            {
                Dock = DockStyle.Top,
                Height = 22,
                BackColor = Color.FromArgb(0, 0, 90),
                ForeColor = Color.White,
                Font = new Font("Consolas", 10f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = " Windows"
            };
            _lv = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                BorderStyle = BorderStyle.None,
                BackColor = BG,
                ForeColor = FG,
                Font = FT,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                MultiSelect = false
            };
            _lv.Columns.Add("Имя", 300);
            _lv.Columns.Add("Размер", 95);
            _lv.Columns.Add("Тип", 80);
            _lv.ItemActivate += (s, e) => Enter_();
            _lv.Enter += (s, e) => OnActivate?.Invoke();
            _lv.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { Enter_(); e.Handled = true; } };
            Controls.Add(_lv);
            Controls.Add(_hdr);
            CurrentDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        }

        public void SetActive(bool on)
        {
            _hdr.BackColor = on ? Color.FromArgb(0, 100, 100) : Color.FromArgb(0, 0, 90);
            if (on && _lv.Items.Count > 0 && _lv.SelectedItems.Count == 0)
            { _lv.Items[0].Selected = true; _lv.Focus(); }
        }

        public void Refresh_()
        {
            _lv.Items.Clear();
            _hdr.Text = " " + (CurrentDir ?? "Компьютер");
            if (CurrentDir == null)
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    var it = new ListViewItem(d.Name) { ForeColor = DIR };
                    it.SubItems.Add(""); it.SubItems.Add("диск");
                    it.Tag = new Node { IsDrive = true, Path = d.Name };
                    _lv.Items.Add(it);
                }
                return;
            }
            var up = new ListViewItem("..") { ForeColor = DIR };
            up.SubItems.Add(""); up.SubItems.Add("вверх");
            up.Tag = new Node { IsUp = true };
            _lv.Items.Add(up);
            try
            {
                foreach (var dir in Directory.GetDirectories(CurrentDir))
                {
                    var it = new ListViewItem(Path.GetFileName(dir)) { ForeColor = DIR };
                    it.SubItems.Add(""); it.SubItems.Add("папка");
                    it.Tag = new Node { Path = dir, IsDir = true };
                    _lv.Items.Add(it);
                }
                foreach (var f in Directory.GetFiles(CurrentDir))
                {
                    var fi = new FileInfo(f);
                    var it = new ListViewItem(Path.GetFileName(f));
                    it.SubItems.Add(fi.Length.ToString());
                    it.SubItems.Add(fi.Extension.TrimStart('.').ToUpper());
                    it.Tag = new Node { Path = f };
                    _lv.Items.Add(it);
                }
            }
            catch (Exception ex) { _hdr.Text = " [нет доступа] " + ex.Message; }
        }

        private void Enter_()
        {
            if (_lv.SelectedItems.Count == 0) return;
            var n = (Node)_lv.SelectedItems[0].Tag;
            if (n.IsUp)
            {
                var parent = CurrentDir != null ? Directory.GetParent(CurrentDir) : null;
                CurrentDir = parent?.FullName;
                Refresh_();
            }
            else if (n.IsDrive || n.IsDir)
            {
                CurrentDir = n.Path;
                Refresh_();
            }
        }

        public string SelectedFilePath()
        {
            if (_lv.SelectedItems.Count == 0) return null;
            var n = (Node)_lv.SelectedItems[0].Tag;
            return (n.IsDir || n.IsDrive || n.IsUp) ? null : n.Path;
        }

        class Node { public string Path; public bool IsDir, IsDrive, IsUp; }
    }

    // ══════════ Панель образа .dsk ══════════
    internal class DskPanel : Panel
    {
        public event Action OnActivate;
        public bool HasImage => _img != null;
        private DskImage _img;
        private ListView _lv;
        private Label _hdr;

        static readonly Color BG = Color.FromArgb(0, 0, 128);
        static readonly Color FG = Color.FromArgb(200, 220, 255);
        static readonly Font FT = new Font("Consolas", 10.5f);

        public DskPanel()
        {
            BackColor = BG;
            Padding = new Padding(2);
            _hdr = new Label
            {
                Dock = DockStyle.Top,
                Height = 22,
                BackColor = Color.FromArgb(0, 0, 90),
                ForeColor = Color.White,
                Font = new Font("Consolas", 10f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = " Образ .dsk — F7 открыть"
            };
            _lv = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                BorderStyle = BorderStyle.None,
                BackColor = BG,
                ForeColor = FG,
                Font = FT,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                MultiSelect = false
            };
            _lv.Columns.Add("Имя", 240);
            _lv.Columns.Add("Блоков", 90);
            _lv.Columns.Add("Байт", 100);
            _lv.Enter += (s, e) => OnActivate?.Invoke();
            Controls.Add(_lv);
            Controls.Add(_hdr);
        }

        public void SetActive(bool on)
        {
            _hdr.BackColor = on ? Color.FromArgb(0, 100, 100) : Color.FromArgb(0, 0, 90);
            if (on && _lv.Items.Count > 0 && _lv.SelectedItems.Count == 0)
            { _lv.Items[0].Selected = true; _lv.Focus(); }
        }

        public void Open(string path)
        {
            try { _img = new DskImage(path); Refresh_(); }
            catch (Exception ex)
            { MessageBox.Show("Не открыть образ:\n" + ex.Message, "Ошибка"); }
        }

        public void OpenDialog(Action<string> remember)
        {
            using (var dlg = new OpenFileDialog { Filter = "Образы (*.dsk)|*.dsk|Все (*.*)|*.*" })
                if (dlg.ShowDialog() == DialogResult.OK)
                { Open(dlg.FileName); remember?.Invoke(dlg.FileName); }
        }

        public void Refresh_()
        {
            _lv.Items.Clear();
            if (_img == null) { _hdr.Text = " Образ .dsk — F7 открыть"; return; }
            foreach (var e in _img.ReadDir())
            {
                if (e.Empty) continue;
                var it = new ListViewItem(e.Name);
                it.SubItems.Add(e.Blocks.ToString());
                it.SubItems.Add((e.Blocks * 512).ToString());
                _lv.Items.Add(it);
            }
            _hdr.Text = $" {Path.GetFileName(_img.Path)}  свободно {_img.FreeBlocks()} бл " +
                        $"({_img.FreeBlocks() * 512 / 1024} КБ)";
        }

        public string SelectedName() =>
            _lv.SelectedItems.Count > 0 ? _lv.SelectedItems[0].Text : null;
        public byte[] Extract(string n) => _img?.Extract(n);
        public bool Add(string n, byte[] b) => _img != null && _img.AddFile(n, b);
        public void Delete(string n) => _img?.DeleteFile(n);
    }

    // ══════════ Просмотрщик файла (F3): текст + hex ══════════
    internal class ViewerDialog : Form
    {
        public ViewerDialog(string title, byte[] data)
        {
            Text = "Просмотр: " + title;
            Size = new Size(680, 560);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(0, 0, 128);
            var tb = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                BackColor = Color.FromArgb(0, 0, 128),
                ForeColor = Color.FromArgb(200, 220, 255),
                Font = new Font("Consolas", 10f)
            };
            tb.Text = BuildDump(data);
            Controls.Add(tb);
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.F3) Close(); };
        }

        static string BuildDump(byte[] d)
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
            if (d.Length > n) sb.Append($"\r\n... (показано {n} из {d.Length} байт)");
            return sb.ToString();
        }
    }
}
