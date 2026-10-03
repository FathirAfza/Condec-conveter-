# Condec

Condec is a file converter for Windows: images, documents, PDFs and CAD drawings, all processed on your own device. No file is uploaded, no internet connection is used, and there is no telemetry.

Built with WinUI 3 (Windows App SDK) and .NET 10, packaged as MSIX. License: GPL-3.0-or-later.

## Features

- **Verified results.** Every conversion goes through four stages: decode, encode, chunk verification (SHA-256 of each 1 MiB, read back from disk) and an integrity check (SHA-256 of the whole file, then the file is reopened with the matching decoder). A file is saved only when every stage passes, so there is never a half-written file.
- **Many files at once.** Choose or drop several files of one kind (pictures, PDFs, documents, spreadsheets, presentations, audio or video) and convert them together. Result 1 comes from file 1, result 2 from file 2, and so on. They are saved into a folder you pick with automatic names; a name that is already taken gets " (2)", so nothing is overwritten. A file that fails does not stop the others, and the failed ones can be tried again.
- **Pages.** A PDF or a multi-page TIFF going to a picture format becomes one picture per page: all pages, the first page, or pages you type such as `1-3, 5`.
- **Local history.** File name, format, time, output location and verification status are kept in `%LOCALAPPDATA%\Condec\history.json`. No copy of any file's contents is kept. History can be cleared or switched off.
- **PDF → DXF/DWG.** Lines, arcs, circles, curves and text of a vector PDF become editable CAD objects, with a choice of unit and scale. Choose all pages, the first page, or pages such as `1-3, 5`; they become one DWG per page, or one DWG with the pages side by side. A scanned PDF is treated like a picture.
- **Upscale pictures.** Real-ESRGAN x4plus enlarges a picture 1.5× to 16× (as far as the device allows) on the GPU through DirectML, or on the CPU, with ONNX Runtime. The model ships inside the app and runs on the device. The first upscale on an engine measures its speed, so the time estimate is real, not invented.
- **Architecture page.** A picture or scanned drawing is read on the device: wall lines, doors and windows, text (Windows OCR), logos and tables are found and listed, and you untick what should not become CAD. Blurry areas are marked, and Condec can upscale the picture first. The result is a DWG (or DXF) with the layers WALLS, OPENINGS, TEXT, LOGO and TABLE. The other direction turns a DWG or DXF into PDF, PNG or JPG with a preview, paper size, DPI and layer choice. Several pictures, PDF pages or DXF files of one kind form a queue: each item is read in the background and reviewed on its own (objects, upscale, calibration), then all of them are converted and saved into a folder you pick. The queue holds up to 20 items. Both directions live on the Architecture page, not on Convert File.
- **Your language.** The app follows the Windows language list: English and Indonesian today, English for any other language.
- **Fluent look.** Follows the Windows 11 light, dark and high-contrast themes.

## Supported formats

The format list in the app is built from what the machine can actually do. A format whose codec is missing is not offered.

| Source | Target | Engine |
|---|---|---|
| JPG, PNG, BMP, GIF, TIFF | JPG, PNG, BMP, GIF, TIFF, HEIC¹ | Windows Imaging Component (`Windows.Graphics.Imaging`) |
| HEIC, HEIF, WebP² | JPG, PNG, BMP, GIF, TIFF | Windows Imaging Component |
| PNG, JPG, HEIC², HEIF² and scanned PDF (Architecture page) | DWG, DXF (AutoCAD 2000) | Windows Imaging Component, Windows OCR, own line tracing, ACadSharp |
| MP3, M4A, WAV, WMA, FLAC | MP3, M4A, WAV, WMA, FLAC | `Windows.Media.Transcoding` |
| MP4, M4V, MOV, WMV, AVI | MP4, WMV, and the audio formats above | `Windows.Media.Transcoding` |
| PDF (one file per chosen page) | JPG, PNG, BMP, GIF, TIFF, HEIC¹ | `Windows.Data.Pdf` |
| PDF (chosen pages, Architecture page) | DXF, DWG (AutoCAD 2000) | PdfPig + ACadSharp |
| PDF | DOCX, DOC, ODT, PPTX, PPT, ODP | LibreOffice (PDF import into Writer or Impress) |
| DOCX, DOC, ODT, RTF, TXT | PDF, DOCX, DOC, ODT, RTF | LibreOffice |
| XLSX, XLS, ODS | PDF, XLSX, XLS, ODS | LibreOffice |
| PPTX, PPT, ODP | PDF, PPTX, PPT, ODP | LibreOffice |
| DWG, DXF (Architecture page) | PDF, PNG, JPG | ACadSharp, own drawing code, `Windows.Data.Pdf` |
| DXF → DWG (Architecture page) | DWG | ACadSharp |

