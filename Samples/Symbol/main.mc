// ============================================================
//   ДЕМОНСТРАЦИЯ ЦВЕТА ТЕКСТА — УКНЦ (Электроника МС-0511)
//   setTextColor(c)   — цвет символа     (ESC 160), построчно
//   setPlaceColor(c)  — цвет знакоместа  (ESC 161), глобально
//   setCursorColor(c) — цвет курсора     (ESC 167)
//   Все цвета 0..7. Кириллица (КОИ-8).
// ============================================================
int main() {
    int i;
    init(0);
    cls(0);

    // ── Заголовок ──
    setTextColor(7);
    print_str("=== ЦВЕТА ТЕКСТА УКНЦ ===");
    print_nl();
    print_nl();

    // ── Все 8 цветов символа ──
    setTextColor(7);
    print_str("Цвет символа 0-7:");
    print_nl();
    i = 0;
    while (i < 8) {
        setTextColor(i);
        print_str("  ## цвет ");
        print_int(i);
        print_str(" ##");
        print_nl();
        i = i + 1;
    }
    print_nl();

    // ── Комбинации символ + знакоместо ──
    setTextColor(7);
    setPlaceColor(0);
    print_str("Символ на знакоместе:");
    print_nl();

    setPlaceColor(1);
    setTextColor(7);
    print_str("  белый символ / поле 1  ");
    print_nl();

    setPlaceColor(4);
    setTextColor(0);
    print_str("  чeрный символ / поле 4  ");
    print_nl();

    setPlaceColor(2);
    setTextColor(6);
    print_str("  цвет 6 символ / поле 2  ");
    print_nl();

    // ── Вернуть нормальные цвета ──
    setPlaceColor(0);
    setTextColor(7);
    print_nl();
    print_str("=== ГОТОВО ===");

    // курсор зелёным (если виден)
    setCursorColor(4);

    while (1) { }
    return 0;
}

