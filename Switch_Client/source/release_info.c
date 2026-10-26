#include "release_info.h"
#include <stdio.h>
#include <string.h>
#include <stdlib.h>
#include <ctype.h>
#define JSMN_PARENT_LINKS
#include "vendor/jsmn.h"
static bool equals(const char *json,jsmntok_t *token,const char *value) {
    return token->end-token->start==(int)strlen(value) && !memcmp(json+token->start,value,strlen(value));
}
static int field(const char *json,jsmntok_t *tokens,int count,int object,const char *name) {
    for(int i=object+1;i+1<count && tokens[i].start<tokens[object].end;i++)
        if(tokens[i].parent==object && tokens[i].type==JSMN_STRING && equals(json,&tokens[i],name)) return i+1;
    return -1;
}
static bool copyToken(const char *json,jsmntok_t *token,char *out,size_t capacity) {
    int length=token->end-token->start;
    if(token->type!=JSMN_STRING || length<0 || (size_t)length>=capacity) return false;
    memcpy(out,json+token->start,length);out[length]=0;return true;
}
static bool version(const char *value,unsigned int out[3]) {
    if(*value=='v' || *value=='V') value++;
    for(int i=0;i<3;i++) {
        if(!isdigit((unsigned char)*value)) return false;
        unsigned int number=0;
        while(isdigit((unsigned char)*value)) {if(number>100000) return false;number=number*10+(*value++-'0');}
        out[i]=number;
        if(i<2) {if(*value++!='.') return false;} else if(*value) return false;
    }
    return true;
}
bool releaseNewer(const char *candidate,const char *current) {
    unsigned int a[3],b[3];
    if(!version(candidate,a) || !version(current,b)) return false;
    for(int i=0;i<3;i++) if(a[i]!=b[i]) return a[i]>b[i];
    return false;
}
bool releaseParse(const char *json,size_t length,ReleaseInfo *out) {
    memset(out,0,sizeof(*out));
    if(!json || !length) return false;
    jsmntok_t *tokens=calloc(4096,sizeof(jsmntok_t));if(!tokens) return false;
    jsmn_parser parser;jsmn_init(&parser);int count=jsmn_parse(&parser,json,length,tokens,4096);
    bool valid=false;
    if(count<1 || tokens[0].type!=JSMN_OBJECT) goto done;
    int tag=field(json,tokens,count,0,"tag_name"),draft=field(json,tokens,count,0,"draft"),pre=field(json,tokens,count,0,"prerelease");
    if(tag<0 || draft<0 || pre<0 || !equals(json,&tokens[draft],"false") || !equals(json,&tokens[pre],"false")) goto done;
    if(!copyToken(json,&tokens[tag],out->tag,sizeof(out->tag))) goto done;
    unsigned int parsed[3];if(!version(out->tag,parsed)) goto done;
    snprintf(out->name,sizeof(out->name),"Lite-Link-%s-Switch.nro",out->tag);
    int assets=field(json,tokens,count,0,"assets");if(assets<0 || tokens[assets].type!=JSMN_ARRAY) goto done;
    for(int i=assets+1;i<count && tokens[i].start<tokens[assets].end;i++) {
        if(tokens[i].parent!=assets || tokens[i].type!=JSMN_OBJECT) continue;
        int name=field(json,tokens,count,i,"name"),url=field(json,tokens,count,i,"browser_download_url");
        if(name<0 || url<0) continue;
        if(equals(json,&tokens[name],out->name)) copyToken(json,&tokens[url],out->assetUrl,sizeof(out->assetUrl));
        if(equals(json,&tokens[name],"SHA256SUMS.txt")) copyToken(json,&tokens[url],out->hashUrl,sizeof(out->hashUrl));
    }
    valid=true;
done:free(tokens);return valid;
}
static int hexValue(char value) {
    if(value>='0' && value<='9') return value-'0';
    value=(char)tolower((unsigned char)value);return value>='a' && value<='f'?value-'a'+10:-1;
}
bool releaseHash(const char *manifest,const char *name,unsigned char out[32]) {
    if(!manifest || !name) return false;
    const char *line=manifest;
    while(*line) {
        const char *end=strchr(line,'\n');if(!end) end=line+strlen(line);
        if(end-line>=66) {
            const char *file=line+64;while(file<end && (*file==' ' || *file=='\t' || *file=='*')) file++;
            const char *finish=end;while(finish>file && (finish[-1]=='\r' || finish[-1]==' ')) finish--;
            if((size_t)(finish-file)==strlen(name) && !memcmp(file,name,strlen(name))) {
                for(int i=0;i<32;i++) {int a=hexValue(line[i*2]),b=hexValue(line[i*2+1]);if(a<0 || b<0) return false;out[i]=(unsigned char)(a*16+b);}
                return true;
            }
        }
        line=*end?end+1:end;
    }
    return false;
}
