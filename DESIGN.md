# Condec — DESIGN.md

Versi dokumen 0.2.6 · diubah terakhir 2026-10-03 · target: WinUI 3 (Windows App SDK) di Windows 11

## 0. Cara memakai dokumen ini

1. Dokumen ini adalah **sumber kebenaran** untuk tampilan dan perilaku Condec. Kode mengikuti dokumen, bukan sebaliknya.
2. Setiap perubahan UI atau perilaku (teks, ukuran, kontrol, aturan batas, alur) **wajib mengubah dokumen ini di commit yang sama**, dan menambah satu entri di §14 Changelog.
3. Kalau sebuah permintaan bertentangan dengan dokumen ini, berhenti. Tunjukkan bagian yang bertentangan, lalu tanya pemilik (Afza) sebelum lanjut. Jangan menyimpang diam-diam.
4. Tag status pada angka dan klaim:
   - `[SRC]` diambil dari source resmi `microsoft/microsoft-ui-xaml` (branch main, dicek 2026-10-02).
   - `[DOC]` dari dokumentasi resmi Microsoft.
   - `[ASUMSI]` perkiraan yang belum diverifikasi. Boleh dipakai, tapi harus diuji atau dikonfirmasi.
   - `[TERBUKA]` butuh keputusan pemilik.
   - Tanpa tag = keputusan desain.
5. Rujuk bagian dokumen dengan nomornya, mis. "DESIGN §6.2".
6. Hex di mock visual hanya perkiraan. Yang berlaku adalah `ThemeResource` key di §4.
7. Mock visual (kanvas desain, §12) membantu membayangkan layout. Kalau mock dan teks dokumen berbeda, **teks dokumen yang menang**.

## 1. Ringkasan produk

Condec adalah aplikasi desktop Windows yang 100% offline dan open source (GPL-3.0-or-later). Empat bagian, dipilih lewat menu hamburger:

| Menu | Fungsi |
|---|---|
| Convert File | Ubah format file (gambar, audio/video, dokumen, PDF) dengan verifikasi chunk dan integritas. |
| Upscale Image | Perbesar resolusi gambar memakai GPU, CPU, atau NPU, dengan batas yang mengikuti kemampuan perangkat. |
| Architecture | Gambar, PDF, dan DXF menjadi DWG, atau sebaliknya. Ada pratinjau dan pilihan objek yang tidak dijadikan CAD. |
| Settings | Render mode, batas upscale, batas memori, render dump (cache), tampilan, log. |

Prinsip:
- **Privasi:** semua proses di perangkat. Tanpa telemetry, analytics, update checker, atau upload.
- **Jujur soal batas:** skala yang tidak sanggup dijalankan perangkat dikunci, dan alasannya ditampilkan.
- **Terverifikasi:** setiap file hasil dicek per chunk dan dicek integritasnya sebelum dinyatakan berhasil.
- **Stock saja:** hanya kontrol bawaan WinUI 3.

## 2. Platform dan aturan kontrol

- WinUI 3 (Windows App SDK stabil terbaru), C#, packaged (MSIX), Windows 11.
- **Hanya kontrol stock WinUI 3.** Dilarang: `CommunityToolkit.WinUI.Controls.*` (SettingsCard, Segmented, dll.) dan library kontrol UI pihak ketiga. Library non-UI (mis. CommunityToolkit.Mvvm) boleh.
- WPF bawaan tidak punya NavigationView, InfoBar, ToggleSwitch, dan SelectorBar. Dokumen ini hanya valid untuk WinUI 3.
- Warna dan style teks lewat `ThemeResource` dan style bawaan. Dilarang hardcode hex, kecuali tiga warna gambar pratinjau di §6.3.
- Margin dan padding layout boleh angka literal.
- Bahasa UI mengikuti daftar bahasa Windows: Indonesia dan Inggris, bahasa lain jatuh ke Inggris. Semua teks UI adalah resource di `Condec.Core/Resources`. Teks di dokumen ini adalah versi Indonesia dan jadi acuan terjemahan Inggrisnya. Sentence case. Nama menu sama di kedua bahasa: "Convert File", "Upscale Image", "Architecture", "Settings".
- Format angka (versi Indonesia; versi Inggris memakai titik desimal): desimal pakai koma ("0,9 MP", "23,9 MB"), dimensi "1280 × 720" (spasi di kedua sisi ×), skala "4×", persen "400%", perkiraan diawali "± ".
- Preview vektor DWG: coba `Canvas`/`Polyline`/`Path` stock dulu. Win2D hanya kalau performa tidak cukup, dan minta persetujuan dulu `[TERBUKA]`.

## 3. Shell aplikasi

