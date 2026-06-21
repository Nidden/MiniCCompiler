using System;
using System.Collections.Generic;
using System.Text;

namespace CompMacro11
{
    // Рантайм-библиотека Mini-C → Macro-11 (вынесено из CodeGen.cs)
    public partial class CodeGen
    {
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
            E("; RTSPR — sprite(x,y,w,h,ptr)");
            E(";   x,y в пикселях. w в пикселях кратно 8. h строк.");
            E(";   x кратен 8: быстрый путь — прямое копирование слов.");
            E(";   x не кратен 8: буферный путь — строка в SPBUF[9],");
            E(";     сдвиг вправо на s=x&7 бит через цепочку ROR,");
            E(";     затем вывод w/8+1 слов. Ширина спрайта <= 64px.");
            E("RTSPR:");
            E("        MOV	R5, -(SP)");
            E("        MOV	SP, R5");
            E("        MOV	R0, -(SP)");
            E("        MOV	R1, -(SP)");
            E("        MOV	R2, -(SP)");
            E("        MOV	R3, -(SP)");
            E("        MOV	R4, -(SP)");
            E("        MOV	4.(R5),  R0");   // x
            E("        MOV	6.(R5),  R1");   // y
            E("        MOV	8.(R5),  R2");   // w (пиксели)
            E("        MOV	10.(R5), R3");   // h (строк)
            E("        MOV	12.(R5), R4");   // ptr
            // w/8 → слов
            E("        ASR	R2"); E("        ASR	R2"); E("        ASR	R2");
            // Адрес строки VRAM
            E("        ASL	R1");
            E("        MOV	DSPST(R1), R5");
            // s = x & 7
            E("        MOV	R0, R1");
            E("        BIC	#177770, R1");   // R1 = s
            E("        BNE	RTSPS");         // s!=0 → буферный путь
            // ── БЫСТРЫЙ ПУТЬ: x кратен 8 ─────────────────────────
            // R5=начало строки, R0=x, R2=words, R3=h, R4=ptr
            E("        ASR	R0"); E("        ASR	R0"); E("        ASR	R0");
            E("        ADD	R0, R5");        // R5 = адрес первого слова
            E("        MOV	#80., R0");
            E("        SUB	R2, R0");        // R0 = шаг строки
            E("RTSP1:  MOV	R2, R1");        // R1 = счётчик слов
            E("RTSP2:  MOV	R5, @#176640");
            E("        MOV	(R4)+, @#176642");
            E("        INC	R5");
            E("        DEC	R1");
            E("        BNE	RTSP2");
            E("        ADD	R0, R5");
            E("        DEC	R3");
            E("        BNE	RTSP1");
            E("        BR	RTSPX");
            // ── БУФЕРНЫЙ ПУТЬ: x не кратен 8 ─────────────────────
            // R1=s, R0=x, R2=words, R3=h, R4=ptr, R5=начало строки
            E("RTSPS:");
            E("        ASR	R0"); E("        ASR	R0"); E("        ASR	R0");
            E("        ADD	R0, R5");
            E("        MOV	#80., R0");
            E("        SUB	R2, R0");
            E("        DEC	R0");
            E("        MOV	R1, -(SP)");     // push s
            E("        MOV	R0, -(SP)");     // push step
            E("        MOV	R3, -(SP)");     // push h; SP+0=h SP+2=step SP+4=s
            E("RTSPL1:");
            E("        MOV	R2, R1");
            E("        MOV	#SPBUF, R0");
            E("RTSPC1: MOV	(R4)+, (R0)+");
            E("        DEC	R1");
            E("        BNE	RTSPC1");
            E("        CLR	(R0)");
            E("        MOV	4.(SP), R1");    // R1 = s
            E("        BEQ	RTSPE1");
            // RORB справа налево: carry из бит0 правого байта → бит7 левого
            // CLC перед каждым проходом
            E("RTSPS1: CLC");
            E("        ROLB	SPBUF+1.");
            E("        ROLB	SPBUF+3.");
            E("        ROLB	SPBUF+5.");
            E("        ROLB	SPBUF+7.");
            E("        ROLB	SPBUF+9.");
            E("        ROLB	SPBUF+11.");
            E("        ROLB	SPBUF+13.");
            E("        ROLB	SPBUF+15.");
            E("        ROLB	SPBUF+17.");
            E("        ROLB	SPBUF+19.");
            E("        ROLB	SPBUF+21.");
            E("        ROLB	SPBUF+23.");
            E("        ROLB	SPBUF+25.");
            E("        ROLB	SPBUF+27.");
            E("        ROLB	SPBUF+29.");
            E("        CLC");
            E("        ROLB	SPBUF+0.");
            E("        ROLB	SPBUF+2.");
            E("        ROLB	SPBUF+4.");
            E("        ROLB	SPBUF+6.");
            E("        ROLB	SPBUF+8.");
            E("        ROLB	SPBUF+10.");
            E("        ROLB	SPBUF+12.");
            E("        ROLB	SPBUF+14.");
            E("        ROLB	SPBUF+16.");
            E("        ROLB	SPBUF+18.");
            E("        ROLB	SPBUF+20.");
            E("        ROLB	SPBUF+22.");
            E("        ROLB	SPBUF+24.");
            E("        ROLB	SPBUF+26.");
            E("        ROLB	SPBUF+28.");
            E("        DEC	R1");
            E("        BEQ	RTSPE1");
            E("        JMP	RTSPS1");
            E("RTSPE1: MOV	R2, R1");
            E("        INC	R1");
            E("        MOV	#SPBUF, R0");
            E("RTSPO1: MOV	R5, @#176640");
            E("        MOV	(R0)+, @#176642");
            E("        INC	R5");
            E("        DEC	R1");
            E("        BNE	RTSPO1");
            E("        ADD	2.(SP), R5");    // step
            E("        DEC	0.(SP)");        // h--
            E("        BEQ	RTSPL3");
            E("        JMP	RTSPL1");
            E("RTSPL3: TST	(SP)+");
            E("        TST	(SP)+");
            E("        TST	(SP)+");
            E("RTSPX:");
            E("        MOV	(SP)+, R4");
            E("        MOV	(SP)+, R3");
            E("        MOV	(SP)+, R2");
            E("        MOV	(SP)+, R1");
            E("        MOV	(SP)+, R0");
            E("        MOV	(SP)+, R5");
            E("        RTS	PC");
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

