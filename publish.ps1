# Builds one standalone exe for each edition into dist\. Neither needs .NET installed.
$ErrorActionPreference = 'Stop'
$dist = Join-Path $PSScriptRoot 'dist'
Remove-Item -Recurse -Force $dist -ErrorAction SilentlyContinue

foreach ($edition in @(
    @{ Project = 'ToolCupboard\ToolCupboard.csproj'; Exe = 'ToolCupboard.exe' },
    @{ Project = 'ToolCupboard.Web\ToolCupboard.Web.csproj'; Exe = 'ToolCupboardWeb.exe' })) {
    $out = Join-Path $dist 'build'
    dotnet publish (Join-Path $PSScriptRoot $edition.Project) -c Release -r win-x64 --self-contained -o $out -v q --nologo `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $($edition.Exe)" }
    Move-Item (Join-Path $out $edition.Exe) $dist
    $extra = @(Get-ChildItem $out -Force)
    if ($extra) { throw "$($edition.Exe) needs files beside it: $($extra.Name -join ', ')" }
    Remove-Item -Recurse -Force $out
}
