#include "ui.h"
#include "i18n.h"
#include "mapping.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <assert.h>
#undef assert
#define assert(condition) do { if(!(condition)) { fprintf(stderr,"FAIL: %s at %d\n",#condition,__LINE__); exit(1); } } while(0)
int main(int argc,char **argv) {
    assert(argc>=3);
    for(uint64_t face=0;face<16;face++) for(int ab=0;ab<2;ab++) for(int xy=0;xy<2;xy++) {
        uint64_t original=face | (1ULL<<42),mapped=mapFaceButtons(original,ab,xy);
        assert(mapFaceButtons(mapped,ab,xy)==original);
        assert((mapped&(1ULL<<42))!=0);
        assert(!!(mapped&1)==!!(face&(ab?2:1)));
        assert(!!(mapped&2)==!!(face&(ab?1:2)));
        assert(!!(mapped&4)==!!(face&(xy?8:4)));
        assert(!!(mapped&8)==!!(face&(xy?4:8)));
    }
    assert(uiHitTest(620,250)==UI_SEARCH);
    assert(uiHitTest(60,500)==UI_SWAP_AB);
    assert(uiHitTest(470,500)==UI_SWAP_XY);
    assert(uiHitTest(900,500)==UI_SCREEN);
    assert(uiHitTest(0,0)==-1);
    puts("PASS: all 64 face button combinations, independent swaps, other bits, hit regions");fflush(stdout);
    FILE *fontFile=fopen(argv[1],"rb"); assert(fontFile);
    fseek(fontFile,0,SEEK_END);long fontSize=ftell(fontFile);rewind(fontFile);
    unsigned char *font=malloc(fontSize);assert(fread(font,1,fontSize,fontFile)==(size_t)fontSize);fclose(fontFile);
    fprintf(stderr,"Font bytes: %ld\n",fontSize);
    assert(uiLoadFonts(font,font));
    gChinese=argc>3 && strcmp(argv[3],"zh")==0;
    fprintf(stderr,"Font loaded\n");
    uint32_t *pixels=calloc(1280*720,sizeof(uint32_t));
    UiState state={0}; state.networkValid=true;state.backlightAvailable=true;state.focus=UI_SEARCH;
    strcpy(state.local,"192.168.5.50");strcpy(state.mask,"255.255.255.0");strcpy(state.gateway,"192.168.5.1");strcpy(state.broadcast,"192.168.5.255");
    strcpy(state.pc,"192.168.5.20");strcpy(state.note,tr("网络已就绪。请先在电脑选择网卡并启动服务。","Network ready. Select an adapter and start the receiver on your computer."));
    for(int page=0;page<2;page++) {
        if(page) { state.connected=true;state.swapAB=true;state.swapXY=true;state.focus=-1;state.held=1;state.lx=12000;strcpy(state.note,tr("连接成功。按键直接发送到电脑；触摸下方卡片调整设置。","Connected. Buttons stream to the computer. Tap the cards below for settings.")); }
        fprintf(stderr,"Rendering page %d\n",page);
        uiRender(pixels,1280,720,1280,&state);
        fprintf(stderr,"Rendered page %d\n",page);
        char path[512];snprintf(path,sizeof(path),"%s-%s.ppm",argv[2],page?"connected":"idle");
        FILE *image=fopen(path,"wb");assert(image);fprintf(image,"P6\n1280 720\n255\n");
        for(int i=0;i<1280*720;i++) { unsigned char color[3]={pixels[i]&255,(pixels[i]>>8)&255,(pixels[i]>>16)&255};fwrite(color,1,3,image); }
        fclose(image);
    }
    uiRelease();free(font);free(pixels);
    puts("PASS: actual C renderer generated idle and connected frames");
    return 0;
}
