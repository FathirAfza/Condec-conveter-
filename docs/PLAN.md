# Rencana kerja Condec

Dokumen ini ditulis sebelum kode. Semua versi dan API di bawah sudah dicek ke sumber resmi (NuGet, Microsoft Learn, README GitHub) pada 24 September 2026.

## 1. Temuan lingkungan

- Mesin pengembangan ini Linux. .NET 10 SDK 10.0.401 (LTS) dipasang di `~/.dotnet`.
- Proyek WinUI 3 **tidak bisa di-build di Linux**. `dotnet build` menjalankan `tools/net472/XamlCompiler.exe` dari paket `Microsoft.WindowsAppSDK.WinUI` (lihat `Microsoft.UI.Xaml.Markup.Compiler.interop.targets`, `UseXamlCompilerExecutable=true` saat `MSBuildRuntimeType=Core`). Executable itu .NET Framework, dan pembuatan XBF, PRI, serta MSIX memakai tool native Windows.
- Akibatnya:
  - `Condec.Core` dan `Condec.Tests` di-build dan dites di sini.
  - Kode Windows di `Condec.Core` (target `net10.0-windows10.0.26100.0`) tetap dikompilasi di sini dengan `EnableWindowsTargeting=true`, karena hanya butuh reference assembly WinRT.
  - Proyek aplikasi `Condec` (XAML) harus di-build di Windows.

## 2. Versi yang dipakai

| Komponen | Versi | Lisensi | Sumber |
|---|---|---|---|
| .NET | 10.0 (SDK 10.0.401), LTS sampai 14 Nov 2028 | MIT | releases-index.json |
| Windows App SDK | 2.5.1 (Stable, rilis 16 Sep 2026) | Microsoft (proprietary) | learn.microsoft.com release channels |
| TFM aplikasi | `net10.0-windows10.0.26100.0`, minimum `10.0.17763.0` | – | BundledVersions .NET SDK |
| CommunityToolkit.Mvvm | 8.4.2 | MIT | NuGet |
| PdfPig | 0.1.16 | Apache-2.0 | NuGet |
| ACadSharp | 3.8.0 | MIT | NuGet, README |
| xunit.v3.mtp-v2 | 4.0.1 | Apache-2.0 | template resmi `xunit3` |

Windows App SDK direferensikan lewat paket komponen `Microsoft.WindowsAppSDK.WinUI` dan `Microsoft.WindowsAppSDK.Runtime` (mode framework-dependent, didukung sejak 1.8). Metapaket `Microsoft.WindowsAppSDK` ikut menarik paket AI, ML, Search, dan Widgets yang tidak dipakai. Kalau build di Windows bermasalah, fallback-nya kembali ke metapaket.

## 3. Struktur solusi

```
Condec.sln
global.json                 SDK 10.0.401 + runner Microsoft.Testing.Platform
Directory.Build.props       pengaturan bersama
Directory.Packages.props    versi paket terpusat
src/Condec/                 aplikasi WinUI 3 (MSIX)
src/Condec.Core/            multi-target: net10.0 dan net10.0-windows10.0.26100.0
tests/Condec.Tests/         xUnit v3, net10.0
docs/PLAN.md
```

`Condec.Core` memakai multi-target supaya tetap tiga proyek:
- `net10.0`: kontrak, registry, pipeline, verifikasi, riwayat, konverter PDF → DXF/DWG (PdfPig + ACadSharp), algoritma vektorisasi scan, validator PDF/DXF/DWG. Semua ini bisa dites di OS apa pun.
- `net10.0-windows…`: tambahan file di `Platform/Windows/`, yaitu konverter gambar (`Windows.Graphics.Imaging`), audio/video (`MediaTranscoder`), render PDF (`Windows.Data.Pdf`), runner LibreOffice, dan validator gambar/media.

Aplikasi memakai target Windows, sedangkan tes memakai target `net10.0`.

## 4. Arsitektur inti

### Kontrak
```csharp
public interface IConverter
{
    IReadOnlyList<string> GetTargets(string sourceExtension);
    Task ConvertAsync(ConversionRequest request, IProgress<ConversionProgress> progress, CancellationToken ct);
}
```
- `ConversionRequest`: `SourcePath`, `SourceExtension`, `TargetExtension`, `Options` (misalnya `CadOptions`), dan `Output` (stream tulis sekuensial milik pipeline).
- `ConversionProgress`: `Stage` (Decode/Encode) + `Fraction` 0..1 + `Detail` opsional. Pipeline yang memetakan ke persen global.
- `IOutputValidator`: membuka ulang output per format tujuan (tahap VerifyIntegrity).
- `IExternalToolConverter` (opsional): melaporkan prasyarat eksternal. Hanya dipakai konverter LibreOffice, supaya format dokumen bisa tampil **disabled** dengan InfoBar. Format yang codec-nya tidak ada tidak dikembalikan oleh `GetTargets` sama sekali, jadi tidak muncul.

