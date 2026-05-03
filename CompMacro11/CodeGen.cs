using System;
using System.Collections.Generic;
using System.Text;

namespace CompMacro11
{
    /// <summary>
    /// Кодогенератор Mini-C → Macro-11 (PDP-11)
    ///
    /// Соглашение о вызовах (caller-cleans-up):
    ///   Аргументы: справа налево, MOV arg, -(SP)
    ///   Вызов:     JSR PC, FUNC
    ///   Уборка:    ADD #N*2., SP   (вызывающий)
    ///   Возврат:   R0
    ///
    /// Стековый фрейм после пролога:
    ///   [SP]      → локальные переменные  (R5-N ... R5-2)
    ///   [R5]      → saved R5 caller'а     (R5+0)
    ///   [R5+2]    → адрес возврата         (R5+2)
    ///   [R5+4]    → arg[0]  (первый слева) (R5+4)
    ///   [R5+6]    → arg[1]                 (R5+6)
    ///
    /// PDP-11 MUL R0, R1  → R1 = R1 * R0   (R1 нечётный → 16-бит результат)
    /// PDP-11 DIV src, R0 → R0 = R0:R1/src, R1 = остаток  (R0 чётный!)
    /// </summary>
    public class CodeGen
    {
        private StringBuilder _out;
        private int _labelCnt;
        private Dictionary<string, FuncInfo> _funcs;
        private FuncInfo _cur;

        private Stack<string> _breakLbls = new Stack<string>();
        private Stack<string> _continueLbls = new Stack<string>();

        private class SymInfo
        {
            public MiniCType Type;
            public int Offset;        // байт от R5: >0=параметр, <0=локал
            public bool IsParam;
            public string StaticLabel; // если != null — статический массив в DATA
        }

        private class FuncInfo
        {
            public string Name, AsmLbl, EpilogLbl, RestartLbl;
            public MiniCType RetType;
            public List<ParamNode> Params;
            public Dictionary<string, SymInfo> Syms = new Dictionary<string, SymInfo>();
            public int LocalSize;
            public bool IsLeaf;  // нет локалов, нет вызовов → без фрейма R5
        }

        // ── Встроенные функции УКНЦ ────────────────────────────
        // Зарегистрированы заранее; GenCall распознаёт их по имени.
        //
        //  cls()                    — очистить экран (цвет 0)
        //  box(x,y,w,h,color)       — залить прямоугольник цветом
        //  sprite(x,y,w,h,ptr)      — вывести спрайт из памяти
        //  init()                   — инициализация таблицы строк + пауза
        //  pause()                  — пауза (ждать PPU)
        //
        // Аппаратные константы УКНЦ:
        //   Экран: 320×264 пикселей, 1 бит/пиксель, слово = 16 пикселей
        //   Строка = 80 слов (160 байт)
        //   Видеопорты: @#176640 — адрес, @#176642 — данные
        //   Начало видеопамяти: 100000 (окт) = 32768 (дес)
        //   DSPSTART — таблица адресов строк (440 слов, заполняется SETTBL)
        // ── ВСТРОЕННЫЕ ФУНКЦИИ ────────────────────────────────────
        // При добавлении новой функции обновить ВСЕ четыре места:
        // 1. _builtins (здесь)
        // 2. case "имя" в GenBuiltin()
        // 3. Подпрограмма в EmitRuntime()
        // 4. _usedLabels (зарезервированные метки)
        // 5. Подсветка в Form1.cs → Highlight()
        //
        // Текущий список:
        //   cls()              → RTCLS
        //   init()             → RTCLS
        //   pause()            → RTPAUS
        //   box(x,y,w,h,c)    → RTBOX   + RTCLR
        //   sprite(x,y,w,h,p) → RTSPR
        //   sprite(x,y,w,h,p) → RTSPR
        //   waitkey()          → RTWKEY
        //   getkey()           → RTGKEY
        //   random(n)          → RTRAND
        private static readonly HashSet<string> _builtins = new HashSet<string>
        {
            "cls", "init", "pause",
            "box", "sprite", "spriteOr",
            "waitkey", "getkey",
            "point", "line", "rect", "circle", "print", "printnum", "getTimer"
        };

        public CodeGen() { _out = new StringBuilder(); _funcs = new Dictionary<string, FuncInfo>(); }

        // Peephole оптимизация — отключена по умолчанию
        // Включить: codegen.EnablePeephole = true;
        public bool EnablePeephole = false;

        private string L() => $"L{_labelCnt++:D4}";

        // Macro-11: метка ≤ 6 символов [A-Z0-9]
        private string ToAsm(string name, int maxLen = 6)
        {
            var sb = new StringBuilder();
            foreach (char c in name.ToUpper())
                if (char.IsLetterOrDigit(c)) { sb.Append(c); if (sb.Length == maxLen) break; }
            if (sb.Length == 0) sb.Append('F');
            return sb.ToString();
        }

        private void E(string s) => _out.AppendLine(s);
        private void EL(string l) => _out.AppendLine(l + ":");
        private void EC(string c) { } // комментарии отключены — кириллица ломает Macro-11
        private void EI(string op, string a = "") =>
            _out.AppendLine("        " + (a == "" ? op : op + "\t" + a));

        private string FP(int off)
        {
            if (_cur != null && _cur.IsLeaf && off > 0)
                return $"{off}.(SP)";
            return off == 0 ? "(R5)" : off > 0 ? $"{off}.(R5)" : $"-{-off}.(R5)";
        }

        // Адрес массива в R0: статический через метку или стековый через R5
        private void LoadArrayAddr(SymInfo sym)
        {
            if (sym.StaticLabel != null)
                EI("MOV", $"#{sym.StaticLabel}, R0");
            else
            {
                EI("MOV", "R5, R0");
                if (sym.Offset != 0)
                    EI(sym.Offset > 0 ? "ADD" : "SUB", $"#{Math.Abs(sym.Offset)}., R0");
            }
        }

        // ── Таблица глобальных символов ──────────────────────────
        private Dictionary<string, SymInfo> _globals = new Dictionary<string, SymInfo>();

        // ── Генерация программы ───────────────────────────────────
        public string Generate(ProgramNode prog)
        {
            _out.Clear(); _labelCnt = 0; _funcs.Clear();
            _funcCnt = 0; _usedLabels.Clear();
            _r0Known = false; _labelPos.Clear();
            _globals.Clear(); _strings.Clear(); _strCnt = 0;
            _progFuncs = new System.Collections.Generic.Dictionary<string, FuncDeclNode>();
            foreach (var f in prog.Functions) _progFuncs[f.Name] = f; _inlineFuncs.Clear();
            // Зарезервировать рантайм-метки
            foreach (var lbl in new[] {
                "RTSTTBL","RTTBL1","RTPAUS","RTPS0","RTPRNT","RTPRN1","RTPRN2",
                "RTCLS","RTCSTB","RTCSC0","RTCSC1","RTCSC2","RTCSC3",
                "RTBOX","RTBX1","RTBX2",
                "RTSPR","RTSP1","RTSP2","RTSPB","RTSB1","RTSB2",
                "RTPNUM","RPNP","RPNM","RPNLP","RPNSB","RPNPT","RPNPR","RPNWT","RPNSK","RPNDN","RPNZ","RPNX","RPNTB","RPNPV",
                "RTGTIM",
                "RTWKEY","RTGKEY","RTGK1",
                "RTCLRT","RTCLR",
                "RTMCLR","RTMC1","RTMCPY","RTCP1",
                "RTPTBL","RPTL1","RPTL2","RPTL3","RTPPNT","RPPNR",
                "RTLINE","RTLCK1","RTLCK2","RTLCK3","RTLCK4","RTLNA","RTLNB","RTLNC","RTLND","RTLNE","RTLNF","RTLNG","RTLNL","RTLNX",
                "RTRECT",
                "RTCRC","RCRC1","RCRC1A","RCRC2","RCRC3","RCRC9",
                "XWRD","CM0","CM1","CM2","CM3","CTAB",
                "DSPST","KBDRT","KBDLT","KBDUP","KBDDN" })
                _usedLabels.Add(lbl);

            // Зарегистрировать глобальные массивы
            foreach (var g in prog.Globals)
            {
                string glbl = ToAsm(g.Name);
                _usedLabels.Add(glbl);
                _globals[g.Name] = new SymInfo
                {
                    Type = g.Type,
                    Offset = 0,
                    IsParam = false,
                    StaticLabel = glbl
                };
            }

            // Проход 1: сигнатуры функций
            foreach (var f in prog.Functions)
            {
                RegisterFunc(f);
                if (IsInlineCandidate(f)) _inlineFuncs.Add(f.Name);
            }

            E("        .TITLE\tMINIC");
            E("        .MCall\t.Exit, .TTYIN, .TTINR");
            E("        .PSECT\tCODE, RO, I");
            E("");
            EmitRuntime();
            E("");

            // Проход 2: код пользователя
            foreach (var f in prog.Functions) GenFunc(f);

            // Глобальные переменные и массивы → DATA секция
            if (prog.Globals.Count > 0)
            {
                E("        .PSECT\tDATA, RW, D");
                foreach (var g in prog.Globals)
                {
                    string glbl = ToAsm(g.Name);
                    if (!g.Type.IsArray)
                    {
                        // Скалярная глобальная переменная
                        int initVal = 0;
                        if (g.Init is IntLiteralExpr ile) initVal = ile.Value;
                        else if (g.Init is UnaryExpr ue && ue.Op == "-" &&
                                 ue.Operand is IntLiteralExpr ile2) initVal = -ile2.Value;
                        if (initVal == 0)
                            E($"{glbl}:   .BLKW\t1.");
                        else
                            E($"{glbl}:   .WORD\t{initVal}.");
                    }
                    else
                    {
                        // Массив
                        var flat = g.ArrayInit?.Flat;
                        int total = g.Type.TotalElements();
                        bool hasData = flat != null && flat.Exists(x => x != 0);
                        if (!hasData)
                        {
                            E($"{glbl}:   .BLKW\t{total}.");
                        }
                        else
                        {
                            E($"{glbl}:");
                            var sb2 = new System.Text.StringBuilder();
                            for (int i = 0; i < total; i++)
                            {
                                int val = (i < flat.Count) ? flat[i] : 0;
                                if (sb2.Length > 0) sb2.Append(",");
                                sb2.Append(val > 32767 || val < 0
                                    ? Convert.ToString((ushort)val, 8)
                                    : $"{val}.");
                                if ((i + 1) % 8 == 0 || i == total - 1)
                                {
                                    E($"        .WORD\t{sb2}");
                                    sb2.Clear();
                                }
                            }
                        }
                    }
                }
                E("        .PSECT\tCODE, RO, I");
            }

            // Строковые литералы → DATA секция
            if (_strings.Count > 0)
            {
                E("        .PSECT\tDATA, RW, D");
                foreach (var kv in _strings)
                {
                    E($"{kv.Value}:");
                    // Выводим каждый символ через .BYTE — безопасно для любых символов
                    // Группируем по 8 байт в строку для читаемости
                    var bytes = new System.Collections.Generic.List<int>();
                    foreach (char ch in kv.Key)
                    {
                        // Обработка escape-последовательностей
                        if (ch == '\r') bytes.Add(13);
                        else if (ch == '\n') bytes.Add(10);
                        else if (ch == '\t') bytes.Add(9);
                        else bytes.Add((int)ch & 0x7F);
                    }
                    bytes.Add(0); // null-terminator
                    for (int bi = 0; bi < bytes.Count; bi += 8)
                    {
                        int cnt = Math.Min(8, bytes.Count - bi);
                        var sb2 = new System.Text.StringBuilder();
                        for (int k = 0; k < cnt; k++)
                        {
                            if (k > 0) sb2.Append(",");
                            sb2.Append(bytes[bi + k].ToString());
                            sb2.Append(".");  // десятичный суффикс Macro-11
                        }
                        E("        .BYTE\t" + sb2.ToString());
                    }
                    E($"        .EVEN");
                }
                E("        .PSECT\tCODE, RO, I");
            }

            E("");
            E("        .END\tMAIN");
            string result = _out.ToString();
            return EnablePeephole ? PeepholeOptimizer.Apply(result) : result;
        }

