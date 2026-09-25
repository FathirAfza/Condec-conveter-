# Handoff: konteks proyek Condec

Ditulis 24 September 2026 di akhir sesi pertama (dikerjakan di server Linux). Sesi berikutnya dilanjutkan di mesin Windows lokal supaya aplikasi WinUI bisa di-build, dijalankan, dan dites. Diperbarui 25 September 2026 dari sesi cloud (Linux) yang memasukkan sumber ke repo GitHub; batasan sesi cloud ada di `docs/CLOUD-SESSION-PROMPT.md` dan §6.

Baca urut: `docs/SPEC.md` (spesifikasi dan aturan pemilik proyek) → `docs/PLAN.md` (rencana teknis) → dokumen ini.

## 1. Status

| Tahap | Status |
|---|---|
| 1. Rencana + scaffold | Selesai (24 Sep 2026, Windows). `dotnet build Condec.sln` 0 warning/0 error tanpa perubahan kode, test 1/1. Jendela jalan: 1120×780 epx, title bar 32 epx, tema gelap dicek. Tema terang belum dicek. |
| 2. Condec.Core | Selesai, di-commit (`28b0d6b`). Build 0 warning/0 error, test 81/81. |
| 3. UI + konverter gambar | Selesai, menunggu konfirmasi pemilik. Build 0 warning/0 error, test 174/174 (82 di tiap TFM + 10 khusus Windows). Belum di-commit. |
| 5. Dokumen via LibreOffice | Selesai, menunggu konfirmasi pemilik. LibreOffice 26.8 dibawa di dalam paket. Build 0 warning/0 error, test 240/240 (termasuk konversi nyata lewat salinan bawaan). Belum di-commit. Dikerjakan sebelum tahap 4 atas permintaan pemilik. |
| 6. PDF → gambar/CAD/dokumen, DWG → PDF | Selesai (Windows, 24 Sep). **Direvisi 25 Sep 2026 di sesi cloud** atas laporan pemilik bahwa hasil PDF → DXF/DWG "banyak objek yang tidak tepat": bug rotasi ganda diperbaiki dan konverter dioptimalkan (lihat Fakta tahap 6, revisi). Revisi kedua di hari yang sama dengan PDF asli pemilik (isian tersegitiga, cat putih, teks outline, TEXT per baris). Test portabel 178/178. **Test Windows dan uji UI belum dijalankan ulang setelah revisi.** |
| 4 | Belum dimulai (setelah tahap 6, atas permintaan pemilik). |
| 7. README, LICENSE, THIRD-PARTY-NOTICES | **Selesai** (25 Sep 2026, sesi cloud, dikerjakan sebelum tahap 4 atas permintaan pemilik): `LICENSE` (teks GPL-3.0 utuh), `LICENSE-ADDITIONAL-PERMISSION.md` (izin GPLv3 §7 untuk komponen Microsoft), `THIRD-PARTY-NOTICES.md`, `README.md` (fitur, tabel format, privasi, cara build, struktur, lisensi), header SPDX di semua `.cs` dan `.ps1`, properti `Copyright` di `Directory.Build.props`. README masih menyebut audio/video "sedang dikerjakan"; perbarui di tahap 4. |
| Repo GitHub | Sumber (tahap 1–3, 5, 6) di-commit apa adanya ke `FathirAfza/Condec-conveter-`, branch `claude/kind-mayer-9uxymj` (25 Sep 2026, sesi cloud). Di Linux: build `Condec.Portable.slnf` 0 warning/0 error, test portabel 139/139. Test Windows belum dijalankan ulang. |

**Tugas berikutnya:** (1) di Windows: jalankan ulang test Windows dan uji UI PDF → DXF/DWG setelah revisi 25 Sep dengan `Contoh_Gambar_Kerja_BCC_15th.pdf` (pemilik punya filenya), lalu buka hasilnya di AutoCAD; (2) tahap 4 (audio/video). Urutan yang diminta pemilik: 5, 6, lalu 4. Antislop tetap dipakai selama pengerjaan UI.

**Permintaan pemilik saat tahap 3 (24 Sep 2026):** butuh DOC/XLS/PPT/DWG dan lain-lain ke PDF, serta PDF ke DWG/DXF. DOC/XLS/PPT ke PDF sudah di rencana (tahap 5, LibreOffice). PDF ke DXF/DWG sudah di rencana (tahap 6). DWG → PDF dikerjakan di tahap 6 (ACadSharp mengubah DWG ke DXF, lalu LibreOffice ke PDF).

Cara kerja yang diminta pemilik: setelah setiap tahap, laporkan apa yang selesai beserta hasil build dan test, lalu **berhenti dan tunggu konfirmasi**. Pemilik berkomunikasi dalam bahasa Indonesia.

## 2. Isi repo saat ini

