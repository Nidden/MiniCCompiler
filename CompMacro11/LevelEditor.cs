using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace CompMacro11
{
    // ══════════════════════════════════════════════════════════════
    //  Формат клетки поля — как в Battle City (подтверждено разбором
    //  формата игры): 13x13 клеток, каждая клетка 16x16 пикселей
    //  РЕАЛЬНО состоит из четырёх независимых четвертей 8x8
    //  (TL,TR,BL,BR). Кирпич можно обрушивать по четверти за раз —
    //  выстрел сбивает конкретный угол. Остальные материалы
    //  (сталь/вода/кусты/лёд) четвертями не делятся — красятся и
    //  стираются только целой клеткой.
    //
    //  Один байт на клетку: младшие 4 бита — какие четверти заняты
    //  (TL=1,TR=2,BL=4,BR=8), старшие биты — материал (0=пусто,
    //  1=кирпич, 2=сталь, 3=вода, 4=кусты, 5=лёд).
    // ══════════════════════════════════════════════════════════════
    public static class Terrain
    {
        public const int Empty = 0, Brick = 1, Steel = 2, Water = 3, Trees = 4, Ice = 5;
        public static readonly string[] Names = { "Пусто", "Кирпич", "Сталь", "Вода", "Кусты", "Лёд" };
        public static readonly Color[] Colors =
        {
            Color.Black,
            Color.FromArgb(180, 90, 40),    // кирпич
            Color.Silver,                    // сталь
            Color.DodgerBlue,                // вода
            Color.ForestGreen,               // кусты
            Color.LightCyan                  // лёд
        };
        public const int QTL = 1, QTR = 2, QBL = 4, QBR = 8, QALL = 15;

        // Делимость своя у каждого материала — не общее правило на все.
        // Подтверждено: кирпич и сталь — делимые (можно закрасить/сбить
        // по одной четверти), кусты — нет, только целой клеткой. Вода и
        // лёд пока не подтверждены — по умолчанию тоже целой клеткой,
        // как более безопасное предположение, пока не скажете иначе.
        public static readonly bool[] Divisible =
        {
            false, // Пусто — всегда целиком, отдельный случай, сюда не относится
            true,  // Кирпич
            true,  // Сталь
            false, // Вода
            false, // Кусты
            false  // Лёд
        };

        // Настоящие текстуры материалов — подгружаются из папки Tiles рядом
        // с файлом уровней, по имени материала. У делимых материалов может
        // быть до четырёх РАЗНЫХ картинок — по одной на угол (TL/TR/BL/BR),
        // не одна растянутая на все четверти. Порядок поиска для угла:
        // своя угловая картинка → общая Full.png того же материала,
        // растянутая именно в эту четверть → цветной прямоугольник.
        // Так и заполняется редактор постепенно — просто кладёте файлы
        // с нужными именами в папку, без привязки кнопками.
        public static readonly Image[][] QuadTextures = new Image[6][];   // [материал][0=TL,1=TR,2=BL,3=BR]
        public static readonly Image[] FullTexture = new Image[6];        // запасной вариант под ЛЮБОЙ один угол
        public static readonly Image[] SolidTexture = new Image[6];       // FF.png — вся клетка целиком, только когда заняты все 4 четверти
        static readonly string[] QuadFileNames = { "TL.png", "TR.png", "BL.png", "BR.png" };

        public static void RefreshFromFolder(string tilesDir)
        {
            for (int m = 0; m <= Ice; m++)
            {
                FullTexture[m]?.Dispose();
                FullTexture[m] = null;
                SolidTexture[m]?.Dispose();
                SolidTexture[m] = null;
                if (QuadTextures[m] != null)
                    foreach (var img in QuadTextures[m]) img?.Dispose();
                QuadTextures[m] = new Image[4];

                if (string.IsNullOrEmpty(tilesDir)) continue;
                string matDir = System.IO.Path.Combine(tilesDir, Names[m]);
                if (!System.IO.Directory.Exists(matDir)) continue;

                string fullPath = System.IO.Path.Combine(matDir, "Full.png");
                if (System.IO.File.Exists(fullPath))
                    FullTexture[m] = LoadNoLock(fullPath);

                string ffPath = System.IO.Path.Combine(matDir, "FF.png");
                if (System.IO.File.Exists(ffPath))
                    SolidTexture[m] = LoadNoLock(ffPath);

                for (int q = 0; q < 4; q++)
                {
                    string qPath = System.IO.Path.Combine(matDir, QuadFileNames[q]);
                    if (System.IO.File.Exists(qPath))
                        QuadTextures[m][q] = LoadNoLock(qPath);
                }
            }
        }

        // Image.FromFile() держит файл открытым на диске, пока картинка не
        // выгружена, — тогда её нельзя перезаписать снаружи (другим
        // редактором, экспортом и т.п.), пока наш редактор открыт. Читаем
        // байты в память и создаём картинку из памяти — файл на диске
        // после этого свободен сразу же.
        static Image LoadNoLock(string path)
        {
            try
            {
                byte[] bytes = System.IO.File.ReadAllBytes(path);
                var ms = new System.IO.MemoryStream(bytes);
                return Image.FromStream(ms);   // ms не закрываем — Image читает из него лениво
            }
            catch { return null; }
        }

        // Картинка для конкретного угла конкретного материала, с учётом
        // запасного варианта — или null, если нет вообще ничего (тогда
        // рисуется цвет).
        public static Image PickQuadImage(int material, int quadIndex)
        {
            if (material < 0 || material > Ice) return null;
            var own = QuadTextures[material]?[quadIndex];
            if (own != null) return own;
            return FullTexture[material];
        }
    }

    public class LevelMap
    {
        public const int Size = 13;
        public string Name;
        public int[] Material = new int[Size * Size];   // Terrain.* на клетку
        public int[] Quads = new int[Size * Size];       // биты занятых четвертей (только для кирпича значим)
        public int BaseX = 6, BaseY = 12;                 // клетка базы (орла)
        public List<Point> SpawnPoints = new List<Point>();       // клетки появления ВРАГОВ
        public List<Point> PlayerSpawnPoints = new List<Point>(); // клетки появления ИГРОКА(ов) — отдельно, как в оригинале
        public List<int> BonusSlots = new List<int>();            // номера врагов ПО ОЧЕРЕДИ появления (1-й, 2-й...), несущие бонус — не каждый враг, а именно эти, мигающие

        // ── Настройки уровня, не связанные с самой картой ──
        public int TotalTanks = 20;      // всего врагов за уровень
        public int TankBasic = 20;       // состав по типам — можно не совпадать с
        public int TankFast = 0;         // TotalTanks, компилятор/игра сама решает,
        public int TankArmor = 0;        // как трактовать несовпадение
        public int TankHeavy = 0;
        public int MaxOnScreen = 4;      // одновременно на поле
        public int PlayerLives = 3;

        public LevelMap(string name = "level")
        {
            Name = name;
            for (int i = 0; i < Material.Length; i++) { Material[i] = Terrain.Empty; Quads[i] = 0; }
        }

        public int Get(int cx, int cy) => Material[cy * Size + cx];
        public int GetQuads(int cx, int cy) => Quads[cy * Size + cx];

        // Красит ОДНУ четверть указанным материалом — но только если он
        // делимый (Terrain.Divisible). Неделимый материал (и «Пусто»)
        // красится/стирается сразу целой клеткой, одним кликом.
        public void PaintQuad(int cx, int cy, int quadBit, int material)
        {
            int i = cy * Size + cx;
            if (material == Terrain.Empty || !Terrain.Divisible[material])
            {
                if (Material[i] == material) { Material[i] = Terrain.Empty; Quads[i] = 0; }   // клик по уже занятой — стереть
                else { Material[i] = material; Quads[i] = Terrain.QALL; }
                return;
            }
            if (Material[i] != material) { Material[i] = material; Quads[i] = 0; }
            Quads[i] ^= quadBit;
            if (Quads[i] == 0) Material[i] = Terrain.Empty;
        }

        // Упаковка: старший нибл — материал, младший — четверти.
        public int Pack(int cx, int cy) => (Material[cy * Size + cx] << 4) | Quads[cy * Size + cx];

        public string Serialize()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"NAME={Name}");
            sb.AppendLine($"BASE={BaseX},{BaseY}");
            sb.Append("SPAWN=");
            for (int i = 0; i < SpawnPoints.Count; i++)
                sb.Append((i > 0 ? ";" : "") + SpawnPoints[i].X + "," + SpawnPoints[i].Y);
            sb.AppendLine();
            sb.Append("PSPAWN=");
            for (int i = 0; i < PlayerSpawnPoints.Count; i++)
                sb.Append((i > 0 ? ";" : "") + PlayerSpawnPoints[i].X + "," + PlayerSpawnPoints[i].Y);
            sb.AppendLine();
            sb.AppendLine("BONUS=" + string.Join(",", BonusSlots));
            sb.AppendLine($"SETTINGS={TotalTanks},{TankBasic},{TankFast},{TankArmor},{TankHeavy},{MaxOnScreen},{PlayerLives}");
            sb.AppendLine("DATA=" + string.Join(",", Material) + "|" + string.Join(",", Quads));
            return sb.ToString();
        }

        public static LevelMap Deserialize(string block)
        {
            var m = new LevelMap();
            foreach (var line in block.Split('\n'))
            {
                if (line.StartsWith("NAME=")) m.Name = line.Substring(5).Trim();
                else if (line.StartsWith("BASE="))
                {
                    var p = line.Substring(5).Trim().Split(',');
                    if (p.Length == 2) { int.TryParse(p[0], out m.BaseX); int.TryParse(p[1], out m.BaseY); }
                }
                else if (line.StartsWith("SPAWN="))
                {
                    m.SpawnPoints.Clear();
                    var txt = line.Substring(6).Trim();
                    if (txt.Length > 0)
                        foreach (var pair in txt.Split(';'))
                        {
                            var p = pair.Split(',');
                            if (p.Length == 2 && int.TryParse(p[0], out int x) && int.TryParse(p[1], out int y))
                                m.SpawnPoints.Add(new Point(x, y));
                        }
                }
                else if (line.StartsWith("PSPAWN="))
                {
                    m.PlayerSpawnPoints.Clear();
                    var txt = line.Substring(7).Trim();
                    if (txt.Length > 0)
                        foreach (var pair in txt.Split(';'))
                        {
                            var p = pair.Split(',');
                            if (p.Length == 2 && int.TryParse(p[0], out int x) && int.TryParse(p[1], out int y))
                                m.PlayerSpawnPoints.Add(new Point(x, y));
                        }
                }
                else if (line.StartsWith("BONUS="))
                {
                    m.BonusSlots.Clear();
                    var txt = line.Substring(6).Trim();
                    if (txt.Length > 0)
                        foreach (var s in txt.Split(','))
                            if (int.TryParse(s, out int v)) m.BonusSlots.Add(v);
                }
                else if (line.StartsWith("SETTINGS="))
                {
                    var p = line.Substring(9).Trim().Split(',');
                    if (p.Length == 7)
                    {
                        int.TryParse(p[0], out m.TotalTanks);
                        int.TryParse(p[1], out m.TankBasic);
                        int.TryParse(p[2], out m.TankFast);
                        int.TryParse(p[3], out m.TankArmor);
                        int.TryParse(p[4], out m.TankHeavy);
                        int.TryParse(p[5], out m.MaxOnScreen);
                        int.TryParse(p[6], out m.PlayerLives);
                    }
                }
                else if (line.StartsWith("DATA="))
                {
                    var halves = line.Substring(5).Trim().Split('|');
                    var mat = halves[0].Split(',');
                    for (int i = 0; i < mat.Length && i < m.Material.Length; i++)
                        int.TryParse(mat[i], out m.Material[i]);
                    if (halves.Length > 1)
                    {
                        var qd = halves[1].Split(',');
                        for (int i = 0; i < qd.Length && i < m.Quads.Length; i++)
                            int.TryParse(qd[i], out m.Quads[i]);
                    }
                }
            }
            return m;
        }

        // Экспорт: один int на клетку, упаковано материал+четверти.
        // Плюс отдельно позиция базы и точки спауна — тем же массивом,
        // последними тремя записями, чтобы не городить второй экспорт.
        // Максимумы для переменных по длине частей уровня — структура
        // требует фиксированного размера поля, поэтому резервируем с
        // запасом и отдельно храним, сколько реально использовано.
        public const int MaxSpawns = 4;
        public const int MaxPlayerSpawns = 2;
        public const int MaxBonus = 20;

        // Объявление типа struct Level — поля СТРОГО по порядку, что и
        // значения в ExportC(). Вызывается один раз на программу, не на
        // каждый уровень — сам уровень после этого просто struct Level.
        public static string ExportStructDecl()
        {
            var sb = new StringBuilder();
            sb.AppendLine("struct Level {");
            // Клетка — байт: материал 0..5 в старшем нибле, четверти в
            // младшем, больше 0137 не бывает. Поле занимает 169 байт
            // вместо 169 слов — на уровень 122 слова вместо 206.
            sb.AppendLine($"    byte cells[{Size * Size}];");
            sb.AppendLine("    int levelBase;");
            sb.AppendLine("    int spawnCount;");
            sb.AppendLine($"    int spawns[{MaxSpawns}];");
            sb.AppendLine("    int playerSpawnCount;");
            sb.AppendLine($"    int playerSpawns[{MaxPlayerSpawns}];");
            sb.AppendLine("    int bonusCount;");
            sb.AppendLine($"    int bonusSlots[{MaxBonus}];");
            sb.AppendLine("    int totalTanks;");
            sb.AppendLine("    int tankBasic;");
            sb.AppendLine("    int tankFast;");
            sb.AppendLine("    int tankArmor;");
            sb.AppendLine("    int tankHeavy;");
            sb.AppendLine("    int maxOnScreen;");
            sb.AppendLine("    int playerLives;");
            sb.AppendLine("};");
            return sb.ToString();
        }

        public string ExportC() { return ExportC(Name); }

        // varName — имя переменной в коде. Отличается от Name, когда имя
        // уровня в редакторе не годится в идентификаторы (кириллица,
        // пробелы, цифра первой): человеческое имя остаётся в комментарии.
        public string ExportC(string varName)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"// {Name}  {Size}x{Size}, кирпич — по четвертям (как в Battle City)");
            sb.AppendLine($"// формат клетки: старший нибл = материал(0..5), младший = четверти(TL=1,TR=2,BL=4,BR=8)");
            sb.AppendLine($"// порядок значений строго соответствует полям struct Level");
            sb.Append(Warnings());
            sb.AppendLine($"struct Level {varName} = {{");
            sb.Append(ValueLines("    "));
            sb.AppendLine("};");
            return sb.ToString();
        }

        // Предупреждения о том, что не влезло в фиксированные поля.
        public string Warnings()
        {
            var sb = new StringBuilder();
            if (SpawnPoints.Count > MaxSpawns) sb.AppendLine($"// ВНИМАНИЕ: точек спауна врагов {SpawnPoints.Count}, влезло только {MaxSpawns}");
            if (PlayerSpawnPoints.Count > MaxPlayerSpawns) sb.AppendLine($"// ВНИМАНИЕ: точек спауна игрока {PlayerSpawnPoints.Count}, влезло только {MaxPlayerSpawns}");
            if (BonusSlots.Count > MaxBonus) sb.AppendLine($"// ВНИМАНИЕ: бонусных танков {BonusSlots.Count}, влезло только {MaxBonus}");
            return sb.ToString();
        }

        // Значения уровня строками по 10, с заданным отступом. Последняя
        // строка без запятой — вызывающий сам решает, нужна ли она после
        // закрывающей скобки (элемент массива структур) или нет.
        public string ValueLines(string indent)
        {
            var vals = ExportValues();
            var sb = new StringBuilder();
            for (int i = 0; i < vals.Count; i += 10)
            {
                sb.Append(indent);
                for (int j = i; j < Math.Min(i + 10, vals.Count); j++)
                    sb.Append(vals[j] + (j < vals.Count - 1 ? ", " : ""));
                sb.AppendLine();
            }
            return sb.ToString();
        }

        // Значения уровня в порядке полей struct Level.
        public List<int> ExportValues()
        {
            var vals = new List<int>();
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    vals.Add(Pack(x, y));
            vals.Add(BaseX * 16 + BaseY);

            vals.Add(Math.Min(SpawnPoints.Count, MaxSpawns));
            for (int i = 0; i < MaxSpawns; i++)
                vals.Add(i < SpawnPoints.Count ? SpawnPoints[i].X * 16 + SpawnPoints[i].Y : 0);

            vals.Add(Math.Min(PlayerSpawnPoints.Count, MaxPlayerSpawns));
            for (int i = 0; i < MaxPlayerSpawns; i++)
                vals.Add(i < PlayerSpawnPoints.Count ? PlayerSpawnPoints[i].X * 16 + PlayerSpawnPoints[i].Y : 0);

            vals.Add(Math.Min(BonusSlots.Count, MaxBonus));
            for (int i = 0; i < MaxBonus; i++)
                vals.Add(i < BonusSlots.Count ? BonusSlots[i] : 0);

            vals.Add(TotalTanks); vals.Add(TankBasic); vals.Add(TankFast);
            vals.Add(TankArmor); vals.Add(TankHeavy); vals.Add(MaxOnScreen); vals.Add(PlayerLives);
            return vals;
        }
    }

    public class LevelEditor : Form
    {
        static readonly Color C_BG = Color.FromArgb(28, 28, 28);
        static readonly Color C_BG2 = Color.FromArgb(37, 37, 38);
        static readonly Color C_BG3 = Color.FromArgb(22, 22, 22);
        static readonly Color C_TEXT = Color.FromArgb(212, 212, 212);
        static readonly Color C_GRAY = Color.FromArgb(100, 100, 100);
        static readonly Color C_SEL = Color.FromArgb(0, 100, 180);

        // Обычный Panel не двойно-буферизован по умолчанию — при частой
        // перерисовке (клики, наведение) это моргание. DoubleBuffered у
        // Control защищённое свойство, снаружи не выставить — нужен
        // маленький подкласс.
        class BufferedPanel : Panel
        {
            public BufferedPanel()
            {
                DoubleBuffered = true;
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                       | ControlStyles.UserPaint, true);
            }
        }

        List<LevelMap> _levels = new List<LevelMap>();
        int _cur = 0;
        int _tool = Terrain.Brick;
        int _toolMode = 0;              // 0 = красим местность, 1 = ставим базу, 2 = точки спауна
        int _zoom = 3;                  // экранных пикселей на игровой пиксель (клетка 16px * zoom)
        Panel _canvas;
        FlowLayoutPanel _thumbPanel;
        ToolTip _toolTip = new ToolTip();
        Panel _palPanel;
        Action<string> _insertCode;
        string _levelsPath;
        Timer _autoSaveTimer;
        bool _dirty = false;

        public string LevelsPath
        {
            get => _levelsPath;
            set
            {
                _levelsPath = value;
                TryLoad();
                RefreshTiles();
            }
        }

        // Папка с картинками — Tiles рядом с файлом уровней. Пересчитывается
        // каждый раз при смене LevelsPath (открытие/смена проекта).
        void RefreshTiles()
        {
            if (string.IsNullOrEmpty(_levelsPath)) { Terrain.RefreshFromFolder(null); }
            else
            {
                string dir = Path.Combine(Path.GetDirectoryName(_levelsPath), "Tiles");
                Directory.CreateDirectory(dir);
                foreach (var name in Terrain.Names)
                    Directory.CreateDirectory(Path.Combine(dir, name));
                Terrain.RefreshFromFolder(dir);
            }
            _canvas?.Invalidate();
        }

        void OpenTilesFolder()
        {
            if (string.IsNullOrEmpty(_levelsPath)) return;
            string dir = Path.Combine(Path.GetDirectoryName(_levelsPath), "Tiles");
            Directory.CreateDirectory(dir);
            try { System.Diagnostics.Process.Start("explorer.exe", dir); } catch { }
        }

        public LevelEditor(Action<string> insertCode)
        {
            _insertCode = insertCode;
            Text = "Редактор уровней";
            BackColor = C_BG;
            ForeColor = C_TEXT;
            Width = 1050; Height = 760;
            MinimumSize = new Size(1000, 700);   // меньше — обрезает кнопки верхней панели
            StartPosition = FormStartPosition.CenterParent;

            if (_levels.Count == 0) _levels.Add(new LevelMap("level1"));

            var top = new Panel { Dock = DockStyle.Top, Height = 42, BackColor = C_BG2 };
            Controls.Add(top);
            int tx = 10;
            TBtn(top, "+ Новый", ref tx, C_BG3, () => NewLevel(), 90, "Создать пустой уровень");
            TBtn(top, "⧉ Копия", ref tx, C_BG3, () => DuplicateLevel(), 90, "Дублировать текущий уровень");
            TBtn(top, "🗑 Удалить", ref tx, C_BG3, () => DeleteLevel(), 90, "Удалить текущий уровень");
            TBtn(top, "✎ Имя", ref tx, C_BG3, () => RenameCurrent(), 90, "Переименовать текущий уровень");
            TSep(top, ref tx);
            TBtn(top, "⇩ Импорт", ref tx, C_BG3, () => ImportFromFile(), 90, "Добавить уровни из .lvl-файла (сразу в этот проект)");
            TSep(top, ref tx);
            TBtn(top, "⚙ Танки", ref tx, C_BG3, () => EditSettings(), 90, "Состав врагов, точки появления, жизни");
            TSep(top, ref tx);
            TBtn(top, "🔄 Картинки", ref tx, C_BG3, () => RefreshTiles(), 110, "Перечитать папку Tiles с диска");
            TBtn(top, "📁 Папка", ref tx, C_BG3, () => OpenTilesFolder(), 90, "Открыть папку Tiles в проводнике");
            TSep(top, ref tx);
            TBtn(top, "→ В код", ref tx, Color.FromArgb(0, 80, 50), () => ExportCode(), 90, "Вставить в код массив levels[], currentLevel и getLevel(num)");

            _palPanel = new Panel { Dock = DockStyle.Left, Width = 130, BackColor = C_BG2 };
            Controls.Add(_palPanel);
            BuildPalette();

            _thumbPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                Width = 140,
                BackColor = C_BG2,
                FlowDirection = FlowDirection.TopDown,
                AutoScroll = true,
                WrapContents = false
            };
            Controls.Add(_thumbPanel);

            _canvas = new BufferedPanel { Dock = DockStyle.Fill, BackColor = C_BG3 };
            _canvas.Paint += Canvas_Paint;
            _canvas.MouseDown += Canvas_MouseDown;
            Controls.Add(_canvas);
            _canvas.BringToFront();

            _autoSaveTimer = new Timer { Interval = 2000 };
            _autoSaveTimer.Tick += (s, e) => { _autoSaveTimer.Stop(); AutoSave(); };
            FormClosing += (s, e) => { _autoSaveTimer.Stop(); AutoSave(); };

            RefreshThumbs();
        }

        Button TBtn(Panel p, string t, ref int x, Color bg, Action click, int w = 90, string tip = null)
        {
            var b = new Button
            {
                Text = t,
                Left = x,
                Top = 6,
                Width = w,
                Height = 28,
                FlatStyle = FlatStyle.Flat,
                BackColor = bg,
                ForeColor = C_TEXT
            };
            b.FlatAppearance.BorderSize = 0;
            b.Click += (s, e) => click();
            p.Controls.Add(b);
            if (tip != null) _toolTip.SetToolTip(b, tip);
            x += w + 6;
            return b;
        }

        // Тонкая вертикальная черта между смысловыми группами кнопок —
        // чтобы «уровень», «текстуры» и «настройки» не сливались в один
        // ряд одинаковых прямоугольников.
        void TSep(Panel p, ref int x)
        {
            var line = new Panel { Left = x + 4, Top = 6, Width = 1, Height = 28, BackColor = C_GRAY };
            p.Controls.Add(line);
            x += 14;
        }

        void BuildPalette()
        {
            _palPanel.Controls.Clear();
            int y = 10;
            var lbl = new Label { Text = "МАТЕРИАЛ", Left = 8, Top = y, Width = 114, ForeColor = C_TEXT, Font = new Font(Font, FontStyle.Bold) };
            _palPanel.Controls.Add(lbl); y += 22;
            for (int t = 0; t <= Terrain.Ice; t++)
            {
                int tt = t;
                var b = new Button
                {
                    Text = Terrain.Names[t],
                    Left = 8,
                    Top = y,
                    Width = 114,
                    Height = 28,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = (_tool == t && _toolMode == 0) ? C_SEL : C_BG3,
                    ForeColor = C_TEXT
                };
                b.FlatAppearance.BorderColor = Terrain.Colors[t];
                b.FlatAppearance.BorderSize = 2;
                b.Click += (s, e) => { _tool = tt; _toolMode = 0; BuildPalette(); };
                _toolTip.SetToolTip(b, "Клик по клетке красит четверть. Картинка ищется в Tiles\\" + Terrain.Names[t]);
                _palPanel.Controls.Add(b);
                y += 32;
            }

            y += 6;
            _palPanel.Controls.Add(new Panel { Left = 8, Top = y, Width = 114, Height = 1, BackColor = C_GRAY });
            y += 14;

            var lbl2 = new Label { Text = "ИНСТРУМЕНТЫ", Left = 8, Top = y, Width = 114, ForeColor = C_TEXT, Font = new Font(Font, FontStyle.Bold) };
            _palPanel.Controls.Add(lbl2); y += 22;

            var b2 = new Button
            {
                Text = "⚑ База",
                Left = 8,
                Top = y,
                Width = 114,
                Height = 28,
                FlatStyle = FlatStyle.Flat,
                BackColor = _toolMode == 1 ? C_SEL : C_BG3,
                ForeColor = C_TEXT
            };
            b2.Click += (s, e) => { _toolMode = 1; BuildPalette(); };
            _toolTip.SetToolTip(b2, "Клик по клетке ставит базу (одна на уровень)");
            _palPanel.Controls.Add(b2); y += 32;

            var b3 = new Button
            {
                Text = "▲ Спаун врагов",
                Left = 8,
                Top = y,
                Width = 114,
                Height = 28,
                FlatStyle = FlatStyle.Flat,
                BackColor = _toolMode == 2 ? C_SEL : C_BG3,
                ForeColor = C_TEXT
            };
            b3.Click += (s, e) => { _toolMode = 2; BuildPalette(); };
            _toolTip.SetToolTip(b3, "Клик добавляет точку, клик по ней же — снимает");
            _palPanel.Controls.Add(b3); y += 32;

            var b4 = new Button
            {
                Text = "🚩 Спаун игрока",
                Left = 8,
                Top = y,
                Width = 114,
                Height = 28,
                FlatStyle = FlatStyle.Flat,
                BackColor = _toolMode == 3 ? C_SEL : C_BG3,
                ForeColor = C_TEXT
            };
            b4.Click += (s, e) => { _toolMode = 3; BuildPalette(); };
            _toolTip.SetToolTip(b4, "Клик добавляет точку, клик по ней же — снимает");
            _palPanel.Controls.Add(b4); y += 36;

            var hint = new Label
            {
                Text = "Кликайте по углу\nклетки, не в центр —\nкрасится одна\nчетверть. Клик по\nзанятой стирает её.",
                Left = 8,
                Top = y,
                Width = 114,
                Height = 90,
                ForeColor = C_GRAY
            };
            _palPanel.Controls.Add(hint);
        }

        void Canvas_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            int cell = 16 * _zoom;
            var lv = _levels[_cur];
            for (int cy = 0; cy < LevelMap.Size; cy++)
            {
                for (int cx = 0; cx < LevelMap.Size; cx++)
                {
                    int mat = lv.Get(cx, cy);
                    int quads = lv.GetQuads(cx, cy);
                    int px = cx * cell, py = cy * cell;
                    g.FillRectangle(new SolidBrush(C_BG3), px, py, cell, cell);
                    if (mat != Terrain.Empty)
                    {
                        var solid = quads == Terrain.QALL ? Terrain.SolidTexture[mat] : null;
                        if (solid != null)
                        {
                            g.DrawImage(solid, new Rectangle(px, py, cell, cell));
                        }
                        else
                        {
                            var br = new SolidBrush(Terrain.Colors[mat]);
                            int h = cell / 2;
                            void DrawQ(int qx, int qy, int quadIndex)
                            {
                                var img = Terrain.PickQuadImage(mat, quadIndex);
                                if (img != null) g.DrawImage(img, new Rectangle(qx, qy, h, h));
                                else g.FillRectangle(br, qx, qy, h, h);
                            }
                            if ((quads & Terrain.QTL) != 0) DrawQ(px, py, 0);
                            if ((quads & Terrain.QTR) != 0) DrawQ(px + h, py, 1);
                            if ((quads & Terrain.QBL) != 0) DrawQ(px, py + h, 2);
                            if ((quads & Terrain.QBR) != 0) DrawQ(px + h, py + h, 3);
                        }
                    }
                    g.DrawRectangle(Pens.DimGray, px, py, cell, cell);
                }
            }
            // база
            int bx = lv.BaseX * cell, by = lv.BaseY * cell;
            g.FillRectangle(Brushes.Gold, bx + 4, by + 4, cell - 8, cell - 8);
            g.DrawString("Б", Font, Brushes.Black, bx + cell / 4, by + cell / 4);
            // точки спауна врагов — красным
            foreach (var p in lv.SpawnPoints)
            {
                int sx = p.X * cell, sy = p.Y * cell;
                g.DrawEllipse(new Pen(Color.Red, 2), sx + 4, sy + 4, cell - 8, cell - 8);
            }
            // точки спауна игрока — зелёным, квадратом, чтобы не путать с врагами
            foreach (var p in lv.PlayerSpawnPoints)
            {
                int sx = p.X * cell, sy = p.Y * cell;
                g.DrawRectangle(new Pen(Color.Lime, 2), sx + 4, sy + 4, cell - 8, cell - 8);
            }
        }

        void Canvas_MouseDown(object sender, MouseEventArgs e)
        {
            int cell = 16 * _zoom;
            int cx = e.X / cell, cy = e.Y / cell;
            if (cx < 0 || cx >= LevelMap.Size || cy < 0 || cy >= LevelMap.Size) return;
            var lv = _levels[_cur];

            if (_toolMode == 1)
            {
                lv.BaseX = cx; lv.BaseY = cy;
            }
            else if (_toolMode == 2)
            {
                var pt = new Point(cx, cy);
                int idx = lv.SpawnPoints.FindIndex(p => p.X == cx && p.Y == cy);
                if (idx >= 0) lv.SpawnPoints.RemoveAt(idx);   // клик по существующей — снять
                else lv.SpawnPoints.Add(pt);
            }
            else if (_toolMode == 3)
            {
                var pt = new Point(cx, cy);
                int idx = lv.PlayerSpawnPoints.FindIndex(p => p.X == cx && p.Y == cy);
                if (idx >= 0) lv.PlayerSpawnPoints.RemoveAt(idx);
                else lv.PlayerSpawnPoints.Add(pt);
            }
            else
            {
                int localX = e.X - cx * cell, localY = e.Y - cy * cell;
                int quadBit = (localX < cell / 2)
                    ? (localY < cell / 2 ? Terrain.QTL : Terrain.QBL)
                    : (localY < cell / 2 ? Terrain.QTR : Terrain.QBR);
                lv.PaintQuad(cx, cy, quadBit, _tool);
            }
            _dirty = true;
            _autoSaveTimer.Stop(); _autoSaveTimer.Start();
            _canvas.Invalidate();
            InvalidateCurrentThumb();
        }

        void NewLevel()
        {
            _levels.Add(new LevelMap($"level{_levels.Count + 1}"));
            _cur = _levels.Count - 1;
            RefreshThumbs(); _canvas.Invalidate();
        }

        void DuplicateLevel()
        {
            var src = _levels[_cur];
            var copy = LevelMap.Deserialize(src.Serialize());
            copy.Name = src.Name + "_copy";
            _levels.Insert(_cur + 1, copy);
            _cur++;
            RefreshThumbs(); _canvas.Invalidate();
        }

        void DeleteLevel()
        {
            if (_levels.Count <= 1) return;
            if (MessageBox.Show($"Удалить «{_levels[_cur].Name}»?", "Подтверждение",
                MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            _levels.RemoveAt(_cur);
            _cur = Math.Max(0, _cur - 1);
            RefreshThumbs(); _canvas.Invalidate();
        }

        void RenameCurrent()
        {
            using (var f = new Form { Width = 300, Height = 120, Text = "Имя уровня", BackColor = C_BG, ForeColor = C_TEXT, StartPosition = FormStartPosition.CenterParent })
            {
                var tb = new TextBox { Left = 10, Top = 10, Width = 260, Text = _levels[_cur].Name, BackColor = C_BG3, ForeColor = C_TEXT };
                var ok = new Button { Text = "OK", Left = 100, Top = 45, Width = 80, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Отмена", Left = 190, Top = 45, Width = 80, DialogResult = DialogResult.Cancel };
                f.Controls.Add(tb); f.Controls.Add(ok); f.Controls.Add(cancel);
                f.AcceptButton = ok; f.CancelButton = cancel;
                if (f.ShowDialog() == DialogResult.OK && tb.Text.Trim().Length > 0)
                {
                    _levels[_cur].Name = tb.Text.Trim();
                    RefreshThumbs();
                }
            }
        }

        void EditSettings()
        {
            var lv = _levels[_cur];
            var fields = new (string label, Func<int> get, Action<int> set)[]
            {
                ("Всего танков",        () => lv.TotalTanks,  v => lv.TotalTanks  = v),
                ("  из них базовых",    () => lv.TankBasic,   v => lv.TankBasic   = v),
                ("  из них быстрых",    () => lv.TankFast,    v => lv.TankFast    = v),
                ("  из них бронир.",    () => lv.TankArmor,   v => lv.TankArmor   = v),
                ("  из них тяжёлых",    () => lv.TankHeavy,   v => lv.TankHeavy   = v),
                ("Макс. на экране",     () => lv.MaxOnScreen, v => lv.MaxOnScreen = v),
                ("Жизней игрока",       () => lv.PlayerLives, v => lv.PlayerLives = v),
            };
            using (var f = new Form
            {
                Width = 300,
                Height = 40 + fields.Length * 32 + 90,
                Text = $"Настройки: {lv.Name}",
                BackColor = C_BG,
                ForeColor = C_TEXT,
                StartPosition = FormStartPosition.CenterParent
            })
            {
                var nups = new NumericUpDown[fields.Length];
                int fy = 10;
                for (int i = 0; i < fields.Length; i++)
                {
                    var lbl = new Label { Text = fields[i].label, Left = 10, Top = fy + 4, Width = 140, ForeColor = C_TEXT };
                    var nup = new NumericUpDown
                    {
                        Left = 155,
                        Top = fy,
                        Width = 110,
                        Minimum = 0,
                        Maximum = 999,
                        Value = Math.Max(0, Math.Min(999, fields[i].get())),
                        BackColor = C_BG3,
                        ForeColor = C_TEXT
                    };
                    nups[i] = nup;
                    f.Controls.Add(lbl); f.Controls.Add(nup);
                    fy += 32;
                }
                var bonusLbl = new Label { Text = "Бонусные танки\n(номер по счёту\nпоявления, через\nзапятую):", Left = 10, Top = fy, Width = 140, Height = 60, ForeColor = C_TEXT };
                var bonusTb = new TextBox { Left = 155, Top = fy, Width = 110, BackColor = C_BG3, ForeColor = C_TEXT, Text = string.Join(",", lv.BonusSlots) };
                f.Controls.Add(bonusLbl); f.Controls.Add(bonusTb);
                fy += 64;
                var ok = new Button { Text = "OK", Left = 60, Top = fy + 6, Width = 80, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Отмена", Left = 150, Top = fy + 6, Width = 80, DialogResult = DialogResult.Cancel };
                f.Controls.Add(ok); f.Controls.Add(cancel);
                f.AcceptButton = ok; f.CancelButton = cancel;
                if (f.ShowDialog() == DialogResult.OK)
                {
                    for (int i = 0; i < fields.Length; i++)
                        fields[i].set((int)nups[i].Value);
                    lv.BonusSlots.Clear();
                    foreach (var part in bonusTb.Text.Split(','))
                        if (int.TryParse(part.Trim(), out int v) && v > 0) lv.BonusSlots.Add(v);
                    _dirty = true;
                    _autoSaveTimer.Stop(); _autoSaveTimer.Start();
                    RefreshThumbs();
                }
            }
        }

        // Метки начала и конца вставленного блока: по ним Form1 вырезает
        // предыдущую вставку, чтобы повторное нажатие «→ В код» заменяло
        // блок, а не добавляло второй экземпляр тех же данных.
        public const string MarkBegin = "// ─── УРОВНИ: начало ───";
        public const string MarkEnd = "// ─── УРОВНИ: конец ───";

        // Вставка уровней в программу: массив данных, текущий уровень и один
        // метод getLevel(num).
        //
        // Уровни лежат одним массивом структур, а getLevel() — это одно
        // копирование по индексу. Отдельная переменная на уровень и цепочка
        // if (num == 0) ... if (num == 34) означали бы 35 копий одного и того
        // же кода и невозможность обратиться к уровню вычисленным номером.
        void ExportCode()
        {
            if (_levels.Count == 0) return;

            var sb = new StringBuilder();
            sb.AppendLine(MarkBegin + " (вставлено редактором уровней, правится там же)");
            sb.AppendLine("//   levels[]     — данные всех уровней проекта");
            sb.AppendLine("//   levelCount   — сколько их");
            sb.AppendLine("//   currentLevel — уровень, загруженный последним");
            sb.AppendLine("//   getLevel(num) — скопировать уровень num в currentLevel,");
            sb.AppendLine("//                   1 — получилось, 0 — нет такого номера");
            sb.AppendLine("//");
            sb.AppendLine("//   Клетка поля: старший нибл — материал (0..5),");
            sb.AppendLine("//   младший — четверти кирпича (TL=1, TR=2, BL=4, BR=8).");
            sb.AppendLine("//   База и точки спауна упакованы как X*16 + Y.");
            sb.AppendLine();
            sb.Append(LevelMap.ExportStructDecl());
            sb.AppendLine();
            sb.AppendLine($"int levelCount = {_levels.Count};");
            sb.AppendLine();
            sb.AppendLine("// Уровень, загруженный последним вызовом getLevel().");
            sb.AppendLine("struct Level currentLevel;");
            sb.AppendLine();
            sb.AppendLine($"struct Level levels[{_levels.Count}] = {{");
            for (int i = 0; i < _levels.Count; i++)
            {
                var lv = _levels[i];
                sb.AppendLine($"    // {i} — {lv.Name}");
                string warn = lv.Warnings();
                if (warn.Length > 0)
                    foreach (var w in warn.TrimEnd('\r', '\n').Split('\n'))
                        sb.AppendLine("    " + w.TrimEnd('\r'));
                sb.AppendLine("    {");
                sb.Append(lv.ValueLines("        "));
                sb.AppendLine(i + 1 < _levels.Count ? "    }," : "    }");
            }
            sb.AppendLine("};");
            sb.AppendLine();
            sb.AppendLine("// Загрузить уровень номер num (с нуля) в currentLevel.");
            sb.AppendLine("int getLevel(int num) {");
            sb.AppendLine("    if (num < 0) { return 0; }");
            sb.AppendLine("    if (num >= levelCount) { return 0; }");
            sb.AppendLine("    currentLevel = levels[num];");
            sb.AppendLine("    return 1;");
            sb.AppendLine("}");
            sb.AppendLine(MarkEnd);

            var code = sb.ToString();
            if (_insertCode != null) _insertCode(code);
        }

        List<PictureBox> _thumbPics = new List<PictureBox>();   // по одной на уровень — для точечного обновления без пересборки панели

        void RefreshThumbs()
        {
            _thumbPanel.Controls.Clear();
            _thumbPics.Clear();
            for (int i = 0; i < _levels.Count; i++)
            {
                int ii = i;
                var card = new Panel { Width = 120, Height = 130, BackColor = (i == _cur) ? C_SEL : C_BG3, Margin = new Padding(4) };
                var pic = new PictureBox { Width = 104, Height = 104, Left = 8, Top = 4, BackColor = Color.Black };
                pic.Paint += (s, e) => DrawThumb(e.Graphics, _levels[ii]);
                var lbl = new Label { Text = _levels[ii].Name, Left = 4, Top = 110, Width = 112, ForeColor = C_TEXT, TextAlign = ContentAlignment.MiddleCenter };
                card.Controls.Add(pic); card.Controls.Add(lbl);
                card.Click += (s, e) => { _cur = ii; RefreshThumbs(); _canvas.Invalidate(); };
                pic.Click += (s, e) => { _cur = ii; RefreshThumbs(); _canvas.Invalidate(); };
                _thumbPanel.Controls.Add(card);
                _thumbPics.Add(pic);
            }
        }

        // Перерисовать только миниатюру ТЕКУЩЕГО уровня — без пересборки
        // всей панели превью, вызывается после каждой правки на холсте.
        void InvalidateCurrentThumb()
        {
            if (_cur >= 0 && _cur < _thumbPics.Count) _thumbPics[_cur].Invalidate();
        }

        void DrawThumb(Graphics g, LevelMap lv)
        {
            int cell = 8;
            for (int cy = 0; cy < LevelMap.Size; cy++)
                for (int cx = 0; cx < LevelMap.Size; cx++)
                {
                    int mat = lv.Get(cx, cy);
                    int quads = lv.GetQuads(cx, cy);
                    int px = cx * cell, py = cy * cell;
                    if (mat == Terrain.Empty)
                    {
                        g.FillRectangle(Brushes.Black, px, py, cell, cell);
                        continue;
                    }
                    var solid = quads == Terrain.QALL ? Terrain.SolidTexture[mat] : null;
                    if (solid != null)
                    {
                        g.DrawImage(solid, new Rectangle(px, py, cell, cell));
                        continue;
                    }
                    var br = new SolidBrush(Terrain.Colors[mat]);
                    int h = cell / 2;
                    void DrawQ(int qx, int qy, int quadIndex)
                    {
                        var img = Terrain.PickQuadImage(mat, quadIndex);
                        if (img != null) g.DrawImage(img, new Rectangle(qx, qy, h, h));
                        else g.FillRectangle(br, qx, qy, h, h);
                    }
                    if ((quads & Terrain.QTL) != 0) DrawQ(px, py, 0);
                    if ((quads & Terrain.QTR) != 0) DrawQ(px + h, py, 1);
                    if ((quads & Terrain.QBL) != 0) DrawQ(px, py + h, 2);
                    if ((quads & Terrain.QBR) != 0) DrawQ(px + h, py + h, 3);
                }
        }

        void AutoSave()
        {
            if (string.IsNullOrEmpty(_levelsPath)) return;
            try
            {
                var sb = new StringBuilder();
                foreach (var lv in _levels) { sb.Append(lv.Serialize()); sb.AppendLine("---"); }
                Directory.CreateDirectory(Path.GetDirectoryName(_levelsPath));
                File.WriteAllText(_levelsPath, sb.ToString(), Encoding.UTF8);
                _dirty = false;
            }
            catch { }
        }

        public void SaveNow()
        {
            _autoSaveTimer.Stop();
            AutoSave();
        }

        // Разбор текста файла .lvl (блоки, разделённые "---") в список
        // уровней. Общий метод — им пользуется и обычная загрузка
        // (TryLoad), и ручной импорт (ImportFromFile), чтобы не было
        // двух разных копий одной и той же логики разбора.
        static List<LevelMap> ParseLevelsText(string text)
        {
            var blocks = text.Split(new[] { "---" }, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<LevelMap>();
            foreach (var b in blocks)
                if (b.Trim().Length > 0) list.Add(LevelMap.Deserialize(b));
            return list;
        }

        void TryLoad()
        {
            if (string.IsNullOrEmpty(_levelsPath) || !File.Exists(_levelsPath)) return;
            try
            {
                var text = File.ReadAllText(_levelsPath, Encoding.UTF8);
                var list = ParseLevelsText(text);
                if (list.Count > 0) { _levels = list; _cur = 0; }
            }
            catch { }
            RefreshThumbs(); _canvas?.Invalidate();
        }

        // Импорт уровней из ЛЮБОГО .lvl-файла (например, готового набора
        // уровней) прямо в текущий проект — без ручной подмены файла на
        // диске и без риска, что открытый редактор перезапишет её своим
        // автосохранением. Работает через ту же память (_levels), что и
        // всё остальное в редакторе, поэтому дальше всё ведёт себя как
        // обычно: миниатюры обновляются сразу, автосохранение пишет
        // результат в levels.lvl текущего проекта само.
        void ImportFromFile()
        {
            using (var dlg = new OpenFileDialog { Filter = "Файлы уровней (*.lvl)|*.lvl|Все файлы (*.*)|*.*", Title = "Импорт уровней" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                List<LevelMap> imported;
                try
                {
                    var text = File.ReadAllText(dlg.FileName, Encoding.UTF8);
                    imported = ParseLevelsText(text);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Не удалось прочитать файл:\n" + ex.Message, "Импорт уровней",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                if (imported.Count == 0)
                {
                    MessageBox.Show(this, "В файле не найдено ни одного уровня.", "Импорт уровней",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var existingNames = new HashSet<string>(_levels.Select(lv => lv.Name), StringComparer.OrdinalIgnoreCase);
                int added = 0;
                foreach (var lv in imported)
                {
                    string baseName = string.IsNullOrWhiteSpace(lv.Name) ? "level" : lv.Name;
                    string name = baseName;
                    int n = 2;
                    while (existingNames.Contains(name)) { name = baseName + "_" + n; n++; }
                    lv.Name = name;
                    existingNames.Add(name);
                    _levels.Add(lv);
                    added++;
                }
                _cur = _levels.Count - 1;
                RefreshThumbs();
                _canvas?.Invalidate();
                SaveNow();   // сразу на диск, не дожидаясь таймера — импорт делаем осознанно, один раз

                MessageBox.Show(this, $"Импортировано уровней: {added}", "Импорт уровней",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        public List<LevelMap> GetLevels() => new List<LevelMap>(_levels);
    }
}