¹ HEIC is offered only when the HEVC codec is installed and has been proven to work.
² Only when the HEIF or WebP decoder is installed in Windows (extensions from the Microsoft Store).

Audio and video use the profiles Windows provides, so only formats whose encoders are present on the PC are offered. WAV and FLAC keep the source's sample rate, channels and bit depth; MP3, M4A and WMA are written at 192, 128 or 96 kbps as you choose, and a video can be made 1080p, 720p or 480p (never enlarged, shape kept), also in the same format, to make it smaller. Tags (title, artist, album) are kept.

When a source file looks cut off or damaged, the conversion still runs and the result carries a warning. Windows reads such PNG, JPEG, GIF, WAV, FLAC, WMA and WMV files without an error and fills in or ends early, so Condec checks the file's own structure. A cut-off MP3 can't be told from a short one.

LibreOffice ships inside the x64 Condec package, so no other app needs to be installed. If LibreOffice is already installed on the system, that copy is used.

### PDF → DXF/DWG

- Lines become LINE or LWPOLYLINE (connected lines are joined, even when the PDF draws them separately). Béziers that form a circle become ARC or CIRCLE; other curves become one SPLINE per run.
- Solid fills that the PDF producer split into triangles are merged back into one closed outline, so there are no false diagonals. White paint over paper (backgrounds, masks) is dropped.
- Text becomes one TEXT per line, not per word. Hidden text (an OCR layer) is dropped, except where it pairs with letters drawn as outlines; those outlines are replaced with editable TEXT.
- Geometry outside the page and outside the PDF's clipping window (viewport) is cut off, so objects that aren't visible in a viewer don't come along.
- PDF colors are mapped to the nearest AutoCAD color index.
- Units: 1 PDF point = 1/72 inch, times the chosen scale. `$INSUNITS` is set to match.
- Encrypted PDFs (including ones that only restrict permissions) are refused. Condec doesn't remove any protection.
- Several pages in one drawing are placed left to right with a gap of 10% of a page's width, in the unit of the first page.

### Picture → DWG/DXF (Architecture page)

- The picture is thinned to center lines and fitted with lines and arcs (LINE, LWPOLYLINE, ARC). Doors (arcs of 60–120°) and windows (a few close parallel lines) are looked for, text is read with Windows OCR in the languages of your Windows profile (without an OCR language pack the letters are drawn as lines), and a title block or table is kept as one object.
- Soft areas of the picture are marked. When there are five or more, Condec offers to upscale first (2×, 4× or 8× as far as the device allows) and then reads the result again.
- The drawing is sized from the picture's resolution (96 dpi when the file doesn't state one) unless you calibrate: mark two points and enter the real distance.
- This is tuned on synthetic drawings; real floor plans may need checking and untick-ing of objects. A photo or a colour render becomes one logo object, not lines.
- Old DXF files (AutoCAD R10 and older) are read as R12, so their polylines come through.

## Privacy

- There is no network code in Condec. A unit test makes sure `Condec.Core` doesn't reference the `System.Net.*` assemblies.
- The MSIX manifest doesn't declare the `internetClient` capability.
- The bundled LibreOffice runs headless with its update check switched off.
- The upscale model runs on the device through ONNX Runtime, with ONNX Runtime's telemetry events switched off.
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

# Once: download the upscale model (Real-ESRGAN x4plus as ONNX, SHA-256 pinned) into
# third_party\models (ignored by git). Without it the app builds, but Upscale reports the model missing.
tools\fetch-model.ps1

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
src/Condec.Core/     converter contracts, the 4-stage pipeline, verification, history, PDF → CAD, drawing analysis and CAD rendering
                     Platform/Windows/: converters that use WinRT APIs and LibreOffice
                     Resources/: the strings in each language
tests/Condec.Tests/  xUnit v3; Windows/ runs only on Windows
tools/               PowerShell scripts for LibreOffice and MSIX
docs/                specification, plan and handoff notes
```

## License

Condec is licensed under the [GNU GPL version 3 or later](LICENSE), with an [additional permission](LICENSE-ADDITIONAL-PERMISSION.md) (GPLv3 section 7) to combine it with the Windows App SDK and other Windows components. The list of dependencies and their licenses is in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Copyright (C) 2026 Condec contributors
