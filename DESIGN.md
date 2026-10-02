# Condec — DESIGN.md

Versi dokumen 0.2.1 · diubah terakhir 2026-10-02 · target: WinUI 3 (Windows App SDK) di Windows 11

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
- Ukuran awal 1280 × 820. Ukuran minimum `[TERBUKA]` (usulan 900 × 640).
- `MicaBackdrop`. Title bar kustom: tinggi 32, ikon aplikasi 16, judul "Condec" (CaptionTextBlockStyle). Tombol minimize/maximize/close digambar sistem (46 × 32).
- Pakai `TitleBar` (tersedia di Windows App SDK versi baru; verifikasi di dokumentasi versi yang terpasang), atau `ExtendsContentIntoTitleBar` + `SetTitleBar`.

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
| Ikon item | `FontIcon` 16, glyph dari Segoe Fluent Icons. Pilih dari tabel resmi, jangan menebak codepoint. |
| Isi | `Frame`, navigasi dari `ItemInvoked`/`SelectionChanged` |

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
- Kanan (padding 24): label "Ubah ke format", `ComboBox` format, teks bantu (Caption), `HyperlinkButton` "Butuh DXF atau DWG? Buka Architecture" (pindah ke halaman Architecture).
  - Belum ada file: ComboBox nonaktif, teks "Pilih file dulu", bantu "Format muncul setelah file dipilih".
  - Sudah ada file: placeholder "Pilih format", bantu "N format tersedia untuk .ext". Isi daftar diambil dari `ConverterRegistry`, bukan daftar tetap. Format yang tidak didukung mesin tidak muncul.
- Bawah kartu: kiri teks Caption dengan ikon perisai "Diproses sepenuhnya di perangkat ini. Tidak ada file yang diunggah."; kanan tombol aksen "Konversi dan simpan…", **nonaktif sampai file dan format dipilih**.
- Klik tombol → `FileSavePicker`, lalu keadaan Proses.

**Keadaan Proses**
- Judul `SubtitleTextBlockStyle`: "Mengonversi `<nama file>`". Caption: "Ke `<FORMAT>` · disimpan sebagai `<path>`". Tombol "Batal" di kanan.
- `ProgressBar` + persen (rata kanan, lebar 40, angka tabular).
- Empat tahap, tinggi baris 40, ikon 20 (selesai = lingkaran aksen berisi centang, aktif = `ProgressRing`, menunggu = lingkaran kosong). Nama tahap tebal saat aktif, teks sekunder saat menunggu:

| Tahap | Rentang progres | Keterangan kanan |
|---|---|---|
| Mendekode file sumber | 0–35% | "Membaca `<nama>`" → "Selesai" |
| Menulis ke format `<FORMAT>` | 35–70% | "Encode ke `.ext`" → "Selesai" |
| Verifikasi chunk | 70–90% | "Chunk n dari N" → "N chunk cocok" |
| Cek integritas file | 90–100% | "Menghitung SHA-256" → "Hash cocok" |

- Batal bisa di setiap tahap. File sementara dihapus.

**Keadaan Selesai**
- `InfoBar` Success: judul "Konversi selesai", pesan "File berhasil disimpan dan lolos verifikasi chunk serta cek integritas."
- Tiga baris detail (label lebar 96, Caption sekunder): Lokasi (path penuh), Format ("DOCX → PDF"), Integritas ("SHA-256 cocok · N chunk terverifikasi").
- Tombol: "Buka file" (aksen), "Tampilkan di folder", `HyperlinkButton` "Konversi file lain" (kembali ke Input, kosong).

**Riwayat**
- Header: "Riwayat" (BodyStrong), `HyperlinkButton` "Hapus riwayat".
- `ListView` (`SelectionMode=None`): kotak ikon 32, nama, Caption "ASAL → TUJUAN · waktu", status "Terverifikasi" (Caption, warna sukses, ikon centang 12), tombol ikon folder (`AutomationProperties.Name="Tampilkan di folder"`).
- Kosong: "Belum ada riwayat konversi."
- Data: `%LOCALAPPDATA%\Condec\history.json` (nama, asal → tujuan, waktu, path, status verifikasi). Tanpa salinan isi file. Konversi baru masuk paling atas.

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

