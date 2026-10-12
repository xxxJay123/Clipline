<#
.SYNOPSIS
  Builds a portable Clipline: one Clipline.exe, zipped, with its SHA-256.
.DESCRIPTION
  By default .NET travels inside the exe, so it runs anywhere. With -Small the
  exe is about a megabyte and uses the .NET 10 Desktop Runtime installed on
  the machine instead.
.EXAMPLE
  ./scripts/publish.ps1 -Runtime win-x64
  ./scripts/publish.ps1 -Runtime win-x64 -Small
#>
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',
    [switch]$Small,
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$name = if ($Small) { "Clipline-$Runtime-small" } else { "Clipline-$Runtime" }
$out = Join-Path $root "artifacts/publish/$name"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

$options = if ($Small) {
    @('--self-contained', 'false')
} else {
    @('--self-contained', 'true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=true')
}
dotnet publish (Join-Path $root 'src/Clipline/Clipline.csproj') -c $Configuration -r $Runtime `
    -p:PublishSingleFile=true -p:DebugType=none @options -o $out
if ($LASTEXITCODE) { exit $LASTEXITCODE }

Copy-Item (Join-Path $root 'LICENSE') $out
$zip = Join-Path $root "artifacts/$name.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$out/*" -DestinationPath $zip
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $name.zip" | Set-Content "$zip.sha256" -Encoding ascii
Write-Host "$zip"
Write-Host "sha256 $hash"
