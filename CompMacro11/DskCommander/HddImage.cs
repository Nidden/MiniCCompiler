using System;
using System.Collections.Generic;
using System.IO;

namespace DskCommander
{
    // ── Винчестер УКНЦ ────────────────────────────────────────────────
    //   По документации UKNCBTL (wiki IDE-HDD-ru): в первом секторе диска
    //   записана таблица разделов; первые два байта — число секторов на
    //   дорожке и число головок (нужны контроллеру для пересчёта номера
    //   блока RT-11 в CHS), дальше размеры разделов по одному слову в
    //   блоках RT-11, в конце сектора два слова контрольной суммы.
    //   Разделов до 24 (прошивка WDROM) или до 8 (HDBOOT).
    //
    //   Сектор 0 — таблица разделов: первое слово (геометрия) не нужно,
    //   дальше подряд идут размеры разделов В БЛОКАХ, пока не встретится
    //   0 или 0177777; за ними мусор и контрольная сумма — их не читаем.
    //   Сами данные начинаются с сектора 1: разделы лежат подряд, каждый
    //   раздел — обычный том RT-11, то есть тот же .dsk.
    //
    //   Образ может быть записан инверсно (все байты дополнены до 0xFF).
    //   Признак — в первых 512 байтах очень много 0xFF. Инверсия снимается
    //   при чтении и возвращается при записи (см. DskImage), поэтому всё
    //   остальное о ней не знает.
    public class HddImage
    {
        public class Part
        {
            public int Index;               // номер раздела, с 0
            public int StartBlock;          // первый блок раздела в файле
            public int Blocks;              // длина в блоках
        }

        public string Path { get; private set; }
        public bool Inverted { get; private set; }
        public int TotalBlocks { get; private set; }
        public List<Part> Parts { get; private set; }

        const int MaxParts = 32;            // по описанию бывает до 24
        const int MinPartBlocks = 16;       // меньше — заведомо мусор

        private HddImage() { Parts = new List<Part>(); }

        // Открыть, если файл похож на винчестер; иначе null (значит .dsk).
        //   force — открывать как винчестер, даже если проверки не сошлись
        //   (пункт меню «Открыть как винчестер»). Файлы .hdd открываются
        //   так всегда: раз названо винтом — показываем разделы.
        //
        //   Полярность по количеству 0xFF — только подсказка: разбираем
        //   оба варианта и берём тот, где больше разделов похожи на том
        //   RT-11, а при равенстве — тот, чья сумма размеров ближе к длине
        //   файла.
        // polarity: 0 — определить самому, 1 — читать как есть,
        //           2 — читать инверсно (образ записан дополнением до 0xFF)
        public static HddImage TryOpen(string path) { return TryOpen(path, false, 0); }
        public static HddImage TryOpen(string path, bool force) { return TryOpen(path, force, 0); }

        public static HddImage TryOpen(string path, bool force, int polarity)
        {
            byte[] sec0;
            long fileLen;
            try
            {
                using (var df = DiskFile.Open(path))
                {
                    fileLen = df.Length;
                    if (fileLen < 2 * 512) return null;
                    sec0 = df.ReadChunk(0, 512);
                    if (sec0.Length < 512) return null;
                }
            }
            catch { return null; }

            // полярность задана руками — никаких проверок, читаем как сказано
            if (polarity == 1 || polarity == 2)
                return Parse(path, sec0, fileLen, polarity == 2, true);

            int ff = 0;
            foreach (var b in sec0) if (b == 0xFF) ff++;
            bool likely = ff > 256;                     // «дофига 0xFF» — скорее всего инверсный

            var a = Parse(path, sec0, fileLen, likely, force);
            var b2 = Parse(path, sec0, fileLen, !likely, force);
            var best = Better(a, b2);
            if (best == null) return null;

            if (best.Rt11Parts == 0 && !force && !LooksLikeDiskName(path)) return null;   // это .dsk, а не винт
            return best;
        }

        // Расширения, которыми называют винчестер: для них таблицу
        // разделов читаем, даже если проверка на RT-11 не сошлась
        // (образ может быть от другой системы или размечен иначе).
        public static bool LooksLikeDiskName(string path)
        {
            string e = System.IO.Path.GetExtension(path);
            return string.Equals(e, ".hdd", StringComparison.OrdinalIgnoreCase)
                || string.Equals(e, ".img", StringComparison.OrdinalIgnoreCase);
        }

        // Разбор сектора 0 без всяких проверок — для окна «Сведения».
        public static HddImage Probe(string path, bool inverted)
        {
            try
            {
                using (var df = DiskFile.Open(path))
                {
                    var sec0 = df.ReadChunk(0, 512);
                    if (sec0.Length < 512) return null;
                    return Parse(path, sec0, df.Length, inverted, true);
                }
            }
            catch { return null; }
        }