            // ── RTRAND: random(n) -> 0..n-1 (LFSR 16-бит) ───────
            E("; RTRAND — random(n): псевдослучайное 0..n-1.");
            E("; Вход: 2.(SP)=n. Выход: R0. LFSR x16+x14+x13+x11.");
            E("RTRAND:");
            E("        MOV	R1, -(SP)");
            E("        MOV	R2, -(SP)");
            E("        MOV	RNDSEED, R0");
            E("        MOV	R0, R1");
            E("        ASR	R1");
            E("        XOR	R0, R1");
            E("        ASR	R1");
            E("        ASR	R1");
            E("        XOR	R0, R1");
            E("        ASR	R1");
            E("        XOR	R0, R1");
            E("        BIC	#177776, R1");  // младший бит
            E("        ASL	R0");
            E("        BIS	R1, R0");
            E("        MOV	R0, RNDSEED");
            E("        BIC	#100000, R0");  // снять знак
            E("        MOV	6.(SP), R2");   // n");
            E("        BEQ	RTRNDX");       // n=0 -> 0");
            E("RTRNDM: CMP	R0, R2");
            E("        BLO	RTRNDX");
            E("        SUB	R2, R0");
            E("        BR	RTRNDM");
            E("RTRNDX: MOV	(SP)+, R2");
            E("        MOV	(SP)+, R1");
            E("        RTS	PC");
            E("");
            // ── Кадровое прерывание 50 Гц (вектор 100) ──────────
            // На ЦП УКНЦ нет регистра LTC @#177546 (Trap to 4!) —
            // vsync/getTimer работают через перехват вектора 100
            // с цепочкой к монитору (часы RT-11 продолжают идти).
            E("; VSSETUP — разовый перехват вектора 100.");
            E("VSSETUP:TST\tVSINIT");
            E("        BNE\tVSSET9");
            E("        MOV\t@#100, OLDV");      // старый обработчик
            E("        MOV\t#VSHND, @#100");    // наш обработчик (PSW @#102 не трогаем)
            E("        INC\tVSINIT");
            E("VSSET9: RTS\tPC");
            E("");
            E("; VSHND — обработчик кадра: флаг + счётчик, цепочка к монитору.");
            E("VSHND:  INC\tVSFLAG");
            E("        INC\tVSCNT");
            E("        JMP\t@OLDV");            // старый обработчик сделает RTI
            E("");
            E("; VSREST — восстановить вектор перед выходом (ОБЯЗАТЕЛЬНО).");
            E("VSREST: TST\tVSINIT");
            E("        BEQ\tVSRST9");
            E("        MOV\tOLDV, @#100");
            E("        CLR\tVSINIT");
            E("VSRST9: RTS\tPC");
            E("");
            E("; RTVSNC — vsync(): ждать следующий кадр (50 Гц).");
            E("RTVSNC: JSR\tPC, VSSETUP");
            E("        CLR\tVSFLAG");
            E("RTVSN1: TST\tVSFLAG");
            E("        BEQ\tRTVSN1");
            E("        RTS\tPC");
            E("");
            // ── RTSIN/RTCOS: sin256/cos256 — таблица в DATA ──────
            E("; RTSIN — sin256(a): синус, угол 0..255 = круг, рез. -256..256.");
            E("; Вход: 2.(SP)=угол. Выход: R0.");
            E("RTSIN:  MOV\tR1, -(SP)");
            E("        MOV\t4.(SP), R1");
            E("        BIC\t#177400, R1");     // угол & 255 (177400 окт = 0xFF00)
            E("        ASL\tR1");              // *2 (слово)
            E("        MOV\tSINTAB(R1), R0");
            E("        MOV\t(SP)+, R1");
            E("        RTS\tPC");
            E("");
            E("; RTCOS — cos256(a) = sin256(a+64).");
            E("RTCOS:  MOV\tR1, -(SP)");
            E("        MOV\t4.(SP), R1");
            E("        ADD\t#64., R1");
            E("        BIC\t#177400, R1");
            E("        ASL\tR1");
            E("        MOV\tSINTAB(R1), R0");
            E("        MOV\t(SP)+, R1");
            E("        RTS\tPC");
            E("");
            // ── RTGTIM: читать счётчик времени LTC ───────────────
            E("; RTGTIM — getTimer(): счётчик кадров 50 Гц (через вектор 100).");
            E("; Возвращает число кадров с первого вызова в R0.");
            E("RTGTIM: JSR\tPC, VSSETUP");
            E("        MOV\tVSCNT, R0");
            E("        RTS\tPC");
            E("");

