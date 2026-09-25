using System;
using System.Collections.Generic;
using System.Text;

namespace CompMacro11
{
    // ── Разбор файла RT-11 .SAV ───────────────────────────────────────
    //   Образ памяти: байт файла со смещением N — это адрес N. Первые
    //   512 байт (0..777) — служебная область, код обычно с 001000.
    //   Заголовок (восьмеричные адреса):
    //     40 — точка входа, 42 — начальный стек, 44 — слово состояния
    //     задания (JSW), 46 — адрес загрузки USR, 50 — верхняя граница.
    //
    //   Дальше программа обходится от точки входа: команды прослеживаются
    //   по потоку управления, переходы и вызовы дают новые точки входа.
    //   Что обходом не покрыто — раскладывается на строки (по печатным
    //   символам, включая русские в КОИ-8) и данные (.WORD).
    public static class SavView
    {
        const int Header = 512;

        // виды байтов
        const byte UNK = 0, CODE = 1, TEXT = 2, DATA = 3, HDR = 4;

        public static string Render(byte[] img, string name)
        {
            var sb = new StringBuilder();
            int len = img.Length;
            if (len < Header + 2) return "; файл короче заголовка .SAV\r\n";

            int start = W(img, 0x20);      // 040
            int stack = W(img, 0x22);      // 042
            int jsw = W(img, 0x24);        // 044
            int usr = W(img, 0x26);        // 046
            int high = W(img, 0x28);       // 050

            int entry = Plausible(start, len) ? start : 0x200;   // иначе 001000

            var kind = new byte[len];
            for (int i = 0; i < Header && i < len; i++) kind[i] = HDR;

            var labels = new Dictionary<int, string>();
            var subs = new List<int>();
            Walk(img, kind, entry, labels, subs);
            if (entry != 0x200 && Plausible(0x200, len)) Walk(img, kind, 0x200, labels, subs);

            MarkText(img, kind);
            for (int i = Header; i < len; i++) if (kind[i] == UNK) kind[i] = DATA;

            int nCode = 0, nText = 0, nData = 0;
            for (int i = Header; i < len; i++)
            {
                if (kind[i] == CODE) nCode++;
                else if (kind[i] == TEXT) nText++;
                else if (kind[i] == DATA) nData++;
            }

            // ── шапка ──
            sb.Append("; ══════════════════════════════════════════════════════════\r\n");
            sb.Append("; ").Append(name).Append("   RT-11 SAV, ").Append(len)
              .Append(" байт (").Append((len + 511) / 512).Append(" бл.)\r\n");
            sb.Append("; ══════════════════════════════════════════════════════════\r\n");
            sb.Append("; Точка входа (40):     ").Append(O6(start))
              .Append(start == entry ? "" : "   (не похожа на адрес, обход начат с 001000)").Append("\r\n");
            sb.Append("; Начальный стек (42):  ").Append(O6(stack)).Append("\r\n");
            sb.Append("; JSW (44):             ").Append(O6(jsw)).Append(Jsw(jsw)).Append("\r\n");
            sb.Append("; Загрузка USR (46):    ").Append(O6(usr)).Append("\r\n");
            sb.Append("; Верхняя граница (50): ").Append(O6(high)).Append("\r\n");
            sb.Append("; Разобрано: код ").Append(nCode).Append(" б., текст ").Append(nText)
              .Append(" б., данные ").Append(nData).Append(" б.\r\n");
            sb.Append("; Меток: ").Append(labels.Count).Append(", подпрограмм: ").Append(subs.Count).Append("\r\n");
            if (subs.Count > 0)
            {
                subs.Sort();
                sb.Append(";\r\n; Подпрограммы (JSR):");
                for (int i = 0; i < subs.Count; i++)
                {
                    if (i % 8 == 0) sb.Append("\r\n;   ");
                    sb.Append(O6(subs[i])).Append(' ');
                }
                sb.Append("\r\n");
            }
            sb.Append(";\r\n; Вид: код — команды, текст — .ASCII, остальное — .WORD\r\n");
            sb.Append("; ══════════════════════════════════════════════════════════\r\n\r\n");
            sb.Append("        .ASECT\r\n        . = ").Append(O6(Header)).Append("\r\n\r\n");

            // ── тело ──
            int a = Header;
            while (a < len)
            {
                byte k = kind[a];
                if (k == CODE) a = EmitCode(sb, img, kind, labels, a, entry);
                else if (k == TEXT) a = EmitText(sb, img, kind, labels, a);
                else a = EmitData(sb, img, kind, labels, a);
            }
            sb.Append("\r\n        .END    ").Append(O6(entry)).Append("\r\n");
            return sb.ToString();
        }

