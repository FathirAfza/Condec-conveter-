# Proyek: Condec — aplikasi konversi file untuk Windows

> Spesifikasi asli dari pemilik proyek (disalin apa adanya). Desain visual ada di `Condec.html` (ringkasannya di `docs/HANDOFF.md`).

Bangun aplikasi desktop Windows bernama **Condec**: konverter file native, open source, dan 100% offline. Kerjakan bertahap sesuai urutan di bagian "Urutan kerja". Tulis rencana dulu sebelum menulis kode.

## Aturan penting
1. JANGAN mengarang API. Sebelum memakai library atau API apa pun (Windows App SDK, PdfPig, ACadSharp, dll.), cek dokumentasi resmi atau README GitHub-nya. Kalau ragu sebuah API ada, verifikasi dulu atau tanyakan ke saya.
2. Tidak boleh ada koneksi jaringan sama sekali: tanpa telemetry, analytics, update checker, atau upload. Semua proses berjalan di perangkat.
3. Tidak boleh ada fitur yang membuka proteksi DRM atau membongkar password PDF. PDF terkunci cukup ditolak dengan pesan yang jelas.
4. Setiap tahap baru dianggap selesai kalau `dotnet build` dan semua test lulus.
5. Identifier kode berbahasa Inggris. Teks UI memakai sentence case. *(Diubah pemilik, 2 Okt 2026: semula "Semua teks UI berbahasa Indonesia". Kini bahasa aplikasi mengikuti bahasa Windows: Inggris dan Indonesia, selain itu Inggris. Teks ada di `src/Condec.Core/Resources/Strings*.resx`.)*

## Stack
- WinUI 3 (Windows App SDK versi stabil terbaru), C#, .NET LTS terbaru yang terpasang, aplikasi packaged (MSIX).
- MVVM dengan CommunityToolkit.Mvvm.
- Tes dengan xUnit.
- Lisensi proyek: GPL-3.0-or-later. Buat file LICENSE dan THIRD-PARTY-NOTICES.md yang mencantumkan setiap dependensi beserta lisensinya.
- Hanya pakai dependensi open source yang kompatibel dengan GPL-3.0. Untuk codec berpaten (H.264, HEVC, AAC, HEIF), WAJIB lewat codec bawaan Windows, jangan membundel encoder sendiri.

## Struktur solusi
- `Condec` (aplikasi WinUI 3)
- `Condec.Core` (class library): interface konverter, pipeline, verifikasi, riwayat
- `Condec.Tests` (xUnit)

## Arsitektur inti (Condec.Core)
- Interface `IConverter`:
  - `IReadOnlyList<string> GetTargets(string sourceExtension)`
  - `Task ConvertAsync(ConversionRequest request, IProgress<ConversionProgress> progress, CancellationToken ct)`
- `ConverterRegistry` mengumpulkan semua konverter. Daftar format tujuan di UI diambil dari sini secara dinamis. Format yang tidak didukung di mesin pengguna (misalnya extension codec belum terpasang) tidak boleh muncul.
- Pipeline memiliki 4 tahap berurutan dengan bobot progres:
  1. Decode (0–35%): membaca dan mendekode file sumber.
  2. Encode (35–70%): menulis ke format tujuan ke file sementara `<nama>.condec-tmp` di folder tujuan.
  3. VerifyChunks (70–90%): saat menulis, hitung SHA-256 per chunk 1 MiB. Setelah selesai, baca ulang file dari disk per chunk dan bandingkan hash-nya. Laporkan "Chunk n dari N".
  4. VerifyIntegrity (90–100%): bandingkan SHA-256 seluruh file, lalu buka ulang output dengan decoder yang sesuai (gambar, PDF, DXF/DWG, media) untuk memastikan file valid dan bisa dibaca.
- Kalau semua tahap lulus, pindahkan file sementara ke path akhir. Kalau gagal atau dibatalkan, hapus file sementara dan jangan pernah meninggalkan file setengah jadi.
- Batal harus didukung di setiap tahap lewat CancellationToken.

## Konverter (implementasikan satu kategori per tahap)
1. **Gambar**: pakai `Windows.Graphics.Imaging` (BitmapDecoder/BitmapEncoder). Format: JPG, PNG, BMP, GIF, TIFF. HEIC dan WebP hanya sebagai sumber, dan hanya jika decoder-nya tersedia di sistem.
2. **Audio/video**: pakai `Windows.Media.Transcoding.MediaTranscoder` dengan MediaEncodingProfile bawaan. Tampilkan hanya profil yang benar-benar didukung sistem.
3. **Dokumen (DOCX, ODT, RTF, TXT, HTML, PPTX, XLSX → PDF, dll.)**: deteksi apakah LibreOffice terpasang, lalu jalankan `soffice --headless --convert-to` di folder sementara. Kalau LibreOffice tidak ada, format dokumen ditampilkan disabled dan InfoBar menjelaskan cara memasangnya. Jangan bundel LibreOffice.
4. **PDF → PNG/JPG**: render halaman dengan `Windows.Data.Pdf`.
5. **PDF → DXF/DWG**:
   - Baca operasi grafis halaman dengan PdfPig (Apache-2.0). Ubah path (garis, kurva Bézier, persegi) menjadi LINE, ARC atau SPLINE, dan LWPOLYLINE. Teks menjadi entitas TEXT kalau opsi "Pertahankan teks sebagai teks" aktif.
   - Tulis output dengan ACadSharp (MIT). Cek README ACadSharp untuk versi DWG/DXF yang benar-benar didukung untuk penulisan, lalu pilih default paling aman.
   - Opsi: satuan (mm, cm, m, inci), skala (1:1, 1:50, 1:100, 1:200, kustom), halaman, dan "Gabungkan garis bersambung" (jadi polyline). Konversi satuan: 1 point PDF = 1/72 inci.
   - Deteksi PDF hasil scan: jika halaman hampir tidak punya operasi path dan didominasi satu gambar raster, tandai sebagai scan. Untuk scan: render halaman dengan Windows.Data.Pdf, binarisasi, telusuri kontur (marching squares), sederhanakan dengan Ramer–Douglas–Peucker, lalu tulis sebagai LWPOLYLINE. Opsi teks dinonaktifkan.
   - Nama "DWG" hanya disebut sebagai nama format, tidak dipakai di nama atau logo aplikasi.