            // ── RTPSTR: вывод строки ─────────────────────────────
            E("; RTPSTR — print_str(ptr): вывод строки байт за байтом, завершённой 0.");
            E("RTPSTR:");
            E("        MOV	R0, -(SP)");
            E("        MOV	R1, -(SP)");
            E("        MOV	6.(SP), R1");
            E("RTPST1: MOVB	(R1)+, R0");
            E("        BEQ	RTPST2");
            E("RTPST3: TSTB	@#177564");
            E("        BPL	RTPST3");
            E("        MOVB	R0, @#177566");
            E("        BR	RTPST1");
            E("RTPST2: MOV	(SP)+, R1");
            E("        MOV	(SP)+, R0");
            E("        RTS	PC");
            E("");
            // ── RTPRF: printf(fmt, ...) ───────────────────────
            E("; RTPRF — printf(fmt, arg1, arg2, ...)");
            E("; fmt — адрес строки формата (байты, 0-terminated)");
            E("; аргументы идут выше fmt в стеке (caller-cleans-up)");
            E("; %d = число (RTPNUM), %c = символ, %s = строка (RTPSTR),");
            E("; \\n = CR, все остальные байты выводятся как есть");
            E("RTPRF:");
            E("        MOV	R5, -(SP)");
            E("        MOV	SP, R5");
            E("        MOV	R0, -(SP)");
            E("        MOV	R1, -(SP)");
            E("        MOV	R2, -(SP)");
            E("        MOV	4.(R5), R1");   // R1 = адрес строки формата
            E("        MOV	#6., R2");       // R2 = смещение к первому аргументу (4+2=6)
            E("RTPRF1: MOVB	(R1)+, R0");
            E("        BIC	#177400, R0");
            E("        BEQ	RTPRFX");
            E("        CMP	R0, #'%");
            E("        BNE	RTPRFO");
            E("        MOVB	(R1)+, R0");
            E("        BIC	#177400, R0");
            E("        CMP	R0, #'d");
            E("        BNE	RTPRFC");
            E("        MOV	(R5), -(SP)");   // dummy R5 push
            E("        MOV	R2(R5), -(SP)"); // push arg
            E("        JSR	PC, RTPNUM");
            E("        ADD	#4., SP");
            E("        ADD	#2., R2");
            E("        BR	RTPRF1");
            E("RTPRFC: CMP	R0, #'c");
            E("        BNE	RTPRFS");
            E("        MOV	R2(R5), R0");
            E("RTPRFW: TSTB	@#177564");
            E("        BPL	RTPRFW");
            E("        MOVB	R0, @#177566");
            E("        ADD	#2., R2");
            E("        BR	RTPRF1");
            E("RTPRFS: CMP	R0, #'s");
            E("        BNE	RTPRFN");
            E("        MOV	R2(R5), -(SP)");
            E("        JSR	PC, RTPSTR");
            E("        ADD	#2., SP");
            E("        ADD	#2., R2");
            E("        BR	RTPRF1");
            E("RTPRFN: CMP	R0, #'n");
            E("        BNE	RTPRFO");
            E("        MOV	#13., R0");
            E("        BR	RTPRFW");
            E("RTPRFO: TSTB	@#177564");
            E("        BPL	RTPRFO");
            E("        MOVB	R0, @#177566");
            E("        BR	RTPRF1");
            E("RTPRFX: MOV	(SP)+, R2");
            E("        MOV	(SP)+, R1");
            E("        MOV	(SP)+, R0");
            E("        MOV	(SP)+, R5");
            E("        RTS	PC");
            E("");
            // ── RTWKEY: блокирующее чтение (ждёт клавишу) ───────
            // ── RTPCHR: вывод символа ────────────────────────────
            E("; RTPCHR — print_char(c): вход R0 = символ");
            E("RTPCHR:");
            E("RTPCHR1: TSTB	@#177564");
            E("        BPL	RTPCHR1");
            E("        MOVB	R0, @#177566");
            E("        RTS	PC");
            E("");
            // ── RTPUTC: вывод байта R0 в консоль (с ожиданием) ───
            E("; RTPUTC — вывести байт R0 в порт консоли.");
            E("RTPUTC:");
            E("RTPUTC1: TSTB	@#177564");
            E("        BPL	RTPUTC1");
            E("        MOVB	R0, @#177566");
            E("        RTS	PC");
            E("");
            // ── RTGOTO: gotoxy(x,y) через ESC Y (VT52) ───────────
            E("; RTGOTO — gotoxy(x,y): ESC Y y+32 x+32 (VT52: строка раньше колонки).");
            E("; 4.(R5)=x (колонка)  6.(R5)=y (строка)");
            E("RTGOTO:");
            E("        MOV	R5, -(SP)");
            E("        MOV	SP, R5");
            E("        MOV	R0, -(SP)");
            E("        MOV	#27., R0");      // ESC = 27 дес
            E("        JSR	PC, RTPUTC");
            E("        MOV	#89., R0");      // 'Y'
            E("        JSR	PC, RTPUTC");
            E("        MOV	6.(R5), R0");    // y (строка) — VT52 первым
            E("        ADD	#32., R0");
            E("        JSR	PC, RTPUTC");
            E("        MOV	4.(R5), R0");    // x (колонка) — вторым
            E("        ADD	#32., R0");
            E("        JSR	PC, RTPUTC");
            E("        MOV	(SP)+, R0");
            E("        MOV	(SP)+, R5");
            E("        RTS	PC");
            E("");
            // ── RTSCOL: setTextColor(c) через ESC 240 ────────────
            E("; RTSCOL — setTextColor(c): ESC 0240 (c+'0').");
            E("; 4.(R5)=цвет (цифра передаётся как c+48)");
            E("RTSCOL:");
            E("        MOV	R5, -(SP)");
            E("        MOV	SP, R5");
            E("        MOV	R0, -(SP)");
            E("        MOV	#27., R0");      // ESC
            E("        JSR	PC, RTPUTC");
            E("        MOV	#160., R0");     // 0240 окт = 160 дес
            E("        JSR	PC, RTPUTC");
            E("        MOV	4.(R5), R0");
            E("        ADD	#48., R0");      // c -> ASCII-цифра
            E("        JSR	PC, RTPUTC");
            E("        MOV	(SP)+, R0");
            E("        MOV	(SP)+, R5");
            E("        RTS	PC");
            E("");
            // ── RTPNL: перевод строки ────────────────────────────
            E("; RTPNL — print_nl(): вывод CR (13) + пауза для скроллинга.");
            E("RTPNL:");
            E("        MOV	R0, -(SP)");
            E("        MOV	R1, -(SP)");
            E("        MOV	#13., R0");      // CR
            E("RTPNL1: TSTB	@#177564");
            E("        BPL	RTPNL1");
            E("        MOVB	R0, @#177566");
            E("        MOV	#10., R0");      // LF (перевод строки)
            E("RTPNL3: TSTB	@#177564");
            E("        BPL	RTPNL3");
            E("        MOVB	R0, @#177566");
            E("        MOV	#2000., R1");  // пауза для скроллинга
            E("RTPNL2: SOB	R1, RTPNL2");
            E("        MOV	(SP)+, R1");
            E("        MOV	(SP)+, R0");
            E("        RTS	PC");
            E("");
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

