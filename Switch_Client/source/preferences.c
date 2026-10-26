#include "preferences.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <ctype.h>
static bool ipv4(const char *value) {
    unsigned int first=0;
    for(int i=0;i<4;i++) {
        if(!isdigit((unsigned char)*value)) return false;
        unsigned int number=0,digits=0;
        while(isdigit((unsigned char)*value)) {if(++digits>3) return false;number=number*10+(*value++-'0');}
        if(number>255) return false;
        if(i==0) first=number;
        if(i<3) {if(*value++!='.') return false;} else if(*value) return false;
    }
    return first>0 && first!=127 && first<224;
}
bool preferencesLoad(const char *path,LitePreferences *out) {
    memset(out,0,sizeof(*out));
    FILE *file=fopen(path,"r");
    if(!file) {char backup[1024];if(strlen(path)>1018) return false;snprintf(backup,sizeof(backup),"%s.bak",path);file=fopen(backup,"r");}
    if(!file) return false;
    char line[128];
    while(fgets(line,sizeof(line),file)) {
        line[strcspn(line,"\r\n")]=0;
        char *value=strchr(line,'=');if(!value) continue;*value++=0;
        if(!strcmp(line,"pc") && strlen(value)<sizeof(out->pc) && ipv4(value)) strcpy(out->pc,value);
        if(strcmp(value,"0") && strcmp(value,"1")) continue;
        bool enabled=*value=='1';
        if(!strcmp(line,"swap_ab")) out->swapAB=enabled;
        if(!strcmp(line,"swap_xy")) out->swapXY=enabled;
        if(!strcmp(line,"auto_connect")) out->autoConnect=enabled;
        if(!strcmp(line,"auto_update")) out->autoUpdate=enabled;
    }
    int failed=ferror(file);fclose(file);return !failed;
}
bool preferencesSave(const char *path,const LitePreferences *settings) {
    if(strlen(path)>1018) return false;
    char temporary[1024],backup[1024];snprintf(temporary,sizeof(temporary),"%s.tmp",path);snprintf(backup,sizeof(backup),"%s.bak",path);
    FILE *file=fopen(temporary,"w");if(!file) return false;
    int written=fprintf(file,"swap_ab=%d\nswap_xy=%d\nauto_connect=%d\nauto_update=%d\npc=%s\n",settings->swapAB,settings->swapXY,settings->autoConnect,settings->autoUpdate,settings->pc);
    int closed=fclose(file);if(written<0 || closed) return false;
    bool hadPrevious=false;
    file=fopen(path,"r");if(file) {fclose(file);hadPrevious=true;remove(backup);if(rename(path,backup)) return false;}
    if(rename(temporary,path)) {if(hadPrevious) rename(backup,path);return false;}
    return true;
}
