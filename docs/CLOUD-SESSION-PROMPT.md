# Prompt untuk sesi Claude Code cloud (Linux)

Salin semua teks di bawah garis ke sesi Claude Code cloud yang dibuka di repo Condec. Untuk sesi di laptop Windows, pakai `docs/LOCAL-SESSION-PROMPT.md`.

---

Kita melanjutkan proyek **Condec**: aplikasi konverter file untuk Windows (WinUI 3, Windows App SDK, .NET 10, MSIX), open source dan 100% offline. Sesi ini berjalan di **kontainer Linux (Ubuntu 24.04)**, bukan di Windows. Batasannya dijelaskan di bawah, dan wajib diikuti.

**Sebelum melakukan apa pun, baca berurutan:**
1. `docs/SPEC.md`: spesifikasi dan aturan wajib dari saya. Aturan intinya: jangan mengarang API, tidak ada koneksi jaringan sama sekali di aplikasi, tidak ada pembongkaran DRM atau password PDF, identifier berbahasa Inggris, teks UI bahasa Indonesia dengan sentence case, dan warna hanya lewat ThemeResource.
2. `docs/PLAN.md`: rencana teknis yang sudah disetujui sebagai arah kerja.
3. `docs/HANDOFF.md`: status terakhir, fakta dan versi yang sudah diverifikasi, keputusan yang sudah diambil, dan ringkasan desain. Bagian §6 berisi catatan khusus lingkungan cloud.

Desain visual ada di `Condec.html` (bundel) dan `docs/design/*.html` (markup mentah yang lebih mudah dibaca).

## Apa yang bisa dan tidak bisa dikerjakan di sesi cloud

**Bisa:**
- Mem-build `Condec.Core` (kedua target: `net10.0` dan `net10.0-windows10.0.26100.0`) dan `Condec.Tests`, lewat `Condec.Portable.slnf`.
- Menjalankan test lintas-platform (target `net10.0`). Per 25 Sep 2026 jumlahnya 139 dan semuanya lulus.
- Mengompilasi kode di `src/Condec.Core/Platform/Windows/**` terhadap reference assembly WinRT (`Microsoft.Windows.SDK.NET.dll` dari paket `microsoft.windows.sdk.net.ref`). Ini berarti keberadaan tipe dan method WinRT bisa **diverifikasi saat compile**, tetapi kodenya **tidak bisa dijalankan**.
- Mengedit C#/XAML aplikasi `src/Condec/`, dokumen, dan skrip. Tanpa bisa mengompilasi XAML.
- Tahap 7 (README, LICENSE, THIRD-PARTY-NOTICES.md), karena hanya dokumen.

**Tidak bisa:**
- Mem-build atau menjalankan aplikasi WinUI `src/Condec/` (compiler XAML adalah executable .NET Framework, hanya jalan di Windows).
- Menjalankan test khusus Windows di `tests/Condec.Tests/Windows/` (konverter gambar, LibreOffice, PDF). Pada mesin Windows totalnya 323 test.
- Membuat atau memasang MSIX, menjalankan `tools/*.ps1`, mengunduh dan memangkas LibreOffice.
- Memverifikasi tampilan, tema, aksesibilitas, atau perilaku runtime WinRT (misalnya codec yang tersedia).

**Konsekuensinya untuk cara kerja:**
- Setiap perubahan pada kode Windows yang dibuat di sini berstatus **"terkompilasi, belum diuji"**. Tulis itu secara eksplisit di laporan dan di `docs/HANDOFF.md`, beserta daftar hal yang harus diverifikasi sesi Windows berikutnya.
- Jangan pernah melaporkan "semua test lulus" untuk tahap yang punya test Windows. Laporkan "139 test portabel lulus; test Windows belum dijalankan".
- Kalau sebuah tahap butuh eksperimen runtime (misalnya tahap 4: profil `MediaEncodingProfile` mana yang benar-benar didukung `CodecQuery`), kerjakan bagian yang bisa dikompilasi dan dites secara portabel, lalu serahkan sisanya ke sesi Windows lewat HANDOFF.
- Aturan kerja bertahap tetap berlaku: selesaikan satu tahap (atau bagian tahap yang mungkin di Linux), laporkan hasil build dan test dalam bahasa Indonesia, lalu **berhenti dan tunggu konfirmasi saya**.

## Menyiapkan lingkungan

1. Cek `dotnet --list-sdks`. Kalau .NET 10 belum ada, pasang lewat apt (repositori Ubuntu 24.04 menyediakan `dotnet-sdk-10.0`):
   ```
   apt-get update && apt-get install -y dotnet-sdk-10.0
   ```
   Skrip `dotnet-install.sh` dan `builds.dotnet.microsoft.com` **diblokir** oleh proxy kontainer, jadi jangan dipakai. NuGet (`api.nuget.org`) bisa diakses.
2. Set `DOTNET_CLI_TELEMETRY_OPTOUT=1` dan `DOTNET_NOLOGO=1`.
3. Jalankan dari root repo:
   ```
   dotnet restore Condec.Portable.slnf
   dotnet build   Condec.Portable.slnf
   dotnet test    --solution Condec.Portable.slnf
   ```
   **Wajib pakai `--solution`** (atau `--project` untuk satu proyek). Dengan runner Microsoft.Testing.Platform, `dotnet test Condec.Portable.slnf` meneruskan nama file itu mentah ke test host dan hasilnya "Zero tests ran", exit code 5. Bukan berarti test-nya hilang.
4. Output build di Linux diarahkan ke `~/.cache/condec-artifacts` (lihat `Directory.Build.props`), bukan `bin/obj` di repo. Untuk menjalankan test assembly langsung: `dotnet run --project tests/Condec.Tests/Condec.Tests.csproj -f net10.0`.

## Tugas sekarang

<isi oleh pemilik proyek>

Kalau tugasnya adalah tahap yang punya bagian Windows, bagi pekerjaannya menjadi: (a) yang dikerjakan dan dites di sini, (b) yang hanya dikompilasi di sini, dan (c) yang diserahkan ke sesi Windows. Tulis pembagian itu di laporan dan di `docs/HANDOFF.md`.