```
Condec.sln                  platform x64/x86/ARM64 (x64 pertama, jadi default; tanpa "Any CPU")
Condec.Portable.slnf        hanya Core+Tests, untuk mesin non-Windows
global.json                 SDK 10.0.100+ (rollForward latestFeature), test runner Microsoft.Testing.Platform
Directory.Build.props       LangVersion/Nullable/ImplicitUsings, properti CondecWindowsTargetFramework & CondecWindowsMinVersion,
                            EnableWindowsTargeting + ArtifactsPath=~/.cache/condec-artifacts khusus non-Windows
Directory.Packages.props    versi terpusat + CentralPackageTransitivePinningEnabled
Condec.html                 bundel desain asli (6 layar)
docs/SPEC.md, PLAN.md, HANDOFF.md, LOCAL-SESSION-PROMPT.md
docs/design/main-window.html, pdf-to-cad.html   markup mentah mockup hasil bongkar Condec.html
src/Condec/                 WinUI 3, MSIX single-project. Cangkang: Mica, title bar kustom 32px, jendela 1120×780 (dikali DPI), judul "Konversi file"
src/Condec.Core/            multi-target net10.0 + net10.0-windows10.0.26100.0. Conversion/ (kontrak, registry), Pipeline/ (4 tahap, hashing, verifikasi, jurnal),
                            History/, Formats/ (katalog nama, format waktu/ukuran). File di Platform/Windows/** hanya ikut dikompilasi di target Windows (belum ada)
tests/Condec.Tests/         xunit.v3.mtp-v2, net10.0; 81 test, termasuk OfflineGuaranteeTests (Core tidak boleh mereferensikan assembly System.Net.*)
```

## 3. Fakta yang sudah diverifikasi (jangan diulang tanpa alasan)

- **.NET 10** adalah LTS (sampai 14 Nov 2028). SDK yang dipakai di Linux: 10.0.401.
- **Windows App SDK 2.5.1** = Stable terbaru (rilis 16 Sep 2026, sumber: halaman "release channels" di Microsoft Learn).
- Windows App SDK direferensikan lewat **paket komponen**, `Microsoft.WindowsAppSDK.WinUI` + `Microsoft.WindowsAppSDK.Runtime` (mode framework-dependent, didukung sejak 1.8). Metapaket tidak dipakai karena ikut menarik AI/ML/Search/Widgets.
  - Paket Runtime punya target `VerifyReferencedWindowsAppSdkComponentVersions` (InitialTargets) yang **menggagalkan build kalau versi komponen tidak persis**: Foundation 2.3.12, InteractiveExperiences 2.1.9, WinUI 2.3.9 (Base 2.0.4). Tanpa pin, NuGet memilih InteractiveExperiences 2.1.8. Karena itu semuanya dipin di `Directory.Packages.props`.
  - Kalau build di Windows bermasalah karena paket komponen, fallback-nya: ganti ke metapaket `Microsoft.WindowsAppSDK` 2.5.1 dan hapus pin komponen.
- **Compiler XAML WinUI** (`tools/net472/XamlCompiler.exe`) hanya jalan di Windows. Di Linux build berhenti dengan "Exec format error". Semua langkah sebelumnya (restore, cek versi komponen) sudah lulus di Linux, dan solusi otomatis memakai x64 (`obj/x64/Debug/.../win-x64`).
- **Tidak ada template `dotnet new` WinUI resmi** dari Microsoft. csproj ditulis manual mengikuti `microsoft/WindowsAppSDK-Samples` (Samples/StoragePickers/cs-sample).
- **xUnit v3 4.0.1**: template resmi `xunit3` memakai paket `xunit.v3.mtp-v2` + `global.json` `"test": {"runner": "Microsoft.Testing.Platform"}` untuk .NET 10. Catatan: `dotnet test` (MTP) membuat folder `TestResults/` kosong di direktori kerja. Pakai `--results-directory` kalau mengganggu.
- **API WinUI yang sudah dicek di metadata** (Microsoft.WinUI.dll 2.3.9, Microsoft.InteractiveExperiences.Projection.dll 2.1.9):
  - `Window`: `AppWindow`, `ExtendsContentIntoTitleBar`, `SystemBackdrop`, `SetTitleBar`, event `Activated`
  - `AppWindow`: `Id`, `TitleBar`, `Resize`, `SetIcon`; `AppWindowTitleBar.PreferredHeightOption`; `TitleBarHeightOption.Standard/Tall/Collapsed`
  - Lainnya: `MicaBackdrop`, `WindowActivationState.Deactivated`, `WinRT.Interop.WindowNative.GetWindowHandle`, `Microsoft.UI.Win32Interop.GetWindowIdFromWindow`
