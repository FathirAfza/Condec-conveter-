<#
.SYNOPSIS
    Packs Condec into an MSIX signed with a self-signed test certificate.

.DESCRIPTION
    Builds the app, copies every file the .appxrecipe lists into a fresh layout, packs it with
    makeappx and signs it with signtool, both from the Windows SDK BuildTools package the project
    already restores.

    The certificate (subject CN=Condec, matching the manifest publisher) is created once in the
    current user's personal store, following Microsoft Learn "Create a certificate for package
    signing". Its private key stays there. Only the public part is exported, next to the package.

    Windows only installs the package once that certificate is trusted. From an administrator
    PowerShell:
        Import-Certificate -FilePath dist\Condec-uji.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
        Add-AppxPackage -Path dist\<file>.msix

    An unsigned package (-AllowUnsigned) is not an option: Windows rejects unsigned packages with
    executable activations (0x80073D2B).
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')] [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\Condec'
$certificateName = 'Condec test signing'

dotnet build (Join-Path $root 'Condec.sln') -c $Configuration -p:Platform=x64 -v q -nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

$recipePath = Get-ChildItem (Join-Path $project "bin\x64\$Configuration") -Recurse -Filter 'Condec.build.appxrecipe' | Select-Object -First 1
if (-not $recipePath) { throw 'No .appxrecipe found after the build' }

# The layout goes under obj so it never mixes with the dev layout that Add-AppxPackage -Register uses.
$layout = Join-Path $project "obj\x64\$Configuration\PackLayout"
if (Test-Path -LiteralPath $layout) { Remove-Item -LiteralPath $layout -Recurse -Force }

[xml]$recipe = Get-Content -LiteralPath $recipePath.FullName -Raw
$ns = New-Object System.Xml.XmlNamespaceManager($recipe.NameTable)
$ns.AddNamespace('m', 'http://schemas.microsoft.com/developer/msbuild/2003')
foreach ($item in $recipe.SelectNodes('//m:AppXManifest | //m:AppxPackagedFile', $ns)) {
    $destination = Join-Path $layout $item.SelectSingleNode('m:PackagePath', $ns).InnerText
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Copy-Item -LiteralPath $item.GetAttribute('Include') -Destination $destination
}

[xml]$manifest = Get-Content -LiteralPath (Join-Path $layout 'AppxManifest.xml') -Raw
$publisher = $manifest.Package.Identity.Publisher
$version = $manifest.Package.Identity.Version

# makeappx and signtool from the BuildTools version NuGet resolved for the app project.
$assets = Get-Content -LiteralPath (Join-Path $project 'obj\project.assets.json') -Raw | ConvertFrom-Json
$buildTools = $assets.libraries.PSObject.Properties.Name | Where-Object { $_ -like 'Microsoft.Windows.SDK.BuildTools/*' } | Select-Object -First 1
if (-not $buildTools) { throw 'Microsoft.Windows.SDK.BuildTools is not in project.assets.json' }
$packageDir = Join-Path $assets.project.restore.packagesPath $buildTools.ToLowerInvariant()
function Find-Tool([string]$name) {
    $tool = Get-ChildItem $packageDir -Recurse -Filter $name | Where-Object { $_.Directory.Name -eq 'x64' } | Select-Object -First 1
    if (-not $tool) { throw "$name not found in $packageDir" }
    $tool.FullName
}

$certificate = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq $publisher -and $_.FriendlyName -eq $certificateName -and $_.NotAfter -gt (Get-Date) -and $_.HasPrivateKey } |
    Select-Object -First 1
if (-not $certificate) {
    $certificate = New-SelfSignedCertificate -Type Custom -KeyUsage DigitalSignature -CertStoreLocation 'Cert:\CurrentUser\My' `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}') -Subject $publisher -FriendlyName $certificateName
}

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$output = Join-Path $dist "Condec_${version}_x64_uji.msix"
Export-Certificate -Cert $certificate -FilePath (Join-Path $dist 'Condec-uji.cer') | Out-Null

& (Find-Tool 'makeappx.exe') pack /d $layout /p $output /o | Out-Null
if ($LASTEXITCODE -ne 0) { throw "makeappx failed with exit code $LASTEXITCODE" }

# makeappx hashes with SHA256 by default, and signtool must use the same algorithm.
& (Find-Tool 'signtool.exe') sign /fd SHA256 /sha1 $certificate.Thumbprint /s My $output | Out-Null
if ($LASTEXITCODE -ne 0) { throw "signtool failed with exit code $LASTEXITCODE" }

Get-Item -LiteralPath $output | Select-Object FullName, @{ Name = 'SizeMB'; Expression = { [math]::Round($_.Length / 1MB, 1) } }
