using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace CompMacro11
{
    // ─── Диалог "Новый проект" ───────────────────────────────────────
    public class NewProjectDialog : Form
    {
        public string ProjectName => txtName.Text.Trim();
        public string ProjectFolder => txtFolder.Text.Trim();

        private TextBox txtName;
        private TextBox txtFolder;

        public NewProjectDialog()
        {
            Text = "Новый проект";
            Size = new Size(440, 220);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(37, 37, 38);
            ForeColor = Color.White;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;

            var font = new Font("Segoe UI", 10);
            var fontB = new Font("Segoe UI", 10, FontStyle.Bold);

            // Название
            var lblName = new Label { Text = "Название проекта:", Font = font, ForeColor = Color.LightGray, Location = new Point(16, 20), AutoSize = true };
            txtName = new TextBox { Font = font, BackColor = Color.FromArgb(50, 50, 55), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Location = new Point(16, 42), Size = new Size(390, 26), Text = "МойПроект" };

            // Папка
            var lblFolder = new Label { Text = "Папка:", Font = font, ForeColor = Color.LightGray, Location = new Point(16, 80), AutoSize = true };
            txtFolder = new TextBox { Font = font, BackColor = Color.FromArgb(50, 50, 55), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Location = new Point(16, 102), Size = new Size(320, 26) };
            txtFolder.Text = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "\\CompMacro11Projects";

            var btnBrowse = new Button { Text = "...", Font = fontB, BackColor = Color.FromArgb(60, 60, 65), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Location = new Point(344, 101), Size = new Size(62, 28) };
            btnBrowse.FlatAppearance.BorderSize = 0;
            btnBrowse.Click += (_, __) =>
            {
                var dlg = new FolderBrowserDialog { SelectedPath = txtFolder.Text };
                if (dlg.ShowDialog() == DialogResult.OK) txtFolder.Text = dlg.SelectedPath;
            };

            // Кнопки
            var btnOk = new Button { Text = "Создать", Font = fontB, BackColor = Color.FromArgb(0, 122, 204), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Location = new Point(230, 148), Size = new Size(88, 32), DialogResult = DialogResult.OK };
            btnOk.FlatAppearance.BorderSize = 0;
            var btnCancel = new Button { Text = "Отмена", Font = font, BackColor = Color.FromArgb(60, 60, 65), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Location = new Point(326, 148), Size = new Size(80, 32), DialogResult = DialogResult.Cancel };
            btnCancel.FlatAppearance.BorderSize = 0;

            AcceptButton = btnOk;
            CancelButton = btnCancel;

            Controls.AddRange(new Control[] { lblName, txtName, lblFolder, txtFolder, btnBrowse, btnOk, btnCancel });
        }
    }

    // ─── Панель "Последние проекты" (стартовый экран) ────────────────
    public class StartScreen : Form
    {
        public string SelectedProject { get; private set; }
        public bool WantsNew { get; private set; }
        public bool WantsOpen { get; private set; }

        private ListBox lstRecent;

        public StartScreen()
        {
            Text = "Mini-C → Macro-11  |  Выбор проекта";
            Size = new Size(600, 460);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(30, 30, 30);
            ForeColor = Color.White;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;

            var fontB = new Font("Segoe UI", 11, FontStyle.Bold);
            var font  = new Font("Segoe UI", 10);
            var fontS = new Font("Segoe UI", 9);

            // Заголовок
            var lblTitle = new Label
            {
                Text = "Mini-C → Macro-11 Compiler",
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                ForeColor = Color.FromArgb(0, 180, 255),
                Location = new Point(20, 20),
                AutoSize = true
            };
            var lblSub = new Label
            {
                Text = "Компилятор для УКНЦ (PDP-11)",
                Font = font,
                ForeColor = Color.Gray,
                Location = new Point(22, 50),
                AutoSize = true
            };

            // Кнопки действий
            var btnNew = MakeBtn("＋  Новый проект", Color.FromArgb(0, 122, 204), 20, 90);
            btnNew.Click += (_, __) => { WantsNew = true; DialogResult = DialogResult.OK; Close(); };

            var btnOpen = MakeBtn("📂  Открыть проект...", Color.FromArgb(60, 60, 65), 170, 90);
            btnOpen.Click += (_, __) => { WantsOpen = true; DialogResult = DialogResult.OK; Close(); };

            // Список последних
            var lblRecent = new Label
            {
                Text = "Последние проекты:",
                Font = fontB,
                ForeColor = Color.LightGray,
                Location = new Point(20, 150),
                AutoSize = true
            };

            lstRecent = new ListBox
            {
                Font = font,
                BackColor = Color.FromArgb(40, 40, 45),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.None,
                Location = new Point(20, 178),
                Size = new Size(545, 200),
                ItemHeight = 28
            };

            // Заполняем последние проекты
            var recent = RecentProjects.Load();
            foreach (var r in recent)
                lstRecent.Items.Add(r);

            if (lstRecent.Items.Count == 0)
                lstRecent.Items.Add("(нет последних проектов)");

            lstRecent.DoubleClick += (_, __) => OpenSelected();

            var btnOpenSel = MakeBtn("Открыть выбранный", Color.FromArgb(0, 122, 204), 20, 390);
            btnOpenSel.Click += (_, __) => OpenSelected();

            var btnSkip = MakeBtn("Пропустить", Color.FromArgb(60, 60, 65), 180, 390);
            btnSkip.Click += (_, __) => { DialogResult = DialogResult.Cancel; Close(); };

            Controls.AddRange(new Control[] { lblTitle, lblSub, btnNew, btnOpen, lblRecent, lstRecent, btnOpenSel, btnSkip });
        }

        private void OpenSelected()
        {
            if (lstRecent.SelectedItem == null) return;
            string path = lstRecent.SelectedItem.ToString();
            if (!File.Exists(path)) { MessageBox.Show("Файл не найден:\n" + path); return; }
            SelectedProject = path;
            DialogResult = DialogResult.OK;
            Close();
        }

        private Button MakeBtn(string text, Color bg, int x, int y)
        {
            var btn = new Button
            {
                Text = text,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                BackColor = bg,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(x, y),
                Size = new Size(145, 38),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }
    }
}
