<#
.SYNOPSIS
  Builds Clipline-Setup-<version>-<arch>.exe with Inno Setup.
.DESCRIPTION
  Publishes a self-contained Clipline into a folder (no single-file
  extraction, so it starts quickly once installed) and compiles
  installer/Clipline.iss around it. Needs Inno Setup 6 (ISCC.exe).
.EXAMPLE
  ./scripts/installer.ps1 -Runtime win-x64
#>
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',
    [string]$Version,
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $Version) {
    $Version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version
}
$arch = $Runtime.Substring(4)

$iscc = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 was not found. Install it with: winget install JRSoftware.InnoSetup' }

$publish = Join-Path $root "artifacts/publish/installer-$Runtime"
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish (Join-Path $root 'src/Clipline/Clipline.csproj') -c $Configuration -r $Runtime `
    --self-contained true -p:PublishSingleFile=false -p:DebugType=none -p:Version=$Version -o $publish
if ($LASTEXITCODE) { exit $LASTEXITCODE }

$out = Join-Path $root 'artifacts'
& $iscc /Qp "/DAppVersion=$Version" "/DArch=$arch" "/DPublishDir=$publish" "/DOutputDir=$out" (Join-Path $root 'installer/Clipline.iss')
if ($LASTEXITCODE) { exit $LASTEXITCODE }

$setup = Join-Path $out "Clipline-Setup-$Version-$arch.exe"
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  Clipline-Setup-$Version-$arch.exe" | Set-Content "$setup.sha256" -Encoding ascii
Write-Host $setup
Write-Host "sha256 $hash"