        // ── Рантайм: встроенные подпрограммы УКНЦ ─────────────
        private void EmitRuntime()
        {
            E("; ============================================================");
            E("; Рантайм Mini-C для УКНЦ");
            E("; Экран 320x264, строка = 80 слов");
            E("; Видеопорты: @#176640 (адрес), @#176642 (данные)");
            E("; ============================================================");
            E("");

            // ── Макросы PUSH/POP ─────────────────────────────────
            E(".MACRO\tPUSH x");
            E("        MOV\tx,-(SP)");
            E(".ENDM");
            E(".MACRO\tPOP x");
            E("        MOV\t(SP)+,x");
            E(".ENDM");
            E("");

            // ── RTSTTBL: инициализация таблицы адресов строк ─────
            E("; RTSTTBL — инициализация таблицы адресов строк DSPST");
            E(";   DSPST[i] = 100000(oct) + i*80.  Вызывать один раз.");
            E("; Сохраняет R0,R1,R2.");
            E("RTSTTBL:");
            E("        MOV\tR0, -(SP)");
            E("        MOV\tR1, -(SP)");
            E("        MOV\tR2, -(SP)");
            E("        MOV\t#100000, R0");   // начальный адрес видеопамяти (окт)
            E("        MOV\t#264., R1");      // 264 строки экрана УКНЦ
            E("        MOV\t#DSPST,  R2");
            E("RTTBL1: MOV\tR0, (R2)+");
            E("        ADD\t#80., R0");       // шаг строки = 80 слов
            E("        SOB\tR1, RTTBL1");     // SOB: 264 итерации, тело 2 инстр = ok
            E("        MOV\t(SP)+, R2");
            E("        MOV\t(SP)+, R1");
            E("        MOV\t(SP)+, R0");
            E("        RTS\tPC");
            E("");

            // ── RTPAUS: пауза ─────────────────────────────────────
            E("; RTPAUS — пауза ~177777 итераций NOP.");
            E("; Сохраняет R5 (не портит frame pointer).");
            E("RTPAUS:");
            E("        MOV\tR5, -(SP)");
            E("        MOV\t#177777, R5");
            E("RTPS0:  NOP");
            E("        SOB\tR5, RTPS0");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            E("");

            // ── RTPRINT: вывод строки через терминальный порт ────
            E("; RTPRINT — вывод строки байт за байтом через порт 177566.");
            E(";   Точно как PRINT из M.MAC.");
            E(";   R1 = адрес строки, завершённой байтом 0.");
            E(";   Портит R0, R1.");
            E("RTPRNT:");
            E("        MOVB\t(R1)+, R0");
            E("        BEQ\tRTPRN2");
            E("RTPRN1: TSTB\t@#177564");
            E("        BPL\tRTPRN1");
            E("        MOV\tR0, @#177566");
            E("        BR\tRTPRNT");
            E("RTPRN2: RTS\tPC");
            E("");

            // ── RTCLS: настройка экрана + очистка ────────────────
            E("; RTCLS — настройка графического режима и очистка экрана.");
            E("; Вход: R0 = режим 0..3");
            E(";   0 = 40 кол, палитра 1");
            E(";   1 = 80 кол, палитра 1");
            E(";   2 = 40 кол, палитра 2");
            E(";   3 = 80 кол, палитра 2");
            E("RTCLS:");
            E("        MOV\tR0, -(SP)");
            E("        MOV\tR1, -(SP)");
            E("        BIS\t#010000, @#44");
            E("        MOV\t(SP)+, R1");              // восстановить R1
            E("        MOV\t(SP)+, R0");              // восстановить mode
            E("        MOV\tR1, -(SP)");              // теперь сохраняем R1 для дальнейшей работы
            E("        BIC\t#177774, R0");
            E("        ASL\tR0");
            E("        MOV\tRTCSTB(R0), R1");
            E("        JSR\tPC, RTPRNT");
            E("        JSR\tPC, RTPAUS");
            E("        JSR\tPC, RTSTTBL");
            E("        JSR\tPC, RTPTBL");
            E("        MOV\t(SP)+, R1");
            E("        RTS\tPC");
            E("");

            E("RTCSTB: .WORD\tRTCSC0,RTCSC1,RTCSC2,RTCSC3");
            E("");

            // SCR0: 40 колонок, палитра 1
            E("RTCSC0:");
            E("        .BYTE\t33,246,62");
            E("        .BYTE\t33,240,67");
            E("        .BYTE\t33,241,60");
            E("        .BYTE\t33,242,60");
            E("        .BYTE\t14,0");
            E("        .EVEN");
            E("");

            // SCR1: 80 колонок, палитра 1
            E("RTCSC1:");
            E("        .BYTE\t33,246,61");   // формат экрана 80x24
            E("        .BYTE\t33,240,63");   // цвет символа
            E("        .BYTE\t33,241,60");   // цвет знакоместа 0
            E("        .BYTE\t33,242,60");   // цвет фона 0
            E("        .BYTE\t14,0");        // clear screen
            E("        .EVEN");
            E("");

            // SCR2: 40 колонок, палитра 2
            E("RTCSC2:");
            E("        .BYTE\t33,246,62");
            E("        .BYTE\t33,240,67");
            E("        .BYTE\t33,241,61");
            E("        .BYTE\t33,242,61");
            E("        .BYTE\t14,0");
            E("        .EVEN");
            E("");

            // SCR3: 80 колонок, палитра 2
            E("RTCSC3:");
            E("        .BYTE\t33,246,61");
            E("        .BYTE\t33,240,67");
            E("        .BYTE\t33,241,61");
            E("        .BYTE\t33,242,61");
            E("        .BYTE\t14,0");
            E("        .EVEN");
            E("");

            // ── RTBOX: залить прямоугольник цветом ───────────────
            E("; RTBOX — залить прямоугольник (R0=x,R1=y,R2=w,R3=h,R4=color)");
            E("; Аргументы через стек, caller-cleans-up.");
            E("RTBOX:");
            E("        MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\t4.(R5),  R0");
            E("        MOV\t6.(R5),  R1");
            E("        MOV\t8.(R5),  R2");
            E("        MOV\t10.(R5), R3");
            E("        MOV\t12.(R5), R4");
            E("        ROL\tR1");
            E("        MOV\tDSPST(R1), R5");
            E("        ADD\tR0, R5");
            E("        MOV\t#80., R0");
            E("        SUB\tR2, R0");
            E("RTBX1:  MOV\tR2, R1");
            E("RTBX2:  MOV\tR5, @#176640");
            E("        MOV\tR4, @#176642");
            E("        INC\tR5");
            E("        DEC\tR1");
            E("        BNE\tRTBX2");
            E("        ADD\tR0, R5");
            E("        DEC\tR3");
            E("        BNE\tRTBX1");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            E("");

            // ── RTSPR: вывод спрайта ──────────────────────────────
            E("; RTSPR — вывести спрайт (R0=x,R1=y,R2=w,R3=h,R4=ptr)");
            E("; Аргументы через стек, caller-cleans-up.");
            E("; R5=адрес видеопамяти, R4=ptr спрайта");
            E("; R2=ширина(слов), R3=высота(строк)");
            E("; R0=шаг строки (80-w)");
            E("; Внутренний цикл: DEC+BNE вместо SOB — нет ограничения 126 байт");
            E("RTSPR:");
            E("        MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\t4.(R5),  R0");   // x
            E("        MOV\t6.(R5),  R1");   // y
            E("        MOV\t8.(R5),  R2");   // w
            E("        MOV\t10.(R5), R3");   // h
            E("        MOV\t12.(R5), R4");   // ptr
            E("        ROL\tR1");
            E("        MOV\tDSPST(R1), R5");
            E("        ADD\tR0, R5");
            E("        MOV\t#80., R0");
            E("        SUB\tR2, R0");        // R0 = 80-w
            E("RTSP1:  MOV\tR2, R1");
            E("RTSP2:  MOV\tR5, @#176640");
            E("        MOV\t(R4)+, @#176642");
            E("        INC\tR5");
            E("        DEC\tR1");
            E("        BNE\tRTSP2");
            E("        ADD\tR0, R5");
            E("        DEC\tR3");
            E("        BNE\tRTSP1");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            E("");

            // ── RTSPB: spriteOr — вывод спрайта через BIS ────────
            E("; RTSPB — spriteOr(x,y,w,h,ptr): BIS спрайта с VRAM");
            E("; 0-биты в спрайте = прозрачность, 1-биты = установить.");
            E("; Внутренний цикл: addr→read VRAM→BIS sprite→write");
            E("; R5=адрес строки, R0=шаг, R1=счётчик, R2=w, R3=h, R4=ptr");
            E("; Используем R2 как temp в цикле (w сохранён в стеке)");
            E("RTSPB:");
            E("        MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\tR2, -(SP)");      // сохранить R2 (понадобится как temp)
            E("        MOV\t4.(R5),  R0");    // x
            E("        MOV\t6.(R5),  R1");    // y
            E("        MOV\t8.(R5),  R2");    // w → R2
            E("        MOV\t10.(R5), R3");    // h
            E("        MOV\t12.(R5), R4");    // ptr
            E("        ROL\tR1");
            E("        MOV\tDSPST(R1), R5");  // R5 = начало строки y
            E("        ADD\tR0, R5");         // R5 = адрес первого слова
            E("        MOV\t#80., R0");
            E("        SUB\tR2, R0");         // R0 = 80-w (шаг строки)
            E("        MOV\tR2, -(SP)");      // push w (счётчик строки)
            E("RTSB1:  MOV\t(SP), R1");       // R1 = w (счётчик слов)
            E("RTSB2:  MOV\tR5, @#176640");  // выставить адрес VRAM
            E("        MOV\t@#176642, R2");   // R2 = текущее слово VRAM
            E("        BIS\t(R4)+, R2");      // R2 |= слово спрайта
            E("        MOV\tR5, @#176640");   // адрес снова
            E("        MOV\tR2, @#176642");   // записать результат
            E("        INC\tR5");             // следующее слово
            E("        DEC\tR1");
            E("        BNE\tRTSB2");
            E("        ADD\tR0, R5");         // следующая строка
            E("        DEC\tR3");
            E("        BNE\tRTSB1");
            E("        TST\t(SP)+");          // снять w со стека
            E("        MOV\t(SP)+, R2");      // восстановить R2
            E("        MOV\t(SP)+, R5");      // восстановить R5
            E("        RTS\tPC");
            E("");

            // ── RTPNUM: вывод числа на терминал ─────────────────
            E("; RTPNUM — printnum(n). Таблица степеней сразу после RTS.");
            E("RTPNUM:");
            E("        MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\tR1, -(SP)");
            E("        MOV\tR2, -(SP)");
            E("        MOV\tR3, -(SP)");
            E("        MOV\t4.(R5), R0");
            // Отрицательные
            E("        BPL\tRPNP");
            E("        NEG\tR0");
            E("        MOV\tR0, -(SP)");
            E("        MOV\t#45., R0");             // '-'
            E("RPNM:   TSTB\t@#177564");
            E("        BPL\tRPNM");
            E("        MOV\tR0, @#177566");
            E("        MOV\t(SP)+, R0");
            // Основной цикл с флагом ведущих нулей
            E("RPNP:   MOV\t#RPNTB, R1");           // адрес таблицы
            E("        CLR\tR3");                   // R3 = флаг: 0=ведущие нули
            E("RPNLP:  MOV\t(R1)+, R2");
            E("        BEQ\tRPNDN");
            E("        MOV\t#48., -(SP)");          // '0' на стек (счётчик цифры)
            E("RPNSB:  CMP\tR0, R2");
            E("        BLO\tRPNPT");
            E("        SUB\tR2, R0");
            E("        INC\t(SP)");                 // инкремент ASCII цифры
            E("        BR\tRPNSB");
            // Пропустить ведущий ноль если флаг не установлен
            E("RPNPT:  TST\tR3");                   // уже была ненулевая цифра?
            E("        BNE\tRPNPR");               // да — печатать
            E("        CMP\t(SP), #48.");           // текущая цифра == '0'?
            E("        BEQ\tRPNSK");               // да — пропустить
            E("RPNPR:  MOV\t(SP), R2");            // R2 = символ цифры
            E("        INC\tR3");                   // флаг ненулевой цифры
            E("RPNWT:  TSTB\t@#177564");
            E("        BPL\tRPNWT");
            E("        MOV\tR2, @#177566");
            E("RPNSK:  TST\t(SP)+");               // pop цифру
            E("        BR\tRPNLP");
            // Конец — если R3==0 значит число было 0, вывести '0'
            E("RPNDN:  TST\tR3");
            E("        BNE\tRPNX");
            E("        MOV\t#48., R2");
            E("RPNZ:   TSTB\t@#177564");
            E("        BPL\tRPNZ");
            E("        MOV\tR2, @#177566");
            E("RPNX:   MOV\t(SP)+, R3");
            E("        MOV\t(SP)+, R2");
            E("        MOV\t(SP)+, R1");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            E("");
            // Таблица степеней сразу после RTS — в той же CODE секции
            E("RPNTB:  .WORD\t10000.,1000.,100.,10.,1.,0");
            E("");

            // ── RTGTIM: читать счётчик времени LTC ───────────────
            E("; RTGTIM — getTimer(): читать @#177546 (LTC).");
            E("; Возвращает текущее значение в R0.");
            E("RTGTIM:");
            E("        MOV\t@#177546, R0");
            E("        RTS\tPC");
            E("");

            // ── RTWKEY: блокирующее чтение (ждёт клавишу) ───────
            E("; RTWKEY / waitkey() — крутится пока не придёт символ.");
            E("; BIC #177600 убирает бит чётности (parity) УКНЦ.");
            E("RTWKEY:");
            E("        .TTINR");
            E("        BCS\tRTWKEY");
            E("        BIC\t#177600, R0");    // убрать parity bit
            E("        RTS\tPC");
            E("");

            // ── RTGKEY: неблокирующее чтение (getkey) ────────────
            E("; RTGKEY / getkey() — однократная попытка читать.");
            E("RTGKEY:");
            E("        .TTINR");
            E("        BCS\tRTGK1");
            E("        BIC\t#177600, R0");    // убрать parity bit
            E("        RTS\tPC");
            E("RTGK1:  CLR\tR0");
            E("        RTS\tPC");
            E("");

            // ── Константы кодов клавиш УКНЦ ──────────────────────
            E("; Коды клавиш УКНЦ (восьмеричные в исходнике, десятичные для Mini-C):");
            E("; вправо = 103 окт = 67 дес");
            E("; влево  = 104 окт = 68 дес");
            E("; вверх  = 101 окт = 65 дес");
            E("; вниз   = 102 окт = 66 дес");
            E("; пробел =  40 окт = 32 дес");
            E("; Enter  =  15 окт = 13 дес");
            E("KBDRT:  .WORD\t67.");        // вправо
            E("KBDLT:  .WORD\t68.");        // влево
            E("KBDUP:  .WORD\t65.");        // вверх
            E("KBDDN:  .WORD\t66.");        // вниз
            E("");

            // ── Таблица адресов строк ─────────────────────────────
            // ── Таблица цветов + подпрограмма преобразования ──────
            E("; RTCLRT — таблица цветов по индексу 0-3:");
            E(";   0=чёрный(0) 1=синий(255) 2=зелёный(65280) 3=белый(65535)");
            E("RTCLRT: .WORD\t0");
            E("        .WORD\t255.");
            E("        .WORD\t65280.");
            E("        .WORD\t65535.");
            E("");
            E("; RTCLR — получить слово цвета по индексу.");
            E(";   Вход:  R0 = индекс (0-3)");
            E(";   Выход: R0 = слово цвета");
            E(";   Портит: R0 только");
            E("RTCLR:");
            E("        ASL\tR0");             // индекс * 2 = байтовое смещение
            E("        MOV\tRTCLRT(R0), R0"); // R0 = цвет из таблицы
            E("        RTS\tPC");
            E("");

            E("        .EVEN");
            E("DSPST:");
            E("        .BLKW\t264."); // 264 строки экрана УКНЦ
            E("");

            // ── RTMCLR: очистить блок памяти ─────────────────────
            E("; RTMCLR — заполнить нулями блок памяти.");
            E(";   Вход через стек: addr, count (слов)");
            E("; DEC+BNE вместо SOB — нет ограничения 126 байт");
            E("RTMCLR:");
            E("        MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\tR0, -(SP)");
            E("        MOV\tR1, -(SP)");
            E("        MOV\t4.(R5), R0");
            E("        MOV\t6.(R5), R1");
            E("RTMC1:  CLR\t(R0)+");
            E("        DEC\tR1");           // DEC вместо SOB
            E("        BNE\tRTMC1");
            E("        MOV\t(SP)+, R1");
            E("        MOV\t(SP)+, R0");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            E("");

            // ── RTMCPY: скопировать блок памяти ──────────────────
            E("; RTMCPY — скопировать блок слов src → dst.");
            E(";   4.(R5)=dst  6.(R5)=src  8.(R5)=count");
            E("; DEC+BNE вместо SOB — нет ограничения 126 байт");
            E("RTMCPY:");
            E("        MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\tR0, -(SP)");
            E("        MOV\tR1, -(SP)");
            E("        MOV\tR2, -(SP)");
            E("        MOV\t4.(R5), R0");   // dst
            E("        MOV\t6.(R5), R1");   // src
            E("        MOV\t8.(R5), R2");   // count
            E("RTCP1:  MOV\t(R1)+, (R0)+");
            E("        DEC\tR2");           // DEC вместо SOB — любая дальность
            E("        BNE\tRTCP1");
            E("        MOV\t(SP)+, R2");
            E("        MOV\t(SP)+, R1");
            E("        MOV\t(SP)+, R0");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            E("");

            // ── RTRAND: случайное число 0..N ──────────────────────
            E("; RTPTBL — заполнить XWRD, CM0-CM3 для point().");
            E("; Вызывается из RTCLS. Портит R0-R5.");
            E("RTPTBL:");
            E("        MOV\tR4, -(SP)");
            E("        MOV\tR5, -(SP)");
            // Проход 1: XWRD[i]=i/8, CM1[i]=1<<(i&7)
            E("        MOV\t#XWRD, R0");
            E("        MOV\t#CM1,  R1");
            E("        CLR\tR2");               // word_offset
            E("        MOV\t#1., R3");          // bit_mask
            E("        MOV\t#640., R4");        // 640 пикселей — макс ширина режима 1/3
            E("RPTL1:  MOV\tR2, (R0)+");        // XWRD[i]
            E("        MOV\tR3, (R1)+");        // CM1[i]
            E("        ASL\tR3");               // следующий бит
            E("        BIC\t#177400, R3");      // & 0xFF
            E("        BNE\tRPTL2");
            E("        MOV\t#1., R3");          // сброс → 1
            E("        INC\tR2");               // следующее слово
            E("RPTL2:  DEC\tR4");
            E("        BNE\tRPTL1");
            // Проход 2: CM2=SWAB(CM1), CM3=CM1|CM2
            E("        MOV\t#CM1, R0");
            E("        MOV\t#CM2, R1");
            E("        MOV\t#CM3, R2");
            E("        MOV\t#640., R4");
            E("RPTL3:  MOV\t(R0)+, R3");       // CM1[i]
            E("        MOV\tR3, R5");
            E("        SWAB\tR5");              // R5 = CM2[i] = CM1<<8
            E("        MOV\tR5, (R1)+");        // CM2[i]
            E("        BIS\tR5, R3");           // R3 = CM3[i] = CM1|CM2
            E("        MOV\tR3, (R2)+");        // CM3[i]
            E("        DEC\tR4");
            E("        BNE\tRPTL3");
            // Обнулить CM0 (черный цвет = ничего не ставить)
            E("        MOV\t#640., -(SP)");
            E("        MOV\t#CM0,  -(SP)");
            E("        JSR\tPC, RTMCLR");
            E("        ADD\t#4., SP");
            E("        MOV\t(SP)+, R5");
            E("        MOV\t(SP)+, R4");
            E("        RTS\tPC");
            E("");

            // ── RTPPNT: нарисовать пиксель ────────────────────────
            E("; RTPPNT — point(px, py, color)");
            E(";   4.(R5)=px, 6.(R5)=py, 8.(R5)=color");
            E("; Использует DSPST, XWRD, CM0-CM3, CTAB.");
            E("RTPPNT:");
            E("        MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\tR0, -(SP)");
            E("        MOV\tR1, -(SP)");
            E("        MOV\tR2, -(SP)");
            E("        MOV\tR3, -(SP)");
            E("        MOV\t4.(R5), R0");       // R0 = px
            E("        MOV\t6.(R5), R1");       // R1 = py
            E("        MOV\t8.(R5), R2");       // R2 = color
            // Проверка границ ДО ASL (px может быть отрицательным)
            E("        TST\tR0");               // px < 0?
            E("        BMI\tRPPNR");
            E("        TST\tR1");               // py < 0?
            E("        BMI\tRPPNR");
            E("        CMP\tR0, #640.");        // px >= 640?
            E("        BGE\tRPPNR");
            E("        CMP\tR1, #264.");        // py >= 264?
            E("        BGE\tRPPNR");
            E("        ASL\tR0");               // R0 = px*2 (байтовый индекс)
            E("        ASL\tR1");               // R1 = py*2
            E("        MOV\tDSPST(R1), R3");   // R3 = начало строки
            E("        ADD\tXWRD(R0), R3");    // R3 = адрес слова VRAM
            E("        MOV\tR3, @#176640");    // выставить адрес
            E("        MOV\t@#176642, R1");    // читать слово
            E("        BIC\tCM3(R0), R1");     // сбросить оба плана (CM3=maskW)
            E("        ASL\tR2");              // R2 = color*2
            E("        MOV\tCTAB(R2), R2");   // R2 = база CMx
            E("        ADD\tR0, R2");          // R2 = &CMx[px]
            E("        BIS\t(R2), R1");        // установить оба плана за один BIS
            E("        MOV\tR3, @#176640");    // адрес снова
            E("        MOV\tR1, @#176642");    // записать
            E("RPPNR:  MOV\t(SP)+, R3");      // единый эпилог (нормальный + out-of-bounds)
            E("        MOV\t(SP)+, R2");
            E("        MOV\t(SP)+, R1");
            E("        MOV\t(SP)+, R0");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            E("");

            // ── RTLINE: линия Брезенхэма ──────────────────────────
            E("; RTLINE — line(x0,y0,x1,y1,color)");
            E(";   4.(R5)=x0  6.(R5)=y0  8.(R5)=x1  10.(R5)=y1  12.(R5)=color");
            E("; Точно по bresenham_fast: e2=err<<1 (ASL), без умножения");
            E("; Cohen-Sutherland тривиальный reject: обе точки за одной границей → выход");
            E("RTLINE:");
            E("        MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\tR0, -(SP)");
            E("        MOV\tR1, -(SP)");
            E("        MOV\tR2, -(SP)");
            E("        MOV\tR3, -(SP)");
            // ── Тривиальный reject (Cohen-Sutherland) ────────────
            // Оба x < 0?
            E("        TST\t4.(R5)");             // x0 < 0?
            E("        BPL\tRTLCK1");
            E("        TST\t8.(R5)");             // x1 < 0?
            E("        BMI\tRTLNX");              // оба левее — выход
            E("RTLCK1:");
            // Оба x >= 320?
            E("        CMP\t4.(R5), #320.");      // x0 >= 320?
            E("        BLT\tRTLCK2");
            E("        CMP\t8.(R5), #320.");      // x1 >= 320?
            E("        BGE\tRTLNX");              // оба правее — выход
            E("RTLCK2:");
            // Оба y < 0?
            E("        TST\t6.(R5)");             // y0 < 0?
            E("        BPL\tRTLCK3");
            E("        TST\t10.(R5)");            // y1 < 0?
            E("        BMI\tRTLNX");              // оба выше — выход
            E("RTLCK3:");
            // Оба y >= 264?
            E("        CMP\t6.(R5), #264.");      // y0 >= 264?
            E("        BLT\tRTLCK4");
            E("        CMP\t10.(R5), #264.");     // y1 >= 264?
            E("        BGE\tRTLNX");              // оба ниже — выход
            E("RTLCK4:");
            // ── Брезенхэм ────────────────────────────────────────
            E("        SUB\t#8., SP");            // резервируем: dx dy sx sy
            // SP+0=sy SP+2=sx SP+4=dy SP+6=dx
            E("        MOV\t4.(R5), R0");         // x = x0
            E("        MOV\t6.(R5), R1");         // y = y0
            // dx = abs(x1-x0)
            E("        MOV\t8.(R5), R3");
            E("        SUB\tR0, R3");            // R3 = x1-x0
            E("        BGE\tRTLNA");
            E("        NEG\tR3");
            E("RTLNA:  MOV\tR3, 6.(SP)");       // dx
            // dy = abs(y1-y0)
            E("        MOV\t10.(R5), R3");
            E("        SUB\tR1, R3");            // R3 = y1-y0
            E("        BGE\tRTLNB");
            E("        NEG\tR3");
            E("RTLNB:  MOV\tR3, 4.(SP)");       // dy
            // sx = (x0<x1) ? 1 : -1
            E("        MOV\t#1., R3");
            E("        CMP\tR0, 8.(R5)");        // x0 < x1?
            E("        BLT\tRTLNC");
            E("        NEG\tR3");
            E("RTLNC:  MOV\tR3, 2.(SP)");       // sx
            // sy = (y0<y1) ? 1 : -1
            E("        MOV\t#1., R3");
            E("        CMP\tR1, 10.(R5)");       // y0 < y1?
            E("        BLT\tRTLND");
            E("        NEG\tR3");
            E("RTLND:  MOV\tR3, 0.(SP)");       // sy
            // err = dx - dy
            E("        MOV\t6.(SP), R2");        // R2 = dx
            E("        SUB\t4.(SP), R2");        // R2 = err = dx-dy
            // LOOP
            E("RTLNL:");
            // plot(x, y, color)
            E("        MOV\t12.(R5), R3");       // color
            E("        MOV\tR3, -(SP)");
            E("        MOV\tR1, -(SP)");
            E("        MOV\tR0, -(SP)");
            E("        JSR\tPC, RTPPNT");
            E("        ADD\t#6., SP");
            // if x==x1 && y==y1: break
            E("        CMP\tR0, 8.(R5)");
            E("        BNE\tRTLNE");
            E("        CMP\tR1, 10.(R5)");
            E("        BEQ\tRTLNX");
            E("RTLNE:");
            // e2 = err << 1  (R3 = e2)
            E("        MOV\tR2, R3");
            E("        ASL\tR3");                // R3 = e2
            // if e2 > -dy: err -= dy; x += sx
            E("        MOV\t4.(SP), R3");        // сохраним dy временно... нет
            // Пересчитаем: R3 = e2
            E("        MOV\tR2, R3");
            E("        ASL\tR3");                // R3 = e2
            // neg_dy в temp
            E("        MOV\t4.(SP), -(SP)");     // push dy
            E("        NEG\t(SP)");              // (SP) = -dy
            E("        CMP\tR3, (SP)+");         // e2 > -dy?  (pop)
            E("        BLE\tRTLNF");
            E("        SUB\t4.(SP), R2");        // err -= dy  (4.(SP) теперь dy после pop)
            E("        ADD\t2.(SP), R0");        // x += sx
            E("RTLNF:");
            // if e2 < dx: err += dx; y += sy
            E("        CMP\tR3, 6.(SP)");        // e2 < dx?
            E("        BGE\tRTLNG");
            E("        ADD\t6.(SP), R2");        // err += dx
            E("        ADD\t0.(SP), R1");        // y += sy
            E("RTLNG:");
            E("        BR\tRTLNL");
            E("RTLNX:");                         // эпилог
            E("        ADD\t#8., SP");            // убрать dx dy sx sy
            E("        MOV\t(SP)+, R3");
            E("        MOV\t(SP)+, R2");
            E("        MOV\t(SP)+, R1");
            E("        MOV\t(SP)+, R0");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            E("");

            // ── RTRECT: прямоугольник (контур) ───────────────────
            E("; RTRECT — rect(x,y,w,h,color)");
            E(";   4.(R5)=x  6.(R5)=y  8.(R5)=w  10.(R5)=h  12.(R5)=color");
            E("; Рисуем 4 стороны через RTLINE");
            E("; x2=x+w-1  y2=y+h-1");
            E("RTRECT:");
            E("        MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\tR0, -(SP)");
            E("        MOV\tR1, -(SP)");
            E("        MOV\tR2, -(SP)");
            E("        MOV\tR3, -(SP)");
            // Вычисляем x2 = x+w-1 → R2, y2 = y+h-1 → R3
            E("        MOV\t4.(R5), R2");           // R2 = x
            E("        ADD\t8.(R5), R2");           // R2 = x+w
            E("        DEC\tR2");                   // R2 = x2
            E("        MOV\t6.(R5), R3");           // R3 = y
            E("        ADD\t10.(R5), R3");          // R3 = y+h
            E("        DEC\tR3");                   // R3 = y2
            // ── Верхняя: (x,y)→(x2,y) ────────────────────────────
            E("        MOV\t12.(R5), -(SP)");       // color
            E("        MOV\t6.(R5), -(SP)");        // y
            E("        MOV\tR2, -(SP)");            // x2
            E("        MOV\t6.(R5), -(SP)");        // y
            E("        MOV\t4.(R5), -(SP)");        // x
            E("        JSR\tPC, RTLINE");
            E("        ADD\t#10., SP");
            // ── Нижняя: (x,y2)→(x2,y2) ──────────────────────────
            E("        MOV\t12.(R5), -(SP)");       // color
            E("        MOV\tR3, -(SP)");            // y2
            E("        MOV\tR2, -(SP)");            // x2
            E("        MOV\tR3, -(SP)");            // y2
            E("        MOV\t4.(R5), -(SP)");        // x
            E("        JSR\tPC, RTLINE");
            E("        ADD\t#10., SP");
            // ── Левая: (x,y)→(x,y2) ──────────────────────────────
            E("        MOV\t12.(R5), -(SP)");       // color
            E("        MOV\tR3, -(SP)");            // y2
            E("        MOV\t4.(R5), -(SP)");        // x
            E("        MOV\t6.(R5), -(SP)");        // y
            E("        MOV\t4.(R5), -(SP)");        // x
            E("        JSR\tPC, RTLINE");
            E("        ADD\t#10., SP");
            // ── Правая: (x2,y)→(x2,y2) ──────────────────────────
            E("        MOV\t12.(R5), -(SP)");       // color
            E("        MOV\tR3, -(SP)");            // y2
            E("        MOV\tR2, -(SP)");            // x2
            E("        MOV\t6.(R5), -(SP)");        // y
            E("        MOV\tR2, -(SP)");            // x2
            E("        JSR\tPC, RTLINE");
            E("        ADD\t#10., SP");
            // ── Эпилог ───────────────────────────────────────────
            E("        MOV\t(SP)+, R3");
            E("        MOV\t(SP)+, R2");
            E("        MOV\t(SP)+, R1");
            E("        MOV\t(SP)+, R0");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            E("");

            // ── RTCRC: окружность (алгоритм Брезенхема) ──────────
            E("; RTCRC — circle(cx,cy,r,color)");
            E(";   4.(R5)=cx  6.(R5)=cy  8.(R5)=r  10.(R5)=color");
            E("; Bresenham midpoint: только +/-/сравнение, без умножения.");
            E("; 8 симметричных точек за одну итерацию.");
            E("; RTPPNT проверяет границы — отрицательные координаты безопасны.");
            E("RTCRC:");
            E("        MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\tR0, -(SP)");   // x
            E("        MOV\tR1, -(SP)");   // y
            E("        MOV\tR2, -(SP)");   // d
            E("        MOV\tR3, -(SP)");   // temp
            E("        CLR\tR0");           // x = 0
            E("        MOV\t8.(R5), R1");  // y = r
            // d = 3 - 2*r
            E("        MOV\tR1, R2");
            E("        ASL\tR2");           // 2*r
            E("        NEG\tR2");           // -2*r
            E("        ADD\t#3., R2");      // d = 3 - 2*r
            E("RCRC1:  CMP\tR0, R1");      // x > y ?
            E("        BLE\tRCRC1A");
            E("        JMP\tRCRC9");
            E("RCRC1A:");
            // Хелпер-макрос: push color/cy±dy/cx±dx, JSR RTPPNT, ADD #6,SP
            // (cx+x, cy+y)
            E("        MOV\t10.(R5), -(SP)");
            E("        MOV\t6.(R5), -(SP)"); E("        ADD\tR1, (SP)");
            E("        MOV\t4.(R5), -(SP)"); E("        ADD\tR0, (SP)");
            E("        JSR\tPC, RTPPNT"); E("        ADD\t#6., SP");
            // (cx-x, cy+y)
            E("        MOV\t10.(R5), -(SP)");
            E("        MOV\t6.(R5), -(SP)"); E("        ADD\tR1, (SP)");
            E("        MOV\t4.(R5), -(SP)"); E("        SUB\tR0, (SP)");
            E("        JSR\tPC, RTPPNT"); E("        ADD\t#6., SP");
            // (cx+x, cy-y)
            E("        MOV\t10.(R5), -(SP)");
            E("        MOV\t6.(R5), -(SP)"); E("        SUB\tR1, (SP)");
            E("        MOV\t4.(R5), -(SP)"); E("        ADD\tR0, (SP)");
            E("        JSR\tPC, RTPPNT"); E("        ADD\t#6., SP");
            // (cx-x, cy-y)
            E("        MOV\t10.(R5), -(SP)");
            E("        MOV\t6.(R5), -(SP)"); E("        SUB\tR1, (SP)");
            E("        MOV\t4.(R5), -(SP)"); E("        SUB\tR0, (SP)");
            E("        JSR\tPC, RTPPNT"); E("        ADD\t#6., SP");
            // (cx+y, cy+x) — x и y меняются местами
            E("        MOV\t10.(R5), -(SP)");
            E("        MOV\t6.(R5), -(SP)"); E("        ADD\tR0, (SP)");
            E("        MOV\t4.(R5), -(SP)"); E("        ADD\tR1, (SP)");
            E("        JSR\tPC, RTPPNT"); E("        ADD\t#6., SP");
            // (cx-y, cy+x)
            E("        MOV\t10.(R5), -(SP)");
            E("        MOV\t6.(R5), -(SP)"); E("        ADD\tR0, (SP)");
            E("        MOV\t4.(R5), -(SP)"); E("        SUB\tR1, (SP)");
            E("        JSR\tPC, RTPPNT"); E("        ADD\t#6., SP");
            // (cx+y, cy-x)
            E("        MOV\t10.(R5), -(SP)");
            E("        MOV\t6.(R5), -(SP)"); E("        SUB\tR0, (SP)");
            E("        MOV\t4.(R5), -(SP)"); E("        ADD\tR1, (SP)");
            E("        JSR\tPC, RTPPNT"); E("        ADD\t#6., SP");
            // (cx-y, cy-x)
            E("        MOV\t10.(R5), -(SP)");
            E("        MOV\t6.(R5), -(SP)"); E("        SUB\tR0, (SP)");
            E("        MOV\t4.(R5), -(SP)"); E("        SUB\tR1, (SP)");
            E("        JSR\tPC, RTPPNT"); E("        ADD\t#6., SP");
            // Обновление d (Bresenham)
            E("        TST\tR2");             // d < 0?
            E("        BGE\tRCRC2");
            // d += 4*x + 6
            E("        MOV\tR0, R3");
            E("        ASL\tR3");
            E("        ASL\tR3");             // R3 = 4*x
            E("        ADD\t#6., R3");
            E("        ADD\tR3, R2");         // d += 4*x + 6
            E("        BR\tRCRC3");
            E("RCRC2:");                      // d += 4*(x-y) + 10; y--
            E("        MOV\tR0, R3");
            E("        SUB\tR1, R3");         // R3 = x - y
            E("        ASL\tR3");
            E("        ASL\tR3");             // R3 = 4*(x-y)
            E("        ADD\t#10., R3");
            E("        ADD\tR3, R2");         // d += 4*(x-y) + 10
            E("        DEC\tR1");             // y--
            E("RCRC3:  INC\tR0");             // x++
            E("        JMP\tRCRC1");          // JMP: тело цикла > 127 байт
            E("RCRC9:  MOV\t(SP)+, R3");
            E("        MOV\t(SP)+, R2");
            E("        MOV\t(SP)+, R1");
            E("        MOV\t(SP)+, R0");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            E("");
            // Режимы 0,2: 320 пикселей (40 слов/строку)
            // Режимы 1,3: 640 пикселей (80 слов/строку)
            // Таблицы на 640 — покрывают оба режима
            E("        .PSECT\tDATA, RW, D");
            E("CTAB:   .WORD\tCM0,CM1,CM2,CM3");
            E("XWRD:   .BLKW\t640.");
            E("CM0:    .BLKW\t640.");
            E("CM1:    .BLKW\t640.");
            E("CM2:    .BLKW\t640.");
            E("CM3:    .BLKW\t640.");
            E("        .PSECT\tCODE, RO, I");
            E("");
        }

