using System;
using System.Collections.Generic;
using System.Text;

namespace CompMacro11
{
    // ── Дисковый ввод-вывод через стандартные системные вызовы RT-11 ──
    //    Реализовано прямыми EMT 375 (без .MCall-макросов, чтобы не
    //    зависеть от макробиблиотеки RT-11 при сборке).
    //
    //    C-функции:
    //      fload(name, buf, maxwords) → слов прочитано (или -1 при ошибке)
    //      fsave(name, buf, words)    → 0 успех / -1 ошибка
    //      fdelete(name)              → 0 успех / -1 ошибка
    //      frename(old, new)          → 0 успех / -1 ошибка
    //      mkdir(name)                → 0 успех / -1 ошибка
    //      getcwd(buf)                → 0 успех / -1 ошибка
    //      chdir(path)                → 0 успех / -1 ошибка
    //      file_info(name, buf)       → 0 успех / -1 ошибка (buf[0]=размер, buf[1]=блоки)
    //      print_buf(buf, nbytes)     → печать байтов из буфера
    //      str_to_buf(str, buf)       → строка → буфер, R0 = длина
    public partial class CodeGen
    {
        // Эмитит рантайм дискового I/O. Вызывается в конце обоих EmitRuntime*.
        private void EmitDiskIO()
        {
            E("; ============================================================");
            E("; ДИСКОВЫЙ I/O — стандартные системные вызовы RT-11 (EMT 375)");
            E("; ============================================================");

            EmitRTR50();
            EmitRTFLOAD();
            EmitRTFSAVE();
            EmitRTFDEL();
            EmitRTFREN();
            EmitRTMKDIR();
            EmitRTGETCWD();
            EmitRTCHDIR();
            EmitRTFINFO();
            EmitRTPBUF();
            EmitRTSTBUF();
            EmitRTDUMY();
            EmitRTFDBG();
        }

