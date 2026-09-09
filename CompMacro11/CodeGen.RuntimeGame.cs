using System;
using System.Collections.Generic;
using System.Text;

namespace CompMacro11
{
    // Рантайм-библиотека Mini-C → Macro-11 (вынесено из CodeGen.cs)
    public partial class CodeGen
    {
        private void EmitRuntimeGame()
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
            E("        JSR\tPC, RTNOCUR");          // погасить курсор аппаратно
            // многоцветный режим: если резидент ПП запущен — очистить план 0
            E("        TST\tPPON");                 // резидент ПП активен?
            E("        BEQ\tRTCLS9");               // нет — обычный cls
            E("        MOV\t#4, PPCMD2");           // команда 4: очистить план 0
            E("RTCLS8: TST\tPPCMD2");               // ждать подтверждения ПП
            E("        BNE\tRTCLS8");
            E("RTCLS9: MOV\t(SP)+, R1");
            E("        RTS\tPC");
            E("");

            // ── RTNOCUR — выключить курсор через ячейки монитора ──
            //   ESC 0247 только перекрашивает курсор в цвет фона, поэтому он
            //   продолжает мигать поверх графики. Здесь курсор гасится
            //   по-настоящему: запрет в 23164, затем сброс младшего бита в
            //   полях блока, адрес которого лежит в 60(R5) при R5 из 23150.
            //   Все адреса ВОСЬМЕРИЧНЫЕ.
            E("RTNOCUR:");
            E("        MOV\tR0, -(SP)");
            E("        MOV\tR5, -(SP)");
            E("        CLR\t@#23164");             // запретить курсор
            E("        MOV\t#2, @#7134");
            E("        MOV\t@#23150, R5");
            E("        MOV\t60(R5), R0");          // блок параметров курсора
            E("        BIC\t#1, 6(R0)");
            E("        BIC\t#1, 52(R0)");
            E("        MOV\t(SP)+, R5");
            E("        MOV\t(SP)+, R0");
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
            E("        .BYTE\t33,247,60");   // погасить курсор (цвет = фон)
            E("        .BYTE\t14,0");
            E("        .EVEN");
            E("");

            // SCR1: 80 колонок, палитра 1
            E("RTCSC1:");
            E("        .BYTE\t33,246,61");   // формат экрана 80x24
            E("        .BYTE\t33,240,63");   // цвет символа
            E("        .BYTE\t33,241,60");   // цвет знакоместа 0
            E("        .BYTE\t33,242,60");   // цвет фона 0
            E("        .BYTE\t33,247,60");   // погасить курсор
            E("        .BYTE\t14,0");        // clear screen
            E("        .EVEN");
            E("");

            // SCR2: 40 колонок, палитра 2
            E("RTCSC2:");
            E("        .BYTE\t33,246,62");
            E("        .BYTE\t33,240,67");
            E("        .BYTE\t33,241,61");
            E("        .BYTE\t33,242,61");
            E("        .BYTE\t33,247,61");   // цвет курсора = фон (синий)
            E("        .BYTE\t14,0");
            E("        .EVEN");
            E("");

            // SCR3: 80 колонок, палитра 2
            E("RTCSC3:");
            E("        .BYTE\t33,246,61");
            E("        .BYTE\t33,240,67");
            E("        .BYTE\t33,241,61");
            E("        .BYTE\t33,242,61");
            E("        .BYTE\t33,247,61");   // цвет курсора = фон (синий)
            E("        .BYTE\t14,0");
            E("        .EVEN");
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
            E("        JMP	RTSPX");          // JMP вместо BR — надёжнее по дистанции
            // ── БУФЕРНЫЙ ПУТЬ: x не кратен 8 ─────────────────────
            // R1=s, R0=x, R2=words, R3=h, R4=ptr, R5=начало строки.
            // Строка копируется в SPBUF, сдвигается на s бит через ROLB,
            // выводится w+1 слов. Крайние слова дают чёрную окантовку —
            // известное поведение (некратный x), но спрайт рисуется.
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
            E("        INC	R1");            // R1 = w+1
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
            // ── RTPCOL: setPlaceColor(c) через ESC 241 (ГРАФ-A) ──
            E("; RTPCOL — setPlaceColor(c): ESC 0241 (c+'0'). Цвет знакоместа 0..7.");
            E("; Применяется глобально (атрибут поля). 4.(R5)=цвет.");
            E("RTPCOL:");
            E("        MOV	R5, -(SP)");
            E("        MOV	SP, R5");
            E("        MOV	R0, -(SP)");
            E("        MOV	#27., R0");
            E("        JSR	PC, RTPUTC");
            E("        MOV	#161., R0");
            E("        JSR	PC, RTPUTC");
            E("        MOV	4.(R5), R0");
            E("        ADD	#48., R0");
            E("        JSR	PC, RTPUTC");
            E("        MOV	(SP)+, R0");
            E("        MOV	(SP)+, R5");
            E("        RTS	PC");
            E("");
            // ── RTCCOL: setCursorColor(c) через ESC 247 ──────────
            E("; RTCCOL — setCursorColor(c): ESC 0247 (c+'0'). Цвет курсора 0..7.");
            E("; 4.(R5)=цвет (цифра передаётся как c+48)");
            E("RTCCOL:");
            E("        MOV	R5, -(SP)");
            E("        MOV	SP, R5");
            E("        MOV	R0, -(SP)");
            E("        MOV	#27., R0");      // ESC
            E("        JSR	PC, RTPUTC");
            E("        MOV	#167., R0");     // 0247 окт = 167 дес — цвет курсора
            E("        JSR	PC, RTPUTC");
            E("        MOV	4.(R5), R0");
            E("        ADD	#48., R0");      // c -> ASCII-цифра
            E("        JSR	PC, RTPUTC");
            E("        MOV	(SP)+, R0");
            E("        MOV	(SP)+, R5");
            E("        RTS	PC");
            E("");
            // ── RTPNL — print_nl(): CR + LF + пауза для скроллинга ──
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
            E("        TST\tPPON");           // резидент ПП занял клавиатуру?
            E("        BNE\tRTWKP");
            E("        .TTINR");
            E("        BCS\tRTWKEY");
            E("        BIC\t#177600, R0");    // убрать parity bit
            E("        RTS\tPC");
            E("RTWKP:  TST\tPPKEY");          // ждать код от резидента
            E("        BEQ\tRTWKP");
            E("        JSR\tPC, RTKTAK");
            E("        RTS\tPC");
            E("");

            // ── RTGKEY: неблокирующее чтение (getkey) ────────────
            E("; RTGKEY / getkey() — однократная попытка читать.");
            E("; При активном резиденте ПП штатный ввод монитора молчит:");
            E("; клавиатуру обслуживает сам резидент и кладёт код в PPKEY.");
            E("RTGKEY:");
            E("        TST\tPPON");
            E("        BNE\tRTGKP");
            E("        .TTINR");
            E("        BCS\tRTGK1");
            E("        BIC\t#177600, R0");    // убрать parity bit
            E("        RTS\tPC");
            E("RTGK1:  CLR\tR0");
            E("        RTS\tPC");
            E("RTGKP:  TST\tPPKEY");
            E("        BNE\tRTGKN");             // пришло новое нажатие
            // Клавиша всё ещё удерживается — автоповтор. Отсчёт ведётся в
            // ОПРОСАХ getkey(), а не в кадрах: игра и так опрашивает раз за
            // кадр, зато нет зависимости от перехвата вектора 100.
            E("        TST\tPPKHLD");
            E("        BEQ\tRTGKC");             // отпущена → сбросить состояние
            E("        MOV\tPPKHLD, R0");
            E("        CMP\tR0, KBLAST");
            E("        BNE\tRTGKS");             // зажали, а события нажатия не было
            E("        DEC\tKBCNT");
            E("        BGT\tRTGK1");             // до повтора ещё не дошло
            E("        MOV\tKBRATE, KBCNT");     // взвести на следующий повтор
            E("        MOV\tKBLAST, R0");
            E("        JSR\tPC, RTKMAP");        // перевести скан-код
            E("        RTS\tPC");
            // Клавишу держат, но нажатие мы пропустили — подхватываем её.
            E("RTGKS:  MOV\tPPKHLD, R0");
            E("        MOV\tR0, KBLAST");
            E("        MOV\tKBDLY, KBCNT");
            E("        JSR\tPC, RTKMAP");
            E("        RTS\tPC");
            E("RTGKC:  CLR\tKBLAST");
            E("        CLR\tR0");
            E("        RTS\tPC");
            E("RTGKN:  MOV\tPPKEY, R0");
            E("        MOV\tR0, KBLAST");        // запомнить для автоповтора
            E("        MOV\tKBDLY, KBCNT");      // пауза перед первым повтором
            E("        JSR\tPC, RTKTAK");
            E("        RTS\tPC");
            E("");

            // ── RTKTAK: забрать код из PPKEY и перевести по KBMAP ──
            E("; RTKTAK — взять скан-код из PPKEY, очистить ячейку и");
            E("; заменить на привычный код по таблице KBMAP. Клавиши, которых");
            E("; в таблице нет, возвращаются своим скан-кодом.");
            E("RTKTAK:");
            E("        MOV\tPPKEY, R0");
            E("        CLR\tPPKEY");
            // дальше — перевод скан-кода, он же нужен автоповтору отдельно
            E("RTKMAP:");
            E("        MOV\tR1, -(SP)");
            E("        MOV\t#KBMAP, R1");
            E("RTKTA1: TST\t(R1)");
            E("        BEQ\tRTKTA3");          // конец таблицы — отдать как есть
            E("        CMP\tR0, (R1)");
            E("        BEQ\tRTKTA2");
            E("        ADD\t#4., R1");
            E("        BR\tRTKTA1");
            E("RTKTA2: MOV\t2.(R1), R0");
            E("RTKTA3: MOV\t(SP)+, R1");
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

