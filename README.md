# Condec

Condec is a file converter for Windows: images, documents, PDFs and CAD drawings, all processed on your own device. No file is uploaded, no internet connection is used, and there is no telemetry.

Built with WinUI 3 (Windows App SDK) and .NET 10, packaged as MSIX. License: GPL-3.0-or-later.

## Features

- **Verified results.** Every conversion goes through four stages: decode, encode, chunk verification (SHA-256 of each 1 MiB, read back from disk) and an integrity check (SHA-256 of the whole file, then the file is reopened with the matching decoder). A file is saved only when every stage passes, so there is never a half-written file.
- **Local history.** File name, format, time, output location and verification status are kept in `%LOCALAPPDATA%\Condec\history.json`. No copy of any file's contents is kept. History can be cleared or switched off.
- **PDF → DXF/DWG.** Lines, arcs, circles, curves and text of a vector PDF become editable CAD objects, with a choice of unit, scale and page. A scanned PDF is traced into polylines.
- **Picture → DXF/DWG.** PNG, JPG, HEIC and other pictures are traced into closed outlines, sized in millimeters from the picture's resolution. A DWG is made from the DXF, the same steps as converting the DXF yourself.
- **Your language.** The app follows the Windows language list: English and Indonesian today, English for any other language.
- **Fluent look.** Follows the Windows 11 light, dark and high-contrast themes.

## Supported formats

The format list in the app is built from what the machine can actually do. A format whose codec is missing is not offered.

| Source | Target | Engine |
|---|---|---|
| JPG, PNG, BMP, GIF, TIFF | JPG, PNG, BMP, GIF, TIFF, HEIC¹ | Windows Imaging Component (`Windows.Graphics.Imaging`) |
| HEIC, HEIF, WebP² | JPG, PNG, BMP, GIF, TIFF | Windows Imaging Component |
| JPG, PNG, BMP, GIF, TIFF, HEIC², WebP² | DXF, DWG (AutoCAD 2000) | Windows Imaging Component, outline tracing, ACadSharp |
| PDF (one page) | JPG, PNG, BMP, GIF, TIFF, HEIC¹ | `Windows.Data.Pdf` |
| PDF (one page) | DXF, DWG (AutoCAD 2000) | PdfPig + ACadSharp |
| PDF | DOCX, DOC, ODT, PPTX, PPT, ODP | LibreOffice (PDF import into Writer or Impress) |
| DOCX, DOC, ODT, RTF, TXT | PDF, DOCX, DOC, ODT, RTF | LibreOffice |
| XLSX, XLS, ODS | PDF, XLSX, XLS, ODS | LibreOffice |
| PPTX, PPT, ODP | PDF, PPTX, PPT, ODP | LibreOffice |
| DXF, DWG | PDF | ACadSharp (DWG → DXF), then LibreOffice Draw |
| DXF ↔ DWG | DWG, DXF | ACadSharp |

¹ HEIC is offered only when the HEVC codec is installed and has been proven to work.
² Only when the HEIF or WebP decoder is installed in Windows (extensions from the Microsoft Store).

Audio and video conversion (`Windows.Media.Transcoding`) is still being worked on.

LibreOffice ships inside the x64 Condec package, so no other app needs to be installed. If LibreOffice is already installed on the system, that copy is used.

### PDF → DXF/DWG

- Lines become LINE or LWPOLYLINE (connected lines are joined, even when the PDF draws them separately). Béziers that form a circle become ARC or CIRCLE; other curves become one SPLINE per run.
- Solid fills that the PDF producer split into triangles are merged back into one closed outline, so there are no false diagonals. White paint over paper (backgrounds, masks) is dropped.
- Text becomes one TEXT per line, not per word. Hidden text (an OCR layer) is dropped, except where it pairs with letters drawn as outlines; those outlines are replaced with editable TEXT.
- Geometry outside the page and outside the PDF's clipping window (viewport) is cut off, so objects that aren't visible in a viewer don't come along.
- PDF colors are mapped to the nearest AutoCAD color index.
- Units: 1 PDF point = 1/72 inch, times the chosen scale. `$INSUNITS` is set to match.
- Encrypted PDFs (including ones that only restrict permissions) are refused. Condec doesn't remove any protection.

### Picture → DXF/DWG

