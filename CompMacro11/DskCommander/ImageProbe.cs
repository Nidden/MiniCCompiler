using System;
using System.Text;

namespace DskCommander
{
    // ── «Сведения о файле образа» ─────────────────────────────────────
    //   Показывает, что именно программа увидела в файле: контейнер,
    //   сектор 0 в восьмеричном виде (в обеих полярностях), разбор
    //   таблицы разделов и проверку разделов на RT-11. Нужно, когда
    //   образ не открывается: по этому тексту видно, на каком шаге
    //   ожидания разошлись с файлом.
    public static class ImageProbe
    {
        public static string Describe(string path)
        {
            var sb = new StringBuilder();
            sb.Append("Файл: ").Append(path).Append("\r\n");

            byte[] sec0;
            long len;
            using (var df = DiskFile.Open(path))
            {
                sb.Append(df.Describe().Replace("\n", "\r\n")).Append("\r\n");
                len = df.Length;
                sec0 = df.ReadChunk(0, 512);
            }
            if (sec0.Length < 512)
            {
                sb.Append("\r\nСектор 0 прочитать не удалось (получено ")
                  .Append(sec0.Length).Append(" байт).");
                return sb.ToString();
            }

            int ff = 0, zero = 0;
            foreach (var b in sec0) { if (b == 0xFF) ff++; if (b == 0) zero++; }
            sb.Append("\r\nВ секторе 0: 0xFF — ").Append(ff)
              .Append(" байт, нулей — ").Append(zero).Append(" из 512\r\n");

            sb.Append("\r\nГеометрия (первые два байта сектора 0, док. UKNCBTL):\r\n");
            foreach (bool inv in new[] { false, true })
            {
                int sec = inv ? (~sec0[0]) & 0xFF : sec0[0];
                int hd = inv ? (~sec0[1]) & 0xFF : sec0[1];
                long cyl = (sec * hd) > 0 ? (len / 512) / (sec * hd) : 0;
                sb.Append(inv ? "  инверсно:  " : "  как есть:  ")
                  .Append("секторов ").Append(sec).Append(", головок ").Append(hd)
                  .Append(", цилиндров ").Append(cyl)
                  .Append(sec >= 1 && sec <= 63 && hd >= 1 && hd <= 16 && cyl > 0 && cyl < 1024
                          ? "   → правдоподобно" : "   → не похоже")
                  .Append("\r\n");
            }

            sb.Append("\r\nПоследние слова сектора 0 (там два слова контрольной суммы):\r\n");
            foreach (bool inv in new[] { false, true })
            {
                sb.Append(inv ? "  инверсно:  " : "  как есть:  ");
                for (int i = 252; i < 256; i++)
                {
                    int lo = sec0[i * 2], hi = sec0[i * 2 + 1];
                    if (inv) { lo = (~lo) & 0xFF; hi = (~hi) & 0xFF; }
                    sb.Append(Convert.ToString(lo | (hi << 8), 8).PadLeft(6, '0')).Append(' ');
                }
                long sum = 0;
                for (int i = 0; i < 254; i++)
                {
                    int lo = sec0[i * 2], hi = sec0[i * 2 + 1];
                    if (inv) { lo = (~lo) & 0xFF; hi = (~hi) & 0xFF; }
                    sum += lo | (hi << 8);
                }
                sb.Append("  сумма слов 0..253 = ").Append(Convert.ToString(sum & 0xFFFFFFFF, 8)).Append("\r\n");
            }

            sb.Append("\r\n── Сектор 0, первые 24 слова (восьмеричные) ──\r\n");
            sb.Append("как есть:  ").Append(Words(sec0, false)).Append("\r\n");
            sb.Append("инверсно:  ").Append(Words(sec0, true)).Append("\r\n");

            foreach (bool inv in new[] { false, true })
            {
                sb.Append("\r\n── Разбор таблицы разделов (")
                  .Append(inv ? "инверсно" : "как есть").Append(") ──\r\n");
                var hdd = HddImage.Probe(path, inv);
                if (hdd == null)
                {
                    sb.Append("разделов не найдено\r\n");
                    continue;
                }
                sb.Append("разделов: ").Append(hdd.Parts.Count)
                  .Append(", похожи на RT-11: ").Append(hdd.Rt11Parts).Append("\r\n");
                foreach (var p in hdd.Parts)
                    sb.Append(string.Format("  раздел {0,2}: блок {1,7}  длина {2,6} бл. ({3,6} КБ)\r\n",
                                            p.Index, p.StartBlock, p.Blocks, p.Blocks / 2));
                long tail = len / 512 - (hdd.Parts[hdd.Parts.Count - 1].StartBlock
                                       + hdd.Parts[hdd.Parts.Count - 1].Blocks);
                sb.Append("  остаток диска после последнего раздела: ").Append(tail).Append(" бл.\r\n");
            }

            sb.Append("\r\n── Проверка на одиночный том RT-11 (без разделов) ──\r\n");
            sb.Append(VolumeInfo(path, 0));
            sb.Append("\r\n── То же, если данные начинаются с блока 1 ──\r\n");
            sb.Append(VolumeInfo(path, 1));

            sb.Append("\r\n── Что решила программа ──\r\n");
            var auto = HddImage.TryOpen(path);
            if (auto != null)
                sb.Append("винчестер, разделов ").Append(auto.Parts.Count)
                  .Append(auto.Inverted ? ", запись инверсная" : ", запись обычная").Append("\r\n");
            else
                sb.Append("не винчестер — открывается как одиночный том\r\n");

            sb.Append("\r\n── Начало файла, 64 байта ──\r\n").Append(Hex(sec0, 64));
            return sb.ToString();
        }

