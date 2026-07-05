using System;
using System.Collections.Generic;
using System.IO;

namespace CompMacro11
{
    // ── Работа с образом диска RT-11 (.dsk) ──────────────────────────
    //   Читает каталог, добавляет/извлекает/удаляет файлы. Формат RT-11:
    //   образ = блоки по 512 байт; каталог с блока 6, сегменты по 2 блока.
    //   Заголовок сегмента: 5 слов (total, next, last, extra, start_blk).
    //   Запись: 7 слов (статус, имя1..3 RAD50, длина_блоков, job, дата).
    //   Статус: 512=EMPTY, 1024=PERM, 256=TENT, 2048=конец сегмента.
    public class DskImage
    {
        public class Entry
        {
            public bool Empty;              // пустая область (дыра)
            public string Name;             // "NAME.EXT" (для PERM)
            public int Blocks;              // длина в блоках
            public int StartBlock;          // физический блок начала данных
            public int RawStatus;           // сырой статус-код
        }

        private byte[] _data;
        public string Path { get; private set; }
        public int TotalBlocks => _data.Length / 512;

        private const int SEG0 = 6;         // первый блок каталога
        private const string R50 = " ABCDEFGHIJKLMNOPQRSTUVWXYZ$.%0123456789";

        public DskImage(string path)
        {
            Path = path;
            _data = File.ReadAllBytes(path);
        }

        // ── низкоуровневый доступ к словам ──
        private ushort GetW(int byteOff) =>
            (ushort)(_data[byteOff] | (_data[byteOff + 1] << 8));
        private void SetW(int byteOff, ushort v)
        {
            _data[byteOff] = (byte)(v & 0xFF);
            _data[byteOff + 1] = (byte)(v >> 8);
        }

        // ── RAD50 ──
        private static string R50Word(ushort w)
        {
            char c3 = R50[w % 40]; w /= 40;
            char c2 = R50[w % 40]; w /= 40;
            return "" + R50[w % 40] + c2 + c3;
        }
        private static int R50Code(char ch)
        {
            ch = char.ToUpper(ch);
            if (ch == ' ') return 0;
            if (ch >= 'A' && ch <= 'Z') return ch - 'A' + 1;
            if (ch == '$') return 27;
            if (ch == '.') return 28;
            if (ch >= '0' && ch <= '9') return ch - '0' + 30;
            return 0;
        }
        private static ushort Pack3(string s)
        {
            s = (s + "   ").Substring(0, 3);
            return (ushort)((R50Code(s[0]) * 40 + R50Code(s[1])) * 40 + R50Code(s[2]));
        }
        // "NAME.EXT" → 3 слова RAD50 (имя1, имя2, тип)
        private static ushort[] PackName(string name)
        {
            string fn = name, ext = "";
            int dot = name.IndexOf('.');
            if (dot >= 0) { fn = name.Substring(0, dot); ext = name.Substring(dot + 1); }
            fn = (fn + "      ").Substring(0, 6);
            return new[] { Pack3(fn.Substring(0, 3)), Pack3(fn.Substring(3, 3)), Pack3(ext) };
        }

        // ── чтение каталога (только первый сегмент — как в типичных образах) ──
        //   Возвращает список записей в порядке следования на диске.
        public List<Entry> ReadDir()
        {
            var list = new List<Entry>();
            int segBase = SEG0 * 512;
            int startBlk = GetW(segBase + 8);       // слово 4 = start_blk данных
            int off = segBase + 10;                  // после 5 слов заголовка
            int blk = startBlk;
            while (off + 14 <= segBase + 1024)
            {
                ushort st = GetW(off);
                if (st == 0 || (st & 2048) != 0) break;   // конец каталога
                int len = GetW(off + 8);
                var e = new Entry { RawStatus = st, Blocks = len, StartBlock = blk };
                if ((st & 512) != 0)
                    e.Empty = true;
                else
                {
                    string nm = R50Word(GetW(off + 2)) + R50Word(GetW(off + 4));
                    string ex = R50Word(GetW(off + 6));
                    e.Name = nm.TrimEnd() + "." + ex.TrimEnd();
                }
                list.Add(e);
                blk += len;
                off += 14;
            }
            return list;
        }

        public int FreeBlocks()
        {
            int f = 0;
            foreach (var e in ReadDir()) if (e.Empty) f += e.Blocks;
            return f;
        }