        private int _funcCnt = 0;

        private void RegisterFunc(FuncDeclNode f)
        {
            // Метка функции: до 6 символов из имени
            string asmLbl = ToAsm(f.Name);

            // Эпилог: EP + номер функции — гарантированно уникален
            // EP префикс вместо E — E0000 Macro-11 парсит как float!
            string ep = "EP" + (_funcCnt++).ToString("D3");

            // Если метка функции совпадает с уже существующей — добавить цифру
            int suffix = 0;
            string uniqueLbl = asmLbl;
            while (_usedLabels.Contains(uniqueLbl))
                uniqueLbl = ToAsm(f.Name, 5) + (suffix++).ToString();
            _usedLabels.Add(uniqueLbl);

            _funcs[f.Name] = new FuncInfo
            {
                Name = f.Name,
                AsmLbl = uniqueLbl,
                EpilogLbl = ep,
                RetType = f.ReturnType,
                Params = f.Params
            };
        }

        private readonly HashSet<string> _usedLabels = new HashSet<string>();

        // ── Функция ───────────────────────────────────────────────
        private void GenFunc(FuncDeclNode f)
        {
            _cur = _funcs[f.Name];
            _cur.Syms.Clear(); _cur.LocalSize = 0;
            _cur.IsLeaf = IsLeafCandidate(f);

            EC("--------------------------------------");
            EC($"Функция: {f.Name}{(_cur.IsLeaf ? " [leaf]" : "")}");

            // ── Leaf-функция: нет фрейма ──────────────────────────
            // Условие: нет локальных переменных + нет вызовов функций
            // Параметры адресуются через SP: SP+2=arg0, SP+4=arg1...
            // (без фрейма SP→ret_addr, R5 не трогаем)
            if (_cur.IsLeaf)
            {
                int po = 2;  // ← без фрейма: arg0 at SP+2 (не SP+4 как с фреймом)
                foreach (var p in f.Params)
                {
                    _cur.Syms[p.Name] = new SymInfo { Type = p.Type, Offset = po, IsParam = true };
                    po += 2;
                }

                EL(_cur.AsmLbl);
                _breakLbls.Clear(); _continueLbls.Clear();
                _cur.RestartLbl = L();
                ELTracked(_cur.RestartLbl);

                GenBlock(f.Body);

                // Эпилог leaf — метка не нужна (return вставляет RTS напрямую)
                if (f.Name == "main") E("        .Exit");
                else EI("RTS", "PC");
                E("");
                return;
            }

            // ── Обычная функция: полный фрейм ─────────────────────
            int po2 = 4;  // с фреймом: arg0 at R5+4
            foreach (var p in f.Params)
            {
                _cur.Syms[p.Name] = new SymInfo { Type = p.Type, Offset = po2, IsParam = true };
                po2 += 2;
            }

            EL(_cur.AsmLbl);
            EI("MOV", "R5, -(SP)");
            EI("MOV", "SP, R5");

            const string PH = "@@LOCSIZE@@";
            int phPos = _out.Length;
            E(PH);

            _breakLbls.Clear(); _continueLbls.Clear();
            _cur.RestartLbl = L();
            ELTracked(_cur.RestartLbl);

            GenBlock(f.Body);

            // Финальный эпилог (если функция не заканчивается return)
            EI("MOV", "R5, SP");
            EI("MOV", "(SP)+, R5");
            if (f.Name == "main") E("        .Exit");
            else EI("RTS", "PC");
            E("");

            // Вывести статические данные этой функции в секцию данных
            if (_staticData.Length > 0)
            {
                E("        .PSECT\tDATA, RW, D");
                _out.Append(_staticData);
                E("        .PSECT\tCODE, RO, I");
                E("");
                _staticData.Clear();
            }

            string sub = _cur.LocalSize > 0
                ? $"        SUB\t#{_cur.LocalSize}., SP\r\n"
                : $"        NOP\t; no locals\r\n";

            string full = _out.ToString();
            int idx = full.IndexOf(PH, phPos);
            if (idx >= 0)
            {
                _out.Remove(idx, PH.Length + 2);
                _out.Insert(idx, sub);
            }
        }