        // заголовок каталога RT-11 для тома, начинающегося с startBlock
        static string VolumeInfo(string path, int startBlock)
        {
            try
            {
                using (var df = DiskFile.Open(path))
                {
                    var hdr = df.ReadChunk(((long)startBlock + 6) * 512, 10);
                    if (hdr.Length < 10) return "прочитать блок 6 не удалось\r\n";
                    var sb = new StringBuilder();
                    foreach (bool inv in new[] { false, true })
                    {
                        var h = (byte[])hdr.Clone();
                        if (inv) for (int i = 0; i < h.Length; i++) h[i] = (byte)~h[i];
                        int segs = h[0] | (h[1] << 8);
                        int next = h[2] | (h[3] << 8);
                        int high = h[4] | (h[5] << 8);
                        int ds = h[8] | (h[9] << 8);
                        sb.Append(inv ? "  инверсно:  " : "  как есть:  ")
                          .Append("сегментов ").Append(segs)
                          .Append(", следующий ").Append(next)
                          .Append(", старший ").Append(high)
                          .Append(", данные с блока ").Append(ds)
                          .Append(segs >= 1 && segs <= 31 && next <= segs && high >= 1 && high <= segs
                                  && ds >= 6 + 2 * segs ? "   → похоже на RT-11" : "   → не похоже")
                          .Append("\r\n");
                    }
                    return sb.ToString();
                }
            }
            catch (Exception ex) { return "ошибка: " + ex.Message + "\r\n"; }
        }

        static string Words(byte[] b, bool inv)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 24; i++)
            {
                int lo = b[i * 2], hi = b[i * 2 + 1];
                if (inv) { lo = (~lo) & 0xFF; hi = (~hi) & 0xFF; }
                sb.Append(Convert.ToString(lo | (hi << 8), 8).PadLeft(6, '0')).Append(' ');
                if (i == 11) sb.Append("\r\n           ");
            }
            return sb.ToString().TrimEnd();
        }

        static string Hex(byte[] d, int count)
        {
            var sb = new StringBuilder();
            int n = Math.Min(count, d.Length);
            for (int i = 0; i < n; i += 16)
            {
                sb.Append(i.ToString("X4")).Append("  ");
                for (int j = 0; j < 16; j++)
                    sb.Append(i + j < n ? d[i + j].ToString("X2") + " " : "   ");
                sb.Append(" |");
                for (int j = 0; j < 16 && i + j < n; j++)
                {
                    byte c = d[i + j];
                    sb.Append(c >= 32 && c < 127 ? (char)c : '.');
                }
                sb.Append("|\r\n");
            }
            return sb.ToString();
        }
    }
}
