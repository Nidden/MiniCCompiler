// ============================================================
//  DOOM — проявляется в цикле заполнения. Силуэт проверяется
//  ПЛОСКО (без вложенных функций) — надёжно и быстро.
//  Блоки 4x4, шаг 347. Блок в DOOM → цвет 3, иначе → 1.
// ============================================================

// Точка (x,y) в силуэте DOOM? Все прямоугольники проверяются
// прямо здесь, без вложенных вызовов.
int inDoom(int x, int y) {
    // буква D (lx=30, ly=68)
    if (x>=30  && x<44  && y>=68  && y<196) return 1;   // лев. вертикаль
    if (x>=30  && x<72  && y>=68  && y<82)  return 1;   // верх
    if (x>=30  && x<72  && y>=182 && y<196) return 1;   // низ
    if (x>=72  && x<86  && y>=82  && y<182) return 1;   // прав. тело
    // буква O (lx=98)
    if (x>=98  && x<112 && y>=68  && y<196) return 1;
    if (x>=140 && x<154 && y>=68  && y<196) return 1;
    if (x>=98  && x<154 && y>=68  && y<82)  return 1;
    if (x>=98  && x<154 && y>=182 && y<196) return 1;
    // буква O (lx=166)
    if (x>=166 && x<180 && y>=68  && y<196) return 1;
    if (x>=208 && x<222 && y>=68  && y<196) return 1;
    if (x>=166 && x<222 && y>=68  && y<82)  return 1;
    if (x>=166 && x<222 && y>=182 && y<196) return 1;
    // буква M (lx=234)
    if (x>=234 && x<248 && y>=68  && y<196) return 1;   // лев. вертикаль
    if (x>=276 && x<290 && y>=68  && y<196) return 1;   // прав. вертикаль
    if (x>=248 && x<256 && y>=68  && y<96)  return 1;   // V лев
    if (x>=253 && x<261 && y>=92  && y<116) return 1;
    if (x>=258 && x<266 && y>=112 && y<130) return 1;   // центр V
    if (x>=268 && x<276 && y>=68  && y<96)  return 1;   // V прав
    if (x>=263 && x<271 && y>=92  && y<116) return 1;
    return 0;
}

void draw_block(int x, int y, int c) {
    point(x,   y,   c); point(x+1, y,   c); point(x+2, y,   c); point(x+3, y,   c);
    point(x,   y+1, c); point(x+1, y+1, c); point(x+2, y+1, c); point(x+3, y+1, c);
    point(x,   y+2, c); point(x+1, y+2, c); point(x+2, y+2, c); point(x+3, y+2, c);
    point(x,   y+3, c); point(x+1, y+3, c); point(x+2, y+3, c); point(x+3, y+3, c);
}

int main() {
    int bx;
    int by;
    int carry;
    int total;
    int x;
    int y;
    int c;
    cls(0);
    bx = 0;
    by = 0;
    total = 0;
    while (total < 5280) {
        x = bx * 4;
        y = by * 4;
        c = inDoom(x + 2, y + 2) ? 2 : 1;   // буквы цвет 2, фон цвет 1
        draw_block(x, y, c);
        bx = bx + 27;
        carry = 0;
        if (bx >= 80) { bx = bx - 80; carry = 1; }
        by = by + 4 + carry;
        if (by >= 66) by = by - 66;
        total = total + 1;
    }
    return 0;
}