            // ── RTPPINIT — ppu_init(): загрузить и запустить ПП-движок ──
            //   Заливает экран 8 цветами (палитра) через ПП — 3 плана.
            //   Механизм загрузки по образцу рабочего кода nzeemin (Asteroids):
            //   массив параметров, канал 2 (176674/176676), команды
            //   выделить(1)/записать(20)/пуск(30).
            E("; RTPPINIT — ppu_init(): загрузить ПП-движок и залить экран.");
            E("RTPPINIT:");
            E("        MOV\tR5, -(SP)");
            E("        MTPS\t#340");              // запрет прерываний ЦП (как nzeemin)
            // При allocate в PPACP2 должна быть ДЛИНА (как у nzeemin),
            // адрес кода кладётся в PPACP2 только перед copy (команда 20).
            E("        MOV\t#<PPEND2-PPCODE>/2, PPACP2"); // длина для allocate
            E("        MOV\t#<PPEND2-PPCODE>/2, PPLEN2");  // длина
            E("        MOVB\t#1, PPCMD2");        // выделить
            E("        JSR\tPC, PPSEN2");
            E("        MOV\tPPAPP2, PPADR2");     // запомнить адрес ПП
            E("        MOVB\t#20, PPCMD2");       // записать ЦП->ПП
            E("        MOV\t#PPCODE, PPACP2");    // ТЕПЕРЬ адрес кода ЦП
            E("        JSR\tPC, PPSEN2");
            E("        MOVB\t#30, PPCMD2");       // пуск
            E("        MOV\tPPADR2, PPAPP2");     // восстановить адрес ПП (точка входа!)
            E("        JSR\tPC, PPSEN2");
            E("        CLR\tR0");
            E("RTPPI1: SOB\tR0, RTPPI1");         // пауза, дать ПП отработать
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            E("");
            // передача массива параметров по каналу 2 (5 слов)
            E("PPSEN2: MOV\t#PPMSG2, R2");
            E("        MOV\t#5, R3");
            E("        BR\tPPSE22");
            E("PPSE21: MOVB\t(R2)+, @#176676");
            E("PPSE22: TSTB\t@#176674");
            E("        BPL\tPPSE22");
            E("        SOB\tR3, PPSE21");
            E("        RTS\tPC");
            E("");
            // ПП-код: ПАЛИТРА — 8 широких вертикальных полос цветов 0..7.
            // Каждая зона 10 октетов (80 точек), свой цвет через 177016 + MOVB #377.
            // Метод Худякова: рисуем в готовый экран (init/cls), RTS PC в конце.
            // Геометрия: 80 октетов/строку, старт 100000, 264 строки.
            E("PPCODE:");
            E("        MOV\t#100000, R2");
            E("        MOV\t#264., R0");
            E("PPROW:  MOV\tR2, @#177010");
            E("        CLR\tR4");
            E("PPZONE: MOV\tR4, @#177016");
            E("        MOV\t#10., R3");
            E("PPOCT:  MOVB\t#377, @#177024");
            E("        INC\t@#177010");
            E("        SOB\tR3, PPOCT");
            E("        INC\tR4");
            E("        CMP\tR4, #8.");
            E("        BLT\tPPZONE");
            E("        ADD\t#80., R2");
            E("        SOB\tR0, PPROW");
            E("        RTS\tPC");
            E("PPEND2:");
            E("");

            // ════════════════════════════════════════════════════════════
            //  РЕЗИДЕНТНЫЙ ПП: pp_init / pp_point / pp_stop
            //  Один раз загружаем резидент на ПП (pp_init), он крутится в
            //  цикле и слушает команды через общую память (PPCMD2/PPPX/PPPY/
            //  PPPC). Связь как у nzeemin: ПП читает переменные ЦП косвенно
            //  через 177010 (адрес/2) → 177014.
            //  Протокол PPCMD2:  0=ждать  1=точка  177777=стоп
            // ════════════════════════════════════════════════════════════
            // ── RTPPRES — pp_init(): загрузить резидент и запустить ──
            E("; RTPPRES — pp_init(): загрузить резидентный ПП-движок и запустить.");
            E("RTPPRES:");
            E("        MOV\tR5, -(SP)");
            E("        MTPS\t#340");              // запрет прерываний ЦП
            E("        CLR\tPPCMD2");             // команда = ждать
            // обнулить указатели очереди спрайтов (head=tail=0 → пусто)
            E("        CLR\tSQHEAD");
            E("        CLR\tSQTAIL");
            E("        MOV\t#<PPREND-PPRES>/2, PPACP2"); // длина для allocate
            E("        MOV\t#<PPREND-PPRES>/2, PPLEN2");
            E("        MOVB\t#1, PPCMD2B");        // выделить
            E("        JSR\tPC, PPSEN2");
            E("        MOV\tPPAPP2, PPADR2");     // запомнить адрес ПП
            E("        MOVB\t#20, PPCMD2B");       // записать ЦП->ПП
            E("        MOV\t#PPRES, PPACP2");     // адрес резидента ЦП
            E("        JSR\tPC, PPSEN2");
            E("        MOVB\t#30, PPCMD2B");       // пуск
            E("        MOV\tPPADR2, PPAPP2");     // точка входа
            E("        JSR\tPC, PPSEN2");
            E("        MTPS\t#0");                // вернуть прерывания ЦП
            E("        MOV\t#1, PPON");            // флаг: резидент ПП активен
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            E("");
            // ── RTPPPT — pp_point(x,y,c): послать команду точки резиденту ──
            E("; RTPPPT — pp_point(x,y,c): отправить команду точки резиденту.");
            E("RTPPPT:");
            E("        MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\t4.(R5), PPPX");        // x
            E("        MOV\t6.(R5), PPPY");        // y
            E("        MOV\t8.(R5), PPPC");        // цвет
            E("        MOV\t#1, PPCMD2");          // команда = точка
            E("RTPPW:  TST\tPPCMD2");              // ждать пока ПП обнулит
            E("        BNE\tRTPPW");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            E("");
            // ── RTPPLN — pp_line(x0,y0,x1,y1,c): послать команду линии ──
            E("; RTPPLN — pp_line(x0,y0,x1,y1,c): отправить команду линии резиденту.");
            E("RTPPLN:");
            E("        MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\t4.(R5), PPPX");        // x0
            E("        MOV\t6.(R5), PPPY");        // y0
            E("        MOV\t8.(R5), PPLX1");       // x1
            E("        MOV\t10.(R5), PPLY1");      // y1
            E("        MOV\t12.(R5), PPPC");       // цвет
            E("        MOV\t#2, PPCMD2");          // команда = линия
            E("RTPPLW: TST\tPPCMD2");              // ждать обнуления
            E("        BNE\tRTPPLW");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            E("");
            // ── RTPPSP — pp_sprite(x,y,w,h,ptr): команда спрайта резиденту ──
            E("; RTPPSP — pp_sprite(x,y,w,h,ptr): отправить команду спрайта.");
            E("RTPPSP:");
            E("        MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\t4.(R5), PPSX");         // x
            E("        MOV\t6.(R5), PPSY");         // y
            E("        MOV\t8.(R5), PPSW");         // w
            E("        MOV\t10.(R5), PPSH");        // h
            E("        MOV\t12.(R5), R0");          // ptr (байтовый адрес)
            E("        ASR\tR0");                   // ptr/2 → словесный адрес для ПП
            E("        MOV\tR0, PPSPTR");
            E("        MOV\t#3, PPCMD2");           // команда = спрайт
            E("RTPPSW: TST\tPPCMD2");               // ждать обнуления
            E("        BNE\tRTPPSW");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            E("");
            // ── RTSPRH — spr(x,y,ptr): спрайт с заголовком → делегат RTSPR ──
            //   Формат: ptr[0]=тип, ptr[1]=words, ptr[2]=height, данные с ptr+6.
            E("; RTSPRH — spr(x,y,ptr): универсальный формат с заголовком (ЦП).");
            E(";   тип 0 (4цв): данные 1 сл/октет — прямой делегат RTSPR.");
            E(";   тип 1 (8цв): пары (план0, планы1&2) — построчно копируем");
            E(";   слова2 (= точный ЦП-формат) в HBUF, RTSPR по одной строке.");
            E(";   Контролируемая 4цв-проекция 8-цветного спрайта.");
            E("RTSPRH:");
            E("        MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\tR1, -(SP)");
            E("        MOV\tR2, -(SP)");
            E("        MOV\tR3, -(SP)");
            E("        MOV\tR4, -(SP)");
            E("        MOV\t8.(R5), R0");           // R0 = ptr заголовка
            E("        TST\t(R0)");                 // тип?
            E("        BNE\tRTSPH8");               // 1 → 8цв путь
            // ── тип 0: прямой делегат RTSPR (данные уже ЦП-формат) ──
            E("        MOV\tR0, R1");
            E("        ADD\t#6., R1");              // данные = ptr+6
            E("        MOV\tR1, -(SP)");            // push ptr данных
            E("        MOV\t4.(R0), -(SP)");        // push h
            E("        MOV\t2.(R0), R1");           // words
            E("        ASL\tR1");
            E("        ASL\tR1");
            E("        ASL\tR1");                   // → пиксели
            E("        MOV\tR1, -(SP)");            // push w
            E("        MOV\t6.(R5), -(SP)");        // push y
            E("        MOV\t4.(R5), -(SP)");        // push x
            E("        JSR\tPC, RTSPR");
            E("        ADD\t#10., SP");
            E("        BR\tRTSPHX");
            // ── тип 1: построчно, из пар только слово2 (планы 1&2) ──
            E("RTSPH8: MOV\tR0, R1");
            E("        ADD\t#6., R1");              // R1 = данные (пары)
            E("        MOV\t2.(R0), R2");           // R2 = words
            E("        MOV\t4.(R0), R3");           // R3 = h (счётчик строк)
            E("        MOV\t6.(R5), R4");           // R4 = текущий y
            E("RTSPH1: MOV\tR2, -(SP)");            // счётчик слов строки — на стеке
            E("        MOV\t#HBUF, R0");            // R0 = указатель буфера
            E("RTSPH2: TST\t(R1)+");                // пропустить план0
            E("        MOV\t(R1)+, (R0)+");         // слово2 → HBUF
            E("        DEC\t(SP)");
            E("        BNE\tRTSPH2");
            E("        TST\t(SP)+");                // снять счётчик
            E("        MOV\t#HBUF, -(SP)");         // RTSPR(x, y=R4, w, 1, HBUF)
            E("        MOV\t#1, -(SP)");            // h = 1 строка
            E("        MOV\tR2, R0");
            E("        ASL\tR0");
            E("        ASL\tR0");
            E("        ASL\tR0");                   // w в пикселях
            E("        MOV\tR0, -(SP)");
            E("        MOV\tR4, -(SP)");            // y текущей строки
            E("        MOV\t4.(R5), -(SP)");        // x (фрейм R5 цел)
            E("        JSR\tPC, RTSPR");            // RTSPR сохраняет R0-R4
            E("        ADD\t#10., SP");
            E("        INC\tR4");                   // следующая строка
            E("        DEC\tR3");
            E("        BNE\tRTSPH1");
            E("RTSPHX: MOV\t(SP)+, R4");
            E("        MOV\t(SP)+, R3");
            E("        MOV\t(SP)+, R2");
            E("        MOV\t(SP)+, R1");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            E("");
            // ── RTPPSH — pp_spr(x,y,ptr): спрайт с заголовком → делегат RTPPSP ──
            E("; RTPPSH — pp_spr(x,y,ptr): универсальный формат с заголовком (ПП).");
            E("RTPPSH:");
            E("        MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\t8.(R5), R0");           // R0 = ptr заголовка
            E("        MOV\tR0, R1");
            E("        ADD\t#6., R1");
            E("        MOV\tR1, -(SP)");            // push ptr данных
            E("        MOV\t4.(R0), -(SP)");        // push h
            E("        MOV\t2.(R0), R1");           // words
            E("        ASL\tR1");
            E("        ASL\tR1");
            E("        ASL\tR1");                   // → ширина в пикселях
            E("        MOV\tR1, -(SP)");            // push w (пиксели)
            E("        MOV\t6.(R5), -(SP)");        // push y
            E("        MOV\t4.(R5), -(SP)");        // push x
            E("        JSR\tPC, RTPPSP");           // рабочая отправка команды ПП
            E("        ADD\t#10., SP");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            E("");
            // ── RTSQPUT — pp_spr_q(x,y,ptr): положить спрайт в ОЧЕРЕДЬ ──
            //   ЦП пишет слот (x,y,ptr) в SQBUF[head], двигает head, НЕ ЖДЁТ.
            //   Слот = 3 слова. head/tail — индексы 0..15 (кольцо на 16).
            //   Если очередь полна (head+1==tail) — ждём место (редкий случай).
            E("; RTSQPUT — pp_spr_q(x,y,ptr): положить спрайт в очередь (не ждать).");
            E("RTSQPUT:");
            E("        MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\tR1, -(SP)");
            E("        MOV\tR2, -(SP)");
            E("        MOV\tR3, -(SP)");
            E("        MOV\tR4, -(SP)");
            // ждать если очередь полна: (head+1)&15 == tail
            E("RTSQP1: MOV\tSQHEAD, R1");
            E("        INC\tR1");
            E("        BIC\t#177760, R1");          // (head+1)&15
            E("        CMP\tR1, SQTAIL");
            E("        BEQ\tRTSQP1");               // полна — крутимся (редко)
            // адрес слота head в SQBUF: SQBUF + head*3 (в словах, ЦП-адрес)
            E("        MOV\tSQHEAD, R2");
            E("        MOV\tR2, R3");
            E("        ASL\tR3");
            E("        ADD\tR2, R3");               // head*3
            E("        MOV\t#SQBUF, R2");
            E("        ADD\tR3, R2");               // R2 = адрес слота (ЦП)
            E("        MOV\t6.(R5), (R2)+");        // x  → слот[0]
            E("        MOV\t8.(R5), (R2)+");        // y  → слот[1]
            E("        MOV\t10.(R5), (R2)");        // ptr → слот[2]
            E("        MOV\tR1, SQHEAD");           // head = (head+1)&15
            E("        MOV\t(SP)+, R4");
            E("        MOV\t(SP)+, R3");
            E("        MOV\t(SP)+, R2");
            E("        MOV\t(SP)+, R1");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            E("");
            // ── RTSQFLUSH — pp_flush(): ждать пока очередь опустеет ──
            E("; RTSQFLUSH — pp_flush(): дождаться разбора очереди ПП.");
            E("RTSQFLUSH:");
            E("RTSQF1: MOV\tSQHEAD, R0");
            E("        CMP\tR0, SQTAIL");
            E("        BNE\tRTSQF1");               // пока head != tail — ждём
            E("        RTS\tPC");
            E("");
            // ── RTPPBL — pp_blit(sx,sy,w,h,dx,dy): команда блита резиденту ──
            E("; RTPPBL — pp_blit(sx,sy,w,h,dx,dy): копия прямоугольника на ПП.");
            E("RTPPBL:");
            E("        MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\t4.(R5), PPBSX");
            E("        MOV\t6.(R5), PPBSY");
            E("        MOV\t8.(R5), PPBW");
            E("        MOV\t10.(R5), PPBH");
            E("        MOV\t12.(R5), PPBDX");
            E("        MOV\t14.(R5), PPBDY");
            E("        MOV\t#5, PPCMD2");           // команда = блит
            E("RTPPBW: TST\tPPCMD2");
            E("        BNE\tRTPPBW");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            E("");
            // ── RTPPPK — pp_peek(addr): ПП читает слово видеопамяти → R0 ──
            E("; RTPPPK — pp_peek(addr): прочитать слово через ПП. Результат в R0.");
            E("RTPPPK:");
            E("        MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\t4.(R5), PPKA");         // адрес
            E("        MOV\t#6, PPCMD2");           // команда = peek
            E("RTPPKW: TST\tPPCMD2");
            E("        BNE\tRTPPKW");
            E("        MOV\tPPKV, R0");             // результат
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            E("");
            // ── RTVLOAD — vload(vaddr,arr,n): массив → общий блок видеопамяти ──
            E("; RTVLOAD — vload(vaddr, arr, nwords): загрузка блока через порт ЦП.");
            E("RTVLOAD:");
            E("        MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\tR0, -(SP)");
            E("        MOV\tR1, -(SP)");
            E("        MOV\tR2, -(SP)");
            E("        MOV\t4.(R5), R0");           // vaddr
            E("        MOV\t6.(R5), R1");           // ptr (ОЗУ)
            E("        MOV\t8.(R5), R2");           // n слов
            E("RTVL1:  MOV\tR0, @#176640");
            E("        MOV\t(R1)+, @#176642");
            E("        INC\tR0");
            E("        SOB\tR2, RTVL1");
            E("        MOV\t(SP)+, R2");
            E("        MOV\t(SP)+, R1");
            E("        MOV\t(SP)+, R0");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            E("");
            // ── RTPPVS — pp_vspr(x,y,vaddr): спрайт из блока (ПП сам всё читает) ──
            // ── RTPPUP — pp_upload(ptr): положить спрайт в ОЗУ ПП ──
            //   Тем же протоколом канала К2, что и загрузка резидента:
            //   команда 1 выделяет память и возвращает адрес, команда 20
            //   переписывает туда заголовок и данные. Возвращает адрес в
            //   ОЗУ ПП — его и передают в pp_sprm(). Вызывать ДО pp_init().
            E("RTPPUP:");
            E("        MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\tR1, -(SP)");
            E("        MOV\tR2, -(SP)");
            // PPSEN2 работает через R2 и R3, поэтому всё, что нужно после
            // отправки, держим в ячейках, а не в регистрах. На этом и
            // сгорела первая версия: ширина бралась из затёртого R2.
            E("        MOV\t4.(R5), R2");          // адрес массива спрайта
            E("        MOV\tR2, PPUSRC");
            E("        MOV\t2.(R2), R1");          // words
            E("        MOV\tR1, PPUW");
            E("        ASL\tPPUW");
            E("        ASL\tPPUW");
            E("        ASL\tPPUW");                // ширина в пикселях
            E("        MOV\t4.(R2), R0");          // высота
            E("        MOV\tR0, PPUHT");
            E("        ASL\tR1");                  // слов в строке
            E("        MUL\tR0, R1");              // слов данных
            E("        ADD\t#3., R1");             // плюс заголовок
            E("        MTPS\t#340");
            E("        MOV\tR1, PPACP2");
            E("        MOV\tR1, PPLEN2");
            E("        MOVB\t#1, PPCMD2B");        // выделить память в ПП
            E("        JSR\tPC, PPSEN2");
            E("        MOV\tPPAPP2, PPADR2");      // куда легло
            E("        MOVB\t#20, PPCMD2B");       // записать туда данные
            E("        MOV\tPPUSRC, PPACP2");     // источник — из ячейки, R2 уже затёрт
            E("        JSR\tPC, PPSEN2");
            E("        MTPS\t#0");
            // Запомнить спрайт в таблице: адрес в ОЗУ ПП, ширина, высота.
            // Наружу отдаём НЕЧЁТНЫЙ номер: адреса в памяти ЦП всегда чётные,
            // поэтому по одному биту видно, откуда рисовать спрайт.
            E("        MOV\tPPUCNT, R0");
            E("        CMP\tR0, #16.");
            E("        BLT\tRTPUP1");
            E("        CLR\tR0");                  // таблица полна — вернуть 0
            E("        BR\tRTPUP2");
            E("RTPUP1: MOV\tR0, R1");
            E("        ASL\tR1");
            E("        ADD\tR0, R1");
            E("        ASL\tR1");                  // номер * 6
            E("        ADD\t#PPUTBL, R1");
            E("        MOV\tPPADR2, (R1)");
            E("        MOV\tPPUW, 2.(R1)");        // ширина в пикселях
            E("        MOV\tPPUHT, 4.(R1)");       // высота
            E("        INC\tPPUCNT");
            E("        ASL\tR0");
            E("        INC\tR0");                  // номер*2+1 — всегда нечётный
            E("RTPUP2: MOV\t(SP)+, R2");
            E("        MOV\t(SP)+, R1");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            E("");

