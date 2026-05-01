using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CompMacro11
{
    public class CompilerSettings
    {
        public string TargetPlatform = "UKNC";
        public int    ScreenMode     = 1;
        public bool   OptimizeLabels = true;
    }

    public class McProject
    {
        public string Name          = "Новый проект";
        public string Version       = "1.0";
        public string Created       = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        public string Modified      = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        public string MainFile      = "main.mc";
        public List<string> Files   = new List<string>();
        public List<string> Sprites = new List<string>();
        public CompilerSettings Settings = new CompilerSettings();
        public int    CursorLine    = 1;
        public int    CursorCol     = 1;

        public string ProjectPath;
        public string ProjectDir { get { return ProjectPath != null ? Path.GetDirectoryName(ProjectPath) : null; } }
        public bool   IsModified = false;

        public static McProject CreateNew(string name, string folder)
        {
            var proj = new McProject();
            proj.Name     = name;
            proj.Created  = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            proj.Modified = proj.Created;

            string dir = Path.Combine(folder, name);
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(Path.Combine(dir, "sprites"));
            Directory.CreateDirectory(Path.Combine(dir, "build"));

            string mainPath = Path.Combine(dir, "main.mc");
            File.WriteAllText(mainPath, "// Проект: " + name + "\n\nint main() {\n    return 0;\n}\n");

            proj.Files.Add("main.mc");
            proj.ProjectPath = Path.Combine(dir, name + ".pkc");
            proj.Save();
            return proj;
        }

        public void Save()
        {
            Modified = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"Name\": \""    + Escape(Name)    + "\",");
            sb.AppendLine("  \"Version\": \"" + Escape(Version) + "\",");
            sb.AppendLine("  \"Created\": \"" + Escape(Created) + "\",");
            sb.AppendLine("  \"Modified\": \""+ Escape(Modified)+ "\",");
            sb.AppendLine("  \"MainFile\": \""+ Escape(MainFile)+ "\",");
            sb.AppendLine("  \"CursorLine\": "+ CursorLine      + ",");
            sb.AppendLine("  \"CursorCol\": " + CursorCol       + ",");
            sb.AppendLine("  \"Settings\": {");
            sb.AppendLine("    \"TargetPlatform\": \"" + Escape(Settings.TargetPlatform) + "\",");
            sb.AppendLine("    \"ScreenMode\": "    + Settings.ScreenMode + ",");
            sb.AppendLine("    \"OptimizeLabels\": " + (Settings.OptimizeLabels ? "true" : "false"));
            sb.AppendLine("  },");
            sb.Append("  \"Files\": [");
            for (int i = 0; i < Files.Count; i++)
                sb.Append("\"" + Escape(Files[i]) + "\"" + (i < Files.Count - 1 ? ", " : ""));
            sb.AppendLine("],");
            sb.Append("  \"Sprites\": [");
            for (int i = 0; i < Sprites.Count; i++)
                sb.Append("\"" + Escape(Sprites[i]) + "\"" + (i < Sprites.Count - 1 ? ", " : ""));
            sb.AppendLine("]");
            sb.AppendLine("}");
            File.WriteAllText(ProjectPath, sb.ToString(), Encoding.UTF8);
            IsModified = false;
        }

        public void SaveAs(string newPath)
        {
            ProjectPath = newPath;
            Save();
        }

        public static McProject Load(string path)
        {
            var proj = new McProject();
            proj.ProjectPath = path;
            string text      = File.ReadAllText(path, Encoding.UTF8);
            proj.Name        = ReadStr(text, "Name");
            proj.Version     = ReadStr(text, "Version");
            proj.Created     = ReadStr(text, "Created");
            proj.Modified    = ReadStr(text, "Modified");
            proj.MainFile    = ReadStr(text, "MainFile");
            proj.CursorLine  = ReadInt(text, "CursorLine", 1);
            proj.CursorCol   = ReadInt(text, "CursorCol",  1);
            proj.Settings.TargetPlatform = ReadStr(text, "TargetPlatform");
            proj.Settings.ScreenMode     = ReadInt(text, "ScreenMode", 1);
            proj.Settings.OptimizeLabels = ReadBool(text, "OptimizeLabels", true);
            proj.Files   = ReadList(text, "Files");
            proj.Sprites = ReadList(text, "Sprites");
            proj.IsModified = false;
            return proj;
        }

        public string ReadMainCode()
        {
            string p = Path.Combine(ProjectDir, MainFile);
            return File.Exists(p) ? File.ReadAllText(p, Encoding.UTF8) : "";
        }

        public void WriteMainCode(string code)
        {
            File.WriteAllText(Path.Combine(ProjectDir, MainFile), code, Encoding.UTF8);
            IsModified = true;
        }

        public void Delete()
        {
            if (ProjectDir != null && Directory.Exists(ProjectDir))
                Directory.Delete(ProjectDir, true);
        }

        public override string ToString() { return Name; }

        private static string Escape(string s)
        {
            return s == null ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string ReadStr(string json, string key)
        {
            string search = "\"" + key + "\": \"";
            int i = json.IndexOf(search);
            if (i < 0) return "";
            i += search.Length;
            int j = json.IndexOf("\"", i);
            return j < 0 ? "" : json.Substring(i, j - i).Replace("\\\"", "\"").Replace("\\\\", "\\");
        }

        private static int ReadInt(string json, string key, int def)
        {
            string search = "\"" + key + "\": ";
            int i = json.IndexOf(search);
            if (i < 0) return def;
            i += search.Length;
            int j = i;
            while (j < json.Length && (char.IsDigit(json[j]) || json[j] == '-')) j++;
            int val;
            return int.TryParse(json.Substring(i, j - i), out val) ? val : def;
        }

        private static bool ReadBool(string json, string key, bool def)
        {
            string search = "\"" + key + "\": ";
            int i = json.IndexOf(search);
            if (i < 0) return def;
            i += search.Length;
            if (i + 4 <= json.Length && json.Substring(i, 4) == "true")  return true;
            if (i + 5 <= json.Length && json.Substring(i, 5) == "false") return false;
            return def;
        }

        private static List<string> ReadList(string json, string key)
        {
            var list = new List<string>();
            string search = "\"" + key + "\": [";
            int i = json.IndexOf(search);
            if (i < 0) return list;
            i += search.Length;
            int end = json.IndexOf("]", i);
            if (end < 0) return list;
            string block = json.Substring(i, end - i);
            foreach (var part in block.Split(','))
            {
                string s = part.Trim().Trim('"');
                if (s.Length > 0) list.Add(s);
            }
            return list;
        }
    }

    // ─── Настройки окружения (машины) ───────────────────────────────
    // Хранятся в AppData — не входят в проект, у каждого компа свои
    public static class AppEnvironment
    {
        private static readonly string _envFile =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "CompMacro11", "environment.txt");

        public static string EmulatorPath
        {
            get { return ReadKey("EmulatorPath"); }
            set { WriteKey("EmulatorPath", value); }
        }

        public static string LastProjectPath
        {
            get { return ReadKey("LastProjectPath"); }
            set { WriteKey("LastProjectPath", value); }
        }

        private static string ReadKey(string key)
        {
            try
            {
                if (!File.Exists(_envFile)) return "";
                foreach (var line in File.ReadAllLines(_envFile))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0 && line.Substring(0, eq).Trim() == key)
                        return line.Substring(eq + 1).Trim();
                }
            }
            catch { }
            return "";
        }

        private static void WriteKey(string key, string value)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_envFile));
                var lines = new List<string>();
                bool found = false;
                if (File.Exists(_envFile))
                    foreach (var line in File.ReadAllLines(_envFile))
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0 && line.Substring(0, eq).Trim() == key)
                        { lines.Add(key + "=" + value); found = true; }
                        else lines.Add(line);
                    }
                if (!found) lines.Add(key + "=" + value);
                File.WriteAllLines(_envFile, lines.ToArray());
            }
            catch { }
        }
    }

    // ─── Последние проекты ───────────────────────────────────────────
    public static class RecentProjects
    {
        private static readonly string _recentFile =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "CompMacro11", "recent.txt");

        public static List<string> Load()
        {
            var list = new List<string>();
            try
            {
                if (File.Exists(_recentFile))
                    foreach (var line in File.ReadAllLines(_recentFile))
                        if (line.Length > 0 && File.Exists(line)) list.Add(line);
            }
            catch { }
            return list;
        }

        public static void Add(string path)
        {
            var list = Load();
            list.Remove(path);
            list.Insert(0, path);
            if (list.Count > 10) list.RemoveRange(10, list.Count - 10);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_recentFile));
                File.WriteAllLines(_recentFile, list.ToArray());
            }
            catch { }
        }

        public static void Remove(string path)
        {
            var list = Load();
            list.Remove(path);
            try { File.WriteAllLines(_recentFile, list.ToArray()); } catch { }
        }
    }
}
