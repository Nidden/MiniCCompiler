using System.Collections.Generic;

namespace CompMacro11
{
    // ============================================================
    // Peephole оптимизатор для Macro-11 (PDP-11 / УКНЦ)
    // Подключается опционально через CodeGen.EnablePeephole
    //
    // Работает на уровне строк сгенерированного ассемблера.
    // Применяет локальные замены пар инструкций.
    // ============================================================
    public static class PeepholeOptimizer
    {
        public static string Apply(string src)
        {
            var lines = src.Split('\n');
            var result = new List<string>(lines.Length);

            for (int i = 0; i < lines.Length; i++)
            {
                string cur  = lines[i];
                string next = i + 1 < lines.Length ? lines[i + 1] : "";

                string curT  = cur.Trim();
                string nextT = next.Trim();

                // ── Паттерн 1: MOV A,B / MOV B,A → MOV A,B ─────────
                // Второй MOV избыточен если A уже в B
                if (IsMov(curT, out string c1f, out string c1t) &&
                    IsMov(nextT, out string n1f, out string n1t) &&
                    c1f == n1t && c1t == n1f &&
                    IsRegister(c1f) && IsRegister(c1t))
                {
                    result.Add(cur);
                    i++;
                    continue;
                }

                // ── Паттерн 2: MOV X,R0 / TST R0 → MOV X,R0 ────────
                // На PDP-11 MOV устанавливает флаги N и Z — TST избыточен
                if (IsMov(curT, out string c2f, out string c2t) &&
                    c2t == "R0" && nextT == "TST\tR0")
                {
                    result.Add(cur);
                    i++;
                    continue;
                }

                // ── Паттерн 3: CLR R0 / TST R0 → CLR R0 ────────────
                if (curT == "CLR\tR0" && nextT == "TST\tR0")
                {
                    result.Add(cur);
                    i++;
                    continue;
                }

                // ── Паттерн 4: MOV R0,X / MOV X,R0 → MOV R0,X ──────
                // Только что записали X из R0 — загружать назад не нужно
                if (IsMov(curT, out string c4f, out string c4t) &&
                    c4f == "R0" && !IsRegister(c4t) &&
                    IsMov(nextT, out string n4f, out string n4t) &&
                    n4f == c4t && n4t == "R0")
                {
                    result.Add(cur);
                    i++;
                    continue;
                }

                // ── Паттерн 5: MOV Rx,R0 / MOV R0,Rx → MOV Rx,R0 ───
                if (IsMov(curT, out string c5f, out string c5t) &&
                    c5t == "R0" && IsRegister(c5f) && c5f != "R0" &&
                    IsMov(nextT, out string n5f, out string n5t) &&
                    n5f == "R0" && n5t == c5f)
                {
                    result.Add(cur);
                    i++;
                    continue;
                }

                // ── Паттерн 6: MOV mem,R0 / TST R0 → TST mem ───────
                // TST ставит те же флаги без загрузки в регистр
                if (IsMov(curT, out string c6f, out string c6t) &&
                    c6t == "R0" && !IsRegister(c6f) &&
                    nextT == "TST\tR0")
                {
                    result.Add("        TST\t" + c6f);
                    i++;
                    continue;
                }

                // ── Паттерн 7: ADD R0,R1 / MOV R1,R0 → ADD R1,R0 ───
                // ADD коммутативна — результат сразу в R0
                if (curT == "ADD\tR0, R1" && nextT == "MOV\tR1, R0")
                {
                    result.Add("        ADD\tR1, R0");
                    i++;
                    continue;
                }

                // ── Паттерн 8: CLR R0 / ADD X,R0 → MOV X,R0 ────────
                if (curT == "CLR\tR0" && nextT.StartsWith("ADD\t"))
                {
                    string addArgs = nextT.Substring(4).Trim();
                    int comma = addArgs.IndexOf(',');
                    if (comma > 0)
                    {
                        string src10 = addArgs.Substring(0, comma).Trim();
                        string dst10 = addArgs.Substring(comma + 1).Trim();
                        if (dst10 == "R0" && !IsRegister(src10))
                        {
                            result.Add("        MOV\t" + src10 + ", R0");
                            i++;
                            continue;
                        }
                    }
                }

                result.Add(cur);
            }

            return string.Join("\n", result);
        }

        private static bool IsMov(string line, out string from, out string to)
        {
            from = to = "";
            if (!line.StartsWith("MOV")) return false;
            var rest  = line.Substring(3).TrimStart('\t', ' ');
            var comma = rest.LastIndexOf(',');
            if (comma < 0) return false;
            from = rest.Substring(0, comma).Trim();
            to   = rest.Substring(comma + 1).Trim();
            return from.Length > 0 && to.Length > 0;
        }

        private static bool IsRegister(string s)
            => s == "R0" || s == "R1" || s == "R2" ||
               s == "R3" || s == "R4" || s == "R5";
    }
}
