// ==============================================================
//  ПАЛИТРА УКНЦ + ДЕМО-ТЕТРИС
//  Слева  — цвета символов и знакомест, бегущая строка.
//  Справа — постановочная партия: фигуры идут по сценарию и
//           складываются без единой дырки. Круг сценария даёт
//           4 линии, ещё 4 линии, затем 3. Потом всё сначала.
//  Фигура появляется по центру и подбирает свою колонку прямо
//  во время падения. Цикл бесконечный, только текстовый вывод.
// ==============================================================

#define CLS        12
#define BAR        35
#define LINE       61
#define BLANK      32

#define COL0        3
#define SCROLLROW  19
#define SCROLLW    34
#define SCROLLMAX  22

#define FX         44
#define FY          3
#define FW          8
#define FH         16
#define SPAWNX      2

#define SCRIPTLEN  22

#define TICK_STEP   12
#define TICK_SCROLL 10
#define TICK_PLACE  40

// ── поле: 0 = пусто, иначе цвет кубика ────────────────────────
int field[16][8];

// клетки текущей фигуры
int pcx[4];
int pcy[4];

// семь тетромино: по 4 пары (x,y)
// 0=I  1=O  2=T  3=S  4=Z  5=J  6=L
int shapes[7][8] = {
    {0,1, 1,1, 2,1, 3,1},
    {1,0, 2,0, 1,1, 2,1},
    {1,0, 0,1, 1,1, 2,1},
    {1,0, 2,0, 0,1, 1,1},
    {0,0, 1,0, 1,1, 2,1},
    {0,0, 0,1, 1,1, 2,1},
    {2,0, 0,1, 1,1, 2,1}
};

// сценарий партии: тип, поворот, целевая колонка
// шаги 0..7   — семь фигур мостят колонки 0..6 на четыре строки,
//               вертикальная I закрывает колодец  -> 4 линии
// шаги 8..15  — другая раскладка тех же четырёх строк -> 4 линии
// шаги 16..21 — шесть фигур на три строки во всю ширину -> 3 линии
int script[22][3] = {
    {1,0,0}, {2,0,2}, {4,0,4}, {2,0,3}, {6,3,5}, {5,2,0}, {0,0,0}, {0,1,7},
    {6,0,0}, {0,0,3}, {5,3,5}, {1,0,0}, {4,0,2}, {0,0,0}, {1,0,4}, {0,1,7},
    {6,1,4}, {1,0,0}, {1,0,2}, {4,0,5}, {0,0,0}, {5,2,5}
};

int curType;
int curRot;
int curColor;
int posX;
int posY;
int aimX;
int stepIdx;
int lines;

int scrollPos;
int scrollDir;
int placeNow;

// ── служебное ─────────────────────────────────────────────────
void delay(int frames) {
    int i;
    for (i = 0; i < frames; i++) {
        vsync();
    }
}

void repeatChar(int code, int count) {
    int i;
    for (i = 0; i < count; i++) {
        print_char(code);
    }
}

void colorName(int c) {
    switch (c) {
        case 0: print_str("черный    "); break;
        case 1: print_str("синий     "); break;
        case 2: print_str("зеленый   "); break;
        case 3: print_str("голубой   "); break;
        case 4: print_str("красный   "); break;
        case 5: print_str("малиновый "); break;
        case 6: print_str("желтый    "); break;
        case 7: print_str("белый     "); break;
        default: print_str("?         "); break;
    }
}

// цвет кубика по типу фигуры
int colorOf(int type) {
    switch (type) {
        case 0: return 3;
        case 1: return 6;
        case 2: return 5;
        case 3: return 2;
        case 4: return 4;
        case 5: return 1;
        default: return 7;
    }
}

// ── левая половина: палитра ───────────────────────────────────
void drawTitle() {
    gotoxy(COL0, 0);
    setPlaceColor(0);
    setTextColor(6);
    repeatChar(LINE, SCROLLW);
    gotoxy(COL0 + 10, 1);
    setTextColor(7);
    print_str("ПАЛИТРА УКНЦ");
    gotoxy(COL0, 2);
    setTextColor(6);
    repeatChar(LINE, SCROLLW);
}