            // ── RTFRCT: залитый прямоугольник (fill_rect) ────────
            E("; RTFRCT — fill_rect(x,y,w,h,color)");
            E(";   4.(R5)=x  6.(R5)=y  8.(R5)=w  10.(R5)=h  12.(R5)=color");
            E("; Клиппинг в прологе: x2=x+w, y2=y+h, обрезаем до 0..320/264");
            E("; Если w < 8: один цикл пикселями. Если w >= 8: слова + выступы.");
            E("RTFRCT:");
            E("        MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\tR0, -(SP)");
            E("        MOV\tR1, -(SP)");
            E("        MOV\tR2, -(SP)");
            E("        MOV\tR3, -(SP)");
            E("        MOV\tR4, -(SP)");

            // ── Клиппинг ─────────────────────────────────────────
            // R0=x R1=y R2=x2=x+w R3=y2=y+h
            E("        MOV\t4.(R5),  R0");           // R0 = x
            E("        MOV\t6.(R5),  R1");           // R1 = y
            E("        MOV\tR0, R2");
            E("        ADD\t8.(R5),  R2");           // R2 = x2 = x+w
            E("        MOV\tR1, R3");
            E("        ADD\t10.(R5), R3");           // R3 = y2 = y+h
            // if x < 0: x = 0
            E("        TST\tR0");
            E("        BPL\tRFCK1");
            E("        CLR\tR0");
            E("RFCK1:");
            // if y < 0: y = 0
            E("        TST\tR1");
            E("        BPL\tRFCK2");
            E("        CLR\tR1");
            E("RFCK2:");
            // if x2 > 320: x2 = 320
            E("        CMP\tR2, #320.");
            E("        BLE\tRFCK3");
            E("        MOV\t#320., R2");
            E("RFCK3:");
            // if y2 > 264: y2 = 264
            E("        CMP\tR3, #264.");
            E("        BLE\tRFCK4");
            E("        MOV\t#264., R3");
            E("RFCK4:");
            // if x2 <= x или y2 <= y → выход (полностью за экраном)
            E("        CMP\tR2, R0");
            E("        BLE\tRFCTEX");
            E("        CMP\tR3, R1");
            E("        BLE\tRFCTEX");
            // Вычисляем новые w и h
            E("        SUB\tR0, R2");                // R2 = w = x2-x
            E("        SUB\tR1, R3");                // R3 = h = y2-y
            // Сохраняем обрезанные значения на стек
            // Используем как локальные: cx cy cw ch
            E("        MOV\tR3, -(SP)");             // push ch (h обрезанное)
            E("        MOV\tR2, -(SP)");             // push cw (w обрезанное)
            E("        MOV\tR1, -(SP)");             // push cy (y обрезанное)
            E("        MOV\tR0, -(SP)");             // push cx (x обрезанное)
            // SP+0=cx SP+2=cy SP+4=cw SP+6=ch
            // Используем cx,cy,cw,ch вместо аргументов R5

