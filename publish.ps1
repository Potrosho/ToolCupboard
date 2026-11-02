# Builds a download for each edition into dist\. Neither needs .NET installed.
$ErrorActionPreference = 'Stop'
$dist = Join-Path $PSScriptRoot 'dist'
Remove-Item -Recurse -Force $dist -ErrorAction SilentlyContinue

foreach ($edition in @(
    @{ Project = 'ToolCupboard\ToolCupboard.csproj'; Name = 'ToolCupboard-Desktop-win-x64' },
    @{ Project = 'ToolCupboard.Web\ToolCupboard.Web.csproj'; Name = 'ToolCupboard-Web-win-x64' })) {
    $out = Join-Path $dist $edition.Name
    dotnet publish (Join-Path $PSScriptRoot $edition.Project) -c Release -r win-x64 --self-contained -o $out -v q --nologo
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $($edition.Name)" }
    Compress-Archive -Path "$out\*" -DestinationPath "$out.zip"
    Remove-Item -Recurse -Force $out
}