        // ── обход кода от точки входа ──
        static void Walk(byte[] img, byte[] kind, int entry,
                         Dictionary<int, string> labels, List<int> subs)
        {
            var queue = new Queue<int>();
            queue.Enqueue(entry);
            int guard = 0;

            while (queue.Count > 0 && guard++ < 100000)
            {
                int a = queue.Dequeue();
                while (a >= 0 && a + 1 < img.Length && kind[a] == UNK)
                {
                    int l, t;
                    bool stop, call;
                    Macro11.Flow(img, 0, a, out l, out t, out stop, out call);
                    if (l <= 0) break;
                    for (int i = 0; i < l && a + i < img.Length; i++) kind[a + i] = CODE;

                    if (t >= 0 && t + 1 < img.Length && t >= Header)
                    {
                        if (!labels.ContainsKey(t)) labels[t] = "L" + O6(t);
                        if (call && !subs.Contains(t)) subs.Add(t);
                        if (kind[t] == UNK) queue.Enqueue(t);
                    }
                    if (stop) break;
                    a += l;
                }
            }
        }

        // ── строки: подряд идущие печатные байты (ASCII и КОИ-8) ──
        static void MarkText(byte[] img, byte[] kind)
        {
            int run = 0;
            for (int i = Header; i <= img.Length; i++)
            {
                bool ok = i < img.Length && kind[i] == UNK && Printable(img[i]);
                if (ok) { run++; continue; }
                if (run >= 5)
                    for (int j = i - run; j < i; j++) kind[j] = TEXT;
                run = 0;
            }
        }

        static bool Printable(byte b)
        {
            if (b == 9 || b == 10 || b == 13) return true;
            if (b >= 32 && b < 127) return true;
            return b >= 0xC0;                       // русские буквы КОИ-8
        }

        // ── код ──
        static int EmitCode(StringBuilder sb, byte[] img, byte[] kind,
                            Dictionary<int, string> labels, int a, int entry)
        {
            while (a + 1 < img.Length && kind[a] == CODE)
            {
                if (a == entry) sb.Append("START:\r\n");
                else if (labels.ContainsKey(a)) sb.Append(labels[a]).Append(":\r\n");

                int pc = a;
                string text = Macro11.Decode(img, 0, ref pc);
                if (pc <= a) pc = a + 2;
                if (pc > img.Length) pc = img.Length;
                // команда не должна залезать в строки или данные
                for (int x = a + 2; x < pc; x++)
                    if (kind[x] != CODE) { pc = x; break; }

                var words = new StringBuilder();
                for (int x = a; x + 1 < pc; x += 2)
                    words.Append(O6(img[x] | (img[x + 1] << 8))).Append(' ');

                sb.Append(O6(a)).Append("  ").Append(words.ToString().PadRight(21))
                  .Append("        ").Append(text.PadRight(24)).Append(Note(img, a)).Append("\r\n");
                a = pc;
            }
            return a;
        }

        // подсказка для запросов к RT-11
        static string Note(byte[] img, int a)
        {
            int w = img[a] | (img[a + 1 < img.Length ? a + 1 : a] << 8);
            if (w >= 0x88F4 && w <= 0x88FF) return "; запрос к RT-11 (EMT)";
            if (w == 0x8900) return "; TRAP 0";
            return "";
        }

