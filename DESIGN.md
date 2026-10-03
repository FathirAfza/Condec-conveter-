# Condec — DESIGN.md

Versi dokumen 0.2.13 · diubah terakhir 2026-10-03 · target: WinUI 3 (Windows App SDK) di Windows 11

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
- Kiri: kalau belum ada file, area seret (garis putus-putus, ikon 32, "Seret file ke sini", tombol "Pilih file…"). Dialog dan area seret menerima **satu file atau lebih** dari jenis yang sama; dua file atau lebih mengubah panel kiri dan menambah daftar file (§6.1.3). Kalau sudah ada satu file: ikon jenis file 40, label "File sumber" (Caption), nama (BodyStrong, terpotong ellipsis), jenis dan ukuran (Caption, mis. "Dokumen Word · 2,4 MB"), `HyperlinkButton` "Ganti".
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
- Klik tombol → `FileSavePicker`, lalu keadaan Proses. Bila hasilnya lebih dari satu file (beberapa file, atau beberapa halaman), tombolnya "Konversi dan simpan N file…" dan yang dibuka `FolderPicker` (§6.1.3).

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
| Informational | "File ini berisi N gambar (frame atau halaman). Hanya yang pertama yang dikonversi." (`Note.FirstFrameOnly`) | Sumber punya lebih dari satu frame dan tidak ada halaman yang dipilih (GIF animasi; TIFF banyak halaman memakai pilihan halaman, §6.1.3) |
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
- `ListView` (`SelectionMode=None`): kotak ikon 32, nama, Caption "ASAL → TUJUAN · waktu" ("ASAL → TUJUAN · Upscale 2× · waktu" untuk hasil Upscale Image, §6.2), status "Terverifikasi" (Caption, warna sukses, ikon centang 12), tombol ikon folder (`AutomationProperties.Name="Tampilkan di folder"`).
- Kosong: "Belum ada riwayat konversi." (daftar ini memuat konversi Convert File, Architecture, dan Upscale Image; hanya tampil di Convert File)
- Data: `%LOCALAPPDATA%\Condec\history.json` (nama, asal → tujuan, waktu, path, status verifikasi). Tanpa salinan isi file. Konversi baru masuk paling atas.

#### 6.1.1 Konverter gambar (Windows Imaging Component)

Dipakai Convert File untuk JPG, PNG, BMP, GIF, TIFF, serta WebP dan HEIC. Hanya memakai `Windows.Graphics.Imaging`; tanpa pustaka luar.