        // ── RTR50: разбор C-строки имени в блок RAD50 FNAME (4 слова) ──
        private void EmitRTR50()
        {
            E("; RTR50 — C-строка (R0) → блок имени RAD50 в FNAME (4 слова).");
            E("RTR50:");
            E("        MOV\tR1, -(SP)");
            E("        MOV\tR2, -(SP)");
            E("        MOV\tR3, -(SP)");
            E("        MOV\tR4, -(SP)");
            E("        MOV\t#FNAME, R1");
            E("        CLR\t(R1)+");
            E("        CLR\t(R1)+");
            E("        CLR\t(R1)+");
            E("        CLR\t(R1)");
            E("        MOV\tR0, R2");
            E("        MOV\tR0, R3");
            E("RTR5CS: TSTB\t(R3)");
            E("        BEQ\tRTR5ND");
            E("        CMPB\t(R3), #72");
            E("        BEQ\tRTR5DV");
            E("        INC\tR3");
            E("        MOV\tR3, R4");
            E("        SUB\tR2, R4");
            E("        CMP\tR4, #4.");
            E("        BLT\tRTR5CS");
            E("RTR5ND:");
            E("        MOV\t#RTDKDV, R0");
            E("        JSR\tPC, RTR5W");
            E("        BR\tRTR5NM");
            E("RTR5DV:");
            E("        MOV\tR2, R0");
            E("        JSR\tPC, RTR5W");
            E("        INC\tR0");
            E("        MOV\tR0, R2");
            E("RTR5NM:");
            E("        MOV\tR4, FNAME");
            E("        MOV\tR2, R0");
            E("        JSR\tPC, RTR5W");
            E("        MOV\tR4, FNAME+2");
            E("        JSR\tPC, RTR5W");
            E("        MOV\tR4, FNAME+4");
            E("RTR5FT: TSTB\t(R0)");
            E("        BEQ\tRTR5FX");
            E("        CMPB\t(R0), #56");
            E("        BEQ\tRTR5FD");
            E("        INC\tR0");
            E("        BR\tRTR5FT");
            E("RTR5FD: INC\tR0");
            E("        JSR\tPC, RTR5W");
            E("        MOV\tR4, FNAME+6");
            E("RTR5FX:");
            E("        MOV\t(SP)+, R4");
            E("        MOV\t(SP)+, R3");
            E("        MOV\t(SP)+, R2");
            E("        MOV\t(SP)+, R1");
            E("        RTS\tPC");

            E("; RTR5W — до 3 символов (R0)→ слово RAD50 в R4.");
            E("RTR5W:");
            E("        MOV\tR1, -(SP)");
            E("        MOV\tR2, -(SP)");
            E("        MOV\tR3, -(SP)");
            E("        CLR\tR4");
            E("        MOV\t#3, R3");
            E("RTR5WL: TSTB\t(R0)");
            E("        BEQ\tRTR5WP");
            E("        CMPB\t(R0), #56");
            E("        BEQ\tRTR5WP");
            E("        CMPB\t(R0), #72");
            E("        BEQ\tRTR5WP");
            E("        MOVB\t(R0), R1");
            E("        JSR\tPC, RTR5V");
            E("        MOV\tR4, R2");
            E("        ASL\tR4");
            E("        ASL\tR4");
            E("        ASL\tR4");
            E("        MOV\tR4, -(SP)");
            E("        ASL\tR4");
            E("        ASL\tR4");
            E("        ADD\t(SP)+, R4");
            E("        ADD\tR1, R4");
            E("        INC\tR0");
            E("        DEC\tR3");
            E("        BNE\tRTR5WL");
            E("        BR\tRTR5WX");
            E("RTR5WP:");
            E("RTR5WF: TST\tR3");
            E("        BEQ\tRTR5WX");
            E("        MOV\tR4, R2");
            E("        ASL\tR4");
            E("        ASL\tR4");
            E("        ASL\tR4");
            E("        MOV\tR4, -(SP)");
            E("        ASL\tR4");
            E("        ASL\tR4");
            E("        ADD\t(SP)+, R4");
            E("        DEC\tR3");
            E("        BR\tRTR5WF");
            E("RTR5WX:");
            E("        MOV\t(SP)+, R3");
            E("        MOV\t(SP)+, R2");
            E("        MOV\t(SP)+, R1");
            E("        RTS\tPC");

            E("; RTR5V — ASCII символ (R1) → код RAD50 (R1).");
            E("RTR5V:");
            E("        CMPB\tR1, #40");
            E("        BNE\tRTR5V1");
            E("        CLR\tR1");
            E("        RTS\tPC");
            E("RTR5V1: CMPB\tR1, #101");
            E("        BLT\tRTR5V2");
            E("        CMPB\tR1, #132");
            E("        BGT\tRTR5V2");
            E("        SUB\t#100, R1");
            E("        RTS\tPC");
            E("RTR5V2: CMPB\tR1, #141");
            E("        BLT\tRTR5V3");
            E("        CMPB\tR1, #172");
            E("        BGT\tRTR5V3");
            E("        SUB\t#140, R1");
            E("        RTS\tPC");
            E("RTR5V3: CMPB\tR1, #60");
            E("        BLT\tRTR5V4");
            E("        CMPB\tR1, #71");
            E("        BGT\tRTR5V4");
            E("        SUB\t#60, R1");
            E("        ADD\t#36, R1");
            E("        RTS\tPC");
            E("RTR5V4: CMPB\tR1, #44");
            E("        BNE\tRTR5V5");
            E("        MOV\t#33, R1");
            E("        RTS\tPC");
            E("RTR5V5: MOV\t#34, R1");
            E("        RTS\tPC");
            E("");
        }

        // ── RTFLOAD — fload(name, buf, maxwords) ──────────────────────
        private void EmitRTFLOAD()
        {
            E("; RTFLOAD — fload(name,buf,maxwords): загрузить файл в буфер.");
            E("; Возвращает число прочитанных слов в R0, или -1 при ошибке.");
            E("RTFLOAD:");
            E("        MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\tR1, -(SP)");
            E("        MOV\tR2, -(SP)");
            E("        MOV\t4.(R5), R0");
            E("        JSR\tPC, RTR50");
            E("        MOV\t#<1*400>, EMTBLK");
            E("        MOV\t#FNAME, EMTBLK+2");
            E("        CLR\tEMTBLK+4");
            E("        MOV\t#EMTBLK, R0");
            E("        EMT\t375");
            E("        BCS\tRTFLER");
            E("        MOV\t#<10*400>, EMTBLK");
            E("        CLR\tEMTBLK+2");
            E("        MOV\t6.(R5), EMTBLK+4");
            E("        MOV\t8.(R5), EMTBLK+6");
            E("        CLR\tEMTBLK+10");
            E("        MOV\t#EMTBLK, R0");
            E("        EMT\t375");
            E("RTFLER:");
            E("        BCC\tRTFLOK");
            E("        MOV\t#-1, R0");
            E("RTFLOK:");
            E("        MOV\t(SP)+, R2");
            E("        MOV\t(SP)+, R1");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            E("");
        }