**Kartu kiri**
- Baris info: ikon gambar 32, nama (BodyStrong), Caption "W × H · X,X MP · ukuran file", `HyperlinkButton` "Ganti".
- Diagram ukuran proporsional (rasio 16:9 mengikuti gambar): kotak besar bergaris putus-putus = hasil, kotak kecil di pojok kiri atas = asli, lebarnya 100/skala persen. Legenda: "Asli · W × H" dan "Hasil · W × H".

**Kartu kanan, berurutan**
1. **Mesin render:** Caption "Mesin render", nilai BodyStrong ("GPU · `<nama>`", "CPU · `<nama>`", atau "NPU · `<nama>`") dari Settings, `HyperlinkButton` "Ubah di Settings".
2. **Skala:** label "Skala", `ComboBox` dengan item 2×, 4×, 8×, 16× (keterangan kanan "200%", "400%", dst.). Item di atas batas efektif (§7) nonaktif dengan keterangan "Dikunci". Kalau skala berasal dari slider dan bukan preset, ComboBox menampilkan "Kustom 3,5×" lewat `PlaceholderText`.
3. **Persentase upscale:** label dan nilai ("400%", BodyStrong). `Slider` Minimum=150, Maximum = batas efektif × 100, StepFrequency=50, TickFrequency=50, tick di bawah. Di bawah slider: Caption "150%" (kiri) dan nilai maksimum (kanan). Slider dan ComboBox Skala saling menyinkronkan.
4. Bila ada skala terkunci: Caption berwarna peringatan dengan ikon gembok "Dikunci: 8×, 16×. `<alasan>`." (alasan dari §7.4).
5. **Resolusi hasil:** `ComboBox`, tampilan "Full HD · 1920 × 1080" atau "Kustom · W × H". Preset: Full HD 1920 × 1080, 2K (QHD) 2560 × 1440, 4K (UHD) 3840 × 2160, 5K 5120 × 2880, 8K (UHD) 7680 × 4320 (untuk sumber 16:9). Item nonaktif bila skala yang dibutuhkan (lebar target ÷ lebar sumber) < 1,5 atau di atas batas efektif.
6. **Format hasil:** `RadioButtons` PNG | JPG (horizontal).

**Kartu perkiraan** (grid 4 kolom, nilai `BodyLargeStrongTextBlockStyle`, label dan keterangan Caption):
- "Resolusi hasil" — "5120 × 2880" / "14,7 MP · 5K"
- "Perkiraan ukuran file" — "± 23,9 MB" / "PNG"
- "Perkiraan waktu render" — "± 7 detik" / "GPU · RTX 4060 Laptop"
- "RAM dibutuhkan" — "32 GB" / "Terpasang 32 GB, cukup" (warna sukses) atau "Terpasang N GB, kurang" (warna galat)
- Tombol aksen "Upscale dan simpan…" di kanan, **nonaktif** bila upscale tidak tersedia atau RAM kurang.

**Upscale tidak tersedia** (batas efektif < 1,5): `InfoBar` Error di atas kartu. Judul "Perangkat ini belum memenuhi syarat upscale". Pesan "Upscale butuh RAM terpasang minimal 8 GB untuk hasil sampai 2K. Perangkat ini hanya punya N GB." Skala, slider, dan resolusi nonaktif.

**Proses:** kartu tunggal. Judul "Meng-upscale `<nama>`", Caption "`<skala>` · `<W × H>` · `<mesin>`", "Batal", ProgressBar + persen, tahap: "Menyiapkan mesin render" (0–10%), "Memproses tile" (10–90%, "Tile n dari N"), "Menyimpan hasil" (90–96%, "Menulis .png"), "Verifikasi integritas" (96–100%, "Menghitung SHA-256"). Ukuran tile input 256 px `[ASUMSI]`.

**Selesai:** `InfoBar` Success "Upscale selesai" / "Gambar disimpan dan lolos cek integritas." Detail: Lokasi, Resolusi ("W × H → W' × H' (4×)"), Mesin. Tombol: "Buka file" (aksen), "Tampilkan di folder", `HyperlinkButton` "Upscale gambar lain".

Nama file hasil: `<nama>-<W>x<H>.<ext>`.

### 6.3 Architecture

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
| Render dump | `Button` "Bersihkan cache" | Deskripsi: "Hapus cache render (tile dan data sementara). Ukuran sekarang: 1,8 GB." Setelah dibersihkan: "Cache kosong. Ukuran sekarang: 0 MB." dan tombol nonaktif. |