        // ── Листовая функция: нет локалов, нет вызовов ────────────
        // Сканируем AST до генерации — единственный безопасный способ
        private static bool IsLeafCandidate(FuncDeclNode f)
        {
            // void-функция без параметров тоже может быть leaf
            return !StmtHasLocals(f.Body) && !StmtHasCalls(f.Body);
        }

        private static bool StmtHasLocals(StmtNode s)
        {
            if (s == null) return false;
            if (s is VarDeclStmtNode) return true;
            if (s is BlockStmtNode b) { foreach (var st in b.Stmts) if (StmtHasLocals(st)) return true; return false; }
            if (s is IfStmtNode i) return StmtHasLocals(i.Then) || StmtHasLocals(i.Else);
            if (s is WhileStmtNode w) return StmtHasLocals(w.Body);
            if (s is ForStmtNode f2) return StmtHasLocals(f2.Init) || StmtHasLocals(f2.Body);
            return false;
        }

        private static bool StmtHasCalls(StmtNode s)
        {
            if (s == null) return false;
            if (s is BlockStmtNode b2) { foreach (var st in b2.Stmts) if (StmtHasCalls(st)) return true; return false; }
            if (s is ExprStmtNode e) return ExprHasCalls(e.Expr);
            if (s is ReturnStmtNode r) return ExprHasCalls(r.Value);
            if (s is IfStmtNode i) return ExprHasCalls(i.Cond) || StmtHasCalls(i.Then) || StmtHasCalls(i.Else);
            if (s is WhileStmtNode w) return ExprHasCalls(w.Cond) || StmtHasCalls(w.Body);
            if (s is ForStmtNode f2) return ExprHasCalls(f2.Cond) || ExprHasCalls(f2.Post) ||
                                              StmtHasCalls(f2.Init) || StmtHasCalls(f2.Body);
            if (s is VarDeclStmtNode v) return ExprHasCalls(v.Init);
            return false;
        }

        private static bool ExprHasCalls(ExprNode e)
        {
            if (e == null) return false;
            if (e is CallExpr) return true;
            if (e is BinaryExpr b) return ExprHasCalls(b.Left) || ExprHasCalls(b.Right);
            if (e is UnaryExpr u) return ExprHasCalls(u.Operand);
            if (e is AssignExpr a) return ExprHasCalls(a.Target) || ExprHasCalls(a.Value);
            if (e is ArrayIndexExpr ai) return ExprHasCalls(ai.Array) || ExprHasCalls(ai.Index);
            return false;
        }

        // ── Символы ───────────────────────────────────────────────
        private SymInfo Decl(string name, MiniCType t)
        {
            int bytes = t.IsArray ? t.TotalElements() * 2 : 2;
            _cur.LocalSize += bytes;
            var info = new SymInfo { Type = t, Offset = -_cur.LocalSize, IsParam = false };
            _cur.Syms[name] = info;
            return info;
        }

        private SymInfo Sym(string name, int line)
        {
            if (_cur != null && _cur.Syms.TryGetValue(name, out var s)) return s;
            if (_globals.TryGetValue(name, out var g)) return g;
            throw new Exception($"Строка {line}: '{name}' не объявлена");
        }

        // ── Операторы ─────────────────────────────────────────────
        private void GenBlock(BlockStmtNode b)
        {
            foreach (var s in b.Stmts)
            {
                GenStmt(s);
                // Всё после return/break/continue недостижимо — не генерировать
                if (s is ReturnStmtNode || s is BreakStmtNode || s is ContinueStmtNode)
                    break;
            }
        }

        private void GenStmt(StmtNode st)
        {
            switch (st)
            {
                case BlockStmtNode b: GenBlock(b); break;
                case VarDeclStmtNode v: GenDecl(v); break;
                case ExprStmtNode e: GenExpr(e.Expr); break;
                case IfStmtNode i: GenIf(i); break;
                case WhileStmtNode w: GenWhile(w); break;
                case ForStmtNode f: GenFor(f); break;
                case ReturnStmtNode r: GenRet(r); break;
                case BreakStmtNode _: GenBrk(st); break;
                case ContinueStmtNode _: GenCnt(st); break;
                case SwitchStmtNode s: GenSwitch(s); break;
                case DoWhileStmtNode d: GenDoWhile(d); break;
                default: throw new Exception($"Строка {st.Line}: неизвестный оператор");
            }
        }

        // Порог: массивы больше этого числа слов размещаются статически в DATA
        private const int StaticArrayThreshold = 64;

        // Буфер для статических данных — сбрасывается после каждой функции
        private System.Text.StringBuilder _staticData = new System.Text.StringBuilder();

        // Строковые литералы для print() → .PSECT DATA в конце
        private System.Collections.Generic.Dictionary<string, string> _strings
            = new System.Collections.Generic.Dictionary<string, string>();
        private int _strCnt = 0;
        private string InternString(string s)
        {
            if (!_strings.TryGetValue(s, out var lbl))
            {
                lbl = "SL" + (_strCnt++).ToString("D3");
                _strings[s] = lbl;
                _usedLabels.Add(lbl);
            }
            return lbl;
        }

        private void GenDecl(VarDeclStmtNode v)
        {
            var flat = v.ArrayInit?.Flat;

            if (v.Type.IsArray)
            {
                int total = v.Type.TotalElements();
                bool hasNonZero = flat != null && flat.Exists(x => x != 0);

                // Большой массив → статический в .PSECT DATA (не на стеке)
                if (total > StaticArrayThreshold)
                {
                    string statLbl = "S" + (_labelCnt++).ToString("D4");

                    // Зарегистрировать символ со статической меткой
                    var info2 = new SymInfo
                    {
                        Type = v.Type,
                        Offset = 0,
                        IsParam = false,
                        StaticLabel = statLbl
                    };
                    _cur.Syms[v.Name] = info2;

                    // Сгенерировать данные в буфер (сольём в DATA после функции)
                    var sb3 = new System.Text.StringBuilder();
                    sb3.AppendLine($"{statLbl}:");
                    if (!hasNonZero)
                    {
                        sb3.AppendLine($"        .BLKW\t{total}.");
                    }
                    else
                    {
                        var sb4 = new System.Text.StringBuilder();
                        for (int i = 0; i < total; i++)
                        {
                            int val = (i < flat.Count) ? flat[i] : 0;
                            if (sb4.Length > 0) sb4.Append(",");
                            // Значения > 32767 в восьмеричном (Macro-11 принимает любые)
                            // Значения ≤ 32767 в десятичном с точкой
                            if (val > 32767 || val < 0)
                                sb4.Append(Convert.ToString((ushort)val, 8));
                            else
                                sb4.Append($"{val}.");
                            if ((i + 1) % 8 == 0 || i == total - 1)
                            {
                                sb3.AppendLine($"        .WORD\t{sb4}");
                                sb4.Clear();
                            }
                        }
                    }
                    _staticData.Append(sb3);

                    // Если нулевой массив — обнулить через RTMCLR при инициализации
                    // (.BLKW не гарантирует нули на УКНЦ)
                    if (!hasNonZero)
                    {
                        EC($"обнулить статический {v.Name}[{total}]");
                        EI("MOV", $"#{total}., -(SP)");
                        EI("MOV", $"#{statLbl}, -(SP)");
                        EI("JSR", "PC, RTMCLR");
                        EI("ADD", "#4., SP");
                    }

                    // Статический массив не требует выделения стека
                    return;
                }

                // Малый массив — на стеке как раньше
                var info = Decl(v.Name, v.Type);

                if (!hasNonZero)
                {
                    EC($"обнулить {v.Name}[{total}]");
                    EI("MOV", "R5, R0");
                    if (info.Offset != 0)
                        EI(info.Offset > 0 ? "ADD" : "SUB", $"#{Math.Abs(info.Offset)}., R0");
                    EI("MOV", $"#{total}., -(SP)");
                    EI("MOV", "R0, -(SP)");
                    EI("JSR", "PC, RTMCLR");
                    EI("ADD", "#4., SP");
                }
                else
                {
                    string dataLbl = "D" + (_labelCnt++).ToString("D4");
                    string skipLbl = "D" + (_labelCnt++).ToString("D4");
                    EI("JMP", skipLbl);
                    EL(dataLbl);
                    var sb2 = new System.Text.StringBuilder();
                    for (int i = 0; i < total; i++)
                    {
                        int val = (flat != null && i < flat.Count) ? flat[i] : 0;
                        if (sb2.Length > 0) sb2.Append(",");
                        if (val > 32767 || val < 0)
                            sb2.Append(Convert.ToString((ushort)val, 8));
                        else
                            sb2.Append($"{val}.");
                        if ((i + 1) % 8 == 0 || i == total - 1)
                        {
                            E($"        .WORD\t{sb2}");
                            sb2.Clear();
                        }
                    }
                    EL(skipLbl);
                    EC($"скопировать инициализатор {v.Name}[{total}]");
                    EI("MOV", $"#{total}., -(SP)");
                    EI("MOV", $"#{dataLbl}, -(SP)");
                    EI("MOV", "R5, R0");
                    if (info.Offset != 0)
                        EI(info.Offset > 0 ? "ADD" : "SUB", $"#{Math.Abs(info.Offset)}., R0");
                    EI("MOV", "R0, -(SP)");
                    EI("JSR", "PC, RTMCPY");
                    EI("ADD", "#6., SP");
                }
            }
            else if (v.Init != null) { var info = Decl(v.Name, v.Type); GenExpr(v.Init); EI("MOV", $"R0, {FP(info.Offset)}"); }
            else { var info = Decl(v.Name, v.Type); EI("CLR", FP(info.Offset)); }
        }

        // ── Длинные переходы (нет ограничения ±127 байт) ────────
        // BR  → JMP
        // BEQ → BNE skip / JMP target
        // BNE → BEQ skip / JMP target
        private void JMP(string label) => EI("JMP", label);
        private void JMPEQ(string label)
        {
            string skip = L();
            EI("BNE", skip);
            EmitBrOrJmp(label);
            ELTracked(skip);
        }
        private void JMPNE(string label)
        {
            string skip = L();
            EI("BEQ", skip);
            EmitBrOrJmp(label);
            ELTracked(skip);
        }

        private void GenIf(IfStmtNode i)
        {
            // Устранение мёртвого кода: константное условие
            int? constCond = FoldConst(i.Cond);
            if (constCond.HasValue)
            {
                if (constCond.Value != 0) GenStmt(i.Then);
                else if (i.Else != null) GenStmt(i.Else);
                return;
            }

            string el = L(), en = L();
            if (!TryDirectCond(i.Cond, el))
            { GenExpr(i.Cond); EI("TST", "R0"); JMPEQ(el); }
            GenStmt(i.Then);
            if (i.Else != null) { EmitBrOrJmp(en); ELTracked(el); GenStmt(i.Else); ELTracked(en); }
            else ELTracked(el);
        }

        private void GenWhile(WhileStmtNode w)
        {
            // while(0) — мёртвый цикл
            int? constCond = FoldConst(w.Cond);
            if (constCond.HasValue && constCond.Value == 0) return;

            string top = L(), end = L();
            _breakLbls.Push(end); _continueLbls.Push(top);

            // while(1) — бесконечный цикл, условие не нужно
            if (constCond.HasValue && constCond.Value != 0)
            {
                ELTracked(top);
                GenStmt(w.Body); EmitBrOrJmp(top); ELTracked(end);
                _breakLbls.Pop(); _continueLbls.Pop();
                return;
            }

            // ── SOB оптимизация для while(v > 0) с декрементом в теле ──
            // Паттерн: while(v > 0) { ... ; v--; }
            // Генерируем: проверить v > 0 первый раз, затем тело + DEC + BNE
            if (TryGenWhileSOB(w, top, end))
            {
                _breakLbls.Pop(); _continueLbls.Pop();
                return;
            }

            ELTracked(top);
            if (!TryDirectCond(w.Cond, end))
            { GenExpr(w.Cond); EI("TST", "R0"); JMPEQ(end); }
            GenStmt(w.Body); EmitBrOrJmp(top); ELTracked(end);
            _breakLbls.Pop(); _continueLbls.Pop();
        }

        private bool TryGenWhileSOB(WhileStmtNode w, string top, string end)
        {
            if (!(w.Cond is BinaryExpr cond) || cond.Op != ">") return false;
            if (!(cond.Left is IdentExpr condId)) return false;
            if (!(FoldConst(cond.Right) is int rhs) || rhs != 0) return false;

            if (!(w.Body is BlockStmtNode blk)) return false;
            if (blk.Stmts.Count == 0) return false;
            var last = blk.Stmts[blk.Stmts.Count - 1];
            ExprNode postExpr = null;
            if (last is ExprStmtNode es) postExpr = es.Expr;
            if (postExpr == null || !IsDecrementOf(postExpr, condId.Name)) return false;

            if (!_cur.Syms.TryGetValue(condId.Name, out var sym) || sym.Type.IsArray) return false;
            string fp = FP(sym.Offset);
            string varName = condId.Name;

            // Проверить читается ли переменная в теле (кроме последнего декремента)
            bool pureCounter = true;
            for (int si = 0; si < blk.Stmts.Count - 1; si++)
                if (StmtReferencesVar(blk.Stmts[si], varName)) { pureCounter = false; break; }

            if (pureCounter)
            {
                EC($"SOB R2 — while счётчик {varName} не читается в теле");
                EI("TST", fp);
                JMPEQ(end);
                // Загрузить счётчик в R2
                EI("MOV", $"{fp}, R2");
                ELTracked(top);
                for (int si = 0; si < blk.Stmts.Count - 1; si++)
                    GenStmt(blk.Stmts[si]);
                // Настоящий SOB: ≤ 63 слова = ≤ 126 байт ≈ 200 символов текста
                if (_labelPos.TryGetValue(top, out int p2) && _out.Length - p2 < 200)
                    EI("SOB", $"R2, {top}");
                else
                {
                    EI("DEC", "R2");
                    EmitBneOrJmp(top);
                }
                EI("MOV", $"R2, {fp}");
            }
            else
            {
                EC($"DEC+BNE — while счётчик {varName} читается в теле");
                EI("TST", fp);
                JMPEQ(end);
                ELTracked(top);
                for (int si = 0; si < blk.Stmts.Count - 1; si++)
                    GenStmt(blk.Stmts[si]);
                EI("DEC", fp);
                EmitBneOrJmp(top);
            }
            ELTracked(end);
            return true;
        }

        // ── do { body } while (cond) ─────────────────────────────
        private void GenDoWhile(DoWhileStmtNode d)
        {
            string top = L(), end = L();
            _breakLbls.Push(end); _continueLbls.Push(top);
            ELTracked(top);
            GenStmt(d.Body);
            int? cc = FoldConst(d.Cond);
            if (cc.HasValue && cc.Value == 0)
            {
                // do {} while(0) — выполнить один раз
            }
            else if (cc.HasValue && cc.Value != 0)
            {
                EmitBrOrJmp(top);    // do {} while(1) — бесконечно
            }
            else
            {
                // Обычное условие
                if (!TryDirectCond(d.Cond, end))
                { GenExpr(d.Cond); EI("TST", "R0"); JMPEQ(end); }
                EmitBrOrJmp(top);
            }
            ELTracked(end);
            _breakLbls.Pop(); _continueLbls.Pop();
        }

        // ── switch (expr) { case v: ... default: ... } ───────────
        private void GenSwitch(SwitchStmtNode sw)
        {
            string end = L();
            _breakLbls.Push(end);

            var caseLabels = new System.Collections.Generic.List<string>();
            foreach (var c2 in sw.Cases) caseLabels.Add(L());

            GenExpr(sw.Expr);

            // BEQ label для каждого case (прямой, без лишних skip-меток)
            for (int i = 0; i < sw.Cases.Count; i++)
            {
                int? val = sw.Cases[i].Value;
                if (val.HasValue)
                {
                    EI("CMP", $"R0, #{val.Value}.");
                    EI("BEQ", caseLabels[i]);
                }
            }
            // default
            for (int i = 0; i < sw.Cases.Count; i++)
            {
                if (!sw.Cases[i].Value.HasValue)
                { EmitBrOrJmp(caseLabels[i]); break; }
            }
            bool hasDefault = sw.Cases.Exists(c2 => !c2.Value.HasValue);
            if (!hasDefault) EmitBrOrJmp(end);

            // Тела
            for (int i = 0; i < sw.Cases.Count; i++)
            {
                ELTracked(caseLabels[i]);
                foreach (var stmt in sw.Cases[i].Body)
                    GenStmt(stmt);
            }

            ELTracked(end);
            _breakLbls.Pop();
        }

