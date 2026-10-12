<#
.SYNOPSIS
  Draws the README pictures of the line, in light and dark, without touching the screen.
#>
param([string]$Out = 'docs/images', [string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
dotnet build (Join-Path $root 'src/Clipline/Clipline.csproj') -c $Configuration
if ($LASTEXITCODE) { exit $LASTEXITCODE }
$exe = Join-Path $root "src/Clipline/bin/$Configuration/net10.0-windows/Clipline.exe"
$target = Join-Path $root $Out
$p = Start-Process $exe -ArgumentList '--snapshot', "`"$target`"" -Wait -PassThru
exit $p.ExitCode
