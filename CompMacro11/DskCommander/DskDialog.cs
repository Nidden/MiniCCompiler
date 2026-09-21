using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace DskCommander
{
    // ── Двухпанельный файловый менеджер (Norton Commander стиль) ───────
    //   Верхняя панель — Windows (диски, каталоги, файлы).
    //   Нижняя панель — образ .dsk (файлы RT-11 через DskImage).
    //   F5 — копировать из активной панели в противоположную (можно
    //   несколько файлов сразу — выделить мышью с Ctrl/Shift, или
    //   Ctrl+A — выделить все).
    //   (Ctrl+A работает с клавиатуры, отдельной кнопки нет.) F1 — о программе.
    //   Tab — переключить активную панель. F6 — переименовать (один
    //   файл). F8 — удалить (пакетно в обеих панелях; в Windows — в
    //   корзину). F3 — просмотр. F2 — новый пустой образ. F4 — информация
    //   об образе. F9 — сжать образ.
    public class DskDialog : Form
    {
        static readonly Color NC_BG = Color.FromArgb(0, 0, 128);

        private WinPanel _top;      // Windows
        private DskPanel _bot;      // образ .dsk
        private bool _topActive = true;
        private Panel _fkeys;

        static string LastDiskFile => Path.Combine(
            Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
            "last_disk.txt");

        // Отдельная программа: окно — главное (не модальное внутри IDE),
        // поэтому по центру экрана. startPath — образ .dsk из командной
        // строки (перетащить файл на exe / «Открыть с помощью»); если не
        // задан — открывается последний образ, как и в IDE.
        public DskDialog() : this(null) { }

        public DskDialog(string startPath)
        {
            Text = "DSK Commander";
            Size = new Size(1120, 600);
            MinimumSize = new Size(900, 400);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = NC_BG;
            KeyPreview = true;
            // иконка окна — та же, что вшита в exe (ApplicationIcon в .csproj)
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            _top = new WinPanel { Dock = DockStyle.Left, Width = 512 };   // левая — Windows
            var split = new Panel { Dock = DockStyle.Left, Width = 4, BackColor = Color.Black };
            _bot = new DskPanel { Dock = DockStyle.Fill };                // правая — образ .dsk

            // Нижняя панель — раньше была просто подписью с именами клавиш;
            // теперь это настоящие кнопки: то же самое можно нажать мышью,
            // а не только клавиатурой (сами хоткеи никуда не делись).
            _fkeys = new Panel { Dock = DockStyle.Bottom, Height = 30, BackColor = Color.FromArgb(0, 90, 90) };
            int fx = 4;
            // порядок — по значимости: без образа ничего не работает,
            // дальше — основные действия с файлами, потом сервис.
            // ESC (выход) работает с клавиатуры, кнопки нет.
            FKeyBtn(_fkeys, "F7 Открыть образ", ref fx, () => _bot.OpenDialog(RememberPath));
            FKeyBtn(_fkeys, "F5 Копия", ref fx, () => DoCopy());
            FKeyBtn(_fkeys, "F3 Просмотр", ref fx, () => DoView());
            FKeyBtn(_fkeys, "F8 Удалить", ref fx, () => DoDelete());
            FKeyBtn(_fkeys, "F6 Имя", ref fx, () => DoRename());
            FKeyBtn(_fkeys, "F2 Новый", ref fx, () => DoNew());
            FKeyBtn(_fkeys, "F4 Инфо", ref fx, () => DoInfo());
            FKeyBtn(_fkeys, "F9 Сжать", ref fx, () => DoSqueeze());
            FKeyBtn(_fkeys, "F1 О программе", ref fx, () => DoAbout());
            _fkeys.Resize += (s, e) => LayoutFKeys();

            Controls.Add(_bot);
            Controls.Add(split);
            Controls.Add(_top);
            Controls.Add(_fkeys);

            _top.OnActivate += () => SetActive(true);
            _bot.OnActivate += () => SetActive(false);

            Load += (s, e) =>
            {
                _top.Refresh_();
                string last = !string.IsNullOrEmpty(startPath) && File.Exists(startPath)
                    ? startPath : RecallPath();
                if (!string.IsNullOrEmpty(last) && File.Exists(last))
                {
                    _bot.Open(last);
                    if (last == startPath) RememberPath(last);
                }
                SetActive(true);
            };

            KeyDown += OnKey;
        }

        // Кнопка нижней панели — тот же внешний вид для всех, чтобы не
        // тащить сюда WinForms-дизайнер ради шести одинаковых кнопок.
        private static Button FKeyBtn(Panel p, string text, ref int x, Action click)
        {
            var b = new Button
            {
                Text = text,
                Left = x,
                Top = 3,
                Height = 24,
                Width = TextRenderer.MeasureText(text, new Font("Consolas", 9.5f)).Width + 20,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 128, 128),
                ForeColor = Color.White,
                Font = new Font("Consolas", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(0, 60, 60);
            b.Click += (s, e) => click();
            p.Controls.Add(b);
            x += b.Width + 4;
            return b;
        }

        // Растянуть кнопки на всю ширину нижней панели, как строка
        // F-клавиш в NC: лишнее место делится поровну между кнопками.
        private void LayoutFKeys()
        {
            int n = _fkeys.Controls.Count;
            if (n == 0) return;
            const int gap = 4;
            int avail = _fkeys.ClientSize.Width - gap * (n + 1);
            int need = 0;
            foreach (Control c in _fkeys.Controls)
                need += TextRenderer.MeasureText(c.Text, c.Font).Width + 12;
            int extra = Math.Max(0, avail - need) / n;
            int x = gap;
            foreach (Control c in _fkeys.Controls)
            {
                c.Left = x;
                c.Width = TextRenderer.MeasureText(c.Text, c.Font).Width + 12 + extra;
                x += c.Width + gap;
            }
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
                case Keys.F1: DoAbout(); e.Handled = true; break;
                case Keys.F2: DoNew(); e.Handled = true; break;
                case Keys.F3: DoView(); e.Handled = true; break;
                case Keys.F4: DoInfo(); e.Handled = true; break;
                case Keys.F5: DoCopy(); e.Handled = true; break;
                case Keys.F6: DoRename(); e.Handled = true; break;
                case Keys.F7: _bot.OpenDialog(RememberPath); e.Handled = true; break;
                case Keys.F8: DoDelete(); e.Handled = true; break;
                case Keys.F9: DoSqueeze(); e.Handled = true; break;
                case Keys.A:
                    if (!e.Control) break;
                    if (_topActive) _top.SelectAll_(); else _bot.SelectAll_();
                    e.Handled = true;
                    break;
            }
        }

        // Копирует ВСЕ выделенные элементы активной панели в противоположную.
        // Один файл — как раньше; несколько — сводка вместо диалога на
        // каждый (иначе перенос десятков файлов утыкается в клики по OK).
        private void DoCopy()
        {
            if (_topActive)
            {
                var paths = _top.SelectedFilePaths();
                if (paths.Count == 0) { Beep(); return; }
                if (!_bot.HasImage) { Msg("Сначала откройте образ (F7)."); return; }
                int ok = 0; var failed = new System.Collections.Generic.List<string>();
                foreach (var path in paths)
                {
                    var bytes = File.ReadAllBytes(path);
                    string nm = RtName(Path.GetFileName(path));
                    if (_bot.Add(nm, bytes)) ok++;
                    else failed.Add(nm);
                }
                _bot.Refresh_();
                ReportCopy(ok, failed);
            }
            else
            {
                var names = _bot.SelectedNames();
                if (names.Count == 0 || !_bot.HasImage) { Beep(); return; }
                int ok = 0; var failed = new System.Collections.Generic.List<string>();
                foreach (var name in names)
                {
                    var bytes = _bot.Extract(name);
                    if (bytes == null) { failed.Add(name); continue; }
                    string dst = Path.Combine(_top.CurrentDir ?? ".", name);
                    File.WriteAllBytes(dst, bytes);
                    ok++;
                }
                _top.Refresh_();
                ReportCopy(ok, failed);
            }
        }

        private void ReportCopy(int ok, System.Collections.Generic.List<string> failed)
        {
            if (failed.Count == 0) return;   // тихий успех — как и раньше для одного файла
            Msg($"Скопировано: {ok}.  Не удалось: {failed.Count} ({string.Join(", ", failed)}).");
        }

        private void DoDelete()
        {
            if (_topActive) { DoDeleteWindows(); return; }
            var names = _bot.SelectedNames();
            if (names.Count == 0 || !_bot.HasImage) { Beep(); return; }
            string question = names.Count == 1
                ? $"Удалить {names[0]} из образа?"
                : $"Удалить {names.Count} файлов из образа?\n\n{string.Join(", ", names)}";
            if (NcDialogs.Confirm(this, question, "Подтверждение"))
            {
                foreach (var name in names) _bot.Delete(name);
                _bot.Refresh_();
            }
        }

        // Переименование — ровно одного файла (переименовать несколько
        // выделенных в одно и то же имя бессмысленно, поэтому не пакетное,
        // в отличие от F5/F8).
        private void DoRename()
        {
            if (_topActive)
            {
                string path = _top.SelectedFilePath();
                if (path == null) { Beep(); return; }
                string oldName = Path.GetFileName(path);
                string newName = PromptText("Переименовать", oldName);
                if (newName == null || newName == oldName) return;
                if (!_top.RenameSelected(newName, out string err)) { Msg("Не удалось переименовать: " + err); return; }
                _top.Refresh_();
            }
            else
            {
                string oldName = _bot.SelectedName();
                if (oldName == null || !_bot.HasImage) { Beep(); return; }
                string newName = PromptText("Переименовать (RT-11, до 6 симв. + 3 расширение)", oldName);
                if (newName == null) return;
                newName = RtName(newName);
                if (newName == oldName) return;
                if (!_bot.Rename(oldName, newName, out string err)) { Msg("Не удалось переименовать: " + err); return; }
                _bot.Refresh_();
            }
        }

        // Простой диалог "введите текст" — тот же стиль (тёмная тема,
        // TextBox + OK/Отмена), что уже используется в LevelEditor.cs
        // для переименования уровня.
        private string PromptText(string title, string initial)
        {
            using (var f = new Form
            {
                Width = 360, Height = 130, Text = title,
                BackColor = NC_BG, ForeColor = Color.White,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false, MinimizeBox = false
            })
            {
                var tb = new TextBox { Left = 10, Top = 15, Width = 320, Text = initial, BackColor = Color.FromArgb(0, 0, 90), ForeColor = Color.White };
                var ok = NcDialogs.MakeButton("OK", DialogResult.OK);
                ok.Left = 160; ok.Top = 55; ok.Width = 80;
                var cancel = NcDialogs.MakeButton("Отмена", DialogResult.Cancel);
                cancel.Left = 250; cancel.Top = 55; cancel.Width = 80;
                f.Controls.Add(tb); f.Controls.Add(ok); f.Controls.Add(cancel);
                f.AcceptButton = ok; f.CancelButton = cancel;
                tb.SelectAll();
                bool okPressed = f.ShowDialog(this) == DialogResult.OK;
                string text = tb.Text.Trim();
                return (okPressed && text.Length > 0) ? text : null;
            }
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

        private void DoInfo()
        {
            // информация — всегда об открытом образе, из какой панели ни нажми
            if (!_bot.HasImage) { Msg("Сначала откройте образ (F7)."); return; }
            _bot.ShowInfo(this);
        }

        // ── F8 в панели Windows: удаление в корзину (не безвозвратно) ──
        private void DoDeleteWindows()
        {
            var paths = _top.SelectedDeletablePaths();
            if (paths.Count == 0) { Beep(); return; }
            string question = paths.Count == 1
                ? $"Удалить в корзину:\n{Path.GetFileName(paths[0])} ?"
                : $"Удалить в корзину {paths.Count} объектов?\n\n" +
                  string.Join(", ", paths.ConvertAll(p => Path.GetFileName(p)));
            if (!NcDialogs.Confirm(this, question, "Удаление")) return;

            var failed = new System.Collections.Generic.List<string>();
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
            _top.Refresh_();
            if (failed.Count > 0) Msg("Не удалось удалить:\n" + string.Join("\n", failed));
        }

        // ── F2: новый пустой образ RT-11 ──
        private void DoNew()
        {
            int? blocks = NcDialogs.AskImageSize(this);
            if (blocks == null) return;
            using (var dlg = new SaveFileDialog
            {
                Filter = "Образы (*.dsk)|*.dsk|Все (*.*)|*.*",
                DefaultExt = "dsk",
                FileName = "new.dsk",
                InitialDirectory = _top.CurrentDir ?? "",
                Title = "Новый образ"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try { DskImage.CreateBlank(dlg.FileName, blocks.Value); }
                catch (Exception ex) { Msg("Не удалось создать образ:\n" + ex.Message); return; }
                _bot.Open(dlg.FileName);
                RememberPath(dlg.FileName);
                _top.Refresh_();
                SetActive(false);
            }
        }

        // ── F1: о программе ──
        private void DoAbout()
        {
            using (var a = new AboutDialog()) a.ShowDialog(this);
        }

        // ── F9: сжатие образа ──
        private void DoSqueeze()
        {
            if (!_bot.HasImage) { Msg("Сначала откройте образ (F7)."); return; }
            if (!_bot.NeedsSqueeze()) { Msg("Образ уже сжат: всё свободное место одним куском в конце."); return; }
            if (!NcDialogs.Confirm(this,
                "Сжать образ?\n\nФайлы будут сдвинуты к началу диска, свободное место\n" +
                "соберётся одним куском в конце. Сделайте копию образа,\n" +
                "если он вам дорог.", "Сжатие")) return;
            int moved;
            try { moved = _bot.Squeeze(); }
            catch (Exception ex) { Msg("Ошибка сжатия:\n" + ex.Message); return; }
            _bot.Refresh_();
            Msg($"Готово. Перемещено файлов: {moved}.");
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
        void Msg(string s) => NcDialogs.Msg(this, s, "Commander");
    }

    // ── NC-стилизованные модальные окна — замена MessageBox.Show, чтобы
    //    подтверждения/сообщения/промпты выглядели как остальной Commander
    //    (тёмно-синий фон, бирюзовые плоские кнопки, Consolas), а не как
    //    стандартные системные диалоги. ──────────────────────────────────
    internal static class NcDialogs
    {
        static readonly Color BG = Color.FromArgb(0, 0, 128);
        static readonly Font FT = new Font("Consolas", 11f);

        // Та же кнопка, что и в нижней F-панели (FKeyBtn) — бирюзовая,
        // плоская — просто без привязки к Panel/ref-x, т.к. в диалогах
        // кнопок мало и позиционируются они вручную.
        public static Button MakeButton(string text, DialogResult result)
        {
            var b = new Button
            {
                Text = text,
                Width = 90,
                Height = 28,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(0, 128, 128),
                ForeColor = Color.White,
                Font = new Font("Consolas", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                DialogResult = result
            };
            b.FlatAppearance.BorderColor = Color.FromArgb(0, 60, 60);
            return b;
        }

        // TextBox переносит строку только по "\r\n" — одиночный "\n" он
        // игнорирует и склеивает всё в одну строку. Приводим к \r\n.
        static string Nl(string s) => (s ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");

        static Form BuildBase(string text, string title, out int contentBottom)
        {
            text = Nl(text);
            int w = Math.Min(520, Math.Max(320, TextRenderer.MeasureText(text, FT).Width + 60));
            var f = new Form
            {
                Width = w, Height = 200, Text = title,
                BackColor = BG, ForeColor = Color.White,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false, MinimizeBox = false
            };
            var tb = new TextBox
            {
                Left = 12, Top = 12, Width = f.ClientSize.Width - 24, Height = 100,
                Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None,
                ScrollBars = ScrollBars.Vertical,
                BackColor = BG, ForeColor = Color.White, Font = FT,
                Text = text, TabStop = false
            };
            f.Controls.Add(tb);
            contentBottom = tb.Top + tb.Height;
            return f;
        }

        public static void Msg(IWin32Window owner, string text, string title = "Commander")
        {
            var f = BuildBase(text, title, out int y);
            var ok = MakeButton("OK", DialogResult.OK);
            ok.Left = (f.ClientSize.Width - ok.Width) / 2;
            ok.Top = y + 15;
            f.Controls.Add(ok);
            f.AcceptButton = ok; f.CancelButton = ok;
            f.ShowDialog(owner);
        }

        public static bool Confirm(IWin32Window owner, string text, string title = "Подтверждение")
        {
            var f = BuildBase(text, title, out int y);
            var yes = MakeButton("Да", DialogResult.Yes);
            var no = MakeButton("Нет", DialogResult.No);
            int cw = f.ClientSize.Width;
            yes.Left = cw / 2 - yes.Width - 10; yes.Top = y + 15;
            no.Left = cw / 2 + 10; no.Top = y + 15;
            f.Controls.Add(yes); f.Controls.Add(no);
            f.AcceptButton = yes; f.CancelButton = no;
            return f.ShowDialog(owner) == DialogResult.Yes;
        }

        public static void Info(IWin32Window owner, string text, string title = "Информация")
        {
            var f = BuildBase(text, title, out int y);
            f.Height = Math.Max(200, y + 70);
            var ok = MakeButton("OK", DialogResult.OK);
            ok.Left = (f.ClientSize.Width - ok.Width) / 2;
            ok.Top = y + 15;
            f.Controls.Add(ok);
            f.AcceptButton = ok; f.CancelButton = ok;
            f.ShowDialog(owner);
        }

        public static void DiskInfo(IWin32Window owner, string filename, int totalBlocks, int usedBlocks, int freeBlocks)
        {
            using (var f = new Form
            {
                Width = 620, Height = 240, Text = "Информация образа",
                BackColor = BG, ForeColor = Color.White,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false, MinimizeBox = false
            })
            {
                // Левая часть — текстовая информация
                var tb = new TextBox
                {
                    Left = 12, Top = 12, Width = 310, Height = 140,
                    Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None,
                    ScrollBars = ScrollBars.None,
                    BackColor = BG, ForeColor = Color.White, Font = new Font("Consolas", 10f),
                    TabStop = false
                };
                int usedKB = usedBlocks * 512 / 1024;
                int freeKB = freeBlocks * 512 / 1024;
                int totalKB = totalBlocks * 512 / 1024;
                tb.Text = Nl($"Образ:     {filename}\n" +
                             $"\n" +
                             $"Всего:     {totalBlocks,5} бл.  ({totalKB,4} КБ)\n" +
                             $"\n" +
                             $"Занято:    {usedBlocks,5} бл.  ({usedKB,4} КБ)\n" +
                             $"\n" +
                             $"Свободно:  {freeBlocks,5} бл.  ({freeKB,4} КБ)");
                f.Controls.Add(tb);

                // Правая часть — круговая диаграмма
                var pb = new PictureBox
                {
                    Left = 335, Top = 12, Width = 160, Height = 160,
                    BackColor = BG, BorderStyle = BorderStyle.None
                };
                pb.Image = DrawPie(totalBlocks, usedBlocks, freeBlocks);
                f.Controls.Add(pb);

                // Кнопка OK внизу
                var ok = MakeButton("OK", DialogResult.OK);
                ok.Left = (f.ClientSize.Width - ok.Width) / 2;
                ok.Top = 170;
                f.Controls.Add(ok);
                f.AcceptButton = ok; f.CancelButton = ok;

                f.ShowDialog(owner);
            }
        }

        static Bitmap DrawPie(int total, int used, int free)
        {
            var bmp = new Bitmap(140, 140);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.FromArgb(0, 0, 128));
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

                float usedDegrees = total > 0 ? (float)used / total * 360 : 0;
                float usedPercent = total > 0 ? (float)used / total * 100 : 0;
                float freePercent = 100 - usedPercent;

                using (var redBrush = new SolidBrush(Color.FromArgb(220, 80, 80)))
                using (var greenBrush = new SolidBrush(Color.FromArgb(100, 200, 100)))
                using (var borderPen = new Pen(Color.White, 1.5f))
                using (var textFont = new Font("Consolas", 9f, FontStyle.Bold))
                using (var textBrush = new SolidBrush(Color.White))
                {
                    g.FillPie(redBrush, 15, 15, 110, 110, 0, usedDegrees);
                    g.FillPie(greenBrush, 15, 15, 110, 110, usedDegrees, 360 - usedDegrees);
                    g.DrawEllipse(borderPen, 15, 15, 110, 110);

                    // Текст с процентами
                    string usedText = $"{usedPercent:F0}%";
                    string freeText = $"{freePercent:F0}%";

                    // Позиция для процента занято (красный сегмент)
                    if (usedPercent > 5)
                    {
                        float angle = usedDegrees / 2 * (float)Math.PI / 180;
                        float tx = 70 + (float)Math.Cos(angle) * 35;
                        float ty = 70 + (float)Math.Sin(angle) * 35;
                        var size = g.MeasureString(usedText, textFont);
                        g.DrawString(usedText, textFont, textBrush, tx - size.Width / 2, ty - size.Height / 2);
                    }

                    // Позиция для процента свободно (зеленый сегмент)
                    if (freePercent > 5)
                    {
                        float angle = (usedDegrees + (360 - usedDegrees) / 2) * (float)Math.PI / 180;
                        float tx = 70 + (float)Math.Cos(angle) * 35;
                        float ty = 70 + (float)Math.Sin(angle) * 35;
                        var size = g.MeasureString(freeText, textFont);
                        g.DrawString(freeText, textFont, textBrush, tx - size.Width / 2, ty - size.Height / 2);
                    }
                }
            }
            return bmp;
        }

        // ── общий каркас NC-формы без текста (для диалогов с контролами) ──
        static Form BuildForm(string title, int w, int h) => new Form
        {
            Width = w, Height = h, Text = title,
            BackColor = BG, ForeColor = Color.White, Font = FT,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false
        };

        static Label MakeLabel(string text, int x, int y) => new Label
        {
            Text = text, Left = x, Top = y, AutoSize = true,
            ForeColor = Color.White, BackColor = BG, Font = FT
        };

        // ── F2: выбор размера нового образа. null — отмена. ──
        public static int? AskImageSize(IWin32Window owner)
        {
            var presets = new[]
            {
                new { Name = "МЗ УКНЦ, 2 стороны — 800 КБ", Blocks = 1600 },
                new { Name = "МЗ УКНЦ, 1 сторона — 400 КБ", Blocks = 800 },
                new { Name = "RX01 — 494 блока",            Blocks = 494 },
                new { Name = "RX02 — 988 блоков",           Blocks = 988 },
                new { Name = "Свой размер",                 Blocks = 0 },
            };
            using (var f = BuildForm("Новый образ", 440, 230))
            {
                f.Controls.Add(MakeLabel("Тип диска:", 14, 16));
                var cb = new ComboBox
                {
                    Left = 14, Top = 40, Width = 396,
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(0, 0, 90), ForeColor = Color.White, Font = FT
                };
                foreach (var p in presets) cb.Items.Add(p.Name);
                f.Controls.Add(cb);

                f.Controls.Add(MakeLabel("Размер, блоков по 512 байт:", 14, 80));
                var nud = new NumericUpDown
                {
                    Left = 14, Top = 104, Width = 120,
                    Minimum = 32, Maximum = 65535, Value = 1600,
                    BackColor = Color.FromArgb(0, 0, 90), ForeColor = Color.White, Font = FT,
                    BorderStyle = BorderStyle.FixedSingle
                };
                f.Controls.Add(nud);
                var kb = MakeLabel("", 146, 106);
                kb.ForeColor = Color.FromArgb(200, 220, 255);
                f.Controls.Add(kb);

                bool sync = false;
                nud.ValueChanged += (s, e) =>
                {
                    int v = (int)nud.Value;
                    kb.Text = $"= {v / 2} КБ";
                    if (sync) return;
                    int idx = presets.Length - 1;
                    for (int i = 0; i < presets.Length - 1; i++) if (presets[i].Blocks == v) idx = i;
                    sync = true; cb.SelectedIndex = idx; sync = false;
                };
                cb.SelectedIndexChanged += (s, e) =>
                {
                    int b = presets[cb.SelectedIndex].Blocks;
                    if (sync || b == 0) return;
                    sync = true; nud.Value = b; sync = false;
                    kb.Text = $"= {b / 2} КБ";
                };
                cb.SelectedIndex = 0;
                kb.Text = "= 800 КБ";

                var ok = MakeButton("Создать", DialogResult.OK);
                var cancel = MakeButton("Отмена", DialogResult.Cancel);
                ok.Left = 440 / 2 - ok.Width - 16; ok.Top = 146;
                cancel.Left = 440 / 2 - 6; cancel.Top = 146;
                f.Controls.Add(ok); f.Controls.Add(cancel);
                f.AcceptButton = ok; f.CancelButton = cancel;

                if (f.ShowDialog(owner) != DialogResult.OK) return null;
                return (int)nud.Value;
            }
        }
    }

    // ══════════ F1: окно «О программе» ══════════
    //   Всё рисуется кодом (без картинок в ресурсах): двойная рамка NC,
    //   УКНЦ — монитор с синим экраном «коммандера», блок с клавиатурой
    //   (сама ЭВМ МС 0511) и дисковод; ниже подписи.
    internal class AboutDialog : Form
    {
        static readonly Color BG = Color.FromArgb(0, 0, 128);
        static readonly Color CYAN = Color.FromArgb(0, 200, 200);
        static readonly Color YEL = Color.FromArgb(255, 255, 110);
        static readonly Color LIGHT = Color.FromArgb(200, 220, 255);

        public AboutDialog()
        {
            Text = "О программе";
            ClientSize = new Size(440, 480);
            BackColor = BG; ForeColor = Color.White;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            DoubleBuffered = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var ok = NcDialogs.MakeButton("OK", DialogResult.OK);
            ok.Left = (ClientSize.Width - ok.Width) / 2;
            ok.Top = 400;
            Controls.Add(ok);
            AcceptButton = ok; CancelButton = ok;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            int W = ClientSize.Width, H = ClientSize.Height;

            // двойная рамка, как у окон NC
            using (var p = new Pen(CYAN, 2))
            {
                g.DrawRectangle(p, 7, 7, W - 15, H - 15);
                g.DrawRectangle(p, 12, 12, W - 25, H - 25);
            }

            DrawUknc(g, 40, 30);

            // разделитель под картинкой — одинарная линия внутри рамки
            using (var p = new Pen(CYAN, 1)) g.DrawLine(p, 12, 262, W - 13, 262);

            Center(g, "DSK Commander", new Font("Consolas", 18f, FontStyle.Bold), CYAN, 274);
            Center(g, "ДЛЯ ВСЕХ, КТО ЗНАЕТ УКНЦ", new Font("Consolas", 13f, FontStyle.Bold), YEL, 312);
            Center(g, "Файловый менеджер образов RT-11 (.dsk)", new Font("Consolas", 9.5f), LIGHT, 346);
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            Center(g, $"Версия {v.Major}.{v.Minor}", new Font("Consolas", 9f), LIGHT, 368);

            // подпись автора — очень мелко внизу
            Center(g, "Nid", new Font("Consolas", 6.5f), Color.FromArgb(120, 140, 210), H - 36);
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

        static System.Drawing.Drawing2D.LinearGradientBrush Grad(RectangleF r, Color top, Color bottom) =>
            new System.Drawing.Drawing2D.LinearGradientBrush(r, top, bottom, 90f);

        // ── УКНЦ: картинка 360×220, левый верхний угол (ox, oy) ──
        static void DrawUknc(Graphics g, float ox, float oy)
        {
            Color caseTop = Color.FromArgb(232, 228, 214), caseBot = Color.FromArgb(188, 183, 166);
            Color edge = Color.FromArgb(120, 116, 102);

            // тень под всей композицией
            using (var sh = new SolidBrush(Color.FromArgb(70, 0, 0, 30)))
                g.FillEllipse(sh, ox + 0, oy + 204, 360, 18);

            // ── кабель от дисковода к ЭВМ ──
            using (var cable = new Pen(Color.FromArgb(40, 40, 50), 3))
                g.DrawBezier(cable, ox + 300, oy + 150, ox + 305, oy + 170, ox + 290, oy + 160, ox + 280, oy + 172);

            // ── монитор ──
            var mon = new RectangleF(ox + 60, oy + 0, 180, 140);
            using (var path = Round(mon, 10))
            using (var br = Grad(mon, caseTop, caseBot))
            using (var pen = new Pen(edge, 1.2f))
            { g.FillPath(br, path); g.DrawPath(pen, path); }

            var bezel = new RectangleF(mon.X + 11, mon.Y + 10, mon.Width - 22, mon.Height - 30);
            using (var path = Round(bezel, 7))
            using (var br = new SolidBrush(Color.FromArgb(38, 38, 44)))
                g.FillPath(br, path);

            var scr = new RectangleF(bezel.X + 7, bezel.Y + 7, bezel.Width - 14, bezel.Height - 14);
            using (var path = Round(scr, 4))
            using (var br = Grad(scr, Color.FromArgb(0, 0, 180), Color.FromArgb(0, 0, 110)))
                g.FillPath(br, path);
            DrawNcScreen(g, scr);
            // блик на стекле
            using (var glare = new SolidBrush(Color.FromArgb(28, 255, 255, 255)))
                g.FillEllipse(glare, scr.X - 10, scr.Y - 14, scr.Width * 0.7f, scr.Height * 0.55f);

            // кнопка питания и лампочка на корпусе монитора
            using (var br = new SolidBrush(Color.FromArgb(90, 88, 80)))
                g.FillEllipse(br, mon.Right - 24, mon.Bottom - 15, 9, 9);
            using (var br = new SolidBrush(Color.FromArgb(80, 230, 90)))
                g.FillEllipse(br, mon.Right - 36, mon.Bottom - 12, 5, 5);

            // подставка
            using (var br = Grad(new RectangleF(ox + 135, oy + 140, 30, 14), caseBot, edge))
                g.FillRectangle(br, ox + 136, oy + 140, 28, 10);
            using (var br = Grad(new RectangleF(ox + 105, oy + 148, 90, 10), caseTop, caseBot))
            using (var pen = new Pen(edge, 1f))
            { g.FillEllipse(br, ox + 105, oy + 147, 90, 10); g.DrawEllipse(pen, ox + 105, oy + 147, 90, 10); }

            // ── дисковод ──
            var fdd = new RectangleF(ox + 256, oy + 72, 92, 78);
            using (var path = Round(fdd, 6))
            using (var br = Grad(fdd, caseTop, caseBot))
            using (var pen = new Pen(edge, 1.2f))
            { g.FillPath(br, path); g.DrawPath(pen, path); }
            using (var br = new SolidBrush(Color.FromArgb(205, 200, 184)))
                g.FillRectangle(br, fdd.X + 8, fdd.Y + 22, fdd.Width - 16, 26);
            using (var br = new SolidBrush(Color.FromArgb(30, 30, 36)))
                g.FillRectangle(br, fdd.X + 14, fdd.Y + 33, fdd.Width - 28, 4);      // щель
            using (var br = new SolidBrush(Color.FromArgb(70, 68, 62)))
                g.FillRectangle(br, fdd.X + fdd.Width / 2 - 4, fdd.Y + 39, 8, 7);    // рычажок
            using (var br = new SolidBrush(Color.FromArgb(255, 80, 60)))
                g.FillEllipse(br, fdd.X + 12, fdd.Y + 56, 5, 5);                    // лампочка

            // ── ЭВМ с клавиатурой (сам УКНЦ): клиновидный корпус ──
            float yTop = oy + 162, yBot = oy + 206;
            float xlT = ox + 22, xrT = ox + 338, xlB = ox + 6, xrB = ox + 354;
            var body = new[] { new PointF(xlT, yTop), new PointF(xrT, yTop), new PointF(xrB, yBot), new PointF(xlB, yBot) };
            using (var br = Grad(new RectangleF(xlB, yTop, xrB - xlB, yBot - yTop + 1), caseTop, caseBot))
            using (var pen = new Pen(edge, 1.2f))
            { g.FillPolygon(br, body); g.DrawPolygon(pen, body); }
            // передняя кромка
            var lip = new RectangleF(xlB, yBot, xrB - xlB, 9);
            using (var br = Grad(lip, caseBot, edge))
                g.FillRectangle(br, lip);
            using (var f = new Font("Consolas", 5.5f, FontStyle.Bold))
            using (var br = new SolidBrush(Color.FromArgb(70, 66, 56)))
                g.DrawString("ЭЛЕКТРОНИКА МС 0511", f, br, xlB + 8, yBot + 0.5f);

            // клавиши: 4 ряда, ширина ряда следует за скосом корпуса
            Color keyTop = Color.FromArgb(70, 70, 78), keyBot = Color.FromArgb(28, 28, 34);
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
                    float kx = start + k * (kw + gap);
                    float w = kw;
                    // пробел в нижнем ряду — длинная клавиша посередине
                    if (row == 3 && k == n / 2 - 4) { w = kw * 8 + gap * 7; k += 7; }
                    var kr = new RectangleF(kx, ky, w, 7.5f);
                    Color a = keyTop, b = keyBot;
                    if (row == 0) { a = Color.FromArgb(175, 175, 180); b = Color.FromArgb(120, 120, 128); }   // функциональные
                    else if (row == 1 && k == n - 1) { a = Color.FromArgb(230, 70, 60); b = Color.FromArgb(150, 30, 25); } // «СТОП»
                    using (var path = Round(kr, 1.5f))
                    using (var br = Grad(kr, a, b))
                        g.FillPath(br, path);
                }
            }
        }

        // мини-«коммандер» на экране монитора
        static void DrawNcScreen(Graphics g, RectangleF s)
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
                        if (p == 1 && i == 2)   // курсор в правой панели
                            g.FillRectangle(teal, r.X + 4, ly - 1.5f, r.Width - 8, 6);
                        g.FillRectangle(i == 0 ? yel : lb, r.X + 7, ly, (r.Width - 14) * lens[i], 3);
                    }
                }
                // строка F-клавиш
                float bw = (s.Width - 8) / 8f;
                for (int i = 0; i < 8; i++)
                    g.FillRectangle(teal, s.X + 4 + i * bw + 1, s.Bottom - 10, bw - 3, 6);
            }
        }
    }

    // ══════════ Сортировка списка по клику на заголовок ══════════
    //   Первый клик по столбцу — по возрастанию (↑), повторный — по
    //   убыванию (↓), и так по кругу. Клик по другому столбцу — снова ↑.
    //   group — «этаж» строки, который важнее сортировки (".." и папки
    //   всегда над файлами в обе стороны). При равенстве — по имени.
    internal class LvSorter : System.Collections.IComparer
    {
        readonly ListView _lv;
        readonly string[] _names;
        readonly bool[] _numeric;
        readonly Func<ListViewItem, int> _group;

        public int Column { get; private set; }     // -1 — исходный порядок
        public bool Desc { get; private set; }

        public LvSorter(ListView lv, string[] names, bool[] numeric, Func<ListViewItem, int> group, int initialColumn)
        {
            _lv = lv; _names = names; _numeric = numeric; _group = group;
            Column = initialColumn;
            _lv.ColumnClick += (s, e) =>
            {
                if (Column == e.Column) Desc = !Desc;
                else { Column = e.Column; Desc = false; }
                Apply();
            };
            MarkHeader();
        }

        void Apply()
        {
            _lv.ListViewItemSorter = this;
            _lv.Sort();
            MarkHeader();
            if (_lv.SelectedItems.Count > 0) _lv.SelectedItems[0].EnsureVisible();
        }

        // на время заполнения списка — без сортировки (иначе ListView
        // пересортировывает после каждого добавленного элемента)
        public void Suspend() => _lv.ListViewItemSorter = null;
        public void Resume()
        {
            if (Column < 0) return;
            _lv.ListViewItemSorter = this;      // установка сама вызывает Sort()
        }

        void MarkHeader()
        {
            for (int i = 0; i < _names.Length && i < _lv.Columns.Count; i++)
                _lv.Columns[i].Text = _names[i] + (i == Column ? (Desc ? " ↓" : " ↑") : "");
        }

        public int Compare(object x, object y)
        {
            var a = (ListViewItem)x; var b = (ListViewItem)y;
            if (_group != null)
            {
                int g = _group(a).CompareTo(_group(b));
                if (g != 0) return g;
            }
            int r = Cmp(a, b, Column);
            if (Desc) r = -r;
            if (r == 0 && Column != 0) r = Cmp(a, b, 0);
            return r;
        }

        int Cmp(ListViewItem a, ListViewItem b, int col)
        {
            string ta = col < a.SubItems.Count ? a.SubItems[col].Text : "";
            string tb = col < b.SubItems.Count ? b.SubItems[col].Text : "";
            if (col < _numeric.Length && _numeric[col])
            {
                long na = long.TryParse(ta, out long va) ? va : -1;
                long nb = long.TryParse(tb, out long vb) ? vb : -1;
                return na.CompareTo(nb);
            }
            return string.Compare(ta, tb, StringComparison.CurrentCultureIgnoreCase);
        }
    }

    // ══════════ Панель Windows ══════════
    internal class WinPanel : Panel
    {
        public event Action OnActivate;
        public string CurrentDir { get; private set; }
        private ListView _lv;
        private Label _hdr;
        private LvSorter _sorter;

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
                HeaderStyle = ColumnHeaderStyle.Clickable,
                MultiSelect = true,
                HideSelection = false
            };
            _lv.Columns.Add("Имя", 300);
            _lv.Columns.Add("Размер", 95);
            _lv.Columns.Add("Тип", 80);
            // сортировка кликом по заголовку; ".." всегда сверху, затем
            // папки/диски, затем файлы — как в NC. Сначала — по имени.
            _sorter = new LvSorter(_lv, new[] { "Имя", "Размер", "Тип" },
                new[] { false, true, false },
                it => { var n = (Node)it.Tag; return n.IsUp ? 0 : (n.IsDir || n.IsDrive) ? 1 : 2; },
                0);
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
            _lv.BeginUpdate();
            _sorter.Suspend();          // без пересортировки на каждом Add
            try { Fill(); }
            finally { _sorter.Resume(); _lv.EndUpdate(); }
        }

        private void Fill()
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

        // Все выделенные файлы (папки/диски/".." пропускаются — их
        // копировать некуда и незачем, в отличие от файлов).
        public System.Collections.Generic.List<string> SelectedFilePaths()
        {
            var result = new System.Collections.Generic.List<string>();
            foreach (ListViewItem it in _lv.SelectedItems)
            {
                var n = (Node)it.Tag;
                if (!n.IsDir && !n.IsDrive && !n.IsUp) result.Add(n.Path);
            }
            return result;
        }

        // Для F8: выделенные файлы и папки (без "..", без дисков).
        public System.Collections.Generic.List<string> SelectedDeletablePaths()
        {
            var result = new System.Collections.Generic.List<string>();
            foreach (ListViewItem it in _lv.SelectedItems)
            {
                var n = (Node)it.Tag;
                if (!n.IsDrive && !n.IsUp) result.Add(n.Path);
            }
            return result;
        }

        // HideSelection=false уже делает подсветку одинаковой без фокуса,
        // но фокус всё равно возвращаем на список — так же, как после
        // обычного клика мышью, и клавиши (Enter/стрелки) сразу работают.
        public void SelectAll_()
        {
            foreach (ListViewItem it in _lv.Items) it.Selected = true;
            _lv.Focus();
        }

        // Переименование реального файла/папки Windows — в отличие от
        // удаления, это обратимое действие (File.Move/Directory.Move), не
        // трогает содержимое, поэтому в отличие от F8 здесь не заблокировано.
        public bool RenameSelected(string newName, out string error)
        {
            error = null;
            if (_lv.SelectedItems.Count == 0) { error = "ничего не выбрано"; return false; }
            var n = (Node)_lv.SelectedItems[0].Tag;
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
        private LvSorter _sorter;

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
                HeaderStyle = ColumnHeaderStyle.Clickable,
                MultiSelect = true,
                HideSelection = false
            };
            _lv.Columns.Add("Имя", 240);
            _lv.Columns.Add("Блоков", 90);
            _lv.Columns.Add("Байт", 100);
            // пока не кликнули по заголовку — порядок как на диске
            // (физический порядок каталога RT-11)
            _sorter = new LvSorter(_lv, new[] { "Имя", "Блоков", "Байт" },
                new[] { false, true, true }, null, -1);
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
            { NcDialogs.Msg(FindForm() ?? (IWin32Window)this, "Не открыть образ:\n" + ex.Message, "Ошибка"); }
        }

        public void OpenDialog(Action<string> remember)
        {
            using (var dlg = new OpenFileDialog { Filter = "Образы (*.dsk)|*.dsk|Все (*.*)|*.*" })
                if (dlg.ShowDialog() == DialogResult.OK)
                { Open(dlg.FileName); remember?.Invoke(dlg.FileName); }
        }

        public void ShowInfo(IWin32Window owner)
        {
            if (_img == null) return;
            int total = _img.TotalBlocks;
            int free = _img.FreeBlocks();
            int used = total - free;
            NcDialogs.DiskInfo(owner, Path.GetFileName(_img.Path), total, used, free);
        }

        public void Refresh_()
        {
            _lv.BeginUpdate();
            _sorter.Suspend();
            try
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
            }
            finally { _sorter.Resume(); _lv.EndUpdate(); }
            _hdr.Text = $" {Path.GetFileName(_img.Path)}  свободно {_img.FreeBlocks()} бл " +
                        $"({_img.FreeBlocks() * 512 / 1024} КБ)";
        }

        public string SelectedName() =>
            _lv.SelectedItems.Count > 0 ? _lv.SelectedItems[0].Text : null;

        public System.Collections.Generic.List<string> SelectedNames()
        {
            var result = new System.Collections.Generic.List<string>();
            foreach (ListViewItem it in _lv.SelectedItems) result.Add(it.Text);
            return result;
        }

        public void SelectAll_()
        {
            foreach (ListViewItem it in _lv.Items) it.Selected = true;
            _lv.Focus();
        }

        public byte[] Extract(string n) => _img?.Extract(n);
        public bool Add(string n, byte[] b) => _img != null && _img.AddFile(n, b);
        public void Delete(string n) => _img?.DeleteFile(n);
        public bool NeedsSqueeze() => _img != null && _img.NeedsSqueeze();
        public int Squeeze() => _img?.Squeeze() ?? 0;

        public bool Rename(string oldName, string newName, out string error)
        {
            error = null;
            if (_img == null) { error = "образ не открыт"; return false; }
            if (!_img.RenameFile(oldName, newName)) { error = "имя занято или файл не найден"; return false; }
            return true;
        }
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
