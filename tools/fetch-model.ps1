# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Condec contributors

<#
.SYNOPSIS
    Puts the upscale model that ships inside Condec in third_party\models\realesrgan-x4plus.

.DESCRIPTION
    Downloads the ONNX export of Real-ESRGAN x4plus (BSD-3-Clause, see THIRD-PARTY-NOTICES.md) and checks its SHA-256.
    The file is not modified. tools\verify-upscale-model.py shows how it was checked against the official weights:
    every one of the 702 tensors is bit-identical to RealESRGAN_x4plus.pth from the xinntao/Real-ESRGAN v0.1.0 release.

    This is a build step. Condec itself never goes online: the model is packaged with the app (and found at
    Models\realesrgan-x4plus\model.onnx next to Condec.exe). third_party\ is ignored by git.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

# The export is published by SkillSafe (Hugging Face), not by the Real-ESRGAN authors; the hash pins this exact file.
$Url = 'https://huggingface.co/skillsafe-ai/realesrgan-x4plus/resolve/main/model.onnx'
$Sha256 = '4851ec156207d271f5328605d0582eeb851e656227da8aca093ced9e60789291'

$root = Split-Path -Parent $PSScriptRoot
$directory = Join-Path $root 'third_party\models\realesrgan-x4plus'
$target = Join-Path $directory 'model.onnx'

if ((Test-Path $target) -and (Get-FileHash $target -Algorithm SHA256).Hash -eq $Sha256) {
    Write-Host "The model is already in $target"
    return
}

New-Item -ItemType Directory -Force $directory | Out-Null
$partial = "$target.download"

Write-Host "Downloading $Url"
# --silent: in Windows PowerShell, curl's progress meter on stderr would count as a script error.
curl.exe --fail --location --silent --show-error --output $partial $Url
if ($LASTEXITCODE -ne 0) { Remove-Item $partial -Force -ErrorAction SilentlyContinue; throw "Download failed (curl exit code $LASTEXITCODE)." }

$actual = (Get-FileHash $partial -Algorithm SHA256).Hash
if ($actual -ne $Sha256) {
    Remove-Item $partial -Force
    throw "SHA-256 mismatch: expected $Sha256, got $actual. The file was not kept."
}

Move-Item $partial $target -Force
$size = (Get-Item $target).Length / 1MB
Write-Host ("The upscale model is in {0} ({1:N0} MB)" -f $target, $size)
