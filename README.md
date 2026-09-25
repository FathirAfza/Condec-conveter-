# Condec

Condec adalah aplikasi konverter file untuk Windows: gambar, dokumen, PDF, dan gambar CAD, semuanya diproses di perangkat Anda sendiri. Tidak ada file yang diunggah, tidak ada koneksi internet yang dipakai, dan tidak ada telemetri.

Dibangun dengan WinUI 3 (Windows App SDK), .NET 10, dan dipaketkan sebagai MSIX. Lisensi: GPL-3.0-or-later.

## Fitur utama

- **Verifikasi hasil.** Setiap konversi melewati empat tahap: dekode, encode, verifikasi chunk (SHA-256 per 1 MiB yang dibaca ulang dari disk), dan cek integritas (SHA-256 seluruh file lalu file dibuka ulang dengan decoder yang sesuai). File hanya disimpan kalau semua tahap lulus, jadi tidak pernah ada file setengah jadi.
- **Riwayat lokal.** Nama file, format, waktu, lokasi output, dan status verifikasi disimpan di `%LOCALAPPDATA%\Condec\history.json`. Tidak ada salinan isi file. Riwayat bisa dihapus atau dimatikan.
- **PDF → DXF/DWG.** Garis, busur, lingkaran, kurva, dan teks PDF vektor menjadi objek CAD yang bisa disunting, dengan pilihan satuan, skala, dan halaman. PDF hasil scan ditelusuri menjadi polyline.
- **Tampilan Fluent.** Mengikuti tema terang, gelap, dan kontras tinggi Windows 11.

## Format yang didukung

Daftar format di aplikasi diambil dari mesin pengguna secara dinamis. Format yang codec-nya tidak ada tidak ditampilkan.

| Sumber | Tujuan | Mesin |
|---|---|---|
| JPG, PNG, BMP, GIF, TIFF | JPG, PNG, BMP, GIF, TIFF, HEIC¹ | Windows Imaging Component (`Windows.Graphics.Imaging`) |
| HEIC, HEIF, WebP² | JPG, PNG, BMP, GIF, TIFF | Windows Imaging Component |
| PDF (satu halaman) | JPG, PNG, BMP, GIF, TIFF, HEIC¹ | `Windows.Data.Pdf` |
| PDF (satu halaman) | DXF, DWG (AutoCAD 2000) | PdfPig + ACadSharp |
| PDF | DOCX, DOC, ODT, PPTX, PPT, ODP | LibreOffice (impor PDF ke Writer atau Impress) |
| DOCX, DOC, ODT, RTF, TXT | PDF, DOCX, DOC, ODT, RTF | LibreOffice |
| XLSX, XLS, ODS | PDF, XLSX, XLS, ODS | LibreOffice |
| PPTX, PPT, ODP | PDF, PPTX, PPT, ODP | LibreOffice |
| DXF, DWG | PDF | ACadSharp (DWG → DXF) lalu LibreOffice Draw |
| DXF ↔ DWG | DWG, DXF | ACadSharp |

¹ HEIC hanya ditawarkan kalau codec HEVC terpasang dan terbukti bisa dipakai.
² Hanya kalau decoder HEIF atau WebP terpasang di Windows (ekstensi dari Microsoft Store).

Konversi audio/video (`Windows.Media.Transcoding`) sedang dikerjakan.

LibreOffice dibawa di dalam paket Condec x64, jadi tidak perlu memasang aplikasi lain. Kalau LibreOffice sudah terpasang di sistem, versi itulah yang dipakai.

### PDF → DXF/DWG

- Garis menjadi LINE atau LWPOLYLINE (garis bersambung digabung, juga kalau digambar terpisah di PDF). Bézier yang membentuk lingkaran menjadi ARC atau CIRCLE, kurva lain menjadi satu SPLINE per rangkaian.
- Isian padat yang dipecah produser PDF menjadi segitiga digabung kembali menjadi satu outline tertutup, sehingga tidak ada diagonal palsu. Cat putih di atas kertas (latar, penutup) dibuang.
- Teks menjadi TEXT per baris, bukan per kata. Teks tersembunyi (lapisan OCR) dibuang, kecuali yang menjadi pasangan huruf yang digambar sebagai outline; outline-nya diganti TEXT yang bisa disunting.
- Geometri di luar halaman dan di luar jendela clipping PDF (viewport) dipotong, sehingga objek yang tidak terlihat di viewer tidak ikut masuk.
- Warna PDF dipetakan ke indeks warna AutoCAD terdekat.
- Satuan: 1 point PDF = 1/72 inci, dikali skala yang dipilih. `$INSUNITS` diisi sesuai satuan.
- PDF terenkripsi (termasuk yang hanya dibatasi izinnya) ditolak. Condec tidak membuka proteksi apa pun.