### 3.1 Jendela
- Ukuran awal 1280 × 820 (dikecilkan seperlunya bila layar lebih kecil). Ukuran minimum 900 × 640 (usulan §13 #6, dipakai sementara; masih `[TERBUKA]`).
- `MicaBackdrop`, bisa dimatikan di Settings (§6.4); saat mati, latar jendela `SolidBackgroundFillColorBaseBrush`. Title bar kustom: tinggi 32, ikon aplikasi 16, judul "Condec". Tombol minimize/maximize/close digambar sistem (46 × 32).
- Dipakai kontrol `TitleBar` (Microsoft.WinUI 2.3.9, Windows App SDK 2.5.1; properti `Title`, `IconSource`, `IsBackButtonVisible`, `IsPaneToggleButtonVisible` dicek di metadata; tinggi bawaan `TitleBarCompactHeight` 32, ikon maks 16 `[SRC]`), dipasang lewat `ExtendsContentIntoTitleBar` + `SetTitleBar`. Judulnya meredup sendiri saat jendela tidak aktif. Warna tombol caption mengikuti tema pilihan lewat `AppWindow.TitleBar.PreferredTheme`.

### 3.2 NavigationView

| Properti | Nilai |
|---|---|
| PaneDisplayMode | Auto (Left saat lebar ≥ 1008, LeftCompact 641–1007, LeftMinimal ≤ 640) `[DOC]` |
| IsPaneToggleButtonVisible | True (hamburger). Menutup pane = mode kompak. |
| OpenPaneLength | 280 (bawaan 320 `[DOC]`) |
| CompactPaneLength | 48 (bawaan) `[SRC]` |
| IsBackButtonVisible | Collapsed |
| IsSettingsVisible | True (item Settings otomatis di bawah) |
| Item | Convert File (terpilih awal), Upscale Image, Architecture |
| Ikon item | `FontIcon` 16, glyph dari Segoe Fluent Icons. Pilih dari tabel resmi, jangan menebak codepoint. Dipakai (dari enum `Symbol` di `Microsoft.UI.Xaml.winmd`): Convert File `Switch` U+E13C, Upscale Image `FullScreen` U+E1D9, Architecture `Map` U+E1C4. |
| Isi | `Frame`, navigasi dari `SelectionChanged`. Halaman di-cache (`NavigationCacheMode=Required`) supaya pilihan dan konversi yang sedang berjalan tidak hilang saat pindah halaman. |

Dengan ukuran minimum 900 (§3.1), pane hanya bisa berpindah antara terbuka (≥ 1008) dan kompak (900–1007); mode minimal (≤ 640) tidak tercapai lewat pengubahan ukuran jendela. Nama item Settings diganti aplikasi supaya tetap "Settings" di kedua bahasa (§2).

Metrik bawaan yang dipakai mock `[SRC]`: tinggi item 36, tombol hamburger 40 × 36, indikator pilih 3 × 16 (radius 2), sudut kiri atas area konten 8, lebar kompak 48.

### 3.3 Area konten
- Latar `LayerOnMicaBaseAltFillColorDefaultBrush`, garis tipis dan sudut kiri atas 8 digambar NavigationView sendiri (jangan ditiru manual).
- Tiap `Page` memuat `ScrollViewer` dengan padding **40 kiri/kanan, 28 atas, 32 bawah**. Judul halaman adalah `TextBlock` di dalam Page (bukan `NavigationView.Header`, yang bawaannya bermargin 56,44 `[SRC]`), supaya ikut scroll.
- Kolom konten: lebar maksimum 960, rata kiri. Jarak judul → konten 20. Jarak antar kartu 16.
- Judul halaman: `TitleTextBlockStyle`. Subjudul: `BodyTextBlockStyle` dengan `TextFillColorSecondaryBrush`. Jarak judul-subjudul 4.

| Halaman | Judul | Subjudul |
|---|---|---|
| Convert File | Convert File | Pilih file, tentukan format tujuan, lalu simpan. |
| Upscale Image | Upscale Image | Perbesar resolusi gambar di perangkat ini dengan GPU, CPU, atau NPU. |
| Architecture | Architecture | Ubah gambar, PDF, dan DXF menjadi DWG, atau sebaliknya. |
| Settings | Settings | Atur mesin render, batas memori, tampilan, dan log. |

Selama tahap 3 sampai 6: halaman Upscale Image hanya memuat judul, subjudul, dan satu `InfoBar` Informational "Upscale belum tersedia" (supaya menu tidak tampak rusak; dihapus di tahap 6). Halaman Architecture memuat kartu konverter yang sama dengan Convert File, khusus DXF dan DWG (§6.3); diganti layar §6.3 di tahap 7.

### 3.4 Wireframe shell
```
+-------------------------------------------------------------------+
| [logo] Condec                                          -   []   x |  32
+------+------------------------------------------------------------+
| (=)  | Convert File                         <- TitleTextBlockStyle |
| [<>] | Pilih file, tentukan format ...                             |
| [^]  |                                                             |
| [#]  |  (isi halaman, kolom maks 960, rata kiri)                   |
|      |                                                             |
| [*]  |                                                             |
+------+------------------------------------------------------------+
 pane 280 (kompak 48)       konten: padding 40 / 28 / 40 / 32
```
Urutan pane: hamburger, Convert File, Upscale Image, Architecture, (ruang kosong), Settings.

## 4. Token warna dan tipografi

### 4.1 Warna (selalu lewat ThemeResource)
Nilai dari `Common_themeresources_any.xaml` `[SRC]`. Cek keberadaan key di versi Windows App SDK yang terpasang saat build.

| Peran | Key | Terang | Gelap |
|---|---|---|---|
| Latar jendela (fallback Mica) | `SolidBackgroundFillColorBase` | #F3F3F3 | #202020 |
| Area konten | `LayerOnMicaBaseAltFillColorDefaultBrush` | #B3FFFFFF | #733A3A3A |
| Kartu | `CardBackgroundFillColorDefaultBrush` | #B3FFFFFF | #0DFFFFFF |
| Garis kartu | `CardStrokeColorDefaultBrush` | #0F000000 | #19000000 |
| Isi kontrol | `ControlFillColorDefaultBrush` | #B3FFFFFF | #0FFFFFFF |
| Garis kontrol | `ControlStrokeColorDefaultBrush` | #0F000000 | #12FFFFFF |
| Garis kontrol tegas | `ControlStrongStrokeColorDefaultBrush` | #72000000 | #8BFFFFFF |
| Isi kontrol solid (thumb slider) | `ControlSolidFillColorDefaultBrush` | #FFFFFF | #454545 |
| Hover dan item terpilih | `SubtleFillColorSecondaryBrush` | #09000000 | #0FFFFFFF |
| Teks utama | `TextFillColorPrimaryBrush` | #E4000000 | #FFFFFF |
| Teks sekunder | `TextFillColorSecondaryBrush` | #9E000000 | #C5FFFFFF |
| Teks nonaktif | `TextFillColorDisabledBrush` | #5C000000 | #5DFFFFFF |
| Teks di atas aksen | `TextOnAccentFillColorPrimaryBrush` | #FFFFFF | #000000 |
| Aksen | `AccentFillColorDefaultBrush` | turunan warna aksen sistem | turunan warna aksen sistem |
| InfoBar sukses (latar / ikon) | `SystemFillColorSuccessBackgroundBrush` / `SystemFillColorSuccessBrush` | #DFF6DD / #0F7B0F | #393D1B / #6CCB5F |
| InfoBar peringatan | `SystemFillColorCautionBackgroundBrush` / `SystemFillColorCautionBrush` | #FFF4CE / #9D5D00 | #433519 / #FCE100 |
| InfoBar galat | `SystemFillColorCriticalBackgroundBrush` / `SystemFillColorCriticalBrush` | #FDE7E9 / #C42B1C | #442726 / #FF99A4 |
| InfoBar informasi | `SystemFillColorAttentionBackgroundBrush` | #80F6F6F6 | #08FFFFFF |

Catatan: mock visual memakai hex yang sudah "diratakan" (mis. kartu #FDFDFD). Di aplikasi nyata, nilai bawaan di atas yang berlaku, dan Mica membuat hasilnya sedikit berbeda.

**Satu-satunya warna tetap (bukan tema)**, khusus gambar pratinjau di Architecture:
- Kertas pratinjau selalu putih #FFFFFF dengan garis #1B1B1B, juga di tema gelap.
- Objek yang dijadikan CAD: garis #0F6CBD, isi biru 10%.
- Objek yang tidak dijadikan CAD: garis putus-putus #8A8A8A, isi putih 82%.
- Penanda area tidak jelas: lingkaran #F7B500 dengan tanda "!".

### 4.2 Tipografi `[SRC]`
Font bawaan (`XamlAutoFontFamily`, Segoe UI Variable). Style dasar bobot SemiBold, style turunan menimpa.

| Pakai untuk | Style | Ukuran / bobot |
|---|---|---|
| Judul halaman, angka besar "Batas upscale perangkat" | `TitleTextBlockStyle` | 28 SemiBold |
| Judul keadaan ("Mengonversi …") | `SubtitleTextBlockStyle` | 20 SemiBold |
| Nilai statistik (resolusi hasil, ukuran, waktu, RAM) | `BodyLargeStrongTextBlockStyle` | 18 SemiBold |
| Judul grup setelan, nama item penting | `BodyStrongTextBlockStyle` | 14 SemiBold |
| Isi | `BodyTextBlockStyle` | 14 Normal |
| Keterangan, label kecil, status | `CaptionTextBlockStyle` | 12 Normal |

Mock memakai 16 untuk nilai statistik. Implementasi memakai 18 (`BodyLargeStrongTextBlockStyle`) agar tidak ada override ukuran.

## 5. Katalog kontrol stock

| Kontrol | Metrik dan catatan |
|---|---|
| `Button` | Padding 11,5,11,6 `[SRC]`. Sudut 4 (`ControlCornerRadius`) `[DOC]`. Tinggi tampil 32. Aksi utama: `AccentButtonStyle`. |
| `HyperlinkButton` | Aksi sekunder berbentuk teks ("Ganti", "Hapus riwayat", "Ubah di Settings"). |
| `ComboBox` | MinHeight 32. Item: padding 11,5,11,7, sudut 3, pill pilih 3 × 16 (radius 1,5), margin isi dropdown 0,4 `[SRC]`. Item yang tidak boleh dipilih: `ComboBoxItem.IsEnabled=False`. Nilai di luar daftar: `PlaceholderText`. |
| `ToggleSwitch` | Track 44 × 20 (radius 10), knob 10, jarak ke label 12 `[SRC]`. `OnContent="Aktif"`, `OffContent="Nonaktif"`. |
| `Slider` | Tinggi track 4, tinggi kontrol 32, tick bar tinggi 4 margin 4 `[SRC]`. Warna: nilai = `AccentFillColorDefaultBrush`, sisa = `ControlStrongFillColorDefaultBrush`, thumb luar `ControlSolidFillColorDefaultBrush`. Ukuran thumb `[ASUMSI]` 20. Pakai `TickFrequency`, `TickPlacement=BottomRight`, `SnapsTo=Ticks`. |
| `RadioButtons` / `RadioButton` | Lingkaran 20, titik tengah 10 `[SRC]`. Item yang tidak berlaku: `IsEnabled=False`. |
| `CheckBox` | Kotak 20 × 20 `[SRC]`. Dalam `ListView` mode Multiple, kotak centang muncul otomatis. |
| `ListView` / `ListViewItem` | MinHeight item 40 `[SRC]`. Mock memakai tinggi 44 untuk item dua baris. Riwayat: `SelectionMode=None`. Daftar objek dan layer: `SelectionMode=Multiple`. |
| `InfoBar` | MinHeight 48, padding isi 16,0,0,0, ikon 16 (margin 0,16,14,16), judul SemiBold 14, pesan 14, tombol tutup 38, garis 1 `CardStrokeColorDefaultBrush`, sudut 4 `[SRC]`. `IsClosable=False` di semua pemakaian. Tombol aksi di `ActionButton` (satu), atau di `Content` bila perlu lebih dari satu. |
| `ProgressBar` | Tinggi 3, track 1, radius indikator 1,5, radius track 0,5 `[SRC]`. |
| `ProgressRing` | Untuk tahap yang sedang berjalan di daftar tahap. |
| `Expander` | MinHeight 48, padding header 16,0,0,0, padding isi 16, tombol chevron 32 dengan glyph 12 `[SRC]`. |
| `NavigationView` | Lihat §3.2. |
| `SelectorBar` / `SelectorBarItem` | Padding item 12,10,12,7, tinggi pill pilih 3 `[SRC]`. Lebar pill `[TERBUKA]` (mock memakai 16). |
| `SplitButton` + `MenuFlyout` | Bagian kiri = aksi bawaan, chevron membuka menu pilihan. Item menu terkunci: `IsEnabled=False`. |
| Kartu | `Border` + `Grid`. Latar `CardBackgroundFillColorDefaultBrush`, garis `CardStrokeColorDefaultBrush` tebal 1. Sudut 8 untuk kartu besar, 4 untuk baris setelan `[DOC]`. |
| Area seret file | `Grid` dengan `AllowDrop=True`. Garis putus-putus memakai `Rectangle` + `StrokeDashArray` (Border tidak punya stroke putus-putus). |
| `FileOpenPicker` / `FileSavePicker` | Wajib `InitializeWithWindow` dengan HWND jendela. |

## 6. Halaman

Semua halaman memakai kartu (sudut 8, garis 1). Tinggi komponen standar 32. Jarak vertikal antar kartu 16.

### 6.1 Convert File

Struktur: kartu konverter (padding 24) dengan tiga keadaan, lalu kartu Riwayat.

```
+-- Kartu konverter (padding 24) --------------------------------------+
| [ Area file / seret (tinggi 168) ]  ->  [ Ubah ke format (tinggi 168)]|
| (perisai) Diproses sepenuhnya di perangkat ini...  [Konversi dan simpan...] |
+----------------------------------------------------------------------+
+-- Riwayat ----------------------------------------- Hapus riwayat ---+
| [ikon] nama.docx   DOCX -> PDF · waktu     v Terverifikasi    [folder]|
+----------------------------------------------------------------------+
```

**Keadaan Input**
- Kiri: kalau belum ada file, area seret (garis putus-putus, ikon 32, "Seret file ke sini", tombol "Pilih file…"). Kalau sudah ada file: ikon jenis file 40, label "File sumber" (Caption), nama (BodyStrong, terpotong ellipsis), jenis dan ukuran (Caption, mis. "Dokumen Word · 2,4 MB"), `HyperlinkButton` "Ganti".
- Tengah: ikon panah ke kanan 24.
- Kanan (padding 24): label "Ubah ke format", `ComboBox` format, teks bantu (Caption), `HyperlinkButton` "Butuh DXF atau DWG? Buka Architecture" (memilih item Architecture di menu).
  - Cakupan: Convert File hanya menawarkan konversi yang bukan dari dan bukan ke DXF/DWG. File DXF/DWG yang dipilih atau diseret ke sini mendapat `InfoBar` Informational yang mengarahkan ke Architecture ("File .dxf diproses di Architecture. Buka dari menu."). Sebaliknya di Architecture (§6.3).
  - Halaman bergulir sebagai satu kesatuan (§3.3): kartu Riwayat tidak lagi mengisi sisa tinggi jendela.
  - Belum ada file: ComboBox nonaktif, teks "Pilih file dulu", bantu "Format muncul setelah file dipilih".
  - Sudah ada file: placeholder "Pilih format", bantu "N format tersedia untuk .ext". Isi daftar diambil dari `ConverterRegistry`, bukan daftar tetap. Format yang tidak didukung mesin tidak muncul.
- Bawah kartu: kiri teks Caption dengan ikon perisai "Diproses sepenuhnya di perangkat ini. Tidak ada file yang diunggah."; kanan tombol aksen "Konversi dan simpan…", **nonaktif sampai file dan format dipilih**.
- Opsi media, hanya untuk target audio dan video: satu baris di bawah dua panel (margin atas 20, dua kolom, jarak 16), `ComboBox` stock berheader. Tidak ada untuk WAV dan FLAC (tanpa kehilangan) dan untuk format lain.
  - Target MP3, M4A, WMA: "Kualitas audio" dengan "Tinggi (192 kbps)" (bawaan), "Sedang (128 kbps)", "Rendah (96 kbps)".
  - Target MP4, WMV: "Ukuran video" dengan "Sama dengan sumber" (bawaan), "1080p (Full HD)", "720p (HD)", "480p", dan teks bantu Caption "Video tidak pernah diperbesar, dan bentuk gambarnya tetap sama."
  - Pilihan bertahan sampai aplikasi ditutup (tidak disimpan ke Settings) dan berlaku juga untuk "Coba lagi". Detailnya di §6.1.2.
- Format sumber tidak ditawarkan sebagai tujuan, **kecuali** MP3, M4A, WMA, MP4, dan WMV: itu boleh dikonversi ke format yang sama supaya kualitas atau ukurannya bisa diubah (memperkecil MP4, menurunkan bitrate MP3). Nama yang disarankan di dialog simpan lalu diberi akhiran " (hasil)" (Inggris " (converted)"), supaya yang pertama ditawarkan bukan menimpa sumber. Menyimpan di atas sumber tetap ditolak (`Error.SameFile`).
- Klik tombol → `FileSavePicker`, lalu keadaan Proses.

**Keadaan Proses**
- Judul `SubtitleTextBlockStyle`: "Mengonversi `<nama file>`". Caption: "Ke `<FORMAT>` · disimpan sebagai `<path>`". Tombol "Batal" di kanan.
- `ProgressBar` + persen (rata kanan, lebar 40, angka tabular).
- Empat tahap, tinggi baris 40, ikon 20 (selesai = lingkaran aksen berisi centang, aktif = `ProgressRing`, menunggu = lingkaran kosong). Nama tahap tebal saat aktif, teks sekunder saat menunggu:

| Tahap | Rentang progres | Keterangan kanan |
|---|---|---|
| Mendekode file sumber | 0–35% | "Membaca `<nama>`" → "Selesai" |
| Menulis ke format `<FORMAT>` | 35–70% | "Encode ke `.ext`" → "Selesai" |
| Verifikasi chunk | 70–90% | "Chunk n dari N" → "N chunk cocok" ("1 chunk matches" untuk satu chunk di Inggris) |
| Cek integritas file | 90–100% | "Menghitung SHA-256" → "Hash cocok" |

- Batal bisa di setiap tahap. File sementara dihapus.

**Keadaan Selesai**
- `InfoBar` Success: judul "Konversi selesai", pesan "File berhasil disimpan dan lolos verifikasi chunk serta cek integritas."
- Tiga baris detail (label lebar 96, Caption sekunder): Lokasi (path penuh), Format ("DOCX → PDF"), Integritas ("SHA-256 cocok · N chunk terverifikasi"; untuk satu chunk, Inggris memakai bentuk tunggal "1 chunk verified").
- Catatan konverter (nol atau lebih), di antara tiga baris detail dan tombol: satu `InfoBar` per catatan (`IsClosable=False`, margin bawah 20), urut seperti dilaporkan konverter. Catatan **bukan kegagalan**: file tetap disimpan dan lolos verifikasi; catatan hanya memberi tahu sesuatu tentang sumbernya.

| Tingkat | Teks (kunci resource) | Kapan |
|---|---|---|
| Warning | "File sumber tampak terpotong atau rusak, jadi sebagian gambar bisa hilang atau abu-abu. Periksa hasilnya sebelum dipakai." (`Note.SourceIncomplete`) | PNG, JPEG, GIF, atau WebP yang strukturnya berakhir sebelum penanda akhirnya (§6.1.1) |
| Informational | "File ini berisi N gambar (frame atau halaman). Hanya yang pertama yang dikonversi." (`Note.FirstFrameOnly`) | Sumber punya lebih dari satu frame (GIF animasi, TIFF banyak halaman) |
| Warning | "File sumber tampak terpotong atau rusak, jadi hasilnya bisa berhenti lebih awal atau ada bagian yang hilang. Periksa hasilnya sebelum dipakai." (`Note.MediaIncomplete`) | Audio atau video yang terpotong (§6.1.2) |

  - Catatan dikosongkan saat konversi baru dimulai (termasuk "Coba lagi") dan saat "Konversi file lain". Setiap catatan juga ditulis ke log aktivitas tanpa path: hanya jenis konversi ("`.jpg -> .png`") dan tingkatnya.
  - "SHA-256 cocok" dan status "Terverifikasi" di Riwayat berarti **keluaran** utuh dan sama dengan yang ditulis, bukan bahwa sumbernya utuh. Untuk sumber yang terpotong, kedua hal itu tampil bersama Warning di atas.
- Tombol: "Buka file" (aksen), "Tampilkan di folder", `HyperlinkButton` "Konversi file lain" (kembali ke Input, kosong). Kalau "Buka file" atau "Tampilkan di folder" gagal, `InfoBar` Warning dengan pesannya muncul di antara catatan dan tombol.

**Keadaan Gagal**
- `InfoBar` Error: judul "Konversi gagal", pesan sesuai penyebab dan tahap terakhir (tabel di bawah). Dua baris detail (label lebar 96): File (nama sumber), Format ("PNG → JPG").
- Tombol: "Coba lagi" (aksen, mengulang pekerjaan yang sama), "Ubah pilihan" (kembali ke Input dengan file dan format yang sama).
- Tidak ada file setengah jadi yang tersisa di folder tujuan.

| Penyebab | Pesan (kunci) |
|---|---|
| Sumber tidak bisa didekode (rusak, atau codec belum terpasang) | "File sumber tidak bisa dibaca. File mungkin rusak, atau codec untuk format ini belum terpasang di Windows." (`Error.Decode`) |
| Gambar terlalu besar untuk memori (§6.1.1) | "Gambar ini (N MP) terlalu besar untuk dikonversi dengan memori yang tersedia di PC ini. Tutup aplikasi lain lalu coba lagi, atau pakai gambar yang lebih kecil." (`Error.ImageTooLarge`) |
| Windows tidak bisa mengonversi audio atau video (rusak, kosong, bukan media, codec tidak ada, atau gagal di tengah jalan) | "Windows tidak bisa mengonversi file audio atau video ini. File mungkin rusak, atau codec-nya belum terpasang." (`Error.Media`) |
| Verifikasi gagal, file tidak ditulis, folder, izin, PDF terkunci, dan sebagainya | Teks yang sudah ada di `Strings.resx` (`Error.*`) |

**Riwayat**
- Header: "Riwayat" (BodyStrong), `CheckBox` "Catat riwayat" (nilai awal hidup; disimpan di `history.json`; saat mati konversi baru tidak dicatat, entri lama tetap sampai dihapus, daftar kosong menampilkan "Riwayat sedang tidak dicatat. Konversi berikutnya tidak akan muncul di sini."), `HyperlinkButton` "Hapus riwayat" (membuka `Flyout` konfirmasi: "Hapus semua riwayat? File hasil konversi tidak ikut terhapus.").
- `ListView` (`SelectionMode=None`): kotak ikon 32, nama, Caption "ASAL → TUJUAN · waktu", status "Terverifikasi" (Caption, warna sukses, ikon centang 12), tombol ikon folder (`AutomationProperties.Name="Tampilkan di folder"`).
- Kosong: "Belum ada riwayat konversi."
- Data: `%LOCALAPPDATA%\Condec\history.json` (nama, asal → tujuan, waktu, path, status verifikasi). Tanpa salinan isi file. Konversi baru masuk paling atas.

#### 6.1.1 Konverter gambar (Windows Imaging Component)

Dipakai Convert File untuk JPG, PNG, BMP, GIF, TIFF, serta WebP dan HEIC. Hanya memakai `Windows.Graphics.Imaging`; tanpa pustaka luar.

- **Format.** Sumber: JPG/JPEG, PNG, BMP, GIF, TIF/TIFF; WebP dan HEIC/HEIF hanya bila dekoder Windows-nya terpasang. Tujuan: JPG, PNG, BMP, GIF, TIFF; HEIC hanya bila Windows bisa menulisnya (§13 #15; dicek dengan satu kali tulis lalu baca). Format sumber tidak ditawarkan sebagai tujuan.
- **Piksel.** Dibaca sebagai Bgra8 (alfa lurus), dengan rotasi EXIF diterapkan dan warna dikonversi ke sRGB. Format tanpa alfa (JPG, BMP, GIF) menerima gambar yang transparansinya dilebur ke **putih** (tidak hitam). PNG dan TIFF mempertahankan transparansi.
- **Tidak dibawa:** metadata (EXIF, GPS, profil ICC, teks) dan kualitas khusus. Hanya piksel dan DPI yang ditulis; encoder Windows memakai nilai bawaannya, tanpa pengaturan kualitas (§13 #19).
- **Banyak frame.** Hanya frame pertama yang dikonversi; catatan Informational memberi tahu jumlahnya (§6.1 Selesai).
- **Sumber terpotong.** Windows mendekode PNG, JPEG, dan GIF yang terpotong **tanpa galat** dan mengisi bagian yang hilang (sering abu-abu), sehingga verifikasi keluaran saja tidak menangkapnya. `ImageStructure` (Core, portabel) menelusuri struktur file: PNG sampai chunk `IEND` (termasuk 4 byte CRC-nya; nilai CRC tidak diperiksa), JPEG sampai `EOI` (`FFD9`), GIF sampai trailer `3B`, WebP sampai panjang RIFF yang dideklarasikan. Kalau berakhir lebih awal, konversi tetap jalan dan hasilnya membawa Warning. File yang terpotong di dalam header juga dihitung terpotong. BMP dan TIFF tidak diperiksa: Windows sendiri menolaknya (galat dekode). Format lain, dan struktur yang tidak bisa diikuti, tidak dinilai (tanpa catatan).
- **Ukuran.** Gambar dibaca utuh di memori, 4 byte per piksel, dalam satu array .NET. Gambar dengan lebih dari 536.870.911 piksel (`int.MaxValue / 4`, sekitar 537 MP) ditolak sebelum didekode, dibaca dari header. Kehabisan memori (`E_OUTOFMEMORY`, yang datang sebagai `COMException`, bukan `OutOfMemoryException`) saat dekode atau encode juga menjadi galat yang sama. Pesan: `Error.ImageTooLarge` (§6.1 Gagal). Keduanya memakai `ImageTooLargeException`; angka MP dibulatkan ke bilangan bulat terdekat. Halaman PDF yang dirender menjadi gambar (PDF → PNG/JPG/HEIC pada 200 dpi, dan jalur Architecture) memakai batas dan pesan yang sama, dihitung dari ukuran halaman sebelum merender: halaman 14.400 pt (200 inci, ukuran terbesar PDF) berarti 1.600 MP dan ditolak seketika, bukan setelah setengah menit dengan galat memori.
- **Catatan konverter** adalah jalur umum, bukan khusus gambar: `ConversionRequest.Notes` diisi konverter mana pun, dan `ConversionResult.Notes` membawanya ke layar Selesai. Konverter lain boleh memakainya di tahap berikutnya.

#### 6.1.2 Konverter audio dan video (`Windows.Media.Transcoding`)

Dipakai Convert File untuk audio dan video. Hanya memakai `MediaTranscoder` dengan profil bawaan Windows; tanpa pustaka luar dan tanpa jaringan.

- **Format.** Sumber audio: MP3, M4A, WAV, WMA, FLAC. Sumber video: MP4, M4V, MOV, WMV, AVI. Windows mengenali file dari isinya, bukan namanya (MP3 yang dinamai `.wav` tetap terbaca), jadi daftar ini adalah daftar Condec sendiri: hanya jenis yang dicoba pada file asli yang ditawarkan (§13 #21). Tujuan audio: MP3, M4A (AAC), WAV, WMA, FLAC. Tujuan video: MP4 (H.264 + AAC), WMV (VC-1 + WMA). Sumber audio hanya mendapat tujuan audio; sumber video mendapat tujuan video dan tujuan audio (suaranya diambil). Format sumber tidak ditawarkan sebagai tujuan, kecuali MP3, M4A, WMA, MP4, dan WMV (§6.1; `IReencodingConverter`). Urutan daftar: video lebih dulu, lalu audio.
- **Encoder.** Tujuan muncul hanya bila Windows punya encoder untuk setiap trek yang dibutuhkan, ditanyakan lewat `CodecQuery` (jadi Windows edisi N tanpa Media Feature Pack tidak menawarkan satu pun). HEVC dan ALAC tidak ditawarkan: keduanya berekstensi sama dengan MP4 dan M4A biasa, dan daftar format dibangun dari ekstensi (§13 #22). AVI tidak ditawarkan sebagai tujuan: profil AVI bawaan Windows tidak terkompresi (162 MB untuk 3 detik).
- **Kualitas.** Dua pilihan (§6.1), bawaan Windows di balik nama yang jujur. Kualitas audio untuk MP3, M4A, dan WMA memakai profil Windows: Tinggi 192 kbps (48 kHz), Sedang 128 kbps (44,1 kHz), Rendah 96 kbps (44,1 kHz), stereo; hasil dibuktikan dengan membaca bitrate file keluaran. Ukuran video untuk MP4 dan WMV: "Sama dengan sumber" memakai profil Auto; 1080p, 720p, dan 480p menetapkan tinggi gambar (bitrate 18, 9, dan 4,5 Mbps seperti profil standarnya) dengan lebar mengikuti bentuk gambar sumber, dibulatkan ke genap. Windows tidak pernah memperbesar: video yang sudah setinggi itu atau lebih kecil dibiarkan seperti aslinya. Profil ukuran standar Windows memaksa bentuk tetap (video 4:3 menjadi 1280 × 720), itu sebabnya ukuran dihitung sendiri. **Belum diuji:** video yang diputar lewat metadata rotasi (rekaman ponsel tegak); ukurannya dihitung dari ukuran terkode, jadi hasilnya mungkin salah arah (§13 #24). Khusus WAV dan FLAC, yang "tanpa kehilangan", sample rate, jumlah kanal, dan kedalaman bit (16 atau 24) dipertahankan dari sumber; profil bawaan akan mengubahnya menjadi 48 kHz stereo (24 bit untuk FLAC). Kalau Windows menolak kombinasi itu, dipakai profil standar. FLAC buatan Windows tidak menulis di bawah 44,1 kHz (22,05 kHz kembali sebagai 44,1 kHz). WAV → FLAC → WAV memberi sampel yang sama persis (diuji).
- **Dibawa.** Tag audio (judul, artis, album) ikut terbawa (diuji MP3 ke M4A, WMA, FLAC). Hanya satu trek video dan satu trek audio yang ditulis.
- **Keanehan Windows, tercatat.** Encoder WMV menulis file satu detik lebih panjang dari sumbernya (3 detik jadi 4 detik). Durasi FLAC yang dilaporkan Windows tidak bisa dipercaya (2,0 detik untuk file 3,0 detik), maka durasi FLAC dibaca dari header FLAC sendiri (STREAMINFO).
- **Sumber terpotong.** Windows menolak MP4 dan M4A yang terpotong (galat jelas, `Error.Media`), tetapi mengonversi WAV, FLAC, WMA, WMV, dan MP3 yang terpotong tanpa galat dan hasilnya berhenti lebih awal. `MediaStructure` (Core, portabel) memeriksa: WAV dan AVI lewat ukuran RIFF, WMA dan WMV lewat ukuran file di header ASF (kecuali bendera siaran langsung), FLAC lewat total sampel di STREAMINFO dibandingkan dengan panjang hasil (selisih lebih dari 0,25 detik atau 1,5%). Hasilnya Warning `Note.MediaIncomplete`. **MP3 yang terpotong tidak bisa dikenali**: formatnya tidak menyatakan panjang.
- **Verifikasi hasil.** Selain chunk dan hash (§6.1), hasil dibuka lagi lewat Windows: trek audio dan videonya harus jenis yang benar (mis. MP3, AAC, H.264), file audio tidak boleh punya trek video, dan panjangnya harus lebih dari nol. Ini membaca format dan panjang, bukan mendekode setiap sampel.
- **Pengerjaan.** `MediaTranscoder` butuh menulis dengan seek (MP4 dan FLAC menulis ulang headernya), jadi hasil dibuat di `%TEMP%\Condec\media-<id>` lalu disalin ke pipeline pada akhir tahap Encode; folder itu dihapus di akhir, juga saat gagal atau dibatalkan. Progres: Decode "Membuka file" lalu Encode 0 sampai 90% mengikuti transcoder, 90 sampai 100% menyalin hasil. Pembatalan berlaku di setiap titik. Encoder perangkat keras dipakai bila ada.

### 6.2 Upscale Image

```
+-- Kartu kiri (fleksibel) ---------+  +-- Kartu kanan (lebar 380) -------+
| [ikon] foto.jpg  1280 x 720 ...   |  | Mesin render  GPU · RTX...  Ubah |
| +---------------------------+     |  | Skala              [ 4x    v ]   |
| |  kotak HASIL (garis putus) |    |  | Persentase upscale        400%   |
| |  [kotak ASLI]              |    |  | o------------------O  (tick)     |
| +---------------------------+     |  | Resolusi hasil     [ ...     v ] |
| Asli · 1280 x 720  Hasil · ...    |  | Format hasil     (o) PNG ( ) JPG |
+-----------------------------------+  +----------------------------------+
+-- Kartu perkiraan ------------------------------------------------------+
| Resolusi hasil | Perkiraan ukuran file | Perkiraan waktu | RAM  [Upscale dan simpan...] |
+------------------------------------------------------------------------+
```

**Mesin upscale.** Real-ESRGAN x4plus (BSD-3-Clause) sebagai ONNX, dijalankan ONNX Runtime di CPU atau, lewat DirectML, di GPU; seluruhnya di perangkat. Model selalu memperbesar 4×; hasil 4× itu lalu diubah ukurannya dengan Lanczos3 ke ukuran yang dipilih persis (lebih kecil untuk 1,5×–3,5×, lebih besar untuk di atas 4×; di atas 4× detail tambahan berasal dari interpolasi, bukan dari model). PNG dengan transparansi: kanal alfa diubah ukurannya terpisah dengan Lanczos3, warna lewat model. JPG: transparansi dilebur ke putih sebelum masuk model. Metadata tidak dibawa (§13 #19); DPI sumber dipertahankan. Gambar beranimasi: hanya bingkai pertama, dengan catatan. Model dicek SHA-256-nya saat aplikasi mulai; bila hilang atau rusak, halaman menampilkan `InfoBar` Error "Upscale tidak tersedia" dengan pesan yang sesuai (model tidak ada / model rusak); gambar masih bisa dipilih, tetapi skala, slider, resolusi, format, dan tombol "Upscale dan simpan…" nonaktif.

**Belum ada gambar:** kartu tunggal sebagai area seret: ikon gambar, "Seret gambar ke sini", tombol "Pilih gambar…". Hanya satu gambar; menyeret beberapa file atau folder memberi pesan "Seret satu gambar saja.". Sumbernya format gambar yang bisa dibaca konverter gambar (§6.1.1); format lain ditolak dengan pesan berisi daftar format.

**Kartu kiri**
- Baris info: ikon gambar 32, nama (BodyStrong), Caption "W × H · X,X MP · ukuran file", `HyperlinkButton` "Ganti".
- Diagram ukuran proporsional (rasio 16:9 mengikuti gambar): kotak besar bergaris putus-putus = hasil, kotak kecil di pojok kiri atas = asli, lebarnya 100/skala persen. Legenda: "Asli · W × H" dan "Hasil · W × H".

**Kartu kanan, berurutan**
1. **Mesin render:** Caption "Mesin render", nilai BodyStrong ("GPU · `<nama>`", "CPU · `<nama>`", atau "NPU · `<nama>`") dari Settings, `HyperlinkButton` "Ubah di Settings". Mesin NPU belum bisa menjalankan model (§13 #8): bila NPU dipilih, Caption di bawahnya "NPU belum bisa menjalankan model ini, jadi `<GPU/CPU>` yang merender." dan perkiraan waktu memakai mesin yang benar-benar merender.
2. **Skala:** label "Skala", `ComboBox` dengan item 2×, 4×, 8×, 16× (keterangan kanan "200%", "400%", dst.). Item di atas batas efektif (§7) nonaktif dengan keterangan "Dikunci". Kalau skala berasal dari slider dan bukan preset, ComboBox menampilkan "Kustom 3,5×" lewat `PlaceholderText`.
3. **Persentase upscale:** label dan nilai ("400%", BodyStrong). `Slider` Minimum=150, Maximum = batas efektif × 100, StepFrequency=50, TickFrequency=50, tick di bawah. Di bawah slider: Caption "150%" (kiri) dan nilai maksimum (kanan). Slider dan ComboBox Skala saling menyinkronkan.
4. Bila ada skala terkunci: Caption berwarna peringatan dengan ikon gembok "Dikunci: 8×, 16×. `<alasan>`." (alasan dari §7.4).
5. **Resolusi hasil:** `ComboBox`, tampilan "Full HD · 1920 × 1080" atau "Kustom · W × H". Preset: Full HD 1920 × 1080, 2K (QHD) 2560 × 1440, 4K (UHD) 3840 × 2160, 5K 5120 × 2880, 8K (UHD) 7680 × 4320 (untuk sumber 16:9). Preset menetapkan **sisi terpanjang** (1920, 2560, 3840, 5120, 7680); sisi lain mengikuti bentuk gambar, jadi gambar tegak 1080 × 1920 pada "Full HD" tetap tegak dan bentuk tidak pernah berubah `[ASUMSI]`. Item nonaktif bila skala yang dibutuhkan (sisi terpanjang target ÷ sisi terpanjang sumber) < 1,5 atau di atas batas efektif. Memilih skala atau menggeser slider memilih preset yang ukurannya persis sama, selain itu "Kustom".
6. **Format hasil:** `RadioButtons` PNG | JPG (horizontal).

**Kartu perkiraan** (grid 4 kolom, nilai `BodyLargeStrongTextBlockStyle`, label dan keterangan Caption):
- "Resolusi hasil" — "5120 × 2880" / "14,7 MP · 5K"
- "Perkiraan ukuran file" — "± 23,9 MB" / "PNG"
- "Perkiraan waktu render" — "± 7 detik" / "GPU · RTX 4060 Laptop"
- "RAM dibutuhkan" — "32 GB" / "Terpasang 32 GB, cukup" (warna sukses) atau "Terpasang N GB, kurang" (warna galat)
- Tombol aksen "Upscale dan simpan…" di kanan, **nonaktif** bila upscale tidak tersedia atau RAM kurang.

**Upscale tidak tersedia** (batas efektif < 1,5): `InfoBar` Error di atas kartu. Judul "Perangkat ini belum memenuhi syarat upscale". Pesan "Upscale butuh RAM terpasang minimal 8 GB untuk hasil sampai 2K. Perangkat ini hanya punya N GB." Bila RAM cukup tetapi gambarnya terlalu besar untuk diperbesar 1,5× (syarat RAM §7.2 atau batas satu gambar di memori, §13 #20), pesannya "Gambar ini terlalu besar untuk diperbesar di perangkat ini. `<alasan §7.4>`." Skala, slider, dan resolusi nonaktif.

**Proses:** kartu tunggal. Judul "Meng-upscale `<nama>`", Caption "`<skala>` · `<W × H>` · `<mesin>`", "Batal", ProgressBar + persen, tahap: "Menyiapkan mesin render" (0–10%; keterangan "Mengukur kecepatan mesin (sekali saja)" saat benchmark §8 berjalan, lalu "Memuat model"), "Memproses tile" (10–90%, "Tile n dari N"), "Menyimpan hasil" (90–96%, "Menulis .png"), "Verifikasi integritas" (96–100%, "Menghitung SHA-256"). Tile masukan 128 × 128 px dengan tepi 10 px yang dibuang setelah diproses (108 px berguna per tile, 432 px di hasil); tepi gambar diisi pantulan supaya semua tile berukuran sama. Ukuran ini hasil pengukuran di GPU terintegrasi mesin uji (2026-10-02, DirectML): tile 128 menghasilkan 0,47 MP/detik, tile 256 hanya 0,32; tile 48–192 tidak lebih cepat dari 128. Hasil bertile dengan tepi 10 px sama dengan hasil satu gambar utuh pada 52,9 dB PSNR (tanpa sambungan yang terlihat). Bila GPU gagal menjalankan model, render pindah ke CPU dengan catatan Informational "Kartu grafis tidak bisa menjalankan model upscale, jadi CPU yang merender gambar. Ini lebih lama."

**Selesai:** `InfoBar` Success "Upscale selesai" / "Gambar disimpan dan lolos cek integritas." Detail: Lokasi, Resolusi ("W × H → W' × H' (4×)"), Mesin, Integritas (sama dengan Convert File). Catatan konversi (mis. bingkai pertama saja, render pindah ke CPU) tampil sebagai `InfoBar` di bawah detail. Tombol: "Buka file" (aksen), "Tampilkan di folder", `HyperlinkButton` "Upscale gambar lain".

**Gagal:** `InfoBar` Error "Upscale gagal" dengan pesan dari §6.1 (mis. memori habis = gambar terlalu besar), nama file, tombol "Coba lagi" (aksen) dan "Ubah pilihan". Batal tidak meninggalkan file dan kembali ke pilihan dengan pesan "Upscale dibatalkan. Tidak ada file yang disimpan."

Nama file hasil: `<nama>-<W>x<H>.<ext>`. Upscale tidak dicatat di daftar Riwayat Convert File `[ASUMSI]` (§13 #10).

### 6.3 Architecture

> **Sementara (tahap 3–6):** halaman ini memuat kartu konverter §6.1 untuk DXF dan DWG saja: PDF, PNG, JPG, HEIC dan lainnya ke DXF/DWG, serta DXF/DWG ke gambar atau PDF, dengan opsi dan perilaku yang sama seperti sebelum redesign. Layar di bawah dibangun di tahap 7. Riwayatnya masuk ke daftar Riwayat di Convert File (menyentuh §13 #10).

`SelectorBar` di atas dengan dua item: **"Gambar, PDF, DXF → DWG"** dan **"DWG, DXF → Gambar, PDF"**.

#### 6.3.1 Arah "ke CAD"
Sumber yang didukung: DXF, PNG, JPG, HEIC, HEIF, PDF. Jenis sumber menentukan jalur:
- DXF dan PDF vektor: konversi langsung, tanpa vektorisasi dan tanpa saran upscale.
- PNG, JPG, HEIC, HEIF, dan PDF hasil scan: perlu vektorisasi.

**Keadaan (urutan alur)**

| Keadaan | Tampilan |
|---|---|
| Kosong | Kartu besar (tinggi 400) dengan garis putus-putus: ikon denah 40, "Seret gambar, PDF, atau DXF ke sini", Caption "DXF, PNG, JPG, HEIC, HEIF, PDF → DWG", tombol aksen "Pilih file…". |
| Objek tidak jelas | `InfoBar` Warning di atas pratinjau. Pratinjau agak buram dengan penanda "!" di area yang tidak jelas. |
| Perangkat tidak memenuhi syarat | `InfoBar` Error, bukan Warning. |
| Sedang upscale | `InfoBar` Informational dengan `ProgressBar`, persen, dan "Batal". |
| Siap | `InfoBar` Success. Pratinjau tajam. |
| Siap tanpa upscale | `InfoBar` Warning "Tanpa upscale". Pratinjau tetap buram. |

**Teks InfoBar**
- Warning: judul "Banyak objek tidak jelas (N area)". Pesan "Garis putus dan buram sulit dibaca, jadi hasil CAD bisa berantakan. Upscale dulu supaya lebih rapi. Perangkat ini mampu sampai `<batas efektif>`." Aksi: `SplitButton` "Upscale `<n>×` dulu" (menu: 2×, 4×, 8×; yang melewati batas efektif gambar itu nonaktif dengan keterangan "Dikunci"; bawaan 2×) dan `Button` "Lewati".
- Error: judul "Perangkat ini belum memenuhi syarat upscale". Pesan "Upscale 2× untuk gambar ini butuh RAM terpasang `<X>` GB, sedangkan perangkat ini `<Y>` GB. Kamu tetap bisa lanjut tanpa upscale, tapi objek yang tidak jelas mungkin tidak terbaca." Aksi: "Lanjut tanpa upscale".
- Sedang upscale: judul "Meng-upscale `<n>×`", pesan `<mesin>`, ProgressBar, persen, "Batal".
- Success: judul "Gambar siap dikonversi", pesan "Setelah upscale `<n>×`, 94% objek terbaca jelas. Periksa daftar objek di kanan." (angka 94% dihitung dari hasil analisis nyata, bukan tetap).
- Warning tanpa upscale: judul "Tanpa upscale", pesan "N area tidak jelas mungkin tidak terbaca atau berbentuk salah di hasil CAD." Aksi "Upscale `<n>×` sekarang" bila perangkat mampu.
- Ambang "banyak objek tidak jelas" `[ASUMSI]`: 5 area atau lebih ditandai tidak jelas. Ukur dengan metrik lokal (mis. variansi Laplacian dan ketebalan garis), lalu kalibrasi.

**Pratinjau dan daftar objek** (dua kolom: pratinjau fleksibel, panel kanan lebar 380)
- Kartu pratinjau: nama file (BodyStrong), Caption ukuran (setelah upscale: "3200 × 2400 · 7,7 MP · setelah upscale 2×"), Caption kanan jenis ("Gambar raster · perlu vektorisasi" atau "Vektor · langsung dirender"). Kertas rasio 3:2, putih. Legenda: Dijadikan CAD, Tidak dijadikan CAD, Area tidak jelas.
- Panel kanan "Objek terdeteksi" (BodyStrong) dengan Caption "Hilangkan centang untuk objek yang tidak dijadikan CAD." `ListView` Multiple, tinggi item 44:

| Objek | Jumlah | Keterangan saat dicentang | Awal |
|---|---|---|---|
| Dinding dan garis denah | 126 garis | Jadi LINE dan POLYLINE | dicentang |
| Pintu dan jendela | 14 objek | Jadi ARC dan garis ganda | dicentang |
| Teks dan dimensi | 22 teks | Jadi TEXT (hasil OCR, periksa ulang) | dicentang |
| Logo dan kop | 1 objek | Jadi objek CAD | tidak |
| Tabel keterangan | 1 tabel | Jadi objek CAD | tidak |

  Tidak dicentang → keterangan "Tidak dijadikan CAD", kotak di pratinjau jadi putus-putus abu dan terhapus samar. Dicentang → kotak biru. Jumlah dan jenis objek dihitung dari analisis nyata, tabel ini hanya contoh.
- Garis pemisah. Baris "Format CAD": `RadioButtons` DWG | DXF. Baris "Skala gambar": Caption peringatan "Belum dikalibrasi" dan `Button` "Kalibrasi…". Gambar raster tidak punya skala asli, jadi pengguna menandai dua titik dan mengisi panjang aslinya.
- Bawah halaman: kiri Caption perisai "Diproses sepenuhnya di perangkat ini. Tidak ada file yang diunggah."; kanan `Button` "Pilih file lain" dan tombol aksen "Konversi ke DWG…". Selama belum siap atau dilewati, label tombol "Tetap konversi ke DWG…". Nonaktif saat sedang upscale.
- Keluaran: DWG atau DXF dengan layer WALLS, OPENINGS, TEXT. Nama default `<nama>.dwg`.

#### 6.3.2 Arah "dari CAD"
- Kosong: area seret "Seret file DWG atau DXF ke sini", Caption "DWG, DXF → PNG, JPG, PDF".
- Siap: `InfoBar` Informational "File DWG terbaca" / "3 layer, satuan milimeter. Layer yang dimatikan tidak ikut di hasil." Pratinjau tajam tanpa penanda. Layer yang dimatikan tersembunyi di pratinjau.
- Panel "Opsi hasil" (BodyStrong): lima `RadioButtons` horizontal:

| Opsi | Pilihan | Awal | Aturan |
|---|---|---|---|
| Format hasil | PDF, PNG, JPG | PDF | |
| Ukuran kertas | A4, A3 | A3 | |
| Resolusi (DPI) | 150, 300, 600 | 300 | Nonaktif untuk PDF (vektor) |
| Latar | Putih, Transparan | Putih | Transparan hanya bila format PNG |

  Lalu garis pemisah, "Layer" (BodyStrong) dengan `ListView` Multiple (Dinding dan garis denah, Pintu dan jendela, Teks dan dimensi; keterangan "Ditampilkan"/"Disembunyikan"), dan Caption "Perkiraan hasil: …".
- Perkiraan hasil `[ASUMSI]`: piksel = mm ÷ 25,4 × DPI per sisi (A3 lanskap 420 × 297 mm, A4 lanskap 297 × 210 mm). PNG ≈ 0,1 byte/piksel, JPG ≈ 0,14 byte/piksel, PDF ≈ 0,3 MB. Tampilan: "PDF vektor · ± 0,3 MB" atau "4961 × 3508 px · ± 1,7 MB".
- Tombol aksen "Konversi dan simpan…".

### 6.4 Settings

`SelectorBar` dengan dua item: **"Render dan performa"** dan **"Umum"**. Tiap setelan adalah baris kartu (`Border` + `Grid`, sudut 4, tinggi minimum 68, padding 16, jarak antar baris 4): judul (Body) dan deskripsi (Caption sekunder) di kiri, kontrol di kanan. Judul grup memakai `BodyStrongTextBlockStyle` dengan jarak atas 16.

#### Tab "Render dan performa"

| Grup / baris | Kontrol | Isi |
|---|---|---|
| Perangkat ini | Kartu info (grid 2 × 2) | Prosesor, RAM terpasang, GPU, NPU ("Tidak terdeteksi" bila tidak ada). Kanan: Caption "Batas upscale perangkat" dan angka `TitleTextBlockStyle` ("4×"). |
| Render mode | `Expander` | Header: "Render mode" + deskripsi "Mesin yang dipakai untuk upscale dan vektorisasi. GPU paling cepat, CPU selalu tersedia." + nilai terpilih di kanan. Isi: `RadioButtons` vertikal GPU / CPU / NPU, masing-masing dengan nama perangkat dan status ("Terdeteksi · tercepat", "Selalu tersedia · paling lambat", "Terdeteksi" / "Tidak terdeteksi"). NPU nonaktif bila tidak terdeteksi. |
| Batas upscale | `ComboBox` (lebar 160) | 2×, 4×, 8×, 16×. Item di atas kemampuan perangkat nonaktif ("Di atas batas perangkat"). Deskripsi: "Skala maksimum yang boleh dipilih di Upscale Image dan Architecture. Perangkat ini mampu sampai N×." |
| Batas memori | `Slider` (lebar 220) + nilai | Minimum 4, Maximum RAM terpasang (GB), StepFrequency 1, TickFrequency 4. Nilai "N GB" di kanan. Deskripsi: "RAM maksimum yang boleh dipakai Condec. Batas kecil membuat render lebih lambat, bukan lebih buruk kualitasnya." Di bawahnya Caption: "Syarat RAM terpasang menurut resolusi hasil: HD–2K 8 GB · 2K–4K 16 GB · 4K–8K 32 GB · di atas 8K 64 GB." |
| Render dump | `Button` "Bersihkan cache" | Deskripsi: "Hapus cache render (tile dan data sementara). Ukuran sekarang: 1,8 GB." Setelah dibersihkan: "Cache kosong. Ukuran sekarang: 0 MB." dan tombol nonaktif. Folder cache: `%LOCALAPPDATA%\Condec\cache` (belum ada isinya sampai tahap 6). Bila ada file yang sedang dipakai: "Sebagian file cache sedang dipakai dan tidak terhapus. Ukuran sekarang: …". |

#### Tab "Umum"

| Grup / baris | Kontrol | Isi |
|---|---|---|
| Tampilan → Tema aplikasi | `ComboBox` | Terang, Gelap, Ikuti sistem. Diterapkan lewat `RequestedTheme` di elemen root ("Ikuti sistem" = `ElementTheme.Default`). |
| Tampilan → Latar Mica | `ToggleSwitch` | "Latar jendela ikut warna wallpaper (Windows 11)." Label "Aktif"/"Nonaktif". |
| Log → Simpan log aktivitas | `ToggleSwitch` | "Catatan proses konversi dan upscale untuk mencari masalah. Tidak berisi isi file dan tidak pernah dikirim ke mana pun." |
| Log → Tingkat detail log | `ComboBox` | Error, Info, Debug. Nonaktif saat log mati. Deskripsi: "Debug mencatat paling banyak dan bisa membuat file log besar." |
| Log → Folder log | `Button` "Buka folder", `Button` "Hapus log" | `%LOCALAPPDATA%\Condec\logs`. Buka folder lewat `Launcher.LaunchFolderAsync`. Satu file per hari, `condec-yyyy-MM-dd.log`. Isi log: waktu, tingkat, dan peristiwa (mulai, hasil, tahap, jenis galat); tanpa isi file dan tanpa path file. Hasil "Hapus log" dan galat membuka file ditampilkan di `InfoBar` Informational di atas tab Umum. |
| Tentang | `Button` "Lisensi pihak ketiga" | "Condec 0.1.0" (versi dari manifest paket, jadi build portable menampilkan angka yang sama) / "Open source, GPL-3.0-or-later. Semua proses berjalan offline di perangkat ini." Tombol membuka `THIRD-PARTY-NOTICES.md` (ikut di paket dan build portable) di Notepad, karena Windows tidak punya aplikasi bawaan untuk `.md`. |

Catatan perilaku Settings yang sudah dipasang (tahap 3):
- Baris Render mode: item GPU/NPU yang tidak terdeteksi nonaktif dengan status "Tidak terdeteksi". Pilihan tersimpan yang tidak tersedia lagi dibaca sebagai GPU atau CPU (aturan di bawah).
- Batas upscale: skala di atas kemampuan perangkat tetap ada di daftar, diredupkan dengan keterangan "Di atas batas perangkat", dan tidak bisa dipilih (pilihan dikembalikan). `ComboBoxItem` tidak dinonaktifkan karena itu menutup daftar yang sedang terbuka saat item berfokus.
- Teks "Syarat RAM terpasang menurut resolusi hasil" dirangkai dari tabel `CapabilityPolicy.RamTiers` (§7.2), bukan ditulis ulang.
- Perubahan tema berlaku langsung tanpa restart, termasuk warna tombol caption.

#### Nilai awal dan penyimpanan
Disimpan di `ApplicationData.Current.LocalSettings` bila aplikasi terpasang sebagai MSIX, dan di `%LOCALAPPDATA%\Condec\settings.json` bila dijalankan sebagai build portable (tanpa identitas paket). Kuncinya sama di keduanya.

| Kunci | Nilai awal |
|---|---|
| `render.mode` | GPU bila terdeteksi, selain itu CPU |
| `limit.scale` | batas upscale perangkat (§7.1) |
| `limit.memoryGb` | 75% RAM terpasang, dibulatkan |
| `ui.theme` | Ikuti sistem |
| `ui.mica` | Aktif |
| `log.enabled` | Aktif |
| `log.level` | Info |

Aturan:
- Mode tersimpan yang tidak tersedia lagi (mis. NPU dicabut) → pindah ke GPU, atau CPU bila tidak ada GPU.
- `limit.scale` dan `limit.memoryGb` dijepit ke kemampuan perangkat saat dibaca.
- Perubahan Render mode, Batas upscale, dan Batas memori langsung berlaku di Upscale Image dan Architecture tanpa restart.

## 7. Aturan batas perangkat

Sumber logika tunggal: kelas `CapabilityPolicy` di `Condec.Core`. UI hanya menampilkan hasilnya.

### 7.1 Batas dari GPU `[ASUMSI]`
Nilai awal, ganti setelah benchmark. Diambil dari memori adapter lewat DXCore.

| Perangkat | Batas skala |
|---|---|
| GPU terintegrasi atau VRAM < 6 GB | 2× |
| VRAM 6–11 GB | 4× |
| VRAM 12–15 GB | 8× |
| VRAM ≥ 16 GB | 16× |

Batas yang sama berlaku untuk mode CPU dan NPU (batas perangkat, bukan batas mesin).

Memori adapter dibulatkan ke GB terdekat sebelum dibandingkan (kartu 12 GB melapor 11,99 GB). Tanpa GPU yang terdeteksi, batasnya 2×, sama dengan GPU terintegrasi `[ASUMSI]`. Adapter perangkat lunak (Microsoft Basic Render Driver) tidak dihitung sebagai GPU.

### 7.2 Syarat RAM menurut resolusi hasil
Dihitung dari **RAM terpasang**, bukan RAM yang dipakai Condec. Nilainya dari `GetPhysicallyInstalledSystemMemory` (tabel firmware), dibulatkan ke GB terdekat; bila Windows tidak bisa menjawab (mis. mesin virtual), dari `GlobalMemoryStatusEx` yang sedikit lebih kecil (laptop 8 GB melapor ±7,7 GB, jadi pembulatan itu perlu).

| Resolusi hasil (piksel) | RAM terpasang minimal | Label | Tag |
|---|---|---|---|
| ≤ 2560 × 1440 | 8 GB | HD–2K | sesuai permintaan |
| ≤ 3840 × 2160 | 16 GB | 2K–4K | sesuai permintaan |
| ≤ 7680 × 4320 | 32 GB | 4K–8K | `[ASUMSI]` |
| lebih besar | 64 GB | di atas 8K | `[ASUMSI]` |

### 7.3 Batas efektif
```
tierRam      = tingkat terbesar dengan gb <= RAM terpasang
ramMax       = (RAM < 8 GB) ? 0
             : (tierRam tanpa batas piksel) ? 16
             : sqrt(maxPixelTierRam / (lebarSumber * tinggiSumber))
raw          = min(batasGpu, batasDiSettings, ramMax, 16)
efektif      = floor(raw * 2) / 2          // kelipatan 0,5
tersedia     = efektif >= 1.5
```
- Slider: 150%–(efektif × 100), langkah 50.
- Item ComboBox Skala 2×/4×/8×/16×: nonaktif bila lebih besar dari efektif.
- Preset resolusi: nonaktif bila (lebar target ÷ lebar sumber) < 1,5 atau > efektif.
- Skala awal di Upscale Image: min(4, efektif). Di Architecture, saran upscale awal 2× dan hanya ditawarkan bila efektif ≥ 2.
- "Tersedia" = false: lihat InfoBar Error di §6.2 dan §6.3.1.

### 7.4 Teks alasan (digabung dengan " · ")
- Batas GPU menjadi penentu: "Batas perangkat N×"
- Batas di Settings menjadi penentu dan lebih kecil dari batas GPU: "Batas di Settings N×"
- Syarat RAM menjadi penentu: "RAM X GB: hasil maksimal `<label>`" (label dari tabel §7.2)
- RAM terpasang di bawah 8 GB: "RAM X GB: butuh minimal 8 GB"

"Menjadi penentu" = nilainya sama dengan `raw`. Beberapa alasan bisa muncul bersamaan.

### 7.5 Batas memori (Settings)
Tidak menutup akses skala. Batas kecil memperlambat render karena tile lebih kecil dan paralelisme lebih rendah `[ASUMSI]`: faktor waktu ×1,0 bila ≥ 12 GB, ×1,3 bila 8–11 GB, ×1,8 bila < 8 GB.

Kenyataan di tahap 6: renderer memakai tile 128 px tetap (§6.2) dan **tidak membaca batas ini**, jadi faktor di atas hanya memperbesar perkiraan waktu, bukan waktu nyata (lihat §13 #26).

## 8. Estimasi

| Besaran | Rumus | Tag |
|---|---|---|
| Piksel hasil | `round(W × skala) × round(H × skala)` | |
| Ukuran file PNG | piksel × 1,7 byte | `[ASUMSI]` |
| Ukuran file JPG | piksel × 0,4 byte | `[ASUMSI]` |
| Waktu render | `MP kerja ÷ throughput(mesin) × faktorMemori`. MP kerja = jumlah tile × 0,186624 MP (satu tile 108 px berguna, 4× = 432 × 432 px). Tile di tepi kanan dan bawah dihitung utuh karena model tetap memproses tile penuh. Tidak bergantung pada skala yang dipilih, karena model selalu bekerja 4× (§6.2) | |
| Throughput | hasil benchmark singkat pada eksekusi pertama (tile 128 px yang sama dengan render), disimpan per mesin dan nama perangkat; ganti GPU atau NPU berarti diukur ulang. Protokol: 1 tile pemanasan tanpa hitung waktu, lalu 3 tile diukur; satuannya MP berguna per detik. Diukur saat upscale pertama dengan mesin itu, pada tahap "Menyiapkan mesin render" | protokol `[ASUMSI]` |
| Format waktu | < 1 dtk: "< 1 detik"; < 60: "N detik"; < 60 mnt: "N menit M detik"; selain itu "N jam M menit" | |

Selalu tampil dengan awalan "± ". Angka throughput di mock hanya contoh dan tidak boleh dipakai di aplikasi nyata. Sebelum ada hasil benchmark untuk mesin terpilih, "Perkiraan waktu render" menampilkan "—" dan keterangan "Belum diukur", bukan angka karangan.

## 9. Perangkat contoh dan test vector

Tiga profil untuk mock dan unit test:

| Profil | CPU | RAM | GPU | NPU | Batas GPU |
|---|---|---|---|---|---|
| sedang | Intel Core Ultra 7 155H | 32 GB | NVIDIA GeForce RTX 4060 Laptop, 8 GB | Intel AI Boost | 4× |
| tinggi | AMD Ryzen 9 7950X | 64 GB | NVIDIA GeForce RTX 4090, 24 GB | tidak ada | 16× |
| dasar | Intel Core i5-1135G7 | 8 GB | Intel Iris Xe (terintegrasi) | tidak ada | 2× |

**Test vector `CapabilityPolicy.EffectiveMaxScale`** (batas di Settings = batas GPU kecuali disebut lain). Wajib lulus:

| Sumber | RAM | Batas GPU | Batas Settings | ramMax | Efektif | Tersedia |
|---|---|---|---|---|---|---|
| 1280 × 720 | 32 GB | 4× | 4× | 6,0 | 4,0 | ya |
| 1280 × 720 | 64 GB | 16× | 16× | 16 | 16,0 | ya |
| 1280 × 720 | 8 GB | 2× | 2× | 2,0 | 2,0 | ya |
| 1280 × 720 | 16 GB | 4× | 4× | 3,0 | 3,0 | ya |
| 1280 × 720 | 32 GB | 4× | 2× | 6,0 | 2,0 | ya |
| 3840 × 2160 | 32 GB | 4× | 4× | 2,0 | 2,0 | ya |
| 3840 × 2160 | 16 GB | 4× | 4× | 1,0 | 1,0 | tidak |
| 1600 × 1200 | 32 GB | 4× | 4× | 4,157 | 4,0 | ya |
| 1600 × 1200 | 8 GB | 2× | 2× | 1,386 | 1,0 | tidak |
| 1600 × 1200 | 4 GB | 2× | 2× | 0 | 0 | tidak |

Turunan: RAM yang dibutuhkan untuk upscale 2× gambar 1600 × 1200 = 16 GB. Upscale 4× gambar 1280 × 720 → 5120 × 2880 = 14,7 MP, PNG ± 23,9 MB, butuh tingkat 32 GB.

## 10. Aksesibilitas

- Semua tombol ikon punya `AutomationProperties.Name` ("Tampilkan di folder", "Buka atau tutup menu navigasi", dll.). Tombol hamburger diberi nama ini lewat bagian template `TogglePaneButton`; tanpa itu NavigationView memakai nama bawaannya ("Close Navigation") dalam bahasa sistem.
- Pilihan yang isinya dua baris teks (Render mode, daftar skala) punya nama gabungan yang dibacakan, mis. "NPU, Tidak terdeteksi" dan "4×, Di atas batas perangkat".
- `ProgressBar` punya nama ("Kemajuan konversi", "Kemajuan upscale"). `Slider` punya nama ("Persentase upscale", "Batas memori dalam GB").
- Navigasi keyboard penuh. Urutan fokus mengikuti urutan visual, kiri ke kanan, atas ke bawah.
- Status tidak boleh hanya berupa warna: sukses/peringatan/galat selalu disertai ikon dan teks.
- Mode high contrast didukung (otomatis bila hanya memakai ThemeResource).
- Item yang dikunci menjelaskan alasannya dalam teks, bukan hanya tampak abu-abu.

## 11. Peta kode (saran)

```
Condec/                     WinUI 3
  MainWindow.xaml           TitleBar + NavigationView + Frame      (§3)
  Views/ConvertPage.xaml    (§6.1)
  Views/UpscalePage.xaml    (§6.2)
  Views/ArchitecturePage.xaml (§6.3)
  Views/SettingsPage.xaml   (§6.4)
  ViewModels/               satu per halaman
Condec.Core/
  Pipeline/                 pipeline 4 tahap, verifikasi chunk dan integritas
  Converters/               IConverter, registry
  Devices/                  DeviceProfile (DXCore), CapabilityPolicy      (§7)
  Upscale/                  tile, estimasi                                 (§8)
  Architecture/             vektorisasi, klasifikasi objek, tulis DWG/DXF
  Settings/                 kunci dan nilai awal                           (§6.4)
Condec.Tests/
  CapabilityPolicyTests     test vector §9
```

## 12. Referensi visual (kanvas desain)

Kanvas: https://claude.ai/artifact/Hai2GMkLkn5Z8SwnWeXR75 (dibuka lewat akun pemilik). Board 1 bisa di-Play.

| Board | Isi | Bagian |
|---|---|---|
| 1–4 | Convert File: awal, pilih format, proses, selesai | §6.1 |
| 5–7 | Upscale Image: perangkat sedang (4×), perangkat dasar (2×), menu ditutup + GPU kelas atas (16×) | §6.2, §3.2 |
| 8–12 | Architecture: kosong, objek tidak jelas, perangkat tidak memenuhi syarat, siap, DWG ke gambar | §6.3 |
| 13–14 | Settings: render dan performa, umum (tema gelap) | §6.4 |
| 15 | Peta kontrol WinUI 3 | §5 |

## 13. Asumsi dan pertanyaan terbuka

| # | Hal | Status |
|---|---|---|
| 1 | Tingkat RAM 32 GB (4K–8K) dan 64 GB (di atas 8K) | `[ASUMSI]` usulan, hanya 8 dan 16 GB yang diminta pemilik |
| 2 | Ambang batas GPU 2×/4×/8×/16× (§7.1) | `[ASUMSI]`, ganti setelah benchmark |
| 3 | RAM dihitung dari RAM terpasang, bukan RAM yang dipakai Condec | `[ASUMSI]` |
| 4 | Ukuran thumb `Slider` 20 px | `[ASUMSI]`, tidak ada di source yang dibaca |
| 5 | Lebar pill `SelectorBar` | `[TERBUKA]` |
| 6 | Ukuran minimum jendela 900 × 640 | `[TERBUKA]`, usulan ini sudah dipakai (tahap 3); konfirmasi atau ganti |
| 7 | Versi DWG yang ditulis ACadSharp | diputuskan: AC1015 (AutoCAD 2000). ACadSharp 3.8.0 menulis DWG AC1014, AC1015, AC1018, AC1024, AC1027, AC1032 |
| 8 | Model upscale (lisensi kode dan bobot harus kompatibel GPL-3.0) dan apakah bisa jalan di NPU | Model diputuskan pemilik 2026-10-02: hanya Real-ESRGAN x4plus (BSD-3-Clause), ekspor ONNX pihak ketiga (SkillSafe) yang diverifikasi identik dengan bobot resmi (THIRD-PARTY-NOTICES.md). NPU: `[TERBUKA]`. Paket ONNX Runtime DirectML tidak bisa diarahkan ke NPU, dan belum ada perangkat ber-NPU untuk dicoba; pilihan NPU merender di GPU (atau CPU) dengan keterangan (§6.2) |
| 9 | OCR teks gambar: `Windows.Media.Ocr`, bahasa terpasang | `[TERBUKA]`, cek dokumentasi |
| 10 | Apakah riwayat Upscale dan Architecture masuk ke satu daftar Riwayat | `[TERBUKA]`. Sementara (tahap 3): konversi di Architecture tercatat di daftar yang sama dengan Convert File (satu `history.json`), supaya riwayat konversi DXF/DWG yang sudah ada tidak hilang; daftar hanya ditampilkan di Convert File. Tahap 6: hasil Upscale **tidak** dicatat di daftar itu `[ASUMSI]`, karena daftarnya hanya tampil di Convert File dan berisi konversi format; menambahkan upscale menunggu jawaban pertanyaan ini. |
| 11 | Ambang "banyak objek tidak jelas" (5 area) | `[ASUMSI]`, kalibrasi |
| 12 | Perkiraan ukuran file (§8) dan hasil DWG → gambar (§6.3.2) | `[ASUMSI]` |
| 13 | Win2D untuk render DWG | `[TERBUKA]`, hanya kalau kontrol stock tidak cukup |
| 16 | Tanpa GPU terdeteksi dihitung seperti GPU terintegrasi (batas 2×) | `[ASUMSI]` |
| 17 | Protokol benchmark: 1 tile pemanasan + 3 tile diukur, disimpan per mesin dan nama perangkat | `[ASUMSI]`. Terbukti di mesin uji (2026-10-03): GPU terintegrasi 0,32 MP/detik, CPU 0,061 MP/detik. Gambar 640 × 360 (24 tile) di CPU: perkiraan dari jumlah tile 73 detik, render nyata 61 detik (tanpa faktor memori) |
| 18 | DXCore (GPU/NPU) di perangkat nyata | Terbukti jalan (2026-10-02, laptop pemilik, 51 ms): "AMD Ryzen 5 5600H with Radeon Graphics", RAM 8 GB, GPU "AMD Radeon(TM) Graphics" terintegrasi dengan memori khusus 496 MB (cocok dengan `Win32_VideoController`), NPU tidak ada, batas perangkat 2×. **Belum dicoba** pada GPU diskrit dan pada perangkat dengan NPU; `[TERBUKA]` untuk dua kasus itu |
| 19 | Konverter gambar tidak membawa metadata (EXIF, GPS, profil ICC); warna dikonversi ke sRGB, transparansi dilebur ke putih untuk format tanpa alfa (§6.1.1) | Diputuskan pemilik untuk beta: tidak dibawa (lebih aman untuk privasi). Opsi "pertahankan metadata" (bawaan mati, JPG/PNG/TIFF) hanya ditambah bila diminta. |
| 20 | Batas gambar 536.870.911 piksel (`int.MaxValue / 4`, §6.1.1) dan kehabisan memori dianggap "terlalu besar" | `[ASUMSI]`. Batas itu adalah batas satu array .NET; batas nyata bergantung memori mesin dan belum diukur untuk gambar di antara 100 dan 537 MP |
| 21 | Daftar sumber audio/video (§6.1.2): hanya MP3, M4A, WAV, WMA, FLAC, MP4, M4V, MOV, WMV, AVI. `.mkv`, `.ogg`, `.opus`, `.aac`, `.3gp`, `.webm`, dan lain-lain belum ditawarkan karena belum dicoba pada file asli, walau mesin ini punya dekodernya (Opus, FFmpeg lewat Web Media Extensions, AV1, VP9, HEVC) | `[TERBUKA]`. Tambahkan setelah pemilik memberi contoh file |
| 22 | HEVC dan ALAC sebagai tujuan: sama ekstensi dengan MP4 dan M4A, jadi butuh konsep "profil" selain ekstensi di daftar format | `[TERBUKA]` |
| 23 | Opsi kualitas audio dan ukuran video (§6.1, §6.1.2) | Diputuskan pemilik 2026-10-02 ("justru itu yang aku butuhkan"). Tiga tingkat audio dan tiga ukuran video adalah usulan; bitrate kustom tidak ada |
| 24 | Ukuran video yang dipilih untuk video dengan metadata rotasi (rekaman ponsel tegak) | `[TERBUKA]`, belum diuji; butuh contoh file dari ponsel |
| 25 | Data latih Real-ESRGAN (DIV2K, Flickr2K, OST) punya ketentuan sendiri yang tidak dijelaskan repo Real-ESRGAN untuk bobotnya | Diputuskan pemilik 2026-10-02: dicatat apa adanya di THIRD-PARTY-NOTICES.md, model tetap dibawa |
| 26 | Batas memori di Settings (§7.5) tidak dipakai renderer, tetapi faktor ×1,3/×1,8 tetap memperbesar perkiraan waktu (di mesin uji RAM 8 GB, CPU: tampil "± 2 menit 11 detik" untuk render nyata 61 detik). Render CPU 640 × 360 memakai puncak ±790 MB memori privat lalu turun lagi; Windows mencatat event `RADAR_PRE_LEAK_64` (deteksi pemakaian memori, bukan crash) saat render pertama | `[TERBUKA]`: buang faktornya, atau buat batasnya nyata (mis. tile lebih kecil atau satu inti CPU lebih sedikit) |
| 27 | Laptop dengan dua GPU: DirectML memakai adaptor 0, yang belum tentu GPU yang ditampilkan Settings | `[TERBUKA]`, belum ada perangkat untuk diuji |
| 28 | Microsoft menyatakan DirectML dalam mode pemeliharaan (Windows ML disarankan untuk proyek baru) | `[TERBUKA]`. DirectML dipakai karena berjalan tanpa identitas paket (build portabel) dan sudah diuji di mesin ini; pindah ke Windows ML dipertimbangkan setelah beta |
| 29 | Skala di atas 4×: model berhenti di 4×, sisanya diperbesar dengan Lanczos3, jadi 8× dan 16× tidak menambah detail baru | `[ASUMSI]`. Alternatifnya (model dijalankan dua kali) jauh lebih lama dan butuh jauh lebih banyak memori; belum bisa dicoba di mesin uji (batas 2×) |
| 30 | `PdfDocument` (Windows.Data.Pdf) yang sudah merender lalu dilepas membuat proses crash beberapa saat kemudian di driver grafis (AMD `atidxx64.dll`), juga di test host | Diatasi tahap 6: dokumen yang sudah dirender disimpan sampai proses selesai (maksimal 64). Belum dicoba di driver lain |
| 14 | LibreOffice tetap dibundel di paket x64 (keputusan pemilik 2026-09-24, dikonfirmasi 2026-10-02) | diputuskan |
| 15 | HEIC tetap boleh jadi format tujuan bila codec HEVC terpasang | diputuskan |

## 14. Changelog

Format entri: `[versi] tanggal — Ditambah / Diubah / Dihapus`. Entri baru ditaruh paling atas.

### [0.2.6] 2026-10-03 (tahap 6: Upscale Image)
- **Ditambah:** halaman Upscale Image berfungsi penuh (§6.2): Real-ESRGAN x4plus lewat ONNX Runtime di CPU atau GPU (DirectML), sepenuhnya lokal; tile 128 px dengan tepi 10 px; hasil diubah ukurannya ke ukuran persis dengan Lanczos3; PNG mempertahankan transparansi. Keadaan "belum ada gambar" dengan area seret, keadaan gagal, dan catatan saat NPU atau GPU tidak bisa dipakai.
- **Ditambah:** benchmark nyata saat upscale pertama per mesin (§8), disimpan per mesin dan nama perangkat; perkiraan waktu dihitung dari jumlah tile.
- **Ditambah:** model dibundel di `Models\realesrgan-x4plus\` (diambil `tools/fetch-model.ps1`, SHA-256 dipin, dicek aplikasi sebelum dipakai); pesan bila model hilang atau rusak. ONNX Runtime 1.24.4 dan DirectML 1.15.4 beserta berkas lisensinya (`Licenses\`). THIRD-PARTY-NOTICES.md: ONNX Runtime, DirectML, Real-ESRGAN (lampiran E), dan catatan data latih (keputusan pemilik).
- **Diubah:** preset resolusi menetapkan sisi terpanjang, jadi gambar tegak tetap tegak (§6.2). Ukuran tile 256 `[ASUMSI]` diganti 128 hasil ukur.
- **Diperbaiki:** proses crash beberapa saat setelah PDF dirender (driver AMD, `PdfDocument` dilepas), yang juga membuat test host berhenti acak (§13 #30).
- **Dicek:** uji langsung di build portabel: benchmark GPU 0,32 MP/detik, 24 tile, PNG dan JPG lolos integritas, Batal tanpa file sisa, tautan "Ubah di Settings". Mode CPU (benchmark 0,061 MP/detik, 24 tile dalam 61 detik), pesan model hilang dan model rusak, dan menutup aplikasi tanpa crash juga dicek langsung. **Belum dicoba langsung:** perangkat ber-NPU, GPU diskrit, skala di atas 2× (ada unit test).
- **Diajukan:** §13 #25 sampai #30; §13 #8, #10, #17 diperbarui.

### [0.2.5] 2026-10-02 (opsi kualitas audio dan video)
- **Ditambah:** "Kualitas audio" (MP3, M4A, WMA: 192, 128, 96 kbps) dan "Ukuran video" (MP4, WMV: sama dengan sumber, 1080p, 720p, 480p; tidak pernah memperbesar, bentuk gambar terjaga) di keadaan Input (§6.1, §6.1.2). Permintaan pemilik.
- **Diubah:** MP3, M4A, WMA, MP4, dan WMV boleh dikonversi ke format yang sama supaya kualitas atau ukuran bisa diubah (`IReencodingConverter`); nama yang disarankan diberi akhiran " (hasil)". Format lain tetap tidak menawarkan format sumbernya.
- **Dicek:** bitrate file keluaran sama dengan pilihan (192, 128, 96); 640 × 480 tetap 640 × 480 untuk semua ukuran, 1440 × 1080 menjadi 640 × 480 pada 480p, 1280 × 720 menjadi 854 × 480. Uji langsung di UI.
- **Diajukan:** §13 #24 (video berotasi).

### [0.2.4] 2026-10-02 (tahap 5: audio dan video, PDF → gambar)
- **Ditambah:** konverter audio dan video lewat `Windows.Media.Transcoding` (§6.1.2): MP3, M4A, WAV, WMA, FLAC dan MP4, WMV sebagai tujuan; sumber MP3, M4A, WAV, WMA, FLAC, MP4, M4V, MOV, WMV, AVI; video bisa diambil suaranya. WAV dan FLAC mempertahankan sample rate, kanal, dan kedalaman bit sumber. Tujuan disaring dengan `CodecQuery`.
- **Ditambah:** deteksi audio/video terpotong (`MediaStructure`, Warning `Note.MediaIncomplete`) dan galat `Error.Media`; validator hasil (`MediaOutputValidator`). Ikon audio dan video untuk file sumber dan riwayat; `.m4v`, `.mov`, `.avi` masuk katalog format.
- **Diperbaiki:** halaman PDF yang terlalu besar untuk dirender (14.400 pt = 1.600 MP) menghabiskan 38 detik lalu gagal dengan `OutOfMemoryException` ("galat tak terduga"); sekarang ditolak seketika dengan `Error.ImageTooLarge` (§6.1.1). Halaman 8000 pt (494 MP) tetap diproses (61 detik, 2,5 GB di mesin ini).
- **Dicek:** uji langsung di build portabel: WAV → MP3 (tanpa catatan), WAV terpotong → Warning, MP4 → MP3 (suara diambil), MP4 terpotong → `Error.Media`. **Tidak diaudit ulang:** dokumen lewat LibreOffice (belum terpasang di mesin ini, 27 tes dilewati).
- **Diajukan:** §13 #21 sampai #23.

### [0.2.3] 2026-10-02 (tahap 4: Convert File dan konverter gambar)
- **Ditambah:** catatan konverter di keadaan Selesai (§6.1): `InfoBar` Warning "File sumber tampak terpotong atau rusak…" dan Informational "File ini berisi N gambar… Hanya yang pertama yang dikonversi." Jalurnya umum (`ConversionRequest.Notes` → `ConversionResult.Notes`), dicatat ke log tanpa path.
- **Ditambah:** `ImageStructure` (Core, portabel) untuk mendeteksi PNG/JPEG/GIF/WebP yang terpotong; Windows mendekodenya tanpa galat, sehingga sebelumnya konversi "terverifikasi" padahal sumbernya rusak. Penyebabnya ditemukan lewat audit konverter gambar (§6.1.1).
- **Ditambah:** galat `Error.ImageTooLarge` untuk gambar yang terlalu besar bagi memori (§6.1 Gagal, §6.1.1). Sebelumnya `E_OUTOFMEMORY` jatuh ke "Terjadi galat tak terduga (COMException)".
- **Ditambah (dokumentasi):** keadaan Gagal dan kotak "Catat riwayat" di §6.1, yang sudah ada di aplikasi tetapi belum tercatat; §6.1.1 Konverter gambar (format, piksel, metadata tidak dibawa, banyak frame).
- **Dicek:** uji langsung pada build portabel: JPEG terpotong → Warning; GIF 3 frame → Informational; PNG rusak → `Error.Decode`; PNG utuh → tanpa catatan; catatan hilang di konversi berikutnya.
- **Diajukan:** §13 #19 (metadata) dan #20 (batas piksel), untuk keputusan pemilik. Keputusan pemilik atas #19: metadata tetap dibuang di beta; opsi "pertahankan metadata" (mati secara bawaan, hanya JPG/PNG/TIFF) bisa ditambah nanti bila diminta.
- **Diubah:** ringkasan integritas dan keterangan tahap Verifikasi chunk memakai bentuk tunggal untuk satu chunk ("1 chunk verified", "1 chunk matches"; Indonesia tidak berubah). Keputusan pemilik.

### [0.2.2] 2026-10-02 (tahap 3: shell dan Settings)
- **Ditambah:** shell (§3): `TitleBar` + `NavigationView` (pane 280, mode Auto) + `Frame`, halaman Convert File, Upscale Image, Architecture, Settings. Tema (terang/gelap/ikuti sistem) dan Mica bisa diubah dan berlaku langsung.
- **Ditambah:** halaman Settings lengkap (§6.4) dengan nilai lewat `AppSettings`; penyimpanan `ApplicationData.LocalSettings` bila ada identitas paket (`GetCurrentPackageFullName`, `APPMODEL_ERROR_NO_PACKAGE` = tanpa paket), selain itu `settings.json`. Log aktivitas (`ActivityLog`), ukuran dan pembersihan cache, tombol lisensi pihak ketiga.
- **Diubah:** DXF/DWG pindah dari Convert File ke Architecture. Karena layar Architecture baru dibangun di tahap 7, halaman Architecture sementara memuat kartu konverter lama khusus DXF/DWG (§6.3); Convert File memuat tautan ke sana. Keputusan: alur lama dipertahankan di Architecture (lebih aman daripada halaman kosong). Riwayatnya satu daftar dengan Convert File (§13 #10).
- **Diubah:** halaman bergulir sebagai satu kesatuan; kartu Riwayat tidak lagi mengisi sisa tinggi (§3.3, §6.1). Ukuran jendela awal 1280 × 820, minimum 900 × 640.
- **Ditambah:** Upscale Image sementara berisi `InfoBar` "Upscale belum tersedia" (§3.3), dihapus di tahap 6.
- **Diubah:** item Batas upscale yang terkunci diredupkan dan dikembalikan saat dipilih, bukan `ComboBoxItem.IsEnabled=False` (§6.4).
- **Diperbaiki:** merender halaman PDF menghasilkan ukuran salah (1,4×) pada layar 175% di Windows build 10.0.26340; `PdfPageRenderer` sekarang menskalakan hasil ke ukuran yang diminta.
- **Dicek:** §13 #18 (DXCore terbukti jalan di laptop pemilik, lihat tabel).

### [0.2.1] 2026-10-02
- **Ditambah:** `DeviceProfile`, `CapabilityPolicy` (test vector §9 lulus), estimasi (§8), `AppSettings` dan benchmark di `Condec.Core`. Catatan pembulatan RAM dan memori GPU (§7.1, §7.2), alasan "RAM di bawah 8 GB" (§7.4), waktu render "—" sebelum diukur (§8).
- **Diubah:** bahasa UI mengikuti bahasa Windows (Indonesia, Inggris; lainnya Inggris), bukan hanya Indonesia (§2). Keputusan pemilik.
- **Diubah:** penyimpanan Settings memakai `settings.json` di `%LOCALAPPDATA%\Condec` untuk build portable (§6.4).
- **Diputuskan:** LibreOffice tetap dibundel; HEIC tetap boleh jadi tujuan; versi DWG AC1015 (§13 #7, #14, #15).

### [0.2.0] 2026-10-02
- **Ditambah:** menu hamburger (NavigationView) dengan halaman Upscale Image, Architecture, dan Settings; aturan batas perangkat (§7); estimasi (§8); test vector (§9).
- **Ditambah:** Architecture dua arah, pilihan objek yang tidak dijadikan CAD, saran upscale otomatis, kalibrasi skala.
- **Diubah:** semua kontrol memakai stock WinUI 3. Segmented control dihapus dan diganti `ComboBox`, `RadioButtons`, `SplitButton`.
- **Diubah:** ukuran dikoreksi dari source: item NavigationView 36 (sebelumnya 40), tombol hamburger 40 × 36, `ToggleSwitch` 44 × 20 dengan knob 10 (sebelumnya 40 × 20 dan 12), struktur InfoBar mengikuti template asli.
- **Diubah:** window 1280 × 820, lebar pane terbuka 280. Judul halaman "Konversi file" → "Convert File".
- **Diubah:** PDF → DXF/DWG pindah dari Convert File ke Architecture. Convert File hanya memuat tautan ke sana.
- **Diubah:** nilai statistik memakai `BodyLargeStrongTextBlockStyle` (18), bukan 16 seperti di mock.
- **Dihapus:** layar "PDF ke CAD" lama (board 5–6 versi 0.1).

### [0.1.0] 2026-09-24
- **Ditambah:** alur Convert File (pilih file, format, konversi, tahap proses, verifikasi chunk dan integritas, selesai, riwayat) dan layar PDF → DXF/DWG (digantikan di 0.2.0).
