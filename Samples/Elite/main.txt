// Elite — 4 models (C/D): 1 TET 2 OCT 3 PYT 4 COR
int VX[24] = { 45, 45, -45, -45, 55, -55, 0, 0, 0, 0, 0, 0, 35, -35, 0, 0, -40, 40, 40, -40, -40, 40, 40, -40 };
int VY[24] = { 45, -45, 45, -45, 0, 0, 55, -55, 0, 0, 0, 0, 0, 0, 28, -28, -40, -40, 40, 40, -40, -40, 40, 40 };
int VZ[24] = { 45, -45, -45, 45, 0, 0, 0, 0, 55, -55, 80, -80, 0, 0, 0, 0, -40, -40, -40, -40, 40, 40, 40, 40 };
int FV[84] = { 0, 1, 2, 0, 3, 1, 0, 2, 3, 1, 3, 2, 0, 2, 4, 0, 4, 3, 0, 3, 5, 0, 5, 2, 1, 4, 2, 1, 3, 4, 1, 5, 3, 1, 2, 5, 0, 2, 4, 0, 4, 3, 0, 3, 5, 0, 5, 2, 1, 4, 2, 1, 3, 4, 1, 5, 3, 1, 2, 5, 3, 2, 1, 0, 4, 5, 6, 7, 0, 1, 5, 4, 2, 3, 7, 6, 1, 2, 6, 5, 4, 7, 3, 0 };
int FOFF[26] = { 0, 3, 6, 9, 12, 15, 18, 21, 24, 27, 30, 33, 36, 39, 42, 45, 48, 51, 54, 57, 60, 64, 68, 72, 76, 80 };
int FLEN[26] = { 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 4, 4, 4, 4, 4, 4 };
int MVOFF[4] = { 0, 4, 10, 16 };
int MVCNT[4] = { 4, 6, 6, 8 };
int MFOFF[4] = { 0, 4, 12, 20 };
int MFCNT[4] = { 4, 8, 8, 6 };
int PX[8];
int PY[8];
int EX[8];
int EY[8];
int edgeO[64];
int edgeN[64];
int DG[20] = { 2, 7, 2, 2, 2, 7, 1, 7, 4, 7, 7, 1, 7, 1, 7, 5, 5, 7, 1, 1 };

void snapEx() {
    int i = 0;
    while (i < 8) { EX[i] = PX[i];  EY[i] = PY[i];  i = i + 1; }
}

void project(int m, int a, int b) {
    int i, n, base, x, y, z, ca, sa, cb, sb, rx, rz, ry, rz2, zp;
    base = MVOFF[m];
    n = MVCNT[m];
    ca = cos256(a);  sa = sin256(a);
    cb = cos256(b);  sb = sin256(b);
    i = 0;
    while (i < n) {
        x = VX[base + i];  y = VY[base + i];  z = VZ[base + i];
        rx = (x * ca - z * sa) / 256;
        rz = (x * sa + z * ca) / 256;
        ry = (y * cb - rz * sb) / 256;
        rz2 = (y * sb + rz * cb) / 256;
        zp = rz2 + 340;
        PX[i] = 160 + (rx * 256 + 128) / zp;
        PY[i] = 132 - (ry * 256 + 128) / zp;
        i = i + 1;
    }
}

void wire(int m, int c, int ex) {
    int f, o, n, j, v0, v1, ux, uy, vx, vy, cross;
    f = MFOFF[m];
    while (f < MFOFF[m] + MFCNT[m]) {
        o = FOFF[f];
        n = FLEN[f];
        if (ex == 0) {
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
        } else {
            j = 0;
            while (j < n) {
                v0 = FV[o + j];
                v1 = FV[o + (j + 1) % n];
                line(EX[v0], EY[v0], EX[v1], EY[v1], 0);
                j = j + 1;
            }
        }
        f = f + 1;
    }
}

void syncFrame(int m) {
    int f, o, n, j, v0, v1, t, k, nv, ux, uy, vx, vy, cross;
    int ox0, oy0, ox1, oy1, nx0, ny0, nx1, ny1;
    nv = MVCNT[m];
    k = 0;
    while (k < 64) { edgeO[k] = 0;  edgeN[k] = 0;  k = k + 1; }
    f = MFOFF[m];
    while (f < MFOFF[m] + MFCNT[m]) {
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
                if (v0 > v1) { t = v0;  v0 = v1;  v1 = t; }
                edgeO[v0 * 8 + v1] = 1;
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
                if (v0 > v1) { t = v0;  v0 = v1;  v1 = t; }
                edgeN[v0 * 8 + v1] = 1;
                j = j + 1;
            }
        }
        f = f + 1;
    }
    v0 = 0;
    while (v0 < nv) {
        v1 = v0 + 1;
        while (v1 < nv) {
            k = v0 * 8 + v1;
            if (edgeO[k] != 0 || edgeN[k] != 0) {
                ox0 = EX[v0];  oy0 = EY[v0];
                ox1 = EX[v1];  oy1 = EY[v1];
                nx0 = PX[v0];  ny0 = PY[v0];
                nx1 = PX[v1];  ny1 = PY[v1];
                if (edgeO[k] != 0 && edgeN[k] != 0) {
                    if (ox0 != nx0 || oy0 != ny0 || ox1 != nx1 || oy1 != ny1) {
                        line(ox0, oy0, ox1, oy1, 0);
                        line(nx0, ny0, nx1, ny1, 3);
                    }
                } else if (edgeO[k] != 0) {
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

void drawLabel(int m, int c) {
    int d, r, bits, col, mask, gx;
    d = m + 1;
    gx = 158;
    r = 0;
    while (r < 5) {
        bits = DG[(d - 1) * 5 + r];
        col = 0;
        mask = 4;
        while (col < 3) {
            if (bits & mask) point(gx + col, 252 + r, c);
            mask = mask / 2;
            col = col + 1;
        }
        r = r + 1;
    }
}

int main() {
    int curM, drawM, eraseM, a, b, k;
    init(0);
    curM = 0;  drawM = 0;  eraseM = 0;  a = 0;  b = 0;
    project(curM, a, b);
    wire(curM, 3, 0);
    snapEx();
    drawLabel(curM, 1);
    a = a + 3;  b = b + 1;
    project(curM, a, b);
    while (1) {
        vsync();
        syncFrame(drawM);
        snapEx();
        k = getkey();
        if (k == 67) { curM = curM + 1;  if (curM > 3) curM = 0; }
        if (k == 68) { curM = curM - 1;  if (curM < 0) curM = 3; }
        a = a + 3;  b = b + 1;
        project(curM, a, b);
        if (curM != drawM) {
            cls(0);
            wire(curM, 3, 0);
            snapEx();
            drawLabel(curM, 1);
            eraseM = curM;
        }
        drawM = curM;
    }
}
