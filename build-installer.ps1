<#
  Builds artifacts\PictureViewer-Setup-<version>.exe:
    1. publishes a self-contained win-x64 build (no .NET install needed on the target PC)
    2. packs it with Inno Setup (winget install JRSoftware.InnoSetup --scope user)
#>
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\PictureViewer\PictureViewer.csproj'
$publish = Join-Path $root 'artifacts\publish'

$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { $version = '1.0.0' }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup --scope user' }

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -p:DebugType=none -o $publish
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

& $iscc "/DAppVersion=$version" "/DSourceDir=$publish" (Join-Path $root 'installer\PictureViewer.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed' }

$setup = Join-Path $root "artifacts\PictureViewer-Setup-$version.exe"
'{0} ({1:N1} MB)' -f $setup, ((Get-Item $setup).Length / 1MB)
