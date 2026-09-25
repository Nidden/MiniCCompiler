using System;
using System.Text;

namespace DskCommander
{
    // ── Дизассемблер PDP-11 в текст MACRO-11 ──────────────────────────
    //   Используется в просмотрщике (F3): рядом со словами в восьмеричном
    //   виде показывается команда так, как её написали бы в исходнике.
    //
    //   Числа восьмеричные — как принято в MACRO-11. Режимы адресации:
    //     0 Rn      1 (Rn)     2 (Rn)+    3 @(Rn)+
    //     4 -(Rn)   5 @-(Rn)   6 X(Rn)    7 @X(Rn)
    //   Для R7 (PC) режимы читаются иначе: 2 → #число, 3 → @#адрес,
    //   6 → адрес (относительный), 7 → @адрес.
    public static class Macro11
    {
        // ── имена регистров ──
        static string Reg(int r)
        {
            if (r == 6) return "SP";
            if (r == 7) return "PC";
            return "R" + r;
        }

        static string Oct(int v) { return Convert.ToString(v & 0xFFFF, 8); }
        static string Oct6(int v) { return Convert.ToString(v & 0xFFFF, 8).PadLeft(6, '0'); }

        // двухоперандные: индекс = старшая тетрада слова
        static readonly string[] Dbl = {
            null, "MOV", "CMP", "BIT", "BIC", "BIS", "ADD", null,
            null, "MOVB", "CMPB", "BITB", "BICB", "BISB", "SUB", null
        };

        // однооперандные 0050DD..0067DD
        static readonly string[] Sng = {
            "CLR", "COM", "INC", "DEC", "NEG", "ADC", "SBC", "TST",
            "ROR", "ROL", "ASR", "ASL", "MARK", "MFPI", "MTPI", "SXT"
        };

        // байтовые 1050DD..1067DD (последние четыре — не просто «B»)
        static readonly string[] SngB = {
            "CLRB", "COMB", "INCB", "DECB", "NEGB", "ADCB", "SBCB", "TSTB",
            "RORB", "ROLB", "ASRB", "ASLB", "MTPS", "MFPD", "MTPD", "MFPS"
        };

        // ── операнд по шести битам режим/регистр ──
        //    pc — адрес следующего слова; если операнду нужно слово,
        //    оно берётся отсюда и pc сдвигается.
        static string Operand(int spec, byte[] d, int baseAddr, ref int pc)
        {
            int mode = (spec >> 3) & 7, reg = spec & 7;
            bool isPc = reg == 7;

            switch (mode)
            {
                case 0: return Reg(reg);
                case 1: return "(" + Reg(reg) + ")";
                case 2:
                    if (isPc) return "#" + Oct(Word(d, baseAddr, ref pc));
                    return "(" + Reg(reg) + ")+";
                case 3:
                    if (isPc) return "@#" + Oct(Word(d, baseAddr, ref pc));
                    return "@(" + Reg(reg) + ")+";
                case 4: return "-(" + Reg(reg) + ")";
                case 5: return "@-(" + Reg(reg) + ")";
                case 6:
                    {
                        int x = Word(d, baseAddr, ref pc);
                        if (isPc) return Oct((pc + x) & 0xFFFF);        // относительный адрес
                        return Oct(x) + "(" + Reg(reg) + ")";
                    }
                default:
                    {
                        int x = Word(d, baseAddr, ref pc);
                        if (isPc) return "@" + Oct((pc + x) & 0xFFFF);
                        return "@" + Oct(x) + "(" + Reg(reg) + ")";
                    }
            }
        }

        static int Word(byte[] d, int baseAddr, ref int pc)
        {
            int off = pc - baseAddr;
            int v = (off >= 0 && off + 1 < d.Length) ? (d[off] | (d[off + 1] << 8)) : 0;
            pc += 2;
            return v;
        }

        static int Peek(byte[] d, int baseAddr, int addr)
        {
            int off = addr - baseAddr;
            return (off >= 0 && off + 1 < d.Length) ? (d[off] | (d[off + 1] << 8)) : 0;
        }

