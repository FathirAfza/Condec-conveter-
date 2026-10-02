# Prompt untuk sesi Claude Code lokal (Windows)

Salin semua teks di bawah garis ke sesi baru Claude Code yang dibuka di root folder proyek Condec (clone lokal dari `FathirAfza/Condec-conveter-`). Untuk sesi cloud (Linux), pakai `docs/CLOUD-SESSION-PROMPT.md`; sesi Linux tidak bisa mem-build aplikasi WinUI.

Terakhir diperbarui 2 Okt 2026, setelah tahap 2 redesign (commit `9587e71`).

---

Kita melanjutkan proyek **Condec**: konverter file Windows 100% offline (WinUI 3, Windows App SDK 2.5.1, .NET 10, MSIX, GPL-3.0-or-later). Saya Afza, pemilik proyek, dan berbahasa Indonesia: balas saya dalam bahasa Indonesia, kode dan identifier dalam bahasa Inggris. Sesi sebelumnya berjalan di cloud (Linux) yang tidak bisa mem-build WinUI; sekarang kamu berjalan di Windows, jadi kamu bisa mem-build dan menjalankan aplikasinya.

## Langkah pertama

```powershell
git fetch origin
git checkout claude/kind-mayer-9uxymj
git pull origin claude/kind-mayer-9uxymj
dotnet --list-sdks   # harus ada .NET 10; kalau tidak ada, beri tahu saya, jangan memasang apa pun tanpa izin
dotnet build Condec.sln
dotnet test --solution Condec.sln
```

