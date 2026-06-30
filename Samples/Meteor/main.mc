// ============================================================
//   МЕТЕОРИТЫ — мини-игра для УКНЦ.
//   Корабль внизу, метеориты падают сверху. Уклоняйся!
//   ← / → — движение. Столкновение — взрыв и рестарт.
//   Полоска сверху — время выживания (длиннее = лучше).
//   Только графика: стираем/рисуем изменённое, без cls в цикле.
// ============================================================

int mx[8];
int my[8];
int ms[8];
int px;
int py;
int score;
int barW;
int alive;
int tick;

void drawShip(int x, int y, int c) {
    point(x, y, c);
    point(x - 2, y + 2, c);
    point(x - 1, y + 2, c);
    point(x, y + 2, c);
    point(x + 1, y + 2, c);
    point(x + 2, y + 2, c);
    point(x - 1, y + 4, c);
    point(x, y + 4, c);
    point(x + 1, y + 4, c);
}

void drawRock(int x, int y, int c) {
    fill_rect(x, y, 5, 5, c);
}

int overlap(int sx, int sy, int rx, int ry) {
    if (sx + 2 < rx) return 0;
    if (sx - 2 > rx + 4) return 0;
    if (sy + 4 < ry) return 0;
    if (sy > ry + 4) return 0;
    return 1;
}

void spawnRock(int i) {
    mx[i] = 8 + random(296);
    my[i] = -10 - (i * 28);
    ms[i] = 2 + random(4);
}

void drawBar(int w) {
    if (w > 0) fill_rect(8, 6, w, 3, 2);
}

void eraseBar(int w) {
    if (w > 0) fill_rect(8, 6, w, 3, 0);
}

void explode(int x, int y) {
    int i;
    i = 0;
    while (i < 6) {
        fill_rect(x - 8 + random(16), y - 8 + random(16), 4, 4, 3);
        i = i + 1;
    }
}

int main() {
    int i;
    int k;
    int oldPx;
    init(0);
    cls(0);
    px = 160;
    py = 238;
    score = 0;
    barW = 0;
    alive = 1;
    tick = 0;
    i = 0;
    while (i < 8) {
        spawnRock(i);
        i = i + 1;
    }
    drawShip(px, py, 3);
    while (1) {
        if (alive == 0) {
            explode(px, py);
            vsync();
            pause();
            cls(0);
            px = 160;
            score = 0;
            barW = 0;
            alive = 1;
            tick = 0;
            i = 0;
            while (i < 8) {
                spawnRock(i);
                i = i + 1;
            }
            drawShip(px, py, 3);
        }
        oldPx = px;
        k = getkey();
        if (k == 68) {
            if (px > 12) px = px - 10;
        }
        if (k == 67) {
            if (px < 308) px = px + 10;
        }
        if (oldPx != px) {
            drawShip(oldPx, py, 0);
            drawShip(px, py, 3);
        }
        i = 0;
        while (i < 8) {
            drawRock(mx[i], my[i], 0);
            my[i] = my[i] + ms[i];
            if (my[i] > 270) {
                spawnRock(i);
                my[i] = -8;
            }
            drawRock(mx[i], my[i], 2);
            if (alive == 1 && overlap(px, py, mx[i], my[i]) == 1) {
                alive = 0;
            }
            i = i + 1;
        }
        tick = tick + 1;
        if (tick % 25 == 0) {
            eraseBar(barW);
            score = score + 1;
            barW = score * 4;
            if (barW > 304) barW = 304;
            drawBar(barW);
        }
        vsync();
    }
    return 0;
}