        // ── одна команда; pc входит как адрес слова команды,
        //    выходит как адрес следующей команды ──
        public static string Decode(byte[] d, int baseAddr, ref int pc)
        {
            int addr = pc;
            int w = Word(d, baseAddr, ref pc);
            int top = (w >> 12) & 15;

            // двухоперандные
            if (Dbl[top] != null)
            {
                string src = Operand((w >> 6) & 0x3F, d, baseAddr, ref pc);
                string dst = Operand(w & 0x3F, d, baseAddr, ref pc);
                return Pad(Dbl[top]) + src + "," + dst;
            }

            if (top == 0)
            {
                if (w <= 7)
                {
                    string[] misc = { "HALT", "WAIT", "RTI", "BPT", "IOT", "RESET", "RTT", "MFPT" };
                    return misc[w];
                }
                if (w >= 0x40 && w <= 0x7F)      // 000100..000177 JMP
                    return Pad("JMP") + Operand(w & 0x3F, d, baseAddr, ref pc);
                if (w >= 0x80 && w <= 0x87)      // 000200..000207 RTS
                    return Pad("RTS") + Reg(w & 7);
                if (w >= 0x98 && w <= 0x9F)      // 000230..000237 SPL
                    return Pad("SPL") + Oct(w & 7);
                if (w >= 0xA0 && w <= 0xBF)      // 000240..000277 коды условий
                    return Cond(w);
                if (w >= 0xC0 && w <= 0xFF)      // 000300..000377 SWAB
                    return Pad("SWAB") + Operand(w & 0x3F, d, baseAddr, ref pc);
                if (w >= 0x100 && w <= 0x7FF)    // 000400..003777 переходы
                {
                    string[] br = { "BR", "BNE", "BEQ", "BGE", "BLT", "BGT", "BLE" };
                    int k = (w >> 8) - 1;
                    if (k >= 0 && k < br.Length) return Pad(br[k]) + Branch(w, pc);
                }
                if (w >= 0x800 && w <= 0x9FF)    // 004000..004777 JSR
                {
                    string dst = Operand(w & 0x3F, d, baseAddr, ref pc);
                    return Pad("JSR") + Reg((w >> 6) & 7) + "," + dst;
                }
                if (w >= 0xA00 && w <= 0xDFF)    // 005000..006777 однооперандные
                {
                    int idx = ((w >> 6) & 0x3F) - 0x28;
                    if (idx >= 0 && idx < Sng.Length)
                        return Pad(Sng[idx]) + Operand(w & 0x3F, d, baseAddr, ref pc);
                }
                return Word_(w);
            }

            if (top == 7)                        // 07xxxx: MUL/DIV/ASH/ASHC/XOR/SOB
            {
                int sub = (w >> 9) & 7;
                int r = (w >> 6) & 7;
                if (sub <= 3)
                {
                    string[] n = { "MUL", "DIV", "ASH", "ASHC" };
                    string src = Operand(w & 0x3F, d, baseAddr, ref pc);
                    return Pad(n[sub]) + src + "," + Reg(r);
                }
                if (sub == 4)
                {
                    string dst = Operand(w & 0x3F, d, baseAddr, ref pc);
                    return Pad("XOR") + Reg(r) + "," + dst;
                }
                if (sub == 7)
                {
                    int off = w & 0x3F;
                    return Pad("SOB") + Reg(r) + "," + Oct((pc - 2 * off) & 0xFFFF);
                }
                return Word_(w);                 // 075xxx/076xxx — сопроцессор
            }

            if (top == 8)                        // 10xxxx
            {
                if (w >= 0x8000 && w <= 0x83FF)  // 100000..101777 BPL/BMI/BHI/BLOS
                {
                    string[] br = { "BPL", "BMI", "BHI", "BLOS" };
                    return Pad(br[(w >> 8) & 3]) + Branch(w, pc);
                }
                if (w >= 0x8400 && w <= 0x87FF)  // 102000..103777 BVC/BVS/BCC/BCS
                {
                    string[] br = { "BVC", "BVS", "BCC", "BCS" };
                    return Pad(br[(w >> 8) & 3]) + Branch(w, pc);
                }
                if (w >= 0x8800 && w <= 0x88FF) return Pad("EMT") + Oct(w & 0xFF);
                if (w >= 0x8900 && w <= 0x89FF) return Pad("TRAP") + Oct(w & 0xFF);
                if (w >= 0x8A00 && w <= 0x8DFF)  // 105000..106777 байтовые
                {
                    int idx = ((w >> 6) & 0x3F) - 0x28;
                    if (idx >= 0 && idx < SngB.Length)
                        return Pad(SngB[idx]) + Operand(w & 0x3F, d, baseAddr, ref pc);
                }
                return Word_(w);
            }

            return Word_(w);                     // 17xxxx — плавающая арифметика
        }

        static string Branch(int w, int pcAfter)
        {
            int off = (sbyte)(w & 0xFF);
            return Oct((pcAfter + 2 * off) & 0xFFFF);
        }

        static string Cond(int w)
        {
            if (w == 0xA0) return "NOP";
            if (w == 0xAF) return "CCC";
            if (w == 0xBF) return "SCC";
            var sb = new StringBuilder();
            bool set = (w & 0x10) != 0;
            if ((w & 1) != 0) sb.Append(set ? "SEC " : "CLC ");
            if ((w & 2) != 0) sb.Append(set ? "SEV " : "CLV ");
            if ((w & 4) != 0) sb.Append(set ? "SEZ " : "CLZ ");
            if ((w & 8) != 0) sb.Append(set ? "SEN " : "CLN ");
            string s = sb.ToString().Trim();
            return s.Length > 0 ? s : Word_(w);
        }

