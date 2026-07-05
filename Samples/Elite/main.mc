// ============================================================
//   ГАЛЕРЕЯ ELITE — выпуклые тела с отсечением невидимых граней
//   7 моделей: Tetra, Octahedron, Prism, Python, Anaconda,
//   Coriolis (станция), Thargoid. Все объёмные — задние грани
//   отсекаются (cross > 0 = лицевая), плоских «стрелок» нет.
//
//   Управление:  ← / →  — следующее/предыдущее тело
//
//   Анти-моргание (только Mini-C):
//     • syncFrame — дифф. обновление рёбер через EX/EY и PX/PY;
//     • звёзды — план в холодной зоне, рисуем до стирания;
//     • vsync — отрисовка только в горячей зоне кадра.
// ============================================================

// ── Геометрия (вершины + грани, сгенерировано и проверено) ──
int VX[52] = { 45, 45, -45, -45, 55, -55, 0, 0, 0, 0, 44, 22, -22, -44, -22, 22, 44, 22, -22, -44, -22, 22, 0, 0, 35, -35, 0, 0, 0, 0, 40, 0, -40, 0, -40, 40, 40, -40, -40, 40, 40, -40, 50, 35, 0, -35, -50, -35, 0, 35, 0, 0 };
int VY[52] = { 45, -45, 45, -45, 0, 0, 55, -55, 0, 0, 0, 38, 38, 0, -38, -38, 0, 38, 38, 0, -38, -38, 0, 0, 0, 0, 28, -28, 0, 0, 0, 30, 0, -30, -40, -40, 40, 40, -40, -40, 40, 40, 0, 35, 50, 35, 0, -35, -50, -35, 0, 0 };
int VZ[52] = { 45, -45, -45, 45, 0, 0, 0, 0, 55, -55, 34, 34, 34, 34, 34, 34, -34, -34, -34, -34, -34, -34, 80, -80, 0, 0, 0, 0, 95, -75, 0, 0, 0, 0, -40, -40, -40, -40, 40, 40, 40, 40, 0, 0, 0, 0, 0, 0, 0, 0, 25, -25 };

int FV[192] = { 0, 1, 2, 0, 3, 1, 0, 2, 3, 1, 3, 2, 0, 2, 4, 0, 4, 3, 0, 3, 5, 0, 5, 2, 1, 4, 2, 1, 3, 4, 1, 5, 3, 1, 2, 5, 0, 1, 2, 3, 4, 5, 11, 10, 9, 8, 7, 6, 6, 7, 1, 0, 7, 8, 2, 1, 8, 9, 3, 2, 9, 10, 4, 3, 10, 11, 5, 4, 11, 6, 0, 5, 0, 2, 4, 0, 4, 3, 0, 3, 5, 0, 5, 2, 1, 4, 2, 1, 3, 4, 1, 5, 3, 1, 2, 5, 0, 2, 3, 0, 3, 4, 0, 4, 5, 0, 5, 2, 1, 3, 2, 1, 4, 3, 1, 5, 4, 1, 2, 5, 3, 2, 1, 0, 4, 5, 6, 7, 0, 1, 5, 4, 2, 3, 7, 6, 1, 2, 6, 5, 4, 7, 3, 0, 8, 0, 1, 8, 1, 2, 8, 2, 3, 8, 3, 4, 8, 4, 5, 8, 5, 6, 8, 6, 7, 8, 7, 0, 9, 1, 0, 9, 2, 1, 9, 3, 2, 9, 4, 3, 9, 5, 4, 9, 6, 5, 9, 7, 6, 9, 0, 7 };
int FOFF[58] = { 0, 3, 6, 9, 12, 15, 18, 21, 24, 27, 30, 33, 36, 42, 48, 52, 56, 60, 64, 68, 72, 75, 78, 81, 84, 87, 90, 93, 96, 99, 102, 105, 108, 111, 114, 117, 120, 124, 128, 132, 136, 140, 144, 147, 150, 153, 156, 159, 162, 165, 168, 171, 174, 177, 180, 183, 186, 189 };
int FLEN[58] = { 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 6, 6, 4, 4, 4, 4, 4, 4, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 4, 4, 4, 4, 4, 4, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3 };
int MVOFF[7] = { 0, 4, 10, 22, 28, 34, 42 };
int MVCNT[7] = { 4, 6, 12, 6, 6, 8, 10 };
int MFOFF[7] = { 0, 4, 12, 20, 28, 36, 42 };
int MFCNT[7] = { 4, 8, 8, 8, 8, 6, 16 };

int PX[12];
int PY[12];
int EX[12];
int EY[12];

int starX[30];
int starY[30];
int starOX[30];
int starOY[30];
int starNC[30];
int starSpeed[30];

int edgeOld[144];
int edgeNew[144];