- **Format.** Sumber: JPG/JPEG, PNG, BMP, GIF, TIF/TIFF; WebP dan HEIC/HEIF hanya bila dekoder Windows-nya terpasang. Tujuan: JPG, PNG, BMP, GIF, TIFF; HEIC hanya bila Windows bisa menulisnya (§13 #15; dicek dengan satu kali tulis lalu baca). Format sumber tidak ditawarkan sebagai tujuan.
- **Piksel.** Dibaca sebagai Bgra8 (alfa lurus), dengan rotasi EXIF diterapkan dan warna dikonversi ke sRGB. Format tanpa alfa (JPG, BMP, GIF) menerima gambar yang transparansinya dilebur ke **putih** (tidak hitam). PNG dan TIFF mempertahankan transparansi.
- **Tidak dibawa:** metadata (EXIF, GPS, profil ICC, teks) dan kualitas khusus. Hanya piksel dan DPI yang ditulis; encoder Windows memakai nilai bawaannya, tanpa pengaturan kualitas (§13 #19).
- **Banyak frame.** TIFF banyak halaman: halaman yang dipilih (§6.1.3), satu hasil per halaman, dibaca lewat `BitmapDecoder.GetFrameAsync`. GIF animasi: hanya frame pertama, dengan catatan Informational yang memberi tahu jumlahnya (§6.1 Selesai); frame GIF bukan halaman (§13 #41).
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

#### 6.1.3 Banyak file dan banyak halaman

Keputusan pemilik 2026-10-03 (§14 [0.2.9]): beberapa file sejenis bisa dikonversi bersamaan, dan PDF atau TIFF berhalaman banyak bisa menjadi satu file per halaman. **Hasil ke-n selalu milik file ke-n** (urutan daftar), dan tidak ada file yang ditimpa.

**Jenis yang boleh bersamaan** (`SourceKinds`, Core): gambar (JPG, PNG, BMP, GIF, TIFF, HEIC/HEIF, WebP, boleh campur), PDF, dokumen (DOCX, DOC, ODT, RTF, TXT, HTML, MD), spreadsheet (XLSX, XLS, ODS), presentasi (PPTX, PPT, ODP), audio, video. DXF/DWG tetap ke Architecture. Jenis pertama yang dipilih menentukan kelompok; file jenis lain, format yang tidak didukung, file yang tidak bisa dibaca, PDF berkata sandi, dan file DXF/DWG tidak diambil, dengan `InfoBar` Warning "Beberapa file tidak diambil: a.pdf (jenisnya berbeda), b.dwg (buka di Architecture). File yang dikonversi bersamaan harus sejenis, misalnya semua gambar atau semua PDF." Kalau tidak satu pun bisa diambil: "Tidak ada file yang bisa dikonversi di sini: …". Folder yang diseret tidak diambil (Informational). File yang sudah ada di daftar tidak ditambah dua kali.

**Format tujuan untuk banyak file:** format yang bisa dicapai **setiap** file, atau yang memang sudah menjadi format file itu, dan sedikitnya satu file benar-benar dikonversi ke sana (urut kemunculan). Contoh PNG + JPG: JPG ditawarkan; PNG ke JPG, sedangkan JPG ditandai "Sudah JPG, dibiarkan" dan tidak menghasilkan file. `.jpeg` = `.jpg`, `.tiff` = `.tif`, `.htm` = `.html`. MP3/M4A/WMA/MP4/WMV ke format yang sama tetap konversi sungguhan (§6.1). Bantu: "N format tersedia untuk .png, .jpg".

**Keadaan Input dengan dua file atau lebih**
- Panel kiri: ikon jenis 40, label "File sumber", judul "3 gambar" / "4 file PDF" / "2 video" (BodyStrong), Caption "Total 566 KB" atau, bila setiap file punya jumlah halaman, "16 halaman, total 3,8 MB"; `HyperlinkButton` "Ganti" (nama terbaca "Ganti file sumber").
- Di bawah dua panel (margin atas 20): judul "Daftar file, sesuai urutan konversi" (BodyStrong) dan `HyperlinkButton` "Tambah file…" (dialog dibuka di folder file pertama). `ListView` (`SelectionMode=None`, tinggi maksimal 264, bergulir): nomor "1.", ikon 20, nama dan Caption jenis · ukuran, hasilnya di kanan ("→ foto.jpg", "→ 3 file", "Sudah JPG, dibiarkan", "Tidak ada halaman terpilih"), tombol ikon hapus (`AutomationProperties.Name="Hapus a.png dari daftar"`). Menghapus file menomori ulang daftar; menghapus sampai tersisa satu kembali ke panel satu file.
- Opsi media (§6.1) berlaku untuk semua file.

**Pilihan halaman** (PDF dan TIFF berhalaman lebih dari satu, ke tujuan gambar: PNG, JPG, HEIC, BMP, GIF, TIFF). Muncul di bawah daftar (margin atas 20, jarak 8):
- `RadioButtons` berheader "Halaman": "Semua halaman" (bawaan), "Halaman pertama saja", "Pilih halaman". "Pilih halaman" memunculkan `TextBox` lebar 240 (placeholder "Contoh: 1-3, 5"; nama terbaca "Halaman yang dikonversi, contoh 1-3, 5"). Format: nomor, rentang `a-b`, rentang terbuka `a-`, dipisah koma; spasi boleh. Teks yang salah (kosong, 0, terbalik, huruf) menampilkan Caption warna kritis "Tulis halaman seperti 1-3, 5. Halaman pertama adalah 1." dan tombol konversi nonaktif. Halaman di atas jumlah halaman file dilewati per file.
- Caption "N file akan disimpan", hanya bila N > 0. Bila pilihan valid tetapi tidak ada halaman yang ada di file mana pun: `InfoBar` Warning "Halaman yang dipilih tidak ada di file yang dipilih. Pilih halaman lain." dan tombol nonaktif.
- Pilihan halaman bertahan selama file yang dipilih masih berhalaman banyak; kembali ke "Semua halaman" saat "Konversi file lain" atau saat tidak ada lagi file berhalaman banyak.
- Tujuan lain (PDF → DOCX, dan sebagainya) selalu mengonversi seluruh file, tanpa pilihan halaman. Halaman PDF dirender 200 dpi seperti sebelumnya (§6.1.1).

**Menyimpan.** Satu hasil (satu file, atau satu halaman yang dipilih) tetap lewat `FileSavePicker`. Lebih dari satu hasil: `FolderPicker` dibuka di folder file pertama, lalu nama dibuat otomatis (`OutputNames`, Core):
- `<nama sumber>.<ext>`; satu halaman `<nama sumber>-<n>.<ext>`, nomor diberi nol di depan sepanjang jumlah halaman file itu ("-01" … "-12") supaya urut di Explorer. Konversi ke format yang sama mendapat akhiran " (hasil)" seperti dialog simpan.
- Nama yang sudah dipakai (file di folder, file sumber, atau hasil sebelumnya di batch yang sama) mendapat " (2)", " (3)", dan seterusnya. Nama dicek lagi tepat sebelum setiap file ditulis, kalau ada file baru muncul selama batch berjalan. Tidak pernah menimpa.

**Keadaan Proses (banyak hasil):** judul "Mengonversi N file", Caption "Ke PNG · disimpan di `<folder>`", tombol "Batal". `ProgressBar` dan persen untuk seluruh batch (dibulatkan ke bawah, jadi 100% hanya setelah file terakhir disimpan). Teks BodyStrong "File 2 dari 5: scan.tif, halaman 2", lalu empat tahap yang sama (§6.1) untuk file yang sedang dikerjakan. File dikerjakan **satu per satu** sesuai urutan; file yang gagal tidak menghentikan file berikutnya.

**Keadaan Selesai (banyak hasil):**
- `InfoBar` (`IsClosable=False`): semua tersimpan = Success "Konversi selesai" / "Semua N file disimpan dan lolos verifikasi chunk serta cek integritas."; sebagian gagal = Warning "Sebagian file gagal" / "X dari N file disimpan. Sisanya tidak disimpan; alasannya ada di daftar."; dihentikan setelah sebagian tersimpan = Warning "Konversi dihentikan" / "X dari N file sudah disimpan sebelum dihentikan. Sisanya tidak dikonversi."; tidak ada yang tersimpan karena gagal = Error "Konversi gagal" / "Tidak ada file yang berhasil dikonversi. Alasannya ada di daftar."
- Tiga baris detail: Lokasi (folder), Format ("PNG, TIF → BMP"), Disimpan ("X dari N file").
- `ListView` hasil (nama terbaca "Hasil tiap file"): nomor, nama hasil, Caption "Dari scan.tif, halaman 2", alasan gagal atau catatan konverter (Caption), status dengan **ikon dan kata** ("Terverifikasi" warna sukses, "Gagal" warna kritis, "Tidak dikonversi" sekunder), tombol folder hanya untuk yang tersimpan.
- Tombol: "Buka folder" (aksen, bila ada yang tersimpan), "Coba lagi file yang gagal" (bila ada yang gagal; menjadi "Konversi sisanya" bila yang belum tersimpan hanya karena dihentikan) yang mengerjakan ulang baris yang belum tersimpan ke folder yang sama, "Ubah pilihan" (bila tidak ada yang tersimpan), `HyperlinkButton` "Konversi file lain".
- Dibatalkan sebelum ada yang tersimpan: kembali ke Input dengan pilihan yang sama dan `InfoBar` "Konversi dibatalkan. Tidak ada file yang disimpan."
- Riwayat: satu entri per file yang tersimpan; nama halaman "scan.tif, halaman 2".
- Log aktivitas: awal dan akhir batch (jenis konversi, jumlah tersimpan, gagal, tidak dikonversi, lama) dan setiap kegagalan, tanpa path.

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

**Belum ada gambar:** kartu tunggal sebagai area seret: ikon gambar, "Seret satu atau beberapa gambar ke sini", tombol "Pilih gambar…" (boleh pilih banyak; antrean di bawah). Sumbernya format gambar yang bisa dibaca konverter gambar (§6.1.1); satu file format lain ditolak dengan pesan berisi daftar format.

**Banyak gambar (antrean)** (keputusan pemilik 2026-10-03, §14 [0.2.9] dan [0.2.12]): beberapa gambar ditinjau satu per satu, lalu di-upscale satu per satu ke folder yang dipilih. Hasil ke-n milik gambar ke-n, dan tidak ada file yang ditimpa.
- Memilih: "Pilih gambar…" dan seret-lepas menerima banyak gambar (semua format §6.1.1, boleh campur). File yang bukan gambar, tidak bisa dibaca, atau terlalu besar untuk jaringan tidak diambil, dengan `InfoBar` Warning "Beberapa file tidak diambil: `<nama>` (`<alasan>`)" (alasan "format tidak didukung", "tidak bisa dibaca", "terlalu besar"). Duplikat diabaikan; folder yang ikut diseret diabaikan dengan pesan. Satu gambar saja tetap mendapat pesan di atas. Paling banyak **20 gambar** (§13 #54): selebihnya tidak diambil, dengan pesan "Antrean memuat paling banyak 20 item, jadi N file tidak diambil." "Ganti" dan seret ke kartu kiri memulai antrean baru; `HyperlinkButton` "Tambah gambar…" di sebelah "Ganti" (dialog dibuka di folder gambar pertama) menambah gambar ke akhir antrean.
- Kartu antrean (di atas kartu kiri dan kanan, `CardBorderStyle`, padding 16), tampil bila ada lebih dari satu gambar, bentuknya sama dengan §6.3.4: `ComboBox` "Antrean" berisi "2. pantai.jpg · 2× · Belum ditinjau" (status "Belum ditinjau", "Sudah ditinjau", "Tidak bisa di-upscale"), tombol ikon "Item sebelumnya" dan "Item berikutnya", `Button` "Hapus dari antrean", dan Caption "3 item · 2 sudah ditinjau" atau alasan tombol nonaktif: "Item 2 tidak bisa di-upscale di perangkat ini. Hapus dari antrean untuk meng-upscale sisanya." atau "Item 2 butuh memori lebih banyak daripada yang ada. Pilih skala yang lebih kecil untuknya, atau hapus dari antrean."
- Per gambar: skala, persentase, dan resolusi hasil milik masing-masing dan tetap saat berpindah gambar; gambar baru mulai di skala awal yang sama dengan satu gambar. Diagram, kartu perkiraan, dan `InfoBar` "Upscale tidak tersedia" mengikuti gambar yang tampil (§13 #55). Format hasil satu untuk semua gambar (§13 #53). Bila Settings menurunkan batas, skala gambar lain ikut turun ke batas baru.
- Tombol "Upscale dan simpan N gambar…" aktif bila setiap gambar bisa di-upscale pada ukuran pilihannya. `FolderPicker` (dibuka di folder gambar pertama); nama `<nama>-<W>x<H>.<ext>`, nama yang terpakai di folder, oleh sumber, atau oleh hasil sebelumnya di batch yang sama mendapat " (2)" (§6.1.3).
- Proses: kartu yang sama dengan judul "Meng-upscale N gambar", Caption "`<mesin>` · disimpan di `<folder>`", ProgressBar untuk seluruh batch, baris BodyStrong "Gambar 2 dari 5: pantai.jpg · 2× · 2560 × 1440", dan empat tahap di atas untuk gambar itu. Pengukuran mesin (§8) dijalankan sekali per ukuran tile sebelum gambar pertama. "Batal" menghentikan gambar yang sedang dibuat (file sementaranya dihapus) dan semua sesudahnya.
- Selesai: `InfoBar` "Upscale selesai" / "Semua N gambar disimpan dan lolos cek integritas.", "Sebagian gambar gagal", "Upscale dihentikan", atau "Upscale gagal"; detail Lokasi (folder), Mesin, Disimpan ("2 dari 3 gambar"); daftar hasil tiap gambar yang sama dengan §6.1.3; tombol "Buka folder" (aksen), "Coba lagi gambar yang gagal" atau "Upscale sisanya", "Ubah pilihan" bila tidak ada yang tersimpan, dan "Upscale gambar lain". Batal sebelum ada yang tersimpan kembali ke pilihan dengan "Upscale dibatalkan. Tidak ada file yang disimpan." Setiap gambar yang tersimpan dicatat di Riwayat dengan skalanya sendiri.


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
- "Perkiraan waktu render" — "± 7 detik" / "GPU · RTX 4060 Laptop" ("GPU · RTX 4060 Laptop · tile 64 px (batas memori)" bila batas memori memaksa tile lebih kecil)
- "RAM dibutuhkan" — "32 GB" / "Terpasang 32 GB, cukup" (warna sukses), "Terpasang N GB, kurang" (warna galat), atau "Batas memori N GB kurang: butuh X GB" (warna galat; gambar tidak muat di batas memori Settings walau dengan tile terkecil, §7.5)
- Tombol aksen "Upscale dan simpan…" di kanan, **nonaktif** bila upscale tidak tersedia, RAM kurang, atau gambar tidak muat di batas memori.

**Upscale tidak tersedia** (batas efektif < 1,5): `InfoBar` Error di atas kartu. Judul "Perangkat ini belum memenuhi syarat upscale". Pesan "Upscale butuh RAM terpasang minimal 8 GB untuk hasil sampai 2K. Perangkat ini hanya punya N GB." Bila RAM cukup tetapi gambarnya terlalu besar untuk diperbesar 1,5× (syarat RAM §7.2 atau batas satu gambar di memori, §13 #20), pesannya "Gambar ini terlalu besar untuk diperbesar di perangkat ini. `<alasan §7.4>`." Skala, slider, dan resolusi nonaktif.

**Proses:** kartu tunggal. Judul "Meng-upscale `<nama>`", Caption "`<skala>` · `<W × H>` · `<mesin>`", "Batal", ProgressBar + persen, tahap: "Menyiapkan mesin render" (0–10%; keterangan "Mengukur kecepatan mesin (sekali saja)" saat benchmark §8 berjalan, lalu "Memuat model"), "Memproses tile" (10–90%, "Tile n dari N"), "Menyimpan hasil" (90–96%, "Menulis .png"), "Verifikasi integritas" (96–100%, "Menghitung SHA-256"). Tile masukan 128 × 128 px. Tepi 4 px tiap tile dibuang setelah diproses, dan tile yang bersebelahan bertumpuk 12 px: di bagian itu hasil tile yang satu memudar linear ke hasil tile berikutnya, jadi tidak ada garis potong. Tiap tile menambah 108 px (432 px di hasil); tile terakhir di tiap sisi dibaca sampai tepi gambar, dan satu piksel paling banyak dibagi dua tile per sisi. Tepi gambar diisi pantulan supaya semua tile berukuran sama. Ukuran tile bisa mengecil menjadi 96, 64, atau 48 px (tepi 4 px dan tumpang tindih 12 px tetap) bila batas memori di Settings menuntutnya (§7.5); langkah antartile sama dengan sebelum [0.2.13], dan jumlah tile sama atau lebih sedikit karena tile pertama di tiap sisi menutup 120 px; tile kecil lebih lambat (§13 #56). Ukuran 128 hasil pengukuran di GPU terintegrasi mesin uji (2026-10-02, DirectML): tile 128 menghasilkan 0,47 MP/detik, tile 256 hanya 0,32; tile 48–192 tidak lebih cepat dari 128. Dibanding hasil satu gambar utuh (2026-10-03, model yang dibundel, tiga gambar uji kecil yang buram dan berkompresi berat): tile 128 yang dibaurkan 37,4–39,9 dB PSNR, 1,3–2,9 dB lebih dekat daripada tile yang dipotong di tepi 10 px (sebelum [0.2.13]), yang meninggalkan garis sambungan terlihat setiap 108 px sumber. Di aplikasi (GPU, 8x07ex 2×): 36,6 dB sebelumnya, 39,8 dB sekarang. Bila GPU gagal menjalankan model, render pindah ke CPU dengan catatan Informational "Kartu grafis tidak bisa menjalankan model upscale, jadi CPU yang merender gambar. Ini lebih lama."

**Selesai:** `InfoBar` Success "Upscale selesai" / "Gambar disimpan dan lolos cek integritas." Detail: Lokasi, Resolusi ("W × H → W' × H' (4×)"), Mesin, Integritas (sama dengan Convert File). Catatan konversi (mis. bingkai pertama saja, render pindah ke CPU) tampil sebagai `InfoBar` di bawah detail. Tombol: "Buka file" (aksen), "Tampilkan di folder", `HyperlinkButton` "Upscale gambar lain".

**Gagal:** `InfoBar` Error "Upscale gagal" dengan pesan dari §6.1 (mis. memori habis = gambar terlalu besar), nama file, tombol "Coba lagi" (aksen) dan "Ubah pilihan". Batal tidak meninggalkan file dan kembali ke pilihan dengan pesan "Upscale dibatalkan. Tidak ada file yang disimpan."

Nama file hasil: `<nama>-<W>x<H>.<ext>` (satu gambar dan antrean). Hasil Upscale dicatat di daftar Riwayat yang sama dengan konversi (keputusan pemilik 2026-10-03, §13 #10), dengan skalanya: "PNG → PNG · Upscale 2× · waktu". Bila entri tidak bisa disimpan, kartu Selesai menampilkan "Konversi berhasil, tetapi entri riwayatnya tidak bisa disimpan.".

### 6.3 Architecture

> **Dibangun di tahap 7.** Layar di bawah berfungsi penuh; yang berbeda dari rancangan awal dan hal yang ditambahkan saat membangun ada di §6.3.3. Convert File tidak lagi punya opsi CAD maupun penelusuran gambar: semua yang berhubungan dengan DXF/DWG ada di sini. Riwayat Architecture masuk ke daftar Riwayat di Convert File (§13 #10).

`SelectorBar` di atas dengan dua item: **"Gambar, PDF, DXF → DWG"** dan **"DWG, DXF → Gambar, PDF"**.

#### 6.3.1 Arah "ke CAD"
Sumber yang didukung: DXF, PNG, JPG, HEIC, HEIF, PDF. Jenis sumber menentukan jalur:
- DXF dan PDF vektor: konversi langsung, tanpa vektorisasi dan tanpa saran upscale.
- PNG, JPG, HEIC, HEIF, dan PDF hasil scan: perlu vektorisasi.

**Keadaan (urutan alur)**

| Keadaan | Tampilan |
|---|---|
| Kosong | Kartu besar (tinggi 400) dengan garis putus-putus: ikon denah 40, "Seret satu atau beberapa gambar, PDF, atau DXF ke sini", Caption "DXF, PNG, JPG, HEIC, HEIF, PDF → DWG", tombol aksen "Pilih file…" (boleh pilih banyak, §6.3.4). |
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
- Kosong: area seret "Seret satu atau beberapa file DWG atau DXF ke sini", Caption "DWG, DXF → PNG, JPG, PDF", tombol aksen "Pilih file…" (boleh pilih banyak, §6.3.4).
- Siap: `InfoBar` Informational "File DWG terbaca" / "3 layer, satuan milimeter. Layer yang dimatikan tidak ikut di hasil." Pratinjau tajam tanpa penanda. Layer yang dimatikan tersembunyi di pratinjau.
- Panel "Opsi hasil" (BodyStrong): lima `RadioButtons` horizontal:

| Opsi | Pilihan | Awal | Aturan |
|---|---|---|---|
| Format hasil | PDF, PNG, JPG | PDF | |
| Ukuran kertas | A4, A3 | A3 | |
| Resolusi (DPI) | 150, 300, 600 | 300 | Nonaktif untuk PDF (vektor) |
| Latar | Putih, Transparan | Putih | Transparan hanya bila format PNG |

  Lalu garis pemisah, "Layer" (BodyStrong) dengan `ListView` Multiple berisi layer file yang benar-benar punya bentuk atau teks (nama CAD baku seperti WALLS, OPENINGS, TEXT ditampilkan sebagai "Dinding dan garis denah", "Pintu dan jendela", "Teks dan dimensi"; layer lain memakai namanya; keterangan "Ditampilkan"/"Disembunyikan"), dan Caption "Perkiraan hasil: …".
- Perkiraan hasil `[ASUMSI]`: piksel = mm ÷ 25,4 × DPI per sisi (A3 420 × 297 mm, A4 297 × 210 mm; kertas lanskap bila gambar lebih lebar daripada tinggi, tegak bila sebaliknya, memakai layer yang ditampilkan). PNG ≈ 0,1 byte/piksel, JPG ≈ 0,14 byte/piksel, PDF ≈ 0,3 MB. Tampilan: "PDF vektor · ± 0,3 MB" atau "4961 × 3508 px · ± 1,7 MB".
- Tombol aksen "Konversi dan simpan…" (untuk banyak gambar: "Konversi dan simpan N file…", §6.3.4).

### 6.3.3 Perilaku yang dibangun di tahap 7

Hal yang tidak ada atau berbeda di §6.3.1 dan §6.3.2 (tiap butir yang bertanda `[ASUMSI]` tercatat di §13 #31 sampai #38).

**Pembacaan gambar (ke CAD)**
- Gambar dibaca di perangkat: `DrawingAnalyzer` mencari dinding, pintu dan jendela, teks, logo, dan tabel; `ClarityAnalyzer` menandai area buram. Selama membaca, kartu menampilkan "Membaca `<nama>`" dengan `ProgressBar` ber-nama dan tombol "Hentikan". Sisi terpanjang dianalisis paling besar 2400 px; garis yang dibaca OCR paling banyak 500.
- Jenis objek yang tidak ditemukan **tidak ditampilkan** di daftar (baris "Logo dan kop" muncul hanya bila ada logo).
- "Teks dan dimensi" dibaca dengan `Windows.Media.Ocr` memakai bahasa profil pengguna Windows. Bila tidak ada paket bahasa OCR, teks tetap dikenali letaknya tetapi digambar sebagai garis, dan baris itu berkata "Digambar sebagai garis (tulisan tidak terbaca)" (§13 #9).
- Gambar yang sebagian besar berwarna (render berwarna, foto) menjadi **satu** objek "Logo dan kop", tidak ditelusuri jadi garis.
- Tidak ada garis atau teks sama sekali: `InfoBar` Warning "Tidak ada objek yang terdeteksi" dan tombol Konversi nonaktif.
- Tidak ada yang dicentang: Caption "Tidak ada yang dicentang, jadi tidak ada yang dikonversi." dan tombol Konversi nonaktif.

**Area tidak jelas**
- Gambar dibagi petak 32 × 32 px. Petak yang garisnya landai (kecuraman tepi di bawah 0,5) dan cukup kontras digabung jadi area, **paling besar 6 × 6 petak (192 px)** sehingga gambar yang buram seluruhnya terhitung banyak area, bukan satu. Ambang "banyak" tetap 5 area atau lebih.
- Di pratinjau area tidak jelas hanya digambar dengan garis tepi (tanpa isi) supaya gambar di bawahnya tetap terlihat.
- Persen "objek terbaca jelas" di `InfoBar` Success = bagian objek yang tidak berada di area tidak jelas, dari analisis nyata. Setelah upscale, analisis diulang pada hasilnya.

**Upscale dari halaman ini**
- Gambar yang akan di-upscale disimpan sebagai PNG sementara di folder cache render (`%LOCALAPPDATA%\Condec\cache\architecture`, yang dikosongkan "Hapus cache" di Settings), dijalankan lewat pipeline Upscale yang sama dengan §6.2 (mesin dan batas memori dari Settings), lalu dibaca ulang dengan DPI sumber × skala dan dianalisis lagi. File sementara dihapus setelah selesai atau dibatalkan.
- Hasil upscale hidup di memori selama gambar itu terbuka; yang dikonversi ke CAD adalah gambar hasil upscale. Kalibrasi skala disimpan per piksel gambar asli, jadi tetap benar setelah upscale.
- Upscale yang gagal atau dibatalkan: gambar asli dipakai, dengan catatan "Upscale tidak berhasil, jadi gambar dikonversi apa adanya." atau "Upscale dihentikan."
- Perangkat yang tidak memenuhi syarat dihitung dengan tingkat RAM dan batas efektif §7 untuk skala 2×; bila gambar terlalu besar bahkan untuk 2×, pesan Error berkata "Gambar ini terlalu besar untuk diperbesar di perangkat ini".

**DXF dan PDF**
- DXF: selalu dikonversi ke DWG (tidak ada pilihan format; DXF → DXF tidak berarti). Pesan Informational "File DXF sudah berupa CAD. File ini dikonversi ke DWG apa adanya."
- PDF: setiap halaman yang dipilih menjadi satu item antrean (§6.3.4); sebelum [0.2.10] hanya halaman pertama. PDF vektor memakai konverter PDF → CAD yang sama dengan sebelum redesign, jadi kontrol satuan, skala, "Pertahankan teks sebagai teks", dan "Gabungkan garis bersambung" tetap ada di panel kanan menggantikan kalibrasi (`Architecture.Objects.PdfHint`). PDF hasil scan diperlakukan seperti gambar raster. Satuan, skala, dan dua sakelar itu berlaku untuk semua halaman vektor di antrean; nilai awalnya diambil dari halaman vektor pertama yang dibaca.

**Kalibrasi**
- `ContentDialog` "Kalibrasi skala": klik dua titik pada gambar, isi jarak asli dan satuan (milimeter, sentimeter, meter, inci, kaki); "Terapkan" menyimpan "1 piksel = N mm". Jalur keyboard: tombol panah menggeser kursor (Shift untuk langkah besar), Enter menandai titik; Caption petunjuknya ada di dialog. Gambar dalam dialog punya nama untuk pembaca layar. Tombol "Tandai ulang" mengosongkan titik.
- Tanpa kalibrasi skala berasal dari DPI gambar (96 bila file tidak menyatakan).

**Arah "dari CAD"**
- Pratinjau dan hasil digambar oleh penggambar sendiri (`CadFlattener` → `CadPdfWriter` → render PDF Windows), bukan LibreOffice dan bukan Win2D (§13 #13). Format hasil PDF ditulis langsung oleh penggambar itu (vektor); PNG dan JPG dirender dari PDF yang sama pada DPI pilihan, JPG dengan latar putih.
- Menonaktifkan semua layer: Caption "Tampilkan minimal satu layer." dan tombol nonaktif. Pratinjau yang gagal digambar: Caption "Pratinjau tidak bisa digambar, tapi file tetap bisa dikonversi."

**Aksesibilitas dan lainnya**
- `SelectorBar` dan semua tombol bisa dijangkau keyboard; pratinjau punya nama ("Pratinjau `<nama>`"); status tidak hanya lewat warna (judul `InfoBar`, ikon, dan teks "Ditampilkan"/"Disembunyikan"). Tiap baris objek dan layer membawa nama gabungan untuk pembaca layar.
- Area seret "ke CAD" menerima satu atau beberapa file sejenis (§6.3.4); satu file yang tidak didukung mendapat pesan "File `<ekstensi>` tidak bisa dijadikan CAD di sini." Area seret "dari CAD" juga menerima satu atau beberapa file DWG atau DXF (§6.3.4, sejak [0.2.11]); satu file yang tidak didukung mendapat pesan "File `<ekstensi>` tidak bisa digambar di sini. Pakai file DWG atau DXF."
- Konversi dari halaman ini ditulis ke Riwayat (§13 #10) dan log aktivitas dengan jenis konversi saja (tanpa path).

### 6.3.4 Banyak file dan banyak halaman

Keputusan pemilik 2026-10-03 (§14 [0.2.9], [0.2.10]): PDF ke DWG/DXF bisa banyak halaman, beberapa file sejenis bisa dipilih sekaligus, dan arah "ke CAD" memakai **antrean yang ditinjau satu per satu**. Hasil ke-n milik item ke-n, dan tidak ada file yang ditimpa (aturan nama sama dengan §6.1.3).

**Memilih file.** "Pilih file…" dan seret-lepas menerima banyak file. Kelompok jenis (`SourceKinds`): gambar (PNG, JPG, HEIC, HEIF, boleh campur), PDF, atau DXF. Jenis pertama menentukan kelompok; file jenis lain, format lain, file yang tidak bisa dibaca, dan PDF bersandi tidak diambil, dengan `InfoBar` Warning yang sama dengan §6.1.3. Duplikat diabaikan. Satu file yang tidak didukung tetap mendapat pesan §6.3.3. "Pilih file lain" memulai antrean baru; tombol "Tambah file…" (di sebelahnya, dialog dibuka di folder file pertama) menambah file sejenis ke akhir antrean.

**Antrean.** Setiap gambar dan DXF menjadi satu item; PDF menjadi satu item per halaman terpilih, bernama "gambar.pdf, halaman 2". Paling banyak **20 item** (§13 #45): bila lebih, hanya 20 pertama yang dibaca, status antrean berkata "Antrean memuat paling banyak 20 item, dan N item lagi dipilih. Pilih lebih sedikit halaman atau file untuk mengonversi." dan tombol Konversi nonaktif.

**Kartu antrean** (di atas banner, `CardBorderStyle`, padding 16, jarak 12), tampil bila ada lebih dari satu item atau ada PDF berhalaman banyak:
- `ComboBox` berheader "Antrean" berisi "2. gambar.pdf, halaman 2 · Belum ditinjau"; status per item: "Menunggu", "Sedang dibaca", "Belum ditinjau", "Sudah ditinjau", "Tidak bisa dibaca", "Tidak ada yang dikonversi". Di kanannya tombol ikon "Item sebelumnya" dan "Item berikutnya" (nama terbaca dan tooltip) dan `Button` "Hapus dari antrean" (aktif bila lebih dari satu item; menghapus halaman hanya menghapus halaman itu, lalu nomor dihitung ulang).
- Pilihan halaman (PDF berhalaman lebih dari satu): `RadioButtons` "Halaman" dan kotak rentang yang sama dengan §6.1.3, berlaku untuk semua PDF di antrean. Mengubahnya menyusun ulang antrean; item yang tetap ada menyimpan tinjauannya. Bila tidak ada halaman terpilih di PDF mana pun, antrean tetap seperti sebelumnya dan statusnya berkata "Halaman yang dipilih tidak ada di file yang dipilih. Pilih halaman lain."
- Hasil untuk PDF, tampil bila satu PDF punya lebih dari satu item: `RadioButtons` "Halaman dari satu PDF": "Satu file per halaman" (bawaan, §13 #46) atau "Satu gambar CAD, halaman berdampingan" (`CombinedCadBuilder`: halaman berjajar ke kanan dengan jarak 10% lebar, satuan dari halaman pertama).
- Caption status: "5 item · 3 sudah ditinjau · 2 masih dibaca", atau alasan tombol nonaktif ("Item 3 tidak bisa dibaca. Hapus dari antrean untuk mengonversi sisanya.", "Item 2 tidak punya apa pun untuk dikonversi. Centang objeknya, atau hapus dari antrean.", halaman salah, batas 20 item).
- Selama upscale, kartu antrean, "Pilih file lain", "Tambah file…", dan Konversi nonaktif.

**Membaca.** Item dibaca **satu per satu di latar belakang** (§13 #47), item yang sedang ditampilkan didahulukan; membaca dan upscale tidak pernah berjalan bersamaan. Item yang ditampilkan dan belum terbaca menampilkan kartu "Membaca `<nama>`" (§6.3.3). "Hentikan" mengeluarkan item yang belum terbaca dari antrean; yang sudah terbaca tetap, dengan `InfoBar` "Pembacaan dihentikan. Yang sudah dibaca tetap di antrean." (bila belum ada yang terbaca: kembali ke area seret, "Pembacaan dihentikan."). Item yang gagal dibaca menampilkan kartu "Item ini tidak bisa dibaca" dengan nama, alasannya, dan tombol aksen "Hapus dari antrean".

**Tinjauan per item.** Banner, pratinjau, daftar objek yang dicentang, upscale, dan kalibrasi milik masing-masing item dan tetap saat berpindah item. Item menjadi "Sudah ditinjau" setelah ditampilkan sekali. Format CAD (DWG/DXF) berlaku untuk semua item; antrean DXF selalu ke DWG.

**Konversi.** Tombol aktif bila semua item terbaca, tidak ada yang gagal atau kosong, halaman valid, batas tidak terlampaui, dan skala PDF vektor valid. Label: "Konversi ke DWG…" untuk satu hasil, "Konversi dan simpan N file…" untuk lebih. Satu hasil memakai `FileSavePicker` (nama `<nama>.dwg`, atau `<nama>-<n>.dwg` untuk satu halaman dari PDF berhalaman banyak). Lebih dari satu hasil memakai `FolderPicker` dan nama otomatis §6.1.3 (`gambarkerja-1.dwg` dan seterusnya; gabungan: `gambarkerja.dwg`), lalu proses, Selesai, "Coba lagi file yang gagal", Riwayat, dan log batch yang sama dengan §6.1.3. Format batch: "PDF → DWG".

**Arah "dari CAD"** (§14 [0.2.11]). Antrean yang sama, dengan perbedaan ini:
- DWG dan DXF satu kelompok dan boleh campur; file lain tidak diambil dengan `InfoBar` Warning §6.1.3, duplikat diabaikan. Tiap file satu item (tidak ada pilihan halaman). File di atas 20 item **tidak diambil** sama sekali (§13 #51), dengan `InfoBar` Warning "Antrean memuat paling banyak 20 item, jadi N file tidak diambil."
- Kartu antrean (bentuk sama dengan arah "ke CAD") tampil bila ada lebih dari satu item, dan disembunyikan selama konversi.
- Layer yang ditampilkan dan pratinjaunya milik masing-masing item dan tetap saat berpindah item; layer yang dimatikan di file tetap tersembunyi di awal. Pratinjau yang digambar untuk kertas lain digambar ulang saat item ditampilkan. Format, ukuran kertas, DPI, dan latar berlaku untuk semua item (§13 #50).
- Status item "Tidak ada yang dikonversi" berarti semua layernya disembunyikan; Caption status lalu berkata "Item 2 tidak menampilkan layer apa pun. Tampilkan minimal satu layer, atau hapus dari antrean."
- File yang tidak bisa dibaca, atau yang tidak punya bentuk untuk digambar, menjadi item gagal dengan kartu "Item ini tidak bisa dibaca" (sebelumnya: kembali ke area seret dengan peringatan). "Hapus dari antrean" di kartu itu juga berlaku untuk satu-satunya item, dan kembali ke area seret.
- Tombol Konversi aktif bila semua item terbaca dan masing-masing menampilkan minimal satu layer. Satu item: `FileSavePicker` dengan nama `<nama>.png` (atau `.jpg`, `.pdf`) seperti sebelumnya. Lebih dari satu: `FolderPicker`, nama `<nama>.<format>`, nama terpakai mendapat " (2)", termasuk dua hasil bernama sama dari `denah.dwg` dan `denah.dxf` di batch yang sama, lalu proses, Selesai, coba lagi, dan Riwayat §6.1.3. Format batch menyebut jenis sumber yang ada, mis. "DWG, DXF → PNG".

### 6.4 Settings

`SelectorBar` dengan dua item: **"Render dan performa"** dan **"Umum"**. Tiap setelan adalah baris kartu (`Border` + `Grid`, sudut 4, tinggi minimum 68, padding 16, jarak antar baris 4): judul (Body) dan deskripsi (Caption sekunder) di kiri, kontrol di kanan. Judul grup memakai `BodyStrongTextBlockStyle` dengan jarak atas 16.

#### Tab "Render dan performa"

| Grup / baris | Kontrol | Isi |
|---|---|---|
| Perangkat ini | Kartu info (grid 2 × 2) | Prosesor, RAM terpasang, GPU, NPU ("Tidak terdeteksi" bila tidak ada). Kanan: Caption "Batas upscale perangkat" dan angka `TitleTextBlockStyle` ("4×"). |
| Render mode | `Expander` | Header: "Render mode" + deskripsi "Mesin yang dipakai untuk upscale dan vektorisasi. GPU paling cepat, CPU selalu tersedia." + nilai terpilih di kanan. Isi: `RadioButtons` vertikal GPU / CPU / NPU, masing-masing dengan nama perangkat dan status ("Terdeteksi · tercepat", "Selalu tersedia · paling lambat", "Terdeteksi" / "Tidak terdeteksi"). NPU nonaktif bila tidak terdeteksi. |
| Batas upscale | `ComboBox` (lebar 160) | 2×, 4×, 8×, 16×. Item di atas kemampuan perangkat nonaktif ("Di atas batas perangkat"). Deskripsi: "Skala maksimum yang boleh dipilih di Upscale Image dan Architecture. Perangkat ini mampu sampai N×." |
| Batas memori | `Slider` (lebar 220) + nilai | Minimum 4, Maximum RAM terpasang (GB), StepFrequency 1, TickFrequency 4. Nilai "N GB" di kanan. Deskripsi: "RAM maksimum yang boleh dipakai Condec. Batas kecil membuat upscale lebih lambat dan hasilnya bisa sedikit kurang rapi (tile lebih kecil); gambar yang tetap tidak muat tidak dimulai." Di bawahnya Caption: "Syarat RAM terpasang menurut resolusi hasil: HD–2K 8 GB · 2K–4K 16 GB · 4K–8K 32 GB · di atas 8K 64 GB." |
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
Batas ini nyata (keputusan pemilik 2026-10-03): Upscale Image tidak memakai lebih banyak memori daripada batas ini, menurut perhitungan di bawah. Batas ini tidak menutup akses skala.

Memori puncak proses = `app + model + tile + gambar`:
- app: 300 MB (WinUI, halaman, heap sebelum gambar).
- model: 180 MB di GPU, 120 MB di CPU (bobot dan mesin).
- tile: 22 KB (GPU) atau 14 KB (CPU) per piksel tile, jadi tile 128 px memakai 360 MB di GPU dan 230 MB di CPU. Di CPU, arena memori ONNX Runtime dimatikan (tile 128 butuh 183 MB, bukan 542 MB, tanpa jadi lebih lambat).
- gambar: sumber (4 B/px) + hasil jaringan (64 B per piksel sumber) + langkah pertama resize (16 B × lebar hasil × tinggi sumber) + hasil (4 B/px hasil) + salinan saat menyimpan (6 B/px hasil) + transparansi bila hasilnya PNG. Gambar jaringan dan langkah pertama dilepas (GC paksa) sebelum hasil disimpan.

Aturan: dari ukuran tile 128, 96, 64, 48 dipakai yang terbesar yang puncaknya masih ≤ batas. Tile yang lebih kecil lebih lambat per piksel (di GPU uji: 48 → 0,13, 64 → 0,20, 96 → 0,28, 128 → 0,32 MP/detik), dan hasilnya lebih jauh dari hasil gambar utuh (§13 #56). Bila tile 48 pun tidak muat, upscale tidak dimulai dan kartu perkiraan menulis "Batas memori N GB kurang: butuh X GB". Angka di atas diukur dengan proses penuh (§13 #26) dan setiap konstanta sedikit di atas hasil ukur.

## 8. Estimasi

| Besaran | Rumus | Tag |
|---|---|---|
| Piksel hasil | `round(W × skala) × round(H × skala)` | |
| Ukuran file PNG | piksel × 1,7 byte | `[ASUMSI]` |
| Ukuran file JPG | piksel × 0,4 byte | `[ASUMSI]` |
| Waktu render | `MP kerja ÷ throughput(mesin, ukuran tile)`. MP kerja = jumlah tile × MP berguna satu tile (tile 128: tiap tile menambah 108 px, 4× = 432 × 432 px = 0,186624 MP; tile 96: 304 × 304; 64: 176 × 176; 48: 112 × 112). Tile di tepi kanan dan bawah dihitung utuh karena model tetap memproses tile penuh. Tidak bergantung pada skala yang dipilih, karena model selalu bekerja 4× (§6.2) | |
| Throughput | hasil benchmark singkat pada eksekusi pertama (tile yang sama dengan render), disimpan per mesin, ukuran tile (`bench.<mesin>.<tile>`), dan nama perangkat; ganti GPU atau NPU berarti diukur ulang, dan ukuran tile yang belum pernah dipakai diukur saat pertama kali dipakai. Protokol: 1 tile pemanasan tanpa hitung waktu, lalu 3 tile diukur; satuannya MP berguna per detik. Diukur saat upscale pertama dengan mesin itu, pada tahap "Menyiapkan mesin render" | protokol `[ASUMSI]` |
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
  Architecture/             analisis gambar (garis, kejelasan, pintu, teks, logo, tabel), tulis DWG/DXF,
                            adegan CAD dan penulis PDF untuk "dari CAD"
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
| 9 | OCR teks gambar: `Windows.Media.Ocr`, bahasa terpasang | Dipakai di tahap 7 (`WindowsTextRecognizer`) dengan bahasa profil pengguna Windows. Bergantung pada paket bahasa OCR yang terpasang: tanpa itu teks digambar sebagai garis dan baris objek berkata begitu (§6.3.3). Belum dicoba di Windows berbahasa Indonesia |
| 10 | Apakah riwayat Upscale dan Architecture masuk ke satu daftar Riwayat | Diputuskan pemilik 2026-10-03: ya, satu `history.json`. Konversi Convert File, Architecture, dan Upscale Image (dengan skalanya) ada di daftar yang sama, yang hanya ditampilkan di Convert File. |
| 11 | Ambang "banyak objek tidak jelas" (5 area) | `[ASUMSI]`. Satu area paling besar 6 × 6 petak 32 px, kecuraman tepi minimum 0,5, kontras minimum 40: dikalibrasi hanya pada gambar sintetis yang diburamkan, bukan pada denah nyata (§13 #31) |
| 12 | Perkiraan ukuran file (§8) dan hasil DWG → gambar (§6.3.2) | `[ASUMSI]` |
| 13 | Win2D untuk render DWG | Tidak dipakai. Tahap 7 menggambar sendiri (`CadPdfWriter` + `Windows.Data.Pdf`) sehingga tanpa paket tambahan dan hasil PDF vektor yang sama dengan pratinjau |
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
| 26 | Batas memori di Settings (§7.5) | Diputuskan pemilik 2026-10-03: dibuat nyata. Diukur dengan proses penuh (puncak memori privat di atas proses kosong): GPU 640 × 360 → 1280 × 720 tile 128 +400 MB, 2000 × 1500 → 4000 × 3000 tile 128 +782 MB (PNG) / +777 MB (JPG), 1000 × 600 → 2000 × 1200 tile 96 +436 MB dan tile 48 +305 MB; CPU tile 128 +300 MB (640 × 360) dan +322 MB (1000 × 600), tile 64 +191 MB, tile 48 +187 MB. Perkiraan selalu di atas hasil ukur dan paling jauh 1,5× di atasnya (test). Di mesin uji (RAM 8 GB, sumber paling besar ±1,6 MP) batas 4 GB tidak pernah tercapai; tile mengecil hanya untuk gambar besar di perangkat dengan RAM besar dan batas yang rendah. `[ASUMSI]`: aplikasi diukur 104–210 MB saat idle, dihitung 300 MB; transparansi PNG dianggap ada, karena baru ketahuan setelah gambar dibaca |
| 27 | Laptop dengan dua GPU: DirectML memakai adaptor 0, yang belum tentu GPU yang ditampilkan Settings | `[TERBUKA]`, belum ada perangkat untuk diuji |
| 28 | Microsoft menyatakan DirectML dalam mode pemeliharaan (Windows ML disarankan untuk proyek baru) | `[TERBUKA]`. DirectML dipakai karena berjalan tanpa identitas paket (build portabel) dan sudah diuji di mesin ini; pindah ke Windows ML dipertimbangkan setelah beta |
| 29 | Skala di atas 4×: model berhenti di 4×, sisanya diperbesar dengan Lanczos3, jadi 8× dan 16× tidak menambah detail baru | `[ASUMSI]`. Alternatifnya (model dijalankan dua kali) jauh lebih lama dan butuh jauh lebih banyak memori; belum bisa dicoba di mesin uji (batas 2×) |
| 30 | `PdfDocument` (Windows.Data.Pdf) yang sudah merender lalu dilepas membuat proses crash beberapa saat kemudian di driver grafis (AMD `atidxx64.dll`), juga di test host | Diatasi tahap 6: dokumen yang sudah dirender disimpan sampai proses selesai (maksimal 64). Belum dicoba di driver lain |
| 31 | Heuristik analisis gambar (tahap 7): pintu = busur 60–120° dengan jari-jari kurang dari 15% sisi dan daun sepanjang jari-jari ±20%; jendela = 3–5 garis sejajar berjarak rapat; tabel = bingkai dengan sedikitnya 4 sel kecil; logo = daerah berwarna jenuh ≥ 70 dengan sisi ≥ 3% dan luas ≤ 50%; teks = tanda kecil berderet (tinggi ≤ 6% sisi, rentang tinggi ≤ 3,5×) | `[ASUMSI]`. Tidak ada denah nyata untuk dikalibrasi (contoh pemilik hanya gambar kerja PDF/DWG dan DXF); diuji dengan gambar sintetis. Perlu satu-dua denah pemilik |
| 32 | Lebar teks hasil OCR diskalakan 0,75 dari lebar kotak, dan tinggi dari tinggi kotak | `[ASUMSI]`, supaya TEXT tidak lebih lebar dari tulisan asli |
| 33 | "Kop gambar" dikenali sebagai tabel yang punya logo di dalamnya (jadi satu objek kop, bukan tabel dan logo terpisah) | `[ASUMSI]` |
| 34 | PDF vektor di Architecture memakai kontrol satuan dan skala lama, bukan kalibrasi; satu nilai untuk semua halaman vektor di antrean | "Halaman pertama saja" diganti keputusan pemilik 2026-10-03 (§6.3.4). Satu nilai untuk semua halaman `[ASUMSI]` |
| 35 | DXF → DXF tidak ditawarkan: DXF selalu jadi DWG | Diputuskan saat membangun (§6.3.3) |
| 36 | Urutan objek dan layer file DWG/DXF tampil seperti di file; nama baku (WALLS dst.) diterjemahkan | `[ASUMSI]` |
| 37 | Gambar yang sebagian besar berwarna (render, foto) menjadi satu objek "Logo dan kop" | `[ASUMSI]`; hasil CAD dari foto memang tidak berarti |
| 38 | Dialog kalibrasi: jalur keyboard (panah + Enter) | `[TERBUKA]` belum dicoba langsung di UI; ada unit test untuk hitungannya |
| 39 | Kelompok jenis untuk banyak file (§6.1.3): TXT, HTML, dan MD masuk "dokumen"; PDF kelompok sendiri | Kelompok diputuskan pemilik 2026-10-03; letak TXT/HTML/MD `[ASUMSI]` |
| 40 | Pilihan halaman hanya untuk tujuan gambar, bawaan "Semua halaman"; PDF ke DOCX dan sejenisnya selalu seluruh file | `[ASUMSI]` |
| 41 | Frame GIF animasi tidak dianggap halaman (tetap frame pertama dengan catatan); hanya PDF dan TIFF yang berhalaman | `[ASUMSI]` |
| 42 | Nama hasil batch: "-n" dengan nol di depan sepanjang jumlah halaman; nama terpakai mendapat " (2)" sampai " (9999)" | Aturan " (2)" dan tanpa menimpa diputuskan pemilik 2026-10-03; nol di depan dan batas 9999 `[ASUMSI]` |
| 43 | Tidak ada batas jumlah file atau halaman dalam satu batch; dikerjakan satu per satu, bukan paralel | `[ASUMSI]`. Satu per satu supaya memori sama dengan satu konversi; batas bisa ditambah bila pemilik mau |
| 44 | Seret-lepas beberapa file sekaligus dan jalur Tab di daftar file | `[TERBUKA]` belum dicoba langsung (otomasi tidak bisa menyeret file atau mengirim Tab ke aplikasi di mesin uji); kodenya sama dengan "Pilih file…", dan kontrolnya stock dengan nama terbaca |
| 45 | Antrean Architecture paling banyak 20 item; lebih dari itu tidak dikonversi sampai dikurangi | Batas dibuat nyata atas keputusan pemilik 2026-10-03 ("batasannya dibuat nyata"); angka 20 `[ASUMSI]`, karena tiap item yang terbaca menyimpan analisis dan pratinjaunya di memori |
| 46 | Bawaan hasil PDF banyak halaman: "Satu file per halaman" | `[ASUMSI]`; pilihan gabungan diputuskan pemilik 2026-10-03 |
| 47 | Item antrean dibaca otomatis di latar belakang, satu per satu, item yang ditampilkan didahulukan; "Hentikan" mengeluarkan yang belum terbaca | `[ASUMSI]` |
| 48 | Setelah "Hentikan", pilihan halaman tidak lagi menggambarkan antrean sampai diubah (mengubahnya menyusun ulang dari semua halaman PDF) | `[ASUMSI]` |
| 49 | Seret-lepas banyak file ke Architecture, jalur Tab di kartu antrean, dan antrean di Windows berbahasa Indonesia | `[TERBUKA]` belum dicoba langsung (alasan sama dengan #44); berlaku juga untuk antrean Upscale Image (§6.2) |
| 50 | Antrean "dari CAD": format, kertas, DPI, dan latar satu untuk semua item; layer per item | `[ASUMSI]`. Hasil satu batch biasanya dicetak atau dikirim bersama, jadi satu ukuran kertas dan format |
| 51 | Antrean "dari CAD": file di atas 20 item tidak diambil (bukan diambil lalu tombol nonaktif seperti "ke CAD") | `[ASUMSI]`. "Ke CAD" bisa dipendekkan lewat pilihan halaman; "dari CAD" tidak punya pilihan itu |
| 52 | DWG/DXF yang rusak mendapat pesan umum `Error.Decode` ("…atau codec untuk format ini belum terpasang di Windows"), padahal DWG/DXF tidak memakai codec Windows | `[TERBUKA]`, sudah begitu sejak [0.2.8]; usul: pesan khusus CAD ("File ini rusak atau bukan DWG/DXF yang sah") bila pemilik setuju |
| 53 | Antrean Upscale: format hasil (PNG/JPG) satu untuk semua gambar; skala per gambar | `[ASUMSI]`, sama dengan opsi hasil bersama di Architecture "dari CAD" (#50) |
| 54 | Antrean Upscale paling banyak 20 gambar; selebihnya tidak diambil | `[ASUMSI]`. Batas dibuat nyata atas keputusan pemilik ("batasannya dibuat nyata"); angka 20 sama dengan #45 |
| 55 | Kartu perkiraan (ukuran, waktu, RAM) hanya untuk gambar yang tampil; tidak ada jumlah untuk seluruh antrean | `[ASUMSI]` |
| 56 | Tile kecil lebih jauh dari hasil gambar utuh: tiap tile melihat lebih sedikit sekelilingnya. Diukur 2026-10-03 pada 8x07ex 150 × 150: tile 128 38,3 dB, 96 34,6 dB, 64 29,6 dB, 48 28,7 dB. Sebelum [0.2.13] §7.5 menulis "hasilnya sama persis", dan deskripsi Batas memori di Settings (§6.4, tab "Render dan performa") menulis "bukan lebih buruk kualitasnya" | Diputuskan pemilik 2026-10-03: teks dibuat jujur, "…lebih lambat dan hasilnya bisa sedikit kurang rapi (tile lebih kecil)…" (§6.4, §14 [0.2.13]). Tile kecil hanya dipakai bila batas memori menuntutnya; di mesin uji tidak pernah tercapai (§13 #26) |
| 14 | LibreOffice tetap dibundel di paket x64 (keputusan pemilik 2026-09-24, dikonfirmasi 2026-10-02) | diputuskan |
| 15 | HEIC tetap boleh jadi format tujuan bila codec HEVC terpasang | diputuskan |

## 14. Changelog

Format entri: `[versi] tanggal — Ditambah / Diubah / Dihapus`. Entri baru ditaruh paling atas.

### [0.2.13] 2026-10-03 (Upscale: tile tanpa garis sambungan)
- **Permintaan pemilik (2026-10-03):** hasil upscale masih terlihat "ngaco" pada garis dan geometri. Penyebabnya dua: (1) garis sambungan tile pada gambar buram, diperbaiki di sini; (2) model Real-ESRGAN x4plus mengarang tekstur pada sumber kecil yang berkompresi berat, yang menyangkut pilihan model (§13 #8) dan ditanyakan ke pemilik.
- **Diubah:** tile tidak lagi dipotong di tepi 10 px. Tepi 4 px dibuang dan tile yang bersebelahan bertumpuk 12 px yang dibaurkan linear (§6.2). Langkah antartile tetap 108 px (tile 128), jadi jumlah tile sama atau lebih sedikit (tile pertama di tiap sisi kini menutup 120 px; contoh 1280 × 720 tetap 84 tile, 150 × 150 dengan tile 48 turun dari 36 ke 25) dan render tidak lebih lambat. Satu buffer kecil (4 B × lebar hasil jaringan × 48 baris) dipakai selama render; muat di dalam rumus memori §7.5, yang menghitung buffer resize dan simpan yang belum ada saat render.
- **Diubah:** §7.5 tidak lagi menulis bahwa tile kecil memberi hasil "sama persis" (§13 #56).
- **Keputusan pemilik (2026-10-03, §13 #56):** deskripsi Batas memori di Settings (§6.4) dibuat jujur: "RAM maksimum yang boleh dipakai Condec. Batas kecil membuat upscale lebih lambat dan hasilnya bisa sedikit kurang rapi (tile lebih kecil); gambar yang tetap tidak muat tidak dimulai." (EN: "…slower and the result can be slightly less clean (smaller tiles)…"). Sebelumnya "…bukan lebih buruk kualitasnya…".
- **Ditambah (test):** jumlah tile untuk aturan baru, dua tile terakhir yang hanya berjarak 1 px, dan pembauran tile termasuk sudut tempat empat tile bertemu. Test pembauran dicek dengan merusak pembauran kolom dan baris: keduanya tertangkap.
- **Dicek:** `dotnet build Condec.sln` 0 warning 0 error; `dotnet test --solution Condec.sln` 1393 lulus, 0 gagal. Uji langsung di build portabel (GPU terintegrasi): 8x07ex.jpg 150 × 150 → 300 × 300, lolos cek integritas; dibanding hasil gambar utuh 39,8 dB (sebelumnya 36,6 dB), dan lompatan warna di garis potong lama 0,92× lompatan di sebelahnya (sebelumnya 1,57×; gambar utuh 1,01×), jadi garisnya hilang. Pembagian tepi dan tumpang tindih dipilih dari pengukuran 4 ukuran tile × 4 pembagian × 3 gambar; tepi 4 + tumpang 12 terbaik rata-rata di setiap ukuran tile. Teks Batas memori yang baru dicek langsung di Settings (EN, tema gelap): terbungkus dua baris, tidak terpotong; build dan test diulang, 1393 lulus. **Belum dicoba langsung:** CPU, tile kecil di aplikasi (tidak tercapai di mesin uji), gambar besar, teks ID di Windows berbahasa Indonesia.

### [0.2.12] 2026-10-03 (banyak gambar: Upscale Image)
- **Keputusan pemilik (2026-10-03, lanjutan [0.2.9]):** Upscale Image menerima banyak gambar yang ditinjau satu per satu dalam antrean, lalu disimpan ke folder dengan nama otomatis tanpa menimpa. Mengubah §6.2 yang sebelumnya hanya menerima satu gambar ("Seret satu gambar saja.").
- **Ditambah:** antrean Upscale (§6.2 "Banyak gambar"): pilih dan seret banyak gambar, "Tambah gambar…", kartu antrean, skala dan resolusi per gambar, alasan bila gambar tidak bisa di-upscale, batas 20 gambar, proses satu per satu dengan "Gambar n dari N", Batal, layar Selesai dengan daftar hasil, "Buka folder", "Coba lagi gambar yang gagal" / "Upscale sisanya", dan Riwayat per gambar dengan skalanya.
- **Ditambah (Core):** `OutputNames.PlanNamed` (aturan nama batch untuk nama yang dibuat di luar `OutputNames`) dan `UpscalePlan.ResultName` ("foto-2560x1440.png", dipakai satu gambar dan antrean). 2 test baru.
- **Diubah:** teks area seret "Seret satu atau beberapa gambar ke sini"; label tombol "Upscale dan simpan N gambar…" untuk antrean.
- **Dihapus:** kunci `Upscale.DropMany` ("Seret satu gambar saja…").
- **Diajukan:** §13 #53 sampai #55; §13 #49 diperluas ke Upscale.
- **Dicek:** `dotnet build Condec.sln` 0 warning 0 error; `dotnet test --solution Condec.sln` 1379 lulus, 0 gagal. Audit key resource: 0 tidak terpakai, kunci ID dan EN sama. Uji langsung di build portabel (GPU terintegrasi, batas perangkat 2×): 3 gambar + TXT (TXT tidak diambil dengan alasan) → antrean 3 item; skala 150% di gambar 1 tetap saat pindah ke gambar 2 (2×) dan kembali; memilih item dari daftar; "Tambah gambar…" dengan satu.png dari subfolder (nama sama) dan satu.png yang sudah ada (duplikat diabaikan) → 4 item; batch ke folder yang sudah berisi `dua-400x400.png` → `satu-360x240.png`, `dua-400x400 (2).png` (file lama utuh), `tiga-320x480.png`, `satu-480x320.png`, ukuran piksel cocok, Riwayat mencatat skala 1.5 dan 2 per gambar, pengukuran mesin sekali sebelum gambar pertama; gambar 2000 × 1500 (terlalu besar untuk perangkat 8 GB) → "Tidak bisa di-upscale", alasan di Caption, tombol nonaktif, dihapus → aktif dan kartu antrean hilang; satu gambar → `FileSavePicker` dengan nama `satu-480x320` seperti sebelumnya; batch 3 gambar dibatalkan di gambar 2 → "Upscale dihentikan", 1 dari 3, tanpa file sisa → "Upscale sisanya" menyimpan dua sisanya; satu sumber dihapus sebelum batch → "Sebagian gambar gagal" dengan alasan → dikembalikan → "Coba lagi gambar yang gagal" menyimpannya. **Belum dicoba langsung:** seret-lepas banyak gambar, jalur Tab di kartu antrean, gambar yang terhalang batas memori (bukan RAM), Windows berbahasa Indonesia, tema kontras tinggi, paket MSIX terpasang.

### [0.2.11] 2026-10-03 (banyak file: Architecture dari CAD)
- **Keputusan pemilik (2026-10-03, lanjutan [0.2.9]):** banyak file sejenis bisa dipilih sekaligus, hasil ke-n milik file ke-n, dan Architecture memakai antrean yang ditinjau satu per satu. Mengubah §6.3.2 dan §6.3.3 yang sebelumnya menerima satu file DWG/DXF.
- **Ditambah:** antrean "dari CAD" (§6.3.4): pilih dan seret banyak DWG/DXF, "Tambah file…", kartu antrean, pembacaan di latar belakang satu per satu dengan "Hentikan", kartu item yang gagal dibaca, layer dan pratinjau per item, opsi hasil bersama, batas 20 file, dan penyimpanan batch ke folder dengan Selesai, coba lagi, dan Riwayat.
- **Diubah:** file DWG/DXF yang tidak bisa dibaca atau kosong menjadi item gagal, bukan kembali ke area seret. Teks area seret "Seret satu atau beberapa file DWG atau DXF ke sini"; tombol "Pilih file…" memakai `Input.ChooseMany`. Label tombol Konversi "Konversi dan simpan N file…" untuk banyak gambar.
- **Dihapus:** kunci resource yang tidak terpakai lagi `Input.Choose` dan `Input.DropMany` ("Seret satu file saja…", sudah tidak benar).
- **Diajukan:** §13 #50 sampai #52.
- **Dicek:** `dotnet build Condec.sln` 0 warning 0 error; `dotnet test --solution Condec.sln` 1375 lulus, 0 gagal. Audit key resource: 0 tidak terpakai, kunci ID dan EN sama. Uji langsung di build portabel: DWG + DXF + PNG (PNG tidak diambil dengan alasan) → antrean 2 item; layer yang disembunyikan di satu item tetap saat pindah item dan tidak memengaruhi item lain; semua layer satu item disembunyikan → tombol nonaktif dengan alasan; ukuran kertas A4 → pratinjau item lain digambar ulang saat ditampilkan; 2 item → PNG ke folder (bcc.png, geek.png, format "DWG, DXF → PNG"); satu DXF → tanpa kartu antrean, `FileSavePicker` dengan nama geek.png, "DXF → PNG"; DXF rusak + DXF baik → item gagal dengan kartunya, "Hapus dari antrean" menyisakan satu gambar; satu DXF rusak saja → kartu gagal → "Hapus dari antrean" kembali ke area seret. "Hentikan" tidak tertangkap otomasi (5 file terbaca dalam 2 detik); kodenya sama dengan arah "ke CAD" yang sudah dicoba langsung di [0.2.10]. **Belum dicoba langsung:** "Hentikan" di arah ini, batas 20 file, seret-lepas banyak file (§13 #49), tema kontras tinggi, paket MSIX terpasang.

### [0.2.10] 2026-10-03 (banyak file dan banyak halaman: Architecture ke CAD)
- **Keputusan pemilik (2026-10-03, lanjutan [0.2.9]):** Architecture ke CAD ditinjau satu per satu dalam antrean; hasil PDF banyak halaman dipilih pengguna (file per halaman atau satu gambar CAD gabungan); batas dibuat nyata. Mengubah §6.3.3 yang sebelumnya hanya mengonversi halaman pertama PDF dan menerima satu file.
- **Ditambah:** antrean "ke CAD" (§6.3.4): pilih dan seret banyak file sejenis, "Tambah file…", satu item per halaman PDF dengan pilihan halaman, kartu antrean (daftar item dengan status, sebelumnya, berikutnya, hapus dari antrean), pembacaan di latar belakang satu per satu, "Hentikan" yang menyimpan item terbaca, kartu item yang gagal dibaca, tinjauan dan upscale per item, batas 20 item, pilihan "Satu file per halaman" atau "Satu gambar CAD, halaman berdampingan", dan penyimpanan batch ke folder dengan layar Selesai, coba lagi, dan Riwayat yang sama dengan Convert File.
- **Ditambah (Core):** `WindowsPictureReader.ReadAsync` dan `ArchitectureFiles.RenderPdfAsync` membaca halaman PDF yang dipilih, bukan selalu halaman pertama. Daftar hasil batch (`BatchResultList`) dipakai bersama Convert File dan Architecture.
- **Diubah:** teks area seret "Seret satu atau beberapa gambar, PDF, atau DXF ke sini" dan tombol "Pilih file…" (`Input.ChooseMany`).
- **Dihapus:** catatan "Hanya halaman pertama yang dikonversi" (`Architecture.Pdf.FirstPageOnly`).
- **Diajukan:** §13 #45 sampai #49; §13 #34 diperbarui.
- **Dicek:** `dotnet build Condec.sln` 0 warning 0 error; `dotnet test --solution Condec.sln` 1375 lulus, 0 gagal; 2 test baru membaca halaman 2 PDF lewat pembaca gambar dan pratinjau. Audit key resource: 0 tidak terpakai, ID dan EN sama. Uji langsung di build portabel: PDF vektor 4 halaman → 4 DWG (`gambarkerja-1` sampai `-4`) dan → satu DWG dengan 4 halaman berdampingan (dibuka lagi di "dari CAD"); PNG + PNG + PDF (PDF ditolak "jenisnya berbeda") → 2 DWG, lalu ke folder yang sama → "geek (2).dwg" dan "blurry (2).dwg"; item dihapus dari antrean; 2 DXF → 2 DWG; PDF scan 20 halaman dibaca di latar belakang dan "Hentikan" di tengah (3 item tersisa); 6 + 20 halaman → batas 20 item dan tombol nonaktif, lalu "1-3" → 6 item; gabungan halaman scan dari 2 PDF → 2 DWG; upscale 2× di item kedua (antrean terkunci selama upscale, hasil tetap setelah pindah item); satu DXF dihapus sebelum konversi → "Sebagian file gagal" → dikembalikan → "Coba lagi file yang gagal" menyimpan b.dwg. **Belum dicoba langsung:** §13 #49, tema kontras tinggi, paket MSIX terpasang.

### [0.2.9] 2026-10-03 (banyak file dan banyak halaman: Convert File)
- **Keputusan pemilik (2026-10-03, percakapan):** PDF ke DWG/DXF belum bisa banyak halaman; semua yang bisa harus mendukung banyak halaman, dan banyak file sejenis bisa dipilih sekaligus dengan hasil ke-n milik file ke-n. Jawaban pemilik: (1) hasil PDF banyak halaman dipilih pengguna (satu file per halaman, atau satu gambar CAD gabungan); (2) satu kelompok jenis per batch; (3) Architecture ke CAD dan Upscale Image: ditinjau satu per satu dalam antrean; (4) batch disimpan ke folder yang dipilih dengan nama otomatis, nama terpakai mendapat " (2)", tidak pernah menimpa. Ini mengubah §6.1 yang sebelumnya hanya menerima satu file.
- **Ditambah:** Convert File menerima banyak file sejenis (§6.1.3): dialog pilih banyak, seret banyak file, "Tambah file…", daftar file bernomor dengan hasil per file dan tombol hapus, tujuan yang bisa dicapai semua file, file yang sudah berformat tujuan dibiarkan.
- **Ditambah:** pilihan halaman untuk PDF dan TIFF ke gambar ("Semua halaman", "Halaman pertama saja", "Pilih halaman" dengan rentang seperti 1-3, 5), satu hasil per halaman. Konverter gambar membaca halaman TIFF yang dipilih (§6.1.1).
- **Ditambah:** penyimpanan batch ke folder dengan nama otomatis (`OutputNames`), proses satu per satu (`ConversionBatch`) yang lanjut melewati file gagal, layar Selesai dengan hasil tiap file, "Buka folder", "Coba lagi file yang gagal" / "Konversi sisanya", dan pembatalan di tengah batch. Satu entri Riwayat per hasil.
- **Ditambah (Core, untuk tahap Architecture berikutnya):** `CombinedCadOptions` dan `CombinedCadBuilder`: beberapa halaman PDF vektor atau hasil analisis gambar dijadikan satu gambar CAD, berjajar ke kanan dengan jarak 10% lebar, satuan dari bagian pertama. Belum dipakai UI; antrean Architecture dan Upscale menyusul di tahap berikutnya, §6.2 dan §6.3 belum berubah.
- **Diganti:** `ComboBox` "Halaman" lama untuk PDF → gambar (satu halaman, `Cad.PageHeader`, belum tercatat di dokumen ini) diganti pilihan halaman di atas. Kunci `Input.DropHere` diganti `Input.DropHereMany` dengan teks yang sama.
- **Diajukan:** §13 #39 sampai #44.
- **Dicek:** `dotnet build Condec.sln` 0 warning 0 error; `dotnet test --solution Condec.sln` 1373 lulus, 0 gagal. Audit key resource: 0 tidak terpakai, kunci ID dan EN sama. Uji langsung di build portabel: 3 gambar (PNG, JPG, TIFF 3 halaman) → JPG dengan nama "foto (2).jpg" karena foto.jpg sudah ada (tidak ditimpa) dan liburan.jpg dibiarkan; PDF 4 halaman → PNG dengan "Semua halaman" (4 file), teks salah, rentang di luar halaman, dan "2-3" (gambarkerja-2.png dan -3.png); satu sumber dihapus sebelum konversi → "Sebagian file gagal" dengan alasannya → dikembalikan → "Coba lagi file yang gagal" menyimpan hapus.bmp; 4 PDF → DOCX dibatalkan sebelum file pertama selesai (kembali ke Input, tanpa file sisa) dan setelah file pertama ("Konversi dihentikan", 1 dari 4) lalu sisanya dikonversi; 2 video → MP4 dengan satu video terpotong ("Sebagian file gagal", `Error.Media`); 6 video, satu dihapus dari daftar, → WMV; "Tambah file…" dengan PDF dan duplikat (PDF ditolak dengan alasan, duplikat diabaikan); satu file tetap memakai dialog simpan. **Belum dicoba langsung:** seret-lepas banyak file, jalur Tab (§13 #44), Windows berbahasa Indonesia, tema kontras tinggi, paket MSIX terpasang.

### [0.2.8] 2026-10-03 (tahap 7: halaman Architecture)
- **Ditambah:** halaman Architecture berfungsi penuh (§6.3, §6.3.3): `SelectorBar` dua arah. "Ke CAD": area seret, pembacaan gambar di perangkat (garis dinding, pintu dan jendela, teks lewat OCR Windows, logo, tabel), area tidak jelas, saran dan jalankan upscale langsung di halaman, daftar objek yang bisa dicentang, kalibrasi skala (dialog dengan jalur keyboard), keluaran DWG atau DXF dengan layer WALLS, OPENINGS, TEXT, LOGO, TABLE. "Dari CAD": pratinjau yang digambar sendiri, opsi hasil (PDF, PNG, JPG; A4 atau A3; 150, 300, 600 DPI; latar putih atau transparan), layer, perkiraan ukuran.
- **Diubah:** Convert File tidak lagi menawarkan DXF/DWG maupun opsi CAD; kartu konverter hanya untuk format bukan CAD dan menautkan ke Architecture. Konverter gambar → CAD lama (garis tepi tertutup lewat Otsu dan marching squares) diganti analisis gambar baru; `ImageToCadConverter`, `WicImageRasterizer`, dan test-nya dihapus.
- **Diubah:** `ClarityAnalyzer` membagi area buram jadi blok paling besar 6 × 6 petak, supaya gambar yang buram seluruhnya tidak dianggap satu area dan lolos dari ambang 5 area. Pratinjau menandainya dengan garis tepi saja.
- **Diperbaiki:** upscale dari halaman Architecture gagal dengan `DirectoryNotFoundException` karena folder `cache\architecture` belum ada; folder dibuat saat dibutuhkan.
- **Diajukan:** §13 #31 sampai #38; §13 #9, #11, #13 diperbarui.
- **Dicek:** `dotnet build Condec.sln` 0 warning 0 error; `dotnet test --solution Condec.sln` 1269 lulus, 0 gagal. Uji langsung di build portabel: PNG (geek), DXF (geek), PDF (gambar kerja), DWG (bcc) ke dan dari CAD; gambar buram sintetis → saran upscale → Upscale 2× 44 detik di GPU terintegrasi → banner "Setelah upscale 2×, 100% objek terbaca jelas" → konversi DWG lolos verifikasi chunk dan integritas; entri muncul di Riwayat. Audit key resource: 431 key, 0 tidak terpakai. **Belum dicoba langsung:** tema kontras tinggi, Windows berbahasa Indonesia, paket MSIX terpasang, seret-lepas file sungguhan, dialog kalibrasi lewat keyboard, perangkat ber-NPU atau GPU diskrit, upscale di atas 2×, denah arsitektur nyata (heuristik pintu, jendela, tabel).

### [0.2.7] 2026-10-03 (batas memori nyata, Upscale masuk Riwayat)
- **Diubah (keputusan pemilik):** batas memori di Settings kini nyata (§7.5, §6.4): ukuran tile (128, 96, 64, 48) mengikuti batas, gambar yang tidak muat tidak dimulai (§6.2). Faktor waktu ×1,3/×1,8 dibuang; throughput diukur per ukuran tile (§8). §13 #26 selesai.
- **Diubah:** arena memori CPU ONNX Runtime dimatikan, buffer hasil tile dipakai ulang, dan hasil jaringan dilepas sebelum gambar disimpan: memori puncak render CPU tile 128 turun dari ±790 MB ke ±300 MB, tanpa jadi lebih lambat.
- **Diubah (keputusan pemilik):** hasil Upscale Image masuk ke Riwayat yang sama dengan konversi, dengan skalanya (§6.1, §6.2, §13 #10).
- **Dicek:** uji langsung di build portabel: benchmark GPU tersimpan sebagai `bench.Gpu.128`, entri Riwayat "PNG → PNG · Upscale 2× · Just now" di Convert File, perkiraan waktu ±15 detik dari ukuran tile yang diukur. Memori diukur dengan proses penuh pada 9 kombinasi mesin, ukuran gambar, dan ukuran tile (§13 #26); test memastikan perkiraan tidak di bawah hasil ukur. **Belum dicoba langsung:** tampilan "Batas memori kurang" (tidak tercapai di mesin uji, ada unit test) dan tile kecil di UI.

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
