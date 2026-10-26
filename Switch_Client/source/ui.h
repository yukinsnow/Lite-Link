#ifndef LITE_UI_H
#define LITE_UI_H
#include <stdint.h>
#include <stdbool.h>
enum { UI_SEARCH, UI_MANUAL, UI_REFRESH, UI_SWAP_AB, UI_SWAP_XY, UI_SCREEN, UI_AUTO_CONNECT, UI_UPDATE, UI_AUTO_UPDATE, UI_ACTION_COUNT };
typedef struct {
    bool connected, networkValid, swapAB, swapXY, screenOff, backlightAvailable, busy, autoConnect, autoUpdate, updateAvailable;
    int focus;
    uint64_t held;
    int lx, ly, rx, ry;
    char local[16], mask[16], gateway[16], broadcast[16], pc[16];
    char note[192];
} UiState;
bool uiLoadFonts(const void *standard, const void *chinese);
void uiRelease(void);
void uiRender(uint32_t *pixels, int width, int height, int stridePixels, const UiState *state);
int uiHitTest(int x, int y);
#endif