void drawTextColors() {
    int c;
    gotoxy(COL0, 4);
    setPlaceColor(0);
    setTextColor(4);
    print_str("ЦВЕТА СИМВОЛОВ");
    for (c = 0; c < 8; c++) {
        gotoxy(COL0, 6 + c);
        setTextColor(7);
        printf("%d ", c);
        setTextColor(c);
        repeatChar(BAR, 12);
        setTextColor(7);
        print_char(BLANK);
        colorName(c);
    }
}

void showPlace(int c) {
    gotoxy(COL0, 16);
    setPlaceColor(c);
    setTextColor(7 - c);
    printf("ЗНАКОМЕСТО %d ", c);
    colorName(c);
}

// ── бегущая строка ────────────────────────────────────────────
void scrollStep() {
    gotoxy(COL0, SCROLLROW);
    setPlaceColor(0);
    repeatChar(BLANK, SCROLLW);
    gotoxy(COL0 + scrollPos, SCROLLROW);
    setPlaceColor(4);
    setTextColor(6);
    print_str(" СЛАВА КПСС ");
    scrollPos = scrollPos + scrollDir;
    if (scrollPos >= SCROLLMAX) {
        scrollDir = -1;
    }
    if (scrollPos <= 0) {
        scrollDir = 1;
    }
}

// ── тетрис: отрисовка ─────────────────────────────────────────
void drawCell(int x, int y, int color) {
    gotoxy(FX + x * 2, FY + y);
    setPlaceColor(color);
    print_str("  ");
}

void drawPiece(int color) {
    int i;
    for (i = 0; i < 4; i++) {
        drawCell(posX + pcx[i], posY + pcy[i], color);
    }
    setPlaceColor(0);
    gotoxy(0, 0);
}

void redrawField() {
    int x;
    int y;
    for (y = 0; y < FH; y++) {
        for (x = 0; x < FW; x++) {
            drawCell(x, y, field[y][x]);
        }
    }
    setPlaceColor(0);
    gotoxy(0, 0);
}

void drawFrame() {
    int y;
    gotoxy(FX + 4, 1);
    setPlaceColor(0);
    setTextColor(6);
    print_str("ТЕТРИС");
    setPlaceColor(7);
    gotoxy(FX - 2, FY - 1);
    repeatChar(BLANK, FW * 2 + 4);
    gotoxy(FX - 2, FY + FH);
    repeatChar(BLANK, FW * 2 + 4);
    for (y = 0; y < FH; y++) {
        gotoxy(FX - 2, FY + y);
        setPlaceColor(7);
        print_str("  ");
        gotoxy(FX + FW * 2, FY + y);
        setPlaceColor(7);
        print_str("  ");
    }
}

void showScore() {
    gotoxy(FX - 2, FY + FH + 1);
    setPlaceColor(0);
    setTextColor(7);
    printf("линий: %d  ", lines);
}

// ── тетрис: геометрия фигур ───────────────────────────────────
void normalize() {
    int i;
    int mx;
    int my;
    mx = pcx[0];
    my = pcy[0];
    for (i = 1; i < 4; i++) {
        if (pcx[i] < mx) { mx = pcx[i]; }
        if (pcy[i] < my) { my = pcy[i]; }
    }
    for (i = 0; i < 4; i++) {
        pcx[i] = pcx[i] - mx;
        pcy[i] = pcy[i] - my;
    }
}

void setCells(int type, int rot) {
    int i;
    int r;
    int t;
    for (i = 0; i < 4; i++) {
        pcx[i] = shapes[type][i * 2];
        pcy[i] = shapes[type][i * 2 + 1];
    }
    normalize();
    for (r = 0; r < rot; r++) {
        for (i = 0; i < 4; i++) {
            t = pcx[i];
            pcx[i] = 3 - pcy[i];
            pcy[i] = t;
        }
        normalize();
    }
}

