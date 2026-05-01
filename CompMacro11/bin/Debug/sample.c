
// sprite: spr_bg  32x32
// sprite(x, y, 4, 32, spr_bg);
// sprite: spr_cur  32x32
// sprite(x, y, 4, 32, spr_cur);
// sprite: spr_sel  32x32
// sprite(x, y, 4, 32, spr_sel);
// sprite: spr1  32x32
// sprite(x, y, 4, 32, spr1);
// sprite: spr2  32x32
// sprite(x, y, 4, 32, spr2);
// sprite: spr3  32x32
// sprite(x, y, 4, 32, spr3);
// sprite: spr4  32x32
// sprite(x, y, 4, 32, spr4);
// sprite: spr5  32x32
// sprite(x, y, 4, 32, spr5);
// sprite: spr6  32x32
// sprite(x, y, 4, 32, spr6);



// ══════════════════════════════════════════════════════════
// MATCH-3 для УКНЦ  —  поле 7x7, 6 типов фишек
// Режим 1: 640x264
// Управление:
//   Стрелки — перемещение курсора
//   Enter   — выбрать фишку для обмена
//   Стрелка — направление обмена
//   Esc     — отменить выбор
// ══════════════════════════════════════════════════════════


// ── Константы ────────────────────────────────────────────
// Поле на экране:
//   wx = 26 + bx*4  (слова, режим 1 = 80 слов/строку)
//   py = 20 + by*32 (пиксели)
// Коды клавиш УКНЦ:
//   вправо=67 влево=68 вверх=65 вниз=66 Enter=13 Esc=27

// ── Отрисовка одной ячейки ────────────────────────────────
void draw_cell(int bx, int by, int piece, int sel, int cur) {
    int wx = 26 + bx * 4;
    int py = 20 + by * 32;
    sprite(wx, py, 4, 32, spr_bg);
    if (piece == 1) sprite(wx, py, 4, 32, spr1);
    if (piece == 2) sprite(wx, py, 4, 32, spr2);
    if (piece == 3) sprite(wx, py, 4, 32, spr3);
    if (piece == 4) sprite(wx, py, 4, 32, spr4);
    if (piece == 5) sprite(wx, py, 4, 32, spr5);
    if (piece == 6) sprite(wx, py, 4, 32, spr6);
    if (sel) sprite(wx, py, 4, 32, spr_sel);
    if (cur) sprite(wx, py, 4, 32, spr_cur);
}

// ── Перерисовка всего поля ────────────────────────────────
void draw_board(int b[], int cx, int cy,
                int selx, int sely, int mode) {
    int x = 0;
    int y = 0;
    while (y < 7) {
        x = 0;
        while (x < 7) {
            int is_sel = 0;
            int is_cur = 0;
            if (x == cx && y == cy) is_cur = 1;
            if (mode == 1 && x == selx && y == sely) is_sel = 1;
            draw_cell(x, y, b[y * 7 + x], is_sel, is_cur);
            x = x + 1;
        }
        y = y + 1;
    }
}

// ── Псевдослучайное 1..6  (LCG mod 65536) ────────────────
int rnd(int s[]) {
    int v = s[0] * 25 + 13;
    s[0] = v;
    if (v < 0) v = 0 - v;
    v = v && 32767;
    v = v % 6;
    return v + 1;
}

// ── Начальное заполнение без матчей ───────────────────────
void fill_board(int b[], int s[]) {
    int x = 0;
    int y = 0;
    int p = 0;
    while (y < 7) {
        x = 0;
        while (x < 7) {
            p = rnd(s);
            // Не создавать горизонтальный матч
            if (x >= 2) {
                if (b[y*7+x-1] == p && b[y*7+x-2] == p)
                    p = p % 6 + 1;
                if (b[y*7+x-1] == p && b[y*7+x-2] == p)
                    p = p % 6 + 1;
            }
            // Не создавать вертикальный матч
            if (y >= 2) {
                if (b[(y-1)*7+x] == p && b[(y-2)*7+x] == p)
                    p = p % 6 + 1;
                if (b[(y-1)*7+x] == p && b[(y-2)*7+x] == p)
                    p = p % 6 + 1;
            }
            b[y * 7 + x] = p;
            x = x + 1;
        }
        y = y + 1;
    }
}

// ── Найти матчи (m[]=1 если фишка к удалению) ─────────────
// Возвращает 1 если есть хоть один матч
int find_matches(int b[], int m[]) {
    int x = 0;
    int y = 0;
    int found = 0;
    int p = 0;
    // Сбросить marks
    while (y < 7) {
        x = 0;
        while (x < 7) { m[y*7+x] = 0; x = x + 1; }
        y = y + 1;
    }
    // Горизонталь
    y = 0;
    while (y < 7) {
        x = 0;
        while (x < 5) {
            p = b[y*7+x];
            if (p != 0 && p == b[y*7+x+1] && p == b[y*7+x+2]) {
                m[y*7+x]   = 1;
                m[y*7+x+1] = 1;
                m[y*7+x+2] = 1;
                found = 1;
            }
            x = x + 1;
        }
        y = y + 1;
    }
    // Вертикаль
    y = 0;
    while (y < 5) {
        x = 0;
        while (x < 7) {
            p = b[y*7+x];
            if (p != 0 && p == b[(y+1)*7+x] && p == b[(y+2)*7+x]) {
                m[y*7+x]       = 1;
                m[(y+1)*7+x]   = 1;
                m[(y+2)*7+x]   = 1;
                found = 1;
            }
            x = x + 1;
        }
        y = y + 1;
    }
    return found;
}

