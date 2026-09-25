# Prompt untuk sesi Claude Code lokal (Windows)

Salin semua teks di bawah garis ke sesi baru Claude Code yang dibuka di root folder proyek Condec.

---

Kita melanjutkan proyek **Condec**: aplikasi konverter file untuk Windows (WinUI 3, Windows App SDK, .NET 10, MSIX), open source dan 100% offline. Sesi sebelumnya dikerjakan di server Linux yang tidak bisa mem-build WinUI, jadi pengerjaan pindah ke mesin Windows ini.

**Sebelum melakukan apa pun, baca berurutan:**
1. `docs/SPEC.md`: spesifikasi dan aturan wajib dari saya. Aturan intinya: jangan mengarang API, tidak ada koneksi jaringan sama sekali, tidak ada pembongkaran DRM atau password PDF, identifier berbahasa Inggris, teks UI bahasa Indonesia dengan sentence case, dan warna hanya lewat ThemeResource.
2. `docs/PLAN.md`: rencana teknis yang sudah disetujui sebagai arah kerja.
3. `docs/HANDOFF.md`: status terakhir, fakta dan versi yang sudah diverifikasi, keputusan yang sudah diambil, dan ringkasan desain.

Desain visual ada di `Condec.html` (bundel) dan `docs/design/*.html` (markup mentah yang lebih mudah dibaca).

**Tugas sekarang: menyelesaikan verifikasi tahap 1 di Windows.**
1. Cek prasyarat: `dotnet --list-sdks` harus menampilkan .NET 10. Kalau belum ada, beri tahu saya. Jangan memasang apa pun tanpa izin.
2. Kalau folder ini belum repo git, jalankan `git init` dan buat commit awal berisi scaffold apa adanya, sebagai baseline.
3. Jalankan dari root proyek:
   - `dotnet build Condec.sln`
   - `dotnet test Condec.sln`
4. Kalau ada error, perbaiki dengan perubahan sekecil mungkin. Setiap API atau properti MSBuild yang dipakai untuk perbaikan harus diverifikasi dulu (dokumentasi resmi Microsoft Learn, README, atau isi paket NuGet di `%USERPROFILE%\.nuget\packages`). Jangan menebak. Kalau masalahnya ada di paket komponen Windows App SDK, ikuti fallback di `docs/HANDOFF.md` §3.
5. Kalau build lulus, coba jalankan aplikasinya. Pastikan jendela terbuka dengan Mica, title bar kustom 32px, ukuran sekitar 1120×780, dan judul "Konversi file", di tema terang maupun gelap.
6. Laporkan dalam bahasa Indonesia: apa yang dijalankan, output build/test (ringkas, tapi sertakan error lengkap kalau ada), apa yang diubah dan alasannya, serta hasil uji jalan aplikasi.

Setelah melapor, **berhenti dan tunggu konfirmasi saya** sebelum mulai tahap 2 (Condec.Core: kontrak, registry, pipeline 4 tahap, verifikasi chunk dan integritas, file sementara atomik, riwayat, beserta unit test). Aturan ini berlaku untuk setiap tahap berikutnya juga: selesaikan satu tahap, laporkan hasil build dan test, lalu tunggu konfirmasi.