        // ── извлечь файл в массив байт ──
        public byte[] Extract(string name, int byteLen = -1)
        {
            foreach (var e in ReadDir())
                if (!e.Empty && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    int len = byteLen < 0 ? e.Blocks * 512 : byteLen;
                    var outb = new byte[len];
                    Array.Copy(_data, e.StartBlock * 512, outb, 0, Math.Min(len, e.Blocks * 512));
                    return outb;
                }
            return null;
        }

        // ── добавить файл (байты). Ищет EMPTY-область достаточного размера,
        //    при необходимости сливая соседние пустые записи. ──
        //    Возвращает true при успехе. Если файл с таким именем есть — удаляет старый.
        public bool AddFile(string name, byte[] bytes)
        {
            name = name.ToUpper();
            DeleteFile(name);                        // перезапись, если существует

            int needBlocks = (bytes.Length + 511) / 512;
            var dir = ReadDir();

            // найти EMPTY-цепочку подряд идущих пустых записей суммой >= needBlocks
            int startIdx = -1, sumBlocks = 0, startBlk = 0;
            for (int i = 0; i < dir.Count; i++)
            {
                if (dir[i].Empty)
                {
                    if (startIdx < 0) { startIdx = i; sumBlocks = 0; startBlk = dir[i].StartBlock; }
                    sumBlocks += dir[i].Blocks;
                    if (sumBlocks >= needBlocks) break;
                }
                else { startIdx = -1; sumBlocks = 0; }
            }
            if (startIdx < 0 || sumBlocks < needBlocks) return false;   // нет места

            // сколько записей-дыр слить
            int endIdx = startIdx;
            int acc = 0;
            for (int i = startIdx; i < dir.Count; i++)
            {
                acc += dir[i].Blocks; endIdx = i;
                if (acc >= needBlocks) break;
            }

            // перестроить список записей: [..до дыры] PERM(файл) [EMPTY остаток] [..после]
            var rebuilt = new List<Entry>();
            for (int i = 0; i < startIdx; i++) rebuilt.Add(dir[i]);
            rebuilt.Add(new Entry { Empty = false, Name = name, Blocks = needBlocks, StartBlock = startBlk });
            int rest = acc - needBlocks;
            if (rest > 0)
                rebuilt.Add(new Entry { Empty = true, Blocks = rest, StartBlock = startBlk + needBlocks });
            for (int i = endIdx + 1; i < dir.Count; i++) rebuilt.Add(dir[i]);

            // записать данные файла
            int dataOff = startBlk * 512;
            Array.Clear(_data, dataOff, needBlocks * 512);
            Array.Copy(bytes, 0, _data, dataOff, bytes.Length);

            WriteDir(rebuilt);
            File.WriteAllBytes(Path, _data);
            return true;
        }

        // ── удалить файл: PERM → EMPTY (данные не трогаем) ──
        public bool DeleteFile(string name)
        {
            var dir = ReadDir();
            bool found = false;
            foreach (var e in dir)
                if (!e.Empty && string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
                { e.Empty = true; e.Name = null; found = true; }
            if (found) { WriteDir(dir); File.WriteAllBytes(Path, _data); }
            return found;
        }

        // ── слить соседние EMPTY-записи (уплотнение каталога) ──
        private static void MergeEmpty(List<Entry> dir)
        {
            for (int i = dir.Count - 2; i >= 0; i--)
                if (dir[i].Empty && dir[i + 1].Empty)
                {
                    dir[i].Blocks += dir[i + 1].Blocks;
                    dir.RemoveAt(i + 1);
                }
        }

        // ── записать каталог (первый сегмент) обратно в образ ──
        private void WriteDir(List<Entry> dir)
        {
            MergeEmpty(dir);
            int segBase = SEG0 * 512;
            int off = segBase + 10;
            foreach (var e in dir)
            {
                ushort st = e.Empty ? (ushort)512 : (ushort)1024;
                ushort n1 = 0, n2 = 0, n3 = 0;
                if (!e.Empty)
                {
                    var p = PackName(e.Name);
                    n1 = p[0]; n2 = p[1]; n3 = p[2];
                }
                SetW(off, st);
                SetW(off + 2, n1);
                SetW(off + 4, n2);
                SetW(off + 6, n3);
                SetW(off + 8, (ushort)e.Blocks);
                SetW(off + 10, 0);           // job
                SetW(off + 12, 0);           // дата
                off += 14;
                if (off + 14 > segBase + 1024) break;   // сегмент заполнен
            }
            SetW(off, 2048);               // маркер конца каталога
            off += 2;
            // обнулить хвост сегмента
            for (int p = off; p < segBase + 1024; p++) _data[p] = 0;
        }
    }
}