// ── Удалить помеченные фишки ──────────────────────────────
void remove_marked(int b[], int m[]) {
    int i = 0;
    while (i < 49) {
        if (m[i]) b[i] = 0;
        i = i + 1;
    }
}

// ── Гравитация: фишки падают вниз ─────────────────────────
void drop_pieces(int b[]) {
    int x = 0;
    int y = 0;
    int fy = 0;
    while (x < 7) {
        y = 6;
        while (y > 0) {
            if (b[y*7+x] == 0) {
                fy = y - 1;
                while (fy >= 0 && b[fy*7+x] == 0) fy = fy - 1;
                if (fy >= 0) {
                    b[y*7+x]  = b[fy*7+x];
                    b[fy*7+x] = 0;
                }
            }
            y = y - 1;
        }
        x = x + 1;
    }
}

// ── Заполнить пустые ячейки сверху ────────────────────────
void refill(int b[], int s[]) {
    int x = 0;
    int y = 0;
    while (y < 7) {
        x = 0;
        while (x < 7) {
            if (b[y*7+x] == 0) b[y*7+x] = rnd(s);
            x = x + 1;
        }
        y = y + 1;
    }
}

// ── Обменять две ячейки ───────────────────────────────────
void do_swap(int b[], int x1, int y1, int x2, int y2) {
    int t = b[y1*7+x1];
    b[y1*7+x1] = b[y2*7+x2];
    b[y2*7+x2] = t;
}

// ── Каскад: удалять/падать/добавлять пока есть матчи ──────
void cascade(int b[], int m[], int s[]) {
    int found = find_matches(b, m);
    while (found) {
        remove_marked(b, m);
        drop_pieces(b);
        refill(b, s);
        found = find_matches(b, m);
    }
}

// ── Главная программа ─────────────────────────────────────
int main(void) {
    cls(1);

    int board[49];
    int marks[49];
    int seed[1];
    int cx   = 3;   // курсор X
    int cy   = 3;   // курсор Y
    int selx = 0;   // выбранная X
    int sely = 0;   // выбранная Y
    int mode = 0;   // 0=нормально  1=выбрана фишка
    int k    = 0;

    seed[0] = 57;
    fill_board(board, seed);
    cascade(board, marks, seed);     // убрать случайные начальные матчи
    draw_board(board, cx, cy, selx, sely, mode);

    while (1) {
        k = waitkey();

        if (mode == 0) {
            // ── Режим перемещения курсора ─────────────────
            if (k == 65 && cy > 0) cy = cy - 1;   // вверх
            if (k == 66 && cy < 6) cy = cy + 1;   // вниз
            if (k == 68 && cx > 0) cx = cx - 1;   // влево
            if (k == 67 && cx < 6) cx = cx + 1;   // вправо
            if (k == 13) {
                selx = cx;
                sely = cy;
                mode = 1;                          // выбрать фишку
            }
        } else {
            // ── Режим обмена: ждём направление ───────────
            if (k == 27) {
                mode = 0;                          // Esc — отмена
            }
            if (k == 65 && sely > 0) {            // обмен вверх
                do_swap(board, selx, sely, selx, sely - 1);
                if (find_matches(board, marks) == 0)
                    do_swap(board, selx, sely, selx, sely - 1);
                else
                    cascade(board, marks, seed);
                mode = 0;
            }
            if (k == 66 && sely < 6) {            // обмен вниз
                do_swap(board, selx, sely, selx, sely + 1);
                if (find_matches(board, marks) == 0)
                    do_swap(board, selx, sely, selx, sely + 1);
                else
                    cascade(board, marks, seed);
                mode = 0;
            }
            if (k == 68 && selx > 0) {            // обмен влево
                do_swap(board, selx, sely, selx - 1, sely);
                if (find_matches(board, marks) == 0)
                    do_swap(board, selx, sely, selx - 1, sely);
                else
                    cascade(board, marks, seed);
                mode = 0;
            }
            if (k == 67 && selx < 6) {            // обмен вправо
                do_swap(board, selx, sely, selx + 1, sely);
                if (find_matches(board, marks) == 0)
                    do_swap(board, selx, sely, selx + 1, sely);
                else
                    cascade(board, marks, seed);
                mode = 0;
            }
        }

        draw_board(board, cx, cy, selx, sely, mode);
    }
    return 0;
}


