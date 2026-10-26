# Attribution

Lite Link v0.3.0, maintained by GitHub @Yukinsnow.

This is a modified work derived from Vins98/NXInput (baseline commit `38791c3a227a2947fd2be481091f5a999a7941f1`), itself based on WerWolv/SwitchXBOXController. The original core Switch sender and Windows receiver are preserved and modified under the accompanying GPL-3.0 license.

Local modifications dated 2026-10-26 include a graphical Switch UI, independent face-button swaps, screen/backlight controls, network discovery and multi-adapter selection, input validation and release handling, reconnect support, Chinese/English locale detection, original artwork and branding, tests, and an experimental native CoreHID receiver for macOS.

Version 0.3 adds opt-in login/app startup behavior, remembered connection preferences, and verified GitHub Release update flows. The Switch updater also uses the separately licensed jsmn parser, libcurl and zlib. Library licenses are included in the source/vendor and docs directories; libcurl/zlib are supplied by the devkitPro Switch toolchain and are not vendored as binaries in this repository.

`Switch_Client/source/vendor/stb_truetype.h` is from the official nothings/stb project. Its separate license is included next to the source. Platform SDKs and system fonts are not distributed.
