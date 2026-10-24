int mx[10];
int my[10];
int ms[10];
int mz[10];
int px;
int py;
int score;
int barW;
int alive;
int dieF;
int tick;
int starX[24];
int starY[24];
int starS[24];

int DIG[50] = {
    7, 5, 5, 5, 7,
    2, 6, 2, 2, 7,
    6, 1, 6, 4, 7,
    6, 1, 6, 1, 7,
    5, 5, 6, 1, 1,
    7, 4, 6, 1, 7,
    7, 4, 6, 5, 7,
    6, 1, 2, 4, 4,
    7, 5, 7, 5, 7,
    6, 5, 7, 1, 7
};

void drawDigit(int x, int y, int d, int c) {
    int row;
    int bits;
    int col;
    int mask;
    row = 0;
    while (row < 5) {
        bits = DIG[d * 5 + row];
        col = 0;
        mask = 4;
        while (col < 3) {
            if (bits & mask) point(x + col, y + row, c);
            mask = mask / 2;
            col = col + 1;
        }
        row = row + 1;
    }
}

void eraseDigit(int x, int y) {
    fill_rect(x, y, 3, 5, 0);
}

void drawScore(int n, int c) {
    int x;
    x = 292;
    eraseDigit(x, 4);
    eraseDigit(x - 5, 4);
    eraseDigit(x - 10, 4);
    if (n >= 100) {
        drawDigit(x - 10, 4, n / 100, c);
        drawDigit(x - 5, 4, (n / 10) % 10, c);
        drawDigit(x, 4, n % 10, c);
    } else if (n >= 10) {
        drawDigit(x - 5, 4, n / 10, c);
        drawDigit(x, 4, n % 10, c);
    } else {
        drawDigit(x, 4, n, c);
    }
}

void drawShip(int x, int y, int c) {
    point(x, y, c);
    point(x - 2, y + 2, c);
    point(x - 1, y + 2, c);
    point(x, y + 2, c);
    point(x + 1, y + 2, c);
    point(x + 2, y + 2, c);
    point(x - 2, y + 4, c);
    point(x - 1, y + 4, c);
    point(x, y + 4, c);
    point(x + 1, y + 4, c);
    point(x + 2, y + 4, c);
    point(x - 1, y + 6, c);
    point(x, y + 6, c);
    point(x + 1, y + 6, c);
}

void drawFlame(int x, int y, int c) {
    point(x, y + 7, c);
    point(x - 1, y + 8, c);
    point(x, y + 8, c);
    point(x + 1, y + 8, c);
}

int rockSize(int z) {
    if (z == 0) return 3;
    if (z == 1) return 5;
    return 7;
}

void drawRock(int x, int y, int z, int c) {
    int s;
    s = rockSize(z);
    if (z == 0) {
        fill_rect(x, y, s, s, c);
    } else if (z == 1) {
        fill_rect(x, y, s, s, c);
        point(x + 1, y + 1, 0);
        point(x + 3, y + 2, 0);
    } else {
        fill_rect(x, y, s, s, c);
        point(x + 2, y + 1, 1);
        point(x + 1, y + 3, 1);
        point(x + 4, y + 4, 1);
    }
}

int rockColor(int z) {
    if (z == 0) return 1;
    if (z == 1) return 2;
    return 3;
}

int overlap(int sx, int sy, int rx, int ry, int z) {
    int pad;
    int rs;
    rs = rockSize(z);
    pad = 2;
    if (sx + pad < rx) return 0;
    if (sx - pad > rx + rs - 1) return 0;
    if (sy + 5 < ry) return 0;
    if (sy > ry + rs - 1) return 0;
    return 1;
}

void spawnRock(int i) {
    mz[i] = random(3);
    mx[i] = 8 + random(296);
    my[i] = -12 - (i * 22);
    ms[i] = 2 + mz[i] + random(2) + (score / 12);
    if (ms[i] > 9) ms[i] = 9;
}