            // ── RTPPSM — pp_sprm(x,y,ppaddr): спрайт из ОЗУ ПП ──
            // ── RTPPSM — pp_sprm(x,y,id,flip): спрайт из ОЗУ ПП, flip=1 —
            //   зеркально по x (только x, кратный 8). Одна обёртка, ветка
            //   по четвёртому аргументу выбирает команду резиденту.
            E("RTPPSM:");
            E("        MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\t4.(R5), PPSX");
            E("        MOV\t6.(R5), PPSY");
            E("        MOV\t8.(R5), R0");
            E("        JSR\tPC, RTPUSZ");          // размеры спрайта в PPSW/PPSH
            E("        MOV\t8.(R5), R0");
            E("        JSR\tPC, RTPUAD");          // номер → адрес в ОЗУ ПП
            E("        MOV\tR0, PPSPTR");
            E("        MOV\t#11, PPCMD2");         // команда «спрайт из ОЗУ ПП»
            E("RTPSMW: TST\tPPCMD2");
            E("        BNE\tRTPSMW");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            E("");

            // ── RTPPUT — pp_put(x,y,id): вывод спрайта 16 пикселей в любую
            //   позицию, командой 12. Артефактов по краям нет.
            E("RTPPUT: MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\t4.(R5), PPSX");
            E("        MOV\t6.(R5), PPSY");
            E("        MOV\t8.(R5), R0");
            E("        JSR\tPC, RTPUSZ");          // высота из таблицы
            E("        MOV\t8.(R5), R0");
            E("        JSR\tPC, RTPUAD");          // адрес в ОЗУ ПП
            E("        ADD\t#6, R0");              // данные за заголовком
            E("        MOV\tR0, PPSPTR");
            E("        CLR\tPPFLIP");
            E("        MOV\t#12, PPCMD2");
            E("RTPUTW: TST\tPPCMD2");
            E("        BNE\tRTPUTW");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            E("");

            // ── RTPPUTF — pp_putf(x,y,id): то же самое, зеркально по
            //   горизонтали. Данные в ОЗУ ПП те же, разворот — только
            //   при выводе (см. PPS3L/RTBREV/RTBRVW), ничего не копируется.
            E("RTPPUTF: MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\t4.(R5), PPSX");
            E("        MOV\t6.(R5), PPSY");
            E("        MOV\t8.(R5), R0");
            E("        JSR\tPC, RTPUSZ");
            E("        MOV\t8.(R5), R0");
            E("        JSR\tPC, RTPUAD");
            E("        ADD\t#6, R0");
            E("        MOV\tR0, PPSPTR");
            E("        MOV\t#1, PPFLIP");
            E("        MOV\t#12, PPCMD2");
            E("RTPTFW: TST\tPPCMD2");
            E("        BNE\tRTPTFW");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            E("");
            E("");

            // ── RTPUSZ — по номеру выставить PPSW/PPSH из таблицы ──
            E("RTPUSZ: MOV\tR1, -(SP)");
            E("        ASR\tR0");
            E("        MOV\tR0, R1");
            E("        ASL\tR1");
            E("        ADD\tR0, R1");
            E("        ASL\tR1");
            E("        ADD\t#PPUTBL, R1");
            E("        MOV\t2.(R1), PPSW");
            E("        MOV\t4.(R1), PPSH");
            E("        MOV\t(SP)+, R1");
            E("        RTS\tPC");
            E("");

            // ── RTPUAT — pp_upaddr(id): адрес спрайта в ОЗУ ПП (диагностика) ──
            E("RTPUAT: MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\t4.(R5), R0");
            E("        JSR\tPC, RTPUAD");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            E("");