            // Проверяем cw < 8  (R2=cw после клиппинга)
            E("        CMP\tR2, #10.");              // cw < 8?
            E("        BLT\tRFCTSM");

            // ── Вычисляем x_left, x_right, left_w, right_w, words ─
            // SP+0=cx SP+2=cy SP+4=cw SP+6=ch
            // R0=cx R1=cy R2=cw R3=ch (из клиппинга)
            // x2 = cx + cw
            E("        MOV\tR0, R2");                // R2 = cx
            E("        ADD\t4.(SP), R2");            // R2 = cx+cw

            // x_left = (cx+7) & ~7
            E("        MOV\tR0, R1");
            E("        ADD\t#7., R1");
            E("        BIC\t#7, R1");                // R1 = x_left

            // x_right = (cx+cw) & ~7
            E("        MOV\tR2, R3");
            E("        BIC\t#7, R3");                // R3 = x_right

            // left_w = x_left - cx
            E("        MOV\tR1, R4");
            E("        SUB\tR0, R4");                // R4 = left_w

            // right_w = (cx+cw) - x_right
            E("        MOV\tR2, R0");
            E("        SUB\tR3, R0");                // R0 = right_w

            // words = (x_right - x_left) >> 3
            E("        MOV\tR3, R2");
            E("        SUB\tR1, R2");
            E("        ASR\tR2");
            E("        ASR\tR2");
            E("        ASR\tR2");                    // R2 = words

            // Пушим: h x_left x_right left_w right_w words
            // SP сейчас: cx cy cw ch (4 слова = 8 байт)
            E("        MOV\t6.(SP), -(SP)");         // push ch = h  → SP+10=h
            E("        MOV\tR1, -(SP)");             // SP+8 = x_left
            E("        MOV\tR3, -(SP)");             // SP+6 = x_right
            E("        MOV\tR4, -(SP)");             // SP+4 = left_w
            E("        MOV\tR0, -(SP)");             // SP+2 = right_w
            E("        MOV\tR2, -(SP)");             // SP+0 = words
            // Итого на стеке: words right_w left_w x_right x_left h cx cy cw ch

            // Слово цвета → R4
            E("        MOV\t12.(R5), R4");           // color (R5 = FP не изменился)
            E("        ASL\tR4");
            E("        MOV\tFCTAB(R4), R4");

            // cy*2 → R1  (cy = SP+14)
            E("        MOV\t14.(SP), R1");           // cy
            E("        ASL\tR1");                    // R1 = cy*2

            // ══ Цикл по строкам ══════════════════════════════════
            E("RFCTY:");

            // 1. Центр словами
            E("        TST\t0.(SP)");                // words == 0?
            E("        BEQ\tRFCTL0");
            E("        MOV\tDSPST(R1), R3");         // начало строки VRAM
            E("        MOV\t8.(SP), R0");            // x_left
            E("        ASR\tR0");
            E("        ASR\tR0");
            E("        ASR\tR0");                    // x_left/8 = номер слова
            E("        ADD\tR0, R3");                // адрес первого слова
            E("        MOV\t0.(SP), R0");            // words
            E("RFCTW:");
            E("        MOV\tR3, @#176640");
            E("        MOV\tR4, @#176642");
            E("        INC\tR3");
            E("        DEC\tR0");
            E("        BNE\tRFCTW");

            // 2. Левый выступ: left_w пикселей от cx
            E("RFCTL0:");
            E("        TST\t4.(SP)");                // left_w == 0?
            E("        BEQ\tRFCTR0");
            E("        MOV\t12.(SP), R0");           // R0 = cx
            E("        MOV\tR1, R2");
            E("        ASR\tR2");                    // R2 = y
            E("        MOV\t4.(SP), R3");            // R3 = left_w
            E("RFCTL:");
            E("        MOV\tR1, -(SP)");
            E("        MOV\t12.(R5), -(SP)");        // color
            E("        MOV\tR2, -(SP)");
            E("        MOV\tR0, -(SP)");
            E("        JSR\tPC, RTPPNT");
            E("        ADD\t#6., SP");
            E("        MOV\t(SP)+, R1");
            E("        INC\tR0");
            E("        DEC\tR3");
            E("        BNE\tRFCTL");

            // 3. Правый выступ: right_w пикселей от x_right
            E("RFCTR0:");
            E("        TST\t2.(SP)");                // right_w == 0?
            E("        BEQ\tRFCTN");
            E("        MOV\t6.(SP), R0");            // R0 = x_right
            E("        MOV\tR1, R2");
            E("        ASR\tR2");
            E("        MOV\t2.(SP), R3");            // R3 = right_w
            E("RFCTRP:");
            E("        MOV\tR1, -(SP)");
            E("        MOV\t12.(R5), -(SP)");        // color
            E("        MOV\tR2, -(SP)");
            E("        MOV\tR0, -(SP)");
            E("        JSR\tPC, RTPPNT");
            E("        ADD\t#6., SP");
            E("        MOV\t(SP)+, R1");
            E("        INC\tR0");
            E("        DEC\tR3");
            E("        BNE\tRFCTRP");

