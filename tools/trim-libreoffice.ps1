<#
.SYNOPSIS
    Removes the parts of a LibreOffice folder that Condec's headless conversions don't use.

.DESCRIPTION
    Only whole folders and files are removed; nothing is edited. Kept on purpose:
    - Math (smlo.dll): formulas inside Word and Writer documents are drawn by it.
    - Base libraries: Writer's mail-merge fields load them.
    - xpdfimport: PDF import, used for PDF to DOCX and PPTX.
    - English and Indonesian dictionaries: their hyphenation patterns change line breaks, so dropping
      them would change the layout of documents that use automatic hyphenation.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Path
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path (Join-Path $Path 'program\soffice.exe'))) {
    throw "$Path is not a LibreOffice folder (no program\soffice.exe)."
}

$remove = @(
    # Offline help and the extensions that only matter in the user interface.
    'help'
    'share\extensions\nlpsolver'
    'share\extensions\wiki-publisher'
    # Templates, clip art, wizards and AutoText, used only when writing documents by hand. The AutoText
    # folders are named after languages (en-US, id), which the MSIX resource indexer would take for
    # Condec's own language resources (warning PRI263).
    'share\autotext'
    'share\gallery'
    'share\template'
    'share\wizards'
    'program\wizards'
    # Java (the HSQLDB database engine) and Firebird: database files are not a Condec source format.
    'program\classes'
    'share\firebird'
    # Python scripting and macros.
    'program\python-core-*'
    'program\python.exe'
    'program\python3.dll'
    'program\python313.dll'
    'program\pyuno.pyd'
    'program\pythonloader*'
    'program\*.py'
    'program\*.pyi'
    'share\Scripts\python'
)

# Icon themes: headless mode draws no user interface. Colibre is the default theme on Windows and stays.
$remove += Get-ChildItem (Join-Path $Path 'share\config') -Filter 'images_*.zip' |
    Where-Object Name -ne 'images_colibre.zip' |
    ForEach-Object { "share\config\$($_.Name)" }

# Languages: the administrative install carries every translation and dictionary. English (built in, en-US)
# and Indonesian stay; the CJK and complex-text settings (cjk_*.xcd, ctl_*.xcd) are not translations and stay too.
$keep = @('en-US', 'id')
$remove += Get-ChildItem (Join-Path $Path 'program\resource') -Directory |
    Where-Object { $_.Name -ne 'common' -and ($_.Name -replace '_', '-') -notin $keep } |
    ForEach-Object { "program\resource\$($_.Name)" }
$remove += Get-ChildItem (Join-Path $Path 'share\registry') -Filter 'Langpack-*.xcd' |
    Where-Object { $_.BaseName.Substring('Langpack-'.Length) -notin $keep } |
    ForEach-Object { "share\registry\$($_.Name)" }
$remove += Get-ChildItem (Join-Path $Path 'share\registry\res') -Filter '*.xcd' |
    Where-Object { ($_.BaseName -replace '^.*?_(langpack_)?', '') -notin $keep } |
    ForEach-Object { "share\registry\res\$($_.Name)" }
$remove += Get-ChildItem (Join-Path $Path 'share\extensions') -Directory -Filter 'dict-*' |
    Where-Object { $_.Name.Substring('dict-'.Length) -notin @('en', 'id') } |
    ForEach-Object { "share\extensions\$($_.Name)" }

$before = (Get-ChildItem $Path -Recurse -File | Measure-Object Length -Sum).Sum
foreach ($pattern in $remove) {
    Get-Item (Join-Path $Path $pattern) -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
}
$after = (Get-ChildItem $Path -Recurse -File | Measure-Object Length -Sum).Sum

Write-Host ("Trimmed {0:N0} MB to {1:N0} MB" -f ($before / 1MB), ($after / 1MB))
