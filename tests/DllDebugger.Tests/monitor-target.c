#include <windows.h>
__declspec(dllimport) int __cdecl add(int a, int b);
__declspec(dllimport) int __cdecl count(void);

void start(void) {
    int sum = 0;
    for (int i = 0; i < 10; ++i) {
        sum += add(i, 100);
        count();
    }
    ExitProcess(sum == 1045 ? 0 : 1);
}