        // какой разбор правдоподобнее: сначала по числу разделов, похожих
        // на RT-11, затем по правдоподобию геометрии, затем по тому, чья
        // сумма размеров ближе к длине файла
        static HddImage Better(HddImage a, HddImage b)
        {
            if (a == null) return b;
            if (b == null) return a;
            if (a.Rt11Parts != b.Rt11Parts) return a.Rt11Parts > b.Rt11Parts ? a : b;
            if (a.GeometryOk != b.GeometryOk) return a.GeometryOk ? a : b;
            return a.Slack <= b.Slack ? a : b;
        }

        public int Rt11Parts { get; private set; }      // сколько разделов похожи на RT-11
        public int Sectors { get; private set; }        // секторов на дорожке (байт 0)
        public int Heads { get; private set; }          // головок (байт 1)
        public bool GeometryOk { get; private set; }    // геометрия правдоподобна
        long Slack;                                     // насколько сумма разделов не сошлась с файлом

        // цилиндров по геометрии — контроллер требует меньше 1024
        public int Cylinders
        {
            get
            {
                int div = Sectors * Heads;
                return div > 0 ? (int)(TotalBlocks / div) : 0;
            }
        }

        // lenient — не отбрасывать «мелкие» разделы: при ручном выборе
        // полярности и при «открыть как винчестер» решает пользователь
        static HddImage Parse(string path, byte[] sec0raw, long fileLen, bool inv, bool lenient)
        {
            var sec0 = (byte[])sec0raw.Clone();
            if (inv) for (int i = 0; i < sec0.Length; i++) sec0[i] = (byte)~sec0[i];

            long totalBlocks = fileLen / 512;
            var hdd = new HddImage
            {
                Path = path,
                Inverted = inv,
                TotalBlocks = (int)Math.Min(int.MaxValue, totalBlocks),
                Sectors = sec0[0],
                Heads = sec0[1]
            };
            hdd.GeometryOk = hdd.Sectors >= 1 && hdd.Sectors <= 63
                          && hdd.Heads >= 1 && hdd.Heads <= 16
                          && hdd.Cylinders > 0 && hdd.Cylinders < 1024;

            int start = 1;                             // данные идут с сектора 1
            for (int w = 1; w <= MaxParts; w++)
            {
                int size = sec0[w * 2] | (sec0[w * 2 + 1] << 8);
                if (size == 0 || size == 0xFFFF) break;        // таблица кончилась
                if (!lenient && size < MinPartBlocks) break;    // дальше пошёл мусор
                if (size < 1) break;
                if (start >= totalBlocks) break;               // за концом файла
                hdd.Parts.Add(new Part { Index = hdd.Parts.Count, StartBlock = start, Blocks = size });
                start += size;
            }
            if (hdd.Parts.Count == 0) return null;

            foreach (var p in hdd.Parts)
                if (LooksLikeRt11(path, p, inv, totalBlocks)) hdd.Rt11Parts++;
            hdd.Slack = Math.Abs(totalBlocks - start);
            return hdd;
        }

        // Заголовок каталога RT-11 лежит в блоке 6 тома: 5 слов —
        // всего сегментов, следующий сегмент, старший использованный,
        // доп. байты, первый блок данных.
        static bool LooksLikeRt11(string path, Part p, bool inv, long totalBlocks)
        {
            long off = ((long)p.StartBlock + 6) * 512;
            if (off + 10 > totalBlocks * 512) return false;
            byte[] hdr;
            try
            {
                using (var df = DiskFile.Open(path))
                    hdr = df.ReadChunk(off, 10);
                if (hdr.Length < 10) return false;
            }
            catch { return false; }
            if (inv) for (int i = 0; i < hdr.Length; i++) hdr[i] = (byte)~hdr[i];

            int segs = hdr[0] | (hdr[1] << 8);
            int next = hdr[2] | (hdr[3] << 8);
            int high = hdr[4] | (hdr[5] << 8);
            int dataStart = hdr[8] | (hdr[9] << 8);
            if (segs < 1 || segs > 31) return false;
            if (next > segs) return false;
            if (high < 1 || high > segs) return false;
            if (dataStart < 6 + 2 * segs || dataStart >= p.Blocks) return false;
            return true;
        }

        // Открыть раздел как обычный том RT-11.
        public DskImage OpenPartition(int index)
        {
            if (index < 0 || index >= Parts.Count) return null;
            var p = Parts[index];
            return new DskImage(Path, p.StartBlock, p.Blocks, Inverted);
        }
    }
}