            // ── RTPUAD — по нечётному номеру вернуть адрес в ОЗУ ПП ──
            E("RTPUAD: MOV\tR1, -(SP)");
            E("        ASR\tR0");
            E("        MOV\tR0, R1");
            E("        ASL\tR1");
            E("        ADD\tR0, R1");
            E("        ASL\tR1");
            E("        ADD\t#PPUTBL, R1");
            E("        MOV\t(R1), R0");
            E("        MOV\t(SP)+, R1");
            E("        RTS\tPC");
            E("");

            E("; RTPPVS — pp_vspr(x,y,vaddr): команда 7 — спрайт из общего блока.");
            E("RTPPVS:");
            E("        MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\t4.(R5), PPSX");         // x
            E("        MOV\t6.(R5), PPSY");         // y
            E("        MOV\t8.(R5), PPKA");         // vaddr спрайта в блоке
            E("        MOV\t#7, PPCMD2");           // команда = спрайт из блока
            E("RTPPVW: TST\tPPCMD2");
            E("        BNE\tRTPPVW");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            E("");
            // ── RTPPSTOP — pp_stop(): остановить резидент ──
            E("; RTPPSTOP — pp_stop(): завершить резидентный ПП-движок.");
            E("RTPPSTOP:");
            E("        MOV\t#177777, PPCMD2");     // команда = стоп
            E("RTPPS1: CMP\tPPCMD2, #177777");     // ждать пока ПП подтвердит
            E("        BEQ\tRTPPS1");
            E("        CLR\tPPON");                // флаг: резидент остановлен
            E("        RTS\tPC");
            E("");
            // Резидентный ПП: диспетчер + PPDOT (точка из PPPX/PPPY/PPPC) + PPLINE.
            // Протокол PPCMD2: 0=ждать 1=точка 2=линия 177777=стоп.
            // PPLINE = дословный рабочий Брезенхем, на каждом шаге пишет x,y в
            // PPPX/PPPY и зовёт PPDOT. Регистры не конфликтуют: всё через память.
            // Забрать клавиатуру себе: сбросить разряд 6 регистра состояния
            // (177700) — запрет прерывания с вектором 300. Иначе обработчик
            // штатной программы ПП читает 177702 первым и гасит готовность,
            // а нашему опросу достаётся пустой регистр.
            E("PPRES:  BIC\t#100, @#177700");
            E("        JSR\tPC, PPCLR0");           // при старте: чистый план 0
            E("PPRLP:");
            // ── опрос клавиатуры (регистры на магистрали ПП) ──
            //   177700 бит7 = готовность (нажатие ИЛИ отжатие),
            //   177702 разряды 0-6 = код, бит7: 0 = нажата, 1 = отжата.
            //   Чтение регистра данных сбрасывает готовность.
            //   Пока резидент занимает ПП, штатная обработка клавиатуры
            //   не выполняется, поэтому код кладём сами в ячейку ЦП PPKEY.
            E("        TSTB\t@#177700");
            E("        BPL\tPPKNO");              // событий нет
            E("        MOVB\t@#177702, R0");      // MOVB в регистр расширяет знак
            E("        BMI\tPPKUP");              // бит7=1 → клавишу отпустили
            E("        BIC\t#177600, R0");        // оставить разряды 0-6
            E("        BEQ\tPPKNO");              // код 0 означал бы «нет клавиши»
            E("        MOV\t#<PPKEY/2>, @#177010");
            E("        MOV\tR0, @#177014");       // разовое событие нажатия
            E("        MOV\t#<PPKHLD/2>, @#177010");
            E("        MOV\tR0, @#177014");       // и отметка «клавиша удерживается»
            E("        BR\tPPKNO");
            // Отпускание: регистр отдаёт только разряды 0-3, поэтому
            // сверяем с удерживаемой клавишей по её младшей тетраде.
            E("PPKUP:  BIC\t#177760, R0");        // разряды 0-3 отпущенной
            E("        MOV\t#<PPKHLD/2>, @#177010");
            E("        MOV\t@#177014, R1");
            E("        BEQ\tPPKNO");              // ничего не удерживалось
            E("        BIC\t#177760, R1");        // младшая тетрада удерживаемой
            E("        CMP\tR0, R1");
            E("        BNE\tPPKNO");              // отпустили другую клавишу
            E("        MOV\t#<PPKHLD/2>, @#177010");
            E("        CLR\t@#177014");           // удержание снято
            E("PPKNO:");
            // сначала обслуживаем ОЧЕРЕДЬ спрайтов (параллельный путь)
            E("        MOV\t#<SQHEAD/2>, @#177010");
            E("        MOV\t@#177014, R0");        // head
            E("        MOV\t#<SQTAIL/2>, @#177010");
            E("        MOV\t@#177014, R1");        // tail
            E("        CMP\tR0, R1");
            E("        BEQ\tPPRLQ");               // очередь пуста → к обычным командам
            E("        JSR\tPC, PPSQDRAW");        // нарисовать спрайт из слота tail
            E("        BR\tPPRLP");                // и снова (разгребаем очередь)
            E("PPRLQ:  MOV\t#<PPCMD2/2>, @#177010");
            E("        MOV\t@#177014, R0");
            E("        BEQ\tPPRLP");
            E("        CMP\tR0, #177777");
            E("        BEQ\tPPRDON");
            E("        CMP\tR0, #12");
            E("        BEQ\tPPRS3");
            E("        CMP\tR0, #11");
            E("        BEQ\tPPRSM");
            E("        CMP\tR0, #7");
            E("        BEQ\tPPRVS");
            E("        CMP\tR0, #6");
            E("        BEQ\tPPRPK");
            E("        CMP\tR0, #5");
            E("        BEQ\tPPRBL");
            E("        CMP\tR0, #4");
            E("        BEQ\tPPRCL");
            E("        CMP\tR0, #3");
            E("        BEQ\tPPRSP");
            E("        CMP\tR0, #2");
            E("        BEQ\tPPRLN");
            E("        CMP\tR0, #1");
            E("        BNE\tPPRACK");
            E("        JSR\tPC, PPDOT");
            E("        BR\tPPRACK");
            E("PPRLN:  JSR\tPC, PPLINE");
            E("        BR\tPPRACK");
            E("PPRSP:  JSR\tPC, PPSPR");
            E("        BR\tPPRACK");
            E("PPRSM:  JSR\tPC, PPSPM");
            E("        BR\tPPRACK");
            E("PPRS3:  JSR\tPC, PPS3");
            E("        BR\tPPRACK");
            E("PPRCL:  JSR\tPC, PPCLR0");
            E("        BR\tPPRACK");
            E("PPRBL:  JSR\tPC, PPBLT");
            E("        BR\tPPRACK");
            E("PPRPK:  JSR\tPC, PPPEEK");
            E("        BR\tPPRACK");
            E("PPRVS:  JSR\tPC, PPVSPR");
            E("        BR\tPPRACK");
            E("PPRACK: MOV\t#<PPCMD2/2>, @#177010");
            E("        CLR\t@#177014");
            E("        BR\tPPRLP");
            E("PPRDON: BIS\t#100, @#177700");      // вернуть прерывание клавиатуры монитору
            E("        MOV\t#<PPCMD2/2>, @#177010");
            E("        CLR\t@#177014");
            E("        RTS\tPC");
            E("");
            // PPDOT — точка из PPPX/PPPY/PPPC. Портит R0..R4, сохраняет R5.
            // ── PPSQDRAW — нарисовать спрайт из очереди (слот SQTAIL), tail++ ──
            //   Слот = 3 слова (x,y,ptr). Читает заголовок спрайта из ptr,
            //   раскладывает в PPSX/PPSY/PPSW/PPSH/PPSPTR и зовёт PPSPR.
            E("PPSQDRAW:");
            E("        MOV\t#SQTAIL, @#177010");
            E("        MOV\t@#177014, R2");        // R2 = tail (индекс слота 0..15)
            E("        MOV\tR2, R3");
            E("        ASL\tR3");
            E("        ADD\tR2, R3");              // R3 = tail*3 (смещение в словах)
            // адрес слота в SQBUF (в словах, для порта ПП)
            E("        MOV\t#<SQBUF/2>, R4");
            E("        ADD\tR3, R4");              // R4 = адрес x-слова слота
            E("        MOV\tR4, @#177010");
            E("        MOV\t@#177014, R0");        // x
            E("        MOV\t#<PPSX/2>, @#177010");
            E("        MOV\tR0, @#177014");
            E("        INC\tR4");
            E("        MOV\tR4, @#177010");
            E("        MOV\t@#177014, R0");        // y
            E("        MOV\t#<PPSY/2>, @#177010");
            E("        MOV\tR0, @#177014");
            E("        INC\tR4");
            E("        MOV\tR4, @#177010");
            E("        MOV\t@#177014, R4");        // R4 = ptr (байтовый адрес ОЗУ)
            E("        ASR\tR4");                  // → словный адрес (как RTPPSP)
            // прочитать заголовок [тип,words,height] по ptr
            E("        INC\tR4");                  // пропустить тип (8цв данные)
            E("        MOV\tR4, @#177010");
            E("        MOV\t@#177014, R0");        // words
            E("        ASL\tR0");
            E("        ASL\tR0");
            E("        ASL\tR0");                  // → пиксели
            E("        MOV\t#<PPSW/2>, @#177010");
            E("        MOV\tR0, @#177014");
            E("        INC\tR4");
            E("        MOV\tR4, @#177010");
            E("        MOV\t@#177014, R0");        // height
            E("        MOV\t#<PPSH/2>, @#177010");
            E("        MOV\tR0, @#177014");
            E("        INC\tR4");                  // R4 = адрес данных
            E("        MOV\t#<PPSPTR/2>, @#177010");
            E("        MOV\tR4, @#177014");
            E("        JSR\tPC, PPSPR");           // рисуем
            // tail = (tail+1) & 15
            E("        MOV\t#SQTAIL, @#177010");
            E("        MOV\t@#177014, R0");
            E("        INC\tR0");
            E("        BIC\t#177760, R0");         // & 15
            E("        MOV\t#SQTAIL, @#177010");
            E("        MOV\tR0, @#177014");
            E("        RTS\tPC");
            E("");
            E("PPDOT:  MOV\t#<PPPY/2>, @#177010");
            E("        MOV\t@#177014, R1");
            E("        MOV\t#<DSPST/2>, R2");
            E("        ADD\tR1, R2");
            E("        MOV\tR2, @#177010");
            E("        MOV\t@#177014, R3");
            E("        MOV\t#<PPPX/2>, @#177010");
            E("        MOV\t@#177014, R1");
            E("        MOV\tR1, R4");
            E("        ASR\tR4");
            E("        ASR\tR4");
            E("        ASR\tR4");
            E("        ADD\tR4, R3");
            E("        MOV\t#<PPPC/2>, @#177010");
            E("        MOV\t@#177014, R0");
            E("        MOV\tR0, @#177016");
            E("        BIC\t#177770, R1");
            E("        MOV\t#1, R0");
            E("        ASH\tR1, R0");
            E("        MOV\tR3, @#177010");
            E("        MOVB\tR0, @#177024");
            E("        RTS\tPC");
            E("");
            // PPLINE — Брезенхем. R0=x R1=y. dx,dy,sx,sy,err на стеке. PPDOT через память.
            E("PPLINE:");
            E("        SUB\t#12., SP");
            E("        MOV\t#<PPLX1/2>, @#177010");
            E("        MOV\t@#177014, R3");
            E("        MOV\tR3, 8.(SP)");
            E("        MOV\t#<PPLY1/2>, @#177010");
            E("        MOV\t@#177014, R3");
            E("        MOV\tR3, 10.(SP)");
            E("        MOV\t#<PPPX/2>, @#177010");
            E("        MOV\t@#177014, R0");
            E("        MOV\t#<PPPY/2>, @#177010");
            E("        MOV\t@#177014, R1");
            E("        MOV\t8.(SP), R3");
            E("        SUB\tR0, R3");
            E("        BGE\tPLA");
            E("        NEG\tR3");
            E("PLA:    MOV\tR3, 6.(SP)");
            E("        MOV\t10.(SP), R3");
            E("        SUB\tR1, R3");
            E("        BGE\tPLB");
            E("        NEG\tR3");
            E("PLB:    MOV\tR3, 4.(SP)");
            E("        MOV\t#1, R3");
            E("        CMP\tR0, 8.(SP)");
            E("        BLT\tPLC");
            E("        NEG\tR3");
            E("PLC:    MOV\tR3, 2.(SP)");
            E("        MOV\t#1, R3");
            E("        CMP\tR1, 10.(SP)");
            E("        BLT\tPLD");
            E("        NEG\tR3");
            E("PLD:    MOV\tR3, 0.(SP)");
            E("        MOV\t6.(SP), R2");
            E("        SUB\t4.(SP), R2");
            E("PLLP:   MOV\t#<PPPX/2>, @#177010");
            E("        MOV\tR0, @#177014");
            E("        MOV\t#<PPPY/2>, @#177010");
            E("        MOV\tR1, @#177014");
            E("        MOV\tR0, -(SP)");
            E("        MOV\tR1, -(SP)");
            E("        MOV\tR2, -(SP)");
            E("        JSR\tPC, PPDOT");
            E("        MOV\t(SP)+, R2");
            E("        MOV\t(SP)+, R1");
            E("        MOV\t(SP)+, R0");
            E("        CMP\tR0, 8.(SP)");
            E("        BNE\tPLE");
            E("        CMP\tR1, 10.(SP)");
            E("        BEQ\tPLX");
            E("PLE:    MOV\tR2, R3");
            E("        ASL\tR3");
            E("        MOV\t4.(SP), R4");
            E("        NEG\tR4");
            E("        CMP\tR3, R4");
            E("        BLE\tPLF");
            E("        SUB\t4.(SP), R2");
            E("        ADD\t2.(SP), R0");
            E("PLF:    CMP\tR3, 6.(SP)");
            E("        BGE\tPLG");
            E("        ADD\t6.(SP), R2");
            E("        ADD\t0.(SP), R1");
            E("PLG:    BR\tPLLP");
            E("PLX:    ADD\t#12., SP");
            E("        RTS\tPC");
            E("");
            // ── PPSPR — вывод 8-цветного спрайта (перенос быстрого пути ЦП) ──
            //   Спрайт из редактора: 2 слова на октет (план0, планы1&2).
            //   x кратен 8 (быстрый путь). Параметры из памяти ЦП:
            //   PPSX,PPSY (пиксели), PPSW (пиксели, кратно 8), PPSH (строк),
            //   PPSPTR (адрес данных в словах). Пишем через 177010/177012/177014.
            //   R5=адрес октета, R4=адрес данных, R2=words, R3=h, R0=шаг.
            E("PPSPR:");
            // развилка: x кратен 8 → быстрый путь (октеты), иначе → медленный (пиксели)
            E("        MOV\t#<PPSX/2>, @#177010");
            E("        MOV\t@#177014, R0");
            E("        BIC\t#177770, R0");          // R0 = x & 7
            E("        BEQ\tPPSPF");                // x кратен 8 → быстрый
            E("        JMP\tPPSPX");                // иначе → медленный (попиксельно)
            E("PPSPF:");
            E("        MOV\t#<PPSY/2>, @#177010");
            E("        MOV\t@#177014, R1");
            E("        MOV\t#<DSPST/2>, R2");
            E("        ADD\tR1, R2");
            E("        MOV\tR2, @#177010");
            E("        MOV\t@#177014, R5");        // адрес строки y в планах
            E("        MOV\t#<PPSX/2>, @#177010");
            E("        MOV\t@#177014, R0");
            E("        ASR\tR0");
            E("        ASR\tR0");
            E("        ASR\tR0");
            E("        ADD\tR0, R5");              // + x/8 октетов
            E("        MOV\t#<PPSW/2>, @#177010");
            E("        MOV\t@#177014, R2");
            E("        ASR\tR2");
            E("        ASR\tR2");
            E("        ASR\tR2");                  // ширина в октетах
            E("        MOV\tR2, PPSW8");
            E("        ASL\tR2");
            E("        MOV\tR2, PPSW2");           // слов в строке спрайта
            E("        MOV\t#<PPSH/2>, @#177010");
            E("        MOV\t@#177014, R3");        // высота
            E("        MOV\t#<PPSPTR/2>, @#177010");
            E("        MOV\t@#177014, R4");        // адрес данных (в словах)
            // Чтение и запись разведены: регистр адреса 177010 один, и
            // чередование «прочитать слово данных — записать октет» стоило
            // двух его переустановок на каждый октет. Теперь адрес ставится
            // дважды за строку, а внутри работает INC @#177010.
            E("PPSP1:  MOV\tR4, @#177010");        // ── строка спрайта в буфер ──
            E("        MOV\tPC, R0");
            E("        ADD\t#PSBUF-., R0");
            E("        MOV\tPPSW2, R2");
            E("PPSP2:  MOV\t@#177014, (R0)+");
            E("        INC\t@#177010");
            E("        SOB\tR2, PPSP2");
            E("        ADD\tPPSW2, R4");           // следующая строка данных
            E("        MOV\tR5, @#177010");        // ── буфер в видеопамять ──
            E("        MOV\tPC, R0");
            E("        ADD\t#PSBUF-., R0");
            E("        MOV\tPPSW8, R2");
            E("PPSP3:  MOV\t(R0)+, @#177012");     // план 0
            E("        MOV\t(R0)+, R1");           // планы 1 и 2 из данных
            E("        SWAB\tR1");                  // → аппаратный порядок
            E("        MOV\tR1, @#177014");
            E("        INC\t@#177010");
            E("        SOB\tR2, PPSP3");
            E("        ADD\t#80., R5");            // следующая строка экрана
            E("        DEC\tR3");
            E("        BNE\tPPSP1");
            E("        RTS\tPC");
            E("PPSW8:  .WORD\t0");                 // ширина спрайта в октетах
            E("PPSW2:  .WORD\t0");                 // слов в строке спрайта
            E("");

