"""Package downloaded GitHub Actions artifacts, including assets needed by the updater.

python tools/package_release.py --version 0.3.0 --artifacts artifacts --output dist
"""
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED
import argparse,copy,hashlib,re,struct

parser=argparse.ArgumentParser()
parser.add_argument('--version',required=True)
parser.add_argument('--artifacts',type=Path,required=True)
parser.add_argument('--output',type=Path,required=True)
args=parser.parse_args()
if not re.fullmatch(r'\d+\.\d+\.\d+',args.version):parser.error('Expected X.Y.Z')
tag='v'+args.version
root=Path(__file__).resolve().parents[1]
ci=args.artifacts;out=args.output;out.mkdir(parents=True,exist_ok=True)
common={'LICENSE':root/'LICENSE','NOTICE.md':root/'NOTICE.md'}
source=f'https://github.com/yukinsnow/Lite-Link/tree/{tag}\n'
def package(path,files,text):
    with ZipFile(path,'w',ZIP_DEFLATED) as z:
        for name,file in files.items():z.write(file,name)
        z.writestr('INSTALL.txt',text)
        z.writestr('SOURCE-URL.txt',source)
names=['NXInput Server.exe','NXInput Server.exe.config','Nefarius.ViGEm.Client.dll']
executable=(ci/'Lite-Link-Windows'/names[0]).read_bytes()
assert b'asInvoker' in executable or 'asInvoker'.encode('utf-16le') in executable,'Windows updater helper must not require elevation'
package(out/f'Lite-Link-{tag}-Windows.zip',{
    **common,'LICENSE-ViGEm.NET.txt':root/'docs/LICENSE-ViGEm.NET.txt',
    **{'Windows/'+name:ci/'Lite-Link-Windows'/name for name in names},
},f'Lite Link {tag} — GitHub @Yukinsnow\n\nKeep all files in Windows together. Install ViGEmBus if needed. Select an adapter and start the server. Login startup, automatic receiving and automatic updates are separate opt-in options; defaults are off. Use Check updates to check manually and confirm installation. Stop receiving before installing an update. Settings are kept under %APPDATA%/LiteLink.\n\n中文：需要 ViGEmBus。保持 Windows 文件夹文件在一起，选择网卡并启动服务。登录自启动、打开后接收、自动更新均需用户手动开启，默认关闭。独立检查更新按钮需确认后才安装，安装前停止接收。\n')
nro=ci/'Lite-Link-Switch/Switch_Client.nro';data=nro.read_bytes()
assert len(data)>128 and data[16:20]==b'NRO0'
size=struct.unpack_from('<I',data,24)[0];assert data[size:size+4]==b'ASET'
icon_offset,icon_size=struct.unpack_from('<QQ',data,size+8)
assert data[size+icon_offset:size+icon_offset+icon_size]==(root/'assets/icons/C-sunrise-pad.jpg').read_bytes()
raw=out/f'Lite-Link-{tag}-Switch.nro';raw.write_bytes(data)
package(out/f'Lite-Link-{tag}-Switch.zip',{
    **common,'LICENSE-stb.txt':root/'Switch_Client/source/vendor/LICENSE-stb.txt',
    'LICENSE-jsmn.txt':root/'Switch_Client/source/vendor/LICENSE-jsmn.txt',
    'LICENSE-curl.txt':root/'docs/LICENSE-curl.txt','LICENSE-zlib.txt':root/'docs/LICENSE-zlib.txt',
    'sdcard/switch/SwitchLiteController/SwitchLiteController.nro':nro,
},f'Lite Link {tag} — GitHub @Yukinsnow\n\nMerge sdcard/switch into the SD root. Keep settings.ini. Launch from hbmenu. Auto-connect and Update on launch are off by default. Check for updates is independent; tap Install update to confirm, disconnect first, and reopen the app after installation. This is app-launch auto-connect, not Switch boot-time homebrew launch.\n\n中文：合并 sdcard 中的 switch 文件夹到 SD 根目录，覆盖 NRO 并保留 settings.ini。自动连接和自动更新默认关闭；检查更新需再次确认安装，先断开连接，完成后重新打开。本功能不是 Switch 开机自动运行自制程序。\n')
original=ci/'Lite-Link-macOS-experimental/Lite-Link-macOS-experimental.zip'
with ZipFile(original) as src, ZipFile(out/f'Lite-Link-{tag}-macOS-experimental.zip','w') as dst:
    for entry in src.infolist():dst.writestr(copy.copy(entry),src.read(entry.filename))
    for name,file in common.items():dst.write(file,name)
    dst.writestr('SOURCE-URL.txt',source)
    dst.writestr('EXPERIMENTAL.txt','Native CoreHID prototype, not a ready-to-use gamepad on ordinary Macs. An Apple-approved virtual HID entitlement and matching signing profile are required. This diagnostic app omits that entitlement and refuses a misleading controller session. No keyboard/mouse fallback. Windows/Switch startup and auto-update controls are not included in this experimental macOS receiver.\n\n中文：macOS 原生 HID 实验版，需要 Apple 专用授权。普通诊断构建不能直接输出手柄。暂不包含 Windows/Switch 的启动和更新选项。\n')
package(out/f'Lite-Link-{tag}-Icons.zip',{
    'LICENSE':root/'LICENSE',**{'icons/'+p.name:p for p in (root/'assets/icons').iterdir() if p.is_file()}
},'Lite Link — GitHub @Yukinsnow\nDefault: C Sunrise Pad. A, B and D are optional original designs. SVG, PNG, JPEG and ICO included.\n')
assets=sorted([*out.glob(f'Lite-Link-{tag}-*.zip'),raw])
(out/'SHA256SUMS.txt').write_text(''.join(hashlib.sha256(p.read_bytes()).hexdigest()+'  '+p.name+'\n' for p in assets),encoding='utf-8')
print('\n'.join(str(p) for p in assets))
