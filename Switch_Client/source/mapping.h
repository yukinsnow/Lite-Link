#ifndef LITE_MAPPING_H
#define LITE_MAPPING_H
#include <stdint.h>
/* libnx face button bits: A=0, B=1, X=2, Y=3. Other bits are preserved. */
static inline uint64_t mapFaceButtons(uint64_t held, int swapAB, int swapXY) {
    if (swapAB) held = (held & ~UINT64_C(3)) | ((held & 1) << 1) | ((held & 2) >> 1);
    if (swapXY) held = (held & ~UINT64_C(12)) | ((held & 4) << 1) | ((held & 8) >> 1);
    return held;
}
#endif
