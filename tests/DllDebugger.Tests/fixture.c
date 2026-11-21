int data = 7;
int add(int a, int b) { return a + b; }
int hidden(void) { return 42; }
int _fltused;
double twice(double v) { return 2 * v; }
static int active;
static int counter;
void *open_handle(void) { active = 1; return &active; }
int use_handle(void *h) { return h == &active && active; }
void close_handle(void *h) { if (h == &active) active = 0; }
int count(void) { return ++counter; }
void spin(void) { for (;;) {} }
int fill(const char *text, char *dst, int *length) {
    int i = 0;
    while (i < 3 && text[i]) { dst[i] = text[i]; ++i; }
    dst[i] = 0;
    return *length = i;
}
struct Record { unsigned char tag; __SIZE_TYPE__ size; int value; };
int adjust(struct Record *r) { r->value += r->tag + (int)r->size; return sizeof(*r); }
