#include <switch.h>
#include <curl/curl.h>
#include <stdio.h>
#include <string.h>
#include <stdlib.h>
#include <stdint.h>
#include "update.h"
typedef struct { unsigned char *data;size_t size,limit;UpdateProgress progress;void *user; } Download;
static size_t receiveBytes(char *bytes,size_t size,size_t count,void *user) {
    Download *download=user;
    if(size && count>SIZE_MAX/size) return 0;
    size_t length=size*count;
    if(length>download->limit-download->size) return 0;
    unsigned char *grown=realloc(download->data,download->size+length+1);if(!grown) return 0;
    download->data=grown;memcpy(grown+download->size,bytes,length);download->size+=length;grown[download->size]=0;return length;
}
static int progressCallback(void *user,curl_off_t total,curl_off_t done,curl_off_t uploadTotal,curl_off_t uploadDone) {
    (void)uploadTotal;(void)uploadDone;Download *download=user;
    return download->progress?download->progress((size_t)done,(size_t)total,download->user):0;
}
static bool fetch(const char *url,Download *download,char *error,size_t capacity) {
    if(curl_global_init(CURL_GLOBAL_DEFAULT)!=CURLE_OK) {snprintf(error,capacity,"HTTPS initialization failed");return false;}
    CURL *curl=curl_easy_init();if(!curl) {curl_global_cleanup();return false;}
    curl_easy_setopt(curl,CURLOPT_URL,url);
    curl_easy_setopt(curl,CURLOPT_USERAGENT,"Lite-Link/" LITE_LINK_VERSION);
    curl_easy_setopt(curl,CURLOPT_FOLLOWLOCATION,1L);curl_easy_setopt(curl,CURLOPT_MAXREDIRS,5L);
    curl_easy_setopt(curl,CURLOPT_PROTOCOLS,(long)CURLPROTO_HTTPS);curl_easy_setopt(curl,CURLOPT_REDIR_PROTOCOLS,(long)CURLPROTO_HTTPS);
    curl_easy_setopt(curl,CURLOPT_SSL_VERIFYPEER,1L);curl_easy_setopt(curl,CURLOPT_SSL_VERIFYHOST,2L);
    curl_easy_setopt(curl,CURLOPT_CONNECTTIMEOUT,10L);curl_easy_setopt(curl,CURLOPT_TIMEOUT,90L);
    curl_easy_setopt(curl,CURLOPT_FAILONERROR,1L);curl_easy_setopt(curl,CURLOPT_ACCEPT_ENCODING,"identity");
    curl_easy_setopt(curl,CURLOPT_WRITEFUNCTION,receiveBytes);curl_easy_setopt(curl,CURLOPT_WRITEDATA,download);
    curl_easy_setopt(curl,CURLOPT_NOPROGRESS,0L);curl_easy_setopt(curl,CURLOPT_XFERINFOFUNCTION,progressCallback);curl_easy_setopt(curl,CURLOPT_XFERINFODATA,download);
    CURLcode result=curl_easy_perform(curl);
    if(result!=CURLE_OK) snprintf(error,capacity,"HTTPS: %s",curl_easy_strerror(result));
    curl_easy_cleanup(curl);curl_global_cleanup();return result==CURLE_OK;
}
bool switchUpdateCheck(ReleaseInfo *release,UpdateProgress progress,void *user,char *error,size_t errorSize) {
    Download download={.limit=1024*1024,.progress=progress,.user=user};
    bool success=fetch("https://api.github.com/repos/yukinsnow/Lite-Link/releases/latest",&download,error,errorSize);
    if(success && !releaseParse((const char *)download.data,download.size,release)) {success=false;snprintf(error,errorSize,"Invalid stable release metadata");}
    free(download.data);return success;
}
bool switchUpdateInstall(const ReleaseInfo *release,const char *selfPath,UpdateProgress progress,void *user,char *error,size_t errorSize) {
    const char *allowed="https://github.com/yukinsnow/Lite-Link/releases/download/";
    size_t pathLength=strlen(selfPath);
    if(strncmp(selfPath,"sdmc:/",6) || pathLength<10 || pathLength>900 || strcmp(selfPath+pathLength-4,".nro")) {snprintf(error,errorSize,"Cannot identify the running NRO path");return false;}
    FILE *current=fopen(selfPath,"rb");char currentMagic[4]={0};
    if(current) {if(!fseek(current,16,SEEK_SET)) fread(currentMagic,1,4,current);fclose(current);}
    if(memcmp(currentMagic,"NRO0",4)) {snprintf(error,errorSize,"Current NRO missing or invalid; backup kept");return false;}
    if(strncmp(release->assetUrl,allowed,strlen(allowed)) || strncmp(release->hashUrl,allowed,strlen(allowed))) {snprintf(error,errorSize,"Release has no supported NRO/checksum assets");return false;}
    Download hashes={.limit=128*1024,.progress=progress,.user=user};
    Download nro={.limit=32*1024*1024,.progress=progress,.user=user};
    bool success=false;unsigned char expected[32],actual[32];
    if(!fetch(release->hashUrl,&hashes,error,errorSize)) goto done;
    if(!releaseHash((const char *)hashes.data,release->name,expected)) {snprintf(error,errorSize,"NRO checksum missing");goto done;}
    if(!fetch(release->assetUrl,&nro,error,errorSize)) goto done;
    sha256CalculateHash(actual,nro.data,nro.size);
    if(memcmp(actual,expected,32)) {snprintf(error,errorSize,"SHA-256 checksum mismatch");goto done;}
    if(nro.size<0x80 || memcmp(nro.data+16,"NRO0",4)) {snprintf(error,errorSize,"Invalid NRO file");goto done;}
    uint32_t programSize;memcpy(&programSize,nro.data+24,sizeof(programSize));
    if(programSize<0x80 || programSize>nro.size-4 || memcmp(nro.data+programSize,"ASET",4)) {snprintf(error,errorSize,"Invalid NRO asset header");goto done;}
    char temporary[1024],backup[1024];snprintf(temporary,sizeof(temporary),"%s.new",selfPath);snprintf(backup,sizeof(backup),"%s.bak",selfPath);
    FILE *file=fopen(temporary,"wb");if(!file) {snprintf(error,errorSize,"Cannot write to SD card");goto done;}
    size_t written=fwrite(nro.data,1,nro.size,file);int closed=fclose(file);
    if(written!=nro.size || closed) {snprintf(error,errorSize,"SD write failed; current NRO unchanged");goto done;}
    remove(backup);
    if(rename(selfPath,backup)) {snprintf(error,errorSize,"Cannot back up the current NRO");goto done;}
    if(rename(temporary,selfPath)) {
        int restored=rename(backup,selfPath);
        snprintf(error,errorSize,restored?"Install failed. Restore .nro.bak manually":"Install failed; previous NRO restored");goto done;
    }
    success=true;
done:free(hashes.data);free(nro.data);return success;
}