        // ── RTFSAVE — fsave(name, buf, words) ─────────────────────────
        private void EmitRTFSAVE()
        {
            E("; RTFSAVE — fsave(name,buf,words): записать буфер в файл.");
            E("RTFSAVE:");
            E("        MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\tR1, -(SP)");
            E("        MOV\tR2, -(SP)");
            E("        MOV\t4.(R5), R0");
            E("        JSR\tPC, RTR50");
            E("        MOV\t8.(R5), R0");
            E("        ADD\t#377, R0");
            E("        ASH\t#-8., R0");
            E("        MOV\tR0, R1");
            E("        MOV\t#<2*400>, EMTBLK");
            E("        MOV\t#FNAME, EMTBLK+2");
            E("        MOV\tR1, EMTBLK+4");
            E("        CLR\tEMTBLK+6");
            E("        MOV\t#EMTBLK, R0");
            E("        EMT\t375");
            E("        BCS\tRTFSER");
            E("        MOV\t#<11*400>, EMTBLK");
            E("        CLR\tEMTBLK+2");
            E("        MOV\t6.(R5), EMTBLK+4");
            E("        MOV\t8.(R5), EMTBLK+6");
            E("        CLR\tEMTBLK+10");
            E("        MOV\t#EMTBLK, R0");
            E("        EMT\t375");
            E("        BCS\tRTFSER");
            E("        MOV\t#<6*400>, R0");
            E("        EMT\t374");
            E("        CLR\tR0");
            E("        BR\tRTFSX");
            E("RTFSER: MOV\t#-1, R0");
            E("RTFSX:");
            E("        MOV\t(SP)+, R2");
            E("        MOV\t(SP)+, R1");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            E("");
        }

        // ── RTFDEL — fdelete(name): удалить файл ──────────────────────
        private void EmitRTFDEL()
        {
            E("; RTFDEL — fdelete(name): удалить файл.");
            E("; Вход: 2.(SP)=адрес имени (ASCIZ). Выход: R0 = 0 успех / -1 ошибка.");
            E("RTFDEL:");
            E("        MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\tR1, -(SP)");
            E("        MOV\t4.(R5), R0");
            E("        JSR\tPC, RTR50");
            E("        MOV\t#<5*400>, EMTBLK");
            E("        MOV\t#FNAME, EMTBLK+2");
            E("        MOV\t#EMTBLK, R0");
            E("        EMT\t375");
            E("        BCC\tRTFDLX");
            E("        MOV\t#-1, R0");
            E("        BR\tRTFDL2");
            E("RTFDLX: CLR\tR0");
            E("RTFDL2: MOV\t(SP)+, R1");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            E("");
        }

        // ── RTFREN — frename(old,new): переименовать файл ──────────────
        private void EmitRTFREN()
        {
            E("; RTFREN — frename(old,new): переименовать файл.");
            E("; Вход: 4.(R5)=old, 6.(R5)=new. Выход: R0 = 0 успех / -1 ошибка.");
            E("RTFREN:");
            E("        MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\tR1, -(SP)");
            E("        MOV\tR2, -(SP)");
            E("        MOV\t4.(R5), R0");
            E("        JSR\tPC, RTR50");
            E("        MOV\t#FNAME, R2");
            E("        MOV\t(R2)+, -(SP)");
            E("        MOV\t(R2)+, -(SP)");
            E("        MOV\t(R2)+, -(SP)");
            E("        MOV\t(R2), -(SP)");
            E("        MOV\t6.(R5), R0");
            E("        JSR\tPC, RTR50");
            E("        MOV\t#FNAME, R1");
            E("        MOV\t(SP)+, (R1)+");
            E("        MOV\t(SP)+, (R1)+");
            E("        MOV\t(SP)+, (R1)+");
            E("        MOV\t(SP)+, (R1)");
            E("        MOV\t#<4*400>, EMTBLK");
            E("        MOV\t#FNAME, EMTBLK+2");
            E("        MOV\t#EMTBLK, R0");
            E("        EMT\t375");
            E("        BCC\tRTFRX");
            E("        MOV\t#-1, R0");
            E("        BR\tRTFR2");
            E("RTFRX: CLR\tR0");
            E("RTFR2: MOV\t(SP)+, R2");
            E("        MOV\t(SP)+, R1");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            E("");
        }

