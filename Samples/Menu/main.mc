// ============================================================
//   МЕНЮ С УПРАВЛЕНИЕМ — перерисовка ТОЛЬКО изменённого.
//   Меню рисуется один раз. При движении gotoxy на старую и
//   новую строку — две строки вместо всего экрана (быстро).
//   Стрелки двухбайтные: ESC + A/B (верх/низ). Инверсия ESC 243.
// ============================================================
void invOn()  { print_char(27); print_char(163); }
void invOff() { print_char(27); print_char(191); print_char(163); }

int sel;

// текст пункта по номеру
void itemText(int i) {
    if (i == 0) print_str("  Графика     ");
    if (i == 1) print_str("  Игры        ");
    if (i == 2) print_str("  Настройки   ");
    if (i == 3) print_str("  Выход       ");
}

// перерисовать ОДИН пункт: highlighted=1 — с инверсией
void drawItem(int i, int highlighted) {
    gotoxy(2, 2 + i);          // колонка 2, строка 2+i
    setTextColor(7);
    if (highlighted) invOn();
    itemText(i);
    if (highlighted) invOff();
}

void drawAll() {
    int i;
    cls(0);
    setTextColor(6);
    gotoxy(2, 0);
    print_str("=== МЕНЮ УКНЦ ===");
    i = 0;
    while (i < 4) {
        drawItem(i, i == sel);
        i = i + 1;
    }
    setTextColor(5);
    gotoxy(2, 7);
    print_str("Стрелки - выбор, ВК - выбрать");
}

int main() {
    int k;
    int k2;
    int old;
    init(0);
    sel = 0;
    drawAll();
    while (1) {
        k = getkey();
        if (k == 27) {
            k2 = getkey();
            while (k2 == 0) k2 = getkey();
            old = sel;
            if (k2 == 65) { if (sel > 0) sel = sel - 1; }   // вверх
            if (k2 == 66) { if (sel < 3) sel = sel + 1; }   // вниз
            if (old != sel) {
                drawItem(old, 0);       // снять выделение со старого
                drawItem(sel, 1);       // поставить на новый
            }
        }
        if (k == 13) {                  // ВК — выбор
            cls(0);
            setTextColor(2);
            gotoxy(2, 2);
            print_str("Выбран пункт ");
            print_int(sel);
            gotoxy(2, 4);
            setTextColor(7);
            print_str("Нажми ВК для возврата");
            k2 = 0;
            while (k2 != 13) k2 = getkey();
            drawAll();
        }
    }
    return 0;
}