        private void GenFor(ForStmtNode f)
        {
            string top = L(), cont = L(), end = L();
            if (f.Init != null) GenStmt(f.Init);

            // ── SOB оптимизация: for(v=N; v>0; v--) ─────────────────
            if (TryDetectSOBPattern(f, out string sobFP, out string sobVar, out int sobN))
            {
                _breakLbls.Push(end); _continueLbls.Push(cont);

                // Переменная не читается в теле → настоящий SOB R2
                // SOB: 6-битный отрицательный офсет = ≤ 63 слова назад = ≤ 126 байт
                // Порог текста: ~200 символов (консервативно)
                bool pureCounter = !StmtReferencesVar(f.Body, sobVar);
                if (pureCounter)
                {
                    EC($"SOB R2 — счётчик {sobVar}={sobN} не читается в теле");
                    EI("MOV", $"#{sobN}., R2");    // загрузить счётчик в R2
                    ELTracked(top);
                    GenStmt(f.Body);
                    ELTracked(cont);
                    // Настоящий SOB: только если тело ≤ 63 слова назад
                    if (_labelPos.TryGetValue(top, out int sobPos2) && _out.Length - sobPos2 < 200)
                        EI("SOB", $"R2, {top}");
                    else
                    {
                        // Тело слишком большое для SOB → DEC R2 + BNE/JMP
                        EI("DEC", "R2");
                        EmitBneOrJmp(top);
                    }
                    // Записать финальное значение обратно в память
                    EI("MOV", $"R2, {sobFP}");
                }
                else
                {
                    // Переменная читается в теле → DEC память + BNE
                    EC($"DEC+BNE — счётчик {sobVar} читается в теле");
                    ELTracked(top);
                    GenStmt(f.Body);
                    ELTracked(cont);
                    EI("DEC", sobFP);
                    // BNE: 8-битный офсет = ≤ 127 слов = ≤ 254 байт, порог ~700 символов
                    EmitBneOrJmp(top);
                }

                ELTracked(end);
                _breakLbls.Pop(); _continueLbls.Pop();
                return;
            }

            // ── Обычный путь ──────────────────────────────────────────
            _breakLbls.Push(end); _continueLbls.Push(cont);
            ELTracked(top);
            if (f.Cond != null)
            {
                int? constCond = FoldConst(f.Cond);
                if (constCond.HasValue && constCond.Value == 0)
                {
                    ELTracked(cont); ELTracked(end);
                    _breakLbls.Pop(); _continueLbls.Pop();
                    return;
                }
                if (!constCond.HasValue)
                {
                    if (!TryDirectCond(f.Cond, end))
                    { GenExpr(f.Cond); EI("TST", "R0"); JMPEQ(end); }
                }
            }
            GenStmt(f.Body);
            ELTracked(cont);
            if (f.Post != null) GenExpr(f.Post);
            EmitBrOrJmp(top); ELTracked(end);
            _breakLbls.Pop(); _continueLbls.Pop();
        }

        // Вспомогательный метод: BNE если близко, иначе BEQ+JMP
        private void EmitBneOrJmp(string top)
        {
            if (_labelPos.TryGetValue(top, out int p) && _out.Length - p < 700)
                EI("BNE", top);
            else
            {
                string skip = L();
                EI("BEQ", skip);
                EI("JMP", top);
                ELTracked(skip);
            }
        }

        // Обнаружить паттерн for(v=N; v>0; v--) где N — положительная константа
        private bool TryDetectSOBPattern(ForStmtNode f, out string fp, out string varName, out int N)
        {
            fp = null; varName = null; N = 0;
            if (f.Init == null || f.Cond == null || f.Post == null) return false;

            if (!(f.Init is ExprStmtNode initS)) return false;
            if (!(initS.Expr is AssignExpr initA) || initA.Op != "=") return false;
            if (!(initA.Target is IdentExpr initId)) return false;
            int? Nv = FoldConst(initA.Value);
            if (!Nv.HasValue || Nv.Value <= 0) return false;

            if (!(f.Cond is BinaryExpr cond) || cond.Op != ">") return false;
            if (!(cond.Left is IdentExpr condId) || condId.Name != initId.Name) return false;
            int? rhs = FoldConst(cond.Right);
            if (!rhs.HasValue || rhs.Value != 0) return false;

            if (!IsDecrementOf(f.Post, initId.Name)) return false;

            if (!_cur.Syms.TryGetValue(initId.Name, out var sym) || sym.Type.IsArray) return false;

            fp = FP(sym.Offset);
            varName = initId.Name;
            N = Nv.Value;
            return true;
        }

        // Проверить что выражение уменьшает переменную varName на 1
        private static bool IsDecrementOf(ExprNode post, string varName)
        {
            if (post is UnaryExpr u && u.Op == "--" &&
                u.Operand is IdentExpr ui && ui.Name == varName)
                return true;
            if (post is AssignExpr a && a.Target is IdentExpr ai && ai.Name == varName)
            {
                if (a.Op == "-=") { int? v = FoldConst(a.Value); return v == 1; }
                if (a.Op == "=" && a.Value is BinaryExpr b && b.Op == "-" &&
                    b.Left is IdentExpr bl && bl.Name == varName)
                { int? v = FoldConst(b.Right); return v == 1; }
            }
            return false;
        }

        // Проверить что переменная varName встречается как чтение в stmt
        private static bool StmtReferencesVar(StmtNode s, string varName)
        {
            if (s == null) return false;
            if (s is BlockStmtNode b) { foreach (var st in b.Stmts) if (StmtReferencesVar(st, varName)) return true; return false; }
            if (s is ExprStmtNode e) return ExprReferencesVar(e.Expr, varName);
            if (s is ReturnStmtNode r) return ExprReferencesVar(r.Value, varName);
            if (s is IfStmtNode i) return ExprReferencesVar(i.Cond, varName) || StmtReferencesVar(i.Then, varName) || StmtReferencesVar(i.Else, varName);
            if (s is WhileStmtNode w) return ExprReferencesVar(w.Cond, varName) || StmtReferencesVar(w.Body, varName);
            if (s is ForStmtNode f) return StmtReferencesVar(f.Init, varName) || ExprReferencesVar(f.Cond, varName) || ExprReferencesVar(f.Post, varName) || StmtReferencesVar(f.Body, varName);
            if (s is VarDeclStmtNode v) return ExprReferencesVar(v.Init, varName);
            return false;
        }

        private static bool ExprReferencesVar(ExprNode e, string varName)
        {
            if (e == null) return false;
            if (e is IdentExpr id) return id.Name == varName;
            if (e is BinaryExpr b) return ExprReferencesVar(b.Left, varName) || ExprReferencesVar(b.Right, varName);
            if (e is UnaryExpr u) return ExprReferencesVar(u.Operand, varName);
            if (e is AssignExpr a) return ExprReferencesVar(a.Target, varName) || ExprReferencesVar(a.Value, varName);
            if (e is ArrayIndexExpr ai) return ExprReferencesVar(ai.Array, varName) || ExprReferencesVar(ai.Index, varName);
            if (e is CallExpr c) { foreach (var arg in c.Args) if (ExprReferencesVar(arg, varName)) return true; return false; }
            return false;
        }

        private void GenRet(ReturnStmtNode r)
        {
            // Tail call оптимизация: return f(args) → переиспользовать фрейм
            if (r.Value is CallExpr tc && TryTailCall(tc))
                return;
            if (r.Value != null) GenExpr(r.Value);

            // Вместо JMP к эпилогу — сразу восстановить фрейм и выйти
            // Это избегает проблем с undefined label при forward-jump
            if (_cur.IsLeaf)
            {
                if (_cur.Name == "main") E("        .Exit");
                else EI("RTS", "PC");
            }
            else
            {
                EI("MOV", "R5, SP");
                EI("MOV", "(SP)+, R5");
                if (_cur.Name == "main") E("        .Exit");
                else EI("RTS", "PC");
            }
        }

        // ── Tail Call оптимизация ─────────────────────────────────
        private bool TryTailCall(CallExpr c)
        {
            // Только пользовательские функции, не встроенные, не inline
            if (_builtins.Contains(c.FuncName)) return false;
            if (_inlineFuncs.Contains(c.FuncName)) return false;
            if (!_funcs.TryGetValue(c.FuncName, out var fi)) return false;
            if (c.Args.Count != fi.Params.Count) return false;

            // ── Хвостовая рекурсия: return f(...) где f — текущая функция
            if (fi.Name == _cur.Name)
                return TrySelfTailCall(c, fi);

            // ── Хвостовой вызов другой функции
            return TryGeneralTailCall(c, fi);
        }

        // return f(a, b) — та же функция: пересчитать параметры и JMP restart
        private bool TrySelfTailCall(CallExpr c, FuncInfo fi)
        {
            EC($"tail self-call: {c.FuncName}({ArgStr(c)}) → restart");

            // Вычислить все новые аргументы и сохранить на стеке
            // (порядок: arg[0] первым → окажется верхним при попе)
            int argCount = c.Args.Count;
            for (int i = 0; i < argCount; i++)
            {
                GenExpr(c.Args[i]);
                EI("MOV", "R0, -(SP)");
            }

            // Записать новые значения в слоты параметров (снизу вверх)
            // arg[0] был пушнут первым → лежит глубже → попнуть последним
            for (int i = argCount - 1; i >= 0; i--)
            {
                var sym = _cur.Syms[fi.Params[i].Name];
                EI("MOV", $"(SP)+, R0");
                EI("MOV", $"R0, {FP(sym.Offset)}");
            }

            // Перейти к началу тела функции (минуя пролог)
            EmitBrOrJmp(_cur.RestartLbl);
            return true;
        }

        // return g(a, b) — другая функция
        // Алгоритм (корректный для PDP-11 caller-cleans-up):
        //   1. Вычислить arg[0]..arg[n-1] и сохранить на стек
        //   2. Восстановить фрейм: MOV R5,SP / MOV (SP)+,R5
        //   3. Снять ret_addr в R0: MOV (SP)+, R0
        //   4. Запушить g's args в обратном порядке (arg[n-1]..arg[0])
        //      Берём их из области памяти ВЫШЕ нового SP (бывшие локалы)
        //   5. MOV R0, -(SP)  — вернуть ret_addr
        //   6. JMP g (g сам делает пролог, найдёт args на месте)
        //
        // Ограничение: реализован только для n==0 и простых args.
        // Для сложных args используется обычный вызов.
        private bool TryGeneralTailCall(CallExpr c, FuncInfo fi)
        {
            int n = c.Args.Count;

            // 0 аргументов — тривиально: просто восстановить фрейм и JMP
            if (n == 0)
            {
                EC($"tail call → {c.FuncName}()");
                EI("MOV", "R5, SP");
                EI("MOV", "(SP)+, R5");
                // SP теперь на ret_addr; JMP g сделает пролог поверх него
                EI("MOV", "(SP)+, R0");  // снять ret_addr
                EI("MOV", "R0, -(SP)");  // вернуть обратно (g найдёт его)
                EI("JMP", fi.AsmLbl);
                return true;
            }

            // N аргументов, все простые (константы или переменные — без вызовов)
            // Тогда: вычислить после эпилога безопасно
            bool allSimple = true;
            foreach (var a in c.Args)
                if (!CanLoadDirect(a)) { allSimple = false; break; }

            if (allSimple)
            {
                EC($"tail call → {c.FuncName}({ArgStr(c)})");
                EI("MOV", "R5, SP");
                EI("MOV", "(SP)+, R5");
                EI("MOV", "(SP)+, R0");          // R0 = ret_to_caller

                // Пушим args g: arg[n-1] первым (глубже), arg[0] последним
                for (int i = n - 1; i >= 0; i--)
                {
                    LoadDirect(c.Args[i], "R1");
                    EI("MOV", "R1, -(SP)");
                }

                EI("MOV", "R0, -(SP)");           // вернуть ret_addr под args
                EI("JMP", fi.AsmLbl);
                return true;
            }

            // Сложные args — не оптимизируем
            return false;
        }

        private void GenBrk(StmtNode s)
        {
            if (_breakLbls.Count == 0) throw new Exception($"Строка {s.Line}: break вне цикла");
            EmitBrOrJmp(_breakLbls.Peek());
        }

        private void GenCnt(StmtNode s)
        {
            if (_continueLbls.Count == 0) throw new Exception($"Строка {s.Line}: continue вне цикла");
            EmitBrOrJmp(_continueLbls.Peek());
        }

        // ── Выражения (результат → R0) ────────────────────────────
        private void GenExpr(ExprNode expr)
        {
            switch (expr)
            {
                case IntLiteralExpr lit:
                    EmitLiteralOpt(lit.Value);
                    break;
                case BoolLiteralExpr blit:
                    EmitLiteralOpt(blit.Value ? 1 : 0);
                    break;
                case StringLiteralExpr slit:
                    // Адрес строки → R0 (для передачи как указатель)
                    EI("MOV", $"#{InternString(slit.Value)}, R0");
                    break;
                case IdentExpr id:
                    GenIdent(id); InvalidateR0();
                    break;
                case ArrayIndexExpr ai:
                    GenArrLoad(ai); InvalidateR0();
                    break;
                case UnaryExpr u:
                    GenUnary(u);
                    break;
                case BinaryExpr b:
                    int? fv = FoldConst(b);
                    if (fv != null) { EmitLiteralOpt(fv.Value); break; }
                    GenBinary(b);
                    break;
                case AssignExpr a:
                    GenAssign(a);
                    break;
                case CallExpr c:
                    GenCall(c); InvalidateR0();
                    break;
                default: throw new Exception($"Строка {expr.Line}: неизвестное выражение");
            }
        }

        private void GenIdent(IdentExpr id)
        {
            var sym = Sym(id.Name, id.Line);
            if (sym.Type.IsArray)
            {
                if (sym.IsParam) EI("MOV", $"{FP(sym.Offset)}, R0");
                else LoadArrayAddr(sym);
            }
            else if (sym.StaticLabel != null)
                EI("MOV", $"{sym.StaticLabel}, R0");    // глобальная переменная
            else
                EI("MOV", $"{FP(sym.Offset)}, R0");
        }

        private void GenAddr(ExprNode lval)
        {
            if (lval is IdentExpr id)
            {
                var sym = Sym(id.Name, id.Line);
                if (sym.Type.IsArray) throw new Exception($"Строка {id.Line}: присваивание массиву");
                if (sym.StaticLabel != null)
                {
                    EI("MOV", $"#{sym.StaticLabel}, R1"); // адрес глобальной переменной
                }
                else
                {
                    EI("MOV", "R5, R1");
                    if (sym.Offset != 0)
                        EI(sym.Offset > 0 ? "ADD" : "SUB", $"#{Math.Abs(sym.Offset)}., R1");
                }
            }
            else if (lval is ArrayIndexExpr ai) GenArrAddr(ai);
            else throw new Exception($"Строка {lval.Line}: недопустимый lvalue");
        }

        private void GenArrLoad(ArrayIndexExpr ai)
        {
            GenArrAddr(ai);
            EI("MOV", "(R1), R0");
        }

        // Вычислить адрес элемента массива → R1
        // Использует алгоритм Хорнера. R2=накопленный индекс, R3=dim.
        private void GenArrAddr(ArrayIndexExpr ai)
        {
            // Развернуть цепочку a[i][j][k]
            var chain = new List<ArrayIndexExpr>();
            ExprNode node = ai;
            while (node is ArrayIndexExpr aie) { chain.Add(aie); node = aie.Array; }
            chain.Reverse();

            var rootId = node as IdentExpr
                ?? throw new Exception($"Строка {ai.Line}: сложная индексация не поддерживается");
            var sym = Sym(rootId.Name, rootId.Line);
            var dims = sym.Type.Dims;

            // R2 = index[0]
            GenExpr(chain[0].Index);
            EI("MOV", "R0, R2");

            // Хорнер: R2 = R2 * dims[k] + index[k]
            for (int k = 1; k < chain.Count; k++)
            {
                int dim = k < dims.Count ? dims[k] : 1;
                if (dim > 1)
                {
                    int dimShift = Log2Exact(dim);
                    if (dimShift > 0)
                        for (int s = 0; s < dimShift; s++) EI("ASL", "R2");
                    else
                    {
                        EI("MOV", $"#{dim}., R0");
                        EI("MOV", "R2, R1");
                        EI("MUL", "R0, R1");
                        EI("MOV", "R1, R2");
                    }
                }
                GenExpr(chain[k].Index);  // R0 = index[k]
                EI("ADD", "R0, R2");
            }

            // Базовый адрес → R1
            if (sym.IsParam && sym.Type.IsArray)
                EI("MOV", $"{FP(sym.Offset)}, R1");
            else
            {
                EI("MOV", "R5, R1");
                if (sym.Offset != 0)
                    EI(sym.Offset > 0 ? "ADD" : "SUB", $"#{Math.Abs(sym.Offset)}., R1");
            }

            // R1 += R2 * 2
            EI("ASL", "R2");
            EI("ADD", "R2, R1");
        }

