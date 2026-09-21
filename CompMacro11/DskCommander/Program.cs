using System;
using System.Windows.Forms;

namespace DskCommander
{
    internal static class Program
    {
        // Точка входа отдельной версии файлового менеджера.
        // Аргумент командной строки (необязательный) — путь к образу .dsk.
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string path = args.Length > 0 ? args[0] : null;
            Application.Run(new DskDialog(path));
        }
    }
}