int fits(int ox, int oy) {
    int i;
    int x;
    int y;
    for (i = 0; i < 4; i++) {
        x = ox + pcx[i];
        y = oy + pcy[i];
        if (x < 0 || x >= FW || y >= FH) {
            return 0;
        }
        if (field[y][x] != 0) {
            return 0;
        }
    }
    return 1;
}

// ── тетрис: игровая логика ────────────────────────────────────
void initField() {
    int x;
    int y;
    for (y = 0; y < FH; y++) {
        for (x = 0; x < FW; x++) {
            field[y][x] = 0;
        }
    }
}

int clearLines() {
    int x;
    int y;
    int yy;
    int full;
    int n;
    n = 0;
    for (y = 0; y < FH; y++) {
        full = 1;
        for (x = 0; x < FW; x++) {
            if (field[y][x] == 0) {
                full = 0;
            }
        }
        if (full == 1) {
            n++;
            for (yy = y; yy > 0; yy--) {
                for (x = 0; x < FW; x++) {
                    field[yy][x] = field[yy - 1][x];
                }
            }
            for (x = 0; x < FW; x++) {
                field[0][x] = 0;
            }
        }
    }
    return n;
}

void spawn() {
    curType = script[stepIdx][0];
    curRot = script[stepIdx][1];
    aimX = script[stepIdx][2];
    curColor = colorOf(curType);
    stepIdx++;
    if (stepIdx >= SCRIPTLEN) {
        stepIdx = 0;
    }
    setCells(curType, curRot);
    posX = SPAWNX;
    posY = 0;
    if (fits(posX, posY) == 0) {
        initField();
        redrawField();
    }
}

void lockPiece() {
    int i;
    int n;
    for (i = 0; i < 4; i++) {
        field[posY + pcy[i]][posX + pcx[i]] = curColor;
    }
    n = clearLines();
    if (n > 0) {
        lines = lines + n;
        redrawField();
        showScore();
    }
    spawn();
}

// шаг: подвинуться на клетку к своей колонке и опуститься на строку
void tetrisStep() {
    drawPiece(0);
    if (posX < aimX) {
        if (fits(posX + 1, posY) == 1) {
            posX++;
        }
    } else {
        if (posX > aimX) {
            if (fits(posX - 1, posY) == 1) {
                posX = posX - 1;
            }
        }
    }
    if (fits(posX, posY + 1) == 1) {
        posY++;
        drawPiece(curColor);
    } else {
        drawPiece(curColor);
        lockPiece();
    }
}

// ── главный цикл ──────────────────────────────────────────────
int main() {
    int tStep;
    int tScroll;
    int tPlace;

    setPlaceColor(0);
    setCursorColor(0);
    print_char(CLS);

    drawTitle();
    drawTextColors();

    gotoxy(COL0, 15);
    setPlaceColor(0);
    setTextColor(4);
    print_str("ЦВЕТА ЗНАКОМЕСТ");

    gotoxy(COL0, 18);
    setPlaceColor(0);
    setTextColor(4);
    print_str("БЕГУЩАЯ СТРОКА");

    initField();
    lines = 0;
    stepIdx = 0;
    drawFrame();
    redrawField();
    showScore();
    spawn();

    scrollPos = 0;
    scrollDir = 1;
    placeNow = 0;
    showPlace(placeNow);
    scrollStep();

    tStep = 0;
    tScroll = 0;
    tPlace = 0;

    while (1) {
        delay(1);

        tStep++;
        if (tStep >= TICK_STEP) {
            tStep = 0;
            tetrisStep();
        }

        tScroll++;
        if (tScroll >= TICK_SCROLL) {
            tScroll = 0;
            scrollStep();
        }

        tPlace++;
        if (tPlace >= TICK_PLACE) {
            tPlace = 0;
            placeNow++;
            if (placeNow > 7) {
                placeNow = 0;
            }
            showPlace(placeNow);
        }
    }

    return 0;
}