Pakai `--solution` (atau `--project`) untuk `dotnet test`; runner Microsoft.Testing.Platform menganggap argumen posisional sebagai argumen test host. Test yang butuh LibreOffice memakai `third_party\libreoffice`; kalau belum ada, jalankan `tools\fetch-libreoffice.ps1` sekali. Hasil terakhir di Windows CI (run #10): 681 lulus, 0 gagal, 2 dilewati (HEIC tidak bisa ditulis di runner).

## Baca berurutan sebelum mengubah apa pun

1. `CLAUDE.md`: aturan singkat proyek.
2. `DESIGN.md` (versi 0.2.1): sumber kebenaran tampilan dan perilaku redesign. Perubahan UI atau perilaku wajib mengubah `DESIGN.md` **dan** menambah entri di §14 pada commit yang sama.
3. `docs/HANDOFF.md`: status, fakta yang sudah diverifikasi, keputusan, catatan lingkungan. Entri "Redesign (mulai 2 Okt 2026)" paling relevan.
4. `docs/SPEC.md` (aturan wajib dari saya) dan `docs/PLAN.md`.

## Aturan

- **Percakapan dengan saya menang atas `DESIGN.md` dan prompt awal redesign** bila bertentangan. Keputusan sudah diambil dan tercatat di `DESIGN.md` §14 [0.2.1]:
  - Bahasa UI mengikuti bahasa Windows: Indonesia dan Inggris, bahasa lain jatuh ke Inggris. Semua teks UI adalah resource (`src/Condec.Core/Resources/Strings.resx` Inggris, `Strings.id.resx` Indonesia); XAML memakai attached property `local:L.Text/Content/Header/PlaceholderText/Title/Message/AutomationName/ToolTip="Kunci"` (`src/Condec/L.cs`). Teks di `DESIGN.md` adalah versi Indonesia; tambahkan kedua bahasa untuk setiap kunci baru (`LocalizationTests` memeriksa kunci dan placeholder).
  - LibreOffice tetap dibundel di paket x64.
  - HEIC tetap boleh jadi format tujuan bila codec HEVC terpasang.
  - Settings: `ApplicationData.Current.LocalSettings` bila terpasang sebagai MSIX, `%LOCALAPPDATA%\Condec\settings.json` (`JsonFileSettingsStore`) bila portable. Build portable harus tetap jalan (tanpa identitas paket).
  - Versi DWG yang ditulis: AC1015.
  - **Jangan membuat PR atau rilis** sampai saya selesai menguji paket uji dan meminta. Kerja tetap di branch `claude/kind-mayer-9uxymj`.
- Hanya kontrol stock WinUI 3 (tanpa CommunityToolkit.WinUI.Controls). Warna lewat `ThemeResource`, tanpa hex, kecuali tiga warna pratinjau di `DESIGN.md` §4.1.
- Jangan mengarang API. Cek dokumentasi resmi, metadata paket di `%USERPROFILE%\.nuget\packages`, atau header resmi sebelum memakai. Tag `[ASUMSI]`/`[TERBUKA]` di `DESIGN.md` bukan fakta.
- Offline: tanpa jaringan, telemetry, atau update checker. Tidak ada pembongkaran DRM atau password PDF.
- Tahap selesai kalau `dotnet build` dan `dotnet test` lulus. Setelah tiap tahap, lapor dalam format: (1) apa yang selesai, (2) hasil build dan test, (3) perubahan di `DESIGN.md`, (4) hal bertag `[ASUMSI]`/`[TERBUKA]` yang tersentuh dan keputusanmu, (5) pertanyaan untuk saya. Lalu **tunggu konfirmasi saya** sebelum tahap berikutnya.
- Jangan commit file milik saya (mis. DXF/PDF contoh). Jangan `git push --force`.

## Yang sudah selesai (semuanya sudah di-push)

- Rilis v0.1.0-beta.3 (perbaikan DXF R10 dan SEQEND) sudah terbit dan saya pasang.
- Di branch kerja, belum dirilis: bahasa mengikuti Windows (English + Indonesian), README berbahasa Inggris, PNG/JPG/HEIC dll. → DXF/DWG (DWG lewat DXF dulu), kinerja `ScanVectorizer`. Saya sudah mengujinya ("Good").
- Redesign tahap 1 (`DESIGN.md`, `CLAUDE.md`) dan tahap 2 (`Condec.Core`):
  - `Devices/DeviceProfile` + `IDeviceProbe`; `Platform/Windows/Devices/WindowsDeviceProbe` (RAM lewat `GetPhysicallyInstalledSystemMemory` dengan cadangan `GlobalMemoryStatusEx`, CPU dari registri, GPU dan NPU lewat DXCore `[GeneratedComInterface]` dari header resmi `dxcore_interface.h`).
  - `Devices/CapabilityPolicy`: aturan §7; 10 test vector §9 lulus (`CapabilityPolicyTests`).
  - `Upscale/UpscaleEstimator`, `Upscale/EngineBenchmark`, `Formats/ScaleText` (angka §2/§8).
  - `Settings/AppSettings`, `ISettingsStore`, `JsonFileSettingsStore`.
- Belum ada perubahan UI sama sekali: `MainWindow.xaml` masih jendela lama satu halaman.

## Yang belum terbukti (periksa di mesin ini)

- **DXCore** (GPU dan NPU) belum pernah jalan di mesin dengan GPU; CI tidak punya GPU. Setelah `AppSettings` dan probe terpasang di UI (tahap 3), cek bahwa nama GPU, VRAM, dan NPU terbaca benar di mesin ini, dan catat hasilnya di `DESIGN.md` §13 #18.
- `GlobalizationPreferences.Languages` untuk memilih bahasa: API sudah dicek di metadata, perilaku runtime belum. Cek dengan Windows berbahasa Indonesia dan Inggris.
- Tampilan XAML hasil attached property `L.*` di jendela nyata.

## Tugas sekarang: tahap 3, shell dan Settings (`DESIGN.md` §3, §4, §5, §6.4, §10, §11)

1. Verifikasi API sebelum dipakai: `TitleBar` (Microsoft.WinUI 2.3.9 di Windows App SDK 2.5.1; cek properti dan cara pakainya di dokumentasi/metadata), `NavigationView`, `SelectorBar`/`SelectorBarItem`, `InfoBar`, `Expander`, `RadioButtons`, `ToggleSwitch`, `Slider`, `MicaBackdrop`. Semuanya ada di `Microsoft.WinUI.dll`.
2. Bangun shell: `MainWindow` berisi `TitleBar` + `NavigationView` + `Frame` (jendela 1280 × 820, pane 280, mode Auto, item Convert File / Upscale Image / Architecture, Settings otomatis di bawah) dengan halaman di `Views/`: `ConvertPage` (isi dan perilaku jendela lama dipindahkan, bukan ditulis ulang: pipeline, riwayat, PDF options, converter yang ada tetap dipakai), `UpscalePage` dan `ArchitecturePage` (sementara kerangka dengan judul dan subjudul §3.3), `SettingsPage`.
3. `SettingsPage` sesuai §6.4: `SelectorBar` "Render dan performa" / "Umum"; kartu Perangkat ini (isi dari `WindowsDeviceProbe`: CPU, RAM, GPU, NPU, "Batas upscale perangkat"), Render mode (`Expander` + `RadioButtons`), Batas upscale, Batas memori, Render dump (cache), Tema (`RequestedTheme` di root), Latar Mica, Log, Tentang. Nilai lewat `AppSettings`; buat `ISettingsStore` untuk MSIX (`ApplicationData.Current.LocalSettings`) dan pilih antara itu dan `JsonFileSettingsStore` saat start (cek identitas paket dengan cara yang terdokumentasi, mis. `GetCurrentPackageFullName`/`AppModel.HasPackageIdentity`; verifikasi dulu).
4. Layar Convert File mengikuti §6.1. PDF → DXF/DWG pindah ke Architecture (Convert File hanya memuat tautan ke sana; sementara tautan menuju halaman kerangka, atau pertahankan alur lama lewat Architecture bila lebih aman, dan beri tahu saya).
5. Semua teks baru masuk resource Inggris dan Indonesia. Tambahkan test yang relevan; `dotnet build` dan `dotnet test` harus lulus. Jalankan aplikasinya, uji tema terang/gelap/high contrast, ubah ukuran jendela (pane Auto), dan uji Settings (tema, Mica, batas).
6. Perbarui `DESIGN.md` (+ entri §14) untuk setiap penyimpangan, `docs/HANDOFF.md` untuk fakta baru, lalu commit dan push ke `claude/kind-mayer-9uxymj`. Untuk paket uji yang bisa saya pasang tanpa mesin build: dispatch workflow "Rilis" pada branch ini (`.github/workflows/release.yml`, `workflow_dispatch`; hasilnya artifact `Condec-v<manifest>-dev.<run>`, tidak menerbitkan rilis). Jika sesi lokal tidak punya akses ke Actions, pakai `tools\pack-test-msix.ps1` dan `tools\publish-portable.ps1`.

Setelah tahap 3, lapor dengan format di atas dan **berhenti menunggu konfirmasi saya**. Urutan tahap berikutnya (hanya setelah konfirmasi): 4 Convert File + konverter gambar; 5 audio/video (`Windows.Media.Transcoding`), dokumen, PDF → gambar; 6 Upscale (ONNX Runtime CPU/DirectML/NPU; periksa lisensi model dan tunjukkan hasilnya ke saya sebelum dibundel; benchmark nyata); 7 Architecture (vektorisasi, OCR `Windows.Media.Ocr`, kalibrasi, layer WALLS/OPENINGS/TEXT, DWG → gambar/PDF); 8 README, LICENSE, THIRD-PARTY-NOTICES.
