#include "i18n.h"
#include <switch.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <arpa/inet.h>
#include <sys/socket.h>
#include <sys/stat.h>
#include <fcntl.h>
#include <errno.h>
#include <unistd.h>
#include "network_config.h"
#include "mapping.h"
#include "ui.h"
#include "update.h"
#include "preferences.h"

#define PORT 8192
#define DISCOVERY_PORT 8193
#define SETTINGS_DIR "sdmc:/switch/SwitchLiteController"
#define SETTINGS_PATH SETTINGS_DIR "/settings.ini"
static UiState ui;
static PadState pad;
static Framebuffer framebuffer;
static bool framebufferReady, nifmReady, plReady, lblReady;
static int connectionSocket=-1;
static struct sockaddr_in target;
static bool discovering;
static uint64_t deadline,lastQuery,lastDraw,lastActivity;
static uint64_t suppressedButtons;
static uint32_t localAddress,localMask,broadcastAddress;
static ReleaseInfo pendingRelease;
static char selfPath[1024];
static bool requestedExit, automaticFallback, updateCancelled;

static uint64_t nowMs(void) { return armTicksToNs(armGetSystemTick())/1000000; }
static void note(const char *value) { snprintf(ui.note,sizeof(ui.note),"%s",value); }
static void formatIp(uint32_t value,char output[16]) {
    struct in_addr address={.s_addr=value};
    if(!inet_ntop(AF_INET,&address,output,16)) strcpy(output,tr("不可用","Unavailable"));
}
static bool validPc(const char *value,struct in_addr *out) {
    if(inet_pton(AF_INET,value,out)!=1) return false;
    uint32_t host=ntohl(out->s_addr), first=host>>24;
    return first!=0 && first!=127 && first<224 && host!=0xFFFFFFFFu;
}
static void saveSettings(void) {
    mkdir(SETTINGS_DIR,0777);
    LitePreferences settings={.swapAB=ui.swapAB,.swapXY=ui.swapXY,.autoConnect=ui.autoConnect,.autoUpdate=ui.autoUpdate};
    strcpy(settings.pc,ui.pc);
    if(!preferencesSave(SETTINGS_PATH,&settings)) note(tr("设置已应用，但 SD 卡保存失败。","Settings applied, but could not save to the SD card."));
}
static void loadSettings(void) {
    LitePreferences settings;
    if(!preferencesLoad(SETTINGS_PATH,&settings)) return;
    ui.swapAB=settings.swapAB;ui.swapXY=settings.swapXY;ui.autoConnect=settings.autoConnect;ui.autoUpdate=settings.autoUpdate;
    strcpy(ui.pc,settings.pc);
}
static void refreshNetwork(void) {
    if(!nifmReady) nifmReady=R_SUCCEEDED(nifmInitialize(NifmServiceType_User));
    uint32_t gateway=0;
    Result result=nifmReady?nifmGetCurrentIpConfigInfo(&localAddress,&localMask,&gateway,NULL,NULL):1;
    ui.networkValid=R_SUCCEEDED(result) && localAddress && localMask;
    if(ui.networkValid) {
        broadcastAddress=LAN_BROADCAST(localAddress,localMask);
        formatIp(localAddress,ui.local); formatIp(localMask,ui.mask);
        formatIp(gateway,ui.gateway); formatIp(broadcastAddress,ui.broadcast);
        note(tr("网络已就绪。请先在电脑选择网卡并启动服务。","Network ready. Select an adapter and start the receiver on your computer."));
    } else note(tr("未读取到 Wi-Fi 配置。连接网络后点击刷新，或手动输入电脑 IP。","Wi-Fi configuration unavailable. Connect and refresh, or enter the computer IP."));
}
static bool setScreenOff(bool off) {
    if(off && (!ui.connected || !lblReady)) { note(tr("连接电脑后才能熄屏；需要可用的背光服务。","Connect first. Turning the screen off also requires the backlight service.")); return false; }
    if(!lblReady) return false;
    Result result=off?lblSwitchBacklightOff(100000000):lblSwitchBacklightOn(100000000);
    if(R_FAILED(result)) {
        snprintf(ui.note,sizeof(ui.note),tr("背光操作失败：0x%08X；输入发送不受影响。","Backlight operation failed: 0x%08X. Input streaming continues."),result);
        return false;
    }
    ui.screenOff=off;
    if(!off) { lastDraw=0; note(tr("屏幕已唤醒，手柄继续工作。","Screen awake. Controller streaming continues.")); }
    return true;
}
static void focusHook(AppletHookType hook,void *unused) {
    (void)unused;
    if(hook==AppletHookType_OnFocusState && appletGetFocusState()!=AppletFocusState_InFocus && ui.screenOff)
        setScreenOff(false);
}
static void draw(void) {
    if(ui.screenOff || !framebufferReady) return;
    uint32_t strideBytes;
    uint32_t *pixels=framebufferBegin(&framebuffer,&strideBytes);
    uiRender(pixels,1280,720,strideBytes/4,&ui);
    framebufferEnd(&framebuffer);
    lastDraw=nowMs();
}
typedef struct { uint64_t mask; uint8_t code; } ButtonBinding;
static const ButtonBinding bindings[]={
    {HidNpadButton_Up,1},{HidNpadButton_Down,2},{HidNpadButton_Left,3},{HidNpadButton_Right,4},
    {HidNpadButton_Minus,5},{HidNpadButton_Plus,6},{HidNpadButton_StickL,7},{HidNpadButton_StickR,8},
    {HidNpadButton_L,9},{HidNpadButton_R,10},{HidNpadButton_A,12},{HidNpadButton_B,13},
    {HidNpadButton_X,14},{HidNpadButton_Y,15},{HidNpadButton_ZL,16},{HidNpadButton_ZR,17}
};
static void sendInput(uint64_t held,int lx,int ly,int rx,int ry) {
    for(unsigned i=0;i<sizeof(bindings)/sizeof(bindings[0]);i++) {
        uint8_t packet[2]={bindings[i].code,(held&bindings[i].mask)?1:0};
        sendto(connectionSocket,packet,sizeof(packet),0,(struct sockaddr *)&target,sizeof(target));
    }
    int values[2][2]={{lx,ly},{rx,ry}};
    for(int i=0;i<2;i++) {
        uint16_t x=(uint16_t)values[i][0],y=(uint16_t)values[i][1];
        uint8_t packet[5]={(uint8_t)(18+i),x>>8,x&255,y>>8,y&255};
        sendto(connectionSocket,packet,sizeof(packet),0,(struct sockaddr *)&target,sizeof(target));
    }
}
static void disconnect(void) {
    if(ui.screenOff) setScreenOff(false);
    if(ui.connected && connectionSocket>=0) sendInput(0,0,0,0,0);
    if(connectionSocket>=0) close(connectionSocket);
    connectionSocket=-1; ui.connected=false; ui.busy=false; ui.held=0;
    automaticFallback=false;
    ui.focus=UI_SEARCH;
    ui.lx=ui.ly=ui.rx=ui.ry=0;
    note(tr("已断开。可以重新连接；旧版电脑接收端需先停止再启动。","Disconnected. Reconnect anytime; older receivers may need a restart."));
}
static void beginConnection(bool automatic) {
    if(ui.connected || ui.busy) return;
    if(automatic) {
        refreshNetwork();
        if(!ui.networkValid || ntohl(localMask)>=0xFFFFFFFEu) { note(tr("当前网络不能自动搜索，请手动填写电脑 IP。","Discovery is unavailable on this network. Enter the computer IP manually.")); return; }
    }
    memset(&target,0,sizeof(target)); target.sin_family=AF_INET;
    if(!automatic && !validPc(ui.pc,&target.sin_addr)) { note(tr("请先填写有效的电脑 IPv4 地址。","Enter a valid computer IPv4 address first.")); return; }
    if(automatic) target.sin_addr.s_addr=broadcastAddress;
    target.sin_port=htons(automatic?DISCOVERY_PORT:PORT);
    connectionSocket=socket(AF_INET,SOCK_DGRAM,0);
    if(connectionSocket<0) { note(tr("无法建立网络套接字，请刷新网络后重试。","Could not open the network socket. Refresh and try again.")); return; }
    int on=1;
    if(setsockopt(connectionSocket,SOL_SOCKET,SO_BROADCAST,&on,sizeof(on))<0 || fcntl(connectionSocket,F_SETFL,O_NONBLOCK)<0) {
        disconnect(); note(tr("无法初始化非阻塞网络连接。","Could not initialize the network connection.")); return;
    }
    discovering=automatic; ui.busy=true; deadline=nowMs()+10000; lastQuery=0;
    note(automatic?tr("正在搜索当前局域网中的电脑… 按 B 取消。","Searching this LAN for a receiver... Press B to cancel."):tr("正在连接指定电脑… 按 B 取消。","Connecting to the computer... Press B to cancel."));
}
static void pollConnection(void) {
    if(!ui.busy) return;
    uint64_t now=nowMs();
    if(now>=deadline) {
        bool fallback=automaticFallback;disconnect();
        if(fallback) { beginConnection(true); return; }
        note(tr("连接超时：检查电脑网卡和防火墙，或改用手动 IP。","Connection timed out. Check the adapter/firewall, or use a manual IP.")); return;
    }
    if(!lastQuery || now-lastQuery>=250) {
        const char query[]="xbox_switch";
        if(sendto(connectionSocket,query,sizeof(query),0,(struct sockaddr *)&target,sizeof(target))<0 && errno!=EWOULDBLOCK && errno!=EAGAIN) {
            disconnect(); note(tr("网络发送失败，请检查 Wi-Fi 并刷新网络。","Network send failed. Check Wi-Fi and refresh.")); return;
        }
        lastQuery=now;
    }
    char reply[32];
    struct sockaddr_in peer={0}; socklen_t length=sizeof(peer);
    int count=recvfrom(connectionSocket,reply,sizeof(reply),0,(struct sockaddr *)&peer,&length);
    if(peer.sin_family!=AF_INET || peer.sin_port!=htons(PORT)) return;
    if(!((count==4 && memcmp(reply,"xbox",4)==0) || (count==5 && memcmp(reply,"xbox\0",5)==0))) return;
    if(!discovering && peer.sin_addr.s_addr!=target.sin_addr.s_addr) return;
    if(discovering) {
        target=peer; discovering=false; lastQuery=0; deadline=now+5000;
        formatIp(peer.sin_addr.s_addr,ui.pc); note(tr("已找到电脑，正在确认手柄连接…","Computer found. Confirming the controller connection...")); return;
    }
    ui.busy=false; ui.connected=true; ui.focus=-1;
    suppressedButtons=padGetButtons(&pad);
    formatIp(peer.sin_addr.s_addr,ui.pc); saveSettings();
    note(tr("连接成功。按键直接发送到电脑；触摸下方卡片调整设置。","Connected. Buttons stream to the computer. Tap the cards below for settings."));
}
static void manualKeyboard(void) {
    if(ui.busy || ui.connected) return;
    SwkbdConfig keyboard;
    Result result=swkbdCreate(&keyboard,0);
    if(R_FAILED(result)) { note(tr("系统键盘不可用，请使用自动搜索。","System keyboard unavailable. Use discovery instead.")); return; }
    swkbdConfigMakePresetDefault(&keyboard);
    swkbdConfigSetHeaderText(&keyboard,tr("填写 Windows 接收端显示的电脑 IP","Enter the IP shown by the computer receiver"));
    swkbdConfigSetGuideText(&keyboard,tr("例如 192.168.5.20（不是网关地址）","For example 192.168.5.20 (not the gateway)"));
    swkbdConfigSetInitialText(&keyboard,ui.pc);
    swkbdConfigSetStringLenMax(&keyboard,15); swkbdConfigSetStringLenMin(&keyboard,7);
    char value[16]={0}; result=swkbdShow(&keyboard,value,sizeof(value)); swkbdClose(&keyboard);
    if(R_SUCCEEDED(result)) {
        struct in_addr address;
        if(!validPc(value,&address)) { note(tr("IP 地址格式不正确，请重新输入。","Invalid IPv4 address. Please try again.")); return; }
        strcpy(ui.pc,value); beginConnection(false);
    }
}
static int updateProgress(size_t done,size_t total,void *unused) {
    (void)unused;
    if(!appletMainLoop()) { updateCancelled=true;return 1; }
    padUpdate(&pad);
    if(padGetButtonsDown(&pad)&HidNpadButton_B) {updateCancelled=true;return 1;}
    if(nowMs()-lastDraw>150) {
        if(total) snprintf(ui.note,sizeof(ui.note),tr("更新传输：%u%% · B 取消","Update transfer: %u%% · B cancels"),(unsigned)(done*100/total));
        draw();
    }
    return 0;
}
static void performUpdate(bool automatic) {
    if(ui.connected || ui.busy) {note(tr("请先断开连接再更新，避免中断游戏。","Disconnect before updating to avoid interrupting a game."));return;}
    bool install=ui.updateAvailable;
    ui.busy=true;updateCancelled=false;
    note(tr("正在检查 GitHub 正式版… B 取消。","Checking the latest stable GitHub release... B cancels."));draw();
    char error[128]={0};
    if(!install && !switchUpdateCheck(&pendingRelease,updateProgress,NULL,error,sizeof(error))) goto failed;
    if(!releaseNewer(pendingRelease.tag,LITE_LINK_VERSION)) {
        ui.updateAvailable=false;ui.busy=false;note(tr("已是最新版本。","Already up to date."));return;
    }
    ui.updateAvailable=true;
    if(!automatic && !install) {
        ui.busy=false;
        snprintf(ui.note,sizeof(ui.note),tr("发现 %s。再次点击“安装更新”下载、校验并替换。","Found %s. Tap Install update to download, verify and replace."),pendingRelease.tag);return;
    }
    note(tr("正在下载并校验 NRO… B 取消。","Downloading and verifying the NRO... B cancels."));draw();
    if(!switchUpdateInstall(&pendingRelease,selfPath,updateProgress,NULL,error,sizeof(error))) goto failed;
    saveSettings();ui.busy=false;requestedExit=true;
    note(tr("更新已安装。返回菜单后请重新打开 Lite Link。","Update installed. Reopen Lite Link after returning to the menu."));draw();svcSleepThread(2000000000);return;
failed:
    ui.busy=false;
    if(updateCancelled) note(tr("更新已取消，当前版本保留。","Update cancelled. Current version kept."));
    else snprintf(ui.note,sizeof(ui.note),tr("更新失败：%s","Update failed: %s"),error);
}
static void action(int which) {
    if(ui.busy) return;
    if(which==UI_SEARCH) { if(ui.connected) disconnect(); else beginConnection(true); }
    if(which==UI_MANUAL) manualKeyboard();
    if(which==UI_REFRESH && !ui.connected) refreshNetwork();
    if(which==UI_SWAP_AB) { ui.swapAB=!ui.swapAB; note(ui.swapAB?tr("A/B 交换已开启：Switch A 对应 Xbox A。","A/B swap on: Switch A maps to Xbox A."):tr("A/B 交换已关闭：按物理位置对应 Xbox 按钮。","A/B swap off: match the Xbox button positions.")); saveSettings(); }
    if(which==UI_SWAP_XY) { ui.swapXY=!ui.swapXY; note(ui.swapXY?tr("X/Y 交换已开启：Switch X 对应 Xbox X。","X/Y swap on: Switch X maps to Xbox X."):tr("X/Y 交换已关闭：按物理位置对应 Xbox 按钮。","X/Y swap off: match the Xbox button positions.")); saveSettings(); }
    if(which==UI_SCREEN) setScreenOff(!ui.screenOff);
    if(which==UI_AUTO_CONNECT) {
        ui.autoConnect=!ui.autoConnect;
        note(tr("自动连接设置已保存；下次打开将优先连接上次电脑。","Auto-connect saved. Next launch will try the last computer first.")); saveSettings();
    }
    if(which==UI_AUTO_UPDATE) {ui.autoUpdate=!ui.autoUpdate;note(tr("自动更新设置已保存，下次打开时生效。","Auto-update saved. It takes effect on the next launch."));saveSettings();}
    if(which==UI_UPDATE) performUpdate(false);
}
int main(int argc,char **argv) {
    if(argc>0 && argv[0]) snprintf(selfPath,sizeof(selfPath),"%s",argv[0]);
    if(R_SUCCEEDED(setInitialize())) {
        u64 languageCode=0; SetLanguage language=SetLanguage_ENUS;
        if(R_SUCCEEDED(setGetSystemLanguage(&languageCode)) && R_SUCCEEDED(setMakeLanguage(languageCode,&language)))
            gChinese=language==SetLanguage_ZHCN || language==SetLanguage_ZHTW || language==SetLanguage_ZHHANS || language==SetLanguage_ZHHANT;
        setExit();
    }
    loadSettings(); ui.focus=UI_SEARCH;
    Result result=framebufferCreate(&framebuffer,nwindowGetDefault(),1280,720,PIXEL_FORMAT_RGBA_8888,2);
    if(R_SUCCEEDED(result)) {
        framebufferReady=true;
        result=framebufferMakeLinear(&framebuffer);
    }
    plReady=R_SUCCEEDED(plInitialize(PlServiceType_User));
    PlFontData standard={0},chinese={0};
    if(plReady) {
        plGetSharedFontByType(&standard,PlSharedFontType_Standard);
        plGetSharedFontByType(&chinese,PlSharedFontType_ChineseSimplified);
    }
    if(R_FAILED(result) || !uiLoadFonts(standard.address,chinese.address)) {
        if(framebufferReady) framebufferClose(&framebuffer);
        if(plReady) plExit();
        consoleInit(NULL); printf("Could not initialize UI or system fonts.\nPress + to exit.\n");
        padConfigureInput(1,HidNpadStyleSet_NpadStandard); padInitializeDefault(&pad);
        while(appletMainLoop()) { padUpdate(&pad); if(padGetButtonsDown(&pad)&HidNpadButton_Plus) break; consoleUpdate(NULL); }
        consoleExit(NULL); return 1;
    }
    padConfigureInput(1,HidNpadStyleSet_NpadStandard); padInitializeDefault(&pad); hidInitializeTouchScreen();
    Result socketResult=socketInitializeDefault();
    bool socketReady=R_SUCCEEDED(socketResult);
    lblReady=R_SUCCEEDED(lblInitialize()); ui.backlightAvailable=lblReady;
    AppletHookCookie hook; appletHook(&hook,focusHook,NULL);
    refreshNetwork();
    if(!socketReady) note(tr("网络服务初始化失败，请退出后重新运行程序。","Network initialization failed. Exit and launch the app again."));
    uint64_t automaticAt=nowMs()+2000;
    bool automaticPending=socketReady && ui.autoConnect;
    bool updatePending=socketReady && ui.autoUpdate;
    bool previousTouch=false,firstFrame=true,launchTouchHeld=false;
    uint64_t launchHeld=0;
    while(!requestedExit && appletMainLoop()) {
        padUpdate(&pad);
        uint64_t held=padGetButtons(&pad),down=padGetButtonsDown(&pad);
        bool wasConnected=ui.connected;
        HidTouchScreenState touch={0}; hidGetTouchScreenStates(&touch,1);
        bool touched=touch.count>0;
        if(firstFrame) {launchHeld=held;launchTouchHeld=touched;firstFrame=false;}
        launchHeld&=held;down&=~launchHeld;
        if(!touched) launchTouchHeld=false;
        if((automaticPending || updatePending) && ((touched && !previousTouch && !launchTouchHeld) || down)) { automaticPending=false;updatePending=false;note(tr("已取消本次自动启动操作。","Automatic launch actions cancelled for this session.")); }
        if(updatePending && nowMs()>=automaticAt) {
            updatePending=false;performUpdate(true);
            if(requestedExit) break;
            if(updateCancelled) automaticPending=false;
        }
        if(automaticPending && nowMs()>=automaticAt) {
            automaticPending=false;
            if(ui.pc[0]) beginConnection(false); else beginConnection(true);
            automaticFallback=ui.pc[0]!=0 && ui.busy;
        }
        bool woke=false;
        if(ui.screenOff && touched && !previousTouch) { setScreenOff(false); woke=true; }
        const uint64_t screenChord=HidNpadButton_L|HidNpadButton_R|HidNpadButton_Minus;
        if(ui.connected && (held&screenChord)==screenChord && (down&HidNpadButton_Minus)) {
            setScreenOff(!ui.screenOff); woke=true;
        }
        if((held&(HidNpadButton_Plus|HidNpadButton_Minus))==(HidNpadButton_Plus|HidNpadButton_Minus)) break;
        if(!wasConnected && !ui.busy && (down&HidNpadButton_Plus)) break;
        if(ui.busy && (down&HidNpadButton_B)) { disconnect(); note(tr("已取消连接。","Connection cancelled.")); }
        if(!woke && touched && !previousTouch && !launchTouchHeld && !ui.screenOff) {
            int hit=uiHitTest(touch.touches[0].x,touch.touches[0].y);
            if(hit>=0 && socketReady) { ui.focus=wasConnected?-1:hit; action(hit); }
        }
        previousTouch=touched;
        if(!wasConnected && !ui.busy && !woke) {
            if(down&(HidNpadButton_Right|HidNpadButton_Down)) ui.focus=(ui.focus+1)%UI_ACTION_COUNT;
            if(down&(HidNpadButton_Left|HidNpadButton_Up)) ui.focus=(ui.focus+UI_ACTION_COUNT-1)%UI_ACTION_COUNT;
            if(socketReady && (down&HidNpadButton_A)) action(ui.focus);
            if(socketReady && (down&HidNpadButton_X)) beginConnection(true);
            if(down&HidNpadButton_Y) refreshNetwork();
        }
        pollConnection();
        if(ui.connected) {
            HidAnalogStickState left=padGetStickPos(&pad,0),right=padGetStickPos(&pad,1);
            ui.held=held; ui.lx=left.x;ui.ly=left.y;ui.rx=right.x;ui.ry=right.y;
            suppressedButtons&=held;
            held&=~suppressedButtons;
            if((ui.held&screenChord)==screenChord) held&=~screenChord;
            sendInput(mapFaceButtons(held,ui.swapAB,ui.swapXY),left.x,left.y,right.x,right.y);
            if(nowMs()-lastActivity>30000) { appletReportUserIsActive(); lastActivity=nowMs(); }
        }
        if(nowMs()-lastDraw>=50) draw();
        svcSleepThread(8333333);
    }
    disconnect();
    appletUnhook(&hook);
    if(lblReady) { if(ui.screenOff) lblSwitchBacklightOn(0); lblExit(); }
    if(nifmReady) nifmExit();
    if(socketReady) socketExit();
    uiRelease();
    if(plReady) plExit();
    framebufferClose(&framebuffer);
    return 0;
}