        // ── RTMKDIR — mkdir(name): создать каталог ─────────────────────
        private void EmitRTMKDIR()
        {
            E("; RTMKDIR — mkdir(name): создать каталог.");
            E("; Вход: 2.(SP)=адрес имени. Выход: R0 = 0 успех / -1 ошибка.");
            E("RTMKDIR:");
            E("        MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\tR1, -(SP)");
            E("        MOV\t4.(R5), R0");
            E("        JSR\tPC, RTR50");
            E("        MOV\t#<14*400>, EMTBLK");
            E("        MOV\t#FNAME, EMTBLK+2");
            E("        MOV\t#0, EMTBLK+4");
            E("        MOV\t#EMTBLK, R0");
            E("        EMT\t375");
            E("        BCC\tRTMDX");
            E("        MOV\t#-1, R0");
            E("        BR\tRTMD2");
            E("RTMDX: CLR\tR0");
            E("RTMD2: MOV\t(SP)+, R1");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            E("");
        }

        // ── RTGETCWD — getcwd(buf): получить текущий каталог ──────────
        private void EmitRTGETCWD()
        {
            E("; RTGETCWD — getcwd(buf): получить текущий каталог.");
            E("; Вход: 2.(SP)=адрес буфера. Выход: R0 = 0 успех / -1 ошибка.");
            E("RTGETCWD:");
            E("        MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\tR1, -(SP)");
            E("        MOV\tR2, -(SP)");
            E("        MOV\t4.(R5), R1");
            E("        JSR\tPC, RTDUMY");
            E("        MOV\t#CWDNAME, R0");
            E("RTGC1:  MOVB\t(R0)+, R2");
            E("        BEQ\tRTGC2");
            E("        MOVB\tR2, (R1)+");
            E("        BR\tRTGC1");
            E("RTGC2:  CLRB\t(R1)");
            E("        CLR\tR0");
            E("        MOV\t(SP)+, R2");
            E("        MOV\t(SP)+, R1");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            E("");
        }

        // ── RTCHDIR — chdir(path): сменить каталог ─────────────────────
        private void EmitRTCHDIR()
        {
            E("; RTCHDIR — chdir(path): сменить каталог.");
            E("; Вход: 2.(SP)=адрес пути. Выход: R0 = 0 успех / -1 ошибка.");
            E("RTCHDIR:");
            E("        MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\tR1, -(SP)");
            E("        MOV\t4.(R5), R0");
            E("        JSR\tPC, RTR50");
            E("        MOV\t#<15*400>, EMTBLK");
            E("        MOV\t#FNAME, EMTBLK+2");
            E("        MOV\t#EMTBLK, R0");
            E("        EMT\t375");
            E("        BCC\tRTCDX");
            E("        MOV\t#-1, R0");
            E("        BR\tRTCD2");
            E("RTCDX: CLR\tR0");
            E("RTCD2: MOV\t(SP)+, R1");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            E("");
        }

        // ── RTFINFO — file_info(name,buf): информация о файле ──────────
        private void EmitRTFINFO()
        {
            E("; RTFINFO — file_info(name,buf): получить информацию о файле.");
            E("; Вход: 4.(R5)=имя, 6.(R5)=буфер (4 слова).");
            E("; Выход: buf[0]=размер_байт, buf[1]=блоки, buf[2]=тип, buf[3]=дата.");
            E("; R0 = 0 успех / -1 ошибка.");
            E("RTFINFO:");
            E("        MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\tR1, -(SP)");
            E("        MOV\tR2, -(SP)");
            E("        MOV\t4.(R5), R0");
            E("        JSR\tPC, RTR50");
            E("        MOV\t#<17*400>, EMTBLK");
            E("        MOV\t#FNAME, EMTBLK+2");
            E("        MOV\t#FINFBUF, EMTBLK+4");
            E("        MOV\t#EMTBLK, R0");
            E("        EMT\t375");
            E("        BCS\tRTFIX");
            E("        MOV\t6.(R5), R1");
            E("        MOV\tFINFBUF+0, (R1)+");
            E("        MOV\tFINFBUF+2, (R1)+");
            E("        MOV\tFINFBUF+4, (R1)+");
            E("        MOV\tFINFBUF+6, (R1)");
            E("        CLR\tR0");
            E("        BR\tRTFI2");
            E("RTFIX:  MOV\t#-1, R0");
            E("RTFI2:  MOV\t(SP)+, R2");
            E("        MOV\t(SP)+, R1");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            E("");
        }

