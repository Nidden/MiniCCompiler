using System;
using System.IO;

namespace CompMacro11
{
    // ── Доступ к содержимому образа ───────────────────────────────────
    //   Всё выше (DskImage, HddImage) работает с «диском» — линейным
    //   набором байт. Чем он является на самом деле, знает только этот
    //   слой: сейчас это обычный файл (.dsk, .hdd, .img). Если когда-то
    //   понадобится контейнер (образ внутри образа), добавляется он
    //   здесь, а всё остальное менять не придётся.
    public abstract class DiskFile : IDisposable
    {
        public string Path { get; protected set; }
        public abstract long Length { get; }                  // размер данных диска
        public abstract void Read(long off, byte[] buf, int index, int count);
        public abstract void Write(long off, byte[] buf, int index, int count);
        public virtual void Dispose() { }
        public abstract string Describe();      // для окна «Сведения об образе»

        public static DiskFile Open(string path)
        {
            return new RawDiskFile(path);
        }

        // прочитать кусок в новый массив (короче — если файл кончился)
        public byte[] ReadChunk(long off, int count)
        {
            long avail = Length - off;
            if (avail <= 0) return new byte[0];
            int n = (int)Math.Min(count, avail);
            var buf = new byte[n];
            Read(off, buf, 0, n);
            return buf;
        }
    }

    // ── обычный файл ──────────────────────────────────────────────────
    public class RawDiskFile : DiskFile
    {
        long _len;

        public RawDiskFile(string path)
        {
            Path = path;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                _len = fs.Length;
        }

        public override long Length { get { return _len; } }

        public override string Describe()
        {
            return "Контейнер: обычный файл\nРазмер: " + _len + " байт (" + (_len / 512) + " блоков)";
        }

        public override void Read(long off, byte[] buf, int index, int count)
        {
            using (var fs = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                fs.Seek(off, SeekOrigin.Begin);
                int done = 0;
                while (done < count)
                {
                    int n = fs.Read(buf, index + done, count - done);
                    if (n <= 0) break;
                    done += n;
                }
            }
        }

        public override void Write(long off, byte[] buf, int index, int count)
        {
            using (var fs = new FileStream(Path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
            {
                fs.Seek(off, SeekOrigin.Begin);
                fs.Write(buf, index, count);
            }
            _len = Math.Max(_len, off + count);
        }
    }
}