int FONT[119] = { 0, 0, 0, 0, 0, 0, 0, 14, 17, 17, 31, 17, 17, 17, 14, 17, 16, 16, 16, 17, 14, 30, 17, 17, 17, 17, 17, 30, 31, 16, 30, 16, 16, 16, 31, 14, 17, 16, 23, 17, 17, 14, 17, 17, 17, 31, 17, 17, 17, 14, 4, 4, 4, 4, 4, 14, 16, 16, 16, 16, 16, 16, 31, 17, 27, 21, 21, 17, 17, 17, 17, 25, 21, 19, 17, 17, 17, 14, 17, 17, 17, 17, 17, 14, 30, 17, 17, 30, 16, 16, 16, 30, 17, 17, 30, 20, 18, 17, 15, 16, 16, 14, 1, 1, 30, 31, 4, 4, 4, 4, 4, 4, 17, 17, 10, 4, 4, 4, 4 };
int NAMECH[50] = { 15, 4, 15, 13, 1, 11, 2, 15, 1, 6, 4, 3, 13, 11, 10, 12, 13, 7, 14, 9, 12, 16, 15, 6, 11, 10, 1, 10, 1, 2, 11, 10, 3, 1, 2, 11, 13, 7, 11, 8, 7, 14, 15, 6, 1, 13, 5, 11, 7, 3 };
int NOFF[7] = { 0, 5, 15, 20, 26, 34, 42 };
int NLEN[7] = { 5, 10, 5, 6, 8, 8, 8 };

void project(int m, int a, int b) {
    int i, n, base;
    int x, y, z;
    int ca, sa, cb, sb;
    int rx, rz, ry, rz2, zp;

    base = MVOFF[m];
    n = MVCNT[m];
    ca = cos256(a);  sa = sin256(a);
    cb = cos256(b);  sb = sin256(b);

    i = 0;
    while (i < n) {
        x = VX[base + i];  y = VY[base + i];  z = VZ[base + i];

        rx  = (x * ca - z * sa) / 256;
        rz  = (x * sa + z * ca) / 256;
        ry  = (y * cb - rz * sb) / 256;
        rz2 = (y * sb + rz * cb) / 256;

        zp = rz2 + 340;
        PX[i] = 160 + (rx * 256) / zp;
        PY[i] = 132 - (ry * 256) / zp;

        i = i + 1;
    }
}

// Отрисовка тела по PX/PY (только лицевые грани).
void drawModel(int m, int c) {
    int f, fend, o, n, j, v0, v1;
    int ux, uy, vx, vy, cross;

    f = MFOFF[m];
    fend = f + MFCNT[m];
    while (f < fend) {
        o = FOFF[f];
        n = FLEN[f];
        ux = PX[FV[o + 1]] - PX[FV[o]];
        uy = PY[FV[o + 1]] - PY[FV[o]];
        vx = PX[FV[o + 2]] - PX[FV[o]];
        vy = PY[FV[o + 2]] - PY[FV[o]];
        cross = ux * vy - vx * uy;

        if (cross > 0) {
            j = 0;
            while (j < n) {
                v0 = FV[o + j];
                v1 = FV[o + (j + 1) % n];
                line(PX[v0], PY[v0], PX[v1], PY[v1], c);
                j = j + 1;
            }
        }
        f = f + 1;
    }
}

// Стирание всех рёбер модели m по EX/EY — без отсечения.
void eraseModel(int m) {
    int f, fend, o, n, j, v0, v1;

    f = MFOFF[m];
    fend = f + MFCNT[m];
    while (f < fend) {
        o = FOFF[f];
        n = FLEN[f];
        j = 0;
        while (j < n) {
            v0 = FV[o + j];
            v1 = FV[o + (j + 1) % n];
            line(EX[v0], EY[v0], EX[v1], EY[v1], 0);
            j = j + 1;
        }
        f = f + 1;
    }
}

