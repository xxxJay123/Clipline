<#
.SYNOPSIS
  Builds everything and runs the tests.
#>
param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
dotnet build (Join-Path $root 'Clipline.slnx') -c $Configuration
if ($LASTEXITCODE) { exit $LASTEXITCODE }
dotnet test (Join-Path $root 'tests/Clipline.Tests') -c $Configuration --no-build
if ($LASTEXITCODE) { exit $LASTEXITCODE }