- **Save picker**: `Microsoft.Windows.Storage.Pickers.FileSavePicker(AppWindow.Id)` (Windows App SDK). Sejak 2.0 picker ini **tidak membuat file kosong** dan punya `ShowOverwritePrompt`, `SuggestedFileName`, `FileTypeChoices`, `DefaultFileExtension`. Hasil `PickSaveFileAsync()` mengembalikan `.Path`. Cocok dengan skema `.condec-tmp`.
- **PdfPig 0.1.16** (Apache-2.0): `Page.Paths` → `PdfPath` (`IsClipping`, `IsFilled`, `IsStroked`, `LineWidth`) → `PdfSubpath` dengan `Line`, `CubicBezierCurve`, `Move`, `Close`. Juga `Page.GetImages()` (`IPdfImage.BoundingBox`; `Bounds` usang), `Page.GetWords()`, `Page.Letters`, `PdfDocument.IsEncrypted`, `PdfDocumentEncryptedException`.
- **ACadSharp 3.8.0** (MIT): DwgWriter mendukung AC1014, AC1015, AC1018, AC1024, AC1027, AC1032 (bukan AC1021). DxfWriter mendukung AC1012–AC1032. API yang dipakai sudah dicek di tahap 6 (lihat Fakta tahap 6).
- **CommunityToolkit.Mvvm 8.4.2** (MIT).
- **Mesin Windows** (24 Sep 2026): .NET SDK 10.0.302, Developer Mode aktif, framework `Microsoft.WindowsAppRuntime.2` 2.5.1.0 x64 sudah terpasang. Tidak ada Visual Studio.
- **Menjalankan aplikasi dari CLI:** build tidak menyalin `Assets\` ke `bin`. Salin setiap `AppXManifest`/`AppxPackagedFile` di `bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\Condec.build.appxrecipe` ke `LayoutDir\PackagePath` (sama seperti F5 di VS), lalu `Add-AppxPackage -Register <LayoutDir>\AppxManifest.xml`. Setelah itu jalankan `shell:AppsFolder\Condec_xhvencdhx5j6m!App`.
- **AppData pada aplikasi MSIX** (Microsoft Learn, "Understanding how packaged desktop apps run on Windows"): mulai Windows 10 1903, file dan folder **baru** di `AppData\Local` dari aplikasi yang divirtualisasi ditulis ke lokasi privat per pengguna per paket, tetapi tampil di path asli. Semuanya dihapus saat uninstall. File yang sudah ada di path asli tidak divirtualisasi, jadi test tidak boleh menulis ke `%LOCALAPPDATA%\Condec` sungguhan. Path fisiknya dicek langsung di tahap 3.
- **Singkatan bulan `id-ID`** (data kultur Windows): Jan, Feb, Mar, Apr, Mei, Jun, Jul, Agu, Sep, Okt, Nov, Des.

### Fakta tahap 3 (diuji di Windows)

- `history.json` ditulis ke `%LOCALAPPDATA%\Packages\Condec_xhvencdhx5j6m\LocalCache\Local\Condec\` (AppData tervirtualisasi MSIX). `%LOCALAPPDATA%\Condec` asli tidak dibuat.
- `OverlappedPresenter.PreferredMinimumWidth/Height` berunit **piksel fisik**: nilai 760×680 epx dikali 1,25 menjadi jendela minimum 950×850 px.
- `HyperlinkButton` tidak punya properti `Flyout`; pakai `FlyoutBase.AttachedFlyout` + `FlyoutBase.ShowAttachedFlyout`.
- Dialog picker adalah jendela `#32770` milik jendela Condec, tidak muncul sebagai anak desktop di UIA. Skrip uji: `EnumWindows`, lalu `WM_SETTEXT` ke kotak nama dan `BM_CLICK` ke tombol.
- Kotak nama file di dialog buka memakai id 1148, di dialog simpan id 1001.
- `dotnet test` dari Git Bash melaporkan 0 test; dari PowerShell 174 lulus. Jalankan test dari PowerShell.
  - Penyebab yang sama muncul di Linux (25 Sep 2026): dengan runner Microsoft.Testing.Platform, `dotnet test Condec.Portable.slnf` meneruskan argumen posisi itu mentah ke test host (`Command line arguments: 'Condec.Portable.slnf --server dotnettestcli ...'` di log diagnostik), sehingga "Zero tests ran", exit code 5. Bentuk yang benar: `dotnet test --solution Condec.Portable.slnf` atau `dotnet test --project tests/Condec.Tests/Condec.Tests.csproj`.
- Root grid halaman: `MaxWidth` saja membuat kolom 880 tidak rata tengah. Lebar diatur dari code-behind (`UpdatePageSize`).
- PNG 1600×1200 (6,4 MB) ke JPG selesai kurang dari 1 detik, 2 chunk.

### Fakta tahap 5 (diuji di Windows, LibreOffice 26.8.0.3)

