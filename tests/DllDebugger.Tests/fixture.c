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