            // Следующая строка
            E("RFCTN:");
            E("        ADD\t#2., R1");               // y*2 += 2
            E("        DEC\t10.(SP)");               // h--
            E("        BNE\tRFCTY");
            E("        ADD\t#20., SP");              // убрать words..h + cx cy cw ch
            E("        BR\tRFCTEX");

            // ── Малый путь: cw < 8 — один цикл пикселей ─────────
            E("RFCTSM:");
            // SP: cx cy cw ch
            E("        MOV\t2.(SP), R1");            // R1 = cy
            E("        ASL\tR1");                    // R1 = cy*2
            E("        MOV\t6.(SP), R3");            // R3 = ch (счётчик строк)
            E("RFCSMY:");
            E("        MOV\t0.(SP), R0");            // R0 = px = cx
            E("        MOV\tR1, R2");
            E("        ASR\tR2");                    // R2 = y
            E("        MOV\t4.(SP), R4");            // R4 = cw (счётчик пикселей)
            E("RFCSMX:");
            E("        MOV\tR1, -(SP)");
            E("        MOV\tR3, -(SP)");
            E("        MOV\t12.(R5), -(SP)");        // color
            E("        MOV\tR2, -(SP)");
            E("        MOV\tR0, -(SP)");
            E("        JSR\tPC, RTPPNT");
            E("        ADD\t#6., SP");
            E("        MOV\t(SP)+, R3");
            E("        MOV\t(SP)+, R1");
            E("        INC\tR0");
            E("        DEC\tR4");
            E("        BNE\tRFCSMX");
            E("        ADD\t#2., R1");
            E("        DEC\tR3");
            E("        BNE\tRFCSMY");
            E("        ADD\t#8., SP");               // убрать cx cy cw ch

            // ── Общий эпилог ─────────────────────────────────────
            E("RFCTEX:");
            E("        MOV\t(SP)+, R4");
            E("        MOV\t(SP)+, R3");
            E("        MOV\t(SP)+, R2");
            E("        MOV\t(SP)+, R1");
            E("        MOV\t(SP)+, R0");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            // ── RTGRAD: градиентная заливка ───────────────────────
            E("; RTGRAD — fill_gradient(x,y,w,h,fg,bg,dir)");
            E(";   4.(R5)=x 6.(R5)=y 8.(R5)=w 10.(R5)=h");
            E(";   12.(R5)=fg 14.(R5)=bg 16.(R5)=dir");
            E("; dir: 0=лево→право 1=право→лево 2=верх→низ 3=низ→верх");
            E("; Точка входа — выбор подпрограммы по dir");
            E("; Подпрограммы: RGGRLR RGGRRL RGGRTB RGGRBT");
            E("; Каждая пишет прямо в VRAM через RTDITH (8 вызовов)");
            E("RTGRAD:");
            E("        MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\tR0, -(SP)");
            E("        MOV\tR1, -(SP)");
            E("        MOV\tR2, -(SP)");
            E("        MOV\tR3, -(SP)");
            E("        MOV\tR4, -(SP)");
            E("        MOV\t16.(R5), R0");           // dir
            E("        BEQ\tRGGRLR");                // 0 = лево→право
            E("        CMP\tR0, #1.");
            E("        BEQ\tRGGRRL");                // 1 = право→лево
            E("        CMP\tR0, #2.");
            E("        BEQ\tRGGRTB");                // 2 = верх→низ
            E("        BR\tRGGRBT");                 // 3 = низ→верх

            // ── Общий эпилог ─────────────────────────────────────
            E("RGRADEX:");
            E("        MOV\t(SP)+, R4");
            E("        MOV\t(SP)+, R3");
            E("        MOV\t(SP)+, R2");
            E("        MOV\t(SP)+, R1");
            E("        MOV\t(SP)+, R0");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            E("");

            // ── Вспомогательный макрос: вызов RTDITH ─────────────
            // Вызывает RTDITH(cx, cy, pw, ph, pat, fg, bg)
            // Аргументы: R0=cx R1=cy R2=pw R3=ph R4=pat
            // fg=12.(R5) bg=14.(R5) — из фрейма родителя
            E("; RGCALL — вызов RTDITH с R0=x R1=y R2=w R3=h R4=pat");
            E("; fg=12.(R5) bg=14.(R5)");
            E("RGCALL:");
            E("        MOV\t14.(R5), -(SP)");        // bg
            E("        MOV\t12.(R5), -(SP)");        // fg
            E("        MOV\tR4, -(SP)");             // pat
            E("        MOV\tR3, -(SP)");             // h
            E("        MOV\tR2, -(SP)");             // w
            E("        MOV\tR1, -(SP)");             // y
            E("        MOV\tR0, -(SP)");             // x
            E("        JSR\tPC, RTDITH");
            E("        ADD\t#14., SP");
            E("        RTS\tPC");
            E("");

