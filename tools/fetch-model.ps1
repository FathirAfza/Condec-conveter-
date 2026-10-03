# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Condec contributors

<#
.SYNOPSIS
    Puts the two upscale models that ship inside Condec in third_party\models.

.DESCRIPTION
    Sharp (realesrgan-x4plus): downloads the ONNX export of Real-ESRGAN x4plus (BSD-3-Clause, see THIRD-PARTY-NOTICES.md)
    and checks its SHA-256. The file is not modified. tools\verify-upscale-model.py shows how it was checked against the
    official weights: every one of the 702 tensors is bit-identical to RealESRGAN_x4plus.pth from the xinntao/Real-ESRGAN
    v0.1.0 release.

    Faithful (realesrnet-x4plus): downloads RealESRNet_x4plus.pth from the xinntao/Real-ESRGAN v0.1.1 release, checks its
    SHA-256, and runs tools\make-faithful-model.py, which writes those weights into a copy of the Sharp file (the network is
    the same). The result is the same file on every machine, so its SHA-256 is checked too. This step needs Python with
    numpy and onnx (pip install numpy onnx).

    This is a build step. Condec itself never goes online: the models are packaged with the app (and found at
    Models\<name>\model.onnx next to Condec.exe). third_party\ is ignored by git.
#>
[CmdletBinding()]
param(
    # The Python that runs tools\make-faithful-model.py.
    [string]$Python = 'python'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$models = Join-Path $root 'third_party\models'

function Test-Hash([string]$Path, [string]$Sha256) {
    (Test-Path $Path) -and (Get-FileHash $Path -Algorithm SHA256).Hash -eq $Sha256
}

# Downloads $Url to $Target unless a file with $Sha256 is already there, and keeps it only when the hash matches.
function Get-PinnedFile([string]$Url, [string]$Target, [string]$Sha256) {
    if (Test-Hash $Target $Sha256) {
        return
    }

    New-Item -ItemType Directory -Force (Split-Path -Parent $Target) | Out-Null
    $partial = "$Target.download"
    Write-Host "Downloading $Url"
    # --silent: in Windows PowerShell, curl's progress meter on stderr would count as a script error.
    curl.exe --fail --location --silent --show-error --output $partial $Url
    if ($LASTEXITCODE -ne 0) { Remove-Item $partial -Force -ErrorAction SilentlyContinue; throw "Download failed (curl exit code $LASTEXITCODE)." }

    $actual = (Get-FileHash $partial -Algorithm SHA256).Hash
    if ($actual -ne $Sha256) {
        Remove-Item $partial -Force
        throw "SHA-256 mismatch for ${Url}: expected $Sha256, got $actual. The file was not kept."
    }

    Move-Item $partial $Target -Force
}

# Sharp. The export is published by SkillSafe (Hugging Face), not by the Real-ESRGAN authors; the hash pins this exact file.
$sharp = Join-Path $models 'realesrgan-x4plus\model.onnx'
Get-PinnedFile 'https://huggingface.co/skillsafe-ai/realesrgan-x4plus/resolve/main/model.onnx' $sharp '4851ec156207d271f5328605d0582eeb851e656227da8aca093ced9e60789291'
Write-Host ("Sharp model: {0} ({1:N0} MB)" -f $sharp, ((Get-Item $sharp).Length / 1MB))

# Faithful.
$faithful = Join-Path $models 'realesrnet-x4plus\model.onnx'
$faithfulSha256 = 'DC6B06112170ACC10C2E7ED740BFFC6B28227A9B1288DECB142BBAC678BE4D40'
if (-not (Test-Hash $faithful $faithfulSha256)) {
    # Kept outside third_party\models, so only model files are there to package.
    $checkpoint = Join-Path $root 'third_party\checkpoints\RealESRNet_x4plus.pth'
    Get-PinnedFile 'https://github.com/xinntao/Real-ESRGAN/releases/download/v0.1.1/RealESRNet_x4plus.pth' $checkpoint 'A820B9BDE89A874D7599D545567308CE6C128FC8754A53208EDA016D40AA81DF'

    $partial = "$faithful.making"
    & $Python (Join-Path $PSScriptRoot 'make-faithful-model.py') $sharp $checkpoint $partial
    if ($LASTEXITCODE -ne 0) {
        Remove-Item $partial -Force -ErrorAction SilentlyContinue
        throw "tools\make-faithful-model.py failed (exit code $LASTEXITCODE). It needs Python with numpy and onnx: pip install numpy onnx"
    }

    $actual = (Get-FileHash $partial -Algorithm SHA256).Hash
    if ($actual -ne $faithfulSha256) {
        Remove-Item $partial -Force
        throw "SHA-256 mismatch for the Faithful model: expected $faithfulSha256, got $actual. The file was not kept."
    }

    Move-Item $partial $faithful -Force
}
Write-Host ("Faithful model: {0} ({1:N0} MB)" -f $faithful, ((Get-Item $faithful).Length / 1MB))
