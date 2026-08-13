using System;
using System.Collections.Generic;
using System.Text;

namespace CompMacro11
{
    // ── Дисковый ввод-вывод через стандартные системные вызовы RT-11 ──
    //    Реализовано прямыми EMT 375 (без .MCall-макросов, чтобы не
    //    зависеть от макробиблиотеки RT-11 при сборке). Общий модуль:
    //    вызывается из обоих рантаймов (ЦП и Game) — I/O не зависит от режима.
    //
    //    C-функции:
    //      fload(name, buf, maxwords) → слов прочитано (или -1 при ошибке)
    //      fsave(name, buf, words)    → 0 успех / -1 ошибка
    //    name — обычная C-строка "DK:SPLASH.SPR" (или "SPLASH.SPR" — устр. DK по умолчанию)
    public partial class CodeGen
    {
        // Эмитит рантайм дискового I/O. Вызывается в конце обоих EmitRuntime*.
        private void EmitDiskIO()
        {
            E("; ============================================================");
            E("; ДИСКОВЫЙ I/O — стандартные системные вызовы RT-11 (EMT 375)");
            E("; ============================================================");

            // ── RTR50: разбор C-строки имени в блок RAD50 FNAME (4 слова) ──
            //   Вход: R0 = адрес строки (ASCIZ). Формат "УСТ:ИМЯ.ТИП" или "ИМЯ.ТИП".
            //   Выход: FNAME[0]=устройство, [1..2]=имя, [3]=тип (Radix-50).
            //   По умолчанию устройство "DK".
            E("; RTR50 — C-строка (R0) → блок имени RAD50 в FNAME (4 слова).");
            E("RTR50:");
            E("        MOV\tR1, -(SP)");
            E("        MOV\tR2, -(SP)");
            E("        MOV\tR3, -(SP)");
            E("        MOV\tR4, -(SP)");
            // очистить FNAME
            E("        MOV\t#FNAME, R1");
            E("        CLR\t(R1)+");
            E("        CLR\t(R1)+");
            E("        CLR\t(R1)+");
            E("        CLR\t(R1)");
            // проверить есть ли "УСТ:" — ищем ':' в первых 3-4 символах
            E("        MOV\tR0, R2");             // R2 = начало строки
            E("        MOV\tR0, R3");             // R3 = сканер
            E("RTR5CS: TSTB\t(R3)");              // конец строки?
            E("        BEQ\tRTR5ND");             // ':' не найден — устройство по умолч.
            E("        CMPB\t(R3), #72");         // ':' = 072
            E("        BEQ\tRTR5DV");             // найдено устройство
            E("        INC\tR3");
            E("        MOV\tR3, R4");
            E("        SUB\tR2, R4");
            E("        CMP\tR4, #4.");            // не дальше 4 символов
            E("        BLT\tRTR5CS");
            E("RTR5ND:");
            // устройство по умолчанию "DK " → упаковать константной строкой
            E("        MOV\t#RTDKDV, R0");         // адрес "DK "
            E("        JSR\tPC, RTR5W");           // → R4
            E("        BR\tRTR5NM");
            E("RTR5DV:");
            // упаковать устройство [R2..R3) в FNAME[0]
            E("        MOV\tR2, R0");             // с начала до ':'
            E("        JSR\tPC, RTR5W");          // упаковать до 3 симв → R4
            E("        INC\tR0");                 // пропустить ':'
            E("        MOV\tR0, R2");
            E("RTR5NM:");
            E("        MOV\tR4, FNAME");          // устройство
            // имя: символы до '.' → FNAME[1], FNAME[2]
            E("        MOV\tR2, R0");
            E("        JSR\tPC, RTR5W");
            E("        MOV\tR4, FNAME+2");
            E("        JSR\tPC, RTR5W");
            E("        MOV\tR4, FNAME+4");
            // пропустить до '.' и упаковать тип → FNAME[3]
            E("RTR5FT: TSTB\t(R0)");
            E("        BEQ\tRTR5FX");
            E("        CMPB\t(R0), #56");         // '.' = 056
            E("        BEQ\tRTR5FD");
            E("        INC\tR0");
            E("        BR\tRTR5FT");
            E("RTR5FD: INC\tR0");                 // пропустить '.'
            E("        JSR\tPC, RTR5W");
            E("        MOV\tR4, FNAME+6");
            E("RTR5FX:");
            E("        MOV\t(SP)+, R4");
            E("        MOV\t(SP)+, R3");
            E("        MOV\t(SP)+, R2");
            E("        MOV\t(SP)+, R1");
            E("        RTS\tPC");

            // ── RTR5W: упаковать до 3 символов из строки (R0) в слово RAD50 (R4) ──
            //   R0 продвигается. Стоп на '.', ':', конце строки или 3 символах.
            //   Использует таблицу RAD50T.
            E("; RTR5W — до 3 символов (R0)→ слово RAD50 в R4. R0 продвигается.");
            E("RTR5W:");
            E("        MOV\tR1, -(SP)");
            E("        MOV\tR2, -(SP)");
            E("        MOV\tR3, -(SP)");
            E("        CLR\tR4");                 // аккумулятор
            E("        MOV\t#3, R3");             // счётчик символов
            E("RTR5WL: TSTB\t(R0)");
            E("        BEQ\tRTR5WP");             // конец строки
            E("        CMPB\t(R0), #56");
            E("        BEQ\tRTR5WP");             // '.'
            E("        CMPB\t(R0), #72");
            E("        BEQ\tRTR5WP");             // ':'
            // код символа → индекс RAD50 через RTR5V
            E("        MOVB\t(R0), R1");
            E("        JSR\tPC, RTR5V");          // R1 = RAD50-код символа
            // R4 = R4*50(окт) + R1.  50окт=40дес=32+8 → (R4<<5)+(R4<<3)
            E("        MOV\tR4, R2");             // R2 = R4
            E("        ASL\tR4");
            E("        ASL\tR4");
            E("        ASL\tR4");                 // R4 = R2<<3  (*8)
            E("        MOV\tR4, -(SP)");          // сохранить *8
            E("        ASL\tR4");
            E("        ASL\tR4");                 // R4 = R2<<5  (*32)
            E("        ADD\t(SP)+, R4");          // R4 = *32 + *8 = *40
            E("        ADD\tR1, R4");             // + код символа
            E("        INC\tR0");
            E("        DEC\tR3");
            E("        BNE\tRTR5WL");
            E("        BR\tRTR5WX");
            E("RTR5WP:");
            // добить оставшиеся позиции нулями (пробел=0): R4 = R4*40 нужное число раз
            E("RTR5WF: TST\tR3");
            E("        BEQ\tRTR5WX");
            E("        MOV\tR4, R2");
            E("        ASL\tR4");
            E("        ASL\tR4");
            E("        ASL\tR4");                 // *8
            E("        MOV\tR4, -(SP)");
            E("        ASL\tR4");
            E("        ASL\tR4");                 // *32
            E("        ADD\t(SP)+, R4");          // *40
            E("        DEC\tR3");
            E("        BR\tRTR5WF");
            E("RTR5WX:");
            E("        MOV\t(SP)+, R3");
            E("        MOV\t(SP)+, R2");
            E("        MOV\t(SP)+, R1");
            E("        RTS\tPC");

            // ── RTR5V: символ (R1) → код RAD50 (R1). ──
            //   ' '=0, A-Z=1..32(окт), $=33, .=34, 0-9=36..47(окт).
            E("; RTR5V — ASCII символ (R1) → код RAD50 (R1).");
            E("RTR5V:");
            E("        CMPB\tR1, #40");           // ' ' (пробел 040)
            E("        BNE\tRTR5V1");
            E("        CLR\tR1");
            E("        RTS\tPC");
            E("RTR5V1: CMPB\tR1, #101");          // 'A' = 0101
            E("        BLT\tRTR5V2");
            E("        CMPB\tR1, #132");           // 'Z' = 0132
            E("        BGT\tRTR5V2");
            E("        SUB\t#100, R1");           // A→1
            E("        RTS\tPC");
            E("RTR5V2: CMPB\tR1, #141");          // 'a' = 0141
            E("        BLT\tRTR5V3");
            E("        CMPB\tR1, #172");           // 'z'
            E("        BGT\tRTR5V3");
            E("        SUB\t#140, R1");           // a→1 (строчные как заглавные)
            E("        RTS\tPC");
            E("RTR5V3: CMPB\tR1, #60");           // '0' = 060
            E("        BLT\tRTR5V4");
            E("        CMPB\tR1, #71");            // '9'
            E("        BGT\tRTR5V4");
            E("        SUB\t#60, R1");
            E("        ADD\t#36, R1");            // 0→36 (октально)
            E("        RTS\tPC");
            E("RTR5V4: CMPB\tR1, #44");           // '$' = 044
            E("        BNE\tRTR5V5");
            E("        MOV\t#33, R1");
            E("        RTS\tPC");
            E("RTR5V5: MOV\t#34, R1");            // '.' и всё прочее → 34
            E("        RTS\tPC");

            // ── RTFLOAD — fload(name, buf, maxwords) → R0 = прочитано слов / -1 ──
            E("; RTFLOAD — fload(name,buf,maxwords): загрузить файл в буфер.");
            E("; Возвращает число прочитанных слов в R0, или -1 при ошибке.");
            E("RTFLOAD:");
            E("        MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\tR1, -(SP)");
            E("        MOV\tR2, -(SP)");
            E("        MOV\t4.(R5), R0");          // name
            E("        JSR\tPC, RTR50");           // → FNAME
            // .LOOKUP канал 0
            E("        MOV\t#<1*400>, EMTBLK");     // старший байт=код 1, младший=канал 0
            E("        MOV\t#FNAME, EMTBLK+2");     // +2 = указатель на имя (dblk)
            E("        CLR\tEMTBLK+4");             // +4 = seqnum
            E("        MOV\t#EMTBLK, R0");
            E("        EMT\t375");
            E("        BCS\tRTFLER");               // ошибка открытия
            E("        MOVB\t#114, R1");             // отладка: 'L' = LOOKUP прошёл
            E("        JSR\tPC, RTFDBG");
            // .READW канал 0: блок 0, буфер, maxwords слов
            E("        MOV\t#<10*400>, EMTBLK");    // код 10(окт) | канал 0
            E("        CLR\tEMTBLK+2");             // блок 0
            E("        MOV\t6.(R5), EMTBLK+4");     // буфер
            E("        MOV\t8.(R5), EMTBLK+6");     // maxwords
            E("        CLR\tEMTBLK+10");             // 0 = синхронный W-режим (эталон RT-11)
            E("        MOV\t#EMTBLK, R0");
            E("        EMT\t375");
            E("        BCS\tRTFLC");                // EOF/ошибка чтения — но данные могли прочесть
            E("RTFLC:");
            // R0 после .READW = число реально прочитанных слов
            E("        MOV\tR0, -(SP)");            // сохранить счётчик
            E("        MOVB\t#122, R1");             // отладка: 'R' = READW прошёл
            E("        JSR\tPC, RTFDBG");
            // .CLOSE канал 0 — короткий вызов EMT 374 (R0 = код 6<<8 | канал)
            E("        MOV\t#<6*400>, R0");
            E("        EMT\t374");
            E("        MOV\t(SP)+, R0");            // вернуть счётчик прочитанного
            E("        BR\tRTFLX");
            E("RTFLER: MOV\t#-1, R0");              // -1: файл не найден
            E("RTFLX:");
            E("        MOV\t(SP)+, R2");
            E("        MOV\t(SP)+, R1");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");

            // ── RTFSAVE — fsave(name, buf, words) → R0 = 0 успех / -1 ──
            E("; RTFSAVE — fsave(name,buf,words): записать буфер в файл.");
            E("RTFSAVE:");
            E("        MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\tR1, -(SP)");
            E("        MOV\tR2, -(SP)");
            E("        MOV\t4.(R5), R0");           // name
            E("        JSR\tPC, RTR50");
            // .ENTER канал 0: размер файла в блоках = (words+255)/256
            E("        MOV\t8.(R5), R0");           // words
            E("        ADD\t#377, R0");
            E("        ASH\t#-8., R0");             // слова → блоки RT-11 (блок = 256 слов)
            E("        MOV\tR0, R1");               // R1 = блоков
            E("        MOV\t#<2*400>, EMTBLK");     // код 2 (.ENTER), канал 0
            E("        MOV\t#FNAME, EMTBLK+2");     // +2 = указатель на имя (dblk)
            E("        MOV\tR1, EMTBLK+4");         // +4 = длина в блоках
            E("        CLR\tEMTBLK+6");             // +6 = seqnum
            E("        MOV\t#EMTBLK, R0");
            E("        EMT\t375");
            E("        BCS\tRTFSER");
            E("        MOVB\t#105, R1");             // отладка: 'E' = ENTER прошёл
            E("        JSR\tPC, RTFDBG");
            // .WRITW канал 0
            E("        MOV\t#<11*400>, EMTBLK");    // код 11(окт) | канал 0
            E("        CLR\tEMTBLK+2");             // блок 0
            E("        MOV\t6.(R5), EMTBLK+4");     // буфер
            E("        MOV\t8.(R5), EMTBLK+6");     // words
            E("        CLR\tEMTBLK+10");             // 0 = синхронный W-режим (эталон RT-11)
            E("        MOV\t#EMTBLK, R0");
            E("        EMT\t375");
            E("        BCS\tRTFSER");
            E("        MOVB\t#127, R1");             // отладка: 'W' = WRITW прошёл
            E("        JSR\tPC, RTFDBG");
            // .CLOSE — короткий EMT 374
            E("        MOV\t#<6*400>, R0");
            E("        EMT\t374");
            E("        MOVB\t#103, R1");             // отладка: 'C' = CLOSE прошёл
            E("        JSR\tPC, RTFDBG");
            E("        CLR\tR0");                   // успех
            E("        BR\tRTFSX");
            E("RTFSER: MOV\t#-1, R0");
            E("RTFSX:");
            E("        MOV\t(SP)+, R2");
            E("        MOV\t(SP)+, R1");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");

            // ── RTFDBG — ВРЕМЕННАЯ отладка: печать символа-стадии из R1 ──
            E("; RTFDBG — отладочный маркер стадии I/O (символ в R1). ВРЕМЕННО.");
            E("RTFDBG: MOV\tR0, -(SP)");
            E("RTFDB1: TSTB\t@#177564");
            E("        BPL\tRTFDB1");
            E("        MOVB\tR1, @#177566");
            E("        MOV\t(SP)+, R0");
            E("        RTS\tPC");
            E("");
            // ── RTPBUF — print_buf(buf, nbytes): печать байтов из буфера ──
            //   Буфер хранит текст словами (2 байта/слово). Печатаем nbytes
            //   байт через терминал. Для показа текста, прочитанного из файла.
            E("; RTPBUF — print_buf(buf,nbytes): печать текста из буфера слов.");
            E("RTPBUF:");
            E("        MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\tR1, -(SP)");
            E("        MOV\tR2, -(SP)");
            E("        MOV\t4.(R5), R1");           // buf
            E("        MOV\t6.(R5), R2");           // nbytes
            E("RTPB1:  TST\tR2");
            E("        BLE\tRTPBX");
            E("        MOVB\t(R1)+, R0");           // очередной байт
            E("RTPB2:  TSTB\t@#177564");            // ждать готовности терминала
            E("        BPL\tRTPB2");
            E("        MOV\tR0, @#177566");         // вывести
            E("        DEC\tR2");
            E("        BR\tRTPB1");
            E("RTPBX:  MOV\t(SP)+, R2");
            E("        MOV\t(SP)+, R1");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");

            // ── RTSTBUF — строка ASCIZ (R1) → буфер (R2). R0 = число байт ──
            //   Копирует байты строки в буфер. Чётная длина дополняется 0
            //   (для записи целыми словами). Возвращает число байт в R0.
            E("; RTSTBUF — строка (R1) → буфер (R2). Возвращает длину в R0.");
            E("RTSTBUF:");
            E("        MOV\tR3, -(SP)");
            E("        CLR\tR0");                   // счётчик
            E("RTSTB1: MOVB\t(R1), R3");
            E("        BEQ\tRTSTB2");               // конец строки
            E("        MOVB\t(R1)+, (R2)+");
            E("        INC\tR0");
            E("        BR\tRTSTB1");
            E("RTSTB2:");                            // дополнить до чётного
            E("        BIT\t#1, R0");
            E("        BEQ\tRTSTB3");
            E("        CLRB\t(R2)+");
            E("        INC\tR0");
            E("RTSTB3: MOV\t(SP)+, R3");
            E("        RTS\tPC");
        }

        // Данные дискового I/O (в DATA-секции обоих рантаймов).
        private void EmitDiskIOData()
        {
            E("FNAME:  .BLKW\t4.");                 // имя файла в RAD50 (устр,имя1,имя2,тип)
            E("EMTBLK: .BLKW\t6.");                 // блок аргументов EMT 375
            E("RTDKDV: .ASCIZ\t\"MZ \"");           // устройство по умолчанию (диск УКНЦ)
            E("        .EVEN");
        }
    }
}