void syncFrame(int m) {
    int f, fend, o, n, j, v0, v1, tmp, key, nv;
    int ux, uy, vx, vy, cross;
    int ox0, oy0, ox1, oy1, nx0, ny0, nx1, ny1;

    nv = MVCNT[m];

    key = 0;
    while (key < 144) {
        edgeOld[key] = 0;
        edgeNew[key] = 0;
        key = key + 1;
    }

    f = MFOFF[m];
    fend = f + MFCNT[m];
    while (f < fend) {
        o = FOFF[f];
        n = FLEN[f];

        ux = EX[FV[o + 1]] - EX[FV[o]];
        uy = EY[FV[o + 1]] - EY[FV[o]];
        vx = EX[FV[o + 2]] - EX[FV[o]];
        vy = EY[FV[o + 2]] - EY[FV[o]];
        cross = ux * vy - vx * uy;
        if (cross > 0) {
            j = 0;
            while (j < n) {
                v0 = FV[o + j];
                v1 = FV[o + (j + 1) % n];
                if (v0 > v1) { tmp = v0;  v0 = v1;  v1 = tmp; }
                edgeOld[v0 * 12 + v1] = 1;
                j = j + 1;
            }
        }

        ux = PX[FV[o + 1]] - PX[FV[o]];
        uy = PY[FV[o + 1]] - PY[FV[o]];
        vx = PX[FV[o + 2]] - PX[FV[o]];
        vy = PY[FV[o + 2]] - PY[FV[o]];
        cross = ux * vy - vx * uy;
        if (cross > 0) {
            j = 0;
            while (j < n) {
                v0 = FV[o + j];
                v1 = FV[o + (j + 1) % n];
                if (v0 > v1) { tmp = v0;  v0 = v1;  v1 = tmp; }
                edgeNew[v0 * 12 + v1] = 1;
                j = j + 1;
            }
        }
        f = f + 1;
    }

    v0 = 0;
    while (v0 < nv) {
        v1 = v0 + 1;
        while (v1 < nv) {
            key = v0 * 12 + v1;
            if (edgeOld[key] != 0 || edgeNew[key] != 0) {
                ox0 = EX[v0];  oy0 = EY[v0];
                ox1 = EX[v1];  oy1 = EY[v1];
                nx0 = PX[v0];  ny0 = PY[v0];
                nx1 = PX[v1];  ny1 = PY[v1];

                if (edgeOld[key] != 0 && edgeNew[key] != 0) {
                    if (ox0 != nx0 || oy0 != ny0 || ox1 != nx1 || oy1 != ny1) {
                        line(nx0, ny0, nx1, ny1, 3);
                        line(ox0, oy0, ox1, oy1, 0);
                    }
                } else if (edgeOld[key] != 0) {
                    line(ox0, oy0, ox1, oy1, 0);
                } else {
                    line(nx0, ny0, nx1, ny1, 3);
                }
            }
            v1 = v1 + 1;
        }
        v0 = v0 + 1;
    }
}

void drawGlyph(int gx, int gy, int g, int c) {
    int r, col, bits, mask, base;
    base = g * 7;
    r = 0;
    while (r < 7) {
        bits = FONT[base + r];
        col = 0;
        mask = 16;
        while (col < 5) {
            if (bits & mask) point(gx + col, gy + r, c);
            mask = mask / 2;
            col = col + 1;
        }
        r = r + 1;
    }
}

void drawName(int m, int c) {
    int i, n, off, gx;
    n = NLEN[m];
    off = NOFF[m];
    gx = 160 - n * 3;
    i = 0;
    while (i < n) {
        drawGlyph(gx, 246, NAMECH[off + i], c);
        gx = gx + 6;
        i = i + 1;
    }
}

void starsPlan(int t) {
    int i;
    i = 0;
    while (i < 30) {
        starOX[i] = starX[i];
        starOY[i] = starY[i];
        starY[i] = starY[i] + starSpeed[i];
        if (starY[i] > 242) {
            starY[i] = 8;
            starX[i] = 10 + (i * 37 + t) % 300;
        }
        starNC[i] = ((i + t) % 3) + 1;
        i = i + 1;
    }
}

void starsRender() {
    int i;
    i = 0;
    while (i < 30) {
        point(starX[i], starY[i], starNC[i]);
        i = i + 1;
    }
    i = 0;
    while (i < 30) {
        if (starOX[i] != starX[i] || starOY[i] != starY[i])
            point(starOX[i], starOY[i], 0);
        i = i + 1;
    }
}

int main() {
    int curM, drawM, eraseM, shownM;
    int a, b, t, i, k;

    init(0);

    i = 0;
    while (i < 30) {
        starX[i] = 10 + (i * 37) % 300;
        starY[i] = 8 + (i * 53) % 235;
        starOX[i] = starX[i];
        starOY[i] = starY[i];
        starSpeed[i] = (i % 3) + 1;
        i = i + 1;
    }

    curM = 0;  a = 0;  b = 0;  t = 0;
    project(curM, a, b);
    drawModel(curM, 3);
    i = 0;
    while (i < 12) {
        EX[i] = PX[i];
        EY[i] = PY[i];
        i = i + 1;
    }
    eraseM = curM;
    drawName(curM, 1);  shownM = curM;

    a = a + 3;  b = b + 1;
    project(curM, a, b);
    drawM = curM;
    starsPlan(t);

    while (1) {
        vsync();
        syncFrame(drawM);
        i = 0;
        while (i < 12) {
            EX[i] = PX[i];
            EY[i] = PY[i];
            i = i + 1;
        }
        starsRender();

        k = getkey();
        if (k == 67) { curM = curM + 1;  if (curM > 6) curM = 0; }
        if (k == 68) { curM = curM - 1;  if (curM < 0) curM = 6; }
        if (curM != shownM) {
            drawName(shownM, 0);
            drawName(curM, 1);
            shownM = curM;
        }

        a = a + 3;  b = b + 1;
        t = t + 2;
        if (t >= 256) t = t - 256;
        project(curM, a, b);

        if (curM != drawM) {
            eraseModel(eraseM);
            drawModel(curM, 3);
            i = 0;
            while (i < 12) {
                EX[i] = PX[i];
                EY[i] = PY[i];
                i = i + 1;
            }
            eraseM = curM;
        }
        drawM = curM;
        starsPlan(t);
    }

    return 0;
}
