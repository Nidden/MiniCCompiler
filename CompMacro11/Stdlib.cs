using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CompMacro11
{
    // ── Стандартная библиотека Mini-C ─────────────────────────
    // Функции автоматически добавляются при компиляции если
    // используются в коде и не объявлены пользователем.
    //
    // fillhline — ВСТРОЕННАЯ (builtin), вызывает RTFHL в ассемблере.
    // Не добавляется через StdLib, доступна всегда как point/circle.

    internal static class StdLib
    {
        // Имена для подсветки синтаксиса
        public static readonly HashSet<string> Names = new HashSet<string>
        {
            "line", "iabs", "fillCircle", "fillhline", "frame"
        };

        private static readonly Dictionary<string, string> _functions =
            new Dictionary<string, string>
            {
                ["iabs"] =
@"int iabs(int v) {
    if (v < 0) return 0 - v;
    return v;
}
",
                ["line"] =
@"void line(int x1, int y1, int x2, int y2, int c) {
    int dx = iabs(x2 - x1);
    int dy = iabs(y2 - y1);
    int sx = 1;
    int sy = 1;
    int err = dx - dy;
    int x = x1;
    int y = y1;
    int steps = 0;
    if (x2 < x1) sx = 0 - 1;
    if (y2 < y1) sy = 0 - 1;
    if (dx > dy) steps = dx;
    else         steps = dy;
    while (steps >= 0) {
        point(x, y, c);
        steps = steps - 1;
        int e2 = err + err;
        if (e2 > 0 - dy) { err = err - dy; x = x + sx; }
        if (e2 < dx)     { err = err + dx; y = y + sy; }
    }
}
",
                ["fillhline"] =
@"void fillhline(int lx, int rx, int py, int c) {
    if (lx < 0) lx = 0;
    if (rx > 639) rx = 639;
    if (lx > rx) return;
    if (py < 0) return;
    if (py > 263) return;
    int px = lx;
    while (px <= rx) { point(px, py, c); px = px + 1; }
}
",
                // fillCircle вызывает fillhline
                ["fillCircle"] =
@"void fillCircle(int cx, int cy, int r, int c) {
    int x = 0;
    int y = r;
    int d = 3 - r - r;
    while (x <= y) {
        fillhline(cx - y, cx + y, cy - x, c);
        if (x > 0) fillhline(cx - y, cx + y, cy + x, c);
        fillhline(cx - x, cx + x, cy - y, c);
        if (y != x) fillhline(cx - x, cx + x, cy + y, c);
        if (d < 0) {
            d = d + x + x + x + x + 6;
        } else {
            d = d + x + x - y - y + x + x - y - y + 10;
            y = y - 1;
        }
        x = x + 1;
    }
}
",
                ["frame"] =
@"void frame(int x, int y, int w, int h, int c) {
    int x2 = x + w - 1;
    int y2 = y + h - 1;
    fillhline(x, x2, y,  c);
    fillhline(x, x2, y2, c);
    int py = y + 1;
    while (py < y2) {
        point(x,  py, c);
        point(x2, py, c);
        py = py + 1;
    }
}
",
            };

        private static readonly Dictionary<string, string[]> _deps =
            new Dictionary<string, string[]>
            {
                ["line"] = new[] { "iabs" },
                ["fillhline"] = new string[] { },
                ["fillCircle"] = new[] { "fillhline" },
                ["frame"] = new[] { "fillhline" },
            };

        public static string Inject(string src)
        {
            var needed = new List<string>();
            foreach (var name in _functions.Keys)
                if (IsUsed(src, name) && !IsDeclared(src, name))
                    CollectWithDeps(name, src, needed);
            if (needed.Count == 0) return src;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("// ── StdLib ──");
            foreach (var name in needed)
                sb.Append(_functions[name]);
            sb.AppendLine();

            var m = Regex.Match(src, @"^\s*(void|int)\s+\w+\s*\(", RegexOptions.Multiline);
            int pos = m.Success ? m.Index : 0;
            return src.Substring(0, pos) + sb.ToString() + src.Substring(pos);
        }

        private static void CollectWithDeps(string name, string src, List<string> result)
        {
            if (result.Contains(name)) return;
            if (_deps.TryGetValue(name, out var deps))
                foreach (var dep in deps)
                    if (!IsDeclared(src, dep))
                        CollectWithDeps(dep, src, result);
            if (!result.Contains(name))
                result.Add(name);
        }

        private static bool IsUsed(string src, string name) =>
            Regex.IsMatch(src, $@"\b{Regex.Escape(name)}\s*\(");

        private static bool IsDeclared(string src, string name) =>
            Regex.IsMatch(src, $@"\b(void|int)\s+{Regex.Escape(name)}\s*\(");
    }
}