## Riwayat
- Simpan di `%LOCALAPPDATA%\Condec\history.json`: nama file, format asal → tujuan, waktu, path output, status verifikasi. Tidak ada salinan isi file.
- Ada tombol "Hapus riwayat" dan opsi mematikan riwayat.

## UI (ikuti Fluent / Windows 11, gunakan kontrol bawaan WinUI)
- Pakai ThemeResource untuk semua warna (TextFillColorPrimaryBrush, CardBackgroundFillColorDefaultBrush, CardStrokeColorDefaultBrush, dll.). DILARANG hardcode hex. Harus mendukung tema terang, gelap, dan high contrast.
- Tipografi memakai style bawaan: TitleTextBlockStyle, SubtitleTextBlockStyle, BodyTextBlockStyle, BodyStrongTextBlockStyle, CaptionTextBlockStyle.
- Latar jendela MicaBackdrop. Title bar kustom lewat ExtendsContentIntoTitleBar: tinggi 32px, ikon 16px, judul "Condec" dengan gaya caption.
- Ukuran jendela default 1120×780. Konten dalam satu kolom lebar maks 880px di tengah.
- Susunan dari atas ke bawah:
  1. Judul "Konversi file" dan subjudul "Pilih file, tentukan format tujuan, lalu simpan."
  2. Kartu konverter (radius 8px) dengan 3 keadaan:
     - **Input:** kiri area file (drag-and-drop plus tombol "Pilih file…"; setelah dipilih menampilkan nama, jenis, ukuran, dan tombol "Ganti"). Tengah ikon panah ke kanan. Kanan label "Ubah ke format" dan ComboBox format (disabled "Pilih file dulu" jika belum ada file). Di bawahnya teks kecil "Diproses sepenuhnya di perangkat ini. Tidak ada file yang diunggah." dan tombol aksen "Konversi dan simpan…" di kanan.
     - **Proses:** judul "Mengonversi <nama file>", keterangan format dan path tujuan, tombol "Batal", ProgressBar dengan persen, dan daftar 4 tahap (Mendekode file sumber, Menulis ke format X, Verifikasi chunk, Cek integritas file), masing-masing dengan status selesai, aktif, atau menunggu.
     - **Selesai:** InfoBar Success "Konversi selesai", lalu detail lokasi, format, dan integritas ("SHA-256 cocok · N chunk terverifikasi"). Tombol "Buka file" (aksen), "Tampilkan di folder", dan "Konversi file lain".
  3. Untuk sumber PDF dengan tujuan DXF/DWG, di kartu input muncul: InfoBar (Success "PDF vektor terdeteksi" atau Warning "PDF hasil scan terdeteksi" dengan tombol "Pilih file lain"), ComboBox Satuan, Skala, dan Halaman dalam 3 kolom, serta dua ToggleSwitch. Untuk scan, tombol utama menjadi "Tetap konversi…".
  4. Kartu "Riwayat" berisi ListView yang bisa di-scroll: ikon file, nama, "ASAL → TUJUAN · waktu", status "Terverifikasi", dan tombol ikon folder (dengan AutomationProperties.Name).
- Alur: klik "Konversi dan simpan…" membuka FileSavePicker (inisialisasi dengan HWND jendela), lalu pipeline berjalan.
- Aksesibilitas: semua tombol ikon punya AutomationProperties.Name, dan navigasi keyboard harus berjalan.

## Urutan kerja
1. Tulis rencana singkat, lalu scaffold solusi (kalau template WinUI tidak tersedia di CLI, buat csproj manual sesuai dokumentasi Windows App SDK). Pastikan build lulus.
2. Condec.Core: IConverter, registry, pipeline 4 tahap, verifikasi chunk dan integritas, file sementara atomik, riwayat. Beserta unit test-nya.
3. UI tiga keadaan (Input, Proses, Selesai) dengan konverter gambar sebagai konverter pertama.
4. Konverter audio/video.
5. Konverter dokumen via LibreOffice.
6. PDF → gambar, lalu PDF → DXF/DWG termasuk deteksi dan vektorisasi scan.
7. README (cara build, daftar format, catatan privasi), LICENSE, dan THIRD-PARTY-NOTICES.md.

Setelah setiap tahap, laporkan apa yang selesai, hasil build dan test, lalu tunggu konfirmasi saya sebelum lanjut.