- The dark shapes are found with Otsu thresholding and traced into closed LWPOLYLINE outlines (marching squares, then Ramer–Douglas–Peucker simplification). Light shapes on a dark background are flipped first, so the shapes are traced and not the background.
- This works best for drawings, logos and scans. A photo gives only a rough result.
- The drawing is in millimeters, sized from the picture's resolution (96 dpi when the file doesn't state one). A picture larger than 3000 pixels is averaged down before tracing.
- For DWG, the traced drawing is written as DXF first, read back, and converted to DWG.
- Old DXF files (AutoCAD R10 and older, which many image-to-DXF tools write) are read as R12, so their polylines come through.

## Privacy

- There is no network code in Condec. A unit test makes sure `Condec.Core` doesn't reference the `System.Net.*` assemblies.
- The MSIX manifest doesn't declare the `internetClient` capability.
- The bundled LibreOffice runs headless with its update check switched off.
- A limit of the verification: the read-back for chunk verification can be served from the operating system's cache, so it catches write mistakes made by the app, not damage to the storage medium.

## Download and install

Every release on the [Releases](../../releases) page offers two forms.

**Portable (the quickest way to try it).** Download `Condec_<version>_x64_portable.zip`, extract it to any folder, and run `Condec.exe`. Nothing is installed. Because the files aren't signed by a trusted publisher yet, SmartScreen may ask once: choose "More info" and then "Run anyway".

**MSIX (installed, in the Start menu).** Pre-release builds are signed with a test certificate, and each build has its own, so import the certificate of the build you install. In PowerShell as administrator, one command at a time:

```powershell
cd "$env:USERPROFILE\Downloads"
Export-Certificate -Cert (Get-AuthenticodeSignature .\Condec_<version>_x64.msix).SignerCertificate -FilePath .\condec-signer.cer
Import-Certificate -FilePath .\condec-signer.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
Add-AppxPackage -Path .\Condec_<version>_x64.msix
```

Close Condec before updating it. Requirements: Windows 10 1809 or later, 64-bit. The MSIX also needs the Windows App Runtime 2.5 (App Installer usually downloads it); the portable build already includes it.

Releases are built by the workflow `.github/workflows/release.yml` on a Windows runner whenever a `v*` tag is pushed: build, all tests, download and trim LibreOffice, pack and sign the MSIX, publish the portable build, then upload the `.msix`, `.cer`, `_portable.zip` and `SHA256SUMS.txt`.

## Building

Prerequisites on Windows 10 1809 or later:

- .NET 10 SDK.
- Windows App Runtime 2.5 (`Microsoft.WindowsAppRuntime.2`) to run the app. To deploy from an IDE, Visual Studio with the WinUI / Windows App SDK workload and Developer Mode on.
- PowerShell for the scripts in `tools\`.

```powershell
# Once: download the official LibreOffice MSI (SHA-256 pinned), extract it without installing,
# and trim it into third_party\libreoffice (ignored by git).
tools\fetch-libreoffice.ps1

dotnet build Condec.sln
dotnet test --solution Condec.sln

# Packages to share: an MSIX signed with a test certificate, and a portable zip with Condec.exe.
tools\pack-test-msix.ps1
tools\publish-portable.ps1
```

Run the tests from PowerShell with `--solution` or `--project`; the Microsoft.Testing.Platform runner treats a positional argument as an argument for the test host.

On Linux or macOS only `Condec.Core` and the cross-platform tests can be built:

```sh
dotnet build Condec.Portable.slnf
dotnet test --solution Condec.Portable.slnf
```

Build output on non-Windows machines goes to `~/.cache/condec-artifacts`.

## Languages

All texts are string resources in `src/Condec.Core/Resources`: `Strings.resx` is English (the neutral language) and `Strings.<language>.resx` is a translation. To add a language, copy `Strings.resx` to `Strings.<language code>.resx`, translate the values, and add the code to `Languages.Supported`. The tests check that every language has the same keys and placeholders as English.

## Layout

```
src/Condec/          the WinUI 3 app (MVVM with CommunityToolkit.Mvvm), MSIX
src/Condec.Core/     converter contracts, the 4-stage pipeline, verification, history, PDF and picture → CAD
                     Platform/Windows/: converters that use WinRT APIs and LibreOffice
                     Resources/: the strings in each language
tests/Condec.Tests/  xUnit v3; Windows/ runs only on Windows
tools/               PowerShell scripts for LibreOffice and MSIX
docs/                specification, plan and handoff notes
```

## License

Condec is licensed under the [GNU GPL version 3 or later](LICENSE), with an [additional permission](LICENSE-ADDITIONAL-PERMISSION.md) (GPLv3 section 7) to combine it with the Windows App SDK and other Windows components. The list of dependencies and their licenses is in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Copyright (C) 2026 Condec contributors
