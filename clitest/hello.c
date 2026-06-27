// Hello-world Mini-C program for UKNC (Macro-11 target)
int square(int n) {
    return n * n;
}

int main() {
    int i;
    init(0);
    cls(0);

    setTextColor(7);
    print_str("HELLO UKNC FROM MINI-C");
    print_nl();

    i = 1;
    while (i <= 5) {
        print_str("square(");
        print_int(i);
        print_str(") = ");
        print_int(square(i));
        print_nl();
        i = i + 1;
    }

    return 0;
}
