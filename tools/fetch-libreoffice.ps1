# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Condec contributors

<#
.SYNOPSIS
    Puts the LibreOffice copy that ships inside Condec in third_party\libreoffice.

.DESCRIPTION
    Downloads the official LibreOffice MSI, checks its SHA-256, extracts it with an administrative
    install (msiexec /a: files only, nothing is installed or registered on this machine), then removes
    the parts Condec's headless conversions don't use. The files themselves are not modified.

    This is a build step. Condec itself never goes online: the copy is packaged into the MSIX.
    third_party\ is ignored by git.
#>
[CmdletBinding()]
param(
    [switch]$KeepDownload
)

$ErrorActionPreference = 'Stop'

# Pinned release. The hash is the one the winget manifest TheDocumentFoundation.LibreOffice 26.8.0.3 lists.
$Version = '26.8.0'
$Url = "https://download.documentfoundation.org/libreoffice/stable/$Version/win/x86_64/LibreOffice_${Version}_Win_x86-64.msi"
$Sha256 = '4aa6c6e1895f4055104effcb556bd3362d20c6ad707c149543304f395ef9db95'

$root = Split-Path -Parent $PSScriptRoot
$target = Join-Path $root 'third_party\libreoffice'
$work = Join-Path ([IO.Path]::GetTempPath()) "condec-libreoffice-$Version"
$msi = Join-Path $work "LibreOffice_${Version}_Win_x86-64.msi"
$extract = Join-Path $work 'extract'

New-Item -ItemType Directory -Force $work | Out-Null

if (-not (Test-Path $msi) -or (Get-FileHash $msi -Algorithm SHA256).Hash -ne $Sha256) {
    Write-Host "Downloading $Url"
    # --silent: in Windows PowerShell, curl's progress meter on stderr would count as a script error.
    curl.exe --fail --location --silent --show-error --output $msi $Url
    if ($LASTEXITCODE -ne 0) { throw "Download failed (curl exit code $LASTEXITCODE)." }
}

$actual = (Get-FileHash $msi -Algorithm SHA256).Hash
if ($actual -ne $Sha256) {
    throw "SHA-256 mismatch: expected $Sha256, got $actual. The file was not extracted."
}

Write-Host 'Extracting (administrative install, nothing is installed)'
if (Test-Path $extract) { Remove-Item $extract -Recurse -Force }
$process = Start-Process msiexec.exe -ArgumentList '/a', "`"$msi`"", '/qn', "TARGETDIR=`"$extract`"" -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "msiexec /a failed with exit code $($process.ExitCode)." }

if (-not (Test-Path (Join-Path $extract 'program\soffice.exe'))) { throw 'The extracted MSI has no program\soffice.exe.' }

# The administrative install leaves a copy of the MSI next to the files.
Get-ChildItem $extract -Filter *.msi | Remove-Item -Force

# A normal install puts these outside the LibreOffice folder. Condec installs nothing into Windows, so they
# move inside it, where LibreOffice finds them on its own:
# - Fonts (Carlito, Liberation and others that stand in for Microsoft fonts): LibreOffice registers every font in
#   share\fonts\truetype for its own process only (AddFontResourceExW with FR_PRIVATE, vcl/win/gdi/salfont.cxx).
# - The Visual C++ runtime that the installer puts in System32: Windows loads DLLs from the executable's folder first.
$fonts = Join-Path $extract 'share\fonts\truetype'
New-Item -ItemType Directory -Force $fonts | Out-Null
Move-Item (Join-Path $extract 'Fonts\*') $fonts -Force
Move-Item (Join-Path $extract 'System64\*.dll') (Join-Path $extract 'program') -Force
Remove-Item (Join-Path $extract 'Fonts'), (Join-Path $extract 'System'), (Join-Path $extract 'System64') -Recurse -Force

& (Join-Path $PSScriptRoot 'trim-libreoffice.ps1') -Path $extract

if (Test-Path $target) { Remove-Item $target -Recurse -Force }
New-Item -ItemType Directory -Force (Split-Path -Parent $target) | Out-Null
Move-Item $extract $target

if (-not $KeepDownload) { Remove-Item $msi -Force }

$size = (Get-ChildItem $target -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("LibreOffice {0} is in {1} ({2:N0} MB)" -f $Version, $target, $size)
