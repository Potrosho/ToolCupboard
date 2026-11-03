# Builds the ToolCupboard Harmony mod against a Rust server's DLLs and puts it where ToolCupboard.Core bundles it.
# Run it after changing ToolCupboard.Mod, then commit ToolCupboard.Core\Mod\ToolCupboard.Mod.dll.
param([string]$RustManaged = 'D:\RustGSM-Test\server\RustDedicated_Data\Managed')
$ErrorActionPreference = 'Stop'
if (-not (Test-Path "$RustManaged\Assembly-CSharp.dll")) { throw "No Rust server DLLs in $RustManaged. Pass -RustManaged <server>\RustDedicated_Data\Managed" }

$out = Join-Path $env:TEMP 'toolcupboard-mod-build'
Remove-Item -Recurse -Force $out -ErrorAction SilentlyContinue
dotnet build (Join-Path $PSScriptRoot 'ToolCupboard.Mod\ToolCupboard.Mod.csproj') -c Release -o $out -v q --nologo "-p:RustManaged=$RustManaged" -p:NoWarn=MSB3277
if ($LASTEXITCODE -ne 0) { throw 'The mod did not build' }

$target = Join-Path $PSScriptRoot 'ToolCupboard.Core\Mod'
New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item (Join-Path $out 'ToolCupboard.Mod.dll') $target -Force
Remove-Item -Recurse -Force $out
"Built $target\ToolCupboard.Mod.dll"