## Privasi

- Tidak ada kode jaringan di Condec. Ada unit test yang memastikan `Condec.Core` tidak mereferensikan assembly `System.Net.*`.
- Manifest MSIX tidak mendeklarasikan capability `internetClient`.
- LibreOffice yang dibawa dijalankan dalam mode headless dengan pengecekan update dimatikan.
- Keterbatasan verifikasi: pembacaan ulang file untuk verifikasi chunk bisa dilayani cache sistem operasi, jadi verifikasi ini menangkap kesalahan tulis di sisi aplikasi, bukan kerusakan media penyimpanan.

## Unduh dan pasang

Paket MSIX ada di halaman [Releases](../../releases). Versi pra-rilis ditandatangani sertifikat uji, jadi sekali saja sertifikat `Condec.cer` perlu diimpor dari PowerShell administrator:

```powershell
Import-Certificate -FilePath .\Condec.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
Add-AppxPackage -Path .\Condec_<versi>_x64.msix
```

Prasyarat: Windows 10 1809 atau lebih baru, 64-bit, dan Windows App Runtime 2.5 (App Installer biasanya mengunduhnya sendiri). Rilis dibuat oleh workflow `.github/workflows/release.yml` di runner Windows setiap kali tag `v*` dipush: build, semua test, unduh dan pangkas LibreOffice, pack, tanda tangan, lalu unggah `.msix`, `.cer`, dan `SHA256SUMS.txt`.

## Cara build

Prasyarat di Windows 10 1809 atau lebih baru:

- .NET 10 SDK.
- Windows App Runtime 2.5 (`Microsoft.WindowsAppRuntime.2`) untuk menjalankan aplikasi. Untuk men-deploy dari IDE, Visual Studio dengan workload WinUI/Windows App SDK dan Developer Mode aktif.
- PowerShell untuk skrip di `tools\`.

```powershell
# Sekali: mengunduh MSI LibreOffice resmi (SHA-256 dipin), mengekstraknya tanpa memasang,
# dan memangkasnya ke third_party\libreoffice (diabaikan git).
tools\fetch-libreoffice.ps1

dotnet build Condec.sln
dotnet test --solution Condec.sln

# MSIX Release x64 yang ditandatangani sertifikat uji, di dist\
tools\pack-test-msix.ps1
```

Jalankan test dari PowerShell dengan `--solution` atau `--project`; runner Microsoft.Testing.Platform menganggap argumen posisi sebagai argumen untuk test host.

Di Linux atau macOS hanya `Condec.Core` dan test lintas-platform yang bisa di-build:

```sh
dotnet build Condec.Portable.slnf
dotnet test --solution Condec.Portable.slnf
```

Output build di mesin non-Windows diarahkan ke `~/.cache/condec-artifacts`.

## Struktur

```
src/Condec/          aplikasi WinUI 3 (MVVM dengan CommunityToolkit.Mvvm), MSIX
src/Condec.Core/     kontrak konverter, pipeline 4 tahap, verifikasi, riwayat, PDF → CAD
                     Platform/Windows/: konverter yang memakai API WinRT dan LibreOffice
tests/Condec.Tests/  xUnit v3; Windows/ hanya jalan di Windows
tools/               skrip PowerShell untuk LibreOffice dan MSIX
docs/                spesifikasi, rencana, dan catatan serah terima
```

## Lisensi

Condec berlisensi [GNU GPL versi 3 atau yang lebih baru](LICENSE), dengan [izin tambahan](LICENSE-ADDITIONAL-PERMISSION.md) (GPLv3 pasal 7) untuk menggabungkannya dengan Windows App SDK dan komponen Windows lain. Daftar dependensi beserta lisensinya ada di [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Copyright (C) 2026 Condec contributors