        // ── строки ──
        static int EmitText(StringBuilder sb, byte[] img, byte[] kind,
                            Dictionary<int, string> labels, int a)
        {
            int start = a;
            var line = new StringBuilder();
            if (labels.ContainsKey(a)) sb.Append(labels[a]).Append(":\r\n");

            while (a < img.Length && kind[a] == TEXT)
            {
                byte b = img[a];
                if (b == 13 || b == 10 || b == 9 || b < 32)
                {
                    Flush(sb, start, line);
                    sb.Append(O6(a)).Append("          .BYTE   ").Append(Convert.ToString(b, 8)).Append("\r\n");
                    a++; start = a;
                    continue;
                }
                line.Append(Ch(b));
                a++;
                if (line.Length >= 48) { Flush(sb, start, line); start = a; }
            }
            Flush(sb, start, line);
            return a;
        }

        static void Flush(StringBuilder sb, int addr, StringBuilder line)
        {
            if (line.Length == 0) return;
            sb.Append(O6(addr)).Append("          .ASCII  /").Append(line).Append("/\r\n");
            line.Length = 0;
        }

        // ── данные ──
        static int EmitData(StringBuilder sb, byte[] img, byte[] kind,
                            Dictionary<int, string> labels, int a)
        {
            while (a < img.Length && kind[a] == DATA)
            {
                if (labels.ContainsKey(a)) sb.Append(labels[a]).Append(":\r\n");

                var words = new StringBuilder();
                var asc = new StringBuilder();
                int shown = 0, x = a;
                while (x + 1 < img.Length && kind[x] == DATA && shown < 8)
                {
                    if (shown > 0 && labels.ContainsKey(x)) break;
                    int w = img[x] | (img[x + 1] << 8);
                    if (shown > 0) words.Append(',');
                    words.Append(O6(w));
                    asc.Append(Vis(img[x])).Append(Vis(img[x + 1]));
                    x += 2; shown++;
                }
                if (shown == 0)
                {
                    sb.Append(O6(a)).Append("          .BYTE   ")
                      .Append(Convert.ToString(img[a], 8)).Append("\r\n");
                    return a + 1;
                }
                sb.Append(O6(a)).Append("          .WORD   ").Append(words.ToString().PadRight(55))
                  .Append("; ").Append(asc).Append("\r\n");
                a = x;
            }
            return a;
        }

        // ── мелочи ──
        static int W(byte[] d, int off)
        {
            return (off + 1 < d.Length) ? (d[off] | (d[off + 1] << 8)) : 0;
        }

        static bool Plausible(int addr, int len)
        {
            return addr >= Header && addr + 1 < len && (addr & 1) == 0;
        }

        static string O6(int v) { return Convert.ToString(v & 0xFFFF, 8).PadLeft(6, '0'); }

        static string Jsw(int jsw)
        {
            var sb = new StringBuilder();
            if ((jsw & 0x4000) != 0) sb.Append(" TT спецрежим");
            if ((jsw & 0x2000) != 0) sb.Append(" REENTER");
            if ((jsw & 0x0100) != 0) sb.Append(" оверлеи");
            if ((jsw & 0x0400) != 0) sb.Append(" виртуальная память");
            return sb.Length > 0 ? "  —" + sb : "";
        }

        // КОИ-8: строчные 192..223, заглавные 224..255
        static char Ch(byte b)
        {
            const string low = "юабцдефгхийклмнопярстужвьызшэщчъ";
            const string up = "ЮАБЦДЕФГХИЙКЛМНОПЯРСТУЖВЬЫЗШЭЩЧЪ";
            if (b >= 32 && b < 127) return (char)b;
            if (b >= 192 && b < 192 + low.Length) return low[b - 192];
            if (b >= 224 && b < 224 + up.Length) return up[b - 224];
            return '.';
        }

        static char Vis(byte b) { return (b >= 32 && b < 127) ? (char)b : '.'; }
    }
}
