#include "preferences.h"
#include "release_info.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#define CHECK(x) do {if(!(x)) {fprintf(stderr,"FAIL %d: %s\n",__LINE__,#x);return 1;}} while(0)
int main(int argc,char **argv) {
    CHECK(argc==2);
    char path[1024],backup[1024];snprintf(path,sizeof(path),"%s/settings.ini",argv[1]);snprintf(backup,sizeof(backup),"%s.bak",path);
    LitePreferences saved={.swapAB=true,.autoConnect=true,.autoUpdate=true},loaded;
    strcpy(saved.pc,"192.168.5.20");
    CHECK(preferencesSave(path,&saved));CHECK(preferencesLoad(path,&loaded));
    CHECK(loaded.autoConnect && loaded.autoUpdate && loaded.swapAB && !loaded.swapXY && !strcmp(loaded.pc,saved.pc));
    saved.swapXY=true;CHECK(preferencesSave(path,&saved));CHECK(preferencesSave(path,&saved));
    CHECK(preferencesLoad(path,&loaded) && loaded.swapXY);
    remove(path);CHECK(preferencesLoad(path,&loaded));CHECK(loaded.autoConnect);
    FILE *legacy=fopen(path,"w");CHECK(legacy);fputs("swap_ab=1\npc=192.168.5.10\n",legacy);fclose(legacy);
    CHECK(preferencesLoad(path,&loaded));CHECK(loaded.swapAB && !loaded.autoConnect && !loaded.autoUpdate);
    legacy=fopen(path,"w");CHECK(legacy);fputs("auto_connect=unexpected\npc=999.168.5.20\n",legacy);fclose(legacy);
    CHECK(preferencesLoad(path,&loaded));CHECK(!loaded.autoConnect && !loaded.pc[0]);
    CHECK(releaseNewer("v0.4.0","0.3.0"));CHECK(!releaseNewer("v0.3.0","0.3.0"));
    CHECK(!releaseNewer("v0.4.0-beta","0.3.0"));CHECK(!releaseNewer("v-1.4.0","0.3.0"));
    const char *json="{\"draft\":false,\"prerelease\":false,\"tag_name\":\"v0.4.0\",\"assets\":[{\"name\":\"Lite-Link-v0.4.0-Switch.nro\",\"browser_download_url\":\"https://github.com/yukinsnow/Lite-Link/releases/download/v0.4.0/Lite-Link-v0.4.0-Switch.nro\"},{\"name\":\"SHA256SUMS.txt\",\"browser_download_url\":\"https://github.com/yukinsnow/Lite-Link/releases/download/v0.4.0/SHA256SUMS.txt\"}]}";
    ReleaseInfo release;CHECK(releaseParse(json,strlen(json),&release));CHECK(!strcmp(release.tag,"v0.4.0"));CHECK(release.assetUrl[0] && release.hashUrl[0]);
    CHECK(!releaseParse("{}",2,&release));CHECK(!releaseParse(NULL,0,&release));
    unsigned char hash[32];CHECK(releaseHash("000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f  test.nro\n","test.nro",hash));
    CHECK(hash[0]==0 && hash[31]==31);CHECK(!releaseHash("bad checksum\n","test.nro",hash));CHECK(!releaseHash(NULL,"test.nro",hash));
    puts("PASS: Switch preference persistence, repeat save, backup recovery, legacy defaults, invalid input, release metadata/version and checksum parsing");
    return 0;
}
