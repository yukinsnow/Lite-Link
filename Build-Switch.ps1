param(
    [Parameter(Mandatory=$true)][string]$DevkitPro,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'build-switch')
)
$ErrorActionPreference = 'Stop'
$dkpRoot = (Resolve-Path -LiteralPath $DevkitPro).Path.Replace('\','/')
$env:DEVKITPRO = $dkpRoot
$gcc = Join-Path $dkpRoot 'devkitA64/bin/aarch64-none-elf-gcc.exe'
$nacptool = Join-Path $dkpRoot 'tools/bin/nacptool.exe'
$elf2nro = Join-Path $dkpRoot 'tools/bin/elf2nro.exe'
foreach ($tool in @($gcc,$nacptool,$elf2nro)) {
    if (!(Test-Path -LiteralPath $tool)) { throw "Missing tool: $tool" }
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$outRoot = (Resolve-Path -LiteralPath $OutputDirectory).Path
$sourceDirectory = Join-Path $PSScriptRoot 'Switch_Client/source'
$icon = Join-Path $PSScriptRoot 'Switch_Client/icon.jpg'
$objectFile = Join-Path $outRoot 'main.o'
$elf = Join-Path $outRoot 'SwitchLiteController.elf'
$nacp = Join-Path $outRoot 'SwitchLiteController.nacp'
$nro = Join-Path $outRoot 'SwitchLiteController.nro'
$objectFiles = @()
foreach ($source in Get-ChildItem -LiteralPath $sourceDirectory -Filter '*.c') {
    $objectFile = Join-Path $outRoot ($source.BaseName + '.o')
    & $gcc -g -Wall -O2 -ffunction-sections -march=armv8-a -mtune=cortex-a57 -mtp=soft -fPIE -D__SWITCH__ "-I$dkpRoot/libnx/include" "-I$dkpRoot/portlibs/switch/include" -c $source.FullName -o $objectFile
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $($source.Name)" }
    $objectFiles += $objectFile
}
& $gcc "-specs=$dkpRoot/libnx/switch.specs" -g -march=armv8-a -mtune=cortex-a57 -mtp=soft -fPIE @objectFiles "-L$dkpRoot/libnx/lib" "-L$dkpRoot/portlibs/switch/lib" -lcurl -lz -lnx -lm -o $elf
if ($LASTEXITCODE -ne 0) { throw 'Linking failed' }
& $nacptool --create 'Lite Link' 'Yukinsnow; Vins98; WerWolv' '0.3.1' $nacp
if ($LASTEXITCODE -ne 0) { throw 'NACP creation failed' }
& $elf2nro $elf $nro "--icon=$icon" "--nacp=$nacp"
if ($LASTEXITCODE -ne 0) { throw 'NRO creation failed' }
Write-Output "Built: $nro"

