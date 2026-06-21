// ============================================================
// DOOM финал: один надёжный шаг 347, блоки 4x4
// НОД(347, 5280) = 1 — все 5280 блоков ровно по одному разу
// ============================================================

void draw_block(int bx, int by) {
    int x;
    int y;
    x = bx * 4;
    y = by * 4;
    point(x,   y,   1); point(x+1, y,   1); point(x+2, y,   1); point(x+3, y,   1);
    point(x,   y+1, 1); point(x+1, y+1, 1); point(x+2, y+1, 1); point(x+3, y+1, 1);
    point(x,   y+2, 1); point(x+1, y+2, 1); point(x+2, y+2, 1); point(x+3, y+2, 1);
    point(x,   y+3, 1); point(x+1, y+3, 1); point(x+2, y+3, 1); point(x+3, y+3, 1);
}

int main() {
    int bx;
    int by;
    int carry;
    int total;

    cls(0);

    bx = 0;
    by = 0;
    total = 0;

    while (total < 5280) {
        draw_block(bx, by);

        bx = bx + 27;
        carry = 0;
        if (bx >= 80) { bx = bx - 80; carry = 1; }
        by = by + 4 + carry;
        if (by >= 66) by = by - 66;

        total = total + 1;
    }

    return 0;
}