        // ── Унарные ──────────────────────────────────────────────
        private void GenUnary(UnaryExpr u)
        {
            switch (u.Op)
            {
                case "-": GenExpr(u.Operand); EI("NEG", "R0"); break;
                case "~": GenExpr(u.Operand); EI("COM", "R0"); break; // побитовое NOT
                case "!":
                    {
                        GenExpr(u.Operand); EI("TST", "R0");
                        string t = L(), e = L();
                        JMPNE(t); EI("MOV", "#1., R0"); JMP(e);
                        EL(t); EI("CLR", "R0"); EL(e);
                        break;
                    }
                case "++": GenAddr(u.Operand); EI("MOV", "(R1), R0"); EI("INC", "(R1)"); break;
                case "--": GenAddr(u.Operand); EI("MOV", "(R1), R0"); EI("DEC", "(R1)"); break;
                default: throw new Exception($"Строка {u.Line}: неизвестный оператор '{u.Op}'");
            }
        }

        // ── Бинарные ─────────────────────────────────────────────
        private static bool IsCommutative(string op)
            => op == "+" || op == "*" || op == "==" || op == "!=";

        // Проверить что выражение можно загрузить в регистр напрямую (без стека)
        private bool CanLoadDirect(ExprNode expr)
        {
            if (expr is IntLiteralExpr) return true;
            if (expr is IdentExpr id &&
                _cur.Syms.TryGetValue(id.Name, out var sym) && !sym.Type.IsArray) return true;
            return false;
        }

        // Загрузить простое выражение прямо в регистр
        private void LoadDirect(ExprNode expr, string reg)
        {
            if (expr is IntLiteralExpr lit)
            {
                if (lit.Value == 0) EI("CLR", reg);
                else EI("MOV", $"#{lit.Value}., {reg}");
            }
            else if (expr is IdentExpr id &&
                     _cur.Syms.TryGetValue(id.Name, out var sym))
            {
                EI("MOV", $"{FP(sym.Offset)}, {reg}");
            }
        }

        private void GenBinary(BinaryExpr b)
        {
            if (b.Op == "&&")
            {
                string fL = L(), eL = L();
                GenExpr(b.Left); EI("TST", "R0"); JMPEQ(fL);
                GenExpr(b.Right); EI("TST", "R0"); JMPEQ(fL);
                EI("MOV", "#1., R0"); JMP(eL);
                EL(fL); EI("CLR", "R0"); EL(eL);
                return;
            }
            if (b.Op == "||")
            {
                string tL = L(), eL = L();
                GenExpr(b.Left); EI("TST", "R0"); JMPNE(tL);
                GenExpr(b.Right); EI("TST", "R0"); JMPEQ(eL);
                EL(tL); EI("MOV", "#1., R0"); EL(eL);
                return;
            }

            // Оптимизация: свёртка силы операций (без стека)
            if (TryStrengthReduction(b)) return;

            // ── Оптимизация: избегать push/pop ───────────────────
            // Соглашение: R1 = left, R0 = right

            // Случай A: right — простое выражение
            //   GenExpr(left)→R0, MOV R0→R1, LoadDirect(right)→R0
            //   Итого: 1 MOV вместо push+pop
            if (CanLoadDirect(b.Right))
            {
                GenExpr(b.Left);
                EI("MOV", "R0, R1");
                LoadDirect(b.Right, "R0");
            }
            // Случай B: коммутативная + left простое
            //   GenExpr(right)→R0, LoadDirect(left)→R1
            //   Итого: 1 MOV вместо push+pop
            else if (IsCommutative(b.Op) && CanLoadDirect(b.Left))
            {
                GenExpr(b.Right);
                LoadDirect(b.Left, "R1");
            }
            else
            {
                // Общий случай: push/pop
                GenExpr(b.Left);
                EI("MOV", "R0, -(SP)");
                GenExpr(b.Right);
                EI("MOV", "(SP)+, R1");
            }

            switch (b.Op)
            {
                case "+": EI("ADD", "R0, R1"); EI("MOV", "R1, R0"); break;
                case "-": EI("SUB", "R0, R1"); EI("MOV", "R1, R0"); break;

                case "*":
                    {
                        var rLit = b.Right as IntLiteralExpr;
                        var lLit = b.Left as IntLiteralExpr;
                        if (rLit != null) { EmitMulConst(rLit.Value, "R1"); EI("MOV", "R1, R0"); }
                        else if (lLit != null) { EmitMulConst(lLit.Value, "R0"); }
                        else { EI("MUL", "R0, R1"); EI("MOV", "R1, R0"); }
                        break;
                    }

                case "/":
                    {
                        var rLit = b.Right as IntLiteralExpr;
                        int rk = (rLit != null) ? Log2Exact(rLit.Value) : -2;
                        if (rk > 0) EmitDivConst(rLit.Value);
                        else { EI("MOV", "R0, -(SP)"); EI("MOV", "R1, R0"); EI("CLR", "R1"); EI("DIV", "(SP)+, R0"); }
                        break;
                    }

                case "%":
                    {
                        var rLit = b.Right as IntLiteralExpr;
                        int rk = (rLit != null) ? Log2Exact(rLit.Value) : -2;
                        if (rk > 0) EmitModConst(rLit.Value);
                        else { EI("MOV", "R0, -(SP)"); EI("MOV", "R1, R0"); EI("CLR", "R1"); EI("DIV", "(SP)+, R0"); EI("MOV", "R1, R0"); }
                        break;
                    }

                case "==": Cmp("BEQ"); break;
                case "!=": Cmp("BNE"); break;
                case "<": Cmp("BLT"); break;
                case ">": Cmp("BGT"); break;
                case "<=": Cmp("BLE"); break;
                case ">=": Cmp("BGE"); break;

                // Битовые операции
                case "&":
                    {
                        var rLit = b.Right as IntLiteralExpr;
                        var lLit = b.Left as IntLiteralExpr;
                        int? maskVal = rLit != null ? rLit.Value : lLit?.Value;
                        if (maskVal.HasValue)
                        {
                            int inv = (~maskVal.Value) & 0xFFFF;
                            EI("BIC", $"#{inv}., R1");
                            EI("MOV", "R1, R0");
                        }
                        else
                        {
                            EI("COM", "R0");          // R0 = ~right
                            EI("BIC", "R0, R1");      // R1 &= ~(~right) = R1 & right ✓
                            EI("MOV", "R1, R0");
                        }
                        break;
                    }

                case "|":
                    EI("BIS", "R0, R1");          // R1 |= R0
                    EI("MOV", "R1, R0");
                    break;

                case "^":
                    EI("XOR", "R0, R1");          // R1 ^= R0 (XOR: src=reg, dst=любой)
                    EI("MOV", "R1, R0");
                    break;

                case "<<":
                    {
                        // Сдвиг влево: если правый операнд константа — цепочка ASL
                        // иначе цикл по счётчику в R0
                        var rLit = b.Right as IntLiteralExpr;
                        if (rLit != null && rLit.Value >= 0 && rLit.Value <= 15)
                        {
                            for (int k = 0; k < rLit.Value; k++) EI("ASL", "R1");
                            EI("MOV", "R1, R0");
                        }
                        else
                        {
                            // R0=count, R1=value → цикл
                            string lbl = L(), end = L();
                            EI("TST", "R0");
                            JMPEQ(end);
                            ELTracked(lbl);
                            EI("ASL", "R1");
                            EI("DEC", "R0");
                            JMPNE(lbl);
                            ELTracked(end);
                            EI("MOV", "R1, R0");
                        }
                        break;
                    }

                case ">>":
                    {
                        // Сдвиг вправо: ASR (арифметический, сохраняет знак)
                        var rLit = b.Right as IntLiteralExpr;
                        if (rLit != null && rLit.Value >= 0 && rLit.Value <= 15)
                        {
                            for (int k = 0; k < rLit.Value; k++) EI("ASR", "R1");
                            EI("MOV", "R1, R0");
                        }
                        else
                        {
                            string lbl = L(), end = L();
                            EI("TST", "R0");
                            JMPEQ(end);
                            ELTracked(lbl);
                            EI("ASR", "R1");
                            EI("DEC", "R0");
                            JMPNE(lbl);
                            ELTracked(end);
                            EI("MOV", "R1, R0");
                        }
                        break;
                    }

                default: throw new Exception($"Строка {b.Line}: неизвестный оператор '{b.Op}'");
            }
        }

        // ══════════════════════════════════════════════════════════
        // ОПТИМИЗАЦИИ
        // ══════════════════════════════════════════════════════════

        // ── 1. Свёртка константных выражений ─────────────────────
        private static int? FoldConst(ExprNode expr)
        {
            if (expr is IntLiteralExpr lit) return lit.Value;
            if (expr is BoolLiteralExpr blit) return blit.Value ? 1 : 0;
            if (expr is UnaryExpr u)
            {
                int? v = FoldConst(u.Operand);
                if (v == null) return null;
                if (u.Op == "-") return -v.Value;
                if (u.Op == "!") return v.Value == 0 ? 1 : 0;
                if (u.Op == "~") return ~v.Value & 0xFFFF;
            }
            if (expr is BinaryExpr b)
            {
                int? lv = FoldConst(b.Left), rv = FoldConst(b.Right);
                if (lv == null || rv == null) return null;
                switch (b.Op)
                {
                    case "+": return lv + rv;
                    case "-": return lv - rv;
                    case "*": return lv * rv;
                    case "/": return rv != 0 ? lv / rv : (int?)null;
                    case "%": return rv != 0 ? lv % rv : (int?)null;
                    case "==": return lv == rv ? 1 : 0;
                    case "!=": return lv != rv ? 1 : 0;
                    case "<": return lv < rv ? 1 : 0;
                    case ">": return lv > rv ? 1 : 0;
                    case "<=": return lv <= rv ? 1 : 0;
                    case ">=": return lv >= rv ? 1 : 0;
                    case "&": return lv & rv;
                    case "|": return lv | rv;
                    case "^": return lv ^ rv;
                    case "<<": return rv >= 0 ? (lv << rv.Value) & 0xFFFF : (int?)null;
                    case ">>": return rv >= 0 ? lv >> rv.Value : (int?)null;
                }
            }
            return null;
        }

        // ── 3. Выдать константу в R0 ──────────────────────────────
        private void EmitConst(int v)
        {
            if (v == 0) EI("CLR", "R0");
            else EI("MOV", $"#{v}., R0");
        }

        // ── 4. INC/DEC для ±1 ─────────────────────────────────────
        // Проверяет паттерны: x += 1, x -= 1, x = x+1, x = x-1
        // ── 4. Прямые операции с памятью ─────────────────────────
        // Избегаем: MOV mem,R0 / ADD #N,R0 / MOV R0,mem
        // Генерируем: ADD #N., mem  (PDP-11 поддерживает ADD/SUB с любым операндом)
        //
        // Покрытые паттерны:
        //   x += 1        → INC mem
        //   x -= 1        → DEC mem
        //   x += N        → ADD #N., mem
        //   x -= N        → SUB #N., mem
        //   x = x + N     → ADD #N., mem  (коммутативно)
        //   x = x - N     → SUB #N., mem
        //   x = 0         → CLR mem
        //   x = N (const) → MOV #N., mem  (без загрузки через R0)
        private bool TryIncDec(AssignExpr a)
        {
            if (!(a.Target is IdentExpr tid)) return false;
            if (!_cur.Syms.TryGetValue(tid.Name, out var sym)) return false;
            if (sym.Type.IsArray) return false;
            string fp = FP(sym.Offset);

            // ── x = 0 → CLR mem ──────────────────────────────────
            if (a.Op == "=")
            {
                int? constVal = FoldConst(a.Value);
                if (constVal.HasValue)
                {
                    if (constVal.Value == 0)
                    {
                        EI("CLR", fp);
                        EI("CLR", "R0"); // результат выражения = 0
                        return true;
                    }
                    // x = const → MOV #N., mem (без лишнего MOV R0,mem)
                    EI("MOV", $"#{constVal.Value}., {fp}");
                    EI("MOV", $"#{constVal.Value}., R0");
                    return true;
                }
            }

            // ── x += N, x -= N ───────────────────────────────────
            if (a.Op == "+=" || a.Op == "-=")
            {
                int? rv = FoldConst(a.Value);
                if (rv == null) return false;
                int n = rv.Value;
                if (n == 0) { EI("MOV", $"{fp}, R0"); return true; } // x+=0 → нет-оп
                if (n == 1 && a.Op == "+=") { EI("INC", fp); EI("MOV", $"{fp}, R0"); return true; }
                if (n == -1 && a.Op == "+=") { EI("DEC", fp); EI("MOV", $"{fp}, R0"); return true; }
                if (n == 1 && a.Op == "-=") { EI("DEC", fp); EI("MOV", $"{fp}, R0"); return true; }
                if (n == -1 && a.Op == "-=") { EI("INC", fp); EI("MOV", $"{fp}, R0"); return true; }
                // Общий случай: ADD/SUB #N., mem
                string op = a.Op == "+=" ? "ADD" : "SUB";
                int absN = Math.Abs(n);
                if (n < 0) op = a.Op == "+=" ? "SUB" : "ADD"; // отрицательная константа
                EI(op, $"#{absN}., {fp}");
                EI("MOV", $"{fp}, R0");
                return true;
            }

            // ── x = x + N, x = x - N, x = N + x ─────────────────
            if (a.Op == "=")
            {
                var bin = a.Value as BinaryExpr;
                if (bin == null) return false;
                bool lIsX = bin.Left is IdentExpr li && li.Name == tid.Name;
                bool rIsX = bin.Right is IdentExpr ri && ri.Name == tid.Name;
                if (!lIsX && !rIsX) return false;
                int? cv = lIsX ? FoldConst(bin.Right) : FoldConst(bin.Left);
                if (cv == null) return false;
                int n = cv.Value;

                if (bin.Op == "+")
                {
                    if (n == 1) { EI("INC", fp); EI("MOV", $"{fp}, R0"); return true; }
                    if (n == -1) { EI("DEC", fp); EI("MOV", $"{fp}, R0"); return true; }
                    if (n == 0) { EI("MOV", $"{fp}, R0"); return true; }
                    // x = x + N → ADD #N., mem
                    if (n > 0) { EI("ADD", $"#{n}., {fp}"); EI("MOV", $"{fp}, R0"); return true; }
                    else { EI("SUB", $"#{-n}., {fp}"); EI("MOV", $"{fp}, R0"); return true; }
                }
                if (bin.Op == "-" && lIsX) // только x - N, не N - x
                {
                    if (n == 1) { EI("DEC", fp); EI("MOV", $"{fp}, R0"); return true; }
                    if (n == -1) { EI("INC", fp); EI("MOV", $"{fp}, R0"); return true; }
                    if (n == 0) { EI("MOV", $"{fp}, R0"); return true; }
                    // x = x - N → SUB #N., mem
                    if (n > 0) { EI("SUB", $"#{n}., {fp}"); EI("MOV", $"{fp}, R0"); return true; }
                    else { EI("ADD", $"#{-n}., {fp}"); EI("MOV", $"{fp}, R0"); return true; }
                }
            }
            return false;
        }

        // ── 7. Inline кандидат ────────────────────────────────────
        private bool IsInlineCandidate(FuncDeclNode f)
        {
            if (f.Params.Count > 4) return false;  // слишком много аргументов
            if (f.Body.Stmts.Count != 1) return false;
            var stmt = f.Body.Stmts[0];
            if (stmt is ReturnStmtNode ret)
                return ret.Value != null && !(ret.Value is CallExpr ce && ce.FuncName == f.Name);
            if (stmt is ExprStmtNode)
                return true;
            return false;
        }

        private readonly HashSet<string> _inlineFuncs = new HashSet<string>();

        // ── 8. Прямое условие без вычисления 0/1 ─────────────────
        // Вместо: GenExpr → TST R0 → JMPEQ
        // Генерирует: CMP/BIT/TST + прямой условный переход
        private bool TryDirectCond(ExprNode cond, string labelIfFalse)
        {
            var bin = cond as BinaryExpr;
            if (bin == null) return false;

            // ── BIT оптимизация ───────────────────────────────────
            // Паттерн: (x & const) == 0  или  (x & const) != 0
            // BIT #mask, x  устанавливает флаги как AND без записи результата
            //
            // Покрытые случаи:
            //   if (x & mask)          → BIT #mask, x / BEQ skip
            //   if (x & mask == 0)     → BIT #mask, x / BNE skip  (через != ниже)
            //   if (x & mask != 0)     → BIT #mask, x / BEQ skip
            //   if ((x & mask) == 0)   → BIT #mask, x / BNE skip
            //   if ((x & mask) != 0)   → BIT #mask, x / BEQ skip

            if (bin.Op == "==" || bin.Op == "!=")
            {
                // Определить какой операнд — AND-выражение, какой — ноль
                BinaryExpr andExpr = null;
                bool zeroSide = false; // true если ноль справа

                if (bin.Left is BinaryExpr lb && lb.Op == "&")
                {
                    // (x & mask) == 0  или  (x & mask) != 0
                    int? rConst = FoldConst(bin.Right);
                    if (rConst.HasValue && rConst.Value == 0) { andExpr = lb; zeroSide = true; }
                }
                else if (bin.Right is BinaryExpr rb && rb.Op == "&")
                {
                    // 0 == (x & mask)  или  0 != (x & mask)
                    int? lConst = FoldConst(bin.Left);
                    if (lConst.HasValue && lConst.Value == 0) { andExpr = rb; zeroSide = true; }
                }

                if (andExpr != null && zeroSide)
                {
                    if (TryEmitBIT(andExpr, labelIfFalse, bin.Op == "=="))
                        return true;
                }
            }

            // Прямое использование & как условие: if (x & mask)
            if (bin.Op == "&")
            {
                if (TryEmitBIT(bin, labelIfFalse, jumpIfZero: true))
                    return true;
            }

            // ── Общий случай: CMP R1, R0 ─────────────────────────
            string brFalse;
            switch (bin.Op)
            {
                case "==": brFalse = "BNE"; break;
                case "!=": brFalse = "BEQ"; break;
                case "<": brFalse = "BGE"; break;
                case ">": brFalse = "BLE"; break;
                case "<=": brFalse = "BGT"; break;
                case ">=": brFalse = "BLT"; break;
                default: return false;
            }
            GenExpr(bin.Left);
            EI("MOV", "R0, -(SP)");
            GenExpr(bin.Right);
            EI("MOV", "(SP)+, R1"); // R1=left, R0=right
            EI("CMP", "R1, R0");
            string skip = L();
            EI(InvBr(brFalse), skip);
            JMP(labelIfFalse);
            EL(skip);
            return true;
        }

