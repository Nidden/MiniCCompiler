// ============================================================
//   ВОЛНЫ — лёгкая демосцена для медленного УКНЦ.
//   Две тонкие волны бегут по экрану. Каждый кадр стираем
//   только старую точку и рисуем новую — без cls, минимум работы.
//   sin256, 4 цвета. Бесконечный цикл с vsync.
// ============================================================
int prevA[40];      // прошлые Y волны A
int prevB[40];      // прошлые Y волны B

int main() {
    int t;
    int col;
    int x;
    int ya;
    int yb;
    init(0);
    cls(0);
    col = 0;
    while (col < 40) { prevA[col] = 130; prevB[col] = 130; col = col + 1; }
    t = 0;
    while (1) {
        col = 0;
        while (col < 40) {
            x = col * 8;
            // две волны разной фазы и высоты
            ya = 100 + (sin256((col * 6 + t)       & 255) * 40 >> 7);
            yb = 150 + (sin256((col * 5 + t + 90)  & 255) * 30 >> 7);
            // стереть старые точки
            point(x,     prevA[col], 0);
            point(x + 1, prevA[col], 0);
            point(x,     prevB[col], 0);
            point(x + 1, prevB[col], 0);
            // нарисовать новые
            point(x,     ya, 2);
            point(x + 1, ya, 2);
            point(x,     yb, 3);
            point(x + 1, yb, 3);
            // запомнить
            prevA[col] = ya;
            prevB[col] = yb;
            col = col + 1;
        }
        t = t + 2;
        if (t >= 256) t = t - 256;
        vsync();
    }
    return 0;
}