            // ══════════════════════════════════════════════════════════
            //  PPS3 — вывод 8-цветного спрайта шириной 16 пикселей в ЛЮБУЮ
            //  позицию. Отдельная процедура: старый путь не затрагивается.
            //
            //  Спрайт занимает 2 октета, после сдвига — 3. Цепочка сдвига
            //  развёрнута ровно на эти 3 октета (9 команд вместо 48), а
            //  адрес буфера лежит в регистре — значит цепочка не зависит
            //  от того, по какому адресу загружен резидент.
            //
            //  Крайние октеты пишутся под маской, средний целиком.
            //  Вход: PPSX, PPSY, PPSH, PPSPTR (байтовый адрес данных в ОЗУ ПП).
            // ══════════════════════════════════════════════════════════
            // ── RTBREV — развернуть биты одного байта (в младшем байте R0,
            //   старший не трогает). RTBRVW — то же для ОБОИХ байт слова
            //   независимо (план1 и план2 каждый по себе, не путая местами).
            E("RTBREV: MOV\tR2, -(SP)");
            E("        CLR\tR2");
            E("        MOV\t#8., R1");
            E("RTBRL:  ASRB\tR0");
            E("        ROLB\tR2");
            E("        SOB\tR1, RTBRL");
            E("        BIC\t#377, R0");
            E("        BIS\tR2, R0");
            E("        MOV\t(SP)+, R2");
            E("        RTS\tPC");
            E("");
            E("RTBRVW: JSR\tPC, RTBREV");
            E("        SWAB\tR0");
            E("        JSR\tPC, RTBREV");
            E("        SWAB\tR0");
            E("        RTS\tPC");
            E("");