void drawBar(int w) {
    if (w > 0) fill_rect(8, 4, w, 2, 2);
}

void eraseBar(int w) {
    if (w > 0) fill_rect(8, 4, w, 2, 0);
}

void initStars() {
    int i;
    i = 0;
    while (i < 24) {
        starX[i] = 4 + (i * 41) % 312;
        starY[i] = 4 + (i * 67) % 200;
        starS[i] = (i % 3) + 1;
        i = i + 1;
    }
}

void stars(int t) {
    int i;
    int c;
    i = 0;
    while (i < 24) {
        point(starX[i], starY[i], 0);
        starY[i] = starY[i] + starS[i];
        if (starY[i] > 230) {
            starY[i] = 4;
            starX[i] = 8 + (i * 37 + t) % 304;
        }
        c = ((i + t) % 3) + 1;
        point(starX[i], starY[i], c);
        i = i + 1;
    }
}

void explodeRing(int x, int y, int f) {
    int i;
    int r;
    int a;
    r = 6 + f * 8;
    i = 0;
    while (i < 12) {
        a = (i * 21 + f * 17) & 255;
        point(x + (sin256(a) * r >> 8), y + (sin256((a + 64) & 255) * r >> 8), 3 - (f & 1));
        i = i + 1;
    }
}

void drawCross() {
    line(130, 108, 190, 168, 3);
    line(190, 108, 130, 168, 3);
}

void eraseCross() {
    line(130, 108, 190, 168, 0);
    line(190, 108, 130, 168, 0);
}

void resetGame() {
    int i;
    cls(0);
    initStars();
    px = 160;
    py = 232;
    score = 0;
    barW = 0;
    alive = 1;
    dieF = 0;
    tick = 0;
    i = 0;
    while (i < 10) {
        spawnRock(i);
        i = i + 1;
    }
    drawShip(px, py, 3);
    drawScore(0, 3);
}

int main() {
    int i;
    int k;
    int oldPx;
    int moved;
    int fc;
    init(0);
    resetGame();
    while (1) {
        if (alive == 0) {
            if (dieF == 0) {
                drawShip(px, py, 0);
                drawFlame(px, py, 0);
                i = 0;
                while (i < 10) {
                    drawRock(mx[i], my[i], mz[i], 0);
                    i = i + 1;
                }
            }
            if (dieF < 5) {
                explodeRing(px, py, dieF);
                dieF = dieF + 1;
                vsync();
            } else {
                drawCross();
                k = 0;
                while (k == 0) k = getkey();
                eraseCross();
                resetGame();
            }
        } else {
            oldPx = px;
            moved = 0;
            k = getkey();
            if (k == 68) {
                if (px > 14) { px = px - 8; moved = 1; }
            }
            if (k == 67) {
                if (px < 306) { px = px + 8; moved = 1; }
            }
            if (moved == 1) {
                drawShip(oldPx, py, 0);
                drawFlame(oldPx, py, 0);
                drawShip(px, py, 3);
                fc = 2 + (tick & 1);
                drawFlame(px, py, fc);
            } else {
                drawFlame(px, py, 0);
                fc = 2 + (tick & 1);
                drawFlame(px, py, fc);
            }
            i = 0;
            while (i < 10) {
                drawRock(mx[i], my[i], mz[i], 0);
                my[i] = my[i] + ms[i];
                if (my[i] > 268) {
                    spawnRock(i);
                    my[i] = -rockSize(mz[i]);
                }
                drawRock(mx[i], my[i], mz[i], rockColor(mz[i]));
                if (overlap(px, py, mx[i], my[i], mz[i]) == 1) {
                    alive = 0;
                    dieF = 0;
                }
                i = i + 1;
            }
            tick = tick + 1;
            if (tick % 20 == 0) {
                eraseBar(barW);
                score = score + 1;
                barW = score * 3;
                if (barW > 280) barW = 280;
                drawBar(barW);
                drawScore(score, 3);
            }
            stars(tick);
            vsync();
        }
    }
    return 0;
}
