#ifndef LITE_PREFERENCES_H
#define LITE_PREFERENCES_H
#include <stdbool.h>
typedef struct { bool swapAB,swapXY,autoConnect,autoUpdate;char pc[16]; } LitePreferences;
bool preferencesLoad(const char *path,LitePreferences *out);
bool preferencesSave(const char *path,const LitePreferences *settings);
#endif