#### Tab "Umum"

| Grup / baris | Kontrol | Isi |
|---|---|---|
| Tampilan → Tema aplikasi | `ComboBox` | Terang, Gelap, Ikuti sistem. Diterapkan lewat `RequestedTheme` di elemen root ("Ikuti sistem" = `ElementTheme.Default`). |
| Tampilan → Latar Mica | `ToggleSwitch` | "Latar jendela ikut warna wallpaper (Windows 11)." Label "Aktif"/"Nonaktif". |
| Log → Simpan log aktivitas | `ToggleSwitch` | "Catatan proses konversi dan upscale untuk mencari masalah. Tidak berisi isi file dan tidak pernah dikirim ke mana pun." |
| Log → Tingkat detail log | `ComboBox` | Error, Info, Debug. Nonaktif saat log mati. Deskripsi: "Debug mencatat paling banyak dan bisa membuat file log besar." |
| Log → Folder log | `Button` "Buka folder", `Button` "Hapus log" | `%LOCALAPPDATA%\Condec\logs`. Buka folder lewat `Launcher.LaunchFolderAsync`. |
| Tentang | `Button` "Lisensi pihak ketiga" | "Condec 0.1.0" / "Open source, GPL-3.0-or-later. Semua proses berjalan offline di perangkat ini." |

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

## 8. Estimasi

| Besaran | Rumus | Tag |
|---|---|---|
| Piksel hasil | `round(W × skala) × round(H × skala)` | |
| Ukuran file PNG | piksel × 1,7 byte | `[ASUMSI]` |
| Ukuran file JPG | piksel × 0,4 byte | `[ASUMSI]` |
| Waktu render | `MP hasil ÷ throughput(mesin) × faktorMemori` | |
| Throughput | hasil benchmark singkat pada eksekusi pertama (tile kecil per mesin), disimpan per mesin dan nama perangkat; ganti GPU atau NPU berarti diukur ulang. Protokol: 1 tile pemanasan tanpa hitung waktu, lalu 3 tile diukur | protokol `[ASUMSI]` |
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

- Semua tombol ikon punya `AutomationProperties.Name` ("Tampilkan di folder", "Buka atau tutup menu navigasi", dll.).
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
| 6 | Ukuran minimum jendela (usulan 900 × 640) | `[TERBUKA]` |
| 7 | Versi DWG yang ditulis ACadSharp | diputuskan: AC1015 (AutoCAD 2000). ACadSharp 3.8.0 menulis DWG AC1014, AC1015, AC1018, AC1024, AC1027, AC1032 |
| 8 | Model upscale (lisensi kode dan bobot harus kompatibel GPL-3.0) dan apakah bisa jalan di NPU | `[TERBUKA]`, jangan menebak |
| 9 | OCR teks gambar: `Windows.Media.Ocr`, bahasa terpasang | `[TERBUKA]`, cek dokumentasi |
| 10 | Apakah riwayat Upscale dan Architecture masuk ke satu daftar Riwayat | `[TERBUKA]`, saat ini hanya Convert File |
| 11 | Ambang "banyak objek tidak jelas" (5 area) | `[ASUMSI]`, kalibrasi |
| 12 | Perkiraan ukuran file (§8) dan hasil DWG → gambar (§6.3.2) | `[ASUMSI]` |
| 13 | Win2D untuk render DWG | `[TERBUKA]`, hanya kalau kontrol stock tidak cukup |
| 16 | Tanpa GPU terdeteksi dihitung seperti GPU terintegrasi (batas 2×) | `[ASUMSI]` |
| 17 | Protokol benchmark: 1 tile pemanasan + 3 tile diukur, disimpan per mesin dan nama perangkat | `[ASUMSI]` |
| 18 | DXCore (GPU/NPU) belum terbukti di perangkat nyata; hanya dites di runner CI tanpa GPU | `[TERBUKA]`, cek di perangkat pemilik lewat Settings |
| 14 | LibreOffice tetap dibundel di paket x64 (keputusan pemilik 2026-09-24, dikonfirmasi 2026-10-02) | diputuskan |
| 15 | HEIC tetap boleh jadi format tujuan bila codec HEVC terpasang | diputuskan |

## 14. Changelog

Format entri: `[versi] tanggal — Ditambah / Diubah / Dihapus`. Entri baru ditaruh paling atas.

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