            E("PPS3:   BR\tPPS3G");
            E("PPS3Q:  RTS\tPC");                  // выход рядом: ветвление далеко не достаёт
            E("PPS3G:  MOV\t#<PPSPTR/2>, @#177010");
            E("        MOV\t@#177014, R4");         // данные спрайта
            E("        MOV\t#<PPSH/2>, @#177010");
            E("        MOV\t@#177014, R3");         // высота
            E("        BEQ\tPPS3Q");
            // ── отсечение по вертикали ──
            //   Таблица строк рассчитана на 264 строки. Спрайт за краем
            //   экрана брал бы адрес из-за её конца, и запись уходила бы
            //   по случайному месту. Снизу высота обрезается, целиком
            //   ушедший за экран не рисуется вовсе.
            E("        MOV\t#<PPSY/2>, @#177010");
            E("        MOV\t@#177014, R0");         // y
            E("        BLT\tPPS3Q");               // выше экрана — не рисуем
            E("        CMP\tR0, #264.");
            E("        BGE\tPPS3Q");               // ниже экрана — не рисуем
            E("        MOV\tR0, R1");
            E("        ADD\tR3, R1");
            E("        CMP\tR1, #264.");
            E("        BLE\tPPS3C");
            E("        MOV\t#264., R3");           // низ за экраном — обрезать
            E("        SUB\tR0, R3");
            E("PPS3C:  MOV\tR3, PS3H");
            E("        MOV\tR0, R1");              // R1 = y для таблицы строк
            E("        MOV\t#<DSPST/2>, R2");
            E("        ADD\tR1, R2");
            E("        MOV\tR2, @#177010");
            E("        MOV\t@#177014, R5");         // адрес строки y
            E("        MOV\t#<PPSX/2>, @#177010");
            E("        MOV\t@#177014, R0");
            E("        MOV\tR0, R1");
            E("        BIC\t#177770, R1");          // s = x & 7
            E("        ASR\tR0");
            E("        ASR\tR0");
            E("        ASR\tR0");
            E("        ADD\tR0, R5");               // + x/8
            E("        MOV\tR1, PS3S");
            // маски крайних октетов
            E("        MOV\t#377, R2");
            E("        MOV\tR1, R0");
            E("        BEQ\tPPS3M2");
            E("PPS3M1: ASL\tR2");
            E("        DEC\tR0");
            E("        BNE\tPPS3M1");
            E("PPS3M2: BIC\t#177400, R2");
            E("        MOV\tR2, PS3ML");            // левый октет: биты s..7
            E("        MOV\tR2, R0");
            E("        SWAB\tR0");
            E("        BIS\tR2, R0");
            E("        MOV\tR0, PS3ML2");
            E("        COM\tR2");
            E("        BIC\t#177400, R2");
            E("        MOV\tR2, PS3MR");            // правый октет: биты 0..s-1
            E("        MOV\tR2, R0");
            E("        SWAB\tR0");
            E("        BIS\tR2, R0");
            E("        MOV\tR0, PS3MR2");
            // ── строка ──
            // ── PPS3L — прочитать строку в буфер. Если PPFLIP=1, читает
            //   октеты в обратном порядке и разворачивает биты в каждом
            //   байте (RTBREV/RTBRVW) — горизонтальный флип спрайта.
            //   Данные в ОЗУ ПП не трогаются и не копируются лишний раз —
            //   разворот происходит в регистрах на лету, при выводе.
            E("PPS3L:  MOV\t#<PPFLIP/2>, @#177010");
            E("        MOV\t@#177014, R0");
            E("        TST\tR0");
            E("        BNE\tPPS3LF");
            E("        MOV\tPC, R0");
            E("        ADD\t#PS3BUF-., R0");        // R0 = буфер
            E("        MOV\t(R4)+, (R0)+");         // октет 0: план0
            E("        MOV\t(R4)+, (R0)+");         // октет 0: план1|план2 из данных
            E("        MOV\t(R4)+, (R0)+");         // октет 1: план0
            E("        MOV\t(R4)+, (R0)+");         // октет 1: план1|план2 из данных
            E("        BR\tPPS3LX");
            E("PPS3LF: MOV\tPC, R2");
            E("        ADD\t#PS3BUF-., R2");        // R2 = буфер (не трогается RTBREV)
            E("        MOV\t(R4)+, R3");            // октет 0: план0 — придержать
            E("        MOV\t(R4)+, PPFT1");         // октет 0: план1|план2 — придержать
            E("        MOV\t(R4)+, R0");            // октет 1: план0
            E("        JSR\tPC, RTBREV");           // развернуть биты
            E("        MOV\tR0, (R2)+");            // -> позиция 0 (октет 1 встал первым)
            E("        MOV\t(R4)+, R0");            // октет 1: план1|план2
            E("        JSR\tPC, RTBRVW");           // развернуть биты в КАЖДОМ байте отдельно
            E("        MOV\tR0, (R2)+");
            E("        MOV\tR3, R0");                // октет 0: план0 (придержанный)
            E("        JSR\tPC, RTBREV");
            E("        MOV\tR0, (R2)+");             // -> позиция 1 (октет 0 встал вторым)
            E("        MOV\tPPFT1, R0");
            E("        JSR\tPC, RTBRVW");
            E("        MOV\tR0, (R2)+");
            E("        MOV\tR2, R0");                // передать позицию в R0 для общего хвоста
            E("PPS3LX: CLR\t(R0)+");                // октет 2 — под перенос
            E("        CLR\t(R0)");
            E("        MOV\tPC, R0");
            E("        ADD\t#PS3BUF-., R0");
            E("        MOV\tPS3S, R1");
            E("        BEQ\tPPS3W");                // кратно 8 — сдвигать нечего
            // цепочка сдвига: три плоскости по три октета, флаг переноса
            // несёт бит из байта в байт, вставить сюда ничего нельзя
            E("PPS3S1: CLC");
            E("        ROLB\t0.(R0)");
            E("        ROLB\t4.(R0)");
            E("        ROLB\t8.(R0)");
            E("        CLC");
            E("        ROLB\t2.(R0)");
            E("        ROLB\t6.(R0)");
            E("        ROLB\t10.(R0)");
            E("        CLC");
            E("        ROLB\t3.(R0)");
            E("        ROLB\t7.(R0)");
            E("        ROLB\t11.(R0)");
            E("        DEC\tR1");
            E("        BNE\tPPS3S1");
            // ── вывод трёх октетов ──
            E("PPS3W:  MOV\tPS3ML, PS3MA");
            E("        MOV\tPS3ML2, PS3MB");
            E("        JSR\tPC, PPS3R1");           // левый под маской
            E("        MOV\tR5, @#177010");         // средний целиком
            E("        MOV\t(R0)+, @#177012");
            E("        MOV\t(R0)+, @#177014");      // уже развёрнуто в PPS3L — второй раз не трогаем
            E("        INC\tR5");
            E("        MOV\tPS3MR, PS3MA");
            E("        MOV\tPS3MR2, PS3MB");
            E("        JSR\tPC, PPS3R1");           // правый под маской
            E("        SUB\t#3, R5");               // вернуться к началу строки (3 октета)
            E("        ADD\t#80., R5");             // и на строку ниже
            E("        DEC\tPS3H");
            E("        BNE\tPPS3L");
            E("PPS3R:  RTS\tPC");
            E("");
            // ── PPS3R1 — записать октет под маской PS3MA/PS3MB ──
            E("PPS3R1: MOV\tR5, @#177010");
            E("        MOV\t@#177012, PS3T1");
            E("        MOV\t@#177014, PS3T2");
            E("        MOV\tPS3MA, R1");
            E("        BIC\tR1, PS3T1");
            E("        COM\tR1");
            E("        MOV\t(R0)+, PS3T3");
            E("        BIC\tR1, PS3T3");
            E("        BIS\tPS3T3, PS3T1");
            E("        MOV\tPS3MB, R1");
            E("        BIC\tR1, PS3T2");
            E("        COM\tR1");
            E("        MOV\t(R0)+, PS3T3");
            E("        BIC\tR1, PS3T3");
            E("        BIS\tPS3T3, PS3T2");
            E("        MOV\tR5, @#177010");
            E("        MOV\tPS3T1, @#177012");
            E("        MOV\tPS3T2, @#177014");
            E("        INC\tR5");
            E("        RTS\tPC");
            E("PS3BUF: .BLKW\t6.");                 // три октета по два слова
            E("PPFT1:  .WORD\t0");                  // придержать план1|план2 октета 0 при флипе (своя память ПП)
            E("PS3S:   .WORD\t0");
            E("PS3H:   .WORD\t0");
            E("PS3ML:  .WORD\t0");
            E("PS3ML2: .WORD\t0");
            E("PS3MR:  .WORD\t0");
            E("PS3MR2: .WORD\t0");
            E("PS3MA:  .WORD\t0");
            E("PS3MB:  .WORD\t0");
            E("PS3T1:  .WORD\t0");
            E("PS3T2:  .WORD\t0");
            E("PS3T3:  .WORD\t0");
            E("");

            // ── PPSPX — вывод спрайта с ПРОИЗВОЛЬНЫМ x (перенос буферного пути RTSPS с ЦП) ──
            // Схема ЦП один-в-один: строка копируется в буфер PSBUF, сдвигается
            // ROLB-цепочками на s=x&7 бит, выводится words+1 октетов.
            // Отличия от ЦП только технические: чтение строки из памяти ЦП косвенно
            // (177010/177014), ТРИ ROLB-цепочки (план0/план1/план2 — формат 2 сл/октет),
            // вывод через 177010/177012/177014, адрес буфера через PC (код перемещаемый).
            E("PPSPX:");
            E("        MOV\t#<PPSY/2>, @#177010");
            E("        MOV\t@#177014, R1");
            E("        MOV\t#<DSPST/2>, R2");
            E("        ADD\tR1, R2");
            E("        MOV\tR2, @#177010");
            E("        MOV\t@#177014, R5");
            E("        MOV\t#<PPSX/2>, @#177010");
            E("        MOV\t@#177014, R0");
            E("        MOV\tR0, R1");
            E("        BIC\t#177770, R1");        // s = x & 7
            // Маски крайних октетов. Спрайт занимает в первом октете биты
            // s..7, в последнем — биты 0..s-1. Всё остальное принадлежит
            // фону и должно уцелеть, поэтому крайние октеты пишутся
            // read-modify-write, а не целиком.
            E("        MOV\t#377, R3");
            E("        MOV\tR1, R4");
            E("        BEQ\tPSXMK2");
            E("PSXMK1: ASL\tR3");
            E("        DEC\tR4");
            E("        BNE\tPSXMK1");
            E("PSXMK2: BIC\t#177400, R3");        // R3 = маска левого октета
            E("        MOV\tR3, PSMKL0");
            E("        MOV\tR3, R4");
            E("        SWAB\tR4");
            E("        BIS\tR3, R4");
            E("        MOV\tR4, PSMKL2");
            E("        COM\tR3");
            E("        BIC\t#177400, R3");        // R3 = маска правого октета
            E("        MOV\tR3, PSMKR0");
            E("        MOV\tR3, R4");
            E("        SWAB\tR4");
            E("        BIS\tR3, R4");
            E("        MOV\tR4, PSMKR2");
            E("        ASR\tR0");
            E("        ASR\tR0");
            E("        ASR\tR0");
            E("        ADD\tR0, R5");
            E("        MOV\t#<PPSW/2>, @#177010");
            E("        MOV\t@#177014, R2");
            E("        ASR\tR2");
            E("        ASR\tR2");
            E("        ASR\tR2");
            E("        MOV\t#<PPSH/2>, @#177010");
            E("        MOV\t@#177014, R3");
            E("        MOV\t#<PPSPTR/2>, @#177010");
            E("        MOV\t@#177014, R4");
            E("        MOV\t#80., R0");
            E("        SUB\tR2, R0");
            E("        DEC\tR0");
            E("        MOV\tR1, -(SP)");
            E("        MOV\tR0, -(SP)");
            E("        MOV\tR3, -(SP)");
            E("PSXL1:");
            E("        MOV\tR2, R1");
            E("        MOV\tPC, R0");
            E("        ADD\t#PSBUF-., R0");
            E("PSXC1:  MOV\tR4, @#177010");
            E("        MOV\t@#177014, (R0)+");
            E("        INC\tR4");
            E("        MOV\tR4, @#177010");
            E("        MOV\t@#177014, (R0)+");
            E("        SWAB\t-2.(R0)");              // → аппаратный порядок
            E("        INC\tR4");
            E("        DEC\tR1");
            E("        BNE\tPSXC1");
            E("        CLR\t(R0)+");
            E("        CLR\t(R0)");
            E("        MOV\t4.(SP), R1");
            E("        JSR\tPC, PSXSHF");         // сдвинуть строку на x&7 пикселей
            // вывод строки — инлайном: лишний уровень вызова в резиденте
            // ПП ломает возврат (стека хватает на два), поэтому PSXOUT
            // раскрыт здесь. Внутри остаётся один JSR — на PSXRMW.
            E("        MOV\tPC, R0");
            E("        ADD\t#PSBUF-., R0");
            E("        MOV\tPSMKL0, PSMKA");
            E("        MOV\tPSMKL2, PSMKB");
            E("        JSR\tPC, PSXRMW");
            E("        MOV\tR2, R1");
            E("        DEC\tR1");
            E("        BEQ\tPSXO2");
            E("PSXO1:  MOV\tR5, @#177010");
            E("        MOV\t(R0)+, @#177012");
            E("        MOV\t(R0)+, @#177014");
            E("        INC\tR5");
            E("        DEC\tR1");
            E("        BNE\tPSXO1");
            E("PSXO2:  MOV\tPSMKR0, PSMKA");
            E("        MOV\tPSMKR2, PSMKB");
            E("        JSR\tPC, PSXRMW");
            E("        ADD\t2.(SP), R5");
            E("        DEC\t0.(SP)");
            E("        BEQ\tPSXL3");
            E("        JMP\tPSXL1");
            E("PSXL3:  TST\t(SP)+");
            E("        TST\t(SP)+");
            E("        TST\t(SP)+");
            E("        RTS\tPC");
            E("");
            // ── PPSPM — спрайт, лежащий в ОЗУ ПП (команда 11) ──
            //   PPSPTR указывает на заголовок [тип, words, height] в памяти
            //   самого ПП, поэтому данные читаются напрямую, без окна
            //   177010/177014: одна команда на слово вместо двух.
            //   При x кратном 8 строка идёт в видеопамять сразу, без буфера —
            //   четыре команды на октет против двенадцати в исходном пути.
            // ── RTFLPB — развернуть биты НИЗКОГО байта R2 (0..255), высокий
            //   байт результата очищается. Классический приём: 8 раз
            //   вынести бит через перенос и тут же вдвинуть его в R0 с
            //   другого конца. ──

