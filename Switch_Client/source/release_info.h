#ifndef RELEASE_INFO_H
#define RELEASE_INFO_H
#include <stddef.h>
#include <stdbool.h>
#define LITE_LINK_VERSION "0.3.1"
typedef struct { char tag[32],name[96],assetUrl[512],hashUrl[512]; } ReleaseInfo;
bool releaseParse(const char *json,size_t length,ReleaseInfo *out);
bool releaseNewer(const char *candidate,const char *current);
bool releaseHash(const char *manifest,const char *name,unsigned char out[32]);
#endif
