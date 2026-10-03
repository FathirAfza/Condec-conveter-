# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Condec contributors

<#
.SYNOPSIS
    Publishes Condec as a portable folder (no MSIX, no installation) and zips it.

.DESCRIPTION
    Runs `dotnet publish` with WindowsPackageType=None and WindowsAppSDKSelfContained=true, so the
    output folder holds Condec.exe together with the .NET runtime, the Windows App SDK runtime, and the
    LibreOffice copy from tools\fetch-libreoffice.ps1, and the upscale model from tools\fetch-model.ps1. Unzip anywhere and start Condec.exe; nothing
    is installed or registered, and the app keeps its data in %LOCALAPPDATA%\Condec as usual.

    The same code as the MSIX, minus package identity: no Start menu tile, no file associations.
    The executable is not signed, so SmartScreen asks once before the first start.
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')] [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\Condec\Condec.csproj'
$version = ([xml](Get-Content (Join-Path $root 'src\Condec\Package.appxmanifest') -Raw)).Package.Identity.Version
$publishDir = Join-Path $root "src\Condec\obj\x64\$Configuration\Portable\Condec"
$dist = Join-Path $root 'dist'
$output = Join-Path $dist "Condec_${version}_x64_portable.zip"

if (-not (Test-Path (Join-Path $root 'third_party\libreoffice\program\soffice.exe'))) {
    throw 'third_party\libreoffice is missing; run tools\fetch-libreoffice.ps1 first.'
}

if (-not (Test-Path (Join-Path $root 'third_party\models\realesrgan-x4plus\model.onnx')) -or -not (Test-Path (Join-Path $root 'third_party\models\realesrnet-x4plus\model.onnx'))) {
    throw 'third_party\models is missing; run tools\fetch-model.ps1 first.'
}

if (Test-Path -LiteralPath $publishDir) { Remove-Item -LiteralPath $publishDir -Recurse -Force }

# No trailing backslash on PublishDir: Windows PowerShell 5.1 reads \" as an escaped quote when the path has a space,
# and the rest of the command line (-v q -nologo) ends up inside the path. MSBuild adds the trailing slash itself.
# ReadyToRun stays off: it roughly doubles the publish time for a start-up gain nobody notices here.
dotnet publish $project -c $Configuration -r win-x64 -p:Platform=x64 --self-contained `
    -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:PublishReadyToRun=false `
    "-p:PublishDir=$publishDir" -v q -nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }

foreach ($required in 'Condec.exe', 'Microsoft.WindowsAppRuntime.dll', 'Microsoft.ui.xaml.dll', 'LibreOffice\program\soffice.exe', 'Models\realesrgan-x4plus\model.onnx', 'Models\realesrnet-x4plus\model.onnx', 'onnxruntime.dll', 'Condec.pri') {
    if (-not (Test-Path (Join-Path $publishDir $required))) { throw "The portable build lacks $required" }
}

New-Item -ItemType Directory -Force -Path $dist | Out-Null
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Force }
# Compress-Archive is limited to 2 GB and slow; the .NET zip API has neither problem.
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($publishDir, $output, [IO.Compression.CompressionLevel]::Optimal, $true)

Get-Item -LiteralPath $output | Select-Object FullName, @{ Name = 'SizeMB'; Expression = { [math]::Round($_.Length / 1MB, 1) } }