            E("PPSPM:  MOV\t#<PPSPTR/2>, @#177010");
            E("        MOV\t@#177014, R4");        // адрес заголовка в ОЗУ ПП
            E("        ADD\t#6, R4");              // данные идут за заголовком
            // Размеры берём из PPSW/PPSH — их выставляет ЦП по таблице
            // загруженных спрайтов. Читать заголовок из ОЗУ ПП незачем:
            // ошибка в адресе давала бы мусорную высоту и вечный цикл.
            E("        MOV\t#<PPSW/2>, @#177010");
            E("        MOV\t@#177014, R2");
            E("        ASR\tR2");
            E("        ASR\tR2");
            E("        ASR\tR2");                  // ширина в октетах
            E("        BEQ\tPPSMR");
            E("        MOV\t#<PPSH/2>, @#177010");
            E("        MOV\t@#177014, R3");        // высота
            E("        BNE\tPPSMY");
            E("PPSMR: RTS\tPC");                   // нулевой размер — выходим
            E("PPSMY:");
            E("        MOV\t#<PPSY/2>, @#177010");
            E("        MOV\t@#177014, R1");
            E("        MOV\t#<DSPST/2>, R0");
            E("        ADD\tR1, R0");
            E("        MOV\tR0, @#177010");
            E("        MOV\t@#177014, R5");        // адрес строки y
            E("        MOV\t#<PPSX/2>, @#177010");
            E("        MOV\t@#177014, R0");
            E("        MOV\tR0, R1");
            E("        BIC\t#177770, R1");         // s = x & 7
            E("        ASR\tR0");
            E("        ASR\tR0");
            E("        ASR\tR0");
            E("        ADD\tR0, R5");
            E("        TST\tR1");
            E("        BNE\tPPSMX");               // x не кратен 8 — через буфер
            E("PPSM1:  MOV\tR5, @#177010");
            E("        MOV\tR2, R1");
            E("PPSM2:  MOV\t(R4)+, @#177012");
            E("        MOV\t(R4)+, @#177014");
            E("        INC\t@#177010");
            E("        SOB\tR1, PPSM2");
            E("        ADD\t#80., R5");
            E("        DEC\tR3");
            E("        BNE\tPPSM1");
            E("        RTS\tPC");
            // произвольный x: строка в буфер, сдвиг, вывод под масками
            E("PPSMX:  MOV\tR1, PPSMS");           // запомнить сдвиг
            E("        MOV\t#377, R3");
            E("        MOV\tR1, R0");
            E("PPSMK1: ASL\tR3");
            E("        DEC\tR0");
            E("        BNE\tPPSMK1");
            E("        BIC\t#177400, R3");
            E("        MOV\tR3, PSMKL0");
            E("        MOV\tR3, R0");
            E("        SWAB\tR0");
            E("        BIS\tR3, R0");
            E("        MOV\tR0, PSMKL2");
            E("        COM\tR3");
            E("        BIC\t#177400, R3");
            E("        MOV\tR3, PSMKR0");
            E("        MOV\tR3, R0");
            E("        SWAB\tR0");
            E("        BIS\tR3, R0");
            E("        MOV\tR0, PSMKR2");
            E("        MOV\t#<PPSH/2>, @#177010");
            E("        MOV\t@#177014, R3");        // высота (R3 был занят маской)
            // Высота живёт в ячейке, а не в R3: PSXRMW использует R3 как
            // рабочий, и счётчик строк затирался маской.
            E("        MOV\tR3, PPSMH");
            E("        MOV\t#80., R0");
            E("        SUB\tR2, R0");
            E("        DEC\tR0");
            E("        MOV\tR0, PPSMD");           // шаг до следующей строки
            E("PPSML1: MOV\tR2, R1");
            E("        MOV\tPC, R0");
            E("        ADD\t#PSBUF-., R0");
            E("PPSMC1: MOV\t(R4)+, (R0)+");        // строка из ОЗУ ПП — напрямую
            E("        MOV\t(R4)+, (R0)+");
            E("        SOB\tR1, PPSMC1");
            E("        CLR\t(R0)+");
            E("        CLR\t(R0)");
            E("        MOV\tPPSMS, R1");
            E("        JSR\tPC, PSXSHF");
            // вывод строки — инлайном: лишний уровень вызова в резиденте
            // ПП ломает возврат (стека хватает на два), поэтому PSXOUT
            // раскрыт здесь. Внутри остаётся один JSR — на PSXRMW.
            E("        MOV\tPC, R0");
            E("        ADD\t#PSBUF-., R0");
            E("        MOV\tPSMKL0, PSMKA");
            E("        MOV\tPSMKL2, PSMKB");
            E("        JSR\tPC, PSXRMW");
            E("        MOV\tR2, R1");
            E("        DEC\tR1");
            E("        BEQ\tPSMO2");
            E("PSMO1:  MOV\tR5, @#177010");
            E("        MOV\t(R0)+, @#177012");
            E("        MOV\t(R0)+, @#177014");
            E("        INC\tR5");
            E("        DEC\tR1");
            E("        BNE\tPSMO1");
            E("PSMO2:  MOV\tPSMKR0, PSMKA");
            E("        MOV\tPSMKR2, PSMKB");
            E("        JSR\tPC, PSXRMW");
            E("        ADD\tPPSMD, R5");
            E("        DEC\tPPSMH");
            E("        BNE\tPPSML1");
            E("        RTS\tPC");
            E("PPSMH:  .WORD\t0");                 // счётчик строк буферного пути
            E("PPSMS:  .WORD\t0");                 // сдвиг x&7
            E("PPSMD:  .WORD\t0");                 // шаг строки
            E("");
            E("");

            // ── PSXSHF — сдвинуть строку в PSBUF на R1 пикселей вправо ──
            //   Три ROLB-цепочки: план0, план1, план2.
            E("PSXSHF: TST\tR1");
            E("        BEQ\tPSXSH9");
            E("PSXSH1: CLC");
            E("        ROLB\tPSBUF+0.");
            E("        ROLB\tPSBUF+4.");
            E("        ROLB\tPSBUF+8.");
            E("        ROLB\tPSBUF+12.");
            E("        ROLB\tPSBUF+16.");
            E("        ROLB\tPSBUF+20.");
            E("        ROLB\tPSBUF+24.");
            E("        ROLB\tPSBUF+28.");
            E("        ROLB\tPSBUF+32.");
            E("        ROLB\tPSBUF+36.");
            E("        ROLB\tPSBUF+40.");
            E("        ROLB\tPSBUF+44.");
            E("        ROLB\tPSBUF+48.");
            E("        ROLB\tPSBUF+52.");
            E("        ROLB\tPSBUF+56.");
            E("        ROLB\tPSBUF+60.");
            E("        CLC");
            E("        ROLB\tPSBUF+2.");
            E("        ROLB\tPSBUF+6.");
            E("        ROLB\tPSBUF+10.");
            E("        ROLB\tPSBUF+14.");
            E("        ROLB\tPSBUF+18.");
            E("        ROLB\tPSBUF+22.");
            E("        ROLB\tPSBUF+26.");
            E("        ROLB\tPSBUF+30.");
            E("        ROLB\tPSBUF+34.");
            E("        ROLB\tPSBUF+38.");
            E("        ROLB\tPSBUF+42.");
            E("        ROLB\tPSBUF+46.");
            E("        ROLB\tPSBUF+50.");
            E("        ROLB\tPSBUF+54.");
            E("        ROLB\tPSBUF+58.");
            E("        ROLB\tPSBUF+62.");
            E("        CLC");
            E("        ROLB\tPSBUF+3.");
            E("        ROLB\tPSBUF+7.");
            E("        ROLB\tPSBUF+11.");
            E("        ROLB\tPSBUF+15.");
            E("        ROLB\tPSBUF+19.");
            E("        ROLB\tPSBUF+23.");
            E("        ROLB\tPSBUF+27.");
            E("        ROLB\tPSBUF+31.");
            E("        ROLB\tPSBUF+35.");
            E("        ROLB\tPSBUF+39.");
            E("        ROLB\tPSBUF+43.");
            E("        ROLB\tPSBUF+47.");
            E("        ROLB\tPSBUF+51.");
            E("        ROLB\tPSBUF+55.");
            E("        ROLB\tPSBUF+59.");
            E("        ROLB\tPSBUF+63.");
            E("        DEC\tR1");
            E("        BNE\tPSXSH1");
            E("PSXSH9: RTS\tPC");
            E("");