            // ── RGGRLR: лево→право ────────────────────────────────
            // 8 вертикальных полос шириной ~w/8
            // pat=0 слева, pat=7 справа
            // base=w>>3, rem=w&7, Брезенхэм для остатка
            E("RGGRLR:");
            E("        MOV\t8.(R5), R2");            // w
            E("        MOV\tR2, R3");
            E("        ASR\tR3"); E("        ASR\tR3"); E("        ASR\tR3"); // R3=base=w>>3
            E("        BIC\t#177770, R2");           // R2=rem=w&7
            // Локали на стеке: cx base rem accum
            E("        MOV\t4.(R5), -(SP)");         // SP+6=cx=x
            E("        MOV\tR3, -(SP)");             // SP+4=base
            E("        MOV\tR2, -(SP)");             // SP+2=rem
            E("        CLR\t-(SP)");                 // SP+0=accum=0
            E("        CLR\tR4");                    // R4=pat=0
            E("        MOV\t#10., R1");              // R1=8 (счётчик)
            E("RGLRL:");
            // pw = base; accum+=rem; if accum>=8: pw++; accum-=8
            E("        MOV\t4.(SP), R2");            // R2=base=pw
            E("        ADD\t2.(SP), 0.(SP)");        // accum+=rem
            E("        CMP\t0.(SP), #10.");
            E("        BLT\tRGLR1");
            E("        INC\tR2");
            E("        SUB\t#10., 0.(SP)");
            E("RGLR1:");
            // вызов: cx=6.(SP) y=6.(R5) w=R2 h=10.(R5) pat=R4
            E("        MOV\t6.(SP), R0");            // cx
            E("        MOV\t6.(R5), R1");            // y
            E("        MOV\t10.(R5), R3");           // h
            E("        JSR\tPC, RGCALL");
            // cx += pw
            E("        ADD\tR2, 6.(SP)");
            E("        INC\tR4");                    // pat++
            E("        DEC\tR1");                    // счётчик--  (R1 портится RGCALL? нет — RGCALL сохраняет регистры через RTDITH)
            E("        BNE\tRGLRL");
            E("        ADD\t#8., SP");               // убрать cx base rem accum
            E("        BR\tRGRADEX");

            // ── RGGRRL: право→лево ────────────────────────────────
            // pat=0 справа, pat=7 слева
            // cx начинается с x+w, идёт влево
            E("RGGRRL:");
            E("        MOV\t8.(R5), R2");
            E("        MOV\tR2, R3");
            E("        ASR\tR3"); E("        ASR\tR3"); E("        ASR\tR3");
            E("        BIC\t#177770, R2");
            // cx = x+w (начнём с правого края, будем вычитать pw)
            E("        MOV\t4.(R5), R0");
            E("        ADD\t8.(R5), R0");            // R0=x+w
            E("        MOV\tR0, -(SP)");             // SP+6=cx=x+w
            E("        MOV\tR3, -(SP)");
            E("        MOV\tR2, -(SP)");
            E("        CLR\t-(SP)");
            E("        CLR\tR4");
            E("        MOV\t#10., R1");
            E("RGRLL:");
            E("        MOV\t4.(SP), R2");
            E("        ADD\t2.(SP), 0.(SP)");
            E("        CMP\t0.(SP), #10.");
            E("        BLT\tRGRL1");
            E("        INC\tR2");
            E("        SUB\t#10., 0.(SP)");
            E("RGRL1:");
            // cx -= pw (рисуем справа налево)
            E("        SUB\tR2, 6.(SP)");
            E("        MOV\t6.(SP), R0");            // cx (уже вычтено)
            E("        MOV\t6.(R5), R1");
            E("        MOV\t10.(R5), R3");
            E("        JSR\tPC, RGCALL");
            E("        INC\tR4");
            E("        DEC\tR1");
            E("        BNE\tRGRLL");
            E("        ADD\t#8., SP");
            E("        BR\tRGRADEX");

            // ── RGGRTB: верх→низ ──────────────────────────────────
            // 8 горизонтальных полос высотой ~h/8
            // pat=0 сверху, pat=7 снизу
            E("RGGRTB:");
            E("        MOV\t10.(R5), R2");           // h
            E("        MOV\tR2, R3");
            E("        ASR\tR3"); E("        ASR\tR3"); E("        ASR\tR3");
            E("        BIC\t#177770, R2");
            E("        MOV\t6.(R5), -(SP)");         // SP+6=cy=y
            E("        MOV\tR3, -(SP)");
            E("        MOV\tR2, -(SP)");
            E("        CLR\t-(SP)");
            E("        CLR\tR4");
            E("        MOV\t#10., R1");
            E("RGTBL:");
            E("        MOV\t4.(SP), R3");            // R3=base=ph
            E("        ADD\t2.(SP), 0.(SP)");
            E("        CMP\t0.(SP), #10.");
            E("        BLT\tRGTB1");
            E("        INC\tR3");
            E("        SUB\t#10., 0.(SP)");
            E("RGTB1:");
            E("        MOV\t4.(R5), R0");            // x
            E("        MOV\t6.(SP), R1");            // cy
            E("        MOV\t8.(R5), R2");            // w
            E("        JSR\tPC, RGCALL");
            E("        ADD\tR3, 6.(SP)");            // cy += ph
            E("        INC\tR4");
            E("        DEC\tR1");
            E("        BNE\tRGTBL");
            E("        ADD\t#8., SP");
            E("        BR\tRGRADEX");

