# Native macOS gamepad backend

The receiver uses Apple's public [CoreHID HIDVirtualDevice](https://developer.apple.com/documentation/corehid/hidvirtualdevice) API on macOS 15+. It creates an actual generic HID gamepad with 16 buttons, 4 stick axes, 2 triggers and a hat switch. Experimental VID/PID `FFFF:4C4C` identifies this prototype; it is not an assigned commercial USB identity or an Xbox impersonation.

## Signing is a real external requirement

Apple gates virtual HID creation behind `com.apple.developer.hid.virtual.device`. Distribution requires Apple approval, a matching provisioning profile and signing identity. Simply adding the entitlement to an ad-hoc binary does not grant it and can cause launch failure. The project does **not** disable SIP, weaken AMFI, install unsigned kernel extensions, inject games, or silently switch to keyboard/mouse output.

The default build intentionally omits the restricted entitlement. It is useful for testing the UI and protocol encoder, but it will show an explicit creation error on ordinary Macs and will not advertise a working controller session.

## Build a diagnostic app

```sh
bash macOS/build-app.sh
```

This runs the Swift tests, builds arm64 and x86_64 slices, creates a universal `.app`, and applies an ad-hoc signature **without** the entitlement. The archive name includes `experimental`.

## Authorized signing

Only after obtaining an approved profile:

```sh
SIGNING_IDENTITY='Developer ID Application: Your Name (TEAMID)' \
PROVISIONING_PROFILE='/path/to/approved.provisionprofile' \
bash macOS/build-app.sh
```

The script embeds the provided profile and uses `VirtualHID.entitlements`. Keep profiles, certificates and private keys out of the repository. Notarization, if needed for distribution, is an additional step and does not grant the virtual HID entitlement.

## Compatibility and validation

- Real HID output is intended for games that accept generic HID/SDL controllers. Apple GameController-only compatibility is not guaranteed.
- Successful controller creation is required **before** opening input/discovery sockets.
- Two sockets preserve selected-adapter input binding while receiving subnet-filtered broadcast discovery.
- Invalid packets and other endpoints are ignored; valid input times out to neutral after 500 ms.
- No rumble output or gyro transport is implemented.
- Physical Mac testing with an authorized signing profile is still required. A CI build proves compilation and report-encoding tests, not permission to create a controller on a user's Mac.

## 中文说明

这是真正的通用 HID 手柄后端，不是键鼠映射。普通诊断构建没有 Apple 批准的专用权限，因此无法在普通 Mac 上直接创建手柄；程序会报错并停止握手。必须取得受批准的权限、匹配的描述文件和签名身份，才能验证完整功能。不会要求关闭 SIP/AMFI。游戏仍需支持通用 HID 手柄，尚不保证所有 GameController 游戏兼容。
