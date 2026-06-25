// ============================================================
//  СПИРАЛЬ — вращающаяся спираль из точек
//  Минимум работы, только point()
// ============================================================

int prevX[100];
int prevY[100];

int main() {
    int t, i, x, y, nx, ny;
    int cx, cy, radius, angle;
    
    init(0);
    cls(0);
    cx = 160;
    cy = 132;
    
    i = 0;
    while (i < 100) {
        prevX[i] = cx;
        prevY[i] = cy;
        i = i + 1;
    }
    
    t = 0;
    while (1) {
        i = 0;
        while (i < 100) {
            // Стираем старую точку
            point(prevX[i], prevY[i], 0);
            
            // Новая позиция на спирали
            angle = (i * 3 + t) & 255;
            radius = (i * 2 + t) & 63;
            
            nx = cx + (sin256(angle) * radius >> 7);
            ny = cy + (sin256((angle + 64) & 255) * radius >> 7);
            
            // Рисуем новую точку
            point(nx, ny, 3);
            
            prevX[i] = nx;
            prevY[i] = ny;
            i = i + 1;
        }
        t = t + 2;
        if (t >= 256) t = t - 256;
        vsync();
    }
    return 0;
}