        static string Word_(int w) { return Pad(".WORD") + Oct6(w); }
        static string Pad(string name) { return name.PadRight(7); }

        // ── весь листинг: адрес, слова, команда ──
        //    baseAddr — адрес, по которому лежит первый байт data.
        public static string Render(byte[] data, int startOffset, int baseAddr, int maxBytes)
        {
            var sb = new StringBuilder();
            int end = Math.Min(data.Length, startOffset + maxBytes);
            int pc = baseAddr + startOffset;
            int limit = baseAddr + end;

            while (pc + 1 < limit)
            {
                int addr = pc;
                string text = Decode(data, baseAddr, ref pc);

                // слова, которые заняла команда
                var words = new StringBuilder();
                for (int a = addr; a < pc && a < limit; a += 2)
                    words.Append(Oct6(Peek(data, baseAddr, a))).Append(' ');

                sb.Append(Oct6(addr)).Append("  ")
                  .Append(words.ToString().PadRight(22))
                  .Append(text);

                // текстом — то, что лежит в этих словах (видно строки)
                var asc = new StringBuilder();
                for (int a = addr; a < pc && a < limit; a += 2)
                {
                    int v = Peek(data, baseAddr, a);
                    asc.Append(Ch(v & 0xFF)).Append(Ch(v >> 8));
                }
                sb.Append("   ; ").Append(asc.ToString());
                sb.Append("\r\n");
            }
            if (end < data.Length)
                sb.Append("\r\n; ... показано ").Append(end - startOffset)
                  .Append(" из ").Append(data.Length - startOffset).Append(" байт\r\n");
            return sb.ToString();
        }

        static char Ch(int b) { return (b >= 32 && b < 127) ? (char)b : '.'; }

        // ── сведения о команде для обхода кода ──
        //    len — длина команды в байтах, target — адрес перехода
        //    (-1 если неизвестен), stop — после неё поток прерывается,
        //    call — это вызов подпрограммы.
        public static void Flow(byte[] d, int baseAddr, int addr,
                               out int len, out int target, out bool stop, out bool call)
        {
            int pc = addr;
            Decode(d, baseAddr, ref pc);
            len = pc - addr;
            target = -1; stop = false; call = false;

            int w = Peek(d, baseAddr, addr);
            int top = (w >> 12) & 15;

            // условные переходы: 000400..003777 и 100000..103777
            bool cond = (w >= 0x100 && w <= 0x7FF) || (w >= 0x8000 && w <= 0x87FF);
            if (cond)
            {
                int off = (sbyte)(w & 0xFF);
                target = (addr + 2 + 2 * off) & 0xFFFF;
                if (w >= 0x100 && w <= 0x1FF) stop = true;      // BR — безусловный
                return;
            }
            if (top == 7 && ((w >> 9) & 7) == 7)                // SOB
            {
                target = (addr + 2 - 2 * (w & 0x3F)) & 0xFFFF;
                return;
            }
            if (w >= 0x40 && w <= 0x7F)                         // JMP
            {
                target = OperandTarget(d, baseAddr, addr, w);
                stop = true;
                return;
            }
            if (w >= 0x800 && w <= 0x9FF)                       // JSR
            {
                target = OperandTarget(d, baseAddr, addr, w);
                call = true;
                return;
            }
            if (w == 0 || w == 2 || w == 6 || (w >= 0x80 && w <= 0x87))   // HALT, RTI, RTT, RTS
                stop = true;
        }

        // адрес, на который показывает операнд JMP/JSR: относительный
        // X(PC) или абсолютный @#адрес; иначе неизвестен
        static int OperandTarget(byte[] d, int baseAddr, int addr, int w)
        {
            int spec = w & 0x3F, mode = (spec >> 3) & 7, reg = spec & 7;
            if (reg != 7) return -1;
            int x = Peek(d, baseAddr, addr + 2);
            if (mode == 6) return (addr + 4 + x) & 0xFFFF;      // относительный
            if (mode == 3) return x & 0xFFFF;                   // @#абсолютный
            return -1;
        }

        // ── похоже ли на код: у RT-11 .SAV первые 512 байт — заголовок,
        //    код начинается с адреса 001000 ──
        public static bool IsSav(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string e = System.IO.Path.GetExtension(name).ToUpperInvariant();
            return e == ".SAV" || e == ".SYS" || e == ".REL";
        }
    }
}