            // ── PSXRMW — записать октет из буфера под маской PSMKA/PSMKB ──
            //   Биты маски = пиксели спрайта, остальные берутся из VRAM.
            //   Вход: R5 — адрес октета, R0 — указатель в PSBUF (2 слова).
            //   Выход: R5 увеличен на 1, R0 сдвинут на пару слов.
            E("PSXRMW: MOV\tR5, @#177010");
            E("        MOV\t@#177012, PSTM1");    // фон: план0
            E("        MOV\t@#177014, PSTM2");    // фон: планы1&2
            E("        MOV\tPSMKA, R3");
            E("        BIC\tR3, PSTM1");          // освободить биты под спрайт
            E("        COM\tR3");
            E("        MOV\t(R0)+, PSTM3");
            E("        BIC\tR3, PSTM3");          // из спрайта — только его биты
            E("        BIS\tPSTM3, PSTM1");
            E("        MOV\tPSMKB, R3");
            E("        BIC\tR3, PSTM2");
            E("        COM\tR3");
            E("        MOV\t(R0)+, PSTM3");
            E("        BIC\tR3, PSTM3");
            E("        BIS\tPSTM3, PSTM2");
            E("        MOV\tR5, @#177010");
            E("        MOV\tPSTM1, @#177012");
            E("        MOV\tPSTM2, @#177014");
            E("        INC\tR5");
            E("        RTS\tPC");
            // ── PPBLT — блит: копия прямоугольника VRAM→VRAM, все 3 плана ──
            //   Параметры (память ЦП): PPBSX,PPBSY (источник), PPBW (пиксели,
            //   кратно 8), PPBH, PPBDX,PPBDY (приёмник). sx,dx кратны 8.
            //   Основное применение: атлас спрайтов в невидимой зоне (x>=320
            //   в режиме 0) → быстрый вывод в видимую без распаковки формата.
            // PPVSPR — спрайт из общего блока: заголовок [тип,words,h] по
            //   видео-адресу PPKA, данные с PPKA+3. Раскладывает параметры в
            //   PPSW/PPSH/PPSPTR и вызывает рабочий PPSPR (x,y уже в PPSX/PPSY).
            E("PPVSPR: MOV\t#<PPKA/2>, @#177010");
            E("        MOV\t@#177014, R4");        // R4 = vaddr
            E("        INC\tR4");                  // пропустить тип (данные 8цв)
            E("        MOV\tR4, @#177010");
            E("        MOV\t@#177014, R1");        // words
            E("        ASL\tR1");
            E("        ASL\tR1");
            E("        ASL\tR1");                  // → пиксели (PPSPR ждёт пиксели)
            E("        INC\tR4");
            E("        MOV\tR4, @#177010");
            E("        MOV\t@#177014, R2");        // height
            E("        INC\tR4");                  // R4 = видео-адрес данных
            E("        MOV\t#<PPSW/2>, @#177010");
            E("        MOV\tR1, @#177014");
            E("        MOV\t#<PPSH/2>, @#177010");
            E("        MOV\tR2, @#177014");
            E("        MOV\t#<PPSPTR/2>, @#177010");
            E("        MOV\tR4, @#177014");
            E("        JSR\tPC, PPSPR");           // рабочий вывод спрайта
            E("        RTS\tPC");
            // PPPEEK — прочитать слово (планы 1&2) по адресу PPKA → PPKV
            E("PPPEEK: MOV\t#<PPKA/2>, @#177010");
            E("        MOV\t@#177014, R0");        // R0 = адрес-параметр
            E("        MOV\tR0, @#177010");
            E("        MOV\t@#177014, R1");        // R1 = слово по адресу
            E("        MOV\t#<PPKV/2>, @#177010");
            E("        MOV\tR1, @#177014");        // вернуть результат
            E("        RTS\tPC");
            E("PPBLT:");
            E("        MOV\t#<PPBSY/2>, @#177010");
            E("        MOV\t@#177014, R1");
            E("        MOV\t#<DSPST/2>, R2");
            E("        ADD\tR1, R2");
            E("        MOV\tR2, @#177010");
            E("        MOV\t@#177014, R4");        // адрес строки src
            E("        MOV\t#<PPBSX/2>, @#177010");
            E("        MOV\t@#177014, R0");
            E("        ASR\tR0");
            E("        ASR\tR0");
            E("        ASR\tR0");
            E("        ADD\tR0, R4");              // R4 = src октет
            E("        MOV\t#<PPBDY/2>, @#177010");
            E("        MOV\t@#177014, R1");
            E("        MOV\t#<DSPST/2>, R2");
            E("        ADD\tR1, R2");
            E("        MOV\tR2, @#177010");
            E("        MOV\t@#177014, R5");
            E("        MOV\t#<PPBDX/2>, @#177010");
            E("        MOV\t@#177014, R0");
            E("        ASR\tR0");
            E("        ASR\tR0");
            E("        ASR\tR0");
            E("        ADD\tR0, R5");              // R5 = dst октет
            E("        MOV\t#<PPBW/2>, @#177010");
            E("        MOV\t@#177014, R2");
            E("        ASR\tR2");
            E("        ASR\tR2");
            E("        ASR\tR2");                  // R2 = октетов
            E("        MOV\t#<PPBH/2>, @#177010");
            E("        MOV\t@#177014, R3");        // R3 = h
            E("        MOV\t#80., R0");
            E("        SUB\tR2, R0");
            E("        MOV\tR0, -(SP)");           // шаг строки на стеке
            E("PPBL1:  MOV\tR2, -(SP)");           // счётчик октетов
            // Адрес переустанавливается перед каждым обращением: читать или
            // писать два слова после одной установки — допущение, которое
            // здесь ничем не подтверждено, а ошибка дала бы порчу планов 1 и 2.
            E("PPBL2:  MOV\tR4, @#177010");
            E("        MOV\t@#177012, R0");        // план0 src
            E("        MOV\tR4, @#177010");
            E("        MOV\t@#177014, R1");        // планы1&2 src
            E("        MOV\tR5, @#177010");
            E("        MOV\tR0, @#177012");        // план0 dst
            E("        MOV\tR5, @#177010");
            E("        MOV\tR1, @#177014");        // планы1&2 dst
            E("        INC\tR4");
            E("        INC\tR5");
            E("        DEC\t(SP)");
            E("        BNE\tPPBL2");
            E("        TST\t(SP)+");               // снять счётчик
            E("        MOV\t(SP), R0");            // шаг
            E("        ADD\tR0, R4");
            E("        ADD\tR0, R5");
            E("        DEC\tR3");
            E("        BNE\tPPBL1");
            E("        TST\t(SP)+");               // снять шаг
            E("        RTS\tPC");
            // PPCLR0 — очистить план 0 видимой области (264 строки × 80 октетов)
            E("PPCLR0: MOV\t#100000, R1");
            E("        MOV\t#21120., R0");         // 264*80 октетов
            E("PPCL01: MOV\tR1, @#177010");
            E("        CLR\t@#177012");            // план0 = 0 (планы 1&2 не трогаем)
            E("        INC\tR1");
            E("        SOB\tR0, PPCL01");
            E("        RTS\tPC");
            E("PSBUF:  .BLKW\t34.");
            // маски крайних октетов для вывода при x, не кратном 8
            E("PSMKL0: .WORD\t0");                // левый октет, план0
            E("PSMKL2: .WORD\t0");                // левый октет, планы1&2
            E("PSMKR0: .WORD\t0");                // правый октет, план0
            E("PSMKR2: .WORD\t0");                // правый октет, планы1&2
            E("PSMKA:  .WORD\t0");                // текущая маска, план0
            E("PSMKB:  .WORD\t0");                // текущая маска, планы1&2
            E("PSTM1:  .WORD\t0");
            E("PSTM2:  .WORD\t0");
            E("PSTM3:  .WORD\t0");
            E("PPREND:");
            E("");

            // Режимы 0,2: 320 пикселей (40 слов/строку)
            // Режимы 1,3: 640 пикселей (80 слов/строку)
            // Таблицы на 640 — покрывают оба режима
            EmitDiskIO();                       // дисковый I/O (RT-11 EMT)
            E("        .PSECT\tDATA, RW, D");
            EmitDiskIOData();                   // буферы/имя для I/O
            E("RNDSEED: .WORD\t12345.");          // зерно генератора случайных
            E("VSINIT: .WORD\t0");                // вектор 100 перехвачен?
            E("; --- данные ppu_init: массив параметров канала 2 ---");
            E("PPMSG2: .WORD\tPPARR2");           // указатель на массив
            E("        .WORD\t177777");           // стоп-слово
            E("PPARR2: .BYTE\t0");                // return value (0=OK)
            E("PPCMD2B:.BYTE\t1");                // команда массива (allocate/copy/run)
            E("        .WORD\t32");                // тип устройства = ПП (32 ВОСЬМЕРИЧНОЕ!)
            E("PPAPP2: .WORD\t0");                // адрес ОЗУ ПП (вернёт ПП)
            E("PPACP2: .WORD\t0");                // адрес ОЗУ ЦП
            E("PPLEN2: .WORD\t0");                // длина в словах
            E("PPADR2: .WORD\t0");                // сохранённый адрес ПП
            E("; --- переменные протокола резидентного ПП (общая память) ---");
            E("PPCMD2: .WORD\t0");                // команда резиденту: 0/1/177777
            E("SQHEAD: .WORD\t0");                // очередь: указатель записи (ЦП)
            E("SQTAIL: .WORD\t0");                // очередь: указатель чтения (ПП)
            E("SQBUF:  .BLKW\t48.");              // очередь: 16 слотов × 3 слова
            E("PPPX:   .WORD\t0");                // x точки / x0 линии
            E("PPPY:   .WORD\t0");                // y точки / y0 линии
            E("PPPC:   .WORD\t0");                // цвет
            E("PPLX1:  .WORD\t0");                // x1 линии
            E("PPLY1:  .WORD\t0");                // y1 линии
            E("PPSX:   .WORD\t0");                // x спрайта
            E("PPSY:   .WORD\t0");                // y спрайта
            E("PPSW:   .WORD\t0");                // ширина (пиксели, кратно 8)
            E("PPSH:   .WORD\t0");                // высота (строк)
            E("PPSPTR: .WORD\t0");                // адрес данных спрайта (в словах)
            E("PPON:   .WORD\t0");                // 1 = резидент ПП запущен (для cls)
            E("PPKEY:  .WORD\t0");                // код клавиши от резидента (0 = нет)
            E("PPFLIP: .WORD\t0");                // 1 = следующий вывод PPS3 — зеркально
            E("PPUCNT: .WORD\t0");                // сколько спрайтов загружено в ПП
            E("PPUW:   .WORD\t0");                // ширина загружаемого спрайта
            E("PPUHT:  .WORD\t0");                // его высота
            E("PPUSRC: .WORD\t0");                // адрес массива в памяти ЦП                // адрес массива в памяти ЦП
            E("PPUTBL: .BLKW\t48.");              // 16 спрайтов: адрес ПП, ширина, высота
            E("PPKHLD: .WORD\t0");                // код удерживаемой клавиши (0 = отпущена)
            E("KBLAST: .WORD\t0");                // клавиша, для которой идёт автоповтор
            E("KBCNT:  .WORD\t0");                // сколько опросов осталось до повтора
            E("KBDLY:  .WORD\t12.");              // пауза до первого повтора, опросов
            E("KBRATE: .WORD\t2.");               // период повторов, опросов
            E("; Скан-коды клавиатуры ПП (регистр 177702) → коды, привычные");
            E("; для режима ЦП. Пары: скан, значение; 0 = конец таблицы.");
            E("KBMAP:  .WORD\t113, 32.");         // ПРОБЕЛ
            E("        .WORD\t154, 65.");         // стрелка вверх
            E("        .WORD\t134, 66.");         // стрелка вниз
            E("        .WORD\t133, 67.");         // стрелка вправо
            E("        .WORD\t116, 68.");         // стрелка влево
            E("        .WORD\t153, 13.");         // ВВОД
            E("        .WORD\t166, 13.");         // ВВОД (доп. поле)
            E("        .WORD\t132, 8.");          // ЗБ (забой)
            E("        .WORD\t0, 0");             // конец
            E("HBUF:   .BLKW\t8.");               // буфер строки для spr() 8цв (слова2)
            E("PPBSX:  .WORD\t0");                // блит: x источника
            E("PPBSY:  .WORD\t0");                // блит: y источника
            E("PPBW:   .WORD\t0");                // блит: ширина (пиксели)
            E("PPBH:   .WORD\t0");                // блит: высота
            E("PPBDX:  .WORD\t0");                // блит: x приёмника
            E("PPBDY:  .WORD\t0");                // блит: y приёмника
            E("PPKA:   .WORD\t0");                // peek: адрес
            E("PPKV:   .WORD\t0");                // peek: результат
            E("VSFLAG: .WORD\t0");                // флаг кадра (vsync)
            E("VSCNT:  .WORD\t0");                // счётчик кадров (getTimer)
            E("OLDV:   .WORD\t0");                // старый обработчик вектора 100
            E("SPBUF:  .BLKW\t15.");           // буфер строки спрайта (14 слов + хвост)
            E("        .EVEN");
            E("        .PSECT\tCODE, RO, I");
            E("");
        }
    }
}