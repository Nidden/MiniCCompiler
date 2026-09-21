using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace CompMacro11
{
    // ── Конвертер картинки 320×264 под экран УКНЦ ────────────────────
    //   Использует построчное переключение яркости (см. TSPAL.MAC,
    //   dr.Titus) — каждая строка экрана независимо выбирает обычный
    //   или "яркий" вариант тех же 8 подтверждённых цветов.
    //
    //   ЧЕСТНО: 8 базовых цветов (PAL8) подтверждены на реальном
    //   железе в этом же проекте (см. Spriteeditor.cs). Формула
    //   "яркого" варианта — смешение с белым — ДОПУЩЕНИЕ, не
    //   проверено на УКНЦ. Ползунок ниже даёт покрутить это живьём
    //   и увидеть, где допущение выглядит плохо (например, "яркий
    //   чёрный" превращается в серый, которого на УКНЦ не бывает).
    public class ImageConverter : Form
    {
        static readonly Color C_BG = Color.FromArgb(28, 28, 28);
        static readonly Color C_BG2 = Color.FromArgb(37, 37, 38);
        static readonly Color C_BG3 = Color.FromArgb(22, 22, 22);
        static readonly Color C_TEXT = Color.FromArgb(212, 212, 212);
        static readonly Color C_ACCENT = Color.FromArgb(20, 115, 230);
        static readonly Color C_GRAY = Color.FromArgb(120, 120, 120);

        const int W = 320, H = 264;

        // Подтверждённые на железе 8 цветов — те же, что в Spriteeditor.cs
        static readonly Color[] PAL8_NORMAL =
        {
            Color.Black, Color.Blue, Color.Red, Color.Magenta,
            Color.Green, Color.Cyan, Color.Yellow, Color.White
        };

        Bitmap _original;
        Bitmap _converted;
        int[] _lineBright;      // 0/1 на каждую из H строк — что реально выбрано
        PictureBox _picOrig, _picConv;
        Label _statusLbl, _threshLbl, _blendLbl;
        TrackBar _threshSlider;   // порог яркости строки, при котором берётся "яркая" палитра
        TrackBar _blendSlider;    // сила смешения "яркого" варианта с белым

        public ImageConverter()
        {
            Text = "Картинка → УКНЦ (320×264, построчная яркость)";
            Size = new Size(1000, 620);
            MinimumSize = new Size(820, 520);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = C_BG;
            ForeColor = C_TEXT;
            Font = new Font("Segoe UI", 9f);

            // ── Верхняя панель — загрузка и параметры ──
            var top = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = C_BG2 };

            var btnLoad = new Button
            {
                Text = "📂 Загрузить PNG",
                Location = new Point(10, 8),
                Width = 150,
                Height = 28,
                FlatStyle = FlatStyle.Flat,
                BackColor = C_ACCENT,
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            btnLoad.FlatAppearance.BorderSize = 0;
            btnLoad.Click += (s, e) => LoadImage();

            var btnExport = new Button
            {
                Text = "💾 Экспорт данных",
                Location = new Point(168, 8),
                Width = 150,
                Height = 28,
                FlatStyle = FlatStyle.Flat,
                BackColor = C_BG3,
                ForeColor = Color.White,
                Cursor = Cursors.Hand
            };
            btnExport.FlatAppearance.BorderSize = 0;
            btnExport.Click += (s, e) => ExportData();

            _threshLbl = new Label { Text = "Порог яркости строки: 127", Location = new Point(10, 42), AutoSize = true, ForeColor = C_TEXT };
            _threshSlider = new TrackBar
            {
                Location = new Point(10, 58),
                Width = 220,
                Minimum = 0,
                Maximum = 255,
                Value = 127,
                TickStyle = TickStyle.None
            };
            _threshSlider.ValueChanged += (s, e) => { _threshLbl.Text = $"Порог яркости строки: {_threshSlider.Value}"; Reconvert(); };

            _blendLbl = new Label { Text = "Сила \"яркого\" (0-100%): 50%", Location = new Point(240, 42), AutoSize = true, ForeColor = C_TEXT };
            _blendSlider = new TrackBar
            {
                Location = new Point(240, 58),
                Width = 220,
                Minimum = 0,
                Maximum = 100,
                Value = 50,
                TickStyle = TickStyle.None
            };
            _blendSlider.ValueChanged += (s, e) => { _blendLbl.Text = $"Сила \"яркого\" (0-100%): {_blendSlider.Value}%"; Reconvert(); };

            top.Controls.AddRange(new Control[] { btnLoad, btnExport, _threshLbl, _threshSlider, _blendLbl, _blendSlider });

            // ── Нижняя панель — статус ──
            var bot = new Panel { Dock = DockStyle.Bottom, Height = 26, BackColor = C_BG2 };
            _statusLbl = new Label { Text = "Загрузите PNG 320×264, чтобы начать.", Dock = DockStyle.Fill, ForeColor = C_GRAY, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) };
            bot.Controls.Add(_statusLbl);

            // ── Центр — два превью рядом ──
            var split = new SplitContainer { Dock = DockStyle.Fill, BackColor = C_BG };

            var origWrap = new Panel { Dock = DockStyle.Fill, BackColor = C_BG3, AutoScroll = true };
            var origHdr = new Label { Text = " Оригинал", Dock = DockStyle.Top, Height = 22, ForeColor = C_GRAY, BackColor = C_BG2 };
            _picOrig = new PictureBox { Location = new Point(4, 26), SizeMode = PictureBoxSizeMode.AutoSize, BackColor = C_BG };
            origWrap.Controls.Add(_picOrig);
            origWrap.Controls.Add(origHdr);

            var convWrap = new Panel { Dock = DockStyle.Fill, BackColor = C_BG3, AutoScroll = true };
            var convHdr = new Label { Text = " На экране УКНЦ (превью)", Dock = DockStyle.Top, Height = 22, ForeColor = C_GRAY, BackColor = C_BG2 };
            _picConv = new PictureBox { Location = new Point(4, 26), SizeMode = PictureBoxSizeMode.AutoSize, BackColor = C_BG };
            convWrap.Controls.Add(_picConv);
            convWrap.Controls.Add(convHdr);

            split.Panel1.Controls.Add(origWrap);
            split.Panel2.Controls.Add(convWrap);

            Controls.Add(split);
            Controls.Add(bot);
            Controls.Add(top);

            Shown += (s, e) => { split.SplitterDistance = ClientSize.Width / 2; };
        }

        void LoadImage()
        {
            using (var dlg = new OpenFileDialog { Filter = "Изображения (*.png;*.bmp;*.jpg)|*.png;*.bmp;*.jpg" })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                try
                {
                    var loaded = new Bitmap(dlg.FileName);
                    if (loaded.Width != W || loaded.Height != H)
                    {
                        _statusLbl.Text = $"Внимание: картинка {loaded.Width}×{loaded.Height}, а нужно {W}×{H} — растягиваю.";
                        var resized = new Bitmap(W, H);
                        using (var g = Graphics.FromImage(resized))
                            g.DrawImage(loaded, 0, 0, W, H);
                        loaded.Dispose();
                        loaded = resized;
                    }
                    _original = loaded;
                    _picOrig.Image = _original;
                    Reconvert();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Не удалось загрузить: " + ex.Message, "Ошибка");
                }
            }
        }

        Color Brighten(Color c, double amount)
        {
            int r = c.R + (int)((255 - c.R) * amount);
            int g = c.G + (int)((255 - c.G) * amount);
            int b = c.B + (int)((255 - c.B) * amount);
            return Color.FromArgb(r, g, b);
        }

        int NearestIndex(Color px, Color[] pal)
        {
            int bestI = 0; long bestD = long.MaxValue;
            for (int i = 0; i < pal.Length; i++)
            {
                long dr = px.R - pal[i].R, dg = px.G - pal[i].G, db = px.B - pal[i].B;
                long d = dr * dr + dg * dg + db * db;
                if (d < bestD) { bestD = d; bestI = i; }
            }
            return bestI;
        }

        void Reconvert()
        {
            if (_original == null) return;

            double blend = _blendSlider.Value / 100.0;
            int threshold = _threshSlider.Value;
            var palBright = new Color[8];
            for (int i = 0; i < 8; i++) palBright[i] = Brighten(PAL8_NORMAL[i], blend);

            _converted = new Bitmap(W, H);
            _lineBright = new int[H];

            var srcData = _original.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int stride = srcData.Stride;
            byte[] srcBytes = new byte[stride * H];
            System.Runtime.InteropServices.Marshal.Copy(srcData.Scan0, srcBytes, 0, srcBytes.Length);
            _original.UnlockBits(srcData);

            var dstData = _converted.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            int dstStride = dstData.Stride;
            byte[] dstBytes = new byte[dstStride * H];

            int brightLines = 0;
            for (int y = 0; y < H; y++)
            {
                long sum = 0;
                for (int x = 0; x < W; x++)
                {
                    int o = y * stride + x * 4;
                    sum += srcBytes[o] + srcBytes[o + 1] + srcBytes[o + 2];
                }
                double avg = sum / (double)(W * 3);
                bool bright = avg > threshold;
                _lineBright[y] = bright ? 1 : 0;
                if (bright) brightLines++;
                var pal = bright ? palBright : PAL8_NORMAL;

                for (int x = 0; x < W; x++)
                {
                    int o = y * stride + x * 4;
                    var px = Color.FromArgb(srcBytes[o + 2], srcBytes[o + 1], srcBytes[o]);
                    int idx = NearestIndex(px, pal);
                    var c = pal[idx];
                    int d = y * dstStride + x * 4;
                    dstBytes[d] = c.B; dstBytes[d + 1] = c.G; dstBytes[d + 2] = c.R; dstBytes[d + 3] = 255;
                }
            }

            System.Runtime.InteropServices.Marshal.Copy(dstBytes, 0, dstData.Scan0, dstBytes.Length);
            _converted.UnlockBits(dstData);

            _picConv.Image = _converted;
            _statusLbl.Text = $"{H} строк: ярких {brightLines}, обычных {H - brightLines}.  Порог {threshold}, сила яркости {_blendSlider.Value}%.";
        }

        void ExportData()
        {
            if (_converted == null || _lineBright == null) { MessageBox.Show("Сначала загрузите картинку."); return; }

            using (var dlg = new SaveFileDialog { Filter = "Данные планов (*.bin)|*.bin", FileName = "image_planes.bin" })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;

                var planes = new byte[H * 120];
                var srcData = _converted.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                int stride = srcData.Stride;
                byte[] bytes = new byte[stride * H];
                System.Runtime.InteropServices.Marshal.Copy(srcData.Scan0, bytes, 0, bytes.Length);
                _converted.UnlockBits(srcData);

                for (int y = 0; y < H; y++)
                {
                    int rowOff = y * 120;
                    for (int x = 0; x < W; x++)
                    {
                        int o = y * stride + x * 4;
                        var c = Color.FromArgb(bytes[o + 2], bytes[o + 1], bytes[o]);
                        int idx = NearestIndex(c, _lineBright[y] == 1
                            ? Array.ConvertAll(PAL8_NORMAL, cc => Brighten(cc, _blendSlider.Value / 100.0))
                            : PAL8_NORMAL);
                        int bit0 = idx & 1, bit1 = (idx >> 1) & 1, bit2 = (idx >> 2) & 1;
                        int byteI = x / 8, bitI = 7 - (x % 8);
                        if (bit0 != 0) planes[rowOff + byteI] |= (byte)(1 << bitI);
                        if (bit1 != 0) planes[rowOff + 40 + byteI] |= (byte)(1 << bitI);
                        if (bit2 != 0) planes[rowOff + 80 + byteI] |= (byte)(1 << bitI);
                    }
                }
                File.WriteAllBytes(dlg.FileName, planes);

                string linesPath = Path.ChangeExtension(dlg.FileName, null) + "_lines.txt";
                using (var w = new StreamWriter(linesPath))
                {
                    w.WriteLine("# строка: яркость (0=обычная, 1=яркая)");
                    for (int y = 0; y < H; y++) w.WriteLine($"{y}: {_lineBright[y]}");
                }

                MessageBox.Show($"Сохранено:\n{dlg.FileName}\n{linesPath}", "Готово");
            }
        }
    }
}