        // ── RTPBUF — print_buf(buf,nbytes): печать байтов из буфера ────
        private void EmitRTPBUF()
        {
            E("; RTPBUF — print_buf(buf,nbytes): печать текста из буфера слов.");
            E("RTPBUF:");
            E("        MOV\tR5, -(SP)");
            E("        MOV\tSP, R5");
            E("        MOV\tR1, -(SP)");
            E("        MOV\tR2, -(SP)");
            E("        MOV\t4.(R5), R1");
            E("        MOV\t6.(R5), R2");
            E("RTPB1:  TST\tR2");
            E("        BLE\tRTPBX");
            E("        MOVB\t(R1)+, R0");
            E("RTPB2:  TSTB\t@#177564");
            E("        BPL\tRTPB2");
            E("        MOV\tR0, @#177566");
            E("        DEC\tR2");
            E("        BR\tRTPB1");
            E("RTPBX:  MOV\t(SP)+, R2");
            E("        MOV\t(SP)+, R1");
            E("        MOV\t(SP)+, R5");
            E("        RTS\tPC");
            E("");
        }

        // ── RTSTBUF — строка ASCIZ (R1) → буфер (R2) ──────────────────
        private void EmitRTSTBUF()
        {
            E("; RTSTBUF — строка (R1) → буфер (R2). Возвращает длину в R0.");
            E("RTSTBUF:");
            E("        MOV\tR3, -(SP)");
            E("        CLR\tR0");
            E("RTSTB1: MOVB\t(R1), R3");
            E("        BEQ\tRTSTB2");
            E("        MOVB\t(R1)+, (R2)+");
            E("        INC\tR0");
            E("        BR\tRTSTB1");
            E("RTSTB2: BIT\t#1, R0");
            E("        BEQ\tRTSTB3");
            E("        CLRB\t(R2)+");
            E("        INC\tR0");
            E("RTSTB3: MOV\t(SP)+, R3");
            E("        RTS\tPC");
            E("");
        }

        // ── RTDUMY — заглушка для getcwd (возвращает DK0:) ────────────
        private void EmitRTDUMY()
        {
            E("; RTDUMY — заглушка для getcwd (возвращает DK0:).");
            E("RTDUMY: MOV\t#CWDNAME, R0");
            E("        MOVB\t#'D, (R0)+");
            E("        MOVB\t#'K, (R0)+");
            E("        MOVB\t#'0, (R0)+");
            E("        MOVB\t#':, (R0)+");
            E("        CLRB\t(R0)");
            E("        RTS\tPC");
            E("");
        }

        // ── RTFDBG — отладочный вывод (временный) ──────────────────────
        private void EmitRTFDBG()
        {
            E("; RTFDBG — отладочный маркер стадии I/O (символ в R1).");
            E("RTFDBG: MOV\tR0, -(SP)");
            E("RTFDB1: TSTB\t@#177564");
            E("        BPL\tRTFDB1");
            E("        MOVB\tR1, @#177566");
            E("        MOV\t(SP)+, R0");
            E("        RTS\tPC");
            E("");
        }

        // ── Данные дискового I/O (в DATA-секции обоих рантаймов) ─────
        private void EmitDiskIOData()
        {
            E("FNAME:  .BLKW\t4.");                 // имя файла в RAD50 (устр,имя1,имя2,тип)
            E("EMTBLK: .BLKW\t6.");                 // блок аргументов EMT 375
            E("RTDKDV: .ASCIZ\t\"DK \"");            // устройство по умолчанию
            E("CWDNAME:.BLKW\t16.");                // буфер для getcwd
            E("FINFBUF:.BLKW\t4.");                 // буфер для file_info
            E("        .EVEN");
        }
    }
}