            // ── RGGRBT: низ→верх ──────────────────────────────────
            // pat=0 снизу, pat=7 сверху
            E("RGGRBT:");
            E("        MOV\t10.(R5), R2");
            E("        MOV\tR2, R3");
            E("        ASR\tR3"); E("        ASR\tR3"); E("        ASR\tR3");
            E("        BIC\t#177770, R2");
            // cy = y+h (начинаем снизу)
            E("        MOV\t6.(R5), R0");
            E("        ADD\t10.(R5), R0");
            E("        MOV\tR0, -(SP)");             // SP+6=cy=y+h
            E("        MOV\tR3, -(SP)");
            E("        MOV\tR2, -(SP)");
            E("        CLR\t-(SP)");
            E("        CLR\tR4");
            E("        MOV\t#10., R1");
            E("RGBTL:");
            E("        MOV\t4.(SP), R3");
            E("        ADD\t2.(SP), 0.(SP)");
            E("        CMP\t0.(SP), #10.");
            E("        BLT\tRGBT1");
            E("        INC\tR3");
            E("        SUB\t#10., 0.(SP)");
            E("RGBT1:");
            E("        SUB\tR3, 6.(SP)");            // cy -= ph
            E("        MOV\t4.(R5), R0");
            E("        MOV\t6.(SP), R1");            // cy
            E("        MOV\t8.(R5), R2");
            E("        JSR\tPC, RGCALL");
            E("        INC\tR4");
            E("        DEC\tR1");
            E("        BNE\tRGBTL");
            E("        ADD\t#8., SP");
            E("        BR\tRGRADEX");
            E("");

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
            E("; FCTAB: слово цвета для fill_rect (8 пикселей одного цвета)");
            E("; цвет 0=0x0000  1=0x00FF  2=0xFF00  3=0xFFFF");
            E("FCTAB:  .WORD\t0, 377, 177400, 177777");
            E("; DTAB: 8 паттернов дизеринга, каждый 8 строк по 1 слову (оба плана одинаковы)");
            E("; Паттерн p: 8 слов DTAB[p*8+row], row=0..7");
            E("; Плотность: 0=0% 1=12% 2=25% 3=37% 4=50% 5=62% 6=75% 7=100%");
            E("; Каждое слово = старший байт план1 + младший байт план2 (одинаковые)");
            E("; Байт паттерна: биты = какие пиксели закрашены в строке");
            E("; p0: 00000000 — пусто");
            E("; p1: 10000000 10000000 ... — 1 из 8");
            E("; p2: 10001000 ... — 2 из 8");
            E("; p3: 10010100 ... — 3 из 8");
            E("; p4: 10101010 ... — 4 из 8 шахматка");
            E("; p5: 11010101 ... — 5 из 8");
            E("; p6: 11101110 ... — 6 из 8");
            E("; p7: 11111111 — полный");
            E("; Формат слова: оба байта одинаковы = паттерн виден в обоих планах");
            E("DTAB:");
            // p0: 0% — пусто
            E("        .WORD\t0,0,0,0,0,0,0,0");
            // p1: 12% — диагональ редкая
            E("        .WORD\t100200,10020,1002,40100,4010,401,20040,2004");
            // p2: 25% — редкая сетка
            E("        .WORD\t104210,0,21042,0,104210,0,21042,0");
            // p3: 37%
            E("        .WORD\t104210,42104,21042,10421,104210,42104,21042,10421");
            // p4: 50% — шахматка
            E("        .WORD\t125252,52525,125252,52525,125252,52525,125252,52525");
            // p5: 62%
            E("        .WORD\t73567,135673,156735,167356,73567,135673,156735,167356");
            // p6: 75%
            E("        .WORD\t73567,177777,156735,177777,73567,177777,156735,177777");
            // p7: 100% — полный
            E("        .WORD\t177777,177777,177777,177777,177777,177777,177777,177777");
            E("XWRD:   .BLKW\t640.");
            E("CM0:    .BLKW\t640.");
            E("CM1:    .BLKW\t640.");
            E("CM2:    .BLKW\t640.");
            E("CM3:    .BLKW\t640.");
            E("RNDSEED: .WORD\t12345.");          // зерно генератора случайных
            E("VSINIT: .WORD\t0");                // вектор 100 перехвачен?
            E("VSFLAG: .WORD\t0");                // флаг кадра (vsync)
            E("VSCNT:  .WORD\t0");                // счётчик кадров (getTimer)
            E("OLDV:   .WORD\t0");                // старый обработчик вектора 100
            // Таблица синусов для sin256/cos256: 256 слов, масштаб 256
            E("        .EVEN");                  // страховка выравнивания (.WORD требует чётный адрес)
            E("; SINTAB — sin(2*pi*i/256)*256, i=0..255");
            E("SINTAB:");
            for (int si = 0; si < 256; si += 8)
            {
                var vals = new System.Collections.Generic.List<string>();
                for (int sj = si; sj < si + 8; sj++)
                    vals.Add(((int)Math.Round(256.0 * Math.Sin(2.0 * Math.PI * sj / 256.0))).ToString() + ".");
                E("        .WORD\t" + string.Join(", ", vals));
            }
            E("SPBUF:  .BLKW\t15.");           // буфер строки спрайта (14 слов + хвост)
            E("        .EVEN");
            E("        .PSECT\tCODE, RO, I");
            E("");
        }
    }
}
