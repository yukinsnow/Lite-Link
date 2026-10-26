#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
swift test
swift build -c release --arch arm64
swift build -c release --arch x86_64
app="$PWD/dist/Lite Link.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
lipo -create .build/arm64-apple-macosx/release/LiteLink .build/x86_64-apple-macosx/release/LiteLink -output "$app/Contents/MacOS/LiteLink"
cp Info.plist "$app/Contents/Info.plist"
iconset="$PWD/.build/AppIcon.iconset"
mkdir -p "$iconset"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" ../assets/icons/C-sunrise-pad.png --out "$iconset/icon_${size}x${size}.png" >/dev/null
  double=$((size * 2))
  sips -z "$double" "$double" ../assets/icons/C-sunrise-pad.png --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$app/Contents/Resources/AppIcon.icns"
if [[ -n "${SIGNING_IDENTITY:-}" && -n "${PROVISIONING_PROFILE:-}" ]]; then
  cp "$PROVISIONING_PROFILE" "$app/Contents/embedded.provisionprofile"
  codesign --force --options runtime --entitlements VirtualHID.entitlements --sign "$SIGNING_IDENTITY" "$app"
else
  # Diagnostic build only: never attach the restricted entitlement to ad-hoc signatures.
  codesign --force --sign - "$app"
fi
codesign --verify --deep --strict "$app"
ditto -c -k --keepParent "$app" dist/Lite-Link-macOS-experimental.zip
echo "Built: $app"
