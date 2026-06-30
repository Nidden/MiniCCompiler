int W;
int H;
int fire[1760];
int prev[1760];

int heatColor(int v) {
    if (v < 24) return 0;
    if (v < 90) return 1;
    if (v < 170) return 2;
    return 3;
}

void drawCell(int col, int row, int c) {
    int x;
    int y;
    x = col * 8;
    y = 263 - row * 6;
    fill_rect(x, y - 5, 8, 6, c);
}

void fuel() {
    int col;
    col = 2;
    while (col < 38) {
        if (random(7) == 0) {
            fire[col] = 210 + random(45);
        } else if (fire[col] < 250) {
            fire[col] = fire[col] + random(18);
        }
        col = col + 1;
    }
}

void spread() {
    int row;
    int col;
    int i;
    int v;
    int l;
    int m;
    int r;
    row = 0;
    while (row < H - 1) {
        col = 1;
        while (col < W - 1) {
            i = col + row * W;
            l = fire[i - 1];
            m = fire[i];
            r = fire[i + 1];
            v = (l + m + m + r) / 4;
            v = v - random(5);
            if (v < 0) v = 0;
            if (col > 1 && random(16) == 0) {
                v = v + random(8);
            }
            fire[i + W] = v;
            col = col + 1;
        }
        row = row + 1;
    }
}

void render() {
    int i;
    int col;
    int row;
    int c;
    int oc;
    i = 1;
    while (i < W * H - 1) {
        c = heatColor(fire[i]);
        oc = prev[i];
        if (c != oc) {
            col = i % W;
            row = i / W;
            drawCell(col, row, c);
            prev[i] = c;
        }
        i = i + 1;
    }
}

int main() {
    int i;
    W = 40;
    H = 44;
    init(0);
    cls(0);
    i = 0;
    while (i < W * H) {
        fire[i] = 0;
        prev[i] = -1;
        i = i + 1;
    }
    while (1) {
        fuel();
        spread();
        render();
        vsync();
    }
    return 0;
}