- LibreOffice dipasang lewat `winget install TheDocumentFoundation.LibreOffice` dengan izin pemilik.
- Registry: `HKLM\SOFTWARE\LibreOffice\UNO\InstallPath` (default) = folder `program`. Ada juga `HKLM\SOFTWARE\LibreOffice\LibreOffice\26.8\Path` = path `soffice.exe`.
- `soffice.exe --headless --convert-to` **menunggu sampai konversi selesai** dan exit code 0. Proses kerjanya `soffice.bin`, jadi pembatalan membunuh seluruh pohon proses.
- Profil baru makan 8–16 detik, profil yang sudah ada sekitar 3 detik. Karena itu profil Condec disimpan permanen, bukan dibuat baru tiap konversi.
- LibreOffice berjalan **di luar kontainer MSIX**, jadi tulisannya ke `%LOCALAPPDATA%` tidak divirtualisasi. Profil ditaruh di `ApplicationData.Current.LocalCacheFolder\libreoffice-profile` (`%LOCALAPPDATA%\Packages\Condec_xhvencdhx5j6m\LocalCache\libreoffice-profile`) supaya ikut terhapus saat uninstall.
- Pengecekan update: job `UpdateCheck` hanya dipicu event `onFirstVisibleTask` (`share\registry\onlineupdate.xcd`), yang tidak terjadi di mode headless. `AutoCheckEnabled` tetap di-set `false` di `registrymodifications.xcu`. LibreOffice mengubahnya ke `true` saat membuat profil baru, walaupun sudah diisi sebelumnya, jadi Condec men-set ulang setelah setiap jalan. Setelah profil jadi, nilai `false` bertahan.
- **Tidak ada filter impor DWG** di LibreOffice 26.8. Hanya ada `DXF - AutoCAD Interchange` (impor ke Draw). DWG → PDF tidak bisa lewat LibreOffice saja. Tahap 6 memakai jalur ACadSharp: DWG → DXF → LibreOffice → PDF (sudah diuji).
- DOCX setengah terpotong: LibreOffice selesai tanpa file output, lalu Condec menampilkan pesan "LibreOffice tidak bisa mengonversi file ini".
- Uji UI (MSIX): DOCX → PDF 7,5 detik (termasuk membuat profil), XLSX → PDF 9 detik. Keduanya lolos verifikasi.
- **Keputusan pemilik (24 Sep 2026): LibreOffice dibawa di dalam paket Condec.** Pengguna tidak boleh diminta memasang aplikasi kedua. `tools\fetch-libreoffice.ps1` mengunduh MSI resmi (SHA-256 dipin, dari manifest winget), mengekstraknya dengan `msiexec /a` (tanpa memasang), lalu `tools\trim-libreoffice.ps1` membuang bagian yang tidak dipakai. Hasilnya ada di `third_party\libreoffice` (diabaikan git) dan dipaketkan ke `LibreOffice\` di MSIX (khusus x64). Kalau LibreOffice terpasang di sistem, Condec memakai yang terpasang karena biasanya lebih baru. THIRD-PARTY-NOTICES (tahap 7) wajib mencantumkan MPL-2.0 dan tautan ke kode sumber LibreOffice.
- **Permintaan pemilik untuk tahap 6:** PDF → JPG/PNG/HEIC, DOCX/DOC, PPTX/PPT, selain DXF/DWG. PDF → XLSX/XLS **ditunda** (LibreOffice tidak bisa mengimpor PDF ke Calc). Hasil uji: `--infilter=writer_pdf_import` → DOCX berisi kotak teks berposisi tetap. `--infilter=impress_pdf_import` → PPTX, satu slide per halaman. Tanpa infilter, PDF dibuka di Draw, yang tidak punya ekspor PPTX.
- **LibreOffice bawaan (sudah dikerjakan dan diuji):**
  - `msiexec /a` menghasilkan 1,5 GB (semua bahasa dan kamus). Setelah dipangkas jadi 561 MB, atau **190 MB** kalau dikompres ZIP. Perkiraan installer sekitar 200 MB.
  - MSI resmi meletakkan font (127 file, misalnya Carlito dan Liberation) di `Fonts\` dan runtime VC++ di `System64\`, yang pada instalasi biasa dipasang ke Windows. Skrip memindahkan font ke `share\fonts\truetype`, yang didaftarkan LibreOffice secara privat (`AddFontResourceExW` + `FR_PRIVATE`, `vcl/win/gdi/salfont.cxx`). DLL runtime dipindah ke `program\`.
  - Diuji setelah LibreOffice sistem **dicopot** (atas izin pemilik; font Liberation juga hilang dari Windows). DOCX/XLSX/PPTX → PDF dan PPTX → ODP lewat Condec berhasil, dan PDF-nya berisi `LiberationMono`, jadi font privat terbukti dipakai.
  - Folder `share\autotext\en-US` dan `id` membuat MakePri memberi warning PRI263 (dikira resource bahasa Condec). AutoText hanya untuk mengetik, jadi dibuang oleh skrip pemangkas.
  - Dev deploy: `Add-AppxPackage -Register` di atas layout yang berubah bisa gagal dengan 0x80073CFB. Solusinya, copot paket dulu (`deploy-loose.ps1` sudah melakukannya). Akibatnya profil LibreOffice di LocalCache dibuat ulang, dan konversi pertama makan sekitar 16–19 detik.
  - MSI yang diunduh masih ada di `%TEMP%\condec-libreoffice-26.8.0\` (358 MB, `-KeepDownload`), jadi aman dihapus.
- Format yang butuh LibreOffice tapi tidak tersedia (misalnya build x86/ARM64, yang belum membawa LibreOffice) tetap tampil dengan keterangan "Tidak tersedia", tapi pilihannya ditolak di `FormatComboBox_SelectionChanged`. Menonaktifkan `ComboBoxItem` justru **membuat daftar langsung tertutup** kalau item yang disorot ikut dinonaktifkan. Hal ini diuji dengan menyembunyikan `soffice.exe` di layout.

### Fakta tahap 6 (diuji di Windows)

- **Windows.Data.Pdf:** `PdfPage.Size` berunit DIP (A4 pada 200 dpi = 1653×2339 px). PDF harus dibuka lewat `StorageFile.GetFileFromPathAsync` + `PdfDocument.LoadFromFileAsync`. Stream .NET yang dibungkus ke `IRandomAccessStream` membuat host test crash 0xC0000005 saat keluar.
- **HEIC:** hanya ditawarkan kalau probe (encode 16×16 lalu decode) berhasil. Di mesin ini berhasil.
- **PDF → DXF/DWG (PdfPig + ACadSharp):**
  - Path yang sama digambar dua kali (isi lalu garis tepi) digabung lewat tanda tangan koordinat. Path isi yang menutupi ≥ 95% halaman dianggap latar dan dilewati. Path kliping dilewati.
  - Bézier yang cocok dengan lingkaran (toleransi 0,2% jari-jari) jadi ARC. Busur yang bersambung digabung, dan rantai busur tertutup satu putaran jadi CIRCLE.
  - Teks jadi entitas TEXT per kata (tinggi = ukuran huruf × 0,7). `TextEntity.Rotation` ACadSharp berunit radian dan ditulis sebagai derajat di DXF (ada test-nya).
  - Deteksi scan: kurang dari 50 segmen dan satu gambar menutupi ≥ 70% halaman. Scan dirender 3 px/pt, diambang Otsu, lalu ditelusuri dengan marching squares + RDP menjadi polyline tertutup. Bintik < 3 px dibuang.
  - `/Rotate` halaman diikuti, jadi hasil CAD sama arahnya dengan tampilan viewer.
- **PDF → DOCX/DOC/ODT** memakai `--infilter=writer_pdf_import`, **PDF → PPTX/PPT/ODP** memakai `impress_pdf_import`. PDF terkunci ditolak sebelum LibreOffice dijalankan.
- **x:Bind:** fungsi pembantu harus method instance (static memberi CS0176). Hasil fungsi `bool` yang di-cast ke `Visibility` menghasilkan kode yang tidak bisa dibuild, jadi fungsi mengembalikan `Visibility` langsung.
- `NumberBox` skala kustom: kotak dikosongkan = `NaN`, dan tombol konversi dinonaktifkan. Nilai di bawah 1 dibulatkan ke 1 oleh `Minimum`.
- Uji UI (MSIX, lewat UI Automation): PDF vektor → DXF skala kustom 1:250 dalam meter (INSUNITS 6, lingkaran 4 cm jadi r ≈ 5 m, tanpa duplikat), DXF → DWG → PDF, PDF → DOCX dan PPTX (teks ikut). PDF terkunci dan PDF scan menampilkan pesan yang benar.

### Fakta tahap 6, revisi 25 Sep 2026 (sesi cloud, diuji dengan PDF sintetis lewat PdfPig di Linux)

- **Bug utama: rotasi ganda.** PdfPig 0.1.16 sudah menerapkan `/Rotate` halaman **dan** mengurangi asal CropBox pada semua koordinat path dan huruf (`page.Width`/`page.Height` sudah mengikuti orientasi tampilan, sedangkan `page.CropBox.Bounds` tetap mentah). `PageTransform` lama memutarnya lagi, sehingga halaman landscape dengan `/Rotate 90` (umum pada PDF CAD) keluar tercermin dan teks terbalik. Kini `PageTransform` hanya menskalakan. Dibuktikan dengan PDF `/Rotate 0/90/180/270` dan MediaBox/CropBox bergeser.
- **Teks pada halaman berputar** dipecah per huruf oleh `page.GetWords()` (DefaultWordExtractor hanya menggabungkan huruf horizontal; untuk `/Rotate 180` urutannya terbalik: "hawaB"). Sekarang memakai `NearestNeighbourWordExtractor.Instance.GetWords(letters)` (assembly `UglyToad.PdfPig.DocumentLayoutAnalysis`, ikut dalam paket PdfPig). `Word.BoundingBox` teks berputar bisa punya Left > Right, jadi kotak dibangun dari keempat sudutnya (`PdfToCadConverter.ToRect`).
- **Teks tersembunyi** (`Tr 3`, `Letter.RenderingMode == Neither/NeitherClip`, lapisan OCR di atas scan) tidak lagi menjadi TEXT.
- **Clipping.** `ParsingOptions.ClipPaths = true` PdfPig **tidak dipakai**: ia meratakan semua Bézier jadi 10 garis, membalik urutan titik, dan pada uji sederhana (segitiga isi + garis tepi) menghilangkan objek yang sah. Gantinya `PdfClipTracker` memutar ulang `page.Operations` (q/Q, konstruksi path, W/W*, operator pengecatan) dan mencocokkannya satu-satu dengan `page.Paths` (setiap operator pengecatan atau `n` menghasilkan tepat satu entri; entri `W n` bertanda `IsClipping`). Clip non-persegi dipakai kotak pembatasnya (konservatif). Kalau jumlahnya tidak cocok (form XObject menambah path tanpa operator di daftar halaman), semua path hanya dipotong ke kotak halaman. Garis dipotong dengan Liang–Barsky; busur, spline, lingkaran, dan teks dibuang kalau kotaknya di luar clip.
- **Penggabungan lintas path.** Produsen CAD mengecat tiap segmen sebagai path sendiri, jadi "Gabungkan garis bersambung" dulu hampir tidak berpengaruh. `PolylineJoiner` menyambung run yang ujungnya bertemu tepat berdua (derajat 2, warna sama); pertemuan tiga ujung (sambungan T) tidak digabung. Rantai yang kembali ke awal menjadi LWPOLYLINE tertutup. Run dua titik ditulis sebagai LINE, bukan LWPOLYLINE.
- **Lingkaran tanpa `h`** (empat Bézier yang kembali ke titik awal tanpa Close) kini juga menjadi CIRCLE. Rangkaian Bézier non-lingkaran yang bersambung menjadi **satu** SPLINE (knot 0,0,0,0,1,1,1,…,n,n,n,n; flag Planar), bukan satu SPLINE per Bézier. Bézier yang lurus (titik kontrol pada tali busur, toleransi 0,1%) menjadi LINE.
- **Isian tipis** (jajar genjang isi tanpa garis tepi, lebar ≤ 1,5 pt, panjang ≥ 4× lebar) menjadi satu LINE di tengahnya; beberapa produsen menggambar garis dengan cara ini.
- **Warna** stroke/fill dipetakan ke indeks ACI terdekat dari palet kecil (`CadColors`; 1–9, abu-abu 250–254, dan beberapa warna antara). `ACadSharp.Color.ApproxIndex` **tidak dipakai** karena hasilnya salah (biru murni → 1). Hitam dan putih → 7. AC1015 tidak punya true color.
- **Extents** (`$EXTMIN`/`$EXTMAX`) diisi dari `Entity.GetBoundingBox()` (`CSMath.BoundingBox.Merge`, cek `Extent == Finite`).
- `ACadSharp.Entities.Arc.StartAngle/EndAngle` berunit radian dan ditulis derajat ke DXF (dicek: π/2 → 90).
- Test baru: `tests/Condec.Tests/TestPdf.cs` menulis PDF satu halaman dari content stream (font Helvetica /F1, form XObject /Fx opsional), dan `PdfToCadTests.cs` menguji semua perilaku di atas lewat PdfPig sungguhan di target `net10.0`.
- **Perlu dicek di Windows:** test `VectorPage_KeepsRectangleCircleLineAndText_InMillimeters` disesuaikan (garis 10 cm kini LINE); uji UI PDF → DXF ulang; buka DXF hasil di AutoCAD/LibreOffice untuk memastikan SPLINE dengan flag Planar dan knot ganda tiga diterima; minta PDF asli pemilik yang dulu hasilnya salah dan uji dengan itu.

### Fakta tahap 6, revisi kedua 25 Sep 2026 (diuji dengan PDF asli pemilik: `Contoh_Gambar_Kerja_BCC_15th.pdf`, 4 halaman A3, produser iLovePDF dari ekspor CAD)

- **DWG yang dikirim pemilik ternyata hasil konversi Condec lama** (AC1015, hanya LWPOLYLINE/TEXT/ARC, bbox 419,95 × 296,93 mm = A3). Dirender ke SVG dan dibandingkan dengan output baru; gambar `scratchpad` tidak disimpan di repo.
- **Penyebab utama "ngaco": isian yang ditriangulasi.** Produser PDF memecah setiap isian (latar sel kop gambar, pita bingkai, plat abu-abu) menjadi segitiga yang berbagi sisi, dicat dengan `b` (isi + garis tepi warna sama). Ditulis sebagai outline per segitiga, sisi bersamanya menjadi **diagonal besar melintang gambar**. Kini `PolygonMerger` menggabungkan ring yang berbagi sisi dan membuang sudut kolinear, jadi dua segitiga → satu persegi panjang. Isian yang garis tepinya sewarna dianggap bentuk padat (`solid`).
- **Cat putih di atas kertas** (`1 g 1 G`, kotak latar area gambar 970 × 785 pt dan bingkai halaman putih lw 5) tidak terlihat di viewer tetapi dulu ikut ditulis. Path yang semua catnya putih (≥ 0,97 di tiap kanal) kini dilewati (`IsPaperColoured`).
- **Kutipan miring** (font Swiss721BT-BoldCondensedItalic, 88 huruf) ada di PDF sebagai **outline huruf terisi (80 path) + teks tak terlihat (`3 Tr`)** untuk pencarian. Aturan: teks tak terlihat tetap dibuang, **kecuali** ada path isi yang berada di dalam kotak barisnya; path itu dibuang dan barisnya ditulis sebagai TEXT. Teks tak terlihat tanpa "punggung" (OCR di atas gambar) tetap dibuang.
- **TEXT per baris, bukan per kata** (`TextLines`): kata dengan arah sama, garis dasar selisih ≤ 0,3 × ukuran huruf, dan jarak ≤ 1 × ukuran huruf digabung ("NAMA JEMBATAN :", "1. NAMA KETUA TEAM"). Kolom tabel berikutnya lebih jauh, jadi tetap terpisah. Sesuai konvensi CAD (DWG lama pemilik punya TEXT per kata dan saling tumpang tindih).
- Hasil halaman 1: 365 → **135 objek** (60 LWPOLYLINE, 39 LINE, 35 TEXT, 1 CIRCLE), tanpa diagonal, dalam ±120 ms. Halaman 2: 946 objek (rangka jembatan, dimensi, arsiran tumpuan), 38 ms. Dua gambar raster (logo) di kop gambar diabaikan, karena tidak ada padanan CAD.
- Kelemahan yang masih ada: lebar garis PDF tidak dipetakan ke lineweight (nilai `w` mentah tanpa CTM, mis. `w 6` di bawah `cm 0,12`), garis putus-putus menjadi garis utuh, isian ditulis sebagai outline tertutup bukan HATCH SOLID.
- Alat bantu pengujian: proyek konsol sementara di scratchpad dengan `AssemblyName=Condec.Tests` (memakai `InternalsVisibleTo`), render entitas ke SVG lalu screenshot dengan Chromium headless (`/opt/pw-browsers/.../headless_shell --screenshot`) untuk pemeriksaan visual di Linux.

### Paket uji (24 Sep 2026)

- `tools\pack-test-msix.ps1` membuat MSIX Release x64 (258 MB, termasuk LibreOffice) di `dist\`, ditandatangani sertifikat self-signed `CN=Condec` (FriendlyName "Condec test signing", di `Cert:\CurrentUser\My`, kunci privat tidak diekspor). Bagian publiknya diekspor ke `dist\Condec-uji.cer`. Pemasangan: `Import-Certificate` ke `Cert:\LocalMachine\TrustedPeople` (admin, dilakukan pemilik), lalu `Add-AppxPackage`. **MSIX tanpa tanda tangan (`-AllowUnsigned` + OID publisher) ditolak** dengan 0x80073D2B: "an unsigned package cannot include Executable activations", walaupun Microsoft Learn hanya menyebut perlu admin.
- Paket ini butuh framework `Microsoft.WindowsAppRuntime.2` 2.5.1 yang sudah terpasang di sistem.

## 4. Keputusan yang sudah diambil

- Tetap tiga proyek. Kode WinRT (konverter gambar/media/PDF-render, runner LibreOffice) ditaruh di `src/Condec.Core/Platform/Windows/`, jadi hanya ikut di target Windows. Tes memakai target `net10.0`.
- `ChunkHashingStream` sekuensial (`CanSeek=false`). API yang butuh seek (BitmapEncoder, MediaTranscoder, DwgWriter, LibreOffice) menulis dulu ke staging, lalu disalin sekuensial ke `Output`.
- Tmp yang aktif dicatat di jurnal di `%LOCALAPPDATA%\Condec\`, lalu dibersihkan saat aplikasi mulai (untuk kasus crash).
- Semua PDF terenkripsi ditolak, termasuk yang hanya punya owner password.
- DXF dan DWG hasil PDF memakai AC1015 (R2000), ASCII untuk DXF. Keduanya lolos baca ulang ACadSharp dan impor LibreOffice.
- `PublishTrimmed=False` sampai bisa diuji.
- Manifest MSIX tanpa capability `internetClient`.
- Ikon aplikasi dirender dari SVG ikon di mockup (kotak biru membulat + dua panah), tanpa kata "DWG".
- **Keputusan pemilik (25 Sep 2026):** additional permission GPLv3 §7 untuk linking ke Windows App SDK, WebView2, WinRT, .NET runtime, dan komponen Windows lain **ditambahkan** (`LICENSE-ADDITIONAL-PERMISSION.md`). Pemegang hak cipta ditulis sebagai "Condec contributors". Header sumber memakai dua baris komentar SPDX (`GPL-3.0-or-later`) di `.cs` dan `.ps1`; berkas XAML sengaja tidak diberi header karena belum bisa dikompilasi di sesi cloud.
- **Perlu dicek di sesi Windows berikutnya:** (1) nama persis berkas lisensi LibreOffice di root `third_party\libreoffice` (diasumsikan `license.txt` dan `LICENSE.html`) dan pastikan `trim-libreoffice.ps1` tidak membuangnya; sesuaikan `THIRD-PARTY-NOTICES.md` §1 kalau berbeda; (2) `tools/*.ps1` masih jalan dengan dua baris komentar SPDX di atas blok comment-based help; (3) `Microsoft.Windows.SDK.NET.dll` dan `WinRT.Runtime.dll` memang ada di output paket (klaim di THIRD-PARTY-NOTICES).
- **Core (tahap 2):**
  - Namespace `Condec.Core.Conversion` (kontrak, registry), `.Pipeline`, `.History`, dan `.Formats`. Core melaporkan progres terstruktur (tahap, fraksi, nomor chunk); teks seperti "Chunk n dari N" dirangkai di UI.
  - File sementara bernama `<nama tujuan lengkap>.condec-tmp`, misalnya `laporan.pdf.condec-tmp`. Kalau nama itu sudah dipakai file lain, dipakai `laporan.pdf.2.condec-tmp`, dan file lama tidak disentuh.
  - Target tanpa `IOutputValidator` ditolak. Output yang tidak bisa diverifikasi tidak pernah disimpan.
  - Jurnal: satu file `*.pending` per konversi di `%LOCALAPPDATA%\Condec\journal`, dibuka tanpa berbagi selama konversi berjalan. `CleanupStale()` melewati entri yang masih terkunci (instance lain yang sedang jalan) dan hanya menghapus path berakhiran `.condec-tmp`.
  - VerifyIntegrity: SHA-256 seluruh file di paruh pertama, validator di paruh kedua.
  - Riwayat menyimpan status `Verified`/`Failed` dan flag aktif/mati di `history.json`. File yang rusak dibaca sebagai riwayat kosong.
  - Format waktu: "Baru saja" (di bawah 1 menit), "Hari ini, 08.05" (tidak ada di mockup, ditambahkan untuk hari yang sama), "Kemarin, 19.40", "22 Sep, 15.12", dan "31 Des 2025, 23.59" untuk tahun lain.
  - Ukuran memakai 1 KB = 1024 byte seperti File Explorer, satu desimal di bawah 100 ("2,4 MB"), bilangan bulat di atasnya ("245 MB").
- Masih perlu diverifikasi nanti: virtualisasi AppData pada aplikasi MSIX (tahap 2), filter Markdown LibreOffice (tahap 5), API `CodecQuery`/`MediaEncodingProfile` (tahap 4).

## 5. Ringkasan desain (dari Condec.html)

Referensi presisi ada di `docs/design/*.html`. Pemetaan warna → ThemeResource ada di `docs/PLAN.md` §6. **Tidak boleh ada hex di XAML.**

**Kerangka (1120×780)**
- Title bar 32px: padding kiri 16, ikon 16px, jarak 16, teks "Condec" caption.
- Area konten: padding 24 atas, 40 kiri-kanan, 32 bawah. Jarak antar-bagian 20. Kolom 880.
- Judul "Konversi file" (Title, 28/36 semibold). Subjudul warna sekunder, jarak 4.

**Kartu konverter** (padding 24, radius 8, border 1)
- *Input:* baris dengan jarak 16: [panel file flex 1, tinggi 168] [ikon panah → 24px] [panel format flex 1, tinggi 168, padding 24]. Panel dalam memakai radius 8 dan latar lapisan halus.
  - Belum ada file: border putus-putus, ikon unggah 32px, "Seret file ke sini", tombol "Pilih file…".
  - Ada file: ikon file 40px, caption "File sumber", nama (semibold, ellipsis), caption "Dokumen Word · 2,4 MB", tombol teks aksen "Ganti".
  - Panel format: label "Ubah ke format", ComboBox (disabled "Pilih file dulu"; placeholder "Pilih format"). Item menampilkan nama di kiri dan ekstensi caption di kanan (mis. "PDF" … ".pdf"). Helper caption: "6 format tersedia untuk .docx" / "Format muncul setelah file dipilih".
  - Footer: ikon perisai 16 + caption "Diproses sepenuhnya di perangkat ini. Tidak ada file yang diunggah." Di kanan tombol aksen "Konversi dan simpan…" (disabled sampai file dan format dipilih).
- *Proses:* judul "Mengonversi <nama>" (Subtitle 20/28), caption "Ke <FORMAT> · disimpan sebagai <path>", tombol "Batal" di kanan. ProgressBar + teks persen (lebar 40, rata kanan, angka tabular).
  - Daftar 4 langkah, tiap baris tinggi 40 dengan jarak 12: ikon 20px (selesai = lingkaran aksen penuh + centang; aktif = cincin berputar; menunggu = lingkaran garis), label (aktif semibold; menunggu warna sekunder), detail caption di kanan.
  - Mendekode file sumber: aktif "Membaca <nama>"
  - Menulis ke format <X>: aktif "Encode ke .<ext>"
  - Verifikasi chunk: aktif "Chunk n dari N", selesai "N chunk cocok"
  - Cek integritas file: aktif "Menghitung SHA-256", selesai "Hash cocok"
  - Detail default: selesai "Selesai", belum mulai "Menunggu".
- *Selesai:* InfoBar Success "Konversi selesai" + "File berhasil disimpan dan lolos verifikasi chunk serta cek integritas."
  - Baris detail dengan label lebar 96 (warna sekunder): "Lokasi", "Format" ("DOCX → PDF"), "Integritas" ("SHA-256 cocok · 24 chunk terverifikasi").
  - Tombol dengan jarak 8: "Buka file" (aksen), "Tampilkan di folder", "Konversi file lain" (tombol teks aksen).

**Varian PDF → DXF/DWG** (kartu input)
- Panel file dan panel format tinggi 120 (padding 20/24). Format "DXF (gambar CAD)". Meta "Dokumen PDF · 3 halaman · 1,1 MB".
- InfoBar Success "PDF vektor terdeteksi" — "Garis, busur, dan teks akan diubah menjadi objek CAD."
- InfoBar Warning "PDF hasil scan terdeteksi" — "Isinya gambar piksel, bukan garis. Hasil DXF berupa jejak garis perkiraan dan tidak presisi untuk ukuran." + tombol "Pilih file lain".
- Grid 3 kolom (jarak 16): "Satuan gambar" (Milimeter (mm)), "Skala gambar" (1 : 100), "Halaman" (Halaman 1 dari 3).
- Toggle (jarak 32), default keduanya aktif:
  - "Pertahankan teks sebagai teks" — "Disimpan sebagai entitas TEXT, bukan garis". Untuk scan: disabled, dengan catatan "Tidak tersedia untuk PDF hasil scan".
  - "Gabungkan garis bersambung" — "Menjadi satu polyline".
- Tombol utama: "Konversi dan simpan…". Untuk scan: "Tetap konversi…".

**Kartu Riwayat** (mengisi sisa tinggi)
- Header (padding 12/16/8/24): "Riwayat" (BodyStrong) + tombol teks "Hapus riwayat".
- ListView scroll. Item: padding 8/8/8/16, jarak 12, radius 4.
  - Kotak ikon 32×32 dengan ikon file 16.
  - Nama (ellipsis) + caption "DOCX → PDF · Kemarin, 19.40".
  - Status "✓ Terverifikasi" (warna success).
  - Tombol ikon folder 32×32 dengan `AutomationProperties.Name="Tampilkan di folder"`.
- Kosong: "Belum ada riwayat konversi."
- Format waktu: "Baru saja", "Kemarin, 19.40", "22 Sep, 15.12". Ukuran: "2,4 MB" (koma desimal).

## 6. Catatan lingkungan

- Sesi pertama berjalan di server Linux dengan folder yang di-mount ke Nextcloud. File yang ditulis langsung di disk server tidak terlihat oleh client Nextcloud sampai ada rescan. Itu sebabnya pengerjaan pindah ke mesin lokal.
- Di Windows, sebaiknya kerjakan proyek di folder lokal yang **tidak** disinkron Nextcloud, supaya `bin/obj` tidak ikut terunggah dan tidak bentrok dengan salinan di server.
- Prasyarat Windows: .NET 10 SDK dan Windows 10 1809+. Untuk menjalankan atau men-deploy MSIX dari IDE, pakai Visual Studio dengan workload WinUI/Windows App SDK, dan aktifkan Developer Mode.
- **Sesi cloud Claude Code (Linux, Ubuntu 24.04), 25 Sep 2026.** Repo GitHub `FathirAfza/Condec-conveter-`. Yang bisa dikerjakan hanya `Condec.Core` dan test portabel; aplikasi WinUI, test Windows, MSIX, dan LibreOffice tidak bisa dijalankan. Detail dan prompt pembukanya ada di `docs/CLOUD-SESSION-PROMPT.md`.
  - .NET 10 SDK dipasang dari repositori Ubuntu (`apt-get install dotnet-sdk-10.0`, versi 10.0.112; `global.json` `rollForward: latestFeature` menerimanya). `builds.dotnet.microsoft.com`, `dot.net`, dan `aka.ms` diblokir proxy kontainer, jadi `dotnet-install.sh` tidak bisa dipakai. `api.nuget.org` dan `packages.microsoft.com` bisa diakses.
  - Target Windows `Condec.Core` ikut terkompilasi di Linux (reference assembly `Microsoft.Windows.SDK.NET.dll` 10.0.26100.57 ada di cache NuGet), jadi keberadaan API WinRT bisa dicek saat compile. Kodenya tetap tidak bisa dijalankan di sini.
  - Output build di Linux ada di `~/.cache/condec-artifacts` (`ArtifactsPath` di `Directory.Build.props`), bukan `bin/obj`.