### ConverterRegistry
Mengumpulkan semua `IConverter`. `GetTargetOptions(ext)` mengembalikan `TargetOption(Extension, DisplayName, IsEnabled, DisabledReason)`. UI hanya membaca dari sini. Katalog `FormatCatalog` menyimpan nama tampilan berbahasa Indonesia ("Dokumen Word", "DXF (gambar CAD)").

### Pipeline 4 tahap
| Tahap | Bobot | Isi |
|---|---|---|
| Decode | 0–35% | converter membaca/mendekode sumber |
| Encode | 35–70% | converter menulis ke `Output` → `<nama>.condec-tmp` di folder tujuan |
| VerifyChunks | 70–90% | baca ulang dari disk per 1 MiB, bandingkan SHA-256 per chunk; lapor "Chunk n dari N" |
| VerifyIntegrity | 90–100% | SHA-256 seluruh file vs hash saat menulis, lalu `IOutputValidator` membuka ulang file |

Detail penting:
- `ChunkHashingStream` membungkus `FileStream` tujuan. Stream ini **sekuensial** (`CanSeek=false`) dan menghitung SHA-256 per chunk 1 MiB plus SHA-256 seluruh file selama penulisan.
- Beberapa API native butuh stream yang bisa di-seek: `BitmapEncoder`, `MediaTranscoder` (MP4 menulis ulang header), `DwgWriter`, dan LibreOffice yang menulis ke folder. Konverter seperti ini menulis dulu ke staging (memori atau folder temp aplikasi), lalu menyalinnya secara sekuensial ke `Output` sebagai bagian akhir Encode. Dengan begitu hash "saat menulis" tetap jujur.
- Sebelum verifikasi, file di-flush dengan `Flush(flushToDisk: true)`. Keterbatasannya: pembacaan ulang bisa dilayani cache OS. Ini akan dicatat di README.
- Sukses: `File.Move(tmp, final, overwrite: true)` (rename di volume yang sama).
- Gagal/batal: tmp dihapus di blok `finally`. Path tmp yang sedang aktif juga dicatat di jurnal kecil di `%LOCALAPPDATA%\Condec\`, lalu dibersihkan saat aplikasi mulai. Ini menangani kasus crash atau mati listrik.
- `CancellationToken` diperiksa di setiap tahap dan setiap chunk.

### Riwayat
`HistoryStore` menulis `%LOCALAPPDATA%\Condec\history.json` secara atomik (tulis ke tmp lalu rename). Isinya: nama file, format asal → tujuan, waktu, path output, dan status verifikasi. Tidak ada isi file. Ada opsi mematikan riwayat dan tombol "Hapus riwayat". Catatan: pada aplikasi MSIX, penulisan ke AppData diarahkan Windows ke folder privat paket. Ini akan diverifikasi di tahap 2.

### Format waktu dan ukuran
Diformat eksplisit tanpa bergantung pada ICU: "Baru saja", "Kemarin, 19.40", "22 Sep, 15.12", "2,4 MB". Ada unit test-nya.

## 5. Konverter per tahap

1. **Gambar**: JPG, PNG, BMP, GIF, TIFF saling konversi. HEIC dan WebP hanya sebagai sumber, dan hanya jika `BitmapDecoder.GetDecoderInformationEnumerator()` melaporkan decoder-nya ada. Validasi: buka ulang dengan `BitmapDecoder`.
2. **Audio/video**: profil `MediaEncodingProfile` bawaan (MP4, M4A, MP3, WAV, WMA, WMV, FLAC, ALAC, HEVC, dll.). Hanya profil yang encoder-nya ditemukan lewat `CodecQuery` yang ditampilkan. Detail API dicek di tahap 4.
3. **Dokumen**: deteksi `soffice.exe` lewat registry dan path standar. Jalankan `soffice --headless --norestore --convert-to <fmt> --outdir <tmp>` dengan profil pengguna sementara (`-env:UserInstallation`). Profil itu juga mematikan pengecekan update LibreOffice. Target Markdown hanya ditampilkan jika filter-nya terverifikasi ada di versi LibreOffice terpasang.
4. **PDF → PNG/JPG**: `Windows.Data.Pdf`, halaman dipilih lewat ComboBox "Halaman".
5. **PDF → DXF/DWG**:
   - Baca `Page.Paths` (PdfPig): `PdfSubpath.Line`, `CubicBezierCurve`, `Close`. Path clipping dilewati.
   - Bézier yang cocok dengan busur lingkaran (dalam toleransi) menjadi ARC, sisanya SPLINE derajat 3. Garis bersambung menjadi LWPOLYLINE jika "Gabungkan garis bersambung" aktif, jika tidak menjadi LINE.
   - Teks (`GetWords`) menjadi TEXT jika opsi aktif.
   - Koordinat: pt × (1/72) inci → satuan tujuan × penyebut skala. `$INSUNITS` diisi sesuai satuan.
   - Versi default: DXF ASCII AC1015 (R2000), karena paling luas dibaca. DWG dari daftar tulis ACadSharp (AC1014/1015/1018/1024/1027/1032). Pilihan final setelah uji baca ulang di tahap 6.
   - Deteksi scan: jumlah segmen path di bawah ambang **dan** satu gambar raster menutupi ≥ 70% area halaman.
   - Vektorisasi scan: render (Windows.Data.Pdf) → grayscale → binarisasi Otsu → marching squares → Ramer–Douglas–Peucker → LWPOLYLINE. Algoritmanya murni C# di Core dan punya unit test.

**PDF terenkripsi** (termasuk yang hanya punya owner password/pembatasan izin) langsung ditolak dengan pesan jelas. Tidak ada upaya membuka proteksi.

## 6. UI (tahap 3)

Mengikuti `Condec.html` (6 layar). Semua warna memakai ThemeResource:

| Mockup | ThemeResource |
|---|---|
| latar #F3F3F3 | `MicaBackdrop` |
| kartu #FBFBFB / garis #E5E5E5 | `CardBackgroundFillColorDefaultBrush` / `CardStrokeColorDefaultBrush` |
| teks #1B1B1B / #616161 | `TextFillColorPrimaryBrush` / `TextFillColorSecondaryBrush` |
| aksen #005FB8 | tombol `AccentButtonStyle`, `AccentFillColorDefaultBrush` |
| garis putus-putus #8A8A8A | `ControlStrongStrokeColorDefaultBrush` (Rectangle + `StrokeDashArray`) |
| hijau #0F7B0F | `SystemFillColorSuccessBrush` |
| InfoBar hijau/kuning | `InfoBar` Severity Success/Warning bawaan |

- Title bar kustom 32px (`ExtendsContentIntoTitleBar` + `SetTitleBar`), ikon 16px, judul "Condec" gaya caption.
- Ukuran 1120×780 (dikalikan skala DPI), kolom konten maks 880px di tengah.
- FileSavePicker: `Microsoft.Windows.Storage.Pickers.FileSavePicker` dari Windows App SDK, diinisialisasi dengan `WindowId` dari HWND jendela (`Win32Interop.GetWindowIdFromWindow`). Sejak 2.0, picker ini tidak membuat file kosong, jadi cocok dengan skema file sementara.
- MVVM: `MainViewModel` dengan state `Input`/`Processing`/`Done`, `[ObservableProperty]`, dan `[RelayCommand]`.
- Aksesibilitas: `AutomationProperties.Name` di semua tombol ikon, urutan tab logis, dan area drop bisa difokus.

## 7. Privasi

- Tidak ada kode jaringan di Condec. Ada unit test yang memastikan `Condec.Core` tidak mereferensikan assembly `System.Net.*`.
- Manifest MSIX tidak mendeklarasikan capability `internetClient`.
- Build CLI dijalankan dengan `DOTNET_CLI_TELEMETRY_OPTOUT=1`. Ini hanya soal alat pengembangan, bukan aplikasi.

## 8. Lisensi

GPL-3.0-or-later. Semua dependensi pihak ketiga (MIT, Apache-2.0) kompatibel dengan GPLv3. Satu catatan: Windows App SDK berlisensi proprietary dan dimuat sebagai framework package milik sistem. Disarankan menambah *additional permission* (GPLv3 §7) yang mengizinkan linking ke Windows App SDK dan komponen Windows. Ini keputusan pemilik proyek dan akan ditanyakan di tahap 7.

## 9. Urutan kerja

1. Rencana + scaffold. Build Core/Tests di Linux, build app di Windows.
2. Core: kontrak, registry, pipeline, verifikasi, file sementara atomik, riwayat + tes.
3. UI tiga keadaan + konverter gambar.
4. Audio/video.
5. Dokumen via LibreOffice.
6. PDF → gambar, PDF → DXF/DWG, deteksi dan vektorisasi scan.
7. README, LICENSE, THIRD-PARTY-NOTICES.md.