        // Попытаться сгенерировать BIT #mask, operand для (x & mask)
        // jumpIfZero=true  → перейти на labelIfFalse если бит НЕ установлен (BEQ)
        // jumpIfZero=false → перейти на labelIfFalse если бит УСТАНОВЛЕН    (BNE)
        private bool TryEmitBIT(BinaryExpr andExpr, string labelIfFalse, bool jumpIfZero)
        {
            // Найти: одна сторона — константа (маска), другая — операнд памяти/рег
            IntLiteralExpr maskLit = null;
            ExprNode operand = null;

            if (andExpr.Right is IntLiteralExpr rm) { maskLit = rm; operand = andExpr.Left; }
            else if (andExpr.Left is IntLiteralExpr lm) { maskLit = lm; operand = andExpr.Right; }

            if (maskLit == null) return false;
            if (maskLit.Value == 0) return false; // BIT #0 бессмысленно

            // Операнд должен быть простым (переменная или элемент массива)
            // — чтобы можно было адресовать напрямую
            string bitSrc;
            if (operand is IdentExpr id &&
                _cur.Syms.TryGetValue(id.Name, out var sym) && !sym.Type.IsArray)
            {
                // Простая переменная: BIT #mask, -N.(R5)
                bitSrc = FP(sym.Offset);
            }
            else
            {
                // Сложный операнд: вычислить в R0, BIT #mask, R0
                GenExpr(operand);
                bitSrc = "R0";
            }

            EI("BIT", $"#{maskLit.Value}., {bitSrc}");

            // jumpIfZero=true  → условие FALSE если Z=1 (бит не установлен) → BEQ labelIfFalse
            // jumpIfZero=false → условие FALSE если Z=0 (бит установлен)     → BNE labelIfFalse
            string brFalse = jumpIfZero ? "BEQ" : "BNE";
            string skip = L();
            EI(InvBr(brFalse), skip);
            JMP(labelIfFalse);
            EL(skip);
            return true;
        }

        private string InvBr(string br)
        {
            switch (br)
            {
                case "BEQ": return "BNE";
                case "BNE": return "BEQ";
                case "BLT": return "BGE";
                case "BGE": return "BLT";
                case "BGT": return "BLE";
                case "BLE": return "BGT";
                default: return "BNE";
            }
        }

        // ── Степень двойки ────────────────────────────────────────
        private static int Log2Exact(int n)
        {
            if (n <= 0) return -1;
            int shift = 0;
            while (n > 1) { if ((n & 1) != 0) return -1; n >>= 1; shift++; }
            return shift;
        }

        private void EmitMulConst(int n, string reg)
        {
            // Степень 2 → только ASL
            int shift = Log2Exact(n);
            if (shift == 0) return;
            if (shift > 0)
            {
                for (int i = 0; i < shift; i++) EI("ASL", reg);
                InvalidateR0();
                return;
            }

            // Малые константы через сдвиги и сложения — без MUL
            // reg = x. tmp — временный регистр.
            string tmp = (reg == "R1") ? "R0" : "R1";

            switch (n)
            {
                case 3:  // x*2 + x
                    EI("MOV", $"{reg}, {tmp}");
                    EI("ASL", reg);
                    EI("ADD", $"{tmp}, {reg}");
                    break;
                case 5:  // x*4 + x
                    EI("MOV", $"{reg}, {tmp}");
                    EI("ASL", reg); EI("ASL", reg);
                    EI("ADD", $"{tmp}, {reg}");
                    break;
                case 6:  // x*4 + x*2
                    EI("ASL", reg);
                    EI("MOV", $"{reg}, {tmp}");
                    EI("ASL", reg);
                    EI("ADD", $"{tmp}, {reg}");
                    break;
                case 7:  // x*8 - x
                    EI("MOV", $"{reg}, {tmp}");
                    EI("ASL", reg); EI("ASL", reg); EI("ASL", reg);
                    EI("SUB", $"{tmp}, {reg}");
                    break;
                case 9:  // x*8 + x
                    EI("MOV", $"{reg}, {tmp}");
                    EI("ASL", reg); EI("ASL", reg); EI("ASL", reg);
                    EI("ADD", $"{tmp}, {reg}");
                    break;
                case 10: // x*8 + x*2
                    EI("MOV", $"{reg}, {tmp}");
                    EI("ASL", tmp);
                    EI("ASL", reg); EI("ASL", reg); EI("ASL", reg);
                    EI("ADD", $"{tmp}, {reg}");
                    break;
                case 11: // x*8 + x*2 + x
                    EI("MOV", $"{reg}, {tmp}");
                    EI("ASL", reg); EI("ASL", reg); EI("ASL", reg); // *8
                    EI("ADD", $"{tmp}, {reg}");                      // *9
                    EI("ASL", tmp);                                  // tmp=x*2
                    EI("ADD", $"{tmp}, {reg}");                      // *11
                    break;
                case 12: // x*8 + x*4
                    EI("MOV", $"{reg}, {tmp}");
                    EI("ASL", tmp); EI("ASL", tmp);
                    EI("ASL", reg); EI("ASL", reg); EI("ASL", reg);
                    EI("ADD", $"{tmp}, {reg}");
                    break;
                case 13: // x*8 + x*4 + x  →  tmp=x, reg=x*4, reg+=tmp(→x*5), reg*=2(→x*10+??)
                         // Правильно: tmp=x, reg*=4, reg+=tmp(→x*5), reg*=2(→x*10), tmp*=2(→x*2), reg+=tmp(→x*12)?
                         // Проще: reg=x*16-x*2-x = x*13
                    EI("MOV", $"{reg}, {tmp}");                       // tmp=x
                    EI("ASL", reg); EI("ASL", reg); EI("ASL", reg); EI("ASL", reg); // reg=x*16
                    EI("SUB", $"{tmp}, {reg}");                       // reg=x*15
                    EI("ASL", tmp);                                   // tmp=x*2
                    EI("SUB", $"{tmp}, {reg}");                       // reg=x*13
                    break;
                case 19: // x*16 + x*2 + x
                    EI("MOV", $"{reg}, {tmp}");
                    EI("ASL", reg); EI("ASL", reg); EI("ASL", reg); EI("ASL", reg); // *16
                    EI("ADD", $"{tmp}, {reg}");                       // *17
                    EI("ASL", tmp);                                   // tmp=x*2
                    EI("ADD", $"{tmp}, {reg}");                       // *19
                    break;
                case 21: // x*16 + x*4 + x
                    EI("MOV", $"{reg}, {tmp}");
                    EI("ASL", reg); EI("ASL", reg); EI("ASL", reg); EI("ASL", reg); // *16
                    EI("ADD", $"{tmp}, {reg}");                       // *17
                    EI("ASL", tmp); EI("ASL", tmp);                   // tmp=x*4
                    EI("ADD", $"{tmp}, {reg}");                       // *21
                    break;
                case 14: // x*16 - x*2
                    EI("MOV", $"{reg}, {tmp}");
                    EI("ASL", tmp);                                   // tmp = x*2
                    EI("ASL", reg); EI("ASL", reg); EI("ASL", reg); EI("ASL", reg); // *16
                    EI("SUB", $"{tmp}, {reg}");
                    break;
                case 15: // x*16 - x
                    EI("MOV", $"{reg}, {tmp}");
                    EI("ASL", reg); EI("ASL", reg); EI("ASL", reg); EI("ASL", reg);
                    EI("SUB", $"{tmp}, {reg}");
                    break;
                case 17: // x*16 + x
                    EI("MOV", $"{reg}, {tmp}");
                    EI("ASL", reg); EI("ASL", reg); EI("ASL", reg); EI("ASL", reg);
                    EI("ADD", $"{tmp}, {reg}");
                    break;
                case 18: // x*16 + x*2
                    EI("MOV", $"{reg}, {tmp}");
                    EI("ASL", tmp);
                    EI("ASL", reg); EI("ASL", reg); EI("ASL", reg); EI("ASL", reg);
                    EI("ADD", $"{tmp}, {reg}");
                    break;
                case 20: // x*16 + x*4
                    EI("MOV", $"{reg}, {tmp}");
                    EI("ASL", tmp); EI("ASL", tmp);
                    EI("ASL", reg); EI("ASL", reg); EI("ASL", reg); EI("ASL", reg);
                    EI("ADD", $"{tmp}, {reg}");
                    break;
                case 24: // x*16 + x*8
                    EI("MOV", $"{reg}, {tmp}");
                    EI("ASL", tmp); EI("ASL", tmp); EI("ASL", tmp);
                    EI("ASL", reg); EI("ASL", reg); EI("ASL", reg); EI("ASL", reg);
                    EI("ADD", $"{tmp}, {reg}");
                    break;
                case 25: // x*16 + x*8 + x
                    EI("MOV", $"{reg}, {tmp}");
                    EI("ASL", reg); EI("ASL", reg); EI("ASL", reg); EI("ASL", reg); // *16
                    EI("ADD", $"{tmp}, {reg}");                      // *17
                    EI("ASL", tmp); EI("ASL", tmp); EI("ASL", tmp); // tmp=x*8
                    EI("ADD", $"{tmp}, {reg}");                      // *25
                    break;
                default:
                    // Общий случай — MUL
                    if (reg == "R0") { EI("MOV", "R0, R1"); EI("MOV", $"#{n}., R0"); EI("MUL", "R0, R1"); EI("MOV", "R1, R0"); }
                    else { EI("MOV", $"#{n}., R0"); EI("MUL", "R0, R1"); EI("MOV", "R1, " + reg); }
                    break;
            }
            InvalidateR0();
        }

        private void EmitDivConst(int n)
        {
            // Деление на 1 → нет-оп
            if (n == 1) { EI("MOV", "R1, R0"); InvalidateR0(); return; }

            // Степень 2 → ASR (точно, быстро)
            int shift = Log2Exact(n);
            if (shift > 0)
            {
                EI("MOV", "R1, R0");
                for (int i = 0; i < shift; i++) EI("ASR", "R0");
                InvalidateR0();
                return;
            }

            // Малые константы: умножение на обратное (reciprocal multiplication)
            // x / N ≈ (x * M) >> S  где M = ceil(2^S / N), S подобрано так чтобы
            // результат был точным для 16-битных целых без знака (0..32767).
            // Формула: M = (2^S + N - 1) / N,  S = 16 + floor(log2(N))
            // Проверяем точность для всего диапазона [0..32767].
            //
            // На PDP-11:
            //   MOV R1, R0          ; R0 = x
            //   MOV #M., R1         ; R1 = magic
            //   MUL R1, R0          ; R0:R1 = x * M  (32-бит результат в паре R0:R1)
            //   ASR R0 (S-16 раз)   ; R0 = высокое слово >> (S-16)  = x/N
            //
            // Таблица проверенных магических чисел (точные для 0..32767):
            int magic, extraShift;
            if (TryReciprocalDiv(n, out magic, out extraShift))
            {
                EC($"div {n} → reciprocal * {magic} >> {16 + extraShift}");
                EI("MOV", "R1, R0");           // R0 = x
                EI("MOV", $"#{magic}., R1");   // R1 = magic
                EI("MUL", "R1, R0");           // R0:R1 = x * magic (32-бит)
                // После MUL R1,R0: R0 = старшее слово, R1 = младшее
                for (int i = 0; i < extraShift; i++) EI("ASR", "R0");
                // Результат в R0
                InvalidateR0();
                return;
            }

            // Общий случай — DIV (медленно, но точно)
            EI("MOV", "R0, -(SP)");
            EI("MOV", "R1, R0");
            EI("CLR", "R1");
            EI("DIV", "(SP)+, R0");
            InvalidateR0();
        }

        // Подобрать магическое число для деления на n через умножение.
        // Возвращает true если найдено точное решение для диапазона [0..32767].
        // magic * x >> (16 + extraShift) == x / n  для всех x в [0..32767]
        private static bool TryReciprocalDiv(int n, out int magic, out int extraShift)
        {
            magic = 0; extraShift = 0;
            if (n <= 1 || n > 255) return false;

            // Перебираем extraShift от 0 до 7 (S = 16..23)
            for (int es = 0; es <= 7; es++)
            {
                long pow = 1L << (16 + es);
                // Магическое число: округление вверх
                long m = (pow + n - 1) / n;
                if (m > 32767) continue; // не влезает в 16-бит со знаком

                // Проверить точность для всего диапазона [0..32767]
                bool ok = true;
                for (int x = 0; x <= 32767; x++)
                {
                    long product = (long)x * m;
                    int got = (int)(product >> (16 + es));
                    if (got != x / n) { ok = false; break; }
                }
                if (ok)
                {
                    magic = (int)m;
                    extraShift = es;
                    return true;
                }
            }
            return false;
        }

        private void EmitModConst(int n)
        {
            int shift = Log2Exact(n);
            if (shift > 0) { EI("MOV", "R1, R0"); EI("BIC", $"#{~(n - 1) & 0xFFFF}., R0"); }
            else { EI("MOV", "R0, -(SP)"); EI("MOV", "R1, R0"); EI("CLR", "R1"); EI("DIV", "(SP)+, R0"); EI("MOV", "R1, R0"); }
            InvalidateR0();
        }
        // CMP R1, R0 → 0 или 1 в R0
        private void Cmp(string br)
        {
            EI("CMP", "R1, R0");
            string t = L(), e = L();
            EI(br, t); EI("CLR", "R0"); JMP(e);
            EL(t); EI("MOV", "#1., R0"); EL(e);
        }

        // Проверить совпадение двух выражений — одна и та же переменная
        private static bool IsSameVar(ExprNode a, ExprNode b)
        {
            if (a is IdentExpr ai && b is IdentExpr bi) return ai.Name == bi.Name;
            return false;
        }

        // Проверить одинаковое выражение (для x-x, x+x и подобных)
        private static bool IsSameExpr(ExprNode a, ExprNode b)
        {
            if (IsSameVar(a, b)) return true;
            if (a is IntLiteralExpr al && b is IntLiteralExpr bl) return al.Value == bl.Value;
            if (a is ArrayIndexExpr aa && b is ArrayIndexExpr ba)
                return IsSameExpr(aa.Array, ba.Array) && IsSameExpr(aa.Index, ba.Index);
            return false;
        }

