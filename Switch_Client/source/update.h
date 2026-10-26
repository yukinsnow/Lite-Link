#ifndef LITE_LINK_UPDATE_H
#define LITE_LINK_UPDATE_H
#include "release_info.h"
typedef int (*UpdateProgress)(size_t done,size_t total,void *user);
bool switchUpdateCheck(ReleaseInfo *release,UpdateProgress progress,void *user,char *error,size_t errorSize);
bool switchUpdateInstall(const ReleaseInfo *release,const char *selfPath,UpdateProgress progress,void *user,char *error,size_t errorSize);
#endif
