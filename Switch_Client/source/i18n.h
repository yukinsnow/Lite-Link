#ifndef LITE_LINK_I18N_H
#define LITE_LINK_I18N_H
#include <stdbool.h>
extern bool gChinese;
static inline const char *tr(const char *zh,const char *en) { return gChinese?zh:en; }
#endif