        // ── Свёртка силы операций ─────────────────────────────────
        // Ловим паттерны которые можно выразить без стека и без MUL
        private bool TryStrengthReduction(BinaryExpr b)
        {
            // x + x  →  ASL R0  (умножение на 2 без MUL и без стека)
            if (b.Op == "+" && IsSameExpr(b.Left, b.Right))
            {
                GenExpr(b.Left);
                EI("ASL", "R0");
                return true;
            }

            // x - x  →  CLR R0
            if (b.Op == "-" && IsSameExpr(b.Left, b.Right))
            {
                EI("CLR", "R0");
                return true;
            }

            // x * 0  →  CLR R0
            // 0 * x  →  CLR R0
            if (b.Op == "*")
            {
                if (b.Right is IntLiteralExpr r0 && r0.Value == 0) { EI("CLR", "R0"); return true; }
                if (b.Left is IntLiteralExpr l0 && l0.Value == 0) { EI("CLR", "R0"); return true; }
            }

            // x * 1  →  GenExpr(x)
            // 1 * x  →  GenExpr(x)
            if (b.Op == "*")
            {
                if (b.Right is IntLiteralExpr r1 && r1.Value == 1) { GenExpr(b.Left); return true; }
                if (b.Left is IntLiteralExpr l1 && l1.Value == 1) { GenExpr(b.Right); return true; }
            }

            // x + 0  →  GenExpr(x)
            // 0 + x  →  GenExpr(x)
            if (b.Op == "+")
            {
                if (b.Right is IntLiteralExpr r0 && r0.Value == 0) { GenExpr(b.Left); return true; }
                if (b.Left is IntLiteralExpr l0 && l0.Value == 0) { GenExpr(b.Right); return true; }
            }

            // x - 0  →  GenExpr(x)
            if (b.Op == "-")
            {
                if (b.Right is IntLiteralExpr r0 && r0.Value == 0) { GenExpr(b.Left); return true; }
            }

            // x == x  →  MOV #1, R0  (всегда истина)
            // x != x  →  CLR R0      (всегда ложь)
            if (b.Op == "==" && IsSameExpr(b.Left, b.Right)) { EI("MOV", "#1., R0"); return true; }
            if (b.Op == "!=" && IsSameExpr(b.Left, b.Right)) { EI("CLR", "R0"); return true; }

            // const OP const  →  уже обрабатывается FoldConst выше
            return false;
        }
        private void GenAssign(AssignExpr a)
        {
            // Оптимизация 4: x = x±1, x += 1, x -= 1  →  INC/DEC
            if (TryIncDec(a)) return;

            if (a.Op == "=")
            {
                if (a.Target is IdentExpr tid &&
                    _cur.Syms.TryGetValue(tid.Name, out var sym) && !sym.Type.IsArray)
                {
                    GenExpr(a.Value);
                    // bool: нормализовать к 0/1 — любое ненулевое → 1
                    if (sym.Type.IsBool) EmitNormalizeBool();
                    EI("MOV", $"R0, {FP(sym.Offset)}");
                    return;
                }
                GenExpr(a.Value);
                EI("MOV", "R0, -(SP)");
                GenAddr(a.Target);
                EI("MOV", "(SP)+, R0");
                EI("MOV", "R0, (R1)");
                return;
            }

            // Составное: read-modify-write
            GenAddr(a.Target);
            EI("MOV", "R1, -(SP)");
            EI("MOV", "(R1), -(SP)");
            GenExpr(a.Value);
            EI("MOV", "(SP)+, R1");

            switch (a.Op)
            {
                case "+=": EI("ADD", "R0, R1"); EI("MOV", "R1, R0"); break;
                case "-=": EI("SUB", "R0, R1"); EI("MOV", "R1, R0"); break;
                case "*=": EI("MUL", "R0, R1"); EI("MOV", "R1, R0"); break;
                case "/=":
                    EI("MOV", "R0, -(SP)"); EI("MOV", "R1, R0"); EI("CLR", "R1");
                    EI("DIV", "(SP)+, R0");
                    break;
                case "%=":
                    EI("MOV", "R0, -(SP)"); EI("MOV", "R1, R0"); EI("CLR", "R1");
                    EI("DIV", "(SP)+, R0"); EI("MOV", "R1, R0");
                    break;
                case "&=":
                    EI("COM", "R0");           // R0 = ~right
                    EI("BIC", "R0, R1");       // R1 &= right
                    EI("MOV", "R1, R0");
                    break;
                case "|=": EI("BIS", "R0, R1"); EI("MOV", "R1, R0"); break;
                case "^=": EI("XOR", "R0, R1"); EI("MOV", "R1, R0"); break;
                case "<<=":
                    {
                        string lbl = L(), end = L();
                        EI("TST", "R0"); JMPEQ(end);
                        ELTracked(lbl); EI("ASL", "R1"); EI("DEC", "R0"); JMPNE(lbl);
                        ELTracked(end); EI("MOV", "R1, R0");
                        break;
                    }
                case ">>=":
                    {
                        string lbl = L(), end = L();
                        EI("TST", "R0"); JMPEQ(end);
                        ELTracked(lbl); EI("ASR", "R1"); EI("DEC", "R0"); JMPNE(lbl);
                        ELTracked(end); EI("MOV", "R1, R0");
                        break;
                    }
            }

            EI("MOV", "(SP)+, R1");
            EI("MOV", "R0, (R1)");
        }

        // Нормализовать R0 к 0 или 1 (для bool)
        // R0 != 0 → R0 = 1; R0 == 0 → R0 = 0
        // Алгоритм: NEG R0; ADC R0; NEG R0
        //   если R0=0:  NEG→0, ADC→0(C=0), NEG→0
        //   если R0=N:  NEG→-N(C=1), ADC→-N+1, NEG→N-1... нет
        // Проще: TST R0 / BEQ skip / MOV #1,R0 / skip:
        private void EmitNormalizeBool()
        {
            string skip = L();
            EI("BEQ", skip);          // если уже 0 — пропустить
            EI("MOV", "#1., R0");     // любое ненулевое → 1
            ELTracked(skip);
        }

        // ── Вызов функции ─────────────────────────────────────────
        private void GenCall(CallExpr c)
        {
            // Встроенные функции
            if (_builtins.Contains(c.FuncName)) { GenBuiltin(c); return; }

            if (!_funcs.TryGetValue(c.FuncName, out var fi))
                throw new Exception($"Строка {c.Line}: функция '{c.FuncName}' не объявлена");

            // Оптимизация 7: inline подстановка
            if (_inlineFuncs.Contains(c.FuncName))
            {
                GenInline(c, fi); return;
            }

            // Аргументы: справа налево
            for (int i = c.Args.Count - 1; i >= 0; i--)
            {
                var arg = c.Args[i];

                // Массив → передаём адрес
                if (arg is IdentExpr argId &&
                    _cur.Syms.TryGetValue(argId.Name, out var argSym) &&
                    argSym.Type.IsArray)
                {
                    if (argSym.IsParam)
                        EI("MOV", $"{FP(argSym.Offset)}, -(SP)");
                    else
                    {
                        EI("MOV", "R5, R0");
                        if (argSym.Offset != 0)
                            EI(argSym.Offset > 0 ? "ADD" : "SUB",
                               $"#{Math.Abs(argSym.Offset)}., R0");
                        EI("MOV", "R0, -(SP)");
                    }
                    continue;
                }

                GenExpr(arg);
                EI("MOV", "R0, -(SP)");
            }

            EI("JSR", $"PC, {fi.AsmLbl}");

            if (c.Args.Count > 0)
                EI("ADD", $"#{c.Args.Count * 2}., SP");
        }

        // ── Встроенные функции ────────────────────────────────────
        private void GenBuiltin(CallExpr c)
        {
            // Все встроенные используют соглашение caller-cleans-up
            // с передачей аргументов через стек, кроме cls/init/pause.
            switch (c.FuncName)
            {
                case "init":
                case "cls":
                    if (c.Args.Count != 1)
                        throw new Exception($"Строка {c.Line}: {c.FuncName}(mode) требует 1 аргумент (0..3)");
                    EC($"{c.FuncName}(mode): настройка экрана");
                    GenExpr(c.Args[0]);   // R0 = mode
                    EI("JSR", "PC, RTCLS");
                    break;

                case "pause":
                    if (c.Args.Count != 0)
                        throw new Exception($"Строка {c.Line}: pause() не принимает аргументов");
                    EC("pause(): пауза");
                    EI("JSR", "PC, RTPAUS");
                    break;

                case "box":
                    if (c.Args.Count != 5)
                        throw new Exception($"Строка {c.Line}: box(x,y,w,h,color) требует 5 аргументов");
                    EC($"box({ArgStr(c)}): прямоугольник");
                    // Вычислить color первым и сохранить на стек
                    // (color = первый аргумент в стеке = дальше от вершины)
                    GenExpr(c.Args[4]);          // R0 = индекс цвета
                    EI("JSR", "PC, RTCLR");      // R0 = слово цвета
                    EI("MOV", "R0, -(SP)");      // push color
                    // Затем h, w, y, x (справа налево из оставшихся)
                    GenExpr(c.Args[3]); EI("MOV", "R0, -(SP)"); // push h
                    GenExpr(c.Args[2]); EI("MOV", "R0, -(SP)"); // push w
                    GenExpr(c.Args[1]); EI("MOV", "R0, -(SP)"); // push y
                    GenExpr(c.Args[0]); EI("MOV", "R0, -(SP)"); // push x
                    EI("JSR", "PC, RTBOX");
                    EI("ADD", "#10., SP");
                    break;

                case "sprite":
                    if (c.Args.Count != 5)
                        throw new Exception($"Строка {c.Line}: sprite(x,y,w,h,ptr) требует 5 аргументов");
                    EC($"sprite({ArgStr(c)}): спрайт");
                    for (int i = 4; i >= 0; i--) { GenExpr(c.Args[i]); EI("MOV", "R0, -(SP)"); }
                    EI("JSR", "PC, RTSPR");
                    EI("ADD", "#10., SP");
                    break;

                case "spriteOr":
                    if (c.Args.Count != 5)
                        throw new Exception($"Строка {c.Line}: spriteOr(x,y,w,h,ptr) требует 5 аргументов");
                    EC($"spriteOr({ArgStr(c)}): спрайт через BIS");
                    for (int i = 4; i >= 0; i--) { GenExpr(c.Args[i]); EI("MOV", "R0, -(SP)"); }
                    EI("JSR", "PC, RTSPB");
                    EI("ADD", "#10., SP");
                    break;

                case "circle":
                    if (c.Args.Count != 4)
                        throw new Exception($"Строка {c.Line}: circle(cx,cy,r,color) требует 4 аргумента");
                    EC($"circle({ArgStr(c)}): окружность Брезенхема");
                    GenExpr(c.Args[3]); EI("MOV", "R0, -(SP)"); // color
                    GenExpr(c.Args[2]); EI("MOV", "R0, -(SP)"); // r
                    GenExpr(c.Args[1]); EI("MOV", "R0, -(SP)"); // cy
                    GenExpr(c.Args[0]); EI("MOV", "R0, -(SP)"); // cx
                    EI("JSR", "PC, RTCRC");
                    EI("ADD", "#8., SP");
                    break;

                case "getTimer":
                    if (c.Args.Count != 0)
                        throw new Exception($"Строка {c.Line}: getTimer() не принимает аргументов");
                    EI("JSR", "PC, RTGTIM");   // результат в R0
                    break;

                case "printnum":
                    if (c.Args.Count != 1)
                        throw new Exception($"Строка {c.Line}: printnum(n) требует 1 аргумент");
                    GenExpr(c.Args[0]); EI("MOV", "R0, -(SP)");
                    EI("JSR", "PC, RTPNUM");
                    EI("ADD", "#2., SP");
                    break;

                case "print":
                    // print("text") — вывод строки через RTPRNT
                    // print(expr)   — вывод числа (не реализовано пока — только строки)
                    if (c.Args.Count != 1)
                        throw new Exception($"Строка {c.Line}: print(str) требует 1 аргумент");
                    if (c.Args[0] is StringLiteralExpr sle)
                    {
                        EC($"print(\"{sle.Value}\"): вывод строки");
                        string lbl = InternString(sle.Value);
                        EI("MOV", $"#{lbl}, R1");
                        EI("JSR", "PC, RTPRNT");
                    }
                    else
                    {
                        throw new Exception($"Строка {c.Line}: print() принимает только строковые литералы");
                    }
                    break;

                case "point":
                    if (c.Args.Count != 3)
                        throw new Exception($"Строка {c.Line}: point(x,y,color) требует 3 аргумента");
                    EC($"point({ArgStr(c)}): пиксель");
                    GenExpr(c.Args[2]); EI("MOV", "R0, -(SP)"); // color
                    GenExpr(c.Args[1]); EI("MOV", "R0, -(SP)"); // y
                    GenExpr(c.Args[0]); EI("MOV", "R0, -(SP)"); // x
                    EI("JSR", "PC, RTPPNT");
                    EI("ADD", "#6., SP");
                    break;

                case "line":
                    if (c.Args.Count != 5)
                        throw new Exception($"Строка {c.Line}: line(x0,y0,x1,y1,color) требует 5 аргументов");
                    EC($"line({ArgStr(c)}): линия Брезенхэма");
                    GenExpr(c.Args[4]); EI("MOV", "R0, -(SP)"); // color
                    GenExpr(c.Args[3]); EI("MOV", "R0, -(SP)"); // y1
                    GenExpr(c.Args[2]); EI("MOV", "R0, -(SP)"); // x1
                    GenExpr(c.Args[1]); EI("MOV", "R0, -(SP)"); // y0
                    GenExpr(c.Args[0]); EI("MOV", "R0, -(SP)"); // x0
                    EI("JSR", "PC, RTLINE");
                    EI("ADD", "#10., SP");
                    break;

                case "rect":
                    if (c.Args.Count != 5)
                        throw new Exception($"Строка {c.Line}: rect(x,y,w,h,color) требует 5 аргументов");
                    EC($"rect({ArgStr(c)}): прямоугольник");
                    GenExpr(c.Args[4]); EI("MOV", "R0, -(SP)"); // color
                    GenExpr(c.Args[3]); EI("MOV", "R0, -(SP)"); // h
                    GenExpr(c.Args[2]); EI("MOV", "R0, -(SP)"); // w
                    GenExpr(c.Args[1]); EI("MOV", "R0, -(SP)"); // y
                    GenExpr(c.Args[0]); EI("MOV", "R0, -(SP)"); // x
                    EI("JSR", "PC, RTRECT");
                    EI("ADD", "#10., SP");
                    break;

                case "waitkey":
                    if (c.Args.Count != 0)
                        throw new Exception($"Строка {c.Line}: waitkey() не принимает аргументов");
                    EC("waitkey(): ждать клавишу → R0");
                    EI("JSR", "PC, RTWKEY");
                    break;

                case "getkey":
                    // getkey() — неблокирующее чтение клавиши.
                    // Возвращает 0 если ничего не нажато.
                    if (c.Args.Count != 0)
                        throw new Exception($"Строка {c.Line}: getkey() не принимает аргументов");
                    EC("getkey(): прочитать клавишу без ожидания → R0 (0=нет)");
                    EI("JSR", "PC, RTGKEY");
                    break;

                default:
                    throw new Exception($"Строка {c.Line}: неизвестная встроенная функция '{c.FuncName}'");
            }
        }

        private string ArgStr(CallExpr c)
        {
            var parts = new System.Text.StringBuilder();
            for (int i = 0; i < c.Args.Count; i++)
            {
                if (i > 0) parts.Append(',');
                if (c.Args[i] is IntLiteralExpr lit) parts.Append(lit.Value);
                else if (c.Args[i] is IdentExpr id) parts.Append(id.Name);
                else parts.Append('?');
            }
            return parts.ToString();
        }

        // ── 2/3. Кэш R0 отключён — слишком ненадёжен ────────────
        private bool _r0Known = false;
        private int _r0Val = 0;
        private void SetR0(int v) { }
        private void InvalidateR0() { }

        // Выдать константу в R0
        private void EmitLiteralOpt(int v)
        {
            if (v == 0) EI("CLR", "R0");
            else EI("MOV", $"#{v}., R0");
        }

        // ── 5. Короткий переход если метка уже близко ────────────
        private readonly System.Collections.Generic.Dictionary<string, int> _labelPos =
            new System.Collections.Generic.Dictionary<string, int>();

        // Запомнить позицию метки по длине текста в _out
        private void ELTracked(string l)
        {
            _out.AppendLine(l + ":");
            _labelPos[l] = _out.Length;
        }

        private void EmitBrOrJmp(string label)
        {
            // Метка уже определена и текущая позиция близко к ней.
            // Эмпирически: одна инструкция Macro-11 ≈ 25 символов текста,
            // BR достигает ±127 байт кода ≈ ~40 инструкций ≈ ~1000 символов.
            // Берём запас: 800 символов.
            if (_labelPos.TryGetValue(label, out int pos) &&
                _out.Length - pos < 800)
                EI("BR", label);
            else
                EI("JMP", label);
        }

        // ── 7. Inline подстановка простой функции ────────────────
        // Подставляет тело функции прямо на месте вызова,
        // привязывая аргументы к параметрам через временные переменные.
        private void GenInline(CallExpr c, FuncInfo fi)
        {
            EC($"inline: {fi.Name}({ArgStr(c)})");

            // Вычислить аргументы и запомнить смещения
            // Создаём временные локальные переменные для параметров
            var savedSyms = new System.Collections.Generic.Dictionary<string, SymInfo>();
            foreach (var kv in _cur.Syms) savedSyms[kv.Key] = kv.Value;

            // Для каждого параметра: вычислить аргумент, сохранить в новую локальную
            var paramVars = new System.Collections.Generic.List<string>();
            for (int i = 0; i < fi.Params.Count && i < c.Args.Count; i++)
            {
                var p = fi.Params[i];
                string tmpName = "__il_" + p.Name + "_" + _labelCnt++;
                GenExpr(c.Args[i]);
                var info = Decl(tmpName, p.Type);
                EI("MOV", $"R0, {FP(info.Offset)}");
                // Переопределить символ параметра как локальный
                _cur.Syms[p.Name] = info;
                paramVars.Add(p.Name);
            }

            // Найти исходное объявление функции
            var funcDecl = _progFuncs != null && _progFuncs.TryGetValue(fi.Name, out var fd) ? fd : null;
            if (funcDecl != null)
            {
                // Генерировать тело (только первый оператор — критерий inline)
                var stmt = funcDecl.Body.Stmts[0];
                if (stmt is ReturnStmtNode ret)
                {
                    if (ret.Value != null) GenExpr(ret.Value);
                }
                else GenStmt(stmt);
            }

            // Восстановить таблицу символов
            foreach (var kv in savedSyms) _cur.Syms[kv.Key] = kv.Value;
            // Убрать временные параметры
            foreach (var pName in paramVars)
                if (!savedSyms.ContainsKey(pName)) _cur.Syms.Remove(pName);
        }

        // Хранилище AST функций для inline
        private System.Collections.Generic.Dictionary<string, FuncDeclNode> _progFuncs;
    }
}
