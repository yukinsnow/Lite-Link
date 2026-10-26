#include "i18n.h"
#include "ui.h"
#include "release_info.h"
#include <stdlib.h>
#include <string.h>
#include <stdio.h>
#include <math.h>
#define STB_TRUETYPE_IMPLEMENTATION
#include "vendor/stb_truetype.h"
bool gChinese=false;

typedef struct { int x,y,w,h; } Rect;
static const Rect actions[UI_ACTION_COUNT] = {
    {604,236,282,58}, {906,236,306,58}, {438,150,94,42},
    {40,458,384,122}, {448,458,384,122}, {856,458,384,122},
    {856,600,384,44}, {720,82,220,36}, {448,600,384,44}
};
static stbtt_fontinfo fonts[2];
static bool fontReady[2];
static uint32_t *buffer;
static int screenWidth, screenHeight, stride;
typedef struct { int code,size,w,h,x,y,advance; unsigned char *bitmap; } Glyph;
static Glyph cache[768];
static unsigned nextGlyph;

static uint32_t rgb(int r,int g,int b) { return (uint32_t)(r | g<<8 | b<<16 | 0xFF000000u); }
static const uint32_t WHITE=0xFFF3F1ECu, MUTED=0xFFAD9B85u, TEAL=0xFFB9E249u;
static void pixel(int x,int y,uint32_t color) {
    if (x>=0 && y>=0 && x<screenWidth && y<screenHeight) buffer[y*stride+x]=color;
}
static void rect(int x,int y,int w,int h,uint32_t color) {
    for(int yy=y;yy<y+h;yy++) {
        if(yy<0 || yy>=screenHeight) continue;
        int left=x<0?0:x, right=x+w>screenWidth?screenWidth:x+w;
        for(int xx=left;xx<right;xx++) buffer[yy*stride+xx]=color;
    }
}
static void roundRect(Rect r,int radius,uint32_t color) {
    for(int y=0;y<r.h;y++) {
        int inset=0;
        if(y<radius || y>=r.h-radius) {
            float dy=y<radius?radius-y-0.5f:y-(r.h-radius)+0.5f;
            inset=radius-(int)sqrtf((float)radius*radius-dy*dy);
        }
        rect(r.x+inset,r.y+y,r.w-2*inset,1,color);
    }
}
static void circle(int x,int y,int radius,uint32_t color) {
    for(int yy=-radius;yy<=radius;yy++) {
        int width=(int)sqrtf((float)radius*radius-yy*yy);
        rect(x-width,y+yy,width*2+1,1,color);
    }
}
bool uiLoadFonts(const void *standard,const void *chinese) {
    const void *data[2]={standard,chinese};
    for(int i=0;i<2;i++) if(data[i]) {
        int offset=stbtt_GetFontOffsetForIndex(data[i],0);
        fontReady[i]=offset>=0 && stbtt_InitFont(&fonts[i],data[i],offset);
    }
    return fontReady[0] || fontReady[1];
}
void uiRelease(void) {
    for(unsigned i=0;i<sizeof(cache)/sizeof(cache[0]);i++) {
        free(cache[i].bitmap); memset(&cache[i],0,sizeof(Glyph));
    }
}
static Glyph *glyph(int code,int size) {
    for(unsigned i=0;i<sizeof(cache)/sizeof(cache[0]);i++)
        if(cache[i].code==code && cache[i].size==size) return &cache[i];
    Glyph *g=&cache[nextGlyph++ % (sizeof(cache)/sizeof(cache[0]))];
    free(g->bitmap); memset(g,0,sizeof(*g));
    g->code=code; g->size=size;
    int font=fontReady[0]?0:1;
    if(fontReady[1] && !stbtt_FindGlyphIndex(&fonts[font],code)) font=1;
    float scale=stbtt_ScaleForPixelHeight(&fonts[font],(float)size);
    int advance;
    stbtt_GetCodepointHMetrics(&fonts[font],code,&advance,NULL);
    g->advance=(int)(advance*scale+0.5f);
    g->bitmap=stbtt_GetCodepointBitmap(&fonts[font],0,scale,code,&g->w,&g->h,&g->x,&g->y);
    return g;
}
static int nextCode(const char **p) {
    const unsigned char *s=(const unsigned char *)*p;
    int count=1, code=s[0];
    if((s[0]&0xE0)==0xC0) { count=2; code=s[0]&31; }
    else if((s[0]&0xF0)==0xE0) { count=3; code=s[0]&15; }
    else if((s[0]&0xF8)==0xF0) { count=4; code=s[0]&7; }
    for(int i=1;i<count;i++) { if(!s[i] || (s[i]&0xC0)!=0x80) { count=1;code='?';break; } code=(code<<6)|(s[i]&63); }
    *p+=count; return code;
}
static void text(int x,int top,int size,uint32_t color,const char *value,int maxWidth) {
    int start=x, baseline=top+size;
    while(*value) {
        int code=nextCode(&value);
        Glyph *g=glyph(code,size);
        if(code=='\n' || (x>start && x+g->advance>start+maxWidth)) {
            x=start; baseline+=size+7; if(code=='\n') continue;
        }
        for(int yy=0;yy<g->h;yy++) for(int xx=0;xx<g->w;xx++) {
            int a=g->bitmap[yy*g->w+xx];
            int px=x+g->x+xx,py=baseline+g->y+yy;
            if(!a || px<0 || py<0 || px>=screenWidth || py>=screenHeight) continue;
            uint32_t bg=buffer[py*stride+px];
            int r=((color&255)*a+(bg&255)*(255-a))/255;
            int green=(((color>>8)&255)*a+((bg>>8)&255)*(255-a))/255;
            int b=(((color>>16)&255)*a+((bg>>16)&255)*(255-a))/255;
            pixel(px,py,rgb(r,green,b));
        }
        x+=g->advance;
    }
}
int uiHitTest(int x,int y) {
    for(int i=0;i<UI_ACTION_COUNT;i++) if(x>=actions[i].x && x<actions[i].x+actions[i].w && y>=actions[i].y && y<actions[i].y+actions[i].h) return i;
    return -1;
}
static void button(int action,const UiState *s,const char *label,bool enabled) {
    Rect r=actions[action];
    if(s->focus==action) { Rect outline={r.x-3,r.y-3,r.w+6,r.h+6}; roundRect(outline,17,TEAL); }
    roundRect(r,14,enabled?rgb(38,66,82):rgb(29,40,53));
    int padding=action==UI_REFRESH?12:24;
    text(r.x+padding,r.y+(action==UI_REFRESH?8:14),action==UI_REFRESH?20:24,enabled?WHITE:MUTED,label,r.w-padding*2);
}
static void toggle(int action,const UiState *s,const char *title,const char *detail,bool on,bool enabled) {
    Rect r=actions[action];
    if(s->focus==action) { Rect border={r.x-3,r.y-3,r.w+6,r.h+6}; roundRect(border,21,TEAL); }
    roundRect(r,18,rgb(23,37,53));
    text(r.x+24,r.y+18,27,WHITE,title,r.w-120);
    text(r.x+24,r.y+72,20,MUTED,detail,r.w-44);
    Rect track={r.x+r.w-88,r.y+24,62,32};
    roundRect(track,16,on?TEAL:rgb(65,78,92));
    circle(track.x+(on?46:16),track.y+16,11,enabled?WHITE:rgb(120,130,140));
}
void uiRender(uint32_t *pixels,int width,int height,int stridePixels,const UiState *s) {
    buffer=pixels; screenWidth=width;screenHeight=height;stride=stridePixels;
    for(int y=0;y<height;y++) rect(0,y,width,1,rgb(10+y*3/height,18+y*5/height,31+y*8/height));
    text(40,28,43,WHITE,"LITE / LINK",500);
    text(42,83,17,TEAL,s->note,630);
    text(1008,94,18,MUTED,"GitHub @Yukinsnow",232);
    Rect update=actions[UI_UPDATE];
    if(s->focus==UI_UPDATE) { Rect border={update.x-2,update.y-2,update.w+4,update.h+4};roundRect(border,14,TEAL); }
    roundRect(update,12,rgb(28,46,62));
    text(update.x+14,update.y+6,19,MUTED,s->updateAvailable?tr("安装更新","Install update"):tr("检查更新","Check for updates"),200);
    Rect badge={1010,43,230,48}; roundRect(badge,24,rgb(24,45,54));
    circle(1036,67,6,s->connected?TEAL:rgb(239,180,95));
    text(1054,51,24,WHITE,s->connected?tr("正在传送输入","Sending input"):tr("等待连接","Not connected"),170);
    Rect network={40,132,520,280}, connection={580,132,660,280};
    roundRect(network,22,rgb(23,37,53)); roundRect(connection,22,rgb(23,37,53));
    text(64,152,22,MUTED,tr("SWITCH 网络","SWITCH NETWORK"),340);
    button(UI_REFRESH,s,tr("刷新","Refresh"),!s->connected && !s->busy);
    text(62,192,45,WHITE,s->networkValid?s->local:tr("网络未就绪","Network unavailable"),450);
    char line[96];
    snprintf(line,sizeof(line),tr("子网掩码   %s","Subnet mask   %s"),s->networkValid?s->mask:"—"); text(64,265,21,MUTED,line,460);
    snprintf(line,sizeof(line),tr("默认网关   %s","Gateway   %s"),s->networkValid?s->gateway:"—"); text(64,303,21,MUTED,line,460);
    snprintf(line,sizeof(line),tr("搜索地址   %s","Discovery   %s"),s->networkValid?s->broadcast:tr("请先连接 Wi-Fi","Connect to Wi-Fi first")); text(64,353,21,TEAL,line,460);
    text(604,151,28,WHITE,s->connected?tr("电脑已连接","Connected to computer"):s->busy?tr("正在建立连接","Connecting"):tr("连接你的 Windows 电脑","Connect to your computer"),600);
    snprintf(line,sizeof(line),tr("电脑 IP   %s","Computer IP   %s"),s->pc[0]?s->pc:tr("自动发现，或手动输入","Auto discovery or manual IP")); text(604,195,21,MUTED,line,590);
    button(UI_SEARCH,s,s->connected?tr("断开连接","Disconnect"):tr("自动搜索","Find computer"),!s->busy && (s->connected || s->networkValid));
    button(UI_MANUAL,s,tr("输入电脑 IP","Enter computer IP"),!s->connected && !s->busy);
    int cx[2]={640,755};
    for(int i=0;i<2;i++) {
        circle(cx[i],350,24,rgb(41,58,74));
        int x=i?s->rx:s->lx,y=i?s->ry:s->ly;
        circle(cx[i]+x*14/32768,350-y*14/32768,10,TEAL);
        text(cx[i]-32,378,17,MUTED,i?tr("右摇杆","Right stick"):tr("左摇杆","Left stick"),90);
    }
    const int fx[4]={1180,1147,1147,1114},fy[4]={350,378,322,350};
    const char *names[4]={"A","B","X","Y"};
    for(int i=0;i<4;i++) { circle(fx[i],fy[i],15,(s->held&(1ULL<<i))?TEAL:rgb(43,60,77)); text(fx[i]-6,fy[i]-12,19,WHITE,names[i],22); }
    text(42,423,20,MUTED,tr("手柄设置","CONTROLLER SETTINGS"),600);
    toggle(UI_SWAP_AB,s,tr("A / B 交换","Swap A / B"),s->swapAB?"Switch A → Xbox A":"Switch A → Xbox B",s->swapAB,true);
    toggle(UI_SWAP_XY,s,tr("X / Y 交换","Swap X / Y"),s->swapXY?"Switch X → Xbox X":"Switch X → Xbox Y",s->swapXY,true);
    toggle(UI_SCREEN,s,tr("关闭屏幕背光","Turn screen off"),s->backlightAvailable?tr("熄屏后，触摸任意位置唤醒","Tap anywhere to wake"):tr("背光服务不可用","Backlight service unavailable"),s->screenOff,s->backlightAvailable && s->connected);
    Rect automatic=actions[UI_AUTO_CONNECT];
    if(s->focus==UI_AUTO_CONNECT) { Rect border={automatic.x-2,automatic.y-2,automatic.w+4,automatic.h+4}; roundRect(border,15,TEAL); }
    roundRect(automatic,13,rgb(23,37,53));
    text(automatic.x+16,automatic.y+9,20,WHITE,tr("打开后自动连接","Auto-connect on launch"),270);
    Rect track={automatic.x+304,automatic.y+10,60,26};
    roundRect(track,13,s->autoConnect?TEAL:rgb(65,78,92));
    circle(track.x+(s->autoConnect?47:13),track.y+13,9,WHITE);
    Rect autoUpdate=actions[UI_AUTO_UPDATE];
    if(s->focus==UI_AUTO_UPDATE) { Rect border={autoUpdate.x-2,autoUpdate.y-2,autoUpdate.w+4,autoUpdate.h+4}; roundRect(border,15,TEAL); }
    roundRect(autoUpdate,13,rgb(23,37,53));
    text(autoUpdate.x+16,autoUpdate.y+9,20,WHITE,tr("打开后自动更新","Update on launch"),270);
    track=(Rect){autoUpdate.x+304,autoUpdate.y+10,60,26};
    roundRect(track,13,s->autoUpdate?TEAL:rgb(65,78,92));
    circle(track.x+(s->autoUpdate?47:13),track.y+13,9,WHITE);
    text(42,610,19,MUTED,"LITE LINK  " LITE_LINK_VERSION,380);
    rect(40,652,1200,1,rgb(40,54,68));
    text(42,667,19,MUTED,s->connected?tr("触摸修改设置   ·   L + R + 减号：熄屏 / 唤醒   ·   加号 + 减号：退出","Touch to change settings  ·  L + R + Minus: screen  ·  Plus + Minus: exit"):tr("方向键选择 / A 确认   ·   X 自动搜索   ·   Y 刷新网络   ·   加号退出","D-pad: select  /  A: confirm  ·  X: find computer  ·  Y: refresh  ·  Plus: exit"),1180);
}
